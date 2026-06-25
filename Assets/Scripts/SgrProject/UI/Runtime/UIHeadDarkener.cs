using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UITest;

namespace SgrUnity
{
    [DisallowMultipleComponent]
    public class UIHeadDarkener : MonoBehaviour
    {
        private static readonly int TintColorId = Shader.PropertyToID("_TintColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int FaceColorId = Shader.PropertyToID("_FaceColor");
        private static readonly int GrayStrengthId = Shader.PropertyToID("_GrayStrength");
        private const string GrayKeyword = "_IS_GRAY";

        [SerializeField] private bool isDark;
        [SerializeField] [Range(0f, 1f)] private float darkFactor = 0.35f;
        [SerializeField] [Range(0f, 1f)] private float grayStrength = 0.6f;
        [SerializeField] private bool useMaterialFallback = true;
        [SerializeField] private bool autoRefreshOnChildrenChanged = true;

        private readonly Dictionary<Graphic, GraphicState> graphicStates = new Dictionary<Graphic, GraphicState>();
        private readonly Dictionary<UIShaderParamHelper_vx_common_shader, ShaderHelperState> helperStates =
            new Dictionary<UIShaderParamHelper_vx_common_shader, ShaderHelperState>();

        private bool refreshPending;

        public bool IsDark
        {
            get { return isDark; }
        }

        public float DarkFactor
        {
            get { return darkFactor; }
            set
            {
                darkFactor = Mathf.Clamp01(value);
                if (isDark)
                {
                    ApplyDarkState();
                }
            }
        }

        public float GrayStrength
        {
            get { return grayStrength; }
            set
            {
                grayStrength = Mathf.Clamp01(value);
                if (isDark)
                {
                    ApplyDarkState();
                }
            }
        }

        public bool UseMaterialFallback
        {
            get { return useMaterialFallback; }
            set
            {
                useMaterialFallback = value;
                if (isDark)
                {
                    ApplyDarkState();
                }
            }
        }

        public void SetDark(bool dark)
        {
            SetDark(dark, darkFactor, grayStrength);
        }

        public void SetDark(bool dark, float factor)
        {
            SetDark(dark, factor, grayStrength);
        }

        public void SetDark(bool dark, float factor, float gray)
        {
            darkFactor = Mathf.Clamp01(factor);
            grayStrength = Mathf.Clamp01(gray);
            isDark = dark;

            if (isDark)
            {
                ApplyDarkState();
            }
            else
            {
                RestoreAll();
            }
        }

        public void Refresh()
        {
            if (isDark)
            {
                ApplyDarkState();
            }
            else
            {
                ClearInvalidStates();
            }
        }

        private void OnEnable()
        {
            if (isDark)
            {
                ApplyDarkState();
            }
        }

        private void OnDisable()
        {
            RestoreAll();
        }

        private void OnDestroy()
        {
            RestoreAll();
        }

        private void OnTransformChildrenChanged()
        {
            if (autoRefreshOnChildrenChanged && isDark)
            {
                refreshPending = true;
            }
        }

        private void LateUpdate()
        {
            if (!refreshPending)
            {
                return;
            }

            refreshPending = false;
            Refresh();
        }

        private void ApplyDarkState()
        {
            ClearInvalidStates();
            ApplyShaderHelpers();
            ApplyGraphics();
        }

        private void ApplyShaderHelpers()
        {
            var helpers = GetComponentsInChildren<UIShaderParamHelper_vx_common_shader>(true);
            for (int i = 0; i < helpers.Length; i++)
            {
                var helper = helpers[i];
                if (helper == null)
                {
                    continue;
                }

                ShaderHelperState state;
                if (!helperStates.TryGetValue(helper, out state))
                {
                    state = new ShaderHelperState(helper.TintColor, helper.GrayStrength, helper.IsGray);
                    helperStates.Add(helper, state);
                }

                helper.TintColor = MultiplyRgb(state.TintColor, darkFactor);
                helper.GrayStrength = grayStrength;
                helper.IsGray = grayStrength > 0f;
            }
        }

        private void ApplyGraphics()
        {
            var graphics = GetComponentsInChildren<Graphic>(true);
            for (int i = 0; i < graphics.Length; i++)
            {
                var graphic = graphics[i];
                if (graphic == null)
                {
                    continue;
                }

                if (graphic.GetComponent<UIShaderParamHelper_vx_common_shader>() != null)
                {
                    continue;
                }

                GraphicState state = GetOrCreateGraphicState(graphic);
                bool materialApplied = useMaterialFallback && TryApplyMaterialFallback(graphic, state);

                state.UsesGraphicColor = !materialApplied;
                if (state.UsesGraphicColor)
                {
                    RestoreRuntimeMaterial(graphic, state);
                    graphic.color = MultiplyRgb(state.Color, darkFactor);
                }
            }
        }

        private GraphicState GetOrCreateGraphicState(Graphic graphic)
        {
            GraphicState state;
            if (!graphicStates.TryGetValue(graphic, out state))
            {
                state = new GraphicState(graphic.color, graphic.material);
                graphicStates.Add(graphic, state);
                return state;
            }

            if (state.RuntimeMaterial != null && graphic.material != state.RuntimeMaterial)
            {
                DestroyMaterial(state.RuntimeMaterial);
                state.RuntimeMaterial = null;
                state.Material = graphic.material;
                state.Color = graphic.color;
            }

            return state;
        }

        private bool TryApplyMaterialFallback(Graphic graphic, GraphicState state)
        {
            Material sourceMaterial = state.Material;
            if (sourceMaterial == null || sourceMaterial == Graphic.defaultGraphicMaterial)
            {
                return false;
            }

            bool hasColorProperty = HasColorProperty(sourceMaterial);
            bool hasGrayProperty = sourceMaterial.HasProperty(GrayStrengthId);
            if (!hasColorProperty && !hasGrayProperty)
            {
                return false;
            }

            if (state.RuntimeMaterial == null)
            {
                state.RuntimeMaterial = new Material(sourceMaterial);
                state.RuntimeMaterial.name = sourceMaterial.name + " (UIHeadDarkener)";
            }

            CopyMaterialDarkParams(sourceMaterial, state.RuntimeMaterial, hasColorProperty, hasGrayProperty);
            graphic.material = state.RuntimeMaterial;
            graphic.SetMaterialDirty();
            return hasColorProperty;
        }

        private void RestoreRuntimeMaterial(Graphic graphic, GraphicState state)
        {
            if (state.RuntimeMaterial == null)
            {
                return;
            }

            graphic.material = state.Material;
            graphic.SetMaterialDirty();
            DestroyMaterial(state.RuntimeMaterial);
            state.RuntimeMaterial = null;
        }

        private void CopyMaterialDarkParams(Material source, Material target, bool hasColorProperty, bool hasGrayProperty)
        {
            if (hasColorProperty)
            {
                if (source.HasProperty(TintColorId))
                {
                    target.SetColor(TintColorId, MultiplyRgb(source.GetColor(TintColorId), darkFactor));
                }

                if (source.HasProperty(ColorId))
                {
                    target.SetColor(ColorId, MultiplyRgb(source.GetColor(ColorId), darkFactor));
                }

                if (source.HasProperty(FaceColorId))
                {
                    target.SetColor(FaceColorId, MultiplyRgb(source.GetColor(FaceColorId), darkFactor));
                }
            }

            if (hasGrayProperty)
            {
                target.SetFloat(GrayStrengthId, grayStrength);

                if (grayStrength > 0f)
                {
                    target.EnableKeyword(GrayKeyword);
                }
                else
                {
                    target.DisableKeyword(GrayKeyword);
                }
            }
        }

        private static bool HasColorProperty(Material material)
        {
            return material.HasProperty(TintColorId) || material.HasProperty(ColorId) || material.HasProperty(FaceColorId);
        }

        private static Color MultiplyRgb(Color color, float factor)
        {
            color.r *= factor;
            color.g *= factor;
            color.b *= factor;
            return color;
        }

        private void RestoreAll()
        {
            foreach (var pair in helperStates)
            {
                var helper = pair.Key;
                if (helper == null)
                {
                    continue;
                }

                var state = pair.Value;
                helper.TintColor = state.TintColor;
                helper.GrayStrength = state.GrayStrength;
                helper.IsGray = state.IsGray;
            }
            helperStates.Clear();

            foreach (var pair in graphicStates)
            {
                var graphic = pair.Key;
                var state = pair.Value;

                if (graphic != null)
                {
                    if (state.UsesGraphicColor)
                    {
                        graphic.color = state.Color;
                    }

                    if (state.RuntimeMaterial != null)
                    {
                        graphic.material = state.Material;
                        graphic.SetMaterialDirty();
                    }
                }

                if (state.RuntimeMaterial != null)
                {
                    DestroyMaterial(state.RuntimeMaterial);
                    state.RuntimeMaterial = null;
                }
            }
            graphicStates.Clear();
        }

        private void ClearInvalidStates()
        {
            RemoveInvalidHelpers();
            RemoveInvalidGraphics();
        }

        private void RemoveInvalidHelpers()
        {
            if (helperStates.Count == 0)
            {
                return;
            }

            var invalid = new List<UIShaderParamHelper_vx_common_shader>();
            foreach (var pair in helperStates)
            {
                if (pair.Key == null)
                {
                    invalid.Add(pair.Key);
                }
            }

            for (int i = 0; i < invalid.Count; i++)
            {
                helperStates.Remove(invalid[i]);
            }
        }

        private void RemoveInvalidGraphics()
        {
            if (graphicStates.Count == 0)
            {
                return;
            }

            var invalid = new List<Graphic>();
            foreach (var pair in graphicStates)
            {
                if (pair.Key == null)
                {
                    if (pair.Value.RuntimeMaterial != null)
                    {
                        DestroyMaterial(pair.Value.RuntimeMaterial);
                    }
                    invalid.Add(pair.Key);
                }
            }

            for (int i = 0; i < invalid.Count; i++)
            {
                graphicStates.Remove(invalid[i]);
            }
        }

        private static void DestroyMaterial(Material material)
        {
            if (material == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(material);
            }
            else
            {
                DestroyImmediate(material);
            }
        }

        private class GraphicState
        {
            public Color Color;
            public Material Material;
            public Material RuntimeMaterial;
            public bool UsesGraphicColor;

            public GraphicState(Color color, Material material)
            {
                Color = color;
                Material = material;
            }
        }

        private struct ShaderHelperState
        {
            public Color TintColor;
            public float GrayStrength;
            public bool IsGray;

            public ShaderHelperState(Color tintColor, float grayStrength, bool isGray)
            {
                TintColor = tintColor;
                GrayStrength = grayStrength;
                IsGray = isGray;
            }
        }
    }

    public static class UIHeadDarkenerUtility
    {
        public static UIHeadDarkener SetDark(GameObject root, bool dark)
        {
            return SetDark(root, dark, 0.35f, 0.6f);
        }

        public static UIHeadDarkener SetDark(GameObject root, bool dark, float darkFactor)
        {
            return SetDark(root, dark, darkFactor, 0.6f);
        }

        public static UIHeadDarkener SetDark(GameObject root, bool dark, float darkFactor, float grayStrength)
        {
            if (root == null)
            {
                return null;
            }

            var darkener = root.GetComponent<UIHeadDarkener>();
            if (darkener == null)
            {
                darkener = root.AddComponent<UIHeadDarkener>();
            }

            darkener.SetDark(dark, darkFactor, grayStrength);
            return darkener;
        }

        public static void Refresh(GameObject root)
        {
            if (root == null)
            {
                return;
            }

            var darkener = root.GetComponent<UIHeadDarkener>();
            if (darkener != null)
            {
                darkener.Refresh();
            }
        }
    }
}
