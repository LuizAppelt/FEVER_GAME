using UnityEngine;
using UnityEngine.Rendering;

namespace StomachAbyss.Atmosphere
{
    public enum VaporZonePreset
    {
        BossViaductPlumes,
        LowerArenaLakeMist,
        WaterfallSpray,
        CathedralAcidChannels,
        BoilingAcidDroplets,
        Custom
    }

    /// <summary>
    /// Creates and configures a curated URP PS1 ParticleSystem for toxic gastric vapor plumes,
    /// lake mist, waterfall spray, cathedral channel fumes, or boiling acid droplets.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ParticleSystem))]
    public class AcidToxicVaporField : MonoBehaviour
    {
        [Header("Zone Preset & Bounds")]
        [SerializeField] private VaporZonePreset preset = VaporZonePreset.BossViaductPlumes;
        [SerializeField] private Vector3 emitterBoxSize = new Vector3(88f, 2f, 130f);
        [SerializeField] private Material particleMaterial;
        [SerializeField] private bool configureOnAwake = false;

        [Header("Override Tuning")]
        [SerializeField, Range(1f, 80f)] private float emissionRate = 18f;
        [SerializeField, Range(0.2f, 2.5f)] private float sizeMultiplier = 1.0f;
        [SerializeField, Range(0.2f, 2.5f)] private float speedMultiplier = 1.0f;
        [SerializeField] private Color tintColor = new Color(0.56f, 0.96f, 0.30f, 1f);

        private ParticleSystem _ps;

        public VaporZonePreset Preset => preset;

        private void Awake()
        {
            _ps = GetComponent<ParticleSystem>();
            if (configureOnAwake)
            {
                ApplyPresetConfiguration();
            }
        }

        public void Configure(VaporZonePreset zonePreset, Vector3 boxSize, Material mat, Color? overrideTint = null)
        {
            preset = zonePreset;
            emitterBoxSize = boxSize;
            if (mat != null) particleMaterial = mat;
            if (overrideTint.HasValue) tintColor = overrideTint.Value;
            ApplyDefaultsForPreset(zonePreset);
            ApplyPresetConfiguration();
        }

        public void ApplyDefaultsForPreset(VaporZonePreset zonePreset)
        {
            switch (zonePreset)
            {
                case VaporZonePreset.BossViaductPlumes:
                    emissionRate = 16f;
                    sizeMultiplier = 1.0f;
                    speedMultiplier = 1.0f;
                    tintColor = new Color(0.50f, 0.94f, 0.28f, 1f);
                    break;
                case VaporZonePreset.LowerArenaLakeMist:
                    emissionRate = 20f;
                    sizeMultiplier = 1.0f;
                    speedMultiplier = 1.0f;
                    tintColor = new Color(0.45f, 0.88f, 0.26f, 1f);
                    break;
                case VaporZonePreset.WaterfallSpray:
                    emissionRate = 26f;
                    sizeMultiplier = 1.0f;
                    speedMultiplier = 1.0f;
                    tintColor = new Color(0.62f, 1.00f, 0.36f, 1f);
                    break;
                case VaporZonePreset.CathedralAcidChannels:
                    emissionRate = 14f;
                    sizeMultiplier = 1.0f;
                    speedMultiplier = 1.0f;
                    tintColor = new Color(0.54f, 0.98f, 0.32f, 1f);
                    break;
                case VaporZonePreset.BoilingAcidDroplets:
                    emissionRate = 32f;
                    sizeMultiplier = 1.0f;
                    speedMultiplier = 1.0f;
                    tintColor = new Color(0.75f, 1.00f, 0.30f, 1f);
                    break;
            }
        }

        [ContextMenu("Rebuild Particle Configuration")]
        public void ApplyPresetConfiguration()
        {
            if (_ps == null) _ps = GetComponent<ParticleSystem>();
            if (_ps == null) return;

            bool isDroplet = preset == VaporZonePreset.BoilingAcidDroplets;

            var main = _ps.main;
            main.loop = true;
            main.playOnAwake = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Shape;

            var emission = _ps.emission;
            emission.enabled = true;
            emission.rateOverTime = new ParticleSystem.MinMaxCurve(emissionRate);

            var shape = _ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;

            var vel = _ps.velocityOverLifetime;
            var noise = _ps.noise;
            var colOverLife = _ps.colorOverLifetime;
            var sizeOverLife = _ps.sizeOverLifetime;

            if (isDroplet)
            {
                main.duration = 4f;
                main.prewarm = true;
                main.maxParticles = 240;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.85f, 1.65f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(4.5f * speedMultiplier, 9.5f * speedMultiplier);
                main.startSize = new ParticleSystem.MinMaxCurve(0.45f * sizeMultiplier, 1.05f * sizeMultiplier);
                main.startRotation = new ParticleSystem.MinMaxCurve(-0.35f, 0.35f);
                main.gravityModifier = 1.85f;
                main.startColor = new ParticleSystem.MinMaxGradient(
                    tintColor,
                    new Color(0.42f, 0.95f, 0.18f, 1f));

                // Local +Z points to +Y so startSpeed ejects droplets upward before gravity pulls them back
                shape.rotation = new Vector3(-90f, 0f, 0f);
                shape.scale = new Vector3(emitterBoxSize.x, emitterBoxSize.z, Mathf.Max(0.2f, emitterBoxSize.y));
                shape.randomDirectionAmount = 0.18f;

                vel.enabled = false;

                noise.enabled = true;
                noise.strength = 0.45f;
                noise.frequency = 0.65f;
                noise.scrollSpeed = 0.5f;
                noise.octaveCount = 1;
                noise.damping = true;

                Gradient dropGrad = new Gradient();
                dropGrad.SetKeys(
                    new[]
                    {
                        new GradientColorKey(Color.white, 0f),
                        new GradientColorKey(new Color(0.85f, 1f, 0.65f), 0.7f),
                        new GradientColorKey(new Color(0.45f, 0.85f, 0.25f), 1f)
                    },
                    new[]
                    {
                        new GradientAlphaKey(0.0f, 0f),
                        new GradientAlphaKey(0.95f, 0.12f),
                        new GradientAlphaKey(0.80f, 0.72f),
                        new GradientAlphaKey(0.0f, 1f)
                    });
                colOverLife.enabled = true;
                colOverLife.color = new ParticleSystem.MinMaxGradient(dropGrad);

                AnimationCurve dropSize = new AnimationCurve(
                    new Keyframe(0f, 0.55f),
                    new Keyframe(0.25f, 1.15f),
                    new Keyframe(1f, 0.35f));
                sizeOverLife.enabled = true;
                sizeOverLife.size = new ParticleSystem.MinMaxCurve(1f, dropSize);
            }
            else
            {
                ConfigureVaporParams(
                    out float minLife, out float maxLife,
                    out float minSize, out float maxSize,
                    out float minUpVel, out float maxUpVel,
                    out float driftHoriz, out float noiseStr, out float noiseFreq,
                    out float peakAlpha, out int maxCount);

                main.duration = maxLife;
                main.prewarm = true;
                main.maxParticles = maxCount;
                main.startLifetime = new ParticleSystem.MinMaxCurve(minLife, maxLife);
                main.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.25f);
                main.startSize = new ParticleSystem.MinMaxCurve(minSize * sizeMultiplier, maxSize * sizeMultiplier);
                main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                main.gravityModifier = 0f;
                main.startColor = new ParticleSystem.MinMaxGradient(
                    tintColor,
                    new Color(tintColor.r * 0.72f, tintColor.g * 0.82f, tintColor.b * 0.68f, 1f));

                shape.rotation = Vector3.zero;
                shape.scale = emitterBoxSize;
                shape.randomDirectionAmount = 0.08f;

                vel.enabled = true;
                vel.space = ParticleSystemSimulationSpace.World;
                vel.x = new ParticleSystem.MinMaxCurve(-driftHoriz * speedMultiplier, driftHoriz * speedMultiplier);
                vel.y = new ParticleSystem.MinMaxCurve(minUpVel * speedMultiplier, maxUpVel * speedMultiplier);
                vel.z = new ParticleSystem.MinMaxCurve(-driftHoriz * speedMultiplier, driftHoriz * speedMultiplier);

                noise.enabled = true;
                noise.strength = noiseStr;
                noise.frequency = noiseFreq;
                noise.scrollSpeed = 0.22f;
                noise.octaveCount = 2;
                noise.damping = true;

                Gradient vaporGrad = new Gradient();
                vaporGrad.SetKeys(
                    new[]
                    {
                        new GradientColorKey(new Color(0.80f, 1.00f, 0.65f), 0f),
                        new GradientColorKey(Color.white, 0.45f),
                        new GradientColorKey(new Color(0.60f, 0.90f, 0.45f), 1f)
                    },
                    new[]
                    {
                        new GradientAlphaKey(0f, 0f),
                        new GradientAlphaKey(peakAlpha * 0.85f, 0.16f),
                        new GradientAlphaKey(peakAlpha, 0.48f),
                        new GradientAlphaKey(peakAlpha * 0.55f, 0.78f),
                        new GradientAlphaKey(0f, 1f)
                    });
                colOverLife.enabled = true;
                colOverLife.color = new ParticleSystem.MinMaxGradient(vaporGrad);

                AnimationCurve growCurve = new AnimationCurve(
                    new Keyframe(0f, 0.65f),
                    new Keyframe(0.5f, 1.15f),
                    new Keyframe(1f, 1.65f));
                sizeOverLife.enabled = true;
                sizeOverLife.size = new ParticleSystem.MinMaxCurve(1f, growCurve);
            }

            ParticleSystemRenderer psRenderer = _ps.GetComponent<ParticleSystemRenderer>();
            if (psRenderer != null)
            {
                psRenderer.renderMode = ParticleSystemRenderMode.Billboard;
                psRenderer.alignment = ParticleSystemRenderSpace.View;
                psRenderer.sortMode = ParticleSystemSortMode.Distance;
                psRenderer.shadowCastingMode = ShadowCastingMode.Off;
                psRenderer.receiveShadows = false;
                if (particleMaterial != null)
                {
                    psRenderer.sharedMaterial = particleMaterial;
                }
            }
        }

        private void ConfigureVaporParams(
            out float minLife, out float maxLife,
            out float minSize, out float maxSize,
            out float minUpVel, out float maxUpVel,
            out float driftHoriz, out float noiseStr, out float noiseFreq,
            out float peakAlpha, out int maxCount)
        {
            switch (preset)
            {
                case VaporZonePreset.BossViaductPlumes:
                    // Rises ~35-58m from Y=2 up past the spine viaduct height without obscuring Cathedral
                    minLife = 9.5f; maxLife = 13.5f;
                    minSize = 11.0f; maxSize = 22.0f;
                    minUpVel = 3.2f; maxUpVel = 5.0f;
                    driftHoriz = 0.85f;
                    noiseStr = 1.65f; noiseFreq = 0.16f;
                    peakAlpha = 0.24f;
                    maxCount = 260;
                    break;

                case VaporZonePreset.LowerArenaLakeMist:
                    minLife = 6.5f; maxLife = 10.5f;
                    minSize = 8.0f; maxSize = 16.0f;
                    minUpVel = 0.55f; maxUpVel = 1.35f;
                    driftHoriz = 1.05f;
                    noiseStr = 1.20f; noiseFreq = 0.18f;
                    peakAlpha = 0.26f;
                    maxCount = 220;
                    break;

                case VaporZonePreset.WaterfallSpray:
                    minLife = 3.2f; maxLife = 5.4f;
                    minSize = 4.2f; maxSize = 9.2f;
                    minUpVel = 1.8f; maxUpVel = 4.2f;
                    driftHoriz = 1.8f;
                    noiseStr = 2.10f; noiseFreq = 0.28f;
                    peakAlpha = 0.34f;
                    maxCount = 180;
                    break;

                case VaporZonePreset.CathedralAcidChannels:
                    minLife = 3.5f; maxLife = 5.8f;
                    minSize = 1.6f; maxSize = 3.4f;
                    minUpVel = 1.1f; maxUpVel = 2.2f;
                    driftHoriz = 0.28f;
                    noiseStr = 0.65f; noiseFreq = 0.36f;
                    peakAlpha = 0.28f;
                    maxCount = 110;
                    break;

                default:
                    minLife = 6.0f; maxLife = 9.0f;
                    minSize = 6.0f; maxSize = 12.0f;
                    minUpVel = 1.5f; maxUpVel = 3.0f;
                    driftHoriz = 0.65f;
                    noiseStr = 1.10f; noiseFreq = 0.20f;
                    peakAlpha = 0.25f;
                    maxCount = 160;
                    break;
            }
        }
    }
}
