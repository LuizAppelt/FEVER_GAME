using System.Collections.Generic;
using StomachAbyss.Combat;
using UnityEngine;

namespace MapaEstomago
{
    /// <summary>
    /// Projetil de Bolha Acida do Chicote Mini Tenia:
    /// - Modo Rapido (isHeavyBossBubble = false): tiro rapido ate o ponto alvo, dano direto (~18) + splash acido.
    /// - Modo Pesado / Boss (isHeavyBossBubble = true): arco parabolico ate o solo alvo; ao impactar,
    ///   causa dano em area (~35) e cria uma Poca Acida Corrosiva (AcidGroundPool) por 3.0s (~3.0m de raio).
    /// - Constroi visuais PS1 automaticamente se nao houver prefab atribuido.
    /// </summary>
    public class AcidProjectile : MonoBehaviour
    {
        [Header("Modo e Dano")]
        public bool isHeavyBossBubble = false;
        public float smallDamage = 18f;
        public float heavyImpactDamage = 35f;
        public float heavyImpactRadius = 2.6f;

        [Header("Movimento")]
        public float smallProjectileSpeed = 24f;
        public float smallMaxLifetime = 1.8f;
        public float heavyFlightDuration = 0.62f;
        public float heavyArcHeight = 3.2f;

        [Header("Poca Acida (Ataque Pesado)")]
        public float poolRadius = 3.0f;
        public float poolDuration = 3.0f;
        public float poolDamagePerSecond = 16f;
        public float poolTickInterval = 0.30f;

        [Header("Materiais PS1 (Opcional)")]
        public Material acidMaterial;
        public Material pixelParticleMaterial;

        private GameObject _owner;
        private Vector3 _startPos;
        private Vector3 _targetPos;
        private Vector3 _velocity;
        private float _elapsed;
        private float _groundY;
        private bool _initialized;
        private bool _impacted;
        private Transform _visualSphere;
        private ParticleSystem _trailParticles;

        public static AcidProjectile Spawn(
            GameObject owner,
            Vector3 spawnPos,
            Vector3 targetPos,
            bool isHeavy,
            Material acidMat = null,
            Material particleMat = null)
        {
            GameObject go = new GameObject(isHeavy ? "AcidBubble_HeavyBoss" : "AcidBubble_Small");
            go.transform.position = spawnPos;

            AcidProjectile proj = go.AddComponent<AcidProjectile>();
            proj.acidMaterial = acidMat;
            proj.pixelParticleMaterial = particleMat;
            proj.Initialize(owner, spawnPos, targetPos, isHeavy);
            return proj;
        }

        public void Initialize(GameObject owner, Vector3 startPos, Vector3 targetPos, bool isHeavy)
        {
            _owner = owner;
            _startPos = startPos;
            _targetPos = targetPos;
            _groundY = targetPos.y;
            isHeavyBossBubble = isHeavy;
            _elapsed = 0f;
            _impacted = false;
            _initialized = true;

            if (!isHeavyBossBubble)
            {
                Vector3 dir = targetPos - startPos;
                dir.y = 0f;
                if (dir.sqrMagnitude < 0.001f)
                {
                    dir = owner != null ? owner.transform.forward : Vector3.forward;
                }
                dir.Normalize();
                _velocity = dir * smallProjectileSpeed;
                transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
            }
            else
            {
                Vector3 flatDir = targetPos - startPos;
                flatDir.y = 0f;
                if (flatDir.sqrMagnitude > 0.001f)
                {
                    transform.rotation = Quaternion.LookRotation(flatDir.normalized, Vector3.up);
                }
            }

            BuildVisuals();
            SetupTriggerCollider();
        }

        private void SetupTriggerCollider()
        {
            SphereCollider col = GetComponent<SphereCollider>();
            if (col == null) col = gameObject.AddComponent<SphereCollider>();
            col.isTrigger = true;
            col.radius = isHeavyBossBubble ? 0.52f : 0.28f;

            Rigidbody rb = GetComponent<Rigidbody>();
            if (rb == null) rb = gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
        }

        private void BuildVisuals()
        {
            if (_visualSphere != null) return;

            GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = "BubbleMesh";
            sphere.transform.SetParent(transform, false);
            Destroy(sphere.GetComponent<Collider>());

            float scale = isHeavyBossBubble ? 0.88f : 0.42f;
            sphere.transform.localScale = Vector3.one * scale;
            _visualSphere = sphere.transform;

            Renderer rend = sphere.GetComponent<Renderer>();
            if (rend != null)
            {
                if (acidMaterial != null)
                {
                    rend.sharedMaterial = acidMaterial;
                }
                else
                {
                    Shader s = Shader.Find("MapaEstomago/URP/PS1_GastricAcid");
                    if (s == null) s = Shader.Find("Universal Render Pipeline/Lit");
                    Material fallback = new Material(s);
                    fallback.color = new Color(0.32f, 0.96f, 0.22f, 0.92f);
                    rend.sharedMaterial = fallback;
                }
            }

            GameObject trailGo = new GameObject("AcidTrail");
            trailGo.transform.SetParent(transform, false);
            _trailParticles = trailGo.AddComponent<ParticleSystem>();

            var main = _trailParticles.main;
            main.loop = true;
            main.startLifetime = isHeavyBossBubble ? 0.38f : 0.22f;
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 1.4f);
            main.startSize = new ParticleSystem.MinMaxCurve(isHeavyBossBubble ? 0.20f : 0.11f, isHeavyBossBubble ? 0.34f : 0.18f);
            main.startColor = new Color(0.45f, 1.0f, 0.25f, 0.95f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var emission = _trailParticles.emission;
            emission.rateOverTime = isHeavyBossBubble ? 28f : 18f;

            var shape = _trailParticles.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = isHeavyBossBubble ? 0.32f : 0.12f;

            ParticleSystemRenderer psRend = trailGo.GetComponent<ParticleSystemRenderer>();
            if (psRend != null && pixelParticleMaterial != null)
            {
                psRend.sharedMaterial = pixelParticleMaterial;
            }
        }

        private void Update()
        {
            if (!_initialized || _impacted) return;

            float dt = Time.deltaTime;
            _elapsed += dt;

            if (_visualSphere != null)
            {
                float wobble = 1f + Mathf.Sin(Time.time * 18f) * 0.12f;
                float baseScale = isHeavyBossBubble ? 0.88f : 0.42f;
                _visualSphere.localScale = new Vector3(baseScale * wobble, baseScale * (2f - wobble), baseScale * wobble);
            }

            if (isHeavyBossBubble)
            {
                float t = Mathf.Clamp01(_elapsed / Mathf.Max(0.15f, heavyFlightDuration));
                Vector3 pos = Vector3.Lerp(_startPos, _targetPos, t);
                pos.y += Mathf.Sin(t * Mathf.PI) * heavyArcHeight;
                transform.position = pos;

                if (t >= 1f)
                {
                    ExplodeImpact(new Vector3(_targetPos.x, _groundY, _targetPos.z));
                }
            }
            else
            {
                transform.position += _velocity * dt;

                // Mantem o tiro rapido na altura aproximada do plano alvo
                if (_elapsed >= smallMaxLifetime)
                {
                    ExplodeImpact(transform.position);
                }
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!_initialized || _impacted || other == null || other.isTrigger) return;
            if (IsOwnerCollider(other)) return;

            // Ignora piso no inicio do voo da bolha pesada para nao explodir antes de completar o arco
            if (isHeavyBossBubble && _elapsed < heavyFlightDuration * 0.45f)
            {
                IAcidDamageable dmg = other.GetComponentInParent<IAcidDamageable>();
                if (dmg == null) return;
            }

            Vector3 hitPos = other.ClosestPoint(transform.position);
            if (isHeavyBossBubble)
            {
                hitPos.y = _groundY;
            }
            ExplodeImpact(hitPos);
        }

        private bool IsOwnerCollider(Collider col)
        {
            if (_owner != null && (col.gameObject == _owner || col.transform.IsChildOf(_owner.transform)))
            {
                return true;
            }
            return col.CompareTag("Player") || col.GetComponentInParent<SimplePlayerController>() != null;
        }

        private void ExplodeImpact(Vector3 impactPos)
        {
            if (_impacted) return;
            _impacted = true;

            if (isHeavyBossBubble)
            {
                Vector3 groundCenter = ResolveGroundPoint(impactPos);
                ApplyAoEDamage(groundCenter, heavyImpactRadius, heavyImpactDamage, isHeavy: true);
                SpawnAcidPool(groundCenter);
                SpawnSplashBurst(groundCenter, 32, 1.45f);
                CombatSfxBank.PlayAt(CombatSfxId.AcidPoolSplash, groundCenter, 0.92f, 0.94f, 1.06f, 0.45f, 0.04f);

                IsometricCameraFollow cam = FindFirstObjectByType<IsometricCameraFollow>();
                if (cam != null) cam.TriggerShake(0.16f, 0.14f);
            }
            else
            {
                ApplyAoEDamage(impactPos, 1.05f, smallDamage, isHeavy: false);
                SpawnSplashBurst(impactPos, 16, 0.78f);
                CombatSfxBank.PlayAt(CombatSfxId.AcidPoolSplash, impactPos, 0.56f, 1.18f, 1.34f, 0.45f, 0.03f);
            }

            Destroy(gameObject);
        }

        private Vector3 ResolveGroundPoint(Vector3 pos)
        {
            Ray ray = new Ray(new Vector3(pos.x, pos.y + 3.5f, pos.z), Vector3.down);
            RaycastHit[] hits = Physics.RaycastAll(ray, 10f, ~0, QueryTriggerInteraction.Ignore);
            float bestDist = float.MaxValue;
            Vector3 bestPt = new Vector3(pos.x, _groundY, pos.z);
            for (int i = 0; i < hits.Length; i++)
            {
                Collider c = hits[i].collider;
                if (c == null || IsOwnerCollider(c)) continue;
                if (c.GetComponentInParent<IAcidDamageable>() != null) continue;
                if (hits[i].distance < bestDist)
                {
                    bestDist = hits[i].distance;
                    bestPt = hits[i].point;
                }
            }
            return bestPt;
        }

        private void ApplyAoEDamage(Vector3 center, float radius, float damage, bool isHeavy)
        {
            Collider[] cols = Physics.OverlapSphere(center, radius);
            HashSet<Object> hitSet = new HashSet<Object>();
            bool hitTarget = false;

            for (int i = 0; i < cols.Length; i++)
            {
                Collider c = cols[i];
                if (c == null || IsOwnerCollider(c)) continue;

                OrganPulse op = c.GetComponentInParent<OrganPulse>();
                if (op != null && hitSet.Add(op))
                {
                    op.TriggerHitRecoil();
                }

                WhipTrainingDummy dummy = c.GetComponentInParent<WhipTrainingDummy>();
                if (dummy != null && hitSet.Add(dummy))
                {
                    dummy.TakeWhipDamage(damage, isMelee: false);
                    hitTarget = true;
                    continue;
                }

                IAcidDamageable dmg = c.GetComponentInParent<IAcidDamageable>();
                if (dmg != null && dmg is Object obj && hitSet.Add(obj))
                {
                    Vector3 pt = c.ClosestPoint(center);
                    Vector3 pushDir = (pt - center);
                    pushDir.y = 0.25f;
                    if (pushDir.sqrMagnitude < 0.01f) pushDir = Vector3.up;
                    pushDir.Normalize();

                    BloodSplatterSystem.BeginAttackBurstContext(pushDir, pt, isHeavy ? 1.15f : 0.72f, isHeavy);
                    CombatSfxBank.BeginImpactScope(isHeavy);
                    try
                    {
                        dmg.TakeAcidDamage(damage);
                    }
                    finally
                    {
                        if (!BloodSplatterSystem.WasEntityHandledInContext)
                        {
                            BloodSplatterSystem.SpawnBloodBurst(pt, pushDir, isHeavy ? 1.15f : 0.72f, null, isHeavy);
                        }
                        if (!CombatSfxBank.WasImpactClaimedInScope)
                        {
                            CombatSfxBank.PlayImpact(isHeavy, pt, isHeavy ? 0.85f : 0.68f);
                        }
                        BloodSplatterSystem.EndAttackBurstContext();
                        CombatSfxBank.EndImpactScope(isHeavy);
                    }
                    hitTarget = true;
                }
            }

            if (hitTarget)
            {
                CombatHitstopManager.TriggerHitstop(isHeavy ? 0.052f : 0.028f, 0.03f, isHeavy);
            }
        }

        private void SpawnAcidPool(Vector3 center)
        {
            GameObject poolGo = new GameObject("Tenias_AcidGroundPool");
            poolGo.transform.position = new Vector3(center.x, center.y + 0.05f, center.z);

            AcidGroundPool pool = poolGo.AddComponent<AcidGroundPool>();
            pool.owner = _owner;
            pool.radius = poolRadius;
            pool.duration = poolDuration;
            pool.damagePerSecond = poolDamagePerSecond;
            pool.tickInterval = poolTickInterval;
            pool.acidMaterial = acidMaterial;
            pool.pixelParticleMaterial = pixelParticleMaterial;
            pool.Initialize();
        }

        private void SpawnSplashBurst(Vector3 pos, int count, float scaleMult)
        {
            GameObject fxGo = new GameObject("AcidSplashFX");
            fxGo.transform.position = pos + Vector3.up * 0.15f;

            ParticleSystem ps = fxGo.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = false;
            main.duration = 0.5f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.22f, 0.45f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2.2f * scaleMult, 5.2f * scaleMult);
            main.startSize = new ParticleSystem.MinMaxCurve(0.14f * scaleMult, 0.28f * scaleMult);
            main.startColor = new Color(0.45f, 1.0f, 0.25f, 1f);
            main.gravityModifier = 0.85f;
            main.stopAction = ParticleSystemStopAction.Destroy;

            var emission = ps.emission;
            emission.enabled = false;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Hemisphere;
            shape.radius = 0.2f * scaleMult;

            ParticleSystemRenderer psRend = fxGo.GetComponent<ParticleSystemRenderer>();
            if (psRend != null && pixelParticleMaterial != null)
            {
                psRend.sharedMaterial = pixelParticleMaterial;
            }

            ps.Emit(count);
        }
    }

    /// <summary>
    /// Poca Acida Corrosiva deixada pela Bolha Pesada do Chicote Mini Tenia.
    /// Cobre o solo (~3.0m de raio) por 3.0 segundos e aplica dano continuo nos inimigos dentro dela.
    /// </summary>
    public class AcidGroundPool : MonoBehaviour
    {
        public GameObject owner;
        public float radius = 3.0f;
        public float duration = 3.0f;
        public float damagePerSecond = 16f;
        public float tickInterval = 0.30f;
        public Material acidMaterial;
        public Material pixelParticleMaterial;

        private float _elapsed;
        private float _tickTimer;
        private Transform _discTransform;
        private ParticleSystem _bubblePs;
        private readonly HashSet<Object> _tickHitSet = new HashSet<Object>();

        public void Initialize()
        {
            BuildPoolVisuals();
            // Primeiro tick imediato ao formar a poca
            ApplyPoolTick();
            _tickTimer = tickInterval;
        }

        private void BuildPoolVisuals()
        {
            GameObject disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            disc.name = "AcidPoolDisc";
            disc.transform.SetParent(transform, false);
            disc.transform.localPosition = Vector3.zero;
            disc.transform.localScale = new Vector3(radius * 2f, 0.03f, radius * 2f);
            Destroy(disc.GetComponent<Collider>());
            _discTransform = disc.transform;

            Renderer rend = disc.GetComponent<Renderer>();
            if (rend != null)
            {
                if (acidMaterial != null)
                {
                    rend.sharedMaterial = acidMaterial;
                }
                else
                {
                    Shader s = Shader.Find("MapaEstomago/URP/PS1_GastricAcid");
                    if (s == null) s = Shader.Find("Universal Render Pipeline/Lit");
                    Material fallback = new Material(s);
                    fallback.color = new Color(0.28f, 0.92f, 0.22f, 0.85f);
                    rend.sharedMaterial = fallback;
                }
            }

            GameObject fx = new GameObject("PoolBubbles");
            fx.transform.SetParent(transform, false);
            _bubblePs = fx.AddComponent<ParticleSystem>();

            var main = _bubblePs.main;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.70f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 1.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.14f, 0.28f);
            main.startColor = new Color(0.50f, 1.0f, 0.30f, 0.95f);

            var emission = _bubblePs.emission;
            emission.rateOverTime = 24f;

            var shape = _bubblePs.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = radius * 0.85f;
            shape.rotation = new Vector3(-90f, 0f, 0f);

            ParticleSystemRenderer psRend = fx.GetComponent<ParticleSystemRenderer>();
            if (psRend != null && pixelParticleMaterial != null)
            {
                psRend.sharedMaterial = pixelParticleMaterial;
            }

            GameObject lightGo = new GameObject("PoolGlow");
            lightGo.transform.SetParent(transform, false);
            lightGo.transform.localPosition = new Vector3(0f, 0.55f, 0f);
            Light l = lightGo.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = new Color(0.38f, 1.0f, 0.16f);
            l.intensity = 1.45f;
            l.range = Mathf.Max(2.8f, radius * 1.55f);
            l.shadows = LightShadows.None;
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            _elapsed += dt;
            _tickTimer -= dt;

            if (_tickTimer <= 0f)
            {
                _tickTimer += Mathf.Max(0.05f, tickInterval);
                ApplyPoolTick();
            }

            if (_discTransform != null)
            {
                float lifeRatio = Mathf.Clamp01(1f - (_elapsed / Mathf.Max(0.1f, duration)));
                float fadeScale = lifeRatio < 0.22f ? Mathf.Lerp(0.15f, 1f, lifeRatio / 0.22f) : 1f;
                float pulse = 1f + Mathf.Sin(Time.time * 8f) * 0.03f;
                float d = radius * 2f * fadeScale * pulse;
                _discTransform.localScale = new Vector3(d, 0.03f, d);
            }

            if (_elapsed >= duration)
            {
                Destroy(gameObject);
            }
        }

        private void ApplyPoolTick()
        {
            float tickDamage = damagePerSecond * Mathf.Max(0.05f, tickInterval);
            Collider[] cols = Physics.OverlapSphere(transform.position, radius);
            _tickHitSet.Clear();

            for (int i = 0; i < cols.Length; i++)
            {
                Collider c = cols[i];
                if (c == null || c.CompareTag("Player") || c.GetComponentInParent<SimplePlayerController>() != null) continue;
                if (owner != null && (c.gameObject == owner || c.transform.IsChildOf(owner.transform))) continue;

                WhipTrainingDummy dummy = c.GetComponentInParent<WhipTrainingDummy>();
                if (dummy != null && _tickHitSet.Add(dummy))
                {
                    dummy.TakeWhipDamage(tickDamage, isMelee: false);
                    continue;
                }

                IAcidDamageable dmg = c.GetComponentInParent<IAcidDamageable>();
                if (dmg != null && dmg is Object obj && _tickHitSet.Add(obj))
                {
                    dmg.TakeAcidDamage(tickDamage);
                }
            }
        }
    }
}
