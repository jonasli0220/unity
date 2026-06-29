using System;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[InitializeOnLoad]
internal static class FigmaPasteSceneHandler
{
    private const string MenuRoot = "Tools/UI/Figma Bridge/Figma Paste/";
    private const string EnableMenuPath = MenuRoot + "Enable Scene Ctrl+V Paste";
    private const string InspectMenuPath = MenuRoot + "Inspect Clipboard";
    private const string EnabledEditorPrefKey = "Dragon.UI.FigmaPaste.ScenePaste.Enabled";
    private const string UIPrefabFolderMarker = "/Content/UI/Prefab/";
    private const string DefaultTMPFontPath =
        "Assets/Content/UI/Mutilanguage/zh-Hans/TMP_Font/uifont.asset";

    private static Vector2 lastSceneMousePosition;
    private static bool hasSceneMousePosition;
    private static double lastHandledPasteTime;

    static FigmaPasteSceneHandler()
    {
        if (!EditorPrefs.HasKey(EnabledEditorPrefKey))
        {
            EditorPrefs.SetBool(EnabledEditorPrefKey, true);
        }

        SceneView.beforeSceneGui -= OnBeforeSceneGui;
        SceneView.beforeSceneGui += OnBeforeSceneGui;
    }

    [MenuItem(EnableMenuPath)]
    private static void ToggleScenePaste()
    {
        bool enabled = !IsEnabled();
        EditorPrefs.SetBool(EnabledEditorPrefKey, enabled);
        Menu.SetChecked(EnableMenuPath, enabled);
        SceneView.RepaintAll();
    }

    [MenuItem(EnableMenuPath, true)]
    private static bool ValidateToggleScenePaste()
    {
        Menu.SetChecked(EnableMenuPath, IsEnabled());
        return true;
    }

    [MenuItem(InspectMenuPath)]
    private static void InspectClipboard()
    {
        string reportPath = FigmaPasteClipboard.WriteInspectionReport();
        EditorUtility.RevealInFinder(reportPath);
        EditorUtility.DisplayDialog(
            "Figma Paste",
            "Clipboard inspection written to:\n" + reportPath,
            "OK");
    }

    private static bool IsEnabled()
    {
        return EditorPrefs.GetBool(EnabledEditorPrefKey, true);
    }

    private static void OnBeforeSceneGui(SceneView sceneView)
    {
        Event currentEvent = Event.current;
        if (currentEvent == null)
        {
            return;
        }

        TrackSceneMouse(currentEvent);

        if (!IsEnabled() ||
            !IsPasteEvent(currentEvent) ||
            !IsStructuralEditingAllowedInCurrentContext())
        {
            return;
        }

        if (EditorGUIUtility.editingTextField || GUIUtility.hotControl != 0)
        {
            return;
        }

        if (EditorApplication.timeSinceStartup - lastHandledPasteTime < 0.2d)
        {
            currentEvent.Use();
            return;
        }

        FigmaPasteClipboardPayload payload;
        string error;
        if (!FigmaPasteClipboard.TryRead(out payload, out error))
        {
            ShowSceneNotification(sceneView, "Clipboard read failed: " + error);
            return;
        }

        if (payload == null || (!payload.HasImage && !payload.HasText))
        {
            return;
        }

        PrefabStage prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
        RectTransform parent;
        string parentMessage;
        if (!TryResolvePasteParent(prefabStage, out parent, out parentMessage))
        {
            ShowSceneNotification(sceneView, parentMessage);
            return;
        }

        bool pasted = false;
        if (payload.HasImage)
        {
            if (!IsUIPrefabStage(prefabStage))
            {
                ShowSceneNotification(
                    sceneView,
                    "Image paste needs an open UI Prefab Stage so the PNG can be saved to resource.");
                currentEvent.Use();
                lastHandledPasteTime = EditorApplication.timeSinceStartup;
                return;
            }

            pasted = PasteImage(sceneView, prefabStage, parent, payload);
        }
        else if (payload.HasText)
        {
            pasted = PasteText(sceneView, prefabStage, parent, payload.Text);
        }

        if (pasted)
        {
            currentEvent.Use();
            lastHandledPasteTime = EditorApplication.timeSinceStartup;
        }
    }

    private static void TrackSceneMouse(Event currentEvent)
    {
        if (currentEvent.type == EventType.MouseMove ||
            currentEvent.type == EventType.MouseDown ||
            currentEvent.type == EventType.MouseDrag ||
            currentEvent.type == EventType.MouseUp)
        {
            lastSceneMousePosition = currentEvent.mousePosition;
            hasSceneMousePosition = true;
        }
    }

    private static bool IsPasteEvent(Event currentEvent)
    {
        if (currentEvent.type == EventType.ExecuteCommand &&
            string.Equals(currentEvent.commandName, "Paste", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return currentEvent.type == EventType.KeyDown &&
               currentEvent.keyCode == KeyCode.V &&
               (currentEvent.control || currentEvent.command) &&
               !currentEvent.alt;
    }

    private static bool PasteImage(
        SceneView sceneView,
        PrefabStage prefabStage,
        RectTransform parent,
        FigmaPasteClipboardPayload payload)
    {
        Sprite sprite = ImportClipboardImageForPrefab(prefabStage, payload);
        if (sprite == null)
        {
            ShowSceneNotification(sceneView, "Clipboard image could not be imported as a Sprite.");
            return false;
        }

        const string undoName = "Paste Figma Clipboard Image";
        Vector3 worldPosition = GetPasteWorldPosition(sceneView, parent);

        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName(undoName);

        GameObject imageObject = ObjectFactory.CreateGameObject(
            sprite.name,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(SgrUnity.SgrImage));
        RectTransform rectTransform = imageObject.GetComponent<RectTransform>();
        Undo.SetTransformParent(rectTransform, parent, undoName);
        GameObjectUtility.EnsureUniqueNameForSibling(imageObject);

        InitializeRectTransform(rectTransform, worldPosition, new Vector2(100f, 100f));
        imageObject.layer = parent.gameObject.layer;

        SgrUnity.SgrImage image = imageObject.GetComponent<SgrUnity.SgrImage>();
        image.sprite = sprite;
        image.raycastTarget = false;
        image.SetNativeSize();

        FinalizeCreatedObject(parent, imageObject, undoName, undoGroup, prefabStage);
        ShowSceneNotification(sceneView, "Pasted image: " + sprite.name);
        return true;
    }

    private static bool PasteText(
        SceneView sceneView,
        PrefabStage prefabStage,
        RectTransform parent,
        string textValue)
    {
        const string undoName = "Paste Figma Clipboard Text";
        Vector3 worldPosition = GetPasteWorldPosition(sceneView, parent);

        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName(undoName);

        GameObject textObject = ObjectFactory.CreateGameObject(
            "text",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(XSolution.XMultilanguage.MultiLanguageTMPText));
        RectTransform rectTransform = textObject.GetComponent<RectTransform>();
        Undo.SetTransformParent(rectTransform, parent, undoName);
        GameObjectUtility.EnsureUniqueNameForSibling(textObject);

        InitializeRectTransform(rectTransform, worldPosition, EstimateTextSize(textValue));
        textObject.layer = parent.gameObject.layer;

        XSolution.XMultilanguage.MultiLanguageTMPText tmp =
            textObject.GetComponent<XSolution.XMultilanguage.MultiLanguageTMPText>();
        tmp.raycastTarget = false;
        tmp.text = textValue ?? string.Empty;
        tmp.fontSize = 32f;
        tmp.alignment = TextAlignmentOptions.Center;
        TMP_FontAsset defaultFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(DefaultTMPFontPath);
        if (defaultFont != null)
        {
            tmp.font = defaultFont;
        }

        FinalizeCreatedObject(parent, textObject, undoName, undoGroup, prefabStage);
        ShowSceneNotification(sceneView, "Pasted text");
        return true;
    }

    private static void InitializeRectTransform(
        RectTransform rectTransform,
        Vector3 worldPosition,
        Vector2 size)
    {
        rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
        rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        rectTransform.pivot = new Vector2(0.5f, 0.5f);
        rectTransform.localRotation = Quaternion.identity;
        rectTransform.localScale = Vector3.one;
        rectTransform.sizeDelta = size;
        rectTransform.position = worldPosition;
    }

    private static void FinalizeCreatedObject(
        RectTransform parent,
        GameObject createdObject,
        string undoName,
        int undoGroup,
        PrefabStage prefabStage)
    {
        Undo.RegisterFullObjectHierarchyUndo(parent.gameObject, undoName);
        Undo.CollapseUndoOperations(undoGroup);

        Scene editedScene = prefabStage != null ? prefabStage.scene : parent.gameObject.scene;
        if (editedScene.IsValid())
        {
            EditorSceneManager.MarkSceneDirty(editedScene);
        }

        Selection.activeGameObject = createdObject;
        SceneView.RepaintAll();
    }

    private static Vector2 EstimateTextSize(string textValue)
    {
        if (string.IsNullOrEmpty(textValue))
        {
            return new Vector2(240f, 60f);
        }

        string normalized = textValue.Replace("\r\n", "\n").Replace('\r', '\n');
        string[] lines = normalized.Split('\n');
        int longestLine = 0;
        for (int i = 0; i < lines.Length; i++)
        {
            longestLine = Mathf.Max(longestLine, lines[i].Length);
        }

        float width = Mathf.Clamp(longestLine * 18f + 48f, 240f, 960f);
        float height = Mathf.Clamp(lines.Length * 42f + 28f, 60f, 420f);
        return new Vector2(width, height);
    }

    private static Sprite ImportClipboardImageForPrefab(
        PrefabStage prefabStage,
        FigmaPasteClipboardPayload payload)
    {
        string targetFolder = GetCurrentPrefabResourceFolder(prefabStage);
        if (string.IsNullOrEmpty(targetFolder))
        {
            return null;
        }

        EnsureAssetFolderExists(targetFolder);
        if (!AssetDatabase.IsValidFolder(targetFolder))
        {
            Debug.LogError("Unable to create UI resource folder: " + targetFolder);
            return null;
        }

        string assetPath = AssetDatabase.GenerateUniqueAssetPath(
            targetFolder + "/figma_clipboard_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png");
        string absolutePath = AssetPathToAbsolutePath(assetPath);

        File.WriteAllBytes(absolutePath, payload.ImagePngBytes);
        AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
        ConfigureImportedSprite(assetPath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        return AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
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

    private static bool TryResolvePasteParent(
        PrefabStage prefabStage,
        out RectTransform parent,
        out string message)
    {
        parent = null;
        message = string.Empty;

        Transform selectedTransform = Selection.activeTransform;
        RectTransform selectedRect = selectedTransform as RectTransform;
        if (selectedRect != null && IsInsideCurrentEditingContext(selectedRect, prefabStage))
        {
            if (!UIDesignBoardLiveScene.IsActive ||
                UIDesignBoardLiveScene.IsEditableObject(selectedRect.gameObject))
            {
                parent = selectedRect;
                return true;
            }
        }

        if (IsUIPrefabStage(prefabStage))
        {
            Canvas[] prefabCanvases =
                prefabStage.prefabContentsRoot.GetComponentsInChildren<Canvas>(true);
            for (int i = 0; i < prefabCanvases.Length; i++)
            {
                RectTransform canvasRect = prefabCanvases[i].transform as RectTransform;
                if (canvasRect != null && IsInsidePrefabStage(canvasRect, prefabStage))
                {
                    parent = canvasRect;
                    return true;
                }
            }

            parent = prefabStage.prefabContentsRoot.transform as RectTransform;
            if (parent != null)
            {
                return true;
            }
        }

        if (UIDesignBoardLiveScene.IsActive)
        {
            message = "Select an editable RectTransform inside a Live Board artboard before pasting.";
            return false;
        }

        message = "Open a UI prefab and select a UI parent before pasting.";
        return false;
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

    private static bool IsInsideCurrentEditingContext(Transform transform, PrefabStage prefabStage)
    {
        if (transform == null || EditorUtility.IsPersistent(transform.gameObject))
        {
            return false;
        }

        if (prefabStage != null)
        {
            return IsInsidePrefabStage(transform, prefabStage);
        }

        return UIDesignBoardLiveScene.IsActive &&
               UIDesignBoardLiveScene.IsEditableObject(transform.gameObject);
    }

    private static bool IsInsidePrefabStage(Transform transform, PrefabStage prefabStage)
    {
        if (prefabStage == null || prefabStage.prefabContentsRoot == null)
        {
            return false;
        }

        Transform prefabRoot = prefabStage.prefabContentsRoot.transform;
        return transform != null &&
               transform.gameObject.scene == prefabStage.scene &&
               (transform == prefabRoot || transform.IsChildOf(prefabRoot));
    }

    private static bool IsUIPrefabStage(PrefabStage prefabStage)
    {
        if (prefabStage == null ||
            prefabStage.prefabContentsRoot == null ||
            string.IsNullOrEmpty(prefabStage.assetPath))
        {
            return false;
        }

        string normalizedPath = prefabStage.assetPath.Replace('\\', '/');
        if (normalizedPath.IndexOf(UIPrefabFolderMarker, StringComparison.OrdinalIgnoreCase) < 0 ||
            !normalizedPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return prefabStage.prefabContentsRoot.GetComponentInChildren<RectTransform>(true) != null;
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

    private static string AssetPathToAbsolutePath(string assetPath)
    {
        string projectRoot = Path.GetDirectoryName(Application.dataPath);
        return Path.GetFullPath(Path.Combine(projectRoot, assetPath));
    }

    private static Vector3 GetPasteWorldPosition(SceneView sceneView, RectTransform parent)
    {
        Vector2 mousePosition = hasSceneMousePosition
            ? lastSceneMousePosition
            : new Vector2(sceneView.position.width * 0.5f, sceneView.position.height * 0.5f);

        Ray ray = HandleUtility.GUIPointToWorldRay(mousePosition);
        Plane plane = new Plane(parent.forward, parent.position);
        float distance;
        if (plane.Raycast(ray, out distance))
        {
            return ray.GetPoint(distance);
        }

        return parent.position;
    }

    private static void ShowSceneNotification(SceneView sceneView, string message)
    {
        if (sceneView != null && !string.IsNullOrEmpty(message))
        {
            sceneView.ShowNotification(new GUIContent(message));
        }
    }
}
