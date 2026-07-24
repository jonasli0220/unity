using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.ShortcutManagement;
using UnityEngine;
using UnityEngine.UIElements;

[InitializeOnLoad]
internal static class ProjectFolderHistory
{
    private const string SessionStateKey = "Dragon.ProjectFolderHistory.State.v1";
    private const string ToolbarName = "dragon-project-folder-history-toolbar";
    private const string BackButtonName = "dragon-project-folder-history-back";
    private const string ForwardButtonName = "dragon-project-folder-history-forward";
    private const string ShortcutPrefix = "Dragon/Project Folder History/";
    private const int MaxHistoryCount = 64;
    private const float ToolbarRightOffset = 646f;
    private const float ToolbarWidth = 58f;
    private const float ButtonWidth = 28f;
    private const float ButtonHeight = 20f;
    private const float MinimumToolbarWindowWidth = 860f;
    private const double UpdateIntervalSeconds = 0.1d;
    private const double NavigationTimeoutSeconds = 1d;

    private const BindingFlags InstanceMemberFlags =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly Type ProjectBrowserType =
        typeof(EditorWindow).Assembly.GetType("UnityEditor.ProjectBrowser");
    private static readonly MethodInfo GetActiveFolderPathMethod = ProjectBrowserType == null
        ? null
        : ProjectBrowserType.GetMethod(
            "GetActiveFolderPath",
            InstanceMemberFlags,
            null,
            Type.EmptyTypes,
            null);
    private static readonly MethodInfo ShowFolderContentsMethod = ProjectBrowserType == null
        ? null
        : ProjectBrowserType.GetMethod(
            "ShowFolderContents",
            InstanceMemberFlags,
            null,
            new[] { typeof(int), typeof(bool) },
            null);
    private static readonly Dictionary<int, HistoryState> HistoryByBrowserId =
        new Dictionary<int, HistoryState>();

    private static double nextUpdateTime;
    private static bool sessionStateLoaded;
    private static bool warnedReflectionUnavailable;
    private static bool warnedNavigationFailure;

    static ProjectFolderHistory()
    {
        LoadSessionState();
        EditorApplication.update -= OnEditorUpdate;
        EditorApplication.update += OnEditorUpdate;
        EditorApplication.delayCall += RefreshAllProjectBrowsers;
    }

    [Shortcut(ShortcutPrefix + "Back", KeyCode.LeftArrow, ShortcutModifiers.Alt)]
    private static void GoBackShortcut()
    {
        NavigateFocusedProjectBrowser(-1);
    }

    [Shortcut(ShortcutPrefix + "Forward", KeyCode.RightArrow, ShortcutModifiers.Alt)]
    private static void GoForwardShortcut()
    {
        NavigateFocusedProjectBrowser(1);
    }

    private static bool ReflectionIsAvailable
    {
        get
        {
            return ProjectBrowserType != null
                && GetActiveFolderPathMethod != null
                && ShowFolderContentsMethod != null;
        }
    }

    private static void OnEditorUpdate()
    {
        double now = EditorApplication.timeSinceStartup;
        if (now < nextUpdateTime)
        {
            return;
        }

        nextUpdateTime = now + UpdateIntervalSeconds;
        RefreshAllProjectBrowsers();
    }

    private static void RefreshAllProjectBrowsers()
    {
        if (!ReflectionIsAvailable)
        {
            WarnReflectionUnavailableOnce();
            return;
        }

        UnityEngine.Object[] browserObjects = Resources.FindObjectsOfTypeAll(ProjectBrowserType);
        for (int index = 0; index < browserObjects.Length; index++)
        {
            EditorWindow browser = browserObjects[index] as EditorWindow;
            if (browser == null || browser.rootVisualElement == null)
            {
                continue;
            }

            HistoryState state = GetOrCreateState(browser);
            ObserveActiveFolder(browser, state);
            EnsureToolbar(browser, state);
        }
    }

    private static HistoryState GetOrCreateState(EditorWindow browser)
    {
        int browserId = browser.GetInstanceID();
        HistoryState state;
        if (!HistoryByBrowserId.TryGetValue(browserId, out state))
        {
            state = new HistoryState
            {
                browserInstanceId = browserId
            };
            HistoryByBrowserId.Add(browserId, state);
        }

        if (state.entries == null)
        {
            state.entries = new List<string>();
        }

        state.index = Mathf.Clamp(state.index, 0, Mathf.Max(0, state.entries.Count - 1));
        return state;
    }

    private static void ObserveActiveFolder(EditorWindow browser, HistoryState state)
    {
        string activePath = GetActiveFolderPath(browser);
        if (!IsValidFolder(activePath))
        {
            return;
        }

        if (state.entries.Count == 0)
        {
            state.entries.Add(activePath);
            state.index = 0;
            state.lastObservedPath = activePath;
            state.pendingTargetPath = null;
            SaveSessionState();
            return;
        }

        if (!string.IsNullOrEmpty(state.pendingTargetPath))
        {
            if (PathsEqual(activePath, state.pendingTargetPath))
            {
                state.pendingTargetPath = null;
                state.pendingPreviousIndex = -1;
                state.lastObservedPath = activePath;
                SaveSessionState();
                return;
            }

            if (EditorApplication.timeSinceStartup < state.pendingDeadline)
            {
                return;
            }

            int previousIndex = state.pendingPreviousIndex;
            state.pendingTargetPath = null;
            state.pendingPreviousIndex = -1;
            if (previousIndex >= 0
                && previousIndex < state.entries.Count
                && PathsEqual(activePath, state.entries[previousIndex]))
            {
                state.index = previousIndex;
                state.lastObservedPath = activePath;
                SaveSessionState();
                return;
            }
        }

        if (PathsEqual(activePath, state.lastObservedPath))
        {
            return;
        }

        state.lastObservedPath = activePath;
        if (state.index >= 0
            && state.index < state.entries.Count
            && PathsEqual(activePath, state.entries[state.index]))
        {
            return;
        }

        if (state.index < state.entries.Count - 1)
        {
            state.entries.RemoveRange(
                state.index + 1,
                state.entries.Count - state.index - 1);
        }

        if (state.entries.Count == 0
            || !PathsEqual(activePath, state.entries[state.entries.Count - 1]))
        {
            state.entries.Add(activePath);
        }

        state.index = state.entries.Count - 1;
        TrimHistory(state);
        SaveSessionState();
    }

    private static void EnsureToolbar(EditorWindow browser, HistoryState state)
    {
        VisualElement root = browser.rootVisualElement;
        VisualElement toolbar = root.Q<VisualElement>(ToolbarName);
        if (toolbar == null)
        {
            toolbar = CreateToolbar(browser);
            root.Add(toolbar);
        }

        toolbar.style.display = browser.position.width >= MinimumToolbarWindowWidth
            ? DisplayStyle.Flex
            : DisplayStyle.None;
        toolbar.BringToFront();
        UpdateToolbarState(toolbar, state);
    }

    private static VisualElement CreateToolbar(EditorWindow browser)
    {
        VisualElement toolbar = new VisualElement
        {
            name = ToolbarName,
            tooltip = "\u6d4f\u89c8 Project \u6587\u4ef6\u5939\u5386\u53f2"
        };

        toolbar.style.position = Position.Absolute;
        toolbar.style.top = 1f;
        toolbar.style.right = ToolbarRightOffset;
        toolbar.style.width = ToolbarWidth;
        toolbar.style.height = ButtonHeight;
        toolbar.style.flexDirection = FlexDirection.Row;
        toolbar.style.alignItems = Align.Center;

        Button backButton = CreateToolbarButton(
            BackButtonName,
            "\u2190",
            () => Navigate(browser, -1));
        Button forwardButton = CreateToolbarButton(
            ForwardButtonName,
            "\u2192",
            () => Navigate(browser, 1));

        backButton.style.marginRight = 2f;
        toolbar.Add(backButton);
        toolbar.Add(forwardButton);
        return toolbar;
    }

    private static Button CreateToolbarButton(string name, string text, Action clicked)
    {
        Button button = new Button(clicked)
        {
            name = name,
            text = text,
            focusable = false
        };

        button.AddToClassList("unity-editor-toolbar-button");
        button.style.width = ButtonWidth;
        button.style.minWidth = ButtonWidth;
        button.style.maxWidth = ButtonWidth;
        button.style.height = ButtonHeight;
        button.style.minHeight = ButtonHeight;
        button.style.maxHeight = ButtonHeight;
        button.style.marginLeft = 0f;
        button.style.marginTop = 0f;
        button.style.marginBottom = 0f;
        button.style.paddingLeft = 0f;
        button.style.paddingRight = 0f;
        button.style.unityTextAlign = TextAnchor.MiddleCenter;
        button.style.fontSize = 13f;
        return button;
    }

    private static void UpdateToolbarState(VisualElement toolbar, HistoryState state)
    {
        Button backButton = toolbar.Q<Button>(BackButtonName);
        Button forwardButton = toolbar.Q<Button>(ForwardButtonName);

        int backIndex;
        bool canGoBack = TryGetTargetIndex(state, -1, out backIndex);
        int forwardIndex;
        bool canGoForward = TryGetTargetIndex(state, 1, out forwardIndex);

        if (backButton != null)
        {
            backButton.SetEnabled(canGoBack);
            backButton.tooltip = canGoBack
                ? "\u540e\u9000\u5230\uff1a" + state.entries[backIndex] + "  (Alt+\u2190)"
                : "\u6ca1\u6709\u53ef\u540e\u9000\u7684\u6587\u4ef6\u5939  (Alt+\u2190)";
        }

        if (forwardButton != null)
        {
            forwardButton.SetEnabled(canGoForward);
            forwardButton.tooltip = canGoForward
                ? "\u524d\u8fdb\u5230\uff1a" + state.entries[forwardIndex] + "  (Alt+\u2192)"
                : "\u6ca1\u6709\u53ef\u524d\u8fdb\u7684\u6587\u4ef6\u5939  (Alt+\u2192)";
        }
    }

    private static void NavigateFocusedProjectBrowser(int direction)
    {
        EditorWindow browser = EditorWindow.focusedWindow;
        if (!IsProjectBrowser(browser))
        {
            return;
        }

        Navigate(browser, direction);
    }

    private static void Navigate(EditorWindow browser, int direction)
    {
        if (!ReflectionIsAvailable || !IsProjectBrowser(browser))
        {
            WarnReflectionUnavailableOnce();
            return;
        }

        HistoryState state = GetOrCreateState(browser);
        ObserveActiveFolder(browser, state);

        int targetIndex;
        if (!TryGetTargetIndex(state, direction, out targetIndex))
        {
            UpdateBrowserToolbarState(browser, state);
            return;
        }

        string targetPath = state.entries[targetIndex];
        UnityEngine.Object targetFolder =
            AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(targetPath);
        if (targetFolder == null)
        {
            UpdateBrowserToolbarState(browser, state);
            return;
        }

        int previousIndex = state.index;
        state.index = targetIndex;
        state.pendingTargetPath = targetPath;
        state.pendingPreviousIndex = previousIndex;
        state.pendingDeadline =
            EditorApplication.timeSinceStartup + NavigationTimeoutSeconds;

        try
        {
            ShowFolderContentsMethod.Invoke(
                browser,
                new object[] { targetFolder.GetInstanceID(), true });
            browser.Repaint();
            UpdateBrowserToolbarState(browser, state);
            SaveSessionState();
            EditorApplication.delayCall += () =>
            {
                if (browser == null)
                {
                    return;
                }

                ObserveActiveFolder(browser, state);
                UpdateBrowserToolbarState(browser, state);
            };
        }
        catch (Exception exception)
        {
            state.index = previousIndex;
            state.pendingTargetPath = null;
            state.pendingPreviousIndex = -1;
            WarnNavigationFailureOnce(exception);
            UpdateBrowserToolbarState(browser, state);
        }
    }

    private static bool TryGetTargetIndex(
        HistoryState state,
        int direction,
        out int targetIndex)
    {
        targetIndex = -1;
        if (state == null || state.entries == null || direction == 0)
        {
            return false;
        }

        int candidate = state.index + Math.Sign(direction);
        while (candidate >= 0 && candidate < state.entries.Count)
        {
            if (IsValidFolder(state.entries[candidate]))
            {
                targetIndex = candidate;
                return true;
            }

            candidate += Math.Sign(direction);
        }

        return false;
    }

    private static void UpdateBrowserToolbarState(
        EditorWindow browser,
        HistoryState state)
    {
        if (browser == null || browser.rootVisualElement == null)
        {
            return;
        }

        VisualElement toolbar =
            browser.rootVisualElement.Q<VisualElement>(ToolbarName);
        if (toolbar != null)
        {
            UpdateToolbarState(toolbar, state);
        }
    }

    private static string GetActiveFolderPath(EditorWindow browser)
    {
        try
        {
            return NormalizePath(
                GetActiveFolderPathMethod.Invoke(browser, null) as string);
        }
        catch (Exception exception)
        {
            WarnNavigationFailureOnce(exception);
            return null;
        }
    }

    private static bool IsProjectBrowser(EditorWindow window)
    {
        return window != null
            && ProjectBrowserType != null
            && ProjectBrowserType.IsInstanceOfType(window);
    }

    private static bool IsValidFolder(string path)
    {
        return !string.IsNullOrEmpty(path) && AssetDatabase.IsValidFolder(path);
    }

    private static bool PathsEqual(string left, string right)
    {
        return string.Equals(
            NormalizePath(left),
            NormalizePath(right),
            StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        return path.Replace('\\', '/').TrimEnd('/');
    }

    private static void TrimHistory(HistoryState state)
    {
        int excessCount = state.entries.Count - MaxHistoryCount;
        if (excessCount <= 0)
        {
            return;
        }

        state.entries.RemoveRange(0, excessCount);
        state.index = Mathf.Max(0, state.index - excessCount);
    }

    private static void LoadSessionState()
    {
        if (sessionStateLoaded)
        {
            return;
        }

        sessionStateLoaded = true;
        string json = SessionState.GetString(SessionStateKey, string.Empty);
        if (string.IsNullOrEmpty(json))
        {
            return;
        }

        try
        {
            HistoryStore store = JsonUtility.FromJson<HistoryStore>(json);
            if (store == null || store.states == null)
            {
                return;
            }

            for (int index = 0; index < store.states.Count; index++)
            {
                HistoryState state = store.states[index];
                if (state == null || state.browserInstanceId == 0)
                {
                    continue;
                }

                if (state.entries == null)
                {
                    state.entries = new List<string>();
                }

                state.index = Mathf.Clamp(
                    state.index,
                    0,
                    Mathf.Max(0, state.entries.Count - 1));
                HistoryByBrowserId[state.browserInstanceId] = state;
            }
        }
        catch (Exception)
        {
            SessionState.EraseString(SessionStateKey);
        }
    }

    private static void SaveSessionState()
    {
        HistoryStore store = new HistoryStore();
        foreach (KeyValuePair<int, HistoryState> pair in HistoryByBrowserId)
        {
            store.states.Add(pair.Value);
        }

        SessionState.SetString(SessionStateKey, JsonUtility.ToJson(store));
    }

    private static void WarnReflectionUnavailableOnce()
    {
        if (warnedReflectionUnavailable)
        {
            return;
        }

        warnedReflectionUnavailable = true;
        Debug.LogWarning(
            "[ProjectFolderHistory] \u5f53\u524d Unity \u7248\u672c\u65e0\u6cd5\u8bbf\u95ee ProjectBrowser "
            + "\u7684\u6587\u4ef6\u5939\u5bfc\u822a\u63a5\u53e3\u3002Project \u7a97\u53e3\u4fdd\u6301\u539f\u751f\u884c\u4e3a\uff0c"
            + "\u8bf7\u68c0\u67e5 Unity \u7248\u672c\u6216\u66f4\u65b0\u8be5\u5de5\u5177\u7684\u53cd\u5c04\u9002\u914d\u3002");
    }

    private static void WarnNavigationFailureOnce(Exception exception)
    {
        if (warnedNavigationFailure)
        {
            return;
        }

        warnedNavigationFailure = true;
        Exception rootException =
            exception is TargetInvocationException && exception.InnerException != null
                ? exception.InnerException
                : exception;
        Debug.LogWarning(
            "[ProjectFolderHistory] Project \u6587\u4ef6\u5939\u5386\u53f2\u5bfc\u822a\u672a\u80fd\u5b8c\u6210\u3002"
            + "\u539f\u751f Project \u7a97\u53e3\u4ecd\u53ef\u6b63\u5e38\u4f7f\u7528\u3002\n"
            + rootException.GetType().Name
            + ": "
            + rootException.Message);
    }

    [Serializable]
    private sealed class HistoryStore
    {
        public List<HistoryState> states = new List<HistoryState>();
    }

    [Serializable]
    private sealed class HistoryState
    {
        public int browserInstanceId;
        public List<string> entries = new List<string>();
        public int index;
        public string lastObservedPath;

        [NonSerialized] public string pendingTargetPath;
        [NonSerialized] public double pendingDeadline;
        [NonSerialized] public int pendingPreviousIndex = -1;
    }
}
