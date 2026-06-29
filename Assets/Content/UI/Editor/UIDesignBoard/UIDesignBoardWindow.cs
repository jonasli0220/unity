using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

public class UIDesignBoardWindow : EditorWindow
{
    private const int BoardVersion = 1;
    private const int SemanticCacheVersion = 2;
    private const int MaxSearchResults = 40;
    private const int PreviewRenderSize = 384;
    private const float ToolbarHeight = 22f;
    private const float SidebarWidth = 340f;
    private const float CardWidth = 340f;
    private const float CardHeight = 248f;
    private const float CardGap = 72f;
    private const float MinZoom = 0.25f;
    private const float MaxZoom = 2.5f;
    private const string MenuPath = "Tools/UI/UI Design Board/Open";
    private const string OpenLiveMenuPath = "Tools/UI/UI Design Board/Open Live Board";
    private const string AddSelectedMenuPath = "Tools/UI/UI Design Board/Add Selected Prefabs";
    private const string AssetAddSelectedMenuPath = "Assets/UI Design Board/Add Selected Prefabs to Board";
    private const string SearchControlName = "UIDesignBoard.Search";
    private const string PrefabRoot = "Assets/Content/UI/Prefab";
    private const string BoardRelativePath = "Library/Dragon/UIDesignBoard/board.json";
    private const string SemanticCacheRelativePath = "Library/Dragon/UISemanticLocator/index.json";

    private static readonly Regex WordSplitRegex = new Regex("[^\\p{L}\\p{Nd}]+", RegexOptions.Compiled);

    private UIDesignBoardState board;
    private Vector2 searchScroll;
    private Vector2 boardListScroll;
    private string searchText = string.Empty;
    private string statusText = string.Empty;
    private string selectedGuid = string.Empty;
    private string pendingFocusGuid = string.Empty;
    private Rect lastCanvasRect;
    private List<SearchCandidate> searchIndex = new List<SearchCandidate>();
    private List<SearchResult> searchResults = new List<SearchResult>();
    private bool searchIndexLoaded;
    private bool isPanning;
    private bool isDraggingCard;
    private string draggingGuid = string.Empty;
    private Vector2 dragOffsetCanvas;
    private Vector2 lastMousePosition;
    private bool boardDirty;
    private bool saveScheduled;

    private readonly Dictionary<string, Texture2D> previewCache = new Dictionary<string, Texture2D>();
    private readonly Dictionary<string, string> previewErrors = new Dictionary<string, string>();
    private readonly Queue<string> previewQueue = new Queue<string>();
    private readonly HashSet<string> queuedPreviewPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private bool previewGenerationScheduled;
    private static bool previewMethodResolved;
    private static MethodInfo previewGenerateMethod;

    [MenuItem(MenuPath, false, 2320)]
    public static void Open()
    {
        UIDesignBoardWindow window = GetWindow<UIDesignBoardWindow>("UI Design Board");
        window.minSize = new Vector2(900f, 560f);
        window.Show();
    }

    [MenuItem(OpenLiveMenuPath, false, 2321)]
    private static void OpenLiveFromMenu()
    {
        Open();
        UIDesignBoardWindow window = GetWindow<UIDesignBoardWindow>("UI Design Board");
        window.OpenLiveBoard();
    }

    [MenuItem(AddSelectedMenuPath, false, 2321)]
    private static void AddSelectedPrefabsFromToolsMenu()
    {
        OpenAndAddSelection();
    }

    [MenuItem(AddSelectedMenuPath, true)]
    private static bool ValidateAddSelectedPrefabsFromToolsMenu()
    {
        return CollectSelectedUiPrefabPaths().Count > 0;
    }

    [MenuItem(AssetAddSelectedMenuPath, false, 2120)]
    private static void AddSelectedPrefabsFromProjectMenu()
    {
        OpenAndAddSelection();
    }

    [MenuItem(AssetAddSelectedMenuPath, true)]
    private static bool ValidateAddSelectedPrefabsFromProjectMenu()
    {
        return CollectSelectedUiPrefabPaths().Count > 0;
    }

    private static void OpenAndAddSelection()
    {
        Open();
        UIDesignBoardWindow window = GetWindow<UIDesignBoardWindow>("UI Design Board");
        window.AddSelectedPrefabsToBoard();
    }

    private void OnEnable()
    {
        UIDesignBoardLiveScene.StateChanged -= OnLiveBoardStateChanged;
        UIDesignBoardLiveScene.StateChanged += OnLiveBoardStateChanged;
        LoadBoard();
        RefreshArtboardPaths();
        LoadSearchIndex(false);
        Search();
    }

    private void OnDisable()
    {
        UIDesignBoardLiveScene.StateChanged -= OnLiveBoardStateChanged;
        EditorApplication.delayCall -= ProcessNextPreview;
        EditorApplication.delayCall -= SaveIfDirty;
        previewGenerationScheduled = false;
        saveScheduled = false;
        SaveIfDirty();
        DestroyPreviewTextures();
    }

    private void OnSelectionChange()
    {
        if (UIDesignBoardLiveScene.TryGetArtboardGuid(Selection.activeGameObject, out string guid))
        {
            selectedGuid = guid;
        }

        Repaint();
    }

    private void OnLiveBoardStateChanged()
    {
        if (UIDesignBoardLiveScene.TryGetArtboardGuid(Selection.activeGameObject, out string guid))
        {
            selectedGuid = guid;
        }

        Repaint();
    }

    private void OnGUI()
    {
        EnsureBoard();
        HandleGlobalKeys();
        DrawToolbar();

        Rect bodyRect = new Rect(0f, ToolbarHeight, position.width, Mathf.Max(0f, position.height - ToolbarHeight));
        float sidebarWidth = Mathf.Min(SidebarWidth, Mathf.Max(280f, bodyRect.width * 0.42f));
        Rect sidebarRect = new Rect(bodyRect.x, bodyRect.y, sidebarWidth, bodyRect.height);
        Rect canvasRect = new Rect(sidebarRect.xMax, bodyRect.y, Mathf.Max(0f, bodyRect.width - sidebarWidth), bodyRect.height);

        DrawSidebar(sidebarRect);
        DrawCanvas(canvasRect);
    }

    private void DrawToolbar()
    {
        using (new GUILayout.HorizontalScope(EditorStyles.toolbar, GUILayout.Height(ToolbarHeight)))
        {
            if (GUILayout.Button("Add Selected", EditorStyles.toolbarButton, GUILayout.Width(96f)))
            {
                AddSelectedPrefabsToBoard();
            }

            if (GUILayout.Button(
                    UIDesignBoardLiveScene.IsOpen ? "Sync Live" : "Live Edit",
                    EditorStyles.toolbarButton,
                    GUILayout.Width(68f)))
            {
                OpenLiveBoard();
            }

            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(selectedGuid)))
            {
                if (GUILayout.Button("Focus", EditorStyles.toolbarButton, GUILayout.Width(54f)))
                {
                    pendingFocusGuid = selectedGuid;
                    Repaint();
                }

                if (GUILayout.Button("Live", EditorStyles.toolbarButton, GUILayout.Width(44f)))
                {
                    FocusLiveArtboard(selectedGuid);
                }
            }

            if (GUILayout.Button("Fit All", EditorStyles.toolbarButton, GUILayout.Width(62f)))
            {
                FitAllArtboards(lastCanvasRect);
            }

            if (GUILayout.Button("Reset", EditorStyles.toolbarButton, GUILayout.Width(54f)))
            {
                board.Zoom = 1f;
                board.Pan = new Vector2(80f, 80f);
                MarkDirty();
                Repaint();
            }

            GUILayout.Space(8f);
            GUILayout.Label("Zoom " + Mathf.RoundToInt(board.Zoom * 100f) + "%", EditorStyles.miniLabel, GUILayout.Width(72f));

            using (new EditorGUI.DisabledScope(!UIDesignBoardLiveScene.IsOpen || string.IsNullOrEmpty(selectedGuid)))
            {
                if (GUILayout.Button("Apply", EditorStyles.toolbarButton, GUILayout.Width(48f)))
                {
                    ApplyLiveArtboard(selectedGuid);
                }

                if (GUILayout.Button("Revert", EditorStyles.toolbarButton, GUILayout.Width(52f)))
                {
                    RevertLiveArtboard(selectedGuid);
                }
            }

            using (new EditorGUI.DisabledScope(!UIDesignBoardLiveScene.IsOpen))
            {
                if (GUILayout.Button("Close Live", EditorStyles.toolbarButton, GUILayout.Width(70f)))
                {
                    UIDesignBoardLiveScene.Close(out statusText);
                }
            }

            GUILayout.FlexibleSpace();

            if (!string.IsNullOrEmpty(statusText))
            {
                GUILayout.Label(statusText, EditorStyles.miniLabel);
            }
        }
    }

    private void DrawSidebar(Rect rect)
    {
        EditorGUI.DrawRect(rect, new Color(0.18f, 0.18f, 0.18f, 1f));
        EditorGUI.DrawRect(new Rect(rect.xMax - 1f, rect.y, 1f, rect.height), new Color(0f, 0f, 0f, 0.35f));

        Rect inner = new Rect(rect.x + 10f, rect.y + 8f, rect.width - 20f, rect.height - 16f);
        GUILayout.BeginArea(inner);

        DrawSearchPanel();
        GUILayout.Space(8f);
        DrawBoardListPanel();

        GUILayout.EndArea();
    }

    private void DrawSearchPanel()
    {
        GUILayout.Label("Search Prefab", EditorStyles.boldLabel);

        using (new GUILayout.HorizontalScope())
        {
            Event current = Event.current;
            GUI.SetNextControlName(SearchControlName);
            GUIStyle searchStyle = GUI.skin.FindStyle("ToolbarSeachTextField") ?? EditorStyles.toolbarTextField;
            string nextSearch = GUILayout.TextField(searchText, searchStyle, GUILayout.MinWidth(160f));
            if (nextSearch != searchText)
            {
                searchText = nextSearch;
                Search();
            }

            if (GUILayout.Button("Search", GUILayout.Width(64f)))
            {
                Search();
            }

            if (current.type == EventType.KeyDown
                && IsSearchCommitKey(current.keyCode)
                && GUI.GetNameOfFocusedControl() == SearchControlName)
            {
                Search();
                current.Use();
            }
        }

        using (new GUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Reload Index", GUILayout.Width(96f)))
            {
                LoadSearchIndex(true);
                Search();
            }

            if (GUILayout.Button("Open Locator", GUILayout.Width(96f)))
            {
                EditorApplication.ExecuteMenuItem("Tools/UI/Semantic UI Locator");
            }

            GUILayout.FlexibleSpace();
            GUILayout.Label(searchIndex.Count + " prefabs", EditorStyles.miniLabel);
        }

        Rect resultRect = GUILayoutUtility.GetRect(1f, 180f, GUILayout.ExpandWidth(true));
        GUILayout.BeginArea(resultRect, EditorStyles.helpBox);
        searchScroll = GUILayout.BeginScrollView(searchScroll);

        if (string.IsNullOrEmpty(searchText.Trim()))
        {
            GUILayout.Label("Type a route, prefab name, activity name, or clue.", EditorStyles.wordWrappedMiniLabel);
        }
        else if (searchResults.Count == 0)
        {
            GUILayout.Label("No result. Try opening Semantic UI Locator once to rebuild its index.", EditorStyles.wordWrappedMiniLabel);
        }
        else
        {
            for (int i = 0; i < searchResults.Count; i++)
            {
                DrawSearchResult(searchResults[i]);
            }
        }

        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    private void DrawSearchResult(SearchResult result)
    {
        SearchCandidate item = result.Candidate;
        using (new GUILayout.VerticalScope(EditorStyles.helpBox))
        {
            GUILayout.Label(GetDisplayTitle(item.PrefabPath), EditorStyles.boldLabel);
            if (!string.IsNullOrEmpty(item.Route))
            {
                GUILayout.Label(item.Route, EditorStyles.miniLabel);
            }

            GUILayout.Label(item.PrefabPath, EditorStyles.wordWrappedMiniLabel);

            using (new GUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Add", GUILayout.Width(48f)))
                {
                    AddOrFocusArtboard(item.PrefabPath, item.Route);
                }

                if (GUILayout.Button("Open", GUILayout.Width(52f)))
                {
                    OpenPrefab(item.PrefabPath);
                }

                if (GUILayout.Button("Ping", GUILayout.Width(48f)))
                {
                    PingPrefab(item.PrefabPath);
                }

                GUILayout.FlexibleSpace();
                GUILayout.Label(result.Score.ToString("0"), EditorStyles.miniLabel, GUILayout.Width(32f));
            }
        }
    }

    private void DrawBoardListPanel()
    {
        using (new GUILayout.HorizontalScope())
        {
            GUILayout.Label("Board", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            GUILayout.Label(board.Artboards.Count + " artboards", EditorStyles.miniLabel);
        }

        Rect listRect = GUILayoutUtility.GetRect(1f, 1f, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
        GUILayout.BeginArea(listRect, EditorStyles.helpBox);
        boardListScroll = GUILayout.BeginScrollView(boardListScroll);

        if (board.Artboards.Count == 0)
        {
            GUILayout.Label("Select UI prefabs in Project, then click Add Selected.", EditorStyles.wordWrappedMiniLabel);
        }
        else
        {
            for (int i = 0; i < board.Artboards.Count; i++)
            {
                DrawBoardListItem(board.Artboards[i]);
            }
        }

        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    private void DrawBoardListItem(UIDesignArtboard artboard)
    {
        bool selected = artboard.Guid == selectedGuid;
        GUIStyle style = selected ? "MeTransitionSelectHead" : EditorStyles.helpBox;
        using (new GUILayout.VerticalScope(style))
        {
            using (new GUILayout.HorizontalScope())
            {
                if (GUILayout.Button(GetArtboardTitle(artboard), EditorStyles.label))
                {
                    SelectAndFocus(artboard.Guid);
                }

                if (GUILayout.Button("F", GUILayout.Width(24f)))
                {
                    SelectAndFocus(artboard.Guid);
                }
            }

            GUILayout.Label(ResolvePrefabPath(artboard), EditorStyles.wordWrappedMiniLabel);

            using (new GUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Live", GUILayout.Width(44f)))
                {
                    FocusLiveArtboard(artboard.Guid);
                }

                if (GUILayout.Button("Open", GUILayout.Width(52f)))
                {
                    OpenPrefab(ResolvePrefabPath(artboard));
                }

                if (GUILayout.Button("Ping", GUILayout.Width(48f)))
                {
                    PingPrefab(ResolvePrefabPath(artboard));
                }

                if (GUILayout.Button("Copy", GUILayout.Width(48f)))
                {
                    EditorGUIUtility.systemCopyBuffer = ResolvePrefabPath(artboard);
                    statusText = "Copied prefab path.";
                }

                GUILayout.FlexibleSpace();

                if (GUILayout.Button("Remove", GUILayout.Width(68f)))
                {
                    RemoveArtboard(artboard.Guid);
                }
            }
        }
    }

    private void DrawCanvas(Rect rect)
    {
        lastCanvasRect = rect;
        HandleCanvasEvents(rect);

        GUI.BeginGroup(rect);
        Rect localRect = new Rect(0f, 0f, rect.width, rect.height);

        DrawCanvasBackground(localRect);

        if (!string.IsNullOrEmpty(pendingFocusGuid))
        {
            FocusArtboard(pendingFocusGuid, localRect);
            pendingFocusGuid = string.Empty;
        }

        for (int i = 0; i < board.Artboards.Count; i++)
        {
            DrawArtboardCard(board.Artboards[i], localRect);
        }

        DrawCanvasHint(localRect);
        GUI.EndGroup();
    }

    private void DrawCanvasBackground(Rect rect)
    {
        EditorGUI.DrawRect(rect, new Color(0.12f, 0.125f, 0.13f, 1f));

        float spacing = 64f * board.Zoom;
        while (spacing < 24f)
        {
            spacing *= 2f;
        }

        while (spacing > 160f)
        {
            spacing *= 0.5f;
        }

        Color minor = new Color(1f, 1f, 1f, 0.045f);
        Color major = new Color(1f, 1f, 1f, 0.08f);
        float startX = Mathf.Repeat(board.Pan.x, spacing);
        float startY = Mathf.Repeat(board.Pan.y, spacing);

        int index = 0;
        for (float x = startX; x < rect.width; x += spacing)
        {
            EditorGUI.DrawRect(new Rect(Mathf.Round(x), 0f, 1f, rect.height), index % 4 == 0 ? major : minor);
            index++;
        }

        index = 0;
        for (float y = startY; y < rect.height; y += spacing)
        {
            EditorGUI.DrawRect(new Rect(0f, Mathf.Round(y), rect.width, 1f), index % 4 == 0 ? major : minor);
            index++;
        }

        Vector2 origin = CanvasToLocal(Vector2.zero);
        if (origin.x >= 0f && origin.x <= rect.width)
        {
            EditorGUI.DrawRect(new Rect(Mathf.Round(origin.x), 0f, 1f, rect.height), new Color(0.35f, 0.5f, 0.95f, 0.28f));
        }

        if (origin.y >= 0f && origin.y <= rect.height)
        {
            EditorGUI.DrawRect(new Rect(0f, Mathf.Round(origin.y), rect.width, 1f), new Color(0.35f, 0.5f, 0.95f, 0.28f));
        }
    }

    private void DrawArtboardCard(UIDesignArtboard artboard, Rect visibleRect)
    {
        Rect cardRect = GetLocalCardRect(artboard);
        if (!cardRect.Overlaps(visibleRect))
        {
            RequestPreview(ResolvePrefabPath(artboard));
            return;
        }

        RequestPreview(ResolvePrefabPath(artboard));

        bool selected = artboard.Guid == selectedGuid;
        Rect shadowRect = new Rect(cardRect.x + 4f, cardRect.y + 5f, cardRect.width, cardRect.height);
        EditorGUI.DrawRect(shadowRect, new Color(0f, 0f, 0f, 0.18f));
        EditorGUI.DrawRect(cardRect, new Color(0.235f, 0.24f, 0.25f, 1f));
        DrawBorder(cardRect, selected ? new Color(0.32f, 0.58f, 1f, 1f) : new Color(0f, 0f, 0f, 0.55f), selected ? 2f : 1f);

        float padding = Mathf.Clamp(12f * board.Zoom, 7f, 14f);
        float headerHeight = Mathf.Clamp(36f * board.Zoom, 24f, 38f);
        float actionHeight = board.Zoom > 0.55f ? 28f : 0f;
        Rect headerRect = new Rect(cardRect.x + padding, cardRect.y + 5f, cardRect.width - padding * 2f, headerHeight);
        Rect previewRect = new Rect(
            cardRect.x + padding,
            headerRect.yMax + 2f,
            cardRect.width - padding * 2f,
            Mathf.Max(40f, cardRect.height - headerHeight - actionHeight - padding * 2.5f));
        Rect actionRect = new Rect(cardRect.x + padding, cardRect.yMax - actionHeight - padding * 0.5f, cardRect.width - padding * 2f, actionHeight);

        GUI.Label(headerRect, GetArtboardTitle(artboard), selected ? EditorStyles.whiteBoldLabel : EditorStyles.boldLabel);
        DrawPreview(previewRect, ResolvePrefabPath(artboard));

        if (board.Zoom > 0.55f)
        {
            DrawCardActions(actionRect, artboard);
        }
    }

    private void DrawPreview(Rect rect, string prefabPath)
    {
        EditorGUI.DrawRect(rect, new Color(0.055f, 0.055f, 0.055f, 1f));
        Texture2D texture = GetPrefabPreview(prefabPath);
        if (texture != null)
        {
            GUI.DrawTexture(new Rect(rect.x + 2f, rect.y + 2f, rect.width - 4f, rect.height - 4f), texture, ScaleMode.ScaleToFit, true);
        }
        else
        {
            string label = previewErrors.ContainsKey(prefabPath) ? "Preview failed" : "Loading preview";
            GUI.Label(rect, label, CenteredMiniLabel);
        }

        DrawBorder(rect, new Color(0f, 0f, 0f, 0.45f), 1f);
    }

    private void DrawCardActions(Rect rect, UIDesignArtboard artboard)
    {
        float buttonWidth = 48f;
        Rect liveRect = new Rect(rect.x, rect.y, buttonWidth, 22f);
        Rect openRect = new Rect(liveRect.xMax + 6f, rect.y, buttonWidth, 22f);
        Rect pingRect = new Rect(openRect.xMax + 6f, rect.y, buttonWidth, 22f);
        Rect removeRect = new Rect(rect.xMax - 68f, rect.y, 68f, 22f);

        string path = ResolvePrefabPath(artboard);
        if (GUI.Button(liveRect, "Live"))
        {
            FocusLiveArtboard(artboard.Guid);
        }

        if (GUI.Button(openRect, "Open"))
        {
            OpenPrefab(path);
        }

        if (GUI.Button(pingRect, "Ping"))
        {
            PingPrefab(path);
        }

        if (GUI.Button(removeRect, "Remove"))
        {
            RemoveArtboard(artboard.Guid);
        }
    }

    private void DrawCanvasHint(Rect rect)
    {
        Rect hintRect = new Rect(rect.x + 12f, rect.yMax - 28f, rect.width - 24f, 20f);
        GUI.Label(hintRect, "Mouse wheel zooms. Middle/right drag pans. Drag a card to arrange. Double-click opens its Live artboard.", EditorStyles.miniLabel);
    }

    private void HandleCanvasEvents(Rect canvasRect)
    {
        Event current = Event.current;
        if (current == null || canvasRect.width <= 0f || canvasRect.height <= 0f)
        {
            return;
        }

        Vector2 localMouse = current.mousePosition - canvasRect.position;
        bool inside = new Rect(Vector2.zero, canvasRect.size).Contains(localMouse);

        if (current.type == EventType.ScrollWheel && inside)
        {
            ZoomAround(localMouse, current.delta.y);
            current.Use();
            return;
        }

        if (current.type == EventType.MouseDown && inside)
        {
            lastMousePosition = localMouse;

            if (current.button == 2 || current.button == 1 || (current.button == 0 && current.modifiers == EventModifiers.Alt))
            {
                isPanning = true;
                GUI.FocusControl(null);
                current.Use();
                return;
            }

            if (current.button == 0)
            {
                UIDesignArtboard hit = FindArtboardAtLocalPoint(localMouse);
                if (hit != null)
                {
                    selectedGuid = hit.Guid;
                    draggingGuid = hit.Guid;
                    dragOffsetCanvas = LocalToCanvas(localMouse) - hit.Position;
                    isDraggingCard = true;
                    GUI.FocusControl(null);

                    if (current.clickCount == 2)
                    {
                        FocusLiveArtboard(hit.Guid);
                        current.Use();
                    }

                    Repaint();
                }
                else
                {
                    selectedGuid = string.Empty;
                    GUI.FocusControl(null);
                    Repaint();
                }
            }
        }

        if (current.type == EventType.MouseDrag)
        {
            if (isPanning)
            {
                Vector2 delta = localMouse - lastMousePosition;
                board.Pan += delta;
                lastMousePosition = localMouse;
                MarkDirty();
                current.Use();
                Repaint();
                return;
            }

            if (isDraggingCard && !string.IsNullOrEmpty(draggingGuid))
            {
                UIDesignArtboard artboard = FindArtboardByGuid(draggingGuid);
                if (artboard != null)
                {
                    artboard.Position = LocalToCanvas(localMouse) - dragOffsetCanvas;
                    UIDesignBoardLiveScene.UpdateArtboardPosition(artboard.Guid, artboard.Position);
                    MarkDirty();
                    current.Use();
                    Repaint();
                }
            }
        }

        if (current.type == EventType.MouseUp || current.type == EventType.Ignore)
        {
            isPanning = false;
            isDraggingCard = false;
            draggingGuid = string.Empty;
        }
    }

    private void HandleGlobalKeys()
    {
        Event current = Event.current;
        if (current == null || current.type != EventType.KeyDown)
        {
            return;
        }

        if (GUI.GetNameOfFocusedControl() == SearchControlName)
        {
            return;
        }

        if ((current.keyCode == KeyCode.Delete || current.keyCode == KeyCode.Backspace) && !string.IsNullOrEmpty(selectedGuid))
        {
            RemoveArtboard(selectedGuid);
            current.Use();
            return;
        }

        if (current.keyCode == KeyCode.F && !string.IsNullOrEmpty(selectedGuid))
        {
            pendingFocusGuid = selectedGuid;
            current.Use();
            Repaint();
            return;
        }

        if (current.keyCode == KeyCode.A && current.control)
        {
            FitAllArtboards(lastCanvasRect);
            current.Use();
        }
    }

    private void AddSelectedPrefabsToBoard()
    {
        List<string> paths = CollectSelectedUiPrefabPaths();
        if (paths.Count == 0)
        {
            EditorUtility.DisplayDialog("UI Design Board", "Select one or more prefabs or folders under Assets/Content/UI/Prefab.", "OK");
            return;
        }

        int added = 0;
        for (int i = 0; i < paths.Count; i++)
        {
            if (AddOrFocusArtboard(paths[i], string.Empty, false))
            {
                added++;
            }
        }

        if (paths.Count > 0)
        {
            string lastPath = paths[paths.Count - 1];
            string guid = AssetDatabase.AssetPathToGUID(lastPath);
            selectedGuid = guid;
            pendingFocusGuid = guid;
        }

        statusText = added > 0
            ? "Added " + added + " prefab(s) to board."
            : "Selected prefab(s) are already on the board.";
        MarkDirty();
        Repaint();
    }

    private void OpenLiveBoard()
    {
        EnsureBoard();
        SaveIfDirty();
        UIDesignBoardLiveScene.OpenOrSync(
            CreateLiveItems(),
            selectedGuid,
            out statusText);
        Repaint();
    }

    private void FocusLiveArtboard(string guid)
    {
        if (string.IsNullOrEmpty(guid))
        {
            return;
        }

        selectedGuid = guid;
        if (!UIDesignBoardLiveScene.IsOpen)
        {
            UIDesignBoardLiveScene.OpenOrSync(
                CreateLiveItems(),
                guid,
                out statusText);
        }
        else
        {
            UIDesignBoardLiveScene.FocusArtboard(guid, out statusText);
        }

        Repaint();
    }

    private void ApplyLiveArtboard(string guid)
    {
        UIDesignBoardLiveScene.ApplyArtboard(guid, out statusText);
        Repaint();
    }

    private void RevertLiveArtboard(string guid)
    {
        UIDesignBoardLiveScene.RevertArtboard(guid, true, out statusText);
        Repaint();
    }

    private List<UIDesignBoardLiveItem> CreateLiveItems()
    {
        List<UIDesignBoardLiveItem> items = new List<UIDesignBoardLiveItem>();
        for (int i = 0; i < board.Artboards.Count; i++)
        {
            items.Add(CreateLiveItem(board.Artboards[i]));
        }

        return items;
    }

    private UIDesignBoardLiveItem CreateLiveItem(UIDesignArtboard artboard)
    {
        return new UIDesignBoardLiveItem
        {
            Guid = artboard.Guid,
            PrefabPath = ResolvePrefabPath(artboard),
            Title = GetArtboardTitle(artboard),
            Position = artboard.Position
        };
    }

    private void AddOrFocusArtboard(string prefabPath, string route)
    {
        bool added = AddOrFocusArtboard(prefabPath, route, true);
        statusText = added ? "Added prefab to board." : "Prefab is already on the board.";
    }

    private bool AddOrFocusArtboard(string prefabPath, string route, bool focus)
    {
        if (!IsUiPrefabPath(prefabPath))
        {
            return false;
        }

        string guid = AssetDatabase.AssetPathToGUID(prefabPath);
        if (string.IsNullOrEmpty(guid))
        {
            return false;
        }

        UIDesignArtboard existing = FindArtboardByGuid(guid);
        if (existing != null)
        {
            if (!string.IsNullOrEmpty(route) && string.IsNullOrEmpty(existing.Route))
            {
                existing.Route = route;
                MarkDirty();
            }

            selectedGuid = existing.Guid;
            if (focus)
            {
                pendingFocusGuid = existing.Guid;
            }

            UIDesignBoardLiveScene.AddOrSyncArtboard(CreateLiveItem(existing));

            Repaint();
            return false;
        }

        UIDesignArtboard artboard = new UIDesignArtboard
        {
            Guid = guid,
            PrefabPath = prefabPath,
            Title = GetDisplayTitle(prefabPath),
            Route = route,
            Position = GetNextArtboardPosition()
        };

        board.Artboards.Add(artboard);
        UIDesignBoardLiveScene.AddOrSyncArtboard(CreateLiveItem(artboard));
        selectedGuid = guid;
        if (focus)
        {
            pendingFocusGuid = guid;
        }

        MarkDirty();
        Repaint();
        return true;
    }

    private void RemoveArtboard(string guid)
    {
        if (string.IsNullOrEmpty(guid))
        {
            return;
        }

        int index = board.Artboards.FindIndex(item => item.Guid == guid);
        if (index < 0)
        {
            return;
        }

        if (!UIDesignBoardLiveScene.RemoveArtboard(guid, out string liveMessage))
        {
            statusText = liveMessage;
            return;
        }

        board.Artboards.RemoveAt(index);
        if (selectedGuid == guid)
        {
            selectedGuid = string.Empty;
        }

        MarkDirty();
        statusText = string.IsNullOrEmpty(liveMessage) ? "Removed artboard." : liveMessage;
        Repaint();
    }

    private void SelectAndFocus(string guid)
    {
        selectedGuid = guid;
        pendingFocusGuid = guid;
        GUI.FocusControl(null);
        Repaint();
    }

    private Vector2 GetNextArtboardPosition()
    {
        if (board.Artboards.Count == 0)
        {
            return Vector2.zero;
        }

        int index = board.Artboards.Count;
        int column = index % 4;
        int row = index / 4;
        return new Vector2(column * (CardWidth + CardGap), row * (CardHeight + CardGap));
    }

    private void FocusArtboard(string guid, Rect localCanvasRect)
    {
        UIDesignArtboard artboard = FindArtboardByGuid(guid);
        if (artboard == null || localCanvasRect.width <= 0f || localCanvasRect.height <= 0f)
        {
            return;
        }

        Vector2 center = artboard.Position + new Vector2(CardWidth, CardHeight) * 0.5f;
        board.Pan = localCanvasRect.center - center * board.Zoom;
        MarkDirty();
        Repaint();
    }

    private void FitAllArtboards(Rect canvasRect)
    {
        if (board.Artboards.Count == 0)
        {
            board.Zoom = 1f;
            board.Pan = new Vector2(80f, 80f);
            MarkDirty();
            Repaint();
            return;
        }

        Rect localCanvasRect = new Rect(0f, 0f, canvasRect.width, canvasRect.height);
        if (localCanvasRect.width <= 0f || localCanvasRect.height <= 0f)
        {
            return;
        }

        Rect bounds = GetBoardBounds();
        float padding = 80f;
        float zoomX = (localCanvasRect.width - padding) / Mathf.Max(bounds.width, 1f);
        float zoomY = (localCanvasRect.height - padding) / Mathf.Max(bounds.height, 1f);
        board.Zoom = Mathf.Clamp(Mathf.Min(zoomX, zoomY), MinZoom, MaxZoom);
        board.Pan = localCanvasRect.center - bounds.center * board.Zoom;
        MarkDirty();
        Repaint();
    }

    private Rect GetBoardBounds()
    {
        if (board.Artboards.Count == 0)
        {
            return new Rect(0f, 0f, CardWidth, CardHeight);
        }

        Rect bounds = new Rect(board.Artboards[0].Position.x, board.Artboards[0].Position.y, CardWidth, CardHeight);
        for (int i = 1; i < board.Artboards.Count; i++)
        {
            Rect card = new Rect(board.Artboards[i].Position.x, board.Artboards[i].Position.y, CardWidth, CardHeight);
            bounds = Union(bounds, card);
        }

        return bounds;
    }

    private static Rect Union(Rect a, Rect b)
    {
        float xMin = Mathf.Min(a.xMin, b.xMin);
        float yMin = Mathf.Min(a.yMin, b.yMin);
        float xMax = Mathf.Max(a.xMax, b.xMax);
        float yMax = Mathf.Max(a.yMax, b.yMax);
        return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
    }

    private void ZoomAround(Vector2 localMouse, float wheelDelta)
    {
        Vector2 before = LocalToCanvas(localMouse);
        float nextZoom = board.Zoom * (wheelDelta > 0f ? 0.9f : 1.1f);
        board.Zoom = Mathf.Clamp(nextZoom, MinZoom, MaxZoom);
        board.Pan = localMouse - before * board.Zoom;
        MarkDirty();
        Repaint();
    }

    private UIDesignArtboard FindArtboardAtLocalPoint(Vector2 localPoint)
    {
        for (int i = board.Artboards.Count - 1; i >= 0; i--)
        {
            UIDesignArtboard artboard = board.Artboards[i];
            if (GetLocalCardRect(artboard).Contains(localPoint))
            {
                return artboard;
            }
        }

        return null;
    }

    private Rect GetLocalCardRect(UIDesignArtboard artboard)
    {
        Vector2 position = CanvasToLocal(artboard.Position);
        return new Rect(position.x, position.y, CardWidth * board.Zoom, CardHeight * board.Zoom);
    }

    private Vector2 CanvasToLocal(Vector2 canvasPoint)
    {
        return canvasPoint * board.Zoom + board.Pan;
    }

    private Vector2 LocalToCanvas(Vector2 localPoint)
    {
        return (localPoint - board.Pan) / Mathf.Max(board.Zoom, 0.0001f);
    }

    private void LoadSearchIndex(bool force)
    {
        if (searchIndexLoaded && !force)
        {
            return;
        }

        Dictionary<string, SearchCandidate> candidates = new Dictionary<string, SearchCandidate>(StringComparer.OrdinalIgnoreCase);
        LoadSemanticCache(candidates);
        AddRawPrefabCandidates(candidates);
        searchIndex = candidates.Values
            .OrderBy(item => item.PrefabPath, StringComparer.OrdinalIgnoreCase)
            .ToList();
        searchIndexLoaded = true;
        statusText = "Loaded " + searchIndex.Count + " searchable prefabs.";
    }

    private void LoadSemanticCache(Dictionary<string, SearchCandidate> candidates)
    {
        string path = Path.Combine(GetProjectRoot(), SemanticCacheRelativePath).Replace('\\', '/');
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            string json = File.ReadAllText(path, Encoding.UTF8);
            SemanticIndexCache cache = JsonUtility.FromJson<SemanticIndexCache>(json);
            if (cache == null || cache.Version != SemanticCacheVersion || cache.Entries == null)
            {
                return;
            }

            for (int i = 0; i < cache.Entries.Count; i++)
            {
                SemanticIndexEntry entry = cache.Entries[i];
                if (entry == null || !IsUiPrefabPath(entry.PrefabPath))
                {
                    continue;
                }

                SearchCandidate candidate = GetOrCreateCandidate(candidates, entry.PrefabPath);
                if (string.IsNullOrEmpty(candidate.Route) && !string.IsNullOrEmpty(entry.Route))
                {
                    candidate.Route = entry.Route;
                }

                candidate.Haystack += " " + entry.Haystack + " " + entry.Route + " " + entry.PrefabPath;
                if (entry.Evidence != null && entry.Evidence.Count > 0 && string.IsNullOrEmpty(candidate.Evidence))
                {
                    candidate.Evidence = entry.Evidence[0];
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[UI Design Board] Could not read semantic cache: " + ex.Message);
        }
    }

    private void AddRawPrefabCandidates(Dictionary<string, SearchCandidate> candidates)
    {
        string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { PrefabRoot });
        Array.Sort(guids, StringComparer.Ordinal);
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]).Replace("\\", "/");
            if (!IsUiPrefabPath(path))
            {
                continue;
            }

            SearchCandidate candidate = GetOrCreateCandidate(candidates, path);
            candidate.Haystack += " " + path + " " + Path.GetFileNameWithoutExtension(path);
        }
    }

    private static SearchCandidate GetOrCreateCandidate(Dictionary<string, SearchCandidate> candidates, string prefabPath)
    {
        SearchCandidate candidate;
        if (candidates.TryGetValue(prefabPath, out candidate))
        {
            return candidate;
        }

        candidate = new SearchCandidate
        {
            PrefabPath = prefabPath,
            Haystack = prefabPath + " " + Path.GetFileNameWithoutExtension(prefabPath)
        };
        candidates[prefabPath] = candidate;
        return candidate;
    }

    private void Search()
    {
        LoadSearchIndex(false);
        searchResults.Clear();

        string query = searchText == null ? string.Empty : searchText.Trim();
        if (string.IsNullOrEmpty(query))
        {
            return;
        }

        string normalized = NormalizeForSearch(query);
        string[] terms = normalized.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

        for (int i = 0; i < searchIndex.Count; i++)
        {
            SearchCandidate candidate = searchIndex[i];
            string haystack = NormalizeForSearch(candidate.Haystack + " " + candidate.Route + " " + candidate.PrefabPath);
            float score = 0f;

            if (!string.IsNullOrEmpty(normalized) && haystack.Contains(normalized))
            {
                score += 120f;
            }

            for (int t = 0; t < terms.Length; t++)
            {
                string term = terms[t];
                if (term.Length <= 1)
                {
                    continue;
                }

                if (haystack.Contains(term))
                {
                    score += 16f;
                }

                if (!string.IsNullOrEmpty(candidate.Route)
                    && NormalizeForSearch(candidate.Route).Contains(term))
                {
                    score += 18f;
                }

                if (NormalizeForSearch(Path.GetFileNameWithoutExtension(candidate.PrefabPath)).Contains(term))
                {
                    score += 22f;
                }
            }

            if (score <= 0f)
            {
                continue;
            }

            searchResults.Add(new SearchResult
            {
                Candidate = candidate,
                Score = score
            });
        }

        searchResults = searchResults
            .OrderByDescending(item => item.Score)
            .ThenBy(item => string.IsNullOrEmpty(item.Candidate.Route) ? 1 : 0)
            .ThenBy(item => item.Candidate.PrefabPath, StringComparer.OrdinalIgnoreCase)
            .Take(MaxSearchResults)
            .ToList();
    }

    private void RefreshArtboardPaths()
    {
        EnsureBoard();
        bool changed = false;

        for (int i = 0; i < board.Artboards.Count; i++)
        {
            UIDesignArtboard artboard = board.Artboards[i];
            if (!string.IsNullOrEmpty(artboard.Guid))
            {
                string path = AssetDatabase.GUIDToAssetPath(artboard.Guid).Replace("\\", "/");
                if (!string.IsNullOrEmpty(path) && path != artboard.PrefabPath)
                {
                    artboard.PrefabPath = path;
                    changed = true;
                }
            }
            else if (!string.IsNullOrEmpty(artboard.PrefabPath))
            {
                string guid = AssetDatabase.AssetPathToGUID(artboard.PrefabPath);
                if (!string.IsNullOrEmpty(guid))
                {
                    artboard.Guid = guid;
                    changed = true;
                }
            }
        }

        if (changed)
        {
            MarkDirty();
        }
    }

    private Texture2D GetPrefabPreview(string prefabPath)
    {
        if (string.IsNullOrEmpty(prefabPath))
        {
            return null;
        }

        Texture2D texture;
        if (previewCache.TryGetValue(prefabPath, out texture) && texture != null)
        {
            return texture;
        }

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

    private void RequestPreview(string prefabPath)
    {
        if (string.IsNullOrEmpty(prefabPath)
            || previewCache.ContainsKey(prefabPath)
            || previewErrors.ContainsKey(prefabPath)
            || queuedPreviewPaths.Contains(prefabPath)
            || GetPrefabPreviewGenerateMethod() == null)
        {
            return;
        }

        queuedPreviewPaths.Add(prefabPath);
        previewQueue.Enqueue(prefabPath);
        SchedulePreviewGeneration();
    }

    private void SchedulePreviewGeneration()
    {
        if (previewGenerationScheduled)
        {
            return;
        }

        previewGenerationScheduled = true;
        EditorApplication.delayCall += ProcessNextPreview;
    }

    private void ProcessNextPreview()
    {
        previewGenerationScheduled = false;

        while (previewQueue.Count > 0)
        {
            string prefabPath = previewQueue.Dequeue();
            queuedPreviewPaths.Remove(prefabPath);

            if (previewCache.ContainsKey(prefabPath) || previewErrors.ContainsKey(prefabPath))
            {
                continue;
            }

            GeneratePreview(prefabPath);
            break;
        }

        if (previewQueue.Count > 0)
        {
            SchedulePreviewGeneration();
        }

        Repaint();
    }

    private void GeneratePreview(string prefabPath)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab == null)
        {
            previewErrors[prefabPath] = "Prefab not found.";
            return;
        }

        Texture2D texture;
        string error;
        if (TryGenerateProjectPreview(prefab, out texture, out error) && texture != null)
        {
            texture.hideFlags = HideFlags.HideAndDontSave;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            previewCache[prefabPath] = texture;
            previewErrors.Remove(prefabPath);
            return;
        }

        if (!string.IsNullOrEmpty(error))
        {
            previewErrors[prefabPath] = error;
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
        if (previewMethodResolved)
        {
            return previewGenerateMethod;
        }

        previewMethodResolved = true;
        Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
        for (int i = 0; i < assemblies.Length; i++)
        {
            Type type = assemblies[i].GetType("UIPrefabPreviewGenerator");
            if (type == null)
            {
                continue;
            }

            previewGenerateMethod = type.GetMethod(
                "Generate",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { typeof(GameObject), typeof(int), typeof(string).MakeByRefType() },
                null);
            if (previewGenerateMethod != null)
            {
                break;
            }
        }

        return previewGenerateMethod;
    }

    private void LoadBoard()
    {
        string path = GetBoardPath();
        if (File.Exists(path))
        {
            try
            {
                string json = File.ReadAllText(path, Encoding.UTF8);
                board = JsonUtility.FromJson<UIDesignBoardState>(json);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[UI Design Board] Could not load board data: " + ex.Message);
            }
        }

        EnsureBoard();
    }

    private void EnsureBoard()
    {
        if (board == null)
        {
            board = new UIDesignBoardState();
        }

        board.Version = BoardVersion;
        if (board.Artboards == null)
        {
            board.Artboards = new List<UIDesignArtboard>();
        }

        if (board.Zoom <= 0f)
        {
            board.Zoom = 1f;
        }

        board.Zoom = Mathf.Clamp(board.Zoom, MinZoom, MaxZoom);
        if (board.Pan == Vector2.zero && board.Artboards.Count == 0)
        {
            board.Pan = new Vector2(80f, 80f);
        }
    }

    private void MarkDirty()
    {
        boardDirty = true;
        if (saveScheduled)
        {
            return;
        }

        saveScheduled = true;
        EditorApplication.delayCall += SaveIfDirty;
    }

    private void SaveIfDirty()
    {
        saveScheduled = false;
        if (!boardDirty || board == null)
        {
            return;
        }

        boardDirty = false;
        try
        {
            string path = GetBoardPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonUtility.ToJson(board, true), Encoding.UTF8);
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[UI Design Board] Could not save board data: " + ex.Message);
        }
    }

    private static string GetBoardPath()
    {
        return Path.Combine(GetProjectRoot(), BoardRelativePath).Replace('\\', '/');
    }

    private static string GetProjectRoot()
    {
        return Directory.GetParent(Application.dataPath).FullName.Replace('\\', '/');
    }

    private static List<string> CollectSelectedUiPrefabPaths()
    {
        List<string> paths = new List<string>();
        HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        string[] selectedGuids = Selection.assetGUIDs;
        for (int i = 0; i < selectedGuids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(selectedGuids[i]).Replace("\\", "/");
            if (IsUiPrefabPath(path))
            {
                AddPath(path, paths, seen);
                continue;
            }

            if (AssetDatabase.IsValidFolder(path)
                && path.StartsWith(PrefabRoot, StringComparison.OrdinalIgnoreCase))
            {
                string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { path });
                Array.Sort(prefabGuids, StringComparer.Ordinal);
                for (int p = 0; p < prefabGuids.Length; p++)
                {
                    string prefabPath = AssetDatabase.GUIDToAssetPath(prefabGuids[p]).Replace("\\", "/");
                    if (IsUiPrefabPath(prefabPath))
                    {
                        AddPath(prefabPath, paths, seen);
                    }
                }
            }
        }

        return paths;
    }

    private static void AddPath(string path, List<string> paths, HashSet<string> seen)
    {
        if (seen.Add(path))
        {
            paths.Add(path);
        }
    }

    private static bool IsUiPrefabPath(string prefabPath)
    {
        return !string.IsNullOrEmpty(prefabPath)
               && prefabPath.StartsWith(PrefabRoot + "/", StringComparison.OrdinalIgnoreCase)
               && prefabPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase);
    }

    private static void OpenPrefab(string prefabPath)
    {
        UnityEngine.Object prefab = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(prefabPath);
        if (prefab == null)
        {
            EditorUtility.DisplayDialog("UI Design Board", "Prefab not found:\n" + prefabPath, "OK");
            return;
        }

        AssetDatabase.OpenAsset(prefab);
    }

    private static void PingPrefab(string prefabPath)
    {
        UnityEngine.Object prefab = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(prefabPath);
        if (prefab == null)
        {
            EditorUtility.DisplayDialog("UI Design Board", "Prefab not found:\n" + prefabPath, "OK");
            return;
        }

        Selection.activeObject = prefab;
        EditorGUIUtility.PingObject(prefab);
    }

    private UIDesignArtboard FindArtboardByGuid(string guid)
    {
        if (string.IsNullOrEmpty(guid))
        {
            return null;
        }

        for (int i = 0; i < board.Artboards.Count; i++)
        {
            if (board.Artboards[i].Guid == guid)
            {
                return board.Artboards[i];
            }
        }

        return null;
    }

    private string ResolvePrefabPath(UIDesignArtboard artboard)
    {
        if (artboard == null)
        {
            return string.Empty;
        }

        if (!string.IsNullOrEmpty(artboard.Guid))
        {
            string path = AssetDatabase.GUIDToAssetPath(artboard.Guid).Replace("\\", "/");
            if (!string.IsNullOrEmpty(path))
            {
                return path;
            }
        }

        return artboard.PrefabPath ?? string.Empty;
    }

    private static string GetDisplayTitle(string prefabPath)
    {
        if (string.IsNullOrEmpty(prefabPath))
        {
            return "(missing prefab)";
        }

        return Path.GetFileNameWithoutExtension(prefabPath);
    }

    private string GetArtboardTitle(UIDesignArtboard artboard)
    {
        if (artboard == null)
        {
            return "(missing artboard)";
        }

        if (!string.IsNullOrEmpty(artboard.Title))
        {
            return artboard.Title;
        }

        return GetDisplayTitle(ResolvePrefabPath(artboard));
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

    private static bool IsSearchCommitKey(KeyCode keyCode)
    {
        return keyCode == KeyCode.Return || keyCode == KeyCode.KeypadEnter;
    }

    private static void DrawBorder(Rect rect, Color color, float thickness)
    {
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, thickness), color);
        EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), color);
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, thickness, rect.height), color);
        EditorGUI.DrawRect(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), color);
    }

    private void DestroyPreviewTextures()
    {
        foreach (Texture2D texture in previewCache.Values)
        {
            if (texture != null)
            {
                DestroyImmediate(texture);
            }
        }

        previewCache.Clear();
        previewErrors.Clear();
        previewQueue.Clear();
        queuedPreviewPaths.Clear();
    }

    private static GUIStyle centeredMiniLabel;
    private static GUIStyle CenteredMiniLabel
    {
        get
        {
            if (centeredMiniLabel == null)
            {
                centeredMiniLabel = new GUIStyle(EditorStyles.miniLabel)
                {
                    alignment = TextAnchor.MiddleCenter
                };
            }

            return centeredMiniLabel;
        }
    }

    [Serializable]
    private class UIDesignBoardState
    {
        public int Version = BoardVersion;
        public Vector2 Pan = new Vector2(80f, 80f);
        public float Zoom = 1f;
        public List<UIDesignArtboard> Artboards = new List<UIDesignArtboard>();
    }

    [Serializable]
    private class UIDesignArtboard
    {
        public string Guid;
        public string PrefabPath;
        public string Title;
        public string Route;
        public string Note;
        public Vector2 Position;
    }

    [Serializable]
    private class SemanticIndexCache
    {
        public int Version;
        public List<SemanticIndexEntry> Entries = new List<SemanticIndexEntry>();
    }

    [Serializable]
    private class SemanticIndexEntry
    {
        public string Route;
        public string PrefabPath;
        public string Haystack;
        public List<string> Evidence = new List<string>();
    }

    private class SearchCandidate
    {
        public string PrefabPath;
        public string Route;
        public string Haystack;
        public string Evidence;
    }

    private class SearchResult
    {
        public SearchCandidate Candidate;
        public float Score;
    }
}
