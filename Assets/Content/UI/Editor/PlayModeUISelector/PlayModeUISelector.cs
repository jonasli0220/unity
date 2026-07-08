using System;
using System.Collections.Generic;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.UIElements;

[InitializeOnLoad]
internal static class PlayModeUISelector
{
    private const string MenuPath = "UITools/运行时 Alt+左键选中UI";
    private const string EnabledEditorPrefKey = "SgrProject.UI.PlayModeUISelector.Enabled";
    private const double GameViewScanInterval = 0.75d;

    private static readonly Type GameViewType =
        typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
    private static readonly Type SceneHierarchyWindowType =
        typeof(EditorWindow).Assembly.GetType("UnityEditor.SceneHierarchyWindow");
    private static readonly PropertyInfo GameViewTargetDisplayProperty =
        GameViewType != null
            ? GameViewType.GetProperty(
                "targetDisplay",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            : null;
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

    private static readonly Dictionary<EditorWindow, EventCallback<MouseDownEvent>>
        GameViewCallbacks =
            new Dictionary<EditorWindow, EventCallback<MouseDownEvent>>();

    private static double nextGameViewScanTime;

    static PlayModeUISelector()
    {
        if (!EditorPrefs.HasKey(EnabledEditorPrefKey))
        {
            EditorPrefs.SetBool(EnabledEditorPrefKey, true);
        }

        EditorApplication.update -= OnEditorUpdate;
        EditorApplication.update += OnEditorUpdate;
        AssemblyReloadEvents.beforeAssemblyReload -= UnhookAllGameViews;
        AssemblyReloadEvents.beforeAssemblyReload += UnhookAllGameViews;
        EditorApplication.delayCall += EnsureGameViewHooks;
    }

    [MenuItem(MenuPath)]
    private static void ToggleEnabled()
    {
        bool enabled = !IsEnabled();
        EditorPrefs.SetBool(EnabledEditorPrefKey, enabled);
        Menu.SetChecked(MenuPath, enabled);
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

    private static void OnEditorUpdate()
    {
        if (EditorApplication.timeSinceStartup < nextGameViewScanTime)
        {
            return;
        }

        nextGameViewScanTime = EditorApplication.timeSinceStartup + GameViewScanInterval;
        EnsureGameViewHooks();
    }

    private static void EnsureGameViewHooks()
    {
        if (GameViewType == null)
        {
            return;
        }

        UnityEngine.Object[] foundViews = Resources.FindObjectsOfTypeAll(GameViewType);
        HashSet<EditorWindow> liveViews = new HashSet<EditorWindow>();

        for (int i = 0; i < foundViews.Length; i++)
        {
            EditorWindow gameView = foundViews[i] as EditorWindow;
            if (gameView == null)
            {
                continue;
            }

            liveViews.Add(gameView);
            if (GameViewCallbacks.ContainsKey(gameView))
            {
                continue;
            }

            EditorWindow capturedGameView = gameView;
            EventCallback<MouseDownEvent> callback =
                evt => OnGameViewMouseDown(capturedGameView, evt);
            gameView.rootVisualElement.RegisterCallback(
                callback,
                TrickleDown.TrickleDown);
            GameViewCallbacks.Add(gameView, callback);
        }

        List<EditorWindow> staleViews = new List<EditorWindow>();
        foreach (KeyValuePair<EditorWindow, EventCallback<MouseDownEvent>> pair in GameViewCallbacks)
        {
            if (pair.Key == null || !liveViews.Contains(pair.Key))
            {
                staleViews.Add(pair.Key);
            }
        }

        for (int i = 0; i < staleViews.Count; i++)
        {
            EditorWindow staleView = staleViews[i];
            if (staleView != null)
            {
                staleView.rootVisualElement.UnregisterCallback(
                    GameViewCallbacks[staleView],
                    TrickleDown.TrickleDown);
            }

            GameViewCallbacks.Remove(staleView);
        }
    }

    private static void UnhookAllGameViews()
    {
        foreach (KeyValuePair<EditorWindow, EventCallback<MouseDownEvent>> pair in GameViewCallbacks)
        {
            if (pair.Key == null)
            {
                continue;
            }

            pair.Key.rootVisualElement.UnregisterCallback(
                pair.Value,
                TrickleDown.TrickleDown);
        }

        GameViewCallbacks.Clear();
    }

    private static void OnGameViewMouseDown(EditorWindow gameView, MouseDownEvent evt)
    {
        if (!IsEnabled() ||
            !EditorApplication.isPlaying ||
            gameView == null ||
            EditorWindow.mouseOverWindow != gameView ||
            evt.button != 0 ||
            !evt.altKey)
        {
            return;
        }

        Vector2 screenPosition;
        try
        {
            screenPosition = Input.mousePosition;
        }
        catch (InvalidOperationException)
        {
            return;
        }

        int targetDisplay = GetTargetDisplay(gameView);
        if (!IsInsideTargetDisplay(screenPosition, targetDisplay))
        {
            return;
        }

        evt.StopImmediatePropagation();
        evt.PreventDefault();
        EditorApplication.delayCall += () => SelectUIAt(screenPosition, targetDisplay);
    }

    private static int GetTargetDisplay(EditorWindow gameView)
    {
        if (GameViewTargetDisplayProperty == null || gameView == null)
        {
            return 0;
        }

        try
        {
            return (int)GameViewTargetDisplayProperty.GetValue(gameView, null);
        }
        catch
        {
            return 0;
        }
    }

    private static bool IsInsideTargetDisplay(Vector2 screenPosition, int targetDisplay)
    {
        float width = Screen.width;
        float height = Screen.height;

        if (targetDisplay >= 0 && targetDisplay < Display.displays.Length)
        {
            Display display = Display.displays[targetDisplay];
            if (display.renderingWidth > 0 && display.renderingHeight > 0)
            {
                width = display.renderingWidth;
                height = display.renderingHeight;
            }
        }

        return screenPosition.x >= 0f &&
               screenPosition.y >= 0f &&
               screenPosition.x <= width &&
               screenPosition.y <= height;
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
