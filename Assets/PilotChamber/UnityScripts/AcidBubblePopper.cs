using UnityEngine;

namespace MapaEstomago
{
    /// <summary>
    /// Faz cada bolha do acido (Acid_Bubble_01..20) crescer, tremer e "explodir"
    /// soltando gotas pixeladas de acido verde estilo PS1.
    /// </summary>
    public class AcidBubblePopper : MonoBehaviour
    {
        [Header("Ciclo da Bolha")]
        public float minDormantTime = 0.4f;
        public float maxDormantTime = 2.6f;
        public float swellDuration = 1.4f;
        public float trembleDuration = 0.35f;
        public float maxPopScale = 1.45f;

        [Header("Particulas da Explosao (Opcional - cria automatico se vazio)")]
        public ParticleSystem popParticles;
        public Material pixelParticleMaterial;
        public Material vaporParticleMaterial;

        [Header("Audio 3D Sutil")]
        public bool playPopSound = true;
        [Range(0f, 1f)]
        public float popSoundVolume = 0.22f;
        public float maxAudibleDistance = 18f;

        [Header("Luz da Explosao (Opcional)")]
        public Light popFlashLight;
        public float flashPeakIntensity = 3.5f;

        [Header("Otimizacao")]
        [Tooltip("Distancia maxima da camera/jogador para animar esta bolha.")]
        public float activeCullDistance = 75f;

        private static AudioClip s_sharedPopClip;
        private static Camera s_cachedMainCam;
        private ParticleSystem _vaporParticles;
        private AudioSource _audioSource;
        private float _cullCheckTimer;
        private bool _isCulled;

        private enum State { Dormant, Swelling, Trembling }
        private State _state;
        private float _timer;
        private float _duration;
        private float _flashTimer;
        private Vector3 _baseScale;
        private Vector3 _baseLocalPos;
        private MeshRenderer _renderer;

        private void Start()
        {
            _baseScale = transform.localScale;
            _baseLocalPos = transform.localPosition;
            _renderer = GetComponentInChildren<MeshRenderer>();

            if (popParticles == null)
            {
                CreateDefaultPopParticles();
            }

            if (vaporParticleMaterial != null && _vaporParticles == null)
            {
                CreateVaporPopParticles();
            }

            if (playPopSound)
            {
                EnsureSharedPopClip();
                _audioSource = GetComponent<AudioSource>();
                if (_audioSource == null) _audioSource = gameObject.AddComponent<AudioSource>();
                _audioSource.playOnAwake = false;
                _audioSource.spatialBlend = 1f;
                _audioSource.rolloffMode = AudioRolloffMode.Linear;
                _audioSource.minDistance = 2.5f;
                _audioSource.maxDistance = maxAudibleDistance;
                _audioSource.volume = popSoundVolume;
            }

            EnterDormant(Random.Range(0.05f, maxDormantTime));
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            _cullCheckTimer -= dt;
            if (_cullCheckTimer <= 0f)
            {
                _cullCheckTimer = 0.5f;
                if (s_cachedMainCam == null) s_cachedMainCam = Camera.main;
                if (s_cachedMainCam != null)
                {
                    float maxDistSq = activeCullDistance * activeCullDistance;
                    bool shouldCull = (s_cachedMainCam.transform.position - transform.position).sqrMagnitude > maxDistSq;
                    if (shouldCull != _isCulled)
                    {
                        _isCulled = shouldCull;
                        if (_isCulled && _renderer != null) _renderer.enabled = false;
                    }
                }
            }
            if (_isCulled) return;

            _timer += dt;

            if (popFlashLight != null && _flashTimer > 0f)
            {
                _flashTimer -= dt;
                popFlashLight.intensity = Mathf.Max(0f, (_flashTimer / 0.25f) * flashPeakIntensity);
                if (_flashTimer <= 0f) popFlashLight.enabled = false;
            }

            switch (_state)
            {
                case State.Dormant:
                    if (_timer >= _duration)
                    {
                        _state = State.Swelling;
                        _timer = 0f;
                        _duration = swellDuration * Random.Range(0.8f, 1.25f);
                        if (_renderer != null) _renderer.enabled = true;
                    }
                    break;

                case State.Swelling:
                    {
                        float t = Mathf.Clamp01(_timer / _duration);
                        // Curva organica: cresce mais rapido no final + leve squash-and-stretch
                        float ease = t * t * (3f - 2f * t);
                        float s = Mathf.Lerp(0.08f, maxPopScale, ease);
                        float wobble = 1f + Mathf.Sin(t * Mathf.PI * 5f) * 0.08f;
                        transform.localScale = new Vector3(_baseScale.x * s * wobble, _baseScale.y * s / wobble, _baseScale.z * s * wobble);
                        transform.localPosition = _baseLocalPos;

                        if (t >= 1f)
                        {
                            _state = State.Trembling;
                            _timer = 0f;
                            _duration = trembleDuration;
                        }
                        break;
                    }

                case State.Trembling:
                    {
                        float jitter = Mathf.Sin(Time.time * 65f) * 0.14f;
                        float s = maxPopScale + jitter;
                        transform.localScale = _baseScale * s;
                        transform.localPosition = _baseLocalPos + new Vector3(
                            Mathf.Sin(Time.time * 80f) * 0.04f,
                            Mathf.Cos(Time.time * 75f) * 0.03f,
                            Mathf.Cos(Time.time * 85f) * 0.04f
                        );

                        if (_timer >= _duration)
                        {
                            ExplodePop();
                            EnterDormant(Random.Range(minDormantTime, maxDormantTime));
                        }
                        break;
                    }
            }
        }

        private void EnterDormant(float waitTime)
        {
            _state = State.Dormant;
            _timer = 0f;
            _duration = waitTime;
            transform.localScale = Vector3.zero;
            transform.localPosition = _baseLocalPos;
            if (_renderer != null) _renderer.enabled = false;
        }

        private void ExplodePop()
        {
            if (popParticles != null)
            {
                popParticles.transform.position = transform.position + Vector3.up * 0.15f;
                popParticles.Emit(Random.Range(10, 16));
            }

            if (_vaporParticles != null)
            {
                _vaporParticles.transform.position = transform.position + Vector3.up * 0.12f;
                _vaporParticles.Emit(Random.Range(2, 4));
            }

            if (playPopSound && _audioSource != null && s_sharedPopClip != null)
            {
                Camera cam = Camera.main;
                if (cam == null || (cam.transform.position - transform.position).sqrMagnitude <= maxAudibleDistance * maxAudibleDistance)
                {
                    _audioSource.pitch = Random.Range(0.78f, 1.24f);
                    _audioSource.PlayOneShot(s_sharedPopClip, popSoundVolume);
                }
            }

            if (popFlashLight != null)
            {
                popFlashLight.enabled = true;
                popFlashLight.intensity = flashPeakIntensity;
                _flashTimer = 0.25f;
            }
        }

        private void CreateVaporPopParticles()
        {
            GameObject fxObj = new GameObject("AcidVaporPopFX");
            fxObj.transform.SetParent(transform, false);
            fxObj.transform.localPosition = Vector3.up * 0.12f;

            _vaporParticles = fxObj.AddComponent<ParticleSystem>();
            _vaporParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = _vaporParticles.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = 1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.65f, 1.25f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.45f, 1.1f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.45f, 0.95f);
            main.gravityModifier = -0.08f;
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.32f, 0.95f, 0.18f, 0.42f),
                new Color(0.62f, 1.0f, 0.35f, 0.28f)
            );
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var emission = _vaporParticles.emission;
            emission.enabled = false;

            var shape = _vaporParticles.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.22f;

            var psRenderer = fxObj.GetComponent<ParticleSystemRenderer>();
            psRenderer.sharedMaterial = vaporParticleMaterial;
        }

        private static void EnsureSharedPopClip()
        {
            if (s_sharedPopClip != null) return;
            const int sampleRate = 22050;
            const float duration = 0.12f;
            int count = Mathf.RoundToInt(sampleRate * duration);
            float[] data = new float[count];
            for (int i = 0; i < count; i++)
            {
                float t = (float)i / count;
                // Classic liquid bubble "bloop-pop": rising pitch sine + quick pop transient
                float freq = Mathf.Lerp(170f, 680f, t * t);
                float env = Mathf.Sin(t * Mathf.PI) * Mathf.Pow(1f - t, 1.2f);
                float wave = Mathf.Sin(2f * Mathf.PI * freq * (i / (float)sampleRate));
                float noise = (t > 0.55f && t < 0.72f) ? (Random.Range(-1f, 1f) * 0.28f) : 0f;
                data[i] = Mathf.Clamp((wave * 0.75f + noise) * env, -1f, 1f);
            }
            s_sharedPopClip = AudioClip.Create("AcidBubblePop_SFX", count, 1, sampleRate, false);
            s_sharedPopClip.SetData(data, 0);
        }

        public bool PopByAttack()
        {
            ExplodePop();
            EnterDormant(Random.Range(minDormantTime, maxDormantTime));
            return true;
        }

        private void CreateDefaultPopParticles()
        {
            GameObject fxObj = new GameObject("AcidPopFX");
            fxObj.transform.SetParent(transform, false);
            fxObj.transform.localPosition = Vector3.up * 0.12f;

            popParticles = fxObj.AddComponent<ParticleSystem>();
            popParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = popParticles.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = 1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.70f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.8f, 4.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.10f, 0.22f);
            main.gravityModifier = 1.4f;
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.30f, 1.0f, 0.15f, 1f),
                new Color(0.85f, 1.0f, 0.45f, 1f)
            );
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var emission = popParticles.emission;
            emission.enabled = false;

            var shape = popParticles.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 52f;
            shape.radius = 0.18f;
            shape.rotation = new Vector3(-90f, 0f, 0f);

            var sizeOverLife = popParticles.sizeOverLifetime;
            sizeOverLife.enabled = true;
            sizeOverLife.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.1f));

            var psRenderer = fxObj.GetComponent<ParticleSystemRenderer>();
            if (pixelParticleMaterial != null)
            {
                psRenderer.sharedMaterial = pixelParticleMaterial;
            }
        }
    }
}
