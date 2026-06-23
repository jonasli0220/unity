#if false // Replaced by Assets/Content/UI/Editor/UICreator.cs.
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.UI;
using System;
using System.Reflection;
using TMPro;
using TMPro.EditorUtilities;

public class UICreator
{
    public static bool UIWasAddedIntercept = true;

    [InitializeOnLoadMethod]
    public static void InitUICreator()
    {
        ObjectFactory.componentWasAdded += OnComponentWasAdded;
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
#endif
