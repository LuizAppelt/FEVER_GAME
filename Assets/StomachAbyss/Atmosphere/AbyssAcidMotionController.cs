using System.Collections.Generic;
using UnityEngine;

namespace StomachAbyss.Atmosphere
{
    /// <summary>
    /// Animates organic UV flow surges, wave breathing, and HDR caustic simmer on the abyss acid lake
    /// and optional waterfall renderers using MaterialPropertyBlock (never duplicating shared materials).
    /// </summary>
    [DisallowMultipleComponent]
    public class AbyssAcidMotionController : MonoBehaviour
    {
        [Header("Target Renderers")]
        [SerializeField] private Renderer lakeRenderer;
        [SerializeField] private List<Renderer> waterfallRenderers = new List<Renderer>();

        [Header("Lake Flow & Simmer")]
        [SerializeField, Range(0f, 1.5f)] private float flowSurgeAmplitude = 0.42f;
        [SerializeField, Range(0.05f, 2.0f)] private float flowSurgeFrequency = 0.28f;
        [SerializeField, Range(0f, 0.35f)] private float waveBreathingFraction = 0.16f;
        [SerializeField, Range(0f, 0.5f)] private float emissiveSimmerAmplitude = 0.22f;
        [SerializeField, Range(0.1f, 4.0f)] private float emissiveSimmerSpeed = 1.35f;

        [Header("Waterfall Simmer")]
        [SerializeField, Range(0f, 0.5f)] private float waterfallEmissionAmplitude = 0.24f;
        [SerializeField, Range(0.2f, 5.0f)] private float waterfallSimmerSpeed = 2.4f;

        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        private static readonly int WaveAmpId = Shader.PropertyToID("_WaveAmp");
        private static readonly int WarpStrengthId = Shader.PropertyToID("_WarpStrength");
        private static readonly int FlowModId = Shader.PropertyToID("_FlowMod");

        private MaterialPropertyBlock _mpb;

        private bool _lakeCached;
        private Color _lakeBaseEmission;
        private float _lakeBaseWaveAmp;
        private float _lakeBaseWarp;
        private bool _lakeHasEmission;
        private bool _lakeHasWaveAmp;
        private bool _lakeHasWarp;
        private bool _lakeHasFlowMod;

        private readonly List<WaterfallCache> _waterfallCache = new List<WaterfallCache>();

        private struct WaterfallCache
        {
            public Renderer Renderer;
            public Color BaseEmission;
            public float BaseWaveAmp;
            public float PhaseOffset;
            public bool HasEmission;
            public bool HasWaveAmp;
        }

        public void SetTargets(Renderer lake, IEnumerable<Renderer> waterfalls)
        {
            lakeRenderer = lake;
            waterfallRenderers.Clear();
            if (waterfalls != null)
            {
                foreach (Renderer r in waterfalls)
                {
                    if (r != null && !waterfallRenderers.Contains(r))
                    {
                        waterfallRenderers.Add(r);
                    }
                }
            }
            RebuildCache();
        }

        private void Awake()
        {
            if (lakeRenderer == null)
            {
                lakeRenderer = GetComponent<Renderer>();
            }
            RebuildCache();
        }

        private void OnEnable()
        {
            if (_mpb == null) _mpb = new MaterialPropertyBlock();
            RebuildCache();
        }

        private void OnDisable()
        {
            ClearPropertyBlocks();
        }

        private void RebuildCache()
        {
            if (_mpb == null) _mpb = new MaterialPropertyBlock();

            _lakeCached = false;
            if (lakeRenderer != null && lakeRenderer.sharedMaterial != null)
            {
                Material mat = lakeRenderer.sharedMaterial;
                _lakeHasEmission = mat.HasProperty(EmissionColorId);
                _lakeHasWaveAmp = mat.HasProperty(WaveAmpId);
                _lakeHasWarp = mat.HasProperty(WarpStrengthId);
                _lakeHasFlowMod = mat.HasProperty(FlowModId);

                _lakeBaseEmission = _lakeHasEmission ? mat.GetColor(EmissionColorId) : new Color(0.42f, 1.85f, 0.16f, 1f);
                _lakeBaseWaveAmp = _lakeHasWaveAmp ? mat.GetFloat(WaveAmpId) : 0.48f;
                _lakeBaseWarp = _lakeHasWarp ? mat.GetFloat(WarpStrengthId) : 0.14f;
                _lakeCached = true;
            }

            _waterfallCache.Clear();
            for (int i = 0; i < waterfallRenderers.Count; i++)
            {
                Renderer r = waterfallRenderers[i];
                if (r == null || r.sharedMaterial == null) continue;

                Material mat = r.sharedMaterial;
                bool hasEm = mat.HasProperty(EmissionColorId);
                bool hasWave = mat.HasProperty(WaveAmpId);

                _waterfallCache.Add(new WaterfallCache
                {
                    Renderer = r,
                    BaseEmission = hasEm ? mat.GetColor(EmissionColorId) : new Color(0.18f, 0.85f, 0.10f, 1f),
                    BaseWaveAmp = hasWave ? mat.GetFloat(WaveAmpId) : 0.03f,
                    PhaseOffset = i * 1.37f,
                    HasEmission = hasEm,
                    HasWaveAmp = hasWave
                });
            }
        }

        private void Update()
        {
            float t = Time.time;

            if (_lakeCached && lakeRenderer != null)
            {
                lakeRenderer.GetPropertyBlock(_mpb);

                float swellWave = Mathf.Sin(t * flowSurgeFrequency * Mathf.PI * 2f) * 0.65f
                                + Mathf.Cos(t * flowSurgeFrequency * 1.61f * Mathf.PI * 2f) * 0.35f;

                if (_lakeHasFlowMod)
                {
                    _mpb.SetFloat(FlowModId, swellWave * flowSurgeAmplitude);
                }

                if (_lakeHasWaveAmp)
                {
                    float ampScale = 1f + swellWave * waveBreathingFraction;
                    _mpb.SetFloat(WaveAmpId, Mathf.Max(0.01f, _lakeBaseWaveAmp * ampScale));
                }

                if (_lakeHasWarp)
                {
                    float warpScale = 1f + Mathf.Sin(t * 0.47f) * 0.22f;
                    _mpb.SetFloat(WarpStrengthId, Mathf.Max(0f, _lakeBaseWarp * warpScale));
                }

                if (_lakeHasEmission)
                {
                    float simmer = 1f + (Mathf.Sin(t * emissiveSimmerSpeed) * 0.65f
                                       + Mathf.Cos(t * emissiveSimmerSpeed * 1.73f) * 0.35f) * emissiveSimmerAmplitude;
                    Color c = _lakeBaseEmission * Mathf.Max(0.25f, simmer);
                    c.a = _lakeBaseEmission.a;
                    _mpb.SetColor(EmissionColorId, c);
                }

                lakeRenderer.SetPropertyBlock(_mpb);
            }

            for (int i = 0; i < _waterfallCache.Count; i++)
            {
                WaterfallCache item = _waterfallCache[i];
                if (item.Renderer == null) continue;

                item.Renderer.GetPropertyBlock(_mpb);
                float phase = t * waterfallSimmerSpeed + item.PhaseOffset;
                float ripple = Mathf.Sin(phase) * 0.6f + Mathf.Sin(phase * 1.91f) * 0.4f;

                if (item.HasEmission)
                {
                    Color c = item.BaseEmission * Mathf.Max(0.3f, 1f + ripple * waterfallEmissionAmplitude);
                    c.a = item.BaseEmission.a;
                    _mpb.SetColor(EmissionColorId, c);
                }

                if (item.HasWaveAmp)
                {
                    _mpb.SetFloat(WaveAmpId, Mathf.Max(0.005f, item.BaseWaveAmp * (1f + ripple * 0.25f)));
                }

                item.Renderer.SetPropertyBlock(_mpb);
            }
        }

        private void ClearPropertyBlocks()
        {
            if (lakeRenderer != null) lakeRenderer.SetPropertyBlock(null);
            for (int i = 0; i < waterfallRenderers.Count; i++)
            {
                if (waterfallRenderers[i] != null)
                {
                    waterfallRenderers[i].SetPropertyBlock(null);
                }
            }
        }
    }
}
