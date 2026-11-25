using System.Collections;
using StomachAbyss.Combat;
using UnityEngine;

namespace MapaEstomago
{
    /// <summary>
    /// Controla a Espada Grande de Cruz com 3 Laminas de Barbear Flutuantes (Opcao C - Hibrido):
    /// - Corpo a Corpo (Segura na fibra direita):
    ///   * Com >= 1 lamina acoplada: combo de 3 golpes (Corte Dir->Esq, Corte Esq->Dir, Esmagamento Vertical Pesado).
    ///   * Com 0 laminas acopladas: Estocada curta com a cruz, causando dano e alcance reduzidos.
    /// - Modo Disparo (Segurar Botao Direito): desacopla da mao e levita a frente mirando para o cursor,
    ///   disparando 1 lamina por vez como bumerangue com retorno mais lento e peso tatico.
    /// - Game Feel: Hitstop, camera shake sutil, faiscas metalicas, pulso visual de recarga e audio procedural.
    /// </summary>
    public class CrossRazorGreatswordController : MonoBehaviour, IPlayerWeapon
    {
        private enum SwingMoveType
        {
            None = 0,
            SlashRightToLeft = 1,
            SlashLeftToRight = 2,
            OverheadSlam = 3,
            CrossThrust = 4
        }

        [Header("Referencia da Espada e Slots")]
        public Transform weaponMount;
        public Transform swordRoot;
        public Transform[] razorSlots = new Transform[3]; // 0 = Base, 1 = Mid, 2 = Tip
        public Material razorBladeMaterial;

        [Header("Ossos do Personagem (Modo Hibrido - Opcao C)")]
        public Transform coreBone;
        public Transform fiberR1;
        public Transform fiberR2;
        public Transform fiberRTip;
        public Transform fiberL1;
        public Transform fiberL2;

        [Header("Posicao / Pose")]
        public Vector3 handGripLocalOffset = new Vector3(0.04f, -0.02f, 0.08f);
        public Vector3 idleHandSwordEuler = new Vector3(28f, -14f, -24f);
        public Vector3 aimMountLocalPos = new Vector3(0.0f, 1.35f, 0.95f);
        public Vector3 aimSwordEuler = new Vector3(90f, 0f, 0f);
        public float bladeFloatAmplitude = 0.022f;
        public float bladeFloatSpeed = 2.8f;

        [Header("Ataque Corpo a Corpo (Combo 3 Golpes & Estocada Sem Laminas)")]
        public float slashSwingDuration = 0.44f;
        public float slamSwingDuration = 0.56f;
        public float thrustDuration = 0.28f;
        public float meleeCooldown = 0.56f;
        public float thrustCooldown = 0.46f;
        public float comboResetWindow = 0.90f;
        public float meleeBaseReach = 3.25f;
        public float meleeDamage = 34f;

        [Header("Disparo Longo Alcance (Bumerangue das 3 Laminas)")]
        public float shotCooldown = 0.32f;
        public float projectileSpeed = 20f;
        public float projectileReturnSpeed = 14.5f;
        public float projectileRange = 16.5f;
        public float projectileApexHover = 0.35f;
        public float emptyBladesRecallDuration = 2.5f;
        public float projectileDamage = 15f;

        [Header("Custo de Estamina")]
        public float meleeLightStaminaCost = 14f;
        public float meleeSlamStaminaCost = 20f;
        public float thrustStaminaCost = 10f;
        public float rangedShotStaminaCost = 16f;

        [Header("Rastro de Corte (Trail)")]
        public float meleeTrailDuration = 0.16f;
        public float meleeTrailWidth = 0.36f;

        [Header("Impacto e Game Feel")]
        public float hitstopSlashDuration = 0.062f;
        public float hitstopSlamDuration = 0.095f;
        public float cameraShakeIntensity = 0.14f;

        private readonly bool[] _bladeMounted = new bool[] { true, true, true };
        private readonly float[] _slotReloadFlash = new float[3];
        private Vector3[] _slotBaseLocalPos = new Vector3[3];
        private Mesh[] _slotMeshes = new Mesh[3];

        private Camera _cam;
        private SimplePlayerController _playerCtrl;
        private IsometricCameraFollow _isoCam;
        private TrailRenderer _meleeTrail;
        private Material _sharedTrailMaterial;
        private ParticleSystem _sparkParticles;
        private AudioSource _sfxSource;
        private AudioClip _clipHitClang;

        private bool _equippedToOwner;
        private bool _isAiming;
        private float _aimBlend;
        private float _emptyRecallLockTimer;

        private SwingMoveType _activeMove = SwingMoveType.None;
        private float _moveTimer;
        private float _moveDuration;
        private float _attackCooldownTimer;
        private float _shotCooldownTimer;
        private float _comboResetTimer;
        private int _comboStep;
        private bool _hitApplied;
        private bool _bufferedPrimaryAttack;

        public string WeaponDisplayName => "Espada Cruz";

        public string HudStatusText
        {
            get
            {
                int count = AvailableBlades;
                if (count <= 0)
                {
                    return _emptyRecallLockTimer > 0.05f
                        ? $"0/3 Laminas (ESTOCADA {_emptyRecallLockTimer:0.0}s)"
                        : "0/3 Laminas (ESTOCADA FRACA)";
                }
                return $"{count}/3 Laminas | Combo {_comboStep}/3";
            }
        }

        public bool ShouldDelayBladeReturn => AvailableBlades == 0 && _emptyRecallLockTimer > 0.9f;
        public float EmptyRecallRemaining => _emptyRecallLockTimer;

        public bool IsBusy => _activeMove != SwingMoveType.None;
        public bool IsAiming => _isAiming;
        public bool SuppressesDefaultClawAttack => true;
        public float MoveSpeedMultiplier => _isAiming ? 0.68f : (_activeMove == SwingMoveType.OverheadSlam ? 0.55f : 1f);

        public int AvailableBlades
        {
            get
            {
                int c = 0;
                for (int i = 0; i < _bladeMounted.Length; i++)
                {
                    if (_bladeMounted[i]) c++;
                }
                return c;
            }
        }

        private bool _runtimeInitialized;

        private void Awake()
        {
            EnsureRuntimeInitialized();
        }

        private void Start()
        {
            EnsureRuntimeInitialized();
        }

        private void OnEnable()
        {
            if (Application.isPlaying)
            {
                EnsureRuntimeInitialized();
            }
            if (_playerCtrl == null) _playerCtrl = GetComponent<SimplePlayerController>();
            if (_playerCtrl != null && _playerCtrl.ActiveWeapon == null)
            {
                _playerCtrl.SetActiveWeapon(this);
            }
        }

        public void EnsureRuntimeInitialized()
        {
            if (_runtimeInitialized && _meleeTrail != null && _slotMeshes[0] != null) return;

            if (_cam == null) _cam = Camera.main;
            if (_cam == null) _cam = FindAnyObjectByType<Camera>();
            if (_isoCam == null) _isoCam = FindAnyObjectByType<IsometricCameraFollow>();
            if (_playerCtrl == null) _playerCtrl = GetComponent<SimplePlayerController>();

            ResolveSwordHierarchy();
            ResolveVirusBones();
            CleanupLegacyAimGuideLine();
            EnsureSlashTrail();
            EnsureSparkEffects();
            if (_clipHitClang == null) EnsureProceduralSfx();
            CombatSfxBank.EnsureExists();
            CombatHitstopManager.EnsureExists();
            BloodSplatterSystem.EnsureExists();
            _runtimeInitialized = true;
        }

        private void OnDisable()
        {
            RestoreTimeScaleSafely();
        }

        private void OnDestroy()
        {
            RestoreTimeScaleSafely();
        }

        private void RestoreTimeScaleSafely()
        {
            if (Mathf.Abs(Time.timeScale - 1f) > 0.001f)
            {
                Time.timeScale = 1f;
            }
        }

        public void OnWeaponEquipped(SimplePlayerController owner)
        {
            _playerCtrl = owner;
            _equippedToOwner = true;
            EnsureRuntimeInitialized();
            ResolveVirusBones();
            if (swordRoot != null && !swordRoot.gameObject.activeSelf)
            {
                swordRoot.gameObject.SetActive(true);
            }
        }

        public void OnWeaponUnequipped()
        {
            _equippedToOwner = false;
            CancelActiveAttack();
            SetAimingState(false);
            _aimBlend = 0f;
            if (_meleeTrail != null)
            {
                _meleeTrail.emitting = false;
                _meleeTrail.Clear();
            }
            if (swordRoot != null && swordRoot.gameObject.activeSelf)
            {
                swordRoot.gameObject.SetActive(false);
            }
        }

        public void OnPrimaryAttackPressed()
        {
            if (_playerCtrl != null && (_playerCtrl.IsDead || _playerCtrl.IsDodging)) return;

            if (_isAiming)
            {
                if (_shotCooldownTimer <= 0f)
                {
                    TryFireNextBlade();
                }
                return;
            }

            if (_activeMove != SwingMoveType.None)
            {
                // Buffer do proximo golpe do combo se clicado nos ultimos 45% do ataque atual
                float progress = 1f - Mathf.Clamp01(_moveTimer / Mathf.Max(0.01f, _moveDuration));
                if (progress >= 0.45f)
                {
                    _bufferedPrimaryAttack = true;
                }
                return;
            }

            if (_attackCooldownTimer <= 0f)
            {
                TriggerNextMeleeAttack();
            }
        }

        public void OnSecondaryAimState(bool held, bool justReleased)
        {
            if (_playerCtrl != null && _playerCtrl.IsDead)
            {
                SetAimingState(false);
                return;
            }

            if (held)
            {
                if (!_isAiming)
                {
                    SetAimingState(true);
                    // Interrompe combo ao entrar rapidamente no modo de mira
                    if (_activeMove != SwingMoveType.None)
                    {
                        CancelActiveAttack();
                    }
                }
            }
            else
            {
                // Soltar o botao direito apenas sai da mira — disparo acontece somente ao apertar o botao esquerdo mirando
                SetAimingState(false);
            }
        }

        public void CancelActiveAttack()
        {
            _activeMove = SwingMoveType.None;
            _moveTimer = 0f;
            _hitApplied = false;
            _bufferedPrimaryAttack = false;
            if (_meleeTrail != null) _meleeTrail.emitting = false;
        }

        public void ResolveSwordHierarchy()
        {
            if (weaponMount == null)
            {
                weaponMount = transform.Find("Weapon_Mount_Pivot");
            }
            if (swordRoot == null && weaponMount != null)
            {
                swordRoot = weaponMount.Find("Cross_Razor_Greatsword");
                if (swordRoot == null && weaponMount.childCount > 0)
                {
                    Transform candidate = weaponMount.GetChild(0);
                    if (candidate != null && !candidate.name.Contains("Tenia") && !candidate.name.Contains("Lamina"))
                    {
                        swordRoot = candidate;
                    }
                }
            }
            if (swordRoot == null)
            {
                Transform found = transform.Find("Cross_Razor_Greatsword");
                if (found != null) swordRoot = found;
            }

            if (swordRoot != null)
            {
                string[] slotNames = { "Razor_01_Base", "Razor_02_Mid", "Razor_03_Tip" };
                for (int i = 0; i < 3; i++)
                {
                    if (razorSlots[i] == null)
                    {
                        razorSlots[i] = swordRoot.Find(slotNames[i]);
                    }
                    if (razorSlots[i] != null)
                    {
                        _slotBaseLocalPos[i] = razorSlots[i].localPosition;
                        MeshFilter mf = razorSlots[i].GetComponent<MeshFilter>();
                        if (mf != null) _slotMeshes[i] = mf.sharedMesh;
                        if (razorBladeMaterial == null)
                        {
                            MeshRenderer mr = razorSlots[i].GetComponent<MeshRenderer>();
                            if (mr != null) razorBladeMaterial = mr.sharedMaterial;
                        }
                    }
                }
            }
        }

        private void ResolveVirusBones()
        {
            Transform modelRoot = _playerCtrl != null && _playerCtrl.visualRoot != null
                ? _playerCtrl.visualRoot
                : transform.Find("Virus_Model");
            if (modelRoot == null) return;

            if (coreBone == null) coreBone = modelRoot.Find("Root/Core");
            if (coreBone != null)
            {
                if (fiberR1 == null) fiberR1 = coreBone.Find("Fiber_R_1");
                if (fiberR1 != null && fiberR2 == null) fiberR2 = fiberR1.Find("Fiber_R_2");
                if (fiberR2 != null && fiberRTip == null) fiberRTip = fiberR2.Find("Fiber_R_Tip");

                if (fiberL1 == null) fiberL1 = coreBone.Find("Fiber_L_1");
                if (fiberL1 != null && fiberL2 == null) fiberL2 = fiberL1.Find("Fiber_L_2");
            }
        }

        private void CleanupLegacyAimGuideLine()
        {
            Transform legacy = transform.Find("Sword_Aim_GuideLine");
            if (legacy != null)
            {
                Destroy(legacy.gameObject);
            }
        }

        private void EnsureSlashTrail()
        {
            if (swordRoot == null) return;
            Transform existing = swordRoot.Find("Sword_Swing_Trail");
            if (existing != null)
            {
                _meleeTrail = existing.GetComponent<TrailRenderer>();
            }
            if (_meleeTrail == null)
            {
                GameObject go = new GameObject("Sword_Swing_Trail");
                go.transform.SetParent(swordRoot, false);
                go.transform.localPosition = new Vector3(0f, 1.45f, 0f);
                _meleeTrail = go.AddComponent<TrailRenderer>();
            }
            _sharedTrailMaterial = CreateOrReuseTrailMaterial();
            ConfigureBladeTrail(_meleeTrail, meleeTrailDuration, meleeTrailWidth, _sharedTrailMaterial);
            _meleeTrail.emitting = false;
        }

        public static Material CreateOrReuseTrailMaterial()
        {
            Shader unlit = Shader.Find("Universal Render Pipeline/Unlit");
            if (unlit == null) unlit = Shader.Find("Sprites/Default");
            Material mat = unlit != null ? new Material(unlit) : null;
            if (mat != null)
            {
                mat.color = Color.white;
            }
            return mat;
        }

        public static void ConfigureBladeTrail(TrailRenderer trail, float time, float startWidth, Material mat)
        {
            if (trail == null) return;
            trail.time = time;
            trail.minVertexDistance = 0.06f;
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            trail.receiveShadows = false;
            trail.alignment = LineAlignment.View;
            trail.widthCurve = new AnimationCurve(
                new Keyframe(0f, startWidth),
                new Keyframe(0.45f, startWidth * 0.55f),
                new Keyframe(1f, 0f)
            );

            Gradient grad = new Gradient();
            grad.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(0.95f, 0.98f, 1.0f), 0f),
                    new GradientColorKey(new Color(0.45f, 0.88f, 1.0f), 0.45f),
                    new GradientColorKey(new Color(0.25f, 0.45f, 0.85f), 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0.92f, 0f),
                    new GradientAlphaKey(0.50f, 0.5f),
                    new GradientAlphaKey(0f, 1f)
                }
            );
            trail.colorGradient = grad;
            if (mat != null) trail.sharedMaterial = mat;
        }

        private void EnsureSparkEffects()
        {
            Transform existing = transform.Find("Sword_Impact_SparksFX");
            if (existing != null)
            {
                _sparkParticles = existing.GetComponent<ParticleSystem>();
            }
            if (_sparkParticles == null)
            {
                GameObject go = new GameObject("Sword_Impact_SparksFX");
                go.transform.SetParent(transform, false);
                _sparkParticles = go.AddComponent<ParticleSystem>();

                var main = _sparkParticles.main;
                main.playOnAwake = false;
                main.loop = false;
                main.duration = 0.35f;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.14f, 0.30f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(5.5f, 11.0f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.14f);
                main.gravityModifier = 1.2f;
                main.startColor = new ParticleSystem.MinMaxGradient(
                    new Color(1.0f, 0.90f, 0.52f, 1f),
                    new Color(0.85f, 0.95f, 1.0f, 1f)
                );
                main.simulationSpace = ParticleSystemSimulationSpace.World;

                var emission = _sparkParticles.emission;
                emission.enabled = false;

                var shape = _sparkParticles.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Sphere;
                shape.radius = 0.25f;

                var renderer = go.GetComponent<ParticleSystemRenderer>();
                Shader sh = Shader.Find("MapaEstomago/URP/PS1_PixelParticle");
                if (sh == null) sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
                if (sh != null && renderer != null)
                {
                    Material mat = new Material(sh);
                    mat.SetColor("_BaseColor", new Color(1.0f, 0.92f, 0.60f, 1f));
                    mat.SetFloat("_EmissionBoost", 2.8f);
                    renderer.sharedMaterial = mat;
                }
            }
        }

        private void EnsureProceduralSfx()
        {
            _sfxSource = GetComponent<AudioSource>();
            if (_sfxSource == null)
            {
                _sfxSource = gameObject.AddComponent<AudioSource>();
                _sfxSource.playOnAwake = false;
                _sfxSource.spatialBlend = 0f;
                _sfxSource.volume = 0.65f;
            }

            _clipHitClang = CreateMetallicClip("SFX_RazorClang", 0.18f, 760f, 1240f, 0.58f);
        }

        private static AudioClip CreateMetallicClip(string name, float dur, float f1, float f2, float amp)
        {
            const int sr = 22050;
            int len = Mathf.Max(64, Mathf.RoundToInt(sr * dur));
            float[] data = new float[len];
            for (int i = 0; i < len; i++)
            {
                float t = (float)i / sr;
                float norm = (float)i / len;
                float env = Mathf.Exp(-norm * 6.5f);
                float sig = Mathf.Sin(2f * Mathf.PI * f1 * t) * 0.55f +
                            Mathf.Sin(2f * Mathf.PI * f2 * t) * 0.45f;
                data[i] = sig * env * amp;
            }
            AudioClip clip = AudioClip.Create(name, len, 1, sr, false);
            clip.SetData(data, 0);
            return clip;
        }

        private void Update()
        {
            if (!_runtimeInitialized) EnsureRuntimeInitialized();

            if (_playerCtrl != null && _playerCtrl.IsDead)
            {
                SetAimingState(false);
                CancelActiveAttack();
                return;
            }

            float dt = Time.deltaTime;
            if (_attackCooldownTimer > 0f) _attackCooldownTimer -= dt;
            if (_shotCooldownTimer > 0f) _shotCooldownTimer -= dt;
            if (_emptyRecallLockTimer > 0f) _emptyRecallLockTimer = Mathf.Max(0f, _emptyRecallLockTimer - dt);

            for (int i = 0; i < 3; i++)
            {
                if (_slotReloadFlash[i] > 0f)
                {
                    _slotReloadFlash[i] = Mathf.Max(0f, _slotReloadFlash[i] - dt * 3.5f);
                }
            }

            if (_activeMove != SwingMoveType.None)
            {
                _moveTimer -= dt;
                float progress = 1f - Mathf.Clamp01(_moveTimer / Mathf.Max(0.01f, _moveDuration));

                float hitPoint = _activeMove == SwingMoveType.OverheadSlam ? 0.50f :
                                 (_activeMove == SwingMoveType.CrossThrust ? 0.36f : 0.40f);
                if (!_hitApplied && progress >= hitPoint)
                {
                    _hitApplied = true;
                    ExecuteActiveMeleeHitbox();
                }

                if (_moveTimer <= 0f)
                {
                    _activeMove = SwingMoveType.None;
                    _comboResetTimer = comboResetWindow;

                    if (_bufferedPrimaryAttack && !_isAiming && _attackCooldownTimer <= 0f)
                    {
                        _bufferedPrimaryAttack = false;
                        TriggerNextMeleeAttack();
                    }
                    else
                    {
                        _bufferedPrimaryAttack = false;
                    }
                }
            }
            else if (_comboResetTimer > 0f)
            {
                _comboResetTimer -= dt;
                if (_comboResetTimer <= 0f)
                {
                    _comboStep = 0;
                }
            }

            if (_isAiming)
            {
                AimCharacterTowardCursor(dt);
            }

            // Fallback apenas se este componente estiver sozinho sem SimplePlayerController
            if (!_equippedToOwner && _playerCtrl == null)
            {
                UpdateStandaloneFallback();
            }
        }

        private void LateUpdate()
        {
            if (_playerCtrl != null && _playerCtrl.IsDead) return;
            float dt = Time.deltaTime;

            // Opcao C (Hibrido): durante a mira, a espada levita a frente; fora da mira, fica na mao direita
            float targetAimBlend = _isAiming ? 1f : 0f;
            _aimBlend = Mathf.MoveTowards(_aimBlend, targetAimBlend, dt * 8.5f);

            UpdateCharacterHybridArmPose(dt);
            UpdateSwordMountAndVisuals(dt);
        }

        private void UpdateStandaloneFallback()
        {
#if ENABLE_INPUT_SYSTEM
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse != null)
            {
                OnSecondaryAimState(mouse.rightButton.isPressed, mouse.rightButton.wasReleasedThisFrame);
                if (mouse.leftButton.wasPressedThisFrame) OnPrimaryAttackPressed();
            }
#endif
        }

        private void TriggerNextMeleeAttack()
        {
            EnsureRuntimeInitialized();
            if (AvailableBlades <= 0)
            {
                if (_playerCtrl != null && !_playerCtrl.TryConsumeActionStamina(thrustStaminaCost)) return;
                // Sem laminas: Estocada rapida e mais fraca com a haste da cruz
                _activeMove = SwingMoveType.CrossThrust;
                _moveDuration = thrustDuration;
                _moveTimer = thrustDuration;
                _attackCooldownTimer = thrustCooldown;
                _comboStep = 0;
                _hitApplied = false;

                _playerCtrl?.ApplyAttackMovementSlow(0.32f, _moveDuration * 0.85f);
                _playerCtrl?.ApplyCombatLunge(transform.forward, 6.2f, 0.14f);
                CombatSfxBank.Play(CombatSfxId.CrossThrust, 0.86f, 0.96f, 1.08f, 0.04f);
                return;
            }

            // Combo de 3 golpes com as laminas acopladas
            int nextStep = (_comboStep % 3) + 1;
            float meleeCost = nextStep == 3 ? meleeSlamStaminaCost : meleeLightStaminaCost;
            if (_playerCtrl != null && !_playerCtrl.TryConsumeActionStamina(meleeCost)) return;

            _comboStep = nextStep;
            switch (_comboStep)
            {
                case 1:
                    _activeMove = SwingMoveType.SlashRightToLeft;
                    _moveDuration = slashSwingDuration;
                    _attackCooldownTimer = meleeCooldown;
                    _playerCtrl?.ApplyCombatLunge(transform.forward, 5.2f, 0.16f);
                    CombatSfxBank.Play(CombatSfxId.SwordSlashLight, 0.88f, 0.96f, 1.04f, 0.04f);
                    break;
                case 2:
                    _activeMove = SwingMoveType.SlashLeftToRight;
                    _moveDuration = slashSwingDuration * 0.94f;
                    _attackCooldownTimer = meleeCooldown * 0.95f;
                    _playerCtrl?.ApplyCombatLunge(transform.forward, 5.6f, 0.16f);
                    CombatSfxBank.Play(CombatSfxId.SwordSlashLight, 0.92f, 1.06f, 1.15f, 0.04f);
                    break;
                default:
                    _activeMove = SwingMoveType.OverheadSlam;
                    _moveDuration = slamSwingDuration;
                    _attackCooldownTimer = meleeCooldown + 0.18f;
                    _playerCtrl?.ApplyCombatLunge(transform.forward, 7.0f, 0.22f);
                    CombatSfxBank.Play(CombatSfxId.SwordSlamHeavy, 0.96f, 0.94f, 1.02f, 0.04f);
                    break;
            }

            _playerCtrl?.ApplyAttackMovementSlow(0.32f, _moveDuration * 0.85f);
            _moveTimer = _moveDuration;
            _hitApplied = false;
            _comboResetTimer = comboResetWindow;

            if (_meleeTrail != null)
            {
                // Posiciona o rastro na ponta da lamina mais externa acoplada
                int outerSlot = -1;
                for (int i = 2; i >= 0; i--)
                {
                    if (_bladeMounted[i] && razorSlots[i] != null) { outerSlot = i; break; }
                }
                if (outerSlot >= 0)
                {
                    _meleeTrail.transform.localPosition = _slotBaseLocalPos[outerSlot] + new Vector3(0f, 0.22f, 0f);
                }
                _meleeTrail.startWidth = _activeMove == SwingMoveType.OverheadSlam ? meleeTrailWidth * 1.32f : meleeTrailWidth;
                _meleeTrail.Clear();
                _meleeTrail.emitting = false;
            }
        }

        public bool TryFireNextBlade()
        {
            EnsureRuntimeInitialized();

            // Dispara da ponta para a base: Tip (2) -> Mid (1) -> Base (0)
            int slotToFire = -1;
            for (int i = 2; i >= 0; i--)
            {
                if (_bladeMounted[i] && razorSlots[i] != null && _slotMeshes[i] != null)
                {
                    slotToFire = i;
                    break;
                }
            }

            if (slotToFire < 0) return false;
            if (_playerCtrl != null && !_playerCtrl.TryConsumeActionStamina(rangedShotStaminaCost)) return false;

            _bladeMounted[slotToFire] = false;
            _shotCooldownTimer = shotCooldown;

            Transform slotTr = razorSlots[slotToFire];
            Vector3 playerOrigin = transform.position + Vector3.up * 1.25f;
            bool hasCursorTarget = TryGetCursorWorldTarget(playerOrigin.y, out Vector3 cursorTarget);

            Vector3 toTargetFromPlayer = hasCursorTarget ? (cursorTarget - playerOrigin) : transform.forward * 10f;
            Vector3 flatAim = new Vector3(toTargetFromPlayer.x, 0f, toTargetFromPlayer.z);
            float planarDistFromPlayer = flatAim.magnitude;
            if (planarDistFromPlayer > 0.05f)
            {
                flatAim /= planarDistFromPlayer;
            }
            else
            {
                flatAim = transform.forward;
            }

            // Alinha imediatamente para o ponteiro no instante do tiro
            transform.rotation = Quaternion.LookRotation(flatAim, Vector3.up);

            // Se o cursor estiver proximo ao personagem (< 3.2m), lanca a partir do pivo frontal para nunca atirar para tras da ponta da espada
            Vector3 spawnPos = (_aimBlend >= 0.7f && planarDistFromPlayer > 3.2f)
                ? slotTr.position
                : (playerOrigin + flatAim * 0.95f);

            Vector3 fireDir;
            if (hasCursorTarget)
            {
                Vector3 toCursor = planarDistFromPlayer > 3.2f
                    ? (cursorTarget - spawnPos)
                    : toTargetFromPlayer;
                float planarDist = new Vector2(toCursor.x, toCursor.z).magnitude;
                if (planarDist > 0.15f)
                {
                    float maxVert = planarDist * 0.45f;
                    toCursor.y = Mathf.Clamp(toCursor.y, -maxVert, maxVert);
                    fireDir = toCursor.normalized;
                }
                else
                {
                    fireDir = flatAim;
                }
            }
            else
            {
                fireDir = flatAim;
            }

            Vector3 worldScale = slotTr.lossyScale;

            slotTr.gameObject.SetActive(false);

            GameObject projGo = new GameObject($"Razor_Boomerang_Slot{slotToFire}");
            CrossRazorBoomerangProjectile proj = projGo.AddComponent<CrossRazorBoomerangProjectile>();
            proj.Launch(
                this,
                transform,
                slotToFire,
                spawnPos,
                fireDir,
                _slotMeshes[slotToFire],
                razorBladeMaterial,
                worldScale,
                projectileSpeed,
                projectileReturnSpeed,
                projectileRange,
                projectileDamage * StomachAbyss.Progression.BiomassProgressionSystem.GetDamageMultiplier(),
                projectileApexHover
            );

            CombatSfxBank.Play(CombatSfxId.RazorThrow, 0.82f, 0.94f + slotToFire * 0.06f, 1.02f + slotToFire * 0.06f, 0.04f);
            _playerCtrl?.EmitCombatParticles(spawnPos, Quaternion.LookRotation(fireDir, Vector3.up), 8);

            // Se acabou de ficar sem nenhuma lamina acoplada, ativa a espera longa (~5s) para o retorno
            if (AvailableBlades == 0)
            {
                _emptyRecallLockTimer = emptyBladesRecallDuration;
            }
            return true;
        }

        public Vector3 GetSlotWorldPosition(int slotIndex)
        {
            if (slotIndex >= 0 && slotIndex < 3 && razorSlots[slotIndex] != null && swordRoot != null)
            {
                return swordRoot.TransformPoint(_slotBaseLocalPos[slotIndex]);
            }
            return transform.position + Vector3.up * 1.3f;
        }

        public void ReturnBladeToSlot(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= 3) return;
            _bladeMounted[slotIndex] = true;
            _slotReloadFlash[slotIndex] = 1f;

            if (razorSlots[slotIndex] != null)
            {
                razorSlots[slotIndex].gameObject.SetActive(true);
            }

            Vector3 slotPos = GetSlotWorldPosition(slotIndex);
            EmitSparksAt(slotPos, 10);
            CombatSfxBank.Play(CombatSfxId.RazorReturnLock, 0.86f, 0.95f + slotIndex * 0.05f, 1.01f + slotIndex * 0.05f, 0.065f);

            // Feedback visual e sonoro marcante quando TODAS as 3 laminas estao travadas de volta na espada
            if (AvailableBlades == 3)
            {
                for (int i = 0; i < 3; i++)
                {
                    _slotReloadFlash[i] = 1.35f;
                    EmitSparksAt(GetSlotWorldPosition(i), 12);
                }
                _playerCtrl?.TriggerWeaponFlash(new Color(0.55f, 2.2f, 2.6f, 1f), 0.28f, 2.35f);
                CombatSfxBank.Play(CombatSfxId.RazorReturnLock, 0.98f, 1.24f, 1.32f, 0.04f);
                PlaySfx(_clipHitClang, 0.34f, 1.38f);
                _isoCam?.TriggerShake(cameraShakeIntensity * 0.45f, 0.11f);
            }
            else
            {
                _playerCtrl?.TriggerWeaponFlash(new Color(0.85f, 1.4f, 1.9f, 1f), 0.12f, 1.6f);
            }
        }

        public void NotifyBladeImpact(Vector3 worldPos, bool damagedTarget)
        {
            EmitSparksAt(worldPos, damagedTarget ? 14 : 7);
            PlaySfx(_clipHitClang, damagedTarget ? 0.26f : 0.34f, UnityEngine.Random.Range(0.95f, 1.15f));
            if (damagedTarget && _isoCam != null)
            {
                _isoCam.TriggerShake(cameraShakeIntensity * 0.45f, 0.09f);
            }
        }

        private void ExecuteActiveMeleeHitbox()
        {
            int mountedCount = AvailableBlades;
            bool isThrust = _activeMove == SwingMoveType.CrossThrust;
            bool isSlam = _activeMove == SwingMoveType.OverheadSlam;

            float reach;
            float radius;
            float dmg;

            if (isThrust)
            {
                // Estocada com a cruz sem laminas: menor alcance, raio estreito e menos dano
                reach = 1.95f;
                radius = 0.85f;
                dmg = meleeDamage * 0.35f;
            }
            else
            {
                float bladeScale = mountedCount == 3 ? 1.12f : (mountedCount == 2 ? 0.94f : 0.76f);
                float comboMult = isSlam ? 1.35f : (_activeMove == SwingMoveType.SlashLeftToRight ? 1.06f : 1.0f);
                reach = meleeBaseReach * bladeScale * (isSlam ? 1.14f : 1.0f);
                radius = reach * (isSlam ? 0.62f : 0.68f);
                dmg = meleeDamage * bladeScale * comboMult;
            }

            dmg *= StomachAbyss.Progression.BiomassProgressionSystem.GetDamageMultiplier();

            Vector3 hitCenter = transform.position + transform.forward * (reach * 0.56f) + Vector3.up * (isSlam ? 0.65f : 0.95f);
            Vector3 swingDir;
            switch (_activeMove)
            {
                case SwingMoveType.SlashRightToLeft:
                    swingDir = (-transform.right + transform.forward * 0.45f).normalized;
                    break;
                case SwingMoveType.SlashLeftToRight:
                    swingDir = (transform.right + transform.forward * 0.45f).normalized;
                    break;
                case SwingMoveType.OverheadSlam:
                    swingDir = (transform.forward * 0.82f + Vector3.up * 0.25f).normalized;
                    break;
                default:
                    swingDir = transform.forward;
                    break;
            }

            bool hitAnything = false;
            bool hitDamageable = false;

            if (isSlam)
            {
                Vector3 slamPos = transform.position + transform.forward * (reach * 0.75f);
                EmitSparksAt(slamPos + Vector3.up * 0.25f, 18);
                _playerCtrl?.EmitCombatParticles(hitCenter, transform.rotation, 14);
                CombatCameraJuice.Shake(0.48f, 0.24f);
                CombatCameraJuice.PunchFov(-2.8f, 0.22f);
                CombatCameraJuice.SpawnSlamShockwave(slamPos, 2.8f);
            }

            Collider[] cols = Physics.OverlapSphere(hitCenter, radius);
            if (cols != null)
            {
                for (int i = 0; i < cols.Length; i++)
                {
                    Collider c = cols[i];
                    if (c == null || c.transform.IsChildOf(transform)) continue;

                    OrganPulse op = c.GetComponentInParent<OrganPulse>();
                    if (op != null)
                    {
                        op.TriggerHitRecoil();
                        hitAnything = true;
                        EmitSparksAt(c.ClosestPoint(hitCenter), isSlam ? 16 : 10);
                    }

                    IAcidDamageable d = c.GetComponentInParent<IAcidDamageable>();
                    if (d != null && !ReferenceEquals(d, _playerCtrl))
                    {
                        Vector3 impactPt = c.ClosestPoint(hitCenter);
                        float bloodIntensity = isSlam ? 1.45f : (isThrust ? 0.65f : 1.05f);
                        BloodSplatterSystem.BeginAttackBurstContext(swingDir, impactPt, bloodIntensity, isSlam);
                        CombatSfxBank.BeginImpactScope(isSlam);
                        try
                        {
                            d.TakeAcidDamage(dmg);
                        }
                        finally
                        {
                            if (!BloodSplatterSystem.WasEntityHandledInContext)
                            {
                                BloodSplatterSystem.SpawnBloodBurst(impactPt, swingDir, bloodIntensity, null, isSlam);
                            }
                            if (!CombatSfxBank.WasImpactClaimedInScope)
                            {
                                CombatSfxBank.PlayImpact(isSlam, impactPt, isSlam ? 0.92f : 0.82f);
                            }
                            BloodSplatterSystem.EndAttackBurstContext();
                            CombatSfxBank.EndImpactScope(isSlam);
                        }

                        hitDamageable = true;
                        hitAnything = true;
                        EmitSparksAt(impactPt, isSlam ? 18 : 12);
                    }
                }
            }

            // Interacao direta com bolhas, ulceras e portas viscerais do estomago
            AcidBubblePopper[] bubbles = FindObjectsByType<AcidBubblePopper>(FindObjectsSortMode.None);
            foreach (AcidBubblePopper b in bubbles)
            {
                if (b != null && Vector3.Distance(hitCenter, b.transform.position) <= radius + 0.65f)
                {
                    b.PopByAttack();
                    hitAnything = true;
                    EmitSparksAt(b.transform.position, 10);
                }
            }

            UlcerEruption[] ulcers = FindObjectsByType<UlcerEruption>(FindObjectsSortMode.None);
            foreach (UlcerEruption u in ulcers)
            {
                if (u != null && Vector3.Distance(hitCenter, u.transform.position) <= radius + 1.0f)
                {
                    u.TriggerEruption();
                    hitAnything = true;
                    EmitSparksAt(u.transform.position, 10);
                }
            }

            FleshEyeDoorController[] doors = FindObjectsByType<FleshEyeDoorController>(FindObjectsSortMode.None);
            foreach (FleshEyeDoorController door in doors)
            {
                if (door != null && Vector3.Distance(hitCenter, door.transform.position) <= radius + 1.5f)
                {
                    door.EmitMouthBurst(12, new Color(0.85f, 0.08f, 0.12f, 1f), 4.0f);
                    hitAnything = true;
                }
            }

            if (hitAnything || isSlam)
            {
                if (hitAnything)
                {
                    float clangVol = hitDamageable ? (isSlam ? 0.34f : 0.24f) : (isSlam ? 0.72f : 0.52f);
                    PlaySfx(_clipHitClang, clangVol, isThrust ? 0.88f : 1.02f);
                    float hitstopDur = isSlam ? Mathf.Max(hitstopSlamDuration, 0.095f) : (isThrust ? 0.026f : Mathf.Max(hitstopSlashDuration, 0.062f));
                    CombatHitstopManager.TriggerHitstop(hitstopDur, 0.035f, isSlam);
                    if (!isSlam)
                    {
                        CombatCameraJuice.Shake(isThrust ? 0.16f : 0.26f, isThrust ? 0.10f : 0.14f);
                    }
                }
                if (_isoCam != null)
                {
                    float shake = isSlam ? cameraShakeIntensity * 1.35f : (isThrust ? cameraShakeIntensity * 0.45f : cameraShakeIntensity);
                    _isoCam.TriggerShake(shake, isSlam ? 0.18f : 0.11f);
                }
            }
        }

        private void EmitSparksAt(Vector3 worldPos, int count)
        {
            if (_sparkParticles == null) return;
            _sparkParticles.transform.position = worldPos;
            _sparkParticles.Emit(count);
        }

        private void PlaySfx(AudioClip clip, float volume, float pitch)
        {
            if (_sfxSource == null || clip == null) return;
            _sfxSource.pitch = pitch;
            _sfxSource.PlayOneShot(clip, volume);
        }

        private bool TryGetCursorWorldTarget(float defaultPlaneY, out Vector3 targetPoint)
        {
            targetPoint = transform.position + transform.forward * 10f;
            if (_cam == null) _cam = Camera.main;
            if (_cam == null) _cam = FindAnyObjectByType<Camera>();
            if (_cam == null) return false;

            Vector2 screenPos = Vector2.zero;
#if ENABLE_INPUT_SYSTEM
            if (UnityEngine.InputSystem.Mouse.current != null)
            {
                screenPos = UnityEngine.InputSystem.Mouse.current.position.ReadValue();
            }
#else
            screenPos = Input.mousePosition;
#endif
            Ray ray = _cam.ScreenPointToRay(screenPos);

            // 1. Apenas alvos reais de combate/interacao (ignora paredes invisiveis BG3, tetos, arcos e colunas do cenario)
            RaycastHit[] hits = Physics.RaycastAll(ray, 160f, ~0, QueryTriggerInteraction.Collide);
            float bestDist = float.MaxValue;
            RaycastHit bestHit = default;
            bool foundDirectTarget = false;
            for (int i = 0; i < hits.Length; i++)
            {
                Collider c = hits[i].collider;
                if (c == null || c.transform.IsChildOf(transform)) continue;

                bool isCombatTarget = c.GetComponentInParent<IAcidDamageable>() != null ||
                                      c.GetComponentInParent<AcidBubblePopper>() != null ||
                                      c.GetComponentInParent<UlcerEruption>() != null ||
                                      c.GetComponentInParent<OrganPulse>() != null;
                if (!isCombatTarget) continue;

                if (hits[i].distance < bestDist)
                {
                    bestDist = hits[i].distance;
                    bestHit = hits[i];
                    foundDirectTarget = true;
                }
            }

            if (foundDirectTarget)
            {
                targetPoint = bestHit.point;
                return true;
            }

            // 2. Plano horizontal limpo na altura do tronco/lamina: garante mira 360 graus suave na direcao do mouse
            Plane p = new Plane(Vector3.up, new Vector3(0f, defaultPlaneY, 0f));
            if (p.Raycast(ray, out float enter))
            {
                targetPoint = ray.GetPoint(enter);
                return true;
            }
            return false;
        }

        private Vector3 GetAimDirectionOnPlane()
        {
            if (TryGetCursorWorldTarget(transform.position.y + 1.1f, out Vector3 pt))
            {
                Vector3 dir = pt - transform.position;
                dir.y = 0f;
                if (dir.sqrMagnitude > 0.01f) return dir.normalized;
            }
            return transform.forward;
        }

        private void AimCharacterTowardCursor(float dt)
        {
            Vector3 aimDir = GetAimDirectionOnPlane();
            Quaternion targetRot = Quaternion.LookRotation(aimDir, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, 34f * dt);
        }

        private void SetAimingState(bool aim)
        {
            _isAiming = aim;
        }

        private void UpdateCharacterHybridArmPose(float dt)
        {
            if (coreBone == null || fiberR1 == null) return;
            if (_playerCtrl != null && _playerCtrl.IsDodging) return;

            Vector3 coreEulerOffset = Vector3.zero;
            Vector3 r1EulerTarget = new Vector3(-42f, 0f, 24f); // Guarda natural com espada na mao
            Vector3 r2EulerTarget = new Vector3(-32f, 0f, 0f);
            Vector3 l1EulerOffset = Vector3.zero;
            float rWeight = 0.85f;
            float lWeight = 0f;

            if (_activeMove != SwingMoveType.None)
            {
                float p = 1f - Mathf.Clamp01(_moveTimer / Mathf.Max(0.01f, _moveDuration));
                rWeight = 0.98f;
                lWeight = 0.35f;

                switch (_activeMove)
                {
                    case SwingMoveType.SlashRightToLeft:
                    {
                        float s = EvaluateHeavySwingCurve(p);
                        coreEulerOffset = new Vector3(Mathf.Lerp(-6f, 10f, s), Mathf.Lerp(26f, -34f, s), 0f);
                        r1EulerTarget = Vector3.Lerp(new Vector3(-74f, 0f, 64f), new Vector3(-84f, 0f, -52f), s);
                        r2EulerTarget = new Vector3(-16f, 0f, 0f);
                        l1EulerOffset = new Vector3(14f, 0f, -18f);
                        break;
                    }
                    case SwingMoveType.SlashLeftToRight:
                    {
                        float s = EvaluateHeavySwingCurve(p);
                        coreEulerOffset = new Vector3(Mathf.Lerp(10f, -4f, s), Mathf.Lerp(-32f, 34f, s), 0f);
                        r1EulerTarget = Vector3.Lerp(new Vector3(-82f, 0f, -48f), new Vector3(-76f, 0f, 66f), s);
                        r2EulerTarget = new Vector3(-14f, 0f, 0f);
                        l1EulerOffset = new Vector3(12f, 0f, -15f);
                        break;
                    }
                    case SwingMoveType.OverheadSlam:
                    {
                        float heavy = EvaluateHeavySwingCurve(p);
                        if (p < 0.35f)
                        {
                            float w = Mathf.Clamp01(heavy / 0.14f);
                            coreEulerOffset = Vector3.Lerp(Vector3.zero, new Vector3(-16f, 8f, 0f), w);
                            r1EulerTarget = Vector3.Lerp(new Vector3(-42f, 0f, 24f), new Vector3(-152f, 0f, 12f), w);
                            r2EulerTarget = Vector3.Lerp(new Vector3(-32f, 0f, 0f), new Vector3(-18f, 0f, 0f), w);
                        }
                        else
                        {
                            float eased = Mathf.Clamp01((heavy - 0.14f) / 0.86f);
                            coreEulerOffset = Vector3.Lerp(new Vector3(-16f, 8f, 0f), new Vector3(24f, -10f, 0f), eased);
                            r1EulerTarget = Vector3.Lerp(new Vector3(-152f, 0f, 12f), new Vector3(-34f, 0f, 8f), eased);
                            r2EulerTarget = new Vector3(-12f, 0f, 0f);
                        }
                        l1EulerOffset = new Vector3(-22f, 0f, -24f);
                        break;
                    }
                    case SwingMoveType.CrossThrust:
                    {
                        float thrustCurve = Mathf.Sin(Mathf.Clamp01(p / 0.72f) * Mathf.PI);
                        coreEulerOffset = new Vector3(14f * thrustCurve, -12f * thrustCurve, 0f);
                        r1EulerTarget = Vector3.Lerp(new Vector3(-38f, 0f, 20f), new Vector3(-92f, 0f, 4f), thrustCurve);
                        r2EulerTarget = Vector3.Lerp(new Vector3(-36f, 0f, 0f), new Vector3(-4f, 0f, 0f), thrustCurve);
                        break;
                    }
                }
            }
            else if (_aimBlend > 0.01f)
            {
                // Postura telecinetica: fibras abertas para frente canalizando as laminas flutuantes
                rWeight = Mathf.Lerp(0.85f, 0.92f, _aimBlend);
                lWeight = Mathf.Lerp(0f, 0.85f, _aimBlend);
                Vector3 aimR1 = new Vector3(-66f, 0f, 34f);
                Vector3 aimR2 = new Vector3(-20f, 0f, 0f);
                r1EulerTarget = Vector3.Lerp(r1EulerTarget, aimR1, _aimBlend);
                r2EulerTarget = Vector3.Lerp(r2EulerTarget, aimR2, _aimBlend);
                l1EulerOffset = new Vector3(-60f, 0f, -34f);
                coreEulerOffset = new Vector3(6f * _aimBlend, 0f, 0f);
            }

            if (coreEulerOffset.sqrMagnitude > 0.01f)
            {
                coreBone.localRotation = coreBone.localRotation * Quaternion.Euler(coreEulerOffset);
            }

            fiberR1.localRotation = Quaternion.Slerp(fiberR1.localRotation, Quaternion.Euler(r1EulerTarget), rWeight);
            if (fiberR2 != null)
            {
                fiberR2.localRotation = Quaternion.Slerp(fiberR2.localRotation, Quaternion.Euler(r2EulerTarget), rWeight);
            }
            if (fiberL1 != null && lWeight > 0.01f)
            {
                fiberL1.localRotation = Quaternion.Slerp(fiberL1.localRotation, Quaternion.Euler(l1EulerOffset), lWeight);
                if (fiberL2 != null && _aimBlend > 0.01f)
                {
                    fiberL2.localRotation = Quaternion.Slerp(fiberL2.localRotation, Quaternion.Euler(-18f, 0f, 0f), _aimBlend * 0.85f);
                }
            }
        }

        /// <summary>
        /// Non-linear heavy easing curve (No Rest for the Wicked feel):
        /// 0.00..0.35: slow heavy windup (0.00 -> 0.14)
        /// 0.35..0.72: explosive fast snap (0.14 -> 0.90)
        /// 0.72..1.00: heavy decelerating follow-through (0.90 -> 1.00)
        /// </summary>
        private static float EvaluateHeavySwingCurve(float t)
        {
            t = Mathf.Clamp01(t);
            if (t < 0.35f)
            {
                float w = t / 0.35f;
                return w * w * 0.14f;
            }
            if (t < 0.72f)
            {
                float s = (t - 0.35f) / 0.37f;
                float snap = s < 0.5f
                    ? 4f * s * s * s
                    : 1f - Mathf.Pow(-2f * s + 2f, 3f) * 0.5f;
                return Mathf.Lerp(0.14f, 0.90f, snap);
            }
            float f = (t - 0.72f) / 0.28f;
            float follow = 1f - (1f - f) * (1f - f);
            return Mathf.Lerp(0.90f, 1.0f, follow);
        }

        private static float SmoothSwingCurve(float progress, float windupEnd, float strikeEnd)
        {
            if (progress < windupEnd)
            {
                return -(progress / Mathf.Max(0.01f, windupEnd)) * 0.16f;
            }
            float t = Mathf.Clamp01((progress - windupEnd) / Mathf.Max(0.01f, strikeEnd - windupEnd));
            return t * t * (3f - 2f * t);
        }

        private void UpdateSwordMountAndVisuals(float dt)
        {
            if (weaponMount == null || swordRoot == null) return;

            // Flutuacao sutil das laminas acopladas + leve pulso visual quando recarregam
            for (int i = 0; i < 3; i++)
            {
                if (razorSlots[i] != null && _bladeMounted[i])
                {
                    float offset = Mathf.Sin(Time.time * bladeFloatSpeed + i * 1.15f) * bladeFloatAmplitude;
                    float snapBounce = Mathf.Sin(_slotReloadFlash[i] * Mathf.PI) * 0.035f;
                    razorSlots[i].localPosition = _slotBaseLocalPos[i] + new Vector3(0f, offset + snapBounce, 0f);
                }
            }

            // Posicao base da mao direita em espaco local do jogador
            Vector3 handLocalPos = new Vector3(0.46f, 0.92f, 0.34f);
            if (fiberRTip != null)
            {
                handLocalPos = transform.InverseTransformPoint(fiberRTip.position) + handGripLocalOffset;
            }

            if (_activeMove != SwingMoveType.None)
            {
                float p = 1f - Mathf.Clamp01(_moveTimer / Mathf.Max(0.01f, _moveDuration));

                if (_meleeTrail != null)
                {
                    _meleeTrail.emitting = p >= 0.12f && p <= 0.80f;
                }

                switch (_activeMove)
                {
                    case SwingMoveType.SlashRightToLeft:
                    case SwingMoveType.SlashLeftToRight:
                    {
                        float dirSign = _activeMove == SwingMoveType.SlashRightToLeft ? 1f : -1f;
                        float s = EvaluateHeavySwingCurve(p);
                        float yaw = Mathf.Lerp(105f * dirSign, -108f * dirSign, s);
                        weaponMount.localPosition = Vector3.Lerp(weaponMount.localPosition, handLocalPos, dt * 32f);
                        weaponMount.localRotation = Quaternion.Euler(0f, yaw, 0f);
                        swordRoot.localRotation = Quaternion.Euler(90f, 0f, 90f * dirSign);
                        break;
                    }
                    case SwingMoveType.OverheadSlam:
                    {
                        float heavy = EvaluateHeavySwingCurve(p);
                        float pitch;
                        if (p < 0.35f)
                        {
                            float w = Mathf.Clamp01(heavy / 0.14f);
                            pitch = Mathf.Lerp(idleHandSwordEuler.x, -65f, w);
                        }
                        else
                        {
                            float slamT = Mathf.Clamp01((heavy - 0.14f) / 0.86f);
                            pitch = Mathf.Lerp(-65f, 112f, slamT);
                        }
                        weaponMount.localPosition = Vector3.Lerp(weaponMount.localPosition, handLocalPos, dt * 32f);
                        weaponMount.localRotation = Quaternion.Euler(pitch, 0f, 0f);
                        swordRoot.localRotation = Quaternion.Euler(0f, 90f, 0f);
                        break;
                    }
                    case SwingMoveType.CrossThrust:
                    {
                        float thrust = Mathf.Sin(Mathf.Clamp01(p / 0.72f) * Mathf.PI);
                        Vector3 thrustPos = handLocalPos + new Vector3(-0.08f * thrust, 0.08f * thrust, 0.55f * thrust);
                        weaponMount.localPosition = Vector3.Lerp(weaponMount.localPosition, thrustPos, dt * 35f);
                        weaponMount.localRotation = Quaternion.Slerp(weaponMount.localRotation, Quaternion.identity, dt * 28f);
                        swordRoot.localRotation = Quaternion.Slerp(swordRoot.localRotation, Quaternion.Euler(90f, 0f, 0f), dt * 28f);
                        break;
                    }
                }
            }
            else
            {
                if (_meleeTrail != null) _meleeTrail.emitting = false;

                // Hibrido (Opcao C): interpola suavemente entre Mao Direita (handLocalPos) e Levitacao Frontal (aimMountLocalPos)
                float hoverY = Mathf.Sin(Time.time * 2.2f) * (0.04f * _aimBlend);
                Vector3 aimPos = aimMountLocalPos + new Vector3(0f, hoverY, 0f);
                Vector3 blendedPos = Vector3.Lerp(handLocalPos, aimPos, _aimBlend);
                Quaternion blendedSwordRot = Quaternion.Slerp(Quaternion.Euler(idleHandSwordEuler), Quaternion.Euler(aimSwordEuler), _aimBlend);

                weaponMount.localPosition = Vector3.Lerp(weaponMount.localPosition, blendedPos, dt * 18f);
                weaponMount.localRotation = Quaternion.Slerp(weaponMount.localRotation, Quaternion.identity, dt * 18f);
                swordRoot.localRotation = Quaternion.Slerp(swordRoot.localRotation, blendedSwordRot, dt * 18f);
            }
        }
    }
}
