using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

public sealed class AssetFavoritesWindow : EditorWindow
{
    internal const string EntryDragKey = "Dragon.AssetFavorites.EntryGuids";

    private const string MenuPath = "Tools/UI/Asset Favorites";
    private const string PreviewSizePrefsKey = "Dragon.AssetFavorites.PreviewSize";
    private const float ToolbarHeight = 30f;
    private const float FolderPanelWidth = 238f;
    private const float FolderActionHeight = 36f;
    private const float StatusHeight = 26f;
    private const float MinPreviewSize = 64f;
    private const float ListViewThreshold = 64f;
    private const float DefaultPreviewSize = 96f;
    private const float MaxPreviewSize = 512f;
    private const float PreviewZoomStep = 24f;
    private const float GridHorizontalPadding = 24f;
    private const float GridVerticalPadding = 48f;
    private const float ListRowHeight = 24f;

    private AssetFavoritesLibrary library;
    private TreeViewState treeState;
    private AssetFavoritesTreeView treeView;
    private string currentFolderId = string.Empty;
    private string searchText = string.Empty;
    private string statusText = "将 Project 中的资产或文件夹拖到这里收藏";
    private Vector2 contentScroll;
    private float previewSize = DefaultPreviewSize;
    private readonly HashSet<string> selectedGuids = new HashSet<string>();
    private string selectionAnchorGuid = string.Empty;
    private string mouseDownGuid = string.Empty;
    private Vector2 mouseDownPosition;

    private bool IsListView
    {
        get { return previewSize <= ListViewThreshold; }
    }

    [MenuItem(MenuPath, false, 2330)]
    public static void OpenAndFocus()
    {
        AssetFavoritesWindow window = GetWindow<AssetFavoritesWindow>("Asset Favorites");
        window.minSize = new Vector2(720f, 420f);
        window.Show();
        window.Focus();
    }

    private void OnEnable()
    {
        previewSize = Mathf.Clamp(EditorPrefs.GetFloat(PreviewSizePrefsKey, DefaultPreviewSize), MinPreviewSize, MaxPreviewSize);
        ReloadLibrary();
    }

    private void OnInspectorUpdate()
    {
        Repaint();
    }

    private void OnProjectChange()
    {
        ReloadLibrary();
        Repaint();
    }

    private void OnGUI()
    {
        EnsureReady();
        DrawToolbar();

        Rect statusRect = new Rect(0f, position.height - StatusHeight, position.width, StatusHeight);
        Rect bodyRect = new Rect(0f, ToolbarHeight, position.width, Mathf.Max(0f, statusRect.y - ToolbarHeight));
        float leftWidth = Mathf.Min(FolderPanelWidth, Mathf.Max(190f, bodyRect.width * 0.34f));
        Rect leftRect = new Rect(bodyRect.x, bodyRect.y, leftWidth, bodyRect.height);
        Rect rightRect = new Rect(leftRect.xMax + 1f, bodyRect.y, Mathf.Max(0f, bodyRect.width - leftWidth - 1f), bodyRect.height);
        List<EntryView> visibleEntries = GetVisibleEntries();

        EditorGUI.DrawRect(new Rect(leftRect.xMax, bodyRect.y, 1f, bodyRect.height), EditorGUIUtility.isProSkin ? new Color(0.12f, 0.12f, 0.12f) : new Color(0.65f, 0.65f, 0.65f));
        DrawFolderPanel(leftRect);
        DrawContentPanel(rightRect, visibleEntries);
        DrawStatusBar(statusRect, visibleEntries.Count);
    }

    private void DrawToolbar()
    {
        using (new GUILayout.HorizontalScope(EditorStyles.toolbar, GUILayout.Height(ToolbarHeight)))
        {
            GUILayout.Space(4f);
            GUIStyle searchStyle = GUI.skin.FindStyle("ToolbarSeachTextField") ?? EditorStyles.toolbarTextField;
            string nextSearch = GUILayout.TextField(searchText, searchStyle, GUILayout.ExpandWidth(true), GUILayout.Height(20f));
            if (nextSearch != searchText)
            {
                searchText = nextSearch;
                contentScroll = Vector2.zero;
            }

            if (!string.IsNullOrEmpty(searchText) && GUILayout.Button("×", EditorStyles.toolbarButton, GUILayout.Width(24f)))
            {
                searchText = string.Empty;
                contentScroll = Vector2.zero;
            }
            GUILayout.Space(4f);
        }
    }

    private void DrawFolderPanel(Rect rect)
    {
        Rect footerRect = new Rect(rect.x, rect.yMax - 26f, rect.width, 26f);
        Rect actionRect = new Rect(rect.x + 6f, rect.y + 6f, rect.width - 12f, 26f);
        Rect treeRect = new Rect(rect.x + 2f, rect.y + FolderActionHeight, rect.width - 4f, Mathf.Max(0f, footerRect.y - rect.y - FolderActionHeight - 2f));

        if (GUI.Button(actionRect, "+ 新建文件夹"))
        {
            CreateFolder();
        }

        treeView.OnGUI(treeRect);

        AssetFavoriteFolder selectedFolder = library.FindFolder(currentFolderId);
        using (new EditorGUI.DisabledScope(selectedFolder == null || selectedFolder.automatic))
        {
            if (GUI.Button(new Rect(footerRect.x + 6f, footerRect.y + 3f, 70f, 20f), "重命名", EditorStyles.miniButtonLeft))
            {
                treeView.BeginRenameFolder(currentFolderId);
            }

            if (GUI.Button(new Rect(footerRect.x + 76f, footerRect.y + 3f, 62f, 20f), "删除", EditorStyles.miniButtonRight))
            {
                DeleteCurrentFolder();
            }
        }
    }

    private void DrawContentPanel(Rect rect, List<EntryView> visibleEntries)
    {
        DrawEntries(rect, visibleEntries);
        HandleProjectDrop(rect);
    }

    private void DrawStatusBar(Rect rect, int visibleCount)
    {
        EditorGUI.DrawRect(rect, EditorGUIUtility.isProSkin ? new Color(0.16f, 0.16f, 0.16f) : new Color(0.88f, 0.88f, 0.88f));
        GUI.Label(new Rect(rect.x + 8f, rect.y + 4f, 78f, 18f), visibleCount + " 个资产", EditorStyles.miniLabel);

        const float controlsWidth = 230f;
        Rect controlsRect = new Rect(rect.xMax - controlsWidth - 6f, rect.y + 2f, controlsWidth, rect.height - 4f);
        float statusWidth = Mathf.Max(0f, controlsRect.x - rect.x - 92f);
        GUI.Label(new Rect(rect.x + 88f, rect.y + 4f, statusWidth, 18f), statusText, EditorStyles.miniLabel);

        Rect listRect = new Rect(controlsRect.x, controlsRect.y, 28f, controlsRect.height);
        Rect sizeLabelRect = new Rect(listRect.xMax + 4f, controlsRect.y + 2f, 44f, controlsRect.height - 4f);
        Rect sliderRect = new Rect(sizeLabelRect.xMax + 2f, controlsRect.y + 3f, 120f, controlsRect.height - 6f);
        Rect gridRect = new Rect(sliderRect.xMax + 4f, controlsRect.y, 28f, controlsRect.height);

        DrawViewIndicator(listRect, "≡", IsListView, "缩小到 64 px 自动进入列表");
        GUI.Label(sizeLabelRect, Mathf.RoundToInt(previewSize) + " px", EditorStyles.centeredGreyMiniLabel);
        float nextPreviewSize = GUI.HorizontalSlider(sliderRect, previewSize, MinPreviewSize, MaxPreviewSize);
        if (!Mathf.Approximately(nextPreviewSize, previewSize))
        {
            SetPreviewSize(nextPreviewSize, true);
        }
        DrawViewIndicator(gridRect, "▦", !IsListView, "放大到 64 px 以上自动进入网格");

        HandleZoomControlWheel(controlsRect);
    }

    private static void DrawViewIndicator(Rect rect, string glyph, bool active, string tooltip)
    {
        if (active)
        {
            EditorGUI.DrawRect(rect, EditorGUIUtility.isProSkin ? new Color(0.24f, 0.39f, 0.56f, 0.9f) : new Color(0.30f, 0.52f, 0.78f, 0.75f));
        }

        GUIStyle style = new GUIStyle(EditorStyles.centeredGreyMiniLabel);
        if (active)
        {
            style.normal.textColor = Color.white;
        }
        GUI.Label(rect, new GUIContent(glyph, tooltip), style);
    }

    private void HandleContentZoomWheel(Rect contentRect)
    {
        Event current = Event.current;
        if (current.type != EventType.ScrollWheel
            || !contentRect.Contains(current.mousePosition)
            || (!current.control && !current.command))
        {
            return;
        }

        SetPreviewSize(previewSize - current.delta.y * PreviewZoomStep, true);
        current.Use();
    }

    private void HandleZoomControlWheel(Rect controlsRect)
    {
        Event current = Event.current;
        if (current.type != EventType.ScrollWheel || !controlsRect.Contains(current.mousePosition))
        {
            return;
        }

        SetPreviewSize(previewSize - current.delta.y * PreviewZoomStep, true);
        current.Use();
    }

    private void SetPreviewSize(float requestedSize, bool preserveScrollPosition)
    {
        float nextSize = Mathf.Round(Mathf.Clamp(requestedSize, MinPreviewSize, MaxPreviewSize));
        if (Mathf.Approximately(nextSize, previewSize))
        {
            return;
        }

        bool wasListView = IsListView;
        bool willBeListView = nextSize <= ListViewThreshold;
        if (wasListView != willBeListView)
        {
            contentScroll = Vector2.zero;
        }
        else if (preserveScrollPosition && !willBeListView && previewSize > 0f)
        {
            float scale = (nextSize + GridVerticalPadding) / (previewSize + GridVerticalPadding);
            contentScroll *= scale;
        }

        previewSize = nextSize;
        EditorPrefs.SetFloat(PreviewSizePrefsKey, previewSize);
        Repaint();
    }

    private void DrawEntries(Rect contentRect, List<EntryView> visibleEntries)
    {
        HandleContentZoomWheel(contentRect);

        if (visibleEntries.Count == 0)
        {
            string message = string.IsNullOrEmpty(searchText)
                ? "拖入资产开始收藏"
                : "没有匹配的收藏";
            GUI.Label(contentRect, message, CenteredLabelStyle());
            return;
        }

        float contentWidth = Mathf.Max(1f, contentRect.width - 18f);
        float contentHeight;
        float viewWidth = contentWidth;
        int columns = 1;
        if (!IsListView)
        {
            float gridTileWidth = previewSize + GridHorizontalPadding;
            float gridTileHeight = previewSize + GridVerticalPadding;
            columns = Mathf.Max(1, Mathf.FloorToInt((contentWidth - 8f) / gridTileWidth));
            int rows = Mathf.CeilToInt(visibleEntries.Count / (float)columns);
            contentHeight = rows * gridTileHeight + 12f;
            viewWidth = Mathf.Max(contentWidth, gridTileWidth + 10f);
        }
        else
        {
            contentHeight = visibleEntries.Count * ListRowHeight + 8f;
        }

        Rect viewRect = new Rect(0f, 0f, viewWidth, Mathf.Max(contentRect.height, contentHeight));
        contentScroll = GUI.BeginScrollView(contentRect, contentScroll, viewRect);
        for (int i = 0; i < visibleEntries.Count; i++)
        {
            Rect itemRect;
            if (!IsListView)
            {
                float gridTileWidth = previewSize + GridHorizontalPadding;
                float gridTileHeight = previewSize + GridVerticalPadding;
                int row = i / columns;
                int column = i % columns;
                itemRect = new Rect(7f + column * gridTileWidth, 6f + row * gridTileHeight, gridTileWidth - 8f, gridTileHeight - 8f);
                DrawGridEntry(itemRect, visibleEntries[i]);
            }
            else
            {
                itemRect = new Rect(5f, 4f + i * ListRowHeight, contentWidth - 10f, ListRowHeight - 2f);
                DrawListEntry(itemRect, visibleEntries[i]);
            }

            HandleEntryEvent(itemRect, visibleEntries[i], visibleEntries);
        }
        GUI.EndScrollView();
    }

    private void DrawGridEntry(Rect rect, EntryView entry)
    {
        bool selected = selectedGuids.Contains(entry.Entry.assetGuid);
        bool hovered = rect.Contains(Event.current.mousePosition);
        if (selected)
        {
            EditorGUI.DrawRect(rect, EditorGUIUtility.isProSkin ? new Color(0.20f, 0.42f, 0.65f, 0.72f) : new Color(0.30f, 0.55f, 0.82f, 0.52f));
        }
        else if (hovered && Event.current.type == EventType.Repaint)
        {
            EditorGUI.DrawRect(rect, EditorGUIUtility.isProSkin ? new Color(1f, 1f, 1f, 0.055f) : new Color(0f, 0f, 0f, 0.055f));
        }

        Rect previewRect = new Rect(rect.x + 8f, rect.y + 5f, previewSize, previewSize);
        DrawAssetPreview(previewRect, entry);
        GUI.Label(new Rect(rect.x + 4f, previewRect.yMax + 3f, rect.width - 8f, 30f), entry.DisplayName, CenteredMiniLabelStyle());
    }

    private void DrawListEntry(Rect rect, EntryView entry)
    {
        bool selected = selectedGuids.Contains(entry.Entry.assetGuid);
        bool hovered = rect.Contains(Event.current.mousePosition);
        if (selected)
        {
            EditorGUI.DrawRect(rect, EditorGUIUtility.isProSkin ? new Color(0.20f, 0.42f, 0.65f, 0.75f) : new Color(0.30f, 0.55f, 0.82f, 0.55f));
        }
        else if (hovered && Event.current.type == EventType.Repaint)
        {
            EditorGUI.DrawRect(rect, EditorGUIUtility.isProSkin ? new Color(1f, 1f, 1f, 0.055f) : new Color(0f, 0f, 0f, 0.055f));
        }

        Rect iconRect = new Rect(rect.x + 6f, rect.y + 2f, 20f, 20f);
        DrawAssetPreview(iconRect, entry);
        GUI.Label(new Rect(iconRect.xMax + 7f, rect.y + 2f, rect.width - iconRect.width - 18f, 20f), new GUIContent(entry.DisplayName, entry.Path), EditorStyles.label);
    }

    private void DrawAssetPreview(Rect rect, EntryView entry)
    {
        Texture preview = entry.Asset as Texture;
        if (entry.Asset != null)
        {
            if (preview == null)
            {
                preview = AssetPreview.GetAssetPreview(entry.Asset);
            }
            if (preview == null)
            {
                preview = AssetPreview.GetMiniThumbnail(entry.Asset);
            }
        }

        if (preview != null)
        {
            GUI.DrawTexture(rect, preview, ScaleMode.ScaleToFit, true);
        }
        else
        {
            GUI.Label(rect, EditorGUIUtility.IconContent("console.warnicon"), CenteredLabelStyle());
        }
    }

    private void HandleEntryEvent(Rect rect, EntryView entry, List<EntryView> visibleEntries)
    {
        Event current = Event.current;
        if (!rect.Contains(current.mousePosition))
        {
            return;
        }

        if (current.type == EventType.MouseDown && current.button == 0)
        {
            ApplyEntrySelection(entry.Entry.assetGuid, visibleEntries, current.control || current.command, current.shift);
            Ping(entry.Asset);
            mouseDownGuid = entry.Entry.assetGuid;
            mouseDownPosition = current.mousePosition;

            if (current.clickCount == 2)
            {
                Open(entry.Asset);
            }

            current.Use();
            Repaint();
        }
        else if (current.type == EventType.MouseDrag && current.button == 0
                 && !string.IsNullOrEmpty(mouseDownGuid)
                 && Vector2.Distance(mouseDownPosition, current.mousePosition) > 4f)
        {
            StartEntryDrag();
            current.Use();
        }
        else if (current.type == EventType.ContextClick)
        {
            if (!selectedGuids.Contains(entry.Entry.assetGuid))
            {
                selectedGuids.Clear();
                selectedGuids.Add(entry.Entry.assetGuid);
                selectionAnchorGuid = entry.Entry.assetGuid;
            }

            GenericMenu menu = new GenericMenu();
            menu.AddItem(new GUIContent("Ping in Project"), false, () => Ping(entry.Asset));
            menu.AddItem(new GUIContent("Open"), false, () => Open(entry.Asset));
            menu.AddSeparator(string.Empty);
            menu.AddItem(new GUIContent("Remove from Favorites"), false, RemoveSelectedEntries);
            menu.ShowAsContext();
            current.Use();
        }
    }

    private void ApplyEntrySelection(string guid, List<EntryView> visibleEntries, bool toggle, bool range)
    {
        if (range && !string.IsNullOrEmpty(selectionAnchorGuid))
        {
            int anchorIndex = visibleEntries.FindIndex(entry => entry.Entry.assetGuid == selectionAnchorGuid);
            int clickedIndex = visibleEntries.FindIndex(entry => entry.Entry.assetGuid == guid);
            if (anchorIndex >= 0 && clickedIndex >= 0)
            {
                if (!toggle)
                {
                    selectedGuids.Clear();
                }

                int start = Mathf.Min(anchorIndex, clickedIndex);
                int end = Mathf.Max(anchorIndex, clickedIndex);
                for (int i = start; i <= end; i++)
                {
                    selectedGuids.Add(visibleEntries[i].Entry.assetGuid);
                }
                return;
            }
        }

        if (toggle)
        {
            if (!selectedGuids.Add(guid))
            {
                selectedGuids.Remove(guid);
            }
        }
        else
        {
            selectedGuids.Clear();
            selectedGuids.Add(guid);
        }

        selectionAnchorGuid = guid;
    }

    private void StartEntryDrag()
    {
        string[] guids = selectedGuids.Count > 0 ? selectedGuids.ToArray() : new[] { mouseDownGuid };
        UnityEngine.Object[] objects = guids
            .Select(AssetDatabase.GUIDToAssetPath)
            .Select(AssetDatabase.LoadMainAssetAtPath)
            .Where(item => item != null)
            .ToArray();

        DragAndDrop.PrepareStartDrag();
        DragAndDrop.SetGenericData(EntryDragKey, guids);
        DragAndDrop.objectReferences = objects;
        DragAndDrop.StartDrag(guids.Length == 1 ? "Favorite Asset" : guids.Length + " Favorite Assets");
        mouseDownGuid = string.Empty;
    }

    private void HandleProjectDrop(Rect rect)
    {
        Event current = Event.current;
        if (!rect.Contains(current.mousePosition)
            || (current.type != EventType.DragUpdated && current.type != EventType.DragPerform))
        {
            return;
        }

        if (DragAndDrop.GetGenericData(EntryDragKey) != null)
        {
            return;
        }

        string[] paths = DragAndDrop.objectReferences
            .Select(AssetDatabase.GetAssetPath)
            .Where(path => !string.IsNullOrEmpty(path))
            .ToArray();
        if (paths.Length == 0)
        {
            return;
        }

        DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
        if (current.type == EventType.DragPerform)
        {
            DragAndDrop.AcceptDrag();
            AddPaths(paths, currentFolderId);
            DragAndDrop.SetGenericData(EntryDragKey, null);
        }

        current.Use();
    }

    private void AddPaths(IEnumerable<string> paths, string destinationFolderId)
    {
        int skipped;
        int added = library.AddProjectPaths(paths, destinationFolderId, out skipped);
        statusText = "已收藏 " + added + " 个；跳过 " + skipped + " 个已收藏或无效资产";
        ReloadTreeKeepingSelection();
        Repaint();
    }

    private void MoveEntries(IEnumerable<string> guids, string destinationFolderId)
    {
        int moved = library.MoveEntries(guids, destinationFolderId);
        statusText = moved > 0 ? "已移动 " + moved + " 个收藏" : "收藏已在该目录中";
        ReloadTreeKeepingSelection();
        Repaint();
    }

    private void RemoveSelectedEntries()
    {
        if (selectedGuids.Count == 0)
        {
            return;
        }

        int removed = library.RemoveEntries(selectedGuids);
        selectedGuids.Clear();
        selectionAnchorGuid = string.Empty;
        statusText = "已从收藏中移除 " + removed + " 个资产；原资产未受影响";
        ReloadTreeKeepingSelection();
        Repaint();
    }

    private void CreateFolder()
    {
        string parentId = currentFolderId;
        string createdId = library.CreateFolder(parentId, "New Folder");
        ReloadTreeKeepingSelection();
        currentFolderId = createdId;
        treeView.SelectFolder(createdId);
        treeView.BeginRenameFolder(createdId);
        statusText = "已创建文件夹；输入名称后按 Enter";
    }

    private void DeleteCurrentFolder()
    {
        AssetFavoriteFolder folder = library.FindFolder(currentFolderId);
        if (folder == null || folder.automatic)
        {
            return;
        }

        int count = library.CountEntriesRecursive(folder.id);
        if (!EditorUtility.DisplayDialog("删除收藏文件夹", "将移除“" + folder.displayName + "”及子文件夹中的 " + count + " 个收藏关系。\n\n项目内原始资产不会被删除。", "删除收藏关系", "取消"))
        {
            return;
        }

        int removed = library.DeleteFolder(folder.id);
        currentFolderId = string.Empty;
        selectedGuids.Clear();
        ReloadTreeKeepingSelection();
        statusText = "已删除文件夹并移除 " + removed + " 个收藏关系；原资产未受影响";
    }

    private List<EntryView> GetVisibleEntries()
    {
        IEnumerable<AssetFavoriteEntry> source = library.Entries;
        if (!string.IsNullOrEmpty(currentFolderId))
        {
            source = source.Where(entry => entry.folderId == currentFolderId);
        }

        string query = searchText.Trim();
        List<EntryView> views = source.Select(entry => new EntryView(entry)).ToList();
        if (!string.IsNullOrEmpty(query))
        {
            views = views.Where(view => view.DisplayName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
                                        || view.Path.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();
        }

        return views.OrderBy(view => view.DisplayName, StringComparer.OrdinalIgnoreCase).ThenBy(view => view.Path).ToList();
    }

    private string BuildBreadcrumb()
    {
        if (string.IsNullOrEmpty(currentFolderId))
        {
            return "All Favorites";
        }

        List<string> names = new List<string>();
        AssetFavoriteFolder folder = library.FindFolder(currentFolderId);
        HashSet<string> visited = new HashSet<string>();
        while (folder != null && visited.Add(folder.id))
        {
            names.Add(folder.displayName);
            folder = library.FindFolder(folder.parentId);
        }

        names.Reverse();
        return "All Favorites / " + string.Join(" / ", names.ToArray());
    }

    private void ReloadLibrary()
    {
        library = AssetFavoritesLibrary.LoadOrCreate();
        if (treeState == null)
        {
            treeState = new TreeViewState();
        }

        treeView = new AssetFavoritesTreeView(treeState, library, OnFolderSelected, AddPaths, MoveEntries, Repaint);
        treeView.SelectFolder(currentFolderId);
    }

    private void ReloadTreeKeepingSelection()
    {
        treeView.ReloadData();
        treeView.SelectFolder(currentFolderId);
    }

    private void EnsureReady()
    {
        if (library == null || treeView == null)
        {
            ReloadLibrary();
        }
    }

    private void OnFolderSelected(string folderId)
    {
        currentFolderId = folderId;
        selectedGuids.Clear();
        selectionAnchorGuid = string.Empty;
        contentScroll = Vector2.zero;
        statusText = string.IsNullOrEmpty(folderId) ? "未指定目录时，新收藏会按类型自动分类" : "拖入资产会收藏到当前目录";
        Repaint();
    }

    private static void Ping(UnityEngine.Object asset)
    {
        if (asset != null)
        {
            EditorGUIUtility.PingObject(asset);
        }
    }

    private static void Open(UnityEngine.Object asset)
    {
        if (asset != null)
        {
            AssetDatabase.OpenAsset(asset);
        }
    }

    private static GUIStyle CenteredLabelStyle()
    {
        GUIStyle style = new GUIStyle(EditorStyles.label);
        style.alignment = TextAnchor.MiddleCenter;
        style.wordWrap = true;
        return style;
    }

    private static GUIStyle CenteredMiniLabelStyle()
    {
        GUIStyle style = new GUIStyle(EditorStyles.miniLabel);
        style.alignment = TextAnchor.UpperCenter;
        style.wordWrap = true;
        return style;
    }

    private sealed class EntryView
    {
        public readonly AssetFavoriteEntry Entry;
        public readonly string Path;
        public readonly string DisplayName;
        public readonly UnityEngine.Object Asset;

        public EntryView(AssetFavoriteEntry entry)
        {
            Entry = entry;
            Path = AssetDatabase.GUIDToAssetPath(entry.assetGuid);
            Asset = string.IsNullOrEmpty(Path) ? null : AssetDatabase.LoadMainAssetAtPath(Path);
            DisplayName = string.IsNullOrEmpty(Path) ? "Missing Asset" : System.IO.Path.GetFileNameWithoutExtension(Path);
        }
    }
}

internal sealed class AssetFavoritesTreeView : TreeView
{
    private readonly AssetFavoritesLibrary library;
    private readonly Action<string> selectionChanged;
    private readonly Action<IEnumerable<string>, string> addPaths;
    private readonly Action<IEnumerable<string>, string> moveEntries;
    private readonly Action repaint;
    private readonly Dictionary<int, string> itemFolders = new Dictionary<int, string>();
    private readonly Dictionary<string, int> folderItems = new Dictionary<string, int>();

    public AssetFavoritesTreeView(
        TreeViewState state,
        AssetFavoritesLibrary library,
        Action<string> selectionChanged,
        Action<IEnumerable<string>, string> addPaths,
        Action<IEnumerable<string>, string> moveEntries,
        Action repaint) : base(state)
    {
        this.library = library;
        this.selectionChanged = selectionChanged;
        this.addPaths = addPaths;
        this.moveEntries = moveEntries;
        this.repaint = repaint;
        showBorder = false;
        rowHeight = 20f;
        Reload();
        SetExpanded(1, true);
    }

    public void ReloadData()
    {
        Reload();
    }

    public void SelectFolder(string folderId)
    {
        int itemId;
        if (!folderItems.TryGetValue(folderId ?? string.Empty, out itemId))
        {
            itemId = 1;
        }

        SetSelection(new[] { itemId }, TreeViewSelectionOptions.RevealAndFrame);
    }

    public void BeginRenameFolder(string folderId)
    {
        int itemId;
        if (!folderItems.TryGetValue(folderId ?? string.Empty, out itemId))
        {
            return;
        }

        TreeViewItem item = FindItem(itemId, rootItem);
        if (item != null && CanRename(item))
        {
            BeginRename(item);
        }
    }

    protected override TreeViewItem BuildRoot()
    {
        itemFolders.Clear();
        folderItems.Clear();

        TreeViewItem root = new TreeViewItem(0, -1, "Root");
        TreeViewItem all = new TreeViewItem(1, 0, "All Favorites");
        all.icon = EditorGUIUtility.IconContent("Folder Icon").image as Texture2D;
        root.AddChild(all);
        itemFolders[1] = string.Empty;
        folderItems[string.Empty] = 1;

        int nextId = 2;
        AddFolderChildren(all, string.Empty, 1, ref nextId);
        SetupDepthsFromParentsAndChildren(root);
        return root;
    }

    protected override void RowGUI(RowGUIArgs args)
    {
        string folderId;
        itemFolders.TryGetValue(args.item.id, out folderId);
        int count = library.CountEntriesRecursive(folderId);
        Rect labelRect = args.rowRect;
        labelRect.xMax -= 38f;
        args.rowRect = labelRect;
        base.RowGUI(args);
        GUI.Label(new Rect(labelRect.xMax + 2f, labelRect.y, 34f, labelRect.height), count.ToString(), EditorStyles.miniLabel);
    }

    protected override void SelectionChanged(IList<int> selectedIds)
    {
        if (selectedIds == null || selectedIds.Count == 0)
        {
            return;
        }

        int id = selectedIds[0];
        string folderId;
        if (itemFolders.TryGetValue(id, out folderId))
        {
            selectionChanged(folderId);
        }
    }

    protected override bool CanRename(TreeViewItem item)
    {
        string folderId;
        if (!itemFolders.TryGetValue(item.id, out folderId) || string.IsNullOrEmpty(folderId))
        {
            return false;
        }

        AssetFavoriteFolder folder = library.FindFolder(folderId);
        return folder != null && !folder.automatic;
    }

    protected override void RenameEnded(RenameEndedArgs args)
    {
        if (!args.acceptedRename)
        {
            return;
        }

        string folderId;
        if (itemFolders.TryGetValue(args.itemID, out folderId) && library.RenameFolder(folderId, args.newName))
        {
            Reload();
            SelectFolder(folderId);
            repaint();
        }
    }

    protected override void ContextClickedItem(int id)
    {
        string folderId;
        if (!itemFolders.TryGetValue(id, out folderId))
        {
            return;
        }

        SelectFolder(folderId);
        selectionChanged(folderId);
        AssetFavoriteFolder folder = library.FindFolder(folderId);
        GenericMenu menu = new GenericMenu();
        menu.AddItem(new GUIContent("New Subfolder"), false, () =>
        {
            string createdId = library.CreateFolder(folderId, "New Folder");
            Reload();
            SelectFolder(createdId);
            selectionChanged(createdId);
            BeginRenameFolder(createdId);
        });

        if (folder != null && !folder.automatic)
        {
            menu.AddItem(new GUIContent("Rename"), false, () => BeginRenameFolder(folderId));
        }
        else
        {
            menu.AddDisabledItem(new GUIContent("Rename"));
        }

        menu.ShowAsContext();
    }

    protected override DragAndDropVisualMode HandleDragAndDrop(DragAndDropArgs args)
    {
        string targetFolderId = string.Empty;
        string mappedFolderId;
        if (args.parentItem != null && itemFolders.TryGetValue(args.parentItem.id, out mappedFolderId))
        {
            targetFolderId = mappedFolderId;
        }

        string[] draggedEntries = DragAndDrop.GetGenericData(AssetFavoritesWindow.EntryDragKey) as string[];
        if (draggedEntries != null && draggedEntries.Length > 0)
        {
            if (args.performDrop)
            {
                moveEntries(draggedEntries, targetFolderId);
                DragAndDrop.AcceptDrag();
                DragAndDrop.SetGenericData(AssetFavoritesWindow.EntryDragKey, null);
            }
            return DragAndDropVisualMode.Move;
        }

        string[] paths = DragAndDrop.objectReferences
            .Select(AssetDatabase.GetAssetPath)
            .Where(path => !string.IsNullOrEmpty(path))
            .ToArray();
        if (paths.Length == 0)
        {
            return DragAndDropVisualMode.None;
        }

        if (args.performDrop)
        {
            addPaths(paths, targetFolderId);
            DragAndDrop.AcceptDrag();
        }
        return DragAndDropVisualMode.Copy;
    }

    private void AddFolderChildren(TreeViewItem parentItem, string parentFolderId, int depth, ref int nextId)
    {
        foreach (AssetFavoriteFolder folder in library.GetChildFolders(parentFolderId))
        {
            int itemId = nextId++;
            TreeViewItem item = new TreeViewItem(itemId, depth, folder.displayName);
            item.icon = EditorGUIUtility.IconContent("Folder Icon").image as Texture2D;
            parentItem.AddChild(item);
            itemFolders[itemId] = folder.id;
            folderItems[folder.id] = itemId;
            AddFolderChildren(item, folder.id, depth + 1, ref nextId);
        }
    }
}
