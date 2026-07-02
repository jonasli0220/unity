using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.UI;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using System;
using TMPro;
using TMPro.EditorUtilities;

public class UICreator
{
    private const string UIPrefabFolderMarker = "/Content/UI/Prefab/";
    private const float RightClickDragThreshold = 6f;
    private const string DefaultTMPFontPath =
        "Assets/Content/UI/Mutilanguage/zh-Hans/TMP_Font/uifont.asset";

    private static bool isRightClickPending;
    private static bool isRightClickDragging;
    private static Vector2 rightClickMouseDownPosition;

    public static bool UIWasAddedIntercept = true;

    [InitializeOnLoadMethod]
    public static void InitUICreator()
    {
        ObjectFactory.componentWasAdded -= OnComponentWasAdded;
        ObjectFactory.componentWasAdded += OnComponentWasAdded;

        SceneView.beforeSceneGui -= HandleUIQuickCreateContextMenu;
        SceneView.beforeSceneGui += HandleUIQuickCreateContextMenu;
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
        if (!IsUIPrefabStage(prefabStage))
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

        RectTransform parent = ResolveQuickCreateParent();
        if (parent == null)
        {
            return;
        }

        Vector2 mousePosition = currentEvent.mousePosition;
        Vector3 worldPosition = GetSceneViewWorldPosition(parent, mousePosition);
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
        if (!IsUIPrefabStage(prefabStage) ||
            parent == null ||
            !IsInsidePrefabStage(parent, prefabStage))
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
        EditorSceneManager.MarkSceneDirty(prefabStage.scene);
        Selection.activeGameObject = createdObject;
        SceneView.RepaintAll();
    }

    private static void ResetRightClickState()
    {
        isRightClickPending = false;
        isRightClickDragging = false;
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

    private static RectTransform ResolveQuickCreateParent()
    {
        PrefabStage prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
        Transform selectedTransform = Selection.activeTransform;
        RectTransform selectedRect = selectedTransform as RectTransform;
        if (selectedRect != null &&
            IsInsideCurrentEditingContext(selectedRect, prefabStage) &&
            (prefabStage != null
                ? IsUIPrefabStage(prefabStage)
                : selectedRect.GetComponentInParent<Canvas>() != null))
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

    private static Vector3 GetSceneViewWorldPosition(RectTransform parent, Vector2 mousePosition)
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

    [MenuItem("Assets/UI/AnimationCilp")]
    static AnimationClip CreateAnimationCilp()
    {
        AnimationClip clip = new AnimationClip();
        clip.wrapMode = WrapMode.Once;
        string path = AssetDatabase.GetAssetPath(Selection.activeObject) + "/" + "NewAnimation.anim";
        AssetDatabase.CreateAsset(clip, path);
        return clip;
    }
}
