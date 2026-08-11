using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

[InitializeOnLoad]
internal static class PrefabStageHierarchySwitcher
{
    private const string MenuPath = "UITools/Prefab编辑时按视窗切换Hierarchy";
    private const string EnabledEditorPrefKey =
        "SgrProject.UI.PrefabStageHierarchySwitcher.Enabled";
    private const double HierarchyWindowPollInterval = 0.5d;

    private static readonly Type SceneHierarchyWindowType =
        typeof(EditorWindow).Assembly.GetType("UnityEditor.SceneHierarchyWindow");
    private static readonly Type SceneHierarchyType =
        typeof(EditorWindow).Assembly.GetType("UnityEditor.SceneHierarchy");
    private static readonly Type GameViewType =
        typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
    private static readonly FieldInfo SceneHierarchyField =
        SceneHierarchyWindowType != null
            ? SceneHierarchyWindowType.GetField(
                "m_SceneHierarchy",
                BindingFlags.Instance | BindingFlags.NonPublic)
            : null;
    private static readonly PropertyInfo CustomScenesProperty =
        SceneHierarchyType != null
            ? SceneHierarchyType.GetProperty(
                "customScenes",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            : null;
    private static readonly PropertyInfo HierarchyLockedProperty =
        SceneHierarchyType != null
            ? SceneHierarchyType.GetProperty(
                "isLocked",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            : null;

    private static readonly Dictionary<int, HierarchyWindowState> OverriddenWindows =
        new Dictionary<int, HierarchyWindowState>();

    private static EditorWindow lastFocusedWindow;
    private static HierarchyContentMode currentMode;
    private static double nextHierarchyWindowPollTime;
    private static bool runtimeSceneRefreshQueued;
    private static bool prefabStageOpenRestoreQueued;
    private static bool reflectionWarningShown;

    static PrefabStageHierarchySwitcher()
    {
        if (!EditorPrefs.HasKey(EnabledEditorPrefKey))
        {
            EditorPrefs.SetBool(EnabledEditorPrefKey, true);
        }

        EditorApplication.update -= Update;
        EditorApplication.update += Update;
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        EditorApplication.quitting -= RestoreBeforeEditorExit;
        EditorApplication.quitting += RestoreBeforeEditorExit;
        AssemblyReloadEvents.beforeAssemblyReload -= RestoreBeforeEditorExit;
        AssemblyReloadEvents.beforeAssemblyReload += RestoreBeforeEditorExit;
        PrefabStage.prefabStageOpened -= OnPrefabStageOpened;
        PrefabStage.prefabStageOpened += OnPrefabStageOpened;
        PrefabStage.prefabStageClosing -= OnPrefabStageClosing;
        PrefabStage.prefabStageClosing += OnPrefabStageClosing;
        SceneManager.sceneLoaded -= OnRuntimeSceneLoaded;
        SceneManager.sceneLoaded += OnRuntimeSceneLoaded;
        SceneManager.sceneUnloaded -= OnRuntimeSceneUnloaded;
        SceneManager.sceneUnloaded += OnRuntimeSceneUnloaded;

        EditorApplication.delayCall += ResetFocusTracking;
    }

    [MenuItem(MenuPath)]
    private static void ToggleEnabled()
    {
        bool enabled = !IsEnabled();
        EditorPrefs.SetBool(EnabledEditorPrefKey, enabled);
        Menu.SetChecked(MenuPath, enabled);

        if (enabled)
        {
            ResetFocusTracking();
        }
        else
        {
            ResetToUnityDefault();
        }
    }

    [MenuItem(MenuPath, true)]
    private static bool ValidateToggleEnabled()
    {
        Menu.SetChecked(MenuPath, IsEnabled());
        return true;
    }

    private static bool IsEnabled()
    {
        return EditorPrefs.GetBool(EnabledEditorPrefKey, true);
    }

    private static void Update()
    {
        PrefabStage prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
        if (!IsEnabled() ||
            !EditorApplication.isPlaying ||
            prefabStage == null)
        {
            if (currentMode != HierarchyContentMode.None || OverriddenWindows.Count > 0)
            {
                ResetToUnityDefault();
            }

            return;
        }

        EditorWindow focusedWindow = EditorWindow.focusedWindow;
        if (focusedWindow != null && focusedWindow != lastFocusedWindow)
        {
            lastFocusedWindow = focusedWindow;

            if (focusedWindow is SceneView)
            {
                ShowPrefabStageHierarchy();
            }
            else if (IsGameView(focusedWindow))
            {
                ShowRuntimeHierarchy(prefabStage, true);
            }
        }

        if (currentMode == HierarchyContentMode.Runtime &&
            EditorApplication.timeSinceStartup >= nextHierarchyWindowPollTime)
        {
            nextHierarchyWindowPollTime =
                EditorApplication.timeSinceStartup + HierarchyWindowPollInterval;
            ShowRuntimeHierarchy(prefabStage, false);
        }
    }

    private static bool IsGameView(EditorWindow window)
    {
        return window != null &&
               ((GameViewType != null && GameViewType.IsInstanceOfType(window)) ||
                window.GetType().FullName == "UnityEditor.GameView");
    }

    private static void ShowPrefabStageHierarchy()
    {
        currentMode = HierarchyContentMode.PrefabStage;
        RestoreOverriddenHierarchyWindows();
    }

    private static void ShowRuntimeHierarchy(PrefabStage prefabStage, bool refreshExisting)
    {
        currentMode = HierarchyContentMode.Runtime;

        if (!HasRequiredReflection())
        {
            WarnReflectionUnavailable(null);
            return;
        }

        Object[] hierarchyWindows = Resources.FindObjectsOfTypeAll(SceneHierarchyWindowType);
        bool needsRuntimeScenes = refreshExisting;
        for (int i = 0; i < hierarchyWindows.Length && !needsRuntimeScenes; i++)
        {
            Object hierarchyWindow = hierarchyWindows[i];
            if (hierarchyWindow != null &&
                !OverriddenWindows.ContainsKey(hierarchyWindow.GetInstanceID()) &&
                !IsHierarchyLocked(hierarchyWindow))
            {
                needsRuntimeScenes = true;
            }
        }

        if (!needsRuntimeScenes)
        {
            return;
        }

        Scene[] runtimeScenes = CollectRuntimeScenes(prefabStage);
        if (runtimeScenes.Length == 0)
        {
            return;
        }

        for (int i = 0; i < hierarchyWindows.Length; i++)
        {
            Object hierarchyWindow = hierarchyWindows[i];
            if (hierarchyWindow == null)
            {
                continue;
            }

            int windowId = hierarchyWindow.GetInstanceID();
            HierarchyWindowState state;
            if (OverriddenWindows.TryGetValue(windowId, out state))
            {
                if (refreshExisting)
                {
                    SetCustomScenes(state.Window, runtimeScenes);
                }

                continue;
            }

            if (IsHierarchyLocked(hierarchyWindow))
            {
                continue;
            }

            try
            {
                object sceneHierarchy = SceneHierarchyField.GetValue(hierarchyWindow);
                if (sceneHierarchy == null)
                {
                    continue;
                }

                Scene[] originalScenes =
                    CustomScenesProperty.GetValue(sceneHierarchy, null) as Scene[];
                SetCustomScenesOnSceneHierarchy(sceneHierarchy, runtimeScenes);
                EditorWindow hierarchyEditorWindow = hierarchyWindow as EditorWindow;
                if (hierarchyEditorWindow != null)
                {
                    hierarchyEditorWindow.Repaint();
                }

                OverriddenWindows.Add(
                    windowId,
                    new HierarchyWindowState(
                        hierarchyWindow,
                        originalScenes == null ? null : (Scene[])originalScenes.Clone()));
            }
            catch (Exception exception)
            {
                WarnReflectionUnavailable(exception);
            }
        }

        EditorApplication.RepaintHierarchyWindow();
    }

    private static Scene[] CollectRuntimeScenes(PrefabStage prefabStage)
    {
        List<Scene> scenes = new List<Scene>();
        HashSet<int> sceneHandles = new HashSet<int>();
        int prefabSceneHandle = prefabStage.scene.handle;

        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            AddRuntimeScene(
                SceneManager.GetSceneAt(i),
                prefabSceneHandle,
                scenes,
                sceneHandles);
        }

        GameObject[] allGameObjects = Resources.FindObjectsOfTypeAll<GameObject>();
        for (int i = 0; i < allGameObjects.Length; i++)
        {
            GameObject gameObject = allGameObjects[i];
            if (gameObject == null || EditorUtility.IsPersistent(gameObject))
            {
                continue;
            }

            AddRuntimeScene(
                gameObject.scene,
                prefabSceneHandle,
                scenes,
                sceneHandles);
        }

        return scenes.ToArray();
    }

    private static void AddRuntimeScene(
        Scene scene,
        int prefabSceneHandle,
        List<Scene> scenes,
        HashSet<int> sceneHandles)
    {
        if (!scene.IsValid() ||
            !scene.isLoaded ||
            scene.handle == prefabSceneHandle ||
            EditorSceneManager.IsPreviewScene(scene) ||
            !sceneHandles.Add(scene.handle))
        {
            return;
        }

        scenes.Add(scene);
    }

    private static bool IsHierarchyLocked(Object hierarchyWindow)
    {
        try
        {
            object sceneHierarchy = SceneHierarchyField.GetValue(hierarchyWindow);
            return sceneHierarchy == null ||
                   (bool)HierarchyLockedProperty.GetValue(sceneHierarchy, null);
        }
        catch (Exception exception)
        {
            WarnReflectionUnavailable(exception);
            return true;
        }
    }

    private static void SetCustomScenes(Object hierarchyWindow, Scene[] scenes)
    {
        if (hierarchyWindow == null)
        {
            return;
        }

        try
        {
            object sceneHierarchy = SceneHierarchyField.GetValue(hierarchyWindow);
            if (sceneHierarchy != null)
            {
                SetCustomScenesOnSceneHierarchy(sceneHierarchy, scenes);
                EditorWindow hierarchyEditorWindow = hierarchyWindow as EditorWindow;
                if (hierarchyEditorWindow != null)
                {
                    hierarchyEditorWindow.Repaint();
                }
            }
        }
        catch (Exception exception)
        {
            WarnReflectionUnavailable(exception);
        }
    }

    private static void SetCustomScenesOnSceneHierarchy(object sceneHierarchy, Scene[] scenes)
    {
        CustomScenesProperty.SetValue(sceneHierarchy, scenes, null);
    }

    private static void RestoreOverriddenHierarchyWindows()
    {
        if (OverriddenWindows.Count == 0)
        {
            return;
        }

        foreach (HierarchyWindowState state in OverriddenWindows.Values)
        {
            SetCustomScenes(state.Window, state.OriginalScenes);
        }

        OverriddenWindows.Clear();
        EditorApplication.RepaintHierarchyWindow();
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingPlayMode ||
            state == PlayModeStateChange.EnteredEditMode)
        {
            ResetToUnityDefault();
        }
        else if (state == PlayModeStateChange.EnteredPlayMode)
        {
            ResetFocusTracking();
        }
    }

    private static void OnPrefabStageOpened(PrefabStage prefabStage)
    {
        RestorePrefabHierarchyAfterStageOpen();

        if (!prefabStageOpenRestoreQueued)
        {
            prefabStageOpenRestoreQueued = true;
            EditorApplication.delayCall += RestorePrefabHierarchyAfterStageOpen;
        }
    }

    private static void OnPrefabStageClosing(PrefabStage prefabStage)
    {
        prefabStageOpenRestoreQueued = false;
        EditorApplication.delayCall -= RestorePrefabHierarchyAfterStageOpen;
        ResetToUnityDefault();
    }

    private static void RestorePrefabHierarchyAfterStageOpen()
    {
        prefabStageOpenRestoreQueued = false;

        if (!IsEnabled() ||
            !EditorApplication.isPlaying ||
            PrefabStageUtility.GetCurrentPrefabStage() == null)
        {
            return;
        }

        runtimeSceneRefreshQueued = false;
        ShowPrefabStageHierarchy();

        // Treat the window that opened the Prefab as the new focus baseline.
        // The user must explicitly focus Game again before runtime scenes return.
        lastFocusedWindow = EditorWindow.focusedWindow;
        nextHierarchyWindowPollTime = 0d;
    }

    private static void OnRuntimeSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        QueueRuntimeSceneRefresh();
    }

    private static void OnRuntimeSceneUnloaded(Scene scene)
    {
        QueueRuntimeSceneRefresh();
    }

    private static void QueueRuntimeSceneRefresh()
    {
        if (currentMode != HierarchyContentMode.Runtime || runtimeSceneRefreshQueued)
        {
            return;
        }

        runtimeSceneRefreshQueued = true;
        EditorApplication.delayCall += RefreshRuntimeScenes;
    }

    private static void RefreshRuntimeScenes()
    {
        runtimeSceneRefreshQueued = false;
        PrefabStage prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
        if (currentMode == HierarchyContentMode.Runtime &&
            EditorApplication.isPlaying &&
            prefabStage != null)
        {
            ShowRuntimeHierarchy(prefabStage, true);
        }
    }

    private static void ResetFocusTracking()
    {
        lastFocusedWindow = null;
        currentMode = HierarchyContentMode.None;
        nextHierarchyWindowPollTime = 0d;
    }

    private static void ResetToUnityDefault()
    {
        runtimeSceneRefreshQueued = false;
        prefabStageOpenRestoreQueued = false;
        RestoreOverriddenHierarchyWindows();
        ResetFocusTracking();
    }

    private static void RestoreBeforeEditorExit()
    {
        RestoreOverriddenHierarchyWindows();
    }

    private static bool HasRequiredReflection()
    {
        return SceneHierarchyWindowType != null &&
               SceneHierarchyType != null &&
               SceneHierarchyField != null &&
               CustomScenesProperty != null &&
               CustomScenesProperty.CanRead &&
               CustomScenesProperty.CanWrite &&
               HierarchyLockedProperty != null &&
               HierarchyLockedProperty.CanRead;
    }

    private static void WarnReflectionUnavailable(Exception exception)
    {
        if (reflectionWarningShown)
        {
            return;
        }

        reflectionWarningShown = true;
        string detail = exception == null ? string.Empty : "\n" + exception.Message;
        Debug.LogWarning(
            "[Prefab Stage Hierarchy Switcher] 当前 Unity 版本的 Hierarchy 内部接口不可用，" +
            "已保留 Unity 默认显示。可在菜单“" + MenuPath + "”中关闭此功能。" + detail);
    }

    private enum HierarchyContentMode
    {
        None,
        PrefabStage,
        Runtime
    }

    private sealed class HierarchyWindowState
    {
        internal HierarchyWindowState(Object window, Scene[] originalScenes)
        {
            Window = window;
            OriginalScenes = originalScenes;
        }

        internal Object Window { get; private set; }
        internal Scene[] OriginalScenes { get; private set; }
    }
}
