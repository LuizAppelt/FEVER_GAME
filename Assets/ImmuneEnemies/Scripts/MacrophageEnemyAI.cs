using BossTenia;
using UnityEngine;

namespace ImmuneEnemies
{
    /// <summary>
    /// Abade Macrofago: tanque pesado e inchado que persegue o Virus Player pelas arenas,
    /// abre sua bocarra vertical no abdomen para tentar suga-lo/engoli-lo e deixa abertura nas costas.
    /// </summary>
    public class MacrophageEnemyAI : ImmuneEnemyBase
    {
        private enum State
        {
            PatrolIdle,
            Chase,
            ChompWindup,
            ChompLunge,
            VulnerableRecover
        }

        [Header("Movimento & Combate - Abade Macrofago")]
        public float moveSpeed = 2.75f;
        public float turnSpeedDeg = 185f;
        public float chompTriggerRange = 4.3f;
        public float chompWindupDuration = 0.72f;
        public float suctionPullRadius = 5.4f;
        public float suctionPullStrength = 3.6f;
        public float chompRadius = 2.35f;
        public float chompDamage = 24f;
        public float vulnerableBackMultiplier = 1.45f;

        [Header("Referencias de Rig Procedural")]
        public Transform bodyRoot;
        public Transform jawLeft;
        public Transform jawRight;
        public Transform armLeft;
        public Transform armRight;
        public Transform legLeft;
        public Transform legRight;
        public Transform digestedGutCore;
        public Light bellyLight;

        private State _state = State.PatrolIdle;
        private float _stateTimer;
        private float _attackCooldown;
        private float _walkCycle;
        private float _stompStepAccum;
        private float _jawOpenAngle;
        private Vector3 _patrolTarget;
        private float _patrolWaitTimer;
        private Vector3 _chompStrikeSpot;
        private TeniaAcidTelegraph _activeTelegraph;
        private bool _hitDealtThisLunge;

        protected override void Awake()
        {
            enemyDisplayName = "Abade Macrofago";
            if (Mathf.Approximately(maxHealth, 120f)) maxHealth = 190f;
            if (Mathf.Approximately(aggroRange, 17f)) aggroRange = 17.5f;
            leashRadius = 28f;
            dropMinorHealOrbOnDeath = true;
            minorHealAmount = 18f;
            bloodParticleColor = new Color(0.45f, 1.0f, 0.22f, 1f);
            base.Awake();

            _patrolTarget = AnchorPosition;
            _patrolWaitTimer = Random.Range(0.4f, 1.8f);
        }

        private void OnDisable()
        {
            ClearTelegraph();
        }

        protected override float GetIncomingDamageMultiplier()
        {
            if (_state == State.VulnerableRecover) return vulnerableBackMultiplier;
            if (PlayerTarget != null)
            {
                Vector3 toAttacker = PlayerTarget.position - transform.position;
                toAttacker.y = 0f;
                if (toAttacker.sqrMagnitude > 0.01f && Vector3.Dot(transform.forward, toAttacker.normalized) < -0.2f)
                {
                    return 1.35f; // Flanco/costas toma dano extra
                }
            }
            return 1f;
        }

        protected override bool IsDamageFlashCritical()
        {
            return _state == State.VulnerableRecover || GetIncomingDamageMultiplier() > 1.1f;
        }

        protected override void OnDamaged(float dmg)
        {
            if (_state == State.PatrolIdle && IsPlayerTargetAlive())
            {
                _state = State.Chase;
                _stateTimer = 0f;
            }
        }

        protected override void OnDeathTriggered()
        {
            ClearTelegraph();
        }

        protected override void TickEnemyBehavior(float dt)
        {
            _stateTimer += dt;
            if (_attackCooldown > 0f) _attackCooldown -= dt;

            bool moving = false;
            float speedRatio = 0f;

            switch (_state)
            {
                case State.PatrolIdle:
                    TickPatrol(dt, ref moving, ref speedRatio);
                    break;
                case State.Chase:
                    TickChase(dt, ref moving, ref speedRatio);
                    break;
                case State.ChompWindup:
                    TickChompWindup(dt);
                    break;
                case State.ChompLunge:
                    TickChompLunge(dt, ref moving, ref speedRatio);
                    break;
                case State.VulnerableRecover:
                    TickVulnerableRecover(dt);
                    break;
            }

            AnimateBody(dt, moving, speedRatio);
        }

        private void TickPatrol(float dt, ref bool moving, ref float speedRatio)
        {
            if (IsPlayerTargetAlive())
            {
                float dPlayer = Vector3.Distance(transform.position, PlayerTarget.position);
                if (dPlayer <= aggroRange && Mathf.Abs(PlayerTarget.position.y - transform.position.y) <= 5.5f)
                {
                    _state = State.Chase;
                    _stateTimer = 0f;
                    return;
                }
            }

            if (_patrolWaitTimer > 0f)
            {
                _patrolWaitTimer -= dt;
                return;
            }

            Vector3 toPt = _patrolTarget - transform.position;
            toPt.y = 0f;
            if (toPt.magnitude <= 0.65f)
            {
                Vector2 rnd = Random.insideUnitCircle * 4.5f;
                _patrolTarget = AnchorPosition + new Vector3(rnd.x, 0f, rnd.y);
                _patrolWaitTimer = Random.Range(1.4f, 2.8f);
                return;
            }

            RotateTowardXZ(toPt, turnSpeedDeg * 0.65f, dt);
            ApplyHorizontalStep(transform.forward * (moveSpeed * 0.48f * dt));
            moving = true;
            speedRatio = 0.5f;
        }

        private void TickChase(float dt, ref bool moving, ref float speedRatio)
        {
            if (!IsPlayerTargetAlive() || Vector3.Distance(AnchorPosition, transform.position) > leashRadius)
            {
                _patrolTarget = AnchorPosition;
                _state = State.PatrolIdle;
                _stateTimer = 0f;
                return;
            }

            Vector3 toPlayer = PlayerTarget.position - transform.position;
            toPlayer.y = 0f;
            float dist = toPlayer.magnitude;

            if (dist > aggroRange * 1.45f)
            {
                _patrolTarget = AnchorPosition;
                _state = State.PatrolIdle;
                return;
            }

            if (dist > 0.1f)
            {
                RotateTowardXZ(toPlayer, turnSpeedDeg, dt);
            }

            if (dist <= chompTriggerRange && _attackCooldown <= 0f)
            {
                EnterChompWindup();
                return;
            }

            if (dist > 1.85f)
            {
                ApplyHorizontalStep(transform.forward * (moveSpeed * dt));
                moving = true;
                speedRatio = 1f;
            }
        }

        private void EnterChompWindup()
        {
            _state = State.ChompWindup;
            _stateTimer = 0f;
            _hitDealtThisLunge = false;

            Vector3 dir = PlayerTarget != null ? (PlayerTarget.position - transform.position) : transform.forward;
            dir.y = 0f;
            dir = dir.sqrMagnitude > 0.01f ? dir.normalized : transform.forward;
            transform.rotation = Quaternion.LookRotation(dir, Vector3.up);

            _chompStrikeSpot = transform.position + dir * 2.15f;
            if (TrySampleWalkableGround(_chompStrikeSpot, out float floorY))
            {
                _chompStrikeSpot.y = floorY;
            }

            ClearTelegraph();
            _activeTelegraph = TeniaAcidTelegraph.Spawn(_chompStrikeSpot, chompRadius, chompWindupDuration, true);
            ImmuneEnemyAudio.Play(AudioSrc, ImmuneEnemyAudio.ClipMacrophageInhale, 0.72f, 0.90f, 1.05f);
        }

        private void TickChompWindup(float dt)
        {
            if (IsPlayerTargetAlive())
            {
                Vector3 toPlayer = PlayerTarget.position - transform.position;
                toPlayer.y = 0f;
                if (toPlayer.sqrMagnitude > 0.05f)
                {
                    RotateTowardXZ(toPlayer, turnSpeedDeg * 0.45f, dt);
                }

                // Sucção de fagocitose puxando levemente o virus para perto da boca
                float dist = toPlayer.magnitude;
                if (dist <= suctionPullRadius && dist > 1.25f && PlayerController != null && !PlayerController.IsDodging)
                {
                    Vector3 pull = -toPlayer.normalized * (suctionPullStrength * dt);
                    PlayerController.ApplyExternalKnockback(pull);
                }
            }

            float prep01 = Mathf.Clamp01(_stateTimer / Mathf.Max(0.1f, chompWindupDuration));
            _jawOpenAngle = Mathf.Lerp(0f, 44f, prep01);

            if (_stateTimer >= chompWindupDuration)
            {
                ClearTelegraph();
                _state = State.ChompLunge;
                _stateTimer = 0f;
                ImmuneEnemyAudio.Play(AudioSrc, ImmuneEnemyAudio.ClipMacrophageChomp, 0.82f, 0.92f, 1.08f);
            }
        }

        private void TickChompLunge(float dt, ref bool moving, ref float speedRatio)
        {
            float t = Mathf.Clamp01(_stateTimer / 0.28f);
            _jawOpenAngle = Mathf.Lerp(44f, 0f, t * t);

            ApplyHorizontalStep(transform.forward * (moveSpeed * 3.1f * dt));
            moving = true;
            speedRatio = 1.6f;

            if (!_hitDealtThisLunge && _stateTimer >= 0.08f && IsPlayerTargetAlive())
            {
                Vector3 jawWorld = transform.position + transform.forward * 1.45f;
                if (FlatDistance(PlayerTarget.position, jawWorld) <= chompRadius || FlatDistance(PlayerTarget.position, _chompStrikeSpot) <= chompRadius)
                {
                    _hitDealtThisLunge = true;
                    PlayerController?.TakeAcidDamage(chompDamage);
                    Vector3 push = PlayerTarget.position - transform.position;
                    push.y = 0f;
                    push = push.sqrMagnitude > 0.01f ? push.normalized : transform.forward;
                    PlayerController?.ApplyExternalKnockback(push * 2.1f);
                }
            }

            if (_stateTimer >= 0.28f)
            {
                _jawOpenAngle = 0f;
                _state = State.VulnerableRecover;
                _stateTimer = 0f;
                _attackCooldown = 1.85f;
                SpawnSplashParticles(transform.position + transform.forward * 1.4f + Vector3.up * 1.1f, bloodParticleColor, 9);
            }
        }

        private void TickVulnerableRecover(float dt)
        {
            if (_stateTimer >= 1.15f)
            {
                _state = State.Chase;
                _stateTimer = 0f;
            }
        }

        private void AnimateBody(float dt, bool moving, float speedRatio)
        {
            if (moving)
            {
                _walkCycle += dt * (6.2f * speedRatio);
                _stompStepAccum += dt * (2.6f * speedRatio);
                if (_stompStepAccum >= 1f)
                {
                    _stompStepAccum -= 1f;
                    ImmuneEnemyAudio.Play(AudioSrc, ImmuneEnemyAudio.ClipMacrophageStomp, 0.36f, 0.85f, 1.05f);
                }
            }
            else
            {
                _walkCycle = Mathf.Lerp(_walkCycle, Mathf.Round(_walkCycle / Mathf.PI) * Mathf.PI, dt * 4f);
            }

            float swing = moving ? Mathf.Sin(_walkCycle) : 0f;
            float breath = Mathf.Sin(Time.time * 2.6f);

            if (bodyRoot != null)
            {
                float hunch = _state == State.VulnerableRecover ? 16f : (_state == State.ChompWindup ? -12f : 4f);
                float roll = swing * 6.5f;
                bodyRoot.localRotation = Quaternion.Slerp(bodyRoot.localRotation, Quaternion.Euler(hunch, 0f, roll), dt * 10f);
                bodyRoot.localPosition = new Vector3(0f, Mathf.Abs(swing) * 0.08f + breath * 0.025f, 0f);
            }

            if (jawLeft != null) jawLeft.localRotation = Quaternion.Euler(0f, -_jawOpenAngle, 0f);
            if (jawRight != null) jawRight.localRotation = Quaternion.Euler(0f, _jawOpenAngle, 0f);

            float armRaise = _state == State.ChompWindup ? -55f : (_state == State.VulnerableRecover ? 18f : 0f);
            if (armLeft != null) armLeft.localRotation = Quaternion.Euler(armRaise - swing * 24f, 0f, 10f);
            if (armRight != null) armRight.localRotation = Quaternion.Euler(armRaise + swing * 24f, 0f, -10f);

            if (legLeft != null) legLeft.localRotation = Quaternion.Euler(swing * 22f, 0f, 0f);
            if (legRight != null) legRight.localRotation = Quaternion.Euler(-swing * 22f, 0f, 0f);

            if (digestedGutCore != null)
            {
                float pulse = 1f + 0.14f * Mathf.Sin(Time.time * 4.2f + (_state == State.ChompWindup ? 3f : 0f));
                digestedGutCore.localScale = Vector3.one * pulse;
            }

            if (bellyLight != null)
            {
                bellyLight.intensity = _state == State.ChompWindup ? 1.15f : (0.28f + 0.08f * breath);
            }
        }

        private void ClearTelegraph()
        {
            if (_activeTelegraph != null)
            {
                _activeTelegraph.CompleteAndDestroy();
                _activeTelegraph = null;
            }
        }
    }
}
