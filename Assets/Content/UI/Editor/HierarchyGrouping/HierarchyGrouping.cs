using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Dragon.UI.EditorTools
{
    /// <summary>
    /// Provides a Figma-style Group command for editable Hierarchy objects.
    /// </summary>
    public static class HierarchyGrouping
    {
        private const string MenuPath = "GameObject/Group Selected %g";
        private const string ToolsMenuPath = "UITools/Hierarchy/Group Selected";
        private const string UndoName = "Group Selected Objects";
        private const string DefaultGroupName = "group";

        [MenuItem(MenuPath, false, 0)]
        private static void GroupSelectedFromGameObjectMenu()
        {
            GroupSelected();
        }

        [MenuItem(MenuPath, true)]
        private static bool ValidateGroupSelectedFromGameObjectMenu()
        {
            return CanStartGrouping();
        }

        [MenuItem(ToolsMenuPath, false)]
        private static void GroupSelectedFromToolsMenu()
        {
            GroupSelected();
        }

        [MenuItem(ToolsMenuPath, true)]
        private static bool ValidateGroupSelectedFromToolsMenu()
        {
            return CanStartGrouping();
        }

        private static bool CanStartGrouping()
        {
            if (EditorApplication.isPlaying)
            {
                return PrefabStageUtility.GetCurrentPrefabStage() != null
                    && GetTopLevelSelection().Count > 0;
            }

            return !EditorApplication.isPlayingOrWillChangePlaymode
                && GetTopLevelSelection().Count > 0;
        }

        private static void GroupSelected()
        {
            List<Transform> selection = GetTopLevelSelection();
            if (selection.Count == 0)
            {
                ShowCannotGroup("请先在 Hierarchy 中选择至少一个可编辑节点。");
                return;
            }

            if (EditorApplication.isPlaying && PrefabStageUtility.GetCurrentPrefabStage() == null)
            {
                ShowCannotGroup("运行模式下只能在打开的 Prefab Stage 中创建 Group。");
                return;
            }

            Transform commonParent = selection[0].parent;
            if (commonParent == null)
            {
                ShowCannotGroup("场景根节点不能使用此命令。请先把节点放到同一个父节点下。");
                return;
            }

            for (int i = 0; i < selection.Count; i++)
            {
                Transform item = selection[i];
                if (item.parent != commonParent)
                {
                    ShowCannotGroup("请选择同一父节点下的节点后再按 Ctrl+G。");
                    return;
                }

                if (PrefabUtility.IsPartOfImmutablePrefab(item.gameObject))
                {
                    ShowCannotGroup("选中内容属于不可编辑的 Prefab。请先打开 Prefab 或解除只读限制。");
                    return;
                }
            }

            selection.Sort(CompareSiblingOrder);

            bool uiParent = commonParent is RectTransform;
            if (uiParent && !AllRectTransforms(selection))
            {
                ShowCannotGroup("UI 父节点下的选中项必须全部使用 RectTransform。");
                return;
            }

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(UndoName);
            Undo.RegisterFullObjectHierarchyUndo(commonParent.gameObject, UndoName);

            GameObject groupObject;
            try
            {
                groupObject = uiParent
                    ? GroupRectTransforms(selection, (RectTransform)commonParent)
                    : GroupTransforms(selection, commonParent);
            }
            catch (Exception exception)
            {
                Undo.RevertAllDownToGroup(undoGroup);
                Debug.LogException(exception);
                ShowCannotGroup("创建 Group 时发生错误，已撤销本次修改。请查看 Console 获取详情。");
                return;
            }

            if (groupObject == null)
            {
                Undo.RevertAllDownToGroup(undoGroup);
                ShowCannotGroup("创建 Group 失败，未修改当前层级。");
                return;
            }

            Undo.FlushUndoRecordObjects();
            Undo.CollapseUndoOperations(undoGroup);

            if (groupObject.scene.IsValid())
            {
                EditorSceneManager.MarkSceneDirty(groupObject.scene);
            }

            Selection.activeGameObject = groupObject;
            EditorApplication.RepaintHierarchyWindow();
            SceneView.RepaintAll();
        }

        private static GameObject GroupRectTransforms(
            List<Transform> selection,
            RectTransform commonParent)
        {
            List<RectTransformSnapshot> snapshots = CaptureRectTransformSnapshots(selection);
            if (snapshots.Count == 0)
            {
                return null;
            }

            Vector2 min;
            Vector2 max;
            CalculateSelectionBounds(snapshots, commonParent, out min, out max);

            int insertIndex = snapshots[0].SiblingIndex;
            float groupLocalZ = CalculateAverageLocalZ(snapshots);

            GameObject groupObject = ObjectFactory.CreateGameObject(
                DefaultGroupName,
                typeof(RectTransform));
            RectTransform groupRect = groupObject.GetComponent<RectTransform>();

            Undo.SetTransformParent(groupRect, commonParent, UndoName);
            GameObjectUtility.EnsureUniqueNameForSibling(groupObject);

            groupRect.anchorMin = new Vector2(0.5f, 0.5f);
            groupRect.anchorMax = new Vector2(0.5f, 0.5f);
            groupRect.pivot = new Vector2(0.5f, 0.5f);
            groupRect.localRotation = Quaternion.identity;
            groupRect.localScale = Vector3.one;
            groupRect.sizeDelta = max - min;
            groupRect.localPosition = new Vector3(
                (min.x + max.x) * 0.5f,
                (min.y + max.y) * 0.5f,
                groupLocalZ);
            groupRect.SetSiblingIndex(insertIndex);
            groupObject.layer = commonParent.gameObject.layer;

            Undo.RegisterCreatedObjectUndo(groupObject, UndoName);

            for (int i = 0; i < snapshots.Count; i++)
            {
                RectTransformSnapshot snapshot = snapshots[i];
                RectTransform child = snapshot.Transform;

                Undo.RecordObject(child, UndoName);

                child.anchorMin = new Vector2(0.5f, 0.5f);
                child.anchorMax = new Vector2(0.5f, 0.5f);
                child.pivot = snapshot.Pivot;
                child.sizeDelta = snapshot.Size;
                child.localPosition = snapshot.LocalPosition;
                child.localRotation = snapshot.LocalRotation;
                child.localScale = snapshot.LocalScale;

                Undo.SetTransformParent(child, groupRect, UndoName);
                child.SetSiblingIndex(i);
            }

            return groupObject;
        }

        private static GameObject GroupTransforms(
            List<Transform> selection,
            Transform commonParent)
        {
            int insertIndex = selection[0].GetSiblingIndex();
            Vector3 localCenter = Vector3.zero;
            for (int i = 0; i < selection.Count; i++)
            {
                localCenter += selection[i].localPosition;
            }
            localCenter /= selection.Count;

            GameObject groupObject = ObjectFactory.CreateGameObject(DefaultGroupName);
            Transform groupTransform = groupObject.transform;

            Undo.SetTransformParent(groupTransform, commonParent, UndoName);
            GameObjectUtility.EnsureUniqueNameForSibling(groupObject);
            groupTransform.localPosition = localCenter;
            groupTransform.localRotation = Quaternion.identity;
            groupTransform.localScale = Vector3.one;
            groupTransform.SetSiblingIndex(insertIndex);
            groupObject.layer = commonParent.gameObject.layer;

            Undo.RegisterCreatedObjectUndo(groupObject, UndoName);

            for (int i = 0; i < selection.Count; i++)
            {
                Undo.SetTransformParent(selection[i], groupTransform, UndoName);
                selection[i].SetSiblingIndex(i);
            }

            return groupObject;
        }

        private static List<RectTransformSnapshot> CaptureRectTransformSnapshots(
            List<Transform> selection)
        {
            List<RectTransformSnapshot> snapshots =
                new List<RectTransformSnapshot>(selection.Count);

            for (int i = 0; i < selection.Count; i++)
            {
                RectTransform rectTransform = selection[i] as RectTransform;
                if (rectTransform == null)
                {
                    continue;
                }

                snapshots.Add(new RectTransformSnapshot(rectTransform));
            }

            return snapshots;
        }

        private static void CalculateSelectionBounds(
            List<RectTransformSnapshot> snapshots,
            RectTransform commonParent,
            out Vector2 min,
            out Vector2 max)
        {
            min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            Vector3[] worldCorners = new Vector3[4];

            for (int i = 0; i < snapshots.Count; i++)
            {
                snapshots[i].Transform.GetWorldCorners(worldCorners);
                for (int cornerIndex = 0; cornerIndex < worldCorners.Length; cornerIndex++)
                {
                    Vector3 localCorner = commonParent.InverseTransformPoint(
                        worldCorners[cornerIndex]);
                    min = Vector2.Min(min, localCorner);
                    max = Vector2.Max(max, localCorner);
                }
            }
        }

        private static float CalculateAverageLocalZ(
            List<RectTransformSnapshot> snapshots)
        {
            float total = 0f;
            for (int i = 0; i < snapshots.Count; i++)
            {
                total += snapshots[i].LocalPosition.z;
            }
            return total / snapshots.Count;
        }

        private static bool AllRectTransforms(List<Transform> selection)
        {
            for (int i = 0; i < selection.Count; i++)
            {
                if (!(selection[i] is RectTransform))
                {
                    return false;
                }
            }
            return true;
        }

        private static List<Transform> GetTopLevelSelection()
        {
            Transform[] selectedTransforms = Selection.transforms;
            HashSet<Transform> selectedSet = new HashSet<Transform>(selectedTransforms);
            List<Transform> result = new List<Transform>();

            for (int i = 0; i < selectedTransforms.Length; i++)
            {
                Transform candidate = selectedTransforms[i];
                if (candidate == null
                    || EditorUtility.IsPersistent(candidate)
                    || !candidate.gameObject.scene.IsValid())
                {
                    continue;
                }

                bool hasSelectedAncestor = false;
                Transform ancestor = candidate.parent;
                while (ancestor != null)
                {
                    if (selectedSet.Contains(ancestor))
                    {
                        hasSelectedAncestor = true;
                        break;
                    }
                    ancestor = ancestor.parent;
                }

                if (!hasSelectedAncestor)
                {
                    result.Add(candidate);
                }
            }

            return result;
        }

        private static int CompareSiblingOrder(Transform left, Transform right)
        {
            return left.GetSiblingIndex().CompareTo(right.GetSiblingIndex());
        }

        private static void ShowCannotGroup(string message)
        {
            Debug.LogWarning("Hierarchy Grouping: " + message);
            EditorUtility.DisplayDialog("无法创建 Group", message, "知道了");
        }

        private sealed class RectTransformSnapshot
        {
            public RectTransformSnapshot(RectTransform transform)
            {
                Transform = transform;
                LocalPosition = transform.localPosition;
                LocalRotation = transform.localRotation;
                LocalScale = transform.localScale;
                Pivot = transform.pivot;
                Size = transform.rect.size;
                SiblingIndex = transform.GetSiblingIndex();
            }

            public RectTransform Transform { get; private set; }
            public Vector3 LocalPosition { get; private set; }
            public Quaternion LocalRotation { get; private set; }
            public Vector3 LocalScale { get; private set; }
            public Vector2 Pivot { get; private set; }
            public Vector2 Size { get; private set; }
            public int SiblingIndex { get; private set; }
        }
    }
}
