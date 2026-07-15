using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[InitializeOnLoad]
internal static class PlayModeUISelector
{
    private const string MenuPath = "UITools/运行时 Alt+左键选中UI";
    private const string EnabledEditorPrefKey = "SgrProject.UI.PlayModeUISelector.Enabled";
    private const string OpenPrefabMenuPath = "GameObject/UI/打开引用的 UI Prefab";
    private const string UIPrefabRoot = "Assets/Content/UI/Prefab";
    private const string IgnoredClickFeedbackRootName = "common_click_feedback";
    private const string CloneNameSuffix = "(Clone)";
    private const float DragThreshold = 6f;

    private static readonly Type SceneHierarchyWindowType =
        typeof(EditorWindow).Assembly.GetType("UnityEditor.SceneHierarchyWindow");
    private static readonly MethodInfo HierarchySetExpandedMethod =
        SceneHierarchyWindowType != null
            ? SceneHierarchyWindowType.GetMethod(
                "SetExpanded",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { typeof(int), typeof(bool) },
                null)
            : null;
    private static readonly MethodInfo HierarchyFrameObjectMethod =
        SceneHierarchyWindowType != null
            ? SceneHierarchyWindowType.GetMethod(
                "FrameObject",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { typeof(int), typeof(bool) },
                null)
            : null;
    private static readonly GUIContent OpenPrefabButtonContent = new GUIContent();

    private static PlayModeUISelectorInputBlocker inputBlocker;
    private static bool isAltPointerDown;
    private static bool isDraggingSelection;
    private static bool selectTopmostOnPointerUp;
    private static bool isLayoutControlledDrag;
    private static int activePointerTargetDisplay;
    private static int dragUndoGroup = -1;
    private static Vector2 pointerDownScreenPosition;
    private static RectTransform pendingDragRectTransform;
    private static RectTransform draggedRectTransform;
    private static Canvas draggedCanvas;
    private static Vector3 dragStartPointerWorld;
    private static Vector3 dragStartObjectWorld;

    static PlayModeUISelector()
    {
        if (!EditorPrefs.HasKey(EnabledEditorPrefKey))
        {
            EditorPrefs.SetBool(EnabledEditorPrefKey, true);
        }

        EditorApplication.update -= EnsureInputBlocker;
        EditorApplication.update += EnsureInputBlocker;
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        AssemblyReloadEvents.beforeAssemblyReload -= DestroyInputBlocker;
        AssemblyReloadEvents.beforeAssemblyReload += DestroyInputBlocker;
        Editor.finishedDefaultHeaderGUI -= DrawGameObjectHeader;
        Editor.finishedDefaultHeaderGUI += DrawGameObjectHeader;
        EditorApplication.delayCall += EnsureInputBlocker;
    }

    [MenuItem(MenuPath)]
    private static void ToggleEnabled()
    {
        bool enabled = !IsEnabled();
        EditorPrefs.SetBool(EnabledEditorPrefKey, enabled);
        Menu.SetChecked(MenuPath, enabled);

        if (enabled)
        {
            EnsureInputBlocker();
        }
        else
        {
            DestroyInputBlocker();
        }
    }

    [MenuItem(MenuPath, true)]
    private static bool ValidateToggleEnabled()
    {
        Menu.SetChecked(MenuPath, IsEnabled());
        return true;
    }

    private static bool IsEnabled()
    {
        return EditorPrefs.GetBool(EnabledEditorPrefKey, true);
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
        OpenPrefabButtonContent.text = string.IsNullOrEmpty(candidateName)
            ? "打开引用的 UI Prefab"
            : "打开 " + candidateName + ".prefab";
        OpenPrefabButtonContent.tooltip =
            "打开当前 Play Mode UI 对象引用的原始 Prefab。";

        GUILayout.Space(2f);
        using (new EditorGUI.DisabledScope(EditorApplication.isCompiling))
        {
            if (GUILayout.Button(OpenPrefabButtonContent, GUILayout.Height(22f)))
            {
                OpenSourcePrefab(target);
            }
        }
    }

    [MenuItem(OpenPrefabMenuPath, false, 49)]
    private static void OpenSelectedRuntimeUIPrefab(MenuCommand command)
    {
        GameObject target = command.context as GameObject;
        if (target == null)
        {
            target = Selection.activeGameObject;
        }

        OpenSourcePrefab(target);
    }

    [MenuItem(OpenPrefabMenuPath, true)]
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

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            EnsureInputBlocker();
            return;
        }

        if (state == PlayModeStateChange.ExitingPlayMode ||
            state == PlayModeStateChange.EnteredEditMode)
        {
            DestroyInputBlocker();
        }
    }

    private static void EnsureInputBlocker()
    {
        if (!EditorApplication.isPlaying ||
            EditorApplication.isCompiling ||
            !IsEnabled())
        {
            DestroyInputBlocker();
            return;
        }

        if (inputBlocker != null)
        {
            return;
        }

        PlayModeUISelectorInputBlocker[] existingBlockers =
            Resources.FindObjectsOfTypeAll<PlayModeUISelectorInputBlocker>();
        for (int i = 0; i < existingBlockers.Length; i++)
        {
            PlayModeUISelectorInputBlocker existing = existingBlockers[i];
            if (existing == null || EditorUtility.IsPersistent(existing))
            {
                continue;
            }

            inputBlocker = existing;
            return;
        }

        GameObject blockerObject = new GameObject(
            "[Editor] Play Mode UI Selector",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(GraphicRaycaster),
            typeof(Image),
            typeof(PlayModeUISelectorInputBlocker));
        blockerObject.hideFlags = HideFlags.HideAndDontSave;

        int uiLayer = LayerMask.NameToLayer("UI");
        if (uiLayer >= 0)
        {
            blockerObject.layer = uiLayer;
        }

        Canvas canvas = blockerObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.overrideSorting = true;
        canvas.sortingLayerID = GetHighestSortingLayerID();
        canvas.sortingOrder = short.MaxValue;
        canvas.targetDisplay = 0;

        Image image = blockerObject.GetComponent<Image>();
        image.color = Color.clear;
        image.raycastTarget = true;

        Component[] components = blockerObject.GetComponents<Component>();
        for (int i = 0; i < components.Length; i++)
        {
            if (components[i] != null)
            {
                components[i].hideFlags = HideFlags.HideAndDontSave;
            }
        }

        inputBlocker = blockerObject.GetComponent<PlayModeUISelectorInputBlocker>();
        inputBlocker.Configure(canvas.targetDisplay);
        UnityEngine.Object.DontDestroyOnLoad(blockerObject);
    }

    private static int GetHighestSortingLayerID()
    {
        SortingLayer[] layers = SortingLayer.layers;
        if (layers == null || layers.Length == 0)
        {
            return 0;
        }

        int highestLayerID = layers[0].id;
        int highestLayerValue = SortingLayer.GetLayerValueFromID(highestLayerID);
        for (int i = 1; i < layers.Length; i++)
        {
            int value = SortingLayer.GetLayerValueFromID(layers[i].id);
            if (value > highestLayerValue)
            {
                highestLayerID = layers[i].id;
                highestLayerValue = value;
            }
        }

        return highestLayerID;
    }

    private static void DestroyInputBlocker()
    {
        if (inputBlocker != null)
        {
            UnityEngine.Object.DestroyImmediate(inputBlocker.gameObject);
            inputBlocker = null;
        }
    }

    internal static bool ShouldInterceptAltLeftClick()
    {
        if (!IsEnabled() || !EditorApplication.isPlaying)
        {
            return false;
        }

        try
        {
            bool isAltHeld =
                Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
            return isAltHeld;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    internal static void HandleAltPointerDown(
        Vector2 screenPosition,
        int targetDisplay)
    {
        BeginAltPointerPress(screenPosition, targetDisplay);
    }

    internal static void HandleAltBeginDrag(Vector2 screenPosition)
    {
        TryBeginAltDrag(screenPosition);
    }

    internal static void HandleAltDrag(Vector2 screenPosition)
    {
        if (!isDraggingSelection)
        {
            TryBeginAltDrag(screenPosition);
        }

        UpdateAltDrag(screenPosition);
    }

    internal static void HandleAltPointerUp(Vector2 screenPosition)
    {
        if (!isAltPointerDown)
        {
            return;
        }

        if (isDraggingSelection)
        {
            FinishAltDrag(screenPosition);
        }
        else if (selectTopmostOnPointerUp)
        {
            SelectUIAt(screenPosition, activePointerTargetDisplay);
        }

        ClearAltPointerState();
    }

    private static void BeginAltPointerPress(
        Vector2 screenPosition,
        int targetDisplay)
    {
        ClearAltPointerState();

        if (!IsEnabled() || !EditorApplication.isPlaying)
        {
            return;
        }

        isAltPointerDown = true;
        activePointerTargetDisplay = targetDisplay;
        pointerDownScreenPosition = screenPosition;

        RectTransform selectedRectTransform =
            GetSelectedDraggableRectTransform(screenPosition, targetDisplay);
        if (selectedRectTransform != null)
        {
            pendingDragRectTransform = selectedRectTransform;
            selectTopmostOnPointerUp = true;
            return;
        }

        GameObject pickedObject =
            PickTopmostVisibleUIObject(screenPosition, targetDisplay);
        if (pickedObject == null)
        {
            return;
        }

        SelectPickedObject(pickedObject);
        pendingDragRectTransform =
            GetDraggableRectTransform(pickedObject, screenPosition, targetDisplay);
    }

    private static void SelectUIAt(Vector2 screenPosition, int targetDisplay)
    {
        if (!IsEnabled() || !EditorApplication.isPlaying)
        {
            return;
        }

        GameObject pickedObject = PickTopmostVisibleUIObject(screenPosition, targetDisplay);
        if (pickedObject == null)
        {
            return;
        }

        SelectPickedObject(pickedObject);
    }

    private static void SelectPickedObject(GameObject pickedObject)
    {
        if (pickedObject == null || IsIgnoredClickFeedbackObject(pickedObject))
        {
            return;
        }

        ExpandHierarchyAncestors(pickedObject.transform);
        Selection.activeGameObject = pickedObject;
        EditorGUIUtility.PingObject(pickedObject);
        FrameObjectInHierarchy(pickedObject);
        EditorApplication.RepaintHierarchyWindow();
    }

    private static RectTransform GetSelectedDraggableRectTransform(
        Vector2 screenPosition,
        int targetDisplay)
    {
        return GetDraggableRectTransform(
            Selection.activeGameObject,
            screenPosition,
            targetDisplay);
    }

    private static RectTransform GetDraggableRectTransform(
        GameObject target,
        Vector2 screenPosition,
        int targetDisplay)
    {
        if (target == null ||
            EditorUtility.IsPersistent(target) ||
            !target.scene.IsValid() ||
            !target.scene.isLoaded ||
            !target.activeInHierarchy ||
            IsIgnoredClickFeedbackObject(target) ||
            !IsSceneVisibleAndPickable(target))
        {
            return null;
        }

        RectTransform rectTransform = target.GetComponent<RectTransform>();
        if (rectTransform == null)
        {
            return null;
        }

        Canvas canvas = rectTransform.GetComponentInParent<Canvas>();
        if (canvas == null ||
            canvas.rootCanvas == null ||
            canvas.rootCanvas.targetDisplay != targetDisplay ||
            !RectTransformUtility.RectangleContainsScreenPoint(
                rectTransform,
                screenPosition,
                GetEventCamera(canvas)))
        {
            return null;
        }

        return rectTransform;
    }

    private static void TryBeginAltDrag(Vector2 screenPosition)
    {
        if (!isAltPointerDown ||
            isDraggingSelection ||
            pendingDragRectTransform == null ||
            Vector2.Distance(pointerDownScreenPosition, screenPosition) < DragThreshold)
        {
            return;
        }

        if (!IsEnabled() || !EditorApplication.isPlaying)
        {
            ClearAltPointerState();
            return;
        }

        Canvas canvas = pendingDragRectTransform.GetComponentInParent<Canvas>();
        if (canvas == null)
        {
            ClearAltPointerState();
            return;
        }

        Camera eventCamera = GetEventCamera(canvas);
        if (!RectTransformUtility.ScreenPointToWorldPointInRectangle(
                pendingDragRectTransform,
                pointerDownScreenPosition,
                eventCamera,
                out Vector3 startPointerWorld) ||
            !RectTransformUtility.ScreenPointToWorldPointInRectangle(
                pendingDragRectTransform,
                screenPosition,
                eventCamera,
                out _))
        {
            ClearAltPointerState();
            return;
        }

        draggedRectTransform = pendingDragRectTransform;
        draggedCanvas = canvas;
        dragStartPointerWorld = startPointerWorld;
        dragStartObjectWorld = draggedRectTransform.position;
        isLayoutControlledDrag = IsDrivenByParentLayout(draggedRectTransform);
        isDraggingSelection = true;
        selectTopmostOnPointerUp = false;

        if (!isLayoutControlledDrag)
        {
            Undo.IncrementCurrentGroup();
            dragUndoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Move Play Mode UI Element");
            Undo.RecordObject(draggedRectTransform, "Move Play Mode UI Element");
        }

        SelectPickedObject(draggedRectTransform.gameObject);
    }

    private static void UpdateAltDrag(Vector2 screenPosition)
    {
        if (!isDraggingSelection || draggedRectTransform == null)
        {
            return;
        }

        if (isLayoutControlledDrag)
        {
            return;
        }

        Camera eventCamera = GetEventCamera(draggedCanvas);
        if (!RectTransformUtility.ScreenPointToWorldPointInRectangle(
                draggedRectTransform,
                screenPosition,
                eventCamera,
                out Vector3 pointerWorld))
        {
            return;
        }

        draggedRectTransform.position =
            dragStartObjectWorld + (pointerWorld - dragStartPointerWorld);
        EditorUtility.SetDirty(draggedRectTransform);
        Canvas.ForceUpdateCanvases();
    }

    private static void FinishAltDrag(Vector2 screenPosition)
    {
        UpdateAltDrag(screenPosition);
        if (dragUndoGroup >= 0)
        {
            Undo.CollapseUndoOperations(dragUndoGroup);
        }
    }

    private static bool IsDrivenByParentLayout(RectTransform rectTransform)
    {
        if (rectTransform == null || rectTransform.parent == null)
        {
            return false;
        }

        LayoutGroup parentLayout =
            rectTransform.parent.GetComponent<LayoutGroup>();
        if (parentLayout == null || !parentLayout.isActiveAndEnabled)
        {
            return false;
        }

        Component[] components = rectTransform.GetComponents<Component>();
        for (int i = 0; i < components.Length; i++)
        {
            ILayoutIgnorer layoutIgnorer = components[i] as ILayoutIgnorer;
            if (layoutIgnorer == null || !layoutIgnorer.ignoreLayout)
            {
                continue;
            }

            Behaviour behaviour = components[i] as Behaviour;
            if (behaviour == null || behaviour.isActiveAndEnabled)
            {
                return false;
            }
        }

        return true;
    }

    private static void ClearAltPointerState()
    {
        isAltPointerDown = false;
        isDraggingSelection = false;
        selectTopmostOnPointerUp = false;
        isLayoutControlledDrag = false;
        activePointerTargetDisplay = 0;
        dragUndoGroup = -1;
        pointerDownScreenPosition = default(Vector2);
        pendingDragRectTransform = null;
        draggedRectTransform = null;
        draggedCanvas = null;
        dragStartPointerWorld = default(Vector3);
        dragStartObjectWorld = default(Vector3);
    }

    private static GameObject PickTopmostVisibleUIObject(
        Vector2 screenPosition,
        int targetDisplay)
    {
        Canvas.ForceUpdateCanvases();
        Graphic[] graphics = Resources.FindObjectsOfTypeAll<Graphic>();
        Graphic bestGraphic = null;
        PickOrder bestOrder = default(PickOrder);

        for (int i = 0; i < graphics.Length; i++)
        {
            Graphic graphic = graphics[i];
            if (!IsSelectableVisibleGraphic(graphic, targetDisplay))
            {
                continue;
            }

            Camera eventCamera = GetEventCamera(graphic.canvas);
            if (!RectTransformUtility.RectangleContainsScreenPoint(
                    graphic.rectTransform,
                    screenPosition,
                    eventCamera))
            {
                continue;
            }

            if (!IsInsideVisibleMasks(graphic, screenPosition))
            {
                continue;
            }

            PickOrder order = BuildPickOrder(graphic, eventCamera, i);
            if (bestGraphic == null || order.IsAbove(bestOrder))
            {
                bestGraphic = graphic;
                bestOrder = order;
            }
        }

        return ResolveSelectionObject(bestGraphic);
    }

    private static bool IsSelectableVisibleGraphic(Graphic graphic, int targetDisplay)
    {
        if (graphic == null ||
            graphic.GetType().Name == "EmptyRaycast" ||
            EditorUtility.IsPersistent(graphic) ||
            !graphic.gameObject.scene.IsValid() ||
            !graphic.gameObject.scene.isLoaded ||
            IsIgnoredClickFeedbackObject(graphic.gameObject) ||
            !graphic.IsActive() ||
            graphic.canvas == null ||
            graphic.canvas.rootCanvas == null ||
            graphic.canvas.rootCanvas.targetDisplay != targetDisplay ||
            graphic.canvasRenderer == null ||
            graphic.canvasRenderer.cull ||
            graphic.depth < 0 ||
            !IsSceneVisibleAndPickable(graphic.gameObject))
        {
            return false;
        }

        Rect rect = graphic.rectTransform.rect;
        if (rect.width <= 0.01f || rect.height <= 0.01f)
        {
            return false;
        }

        float visibleAlpha = graphic.color.a * graphic.canvasRenderer.GetAlpha();
        CanvasGroup[] canvasGroups = graphic.GetComponentsInParent<CanvasGroup>(true);
        for (int i = 0; i < canvasGroups.Length; i++)
        {
            visibleAlpha *= canvasGroups[i].alpha;
        }

        return visibleAlpha > 0.01f;
    }

    private static bool IsInsideVisibleMasks(Graphic graphic, Vector2 screenPosition)
    {
        RectMask2D[] rectMasks = graphic.GetComponentsInParent<RectMask2D>(true);
        for (int i = 0; i < rectMasks.Length; i++)
        {
            RectMask2D rectMask = rectMasks[i];
            if (rectMask == null || !rectMask.isActiveAndEnabled)
            {
                continue;
            }

            RectTransform maskRect = rectMask.transform as RectTransform;
            Canvas maskCanvas = rectMask.GetComponentInParent<Canvas>();
            if (maskRect != null &&
                !RectTransformUtility.RectangleContainsScreenPoint(
                    maskRect,
                    screenPosition,
                    GetEventCamera(maskCanvas)))
            {
                return false;
            }
        }

        Mask[] masks = graphic.GetComponentsInParent<Mask>(true);
        for (int i = 0; i < masks.Length; i++)
        {
            Mask mask = masks[i];
            if (mask == null || !mask.isActiveAndEnabled)
            {
                continue;
            }

            RectTransform maskRect = mask.transform as RectTransform;
            Canvas maskCanvas = mask.GetComponentInParent<Canvas>();
            if (maskRect != null &&
                !RectTransformUtility.RectangleContainsScreenPoint(
                    maskRect,
                    screenPosition,
                    GetEventCamera(maskCanvas)))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsIgnoredClickFeedbackObject(GameObject target)
    {
        if (target == null)
        {
            return false;
        }

        for (Transform current = target.transform; current != null; current = current.parent)
        {
            string normalizedName = NormalizeCloneName(current.name);
            if (string.Equals(
                    normalizedName,
                    IgnoredClickFeedbackRootName,
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string NormalizeCloneName(string objectName)
    {
        if (string.IsNullOrEmpty(objectName))
        {
            return string.Empty;
        }

        string trimmedName = objectName.Trim();
        if (trimmedName.EndsWith(CloneNameSuffix, StringComparison.Ordinal))
        {
            trimmedName = trimmedName
                .Substring(0, trimmedName.Length - CloneNameSuffix.Length)
                .TrimEnd();
        }

        return trimmedName;
    }

    private static bool IsSceneVisibleAndPickable(GameObject target)
    {
        SceneVisibilityManager visibilityManager = SceneVisibilityManager.instance;
        for (Transform current = target.transform; current != null; current = current.parent)
        {
            GameObject currentObject = current.gameObject;
            if (visibilityManager.IsHidden(currentObject, false) ||
                visibilityManager.IsPickingDisabled(currentObject, false))
            {
                return false;
            }
        }

        return true;
    }

    private static Camera GetEventCamera(Canvas canvas)
    {
        if (canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay)
        {
            return null;
        }

        return canvas.worldCamera != null ? canvas.worldCamera : Camera.main;
    }

    private static PickOrder BuildPickOrder(
        Graphic graphic,
        Camera eventCamera,
        int hierarchyOrder)
    {
        Canvas canvas = graphic.canvas;
        RectTransform rectTransform = graphic.rectTransform;
        Vector3[] corners = new Vector3[4];
        rectTransform.GetWorldCorners(corners);
        Vector2 min = RectTransformUtility.WorldToScreenPoint(eventCamera, corners[0]);
        Vector2 max = min;

        for (int i = 1; i < corners.Length; i++)
        {
            Vector2 point = RectTransformUtility.WorldToScreenPoint(eventCamera, corners[i]);
            min = Vector2.Min(min, point);
            max = Vector2.Max(max, point);
        }

        float screenArea = Mathf.Abs((max.x - min.x) * (max.y - min.y));
        float cameraDepth = eventCamera != null ? eventCamera.depth : float.MaxValue;

        return new PickOrder(
            SortingLayer.GetLayerValueFromID(canvas.sortingLayerID),
            canvas.sortingOrder,
            cameraDepth,
            canvas.rootCanvas.renderOrder,
            graphic.depth,
            screenArea,
            hierarchyOrder);
    }

    private static GameObject ResolveSelectionObject(Graphic graphic)
    {
        if (graphic == null)
        {
            return null;
        }

        TMP_SubMeshUI subMesh = graphic as TMP_SubMeshUI;
        if (subMesh != null && subMesh.textComponent != null)
        {
            return subMesh.textComponent.gameObject;
        }

        return graphic.gameObject;
    }

    private static void ExpandHierarchyAncestors(Transform target)
    {
        if (target == null ||
            SceneHierarchyWindowType == null ||
            HierarchySetExpandedMethod == null)
        {
            return;
        }

        Stack<Transform> ancestors = new Stack<Transform>();
        for (Transform current = target.parent; current != null; current = current.parent)
        {
            ancestors.Push(current);
        }

        UnityEngine.Object[] hierarchyWindows =
            Resources.FindObjectsOfTypeAll(SceneHierarchyWindowType);
        for (int i = 0; i < hierarchyWindows.Length; i++)
        {
            object hierarchyWindow = hierarchyWindows[i];
            foreach (Transform ancestor in ancestors)
            {
                try
                {
                    HierarchySetExpandedMethod.Invoke(
                        hierarchyWindow,
                        new object[] { ancestor.gameObject.GetInstanceID(), true });
                }
                catch
                {
                    break;
                }
            }
        }
    }

    private static void FrameObjectInHierarchy(GameObject target)
    {
        if (target == null ||
            SceneHierarchyWindowType == null ||
            HierarchyFrameObjectMethod == null)
        {
            return;
        }

        UnityEngine.Object[] hierarchyWindows =
            Resources.FindObjectsOfTypeAll(SceneHierarchyWindowType);
        for (int i = 0; i < hierarchyWindows.Length; i++)
        {
            try
            {
                HierarchyFrameObjectMethod.Invoke(
                    hierarchyWindows[i],
                    new object[] { target.GetInstanceID(), false });
            }
            catch
            {
                // Selection and PingObject remain as the safe fallback.
            }
        }
    }

    private struct PickOrder
    {
        private readonly int sortingLayer;
        private readonly int sortingOrder;
        private readonly float cameraDepth;
        private readonly int renderOrder;
        private readonly int graphicDepth;
        private readonly float screenArea;
        private readonly int hierarchyOrder;

        public PickOrder(
            int sortingLayer,
            int sortingOrder,
            float cameraDepth,
            int renderOrder,
            int graphicDepth,
            float screenArea,
            int hierarchyOrder)
        {
            this.sortingLayer = sortingLayer;
            this.sortingOrder = sortingOrder;
            this.cameraDepth = cameraDepth;
            this.renderOrder = renderOrder;
            this.graphicDepth = graphicDepth;
            this.screenArea = screenArea;
            this.hierarchyOrder = hierarchyOrder;
        }

        public bool IsAbove(PickOrder other)
        {
            if (sortingLayer != other.sortingLayer)
            {
                return sortingLayer > other.sortingLayer;
            }

            if (sortingOrder != other.sortingOrder)
            {
                return sortingOrder > other.sortingOrder;
            }

            if (!Mathf.Approximately(cameraDepth, other.cameraDepth))
            {
                return cameraDepth > other.cameraDepth;
            }

            if (renderOrder != other.renderOrder)
            {
                return renderOrder > other.renderOrder;
            }

            if (graphicDepth != other.graphicDepth)
            {
                return graphicDepth > other.graphicDepth;
            }

            if (!Mathf.Approximately(screenArea, other.screenArea))
            {
                return screenArea < other.screenArea;
            }

            return hierarchyOrder > other.hierarchyOrder;
        }
    }
}

internal sealed class PlayModeUISelectorInputBlocker :
    MonoBehaviour,
    IPointerDownHandler,
    IPointerUpHandler,
    IBeginDragHandler,
    IDragHandler,
    IEndDragHandler,
    ICanvasRaycastFilter
{
    private int targetDisplay;

    internal void Configure(int display)
    {
        targetDisplay = display;
    }

    public bool IsRaycastLocationValid(Vector2 screenPoint, Camera eventCamera)
    {
        return PlayModeUISelector.ShouldInterceptAltLeftClick();
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (eventData == null ||
            eventData.button != PointerEventData.InputButton.Left ||
            !PlayModeUISelector.ShouldInterceptAltLeftClick())
        {
            return;
        }

        PlayModeUISelector.HandleAltPointerDown(eventData.position, targetDisplay);
        eventData.Use();
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (eventData == null ||
            eventData.button != PointerEventData.InputButton.Left)
        {
            return;
        }

        PlayModeUISelector.HandleAltBeginDrag(eventData.position);
        eventData.Use();
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (eventData == null ||
            eventData.button != PointerEventData.InputButton.Left)
        {
            return;
        }

        PlayModeUISelector.HandleAltDrag(eventData.position);
        eventData.Use();
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (eventData == null ||
            eventData.button != PointerEventData.InputButton.Left)
        {
            return;
        }

        PlayModeUISelector.HandleAltPointerUp(eventData.position);
        eventData.Use();
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (eventData == null ||
            eventData.button != PointerEventData.InputButton.Left)
        {
            return;
        }

        PlayModeUISelector.HandleAltPointerUp(eventData.position);
        eventData.Use();
    }
}
