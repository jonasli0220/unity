using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

[InitializeOnLoad]
internal static class DirectVisibleUISelection
{
    private const string MenuPath = "UITools/点击直选可见UI图层";
    private const string EnabledEditorPrefKey = "SgrProject.UI.DirectVisibleUISelection.Enabled";
    private const float DragThreshold = 6f;
    private const string InlineTextControlName = "DirectVisibleUISelection.InlineTMPText";
    private const float InlineTextMinWidth = 120f;
    private const float InlineTextMinHeight = 28f;

    private static readonly int DirectDragControlHash =
        "DirectVisibleUISelection.DirectDrag".GetHashCode();
    private static readonly int InlineTextControlHash =
        "DirectVisibleUISelection.InlineTMPTextControl".GetHashCode();
    private static readonly Color HoverFillColor = new Color(0.2f, 0.7f, 1f, 0.04f);
    private static readonly Color HoverOutlineColor = new Color(0.2f, 0.75f, 1f, 0.65f);
    private static readonly FieldInfo RectSelectionControlIdField;

    private static bool isClickPending;
    private static bool isDragging;
    private static bool isDirectDragging;
    private static Vector2 mouseDownPosition;
    private static GameObject pressedObject;
    private static GameObject hoveredObject;
    private static RectTransform draggedRectTransform;
    private static Plane directDragPlane;
    private static Vector3 directDragStartPointerWorld;
    private static Vector3 directDragStartObjectWorld;
    private static int directDragControlId;
    private static int directDragUndoGroup = -1;
    private static TMP_Text inlineEditingText;
    private static string inlineEditingValue;
    private static string inlineEditingOriginalValue;
    private static int inlineTextUndoGroup = -1;
    private static bool shouldFocusInlineTextEditor;
    private static GUIStyle inlineTextAreaStyle;

    static DirectVisibleUISelection()
    {
        System.Type rectSelectionType = typeof(Editor).Assembly.GetType("UnityEditor.RectSelection");
        RectSelectionControlIdField = rectSelectionType?.GetField(
            "s_RectSelectionID",
            BindingFlags.NonPublic | BindingFlags.Static);

        if (!EditorPrefs.HasKey(EnabledEditorPrefKey))
        {
            EditorPrefs.SetBool(EnabledEditorPrefKey, true);
        }

        SceneView.beforeSceneGui -= OnBeforeSceneGui;
        SceneView.beforeSceneGui += OnBeforeSceneGui;
        SceneView.duringSceneGui -= OnDuringSceneGui;
        SceneView.duringSceneGui += OnDuringSceneGui;
        Selection.selectionChanged -= OnSelectionChanged;
        Selection.selectionChanged += OnSelectionChanged;
        SceneVisibilityManager.visibilityChanged -= OnSceneVisibilityChanged;
        SceneVisibilityManager.visibilityChanged += OnSceneVisibilityChanged;
    }

    [MenuItem(MenuPath)]
    private static void ToggleEnabled()
    {
        bool enabled = !IsEnabled();
        EditorPrefs.SetBool(EnabledEditorPrefKey, enabled);
        Menu.SetChecked(MenuPath, enabled);
        CommitInlineTextEdit();
        CancelDirectDrag();
        ResetClickState();
        ClearHoverPreview();
        SceneView.RepaintAll();
    }

    [MenuItem(MenuPath, true)]
    private static bool ValidateToggleEnabled()
    {
        Menu.SetChecked(MenuPath, IsEnabled());
        return true;
    }

    private static void OnBeforeSceneGui(SceneView sceneView)
    {
        Event currentEvent = Event.current;
        if (currentEvent == null)
        {
            return;
        }

        if (HandleInlineTextEditingBeforeSceneGui(sceneView, currentEvent))
        {
            return;
        }

        if (!IsEnabled())
        {
            CancelDirectDrag();
            ResetClickState();
            ClearHoverPreview();
            return;
        }

        int currentDirectDragControlId =
            GUIUtility.GetControlID(DirectDragControlHash, FocusType.Passive);

        switch (currentEvent.type)
        {
            case EventType.MouseDown:
                if (TryBeginInlineTextEdit(sceneView, currentEvent))
                {
                    break;
                }

                BeginClick(currentEvent);
                break;

            case EventType.MouseDrag:
                UpdateDragState(sceneView, currentEvent, currentDirectDragControlId);
                ClearHoverPreview();
                break;

            case EventType.MouseUp:
                if (TryFinishDirectDrag(sceneView, currentEvent))
                {
                    break;
                }

                TrySelectTopmostVisibleUI(sceneView, currentEvent);
                break;

            case EventType.KeyDown:
                if (currentEvent.keyCode == KeyCode.Escape)
                {
                    bool canceledDirectDrag = isDirectDragging;
                    CancelDirectDrag();
                    ResetClickState();
                    ClearHoverPreview();
                    if (canceledDirectDrag)
                    {
                        currentEvent.Use();
                    }
                }
                break;

            case EventType.Ignore:
                CancelDirectDrag();
                ResetClickState();
                ClearHoverPreview();
                break;

        }
    }

    private static void OnDuringSceneGui(SceneView sceneView)
    {
        Event currentEvent = Event.current;
        if (currentEvent == null)
        {
            return;
        }

        sceneView.wantsMouseMove = true;

        if (inlineEditingText != null)
        {
            DrawInlineTextEditor(sceneView);
            return;
        }

        if (!IsEnabled())
        {
            ClearHoverPreview();
            return;
        }

        switch (currentEvent.type)
        {
            case EventType.MouseMove:
                UpdateHoverPreview(sceneView, currentEvent);
                break;

            case EventType.Repaint:
                DrawHoverPreview();
                break;

            case EventType.MouseLeaveWindow:
                ClearHoverPreview();
                break;
        }
    }

    private static bool HandleInlineTextEditingBeforeSceneGui(
        SceneView sceneView,
        Event currentEvent)
    {
        if (inlineEditingText == null)
        {
            return false;
        }

        int controlId = GUIUtility.GetControlID(InlineTextControlHash, FocusType.Passive);
        if (currentEvent.type == EventType.Layout)
        {
            HandleUtility.AddDefaultControl(controlId);
        }

        if (currentEvent.type == EventType.KeyDown)
        {
            bool isUndoShortcut =
                (currentEvent.control || currentEvent.command) &&
                currentEvent.keyCode == KeyCode.Z &&
                !currentEvent.shift;
            if (isUndoShortcut)
            {
                CancelInlineTextEdit();
                currentEvent.Use();
                return true;
            }

            if (currentEvent.keyCode == KeyCode.Escape)
            {
                CancelInlineTextEdit();
                currentEvent.Use();
                return true;
            }

            bool isCommitShortcut =
                (currentEvent.control || currentEvent.command) &&
                (currentEvent.keyCode == KeyCode.Return ||
                 currentEvent.keyCode == KeyCode.KeypadEnter);
            if (isCommitShortcut)
            {
                CommitInlineTextEdit();
                currentEvent.Use();
                return true;
            }
        }

        if (currentEvent.type == EventType.MouseDown && currentEvent.button == 0)
        {
            Rect editorRect = GetInlineTextEditorRect(inlineEditingText, sceneView);
            if (!editorRect.Contains(currentEvent.mousePosition))
            {
                CommitInlineTextEdit();
                return false;
            }
        }

        return true;
    }

    private static bool TryBeginInlineTextEdit(
        SceneView sceneView,
        Event currentEvent)
    {
        if (currentEvent.button != 0 ||
            currentEvent.clickCount < 2 ||
            HasSelectionModifier(currentEvent))
        {
            return false;
        }

        PrefabStage prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
        if (!IsUIPrefabStage(prefabStage))
        {
            return false;
        }

        TMP_Text targetText =
            PickTopmostVisibleUIText(prefabStage, currentEvent.mousePosition);
        if (targetText == null)
        {
            return false;
        }

        if (!BeginInlineTextEdit(targetText, sceneView))
        {
            return false;
        }

        currentEvent.Use();
        return true;
    }

    internal static bool BeginInlineTextEdit(TMP_Text targetText, SceneView sceneView = null)
    {
        if (targetText == null ||
            !IsSceneVisibleAndPickable(targetText.gameObject))
        {
            return false;
        }

        PrefabStage prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
        if (!IsUIPrefabStage(prefabStage) ||
            !IsEditableUIObjectInStage(targetText.gameObject, prefabStage))
        {
            return false;
        }

        if (sceneView == null)
        {
            sceneView = SceneView.lastActiveSceneView;
        }

        if (inlineEditingText != null)
        {
            CommitInlineTextEdit();
        }

        CancelDirectDrag();
        ResetClickState();

        Undo.IncrementCurrentGroup();
        inlineTextUndoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Edit TMP Text");
        Undo.RegisterCompleteObjectUndo(targetText, "Edit TMP Text");

        inlineEditingText = targetText;
        inlineEditingValue = targetText.text;
        inlineEditingOriginalValue = targetText.text;
        shouldFocusInlineTextEditor = true;

        GUIUtility.hotControl = 0;
        GUIUtility.keyboardControl = 0;
        SelectPickedUIObject(targetText.gameObject, prefabStage);
        if (sceneView != null)
        {
            SetHoveredObject(targetText.gameObject, sceneView);
            sceneView.Repaint();
        }

        EditorApplication.RepaintHierarchyWindow();
        SceneView.RepaintAll();
        return true;
    }

    private static void DrawInlineTextEditor(SceneView sceneView)
    {
        if (inlineEditingText == null)
        {
            return;
        }

        Rect editorRect = GetInlineTextEditorRect(inlineEditingText, sceneView);
        GUIStyle textAreaStyle = GetInlineTextAreaStyle(inlineEditingText, editorRect);

        Handles.BeginGUI();
        try
        {
            Rect outlineRect = new Rect(
                editorRect.x - 2f,
                editorRect.y - 2f,
                editorRect.width + 4f,
                editorRect.height + 4f);
            EditorGUI.DrawRect(outlineRect, HoverOutlineColor);
            EditorGUI.DrawRect(editorRect, new Color(0.07f, 0.09f, 0.12f, 0.96f));

            GUI.SetNextControlName(InlineTextControlName);
            EditorGUI.BeginChangeCheck();
            string newValue = EditorGUI.TextArea(
                new Rect(
                    editorRect.x + 3f,
                    editorRect.y + 3f,
                    editorRect.width - 6f,
                    editorRect.height - 6f),
                inlineEditingValue,
                textAreaStyle);
            if (EditorGUI.EndChangeCheck())
            {
                ApplyInlineTextValue(newValue, sceneView);
            }

            if (shouldFocusInlineTextEditor)
            {
                EditorGUI.FocusTextInControl(InlineTextControlName);
                bool hasTextFocus =
                    GUI.GetNameOfFocusedControl() == InlineTextControlName &&
                    GUIUtility.keyboardControl != 0;
                if (hasTextFocus && Event.current.type == EventType.Repaint)
                {
                    TextEditor textEditor = GUIUtility.GetStateObject(
                        typeof(TextEditor),
                        GUIUtility.keyboardControl) as TextEditor;
                    if (textEditor != null)
                    {
                        textEditor.cursorIndex = 0;
                        textEditor.selectIndex = inlineEditingValue?.Length ?? 0;
                        shouldFocusInlineTextEditor = false;
                    }
                }

                if (shouldFocusInlineTextEditor)
                {
                    sceneView.Repaint();
                }
            }

            Rect hintRect = new Rect(
                editorRect.x,
                editorRect.yMax + 4f,
                editorRect.width,
                18f);
            GUI.Label(hintRect, "Ctrl+Enter 完成    Esc 取消", EditorStyles.miniLabel);
        }
        finally
        {
            Handles.EndGUI();
        }
    }

    private static void ApplyInlineTextValue(string newValue, SceneView sceneView)
    {
        if (inlineEditingText == null || inlineEditingValue == newValue)
        {
            return;
        }

        inlineEditingValue = newValue;
        inlineEditingText.text = newValue;
        inlineEditingText.SetAllDirty();
        PrefabUtility.RecordPrefabInstancePropertyModifications(inlineEditingText);
        EditorUtility.SetDirty(inlineEditingText);

        PrefabStage prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
        if (prefabStage != null)
        {
            EditorSceneManager.MarkSceneDirty(prefabStage.scene);
        }

        sceneView.Repaint();
        UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
    }

    private static void CommitInlineTextEdit()
    {
        if (inlineEditingText == null && inlineTextUndoGroup < 0)
        {
            return;
        }

        if (inlineEditingText != null)
        {
            inlineEditingText.text = inlineEditingValue ?? string.Empty;
            inlineEditingText.SetAllDirty();
            PrefabUtility.RecordPrefabInstancePropertyModifications(inlineEditingText);
            EditorUtility.SetDirty(inlineEditingText);
        }

        if (inlineTextUndoGroup >= 0)
        {
            Undo.FlushUndoRecordObjects();
            Undo.CollapseUndoOperations(inlineTextUndoGroup);
        }

        ClearInlineTextEditState();
        SceneView.RepaintAll();
    }

    private static void CancelInlineTextEdit()
    {
        TMP_Text targetText = inlineEditingText;
        string originalValue = inlineEditingOriginalValue;

        if (inlineTextUndoGroup >= 0)
        {
            Undo.RevertAllDownToGroup(inlineTextUndoGroup);
        }
        else if (targetText != null)
        {
            targetText.text = originalValue ?? string.Empty;
            targetText.SetAllDirty();
        }

        ClearInlineTextEditState();
        SceneView.RepaintAll();
    }

    private static void ClearInlineTextEditState()
    {
        inlineEditingText = null;
        inlineEditingValue = null;
        inlineEditingOriginalValue = null;
        inlineTextUndoGroup = -1;
        shouldFocusInlineTextEditor = false;
        GUIUtility.keyboardControl = 0;
    }

    private static void OnSelectionChanged()
    {
        if (inlineEditingText != null &&
            Selection.activeGameObject != inlineEditingText.gameObject)
        {
            CommitInlineTextEdit();
        }
    }

    private static Rect GetInlineTextEditorRect(TMP_Text text, SceneView sceneView)
    {
        RectTransform rectTransform = text.rectTransform;
        Vector3[] worldCorners = new Vector3[4];
        rectTransform.GetWorldCorners(worldCorners);

        Vector2 firstCorner = HandleUtility.WorldToGUIPoint(worldCorners[0]);
        float minX = firstCorner.x;
        float maxX = firstCorner.x;
        float minY = firstCorner.y;
        float maxY = firstCorner.y;

        for (int i = 1; i < worldCorners.Length; i++)
        {
            Vector2 guiPoint = HandleUtility.WorldToGUIPoint(worldCorners[i]);
            minX = Mathf.Min(minX, guiPoint.x);
            maxX = Mathf.Max(maxX, guiPoint.x);
            minY = Mathf.Min(minY, guiPoint.y);
            maxY = Mathf.Max(maxY, guiPoint.y);
        }

        float width = Mathf.Max(InlineTextMinWidth, maxX - minX);
        float height = Mathf.Max(
            InlineTextMinHeight,
            Mathf.Max(maxY - minY, GetInlineFontSize(text, worldCorners) + 12f));
        float centerX = (minX + maxX) * 0.5f;
        float centerY = (minY + maxY) * 0.5f;

        float maxWidth = Mathf.Max(InlineTextMinWidth, sceneView.position.width - 16f);
        float maxHeight = Mathf.Max(InlineTextMinHeight, sceneView.position.height - 42f);
        width = Mathf.Min(width, maxWidth);
        height = Mathf.Min(height, maxHeight);

        Rect editorRect = new Rect(
            centerX - width * 0.5f,
            centerY - height * 0.5f,
            width,
            height);
        editorRect.x = Mathf.Clamp(editorRect.x, 8f, sceneView.position.width - width - 8f);
        editorRect.y = Mathf.Clamp(editorRect.y, 8f, sceneView.position.height - height - 26f);
        return editorRect;
    }

    private static GUIStyle GetInlineTextAreaStyle(TMP_Text text, Rect editorRect)
    {
        if (inlineTextAreaStyle == null)
        {
            inlineTextAreaStyle = new GUIStyle(EditorStyles.textArea)
            {
                wordWrap = true,
                richText = false
            };
        }

        Vector3[] worldCorners = new Vector3[4];
        text.rectTransform.GetWorldCorners(worldCorners);
        inlineTextAreaStyle.fontSize = Mathf.RoundToInt(GetInlineFontSize(text, worldCorners));
        inlineTextAreaStyle.alignment = GetInlineTextAnchor(text);

        bool isBold = (text.fontStyle & FontStyles.Bold) != 0;
        bool isItalic = (text.fontStyle & FontStyles.Italic) != 0;
        inlineTextAreaStyle.fontStyle =
            isBold && isItalic ? FontStyle.BoldAndItalic :
            isBold ? FontStyle.Bold :
            isItalic ? FontStyle.Italic :
            FontStyle.Normal;
        inlineTextAreaStyle.normal.textColor = Color.white;
        inlineTextAreaStyle.focused.textColor = Color.white;
        return inlineTextAreaStyle;
    }

    private static float GetInlineFontSize(TMP_Text text, Vector3[] worldCorners)
    {
        float localHeight = Mathf.Abs(text.rectTransform.rect.height);
        if (localHeight <= 0.01f)
        {
            return 14f;
        }

        Vector2 bottomLeft = HandleUtility.WorldToGUIPoint(worldCorners[0]);
        Vector2 topLeft = HandleUtility.WorldToGUIPoint(worldCorners[1]);
        float pixelsPerLocalUnit = Vector2.Distance(bottomLeft, topLeft) / localHeight;
        return Mathf.Clamp(text.fontSize * pixelsPerLocalUnit, 11f, 72f);
    }

    private static TextAnchor GetInlineTextAnchor(TMP_Text text)
    {
        string alignmentName = text.alignment.ToString();
        bool isRight = alignmentName.Contains("Right");
        bool isHorizontalCenter = alignmentName.Contains("Center");
        bool isTop = alignmentName.Contains("Top");
        bool isBottom = alignmentName.Contains("Bottom");

        if (isTop)
        {
            return isRight
                ? TextAnchor.UpperRight
                : isHorizontalCenter
                    ? TextAnchor.UpperCenter
                    : TextAnchor.UpperLeft;
        }

        if (isBottom)
        {
            return isRight
                ? TextAnchor.LowerRight
                : isHorizontalCenter
                    ? TextAnchor.LowerCenter
                    : TextAnchor.LowerLeft;
        }

        return isRight
            ? TextAnchor.MiddleRight
            : isHorizontalCenter
                ? TextAnchor.MiddleCenter
                : TextAnchor.MiddleLeft;
    }

    private static void UpdateHoverPreview(SceneView sceneView, Event currentEvent)
    {
        if (HasSelectionModifier(currentEvent))
        {
            SetHoveredObject(null, sceneView);
            return;
        }

        PrefabStage prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
        if (!IsUIPrefabStage(prefabStage))
        {
            SetHoveredObject(null, sceneView);
            return;
        }

        GameObject pickedObject =
            PickTopmostVisibleUIObject(prefabStage, currentEvent.mousePosition);

        SetHoveredObject(
            IsEditableUIObjectInStage(pickedObject, prefabStage) ? pickedObject : null,
            sceneView);
    }

    private static void BeginClick(Event currentEvent)
    {
        ResetClickState();

        if (currentEvent.button != 0 ||
            HasSelectionModifier(currentEvent))
        {
            return;
        }

        PrefabStage prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
        if (!IsUIPrefabStage(prefabStage))
        {
            return;
        }

        GameObject pickedObject =
            PickTopmostVisibleUIObject(prefabStage, currentEvent.mousePosition);
        if (!IsEditableUIObjectInStage(pickedObject, prefabStage))
        {
            return;
        }

        isClickPending = true;
        mouseDownPosition = currentEvent.mousePosition;
        pressedObject = pickedObject;
    }

    private static void UpdateDragState(
        SceneView sceneView,
        Event currentEvent,
        int currentDirectDragControlId)
    {
        if (!isClickPending)
        {
            return;
        }

        if (isDirectDragging)
        {
            UpdateDirectDrag(sceneView, currentEvent.mousePosition);
            currentEvent.Use();
            return;
        }

        if (Vector2.Distance(mouseDownPosition, currentEvent.mousePosition) < DragThreshold)
        {
            return;
        }

        isDragging = true;
        if (TryBeginDirectDrag(sceneView, currentEvent, currentDirectDragControlId))
        {
            UpdateDirectDrag(sceneView, currentEvent.mousePosition);
            currentEvent.Use();
        }
    }

    private static bool TryBeginDirectDrag(
        SceneView sceneView,
        Event currentEvent,
        int currentDirectDragControlId)
    {
        if (pressedObject == null ||
            pressedObject == Selection.activeGameObject ||
            Tools.current == Tool.View)
        {
            return false;
        }

        PrefabStage prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
        if (!IsUIPrefabStage(prefabStage) ||
            !IsEditableUIObjectInStage(pressedObject, prefabStage) ||
            !IsSceneVisibleAndPickable(pressedObject) ||
            IsBlockedByActiveSceneControl(pressedObject, prefabStage))
        {
            return false;
        }

        RectTransform rectTransform = pressedObject.GetComponent<RectTransform>();
        if (rectTransform == null)
        {
            return false;
        }

        Plane dragPlane = new Plane(rectTransform.forward, rectTransform.position);
        if (!TryGetWorldPointOnPlane(dragPlane, mouseDownPosition, out Vector3 startPointerWorld) ||
            !TryGetWorldPointOnPlane(dragPlane, currentEvent.mousePosition, out _))
        {
            return false;
        }

        Undo.IncrementCurrentGroup();
        directDragUndoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Move UI Element");
        Undo.RecordObject(rectTransform, "Move UI Element");

        draggedRectTransform = rectTransform;
        directDragPlane = dragPlane;
        directDragStartPointerWorld = startPointerWorld;
        directDragStartObjectWorld = rectTransform.position;
        directDragControlId = currentDirectDragControlId;
        isDirectDragging = true;

        GUIUtility.hotControl = directDragControlId;
        GUIUtility.keyboardControl = 0;
        SelectPickedUIObject(pressedObject, prefabStage);
        EditorApplication.RepaintHierarchyWindow();
        sceneView.Repaint();
        return true;
    }

    private static void UpdateDirectDrag(SceneView sceneView, Vector2 mousePosition)
    {
        if (!isDirectDragging ||
            draggedRectTransform == null ||
            !TryGetWorldPointOnPlane(directDragPlane, mousePosition, out Vector3 pointerWorld))
        {
            return;
        }

        draggedRectTransform.position =
            directDragStartObjectWorld + (pointerWorld - directDragStartPointerWorld);
        PrefabUtility.RecordPrefabInstancePropertyModifications(draggedRectTransform);
        EditorUtility.SetDirty(draggedRectTransform);

        PrefabStage prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
        if (prefabStage != null)
        {
            EditorSceneManager.MarkSceneDirty(prefabStage.scene);
        }

        sceneView.Repaint();
    }

    private static bool TryFinishDirectDrag(SceneView sceneView, Event currentEvent)
    {
        if (!isDirectDragging || currentEvent.button != 0)
        {
            return false;
        }

        UpdateDirectDrag(sceneView, currentEvent.mousePosition);
        GameObject draggedObject =
            draggedRectTransform != null ? draggedRectTransform.gameObject : null;

        ReleaseDirectDragControl();
        if (directDragUndoGroup >= 0)
        {
            Undo.CollapseUndoOperations(directDragUndoGroup);
        }

        ClearDirectDragState();
        ResetClickState();
        SetHoveredObject(draggedObject, sceneView);
        currentEvent.Use();
        return true;
    }

    private static void CancelDirectDrag()
    {
        if (!isDirectDragging)
        {
            return;
        }

        ReleaseDirectDragControl();
        if (directDragUndoGroup >= 0)
        {
            Undo.RevertAllDownToGroup(directDragUndoGroup);
        }

        ClearDirectDragState();
    }

    private static void ReleaseDirectDragControl()
    {
        if (GUIUtility.hotControl == directDragControlId)
        {
            GUIUtility.hotControl = 0;
        }
    }

    private static void ClearDirectDragState()
    {
        isDirectDragging = false;
        draggedRectTransform = null;
        directDragControlId = 0;
        directDragUndoGroup = -1;
    }

    private static bool TryGetWorldPointOnPlane(
        Plane plane,
        Vector2 mousePosition,
        out Vector3 worldPoint)
    {
        Ray mouseRay = HandleUtility.GUIPointToWorldRay(mousePosition);
        if (plane.Raycast(mouseRay, out float distance))
        {
            worldPoint = mouseRay.GetPoint(distance);
            return true;
        }

        worldPoint = default;
        return false;
    }

    private static void TrySelectTopmostVisibleUI(SceneView sceneView, Event currentEvent)
    {
        bool shouldHandleClick =
            isClickPending &&
            !isDragging &&
            currentEvent.button == 0 &&
            !HasSelectionModifier(currentEvent);

        ResetClickState();

        if (!shouldHandleClick)
        {
            return;
        }

        PrefabStage prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
        if (!IsUIPrefabStage(prefabStage))
        {
            return;
        }

        GameObject pickedObject = PickTopmostVisibleUIObject(prefabStage, currentEvent.mousePosition);
        if (!IsEditableUIObjectInStage(pickedObject, prefabStage))
        {
            return;
        }

        if (IsBlockedByActiveSceneControl(pickedObject, prefabStage))
        {
            return;
        }

        GUIUtility.hotControl = 0;
        SelectPickedUIObject(pickedObject, prefabStage);
        SetHoveredObject(pickedObject, sceneView);
        currentEvent.Use();

        EditorApplication.RepaintHierarchyWindow();
        sceneView.Repaint();
    }

    private static GameObject PickTopmostVisibleUIObject(PrefabStage prefabStage, Vector2 mousePosition)
    {
        if (prefabStage == null || prefabStage.prefabContentsRoot == null)
        {
            return null;
        }

        Canvas.ForceUpdateCanvases();

        Graphic[] graphics = prefabStage.prefabContentsRoot.GetComponentsInChildren<Graphic>(true);
        Graphic bestGraphic = null;
        int bestDepth = int.MinValue;
        int bestHierarchyOrder = int.MinValue;
        float bestArea = float.MaxValue;

        for (int i = 0; i < graphics.Length; i++)
        {
            Graphic graphic = graphics[i];
            if (!IsSelectableVisibleGraphic(graphic))
            {
                continue;
            }

            RectTransform rectTransform = graphic.rectTransform;
            if (!RectTransformContainsGuiPoint(rectTransform, mousePosition))
            {
                continue;
            }

            int depth = graphic.depth;
            float area = GetGuiRectArea(rectTransform);
            if (bestGraphic == null ||
                depth > bestDepth ||
                (depth == bestDepth && area < bestArea) ||
                (depth == bestDepth && Mathf.Approximately(area, bestArea) && i > bestHierarchyOrder))
            {
                bestGraphic = graphic;
                bestDepth = depth;
                bestHierarchyOrder = i;
                bestArea = area;
            }
        }

        return ResolveSelectionObject(bestGraphic);
    }

    private static TMP_Text PickTopmostVisibleUIText(
        PrefabStage prefabStage,
        Vector2 mousePosition)
    {
        if (prefabStage == null || prefabStage.prefabContentsRoot == null)
        {
            return null;
        }

        TMP_Text selectedText =
            Selection.activeGameObject != null
                ? Selection.activeGameObject.GetComponent<TMP_Text>()
                : null;
        if (IsSelectableVisibleUIText(selectedText, prefabStage, mousePosition))
        {
            return selectedText;
        }

        TMP_Text[] texts =
            prefabStage.prefabContentsRoot.GetComponentsInChildren<TMP_Text>(true);
        TMP_Text bestText = null;
        int bestDepth = int.MinValue;
        int bestHierarchyOrder = int.MinValue;
        float bestArea = float.MaxValue;

        for (int i = 0; i < texts.Length; i++)
        {
            TMP_Text text = texts[i];
            if (!IsSelectableVisibleUIText(text, prefabStage, mousePosition))
            {
                continue;
            }

            Graphic graphic = text as Graphic;
            int depth = graphic.depth;
            float area = GetGuiRectArea(text.rectTransform);
            if (bestText == null ||
                depth > bestDepth ||
                (depth == bestDepth && area < bestArea) ||
                (depth == bestDepth &&
                 Mathf.Approximately(area, bestArea) &&
                 i > bestHierarchyOrder))
            {
                bestText = text;
                bestDepth = depth;
                bestHierarchyOrder = i;
                bestArea = area;
            }
        }

        return bestText;
    }

    private static bool IsSelectableVisibleUIText(
        TMP_Text text,
        PrefabStage prefabStage,
        Vector2 mousePosition)
    {
        Graphic graphic = text as Graphic;
        return graphic != null &&
               IsEditableUIObjectInStage(text.gameObject, prefabStage) &&
               IsSelectableVisibleGraphic(graphic) &&
               RectTransformContainsGuiPoint(text.rectTransform, mousePosition);
    }

    private static bool IsSelectableVisibleGraphic(Graphic graphic)
    {
        if (graphic == null ||
            graphic is EmptyRaycast ||
            !IsSceneVisibleAndPickable(graphic.gameObject) ||
            !graphic.IsActive() ||
            graphic.canvas == null ||
            graphic.canvasRenderer == null ||
            graphic.canvasRenderer.cull ||
            graphic.depth < 0)
        {
            return false;
        }

        Rect rect = graphic.rectTransform.rect;
        if (rect.width <= 0.01f || rect.height <= 0.01f)
        {
            return false;
        }

        return graphic.color.a > 0.01f && graphic.canvasRenderer.GetAlpha() > 0.01f;
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

    private static bool RectTransformContainsGuiPoint(RectTransform rectTransform, Vector2 mousePosition)
    {
        Vector3[] worldCorners = new Vector3[4];
        rectTransform.GetWorldCorners(worldCorners);

        Vector2[] guiCorners =
        {
            HandleUtility.WorldToGUIPoint(worldCorners[0]),
            HandleUtility.WorldToGUIPoint(worldCorners[1]),
            HandleUtility.WorldToGUIPoint(worldCorners[2]),
            HandleUtility.WorldToGUIPoint(worldCorners[3])
        };

        return IsPointInPolygon(mousePosition, guiCorners);
    }

    private static bool IsPointInPolygon(Vector2 point, Vector2[] polygon)
    {
        bool inside = false;
        for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
        {
            Vector2 current = polygon[i];
            Vector2 previous = polygon[j];
            bool crossesY = current.y > point.y != previous.y > point.y;
            if (crossesY)
            {
                float xOnEdge = (previous.x - current.x) * (point.y - current.y) /
                    (previous.y - current.y) + current.x;
                if (point.x < xOnEdge)
                {
                    inside = !inside;
                }
            }
        }

        return inside;
    }

    private static float GetGuiRectArea(RectTransform rectTransform)
    {
        Vector3[] worldCorners = new Vector3[4];
        rectTransform.GetWorldCorners(worldCorners);

        Vector2 bottomLeft = HandleUtility.WorldToGUIPoint(worldCorners[0]);
        Vector2 topLeft = HandleUtility.WorldToGUIPoint(worldCorners[1]);
        Vector2 topRight = HandleUtility.WorldToGUIPoint(worldCorners[2]);

        return Vector2.Distance(bottomLeft, topLeft) * Vector2.Distance(topLeft, topRight);
    }

    private static void SetHoveredObject(GameObject target, SceneView sceneView)
    {
        if (target != null && !IsSceneVisibleAndPickable(target))
        {
            target = null;
        }

        if (hoveredObject == target)
        {
            return;
        }

        hoveredObject = target;
        sceneView.Repaint();
    }

    private static void ClearHoverPreview()
    {
        if (hoveredObject == null)
        {
            return;
        }

        hoveredObject = null;
        SceneView.RepaintAll();
    }

    private static void DrawHoverPreview()
    {
        if (hoveredObject == null ||
            !hoveredObject.activeInHierarchy ||
            !IsSceneVisibleAndPickable(hoveredObject))
        {
            return;
        }

        RectTransform rectTransform = hoveredObject.GetComponent<RectTransform>();
        if (rectTransform == null)
        {
            return;
        }

        Vector3[] worldCorners = new Vector3[4];
        rectTransform.GetWorldCorners(worldCorners);

        Vector3[] guiCorners = new Vector3[4];
        for (int i = 0; i < worldCorners.Length; i++)
        {
            guiCorners[i] = HandleUtility.WorldToGUIPoint(worldCorners[i]);
        }

        Vector3[] outlinePoints =
        {
            guiCorners[0],
            guiCorners[1],
            guiCorners[2],
            guiCorners[3],
            guiCorners[0]
        };

        Color previousColor = Handles.color;
        Handles.BeginGUI();
        try
        {
            Handles.color = HoverFillColor;
            Handles.DrawAAConvexPolygon(guiCorners);

            Handles.color = HoverOutlineColor;
            Handles.DrawAAPolyLine(1.5f, outlinePoints);
        }
        finally
        {
            Handles.color = previousColor;
            Handles.EndGUI();
        }
    }

    private static void SelectPickedUIObject(
        GameObject pickedObject,
        PrefabStage prefabStage)
    {
        GameObject prefabInstanceRoot = PrefabUtility.GetNearestPrefabInstanceRoot(pickedObject);
        Object selectionContext =
            prefabInstanceRoot != null &&
            prefabInstanceRoot != pickedObject &&
            IsEditableUIObjectInStage(prefabInstanceRoot, prefabStage)
                ? prefabInstanceRoot
                : prefabStage.prefabContentsRoot;

        Selection.SetActiveObjectWithContext(pickedObject, selectionContext);
        EditorGUIUtility.PingObject(pickedObject);
    }

    private static bool IsBlockedByActiveSceneControl(
        GameObject pickedObject,
        PrefabStage prefabStage)
    {
        int hotControl = GUIUtility.hotControl;
        if (hotControl == 0 || IsDefaultSceneSelectionControlActive())
        {
            return false;
        }

        // A layout-only RectTransform can cover most or all of the prefab canvas.
        // Rect Tool owns the mouse in that case even for a simple click, which
        // previously prevented the visible Graphic underneath from being selected.
        GameObject activeObject = Selection.activeGameObject;
        bool canClickThroughLayoutRoot =
            activeObject != pickedObject &&
            IsEditableUIObjectInStage(activeObject, prefabStage) &&
            activeObject.GetComponent<Graphic>() == null;

        return !canClickThroughLayoutRoot;
    }

    private static bool IsDefaultSceneSelectionControlActive()
    {
        if (RectSelectionControlIdField == null)
        {
            return false;
        }

        object value = RectSelectionControlIdField.GetValue(null);
        return value is int controlId &&
               controlId != 0 &&
               GUIUtility.hotControl == controlId;
    }

    private static bool IsUIPrefabStage(PrefabStage prefabStage)
    {
        return prefabStage != null &&
               prefabStage.prefabContentsRoot != null &&
               prefabStage.prefabContentsRoot.GetComponentInChildren<Canvas>(true) != null;
    }

    private static bool IsEditableUIObjectInStage(GameObject pickedObject, PrefabStage prefabStage)
    {
        if (pickedObject == null || pickedObject.GetComponent<RectTransform>() == null)
        {
            return false;
        }

        Transform prefabRoot = prefabStage.prefabContentsRoot.transform;
        Transform pickedTransform = pickedObject.transform;
        return pickedTransform == prefabRoot || pickedTransform.IsChildOf(prefabRoot);
    }

    private static bool IsSceneVisibleAndPickable(GameObject target)
    {
        if (target == null)
        {
            return false;
        }

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

    private static void OnSceneVisibilityChanged()
    {
        if (inlineEditingText != null &&
            !IsSceneVisibleAndPickable(inlineEditingText.gameObject))
        {
            CommitInlineTextEdit();
        }

        if (isDirectDragging &&
            draggedRectTransform != null &&
            !IsSceneVisibleAndPickable(draggedRectTransform.gameObject))
        {
            CancelDirectDrag();
        }

        if (pressedObject != null && !IsSceneVisibleAndPickable(pressedObject))
        {
            ResetClickState();
        }

        if (hoveredObject != null && !IsSceneVisibleAndPickable(hoveredObject))
        {
            ClearHoverPreview();
        }

        SceneView.RepaintAll();
    }

    private static bool HasSelectionModifier(Event currentEvent)
    {
        return currentEvent.alt ||
               currentEvent.shift ||
               currentEvent.control ||
               currentEvent.command;
    }

    private static bool IsEnabled()
    {
        return EditorPrefs.GetBool(EnabledEditorPrefKey, true);
    }

    private static void ResetClickState()
    {
        isClickPending = false;
        isDragging = false;
        pressedObject = null;
    }
}
