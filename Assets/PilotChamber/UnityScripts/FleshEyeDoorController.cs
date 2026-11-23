using UnityEngine;

namespace MapaEstomago
{
    /// <summary>
    /// Controla todas as animacoes vivas da Porta de Carne e Olho:
    /// - Pulsacao cardiaca/peristaltica da estrutura
    /// - Boca se mexendo levemente (gengiva/dentes superiores + lingua inferior)
    /// - Palpebras piscando naturalmente (com piscada dupla ocasional)
    /// - Globo ocular seguindo o jogador (ou a camera enquanto nao houver Player na cena)
    /// - Sequencia cinematica completa de ENGOLIR, MASTIGAR e CUSPIR o jogador!
    /// </summary>
    public class FleshEyeDoorController : MonoBehaviour
    {
        public enum DoorActionState
        {
            Idle,
            BitingAndChewing,
            SwollenClosedWait,
            SpittingOut
        }

        [Header("Alvo do Olho")]
        [Tooltip("Se vazio, busca automaticamente um objeto com a tag 'Player'.")]
        public Transform playerTarget;

        [Tooltip("Enquanto nao existir Player na cena, o olho acompanha levemente a Camera principal para facilitar testes.")]
        public bool trackCameraIfNoPlayer = true;

        [Tooltip("Velocidade de rotacao do olho ao acompanhar o alvo.")]
        public float eyeFollowSpeed = 7.5f;

        [Range(5f, 55f)]
        public float maxYawAngle = 34f;

        [Range(5f, 40f)]
        public float maxPitchAngle = 22f;

        [Header("Piscada das Palpebras")]
        public float minBlinkInterval = 2.0f;
        public float maxBlinkInterval = 5.2f;
        public float blinkCloseTime = 0.075f;
        public float blinkHoldTime = 0.045f;
        public float blinkOpenTime = 0.13f;
        [Range(0f, 1f)]
        public float doubleBlinkChance = 0.28f;
        public float upperLidCloseAngle = 34f;
        public float lowerLidCloseAngle = -30f;

        [Header("Movimento Leve da Boca (Idle)")]
        public float mouthSpeed = 1.45f;
        public float upperJawPitchDeg = 3.2f;
        public float upperJawBobAmount = 0.025f;
        public float lowerJawPitchDeg = 4.5f;

        [Header("Animacao de Mordida, Mastigacao e Cusparada")]
        [Tooltip("Deslocamento vertical da gengiva/dentes superiores ao fechar a boca totalmente.")]
        public float biteUpperDropY = -1.06f;
        [Tooltip("Rotacao de fechamento dos dentes superiores ao morder.")]
        public float biteUpperPitchDeg = 22f;
        [Tooltip("Elevacao da lingua/mandibula inferior ao fechar a boca.")]
        public float biteLowerRiseY = 0.24f;
        [Tooltip("Rotacao de fechamento da mandibula inferior.")]
        public float biteLowerPitchDeg = -26f;
        [Tooltip("Material opcional de particulas pixeladas para respingos ao mastigar e cuspir.")]
        public Material spitParticleMaterial;

        [Header("Pulsacao Organica da Porta")]
        public float beatsPerSecond = 0.72f;
        public float pulseAmplitudeXZ = 0.030f;
        public float pulseAmplitudeY = 0.018f;
        public float phaseOffset = 0f;

        [Header("Referencias das Partes (auto-preenchidas pelo nome)")]
        [SerializeField] private Transform eyeball;
        [SerializeField] private Transform eyelidUpper;
        [SerializeField] private Transform eyelidLower;
        [SerializeField] private Transform upperGum;
        [SerializeField] private Transform upperTeeth;
        [SerializeField] private Transform lowerJaw;

        private Vector3 _baseScale;
        private Vector3 _baseLocalPos;
        private Quaternion _eyeBaseRot;
        private Quaternion _upperLidBaseRot;
        private Quaternion _lowerLidBaseRot;
        private Quaternion _upperGumBaseRot;
        private Vector3 _upperGumBasePos;
        private Vector3 _upperGumBaseScale;
        private Quaternion _upperTeethBaseRot;
        private Vector3 _upperTeethBasePos;
        private Vector3 _upperTeethBaseScale;
        private Quaternion _lowerJawBaseRot;
        private Vector3 _lowerJawBasePos;
        private Vector3 _lowerJawBaseScale;

        private float _currentYaw;
        private float _currentPitch;
        private float _nextBlinkTimer;
        private float _blinkElapsed = -1f;
        private bool _pendingDoubleBlink;
        private float _nextPlayerSearchTime;
        private Transform _cachedTagPlayer;

        // Estado da animacao cinemática (Engolir / Mastigar / Cuspir)
        private DoorActionState _actionState = DoorActionState.Idle;
        private float _actionTimer;
        private float _biteShutDuration = 0.30f;
        private float _chewTotalDuration = 1.15f;
        private float _preSpitSwellDuration = 0.28f;
        private float _spitOpenDuration = 0.75f;
        private int _lastChewSpurtIndex = -1;
        private bool _spitBurstFired;
        private ParticleSystem _mouthVfx;

        public DoorActionState CurrentState => _actionState;

        /// <summary>
        /// Posicao no espaco global do interior da garganta da porta (para puxar o jogador).
        /// </summary>
        public Vector3 GetMouthThroatWorldPos()
        {
            return transform.TransformPoint(new Vector3(0f, 0.92f, -0.25f));
        }

        /// <summary>
        /// Posicao no espaco global da ponta da lingua (inicio do arco de cusparada).
        /// </summary>
        public Vector3 GetMouthLipsWorldPos()
        {
            return transform.TransformPoint(new Vector3(0f, 0.85f, 0.55f));
        }

        private void Reset()
        {
            BindParts();
        }

        private void Awake()
        {
            BindParts();
            _baseScale = transform.localScale;
            _baseLocalPos = transform.localPosition;

            if (eyeball != null) _eyeBaseRot = eyeball.localRotation;
            if (eyelidUpper != null) _upperLidBaseRot = eyelidUpper.localRotation;
            if (eyelidLower != null) _lowerLidBaseRot = eyelidLower.localRotation;
            if (upperGum != null)
            {
                _upperGumBaseRot = upperGum.localRotation;
                _upperGumBasePos = upperGum.localPosition;
                _upperGumBaseScale = upperGum.localScale;
            }
            if (upperTeeth != null)
            {
                _upperTeethBaseRot = upperTeeth.localRotation;
                _upperTeethBasePos = upperTeeth.localPosition;
                _upperTeethBaseScale = upperTeeth.localScale;
            }
            if (lowerJaw != null)
            {
                _lowerJawBaseRot = lowerJaw.localRotation;
                _lowerJawBasePos = lowerJaw.localPosition;
                _lowerJawBaseScale = lowerJaw.localScale;
            }

            _nextBlinkTimer = Random.Range(0.8f, 2.4f);
            EnsureMouthParticles();
        }

        /// <summary>
        /// Localiza automaticamente os nos da malha pelo nome caso tenham sido importados via glTFast.
        /// </summary>
        public void BindParts()
        {
            Transform[] children = GetComponentsInChildren<Transform>(true);
            foreach (Transform t in children)
            {
                switch (t.name)
                {
                    case "Door_Eyeball": if (eyeball == null) eyeball = t; break;
                    case "Door_Eyelid_Upper": if (eyelidUpper == null) eyelidUpper = t; break;
                    case "Door_Eyelid_Lower": if (eyelidLower == null) eyelidLower = t; break;
                    case "Door_Upper_Gum": if (upperGum == null) upperGum = t; break;
                    case "Door_Upper_Teeth": if (upperTeeth == null) upperTeeth = t; break;
                    case "Door_Lower_Jaw": if (lowerJaw == null) lowerJaw = t; break;
                }
            }
        }

        /// <summary>
        /// Inicia a animacao de fechar a boca com forca e mastigar o jogador.
        /// </summary>
        public void PlayBiteAndChew(float biteShutDuration = 0.28f, float chewDuration = 1.15f)
        {
            _biteShutDuration = Mathf.Max(0.10f, biteShutDuration);
            _chewTotalDuration = Mathf.Max(0.30f, chewDuration);
            _actionTimer = 0f;
            _lastChewSpurtIndex = -1;
            _actionState = DoorActionState.BitingAndChewing;
            EmitMouthBurst(24, new Color(0.85f, 0.06f, 0.08f, 1f), 3.8f);
        }

        /// <summary>
        /// Coloca a porta de destino imediatamente com a boca trancada e inchada (durante a tela preta),
        /// pronta para cuspir o jogador.
        /// </summary>
        public void PrepareClosedForSpit()
        {
            _actionTimer = 0f;
            _actionState = DoorActionState.SwollenClosedWait;
            ApplyJawPose(1.05f, 0f, 0f, 1.0f);
        }

        /// <summary>
        /// Inicia a animacao da porta de destino inchando, escancarando a boca e cuspindo o jogador!
        /// </summary>
        public void PlaySpitOut(float swellDuration = 0.22f, float spitOpenDuration = 0.80f)
        {
            _preSpitSwellDuration = Mathf.Max(0.05f, swellDuration);
            _spitOpenDuration = Mathf.Max(0.25f, spitOpenDuration);
            _actionTimer = 0f;
            _spitBurstFired = false;
            _actionState = DoorActionState.SpittingOut;
        }

        private void Update()
        {
            if (_actionState == DoorActionState.Idle)
            {
                UpdateDoorPulse();
                UpdateMouthMotion();
                UpdateEyeballTracking();
                UpdateBlinking();
            }
            else
            {
                UpdateActionSequence();
            }
        }

        private void UpdateActionSequence()
        {
            _actionTimer += Time.deltaTime;

            if (_actionState == DoorActionState.BitingAndChewing)
            {
                float totalTime = _biteShutDuration + _chewTotalDuration;
                if (_actionTimer < _biteShutDuration)
                {
                    // Fase 1: Dentes superiores despencam com tudo fechando a boca!
                    float t = Mathf.Clamp01(_actionTimer / _biteShutDuration);
                    // Curva agressiva de mordida (slam)
                    float slam = t * t * (3f - 2f * t);
                    slam = Mathf.Pow(slam, 0.65f);

                    ApplyJawPose(slam * 1.08f, 0f, 0f, slam * 0.85f);

                    // Contração da porta no impacto da mordida
                    float impact = Mathf.Sin(t * Mathf.PI);
                    transform.localScale = new Vector3(
                        _baseScale.x * (1f + 0.12f * impact),
                        _baseScale.y * (1f - 0.08f * impact),
                        _baseScale.z * (1f + 0.14f * impact)
                    );
                    UpdateEyeballForced(0f, -24f, 0.75f);
                }
                else if (_actionTimer < totalTime)
                {
                    // Fase 2: Mastigando violentamente (3 mordidas ritmicas + trituracao lateral)
                    float chewElapsed = _actionTimer - _biteShutDuration;
                    float chewNorm = Mathf.Clamp01(chewElapsed / _chewTotalDuration);
                    float chompCycles = 3.2f;
                    float phase = chewNorm * chompCycles * Mathf.PI * 2f;

                    // Oscila entre 0.42 (entre-aberta) e 1.12 (esmagando)
                    float chompWave = 0.5f + 0.5f * Mathf.Cos(phase);
                    float closeAmt = Mathf.Lerp(0.42f, 1.12f, Mathf.Pow(chompWave, 0.75f));

                    // Movimento lateral de trituracao (moendo)
                    float grindX = Mathf.Sin(phase * 0.5f) * 0.08f;
                    float grindYaw = Mathf.Cos(phase * 0.5f) * 8.5f;

                    ApplyJawPose(closeAmt, grindX, grindYaw, 0.55f + 0.35f * chompWave);

                    // Espasmo peristaltico da porta inteira + tremor
                    float bulge = Mathf.Sin(phase);
                    transform.localScale = new Vector3(
                        _baseScale.x * (1f + 0.11f * bulge),
                        _baseScale.y * (1f - 0.07f * bulge),
                        _baseScale.z * (1f + 0.13f * bulge)
                    );

                    // Dispara jatos de sangue entre os dentes a cada mordida
                    int spurtIdx = Mathf.FloorToInt(chewNorm * chompCycles);
                    if (spurtIdx != _lastChewSpurtIndex && chompWave > 0.82f)
                    {
                        _lastChewSpurtIndex = spurtIdx;
                        EmitMouthBurst(16, new Color(0.82f, 0.04f, 0.06f, 1f), 3.2f);
                    }

                    UpdateEyeballForced(grindYaw * 1.4f, -22f + bulge * 8f, 0.65f + 0.30f * chompWave);
                }
                else
                {
                    _actionState = DoorActionState.Idle;
                }
            }
            else if (_actionState == DoorActionState.SwollenClosedWait)
            {
                float tremble = Mathf.Sin(Time.time * 42f) * 0.025f;
                ApplyJawPose(1.05f, tremble, 0f, 0.90f);
                transform.localScale = new Vector3(
                    _baseScale.x * 1.12f,
                    _baseScale.y * 0.94f,
                    _baseScale.z * 1.14f
                );
                UpdateEyeballForced(0f, -18f, 0.90f);
            }
            else if (_actionState == DoorActionState.SpittingOut)
            {
                float totalTime = _preSpitSwellDuration + _spitOpenDuration;
                if (_actionTimer < _preSpitSwellDuration)
                {
                    // Incha prestes a vomitar/cuspir com a boca ainda fechada
                    float t = Mathf.Clamp01(_actionTimer / _preSpitSwellDuration);
                    float swell = Mathf.Sin(t * Mathf.PI * 0.5f);
                    float shake = Mathf.Sin(t * 50f) * 0.03f;

                    ApplyJawPose(1.05f, shake, shake * 80f, 0.85f);
                    transform.localScale = new Vector3(
                        _baseScale.x * (1f + 0.18f * swell),
                        _baseScale.y * (1f - 0.10f * swell),
                        _baseScale.z * (1f + 0.20f * swell)
                    );
                    UpdateEyeballForced(0f, -20f, 0.85f);
                }
                else if (_actionTimer < totalTime)
                {
                    // ESCANCARA A BOCA e cospe o jogador para fora!
                    if (!_spitBurstFired)
                    {
                        _spitBurstFired = true;
                        EmitMouthBurst(38, new Color(0.55f, 0.95f, 0.18f, 1f), 6.8f);
                        EmitMouthBurst(24, new Color(0.88f, 0.08f, 0.10f, 1f), 5.4f);
                    }

                    float spitElapsed = _actionTimer - _preSpitSwellDuration;
                    float t = Mathf.Clamp01(spitElapsed / _spitOpenDuration);

                    // Abre muito nos primeiros 25% e depois relaxa de volta ao normal
                    float openWide;
                    if (t < 0.25f)
                    {
                        openWide = Mathf.SmoothStep(0f, 1f, t / 0.25f);
                    }
                    else
                    {
                        openWide = 1f - Mathf.SmoothStep(0f, 1f, (t - 0.25f) / 0.75f);
                    }

                    // Valores negativos em ApplyJawPose escancaram a mandibula alem do repouso!
                    ApplyJawPose(-0.48f * openWide, 0f, 0f, -0.35f * openWide);

                    float recoil = Mathf.Sin(t * Mathf.PI) * (1f - t * 0.5f);
                    transform.localScale = new Vector3(
                        _baseScale.x * (1f - 0.08f * recoil),
                        _baseScale.y * (1f + 0.12f * recoil),
                        _baseScale.z * (1f - 0.06f * recoil)
                    );

                    // Olho arregalado olhando para o jogador sendo cuspido
                    UpdateEyeballForced(0f, 10f * openWide, -0.35f * openWide);
                }
                else
                {
                    transform.localScale = _baseScale;
                    _actionState = DoorActionState.Idle;
                }
            }
        }

        /// <summary>
        /// Aplica pose de abertura/fechamento da mandibula:
        /// closeAmount = 1 -> totalmente fechada (dentes cravados na lingua)
        /// closeAmount = 0 -> repouso aberto normal
        /// closeAmount < 0 -> escancarada para cuspir
        /// </summary>
        private void ApplyJawPose(float closeAmount, float grindOffsetX, float grindYawDeg, float lidSquint)
        {
            if (upperGum != null)
            {
                Vector3 posOff = new Vector3(grindOffsetX, closeAmount * biteUpperDropY * 0.88f, 0f);
                Quaternion rotOff = Quaternion.Euler(closeAmount * biteUpperPitchDeg, grindYawDeg, 0f);
                float stretchY = closeAmount > 0f ? (1f + closeAmount * 0.18f) : 1f;
                upperGum.localPosition = _upperGumBasePos + posOff;
                upperGum.localRotation = _upperGumBaseRot * rotOff;
                upperGum.localScale = new Vector3(_upperGumBaseScale.x, _upperGumBaseScale.y * stretchY, _upperGumBaseScale.z);
            }

            if (upperTeeth != null)
            {
                Vector3 posOff = new Vector3(grindOffsetX, closeAmount * biteUpperDropY, -Mathf.Max(0f, closeAmount) * 0.06f);
                Quaternion rotOff = Quaternion.Euler(closeAmount * biteUpperPitchDeg, grindYawDeg, 0f);
                float stretchY = closeAmount > 0f ? (1f + closeAmount * 0.26f) : 1f;
                upperTeeth.localPosition = _upperTeethBasePos + posOff;
                upperTeeth.localRotation = _upperTeethBaseRot * rotOff;
                upperTeeth.localScale = new Vector3(_upperTeethBaseScale.x, _upperTeethBaseScale.y * stretchY, _upperTeethBaseScale.z);
            }

            if (lowerJaw != null)
            {
                Vector3 posOff = new Vector3(-grindOffsetX * 0.6f, closeAmount * biteLowerRiseY, 0f);
                Quaternion rotOff = Quaternion.Euler(closeAmount * biteLowerPitchDeg, -grindYawDeg * 0.6f, 0f);
                float tongueStretchZ = closeAmount < 0f ? (1f + (-closeAmount) * 0.45f) : (1f + Mathf.Max(0f, closeAmount) * 0.08f);
                lowerJaw.localPosition = _lowerJawBasePos + posOff;
                lowerJaw.localRotation = _lowerJawBaseRot * rotOff;
                lowerJaw.localScale = new Vector3(_lowerJawBaseScale.x, _lowerJawBaseScale.y, _lowerJawBaseScale.z * tongueStretchZ);
            }

            if (eyelidUpper != null)
            {
                float upperDeg = lidSquint >= 0f
                    ? Mathf.Lerp(0f, upperLidCloseAngle, Mathf.Clamp01(lidSquint))
                    : lidSquint * 18f;
                eyelidUpper.localRotation = _upperLidBaseRot * Quaternion.Euler(upperDeg, 0f, 0f);
            }

            if (eyelidLower != null)
            {
                float lowerDeg = lidSquint >= 0f
                    ? Mathf.Lerp(0f, lowerLidCloseAngle, Mathf.Clamp01(lidSquint))
                    : -lidSquint * 14f;
                eyelidLower.localRotation = _lowerLidBaseRot * Quaternion.Euler(lowerDeg, 0f, 0f);
            }
        }

        private void UpdateEyeballForced(float targetYaw, float targetPitch, float lidSquint)
        {
            if (eyeball == null) return;
            float blend = 1f - Mathf.Exp(-14f * Time.deltaTime);
            _currentYaw = Mathf.Lerp(_currentYaw, Mathf.Clamp(targetYaw, -maxYawAngle, maxYawAngle), blend);
            _currentPitch = Mathf.Lerp(_currentPitch, Mathf.Clamp(targetPitch, -maxPitchAngle, maxPitchAngle), blend);
            eyeball.localRotation = _eyeBaseRot * Quaternion.Euler(_currentPitch, _currentYaw, 0f);
        }

        private void EnsureMouthParticles()
        {
            Transform existing = transform.Find("Mouth_Spit_VFX");
            if (existing != null)
            {
                _mouthVfx = existing.GetComponent<ParticleSystem>();
                return;
            }

            GameObject vfxGo = new GameObject("Mouth_Spit_VFX");
            vfxGo.transform.SetParent(transform, false);
            vfxGo.transform.localPosition = new Vector3(0f, 1.05f, 0.35f);
            vfxGo.transform.localRotation = Quaternion.identity;

            _mouthVfx = vfxGo.AddComponent<ParticleSystem>();
            var main = _mouthVfx.main;
            main.playOnAwake = false;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.75f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2.2f, 5.8f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.10f, 0.24f);
            main.gravityModifier = 1.4f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var emission = _mouthVfx.emission;
            emission.enabled = false;

            var shape = _mouthVfx.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 28f;
            shape.radius = 0.45f;

            var psRenderer = vfxGo.GetComponent<ParticleSystemRenderer>();
            if (psRenderer != null)
            {
                if (spitParticleMaterial != null)
                {
                    psRenderer.sharedMaterial = spitParticleMaterial;
                }
                else
                {
                    Shader sh = Shader.Find("MapaEstomago/URP/PS1_PixelParticle");
                    if (sh == null) sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
                    if (sh != null) psRenderer.sharedMaterial = new Material(sh);
                }
            }
        }

        public void EmitMouthBurst(int count, Color color, float speedMax)
        {
            if (_mouthVfx == null) EnsureMouthParticles();
            if (_mouthVfx == null) return;

            var main = _mouthVfx.main;
            main.startColor = color;
            main.startSpeed = new ParticleSystem.MinMaxCurve(speedMax * 0.45f, speedMax);
            _mouthVfx.Emit(count);
        }

        private void UpdateDoorPulse()
        {
            float cyc = (Time.time * beatsPerSecond + phaseOffset) % 1f;
            float beat1 = Mathf.Exp(-Mathf.Pow((cyc - 0.16f) / 0.07f, 2f));
            float beat2 = 0.72f * Mathf.Exp(-Mathf.Pow((cyc - 0.34f) / 0.08f, 2f));
            float pulse = beat1 + beat2;

            transform.localScale = new Vector3(
                _baseScale.x * (1f + pulse * pulseAmplitudeXZ),
                _baseScale.y * (1f + pulse * pulseAmplitudeY),
                _baseScale.z * (1f + pulse * pulseAmplitudeXZ)
            );
        }

        private void UpdateMouthMotion()
        {
            float t = Time.time;
            float chewWave = Mathf.Sin(t * mouthSpeed) * 0.65f + Mathf.Sin(t * mouthSpeed * 2.25f + 0.7f) * 0.35f;
            float upperPitch = chewWave * upperJawPitchDeg;
            Vector3 upperBob = new Vector3(0f, Mathf.Sin(t * mouthSpeed + 0.4f) * upperJawBobAmount, 0f);

            Quaternion upperRotOffset = Quaternion.Euler(upperPitch, 0f, 0f);
            if (upperGum != null)
            {
                upperGum.localRotation = _upperGumBaseRot * upperRotOffset;
                upperGum.localPosition = _upperGumBasePos + upperBob;
                upperGum.localScale = _upperGumBaseScale;
            }
            if (upperTeeth != null)
            {
                upperTeeth.localRotation = _upperTeethBaseRot * upperRotOffset;
                upperTeeth.localPosition = _upperTeethBasePos + upperBob;
                upperTeeth.localScale = _upperTeethBaseScale;
            }

            if (lowerJaw != null)
            {
                float tongueWave = Mathf.Sin(t * mouthSpeed + 2.3f) * 0.70f + Mathf.Sin(t * mouthSpeed * 1.9f) * 0.30f;
                lowerJaw.localPosition = _lowerJawBasePos;
                lowerJaw.localRotation = _lowerJawBaseRot * Quaternion.Euler(tongueWave * lowerJawPitchDeg, 0f, 0f);
                float swell = 1f + 0.03f * Mathf.Sin(t * mouthSpeed * 2.0f + 0.5f);
                lowerJaw.localScale = new Vector3(_lowerJawBaseScale.x * swell, _lowerJawBaseScale.y, _lowerJawBaseScale.z * swell);
            }
        }

        private Transform ResolveTarget()
        {
            if (playerTarget != null) return playerTarget;

            if (_cachedTagPlayer == null && Time.time >= _nextPlayerSearchTime)
            {
                _nextPlayerSearchTime = Time.time + 1.0f;
                GameObject p = GameObject.FindGameObjectWithTag("Player");
                if (p != null) _cachedTagPlayer = p.transform;
            }
            if (_cachedTagPlayer != null) return _cachedTagPlayer;

            if (trackCameraIfNoPlayer && Camera.main != null)
            {
                return Camera.main.transform;
            }
            return null;
        }

        private void UpdateEyeballTracking()
        {
            if (eyeball == null) return;

            float targetYaw = 0f;
            float targetPitch = 0f;
            Transform tgt = ResolveTarget();

            if (tgt != null)
            {
                Vector3 worldDir = (tgt.position - eyeball.position).normalized;
                Vector3 parentDir = eyeball.parent != null
                    ? eyeball.parent.InverseTransformDirection(worldDir)
                    : worldDir;
                Vector3 baseLocalDir = Quaternion.Inverse(_eyeBaseRot) * parentDir;

                float clampedZ = Mathf.Max(0.18f, baseLocalDir.z);
                targetYaw = Mathf.Atan2(baseLocalDir.x, clampedZ) * Mathf.Rad2Deg;
                float horiz = Mathf.Sqrt(baseLocalDir.x * baseLocalDir.x + clampedZ * clampedZ);
                targetPitch = -Mathf.Atan2(baseLocalDir.y, horiz) * Mathf.Rad2Deg;
            }
            else
            {
                targetYaw = Mathf.Sin(Time.time * 0.75f) * 18f;
                targetPitch = Mathf.Sin(Time.time * 1.15f + 1.1f) * 8f;
            }

            float saccadeYaw = Mathf.Sin(Time.time * 3.7f) * 0.9f;
            float saccadePitch = Mathf.Cos(Time.time * 4.3f) * 0.6f;

            targetYaw = Mathf.Clamp(targetYaw + saccadeYaw, -maxYawAngle, maxYawAngle);
            targetPitch = Mathf.Clamp(targetPitch + saccadePitch, -maxPitchAngle, maxPitchAngle);

            float blend = 1f - Mathf.Exp(-eyeFollowSpeed * Time.deltaTime);
            _currentYaw = Mathf.Lerp(_currentYaw, targetYaw, blend);
            _currentPitch = Mathf.Lerp(_currentPitch, targetPitch, blend);

            eyeball.localRotation = _eyeBaseRot * Quaternion.Euler(_currentPitch, _currentYaw, 0f);
        }

        private void UpdateBlinking()
        {
            if (eyelidUpper == null && eyelidLower == null) return;

            float blinkT = 0f;
            if (_blinkElapsed < 0f)
            {
                _nextBlinkTimer -= Time.deltaTime;
                if (_nextBlinkTimer <= 0f)
                {
                    _blinkElapsed = 0f;
                }
            }
            else
            {
                _blinkElapsed += Time.deltaTime;
                float totalDur = blinkCloseTime + blinkHoldTime + blinkOpenTime;

                if (_blinkElapsed < blinkCloseTime)
                {
                    blinkT = Mathf.SmoothStep(0f, 1f, _blinkElapsed / Mathf.Max(0.01f, blinkCloseTime));
                }
                else if (_blinkElapsed < blinkCloseTime + blinkHoldTime)
                {
                    blinkT = 1f;
                }
                else if (_blinkElapsed < totalDur)
                {
                    float openPhase = (_blinkElapsed - blinkCloseTime - blinkHoldTime) / Mathf.Max(0.01f, blinkOpenTime);
                    blinkT = 1f - Mathf.SmoothStep(0f, 1f, openPhase);
                }
                else
                {
                    _blinkElapsed = -1f;
                    if (!_pendingDoubleBlink && Random.value < doubleBlinkChance)
                    {
                        _pendingDoubleBlink = true;
                        _nextBlinkTimer = Random.Range(0.10f, 0.22f);
                    }
                    else
                    {
                        _pendingDoubleBlink = false;
                        _nextBlinkTimer = Random.Range(minBlinkInterval, maxBlinkInterval);
                    }
                }
            }

            float idleSquint = Mathf.Sin(Time.time * 1.1f) * 1.8f;
            float gazeFollow = _currentPitch * 0.22f;

            if (eyelidUpper != null)
            {
                float upperDeg = Mathf.Lerp(gazeFollow + idleSquint, upperLidCloseAngle, blinkT);
                eyelidUpper.localRotation = _upperLidBaseRot * Quaternion.Euler(upperDeg, 0f, 0f);
            }
            if (eyelidLower != null)
            {
                float lowerDeg = Mathf.Lerp(gazeFollow * 0.4f - idleSquint * 0.5f, lowerLidCloseAngle, blinkT);
                eyelidLower.localRotation = _lowerLidBaseRot * Quaternion.Euler(lowerDeg, 0f, 0f);
            }
        }
    }
}
