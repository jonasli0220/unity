using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
internal static class PlayModeAnimationSaver
{
    private const string MenuPath = "UITools/运行模式/保存当前 Animation 动效";
    private const string UIAssetRoot = "Assets/Content/UI";
    private const string UIPrefabRoot = "Assets/Content/UI/Prefab";
    private const string CloneSuffix = "(Clone)";

    private static readonly MethodInfo SaveAssetIfDirtyMethod =
        typeof(AssetDatabase).GetMethod(
            "SaveAssetIfDirty",
            BindingFlags.Static | BindingFlags.Public,
            null,
            new[] { typeof(UnityEngine.Object) },
            null);

    private static bool isChangingPlayMode;

    static PlayModeAnimationSaver()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    [MenuItem(MenuPath, false, 1980)]
    private static void SaveCurrentAnimationClip()
    {
        if (!CanOperate())
        {
            EditorUtility.DisplayDialog(
                "无法保存运行模式动效",
                "请在稳定的运行模式中操作；等待编译结束后再试。",
                "知道了");
            return;
        }

        PlayModeAnimationContext context =
            PlayModeAnimationWindowAccess.GetCurrentContext();
        AnimationClip runtimeClip = context.Clip;
        if (runtimeClip == null)
        {
            EditorUtility.DisplayDialog(
                "没有检测到当前动效",
                "请先打开 Animation 窗口，并选中正在调试的动画片段。",
                "知道了");
            return;
        }

        AnimationClip directAsset = GetStandaloneWritableMainClip(runtimeClip);
        if (directAsset == runtimeClip)
        {
            TrySave(runtimeClip, directAsset, context.Window);
            return;
        }

        GameObject contextRoot = context.Root != null
            ? context.Root
            : Selection.activeGameObject;
        List<PlayModeAnimationCandidate> candidates =
            FindSourceCandidates(runtimeClip.name, contextRoot);
        AnimationClip preferredTarget = ResolvePreferredTarget(candidates);
        if (preferredTarget != null)
        {
            TrySave(runtimeClip, preferredTarget, context.Window);
            return;
        }

        PlayModeAnimationTargetWindow.Show(
            runtimeClip,
            context.Window,
            candidates);
    }

    [MenuItem(MenuPath, true)]
    private static bool ValidateSaveCurrentAnimationClip()
    {
        return CanOperate();
    }

    internal static bool TrySave(
        AnimationClip runtimeClip,
        AnimationClip targetClip,
        EditorWindow sourceWindow)
    {
        string validationError;
        if (!ValidateTarget(runtimeClip, targetClip, out validationError))
        {
            EditorUtility.DisplayDialog(
                "无法保存运行模式动效",
                validationError,
                "知道了");
            return false;
        }

        PlayModeAnimationClipSnapshot snapshot;
        try
        {
            snapshot = PlayModeAnimationClipSnapshot.Capture(runtimeClip);
        }
        catch (Exception exception)
        {
            EditorUtility.DisplayDialog(
                "读取当前动效失败",
                "Animation 窗口中的曲线没有完整读出：\n" + exception.Message,
                "知道了");
            return false;
        }

        string targetPath = AssetDatabase.GetAssetPath(targetClip);
        if (runtimeClip == targetClip)
        {
            try
            {
                EditorUtility.SetDirty(targetClip);
                SaveOnlyThisAsset(targetClip);
                string directVerifyError = snapshot.Verify(targetClip);
                if (!string.IsNullOrEmpty(directVerifyError))
                {
                    throw new InvalidOperationException(directVerifyError);
                }

                FinishSuccessfulSave(
                    sourceWindow,
                    targetClip,
                    targetPath,
                    snapshot,
                    false);
                return true;
            }
            catch (Exception exception)
            {
                EditorUtility.DisplayDialog(
                    "保存当前 .anim 失败",
                    exception.Message,
                    "知道了");
                return false;
            }
        }

        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("保存运行模式 Animation 动效");
        Undo.RegisterCompleteObjectUndo(
            targetClip,
            "保存运行模式 Animation 动效");

        try
        {
            snapshot.ApplyTo(targetClip);
            EditorUtility.SetDirty(targetClip);

            string verifyError = snapshot.Verify(targetClip);
            if (!string.IsNullOrEmpty(verifyError))
            {
                throw new InvalidOperationException(verifyError);
            }

            Undo.CollapseUndoOperations(undoGroup);
            SaveOnlyThisAsset(targetClip);

            string savedVerifyError = snapshot.Verify(targetClip);
            if (!string.IsNullOrEmpty(savedVerifyError))
            {
                throw new InvalidOperationException(savedVerifyError);
            }

            FinishSuccessfulSave(
                sourceWindow,
                targetClip,
                targetPath,
                snapshot,
                true);
            return true;
        }
        catch (Exception exception)
        {
            Undo.RevertAllDownToGroup(undoGroup);
            EditorUtility.SetDirty(targetClip);
            SaveOnlyThisAsset(targetClip);
            EditorUtility.DisplayDialog(
                "回写源 .anim 失败",
                "本次写入已经自动撤销，源动画保持原样。\n\n" + exception.Message,
                "知道了");
            return false;
        }
    }

    internal static bool ValidateTarget(
        AnimationClip runtimeClip,
        AnimationClip targetClip,
        out string error)
    {
        if (!CanOperate())
        {
            error = "当前已不在稳定的运行模式中，运行时曲线可能已经失效。";
            return false;
        }

        if (runtimeClip == null)
        {
            error = "当前运行时 AnimationClip 已失效，请重新进入运行模式后再调试。";
            return false;
        }

        if (targetClip == null)
        {
            error = "请选择要写入的源 .anim 文件。";
            return false;
        }

        if (!string.Equals(
                runtimeClip.name,
                targetClip.name,
                StringComparison.Ordinal))
        {
            error = string.Format(
                "目标名称必须与当前动效完全一致。\n当前：{0}\n目标：{1}",
                runtimeClip.name,
                targetClip.name);
            return false;
        }

        string path = AssetDatabase.GetAssetPath(targetClip);
        AnimationClip mainClip = GetStandaloneWritableMainClip(targetClip);
        if (mainClip != targetClip)
        {
            error = string.IsNullOrEmpty(path)
                ? "请选择 Project 中的源 .anim 文件，不能选择另一个运行时实例。"
                : "目标必须是可写的独立 .anim 文件；FBX 内嵌动画和只读文件不能直接覆盖。\n" + path;
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static bool CanOperate()
    {
        return EditorApplication.isPlaying
            && !isChangingPlayMode
            && !EditorApplication.isCompiling
            && !EditorApplication.isUpdating;
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingEditMode
            || state == PlayModeStateChange.ExitingPlayMode)
        {
            isChangingPlayMode = true;
            return;
        }

        if (state == PlayModeStateChange.EnteredEditMode
            || state == PlayModeStateChange.EnteredPlayMode)
        {
            isChangingPlayMode = false;
        }
    }

    private static AnimationClip GetStandaloneWritableMainClip(
        AnimationClip clip)
    {
        if (clip == null)
        {
            return null;
        }

        string path = AssetDatabase.GetAssetPath(clip);
        if (string.IsNullOrEmpty(path)
            || !path.EndsWith(".anim", StringComparison.OrdinalIgnoreCase)
            || !IsWritableAssetFile(path))
        {
            return null;
        }

        return AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
    }

    private static bool IsWritableAssetFile(string assetPath)
    {
        try
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string absolutePath = Path.GetFullPath(
                Path.Combine(
                    projectRoot,
                    assetPath.Replace('/', Path.DirectorySeparatorChar)));
            var file = new FileInfo(absolutePath);
            return file.Exists && !file.IsReadOnly;
        }
        catch
        {
            return false;
        }
    }

    private static List<PlayModeAnimationCandidate> FindSourceCandidates(
        string clipName,
        GameObject contextRoot)
    {
        var dependencyPaths = CollectContextDependencyPaths(contextRoot);
        var candidates = new List<PlayModeAnimationCandidate>();
        var visitedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddCandidatesFromSearch(
            candidates,
            visitedPaths,
            dependencyPaths,
            clipName,
            AssetDatabase.IsValidFolder(UIAssetRoot)
                ? new[] { UIAssetRoot }
                : null);

        if (candidates.Count == 0)
        {
            AddCandidatesFromSearch(
                candidates,
                visitedPaths,
                dependencyPaths,
                clipName,
                null);
        }

        return candidates
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void AddCandidatesFromSearch(
        ICollection<PlayModeAnimationCandidate> candidates,
        ISet<string> visitedPaths,
        ISet<string> dependencyPaths,
        string clipName,
        string[] searchFolders)
    {
        string[] guids = searchFolders == null
            ? AssetDatabase.FindAssets(clipName + " t:AnimationClip")
            : AssetDatabase.FindAssets(
                clipName + " t:AnimationClip",
                searchFolders);

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!visitedPaths.Add(path)
                || !path.EndsWith(".anim", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            AnimationClip candidate = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (candidate == null
                || !string.Equals(
                    candidate.name,
                    clipName,
                    StringComparison.Ordinal)
                || GetStandaloneWritableMainClip(candidate) != candidate)
            {
                continue;
            }

            int score = dependencyPaths.Contains(path) ? 100 : 0;
            if (path.StartsWith(UIAssetRoot + "/", StringComparison.OrdinalIgnoreCase))
            {
                score += 1;
            }

            candidates.Add(new PlayModeAnimationCandidate(candidate, path, score));
        }
    }

    private static AnimationClip ResolvePreferredTarget(
        IList<PlayModeAnimationCandidate> candidates)
    {
        if (candidates == null || candidates.Count == 0)
        {
            return null;
        }

        if (candidates.Count == 1)
        {
            return candidates[0].Clip;
        }

        PlayModeAnimationCandidate first = candidates[0];
        PlayModeAnimationCandidate second = candidates[1];
        return first.Score >= 100 && first.Score > second.Score
            ? first.Clip
            : null;
    }

    private static HashSet<string> CollectContextDependencyPaths(
        GameObject contextRoot)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (contextRoot == null)
        {
            return paths;
        }

        string nativePrefabPath = FindNativePrefabPath(contextRoot);
        AddAssetDependencies(paths, nativePrefabPath);

        foreach (string cloneName in CollectClonePrefabNames(contextRoot))
        {
            foreach (string prefabPath in FindExactPrefabPaths(cloneName))
            {
                AddAssetDependencies(paths, prefabPath);
            }
        }

        var animatorIds = new HashSet<int>();
        foreach (Animator animator in contextRoot.GetComponentsInParent<Animator>(true))
        {
            AddAnimatorDependencies(paths, animatorIds, animator);
        }

        foreach (Animator animator in contextRoot.GetComponentsInChildren<Animator>(true))
        {
            AddAnimatorDependencies(paths, animatorIds, animator);
        }

        return paths;
    }

    private static void AddAnimatorDependencies(
        ISet<string> paths,
        ISet<int> animatorIds,
        Animator animator)
    {
        if (animator == null || !animatorIds.Add(animator.GetInstanceID()))
        {
            return;
        }

        string controllerPath = AssetDatabase.GetAssetPath(
            animator.runtimeAnimatorController);
        AddAssetDependencies(paths, controllerPath);
    }

    private static string FindNativePrefabPath(GameObject target)
    {
        for (Transform current = target.transform;
             current != null;
             current = current.parent)
        {
            GameObject source =
                PrefabUtility.GetCorrespondingObjectFromSource(current.gameObject);
            string sourcePath = AssetDatabase.GetAssetPath(source);
            if (!string.IsNullOrEmpty(sourcePath)
                && sourcePath.EndsWith(
                    ".prefab",
                    StringComparison.OrdinalIgnoreCase))
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

    private static IEnumerable<string> CollectClonePrefabNames(GameObject target)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (Transform current = target.transform;
             current != null;
             current = current.parent)
        {
            if (!current.name.EndsWith(CloneSuffix, StringComparison.Ordinal))
            {
                continue;
            }

            string name = current.name.Substring(
                0,
                current.name.Length - CloneSuffix.Length).Trim();
            if (!string.IsNullOrEmpty(name))
            {
                names.Add(name);
            }
        }

        return names;
    }

    private static IEnumerable<string> FindExactPrefabPaths(string prefabName)
    {
        string[] folders = AssetDatabase.IsValidFolder(UIPrefabRoot)
            ? new[] { UIPrefabRoot }
            : null;
        string[] guids = folders == null
            ? AssetDatabase.FindAssets(prefabName + " t:Prefab")
            : AssetDatabase.FindAssets(prefabName + " t:Prefab", folders);

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.Equals(
                Path.GetFileNameWithoutExtension(path),
                prefabName,
                StringComparison.OrdinalIgnoreCase))
            {
                yield return path;
            }
        }
    }

    private static void AddAssetDependencies(
        ISet<string> paths,
        string assetPath)
    {
        if (string.IsNullOrEmpty(assetPath))
        {
            return;
        }

        foreach (string dependency in AssetDatabase.GetDependencies(assetPath, true))
        {
            paths.Add(dependency);
        }
    }

    private static void SaveOnlyThisAsset(UnityEngine.Object asset)
    {
        if (SaveAssetIfDirtyMethod != null)
        {
            SaveAssetIfDirtyMethod.Invoke(null, new object[] { asset });
            return;
        }

        AssetDatabase.SaveAssets();
    }

    private static void FinishSuccessfulSave(
        EditorWindow sourceWindow,
        AnimationClip targetClip,
        string targetPath,
        PlayModeAnimationClipSnapshot snapshot,
        bool copiedFromRuntimeInstance)
    {
        PlayModeAnimationWindowAccess.ForceRefreshOpenWindows();
        string notification = copiedFromRuntimeInstance
            ? "✓ 已回写并保存源 .anim"
            : "✓ 当前 .anim 已保存";
        if (sourceWindow != null)
        {
            sourceWindow.ShowNotification(new GUIContent(notification));
        }

        EditorGUIUtility.PingObject(targetClip);
        Debug.Log(string.Format(
            "[PlayModeAnimationSaver] {0}：{1}（{2} 条属性曲线，{3} 条对象曲线）",
            notification.TrimStart('✓', ' '),
            targetPath,
            snapshot.FloatCurveCount,
            snapshot.ObjectCurveCount));
    }
}

internal sealed class PlayModeAnimationTargetWindow : EditorWindow
{
    private AnimationClip runtimeClip;
    private AnimationClip targetClip;
    private EditorWindow sourceAnimationWindow;
    private List<PlayModeAnimationCandidate> candidates =
        new List<PlayModeAnimationCandidate>();
    private int candidateIndex = -1;

    internal static void Show(
        AnimationClip runtimeClip,
        EditorWindow sourceAnimationWindow,
        List<PlayModeAnimationCandidate> candidates)
    {
        var window = GetWindow<PlayModeAnimationTargetWindow>(true);
        window.titleContent = new GUIContent("选择源 .anim");
        window.minSize = new Vector2(560f, 235f);
        window.maxSize = new Vector2(900f, 360f);
        window.runtimeClip = runtimeClip;
        window.sourceAnimationWindow = sourceAnimationWindow;
        window.candidates = candidates ?? new List<PlayModeAnimationCandidate>();
        window.candidateIndex = -1;
        window.targetClip = null;
        window.ShowUtility();
    }

    private void OnGUI()
    {
        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("保存运行模式动效", EditorStyles.boldLabel);
        EditorGUILayout.LabelField(
            "当前 AnimationClip",
            runtimeClip != null ? runtimeClip.name : "已失效");

        if (candidates.Count > 0)
        {
            string[] labels = new string[candidates.Count + 1];
            labels[0] = "请选择源 .anim…";
            for (int i = 0; i < candidates.Count; i++)
            {
                labels[i + 1] = candidates[i].Path;
            }

            int popupIndex = EditorGUILayout.Popup(
                "自动匹配结果",
                candidateIndex + 1,
                labels);
            if (popupIndex - 1 != candidateIndex)
            {
                candidateIndex = popupIndex - 1;
                targetClip = candidateIndex >= 0
                    ? candidates[candidateIndex].Clip
                    : null;
            }
        }
        else
        {
            EditorGUILayout.HelpBox(
                "没有找到同名且可写的独立 .anim。请从 Project 拖入正确的源动画；FBX 内嵌动画不能直接覆盖。",
                MessageType.Warning);
        }

        AnimationClip newTarget = EditorGUILayout.ObjectField(
            "写入目标",
            targetClip,
            typeof(AnimationClip),
            false) as AnimationClip;
        if (newTarget != targetClip)
        {
            targetClip = newTarget;
            candidateIndex = candidates.FindIndex(
                candidate => candidate.Clip == targetClip);
        }

        string error;
        bool isValid = PlayModeAnimationSaver.ValidateTarget(
            runtimeClip,
            targetClip,
            out error);
        if (targetClip != null)
        {
            EditorGUILayout.SelectableLabel(
                AssetDatabase.GetAssetPath(targetClip),
                EditorStyles.textField,
                GUILayout.Height(EditorGUIUtility.singleLineHeight));
        }

        if (!isValid)
        {
            EditorGUILayout.HelpBox(error, MessageType.Info);
        }
        else
        {
            EditorGUILayout.HelpBox(
                "将用当前运行时曲线完整覆盖这个源 .anim。支持 Ctrl+Z 撤销。",
                MessageType.Info);
        }

        GUILayout.FlexibleSpace();
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("取消", GUILayout.Height(30f)))
            {
                Close();
            }

            using (new EditorGUI.DisabledScope(!isValid))
            {
                if (GUILayout.Button("保存到源 .anim", GUILayout.Height(30f))
                    && PlayModeAnimationSaver.TrySave(
                        runtimeClip,
                        targetClip,
                        sourceAnimationWindow))
                {
                    Close();
                }
            }
        }

        EditorGUILayout.Space(8f);
    }
}

internal sealed class PlayModeAnimationCandidate
{
    internal PlayModeAnimationCandidate(
        AnimationClip clip,
        string path,
        int score)
    {
        Clip = clip;
        Path = path;
        Score = score;
    }

    internal AnimationClip Clip { get; private set; }
    internal string Path { get; private set; }
    internal int Score { get; private set; }
}

internal sealed class PlayModeAnimationClipSnapshot
{
    private readonly List<FloatCurveSnapshot> floatCurves;
    private readonly List<ObjectCurveSnapshot> objectCurves;
    private readonly AnimationEvent[] events;
    private readonly AnimationClipSettings settings;
    private readonly float frameRate;
    private readonly WrapMode wrapMode;

    private PlayModeAnimationClipSnapshot(
        List<FloatCurveSnapshot> floatCurves,
        List<ObjectCurveSnapshot> objectCurves,
        AnimationEvent[] events,
        AnimationClipSettings settings,
        float frameRate,
        WrapMode wrapMode)
    {
        this.floatCurves = floatCurves;
        this.objectCurves = objectCurves;
        this.events = events;
        this.settings = settings;
        this.frameRate = frameRate;
        this.wrapMode = wrapMode;
    }

    internal int FloatCurveCount
    {
        get { return floatCurves.Count; }
    }

    internal int ObjectCurveCount
    {
        get { return objectCurves.Count; }
    }

    internal static PlayModeAnimationClipSnapshot Capture(AnimationClip clip)
    {
        var floatSnapshots = new List<FloatCurveSnapshot>();
        foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(clip))
        {
            floatSnapshots.Add(new FloatCurveSnapshot(
                binding,
                AnimationUtility.GetEditorCurve(clip, binding)));
        }

        var objectSnapshots = new List<ObjectCurveSnapshot>();
        foreach (EditorCurveBinding binding in
                 AnimationUtility.GetObjectReferenceCurveBindings(clip))
        {
            ObjectReferenceKeyframe[] keyframes =
                AnimationUtility.GetObjectReferenceCurve(clip, binding)
                ?? Array.Empty<ObjectReferenceKeyframe>();
            var copiedKeyframes = new ObjectReferenceKeyframe[keyframes.Length];
            Array.Copy(keyframes, copiedKeyframes, keyframes.Length);
            objectSnapshots.Add(new ObjectCurveSnapshot(
                binding,
                copiedKeyframes));
        }

        return new PlayModeAnimationClipSnapshot(
            floatSnapshots,
            objectSnapshots,
            CopyEvents(AnimationUtility.GetAnimationEvents(clip)),
            AnimationUtility.GetAnimationClipSettings(clip),
            clip.frameRate,
            clip.wrapMode);
    }

    internal void ApplyTo(AnimationClip target)
    {
        foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(target))
        {
            AnimationUtility.SetEditorCurve(target, binding, null);
        }

        foreach (EditorCurveBinding binding in
                 AnimationUtility.GetObjectReferenceCurveBindings(target))
        {
            AnimationUtility.SetObjectReferenceCurve(target, binding, null);
        }

        foreach (FloatCurveSnapshot curve in floatCurves)
        {
            AnimationUtility.SetEditorCurve(target, curve.Binding, curve.Curve);
        }

        foreach (ObjectCurveSnapshot curve in objectCurves)
        {
            AnimationUtility.SetObjectReferenceCurve(
                target,
                curve.Binding,
                curve.Keyframes);
        }

        target.frameRate = frameRate;
        target.wrapMode = wrapMode;
        AnimationUtility.SetAnimationClipSettings(target, settings);
        AnimationUtility.SetAnimationEvents(target, CopyEvents(events));
    }

    internal string Verify(AnimationClip target)
    {
        EditorCurveBinding[] savedFloatBindings =
            AnimationUtility.GetCurveBindings(target);
        if (savedFloatBindings.Length != floatCurves.Count)
        {
            return string.Format(
                "属性曲线数量不一致：应为 {0}，实际为 {1}。",
                floatCurves.Count,
                savedFloatBindings.Length);
        }

        foreach (FloatCurveSnapshot expected in floatCurves)
        {
            AnimationCurve actual = AnimationUtility.GetEditorCurve(
                target,
                expected.Binding);
            if (!AreCurvesEquivalent(expected.Curve, actual))
            {
                return "属性曲线回读不一致：" + FormatBinding(expected.Binding);
            }
        }

        EditorCurveBinding[] savedObjectBindings =
            AnimationUtility.GetObjectReferenceCurveBindings(target);
        if (savedObjectBindings.Length != objectCurves.Count)
        {
            return string.Format(
                "对象曲线数量不一致：应为 {0}，实际为 {1}。",
                objectCurves.Count,
                savedObjectBindings.Length);
        }

        foreach (ObjectCurveSnapshot expected in objectCurves)
        {
            ObjectReferenceKeyframe[] actual =
                AnimationUtility.GetObjectReferenceCurve(
                    target,
                    expected.Binding);
            if (!AreObjectKeyframesEquivalent(expected.Keyframes, actual))
            {
                return "对象曲线回读不一致：" + FormatBinding(expected.Binding);
            }
        }

        if (!Mathf.Approximately(frameRate, target.frameRate)
            || wrapMode != target.wrapMode)
        {
            return "AnimationClip 的帧率或 Wrap Mode 回读不一致。";
        }

        if (!AreEventsEquivalent(
                events,
                AnimationUtility.GetAnimationEvents(target)))
        {
            return "Animation Events 回读不一致。";
        }

        return string.Empty;
    }

    private static AnimationEvent[] CopyEvents(AnimationEvent[] source)
    {
        source = source ?? Array.Empty<AnimationEvent>();
        var result = new AnimationEvent[source.Length];
        for (int i = 0; i < source.Length; i++)
        {
            AnimationEvent item = source[i];
            result[i] = new AnimationEvent
            {
                time = item.time,
                functionName = item.functionName,
                stringParameter = item.stringParameter,
                floatParameter = item.floatParameter,
                intParameter = item.intParameter,
                objectReferenceParameter = item.objectReferenceParameter,
                messageOptions = item.messageOptions
            };
        }

        return result;
    }

    private static bool AreCurvesEquivalent(
        AnimationCurve expected,
        AnimationCurve actual)
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
            Keyframe left = expected.keys[i];
            Keyframe right = actual.keys[i];
            if (!Mathf.Approximately(left.time, right.time)
                || !Mathf.Approximately(left.value, right.value)
                || !Mathf.Approximately(left.inTangent, right.inTangent)
                || !Mathf.Approximately(left.outTangent, right.outTangent)
                || !Mathf.Approximately(left.inWeight, right.inWeight)
                || !Mathf.Approximately(left.outWeight, right.outWeight)
                || left.weightedMode != right.weightedMode
                || AnimationUtility.GetKeyBroken(expected, i)
                    != AnimationUtility.GetKeyBroken(actual, i)
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

    private static bool AreEventsEquivalent(
        AnimationEvent[] expected,
        AnimationEvent[] actual)
    {
        expected = expected ?? Array.Empty<AnimationEvent>();
        actual = actual ?? Array.Empty<AnimationEvent>();
        if (expected.Length != actual.Length)
        {
            return false;
        }

        for (int i = 0; i < expected.Length; i++)
        {
            AnimationEvent left = expected[i];
            AnimationEvent right = actual[i];
            if (!Mathf.Approximately(left.time, right.time)
                || !string.Equals(
                    left.functionName,
                    right.functionName,
                    StringComparison.Ordinal)
                || !string.Equals(
                    left.stringParameter,
                    right.stringParameter,
                    StringComparison.Ordinal)
                || !Mathf.Approximately(
                    left.floatParameter,
                    right.floatParameter)
                || left.intParameter != right.intParameter
                || left.objectReferenceParameter
                    != right.objectReferenceParameter
                || left.messageOptions != right.messageOptions)
            {
                return false;
            }
        }

        return true;
    }

    private static string FormatBinding(EditorCurveBinding binding)
    {
        return string.Format(
            "{0} / {1} / {2}",
            binding.path,
            binding.type != null ? binding.type.Name : "Unknown",
            binding.propertyName);
    }

    private sealed class FloatCurveSnapshot
    {
        internal FloatCurveSnapshot(
            EditorCurveBinding binding,
            AnimationCurve curve)
        {
            Binding = binding;
            Curve = curve;
        }

        internal EditorCurveBinding Binding { get; private set; }
        internal AnimationCurve Curve { get; private set; }
    }

    private sealed class ObjectCurveSnapshot
    {
        internal ObjectCurveSnapshot(
            EditorCurveBinding binding,
            ObjectReferenceKeyframe[] keyframes)
        {
            Binding = binding;
            Keyframes = keyframes;
        }

        internal EditorCurveBinding Binding { get; private set; }
        internal ObjectReferenceKeyframe[] Keyframes { get; private set; }
    }
}

internal static class PlayModeAnimationWindowAccess
{
    private const BindingFlags Flags =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static Type animationWindowType;

    private static Type AnimationWindowType
    {
        get
        {
            if (animationWindowType == null)
            {
                animationWindowType = typeof(EditorWindow).Assembly.GetType(
                    "UnityEditor.AnimationWindow");
            }

            return animationWindowType;
        }
    }

    internal static PlayModeAnimationContext GetCurrentContext()
    {
        EditorWindow focused = EditorWindow.focusedWindow;
        Type type = AnimationWindowType;
        if (type != null
            && focused != null
            && type.IsInstanceOfType(focused))
        {
            PlayModeAnimationContext focusedContext = GetContext(focused);
            if (focusedContext.Clip != null || focusedContext.Root != null)
            {
                return focusedContext;
            }
        }

        foreach (EditorWindow window in GetOpenAnimationWindows())
        {
            PlayModeAnimationContext context = GetContext(window);
            if (context.Clip != null || context.Root != null)
            {
                return context;
            }
        }

        return new PlayModeAnimationContext();
    }

    internal static void ForceRefreshOpenWindows()
    {
        Type type = AnimationWindowType;
        if (type == null)
        {
            return;
        }

        MethodInfo forceRefresh = type.GetMethod(
            "ForceRefresh",
            Flags,
            null,
            Type.EmptyTypes,
            null);
        foreach (EditorWindow window in GetOpenAnimationWindows())
        {
            try
            {
                if (forceRefresh != null)
                {
                    forceRefresh.Invoke(window, null);
                }

                window.Repaint();
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "[PlayModeAnimationSaver] 刷新 Animation 窗口失败：" +
                    exception.Message);
            }
        }
    }

    private static IEnumerable<EditorWindow> GetOpenAnimationWindows()
    {
        Type type = AnimationWindowType;
        if (type == null)
        {
            yield break;
        }

        foreach (UnityEngine.Object item in Resources.FindObjectsOfTypeAll(type))
        {
            EditorWindow window = item as EditorWindow;
            if (window != null)
            {
                yield return window;
            }
        }
    }

    private static PlayModeAnimationContext GetContext(EditorWindow window)
    {
        object state = GetProperty<object>(window, "state");
        if (state == null)
        {
            return new PlayModeAnimationContext();
        }

        return new PlayModeAnimationContext
        {
            Window = window,
            Clip = GetActiveClip(state),
            Root = GetActiveRoot(state)
        };
    }

    private static AnimationClip GetActiveClip(object state)
    {
        AnimationClip clip = GetProperty<AnimationClip>(
            state,
            "activeAnimationClip");
        if (clip != null)
        {
            return clip;
        }

        clip = GetClipFromCurves(state, "activeCurves");
        return clip != null ? clip : GetClipFromCurves(state, "allCurves");
    }

    private static AnimationClip GetClipFromCurves(
        object state,
        string propertyName)
    {
        var curves = GetProperty<object>(state, propertyName)
            as System.Collections.IEnumerable;
        if (curves == null)
        {
            return null;
        }

        foreach (object curve in curves)
        {
            AnimationClip clip = GetProperty<AnimationClip>(curve, "clip");
            if (clip != null)
            {
                return clip;
            }
        }

        return null;
    }

    private static GameObject GetActiveRoot(object state)
    {
        GameObject root = GetGameObjectProperty(
            state,
            "activeRootGameObject");
        if (root != null)
        {
            return root;
        }

        root = GetGameObjectProperty(state, "rootGameObject");
        if (root != null)
        {
            return root;
        }

        GameObject active = GetGameObjectProperty(state, "activeGameObject");
        return active != null ? active.transform.root.gameObject : null;
    }

    private static GameObject GetGameObjectProperty(
        object target,
        string propertyName)
    {
        UnityEngine.Object value = GetProperty<UnityEngine.Object>(
            target,
            propertyName);
        GameObject gameObject = value as GameObject;
        if (gameObject != null)
        {
            return gameObject;
        }

        Component component = value as Component;
        return component != null ? component.gameObject : null;
    }

    private static T GetProperty<T>(object target, string propertyName)
        where T : class
    {
        if (target == null)
        {
            return null;
        }

        PropertyInfo property = target.GetType().GetProperty(
            propertyName,
            Flags);
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

internal struct PlayModeAnimationContext
{
    internal EditorWindow Window;
    internal AnimationClip Clip;
    internal GameObject Root;
}
