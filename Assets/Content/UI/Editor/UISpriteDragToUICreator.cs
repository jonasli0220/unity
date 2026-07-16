using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

internal static class UISpriteDragToUICreator
{
    private const string UIPrefabFolderMarker = "/Content/UI/Prefab/";
    private const float MultiSpriteSpacing = 12f;
    private const float DragPreviewAlpha = 0.72f;
    private const string SpriteDragMenuPath = "UITools/拖入图片自动创建UI";
    private const string SpriteDragEnabledEditorPrefKey =
        "SgrProject.UI.SpriteDragToUI.Enabled";

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

    [InitializeOnLoadMethod]
    private static void InitSpriteDragToUICreator()
    {
        if (!EditorPrefs.HasKey(SpriteDragEnabledEditorPrefKey))
        {
            EditorPrefs.SetBool(SpriteDragEnabledEditorPrefKey, true);
        }

        SceneView.duringSceneGui -= DrawSpriteDragPreviewOverlay;
        SceneView.duringSceneGui += DrawSpriteDragPreviewOverlay;

        SceneView.duringSceneGui -= HandleSpriteDragIntoUI;
        SceneView.beforeSceneGui -= HandleSpriteDragIntoUI;
        SceneView.beforeSceneGui += HandleSpriteDragIntoUI;

        EditorApplication.hierarchyWindowItemOnGUI -= HandleSpriteDragIntoHierarchy;
        EditorApplication.hierarchyWindowItemOnGUI += HandleSpriteDragIntoHierarchy;
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

        int insertSiblingIndex;
        RectTransform parent = ResolveSpriteDropParent(out insertSiblingIndex);
        if (parent == null)
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

        CreateDraggedSgrImages(parent, insertSiblingIndex, sprites, currentEvent.mousePosition);
        currentEvent.Use();
    }

    private static void HandleSpriteDragIntoHierarchy(int instanceId, Rect selectionRect)
    {
        if (!IsSpriteDragToUIEnabled() ||
            !IsStructuralEditingAllowedInCurrentContext())
        {
            return;
        }

        Event currentEvent = Event.current;
        if (currentEvent == null ||
            (currentEvent.type != EventType.DragUpdated &&
                currentEvent.type != EventType.DragPerform))
        {
            return;
        }

        Rect rowRect = selectionRect;
        rowRect.x = 0f;
        rowRect.width = EditorGUIUtility.currentViewWidth;
        if (!rowRect.Contains(currentEvent.mousePosition))
        {
            return;
        }

        List<Sprite> sprites = new List<Sprite>();
        List<string> externalImagePaths = new List<string>();
        bool isExternalImageDrag = TryGetExternalImagePaths(externalImagePaths);
        if (!isExternalImageDrag && !TryGetDraggedSprites(sprites))
        {
            return;
        }

        if (isExternalImageDrag && !IsUIPrefabStage(PrefabStageUtility.GetCurrentPrefabStage()))
        {
            return;
        }

        GameObject targetObject = EditorUtility.InstanceIDToObject(instanceId) as GameObject;
        int insertSiblingIndex;
        RectTransform parent = ResolveHierarchyDropParent(targetObject, out insertSiblingIndex);
        if (parent == null)
        {
            return;
        }

        DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
        if (currentEvent.type == EventType.DragPerform)
        {
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

            CreateDraggedSgrImages(parent, insertSiblingIndex, sprites, parent.position);
            EditorApplication.RepaintHierarchyWindow();
        }

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

        GameObject prefabRoot = prefabStage.prefabContentsRoot;
        if (prefabRoot.GetComponentInChildren<Canvas>(true) != null)
        {
            return true;
        }

        return prefabRoot.GetComponentInChildren<RectTransform>(true) != null;
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
        UnityEngine.Object[] draggedObjects = DragAndDrop.objectReferences;
        if (draggedObjects == null || draggedObjects.Length == 0)
        {
            return false;
        }

        if (!HasDraggedProjectAssetPath())
        {
            return false;
        }

        HashSet<int> spriteIds = new HashSet<int>();
        for (int i = 0; i < draggedObjects.Length; i++)
        {
            UnityEngine.Object draggedObject = draggedObjects[i];
            if (draggedObject == null || !AssetDatabase.Contains(draggedObject))
            {
                return false;
            }

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

    private static bool HasDraggedProjectAssetPath()
    {
        string[] draggedPaths = DragAndDrop.paths;
        if (draggedPaths == null || draggedPaths.Length == 0)
        {
            return false;
        }

        for (int i = 0; i < draggedPaths.Length; i++)
        {
            string draggedPath = draggedPaths[i];
            if (string.IsNullOrEmpty(draggedPath) ||
                !draggedPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (AssetDatabase.LoadMainAssetAtPath(draggedPath) != null)
            {
                return true;
            }
        }

        return false;
    }

    private static Sprite LoadSingleSpriteFromTexture(UnityEngine.Object texture)
    {
        string assetPath = AssetDatabase.GetAssetPath(texture);
        if (string.IsNullOrEmpty(assetPath))
        {
            return null;
        }

        Sprite mainSprite = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
        if (mainSprite != null)
        {
            return mainSprite;
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

    private static RectTransform ResolveSpriteDropParent(out int insertSiblingIndex)
    {
        insertSiblingIndex = -1;
        PrefabStage prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
        Transform selectedTransform = Selection.activeTransform;
        RectTransform selectedRect = selectedTransform as RectTransform;
        if (selectedRect != null
            && IsInsideCurrentEditingContext(selectedRect, prefabStage)
            && (prefabStage != null
                ? IsUIPrefabStage(prefabStage)
                : selectedRect.GetComponentInParent<Canvas>() != null))
        {
            RectTransform selectedParentRect = selectedRect.parent as RectTransform;
            if (selectedParentRect != null
                && selectedRect.GetComponent<Canvas>() == null
                && IsInsideCurrentEditingContext(selectedParentRect, prefabStage))
            {
                insertSiblingIndex = selectedRect.GetSiblingIndex() + 1;
                return selectedParentRect;
            }

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

            RectTransform prefabRootRect =
                prefabStage.prefabContentsRoot.transform as RectTransform;
            if (prefabRootRect != null && IsInsidePrefabStage(prefabRootRect, prefabStage))
            {
                return prefabRootRect;
            }

            RectTransform[] prefabRects =
                prefabStage.prefabContentsRoot.GetComponentsInChildren<RectTransform>(true);
            for (int i = 0; i < prefabRects.Length; i++)
            {
                if (prefabRects[i] != null && IsInsidePrefabStage(prefabRects[i], prefabStage))
                {
                    return prefabRects[i];
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

    private static RectTransform ResolveHierarchyDropParent(
        GameObject targetObject,
        out int insertSiblingIndex)
    {
        insertSiblingIndex = -1;
        if (targetObject == null)
        {
            return null;
        }

        PrefabStage prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
        RectTransform targetRect = targetObject.transform as RectTransform;
        if (targetRect == null
            || !IsInsideCurrentEditingContext(targetRect, prefabStage)
            || (prefabStage != null
                ? !IsUIPrefabStage(prefabStage)
                : targetRect.GetComponentInParent<Canvas>() == null))
        {
            return null;
        }

        if (targetRect.GetComponent<Canvas>() != null)
        {
            return targetRect;
        }

        RectTransform parentRect = targetRect.parent as RectTransform;
        if (parentRect == null || !IsInsideCurrentEditingContext(parentRect, prefabStage))
        {
            return targetRect;
        }

        insertSiblingIndex = targetRect.GetSiblingIndex() + 1;
        return parentRect;
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
        int insertSiblingIndex,
        List<Sprite> sprites,
        Vector2 mousePosition)
    {
        CreateDraggedSgrImages(
            parent,
            insertSiblingIndex,
            sprites,
            GetDropWorldPosition(parent, mousePosition));
    }

    private static void CreateDraggedSgrImages(
        RectTransform parent,
        int insertSiblingIndex,
        List<Sprite> sprites,
        Vector3 dropWorldPosition)
    {
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
            if (insertSiblingIndex >= 0)
            {
                rectTransform.SetSiblingIndex(insertSiblingIndex + i);
            }

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
}
