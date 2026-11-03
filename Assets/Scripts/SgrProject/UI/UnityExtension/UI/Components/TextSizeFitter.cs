using UnityEngine;
using UnityEngine.EventSystems;
using TMPro;

namespace UnityEngine.UI
{

    [ExecuteAlways]
    [RequireComponent(typeof(RectTransform))]
    public class TextSizeFitter : UIBehaviour, ILayoutSelfController, ILayoutElement
    {
        public enum FitMode
        {
            FixedSize,
            AutoSize,
        }

        [Header("水平模式")]
        public FitMode HorizontalFit = FitMode.FixedSize;
        [Header("垂直模式")]
        public FitMode VerticalFit = FitMode.FixedSize;

        [Header("水平额外值")]
        public float AddSizeHorizontal;
        [Header("垂直额外值")]
        public float AddSizeVertical;

        [Header("水平固定值")]
        public float FixedSizeHorizontal = 100;
        [Header("垂直固定值")]
        public float FixedSizeVertical = 100;

        private string _LastText = null;
        private Vector2 _lastTextRectSize;
        private bool _hasTextSnapshot;
        private bool _layoutDirty = true;
        private bool _isRefreshingTextGeometry;


        [Header("绑定的Text")]
        public TMPro.TextMeshProUGUI _LanguageText;

        private RectTransform m_Rect;

        private RectTransform rectTransform
        {
            get
            {
                if (m_Rect == null)
                    m_Rect = GetComponent<RectTransform>();
                return m_Rect;
            }
        }


        protected TextSizeFitter()
        { }

        protected override void OnEnable()
        {
            base.OnEnable();
            TMPro_EventManager.TEXT_CHANGED_EVENT.Add(OnTextChanged);
            RequestLayoutUpdate();
        }

        protected override void OnDisable()
        {
            TMPro_EventManager.TEXT_CHANGED_EVENT.Remove(OnTextChanged);
            LayoutRebuilder.MarkLayoutForRebuild(rectTransform);
            base.OnDisable();
        }

        private void OnTextChanged(Object changedObject)
        {
            if (changedObject == _LanguageText && !_isRefreshingTextGeometry)
            {
                RequestLayoutUpdate();
            }
        }

        protected override void OnRectTransformDimensionsChange()
        {
            base.OnRectTransformDimensionsChange();
            SetLayoutHorizontal();
            SetLayoutVertical();
        }
        private void LateUpdate()
        {
            if (_LanguageText == null)
            {
                _hasTextSnapshot = false;
                return;
            }

            Vector2 textRectSize = _LanguageText.rectTransform.rect.size;
            if (!_hasTextSnapshot ||
                _LastText != _LanguageText.text ||
                !Approximately(_lastTextRectSize, textRectSize) ||
                _LanguageText.havePropertiesChanged)
            {
                _layoutDirty = true;
            }

            if (_layoutDirty)
            {
                EnsureTextGeometryIsCurrent();
                SetLayoutHorizontal();
                SetLayoutVertical();
            }
        }

        private static bool Approximately(Vector2 lhs, Vector2 rhs)
        {
            return Mathf.Approximately(lhs.x, rhs.x) && Mathf.Approximately(lhs.y, rhs.y);
        }

        private void RequestLayoutUpdate()
        {
            _layoutDirty = true;
            _hasTextSnapshot = false;
            SetDirty();
        }

        private void EnsureTextGeometryIsCurrent()
        {
            if (_LanguageText == null || (!_layoutDirty && !_LanguageText.havePropertiesChanged))
            {
                return;
            }

            // TMP 的 preferredWidth / preferredHeight 不反映最终 Auto Size 字号；先刷新网格再读取真实尺寸。
            _isRefreshingTextGeometry = true;
            try
            {
                _LanguageText.ForceMeshUpdate();
            }
            finally
            {
                _isRefreshingTextGeometry = false;
            }
            _layoutDirty = false;
            _LastText = _LanguageText.text;
            _lastTextRectSize = _LanguageText.rectTransform.rect.size;
            _hasTextSnapshot = true;
        }

        private float GetRenderedTextWidth()
        {
            EnsureTextGeometryIsCurrent();

            TMP_TextInfo textInfo = _LanguageText.textInfo;
            if (textInfo == null || textInfo.lineCount == 0)
            {
                return 0;
            }

            // 始终使用当前字号下的真实行宽，兼容单行 Auto Size 缩字和多行自动换行。
            float maxLineWidth = 0;
            for (int i = 0; i < textInfo.lineCount; i++)
            {
                TMP_LineInfo lineInfo = textInfo.lineInfo[i];
                float lineWidth = lineInfo.lineExtents.max.x - lineInfo.lineExtents.min.x;
                if (!float.IsNaN(lineWidth) && !float.IsInfinity(lineWidth) && lineWidth > maxLineWidth)
                {
                    maxLineWidth = lineWidth;
                }
            }

            return maxLineWidth;
        }

        private float GetRenderedTextHeight()
        {
            EnsureTextGeometryIsCurrent();

            TMP_TextInfo textInfo = _LanguageText.textInfo;
            if (textInfo == null || textInfo.characterCount == 0)
            {
                return 0;
            }

            float renderedHeight = _LanguageText.GetRenderedValues(false).y;
            if (float.IsNaN(renderedHeight) || float.IsInfinity(renderedHeight))
            {
                return 0;
            }

            return Mathf.Max(0, renderedHeight);
        }

        private void HandleSelfFittingAlongAxis(int axis)
        {
            if (_LanguageText == null || !IsActive())
            {
                return;
            }
            FitMode fitting = (axis == 0 ? HorizontalFit : VerticalFit);
            if (fitting == FitMode.FixedSize)
            {
                return;
            }

            if (fitting == FitMode.AutoSize)
            {
                if (axis == 0)
                {
                    rectTransform.SetSizeWithCurrentAnchors((RectTransform.Axis)axis,
                        GetRenderedTextWidth() + AddSizeHorizontal);
                }
                else
                {
                    rectTransform.SetSizeWithCurrentAnchors((RectTransform.Axis)axis,
                        GetRenderedTextHeight() + AddSizeVertical);
                }
            }
        }

        /// <summary>
        /// Calculate and apply the horizontal component of the size to the RectTransform
        /// </summary>
        public virtual void SetLayoutHorizontal()
        {
            HandleSelfFittingAlongAxis(0);
        }

        /// <summary>
        /// Calculate and apply the vertical component of the size to the RectTransform
        /// </summary>
        public virtual void SetLayoutVertical()
        {
            HandleSelfFittingAlongAxis(1);
        }

        protected void SetDirty()
        {
            if (!IsActive())
                return;

            LayoutRebuilder.MarkLayoutForRebuild(rectTransform);
        }

#if UNITY_EDITOR
        protected override void OnValidate()
        {
            RequestLayoutUpdate();
        }

#endif

        public virtual float flexibleHeight { get { return -1; } }

        public virtual int layoutPriority { get { return 0; } }

        public virtual float preferredHeight
        {
            get
            {
                if (VerticalFit == FitMode.FixedSize || _LanguageText == null)
                {
                    return FixedSizeVertical;
                }


                return GetRenderedTextHeight() + AddSizeVertical;
            }
        }

        public virtual void CalculateLayoutInputHorizontal() { }
        public virtual void CalculateLayoutInputVertical() { }

        public virtual float minWidth
        {
            get { return 0; }
        }

        public virtual float preferredWidth
        {
            get
            {
                if (HorizontalFit == FitMode.FixedSize || _LanguageText == null)
                {
                    return FixedSizeHorizontal;
                }


                return GetRenderedTextWidth() + AddSizeHorizontal;
            }
        }

        public virtual float flexibleWidth { get { return -1; } }

        public virtual float minHeight
        {
            get { return 0; }
        }
    }
}
