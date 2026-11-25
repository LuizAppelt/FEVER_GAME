using System.Collections.Generic;
using BossTenia;
using StomachAbyss.Combat;
using UnityEngine;
using UnityEngine.Rendering;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace MapaEstomago
{
    /// <summary>
    /// Arma Slot [3] "Lamina Ardente":
    /// - Sabre/rapieira carmesim ultra-rapida, SEM ataque a distancia (projeteis).
    /// - Concede +8% de agilidade de movimento enquanto equipada.
    /// - Botao Esquerdo: Combo fluido de 4 golpes rapidos (12 / 12 / 14 / 19) com input buffer nos ultimos 50%.
    ///   Sem trava de clique esquerdo, exceto durante os 0.22s da propria Estocada de Fogo.
    ///   Acertar um inimigo em chamas dispara Flame Fan (+10 dano bonus de fogo, +0.5s queimadura e 14 faiscas).
    /// - Segurar e Soltar Botao Direito: Carrega "Estocada de Fogo" com telegrafo luminoso laranja no chao (~4.4m x 1.2m),
    ///   aquece a lamina para o Modo Ardente e avanca em estocada perfurante deixando rastro de fogo (FlameGroundTrail)
    ///   e incendiando inimigos atingidos (BurnStatusEffect), mantendo Modo Ardente ativo por 3.5s apos a estocada.
    /// </summary>
    public class LaminaArdenteWeapon : MonoBehaviour, IPlayerWeapon
    {
        private enum ArdenteMoveType
        {
            None = 0,
            SlashFastR2L = 1,
            SlashFastL2R = 2,
            SlashLowCross = 3,
            QuickStinger = 4
        }

        private static readonly int PropEmissionColor = Shader.PropertyToID("_EmissionColor");
        private static readonly int PropBaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int PropColor = Shader.PropertyToID("_Color");

        [Header("Referencias de Montagem e Materiais")]
        public Transform weaponMount;
        public Transform swordRoot;
        public Transform hiltGuard;
        public Transform bladePart;
        public Transform emberCore;
        public Material bladeMaterial;
        public Material emberCoreMaterial;
        public Material pixelFireMaterial;

        [Header("Ossos do Personagem (Virus_Model)")]
        public Transform coreBone;
        public Transform fiberR1;
        public Transform fiberR2;
        public Transform fiberRTip;
        public Transform fiberL1;
        public Transform fiberL2;

        [Header("Pose e Agilidade (+8% Movimento)")]
        public float agilitySpeedMultiplier = 1.08f;
        public float chargeMoveSpeedMultiplier = 0.78f;
        public Vector3 handGripLocalOffset = new Vector3(0.04f, -0.02f, 0.08f);
        public Vector3 idleHandSaberEuler = new Vector3(24f, -12f, -20f);

        [Header("Combo Rapido 4 Golpes (Botao Esquerdo)")]
        public float slashR2LDuration = 0.18f;
        public float slashL2RDuration = 0.17f;
        public float slashLowCrossDuration = 0.18f;
        public float quickStingerDuration = 0.21f;
        public float comboResetWindow = 0.85f;
        public float slashReach = 2.85f;
        public float stingerReach = 3.45f;
        public float[] comboDamages = new float[] { 13f, 14f, 16f, 22f };
        public float ardenteModeSlashBonusDamage = 2f;
        public float lightHitstopDuration = 0.024f;
        public float cameraShakeIntensity = 0.065f;

        [Header("Estocada de Fogo (Segurar & Soltar Botao Direito)")]
        public float minChargeThreshold = 0.25f;
        public float fullChargeTime = 0.42f;
        public float thrustDashDuration = 0.22f;
        public float thrustDashDistance = 4.4f;
        public float thrustDashWidth = 1.2f;
        public float thrustDashCooldown = 0.65f;
        public float thrustPierceDamage = 34f;
        public float flameTrailSpacing = 0.85f;
        public float flameTrailRadius = 1.15f;
        public float flameTrailDuration = 3.2f;
        public float flameTrailDps = 16f;
        public float burnDuration = 4.0f;
        public float burnDps = 18f;
        public float flameFanBonusDamage = 10f;
        public float flameFanExtendSeconds = 0.5f;
        public float ardenteModeDuration = 3.5f;

        [Header("Custo de Estamina")]
        public float comboLightStaminaCost = 10f;
        public float comboFinisherStaminaCost = 15f;
        public float flameThrustStaminaCost = 24f;

        // IPlayerWeapon
        public string WeaponDisplayName => "Lamina Ardente";

        public string HudStatusText
        {
            get
            {
                if (_isDashingThrust)
                {
                    return "ESTOCADA DE FOGO!";
                }
                if (_isChargingThrust)
                {
                    int pct = Mathf.RoundToInt(Mathf.Clamp01(_chargeTimer / Mathf.Max(0.05f, fullChargeTime)) * 100f);
                    return $"Carregando Fogo {pct}%";
                }
                if (_ardenteGlowTimer > 0.05f)
                {
                    return $"MODO ARDENTE {_ardenteGlowTimer:0.0}s | Combo {_comboStep}/4";
                }
                if (_thrustCooldownTimer > 0.05f)
                {
                    return $"Recarga Fogo {_thrustCooldownTimer:0.0}s | Combo {_comboStep}/4";
                }
                return _comboStep > 0
                    ? $"Pronta [RMB Estocada Fogo] | Combo {_comboStep}/4"
                    : "Pronta [RMB Estocada Fogo]";
            }
        }

        public bool IsBusy => _activeMove != ArdenteMoveType.None || _isChargingThrust || _isDashingThrust;
        public bool IsAiming => _isChargingThrust || _isDashingThrust;
        public bool SuppressesDefaultClawAttack => true;

        public float MoveSpeedMultiplier
        {
            get
            {
                if (_isChargingThrust) return chargeMoveSpeedMultiplier;
                if (_isDashingThrust) return 1.0f;
                return agilitySpeedMultiplier;
            }
        }

        private SimplePlayerController _owner;
        private IsometricCameraFollow _isoCam;
        private Camera _cam;
        private bool _equippedToOwner;
        private bool _runtimeInitialized;

        // Combo Melee
        private ArdenteMoveType _activeMove = ArdenteMoveType.None;
        private float _moveTimer;
        private float _moveDuration;
        private float _attackCooldownTimer;
        private float _comboResetTimer;
        private int _comboStep;
        private bool _hitApplied;
        private bool _bufferedPrimaryAttack;
        private readonly HashSet<GameObject> _swingHitRoots = new HashSet<GameObject>();

        // Estocada de Fogo & Modo Ardente
        private bool _isChargingThrust;
        private float _chargeTimer;
        private bool _isDashingThrust;
        private float _dashTimer;
        private float _thrustCooldownTimer;
        private float _ardenteGlowTimer;
        private Vector3 _dashDirection = Vector3.forward;
        private float _activeDashDistance = 4.4f;
        private float _activeDashDamage = 28f;
        private Vector3 _lastTrailSpawnPos;
        private bool _dashHitstopTriggered;
        private readonly HashSet<GameObject> _dashHitRoots = new HashSet<GameObject>();

        // FX & Telegrafo
        private TrailRenderer _slashTrail;
        private Material _trailMat;
        private ParticleSystem _sparkParticles;
        private ParticleSystem _bladeFireParticles;
        private Light _bladeGlowLight;
        private Transform _bladeTipAnchor;
        private MaterialPropertyBlock _emissionMpb;
        private Renderer[] _bladeRenderers = System.Array.Empty<Renderer>();
        private Renderer[] _emberRenderers = System.Array.Empty<Renderer>();
        private Renderer[] _hiltRenderers = System.Array.Empty<Renderer>();
        private float _lastAppliedArdenteGlow = -1f;

        private GameObject _telegraphRoot;
        private LineRenderer _telegraphBorder;
        private MeshRenderer _telegraphBaseFill;
        private MeshRenderer _telegraphChargeFill;
        private Material _telegraphBorderMat;
        private Material _telegraphBaseMat;
        private Material _telegraphChargeMat;
        private MaterialPropertyBlock _telegraphMpb;

        private static readonly Color NormalBladeEmission = new Color(0.04f, 0.008f, 0.005f, 1f);
        private static readonly Color ArdenteBladeEmission = new Color(1.45f, 0.40f, 0.05f, 1f);
        private static readonly Color NormalEmberEmission = new Color(0.35f, 0.06f, 0.01f, 1f);
        private static readonly Color ArdenteEmberEmission = new Color(2.85f, 1.15f, 0.16f, 1f);
        private static readonly Color ArdenteHiltEmission = new Color(0.55f, 0.14f, 0.02f, 1f);

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
            if (_owner != null && _owner.ActiveWeapon == null)
            {
                _owner.SetActiveWeapon(this);
            }
        }

        private void OnDisable()
        {
            CancelChargeAndDash();
            CancelActiveAttack();
            SetTelegraphVisible(false);
            if (_slashTrail != null)
            {
                _slashTrail.emitting = false;
                _slashTrail.Clear();
            }
        }

        private void OnDestroy()
        {
            if (_telegraphRoot != null)
            {
                Destroy(_telegraphRoot);
                _telegraphRoot = null;
            }
            if (_trailMat != null) Destroy(_trailMat);
            if (_telegraphBorderMat != null) Destroy(_telegraphBorderMat);
            if (_telegraphBaseMat != null) Destroy(_telegraphBaseMat);
            if (_telegraphChargeMat != null) Destroy(_telegraphChargeMat);
        }

        public void EnsureRuntimeInitialized()
        {
            if (_runtimeInitialized && _slashTrail != null && _sparkParticles != null) return;

            if (_cam == null) _cam = Camera.main;
            if (_cam == null) _cam = FindAnyObjectByType<Camera>();
            if (_isoCam == null) _isoCam = FindAnyObjectByType<IsometricCameraFollow>();
            if (_owner == null) _owner = GetComponent<SimplePlayerController>();

            ResolveSwordHierarchy();
            ResolveVirusBones();
            EnsureSlashTrail();
            EnsureSparkEffects();
            EnsureBladeFireEffects();
            EnsureThrustTelegraph();

            CombatSfxBank.EnsureExists();
            CombatHitstopManager.EnsureExists();
            BloodSplatterSystem.EnsureExists();

            _runtimeInitialized = true;
            ApplyModoArdenteVisuals(0f);
        }

        public void OnWeaponEquipped(SimplePlayerController owner)
        {
            _owner = owner;
            _equippedToOwner = true;
            EnsureRuntimeInitialized();
            ResolveSwordHierarchy();
            ResolveVirusBones();

            _comboStep = 0;
            _comboResetTimer = 0f;
            _attackCooldownTimer = 0f;
            _bufferedPrimaryAttack = false;
            CancelChargeAndDash();

            if (swordRoot != null && !swordRoot.gameObject.activeSelf)
            {
                swordRoot.gameObject.SetActive(true);
            }

            ApplyModoArdenteVisuals(0f);
        }

        public void OnWeaponUnequipped()
        {
            _equippedToOwner = false;
            CancelChargeAndDash();
            CancelActiveAttack();
            _ardenteGlowTimer = 0f;
            _thrustCooldownTimer = 0f;
            SetTelegraphVisible(false);

            if (_slashTrail != null)
            {
                _slashTrail.emitting = false;
                _slashTrail.Clear();
            }

            ApplyModoArdenteVisuals(0f);

            if (swordRoot != null && swordRoot.gameObject.activeSelf)
            {
                swordRoot.gameObject.SetActive(false);
            }
        }

        public void OnPrimaryAttackPressed()
        {
            if (_owner != null && (_owner.IsDead || _owner.IsDodging || _owner.inputLocked)) return;

            // Sem trava de clique esquerdo, exceto durante o proprio avanco da Estocada de Fogo (0.22s)
            if (_isDashingThrust) return;

            // Se estava carregando o Botao Direito e clicou Esquerdo, cancela a carga imediatamente para golpear
            if (_isChargingThrust)
            {
                CancelChargeState();
            }

            if (_activeMove != ArdenteMoveType.None)
            {
                // Buffer do proximo golpe nos ultimos 50% da animacao atual
                float progress = 1f - Mathf.Clamp01(_moveTimer / Mathf.Max(0.01f, _moveDuration));
                if (progress >= 0.50f)
                {
                    _bufferedPrimaryAttack = true;
                }
                return;
            }

            if (_attackCooldownTimer <= 0f)
            {
                TriggerNextComboMove();
            }
        }

        public void OnSecondaryAimState(bool held, bool justReleased)
        {
            if (_owner != null && (_owner.IsDead || _owner.IsDodging || _owner.inputLocked))
            {
                if (_isChargingThrust)
                {
                    CancelChargeState();
                }
                return;
            }

            if (_isDashingThrust) return;

            if (held)
            {
                if (_thrustCooldownTimer > 0f) return;

                if (!_isChargingThrust)
                {
                    BeginThrustCharge();
                }
            }
            else if (_isChargingThrust)
            {
                float chargedTime = _chargeTimer;
                CancelChargeState();

                if (chargedTime >= minChargeThreshold)
                {
                    ExecuteFlameThrustDash(chargedTime);
                }
            }
        }

        public void CancelActiveAttack()
        {
            _activeMove = ArdenteMoveType.None;
            _moveTimer = 0f;
            _hitApplied = false;
            _bufferedPrimaryAttack = false;
            if (_slashTrail != null) _slashTrail.emitting = false;
        }

        private void CancelChargeState()
        {
            _isChargingThrust = false;
            _chargeTimer = 0f;
            SetTelegraphVisible(false);
        }

        private void CancelChargeAndDash()
        {
            CancelChargeState();
            _isDashingThrust = false;
            _dashTimer = 0f;
            _dashHitRoots.Clear();
        }

        public void ResolveSwordHierarchy()
        {
            if (weaponMount == null)
            {
                weaponMount = transform.Find("Weapon_Mount_Pivot");
            }

            if (swordRoot == null && weaponMount != null)
            {
                swordRoot = weaponMount.Find("Lamina_Ardente");
            }
            if (swordRoot == null)
            {
                swordRoot = FindDescendantByName(transform, "Lamina_Ardente");
            }

            if (swordRoot != null)
            {
                if (hiltGuard == null) hiltGuard = FindDescendantByName(swordRoot, "Ardente_Hilt_Guard");
                if (bladePart == null) bladePart = FindDescendantByName(swordRoot, "Ardente_Blade");
                if (emberCore == null) emberCore = FindDescendantByName(swordRoot, "Ardente_Ember_Core");

                _hiltRenderers = hiltGuard != null
                    ? hiltGuard.GetComponentsInChildren<Renderer>(true)
                    : System.Array.Empty<Renderer>();
                _bladeRenderers = bladePart != null
                    ? bladePart.GetComponentsInChildren<Renderer>(true)
                    : swordRoot.GetComponentsInChildren<Renderer>(true);
                _emberRenderers = emberCore != null
                    ? emberCore.GetComponentsInChildren<Renderer>(true)
                    : System.Array.Empty<Renderer>();

                if (bladeMaterial == null && _bladeRenderers.Length > 0 && _bladeRenderers[0] != null)
                {
                    bladeMaterial = _bladeRenderers[0].sharedMaterial;
                }
                if (emberCoreMaterial == null && _emberRenderers.Length > 0 && _emberRenderers[0] != null)
                {
                    emberCoreMaterial = _emberRenderers[0].sharedMaterial;
                }
            }
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

        private void Update()
        {
            if (!_runtimeInitialized) EnsureRuntimeInitialized();

            if (_owner != null && _owner.IsDead)
            {
                CancelChargeAndDash();
                CancelActiveAttack();
                _ardenteGlowTimer = 0f;
                ApplyModoArdenteVisuals(0f);
                return;
            }

            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            if (_attackCooldownTimer > 0f) _attackCooldownTimer = Mathf.Max(0f, _attackCooldownTimer - dt);
            if (_thrustCooldownTimer > 0f) _thrustCooldownTimer = Mathf.Max(0f, _thrustCooldownTimer - dt);
            if (_ardenteGlowTimer > 0f) _ardenteGlowTimer = Mathf.Max(0f, _ardenteGlowTimer - dt);

            if (_isChargingThrust)
            {
                _chargeTimer += dt;
                AimCharacterTowardCursor(dt);
                UpdateThrustTelegraph();
            }
            else if (_isDashingThrust)
            {
                TickFlameThrustDash(dt);
            }
            else if (_activeMove != ArdenteMoveType.None)
            {
                _moveTimer -= dt;
                float progress = 1f - Mathf.Clamp01(_moveTimer / Mathf.Max(0.01f, _moveDuration));
                float hitPoint = _activeMove == ArdenteMoveType.QuickStinger ? 0.42f : 0.38f;

                if (!_hitApplied && progress >= hitPoint)
                {
                    _hitApplied = true;
                    ExecuteComboHitbox();
                }

                if (_moveTimer <= 0f)
                {
                    _activeMove = ArdenteMoveType.None;
                    _comboResetTimer = comboResetWindow;

                    if (_bufferedPrimaryAttack && _attackCooldownTimer <= 0f)
                    {
                        _bufferedPrimaryAttack = false;
                        TriggerNextComboMove();
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

            // Fallback standalone caso nao esteja sob SimplePlayerController
            if (!_equippedToOwner && _owner == null)
            {
                UpdateStandaloneFallback();
            }

            UpdateModoArdenteVisuals(dt);
        }

        private void LateUpdate()
        {
            if (_owner != null && _owner.IsDead) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            UpdateVirusArmPose(dt);
            UpdateWeaponMountPose(dt);
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

        // =========================================================================
        // COMBO RAPIDO DE 4 GOLPES (BOTAO ESQUERDO)
        // =========================================================================

        private void TriggerNextComboMove()
        {
            EnsureRuntimeInitialized();
            int nextStep = (_comboStep % 4) + 1;
            float staminaCost = nextStep == 4 ? comboFinisherStaminaCost : comboLightStaminaCost;
            if (_owner != null && !_owner.TryConsumeActionStamina(staminaCost)) return;

            Transform ownerTf = _owner != null ? _owner.transform : transform;

            if (TryGetCursorWorldTarget(ownerTf.position.y + 1.0f, out Vector3 cursorPt))
            {
                Vector3 toCursor = cursorPt - ownerTf.position;
                toCursor.y = 0f;
                if (toCursor.sqrMagnitude > 0.04f)
                {
                    ownerTf.rotation = Quaternion.LookRotation(toCursor.normalized, Vector3.up);
                }
            }

            _comboStep = nextStep;

            switch (_comboStep)
            {
                case 1:
                    _activeMove = ArdenteMoveType.SlashFastR2L;
                    _moveDuration = slashR2LDuration;
                    _attackCooldownTimer = 0.16f;
                    _owner?.ApplyCombatLunge(ownerTf.forward, 3.8f, 0.09f);
                    CombatSfxBank.Play(CombatSfxId.FlameSlashLight, 0.90f, 0.96f, 1.05f, 0.03f);
                    break;
                case 2:
                    _activeMove = ArdenteMoveType.SlashFastL2R;
                    _moveDuration = slashL2RDuration;
                    _attackCooldownTimer = 0.15f;
                    _owner?.ApplyCombatLunge(ownerTf.forward, 4.1f, 0.09f);
                    CombatSfxBank.Play(CombatSfxId.FlameSlashLightAlt, 0.92f, 0.97f, 1.06f, 0.03f);
                    break;
                case 3:
                    _activeMove = ArdenteMoveType.SlashLowCross;
                    _moveDuration = slashLowCrossDuration;
                    _attackCooldownTimer = 0.16f;
                    _owner?.ApplyCombatLunge(ownerTf.forward, 4.5f, 0.10f);
                    CombatSfxBank.Play(CombatSfxId.FlameSlashLight, 0.94f, 0.92f, 1.01f, 0.03f);
                    break;
                default:
                    _activeMove = ArdenteMoveType.QuickStinger;
                    _moveDuration = quickStingerDuration;
                    _attackCooldownTimer = 0.22f;
                    _owner?.ApplyCombatLunge(ownerTf.forward, 7.6f, 0.12f);
                    CombatSfxBank.Play(CombatSfxId.KatanaIaidoHeavy, 0.96f, 0.96f, 1.05f, 0.03f);
                    break;
            }

            _owner?.ApplyAttackMovementSlow(0.76f, _moveDuration * 0.65f);
            _moveTimer = _moveDuration;
            _hitApplied = false;
            _comboResetTimer = comboResetWindow;

            if (_slashTrail != null)
            {
                ConfigureTrailVisuals(_ardenteGlowTimer > 0.05f);
                _slashTrail.Clear();
                _slashTrail.emitting = false;
            }
        }

        private void ExecuteComboHitbox()
        {
            Transform ownerTf = _owner != null ? _owner.transform : transform;
            bool isStinger = _activeMove == ArdenteMoveType.QuickStinger;
            bool inArdenteMode = _ardenteGlowTimer > 0.05f;

            float reach = isStinger ? stingerReach : slashReach;
            float radius = isStinger ? 1.05f : 1.50f;
            int dmgIdx = Mathf.Clamp(_comboStep - 1, 0, comboDamages.Length - 1);
            float baseDmg = (comboDamages != null && comboDamages.Length > 0) ? comboDamages[dmgIdx] : 12f;
            if (inArdenteMode)
            {
                baseDmg += ardenteModeSlashBonusDamage;
            }
            baseDmg *= StomachAbyss.Progression.BiomassProgressionSystem.GetDamageMultiplier();

            Vector3 hitCenter = ownerTf.position + ownerTf.forward * (reach * 0.56f) + Vector3.up * 0.95f;
            Vector3 swingDir = _activeMove switch
            {
                ArdenteMoveType.SlashFastR2L => -ownerTf.right + ownerTf.forward * 0.45f,
                ArdenteMoveType.SlashFastL2R => ownerTf.right + ownerTf.forward * 0.45f,
                ArdenteMoveType.SlashLowCross => (ownerTf.right + Vector3.up * 0.55f + ownerTf.forward * 0.45f),
                _ => ownerTf.forward
            };
            swingDir.Normalize();

            _swingHitRoots.Clear();
            bool hitAnything = false;
            bool hitBurnBurst = false;

            Collider[] cols = Physics.OverlapSphere(hitCenter, radius, ~0, QueryTriggerInteraction.Collide);
            if (cols != null)
            {
                for (int i = 0; i < cols.Length; i++)
                {
                    Collider c = cols[i];
                    if (c == null || IsPlayerCollider(c, ownerTf)) continue;

                    OrganPulse op = c.GetComponentInParent<OrganPulse>();
                    if (op != null)
                    {
                        op.TriggerHitRecoil();
                        hitAnything = true;
                    }

                    IAcidDamageable dmg = c.GetComponentInParent<IAcidDamageable>();
                    if (dmg == null || ReferenceEquals(dmg, _owner)) continue;

                    GameObject targetRoot = ResolveTargetRoot(dmg);
                    if (targetRoot == null || !_swingHitRoots.Add(targetRoot)) continue;

                    Vector3 impactPt = c.ClosestPoint(hitCenter);
                    BurnStatusEffect burn = targetRoot.GetComponent<BurnStatusEffect>();
                    bool isTargetBurning = burn != null && burn.IsBurning;

                    float slashDamageToApply = baseDmg;
                    bool isBossTarget = targetRoot.GetComponent<TeniaBossHealth>() != null;
                    if (isTargetBurning && isBossTarget)
                    {
                        // Evita que a janela de deduplicacao do Boss (0.16s) descarte o +10 de Flame Fan no mesmo frame
                        slashDamageToApply += flameFanBonusDamage;
                    }

                    BloodSplatterSystem.BeginAttackBurstContext(swingDir, impactPt, isStinger ? 0.92f : 0.72f, false);
                    CombatSfxBank.BeginImpactScope(false);
                    try
                    {
                        dmg.TakeAcidDamage(slashDamageToApply);
                    }
                    finally
                    {
                        if (!BloodSplatterSystem.WasEntityHandledInContext)
                        {
                            BloodSplatterSystem.SpawnBloodBurst(impactPt, swingDir, isStinger ? 0.92f : 0.72f, null, false);
                        }
                        if (!CombatSfxBank.WasImpactClaimedInScope)
                        {
                            CombatSfxBank.PlayImpact(false, impactPt, 0.76f);
                        }
                        BloodSplatterSystem.EndAttackBurstContext();
                        CombatSfxBank.EndImpactScope(false);
                    }

                    if (isTargetBurning)
                    {
                        burn.ExtendBurn(flameFanExtendSeconds);
                        burn.TriggerFlameFanBonus(isBossTarget ? 0f : flameFanBonusDamage);
                        hitBurnBurst = true;
                    }

                    EmitSparksAt(impactPt, inArdenteMode || isTargetBurning ? 11 : 6);
                    hitAnything = true;
                }
            }

            if (TriggerStomachPropInteractions(hitCenter, radius))
            {
                hitAnything = true;
            }

            if (hitBurnBurst)
            {
                CombatSfxBank.PlayAt(CombatSfxId.FlameBurnBurst, hitCenter, 0.88f, 0.96f, 1.08f, 0.35f, 0.04f);
            }

            if (hitAnything)
            {
                CombatHitstopManager.TriggerHitstop(lightHitstopDuration, 0.04f, false);
                CombatCameraJuice.Shake(0.14f, 0.09f);
                _isoCam?.TriggerShake(isStinger ? cameraShakeIntensity * 1.15f : cameraShakeIntensity * 0.8f, 0.08f);
            }
        }

        // =========================================================================
        // ESTOCADA DE FOGO (SEGURAR & SOLTAR BOTAO DIREITO)
        // =========================================================================

        private void BeginThrustCharge()
        {
            EnsureRuntimeInitialized();
            _isChargingThrust = true;
            _chargeTimer = 0f;
            CancelActiveAttack();

            UpdateThrustTelegraph();
            SetTelegraphVisible(true);
            CombatSfxBank.Play(CombatSfxId.FlameChargeIgnite, 0.82f, 0.95f, 1.06f, 0.12f);
        }

        private void ExecuteFlameThrustDash(float chargedTime)
        {
            if (_owner != null && !_owner.TryConsumeActionStamina(flameThrustStaminaCost)) return;
            Transform ownerTf = _owner != null ? _owner.transform : transform;
            float chargeNorm = Mathf.InverseLerp(minChargeThreshold, Mathf.Max(minChargeThreshold + 0.05f, fullChargeTime), chargedTime);
            float powerScale = Mathf.Lerp(0.75f, 1.0f, chargeNorm);

            _dashDirection = GetAimDirectionOnPlane();
            ownerTf.rotation = Quaternion.LookRotation(_dashDirection, Vector3.up);

            _isDashingThrust = true;
            _dashTimer = thrustDashDuration;
            _activeDashDistance = thrustDashDistance * powerScale;
            _activeDashDamage = Mathf.Round(thrustPierceDamage * powerScale * StomachAbyss.Progression.BiomassProgressionSystem.GetDamageMultiplier());
            _dashHitRoots.Clear();
            _dashHitstopTriggered = false;

            _ardenteGlowTimer = ardenteModeDuration;
            _thrustCooldownTimer = thrustDashCooldown;

            float dashSpeed = _activeDashDistance / Mathf.Max(0.05f, thrustDashDuration);
            _owner?.ApplyCombatLunge(_dashDirection, dashSpeed, thrustDashDuration);
            _owner?.TriggerWeaponFlash(new Color(2.4f, 0.85f, 0.18f, 1f), 0.24f, 2.4f);

            CombatSfxBank.Play(CombatSfxId.FlameThrustDash, 0.94f, 0.95f, 1.05f, 0.05f);
            CombatCameraJuice.PunchFov(3.8f, 0.26f);
            CombatCameraJuice.Shake(0.38f, 0.22f);
            _isoCam?.TriggerShake(0.14f, 0.14f);

            _lastTrailSpawnPos = ownerTf.position;
            SpawnGroundTrailAt(ownerTf.position + _dashDirection * 0.35f);
            EmitSparksAt(ownerTf.position + _dashDirection * 0.95f + Vector3.up * 0.95f, 16);

            if (_slashTrail != null)
            {
                ConfigureTrailVisuals(true);
                _slashTrail.Clear();
                _slashTrail.emitting = true;
            }
        }

        private void TickFlameThrustDash(float dt)
        {
            Transform ownerTf = _owner != null ? _owner.transform : transform;
            _dashTimer -= dt;

            // Spawna placas de fogo no chao a cada ~0.85m percorridos durante a estocada
            Vector3 curPos = ownerTf.position;
            Vector3 flatDelta = new Vector3(curPos.x - _lastTrailSpawnPos.x, 0f, curPos.z - _lastTrailSpawnPos.z);
            float movedDist = flatDelta.magnitude;
            while (movedDist >= flameTrailSpacing)
            {
                Vector3 stepDir = flatDelta / movedDist;
                _lastTrailSpawnPos += stepDir * flameTrailSpacing;
                SpawnGroundTrailAt(_lastTrailSpawnPos);
                flatDelta = new Vector3(curPos.x - _lastTrailSpawnPos.x, 0f, curPos.z - _lastTrailSpawnPos.z);
                movedDist = flatDelta.magnitude;
            }

            // Verifica acertos perfurantes no corredor frontal da estocada
            CheckFlameThrustCorridorHits(ownerTf);

            if (_dashTimer <= 0f)
            {
                _isDashingThrust = false;
                if (movedDist >= 0.32f)
                {
                    SpawnGroundTrailAt(curPos);
                }
                if (_slashTrail != null)
                {
                    _slashTrail.emitting = false;
                }
            }
        }

        private void CheckFlameThrustCorridorHits(Transform ownerTf)
        {
            float probeRadius = thrustDashWidth * 0.54f;
            Vector3 probeNear = ownerTf.position + _dashDirection * 0.55f + Vector3.up * 0.95f;
            Vector3 probeFar = ownerTf.position + _dashDirection * 1.65f + Vector3.up * 0.95f;

            Collider[] cols = Physics.OverlapCapsule(probeNear, probeFar, probeRadius, ~0, QueryTriggerInteraction.Collide);
            if (cols != null)
            {
                for (int i = 0; i < cols.Length; i++)
                {
                    Collider c = cols[i];
                    if (c == null || IsPlayerCollider(c, ownerTf)) continue;

                    OrganPulse op = c.GetComponentInParent<OrganPulse>();
                    if (op != null) op.TriggerHitRecoil();

                    IAcidDamageable dmg = c.GetComponentInParent<IAcidDamageable>();
                    if (dmg == null || ReferenceEquals(dmg, _owner)) continue;

                    GameObject targetRoot = ResolveTargetRoot(dmg);
                    if (targetRoot == null || !_dashHitRoots.Add(targetRoot)) continue;

                    Vector3 impactPt = c.ClosestPoint(probeFar);

                    BloodSplatterSystem.BeginAttackBurstContext(_dashDirection, impactPt, 1.32f, true);
                    CombatSfxBank.BeginImpactScope(true);
                    try
                    {
                        dmg.TakeAcidDamage(_activeDashDamage);
                    }
                    finally
                    {
                        if (!BloodSplatterSystem.WasEntityHandledInContext)
                        {
                            BloodSplatterSystem.SpawnBloodBurst(impactPt, _dashDirection, 1.32f, null, true);
                        }
                        if (!CombatSfxBank.WasImpactClaimedInScope)
                        {
                            CombatSfxBank.PlayImpact(true, impactPt, 0.92f);
                        }
                        BloodSplatterSystem.EndAttackBurstContext();
                        CombatSfxBank.EndImpactScope(true);
                    }

                    BurnStatusEffect burn = targetRoot.GetComponent<BurnStatusEffect>();
                    if (burn == null) burn = targetRoot.AddComponent<BurnStatusEffect>();
                    burn.ApplyBurn(_owner != null ? _owner.gameObject : gameObject, burnDuration, burnDps, pixelFireMaterial);

                    EmitSparksAt(impactPt, 16);

                    if (!_dashHitstopTriggered)
                    {
                        _dashHitstopTriggered = true;
                        CombatHitstopManager.TriggerHitstop(0.032f, 0.04f, true);
                    }
                }
            }

            TriggerStomachPropInteractions(probeFar, probeRadius + 0.35f);
        }

        private void SpawnGroundTrailAt(Vector3 worldPos)
        {
            GameObject ownerGo = _owner != null ? _owner.gameObject : gameObject;
            FlameGroundTrail.SpawnPatch(
                worldPos,
                ownerGo,
                flameTrailRadius,
                flameTrailDuration,
                flameTrailDps,
                null,
                pixelFireMaterial
            );
        }

        private bool TriggerStomachPropInteractions(Vector3 hitCenter, float radius)
        {
            bool hitAny = false;

            AcidBubblePopper[] bubbles = FindObjectsByType<AcidBubblePopper>();
            for (int i = 0; i < bubbles.Length; i++)
            {
                AcidBubblePopper b = bubbles[i];
                if (b != null && Vector3.Distance(hitCenter, b.transform.position) <= radius + 0.65f)
                {
                    b.PopByAttack();
                    EmitSparksAt(b.transform.position, 9);
                    hitAny = true;
                }
            }

            UlcerEruption[] ulcers = FindObjectsByType<UlcerEruption>();
            for (int i = 0; i < ulcers.Length; i++)
            {
                UlcerEruption u = ulcers[i];
                if (u != null && Vector3.Distance(hitCenter, u.transform.position) <= radius + 0.95f)
                {
                    u.TriggerEruption();
                    EmitSparksAt(u.transform.position, 9);
                    hitAny = true;
                }
            }

            FleshEyeDoorController[] doors = FindObjectsByType<FleshEyeDoorController>();
            for (int i = 0; i < doors.Length; i++)
            {
                FleshEyeDoorController door = doors[i];
                if (door != null && Vector3.Distance(hitCenter, door.transform.position) <= radius + 1.4f)
                {
                    door.EmitMouthBurst(12, new Color(1.0f, 0.45f, 0.08f, 1f), 4.0f);
                    hitAny = true;
                }
            }

            return hitAny;
        }

        // =========================================================================
        // TELEGRAFO LUMINOSO NO CHAO (FAIXA DE ESTOCADA ~4.4m x 1.2m)
        // =========================================================================

        private void EnsureThrustTelegraph()
        {
            if (_telegraphRoot != null) return;

            _telegraphMpb = new MaterialPropertyBlock();
            _telegraphRoot = new GameObject("Ardente_ThrustTelegraph");
            _telegraphRoot.hideFlags = HideFlags.DontSave;
            _telegraphRoot.SetActive(false);

            _telegraphBorderMat = CreateTransparentUnlitMaterial(Color.white);
            _telegraphBaseMat = CreateTransparentUnlitMaterial(new Color(1.0f, 0.36f, 0.05f, 0.20f));
            _telegraphChargeMat = CreateTransparentUnlitMaterial(new Color(1.0f, 0.72f, 0.16f, 0.45f));

            // Borda externa com ponta em seta na direcao do avanco
            GameObject borderGo = new GameObject("LaneBorder");
            borderGo.transform.SetParent(_telegraphRoot.transform, false);
            _telegraphBorder = borderGo.AddComponent<LineRenderer>();
            _telegraphBorder.useWorldSpace = false;
            _telegraphBorder.loop = true;
            _telegraphBorder.positionCount = 5;
            _telegraphBorder.startWidth = 0.065f;
            _telegraphBorder.endWidth = 0.065f;
            _telegraphBorder.startColor = new Color(1.0f, 0.56f, 0.12f, 0.95f);
            _telegraphBorder.endColor = new Color(1.0f, 0.56f, 0.12f, 0.95f);
            _telegraphBorder.shadowCastingMode = ShadowCastingMode.Off;
            _telegraphBorder.receiveShadows = false;
            _telegraphBorder.sharedMaterial = _telegraphBorderMat;

            Mesh quadMesh = BuildFloorUnitQuadMesh();
            _telegraphBaseFill = CreateTelegraphQuad("BaseLaneFill", quadMesh, _telegraphBaseMat, 0.008f);
            _telegraphChargeFill = CreateTelegraphQuad("ChargeLaneFill", quadMesh, _telegraphChargeMat, 0.016f);
        }

        private MeshRenderer CreateTelegraphQuad(string name, Mesh mesh, Material mat, float localY)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(_telegraphRoot.transform, false);
            go.transform.localPosition = new Vector3(0f, localY, 0f);

            MeshFilter mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;

            MeshRenderer mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return mr;
        }

        private void SetTelegraphVisible(bool visible)
        {
            if (_telegraphRoot != null && _telegraphRoot.activeSelf != visible)
            {
                _telegraphRoot.SetActive(visible);
            }
        }

        private void UpdateThrustTelegraph()
        {
            if (_telegraphRoot == null) return;

            Transform ownerTf = _owner != null ? _owner.transform : transform;
            Vector3 aimDir = GetAimDirectionOnPlane();
            GameObject ownerGo = ownerTf.gameObject;

            float groundY = FlameGroundTrail.SampleGroundY(ownerTf.position + aimDir * 0.6f, ownerGo, 0.055f);
            _telegraphRoot.transform.position = new Vector3(ownerTf.position.x, groundY, ownerTf.position.z);
            _telegraphRoot.transform.rotation = Quaternion.LookRotation(aimDir, Vector3.up);

            float fullLen = thrustDashDistance;
            float halfW = thrustDashWidth * 0.5f;
            float tipLen = fullLen + 0.35f;

            if (_telegraphBorder != null)
            {
                _telegraphBorder.SetPosition(0, new Vector3(-halfW, 0.02f, 0f));
                _telegraphBorder.SetPosition(1, new Vector3(-halfW, 0.02f, fullLen - 0.30f));
                _telegraphBorder.SetPosition(2, new Vector3(0f, 0.02f, tipLen));
                _telegraphBorder.SetPosition(3, new Vector3(halfW, 0.02f, fullLen - 0.30f));
                _telegraphBorder.SetPosition(4, new Vector3(halfW, 0.02f, 0f));
            }

            if (_telegraphBaseFill != null)
            {
                _telegraphBaseFill.transform.localScale = new Vector3(thrustDashWidth * 0.94f, 1f, fullLen);
            }

            float chargeNorm = Mathf.Clamp01(_chargeTimer / Mathf.Max(0.05f, fullChargeTime));
            bool ready = _chargeTimer >= minChargeThreshold;
            bool full = _chargeTimer >= fullChargeTime;
            float pulse = full ? (0.85f + 0.15f * Mathf.Sin(Time.time * 24f)) : 1f;

            if (_telegraphChargeFill != null)
            {
                float curLen = Mathf.Max(0.15f, fullLen * chargeNorm);
                _telegraphChargeFill.transform.localScale = new Vector3(thrustDashWidth * 0.88f, 1f, curLen);
            }

            Color borderCol = full
                ? new Color(1.0f, 0.88f, 0.30f, 0.98f * pulse)
                : (ready ? new Color(1.0f, 0.56f, 0.10f, 0.95f) : new Color(0.95f, 0.32f, 0.08f, 0.65f));
            Color fillCol = full
                ? new Color(1.0f, 0.68f, 0.14f, 0.48f * pulse)
                : (ready ? new Color(1.0f, 0.46f, 0.08f, 0.36f) : new Color(0.85f, 0.24f, 0.05f, 0.22f));

            if (_telegraphBorder != null)
            {
                _telegraphBorder.startColor = borderCol;
                _telegraphBorder.endColor = borderCol;
            }
            ApplyTelegraphColor(_telegraphChargeFill, fillCol);
        }

        private void ApplyTelegraphColor(Renderer r, Color col)
        {
            if (r == null) return;
            if (_telegraphMpb == null) _telegraphMpb = new MaterialPropertyBlock();
            _telegraphMpb.Clear();
            _telegraphMpb.SetColor(PropBaseColor, col);
            _telegraphMpb.SetColor(PropColor, col);
            r.SetPropertyBlock(_telegraphMpb);
        }

        // =========================================================================
        // MODO NORMAL VS MODO ARDENTE (EMISSAO + PARTICULAS + LUZ + TRAIL)
        // =========================================================================

        private void UpdateModoArdenteVisuals(float dt)
        {
            float targetGlow = 0f;
            if (_isDashingThrust)
            {
                targetGlow = 1.25f;
            }
            else if (_isChargingThrust)
            {
                float chargeNorm = Mathf.Clamp01(_chargeTimer / Mathf.Max(0.05f, fullChargeTime));
                targetGlow = Mathf.Lerp(0.32f, 1.18f, chargeNorm);
            }
            else if (_ardenteGlowTimer > 0f)
            {
                float fadeOut = _ardenteGlowTimer < 0.5f ? Mathf.Clamp01(_ardenteGlowTimer / 0.5f) : 1f;
                float flicker = 0.95f + 0.18f * Mathf.Sin(Time.time * 21f);
                targetGlow = fadeOut * flicker;
            }

            if (Mathf.Abs(targetGlow - _lastAppliedArdenteGlow) > 0.015f || targetGlow > 0.05f)
            {
                ApplyModoArdenteVisuals(targetGlow);
            }

            if (_isChargingThrust && _bladeTipAnchor != null && Time.frameCount % 3 == 0)
            {
                EmitSparksAt(_bladeTipAnchor.position, 1);
            }
        }

        private void ApplyModoArdenteVisuals(float glowLevel)
        {
            _lastAppliedArdenteGlow = glowLevel;
            if (_emissionMpb == null) _emissionMpb = new MaterialPropertyBlock();

            float clampedT = Mathf.Clamp01(glowLevel);
            Color bladeEmis = Color.Lerp(NormalBladeEmission, ArdenteBladeEmission, clampedT) * Mathf.Max(1f, glowLevel);
            Color emberEmis = Color.Lerp(NormalEmberEmission, ArdenteEmberEmission, clampedT) * Mathf.Max(1f, glowLevel);
            Color hiltEmis = Color.Lerp(Color.black, ArdenteHiltEmission, clampedT);

            ApplyEmissionToRenderers(_bladeRenderers, bladeEmis);
            ApplyEmissionToRenderers(_emberRenderers, emberEmis);
            ApplyEmissionToRenderers(_hiltRenderers, hiltEmis);

            if (_bladeFireParticles != null)
            {
                var em = _bladeFireParticles.emission;
                bool activeFire = glowLevel > 0.15f && _equippedToOwner;
                em.enabled = activeFire;
                if (activeFire)
                {
                    em.rateOverTime = Mathf.Lerp(14f, 44f, clampedT);
                    if (!_bladeFireParticles.isPlaying) _bladeFireParticles.Play();
                }
                else if (_bladeFireParticles.isPlaying)
                {
                    _bladeFireParticles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                }
            }

            if (_bladeGlowLight != null)
            {
                bool lightOn = glowLevel > 0.08f && _equippedToOwner;
                _bladeGlowLight.enabled = lightOn;
                _bladeGlowLight.intensity = lightOn ? Mathf.Lerp(0.25f, 1.85f, clampedT) : 0f;
            }
        }

        private void ApplyEmissionToRenderers(Renderer[] renderers, Color emission)
        {
            if (renderers == null || renderers.Length == 0) return;
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer r = renderers[i];
                if (r == null) continue;
                r.GetPropertyBlock(_emissionMpb);
                _emissionMpb.SetColor(PropEmissionColor, emission);
                r.SetPropertyBlock(_emissionMpb);
            }
        }

        // =========================================================================
        // POSE HIBRIDA NO WEAPON_MOUNT_PIVOT & OSSOS DO VIRUS_MODEL
        // =========================================================================

        private void UpdateVirusArmPose(float dt)
        {
            if (coreBone == null || fiberR1 == null) return;
            if (_owner != null && _owner.IsDodging) return;

            Vector3 coreEuler = Vector3.zero;
            Vector3 r1Target = new Vector3(-34f, 0f, 18f);
            Vector3 r2Target = new Vector3(-28f, 0f, 0f);
            Vector3 l1Target = Vector3.zero;
            float rWeight = 0.85f;
            float lWeight = 0f;

            if (_isDashingThrust)
            {
                rWeight = 0.98f;
                lWeight = 0.60f;
                coreEuler = new Vector3(18f, -14f, 0f);
                r1Target = new Vector3(-92f, 0f, 2f);
                r2Target = new Vector3(-2f, 0f, 0f);
                l1Target = new Vector3(26f, 0f, -24f);
            }
            else if (_isChargingThrust)
            {
                float chargeNorm = Mathf.Clamp01(_chargeTimer / Mathf.Max(0.05f, fullChargeTime));
                rWeight = 0.95f;
                lWeight = 0.45f;
                coreEuler = new Vector3(-8f * chargeNorm, 22f * chargeNorm, 0f);
                r1Target = Vector3.Lerp(new Vector3(-40f, 0f, 22f), new Vector3(-28f, 0f, 44f), chargeNorm);
                r2Target = Vector3.Lerp(new Vector3(-32f, 0f, 0f), new Vector3(-46f, 0f, 0f), chargeNorm);
                l1Target = new Vector3(-48f, 0f, -28f);
            }
            else if (_activeMove != ArdenteMoveType.None)
            {
                float p = 1f - Mathf.Clamp01(_moveTimer / Mathf.Max(0.01f, _moveDuration));
                rWeight = 0.98f;
                lWeight = 0.35f;

                switch (_activeMove)
                {
                    case ArdenteMoveType.SlashFastR2L:
                    {
                        float s = SmoothSwingCurve(p, 0.14f, 0.72f);
                        coreEuler = new Vector3(Mathf.Lerp(-4f, 8f, s), Mathf.Lerp(24f, -32f, s), 0f);
                        r1Target = Vector3.Lerp(new Vector3(-70f, 0f, 58f), new Vector3(-80f, 0f, -48f), s);
                        r2Target = new Vector3(-14f, 0f, 0f);
                        l1Target = new Vector3(14f, 0f, -18f);
                        break;
                    }
                    case ArdenteMoveType.SlashFastL2R:
                    {
                        float s = SmoothSwingCurve(p, 0.14f, 0.72f);
                        coreEuler = new Vector3(Mathf.Lerp(8f, -4f, s), Mathf.Lerp(-30f, 30f, s), 0f);
                        r1Target = Vector3.Lerp(new Vector3(-78f, 0f, -46f), new Vector3(-72f, 0f, 60f), s);
                        r2Target = new Vector3(-14f, 0f, 0f);
                        l1Target = new Vector3(12f, 0f, -14f);
                        break;
                    }
                    case ArdenteMoveType.SlashLowCross:
                    {
                        float s = SmoothSwingCurve(p, 0.15f, 0.74f);
                        coreEuler = new Vector3(Mathf.Lerp(12f, -10f, s), Mathf.Lerp(-26f, 26f, s), 0f);
                        r1Target = Vector3.Lerp(new Vector3(-36f, 0f, -44f), new Vector3(-104f, 0f, 44f), s);
                        r2Target = new Vector3(-18f, 0f, 0f);
                        l1Target = new Vector3(-16f, 0f, -20f);
                        break;
                    }
                    case ArdenteMoveType.QuickStinger:
                    {
                        float t = Mathf.Sin(Mathf.Clamp01(p / 0.76f) * Mathf.PI);
                        coreEuler = new Vector3(14f * t, -10f * t, 0f);
                        r1Target = Vector3.Lerp(new Vector3(-36f, 0f, 18f), new Vector3(-90f, 0f, 4f), t);
                        r2Target = Vector3.Lerp(new Vector3(-32f, 0f, 0f), new Vector3(-4f, 0f, 0f), t);
                        l1Target = new Vector3(18f, 0f, -18f);
                        break;
                    }
                }
            }

            if (coreEuler.sqrMagnitude > 0.01f)
            {
                coreBone.localRotation = coreBone.localRotation * Quaternion.Euler(coreEuler);
            }

            fiberR1.localRotation = Quaternion.Slerp(fiberR1.localRotation, Quaternion.Euler(r1Target), rWeight);
            if (fiberR2 != null)
            {
                fiberR2.localRotation = Quaternion.Slerp(fiberR2.localRotation, Quaternion.Euler(r2Target), rWeight);
            }
            if (fiberL1 != null && lWeight > 0.01f)
            {
                fiberL1.localRotation = Quaternion.Slerp(fiberL1.localRotation, Quaternion.Euler(l1Target), lWeight);
            }
        }

        private void UpdateWeaponMountPose(float dt)
        {
            if (weaponMount == null || swordRoot == null) return;

            Vector3 handLocalPos = new Vector3(0.46f, 0.92f, 0.34f);
            if (fiberRTip != null)
            {
                handLocalPos = transform.InverseTransformPoint(fiberRTip.position) + handGripLocalOffset;
            }

            if (_isDashingThrust)
            {
                if (_slashTrail != null) _slashTrail.emitting = true;
                Vector3 thrustPos = handLocalPos + new Vector3(-0.04f, 0.05f, 0.62f);
                weaponMount.localPosition = Vector3.Lerp(weaponMount.localPosition, thrustPos, dt * 40f);
                weaponMount.localRotation = Quaternion.Slerp(weaponMount.localRotation, Quaternion.identity, dt * 36f);
                swordRoot.localRotation = Quaternion.Slerp(swordRoot.localRotation, Quaternion.Euler(90f, 0f, 0f), dt * 36f);
                return;
            }

            if (_isChargingThrust)
            {
                if (_slashTrail != null) _slashTrail.emitting = false;
                float chargeNorm = Mathf.Clamp01(_chargeTimer / Mathf.Max(0.05f, fullChargeTime));
                float jitter = Mathf.Sin(Time.time * 48f) * 0.012f * chargeNorm;
                Vector3 coilPos = handLocalPos + new Vector3(0.10f * chargeNorm + jitter, 0.06f * chargeNorm, -0.30f * chargeNorm);
                weaponMount.localPosition = Vector3.Lerp(weaponMount.localPosition, coilPos, dt * 24f);
                weaponMount.localRotation = Quaternion.Slerp(weaponMount.localRotation, Quaternion.Euler(6f, -18f * chargeNorm, 0f), dt * 24f);
                swordRoot.localRotation = Quaternion.Slerp(swordRoot.localRotation, Quaternion.Euler(86f, 0f, 0f), dt * 24f);
                return;
            }

            if (_activeMove != ArdenteMoveType.None)
            {
                float p = 1f - Mathf.Clamp01(_moveTimer / Mathf.Max(0.01f, _moveDuration));
                if (_slashTrail != null)
                {
                    _slashTrail.emitting = p >= 0.10f && p <= 0.82f;
                }

                switch (_activeMove)
                {
                    case ArdenteMoveType.SlashFastR2L:
                    case ArdenteMoveType.SlashFastL2R:
                    {
                        float dirSign = _activeMove == ArdenteMoveType.SlashFastR2L ? 1f : -1f;
                        float s = Mathf.Clamp01(SmoothSwingCurve(p, 0.14f, 0.72f));
                        float yaw = Mathf.Lerp(102f * dirSign, -104f * dirSign, s);
                        weaponMount.localPosition = Vector3.Lerp(weaponMount.localPosition, handLocalPos, dt * 38f);
                        weaponMount.localRotation = Quaternion.Euler(0f, yaw, 0f);
                        swordRoot.localRotation = Quaternion.Euler(90f, 0f, 88f * dirSign);
                        break;
                    }
                    case ArdenteMoveType.SlashLowCross:
                    {
                        float s = Mathf.Clamp01(SmoothSwingCurve(p, 0.15f, 0.74f));
                        float yaw = Mathf.Lerp(-94f, 96f, s);
                        float pitch = Mathf.Lerp(26f, -24f, s);
                        weaponMount.localPosition = Vector3.Lerp(weaponMount.localPosition, handLocalPos, dt * 38f);
                        weaponMount.localRotation = Quaternion.Euler(pitch, yaw, 0f);
                        swordRoot.localRotation = Quaternion.Euler(90f, 0f, -64f);
                        break;
                    }
                    case ArdenteMoveType.QuickStinger:
                    {
                        float thrust = Mathf.Sin(Mathf.Clamp01(p / 0.78f) * Mathf.PI);
                        Vector3 thrustPos = handLocalPos + new Vector3(-0.04f * thrust, 0.06f * thrust, 0.52f * thrust);
                        weaponMount.localPosition = Vector3.Lerp(weaponMount.localPosition, thrustPos, dt * 40f);
                        weaponMount.localRotation = Quaternion.Slerp(weaponMount.localRotation, Quaternion.identity, dt * 32f);
                        swordRoot.localRotation = Quaternion.Slerp(swordRoot.localRotation, Quaternion.Euler(90f, 0f, 0f), dt * 32f);
                        break;
                    }
                }
            }
            else
            {
                if (_slashTrail != null) _slashTrail.emitting = false;
                weaponMount.localPosition = Vector3.Lerp(weaponMount.localPosition, handLocalPos, dt * 22f);
                weaponMount.localRotation = Quaternion.Slerp(weaponMount.localRotation, Quaternion.identity, dt * 22f);
                swordRoot.localRotation = Quaternion.Slerp(swordRoot.localRotation, Quaternion.Euler(idleHandSaberEuler), dt * 22f);
            }
        }

        // =========================================================================
        // FX DA LAMINA & MIRA NO CURSOR (MESMO PADRAO DA ESPADA CRUZ)
        // =========================================================================

        private void EnsureSlashTrail()
        {
            if (swordRoot == null) return;
            EnsureBladeTipAnchor();

            Transform parentTf = _bladeTipAnchor != null ? _bladeTipAnchor : swordRoot;
            Transform existing = parentTf.Find("Ardente_SlashTrail");
            if (existing != null)
            {
                _slashTrail = existing.GetComponent<TrailRenderer>();
            }
            if (_slashTrail == null)
            {
                GameObject go = new GameObject("Ardente_SlashTrail");
                go.transform.SetParent(parentTf, false);
                go.transform.localPosition = Vector3.zero;
                _slashTrail = go.AddComponent<TrailRenderer>();
            }

            if (_trailMat == null)
            {
                Shader unlit = Shader.Find("Universal Render Pipeline/Particles/Unlit");
                if (unlit == null) unlit = Shader.Find("Universal Render Pipeline/Unlit");
                if (unlit == null) unlit = Shader.Find("Sprites/Default");
                if (unlit != null)
                {
                    _trailMat = new Material(unlit);
                    _trailMat.color = Color.white;
                }
            }

            _slashTrail.time = 0.13f;
            _slashTrail.minVertexDistance = 0.05f;
            _slashTrail.shadowCastingMode = ShadowCastingMode.Off;
            _slashTrail.receiveShadows = false;
            _slashTrail.alignment = LineAlignment.View;
            if (_trailMat != null) _slashTrail.sharedMaterial = _trailMat;

            ConfigureTrailVisuals(false);
            _slashTrail.emitting = false;
        }

        private void ConfigureTrailVisuals(bool ardenteMode)
        {
            if (_slashTrail == null) return;

            float width = ardenteMode ? 0.24f : 0.17f;
            _slashTrail.widthCurve = new AnimationCurve(
                new Keyframe(0f, width),
                new Keyframe(0.45f, width * 0.50f),
                new Keyframe(1f, 0f)
            );

            Gradient grad = new Gradient();
            if (ardenteMode)
            {
                grad.SetKeys(
                    new[]
                    {
                        new GradientColorKey(new Color(1.0f, 0.94f, 0.58f), 0f),
                        new GradientColorKey(new Color(1.0f, 0.45f, 0.08f), 0.45f),
                        new GradientColorKey(new Color(0.68f, 0.08f, 0.04f), 1f)
                    },
                    new[]
                    {
                        new GradientAlphaKey(0.95f, 0f),
                        new GradientAlphaKey(0.60f, 0.5f),
                        new GradientAlphaKey(0f, 1f)
                    }
                );
            }
            else
            {
                grad.SetKeys(
                    new[]
                    {
                        new GradientColorKey(new Color(1.0f, 0.48f, 0.34f), 0f),
                        new GradientColorKey(new Color(0.86f, 0.12f, 0.10f), 0.5f),
                        new GradientColorKey(new Color(0.38f, 0.04f, 0.04f), 1f)
                    },
                    new[]
                    {
                        new GradientAlphaKey(0.88f, 0f),
                        new GradientAlphaKey(0.45f, 0.5f),
                        new GradientAlphaKey(0f, 1f)
                    }
                );
            }
            _slashTrail.colorGradient = grad;
        }

        private void EnsureBladeTipAnchor()
        {
            if (swordRoot == null || _bladeTipAnchor != null) return;

            Transform existing = swordRoot.Find("Ardente_BladeTip");
            if (existing != null)
            {
                _bladeTipAnchor = existing;
                return;
            }

            Vector3 tipLocalPos = new Vector3(0f, 1.45f, 0f);
            if (bladePart != null)
            {
                MeshFilter mf = bladePart.GetComponentInChildren<MeshFilter>(true);
                if (mf != null && mf.sharedMesh != null)
                {
                    Bounds b = mf.sharedMesh.bounds;
                    Vector3 worldCenter = mf.transform.TransformPoint(new Vector3(b.center.x, b.max.y * 0.92f, b.center.z));
                    Vector3 rootSpace = swordRoot.InverseTransformPoint(worldCenter);
                    if (rootSpace.sqrMagnitude > 0.05f)
                    {
                        tipLocalPos = rootSpace;
                    }
                }
            }

            GameObject tipGo = new GameObject("Ardente_BladeTip");
            tipGo.transform.SetParent(swordRoot, false);
            tipGo.transform.localPosition = tipLocalPos;
            _bladeTipAnchor = tipGo.transform;
        }

        private void EnsureSparkEffects()
        {
            Transform existing = transform.Find("Ardente_ImpactSparksFX");
            if (existing != null)
            {
                _sparkParticles = existing.GetComponent<ParticleSystem>();
            }
            if (_sparkParticles == null)
            {
                GameObject go = new GameObject("Ardente_ImpactSparksFX");
                go.transform.SetParent(transform, false);
                _sparkParticles = go.AddComponent<ParticleSystem>();

                var main = _sparkParticles.main;
                main.playOnAwake = false;
                main.loop = false;
                main.duration = 0.35f;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.14f, 0.30f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(5.2f, 10.5f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.07f, 0.15f);
                main.gravityModifier = 0.85f;
                main.startColor = new ParticleSystem.MinMaxGradient(
                    new Color(1.0f, 0.88f, 0.35f, 1f),
                    new Color(1.0f, 0.38f, 0.06f, 1f)
                );
                main.simulationSpace = ParticleSystemSimulationSpace.World;

                var emission = _sparkParticles.emission;
                emission.enabled = false;

                var shape = _sparkParticles.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Sphere;
                shape.radius = 0.22f;

                ParticleSystemRenderer rend = go.GetComponent<ParticleSystemRenderer>();
                if (rend != null)
                {
                    rend.renderMode = ParticleSystemRenderMode.Billboard;
                    rend.sharedMaterial = BurnStatusEffect.GetOrCreateSharedFireParticleMaterial(pixelFireMaterial);
                }
            }
        }

        private void EnsureBladeFireEffects()
        {
            if (swordRoot == null) return;
            EnsureBladeTipAnchor();

            Transform parentTf = _bladeTipAnchor != null ? _bladeTipAnchor : swordRoot;
            Transform existingFx = parentTf.Find("Ardente_BladeFireFX");
            if (existingFx != null)
            {
                _bladeFireParticles = existingFx.GetComponent<ParticleSystem>();
            }
            if (_bladeFireParticles == null)
            {
                GameObject fxGo = new GameObject("Ardente_BladeFireFX");
                fxGo.transform.SetParent(parentTf, false);
                fxGo.transform.localPosition = new Vector3(0f, -0.35f, 0f);
                _bladeFireParticles = fxGo.AddComponent<ParticleSystem>();

                var main = _bladeFireParticles.main;
                main.playOnAwake = false;
                main.loop = true;
                main.duration = 1.0f;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.20f, 0.42f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(0.75f, 1.85f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.09f, 0.19f);
                main.gravityModifier = -0.38f;
                main.startColor = new ParticleSystem.MinMaxGradient(
                    new Color(1.0f, 0.85f, 0.28f, 1f),
                    new Color(1.0f, 0.32f, 0.05f, 1f)
                );
                main.simulationSpace = ParticleSystemSimulationSpace.World;

                var emission = _bladeFireParticles.emission;
                emission.enabled = false;

                var shape = _bladeFireParticles.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Box;
                shape.scale = new Vector3(0.08f, 0.65f, 0.08f);

                ParticleSystemRenderer rend = fxGo.GetComponent<ParticleSystemRenderer>();
                if (rend != null)
                {
                    rend.renderMode = ParticleSystemRenderMode.Billboard;
                    rend.sharedMaterial = BurnStatusEffect.GetOrCreateSharedFireParticleMaterial(pixelFireMaterial);
                }
            }

            Transform existingLight = parentTf.Find("Ardente_BladeGlowLight");
            if (existingLight != null)
            {
                _bladeGlowLight = existingLight.GetComponent<Light>();
            }
            if (_bladeGlowLight == null)
            {
                GameObject lightGo = new GameObject("Ardente_BladeGlowLight");
                lightGo.transform.SetParent(parentTf, false);
                lightGo.transform.localPosition = new Vector3(0f, -0.25f, 0f);
                _bladeGlowLight = lightGo.AddComponent<Light>();
                _bladeGlowLight.type = LightType.Point;
                _bladeGlowLight.color = new Color(1.0f, 0.46f, 0.10f, 1f);
                _bladeGlowLight.intensity = 0f;
                _bladeGlowLight.range = 3.2f;
                _bladeGlowLight.shadows = LightShadows.None;
                _bladeGlowLight.enabled = false;
            }
        }

        private void EmitSparksAt(Vector3 worldPos, int count)
        {
            if (_sparkParticles == null) return;
            _sparkParticles.transform.position = worldPos;
            _sparkParticles.Emit(count);
        }

        private bool TryGetCursorWorldTarget(float defaultPlaneY, out Vector3 targetPoint)
        {
            Transform originTf = _owner != null ? _owner.transform : transform;
            targetPoint = originTf.position + originTf.forward * 8f;
            if (_cam == null) _cam = Camera.main;
            if (_cam == null) _cam = FindAnyObjectByType<Camera>();
            if (_cam == null) return false;

            Vector2 screenPos = Vector2.zero;
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null)
            {
                screenPos = Mouse.current.position.ReadValue();
            }
#else
            screenPos = Input.mousePosition;
#endif
            Ray ray = _cam.ScreenPointToRay(screenPos);

            // Ignora paredes invisiveis BG3, tetos e colunas; mira em alvos de combate ou no plano do chao
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
            Transform originTf = _owner != null ? _owner.transform : transform;
            if (TryGetCursorWorldTarget(originTf.position.y + 1.0f, out Vector3 pt))
            {
                Vector3 dir = pt - originTf.position;
                dir.y = 0f;
                if (dir.sqrMagnitude > 0.01f) return dir.normalized;
            }
            return originTf.forward;
        }

        private void AimCharacterTowardCursor(float dt)
        {
            Transform ownerTf = _owner != null ? _owner.transform : transform;
            Vector3 aimDir = GetAimDirectionOnPlane();
            Quaternion targetRot = Quaternion.LookRotation(aimDir, Vector3.up);
            ownerTf.rotation = Quaternion.Slerp(ownerTf.rotation, targetRot, 34f * dt);
        }

        private static bool IsPlayerCollider(Collider c, Transform ownerTf)
        {
            if (c == null) return true;
            if (ownerTf != null && (c.transform == ownerTf || c.transform.IsChildOf(ownerTf))) return true;
            if (c.GetComponentInParent<SimplePlayerController>() != null) return true;
            try
            {
                if (c.CompareTag("Player")) return true;
            }
            catch (UnityException) { }
            return false;
        }

        private static GameObject ResolveTargetRoot(IAcidDamageable dmg)
        {
            if (dmg is TeniaHitboxRelay relay && relay.ownerHealth != null)
            {
                return relay.ownerHealth.gameObject;
            }
            if (dmg is Component comp)
            {
                return comp.gameObject;
            }
            return null;
        }

        private static Transform FindDescendantByName(Transform root, string targetName)
        {
            if (root == null) return null;
            Transform[] children = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < children.Length; i++)
            {
                if (children[i] != null && children[i].name == targetName)
                {
                    return children[i];
                }
            }
            return null;
        }

        private static float SmoothSwingCurve(float progress, float windupEnd, float strikeEnd)
        {
            if (progress < windupEnd)
            {
                return -(progress / Mathf.Max(0.01f, windupEnd)) * 0.14f;
            }
            float t = Mathf.Clamp01((progress - windupEnd) / Mathf.Max(0.01f, strikeEnd - windupEnd));
            return t * t * (3f - 2f * t);
        }

        private static Material CreateTransparentUnlitMaterial(Color initialColor)
        {
            Shader s = Shader.Find("Universal Render Pipeline/Unlit");
            if (s == null) s = Shader.Find("Sprites/Default");
            Material mat = new Material(s);
            mat.hideFlags = HideFlags.DontSave;
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
            if (mat.HasProperty("_SrcBlend")) mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            if (mat.HasProperty("_DstBlend")) mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);
            if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", (float)CullMode.Off);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", initialColor);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", initialColor);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = 3100;
            return mat;
        }

        private static Mesh BuildFloorUnitQuadMesh()
        {
            Mesh mesh = new Mesh { name = "Ardente_TelegraphLaneQuad", hideFlags = HideFlags.DontSave };
            mesh.vertices = new[]
            {
                new Vector3(-0.5f, 0f, 0f),
                new Vector3(0.5f, 0f, 0f),
                new Vector3(0.5f, 0f, 1f),
                new Vector3(-0.5f, 0f, 1f)
            };
            mesh.uv = new[]
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(1f, 1f),
                new Vector2(0f, 1f)
            };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            mesh.RecalculateNormals();
            return mesh;
        }
    }
}
