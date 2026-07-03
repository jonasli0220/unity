using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[InitializeOnLoad]
public static class UIPrefabPreviewProjectOverlay
{
    private const string MenuRoot = "Tools/UI/UI Prefab Preview/";
    private const string EnabledPrefsKey = "Dragon.UIPrefabPreview.Enabled.v1";
    private const string PreviewSizePrefsKey = "Dragon.UIPrefabPreview.Size.v1";
    private const string UiPrefabRoot = "Assets/Content/UI/Prefab/";
    private const string CacheVersion = "v8_text_bounds1";
    private const int DefaultPreviewSize = 256;
    private const int MinProjectIconSize = 34;
    private const double GenerateIntervalSeconds = 0.15d;
    private const double FailedRetryDelaySeconds = 60d;

    private static readonly Dictionary<string, PreviewEntry> MemoryCache = new Dictionary<string, PreviewEntry>();
    private static readonly Queue<string> PendingGuids = new Queue<string>();
    private static readonly HashSet<string> QueuedGuids = new HashSet<string>();

    private static double nextGenerateTime;

    private static bool Enabled
    {
        get { return EditorPrefs.GetBool(EnabledPrefsKey, true); }
        set { EditorPrefs.SetBool(EnabledPrefsKey, value); }
    }

    private static int PreviewSize
    {
        get { return Mathf.Clamp(EditorPrefs.GetInt(PreviewSizePrefsKey, DefaultPreviewSize), 128, 512); }
    }

    static UIPrefabPreviewProjectOverlay()
    {
        EditorApplication.projectWindowItemOnGUI += OnProjectWindowItemGUI;
        EditorApplication.update += ProcessPreviewQueue;
    }

    [MenuItem(MenuRoot + "Project Thumbnails Enabled", false, 100)]
    private static void ToggleProjectThumbnails()
    {
        Enabled = !Enabled;
        EditorApplication.RepaintProjectWindow();
    }

    [MenuItem(MenuRoot + "Project Thumbnails Enabled", true)]
    private static bool ValidateToggleProjectThumbnails()
    {
        Menu.SetChecked(MenuRoot + "Project Thumbnails Enabled", Enabled);
        return true;
    }

    [MenuItem(MenuRoot + "Preview Size/128", false, 130)]
    private static void SetPreviewSize128()
    {
        SetPreviewSize(128);
    }

    [MenuItem(MenuRoot + "Preview Size/128", true)]
    private static bool ValidatePreviewSize128()
    {
        Menu.SetChecked(MenuRoot + "Preview Size/128", PreviewSize == 128);
        return true;
    }

    [MenuItem(MenuRoot + "Preview Size/256", false, 131)]
    private static void SetPreviewSize256()
    {
        SetPreviewSize(256);
    }

    [MenuItem(MenuRoot + "Preview Size/256", true)]
    private static bool ValidatePreviewSize256()
    {
        Menu.SetChecked(MenuRoot + "Preview Size/256", PreviewSize == 256);
        return true;
    }

    [MenuItem(MenuRoot + "Preview Size/512", false, 132)]
    private static void SetPreviewSize512()
    {
        SetPreviewSize(512);
    }

    [MenuItem(MenuRoot + "Preview Size/512", true)]
    private static bool ValidatePreviewSize512()
    {
        Menu.SetChecked(MenuRoot + "Preview Size/512", PreviewSize == 512);
        return true;
    }

    [MenuItem(MenuRoot + "Refresh Selected Previews", false, 160)]
    private static void RefreshSelectedPreviews()
    {
        var paths = CollectSelectedUiPrefabPaths();
        if (paths.Count == 0)
        {
            EditorUtility.DisplayDialog("UI Prefab Preview", "Select a prefab or folder under Assets/Content/UI/Prefab.", "OK");
            return;
        }

        foreach (var path in paths)
        {
            var guid = AssetDatabase.AssetPathToGUID(path);
            ClearMemoryEntry(guid);
            DeleteCachedFilesForGuid(guid);
            Enqueue(guid);
        }

        EditorApplication.RepaintProjectWindow();
    }

    [MenuItem(MenuRoot + "Clear Preview Cache", false, 190)]
    private static void ClearPreviewCache()
    {
        if (!EditorUtility.DisplayDialog("UI Prefab Preview", "Clear all local UI prefab preview cache files?", "Clear", "Cancel"))
        {
            return;
        }

        ClearMemoryCache();
        PendingGuids.Clear();
        QueuedGuids.Clear();

        var cacheRoot = GetCacheRoot();
        if (Directory.Exists(cacheRoot))
        {
            Directory.Delete(cacheRoot, true);
        }

        EditorApplication.RepaintProjectWindow();
    }

    private static void SetPreviewSize(int size)
    {
        EditorPrefs.SetInt(PreviewSizePrefsKey, size);
        ClearMemoryCache();
        EditorApplication.RepaintProjectWindow();
    }

    private static void OnProjectWindowItemGUI(string guid, Rect selectionRect)
    {
        if (!Enabled || Event.current == null || Event.current.type != EventType.Repaint)
        {
            return;
        }

        if (!TryGetUiPrefabPath(guid, out var assetPath))
        {
            return;
        }

        if (!TryGetIconRect(selectionRect, out var iconRect))
        {
            return;
        }

        var status = GetPreviewStatus(guid, assetPath, out var preview);
        if (status == PreviewStatus.Ready && preview != null)
        {
            DrawPreviewTexture(iconRect, preview);
        }
        else if (status == PreviewStatus.Unavailable)
        {
            DrawEmptyPreview(iconRect);
        }
        else if (status == PreviewStatus.Failed)
        {
            DrawFailedBadge(iconRect);
        }
    }

    private static PreviewStatus GetPreviewStatus(string guid, string assetPath, out Texture2D texture)
    {
        texture = null;
        var hash = GetAssetHash(assetPath);
        var cacheKey = BuildCacheKey(guid, hash, PreviewSize);
        var now = EditorApplication.timeSinceStartup;

        if (MemoryCache.TryGetValue(guid, out var entry) && entry.cacheKey == cacheKey)
        {
            if (entry.texture != null)
            {
                texture = entry.texture;
                return PreviewStatus.Ready;
            }

            if (!string.IsNullOrEmpty(entry.error) && now < entry.retryAfter)
            {
                if (IsNoVisiblePreviewError(entry.error))
                {
                    return PreviewStatus.Unavailable;
                }

                return PreviewStatus.Failed;
            }
        }

        var cachePath = GetCachePath(guid, hash, PreviewSize);
        if (File.Exists(cachePath) && TryLoadTexture(cachePath, out texture))
        {
            ReplaceMemoryEntry(guid, new PreviewEntry
            {
                cacheKey = cacheKey,
                texture = texture
            });
            return PreviewStatus.Ready;
        }

        Enqueue(guid);
        return PreviewStatus.Loading;
    }

    private static void ProcessPreviewQueue()
    {
        if (!Enabled || EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            return;
        }

        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            return;
        }

        if (PendingGuids.Count == 0 || EditorApplication.timeSinceStartup < nextGenerateTime)
        {
            return;
        }

        nextGenerateTime = EditorApplication.timeSinceStartup + GenerateIntervalSeconds;
        var guid = PendingGuids.Dequeue();
        QueuedGuids.Remove(guid);

        if (!TryGetUiPrefabPath(guid, out var assetPath))
        {
            return;
        }

        var hash = GetAssetHash(assetPath);
        var cacheKey = BuildCacheKey(guid, hash, PreviewSize);
        if (MemoryCache.TryGetValue(guid, out var existing) && existing.cacheKey == cacheKey && existing.texture != null)
        {
            return;
        }

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
        if (prefab == null)
        {
            SetFailure(guid, cacheKey, "Prefab asset is missing.");
            return;
        }

        string error;
        Texture2D texture;
        using (PrefabHistoryLogic.SuppressRecording())
        {
            texture = UIPrefabPreviewGenerator.Generate(prefab, PreviewSize, out error);
        }

        if (texture == null)
        {
            if (IsNoVisiblePreviewError(error))
            {
                texture = CreateSolidPreviewTexture(PreviewSize, Color.black);
            }
            else
            {
                SetFailure(guid, cacheKey, error);
                return;
            }
        }

        texture.hideFlags = HideFlags.HideAndDontSave;
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Bilinear;

        try
        {
            Directory.CreateDirectory(GetCacheRoot());
            var cachePath = GetCachePath(guid, hash, PreviewSize);
            File.WriteAllBytes(cachePath, texture.EncodeToPNG());
            DeleteOldCachedFiles(guid, cachePath);
        }
        catch (Exception exception)
        {
            Debug.LogWarning("[UI Prefab Preview] Could not save preview cache: " + exception.Message);
        }

        ReplaceMemoryEntry(guid, new PreviewEntry
        {
            cacheKey = cacheKey,
            texture = texture
        });

        EditorApplication.RepaintProjectWindow();
    }

    private static void Enqueue(string guid)
    {
        if (string.IsNullOrEmpty(guid))
        {
            return;
        }

        if (QueuedGuids.Contains(guid))
        {
            return;
        }

        QueuedGuids.Add(guid);
        PendingGuids.Enqueue(guid);
    }

    private static bool TryGetUiPrefabPath(string guid, out string assetPath)
    {
        assetPath = AssetDatabase.GUIDToAssetPath(guid).Replace("\\", "/");
        return IsUiPrefabPath(assetPath);
    }

    private static bool IsUiPrefabPath(string assetPath)
    {
        return !string.IsNullOrEmpty(assetPath)
            && assetPath.StartsWith(UiPrefabRoot, StringComparison.OrdinalIgnoreCase)
            && assetPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryGetIconRect(Rect selectionRect, out Rect iconRect)
    {
        iconRect = Rect.zero;

        var reservedLabelHeight = selectionRect.height > selectionRect.width + 14f ? 18f : 0f;
        var maxWidth = selectionRect.width - 8f;
        var maxHeight = selectionRect.height - reservedLabelHeight - 4f;
        var size = Mathf.Floor(Mathf.Min(maxWidth, maxHeight));
        if (size < MinProjectIconSize)
        {
            return false;
        }

        iconRect = new Rect(
            Mathf.Floor(selectionRect.x + (selectionRect.width - size) * 0.5f),
            Mathf.Floor(selectionRect.y + 2f),
            size,
            size);
        return true;
    }

    private static void DrawPreviewTexture(Rect rect, Texture2D texture)
    {
        EditorGUI.DrawRect(rect, new Color(0.055f, 0.055f, 0.055f, 1f));
        GUI.DrawTexture(rect, texture, ScaleMode.ScaleToFit, true);
        DrawBorder(rect, new Color(0f, 0f, 0f, 0.45f));
    }

    private static void DrawEmptyPreview(Rect rect)
    {
        EditorGUI.DrawRect(rect, Color.black);
        DrawBorder(rect, new Color(0f, 0f, 0f, 0.45f));
    }

    private static void DrawFailedBadge(Rect iconRect)
    {
        var badgeRect = new Rect(iconRect.xMax - 18f, iconRect.y + 2f, 16f, 16f);
        EditorGUI.DrawRect(badgeRect, new Color(0.55f, 0.2f, 0.12f, 0.92f));
        var style = new GUIStyle(EditorStyles.boldLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 11,
            padding = new RectOffset(0, 0, 0, 1)
        };
        style.normal.textColor = Color.white;
        GUI.Label(badgeRect, "!", style);
    }

    private static void DrawBorder(Rect rect, Color color)
    {
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1f), color);
        EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), color);
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, 1f, rect.height), color);
        EditorGUI.DrawRect(new Rect(rect.xMax - 1f, rect.y, 1f, rect.height), color);
    }

    private static bool TryLoadTexture(string path, out Texture2D texture)
    {
        texture = null;
        try
        {
            var bytes = File.ReadAllBytes(path);
            texture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            if (texture.LoadImage(bytes))
            {
                return true;
            }

            DestroyTexture(texture);
            texture = null;
        }
        catch
        {
            texture = null;
        }

        return false;
    }

    private static Texture2D CreateSolidPreviewTexture(int size, Color color)
    {
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            hideFlags = HideFlags.HideAndDontSave,
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };

        var pixels = new Color[size * size];
        for (var i = 0; i < pixels.Length; i++)
        {
            pixels[i] = color;
        }

        texture.SetPixels(pixels);
        texture.Apply();
        return texture;
    }

    private static void SetFailure(string guid, string cacheKey, string error)
    {
        ReplaceMemoryEntry(guid, new PreviewEntry
        {
            cacheKey = cacheKey,
            error = string.IsNullOrEmpty(error) ? "Preview generation failed." : error,
            retryAfter = EditorApplication.timeSinceStartup + FailedRetryDelaySeconds
        });

        if (!IsNoVisiblePreviewError(error))
        {
            Debug.LogWarning("[UI Prefab Preview] " + Path.GetFileName(AssetDatabase.GUIDToAssetPath(guid)) + ": " + error);
        }

        EditorApplication.RepaintProjectWindow();
    }

    private static void ReplaceMemoryEntry(string guid, PreviewEntry entry)
    {
        ClearMemoryEntry(guid);
        MemoryCache[guid] = entry;
    }

    private static void ClearMemoryEntry(string guid)
    {
        if (!MemoryCache.TryGetValue(guid, out var oldEntry))
        {
            return;
        }

        DestroyTexture(oldEntry.texture);
        MemoryCache.Remove(guid);
    }

    private static void ClearMemoryCache()
    {
        foreach (var entry in MemoryCache.Values)
        {
            DestroyTexture(entry.texture);
        }

        MemoryCache.Clear();
    }

    private static void DestroyTexture(Texture2D texture)
    {
        if (texture != null)
        {
            UnityEngine.Object.DestroyImmediate(texture);
        }
    }

    private static List<string> CollectSelectedUiPrefabPaths()
    {
        var paths = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var guid in Selection.assetGUIDs)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid).Replace("\\", "/");
            if (IsUiPrefabPath(path))
            {
                AddPath(path, paths, seen);
                continue;
            }

            if (AssetDatabase.IsValidFolder(path) && path.StartsWith(UiPrefabRoot.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
            {
                var prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { path });
                foreach (var prefabGuid in prefabGuids)
                {
                    var prefabPath = AssetDatabase.GUIDToAssetPath(prefabGuid).Replace("\\", "/");
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

    private static string GetAssetHash(string assetPath)
    {
        return AssetDatabase.GetAssetDependencyHash(assetPath).ToString();
    }

    private static string BuildCacheKey(string guid, string hash, int size)
    {
        return guid + "_" + hash + "_" + size + "_" + CacheVersion;
    }

    private static string GetCacheRoot()
    {
        return Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Library/Dragon/UIPrefabPreviewCache");
    }

    private static string GetCachePath(string guid, string hash, int size)
    {
        return Path.Combine(GetCacheRoot(), BuildCacheKey(guid, hash, size) + ".png");
    }

    private static void DeleteCachedFilesForGuid(string guid)
    {
        var cacheRoot = GetCacheRoot();
        if (!Directory.Exists(cacheRoot))
        {
            return;
        }

        foreach (var path in Directory.GetFiles(cacheRoot, guid + "_*.png"))
        {
            File.Delete(path);
        }
    }

    private static void DeleteOldCachedFiles(string guid, string keepPath)
    {
        var cacheRoot = GetCacheRoot();
        if (!Directory.Exists(cacheRoot))
        {
            return;
        }

        keepPath = Path.GetFullPath(keepPath);
        foreach (var path in Directory.GetFiles(cacheRoot, guid + "_*.png"))
        {
            if (!string.Equals(Path.GetFullPath(path), keepPath, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(path);
            }
        }
    }

    private static bool IsNoVisiblePreviewError(string error)
    {
        return string.Equals(error, "No visible UI graphics found.", StringComparison.Ordinal);
    }

    private enum PreviewStatus
    {
        Loading,
        Ready,
        Unavailable,
        Failed
    }

    private class PreviewEntry
    {
        public string cacheKey;
        public Texture2D texture;
        public string error;
        public double retryAfter;
    }
}

internal static class UIPrefabPreviewGenerator
{
    private static readonly Vector3[] Corners = new Vector3[4];
    private static readonly BindingFlags ComponentMemberFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    public static Texture2D Generate(GameObject prefabAsset, int size, out string error)
    {
        error = string.Empty;
        var previewScene = new Scene();
        var hasPreviewScene = false;
        GameObject canvasObject = null;
        GameObject cameraObject = null;
        RenderTexture renderTexture = null;
        RenderTexture previousActive = null;

        try
        {
            previewScene = EditorSceneManager.NewPreviewScene();
            hasPreviewScene = true;

            var camera = CreateCamera(previewScene, out cameraObject);
            camera.cullingMask = ~0;
            var canvas = CreateCanvas(previewScene, camera, out canvasObject);
            var root = InstantiatePrefab(prefabAsset, previewScene);
            if (root == null)
            {
                error = "Could not instantiate prefab.";
                return null;
            }

            root.hideFlags = HideFlags.HideAndDontSave;
            root.SetActive(true);
            SetLayerRecursively(root, canvas.gameObject.layer);
            ParentToCanvas(root, canvas.transform);
            NormalizeCanvases(root, camera);
            RebuildLayout(root);
            PrepareSpineGraphics(root);
            RebuildLayout(root);
            var hasDefaultActiveGraphics = HasActiveGraphics(root);

            Bounds bounds;
            if (!TryCalculateBounds(root, out bounds))
            {
                error = "Could not calculate UI bounds.";
                return null;
            }

            ConfigureCamera(camera, bounds, size);

            renderTexture = RenderTexture.GetTemporary(size, size, 24, RenderTextureFormat.ARGB32);
            camera.targetTexture = renderTexture;

            previousActive = RenderTexture.active;
            RenderTexture.active = renderTexture;
            GL.Clear(true, true, Color.clear);
            camera.Render();
            RenderTexture.active = renderTexture;

            var output = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            output.ReadPixels(new Rect(0, 0, size, size), 0, 0);
            output.Apply();
            if (HasUsefulPixels(output))
            {
                return output;
            }

            UnityEngine.Object.DestroyImmediate(output);
            if (!hasDefaultActiveGraphics && TryActivateHiddenPreviewState(root))
            {
                RebuildLayout(root);
                PrepareSpineGraphics(root);
                RebuildLayout(root);

                if (TryCalculateBounds(root, out bounds))
                {
                    ConfigureCamera(camera, bounds, size);
                    RenderTexture.active = renderTexture;
                    GL.Clear(true, true, Color.clear);
                    camera.Render();
                    RenderTexture.active = renderTexture;

                    var stateOutput = new Texture2D(size, size, TextureFormat.RGBA32, false)
                    {
                        hideFlags = HideFlags.HideAndDontSave,
                        filterMode = FilterMode.Bilinear,
                        wrapMode = TextureWrapMode.Clamp
                    };
                    stateOutput.ReadPixels(new Rect(0, 0, size, size), 0, 0);
                    stateOutput.Apply();
                    if (HasUsefulPixels(stateOutput))
                    {
                        return stateOutput;
                    }

                    UnityEngine.Object.DestroyImmediate(stateOutput);
                }
            }

            return RenderSoftwarePreview(root, size, out error);
        }
        catch (Exception exception)
        {
            error = exception.Message;
            return null;
        }
        finally
        {
            if (previousActive != null)
            {
                RenderTexture.active = previousActive;
            }
            else
            {
                RenderTexture.active = null;
            }

            if (renderTexture != null)
            {
                RenderTexture.ReleaseTemporary(renderTexture);
            }

            if (cameraObject != null)
            {
                UnityEngine.Object.DestroyImmediate(cameraObject);
            }

            if (canvasObject != null)
            {
                UnityEngine.Object.DestroyImmediate(canvasObject);
            }

            if (hasPreviewScene)
            {
                EditorSceneManager.ClosePreviewScene(previewScene);
            }
        }
    }

    private static Texture2D RenderSoftwarePreview(GameObject root, int size, out string error)
    {
        error = string.Empty;

        var items = CollectDrawItems(root);
        if (items.Count == 0)
        {
            error = "No visible UI graphics found.";
            return null;
        }

        var drawItems = SelectFocusedItems(items);
        var graphicBounds = CalculateBounds(drawItems);
        var textItems = CollectTextItems(root, graphicBounds, false);
        var tmpTextItems = CollectTmpTextItems(root, graphicBounds, false);
        var bounds = CalculateBounds(drawItems, textItems, tmpTextItems);

        var maxBoundsSize = Mathf.Max(bounds.size.x, bounds.size.y, 1f);
        var scale = size * 0.88f / maxBoundsSize;
        var boundsCenter = new Vector2(bounds.center.x, bounds.center.y);
        var pixels = new Color[size * size];
        var readableCopies = new Dictionary<int, Texture2D>();

        FillPixels(pixels, new Color(0.035f, 0.04f, 0.045f, 1f));

        try
        {
            for (var i = 0; i < drawItems.Count; i++)
            {
                DrawSoftwareItem(drawItems[i], pixels, size, scale, boundsCenter, readableCopies);
            }

            for (var i = 0; i < textItems.Count; i++)
            {
                DrawSoftwareTextItem(textItems[i], pixels, size, scale, boundsCenter, readableCopies);
            }

            for (var i = 0; i < tmpTextItems.Count; i++)
            {
                DrawSoftwareTmpTextItem(tmpTextItems[i], pixels, size, scale, boundsCenter, readableCopies);
            }
        }
        finally
        {
            foreach (var texture in readableCopies.Values)
            {
                if (texture != null)
                {
                    UnityEngine.Object.DestroyImmediate(texture);
                }
            }
        }

        var output = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            hideFlags = HideFlags.HideAndDontSave,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        output.SetPixels(pixels);
        output.Apply();
        return output;
    }

    private static List<SoftwareDrawItem> CollectDrawItems(GameObject root)
    {
        var items = new List<SoftwareDrawItem>();
        var graphics = root.GetComponentsInChildren<Graphic>(false);

        for (var i = 0; i < graphics.Length; i++)
        {
            var graphic = graphics[i];
            if (graphic == null || !graphic.enabled || !graphic.gameObject.activeInHierarchy || graphic.color.a <= 0.01f)
            {
                continue;
            }

            if (ShouldHideOwnMaskGraphic(graphic))
            {
                continue;
            }

            if (!TryGetWorldRect(graphic.rectTransform, out var rect) || rect.width < 1f || rect.height < 1f)
            {
                continue;
            }

            var item = new SoftwareDrawItem
            {
                rect = rect,
                color = graphic.color,
                preserveAspect = false,
                drawSolid = true
            };

            if (TryGetInheritedClipRect(graphic.transform, out var clipRect))
            {
                var clippedRect = IntersectRects(item.rect, clipRect);
                if (clippedRect.width < 1f || clippedRect.height < 1f)
                {
                    continue;
                }

                item.clipRect = clipRect;
                item.hasClipRect = true;
            }

            if (TryConfigureSpineItem(graphic, item))
            {
                items.Add(item);
                continue;
            }

            var image = graphic as Image;
            if (image != null)
            {
                item.sprite = image.sprite;
                item.texture = image.sprite != null ? image.sprite.texture : null;
                item.preserveAspect = image.preserveAspect;
                item.drawSolid = image.sprite == null;

                if (item.texture == null && TryGetMaterialPreviewTexture(graphic, out var materialTexture, out var material))
                {
                    item.texture = materialTexture;
                    item.drawSolid = false;
                    item.avoidSolidFallback = true;
                    ApplyMaterialPreviewMode(item, material, true);
                }
                else if (item.texture != null)
                {
                    ApplyMaterialPreviewMode(item, GetRenderableMaterial(graphic), false);
                }
                else if (HasExplicitCustomMaterial(graphic))
                {
                    continue;
                }
            }
            else
            {
                var rawImage = graphic as RawImage;
                if (rawImage != null)
                {
                    item.texture = rawImage.texture;
                    item.sourceRect = rawImage.uvRect;
                    item.hasNormalizedSourceRect = true;
                    item.drawSolid = rawImage.texture == null;

                    if (item.texture == null && TryGetMaterialPreviewTexture(graphic, out var materialTexture, out var material))
                    {
                        item.texture = materialTexture;
                        item.drawSolid = false;
                        item.avoidSolidFallback = true;
                        ApplyMaterialPreviewMode(item, material, true);
                    }
                    else if (item.texture != null)
                    {
                        ApplyMaterialPreviewMode(item, GetRenderableMaterial(graphic), false);
                    }
                    else if (HasExplicitCustomMaterial(graphic))
                    {
                        continue;
                    }
                }
                else
                {
                    continue;
                }
            }

            if (TryGetShaderMask(graphic, out var shaderMask))
            {
                item.shaderMask = shaderMask;
                item.hasShaderMask = true;
            }

            items.Add(item);
        }

        return items;
    }

    private static bool ShouldHideOwnMaskGraphic(Graphic graphic)
    {
        var mask = graphic.GetComponent<Mask>();
        return mask != null && mask.enabled && !mask.showMaskGraphic;
    }

    private static bool TryGetInheritedClipRect(Transform transform, out Rect clipRect)
    {
        clipRect = Rect.zero;
        var hasClipRect = false;
        var current = transform != null ? transform.parent : null;

        while (current != null)
        {
            if (!current.gameObject.activeInHierarchy)
            {
                current = current.parent;
                continue;
            }

            if (TryGetClipRectFromTransform(current, out var currentClipRect))
            {
                if (!hasClipRect)
                {
                    clipRect = currentClipRect;
                    hasClipRect = true;
                }
                else
                {
                    clipRect = IntersectRects(clipRect, currentClipRect);
                }

                if (clipRect.width < 1f || clipRect.height < 1f)
                {
                    return true;
                }
            }

            current = current.parent;
        }

        return hasClipRect;
    }

    private static bool TryGetClipRectFromTransform(Transform transform, out Rect clipRect)
    {
        clipRect = Rect.zero;
        if (transform == null)
        {
            return false;
        }

        var rectTransform = transform as RectTransform;
        if (rectTransform == null)
        {
            return false;
        }

        var rectMask = transform.GetComponent<RectMask2D>();
        if (rectMask != null && rectMask.enabled)
        {
            return TryGetWorldRect(rectTransform, out clipRect) && clipRect.width >= 1f && clipRect.height >= 1f;
        }

        var mask = transform.GetComponent<Mask>();
        if (mask != null && mask.enabled)
        {
            return TryGetWorldRect(rectTransform, out clipRect) && clipRect.width >= 1f && clipRect.height >= 1f;
        }

        if (HasSoftMaskComponent(transform))
        {
            return TryGetWorldRect(rectTransform, out clipRect) && clipRect.width >= 1f && clipRect.height >= 1f;
        }

        return false;
    }

    private static bool HasSoftMaskComponent(Transform transform)
    {
        var components = transform.GetComponents<Component>();
        for (var i = 0; i < components.Length; i++)
        {
            var component = components[i];
            if (component == null)
            {
                continue;
            }

            var typeName = component.GetType().Name;
            if (string.Equals(typeName, "SoftMask", StringComparison.OrdinalIgnoreCase) && IsComponentEnabled(component))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsComponentEnabled(Component component)
    {
        var behaviour = component as Behaviour;
        return behaviour == null || behaviour.enabled;
    }

    private static bool TryGetMaterialPreviewTexture(Graphic graphic, out Texture texture, out Material material)
    {
        texture = null;
        material = GetRenderableMaterial(graphic);
        if (material == null)
        {
            return false;
        }

        if (TryGetMeaningfulTexture(material.mainTexture, out texture))
        {
            return true;
        }

        var texturePropertyNames = new[]
        {
            "_MainTex",
            "_BaseMap",
            "_Texture",
            "_Tex",
            "_AlphaTex",
            "_NoiseTex",
            "_MaskTex"
        };

        for (var i = 0; i < texturePropertyNames.Length; i++)
        {
            if (!material.HasProperty(texturePropertyNames[i]))
            {
                continue;
            }

            if (TryGetMeaningfulTexture(material.GetTexture(texturePropertyNames[i]), out texture))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryGetMeaningfulTexture(Texture candidate, out Texture texture)
    {
        texture = null;
        if (candidate == null || candidate.width <= 0 || candidate.height <= 0)
        {
            return false;
        }

        if (candidate == Texture2D.whiteTexture || candidate == Texture2D.blackTexture)
        {
            return false;
        }

        if (candidate.width <= 2 && candidate.height <= 2)
        {
            return false;
        }

        texture = candidate;
        return true;
    }

    private static Material GetRenderableMaterial(Graphic graphic)
    {
        if (graphic == null)
        {
            return null;
        }

        if (graphic.materialForRendering != null)
        {
            return graphic.materialForRendering;
        }

        return graphic.material;
    }

    private static bool HasExplicitCustomMaterial(Graphic graphic)
    {
        if (graphic == null || graphic.material == null)
        {
            return false;
        }

        if (graphic.material == Graphic.defaultGraphicMaterial)
        {
            return false;
        }

        var materialName = graphic.material.name;
        return string.IsNullOrEmpty(materialName) || materialName.IndexOf("Default UI Material", StringComparison.OrdinalIgnoreCase) < 0;
    }

    private static void ApplyMaterialPreviewMode(SoftwareDrawItem item, Material material, bool materialTextureOnly)
    {
        if (material == null)
        {
            return;
        }

        if (!materialTextureOnly && !IsEffectMaterial(material))
        {
            return;
        }

        item.useLuminanceAlpha = true;
        item.avoidSolidFallback = true;

        if (materialTextureOnly || IsAdditiveMaterial(material))
        {
            item.useAdditiveBlend = true;
        }
    }

    private static bool IsEffectMaterial(Material material)
    {
        if (material == null)
        {
            return false;
        }

        if (IsAdditiveMaterial(material))
        {
            return true;
        }

        var name = (material.name + " " + (material.shader != null ? material.shader.name : string.Empty)).ToLowerInvariant();
        return name.Contains("effect")
            || name.Contains("particle")
            || name.Contains("glow")
            || name.Contains("flow")
            || name.Contains("flash")
            || name.Contains("light")
            || name.Contains("vfx")
            || name.Contains("vx_")
            || name.Contains("add");
    }

    private static bool IsAdditiveMaterial(Material material)
    {
        if (material == null)
        {
            return false;
        }

        if (material.HasProperty("_SrcBlend") && material.HasProperty("_DstBlend"))
        {
            var srcBlend = Mathf.RoundToInt(material.GetFloat("_SrcBlend"));
            var dstBlend = Mathf.RoundToInt(material.GetFloat("_DstBlend"));
            if (srcBlend == (int)UnityEngine.Rendering.BlendMode.One && dstBlend == (int)UnityEngine.Rendering.BlendMode.One)
            {
                return true;
            }
        }

        var name = (material.name + " " + (material.shader != null ? material.shader.name : string.Empty)).ToLowerInvariant();
        if (name.Contains("add") || name.Contains("additive") || name.Contains("screen"))
        {
            return true;
        }

        var keywords = material.shaderKeywords;
        for (var i = 0; i < keywords.Length; i++)
        {
            var keyword = keywords[i];
            if (!string.IsNullOrEmpty(keyword) && keyword.IndexOf("ADD", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    private static void PrepareShaderParamHelpers(GameObject root)
    {
        var components = root.GetComponentsInChildren<Component>(false);
        for (var i = 0; i < components.Length; i++)
        {
            var component = components[i];
            if (component == null || !component.gameObject.activeInHierarchy || !IsComponentEnabled(component))
            {
                continue;
            }

            if (!string.Equals(component.GetType().Name, "UIShaderParamHelper_vx_common_shader", StringComparison.Ordinal))
            {
                continue;
            }

            InvokeComponentMethod(component, "InitMaterial", false);
            InvokeComponentMethod(component, "SetRectForMaskUseMaskTex");
            InvokeComponentMethod(component, "SetRectForMaskUseFillTex");

            var graphic = component.GetComponent<Graphic>();
            if (graphic == null)
            {
                continue;
            }

            graphic.SetVerticesDirty();
            graphic.SetMaterialDirty();
            graphic.SetAllDirty();
            graphic.Rebuild(CanvasUpdate.PreRender);
            if (graphic.materialForRendering != null)
            {
                graphic.canvasRenderer.SetMaterial(graphic.materialForRendering, 0);
            }

            if (graphic.mainTexture != null)
            {
                graphic.canvasRenderer.SetTexture(graphic.mainTexture);
            }
        }

        Canvas.ForceUpdateCanvases();
    }

    private static void PrepareSpineGraphics(GameObject root)
    {
        var graphics = root.GetComponentsInChildren<Graphic>(false);
        for (var i = 0; i < graphics.Length; i++)
        {
            var graphic = graphics[i];
            if (graphic == null || !string.Equals(graphic.GetType().Name, "SkeletonGraphic", StringComparison.Ordinal))
            {
                continue;
            }

            InvokeComponentMethod(graphic, "Initialize", true);
            InvokeComponentMethod(graphic, "Update", 0f);
            InvokeComponentMethod(graphic, "LateUpdate");
            InvokeComponentMethod(graphic, "UpdateMesh");
            graphic.Rebuild(CanvasUpdate.PreRender);
            if (graphic.materialForRendering != null)
            {
                graphic.canvasRenderer.SetMaterial(graphic.materialForRendering, 0);
            }

            if (graphic.mainTexture != null)
            {
                graphic.canvasRenderer.SetTexture(graphic.mainTexture);
            }

            graphic.SetAllDirty();
        }

        Canvas.ForceUpdateCanvases();
    }

    private static bool TryConfigureSpineItem(Graphic graphic, SoftwareDrawItem item)
    {
        if (graphic == null || !string.Equals(graphic.GetType().Name, "SkeletonGraphic", StringComparison.Ordinal))
        {
            return false;
        }

        InvokeComponentMethod(graphic, "Initialize", true);
        InvokeComponentMethod(graphic, "Update", 0f);
        InvokeComponentMethod(graphic, "UpdateMesh");

        var mesh = InvokeComponentObjectMethod<Mesh>(graphic, "GetLastMesh");
        if (mesh == null || mesh.vertexCount == 0)
        {
            return false;
        }

        var texture = graphic.mainTexture;
        if (texture == null && graphic.materialForRendering != null)
        {
            texture = graphic.materialForRendering.mainTexture;
        }

        if (texture == null && graphic.material != null)
        {
            texture = graphic.material.mainTexture;
        }

        if (texture == null || texture.width <= 0 || texture.height <= 0)
        {
            return false;
        }

        item.mesh = mesh;
        item.meshTexture = texture;
        item.localToWorldMatrix = graphic.rectTransform.localToWorldMatrix;
        if (TryCalculateMeshWorldRect(mesh, item.localToWorldMatrix, out var meshRect))
        {
            item.rect = meshRect;
        }

        item.drawMesh = true;
        item.drawSolid = false;
        return true;
    }

    private static bool TryCalculateMeshWorldRect(Mesh mesh, Matrix4x4 localToWorldMatrix, out Rect rect)
    {
        rect = Rect.zero;
        if (mesh == null || mesh.vertexCount == 0)
        {
            return false;
        }

        var vertices = mesh.vertices;
        if (vertices == null || vertices.Length == 0)
        {
            return false;
        }

        var first = localToWorldMatrix.MultiplyPoint3x4(vertices[0]);
        var minX = first.x;
        var maxX = first.x;
        var minY = first.y;
        var maxY = first.y;

        for (var i = 1; i < vertices.Length; i++)
        {
            var point = localToWorldMatrix.MultiplyPoint3x4(vertices[i]);
            minX = Mathf.Min(minX, point.x);
            maxX = Mathf.Max(maxX, point.x);
            minY = Mathf.Min(minY, point.y);
            maxY = Mathf.Max(maxY, point.y);
        }

        rect = Rect.MinMaxRect(minX, minY, maxX, maxY);
        return rect.width >= 1f && rect.height >= 1f;
    }

    private static void InvokeComponentMethod(Component component, string methodName, params object[] parameters)
    {
        try
        {
            var parameterTypes = new Type[parameters.Length];
            for (var i = 0; i < parameters.Length; i++)
            {
                parameterTypes[i] = parameters[i].GetType();
            }

            var method = component.GetType().GetMethod(methodName, ComponentMemberFlags, null, parameterTypes, null);
            if (method != null)
            {
                method.Invoke(component, parameters);
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning("[UI Prefab Preview] Could not prepare " + component.GetType().Name + "." + methodName + ": " + exception.Message);
        }
    }

    private static T InvokeComponentObjectMethod<T>(Component component, string methodName) where T : UnityEngine.Object
    {
        try
        {
            var method = component.GetType().GetMethod(methodName, ComponentMemberFlags, null, Type.EmptyTypes, null);
            if (method == null)
            {
                return null;
            }

            return method.Invoke(component, null) as T;
        }
        catch (Exception exception)
        {
            Debug.LogWarning("[UI Prefab Preview] Could not read " + component.GetType().Name + "." + methodName + ": " + exception.Message);
            return null;
        }
    }

    private static List<SoftwareDrawItem> SelectFocusedItems(List<SoftwareDrawItem> items)
    {
        var allBounds = CalculateBounds(items);
        if (allBounds.size.x < 600f && allBounds.size.y < 400f)
        {
            return items;
        }

        var focusedItems = new List<SoftwareDrawItem>();

        for (var i = 0; i < items.Count; i++)
        {
            if (!IsBackdropItem(items[i], allBounds))
            {
                focusedItems.Add(items[i]);
            }
        }

        if (focusedItems.Count == 0)
        {
            return items;
        }

        var focusedBounds = CalculateBounds(focusedItems);
        var widthRatio = focusedBounds.size.x / Mathf.Max(allBounds.size.x, 1f);
        var heightRatio = focusedBounds.size.y / Mathf.Max(allBounds.size.y, 1f);

        if (widthRatio < 0.86f || heightRatio < 0.86f)
        {
            return focusedItems;
        }

        return items;
    }

    private static bool IsBackdropItem(SoftwareDrawItem item, Bounds allBounds)
    {
        var boundsWidth = Mathf.Max(allBounds.size.x, 1f);
        var boundsHeight = Mathf.Max(allBounds.size.y, 1f);
        var widthRatio = item.rect.width / boundsWidth;
        var heightRatio = item.rect.height / boundsHeight;

        if (widthRatio < 0.78f || heightRatio < 0.78f)
        {
            return false;
        }

        if (item.drawMesh)
        {
            return false;
        }

        if (item.sprite == null && item.texture == null)
        {
            return true;
        }

        var color = item.color;
        var isDarkOrDim = color.a <= 0.82f || (color.r + color.g + color.b) <= 0.45f;
        var isOversized = widthRatio >= 0.92f || heightRatio >= 0.92f;
        return isOversized && isDarkOrDim;
    }

    private static Bounds CalculateBounds(List<SoftwareDrawItem> items)
    {
        var bounds = new Bounds(new Vector3(items[0].rect.center.x, items[0].rect.center.y, 0f), Vector3.zero);

        for (var i = 0; i < items.Count; i++)
        {
            var rect = items[i].rect;
            bounds.Encapsulate(new Vector3(rect.xMin, rect.yMin, 0f));
            bounds.Encapsulate(new Vector3(rect.xMax, rect.yMax, 0f));
        }

        return bounds;
    }

    private static Bounds CalculateBounds(
        List<SoftwareDrawItem> drawItems,
        List<SoftwareTextItem> textItems,
        List<SoftwareTmpTextItem> tmpTextItems)
    {
        var bounds = CalculateBounds(drawItems);

        for (var i = 0; i < textItems.Count; i++)
        {
            EncapsulateRect(ref bounds, textItems[i].rect);
        }

        for (var i = 0; i < tmpTextItems.Count; i++)
        {
            EncapsulateRect(ref bounds, tmpTextItems[i].rect);
        }

        return bounds;
    }

    private static void EncapsulateRect(ref Bounds bounds, Rect rect)
    {
        if (rect.width <= 0.01f || rect.height <= 0.01f)
        {
            return;
        }

        bounds.Encapsulate(new Vector3(rect.xMin, rect.yMin, 0f));
        bounds.Encapsulate(new Vector3(rect.xMax, rect.yMax, 0f));
    }

    private static void DrawSoftwareItem(
        SoftwareDrawItem item,
        Color[] pixels,
        int size,
        float scale,
        Vector2 boundsCenter,
        Dictionary<int, Texture2D> readableCopies)
    {
        var dstRect = MapWorldRectToTexture(item.rect, size, scale, boundsCenter);
        if (dstRect.width < 1f || dstRect.height < 1f)
        {
            return;
        }

        if (item.drawMesh)
        {
            DrawMeshItem(item, pixels, size, scale, boundsCenter, readableCopies);
            return;
        }

        if (item.drawSolid || item.texture == null)
        {
            if (TryApplyClipRect(item, size, scale, boundsCenter, dstRect, out var clippedSolidRect))
            {
                DrawSolidRect(pixels, size, clippedSolidRect, item.color);
            }

            return;
        }

        var readable = GetReadableCopy(item.texture, readableCopies);
        if (readable == null)
        {
            if (!item.avoidSolidFallback && TryApplyClipRect(item, size, scale, boundsCenter, dstRect, out var clippedSolidFallbackRect))
            {
                DrawSolidRect(pixels, size, clippedSolidFallbackRect, item.color);
            }

            return;
        }

        var sourceRect = GetSourceRect(item, readable);
        if (sourceRect.width < 1f || sourceRect.height < 1f)
        {
            return;
        }

        if (item.preserveAspect)
        {
            dstRect = FitAspect(dstRect, sourceRect.width / sourceRect.height);
        }

        if (TryApplyClipRect(item, size, scale, boundsCenter, dstRect, out var clippedDstRect))
        {
            var clippedSourceRect = CropSourceRect(sourceRect, dstRect, clippedDstRect);
            var maskReadable = item.hasShaderMask ? GetReadableCopy(item.shaderMask.texture, readableCopies) : null;
            DrawTextureRect(
                pixels,
                size,
                clippedDstRect,
                readable,
                clippedSourceRect,
                item.color,
                maskReadable,
                item.shaderMask,
                item.hasShaderMask,
                item.useLuminanceAlpha,
                item.useAdditiveBlend);
        }
    }

    private static void DrawMeshItem(
        SoftwareDrawItem item,
        Color[] pixels,
        int size,
        float scale,
        Vector2 boundsCenter,
        Dictionary<int, Texture2D> readableCopies)
    {
        var readable = GetReadableCopy(item.meshTexture, readableCopies);
        if (readable == null || item.mesh == null)
        {
            return;
        }

        var vertices = item.mesh.vertices;
        var uvs = item.mesh.uv;
        var triangles = item.mesh.triangles;
        if (vertices == null || uvs == null || triangles == null || vertices.Length == 0 || uvs.Length < vertices.Length)
        {
            return;
        }

        var colors32 = item.mesh.colors32;
        var clipRect = new Rect(0f, 0f, size, size);
        if (item.hasClipRect)
        {
            clipRect = IntersectRects(clipRect, MapWorldRectToTexture(item.clipRect, size, scale, boundsCenter));
            if (clipRect.width < 1f || clipRect.height < 1f)
            {
                return;
            }
        }

        for (var i = 0; i + 2 < triangles.Length; i += 3)
        {
            var index0 = triangles[i];
            var index1 = triangles[i + 1];
            var index2 = triangles[i + 2];
            if (index0 < 0 || index1 < 0 || index2 < 0 || index0 >= vertices.Length || index1 >= vertices.Length || index2 >= vertices.Length)
            {
                continue;
            }

            var p0 = MapWorldPointToTexture(item.localToWorldMatrix.MultiplyPoint3x4(vertices[index0]), size, scale, boundsCenter);
            var p1 = MapWorldPointToTexture(item.localToWorldMatrix.MultiplyPoint3x4(vertices[index1]), size, scale, boundsCenter);
            var p2 = MapWorldPointToTexture(item.localToWorldMatrix.MultiplyPoint3x4(vertices[index2]), size, scale, boundsCenter);

            DrawTexturedTriangle(
                pixels,
                size,
                readable,
                clipRect,
                p0,
                p1,
                p2,
                uvs[index0],
                uvs[index1],
                uvs[index2],
                GetMeshVertexColor(colors32, index0),
                GetMeshVertexColor(colors32, index1),
                GetMeshVertexColor(colors32, index2),
                item.color);
        }
    }

    private static bool TryGetWorldRect(RectTransform rectTransform, out Rect rect)
    {
        rect = Rect.zero;
        if (rectTransform == null)
        {
            return false;
        }

        rectTransform.GetWorldCorners(Corners);
        var minX = Corners[0].x;
        var maxX = Corners[0].x;
        var minY = Corners[0].y;
        var maxY = Corners[0].y;

        for (var i = 1; i < Corners.Length; i++)
        {
            minX = Mathf.Min(minX, Corners[i].x);
            maxX = Mathf.Max(maxX, Corners[i].x);
            minY = Mathf.Min(minY, Corners[i].y);
            maxY = Mathf.Max(maxY, Corners[i].y);
        }

        rect = Rect.MinMaxRect(minX, minY, maxX, maxY);
        return true;
    }

    private static Rect MapWorldRectToTexture(Rect worldRect, int size, float scale, Vector2 boundsCenter)
    {
        var xMin = (worldRect.xMin - boundsCenter.x) * scale + size * 0.5f;
        var xMax = (worldRect.xMax - boundsCenter.x) * scale + size * 0.5f;
        var yMin = (worldRect.yMin - boundsCenter.y) * scale + size * 0.5f;
        var yMax = (worldRect.yMax - boundsCenter.y) * scale + size * 0.5f;
        return Rect.MinMaxRect(
            Mathf.Clamp(Mathf.Min(xMin, xMax), 0f, size),
            Mathf.Clamp(Mathf.Min(yMin, yMax), 0f, size),
            Mathf.Clamp(Mathf.Max(xMin, xMax), 0f, size),
            Mathf.Clamp(Mathf.Max(yMin, yMax), 0f, size));
    }

    private static Vector2 MapWorldPointToTexture(Vector3 worldPoint, int size, float scale, Vector2 boundsCenter)
    {
        return new Vector2(
            (worldPoint.x - boundsCenter.x) * scale + size * 0.5f,
            (worldPoint.y - boundsCenter.y) * scale + size * 0.5f);
    }

    private static bool TryApplyClipRect(
        SoftwareDrawItem item,
        int size,
        float scale,
        Vector2 boundsCenter,
        Rect dstRect,
        out Rect clippedDstRect)
    {
        clippedDstRect = dstRect;
        if (!item.hasClipRect)
        {
            return dstRect.width >= 1f && dstRect.height >= 1f;
        }

        var clipDstRect = MapWorldRectToTexture(item.clipRect, size, scale, boundsCenter);
        clippedDstRect = IntersectRects(dstRect, clipDstRect);
        return clippedDstRect.width >= 1f && clippedDstRect.height >= 1f;
    }

    private static Rect IntersectRects(Rect a, Rect b)
    {
        var xMin = Mathf.Max(a.xMin, b.xMin);
        var yMin = Mathf.Max(a.yMin, b.yMin);
        var xMax = Mathf.Min(a.xMax, b.xMax);
        var yMax = Mathf.Min(a.yMax, b.yMax);

        if (xMax <= xMin || yMax <= yMin)
        {
            return Rect.zero;
        }

        return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
    }

    private static Rect FitAspect(Rect rect, float aspect)
    {
        if (aspect <= 0f || rect.width <= 0f || rect.height <= 0f)
        {
            return rect;
        }

        var currentAspect = rect.width / rect.height;
        if (currentAspect > aspect)
        {
            var width = rect.height * aspect;
            rect.x += (rect.width - width) * 0.5f;
            rect.width = width;
        }
        else
        {
            var height = rect.width / aspect;
            rect.y += (rect.height - height) * 0.5f;
            rect.height = height;
        }

        return rect;
    }

    private static Rect GetSourceRect(SoftwareDrawItem item, Texture2D readable)
    {
        if (item.sprite != null)
        {
            try
            {
                return item.sprite.textureRect;
            }
            catch
            {
                return item.sprite.rect;
            }
        }

        if (item.hasNormalizedSourceRect)
        {
            return new Rect(
                item.sourceRect.x * readable.width,
                item.sourceRect.y * readable.height,
                item.sourceRect.width * readable.width,
                item.sourceRect.height * readable.height);
        }

        return new Rect(0f, 0f, readable.width, readable.height);
    }

    private static Rect CropSourceRect(Rect sourceRect, Rect originalDstRect, Rect clippedDstRect)
    {
        if (originalDstRect.width <= 0f || originalDstRect.height <= 0f)
        {
            return sourceRect;
        }

        var uMin = Mathf.Clamp01((clippedDstRect.xMin - originalDstRect.xMin) / originalDstRect.width);
        var uMax = Mathf.Clamp01((clippedDstRect.xMax - originalDstRect.xMin) / originalDstRect.width);
        var vMin = Mathf.Clamp01((clippedDstRect.yMin - originalDstRect.yMin) / originalDstRect.height);
        var vMax = Mathf.Clamp01((clippedDstRect.yMax - originalDstRect.yMin) / originalDstRect.height);

        return Rect.MinMaxRect(
            sourceRect.xMin + sourceRect.width * uMin,
            sourceRect.yMin + sourceRect.height * vMin,
            sourceRect.xMin + sourceRect.width * uMax,
            sourceRect.yMin + sourceRect.height * vMax);
    }

    private static Texture2D GetReadableCopy(Texture texture, Dictionary<int, Texture2D> readableCopies)
    {
        if (texture == null || texture.width <= 0 || texture.height <= 0)
        {
            return null;
        }

        var id = texture.GetInstanceID();
        if (readableCopies.TryGetValue(id, out var cached))
        {
            return cached;
        }

        var previous = RenderTexture.active;
        var renderTexture = RenderTexture.GetTemporary(texture.width, texture.height, 0, RenderTextureFormat.ARGB32);
        try
        {
            Graphics.Blit(texture, renderTexture);
            RenderTexture.active = renderTexture;
            var readable = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            readable.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
            readable.Apply();
            readableCopies[id] = readable;
            return readable;
        }
        catch
        {
            return null;
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(renderTexture);
        }
    }

    private static void DrawSolidRect(Color[] pixels, int size, Rect rect, Color color)
    {
        var xMin = Mathf.Clamp(Mathf.FloorToInt(rect.xMin), 0, size - 1);
        var xMax = Mathf.Clamp(Mathf.CeilToInt(rect.xMax), 0, size);
        var yMin = Mathf.Clamp(Mathf.FloorToInt(rect.yMin), 0, size - 1);
        var yMax = Mathf.Clamp(Mathf.CeilToInt(rect.yMax), 0, size);

        for (var y = yMin; y < yMax; y++)
        {
            for (var x = xMin; x < xMax; x++)
            {
                BlendPixel(pixels, size, x, y, color);
            }
        }
    }

    private static void FillPixels(Color[] pixels, Color color)
    {
        for (var i = 0; i < pixels.Length; i++)
        {
            pixels[i] = color;
        }
    }

    private static void DrawTextureRect(
        Color[] pixels,
        int size,
        Rect rect,
        Texture2D source,
        Rect sourceRect,
        Color tint,
        Texture2D mask,
        ShaderMaskInfo shaderMask,
        bool hasShaderMask,
        bool useLuminanceAlpha,
        bool useAdditiveBlend)
    {
        var xMin = Mathf.Clamp(Mathf.FloorToInt(rect.xMin), 0, size - 1);
        var xMax = Mathf.Clamp(Mathf.CeilToInt(rect.xMax), 0, size);
        var yMin = Mathf.Clamp(Mathf.FloorToInt(rect.yMin), 0, size - 1);
        var yMax = Mathf.Clamp(Mathf.CeilToInt(rect.yMax), 0, size);

        for (var y = yMin; y < yMax; y++)
        {
            var v = Mathf.Clamp01((y + 0.5f - rect.yMin) / rect.height);
            var sourceFloatY = Mathf.Clamp(sourceRect.y + v * sourceRect.height, 0f, source.height - 1f);
            var sourceY = Mathf.Clamp(Mathf.FloorToInt(sourceFloatY), 0, source.height - 1);

            for (var x = xMin; x < xMax; x++)
            {
                var u = Mathf.Clamp01((x + 0.5f - rect.xMin) / rect.width);
                var sourceFloatX = Mathf.Clamp(sourceRect.x + u * sourceRect.width, 0f, source.width - 1f);
                var sourceX = Mathf.Clamp(Mathf.FloorToInt(sourceFloatX), 0, source.width - 1);
                var color = source.GetPixel(sourceX, sourceY);
                color.r *= tint.r;
                color.g *= tint.g;
                color.b *= tint.b;
                color.a *= tint.a;

                if (useLuminanceAlpha)
                {
                    var luminance = Mathf.Max(color.r, color.g, color.b);
                    color.a *= Mathf.Clamp01(luminance);
                }

                if (hasShaderMask && mask != null)
                {
                    color.a *= SampleShaderMaskAlpha(
                        mask,
                        (sourceFloatX + 0.5f) / Mathf.Max(source.width, 1),
                        (sourceFloatY + 0.5f) / Mathf.Max(source.height, 1),
                        shaderMask);
                }

                BlendPixel(pixels, size, x, y, color, useAdditiveBlend);
            }
        }
    }

    private static void DrawTexturedTriangle(
        Color[] pixels,
        int size,
        Texture2D source,
        Rect clipRect,
        Vector2 p0,
        Vector2 p1,
        Vector2 p2,
        Vector2 uv0,
        Vector2 uv1,
        Vector2 uv2,
        Color c0,
        Color c1,
        Color c2,
        Color tint)
    {
        var area = Edge(p0, p1, p2);
        if (Mathf.Abs(area) <= 0.0001f)
        {
            return;
        }

        var xMin = Mathf.Clamp(Mathf.FloorToInt(Mathf.Max(Mathf.Min(p0.x, p1.x, p2.x), clipRect.xMin)), 0, size - 1);
        var xMax = Mathf.Clamp(Mathf.CeilToInt(Mathf.Min(Mathf.Max(p0.x, p1.x, p2.x), clipRect.xMax)), 0, size);
        var yMin = Mathf.Clamp(Mathf.FloorToInt(Mathf.Max(Mathf.Min(p0.y, p1.y, p2.y), clipRect.yMin)), 0, size - 1);
        var yMax = Mathf.Clamp(Mathf.CeilToInt(Mathf.Min(Mathf.Max(p0.y, p1.y, p2.y), clipRect.yMax)), 0, size);

        for (var y = yMin; y < yMax; y++)
        {
            for (var x = xMin; x < xMax; x++)
            {
                var p = new Vector2(x + 0.5f, y + 0.5f);
                var w0 = Edge(p1, p2, p) / area;
                var w1 = Edge(p2, p0, p) / area;
                var w2 = Edge(p0, p1, p) / area;
                if (w0 < -0.0001f || w1 < -0.0001f || w2 < -0.0001f)
                {
                    continue;
                }

                var uv = uv0 * w0 + uv1 * w1 + uv2 * w2;
                var sourceX = Mathf.Clamp(Mathf.FloorToInt(uv.x * source.width), 0, source.width - 1);
                var sourceY = Mathf.Clamp(Mathf.FloorToInt(uv.y * source.height), 0, source.height - 1);
                var textureColor = source.GetPixel(sourceX, sourceY);
                var vertexColor = c0 * w0 + c1 * w1 + c2 * w2;
                var color = new Color(
                    textureColor.r * vertexColor.r * tint.r,
                    textureColor.g * vertexColor.g * tint.g,
                    textureColor.b * vertexColor.b * tint.b,
                    textureColor.a * vertexColor.a * tint.a);

                BlendPixel(pixels, size, x, y, color);
            }
        }
    }

    private static float Edge(Vector2 a, Vector2 b, Vector2 c)
    {
        return (c.x - a.x) * (b.y - a.y) - (c.y - a.y) * (b.x - a.x);
    }

    private static Color GetMeshVertexColor(Color32[] colors32, int index)
    {
        if (colors32 == null || index < 0 || index >= colors32.Length)
        {
            return Color.white;
        }

        return colors32[index];
    }

    private static bool TryGetShaderMask(Graphic graphic, out ShaderMaskInfo shaderMask)
    {
        shaderMask = default(ShaderMaskInfo);
        if (graphic == null)
        {
            return false;
        }

        var components = graphic.GetComponents<Component>();
        for (var i = 0; i < components.Length; i++)
        {
            var component = components[i];
            if (component == null || !IsComponentEnabled(component))
            {
                continue;
            }

            var typeName = component.GetType().Name;
            if (!string.Equals(typeName, "UIShaderParamHelper_vx_common_shader", StringComparison.Ordinal))
            {
                continue;
            }

            var texture = ReadObjectMember<Texture>(component, "MaskTex");
            if (texture == null)
            {
                texture = ReadObjectMember<Texture>(component, "_MaskTex");
            }

            if (texture == null || texture.width <= 0 || texture.height <= 0)
            {
                continue;
            }

            shaderMask = new ShaderMaskInfo
            {
                texture = texture,
                inverse = ReadBoolMember(component, "MaskInverse") || ReadBoolMember(component, "_MaskInverse"),
                textureScale = GetMaskTextureScale(GetRenderableMaterial(graphic)),
                textureOffset = GetMaskTextureOffset(GetRenderableMaterial(graphic)),
                uvOffset = new Vector2(
                    ReadFloatMember(component, "MaskUOffset", "_MaskUOffset"),
                    ReadFloatMember(component, "MaskVOffset", "_MaskVOffset"))
            };
            return true;
        }

        var material = graphic.materialForRendering != null ? graphic.materialForRendering : graphic.material;
        if (material != null && material.HasProperty("_MaskTex"))
        {
            var texture = material.GetTexture("_MaskTex");
            if (texture != null && texture.width > 0 && texture.height > 0)
            {
                shaderMask = new ShaderMaskInfo
                {
                    texture = texture,
                    inverse = material.HasProperty("_MaskInverse") && material.GetFloat("_MaskInverse") > 0.5f,
                    textureScale = GetMaskTextureScale(material),
                    textureOffset = GetMaskTextureOffset(material),
                    uvOffset = new Vector2(
                        material.HasProperty("_MaskUOffset") ? material.GetFloat("_MaskUOffset") : 0f,
                        material.HasProperty("_MaskVOffset") ? material.GetFloat("_MaskVOffset") : 0f)
                };
                return true;
            }
        }

        return false;
    }

    private static T ReadObjectMember<T>(Component component, string memberName) where T : UnityEngine.Object
    {
        var type = component.GetType();
        var property = type.GetProperty(memberName, ComponentMemberFlags);
        if (property != null && typeof(T).IsAssignableFrom(property.PropertyType))
        {
            return property.GetValue(component, null) as T;
        }

        var field = type.GetField(memberName, ComponentMemberFlags);
        if (field != null && typeof(T).IsAssignableFrom(field.FieldType))
        {
            return field.GetValue(component) as T;
        }

        return null;
    }

    private static bool ReadBoolMember(Component component, string memberName)
    {
        var type = component.GetType();
        var property = type.GetProperty(memberName, ComponentMemberFlags);
        if (property != null && property.PropertyType == typeof(bool))
        {
            return (bool)property.GetValue(component, null);
        }

        var field = type.GetField(memberName, ComponentMemberFlags);
        if (field != null && field.FieldType == typeof(bool))
        {
            return (bool)field.GetValue(component);
        }

        return false;
    }

    private static float ReadFloatMember(Component component, string publicName, string fieldName)
    {
        if (TryReadFloatMember(component, publicName, out var value))
        {
            return value;
        }

        return TryReadFloatMember(component, fieldName, out value) ? value : 0f;
    }

    private static bool TryReadFloatMember(Component component, string memberName, out float value)
    {
        value = 0f;
        var type = component.GetType();
        var property = type.GetProperty(memberName, ComponentMemberFlags);
        if (property != null && property.PropertyType == typeof(float))
        {
            value = (float)property.GetValue(component, null);
            return true;
        }

        var field = type.GetField(memberName, ComponentMemberFlags);
        if (field != null && field.FieldType == typeof(float))
        {
            value = (float)field.GetValue(component);
            return true;
        }

        return false;
    }

    private static Vector2 GetMaskTextureScale(Material material)
    {
        if (material != null && material.HasProperty("_MaskTex"))
        {
            return material.GetTextureScale("_MaskTex");
        }

        return Vector2.one;
    }

    private static Vector2 GetMaskTextureOffset(Material material)
    {
        if (material != null && material.HasProperty("_MaskTex"))
        {
            return material.GetTextureOffset("_MaskTex");
        }

        return Vector2.zero;
    }

    private static float SampleShaderMaskAlpha(Texture2D mask, float sourceU, float sourceV, ShaderMaskInfo shaderMask)
    {
        var u = (sourceU + shaderMask.uvOffset.x) * shaderMask.textureScale.x + shaderMask.textureOffset.x;
        var v = (sourceV + shaderMask.uvOffset.y) * shaderMask.textureScale.y + shaderMask.textureOffset.y;
        var x = SampleTextureCoordinate(u, mask.width, mask.wrapMode);
        var y = SampleTextureCoordinate(v, mask.height, mask.wrapMode);
        var color = mask.GetPixel(x, y);
        var maskAlpha = color.a < 0.999f ? color.a : color.r;
        if (shaderMask.inverse)
        {
            maskAlpha = 1f - maskAlpha;
        }

        return Mathf.Clamp01(maskAlpha);
    }

    private static int SampleTextureCoordinate(float coordinate, int size, TextureWrapMode wrapMode)
    {
        if (size <= 1)
        {
            return 0;
        }

        if (wrapMode == TextureWrapMode.Repeat)
        {
            coordinate -= Mathf.Floor(coordinate);
        }
        else if (wrapMode == TextureWrapMode.Mirror || wrapMode == TextureWrapMode.MirrorOnce)
        {
            var mirrored = coordinate - Mathf.Floor(coordinate * 0.5f) * 2f;
            coordinate = mirrored <= 1f ? mirrored : 2f - mirrored;
        }
        else
        {
            coordinate = Mathf.Clamp01(coordinate);
        }

        return Mathf.Clamp(Mathf.FloorToInt(coordinate * size), 0, size - 1);
    }

    private static void BlendPixel(Color[] pixels, int size, int x, int y, Color source)
    {
        BlendPixel(pixels, size, x, y, source, false);
    }

    private static void BlendPixel(Color[] pixels, int size, int x, int y, Color source, bool additive)
    {
        if (source.a <= 0.001f)
        {
            return;
        }

        var index = y * size + x;
        var destination = pixels[index];
        var sourceAlpha = Mathf.Clamp01(source.a);

        if (additive)
        {
            pixels[index] = new Color(
                Mathf.Clamp01(destination.r + source.r * sourceAlpha),
                Mathf.Clamp01(destination.g + source.g * sourceAlpha),
                Mathf.Clamp01(destination.b + source.b * sourceAlpha),
                Mathf.Clamp01(Mathf.Max(destination.a, sourceAlpha)));
            return;
        }

        var destinationAlpha = Mathf.Clamp01(destination.a);
        var outputAlpha = sourceAlpha + destinationAlpha * (1f - sourceAlpha);

        if (outputAlpha <= 0.001f)
        {
            pixels[index] = Color.clear;
            return;
        }

        var destinationFactor = destinationAlpha * (1f - sourceAlpha);
        pixels[index] = new Color(
            (source.r * sourceAlpha + destination.r * destinationFactor) / outputAlpha,
            (source.g * sourceAlpha + destination.g * destinationFactor) / outputAlpha,
            (source.b * sourceAlpha + destination.b * destinationFactor) / outputAlpha,
            outputAlpha);
    }

    private static bool HasUsefulPixels(Texture2D texture)
    {
        if (texture == null)
        {
            return false;
        }

        var usefulPixelCount = 0;
        var requiredUsefulPixels = Mathf.Max(12, texture.width * texture.height / 4096);

        for (var y = 0; y < texture.height; y++)
        {
            for (var x = 0; x < texture.width; x++)
            {
                var color = texture.GetPixel(x, y);
                if (color.a > 0.02f && color.r + color.g + color.b > 0.025f)
                {
                    usefulPixelCount++;
                    if (usefulPixelCount >= requiredUsefulPixels)
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static Camera CreateCamera(Scene scene, out GameObject cameraObject)
    {
        cameraObject = new GameObject("UI Prefab Preview Camera")
        {
            hideFlags = HideFlags.HideAndDontSave
        };
        SceneManager.MoveGameObjectToScene(cameraObject, scene);

        var camera = cameraObject.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.clear;
        camera.orthographic = true;
        camera.nearClipPlane = 0.1f;
        camera.farClipPlane = 2000f;
        camera.cullingMask = -1;
        camera.enabled = true;
        return camera;
    }

    private static Canvas CreateCanvas(Scene scene, Camera camera, out GameObject canvasObject)
    {
        canvasObject = new GameObject("UI Prefab Preview Canvas", typeof(RectTransform), typeof(Canvas))
        {
            hideFlags = HideFlags.HideAndDontSave
        };
        SceneManager.MoveGameObjectToScene(canvasObject, scene);

        var rectTransform = canvasObject.GetComponent<RectTransform>();
        rectTransform.sizeDelta = new Vector2(1920f, 1080f);
        rectTransform.localPosition = Vector3.zero;
        rectTransform.localRotation = Quaternion.identity;
        rectTransform.localScale = Vector3.one;

        var canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = camera;
        canvas.pixelPerfect = false;
        canvas.sortingOrder = 0;
        return canvas;
    }

    private static GameObject InstantiatePrefab(GameObject prefabAsset, Scene scene)
    {
        var root = PrefabUtility.InstantiatePrefab(prefabAsset, scene) as GameObject;
        if (root != null)
        {
            return root;
        }

        root = UnityEngine.Object.Instantiate(prefabAsset);
        SceneManager.MoveGameObjectToScene(root, scene);
        return root;
    }

    private static void ParentToCanvas(GameObject root, Transform canvasTransform)
    {
        root.transform.SetParent(canvasTransform, false);

        var rectTransform = root.GetComponent<RectTransform>();
        if (rectTransform != null)
        {
            rectTransform.localPosition = Vector3.zero;
            rectTransform.localRotation = Quaternion.identity;
            if (rectTransform.localScale == Vector3.zero)
            {
                rectTransform.localScale = Vector3.one;
            }
        }
        else
        {
            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;
        }
    }

    private static void NormalizeCanvases(GameObject root, Camera camera)
    {
        foreach (var canvas in root.GetComponentsInChildren<Canvas>(true))
        {
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = camera;
            canvas.pixelPerfect = false;
        }
    }

    private static void RebuildLayout(GameObject root)
    {
        Canvas.ForceUpdateCanvases();

        foreach (var rectTransform in root.GetComponentsInChildren<RectTransform>(true))
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(rectTransform);
        }

        Canvas.ForceUpdateCanvases();
    }

    private static bool HasActiveGraphics(GameObject root)
    {
        var graphics = root.GetComponentsInChildren<Graphic>(false);
        for (var i = 0; i < graphics.Length; i++)
        {
            var graphic = graphics[i];
            if (graphic != null && graphic.enabled && graphic.gameObject.activeInHierarchy && graphic.color.a > 0.01f)
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasActiveRenderableGraphics(GameObject root)
    {
        var graphics = root.GetComponentsInChildren<Graphic>(false);
        for (var i = 0; i < graphics.Length; i++)
        {
            if (IsRenderablePreviewGraphic(graphics[i]))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryActivateHiddenPreviewState(GameObject root)
    {
        var rootTransform = root.transform;
        var graphics = root.GetComponentsInChildren<Graphic>(true);
        for (var i = 0; i < graphics.Length; i++)
        {
            var graphic = graphics[i];
            if (!IsRenderablePreviewGraphic(graphic) || graphic.gameObject.activeInHierarchy)
            {
                continue;
            }

            if (!HasInactiveAncestor(rootTransform, graphic.transform))
            {
                continue;
            }

            ActivatePath(rootTransform, graphic.transform);
            return true;
        }

        return false;
    }

    private static bool IsRenderablePreviewGraphic(Graphic graphic)
    {
        if (graphic == null || !graphic.enabled || graphic.color.a <= 0.01f || ShouldHideOwnMaskGraphic(graphic))
        {
            return false;
        }

        var rect = graphic.rectTransform.rect;
        if (rect.width < 1f || rect.height < 1f)
        {
            return false;
        }

        var image = graphic as Image;
        if (image != null)
        {
            return image.sprite != null || HasExplicitCustomMaterial(graphic);
        }

        var rawImage = graphic as RawImage;
        if (rawImage != null)
        {
            return rawImage.texture != null || HasExplicitCustomMaterial(graphic);
        }

        var text = graphic as Text;
        if (text != null)
        {
            return !string.IsNullOrEmpty(text.text);
        }

        if (string.Equals(graphic.GetType().Name, "SkeletonGraphic", StringComparison.Ordinal))
        {
            return true;
        }

        return graphic.mainTexture != null || HasExplicitCustomMaterial(graphic);
    }

    private static bool HasInactiveAncestor(Transform root, Transform target)
    {
        var current = target;
        while (current != null && current != root)
        {
            if (!current.gameObject.activeSelf)
            {
                return true;
            }

            current = current.parent;
        }

        return false;
    }

    private static void ActivatePath(Transform root, Transform target)
    {
        var current = target;
        while (current != null && current != root)
        {
            if (!current.gameObject.activeSelf)
            {
                current.gameObject.SetActive(true);
            }

            current = current.parent;
        }
    }

    private static void SetLayerRecursively(GameObject root, int layer)
    {
        root.layer = layer;

        foreach (Transform child in root.transform)
        {
            SetLayerRecursively(child.gameObject, layer);
        }
    }

    private static List<SoftwareTextItem> CollectTextItems(GameObject root, Bounds renderBounds, bool filterByBounds)
    {
        var result = new List<SoftwareTextItem>();
        var texts = root.GetComponentsInChildren<Text>(false);

        for (var i = 0; i < texts.Length; i++)
        {
            var text = texts[i];
            if (text == null || !text.enabled || string.IsNullOrEmpty(text.text) || text.font == null || text.color.a <= 0.01f)
            {
                continue;
            }

            var rectTransform = text.transform as RectTransform;
            if (rectTransform == null)
            {
                continue;
            }

            var rect = GetTextWorldRect(rectTransform);
            if (rect.width <= 0.01f || rect.height <= 0.01f || (filterByBounds && !TextRectIntersectsBounds(rect, renderBounds)))
            {
                continue;
            }

            result.Add(new SoftwareTextItem
            {
                source = text,
                rectTransform = rectTransform,
                rect = rect
            });
        }

        return result;
    }

    private static void DrawSoftwareTextItem(
        SoftwareTextItem item,
        Color[] pixels,
        int size,
        float scale,
        Vector2 boundsCenter,
        Dictionary<int, Texture2D> readableCopies)
    {
        var source = item.source;
        if (source == null || source.font == null)
        {
            return;
        }

        var settings = source.GetGenerationSettings(item.rectTransform.rect.size);
        source.font.RequestCharactersInTexture(source.text, settings.fontSize, settings.fontStyle);
        var generator = source.cachedTextGenerator;
        if (!generator.Populate(source.text, settings))
        {
            return;
        }

        var fontTexture = source.mainTexture as Texture2D;
        if (fontTexture == null)
        {
            return;
        }

        var readable = GetReadableCopy(fontTexture, readableCopies);
        if (readable == null)
        {
            return;
        }

        var vertices = generator.verts;
        var quadCount = Mathf.Max(0, vertices.Count / 4 - 1);
        for (var i = 0; i < quadCount; i++)
        {
            var offset = i * 4;
            DrawSoftwareTextQuad(item, vertices, offset, readable, pixels, size, scale, boundsCenter);
        }
    }

    private static void DrawSoftwareTextQuad(
        SoftwareTextItem item,
        IList<UIVertex> vertices,
        int offset,
        Texture2D readable,
        Color[] pixels,
        int size,
        float scale,
        Vector2 boundsCenter)
    {
        var v0 = vertices[offset + 0];
        var v1 = vertices[offset + 1];
        var v2 = vertices[offset + 2];
        var v3 = vertices[offset + 3];

        var p0 = item.rectTransform.TransformPoint(v0.position);
        var p1 = item.rectTransform.TransformPoint(v1.position);
        var p2 = item.rectTransform.TransformPoint(v2.position);
        var p3 = item.rectTransform.TransformPoint(v3.position);

        var minX = Mathf.Min(p0.x, p1.x, p2.x, p3.x);
        var minY = Mathf.Min(p0.y, p1.y, p2.y, p3.y);
        var maxX = Mathf.Max(p0.x, p1.x, p2.x, p3.x);
        var maxY = Mathf.Max(p0.y, p1.y, p2.y, p3.y);
        if (maxX - minX <= 0.01f || maxY - minY <= 0.01f)
        {
            return;
        }

        var minUvX = Mathf.Min(v0.uv0.x, v1.uv0.x, v2.uv0.x, v3.uv0.x);
        var minUvY = Mathf.Min(v0.uv0.y, v1.uv0.y, v2.uv0.y, v3.uv0.y);
        var maxUvX = Mathf.Max(v0.uv0.x, v1.uv0.x, v2.uv0.x, v3.uv0.x);
        var maxUvY = Mathf.Max(v0.uv0.y, v1.uv0.y, v2.uv0.y, v3.uv0.y);
        if (maxUvX - minUvX <= 0.0001f || maxUvY - minUvY <= 0.0001f)
        {
            return;
        }

        var worldRect = Rect.MinMaxRect(minX, minY, maxX, maxY);
        var dstRect = MapWorldRectToTexture(worldRect, size, scale, boundsCenter);
        var sourceRect = Rect.MinMaxRect(
            minUvX * readable.width,
            minUvY * readable.height,
            maxUvX * readable.width,
            maxUvY * readable.height);
        var color = item.source.color * v0.color;

        DrawTextureRect(
            pixels,
            size,
            dstRect,
            readable,
            sourceRect,
            color,
            null,
            default(ShaderMaskInfo),
            false,
            false,
            false);
    }

    private static Rect GetTextWorldRect(RectTransform rectTransform)
    {
        rectTransform.GetWorldCorners(Corners);

        var minX = Corners[0].x;
        var minY = Corners[0].y;
        var maxX = Corners[0].x;
        var maxY = Corners[0].y;

        for (var i = 1; i < Corners.Length; i++)
        {
            minX = Mathf.Min(minX, Corners[i].x);
            minY = Mathf.Min(minY, Corners[i].y);
            maxX = Mathf.Max(maxX, Corners[i].x);
            maxY = Mathf.Max(maxY, Corners[i].y);
        }

        return Rect.MinMaxRect(minX, minY, maxX, maxY);
    }

    private static bool TextRectIntersectsBounds(Rect rect, Bounds bounds)
    {
        return rect.xMax >= bounds.min.x
            && rect.xMin <= bounds.max.x
            && rect.yMax >= bounds.min.y
            && rect.yMin <= bounds.max.y;
    }

    private static List<SoftwareTmpTextItem> CollectTmpTextItems(GameObject root, Bounds renderBounds, bool filterByBounds)
    {
        var result = new List<SoftwareTmpTextItem>();
        var tmpTexts = root.GetComponentsInChildren<TMPro.TMP_Text>(false);

        for (var i = 0; i < tmpTexts.Length; i++)
        {
            var text = tmpTexts[i];
            if (text == null || !text.enabled || string.IsNullOrEmpty(text.text) || text.color.a <= 0.01f)
            {
                continue;
            }

            var rectTransform = text.transform as RectTransform;
            if (rectTransform == null)
            {
                continue;
            }

            text.ForceMeshUpdate(false, false);
            if (text.textInfo == null || text.textInfo.meshInfo == null)
            {
                continue;
            }

            for (var meshIndex = 0; meshIndex < text.textInfo.meshInfo.Length; meshIndex++)
            {
                var meshInfo = text.textInfo.meshInfo[meshIndex];
                if (meshInfo.vertexCount <= 0 || meshInfo.vertices == null || meshInfo.uvs0 == null || meshInfo.triangles == null)
                {
                    continue;
                }

                var texture = GetTmpMeshTexture(text, meshInfo, meshIndex);
                if (texture == null)
                {
                    continue;
                }

                if (!TryGetTmpMeshWorldRect(rectTransform, meshInfo.vertices, meshInfo.vertexCount, out var rect))
                {
                    rect = GetTextWorldRect(rectTransform);
                }

                if (rect.width <= 0.01f || rect.height <= 0.01f || (filterByBounds && !TextRectIntersectsBounds(rect, renderBounds)))
                {
                    continue;
                }

                result.Add(new SoftwareTmpTextItem
                {
                    source = text,
                    rectTransform = rectTransform,
                    rect = rect,
                    vertices = meshInfo.vertices,
                    uvs = meshInfo.uvs0,
                    colors = meshInfo.colors32,
                    triangles = meshInfo.triangles,
                    vertexCount = meshInfo.vertexCount,
                    texture = texture,
                    color = text.color
                });
            }
        }

        return result;
    }

    private static bool TryGetTmpMeshWorldRect(RectTransform rectTransform, Vector3[] vertices, int vertexCount, out Rect rect)
    {
        rect = Rect.zero;
        if (rectTransform == null || vertices == null || vertexCount <= 0)
        {
            return false;
        }

        var count = Mathf.Min(vertexCount, vertices.Length);
        if (count <= 0)
        {
            return false;
        }

        var first = rectTransform.TransformPoint(vertices[0]);
        var minX = first.x;
        var maxX = first.x;
        var minY = first.y;
        var maxY = first.y;

        for (var i = 1; i < count; i++)
        {
            var point = rectTransform.TransformPoint(vertices[i]);
            minX = Mathf.Min(minX, point.x);
            maxX = Mathf.Max(maxX, point.x);
            minY = Mathf.Min(minY, point.y);
            maxY = Mathf.Max(maxY, point.y);
        }

        rect = Rect.MinMaxRect(minX, minY, maxX, maxY);
        return rect.width >= 0.01f && rect.height >= 0.01f;
    }

    private static bool IsTmpTextComponent(Component component)
    {
        var type = component.GetType();
        while (type != null)
        {
            if (type.Namespace == "TMPro"
                && (type.Name == "TMP_Text" || type.Name.IndexOf("TextMeshPro", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                return true;
            }

            type = type.BaseType;
        }

        return false;
    }

    private static void InvokeTmpForceMeshUpdate(Component component)
    {
        try
        {
            var method = component.GetType().GetMethod(
                "ForceMeshUpdate",
                ComponentMemberFlags,
                null,
                new[] { typeof(bool), typeof(bool) },
                null);
            if (method != null)
            {
                method.Invoke(component, new object[] { false, false });
                return;
            }

            method = component.GetType().GetMethod("ForceMeshUpdate", ComponentMemberFlags, null, Type.EmptyTypes, null);
            if (method != null)
            {
                method.Invoke(component, null);
            }
        }
        catch
        {
        }
    }

    private static string GetComponentStringProperty(Component component, string propertyName)
    {
        try
        {
            var property = component.GetType().GetProperty(propertyName, ComponentMemberFlags);
            return property == null ? string.Empty : property.GetValue(component, null) as string;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static Color GetComponentColorProperty(Component component, string propertyName, Color fallback)
    {
        try
        {
            var property = component.GetType().GetProperty(propertyName, ComponentMemberFlags);
            if (property != null && property.PropertyType == typeof(Color))
            {
                return (Color)property.GetValue(component, null);
            }
        }
        catch
        {
        }

        return fallback;
    }

    private static T GetComponentObjectProperty<T>(Component component, string propertyName) where T : UnityEngine.Object
    {
        try
        {
            var property = component.GetType().GetProperty(propertyName, ComponentMemberFlags);
            return property == null ? null : property.GetValue(component, null) as T;
        }
        catch
        {
            return null;
        }
    }

    private static Texture2D GetTmpFontTexture(Component component)
    {
        var fontMaterial = GetComponentObjectProperty<Material>(component, "fontSharedMaterial")
            ?? GetComponentObjectProperty<Material>(component, "fontMaterial");
        if (fontMaterial != null && fontMaterial.mainTexture is Texture2D materialTexture)
        {
            return materialTexture;
        }

        var fontAsset = GetComponentObjectProperty<UnityEngine.Object>(component, "font");
        if (fontAsset == null)
        {
            return null;
        }

        var atlasTexture = GetObjectProperty<Texture2D>(fontAsset, "atlasTexture");
        if (atlasTexture != null)
        {
            return atlasTexture;
        }

        var atlasTextures = GetObjectProperty<Texture2D[]>(fontAsset, "atlasTextures");
        return atlasTextures != null && atlasTextures.Length > 0 ? atlasTextures[0] : null;
    }

    private static Texture2D GetTmpMeshTexture(TMPro.TMP_Text text, TMPro.TMP_MeshInfo meshInfo, int meshIndex)
    {
        var material = meshInfo.material;
        if (material == null && text.fontSharedMaterials != null && meshIndex < text.fontSharedMaterials.Length)
        {
            material = text.fontSharedMaterials[meshIndex];
        }

        if (material == null)
        {
            material = text.fontSharedMaterial;
        }

        if (material != null && material.mainTexture is Texture2D materialTexture)
        {
            return materialTexture;
        }

        if (text.font != null && text.font.atlasTexture is Texture2D atlasTexture)
        {
            return atlasTexture;
        }

        return null;
    }

    private static T GetObjectProperty<T>(UnityEngine.Object target, string propertyName) where T : class
    {
        try
        {
            var property = target.GetType().GetProperty(propertyName, ComponentMemberFlags);
            return property == null ? null : property.GetValue(target, null) as T;
        }
        catch
        {
            return null;
        }
    }

    private static void DrawSoftwareTmpTextItem(
        SoftwareTmpTextItem item,
        Color[] pixels,
        int size,
        float scale,
        Vector2 boundsCenter,
        Dictionary<int, Texture2D> readableCopies)
    {
        var readable = GetReadableCopy(item.texture, readableCopies);
        if (readable == null || item.vertices == null || item.uvs == null || item.triangles == null)
        {
            return;
        }

        var vertices = item.vertices;
        var uvs = item.uvs;
        var colors = item.colors;
        var triangles = item.triangles;
        var vertexCount = Mathf.Min(item.vertexCount, vertices.Length, uvs.Length);
        for (var i = 0; i + 2 < triangles.Length; i += 3)
        {
            var i0 = triangles[i];
            var i1 = triangles[i + 1];
            var i2 = triangles[i + 2];
            if (i0 >= vertexCount || i1 >= vertexCount || i2 >= vertexCount)
            {
                continue;
            }

            DrawTmpTexturedTriangle(
                pixels,
                size,
                readable,
                new Rect(0f, 0f, size, size),
                MapWorldPointToTexture(item.rectTransform.TransformPoint(vertices[i0]), size, scale, boundsCenter),
                MapWorldPointToTexture(item.rectTransform.TransformPoint(vertices[i1]), size, scale, boundsCenter),
                MapWorldPointToTexture(item.rectTransform.TransformPoint(vertices[i2]), size, scale, boundsCenter),
                uvs[i0],
                uvs[i1],
                uvs[i2],
                GetMeshVertexColor(colors, i0),
                GetMeshVertexColor(colors, i1),
                GetMeshVertexColor(colors, i2),
                item.color);
        }
    }

    private static void DrawTmpTexturedTriangle(
        Color[] pixels,
        int size,
        Texture2D source,
        Rect clipRect,
        Vector2 p0,
        Vector2 p1,
        Vector2 p2,
        Vector2 uv0,
        Vector2 uv1,
        Vector2 uv2,
        Color c0,
        Color c1,
        Color c2,
        Color tint)
    {
        var area = Edge(p0, p1, p2);
        if (Mathf.Abs(area) <= 0.0001f)
        {
            return;
        }

        var xMin = Mathf.Clamp(Mathf.FloorToInt(Mathf.Max(Mathf.Min(p0.x, p1.x, p2.x), clipRect.xMin)), 0, size - 1);
        var xMax = Mathf.Clamp(Mathf.CeilToInt(Mathf.Min(Mathf.Max(p0.x, p1.x, p2.x), clipRect.xMax)), 0, size);
        var yMin = Mathf.Clamp(Mathf.FloorToInt(Mathf.Max(Mathf.Min(p0.y, p1.y, p2.y), clipRect.yMin)), 0, size - 1);
        var yMax = Mathf.Clamp(Mathf.CeilToInt(Mathf.Min(Mathf.Max(p0.y, p1.y, p2.y), clipRect.yMax)), 0, size);

        for (var y = yMin; y < yMax; y++)
        {
            for (var x = xMin; x < xMax; x++)
            {
                var p = new Vector2(x + 0.5f, y + 0.5f);
                var w0 = Edge(p1, p2, p) / area;
                var w1 = Edge(p2, p0, p) / area;
                var w2 = Edge(p0, p1, p) / area;
                if (w0 < -0.0001f || w1 < -0.0001f || w2 < -0.0001f)
                {
                    continue;
                }

                var uv = uv0 * w0 + uv1 * w1 + uv2 * w2;
                var sourceX = Mathf.Clamp(Mathf.FloorToInt(uv.x * source.width), 0, source.width - 1);
                var sourceY = Mathf.Clamp(Mathf.FloorToInt(uv.y * source.height), 0, source.height - 1);
                var textureColor = source.GetPixel(sourceX, sourceY);
                var vertexColor = c0 * w0 + c1 * w1 + c2 * w2;
                var alpha = Mathf.Max(textureColor.a, textureColor.r, textureColor.g, textureColor.b) * vertexColor.a * tint.a;
                if (alpha <= 0.01f)
                {
                    continue;
                }

                var color = new Color(
                    vertexColor.r * tint.r,
                    vertexColor.g * tint.g,
                    vertexColor.b * tint.b,
                    alpha);

                BlendPixel(pixels, size, x, y, color);
            }
        }
    }

    private sealed class SoftwareTextItem
    {
        public Text source;
        public RectTransform rectTransform;
        public Rect rect;
    }

    private sealed class SoftwareTmpTextItem
    {
        public TMPro.TMP_Text source;
        public RectTransform rectTransform;
        public Rect rect;
        public Vector3[] vertices;
        public Vector2[] uvs;
        public Color32[] colors;
        public int[] triangles;
        public int vertexCount;
        public Texture2D texture;
        public Color color;
    }

    private static bool TryCalculateBounds(GameObject root, out Bounds bounds)
    {
        bounds = new Bounds(Vector3.zero, Vector3.zero);
        var hasBounds = false;
        var rectTransforms = root.GetComponentsInChildren<RectTransform>(false);

        foreach (var rectTransform in rectTransforms)
        {
            if (!rectTransform.gameObject.activeInHierarchy)
            {
                continue;
            }

            rectTransform.GetWorldCorners(Corners);
            for (var i = 0; i < Corners.Length; i++)
            {
                EncapsulatePoint(ref bounds, ref hasBounds, Corners[i]);
            }
        }

        var graphics = root.GetComponentsInChildren<Graphic>(false);
        for (var i = 0; i < graphics.Length; i++)
        {
            var graphic = graphics[i];
            if (graphic == null || !graphic.gameObject.activeInHierarchy)
            {
                continue;
            }

            if (!TryGetSpineMeshWorldRect(graphic, out var meshRect))
            {
                continue;
            }

            EncapsulatePoint(ref bounds, ref hasBounds, new Vector3(meshRect.xMin, meshRect.yMin, 0f));
            EncapsulatePoint(ref bounds, ref hasBounds, new Vector3(meshRect.xMax, meshRect.yMax, 0f));
        }

        if (!hasBounds)
        {
            return false;
        }

        if (bounds.size.x < 1f || bounds.size.y < 1f)
        {
            var rootRect = root.GetComponent<RectTransform>();
            if (rootRect != null && rootRect.rect.width >= 1f && rootRect.rect.height >= 1f)
            {
                bounds = new Bounds(rootRect.position, new Vector3(rootRect.rect.width, rootRect.rect.height, 1f));
            }
            else
            {
                bounds = new Bounds(Vector3.zero, new Vector3(512f, 512f, 1f));
            }
        }

        return true;
    }

    private static bool TryGetSpineMeshWorldRect(Graphic graphic, out Rect rect)
    {
        rect = Rect.zero;
        if (graphic == null || !string.Equals(graphic.GetType().Name, "SkeletonGraphic", StringComparison.Ordinal))
        {
            return false;
        }

        var mesh = InvokeComponentObjectMethod<Mesh>(graphic, "GetLastMesh");
        if (mesh == null || mesh.vertexCount == 0)
        {
            InvokeComponentMethod(graphic, "Initialize", true);
            InvokeComponentMethod(graphic, "Update", 0f);
            InvokeComponentMethod(graphic, "UpdateMesh");
            mesh = InvokeComponentObjectMethod<Mesh>(graphic, "GetLastMesh");
        }

        return mesh != null && TryCalculateMeshWorldRect(mesh, graphic.rectTransform.localToWorldMatrix, out rect);
    }

    private static void EncapsulatePoint(ref Bounds bounds, ref bool hasBounds, Vector3 point)
    {
        if (!hasBounds)
        {
            bounds = new Bounds(point, Vector3.zero);
            hasBounds = true;
        }
        else
        {
            bounds.Encapsulate(point);
        }
    }

    private static void ConfigureCamera(Camera camera, Bounds bounds, int size)
    {
        var extents = bounds.extents;
        var maxExtent = Mathf.Max(extents.x, extents.y, 32f);
        camera.transform.position = new Vector3(bounds.center.x, bounds.center.y, -1000f);
        camera.transform.rotation = Quaternion.identity;
        camera.orthographicSize = maxExtent * 1.08f;
        camera.aspect = 1f;
        camera.pixelRect = new Rect(0, 0, size, size);
    }

    private class SoftwareDrawItem
    {
        public Rect rect;
        public Color color;
        public Sprite sprite;
        public Texture texture;
        public Mesh mesh;
        public Texture meshTexture;
        public Matrix4x4 localToWorldMatrix;
        public ShaderMaskInfo shaderMask;
        public Rect sourceRect;
        public Rect clipRect;
        public bool hasNormalizedSourceRect;
        public bool hasClipRect;
        public bool hasShaderMask;
        public bool drawMesh;
        public bool preserveAspect;
        public bool drawSolid;
        public bool useLuminanceAlpha;
        public bool useAdditiveBlend;
        public bool avoidSolidFallback;
    }

    private struct ShaderMaskInfo
    {
        public Texture texture;
        public Vector2 textureScale;
        public Vector2 textureOffset;
        public Vector2 uvOffset;
        public bool inverse;
    }
}
