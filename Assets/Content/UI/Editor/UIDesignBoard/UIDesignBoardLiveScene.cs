using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

internal sealed class UIDesignBoardLiveItem
{
    public string Guid;
    public string PrefabPath;
    public string Title;
    public Vector2 Position;
}

[InitializeOnLoad]
internal static class UIDesignBoardLiveScene
{
    private const string SceneName = "UI Design Board Live";
    private const string RootName = "__UI_DESIGN_BOARD_LIVE_ROOT__";
    private const string WrapperGuidToken = " [UIBoard:";
    private const float ArtboardWidth = 1920f;
    private const float ArtboardHeight = 1080f;
    private const float BoardToWorldX = 5.8f;
    private const float BoardToWorldY = 4.8f;

    private static readonly Color FrameColor = new Color(0.34f, 0.67f, 1f, 0.82f);
    private static readonly Color SelectedFrameColor = new Color(1f, 0.72f, 0.24f, 1f);
    private static GUIStyle artboardLabelStyle;
    private static string lastStatus = string.Empty;

    internal static event Action StateChanged;

    internal static string LastStatus
    {
        get { return lastStatus; }
    }

    internal static bool IsOpen
    {
        get { return TryGetLiveScene(out _, out _); }
    }

    internal static bool IsActive
    {
        get
        {
            if (PrefabStageUtility.GetCurrentPrefabStage() != null)
            {
                return false;
            }

            return TryGetLiveScene(out Scene scene, out _) &&
                   SceneManager.GetActiveScene() == scene;
        }
    }

    static UIDesignBoardLiveScene()
    {
        SceneView.duringSceneGui -= OnSceneGUI;
        SceneView.duringSceneGui += OnSceneGUI;
        Selection.selectionChanged -= OnSelectionChanged;
        Selection.selectionChanged += OnSelectionChanged;
        EditorSceneManager.sceneClosed -= OnSceneClosed;
        EditorSceneManager.sceneClosed += OnSceneClosed;
        EditorApplication.delayCall += RestoreOpenBoardEnvironment;
    }

    internal static bool OpenOrSync(
        IList<UIDesignBoardLiveItem> items,
        string focusGuid,
        out string message)
    {
        message = string.Empty;
        if (items == null || items.Count == 0)
        {
            message = "Add at least one prefab to the board first.";
            SetStatus(message);
            return false;
        }

        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            message = "Exit Play Mode before opening Live Board.";
            SetStatus(message);
            return false;
        }

        PrefabStage prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
        if (prefabStage != null)
        {
            StageUtility.GoToMainStage();
        }

        bool createdScene = false;
        if (!TryGetLiveScene(out Scene scene, out GameObject boardRoot))
        {
            scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            scene.name = SceneName;
            boardRoot = new GameObject(RootName)
            {
                hideFlags = HideFlags.DontSaveInBuild
            };
            SceneManager.MoveGameObjectToScene(boardRoot, scene);
            createdScene = true;
        }

        SceneManager.SetActiveScene(scene);
        int added = SynchronizeBoard(scene, boardRoot, items);
        NormalizeAllArtboards(boardRoot);

        ConfigureSceneView();
        string targetGuid = !string.IsNullOrEmpty(focusGuid)
            ? focusGuid
            : items[0].Guid;
        FocusArtboard(targetGuid, out _);

        message = createdScene
            ? "Live Board opened with " + items.Count + " artboard(s)."
            : added > 0
                ? "Live Board synchronized. Added " + added + " artboard(s)."
                : "Live Board is ready.";
        SetStatus(message);
        NotifyStateChanged();
        return true;
    }

    internal static bool AddOrSyncArtboard(UIDesignBoardLiveItem item)
    {
        if (item == null || !TryGetLiveScene(out Scene scene, out GameObject boardRoot))
        {
            return false;
        }

        GameObject wrapper = FindWrapper(boardRoot, item.Guid);
        if (wrapper == null)
        {
            wrapper = CreateArtboard(scene, boardRoot, item);
        }
        else
        {
            UpdateWrapper(wrapper, item);
            NormalizeArtboard(GetPrefabInstanceRoot(wrapper));
        }

        NotifyStateChanged();
        return wrapper != null;
    }

    internal static void UpdateArtboardPosition(string guid, Vector2 boardPosition)
    {
        if (!TryGetLiveScene(out _, out GameObject boardRoot))
        {
            return;
        }

        GameObject wrapper = FindWrapper(boardRoot, guid);
        if (wrapper == null)
        {
            return;
        }

        wrapper.transform.localPosition = BoardToWorld(boardPosition);
        SceneView.RepaintAll();
    }

    internal static bool FocusArtboard(string guid, out string message)
    {
        message = string.Empty;
        if (!TryGetLiveScene(out Scene scene, out GameObject boardRoot))
        {
            message = "Live Board is not open.";
            return false;
        }

        GameObject wrapper = FindWrapper(boardRoot, guid);
        GameObject instanceRoot = GetPrefabInstanceRoot(wrapper);
        if (wrapper == null || instanceRoot == null)
        {
            message = "Artboard is not available in Live Board.";
            return false;
        }

        SceneManager.SetActiveScene(scene);
        Selection.activeGameObject = instanceRoot;
        ConfigureSceneView();

        SceneView sceneView = SceneView.lastActiveSceneView;
        if (sceneView != null)
        {
            sceneView.Frame(GetArtboardBounds(wrapper), false);
            sceneView.Focus();
            sceneView.Repaint();
        }

        message = "Focused " + GetWrapperTitle(wrapper) + ".";
        SetStatus(message);
        NotifyStateChanged();
        return true;
    }

    internal static bool ApplyArtboard(string guid, out string message)
    {
        message = string.Empty;
        GameObject instanceRoot = GetInstanceRoot(guid);
        if (instanceRoot == null)
        {
            message = "Select an artboard in Live Board first.";
            return false;
        }

        if (!HasUserOverrides(instanceRoot))
        {
            message = "No unapplied changes on this artboard.";
            SetStatus(message);
            return true;
        }

        Transform wrapperParent = instanceRoot.transform.parent;
        try
        {
            instanceRoot.transform.SetParent(null, false);
            RestoreSourceEnvironment(instanceRoot);
            PrefabUtility.ApplyPrefabInstance(instanceRoot, InteractionMode.UserAction);
            AssetDatabase.SaveAssets();
            message = "Applied " + instanceRoot.name + " to its source prefab.";
            SetStatus(message);
            NotifyStateChanged();
            return true;
        }
        catch (Exception exception)
        {
            message = "Apply failed: " + exception.Message;
            Debug.LogException(exception);
            SetStatus(message);
            return false;
        }
        finally
        {
            if (instanceRoot != null && wrapperParent != null)
            {
                instanceRoot.transform.SetParent(wrapperParent, false);
                NormalizeArtboard(instanceRoot);
            }
        }
    }

    internal static bool RevertArtboard(string guid, bool askForConfirmation, out string message)
    {
        message = string.Empty;
        GameObject instanceRoot = GetInstanceRoot(guid);
        if (instanceRoot == null)
        {
            message = "Select an artboard in Live Board first.";
            return false;
        }

        if (!HasUserOverrides(instanceRoot))
        {
            message = "No unapplied changes on this artboard.";
            SetStatus(message);
            return true;
        }

        if (askForConfirmation && !EditorUtility.DisplayDialog(
                "Revert Live Artboard",
                "Discard all unapplied changes on " + instanceRoot.name + "?",
                "Revert",
                "Cancel"))
        {
            message = "Revert canceled.";
            return false;
        }

        try
        {
            PrefabUtility.RevertPrefabInstance(instanceRoot, InteractionMode.UserAction);
            NormalizeArtboard(instanceRoot);
            message = "Reverted " + instanceRoot.name + ".";
            SetStatus(message);
            NotifyStateChanged();
            return true;
        }
        catch (Exception exception)
        {
            NormalizeArtboard(instanceRoot);
            message = "Revert failed: " + exception.Message;
            Debug.LogException(exception);
            SetStatus(message);
            return false;
        }
    }

    internal static bool RemoveArtboard(string guid, out string message)
    {
        message = string.Empty;
        if (!TryGetLiveScene(out _, out GameObject boardRoot))
        {
            return true;
        }

        GameObject wrapper = FindWrapper(boardRoot, guid);
        if (wrapper == null)
        {
            return true;
        }

        GameObject instanceRoot = GetPrefabInstanceRoot(wrapper);
        if (instanceRoot != null && HasUserOverrides(instanceRoot))
        {
            int choice = EditorUtility.DisplayDialogComplex(
                "Remove Live Artboard",
                GetWrapperTitle(wrapper) + " has unapplied changes.",
                "Apply and Remove",
                "Cancel",
                "Discard and Remove");
            if (choice == 1)
            {
                message = "Remove canceled.";
                return false;
            }

            if (choice == 0 && !ApplyArtboard(guid, out message))
            {
                return false;
            }
        }

        UnityEngine.Object.DestroyImmediate(wrapper);
        message = "Removed artboard from Live Board.";
        SetStatus(message);
        NotifyStateChanged();
        return true;
    }

    internal static bool Close(out string message)
    {
        message = string.Empty;
        if (!TryGetLiveScene(out Scene scene, out GameObject boardRoot))
        {
            message = "Live Board is already closed.";
            return true;
        }

        List<string> changedGuids = CollectChangedArtboardGuids(boardRoot);
        if (changedGuids.Count > 0)
        {
            int choice = EditorUtility.DisplayDialogComplex(
                "Close Live Board",
                changedGuids.Count + " artboard(s) have unapplied changes.",
                "Apply All and Close",
                "Cancel",
                "Discard and Close");
            if (choice == 1)
            {
                message = "Close canceled.";
                return false;
            }

            if (choice == 0)
            {
                for (int i = 0; i < changedGuids.Count; i++)
                {
                    if (!ApplyArtboard(changedGuids[i], out message))
                    {
                        return false;
                    }
                }
            }
        }

        Selection.activeObject = null;
        SetFallbackActiveScene(scene);
        Scene sceneToClose = scene;
        EditorApplication.delayCall += () =>
        {
            if (sceneToClose.IsValid() && sceneToClose.isLoaded)
            {
                EditorSceneManager.CloseScene(sceneToClose, true);
            }
        };
        message = "Live Board closed.";
        SetStatus(message);
        NotifyStateChanged();
        return true;
    }

    internal static bool TryGetActiveEditingRoot(out GameObject boardRoot)
    {
        boardRoot = null;
        if (!TryGetLiveScene(out Scene scene, out boardRoot))
        {
            return false;
        }

        return SceneManager.GetActiveScene() == scene;
    }

    internal static bool IsEditableObject(GameObject target)
    {
        if (target == null || !TryGetArtboardWrapper(target, out GameObject wrapper))
        {
            return false;
        }

        GameObject instanceRoot = GetPrefabInstanceRoot(wrapper);
        if (instanceRoot == null)
        {
            return false;
        }

        Transform targetTransform = target.transform;
        Transform instanceTransform = instanceRoot.transform;
        return targetTransform == instanceTransform || targetTransform.IsChildOf(instanceTransform);
    }

    internal static bool TryGetArtboardGuid(GameObject target, out string guid)
    {
        guid = string.Empty;
        if (!TryGetArtboardWrapper(target, out GameObject wrapper))
        {
            return false;
        }

        guid = GetWrapperGuid(wrapper);
        return !string.IsNullOrEmpty(guid);
    }

    private static int SynchronizeBoard(
        Scene scene,
        GameObject boardRoot,
        IList<UIDesignBoardLiveItem> items)
    {
        int added = 0;
        HashSet<string> boardGuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < items.Count; i++)
        {
            UIDesignBoardLiveItem item = items[i];
            if (item == null || string.IsNullOrEmpty(item.Guid))
            {
                continue;
            }

            boardGuids.Add(item.Guid);
            GameObject wrapper = FindWrapper(boardRoot, item.Guid);
            if (wrapper == null)
            {
                if (CreateArtboard(scene, boardRoot, item) != null)
                {
                    added++;
                }
            }
            else
            {
                UpdateWrapper(wrapper, item);
            }
        }

        List<GameObject> obsolete = new List<GameObject>();
        for (int i = 0; i < boardRoot.transform.childCount; i++)
        {
            GameObject wrapper = boardRoot.transform.GetChild(i).gameObject;
            string guid = GetWrapperGuid(wrapper);
            if (!string.IsNullOrEmpty(guid) && !boardGuids.Contains(guid))
            {
                GameObject instanceRoot = GetPrefabInstanceRoot(wrapper);
                if (instanceRoot == null || !HasUserOverrides(instanceRoot))
                {
                    obsolete.Add(wrapper);
                }
            }
        }

        for (int i = 0; i < obsolete.Count; i++)
        {
            UnityEngine.Object.DestroyImmediate(obsolete[i]);
        }

        return added;
    }

    private static GameObject CreateArtboard(
        Scene scene,
        GameObject boardRoot,
        UIDesignBoardLiveItem item)
    {
        GameObject prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(item.PrefabPath);
        if (prefabAsset == null)
        {
            Debug.LogWarning("[UI Design Board] Prefab is missing: " + item.PrefabPath);
            return null;
        }

        GameObject wrapper = new GameObject(
            BuildWrapperName(item.Title, item.Guid),
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler))
        {
            hideFlags = HideFlags.DontSaveInBuild
        };
        SceneManager.MoveGameObjectToScene(wrapper, scene);
        wrapper.transform.SetParent(boardRoot.transform, false);

        RectTransform wrapperRect = wrapper.GetComponent<RectTransform>();
        wrapperRect.anchorMin = new Vector2(0.5f, 0.5f);
        wrapperRect.anchorMax = new Vector2(0.5f, 0.5f);
        wrapperRect.pivot = new Vector2(0.5f, 0.5f);
        wrapperRect.sizeDelta = new Vector2(ArtboardWidth, ArtboardHeight);
        wrapperRect.localPosition = BoardToWorld(item.Position);
        wrapperRect.localRotation = Quaternion.identity;
        wrapperRect.localScale = Vector3.one;

        Canvas canvas = wrapper.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = null;
        canvas.pixelPerfect = false;

        CanvasScaler scaler = wrapper.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(ArtboardWidth, ArtboardHeight);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        GameObject instanceRoot = PrefabUtility.InstantiatePrefab(prefabAsset, scene) as GameObject;
        if (instanceRoot == null)
        {
            UnityEngine.Object.DestroyImmediate(wrapper);
            Debug.LogWarning("[UI Design Board] Could not instantiate prefab: " + item.PrefabPath);
            return null;
        }

        instanceRoot.transform.SetParent(wrapper.transform, false);
        NormalizeArtboard(instanceRoot);
        RebuildLayout(instanceRoot);
        return wrapper;
    }

    private static void UpdateWrapper(GameObject wrapper, UIDesignBoardLiveItem item)
    {
        if (wrapper == null || item == null)
        {
            return;
        }

        wrapper.name = BuildWrapperName(item.Title, item.Guid);
        wrapper.transform.localPosition = BoardToWorld(item.Position);
    }

    private static void NormalizeAllArtboards(GameObject boardRoot)
    {
        if (boardRoot == null)
        {
            return;
        }

        for (int i = 0; i < boardRoot.transform.childCount; i++)
        {
            NormalizeArtboard(GetPrefabInstanceRoot(boardRoot.transform.GetChild(i).gameObject));
        }

        Canvas.ForceUpdateCanvases();
    }

    private static void NormalizeArtboard(GameObject instanceRoot)
    {
        if (instanceRoot == null)
        {
            return;
        }

        Transform sourceRoot = PrefabUtility.GetCorrespondingObjectFromSource(instanceRoot.transform);
        if (sourceRoot != null && sourceRoot.localScale == Vector3.zero)
        {
            instanceRoot.transform.localScale = Vector3.one;
        }

        Canvas[] canvases = instanceRoot.GetComponentsInChildren<Canvas>(true);
        for (int i = 0; i < canvases.Length; i++)
        {
            Canvas canvas = canvases[i];
            Canvas sourceCanvas = PrefabUtility.GetCorrespondingObjectFromSource(canvas);
            if (sourceCanvas == null)
            {
                continue;
            }

            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = null;
        }

        RebuildLayout(instanceRoot);
    }

    private static void RestoreSourceEnvironment(GameObject instanceRoot)
    {
        if (instanceRoot == null)
        {
            return;
        }

        Transform sourceRoot = PrefabUtility.GetCorrespondingObjectFromSource(instanceRoot.transform);
        if (sourceRoot != null && sourceRoot.localScale == Vector3.zero)
        {
            instanceRoot.transform.localScale = sourceRoot.localScale;
        }

        Canvas[] canvases = instanceRoot.GetComponentsInChildren<Canvas>(true);
        for (int i = 0; i < canvases.Length; i++)
        {
            Canvas canvas = canvases[i];
            Canvas sourceCanvas = PrefabUtility.GetCorrespondingObjectFromSource(canvas);
            if (sourceCanvas == null)
            {
                continue;
            }

            canvas.renderMode = sourceCanvas.renderMode;
            canvas.worldCamera = sourceCanvas.worldCamera;
            RestoreRectTransform(
                sourceCanvas.transform as RectTransform,
                canvas.transform as RectTransform);
        }

        Canvas.ForceUpdateCanvases();
    }

    private static void RestoreRectTransform(RectTransform source, RectTransform target)
    {
        if (source == null || target == null)
        {
            return;
        }

        target.anchorMin = source.anchorMin;
        target.anchorMax = source.anchorMax;
        target.pivot = source.pivot;
        target.sizeDelta = source.sizeDelta;
        target.anchoredPosition3D = source.anchoredPosition3D;
        target.localRotation = source.localRotation;
        target.localScale = source.localScale;
    }

    private static bool HasUserOverrides(GameObject instanceRoot)
    {
        if (instanceRoot == null)
        {
            return false;
        }

        Transform wrapperParent = instanceRoot.transform.parent;
        try
        {
            instanceRoot.transform.SetParent(null, false);
            RestoreSourceEnvironment(instanceRoot);

            if (PrefabUtility.GetAddedComponents(instanceRoot).Count > 0 ||
                PrefabUtility.GetRemovedComponents(instanceRoot).Count > 0 ||
                PrefabUtility.GetAddedGameObjects(instanceRoot).Count > 0)
            {
                return true;
            }

            List<ObjectOverride> objectOverrides =
                PrefabUtility.GetObjectOverrides(instanceRoot, false);
            for (int i = 0; i < objectOverrides.Count; i++)
            {
                UnityEngine.Object changedObject = objectOverrides[i].instanceObject;
                if (IsEnvironmentOverride(instanceRoot, changedObject))
                {
                    continue;
                }

                return true;
            }

            return false;
        }
        finally
        {
            if (instanceRoot != null && wrapperParent != null)
            {
                instanceRoot.transform.SetParent(wrapperParent, false);
                NormalizeArtboard(instanceRoot);
            }
        }
    }

    private static bool IsEnvironmentOverride(
        GameObject instanceRoot,
        UnityEngine.Object changedObject)
    {
        if (changedObject == instanceRoot.transform || changedObject is Canvas)
        {
            return true;
        }

        RectTransform rectTransform = changedObject as RectTransform;
        return rectTransform != null &&
               (rectTransform.GetComponent<Canvas>() != null ||
                rectTransform.drivenByObject != null);
    }

    private static List<string> CollectChangedArtboardGuids(GameObject boardRoot)
    {
        List<string> guids = new List<string>();
        if (boardRoot == null)
        {
            return guids;
        }

        for (int i = 0; i < boardRoot.transform.childCount; i++)
        {
            GameObject wrapper = boardRoot.transform.GetChild(i).gameObject;
            GameObject instanceRoot = GetPrefabInstanceRoot(wrapper);
            if (HasUserOverrides(instanceRoot))
            {
                string guid = GetWrapperGuid(wrapper);
                if (!string.IsNullOrEmpty(guid))
                {
                    guids.Add(guid);
                }
            }
        }

        return guids;
    }

    private static GameObject GetInstanceRoot(string guid)
    {
        if (!TryGetLiveScene(out _, out GameObject boardRoot))
        {
            return null;
        }

        if (string.IsNullOrEmpty(guid) && TryGetArtboardGuid(Selection.activeGameObject, out string selectedGuid))
        {
            guid = selectedGuid;
        }

        return GetPrefabInstanceRoot(FindWrapper(boardRoot, guid));
    }

    private static GameObject GetPrefabInstanceRoot(GameObject wrapper)
    {
        if (wrapper == null)
        {
            return null;
        }

        for (int i = 0; i < wrapper.transform.childCount; i++)
        {
            GameObject child = wrapper.transform.GetChild(i).gameObject;
            GameObject instanceRoot = PrefabUtility.GetNearestPrefabInstanceRoot(child);
            if (instanceRoot == child)
            {
                return child;
            }
        }

        return null;
    }

    private static bool TryGetArtboardWrapper(GameObject target, out GameObject wrapper)
    {
        wrapper = null;
        if (target == null || !TryGetLiveScene(out Scene scene, out GameObject boardRoot))
        {
            return false;
        }

        if (target.scene != scene)
        {
            return false;
        }

        Transform rootTransform = boardRoot.transform;
        for (Transform current = target.transform; current != null; current = current.parent)
        {
            if (current.parent == rootTransform && !string.IsNullOrEmpty(GetWrapperGuid(current.gameObject)))
            {
                wrapper = current.gameObject;
                return true;
            }

            if (current == rootTransform)
            {
                break;
            }
        }

        return false;
    }

    private static bool TryGetLiveScene(out Scene scene, out GameObject boardRoot)
    {
        scene = default;
        boardRoot = null;
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene candidate = SceneManager.GetSceneAt(i);
            if (!candidate.IsValid() || !candidate.isLoaded)
            {
                continue;
            }

            GameObject[] roots = candidate.GetRootGameObjects();
            for (int r = 0; r < roots.Length; r++)
            {
                if (roots[r].name != RootName)
                {
                    continue;
                }

                scene = candidate;
                boardRoot = roots[r];
                return true;
            }
        }

        return false;
    }

    private static GameObject FindWrapper(GameObject boardRoot, string guid)
    {
        if (boardRoot == null || string.IsNullOrEmpty(guid))
        {
            return null;
        }

        for (int i = 0; i < boardRoot.transform.childCount; i++)
        {
            GameObject wrapper = boardRoot.transform.GetChild(i).gameObject;
            if (string.Equals(GetWrapperGuid(wrapper), guid, StringComparison.OrdinalIgnoreCase))
            {
                return wrapper;
            }
        }

        return null;
    }

    private static string BuildWrapperName(string title, string guid)
    {
        string safeTitle = string.IsNullOrEmpty(title) ? "Artboard" : title;
        return safeTitle + WrapperGuidToken + guid + "]";
    }

    private static string GetWrapperGuid(GameObject wrapper)
    {
        if (wrapper == null)
        {
            return string.Empty;
        }

        string name = wrapper.name;
        int tokenIndex = name.LastIndexOf(WrapperGuidToken, StringComparison.Ordinal);
        if (tokenIndex < 0 || !name.EndsWith("]", StringComparison.Ordinal))
        {
            return string.Empty;
        }

        int guidStart = tokenIndex + WrapperGuidToken.Length;
        int guidLength = name.Length - guidStart - 1;
        return guidLength > 0 ? name.Substring(guidStart, guidLength) : string.Empty;
    }

    private static string GetWrapperTitle(GameObject wrapper)
    {
        if (wrapper == null)
        {
            return "Artboard";
        }

        int tokenIndex = wrapper.name.LastIndexOf(WrapperGuidToken, StringComparison.Ordinal);
        return tokenIndex > 0 ? wrapper.name.Substring(0, tokenIndex) : wrapper.name;
    }

    private static Vector3 BoardToWorld(Vector2 position)
    {
        return new Vector3(position.x * BoardToWorldX, -position.y * BoardToWorldY, 0f);
    }

    private static void ConfigureSceneView()
    {
        SceneView sceneView = SceneView.lastActiveSceneView;
        if (sceneView == null)
        {
            return;
        }

        sceneView.in2DMode = true;
        sceneView.orthographic = true;
        sceneView.Repaint();
    }

    private static Bounds GetArtboardBounds(GameObject wrapper)
    {
        RectTransform wrapperRect = wrapper != null ? wrapper.GetComponent<RectTransform>() : null;
        if (wrapperRect == null)
        {
            return new Bounds(Vector3.zero, new Vector3(ArtboardWidth, ArtboardHeight, 1f));
        }

        Vector3[] corners = new Vector3[4];
        wrapperRect.GetWorldCorners(corners);
        Bounds bounds = new Bounds(corners[0], Vector3.zero);
        for (int i = 1; i < corners.Length; i++)
        {
            bounds.Encapsulate(corners[i]);
        }

        bounds.Expand(new Vector3(120f, 120f, 1f));
        return bounds;
    }

    private static void RebuildLayout(GameObject instanceRoot)
    {
        if (instanceRoot == null)
        {
            return;
        }

        Canvas.ForceUpdateCanvases();
        RectTransform[] rectTransforms = instanceRoot.GetComponentsInChildren<RectTransform>(true);
        for (int i = 0; i < rectTransforms.Length; i++)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(rectTransforms[i]);
        }

        Canvas.ForceUpdateCanvases();
    }

    private static void RestoreOpenBoardEnvironment()
    {
        if (!TryGetLiveScene(out _, out GameObject boardRoot))
        {
            return;
        }

        NormalizeAllArtboards(boardRoot);
        SceneView.RepaintAll();
    }

    private static void OnSceneGUI(SceneView sceneView)
    {
        if (!IsActive || !TryGetLiveScene(out _, out GameObject boardRoot))
        {
            return;
        }

        DrawArtboardFrames(boardRoot);
        DrawLiveToolbar(sceneView);
    }

    private static void DrawArtboardFrames(GameObject boardRoot)
    {
        string selectedGuid = string.Empty;
        TryGetArtboardGuid(Selection.activeGameObject, out selectedGuid);

        for (int i = 0; i < boardRoot.transform.childCount; i++)
        {
            GameObject wrapper = boardRoot.transform.GetChild(i).gameObject;
            RectTransform rectTransform = wrapper.GetComponent<RectTransform>();
            if (rectTransform == null)
            {
                continue;
            }

            Vector3[] corners = new Vector3[4];
            rectTransform.GetWorldCorners(corners);
            Vector3[] outline =
            {
                corners[0], corners[1], corners[2], corners[3], corners[0]
            };

            bool selected = GetWrapperGuid(wrapper) == selectedGuid;
            Handles.color = selected ? SelectedFrameColor : FrameColor;
            Handles.DrawAAPolyLine(selected ? 4f : 2f, outline);
            Handles.Label(
                corners[1] + new Vector3(0f, 36f, 0f),
                GetWrapperTitle(wrapper),
                GetArtboardLabelStyle(selected));
        }
    }

    private static void DrawLiveToolbar(SceneView sceneView)
    {
        TryGetArtboardGuid(Selection.activeGameObject, out string guid);
        bool hasSelection = !string.IsNullOrEmpty(guid);

        Handles.BeginGUI();
        try
        {
            Rect barRect = new Rect(10f, 10f, 390f, 24f);
            GUILayout.BeginArea(barRect, EditorStyles.toolbar);
            using (new GUILayout.HorizontalScope())
            {
                GUILayout.Label(
                    hasSelection ? "Live Artboard" : "Live Board",
                    EditorStyles.miniLabel,
                    GUILayout.Width(82f));

                using (new EditorGUI.DisabledScope(!hasSelection))
                {
                    if (GUILayout.Button("Apply", EditorStyles.toolbarButton, GUILayout.Width(52f)))
                    {
                        ApplyArtboard(guid, out string message);
                        ShowSceneNotification(sceneView, message);
                    }

                    if (GUILayout.Button("Revert", EditorStyles.toolbarButton, GUILayout.Width(54f)))
                    {
                        RevertArtboard(guid, true, out string message);
                        ShowSceneNotification(sceneView, message);
                    }
                }

                if (GUILayout.Button("Board", EditorStyles.toolbarButton, GUILayout.Width(50f)))
                {
                    UIDesignBoardWindow.Open();
                }

                if (GUILayout.Button("Close Live", EditorStyles.toolbarButton, GUILayout.Width(72f)))
                {
                    Close(out string message);
                    ShowSceneNotification(sceneView, message);
                }

                GUILayout.FlexibleSpace();
            }
            GUILayout.EndArea();
        }
        finally
        {
            Handles.EndGUI();
        }
    }

    private static GUIStyle GetArtboardLabelStyle(bool selected)
    {
        if (artboardLabelStyle == null)
        {
            artboardLabelStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 13,
                padding = new RectOffset(4, 4, 2, 2)
            };
        }

        artboardLabelStyle.normal.textColor = selected ? SelectedFrameColor : Color.white;
        return artboardLabelStyle;
    }

    private static void ShowSceneNotification(SceneView sceneView, string message)
    {
        if (sceneView != null && !string.IsNullOrEmpty(message))
        {
            sceneView.ShowNotification(new GUIContent(message), 2.5d);
        }
    }

    private static void SetFallbackActiveScene(Scene closingScene)
    {
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene candidate = SceneManager.GetSceneAt(i);
            if (candidate != closingScene && candidate.IsValid() && candidate.isLoaded)
            {
                SceneManager.SetActiveScene(candidate);
                return;
            }
        }
    }

    private static void SetStatus(string message)
    {
        lastStatus = message ?? string.Empty;
    }

    private static void OnSelectionChanged()
    {
        NotifyStateChanged();
        SceneView.RepaintAll();
    }

    private static void OnSceneClosed(Scene scene)
    {
        NotifyStateChanged();
    }

    private static void NotifyStateChanged()
    {
        StateChanged?.Invoke();
    }
}
