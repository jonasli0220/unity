using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

public class UIAnimationPathRepairWindow : EditorWindow
{
    private const string MenuPath = "Tools/UI/Animation Path Repair/Open Window";
    private static readonly string[] TransferModeLabels =
    {
        "复制到新路径（保留旧曲线）",
        "迁移到新路径（删除旧曲线）"
    };

    private readonly List<AnimationClip> clips = new List<AnimationClip>();
    private readonly List<PathRepairRow> rows = new List<PathRepairRow>();
    private Vector2 scrollPosition;
    private GameObject targetRoot;
    private BindingTransferMode transferMode = BindingTransferMode.Copy;
    private string scanMessage = "还没有扫描。";
    private string actionMessage = string.Empty;
    private MessageType actionMessageType = MessageType.None;

    [MenuItem(MenuPath, false, 2320)]
    public static void Open()
    {
        var window = GetWindow<UIAnimationPathRepairWindow>("Anim Path Repair");
        window.minSize = new Vector2(520, 420);
        window.AutoFillContext();
        window.Show();
    }

    public static void OpenAndScanCurrentAnimation()
    {
        var window = GetWindow<UIAnimationPathRepairWindow>("Anim Path Repair");
        window.minSize = new Vector2(520, 420);
        window.AutoFillContext();
        window.Scan();
        window.Show();
        window.Focus();
    }

    private void OnEnable()
    {
        if (targetRoot == null && clips.Count == 0)
        {
            AutoFillContext();
        }
    }

    private void OnSelectionChange()
    {
        Repaint();
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Animation 丢失路径复制 / 修复", EditorStyles.boldLabel);
        DrawActionArea();
        EditorGUILayout.Space(8);
        DrawResultArea();
    }

    private void DrawActionArea()
    {
        EditorGUILayout.LabelField("处理方式", EditorStyles.miniBoldLabel);
        transferMode = (BindingTransferMode)GUILayout.Toolbar((int)transferMode, TransferModeLabels);
        EditorGUILayout.HelpBox(
            transferMode == BindingTransferMode.Copy
                ? "复制会完整保留原曲线，并在新路径创建一份相同曲线。关键帧、切线、权重、循环方式和对象引用都会保留。"
                : "迁移会把曲线改到新路径，并删除原路径上的曲线。该模式等同于原来的路径修复。",
            MessageType.Info);
        EditorGUILayout.HelpBox(
            "扫描后“新路径”会先带入原路径。通常只需改掉被重命名的那一段；也可以直接选中 Hierarchy 里的新节点自动带入。",
            MessageType.None);

        using (new EditorGUILayout.HorizontalScope())
        {
            GUI.enabled = targetRoot != null && clips.Any(clip => clip != null);
            if (GUILayout.Button("重新扫描当前动画", GUILayout.Height(30)))
            {
                Scan();
            }

            GUI.enabled = rows.Any(row => row.CanApply);
            var applyButtonText = transferMode == BindingTransferMode.Copy
                ? "批量复制全部有效路径"
                : "批量迁移全部有效路径";
            if (GUILayout.Button(applyButtonText, GUILayout.Height(30)))
            {
                ApplyRepairs(rows.Where(row => row.CanApply).ToArray());
            }

            GUI.enabled = true;
        }

        EditorGUILayout.HelpBox(scanMessage, MessageType.None);
        if (!string.IsNullOrEmpty(actionMessage))
        {
            EditorGUILayout.HelpBox(actionMessage, actionMessageType);
        }
    }

    private void DrawResultArea()
    {
        if (rows.Count == 0)
        {
            return;
        }

        EditorGUILayout.LabelField("扫描结果", EditorStyles.boldLabel);
        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

        foreach (var row in rows)
        {
            if (row.IsAlreadyValid)
            {
                continue;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("原 Missing 路径：" + row.OldPathLabel, EditorStyles.boldLabel);
                    GUILayout.FlexibleSpace();
                    EditorGUILayout.LabelField(row.StatusText, GUILayout.Width(190));
                }

                var nextTargetPath = EditorGUILayout.TextField("新路径", row.TargetPath);
                if (nextTargetPath != row.TargetPath)
                {
                    row.SelectedCandidateIndex = -1;
                    row.SetTargetPath(targetRoot != null ? targetRoot.transform : null, nextTargetPath);
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("使用当前选中节点", GUILayout.Width(130)))
                    {
                        row.TrySetTargetFromTransform(
                            targetRoot != null ? targetRoot.transform : null,
                            Selection.activeTransform);
                    }
                }

                if (!string.IsNullOrEmpty(row.TargetWarning))
                {
                    EditorGUILayout.HelpBox(row.TargetWarning, MessageType.Warning);
                }
                else
                {
                    EditorGUILayout.HelpBox(
                        string.Format("✓ 目标路径有效：{0} → {1}", row.OldPathLabel, row.TargetPathLabel),
                        MessageType.Info);

                    var singleActionText = transferMode == BindingTransferMode.Copy
                        ? "复制这条路径"
                        : "迁移这条路径";
                    if (GUILayout.Button(singleActionText, GUILayout.Height(28)))
                    {
                        ApplyRepairs(new[] { row });
                    }
                }

                if (!string.IsNullOrEmpty(row.LastApplyMessage))
                {
                    EditorGUILayout.HelpBox(row.LastApplyMessage, MessageType.Info);
                }

                if (row.Candidates.Count == 0)
                {
                    if (!row.CanApply)
                    {
                        EditorGUILayout.HelpBox(
                            "请直接修改上面的“新路径”，或在 Hierarchy 选中改名后的节点并点击“使用当前选中节点”。",
                            MessageType.None);
                    }

                    continue;
                }

                if (row.Candidates.Count == 1)
                {
                    DrawCandidateLine(row, 0);
                    continue;
                }

                var popupIndex = row.SelectedCandidateIndex < 0 ? 0 : row.SelectedCandidateIndex + 1;
                var labels = new string[row.Candidates.Count + 1];
                labels[0] = "请选择目标节点...";
                for (int i = 0; i < row.Candidates.Count; i++)
                {
                    labels[i + 1] = row.Candidates[i].DisplayLabel;
                }

                var nextIndex = EditorGUILayout.Popup("目标节点", popupIndex, labels);
                row.SelectedCandidateIndex = nextIndex <= 0 ? -1 : nextIndex - 1;
                if (row.SelectedCandidateIndex >= 0)
                {
                    row.SetTargetPath(targetRoot.transform, row.Candidates[row.SelectedCandidateIndex].Path);
                }

                if (row.SelectedCandidateIndex >= 0)
                {
                    DrawCandidateLine(row, row.SelectedCandidateIndex);
                }
            }
        }

        EditorGUILayout.EndScrollView();
    }

    private void DrawCandidateLine(PathRepairRow row, int candidateIndex)
    {
        var candidate = row.Candidates[candidateIndex];
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField("同名候选", candidate.DisplayLabel);
            if (GUILayout.Button("选中节点", GUILayout.Width(80)))
            {
                Selection.activeGameObject = candidate.Transform.gameObject;
                EditorGUIUtility.PingObject(candidate.Transform.gameObject);
            }
        }

        if (!candidate.IsUsable)
        {
            EditorGUILayout.HelpBox(candidate.Warning, MessageType.Warning);
        }
    }

    private void AutoFillContext()
    {
        var animationWindowContext = UIAnimationPathRepairAnimationWindowAccess.GetCurrentContext();
        targetRoot = animationWindowContext.Root != null ? animationWindowContext.Root : GuessTargetRoot();
        clips.Clear();
        actionMessage = string.Empty;

        if (animationWindowContext.Clip != null)
        {
            AddClip(animationWindowContext.Clip);
        }

        if (clips.Count == 0)
        {
            AddClipsFromSelection();
        }

        if (targetRoot != null && clips.Count == 0)
        {
            AddClipsFromGameObject(targetRoot);
        }

        rows.Clear();
        scanMessage = targetRoot == null
            ? "没有自动找到根节点。请打开 Prefab 或选中一个 GameObject。"
            : "已自动获取上下文，可以扫描。";
    }

    private static GameObject GuessTargetRoot()
    {
        var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
        if (prefabStage != null && prefabStage.prefabContentsRoot != null)
        {
            return prefabStage.prefabContentsRoot;
        }

        if (Selection.activeGameObject != null)
        {
            return Selection.activeGameObject.transform.root.gameObject;
        }

        var scene = SceneManager.GetActiveScene();
        if (scene.IsValid())
        {
            var roots = scene.GetRootGameObjects();
            if (roots.Length == 1)
            {
                return roots[0];
            }
        }

        return null;
    }

    private void AddClipsFromSelection()
    {
        foreach (var selected in Selection.objects)
        {
            AddClipsFromObject(selected);
        }

        if (targetRoot != null && clips.Count == 0)
        {
            AddClipsFromGameObject(targetRoot);
        }
    }

    private void AddClipsFromObject(UnityEngine.Object selected)
    {
        if (selected == null)
        {
            return;
        }

        var clip = selected as AnimationClip;
        if (clip != null)
        {
            AddClip(clip);
            return;
        }

        var controller = selected as RuntimeAnimatorController;
        if (controller != null)
        {
            AddClipsFromController(controller);
            return;
        }

        var gameObject = selected as GameObject;
        if (gameObject != null)
        {
            AddClipsFromGameObject(gameObject);
        }
    }

    private void AddClipsFromGameObject(GameObject gameObject)
    {
        if (gameObject == null)
        {
            return;
        }

        foreach (var animator in gameObject.GetComponentsInChildren<Animator>(true))
        {
            AddClipsFromController(animator.runtimeAnimatorController);
        }

        foreach (var animation in gameObject.GetComponentsInChildren<Animation>(true))
        {
            foreach (AnimationState state in animation)
            {
                AddClip(state.clip);
            }
        }
    }

    private void AddClipsFromController(RuntimeAnimatorController controller)
    {
        if (controller == null)
        {
            return;
        }

        foreach (var clip in controller.animationClips)
        {
            AddClip(clip);
        }
    }

    private void AddClip(AnimationClip clip)
    {
        if (clip == null || clips.Contains(clip))
        {
            return;
        }

        clips.Add(clip);
    }

    private void Scan()
    {
        rows.Clear();
        actionMessage = string.Empty;

        if (targetRoot == null)
        {
            scanMessage = "扫描失败：缺少根节点。";
            return;
        }

        var rowMap = new Dictionary<string, PathRepairRow>();
        foreach (var clip in clips)
        {
            if (clip == null)
            {
                continue;
            }

            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            {
                AddBinding(rowMap, binding);
            }

            foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
            {
                AddBinding(rowMap, binding);
            }
        }

        foreach (var row in rowMap.Values)
        {
            row.Refresh(targetRoot.transform);
            rows.Add(row);
        }

        rows.Sort(CompareRows);

        var repairableCount = rows.Count(row => row.CanApply);
        var choiceCount = rows.Count(row => row.NeedsChoice);
        var unresolvedCount = rows.Count(row => row.IsUnresolved);
        scanMessage = string.Format(
            "扫描完成：{0} 条路径，{1} 条已准备好新路径，{2} 条需要选择，{3} 条需要手动改名或选择节点。",
            rows.Count,
            repairableCount,
            choiceCount,
            unresolvedCount);
    }

    private static int CompareRows(PathRepairRow left, PathRepairRow right)
    {
        var statusCompare = left.SortOrder.CompareTo(right.SortOrder);
        if (statusCompare != 0)
        {
            return statusCompare;
        }

        return string.Compare(left.OldPath, right.OldPath, StringComparison.Ordinal);
    }

    private static void AddBinding(Dictionary<string, PathRepairRow> rowMap, EditorCurveBinding binding)
    {
        var oldPath = binding.path ?? string.Empty;
        PathRepairRow row;
        if (!rowMap.TryGetValue(oldPath, out row))
        {
            row = new PathRepairRow(oldPath);
            rowMap.Add(oldPath, row);
        }

        row.AddBinding(binding);
    }

    private void ApplyRepairs(IEnumerable<PathRepairRow> rowsToApply)
    {
        var selectedRows = rowsToApply
            .Where(row => row.CanApply)
            .GroupBy(row => row.OldPath)
            .Select(group => group.First())
            .ToArray();
        var mappings = selectedRows
            .ToDictionary(row => row.OldPath, row => row.TargetPath);

        if (mappings.Count == 0)
        {
            SetActionMessage("没有可执行的路径。请先确认“新路径”显示为有效。", MessageType.Warning);
            return;
        }

        var plans = clips
            .Where(clip => clip != null)
            .Select(clip => BuildTransferPlan(clip, mappings))
            .Where(plan => plan.BindingCount > 0)
            .ToArray();
        if (plans.Length == 0)
        {
            SetActionMessage("没有从当前 AnimationClip 读取到可复制的曲线。请重新扫描后再试。", MessageType.Error);
            return;
        }

        var bindingCount = plans.Sum(plan => plan.BindingCount);
        var conflictCount = plans.Sum(plan => plan.TargetConflictCount);
        var actionName = transferMode == BindingTransferMode.Copy ? "复制" : "迁移";
        var confirmation = string.Format(
            "将{0} {1} 条路径中的 {2} 条曲线绑定，影响 {3} 个 AnimationClip。",
            actionName,
            mappings.Count,
            bindingCount,
            plans.Length);
        confirmation += transferMode == BindingTransferMode.Copy
            ? "\n\n原 Missing 路径和曲线会保留，新路径会得到一份完整副本。"
            : "\n\n原路径上的曲线会被删除，只保留新路径。";
        if (conflictCount > 0)
        {
            confirmation += string.Format(
                "\n\n注意：目标路径已有 {0} 条同属性曲线，继续后会被本次{1}覆盖。",
                conflictCount,
                actionName);
        }

        var needsConfirmation = transferMode == BindingTransferMode.Move || conflictCount > 0;
        if (needsConfirmation
            && !EditorUtility.DisplayDialog(
                    "Animation Path Repair",
                    confirmation,
                    actionName,
                    "取消"))
        {
            return;
        }

        var undoLabel = transferMode == BindingTransferMode.Copy
            ? "Copy Animation Binding Paths"
            : "Move Animation Binding Paths";
        Undo.IncrementCurrentGroup();
        var undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName(undoLabel);

        try
        {
            foreach (var plan in plans)
            {
                Undo.RecordObject(plan.Clip, undoLabel);
                ApplyTransferPlan(plan, transferMode);
                EditorUtility.SetDirty(plan.Clip);
            }

            AssetDatabase.SaveAssets();
            var verificationError = VerifyTransferPlans(plans, transferMode);
            if (!string.IsNullOrEmpty(verificationError))
            {
                throw new InvalidOperationException(verificationError);
            }

            Undo.CollapseUndoOperations(undoGroup);
        }
        catch (Exception ex)
        {
            Undo.RevertAllDownToGroup(undoGroup);
            AssetDatabase.SaveAssets();
            UIAnimationPathRepairAnimationWindowAccess.ForceRefreshOpenWindows();
            SetActionMessage("复制没有写入 AnimationClip，已自动撤销本次操作。\n" + ex.Message, MessageType.Error);
            Debug.LogException(ex);
            return;
        }

        foreach (var row in selectedRows)
        {
            var copiedBindingCount = plans.Sum(plan =>
                plan.FloatCurves.Count(transfer =>
                    string.Equals(transfer.SourceBinding.path ?? string.Empty, row.OldPath, StringComparison.Ordinal))
                + plan.ObjectCurves.Count(transfer =>
                    string.Equals(transfer.SourceBinding.path ?? string.Empty, row.OldPath, StringComparison.Ordinal)));
            row.MarkApplied(actionName, copiedBindingCount);
        }

        UIAnimationPathRepairAnimationWindowAccess.ForceRefreshOpenWindows();
        SetActionMessage(
            string.Format(
                "✓ {0}成功，并已从 AnimationClip 回读确认：{1} 个动画，{2} 条曲线。{3}",
                actionName,
                plans.Length,
                bindingCount,
                transferMode == BindingTransferMode.Copy
                    ? "\n原 Missing 曲线仍保留；Animation 窗口中会同时出现新路径曲线。"
                    : string.Empty),
            MessageType.Info);
    }

    private void SetActionMessage(string message, MessageType messageType)
    {
        actionMessage = message;
        actionMessageType = messageType;
        Repaint();
    }

    private static ClipTransferPlan BuildTransferPlan(
        AnimationClip clip,
        IReadOnlyDictionary<string, string> mappings)
    {
        var plan = new ClipTransferPlan(clip);
        var floatBindings = AnimationUtility.GetCurveBindings(clip);
        var objectBindings = AnimationUtility.GetObjectReferenceCurveBindings(clip);

        foreach (var sourceBinding in floatBindings)
        {
            string targetPath;
            if (!mappings.TryGetValue(sourceBinding.path ?? string.Empty, out targetPath)
                || targetPath == sourceBinding.path)
            {
                continue;
            }

            var targetBinding = sourceBinding;
            targetBinding.path = targetPath;
            plan.FloatCurves.Add(new FloatCurveTransfer(
                sourceBinding,
                targetBinding,
                AnimationUtility.GetEditorCurve(clip, sourceBinding)));
            if (floatBindings.Any(binding => AreSameBinding(binding, targetBinding))
                || plan.FloatCurves
                    .Take(plan.FloatCurves.Count - 1)
                    .Any(transfer => AreSameBinding(transfer.TargetBinding, targetBinding)))
            {
                plan.TargetConflictCount++;
            }
        }

        foreach (var sourceBinding in objectBindings)
        {
            string targetPath;
            if (!mappings.TryGetValue(sourceBinding.path ?? string.Empty, out targetPath)
                || targetPath == sourceBinding.path)
            {
                continue;
            }

            var targetBinding = sourceBinding;
            targetBinding.path = targetPath;
            plan.ObjectCurves.Add(new ObjectCurveTransfer(
                sourceBinding,
                targetBinding,
                AnimationUtility.GetObjectReferenceCurve(clip, sourceBinding)));
            if (objectBindings.Any(binding => AreSameBinding(binding, targetBinding))
                || plan.ObjectCurves
                    .Take(plan.ObjectCurves.Count - 1)
                    .Any(transfer => AreSameBinding(transfer.TargetBinding, targetBinding)))
            {
                plan.TargetConflictCount++;
            }
        }

        return plan;
    }

    private static void ApplyTransferPlan(ClipTransferPlan plan, BindingTransferMode mode)
    {
        if (mode == BindingTransferMode.Move)
        {
            foreach (var transfer in plan.FloatCurves)
            {
                AnimationUtility.SetEditorCurve(plan.Clip, transfer.SourceBinding, null);
            }

            foreach (var transfer in plan.ObjectCurves)
            {
                AnimationUtility.SetObjectReferenceCurve(plan.Clip, transfer.SourceBinding, null);
            }
        }

        foreach (var transfer in plan.FloatCurves)
        {
            AnimationUtility.SetEditorCurve(plan.Clip, transfer.TargetBinding, transfer.Curve);
        }

        foreach (var transfer in plan.ObjectCurves)
        {
            AnimationUtility.SetObjectReferenceCurve(plan.Clip, transfer.TargetBinding, transfer.Keyframes);
        }
    }

    private static bool AreSameBinding(EditorCurveBinding left, EditorCurveBinding right)
    {
        return string.Equals(left.path, right.path, StringComparison.Ordinal)
            && left.type == right.type
            && string.Equals(left.propertyName, right.propertyName, StringComparison.Ordinal);
    }

    private static string VerifyTransferPlans(
        IEnumerable<ClipTransferPlan> plans,
        BindingTransferMode mode)
    {
        foreach (var plan in plans)
        {
            foreach (var transfer in plan.FloatCurves)
            {
                var targetCurve = AnimationUtility.GetEditorCurve(plan.Clip, transfer.TargetBinding);
                if (!AreCurvesEquivalent(transfer.Curve, targetCurve))
                {
                    return string.Format(
                        "{0} 的目标曲线没有完整写入：{1} / {2}",
                        plan.Clip.name,
                        transfer.TargetBinding.path,
                        transfer.TargetBinding.propertyName);
                }

                if (mode == BindingTransferMode.Copy)
                {
                    var sourceCurve = AnimationUtility.GetEditorCurve(plan.Clip, transfer.SourceBinding);
                    if (!AreCurvesEquivalent(transfer.Curve, sourceCurve))
                    {
                        return string.Format(
                            "{0} 的原曲线没有被完整保留：{1} / {2}",
                            plan.Clip.name,
                            transfer.SourceBinding.path,
                            transfer.SourceBinding.propertyName);
                    }
                }
            }

            foreach (var transfer in plan.ObjectCurves)
            {
                var targetKeyframes =
                    AnimationUtility.GetObjectReferenceCurve(plan.Clip, transfer.TargetBinding);
                if (!AreObjectKeyframesEquivalent(transfer.Keyframes, targetKeyframes))
                {
                    return string.Format(
                        "{0} 的目标对象引用曲线没有完整写入：{1} / {2}",
                        plan.Clip.name,
                        transfer.TargetBinding.path,
                        transfer.TargetBinding.propertyName);
                }

                if (mode == BindingTransferMode.Copy)
                {
                    var sourceKeyframes =
                        AnimationUtility.GetObjectReferenceCurve(plan.Clip, transfer.SourceBinding);
                    if (!AreObjectKeyframesEquivalent(transfer.Keyframes, sourceKeyframes))
                    {
                        return string.Format(
                            "{0} 的原对象引用曲线没有被完整保留：{1} / {2}",
                            plan.Clip.name,
                            transfer.SourceBinding.path,
                            transfer.SourceBinding.propertyName);
                    }
                }
            }
        }

        return string.Empty;
    }

    private static bool AreCurvesEquivalent(AnimationCurve expected, AnimationCurve actual)
    {
        if (expected == null || actual == null)
        {
            return expected == actual;
        }

        if (expected.preWrapMode != actual.preWrapMode
            || expected.postWrapMode != actual.postWrapMode
            || expected.length != actual.length)
        {
            return false;
        }

        for (int i = 0; i < expected.length; i++)
        {
            var expectedKey = expected.keys[i];
            var actualKey = actual.keys[i];
            if (!Mathf.Approximately(expectedKey.time, actualKey.time)
                || !Mathf.Approximately(expectedKey.value, actualKey.value)
                || !Mathf.Approximately(expectedKey.inTangent, actualKey.inTangent)
                || !Mathf.Approximately(expectedKey.outTangent, actualKey.outTangent)
                || !Mathf.Approximately(expectedKey.inWeight, actualKey.inWeight)
                || !Mathf.Approximately(expectedKey.outWeight, actualKey.outWeight)
                || expectedKey.weightedMode != actualKey.weightedMode
                || AnimationUtility.GetKeyBroken(expected, i) != AnimationUtility.GetKeyBroken(actual, i)
                || AnimationUtility.GetKeyLeftTangentMode(expected, i)
                    != AnimationUtility.GetKeyLeftTangentMode(actual, i)
                || AnimationUtility.GetKeyRightTangentMode(expected, i)
                    != AnimationUtility.GetKeyRightTangentMode(actual, i))
            {
                return false;
            }
        }

        return true;
    }

    private static bool AreObjectKeyframesEquivalent(
        ObjectReferenceKeyframe[] expected,
        ObjectReferenceKeyframe[] actual)
    {
        expected = expected ?? Array.Empty<ObjectReferenceKeyframe>();
        actual = actual ?? Array.Empty<ObjectReferenceKeyframe>();
        if (expected.Length != actual.Length)
        {
            return false;
        }

        for (int i = 0; i < expected.Length; i++)
        {
            if (!Mathf.Approximately(expected[i].time, actual[i].time)
                || expected[i].value != actual[i].value)
            {
                return false;
            }
        }

        return true;
    }

    private static string GetScenePath(Transform transform)
    {
        if (transform == null)
        {
            return string.Empty;
        }

        var names = new Stack<string>();
        var current = transform;
        while (current != null)
        {
            names.Push(current.name);
            current = current.parent;
        }

        return string.Join("/", names.ToArray());
    }

    private static string GetRelativePath(Transform root, Transform transform)
    {
        if (root == transform)
        {
            return string.Empty;
        }

        var names = new Stack<string>();
        var current = transform;
        while (current != null && current != root)
        {
            names.Push(current.name);
            current = current.parent;
        }

        return current == root ? string.Join("/", names.ToArray()) : GetScenePath(transform);
    }

    private static Transform FindByPath(Transform root, string path)
    {
        if (root == null)
        {
            return null;
        }

        return string.IsNullOrEmpty(path) ? root : root.Find(path);
    }

    private static string GetLeafName(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return string.Empty;
        }

        var slashIndex = path.LastIndexOf('/');
        return slashIndex < 0 ? path : path.Substring(slashIndex + 1);
    }

    private static string NormalizeBindingPath(string path)
    {
        return string.IsNullOrWhiteSpace(path)
            ? string.Empty
            : path.Trim().Replace('\\', '/').Trim('/');
    }

    private static string[] GetMissingRequiredTypeNames(
        Transform transform,
        IEnumerable<Type> requiredTypes)
    {
        return requiredTypes
            .Where(type => !CanGameObjectSatisfyType(transform, type))
            .Select(type => type == null ? "MissingType" : type.Name)
            .Distinct()
            .OrderBy(name => name)
            .ToArray();
    }

    private enum BindingTransferMode
    {
        Copy,
        Move
    }

    private sealed class ClipTransferPlan
    {
        public ClipTransferPlan(AnimationClip clip)
        {
            Clip = clip;
        }

        public AnimationClip Clip { get; private set; }
        public List<FloatCurveTransfer> FloatCurves { get; } = new List<FloatCurveTransfer>();
        public List<ObjectCurveTransfer> ObjectCurves { get; } = new List<ObjectCurveTransfer>();
        public int TargetConflictCount { get; set; }
        public int BindingCount
        {
            get { return FloatCurves.Count + ObjectCurves.Count; }
        }
    }

    private sealed class FloatCurveTransfer
    {
        public FloatCurveTransfer(
            EditorCurveBinding sourceBinding,
            EditorCurveBinding targetBinding,
            AnimationCurve curve)
        {
            SourceBinding = sourceBinding;
            TargetBinding = targetBinding;
            Curve = curve;
        }

        public EditorCurveBinding SourceBinding { get; private set; }
        public EditorCurveBinding TargetBinding { get; private set; }
        public AnimationCurve Curve { get; private set; }
    }

    private sealed class ObjectCurveTransfer
    {
        public ObjectCurveTransfer(
            EditorCurveBinding sourceBinding,
            EditorCurveBinding targetBinding,
            ObjectReferenceKeyframe[] keyframes)
        {
            SourceBinding = sourceBinding;
            TargetBinding = targetBinding;
            Keyframes = keyframes;
        }

        public EditorCurveBinding SourceBinding { get; private set; }
        public EditorCurveBinding TargetBinding { get; private set; }
        public ObjectReferenceKeyframe[] Keyframes { get; private set; }
    }

    private class PathRepairRow
    {
        private readonly HashSet<Type> requiredTypes = new HashSet<Type>();
        private bool hasTargetPath;

        public PathRepairRow(string oldPath)
        {
            OldPath = oldPath;
        }

        public string OldPath { get; private set; }
        public string TargetPath { get; private set; }
        public string TargetWarning { get; private set; }
        public string LastApplyMessage { get; private set; }
        public List<PathCandidate> Candidates { get; private set; }
        public int SelectedCandidateIndex { get; set; }
        public bool IsAlreadyValid { get; private set; }

        public bool CanApply
        {
            get { return !IsAlreadyValid && string.IsNullOrEmpty(TargetWarning); }
        }

        public bool NeedsChoice
        {
            get
            {
                return !IsAlreadyValid
                    && !CanApply
                    && (!hasTargetPath || string.Equals(TargetPath, OldPath, StringComparison.Ordinal))
                    && Candidates.Count(candidate => candidate.IsUsable) > 1
                    && SelectedCandidateIndex < 0;
            }
        }

        public bool IsUnresolved
        {
            get
            {
                return !IsAlreadyValid && !CanApply && !NeedsChoice;
            }
        }

        public int SortOrder
        {
            get
            {
                if (CanApply)
                {
                    return 0;
                }

                if (NeedsChoice)
                {
                    return 1;
                }

                if (IsUnresolved)
                {
                    return 2;
                }

                return 3;
            }
        }

        public string OldPathLabel
        {
            get { return string.IsNullOrEmpty(OldPath) ? "(root)" : OldPath; }
        }

        public string TargetPathLabel
        {
            get { return string.IsNullOrEmpty(TargetPath) ? "(root)" : TargetPath; }
        }

        public string StatusText
        {
            get
            {
                if (IsAlreadyValid)
                {
                    return "已有效";
                }

                if (!string.IsNullOrEmpty(LastApplyMessage))
                {
                    return "✓ 已执行并验证";
                }

                if (CanApply)
                {
                    return "✓ 目标有效";
                }

                if (NeedsChoice)
                {
                    return "需要选择目标节点";
                }

                return "请填写有效的新路径";
            }
        }

        public void AddBinding(EditorCurveBinding binding)
        {
            if (binding.type != null)
            {
                requiredTypes.Add(binding.type);
            }
        }

        public void Refresh(Transform root)
        {
            Candidates = new List<PathCandidate>();
            SelectedCandidateIndex = -1;
            TargetPath = OldPath;
            hasTargetPath = true;
            TargetWarning = "请修改新路径中已改名的节点，或使用当前选中节点。";
            LastApplyMessage = string.Empty;

            var oldTarget = FindByPath(root, OldPath);
            IsAlreadyValid = oldTarget != null && HasRequiredComponents(oldTarget, requiredTypes);
            if (IsAlreadyValid)
            {
                return;
            }

            var leafName = GetLeafName(OldPath);
            if (string.IsNullOrEmpty(leafName))
            {
                return;
            }

            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            {
                if (transform.name != leafName)
                {
                    continue;
                }

                Candidates.Add(new PathCandidate(root, transform, requiredTypes));
            }

            var usableCandidates = Candidates
                .Select((candidate, index) => new { Candidate = candidate, Index = index })
                .Where(item => item.Candidate.IsUsable)
                .ToArray();
            if (usableCandidates.Length == 1)
            {
                SelectedCandidateIndex = usableCandidates[0].Index;
                SetTargetPath(root, usableCandidates[0].Candidate.Path);
            }
        }

        public void SetTargetPath(Transform root, string path)
        {
            TargetPath = NormalizeBindingPath(path);
            hasTargetPath = !string.IsNullOrWhiteSpace(path);
            LastApplyMessage = string.Empty;
            ValidateTargetPath(root);
        }

        public void TrySetTargetFromTransform(Transform root, Transform target)
        {
            SelectedCandidateIndex = -1;
            if (root == null)
            {
                TargetWarning = "当前没有可用的动画根节点，请重新扫描。";
                return;
            }

            if (target == null)
            {
                TargetWarning = "请先在 Hierarchy 里选中重命名后的目标节点。";
                return;
            }

            if (target != root && !target.IsChildOf(root))
            {
                TargetWarning = "当前选中节点不在动画根节点下，不能作为目标路径。";
                return;
            }

            TargetPath = GetRelativePath(root, target);
            hasTargetPath = true;
            LastApplyMessage = string.Empty;
            ValidateTargetPath(root);
        }

        public void MarkApplied(string actionName, int bindingCount)
        {
            LastApplyMessage = string.Format(
                "✓ 已{0}并回读确认：{1} 条曲线，目标路径 {2}",
                actionName,
                bindingCount,
                TargetPathLabel);
        }

        private void ValidateTargetPath(Transform root)
        {
            if (!hasTargetPath)
            {
                TargetWarning = "请填写新路径，或使用当前选中节点。";
                return;
            }

            if (string.Equals(TargetPath, OldPath, StringComparison.Ordinal))
            {
                TargetWarning = "新路径和原路径相同，不需要复制。";
                return;
            }

            var target = FindByPath(root, TargetPath);
            if (target == null)
            {
                TargetWarning = "当前动画根节点下找不到该路径。建议在 Hierarchy 选中目标节点后自动带入，避免拼写错误。";
                return;
            }

            var missingTypes = GetMissingRequiredTypeNames(target, requiredTypes);
            if (missingTypes.Length > 0)
            {
                TargetWarning = "目标节点缺少动画所需组件：" + string.Join(", ", missingTypes);
                return;
            }

            TargetWarning = string.Empty;
        }

        private static bool HasRequiredComponents(Transform transform, IEnumerable<Type> types)
        {
            return GetMissingRequiredTypeNames(transform, types).Length == 0;
        }
    }

    private class PathCandidate
    {
        public PathCandidate(Transform root, Transform transform, IEnumerable<Type> requiredTypes)
        {
            Transform = transform;
            Path = GetRelativePath(root, transform);
            var missingTypes = GetMissingRequiredTypeNames(transform, requiredTypes);
            IsUsable = missingTypes.Length == 0;
            Warning = IsUsable ? string.Empty : "同名节点存在，但缺少组件：" + string.Join(", ", missingTypes);
        }

        public Transform Transform { get; private set; }
        public string Path { get; private set; }
        public bool IsUsable { get; private set; }
        public string Warning { get; private set; }

        public string DisplayLabel
        {
            get { return IsUsable ? Path : Path + "（缺组件）"; }
        }
    }

    private static bool CanGameObjectSatisfyType(Transform transform, Type type)
    {
        if (transform == null || type == null)
        {
            return false;
        }

        if (type == typeof(GameObject))
        {
            return true;
        }

        if (typeof(Component).IsAssignableFrom(type))
        {
            return transform.GetComponent(type) != null;
        }

        return true;
    }
}

[InitializeOnLoad]
public static class UIAnimationPathRepairAnimationWindowButton
{
    private const string ButtonName = "DragonAnimationPathRepairButton";
    private const string ButtonText = "复制 / 修复路径";
    private const double AttachIntervalSeconds = 0.5d;
    private const float ButtonLeft = 0f;
    private const float ButtonBottom = 0f;
    private const float ButtonWidth = 128f;
    private const float ButtonHeight = 20f;

    private static double nextAttachTime;

    static UIAnimationPathRepairAnimationWindowButton()
    {
        EditorApplication.update += OnEditorUpdate;
    }

    private static void OnEditorUpdate()
    {
        if (EditorApplication.timeSinceStartup < nextAttachTime)
        {
            return;
        }

        nextAttachTime = EditorApplication.timeSinceStartup + AttachIntervalSeconds;
        foreach (var window in UIAnimationPathRepairAnimationWindowAccess.GetOpenAnimationWindows())
        {
            AttachButton(window);
        }
    }

    private static void AttachButton(EditorWindow window)
    {
        if (window == null || window.rootVisualElement == null)
        {
            return;
        }

        var root = window.rootVisualElement;
        var button = root.Q<UnityEngine.UIElements.Button>(ButtonName);
        if (button == null)
        {
            button = CreateButton();
        }

        if (button.parent != root)
        {
            button.RemoveFromHierarchy();
            root.Add(button);
        }

        button.style.position = Position.Absolute;
        button.style.left = ButtonLeft;
        button.style.right = StyleKeyword.Auto;
        button.style.top = StyleKeyword.Auto;
        button.style.bottom = ButtonBottom;
        button.style.width = ButtonWidth;
        button.style.height = ButtonHeight;
        button.style.marginLeft = 0f;
        button.style.marginRight = 0f;
        button.style.marginTop = 0f;
        button.style.marginBottom = 0f;
        button.style.flexShrink = 0f;
    }

    private static UnityEngine.UIElements.Button CreateButton()
    {
        var button = new UnityEngine.UIElements.Button(UIAnimationPathRepairWindow.OpenAndScanCurrentAnimation)
        {
            name = ButtonName,
            text = ButtonText,
            tooltip = "扫描当前 AnimationClip 的 Missing 路径，完整复制曲线到重命名后的节点，或迁移原曲线路径。"
        };

        button.AddToClassList("unity-button");
        button.style.unityTextAlign = TextAnchor.MiddleCenter;
        button.style.unityFontStyleAndWeight = FontStyle.Bold;
        return button;
    }
}

public static class UIAnimationPathRepairAnimationWindowAccess
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static Type animationWindowType;

    private static Type AnimationWindowType
    {
        get
        {
            if (animationWindowType == null)
            {
                animationWindowType = typeof(EditorWindow).Assembly.GetType("UnityEditor.AnimationWindow");
            }

            return animationWindowType;
        }
    }

    public static IEnumerable<EditorWindow> GetOpenAnimationWindows()
    {
        var type = AnimationWindowType;
        if (type == null)
        {
            yield break;
        }

        foreach (var obj in Resources.FindObjectsOfTypeAll(type))
        {
            var window = obj as EditorWindow;
            if (window != null)
            {
                yield return window;
            }
        }
    }

    public static AnimationWindowContext GetCurrentContext()
    {
        foreach (var window in GetOpenAnimationWindows())
        {
            var state = GetAnimationWindowState(window);
            if (state == null)
            {
                continue;
            }

            var context = new AnimationWindowContext
            {
                Clip = GetActiveClip(state),
                Root = GetActiveRoot(state)
            };

            if (context.Clip != null || context.Root != null)
            {
                return context;
            }
        }

        return new AnimationWindowContext();
    }

    public static void ForceRefreshOpenWindows()
    {
        var type = AnimationWindowType;
        if (type == null)
        {
            return;
        }

        var forceRefresh = type.GetMethod(
            "ForceRefresh",
            Flags,
            null,
            Type.EmptyTypes,
            null);
        foreach (var window in GetOpenAnimationWindows())
        {
            try
            {
                if (forceRefresh != null)
                {
                    forceRefresh.Invoke(window, null);
                }

                var state = GetAnimationWindowState(window);
                InvokeNoArgMethod(state, "ForceRefresh");
                window.Repaint();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("Animation Path Repair 刷新 Animation 窗口失败：" + ex.Message);
            }
        }
    }

    private static void InvokeNoArgMethod(object target, string methodName)
    {
        if (target == null)
        {
            return;
        }

        var method = target.GetType().GetMethod(
            methodName,
            Flags,
            null,
            Type.EmptyTypes,
            null);
        if (method != null)
        {
            method.Invoke(target, null);
        }
    }

    private static object GetAnimationWindowState(EditorWindow window)
    {
        if (window == null)
        {
            return null;
        }

        return GetProperty<object>(window, "state");
    }

    private static AnimationClip GetActiveClip(object state)
    {
        var clip = GetProperty<AnimationClip>(state, "activeAnimationClip");
        if (clip != null)
        {
            return clip;
        }

        clip = GetClipFromCurveCollection(state, "activeCurves");
        if (clip != null)
        {
            return clip;
        }

        return GetClipFromCurveCollection(state, "allCurves");
    }

    private static AnimationClip GetClipFromCurveCollection(object state, string propertyName)
    {
        var curves = GetProperty<object>(state, propertyName) as System.Collections.IEnumerable;
        if (curves == null)
        {
            return null;
        }

        foreach (var curve in curves)
        {
            var clip = GetProperty<AnimationClip>(curve, "clip");
            if (clip != null)
            {
                return clip;
            }
        }

        return null;
    }

    private static GameObject GetActiveRoot(object state)
    {
        var root = GetGameObjectProperty(state, "activeRootGameObject");
        if (root != null)
        {
            return root;
        }

        root = GetGameObjectProperty(state, "rootGameObject");
        if (root != null)
        {
            return root;
        }

        var activeGameObject = GetGameObjectProperty(state, "activeGameObject");
        return activeGameObject != null ? activeGameObject.transform.root.gameObject : null;
    }

    private static GameObject GetGameObjectProperty(object target, string propertyName)
    {
        var value = GetProperty<UnityEngine.Object>(target, propertyName);
        var gameObject = value as GameObject;
        if (gameObject != null)
        {
            return gameObject;
        }

        var component = value as Component;
        if (component != null)
        {
            return component.gameObject;
        }

        return null;
    }

    private static T GetProperty<T>(object target, string propertyName) where T : class
    {
        if (target == null)
        {
            return null;
        }

        var property = target.GetType().GetProperty(propertyName, Flags);
        if (property == null)
        {
            return null;
        }

        try
        {
            return property.GetValue(target, null) as T;
        }
        catch
        {
            return null;
        }
    }
}

public struct AnimationWindowContext
{
    public AnimationClip Clip;
    public GameObject Root;
}
