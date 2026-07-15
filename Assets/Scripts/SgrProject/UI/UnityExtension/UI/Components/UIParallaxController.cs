using System;
using System.Collections.Generic;
using UnityEngine;

namespace SgrUnity
{
    /// <summary>
    /// Adds a lightweight pseudo-3D parallax effect to a layered UGUI prefab.
    /// Attach once to the prefab root, then assign free RectTransform wrapper nodes as layers.
    /// Do not assign children that are directly controlled by a LayoutGroup or Animator.
    /// </summary>
    [AddComponentMenu("UI/Effects/UI Parallax Controller")]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    public sealed class UIParallaxController : MonoBehaviour
    {
        [Serializable]
        private sealed class ParallaxLayer
        {
            [Tooltip("仅用于在 Inspector 中标记这一层，例如 Background、Character、Foreground。")]
            public string label = "Layer";

            [Tooltip("建议指定一个不受 LayoutGroup 或 Animator 直接控制的 RectTransform 包装节点。")]
            [SerializeField] private RectTransform target;

            [Tooltip("0 表示不移动；1 表示完整位移；大于 1 更靠近镜头；负数会反向移动。")]
            [SerializeField, Range(-2f, 2f)] private float depth = 1f;

            [NonSerialized] internal Vector2 baseAnchoredPosition;
            [NonSerialized] internal bool hasBaseline;

            internal RectTransform Target => target;
            internal float Depth => depth;
        }

        [Header("Input / 输入")]
        [Tooltip("鼠标或触摸离开有效屏幕区域后，是否自动回到中心。")]
        [SerializeField] private bool returnToCenterOutsideScreen = true;

        [Tooltip("跟随鼠标或触摸时的平滑时间。数值越小，响应越快。")]
        [SerializeField, Min(0f)] private float followSmoothTime = 0.16f;

        [Tooltip("失去输入或窗口焦点后，回到中心的平滑时间。")]
        [SerializeField, Min(0f)] private float returnSmoothTime = 0.24f;

        [Tooltip("归一化输入每秒允许的最大变化速度。减小会更柔和，增大会更跟手。")]
        [SerializeField, Min(0.01f)] private float maxInputSpeed = 8f;

        [Tooltip("UI 动效通常应在 Time.timeScale 为 0 时继续播放。")]
        [SerializeField] private bool useUnscaledTime = true;

        [Header("Root Tilt / 根节点倾斜")]
        [Tooltip("负责整体倾斜的专用包装节点。为空时使用当前物体的 RectTransform。")]
        [SerializeField] private RectTransform tiltRoot;

        [Tooltip("鼠标到达屏幕上下边缘时的最大 X 轴倾斜角度。")]
        [SerializeField, Range(0f, 10f)] private float maxPitchDegrees = 2f;

        [Tooltip("鼠标到达屏幕左右边缘时的最大 Y 轴倾斜角度。")]
        [SerializeField, Range(0f, 10f)] private float maxYawDegrees = 3f;

        [Tooltip("倾斜到屏幕边缘时的最大缩放补偿。1 不缩放，1.02 表示最多放大 2%。")]
        [SerializeField, Min(1f)] private float edgeScale = 1.02f;

        [Header("Layer Parallax / 分层视差")]
        [Tooltip("深度为 1 的层，在屏幕边缘时产生的最大位移，单位为 Canvas 像素。")]
        [SerializeField] private Vector2 maxLayerOffset = new Vector2(28f, 18f);

        [Tooltip("开启后，各层向鼠标或触摸位置的反方向移动，更接近镜头视差。")]
        [SerializeField] private bool moveAgainstPointer = true;

        [Tooltip("按从远到近的顺序配置。建议：背景 0.15、人物 0.5、功能卡片 0.8、前景 1.2。")]
        [SerializeField] private List<ParallaxLayer> layers = new List<ParallaxLayer>();

        private Vector2 _currentInput;
        private Vector2 _inputVelocity;
        private Vector2 _externalInput;
        private Quaternion _baseRootRotation;
        private Vector3 _baseRootScale;
        private bool _externalInputEnabled;
        private bool _hasFocus = true;
        private bool _hasRootBaseline;

        private void Reset()
        {
            tiltRoot = transform as RectTransform;
        }

        private void OnEnable()
        {
            _hasFocus = true;
            CaptureBaseline();
        }

        private void OnDisable()
        {
            RestoreBaseline();
            _currentInput = Vector2.zero;
            _inputVelocity = Vector2.zero;
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            _hasFocus = hasFocus;
        }

        private void LateUpdate()
        {
            if (!_hasRootBaseline)
            {
                CaptureBaseline();
            }

            bool hasActiveInput;
            Vector2 targetInput = GetTargetInput(out hasActiveInput);
            float smoothTime = hasActiveInput ? followSmoothTime : returnSmoothTime;
            float deltaTime = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;

            _currentInput = SmoothToward(
                _currentInput,
                targetInput,
                ref _inputVelocity,
                smoothTime,
                maxInputSpeed,
                deltaTime);
            ApplyRootTilt();
            ApplyLayerOffsets();
        }

        /// <summary>
        /// Re-captures the prefab's current pose as the neutral center pose.
        /// Call this after runtime layout or content changes.
        /// </summary>
        public void RebuildBaseline()
        {
            RestoreBaseline();
            CaptureBaseline();
        }

        /// <summary>
        /// Overrides pointer input. Both axes are expected in the -1 to 1 range.
        /// This can be used later by a gyroscope, camera, or scripted preview.
        /// </summary>
        public void SetExternalInput(Vector2 normalizedInput)
        {
            _externalInput = ClampAxes(normalizedInput);
            _externalInputEnabled = true;
        }

        /// <summary>
        /// Returns control to mouse or touch input.
        /// </summary>
        public void ReleaseExternalInput()
        {
            _externalInputEnabled = false;
        }

        private void CaptureBaseline()
        {
            if (tiltRoot == null)
            {
                tiltRoot = transform as RectTransform;
            }

            if (tiltRoot != null)
            {
                _baseRootRotation = tiltRoot.localRotation;
                _baseRootScale = tiltRoot.localScale;
                _hasRootBaseline = true;
            }

            if (layers == null)
            {
                return;
            }

            for (int i = 0; i < layers.Count; i++)
            {
                ParallaxLayer layer = layers[i];
                if (layer == null || layer.Target == null)
                {
                    continue;
                }

                layer.baseAnchoredPosition = layer.Target.anchoredPosition;
                layer.hasBaseline = true;
            }
        }

        private void RestoreBaseline()
        {
            if (_hasRootBaseline && tiltRoot != null)
            {
                tiltRoot.localRotation = _baseRootRotation;
                tiltRoot.localScale = _baseRootScale;
            }

            if (layers == null)
            {
                _hasRootBaseline = false;
                return;
            }

            for (int i = 0; i < layers.Count; i++)
            {
                ParallaxLayer layer = layers[i];
                if (layer == null || !layer.hasBaseline || layer.Target == null)
                {
                    continue;
                }

                layer.Target.anchoredPosition = layer.baseAnchoredPosition;
                layer.hasBaseline = false;
            }

            _hasRootBaseline = false;
        }

        private Vector2 GetTargetInput(out bool hasActiveInput)
        {
            if (!_hasFocus)
            {
                hasActiveInput = false;
                return Vector2.zero;
            }

            if (_externalInputEnabled)
            {
                hasActiveInput = true;
                return _externalInput;
            }

            Vector2 pointerPosition;
            if (Input.touchCount > 0)
            {
                pointerPosition = Input.GetTouch(0).position;
            }
            else if (Input.mousePresent)
            {
                pointerPosition = Input.mousePosition;
            }
            else
            {
                hasActiveInput = false;
                return Vector2.zero;
            }

            if (Screen.width <= 0 || Screen.height <= 0)
            {
                hasActiveInput = false;
                return Vector2.zero;
            }

            bool isInsideScreen = pointerPosition.x >= 0f && pointerPosition.x <= Screen.width &&
                                  pointerPosition.y >= 0f && pointerPosition.y <= Screen.height;
            if (returnToCenterOutsideScreen && !isInsideScreen)
            {
                hasActiveInput = false;
                return Vector2.zero;
            }

            hasActiveInput = true;
            return ClampAxes(new Vector2(
                pointerPosition.x / Screen.width * 2f - 1f,
                pointerPosition.y / Screen.height * 2f - 1f));
        }

        private void ApplyRootTilt()
        {
            if (!_hasRootBaseline || tiltRoot == null)
            {
                return;
            }

            Quaternion tilt = Quaternion.Euler(
                -_currentInput.y * maxPitchDegrees,
                _currentInput.x * maxYawDegrees,
                0f);
            tiltRoot.localRotation = _baseRootRotation * tilt;

            float edgeAmount = Mathf.Max(Mathf.Abs(_currentInput.x), Mathf.Abs(_currentInput.y));
            float scale = Mathf.Lerp(1f, edgeScale, edgeAmount);
            tiltRoot.localScale = new Vector3(
                _baseRootScale.x * scale,
                _baseRootScale.y * scale,
                _baseRootScale.z);
        }

        private void ApplyLayerOffsets()
        {
            if (layers == null)
            {
                return;
            }

            Vector2 direction = moveAgainstPointer ? -_currentInput : _currentInput;
            Vector2 fullOffset = Vector2.Scale(direction, maxLayerOffset);

            for (int i = 0; i < layers.Count; i++)
            {
                ParallaxLayer layer = layers[i];
                if (layer == null || !layer.hasBaseline || layer.Target == null)
                {
                    continue;
                }

                layer.Target.anchoredPosition = layer.baseAnchoredPosition + fullOffset * layer.Depth;
            }
        }

        private static Vector2 SmoothToward(
            Vector2 current,
            Vector2 target,
            ref Vector2 velocity,
            float smoothTime,
            float maxSpeed,
            float deltaTime)
        {
            if (smoothTime <= 0f)
            {
                velocity = Vector2.zero;
                return target;
            }

            if (deltaTime <= 0f)
            {
                return current;
            }

            Vector2 result = Vector2.SmoothDamp(
                current,
                target,
                ref velocity,
                smoothTime,
                Mathf.Max(0.01f, maxSpeed),
                deltaTime);

            const float settleThreshold = 0.0001f;
            if ((result - target).sqrMagnitude <= settleThreshold * settleThreshold &&
                velocity.sqrMagnitude <= settleThreshold * settleThreshold)
            {
                result = target;
                velocity = Vector2.zero;
            }

            return result;
        }

        private static Vector2 ClampAxes(Vector2 value)
        {
            value.x = Mathf.Clamp(value.x, -1f, 1f);
            value.y = Mathf.Clamp(value.y, -1f, 1f);
            return value;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            followSmoothTime = Mathf.Max(0f, followSmoothTime);
            returnSmoothTime = Mathf.Max(0f, returnSmoothTime);
            maxInputSpeed = Mathf.Max(0.01f, maxInputSpeed);
            edgeScale = Mathf.Max(1f, edgeScale);
            maxLayerOffset.x = Mathf.Max(0f, maxLayerOffset.x);
            maxLayerOffset.y = Mathf.Max(0f, maxLayerOffset.y);
        }
#endif
    }
}
