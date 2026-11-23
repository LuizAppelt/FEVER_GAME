using System.Collections;
using MapaEstomago;
using UnityEngine;

namespace BossTenia
{
    [RequireComponent(typeof(TeniaBossController))]
    [RequireComponent(typeof(TeniaBossHealth))]
    public class TeniaBossAI : MonoBehaviour
    {
        public enum EncounterState
        {
            Dormant,
            IntroRoar,
            MeleeHunt,
            ConstrictAttack,
            RangedArtillery,
            ClimbingToPillar,
            PillarPerched,
            PillarAirHop,
            PillarLeap,
            DescendingFromPillar,
            Staggered,
            Phase2Transition,
            Defeated
        }

        private enum LeapPhase
        {
            Windup,
            Flying,
            ImpactRecover
        }

        private enum MeleePhase
        {
            Stalk,
            BiteWindup,
            BiteStrike,
            TailWindup,
            TailSweep,
            Recover
        }

        private enum ConstrictPhase
        {
            Windup,
            DashGrab,
            Squeezing,
            RecoverMiss
        }

        [Header("Referencias Principais")]
        public TeniaBossController controller;
        public TeniaBossHealth health;
        public TeniaArenaBounds arenaBounds;
        public TeniaAudioController audioController;
        public Transform playerTarget;

        [Header("Spawn & Reset")]
        public bool autoPlaceInCathedralOnStart = true;
        public Vector3 dormantSpawnPosition = new Vector3(100.40f, 2.20f, 173.50f);
        public Vector3 dormantFacingDirection = new Vector3(0f, 0f, -1f);
        public bool autoActivateWhenPlayerEnters = true;

        [Header("Locomocao & Modos de Combate")]
        public float meleeSpeedP1 = 6.3f;
        public float meleeSpeedP2 = 8.4f;
        public float rangedRepositionSpeedP1 = 6.6f;
        public float rangedRepositionSpeedP2 = 8.5f;
        public float climbMoveSpeedP1 = 9.2f;
        public float climbMoveSpeedP2 = 11.6f;
        public float turnSpeedDegP1 = 175f;
        public float turnSpeedDegP2 = 230f;
        public float groundWallMargin = 1.45f;

        [Header("Dano Corpo a Corpo (Mordida & Rabada)")]
        public float biteRange = 6.8f;
        public float biteDamageP1 = 26f;
        public float biteDamageP2 = 34f;
        public float tailSweepRadius = 4.9f;
        public float tailSweepDamageP1 = 23f;
        public float tailSweepDamageP2 = 31f;
        public float contactBodyRadius = 1.10f;
        public float contactBodyDamageP1 = 14f;
        public float contactBodyDamageP2 = 19f;
        public float contactCooldown = 0.88f;

        [Header("Constricao de Cobra (Enrolar e Apertar)")]
        public float constrictGrabRadius = 3.55f;
        public float constrictDuration = 2.65f;
        public float constrictSqueezeInterval = 0.54f;
        public float constrictSqueezeDamageP1 = 10f;
        public float constrictSqueezeDamageP2 = 14f;
        public float constrictEscapeDamageThreshold = 45f;

        [Header("Artilharia & Pilares (Alta Frequencia + Salto AoE)")]
        public float preferredRangedDistance = 13.5f;
        public float tooCloseToKeepRanged = 7.2f;
        public float rangedVolleyIntervalP1 = 2.05f;
        public float rangedVolleyIntervalP2 = 1.45f;
        public float pillarVolleyIntervalP1 = 1.65f;
        public float pillarVolleyIntervalP2 = 1.15f;
        public int pillarVolleysPerPerchP1 = 3;
        public int pillarVolleysPerPerchP2 = 4;
        public float pillarLeapMaxRange = 24.0f;
        public float pillarLeapImpactRadiusP1 = 4.6f;
        public float pillarLeapImpactRadiusP2 = 5.4f;
        public float pillarLeapDamageP1 = 32f;
        public float pillarLeapDamageP2 = 42f;
        public float spitTelegraphDurationP1 = 1.12f;
        public float spitTelegraphDurationP2 = 0.88f;
        public float acidImpactRadiusP1 = 2.60f;
        public float acidImpactRadiusP2 = 3.05f;
        public float acidImpactDamageP1 = 24f;
        public float acidImpactDamageP2 = 32f;
        public float puddleRadius = 2.55f;
        public float puddleDuration = 7.0f;
        public float puddleDps = 13f;

        [Header("Quebra de Pilar & Orbes de Cura")]
        public float pillarPerchedStaggerThreshold = 95f;
        public float pillarLeapWindupStaggerThreshold = 40f;
        public float pillarStaggerDuration = 2.4f;
        public float staggerDamageMultiplier = 1.50f;
        public float soulOrbHealAmount = 28f;
        public float soulOrbRespawnInterval = 26f;
        public int maxActiveSoulOrbs = 3;

        [Header("Debug / Estado Atual")]
        [SerializeField] private EncounterState currentState = EncounterState.Dormant;

        public EncounterState CurrentState => currentState;
        public bool IsEncounterActive => currentState != EncounterState.Dormant && currentState != EncounterState.Defeated;

        private SimplePlayerController _playerController;
        private IAcidDamageable _playerDamageable;
        private IsometricCameraFollow _isoCamera;

        private Vector3 _lastPlayerPos;
        private Vector3 _playerEstVelocity;
        private Vector3 _lastPillarPos = new Vector3(-999f, -999f, -999f);
        private Vector3 _targetPillarPos;
        private Vector3 _pillarApproachPoint;

        private float _stateTimer;
        private float _modeTimer;
        private float _modeDurationLimit;
        private float _contactCooldownTimer;
        private float _meleeActionCooldown;
        private float _constrictGlobalCooldown;
        private float _playerOutsideNaveTimer;

        // Melee sub-state
        private MeleePhase _meleePhase = MeleePhase.Stalk;
        private Vector3 _pendingStrikeSpot;
        private float _orbitDirectionSign = 1f;
        private int _meleeAttackCycle;

        // Constrict sub-state
        private ConstrictPhase _constrictPhase = ConstrictPhase.Windup;
        private Vector3 _constrictTargetSpot;
        private float _constrictTickTimer;
        private float _constrictDamageTakenToBreak;
        private bool _biteOrConstrictDealt;

        // Ranged / Pillar / Leap sub-state
        private bool _isApproachingPillarBase;
        private float _spitCooldownTimer;
        private int _rangedSalvosRemaining;
        private int _volleysRemainingOnPillar;
        private float _tooCloseTimer;
        private int _tacticalPatternStep;
        private LeapPhase _leapPhase = LeapPhase.Windup;
        private Vector3 _leapTargetSpot;
        private float _leapWindupDuration;
        private float _leapImpactRadius;

        private float _baseHeadElevation = 2.85f;
        private float _baseSlitherSpeed = 3.4f;
        private TeniaAcidTelegraph _activeMeleeTelegraph;

        // Pillar air hop, stagger & healing orbs
        private float _pillarStaggerAccumDmg;
        private bool _didPillarAirHopThisPerch;
        private Vector3 _hopOriginPillarPos;
        private int _hopTrailBlobsSpawned;

        // Post-death groan, 5s acid melt & vanish timeline
        private const float DeathMeltStartSeconds = 5.0f;
        private const float DeathMeltDurationSeconds = 2.4f;
        private bool _defeatAcidMeltTriggered;
        private bool _defeatCorpseHidden;
        private float _defeatNextGroanTime;
        private readonly System.Collections.Generic.List<Vector3> _defeatAcidSpots = new System.Collections.Generic.List<Vector3>(8);
        private float _orbRespawnTimer;
        private int _orbSpawnAnchorIdx;

        private void Awake()
        {
            if (controller == null) controller = GetComponent<TeniaBossController>();
            if (health == null) health = GetComponent<TeniaBossHealth>();
            if (arenaBounds == null) arenaBounds = GetComponent<TeniaArenaBounds>();
            if (arenaBounds == null) arenaBounds = FindAnyObjectByType<TeniaArenaBounds>();
            if (audioController == null) audioController = GetComponent<TeniaAudioController>();
            if (audioController == null) audioController = gameObject.AddComponent<TeniaAudioController>();
            _isoCamera = FindAnyObjectByType<IsometricCameraFollow>();

            if (controller != null)
            {
                _baseHeadElevation = controller.headElevation;
                _baseSlitherSpeed = controller.slitherSpeed;
                if (arenaBounds != null && controller.arenaBounds == null)
                {
                    controller.arenaBounds = arenaBounds;
                }
            }

            ResolvePlayerReference();
        }

        private void OnEnable()
        {
            if (health != null)
            {
                health.OnDamaged += HandleBossDamaged;
                health.OnPhase2Entered += HandlePhase2Entered;
                health.OnDefeated += HandleBossDefeated;
            }
        }

        private void OnDisable()
        {
            if (health != null)
            {
                health.OnDamaged -= HandleBossDamaged;
                health.OnPhase2Entered -= HandlePhase2Entered;
                health.OnDefeated -= HandleBossDefeated;
            }
            ClearMeleeTelegraph();
            TeniaSoulOrb.ClearAllActiveOrbs();
            if (audioController != null) audioController.SetEncounterAmbience(false, false);
            if (_isoCamera != null) _isoCamera.SetBossZoomActive(false);
        }

        private void Start()
        {
            ResolvePlayerReference();
            ResetToDormantState();
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            ResolvePlayerReference();
            TrackPlayerVelocity(dt);

            if (_contactCooldownTimer > 0f) _contactCooldownTimer -= dt;
            if (_constrictGlobalCooldown > 0f) _constrictGlobalCooldown -= dt;

            if (currentState != EncounterState.Dormant && currentState != EncounterState.Defeated)
            {
                TickArenaSoulOrbSpawner(dt);
                if (ShouldResetBecausePlayerDiedOutside())
                {
                    ResetToDormantState();
                    return;
                }
            }

            switch (currentState)
            {
                case EncounterState.Dormant:
                    TickDormant();
                    break;
                case EncounterState.IntroRoar:
                    TickIntroRoar(dt);
                    break;
                case EncounterState.MeleeHunt:
                    TickMeleeHunt(dt);
                    break;
                case EncounterState.ConstrictAttack:
                    TickConstrictAttack(dt);
                    break;
                case EncounterState.RangedArtillery:
                    TickRangedArtillery(dt);
                    break;
                case EncounterState.ClimbingToPillar:
                    TickClimbingToPillar(dt);
                    break;
                case EncounterState.PillarPerched:
                    TickPillarPerched(dt);
                    break;
                case EncounterState.PillarAirHop:
                    TickPillarAirHop(dt);
                    break;
                case EncounterState.PillarLeap:
                    TickPillarLeap(dt);
                    break;
                case EncounterState.DescendingFromPillar:
                    TickDescendingFromPillar(dt);
                    break;
                case EncounterState.Staggered:
                    TickStaggered(dt);
                    break;
                case EncounterState.Phase2Transition:
                    TickPhase2Transition(dt);
                    break;
                case EncounterState.Defeated:
                    TickDefeated(dt);
                    break;
            }
        }

        public void ForceStartEncounter()
        {
            if (currentState != EncounterState.Dormant || (health != null && health.IsDead))
            {
                return;
            }
            BeginIntroRoar();
        }

        public void ResetToDormantState()
        {
            StopAllCoroutines();
            ClearMeleeTelegraph();
            TeniaSoulOrb.ClearAllActiveOrbs();
            audioController?.SetEncounterAmbience(false, false);

            if (_playerController != null)
            {
                _playerController.ReleaseBossConstriction();
            }
            if (_isoCamera != null)
            {
                _isoCamera.SetBossZoomActive(false);
            }

            currentState = EncounterState.Dormant;
            _stateTimer = 0f;
            _modeTimer = 0f;
            _tacticalPatternStep = 0;
            _contactCooldownTimer = 0f;
            _constrictGlobalCooldown = 0f;
            _pillarStaggerAccumDmg = 0f;
            _didPillarAirHopThisPerch = false;
            _orbRespawnTimer = soulOrbRespawnInterval * 0.65f;
            _lastPillarPos = new Vector3(-999f, -999f, -999f);

            arenaBounds?.SyncWithCathedralAnchorIfNeeded();
            if (autoPlaceInCathedralOnStart && arenaBounds != null && arenaBounds.cathedralAnchor != null)
            {
                dormantSpawnPosition = arenaBounds.GetDormantSpawnWorldPos();
                dormantFacingDirection = -arenaBounds.GetCathedralForwardXZ();
            }

            Vector3 spawnPos = arenaBounds != null
                ? arenaBounds.ClampPointToNave(dormantSpawnPosition, 1.4f)
                : dormantSpawnPosition;
            if (arenaBounds != null) spawnPos.y = arenaBounds.floorY;

            transform.position = spawnPos;
            Vector3 look = new Vector3(dormantFacingDirection.x, 0f, dormantFacingDirection.z);
            if (look.sqrMagnitude > 0.001f)
            {
                transform.rotation = Quaternion.LookRotation(look.normalized, Vector3.up);
            }

            if (controller != null)
            {
                controller.SetDeadFrozen(false);
                controller.SetVisualsVisible(true);
                controller.SetPillarLeapPrep(0f);
                controller.headElevation = _baseHeadElevation;
                controller.slitherSpeed = _baseSlitherSpeed * 0.45f;
                controller.SetGroundCoilTightness(0.35f);
                controller.SetAimTarget(playerTarget);
                controller.SetPoseImmediateGround();
            }

            if (health != null)
            {
                health.ResetFullHealth();
                health.SetHudVisible(false);
            }

            if (arenaBounds != null)
            {
                arenaBounds.SetPortalBarrierActive(false);
            }

            TeniaHitboxRelay[] relays = GetComponentsInChildren<TeniaHitboxRelay>(true);
            for (int i = 0; i < relays.Length; i++)
            {
                if (relays[i] != null && relays[i].TryGetComponent<Collider>(out var col))
                {
                    col.enabled = true;
                }
            }
        }

        private void TickDormant()
        {
            if (!autoActivateWhenPlayerEnters || playerTarget == null || arenaBounds == null)
            {
                return;
            }

            if (arenaBounds.IsPlayerInsideCathedral(playerTarget.position))
            {
                BeginIntroRoar();
            }
        }

        private void BeginIntroRoar()
        {
            currentState = EncounterState.IntroRoar;
            _stateTimer = 0f;
            _orbRespawnTimer = soulOrbRespawnInterval * 0.65f;

            audioController?.SetEncounterAmbience(true, false);
            audioController?.PlayRoar(false);
            _isoCamera?.TriggerShake(0.32f, 0.25f);

            if (_isoCamera != null)
            {
                _isoCamera.SetBossZoomActive(true);
            }
            if (arenaBounds != null)
            {
                arenaBounds.SetPortalBarrierActive(true);
            }
            if (health != null)
            {
                health.SetHudVisible(true, "PARASITA LITURGICO DO PILORO");
            }
            if (controller != null)
            {
                controller.slitherSpeed = _baseSlitherSpeed * 1.15f;
                controller.SetGroundCoilTightness(0.55f);
                controller.SetAimTarget(playerTarget);
            }
        }

        private void TickIntroRoar(float dt)
        {
            _stateTimer += dt;
            if (playerTarget != null)
            {
                RotateRootToward(playerTarget.position - transform.position, 220f, dt);
            }

            if (_stateTimer >= 1.05f)
            {
                EnterMeleeHunt();
            }
        }

        private void TransitionToNextTacticalMode()
        {
            _tacticalPatternStep++;
            bool p2 = health != null && health.IsPhase2;

            // Ciclo mais agressivo e vertical: sobe nos pilares com alta frequencia!
            // 1: Pilar -> Salto -> 2: Constricao -> 3: Pilar -> Salto -> 4: Artilharia -> (ja vai ao Pilar no fim) -> Repete
            int mod = _tacticalPatternStep % 4;
            if (mod == 1 || mod == 3)
            {
                BeginClimbingToPillar();
            }
            else if (mod == 2)
            {
                if (_constrictGlobalCooldown <= 0f)
                {
                    EnterConstrictAttack();
                }
                else
                {
                    EnterRangedArtillery();
                }
            }
            else
            {
                if (p2 && Random.value < 0.45f)
                {
                    BeginClimbingToPillar();
                }
                else
                {
                    EnterRangedArtillery();
                }
            }

            if (health != null)
            {
                string sub = p2 ? "FASE II: FURIA DO PILORO" : "PARASITA LITURGICO DO PILORO";
                health.SetHudVisible(true, sub);
            }
        }

        // =====================================================================
        // MODO 1: CACA CORPO A CORPO (Mordida + Rabada, sem cuspe de acido)
        // =====================================================================

        private void EnterMeleeHunt()
        {
            ClearMeleeTelegraph();
            currentState = EncounterState.MeleeHunt;
            _meleePhase = MeleePhase.Stalk;
            _stateTimer = 0f;
            _modeTimer = 0f;
            _meleeActionCooldown = 0.55f;
            _playerOutsideNaveTimer = 0f;
            _orbitDirectionSign = Random.value > 0.5f ? 1f : -1f;

            bool p2 = health != null && health.IsPhase2;
            _modeDurationLimit = p2 ? Random.Range(4.8f, 6.4f) : Random.Range(5.4f, 7.2f);

            if (health != null)
            {
                health.SetHudVisible(true, p2 ? "MODO CACADORA: BOTE & RABADA (FASE II)" : "MODO CACADORA: MORDIDA & RABADA");
            }
            if (controller != null)
            {
                controller.slitherSpeed = p2 ? _baseSlitherSpeed * 1.42f : _baseSlitherSpeed * 1.12f;
                controller.SetGroundCoilTightness(0.32f);
                controller.SetAimTarget(playerTarget);
                if (controller.poseState != TeniaBossController.BossPoseState.GroundSlither)
                {
                    controller.SetStateGround();
                }
            }
        }

        private void TickMeleeHunt(float dt)
        {
            _stateTimer += dt;
            _modeTimer += dt;
            if (_meleeActionCooldown > 0f) _meleeActionCooldown -= dt;

            bool p2 = health != null && health.IsPhase2;

            if (playerTarget == null)
            {
                AdvanceGroundRoot(meleeSpeedP1 * 0.5f * dt);
                return;
            }

            Vector3 headPos = controller != null && controller.headSegment != null
                ? controller.headSegment.position
                : transform.position;
            Vector3 tailPos = controller != null ? controller.GetTailWorldPosition() : transform.position - transform.forward * 6f;

            float distHeadToPlayer = FlatDistance(headPos, playerTarget.position);
            float distRootToPlayer = FlatDistance(transform.position, playerTarget.position);
            float distTailToPlayer = FlatDistance(tailPos, playerTarget.position);

            switch (_meleePhase)
            {
                case MeleePhase.Stalk:
                    Vector3 toPlayer = playerTarget.position - transform.position;
                    toPlayer.y = 0f;
                    Vector3 baseDir = toPlayer.sqrMagnitude > 0.01f ? toPlayer.normalized : transform.forward;

                    // Curvas S mais pronunciadas e flanqueamento para o corpo enrolar no rastro
                    float flankBias = Mathf.Sin(_modeTimer * 1.8f) * 32f + _orbitDirectionSign * (distRootToPlayer < 4.5f ? 38f : 14f);
                    Vector3 desiredDir = Quaternion.Euler(0f, flankBias, 0f) * baseDir;
                    if (arenaBounds != null)
                    {
                        desiredDir = arenaBounds.SteerInsideNave(transform.position, desiredDir, groundWallMargin);
                    }

                    RotateRootToward(desiredDir, p2 ? turnSpeedDegP2 : turnSpeedDegP1, dt);
                    float speed = p2 ? meleeSpeedP2 : meleeSpeedP1;
                    if (distRootToPlayer < 1.8f) speed *= 0.45f;
                    AdvanceGroundRoot(speed * dt);
                    EvaluateContactBodyDamage(p2);

                    // Anti-esconderijo: se o jogador passou dos pilares para os corredores laterais, força ataque a distancia / pilar!
                    if (arenaBounds != null && !arenaBounds.IsInsideNave(playerTarget.position, -0.45f))
                    {
                        _playerOutsideNaveTimer += dt;
                        if (_playerOutsideNaveTimer >= 1.15f)
                        {
                            _playerOutsideNaveTimer = 0f;
                            if (Random.value < 0.55f)
                            {
                                BeginClimbingToPillar();
                            }
                            else
                            {
                                EnterRangedArtillery();
                            }
                            return;
                        }
                    }
                    else
                    {
                        _playerOutsideNaveTimer = Mathf.Max(0f, _playerOutsideNaveTimer - dt * 2f);
                    }

                    if (_meleeActionCooldown <= 0f)
                    {
                        // Prioriza rabada se o jogador esta proximo da cauda/flanco, ou mordida se esta na frente
                        bool tryTail = distTailToPlayer <= tailSweepRadius * 0.98f || (_meleeAttackCycle % 3 == 2 && distRootToPlayer <= 5.5f);
                        if (tryTail)
                        {
                            StartTailWindup(p2);
                        }
                        else if (distHeadToPlayer <= biteRange || distRootToPlayer <= biteRange)
                        {
                            StartBiteWindup(p2);
                        }
                    }

                    if (_modeTimer >= _modeDurationLimit)
                    {
                        TransitionToNextTacticalMode();
                    }
                    break;

                case MeleePhase.BiteWindup:
                    RotateRootToward(playerTarget.position - transform.position, 260f, dt);
                    if (_stateTimer >= (p2 ? 0.38f : 0.48f))
                    {
                        _meleePhase = MeleePhase.BiteStrike;
                        _stateTimer = 0f;
                        _biteOrConstrictDealt = false;
                        ClearMeleeTelegraph();
                        audioController?.PlayBiteSnap(false);
                    }
                    break;

                case MeleePhase.BiteStrike:
                    Vector3 lungeDir = (_pendingStrikeSpot - transform.position);
                    lungeDir.y = 0f;
                    if (lungeDir.sqrMagnitude > 0.05f)
                    {
                        RotateRootToward(lungeDir, 300f, dt);
                    }
                    AdvanceGroundRoot((p2 ? 12.8f : 10.4f) * dt);

                    if (!_biteOrConstrictDealt && _stateTimer >= 0.06f)
                    {
                        Vector3 liveHead = controller != null && controller.headSegment != null ? controller.headSegment.position : transform.position;
                        if (FlatDistance(liveHead, playerTarget.position) <= 2.95f || FlatDistance(_pendingStrikeSpot, playerTarget.position) <= 2.35f)
                        {
                            _biteOrConstrictDealt = true;
                            DealDirectDamageToPlayer(p2 ? biteDamageP2 : biteDamageP1);
                        }
                    }

                    if (_stateTimer >= 0.28f)
                    {
                        _meleePhase = MeleePhase.Recover;
                        _stateTimer = 0f;
                        _meleeAttackCycle++;
                    }
                    break;

                case MeleePhase.TailWindup:
                    // Mantem o corpo leve girando enquanto prepara a chicotada de cauda
                    if (_stateTimer >= (p2 ? 0.42f : 0.52f))
                    {
                        _meleePhase = MeleePhase.TailSweep;
                        _stateTimer = 0f;
                        _biteOrConstrictDealt = false;
                        ClearMeleeTelegraph();
                        audioController?.PlayTailWhip();
                    }
                    break;

                case MeleePhase.TailSweep:
                    if (!_biteOrConstrictDealt && _stateTimer >= 0.12f)
                    {
                        _biteOrConstrictDealt = true;
                        Vector3 liveTail = controller != null ? controller.GetTailWorldPosition() : tailPos;
                        float distFromCenter = FlatDistance(_pendingStrikeSpot, playerTarget.position);
                        float distFromTail = FlatDistance(liveTail, playerTarget.position);
                        if (distFromCenter <= tailSweepRadius || distFromTail <= tailSweepRadius * 0.85f)
                        {
                            DealDirectDamageToPlayer(p2 ? tailSweepDamageP2 : tailSweepDamageP1);
                            if (_playerController != null)
                            {
                                Vector3 push = playerTarget.position - _pendingStrikeSpot;
                                push.y = 0f;
                                if (push.sqrMagnitude < 0.01f) push = transform.right;
                                _playerController.ApplyExternalKnockback(push.normalized * 1.45f);
                            }
                        }
                    }

                    if (_stateTimer >= 0.36f)
                    {
                        _meleePhase = MeleePhase.Recover;
                        _stateTimer = 0f;
                        _meleeAttackCycle++;
                    }
                    break;

                case MeleePhase.Recover:
                    RotateRootToward(playerTarget.position - transform.position, 140f, dt);
                    if (_stateTimer >= (p2 ? 0.28f : 0.42f))
                    {
                        _meleePhase = MeleePhase.Stalk;
                        _meleeActionCooldown = p2 ? 0.85f : 1.15f;
                        _orbitDirectionSign = -_orbitDirectionSign;
                        if (_modeTimer >= _modeDurationLimit)
                        {
                            TransitionToNextTacticalMode();
                        }
                    }
                    break;
            }
        }

        private void StartBiteWindup(bool p2)
        {
            _meleePhase = MeleePhase.BiteWindup;
            _stateTimer = 0f;

            Vector3 targetPt = playerTarget.position;
            if (arenaBounds != null) targetPt = arenaBounds.ClampLandingPoint(targetPt, 1.0f);
            _pendingStrikeSpot = targetPt;

            float windup = p2 ? 0.38f : 0.48f;
            controller?.TriggerBiteAnimation(windup, 0.28f, 0.34f);
            audioController?.PlayBiteSnap(true);

            ClearMeleeTelegraph();
            _activeMeleeTelegraph = TeniaAcidTelegraph.Spawn(_pendingStrikeSpot, 2.35f, windup, p2);
        }

        private void StartTailWindup(bool p2)
        {
            _meleePhase = MeleePhase.TailWindup;
            _stateTimer = 0f;

            Vector3 tailPivot = controller != null ? controller.GetTailWorldPosition() : transform.position - transform.forward * 5f;
            if (arenaBounds != null) tailPivot = arenaBounds.ClampLandingPoint(tailPivot, 1.0f);
            _pendingStrikeSpot = tailPivot;

            float windup = p2 ? 0.42f : 0.52f;
            float sideSign = Random.value > 0.5f ? 1f : -1f;
            controller?.TriggerTailSweepAnimation(sideSign, windup, 0.30f, 0.42f);

            ClearMeleeTelegraph();
            _activeMeleeTelegraph = TeniaAcidTelegraph.Spawn(_pendingStrikeSpot, tailSweepRadius, windup, p2);
        }

        // =====================================================================
        // MODO 2: CONSTRICAO DE COBRA (Enrola e Aperta o Jogador)
        // =====================================================================

        private void EnterConstrictAttack()
        {
            ClearMeleeTelegraph();
            currentState = EncounterState.ConstrictAttack;
            _constrictPhase = ConstrictPhase.Windup;
            _stateTimer = 0f;
            _biteOrConstrictDealt = false;
            _constrictDamageTakenToBreak = 0f;
            _constrictGlobalCooldown = 11.5f;

            bool p2 = health != null && health.IsPhase2;
            Vector3 grabCenter = playerTarget != null
                ? playerTarget.position
                : (arenaBounds != null ? arenaBounds.arenaCenter : transform.position);
            if (arenaBounds != null) grabCenter = arenaBounds.ClampLandingPoint(grabCenter, 1.6f);
            _constrictTargetSpot = grabCenter;

            if (health != null)
            {
                health.SetHudVisible(true, "BOTE CONSTRITOR! ESQUIVE DO ANEL!");
            }
            if (controller != null)
            {
                controller.SetGroundCoilTightness(0.95f); // enrola o corpo antes de dar o bote!
                controller.TriggerBiteAnimation(0.70f, 0.38f, 0.30f);
            }

            _activeMeleeTelegraph = TeniaAcidTelegraph.Spawn(_constrictTargetSpot, constrictGrabRadius, p2 ? 0.62f : 0.75f, true);
        }

        private void TickConstrictAttack(float dt)
        {
            _stateTimer += dt;
            bool p2 = health != null && health.IsPhase2;

            switch (_constrictPhase)
            {
                case ConstrictPhase.Windup:
                    if (playerTarget != null)
                    {
                        RotateRootToward(_constrictTargetSpot - transform.position, 280f, dt);
                    }
                    if (_stateTimer >= (p2 ? 0.62f : 0.75f))
                    {
                        _constrictPhase = ConstrictPhase.DashGrab;
                        _stateTimer = 0f;
                        ClearMeleeTelegraph();
                    }
                    break;

                case ConstrictPhase.DashGrab:
                    Vector3 toSpot = _constrictTargetSpot - transform.position;
                    toSpot.y = 0f;
                    float dist = toSpot.magnitude;
                    if (dist > 0.25f)
                    {
                        RotateRootToward(toSpot, 340f, dt);
                        AdvanceGroundRoot(Mathf.Min(dist + 0.5f, (p2 ? 17.5f : 14.5f) * dt));
                    }

                    bool captured = playerTarget != null && (FlatDistance(playerTarget.position, _constrictTargetSpot) <= constrictGrabRadius
                                     || FlatDistance(playerTarget.position, transform.position) <= 2.35f);
                    if (captured)
                    {
                        BeginSqueezingPlayer(p2);
                    }
                    else if (_stateTimer >= 0.45f)
                    {
                        _constrictPhase = ConstrictPhase.RecoverMiss;
                        _stateTimer = 0f;
                        if (health != null)
                        {
                            health.SetHudVisible(true, p2 ? "FASE II: COROADA EM BILE LITURGICA" : "PARASITA LITURGICO DO PILORO");
                        }
                    }
                    break;

                case ConstrictPhase.Squeezing:
                    if (playerTarget == null || (_playerController != null && _playerController.IsDead))
                    {
                        EndConstrictAndContinue(false);
                        return;
                    }

                    // Mantem o centro e pulsa o raio dos aneis apertando o jogador!
                    float pulseR = Mathf.Lerp(1.80f, 1.24f, 0.5f + 0.5f * Mathf.Sin(_stateTimer * 9.0f));
                    controller?.UpdateConstrictPose(_constrictTargetSpot, pulseR);
                    _playerController?.ApplyBossConstriction(_constrictTargetSpot, 0.22f, 0.45f);

                    _constrictTickTimer += dt;
                    if (_constrictTickTimer >= constrictSqueezeInterval)
                    {
                        _constrictTickTimer = 0f;
                        DealDirectDamageToPlayer(p2 ? constrictSqueezeDamageP2 : constrictSqueezeDamageP1);
                        audioController?.PlayConstrictPulse();
                    }

                    // Se o jogador atacar/golpear a Tenia ou atingir limiar de dano, quebra a constricao!
                    if ((_playerController != null && _playerController.IsAttacking && _stateTimer >= 0.35f)
                        || _constrictDamageTakenToBreak >= constrictEscapeDamageThreshold)
                    {
                        EndConstrictAndContinue(true);
                        return;
                    }

                    if (_stateTimer >= constrictDuration)
                    {
                        EndConstrictAndContinue(false);
                    }
                    break;

                case ConstrictPhase.RecoverMiss:
                    controller?.SetGroundCoilTightness(0.25f);
                    if (_stateTimer >= 0.65f)
                    {
                        TransitionToNextTacticalMode();
                    }
                    break;
            }
        }

        private void BeginSqueezingPlayer(bool p2)
        {
            _constrictPhase = ConstrictPhase.Squeezing;
            _stateTimer = 0f;
            _constrictTickTimer = 0.25f;
            _constrictDamageTakenToBreak = 0f;

            if (playerTarget != null)
            {
                _constrictTargetSpot = playerTarget.position;
                if (arenaBounds != null) _constrictTargetSpot = arenaBounds.ClampLandingPoint(_constrictTargetSpot, 1.8f);
            }

            controller?.SetStateConstrict(_constrictTargetSpot, 1.75f);
            _playerController?.ApplyBossConstriction(_constrictTargetSpot, 0.4f, 0.45f);
            audioController?.PlayConstrictGrab();

            if (health != null)
            {
                health.SetHudVisible(true, "PRESO NA CONSTRICAO! ATAQUE PARA ESCAPAR!");
            }
        }

        private void EndConstrictAndContinue(bool escapedByCounterAttack)
        {
            _playerController?.ReleaseBossConstriction();
            controller?.SetGroundCoilTightness(0.25f);
            controller?.SetStateGround();

            if (escapedByCounterAttack && _playerController != null)
            {
                Vector3 away = (_playerController.transform.position - _constrictTargetSpot);
                away.y = 0f;
                away = away.sqrMagnitude > 0.01f ? away.normalized : Vector3.back;
                _playerController.ApplyExternalKnockback(away * 1.15f);
            }

            if (health != null)
            {
                bool p2 = health.IsPhase2;
                health.SetHudVisible(true, p2 ? "FASE II: COROADA EM BILE LITURGICA" : "PARASITA LITURGICO DO PILORO");
            }

            BeginClimbingToPillar();
        }

        // =====================================================================
        // MODO 3: ARTILHARIA A DISTANCIA (Mantem Distancia + Enrola + Telegrafo)
        // =====================================================================

        private void EnterRangedArtillery()
        {
            ClearMeleeTelegraph();
            currentState = EncounterState.RangedArtillery;
            _stateTimer = 0f;
            _tooCloseTimer = 0f;
            bool p2 = health != null && health.IsPhase2;
            _rangedSalvosRemaining = p2 ? 3 : 2;
            _spitCooldownTimer = 0.85f;
            _orbitDirectionSign = Random.value > 0.5f ? 1f : -1f;

            if (health != null)
            {
                health.SetHudVisible(true, p2 ? "MODO ARTILHARIA CORROSIVA (FASE II)" : "MODO ARTILHARIA: MANTENDO DISTANCIA");
            }
            if (controller != null)
            {
                controller.slitherSpeed = p2 ? _baseSlitherSpeed * 1.25f : _baseSlitherSpeed;
                controller.SetGroundCoilTightness(0.85f);
                controller.SetAimTarget(playerTarget);
                if (controller.poseState != TeniaBossController.BossPoseState.GroundSlither)
                {
                    controller.SetStateGround();
                }
            }
        }

        private void TickRangedArtillery(float dt)
        {
            _stateTimer += dt;
            bool p2 = health != null && health.IsPhase2;

            if (playerTarget != null)
            {
                Vector3 toPlayer = playerTarget.position - transform.position;
                toPlayer.y = 0f;
                float dist = toPlayer.magnitude;
                Vector3 dirToPlayer = dist > 0.05f ? toPlayer / dist : transform.forward;
                Vector3 lat = new Vector3(-dirToPlayer.z, 0f, dirToPlayer.x) * _orbitDirectionSign;

                // Mantem distancia! Se jogador esta perto demais (< preferredRangedDistance), foge / circula longe!
                if (dist < preferredRangedDistance)
                {
                    Vector3 retreatDir = (-dirToPlayer * 0.85f + lat * 0.55f).normalized;
                    if (arenaBounds != null)
                    {
                        retreatDir = arenaBounds.SteerInsideNave(transform.position, retreatDir, groundWallMargin);
                    }
                    RotateRootToward(retreatDir, p2 ? turnSpeedDegP2 : turnSpeedDegP1, dt);
                    AdvanceGroundRoot((p2 ? rangedRepositionSpeedP2 : rangedRepositionSpeedP1) * dt);
                    controller?.SetGroundCoilTightness(0.45f);
                }
                else
                {
                    // A boa distancia: enrola o corpo em espiral defensiva circulando lentamente olhando o alvo
                    Vector3 coilCircleDir = (lat * 0.78f + dirToPlayer * 0.22f).normalized;
                    if (arenaBounds != null)
                    {
                        coilCircleDir = arenaBounds.SteerInsideNave(transform.position, coilCircleDir, groundWallMargin);
                    }
                    RotateRootToward(coilCircleDir, 95f, dt);
                    AdvanceGroundRoot(1.65f * dt);
                    controller?.SetGroundCoilTightness(0.95f);
                }

                if (dist < tooCloseToKeepRanged)
                {
                    _tooCloseTimer += dt;
                }
                else
                {
                    _tooCloseTimer = Mathf.Max(0f, _tooCloseTimer - dt * 0.5f);
                }

                _spitCooldownTimer -= dt;
                if (_spitCooldownTimer <= 0f && _rangedSalvosRemaining > 0)
                {
                    _rangedSalvosRemaining--;
                    _spitCooldownTimer = p2 ? rangedVolleyIntervalP2 : rangedVolleyIntervalP1;
                    FireAcidVolley(p2 ? 3 : 2, p2, 4.6f);
                }
            }

            if ((_rangedSalvosRemaining <= 0 && _spitCooldownTimer <= 0.4f) || _tooCloseTimer >= 1.8f || _stateTimer >= 9.0f)
            {
                BeginClimbingToPillar();
            }
        }

        // =====================================================================
        // MODO 4: PILARES (Escalada & Descida Fluidas sem Teletransporte)
        // =====================================================================

        private void BeginClimbingToPillar()
        {
            ClearMeleeTelegraph();
            currentState = EncounterState.ClimbingToPillar;
            _stateTimer = 0f;
            _isApproachingPillarBase = true;
            _didPillarAirHopThisPerch = false;
            _pillarStaggerAccumDmg = 0f;

            Vector3 playerPos = playerTarget != null ? playerTarget.position : (arenaBounds != null ? arenaBounds.arenaCenter : transform.position);
            _targetPillarPos = arenaBounds != null
                ? arenaBounds.SelectBestPillar(playerPos, transform.position, _lastPillarPos, 14.5f)
                : transform.position;
            _lastPillarPos = _targetPillarPos;

            Vector3 centerRef = arenaBounds != null ? arenaBounds.arenaCenter : transform.position;
            Vector3 inwardDir = new Vector3(centerRef.x - _targetPillarPos.x, 0f, centerRef.z - _targetPillarPos.z);
            inwardDir = inwardDir.sqrMagnitude > 0.01f ? inwardDir.normalized : Vector3.right;

            _pillarApproachPoint = _targetPillarPos + inwardDir * 2.25f;
            if (arenaBounds != null)
            {
                _pillarApproachPoint = arenaBounds.ClampPointToNave(_pillarApproachPoint, 1.0f);
                _pillarApproachPoint.y = arenaBounds.floorY;
            }

            controller?.SetGroundCoilTightness(0.15f);
        }

        private void TickClimbingToPillar(float dt)
        {
            _stateTimer += dt;
            bool p2 = health != null && health.IsPhase2;

            if (_isApproachingPillarBase)
            {
                Vector3 toApproach = _pillarApproachPoint - transform.position;
                toApproach.y = 0f;
                float dist = toApproach.magnitude;

                if (dist > 0.35f)
                {
                    Vector3 dir = toApproach / dist;
                    if (arenaBounds != null)
                    {
                        dir = arenaBounds.SteerInsideNave(transform.position, dir, 1.0f);
                    }
                    RotateRootToward(dir, (p2 ? turnSpeedDegP2 : turnSpeedDegP1) * 1.35f, dt);
                    AdvanceGroundRoot((p2 ? climbMoveSpeedP2 : climbMoveSpeedP1) * dt);
                }

                EvaluateContactBodyDamage(p2);

                if (dist <= 2.35f || _stateTimer >= 3.5f)
                {
                    _isApproachingPillarBase = false;
                    _stateTimer = 0f;
                    if (controller != null)
                    {
                        float radius = arenaBounds != null ? arenaBounds.pillarRadius : 0.98f;
                        controller.SetAimTarget(playerTarget);
                        controller.SetStatePillar(_targetPillarPos, radius);
                    }
                    audioController?.PlayClimbScrape();
                    if (health != null)
                    {
                        health.SetHudVisible(true, p2 ? "ESCALANDO PILAR (FASE II)" : "ESCALANDO PILAR DA CATEDRAL");
                    }
                }
            }
            else
            {
                // Aguarda a animacao continua de subida segmento-por-segmento concluir antes de atirar!
                if (controller == null || !controller.IsTransitioningPillar || _stateTimer >= 2.8f)
                {
                    EnterPillarPerched(p2);
                }
            }
        }

        private void EnterPillarPerched(bool p2)
        {
            currentState = EncounterState.PillarPerched;
            _stateTimer = 0f;
            _pillarStaggerAccumDmg = 0f;
            _volleysRemainingOnPillar = _didPillarAirHopThisPerch
                ? 2
                : (p2 ? pillarVolleysPerPerchP2 : pillarVolleysPerPerchP1);
            _spitCooldownTimer = 0.45f;

            if (health != null)
            {
                health.SetHudVisible(true, p2 ? "CHUVA DE BILE DO PILAR (FASE II)" : "EMPOLERADA: CHUVA DE ACIDO");
            }
        }

        private void TickPillarPerched(float dt)
        {
            _stateTimer += dt;
            bool p2 = health != null && health.IsPhase2;

            _spitCooldownTimer -= dt;
            if (_spitCooldownTimer <= 0f && _volleysRemainingOnPillar > 0)
            {
                _volleysRemainingOnPillar--;
                _spitCooldownTimer = p2 ? pillarVolleyIntervalP2 : pillarVolleyIntervalP1;

                int shotCount = p2 ? (_volleysRemainingOnPillar == 0 ? 3 : 2) : (_volleysRemainingOnPillar == 0 ? 2 : 1);
                FireAcidVolley(shotCount, p2, 6.4f);
            }

            if (_volleysRemainingOnPillar <= 0 && _stateTimer >= 1.2f && _spitCooldownTimer <= (p2 ? 0.25f : 0.45f))
            {
                if (p2 && !_didPillarAirHopThisPerch && TryBeginPillarAirHop())
                {
                    return;
                }
                if (TryBeginPillarLeap(p2))
                {
                    return;
                }
                BeginDescendingFromPillar();
            }
        }

        private bool TryBeginPillarAirHop()
        {
            if (arenaBounds == null || controller == null)
            {
                return false;
            }

            Vector3 oppositeSideRef = arenaBounds.arenaCenter + (arenaBounds.arenaCenter - _targetPillarPos).normalized * 9f;
            Vector3 nextPillar = arenaBounds.SelectBestPillar(oppositeSideRef, _targetPillarPos, _targetPillarPos, 6.5f);
            if (FlatDistance(nextPillar, _targetPillarPos) < 2.5f)
            {
                return false;
            }

            _didPillarAirHopThisPerch = true;
            _hopOriginPillarPos = _targetPillarPos;
            _targetPillarPos = nextPillar;
            _lastPillarPos = nextPillar;
            _hopTrailBlobsSpawned = 0;
            _pillarStaggerAccumDmg = 0f;

            currentState = EncounterState.PillarAirHop;
            _stateTimer = 0f;

            float radius = arenaBounds.pillarRadius;
            controller.SetAimTarget(playerTarget);
            controller.TriggerPillarToPillarLeap(nextPillar, radius, 1.08f, 6.2f);
            audioController?.PlayLeapWindup();

            if (health != null)
            {
                health.SetHudVisible(true, "FASE II: SALTO ENTRE PILARES!");
            }
            return true;
        }

        private void TickPillarAirHop(float dt)
        {
            _stateTimer += dt;
            float prog = controller != null ? controller.PillarCrawlProgress : 1f;

            while (_hopTrailBlobsSpawned < 3)
            {
                float threshold = 0.24f + _hopTrailBlobsSpawned * 0.25f;
                if (prog < threshold) break;

                float t = 0.22f + _hopTrailBlobsSpawned * 0.28f;
                Vector3 rawFloor = Vector3.Lerp(_hopOriginPillarPos, _targetPillarPos, t);
                rawFloor.y = arenaBounds != null ? arenaBounds.floorY : transform.position.y;
                Vector3 clampedFloor = ClampAcidTarget(rawFloor);
                Vector3 airPos = Vector3.Lerp(_hopOriginPillarPos, _targetPillarPos, t) + Vector3.up * 5.2f;
                LaunchSingleAcid(airPos, clampedFloor, true, 1.8f);
                _hopTrailBlobsSpawned++;
            }

            if (controller == null || !controller.IsAirHoppingPillar || _stateTimer >= 1.8f)
            {
                audioController?.PlayClimbScrape();
                EnterPillarPerched(true);
            }
        }

        private bool TryBeginPillarLeap(bool p2)
        {
            if (playerTarget == null || controller == null)
            {
                return false;
            }

            float distToPlayer = FlatDistance(_targetPillarPos, playerTarget.position);
            if (distToPlayer > pillarLeapMaxRange)
            {
                return false;
            }

            if (arenaBounds != null && (!arenaBounds.IsPlayerInsideCathedral(playerTarget.position) || !arenaBounds.IsInsideNave(playerTarget.position, -2.5f)))
            {
                return false;
            }

            ClearMeleeTelegraph();
            currentState = EncounterState.PillarLeap;
            _leapPhase = LeapPhase.Windup;
            _stateTimer = 0f;
            _biteOrConstrictDealt = false;
            _leapWindupDuration = p2 ? 0.64f : 0.80f;
            _leapImpactRadius = p2 ? pillarLeapImpactRadiusP2 : pillarLeapImpactRadiusP1;

            Vector3 predictedTarget = playerTarget.position + new Vector3(_playerEstVelocity.x, 0f, _playerEstVelocity.z) * (p2 ? 0.36f : 0.24f);
            Vector3 fromPillar = predictedTarget - _targetPillarPos;
            fromPillar.y = 0f;
            float flatDist = fromPillar.magnitude;
            if (flatDist < 2.2f)
            {
                Vector3 centerRef = arenaBounds != null ? arenaBounds.arenaCenter : transform.position;
                Vector3 toCenter = new Vector3(centerRef.x - _targetPillarPos.x, 0f, centerRef.z - _targetPillarPos.z);
                Vector3 fallbackDir = toCenter.sqrMagnitude > 0.01f ? toCenter.normalized : Vector3.right;
                predictedTarget = _targetPillarPos + fallbackDir * 3.4f;
            }
            else if (flatDist > pillarLeapMaxRange)
            {
                predictedTarget = _targetPillarPos + (fromPillar / flatDist) * pillarLeapMaxRange;
            }

            _leapTargetSpot = ClampBossBodyTarget(predictedTarget);
            _activeMeleeTelegraph = TeniaAcidTelegraph.Spawn(_leapTargetSpot, _leapImpactRadius, _leapWindupDuration, true);
            _pillarStaggerAccumDmg = 0f;
            audioController?.PlayLeapWindup();

            if (health != null)
            {
                health.SetHudVisible(true, p2 ? "SALTO ESMAGADOR DA BILE! ESQUIVE!" : "SALTO DO PILAR! SAIA DO CIRCULO!");
            }
            return true;
        }

        private void TickPillarLeap(float dt)
        {
            _stateTimer += dt;
            bool p2 = health != null && health.IsPhase2;

            switch (_leapPhase)
            {
                case LeapPhase.Windup:
                    float prep01 = Mathf.Clamp01(_stateTimer / Mathf.Max(0.1f, _leapWindupDuration));
                    controller?.SetPillarLeapPrep(prep01);
                    if (_stateTimer >= _leapWindupDuration)
                    {
                        _leapPhase = LeapPhase.Flying;
                        _stateTimer = 0f;
                        ClearMeleeTelegraph();
                        float flightDur = p2 ? 0.64f : 0.76f;
                        controller?.TriggerPillarLeap(_leapTargetSpot, flightDur, p2 ? 6.4f : 5.6f);
                    }
                    break;

                case LeapPhase.Flying:
                    // Aguarda animacao parabolica terminar no chao
                    if (controller == null || !controller.IsLeaping || _stateTimer >= 1.35f)
                    {
                        ExecutePillarLeapImpact(p2);
                    }
                    break;

                case LeapPhase.ImpactRecover:
                    if (_stateTimer >= (p2 ? 0.55f : 0.78f))
                    {
                        if (controller != null)
                        {
                            controller.headElevation = _baseHeadElevation;
                        }
                        if (p2 && _constrictGlobalCooldown <= 0f && playerTarget != null && FlatDistance(_leapTargetSpot, playerTarget.position) <= 6.0f)
                        {
                            EnterConstrictAttack();
                        }
                        else
                        {
                            EnterMeleeHunt();
                        }
                    }
                    break;
            }
        }

        private void ExecutePillarLeapImpact(bool p2)
        {
            _leapPhase = LeapPhase.ImpactRecover;
            _stateTimer = 0f;

            // Treme a camera isometrica fortemente no impacto!
            audioController?.PlayLeapSlam();
            _isoCamera?.TriggerShake(p2 ? 0.88f : 0.68f, 0.38f);

            // Abaixa levemente a cabeca por 0.6s criando visual de impacto + janela justa de contra-ataque
            if (controller != null)
            {
                controller.headElevation = Mathf.Max(1.35f, _baseHeadElevation - 1.15f);
            }

            if (playerTarget != null && (_playerController == null || !_playerController.IsDead))
            {
                float dist = FlatDistance(playerTarget.position, _leapTargetSpot);
                if (dist <= _leapImpactRadius)
                {
                    DealDirectDamageToPlayer(p2 ? pillarLeapDamageP2 : pillarLeapDamageP1);
                    if (_playerController != null)
                    {
                        Vector3 push = playerTarget.position - _leapTargetSpot;
                        push.y = 0f;
                        if (push.sqrMagnitude < 0.01f) push = transform.forward;
                        _playerController.ApplyExternalKnockback(push.normalized * 2.55f);
                    }
                }
            }

            // Poca acida de impacto + onda visual de choque
            TeniaAcidPuddle.Spawn(
                _leapTargetSpot,
                _leapImpactRadius * 0.62f,
                p2 ? puddleDuration * 1.1f : puddleDuration * 0.85f,
                p2 ? puddleDps * 1.15f : puddleDps,
                transform);

            SpawnLeapShockwaveVfx(_leapTargetSpot, _leapImpactRadius, p2);

            if (p2)
            {
                // Fase 2 ainda cospe 2 respingos laterais apos esmagar o chao
                for (int i = 0; i < 2; i++)
                {
                    float ang = (i == 0 ? 1f : -1f) * 0.9f + Random.Range(-0.25f, 0.25f);
                    Vector3 offset = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * (_leapImpactRadius + 2.1f);
                    LaunchSingleAcid(_leapTargetSpot + Vector3.up * 1.4f, ClampAcidTarget(_leapTargetSpot + offset), true, 3.2f);
                }
            }
        }

        private void SpawnLeapShockwaveVfx(Vector3 center, float radius, bool p2)
        {
            GameObject fxObj = new GameObject("TeniaLeapShockwaveFX");
            fxObj.transform.position = center + Vector3.up * 0.14f;
            ParticleSystem ps = fxObj.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.loop = false;
            main.playOnAwake = false;
            main.duration = 0.7f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.32f, 0.62f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(radius * 1.25f, radius * 2.25f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.32f, 0.75f);
            main.gravityModifier = 0.85f;
            main.startColor = p2
                ? new ParticleSystem.MinMaxGradient(new Color(0.75f, 1.0f, 0.16f, 0.95f), new Color(1.0f, 0.25f, 0.12f, 0.9f))
                : new ParticleSystem.MinMaxGradient(new Color(0.45f, 1.0f, 0.18f, 0.95f), new Color(0.18f, 0.75f, 0.12f, 0.85f));

            var emission = ps.emission;
            emission.enabled = false;
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 0.45f;
            shape.rotation = new Vector3(-90f, 0f, 0f);

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            if (renderer != null)
            {
                Shader sh = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Particles/Standard Unlit");
                if (sh != null)
                {
                    Material mat = new Material(sh);
                    mat.color = p2 ? new Color(0.85f, 1f, 0.2f, 0.9f) : new Color(0.45f, 1f, 0.2f, 0.9f);
                    renderer.sharedMaterial = mat;
                }
            }

            ps.Emit(p2 ? 42 : 30);
            Destroy(fxObj, 1.4f);
        }

        private void BeginDescendingFromPillar()
        {
            currentState = EncounterState.DescendingFromPillar;
            _stateTimer = 0f;
            controller?.SetStateGround(); // Inicia descida continua pela espiral sem teletransporte!
        }

        private void TickDescendingFromPillar(float dt)
        {
            _stateTimer += dt;
            if (controller == null || !controller.IsTransitioningPillar || _stateTimer >= 2.4f)
            {
                if (playerTarget != null && arenaBounds != null && !arenaBounds.IsInsideNave(playerTarget.position, -0.45f))
                {
                    EnterRangedArtillery();
                }
                else
                {
                    EnterMeleeHunt();
                }
            }
        }

        // =====================================================================
        // QUEBRA DE PILAR (STAGGERED) & ORBES DE CURA
        // =====================================================================

        public void BeginPillarStaggerFall()
        {
            if (currentState == EncounterState.Staggered || currentState == EncounterState.Defeated || currentState == EncounterState.Dormant)
            {
                return;
            }

            StopAllCoroutines();
            ClearMeleeTelegraph();
            _pillarStaggerAccumDmg = 0f;

            currentState = EncounterState.Staggered;
            _stateTimer = 0f;

            Vector3 centerRef = arenaBounds != null ? arenaBounds.arenaCenter : transform.position;
            Vector3 inward = new Vector3(centerRef.x - _targetPillarPos.x, 0f, centerRef.z - _targetPillarPos.z);
            inward = inward.sqrMagnitude > 0.01f ? inward.normalized : Vector3.right;
            Vector3 fallSpot = ClampBossBodyTarget(_targetPillarPos + inward * 3.8f);

            controller?.SetPillarLeapPrep(0f);
            controller?.TriggerStaggerFall(fallSpot, 0.56f);

            audioController?.PlayStaggerCrash();
            _isoCamera?.TriggerShake(0.75f, 0.35f);

            if (health != null)
            {
                health.damageTakenMultiplier = staggerDamageMultiplier;
                health.SetHudVisible(true, "PILAR QUEBRADO! TENIA ATORDOADA (+50% DANO)!");
            }

            Vector3 orbPos = ClampBossBodyTarget(fallSpot + inward * 2.1f);
            SpawnSoulOrbRespectingCap(orbPos);
        }

        private void TickStaggered(float dt)
        {
            _stateTimer += dt;
            if (controller != null)
            {
                controller.slitherSpeed = 0f;
                if (!controller.IsFallingStagger)
                {
                    controller.headElevation = Mathf.MoveTowards(controller.headElevation, 0.95f, dt * 4.5f);
                }
            }

            if (_stateTimer >= pillarStaggerDuration)
            {
                if (health != null)
                {
                    health.damageTakenMultiplier = 1f;
                }
                if (controller != null)
                {
                    controller.headElevation = _baseHeadElevation;
                }
                EnterMeleeHunt();
            }
        }

        private void TickArenaSoulOrbSpawner(float dt)
        {
            if (arenaBounds == null) return;
            _orbRespawnTimer -= dt;
            if (_orbRespawnTimer > 0f) return;

            _orbRespawnTimer = soulOrbRespawnInterval;
            if (TeniaSoulOrb.ActiveOrbs.Count >= maxActiveSoulOrbs) return;

            Vector3 anchor = GetNaveOrbAnchor(_orbSpawnAnchorIdx++);
            SpawnSoulOrbRespectingCap(anchor);
        }

        private Vector3 GetNaveOrbAnchor(int index)
        {
            if (arenaBounds == null) return transform.position;
            bool left = (index % 2) == 0;
            float xOff = left ? -4.2f : 4.2f;
            float zOff = left ? -6.5f : 6.5f;
            Vector3 worldRaw = arenaBounds.cathedralAnchor != null
                ? arenaBounds.cathedralAnchor.TransformPoint(new Vector3(xOff, 0f, 22.0f + zOff))
                : new Vector3(arenaBounds.arenaCenter.x + xOff, arenaBounds.floorY, arenaBounds.arenaCenter.z + zOff);
            return arenaBounds.ClampLandingPoint(worldRaw, 1.5f);
        }

        private void SpawnSoulOrbRespectingCap(Vector3 floorPos)
        {
            if (TeniaSoulOrb.ActiveOrbs.Count >= maxActiveSoulOrbs) return;
            TeniaSoulOrb.Spawn(floorPos, soulOrbHealAmount, soulOrbRespawnInterval, audioController);
        }

        private void TickPhase2Transition(float dt)
        {
            _stateTimer += dt;
            if (playerTarget != null)
            {
                RotateRootToward(playerTarget.position - transform.position, 220f, dt);
            }

            if (_stateTimer >= 0.90f)
            {
                _isoCamera?.TriggerShake(0.55f, 0.30f);
                FireAcidVolley(4, true, 5.6f);
                BeginClimbingToPillar();
            }
        }

        private void TickDefeated(float dt)
        {
            _stateTimer += dt;

            if (_stateTimer < DeathMeltStartSeconds && _stateTimer >= _defeatNextGroanTime && _defeatNextGroanTime < 4.5f)
            {
                audioController?.PlayLowDeathGroan();
                _defeatNextGroanTime += 1.65f;
            }

            if (!_defeatAcidMeltTriggered && _stateTimer >= DeathMeltStartSeconds)
            {
                _defeatAcidMeltTriggered = true;
                audioController?.PlayAcidMeltDissolve();

                if (controller != null)
                {
                    controller.PopulateDeathAcidPositions(_defeatAcidSpots);
                    for (int i = 0; i < _defeatAcidSpots.Count; i++)
                    {
                        float poolRadius = i == 0 ? 2.55f : 2.10f;
                        TeniaAcidPuddle.Spawn(_defeatAcidSpots[i], poolRadius, 5.2f, 0f, transform);
                    }
                }
            }

            controller?.StepDeadCollapseAndMelt(_stateTimer, DeathMeltStartSeconds, DeathMeltDurationSeconds);

            if (_stateTimer >= DeathMeltStartSeconds && !_defeatCorpseHidden)
            {
                float melt01 = Mathf.Clamp01((_stateTimer - DeathMeltStartSeconds) / DeathMeltDurationSeconds);
                health?.ApplyDeathMeltTint(melt01);
            }

            if (!_defeatCorpseHidden && _stateTimer >= (DeathMeltStartSeconds + DeathMeltDurationSeconds))
            {
                _defeatCorpseHidden = true;
                controller?.SetVisualsVisible(false);
            }
        }

        // =====================================================================
        // HELPERS DE MOVIMENTO, DANO & ACIDO
        // =====================================================================

        private void AdvanceGroundRoot(float stepDistance)
        {
            Vector3 fwdXZ = new Vector3(transform.forward.x, 0f, transform.forward.z);
            if (fwdXZ.sqrMagnitude < 0.0001f) fwdXZ = Vector3.forward;
            fwdXZ.Normalize();

            Vector3 nextPos = transform.position + fwdXZ * stepDistance;
            if (arenaBounds != null)
            {
                nextPos = arenaBounds.ClampPointToNave(nextPos, groundWallMargin);
                nextPos.y = arenaBounds.floorY;
            }
            transform.position = nextPos;
        }

        private void RotateRootToward(Vector3 worldDirXZ, float degPerSec, float dt)
        {
            Vector3 flat = new Vector3(worldDirXZ.x, 0f, worldDirXZ.z);
            if (flat.sqrMagnitude < 0.0001f) return;

            Quaternion targetRot = Quaternion.LookRotation(flat.normalized, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, degPerSec * dt);
        }

        private void EvaluateContactBodyDamage(bool p2)
        {
            if (_contactCooldownTimer > 0f || _playerDamageable == null || playerTarget == null) return;
            if (_playerController != null && _playerController.IsDead) return;

            Vector3 pPos = playerTarget.position;
            float dmg = p2 ? contactBodyDamageP2 : contactBodyDamageP1;

            if (controller != null && controller.bodySegments != null)
            {
                for (int i = 0; i < controller.bodySegments.Count; i += 2)
                {
                    Transform seg = controller.bodySegments[i];
                    if (seg == null) continue;
                    if (FlatDistance(pPos, seg.position) <= contactBodyRadius && Mathf.Abs(pPos.y - seg.position.y) <= 1.9f)
                    {
                        DealDirectDamageToPlayer(dmg);
                        return;
                    }
                }
            }
        }

        private void DealDirectDamageToPlayer(float dmg)
        {
            _contactCooldownTimer = contactCooldown;
            _playerDamageable?.TakeAcidDamage(dmg);
        }

        private void FireAcidVolley(int count, bool p2, float arcHeight)
        {
            if (controller == null) return;

            Vector3 spawnPos = controller.GetAcidProjectileSpawnPos();
            Vector3 baseTarget = playerTarget != null
                ? playerTarget.position + new Vector3(_playerEstVelocity.x, 0f, _playerEstVelocity.z) * (p2 ? 0.50f : 0.34f)
                : (arenaBounds != null ? arenaBounds.arenaCenter : transform.position + transform.forward * 6f);

            if (count <= 1)
            {
                LaunchSingleAcid(spawnPos, ClampAcidTarget(baseTarget), p2, arcHeight);
            }
            else
            {
                StartCoroutine(FireStaggeredBarrageRoutine(count, baseTarget, p2, arcHeight));
            }
        }

        private IEnumerator FireStaggeredBarrageRoutine(int count, Vector3 baseTarget, bool p2, float arcHeight)
        {
            Vector3 right = Vector3.Cross(Vector3.up, (baseTarget - transform.position).normalized);
            if (right.sqrMagnitude < 0.01f) right = Vector3.right;
            right.Normalize();
            Vector3 fwd = new Vector3(-right.z, 0f, right.x);

            for (int i = 0; i < count; i++)
            {
                if (currentState == EncounterState.Defeated || currentState == EncounterState.Dormant)
                {
                    yield break;
                }

                Vector3 offset = Vector3.zero;
                if (i == 1) offset = right * 3.1f + fwd * 1.4f;
                else if (i == 2) offset = -right * 3.1f + fwd * 1.4f;
                else if (i > 2) offset = new Vector3(Random.Range(-3.5f, 3.5f), 0f, Random.Range(-3.5f, 3.5f));

                Vector3 spawnPos = controller != null ? controller.GetAcidProjectileSpawnPos() : transform.position + Vector3.up * 3f;
                Vector3 liveTarget = i == 0 && playerTarget != null
                    ? playerTarget.position + new Vector3(_playerEstVelocity.x, 0f, _playerEstVelocity.z) * 0.40f
                    : baseTarget;

                LaunchSingleAcid(spawnPos, ClampAcidTarget(liveTarget + offset), p2, arcHeight + i * 0.35f);

                if (i + 1 < count)
                {
                    yield return new WaitForSeconds(0.18f);
                }
            }
        }

        private void LaunchSingleAcid(Vector3 spawnPos, Vector3 groundTargetPos, bool p2, float arcHeight)
        {
            audioController?.PlayAcidSpit();
            float flightDuration = p2 ? spitTelegraphDurationP2 : spitTelegraphDurationP1;
            float impactRadius = p2 ? acidImpactRadiusP2 : acidImpactRadiusP1;
            float impactDmg = p2 ? acidImpactDamageP2 : acidImpactDamageP1;

            TeniaAcidProjectile.Launch(
                spawnPos,
                groundTargetPos,
                flightDuration,
                arcHeight,
                impactRadius,
                impactDmg,
                p2 ? puddleRadius * 1.12f : puddleRadius,
                p2 ? puddleDuration * 1.15f : puddleDuration,
                p2 ? puddleDps * 1.2f : puddleDps,
                transform,
                p2
            );
        }

        private Vector3 ClampBossBodyTarget(Vector3 rawTarget)
        {
            if (arenaBounds != null)
            {
                return arenaBounds.ClampLandingPoint(rawTarget, 1.35f);
            }
            rawTarget.y = transform.position.y;
            return rawTarget;
        }

        private Vector3 ClampAcidTarget(Vector3 rawTarget)
        {
            if (arenaBounds != null)
            {
                return arenaBounds.ClampAcidPointToArena(rawTarget, 0.65f);
            }
            rawTarget.y = transform.position.y;
            return rawTarget;
        }

        private void HandleBossDamaged(float dmg, Vector3 hitPos, bool isWeakSpot)
        {
            if (currentState == EncounterState.Dormant && health != null && !health.IsDead)
            {
                BeginIntroRoar();
                return;
            }

            if (currentState == EncounterState.ConstrictAttack && _constrictPhase == ConstrictPhase.Squeezing)
            {
                _constrictDamageTakenToBreak += dmg;
            }
            else if (currentState == EncounterState.PillarLeap && _leapPhase == LeapPhase.Windup)
            {
                _pillarStaggerAccumDmg += dmg;
                if (isWeakSpot || _pillarStaggerAccumDmg >= pillarLeapWindupStaggerThreshold)
                {
                    BeginPillarStaggerFall();
                }
            }
            else if (currentState == EncounterState.PillarPerched)
            {
                _pillarStaggerAccumDmg += isWeakSpot ? dmg * 1.45f : dmg;
                if (_pillarStaggerAccumDmg >= pillarPerchedStaggerThreshold)
                {
                    BeginPillarStaggerFall();
                }
            }
        }

        private void HandlePhase2Entered()
        {
            if (currentState == EncounterState.Defeated) return;
            StopAllCoroutines();
            ClearMeleeTelegraph();
            _playerController?.ReleaseBossConstriction();

            currentState = EncounterState.Phase2Transition;
            _stateTimer = 0f;
            _pillarStaggerAccumDmg = 0f;
            if (health != null)
            {
                health.damageTakenMultiplier = 1f;
            }

            audioController?.SetEncounterAmbience(true, true);
            audioController?.PlayRoar(true);
            SpawnSoulOrbRespectingCap(GetNaveOrbAnchor(0));
            SpawnSoulOrbRespectingCap(GetNaveOrbAnchor(1));

            if (controller != null && controller.poseState != TeniaBossController.BossPoseState.GroundSlither)
            {
                controller.SetStateGround();
            }
            if (health != null)
            {
                health.SetHudVisible(true, "FASE II: COROADA EM BILE LITURGICA");
            }
            if (controller != null)
            {
                controller.slitherSpeed = _baseSlitherSpeed * 1.42f;
            }
        }

        private void HandleBossDefeated()
        {
            StopAllCoroutines();
            ClearMeleeTelegraph();
            TeniaSoulOrb.ClearAllActiveOrbs();
            _playerController?.ReleaseBossConstriction();
            audioController?.SetEncounterAmbience(false, false);

            currentState = EncounterState.Defeated;
            _stateTimer = 0f;
            _defeatAcidMeltTriggered = false;
            _defeatCorpseHidden = false;
            _defeatNextGroanTime = 0.35f;
            if (health != null)
            {
                health.damageTakenMultiplier = 1f;
            }

            if (_isoCamera != null)
            {
                _isoCamera.SetBossZoomActive(false);
            }
            if (arenaBounds != null)
            {
                arenaBounds.SetPortalBarrierActive(false);
            }

            if (controller != null)
            {
                if (controller.poseState != TeniaBossController.BossPoseState.GroundSlither)
                {
                    controller.SetPoseImmediateGround();
                }
                controller.SetDeadFrozen(true);
            }

            TeniaHitboxRelay[] relays = GetComponentsInChildren<TeniaHitboxRelay>(true);
            for (int i = 0; i < relays.Length; i++)
            {
                if (relays[i] != null && relays[i].TryGetComponent<Collider>(out var col))
                {
                    col.enabled = false;
                }
            }

            if (health != null)
            {
                health.ShowVictoryBanner(6.5f);
            }
        }

        private void ClearMeleeTelegraph()
        {
            if (_activeMeleeTelegraph != null)
            {
                _activeMeleeTelegraph.CompleteAndDestroy();
                _activeMeleeTelegraph = null;
            }
        }

        private static float FlatDistance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        private void ResolvePlayerReference()
        {
            if (playerTarget == null)
            {
                GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
                if (playerObj == null) playerObj = GameObject.Find("Test_Player");
                if (playerObj != null)
                {
                    playerTarget = playerObj.transform;
                }
            }

            if (playerTarget != null && (_playerController == null || _playerDamageable == null))
            {
                _playerController = playerTarget.GetComponent<SimplePlayerController>();
                _playerDamageable = playerTarget.GetComponent<IAcidDamageable>();
                if (_playerDamageable == null) _playerDamageable = playerTarget.GetComponentInParent<IAcidDamageable>();
            }

            if (_isoCamera == null)
            {
                _isoCamera = FindAnyObjectByType<IsometricCameraFollow>();
            }
        }

        private void TrackPlayerVelocity(float dt)
        {
            if (playerTarget == null) return;
            if (_lastPlayerPos == Vector3.zero)
            {
                _lastPlayerPos = playerTarget.position;
                return;
            }

            Vector3 instantVel = (playerTarget.position - _lastPlayerPos) / Mathf.Max(0.001f, dt);
            instantVel.y = 0f;
            _playerEstVelocity = Vector3.Lerp(_playerEstVelocity, Vector3.ClampMagnitude(instantVel, 11f), Mathf.Clamp01(dt * 8f));
            _lastPlayerPos = playerTarget.position;
        }

        private bool ShouldResetBecausePlayerDiedOutside()
        {
            if (playerTarget == null || arenaBounds == null) return false;
            if (_playerController != null && _playerController.IsDead) return false;
            return arenaBounds.IsPlayerFarOutsideEntrance(playerTarget.position);
        }
    }
}
