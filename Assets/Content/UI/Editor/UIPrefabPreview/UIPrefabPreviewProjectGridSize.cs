using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class UIPrefabPreviewProjectGridSize
{
    private const string MenuRoot = "Tools/UI/UI Prefab Preview/Project Grid Max/";
    private const string MaxGridSizePrefsKey = "Dragon.UIPrefabPreview.ProjectGridMax.v1";
    private const int UnityDefaultMaxGridSize = 96;
    private const int DefaultMaxGridSize = 192;
    private const int NearPreviousMaximumThreshold = 8;
    private const int MinListGridSize = 16;
    private const int MinIconGridSize = 32;
    private const int WheelZoomLevelCount = 8;
    private const int WheelZoomRounding = 8;
    private const double PatchIntervalSeconds = 0.5d;

    private const BindingFlags InstanceMemberFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags StaticMemberFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    private static readonly Type ProjectBrowserType = typeof(EditorWindow).Assembly.GetType("UnityEditor.ProjectBrowser");
    private static readonly Type ObjectListAreaType = typeof(EditorWindow).Assembly.GetType("UnityEditor.ObjectListArea");
    private static readonly FieldInfo ListAreaField = ProjectBrowserType == null
        ? null
        : ProjectBrowserType.GetField("m_ListArea", InstanceMemberFlags);
    private static readonly FieldInfo MaxGridSizeField = ObjectListAreaType == null
        ? null
        : ObjectListAreaType.GetField("m_MaxGridSize", InstanceMemberFlags);
    private static readonly FieldInfo TotalRectField = ObjectListAreaType == null
        ? null
        : ObjectListAreaType.GetField("m_TotalRect", InstanceMemberFlags);
    private static readonly PropertyInfo GridSizeProperty = ObjectListAreaType == null
        ? null
        : ObjectListAreaType.GetProperty("gridSize", InstanceMemberFlags);
    private static readonly FieldInfo StartGridSizeField = ProjectBrowserType == null
        ? null
        : ProjectBrowserType.GetField("m_StartGridSize", InstanceMemberFlags);
    private static readonly FieldInfo LastFoldersGridSizeField = ProjectBrowserType == null
        ? null
        : ProjectBrowserType.GetField("m_LastFoldersGridSize", InstanceMemberFlags);
    private static readonly FieldInfo GlobalEventHandlerField = typeof(EditorApplication).GetField(
        "globalEventHandler",
        StaticMemberFlags);

    private static double nextPatchTime;
    private static bool warnedUnavailable;
    private static bool warnedWheelHookUnavailable;
    private static bool wheelHookInstalled;
    private static Delegate wheelEventCallback;
    private static int lastProjectBrowserCount;
    private static int lastObservedGridSize = -1;
    private static int lastObservedMaximum = -1;

    static UIPrefabPreviewProjectGridSize()
    {
        TryInstallWheelZoomHook();
        EditorApplication.update += OnEditorUpdate;
        EditorApplication.delayCall += ApplyConfiguredMaximumToAllProjectBrowsers;
    }

    [MenuItem(MenuRoot + "96 (Unity Default)", false, 140)]
    private static void SetUnityDefaultMaximum()
    {
        SetConfiguredMaximum(UnityDefaultMaxGridSize);
    }

    [MenuItem(MenuRoot + "96 (Unity Default)", true)]
    private static bool ValidateUnityDefaultMaximum()
    {
        Menu.SetChecked(MenuRoot + "96 (Unity Default)", ConfiguredMaxGridSize == UnityDefaultMaxGridSize);
        return true;
    }

    [MenuItem(MenuRoot + "160", false, 141)]
    private static void SetMaximum160()
    {
        SetConfiguredMaximum(160);
    }

    [MenuItem(MenuRoot + "160", true)]
    private static bool ValidateMaximum160()
    {
        Menu.SetChecked(MenuRoot + "160", ConfiguredMaxGridSize == 160);
        return true;
    }

    [MenuItem(MenuRoot + "192 (Recommended)", false, 142)]
    private static void SetMaximum192()
    {
        SetConfiguredMaximum(192);
    }

    [MenuItem(MenuRoot + "192 (Recommended)", true)]
    private static bool ValidateMaximum192()
    {
        Menu.SetChecked(MenuRoot + "192 (Recommended)", ConfiguredMaxGridSize == 192);
        return true;
    }

    [MenuItem(MenuRoot + "256", false, 143)]
    private static void SetMaximum256()
    {
        SetConfiguredMaximum(256);
    }

    [MenuItem(MenuRoot + "256", true)]
    private static bool ValidateMaximum256()
    {
        Menu.SetChecked(MenuRoot + "256", ConfiguredMaxGridSize == 256);
        return true;
    }

    private static int ConfiguredMaxGridSize
    {
        get
        {
            var value = EditorPrefs.GetInt(MaxGridSizePrefsKey, DefaultMaxGridSize);
            return value == UnityDefaultMaxGridSize || value == 160 || value == 192 || value == 256
                ? value
                : DefaultMaxGridSize;
        }
    }

    private static bool ReflectionIsAvailable
    {
        get
        {
            return ProjectBrowserType != null
                && ObjectListAreaType != null
                && ListAreaField != null
                && MaxGridSizeField != null
                && GridSizeProperty != null
                && GridSizeProperty.CanRead
                && GridSizeProperty.CanWrite;
        }
    }

    private static bool WheelReflectionIsAvailable
    {
        get
        {
            return ReflectionIsAvailable
                && TotalRectField != null
                && GlobalEventHandlerField != null
                && typeof(Delegate).IsAssignableFrom(GlobalEventHandlerField.FieldType);
        }
    }

    private static void SetConfiguredMaximum(int maxGridSize)
    {
        EditorPrefs.SetInt(MaxGridSizePrefsKey, maxGridSize);
        ApplyConfiguredMaximumToAllProjectBrowsers();

        if (ReflectionIsAvailable)
        {
            var currentGrid = lastObservedGridSize < 0 ? "unavailable" : lastObservedGridSize.ToString();
            var currentMaximum = lastObservedMaximum < 0 ? "unavailable" : lastObservedMaximum.ToString();
            Debug.Log(
                "[UI Prefab Preview] Project grid maximum configured as "
                + maxGridSize
                + " for "
                + lastProjectBrowserCount
                + " native Project window(s). Current grid: "
                + currentGrid
                + ", active maximum: "
                + currentMaximum
                + ". Wheel hook: "
                + (wheelHookInstalled ? "active" : "native fallback")
                + ". Ctrl/Cmd + wheel levels: "
                + FormatWheelZoomLevels(maxGridSize)
                + ".");
        }
    }

    private static void TryInstallWheelZoomHook()
    {
        if (!WheelReflectionIsAvailable)
        {
            WarnWheelHookUnavailableOnce(null);
            return;
        }

        try
        {
            var callbackMethod = typeof(UIPrefabPreviewProjectGridSize).GetMethod(
                nameof(OnGlobalEditorEvent),
                StaticMemberFlags);
            if (callbackMethod == null)
            {
                WarnWheelHookUnavailableOnce(null);
                return;
            }

            wheelEventCallback = Delegate.CreateDelegate(GlobalEventHandlerField.FieldType, callbackMethod);
            var existingCallbacks = GlobalEventHandlerField.GetValue(null) as Delegate;
            GlobalEventHandlerField.SetValue(null, Delegate.Combine(existingCallbacks, wheelEventCallback));
            wheelHookInstalled = true;
        }
        catch (Exception exception)
        {
            WarnWheelHookUnavailableOnce(exception);
        }
    }

    private static void OnGlobalEditorEvent()
    {
        if (!wheelHookInstalled)
        {
            return;
        }

        var currentEvent = Event.current;
        if (currentEvent == null
            || currentEvent.type != EventType.ScrollWheel
            || !EditorGUI.actionKey
            || Mathf.Approximately(currentEvent.delta.y, 0f))
        {
            return;
        }

        var projectBrowser = EditorWindow.mouseOverWindow;
        if (projectBrowser == null || !ProjectBrowserType.IsInstanceOfType(projectBrowser))
        {
            return;
        }

        try
        {
            var listArea = ListAreaField.GetValue(projectBrowser);
            if (listArea == null)
            {
                return;
            }

            var listAreaRect = (Rect)TotalRectField.GetValue(listArea);
            if (!listAreaRect.Contains(currentEvent.mousePosition))
            {
                return;
            }

            var currentGridSize = (int)GridSizeProperty.GetValue(listArea, null);
            var activeMaximum = (int)MaxGridSizeField.GetValue(listArea);
            var nextGridSize = FindNextWheelGridSize(
                currentGridSize,
                activeMaximum,
                currentEvent.delta.y < 0f);

            if (nextGridSize != currentGridSize)
            {
                GridSizeProperty.SetValue(listArea, nextGridSize, null);
                SetBrowserGridPersistence(projectBrowser, nextGridSize);
                RecordObservedValues(listArea);
                projectBrowser.Repaint();
            }

            currentEvent.Use();
            GUI.changed = true;
        }
        catch (Exception exception)
        {
            wheelHookInstalled = false;
            WarnWheelHookUnavailableOnce(exception);
        }
    }

    private static int FindNextWheelGridSize(int currentGridSize, int maxGridSize, bool zoomIn)
    {
        var levels = BuildWheelZoomLevels(maxGridSize);
        if (zoomIn)
        {
            foreach (var level in levels)
            {
                if (level > currentGridSize)
                {
                    return level;
                }
            }

            return levels[levels.Length - 1];
        }

        for (var index = levels.Length - 1; index >= 0; index--)
        {
            if (levels[index] < currentGridSize)
            {
                return levels[index];
            }
        }

        return levels[0];
    }

    private static int[] BuildWheelZoomLevels(int maxGridSize)
    {
        var maximum = Mathf.Max(MinIconGridSize, maxGridSize);
        var levels = new int[WheelZoomLevelCount];
        levels[0] = MinListGridSize;
        levels[1] = MinIconGridSize;

        var iconRange = maximum - MinIconGridSize;
        var iconStepCount = WheelZoomLevelCount - 2;
        for (var index = 2; index < WheelZoomLevelCount; index++)
        {
            var stepIndex = index - 1;
            var unrounded = MinIconGridSize + iconRange * stepIndex / (float)iconStepCount;
            var rounded = Mathf.RoundToInt(unrounded / WheelZoomRounding) * WheelZoomRounding;
            levels[index] = Mathf.Clamp(rounded, levels[index - 1] + 1, maximum);
        }

        levels[levels.Length - 1] = maximum;
        return levels;
    }

    private static string FormatWheelZoomLevels(int maxGridSize)
    {
        return string.Join(" -> ", Array.ConvertAll(BuildWheelZoomLevels(maxGridSize), value => value.ToString()));
    }

    private static void OnEditorUpdate()
    {
        if (EditorApplication.timeSinceStartup < nextPatchTime)
        {
            return;
        }

        nextPatchTime = EditorApplication.timeSinceStartup + PatchIntervalSeconds;
        ApplyConfiguredMaximumToAllProjectBrowsers();
    }

    private static void ApplyConfiguredMaximumToAllProjectBrowsers()
    {
        if (!ReflectionIsAvailable)
        {
            WarnUnavailableOnce(null);
            return;
        }

        try
        {
            var projectBrowsers = Resources.FindObjectsOfTypeAll(ProjectBrowserType);
            var configuredMaximum = ConfiguredMaxGridSize;
            var changed = false;
            lastProjectBrowserCount = projectBrowsers.Length;
            lastObservedGridSize = -1;
            lastObservedMaximum = -1;

            foreach (var projectBrowser in projectBrowsers)
            {
                changed |= ApplyMaximumToProjectBrowser(projectBrowser, configuredMaximum);
            }

            if (changed)
            {
                EditorApplication.RepaintProjectWindow();
            }
        }
        catch (Exception exception)
        {
            WarnUnavailableOnce(exception);
        }
    }

    private static bool ApplyMaximumToProjectBrowser(UnityEngine.Object projectBrowser, int configuredMaximum)
    {
        if (projectBrowser == null)
        {
            return false;
        }

        var listArea = ListAreaField.GetValue(projectBrowser);
        if (listArea == null)
        {
            return false;
        }

        var previousMaximum = (int)MaxGridSizeField.GetValue(listArea);
        var currentGridSize = (int)GridSizeProperty.GetValue(listArea, null);
        var changed = false;

        if (previousMaximum != configuredMaximum)
        {
            MaxGridSizeField.SetValue(listArea, configuredMaximum);
            changed = true;
        }

        var nextGridSize = currentGridSize;
        if (configuredMaximum > previousMaximum
            && currentGridSize >= previousMaximum - NearPreviousMaximumThreshold)
        {
            nextGridSize = configuredMaximum;
        }
        else if (currentGridSize > configuredMaximum)
        {
            nextGridSize = configuredMaximum;
        }

        if (nextGridSize == currentGridSize)
        {
            RecordObservedValues(listArea);
            return changed;
        }

        GridSizeProperty.SetValue(listArea, nextGridSize, null);
        SetBrowserGridPersistence(projectBrowser, nextGridSize);
        RecordObservedValues(listArea);
        return true;
    }

    private static void RecordObservedValues(object listArea)
    {
        lastObservedGridSize = (int)GridSizeProperty.GetValue(listArea, null);
        lastObservedMaximum = (int)MaxGridSizeField.GetValue(listArea);
    }

    private static void SetBrowserGridPersistence(UnityEngine.Object projectBrowser, int gridSize)
    {
        if (StartGridSizeField != null)
        {
            StartGridSizeField.SetValue(projectBrowser, gridSize);
        }

        if (LastFoldersGridSizeField != null)
        {
            LastFoldersGridSizeField.SetValue(projectBrowser, (float)gridSize);
        }
    }

    private static void WarnUnavailableOnce(Exception exception)
    {
        if (warnedUnavailable)
        {
            return;
        }

        warnedUnavailable = true;
        var details = exception == null ? string.Empty : " " + exception.GetType().Name + ": " + exception.Message;
        Debug.LogWarning(
            "[UI Prefab Preview] Could not extend the native Project grid limit in this Unity version. "
            + "Unity's default 96 limit remains active."
            + details);
    }

    private static void WarnWheelHookUnavailableOnce(Exception exception)
    {
        if (warnedWheelHookUnavailable)
        {
            return;
        }

        warnedWheelHookUnavailable = true;
        var details = exception == null ? string.Empty : " " + exception.GetType().Name + ": " + exception.Message;
        Debug.LogWarning(
            "[UI Prefab Preview] Could not accelerate Ctrl/Cmd + wheel zoom in this Unity version. "
            + "The native seven-pixel wheel step remains active; the bottom grid-size slider is unaffected."
            + details);
    }
}
