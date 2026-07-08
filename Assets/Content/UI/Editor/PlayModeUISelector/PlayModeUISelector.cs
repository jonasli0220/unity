using System;
using System.Collections.Generic;
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

    private static PlayModeUISelectorInputBlocker inputBlocker;

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
        SelectUIAt(screenPosition, targetDisplay);
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

        ExpandHierarchyAncestors(pickedObject.transform);
        Selection.activeGameObject = pickedObject;
        EditorGUIUtility.PingObject(pickedObject);
        FrameObjectInHierarchy(pickedObject);
        EditorApplication.RepaintHierarchyWindow();
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
}
