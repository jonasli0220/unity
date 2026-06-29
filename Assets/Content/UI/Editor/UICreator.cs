using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.UI;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using System;
using System.IO;
using System.Reflection;
using TMPro;
using TMPro.EditorUtilities;

public class UICreator
{
    private const string UIPrefabFolderMarker = "/Content/UI/Prefab/";
    private const float MultiSpriteSpacing = 12f;
    private const float DragPreviewAlpha = 0.72f;
    private const float RightClickDragThreshold = 6f;
    private const string SpriteDragMenuPath = "UITools/拖入图片自动创建UI";
    private const string SpriteDragEnabledEditorPrefKey =
        "SgrProject.UI.SpriteDragToUI.Enabled";
    private const string DefaultTMPFontPath =
        "Assets/Content/UI/Mutilanguage/zh-Hans/TMP_Font/uifont.asset";

    private static readonly List<Sprite> DragPreviewSprites = new List<Sprite>();
    private static readonly List<UnityEngine.Object> DragPreviewTemporaryObjects =
        new List<UnityEngine.Object>();
    private static readonly HashSet<string> SupportedExternalImageExtensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".png",
            ".jpg",
            ".jpeg"
        };
    private static RectTransform dragPreviewParent;
    private static Vector2 dragPreviewMousePosition;
    private static string dragPreviewExternalPathKey;
    private static Image dragPreviewReplacementTarget;
    private static bool isDragPreviewReplacement;
    private static bool isDragPreviewActive;
    private static bool isRightClickPending;
    private static bool isRightClickDragging;
    private static Vector2 rightClickMouseDownPosition;

    public static bool UIWasAddedIntercept = true;

    [InitializeOnLoadMethod]
    public static void InitUICreator()
    {
        if (!EditorPrefs.HasKey(SpriteDragEnabledEditorPrefKey))
        {
            EditorPrefs.SetBool(SpriteDragEnabledEditorPrefKey, true);
        }

        ObjectFactory.componentWasAdded -= OnComponentWasAdded;
        ObjectFactory.componentWasAdded += OnComponentWasAdded;

        SceneView.beforeSceneGui -= HandleUIQuickCreateContextMenu;
        SceneView.beforeSceneGui += HandleUIQuickCreateContextMenu;

        SceneView.duringSceneGui -= DrawSpriteDragPreviewOverlay;
        SceneView.duringSceneGui += DrawSpriteDragPreviewOverlay;

        SceneView.duringSceneGui -= HandleSpriteDragIntoUI;
        SceneView.beforeSceneGui -= HandleSpriteDragIntoUI;
        SceneView.beforeSceneGui += HandleSpriteDragIntoUI;
    }

    [MenuItem(SpriteDragMenuPath)]
    private static void ToggleSpriteDragToUI()
    {
        bool enabled = !IsSpriteDragToUIEnabled();
        EditorPrefs.SetBool(SpriteDragEnabledEditorPrefKey, enabled);
        Menu.SetChecked(SpriteDragMenuPath, enabled);
        ClearSpriteDragPreview(SceneView.lastActiveSceneView);
        SceneView.RepaintAll();
    }

    [MenuItem(SpriteDragMenuPath, true)]
    private static bool ValidateSpriteDragToUI()
    {
        Menu.SetChecked(SpriteDragMenuPath, IsSpriteDragToUIEnabled());
        return true;
    }

    private static bool IsSpriteDragToUIEnabled()
    {
        return EditorPrefs.GetBool(SpriteDragEnabledEditorPrefKey, true);
    }

    private static bool IsStructuralEditingAllowedInCurrentContext()
    {
        if (!EditorApplication.isPlayingOrWillChangePlaymode)
        {
            return true;
        }

        return EditorApplication.isPlaying &&
               IsUIPrefabStage(PrefabStageUtility.GetCurrentPrefabStage());
    }

    private static void HandleUIQuickCreateContextMenu(SceneView sceneView)
    {
        Event currentEvent = Event.current;
        if (currentEvent == null || !IsStructuralEditingAllowedInCurrentContext())
        {
            ResetRightClickState();
            return;
        }

        PrefabStage prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
        bool isLiveBoard = UIDesignBoardLiveScene.IsActive;
        if (!IsUIPrefabStage(prefabStage) && !isLiveBoard)
        {
            ResetRightClickState();
            return;
        }

        if (currentEvent.type == EventType.MouseDown && currentEvent.button == 1)
        {
            ResetRightClickState();
            if (currentEvent.alt ||
                currentEvent.shift ||
                currentEvent.control ||
                currentEvent.command)
            {
                return;
            }

            isRightClickPending = true;
            rightClickMouseDownPosition = currentEvent.mousePosition;
            return;
        }

        if (currentEvent.type == EventType.MouseDrag && isRightClickPending)
        {
            if (Vector2.Distance(
                    rightClickMouseDownPosition,
                    currentEvent.mousePosition) >= RightClickDragThreshold)
            {
                isRightClickDragging = true;
            }

            return;
        }

        bool isRightMouseUp =
            currentEvent.type == EventType.MouseUp && currentEvent.button == 1;
        bool isContextClick = currentEvent.type == EventType.ContextClick;
        if (!isRightMouseUp && !isContextClick)
        {
            if (currentEvent.type == EventType.Ignore)
            {
                ResetRightClickState();
            }

            return;
        }

        bool shouldShowMenu = isRightClickPending && !isRightClickDragging;
        ResetRightClickState();
        if (!shouldShowMenu)
        {
            return;
        }

        RectTransform parent = ResolveSpriteDropParent();
        if (parent == null ||
            (isLiveBoard && !UIDesignBoardLiveScene.IsEditableObject(parent.gameObject)))
        {
            return;
        }

        Vector2 mousePosition = currentEvent.mousePosition;
        Vector3 worldPosition = GetDropWorldPosition(parent, mousePosition);
        ShowUIQuickCreateMenu(prefabStage, parent, worldPosition);

        GUIUtility.hotControl = 0;
        currentEvent.Use();
        sceneView.Repaint();
    }

    private static void ShowUIQuickCreateMenu(
        PrefabStage prefabStage,
        RectTransform parent,
        Vector3 worldPosition)
    {
        Sprite selectedSprite = Selection.activeObject as Sprite;
        GenericMenu menu = new GenericMenu();
        menu.AddDisabledItem(new GUIContent($"添加到：{parent.name}"));
        menu.AddSeparator(string.Empty);
        menu.AddItem(
            new GUIContent("TMP 文本"),
            false,
            () => CreateQuickTMPText(prefabStage, parent, worldPosition));
        menu.AddItem(
            new GUIContent("SgrImage"),
            false,
            () => CreateQuickSgrImage(prefabStage, parent, worldPosition, selectedSprite));
        menu.AddItem(
            new GUIContent("空 UI 节点"),
            false,
            () => CreateQuickEmptyUI(prefabStage, parent, worldPosition));
        menu.AddItem(
            new GUIContent("EmptyRaycast"),
            false,
            () => CreateQuickEmptyRaycast(prefabStage, parent, worldPosition));
        menu.ShowAsContext();
    }

    private static void CreateQuickTMPText(
        PrefabStage prefabStage,
        RectTransform parent,
        Vector3 worldPosition)
    {
        GameObject textObject = CreateQuickUIObject(
            prefabStage,
            parent,
            worldPosition,
            "text",
            "Create TMP Text",
            new Vector2(240f, 60f),
            typeof(CanvasRenderer),
            typeof(XSolution.XMultilanguage.MultiLanguageTMPText));
        if (textObject == null)
        {
            return;
        }

        XSolution.XMultilanguage.MultiLanguageTMPText text =
            textObject.GetComponent<XSolution.XMultilanguage.MultiLanguageTMPText>();
        text.raycastTarget = false;
        text.text = "New Text";
        text.fontSize = 32f;
        text.alignment = TextAlignmentOptions.Center;
        text.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(DefaultTMPFontPath);
        FinalizeQuickCreatedUI(prefabStage, parent, textObject, "Create TMP Text");
        EditorApplication.delayCall += () => BeginQuickCreatedTMPTextEdit(text);
    }

    private static void BeginQuickCreatedTMPTextEdit(TMP_Text text)
    {
        if (text == null)
        {
            return;
        }

        DirectVisibleUISelection.BeginInlineTextEdit(
            text,
            SceneView.lastActiveSceneView);
    }

    private static void CreateQuickSgrImage(
        PrefabStage prefabStage,
        RectTransform parent,
        Vector3 worldPosition,
        Sprite selectedSprite)
    {
        GameObject imageObject = CreateQuickUIObject(
            prefabStage,
            parent,
            worldPosition,
            selectedSprite != null ? selectedSprite.name : "image",
            "Create SgrImage",
            new Vector2(100f, 100f),
            typeof(CanvasRenderer),
            typeof(SgrUnity.SgrImage));
        if (imageObject == null)
        {
            return;
        }

        SgrUnity.SgrImage image = imageObject.GetComponent<SgrUnity.SgrImage>();
        image.raycastTarget = false;
        if (selectedSprite != null)
        {
            image.sprite = selectedSprite;
            image.SetNativeSize();
        }

        FinalizeQuickCreatedUI(prefabStage, parent, imageObject, "Create SgrImage");
    }

    private static void CreateQuickEmptyUI(
        PrefabStage prefabStage,
        RectTransform parent,
        Vector3 worldPosition)
    {
        GameObject emptyObject = CreateQuickUIObject(
            prefabStage,
            parent,
            worldPosition,
            "empty",
            "Create Empty UI",
            new Vector2(160f, 100f));
        FinalizeQuickCreatedUI(prefabStage, parent, emptyObject, "Create Empty UI");
    }

    private static void CreateQuickEmptyRaycast(
        PrefabStage prefabStage,
        RectTransform parent,
        Vector3 worldPosition)
    {
        GameObject emptyRaycastObject = CreateQuickUIObject(
            prefabStage,
            parent,
            worldPosition,
            "empty",
            "Create EmptyRaycast",
            new Vector2(160f, 100f),
            typeof(CanvasRenderer),
            typeof(EmptyRaycast));
        FinalizeQuickCreatedUI(
            prefabStage,
            parent,
            emptyRaycastObject,
            "Create EmptyRaycast");
    }

    private static GameObject CreateQuickUIObject(
        PrefabStage prefabStage,
        RectTransform parent,
        Vector3 worldPosition,
        string objectName,
        string undoName,
        Vector2 size,
        params Type[] additionalComponentTypes)
    {
        if (parent == null ||
            !IsInsideCurrentEditingContext(parent, prefabStage) ||
            (UIDesignBoardLiveScene.IsActive &&
             !UIDesignBoardLiveScene.IsEditableObject(parent.gameObject)))
        {
            return null;
        }

        List<Type> componentTypes = new List<Type>
        {
            typeof(RectTransform)
        };
        componentTypes.AddRange(additionalComponentTypes);

        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName(undoName);

        GameObject createdObject = ObjectFactory.CreateGameObject(
            objectName,
            componentTypes.ToArray());
        RectTransform rectTransform = createdObject.GetComponent<RectTransform>();
        Undo.SetTransformParent(rectTransform, parent, undoName);
        GameObjectUtility.EnsureUniqueNameForSibling(createdObject);

        rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
        rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        rectTransform.pivot = new Vector2(0.5f, 0.5f);
        rectTransform.localRotation = Quaternion.identity;
        rectTransform.localScale = Vector3.one;
        rectTransform.sizeDelta = size;
        rectTransform.position = worldPosition;
        createdObject.layer = parent.gameObject.layer;

        return createdObject;
    }

    private static void FinalizeQuickCreatedUI(
        PrefabStage prefabStage,
        RectTransform parent,
        GameObject createdObject,
        string undoName)
    {
        if (createdObject == null)
        {
            return;
        }

        Undo.RegisterFullObjectHierarchyUndo(parent.gameObject, undoName);
        Undo.CollapseUndoOperations(Undo.GetCurrentGroup());
        Scene editedScene = prefabStage != null ? prefabStage.scene : parent.gameObject.scene;
        if (editedScene.IsValid())
        {
            EditorSceneManager.MarkSceneDirty(editedScene);
        }
        Selection.activeGameObject = createdObject;
        SceneView.RepaintAll();
    }

    private static void ResetRightClickState()
    {
        isRightClickPending = false;
        isRightClickDragging = false;
    }

    private static void HandleSpriteDragIntoUI(SceneView sceneView)
    {
        if (!IsSpriteDragToUIEnabled() ||
            !IsStructuralEditingAllowedInCurrentContext())
        {
            ClearSpriteDragPreview(sceneView);
            return;
        }

        Event currentEvent = Event.current;
        if (currentEvent == null)
        {
            return;
        }

        if (currentEvent.type == EventType.DragExited)
        {
            ClearSpriteDragPreview(sceneView);
            return;
        }

        if (currentEvent.type == EventType.KeyDown && currentEvent.keyCode == KeyCode.Escape)
        {
            ClearSpriteDragPreview(sceneView);
            return;
        }

        if (currentEvent.type != EventType.DragUpdated && currentEvent.type != EventType.DragPerform)
        {
            return;
        }

        List<string> externalImagePaths = new List<string>();
        bool isExternalImageDrag = TryGetExternalImagePaths(externalImagePaths);
        List<Sprite> sprites = new List<Sprite>();
        PrefabStage prefabStage = PrefabStageUtility.GetCurrentPrefabStage();

        if (isExternalImageDrag)
        {
            if (!IsUIPrefabStage(prefabStage))
            {
                ClearSpriteDragPreview(sceneView);
                return;
            }

            Image replacementTarget = externalImagePaths.Count == 1
                ? ResolveSelectedImageReplacementTarget(prefabStage)
                : null;
            if (replacementTarget != null)
            {
                EventType replacementEventType = currentEvent.type;
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;

                if (replacementEventType != EventType.DragPerform)
                {
                    UpdateExternalImageReplacementPreview(
                        sceneView,
                        replacementTarget,
                        externalImagePaths[0],
                        currentEvent.mousePosition);
                    currentEvent.Use();
                    return;
                }

                ClearSpriteDragPreview(sceneView);
                DragAndDrop.AcceptDrag();
                sprites = ImportExternalImagesForCurrentPrefab(externalImagePaths);
                if (sprites.Count > 0)
                {
                    ReplaceSelectedImageSprite(replacementTarget, sprites[0]);
                }

                currentEvent.Use();
                return;
            }
        }
        else if (!TryGetDraggedSprites(sprites))
        {
            ClearSpriteDragPreview(sceneView);
            return;
        }

        RectTransform parent = ResolveSpriteDropParent();
        if (parent == null ||
            (UIDesignBoardLiveScene.IsActive &&
             !UIDesignBoardLiveScene.IsEditableObject(parent.gameObject)))
        {
            ClearSpriteDragPreview(sceneView);
            return;
        }

        EventType eventType = currentEvent.type;
        DragAndDrop.visualMode = DragAndDropVisualMode.Copy;

        if (eventType != EventType.DragPerform)
        {
            if (isExternalImageDrag)
            {
                UpdateExternalImageDragPreview(
                    sceneView,
                    parent,
                    externalImagePaths,
                    currentEvent.mousePosition);
            }
            else
            {
                UpdateSpriteDragPreview(sceneView, parent, sprites, currentEvent.mousePosition);
            }

            currentEvent.Use();
            return;
        }

        ClearSpriteDragPreview(sceneView);
        DragAndDrop.AcceptDrag();

        if (isExternalImageDrag)
        {
            sprites = ImportExternalImagesForCurrentPrefab(externalImagePaths);
            if (sprites.Count == 0)
            {
                currentEvent.Use();
                return;
            }
        }

        CreateDraggedSgrImages(parent, sprites, currentEvent.mousePosition);
        currentEvent.Use();
    }

    private static void DrawSpriteDragPreviewOverlay(SceneView sceneView)
    {
        Event currentEvent = Event.current;
        if (currentEvent == null || currentEvent.type != EventType.Repaint)
        {
            return;
        }

        DrawSpriteDragPreview();
    }

    private static void UpdateSpriteDragPreview(
        SceneView sceneView,
        RectTransform parent,
        List<Sprite> sprites,
        Vector2 mousePosition)
    {
        DestroyDragPreviewTemporaryObjects();
        dragPreviewExternalPathKey = null;
        DragPreviewSprites.Clear();
        DragPreviewSprites.AddRange(sprites);
        dragPreviewParent = parent;
        dragPreviewMousePosition = mousePosition;
        dragPreviewReplacementTarget = null;
        isDragPreviewReplacement = false;
        isDragPreviewActive = true;
        sceneView.Repaint();
    }

    private static void UpdateExternalImageDragPreview(
        SceneView sceneView,
        RectTransform parent,
        List<string> externalImagePaths,
        Vector2 mousePosition)
    {
        string pathKey = string.Join("\n", externalImagePaths.ToArray());
        if (!string.Equals(
                dragPreviewExternalPathKey,
                pathKey,
                StringComparison.OrdinalIgnoreCase))
        {
            DestroyDragPreviewTemporaryObjects();
            DragPreviewSprites.Clear();

            for (int i = 0; i < externalImagePaths.Count; i++)
            {
                Sprite previewSprite = CreateExternalImagePreviewSprite(externalImagePaths[i]);
                if (previewSprite != null)
                {
                    DragPreviewSprites.Add(previewSprite);
                }
            }

            dragPreviewExternalPathKey = pathKey;
        }

        if (DragPreviewSprites.Count == 0)
        {
            ClearSpriteDragPreview(sceneView);
            return;
        }

        dragPreviewParent = parent;
        dragPreviewMousePosition = mousePosition;
        dragPreviewReplacementTarget = null;
        isDragPreviewReplacement = false;
        isDragPreviewActive = true;
        sceneView.Repaint();
    }

    private static void UpdateExternalImageReplacementPreview(
        SceneView sceneView,
        Image replacementTarget,
        string externalImagePath,
        Vector2 mousePosition)
    {
        DestroyDragPreviewTemporaryObjects();
        DragPreviewSprites.Clear();
        dragPreviewParent = null;
        dragPreviewExternalPathKey = externalImagePath;
        dragPreviewReplacementTarget = replacementTarget;
        dragPreviewMousePosition = mousePosition;
        isDragPreviewReplacement = true;
        isDragPreviewActive = true;
        sceneView.Repaint();
    }

    private static Sprite CreateExternalImagePreviewSprite(string externalPath)
    {
        try
        {
            byte[] imageBytes = File.ReadAllBytes(externalPath);
            Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            texture.hideFlags = HideFlags.HideAndDontSave;

            if (!texture.LoadImage(imageBytes, true))
            {
                UnityEngine.Object.DestroyImmediate(texture);
                return null;
            }

            texture.name = Path.GetFileNameWithoutExtension(externalPath);
            Sprite sprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(0.5f, 0.5f),
                100f,
                0u,
                SpriteMeshType.FullRect);
            sprite.name = texture.name;
            sprite.hideFlags = HideFlags.HideAndDontSave;

            DragPreviewTemporaryObjects.Add(sprite);
            DragPreviewTemporaryObjects.Add(texture);
            return sprite;
        }
        catch (Exception exception)
        {
            Debug.LogWarning(
                "Unable to preview external UI image: " + externalPath + "\n" + exception.Message);
            return null;
        }
    }

    private static void DestroyDragPreviewTemporaryObjects()
    {
        for (int i = 0; i < DragPreviewTemporaryObjects.Count; i++)
        {
            UnityEngine.Object temporaryObject = DragPreviewTemporaryObjects[i];
            if (temporaryObject != null)
            {
                UnityEngine.Object.DestroyImmediate(temporaryObject);
            }
        }

        DragPreviewTemporaryObjects.Clear();
    }

    private static void ClearSpriteDragPreview(SceneView sceneView)
    {
        bool shouldRepaint = isDragPreviewActive;
        DestroyDragPreviewTemporaryObjects();
        DragPreviewSprites.Clear();
        dragPreviewParent = null;
        dragPreviewExternalPathKey = null;
        dragPreviewReplacementTarget = null;
        isDragPreviewReplacement = false;
        isDragPreviewActive = false;

        if (shouldRepaint && sceneView != null)
        {
            sceneView.Repaint();
        }
    }

    private static void DrawSpriteDragPreview()
    {
        if (!isDragPreviewActive)
        {
            return;
        }

        if (isDragPreviewReplacement)
        {
            DrawExternalImageReplacementFeedback();
            return;
        }

        if (dragPreviewParent == null || DragPreviewSprites.Count == 0)
        {
            return;
        }

        Vector3 dropWorldPosition = GetDropWorldPosition(dragPreviewParent, dragPreviewMousePosition);
        float horizontalOffset = 0f;

        Handles.BeginGUI();
        Color previousColor = GUI.color;
        GUI.color = new Color(1f, 1f, 1f, DragPreviewAlpha);

        for (int i = 0; i < DragPreviewSprites.Count; i++)
        {
            Sprite sprite = DragPreviewSprites[i];
            if (sprite == null || sprite.texture == null)
            {
                continue;
            }

            Vector2 nativeSize = GetNativeSpriteSize(sprite, dragPreviewParent);
            Vector3 centerWorldPosition = dropWorldPosition
                + dragPreviewParent.TransformVector(new Vector3(horizontalOffset, 0f, 0f));
            Rect guiRect = GetSpritePreviewGuiRect(dragPreviewParent, centerWorldPosition, nativeSize);

            DrawSpriteTexture(guiRect, sprite);
            horizontalOffset += nativeSize.x + MultiSpriteSpacing;
        }

        GUI.color = previousColor;
        Handles.EndGUI();
    }

    private static void DrawExternalImageReplacementFeedback()
    {
        if (dragPreviewReplacementTarget == null)
        {
            return;
        }

        RectTransform targetRect = dragPreviewReplacementTarget.rectTransform;
        Vector3[] worldCorners = new Vector3[4];
        targetRect.GetWorldCorners(worldCorners);

        Handles.color = new Color(1f, 0.72f, 0.12f, 1f);
        Handles.DrawAAPolyLine(
            3f,
            worldCorners[0],
            worldCorners[1],
            worldCorners[2],
            worldCorners[3],
            worldCorners[0]);

        Handles.BeginGUI();
        GUIStyle labelStyle = new GUIStyle(EditorStyles.helpBox);
        labelStyle.alignment = TextAnchor.MiddleCenter;
        labelStyle.fontStyle = FontStyle.Bold;
        labelStyle.normal.textColor = Color.white;

        GUIContent label = new GUIContent("松手替换");
        Vector2 labelSize = labelStyle.CalcSize(label);
        Rect labelRect = new Rect(
            dragPreviewMousePosition.x + 16f,
            dragPreviewMousePosition.y + 18f,
            labelSize.x + 18f,
            labelSize.y + 8f);

        Color previousColor = GUI.color;
        GUI.color = new Color(1f, 0.64f, 0.08f, 0.96f);
        GUI.Box(labelRect, label, labelStyle);
        GUI.color = previousColor;
        Handles.EndGUI();
    }

    private static Vector2 GetNativeSpriteSize(Sprite sprite, RectTransform parent)
    {
        Canvas canvas = parent.GetComponentInParent<Canvas>();
        float referencePixelsPerUnit = canvas != null ? canvas.referencePixelsPerUnit : 100f;
        float spritePixelsPerUnit = sprite.pixelsPerUnit > 0f ? sprite.pixelsPerUnit : 100f;
        float pixelsPerUnit = spritePixelsPerUnit / referencePixelsPerUnit;

        return sprite.rect.size / pixelsPerUnit;
    }

    private static Rect GetSpritePreviewGuiRect(
        RectTransform parent,
        Vector3 centerWorldPosition,
        Vector2 nativeSize)
    {
        Vector3 right = parent.TransformVector(new Vector3(nativeSize.x * 0.5f, 0f, 0f));
        Vector3 up = parent.TransformVector(new Vector3(0f, nativeSize.y * 0.5f, 0f));

        Vector2 bottomLeft = HandleUtility.WorldToGUIPoint(centerWorldPosition - right - up);
        Vector2 bottomRight = HandleUtility.WorldToGUIPoint(centerWorldPosition + right - up);
        Vector2 topLeft = HandleUtility.WorldToGUIPoint(centerWorldPosition - right + up);
        Vector2 topRight = HandleUtility.WorldToGUIPoint(centerWorldPosition + right + up);

        float minX = Mathf.Min(bottomLeft.x, bottomRight.x, topLeft.x, topRight.x);
        float maxX = Mathf.Max(bottomLeft.x, bottomRight.x, topLeft.x, topRight.x);
        float minY = Mathf.Min(bottomLeft.y, bottomRight.y, topLeft.y, topRight.y);
        float maxY = Mathf.Max(bottomLeft.y, bottomRight.y, topLeft.y, topRight.y);

        return Rect.MinMaxRect(minX, minY, maxX, maxY);
    }

    private static void DrawSpriteTexture(Rect guiRect, Sprite sprite)
    {
        if (guiRect.width <= 0f || guiRect.height <= 0f)
        {
            return;
        }

        Texture2D texture = sprite.texture;
        Rect textureRect;

        try
        {
            textureRect = sprite.textureRect;
        }
        catch (UnityException)
        {
            Texture2D previewTexture = AssetPreview.GetAssetPreview(sprite);
            if (previewTexture != null)
            {
                GUI.DrawTexture(guiRect, previewTexture, ScaleMode.ScaleToFit, true);
            }

            return;
        }

        Rect textureCoordinates = new Rect(
            textureRect.x / texture.width,
            textureRect.y / texture.height,
            textureRect.width / texture.width,
            textureRect.height / texture.height);

        Vector2 textureRectOffset = sprite.textureRectOffset;
        Vector2 spriteRectSize = sprite.rect.size;
        if (spriteRectSize.x <= 0f || spriteRectSize.y <= 0f)
        {
            return;
        }

        float scaleX = guiRect.width / spriteRectSize.x;
        float scaleY = guiRect.height / spriteRectSize.y;
        Rect visibleGuiRect = new Rect(
            guiRect.x + textureRectOffset.x * scaleX,
            guiRect.y
                + (spriteRectSize.y - textureRectOffset.y - textureRect.height) * scaleY,
            textureRect.width * scaleX,
            textureRect.height * scaleY);

        GUI.DrawTextureWithTexCoords(visibleGuiRect, texture, textureCoordinates, true);
    }

    private static bool IsUIPrefabStage(PrefabStage prefabStage)
    {
        if (prefabStage == null
            || prefabStage.prefabContentsRoot == null
            || string.IsNullOrEmpty(prefabStage.assetPath))
        {
            return false;
        }

        string normalizedPath = prefabStage.assetPath.Replace('\\', '/');
        if (normalizedPath.IndexOf(UIPrefabFolderMarker, StringComparison.OrdinalIgnoreCase) < 0
            || !normalizedPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return prefabStage.prefabContentsRoot.GetComponentInChildren<Canvas>(true) != null;
    }

    private static bool TryGetExternalImagePaths(List<string> externalImagePaths)
    {
        string[] draggedPaths = DragAndDrop.paths;
        if (draggedPaths == null || draggedPaths.Length == 0)
        {
            return false;
        }

        for (int i = 0; i < draggedPaths.Length; i++)
        {
            string draggedPath = draggedPaths[i];
            if (string.IsNullOrEmpty(draggedPath)
                || draggedPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)
                || !Path.IsPathRooted(draggedPath)
                || !File.Exists(draggedPath)
                || !SupportedExternalImageExtensions.Contains(Path.GetExtension(draggedPath)))
            {
                externalImagePaths.Clear();
                return false;
            }

            externalImagePaths.Add(Path.GetFullPath(draggedPath));
        }

        return externalImagePaths.Count > 0;
    }

    private static Image ResolveSelectedImageReplacementTarget(PrefabStage prefabStage)
    {
        GameObject selectedObject = Selection.activeGameObject;
        if (selectedObject == null || !IsUIPrefabStage(prefabStage))
        {
            return null;
        }

        Image selectedImage = selectedObject.GetComponent<Image>();
        if (selectedImage == null
            || selectedImage.rectTransform == null
            || !IsInsidePrefabStage(selectedImage.rectTransform, prefabStage))
        {
            return null;
        }

        return selectedImage;
    }

    private static void ReplaceSelectedImageSprite(Image targetImage, Sprite replacementSprite)
    {
        if (targetImage == null || replacementSprite == null)
        {
            return;
        }

        const string undoName = "Replace UI Image From External File";

        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName(undoName);
        Undo.RecordObject(targetImage, undoName);

        targetImage.overrideSprite = null;
        targetImage.sprite = replacementSprite;

        EditorUtility.SetDirty(targetImage);
        PrefabUtility.RecordPrefabInstancePropertyModifications(targetImage);
        EditorSceneManager.MarkSceneDirty(targetImage.gameObject.scene);
        Undo.CollapseUndoOperations(undoGroup);

        Selection.activeGameObject = targetImage.gameObject;
        SceneView.RepaintAll();
    }

    private static List<Sprite> ImportExternalImagesForCurrentPrefab(
        List<string> externalImagePaths)
    {
        List<Sprite> importedSprites = new List<Sprite>();
        PrefabStage prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
        string targetFolder = GetCurrentPrefabResourceFolder(prefabStage);
        if (string.IsNullOrEmpty(targetFolder))
        {
            return importedSprites;
        }

        EnsureAssetFolderExists(targetFolder);
        if (!AssetDatabase.IsValidFolder(targetFolder))
        {
            Debug.LogError("Unable to create UI resource folder: " + targetFolder);
            return importedSprites;
        }

        for (int i = 0; i < externalImagePaths.Count; i++)
        {
            string externalPath = externalImagePaths[i];
            try
            {
                string fileName = Path.GetFileName(externalPath);
                string assetPath = targetFolder + "/" + fileName;
                string absoluteAssetPath = AssetPathToAbsolutePath(assetPath);
                if ((AssetDatabase.LoadMainAssetAtPath(assetPath) != null
                        || File.Exists(absoluteAssetPath))
                    && !EditorUtility.DisplayDialog(
                        "替换同名图片？",
                        "resource 文件夹里已经存在同名资源：\n\n" +
                        assetPath +
                        "\n\n是否用当前拖入的图片替换它？\n确认后会保留原资源 GUID，已有引用会指向新图片。",
                        "替换",
                        "跳过"))
                {
                    continue;
                }

                if (!string.Equals(
                        Path.GetFullPath(externalPath),
                        Path.GetFullPath(absoluteAssetPath),
                        StringComparison.OrdinalIgnoreCase))
                {
                    File.Copy(externalPath, absoluteAssetPath, true);
                }

                AssetDatabase.ImportAsset(
                    assetPath,
                    ImportAssetOptions.ForceSynchronousImport);
                ConfigureImportedSprite(assetPath);

                Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
                if (sprite == null)
                {
                    Debug.LogError("Imported image is not a Sprite: " + assetPath);
                    continue;
                }

                importedSprites.Add(sprite);
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    "Unable to import external UI image: " + externalPath + "\n" + exception);
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        return importedSprites;
    }

    private static string GetCurrentPrefabResourceFolder(PrefabStage prefabStage)
    {
        if (!IsUIPrefabStage(prefabStage))
        {
            return null;
        }

        string prefabDirectory = Path.GetDirectoryName(prefabStage.assetPath);
        if (string.IsNullOrEmpty(prefabDirectory))
        {
            return null;
        }

        return prefabDirectory.Replace('\\', '/') + "/resource";
    }

    private static void EnsureAssetFolderExists(string assetFolder)
    {
        if (AssetDatabase.IsValidFolder(assetFolder))
        {
            return;
        }

        string parentFolder = Path.GetDirectoryName(assetFolder);
        string folderName = Path.GetFileName(assetFolder);
        if (string.IsNullOrEmpty(parentFolder) || string.IsNullOrEmpty(folderName))
        {
            return;
        }

        parentFolder = parentFolder.Replace('\\', '/');
        if (!AssetDatabase.IsValidFolder(parentFolder))
        {
            EnsureAssetFolderExists(parentFolder);
        }

        if (AssetDatabase.IsValidFolder(parentFolder))
        {
            AssetDatabase.CreateFolder(parentFolder, folderName);
        }
    }

    private static void ConfigureImportedSprite(string assetPath)
    {
        TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        if (importer == null)
        {
            return;
        }

        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = 100f;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.SaveAndReimport();
    }

    private static string AssetPathToAbsolutePath(string assetPath)
    {
        string projectRoot = Path.GetDirectoryName(Application.dataPath);
        return Path.GetFullPath(Path.Combine(projectRoot, assetPath));
    }

    private static bool TryGetDraggedSprites(List<Sprite> sprites)
    {
        string[] draggedPaths = DragAndDrop.paths;
        if (draggedPaths == null || draggedPaths.Length == 0)
        {
            return false;
        }

        UnityEngine.Object[] draggedObjects = DragAndDrop.objectReferences;
        if (draggedObjects == null || draggedObjects.Length == 0)
        {
            return false;
        }

        bool hasValidAssetPath = false;
        for (int i = 0; i < draggedPaths.Length; i++)
        {
            if (!string.IsNullOrEmpty(draggedPaths[i]) &&
                draggedPaths[i].StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            {
                hasValidAssetPath = true;
                break;
            }
        }

        if (!hasValidAssetPath)
        {
            return false;
        }

        HashSet<int> spriteIds = new HashSet<int>();
        for (int i = 0; i < draggedObjects.Length; i++)
        {
            UnityEngine.Object draggedObject = draggedObjects[i];
            Sprite sprite = draggedObject as Sprite;

            if (sprite == null && draggedObject is Texture2D)
            {
                sprite = LoadSingleSpriteFromTexture(draggedObject);
            }

            if (sprite == null)
            {
                return false;
            }

            if (spriteIds.Add(sprite.GetInstanceID()))
            {
                sprites.Add(sprite);
            }
        }

        return sprites.Count > 0;
    }

    private static Sprite LoadSingleSpriteFromTexture(UnityEngine.Object texture)
    {
        string assetPath = AssetDatabase.GetAssetPath(texture);
        if (string.IsNullOrEmpty(assetPath))
        {
            return null;
        }

        UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
        Sprite foundSprite = null;
        for (int i = 0; i < assets.Length; i++)
        {
            Sprite sprite = assets[i] as Sprite;
            if (sprite == null)
            {
                continue;
            }

            if (foundSprite != null)
            {
                return null;
            }

            foundSprite = sprite;
        }

        return foundSprite;
    }

    private static RectTransform ResolveSpriteDropParent()
    {
        PrefabStage prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
        Transform selectedTransform = Selection.activeTransform;
        RectTransform selectedRect = selectedTransform as RectTransform;
        if (selectedRect != null &&
            selectedRect.GetComponentInParent<Canvas>() != null &&
            IsInsideCurrentEditingContext(selectedRect, prefabStage))
        {
            return selectedRect;
        }

        if (prefabStage != null && prefabStage.prefabContentsRoot != null)
        {
            Canvas[] prefabCanvases =
                prefabStage.prefabContentsRoot.GetComponentsInChildren<Canvas>(true);
            for (int i = 0; i < prefabCanvases.Length; i++)
            {
                RectTransform canvasRect = prefabCanvases[i].transform as RectTransform;
                if (canvasRect != null && IsInsidePrefabStage(canvasRect, prefabStage))
                {
                    return canvasRect;
                }
            }

            return null;
        }

        Scene activeScene = SceneManager.GetActiveScene();
        Canvas[] canvases = UnityEngine.Object.FindObjectsOfType<Canvas>(true);
        for (int i = 0; i < canvases.Length; i++)
        {
            RectTransform canvasRect = canvases[i].transform as RectTransform;
            if (canvasRect != null &&
                canvasRect.gameObject.scene == activeScene &&
                !EditorUtility.IsPersistent(canvasRect.gameObject))
            {
                return canvasRect;
            }
        }

        return null;
    }

    private static bool IsInsideCurrentEditingContext(
        Transform transform,
        PrefabStage prefabStage)
    {
        if (transform == null || EditorUtility.IsPersistent(transform.gameObject))
        {
            return false;
        }

        if (prefabStage != null)
        {
            return IsInsidePrefabStage(transform, prefabStage);
        }

        return transform.gameObject.scene.IsValid() &&
               transform.gameObject.scene == SceneManager.GetActiveScene();
    }

    private static bool IsInsidePrefabStage(Transform transform, PrefabStage prefabStage)
    {
        Transform prefabRoot = prefabStage.prefabContentsRoot.transform;
        return transform != null
            && transform.gameObject.scene == prefabStage.scene
            && (transform == prefabRoot || transform.IsChildOf(prefabRoot));
    }

    private static void CreateDraggedSgrImages(
        RectTransform parent,
        List<Sprite> sprites,
        Vector2 mousePosition)
    {
        Vector3 dropWorldPosition = GetDropWorldPosition(parent, mousePosition);
        List<GameObject> createdObjects = new List<GameObject>();
        float horizontalOffset = 0f;

        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Create UI Image From Sprite");

        for (int i = 0; i < sprites.Count; i++)
        {
            Sprite sprite = sprites[i];
            GameObject imageObject = ObjectFactory.CreateGameObject(
                sprite.name,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(SgrUnity.SgrImage));

            GameObjectUtility.EnsureUniqueNameForSibling(imageObject);

            RectTransform rectTransform = imageObject.GetComponent<RectTransform>();
            Undo.SetTransformParent(rectTransform, parent, "Create UI Image From Sprite");
            rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            rectTransform.pivot = new Vector2(0.5f, 0.5f);
            rectTransform.localRotation = Quaternion.identity;
            rectTransform.localScale = Vector3.one;

            imageObject.layer = parent.gameObject.layer;

            SgrUnity.SgrImage image = imageObject.GetComponent<SgrUnity.SgrImage>();
            image.sprite = sprite;
            image.raycastTarget = false;
            image.SetNativeSize();

            rectTransform.position = dropWorldPosition;
            rectTransform.anchoredPosition += new Vector2(horizontalOffset, 0f);
            horizontalOffset += rectTransform.rect.width + MultiSpriteSpacing;

            createdObjects.Add(imageObject);
        }

        Undo.RegisterFullObjectHierarchyUndo(parent.gameObject, "Create UI Image From Sprite");
        Undo.CollapseUndoOperations(undoGroup);
        EditorSceneManager.MarkSceneDirty(parent.gameObject.scene);
        Selection.objects = createdObjects.ToArray();
        SceneView.RepaintAll();
    }

    private static Vector3 GetDropWorldPosition(RectTransform parent, Vector2 mousePosition)
    {
        Ray ray = HandleUtility.GUIPointToWorldRay(mousePosition);
        Plane plane = new Plane(parent.forward, parent.position);
        float distance;
        if (plane.Raycast(ray, out distance))
        {
            return ray.GetPoint(distance);
        }

        return parent.position;
    }

    private static void OnComponentWasAdded(Component obj)
    {
        if (!UIWasAddedIntercept)
        {
            return;
        }

        Text text = obj as Text;
        if (text != null)
        {
            EditorApplication.delayCall += () =>
            {
                if (text != null)
                {
                    //UGUITextToTMP.TextToTMP(text);
                }
            };
            return;
        }
        InputField input = obj as InputField;
        if (input != null)
        {
            EditorApplication.delayCall += () =>
            {
                if (input != null)
                {
                    Transform pr = input.transform.parent;
                    TMPro.TMP_DefaultControls.Resources resources = new TMPro.TMP_DefaultControls.Resources();
                    string kInputFieldBackgroundPath = "UI/Skin/InputFieldBackground.psd";
                    resources.inputField = AssetDatabase.GetBuiltinExtraResource<Sprite>(kInputFieldBackgroundPath);
                    GameObject go = TMPro.TMP_DefaultControls.CreateInputField(resources);
                    go.transform.SetParent(pr);
                    GameObject.DestroyImmediate(input.gameObject, true);
                    Selection.activeGameObject = go;
                }
            };
        }

        Dropdown dropdown = obj as Dropdown;
        if (dropdown != null)
        {
            EditorApplication.delayCall += () =>
            {
                if (dropdown != null)
                {
                    Transform pr = dropdown.transform.parent;
                    TMPro.TMP_DefaultControls.Resources resources = new TMPro.TMP_DefaultControls.Resources();
                    string kStandardSpritePath = "UI/Skin/UISprite.psd";
                    string kBackgroundSpritePath = "UI/Skin/Background.psd";
                    string kInputFieldBackgroundPath = "UI/Skin/InputFieldBackground.psd";
                    string kKnobPath = "UI/Skin/Knob.psd";
                    string kCheckmarkPath = "UI/Skin/Checkmark.psd";
                    string kDropdownArrowPath = "UI/Skin/DropdownArrow.psd";
                    string kMaskPath = "UI/Skin/UIMask.psd";

                    resources.standard = AssetDatabase.GetBuiltinExtraResource<Sprite>(kStandardSpritePath);
                    resources.background = AssetDatabase.GetBuiltinExtraResource<Sprite>(kBackgroundSpritePath);
                    resources.inputField = AssetDatabase.GetBuiltinExtraResource<Sprite>(kInputFieldBackgroundPath);
                    resources.knob = AssetDatabase.GetBuiltinExtraResource<Sprite>(kKnobPath);
                    resources.checkmark = AssetDatabase.GetBuiltinExtraResource<Sprite>(kCheckmarkPath);
                    resources.dropdown = AssetDatabase.GetBuiltinExtraResource<Sprite>(kDropdownArrowPath);
                    resources.mask = AssetDatabase.GetBuiltinExtraResource<Sprite>(kMaskPath);
                    GameObject go = TMPro.TMP_DefaultControls.CreateDropdown(resources);
                    go.transform.SetParent(pr);
                    GameObject.DestroyImmediate(dropdown.gameObject, true);
                    Selection.activeGameObject = go;
                }
            };
        }

        
        if (obj.GetType() == typeof(Image))
        {
            Image image = (Image)obj;
            EditorApplication.delayCall += () =>
            {
                if (image != null)
                {
                    UtilEditorEx.ChangeComponent<SgrUnity.SgrImage>(image);
                }
            };
        }

        if (obj.GetType() == typeof(ScrollRect))
        {
            ScrollRect scroll = (ScrollRect)obj;
            EditorApplication.delayCall += () =>
            {
                if (scroll != null)
                {
                    scroll.scrollSensitivity = 100;
                    EditorUtility.SetDirty(scroll);
                }
            };
        }
    }

    static Transform GetCanvas()
    {
        Transform canvas;
        if (Selection.activeTransform)
        {
            if (Selection.activeTransform.GetComponentInParent<Canvas>())
                canvas = Selection.activeTransform;
            else
                canvas = CreateParentCanvas(Selection.activeTransform);
        }
        else
            canvas = CreateParentCanvas();
        return canvas;
    }

    /// <summary>
    /// 没有canvas的时候需要创建父级的canvas
    /// </summary>
    /// <param name="parent"></param>
    /// <returns></returns>
    static Transform CreateParentCanvas(Transform parent = null)
    {
        GameObject canvas = new GameObject("canvas", typeof(Canvas));
        if (parent != null)
        {
            canvas.transform.SetParent(parent);
        }
        Canvas canvas_com = canvas.GetComponent<Canvas>();
        canvas_com.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.AddComponent<CanvasScaler>();
        canvas.AddComponent<GraphicRaycaster>();
        canvas.gameObject.layer = 5;
        return canvas.transform;
    }

    /// <summary>
    /// 自动取消Image RaycastTarget
    /// </summary>
    [MenuItem("GameObject/UI/Image")]
    static Image CreateImage()
    {
        return CreateSgrImage();
    }

    /// <summary>
    /// 自动取消RawImage RaycastTarget
    /// </summary>
    [MenuItem("GameObject/UI/Raw Image")]
    static RawImage CreatRawImage()
    {
        Transform canvas = GetCanvas();
        RawImage raw_img = new GameObject("rawimage", typeof(RawImage)).GetComponent<RawImage>();
        raw_img.raycastTarget = false;
        raw_img.transform.SetParent(canvas);
        raw_img.gameObject.layer = canvas.gameObject.layer;
        Selection.activeTransform = raw_img.transform;
        return raw_img;
    }


    /// <summary>
    /// 创建输入框覆写
    /// </summary>
    /// <returns></returns>
    [MenuItem("GameObject/UI/InputField", false, 2037)]
    static void CreateInputField(MenuCommand menuCommand)
    {
        var go = TMPro_CreateObjectMenu.AddTextMeshProInputField(menuCommand);
        go.AddComponent<SensitiveCheck>();

    
    }


    [MenuItem("GameObject/UI/Input Field", true)]
    static bool AddInputField()
    {
        return false;
    }

    /// <summary>
    /// 创建EmptyRaycast
    /// </summary>
    [MenuItem("GameObject/UI/EmptyRaycast")]
    static EmptyRaycast EmptyRaycast()
    {
        Transform canvas = GetCanvas();
        EmptyRaycast empty = new GameObject("empty", typeof(EmptyRaycast)).GetComponent<EmptyRaycast>();
        empty.transform.SetParent(canvas);
        empty.gameObject.layer = canvas.gameObject.layer;
        Selection.activeTransform = empty.transform;
        return empty;
    }

    /// <summary>
    /// SgrImage
    /// </summary>
    [MenuItem("GameObject/UI/SgrImage")]
    static Image CreateSgrImage()
    {
        Transform canvas = GetCanvas();
        SgrUnity.SgrImage image = new GameObject("image", typeof(SgrUnity.SgrImage)).GetComponent<SgrUnity.SgrImage>();
        image.raycastTarget = false;
        image.transform.SetParent(canvas);
        image.gameObject.layer = canvas.gameObject.layer;
        Selection.activeTransform = image.transform;
        return image;
    }

    /// <summary>
    /// MultiLanguageText
    /// </summary>
    static Text CreateMultiLanguageText()
    {
        Transform canvas = GetCanvas();
        XSolution.XMultilanguage.MultiLanguageText text = new GameObject("text", typeof(XSolution.XMultilanguage.MultiLanguageText)).GetComponent<XSolution.XMultilanguage.MultiLanguageText>();
        text.raycastTarget = false;
        text.transform.SetParent(canvas);
        text.gameObject.layer = canvas.gameObject.layer;
        Selection.activeTransform = text.transform;
        return text;
    }

    [MenuItem("GameObject/UI/MultiLanguageTMPText")]
    static XSolution.XMultilanguage.MultiLanguageTMPText CreateMultiLanguageTMPText()
    {
        Transform canvas = GetCanvas();
        XSolution.XMultilanguage.MultiLanguageTMPText text = new GameObject("text", typeof(XSolution.XMultilanguage.MultiLanguageTMPText)).GetComponent<XSolution.XMultilanguage.MultiLanguageTMPText>();
        text.raycastTarget = false;
        text.transform.SetParent(canvas);
        text.gameObject.layer = canvas.gameObject.layer;
        text.font = AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>($"Assets/Content/UI/Mutilanguage/zh-Hans/TMP_Font/uifont.asset");
        Selection.activeTransform = text.transform;
        return text;
    }

    /// <summary>
    /// Panel Tempalte Create
    /// </summary>
    [MenuItem("GameObject/UI/UIPrefab")]
    static GameObject CreateUIPrefab()
    {
        string path = "Assets/Content/UI/Prefab/panel.prefab";
        GameObject obj = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        GameObject newone = GameObject.Instantiate<GameObject>(obj);
        return newone;
    }

    [MenuItem("GameObject/UI/ScrollView")]
    static GameObject CreateScrollView()
    {
        Transform canvas = GetCanvas();
        GameObject obj = new GameObject("Scroll View", typeof(ScrollRect));
        obj.transform.SetParent(canvas);
        obj.layer = canvas.gameObject.layer;
        Selection.activeTransform = obj.transform;

        SgrUnity.SgrImage image = obj.AddComponent<SgrUnity.SgrImage>();
        image.raycastTarget = false;

        SgrUnity.SgrImage viewport = new GameObject("Viewport", typeof(SgrUnity.SgrImage)).GetComponent<SgrUnity.SgrImage>();
        viewport.transform.SetParent(obj.transform);
        viewport.gameObject.layer = canvas.gameObject.layer;
        viewport.gameObject.AddComponent<RectMask2D>();

        GameObject content = new GameObject("Content");
        content.transform.SetParent(viewport.transform);
        content.gameObject.layer = canvas.gameObject.layer;

        return obj;
    }

}
