using System;
using System.Collections.Generic;
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
    private const string TitleTMPFontPath =
        "Assets/Content/UI/Mutilanguage/zh-Hans/TMP_Font/uifont_title.asset";
    private const string SpecialTitleTMPFontPath =
        "Assets/Content/UI/Mutilanguage/zh-Hans/TMP_Font/uifont_title_special.asset";
    private const string NumberTMPFontPath =
        "Assets/Content/UI/TMP_Fonts/uifont_num.asset";

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

        if (payload == null)
        {
            return;
        }

        FigmaPasteStructuredPackage structuredPayload;
        bool hasStructuredPayload =
            FigmaPasteStructuredPayload.TryParse(payload.Text, out structuredPayload);
        FigmaPasteSvgRectangle svgRectangle;
        bool hasSvgRectangle = FigmaPasteSvgShape.TryParseRectangle(payload, out svgRectangle);
        bool hasPlainText = !hasStructuredPayload && HasPasteablePlainText(payload);

        if (!hasStructuredPayload && !payload.HasImage && !hasSvgRectangle && !hasPlainText)
        {
            if (payload.LooksLikeFigmaContent)
            {
                ShowSceneNotification(
                    sceneView,
                    "Unsupported Figma clipboard content. Use the Figma plugin Copy Selection for Unity Paste button.");
                ConsumePasteEvent(currentEvent);
            }

            return;
        }

        PrefabStage prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
        RectTransform parent;
        string parentMessage;
        if (!TryResolvePasteParent(prefabStage, out parent, out parentMessage))
        {
            ShowSceneNotification(sceneView, parentMessage);
            if (payload.LooksLikeFigmaContent)
            {
                ConsumePasteEvent(currentEvent);
            }

            return;
        }

        bool pasted = false;
        if (hasStructuredPayload)
        {
            if (FigmaPasteStructuredPayload.HasImageNode(structuredPayload) &&
                !IsUIPrefabStage(prefabStage))
            {
                ShowSceneNotification(
                    sceneView,
                    "Image paste needs an open UI Prefab Stage so the PNG can be saved to resource.");
                ConsumePasteEvent(currentEvent);
                return;
            }

            pasted = PasteStructuredPayload(sceneView, prefabStage, parent, structuredPayload);
            if (!pasted)
            {
                ConsumePasteEvent(currentEvent);
                return;
            }
        }
        else if (payload.HasImage)
        {
            if (!IsUIPrefabStage(prefabStage))
            {
                ShowSceneNotification(
                    sceneView,
                    "Image paste needs an open UI Prefab Stage so the PNG can be saved to resource.");
                ConsumePasteEvent(currentEvent);
                return;
            }

            pasted = PasteImage(sceneView, prefabStage, parent, payload);
        }
        else if (hasSvgRectangle)
        {
            pasted = PasteSvgRectangle(sceneView, prefabStage, parent, svgRectangle);
        }
        else if (hasPlainText)
        {
            pasted = PasteText(sceneView, prefabStage, parent, payload.Text);
        }

        if (pasted)
        {
            ConsumePasteEvent(currentEvent);
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

    private static void ConsumePasteEvent(Event currentEvent)
    {
        if (currentEvent != null)
        {
            currentEvent.Use();
        }

        lastHandledPasteTime = EditorApplication.timeSinceStartup;
    }

    private static bool HasPasteablePlainText(FigmaPasteClipboardPayload payload)
    {
        if (payload == null || !payload.HasText)
        {
            return false;
        }

        if (FigmaPasteStructuredPayload.LooksLikeStructuredPayload(payload.Text))
        {
            return false;
        }

        string trimmed = payload.Text.TrimStart();
        return !StartsWithIgnoreCase(trimmed, "<svg") &&
               !StartsWithIgnoreCase(trimmed, "<html") &&
               !StartsWithIgnoreCase(trimmed, "<!doctype");
    }

    private static bool StartsWithIgnoreCase(string value, string prefix)
    {
        return !string.IsNullOrEmpty(value) &&
               value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
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

    private static bool PasteStructuredPayload(
        SceneView sceneView,
        PrefabStage prefabStage,
        RectTransform parent,
        FigmaPasteStructuredPackage package)
    {
        if (package == null || package.nodes == null || package.nodes.Length == 0)
        {
            ShowSceneNotification(sceneView, "No supported Figma nodes in the copied selection.");
            return false;
        }

        const string undoName = "Paste Figma Structured Selection";
        Vector3 worldPosition = GetPasteWorldPosition(sceneView, parent);
        bool useContainer = package.nodes.Length > 1;

        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName(undoName);

        RectTransform pasteParent = parent;
        GameObject finalSelectionObject = null;
        Vector2 selectionSize = FigmaPasteStructuredPayload.GetSelectionSize(package);
        if (useContainer)
        {
            GameObject containerObject = ObjectFactory.CreateGameObject(
                "figma_selection",
                typeof(RectTransform));
            RectTransform container = containerObject.GetComponent<RectTransform>();
            Undo.SetTransformParent(container, parent, undoName);
            GameObjectUtility.EnsureUniqueNameForSibling(containerObject);
            InitializeRectTransform(container, worldPosition, selectionSize);
            containerObject.layer = parent.gameObject.layer;
            pasteParent = container;
            finalSelectionObject = containerObject;
        }

        int pastedCount = 0;
        List<string> warnings = new List<string>();
        for (int i = 0; i < package.nodes.Length; i++)
        {
            GameObject createdObject = CreateStructuredNode(
                prefabStage,
                pasteParent,
                package.nodes[i],
                useContainer ? pasteParent.position : worldPosition,
                undoName,
                ref pastedCount,
                warnings);
            if (createdObject == null)
            {
                continue;
            }

            RectTransform rectTransform = createdObject.transform as RectTransform;
            if (useContainer && rectTransform != null)
            {
                rectTransform.anchoredPosition =
                    CalculateStructuredChildPosition(package.nodes[i], selectionSize);
            }

            finalSelectionObject = useContainer ? finalSelectionObject : createdObject;
        }

        if (pastedCount == 0)
        {
            if (useContainer && finalSelectionObject != null)
            {
                UnityEngine.Object.DestroyImmediate(finalSelectionObject);
            }

            Undo.CollapseUndoOperations(undoGroup);
            for (int i = 0; i < warnings.Count; i++)
            {
                Debug.LogWarning("[Figma Paste] " + warnings[i]);
            }
            ShowSceneNotification(
                sceneView,
                warnings.Count > 0
                    ? "Unity Prefab source was not found. See Console for the asset path."
                    : "Figma selection could not be pasted.");
            return false;
        }

        FinalizeCreatedObject(parent, finalSelectionObject, undoName, undoGroup, prefabStage);
        string warningSuffix = warnings.Count > 0
            ? "; skipped " + warnings.Count + " missing Unity reference(s)"
            : string.Empty;
        ShowSceneNotification(
            sceneView,
            (pastedCount == 1 ? "Pasted Figma selection" : "Pasted Figma selection: " + pastedCount + " nodes") +
            warningSuffix);
        for (int i = 0; i < warnings.Count; i++)
        {
            Debug.LogWarning("[Figma Paste] " + warnings[i]);
        }
        return true;
    }

    private static GameObject CreateStructuredNode(
        PrefabStage prefabStage,
        RectTransform parent,
        FigmaPasteStructuredNode node,
        Vector3 worldPosition,
        string undoName,
        ref int pastedCount,
        List<string> warnings)
    {
        if (node == null || !FigmaPasteStructuredPayload.IsSupportedNode(node))
        {
            return null;
        }

        GameObject createdObject;
        if (FigmaPasteStructuredPayload.IsReferenceNode(node))
        {
            createdObject = CreateStructuredReference(
                prefabStage,
                parent,
                node,
                worldPosition,
                undoName,
                warnings);
        }
        else if (FigmaPasteStructuredPayload.IsImageNode(node))
        {
            createdObject = CreateStructuredImage(prefabStage, parent, node, worldPosition, undoName);
        }
        else if (FigmaPasteStructuredPayload.IsRectangleNode(node))
        {
            createdObject = CreateStructuredRectangle(parent, node, worldPosition, undoName);
        }
        else if (FigmaPasteStructuredPayload.IsTextNode(node))
        {
            createdObject = CreateStructuredText(parent, node, worldPosition, undoName);
        }
        else
        {
            createdObject = CreateStructuredGroup(parent, node, worldPosition, undoName);
        }

        if (createdObject == null)
        {
            return null;
        }

        pastedCount++;
        RectTransform createdRect = createdObject.transform as RectTransform;
        ApplyStructuredTransform(createdRect, node);

        if (!FigmaPasteStructuredPayload.IsReferenceNode(node) &&
            createdRect != null &&
            node.children != null)
        {
            Vector2 parentSize = FigmaPasteStructuredPayload.GetNodeSize(node);
            for (int i = 0; i < node.children.Length; i++)
            {
                FigmaPasteStructuredNode childNode = node.children[i];
                GameObject childObject = CreateStructuredNode(
                    prefabStage,
                    createdRect,
                    childNode,
                    createdRect.position,
                    undoName,
                    ref pastedCount,
                    warnings);
                RectTransform childRect = childObject != null
                    ? childObject.transform as RectTransform
                    : null;
                if (childRect != null)
                {
                    childRect.anchoredPosition = CalculateStructuredChildPosition(childNode, parentSize);
                }
            }
        }

        return createdObject;
    }

    private static GameObject CreateStructuredGroup(
        RectTransform parent,
        FigmaPasteStructuredNode node,
        Vector3 worldPosition,
        string undoName)
    {
        GameObject groupObject = ObjectFactory.CreateGameObject(
            MakeObjectName(node.name, "group"),
            typeof(RectTransform));
        RectTransform rectTransform = groupObject.GetComponent<RectTransform>();
        Undo.SetTransformParent(rectTransform, parent, undoName);
        GameObjectUtility.EnsureUniqueNameForSibling(groupObject);
        InitializeRectTransform(
            rectTransform,
            worldPosition,
            FigmaPasteStructuredPayload.GetNodeSize(node));
        groupObject.layer = parent.gameObject.layer;
        return groupObject;
    }

    private static GameObject CreateStructuredReference(
        PrefabStage prefabStage,
        RectTransform parent,
        FigmaPasteStructuredNode node,
        Vector3 worldPosition,
        string undoName,
        List<string> warnings)
    {
        string prefabPath = ResolveStructuredPrefabPath(node.source);
        GameObject prefabAsset = string.IsNullOrEmpty(prefabPath)
            ? null
            : AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefabAsset == null)
        {
            warnings.Add(
                MakeObjectName(node.name, "Unity reference") +
                " source prefab was not found: " +
                (node.source.prefabPath ?? string.Empty) +
                " (" + (node.source.prefabGuid ?? string.Empty) + ")");
            return null;
        }

        GameObject instance = prefabStage != null
            ? PrefabUtility.InstantiatePrefab(prefabAsset, prefabStage.scene) as GameObject
            : PrefabUtility.InstantiatePrefab(prefabAsset) as GameObject;
        if (instance == null)
        {
            warnings.Add("Could not instantiate Unity prefab reference: " + prefabPath);
            return null;
        }

        Undo.RegisterCreatedObjectUndo(instance, undoName);
        Undo.SetTransformParent(instance.transform, parent, undoName);
        instance.name = MakeObjectName(node.name, prefabAsset.name);
        GameObjectUtility.EnsureUniqueNameForSibling(instance);

        RectTransform rectTransform = instance.transform as RectTransform;
        if (rectTransform == null)
        {
            Undo.DestroyObjectImmediate(instance);
            warnings.Add("Unity prefab reference has no RectTransform: " + prefabPath);
            return null;
        }

        Vector3 sourceScale = rectTransform.localScale;
        InitializeRectTransform(
            rectTransform,
            worldPosition,
            FigmaPasteStructuredPayload.GetNodeSize(node));
        rectTransform.localScale = new Vector3(
            Mathf.Max(0.0001f, Mathf.Abs(sourceScale.x)),
            Mathf.Max(0.0001f, Mathf.Abs(sourceScale.y)),
            Mathf.Max(0.0001f, Mathf.Abs(sourceScale.z)));
        return instance;
    }

    private static string ResolveStructuredPrefabPath(FigmaPasteStructuredSource source)
    {
        if (source == null)
        {
            return string.Empty;
        }

        string prefabPath = (source.prefabPath ?? string.Empty).Replace('\\', '/');
        if (!string.IsNullOrEmpty(prefabPath) &&
            AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) != null)
        {
            return prefabPath;
        }

        if (!string.IsNullOrEmpty(source.prefabGuid))
        {
            prefabPath = AssetDatabase.GUIDToAssetPath(source.prefabGuid).Replace('\\', '/');
        }

        return prefabPath;
    }

    private static GameObject CreateStructuredImage(
        PrefabStage prefabStage,
        RectTransform parent,
        FigmaPasteStructuredNode node,
        Vector3 worldPosition,
        string undoName)
    {
        byte[] pngBytes;
        int imageWidth;
        int imageHeight;
        if (!FigmaPasteStructuredPayload.TryGetImagePng(
            node,
            out pngBytes,
            out imageWidth,
            out imageHeight))
        {
            return null;
        }

        Sprite sprite = ImportPngForPrefab(prefabStage, pngBytes, node.name);
        if (sprite == null)
        {
            return null;
        }

        GameObject imageObject = ObjectFactory.CreateGameObject(
            MakeObjectName(node.name, "image"),
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(SgrUnity.SgrImage));
        RectTransform rectTransform = imageObject.GetComponent<RectTransform>();
        Undo.SetTransformParent(rectTransform, parent, undoName);
        GameObjectUtility.EnsureUniqueNameForSibling(imageObject);

        Vector2 size = FigmaPasteStructuredPayload.GetNodeSize(node);
        if (size.x <= 1f && imageWidth > 0)
        {
            size.x = imageWidth;
        }

        if (size.y <= 1f && imageHeight > 0)
        {
            size.y = imageHeight;
        }

        InitializeRectTransform(rectTransform, worldPosition, size);
        imageObject.layer = parent.gameObject.layer;

        SgrUnity.SgrImage image = imageObject.GetComponent<SgrUnity.SgrImage>();
        image.sprite = sprite;
        image.raycastTarget = false;
        return imageObject;
    }

    private static GameObject CreateStructuredRectangle(
        RectTransform parent,
        FigmaPasteStructuredNode node,
        Vector3 worldPosition,
        string undoName)
    {
        GameObject imageObject = ObjectFactory.CreateGameObject(
            MakeObjectName(node.name, "rectangle"),
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(SgrUnity.SgrImage));
        RectTransform rectTransform = imageObject.GetComponent<RectTransform>();
        Undo.SetTransformParent(rectTransform, parent, undoName);
        GameObjectUtility.EnsureUniqueNameForSibling(imageObject);

        InitializeRectTransform(
            rectTransform,
            worldPosition,
            FigmaPasteStructuredPayload.GetNodeSize(node));
        imageObject.layer = parent.gameObject.layer;

        SgrUnity.SgrImage image = imageObject.GetComponent<SgrUnity.SgrImage>();
        image.raycastTarget = false;
        image.color = ToUnityColor(node.fill);
        return imageObject;
    }

    private static GameObject CreateStructuredText(
        RectTransform parent,
        FigmaPasteStructuredNode node,
        Vector3 worldPosition,
        string undoName)
    {
        string textValue = node.characters ?? string.Empty;
        GameObject textObject = ObjectFactory.CreateGameObject(
            MakeObjectName(node.name, "text"),
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(XSolution.XMultilanguage.MultiLanguageTMPText));
        RectTransform rectTransform = textObject.GetComponent<RectTransform>();
        Undo.SetTransformParent(rectTransform, parent, undoName);
        GameObjectUtility.EnsureUniqueNameForSibling(textObject);

        Vector2 size = FigmaPasteStructuredPayload.GetNodeSize(node);
        if (size.x <= 1f || size.y <= 1f)
        {
            size = EstimateTextSize(textValue);
        }

        InitializeRectTransform(rectTransform, worldPosition, size);
        textObject.layer = parent.gameObject.layer;

        XSolution.XMultilanguage.MultiLanguageTMPText tmp =
            textObject.GetComponent<XSolution.XMultilanguage.MultiLanguageTMPText>();
        tmp.raycastTarget = false;
        tmp.text = textValue;
        tmp.fontSize = node.fontSize > 0f ? node.fontSize : 32f;
        tmp.alignment = ResolveStructuredTextAlignment(node);
        tmp.color = ToUnityColor(node.fill);
        TMP_FontAsset mappedFont = ResolveStructuredFont(node);
        if (mappedFont != null)
        {
            tmp.font = mappedFont;
        }

        return textObject;
    }

    private static Vector2 CalculateStructuredChildPosition(
        FigmaPasteStructuredNode node,
        Vector2 selectionSize)
    {
        Vector2 size = FigmaPasteStructuredPayload.GetNodeSize(node);
        float centerX = node.x + size.x * 0.5f;
        float centerY = node.y + size.y * 0.5f;
        return new Vector2(
            centerX - selectionSize.x * 0.5f,
            selectionSize.y * 0.5f - centerY);
    }

    private static void ApplyStructuredTransform(
        RectTransform rectTransform,
        FigmaPasteStructuredNode node)
    {
        if (rectTransform == null || node == null)
        {
            return;
        }

        rectTransform.localRotation = Quaternion.Euler(0f, 0f, -node.rotation);
        Vector3 scale = rectTransform.localScale;
        float scaleX = node.scaleX < 0f ? -1f : 1f;
        float scaleY = node.scaleY < 0f ? -1f : 1f;
        rectTransform.localScale = new Vector3(
            Mathf.Max(0.0001f, Mathf.Abs(scale.x)) * scaleX,
            Mathf.Max(0.0001f, Mathf.Abs(scale.y)) * scaleY,
            Mathf.Max(0.0001f, Mathf.Abs(scale.z)));
    }

    private static TMP_FontAsset ResolveStructuredFont(FigmaPasteStructuredNode node)
    {
        if (node == null)
        {
            return AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(DefaultTMPFontPath);
        }

        TMP_FontAsset font = LoadTmpFont(node.fontPath);
        if (font == null && !string.IsNullOrEmpty(node.fontGuid))
        {
            font = LoadTmpFont(AssetDatabase.GUIDToAssetPath(node.fontGuid));
        }

        if (font != null)
        {
            return font;
        }

        string unityFontName = (node.unityFontName ?? string.Empty).Trim().ToLowerInvariant();
        if (!string.IsNullOrEmpty(unityFontName))
        {
            return LoadTmpFont(GetUnityFontPath(unityFontName)) ??
                   AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(DefaultTMPFontPath);
        }

        string figmaFamily = (node.fontFamily ?? string.Empty).Trim().ToLowerInvariant();
        if (figmaFamily == "uifont_title")
        {
            return LoadTmpFont(NumberTMPFontPath);
        }

        if (figmaFamily == "uifont_title_zh-hans")
        {
            return LoadTmpFont(TitleTMPFontPath);
        }

        if (figmaFamily == "uifont_title_special" || figmaFamily == "uifont_title+special")
        {
            return LoadTmpFont(SpecialTitleTMPFontPath);
        }

        return LoadTmpFont(DefaultTMPFontPath);
    }

    private static string GetUnityFontPath(string fontName)
    {
        switch (fontName)
        {
            case "uifont_num":
                return NumberTMPFontPath;
            case "uifont_title":
                return TitleTMPFontPath;
            case "uifont_title_special":
            case "uifont_title+special":
                return SpecialTitleTMPFontPath;
            default:
                return DefaultTMPFontPath;
        }
    }

    private static TMP_FontAsset LoadTmpFont(string assetPath)
    {
        return string.IsNullOrEmpty(assetPath)
            ? null
            : AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(assetPath.Replace('\\', '/'));
    }

    private static TextAlignmentOptions ResolveStructuredTextAlignment(
        FigmaPasteStructuredNode node)
    {
        string horizontal = (node.textAlignHorizontal ?? string.Empty).ToUpperInvariant();
        string vertical = (node.textAlignVertical ?? string.Empty).ToUpperInvariant();
        if (vertical == "TOP")
        {
            return horizontal == "LEFT" ? TextAlignmentOptions.TopLeft :
                   horizontal == "RIGHT" ? TextAlignmentOptions.TopRight :
                   TextAlignmentOptions.Top;
        }

        if (vertical == "BOTTOM")
        {
            return horizontal == "LEFT" ? TextAlignmentOptions.BottomLeft :
                   horizontal == "RIGHT" ? TextAlignmentOptions.BottomRight :
                   TextAlignmentOptions.Bottom;
        }

        return horizontal == "LEFT" ? TextAlignmentOptions.Left :
               horizontal == "RIGHT" ? TextAlignmentOptions.Right :
               TextAlignmentOptions.Center;
    }

    private static Color ToUnityColor(FigmaPasteStructuredColor color)
    {
        if (color == null)
        {
            return Color.white;
        }

        return new Color(
            Mathf.Clamp01(color.r),
            Mathf.Clamp01(color.g),
            Mathf.Clamp01(color.b),
            Mathf.Clamp01(color.a));
    }

    private static string MakeObjectName(string value, string fallback)
    {
        string name = string.IsNullOrEmpty(value) ? fallback : value.Trim();
        if (string.IsNullOrEmpty(name))
        {
            return fallback;
        }

        return name.Length > 80 ? name.Substring(0, 80) : name;
    }

    private static bool PasteSvgRectangle(
        SceneView sceneView,
        PrefabStage prefabStage,
        RectTransform parent,
        FigmaPasteSvgRectangle rectangle)
    {
        if (rectangle == null || rectangle.Size.x <= 0f || rectangle.Size.y <= 0f)
        {
            ShowSceneNotification(sceneView, "Clipboard SVG rectangle could not be parsed.");
            return false;
        }

        const string undoName = "Paste Figma SVG Rectangle";
        Vector3 worldPosition = GetPasteWorldPosition(sceneView, parent);

        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName(undoName);

        GameObject imageObject = ObjectFactory.CreateGameObject(
            "rectangle",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(SgrUnity.SgrImage));
        RectTransform rectTransform = imageObject.GetComponent<RectTransform>();
        Undo.SetTransformParent(rectTransform, parent, undoName);
        GameObjectUtility.EnsureUniqueNameForSibling(imageObject);

        InitializeRectTransform(rectTransform, worldPosition, rectangle.Size);
        imageObject.layer = parent.gameObject.layer;

        SgrUnity.SgrImage image = imageObject.GetComponent<SgrUnity.SgrImage>();
        image.raycastTarget = false;
        image.color = rectangle.FillColor;

        FinalizeCreatedObject(parent, imageObject, undoName, undoGroup, prefabStage);
        ShowSceneNotification(sceneView, "Pasted rectangle");
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
        return ImportPngForPrefab(
            prefabStage,
            payload != null ? payload.ImagePngBytes : null,
            "figma_clipboard");
    }

    private static Sprite ImportPngForPrefab(
        PrefabStage prefabStage,
        byte[] pngBytes,
        string sourceName)
    {
        string targetFolder = GetCurrentPrefabResourceFolder(prefabStage);
        if (string.IsNullOrEmpty(targetFolder) || pngBytes == null || pngBytes.Length == 0)
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
            targetFolder + "/" + MakeImageAssetBaseName(sourceName) + "_" +
            DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png");
        string absolutePath = AssetPathToAbsolutePath(assetPath);

        File.WriteAllBytes(absolutePath, pngBytes);
        AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
        ConfigureImportedSprite(assetPath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        return AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
    }

    private static string MakeImageAssetBaseName(string sourceName)
    {
        string name = string.IsNullOrEmpty(sourceName) ? "figma_clipboard" : sourceName.Trim();
        if (string.IsNullOrEmpty(name))
        {
            name = "figma_clipboard";
        }

        char[] invalidCharacters = Path.GetInvalidFileNameChars();
        char[] characters = name.ToCharArray();
        for (int i = 0; i < characters.Length; i++)
        {
            if (Array.IndexOf(invalidCharacters, characters[i]) >= 0)
            {
                characters[i] = '_';
            }
        }

        name = new string(characters).Trim();
        if (name.Length > 48)
        {
            name = name.Substring(0, 48);
        }

        if (!name.StartsWith("figma_", StringComparison.OrdinalIgnoreCase))
        {
            name = "figma_" + name;
        }

        return name;
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
            parent = selectedRect;
            return true;
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

        return false;
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
