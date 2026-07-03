using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.Overlays;
using UnityEditor.Toolbars;
using UnityEngine;
using UnityEngine.UIElements;

#if UNITY_2021_2_OR_NEWER
using UnityEditor.SceneManagement;
#else
using UnityEditor.Experimental.SceneManagement;
#endif

// ==========================================
// 1. 核心逻辑部分 (保持不变)
// ==========================================
[InitializeOnLoad]
public static class PrefabHistoryLogic
{
    private const string HistoryPrefsKey = "Dragon.PrefabHistoryOverlay.History.v1";

    private static List<string> history = new List<string>();
    private static List<string> pinnedHistory = new List<string>();
    private static int currentIndex = -1;
    private static bool isNavigating = false;
    private static int recordingSuppressionDepth;

    public static Action OnHistoryChanged;
    public static int HistoryCount => history.Count;
    public static int CurrentIndex => currentIndex;
    public static int PinnedCount => pinnedHistory.Count;
    public static int ScrollableHistoryCount
    {
        get
        {
            int count = 0;
            foreach (string path in history)
            {
                if (!IsPinned(path))
                {
                    count++;
                }
            }

            return count;
        }
    }

    static PrefabHistoryLogic()
    {
        LoadHistory();

#if UNITY_2021_2_OR_NEWER
        PrefabStage.prefabStageOpened += OnPrefabOpened;
#else
        PrefabStageUtility.prefabStageOpened += OnPrefabOpened;
#endif
    }

    private static void OnPrefabOpened(PrefabStage stage)
    {
        if (isNavigating || recordingSuppressionDepth > 0) return;

#if UNITY_2021_2_OR_NEWER
        string path = stage.assetPath;
#else
        string path = stage.prefabAssetPath;
#endif
        if (string.IsNullOrEmpty(path)) return;

        int existingIndex = history.IndexOf(path);
        if (existingIndex >= 0)
        {
            history.RemoveAt(existingIndex);
            if (existingIndex <= currentIndex)
            {
                currentIndex--;
            }
        }

        history.Add(path);
        currentIndex = history.Count - 1;

        SaveHistory();
        OnHistoryChanged?.Invoke();
    }

    public static bool CanGoBack() => history.Count > 0 && currentIndex >= 0 && (PrefabStageUtility.GetCurrentPrefabStage() == null || currentIndex > 0);
    public static bool CanGoForward() => history.Count > 0 && currentIndex >= 0 && currentIndex < history.Count - 1 && PrefabStageUtility.GetCurrentPrefabStage() != null;

    public static string GetHistoryPath(int index)
    {
        if (index < 0 || index >= history.Count) return string.Empty;
        return history[index];
    }

    public static string GetPinnedPath(int index)
    {
        if (index < 0) return string.Empty;

        int visibleIndex = 0;
        for (int i = history.Count - 1; i >= 0; i--)
        {
            string path = history[i];
            if (!IsPinned(path)) continue;
            if (visibleIndex == index) return path;
            visibleIndex++;
        }

        return string.Empty;
    }

    public static string GetScrollableHistoryPath(int index)
    {
        if (index < 0) return string.Empty;

        int visibleIndex = 0;
        for (int i = history.Count - 1; i >= 0; i--)
        {
            string path = history[i];
            if (IsPinned(path)) continue;
            if (visibleIndex == index) return path;
            visibleIndex++;
        }

        return string.Empty;
    }

    public static bool IsPinned(string path)
    {
        return !string.IsNullOrEmpty(path) && pinnedHistory.Contains(path);
    }

    public static bool IsCurrentPath(string path)
    {
        return currentIndex >= 0 && currentIndex < history.Count && history[currentIndex] == path;
    }

    public static void ReloadSavedHistory()
    {
        LoadHistory();
        OnHistoryChanged?.Invoke();
    }

    [MenuItem("Tools/Prefab/返回上一个 (Back) &LEFT")]
    public static void GoBack()
    {
        if (!CanGoBack()) return;

        PrefabStage currentStage = PrefabStageUtility.GetCurrentPrefabStage();
        if (currentStage == null)
        {
            OpenPrefab(history[currentIndex]);
            return;
        }

        if (currentIndex > 0)
        {
            currentIndex--;
            OpenPrefab(history[currentIndex]);
        }
    }

    [MenuItem("Tools/Prefab/前进到下一个 (Forward) &RIGHT")]
    public static void GoForward()
    {
        if (!CanGoForward()) return;

        if (currentIndex < history.Count - 1)
        {
            currentIndex++;
            OpenPrefab(history[currentIndex]);
        }
    }

    public static void OpenHistoryItem(int index)
    {
        if (index < 0 || index >= history.Count) return;

        currentIndex = index;
        SaveHistory();
        OpenPrefab(history[currentIndex]);
    }

    public static void OpenHistoryPath(string path)
    {
        int index = history.IndexOf(path);
        if (index < 0) return;

        currentIndex = index;
        SaveHistory();
        OpenPrefab(path);
    }

    public static void RemoveHistoryItem(int index)
    {
        if (index < 0 || index >= history.Count) return;

        RemoveHistoryPath(history[index]);
    }

    public static void RemoveHistoryPath(string path)
    {
        int index = history.IndexOf(path);
        if (index < 0) return;

        history.RemoveAt(index);
        pinnedHistory.Remove(path);

        if (history.Count == 0)
        {
            currentIndex = -1;
        }
        else if (index == currentIndex)
        {
            currentIndex = -1;
        }
        else if (index < currentIndex)
        {
            currentIndex = Mathf.Clamp(currentIndex - 1, 0, history.Count - 1);
        }
        else if (currentIndex >= history.Count)
        {
            currentIndex = history.Count - 1;
        }

        SaveHistory();
        OnHistoryChanged?.Invoke();
    }

    public static void ClearHistory()
    {
        if (history.Count == 0) return;

        string currentPath = currentIndex >= 0 && currentIndex < history.Count ? history[currentIndex] : string.Empty;
        List<string> retainedHistory = new List<string>();
        for (int i = 0; i < history.Count; i++)
        {
            string path = history[i];
            if (!IsPinned(path) || retainedHistory.Contains(path)) continue;
            retainedHistory.Add(path);
        }

        if (retainedHistory.Count == history.Count) return;

        history = retainedHistory;
        pinnedHistory.RemoveAll(path => string.IsNullOrEmpty(path) || !history.Contains(path));
        currentIndex = IsPinned(currentPath) ? history.IndexOf(currentPath) : -1;

        SaveHistory();
        OnHistoryChanged?.Invoke();
    }

    public static IDisposable SuppressRecording()
    {
        recordingSuppressionDepth++;
        return new RecordingSuppressionScope();
    }

    public static void TogglePinned(string path)
    {
        if (string.IsNullOrEmpty(path) || !history.Contains(path)) return;

        if (pinnedHistory.Contains(path))
        {
            pinnedHistory.Remove(path);
        }
        else
        {
            pinnedHistory.Add(path);
        }

        SaveHistory();
        OnHistoryChanged?.Invoke();
    }

    private static void OpenPrefab(string path)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab != null)
        {
            try
            {
                isNavigating = true;
                AssetDatabase.OpenAsset(prefab);
            }
            finally
            {
                isNavigating = false;
            }

            SaveHistory();
            OnHistoryChanged?.Invoke();
        }
        else
        {
            Debug.LogWarning("找不到Prefab: " + path);
            OnHistoryChanged?.Invoke();
        }
    }

    private static void LoadHistory()
    {
        history = new List<string>();
        pinnedHistory = new List<string>();
        currentIndex = -1;

        string json = EditorPrefs.GetString(HistoryPrefsKey, string.Empty);
        if (string.IsNullOrEmpty(json)) return;

        try
        {
            PrefabHistoryState state = JsonUtility.FromJson<PrefabHistoryState>(json);
            if (state == null || state.history == null) return;

            foreach (string path in state.history)
            {
                if (string.IsNullOrEmpty(path)) continue;

                history.Remove(path);
                history.Add(path);
            }

            currentIndex = history.Count == 0 ? -1 : Mathf.Clamp(state.currentIndex, 0, history.Count - 1);

            if (state.pinnedHistory != null)
            {
                foreach (string path in state.pinnedHistory)
                {
                    if (string.IsNullOrEmpty(path) || !history.Contains(path) || pinnedHistory.Contains(path)) continue;
                    pinnedHistory.Add(path);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("读取Prefab历史记录失败: " + ex.Message);
            history = new List<string>();
            pinnedHistory = new List<string>();
            currentIndex = -1;
        }
    }

    private static void SaveHistory()
    {
        PrefabHistoryState state = new PrefabHistoryState
        {
            history = history,
            pinnedHistory = pinnedHistory,
            currentIndex = currentIndex
        };

        EditorPrefs.SetString(HistoryPrefsKey, JsonUtility.ToJson(state));
    }

    [Serializable]
    private class PrefabHistoryState
    {
        public List<string> history = new List<string>();
        public List<string> pinnedHistory = new List<string>();
        public int currentIndex = -1;
    }

    private class RecordingSuppressionScope : IDisposable
    {
        private bool disposed;

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            recordingSuppressionDepth = Mathf.Max(0, recordingSuppressionDepth - 1);
        }
    }
}

// ==========================================
// 2. UI 界面部分 (修复空按钮问题)
// ==========================================

// 注意：这里继承的是普通的 Button，而不是 EditorToolbarButton
[EditorToolbarElement(id, typeof(SceneView))]
class PrefabBackButton : Button
{
    public const string id = "PrefabNavigation/Back";
    
    public PrefabBackButton()
    {
        text = "◀ 返回";
        tooltip = "返回上一个打开的 Prefab (快捷键: Alt + 左箭头)";
        clicked += PrefabHistoryLogic.GoBack;
        
        // 关键1：给普通按钮穿上“工具栏”的皮肤（获得点击和悬停效果）
        AddToClassList("unity-editor-toolbar-button");
        
        // 关键2：解除工具栏按钮固定的死宽度，让文字能撑开按钮
        style.width = StyleKeyword.Auto; 
        style.paddingLeft = 8;
        style.paddingRight = 8;
        style.unityFontStyleAndWeight = FontStyle.Bold;
        
        RegisterCallback<AttachToPanelEvent>(OnAttach);
        RegisterCallback<DetachFromPanelEvent>(OnDetach);
    }

    private void OnAttach(AttachToPanelEvent evt)
    {
        RefreshState();
        PrefabHistoryLogic.OnHistoryChanged += RefreshState;
    }

    private void OnDetach(DetachFromPanelEvent evt)
    {
        PrefabHistoryLogic.OnHistoryChanged -= RefreshState;
    }

    private void RefreshState() => SetEnabled(PrefabHistoryLogic.CanGoBack());
}

[EditorToolbarElement(id, typeof(SceneView))]
class PrefabHistoryButton : Button
{
    public const string id = "PrefabNavigation/History";

    public PrefabHistoryButton()
    {
        text = "历史记录";
        tooltip = "查看所有打开过的 Prefab";
        clicked += ShowHistoryPopup;

        AddToClassList("unity-editor-toolbar-button");
        style.width = StyleKeyword.Auto;
        style.paddingLeft = 10;
        style.paddingRight = 10;
        style.unityFontStyleAndWeight = FontStyle.Bold;

        RegisterCallback<AttachToPanelEvent>(OnAttach);
        RegisterCallback<DetachFromPanelEvent>(OnDetach);
    }

    private void OnAttach(AttachToPanelEvent evt)
    {
        RefreshState();
        PrefabHistoryLogic.OnHistoryChanged += RefreshState;
    }

    private void OnDetach(DetachFromPanelEvent evt)
    {
        PrefabHistoryLogic.OnHistoryChanged -= RefreshState;
    }

    private void ShowHistoryPopup()
    {
        PrefabHistoryLogic.ReloadSavedHistory();
        if (PrefabHistoryLogic.HistoryCount <= 0) return;

        PrefabHistoryWindow.ShowHistoryWindow(worldBound);
    }

    private void RefreshState() => SetEnabled(PrefabHistoryLogic.HistoryCount > 0);
}

class PrefabHistoryWindow : EditorWindow
{
    private const float Width = 382f;
    private const float RowHeight = 36f;
    private const float PinButtonWidth = 32f;
    private const float CloseButtonWidth = 32f;
    private const float Padding = 10f;
    private const float SectionTitleHeight = 24f;
    private const float FooterHeight = 42f;
    private const float ClearButtonWidth = 104f;
    private const float ClearButtonHeight = 28f;
    private const int MaxVisibleRows = 10;
    private static readonly Color NormalRowColor = new Color(0.28f, 0.28f, 0.28f, 1f);
    private static readonly Color HoverRowColor = new Color(0.36f, 0.36f, 0.36f, 1f);
    private static readonly Color ClearButtonColor = new Color(0.24f, 0.24f, 0.24f, 1f);
    private static readonly Color ClearButtonHoverColor = new Color(0.32f, 0.32f, 0.32f, 1f);
    private static readonly Color TextColor = new Color(0.88f, 0.88f, 0.88f, 1f);
    private static readonly Color ButtonColor = new Color(0.7f, 0.7f, 0.7f, 1f);
    private static readonly Color SectionTitleColor = new Color(0.68f, 0.68f, 0.68f, 1f);
    private const int RowCornerRadius = 5;

    private static PrefabHistoryWindow instance;

    private Vector2 scrollPosition;
    private GUIStyle rowTextStyle;
    private GUIStyle sectionTitleStyle;
    private GUIStyle clearButtonStyle;
    private GUIStyle normalRowBackgroundStyle;
    private GUIStyle hoverRowBackgroundStyle;
    private Texture2D normalRowTexture;
    private Texture2D hoverRowTexture;
    private Texture2D clearButtonTexture;
    private Texture2D clearButtonHoverTexture;

    public static void ShowHistoryWindow(Rect activatorRect)
    {
        PrefabHistoryLogic.ReloadSavedHistory();

        if (instance == null)
        {
            instance = CreateInstance<PrefabHistoryWindow>();
            instance.titleContent = new GUIContent("Prefab历史记录");
            instance.minSize = new Vector2(Width, 140f);
            instance.position = GetInitialPosition(activatorRect);
            instance.ShowUtility();
        }
        else
        {
            instance.Focus();
            instance.Repaint();
        }
    }

    private static Rect GetInitialPosition(Rect activatorRect)
    {
        int normalVisibleRows = PrefabHistoryLogic.ScrollableHistoryCount > 0
            ? Mathf.Clamp(PrefabHistoryLogic.ScrollableHistoryCount, 1, MaxVisibleRows)
            : 0;
        float pinnedHeight = PrefabHistoryLogic.PinnedCount > 0
            ? SectionTitleHeight + PrefabHistoryLogic.PinnedCount * RowHeight
            : 0f;
        float height = Mathf.Clamp(Padding * 2 + pinnedHeight + normalVisibleRows * RowHeight + FooterHeight, 180f, 520f);
        return new Rect(activatorRect.x, activatorRect.yMax + 4f, Width, height);
    }

    private void OnEnable()
    {
        wantsMouseMove = true;
        PrefabHistoryLogic.OnHistoryChanged += Repaint;
        ScrollToCurrent();
    }

    private void OnDisable()
    {
        PrefabHistoryLogic.OnHistoryChanged -= Repaint;
        if (instance == this)
        {
            instance = null;
        }
    }

    private void OnGUI()
    {
        EnsureStyles();

        if (PrefabHistoryLogic.HistoryCount <= 0)
        {
            EditorGUILayout.LabelField("暂无 Prefab 历史记录", EditorStyles.centeredGreyMiniLabel, GUILayout.Height(RowHeight));
            return;
        }

        string openPath = string.Empty;
        string removePath = string.Empty;
        string togglePinPath = string.Empty;
        bool clearHistory = false;

        if (PrefabHistoryLogic.PinnedCount > 0)
        {
            DrawSectionTitle("已置顶");
        }

        if (PrefabHistoryLogic.PinnedCount > 0)
        {
            Rect pinnedBlockRect = GUILayoutUtility.GetRect(
                0f,
                PrefabHistoryLogic.PinnedCount * RowHeight,
                GUILayout.ExpandWidth(true));
            for (int i = 0; i < PrefabHistoryLogic.PinnedCount; i++)
            {
                string path = PrefabHistoryLogic.GetPinnedPath(i);
                Rect rowRect = new Rect(
                    pinnedBlockRect.x,
                    pinnedBlockRect.y + i * RowHeight,
                    pinnedBlockRect.width,
                    RowHeight);
                DrawHistoryRow(path, rowRect, true, ref openPath, ref removePath, ref togglePinPath);
            }
        }

        if (PrefabHistoryLogic.PinnedCount > 0 && PrefabHistoryLogic.ScrollableHistoryCount > 0)
        {
            GUILayout.Space(4f);
            DrawDivider();
        }

        if (PrefabHistoryLogic.ScrollableHistoryCount > 0)
        {
            float scrollViewHeight = GetScrollViewHeight();
            bool needScrollBar = PrefabHistoryLogic.ScrollableHistoryCount * RowHeight > scrollViewHeight;
            scrollPosition = EditorGUILayout.BeginScrollView(
                scrollPosition,
                false,
                needScrollBar,
                GUILayout.Height(scrollViewHeight));

            Rect scrollContentRect = GUILayoutUtility.GetRect(
                0f,
                PrefabHistoryLogic.ScrollableHistoryCount * RowHeight,
                GUILayout.ExpandWidth(true));
            for (int i = 0; i < PrefabHistoryLogic.ScrollableHistoryCount; i++)
            {
                string path = PrefabHistoryLogic.GetScrollableHistoryPath(i);
                Rect rowRect = new Rect(
                    scrollContentRect.x,
                    scrollContentRect.y + i * RowHeight,
                    scrollContentRect.width,
                    RowHeight);
                DrawHistoryRow(path, rowRect, false, ref openPath, ref removePath, ref togglePinPath);
            }

            EditorGUILayout.EndScrollView();
        }

        GUILayout.FlexibleSpace();
        clearHistory = DrawClearHistoryButton();

        if (Event.current.type == EventType.MouseMove)
        {
            Repaint();
        }

        if (clearHistory)
        {
            if (EditorUtility.DisplayDialog("Prefab历史记录", "确定要清空未置顶的 Prefab 历史记录吗？置顶记录会保留。", "清空", "取消"))
            {
                PrefabHistoryLogic.ClearHistory();
                Repaint();
            }
        }
        else if (!string.IsNullOrEmpty(removePath))
        {
            PrefabHistoryLogic.RemoveHistoryPath(removePath);
            Repaint();
        }
        else if (!string.IsNullOrEmpty(togglePinPath))
        {
            PrefabHistoryLogic.TogglePinned(togglePinPath);
            Repaint();
        }
        else if (!string.IsNullOrEmpty(openPath))
        {
            PrefabHistoryLogic.OpenHistoryPath(openPath);
            Repaint();
        }
    }

    private void DrawHistoryRow(
        string path,
        Rect allocatedRect,
        bool isPinned,
        ref string openPath,
        ref string removePath,
        ref string togglePinPath)
    {
        if (string.IsNullOrEmpty(path)) return;

        Rect hitRect = allocatedRect;
        hitRect.x += Padding;
        hitRect.width -= Padding * 2;

        Rect visualRect = hitRect;
        visualRect.y += 3;
        visualRect.height -= 6;

        bool isCurrent = PrefabHistoryLogic.IsCurrentPath(path);
        bool isHover = hitRect.Contains(Event.current.mousePosition);
        GUI.Box(visualRect, GUIContent.none, isCurrent || isHover ? hoverRowBackgroundStyle : normalRowBackgroundStyle);

        Rect pinRect = hitRect;
        pinRect.width = PinButtonWidth;

        Rect labelRect = hitRect;
        labelRect.x += PinButtonWidth + 8;
        labelRect.width -= PinButtonWidth + CloseButtonWidth + 16;

        Rect closeRect = hitRect;
        closeRect.x = hitRect.xMax - CloseButtonWidth;
        closeRect.width = CloseButtonWidth;

        if (isHover)
        {
            bool isPinHover = pinRect.Contains(Event.current.mousePosition);
            Color pinColor = isPinHover ? TextColor : ButtonColor;
            if (GUI.Button(pinRect, new GUIContent(string.Empty, isPinned ? "取消置顶" : "置顶"), GUIStyle.none))
            {
                togglePinPath = path;
            }
            DrawPinIcon(pinRect, isPinned, pinColor);
        }

        GUIContent labelContent = new GUIContent(GetPrefabDisplayName(path), path);
        if (GUI.Button(labelRect, labelContent, rowTextStyle))
        {
            openPath = path;
        }

        if (isHover)
        {
            bool isCloseHover = closeRect.Contains(Event.current.mousePosition);
            Color closeColor = isCloseHover ? TextColor : ButtonColor;
            if (GUI.Button(closeRect, new GUIContent(string.Empty, "删除"), GUIStyle.none))
            {
                removePath = path;
            }
            DrawCloseIcon(closeRect, closeColor);
        }
    }

    private float GetScrollViewHeight()
    {
        float reservedHeight = Padding * 2 + FooterHeight + PrefabHistoryLogic.PinnedCount * RowHeight;
        if (PrefabHistoryLogic.PinnedCount > 0)
        {
            reservedHeight += SectionTitleHeight;
        }

        if (PrefabHistoryLogic.PinnedCount > 0 && PrefabHistoryLogic.ScrollableHistoryCount > 0)
        {
            reservedHeight += 8f;
        }

        float availableHeight = Mathf.Max(RowHeight, position.height - reservedHeight);
        return availableHeight;
    }

    private bool DrawClearHistoryButton()
    {
        Rect footerRect = GUILayoutUtility.GetRect(0f, FooterHeight, GUILayout.ExpandWidth(true));
        Rect buttonRect = new Rect(
            footerRect.xMax - Padding - ClearButtonWidth,
            footerRect.y + (FooterHeight - ClearButtonHeight) * 0.5f,
            ClearButtonWidth,
            ClearButtonHeight);
        return GUI.Button(buttonRect, "清空记录", clearButtonStyle);
    }

    private void DrawSectionTitle(string title)
    {
        Rect rect = EditorGUILayout.GetControlRect(false, SectionTitleHeight);
        rect.x += Padding + PinButtonWidth + 8f;
        rect.width -= Padding * 2 + PinButtonWidth + 8f;
        rect.y += 2f;
        GUI.Label(rect, title, sectionTitleStyle);
    }

    private void DrawPinIcon(Rect rect, bool isPinned, Color color)
    {
        Rect iconRect = new Rect(
            rect.x + (rect.width - 18f) * 0.5f,
            rect.y + (rect.height - 18f) * 0.5f,
            18f,
            18f);

        float centerX = Mathf.Floor(iconRect.center.x) + 0.5f;
        float barY = Mathf.Floor(iconRect.y + 3f) + 0.5f;
        float arrowTopY = Mathf.Floor(iconRect.y + 5f) + 0.5f;
        float wingY = Mathf.Floor(iconRect.y + 10f) + 0.5f;
        float stemBottomY = Mathf.Floor(iconRect.yMax - 3f) + 0.5f;
        Vector3 barLeft = new Vector3(centerX - 7f, barY, 0f);
        Vector3 barRight = new Vector3(centerX + 7f, barY, 0f);
        Vector3 arrowTop = new Vector3(centerX, arrowTopY, 0f);
        Vector3 stemBottom = new Vector3(centerX, stemBottomY, 0f);
        Vector3 leftWing = new Vector3(centerX - 5f, wingY, 0f);
        Vector3 rightWing = new Vector3(centerX + 5f, wingY, 0f);
        float lineWidth = isPinned ? 2.9f : 2.3f;

        Handles.BeginGUI();
        Color previousColor = Handles.color;
        Handles.color = color;

        Handles.DrawAAPolyLine(lineWidth, barLeft, barRight);
        Handles.DrawAAPolyLine(lineWidth, arrowTop, stemBottom);
        Handles.DrawAAPolyLine(lineWidth, leftWing, arrowTop);
        Handles.DrawAAPolyLine(lineWidth, arrowTop, rightWing);
        Handles.color = previousColor;
        Handles.EndGUI();
    }

    private void DrawCloseIcon(Rect rect, Color color)
    {
        Rect iconRect = new Rect(
            rect.x + (rect.width - 14f) * 0.5f,
            rect.y + (rect.height - 14f) * 0.5f,
            14f,
            14f);

        Vector3 leftTop = new Vector3(iconRect.x + 2f, iconRect.y + 2f, 0f);
        Vector3 rightTop = new Vector3(iconRect.xMax - 2f, iconRect.y + 2f, 0f);
        Vector3 leftBottom = new Vector3(iconRect.x + 2f, iconRect.yMax - 2f, 0f);
        Vector3 rightBottom = new Vector3(iconRect.xMax - 2f, iconRect.yMax - 2f, 0f);

        Handles.BeginGUI();
        Color previousColor = Handles.color;
        Handles.color = color;
        Handles.DrawAAPolyLine(2.3f, leftTop, rightBottom);
        Handles.DrawAAPolyLine(2.3f, rightTop, leftBottom);
        Handles.color = previousColor;
        Handles.EndGUI();
    }

    private void ScrollToCurrent()
    {
        string currentPath = PrefabHistoryLogic.GetHistoryPath(PrefabHistoryLogic.CurrentIndex);
        if (string.IsNullOrEmpty(currentPath) || PrefabHistoryLogic.IsPinned(currentPath))
        {
            return;
        }

        int visibleIndex = 0;
        for (int i = PrefabHistoryLogic.HistoryCount - 1; i >= 0; i--)
        {
            string path = PrefabHistoryLogic.GetHistoryPath(i);
            if (PrefabHistoryLogic.IsPinned(path)) continue;
            if (path == currentPath) break;
            visibleIndex++;
        }

        float currentBottom = (visibleIndex + 1) * RowHeight;
        scrollPosition.y = Mathf.Max(0f, currentBottom - GetScrollViewHeight());
    }

    private void DrawDivider()
    {
        Rect rect = EditorGUILayout.GetControlRect(false, 1f);
        rect.x += Padding;
        rect.width -= Padding * 2;
        EditorGUI.DrawRect(rect, new Color(0.35f, 0.35f, 0.35f, 1f));
    }

    private void EnsureStyles()
    {
        if (normalRowBackgroundStyle == null)
        {
            normalRowTexture = CreateRoundedTexture(96, 36, RowCornerRadius, NormalRowColor);
            normalRowBackgroundStyle = new GUIStyle
            {
                normal = { background = normalRowTexture },
                border = new RectOffset(RowCornerRadius, RowCornerRadius, RowCornerRadius, RowCornerRadius)
            };
        }

        if (hoverRowBackgroundStyle == null)
        {
            hoverRowTexture = CreateRoundedTexture(96, 36, RowCornerRadius, HoverRowColor);
            hoverRowBackgroundStyle = new GUIStyle
            {
                normal = { background = hoverRowTexture },
                border = new RectOffset(RowCornerRadius, RowCornerRadius, RowCornerRadius, RowCornerRadius)
            };
        }

        if (rowTextStyle == null)
        {
            rowTextStyle = new GUIStyle(EditorStyles.label)
            {
                alignment = TextAnchor.MiddleLeft,
                fontSize = 14,
                clipping = TextClipping.Clip,
                padding = new RectOffset(0, 0, 0, 0)
            };
            rowTextStyle.normal.textColor = TextColor;
            rowTextStyle.hover.textColor = TextColor;
            rowTextStyle.active.textColor = TextColor;
        }

        if (sectionTitleStyle == null)
        {
            sectionTitleStyle = new GUIStyle(EditorStyles.label)
            {
                alignment = TextAnchor.MiddleLeft,
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                clipping = TextClipping.Clip,
                padding = new RectOffset(0, 0, 0, 0)
            };
            sectionTitleStyle.normal.textColor = SectionTitleColor;
        }

        if (clearButtonStyle == null)
        {
            clearButtonTexture = CreateRoundedTexture(96, 28, RowCornerRadius, ClearButtonColor);
            clearButtonHoverTexture = CreateRoundedTexture(96, 28, RowCornerRadius, ClearButtonHoverColor);
            clearButtonStyle = new GUIStyle(EditorStyles.miniButton)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 12,
                clipping = TextClipping.Clip,
                padding = new RectOffset(0, 0, 0, 0),
                border = new RectOffset(RowCornerRadius, RowCornerRadius, RowCornerRadius, RowCornerRadius)
            };
            clearButtonStyle.normal.background = clearButtonTexture;
            clearButtonStyle.hover.background = clearButtonHoverTexture;
            clearButtonStyle.active.background = clearButtonHoverTexture;
            clearButtonStyle.normal.textColor = TextColor;
            clearButtonStyle.hover.textColor = TextColor;
            clearButtonStyle.active.textColor = TextColor;
        }
    }

    private Texture2D CreateRoundedTexture(int width, int height, int radius, Color color)
    {
        Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
        {
            hideFlags = HideFlags.HideAndDontSave,
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };

        Color clear = new Color(0f, 0f, 0f, 0f);
        Color[] pixels = new Color[width * height];
        float antiAliasWidth = 1.4f;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float px = x + 0.5f;
                float py = y + 0.5f;
                float nearestX = Mathf.Clamp(px, radius, width - radius);
                float nearestY = Mathf.Clamp(py, radius, height - radius);
                float distance = Vector2.Distance(new Vector2(px, py), new Vector2(nearestX, nearestY));
                float alpha = 1f;

                if ((px < radius || px > width - radius) && (py < radius || py > height - radius))
                {
                    alpha = Mathf.Clamp01((radius - distance + antiAliasWidth) / (antiAliasWidth * 2f));
                }

                Color pixelColor = alpha <= 0f ? clear : color;
                pixelColor.a *= alpha;
                pixels[y * width + x] = pixelColor;
            }
        }

        texture.SetPixels(pixels);
        texture.Apply();
        return texture;
    }

    private string GetPrefabDisplayName(string path)
    {
        if (string.IsNullOrEmpty(path)) return "(Missing Prefab)";

        string fileName = Path.GetFileNameWithoutExtension(path);
        return string.IsNullOrEmpty(fileName) ? path : fileName;
    }
}

[EditorToolbarElement(id, typeof(SceneView))]
class PrefabForwardButton : Button
{
    public const string id = "PrefabNavigation/Forward";
    
    public PrefabForwardButton()
    {
        text = "前进 ▶";
        tooltip = "前进到下一个打开的 Prefab (快捷键: Alt + 右箭头)";
        clicked += PrefabHistoryLogic.GoForward;
        
        AddToClassList("unity-editor-toolbar-button");
        style.width = StyleKeyword.Auto;
        style.paddingLeft = 8;
        style.paddingRight = 8;
        style.unityFontStyleAndWeight = FontStyle.Bold;
        
        RegisterCallback<AttachToPanelEvent>(OnAttach);
        RegisterCallback<DetachFromPanelEvent>(OnDetach);
    }

    private void OnAttach(AttachToPanelEvent evt)
    {
        RefreshState();
        PrefabHistoryLogic.OnHistoryChanged += RefreshState;
    }

    private void OnDetach(DetachFromPanelEvent evt)
    {
        PrefabHistoryLogic.OnHistoryChanged -= RefreshState;
    }

    private void RefreshState() => SetEnabled(PrefabHistoryLogic.CanGoForward());
}

[Overlay(typeof(SceneView), "PrefabNavigationOverlay", "Prefab导航", true)]
public class PrefabNavigationOverlay : ToolbarOverlay
{
    public PrefabNavigationOverlay() : base(
        PrefabHistoryButton.id,
        PrefabBackButton.id,
        PrefabForwardButton.id,
        UIPcAdaptationPreviewButton.id)
    {
    }
}

[InitializeOnLoad]
static class PrefabNavigationOverlayDefaultVisibility
{
    private const string OverlayId = "PrefabNavigationOverlay";
    private static readonly MethodInfo SetOverlayVisibleMethod = typeof(SceneView).GetMethod(
        "SetOverlayVisible",
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    private static bool appliedDefaultVisibility;

    static PrefabNavigationOverlayDefaultVisibility()
    {
        EditorApplication.delayCall += ApplyToOpenSceneViews;
        SceneView.duringSceneGui += ApplyOnFirstSceneGui;
    }

    private static void ApplyToOpenSceneViews()
    {
        bool didApply = false;
        foreach (SceneView sceneView in SceneView.sceneViews)
        {
            didApply |= ShowOverlay(sceneView);
        }

        if (didApply)
        {
            appliedDefaultVisibility = true;
        }
    }

    private static void ApplyOnFirstSceneGui(SceneView sceneView)
    {
        if (appliedDefaultVisibility)
        {
            SceneView.duringSceneGui -= ApplyOnFirstSceneGui;
            return;
        }

        if (ShowOverlay(sceneView))
        {
            appliedDefaultVisibility = true;
            SceneView.duringSceneGui -= ApplyOnFirstSceneGui;
        }
    }

    private static bool ShowOverlay(SceneView sceneView)
    {
        if (sceneView == null)
        {
            return false;
        }

        Overlay overlay;
        if (sceneView.TryGetOverlay(OverlayId, out overlay) && overlay != null)
        {
            overlay.displayed = true;
            return true;
        }

        if (SetOverlayVisibleMethod == null)
        {
            return false;
        }

        SetOverlayVisibleMethod.Invoke(sceneView, new object[] { OverlayId, true });
        return true;
    }
}
