using System.Collections.Generic;
using UnityEngine;

namespace StomachAbyss.Atmosphere
{
    /// <summary>
    /// Pulses backlit orange membrane wall/ceiling pods (Mat_Abyss_MembraneBacklit) using
    /// MaterialPropertyBlock (no shared material duplication), gentle organic breathing scale,
    /// and linked warm orange point lights with staggered double-pulse respiration curves.
    /// </summary>
    [DisallowMultipleComponent]
    public class MembranePodPulse : MonoBehaviour
    {
        [Header("Targets")]
        [SerializeField] private Renderer targetRenderer;
        [SerializeField] private List<Light> associatedLights = new List<Light>();

        [Header("HDR Emission Pulse")]
        [ColorUsage(true, true)]
        [SerializeField] private Color dimEmberEmission = new Color(0.55f, 0.16f, 0.04f, 1f);
        [ColorUsage(true, true)]
        [SerializeField] private Color brightOrangeEmission = new Color(2.45f, 0.78f, 0.18f, 1f);

        [Header("Respiration & Double-Pulse Rhythm")]
        [SerializeField, Range(0.15f, 2.5f)] private float cyclesPerSecond = 0.42f;
        [SerializeField, Range(0f, 1f)] private float phaseOffset = 0f;
        [SerializeField, Range(0f, 1f)] private float doublePulseWeight = 0.55f;

        [Header("Organic Breathing Scale")]
        [SerializeField] private bool animateScale = true;
        [SerializeField, Range(0.95f, 1.02f)] private float minScaleMultiplier = 1.0f;
        [SerializeField, Range(1.0f, 1.12f)] private float maxScaleMultiplier = 1.045f;

        [Header("Associated Light Modulation")]
        [SerializeField, Range(0.1f, 1.0f)] private float lightMinIntensityMult = 0.45f;
        [SerializeField, Range(1.0f, 2.5f)] private float lightMaxIntensityMult = 1.55f;
        [SerializeField, Range(0f, 0.3f)] private float lightRangeSwing = 0.10f;
        [SerializeField] private Color lightEmberColor = new Color(0.95f, 0.28f, 0.06f, 1f);
        [SerializeField] private Color lightBrightColor = new Color(1.00f, 0.54f, 0.18f, 1f);

        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        private MaterialPropertyBlock _mpb;
        private Vector3 _baseLocalScale;
        private bool _scaleCaptured;

        private struct LinkedLightState
        {
            public Light Light;
            public Color BaseColor;
            public float BaseIntensity;
            public float BaseRange;
        }

        private readonly List<LinkedLightState> _lightStates = new List<LinkedLightState>();

        public void Configure(Renderer podRenderer, IEnumerable<Light> lights, float staggeredPhase)
        {
            targetRenderer = podRenderer;
            phaseOffset = staggeredPhase;

            associatedLights.Clear();
            if (lights != null)
            {
                foreach (Light l in lights)
                {
                    if (l != null && !associatedLights.Contains(l))
                    {
                        associatedLights.Add(l);
                    }
                }
            }

            CaptureInitialState();
        }

        private void Awake()
        {
            if (targetRenderer == null)
            {
                targetRenderer = GetComponent<Renderer>();
            }
            CaptureInitialState();
        }

        private void OnEnable()
        {
            if (_mpb == null) _mpb = new MaterialPropertyBlock();
            CaptureInitialState();
        }

        private void OnDisable()
        {
            RestoreInitialState();
        }

        private void CaptureInitialState()
        {
            if (_mpb == null) _mpb = new MaterialPropertyBlock();

            if (!_scaleCaptured)
            {
                _baseLocalScale = transform.localScale;
                _scaleCaptured = true;
            }

            _lightStates.Clear();
            var seen = new HashSet<Light>();
            for (int i = 0; i < associatedLights.Count; i++)
            {
                Light l = associatedLights[i];
                if (l == null || !seen.Add(l)) continue;

                _lightStates.Add(new LinkedLightState
                {
                    Light = l,
                    BaseColor = l.color,
                    BaseIntensity = l.intensity,
                    BaseRange = l.range
                });
            }
        }

        private void Update()
        {
            float pulse = EvaluateOrganicPulse(Time.time);

            if (targetRenderer != null)
            {
                targetRenderer.GetPropertyBlock(_mpb);
                Color emission = Color.Lerp(dimEmberEmission, brightOrangeEmission, pulse);
                _mpb.SetColor(EmissionColorId, emission);
                targetRenderer.SetPropertyBlock(_mpb);
            }

            if (animateScale && _scaleCaptured)
            {
                float s = Mathf.Lerp(minScaleMultiplier, maxScaleMultiplier, pulse);
                transform.localScale = _baseLocalScale * s;
            }

            for (int i = 0; i < _lightStates.Count; i++)
            {
                LinkedLightState state = _lightStates[i];
                if (state.Light == null) continue;

                state.Light.intensity = state.BaseIntensity * Mathf.Lerp(lightMinIntensityMult, lightMaxIntensityMult, pulse);
                state.Light.range = state.BaseRange * Mathf.Lerp(1f - lightRangeSwing * 0.5f, 1f + lightRangeSwing, pulse);

                Color warmHue = Color.Lerp(lightEmberColor, lightBrightColor, pulse);
                state.Light.color = Color.Lerp(state.BaseColor, warmHue, 0.5f);
            }
        }

        private float EvaluateOrganicPulse(float timeSeconds)
        {
            float cycle = Mathf.Repeat(timeSeconds * cyclesPerSecond + phaseOffset, 1f);

            // Smooth visceral inhalation/exhalation wave
            float breath = 0.5f - 0.5f * Mathf.Cos(cycle * Mathf.PI * 2f);

            // Organic double heartbeat ("lub-dub" surge nested inside breath)
            float beat1 = Mathf.Exp(-Mathf.Pow((cycle - 0.24f) / 0.11f, 2f));
            float beat2 = 0.68f * Mathf.Exp(-Mathf.Pow((cycle - 0.44f) / 0.12f, 2f));
            float doubleBeat = Mathf.Clamp01(beat1 + beat2);

            return Mathf.Clamp01(Mathf.Lerp(breath, doubleBeat, doublePulseWeight * 0.65f));
        }

        private void RestoreInitialState()
        {
            if (targetRenderer != null)
            {
                targetRenderer.SetPropertyBlock(null);
            }

            if (_scaleCaptured)
            {
                transform.localScale = _baseLocalScale;
            }

            for (int i = 0; i < _lightStates.Count; i++)
            {
                LinkedLightState state = _lightStates[i];
                if (state.Light == null) continue;
                state.Light.intensity = state.BaseIntensity;
                state.Light.range = state.BaseRange;
                state.Light.color = state.BaseColor;
            }
        }
    }
}
