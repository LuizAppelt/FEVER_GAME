using System.Collections.Generic;
using StomachAbyss.Combat;
using UnityEngine;
using UnityEngine.Rendering;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace MapaEstomago
{
    /// <summary>
    /// Arma "Chicote Mini Tenia" (Drop do Boss Tenia da Catedral):
    /// - Montada no Weapon_Mount_Pivot integrado aos ossos do Virus_Model (Fiber_R_1/R_2/R_Tip, Core, Fiber_L).
    /// - Combo Corpo a Corpo de 3 Golpes (Botao Esq sem mirar):
    ///   1. Chicotada Dir -> Esq (varredura media, -1.0s Fadiga por acerto)
    ///   2. Chicotada Esq -> Dir (retorno rapido, -1.0s Fadiga por acerto)
    ///   3. Estalo Frontal do Escolex (alcance estendido ~4.9m, -1.5s Fadiga por acerto, impacto pesado)
    /// - Mira Padronizada da Espada Cruz (Segurar Botao Dir):
    ///   * Sem linha guia no chao; o personagem gira suavemente para o cursor e ergue os bracos/arma (_aimBlend).
    ///   * Exibe apenas o circulo acido limpo no local exato da queda do acido no chao.
    ///   * Toque rapido Botao Esq: Bolha Acida Pequena (3 cargas).
    ///   * Segurar Botao Esq (0.55s): Bolha Pesada do Boss (arco parabolico + poca corrosiva de 3.0s no solo).
    /// - Debuff Fadiga (5.0s): bloqueia disparos e desacelera levemente o chicote; hits corpo a corpo aceleram a recarga.
    /// </summary>
    public class TeniaWhipWeapon : MonoBehaviour, IPlayerWeapon
    {
        private enum WhipMoveType
        {
            None = 0,
            SlashRightToLeft = 1,
            SlashLeftToRight = 2,
            ForwardSnap = 3
        }

        [Header("Referencias de Montagem e Materiais")]
        public Transform weaponMount;
        public Transform whipRoot;
        public Material acidMaterial;
        public Material pixelParticleMaterial;

        [Header("Ossos do Personagem (Virus_Model)")]
        public Transform coreBone;
        public Transform fiberR1;
        public Transform fiberR2;
        public Transform fiberRTip;
        public Transform fiberL1;
        public Transform fiberL2;

        [Header("Pose Hibrida (Padrao Espada Cruz)")]
        public Vector3 handGripLocalOffset = new Vector3(0.04f, -0.02f, 0.08f);
        public Vector3 idleHandWhipEuler = new Vector3(16f, -12f, -18f);
        public Vector3 aimMountLocalPos = new Vector3(0.05f, 1.28f, 0.90f);
        public Vector3 aimWhipEuler = new Vector3(10f, 0f, 0f);

        [Header("Combo Corpo a Corpo (3 Chicotadas)")]
        public float slashSwingDuration = 0.34f;
        public float snapSwingDuration = 0.44f;
        public float meleeCooldown = 0.38f;
        public float comboResetWindow = 0.90f;
        public float meleeBaseReach = 3.9f;
        public float snapReach = 4.9f;
        public float meleeDamage = 22f;
        public float snapDamage = 32f;

        [Header("Ataque a Distancia (Copia do Boss)")]
        public int maxCharges = 3;
        public int currentCharges = 3;
        public float shotCooldown = 0.22f;
        public float heavyChargeTime = 0.55f;
        public float rangedMaxDistance = 16.5f;
        public float heavyPoolRadius = 3.0f;

        [Header("Debuff: Fadiga (5.0s)")]
        public float fatigueDuration = 5.0f;
        public float fatigueHitReduction = 1.0f;
        public float fatigueSnapHitReduction = 1.5f;
        public float fatigueMeleeSpeedPenalty = 0.20f;

        [Header("Impacto e Game Feel")]
        public float hitstopLightDuration = 0.042f;
        public float hitstopHeavyDuration = 0.072f;
        public float cameraShakeIntensity = 0.12f;

        // IPlayerWeapon
        public string WeaponDisplayName => "Mini Tenia";
        public string HudStatusText
        {
            get
            {
                if (_fatigueTimer > 0f)
                {
                    return $"FADIGA {_fatigueTimer:0.0}s (Bata p/ acelerar)";
                }
                if (_isChargingHeavy)
                {
                    int pct = Mathf.RoundToInt(Mathf.Clamp01(_chargeTimer / Mathf.Max(0.05f, heavyChargeTime)) * 100f);
                    return $"CARREGANDO PESADO {pct}%";
                }
                return $"{currentCharges}/{maxCharges} Cargas | Combo {_comboStep}/3";
            }
        }

        public bool IsBusy => _activeMove != WhipMoveType.None || _isChargingHeavy;
        public bool IsAiming => _isAiming;
        public bool SuppressesDefaultClawAttack => true;
        public float MoveSpeedMultiplier => _isAiming ? 0.68f : (_activeMove == WhipMoveType.ForwardSnap ? 0.65f : (_fatigueTimer > 0f ? 0.92f : 1.0f));

        private SimplePlayerController _owner;
        private IsometricCameraFollow _isoCam;
        private Camera _cam;
        private PlayerWeaponArsenal _arsenal;
        private bool _equippedToOwner;
        private bool _runtimeInitialized;

        // Hierarquia articulada do Tenia_Whip.glb
        private Transform _hilt;
        private readonly List<Transform> _segments = new List<Transform>(12);
        private readonly List<Vector3> _segBasePos = new List<Vector3>(12);
        private readonly List<Quaternion> _segBaseRot = new List<Quaternion>(12);
        private Transform _scolex;
        private Transform _scolexSuckers;
        private Transform _scolexHooks;
        private Transform _scolexAcid;
        private Vector3 _scolexBasePos;
        private Quaternion _scolexBaseRot;
        private Vector3 _scolexSuckersOffset;
        private Vector3 _scolexHooksOffset;
        private Vector3 _scolexAcidOffset;
        private bool _rigCached;

        // Combate Melee
        private WhipMoveType _activeMove = WhipMoveType.None;
        private float _moveTimer;
        private float _moveDuration;
        private float _meleeCooldownTimer;
        private float _comboResetTimer;
        private int _comboStep;
        private bool _hitApplied;
        private bool _snapSfxPlayed;
        private bool _bufferedPrimaryAttack;
        private Vector3 _meleeDir = Vector3.forward;

        // Mira e Longo Alcance
        private bool _isAiming;
        private float _aimBlend;
        private Vector3 _aimGroundPoint;
        private float _shotCooldownTimer;
        private bool _isChargingHeavy;
        private float _chargeTimer;
        private float _fatigueTimer;
        private float _fatigueRecoveryPulse;

        // VFX (Rastro no Escolex + Circulo de Queda do Acido no Chao)
        private TrailRenderer _whipTrail;
        private GameObject _groundCircleRoot;
        private MeshRenderer _groundOuterRenderer;
        private MeshRenderer _groundFillRenderer;
        private Transform _groundFillTf;
        private Material _groundRingMat;
        private Material _groundFillMat;
        private MaterialPropertyBlock _circleMpb;

        private static Mesh s_ringMesh;
        private static Mesh s_fillMesh;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

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
            if (_owner == null) _owner = GetComponent<SimplePlayerController>();
            if (_arsenal == null) _arsenal = GetComponent<PlayerWeaponArsenal>();
            if (_owner != null && _arsenal == null && _owner.ActiveWeapon == null)
            {
                _owner.SetActiveWeapon(this);
            }
        }

        public void EnsureRuntimeInitialized()
        {
            if (_runtimeInitialized && _whipTrail != null && _groundCircleRoot != null) return;

            if (_cam == null) _cam = Camera.main;
            if (_cam == null) _cam = FindAnyObjectByType<Camera>();
            if (_isoCam == null) _isoCam = FindAnyObjectByType<IsometricCameraFollow>();
            if (_owner == null) _owner = GetComponent<SimplePlayerController>();
            if (_arsenal == null) _arsenal = GetComponent<PlayerWeaponArsenal>();

            if (currentCharges <= 0 && _fatigueTimer <= 0f)
            {
                currentCharges = maxCharges;
            }

            ResolveMountAndRig();
            ResolveVirusBones();
            EnsureWhipTrail();
            EnsureGroundLandingCircle();
            CombatSfxBank.EnsureExists();
            CombatHitstopManager.EnsureExists();
            BloodSplatterSystem.EnsureExists();

            _runtimeInitialized = true;
        }

        private void OnDisable()
        {
            SetGroundCircleVisible(false);
            if (_whipTrail != null)
            {
                _whipTrail.emitting = false;
                _whipTrail.Clear();
            }
        }

        private void OnDestroy()
        {
            if (_groundCircleRoot != null)
            {
                Destroy(_groundCircleRoot);
            }
        }

        public void OnWeaponEquipped(SimplePlayerController owner)
        {
            _owner = owner;
            _equippedToOwner = true;
            EnsureRuntimeInitialized();
            ResolveVirusBones();
            if (whipRoot != null && !whipRoot.gameObject.activeSelf)
            {
                whipRoot.gameObject.SetActive(true);
            }
        }

        public void OnWeaponUnequipped()
        {
            _equippedToOwner = false;
            CancelActiveAttack();
            _isAiming = false;
            _aimBlend = 0f;
            SetGroundCircleVisible(false);
            if (_whipTrail != null)
            {
                _whipTrail.emitting = false;
                _whipTrail.Clear();
            }
            if (whipRoot != null && whipRoot.gameObject.activeSelf)
            {
                whipRoot.gameObject.SetActive(false);
            }
        }

        public void OnPrimaryAttackPressed()
        {
            if (_owner != null && (_owner.IsDead || _owner.IsDodging || _owner.inputLocked)) return;

            if (_isAiming)
            {
                TryBeginRangedChargeOrShot();
                return;
            }

            if (_activeMove != WhipMoveType.None)
            {
                // Buffer da proxima chicotada nos ultimos 45% do golpe
                float progress = 1f - Mathf.Clamp01(_moveTimer / Mathf.Max(0.01f, _moveDuration));
                if (progress >= 0.45f)
                {
                    _bufferedPrimaryAttack = true;
                }
                return;
            }

            if (_meleeCooldownTimer <= 0f)
            {
                TriggerNextMeleeCombo();
            }
        }

        public void OnSecondaryAimState(bool held, bool justReleased)
        {
            if (_owner != null && (_owner.IsDead || _owner.IsDodging || _owner.inputLocked))
            {
                held = false;
                justReleased = true;
            }

            if (held)
            {
                if (!_isAiming)
                {
                    _isAiming = true;
                    if (_activeMove != WhipMoveType.None)
                    {
                        CancelActiveAttack();
                    }
                }
            }
            else
            {
                _isAiming = false;
                _isChargingHeavy = false;
                _chargeTimer = 0f;
                SetGroundCircleVisible(false);
            }
        }

        public void CancelActiveAttack()
        {
            _activeMove = WhipMoveType.None;
            _moveTimer = 0f;
            _hitApplied = false;
            _snapSfxPlayed = false;
            _bufferedPrimaryAttack = false;
            _isChargingHeavy = false;
            _chargeTimer = 0f;
            if (_whipTrail != null) _whipTrail.emitting = false;
        }

        private void Update()
        {
            if (!_runtimeInitialized) EnsureRuntimeInitialized();
            if (_owner != null && _owner.IsDead)
            {
                CancelActiveAttack();
                _isAiming = false;
                SetGroundCircleVisible(false);
                return;
            }

            // Fallback apenas se estiver sem PlayerWeaponArsenal nem SimplePlayerController
            if (!_equippedToOwner && _owner == null)
            {
                UpdateStandaloneFallback();
            }

            float dt = Time.deltaTime;
            if (_meleeCooldownTimer > 0f) _meleeCooldownTimer = Mathf.Max(0f, _meleeCooldownTimer - dt);
            if (_shotCooldownTimer > 0f) _shotCooldownTimer = Mathf.Max(0f, _shotCooldownTimer - dt);
            if (_fatigueRecoveryPulse > 0f) _fatigueRecoveryPulse = Mathf.Max(0f, _fatigueRecoveryPulse - dt * 2.8f);

            UpdateFatigue(dt);
            UpdateMeleeComboState(dt);
            UpdateAimTargetAndGroundCircle(dt);
            UpdateRangedCharge(dt);
        }

        private void LateUpdate()
        {
            if (!_runtimeInitialized) return;
            if (_owner != null && _owner.IsDead) return;
            if (_equippedToOwner && _owner != null && !ReferenceEquals(_owner.ActiveWeapon, this)) return;

            float dt = Time.deltaTime;
            float targetAimBlend = _isAiming ? 1f : 0f;
            _aimBlend = Mathf.MoveTowards(_aimBlend, targetAimBlend, dt * 8.5f);

            UpdateCharacterHybridArmPose(dt);
            UpdateWhipMountAndSegments(dt);
        }

        private void UpdateStandaloneFallback()
        {
#if ENABLE_INPUT_SYSTEM
            Mouse mouse = Mouse.current;
            if (mouse != null)
            {
                OnSecondaryAimState(mouse.rightButton.isPressed, mouse.rightButton.wasReleasedThisFrame);
                if (mouse.leftButton.wasPressedThisFrame) OnPrimaryAttackPressed();
            }
#endif
        }

        private void UpdateFatigue(float dt)
        {
            if (_fatigueTimer <= 0f) return;

            _fatigueTimer = Mathf.Max(0f, _fatigueTimer - dt);
            if (_fatigueTimer <= 0f)
            {
                FinishFatigueRecovery();
            }
        }

        private void FinishFatigueRecovery()
        {
            _fatigueTimer = 0f;
            currentCharges = maxCharges;
            _fatigueRecoveryPulse = 1f;
            CombatSfxBank.Play(CombatSfxId.WhipFatigueReady, 0.78f, 0.96f, 1.06f, 0.05f);
            _owner?.TriggerWeaponFlash(new Color(0.45f, 2.0f, 0.35f, 1f), 0.18f, 1.9f);
        }

        // =========================================================================
        // COMBO DE 3 CHICOTADAS (CORPO A CORPO)
        // =========================================================================

        private void TriggerNextMeleeCombo()
        {
            EnsureRuntimeInitialized();
            float speedScale = _fatigueTimer > 0f ? (1f + fatigueMeleeSpeedPenalty) : 1f;

            _comboStep = (_comboStep % 3) + 1;
            switch (_comboStep)
            {
                case 1:
                    _activeMove = WhipMoveType.SlashRightToLeft;
                    _moveDuration = slashSwingDuration * speedScale;
                    _meleeCooldownTimer = meleeCooldown * speedScale;
                    break;
                case 2:
                    _activeMove = WhipMoveType.SlashLeftToRight;
                    _moveDuration = slashSwingDuration * 0.92f * speedScale;
                    _meleeCooldownTimer = meleeCooldown * 0.92f * speedScale;
                    break;
                default:
                    _activeMove = WhipMoveType.ForwardSnap;
                    _moveDuration = snapSwingDuration * speedScale;
                    _meleeCooldownTimer = (meleeCooldown + 0.16f) * speedScale;
                    break;
            }

            _moveTimer = _moveDuration;
            _hitApplied = false;
            _snapSfxPlayed = false;
            _comboResetTimer = comboResetWindow;

            Transform ownerTf = _owner != null ? _owner.transform : transform;
            if (TryGetCursorWorldTarget(ownerTf.position.y + 0.9f, out Vector3 cursorPt))
            {
                Vector3 dir = cursorPt - ownerTf.position;
                dir.y = 0f;
                _meleeDir = dir.sqrMagnitude > 0.02f ? dir.normalized : ownerTf.forward;
            }
            else
            {
                _meleeDir = ownerTf.forward;
            }

            ownerTf.rotation = Quaternion.LookRotation(_meleeDir, Vector3.up);

            if (_owner != null)
            {
                float lunge = _activeMove == WhipMoveType.ForwardSnap ? 6.4f : 4.6f;
                _owner.ApplyCombatLunge(_meleeDir, lunge, _activeMove == WhipMoveType.ForwardSnap ? 0.18f : 0.13f);
            }

            if (_whipTrail != null)
            {
                _whipTrail.startWidth = _activeMove == WhipMoveType.ForwardSnap ? 0.34f : 0.24f;
                _whipTrail.Clear();
                _whipTrail.emitting = false;
            }
        }

        private void UpdateMeleeComboState(float dt)
        {
            if (_activeMove != WhipMoveType.None)
            {
                _moveTimer = Mathf.Max(0f, _moveTimer - dt);
                float progress = 1f - Mathf.Clamp01(_moveTimer / Mathf.Max(0.01f, _moveDuration));

                // Momento do estalo sonoro no chicote (logo antes/durante a extensao maxima)
                if (!_snapSfxPlayed && progress >= (_activeMove == WhipMoveType.ForwardSnap ? 0.28f : 0.22f))
                {
                    _snapSfxPlayed = true;
                    if (_activeMove == WhipMoveType.ForwardSnap)
                    {
                        CombatSfxBank.Play(CombatSfxId.WhipCrackHeavy, 0.94f, 0.94f, 1.04f, 0.04f);
                    }
                    else
                    {
                        float pitchBase = _activeMove == WhipMoveType.SlashLeftToRight ? 1.06f : 0.96f;
                        CombatSfxBank.Play(CombatSfxId.WhipCrackLight, 0.86f, pitchBase, pitchBase + 0.08f, 0.04f);
                    }
                }

                float hitPoint = _activeMove == WhipMoveType.ForwardSnap ? 0.42f : 0.38f;
                if (!_hitApplied && progress >= hitPoint)
                {
                    _hitApplied = true;
                    ExecuteMeleeWhipHitbox();
                }

                if (_moveTimer <= 0f)
                {
                    _activeMove = WhipMoveType.None;
                    _comboResetTimer = comboResetWindow;

                    if (_bufferedPrimaryAttack && !_isAiming && _meleeCooldownTimer <= 0f)
                    {
                        _bufferedPrimaryAttack = false;
                        TriggerNextMeleeCombo();
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
        }

        private void ExecuteMeleeWhipHitbox()
        {
            Transform originTf = _owner != null ? _owner.transform : transform;
            bool isSnap = _activeMove == WhipMoveType.ForwardSnap;
            float reach = isSnap ? snapReach : meleeBaseReach;
            float damage = isSnap ? snapDamage : meleeDamage;
            float sphereRadius = isSnap ? 1.85f : 2.35f;

            Vector3 hitCenter = originTf.position + _meleeDir * (reach * 0.56f) + Vector3.up * 0.95f;
            Vector3 swingDir;
            switch (_activeMove)
            {
                case WhipMoveType.SlashRightToLeft:
                    swingDir = (-originTf.right + _meleeDir * 0.5f).normalized;
                    break;
                case WhipMoveType.SlashLeftToRight:
                    swingDir = (originTf.right + _meleeDir * 0.5f).normalized;
                    break;
                default:
                    swingDir = _meleeDir;
                    break;
            }

            Collider[] cols = Physics.OverlapSphere(hitCenter, sphereRadius + (reach * 0.35f));
            HashSet<Object> hitSet = new HashSet<Object>();
            int enemyHitsForFatigue = 0;
            bool hitAnything = false;

            if (cols != null)
            {
                for (int i = 0; i < cols.Length; i++)
                {
                    Collider c = cols[i];
                    if (c == null || c.transform.IsChildOf(originTf)) continue;

                    Vector3 toCol = c.ClosestPoint(originTf.position + Vector3.up * 0.95f) - originTf.position;
                    toCol.y = 0f;
                    float dist = toCol.magnitude;
                    if (dist > reach + 0.65f) continue;

                    if (dist > 0.45f)
                    {
                        float dot = Vector3.Dot(toCol.normalized, _meleeDir);
                        float minDot = isSnap ? 0.55f : 0.05f;
                        if (dot < minDot) continue;
                    }

                    OrganPulse op = c.GetComponentInParent<OrganPulse>();
                    if (op != null && hitSet.Add(op))
                    {
                        op.TriggerHitRecoil();
                        hitAnything = true;
                    }

                    WhipTrainingDummy dummy = c.GetComponentInParent<WhipTrainingDummy>();
                    if (dummy != null && hitSet.Add(dummy))
                    {
                        Vector3 pt = c.ClosestPoint(hitCenter);
                        BloodSplatterSystem.SpawnBloodBurst(pt, swingDir, isSnap ? 1.25f : 0.9f, null, isSnap);
                        CombatSfxBank.PlayImpact(isSnap, pt, isSnap ? 0.88f : 0.78f);
                        dummy.TakeWhipDamage(damage, isMelee: true);
                        enemyHitsForFatigue++;
                        hitAnything = true;
                        continue;
                    }

                    IAcidDamageable dmg = c.GetComponentInParent<IAcidDamageable>();
                    if (dmg != null && !ReferenceEquals(dmg, _owner) && dmg is Object obj && hitSet.Add(obj))
                    {
                        Vector3 impactPt = c.ClosestPoint(hitCenter);
                        float bloodInt = isSnap ? 1.35f : 0.95f;
                        BloodSplatterSystem.BeginAttackBurstContext(swingDir, impactPt, bloodInt, isSnap);
                        CombatSfxBank.BeginImpactScope(isSnap);
                        try
                        {
                            dmg.TakeAcidDamage(damage);
                        }
                        finally
                        {
                            if (!BloodSplatterSystem.WasEntityHandledInContext)
                            {
                                BloodSplatterSystem.SpawnBloodBurst(impactPt, swingDir, bloodInt, null, isSnap);
                            }
                            if (!CombatSfxBank.WasImpactClaimedInScope)
                            {
                                CombatSfxBank.PlayImpact(isSnap, impactPt, isSnap ? 0.90f : 0.80f);
                            }
                            BloodSplatterSystem.EndAttackBurstContext();
                            CombatSfxBank.EndImpactScope(isSnap);
                        }
                        enemyHitsForFatigue++;
                        hitAnything = true;
                    }
                }
            }

            // Reacao com props organicos do estomago (igual a Espada Cruz)
            AcidBubblePopper[] bubbles = FindObjectsByType<AcidBubblePopper>(FindObjectsSortMode.None);
            foreach (AcidBubblePopper b in bubbles)
            {
                if (b != null && Vector3.Distance(hitCenter, b.transform.position) <= sphereRadius + 0.7f)
                {
                    b.PopByAttack();
                    hitAnything = true;
                }
            }

            UlcerEruption[] ulcers = FindObjectsByType<UlcerEruption>(FindObjectsSortMode.None);
            foreach (UlcerEruption u in ulcers)
            {
                if (u != null && Vector3.Distance(hitCenter, u.transform.position) <= sphereRadius + 0.9f)
                {
                    u.TriggerEruption();
                    hitAnything = true;
                }
            }

            FleshEyeDoorController[] doors = FindObjectsByType<FleshEyeDoorController>(FindObjectsSortMode.None);
            foreach (FleshEyeDoorController door in doors)
            {
                if (door != null && Vector3.Distance(hitCenter, door.transform.position) <= sphereRadius + 1.4f)
                {
                    door.EmitMouthBurst(12, new Color(0.85f, 0.08f, 0.12f, 1f), 4.0f);
                    hitAnything = true;
                }
            }

            // Reduz o tempo da Fadiga quando acerta inimigos corpo a corpo!
            if (enemyHitsForFatigue > 0 && _fatigueTimer > 0f)
            {
                float reductionPerHit = isSnap ? fatigueSnapHitReduction : fatigueHitReduction;
                _fatigueTimer = Mathf.Max(0f, _fatigueTimer - reductionPerHit * enemyHitsForFatigue);
                _fatigueRecoveryPulse = 0.85f;
                _owner?.TriggerWeaponFlash(new Color(0.35f, 1.85f, 0.45f, 1f), 0.12f, 1.7f);

                if (_fatigueTimer <= 0f)
                {
                    FinishFatigueRecovery();
                }
            }

            if (hitAnything || isSnap)
            {
                if (hitAnything)
                {
                    CombatHitstopManager.TriggerHitstop(
                        isSnap ? hitstopHeavyDuration : hitstopLightDuration,
                        0.032f,
                        isSnap
                    );
                }

                if (_isoCam != null)
                {
                    float shake = isSnap ? cameraShakeIntensity * 1.35f : (enemyHitsForFatigue > 0 ? cameraShakeIntensity * 0.85f : cameraShakeIntensity * 0.35f);
                    _isoCam.TriggerShake(shake, isSnap ? 0.15f : 0.09f);
                }
            }
        }

        // =========================================================================
        // MIRA (PADRAO ESPADA CRUZ) + CIRCULO DE QUEDA DO ACIDO NO SOLO
        // =========================================================================

        private bool TryGetCursorWorldTarget(float defaultPlaneY, out Vector3 targetPoint)
        {
            Transform originTf = _owner != null ? _owner.transform : transform;
            targetPoint = originTf.position + originTf.forward * 8f;
            if (_cam == null) _cam = Camera.main;
            if (_cam == null) _cam = FindAnyObjectByType<Camera>();
            if (_cam == null) return false;

            Vector2 screenPos = GetMouseScreenPosition();
            Ray ray = _cam.ScreenPointToRay(screenPos);

            // 1. Raycast em alvos reais de combate (ignora paredes invisiveis BG3, arcos e colunas)
            RaycastHit[] hits = Physics.RaycastAll(ray, 160f, ~0, QueryTriggerInteraction.Collide);
            float bestDist = float.MaxValue;
            RaycastHit bestHit = default;
            bool foundDirectTarget = false;
            for (int i = 0; i < hits.Length; i++)
            {
                Collider c = hits[i].collider;
                if (c == null || c.transform.IsChildOf(originTf)) continue;

                bool isCombatTarget = c.GetComponentInParent<IAcidDamageable>() != null ||
                                      c.GetComponentInParent<WhipTrainingDummy>() != null ||
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

            // 2. Plano horizontal limpo na altura do tronco/chao -> mira 360 suave igual a Espada Cruz
            Plane p = new Plane(Vector3.up, new Vector3(0f, defaultPlaneY, 0f));
            if (p.Raycast(ray, out float enter))
            {
                targetPoint = ray.GetPoint(enter);
                return true;
            }
            return false;
        }

        private float SampleGroundHeightUnder(Vector3 worldPos, float fallbackY)
        {
            Transform originTf = _owner != null ? _owner.transform : transform;
            Ray ray = new Ray(new Vector3(worldPos.x, fallbackY + 5.0f, worldPos.z), Vector3.down);
            RaycastHit[] hits = Physics.RaycastAll(ray, 14f, ~0, QueryTriggerInteraction.Ignore);
            float bestDist = float.MaxValue;
            float bestY = fallbackY;
            for (int i = 0; i < hits.Length; i++)
            {
                Collider c = hits[i].collider;
                if (c == null || c.transform.IsChildOf(originTf)) continue;
                if (c.GetComponentInParent<IAcidDamageable>() != null || c.GetComponentInParent<WhipTrainingDummy>() != null) continue;
                if (hits[i].distance < bestDist)
                {
                    bestDist = hits[i].distance;
                    bestY = hits[i].point.y;
                }
            }
            return bestY;
        }

        private void UpdateAimTargetAndGroundCircle(float dt)
        {
            Transform originTf = _owner != null ? _owner.transform : transform;
            Vector3 originPos = originTf.position;

            if (TryGetCursorWorldTarget(originPos.y + 0.9f, out Vector3 cursorPt))
            {
                Vector3 flatDelta = new Vector3(cursorPt.x - originPos.x, 0f, cursorPt.z - originPos.z);
                if (flatDelta.magnitude > rangedMaxDistance)
                {
                    flatDelta = flatDelta.normalized * rangedMaxDistance;
                }
                else if (flatDelta.magnitude < 1.25f)
                {
                    Vector3 fwd = flatDelta.sqrMagnitude > 0.001f ? flatDelta.normalized : originTf.forward;
                    flatDelta = fwd * 1.25f;
                }

                Vector3 clampedXZ = new Vector3(originPos.x + flatDelta.x, originPos.y, originPos.z + flatDelta.z);
                clampedXZ.y = SampleGroundHeightUnder(clampedXZ, originPos.y);
                _aimGroundPoint = clampedXZ;
            }
            else
            {
                _aimGroundPoint = originPos + originTf.forward * 6f;
            }

            // Gira o personagem suavemente para o cursor ao segurar Botao Direito (padrao Espada Cruz)
            if (_isAiming && _owner != null && !_owner.IsDead && !_owner.IsDodging)
            {
                Vector3 aimDir = _aimGroundPoint - originPos;
                aimDir.y = 0f;
                if (aimDir.sqrMagnitude > 0.01f)
                {
                    Quaternion targetRot = Quaternion.LookRotation(aimDir.normalized, Vector3.up);
                    _owner.transform.rotation = Quaternion.Slerp(_owner.transform.rotation, targetRot, 34f * dt);
                }
            }

            // Circulo apenas no local da queda do acido
            bool showCircle = _isAiming && (_owner == null || (!_owner.IsDead && !_owner.IsDodging));
            SetGroundCircleVisible(showCircle);
            if (!showCircle || _groundCircleRoot == null) return;

            _groundCircleRoot.transform.position = _aimGroundPoint + Vector3.up * 0.08f;
            float pulse = 1f + Mathf.Sin(Time.time * 9f) * 0.035f;

            if (_fatigueTimer > 0f)
            {
                // Em Fadiga: anel pequeno avermelhado/opaco indicando bloqueio
                float fatigueRadius = 0.85f * pulse;
                _groundOuterRenderer.transform.localScale = new Vector3(fatigueRadius, 1f, fatigueRadius);
                _groundFillRenderer.transform.localScale = new Vector3(0.01f, 1f, 0.01f);
                ApplyCircleColor(new Color(0.85f, 0.22f, 0.20f, 0.45f), new Color(0.65f, 0.12f, 0.12f, 0.15f));
            }
            else if (_isChargingHeavy)
            {
                // Carregando Ataque Pesado: expande ate o raio exato da poca (3.0m) e preenche disco interno!
                float chargeNorm = Mathf.Clamp01(_chargeTimer / Mathf.Max(0.05f, heavyChargeTime));
                float curRadius = Mathf.Lerp(1.05f, heavyPoolRadius, Mathf.SmoothStep(0f, 1f, chargeNorm)) * pulse;
                _groundOuterRenderer.transform.localScale = new Vector3(curRadius, 1f, curRadius);

                float fillScale = Mathf.Max(0.12f, curRadius * Mathf.Lerp(0.18f, 0.98f, chargeNorm));
                _groundFillRenderer.transform.localScale = new Vector3(fillScale, 1f, fillScale);

                Color ringCol = Color.Lerp(new Color(0.35f, 1.0f, 0.16f, 0.90f), new Color(0.95f, 1.0f, 0.20f, 1.0f), chargeNorm);
                Color fillCol = Color.Lerp(new Color(0.28f, 0.95f, 0.14f, 0.22f), new Color(0.62f, 1.0f, 0.18f, 0.46f), chargeNorm);
                ApplyCircleColor(ringCol, fillCol);
            }
            else
            {
                // Mira pronta (Bolha Rapida / Pre-Carga Pesada): circulo acido limpo no local de impacto
                float readyRadius = 0.98f * pulse;
                _groundOuterRenderer.transform.localScale = new Vector3(readyRadius, 1f, readyRadius);
                _groundFillRenderer.transform.localScale = new Vector3(0.28f * pulse, 1f, 0.28f * pulse);
                ApplyCircleColor(new Color(0.36f, 1.0f, 0.18f, 0.88f), new Color(0.42f, 1.0f, 0.22f, 0.35f));
            }
        }

        private void ApplyCircleColor(Color ringCol, Color fillCol)
        {
            if (_circleMpb == null) _circleMpb = new MaterialPropertyBlock();
            if (_groundOuterRenderer != null)
            {
                _circleMpb.Clear();
                _circleMpb.SetColor(BaseColorId, ringCol);
                _circleMpb.SetColor(ColorId, ringCol);
                _groundOuterRenderer.SetPropertyBlock(_circleMpb);
            }
            if (_groundFillRenderer != null)
            {
                _circleMpb.Clear();
                _circleMpb.SetColor(BaseColorId, fillCol);
                _circleMpb.SetColor(ColorId, fillCol);
                _groundFillRenderer.SetPropertyBlock(_circleMpb);
            }
        }

        private void TryBeginRangedChargeOrShot()
        {
            if (_shotCooldownTimer > 0f || _isChargingHeavy) return;

            if (_fatigueTimer > 0f || currentCharges <= 0)
            {
                CombatSfxBank.Play(CombatSfxId.WhipFatigueEnter, 0.42f, 1.05f, 1.18f, 0.18f);
                return;
            }

            _isChargingHeavy = true;
            _chargeTimer = 0f;
        }

        private void UpdateRangedCharge(float dt)
        {
            if (!_isChargingHeavy) return;

            if (!_isAiming || _fatigueTimer > 0f || currentCharges <= 0)
            {
                _isChargingHeavy = false;
                _chargeTimer = 0f;
                return;
            }

            ReadMouseButtons(out bool leftHeld, out bool leftReleased);
            _chargeTimer += dt;

            if (_chargeTimer >= heavyChargeTime)
            {
                FireAcidBubble(isHeavy: true);
                _isChargingHeavy = false;
                _chargeTimer = 0f;
                return;
            }

            if (leftReleased || !leftHeld)
            {
                FireAcidBubble(isHeavy: false);
                _isChargingHeavy = false;
                _chargeTimer = 0f;
            }
        }

        private void FireAcidBubble(bool isHeavy)
        {
            Transform originTf = _owner != null ? _owner.transform : transform;
            GameObject ownerGo = originTf.gameObject;
            Vector3 spawnPos = GetScolexWorldPosition();
            Vector3 targetPos = _aimGroundPoint;

            AcidProjectile proj = AcidProjectile.Spawn(ownerGo, spawnPos, targetPos, isHeavy, acidMaterial, pixelParticleMaterial);
            if (isHeavy)
            {
                proj.poolRadius = heavyPoolRadius;
                proj.poolDuration = 3.0f;
                CombatSfxBank.Play(CombatSfxId.WhipHeavyLob, 0.92f, 0.92f, 1.02f, 0.04f);
            }
            else
            {
                CombatSfxBank.Play(CombatSfxId.WhipAcidSpit, 0.84f, 0.96f, 1.12f, 0.03f);
            }

            _shotCooldownTimer = shotCooldown;

            if (_owner != null)
            {
                _owner.TriggerWeaponFlash(
                    isHeavy ? new Color(0.45f, 2.25f, 0.30f, 1f) : new Color(0.35f, 1.85f, 0.40f, 1f),
                    isHeavy ? 0.24f : 0.12f,
                    isHeavy ? 2.4f : 1.7f
                );
            }

            if (_isoCam != null && isHeavy)
            {
                _isoCam.TriggerShake(cameraShakeIntensity * 0.85f, 0.12f);
            }

            if (isHeavy)
            {
                currentCharges = 0;
                TriggerFatigue();
            }
            else
            {
                currentCharges = Mathf.Max(0, currentCharges - 1);
                if (currentCharges <= 0)
                {
                    TriggerFatigue();
                }
            }
        }

        private void TriggerFatigue()
        {
            _fatigueTimer = fatigueDuration;
            _isChargingHeavy = false;
            _chargeTimer = 0f;
            CombatSfxBank.Play(CombatSfxId.WhipFatigueEnter, 0.72f, 0.92f, 1.04f, 0.08f);
        }

        // =========================================================================
        // ANIMACAO DOS OSSOS DO PERSONAGEM E SEGMENTOS DO CHICOTE
        // =========================================================================

        private void UpdateCharacterHybridArmPose(float dt)
        {
            if (coreBone == null || fiberR1 == null) return;
            if (_owner != null && _owner.IsDodging) return;

            Vector3 coreEulerOffset = Vector3.zero;
            Vector3 r1EulerTarget = _fatigueTimer > 0f ? new Vector3(-26f, 0f, 16f) : new Vector3(-40f, 0f, 24f);
            Vector3 r2EulerTarget = new Vector3(-28f, 0f, 0f);
            Vector3 l1EulerOffset = Vector3.zero;
            float rWeight = 0.86f;
            float lWeight = 0f;

            if (_activeMove != WhipMoveType.None)
            {
                float p = 1f - Mathf.Clamp01(_moveTimer / Mathf.Max(0.01f, _moveDuration));
                rWeight = 0.98f;
                lWeight = 0.35f;

                switch (_activeMove)
                {
                    case WhipMoveType.SlashRightToLeft:
                    {
                        float s = SmoothSwingCurve(p, 0.16f, 0.66f);
                        coreEulerOffset = new Vector3(Mathf.Lerp(-8f, 12f, s), Mathf.Lerp(28f, -38f, s), 0f);
                        r1EulerTarget = Vector3.Lerp(new Vector3(-76f, 0f, 68f), new Vector3(-84f, 0f, -54f), s);
                        r2EulerTarget = new Vector3(-12f, 0f, 0f);
                        l1EulerOffset = new Vector3(14f, 0f, -18f);
                        break;
                    }
                    case WhipMoveType.SlashLeftToRight:
                    {
                        float s = SmoothSwingCurve(p, 0.14f, 0.64f);
                        coreEulerOffset = new Vector3(Mathf.Lerp(10f, -6f, s), Mathf.Lerp(-34f, 36f, s), 0f);
                        r1EulerTarget = Vector3.Lerp(new Vector3(-84f, 0f, -50f), new Vector3(-74f, 0f, 68f), s);
                        r2EulerTarget = new Vector3(-10f, 0f, 0f);
                        l1EulerOffset = new Vector3(12f, 0f, -15f);
                        break;
                    }
                    case WhipMoveType.ForwardSnap:
                    {
                        if (p < 0.30f)
                        {
                            float w = p / 0.30f;
                            coreEulerOffset = Vector3.Lerp(Vector3.zero, new Vector3(-18f, 18f, 0f), w);
                            r1EulerTarget = Vector3.Lerp(new Vector3(-40f, 0f, 24f), new Vector3(-135f, 0f, 26f), w);
                            r2EulerTarget = Vector3.Lerp(new Vector3(-28f, 0f, 0f), new Vector3(-48f, 0f, 0f), w);
                        }
                        else
                        {
                            float s = Mathf.Clamp01((p - 0.30f) / 0.35f);
                            float snap = s * s * (3f - 2f * s);
                            coreEulerOffset = Vector3.Lerp(new Vector3(-18f, 18f, 0f), new Vector3(22f, -12f, 0f), snap);
                            r1EulerTarget = Vector3.Lerp(new Vector3(-135f, 0f, 26f), new Vector3(-88f, 0f, 2f), snap);
                            r2EulerTarget = new Vector3(-4f, 0f, 0f);
                        }
                        l1EulerOffset = new Vector3(-20f, 0f, -24f);
                        break;
                    }
                }
            }
            else if (_aimBlend > 0.01f)
            {
                // Postura de mira igual a da Espada Cruz: fibra direita estendida a frente canalizando o Escolex
                rWeight = Mathf.Lerp(0.86f, 0.94f, _aimBlend);
                lWeight = Mathf.Lerp(0f, 0.72f, _aimBlend);
                float chargePull = _isChargingHeavy ? Mathf.Clamp01(_chargeTimer / Mathf.Max(0.05f, heavyChargeTime)) * 10f : 0f;
                Vector3 aimR1 = new Vector3(-72f - chargePull, 0f, 18f);
                Vector3 aimR2 = new Vector3(-14f, 0f, 0f);
                r1EulerTarget = Vector3.Lerp(r1EulerTarget, aimR1, _aimBlend);
                r2EulerTarget = Vector3.Lerp(r2EulerTarget, aimR2, _aimBlend);
                l1EulerOffset = new Vector3(-46f, 0f, -28f);
                coreEulerOffset = new Vector3((6f - chargePull * 0.4f) * _aimBlend, 0f, 0f);
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
                    fiberL2.localRotation = Quaternion.Slerp(fiberL2.localRotation, Quaternion.Euler(-16f, 0f, 0f), _aimBlend * 0.85f);
                }
            }
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

        private void UpdateWhipMountAndSegments(float dt)
        {
            if (weaponMount == null || whipRoot == null) return;

            Transform ownerTf = _owner != null ? _owner.transform : transform;
            Vector3 handLocalPos = new Vector3(0.46f, 0.92f, 0.34f);
            if (fiberRTip != null)
            {
                handLocalPos = ownerTf.InverseTransformPoint(fiberRTip.position) + handGripLocalOffset;
            }

            float unfurlStrength = 0f;
            float lateralSweep = 0f;
            float verticalLift = 0f;

            if (_activeMove != WhipMoveType.None)
            {
                float p = 1f - Mathf.Clamp01(_moveTimer / Mathf.Max(0.01f, _moveDuration));

                if (_whipTrail != null)
                {
                    _whipTrail.emitting = p >= 0.14f && p <= 0.82f;
                }

                switch (_activeMove)
                {
                    case WhipMoveType.SlashRightToLeft:
                    case WhipMoveType.SlashLeftToRight:
                    {
                        float dirSign = _activeMove == WhipMoveType.SlashRightToLeft ? 1f : -1f;
                        float s = Mathf.Clamp01(SmoothSwingCurve(p, 0.16f, 0.66f));
                        float yaw = Mathf.Lerp(92f * dirSign, -98f * dirSign, s);
                        weaponMount.localPosition = Vector3.Lerp(weaponMount.localPosition, handLocalPos, dt * 32f);
                        weaponMount.localRotation = Quaternion.Euler(4f, yaw, -18f * dirSign);
                        whipRoot.localRotation = Quaternion.Euler(8f, 0f, 0f);
                        unfurlStrength = Mathf.Sin(Mathf.Clamp01(p / 0.85f) * Mathf.PI) * 0.90f;
                        lateralSweep = Mathf.Sin(s * Mathf.PI) * 0.25f * dirSign;
                        break;
                    }
                    case WhipMoveType.ForwardSnap:
                    {
                        float snapWave = p < 0.30f
                            ? -0.20f * (p / 0.30f)
                            : Mathf.Sin(Mathf.Clamp01((p - 0.30f) / 0.58f) * Mathf.PI) * 1.15f;
                        weaponMount.localPosition = Vector3.Lerp(weaponMount.localPosition, handLocalPos + new Vector3(0f, 0.08f, 0.22f * Mathf.Max(0f, snapWave)), dt * 34f);
                        weaponMount.localRotation = Quaternion.Euler(0f, 0f, 0f);
                        whipRoot.localRotation = Quaternion.Euler(0f, 0f, 0f);
                        unfurlStrength = Mathf.Max(0f, snapWave);
                        verticalLift = 0.12f * snapWave;
                        break;
                    }
                }
            }
            else
            {
                if (_whipTrail != null) _whipTrail.emitting = false;

                float hoverY = Mathf.Sin(Time.time * 2.4f) * (0.035f * _aimBlend);
                Vector3 aimPos = aimMountLocalPos + new Vector3(0f, hoverY, 0f);
                Vector3 blendedPos = Vector3.Lerp(handLocalPos, aimPos, _aimBlend);
                Quaternion blendedRot = Quaternion.Slerp(Quaternion.Euler(idleHandWhipEuler), Quaternion.Euler(aimWhipEuler), _aimBlend);

                weaponMount.localPosition = Vector3.Lerp(weaponMount.localPosition, blendedPos, dt * 18f);
                weaponMount.localRotation = Quaternion.Slerp(weaponMount.localRotation, Quaternion.identity, dt * 18f);
                whipRoot.localRotation = Quaternion.Slerp(whipRoot.localRotation, blendedRot, dt * 18f);
                unfurlStrength = _aimBlend * 0.42f;
            }

            AnimateArticulatedSegments(unfurlStrength, lateralSweep, verticalLift);
        }

        private void AnimateArticulatedSegments(float unfurlStrength, float lateralSweep, float verticalLift)
        {
            if (!_rigCached || _segments.Count == 0) return;

            float t = Time.time;
            bool inFatigue = _fatigueTimer > 0f;
            float writheAmp = inFatigue ? 0.012f : (0.025f + _aimBlend * 0.020f);
            float writheSpeed = inFatigue ? 1.8f : (3.6f + (_isChargingHeavy ? 5.0f : 0f));

            Vector3 prevEndDelta = Vector3.zero;
            int n = _segments.Count;

            for (int i = 0; i < n; i++)
            {
                Transform seg = _segments[i];
                if (seg == null) continue;

                float u = (i + 1) / (float)n; // 0..1 ao longo do chicote
                float fatigueDroop = inFatigue ? -0.22f * u * u : 0f;

                // Posicao alvo desenrolada em linha reta a frente (+Z local do whipRoot)
                Vector3 extendedLocalPos = new Vector3(
                    lateralSweep * u * 1.85f + Mathf.Sin(t * 16f - u * 5.5f) * (0.18f * unfurlStrength * u),
                    0.06f + verticalLift * u + fatigueDroop * 0.35f,
                    0.34f + u * 4.25f
                );

                float waveX = Mathf.Sin(t * writheSpeed - i * 0.55f) * writheAmp * u;
                float waveY = Mathf.Cos(t * (writheSpeed * 0.85f) - i * 0.45f) * (writheAmp * 0.7f) * u;
                Vector3 targetPos = Vector3.Lerp(_segBasePos[i], extendedLocalPos, Mathf.Clamp01(unfurlStrength * (0.45f + 0.65f * u)));
                targetPos += new Vector3(waveX, waveY + fatigueDroop * 0.25f, 0f);

                seg.localPosition = targetPos;

                // Inclina cada segmento suavemente para o proximo ponto
                float pitch = Mathf.Lerp(0f, -12f, _aimBlend * u) + (inFatigue ? 14f * u : 0f) + Mathf.Sin(t * writheSpeed - i * 0.5f) * 5.5f * u;
                float yaw = Mathf.Cos(t * (writheSpeed * 0.9f) - i * 0.5f) * 6.5f * u + lateralSweep * 28f * u;
                seg.localRotation = _segBaseRot[i] * Quaternion.Euler(pitch, yaw, 0f);
                prevEndDelta = targetPos - _segBasePos[i];
            }

            if (_scolex != null)
            {
                Vector3 extendedScolexPos = new Vector3(
                    lateralSweep * 2.1f,
                    0.10f + verticalLift + (inFatigue ? -0.32f : 0f),
                    4.72f
                );
                Vector3 scolexPos = Vector3.Lerp(_scolexBasePos, extendedScolexPos, Mathf.Clamp01(unfurlStrength * 1.1f));
                scolexPos += prevEndDelta * 0.15f;
                scolexPos.y += Mathf.Sin(t * writheSpeed) * (writheAmp * 1.2f);

                float scolexYaw = _isAiming ? Mathf.Sin(t * 9f) * 4f : Mathf.Sin(t * 3.5f) * 8f;
                float scolexPitch = _isAiming ? -14f : (inFatigue ? 28f : 0f);
                Quaternion scolexRot = _scolexBaseRot * Quaternion.Euler(scolexPitch, scolexYaw, 0f);

                _scolex.localPosition = scolexPos;
                _scolex.localRotation = scolexRot;

                Vector3 worldDelta = scolexPos - _scolexBasePos;
                if (_scolexSuckers != null)
                {
                    _scolexSuckers.localPosition = _scolexSuckersOffset + worldDelta;
                    _scolexSuckers.localRotation = scolexRot;
                }
                if (_scolexHooks != null)
                {
                    _scolexHooks.localPosition = _scolexHooksOffset + worldDelta;
                    _scolexHooks.localRotation = scolexRot;
                }
                if (_scolexAcid != null)
                {
                    _scolexAcid.localPosition = _scolexAcidOffset + worldDelta;
                    _scolexAcid.localRotation = scolexRot;
                    float acidScale = inFatigue ? 0.25f : (_isChargingHeavy ? 1.65f : (1.0f + _fatigueRecoveryPulse * 0.7f));
                    _scolexAcid.localScale = Vector3.one * acidScale;
                }
            }
        }

        // =========================================================================
        // DESCOBERTA DE HIERARQUIA & VISUAIS AUXILIARES
        // =========================================================================

        private void ResolveMountAndRig()
        {
            if (weaponMount == null)
            {
                weaponMount = transform.Find("Weapon_Mount_Pivot");
            }
            if (whipRoot == null && weaponMount != null)
            {
                whipRoot = weaponMount.Find("Tenia_Whip");
            }
            if (whipRoot == null)
            {
                Transform[] all = GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < all.Length; i++)
                {
                    if (all[i] != null && all[i].name == "Tenia_Whip")
                    {
                        whipRoot = all[i];
                        break;
                    }
                }
            }

            if (whipRoot == null || _rigCached) return;

            _segments.Clear();
            _segBasePos.Clear();
            _segBaseRot.Clear();

            Transform[] segFound = new Transform[12];
            Transform[] children = whipRoot.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < children.Length; i++)
            {
                Transform t = children[i];
                if (t == null) continue;
                if (t.name == "Whip_Hilt") _hilt = t;
                else if (t.name == "Whip_Scolex") _scolex = t;
                else if (t.name == "Whip_Scolex_Suckers") _scolexSuckers = t;
                else if (t.name == "Whip_Scolex_Hooks") _scolexHooks = t;
                else if (t.name == "Whip_Scolex_Acid") _scolexAcid = t;
                else if (t.name.StartsWith("Whip_Seg_"))
                {
                    if (int.TryParse(t.name.Substring("Whip_Seg_".Length), out int idx) && idx >= 1 && idx <= 12)
                    {
                        segFound[idx - 1] = t;
                    }
                }
            }

            for (int i = 0; i < segFound.Length; i++)
            {
                if (segFound[i] != null)
                {
                    _segments.Add(segFound[i]);
                    _segBasePos.Add(segFound[i].localPosition);
                    _segBaseRot.Add(segFound[i].localRotation);
                }
            }

            if (_scolex != null)
            {
                _scolexBasePos = _scolex.localPosition;
                _scolexBaseRot = _scolex.localRotation;
            }
            if (_scolexSuckers != null) _scolexSuckersOffset = _scolexSuckers.localPosition;
            if (_scolexHooks != null) _scolexHooksOffset = _scolexHooks.localPosition;
            if (_scolexAcid != null) _scolexAcidOffset = _scolexAcid.localPosition;

            _rigCached = _segments.Count > 0 || _scolex != null;
        }

        private void ResolveVirusBones()
        {
            Transform modelRoot = _owner != null && _owner.visualRoot != null
                ? _owner.visualRoot
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

        private void EnsureWhipTrail()
        {
            if (_whipTrail != null) return;

            Transform parentTf = _scolex != null ? _scolex : (whipRoot != null ? whipRoot : transform);
            Transform existing = parentTf.Find("Whip_Scolex_Trail");
            if (existing != null)
            {
                _whipTrail = existing.GetComponent<TrailRenderer>();
                if (_whipTrail != null) return;
            }

            GameObject go = new GameObject("Whip_Scolex_Trail");
            go.transform.SetParent(parentTf, false);
            go.transform.localPosition = Vector3.zero;

            _whipTrail = go.AddComponent<TrailRenderer>();
            _whipTrail.time = 0.16f;
            _whipTrail.minVertexDistance = 0.08f;
            _whipTrail.startWidth = 0.26f;
            _whipTrail.endWidth = 0.02f;
            _whipTrail.shadowCastingMode = ShadowCastingMode.Off;
            _whipTrail.receiveShadows = false;
            _whipTrail.emitting = false;

            Gradient grad = new Gradient();
            grad.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(0.75f, 1.0f, 0.35f), 0f),
                    new GradientColorKey(new Color(0.55f, 0.08f, 0.12f), 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0.88f, 0f),
                    new GradientAlphaKey(0f, 1f)
                }
            );
            _whipTrail.colorGradient = grad;

            Shader trailShader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (trailShader == null) trailShader = Shader.Find("Sprites/Default");
            if (trailShader != null)
            {
                Material m = new Material(trailShader);
                m.color = new Color(0.55f, 1.0f, 0.32f, 0.85f);
                _whipTrail.sharedMaterial = m;
            }
        }

        private void EnsureGroundLandingCircle()
        {
            if (_groundCircleRoot != null) return;

            EnsureCircleMeshes();
            _circleMpb = new MaterialPropertyBlock();

            _groundCircleRoot = new GameObject("TeniaWhip_AcidLandingCircle");
            _groundCircleRoot.SetActive(false);

            _groundRingMat = CreateSafeRingMaterial();
            _groundFillMat = CreateSafeRingMaterial();

            _groundOuterRenderer = CreateCircleLayer(_groundCircleRoot.transform, "OuterRing", s_ringMesh, _groundRingMat, 0.02f);
            _groundFillRenderer = CreateCircleLayer(_groundCircleRoot.transform, "InnerFill", s_fillMesh, _groundFillMat, 0.01f);
            _groundFillTf = _groundFillRenderer.transform;
        }

        private static MeshRenderer CreateCircleLayer(Transform parent, string name, Mesh mesh, Material mat, float localY)
        {
            GameObject child = new GameObject(name);
            child.transform.SetParent(parent, false);
            child.transform.localPosition = new Vector3(0f, localY, 0f);

            MeshFilter mf = child.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;

            MeshRenderer mr = child.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return mr;
        }

        private static Material CreateSafeRingMaterial()
        {
            Shader s = Shader.Find("Universal Render Pipeline/Unlit");
            if (s == null) s = Shader.Find("Sprites/Default");
            Material mat = new Material(s);
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
            if (mat.HasProperty("_SrcBlend")) mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            if (mat.HasProperty("_DstBlend")) mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);
            if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", (float)CullMode.Off);
            mat.renderQueue = 3100;
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            return mat;
        }

        private static void EnsureCircleMeshes()
        {
            if (s_ringMesh != null && s_fillMesh != null) return;

            const int segs = 24;
            s_ringMesh = BuildAnnulusMesh(segs, 0.84f, 1.0f);
            s_fillMesh = BuildDiscMesh(segs);
        }

        private static Mesh BuildAnnulusMesh(int segments, float innerR, float outerR)
        {
            Mesh mesh = new Mesh { name = "WhipLandingRingMesh" };
            Vector3[] verts = new Vector3[segments * 2];
            int[] tris = new int[segments * 6];
            for (int i = 0; i < segments; i++)
            {
                float ang = (i / (float)segments) * Mathf.PI * 2f;
                float c = Mathf.Cos(ang);
                float s = Mathf.Sin(ang);
                verts[i * 2] = new Vector3(c * innerR, 0f, s * innerR);
                verts[i * 2 + 1] = new Vector3(c * outerR, 0f, s * outerR);

                int next = (i + 1) % segments;
                int b0 = i * 2;
                int b1 = next * 2;
                int t = i * 6;
                tris[t] = b0;
                tris[t + 1] = b0 + 1;
                tris[t + 2] = b1 + 1;
                tris[t + 3] = b0;
                tris[t + 4] = b1 + 1;
                tris[t + 5] = b1;
            }
            mesh.vertices = verts;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh BuildDiscMesh(int segments)
        {
            Mesh mesh = new Mesh { name = "WhipLandingDiscMesh" };
            Vector3[] verts = new Vector3[segments + 1];
            int[] tris = new int[segments * 3];
            verts[0] = Vector3.zero;
            for (int i = 0; i < segments; i++)
            {
                float ang = (i / (float)segments) * Mathf.PI * 2f;
                verts[i + 1] = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
                int next = (i + 1) % segments;
                tris[i * 3] = 0;
                tris[i * 3 + 1] = next + 1;
                tris[i * 3 + 2] = i + 1;
            }
            mesh.vertices = verts;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private void SetGroundCircleVisible(bool visible)
        {
            if (_groundCircleRoot != null && _groundCircleRoot.activeSelf != visible)
            {
                _groundCircleRoot.SetActive(visible);
            }
        }

        public Vector3 GetScolexWorldPosition()
        {
            if (_scolex != null && _scolex.gameObject.activeInHierarchy)
            {
                return _scolex.position;
            }
            Transform originTf = _owner != null ? _owner.transform : transform;
            return originTf.position + originTf.forward * 1.1f + Vector3.up * 1.15f;
        }

        private static Vector2 GetMouseScreenPosition()
        {
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null) return Mouse.current.position.ReadValue();
            return Vector2.zero;
#else
            return Input.mousePosition;
#endif
        }

        private static void ReadMouseButtons(out bool leftHeld, out bool leftReleased)
        {
#if ENABLE_INPUT_SYSTEM
            Mouse mouse = Mouse.current;
            if (mouse != null)
            {
                leftHeld = mouse.leftButton.isPressed;
                leftReleased = mouse.leftButton.wasReleasedThisFrame;
                return;
            }
            leftHeld = false;
            leftReleased = true;
#else
            leftHeld = Input.GetMouseButton(0);
            leftReleased = Input.GetMouseButtonUp(0);
#endif
        }
    }
}
