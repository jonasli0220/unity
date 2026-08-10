using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
internal static class PlayModePrefabSaver
{
    private const string ToolMenuRoot = "UITools/运行模式/";
    private const string SaveMenuPath = ToolMenuRoot + "保存当前 Prefab 手动修改";
    private const string RestoreMenuPath = ToolMenuRoot + "撤销上次 Prefab 保存";
    private const string ContextSaveMenuPath = "GameObject/UI/保存运行模式 Prefab 修改";
    private const string UIPrefabRoot = "Assets/Content/UI/Prefab";
    private const string CloneSuffix = "(Clone)";
    private const string BackupRelativeRoot =
        "Library/Dragon/PlayModePrefabSaver/Backups";
    private const string LatestBackupRelativePath =
        "Library/Dragon/PlayModePrefabSaver/latest_backup.json";

    private static readonly Dictionary<string, PlayModePrefabRecordedChange>
        Changes = new Dictionary<string, PlayModePrefabRecordedChange>();

    private static bool suppressRecording;

    static PlayModePrefabSaver()
    {
        Undo.postprocessModifications -= OnPostprocessModifications;
        Undo.postprocessModifications += OnPostprocessModifications;
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        AssemblyReloadEvents.beforeAssemblyReload -= ClearChanges;
        AssemblyReloadEvents.beforeAssemblyReload += ClearChanges;
    }

    /// <summary>
    /// Called reflectively by PlayModeUISelector so that the selector still
    /// compiles and works when this optional extension is not installed.
    /// </summary>
    internal static void DrawInlineHeaderButton(GameObject target)
    {
        int count = GetPendingCount(target);
        GUIContent content = count > 0
            ? new GUIContent(
                string.Format("保存修改 ({0})", count),
                "只把当前运行时 Prefab 实例中手动修改过的属性同步回源 Prefab。")
            : new GUIContent(
                "保存修改",
                "尚未记录到 Inspector 或 Scene 手柄产生的手动修改。");

        using (new EditorGUI.DisabledScope(
                   count == 0 || !CanOperate(target)))
        {
            if (GUILayout.Button(
                    content,
                    GUILayout.Width(count > 0 ? 112f : 88f),
                    GUILayout.Height(22f)))
            {
                SaveForTarget(target);
            }
        }
    }

    [MenuItem(SaveMenuPath, false, 1981)]
    private static void SaveSelectedFromMainMenu()
    {
        SaveForTarget(Selection.activeGameObject);
    }

    [MenuItem(SaveMenuPath, true)]
    private static bool ValidateSaveSelectedFromMainMenu()
    {
        return CanOperate(Selection.activeGameObject)
            && GetPendingCount(Selection.activeGameObject) > 0;
    }

    [MenuItem(ContextSaveMenuPath, false, 51)]
    private static void SaveSelectedFromContext(MenuCommand command)
    {
        GameObject target = command.context as GameObject;
        SaveForTarget(target != null ? target : Selection.activeGameObject);
    }

    [MenuItem(ContextSaveMenuPath, true)]
    private static bool ValidateSaveSelectedFromContext(MenuCommand command)
    {
        GameObject target = command.context as GameObject;
        target = target != null ? target : Selection.activeGameObject;
        return CanOperate(target) && GetPendingCount(target) > 0;
    }

    [MenuItem(RestoreMenuPath, false, 1982)]
    private static void RestoreLatestBackupFromMenu()
    {
        PlayModePrefabBackupManifest manifest;
        string error;
        if (!TryReadLatestBackup(out manifest, out error))
        {
            EditorUtility.DisplayDialog("没有可恢复的 Prefab 备份", error, "知道了");
            return;
        }

        if (IsPrefabOpenInPrefabMode(manifest.assetPath))
        {
            EditorUtility.DisplayDialog(
                "源 Prefab 正在 Prefab Mode 中打开",
                "请先保存并关闭该 Prefab Stage，再恢复自动备份。\n\n" +
                manifest.assetPath,
                "知道了");
            return;
        }

        if (!EditorUtility.DisplayDialog(
                "撤销上次 Prefab 保存",
                "将把源 Prefab 恢复到保存前的自动备份。\n\n" +
                manifest.assetPath + "\n备份时间：" + manifest.createdAt,
                "恢复备份",
                "取消"))
        {
            return;
        }

        try
        {
            suppressRecording = true;
            RestoreBackup(manifest);
            GameObject prefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(manifest.assetPath);
            if (prefab != null)
            {
                EditorGUIUtility.PingObject(prefab);
            }

            Debug.Log(
                "[PlayModePrefabSaver] 已恢复上次保存前的备份：" +
                manifest.assetPath);
            Notify("已恢复 Prefab 备份");
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorUtility.DisplayDialog(
                "恢复 Prefab 失败",
                exception.Message,
                "知道了");
        }
        finally
        {
            suppressRecording = false;
        }
    }

    [MenuItem(RestoreMenuPath, true)]
    private static bool ValidateRestoreLatestBackupFromMenu()
    {
        PlayModePrefabBackupManifest manifest;
        string error;
        return !EditorApplication.isCompiling
            && !EditorApplication.isUpdating
            && TryReadLatestBackup(out manifest, out error);
    }

    private static UndoPropertyModification[] OnPostprocessModifications(
        UndoPropertyModification[] modifications)
    {
        if (suppressRecording
            || !EditorApplication.isPlaying
            || EditorApplication.isPlayingOrWillChangePlaymode
                && !EditorApplication.isPlaying
            || EditorApplication.isCompiling
            || EditorApplication.isUpdating
            || modifications == null)
        {
            return modifications;
        }

        bool changed = false;
        for (int i = 0; i < modifications.Length; i++)
        {
            PropertyModification current = modifications[i].currentValue;
            if (current == null || TryRecord(current))
            {
                changed |= current != null;
            }
        }

        if (changed)
        {
            RepaintInspectors();
        }

        return modifications;
    }

    private static bool TryRecord(PropertyModification modification)
    {
        UnityEngine.Object targetObject = modification.target;
        Component component = targetObject as Component;
        GameObject node = targetObject as GameObject;
        if (node == null && component != null)
        {
            node = component.gameObject;
        }

        if (!IsRuntimeUIObject(node)
            || string.IsNullOrEmpty(modification.propertyPath)
            || ShouldSkipPropertyPath(modification.propertyPath))
        {
            return false;
        }

        GameObject runtimeRoot = FindTrackingRoot(node);
        if (runtimeRoot == null)
        {
            return false;
        }

        SerializedObject serializedObject;
        SerializedProperty property;
        try
        {
            serializedObject = new SerializedObject(targetObject);
            serializedObject.UpdateIfRequiredOrScript();
            property = serializedObject.FindProperty(modification.propertyPath);
        }
        catch
        {
            return false;
        }

        PlayModePrefabPropertyValue value;
        if (property == null
            || !PlayModePrefabPropertyValue.TryCapture(property, out value))
        {
            return false;
        }

        PlayModePrefabNodeLocator locator =
            PlayModePrefabNodeLocator.Create(
                runtimeRoot.transform,
                node.transform);
        if (locator == null)
        {
            return false;
        }

        int componentIndex = -1;
        Type componentType = null;
        if (component != null)
        {
            componentType = component.GetType();
            Component[] siblings = node.GetComponents(componentType);
            componentIndex = Array.IndexOf(siblings, component);
            if (componentIndex < 0)
            {
                return false;
            }
        }

        int rootId = runtimeRoot.GetInstanceID();
        string key = BuildChangeKey(
            rootId,
            locator,
            componentType,
            componentIndex,
            modification.propertyPath);
        Changes[key] = new PlayModePrefabRecordedChange
        {
            Key = key,
            RuntimeRootInstanceId = rootId,
            RuntimeRootName = runtimeRoot.name,
            Locator = locator,
            NodeDisplayPath = locator.GetDisplayPath(runtimeRoot.name),
            ComponentType = componentType,
            ComponentIndex = componentIndex,
            ComponentDisplayName = componentType == null
                ? "GameObject"
                : ObjectNames.NicifyVariableName(componentType.Name),
            PropertyPath = modification.propertyPath,
            PropertyDisplayName = property.displayName,
            Value = value
        };
        return true;
    }

    private static string BuildChangeKey(
        int rootId,
        PlayModePrefabNodeLocator locator,
        Type componentType,
        int componentIndex,
        string propertyPath)
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            "{0}|{1}|{2}|{3}|{4}",
            rootId,
            locator.StableKey,
            componentType != null ? componentType.AssemblyQualifiedName : "GameObject",
            componentIndex,
            propertyPath);
    }

    private static bool ShouldSkipPropertyPath(string path)
    {
        return path == "m_Name"
            || path == "m_Script"
            || path == "m_Father"
            || path == "m_RootOrder"
            || path == "m_Children"
            || path.EndsWith(".Array.size", StringComparison.Ordinal)
            || path.IndexOf("m_Prefab", StringComparison.Ordinal) >= 0
            || path.IndexOf("m_CorrespondingSourceObject", StringComparison.Ordinal) >= 0
            || path.IndexOf("m_PrefabInstance", StringComparison.Ordinal) >= 0
            || path.IndexOf("m_PrefabAsset", StringComparison.Ordinal) >= 0
            || path.IndexOf("m_HideFlags", StringComparison.Ordinal) >= 0;
    }

    private static int GetPendingCount(GameObject target)
    {
        GameObject root = FindTrackingRoot(target);
        if (root == null)
        {
            return 0;
        }

        int rootId = root.GetInstanceID();
        return Changes.Values.Count(
            change => change.RuntimeRootInstanceId == rootId);
    }

    private static void SaveForTarget(GameObject target)
    {
        if (!CanOperate(target))
        {
            EditorUtility.DisplayDialog(
                "无法保存运行模式 Prefab 修改",
                "请在稳定的 Play Mode 中选中一个运行时 UI 节点后再试。",
                "知道了");
            return;
        }

        GameObject runtimeRoot = FindTrackingRoot(target);
        if (runtimeRoot == null)
        {
            EditorUtility.DisplayDialog(
                "未找到运行时 Prefab 根节点",
                "当前节点没有可安全定位的 Prefab 实例或 `(Clone)` 根节点。",
                "知道了");
            return;
        }

        List<PlayModePrefabRecordedChange> records = GetChanges(runtimeRoot);
        if (records.Count == 0)
        {
            Notify("当前 Prefab 没有待保存的手动修改");
            return;
        }

        ResolveSourcePrefab(
            target,
            prefabPath => SaveResolved(runtimeRoot, prefabPath, records));
    }

    private static void SaveResolved(
        GameObject runtimeRoot,
        string prefabPath,
        List<PlayModePrefabRecordedChange> records)
    {
        if (runtimeRoot == null || !EditorApplication.isPlaying)
        {
            EditorUtility.DisplayDialog(
                "运行时对象已失效",
                "当前 Prefab 实例已经不存在，请重新操作。",
                "知道了");
            return;
        }

        if (IsPrefabOpenInPrefabMode(prefabPath))
        {
            EditorUtility.DisplayDialog(
                "源 Prefab 正在 Prefab Mode 中打开",
                "请先保存并关闭该 Prefab Stage，再保存运行时修改。\n\n" +
                prefabPath,
                "知道了");
            return;
        }

        PlayModePrefabBackupManifest backup = null;
        try
        {
            suppressRecording = true;

            string validationError;
            int changedCount = ValidateRecords(
                runtimeRoot,
                prefabPath,
                records,
                out validationError);
            if (!string.IsNullOrEmpty(validationError))
            {
                throw new InvalidOperationException(validationError);
            }

            if (changedCount == 0)
            {
                RemoveChanges(records);
                Notify("记录值已与源 Prefab 一致，无需写入");
                Debug.Log(
                    "[PlayModePrefabSaver] 记录值已与源 Prefab 一致：" +
                    prefabPath);
                return;
            }

            backup = CreateBackup(prefabPath);
            ApplyRecords(runtimeRoot, prefabPath, records);

            string verifyError = VerifyRecords(
                runtimeRoot,
                prefabPath,
                records);
            if (!string.IsNullOrEmpty(verifyError))
            {
                throw new InvalidOperationException(verifyError);
            }

            WriteLatestBackup(backup);
            RemoveChanges(records);

            GameObject prefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab != null)
            {
                EditorGUIUtility.PingObject(prefab);
            }

            string message = string.Format(
                "已保存 {0} 项手动修改",
                changedCount);
            Notify(message);
            Debug.Log(
                string.Format(
                    "[PlayModePrefabSaver] {0}，源 Prefab：{1}",
                    message,
                    prefabPath));
        }
        catch (Exception exception)
        {
            string rollbackMessage = string.Empty;
            if (backup != null)
            {
                try
                {
                    RestoreBackup(backup);
                    rollbackMessage =
                        "\n\n已自动恢复保存前备份，源 Prefab 保持原样。";
                }
                catch (Exception rollbackException)
                {
                    rollbackMessage =
                        "\n\n自动恢复失败，请从以下备份手动恢复：\n" +
                        backup.backupPath + "\n" + rollbackException.Message;
                }
            }

            Debug.LogException(exception);
            EditorUtility.DisplayDialog(
                "保存 Prefab 修改失败",
                exception.Message + rollbackMessage,
                "知道了");
        }
        finally
        {
            suppressRecording = false;
            RepaintInspectors();
        }
    }

    private static int ValidateRecords(
        GameObject runtimeRoot,
        string prefabPath,
        IList<PlayModePrefabRecordedChange> records,
        out string error)
    {
        GameObject sourceRoot = PrefabUtility.LoadPrefabContents(prefabPath);
        if (sourceRoot == null)
        {
            error = "Unity 无法载入源 Prefab：" + prefabPath;
            return 0;
        }

        int changedCount = 0;
        try
        {
            for (int i = 0; i < records.Count; i++)
            {
                SerializedObject sourceObject;
                SerializedProperty sourceProperty;
                if (!TryGetSourceProperty(
                        sourceRoot,
                        records[i],
                        out sourceObject,
                        out sourceProperty,
                        out error))
                {
                    return 0;
                }

                bool matches;
                if (!records[i].Value.TryMatches(
                        sourceProperty,
                        runtimeRoot,
                        sourceRoot,
                        out matches,
                        out error))
                {
                    error = FormatRecordError(records[i], error);
                    return 0;
                }

                if (!matches)
                {
                    changedCount++;
                }
            }
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(sourceRoot);
        }

        error = string.Empty;
        return changedCount;
    }

    private static void ApplyRecords(
        GameObject runtimeRoot,
        string prefabPath,
        IList<PlayModePrefabRecordedChange> records)
    {
        GameObject sourceRoot = PrefabUtility.LoadPrefabContents(prefabPath);
        if (sourceRoot == null)
        {
            throw new InvalidOperationException(
                "Unity 无法载入源 Prefab：" + prefabPath);
        }

        try
        {
            for (int i = 0; i < records.Count; i++)
            {
                SerializedObject sourceObject;
                SerializedProperty sourceProperty;
                string error;
                if (!TryGetSourceProperty(
                        sourceRoot,
                        records[i],
                        out sourceObject,
                        out sourceProperty,
                        out error)
                    || !records[i].Value.TryApply(
                        sourceProperty,
                        runtimeRoot,
                        sourceRoot,
                        out error))
                {
                    throw new InvalidOperationException(
                        FormatRecordError(records[i], error));
                }

                if (!sourceObject.ApplyModifiedPropertiesWithoutUndo())
                {
                    // Unity returns false when the assigned value was already
                    // equal. Validation has already established the target.
                    sourceObject.UpdateIfRequiredOrScript();
                }
            }

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(
                sourceRoot,
                prefabPath);
            if (saved == null)
            {
                throw new InvalidOperationException(
                    "Unity 没有成功写入源 Prefab：" + prefabPath);
            }
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(sourceRoot);
        }

        AssetDatabase.ImportAsset(
            prefabPath,
            ImportAssetOptions.ForceUpdate);
    }

    private static string VerifyRecords(
        GameObject runtimeRoot,
        string prefabPath,
        IList<PlayModePrefabRecordedChange> records)
    {
        GameObject sourceRoot = PrefabUtility.LoadPrefabContents(prefabPath);
        if (sourceRoot == null)
        {
            return "保存后无法重新载入源 Prefab。";
        }

        try
        {
            for (int i = 0; i < records.Count; i++)
            {
                SerializedObject sourceObject;
                SerializedProperty sourceProperty;
                string error;
                if (!TryGetSourceProperty(
                        sourceRoot,
                        records[i],
                        out sourceObject,
                        out sourceProperty,
                        out error))
                {
                    return FormatRecordError(records[i], error);
                }

                bool matches;
                if (!records[i].Value.TryMatches(
                        sourceProperty,
                        runtimeRoot,
                        sourceRoot,
                        out matches,
                        out error))
                {
                    return FormatRecordError(records[i], error);
                }

                if (!matches)
                {
                    return FormatRecordError(
                        records[i],
                        "保存后回读值不一致");
                }
            }
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(sourceRoot);
        }

        return string.Empty;
    }

    private static bool TryGetSourceProperty(
        GameObject sourceRoot,
        PlayModePrefabRecordedChange record,
        out SerializedObject serializedObject,
        out SerializedProperty property,
        out string error)
    {
        serializedObject = null;
        property = null;
        Transform sourceNode = record.Locator.Resolve(sourceRoot.transform);
        if (sourceNode == null)
        {
            error = "源 Prefab 中找不到节点";
            return false;
        }

        UnityEngine.Object sourceTarget = sourceNode.gameObject;
        if (record.ComponentType != null)
        {
            Component[] components =
                sourceNode.GetComponents(record.ComponentType);
            if (record.ComponentIndex < 0
                || record.ComponentIndex >= components.Length)
            {
                error = "源 Prefab 中找不到对应组件";
                return false;
            }

            sourceTarget = components[record.ComponentIndex];
        }

        try
        {
            serializedObject = new SerializedObject(sourceTarget);
            serializedObject.UpdateIfRequiredOrScript();
            property = serializedObject.FindProperty(record.PropertyPath);
        }
        catch (Exception exception)
        {
            error = "读取序列化属性失败：" + exception.Message;
            return false;
        }

        if (property == null)
        {
            error = "源 Prefab 中找不到属性 " + record.PropertyPath;
            return false;
        }

        if (property.propertyType != record.Value.PropertyType)
        {
            error = "属性类型已经变化";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static string FormatRecordError(
        PlayModePrefabRecordedChange record,
        string error)
    {
        return string.Format(
            "{0} · {1} · {2}：{3}",
            record.NodeDisplayPath,
            record.ComponentDisplayName,
            string.IsNullOrEmpty(record.PropertyDisplayName)
                ? record.PropertyPath
                : record.PropertyDisplayName,
            error);
    }

    private static List<PlayModePrefabRecordedChange> GetChanges(
        GameObject runtimeRoot)
    {
        if (runtimeRoot == null)
        {
            return new List<PlayModePrefabRecordedChange>();
        }

        int rootId = runtimeRoot.GetInstanceID();
        return Changes.Values
            .Where(change => change.RuntimeRootInstanceId == rootId)
            .OrderBy(change => change.NodeDisplayPath, StringComparer.Ordinal)
            .ThenBy(change => change.ComponentDisplayName, StringComparer.Ordinal)
            .ThenBy(change => change.PropertyPath, StringComparer.Ordinal)
            .ToList();
    }

    private static void RemoveChanges(
        IEnumerable<PlayModePrefabRecordedChange> records)
    {
        foreach (PlayModePrefabRecordedChange record in records)
        {
            Changes.Remove(record.Key);
        }

        RepaintInspectors();
    }

    private static void ResolveSourcePrefab(
        GameObject target,
        Action<string> onResolved)
    {
        string nativePath = GetNativePrefabPath(target);
        if (IsWritablePrefabPath(nativePath))
        {
            onResolved(nativePath);
            return;
        }

        List<string> names = BuildPrefabCandidateNames(target);
        for (int i = 0; i < names.Count; i++)
        {
            List<string> matches = FindExactPrefabPaths(names[i]);
            if (matches.Count == 1)
            {
                onResolved(matches[0]);
                return;
            }

            if (matches.Count > 1)
            {
                ShowPrefabChoiceMenu(matches, onResolved);
                return;
            }
        }

        EditorUtility.DisplayDialog(
            "未找到引用的 UI Prefab",
            "已按运行时 `(Clone)` 名称精确查找，但没有找到唯一且可写的源 Prefab。\n\n" +
            (names.Count > 0 ? string.Join("\n", names.ToArray()) : target.name),
            "知道了");
    }

    private static string GetNativePrefabPath(GameObject target)
    {
        if (target == null)
        {
            return string.Empty;
        }

        for (Transform current = target.transform;
             current != null;
             current = current.parent)
        {
            GameObject source =
                PrefabUtility.GetCorrespondingObjectFromSource(
                    current.gameObject);
            string sourcePath = AssetDatabase.GetAssetPath(source);
            if (IsPrefabAssetPath(sourcePath))
            {
                return sourcePath;
            }

            if (current.name.EndsWith(CloneSuffix, StringComparison.Ordinal))
            {
                break;
            }
        }

        return string.Empty;
    }

    private static List<string> BuildPrefabCandidateNames(GameObject target)
    {
        var names = new List<string>();
        if (target == null)
        {
            return names;
        }

        for (Transform current = target.transform;
             current != null;
             current = current.parent)
        {
            if (current.name.EndsWith(CloneSuffix, StringComparison.Ordinal))
            {
                AddUnique(names, NormalizeCloneName(current.name));
            }
        }

        if (names.Count == 0)
        {
            AddUnique(names, NormalizeCloneName(target.name));
        }

        return names;
    }

    private static void AddUnique(ICollection<string> names, string name)
    {
        if (string.IsNullOrEmpty(name)
            || names.Any(
                existing => string.Equals(
                    existing,
                    name,
                    StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        names.Add(name);
    }

    private static string NormalizeCloneName(string name)
    {
        string normalized = string.IsNullOrEmpty(name) ? string.Empty : name.Trim();
        while (normalized.EndsWith(CloneSuffix, StringComparison.Ordinal))
        {
            normalized = normalized.Substring(
                0,
                normalized.Length - CloneSuffix.Length).TrimEnd();
        }

        return normalized;
    }

    private static List<string> FindExactPrefabPaths(string prefabName)
    {
        List<string> matches = FindExactPrefabPaths(
            prefabName,
            AssetDatabase.IsValidFolder(UIPrefabRoot)
                ? new[] { UIPrefabRoot }
                : null);
        return matches.Count > 0
            ? matches
            : FindExactPrefabPaths(prefabName, null);
    }

    private static List<string> FindExactPrefabPaths(
        string prefabName,
        string[] folders)
    {
        string[] guids = folders != null
            ? AssetDatabase.FindAssets("t:Prefab " + prefabName, folders)
            : AssetDatabase.FindAssets("t:Prefab " + prefabName);
        var matches = new List<string>();
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            if (IsWritablePrefabPath(path)
                && string.Equals(
                    Path.GetFileNameWithoutExtension(path),
                    prefabName,
                    StringComparison.OrdinalIgnoreCase)
                && !matches.Contains(path))
            {
                matches.Add(path);
            }
        }

        matches.Sort(StringComparer.OrdinalIgnoreCase);
        return matches;
    }

    private static void ShowPrefabChoiceMenu(
        IList<string> prefabPaths,
        Action<string> onSelected)
    {
        var menu = new GenericMenu();
        for (int i = 0; i < prefabPaths.Count; i++)
        {
            string path = prefabPaths[i];
            string label = path.StartsWith(
                UIPrefabRoot + "/",
                StringComparison.OrdinalIgnoreCase)
                ? path.Substring(UIPrefabRoot.Length + 1)
                : path;
            menu.AddItem(
                new GUIContent(label),
                false,
                () => onSelected(path));
        }

        menu.ShowAsContext();
    }

    private static bool IsPrefabAssetPath(string path)
    {
        return !string.IsNullOrEmpty(path)
            && path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsWritablePrefabPath(string path)
    {
        if (!IsPrefabAssetPath(path)
            || AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
        {
            return false;
        }

        try
        {
            var info = new FileInfo(ToAbsoluteProjectPath(path));
            return info.Exists && !info.IsReadOnly;
        }
        catch
        {
            return false;
        }
    }

    private static bool CanOperate(GameObject target)
    {
        return EditorApplication.isPlaying
            && !EditorApplication.isCompiling
            && !EditorApplication.isUpdating
            && IsRuntimeUIObject(target);
    }

    private static bool IsRuntimeUIObject(GameObject target)
    {
        return target != null
            && !EditorUtility.IsPersistent(target)
            && target.scene.IsValid()
            && target.scene.isLoaded
            && target.transform is RectTransform;
    }

    private static GameObject FindTrackingRoot(GameObject target)
    {
        if (!IsRuntimeUIObject(target))
        {
            return null;
        }

        GameObject nativeRoot =
            PrefabUtility.GetNearestPrefabInstanceRoot(target);
        if (nativeRoot != null)
        {
            return nativeRoot;
        }

        for (Transform current = target.transform;
             current != null;
             current = current.parent)
        {
            if (current.name.EndsWith(CloneSuffix, StringComparison.Ordinal))
            {
                return current.gameObject;
            }
        }

        return null;
    }

    private static PlayModePrefabBackupManifest CreateBackup(string assetPath)
    {
        string sourcePath = ToAbsoluteProjectPath(assetPath);
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("源 Prefab 文件不存在。", sourcePath);
        }

        string backupRoot = ToAbsoluteProjectPath(BackupRelativeRoot);
        Directory.CreateDirectory(backupRoot);
        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
        string backupPath = Path.Combine(
            backupRoot,
            string.Format(
                "{0}_{1}_{2}.prefab",
                Path.GetFileNameWithoutExtension(assetPath),
                AssetDatabase.AssetPathToGUID(assetPath),
                timestamp));
        File.Copy(sourcePath, backupPath, false);

        return new PlayModePrefabBackupManifest
        {
            assetPath = assetPath,
            backupPath = backupPath,
            createdAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
        };
    }

    private static void RestoreBackup(PlayModePrefabBackupManifest manifest)
    {
        if (manifest == null
            || string.IsNullOrEmpty(manifest.assetPath)
            || string.IsNullOrEmpty(manifest.backupPath)
            || !File.Exists(manifest.backupPath))
        {
            throw new FileNotFoundException("Prefab 自动备份不存在。");
        }

        File.Copy(
            manifest.backupPath,
            ToAbsoluteProjectPath(manifest.assetPath),
            true);
        AssetDatabase.ImportAsset(
            manifest.assetPath,
            ImportAssetOptions.ForceUpdate);
    }

    private static void WriteLatestBackup(
        PlayModePrefabBackupManifest manifest)
    {
        string path = ToAbsoluteProjectPath(LatestBackupRelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(
            path,
            JsonUtility.ToJson(manifest, true),
            new UTF8Encoding(false));
    }

    private static bool TryReadLatestBackup(
        out PlayModePrefabBackupManifest manifest,
        out string error)
    {
        manifest = null;
        string path = ToAbsoluteProjectPath(LatestBackupRelativePath);
        if (!File.Exists(path))
        {
            error = "还没有运行模式 Prefab 保存备份。";
            return false;
        }

        try
        {
            manifest = JsonUtility.FromJson<PlayModePrefabBackupManifest>(
                File.ReadAllText(path, Encoding.UTF8));
            if (manifest == null
                || string.IsNullOrEmpty(manifest.assetPath)
                || string.IsNullOrEmpty(manifest.backupPath)
                || !File.Exists(manifest.backupPath))
            {
                error = "上次备份记录已经失效。";
                return false;
            }

            error = string.Empty;
            return true;
        }
        catch (Exception exception)
        {
            error = "读取上次备份失败：" + exception.Message;
            return false;
        }
    }

    private static string ToAbsoluteProjectPath(string relativePath)
    {
        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        return Path.GetFullPath(
            Path.Combine(
                projectRoot,
                relativePath.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static bool IsPrefabOpenInPrefabMode(string prefabPath)
    {
        var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
        return prefabStage != null
            && string.Equals(
                prefabStage.assetPath,
                prefabPath,
                StringComparison.OrdinalIgnoreCase);
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode
            || state == PlayModeStateChange.ExitingPlayMode
            || state == PlayModeStateChange.EnteredEditMode)
        {
            ClearChanges();
        }
    }

    private static void ClearChanges()
    {
        Changes.Clear();
        RepaintInspectors();
    }

    private static void RepaintInspectors()
    {
        UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
    }

    private static void Notify(string message)
    {
        EditorWindow window = EditorWindow.focusedWindow;
        if (window != null)
        {
            window.ShowNotification(new GUIContent(message));
        }
    }
}

internal sealed class PlayModePrefabRecordedChange
{
    internal string Key;
    internal int RuntimeRootInstanceId;
    internal string RuntimeRootName;
    internal PlayModePrefabNodeLocator Locator;
    internal string NodeDisplayPath;
    internal Type ComponentType;
    internal int ComponentIndex;
    internal string ComponentDisplayName;
    internal string PropertyPath;
    internal string PropertyDisplayName;
    internal PlayModePrefabPropertyValue Value;
}

internal sealed class PlayModePrefabPropertyValue
{
    internal SerializedPropertyType PropertyType;

    private long integerValue;
    private bool boolValue;
    private double doubleValue;
    private string stringValue;
    private Color colorValue;
    private UnityEngine.Object objectReferenceValue;
    private int objectReferenceInstanceId;
    private bool hadObjectReference;
    private Vector2 vector2Value;
    private Vector3 vector3Value;
    private Vector4 vector4Value;
    private Rect rectValue;
    private AnimationCurve animationCurveValue;
    private Bounds boundsValue;
    private Quaternion quaternionValue;
    private Vector2Int vector2IntValue;
    private Vector3Int vector3IntValue;
    private RectInt rectIntValue;
    private BoundsInt boundsIntValue;
    private Hash128 hash128Value;

    internal static bool TryCapture(
        SerializedProperty property,
        out PlayModePrefabPropertyValue value)
    {
        value = new PlayModePrefabPropertyValue
        {
            PropertyType = property.propertyType
        };

        switch (property.propertyType)
        {
            case SerializedPropertyType.Integer:
            case SerializedPropertyType.LayerMask:
            case SerializedPropertyType.Enum:
            case SerializedPropertyType.Character:
                value.integerValue = property.longValue;
                return true;
            case SerializedPropertyType.Boolean:
                value.boolValue = property.boolValue;
                return true;
            case SerializedPropertyType.Float:
                value.doubleValue = property.doubleValue;
                return true;
            case SerializedPropertyType.String:
                value.stringValue = property.stringValue;
                return true;
            case SerializedPropertyType.Color:
                value.colorValue = property.colorValue;
                return true;
            case SerializedPropertyType.ObjectReference:
                value.objectReferenceValue = property.objectReferenceValue;
                value.hadObjectReference = value.objectReferenceValue != null;
                value.objectReferenceInstanceId =
                    property.objectReferenceInstanceIDValue;
                return true;
            case SerializedPropertyType.Vector2:
                value.vector2Value = property.vector2Value;
                return true;
            case SerializedPropertyType.Vector3:
                value.vector3Value = property.vector3Value;
                return true;
            case SerializedPropertyType.Vector4:
                value.vector4Value = property.vector4Value;
                return true;
            case SerializedPropertyType.Rect:
                value.rectValue = property.rectValue;
                return true;
            case SerializedPropertyType.AnimationCurve:
                value.animationCurveValue = CloneCurve(
                    property.animationCurveValue);
                return true;
            case SerializedPropertyType.Bounds:
                value.boundsValue = property.boundsValue;
                return true;
            case SerializedPropertyType.Quaternion:
                value.quaternionValue = property.quaternionValue;
                return true;
            case SerializedPropertyType.Vector2Int:
                value.vector2IntValue = property.vector2IntValue;
                return true;
            case SerializedPropertyType.Vector3Int:
                value.vector3IntValue = property.vector3IntValue;
                return true;
            case SerializedPropertyType.RectInt:
                value.rectIntValue = property.rectIntValue;
                return true;
            case SerializedPropertyType.BoundsInt:
                value.boundsIntValue = property.boundsIntValue;
                return true;
            case SerializedPropertyType.Hash128:
                value.hash128Value = property.hash128Value;
                return true;
            default:
                value = null;
                return false;
        }
    }

    internal bool TryApply(
        SerializedProperty property,
        GameObject runtimeRoot,
        GameObject sourceRoot,
        out string error)
    {
        UnityEngine.Object mappedReference = null;
        if (PropertyType == SerializedPropertyType.ObjectReference
            && !TryMapObjectReference(
                runtimeRoot,
                sourceRoot,
                out mappedReference,
                out error))
        {
            return false;
        }

        switch (PropertyType)
        {
            case SerializedPropertyType.Integer:
            case SerializedPropertyType.LayerMask:
            case SerializedPropertyType.Enum:
            case SerializedPropertyType.Character:
                property.longValue = integerValue;
                break;
            case SerializedPropertyType.Boolean:
                property.boolValue = boolValue;
                break;
            case SerializedPropertyType.Float:
                property.doubleValue = doubleValue;
                break;
            case SerializedPropertyType.String:
                property.stringValue = stringValue;
                break;
            case SerializedPropertyType.Color:
                property.colorValue = colorValue;
                break;
            case SerializedPropertyType.ObjectReference:
                property.objectReferenceValue = mappedReference;
                break;
            case SerializedPropertyType.Vector2:
                property.vector2Value = vector2Value;
                break;
            case SerializedPropertyType.Vector3:
                property.vector3Value = vector3Value;
                break;
            case SerializedPropertyType.Vector4:
                property.vector4Value = vector4Value;
                break;
            case SerializedPropertyType.Rect:
                property.rectValue = rectValue;
                break;
            case SerializedPropertyType.AnimationCurve:
                property.animationCurveValue = CloneCurve(animationCurveValue);
                break;
            case SerializedPropertyType.Bounds:
                property.boundsValue = boundsValue;
                break;
            case SerializedPropertyType.Quaternion:
                property.quaternionValue = quaternionValue;
                break;
            case SerializedPropertyType.Vector2Int:
                property.vector2IntValue = vector2IntValue;
                break;
            case SerializedPropertyType.Vector3Int:
                property.vector3IntValue = vector3IntValue;
                break;
            case SerializedPropertyType.RectInt:
                property.rectIntValue = rectIntValue;
                break;
            case SerializedPropertyType.BoundsInt:
                property.boundsIntValue = boundsIntValue;
                break;
            case SerializedPropertyType.Hash128:
                property.hash128Value = hash128Value;
                break;
            default:
                error = "不支持保存这种序列化属性类型";
                return false;
        }

        error = string.Empty;
        return true;
    }

    internal bool TryMatches(
        SerializedProperty property,
        GameObject runtimeRoot,
        GameObject sourceRoot,
        out bool matches,
        out string error)
    {
        matches = false;
        UnityEngine.Object mappedReference = null;
        if (PropertyType == SerializedPropertyType.ObjectReference
            && !TryMapObjectReference(
                runtimeRoot,
                sourceRoot,
                out mappedReference,
                out error))
        {
            return false;
        }

        switch (PropertyType)
        {
            case SerializedPropertyType.Integer:
            case SerializedPropertyType.LayerMask:
            case SerializedPropertyType.Enum:
            case SerializedPropertyType.Character:
                matches = property.longValue == integerValue;
                break;
            case SerializedPropertyType.Boolean:
                matches = property.boolValue == boolValue;
                break;
            case SerializedPropertyType.Float:
                matches = NearlyEqual(property.doubleValue, doubleValue);
                break;
            case SerializedPropertyType.String:
                matches = property.stringValue == stringValue;
                break;
            case SerializedPropertyType.Color:
                matches = property.colorValue == colorValue;
                break;
            case SerializedPropertyType.ObjectReference:
                matches = property.objectReferenceValue == mappedReference;
                break;
            case SerializedPropertyType.Vector2:
                matches = property.vector2Value == vector2Value;
                break;
            case SerializedPropertyType.Vector3:
                matches = property.vector3Value == vector3Value;
                break;
            case SerializedPropertyType.Vector4:
                matches = property.vector4Value == vector4Value;
                break;
            case SerializedPropertyType.Rect:
                matches = property.rectValue == rectValue;
                break;
            case SerializedPropertyType.AnimationCurve:
                matches = CurvesEqual(
                    property.animationCurveValue,
                    animationCurveValue);
                break;
            case SerializedPropertyType.Bounds:
                matches = property.boundsValue == boundsValue;
                break;
            case SerializedPropertyType.Quaternion:
                matches = property.quaternionValue == quaternionValue;
                break;
            case SerializedPropertyType.Vector2Int:
                matches = property.vector2IntValue == vector2IntValue;
                break;
            case SerializedPropertyType.Vector3Int:
                matches = property.vector3IntValue == vector3IntValue;
                break;
            case SerializedPropertyType.RectInt:
                matches = property.rectIntValue.Equals(rectIntValue);
                break;
            case SerializedPropertyType.BoundsInt:
                matches = property.boundsIntValue == boundsIntValue;
                break;
            case SerializedPropertyType.Hash128:
                matches = property.hash128Value == hash128Value;
                break;
            default:
                error = "不支持校验这种序列化属性类型";
                return false;
        }

        error = string.Empty;
        return true;
    }

    private bool TryMapObjectReference(
        GameObject runtimeRoot,
        GameObject sourceRoot,
        out UnityEngine.Object mapped,
        out string error)
    {
        mapped = objectReferenceValue;
        if (!hadObjectReference)
        {
            mapped = null;
            error = string.Empty;
            return true;
        }

        if (mapped == null)
        {
            mapped = EditorUtility.InstanceIDToObject(
                objectReferenceInstanceId);
        }

        if (mapped == null)
        {
            error = "记录的对象引用已经失效";
            return false;
        }

        if (EditorUtility.IsPersistent(mapped))
        {
            error = string.Empty;
            return true;
        }

        Component runtimeComponent = mapped as Component;
        GameObject runtimeObject = mapped as GameObject;
        if (runtimeObject == null && runtimeComponent != null)
        {
            runtimeObject = runtimeComponent.gameObject;
        }

        if (runtimeObject == null
            || runtimeRoot == null
            || runtimeObject != runtimeRoot
                && !runtimeObject.transform.IsChildOf(runtimeRoot.transform))
        {
            error = "不能把 Prefab 外部的运行时 Scene 对象引用写入资源";
            return false;
        }

        PlayModePrefabNodeLocator locator =
            PlayModePrefabNodeLocator.Create(
                runtimeRoot.transform,
                runtimeObject.transform);
        Transform sourceTransform = locator != null
            ? locator.Resolve(sourceRoot.transform)
            : null;
        if (sourceTransform == null)
        {
            error = "源 Prefab 中找不到对象引用对应的节点";
            return false;
        }

        if (runtimeComponent == null)
        {
            mapped = sourceTransform.gameObject;
            error = string.Empty;
            return true;
        }

        Type type = runtimeComponent.GetType();
        Component[] runtimeComponents = runtimeObject.GetComponents(type);
        int index = Array.IndexOf(runtimeComponents, runtimeComponent);
        Component[] sourceComponents = sourceTransform.GetComponents(type);
        if (index < 0 || index >= sourceComponents.Length)
        {
            error = "源 Prefab 中找不到对象引用对应的组件";
            return false;
        }

        mapped = sourceComponents[index];
        error = string.Empty;
        return true;
    }

    private static bool NearlyEqual(double left, double right)
    {
        double scale = Math.Max(1d, Math.Max(Math.Abs(left), Math.Abs(right)));
        return Math.Abs(left - right) <= 0.0000001d * scale;
    }

    private static AnimationCurve CloneCurve(AnimationCurve source)
    {
        if (source == null)
        {
            return null;
        }

        return new AnimationCurve(source.keys)
        {
            preWrapMode = source.preWrapMode,
            postWrapMode = source.postWrapMode
        };
    }

    private static bool CurvesEqual(AnimationCurve left, AnimationCurve right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left == null || right == null
            || left.preWrapMode != right.preWrapMode
            || left.postWrapMode != right.postWrapMode
            || left.length != right.length)
        {
            return false;
        }

        Keyframe[] leftKeys = left.keys;
        Keyframe[] rightKeys = right.keys;
        for (int i = 0; i < leftKeys.Length; i++)
        {
            if (!leftKeys[i].Equals(rightKeys[i]))
            {
                return false;
            }
        }

        return true;
    }
}

internal sealed class PlayModePrefabNodeLocator
{
    internal readonly List<PlayModePrefabNodeSegment> Segments =
        new List<PlayModePrefabNodeSegment>();

    internal string StableKey
    {
        get
        {
            return Segments.Count == 0
                ? "$root"
                : string.Join(
                    "/",
                    Segments.Select(
                        segment => segment.Name + "#" +
                            segment.SameNameIndex).ToArray());
        }
    }

    internal static PlayModePrefabNodeLocator Create(
        Transform root,
        Transform target)
    {
        if (root == null
            || target == null
            || target != root && !target.IsChildOf(root))
        {
            return null;
        }

        var reversed = new List<PlayModePrefabNodeSegment>();
        for (Transform current = target;
             current != null && current != root;
             current = current.parent)
        {
            Transform parent = current.parent;
            if (parent == null)
            {
                return null;
            }

            int sameNameIndex = 0;
            for (int i = 0; i < current.GetSiblingIndex(); i++)
            {
                if (parent.GetChild(i).name == current.name)
                {
                    sameNameIndex++;
                }
            }

            reversed.Add(new PlayModePrefabNodeSegment
            {
                Name = current.name,
                SameNameIndex = sameNameIndex
            });
        }

        reversed.Reverse();
        var locator = new PlayModePrefabNodeLocator();
        locator.Segments.AddRange(reversed);
        return locator;
    }

    internal Transform Resolve(Transform root)
    {
        Transform current = root;
        for (int i = 0; i < Segments.Count && current != null; i++)
        {
            PlayModePrefabNodeSegment segment = Segments[i];
            int sameNameIndex = 0;
            Transform match = null;
            for (int childIndex = 0;
                 childIndex < current.childCount;
                 childIndex++)
            {
                Transform child = current.GetChild(childIndex);
                if (child.name != segment.Name)
                {
                    continue;
                }

                if (sameNameIndex == segment.SameNameIndex)
                {
                    match = child;
                    break;
                }

                sameNameIndex++;
            }

            current = match;
        }

        return current;
    }

    internal string GetDisplayPath(string rootName)
    {
        return Segments.Count == 0
            ? rootName
            : rootName + "/" + string.Join(
                "/",
                Segments.Select(segment => segment.Name).ToArray());
    }
}

internal sealed class PlayModePrefabNodeSegment
{
    internal string Name;
    internal int SameNameIndex;
}

[Serializable]
internal sealed class PlayModePrefabBackupManifest
{
    public string assetPath;
    public string backupPath;
    public string createdAt;
}
