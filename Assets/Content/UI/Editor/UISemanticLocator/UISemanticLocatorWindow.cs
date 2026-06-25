using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

public class UISemanticLocatorWindow : EditorWindow
{
    private const int CacheVersion = 2;
    private const int MaxResults = 80;
    private const float PreviewWidth = 128f;
    private const float PreviewHeight = 80f;
    private const float EstimatedResultHeight = 116f;
    private const int PreviewRenderSize = 256;
    private const int PreviewWarmupResultCount = 8;
    private const int PreviewVisibleResultCount = 14;
    private const string MenuPath = "Tools/UI/Semantic UI Locator/Open";
    private const string SmokeTestMenuPath = "Tools/UI/Semantic UI Locator/Smoke Test - Hero Journey";
    private const string HardTrainingSmokeTestMenuPath = "Tools/UI/Semantic UI Locator/Smoke Test - Hard Training";
    private const string PrefabRoot = "Assets/Content/UI/Prefab";
    private const string CacheRelativePath = "Library/Dragon/UISemanticLocator/index.json";

    private static readonly Regex PanelMappingRegex = new Regex("\\{[\\s\\S]*?\"id\"\\s*:\\s*\"([^\"]+)\"[\\s\\S]*?\"prefab\"\\s*:\\s*\"([^\"]+\\.prefab)\"[\\s\\S]*?\\}", RegexOptions.Compiled);
    private static readonly Regex ActEntryStartRegex = new Regex("^\\s{6}\\d+\\s*:\\s*\\{\\s*$", RegexOptions.Compiled);
    private static readonly Regex ActEntryEndRegex = new Regex("^\\s{6}\\},?\\s*$", RegexOptions.Compiled);
    private static readonly Regex ActShowTabLineRegex = new Regex("\"show_tab\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.Compiled);
    private static readonly Regex ActShowNameLineRegex = new Regex("\"show_(?:tab_)?name\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.Compiled);
    private static readonly Regex UiCreateRegex = new Regex("(?:game_mgr\\.ui_mgr\\.)?(?:AbbrCreate|Create)\\(\\s*[\"']([^\"']+)[\"']", RegexOptions.Compiled);
    private static readonly Regex CommentAliasRegex = new Regex("^\\s*([A-Za-z_][A-Za-z0-9_]*)\\s*=\\s*[^#\\r\\n]+#\\s*(.+)$", RegexOptions.Compiled);
    private static readonly Regex IdentifierRegex = new Regex("[A-Za-z_][A-Za-z0-9_]*", RegexOptions.Compiled);
    private static readonly Regex WordSplitRegex = new Regex("[^\\p{L}\\p{Nd}]+", RegexOptions.Compiled);

    private string searchText = "英雄之旅";
    private string statusText = string.Empty;
    private Vector2 scrollPosition;
    private IndexCache indexCache;
    private List<SearchResult> results = new List<SearchResult>();
    private readonly Dictionary<string, Texture2D> prefabPreviewCache = new Dictionary<string, Texture2D>();
    private readonly Dictionary<string, string> prefabPreviewErrors = new Dictionary<string, string>();
    private readonly Queue<string> prefabPreviewQueue = new Queue<string>();
    private readonly HashSet<string> prefabPreviewQueued = new HashSet<string>();
    private bool isPreviewGenerationScheduled;
    private static bool prefabPreviewMethodResolved;
    private static MethodInfo prefabPreviewGenerateMethod;

    [MenuItem(MenuPath, false, 2310)]
    public static void Open()
    {
        UISemanticLocatorWindow window = GetWindow<UISemanticLocatorWindow>("UI Semantic Locator");
        window.minSize = new Vector2(980f, 520f);
        window.Show();
    }

    [MenuItem(SmokeTestMenuPath, false, 2311)]
    public static void SmokeTestHeroJourney()
    {
        IndexBuilder builder = new IndexBuilder(GetProjectRoot());
        IndexCache cache = builder.Build();
        QueryPlan queryPlan = QueryPlan.Build("英雄之旅", cache.Aliases);

        List<SearchResult> smokeResults = cache.Entries
            .Select(entry => ScoreEntry(entry, queryPlan))
            .Where(result => result.Score > 0f)
            .OrderByDescending(result => result.Score)
            .ThenBy(result => string.IsNullOrEmpty(result.Entry.Route) ? 1 : 0)
            .ThenBy(result => result.Entry.PrefabPath)
            .Take(10)
            .ToList();

        SearchResult expected = smokeResults.FirstOrDefault(result =>
            result.Entry.Route == "season_all_common.event_subscribe"
            && result.Entry.PrefabPath == "Assets/Content/UI/Prefab/season_all/a_event_subscribe.prefab");

        if (expected != null)
        {
            string message = "Smoke test passed.\n\n"
                             + "Query: 英雄之旅\n"
                             + "Route: " + expected.Entry.Route + "\n"
                             + "Prefab: " + expected.Entry.PrefabPath + "\n"
                             + "Score: " + expected.Score.ToString("0");
            Debug.Log("[UI Semantic Locator] " + message);
            EditorUtility.DisplayDialog("UI Semantic Locator", message, "OK");
        }
        else
        {
            string top = smokeResults.Count > 0
                ? smokeResults[0].Entry.Route + " -> " + smokeResults[0].Entry.PrefabPath
                : "(no result)";
            string message = "Smoke test failed.\n\nExpected season_all_common.event_subscribe -> Assets/Content/UI/Prefab/season_all/a_event_subscribe.prefab\nTop result: " + top;
            Debug.LogError("[UI Semantic Locator] " + message);
            EditorUtility.DisplayDialog("UI Semantic Locator", message, "OK");
        }
    }

    [MenuItem(HardTrainingSmokeTestMenuPath, false, 2312)]
    public static void SmokeTestHardTraining()
    {
        IndexBuilder builder = new IndexBuilder(GetProjectRoot());
        IndexCache cache = builder.Build();
        QueryPlan queryPlan = QueryPlan.Build("千锤百炼", cache.Aliases);

        List<SearchResult> smokeResults = cache.Entries
            .Select(entry => ScoreEntry(entry, queryPlan))
            .Where(result => result.Score > 0f)
            .OrderByDescending(result => result.Score)
            .ThenBy(result => string.IsNullOrEmpty(result.Entry.Route) ? 1 : 0)
            .ThenBy(result => result.Entry.PrefabPath)
            .Take(20)
            .ToList();

        SearchResult expected = smokeResults.FirstOrDefault(result =>
            result.Entry.Route == "activity.week_score"
            && result.Entry.PrefabPath == "Assets/Content/UI/Prefab/event/a_event_week_score.prefab");

        SearchResult rewardPopup = smokeResults.FirstOrDefault(result =>
            result.Entry.Route == "common.multi_reward_window"
            && result.Entry.PrefabPath == "Assets/Content/UI/Prefab/common_window/multi_reward_window.prefab");

        if (expected != null && rewardPopup != null)
        {
            string message = "Smoke test passed.\n\n"
                             + "Query: 千锤百炼\n"
                             + "Main Route: " + expected.Entry.Route + "\n"
                             + "Main Prefab: " + expected.Entry.PrefabPath + "\n"
                             + "Related Popup: " + rewardPopup.Entry.Route + "\n"
                             + "Popup Prefab: " + rewardPopup.Entry.PrefabPath;
            Debug.Log("[UI Semantic Locator] " + message);
            EditorUtility.DisplayDialog("UI Semantic Locator", message, "OK");
        }
        else
        {
            string top = smokeResults.Count > 0
                ? smokeResults[0].Entry.Route + " -> " + smokeResults[0].Entry.PrefabPath
                : "(no result)";
            string message = "Smoke test failed.\n\nExpected activity.week_score and common.multi_reward_window for 千锤百炼.\nTop result: " + top;
            Debug.LogError("[UI Semantic Locator] " + message);
            EditorUtility.DisplayDialog("UI Semantic Locator", message, "OK");
        }
    }

    private void OnEnable()
    {
        LoadOrBuildIndex(false);
        Search();
    }

    private void OnDisable()
    {
        EditorApplication.delayCall -= ProcessNextPrefabPreview;
        isPreviewGenerationScheduled = false;

        foreach (Texture2D texture in prefabPreviewCache.Values)
        {
            if (texture != null)
            {
                DestroyImmediate(texture);
            }
        }

        prefabPreviewCache.Clear();
        prefabPreviewErrors.Clear();
        prefabPreviewQueue.Clear();
        prefabPreviewQueued.Clear();
    }

    private void OnGUI()
    {
        DrawToolbar();
        DrawStatus();
        DrawResults();
    }

    private void DrawToolbar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

        GUI.SetNextControlName("UISemanticLocator.Search");
        searchText = GUILayout.TextField(searchText, GUI.skin.FindStyle("ToolbarSeachTextField") ?? EditorStyles.toolbarTextField, GUILayout.MinWidth(260f));

        if (GUILayout.Button("Search", EditorStyles.toolbarButton, GUILayout.Width(72f)))
        {
            Search();
        }

        if (GUILayout.Button("Rebuild Index", EditorStyles.toolbarButton, GUILayout.Width(104f)))
        {
            LoadOrBuildIndex(true);
            Search();
        }

        if (GUILayout.Button("Ping First", EditorStyles.toolbarButton, GUILayout.Width(82f)))
        {
            if (results.Count > 0)
            {
                PingPrefab(results[0].Entry.PrefabPath);
            }
        }

        GUILayout.FlexibleSpace();

        int count = indexCache != null && indexCache.Entries != null ? indexCache.Entries.Count : 0;
        GUILayout.Label("Index: " + count + " entries", EditorStyles.miniLabel);

        EditorGUILayout.EndHorizontal();

        Event current = Event.current;
        if (current.type == EventType.KeyDown && current.keyCode == KeyCode.Return && GUI.GetNameOfFocusedControl() == "UISemanticLocator.Search")
        {
            Search();
            current.Use();
        }
    }

    private void DrawStatus()
    {
        if (!string.IsNullOrEmpty(statusText))
        {
            EditorGUILayout.HelpBox(statusText, MessageType.Info);
        }
    }

    private void DrawResults()
    {
        if (indexCache == null)
        {
            EditorGUILayout.HelpBox("Index is not ready. Click Rebuild Index.", MessageType.Warning);
            return;
        }

        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField("Results (" + results.Count + ")", EditorStyles.boldLabel);

        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

        if (results.Count == 0)
        {
            EditorGUILayout.HelpBox("No results. Try a route id, prefab filename, entry button name, or Chinese feature name.", MessageType.None);
        }

        for (int i = 0; i < results.Count; i++)
        {
            DrawResult(results[i], i);
        }

        EditorGUILayout.EndScrollView();
    }

    private void DrawResult(SearchResult result, int index)
    {
        IndexEntry entry = result.Entry;

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.BeginHorizontal();

        EditorGUILayout.BeginVertical(GUILayout.Width(58f));
        GUILayout.Label("#" + (index + 1), EditorStyles.boldLabel, GUILayout.Width(52f));
        GUILayout.Label("Score " + result.Score.ToString("0"), EditorStyles.miniBoldLabel, GUILayout.Width(58f));
        EditorGUILayout.EndVertical();

        Rect previewRect = GUILayoutUtility.GetRect(
            PreviewWidth,
            PreviewHeight,
            GUILayout.Width(PreviewWidth),
            GUILayout.Height(PreviewHeight));
        DrawPrefabPreview(previewRect, entry.PrefabPath, ShouldGeneratePreviewForResult(index));

        EditorGUILayout.BeginVertical();
        EditorGUILayout.BeginHorizontal();

        EditorGUILayout.BeginVertical();
        EditorGUILayout.LabelField(
            string.IsNullOrEmpty(entry.Route) ? "(prefab only)" : entry.Route,
            EditorStyles.boldLabel);
        EditorGUILayout.LabelField(entry.PrefabPath, EditorStyles.miniLabel);
        EditorGUILayout.EndVertical();

        if (GUILayout.Button("Ping", GUILayout.Width(54f)))
        {
            PingPrefab(entry.PrefabPath);
        }

        if (GUILayout.Button("Open", GUILayout.Width(54f)))
        {
            OpenPrefab(entry.PrefabPath);
        }

        if (GUILayout.Button("Copy", GUILayout.Width(54f)))
        {
            EditorGUIUtility.systemCopyBuffer = entry.PrefabPath;
            statusText = "Copied prefab path: " + entry.PrefabPath;
        }

        EditorGUILayout.EndHorizontal();

        if (result.Evidence.Count > 0)
        {
            EditorGUILayout.Space(2f);
            for (int i = 0; i < Mathf.Min(4, result.Evidence.Count); i++)
            {
                EditorGUILayout.LabelField("- " + result.Evidence[i], EditorStyles.wordWrappedMiniLabel);
            }
        }

        EditorGUILayout.EndVertical();
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.EndVertical();
    }

    private void DrawPrefabPreview(Rect rect, string prefabPath, bool shouldGenerateProjectPreview)
    {
        EditorGUI.DrawRect(rect, new Color(0.07f, 0.07f, 0.07f, 1f));

        Texture2D texture = GetPrefabPreview(prefabPath, shouldGenerateProjectPreview);
        if (texture != null)
        {
            Rect imageRect = new Rect(rect.x + 4f, rect.y + 4f, rect.width - 8f, rect.height - 8f);
            GUI.DrawTexture(imageRect, texture, ScaleMode.ScaleToFit, true);
        }
        else
        {
            EditorGUI.LabelField(rect, "Preview");
        }

        string tooltip = prefabPath;
        string error;
        if (!string.IsNullOrEmpty(prefabPath)
            && prefabPreviewErrors.TryGetValue(prefabPath, out error)
            && !string.IsNullOrEmpty(error))
        {
            tooltip += "\n" + error;
        }

        GUI.Box(rect, new GUIContent(string.Empty, tooltip));
    }

    private Texture2D GetPrefabPreview(string prefabPath, bool shouldGenerateProjectPreview)
    {
        if (string.IsNullOrEmpty(prefabPath))
        {
            return null;
        }

        Texture2D texture;
        if (prefabPreviewCache.TryGetValue(prefabPath, out texture) && texture != null)
        {
            return texture;
        }

        if (shouldGenerateProjectPreview)
        {
            RequestPrefabPreview(prefabPath);
        }

        return GetAssetPreviewFallback(prefabPath);
    }

    private Texture2D GetAssetPreviewFallback(string prefabPath)
    {
        UnityEngine.Object prefab = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(prefabPath);
        if (prefab == null)
        {
            return null;
        }

        Texture2D preview = AssetPreview.GetAssetPreview(prefab);
        if (preview != null)
        {
            return preview;
        }

        if (AssetPreview.IsLoadingAssetPreview(prefab.GetInstanceID()))
        {
            Repaint();
        }

        return AssetPreview.GetMiniThumbnail(prefab);
    }

    private bool ShouldGeneratePreviewForResult(int index)
    {
        if (index < PreviewWarmupResultCount)
        {
            return true;
        }

        int firstVisibleIndex = Mathf.Max(0, Mathf.FloorToInt(scrollPosition.y / EstimatedResultHeight) - 2);
        return index >= firstVisibleIndex && index < firstVisibleIndex + PreviewVisibleResultCount;
    }

    private void RequestPrefabPreview(string prefabPath)
    {
        if (prefabPreviewCache.ContainsKey(prefabPath)
            || prefabPreviewErrors.ContainsKey(prefabPath)
            || prefabPreviewQueued.Contains(prefabPath)
            || GetPrefabPreviewGenerateMethod() == null)
        {
            return;
        }

        prefabPreviewQueued.Add(prefabPath);
        prefabPreviewQueue.Enqueue(prefabPath);
        SchedulePrefabPreviewGeneration();
    }

    private void SchedulePrefabPreviewGeneration()
    {
        if (isPreviewGenerationScheduled)
        {
            return;
        }

        isPreviewGenerationScheduled = true;
        EditorApplication.delayCall += ProcessNextPrefabPreview;
    }

    private void ProcessNextPrefabPreview()
    {
        isPreviewGenerationScheduled = false;

        while (prefabPreviewQueue.Count > 0)
        {
            string prefabPath = prefabPreviewQueue.Dequeue();
            prefabPreviewQueued.Remove(prefabPath);

            if (prefabPreviewCache.ContainsKey(prefabPath) || prefabPreviewErrors.ContainsKey(prefabPath) || !IsCurrentResultPrefab(prefabPath))
            {
                continue;
            }

            GeneratePrefabPreview(prefabPath);
            break;
        }

        if (prefabPreviewQueue.Count > 0)
        {
            SchedulePrefabPreviewGeneration();
        }

        Repaint();
    }

    private bool IsCurrentResultPrefab(string prefabPath)
    {
        for (int i = 0; i < results.Count; i++)
        {
            if (results[i].Entry.PrefabPath == prefabPath)
            {
                return true;
            }
        }

        return false;
    }

    private void GeneratePrefabPreview(string prefabPath)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab == null)
        {
            prefabPreviewErrors[prefabPath] = "Prefab not found.";
            return;
        }

        Texture2D texture;
        string error;
        if (TryGenerateProjectPreview(prefab, out texture, out error) && texture != null)
        {
            prefabPreviewCache[prefabPath] = texture;
            prefabPreviewErrors.Remove(prefabPath);
            return;
        }

        if (!string.IsNullOrEmpty(error))
        {
            prefabPreviewErrors[prefabPath] = error;
        }
    }

    private static bool TryGenerateProjectPreview(GameObject prefab, out Texture2D texture, out string error)
    {
        texture = null;
        error = string.Empty;

        MethodInfo method = GetPrefabPreviewGenerateMethod();
        if (method == null)
        {
            return false;
        }

        object[] args = { prefab, PreviewRenderSize, string.Empty };
        try
        {
            texture = method.Invoke(null, args) as Texture2D;
            error = args[2] as string ?? string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            Exception actual = ex is TargetInvocationException && ex.InnerException != null ? ex.InnerException : ex;
            error = actual.Message;
            return true;
        }
    }

    private static MethodInfo GetPrefabPreviewGenerateMethod()
    {
        if (prefabPreviewMethodResolved)
        {
            return prefabPreviewGenerateMethod;
        }

        prefabPreviewMethodResolved = true;
        Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
        for (int i = 0; i < assemblies.Length; i++)
        {
            Type type = assemblies[i].GetType("UIPrefabPreviewGenerator");
            if (type == null)
            {
                continue;
            }

            prefabPreviewGenerateMethod = type.GetMethod(
                "Generate",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { typeof(GameObject), typeof(int), typeof(string).MakeByRefType() },
                null);
            if (prefabPreviewGenerateMethod != null)
            {
                break;
            }
        }

        return prefabPreviewGenerateMethod;
    }

    private void LoadOrBuildIndex(bool force)
    {
        string cachePath = GetCachePath();

        if (!force && File.Exists(cachePath))
        {
            try
            {
                string json = File.ReadAllText(cachePath, Encoding.UTF8);
                IndexCache loaded = JsonUtility.FromJson<IndexCache>(json);
                if (loaded != null && loaded.Version == CacheVersion && loaded.Entries != null && loaded.Entries.Count > 0)
                {
                    indexCache = loaded;
                    statusText = "Loaded semantic UI index from " + CacheRelativePath + ".";
                    return;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("Failed to load UI semantic locator cache: " + ex.Message);
            }
        }

        RebuildIndex();
    }

    private void RebuildIndex()
    {
        DateTime startedAt = DateTime.Now;
        IndexBuilder builder = new IndexBuilder(GetProjectRoot());
        indexCache = builder.Build();

        string cachePath = GetCachePath();
        Directory.CreateDirectory(Path.GetDirectoryName(cachePath));
        File.WriteAllText(cachePath, JsonUtility.ToJson(indexCache, true), Encoding.UTF8);

        statusText = "Rebuilt semantic UI index: " + indexCache.Entries.Count + " entries, "
                     + indexCache.Aliases.Count + " aliases, "
                     + (DateTime.Now - startedAt).TotalSeconds.ToString("0.0") + "s.";
    }

    private void Search()
    {
        if (indexCache == null || indexCache.Entries == null)
        {
            results.Clear();
            return;
        }

        string query = searchText == null ? string.Empty : searchText.Trim();
        if (string.IsNullOrEmpty(query))
        {
            results.Clear();
            ClearPendingPreviewRequests();
            statusText = "Type a semantic clue, UI route, or prefab filename.";
            return;
        }

        QueryPlan queryPlan = QueryPlan.Build(query, indexCache.Aliases);
        List<SearchResult> nextResults = new List<SearchResult>();

        for (int i = 0; i < indexCache.Entries.Count; i++)
        {
            SearchResult result = ScoreEntry(indexCache.Entries[i], queryPlan);
            if (result.Score > 0f)
            {
                nextResults.Add(result);
            }
        }

        results = nextResults
            .OrderByDescending(item => item.Score)
            .ThenBy(item => string.IsNullOrEmpty(item.Entry.Route) ? 1 : 0)
            .ThenBy(item => item.Entry.PrefabPath)
            .Take(MaxResults)
            .ToList();

        ClearPendingPreviewRequests();
        statusText = "Search \"" + query + "\" found " + results.Count + " result(s).";
    }

    private void ClearPendingPreviewRequests()
    {
        prefabPreviewQueue.Clear();
        prefabPreviewQueued.Clear();
    }

    private static SearchResult ScoreEntry(IndexEntry entry, QueryPlan queryPlan)
    {
        SearchResult result = new SearchResult();
        result.Entry = entry;

        string haystack = entry.Haystack ?? string.Empty;
        string prefabName = Path.GetFileNameWithoutExtension(entry.PrefabPath ?? string.Empty);
        string route = entry.Route ?? string.Empty;

        if (!string.IsNullOrEmpty(queryPlan.NormalizedQuery) && haystack.Contains(queryPlan.NormalizedQuery))
        {
            result.Score += 120f;
            result.Evidence.Add("Exact text match: " + queryPlan.RawQuery);
        }

        for (int i = 0; i < queryPlan.AliasHits.Count; i++)
        {
            AliasEntry alias = queryPlan.AliasHits[i];
            bool aliasMatched = false;
            for (int t = 0; t < alias.SearchTerms.Count; t++)
            {
                string term = alias.SearchTerms[t];
                if (term.Length > 1 && haystack.Contains(term))
                {
                    aliasMatched = true;
                    result.Score += 24f;
                }
            }

            if (aliasMatched)
            {
                string aliasEvidence = string.IsNullOrEmpty(alias.Source)
                    ? "Semantic alias: " + alias.Phrase + " -> " + alias.Identifier
                    : "Semantic alias: " + alias.Phrase + " -> " + alias.Identifier + " (" + alias.Source + ")";
                AddEvidence(result.Evidence, aliasEvidence);
            }
        }

        for (int i = 0; i < queryPlan.Terms.Count; i++)
        {
            string term = queryPlan.Terms[i];
            if (term.Length <= 1)
            {
                continue;
            }

            if (haystack.Contains(term))
            {
                result.Score += 10f;
                AddEvidence(result.Evidence, "Matched term: " + term);
            }

            string routeNormalized = NormalizeForSearch(route);
            if (!string.IsNullOrEmpty(routeNormalized) && routeNormalized.Contains(term))
            {
                result.Score += 18f;
                AddEvidence(result.Evidence, "Route contains: " + term);
            }

            string prefabNormalized = NormalizeForSearch(prefabName);
            if (!string.IsNullOrEmpty(prefabNormalized) && prefabNormalized.Contains(term))
            {
                result.Score += 16f;
                AddEvidence(result.Evidence, "Prefab name contains: " + term);
            }
        }

        for (int i = 0; i < entry.Evidence.Count; i++)
        {
            if (result.Evidence.Count >= 8)
            {
                break;
            }

            string evidence = entry.Evidence[i];
            string evidenceSearch = ExpandTextForSearch(evidence);
            if (ContainsAny(evidenceSearch, queryPlan.Terms) || ContainsAny(evidenceSearch, queryPlan.AliasTerms))
            {
                AddEvidence(result.Evidence, evidence);
                result.Score += 8f;
            }
        }

        if (result.Score > 0f && !string.IsNullOrEmpty(route))
        {
            result.Score += 4f;
        }

        return result;
    }

    private static void PingPrefab(string prefabPath)
    {
        UnityEngine.Object prefab = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(prefabPath);
        if (prefab == null)
        {
            EditorUtility.DisplayDialog("UI Semantic Locator", "Prefab not found:\n" + prefabPath, "OK");
            return;
        }

        Selection.activeObject = prefab;
        EditorGUIUtility.PingObject(prefab);
    }

    private static void OpenPrefab(string prefabPath)
    {
        UnityEngine.Object prefab = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(prefabPath);
        if (prefab == null)
        {
            EditorUtility.DisplayDialog("UI Semantic Locator", "Prefab not found:\n" + prefabPath, "OK");
            return;
        }

        AssetDatabase.OpenAsset(prefab);
    }

    private static string GetProjectRoot()
    {
        return Directory.GetParent(Application.dataPath).FullName.Replace('\\', '/');
    }

    private static string GetCachePath()
    {
        return Path.Combine(GetProjectRoot(), CacheRelativePath).Replace('\\', '/');
    }

    private static bool ContainsAny(string haystack, List<string> terms)
    {
        if (string.IsNullOrEmpty(haystack) || terms == null)
        {
            return false;
        }

        for (int i = 0; i < terms.Count; i++)
        {
            string term = terms[i];
            if (term.Length > 1 && haystack.Contains(term))
            {
                return true;
            }
        }

        return false;
    }

    private static void AddEvidence(List<string> evidence, string text)
    {
        if (string.IsNullOrEmpty(text) || evidence.Contains(text))
        {
            return;
        }

        evidence.Add(text);
    }

    private static string NormalizeForSearch(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        string lower = text.ToLowerInvariant();
        string normalized = WordSplitRegex.Replace(lower, " ");
        return Regex.Replace(normalized, "\\s+", " ").Trim();
    }

    private static string ExpandTextForSearch(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        StringBuilder builder = new StringBuilder();
        builder.Append(NormalizeForSearch(text));

        MatchCollection identifiers = IdentifierRegex.Matches(text);
        for (int i = 0; i < identifiers.Count; i++)
        {
            AppendIdentifierTerms(builder, identifiers[i].Value);
        }

        return NormalizeForSearch(builder.ToString());
    }

    private static void AppendIdentifierTerms(StringBuilder builder, string identifier)
    {
        if (string.IsNullOrEmpty(identifier))
        {
            return;
        }

        string[] underscoreParts = identifier.Split(new[] { '_' }, StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < underscoreParts.Length; i++)
        {
            builder.Append(' ');
            builder.Append(underscoreParts[i]);
        }

        string camelSplit = Regex.Replace(identifier, "([a-z0-9])([A-Z])", "$1 $2");
        camelSplit = Regex.Replace(camelSplit, "([A-Z]+)([A-Z][a-z])", "$1 $2");
        builder.Append(' ');
        builder.Append(camelSplit);
    }

    private static string ResourcePathToAssetPath(string prefabPath)
    {
        if (string.IsNullOrEmpty(prefabPath))
        {
            return string.Empty;
        }

        string normalized = prefabPath.Replace('\\', '/').Trim();
        if (normalized.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
        {
            return normalized;
        }

        if (normalized.StartsWith("UI/Prefab/", StringComparison.OrdinalIgnoreCase))
        {
            return "Assets/Content/" + normalized;
        }

        if (normalized.StartsWith("Prefab/", StringComparison.OrdinalIgnoreCase))
        {
            return "Assets/Content/UI/" + normalized;
        }

        return normalized;
    }

    private static int GetLineNumber(string text, int index)
    {
        int line = 1;
        int safeIndex = Mathf.Clamp(index, 0, text.Length);
        for (int i = 0; i < safeIndex; i++)
        {
            if (text[i] == '\n')
            {
                line++;
            }
        }

        return line;
    }

    private static string GetContextWindow(string text, int lineNumber, int radius)
    {
        string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        int start = Mathf.Max(0, lineNumber - radius - 1);
        int end = Mathf.Min(lines.Length - 1, lineNumber + radius - 1);

        StringBuilder builder = new StringBuilder();
        for (int i = start; i <= end; i++)
        {
            string trimmed = lines[i].Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.Append(" | ");
            }

            builder.Append(trimmed);
        }

        return builder.ToString();
    }

    private static string ReadTextSafe(string path)
    {
        try
        {
            return File.ReadAllText(path, Encoding.UTF8);
        }
        catch
        {
            try
            {
                return File.ReadAllText(path, Encoding.Default);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("Failed to read " + path + ": " + ex.Message);
                return string.Empty;
            }
        }
    }

    [Serializable]
    private class IndexCache
    {
        public int Version;
        public string BuiltAt;
        public List<IndexEntry> Entries = new List<IndexEntry>();
        public List<AliasEntry> Aliases = new List<AliasEntry>();
    }

    [Serializable]
    private class IndexEntry
    {
        public string Route;
        public string PrefabPath;
        public string Source;
        public string Haystack;
        public List<string> Evidence = new List<string>();
    }

    [Serializable]
    private class AliasEntry
    {
        public string Phrase;
        public string Identifier;
        public string Source;
        public List<string> SearchTerms = new List<string>();
    }

    private class SearchResult
    {
        public IndexEntry Entry;
        public float Score;
        public List<string> Evidence = new List<string>();
    }

    private class QueryPlan
    {
        public string RawQuery;
        public string NormalizedQuery;
        public List<string> Terms = new List<string>();
        public List<string> AliasTerms = new List<string>();
        public List<AliasEntry> AliasHits = new List<AliasEntry>();

        public static QueryPlan Build(string query, List<AliasEntry> aliases)
        {
            QueryPlan plan = new QueryPlan();
            plan.RawQuery = query;
            plan.NormalizedQuery = NormalizeForSearch(query);
            AddTerms(plan.Terms, ExpandTextForSearch(query));

            if (aliases != null)
            {
                for (int i = 0; i < aliases.Count; i++)
                {
                    AliasEntry alias = aliases[i];
                    string phrase = alias.Phrase ?? string.Empty;
                    string normalizedPhrase = NormalizeForSearch(phrase);

                    bool phraseMatched = !string.IsNullOrEmpty(normalizedPhrase)
                                         && (plan.NormalizedQuery.Contains(normalizedPhrase) || normalizedPhrase.Contains(plan.NormalizedQuery));
                    if (!phraseMatched)
                    {
                        continue;
                    }

                    plan.AliasHits.Add(alias);
                    AddTerms(plan.AliasTerms, alias.SearchTerms);
                    AddTerms(plan.Terms, alias.SearchTerms);
                    AddTerms(plan.Terms, ExpandTextForSearch(alias.Identifier));
                }
            }

            plan.Terms = plan.Terms.Distinct().Where(term => term.Length > 1).ToList();
            plan.AliasTerms = plan.AliasTerms.Distinct().Where(term => term.Length > 1).ToList();
            return plan;
        }

        private static void AddTerms(List<string> terms, string expandedText)
        {
            if (string.IsNullOrEmpty(expandedText))
            {
                return;
            }

            string[] parts = expandedText.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                string term = parts[i].Trim();
                if (term.Length > 0 && !terms.Contains(term))
                {
                    terms.Add(term);
                }
            }
        }

        private static void AddTerms(List<string> terms, List<string> extraTerms)
        {
            if (extraTerms == null)
            {
                return;
            }

            for (int i = 0; i < extraTerms.Count; i++)
            {
                string term = NormalizeForSearch(extraTerms[i]);
                if (term.Length > 0 && !terms.Contains(term))
                {
                    terms.Add(term);
                }
            }
        }
    }

    private class IndexBuilder
    {
        private readonly string projectRoot;
        private readonly Dictionary<string, IndexEntry> entriesByKey = new Dictionary<string, IndexEntry>();
        private readonly Dictionary<string, List<IndexEntry>> entriesByRoute = new Dictionary<string, List<IndexEntry>>();
        private readonly HashSet<string> prefabsWithRoute = new HashSet<string>();
        private readonly List<AliasEntry> aliases = new List<AliasEntry>();

        public IndexBuilder(string projectRoot)
        {
            this.projectRoot = projectRoot.Replace('\\', '/');
        }

        public IndexCache Build()
        {
            AddBuiltInAliases();
            AddPanelMappings();
            AddActDataEvidence();
            AddRawPrefabs();
            AddCodeAliasesAndCreateEvidence();
            FinalizeHaystacks();

            IndexCache cache = new IndexCache();
            cache.Version = CacheVersion;
            cache.BuiltAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            cache.Entries = entriesByKey.Values
                .OrderBy(entry => entry.PrefabPath)
                .ThenBy(entry => entry.Route)
                .ToList();
            cache.Aliases = aliases
                .OrderBy(alias => alias.Phrase)
                .ThenBy(alias => alias.Identifier)
                .ToList();
            return cache;
        }

        private void AddPanelMappings()
        {
            string[] mappingFiles =
            {
                CombineProjectPath("game/cross/sgr_data/AB/UIPanelViewMappingData.py"),
                CombineProjectPath("game/server/finalized/A9/UIPanelViewMappingData.py")
            };

            for (int i = 0; i < mappingFiles.Length; i++)
            {
                string file = mappingFiles[i];
                if (!File.Exists(file))
                {
                    continue;
                }

                string text = ReadTextSafe(file);
                MatchCollection matches = PanelMappingRegex.Matches(text);
                for (int m = 0; m < matches.Count; m++)
                {
                    Match match = matches[m];
                    string route = match.Groups[1].Value.Trim();
                    string prefabResourcePath = match.Groups[2].Value.Trim();
                    string prefabAssetPath = ResourcePathToAssetPath(prefabResourcePath);
                    if (!IsUiPrefabPath(prefabAssetPath))
                    {
                        continue;
                    }

                    IndexEntry entry = GetOrCreateEntry(route, prefabAssetPath, "UIPanelViewMappingData");
                    int line = GetLineNumber(text, match.Index);
                    AddEvidence(entry.Evidence, MakeRelative(file) + ":" + line + " maps " + route + " -> " + prefabResourcePath);
                    AppendHaystack(entry, route + " " + prefabResourcePath + " " + prefabAssetPath);
                    prefabsWithRoute.Add(prefabAssetPath);
                }
            }
        }

        private void AddActDataEvidence()
        {
            string[] actDataFiles =
            {
                CombineProjectPath("game/server/finalized/A9/ActData.py")
            };

            for (int i = 0; i < actDataFiles.Length; i++)
            {
                string file = actDataFiles[i];
                if (!File.Exists(file))
                {
                    continue;
                }

                string text = ReadTextSafe(file);
                string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
                bool inEntry = false;
                int entryLine = 0;
                string route = string.Empty;
                List<string> displayNames = new List<string>();

                for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
                {
                    string line = lines[lineIndex];
                    if (!inEntry && ActEntryStartRegex.IsMatch(line))
                    {
                        inEntry = true;
                        entryLine = lineIndex + 1;
                        route = string.Empty;
                        displayNames.Clear();
                        continue;
                    }

                    if (!inEntry)
                    {
                        continue;
                    }

                    Match routeMatch = ActShowTabLineRegex.Match(line);
                    if (routeMatch.Success)
                    {
                        route = routeMatch.Groups[1].Value.Trim();
                    }

                    Match nameMatch = ActShowNameLineRegex.Match(line);
                    if (nameMatch.Success)
                    {
                        string displayName = nameMatch.Groups[1].Value.Trim();
                        if (!string.IsNullOrEmpty(displayName) && !displayNames.Contains(displayName))
                        {
                            displayNames.Add(displayName);
                        }
                    }

                    if (ActEntryEndRegex.IsMatch(line))
                    {
                        AddActEntryEvidence(file, entryLine, route, displayNames);
                        inEntry = false;
                    }
                }
            }
        }

        private void AddActEntryEvidence(string file, int line, string route, List<string> displayNames)
        {
            if (string.IsNullOrEmpty(route) || displayNames == null || displayNames.Count == 0)
            {
                return;
            }

            List<IndexEntry> routeEntries;
            if (!entriesByRoute.TryGetValue(route, out routeEntries))
            {
                return;
            }

            string source = MakeRelative(file) + ":" + line;
            for (int i = 0; i < displayNames.Count; i++)
            {
                string displayName = displayNames[i];
                if (string.IsNullOrEmpty(displayName) || !ContainsCjk(displayName))
                {
                    continue;
                }

                string baseName = StripActivityOrdinal(displayName);
                AddAlias(displayName, route + " " + RouteToSearchTerms(route), source);
                if (!string.IsNullOrEmpty(baseName) && baseName != displayName)
                {
                    AddAlias(baseName, route + " " + RouteToSearchTerms(route), source);
                }

                for (int e = 0; e < routeEntries.Count; e++)
                {
                    IndexEntry entry = routeEntries[e];
                    AddEvidence(entry.Evidence, source + " ActData show_name " + displayName + " uses " + route);
                    AppendHaystack(entry, displayName + " " + baseName + " " + route);
                }
            }
        }

        private void AddRawPrefabs()
        {
            string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { PrefabRoot });
            Array.Sort(guids, StringComparer.Ordinal);

            for (int i = 0; i < guids.Length; i++)
            {
                string prefabPath = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (!IsUiPrefabPath(prefabPath) || prefabsWithRoute.Contains(prefabPath))
                {
                    continue;
                }

                IndexEntry entry = GetOrCreateEntry(string.Empty, prefabPath, "PrefabAsset");
                AddEvidence(entry.Evidence, "Prefab asset fallback: " + prefabPath);
                AppendHaystack(entry, prefabPath + " " + Path.GetFileNameWithoutExtension(prefabPath));
            }
        }

        private void AddCodeAliasesAndCreateEvidence()
        {
            string clientUiRoot = CombineProjectPath("game/client/ui");
            if (!Directory.Exists(clientUiRoot))
            {
                return;
            }

            string[] pythonFiles = Directory.GetFiles(clientUiRoot, "*.py", SearchOption.AllDirectories);
            Array.Sort(pythonFiles, StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < pythonFiles.Length; i++)
            {
                string file = pythonFiles[i].Replace('\\', '/');
                string text = ReadTextSafe(file);
                if (string.IsNullOrEmpty(text))
                {
                    continue;
                }

                AddCommentAliasesFromFile(file, text);
                AddCreateEvidenceFromFile(file, text);
            }
        }

        private void AddCommentAliasesFromFile(string file, string text)
        {
            string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                Match match = CommentAliasRegex.Match(lines[i]);
                if (!match.Success)
                {
                    continue;
                }

                string identifier = match.Groups[1].Value.Trim();
                string phrase = CleanCommentPhrase(match.Groups[2].Value);
                if (!ContainsCjk(phrase))
                {
                    continue;
                }

                AddAlias(phrase, identifier, MakeRelative(file) + ":" + (i + 1));
            }
        }

        private void AddCreateEvidenceFromFile(string file, string text)
        {
            MatchCollection matches = UiCreateRegex.Matches(text);
            for (int i = 0; i < matches.Count; i++)
            {
                Match match = matches[i];
                string route = match.Groups[1].Value.Trim();
                List<IndexEntry> routeEntries;
                if (!entriesByRoute.TryGetValue(route, out routeEntries))
                {
                    continue;
                }

                int line = GetLineNumber(text, match.Index);
                string context = GetContextWindow(text, line, 5);
                string relative = MakeRelative(file);

                for (int e = 0; e < routeEntries.Count; e++)
                {
                    IndexEntry entry = routeEntries[e];
                    AddEvidence(entry.Evidence, relative + ":" + line + " creates " + route);
                    AppendHaystack(entry, relative + " " + context);
                }
            }

            AddHelperCreateEvidence(file, text, "ui_utils.OpenMultiRewardWindow", "common.multi_reward_window");
        }

        private void AddHelperCreateEvidence(string file, string text, string helperCall, string route)
        {
            List<IndexEntry> routeEntries;
            if (!entriesByRoute.TryGetValue(route, out routeEntries))
            {
                return;
            }

            int searchFrom = 0;
            while (searchFrom < text.Length)
            {
                int index = text.IndexOf(helperCall, searchFrom, StringComparison.Ordinal);
                if (index < 0)
                {
                    break;
                }

                int line = GetLineNumber(text, index);
                string context = GetContextWindow(text, line, 5);
                string relative = MakeRelative(file);

                for (int i = 0; i < routeEntries.Count; i++)
                {
                    IndexEntry entry = routeEntries[i];
                    AddEvidence(entry.Evidence, relative + ":" + line + " calls " + helperCall + " -> " + route);
                    AppendHaystack(entry, relative + " " + context);
                }

                searchFrom = index + helperCall.Length;
            }
        }

        private void AddBuiltInAliases()
        {
            AddAlias("英雄之旅", "HERO_JOURNEY OnTrgHeroJourney first_journey season_all_common event_subscribe season_course_subscribe yingxiongzhilv", "built-in project bridge");
        }

        private void FinalizeHaystacks()
        {
            foreach (IndexEntry entry in entriesByKey.Values)
            {
                StringBuilder builder = new StringBuilder();
                builder.Append(entry.Haystack);
                builder.Append(' ');
                builder.Append(entry.Route);
                builder.Append(' ');
                builder.Append(entry.PrefabPath);
                builder.Append(' ');
                builder.Append(Path.GetFileNameWithoutExtension(entry.PrefabPath ?? string.Empty));

                for (int i = 0; i < entry.Evidence.Count; i++)
                {
                    builder.Append(' ');
                    builder.Append(entry.Evidence[i]);
                }

                entry.Haystack = ExpandTextForSearch(builder.ToString());
            }

            for (int i = 0; i < aliases.Count; i++)
            {
                AliasEntry alias = aliases[i];
                alias.SearchTerms = BuildAliasTerms(alias.Identifier);
            }
        }

        private IndexEntry GetOrCreateEntry(string route, string prefabPath, string source)
        {
            string key = string.IsNullOrEmpty(route) ? prefabPath : route + "|" + prefabPath;
            IndexEntry entry;
            if (entriesByKey.TryGetValue(key, out entry))
            {
                return entry;
            }

            entry = new IndexEntry();
            entry.Route = route;
            entry.PrefabPath = prefabPath;
            entry.Source = source;
            entry.Haystack = string.Empty;
            entriesByKey[key] = entry;

            if (!string.IsNullOrEmpty(route))
            {
                List<IndexEntry> routeEntries;
                if (!entriesByRoute.TryGetValue(route, out routeEntries))
                {
                    routeEntries = new List<IndexEntry>();
                    entriesByRoute[route] = routeEntries;
                }

                routeEntries.Add(entry);
            }

            return entry;
        }

        private void AppendHaystack(IndexEntry entry, string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            entry.Haystack = (entry.Haystack ?? string.Empty) + " " + text;
        }

        private void AddAlias(string phrase, string identifier, string source)
        {
            if (string.IsNullOrEmpty(phrase) || string.IsNullOrEmpty(identifier))
            {
                return;
            }

            phrase = phrase.Trim();
            identifier = identifier.Trim();

            for (int i = 0; i < aliases.Count; i++)
            {
                if (aliases[i].Phrase == phrase && aliases[i].Identifier == identifier)
                {
                    return;
                }
            }

            AliasEntry alias = new AliasEntry();
            alias.Phrase = phrase;
            alias.Identifier = identifier;
            alias.Source = source;
            alias.SearchTerms = BuildAliasTerms(identifier);
            aliases.Add(alias);
        }

        private static List<string> BuildAliasTerms(string identifier)
        {
            string expanded = ExpandTextForSearch(identifier);
            return expanded.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(term => term.Length > 1)
                .Distinct()
                .ToList();
        }

        private static bool IsUiPrefabPath(string prefabPath)
        {
            return !string.IsNullOrEmpty(prefabPath)
                   && prefabPath.StartsWith(PrefabRoot + "/", StringComparison.OrdinalIgnoreCase)
                   && prefabPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase);
        }

        private string CombineProjectPath(string relativePath)
        {
            return Path.Combine(projectRoot, relativePath).Replace('\\', '/');
        }

        private string MakeRelative(string absolutePath)
        {
            string normalized = absolutePath.Replace('\\', '/');
            if (normalized.StartsWith(projectRoot + "/", StringComparison.OrdinalIgnoreCase))
            {
                return normalized.Substring(projectRoot.Length + 1);
            }

            return normalized;
        }

        private static string CleanCommentPhrase(string comment)
        {
            if (string.IsNullOrEmpty(comment))
            {
                return string.Empty;
            }

            string phrase = comment.Trim();
            int comma = phrase.IndexOfAny(new[] { ',', '，', ';', '；', '(', '（' });
            if (comma > 0)
            {
                phrase = phrase.Substring(0, comma);
            }

            return phrase.Trim();
        }

        private static string StripActivityOrdinal(string displayName)
        {
            if (string.IsNullOrEmpty(displayName))
            {
                return string.Empty;
            }

            string trimmed = displayName.Trim();
            return Regex.Replace(trimmed, "\\s+(?:[IVXLCDM]+|[0-9]+|[一二三四五六七八九十]+)$", string.Empty, RegexOptions.IgnoreCase).Trim();
        }

        private static string RouteToSearchTerms(string route)
        {
            if (string.IsNullOrEmpty(route))
            {
                return string.Empty;
            }

            return route.Replace('.', ' ').Replace('_', ' ');
        }

        private static bool ContainsCjk(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if ((c >= 0x4E00 && c <= 0x9FFF) || (c >= 0x3400 && c <= 0x4DBF))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
