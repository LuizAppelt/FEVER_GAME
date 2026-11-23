using UnityEngine;

namespace MapaEstomago
{
    /// <summary>
    /// Controla erupcoes periodicas nas crateras ulcerosas de acido (Props_Acid_Vents):
    /// acumula pressao com clarão verde e "explode" um jato de gotas pixeladas de acido.
    /// </summary>
    public class UlcerEruption : MonoBehaviour
    {
        [Header("Temporizacao da Erupcao")]
        public float minInterval = 2.8f;
        public float maxInterval = 6.2f;
        public float chargeTime = 0.85f;
        public int dropletsPerEruption = 28;

        [Header("Referencias")]
        public ParticleSystem eruptionParticles;
        public Light craterGlowLight;
        public Material pixelParticleMaterial;

        [Header("Intensidade da Luz")]
        public float baseLightIntensity = 2.2f;
        public float peakLightIntensity = 9.0f;

        private float _timer;
        private float _nextTrigger;
        private bool _charging;

        private void Start()
        {
            if (eruptionParticles == null)
            {
                CreateDefaultEruptionParticles();
            }
            ScheduleNext();
        }

        private void Update()
        {
            _timer += Time.deltaTime;

            if (!_charging)
            {
                if (craterGlowLight != null)
                {
                    float idle = baseLightIntensity + Mathf.Sin(Time.time * 2.5f) * 0.45f;
                    craterGlowLight.intensity = Mathf.Lerp(craterGlowLight.intensity, idle, Time.deltaTime * 6f);
                }

                if (_timer >= _nextTrigger)
                {
                    _charging = true;
                    _timer = 0f;
                }
            }
            else
            {
                float t = Mathf.Clamp01(_timer / chargeTime);
                if (craterGlowLight != null)
                {
                    craterGlowLight.intensity = Mathf.Lerp(baseLightIntensity, peakLightIntensity, t * t);
                }

                if (t >= 1f)
                {
                    TriggerEruption();
                    _charging = false;
                    ScheduleNext();
                }
            }
        }

        private void ScheduleNext()
        {
            _timer = 0f;
            _nextTrigger = Random.Range(minInterval, maxInterval);
        }

        public void TriggerEruption()
        {
            if (eruptionParticles != null)
            {
                eruptionParticles.Emit(dropletsPerEruption);
            }
            if (craterGlowLight != null)
            {
                craterGlowLight.intensity = peakLightIntensity * 1.2f;
            }
        }

        private void CreateDefaultEruptionParticles()
        {
            GameObject fxObj = new GameObject("UlcerEruptionFX");
            fxObj.transform.SetParent(transform, false);

            eruptionParticles = fxObj.AddComponent<ParticleSystem>();
            var main = eruptionParticles.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = 1.5f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.75f, 1.35f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(4.2f, 7.8f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.14f, 0.30f);
            main.gravityModifier = 1.65f;
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.25f, 1.0f, 0.12f, 1f),
                new Color(0.78f, 1.0f, 0.32f, 1f)
            );
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var emission = eruptionParticles.emission;
            emission.enabled = false;

            var shape = eruptionParticles.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 26f;
            shape.radius = 0.35f;
            shape.rotation = new Vector3(-90f, 0f, 0f);

            var sizeOverLife = eruptionParticles.sizeOverLifetime;
            sizeOverLife.enabled = true;
            sizeOverLife.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.25f));

            var psRenderer = fxObj.GetComponent<ParticleSystemRenderer>();
            if (pixelParticleMaterial != null)
            {
                psRenderer.sharedMaterial = pixelParticleMaterial;
            }
        }
    }
}
