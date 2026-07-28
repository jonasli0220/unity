using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class NodeAnimationSetCreator
{
    private const string GameObjectMenuPath = "GameObject/UI Animation/Create Node Animation Set";
    private const string AnimatorEventExpandTypeName = "AnimatorEventExpand";

    [MenuItem(GameObjectMenuPath, false, 11)]
    private static void CreateForSelectedHierarchyObjects()
    {
        var createdCount = 0;
        foreach (var selectedObject in Selection.gameObjects)
        {
            if (CreateForHierarchyObject(selectedObject))
            {
                createdCount++;
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"Created or updated node animation sets for {createdCount} hierarchy object(s).");
    }

    [MenuItem(GameObjectMenuPath, true)]
    private static bool ValidateCreateForSelectedHierarchyObjects()
    {
        return Selection.gameObjects != null && Selection.gameObjects.Length > 0;
    }

    private static bool CreateForHierarchyObject(GameObject selectedObject)
    {
        if (selectedObject == null)
        {
            return false;
        }

        var baseName = GetAnimationBaseName(selectedObject);
        var folderSourcePath = ResolvePrefabAssetPath(selectedObject);
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
        var clipPath = ResolveClipPath(animationFolder, $"{baseName}_in");
        var clip = GetOrCreateClip(clipPath);
        var controllerPath = ResolveControllerPath(animationFolder, baseName);
        var controller = GetOrCreateController(controllerPath, baseName);
        BindClipAsDefaultState(controller, baseName, clip);

        Undo.RegisterFullObjectHierarchyUndo(selectedObject, "Create Node Animation Set");
        ConfigureRuntimeComponents(selectedObject, controller);
        PrefabUtility.RecordPrefabInstancePropertyModifications(selectedObject);
        EditorSceneManager.MarkSceneDirty(selectedObject.scene);

        Debug.Log($"Node animation set ready: {animationFolder}");
        return true;
    }

    private static string GetAnimationBaseName(GameObject target)
    {
        if (target.transform.parent != null && string.Equals(target.name, "root", StringComparison.OrdinalIgnoreCase))
        {
            return target.transform.parent.name;
        }

        return target.name;
    }

    private static void ConfigureRuntimeComponents(GameObject target, AnimatorController controller)
    {
        var animator = target.GetComponent<Animator>();
        if (animator == null)
        {
            animator = target.AddComponent<Animator>();
        }

        animator.runtimeAnimatorController = controller;
        animator.updateMode = AnimatorUpdateMode.UnscaledTime;
        EditorUtility.SetDirty(animator);

        var animatorEventExpand = GetOrAddAnimatorEventExpand(target);
        if (animatorEventExpand == null)
        {
            Debug.LogWarning($"Could not find script type '{AnimatorEventExpandTypeName}'. Animator was assigned, but Animator Event Expand was not added.");
            return;
        }

        ConfigureAnimatorEventExpand(animatorEventExpand);
        EditorUtility.SetDirty(animatorEventExpand);
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

    private static Type FindTypeByName(string typeName)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                var type = assembly.GetType(typeName);
                if (type != null && typeof(Component).IsAssignableFrom(type))
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

    private static void BindClipAsDefaultState(AnimatorController controller, string baseName, AnimationClip clip)
    {
        if (controller.layers.Length == 0)
        {
            controller.AddLayer("Base Layer");
        }

        var stateMachine = controller.layers[0].stateMachine;
        stateMachine.name = baseName;
        var state = GetOrCreateState(stateMachine, $"{baseName}_in", new Vector3(260, 120, 0));
        state.motion = clip;
        stateMachine.defaultState = state;
        EditorUtility.SetDirty(state);
        EditorUtility.SetDirty(stateMachine);
        EditorUtility.SetDirty(controller);
    }

    private static AnimatorState GetOrCreateState(AnimatorStateMachine stateMachine, string stateName, Vector3 position)
    {
        foreach (var childState in stateMachine.states)
        {
            if (childState.state.name == stateName)
            {
                return childState.state;
            }
        }

        return stateMachine.AddState(stateName, position);
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
        var prefabStage = GetCurrentPrefabStage();
        if (prefabStage == null)
        {
            return null;
        }

        var sceneProperty = prefabStage.GetType().GetProperty("scene");
        if (sceneProperty != null)
        {
            var stageScene = sceneProperty.GetValue(prefabStage, null);
            if (stageScene is UnityEngine.SceneManagement.Scene)
            {
                var scene = (UnityEngine.SceneManagement.Scene)stageScene;
                if (scene != selectedObject.scene)
                {
                    return null;
                }
            }
        }

        var assetPathProperty = prefabStage.GetType().GetProperty("assetPath")
            ?? prefabStage.GetType().GetProperty("prefabAssetPath");
        return assetPathProperty == null ? null : assetPathProperty.GetValue(prefabStage, null) as string;
    }

    private static object GetCurrentPrefabStage()
    {
        var utilityType = FindEditorType(
            "UnityEditor.SceneManagement.PrefabStageUtility",
            "UnityEditor.Experimental.SceneManagement.PrefabStageUtility");
        var method = utilityType?.GetMethod("GetCurrentPrefabStage", BindingFlags.Public | BindingFlags.Static);
        return method == null ? null : method.Invoke(null, null);
    }

    private static Type FindEditorType(params string[] typeNames)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            foreach (var typeName in typeNames)
            {
                var type = assembly.GetType(typeName);
                if (type != null)
                {
                    return type;
                }
            }
        }

        return null;
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

    private static string EnsureFolder(string parentFolder, string folderName)
    {
        var folderPath = $"{parentFolder}/{folderName}";
        if (!AssetDatabase.IsValidFolder(folderPath))
        {
            AssetDatabase.CreateFolder(parentFolder, folderName);
        }

        return folderPath;
    }

    private static string ResolveClipPath(string animationFolder, string expectedClipName)
    {
        var expectedPath = $"{animationFolder}/{expectedClipName}.anim";
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
            if (assetName.EndsWith("_in", StringComparison.OrdinalIgnoreCase))
            {
                return RenameAsset(assetPath, expectedClipName);
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
        EditorUtility.SetDirty(clip);
        return clip;
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
}
