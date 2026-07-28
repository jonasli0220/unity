using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class ButtonAnimationSetCreator
{
    private const string AssetMenuPath = "Assets/UI Animation/Create Button Animation Set";
    private const string EmptyAssetMenuPath = "Assets/UI Animation/Create Button Animation Set (Empty)";
    private const string GameObjectMenuPath = "GameObject/UI Animation/Create Button Animation Set";
    private const string EmptyGameObjectMenuPath = "GameObject/UI Animation/Create Button Animation Set (Empty)";
    private const string PressedLayerName = "Second Layer";
    private const string HighlightedLayerName = "Third Layer";
    private const string UiTriggerTypeName = "UITrigger";
    private const string UiToggleTypeName = "UIToggle";
    private const string AnimatorEventExpandTypeName = "AnimatorEventExpand";
    private const string ShaderHelperTypeName = "UIShaderParamHelper_vx_common_shader";
    private const string ButtonGlowMaterialPath = "Assets/Content/UI/Prefab/button/material/common_btn_glow.mat";
    private const float ButtonAnimationKeyTime = 0.083333336f;
    private const float HighlightEmissionGain = 1.75f;
    private const float PressedScale = 0.95f;

    private static readonly ButtonClipSpec[] ClipSpecs =
    {
        new ButtonClipSpec("_highlighted"),
        new ButtonClipSpec("_highlighted_empty"),
        new ButtonClipSpec("_highlightedOut"),
        new ButtonClipSpec("_pressed"),
        new ButtonClipSpec("_pressed_empty"),
        new ButtonClipSpec("_pressedOut"),
    };

    private static readonly string[] ParameterSuffixes =
    {
        "_highlighted",
        "_highlightedOut",
        "_pressed",
        "_pressedOut",
    };

    private static readonly string[] UiTriggerEventPropertyNames =
    {
        "onEnter",
        "onExit",
        "onPointerUp",
        "onPointerDown",
        "onClick",
        "onInitDrag",
        "onBeginDrag",
        "onDrag",
        "onEndDrag",
        "onDrop",
        "onScroll",
        "onSelect",
        "onDeSelect",
        "onUpdateSelected",
        "onMove",
        "onSubmit",
        "onCancel",
        "onDoubleClick",
        "onLongPointerDown",
        "onLongPointerContinueDown",
    };

    [MenuItem(AssetMenuPath, false, 2001)]
    private static void CreateForSelectedPrefabAssets()
    {
        CreateForSelectedPrefabAssets(true);
    }

    [MenuItem(EmptyAssetMenuPath, false, 2002)]
    private static void CreateEmptyForSelectedPrefabAssets()
    {
        CreateForSelectedPrefabAssets(false);
    }

    private static void CreateForSelectedPrefabAssets(bool includeDefaultAnimationContent)
    {
        var createdCount = 0;
        foreach (var guid in Selection.assetGUIDs)
        {
            var prefabPath = AssetDatabase.GUIDToAssetPath(guid);
            if (!IsPrefabPath(prefabPath))
            {
                continue;
            }

            CreateForPrefabAsset(prefabPath, includeDefaultAnimationContent);
            createdCount++;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"Created or updated {GetModeLogName(includeDefaultAnimationContent)} for {createdCount} prefab asset(s).");
    }

    [MenuItem(AssetMenuPath, true)]
    private static bool ValidateCreateForSelectedPrefabAssets()
    {
        foreach (var guid in Selection.assetGUIDs)
        {
            if (IsPrefabPath(AssetDatabase.GUIDToAssetPath(guid)))
            {
                return true;
            }
        }

        return false;
    }

    [MenuItem(EmptyAssetMenuPath, true)]
    private static bool ValidateCreateEmptyForSelectedPrefabAssets()
    {
        return ValidateCreateForSelectedPrefabAssets();
    }

    [MenuItem(GameObjectMenuPath, false, 10)]
    private static void CreateForSelectedHierarchyObjects()
    {
        CreateForSelectedHierarchyObjects(true);
    }

    [MenuItem(EmptyGameObjectMenuPath, false, 11)]
    private static void CreateEmptyForSelectedHierarchyObjects()
    {
        CreateForSelectedHierarchyObjects(false);
    }

    private static void CreateForSelectedHierarchyObjects(bool includeDefaultAnimationContent)
    {
        var createdCount = 0;
        foreach (var selectedObject in Selection.gameObjects)
        {
            if (CreateForHierarchyObject(selectedObject, includeDefaultAnimationContent))
            {
                createdCount++;
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"Created or updated {GetModeLogName(includeDefaultAnimationContent)} for {createdCount} hierarchy object(s).");
    }

    [MenuItem(GameObjectMenuPath, true)]
    private static bool ValidateCreateForSelectedHierarchyObjects()
    {
        return Selection.gameObjects != null && Selection.gameObjects.Length > 0;
    }

    [MenuItem(EmptyGameObjectMenuPath, true)]
    private static bool ValidateCreateEmptyForSelectedHierarchyObjects()
    {
        return ValidateCreateForSelectedHierarchyObjects();
    }


    private static void CreateForPrefabAsset(string prefabPath, bool includeDefaultAnimationContent)
    {
        var prefabRoot = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            var baseName = GetAnimationBaseName(prefabRoot);
            var animationFolder = EnsureAnimationFolder(prefabPath, baseName);
            if (includeDefaultAnimationContent)
            {
                ConfigureHighlightedImageTargets(prefabRoot);
            }

            var controller = CreateOrUpdateButtonController(animationFolder, baseName, prefabRoot, includeDefaultAnimationContent);

            ConfigureRuntimeComponents(prefabRoot, controller, baseName);
            PrefabUtility.SaveAsPrefabAsset(prefabRoot, prefabPath);

            Debug.Log($"Button animation set ready: {animationFolder}");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(prefabRoot);
        }
    }

    private static bool CreateForHierarchyObject(GameObject selectedObject, bool includeDefaultAnimationContent)
    {
        if (selectedObject == null)
        {
            return false;
        }

        var attachTarget = ResolveHierarchyAttachTarget(selectedObject);
        var baseName = GetAnimationBaseName(attachTarget);
        var prefabAssetPath = ResolvePrefabAssetPath(selectedObject);
        var folderSourcePath = prefabAssetPath;
        if (string.IsNullOrEmpty(folderSourcePath))
        {
            folderSourcePath = ResolveSceneAssetPath(selectedObject);
        }

        if (string.IsNullOrEmpty(folderSourcePath))
        {
            Debug.LogWarning($"Could not resolve an asset folder for '{selectedObject.name}'. Save the scene or select a prefab asset/instance first.");
            return false;
        }

        var animationFolder = EnsureAnimationFolder(folderSourcePath, baseName);
        if (includeDefaultAnimationContent)
        {
            ConfigureHighlightedImageTargets(attachTarget);
        }

        var controller = CreateOrUpdateButtonController(animationFolder, baseName, attachTarget, includeDefaultAnimationContent);

        Undo.RegisterFullObjectHierarchyUndo(attachTarget, "Create Button Animation Set");
        if (attachTarget != selectedObject && IsVisualRoot(selectedObject))
        {
            Undo.RegisterFullObjectHierarchyUndo(selectedObject, "Clean Misplaced Button Animation Set");
        }

        ConfigureRuntimeComponents(attachTarget, controller, baseName);
        CleanupMisplacedVisualRootRuntimeComponents(selectedObject, attachTarget, controller, baseName);
        PrefabUtility.RecordPrefabInstancePropertyModifications(attachTarget);
        EditorSceneManager.MarkSceneDirty(attachTarget.scene);

        Debug.Log($"Button animation set ready: {animationFolder}");
        return true;
    }

    private static AnimatorController CreateOrUpdateButtonController(string animationFolder, string baseName, GameObject attachTarget, bool includeDefaultAnimationContent)
    {
        var clips = new ButtonClipSet();
        foreach (var spec in ClipSpecs)
        {
            var clipPath = ResolveAnimationClipPath(animationFolder, baseName, spec.Suffix);
            var clip = GetOrCreateClip(clipPath);
            spec.Apply(clip, clips);
        }

        if (includeDefaultAnimationContent)
        {
            ApplyDefaultButtonAnimationCurves(clips, attachTarget);
        }

        var controllerPath = ResolveControllerPath(animationFolder, baseName);
        var controller = GetOrCreateController(controllerPath, baseName);
        EnsureButtonParameters(controller, baseName);
        ConfigureButtonLayer(
            controller,
            PressedLayerName,
            baseName,
            "_pressed",
            "_pressedOut",
            clips.PressedEmpty,
            clips.Pressed,
            clips.PressedOut);
        ConfigureButtonLayer(
            controller,
            HighlightedLayerName,
            baseName,
            "_highlighted",
            "_highlightedOut",
            clips.HighlightedEmpty,
            clips.Highlighted,
            clips.HighlightedOut);

        EditorUtility.SetDirty(controller);
        return controller;
    }

    private static string GetModeLogName(bool includeDefaultAnimationContent)
    {
        return includeDefaultAnimationContent ? "button animation sets" : "empty button animation sets";
    }

    private static void ConfigureButtonLayer(
        AnimatorController controller,
        string layerName,
        string baseName,
        string activeSuffix,
        string outSuffix,
        AnimationClip emptyClip,
        AnimationClip activeClip,
        AnimationClip outClip)
    {
        var layerIndex = EnsureLayer(controller, layerName);
        var layers = controller.layers;
        var layer = layers[layerIndex];
        layer.defaultWeight = 1f;

        var stateMachine = layer.stateMachine;
        stateMachine.name = layerName;
        stateMachine.entryPosition = new Vector3(80, 220, 0);
        stateMachine.anyStatePosition = new Vector3(80, 80, 0);
        stateMachine.exitPosition = new Vector3(720, 220, 0);

        var emptyState = GetOrCreateStateBySuffix(stateMachine, $"{baseName}{activeSuffix}_empty", $"{activeSuffix}_empty", new Vector3(260, 40, 0));
        var activeState = GetOrCreateStateBySuffix(stateMachine, $"{baseName}{activeSuffix}", activeSuffix, new Vector3(280, 150, 0));
        var outState = GetOrCreateStateBySuffix(stateMachine, $"{baseName}{outSuffix}", outSuffix, new Vector3(280, 260, 0));

        emptyState.motion = emptyClip;
        activeState.motion = activeClip;
        outState.motion = outClip;
        stateMachine.defaultState = emptyState;

        ConfigureAnyStateTransition(stateMachine, activeState, $"{baseName}{activeSuffix}");
        ConfigureAnyStateTransition(stateMachine, outState, $"{baseName}{outSuffix}");

        layers[layerIndex] = layer;
        controller.layers = layers;

        EditorUtility.SetDirty(stateMachine);
        EditorUtility.SetDirty(emptyState);
        EditorUtility.SetDirty(activeState);
        EditorUtility.SetDirty(outState);
    }

    private static void ConfigureHighlightedImageTargets(GameObject attachTarget)
    {
        if (attachTarget == null)
        {
            return;
        }

        var material = LoadButtonGlowMaterial();
        if (material == null)
        {
            Debug.LogWarning($"Could not find material at '{ButtonGlowMaterialPath}'. Highlight shader setup was skipped.");
            return;
        }

        var shaderHelperType = FindTypeByName(ShaderHelperTypeName);
        if (shaderHelperType == null)
        {
            Debug.LogWarning($"Could not find script type '{ShaderHelperTypeName}'. Highlight shader setup was skipped.");
            return;
        }

        foreach (var image in attachTarget.GetComponentsInChildren<UnityEngine.UI.Image>(true))
        {
            if (image == null)
            {
                continue;
            }

            image.material = material;
            EditorUtility.SetDirty(image);

            var shaderHelper = GetOrAddComponentByType(image.gameObject, shaderHelperType);
            if (shaderHelper == null)
            {
                continue;
            }

            ConfigureShaderHelper(shaderHelper, material);
            EditorUtility.SetDirty(shaderHelper);
        }
    }

    private static Material LoadButtonGlowMaterial()
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(ButtonGlowMaterialPath);
        if (material != null)
        {
            return material;
        }

        foreach (var guid in AssetDatabase.FindAssets("common_btn_glow t:Material"))
        {
            var assetPath = AssetDatabase.GUIDToAssetPath(guid);
            if (string.Equals(Path.GetFileNameWithoutExtension(assetPath), "common_btn_glow", StringComparison.OrdinalIgnoreCase))
            {
                return AssetDatabase.LoadAssetAtPath<Material>(assetPath);
            }
        }

        return null;
    }

    private static Component GetOrAddComponentByType(GameObject target, Type componentType)
    {
        if (target == null || componentType == null)
        {
            return null;
        }

        var existing = target.GetComponent(componentType);
        return existing != null ? existing : target.AddComponent(componentType);
    }

    private static void ConfigureShaderHelper(Component shaderHelper, Material material)
    {
        var serializedObject = new SerializedObject(shaderHelper);
        SetObjectReferenceProperty(serializedObject, material, "_UIMaterial");
        SetFloatProperty(serializedObject, 1f, "_EmissionGain");
        serializedObject.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void ApplyDefaultButtonAnimationCurves(ButtonClipSet clips, GameObject attachTarget)
    {
        if (clips == null || attachTarget == null)
        {
            return;
        }

        var shaderHelperType = FindTypeByName(ShaderHelperTypeName);
        var images = attachTarget.GetComponentsInChildren<UnityEngine.UI.Image>(true);
        if (shaderHelperType != null && images.Length > 0)
        {
            ApplyEmissionClip(clips.HighlightedEmpty, attachTarget, images, shaderHelperType, 1f);
            ApplyEmissionClip(clips.Highlighted, attachTarget, images, shaderHelperType, 1f, HighlightEmissionGain);
            ApplyEmissionClip(clips.HighlightedOut, attachTarget, images, shaderHelperType, HighlightEmissionGain, 1f);
        }

        var visualRootPath = GetVisualRootPath(attachTarget);
        ApplyScaleClip(clips.PressedEmpty, visualRootPath, 1f);
        ApplyScaleClip(clips.Pressed, visualRootPath, 1f, PressedScale);
        ApplyScaleClip(clips.PressedOut, visualRootPath, PressedScale, 1f);
    }

    private static void ApplyEmissionClip(AnimationClip clip, GameObject attachTarget, UnityEngine.UI.Image[] images, Type shaderHelperType, params float[] values)
    {
        if (clip == null || attachTarget == null || images == null || shaderHelperType == null || values == null || values.Length == 0)
        {
            return;
        }

        foreach (var image in images)
        {
            if (image == null)
            {
                continue;
            }

            var path = AnimationUtility.CalculateTransformPath(image.transform, attachTarget.transform);
            var binding = EditorCurveBinding.FloatCurve(path, shaderHelperType, "_EmissionGain");
            AnimationUtility.SetEditorCurve(clip, binding, CreateDefaultCurve(values));
        }

        EditorUtility.SetDirty(clip);
    }

    private static void ApplyScaleClip(AnimationClip clip, string path, params float[] values)
    {
        if (clip == null || values == null || values.Length == 0)
        {
            return;
        }

        var curve = CreateDefaultCurve(values);
        AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(RectTransform), "m_LocalScale.x"), curve);
        AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(RectTransform), "m_LocalScale.y"), CreateDefaultCurve(values));
        AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(RectTransform), "m_LocalScale.z"), CreateDefaultCurve(values));
        EditorUtility.SetDirty(clip);
    }

    private static AnimationCurve CreateDefaultCurve(params float[] values)
    {
        if (values.Length == 1)
        {
            return new AnimationCurve(new Keyframe(0f, values[0], 0f, 0f));
        }

        return new AnimationCurve(
            new Keyframe(0f, values[0], 0f, 0f),
            new Keyframe(ButtonAnimationKeyTime, values[1], 0f, 0f));
    }

    private static string GetVisualRootPath(GameObject attachTarget)
    {
        var visualRoot = ResolveVisualRootTransform(attachTarget);
        return AnimationUtility.CalculateTransformPath(visualRoot, attachTarget.transform);
    }

    private static Transform ResolveVisualRootTransform(GameObject attachTarget)
    {
        if (IsVisualRoot(attachTarget))
        {
            return attachTarget.transform;
        }

        foreach (Transform child in attachTarget.transform)
        {
            if (IsVisualRoot(child.gameObject))
            {
                return child;
            }
        }

        return attachTarget.transform;
    }

    private static void ConfigureRuntimeComponents(GameObject attachTarget, AnimatorController controller, string baseName)
    {
        var animator = attachTarget.GetComponent<Animator>();
        if (animator == null)
        {
            animator = attachTarget.AddComponent<Animator>();
        }

        animator.runtimeAnimatorController = controller;
        animator.updateMode = AnimatorUpdateMode.UnscaledTime;

        var animatorEventExpand = GetOrAddAnimatorEventExpand(attachTarget);
        if (animatorEventExpand != null)
        {
            ConfigureAnimatorEventExpand(animatorEventExpand);
            EditorUtility.SetDirty(animatorEventExpand);
        }
        else
        {
            Debug.LogWarning($"Could not find script type '{AnimatorEventExpandTypeName}'. Animator was assigned, but Animator Event Expand was not added.");
        }

        var selectableAnimationTarget = GetOrAddSelectableAnimationTarget(attachTarget);
        if (selectableAnimationTarget == null)
        {
            Debug.LogWarning($"Could not find script type '{UiTriggerTypeName}' or '{UiToggleTypeName}'. Animator was assigned, but selectable animation settings were not updated.");
            EditorUtility.SetDirty(animator);
            return;
        }

        ConfigureSelectableAnimationTarget(selectableAnimationTarget, animator, baseName);
        EditorUtility.SetDirty(selectableAnimationTarget);
        EditorUtility.SetDirty(animator);
    }

    private static Component GetOrAddSelectableAnimationTarget(GameObject target)
    {
        var existing = FindSelectableAnimationTarget(target);
        if (existing != null)
        {
            return existing;
        }

        var type = FindTypeByName(UiTriggerTypeName);
        return type == null ? null : target.AddComponent(type);
    }

    private static Component FindSelectableAnimationTarget(GameObject target)
    {
        var uiToggle = FindComponentByTypeName(target, UiToggleTypeName);
        if (uiToggle != null)
        {
            return uiToggle;
        }

        return FindComponentByTypeName(target, UiTriggerTypeName);
    }

    private static Component FindComponentByTypeName(GameObject target, string typeName)
    {
        foreach (var behaviour in target.GetComponents<MonoBehaviour>())
        {
            if (behaviour != null && behaviour.GetType().Name == typeName)
            {
                return behaviour;
            }
        }

        return null;
    }

    private static void ConfigureSelectableAnimationTarget(Component selectableAnimationTarget, Animator animator, string baseName)
    {
        var serializedObject = new SerializedObject(selectableAnimationTarget);
        SetStringProperty(serializedObject, string.Empty, "sgrNormalTrigger");
        SetStringProperty(serializedObject, $"{baseName}_highlighted", "sgrHighlightedTrigger");
        SetStringProperty(serializedObject, $"{baseName}_pressed", "sgrPressedTrigger");
        SetStringProperty(serializedObject, $"{baseName}_pressedOut", "sgrPressedOutTrigger");
        SetStringProperty(serializedObject, $"{baseName}_highlightedOut", "sgrHighlightedOutTrigger");
        SetObjectReferenceProperty(serializedObject, animator, "uiAnimator");
        serializedObject.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void SetStringProperty(SerializedObject serializedObject, string value, string propertyName)
    {
        var property = serializedObject.FindProperty(propertyName);
        if (property != null)
        {
            property.stringValue = value;
        }
    }

    private static void SetObjectReferenceProperty(SerializedObject serializedObject, UnityEngine.Object value, string propertyName)
    {
        var property = serializedObject.FindProperty(propertyName);
        if (property != null)
        {
            property.objectReferenceValue = value;
        }
    }

    private static Component GetOrAddAnimatorEventExpand(GameObject target)
    {
        var existing = FindComponentByTypeName(target, AnimatorEventExpandTypeName);
        if (existing != null)
        {
            return existing;
        }

        var type = FindTypeByName(AnimatorEventExpandTypeName);
        return type == null ? null : target.AddComponent(type);
    }

    private static void ConfigureAnimatorEventExpand(Component animatorEventExpand)
    {
        var serializedObject = new SerializedObject(animatorEventExpand);
        SetBoolProperty(serializedObject, true, "AutoContinue");
        SetBoolProperty(serializedObject, false, "AutoCleanEvent");
        SetFloatProperty(serializedObject, 1f, "DefaultTimeLen");
        serializedObject.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void SetBoolProperty(SerializedObject serializedObject, bool value, string propertyName)
    {
        var property = serializedObject.FindProperty(propertyName);
        if (property != null)
        {
            property.boolValue = value;
        }
    }

    private static void SetFloatProperty(SerializedObject serializedObject, float value, string propertyName)
    {
        var property = serializedObject.FindProperty(propertyName);
        if (property != null)
        {
            property.floatValue = value;
        }
    }

    private static Type FindTypeByName(string typeName)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type type = null;
            try
            {
                type = assembly.GetType(typeName);
                if (type != null)
                {
                    return type;
                }

                foreach (var assemblyType in assembly.GetTypes())
                {
                    if (assemblyType.Name == typeName && typeof(Component).IsAssignableFrom(assemblyType))
                    {
                        return assemblyType;
                    }
                }
            }
            catch (ReflectionTypeLoadException exception)
            {
                foreach (var assemblyType in exception.Types)
                {
                    if (assemblyType != null && assemblyType.Name == typeName && typeof(Component).IsAssignableFrom(assemblyType))
                    {
                        return assemblyType;
                    }
                }
            }
        }

        return null;
    }

    private static string GetAnimationBaseName(GameObject target)
    {
        if (target.transform.parent != null && target.name == "root")
        {
            return target.transform.parent.name;
        }

        return target.name;
    }

    private static GameObject ResolveHierarchyAttachTarget(GameObject selectedObject)
    {
        if (selectedObject.transform.parent != null && IsVisualRoot(selectedObject))
        {
            return selectedObject;
        }

        var existingButtonTarget = FindAncestorWithButtonRuntimeComponent(selectedObject);
        if (existingButtonTarget != null)
        {
            return existingButtonTarget;
        }

        return selectedObject;
    }

    private static bool IsVisualRoot(GameObject target)
    {
        return target != null && string.Equals(target.name, "root", StringComparison.OrdinalIgnoreCase);
    }

    private static GameObject FindAncestorWithButtonRuntimeComponent(GameObject selectedObject)
    {
        var current = selectedObject.transform;
        while (current != null)
        {
            var currentObject = current.gameObject;
            if (FindSelectableAnimationTarget(currentObject) != null)
            {
                return currentObject;
            }

            current = current.parent;
        }

        return null;
    }

    private static void CleanupMisplacedVisualRootRuntimeComponents(
        GameObject selectedObject,
        GameObject attachTarget,
        AnimatorController controller,
        string baseName)
    {
        if (selectedObject == null || selectedObject == attachTarget || !IsVisualRoot(selectedObject))
        {
            return;
        }

        var selectableAnimationTarget = FindSelectableAnimationTarget(selectedObject);
        if (selectableAnimationTarget != null && IsToolConfiguredSelectableAnimationTarget(selectableAnimationTarget, baseName) && !HasPersistentSelectableAnimationTargetCalls(selectableAnimationTarget))
        {
            Undo.DestroyObjectImmediate(selectableAnimationTarget);
        }

        var animator = selectedObject.GetComponent<Animator>();
        if (animator != null && (animator.runtimeAnimatorController == null || animator.runtimeAnimatorController == controller))
        {
            Undo.DestroyObjectImmediate(animator);
        }
    }

    private static string ResolvePrefabAssetPath(GameObject selectedObject)
    {
        var prefabStagePath = ResolveCurrentPrefabStageAssetPath(selectedObject);
        if (!string.IsNullOrEmpty(prefabStagePath))
        {
            return prefabStagePath;
        }

        var prefabRoot = PrefabUtility.GetNearestPrefabInstanceRoot(selectedObject);
        if (prefabRoot == null)
        {
            return null;
        }

        var source = PrefabUtility.GetCorrespondingObjectFromSource(prefabRoot);
        return source == null ? null : AssetDatabase.GetAssetPath(source);
    }

    private static string ResolveCurrentPrefabStageAssetPath(GameObject selectedObject)
    {
        var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
        if (prefabStage == null || selectedObject == null)
        {
            return null;
        }

        if (selectedObject.scene.handle != prefabStage.scene.handle)
        {
            return null;
        }

        return string.IsNullOrEmpty(prefabStage.assetPath) ? null : prefabStage.assetPath;
    }

    private static string ResolveSceneAssetPath(GameObject selectedObject)
    {
        var scenePath = selectedObject.scene.path;
        return string.IsNullOrEmpty(scenePath) ? null : scenePath;
    }

    private static string EnsureAnimationFolder(string sourceAssetPath, string baseName)
    {
        var sourceDirectory = Path.GetDirectoryName(sourceAssetPath)?.Replace("\\", "/");
        if (string.IsNullOrEmpty(sourceDirectory))
        {
            sourceDirectory = "Assets";
        }

        var animationRoot = EnsureFolder(sourceDirectory, "animation");
        return EnsureFolder(animationRoot, baseName);
    }

    private static string ResolveAnimationClipPath(string animationFolder, string baseName, string suffix)
    {
        var expectedName = $"{baseName}{suffix}";
        var expectedPath = $"{animationFolder}/{expectedName}.anim";
        if (AssetDatabase.LoadAssetAtPath<AnimationClip>(expectedPath) != null)
        {
            return expectedPath;
        }

        foreach (var guid in AssetDatabase.FindAssets("t:AnimationClip", new[] { animationFolder }))
        {
            var assetPath = AssetDatabase.GUIDToAssetPath(guid);
            if (!IsDirectChildAsset(assetPath, animationFolder))
            {
                continue;
            }

            var assetName = Path.GetFileNameWithoutExtension(assetPath);
            if (assetName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return RenameAsset(assetPath, expectedName);
            }
        }

        return expectedPath;
    }

    private static string ResolveControllerPath(string animationFolder, string baseName)
    {
        var expectedPath = $"{animationFolder}/{baseName}.controller";
        if (AssetDatabase.LoadAssetAtPath<AnimatorController>(expectedPath) != null)
        {
            return expectedPath;
        }

        foreach (var guid in AssetDatabase.FindAssets("t:AnimatorController", new[] { animationFolder }))
        {
            var assetPath = AssetDatabase.GUIDToAssetPath(guid);
            if (IsDirectChildAsset(assetPath, animationFolder))
            {
                return RenameAsset(assetPath, baseName);
            }
        }

        return expectedPath;
    }

    private static AnimationClip GetOrCreateClip(string clipPath)
    {
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
        if (clip == null)
        {
            clip = new AnimationClip();
            AssetDatabase.CreateAsset(clip, clipPath);
        }

        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = false;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        RemoveEmptyAnimationEvents(clip);
        EditorUtility.SetDirty(clip);
        return clip;
    }

    private static void RemoveEmptyAnimationEvents(AnimationClip clip)
    {
        var events = AnimationUtility.GetAnimationEvents(clip);
        if (events == null || events.Length == 0)
        {
            return;
        }

        var validEventCount = 0;
        foreach (var animationEvent in events)
        {
            if (!string.IsNullOrEmpty(animationEvent.functionName))
            {
                validEventCount++;
            }
        }

        if (validEventCount == events.Length)
        {
            return;
        }

        var cleanedEvents = new AnimationEvent[validEventCount];
        var nextIndex = 0;
        foreach (var animationEvent in events)
        {
            if (!string.IsNullOrEmpty(animationEvent.functionName))
            {
                cleanedEvents[nextIndex] = animationEvent;
                nextIndex++;
            }
        }

        AnimationUtility.SetAnimationEvents(clip, cleanedEvents);
    }

    private static AnimatorController GetOrCreateController(string controllerPath, string controllerName)
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
        if (controller == null)
        {
            controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
        }

        controller.name = controllerName;
        return controller;
    }

    private static void EnsureButtonParameters(AnimatorController controller, string baseName)
    {
        var expectedParameters = new string[ParameterSuffixes.Length];
        for (var i = 0; i < ParameterSuffixes.Length; i++)
        {
            expectedParameters[i] = $"{baseName}{ParameterSuffixes[i]}";
        }

        foreach (var parameter in controller.parameters)
        {
            if (IsButtonParameter(parameter.name) && !Contains(expectedParameters, parameter.name))
            {
                controller.RemoveParameter(parameter);
            }
        }

        foreach (var parameterName in expectedParameters)
        {
            if (!HasParameter(controller, parameterName))
            {
                controller.AddParameter(parameterName, AnimatorControllerParameterType.Trigger);
            }
        }
    }

    private static bool IsButtonParameter(string parameterName)
    {
        foreach (var suffix in ParameterSuffixes)
        {
            if (HasParameterSuffix(parameterName, suffix))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasParameterSuffix(string parameterName, string suffix)
    {
        var suffixIndex = parameterName.LastIndexOf(suffix, StringComparison.OrdinalIgnoreCase);
        if (suffixIndex < 0)
        {
            return false;
        }

        var tailIndex = suffixIndex + suffix.Length;
        if (tailIndex == parameterName.Length)
        {
            return true;
        }

        if (parameterName[tailIndex] != ' ')
        {
            return false;
        }

        for (var i = tailIndex + 1; i < parameterName.Length; i++)
        {
            if (!char.IsDigit(parameterName[i]))
            {
                return false;
            }
        }

        return tailIndex + 1 < parameterName.Length;
    }

    private static bool IsToolConfiguredSelectableAnimationTarget(Component selectableAnimationTarget, string baseName)
    {
        var serializedObject = new SerializedObject(selectableAnimationTarget);
        return StringPropertyEquals(serializedObject, "sgrNormalTrigger", string.Empty)
            && StringPropertyEquals(serializedObject, "sgrHighlightedTrigger", $"{baseName}_highlighted")
            && StringPropertyEquals(serializedObject, "sgrPressedTrigger", $"{baseName}_pressed")
            && StringPropertyEquals(serializedObject, "sgrPressedOutTrigger", $"{baseName}_pressedOut")
            && StringPropertyEquals(serializedObject, "sgrHighlightedOutTrigger", $"{baseName}_highlightedOut");
    }

    private static bool StringPropertyEquals(SerializedObject serializedObject, string propertyName, string expectedValue)
    {
        var property = serializedObject.FindProperty(propertyName);
        return property != null && property.stringValue == expectedValue;
    }

    private static bool HasPersistentSelectableAnimationTargetCalls(Component selectableAnimationTarget)
    {
        var serializedObject = new SerializedObject(selectableAnimationTarget);
        foreach (var propertyName in UiTriggerEventPropertyNames)
        {
            var eventProperty = serializedObject.FindProperty(propertyName);
            if (eventProperty == null)
            {
                continue;
            }

            var persistentCalls = eventProperty.FindPropertyRelative("m_PersistentCalls");
            if (persistentCalls == null)
            {
                return true;
            }

            var calls = persistentCalls.FindPropertyRelative("m_Calls");
            if (calls == null)
            {
                return true;
            }

            if (calls.arraySize > 0)
            {
                return true;
            }
        }

        return false;
    }

    private static int EnsureLayer(AnimatorController controller, string layerName)
    {
        for (var i = 0; i < controller.layers.Length; i++)
        {
            if (controller.layers[i].name == layerName)
            {
                return i;
            }
        }

        controller.AddLayer(layerName);
        return controller.layers.Length - 1;
    }

    private static AnimatorState GetOrCreateStateBySuffix(AnimatorStateMachine stateMachine, string expectedName, string suffix, Vector3 position)
    {
        foreach (var childState in stateMachine.states)
        {
            if (childState.state.name == expectedName)
            {
                return childState.state;
            }
        }

        foreach (var childState in stateMachine.states)
        {
            if (childState.state.name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                childState.state.name = expectedName;
                return childState.state;
            }
        }

        return stateMachine.AddState(expectedName, position);
    }

    private static void ConfigureAnyStateTransition(AnimatorStateMachine stateMachine, AnimatorState destinationState, string parameterName)
    {
        foreach (var transition in stateMachine.anyStateTransitions)
        {
            if (transition.destinationState == destinationState)
            {
                stateMachine.RemoveAnyStateTransition(transition);
            }
        }

        var newTransition = stateMachine.AddAnyStateTransition(destinationState);
        newTransition.hasExitTime = false;
        newTransition.exitTime = 0.75f;
        newTransition.duration = 0f;
        newTransition.offset = 0f;
        newTransition.canTransitionToSelf = true;
        ClearConditions(newTransition);
        newTransition.AddCondition(AnimatorConditionMode.If, 0f, parameterName);
        EditorUtility.SetDirty(newTransition);
    }

    private static void ClearConditions(AnimatorStateTransition transition)
    {
        while (transition.conditions.Length > 0)
        {
            transition.RemoveCondition(transition.conditions[0]);
        }
    }

    private static bool HasParameter(AnimatorController controller, string parameterName)
    {
        foreach (var parameter in controller.parameters)
        {
            if (parameter.name == parameterName)
            {
                return true;
            }
        }

        return false;
    }

    private static bool Contains(string[] values, string target)
    {
        foreach (var value in values)
        {
            if (value == target)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsPrefabPath(string assetPath)
    {
        return !string.IsNullOrEmpty(assetPath)
            && assetPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase);
    }

    private static string EnsureFolder(string parentFolder, string folderName)
    {
        var folderPath = $"{parentFolder}/{folderName}";
        if (!AssetDatabase.IsValidFolder(folderPath))
        {
            AssetDatabase.CreateFolder(parentFolder, folderName);
        }

        return folderPath;
    }

    private static string RenameAsset(string assetPath, string newName)
    {
        var error = AssetDatabase.RenameAsset(assetPath, newName);
        if (!string.IsNullOrEmpty(error))
        {
            Debug.LogWarning($"Could not rename asset '{assetPath}' to '{newName}': {error}");
            return assetPath;
        }

        var directory = Path.GetDirectoryName(assetPath)?.Replace("\\", "/");
        var extension = Path.GetExtension(assetPath);
        return $"{directory}/{newName}{extension}";
    }

    private static bool IsDirectChildAsset(string assetPath, string folderPath)
    {
        var directory = Path.GetDirectoryName(assetPath)?.Replace("\\", "/");
        return directory == folderPath;
    }

    private struct ButtonClipSpec
    {
        public ButtonClipSpec(string suffix)
        {
            Suffix = suffix;
        }

        public string Suffix;

        public void Apply(AnimationClip clip, ButtonClipSet clips)
        {
            switch (Suffix)
            {
                case "_highlighted":
                    clips.Highlighted = clip;
                    break;
                case "_highlighted_empty":
                    clips.HighlightedEmpty = clip;
                    break;
                case "_highlightedOut":
                    clips.HighlightedOut = clip;
                    break;
                case "_pressed":
                    clips.Pressed = clip;
                    break;
                case "_pressed_empty":
                    clips.PressedEmpty = clip;
                    break;
                case "_pressedOut":
                    clips.PressedOut = clip;
                    break;
            }
        }
    }

    private sealed class ButtonClipSet
    {
        public AnimationClip Highlighted;
        public AnimationClip HighlightedEmpty;
        public AnimationClip HighlightedOut;
        public AnimationClip Pressed;
        public AnimationClip PressedEmpty;
        public AnimationClip PressedOut;
    }
}
