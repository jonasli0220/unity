using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Presets;
using UnityEngine;

/// <summary>
/// 在 Transform / RectTransform 的组件菜单中，一次复制或粘贴节点上的全部组件及其序列化数值。
/// </summary>
internal static class UINodeComponentClipboard
{
    private const string CopyMenuPath = "CONTEXT/Transform/Copy/节点全部组件（含数值）";
    private const string PasteMenuPath = "CONTEXT/Transform/Paste/节点全部组件（含数值）";

    private sealed class ComponentSnapshot
    {
        public Type Type;
        public Preset Preset;
    }

    private static readonly List<ComponentSnapshot> Snapshots = new List<ComponentSnapshot>();
    private static string sourceName;

    [MenuItem(CopyMenuPath, false, 1000)]
    private static void CopyAllComponents(MenuCommand command)
    {
        var sourceTransform = command.context as Transform;
        if (sourceTransform == null)
        {
            return;
        }

        ClearSnapshots();

        int missingScriptCount = 0;
        Component[] components = sourceTransform.gameObject.GetComponents<Component>();
        foreach (Component component in components)
        {
            if (component == null)
            {
                missingScriptCount++;
                continue;
            }

            var preset = new Preset(component)
            {
                hideFlags = HideFlags.HideAndDontSave
            };

            Snapshots.Add(new ComponentSnapshot
            {
                Type = component.GetType(),
                Preset = preset
            });
        }

        sourceName = sourceTransform.gameObject.name;
        string missingScriptHint = missingScriptCount > 0
            ? $"；跳过 {missingScriptCount} 个 Missing Script"
            : string.Empty;
        Notify($"已复制“{sourceName}”的 {Snapshots.Count} 个组件（含数值）{missingScriptHint}");
    }

    [MenuItem(PasteMenuPath, false, 1000)]
    private static void PasteAllComponents(MenuCommand command)
    {
        var destinationTransform = command.context as Transform;
        if (destinationTransform == null || Snapshots.Count == 0)
        {
            return;
        }

        GameObject destination = destinationTransform.gameObject;
        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("粘贴节点全部组件（含数值）");

        try
        {
            var targets = new List<Component>(Snapshots.Count);
            var occurrenceByType = new Dictionary<Type, int>();
            int addedCount = 0;

            foreach (ComponentSnapshot snapshot in Snapshots)
            {
                Component targetComponent;
                if (typeof(Transform).IsAssignableFrom(snapshot.Type))
                {
                    if (!snapshot.Type.IsInstanceOfType(destinationTransform))
                    {
                        throw new InvalidOperationException(
                            $"目标节点的 {destinationTransform.GetType().Name} 与复制的 {snapshot.Type.Name} 不兼容。");
                    }

                    targetComponent = destinationTransform;
                }
                else
                {
                    int occurrence = GetAndIncreaseOccurrence(occurrenceByType, snapshot.Type);
                    Component[] sameTypeComponents = destination.GetComponents(snapshot.Type);
                    targetComponent = occurrence < sameTypeComponents.Length
                        ? sameTypeComponents[occurrence]
                        : null;

                    if (targetComponent == null)
                    {
                        targetComponent = Undo.AddComponent(destination, snapshot.Type);
                        if (targetComponent == null)
                        {
                            throw new InvalidOperationException($"无法添加组件 {snapshot.Type.Name}。");
                        }

                        addedCount++;
                    }
                }

                targets.Add(targetComponent);
            }

            for (int i = 0; i < Snapshots.Count; i++)
            {
                Component targetComponent = targets[i];
                Undo.RecordObject(targetComponent, "粘贴节点全部组件（含数值）");

                if (!Snapshots[i].Preset.ApplyTo(targetComponent))
                {
                    throw new InvalidOperationException($"无法粘贴组件 {Snapshots[i].Type.Name} 的数值。");
                }

                PrefabUtility.RecordPrefabInstancePropertyModifications(targetComponent);
                EditorUtility.SetDirty(targetComponent);
            }

            Undo.CollapseUndoOperations(undoGroup);
            Notify($"已将“{sourceName}”的 {Snapshots.Count} 个组件粘贴到“{destination.name}”"
                   + (addedCount > 0 ? $"，补齐 {addedCount} 个组件" : string.Empty));
        }
        catch (Exception exception)
        {
            Undo.RevertAllDownToGroup(undoGroup);
            Debug.LogError($"[节点组件剪贴板] 粘贴失败：{exception.Message}", destination);
            EditorUtility.DisplayDialog("粘贴节点全部组件失败", exception.Message, "确定");
        }
    }

    [MenuItem(PasteMenuPath, true)]
    private static bool ValidatePasteAllComponents()
    {
        return Snapshots.Count > 0;
    }

    private static int GetAndIncreaseOccurrence(Dictionary<Type, int> occurrenceByType, Type type)
    {
        int occurrence;
        occurrenceByType.TryGetValue(type, out occurrence);
        occurrenceByType[type] = occurrence + 1;
        return occurrence;
    }

    private static void ClearSnapshots()
    {
        foreach (ComponentSnapshot snapshot in Snapshots)
        {
            if (snapshot.Preset != null)
            {
                UnityEngine.Object.DestroyImmediate(snapshot.Preset);
            }
        }

        Snapshots.Clear();
        sourceName = null;
    }

    private static void Notify(string message)
    {
        Debug.Log($"[节点组件剪贴板] {message}");
        if (SceneView.lastActiveSceneView != null)
        {
            SceneView.lastActiveSceneView.ShowNotification(new GUIContent(message), 2.5f);
        }
    }
}
