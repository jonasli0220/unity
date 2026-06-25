using System.Collections.Generic;
using Coffee.UIExtensions;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using UITest;

namespace SgrUnity
{
    [DisallowMultipleComponent]
    public class UIHeadDarkener : MonoBehaviour
    {
        private static readonly int TintColorId = Shader.PropertyToID("_TintColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int MainColorId = Shader.PropertyToID("_MainColor");
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
        private readonly Dictionary<ParticleSystem, ParticleState> particleStates =
            new Dictionary<ParticleSystem, ParticleState>();
        private readonly Dictionary<ParticleSystemRenderer, ParticleRendererState> particleRendererStates =
            new Dictionary<ParticleSystemRenderer, ParticleRendererState>();

        private ParticleSystem.Particle[] particleBuffer = new ParticleSystem.Particle[128];
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

#if UNITY_EDITOR
        private void OnValidate()
        {
            darkFactor = Mathf.Clamp01(darkFactor);
            grayStrength = Mathf.Clamp01(grayStrength);

            if (!Application.isPlaying || !isActiveAndEnabled)
            {
                return;
            }

            if (isDark)
            {
                ApplyDarkState();
            }
            else
            {
                RestoreAll();
            }
        }
#endif

        [ContextMenu("Apply Dark Now")]
        private void ApplyDarkNow()
        {
            SetDark(true, darkFactor, grayStrength);
        }

        [ContextMenu("Restore Now")]
        private void RestoreNow()
        {
            SetDark(false, darkFactor, grayStrength);
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
            ApplyParticles();
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

        private void ApplyParticles()
        {
            var particles = GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < particles.Length; i++)
            {
                var particle = particles[i];
                if (particle == null)
                {
                    continue;
                }

                ParticleState state;
                if (!particleStates.TryGetValue(particle, out state))
                {
                    state = new ParticleState(particle.main.startColor);
                    particleStates.Add(particle, state);
                }

                var main = particle.main;
                main.startColor = MultiplyGradientRgb(state.StartColor, darkFactor);
                ApplyLiveParticleFactor(particle, state, darkFactor);

                var renderer = particle.GetComponent<ParticleSystemRenderer>();
                if (!useMaterialFallback)
                {
                    RestoreParticleRendererIfTracked(renderer);
                    continue;
                }

                if (renderer != null && TryApplyParticleRendererMaterialFallback(renderer))
                {
                    MarkUIParticleDirty(particle);
                }
            }
        }

        private void RestoreParticleRendererIfTracked(ParticleSystemRenderer renderer)
        {
            if (renderer == null)
            {
                return;
            }

            ParticleRendererState state;
            if (!particleRendererStates.TryGetValue(renderer, out state))
            {
                return;
            }

            RestoreParticleRendererMaterials(renderer, state);
            particleRendererStates.Remove(renderer);
        }

        private bool TryApplyParticleRendererMaterialFallback(ParticleSystemRenderer renderer)
        {
            ParticleRendererState state = GetOrCreateParticleRendererState(renderer);
            bool applied = false;

            Material[] runtimeMaterials = state.RuntimeMaterials;
            if (runtimeMaterials == null || runtimeMaterials.Length != state.Materials.Length)
            {
                DestroyRuntimeMaterials(runtimeMaterials);
                runtimeMaterials = new Material[state.Materials.Length];
                state.RuntimeMaterials = runtimeMaterials;
            }

            Material[] nextMaterials = null;
            for (int i = 0; i < state.Materials.Length; i++)
            {
                Material runtimeMaterial = runtimeMaterials[i];
                if (TryCreateOrUpdateRuntimeMaterial(state.Materials[i], ref runtimeMaterial))
                {
                    runtimeMaterials[i] = runtimeMaterial;
                    if (nextMaterials == null)
                    {
                        nextMaterials = (Material[])state.Materials.Clone();
                    }

                    nextMaterials[i] = runtimeMaterial;
                    applied = true;
                }
            }

            if (nextMaterials != null)
            {
                renderer.sharedMaterials = nextMaterials;
            }

            Material runtimeTrailMaterial = state.RuntimeTrailMaterial;
            if (TryCreateOrUpdateRuntimeMaterial(state.TrailMaterial, ref runtimeTrailMaterial))
            {
                state.RuntimeTrailMaterial = runtimeTrailMaterial;
                renderer.trailMaterial = runtimeTrailMaterial;
                applied = true;
            }

            return applied;
        }

        private ParticleRendererState GetOrCreateParticleRendererState(ParticleSystemRenderer renderer)
        {
            ParticleRendererState state;
            if (!particleRendererStates.TryGetValue(renderer, out state))
            {
                state = new ParticleRendererState(renderer.sharedMaterials, renderer.trailMaterial);
                particleRendererStates.Add(renderer, state);
                return state;
            }

            if (HasParticleRendererMaterialChanged(renderer, state))
            {
                DestroyRuntimeMaterials(state.RuntimeMaterials);
                DestroyMaterial(state.RuntimeTrailMaterial);
                state.Materials = renderer.sharedMaterials;
                state.TrailMaterial = renderer.trailMaterial;
                state.RuntimeMaterials = null;
                state.RuntimeTrailMaterial = null;
            }

            return state;
        }

        private bool HasParticleRendererMaterialChanged(ParticleSystemRenderer renderer, ParticleRendererState state)
        {
            Material[] currentMaterials = renderer.sharedMaterials;
            if (currentMaterials.Length != state.Materials.Length)
            {
                return true;
            }

            for (int i = 0; i < currentMaterials.Length; i++)
            {
                Material runtimeMaterial = null;
                if (state.RuntimeMaterials != null && i < state.RuntimeMaterials.Length)
                {
                    runtimeMaterial = state.RuntimeMaterials[i];
                }

                if (currentMaterials[i] != state.Materials[i] && currentMaterials[i] != runtimeMaterial)
                {
                    return true;
                }
            }

            if (renderer.trailMaterial != state.TrailMaterial && renderer.trailMaterial != state.RuntimeTrailMaterial)
            {
                return true;
            }

            return false;
        }

        private bool TryCreateOrUpdateRuntimeMaterial(Material source, ref Material runtimeMaterial)
        {
            if (source == null)
            {
                return false;
            }

            bool hasColorProperty = HasColorProperty(source);
            bool hasGrayProperty = source.HasProperty(GrayStrengthId);
            if (!hasColorProperty && !hasGrayProperty)
            {
                return false;
            }

            if (runtimeMaterial == null)
            {
                runtimeMaterial = new Material(source);
                runtimeMaterial.name = source.name + " (UIHeadDarkener)";
            }

            CopyMaterialDarkParams(source, runtimeMaterial, hasColorProperty, hasGrayProperty);
            return true;
        }

        private void ApplyLiveParticleFactor(ParticleSystem particle, ParticleState state, float factor)
        {
            float targetFactor = Mathf.Max(factor, 0.001f);
            if (state.LiveFactorApplied && Mathf.Approximately(state.LiveFactor, targetFactor))
            {
                return;
            }

            float multiplier = targetFactor;
            if (state.LiveFactorApplied && state.LiveFactor > 0.001f)
            {
                multiplier = targetFactor / state.LiveFactor;
            }

            TintLiveParticles(particle, multiplier);
            state.LiveFactor = targetFactor;
            state.LiveFactorApplied = true;
        }

        private void RestoreLiveParticleFactor(ParticleSystem particle, ParticleState state)
        {
            if (!state.LiveFactorApplied || state.LiveFactor <= 0.001f)
            {
                state.LiveFactorApplied = false;
                state.LiveFactor = 1f;
                return;
            }

            TintLiveParticles(particle, 1f / state.LiveFactor);
            state.LiveFactorApplied = false;
            state.LiveFactor = 1f;
        }

        private void TintLiveParticles(ParticleSystem particle, float multiplier)
        {
            int particleCount = particle.particleCount;
            if (particleCount <= 0)
            {
                return;
            }

            EnsureParticleBuffer(particleCount);
            int readCount = particle.GetParticles(particleBuffer);
            for (int i = 0; i < readCount; i++)
            {
                particleBuffer[i].startColor = MultiplyRgb(particleBuffer[i].startColor, multiplier);
            }

            particle.SetParticles(particleBuffer, readCount);
        }

        private void EnsureParticleBuffer(int particleCount)
        {
            if (particleBuffer.Length >= particleCount)
            {
                return;
            }

            int nextLength = particleBuffer.Length;
            while (nextLength < particleCount)
            {
                nextLength *= 2;
            }

            particleBuffer = new ParticleSystem.Particle[nextLength];
        }

        private void MarkUIParticleDirty(ParticleSystem particle)
        {
            var uiParticle = particle.GetComponentInParent<UIParticle>();
            if (uiParticle != null)
            {
                uiParticle.SetMaterialDirty();
            }
        }

        private void CopyMaterialDarkParams(Material source, Material target, bool hasColorProperty, bool hasGrayProperty)
        {
            if (hasColorProperty)
            {
                CopyMaterialColorParam(source, target, TintColorId);
                CopyMaterialColorParam(source, target, ColorId);
                CopyMaterialColorParam(source, target, BaseColorId);
                CopyMaterialColorParam(source, target, MainColorId);
                CopyMaterialColorParam(source, target, FaceColorId);
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
            return HasColorProperty(material, TintColorId)
                || HasColorProperty(material, ColorId)
                || HasColorProperty(material, BaseColorId)
                || HasColorProperty(material, MainColorId)
                || HasColorProperty(material, FaceColorId);
        }

        private void CopyMaterialColorParam(Material source, Material target, int propertyId)
        {
            if (HasColorProperty(source, propertyId))
            {
                target.SetColor(propertyId, MultiplyRgb(source.GetColor(propertyId), darkFactor));
            }
        }

        private static bool HasColorProperty(Material material, int propertyId)
        {
            return material.HasProperty(propertyId) && IsColorProperty(material.shader, propertyId);
        }

        private static bool IsColorProperty(Shader shader, int propertyId)
        {
            if (shader == null)
            {
                return false;
            }

            int propertyCount = shader.GetPropertyCount();
            for (int i = 0; i < propertyCount; i++)
            {
                if (shader.GetPropertyNameId(i) == propertyId)
                {
                    return shader.GetPropertyType(i) == ShaderPropertyType.Color;
                }
            }

            return false;
        }

        private static Color MultiplyRgb(Color color, float factor)
        {
            color.r *= factor;
            color.g *= factor;
            color.b *= factor;
            return color;
        }

        private static Color32 MultiplyRgb(Color32 color, float factor)
        {
            color.r = ScaleByte(color.r, factor);
            color.g = ScaleByte(color.g, factor);
            color.b = ScaleByte(color.b, factor);
            return color;
        }

        private static byte ScaleByte(byte value, float factor)
        {
            return (byte)Mathf.Clamp(Mathf.RoundToInt(value * factor), 0, 255);
        }

        private static ParticleSystem.MinMaxGradient MultiplyGradientRgb(ParticleSystem.MinMaxGradient source, float factor)
        {
            switch (source.mode)
            {
                case ParticleSystemGradientMode.Color:
                    return new ParticleSystem.MinMaxGradient(MultiplyRgb(source.color, factor));

                case ParticleSystemGradientMode.TwoColors:
                    return new ParticleSystem.MinMaxGradient(
                        MultiplyRgb(source.colorMin, factor),
                        MultiplyRgb(source.colorMax, factor));

                case ParticleSystemGradientMode.Gradient:
                    return new ParticleSystem.MinMaxGradient(MultiplyGradientRgb(source.gradient, factor));

                case ParticleSystemGradientMode.TwoGradients:
                    return new ParticleSystem.MinMaxGradient(
                        MultiplyGradientRgb(source.gradientMin, factor),
                        MultiplyGradientRgb(source.gradientMax, factor));

                case ParticleSystemGradientMode.RandomColor:
                    var randomColor = new ParticleSystem.MinMaxGradient(MultiplyGradientRgb(source.gradient, factor));
                    randomColor.mode = ParticleSystemGradientMode.RandomColor;
                    return randomColor;

                default:
                    return source;
            }
        }

        private static Gradient MultiplyGradientRgb(Gradient source, float factor)
        {
            if (source == null)
            {
                return null;
            }

            var gradient = new Gradient();
            GradientColorKey[] colorKeys = source.colorKeys;
            for (int i = 0; i < colorKeys.Length; i++)
            {
                colorKeys[i].color = MultiplyRgb(colorKeys[i].color, factor);
            }

            gradient.SetKeys(colorKeys, source.alphaKeys);
            gradient.mode = source.mode;
            return gradient;
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

            foreach (var pair in particleStates)
            {
                var particle = pair.Key;
                if (particle == null)
                {
                    continue;
                }

                var state = pair.Value;
                RestoreLiveParticleFactor(particle, state);
                var main = particle.main;
                main.startColor = state.StartColor;
            }
            particleStates.Clear();

            foreach (var pair in particleRendererStates)
            {
                RestoreParticleRendererMaterials(pair.Key, pair.Value);
            }
            particleRendererStates.Clear();
        }

        private void RestoreParticleRendererMaterials(ParticleSystemRenderer renderer, ParticleRendererState state)
        {
            if (renderer != null)
            {
                if (state.RuntimeMaterials != null)
                {
                    renderer.sharedMaterials = state.Materials;
                }

                if (state.RuntimeTrailMaterial != null)
                {
                    renderer.trailMaterial = state.TrailMaterial;
                }

                var particle = renderer.GetComponent<ParticleSystem>();
                if (particle != null)
                {
                    MarkUIParticleDirty(particle);
                }
            }

            DestroyRuntimeMaterials(state.RuntimeMaterials);
            state.RuntimeMaterials = null;

            if (state.RuntimeTrailMaterial != null)
            {
                DestroyMaterial(state.RuntimeTrailMaterial);
                state.RuntimeTrailMaterial = null;
            }
        }

        private void DestroyRuntimeMaterials(Material[] materials)
        {
            if (materials == null)
            {
                return;
            }

            for (int i = 0; i < materials.Length; i++)
            {
                if (materials[i] != null)
                {
                    DestroyMaterial(materials[i]);
                    materials[i] = null;
                }
            }
        }

        private void ClearInvalidStates()
        {
            RemoveInvalidHelpers();
            RemoveInvalidGraphics();
            RemoveInvalidParticles();
            RemoveInvalidParticleRenderers();
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

        private void RemoveInvalidParticles()
        {
            if (particleStates.Count == 0)
            {
                return;
            }

            var invalid = new List<ParticleSystem>();
            foreach (var pair in particleStates)
            {
                if (pair.Key == null)
                {
                    invalid.Add(pair.Key);
                }
            }

            for (int i = 0; i < invalid.Count; i++)
            {
                particleStates.Remove(invalid[i]);
            }
        }

        private void RemoveInvalidParticleRenderers()
        {
            if (particleRendererStates.Count == 0)
            {
                return;
            }

            var invalid = new List<ParticleSystemRenderer>();
            foreach (var pair in particleRendererStates)
            {
                if (pair.Key == null)
                {
                    DestroyRuntimeMaterials(pair.Value.RuntimeMaterials);
                    DestroyMaterial(pair.Value.RuntimeTrailMaterial);
                    invalid.Add(pair.Key);
                }
            }

            for (int i = 0; i < invalid.Count; i++)
            {
                particleRendererStates.Remove(invalid[i]);
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

        private class ParticleState
        {
            public ParticleSystem.MinMaxGradient StartColor;
            public bool LiveFactorApplied;
            public float LiveFactor = 1f;

            public ParticleState(ParticleSystem.MinMaxGradient startColor)
            {
                StartColor = startColor;
            }
        }

        private class ParticleRendererState
        {
            public Material[] Materials;
            public Material TrailMaterial;
            public Material[] RuntimeMaterials;
            public Material RuntimeTrailMaterial;

            public ParticleRendererState(Material[] materials, Material trailMaterial)
            {
                Materials = materials ?? new Material[0];
                TrailMaterial = trailMaterial;
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
