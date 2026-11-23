using System.Collections.Generic;
using UnityEngine;

namespace StomachAbyss.Atmosphere
{
    /// <summary>
    /// Dynamically animates green acid point lights (intensity, range, acid-lime/emerald color shift,
    /// and subtle ~0.6m spatial orbit) to simulate boiling liquid caustic light bouncing onto undersides
    /// of bone arches, viaduct vertebrae, cliff overhangs, and cathedral foundations.
    /// </summary>
    [DisallowMultipleComponent]
    public class AcidCausticUnderglow : MonoBehaviour
    {
        [Header("Target Lights (auto-collects self/children if empty)")]
        [SerializeField] private List<Light> targetLights = new List<Light>();

        [Header("Caustic Intensity & Range")]
        [SerializeField, Range(0f, 0.6f)] private float intensitySwing = 0.28f;
        [SerializeField, Range(0f, 0.3f)] private float rangeSwing = 0.10f;
        [SerializeField, Range(0.2f, 5.0f)] private float causticSpeed = 1.65f;

        [Header("Color Shift (Acid-Lime <-> Virulent Emerald)")]
        [SerializeField] private Color acidLimeColor = new Color(0.56f, 1.00f, 0.16f, 1f);
        [SerializeField] private Color virulentEmeraldColor = new Color(0.10f, 0.94f, 0.38f, 1f);
        [SerializeField, Range(0f, 1f)] private float colorShiftBlend = 0.55f;

        [Header("Spatial Caustic Orbit")]
        [SerializeField, Range(0f, 1.5f)] private float orbitRadiusXZ = 0.60f;
        [SerializeField, Range(0f, 0.8f)] private float orbitHeaveY = 0.22f;
        [SerializeField, Range(0.1f, 3.0f)] private float orbitSpeed = 0.92f;

        private struct LightState
        {
            public Light Light;
            public Transform Transform;
            public Vector3 BaseLocalPosition;
            public Color BaseColor;
            public float BaseIntensity;
            public float BaseRange;
            public float Phase;
            public float FreqScale;
        }

        private readonly List<LightState> _states = new List<LightState>();

        public void SetTargetLights(IEnumerable<Light> lights)
        {
            targetLights.Clear();
            if (lights != null)
            {
                foreach (Light l in lights)
                {
                    if (l != null && !targetLights.Contains(l))
                    {
                        targetLights.Add(l);
                    }
                }
            }
            RebuildStates();
        }

        private void Awake()
        {
            CollectFallbackIfEmpty();
            RebuildStates();
        }

        private void OnEnable()
        {
            CollectFallbackIfEmpty();
            RebuildStates();
        }

        private void OnDisable()
        {
            RestoreBaseStates();
        }

        private void CollectFallbackIfEmpty()
        {
            if (targetLights.Count > 0) return;

            Light selfLight = GetComponent<Light>();
            if (selfLight != null)
            {
                targetLights.Add(selfLight);
                return;
            }

            Light[] childLights = GetComponentsInChildren<Light>(true);
            for (int i = 0; i < childLights.Length; i++)
            {
                if (childLights[i] != null && !targetLights.Contains(childLights[i]))
                {
                    targetLights.Add(childLights[i]);
                }
            }
        }

        private void RebuildStates()
        {
            _states.Clear();
            var seen = new HashSet<Light>();

            for (int i = 0; i < targetLights.Count; i++)
            {
                Light l = targetLights[i];
                if (l == null || !seen.Add(l)) continue;

                // Deterministic per-light golden-ratio stagger so neighboring lights ripple organically
                float phase = ((i * 0.6180339f) % 1f) * Mathf.PI * 2f;
                float freq = 0.86f + ((i * 0.37f) % 0.32f);

                _states.Add(new LightState
                {
                    Light = l,
                    Transform = l.transform,
                    BaseLocalPosition = l.transform.localPosition,
                    BaseColor = l.color,
                    BaseIntensity = l.intensity,
                    BaseRange = l.range,
                    Phase = phase,
                    FreqScale = freq
                });
            }
        }

        private void Update()
        {
            float t = Time.time;

            for (int i = 0; i < _states.Count; i++)
            {
                LightState s = _states[i];
                if (s.Light == null || !s.Light.enabled || !s.Transform.gameObject.activeInHierarchy)
                {
                    continue;
                }

                float cTime = t * causticSpeed * s.FreqScale + s.Phase;
                float wave1 = Mathf.Sin(cTime);
                float wave2 = Mathf.Cos(cTime * 1.73f + 0.8f);
                float wave3 = Mathf.Sin(cTime * 2.89f - 1.4f);

                float combined = wave1 * 0.52f + wave2 * 0.32f + wave3 * 0.16f;
                float colorT = 0.5f + 0.5f * Mathf.Sin(cTime * 0.63f + wave2 * 0.7f);

                s.Light.intensity = Mathf.Max(0.1f, s.BaseIntensity * (1f + combined * intensitySwing));
                s.Light.range = Mathf.Max(1f, s.BaseRange * (1f + wave2 * rangeSwing));

                Color causticHue = Color.Lerp(acidLimeColor, virulentEmeraldColor, colorT);
                s.Light.color = Color.Lerp(s.BaseColor, causticHue, colorShiftBlend);

                float oTime = t * orbitSpeed * s.FreqScale + s.Phase;
                float dx = Mathf.Cos(oTime) * orbitRadiusXZ + Mathf.Sin(oTime * 1.67f) * (orbitRadiusXZ * 0.25f);
                float dz = Mathf.Sin(oTime * 1.13f) * orbitRadiusXZ;
                float dy = Mathf.Sin(oTime * 1.91f + 0.5f) * orbitHeaveY;
                s.Transform.localPosition = s.BaseLocalPosition + new Vector3(dx, dy, dz);
            }
        }

        private void RestoreBaseStates()
        {
            for (int i = 0; i < _states.Count; i++)
            {
                LightState s = _states[i];
                if (s.Light == null) continue;
                s.Light.color = s.BaseColor;
                s.Light.intensity = s.BaseIntensity;
                s.Light.range = s.BaseRange;
                if (s.Transform != null)
                {
                    s.Transform.localPosition = s.BaseLocalPosition;
                }
            }
        }
    }
}
