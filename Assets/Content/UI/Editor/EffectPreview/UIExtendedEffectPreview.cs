using System;
using System.Collections.Generic;
using SgrEngine;
using Spine.Unity;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

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
            AddSelectedRoots(result, seen, prefabStage.scene, prefabStage.prefabContentsRoot.transform);
            if (result.Count == 0)
            {
                result.Add(prefabStage.prefabContentsRoot);
            }

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

    public static GameObject GetCurrentPrefabRoot()
    {
        PrefabStage prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
        return prefabStage != null ? prefabStage.prefabContentsRoot : null;
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
            || root.GetComponentInChildren<Animator>(true) != null
            || root.GetComponentInChildren<LegacyAnimation>(true) != null;
    }
}

internal static class UIExtendedEffectPreview
{
    private const string EmptyAnimationName = "__Empty__";

    private static readonly List<SpinePreview> ActiveSpines = new List<SpinePreview>();
    private static readonly List<AnimatorPreview> ActiveAnimators = new List<AnimatorPreview>();
    private static readonly List<LegacyAnimationPreview> ActiveLegacyAnimations = new List<LegacyAnimationPreview>();

    private static bool ownsAnimationMode;

    public static bool IsPreviewing
    {
        get
        {
            return ActiveSpines.Count > 0
                || ActiveAnimators.Count > 0
                || ActiveLegacyAnimations.Count > 0;
        }
    }

    public static string Summary
    {
        get
        {
            return $"Spine {ActiveSpines.Count} / Animator {ActiveAnimators.Count} / Animation {ActiveLegacyAnimations.Count}";
        }
    }

    public static bool HasPreviewTargets()
    {
        return CollectTargets().TotalCount > 0;
    }

    public static bool StartPreview()
    {
        StopPreview();
        Targets targets = CollectTargets();
        if (targets.TotalCount == 0)
        {
            return false;
        }

        StartSpines(targets);

        if (targets.animators.Count > 0 || targets.legacyAnimations.Count > 0)
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
    }

    private static Targets CollectTargets()
    {
        Targets result = new Targets();
        List<GameObject> roots = UIEffectPreviewTargetUtility.CollectTargetRoots();
        for (int i = 0; i < roots.Count; i++)
        {
            result.AddUnder(roots[i]);
        }

        GameObject prefabRoot = UIEffectPreviewTargetUtility.GetCurrentPrefabRoot();
        if (prefabRoot != null)
        {
            result.AddAnimatorsUnder(prefabRoot);
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

    private sealed class Targets
    {
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

        public void AddUnder(GameObject root)
        {
            AddComponents(root.GetComponentsInChildren<SkeletonGraphic>(true), skeletonGraphics);
            AddComponents(root.GetComponentsInChildren<SpineSkeletonAnimation>(true), skeletonAnimations);
            AddComponents(root.GetComponentsInChildren<SkeletonMecanim>(true), skeletonMecanims);
            AddComponents(root.GetComponentsInChildren<Animator>(true), animators);
            AddComponents(root.GetComponentsInChildren<LegacyAnimation>(true), legacyAnimations);
        }

        public void AddAnimatorsUnder(GameObject root)
        {
            AddComponents(root.GetComponentsInChildren<Animator>(true), animators);
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
        private AnimationClip currentClip;
        private AnimationClip pendingLoopClip;
        private List<KeyValuePair<AnimationClip, AnimationClip>> overrideClips;
        private bool currentLoop;
        private float switchToLoopAt;
        private float elapsed;

        public AnimatorPreview(Animator component) { animator = component; }

        public bool IsValid
        {
            get
            {
                return HasValidAnimator() && currentClip != null;
            }
        }

        public bool Start()
        {
            if (!HasValidAnimator())
            {
                return false;
            }

            elapsed = 0f;
            currentClip = null;
            pendingLoopClip = null;
            currentLoop = false;
            switchToLoopAt = 0f;

            UIPanelAnimation panelAnimation = animator.GetComponent<UIPanelAnimation>();
            if (panelAnimation != null && panelAnimation.animatorAnimationData != null)
            {
                string enterName = panelAnimation.animatorAnimationData.enterName;
                string loopName = panelAnimation.animatorAnimationData.loopName;
                AnimationClip enterClip = FindClip(enterName);
                AnimationClip loopClip = FindClip(loopName);
                if (enterClip != null)
                {
                    currentClip = enterClip;
                    currentLoop = false;
                    if (panelAnimation.autoToLoop && loopClip != null)
                    {
                        pendingLoopClip = loopClip;
                        switchToLoopAt = enterClip.length;
                    }
                }
                else if (loopClip != null)
                {
                    currentClip = loopClip;
                    currentLoop = true;
                }
            }

            if (currentClip == null)
            {
                TryUseControllerEntry();
            }

            if (currentClip == null)
            {
                UseFallbackClip();
            }

            if (currentClip == null)
            {
                return false;
            }

            Sample(0f);
            return true;
        }

        public void Update(float deltaTime)
        {
            elapsed += deltaTime;
            if (pendingLoopClip != null && elapsed >= switchToLoopAt)
            {
                currentClip = pendingLoopClip;
                pendingLoopClip = null;
                currentLoop = true;
                elapsed = 0f;
                Sample(0f);
                return;
            }

            Sample(elapsed);
        }

        private bool HasValidAnimator()
        {
            return UIEffectPreviewTargetUtility.IsPreviewable(animator)
                && animator.enabled
                && animator.runtimeAnimatorController != null;
        }

        private void Sample(float time)
        {
            if (currentClip == null)
            {
                return;
            }

            float sampleTime = time;
            if (currentClip.length > 0f)
            {
                sampleTime = currentLoop
                    ? Mathf.Repeat(time, currentClip.length)
                    : Mathf.Clamp(time, 0f, currentClip.length);
            }

            AnimationMode.SampleAnimationClip(animator.gameObject, currentClip, sampleTime);
        }

        private AnimationClip FindClip(string clipName)
        {
            if (!IsUsableAnimationName(clipName))
            {
                return null;
            }

            AnimationClip stateClip = FindStateClip(clipName);
            if (stateClip != null)
            {
                return stateClip;
            }

            if (animator.runtimeAnimatorController == null)
            {
                return null;
            }

            AnimationClip[] clips = animator.runtimeAnimatorController.animationClips;
            for (int i = 0; i < clips.Length; i++)
            {
                if (clips[i] != null && clips[i].name == clipName)
                {
                    return clips[i];
                }
            }

            return null;
        }

        private void TryUseControllerEntry()
        {
            AnimatorController animatorController = GetAnimatorController();
            if (animatorController == null || animatorController.layers == null || animatorController.layers.Length == 0)
            {
                return;
            }

            AnimatorState entryState = animatorController.layers[0].stateMachine != null
                ? animatorController.layers[0].stateMachine.defaultState
                : null;
            AnimationClip entryClip = ResolveStateClip(entryState);
            if (entryClip == null)
            {
                return;
            }

            currentClip = entryClip;
            currentLoop = ShouldLoop(entryClip);

            AnimatorStateTransition loopTransition = FindNextTransition(entryState);
            AnimationClip loopClip = loopTransition != null
                ? ResolveStateClip(loopTransition.destinationState)
                : null;
            if (loopClip != null && loopClip != entryClip)
            {
                pendingLoopClip = loopClip;
                switchToLoopAt = GetTransitionSwitchTime(entryClip, loopTransition);
            }
        }

        private void UseFallbackClip()
        {
            if (animator.runtimeAnimatorController == null)
            {
                return;
            }

            AnimationClip[] clips = animator.runtimeAnimatorController.animationClips;
            for (int i = 0; i < clips.Length; i++)
            {
                if (clips[i] != null)
                {
                    currentClip = clips[i];
                    currentLoop = ShouldLoop(currentClip);
                    return;
                }
            }
        }

        private AnimationClip FindStateClip(string stateName)
        {
            AnimatorController animatorController = GetAnimatorController();
            if (animatorController == null || animatorController.layers == null)
            {
                return null;
            }

            for (int i = 0; i < animatorController.layers.Length; i++)
            {
                AnimatorStateMachine stateMachine = animatorController.layers[i].stateMachine;
                AnimatorState state = FindState(stateMachine, stateName);
                AnimationClip clip = ResolveStateClip(state);
                if (clip != null)
                {
                    return clip;
                }
            }

            return null;
        }

        private AnimatorController GetAnimatorController()
        {
            RuntimeAnimatorController controller = animator.runtimeAnimatorController;
            AnimatorOverrideController overrideController = controller as AnimatorOverrideController;
            if (overrideController != null)
            {
                controller = overrideController.runtimeAnimatorController;
            }

            return controller as AnimatorController;
        }

        private AnimatorState FindState(AnimatorStateMachine stateMachine, string stateName)
        {
            if (stateMachine == null || !IsUsableAnimationName(stateName))
            {
                return null;
            }

            ChildAnimatorState[] states = stateMachine.states;
            for (int i = 0; i < states.Length; i++)
            {
                AnimatorState state = states[i].state;
                if (state != null && state.name == stateName)
                {
                    return state;
                }
            }

            ChildAnimatorStateMachine[] childStateMachines = stateMachine.stateMachines;
            for (int i = 0; i < childStateMachines.Length; i++)
            {
                AnimatorState state = FindState(childStateMachines[i].stateMachine, stateName);
                if (state != null)
                {
                    return state;
                }
            }

            return null;
        }

        private AnimationClip ResolveStateClip(AnimatorState state)
        {
            return state != null ? ResolveMotionClip(state.motion) : null;
        }

        private AnimationClip ResolveMotionClip(Motion motion)
        {
            AnimationClip clip = motion as AnimationClip;
            if (clip != null)
            {
                return ResolveOverrideClip(clip);
            }

            BlendTree blendTree = motion as BlendTree;
            if (blendTree == null)
            {
                return null;
            }

            ChildMotion[] childMotions = blendTree.children;
            for (int i = 0; i < childMotions.Length; i++)
            {
                clip = ResolveMotionClip(childMotions[i].motion);
                if (clip != null)
                {
                    return clip;
                }
            }

            return null;
        }

        private AnimationClip ResolveOverrideClip(AnimationClip originalClip)
        {
            AnimatorOverrideController overrideController = animator.runtimeAnimatorController as AnimatorOverrideController;
            if (overrideController == null || originalClip == null)
            {
                return originalClip;
            }

            if (overrideClips == null)
            {
                overrideClips = new List<KeyValuePair<AnimationClip, AnimationClip>>(overrideController.overridesCount);
                overrideController.GetOverrides(overrideClips);
            }

            for (int i = 0; i < overrideClips.Count; i++)
            {
                if (overrideClips[i].Key == originalClip)
                {
                    return overrideClips[i].Value != null ? overrideClips[i].Value : originalClip;
                }
            }

            return originalClip;
        }

        private AnimatorStateTransition FindNextTransition(AnimatorState state)
        {
            if (state == null || state.transitions == null)
            {
                return null;
            }

            AnimatorStateTransition fallback = null;
            AnimatorStateTransition[] transitions = state.transitions;
            for (int i = 0; i < transitions.Length; i++)
            {
                AnimatorStateTransition transition = transitions[i];
                if (transition == null || transition.destinationState == null)
                {
                    continue;
                }

                if (fallback == null)
                {
                    fallback = transition;
                }

                string destinationName = transition.destinationState.name;
                AnimationClip destinationClip = ResolveStateClip(transition.destinationState);
                if ((destinationName != null && destinationName.IndexOf("loop", StringComparison.OrdinalIgnoreCase) >= 0)
                    || (destinationClip != null && destinationClip.name.IndexOf("loop", StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    return transition;
                }
            }

            return fallback;
        }

        private static float GetTransitionSwitchTime(AnimationClip entryClip, AnimatorStateTransition transition)
        {
            if (entryClip == null || entryClip.length <= 0f)
            {
                return 0f;
            }

            if (transition != null && transition.hasExitTime)
            {
                return Mathf.Clamp01(transition.exitTime) * entryClip.length;
            }

            return entryClip.length;
        }

        private static bool ShouldLoop(AnimationClip clip)
        {
            if (clip == null)
            {
                return false;
            }

            return clip.isLooping || clip.wrapMode == WrapMode.Loop;
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
