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

    private static double nextPatchTime;
    private static bool warnedUnavailable;
    private static int lastProjectBrowserCount;
    private static int lastObservedGridSize = -1;
    private static int lastObservedMaximum = -1;

    static UIPrefabPreviewProjectGridSize()
    {
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
                + ".");
        }
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
}
