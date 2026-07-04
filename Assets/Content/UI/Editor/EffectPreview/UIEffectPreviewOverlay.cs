using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Toolbars;
using UnityEngine;
using UnityEngine.UIElements;

[InitializeOnLoad]
public static class UIEffectPreviewController
{
    private const string ToggleMenuPath = "Tools/UI/Effect Preview/Play or Stop Dynamic Effects";
    private const string StopMenuPath = "Tools/UI/Effect Preview/Stop and Reset Dynamic Effects";
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
        get { return ActiveParticleSystems.Count > 0 || UIExtendedEffectPreview.IsPreviewing; }
    }

    public static bool HasPreviewTargets()
    {
        return CollectParticleSystems().Count > 0 || UIExtendedEffectPreview.HasPreviewTargets();
    }

    public static bool StartPreview()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Warn("动态效果预览只在非运行模式可用。", "请先退出运行模式");
            return false;
        }

        List<ParticleSystem> targets = CollectParticleSystems();
        bool hasExtendedTargets = UIExtendedEffectPreview.HasPreviewTargets();
        if (targets.Count == 0 && !hasExtendedTargets)
        {
            Warn(
                PrefabStageUtility.GetCurrentPrefabStage() != null
                    ? "当前 Prefab 中没有可预览的粒子、Spine、Animator 或 Animation。"
                    : "请先选择包含动态效果的节点。",
                "没有找到可预览的动态效果");
            return false;
        }

        StopPreview(false);
        ActiveParticleSystems.AddRange(targets);
        bool extendedStarted = UIExtendedEffectPreview.StartPreview();

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

        if (ActiveParticleSystems.Count == 0 && !extendedStarted)
        {
            Warn("找到的动态效果没有可播放的动画或有效资源。", "动态效果没有可播放内容");
            return false;
        }

        lastUpdateTime = EditorApplication.timeSinceStartup;
        string summary = $"粒子 {ActiveParticleSystems.Count} / {UIExtendedEffectPreview.Summary}";
        Debug.Log("Dynamic effect preview started. " + summary + ". Click ■ 动效 to stop and reset.");
        NotifySceneView("正在预览：" + summary);
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
        bool hadPreview = IsPreviewing;

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
        UIExtendedEffectPreview.StopPreview();
        lastUpdateTime = 0d;

        if (!hadPreview)
        {
            return;
        }

        if (notify)
        {
            Debug.Log("Dynamic effect preview stopped and temporary edit-mode state was reset.");
            NotifySceneView("动效预览已停止");
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

        bool shouldRefresh = false;
        for (int i = ActiveParticleSystems.Count - 1; i >= 0; i--)
        {
            ParticleSystem particleSystem = ActiveParticleSystems[i];
            if (particleSystem == null || !UIEffectPreviewTargetUtility.IsPreviewable(particleSystem))
            {
                ActiveParticleSystems.RemoveAt(i);
                continue;
            }

            particleSystem.Simulate(deltaTime, false, false, true);
            shouldRefresh = true;
        }

        bool hadExtendedPreview = UIExtendedEffectPreview.IsPreviewing;
        UIExtendedEffectPreview.Update(deltaTime);
        shouldRefresh |= hadExtendedPreview || UIExtendedEffectPreview.IsPreviewing;

        if (!IsPreviewing)
        {
            lastUpdateTime = 0d;
            RefreshViews();
            PreviewStateChanged?.Invoke();
            return;
        }

        if (shouldRefresh)
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
        List<GameObject> roots = UIEffectPreviewTargetUtility.CollectTargetRoots();

        for (int i = 0; i < roots.Count; i++)
        {
            AddParticleSystemsUnder(result, seen, roots[i]);
        }

        return result;
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
            if (!UIEffectPreviewTargetUtility.IsPreviewable(particleSystem))
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
        tooltip = "非运行模式预览粒子、Spine、Animator 和 Animation 动效。Prefab 模式下播放当前打开 Prefab 的所有已开启节点；普通 Scene 中播放选中节点。再次点击停止并复位。";
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
        text = isPreviewing ? "■ 动效" : "▶ 动效";
        SetEnabled(isPreviewing
            || (!EditorApplication.isPlayingOrWillChangePlaymode
                && UIEffectPreviewController.HasPreviewTargets()));
    }
}
