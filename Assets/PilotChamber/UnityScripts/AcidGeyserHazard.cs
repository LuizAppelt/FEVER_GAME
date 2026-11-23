using System.Collections.Generic;
using UnityEngine;

namespace MapaEstomago
{
    /// <summary>
    /// Geiser de acido interativo estilo Hades para as ilhas de combate (Island_Geyser_Anchor_01..04).
    /// Alterna entre Dormant -> Warning (telegrafa com luz estroboscopica e respingos) -> Erupting
    /// (coluna vertical de gotas pixeladas de acido + dano por segundo em area via OverlapSphere).
    /// </summary>
    public class AcidGeyserHazard : MonoBehaviour
    {
        public enum GeyserState
        {
            Dormant,
            Warning,
            Erupting
        }

        [Header("Referencias")]
        public Material pixelParticleMaterial;
        public Light warningLight;
        public ParticleSystem geyserParticles;

        [Header("Temporizacao do Ciclo")]
        public float dormantDurationMin = 3.2f;
        public float dormantDurationMax = 6.0f;
        public float warningDuration = 1.5f;
        public float eruptionDuration = 1.2f;

        [Header("Zona de Perigo e Dano")]
        public float hazardRadius = 2.2f;
        public float damagePerSecond = 28f;

        private GeyserState _state = GeyserState.Dormant;
        private float _stateTimer;
        private float _currentDormantDuration;
        private float _sputterTimer;
        private float _eruptEmitAccumulator;
        private ParticleSystemRenderer _psRenderer;
        private readonly HashSet<IAcidDamageable> _damagedThisFrame = new HashSet<IAcidDamageable>();

        private void Awake()
        {
            if (warningLight == null)
            {
                warningLight = GetComponent<Light>();
            }

            if (geyserParticles == null)
            {
                CreateDefaultGeyserParticles();
            }

            EnterDormant(Random.Range(0.6f, dormantDurationMax));
        }

        private void Start()
        {
            if (_psRenderer != null && pixelParticleMaterial != null && _psRenderer.sharedMaterial != pixelParticleMaterial)
            {
                _psRenderer.sharedMaterial = pixelParticleMaterial;
            }
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            _stateTimer += dt;

            switch (_state)
            {
                case GeyserState.Dormant:
                    UpdateDormant();
                    break;

                case GeyserState.Warning:
                    UpdateWarning(dt);
                    break;

                case GeyserState.Erupting:
                    UpdateErupting(dt);
                    break;
            }
        }

        private void EnterDormant(float duration)
        {
            _state = GeyserState.Dormant;
            _stateTimer = 0f;
            _currentDormantDuration = duration;

            if (geyserParticles != null)
            {
                geyserParticles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }

            if (warningLight != null)
            {
                warningLight.color = new Color(0.32f, 1.0f, 0.16f);
                warningLight.intensity = 0.4f;
                warningLight.range = 4f;
            }
        }

        private void EnterWarning()
        {
            _state = GeyserState.Warning;
            _stateTimer = 0f;
            _sputterTimer = 0f;

            if (warningLight != null)
            {
                warningLight.color = new Color(0.76f, 1.0f, 0.18f);
                warningLight.range = 6.5f;
            }
        }

        private void EnterErupting()
        {
            _state = GeyserState.Erupting;
            _stateTimer = 0f;
            _eruptEmitAccumulator = 0f;

            if (warningLight != null)
            {
                warningLight.color = new Color(0.28f, 1.0f, 0.14f);
                warningLight.intensity = 7.5f;
                warningLight.range = 9f;
            }

            if (geyserParticles != null)
            {
                var main = geyserParticles.main;
                main.startSpeed = new ParticleSystem.MinMaxCurve(4.5f, 7.5f);
                geyserParticles.Emit(14);
            }
        }

        private void UpdateDormant()
        {
            if (warningLight != null)
            {
                warningLight.intensity = 0.4f;
                warningLight.range = 4f;
            }

            if (_stateTimer >= _currentDormantDuration)
            {
                EnterWarning();
            }
        }

        private void UpdateWarning(float dt)
        {
            if (warningLight != null)
            {
                // Estrobo rapido entre 1.5 e 5.0 de intensidade com tom verde-amarelado toxico
                float strobe = 0.5f + 0.5f * Mathf.Sin(Time.time * 30f);
                warningLight.color = new Color(0.76f, 1.0f, 0.18f);
                warningLight.intensity = Mathf.Lerp(1.5f, 5.0f, strobe);
                warningLight.range = 6.5f;
            }

            _sputterTimer += dt;
            if (_sputterTimer >= 0.15f)
            {
                _sputterTimer -= 0.15f;
                if (geyserParticles != null)
                {
                    var main = geyserParticles.main;
                    main.startSpeed = new ParticleSystem.MinMaxCurve(1.4f, 2.8f);
                    geyserParticles.Emit(2);
                }
            }

            if (_stateTimer >= warningDuration)
            {
                EnterErupting();
            }
        }

        private void UpdateErupting(float dt)
        {
            if (warningLight != null)
            {
                warningLight.color = new Color(0.28f, 1.0f, 0.14f);
                warningLight.intensity = 7.5f;
                warningLight.range = 9f;
            }

            if (geyserParticles != null)
            {
                var main = geyserParticles.main;
                main.startSpeed = new ParticleSystem.MinMaxCurve(4.5f, 7.5f);

                _eruptEmitAccumulator += dt * 45f;
                int emitCount = Mathf.FloorToInt(_eruptEmitAccumulator);
                if (emitCount > 0)
                {
                    _eruptEmitAccumulator -= emitCount;
                    geyserParticles.Emit(emitCount);
                }
            }

            ApplyEruptionDamage(dt);

            if (_stateTimer >= eruptionDuration)
            {
                EnterDormant(Random.Range(dormantDurationMin, dormantDurationMax));
            }
        }

        private void ApplyEruptionDamage(float dt)
        {
            Collider[] hits = Physics.OverlapSphere(transform.position, hazardRadius);
            if (hits == null || hits.Length == 0) return;

            _damagedThisFrame.Clear();
            float frameDamage = damagePerSecond * dt;

            for (int i = 0; i < hits.Length; i++)
            {
                Collider col = hits[i];
                if (col == null) continue;

                IAcidDamageable damageable = col.GetComponentInParent<IAcidDamageable>() ?? col.GetComponent<IAcidDamageable>();
                if (damageable != null && _damagedThisFrame.Add(damageable))
                {
                    damageable.TakeAcidDamage(frameDamage);
                }
            }
        }

        private void CreateDefaultGeyserParticles()
        {
            GameObject fxObj = new GameObject("AcidGeyserFX");
            fxObj.transform.SetParent(transform, false);

            geyserParticles = fxObj.AddComponent<ParticleSystem>();
            var main = geyserParticles.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = eruptionDuration;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.65f, 1.15f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(4.5f, 7.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.14f, 0.28f);
            // Gravidade 9.8 m/s^2 (1.0 * Physics.gravity de -9.81 m/s^2)
            main.gravityModifier = 1.0f;
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.25f, 1.0f, 0.12f, 1f),
                new Color(0.82f, 1.0f, 0.35f, 1f)
            );
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var emission = geyserParticles.emission;
            emission.enabled = false;

            var shape = geyserParticles.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 14f;
            shape.radius = 0.28f;
            shape.rotation = new Vector3(-90f, 0f, 0f);

            var sizeOverLife = geyserParticles.sizeOverLifetime;
            sizeOverLife.enabled = true;
            sizeOverLife.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.22f));

            _psRenderer = fxObj.GetComponent<ParticleSystemRenderer>();
            if (_psRenderer != null)
            {
                _psRenderer.renderMode = ParticleSystemRenderMode.Billboard;
                if (pixelParticleMaterial != null)
                {
                    _psRenderer.sharedMaterial = pixelParticleMaterial;
                }
            }
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.35f, 1.0f, 0.15f, 0.65f);
            Gizmos.DrawWireSphere(transform.position, hazardRadius);
        }
    }
}
