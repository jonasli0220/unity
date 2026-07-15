using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
internal static class PlayModeUIPrefabOpener
{
    private const string MenuPath = "GameObject/UI/打开引用的 UI Prefab";
    private const string UIPrefabRoot = "Assets/Content/UI/Prefab";
    private const string CloneNameSuffix = "(Clone)";

    private static readonly GUIContent OpenButtonContent = new GUIContent();

    static PlayModeUIPrefabOpener()
    {
        Editor.finishedDefaultHeaderGUI -= DrawGameObjectHeader;
        Editor.finishedDefaultHeaderGUI += DrawGameObjectHeader;
    }

    private static void DrawGameObjectHeader(Editor editor)
    {
        if (!EditorApplication.isPlaying ||
            editor == null ||
            editor.targets == null ||
            editor.targets.Length != 1)
        {
            return;
        }

        GameObject target = editor.target as GameObject;
        if (!IsRuntimeUIObject(target))
        {
            return;
        }

        string candidateName = GetDisplayCandidateName(target);
        OpenButtonContent.text = string.IsNullOrEmpty(candidateName)
            ? "打开引用的 UI Prefab"
            : "打开 " + candidateName + ".prefab";
        OpenButtonContent.tooltip =
            "打开当前 Play Mode UI 对象引用的原始 Prefab。";

        GUILayout.Space(2f);
        using (new EditorGUI.DisabledScope(EditorApplication.isCompiling))
        {
            if (GUILayout.Button(OpenButtonContent, GUILayout.Height(22f)))
            {
                OpenSourcePrefab(target);
            }
        }
    }

    [MenuItem(MenuPath, false, 49)]
    private static void OpenSelectedRuntimeUIPrefab(MenuCommand command)
    {
        GameObject target = command.context as GameObject;
        if (target == null)
        {
            target = Selection.activeGameObject;
        }

        OpenSourcePrefab(target);
    }

    [MenuItem(MenuPath, true)]
    private static bool ValidateOpenSelectedRuntimeUIPrefab(MenuCommand command)
    {
        GameObject target = command.context as GameObject;
        if (target == null)
        {
            target = Selection.activeGameObject;
        }

        return EditorApplication.isPlaying && IsRuntimeUIObject(target);
    }

    private static bool IsRuntimeUIObject(GameObject target)
    {
        return target != null &&
               !EditorUtility.IsPersistent(target) &&
               target.scene.IsValid() &&
               target.scene.isLoaded &&
               target.transform is RectTransform;
    }

    private static void OpenSourcePrefab(GameObject target)
    {
        if (!IsRuntimeUIObject(target))
        {
            return;
        }

        string nativePrefabPath = GetNativePrefabPath(target);
        if (!string.IsNullOrEmpty(nativePrefabPath))
        {
            OpenPrefab(nativePrefabPath);
            return;
        }

        List<string> attemptedNames = BuildPrefabCandidateNames(target);
        for (int i = 0; i < attemptedNames.Count; i++)
        {
            List<string> matchingPaths = FindExactPrefabPaths(attemptedNames[i]);
            if (matchingPaths.Count == 1)
            {
                OpenPrefab(matchingPaths[0]);
                return;
            }

            if (matchingPaths.Count > 1)
            {
                ShowPrefabChoiceMenu(matchingPaths);
                return;
            }
        }

        string names = attemptedNames.Count > 0
            ? string.Join("\n", attemptedNames.ToArray())
            : target.name;
        EditorUtility.DisplayDialog(
            "未找到引用的 UI Prefab",
            "已按以下运行时名称精确查找 Prefab：\n" + names +
            "\n\n请在 Hierarchy 里选择更靠近“(Clone)”的 UI 根节点后重试。",
            "知道了");
    }

    private static string GetNativePrefabPath(GameObject target)
    {
        for (Transform current = target.transform; current != null; current = current.parent)
        {
            GameObject source =
                PrefabUtility.GetCorrespondingObjectFromSource(current.gameObject);
            string sourcePath = AssetDatabase.GetAssetPath(source);
            if (IsPrefabAssetPath(sourcePath))
            {
                return sourcePath;
            }

            // Do not walk past the runtime UI root into an unrelated scene/container prefab.
            if (current.name.EndsWith(CloneNameSuffix, StringComparison.Ordinal))
            {
                break;
            }
        }

        return string.Empty;
    }

    private static bool IsPrefabAssetPath(string assetPath)
    {
        return !string.IsNullOrEmpty(assetPath) &&
               assetPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetDisplayCandidateName(GameObject target)
    {
        List<string> candidateNames = BuildPrefabCandidateNames(target);
        return candidateNames.Count > 0 ? candidateNames[0] : string.Empty;
    }

    private static List<string> BuildPrefabCandidateNames(GameObject target)
    {
        List<string> names = new List<string>();
        if (target == null)
        {
            return names;
        }

        for (Transform current = target.transform; current != null; current = current.parent)
        {
            if (!current.name.EndsWith(CloneNameSuffix, StringComparison.Ordinal))
            {
                continue;
            }

            AddUniqueName(names, NormalizeCloneName(current.name));
        }

        if (names.Count == 0)
        {
            AddUniqueName(names, NormalizeCloneName(target.name));
        }

        return names;
    }

    private static void AddUniqueName(List<string> names, string candidateName)
    {
        if (string.IsNullOrEmpty(candidateName))
        {
            return;
        }

        for (int i = 0; i < names.Count; i++)
        {
            if (string.Equals(
                    names[i],
                    candidateName,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        names.Add(candidateName);
    }

    private static string NormalizeCloneName(string objectName)
    {
        if (string.IsNullOrEmpty(objectName))
        {
            return string.Empty;
        }

        string normalizedName = objectName.Trim();
        if (normalizedName.EndsWith(CloneNameSuffix, StringComparison.Ordinal))
        {
            normalizedName = normalizedName
                .Substring(0, normalizedName.Length - CloneNameSuffix.Length)
                .TrimEnd();
        }

        return normalizedName;
    }

    private static List<string> FindExactPrefabPaths(string prefabName)
    {
        List<string> matches = FindExactPrefabPaths(
            prefabName,
            AssetDatabase.IsValidFolder(UIPrefabRoot)
                ? new[] { UIPrefabRoot }
                : null);
        if (matches.Count > 0)
        {
            return matches;
        }

        return FindExactPrefabPaths(prefabName, null);
    }

    private static List<string> FindExactPrefabPaths(
        string prefabName,
        string[] searchFolders)
    {
        string[] guids = searchFolders != null
            ? AssetDatabase.FindAssets("t:Prefab " + prefabName, searchFolders)
            : AssetDatabase.FindAssets("t:Prefab " + prefabName);
        List<string> matches = new List<string>();

        for (int i = 0; i < guids.Length; i++)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(guids[i]);
            if (!IsPrefabAssetPath(assetPath) ||
                !string.Equals(
                    Path.GetFileNameWithoutExtension(assetPath),
                    prefabName,
                    StringComparison.OrdinalIgnoreCase) ||
                matches.Contains(assetPath))
            {
                continue;
            }

            matches.Add(assetPath);
        }

        matches.Sort(StringComparer.OrdinalIgnoreCase);
        return matches;
    }

    private static void ShowPrefabChoiceMenu(List<string> prefabPaths)
    {
        GenericMenu menu = new GenericMenu();
        for (int i = 0; i < prefabPaths.Count; i++)
        {
            string prefabPath = prefabPaths[i];
            string label = prefabPath.StartsWith(
                UIPrefabRoot + "/",
                StringComparison.OrdinalIgnoreCase)
                ? prefabPath.Substring(UIPrefabRoot.Length + 1)
                : prefabPath;

            menu.AddItem(
                new GUIContent(label),
                false,
                () => OpenPrefab(prefabPath));
        }

        menu.ShowAsContext();
    }

    private static void OpenPrefab(string prefabPath)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab == null)
        {
            EditorUtility.DisplayDialog(
                "无法打开 UI Prefab",
                "资源可能已经移动或删除：\n" + prefabPath,
                "知道了");
            return;
        }

        Selection.activeObject = prefab;
        EditorGUIUtility.PingObject(prefab);
        if (!AssetDatabase.OpenAsset(prefab))
        {
            EditorUtility.DisplayDialog(
                "无法打开 UI Prefab",
                "Unity 未能打开：\n" + prefabPath,
                "知道了");
        }
    }
}
