using System;
using System.Collections.Generic;
using BossTenia;
using MapaEstomago;
using UnityEngine;

namespace StomachAbyss.Cathedral
{
    /// <summary>
    /// Dramatic Dark Souls 3 biomechanical cathedral entrance gate for Stomach_Abyss.
    /// Starts wide open (unobstructed vista from the bridge), then slams shut when the player
    /// enters the nave: 14 living crimson tendons yank interlocking calcified bone-jaw wings
    /// and an upper fang portcullis together with camera shake, blood/bone dust, and reverberant audio.
    /// </summary>
    public class CathedralGateDramaticTrigger : MonoBehaviour
    {
        [Serializable]
        private struct TendonBinding
        {
            public Transform strandTransform;
            public Transform anchor;
            public Transform target;
            public float baseThickness;
            public bool isSeamSuture;
            public float phaseOffset;
        }

        [Header("Referências da Cena")]
        public Transform cathedralAnchor;
        public TeniaArenaBounds arenaBounds;
        public TeniaBossAI bossAI;
        public TeniaBossHealth bossHealth;
        public Transform playerTarget;

        [Header("Threshold Espacial Fail-Safe (Espaço Local da Catedral)")]
        [Tooltip("Z local na catedral onde o portão bate (antes do trigger da Tenia em 7.60).")]
        public float entryTriggerLocalZ = 6.80f;
        public float entryMaxLocalX = 8.0f;
        public float entryMaxLocalY = 8.0f;

        [Header("Áudio do Portão")]
        public AudioClip gateSlamClip;
        [Range(0f, 1.5f)]
        public float gateSlamVolume = 0.96f;

        [Header("Materiais do Projeto (Auto-Preenchidos pelo Installer)")]
        public Material teethMaterial;
        public Material hornedBoneMaterial;
        public Material ossifiedStoneMaterial;
        public Material fleshWallMaterial;
        public Material veinTendonMaterial;
        public Material particleMaterial;

        [Header("Malhas Procedurais (Persistidas ou Geradas em Runtime)")]
        public Mesh leftWingMesh;
        public Mesh rightWingMesh;
        public Mesh fangMesh;
        public Mesh upperBeamMesh;
        public Mesh tendonMesh;
        public Mesh anchorBulbMesh;

        [Header("Estado & Animação")]
        [SerializeField] private Transform _rigRoot;
        [SerializeField] private Transform _leftWingPivot;
        [SerializeField] private Transform _rightWingPivot;
        [SerializeField] private Transform _upperPortcullis;
        [SerializeField] private BoxCollider _blockingCollider;
        [SerializeField] private ParticleSystem _bloodDustParticles;
        [SerializeField] private AudioSource _audioSource;
        [SerializeField] private List<TendonBinding> _tendons = new List<TendonBinding>();

        public bool IsClosed { get; private set; }
        public bool IsAnimatingClose { get; private set; }

        public event Action OnGateSlamStarted;
        public event Action OnGateSlammedShut;

        private const float OpenWingX = 4.05f;
        private const float ClosedWingX = 3.82f;
        private const float OpenWingYaw = 88f;
        private const float PortcullisOpenY = 6.72f;
        private const float PortcullisClosedY = 4.65f;

        private const float TelegraphDuration = 0.10f;
        private const float SlamDuration = 0.30f;
        private const float RecoilDuration = 0.15f;
        private const float TotalCloseDuration = TelegraphDuration + SlamDuration + RecoilDuration;
        private const float ReopenDuration = 0.85f;

        private float _animTimer;
        private bool _isAnimatingOpen;
        private bool _impactTriggered;
        private bool _sawBossEngaged;
        private float _closeProgress;
        private float _tendonVibration;
        private float _sfxCooldownTimer;

        private IsometricCameraFollow _isoCamera;
        private SimplePlayerController _playerController;

        private void Awake()
        {
            ResolveSceneReferences();
            EnsureAudioAndCollider();
            EnsureRigExists();
            ApplyGatePoseImmediate(0f);
            IsClosed = false;
            IsAnimatingClose = false;
            _isAnimatingOpen = false;
            _closeProgress = 0f;
        }

        private void OnEnable()
        {
            ResolveSceneReferences();
            if (bossHealth != null)
            {
                bossHealth.OnDefeated -= HandleBossDefeated;
                bossHealth.OnDefeated += HandleBossDefeated;
            }
        }

        private void OnDisable()
        {
            if (bossHealth != null)
            {
                bossHealth.OnDefeated -= HandleBossDefeated;
            }
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            if (_sfxCooldownTimer > 0f)
            {
                _sfxCooldownTimer -= dt;
            }

            ResolveSceneReferences();
            TrackBossAndResetState();
            CheckFailSafeEntryTrigger();

            if (IsAnimatingClose)
            {
                StepCloseAnimation(dt);
            }
            else if (_isAnimatingOpen)
            {
                StepOpenAnimation(dt);
            }

            if (_tendonVibration > 0f)
            {
                _tendonVibration = Mathf.Max(0f, _tendonVibration - dt * 2.4f);
            }

            UpdateDynamicTendons();
        }

        /// <summary>
        /// Triggers the dramatic tendon-yanked bone gate slam shut.
        /// Safe to call redundantly from both spatial threshold and TeniaArenaBounds.
        /// </summary>
        public void CloseDramatically()
        {
            if (IsClosed || IsAnimatingClose)
            {
                return;
            }

            if (bossHealth != null && bossHealth.IsDead)
            {
                return;
            }

            EnsureRigExists();
            EnsureAudioAndCollider();

            IsAnimatingClose = true;
            _isAnimatingOpen = false;
            IsClosed = false;
            _animTimer = 0f;
            _impactTriggered = false;
            _tendonVibration = 0.35f;

            if (gateSlamClip != null && _audioSource != null && _sfxCooldownTimer <= 0f)
            {
                _sfxCooldownTimer = 0.6f;
                _audioSource.pitch = 1.0f;
                _audioSource.PlayOneShot(gateSlamClip, gateSlamVolume);
            }

            OnGateSlamStarted?.Invoke();
        }

        /// <summary>
        /// Reopens the bone gate smoothly (or immediately) when the boss is defeated or the encounter resets.
        /// </summary>
        public void OpenGate(bool immediate = false)
        {
            EnsureRigExists();
            EnsureAudioAndCollider();

            if (_blockingCollider != null)
            {
                _blockingCollider.enabled = false;
            }

            if (immediate)
            {
                IsClosed = false;
                IsAnimatingClose = false;
                _isAnimatingOpen = false;
                _closeProgress = 0f;
                _tendonVibration = 0f;
                ApplyGatePoseImmediate(0f);
                UpdateDynamicTendons();
                return;
            }

            if (!IsClosed && !IsAnimatingClose && _closeProgress <= 0.01f)
            {
                return;
            }

            IsClosed = false;
            IsAnimatingClose = false;
            _isAnimatingOpen = true;
            _animTimer = (1f - Mathf.Clamp01(_closeProgress)) * ReopenDuration;
            _tendonVibration = 0f;
        }

        /// <summary>
        /// Builds (or rebuilds) the full procedural bone-wing, fang, and crimson-tendon rig.
        /// </summary>
        [ContextMenu("Build / Rebuild Bone Gate Rig")]
        public void BuildGateRig()
        {
            EnsureAudioAndCollider();
            EnsureMaterialsAndMeshes();

            if (_rigRoot != null)
            {
                if (Application.isPlaying)
                    Destroy(_rigRoot.gameObject);
                else
                    DestroyImmediate(_rigRoot.gameObject);
                _rigRoot = null;
            }

            Transform existing = transform.Find("GateRig");
            if (existing != null)
            {
                if (Application.isPlaying)
                    Destroy(existing.gameObject);
                else
                    DestroyImmediate(existing.gameObject);
            }

            _tendons.Clear();

            GameObject rigObj = new GameObject("GateRig");
            rigObj.transform.SetParent(transform, false);
            _rigRoot = rigObj.transform;

            // 1. Stone / flesh anchor mounts embedded around the archway jambs & crown
            Transform anchorsRoot = new GameObject("DoorframeAnchors").transform;
            anchorsRoot.SetParent(_rigRoot, false);

            Transform aL0 = CreateAnchorBulb(anchorsRoot, "Anchor_L_Crown", new Vector3(-0.90f, 6.82f, 0.25f), 0.36f);
            Transform aL1 = CreateAnchorBulb(anchorsRoot, "Anchor_L_Upper", new Vector3(-4.18f, 5.25f, -0.34f), 0.34f);
            Transform aL2 = CreateAnchorBulb(anchorsRoot, "Anchor_L_Mid", new Vector3(-4.18f, 3.05f, -0.34f), 0.34f);
            Transform aL3 = CreateAnchorBulb(anchorsRoot, "Anchor_L_Base", new Vector3(-3.88f, 0.38f, -0.34f), 0.38f);

            Transform aR0 = CreateAnchorBulb(anchorsRoot, "Anchor_R_Crown", new Vector3(0.90f, 6.82f, 0.25f), 0.36f);
            Transform aR1 = CreateAnchorBulb(anchorsRoot, "Anchor_R_Upper", new Vector3(4.18f, 5.25f, -0.34f), 0.34f);
            Transform aR2 = CreateAnchorBulb(anchorsRoot, "Anchor_R_Mid", new Vector3(4.18f, 3.05f, -0.34f), 0.34f);
            Transform aR3 = CreateAnchorBulb(anchorsRoot, "Anchor_R_Base", new Vector3(3.88f, 0.38f, -0.34f), 0.38f);

            Transform aU0 = CreateAnchorBulb(anchorsRoot, "Anchor_Portcullis_L", new Vector3(-2.35f, 6.95f, 0.14f), 0.34f);
            Transform aU1 = CreateAnchorBulb(anchorsRoot, "Anchor_Portcullis_R", new Vector3(2.35f, 6.95f, 0.14f), 0.34f);

            // 2. Left Bone-Jaw Wing
            GameObject leftPivotObj = new GameObject("LeftWingPivot");
            leftPivotObj.transform.SetParent(_rigRoot, false);
            _leftWingPivot = leftPivotObj.transform;

            CreateMeshNode(_leftWingPivot, "LeftWing_BoneFrame", leftWingMesh, hornedBoneMaterial, Vector3.zero, Quaternion.identity, Vector3.one);

            float[] leftFangHeights = { 0.62f, 1.38f, 2.14f, 2.90f, 3.66f, 4.42f, 5.18f, 5.90f };
            float[] leftFangLengths = { 0.92f, 1.10f, 1.18f, 1.24f, 1.20f, 1.14f, 1.02f, 0.85f };
            for (int i = 0; i < leftFangHeights.Length; i++)
            {
                float zTilt = (i % 2 == 0) ? 7f : -7f;
                CreateMeshNode(
                    _leftWingPivot,
                    $"LeftFang_{i:00}",
                    fangMesh,
                    (i % 3 == 0 && hornedBoneMaterial != null) ? hornedBoneMaterial : teethMaterial,
                    new Vector3(3.48f, leftFangHeights[i], (i % 2 == 0) ? 0.04f : -0.04f),
                    Quaternion.Euler(0f, zTilt, (i % 2 == 0) ? 3f : -3f),
                    new Vector3(leftFangLengths[i], 0.92f, 0.92f));
            }

            Transform lwS0 = CreateSocket(_leftWingPivot, "LeftSocket_UpperCrest", new Vector3(2.95f, 5.90f, 0.10f));
            Transform lwS1 = CreateSocket(_leftWingPivot, "LeftSocket_UpperMid", new Vector3(2.45f, 4.45f, -0.18f));
            Transform lwS2 = CreateSocket(_leftWingPivot, "LeftSocket_Center", new Vector3(2.60f, 2.95f, -0.18f));
            Transform lwS3 = CreateSocket(_leftWingPivot, "LeftSocket_Lower", new Vector3(2.25f, 1.05f, -0.18f));
            Transform lwSeamTop = CreateSocket(_leftWingPivot, "LeftSeam_Top", new Vector3(3.40f, 4.35f, -0.16f));
            Transform lwSeamMid = CreateSocket(_leftWingPivot, "LeftSeam_Mid", new Vector3(3.40f, 2.90f, -0.16f));
            Transform lwSeamLow = CreateSocket(_leftWingPivot, "LeftSeam_Low", new Vector3(3.40f, 1.40f, -0.16f));

            // 3. Right Bone-Jaw Wing (staggered interlocking heights)
            GameObject rightPivotObj = new GameObject("RightWingPivot");
            rightPivotObj.transform.SetParent(_rigRoot, false);
            _rightWingPivot = rightPivotObj.transform;

            CreateMeshNode(_rightWingPivot, "RightWing_BoneFrame", rightWingMesh, hornedBoneMaterial, Vector3.zero, Quaternion.identity, Vector3.one);

            float[] rightFangHeights = { 1.00f, 1.76f, 2.52f, 3.28f, 4.04f, 4.80f, 5.54f };
            float[] rightFangLengths = { 1.04f, 1.16f, 1.24f, 1.26f, 1.18f, 1.08f, 0.94f };
            for (int i = 0; i < rightFangHeights.Length; i++)
            {
                float zTilt = (i % 2 == 0) ? -7f : 7f;
                CreateMeshNode(
                    _rightWingPivot,
                    $"RightFang_{i:00}",
                    fangMesh,
                    (i % 3 == 1 && hornedBoneMaterial != null) ? hornedBoneMaterial : teethMaterial,
                    new Vector3(-3.48f, rightFangHeights[i], (i % 2 == 0) ? -0.04f : 0.04f),
                    Quaternion.Euler(0f, 180f + zTilt, (i % 2 == 0) ? -3f : 3f),
                    new Vector3(rightFangLengths[i], 0.92f, 0.92f));
            }

            Transform rwS0 = CreateSocket(_rightWingPivot, "RightSocket_UpperCrest", new Vector3(-2.95f, 5.90f, 0.10f));
            Transform rwS1 = CreateSocket(_rightWingPivot, "RightSocket_UpperMid", new Vector3(-2.45f, 4.45f, -0.18f));
            Transform rwS2 = CreateSocket(_rightWingPivot, "RightSocket_Center", new Vector3(-2.60f, 2.95f, -0.18f));
            Transform rwS3 = CreateSocket(_rightWingPivot, "RightSocket_Lower", new Vector3(-2.25f, 1.05f, -0.18f));
            Transform rwSeamTop = CreateSocket(_rightWingPivot, "RightSeam_Top", new Vector3(-3.40f, 4.35f, -0.16f));
            Transform rwSeamMid = CreateSocket(_rightWingPivot, "RightSeam_Mid", new Vector3(-3.40f, 2.90f, -0.16f));
            Transform rwSeamLow = CreateSocket(_rightWingPivot, "RightSeam_Low", new Vector3(-3.40f, 1.40f, -0.16f));

            // 4. Upper Descending Fang Portcullis
            GameObject portcullisObj = new GameObject("UpperPortcullis");
            portcullisObj.transform.SetParent(_rigRoot, false);
            _upperPortcullis = portcullisObj.transform;

            CreateMeshNode(_upperPortcullis, "Portcullis_BoneBeam", upperBeamMesh, hornedBoneMaterial, Vector3.zero, Quaternion.identity, Vector3.one);

            float[] upperFangX = { -3.05f, -2.28f, -1.52f, -0.76f, 0.0f, 0.76f, 1.52f, 2.28f, 3.05f };
            float[] upperFangLen = { 1.05f, 1.25f, 1.48f, 1.68f, 1.88f, 1.68f, 1.48f, 1.25f, 1.05f };
            for (int i = 0; i < upperFangX.Length; i++)
            {
                float archY = 0.16f * (1f - (upperFangX[i] / 3.4f) * (upperFangX[i] / 3.4f));
                CreateMeshNode(
                    _upperPortcullis,
                    $"UpperFang_{i:00}",
                    fangMesh,
                    teethMaterial,
                    new Vector3(upperFangX[i], -0.10f + archY, (i % 2 == 0) ? 0.06f : -0.06f),
                    Quaternion.Euler(0f, 0f, -90f),
                    new Vector3(upperFangLen[i], 1.04f, 1.04f));
            }

            Transform uSocketL = CreateSocket(_upperPortcullis, "PortcullisSocket_L", new Vector3(-2.30f, 0.25f, 0.10f));
            Transform uSocketR = CreateSocket(_upperPortcullis, "PortcullisSocket_R", new Vector3(2.30f, 0.25f, 0.10f));

            // 5. Dynamic Living Crimson Tendon Strands (10 frame muscles + 4 interlocking seam sutures)
            Transform tendonsRoot = new GameObject("DynamicTendons").transform;
            tendonsRoot.SetParent(_rigRoot, false);

            RegisterTendon(tendonsRoot, "Tendon_L0_Crown", aL0, lwS0, 1.15f, false, 0.0f);
            RegisterTendon(tendonsRoot, "Tendon_L1_Upper", aL1, lwS1, 1.00f, false, 0.9f);
            RegisterTendon(tendonsRoot, "Tendon_L2_Mid", aL2, lwS2, 1.08f, false, 1.7f);
            RegisterTendon(tendonsRoot, "Tendon_L3_Base", aL3, lwS3, 1.12f, false, 2.4f);

            RegisterTendon(tendonsRoot, "Tendon_R0_Crown", aR0, rwS0, 1.15f, false, 0.4f);
            RegisterTendon(tendonsRoot, "Tendon_R1_Upper", aR1, rwS1, 1.00f, false, 1.3f);
            RegisterTendon(tendonsRoot, "Tendon_R2_Mid", aR2, rwS2, 1.08f, false, 2.1f);
            RegisterTendon(tendonsRoot, "Tendon_R3_Base", aR3, rwS3, 1.12f, false, 2.9f);

            RegisterTendon(tendonsRoot, "Tendon_U0_PortcullisL", aU0, uSocketL, 0.95f, false, 0.7f);
            RegisterTendon(tendonsRoot, "Tendon_U1_PortcullisR", aU1, uSocketR, 0.95f, false, 1.8f);

            // Suture tendons erupt across the center tooth seam as jaws lock shut
            RegisterTendon(tendonsRoot, "Tendon_Seam_S0", lwSeamTop, rwSeamMid, 0.85f, true, 0.2f);
            RegisterTendon(tendonsRoot, "Tendon_Seam_S1", rwSeamTop, lwSeamMid, 0.85f, true, 1.1f);
            RegisterTendon(tendonsRoot, "Tendon_Seam_S2", lwSeamMid, rwSeamLow, 0.88f, true, 2.0f);
            RegisterTendon(tendonsRoot, "Tendon_Seam_S3", rwSeamMid, lwSeamLow, 0.88f, true, 2.7f);

            // 6. Impact Crimson Blood & Bone Dust Particles
            EnsureImpactParticles();

            ApplyGatePoseImmediate(0f);
            UpdateDynamicTendons();
        }

        private void EnsureRigExists()
        {
            if (_rigRoot == null || _leftWingPivot == null || _rightWingPivot == null || _upperPortcullis == null ||
                _tendons == null || _tendons.Count == 0)
            {
                BuildGateRig();
                return;
            }

            if (_bloodDustParticles == null)
            {
                EnsureImpactParticles();
            }
            else if (_bloodDustParticles.TryGetComponent<ParticleSystemRenderer>(out var psr) && psr.sharedMaterial == null)
            {
                EnsureImpactParticles();
            }
        }

        private void EnsureAudioAndCollider()
        {
            if (_blockingCollider == null)
            {
                _blockingCollider = GetComponent<BoxCollider>();
                if (_blockingCollider == null)
                {
                    _blockingCollider = gameObject.AddComponent<BoxCollider>();
                }
            }

            _blockingCollider.isTrigger = false;
            _blockingCollider.center = new Vector3(0f, 3.40f, 0f);
            _blockingCollider.size = new Vector3(7.60f, 6.80f, 1.30f);

            if (!IsClosed && !IsAnimatingClose)
            {
                _blockingCollider.enabled = false;
            }

            if (_audioSource == null)
            {
                _audioSource = GetComponent<AudioSource>();
                if (_audioSource == null)
                {
                    _audioSource = gameObject.AddComponent<AudioSource>();
                }
            }

            _audioSource.playOnAwake = false;
            _audioSource.loop = false;
            _audioSource.spatialBlend = 0.22f;
            _audioSource.minDistance = 8f;
            _audioSource.maxDistance = 55f;
            _audioSource.volume = 1.0f;
        }

        private void EnsureImpactParticles()
        {
            Transform existing = _rigRoot != null ? _rigRoot.Find("GateSlam_BloodDust_FX") : transform.Find("GateSlam_BloodDust_FX");
            if (existing != null)
            {
                _bloodDustParticles = existing.GetComponent<ParticleSystem>();
            }

            if (_bloodDustParticles == null)
            {
                GameObject fxGo = new GameObject("GateSlam_BloodDust_FX");
                fxGo.transform.SetParent(_rigRoot != null ? _rigRoot : transform, false);
                fxGo.transform.localPosition = new Vector3(0f, 3.10f, -0.15f);
                _bloodDustParticles = fxGo.AddComponent<ParticleSystem>();

                var main = _bloodDustParticles.main;
                main.playOnAwake = false;
                main.loop = false;
                main.duration = 0.9f;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 1.05f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(2.2f, 7.2f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.34f);
                main.gravityModifier = 0.95f;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.startColor = new ParticleSystem.MinMaxGradient(
                    new Color(0.68f, 0.05f, 0.07f, 1f),
                    new Color(0.86f, 0.80f, 0.68f, 0.88f));

                var emission = _bloodDustParticles.emission;
                emission.enabled = false;

                var shape = _bloodDustParticles.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Box;
                shape.scale = new Vector3(0.55f, 5.20f, 0.40f);

                var sizeOverLife = _bloodDustParticles.sizeOverLifetime;
                sizeOverLife.enabled = true;
                sizeOverLife.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.1f));

                var psr = fxGo.GetComponent<ParticleSystemRenderer>();
                if (psr != null)
                {
                    if (particleMaterial == null)
                    {
                        Shader sh = Shader.Find("MapaEstomago/URP/PS1_PixelParticle");
                        if (sh == null) sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
                        if (sh != null)
                        {
                            particleMaterial = new Material(sh);
                            if (particleMaterial.HasProperty("_BaseColor"))
                                particleMaterial.SetColor("_BaseColor", Color.white);
                            if (particleMaterial.HasProperty("_EmissionBoost"))
                                particleMaterial.SetFloat("_EmissionBoost", 1.35f);
                            if (particleMaterial.HasProperty("_Cutoff"))
                                particleMaterial.SetFloat("_Cutoff", 0.35f);
                        }
                    }
                    if (particleMaterial != null)
                    {
                        psr.sharedMaterial = particleMaterial;
                    }
                }
            }
        }

        private void StepCloseAnimation(float dt)
        {
            _animTimer += dt;

            if (_animTimer < TelegraphDuration)
            {
                // Phase 1: Quick telegraph twitch (tendons yank & tremble)
                float u = Mathf.Clamp01(_animTimer / TelegraphDuration);
                _closeProgress = u * 0.08f;
                _tendonVibration = Mathf.Max(_tendonVibration, 0.25f + 0.35f * u);
                ApplyGatePoseImmediate(_closeProgress, Mathf.Sin(_animTimer * 95f) * 0.8f);
                return;
            }

            if (_animTimer < TelegraphDuration + SlamDuration)
            {
                // Phase 2: Violent EaseInQuad slam shut
                if (_blockingCollider != null && !_blockingCollider.enabled && _animTimer >= TelegraphDuration + 0.02f)
                {
                    _blockingCollider.enabled = true;
                }

                float u = Mathf.Clamp01((_animTimer - TelegraphDuration) / SlamDuration);
                float eased = u * u; // EaseInQuad for accelerating impact
                _closeProgress = Mathf.Lerp(0.08f, 1.0f, eased);
                ApplyGatePoseImmediate(_closeProgress);
                return;
            }

            // Phase 3: Impact moment!
            if (!_impactTriggered)
            {
                _impactTriggered = true;
                _closeProgress = 1.0f;
                _tendonVibration = 1.0f;
                if (_blockingCollider != null)
                {
                    _blockingCollider.enabled = true;
                }

                if (_bloodDustParticles != null)
                {
                    _bloodDustParticles.Emit(58);
                }

                if (_isoCamera == null)
                {
                    _isoCamera = IsometricCameraFollow.Instance != null
                        ? IsometricCameraFollow.Instance
                        : FindAnyObjectByType<IsometricCameraFollow>();
                }
                _isoCamera?.TriggerShake(0.28f, 0.25f);

                OnGateSlammedShut?.Invoke();
            }

            if (_animTimer < TotalCloseDuration)
            {
                // Heavy recoil bounce on teeth/wings while tendons snap taut
                float u = Mathf.Clamp01((_animTimer - (TelegraphDuration + SlamDuration)) / RecoilDuration);
                float bounceDeg = Mathf.Sin(u * Mathf.PI) * (1f - u) * 5.2f;
                float bounceDrop = Mathf.Sin(u * Mathf.PI) * (1f - u) * 0.16f;
                ApplyGatePoseImmediate(1.0f, bounceDeg, bounceDrop);
                return;
            }

            IsAnimatingClose = false;
            IsClosed = true;
            _closeProgress = 1.0f;
            ApplyGatePoseImmediate(1.0f);
        }

        private void StepOpenAnimation(float dt)
        {
            _animTimer += dt;
            float u = Mathf.Clamp01(_animTimer / ReopenDuration);
            float smooth = u * u * (3f - 2f * u);
            _closeProgress = 1f - smooth;
            ApplyGatePoseImmediate(_closeProgress);

            if (u >= 1f)
            {
                _isAnimatingOpen = false;
                _closeProgress = 0f;
                ApplyGatePoseImmediate(0f);
            }
        }

        private void ApplyGatePoseImmediate(float closed01, float wingBounceDeg = 0f, float portcullisBounceY = 0f)
        {
            if (_leftWingPivot == null || _rightWingPivot == null || _upperPortcullis == null) return;

            float t = Mathf.Clamp01(closed01);
            float wingX = Mathf.Lerp(OpenWingX, ClosedWingX, t);
            float yaw = Mathf.Lerp(OpenWingYaw, 0f, t) + wingBounceDeg;

            _leftWingPivot.localPosition = new Vector3(-wingX, 0f, 0f);
            _leftWingPivot.localRotation = Quaternion.Euler(0f, -yaw, 0f);

            _rightWingPivot.localPosition = new Vector3(wingX, 0f, 0f);
            _rightWingPivot.localRotation = Quaternion.Euler(0f, yaw, 0f);

            float portY = Mathf.Lerp(PortcullisOpenY, PortcullisClosedY, t) + portcullisBounceY;
            _upperPortcullis.localPosition = new Vector3(0f, portY, 0f);
        }

        private void UpdateDynamicTendons()
        {
            float time = Application.isPlaying ? Time.time : 0f;
            for (int i = 0; i < _tendons.Count; i++)
            {
                TendonBinding b = _tendons[i];
                if (b.strandTransform == null || b.anchor == null || b.target == null) continue;

                if (b.isSeamSuture)
                {
                    if (_closeProgress < 0.45f)
                    {
                        b.strandTransform.localScale = Vector3.zero;
                        continue;
                    }
                    float sutureGrow = Mathf.Clamp01((_closeProgress - 0.45f) / 0.50f);
                    Vector3 a = b.anchor.position;
                    Vector3 end = Vector3.Lerp(a, b.target.position, sutureGrow);
                    OrientAndStretchTendon(b, a, end, time, sutureGrow);
                }
                else
                {
                    OrientAndStretchTendon(b, b.anchor.position, b.target.position, time, 1f);
                }
            }
        }

        private void OrientAndStretchTendon(in TendonBinding b, Vector3 worldStart, Vector3 worldEnd, float time, float visibilityScale)
        {
            Vector3 delta = worldEnd - worldStart;
            float dist = delta.magnitude;
            if (dist < 0.002f)
            {
                b.strandTransform.localScale = Vector3.zero;
                return;
            }

            Vector3 dir = delta / dist;
            Vector3 upHint = Mathf.Abs(Vector3.Dot(dir, Vector3.up)) > 0.94f ? Vector3.forward : Vector3.up;
            if (Application.isPlaying && _tendonVibration > 0.005f)
            {
                Vector3 sideAxis = Vector3.Cross(dir, upHint).normalized;
                float vib = Mathf.Sin(time * 52f + b.phaseOffset * 3.1f) * 0.038f * _tendonVibration;
                worldStart += sideAxis * vib;
                worldEnd -= sideAxis * vib;
                delta = worldEnd - worldStart;
                dist = delta.magnitude;
                dir = delta / Mathf.Max(0.002f, dist);
                upHint = Mathf.Abs(Vector3.Dot(dir, Vector3.up)) > 0.94f ? Vector3.forward : Vector3.up;
            }

            b.strandTransform.position = worldStart;
            b.strandTransform.rotation = Quaternion.LookRotation(dir, upHint);

            float pulse = Application.isPlaying
                ? (1.0f + 0.09f * Mathf.Sin(time * (IsClosed ? 3.2f : 1.8f) + b.phaseOffset))
                : 1.0f;
            float tensionThicken = 1.0f + (_closeProgress * 0.18f) + (_tendonVibration * 0.16f);
            float thickness = b.baseThickness * pulse * tensionThicken * visibilityScale;

            b.strandTransform.localScale = new Vector3(thickness, thickness, dist);
        }

        private void CheckFailSafeEntryTrigger()
        {
            if (IsClosed || IsAnimatingClose || playerTarget == null) return;
            if (bossHealth != null && bossHealth.IsDead) return;
            if (_playerController != null && _playerController.IsDead) return;

            Transform refAnchor = cathedralAnchor != null
                ? cathedralAnchor
                : (transform.parent != null ? transform.parent : transform);

            Vector3 localPos = refAnchor.InverseTransformPoint(playerTarget.position);
            // If no cathedralAnchor or parent exists, shift Z relative to standard gate offset (1.50)
            if (refAnchor == transform)
            {
                localPos.z += 1.50f;
            }

            if (localPos.z >= entryTriggerLocalZ &&
                localPos.z <= 41.5f &&
                Mathf.Abs(localPos.x) <= entryMaxLocalX &&
                Mathf.Abs(localPos.y) <= entryMaxLocalY)
            {
                CloseDramatically();
            }
        }

        private void TrackBossAndResetState()
        {
            if (bossAI == null) return;

            var state = bossAI.CurrentState;
            if (state != TeniaBossAI.EncounterState.Dormant && state != TeniaBossAI.EncounterState.Defeated)
            {
                _sawBossEngaged = true;
            }
            else if (_sawBossEngaged && state == TeniaBossAI.EncounterState.Dormant)
            {
                _sawBossEngaged = false;
                OpenGate(false);
            }
        }

        private void HandleBossDefeated()
        {
            _sawBossEngaged = false;
            OpenGate(false);
        }

        private void ResolveSceneReferences()
        {
            if (cathedralAnchor == null)
            {
                if (transform.parent != null &&
                    transform.parent.name.StartsWith("Stomach_Boss_Cathedral", StringComparison.OrdinalIgnoreCase))
                {
                    cathedralAnchor = transform.parent;
                }
                else
                {
                    GameObject cat = GameObject.Find("Stomach_Boss_Cathedral (1)");
                    if (cat == null) cat = GameObject.Find("Stomach_Boss_Cathedral");
                    if (cat != null) cathedralAnchor = cat.transform;
                }
            }

            if (bossAI == null) bossAI = FindAnyObjectByType<TeniaBossAI>();
            if (bossHealth == null)
            {
                bossHealth = bossAI != null ? bossAI.health : FindAnyObjectByType<TeniaBossHealth>();
            }
            if (arenaBounds == null)
            {
                arenaBounds = bossAI != null ? bossAI.arenaBounds : FindAnyObjectByType<TeniaArenaBounds>();
            }

            if (playerTarget == null)
            {
                GameObject p = GameObject.FindGameObjectWithTag("Player");
                if (p == null) p = GameObject.Find("Test_Player (1)");
                if (p == null) p = GameObject.Find("Test_Player");
                if (p != null) playerTarget = p.transform;
            }

            if (playerTarget != null && _playerController == null)
            {
                _playerController = playerTarget.GetComponent<SimplePlayerController>();
            }
        }

        private Transform CreateAnchorBulb(Transform parent, string objName, Vector3 localPos, float radius)
        {
            GameObject go = new GameObject(objName);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            if (anchorBulbMesh == null) EnsureMaterialsAndMeshes();

            CreateMeshNode(
                go.transform,
                "Socket",
                anchorBulbMesh,
                fleshWallMaterial != null ? fleshWallMaterial : ossifiedStoneMaterial,
                Vector3.zero,
                Quaternion.identity,
                Vector3.one * (radius * 2f));
            return go.transform;
        }

        private static Transform CreateSocket(Transform parent, string socketName, Vector3 localPos)
        {
            GameObject go = new GameObject(socketName);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            return go.transform;
        }

        private static void CreateMeshNode(
            Transform parent, string objName, Mesh mesh, Material mat,
            Vector3 localPos, Quaternion localRot, Vector3 localScale)
        {
            GameObject go = new GameObject(objName);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = localRot;
            go.transform.localScale = localScale;

            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
        }

        private void RegisterTendon(
            Transform parent, string strandName, Transform anchor, Transform target,
            float thickness, bool isSeamSuture, float phaseOffset)
        {
            GameObject go = new GameObject(strandName);
            go.transform.SetParent(parent, false);
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = tendonMesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = veinTendonMaterial;

            _tendons.Add(new TendonBinding
            {
                strandTransform = go.transform,
                anchor = anchor,
                target = target,
                baseThickness = thickness,
                isSeamSuture = isSeamSuture,
                phaseOffset = phaseOffset
            });
        }

        private void EnsureMaterialsAndMeshes()
        {
            if (teethMaterial == null) teethMaterial = CreateFallbackMaterial(new Color(0.92f, 0.87f, 0.76f, 1f), new Color(0.05f, 0.04f, 0.03f, 1f));
            if (hornedBoneMaterial == null) hornedBoneMaterial = CreateFallbackMaterial(new Color(0.68f, 0.58f, 0.46f, 1f), new Color(0.05f, 0.03f, 0.02f, 1f));
            if (ossifiedStoneMaterial == null) ossifiedStoneMaterial = CreateFallbackMaterial(new Color(0.42f, 0.36f, 0.34f, 1f), Color.black);
            if (fleshWallMaterial == null) fleshWallMaterial = CreateFallbackMaterial(new Color(0.45f, 0.14f, 0.14f, 1f), new Color(0.06f, 0.01f, 0.01f, 1f));
            if (veinTendonMaterial == null) veinTendonMaterial = CreateFallbackMaterial(new Color(0.72f, 0.08f, 0.10f, 1f), new Color(0.18f, 0.02f, 0.02f, 1f));

            if (leftWingMesh == null) leftWingMesh = CreateBoneWingMesh(false);
            if (rightWingMesh == null) rightWingMesh = CreateBoneWingMesh(true);
            if (fangMesh == null) fangMesh = CreateCurvedFangMesh();
            if (upperBeamMesh == null) upperBeamMesh = CreateUpperPortcullisBeamMesh();
            if (tendonMesh == null) tendonMesh = CreateTendonStrandMesh();
            if (anchorBulbMesh == null) anchorBulbMesh = CreateAnchorBulbMesh();
        }

        private static Material CreateFallbackMaterial(Color baseColor, Color emission)
        {
            Shader sh = Shader.Find("MapaEstomago/URP/PS1_LivingFlesh");
            if (sh == null) sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh == null) sh = Shader.Find("Standard");
            Material mat = new Material(sh);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", baseColor);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", baseColor);
            if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", emission);
            if (mat.HasProperty("_UseVertexColor")) mat.SetFloat("_UseVertexColor", 1f);
            return mat;
        }

        // =====================================================================
        // PROCEDURAL PS1 BIOMECHANICAL MESH BUILDERS (WITH VERTEX COLORS)
        // =====================================================================

        public static Mesh CreateBoneWingMesh(bool rightWing)
        {
            float dir = rightWing ? -1f : 1f;
            var builder = new ProceduralMeshBuilder();

            Color spineRoot = new Color(0.54f, 0.44f, 0.38f, 1f);
            Color ribBone = new Color(0.84f, 0.76f, 0.64f, 1f);
            Color darkCartilage = new Color(0.44f, 0.22f, 0.20f, 1f);

            // Outer vertical spine column (hinge axis X=0 -> 0.56)
            builder.AddTaperedSegment(
                new Vector3(0.24f * dir, 0.00f, 0f),
                new Vector3(0.28f * dir, 6.38f, 0f),
                0.32f, 0.26f, 6, spineRoot, ribBone);

            // Inner jagged jaw rail where teeth embed
            builder.AddTaperedSegment(
                new Vector3(3.45f * dir, 0.25f, 0f),
                new Vector3(3.38f * dir, 6.20f, 0f),
                0.22f, 0.17f, 6, darkCartilage, spineRoot);

            // Four sweeping horizontal rib crossbars
            float[] ribYs = { 0.82f, 2.25f, 3.82f, 5.45f };
            float[] ribRad = { 0.22f, 0.20f, 0.19f, 0.18f };
            for (int i = 0; i < ribYs.Length; i++)
            {
                float y = ribYs[i];
                builder.AddTaperedSegment(
                    new Vector3(0.22f * dir, y, 0f),
                    new Vector3(1.90f * dir, y + 0.14f, -0.06f),
                    ribRad[i], ribRad[i] * 0.85f, 6, spineRoot, ribBone);
                builder.AddTaperedSegment(
                    new Vector3(1.90f * dir, y + 0.14f, -0.06f),
                    new Vector3(3.48f * dir, y - 0.04f, 0f),
                    ribRad[i] * 0.85f, ribRad[i] * 0.72f, 6, ribBone, darkCartilage);
            }

            // Diagonal gothic ossified rib-braces
            builder.AddTaperedSegment(new Vector3(0.28f * dir, 0.85f, 0f), new Vector3(3.42f * dir, 2.25f, 0f), 0.13f, 0.10f, 5, darkCartilage, ribBone);
            builder.AddTaperedSegment(new Vector3(0.28f * dir, 2.30f, 0f), new Vector3(3.42f * dir, 3.82f, 0f), 0.13f, 0.10f, 5, darkCartilage, ribBone);
            builder.AddTaperedSegment(new Vector3(0.28f * dir, 3.85f, 0f), new Vector3(3.38f * dir, 5.45f, 0f), 0.12f, 0.09f, 5, darkCartilage, ribBone);
            builder.AddTaperedSegment(new Vector3(0.28f * dir, 5.45f, 0f), new Vector3(3.15f * dir, 6.26f, 0f), 0.14f, 0.09f, 5, spineRoot, ribBone);

            return builder.Build(rightWing ? "Mesh_CathedralGate_RightWing" : "Mesh_CathedralGate_LeftWing");
        }

        public static Mesh CreateCurvedFangMesh()
        {
            var builder = new ProceduralMeshBuilder();
            Color gumRoot = new Color(0.46f, 0.12f, 0.12f, 1f);
            Color midTooth = new Color(0.88f, 0.82f, 0.68f, 1f);
            Color tipIvory = new Color(1.00f, 0.96f, 0.86f, 1f);

            const int rings = 6;
            const int sides = 6;
            Vector3[] centers = new Vector3[rings];
            float[] radii = new float[rings];
            Color[] colors = new Color[rings];

            for (int r = 0; r < rings; r++)
            {
                float t = r / (float)(rings - 1);
                // Fang extends 0..1m along +X with a sinister upward hook curve
                float curveY = Mathf.Pow(t, 1.85f) * 0.16f;
                centers[r] = new Vector3(t, curveY, 0f);
                radii[r] = Mathf.Lerp(0.19f, 0.008f, Mathf.Pow(t, 0.82f));
                colors[r] = t < 0.35f
                    ? Color.Lerp(gumRoot, midTooth, t / 0.35f)
                    : Color.Lerp(midTooth, tipIvory, (t - 0.35f) / 0.65f);
            }

            builder.AddCurvedTube(centers, radii, colors, sides, capStart: true, capEnd: true);
            return builder.Build("Mesh_CathedralGate_CurvedFang");
        }

        public static Mesh CreateUpperPortcullisBeamMesh()
        {
            var builder = new ProceduralMeshBuilder();
            Color darkRoot = new Color(0.48f, 0.32f, 0.28f, 1f);
            Color boneCenter = new Color(0.86f, 0.78f, 0.66f, 1f);

            const int segs = 9;
            Vector3[] centers = new Vector3[segs];
            float[] radii = new float[segs];
            Color[] colors = new Color[segs];

            for (int i = 0; i < segs; i++)
            {
                float u = (i / (float)(segs - 1)) * 2f - 1f; // -1 .. +1
                float archY = 0.24f * (1f - u * u);
                centers[i] = new Vector3(u * 3.55f, archY, 0f);
                radii[i] = Mathf.Lerp(0.32f, 0.24f, Mathf.Abs(u));
                colors[i] = Color.Lerp(boneCenter, darkRoot, Mathf.Abs(u) * 0.75f);
            }

            builder.AddCurvedTube(centers, radii, colors, 6, capStart: true, capEnd: true);
            return builder.Build("Mesh_CathedralGate_UpperBeam");
        }

        public static Mesh CreateTendonStrandMesh()
        {
            var builder = new ProceduralMeshBuilder();
            Color anchorDark = new Color(0.56f, 0.06f, 0.08f, 1f);
            Color bellyBright = new Color(0.96f, 0.16f, 0.15f, 1f);

            const int rings = 9;
            Vector3[] centers = new Vector3[rings];
            float[] radii = new float[rings];
            Color[] colors = new Color[rings];

            for (int i = 0; i < rings; i++)
            {
                float t = i / (float)(rings - 1);
                centers[i] = new Vector3(0f, 0f, t);
                // Flared muscular insertions at both ends, sinewy belly in middle
                float flare = Mathf.Pow(Mathf.Abs(t - 0.5f) * 2f, 2.2f);
                float ripple = 0.012f * Mathf.Sin(t * Mathf.PI * 5f);
                radii[i] = 0.068f + 0.065f * flare + ripple;
                colors[i] = Color.Lerp(bellyBright, anchorDark, flare * 0.85f);
            }

            builder.AddCurvedTube(centers, radii, colors, 6, capStart: true, capEnd: true);
            return builder.Build("Mesh_CathedralGate_CrimsonTendon");
        }

        public static Mesh CreateAnchorBulbMesh()
        {
            var builder = new ProceduralMeshBuilder();
            Color rootDark = new Color(0.44f, 0.14f, 0.14f, 1f);
            Color centerFlesh = new Color(0.76f, 0.28f, 0.24f, 1f);
            Vector3[] centers =
            {
                new Vector3(0f, -0.50f, 0f),
                new Vector3(0f, -0.22f, 0f),
                new Vector3(0f,  0.00f, 0f),
                new Vector3(0f,  0.22f, 0f),
                new Vector3(0f,  0.50f, 0f),
            };
            float[] radii = { 0.14f, 0.44f, 0.52f, 0.44f, 0.14f };
            Color[] colors = { rootDark, centerFlesh, centerFlesh, centerFlesh, rootDark };
            builder.AddCurvedTube(centers, radii, colors, 6, capStart: true, capEnd: true);
            return builder.Build("Mesh_CathedralGate_AnchorBulb");
        }

        private sealed class ProceduralMeshBuilder
        {
            private readonly List<Vector3> _verts = new List<Vector3>(256);
            private readonly List<Vector3> _norms = new List<Vector3>(256);
            private readonly List<Vector2> _uvs = new List<Vector2>(256);
            private readonly List<Color> _cols = new List<Color>(256);
            private readonly List<int> _tris = new List<int>(512);

            public void AddTaperedSegment(
                Vector3 p0, Vector3 p1, float r0, float r1, int sides, Color c0, Color c1)
            {
                Vector3 mid = (p0 + p1) * 0.5f;
                float rMid = (r0 + r1) * 0.46f;
                Color cMid = Color.Lerp(c0, c1, 0.5f);
                AddCurvedTube(
                    new[] { p0, mid, p1 },
                    new[] { r0, rMid, r1 },
                    new[] { c0, cMid, c1 },
                    sides,
                    capStart: true,
                    capEnd: true);
            }

            public void AddCurvedTube(
                Vector3[] centers, float[] radii, Color[] colors, int sides,
                bool capStart, bool capEnd)
            {
                int rings = centers.Length;
                if (rings < 2 || sides < 3) return;

                int baseVert = _verts.Count;
                Vector3 prevTangent = (centers[1] - centers[0]).normalized;
                if (prevTangent.sqrMagnitude < 0.0001f) prevTangent = Vector3.forward;

                Vector3 refUp = Mathf.Abs(Vector3.Dot(prevTangent, Vector3.up)) > 0.92f
                    ? Vector3.forward
                    : Vector3.up;
                Vector3 normalAxis = Vector3.Cross(prevTangent, refUp).normalized;

                for (int r = 0; r < rings; r++)
                {
                    Vector3 tangent;
                    if (r == 0) tangent = centers[1] - centers[0];
                    else if (r == rings - 1) tangent = centers[r] - centers[r - 1];
                    else tangent = centers[r + 1] - centers[r - 1];

                    if (tangent.sqrMagnitude < 0.00001f) tangent = prevTangent;
                    else tangent.Normalize();

                    Quaternion rotAlign = Quaternion.FromToRotation(prevTangent, tangent);
                    normalAxis = (rotAlign * normalAxis).normalized;
                    Vector3 binormal = Vector3.Cross(tangent, normalAxis).normalized;
                    prevTangent = tangent;

                    float vCoord = r / (float)(rings - 1);
                    for (int s = 0; s <= sides; s++)
                    {
                        float uCoord = s / (float)sides;
                        float ang = uCoord * Mathf.PI * 2f;
                        Vector3 radial = normalAxis * Mathf.Cos(ang) + binormal * Mathf.Sin(ang);
                        _verts.Add(centers[r] + radial * radii[r]);
                        _norms.Add(radial);
                        _uvs.Add(new Vector2(uCoord, vCoord * 2f));
                        _cols.Add(colors[r]);
                    }
                }

                int stride = sides + 1;
                for (int r = 0; r < rings - 1; r++)
                {
                    for (int s = 0; s < sides; s++)
                    {
                        int a = baseVert + r * stride + s;
                        int b = baseVert + (r + 1) * stride + s;
                        int c = baseVert + (r + 1) * stride + (s + 1);
                        int d = baseVert + r * stride + (s + 1);

                        _tris.Add(a); _tris.Add(c); _tris.Add(b);
                        _tris.Add(a); _tris.Add(d); _tris.Add(c);
                    }
                }

                if (capStart)
                {
                    Vector3 capNorm = -(centers[1] - centers[0]).normalized;
                    int centerIdx = _verts.Count;
                    _verts.Add(centers[0]);
                    _norms.Add(capNorm);
                    _uvs.Add(new Vector2(0.5f, 0f));
                    _cols.Add(colors[0]);

                    for (int s = 0; s < sides; s++)
                    {
                        _tris.Add(centerIdx);
                        _tris.Add(baseVert + s);
                        _tris.Add(baseVert + s + 1);
                    }
                }

                if (capEnd)
                {
                    Vector3 capNorm = (centers[rings - 1] - centers[rings - 2]).normalized;
                    int lastRingStart = baseVert + (rings - 1) * stride;
                    int centerIdx = _verts.Count;
                    _verts.Add(centers[rings - 1]);
                    _norms.Add(capNorm);
                    _uvs.Add(new Vector2(0.5f, 1f));
                    _cols.Add(colors[rings - 1]);

                    for (int s = 0; s < sides; s++)
                    {
                        _tris.Add(centerIdx);
                        _tris.Add(lastRingStart + s + 1);
                        _tris.Add(lastRingStart + s);
                    }
                }
            }

            public Mesh Build(string meshName)
            {
                Mesh m = new Mesh { name = meshName };
                m.SetVertices(_verts);
                m.SetNormals(_norms);
                m.SetUVs(0, _uvs);
                m.SetColors(_cols);
                m.SetTriangles(_tris, 0);
                m.RecalculateBounds();
                return m;
            }
        }
    }
}
