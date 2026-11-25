using System.Collections;
using StomachAbyss.Combat;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace MapaEstomago
{
    /// <summary>
    /// Controlador completo do Virus Player (VirusMaligno) no mapa do estomago:
    /// - Movimentacao isometrica estilo Hades / Diablo (WASD alinhado a camera)
    /// - Corrida com Shift (acelera a animacao Walk), Esquiva/Rolamento com Espaco (com i-frames e cancelamento), Pulo com Q
    /// - Roteamento unificado de combate via IPlayerWeapon (suporta Espada Cruz e futuras armas sem ataque duplo)
    /// - Fallback para ataque viral de garras caso nao haja arma equipada
    /// - Animacao Hit ao sofrer dano + Flash no shader PS1_VirusPlayer + Death/Respawn completo
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class SimplePlayerController : MonoBehaviour, IAcidDamageable
    {
        [Header("Movimentacao Isometrica")]
        public float walkSpeed = 6.5f;
        public float runSpeed = 10.5f;
        public float groundAcceleration = 58f;
        public float groundDeceleration = 46f;
        public float rotationSpeed = 18f;
        public float jumpHeight = 1.35f;
        public float gravity = 22f;

        [Header("Esquiva / Dash Lateral (Espaco)")]
        public float dodgeSpeed = 15.5f;
        public float dodgeDuration = 0.24f;
        public float dodgeIFrameDuration = 0.24f;
        public float dodgeCooldown = 0.58f;

        [Header("Combate e Acao de Ataque (Fallback Sem Arma)")]
        public float attackCooldown = 0.68f;
        public float attackRange = 2.4f;
        public float attackRadius = 2.1f;
        public float attackLungeSpeed = 5.2f;
        public float attackDamage = 35f;
        public ParticleSystem attackParticles;
        public Light coreBiolumLight;

        [Header("Vida e Dano de Acido")]
        public float maxHealth = 100f;
        public float currentHealth = 100f;
        public Vector3 respawnPosition;
        public float deathRespawnDelay = 1.55f;
        [Tooltip("Multiplicador de velocidade ao andar dentro do lago de acido (viscosidade).")]
        [Range(0.2f, 1f)]
        public float acidMoveSpeedMultiplier = 0.52f;
        [Tooltip("Duracao do derretimento no acido ao morrer queimado (estilo Boss Tenia).")]
        public float acidMeltDuration = 1.90f;

        [Header("Estamina (Esquiva, Pulo, Corrida e Combate)")]
        public float maxStamina = 100f;
        public float currentStamina = 100f;
        public float dodgeStaminaCost = 14f;
        public float jumpStaminaCost = 5f;
        public float unarmedAttackStaminaCost = 12f;
        public float runStaminaDrainPerSec = 12f;
        public float staminaRegenDelay = 0.50f;
        public float staminaRegenRate = 42f;
        public float minStaminaToResumeRun = 10f;

        [Header("Cura Rapida / Estus (Tecla R)")]
        public int maxEstusCharges = 3;
        public int currentEstusCharges = 3;
        public float estusHealAmount = 45f;
        public float estusCooldown = 0.65f;
        public bool showDebugOnGui = false;

        [Header("Visual e Animador (Virus_Player.glb)")]
        public Transform visualRoot;
        public Animator animator;
        public float walkBobFrequency = 11f;
        public float walkBobAmount = 0.08f;

        private CharacterController _cc;
        private Camera _cam;
        private Vector3 _velocity;
        private Vector3 _visualInitialScale;
        private Vector3 _visualInitialLocalPos;
        private Quaternion _visualInitialLocalRot = Quaternion.identity;
        private float _bobTimer;
        private Renderer[] _renderers;
        private Color[] _origColors;
        private float _damageFlashTimer;

        private IPlayerWeapon _activeWeapon;
        private float _dodgeTimer;
        private float _dodgeCooldownTimer;
        private float _dodgeIFrameTimer;
        private Vector3 _dodgeDirection;
        private int _dodgeSideSign = 1;
        private bool _dodgeStartedGrounded;
        private Vector3 _currentHorizVelocity;
        private float _lastSafeGroundY;
        private float _combatLungeTimer;
        private float _combatLungeSpeed;
        private Vector3 _combatLungeDir;

        private float _attackCooldownTimer;
        private float _attackActiveTimer;
        private bool _attackHitTriggered;
        private float _hitAnimCooldownTimer;
        private float _hurtSfxCooldownTimer;
        private float _playerBloodCooldownTimer;
        private bool _isDead;
        private float _baseLightIntensity = 2.2f;
        private float _externalSnareTimer;
        private Vector3 _externalSnareCenter;
        private float _externalSnareMaxRadius = 0.65f;
        private float _staminaRegenDelayTimer;
        private float _verticalVelocity;

        private bool _staminaExhaustedLock;
        private float _estusCooldownTimer;
        private float _attackMoveSlowMultiplier = 1f;
        private float _attackMoveSlowTimer = 0f;
        private int _inAcidPoolCount;
        private bool _diedByAcid;
        private bool _isMelting;
        private MaterialPropertyBlock _meltMpb;

        private static readonly int PropBaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int PropColor = Shader.PropertyToID("_Color");
        private static readonly int PropEmissionColor = Shader.PropertyToID("_EmissionColor");

        private static readonly int HashSpeed = Animator.StringToHash("Speed");
        private static readonly int HashMoveSpeedMult = Animator.StringToHash("MoveSpeedMultiplier");
        private static readonly int HashIsMoving = Animator.StringToHash("IsMoving");
        private static readonly int HashIsDead = Animator.StringToHash("IsDead");
        private static readonly int HashAttack = Animator.StringToHash("Attack");
        private static readonly int HashHit = Animator.StringToHash("Hit");
        private static readonly int PropHitFlash = Shader.PropertyToID("_HitFlash");
        private static readonly int PropHitFlashColor = Shader.PropertyToID("_HitFlashColor");

        [Header("Controle Externo / Dialogo")]
        public bool inputLocked;

        public void SetInputLocked(bool locked)
        {
            inputLocked = locked;
        }

        public void SetInAcidPool(bool inAcid)
        {
            if (inAcid)
            {
                _inAcidPoolCount++;
            }
            else
            {
                _inAcidPoolCount = Mathf.Max(0, _inAcidPoolCount - 1);
            }
        }

        public bool IsInAcidPool => _inAcidPoolCount > 0;

        public bool IsDead => _isDead;
        public bool IsDodging => _dodgeTimer > 0f;
        public bool IsInvulnerable => _dodgeIFrameTimer > 0f;
        public IPlayerWeapon ActiveWeapon => _activeWeapon;
        public bool IsAttacking => _attackActiveTimer > 0f || (_activeWeapon != null && _activeWeapon.IsBusy);
        public bool IsConstricted => _externalSnareTimer > 0f;

        public float CurrentStamina => currentStamina;
        public float MaxStamina => maxStamina;
        public float StaminaNormalized => maxStamina > 0f ? Mathf.Clamp01(currentStamina / maxStamina) : 0f;
        public int CurrentEstusCharges => currentEstusCharges;
        public int MaxEstusCharges => maxEstusCharges;

        public event System.Action<float, float> OnStaminaChanged;
        public event System.Action<int, int> OnEstusChanged;
        public event System.Action OnEstusUsed;

        public void ApplyBossConstriction(Vector3 center, float duration, float maxRadius = 0.55f)
        {
            _externalSnareCenter = center;
            _externalSnareTimer = Mathf.Max(_externalSnareTimer, duration);
            _externalSnareMaxRadius = Mathf.Max(0.15f, maxRadius);
        }

        public void ReleaseBossConstriction()
        {
            _externalSnareTimer = 0f;
        }

        public void ApplyExternalKnockback(Vector3 horizontalDelta)
        {
            if (_cc != null && _cc.enabled && !_isDead)
            {
                _cc.Move(new Vector3(horizontalDelta.x, 0f, horizontalDelta.z));
            }
        }

        private void Awake()
        {
            _cc = GetComponent<CharacterController>();
            _cam = Camera.main;
            if (_cam == null) _cam = FindFirstObjectByType<Camera>();

            respawnPosition = transform.position;
            _lastSafeGroundY = transform.position.y;
            currentHealth = maxHealth;
            currentStamina = maxStamina;
            currentEstusCharges = maxEstusCharges;

            BindVisualAndAnimator();
            EnsureAttackEffects();
            EnsureCombatAudio();
            ResolveActiveWeapon();

            gameObject.tag = "Player";
        }

        public void ResolveActiveWeapon()
        {
            PlayerWeaponArsenal arsenal = GetComponent<PlayerWeaponArsenal>();
            if (arsenal != null && arsenal.slots != null && arsenal.slots.Count > 0)
            {
                arsenal.ApplyCurrentSlotImmediate();
                return;
            }

            MonoBehaviour[] monos = GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < monos.Length; i++)
            {
                if (monos[i] != null && monos[i].enabled && monos[i] is IPlayerWeapon w)
                {
                    SetActiveWeapon(w);
                    return;
                }
            }
        }

        public void SetActiveWeapon(IPlayerWeapon newWeapon)
        {
            if (ReferenceEquals(_activeWeapon, newWeapon)) return;
            _activeWeapon?.OnWeaponUnequipped();
            _activeWeapon = newWeapon;
            _activeWeapon?.OnWeaponEquipped(this);
        }

        public void ApplyCombatLunge(Vector3 direction, float speed, float duration)
        {
            Vector3 flat = new Vector3(direction.x, 0f, direction.z);
            if (flat.sqrMagnitude < 0.001f) flat = transform.forward;
            _combatLungeDir = flat.normalized;
            _combatLungeSpeed = speed;
            _combatLungeTimer = Mathf.Max(_combatLungeTimer, duration);
        }

        public void ApplyAttackMovementSlow(float speedMultiplier, float duration)
        {
            _attackMoveSlowMultiplier = Mathf.Clamp(speedMultiplier, 0.1f, 1f);
            _attackMoveSlowTimer = Mathf.Max(_attackMoveSlowTimer, duration);
        }

        public void TriggerWeaponFlash(Color hdrColor, float duration, float lightMult = 1.8f)
        {
            TriggerShaderFlash(hdrColor, duration);
            if (coreBiolumLight != null)
            {
                coreBiolumLight.intensity = _baseLightIntensity * lightMult;
            }
        }

        public void EmitCombatParticles(Vector3 worldPos, Quaternion worldRot, int count)
        {
            if (attackParticles == null) return;
            attackParticles.transform.position = worldPos;
            attackParticles.transform.rotation = worldRot;
            attackParticles.Emit(count);
        }

        public void BindVisualAndAnimator()
        {
            if (visualRoot == null)
            {
                Transform v = transform.Find("Virus_Model");
                if (v == null) v = transform.Find("Visual");
                visualRoot = v != null ? v : transform;
            }

            if (animator == null)
            {
                animator = GetComponentInChildren<Animator>();
            }

            if (coreBiolumLight == null)
            {
                Light[] lights = GetComponentsInChildren<Light>(true);
                if (lights != null && lights.Length > 0)
                {
                    coreBiolumLight = lights[0];
                    _baseLightIntensity = coreBiolumLight.intensity;
                }
            }

            if (visualRoot != null)
            {
                _visualInitialScale = visualRoot.localScale;
                _visualInitialLocalPos = visualRoot.localPosition;
                _visualInitialLocalRot = visualRoot.localRotation;
                _renderers = GetComponentsInChildren<Renderer>(true);
                if (_renderers != null && _renderers.Length > 0)
                {
                    _origColors = new Color[_renderers.Length];
                    for (int i = 0; i < _renderers.Length; i++)
                    {
                        if (_renderers[i] != null && _renderers[i].sharedMaterial != null)
                        {
                            if (_renderers[i].sharedMaterial.HasProperty("_BaseColor"))
                                _origColors[i] = _renderers[i].sharedMaterial.GetColor("_BaseColor");
                            else if (_renderers[i].sharedMaterial.HasProperty("_Color"))
                                _origColors[i] = _renderers[i].sharedMaterial.color;
                        }
                    }
                }
            }
            else
            {
                _visualInitialScale = Vector3.one;
                _visualInitialLocalPos = Vector3.zero;
            }
        }

        private void Start()
        {
            ResolveActiveWeapon();
            IsometricCameraFollow iso = FindFirstObjectByType<IsometricCameraFollow>();
            if (iso != null)
            {
                iso.target = transform;
            }
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            if (_attackCooldownTimer > 0f) _attackCooldownTimer -= dt;
            if (_hitAnimCooldownTimer > 0f) _hitAnimCooldownTimer -= dt;
            if (_hurtSfxCooldownTimer > 0f) _hurtSfxCooldownTimer -= dt;
            if (_playerBloodCooldownTimer > 0f) _playerBloodCooldownTimer -= dt;
            if (_dodgeCooldownTimer > 0f) _dodgeCooldownTimer -= dt;
            if (_dodgeIFrameTimer > 0f) _dodgeIFrameTimer -= dt;
            if (_combatLungeTimer > 0f) _combatLungeTimer -= dt;
            if (_estusCooldownTimer > 0f) _estusCooldownTimer -= dt;
            if (_attackMoveSlowTimer > 0f) _attackMoveSlowTimer -= dt;
            UpdateStaminaRegen(dt);

            if (!_isDead)
            {
                Vector2 moveInput = Vector2.zero;
                bool isRunning = false;
                bool jumpPressed = false;
                bool dodgePressed = false;
                bool primaryPressed = false;
                bool secondaryHeld = false;
                bool secondaryReleased = false;

                if (!inputLocked)
                {
                    ReadUnifiedInput(
                        out moveInput,
                        out isRunning,
                        out jumpPressed,
                        out dodgePressed,
                        out primaryPressed,
                        out secondaryHeld,
                        out secondaryReleased
                    );
                }

                Vector3 moveDir = ComputeCameraRelativeMoveDir(moveInput);

                if (!inputLocked && dodgePressed)
                {
                    TryStartDodge(moveDir);
                }

                if (!inputLocked && WasEstusPressedThisFrame())
                {
                    TryUseEstus();
                }

                HandleWeaponAndClawCombat(dt, secondaryHeld, secondaryReleased, primaryPressed);
                HandleMovement(dt, moveDir, isRunning, jumpPressed);
            }
            else
            {
                ApplyGravityWhenDead(dt);
            }

            HandleVisualRollAndBob(dt);
            UpdateDamageFlash(dt);
        }

        private void ReadUnifiedInput(
            out Vector2 inputDir,
            out bool isRunning,
            out bool jumpPressed,
            out bool dodgePressed,
            out bool primaryPressed,
            out bool secondaryHeld,
            out bool secondaryReleased)
        {
            inputDir = Vector2.zero;
            isRunning = false;
            jumpPressed = false;
            dodgePressed = false;
            primaryPressed = false;
            secondaryHeld = false;
            secondaryReleased = false;

#if ENABLE_INPUT_SYSTEM
            Keyboard kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.wKey.isPressed || kb.upArrowKey.isPressed) inputDir.y += 1f;
                if (kb.sKey.isPressed || kb.downArrowKey.isPressed) inputDir.y -= 1f;
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) inputDir.x += 1f;
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) inputDir.x -= 1f;
                isRunning = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
                dodgePressed = kb.spaceKey.wasPressedThisFrame;
                jumpPressed = kb.qKey.wasPressedThisFrame;
                if (kb.jKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame)
                {
                    primaryPressed = true;
                }
            }

            Mouse mouse = Mouse.current;
            if (mouse != null)
            {
                if (mouse.leftButton.wasPressedThisFrame) primaryPressed = true;
                secondaryHeld = mouse.rightButton.isPressed;
                secondaryReleased = mouse.rightButton.wasReleasedThisFrame;
            }

            Gamepad pad = Gamepad.current;
            if (pad != null)
            {
                Vector2 stick = pad.leftStick.ReadValue();
                if (stick.sqrMagnitude > 0.04f) inputDir = stick;
                if (pad.buttonEast.wasPressedThisFrame) dodgePressed = true;
                if (pad.buttonSouth.wasPressedThisFrame) jumpPressed = true;
                if (pad.leftShoulder.isPressed) isRunning = true;
                if (pad.buttonWest.wasPressedThisFrame || pad.rightTrigger.wasPressedThisFrame) primaryPressed = true;
                if (pad.leftTrigger.isPressed) secondaryHeld = true;
                if (pad.leftTrigger.wasReleasedThisFrame) secondaryReleased = true;
            }
#else
            inputDir.x = Input.GetAxisRaw("Horizontal");
            inputDir.y = Input.GetAxisRaw("Vertical");
            isRunning = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            dodgePressed = Input.GetKeyDown(KeyCode.Space);
            jumpPressed = Input.GetKeyDown(KeyCode.Q);
            primaryPressed = Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.J) || Input.GetKeyDown(KeyCode.Return);
            secondaryHeld = Input.GetMouseButton(1);
            secondaryReleased = Input.GetMouseButtonUp(1);
#endif
        }

        private Vector3 ComputeCameraRelativeMoveDir(Vector2 inputDir)
        {
            Vector3 moveDir;
            if (_cam != null)
            {
                Vector3 camFwd = _cam.transform.forward;
                Vector3 camRight = _cam.transform.right;
                camFwd.y = 0f;
                camRight.y = 0f;
                camFwd.Normalize();
                camRight.Normalize();

                moveDir = camRight * inputDir.x + camFwd * inputDir.y;
            }
            else
            {
                moveDir = new Vector3(inputDir.x, 0f, inputDir.y);
            }

            if (moveDir.sqrMagnitude > 1f) moveDir.Normalize();
            return moveDir;
        }

        private void TryStartDodge(Vector3 moveDir)
        {
            if (IsDodging || _dodgeCooldownTimer > 0f || _externalSnareTimer > 0f) return;
            if (currentStamina < dodgeStaminaCost) return;

            SpendStamina(dodgeStaminaCost);

            // Cancela imediatamente qualquer recuperacao/golpe pesado da arma ou garra
            _attackActiveTimer = 0f;
            _attackHitTriggered = false;
            _combatLungeTimer = 0f;
            _activeWeapon?.CancelActiveAttack();

            if (moveDir.sqrMagnitude > 0.01f)
            {
                _dodgeDirection = moveDir.normalized;
            }
            else
            {
                // Sem direcao pressionada: esquiva lateral rapida (Dash alternando direita/esquerda)
                _dodgeDirection = transform.right * _dodgeSideSign;
                _dodgeSideSign = -_dodgeSideSign;
            }
            _dodgeDirection.y = 0f;
            if (_dodgeDirection.sqrMagnitude < 0.001f) _dodgeDirection = Vector3.right;
            _dodgeDirection.Normalize();

            _dodgeStartedGrounded = _cc != null && _cc.isGrounded;
            _dodgeTimer = dodgeDuration;
            _dodgeCooldownTimer = dodgeCooldown;
            _dodgeIFrameTimer = dodgeIFrameDuration;
            _currentHorizVelocity = _dodgeDirection * dodgeSpeed;

            EmitCombatParticles(transform.position + Vector3.up * 0.55f, Quaternion.LookRotation(-_dodgeDirection, Vector3.up), 18);
            TriggerWeaponFlash(new Color(0.28f, 1.9f, 2.5f, 1f), 0.18f, 2.2f);
            CombatSfxBank.Play(CombatSfxId.PlayerDash, 0.82f, 0.96f, 1.06f, 0.08f);
        }

        private void HandleWeaponAndClawCombat(float dt, bool secondaryHeld, bool secondaryReleased, bool primaryPressed)
        {
            if (_activeWeapon == null)
            {
                ResolveActiveWeapon();
            }

            bool useWeapon = _activeWeapon != null;

            if (useWeapon)
            {
                if (IsDodging)
                {
                    _activeWeapon.OnSecondaryAimState(false, false);
                }
                else
                {
                    _activeWeapon.OnSecondaryAimState(secondaryHeld, secondaryReleased);
                    if (primaryPressed)
                    {
                        _activeWeapon.OnPrimaryAttackPressed();
                    }
                }

                if (_activeWeapon.SuppressesDefaultClawAttack)
                {
                    _attackActiveTimer = 0f;
                    return;
                }
            }

            // Fallback: ataque viral de garras apenas quando nao ha arma suprimindo
            if (!IsDodging && primaryPressed && _attackCooldownTimer <= 0f)
            {
                StartAttack();
            }

            if (_attackActiveTimer > 0f)
            {
                _attackActiveTimer -= dt;
                // No pico da animacao de ataque (~0.24s apos iniciar), aplica o impacto na frente do Virus
                if (!_attackHitTriggered && _attackActiveTimer <= 0.44f)
                {
                    _attackHitTriggered = true;
                    ExecuteAttackHitbox();
                }
            }
        }

        public void StartAttack()
        {
            if (_isDead) return;
            if (!TryConsumeActionStamina(unarmedAttackStaminaCost)) return;

            _attackCooldownTimer = attackCooldown;
            _attackActiveTimer = 0.68f;
            _attackHitTriggered = false;

            if (animator != null)
            {
                animator.ResetTrigger(HashHit);
                animator.SetTrigger(HashAttack);
                animator.CrossFadeInFixedTime("Attack", 0.06f, 0, 0f);
            }

            // Pulso bioluminescente ciano no inicio do golpe
            TriggerShaderFlash(new Color(0.25f, 2.2f, 2.6f, 1f), 0.22f);
            if (coreBiolumLight != null)
            {
                coreBiolumLight.intensity = _baseLightIntensity * 2.8f;
            }
            CombatSfxBank.Play(CombatSfxId.SwordSlashLight, 0.66f, 1.24f, 1.36f, 0.05f);
        }

        private void ExecuteAttackHitbox()
        {
            if (attackParticles != null)
            {
                attackParticles.transform.position = transform.position + transform.forward * 0.85f + Vector3.up * 0.85f;
                attackParticles.transform.rotation = transform.rotation;
                attackParticles.Emit(22);
            }

            Vector3 hitCenter = transform.position + transform.forward * (attackRange * 0.55f) + Vector3.up * 0.75f;
            Collider[] cols = Physics.OverlapSphere(hitCenter, attackRadius);
            bool hitAnything = false;

            // Tambem checa componentes por proximidade (mesmo aqueles sem Collider proprio, como AcidBubblePopper e UlcerEruption)
            AcidBubblePopper[] bubbles = FindObjectsByType<AcidBubblePopper>(FindObjectsSortMode.None);
            foreach (AcidBubblePopper b in bubbles)
            {
                if (b != null && Vector3.Distance(hitCenter, b.transform.position) <= attackRadius + 0.8f)
                {
                    b.PopByAttack();
                    hitAnything = true;
                }
            }

            UlcerEruption[] ulcers = FindObjectsByType<UlcerEruption>(FindObjectsSortMode.None);
            foreach (UlcerEruption u in ulcers)
            {
                if (u != null && Vector3.Distance(hitCenter, u.transform.position) <= attackRadius + 1.2f)
                {
                    u.TriggerEruption();
                    hitAnything = true;
                }
            }

            OrganPulse[] organs = FindObjectsByType<OrganPulse>(FindObjectsSortMode.None);
            foreach (OrganPulse o in organs)
            {
                if (o != null && Vector3.Distance(hitCenter, o.transform.position) <= attackRadius + 1.0f)
                {
                    o.TriggerHitRecoil();
                    hitAnything = true;
                }
            }

            FleshEyeDoorController[] doors = FindObjectsByType<FleshEyeDoorController>(FindObjectsSortMode.None);
            foreach (FleshEyeDoorController d in doors)
            {
                if (d != null && Vector3.Distance(hitCenter, d.transform.position) <= attackRadius + 1.8f)
                {
                    d.EmitMouthBurst(14, new Color(0.85f, 0.08f, 0.12f, 1f), 4.2f);
                    hitAnything = true;
                }
            }

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
                    }

                    IAcidDamageable dmg = c.GetComponentInParent<IAcidDamageable>();
                    if (dmg != null && !ReferenceEquals(dmg, this))
                    {
                        Vector3 impactPt = c.ClosestPoint(hitCenter);
                        BloodSplatterSystem.BeginAttackBurstContext(transform.forward, impactPt, 0.88f, false);
                        CombatSfxBank.BeginImpactScope(false);
                        try
                        {
                            dmg.TakeAcidDamage(attackDamage);
                        }
                        finally
                        {
                            if (!BloodSplatterSystem.WasEntityHandledInContext)
                            {
                                BloodSplatterSystem.SpawnBloodBurst(impactPt, transform.forward, 0.88f, null, false);
                            }
                            if (!CombatSfxBank.WasImpactClaimedInScope)
                            {
                                CombatSfxBank.PlayImpact(false, impactPt, 0.80f);
                            }
                            BloodSplatterSystem.EndAttackBurstContext();
                            CombatSfxBank.EndImpactScope(false);
                        }
                        hitAnything = true;
                    }
                }
            }

            if (hitAnything)
            {
                CombatHitstopManager.TriggerHitstop(0.040f, 0.04f, false);
            }
        }

        private void HandleMovement(float dt, Vector3 moveDir, bool isRunning, bool jumpPressed)
        {
            if (_cc == null || !_cc.enabled) return;

            float safeDt = Mathf.Min(dt, 0.05f);
            if (_cc.isGrounded)
            {
                _lastSafeGroundY = transform.position.y;
            }

            Vector3 horizVelocity;
            bool allowTurning = true;

            if (IsDodging)
            {
                _dodgeTimer = Mathf.Max(0f, _dodgeTimer - safeDt);
                float progress = 1f - Mathf.Clamp01(_dodgeTimer / Mathf.Max(0.01f, dodgeDuration));
                // Curva suave de dash: arranque rapido e desaceleracao natural sem trancos
                float speedMult = Mathf.Lerp(1.25f, 0.55f, progress * progress);
                horizVelocity = _dodgeDirection * (dodgeSpeed * speedMult);
                _currentHorizVelocity = horizVelocity;
                jumpPressed = false;
                allowTurning = false;
            }
            else
            {
                bool canSprint = isRunning && moveDir.sqrMagnitude > 0.01f && _externalSnareTimer <= 0f && !_staminaExhaustedLock && currentStamina > 0.5f;
                if (canSprint)
                {
                    SpendStamina(runStaminaDrainPerSec * safeDt);
                    if (currentStamina <= 0.01f)
                    {
                        _staminaExhaustedLock = true;
                        canSprint = false;
                    }
                }

                float targetSpeed = canSprint ? runSpeed : walkSpeed;
                if (_activeWeapon != null)
                {
                    targetSpeed *= Mathf.Max(0.1f, _activeWeapon.MoveSpeedMultiplier);
                }
                if (_attackMoveSlowTimer > 0f)
                {
                    targetSpeed *= _attackMoveSlowMultiplier;
                }
                if (_inAcidPoolCount > 0)
                {
                    targetSpeed *= Mathf.Clamp(acidMoveSpeedMultiplier, 0.2f, 1f);
                }

                if (_externalSnareTimer > 0f)
                {
                    _externalSnareTimer = Mathf.Max(0f, _externalSnareTimer - safeDt);
                    targetSpeed *= 0.14f;
                    jumpPressed = false;
                }

                Vector3 desiredHoriz = moveDir * targetSpeed;
                float accel = desiredHoriz.sqrMagnitude > 0.01f ? groundAcceleration : groundDeceleration;
                _currentHorizVelocity = Vector3.MoveTowards(_currentHorizVelocity, desiredHoriz, accel * safeDt);
                horizVelocity = _currentHorizVelocity;

                if (_combatLungeTimer > 0f && _externalSnareTimer <= 0f)
                {
                    horizVelocity = horizVelocity * 0.35f + _combatLungeDir * _combatLungeSpeed;
                }
                else if (_attackActiveTimer > 0.35f && _externalSnareTimer <= 0f)
                {
                    // Durante o pico do ataque sem arma, adiciona um leve avanco das garras para a frente
                    horizVelocity = horizVelocity * 0.45f + transform.forward * attackLungeSpeed;
                }
            }

            if (_cc.isGrounded)
            {
                if (_velocity.y < 0f) _velocity.y = -2f;
                if (jumpPressed && TryConsumeActionStamina(jumpStaminaCost))
                {
                    _velocity.y = Mathf.Sqrt(2f * jumpHeight * gravity);
                    _dodgeStartedGrounded = false;
                    CombatSfxBank.Play(CombatSfxId.PlayerJump, 0.78f, 0.95f, 1.08f, 0.08f);
                }
            }
            else
            {
                // Mantem estabilidade no chao durante o dash em rampas suaves sem afundar
                if (IsDodging && _dodgeStartedGrounded && _velocity.y <= 0f)
                {
                    _velocity.y = -3.5f;
                }
                else
                {
                    _velocity.y -= gravity * safeDt;
                }
            }

            Vector3 finalMove = horizVelocity * safeDt + Vector3.up * (_velocity.y * safeDt);
            _cc.Move(finalMove);

            PreventUnderfloorClip();

            if (_externalSnareTimer > 0f)
            {
                Vector3 radial = transform.position - _externalSnareCenter;
                radial.y = 0f;
                if (radial.magnitude > _externalSnareMaxRadius)
                {
                    Vector3 pullTarget = _externalSnareCenter + radial.normalized * _externalSnareMaxRadius;
                    Vector3 pullDelta = new Vector3(pullTarget.x - transform.position.x, 0f, pullTarget.z - transform.position.z);
                    _cc.Move(pullDelta * Mathf.Clamp01(safeDt * 14f));
                }
            }

            if (allowTurning && moveDir.sqrMagnitude > 0.001f && (_activeWeapon == null || !_activeWeapon.IsAiming))
            {
                Quaternion targetRot = Quaternion.LookRotation(moveDir, Vector3.up);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, rotationSpeed * safeDt);
            }

            // Atualiza parametros do Animator (Idle <-> Walk)
            if (animator != null)
            {
                float planarSpeed = new Vector2(_cc.velocity.x, _cc.velocity.z).magnitude;
                bool moving = !IsDodging && moveDir.sqrMagnitude > 0.01f && planarSpeed > 0.15f;
                animator.SetFloat(HashSpeed, IsDodging ? 0f : planarSpeed);
                animator.SetBool(HashIsMoving, moving);
                animator.SetFloat(HashMoveSpeedMult, isRunning ? 1.55f : 1.0f);
            }
        }

        private void PreventUnderfloorClip()
        {
            if (_cc == null || !_cc.enabled) return;

            // Se por qualquer motivo o jogador atravessar uma fenda do piso para baixo, projeta de volta sobre o piso solido acima
            Vector3 probeTop = new Vector3(transform.position.x, Mathf.Max(transform.position.y + 1.4f, _lastSafeGroundY + 1.2f), transform.position.z);
            RaycastHit[] hits = Physics.RaycastAll(probeTop, Vector3.down, 6.0f, ~0, QueryTriggerInteraction.Ignore);
            float bestFloorY = float.NegativeInfinity;
            for (int i = 0; i < hits.Length; i++)
            {
                Collider col = hits[i].collider;
                if (col == null || col.transform.IsChildOf(transform)) continue;
                if (hits[i].normal.y >= 0.45f && hits[i].point.y > bestFloorY)
                {
                    bestFloorY = hits[i].point.y;
                }
            }

            if (bestFloorY > float.NegativeInfinity && transform.position.y < bestFloorY - 0.12f)
            {
                _cc.enabled = false;
                transform.position = new Vector3(transform.position.x, bestFloorY + 0.02f, transform.position.z);
                _cc.enabled = true;
                if (_velocity.y < 0f) _velocity.y = -1f;
                _lastSafeGroundY = bestFloorY;
            }
        }

        private void ApplyGravityWhenDead(float dt)
        {
            if (_cc == null || !_cc.enabled) return;
            _velocity.y -= gravity * dt;
            _cc.Move(Vector3.up * (_velocity.y * dt));
        }

        private void HandleVisualRollAndBob(float dt)
        {
            if (visualRoot == null || _isMelting) return;

            if (IsDodging)
            {
                float progress = 1f - Mathf.Clamp01(_dodgeTimer / Mathf.Max(0.01f, dodgeDuration));
                float arc = Mathf.Sin(progress * Mathf.PI);

                // Dash lateral/direcional: apenas inclinacao sutil (sem girar 360 graus para baixo do chao!)
                Vector3 localDashDir = transform.InverseTransformDirection(_dodgeDirection);
                float tiltPitch = localDashDir.z * 16f * arc;
                float tiltRoll = -localDashDir.x * 18f * arc;
                visualRoot.localRotation = _visualInitialLocalRot * Quaternion.Euler(tiltPitch, 0f, tiltRoll);

                float dashSquashY = 1f - arc * 0.16f;
                float dashStretchXZ = 1f + arc * 0.12f;
                visualRoot.localScale = new Vector3(
                    _visualInitialScale.x * dashStretchXZ,
                    _visualInitialScale.y * dashSquashY,
                    _visualInitialScale.z * dashStretchXZ
                );
                visualRoot.localPosition = _visualInitialLocalPos + new Vector3(0f, arc * 0.14f, 0f);
                return;
            }

            visualRoot.localRotation = Quaternion.Slerp(visualRoot.localRotation, _visualInitialLocalRot, dt * 18f);

            // Se o modelo possui Animator esqueleto (Virus_Player.glb), as proprias animacoes Idle/Walk
            // ja movem as pernas (Pili), garras (Fibers), coroa e nucleo!
            if (animator != null)
            {
                visualRoot.localPosition = Vector3.Lerp(visualRoot.localPosition, _visualInitialLocalPos, dt * 16f);
                visualRoot.localScale = Vector3.Lerp(visualRoot.localScale, _visualInitialScale, dt * 16f);
                return;
            }

            Vector3 horizVel = new Vector3(_cc.velocity.x, 0f, _cc.velocity.z);
            float speed = horizVel.magnitude;

            if (_cc.isGrounded && speed > 0.2f)
            {
                int prevStep = Mathf.FloorToInt(_bobTimer / Mathf.PI);
                _bobTimer += dt * walkBobFrequency * (speed / walkSpeed);
                int currStep = Mathf.FloorToInt(_bobTimer / Mathf.PI);
                if (currStep > prevStep)
                {
                    CombatSfxBank.Play(CombatSfxId.PlayerFootstepFlesh, 0.28f, 0.88f, 1.14f, 0.14f);
                }
                float sinWave = Mathf.Sin(_bobTimer);
                float cosWave = Mathf.Cos(_bobTimer * 0.5f);

                float yBob = Mathf.Abs(sinWave) * walkBobAmount;
                visualRoot.localPosition = _visualInitialLocalPos + new Vector3(cosWave * (walkBobAmount * 0.35f), yBob, 0f);

                float squash = 1f + sinWave * 0.05f;
                visualRoot.localScale = new Vector3(
                    _visualInitialScale.x * (1f / Mathf.Sqrt(squash)),
                    _visualInitialScale.y * squash,
                    _visualInitialScale.z * (1f / Mathf.Sqrt(squash))
                );
            }
            else
            {
                _bobTimer = 0f;
                visualRoot.localPosition = Vector3.Lerp(visualRoot.localPosition, _visualInitialLocalPos, dt * 12f);
                visualRoot.localScale = Vector3.Lerp(visualRoot.localScale, _visualInitialScale, dt * 12f);
            }
        }

        public void Heal(float amount)
        {
            if (_isDead || amount <= 0f) return;
            currentHealth = Mathf.Min(maxHealth, currentHealth + amount);
            TriggerShaderFlash(new Color(0.25f, 1.95f, 2.35f, 1f), 0.25f);
        }

        public void TakeAcidDamage(float amount)
        {
            ApplyIncomingDamage(amount, isAcidSource: false);
        }

        public void TakeEnvironmentalAcidDamage(float amount)
        {
            ApplyIncomingDamage(amount, isAcidSource: true);
        }

        private void ApplyIncomingDamage(float amount, bool isAcidSource)
        {
            if (_isDead || IsInvulnerable) return;

            currentHealth = Mathf.Max(0f, currentHealth - amount);
            Color flashCol = isAcidSource
                ? new Color(0.35f, 2.2f, 0.16f, 1f)
                : new Color(0.55f, 2.0f, 0.20f, 1f);
            TriggerShaderFlash(flashCol, 0.20f);
            CombatCameraJuice.Shake(amount >= 15f ? 0.42f : 0.18f, 0.22f);
            CombatCameraJuice.TriggerDamageVignette(amount >= 15f ? 0.68f : 0.40f, isAcid: isAcidSource);

            if (!isAcidSource && _hurtSfxCooldownTimer <= 0f)
            {
                _hurtSfxCooldownTimer = amount <= 8f ? 0.48f : 0.22f;
                CombatSfxBank.Play(CombatSfxId.PlayerHurt, amount <= 8f ? 0.58f : 0.86f, 0.94f, 1.06f, 0.15f);
            }

            if (_playerBloodCooldownTimer <= 0f)
            {
                _playerBloodCooldownTimer = amount <= 8f ? 0.56f : 0.24f;
                Vector3 splatterDir = (-transform.forward + Vector3.up * 0.25f).normalized;
                Color splatterColor = isAcidSource
                    ? new Color(0.32f, 0.96f, 0.14f, 1f)
                    : new Color(0.76f, 0.08f, 0.14f, 1f);
                BloodSplatterSystem.SpawnBloodBurst(
                    transform.position + Vector3.up * 0.82f,
                    splatterDir,
                    amount <= 8f ? 0.44f : 0.78f,
                    splatterColor,
                    false);
            }

            if (currentHealth <= 0f)
            {
                _diedByAcid = isAcidSource || _inAcidPoolCount > 0;
                StartCoroutine(DeathAndRespawnRoutine());
                return;
            }

            // Dispara a animacao Hit (respeitando cooldown para nao reiniciar a cada tick de 0.25s)
            if (animator != null && _hitAnimCooldownTimer <= 0f && _attackActiveTimer <= 0.1f)
            {
                _hitAnimCooldownTimer = 0.52f;
                animator.SetTrigger(HashHit);
                animator.CrossFadeInFixedTime("Hit", 0.05f, 0, 0f);
            }
        }

        private void TriggerShaderFlash(Color hdrFlashColor, float duration)
        {
            _damageFlashTimer = duration;
            if (_renderers == null) return;

            for (int i = 0; i < _renderers.Length; i++)
            {
                Renderer r = _renderers[i];
                if (r == null || r.material == null) continue;

                if (r.material.HasProperty(PropHitFlash))
                {
                    r.material.SetColor(PropHitFlashColor, hdrFlashColor);
                    r.material.SetFloat(PropHitFlash, 0.85f);
                }
                else if (r.material.HasProperty("_BaseColor"))
                {
                    r.material.SetColor("_BaseColor", hdrFlashColor);
                }
            }
        }

        private void UpdateDamageFlash(float dt)
        {
            if (coreBiolumLight != null)
            {
                coreBiolumLight.intensity = Mathf.Lerp(coreBiolumLight.intensity, _baseLightIntensity, dt * 8f);
            }

            if (_damageFlashTimer > 0f)
            {
                _damageFlashTimer -= dt;
                float flashRatio = Mathf.Clamp01(_damageFlashTimer / 0.20f);

                if (_renderers != null)
                {
                    for (int i = 0; i < _renderers.Length; i++)
                    {
                        Renderer r = _renderers[i];
                        if (r == null || r.material == null) continue;

                        if (r.material.HasProperty(PropHitFlash))
                        {
                            r.material.SetFloat(PropHitFlash, _damageFlashTimer > 0f ? flashRatio * 0.85f : 0f);
                        }
                        else if (_damageFlashTimer <= 0f && _origColors != null && i < _origColors.Length)
                        {
                            if (r.material.HasProperty("_BaseColor"))
                                r.material.SetColor("_BaseColor", _origColors[i]);
                            else if (r.material.HasProperty("_Color"))
                                r.material.color = _origColors[i];
                        }
                    }
                }
            }
        }

        private IEnumerator DeathAndRespawnRoutine()
        {
            _isDead = true;
            StomachAbyss.Progression.BiomassProgressionSystem.DropBiomassCocoon(transform.position);
            bool meltInAcid = _diedByAcid || _inAcidPoolCount > 0;
            ClearAllConditions();

            if (animator != null)
            {
                animator.ResetTrigger(HashAttack);
                animator.ResetTrigger(HashHit);
                animator.SetBool(HashIsMoving, false);
                animator.SetBool(HashIsDead, true);
                animator.CrossFadeInFixedTime("Death", 0.08f, 0, 0f);
            }

            if (meltInAcid)
            {
                _isMelting = true;
                CombatSfxBank.Play(CombatSfxId.PlayerAcidBurn, 0.95f, 0.72f, 0.84f, 0.08f);
                CombatCameraJuice.TriggerDamageVignette(0.75f, isAcid: true);
                SpawnAcidMeltVfx(transform.position + Vector3.up * 0.25f);
                Debug.Log("[Virus Player] Derretendo no acido gastrico!");

                float duration = Mathf.Max(0.6f, acidMeltDuration);
                float elapsed = 0f;
                Vector3 startScale = visualRoot != null ? visualRoot.localScale : _visualInitialScale;
                Vector3 startPos = visualRoot != null ? visualRoot.localPosition : _visualInitialLocalPos;
                float nextSizzleTime = 0.45f;

                while (elapsed < duration)
                {
                    elapsed += Time.deltaTime;
                    float t = Mathf.Clamp01(elapsed / duration);
                    float meltEase = t * t * (3f - 2f * t);

                    if (elapsed >= nextSizzleTime)
                    {
                        nextSizzleTime += 0.45f;
                        CombatSfxBank.Play(CombatSfxId.PlayerAcidBurn, 0.58f * (1f - t * 0.45f), 0.76f, 0.92f, 0.05f);
                    }

                    if (visualRoot != null)
                    {
                        // Igual ao Boss Tenia: achata no eixo Y e espalha em X/Z formando poca acida
                        float spreadXZ = 1f + 0.55f * meltEase;
                        float squashY = Mathf.Lerp(1f, 0.04f, meltEase);

                        // Nos ultimos 25%, dissolve totalmente no lago
                        float vanishMult = t > 0.75f ? Mathf.Lerp(1f, 0.02f, (t - 0.75f) / 0.25f) : 1f;
                        visualRoot.localScale = new Vector3(
                            startScale.x * spreadXZ * vanishMult,
                            startScale.y * squashY * vanishMult,
                            startScale.z * spreadXZ * vanishMult
                        );
                        visualRoot.localPosition = new Vector3(
                            startPos.x,
                            Mathf.Lerp(startPos.y, startPos.y - 0.35f, meltEase),
                            startPos.z
                        );
                    }

                    ApplyAcidMeltTint(meltEase);
                    yield return null;
                }
            }
            else
            {
                CombatSfxBank.Play(CombatSfxId.PlayerDeath, 0.92f, 0.96f, 1.02f, 0.25f);
                TriggerShaderFlash(new Color(1.8f, 0.25f, 0.35f, 1f), 0.45f);
                Debug.Log("[Virus Player] Eliminado! Executando animacao Death...");
                yield return new WaitForSeconds(deathRespawnDelay);
            }

            DieAndRespawn();
        }

        private void ApplyAcidMeltTint(float melt01)
        {
            if (_renderers == null) return;
            if (_meltMpb == null) _meltMpb = new MaterialPropertyBlock();

            float t = Mathf.Clamp01(melt01);
            float pulse = 0.88f + 0.18f * Mathf.Sin(Time.time * 8.5f);
            Color acidBase = new Color(0.28f, 0.92f, 0.15f, 1f);
            Color acidEmission = new Color(0.45f, 1.95f, 0.16f, 1f) * (t * pulse);

            for (int i = 0; i < _renderers.Length; i++)
            {
                Renderer r = _renderers[i];
                if (r == null || r.sharedMaterial == null) continue;

                r.GetPropertyBlock(_meltMpb);
                Color origCol = (_origColors != null && i < _origColors.Length) ? _origColors[i] : Color.white;

                if (r.sharedMaterial.HasProperty(PropBaseColor))
                {
                    _meltMpb.SetColor(PropBaseColor, Color.Lerp(origCol, acidBase, t * 0.92f));
                }
                else if (r.sharedMaterial.HasProperty(PropColor))
                {
                    _meltMpb.SetColor(PropColor, Color.Lerp(origCol, acidBase, t * 0.92f));
                }

                if (r.sharedMaterial.HasProperty(PropEmissionColor))
                {
                    _meltMpb.SetColor(PropEmissionColor, acidEmission);
                }

                if (r.sharedMaterial.HasProperty(PropHitFlash))
                {
                    _meltMpb.SetColor(PropHitFlashColor, new Color(0.42f, 2.0f, 0.16f, 1f));
                    _meltMpb.SetFloat(PropHitFlash, t * 0.65f * pulse);
                }

                r.SetPropertyBlock(_meltMpb);
            }
        }

        private void ResetAcidMeltVisuals()
        {
            _isMelting = false;
            _diedByAcid = false;
            _inAcidPoolCount = 0;

            if (visualRoot != null)
            {
                visualRoot.localScale = _visualInitialScale;
                visualRoot.localPosition = _visualInitialLocalPos;
                visualRoot.localRotation = _visualInitialLocalRot;
            }

            if (_renderers != null)
            {
                for (int i = 0; i < _renderers.Length; i++)
                {
                    Renderer r = _renderers[i];
                    if (r == null) continue;
                    r.enabled = true;
                    r.SetPropertyBlock(null);
                    if (r.material != null && r.material.HasProperty(PropHitFlash))
                    {
                        r.material.SetFloat(PropHitFlash, 0f);
                    }
                }
            }
            inputLocked = false;
            if (IsometricCameraFollow.Instance != null)
            {
                IsometricCameraFollow.Instance.lockInput = false;
            }
        }

        private static void SpawnAcidMeltVfx(Vector3 worldPos)
        {
            GameObject fxObj = new GameObject("PlayerAcidMeltFX");
            fxObj.transform.position = worldPos;

            ParticleSystem ps = fxObj.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = true;
            main.loop = false;
            main.duration = 1.9f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.55f, 1.15f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.4f, 3.6f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.16f, 0.42f);
            main.gravityModifier = -0.25f;
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.32f, 1.0f, 0.16f, 0.92f),
                new Color(0.78f, 1.0f, 0.35f, 0.85f)
            );
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var emission = ps.emission;
            emission.rateOverTime = 26f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 18) });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 38f;
            shape.radius = 0.45f;
            shape.rotation = new Vector3(-90f, 0f, 0f);

            var sizeOverLife = ps.sizeOverLifetime;
            sizeOverLife.enabled = true;
            sizeOverLife.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.7f, 1f, 0.1f));

            var psRenderer = fxObj.GetComponent<ParticleSystemRenderer>();
            Shader sh = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Particles/Standard Unlit");
            if (sh != null)
            {
                Material mat = new Material(sh);
                mat.color = new Color(0.45f, 1.0f, 0.22f, 0.9f);
                psRenderer.sharedMaterial = mat;
            }

            ps.Play();
            Destroy(fxObj, 3.4f);
        }

        public void DieAndRespawn()
        {
            _isDead = false;
            DoorTransitionTrigger.ForceClearFade();
            ResetAcidMeltVisuals();
            currentHealth = maxHealth;
            currentStamina = maxStamina;
            _staminaExhaustedLock = false;
            _staminaRegenDelayTimer = 0f;
            _verticalVelocity = 0f;
            RefillEstus();
            OnStaminaChanged?.Invoke(currentStamina, maxStamina);

            // Raycast downward from above respawnPosition so the player NEVER spawns inside/under the floor
            Vector3 safeSpawn = respawnPosition + Vector3.up * 1.0f;
            if (Physics.Raycast(respawnPosition + Vector3.up * 4.0f, Vector3.down, out RaycastHit groundHit, 12f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                safeSpawn = groundHit.point + Vector3.up * 0.35f;
            }

            if (_cc != null)
            {
                _cc.enabled = false;
                transform.position = safeSpawn;
                _cc.enabled = true;
            }
            else
            {
                transform.position = safeSpawn;
            }
            _lastSafeGroundY = transform.position.y;

            if (visualRoot != null)
            {
                if (!visualRoot.gameObject.activeSelf) visualRoot.gameObject.SetActive(true);
                if (_visualInitialScale.sqrMagnitude > 0.001f) visualRoot.localScale = _visualInitialScale;
                visualRoot.localPosition = _visualInitialLocalPos;
                visualRoot.localRotation = _visualInitialLocalRot;
            }

            ClearAllConditions();
            ImmuneEnemies.ImmuneEnemyBase.RespawnAllEnemies();

            if (animator != null)
            {
                animator.SetBool(HashIsDead, false);
                animator.SetBool(HashIsMoving, false);
                animator.SetFloat(HashSpeed, 0f);
                animator.CrossFadeInFixedTime("Idle", 0.10f, 0, 0f);
            }

            TriggerShaderFlash(new Color(0.25f, 2.0f, 2.4f, 1f), 0.25f);
            Debug.Log($"[Virus Player] Renascido em: {respawnPosition}");
        }

        public void ClearAllConditions()
        {
            _attackActiveTimer = 0f;
            _attackCooldownTimer = 0f;
            _dodgeTimer = 0f;
            _dodgeCooldownTimer = 0f;
            _dodgeIFrameTimer = 0f;
            _combatLungeTimer = 0f;
            _hitAnimCooldownTimer = 0f;
            _hurtSfxCooldownTimer = 0f;
            _playerBloodCooldownTimer = 0f;
            _estusCooldownTimer = 0f;
            _attackMoveSlowTimer = 0f;
            _attackMoveSlowMultiplier = 1f;
            _externalSnareTimer = 0f;
            _damageFlashTimer = 0f;
            _velocity = Vector3.zero;
            _currentHorizVelocity = Vector3.zero;
            _activeWeapon?.CancelActiveAttack();

            if (_renderers != null)
            {
                for (int i = 0; i < _renderers.Length; i++)
                {
                    Renderer r = _renderers[i];
                    if (r == null || r.material == null) continue;

                    if (r.material.HasProperty(PropHitFlash))
                    {
                        r.material.SetFloat(PropHitFlash, 0f);
                    }
                    else if (_origColors != null && i < _origColors.Length)
                    {
                        if (r.material.HasProperty("_BaseColor"))
                            r.material.SetColor("_BaseColor", _origColors[i]);
                        else if (r.material.HasProperty("_Color"))
                            r.material.color = _origColors[i];
                    }
                }
            }

            BurnStatusEffect[] burns = GetComponents<BurnStatusEffect>();
            for (int i = 0; i < burns.Length; i++)
            {
                if (burns[i] != null)
                {
                    Destroy(burns[i]);
                }
            }

            AcidDamageZone.ClearTargetFromAll(gameObject);
        }

        public void SetRespawnPoint(Vector3 pos)
        {
            respawnPosition = pos;
        }

        /// <summary>
        /// Tenta consumir estamina para ataques/habilidades/pulo (estilo Souls: permite o golpe
        /// se nao estiver exausto, descontando ate 0 e iniciando o delay de regeneracao).
        /// </summary>
        public bool TryConsumeActionStamina(float cost)
        {
            if (cost <= 0f) return true;
            float minRequired = Mathf.Min(cost, 4f);
            if (currentStamina < minRequired)
            {
                return false;
            }
            SpendStamina(cost);
            return true;
        }

        private void SpendStamina(float amount)
        {
            if (amount <= 0f) return;
            currentStamina = Mathf.Max(0f, currentStamina - amount);
            _staminaRegenDelayTimer = staminaRegenDelay;
            OnStaminaChanged?.Invoke(currentStamina, maxStamina);
        }

        private void UpdateStaminaRegen(float dt)
        {
            if (_staminaRegenDelayTimer > 0f)
            {
                _staminaRegenDelayTimer = Mathf.Max(0f, _staminaRegenDelayTimer - dt);
                return;
            }

            if (currentStamina < maxStamina)
            {
                currentStamina = Mathf.Min(maxStamina, currentStamina + staminaRegenRate * dt);
                if (_staminaExhaustedLock && currentStamina >= minStaminaToResumeRun)
                {
                    _staminaExhaustedLock = false;
                }
                OnStaminaChanged?.Invoke(currentStamina, maxStamina);
            }
        }

        private static bool WasEstusPressedThisFrame()
        {
#if ENABLE_INPUT_SYSTEM
            Keyboard kb = Keyboard.current;
            return kb != null && kb.rKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.R);
#endif
        }

        public bool TryUseEstus()
        {
            if (_isDead || _estusCooldownTimer > 0f || currentEstusCharges <= 0 || currentHealth >= maxHealth)
            {
                return false;
            }

            currentEstusCharges--;
            _estusCooldownTimer = estusCooldown;
            Heal(estusHealAmount);
            EmitCombatParticles(transform.position + Vector3.up * 0.85f, Quaternion.LookRotation(Vector3.up), 18);
            CombatSfxBank.Play(CombatSfxId.PlayerHeal, 0.88f, 0.97f, 1.04f, 0.08f);

            OnEstusChanged?.Invoke(currentEstusCharges, maxEstusCharges);
            OnEstusUsed?.Invoke();
            return true;
        }

        public void RefillEstus()
        {
            currentEstusCharges = maxEstusCharges;
            OnEstusChanged?.Invoke(currentEstusCharges, maxEstusCharges);
        }

        private void EnsureAttackEffects()
        {
            if (attackParticles == null)
            {
                Transform existing = transform.Find("Virus_Attack_SlashFX");
                if (existing != null)
                {
                    attackParticles = existing.GetComponent<ParticleSystem>();
                }
            }

            if (attackParticles == null)
            {
                GameObject fxGo = new GameObject("Virus_Attack_SlashFX");
                fxGo.transform.SetParent(transform, false);
                fxGo.transform.localPosition = new Vector3(0f, 0.85f, 0.75f);

                attackParticles = fxGo.AddComponent<ParticleSystem>();
                var main = attackParticles.main;
                main.playOnAwake = false;
                main.loop = false;
                main.duration = 0.5f;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.18f, 0.38f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(3.2f, 6.8f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.10f, 0.22f);
                main.gravityModifier = 0.6f;
                main.startColor = new ParticleSystem.MinMaxGradient(
                    new Color(0.18f, 0.92f, 1.0f, 1f),
                    new Color(0.65f, 0.28f, 1.0f, 1f)
                );
                main.simulationSpace = ParticleSystemSimulationSpace.World;

                var emission = attackParticles.emission;
                emission.enabled = false;

                var shape = attackParticles.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Cone;
                shape.angle = 38f;
                shape.radius = 0.32f;

                var sizeOverLife = attackParticles.sizeOverLifetime;
                sizeOverLife.enabled = true;
                sizeOverLife.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.1f));

                var psRenderer = fxGo.GetComponent<ParticleSystemRenderer>();
                Shader sh = Shader.Find("MapaEstomago/URP/PS1_PixelParticle");
                if (sh == null) sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
                if (sh != null && psRenderer != null)
                {
                    Material mat = new Material(sh);
                    mat.SetColor("_BaseColor", new Color(0.25f, 0.95f, 1.0f, 1f));
                    mat.SetFloat("_EmissionBoost", 2.4f);
                    psRenderer.sharedMaterial = mat;
                }
            }
        }

        private void EnsureCombatAudio()
        {
            CombatSfxBank.EnsureExists();
            CombatHitstopManager.EnsureExists();
            BloodSplatterSystem.EnsureExists();
            CombatCameraJuice.EnsureExists();
        }

        private void OnGUI()
        {
            if (!showDebugOnGui) return;

            float barWidth = 190f;
            float barHeight = 18f;
            float healthRatio = Mathf.Clamp01(currentHealth / maxHealth);

            GUI.Box(new Rect(18, 18, barWidth + 4, barHeight + 4), GUIContent.none);
            Color oldCol = GUI.color;
            GUI.color = IsInvulnerable
                ? new Color(0.75f, 1.0f, 1.0f)
                : Color.Lerp(new Color(0.85f, 0.15f, 0.25f), new Color(0.15f, 0.85f, 0.95f), healthRatio);
            GUI.DrawTexture(new Rect(20, 20, barWidth * healthRatio, barHeight), Texture2D.whiteTexture);
            GUI.color = oldCol;

            string stateLabel = _isDead
                ? "MORTO (Renascendo...)"
                : (IsDodging ? "ESQUIVA (INVULNERAVEL)" : (IsAttacking ? "ATACANDO!" : "ATIVO"));
            string dodgeLabel = _dodgeCooldownTimer > 0f
                ? $"Esquiva: {_dodgeCooldownTimer:0.0}s"
                : "Esquiva: PRONTA [Espaco]";
            string weaponLabel = _activeWeapon != null
                ? $" | {_activeWeapon.WeaponDisplayName}: {_activeWeapon.HudStatusText}"
                : "";

            GUI.Label(new Rect(20, 42, 460, 22),
                $"Virus HP: {Mathf.CeilToInt(currentHealth)}/{maxHealth} [{stateLabel}]");
            GUI.Label(new Rect(20, 62, 540, 22),
                $"{dodgeLabel}{weaponLabel}");
            GUI.Label(new Rect(20, 82, 460, 22),
                "WASD:Mover | Shift:Correr | Espaco:Esquiva | Q:Pulo");
            GUI.Label(new Rect(20, 100, 480, 22),
                "LMB/J:Ataque | RMB+LMB:Mira/Especial | 1-9:Trocar Arma");
        }
    }
}
