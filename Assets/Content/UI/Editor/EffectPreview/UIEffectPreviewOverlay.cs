using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Toolbars;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

[InitializeOnLoad]
public static class UIEffectPreviewController
{
    private const string ToggleMenuPath = "Tools/UI/Effect Preview/Play or Stop Particles";
    private const string StopMenuPath = "Tools/UI/Effect Preview/Stop and Clear Particles";
    private const float MaxSimulationStep = 0.05f;

    private static readonly List<ParticleSystem> ActiveParticleSystems = new List<ParticleSystem>();
    private static double lastUpdateTime;
    private static double lastWarningTime;

    public static event Action PreviewStateChanged;

    static UIEffectPreviewController()
    {
        AssemblyReloadEvents.beforeAssemblyReload += StopPreview;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        EditorApplication.update += OnEditorUpdate;
        EditorApplication.quitting += StopPreview;
        PrefabStage.prefabStageOpened += OnPrefabStageChanged;
        PrefabStage.prefabStageClosing += OnPrefabStageChanged;
    }

    public static bool IsPreviewing
    {
        get { return ActiveParticleSystems.Count > 0; }
    }

    public static bool HasPreviewTargets()
    {
        return CollectParticleSystems().Count > 0;
    }

    public static bool StartPreview()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Warn("粒子预览只在非运行模式可用。", "Exit Play Mode first");
            return false;
        }

        List<ParticleSystem> targets = CollectParticleSystems();
        if (targets.Count == 0)
        {
            Warn(
                PrefabStageUtility.GetCurrentPrefabStage() != null
                    ? "当前 Prefab 中没有可预览的 ParticleSystem。"
                    : "请先选择包含 ParticleSystem 的节点。",
                "No particles found");
            return false;
        }

        StopPreview(false);
        ActiveParticleSystems.AddRange(targets);

        for (int i = 0; i < ActiveParticleSystems.Count; i++)
        {
            ParticleSystem particleSystem = ActiveParticleSystems[i];
            if (particleSystem == null)
            {
                continue;
            }

            particleSystem.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            particleSystem.Clear(false);
            particleSystem.Simulate(0f, false, true, true);
        }

        lastUpdateTime = EditorApplication.timeSinceStartup;
        Debug.Log($"Effect preview started. ParticleSystem count: {ActiveParticleSystems.Count}. Click ■ 粒子 to stop and clear.");
        NotifySceneView($"正在预览 {ActiveParticleSystems.Count} 个粒子系统");
        RefreshViews();
        PreviewStateChanged?.Invoke();
        return true;
    }

    public static void StopPreview()
    {
        StopPreview(true);
    }

    [MenuItem(ToggleMenuPath, false, 2309)]
    public static void TogglePreview()
    {
        if (IsPreviewing)
        {
            StopPreview();
        }
        else
        {
            StartPreview();
        }
    }

    [MenuItem(ToggleMenuPath, true)]
    private static bool ValidateTogglePreview()
    {
        Menu.SetChecked(ToggleMenuPath, IsPreviewing);
        return !EditorApplication.isPlayingOrWillChangePlaymode;
    }

    [MenuItem(StopMenuPath, false, 2310)]
    private static void StopPreviewFromMenu()
    {
        StopPreview();
    }

    [MenuItem(StopMenuPath, true)]
    private static bool ValidateStopPreview()
    {
        return IsPreviewing;
    }

    private static void StopPreview(bool notify)
    {
        bool hadPreview = ActiveParticleSystems.Count > 0;

        for (int i = 0; i < ActiveParticleSystems.Count; i++)
        {
            ParticleSystem particleSystem = ActiveParticleSystems[i];
            if (particleSystem == null)
            {
                continue;
            }

            particleSystem.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            particleSystem.Clear(false);
        }

        ActiveParticleSystems.Clear();
        lastUpdateTime = 0d;

        if (!hadPreview)
        {
            return;
        }

        if (notify)
        {
            Debug.Log("Effect preview stopped and particle state was cleared.");
            NotifySceneView("粒子预览已停止");
        }

        RefreshViews();
        PreviewStateChanged?.Invoke();
    }

    private static void OnEditorUpdate()
    {
        if (!IsPreviewing || EditorApplication.isPlayingOrWillChangePlaymode)
        {
            return;
        }

        double now = EditorApplication.timeSinceStartup;
        float deltaTime = Mathf.Clamp((float)(now - lastUpdateTime), 0f, MaxSimulationStep);
        lastUpdateTime = now;
        if (deltaTime <= 0f)
        {
            return;
        }

        bool hasValidTarget = false;
        for (int i = ActiveParticleSystems.Count - 1; i >= 0; i--)
        {
            ParticleSystem particleSystem = ActiveParticleSystems[i];
            if (particleSystem == null || !IsPreviewableSceneObject(particleSystem))
            {
                ActiveParticleSystems.RemoveAt(i);
                continue;
            }

            if (!particleSystem.gameObject.activeInHierarchy)
            {
                continue;
            }

            particleSystem.Simulate(deltaTime, false, false, true);
            hasValidTarget = true;
        }

        if (ActiveParticleSystems.Count == 0)
        {
            lastUpdateTime = 0d;
            RefreshViews();
            PreviewStateChanged?.Invoke();
            return;
        }

        if (hasValidTarget)
        {
            RefreshViews();
        }
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        StopPreview(false);
    }

    private static void OnPrefabStageChanged(PrefabStage prefabStage)
    {
        StopPreview(false);
    }

    private static List<ParticleSystem> CollectParticleSystems()
    {
        List<ParticleSystem> result = new List<ParticleSystem>();
        HashSet<int> seen = new HashSet<int>();
        PrefabStage prefabStage = PrefabStageUtility.GetCurrentPrefabStage();

        if (prefabStage != null && prefabStage.prefabContentsRoot != null)
        {
            AddSelectedParticleSystems(result, seen, prefabStage.scene, prefabStage.prefabContentsRoot.transform);
            if (result.Count == 0)
            {
                AddParticleSystemsUnder(result, seen, prefabStage.prefabContentsRoot);
            }

            return result;
        }

        AddSelectedParticleSystems(result, seen, default(Scene), null);
        return result;
    }

    private static void AddSelectedParticleSystems(
        List<ParticleSystem> result,
        HashSet<int> seen,
        Scene requiredScene,
        Transform requiredRoot)
    {
        GameObject[] selectedObjects = Selection.gameObjects;
        for (int i = 0; i < selectedObjects.Length; i++)
        {
            GameObject selectedObject = selectedObjects[i];
            if (selectedObject == null || !IsSceneObject(selectedObject))
            {
                continue;
            }

            if (requiredScene.IsValid() && selectedObject.scene != requiredScene)
            {
                continue;
            }

            if (requiredRoot != null && !IsTransformUnder(selectedObject.transform, requiredRoot))
            {
                continue;
            }

            AddParticleSystemsUnder(result, seen, selectedObject);
        }
    }

    private static void AddParticleSystemsUnder(
        List<ParticleSystem> result,
        HashSet<int> seen,
        GameObject root)
    {
        if (root == null)
        {
            return;
        }

        ParticleSystem[] particleSystems = root.GetComponentsInChildren<ParticleSystem>(true);
        for (int i = 0; i < particleSystems.Length; i++)
        {
            ParticleSystem particleSystem = particleSystems[i];
            if (!IsPreviewableSceneObject(particleSystem) || !particleSystem.gameObject.activeInHierarchy)
            {
                continue;
            }

            int instanceId = particleSystem.GetInstanceID();
            if (seen.Add(instanceId))
            {
                result.Add(particleSystem);
            }
        }
    }

    private static bool IsPreviewableSceneObject(Component component)
    {
        return component != null
            && !EditorUtility.IsPersistent(component)
            && IsSceneObject(component.gameObject);
    }

    private static bool IsSceneObject(GameObject gameObject)
    {
        return gameObject != null
            && !EditorUtility.IsPersistent(gameObject)
            && gameObject.scene.IsValid()
            && gameObject.scene.isLoaded;
    }

    private static bool IsTransformUnder(Transform child, Transform root)
    {
        Transform current = child;
        while (current != null)
        {
            if (current == root)
            {
                return true;
            }

            current = current.parent;
        }

        return false;
    }

    private static void Warn(string logMessage, string notification)
    {
        double now = EditorApplication.timeSinceStartup;
        if (now - lastWarningTime >= 1d)
        {
            Debug.LogWarning(logMessage);
            lastWarningTime = now;
        }

        NotifySceneView(notification);
        PreviewStateChanged?.Invoke();
    }

    private static void NotifySceneView(string message)
    {
        SceneView sceneView = SceneView.lastActiveSceneView;
        if (sceneView != null)
        {
            sceneView.ShowNotification(new GUIContent(message));
        }
    }

    private static void RefreshViews()
    {
        Canvas.ForceUpdateCanvases();
        EditorApplication.QueuePlayerLoopUpdate();
        SceneView.RepaintAll();
    }
}

[EditorToolbarElement(id, typeof(SceneView))]
public class UIEffectPreviewButton : Button
{
    public const string id = "DragonUI/EffectPreviewButton";
    private bool ignoreNextClicked;

    public UIEffectPreviewButton()
    {
        tooltip = "非运行模式预览粒子。Prefab 模式下优先播放选中节点；选中节点没有粒子时播放整个 Prefab。再次点击停止并清空。";
        clicked += OnClicked;
        RegisterCallback<MouseDownEvent>(OnMouseDown, TrickleDown.TrickleDown);

        AddToClassList("unity-editor-toolbar-button");
        style.width = StyleKeyword.Auto;
        style.minWidth = 54;
        style.paddingLeft = 8;
        style.paddingRight = 8;
        style.unityFontStyleAndWeight = FontStyle.Bold;
        style.unityTextAlign = TextAnchor.MiddleCenter;

        RegisterCallback<AttachToPanelEvent>(OnAttach);
        RegisterCallback<DetachFromPanelEvent>(OnDetach);
    }

    private void OnAttach(AttachToPanelEvent evt)
    {
        Selection.selectionChanged += RefreshState;
        EditorApplication.hierarchyChanged += RefreshState;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        UIEffectPreviewController.PreviewStateChanged += RefreshState;
        RefreshState();
    }

    private void OnDetach(DetachFromPanelEvent evt)
    {
        ignoreNextClicked = false;
        Selection.selectionChanged -= RefreshState;
        EditorApplication.hierarchyChanged -= RefreshState;
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        UIEffectPreviewController.PreviewStateChanged -= RefreshState;
    }

    private void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        RefreshState();
    }

    private void OnMouseDown(MouseDownEvent evt)
    {
        if (evt.button != 0)
        {
            return;
        }

        ignoreNextClicked = true;
        UIEffectPreviewController.TogglePreview();
        RefreshState();
        evt.StopImmediatePropagation();
    }

    private void OnClicked()
    {
        if (ignoreNextClicked)
        {
            ignoreNextClicked = false;
            return;
        }

        UIEffectPreviewController.TogglePreview();
        RefreshState();
    }

    private void RefreshState()
    {
        bool isPreviewing = UIEffectPreviewController.IsPreviewing;
        text = isPreviewing ? "■ 粒子" : "▶ 粒子";
        SetEnabled(isPreviewing
            || (!EditorApplication.isPlayingOrWillChangePlaymode
                && UIEffectPreviewController.HasPreviewTargets()));
    }
}
