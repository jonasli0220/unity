using System;
using System.Collections.Generic;
using System.IO;
using SgrUnity;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

public static class AutoCloseOutSetup
{
    private const string MenuPath = "GameObject/UI Animation/Setup Auto Close Button";
    private const string AutoClosePanelName = "auto_close_panel";
    private const string AutoCloseButtonName = "auto_close_button";
    private const string OutStateName = "common_window_out";
    private const string StandardOutClipPath = "Assets/Content/UI/Prefab/common_window/animation/common_window_out.anim";

    [MenuItem(MenuPath, false, 12)]
    private static void SetupSelectedButton()
    {
        var selected = Selection.activeGameObject;
        var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
        if (selected == null || prefabStage == null || selected.scene != prefabStage.scene)
        {
            ShowError("Open a UI prefab and select the copied close button first.");
            return;
        }

        var prefabRoot = prefabStage.prefabContentsRoot;
        if (prefabRoot == null)
        {
            ShowError("Could not resolve the current prefab root.");
            return;
        }

        var animator = ResolveRootAnimator(prefabRoot);
        if (animator == null)
        {
            ShowError("No Animator was found on the prefab root or its direct 'root' child.");
            return;
        }

        var controller = animator.runtimeAnimatorController as AnimatorController;
        if (controller == null)
        {
            ShowError("The root Animator must use an AnimatorController asset directly.");
            return;
        }

        var controllerPath = AssetDatabase.GetAssetPath(controller);
        var controllerFolder = Path.GetDirectoryName(controllerPath)?.Replace("\\", "/");
        if (string.IsNullOrEmpty(controllerFolder))
        {
            ShowError("Could not resolve the AnimatorController asset folder.");
            return;
        }

        var otherPanels = FindOtherAutoClosePanels(prefabRoot, selected);
        if (otherPanels.Count > 1)
        {
            ShowError("More than one 'auto_close_panel' exists. Keep only one before running this tool.");
            return;
        }

        if (otherPanels.Count == 1 && otherPanels[0].transform.IsChildOf(selected.transform))
        {
            ShowError("The existing 'auto_close_panel' cannot be a child of the selected button.");
            return;
        }

        Undo.IncrementCurrentGroup();
        var undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Setup Auto Close Button");

        try
        {
            EnsureTrigger(selected);

            GameObject autoCloseTarget;
            if (otherPanels.Count == 1)
            {
                autoCloseTarget = otherPanels[0];
                ConfigureExistingBackground(autoCloseTarget, selected, prefabRoot);
            }
            else
            {
                autoCloseTarget = selected;
                RenameObject(selected, AutoClosePanelName);
            }

            ConfigureAutoClose(autoCloseTarget);
            ConfigureAnimatorEvent(animator.gameObject);

            var clipPath = $"{controllerFolder}/{OutStateName}.anim";
            var clip = GetOrCreateOutClip(clipPath, animator.gameObject);
            BindOutState(controller, clip);

            PrefabUtility.RecordPrefabInstancePropertyModifications(selected);
            PrefabUtility.RecordPrefabInstancePropertyModifications(autoCloseTarget);
            PrefabUtility.RecordPrefabInstancePropertyModifications(animator.gameObject);
            EditorSceneManager.MarkSceneDirty(prefabStage.scene);
            AssetDatabase.SaveAssets();

            Selection.activeGameObject = selected;
            SceneView.lastActiveSceneView?.ShowNotification(new GUIContent("Auto close + common_window_out ready"));
            Debug.Log(
                $"Auto close setup ready. Button: {GetHierarchyPath(selected.transform)}, " +
                $"Animator: {GetHierarchyPath(animator.transform)}, Clip: {clipPath}",
                selected);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            ShowError($"Setup failed: {exception.Message}");
        }
        finally
        {
            Undo.CollapseUndoOperations(undoGroup);
        }
    }

    [MenuItem(MenuPath, true)]
    private static bool ValidateSetupSelectedButton()
    {
        var selected = Selection.activeGameObject;
        var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
        return Selection.gameObjects.Length == 1
            && selected != null
            && selected.GetComponent<RectTransform>() != null
            && prefabStage != null
            && selected.scene == prefabStage.scene;
    }

    private static Animator ResolveRootAnimator(GameObject prefabRoot)
    {
        var candidates = new List<Animator>();
        AddAnimatorCandidate(candidates, prefabRoot);

        var rootChild = prefabRoot.transform.Find("root");
        if (rootChild != null)
        {
            AddAnimatorCandidate(candidates, rootChild.gameObject);
        }

        if (candidates.Count == 1)
        {
            return candidates[0];
        }

        if (candidates.Count > 1)
        {
            ShowError("Both the prefab root and its 'root' child have an Animator. Keep only the UI window Animator.");
        }

        return null;
    }

    private static void AddAnimatorCandidate(List<Animator> candidates, GameObject target)
    {
        var animator = target == null ? null : target.GetComponent<Animator>();
        if (animator != null && !candidates.Contains(animator))
        {
            candidates.Add(animator);
        }
    }

    private static List<GameObject> FindOtherAutoClosePanels(GameObject prefabRoot, GameObject selected)
    {
        var results = new List<GameObject>();
        foreach (var transform in prefabRoot.GetComponentsInChildren<Transform>(true))
        {
            if (transform.gameObject != selected
                && string.Equals(transform.name, AutoClosePanelName, StringComparison.Ordinal))
            {
                results.Add(transform.gameObject);
            }
        }

        return results;
    }

    private static void ConfigureExistingBackground(GameObject autoClosePanel, GameObject selected, GameObject prefabRoot)
    {
        RenameObject(selected, GetUniqueChildName(autoClosePanel.transform, AutoCloseButtonName, selected));

        if (!selected.transform.IsChildOf(autoClosePanel.transform))
        {
            Undo.SetTransformParent(selected.transform, autoClosePanel.transform, "Move Close Button Under Auto Close Panel");
        }

        var parentCanvas = autoClosePanel.GetComponentInParent<Canvas>();
        var buttonCanvas = selected.GetComponent<Canvas>();
        var addedCanvas = buttonCanvas == null;
        if (addedCanvas)
        {
            buttonCanvas = Undo.AddComponent<Canvas>(selected);
        }

        Undo.RecordObject(buttonCanvas, "Configure Close Button Canvas");
        if (parentCanvas != null)
        {
            buttonCanvas.sortingLayerID = parentCanvas.sortingLayerID;
        }

        if (addedCanvas || !buttonCanvas.overrideSorting)
        {
            buttonCanvas.sortingOrder = GetHighestSortingOrder(prefabRoot) + 1;
        }

        buttonCanvas.overrideSorting = true;
        EditorUtility.SetDirty(buttonCanvas);

        if (selected.GetComponent<GraphicRaycaster>() == null)
        {
            Undo.AddComponent<GraphicRaycaster>(selected);
        }
    }

    private static int GetHighestSortingOrder(GameObject prefabRoot)
    {
        var highest = 0;
        foreach (var canvas in prefabRoot.GetComponentsInChildren<Canvas>(true))
        {
            if (canvas != null)
            {
                highest = Mathf.Max(highest, canvas.sortingOrder);
            }
        }

        return highest;
    }

    private static void EnsureTrigger(GameObject selected)
    {
        if (selected.GetComponentInChildren<UITrigger>(true) == null)
        {
            Undo.AddComponent<UITrigger>(selected);
        }
    }

    private static void ConfigureAutoClose(GameObject target)
    {
        var autoClose = target.GetComponent<AutoClose>();
        if (autoClose == null)
        {
            autoClose = Undo.AddComponent<AutoClose>(target);
        }

        Undo.RecordObject(autoClose, "Configure Auto Close");
        autoClose.funcCode = 1;
        autoClose.autoHideNode = null;
        EditorUtility.SetDirty(autoClose);
    }

    private static void ConfigureAnimatorEvent(GameObject animatorObject)
    {
        var animatorEvent = animatorObject.GetComponent<AnimatorEventExpand>();
        if (animatorEvent == null)
        {
            animatorEvent = Undo.AddComponent<AnimatorEventExpand>(animatorObject);
        }

        Undo.RecordObject(animatorEvent, "Configure Animator Event Expand");
        animatorEvent.AutoContinue = true;
        animatorEvent.AutoCleanEvent = false;
        animatorEvent.DefaultTimeLen = 1f;
        EditorUtility.SetDirty(animatorEvent);

        if (animatorObject.GetComponent<CanvasGroup>() == null)
        {
            Undo.AddComponent<CanvasGroup>(animatorObject);
        }
    }

    private static AnimationClip GetOrCreateOutClip(string clipPath, GameObject animatorObject)
    {
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
        if (clip == null)
        {
            clip = new AnimationClip { name = OutStateName };
            AssetDatabase.CreateAsset(clip, clipPath);
        }

        clip.name = OutStateName;
        var hasContent = AnimationUtility.GetCurveBindings(clip).Length > 0
            || AnimationUtility.GetObjectReferenceCurveBindings(clip).Length > 0;
        if (!hasContent)
        {
            ApplyStandardOutCurves(clip, animatorObject.transform);
        }

        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = false;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        EditorUtility.SetDirty(clip);
        return clip;
    }

    private static void ApplyStandardOutCurves(AnimationClip clip, Transform animatorRoot)
    {
        var curve = LoadStandardAlphaCurve();
        AnimationUtility.SetEditorCurve(
            clip,
            EditorCurveBinding.FloatCurve(string.Empty, typeof(CanvasGroup), "m_Alpha"),
            CloneCurve(curve));

        var content = FindDescendantByName(animatorRoot, "content");
        if (content != null && content.GetComponent<CanvasGroup>() != null)
        {
            var contentPath = AnimationUtility.CalculateTransformPath(content, animatorRoot);
            AnimationUtility.SetEditorCurve(
                clip,
                EditorCurveBinding.FloatCurve(contentPath, typeof(CanvasGroup), "m_Alpha"),
                CloneCurve(curve));
        }

        clip.frameRate = 60f;
    }

    private static AnimationCurve LoadStandardAlphaCurve()
    {
        var standardClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(StandardOutClipPath);
        if (standardClip != null)
        {
            foreach (var binding in AnimationUtility.GetCurveBindings(standardClip))
            {
                if (binding.type == typeof(CanvasGroup)
                    && binding.propertyName == "m_Alpha"
                    && string.IsNullOrEmpty(binding.path))
                {
                    var sourceCurve = AnimationUtility.GetEditorCurve(standardClip, binding);
                    if (sourceCurve != null)
                    {
                        return sourceCurve;
                    }
                }
            }
        }

        return new AnimationCurve(
            new Keyframe(0f, 1f, 0f, 0f),
            new Keyframe(1f / 6f, 0f, 0f, 0f));
    }

    private static AnimationCurve CloneCurve(AnimationCurve source)
    {
        return new AnimationCurve(source.keys)
        {
            preWrapMode = source.preWrapMode,
            postWrapMode = source.postWrapMode,
        };
    }

    private static Transform FindDescendantByName(Transform root, string name)
    {
        foreach (Transform child in root)
        {
            if (string.Equals(child.name, name, StringComparison.Ordinal))
            {
                return child;
            }

            var nested = FindDescendantByName(child, name);
            if (nested != null)
            {
                return nested;
            }
        }

        return null;
    }

    private static void BindOutState(AnimatorController controller, AnimationClip clip)
    {
        if (controller.layers.Length == 0)
        {
            controller.AddLayer("Base Layer");
        }

        var stateMachine = controller.layers[0].stateMachine;
        var state = FindState(stateMachine, OutStateName);
        if (state == null)
        {
            state = stateMachine.AddState(OutStateName, new Vector3(320f, 220f, 0f));
        }

        Undo.RecordObject(state, "Bind Common Window Out");
        state.name = OutStateName;
        state.motion = clip;
        EditorUtility.SetDirty(state);
        EditorUtility.SetDirty(stateMachine);
        EditorUtility.SetDirty(controller);
    }

    private static AnimatorState FindState(AnimatorStateMachine stateMachine, string stateName)
    {
        foreach (var childState in stateMachine.states)
        {
            if (string.Equals(childState.state.name, stateName, StringComparison.Ordinal))
            {
                return childState.state;
            }
        }

        foreach (var childStateMachine in stateMachine.stateMachines)
        {
            var state = FindState(childStateMachine.stateMachine, stateName);
            if (state != null)
            {
                return state;
            }
        }

        return null;
    }

    private static void RenameObject(GameObject target, string newName)
    {
        if (target.name == newName)
        {
            return;
        }

        Undo.RecordObject(target, "Rename Auto Close Node");
        target.name = newName;
        EditorUtility.SetDirty(target);
    }

    private static string GetUniqueChildName(Transform parent, string baseName, GameObject selected)
    {
        var candidate = baseName;
        var suffix = 1;
        while (HasSiblingNamed(parent, candidate, selected))
        {
            candidate = $"{baseName}_{suffix}";
            suffix++;
        }

        return candidate;
    }

    private static bool HasSiblingNamed(Transform parent, string name, GameObject selected)
    {
        foreach (Transform child in parent)
        {
            if (child.gameObject != selected && string.Equals(child.name, name, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static string GetHierarchyPath(Transform transform)
    {
        var path = transform.name;
        while (transform.parent != null)
        {
            transform = transform.parent;
            path = $"{transform.name}/{path}";
        }

        return path;
    }

    private static void ShowError(string message)
    {
        EditorUtility.DisplayDialog("Setup Auto Close Button", message, "OK");
        Debug.LogWarning(message);
    }
}
