using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class PrefabAnimationSetCreator
{
    private const string MenuPath = "Assets/UI Animation/Create Animation Set For Prefab";

    [MenuItem(MenuPath, false, 2000)]
    private static void CreateForSelectedPrefabs()
    {
        var prefabPaths = Selection.assetGUIDs;
        var createdCount = 0;

        foreach (var guid in prefabPaths)
        {
            var prefabPath = AssetDatabase.GUIDToAssetPath(guid);
            if (!IsPrefabPath(prefabPath))
            {
                continue;
            }

            CreateAnimationSet(prefabPath);
            createdCount++;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"Created or updated animation sets for {createdCount} prefab(s).");
    }

    [MenuItem(MenuPath, true)]
    private static bool ValidateCreateForSelectedPrefabs()
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

    private static void CreateAnimationSet(string prefabPath)
    {
        var prefabName = Path.GetFileNameWithoutExtension(prefabPath);
        var prefabDirectory = Path.GetDirectoryName(prefabPath)?.Replace("\\", "/");
        if (string.IsNullOrEmpty(prefabDirectory))
        {
            Debug.LogWarning($"Could not resolve prefab directory: {prefabPath}");
            return;
        }

        var animationRoot = EnsureFolder(prefabDirectory, "animation");
        var animationFolder = EnsureFolder(animationRoot, prefabName);

        var inClipPath = ResolveClipPath(animationFolder, $"{prefabName}_in");
        var outClipPath = ResolveClipPath(animationFolder, $"{prefabName}_out");
        var controllerPath = ResolveControllerPath(animationFolder, prefabName);

        var inClip = GetOrCreateClip(inClipPath);
        var outClip = GetOrCreateClip(outClipPath);
        var controller = GetOrCreateController(controllerPath, prefabName);

        BindClipsToController(controller, prefabName, inClip, outClip);

        EditorUtility.SetDirty(controller);
        Debug.Log($"Animation set ready: {animationFolder}");
    }

    private static string GetControllerAssetName(string prefabName)
    {
        return $"{prefabName}.controller";
    }

    private static string ResolveClipPath(string animationFolder, string expectedClipName)
    {
        var expectedPath = $"{animationFolder}/{expectedClipName}.anim";
        if (AssetDatabase.LoadAssetAtPath<AnimationClip>(expectedPath) != null)
        {
            return expectedPath;
        }

        var suffix = expectedClipName.EndsWith("_in", System.StringComparison.OrdinalIgnoreCase) ? "_in" : "_out";
        foreach (var guid in AssetDatabase.FindAssets("t:AnimationClip", new[] { animationFolder }))
        {
            var assetPath = AssetDatabase.GUIDToAssetPath(guid);
            if (!IsDirectChildAsset(assetPath, animationFolder))
            {
                continue;
            }

            var assetName = Path.GetFileNameWithoutExtension(assetPath);
            if (assetName.EndsWith(suffix, System.StringComparison.OrdinalIgnoreCase))
            {
                return RenameAsset(assetPath, expectedClipName);
            }
        }

        return expectedPath;
    }

    private static string ResolveControllerPath(string animationFolder, string prefabName)
    {
        var expectedPath = $"{animationFolder}/{GetControllerAssetName(prefabName)}";
        if (AssetDatabase.LoadAssetAtPath<AnimatorController>(expectedPath) != null)
        {
            return expectedPath;
        }

        foreach (var guid in AssetDatabase.FindAssets("t:AnimatorController", new[] { animationFolder }))
        {
            var assetPath = AssetDatabase.GUIDToAssetPath(guid);
            if (!IsDirectChildAsset(assetPath, animationFolder))
            {
                continue;
            }

            return RenameAsset(assetPath, prefabName);
        }

        return expectedPath;
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

    private static bool IsPrefabPath(string assetPath)
    {
        return !string.IsNullOrEmpty(assetPath)
            && assetPath.EndsWith(".prefab", System.StringComparison.OrdinalIgnoreCase);
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
        if (controller.layers.Length == 0)
        {
            controller.AddLayer("Base Layer");
        }

        controller.layers[0].stateMachine.name = controllerName;
        return controller;
    }

    private static void BindClipsToController(
        AnimatorController controller,
        string prefabName,
        AnimationClip inClip,
        AnimationClip outClip)
    {
        var stateMachine = controller.layers[0].stateMachine;
        var inState = GetOrCreateState(stateMachine, $"{prefabName}_in", new Vector3(250, 80, 0));
        var outState = GetOrCreateState(stateMachine, $"{prefabName}_out", new Vector3(250, 180, 0));

        inState.motion = inClip;
        outState.motion = outClip;
        stateMachine.defaultState = inState;

        EditorUtility.SetDirty(stateMachine);
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

}
