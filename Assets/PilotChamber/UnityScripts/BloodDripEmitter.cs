using UnityEngine;

namespace MapaEstomago
{
    /// <summary>
    /// Emite gotas de sangue pixeladas caindo das dobras superiores das paredes
    /// do estomago diretamente no lago de acido abaixo.
    /// </summary>
    public class BloodDripEmitter : MonoBehaviour
    {
        public Material pixelParticleMaterial;
        public float dripsPerSecond = 8f;
        public float dropHeight = 4.4f;
        public float spawnRadius = 10.6f;

        private ParticleSystem _ps;

        private void Start()
        {
            if (_ps == null) CreateParticleSystem();
        }

        private void CreateParticleSystem()
        {
            _ps = GetComponent<ParticleSystem>();
            if (_ps == null) _ps = gameObject.AddComponent<ParticleSystem>();

            var main = _ps.main;
            main.playOnAwake = true;
            main.loop = true;
            float lifeScale = Mathf.Clamp(Mathf.Sqrt(Mathf.Max(1f, dropHeight) / 4.4f), 0.8f, 2.2f);
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.85f * lifeScale, 1.30f * lifeScale);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.1f, 0.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.22f);
            main.gravityModifier = 1.15f;
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.55f, 0.03f, 0.06f, 1f)
            );
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var emission = _ps.emission;
            emission.enabled = true;
            emission.rateOverTime = dripsPerSecond;

            var shape = _ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Donut;
            shape.radius = spawnRadius;
            shape.donutRadius = 0.8f;
            // Arco superior/fundo da parede (evitando o recorte frontal)
            shape.arc = 215f;
            shape.rotation = new Vector3(90f, 0f, -17.5f);

            var psRenderer = GetComponent<ParticleSystemRenderer>();
            if (pixelParticleMaterial != null && psRenderer != null)
            {
                psRenderer.sharedMaterial = pixelParticleMaterial;
            }
        }
    }
}
