using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
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
    private const int VirtualKeyControl = 0x11;
    private const int VirtualKeyLeftMouseButton = 0x01;
    private const int AsyncKeyPressedMask = 0x8000;
    private const double PatchIntervalSeconds = 0.5d;

    private const BindingFlags InstanceMemberFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static readonly Type ProjectBrowserType = typeof(EditorWindow).Assembly.GetType("UnityEditor.ProjectBrowser");
    private static readonly Type ObjectListAreaType = typeof(EditorWindow).Assembly.GetType("UnityEditor.ObjectListArea");
    private static readonly FieldInfo ListAreaField = ProjectBrowserType == null
        ? null
        : ProjectBrowserType.GetField("m_ListArea", InstanceMemberFlags);
    private static readonly FieldInfo MaxGridSizeField = ObjectListAreaType == null
        ? null
        : ObjectListAreaType.GetField("m_MaxGridSize", InstanceMemberFlags);
    private static readonly PropertyInfo GridSizeProperty = ObjectListAreaType == null
        ? null
        : ObjectListAreaType.GetProperty("gridSize", InstanceMemberFlags);
    private static readonly FieldInfo StartGridSizeField = ProjectBrowserType == null
        ? null
        : ProjectBrowserType.GetField("m_StartGridSize", InstanceMemberFlags);
    private static readonly FieldInfo LastFoldersGridSizeField = ProjectBrowserType == null
        ? null
        : ProjectBrowserType.GetField("m_LastFoldersGridSize", InstanceMemberFlags);

    private static readonly Dictionary<int, int> ObservedGridSizes = new Dictionary<int, int>();

    private static double nextPatchTime;
    private static bool warnedUnavailable;
    private static bool warnedWheelSnapUnavailable;
    private static bool loggedWheelSnapSuccess;
    private static int wheelSnapCount;
    private static int lastProjectBrowserCount;
    private static int lastObservedGridSize = -1;
    private static int lastObservedMaximum = -1;

    static UIPrefabPreviewProjectGridSize()
    {
        EditorApplication.update += OnEditorUpdate;
        EditorApplication.delayCall += ApplyConfiguredMaximumToAllProjectBrowsers;

        if (!WheelReflectionIsAvailable)
        {
            WarnWheelSnapUnavailableOnce(null);
        }
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
            return ReflectionIsAvailable;
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
                + ". Wheel snaps completed since reload: "
                + wheelSnapCount
                + ". Ctrl/Cmd + wheel levels: "
                + FormatWheelZoomLevels(maxGridSize)
                + ".");
        }
    }

    private static void PollProjectGridChanges()
    {
        if (!WheelReflectionIsAvailable)
        {
            return;
        }

        var controlHeld = false;
        var leftMouseHeld = false;
        if (!TryGetWindowsInputState(out controlHeld, out leftMouseHeld))
        {
            controlHeld = false;
        }

        try
        {
            var projectBrowsers = Resources.FindObjectsOfTypeAll(ProjectBrowserType);
            foreach (var projectBrowserObject in projectBrowsers)
            {
                var projectBrowser = projectBrowserObject as EditorWindow;
                var listArea = projectBrowser == null ? null : ListAreaField.GetValue(projectBrowser);
                if (listArea == null)
                {
                    continue;
                }

                var currentGridSize = (int)GridSizeProperty.GetValue(listArea, null);
                var instanceId = projectBrowser.GetInstanceID();
                int previousGridSize;
                if (!ObservedGridSizes.TryGetValue(instanceId, out previousGridSize))
                {
                    ObservedGridSizes[instanceId] = currentGridSize;
                    continue;
                }

                if (currentGridSize == previousGridSize)
                {
                    continue;
                }

                var finalGridSize = currentGridSize;
                if (controlHeld && !leftMouseHeld)
                {
                    var activeMaximum = (int)MaxGridSizeField.GetValue(listArea);
                    var nextGridSize = FindPostNativeWheelGridSize(
                        currentGridSize,
                        activeMaximum,
                        currentGridSize > previousGridSize);

                    if (nextGridSize != currentGridSize)
                    {
                        GridSizeProperty.SetValue(listArea, nextGridSize, null);
                        SetBrowserGridPersistence(projectBrowser, nextGridSize);
                        RecordObservedValues(listArea);
                        wheelSnapCount++;
                        finalGridSize = nextGridSize;
                        projectBrowser.Repaint();

                        if (!loggedWheelSnapSuccess)
                        {
                            loggedWheelSnapSuccess = true;
                            Debug.Log(
                                "[UI Prefab Preview] Eight-level Project wheel snap confirmed: "
                                + currentGridSize
                                + " -> "
                                + nextGridSize
                                + ".");
                        }
                    }
                }

                ObservedGridSizes[instanceId] = finalGridSize;
            }
        }
        catch (Exception exception)
        {
            WarnWheelSnapUnavailableOnce(exception);
        }
    }

    private static bool TryGetWindowsInputState(out bool controlHeld, out bool leftMouseHeld)
    {
        controlHeld = false;
        leftMouseHeld = false;
        if (Application.platform != RuntimePlatform.WindowsEditor)
        {
            return false;
        }

        try
        {
            controlHeld = (GetAsyncKeyState(VirtualKeyControl) & AsyncKeyPressedMask) != 0;
            leftMouseHeld = (GetAsyncKeyState(VirtualKeyLeftMouseButton) & AsyncKeyPressedMask) != 0;
            return true;
        }
        catch (Exception exception)
        {
            WarnWheelSnapUnavailableOnce(exception);
            return false;
        }
    }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    private static int FindPostNativeWheelGridSize(int currentGridSize, int maxGridSize, bool zoomIn)
    {
        var levels = BuildWheelZoomLevels(maxGridSize);
        if (zoomIn)
        {
            foreach (var level in levels)
            {
                if (level >= currentGridSize)
                {
                    return level;
                }
            }

            return levels[levels.Length - 1];
        }

        for (var index = levels.Length - 1; index >= 0; index--)
        {
            if (levels[index] <= currentGridSize)
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
        PollProjectGridChanges();

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
            RememberGridSize(projectBrowser, currentGridSize);
            RecordObservedValues(listArea);
            return changed;
        }

        GridSizeProperty.SetValue(listArea, nextGridSize, null);
        SetBrowserGridPersistence(projectBrowser, nextGridSize);
        RememberGridSize(projectBrowser, nextGridSize);
        RecordObservedValues(listArea);
        return true;
    }

    private static void RememberGridSize(UnityEngine.Object projectBrowser, int gridSize)
    {
        if (projectBrowser != null)
        {
            ObservedGridSizes[projectBrowser.GetInstanceID()] = gridSize;
        }
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

    private static void WarnWheelSnapUnavailableOnce(Exception exception)
    {
        if (warnedWheelSnapUnavailable)
        {
            return;
        }

        warnedWheelSnapUnavailable = true;
        var details = exception == null ? string.Empty : " " + exception.GetType().Name + ": " + exception.Message;
        Debug.LogWarning(
            "[UI Prefab Preview] Could not apply post-native Ctrl/Cmd + wheel snapping in this Unity version. "
            + "The native seven-pixel wheel step remains active; the bottom grid-size slider is unaffected."
            + details);
    }
}
