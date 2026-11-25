using BossTenia;
using UnityEngine;

namespace ImmuneEnemies
{
    /// <summary>
    /// Eosinofilo da Bilis: celula imune voadora antiparasitaria que flutua sobre plataformas/bordas,
    /// cospe granulos acidos com telegrafo e explode em toxina ao morrer.
    /// </summary>
    public class EosinophilEnemyAI : ImmuneEnemyBase
    {
        private enum State
        {
            HoverPatrol,
            Skirmish,
            SpitWindup
        }

        [Header("Voo & Combate - Eosinofilo da Bilis")]
        public float moveSpeed = 4.1f;
        public float hoverHeight = 1.25f;
        [Tooltip("Altura maxima acima do chao para garantir alcance de ataques corpo a corpo.")]
        public float maxHoverHeight = 1.35f;
        [Tooltip("Altura maxima acima do jogador durante o combate.")]
        public float maxHeightAbovePlayer = 1.45f;
        public float preferredDistance = 8.8f;
        public float spitCooldown = 2.5f;
        public float spitWindupDuration = 0.56f;
        public float spitImpactDamage = 15f;
        public float spitImpactRadius = 1.75f;
        public float deathExplosionRadius = 2.15f;
        public float deathExplosionDamage = 18f;

        [Header("Referencias de Rig Procedural")]
        public Transform hoverBody;
        public Transform wingLeft;
        public Transform wingRight;
        public Transform mandibleCluster;
        public Transform[] granules;
        public Light glowLight;

        private State _state = State.HoverPatrol;
        private float _stateTimer;
        private float _spitCooldownTimer;
        private float _orbitSign;
        private float _wingPhase;
        private float _buzzTimer;
        private float _baseAnchorY;
        private bool _deathExplosionDone;
        private TeniaAcidTelegraph _deathTelegraph;
        private float[] _granuleBaseScales;

        protected override void Awake()
        {
            enemyDisplayName = "Eosinofilo da Bilis";
            if (Mathf.Approximately(maxHealth, 120f)) maxHealth = 85f;
            if (Mathf.Approximately(aggroRange, 17f)) aggroRange = 20.5f;
            leashRadius = 30f;
            bloodParticleColor = new Color(0.95f, 0.42f, 0.14f, 1f);
            hitFlashColor = new Color(2.0f, 1.15f, 0.2f, 1f);
            base.Awake();

            hoverHeight = Mathf.Min(hoverHeight, maxHoverHeight);
            if (TrySampleWalkableGround(transform.position, out float startFloorY))
            {
                _baseAnchorY = startFloorY + hoverHeight;
                Vector3 pos = transform.position;
                pos.y = _baseAnchorY;
                transform.position = pos;
            }
            else
            {
                _baseAnchorY = AnchorPosition.y;
            }
            _orbitSign = Random.value > 0.5f ? 1f : -1f;
            _spitCooldownTimer = Random.Range(0.7f, 1.5f);
            _wingPhase = Random.Range(0f, Mathf.PI * 2f);

            if (granules != null && granules.Length > 0)
            {
                _granuleBaseScales = new float[granules.Length];
                for (int i = 0; i < granules.Length; i++)
                {
                    _granuleBaseScales[i] = granules[i] != null ? granules[i].localScale.x : 1f;
                }
            }
        }

        private void OnDisable()
        {
            if (_deathTelegraph != null)
            {
                _deathTelegraph.CompleteAndDestroy();
                _deathTelegraph = null;
            }
        }

        protected override void OnDamaged(float dmg)
        {
            if (_state == State.HoverPatrol && IsPlayerTargetAlive())
            {
                _state = State.Skirmish;
                _stateTimer = 0f;
            }
        }

        protected override void OnDeathTriggered()
        {
            _deathExplosionDone = false;
            Vector3 floorSpot = transform.position;
            if (TrySampleWalkableGround(floorSpot, out float fy))
            {
                floorSpot.y = fy;
            }
            else
            {
                floorSpot.y = _baseAnchorY - hoverHeight;
            }
            _deathTelegraph = TeniaAcidTelegraph.Spawn(floorSpot, deathExplosionRadius, 0.45f, true);
        }

        protected override void TickDeathVisuals(float dt)
        {
            float pulse = Mathf.Sin(Time.time * 42f) * 0.5f + 0.5f;
            SetOverrideEmissionTint(new Color(2.4f, 0.45f, 0.1f), pulse);

            if (hoverBody != null)
            {
                float swell = 1f + Mathf.Min(0.45f, dt * 10f);
                hoverBody.localScale = Vector3.Lerp(hoverBody.localScale, Vector3.one * 1.38f, dt * 6f);
            }

            if (!_deathExplosionDone && Time.deltaTime >= 0f)
            {
                // Reusa contador via localScale timer ou campo dedicado
            }

            TickDeathExplosionTimer(dt);
        }

        private float _deathFuse;
        private void TickDeathExplosionTimer(float dt)
        {
            _deathFuse += dt;
            if (!_deathExplosionDone && _deathFuse >= 0.45f)
            {
                _deathExplosionDone = true;
                if (_deathTelegraph != null)
                {
                    _deathTelegraph.CompleteAndDestroy();
                    _deathTelegraph = null;
                }

                ImmuneEnemyAudio.Play(AudioSrc, ImmuneEnemyAudio.ClipEosinophilSpit, 0.9f, 0.72f, 0.88f);
                SpawnSplashParticles(transform.position, new Color(0.55f, 1.0f, 0.18f), 26);

                if (IsPlayerTargetAlive() && FlatDistance(PlayerTarget.position, transform.position) <= deathExplosionRadius
                    && Mathf.Abs(PlayerTarget.position.y - transform.position.y) <= 4.0f)
                {
                    PlayerController?.TakeAcidDamage(deathExplosionDamage);
                }

                gameObject.SetActive(false);
            }
        }

        protected override void ApplyHorizontalStep(Vector3 deltaXZ)
        {
            // Eosinofilo flutua sobre beiradas respeitando o limite maximo de altura para ataques corpo a corpo
            Vector3 next = transform.position + new Vector3(deltaXZ.x, 0f, deltaXZ.z);
            float clampedHover = Mathf.Min(hoverHeight, maxHoverHeight);
            float targetY = _baseAnchorY;
            if (TrySampleWalkableGround(next, out float floorY))
            {
                targetY = floorY + clampedHover;
                _baseAnchorY = Mathf.Lerp(_baseAnchorY, targetY, Time.deltaTime * 3.5f);
            }
            if (IsPlayerTargetAlive())
            {
                targetY = Mathf.Min(targetY, PlayerTarget.position.y + maxHeightAbovePlayer);
            }
            next.y = Mathf.Lerp(transform.position.y, targetY, Time.deltaTime * 4.5f);
            transform.position = next;
        }

        protected override void TickEnemyBehavior(float dt)
        {
            _stateTimer += dt;
            if (_spitCooldownTimer > 0f) _spitCooldownTimer -= dt;

            _buzzTimer -= dt;
            if (_buzzTimer <= 0f)
            {
                _buzzTimer = Random.Range(0.75f, 1.35f);
                if (IsPlayerTargetAlive() && Vector3.Distance(transform.position, PlayerTarget.position) <= 18f)
                {
                    ImmuneEnemyAudio.Play(AudioSrc, ImmuneEnemyAudio.ClipEosinophilBuzz, 0.24f, 0.92f, 1.15f);
                }
            }

            switch (_state)
            {
                case State.HoverPatrol:
                    TickHoverPatrol(dt);
                    break;
                case State.Skirmish:
                    TickSkirmish(dt);
                    break;
                case State.SpitWindup:
                    TickSpitWindup(dt);
                    break;
            }

            AnimateWingsAndGranules(dt);
        }

        private void TickHoverPatrol(float dt)
        {
            if (IsPlayerTargetAlive() && Vector3.Distance(transform.position, PlayerTarget.position) <= aggroRange)
            {
                _state = State.Skirmish;
                _stateTimer = 0f;
                return;
            }

            float ang = Time.time * 0.7f + AnchorPosition.x;
            Vector3 desired = AnchorPosition + new Vector3(Mathf.Cos(ang) * 2.8f, 0f, Mathf.Sin(ang) * 2.8f);
            Vector3 delta = desired - transform.position;
            delta.y = 0f;
            if (delta.sqrMagnitude > 0.04f)
            {
                RotateTowardXZ(delta, 140f, dt);
                ApplyHorizontalStep(delta.normalized * (moveSpeed * 0.45f * dt));
            }
        }

        private void TickSkirmish(float dt)
        {
            if (!IsPlayerTargetAlive() || Vector3.Distance(AnchorPosition, transform.position) > leashRadius)
            {
                _state = State.HoverPatrol;
                return;
            }

            Vector3 toPlayer = PlayerTarget.position - transform.position;
            toPlayer.y = 0f;
            float dist = toPlayer.magnitude;
            Vector3 dir = dist > 0.05f ? toPlayer / dist : transform.forward;
            Vector3 lat = new Vector3(-dir.z, 0f, dir.x) * _orbitSign;

            RotateTowardXZ(dir, 240f, dt);

            Vector3 moveDir;
            if (dist < preferredDistance * 0.72f)
            {
                moveDir = (-dir * 0.8f + lat * 0.6f).normalized;
            }
            else if (dist > preferredDistance * 1.25f)
            {
                moveDir = (dir * 0.8f + lat * 0.45f).normalized;
            }
            else
            {
                moveDir = lat;
            }

            ApplyHorizontalStep(moveDir * (moveSpeed * dt));

            if (_spitCooldownTimer <= 0f && dist <= aggroRange * 1.05f)
            {
                _state = State.SpitWindup;
                _stateTimer = 0f;
                _orbitSign = Random.value > 0.5f ? 1f : -1f;
            }
        }

        private void TickSpitWindup(float dt)
        {
            if (IsPlayerTargetAlive())
            {
                RotateTowardXZ(PlayerTarget.position - transform.position, 280f, dt);
            }

            if (_stateTimer >= spitWindupDuration)
            {
                FireToxicGranule();
                _state = State.Skirmish;
                _stateTimer = 0f;
                _spitCooldownTimer = spitCooldown;
            }
        }

        private void FireToxicGranule()
        {
            if (!IsPlayerTargetAlive()) return;

            Vector3 spawnPos = mandibleCluster != null
                ? mandibleCluster.position
                : transform.position - Vector3.up * 0.35f + transform.forward * 0.45f;

            Vector3 targetFloor = PlayerTarget.position;
            if (TrySampleWalkableGround(targetFloor, out float floorY))
            {
                targetFloor.y = floorY;
            }

            ImmuneEnemyAudio.Play(AudioSrc, ImmuneEnemyAudio.ClipEosinophilSpit, 0.70f, 0.90f, 1.12f);

            TeniaAcidProjectile.Launch(
                spawnPos,
                targetFloor,
                0.96f,
                3.4f,
                spitImpactRadius,
                spitImpactDamage,
                1.55f,
                3.2f,
                7.0f,
                transform,
                false
            );
        }

        private void AnimateWingsAndGranules(float dt)
        {
            _wingPhase += dt * (_state == State.SpitWindup ? 26f : 16f);
            float flap = Mathf.Sin(_wingPhase) * 38f;
            float bob = Mathf.Sin(Time.time * 3.1f + _wingPhase * 0.05f) * 0.20f;

            if (hoverBody != null)
            {
                hoverBody.localPosition = new Vector3(0f, bob, 0f);
                hoverBody.localRotation = Quaternion.Euler(_state == State.SpitWindup ? 14f : 4f, 0f, 0f);
            }

            if (wingLeft != null) wingLeft.localRotation = Quaternion.Euler(0f, -12f, 18f + flap);
            if (wingRight != null) wingRight.localRotation = Quaternion.Euler(0f, 12f, -18f - flap);

            if (mandibleCluster != null)
            {
                float pinch = _state == State.SpitWindup ? 1.28f : (1f + 0.08f * Mathf.Sin(Time.time * 8f));
                mandibleCluster.localScale = new Vector3(pinch, 1f, pinch);
            }

            if (granules != null)
            {
                float windMul = _state == State.SpitWindup ? 1.32f : 1.0f;
                for (int i = 0; i < granules.Length; i++)
                {
                    if (granules[i] == null) continue;
                    float baseScale = (_granuleBaseScales != null && i < _granuleBaseScales.Length) ? _granuleBaseScales[i] : 1f;
                    float p = baseScale * windMul * (0.94f + 0.12f * Mathf.Sin(Time.time * 5.2f + i * 0.9f));
                    granules[i].localScale = Vector3.one * p;
                }
            }

            if (glowLight != null)
            {
                glowLight.intensity = _state == State.SpitWindup ? 1.25f : 0.52f;
            }
        }
    }
}
