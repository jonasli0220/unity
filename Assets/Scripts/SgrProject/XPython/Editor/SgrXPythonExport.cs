using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using System.Reflection;
using PulsarRenderer;
using UnityEngine.Rendering;
using System.Collections.Generic;
using System;
using XPython;
using Mono.Cecil;
using XSolution.AssetBundles;

[InitializeOnLoad]
public class Startup
{
    static Startup()
    {
        Debug.Log("Dragon XPython Export Start Up!");
        DragonExport.ExportPRP();
        DragonExport.ExportURP();
#if USE_PERFORMANCE_PIPELINE
        DragonExport.ExportPPP();
#endif
        XPythonExport.exportRule.exportPath =
            Application.dataPath + "/Scripts/SgrProject/XPython/Runtime/UnityExports";
        XPythonExport.exportRule.exportTempPath =
            Application.dataPath + "/Scripts/SgrProject/XPython/Runtime/UnityExports/.Temp";

        XPythonExport.exportRule.AddExportType(DragonExport.UnityExportClass);
        XPythonExport.exportRule.AddExportType(DragonExport.UnityExportStaticClass);

        XPythonExport.exportRule.AddExportType(DragonExport.DragonExportClass);
        XPythonExport.exportRule.AddExportType(DragonExport.DragonExportStaticClass);

        XPythonExport.exportRule.AddExportMemberMacro(DragonExport.SgrExportMemberMacroDict);

        XPythonExport.exportRule.AddExportFilter(new DragonExportMemberFilter());

        //设置自定义导出Moulde名的Delegate
        XPythonExport.exportRule.SetModuleNameDelegate(DragonExport.GetModuleNameForType);

#if ENABLE_PYTHON_INJECTION
        XPythonInjection.injectionRule.AddInjectFilter(new DragonInjectionMemberFilter());
#endif
    }
}

public static class DragonExport
{
    // Unity原始类型暂时还是配表，后面统一全部导出
    public static HashSet<Type> UnityExportClass = new HashSet<Type>
    {
        typeof(UnityEngine.RectOffset),
        typeof(UnityEngine.Keyframe),
        typeof(Vector2Int),
        typeof(RectMask2D),
        typeof(Cursor),
        typeof(Resolution),
        typeof(UnityEngine.Object),
        typeof(Component),
        typeof(TextAsset),
        typeof(Transform),
        typeof(RectTransform),
        typeof(Material),
        typeof(Light),
        typeof(Rigidbody),
        typeof(Camera),
        typeof(AudioSource),
        typeof(MonoBehaviour),
        typeof(GameObject),
        typeof(Collider),
        typeof(Collision),
        typeof(Joint),
        typeof(EdgeCollider2D),
        typeof(PolygonCollider2D),
        typeof(CircleCollider2D),
        typeof(Collider2D),
        typeof(Collision2D),
        typeof(Joint2D),
        typeof(ControllerColliderHit),
        typeof(Texture),
        typeof(Texture2D),
        typeof(Shader),
        typeof(Renderer),
        typeof(Bounds),
        typeof(SystemInfo),
        typeof(MaterialPropertyBlock),
        typeof(Graphics),
        typeof(UnityEngine.WWWForm),
        typeof(UnityEngine.Networking.UnityWebRequest),
        typeof(UnityEngine.Networking.UnityWebRequest.Result),
        typeof(UnityEngine.Networking.UnityWebRequestAsyncOperation),
        typeof(UnityEngine.Networking.DownloadHandler),
        typeof(UnityEngine.Networking.CertificateHandler),
        typeof(UnityEngine.Networking.DownloadHandlerFile),
        typeof(UnityEngine.Networking.DownloadHandlerBuffer),
        typeof(UnityEngine.Networking.UploadHandler),
        typeof(UnityEngine.Networking.UploadHandlerRaw),
        typeof(UnityEngine.Networking.UploadHandlerFile),
        typeof(AudioClip),
        typeof(AssetBundle),
        typeof(AssetBundleRequest),
        typeof(AssetBundleCreateRequest),
        typeof(ParticleSystem),
        typeof(ParticleSystem.Particle),
        typeof(SkinnedMeshRenderer),
        typeof(MeshRenderer),
        typeof(TrailRenderer),
        typeof(VolumeManager),
        typeof(BoxCollider),
        typeof(MeshCollider),
        typeof(SphereCollider),
        typeof(CharacterController),
        typeof(Terrain),
        typeof(CapsuleCollider),
        typeof(ParticleSystemRenderer),
        typeof(MeshFilter),
        typeof(Mesh),
        typeof(Animator),
        typeof(AnimatorStateInfo),
        typeof(AnimatorOverrideController),
        typeof(RuntimeAnimatorController),
        typeof(Animation),
        typeof(AnimationClip),
        typeof(AvatarMask),
        typeof(AnimationState),
        typeof(RenderTexture),
        typeof(Texture2DArray),
        typeof(Event),
        typeof(EventType),
        typeof(Ray),
        typeof(RaycastHit),
        typeof(LineRenderer),
        typeof(CustomComponent),
        typeof(PyComponent),
        typeof(UnityEngine.SceneManagement.Scene),
        typeof(UnityEngine.Matrix4x4),
        typeof(Vector4),
        typeof(Vector3),
        typeof(Vector2),
        typeof(UnityEngine.Events.UnityEventBase),
        typeof(UnityEngine.UI.InputField.SubmitEvent),
        //typeof(UnityEngine.UI.InputField.EndEditEvent),
        typeof(UnityEngine.Color32),
        typeof(UnityEngine.Color),
        typeof(UnityEngine.UI.ColorBlock),
        typeof(UnityEngine.LayerMask),
        typeof(UnityEngine.Quaternion),
        typeof(UnityEngine.CameraClearFlags),
        typeof(UnityEngine.AsyncOperation),
        typeof(UnityEngine.LightType),
        typeof(UnityEngine.PlayerPrefs),
        typeof(UnityEngine.ShaderVariantCollection),
        typeof(Text),
        typeof(InputField),
        typeof(InputField.OnChangeEvent),
        typeof(ContentSizeFitter),
        typeof(CanvasGroup),
        typeof(Canvas),
        typeof(ScrollRect),
        typeof(ScrollRect.ScrollRectEvent),
        typeof(Scrollbar),
        typeof(Scrollbar.ScrollEvent),
        typeof(Rect),
        typeof(Button),
        typeof(Button.ButtonClickedEvent),
        typeof(EmptyRaycast),
        typeof(UITrigger),
        typeof(UITrigger.UITriggerEvent),
        typeof(UIToggleGroup),
        typeof(UIToggle),
        typeof(UIToggle.ToggleEvent),
        typeof(UIToggle.ToggleClickedEvent),
        typeof(UISlider),
        typeof(UIDropdown),
        typeof(WwiseButton),
        typeof(Sprite),
        typeof(Font),
        typeof(Slider),
        typeof(Slider.SliderEvent),
        typeof(Image),
        typeof(RawImage),
        typeof(Dropdown),
        typeof(Dropdown.DropdownEvent),
        typeof(HorizontalLayoutGroup),
        typeof(Dropdown.OptionData),
        typeof(UnityEngine.EventSystems.EventSystem),
        typeof(UnityEngine.EventSystems.StandaloneInputModule),
        typeof(UnityEngine.EventSystems.SgrStandaloneInputModule),
        typeof(UnityEngine.EventSystems.PointerEventData),
        typeof(UnityEngine.EventSystems.EventTrigger),
        typeof(UnityEngine.EventSystems.EventTriggerType),
        typeof(UnityEngine.EventSystems.EventTrigger.Entry),
        typeof(UnityEngine.EventSystems.EventTrigger.TriggerEvent),
        typeof(RenderMode),
        typeof(GraphicRaycaster),
        typeof(CanvasScaler),
        typeof(CanvasScaler.ScaleMode),
        typeof(LayoutElement),
        typeof(LayoutRebuilder),
        typeof(VerticalLayoutGroup),
        typeof(GUIUtility),
        typeof(CollisionFlags),
        typeof(MaskableGraphic),
        typeof(UnityEngine.Playables.PlayableDirector),
        typeof(UnityEngine.Playables.PlayableBinding),
        typeof(UnityEngine.Playables.PlayableAsset),
        typeof(UnityEngine.Playables.PlayState),
        typeof(UnityEngine.Playables.Playable),
        typeof(UnityEngine.Playables.PlayableGraph),
        typeof(UnityEngine.Playables.PlayableOutput),
        typeof(UnityEngine.Playables.PlayableHandle),
        typeof(UnityEngine.Animations.AnimationClipPlayable),
        typeof(UnityEngine.Animations.AnimationMixerPlayable),
        typeof(UnityEngine.Animations.AnimationLayerMixerPlayable),
        typeof(UnityEngine.Timeline.TimelineAsset),
        typeof(UnityEngine.Timeline.TimelineClip),
        typeof(UnityEngine.Timeline.ActivationTrack),
        typeof(UnityEngine.Timeline.AnimationTrack),
        typeof(UnityEngine.Timeline.TimelinePlayable),
        typeof(UnityEngine.Timeline.TimeControlPlayable),
        typeof(UnityEngine.Experimental.Rendering.GraphicsFormat),
        typeof(Touch),
        typeof(TouchPhase),
        typeof(KeyCode),
        typeof(UnityEngine.EventSystems.RaycastResult),
        typeof(HorizontalGridScrollList),
        typeof(HorizontalGridScrollList.HSItemEntity),
        typeof(VerticalTreeScrollList),
        typeof(VerticalTreeScrollList.VSItemEntity),
        typeof(AnimatorEventExpand),
        typeof(ParticleSystem.MainModule),
        typeof(ParticleSystem.MinMaxCurve),
        typeof(AutomaticBackground),
        typeof(AdaptationLocking),
        typeof(AnimationCurve),

        typeof(UnityEngine.Video.VideoPlayer),
        typeof(UnityEngine.Video.VideoClip),
        typeof(UnityEngine.TouchScreenKeyboard),
        typeof(UnityEngine.Display),
        typeof(UnityEngine.Profiling.Profiler),
        typeof(VirtualMouse),
        typeof(UnityEngine.UI.Selectable),
    };

    // Unity原始类型暂时还是配表，后面统一全部导出
    public static HashSet<Type> UnityExportStaticClass = new HashSet<Type>
    {
        typeof(Application),
        typeof(Time),
        typeof(Screen),
        typeof(SleepTimeout),
        typeof(Input),
        typeof(Resources),
        typeof(Physics),
        typeof(RenderSettings),
        typeof(QualitySettings),
        typeof(GL),
        typeof(Debug),
        typeof(UnityEngine.SceneManagement.SceneManager),
        typeof(UnityEngine.EventSystems.ExecuteEvents),
        typeof(UnityEngine.Playables.PlayableExtensions),
        typeof(UnityEngine.Playables.PlayableOutputExtensions),
        typeof(UnityEngine.ScreenCapture),
    };

    public static HashSet<Type> DragonExportClass = new HashSet<Type>
    {
        typeof(SgrEngine.WwiseParticleHelper),
        // 迷雾
        typeof(SgrEngine.SandboxFogComponent),
        typeof(SgrEngine.FogTextureData),

        // 红点
        typeof(SgrProject.RedDotNodeData),
        typeof(SgrProject.RedDotDebudData),
        //red dot end

        // UI画线
        typeof(UnityEngine.UI.UIPrimitiveBase),
        typeof(UnityEngine.UI.UILineRenderer),
        typeof(UnityEngine.UI.UILineRenderer.BezierType),
        typeof(UnityEngine.UI.UILineRenderer.JoinType),

        // 多语言
        typeof(SgrUnity.SgrImage),
        typeof(UnityEngine.UI.HyperLinkText),
        typeof(UnityEngine.UI.TraceAreaDrawing),

        //TMP
        typeof(XSolution.XMultilanguage.MultiLanguageTMPText),
        typeof(TMPro.TextMeshProUGUI),
        typeof(TMPro.TextMeshPro),
        typeof(TMPro.TMP_Text),
        typeof(TMPro.TMP_TextInfo),
        typeof(TMPro.TMP_Asset),
        typeof(TMPro.TMP_ColorGradient),
        typeof(TMPro.TMP_SpriteAsset),
        typeof(TMPro.TMP_FontAsset),
        typeof(TMPro.TMP_StyleSheet),
        typeof(TMPro.TMP_Style),
        typeof(TMPro.TextAlignmentOptions),
        typeof(TMPro.TMP_Settings),
        typeof(TMPro.TMP_LinkInfo),
        //TMP_Input
        typeof(TMPro.TMP_InputField),
        typeof(TMPro.TMP_InputField.SubmitEvent),
        typeof(TMPro.TMP_InputField.OnChangeEvent),
        typeof(TMPro.TMP_InputField.SelectionEvent),
        typeof(TMPro.TMP_InputField.TextSelectionEvent),
        typeof(TMPro.TMP_InputField.TouchScreenKeyboardEvent),
        //动态高清地图
        typeof(UnityEngine.UI.MapSudokuRT),

        // 滚动容器
        typeof(SgrEngine.GridScrollListComp),
        typeof(SgrEngine.GridScrollList),
        typeof(SgrEngine.GridLayoutSettings),
        typeof(SgrEngine.ScrollListItem),
        typeof(SgrEngine.HorizontalScrollListComp),
        typeof(SgrEngine.HorizontalLayoutSettings),
        typeof(SgrEngine.HorizontalScrollList),
        typeof(SgrEngine.VerticalScrollListComp),
        typeof(SgrEngine.VerticalScrollList),
        typeof(SgrEngine.VerticalLayoutSettings),
        typeof(SgrUnity.AutoClose),

        // 动画
        typeof(SgrEngine.MixAnimationMecanim),
        typeof(SgrEngine.TimelineHelper),
        typeof(SgrEngine.AttachUtil),
        typeof(SgrEngine.AnimatorPythonCallback),
        typeof(SgrEngine.SimpleAnimator),

        typeof(SgrEngine.Playables.AnimationLayerMixerPlayable),
        typeof(SgrEngine.Playables.AnimationPlayableOutput),
        typeof(SgrEngine.Playables.AnimatorControllerPlayable),
        typeof(SgrEngine.Playables.FlexibleClipPlayable),
        typeof(SgrEngine.Playables.PlayableGraph),
        typeof(SgrEngine.Playables.TranslatePlayable),
        typeof(SgrEngine.Playables.SgrPlayable),
        typeof(SgrEngine.Animations.SgrAnimPlayer),
        typeof(SgrEngine.AnimationEventHelper),
        typeof(MagicaCloth.BaseCloth),
        typeof(MagicaCloth.MagicaBoneCloth),
        typeof(MagicaCloth.BoneClothTarget),
        typeof(MagicaCloth.MagicaPhysicsManager),

        // Spine动画
        typeof(SgrEngine.UIPanelAnimationManager),
        typeof(Spine.Unity.SkeletonAnimation),
        typeof(Spine.AnimationState),
        typeof(Spine.AnimationStateData),
        typeof(Spine.Animation),
        typeof(Spine.SkeletonData),
        typeof(Spine.TrackEntry),

        // 特效
        typeof(SgrEngine.EffectObject),
        typeof(SgrEngine.PositionBindHelper),
        typeof(SgrEngine.CameraShake),
        typeof(SgrEngine.PostProcess),
        typeof(SgrEngine.LineEffectComp),
        typeof(SgrEngine.TagMapDictionary),
        typeof(SgrEngine.TagDictionary),
        typeof(SgrEngine.ResTagComponent),
        typeof(SgrEngine.SmoothLineRender),

        // 相机
        typeof(SgrEngine.CameraComp),
        typeof(SgrEngine.CameraManager),
        typeof(SgrEngine.BaseCameraMode),
        typeof(SgrEngine.CameraModeState),
        typeof(SgrEngine.CameraPythonMode),
        typeof(SgrEngine.CameraMoveMode),
        typeof(SgrEngine.CameraDynamicMode),
        typeof(SgrEngine.CameraManualMode),
        typeof(SgrEngine.CameraScaleMode),
        typeof(SgrEngine.CameraSandBoxMoveMode),
        typeof(SgrEngine.CameraAltarOpt),
        typeof(SgrEngine.CameraTransverseOpt),
        typeof(SgrEngine.CameraOcclusionCullingMode),
        typeof(SgrEngine.CameraCinemachineMode),
        typeof(SgrEngine.CameraShakeMode),
        typeof(SgrEngine.ShakeInfo),
        typeof(SgrEngine.PlotMoveParams),
        typeof(SgrEngine.PlotMoveMode),
        typeof(SgrEngine.VirtualCamera),
        typeof(SgrEngine.ModeVirtualCamera),
        typeof(SgrEngine.CameraSetPositionMode),
        typeof(SgrEngine.CameraHangUpMode),

        // cinemachine
        typeof(Cinemachine.CinemachineBrain),
        typeof(Cinemachine.CinemachineVirtualCamera),
        typeof(Cinemachine.CinemachineVirtualCameraBase),
        typeof(Cinemachine.CinemachineTransposer),
        typeof(Cinemachine.CinemachineTargetGroup),
        typeof(Cinemachine.CinemachineTargetGroup.Target),
        typeof(Cinemachine.CinemachineGroupComposer),
        typeof(Cinemachine.CinemachineBlendListCamera),
        typeof(Cinemachine.CinemachineBlendListCamera.Instruction),
        typeof(Cinemachine.CinemachineBlenderSettings),
        typeof(Cinemachine.CinemachineBlenderSettings.CustomBlend),
        typeof(Cinemachine.CinemachineBlendDefinition),
        typeof(Cinemachine.CameraState),
        typeof(Cinemachine.LensSettings),
        typeof(Cinemachine.CinemachineBasicMultiChannelPerlin),
        typeof(Cinemachine.NoiseSettings),
        typeof(CinemachineTrack),
        typeof(SgrEngine.CinemachineStateParam),

        // 移动
        typeof(SgrEngine.CombatMovementComponent),
        typeof(SgrEngine.SandboxMovementComponent),

        // DoTween
        typeof(DG.Tweening.Ease),
        typeof(DG.Tweening.AxisConstraint),
        typeof(SgrEngine.SgrTween.TweenEntity),
        typeof(SgrEngine.SgrTween.TMPTextMaskEntity),
        typeof(SgrEngine.SgrTween.TextMaskEntity),
        typeof(SgrEngine.SgrTween.TransformEntity),
        typeof(SgrEngine.SgrTween.GraphicColorEntity),
        typeof(SgrEngine.SgrTween.CanvasGroupEntity),
        typeof(SgrEngine.SgrTween.ImageFillAmountEntity),
        typeof(SgrEngine.SgrTween.RectTransformEntity),
        typeof(SgrEngine.SgrTween.MaterialColorEntity),
        typeof(SgrEngine.SgrTween.MaterialVectorEntity),
        typeof(SgrEngine.SgrTween.MaterialFloatEntity),

        typeof(UITest.UIShaderParamHelper_vx_common_shader),

        // 打包
        typeof(XSolution.AssetBundles.AtlasInformation),
        typeof(XSolution.AssetBundles.SpriteInfo),
        typeof(XSolution.AssetBundles.AtlasInformationCollection),
        typeof(XSolution.AssetBundles.AssetOperationHandle),
        typeof(XSolution.AssetBundles.AppBuildConst),
        typeof(XSolution.AssetBundles.WebGetRequest),
        typeof(XSolution.AssetBundles.FileDownloader),
        typeof(XSolution.AssetBundles.DownloadFileInfo),
        typeof(XSolution.AssetBundles.WebTaskRequestStatus),
        typeof(XSolution.AssetBundles.PatchManager),
        typeof(XSolution.AssetBundles.PatchCache),
        typeof(XSolution.AssetBundles.PatchManifest),
        typeof(XSolution.AssetBundles.RuntimePatchManifest),
        typeof(XSolution.AssetBundles.PatchBundle),
        typeof(XSolution.AssetBundles.PatchAsset),
        typeof(XSolution.AssetBundles.PatchRawAsset),
        typeof(XSolution.AssetBundles.PatchScript),
        typeof(XSolution.AssetBundles.PatchVersion),
        typeof(XSolution.AssetBundles.PackedFile),
        typeof(XSolution.AssetBundles.IVirtualFileSystem),
        typeof(XSolution.AssetBundles.VirtualFileSystem),
        typeof(XSolution.AssetBundles.CppVirtualFileSystem),
        typeof(LoveEngine.Native.FSNIStream),
        typeof(XSolution.AssetBundles.ReadOnlyFile),
        typeof(XSolution.AssetBundles.BundleFileLoader),
        typeof(XSolution.AssetBundles.AssetBundleProvider),
        typeof(XSolution.AssetBundles.AssetProviderBase),
        typeof(XSolution.AssetBundles.AssetSceneProvider),

        // 音频
        typeof(AkCallbackInfo),
        typeof(AkCallbackType),
        typeof(AkEventCallbackInfo),
        typeof(AkActionOnEventType),
        typeof(AkWwiseInitializationSettings),
        typeof(AK.Wwise.WwiseAudioManager),
        typeof(AK.Wwise.WwiseAmbient),
        typeof(AK.Wwise.WwiseBanksManifestAsset),
        typeof(AK.Wwise.UIWwiseAnimStateEvent),
        typeof(AK.Wwise.UIWwiseEvent),
        // 视频criware
        typeof(XSolution.GameCriVideo),
        typeof(CriManaMovieMaterial),
        typeof(CriTimeline.Mana.CriManaTrack),
        typeof(CriMana.Player),
        typeof(CriWare.Common),
        typeof(XSolution.CriVideoManager),

        // 其他
        typeof(SgrEngine.SandboxMoveComponent),
        typeof(SgrEngine.PixelMeshTextureData),
        typeof(SgrEngine.BattleStageShowComponent),
        typeof(SgrEngine.BattleModelShakeComponent),
        typeof(SgrEngine.UIHeadInfoComp),
        typeof(SgrEngine.UIHeadFollowTargetComp),
        typeof(SgrEngine.MaterialHelper),
        typeof(SgrEngine.BloodBarHelper),
        typeof(SgrEngine.CharacterMeshHelper),
        typeof(SgrEngine.VideoTrack),
        typeof(SgrEngine.VideoPlayableAsset),
        typeof(SgrEngine.VideoPlayableBehaviour),
        typeof(SgrEngine.MixAnimationTrack),
        typeof(SgrEngine.TimelineLoopTrack),
        typeof(SgrEngine.TimelineLoopClipBehaviour),
        typeof(SgrEngine.TimelineLoopClip),
        typeof(SgrEngine.ResourceManager),
        typeof(SgrProject.XPythonManager),
        typeof(SgrProject.SandboxEventManager),
        typeof(SgrProject.SandboxAnimatorState),
        typeof(EraseProgress),
        typeof(SgrEngine.PerfStat),
        typeof(SgrEngine.PerfHardware),
        typeof(SgrEngine.AutoPerf),
        typeof(SgrUnity.UIHeadDarkener),
        typeof(SgrUnity.UINodeMapping),
        typeof(SgrEngine.UserDataUtils),
        typeof(SgrEngine.CustomLightHelper),
        typeof(SgrEngine.LookAtHelper),
        typeof(SgrEngine.LookAtData),
        typeof(SgrEngine.BridgeFloatingHelper),
        typeof(SgrEngine.StoneFloatingHelper),
        typeof(SgrEngine.TimelineLoopClipBehaviour),
        typeof(SgrEngine.TimelineLoopClip),
        typeof(SgrEngine.TimelineLoopTrack),
        typeof(SgrEngine.Capturer),
        typeof(LightMaskHelper),
        typeof(LightMaskManager),

        typeof(SgrEngine.TrailRendererCycle),
        typeof(SgrEngine.BackwardMovement),

        /******************** Gsdk ***********************/
#if USE_MSDK
        //msdk
        typeof(TDM.ITDataMaster),
        typeof(GCloud.NetworkService),
        typeof(GCloud.DetailNetworkInfo),
        typeof(GCloud.MSDK.MSDK),
        typeof(GCloud.MSDK.MSDKLogin),
        typeof(GCloud.MSDK.MSDKAccount),
        typeof(GCloud.MSDK.MSDKCrash),
        typeof(GCloud.MSDK.MSDKExtend),
        typeof(GCloud.MSDK.MSDKFriend),
        typeof(GCloud.MSDK.MSDKGroup),
        typeof(GCloud.MSDK.MSDKLBS),
        typeof(GCloud.MSDK.MSDKLogin),
        typeof(GCloud.MSDK.MSDKNotice),
        typeof(GCloud.MSDK.MSDKPush),
        typeof(GCloud.MSDK.MSDKPxView),
        typeof(GCloud.MSDK.MSDKTools),
        typeof(GCloud.MSDK.MSDKWebView),
        //ret
        typeof(GCloud.MSDK.MSDKAccountRet),
        typeof(GCloud.MSDK.MSDKExtendRet),
        typeof(GCloud.MSDK.MSDKFriendReqInfo),
        typeof(GCloud.MSDK.MSDKFriendRet),
        typeof(GCloud.MSDK.MSDKPersonInfo),
        typeof(GCloud.MSDK.MSDKGroupInfo),
        typeof(GCloud.MSDK.MSDKGroupMessage),
        typeof(GCloud.MSDK.MSDKGroupRet),
        typeof(GCloud.MSDK.MSDKLBSLocationRet),
        typeof(GCloud.MSDK.MSDKLBSPersonInfo),
        typeof(GCloud.MSDK.MSDKLBSRelationRet),
        typeof(GCloud.MSDK.MSDKLBSIPInfoRet),
        typeof(GCloud.MSDK.MSDKLoginRet),
        typeof(GCloud.MSDK.MSDKNoticePictureInfo),
        typeof(GCloud.MSDK.MSDKNoticeTextInfo),
        typeof(GCloud.MSDK.MSDKNoticeInfo),
        typeof(GCloud.MSDK.MSDKNoticeRet),
        typeof(GCloud.MSDK.MSDKLocalNotification),
        typeof(GCloud.MSDK.MSDKPushRet),
        typeof(GCloud.MSDK.MSDKPxViewRet),
        typeof(GCloud.MSDK.MSDKToolsFreeFlowInfo),
        typeof(GCloud.MSDK.MSDKToolsRet),
        typeof(GCloud.MSDK.MSDKWebViewRet),
        typeof(GCloud.MSDK.MSDKBaseRet),
        typeof(GCloud.MSDK.RetArgsWrapper),
        //Dolphin
        typeof(GCloud.DolphinUpdateAppData),
        typeof(GCloud.DolphinEventWrapper),
        typeof(GCloud.Dolphin.DolphinMgrImp),
        typeof(GCloud.Dolphin.DolphinMgrInterface),
        typeof(GCloud.Dolphin.DolphinDateInterface),
        typeof(GCloud.Dolphin.DolphinCallBackInterface),
        typeof(GCloud.Dolphin.DolphinFactory),
        typeof(GCloud.Dolphin.DolphinMgrInterface),
        typeof(GCloud.Dolphin.UpdateInitInfo),
        typeof(GCloud.Dolphin.UpdateInitType),
        typeof(GCloud.Dolphin.NewVersionInfo),
        typeof(GCloud.Dolphin.MessageBoxType),
        typeof(IIPSMobile.IIPSMobileVersionCallBack),
        typeof(IIPSMobile.IIPSMobileVersionCallBack.VERSIONSTAGE),
        typeof(IIPSMobile.IIPSMobileVersionCallBack.NOTICEACTIONTYPE),
        
        //maple
        typeof(GCloud.GCloudMapleManager),
        typeof(GCloud.TreeNodeType),
        typeof(GCloud.TreeNodeFlag),
        typeof(GCloud.TreeNodeTag),
        typeof(GCloud.TdirCustomData),
        typeof(GCloud.CategoryNode),
        typeof(GCloud.LeafNode),
        typeof(GCloud.NodeWrapper),
        typeof(GCloud.TreeInfo),
        typeof(GCloud.Result),
        typeof(GCloud.ErrorCode),

        //CrashSgiht
        typeof(CrashSightAgent),
        typeof(CrashSightLogCallback),
        typeof(SgrCrashSightLogCallback),
        //PerfSight
        typeof(GCloud.GPM.GPMAgent),
        //TGPA
        typeof(GCloud.TGPA.TGPAHelper),

//海外
#if IS_OVERSEA

        typeof(gcloud_voice.IGCloudVoice.GCloudVoice),
        typeof(gcloud_voice.GCloudVoiceEngine),

#endif

//无鸿蒙
#if !PLATFORM_TYPE_HAR && USE_H5UISDK
        typeof(H5UI.H5Scene),
        typeof(H5UI.Plugin),
        typeof(H5UI.Configure),
        typeof(H5UI.H5Scene.SceneLoadedEvent),
        typeof(H5UI.H5Scene.SceneLoadFailedEvent),
        typeof(H5UI.H5Scene.SceneDidCloseEvent),
        typeof(H5UI.H5Scene.SceneDidShowEvent),
        typeof(H5UI.H5Scene.SceneShowFailedEvent),
        typeof(H5UI.H5Scene.SceneDidReceiveMessageEvent),
#endif

//国内windows
#if GCLOUD_MSDK_WINDOWS && !IS_OVERSEA
        /******************** wegame ***********************/	
        typeof(rail.EventBase),
        typeof(rail.RailSystemState),
        typeof(rail.RailSystemStateChanged),
        typeof(rail.RAILEventID),
        typeof(rail.IRailPlayerImpl),
        typeof(rail.IRailFactoryImpl),
        typeof(rail.IRailSystemHelperImpl),
        typeof(rail.IRailThirdPartyAccountLoginHelperImpl),
        typeof(rail.RailThirdPartyAccountInfo),
        typeof(rail.RailThirdPartyAccountLoginResult),
        typeof(rail.RailResult),
        typeof(rail.RailCallBackHelper),
        typeof(rail.RailPlayerAccountType),
        typeof(rail.RailThirdPartyAccountLoginOptions),
        typeof(rail.rail_api),
        typeof(rail.RailGameID),
        typeof(RailManager),
#endif

        //移动端
#if !(GCLOUD_MSDK_WINDOWS || GCLOUD_MSDK_MAC)
        typeof(GCloud.MSDK.MSDKGame),
        typeof(GCloud.MSDK.MSDKReport),
        typeof(GCloud.MSDK.MSDKSensitive),
#endif

//国内非鸿蒙移动端
#if !IS_OVERSEA && !PLATFORM_TYPE_HAR && (PLATFORM_TYPE_AND || PLATFORM_TYPE_IOS)
        //GRobot
        typeof(GCloud.GRobot.GRobotEngine),
#endif

//海外移动端(无鸿蒙)
#if IS_OVERSEA && (PLATFORM_TYPE_AND || PLATFORM_TYPE_IOS) && !(IS_RU && PLATFORM_TYPE_AND)
        typeof(CentauriPay.MidasCallbackWrapper),
        typeof(CentauriPay.CTIPayService),
        typeof(CentauriPay.CallBackUtils),
        typeof(CentauriPay.CTIBaseRequest),
        typeof(CentauriPay.CTIGameRequest),
        typeof(CentauriPay.CTIGoodsRequest),
        typeof(CentauriPay.CTISubscribeRequest),
        typeof(CentauriPay.CTIPayCallback),
        typeof(CentauriPay.CTIResponse),
        typeof(CentauriPay.CTIInitCallback),
        typeof(CentauriPay.CTIGetInfoCallback),
        typeof(CentauriPay.CTIGetLocalPriceCallback),
        typeof(CentauriPay.CTIReprovideCallback),
        typeof(CentauriSpecial.CTISpclRequest),
#endif

//海外APKPure移动端(无鸿蒙)
#if IS_RU && PLATFORM_TYPE_AND
        typeof(CentauriPay.MidasCallbackWrapper),
        typeof(CentauriPay.CTIUnifiedPayService),
        typeof(CentauriPay.ICTICallback),
        typeof(CentauriPay.CTIResponse),
        typeof(CentauriPay.CTI_RET_CODE),
        typeof(CentauriPay.CTI_PAYMENT_METHOD),
        typeof(CentauriPay.CTI_ENV),
        typeof(CentauriPay.CTIUnifiedInitParams),
        typeof(CentauriPay.CTIPayParams),
        typeof(CentauriPay.CTIPromotionParams),
        typeof(CentauriPay.CTIProductInfoParams),
        typeof(CentauriPay.CTIReceiptParams),
        typeof(CentauriPay.CTIQueueService),
#endif

//国内移动端
#if !IS_OVERSEA && (PLATFORM_TYPE_AND || PLATFORM_TYPE_IOS || PLATFORM_TYPE_HAR)
        typeof(MidasPay.MidasCallbackWrapper),
        typeof(MidasPay.MidasPayService),
        typeof(MidasPay.CallBackUtils),
        typeof(MidasPay.APMidasBaseRequest),
        typeof(MidasPay.APMidasGameRequest),
        typeof(MidasPay.APMidasGoodsRequest),
        typeof(MidasPay.APMidasSubscribeRequest),
        typeof(MidasPay.APMidasResponse),
        typeof(MidasPay.MidasInitCallback),
        typeof(MidasPay.MidasGetLocalPriceCallback),
        typeof(MidasPay.MidasPayCallback),
        typeof(MidasPay.MidasReprovideCallback),
        typeof(MidasPay.MidasGetInfoCallback),
        typeof(MidasPay.MidasGetIntroPriceCallback),
        typeof(MidasPay.MidasReceiptInfoCallback),
#endif

#endif

#if USE_GSDK
        typeof(GSDK.GSDKInner),
        typeof(GSDK.AccountBytedanceService),
        typeof(GSDK.Result),
        typeof(GSDK.AccountInfo),
        typeof(GSDK.AccountInfoForCloudGame),
        typeof(GSDK.AccountType),
        typeof(GSDK.AccountInnerTools),
        typeof(GSDK.AccountConstants),
        typeof(GSDK.AccountCallbackHandler),
        typeof(GSDK.BindInfo),
        typeof(GSDK.DetailedAccountInfo),
        typeof(GSDK.UserDetailInfo),
        typeof(GSDK.AppService),
        typeof(GSDK.WebviewService),
        typeof(GSDK.WebviewParameter),
        typeof(GSDK.SecurityService),
        typeof(GSDK.PerformancePriority),
        typeof(GSDK.ReportService),
        typeof(GSDK.ETService),
        typeof(GSDK.MonitorService),
        typeof(GSDK.UploadFileInfo),
        typeof(GSDK.GPMMonitorService),
        typeof(GSDK.SystemService),
        typeof(GSDK.ComplianceService),
        typeof(GSDK.GeasService),
        typeof(GSDK.UpgradeService),
        typeof(GSDK.ProtocolService),
        typeof(GSDK.ProtocolAddressResult),
        typeof(GSDK.ProtocolUpdateWithUIResult),
        typeof(GSDK.ProtocolCallbackHandler),
        typeof(GSDK.LocationService),
        typeof(GSDK.LocationInfo),
        typeof(GSDK.PushService),
        typeof(GSDK.Notification),
        typeof(GSDK.OneTimeNotification),
        typeof(GSDK.RepeatNotification),
        typeof(GSDK.PushContent),
        typeof(GSDK.PushDate),
        typeof(GSDK.PushTime),
        typeof(GSDK.BulletinService),
        typeof(GSDK.BulletinConfig),
        typeof(GSDK.BulletinInfo),
        typeof(GSDK.BulletinItem),
        typeof(GSDK.ImageItem),
        typeof(GSDK.QRCodeService),
        typeof(GSDK.GnaClientService),
        typeof(GSDK.NetMpaService),
        typeof(GSDK.NetMnaService),
        typeof(GSDK.GameNetDiagnosisService),
        typeof(GSDK.NetExperienceService),
        typeof(GSDK.RatingService),
        typeof(GSDK.RNU.RNUMain),
#if ENABLE_REACT_UNITY_TEXTMESHPRO
        typeof(ReactUnityTextMeshPro.RUTMPPackage),
#endif
        typeof(GSDK.ReactNativeMessage),
        typeof(GSDK.ReactNativeService),
        typeof(GSDK.ReactNativeScene),
        typeof(GSDK.ReactNativeSceneType),
        typeof(GSDK.ReactNativeBadge),
#if UNITY_IOS
        typeof(GSDK.ReactNativeOrientationType),
#endif
        typeof(GSDK.ReactNativeWindowImplementation),
        typeof(GSDK.ReactNativePageImplementationWrapper),
        typeof(GSDK.CustomPraiseViewHandler),
        typeof(GSDK.ExceptionInfo),
#if UNITY_STANDALONE
        typeof(GSDK.LoginQRCodeInfo),
#endif
        // pay start
        typeof(GSDK.PayService),
        typeof(GSDK.PayInnerTools),
        typeof(GSDK.Product),
        typeof(GSDK.ProductAccumulation),
        typeof(GSDK.GoodsType),
        typeof(GSDK.RoleInfoForPay),
        typeof(GSDK.ProductActivity),
        typeof(GSDK.ProductStatus),
        typeof(GSDK.ProductAccumulationDetail),
        typeof(GSDK.GiftType),
        typeof(GSDK.ActivityType),
        typeof(GSDK.ProductPriceInfo),
        //pay end
        //苹果ATT弹窗
        typeof(GSDK.CdKeyService),
        typeof(GSDK.RoleService),
        typeof(GSDK.ZoneInfo),
        typeof(GSDK.RoleInfo),
        typeof(GSDK.ServerInfo),
        typeof(GSDK.ServerTag),
        //share
        typeof(GSDK.ShareService),
        typeof(GSDK.ShareData),
        typeof(GSDK.SystemShare.Link),
        typeof(GSDK.SystemShare.Text),
        typeof(GSDK.SystemShare.Video),
        typeof(GSDK.SystemShare.Image),
        typeof(GSDK.SaveImageShare.Image),
        //videoplayer
        typeof(GSDK.VideoPlayerService),
#if UNITY_ANDROID
        typeof(GSDK.AndroidV1VideoPlayer),
        typeof(GSDK.AndroidV2VideoPlayer),
#endif

#if UNITY_IOS
        typeof(GSDK.IOSVidoePlayer),
#endif

#if IS_OVERSEA
        typeof(GSDK.FacebookShare.Link),
        typeof(GSDK.FacebookShare.Video),
        typeof(GSDK.FacebookShare.Image),
        typeof(GSDK.TiktokShare.Video),
#if PLATFORM_TYPE_AND
        typeof(GSDK.TwitterShare.Link),
        typeof(GSDK.TwitterShare.Text),
        typeof(GSDK.TwitterShare.Image),
        typeof(GSDK.TwitterShare.Video),
#endif
#endif
        // replay
        typeof(GSDK.ReplayService),
        typeof(GSDK.ReplayStartScreenRecordConfig),
        typeof(GSDK.ReplayPrepareResult),
        typeof(GSDK.ReplayRangeInfo),
        typeof(GSDK.ReplayProgressRet),
        typeof(GSDK.ReplaySimpleClipRet),
        typeof(GSDK.ReplaySampleProcessRet),
        typeof(GSDK.ReplayVideoPathRet),
        typeof(GSDK.ReplaySimpleProcessRet),
        typeof(GSDK.ReplaySimpleBGMInfo),
        typeof(GSDK.ReplaySimpleOutputInfo),
        typeof(GSDK.ReplaySimpleBackgroundFillInfo),
        typeof(GSDK.ReplayTimeSpeedAnchor),
        typeof(GSDK.ReplayVideoStatusInfo),
        typeof(GSDK.ReplayProcessConfig),
        typeof(GSDK.ReplayProcessVideoRet),
        typeof(GSDK.ReplayVideoInfo),
        typeof(GSDK.ReplayServerVideoListRet),
        typeof(GSDK.ReplayServerVideoInfo),
        typeof(GSDK.ReplayWebTrafficRet),
        typeof(GSDK.ReplayVideoShareInfoRet),
        typeof(GSDK.ReplayHighlightPicturesExistRet),
        typeof(GSDK.ReplayCollectionRangesExistRet),
        typeof(GSDK.ReplayHighlightVideoInfoList),
        typeof(GSDK.ReplayHighlightVideoInfo),
        typeof(GSDK.ReplayVideoFileInfo),
        typeof(GSDK.ReplayResourceNeedUpdate),
        typeof(GSDK.ReplayDownloadMaterial),
        typeof(GSDK.ReplayLocalCacheSize),
        typeof(GSDK.ReplayVideoExistRet),
        typeof(GSDK.ReplayMediaFilePathRet),
        //因为gsdk IEffect IReplaySticker需要封装的类
        typeof(GSDK.ReplaySimpleVideoInfoWrapper),
        typeof(GSDK.ReplaySimpleProcessInfoWrapper),
        typeof(GSDK.ReplayStickerInfoWrapper),
        typeof(GSDK.ReplayTextStickerInfoWrapper),

        // replay end

#if IS_OVERSEA
        typeof(GSDK.AgeGateService),
#if !UNITY_STANDALONE_WIN
        typeof(GSDK.FirebaseService),
        typeof(GSDK.AppsFlyerService),
#endif
#endif

#if !IS_OVERSEA
        // 实名
        typeof(GSDK.RealNameService),
        typeof(GSDK.RealNameAuthType),
        typeof(GSDK.RealNameState),
        typeof(GSDK.RealNameInfo),
        typeof(GSDK.RealNameAuthLevel),
        // 防沉迷
        typeof(GSDK.AntiAddictionService),
        typeof(GSDK.AntiAddictionInfo),
        typeof(GSDK.AntiAddictionOperation),
        // 隐私
        typeof(GSDK.PrivacyService),
        typeof(GSDK.PrivacyCallbackHandler),
#endif
#endif

#if UNITY_ANDROID && USE_CGSDK
        typeof(GameMatrix.CGSdk),
        typeof(GameMatrix.CloudInfo),
        typeof(GameMatrix.DeviceInfo),
#endif
    };

    public static HashSet<Type> DragonExportStaticClass = new HashSet<Type>
    {
        // 打包
        typeof(XSolution.AssetBundles.AssetSystem),
        typeof(XSolution.AssetBundles.PatchHelper),
        typeof(XSolution.AssetBundles.AssetPathHelper),
        typeof(XSolution.AssetBundles.DownloadSystem),
        typeof(XSolution.AssetBundles.HashUtility),
        typeof(XSolution.AssetBundles.DLCAssetCollecter),
        typeof(XSolution.AssetBundles.CompressionHelper),
        typeof(XSolution.AssetBundles.LaunchHelper),

        // 特效
        typeof(SgrEngine.EffectObjectManager),

        // 音频
        typeof(AkCallbackManager),
        typeof(AK.Wwise.WwiseHelper),

        // 迷雾
        typeof(SgrEngine.FogRegionTextureMgr),


        // 其他
        typeof(SgrEngine.SceneEditorManager),
        typeof(SgrUnity.UIManager),
        typeof(SgrUnity.UIHeadDarkenerUtility),
        typeof(SgrUnity.TextureAtlasManager),
        typeof(SgrEngine.InputManager),
        typeof(XSolution.LogicConfig),
        typeof(SgrEngine.TileMeshDebugManager),
        typeof(SgrEngine.QuadTreeDebugManager),
        typeof(SgrEngine.StorageUtil),
        typeof(SgrEngine.TriggerManager),
        typeof(SgrEngine.GameUtil),
        typeof(SgrEngine.GameLogicUtil),
        typeof(SgrEngine.CameraUtils),
        typeof(SgrEngine.SgrConfig),
        typeof(VideosManager),
        typeof(SgrEngine.LogManager),
        typeof(SgrEngine.PluginManager),
        typeof(SgrEngine.SgrTween),
        typeof(SgrEngine.FractureVATHelper),

        typeof(TMPro.TMP_TextUtilities),
        typeof(SgrEngine.CommonInterfaceUtil),
        typeof(WeLink.WeLinkInterface),
        typeof(XSolution.GCControl),

        typeof(System.Environment),
        typeof(System.GC),
        typeof(GCloud.GCloudSDKWrapper),
        typeof(GCloud.MSDK.SgrMSDKEventHandler),
        typeof(AndroidInstallAPK),
        typeof(AndroidRestartAPK),
        typeof(GameMatrix.CGSDKWrapper),
        
#if USE_MSDK
#endif
        // GSDK
        typeof(GSDK.GSDKWrapper),
        typeof(GSDK.BDUploader),
#if USE_GSDK
        typeof(GSDK.GameSDK),
        typeof(GSDK.Account),
        typeof(GSDK.Pay),
        typeof(GSDK.Rating),
        typeof(GSDK.ReactNative),
        typeof(GSDK.App),
        typeof(GSDK.Webview),
        typeof(GSDK.Security),
        typeof(GSDK.Report),
        typeof(GSDK.GPMMonitor),
        typeof(GSDK.System),
        typeof(GSDK.Compliance),
        typeof(GSDK.Geas),
        typeof(GSDK.Upgrade),
        typeof(GSDK.Location),
        typeof(GSDK.Push),
        typeof(GSDK.Bulletin),
        typeof(GSDK.CdKey),
        typeof(GSDK.Role),
        typeof(GSDK.Replay),
        typeof(GSDK.Share),
        typeof(GSDK.PanelShare),
        typeof(GSDK.QRCode),
        typeof(GSDK.GMVideoPlayer),
        typeof(GSDK.GnaClient),
        typeof(GSDK.NetMpa),
        typeof(GSDK.NetMna),
        typeof(GSDK.GameNetDiagnosis),
        typeof(GSDK.NetExperience),
#if IS_OVERSEA && !UNITY_STANDALONE_WIN
        typeof(GSDK.Firebase),
        typeof(GSDK.AppsFlyer),
#endif

#if !IS_OVERSEA
        typeof(GSDK.AntiAddiction),
        typeof(GSDK.RealName),
#endif

#endif
    };

    // 导出成员宏定义
    public static Dictionary<string, string> SgrExportMemberMacroDict = new Dictionary<string, string>
    {
        { "MeshRenderer.scaleInLightmap", "UNITY_EDITOR" },
        { "MeshRenderer.receiveGI", "UNITY_EDITOR" },
        { "MeshRenderer.stitchLightmapSeams", "UNITY_EDITOR" },
        { "LogicConfig.XPythonInjectTestWin", "UNITY_STANDALONE_WIN" },
        { "LogicConfig.XPythonInjectTestAndroid", "UNITY_ANDROID" },
        { "SkeletonRenderer.EditorUpdateMeshFilterHideFlags", "UNITY_EDITOR"},
        { "SkeletonRenderer.EditorSkipSkinSync", "UNITY_EDITOR"},
        { "SkeletonRenderer.Awake", "UNITY_EDITOR"},
        { "SkeletonRenderer.Start", "UNITY_EDITOR"},
        { "SkeletonRenderer.fixPrefabOverrideViaMeshFilterGlobal", "UNITY_EDITOR"},
        { "SkeletonRenderer.fixPrefabOverrideViaMeshFilter", "UNITY_EDITOR"},
        //CTG Begin: OpenHarmony
        { "LogicConfig.XPythonInjectTestHarmony", "UNITY_OPENHARMONY" },
        //CTG End.
        { "LogicConfig.XPythonInjectTestIOS", "UNITY_IOS" },
        { "AssetBundle.CollectAssetFromBundleDirectly", "ACCELERATE_ASSETBUNDLE_LOADING" },
        { "PlayableGraph.GetEditorName", "UNITY_EDITOR" },
    };

    static List<string> exclude = new List<string>
    {
        "HideInInspector", "ExecuteInEditMode",
        "AddComponentMenu", "ContextMenu",
        "RequireComponent", "DisallowMultipleComponent",
        "SerializeField", "AssemblyIsEditorAssembly",
        "Attribute", "Types",
        "UnitySurrogateSelector", "TrackedReference",
        "TypeInferenceRules", "FFTWindow",
        "RPC", "Network", "MasterServer",
        "BitStream", "HostData",
        "ConnectionTesterStatus", "GUI", "EventType",
        "EventModifiers", "FontStyle", "TextAlignment",
        "TextEditor", "TextEditorDblClickSnapping",
        "TextGenerator", "TextClipping", "Gizmos",
        "ADBannerView", "ADInterstitialAd",
        "Android", "Tizen", "jvalue",
        //CTG Begin: OpenHarmony
        "OpenHarmony",
        //CTG End.
        "iPhone", "iOS", "Windows", "CalendarIdentifier",
        "CalendarUnit", "CalendarUnit",
        "ClusterInput", "FullScreenMovieControlMode",
        "FullScreenMovieScalingMode", "Handheld",
        "LocalNotification", "NotificationServices",
        "RemoteNotificationType", "RemoteNotification",
        "SamsungTV", "TextureCompressionQuality",
        "TouchScreenKeyboardType", "TouchScreenKeyboard",
        "MovieTexture", "UnityEngineInternal",
        "Terrain", "Tree", "SplatPrototype",
        "DetailPrototype", "DetailRenderMode",
        "MeshSubsetCombineUtility", "AOT", "Social", "Enumerator",
        "SendMouseEvents", "Cursor", "Flash", "ActionScript",
        "OnRequestRebuild", "Ping",
        "ShaderVariantCollection", "SimpleJson.Reflection",
        "CoroutineTween", "GraphicRebuildTracker",
        "Advertisements", "UnityEditor", "WSA",
        "EventProvider", "Apple",
        "ClusterInput", "Motion",
        "UnityEngine.UI.ReflectionMethodsCache", "NativeLeakDetection",
        "NativeLeakDetectionMode", "WWWAudioExtensions", "UnityEngine.Experimental"
    };

    static List<string> unityNamespaces = new List<string>()
    {
        "UnityEngine",
        "UnityEngine.UI"
    };

    static bool isExcluded(Type type)
    {
        var fullName = type.FullName;
        for (int i = 0; i < exclude.Count; i++)
        {
            if (fullName.Contains(exclude[i]))
            {
                return true;
            }
        }

        return false;
    }

    // 如果主Python编程，可以导出所有Unity类型
    public static HashSet<Type> GetUnityTypes()
    {
        HashSet<Type> ret = new();
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.ManifestModule is System.Reflection.Emit.ModuleBuilder)
            {
                continue;
            }

            foreach (var type in assembly.GetExportedTypes())
            {
                if (type.Namespace == null ||
                    !unityNamespaces.Contains(type.Namespace) ||
                    isExcluded(type) ||
                    type.BaseType == typeof(MulticastDelegate) ||
                    type.IsInterface ||
                    type.IsEnum)
                {
                    continue;
                }

                ret.Add(type);
            }
        }

        return ret;
    }

    public static void ExportPRP()
    {
        List<Assembly> assemblys = new List<Assembly>();
        Assembly prp = Assembly.Load("ByteDance.PRP.Runtime");
        Assembly main = Assembly.Load("Assembly-CSharp");
        assemblys.Add(prp);
        assemblys.Add(main);
        foreach (var assembly in assemblys)
        {
            foreach (var type in assembly.GetExportedTypes())
            {
                if (!type.IsSubclassOf(typeof(IPRPRenderFeatureBase)) &&
                    !type.IsSubclassOf(typeof(VolumeComponent)) &&
                    !type.IsSubclassOf(typeof(RVTComponent)) &&
                    type != typeof(IPRPRenderFeatureBase) &&
                    type != typeof(VolumeComponent) &&
                    type != typeof(RVTComponent))
                {
                    continue;
                }

                if (type.IsGenericType)
                {
                    continue;
                }

                if (!DragonExportClass.Contains(type))
                {
                    DragonExportClass.Add(type);
                }
            }
        }

        DragonExportClass.Add(typeof(PulsarRenderer.PRPRenderConfig));
        DragonExportClass.Add(typeof(PulsarRenderer.PRPShadowConfig));
        DragonExportClass.Add(typeof(PulsarRenderer.PulsarRenderPipeline));
        DragonExportClass.Add(typeof(PulsarRenderer.PulsarRenderView));
        DragonExportClass.Add(typeof(PulsarRenderer.PRPRenderGraph));
        DragonExportClass.Add(typeof(PulsarRenderer.QualitySettingManager));
        DragonExportClass.Add(typeof(PulsarRenderer.PulsarAdditionalCameraData));
        DragonExportClass.Add(typeof(PulsarRenderer.PulsarRenderPipelineAsset));
        DragonExportClass.Add(typeof(PulsarRenderer.RenderFeatureQuality));
        DragonExportClass.Add(typeof(PulsarRenderer.SSPRComponent));
        DragonExportStaticClass.Add(typeof(PulsarRenderer.PRPRenderConfigSetting));
    }

    public static void ExportURP()
    {
        Assembly prp = Assembly.Load("Unity.RenderPipelines.Universal.Runtime");
        foreach (var type in prp.GetExportedTypes())
        {
            if (type.IsSubclassOf(typeof(VolumeComponent)))
            {
                if (!DragonExportClass.Contains(type))
                {
                    DragonExportClass.Add(type);
                }
            }
        }

        DragonExportClass.Add(typeof(UnityEngine.Rendering.RenderPipelineAsset));
        DragonExportClass.Add(typeof(UnityEngine.Rendering.Volume));
        DragonExportClass.Add(typeof(UnityEngine.Rendering.VolumeComponent));
        DragonExportClass.Add(typeof(UnityEngine.Rendering.VolumeProfile));
        DragonExportClass.Add(typeof(UnityEngine.Rendering.ClampedFloatParameter));
        DragonExportClass.Add(typeof(UnityEngine.Rendering.IntParameter));
        DragonExportClass.Add(typeof(UnityEngine.Rendering.NoInterpIntParameter));
        DragonExportClass.Add(typeof(UnityEngine.Rendering.VolumeStack));
        DragonExportClass.Add(typeof(UnityEngine.Rendering.GraphicsSettings));
    }

#if USE_PERFORMANCE_PIPELINE
    public static void ExportPPP()
    {
        DragonExportStaticClass.Add(typeof(PulsarPerformance.PerformanceHelper));
    }
#endif

    public static string GetModuleNameForType(Type type)
    {
#if USE_GSDK
        if (type == typeof(GSDK.SystemShare.Link) || type == typeof(GSDK.SystemShare.Text)
        || type == typeof(GSDK.SystemShare.Video) || type == typeof(GSDK.SystemShare.Image))
        {
            return "GSDK.SystemShare";
        }
        if (type == typeof(GSDK.SaveImageShare.Image))
        {
            return "GSDK.SaveImageShare";
        }
#if IS_OVERSEA
        if (type == typeof(GSDK.FacebookShare.Link)
        || type == typeof(GSDK.FacebookShare.Video) || type == typeof(GSDK.FacebookShare.Image))
        {
            return "GSDK.FacebookShare";
        }
#if PLATFORM_TYPE_AND
        if (type == typeof(GSDK.TwitterShare.Link) || type == typeof(GSDK.TwitterShare.Text)
        || type == typeof(GSDK.TwitterShare.Video) || type == typeof(GSDK.TwitterShare.Image))
        {
            return "GSDK.TwitterShare";
        }
#endif
        if (type == typeof(GSDK.TiktokShare.Video))
        {
            return "GSDK.TiktokShare";
        }
#endif
#endif
        return type.Namespace;
    }
}

public class DragonExportMemberFilter : XPythonExportMemberFilter
{
    public static HashSet<string> ExportMemberBlackSet = new HashSet<string>
    {
        //可以填完整的类名+函数名，也可以只填函数名，如果只填函数名，那么只匹配函数名，不匹配类名
        "runInEditMode",
        "String.Chars",
        "IsNumeric",
        "Directory.SetAccessControl",
        "File.GetAccessControl",
        "File.SetAccessControl",
        "AkWwiseInitializationSettings.GetOrCreateAsset",
        "AkWwiseInitializationSettings.GetHashOfActiveSettings",
        "AkWwiseInitializationSettings.UpdatePlatforms",
        "WwiseAmbient.OnDrawGizmos",
        "WwiseAmbient.PrecomputeMeshSimplifyData",
        "WwiseAmbient.ComputeSimplifyMeshWithVertexCount",
        "WwiseAmbient.SaveSimplifyMeshData",
        "WwiseAmbient.CleanPrecomputeSimplifyMeshData",
        "WwiseAmbient.MeshFilter",
        "WwiseAmbient.OriginalMesh",
        "WwiseAmbient.OriginalMeshVertexCount",
        "WwiseAmbient.HasPrecomputeSimplifyMeshData",
        "WwiseBanksManifestAsset.GenerateAsset",
        "UIWwiseAnimStateEvent.IsStatesSync",
        "UIWwiseAnimStateEvent.SyncStates",
        "WwiseHelper.WriteAudioResourceUsageToManifest",
        "WwiseHelper.DeleteAudioResourceUsageManifest",
        "WwiseHelper.WwiseEditorAssetPath",
        "WwiseHelper.AudioResourceUsageManifestFullPath",
        "OpenWorldRVT.TerrainEditorMode",
        "OpenWorldRVT.m_EditorHeightData",
        "OpenWorldRVT.m_SandBoxRVTVolume",
        // UnityEngine
        "AnimationClip.averageDuration",
        "AnimationClip.averageAngularSpeed",
        "AnimationClip.averageSpeed",
        "AnimationClip.apparentSpeed",
        "AnimationClip.isLooping",
        "AnimationClip.isAnimatorMotion",
        "AnimationClip.isHumanMotion",
        "AnimatorOverrideController.PerformOverrideClipListCleanup",
        "AnimatorControllerParameter.name",
        "Caching.SetNoBackupFlag",
        "Caching.ResetNoBackupFlag",
        "Light.areaSize",
        "Light.lightmappingMode",
        "Light.lightmapBakeType",
        "Light.shadowAngle",
        "Light.shadowRadius",
        "Light.SetLightDirty",
        "Security.GetChainOfTrustValue",
        "Texture2D.alphaIsTransparency",
        "WWW.movie",
        "WWW.GetMovieTexture",
        "WebCamTexture.MarkNonReadable",
        "WebCamTexture.isReadable",
        "Graphic.OnRebuildRequested",
        "Text.OnRebuildRequested",
        "MultiLanguageText.OnRebuildRequested",
        "EmojiText.OnRebuildRequested",
        "RawImage.OnRebuildRequested",
        "MaskableGraphic.OnRebuildRequested",
        "Image.OnRebuildRequested",
        "SgrImage.OnRebuildRequested",
        "HyperLinkText.OnRebuildRequested",
        "TraceAreaDrawing.OnRebuildRequested",
        "Resources.LoadAssetAtPath",
        "Application.ExternalEval",
        "Handheld.SetActivityIndicatorStyle",
        "CanvasRenderer.OnRequestRebuild",
        "CanvasRenderer.onRequestRebuild",
        "Terrain.bakeLightProbesForTrees",
        "Terrain.deringLightProbesForTrees",
        "MonoBehaviour.runInEditMode",
        "Input.IsJoystickPreconfigured",
        "Input.location",
        "TextureFormat.DXT1Crunched",
        "TextureFormat.DXT5Crunched",
        "RenderTexture.imageContentsHash",
        "Texture.imageContentsHash",
        "Texture2D.imageContentsHash",
        "Texture2DArray.imageContentsHash",
        "QualitySettings.streamingMipmapsMaxLevelReduction",
        "QualitySettings.streamingMipmapsRenderersPerFrame",
        "EmptyRaycast.OnRebuildRequested",
        "Debug.ExtractStackTraceNoAlloc",
        "UITrigger.OnRebuildRequested",
        "UIPrimitiveBase.OnRebuildRequested",
        "UILineRenderer.OnRebuildRequested",
        "ParticleSystemRenderer.supportsMeshInstancing",
        "AssetSystem.StaticInstance",
        "AssetSystem.Add",
        "AssetSystem.ResourceLoadAnalyseSwitch",
        "AssetSystem.mReferenceOwnerList",
        "TextureAtlasManager.NewSubTexture",
        "GameObject.SetScenePickabilityState",
        "GameObject.SetSceneVisibilityState",
        "GameObject.SetSameSceneVisibilityStatesThan",
        "AtlasInformation.CreateInfo",
        "AtlasInformation.AddInfo",
        "AtlasInformation.SaveAssets",
        "CurlwDLL.curlw_multi_get_testinfo1",
        "CurlwDLL.curlw_multi_get_testinfo2",
        "LogicConfig.OnPause",
        "DownloadInterface.InitWinDownload",
        "DownloadInterface.UnityWinInit",
        "QualitySettings.skinSkipInterval",
        "PulsarRenderPipelineAsset.terrainBrushPassIndex",
        "GraphicsSettings.videoShadersIncludeMode",
        "RenderPipelineAsset.terrainBrushPassIndex",
        "PulsarRenderPipelineAsset.CreatePulsarPipelineAsset",
        "PulsarRenderPipelineAsset.CreatePulsarPipeline",
        "PulsarRenderPipelineAsset.CreateRenderConfig",
        "PulsarRenderView.ReloadConfig",
        "PulsarRenderView.IsConfigModify",
        "ParticleSystem.GetParticles",
        "ParticleSystem.SetParticles",
        "ParticleSystemRenderer.GetMeshes",
        "ParticleSystemRenderer.GetMeshWeightings",
        "LineRenderer.GetPositions",
        "AudioSource.PlayOnGamepad",
        "AudioSource.DisableGamepadOutput",
        "AudioSource.SetGamepadSpeakerMixLevel",
        "AudioSource.SetGamepadSpeakerMixLevelDefault",
        "AudioSource.SetGamepadSpeakerRestrictedAudio",
        "AudioSource.GamepadSpeakerSupportsOutputType",
        "AudioSource.gamepadSpeakerOutputType",
        "GraphicsSettings.enableAllShaderDebug",
        "MultiLanguageTMPText.OnRebuildRequested",
        "TextMeshPro.OnRebuildRequested",
        "TextMeshProUGUI.OnRebuildRequested",
        "TMP_Text.OnRebuildRequested",
        "TrailRenderer.GetPositions",
        "TrailRenderer.SetPositions",
        "TrailRenderer.AddPositions",
        "StartCensus",
        "RelatedShaderIndex",
        "SetUpMipTexture",
        "SetUpTable",
        "DrawMipmapLevelToTexture",
        "DrawMipmapResultForCensus",
        "DispatchComputeForCensus",
        "WriteToFile",
        "ChangeTextureMaxSize",
        "PRPRenderConfig.AddRenderFeature",
        "PRPRenderConfig.RemoveRenderFeature",
        "PRPRenderConfigSetting.SetFogTexture",
        "PRPRenderConfigSetting.SetDebugTexture",
        "Application.projectBranch",//暂时不知道哪来的属性，导致patch失败
        "UIManager._UIManagerFindAction",
        "DLCAssetCollecter.Part1Assets",
        "DLCAssetCollecter.Part2Assets",
        "TGRPInterface.Hades_Downloader_SetApplicationInfo",
        "TGRPInterface.Hades_Downloader_InitWinDownload",
        "TGRPInterface.Hades_Downloader_UnityWinInit",
    };

    public override bool CanExport(Type type, MemberInfo member)
    {
        return !ExportMemberBlackSet.Contains(member.Name) &&
               !ExportMemberBlackSet.Contains(type.Name + "." + member.Name);
    }
}

#if ENABLE_PYTHON_INJECTION
public class DragonInjectionMemberFilter : XPythonInjectFilter
{
    public override bool CanInject(Type type)
    {
        if (!type.Namespace.Contains("SgrUnity") && !type.Namespace.Contains("SgrEngine") && !type.Namespace.Contains("SgrProject") && !type.Namespace.Contains("XSolution"))
        {
            return false;
        }

        return true;
    }
    public override bool CanInject(MethodDefinition member)
    {
        if(member.Name == "InitFogData")
        {
            return false;
        }
        return true;
    }
}
#endif
