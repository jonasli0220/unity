using System;
using System.Collections.Generic;
using SgrEngine;
using Spine.Unity;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

using LegacyAnimation = UnityEngine.Animation;
using SpineSkeletonAnimation = Spine.Unity.SkeletonAnimation;

internal static class UIEffectPreviewTargetUtility
{
    public static List<GameObject> CollectTargetRoots()
    {
        List<GameObject> result = new List<GameObject>();
        HashSet<int> seen = new HashSet<int>();
        PrefabStage prefabStage = PrefabStageUtility.GetCurrentPrefabStage();

        if (prefabStage != null && prefabStage.prefabContentsRoot != null)
        {
            result.Add(prefabStage.prefabContentsRoot);
            return result;
        }

        AddSelectedRoots(result, seen, default(Scene), null);
        return result;
    }

    private static void AddSelectedRoots(
        List<GameObject> result,
        HashSet<int> seen,
        Scene requiredScene,
        Transform requiredRoot)
    {
        GameObject[] selectedObjects = Selection.gameObjects;
        for (int i = 0; i < selectedObjects.Length; i++)
        {
            GameObject selectedObject = selectedObjects[i];
            if (!IsSceneObject(selectedObject))
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

            if (!ContainsSupportedEffect(selectedObject) || !seen.Add(selectedObject.GetInstanceID()))
            {
                continue;
            }

            result.Add(selectedObject);
        }
    }

    public static bool IsPreviewable(Component component)
    {
        return component != null
            && !EditorUtility.IsPersistent(component)
            && IsSceneObject(component.gameObject)
            && component.gameObject.activeInHierarchy;
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

    private static bool ContainsSupportedEffect(GameObject root)
    {
        return root.GetComponentInChildren<ParticleSystem>(true) != null
            || root.GetComponentInChildren<SkeletonGraphic>(true) != null
            || root.GetComponentInChildren<SpineSkeletonAnimation>(true) != null
            || root.GetComponentInChildren<SkeletonMecanim>(true) != null
            || (UIExtendedEffectPreview.IncludeAnimatorPreview
                && (root.GetComponentInChildren<Animator>(true) != null
                    || root.GetComponentInChildren<LegacyAnimation>(true) != null));
    }
}

internal static class UIExtendedEffectPreview
{
    private const string EmptyAnimationName = "__Empty__";
    private const string IncludeAnimatorMenuPath = "UITools/动态预览包含animator";
    private const string IncludeAnimatorPrefKey = "Dragon.UI.EffectPreview.IncludeAnimator";

    private static readonly List<SpinePreview> ActiveSpines = new List<SpinePreview>();
    private static readonly List<AnimatorPreview> ActiveAnimators = new List<AnimatorPreview>();
    private static readonly List<LegacyAnimationPreview> ActiveLegacyAnimations = new List<LegacyAnimationPreview>();

    private static PreviewStateSnapshot stateSnapshot;
    private static bool ownsAnimationMode;

    public static bool IncludeAnimatorPreview
    {
        get { return EditorPrefs.GetBool(IncludeAnimatorPrefKey, false); }
    }

    public static bool IsPreviewing
    {
        get
        {
            return ActiveSpines.Count > 0
                || ActiveAnimators.Count > 0
                || ActiveLegacyAnimations.Count > 0
                || stateSnapshot != null;
        }
    }

    public static string Summary
    {
        get
        {
            string animatorSummary = IncludeAnimatorPreview
                ? $"Animator {ActiveAnimators.Count} / Animation {ActiveLegacyAnimations.Count}"
                : "Animator 关闭";
            return $"Spine {ActiveSpines.Count} / {animatorSummary}";
        }
    }

    public static bool HasPreviewTargets()
    {
        return CollectTargets().GetPreviewableCount(IncludeAnimatorPreview) > 0;
    }

    [MenuItem(IncludeAnimatorMenuPath, false, 3000)]
    private static void ToggleIncludeAnimatorPreview()
    {
        bool enabled = !IncludeAnimatorPreview;
        EditorPrefs.SetBool(IncludeAnimatorPrefKey, enabled);
        Debug.Log("动态预览包含animator：" + (enabled ? "开启" : "关闭"));
        SceneView sceneView = SceneView.lastActiveSceneView;
        if (sceneView != null)
        {
            sceneView.ShowNotification(new GUIContent(enabled ? "动态预览将包含 Animator" : "动态预览不包含 Animator"));
        }

        UIEffectPreviewController.NotifyPreviewSettingsChanged();
    }

    [MenuItem(IncludeAnimatorMenuPath, true)]
    private static bool ValidateToggleIncludeAnimatorPreview()
    {
        Menu.SetChecked(IncludeAnimatorMenuPath, IncludeAnimatorPreview);
        return true;
    }

    public static bool StartPreview()
    {
        StopPreview();
        Targets targets = CollectTargets();
        bool includeAnimator = IncludeAnimatorPreview;
        if (targets.GetPreviewableCount(includeAnimator) == 0)
        {
            return false;
        }

        stateSnapshot = PreviewStateSnapshot.Capture(targets.roots);
        StartSpines(targets);

        if (includeAnimator && (targets.animators.Count > 0 || targets.legacyAnimations.Count > 0))
        {
            if (AnimationMode.InAnimationMode())
            {
                Debug.LogWarning("Effect preview skipped Animator and legacy Animation because Unity Animation Mode is already in use.");
            }
            else
            {
                StartUnityAnimations(targets);
            }
        }

        return IsPreviewing;
    }

    public static void Update(float deltaTime)
    {
        UpdateUnityAnimations(deltaTime);
        UpdateSpines(deltaTime);
    }

    public static void StopPreview()
    {
        PreviewStateSnapshot snapshot = stateSnapshot;
        stateSnapshot = null;
        StopOwnedAnimationMode();

        for (int i = 0; i < ActiveSpines.Count; i++)
        {
            try
            {
                ActiveSpines[i].Reset();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("Effect preview could not reset Spine component: " + ex.Message);
            }
        }

        ActiveSpines.Clear();
        ActiveAnimators.Clear();
        ActiveLegacyAnimations.Clear();

        if (snapshot != null)
        {
            snapshot.Restore();
        }
    }

    private static Targets CollectTargets()
    {
        Targets result = new Targets();
        List<GameObject> roots = UIEffectPreviewTargetUtility.CollectTargetRoots();
        for (int i = 0; i < roots.Count; i++)
        {
            result.AddRoot(roots[i]);
            result.AddUnder(roots[i]);
        }

        return result;
    }

    private static void StartSpines(Targets targets)
    {
        for (int i = 0; i < targets.skeletonGraphics.Count; i++)
        {
            TryStartSpine(new SpinePreview(targets.skeletonGraphics[i]));
        }

        for (int i = 0; i < targets.skeletonAnimations.Count; i++)
        {
            TryStartSpine(new SpinePreview(targets.skeletonAnimations[i]));
        }

        for (int i = 0; i < targets.skeletonMecanims.Count; i++)
        {
            TryStartSpine(new SpinePreview(targets.skeletonMecanims[i]));
        }
    }

    private static void TryStartSpine(SpinePreview preview)
    {
        try
        {
            if (preview.Start())
            {
                ActiveSpines.Add(preview);
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("Effect preview skipped Spine component: " + ex.Message);
        }
    }

    private static void StartUnityAnimations(Targets targets)
    {
        try
        {
            AnimationMode.StartAnimationMode();
            ownsAnimationMode = true;
            AnimationMode.BeginSampling();
            try
            {
                for (int i = 0; i < targets.animators.Count; i++)
                {
                    AnimatorPreview preview = new AnimatorPreview(targets.animators[i]);
                    if (preview.Start())
                    {
                        ActiveAnimators.Add(preview);
                    }
                }

                for (int i = 0; i < targets.legacyAnimations.Count; i++)
                {
                    LegacyAnimationPreview preview = new LegacyAnimationPreview(targets.legacyAnimations[i]);
                    if (preview.Start())
                    {
                        ActiveLegacyAnimations.Add(preview);
                    }
                }
            }
            finally
            {
                AnimationMode.EndSampling();
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("Effect preview could not start Unity Animation Mode: " + ex.Message);
            ActiveAnimators.Clear();
            ActiveLegacyAnimations.Clear();
            StopOwnedAnimationMode();
        }
    }

    private static void UpdateUnityAnimations(float deltaTime)
    {
        if (!ownsAnimationMode)
        {
            return;
        }

        if (!AnimationMode.InAnimationMode())
        {
            ownsAnimationMode = false;
            ActiveAnimators.Clear();
            ActiveLegacyAnimations.Clear();
            return;
        }

        try
        {
            AnimationMode.BeginSampling();
            try
            {
                for (int i = ActiveAnimators.Count - 1; i >= 0; i--)
                {
                    if (!ActiveAnimators[i].IsValid)
                    {
                        ActiveAnimators.RemoveAt(i);
                        continue;
                    }

                    ActiveAnimators[i].Update(deltaTime);
                }

                for (int i = ActiveLegacyAnimations.Count - 1; i >= 0; i--)
                {
                    if (!ActiveLegacyAnimations[i].IsValid)
                    {
                        ActiveLegacyAnimations.RemoveAt(i);
                        continue;
                    }

                    ActiveLegacyAnimations[i].Update(deltaTime);
                }
            }
            finally
            {
                AnimationMode.EndSampling();
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("Effect preview stopped Animator/Animation sampling: " + ex.Message);
            ActiveAnimators.Clear();
            ActiveLegacyAnimations.Clear();
            StopOwnedAnimationMode();
        }
    }

    private static void UpdateSpines(float deltaTime)
    {
        for (int i = ActiveSpines.Count - 1; i >= 0; i--)
        {
            SpinePreview preview = ActiveSpines[i];
            if (!preview.IsValid)
            {
                ActiveSpines.RemoveAt(i);
                continue;
            }

            try
            {
                preview.Update(deltaTime);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("Effect preview stopped a Spine component: " + ex.Message);
                ActiveSpines.RemoveAt(i);
            }
        }
    }

    private static void StopOwnedAnimationMode()
    {
        if (!ownsAnimationMode)
        {
            return;
        }

        ownsAnimationMode = false;
        if (AnimationMode.InAnimationMode())
        {
            AnimationMode.StopAnimationMode();
        }
    }

    private static bool IsUsableAnimationName(string animationName)
    {
        return !string.IsNullOrEmpty(animationName) && animationName != EmptyAnimationName;
    }

    private sealed class PreviewStateSnapshot
    {
        private readonly List<TransformState> transforms = new List<TransformState>();
        private readonly List<GraphicState> graphics = new List<GraphicState>();
        private readonly List<CanvasGroupState> canvasGroups = new List<CanvasGroupState>();
        private readonly List<CanvasRendererState> canvasRenderers = new List<CanvasRendererState>();

        public static PreviewStateSnapshot Capture(List<GameObject> roots)
        {
            PreviewStateSnapshot snapshot = new PreviewStateSnapshot();
            HashSet<int> seenTransforms = new HashSet<int>();
            HashSet<int> seenGraphics = new HashSet<int>();
            HashSet<int> seenCanvasGroups = new HashSet<int>();
            HashSet<int> seenCanvasRenderers = new HashSet<int>();

            for (int i = 0; i < roots.Count; i++)
            {
                GameObject root = roots[i];
                if (root == null)
                {
                    continue;
                }

                Transform[] rootTransforms = root.GetComponentsInChildren<Transform>(false);
                for (int j = 0; j < rootTransforms.Length; j++)
                {
                    Transform transform = rootTransforms[j];
                    if (transform != null && seenTransforms.Add(transform.GetInstanceID()))
                    {
                        snapshot.transforms.Add(new TransformState(transform));
                    }
                }

                Graphic[] rootGraphics = root.GetComponentsInChildren<Graphic>(false);
                for (int j = 0; j < rootGraphics.Length; j++)
                {
                    Graphic graphic = rootGraphics[j];
                    if (graphic != null && seenGraphics.Add(graphic.GetInstanceID()))
                    {
                        snapshot.graphics.Add(new GraphicState(graphic));
                    }
                }

                CanvasGroup[] rootCanvasGroups = root.GetComponentsInChildren<CanvasGroup>(false);
                for (int j = 0; j < rootCanvasGroups.Length; j++)
                {
                    CanvasGroup canvasGroup = rootCanvasGroups[j];
                    if (canvasGroup != null && seenCanvasGroups.Add(canvasGroup.GetInstanceID()))
                    {
                        snapshot.canvasGroups.Add(new CanvasGroupState(canvasGroup));
                    }
                }

                CanvasRenderer[] rootCanvasRenderers = root.GetComponentsInChildren<CanvasRenderer>(false);
                for (int j = 0; j < rootCanvasRenderers.Length; j++)
                {
                    CanvasRenderer canvasRenderer = rootCanvasRenderers[j];
                    if (canvasRenderer != null && seenCanvasRenderers.Add(canvasRenderer.GetInstanceID()))
                    {
                        snapshot.canvasRenderers.Add(new CanvasRendererState(canvasRenderer));
                    }
                }
            }

            return snapshot;
        }

        public void Restore()
        {
            for (int i = 0; i < transforms.Count; i++)
            {
                transforms[i].Restore();
            }

            for (int i = 0; i < graphics.Count; i++)
            {
                graphics[i].Restore();
            }

            for (int i = 0; i < canvasGroups.Count; i++)
            {
                canvasGroups[i].Restore();
            }

            for (int i = 0; i < canvasRenderers.Count; i++)
            {
                canvasRenderers[i].Restore();
            }

            Canvas.ForceUpdateCanvases();
        }
    }

    private sealed class TransformState
    {
        private readonly Transform transform;
        private readonly Vector3 localPosition;
        private readonly Quaternion localRotation;
        private readonly Vector3 localScale;
        private readonly bool isRectTransform;
        private readonly Vector2 anchorMin;
        private readonly Vector2 anchorMax;
        private readonly Vector2 anchoredPosition;
        private readonly Vector3 anchoredPosition3D;
        private readonly Vector2 sizeDelta;
        private readonly Vector2 pivot;

        public TransformState(Transform source)
        {
            transform = source;
            localPosition = source.localPosition;
            localRotation = source.localRotation;
            localScale = source.localScale;
            anchorMin = Vector2.zero;
            anchorMax = Vector2.zero;
            anchoredPosition = Vector2.zero;
            anchoredPosition3D = Vector3.zero;
            sizeDelta = Vector2.zero;
            pivot = Vector2.zero;

            RectTransform rectTransform = source as RectTransform;
            isRectTransform = rectTransform != null;
            if (isRectTransform)
            {
                anchorMin = rectTransform.anchorMin;
                anchorMax = rectTransform.anchorMax;
                anchoredPosition = rectTransform.anchoredPosition;
                anchoredPosition3D = rectTransform.anchoredPosition3D;
                sizeDelta = rectTransform.sizeDelta;
                pivot = rectTransform.pivot;
            }
        }

        public void Restore()
        {
            if (transform == null)
            {
                return;
            }

            RectTransform rectTransform = transform as RectTransform;
            if (isRectTransform && rectTransform != null)
            {
                rectTransform.anchorMin = anchorMin;
                rectTransform.anchorMax = anchorMax;
                rectTransform.pivot = pivot;
                rectTransform.sizeDelta = sizeDelta;
                rectTransform.anchoredPosition = anchoredPosition;
                rectTransform.anchoredPosition3D = anchoredPosition3D;
            }

            transform.localPosition = localPosition;
            transform.localRotation = localRotation;
            transform.localScale = localScale;
        }
    }

    private sealed class GraphicState
    {
        private readonly Graphic graphic;
        private readonly Material material;
        private readonly Color color;

        public GraphicState(Graphic source)
        {
            graphic = source;
            material = source.material;
            color = source.color;
        }

        public void Restore()
        {
            if (graphic != null)
            {
                graphic.material = material;
                graphic.color = color;
                graphic.SetVerticesDirty();
                graphic.SetMaterialDirty();
            }
        }
    }

    private sealed class CanvasGroupState
    {
        private readonly CanvasGroup canvasGroup;
        private readonly float alpha;
        private readonly bool interactable;
        private readonly bool blocksRaycasts;
        private readonly bool ignoreParentGroups;

        public CanvasGroupState(CanvasGroup source)
        {
            canvasGroup = source;
            alpha = source.alpha;
            interactable = source.interactable;
            blocksRaycasts = source.blocksRaycasts;
            ignoreParentGroups = source.ignoreParentGroups;
        }

        public void Restore()
        {
            if (canvasGroup == null)
            {
                return;
            }

            canvasGroup.alpha = alpha;
            canvasGroup.interactable = interactable;
            canvasGroup.blocksRaycasts = blocksRaycasts;
            canvasGroup.ignoreParentGroups = ignoreParentGroups;
        }
    }

    private sealed class CanvasRendererState
    {
        private readonly CanvasRenderer canvasRenderer;
        private readonly float alpha;

        public CanvasRendererState(CanvasRenderer source)
        {
            canvasRenderer = source;
            alpha = source.GetAlpha();
        }

        public void Restore()
        {
            if (canvasRenderer != null)
            {
                canvasRenderer.SetAlpha(alpha);
            }
        }
    }

    private sealed class Targets
    {
        public readonly List<GameObject> roots = new List<GameObject>();
        public readonly List<SkeletonGraphic> skeletonGraphics = new List<SkeletonGraphic>();
        public readonly List<SpineSkeletonAnimation> skeletonAnimations = new List<SpineSkeletonAnimation>();
        public readonly List<SkeletonMecanim> skeletonMecanims = new List<SkeletonMecanim>();
        public readonly List<Animator> animators = new List<Animator>();
        public readonly List<LegacyAnimation> legacyAnimations = new List<LegacyAnimation>();
        private readonly HashSet<int> seen = new HashSet<int>();

        public int TotalCount
        {
            get
            {
                return skeletonGraphics.Count
                    + skeletonAnimations.Count
                    + skeletonMecanims.Count
                    + animators.Count
                    + legacyAnimations.Count;
            }
        }

        public int GetPreviewableCount(bool includeAnimator)
        {
            int count = skeletonGraphics.Count + skeletonAnimations.Count + skeletonMecanims.Count;
            if (includeAnimator)
            {
                count += animators.Count + legacyAnimations.Count;
            }

            return count;
        }

        public void AddRoot(GameObject root)
        {
            if (root != null && !roots.Contains(root))
            {
                roots.Add(root);
            }
        }

        public void AddUnder(GameObject root)
        {
            AddComponents(root.GetComponentsInChildren<SkeletonGraphic>(true), skeletonGraphics);
            AddComponents(root.GetComponentsInChildren<SpineSkeletonAnimation>(true), skeletonAnimations);
            AddComponents(root.GetComponentsInChildren<SkeletonMecanim>(true), skeletonMecanims);
            AddComponents(root.GetComponentsInChildren<Animator>(true), animators);
            AddComponents(root.GetComponentsInChildren<LegacyAnimation>(true), legacyAnimations);
        }

        private void AddComponents<T>(T[] components, List<T> output) where T : Behaviour
        {
            for (int i = 0; i < components.Length; i++)
            {
                T component = components[i];
                if (!UIEffectPreviewTargetUtility.IsPreviewable(component)
                    || !component.enabled
                    || !seen.Add(component.GetInstanceID()))
                {
                    continue;
                }

                if (component is Animator animator && animator.runtimeAnimatorController == null)
                {
                    continue;
                }

                output.Add(component);
            }
        }
    }

    private sealed class SpinePreview
    {
        private readonly SkeletonGraphic skeletonGraphic;
        private readonly SpineSkeletonAnimation skeletonAnimation;
        private readonly SkeletonMecanim skeletonMecanim;
        private Spine.AnimationState animationState;
        private string pendingLoopName;
        private float switchToLoopAt;
        private float elapsed;

        public SpinePreview(SkeletonGraphic component) { skeletonGraphic = component; }
        public SpinePreview(SpineSkeletonAnimation component) { skeletonAnimation = component; }
        public SpinePreview(SkeletonMecanim component) { skeletonMecanim = component; }

        private Component Component
        {
            get
            {
                if (skeletonGraphic != null) return skeletonGraphic;
                if (skeletonAnimation != null) return skeletonAnimation;
                return skeletonMecanim;
            }
        }

        public bool IsValid
        {
            get { return UIEffectPreviewTargetUtility.IsPreviewable(Component); }
        }

        public bool Start()
        {
            if (!IsValid)
            {
                return false;
            }

            elapsed = 0f;
            pendingLoopName = null;
            switchToLoopAt = 0f;

            if (skeletonGraphic != null)
            {
                skeletonGraphic.Initialize(false);
                animationState = skeletonGraphic.AnimationState;
                return StartAnimationState(
                    skeletonGraphic.SkeletonData,
                    skeletonGraphic.Skeleton,
                    skeletonGraphic.startingAnimation,
                    skeletonGraphic.startingLoop);
            }

            if (skeletonAnimation != null)
            {
                skeletonAnimation.Initialize(false);
                animationState = skeletonAnimation.AnimationState;
                return StartAnimationState(
                    skeletonAnimation.Skeleton != null ? skeletonAnimation.Skeleton.Data : null,
                    skeletonAnimation.Skeleton,
                    skeletonAnimation.AnimationName,
                    skeletonAnimation.loop);
            }

            skeletonMecanim.Initialize(false);
            skeletonMecanim.Update(0f);
            return skeletonMecanim.Skeleton != null;
        }

        private bool StartAnimationState(
            Spine.SkeletonData skeletonData,
            Spine.Skeleton skeleton,
            string configuredName,
            bool configuredLoop)
        {
            if (animationState == null || skeletonData == null || skeleton == null)
            {
                return false;
            }

            UIPanelAnimation panelAnimation = Component.GetComponent<UIPanelAnimation>();
            string enterName = null;
            string loopName = null;
            bool autoToLoop = false;
            if (panelAnimation != null && panelAnimation.spineAnimation != null)
            {
                enterName = FindAnimationName(skeletonData, panelAnimation.spineAnimation.enterName);
                loopName = FindAnimationName(skeletonData, panelAnimation.spineAnimation.loopName);
                autoToLoop = panelAnimation.autoToLoop;
            }

            string startName = enterName;
            bool startLoop = false;
            if (startName == null)
            {
                startName = loopName;
                startLoop = startName != null;
            }

            if (startName == null)
            {
                startName = FindAnimationName(skeletonData, configuredName);
                startLoop = configuredLoop;
            }

            if (startName == null)
            {
                startName = ChooseFallbackAnimation(skeletonData);
                startLoop = startName != null;
            }

            if (startName == null)
            {
                return false;
            }

            animationState.ClearTracks();
            skeleton.SetToSetupPose();
            Spine.TrackEntry track = animationState.SetAnimation(0, startName, startLoop);
            if (track == null)
            {
                return false;
            }

            if (enterName != null && loopName != null && autoToLoop)
            {
                pendingLoopName = loopName;
                switchToLoopAt = track.Animation.Duration;
            }

            UpdateDirectSpine(0f);
            return true;
        }

        public void Update(float deltaTime)
        {
            if (skeletonMecanim != null)
            {
                skeletonMecanim.Update(deltaTime);
                return;
            }

            UpdateDirectSpine(deltaTime);
            elapsed += deltaTime;
            if (pendingLoopName != null && elapsed >= switchToLoopAt)
            {
                animationState.SetAnimation(0, pendingLoopName, true);
                pendingLoopName = null;
                UpdateDirectSpine(0f);
            }
        }

        private void UpdateDirectSpine(float deltaTime)
        {
            if (skeletonGraphic != null)
            {
                skeletonGraphic.Update(deltaTime);
            }
            else if (skeletonAnimation != null)
            {
                skeletonAnimation.Update(deltaTime);
            }
        }

        public void Reset()
        {
            if (skeletonGraphic != null)
            {
                skeletonGraphic.Initialize(true);
                skeletonGraphic.Update(0f);
                skeletonGraphic.SetVerticesDirty();
            }
            else if (skeletonAnimation != null)
            {
                skeletonAnimation.Initialize(true);
                skeletonAnimation.Update(0f);
            }
            else if (skeletonMecanim != null)
            {
                skeletonMecanim.Initialize(true);
                skeletonMecanim.Update(0f);
            }
        }

        private static string FindAnimationName(Spine.SkeletonData data, string candidate)
        {
            return IsUsableAnimationName(candidate) && data.FindAnimation(candidate) != null
                ? candidate
                : null;
        }

        private static string ChooseFallbackAnimation(Spine.SkeletonData data)
        {
            string[] preferredNames = { "idle", "loop", "animation", "default" };
            for (int i = 0; i < preferredNames.Length; i++)
            {
                if (data.FindAnimation(preferredNames[i]) != null)
                {
                    return preferredNames[i];
                }
            }

            foreach (Spine.Animation animation in data.Animations)
            {
                return animation.Name;
            }

            return null;
        }
    }

    private sealed class AnimatorPreview
    {
        private readonly Animator animator;
        private string pendingLoopName;
        private float switchToLoopAt;
        private float elapsed;

        public AnimatorPreview(Animator component) { animator = component; }

        public bool IsValid
        {
            get
            {
                return HasValidAnimator();
            }
        }

        public bool Start()
        {
            if (!HasValidAnimator())
            {
                return false;
            }

            elapsed = 0f;
            pendingLoopName = null;
            switchToLoopAt = 0f;

            animator.Rebind();
            animator.Update(0f);

            UIPanelAnimation panelAnimation = animator.GetComponent<UIPanelAnimation>();
            if (panelAnimation != null && panelAnimation.animatorAnimationData != null)
            {
                string enterName = panelAnimation.animatorAnimationData.enterName;
                string loopName = panelAnimation.animatorAnimationData.loopName;
                if (IsUsableAnimationName(enterName))
                {
                    animator.Play(enterName, -1, 0f);
                    animator.Update(0f);

                    if (panelAnimation.autoToLoop && IsUsableAnimationName(loopName))
                    {
                        pendingLoopName = loopName;
                        switchToLoopAt = FindClipLength(enterName);
                    }
                }
                else if (IsUsableAnimationName(loopName))
                {
                    animator.Play(loopName, -1, 0f);
                    animator.Update(0f);
                }
            }

            return true;
        }

        public void Update(float deltaTime)
        {
            if (!HasValidAnimator())
            {
                return;
            }

            elapsed += deltaTime;
            if (pendingLoopName != null && elapsed >= switchToLoopAt)
            {
                animator.Play(pendingLoopName, -1, 0f);
                pendingLoopName = null;
                elapsed = 0f;
                animator.Update(0f);
                return;
            }

            animator.Update(deltaTime);
        }

        private bool HasValidAnimator()
        {
            return UIEffectPreviewTargetUtility.IsPreviewable(animator)
                && animator.enabled
                && animator.runtimeAnimatorController != null;
        }

        private float FindClipLength(string clipName)
        {
            if (!IsUsableAnimationName(clipName) || animator.runtimeAnimatorController == null)
            {
                return 0f;
            }

            AnimationClip[] clips = animator.runtimeAnimatorController.animationClips;
            for (int i = 0; i < clips.Length; i++)
            {
                if (clips[i] != null && clips[i].name == clipName)
                {
                    return clips[i].length;
                }
            }

            return 0f;
        }
    }

    private sealed class LegacyAnimationPreview
    {
        private readonly LegacyAnimation animation;
        private AnimationClip clip;
        private AnimationState animationState;
        private float elapsed;

        public LegacyAnimationPreview(LegacyAnimation component) { animation = component; }

        public bool IsValid
        {
            get
            {
                return UIEffectPreviewTargetUtility.IsPreviewable(animation)
                    && animation.enabled
                    && clip != null;
            }
        }

        public bool Start()
        {
            if (!UIEffectPreviewTargetUtility.IsPreviewable(animation) || !animation.enabled)
            {
                return false;
            }

            clip = animation.clip;
            if (clip != null)
            {
                animationState = animation[clip.name];
            }

            if (clip == null)
            {
                foreach (AnimationState state in animation)
                {
                    if (state != null && state.clip != null)
                    {
                        animationState = state;
                        clip = state.clip;
                        break;
                    }
                }
            }

            if (clip == null)
            {
                return false;
            }

            elapsed = 0f;
            AnimationMode.SampleAnimationClip(animation.gameObject, clip, 0f);
            return true;
        }

        public void Update(float deltaTime)
        {
            elapsed += deltaTime * (animationState != null ? animationState.speed : 1f);
            float sampleTime = elapsed;
            WrapMode wrapMode = clip.wrapMode != WrapMode.Default ? clip.wrapMode : animation.wrapMode;

            if (clip.length > 0f)
            {
                if (wrapMode == WrapMode.Loop)
                {
                    sampleTime = Mathf.Repeat(sampleTime, clip.length);
                }
                else if (wrapMode == WrapMode.PingPong)
                {
                    sampleTime = Mathf.PingPong(sampleTime, clip.length);
                }
                else
                {
                    sampleTime = Mathf.Clamp(sampleTime, 0f, clip.length);
                }
            }

            AnimationMode.SampleAnimationClip(animation.gameObject, clip, sampleTime);
        }
    }
}
