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

            GameObject root = new GameObject("AcidGlobVisual");
            root.transform.SetParent(transform, false);
            _visualSphere = root.transform;

            Material mat = acidMaterial;
            if (mat == null)
            {
                Shader s = Shader.Find("MapaEstomago/URP/PS1_GastricAcid")
                    ?? Shader.Find("Universal Render Pipeline/Lit")
                    ?? Shader.Find("Standard");
                mat = new Material(s);
                mat.color = new Color(0.28f, 0.98f, 0.22f, 0.94f);
                if (mat.HasProperty("_EmissionColor"))
                {
                    mat.EnableKeyword("_EMISSION");
                    mat.SetColor("_EmissionColor", new Color(0.35f, 1.85f, 0.22f, 1f));
                }
            }

            // Elongated pressurized bio-cannon acid glob
            GameObject coreBlob = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            coreBlob.name = "AcidCoreBlob";
            coreBlob.transform.SetParent(root.transform, false);
            Destroy(coreBlob.GetComponent<Collider>());
            float scale = isHeavyBossBubble ? 0.86f : 0.44f;
            coreBlob.transform.localScale = new Vector3(scale * 0.85f, scale * 0.82f, scale * 1.48f);
            Renderer rend = coreBlob.GetComponent<Renderer>();
            if (rend != null) rend.sharedMaterial = mat;

            // Orbiting satellite droplets for organic visceral silhouette
            int dropCount = isHeavyBossBubble ? 4 : 2;
            for (int i = 0; i < dropCount; i++)
            {
                float ang = (i / (float)dropCount) * Mathf.PI * 2f;
                GameObject drop = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                drop.name = $"AcidDrop_{i}";
                drop.transform.SetParent(root.transform, false);
                Destroy(drop.GetComponent<Collider>());
                float r = scale * 0.56f;
                drop.transform.localPosition = new Vector3(Mathf.Cos(ang) * r, Mathf.Sin(ang) * r * 0.7f, -scale * (0.25f + i * 0.15f));
                drop.transform.localScale = Vector3.one * (scale * 0.34f);
                Renderer dr = drop.GetComponent<Renderer>();
                if (dr != null) dr.sharedMaterial = mat;
            }

            // Dynamic emerald glow light on projectile
            GameObject lightGo = new GameObject("AcidProjLight");
            lightGo.transform.SetParent(root.transform, false);
            Light pl = lightGo.AddComponent<Light>();
            pl.type = LightType.Point;
            pl.color = new Color(0.36f, 1.0f, 0.20f);
            pl.intensity = isHeavyBossBubble ? 2.2f : 1.25f;
            pl.range = isHeavyBossBubble ? 5.2f : 3.2f;
            pl.shadows = LightShadows.None;

            GameObject trailGo = new GameObject("AcidTrail");
            trailGo.transform.SetParent(transform, false);
            _trailParticles = trailGo.AddComponent<ParticleSystem>();

            var main = _trailParticles.main;
            main.loop = true;
            main.startLifetime = isHeavyBossBubble ? 0.42f : 0.24f;
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 1.6f);
            main.startSize = new ParticleSystem.MinMaxCurve(isHeavyBossBubble ? 0.22f : 0.12f, isHeavyBossBubble ? 0.38f : 0.20f);
            main.startColor = new Color(0.45f, 1.0f, 0.25f, 0.95f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var emission = _trailParticles.emission;
            emission.rateOverTime = isHeavyBossBubble ? 36f : 22f;

            var shape = _trailParticles.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = isHeavyBossBubble ? 0.34f : 0.14f;

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
                float wobble = 1f + Mathf.Sin(Time.time * 20f) * 0.12f;
                _visualSphere.localScale = new Vector3(wobble, 2f - wobble, 1f + (wobble - 1f) * 0.5f);
                _visualSphere.Rotate(0f, 0f, 240f * dt, Space.Self);
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
        private Light _poolLight;
        private readonly List<Transform> _blisters = new List<Transform>(6);
        private readonly List<float> _blisterPhases = new List<float>(6);
        private ParticleSystem _bubblePs;
        private readonly HashSet<Object> _tickHitSet = new HashSet<Object>();
        private static Mesh s_organicPuddleMesh;

        public void Initialize()
        {
            BuildPoolVisuals();
            // Primeiro tick imediato ao formar a poca
            ApplyPoolTick();
            _tickTimer = tickInterval;
        }

        private static Mesh GetOrBuildOrganicPuddleMesh()
        {
            if (s_organicPuddleMesh != null) return s_organicPuddleMesh;

            const int segs = 32;
            const int rings = 2;
            // Center vertex + 2 concentric organic wavy rings (inner raised core + outer irregular splash lobes)
            var verts = new List<Vector3>(1 + segs * rings);
            var uvs = new List<Vector2>(1 + segs * rings);
            var tris = new List<int>(segs * 9);

            verts.Add(new Vector3(0f, 0.045f, 0f));
            uvs.Add(new Vector2(0.5f, 0.5f));

            for (int r = 1; r <= rings; r++)
            {
                float ringFrac = r == 1 ? 0.58f : 1.0f;
                float ringY = r == 1 ? 0.032f : 0.008f;
                for (int i = 0; i < segs; i++)
                {
                    float a = (i / (float)segs) * Mathf.PI * 2f;
                    // Multi-frequency organic puddle lobes (never a flat geometric circle!)
                    float lobe = 1f
                        + 0.16f * Mathf.Sin(a * 3f + 0.4f)
                        + 0.11f * Mathf.Cos(a * 5f - 1.1f)
                        + 0.07f * Mathf.Sin(a * 8f + 2.2f);
                    float rad = ringFrac * lobe;
                    float x = Mathf.Cos(a) * rad;
                    float z = Mathf.Sin(a) * rad;
                    verts.Add(new Vector3(x, ringY, z));
                    uvs.Add(new Vector2(0.5f + x * 0.45f, 0.5f + z * 0.45f));
                }
            }

            // Center fan
            for (int i = 0; i < segs; i++)
            {
                int next = (i + 1) % segs;
                tris.Add(0);
                tris.Add(1 + next);
                tris.Add(1 + i);
            }

            // Outer organic skirt ring
            int innerStart = 1;
            int outerStart = 1 + segs;
            for (int i = 0; i < segs; i++)
            {
                int next = (i + 1) % segs;
                int i0 = innerStart + i;
                int i1 = innerStart + next;
                int o0 = outerStart + i;
                int o1 = outerStart + next;
                tris.Add(i0);
                tris.Add(i1);
                tris.Add(o1);
                tris.Add(i0);
                tris.Add(o1);
                tris.Add(o0);
            }

            Mesh m = new Mesh { name = "OrganicAcidPuddleMesh" };
            m.SetVertices(verts);
            m.SetUVs(0, uvs);
            m.SetTriangles(tris, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            s_organicPuddleMesh = m;
            return m;
        }

        private void BuildPoolVisuals()
        {
            Material mat = acidMaterial;
            if (mat == null)
            {
                Shader s = Shader.Find("MapaEstomago/URP/PS1_GastricAcid")
                    ?? Shader.Find("Universal Render Pipeline/Lit")
                    ?? Shader.Find("Standard");
                mat = new Material(s);
                mat.color = new Color(0.24f, 0.92f, 0.18f, 0.90f);
                if (mat.HasProperty("_EmissionColor"))
                {
                    mat.EnableKeyword("_EMISSION");
                    mat.SetColor("_EmissionColor", new Color(0.28f, 1.45f, 0.18f, 1f));
                }
            }

            // Organic jagged puddle mesh (replaces plain cylinder)
            GameObject puddle = new GameObject("OrganicAcidPuddle");
            puddle.transform.SetParent(transform, false);
            puddle.transform.localPosition = Vector3.zero;
            puddle.transform.localRotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            puddle.transform.localScale = new Vector3(radius * 0.25f, 1f, radius * 0.25f);
            MeshFilter mf = puddle.AddComponent<MeshFilter>();
            mf.sharedMesh = GetOrBuildOrganicPuddleMesh();
            MeshRenderer rend = puddle.AddComponent<MeshRenderer>();
            rend.sharedMaterial = mat;
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _discTransform = puddle.transform;

            // 5 animated 3D acid blisters bubbling & popping on the puddle surface
            _blisters.Clear();
            _blisterPhases.Clear();
            const int blisterCount = 5;
            for (int i = 0; i < blisterCount; i++)
            {
                float ang = (i / (float)blisterCount) * Mathf.PI * 2f + Random.Range(-0.3f, 0.3f);
                float dist = Random.Range(0.20f, 0.72f);
                GameObject b = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                b.name = $"AcidBlister_{i}";
                b.transform.SetParent(_discTransform, false);
                Destroy(b.GetComponent<Collider>());
                b.transform.localPosition = new Vector3(Mathf.Cos(ang) * dist, 0.03f, Mathf.Sin(ang) * dist);
                b.transform.localScale = Vector3.one * 0.12f;
                Renderer br = b.GetComponent<Renderer>();
                if (br != null) br.sharedMaterial = mat;
                _blisters.Add(b.transform);
                _blisterPhases.Add(Random.Range(0f, Mathf.PI * 2f));
            }

            GameObject fx = new GameObject("PoolBubbles");
            fx.transform.SetParent(transform, false);
            _bubblePs = fx.AddComponent<ParticleSystem>();

            var main = _bubblePs.main;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.40f, 0.85f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.7f, 1.8f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.14f, 0.30f);
            main.startColor = new Color(0.48f, 1.0f, 0.26f, 0.95f);

            var emission = _bubblePs.emission;
            emission.rateOverTime = 30f;

            var shape = _bubblePs.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = radius * 0.82f;
            shape.rotation = new Vector3(-90f, 0f, 0f);

            ParticleSystemRenderer psRend = fx.GetComponent<ParticleSystemRenderer>();
            if (psRend != null && pixelParticleMaterial != null)
            {
                psRend.sharedMaterial = pixelParticleMaterial;
            }

            GameObject lightGo = new GameObject("PoolGlow");
            lightGo.transform.SetParent(transform, false);
            lightGo.transform.localPosition = new Vector3(0f, 0.55f, 0f);
            _poolLight = lightGo.AddComponent<Light>();
            _poolLight.type = LightType.Point;
            _poolLight.color = new Color(0.36f, 1.0f, 0.18f);
            _poolLight.intensity = 1.85f;
            _poolLight.range = Mathf.Max(3.2f, radius * 1.65f);
            _poolLight.shadows = LightShadows.None;
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

            float lifeRatio = Mathf.Clamp01(1f - (_elapsed / Mathf.Max(0.1f, duration)));
            float introScale = Mathf.SmoothStep(0.25f, 1f, Mathf.Clamp01(_elapsed / 0.14f));
            float fadeScale = lifeRatio < 0.24f ? Mathf.Lerp(0.10f, 1f, lifeRatio / 0.24f) : 1f;
            float pulse = 1f + Mathf.Sin(Time.time * 7.5f) * 0.035f;

            if (_discTransform != null)
            {
                float r = radius * introScale * fadeScale * pulse;
                _discTransform.localScale = new Vector3(r, 1f, r);
            }

            for (int i = 0; i < _blisters.Count; i++)
            {
                Transform b = _blisters[i];
                if (b == null) continue;
                float cyc = Mathf.Repeat(Time.time * 1.85f + _blisterPhases[i], 1f);
                // Inflates smoothly 0..0.82, then pops sharply 0.82..1.0
                float bScale = cyc < 0.82f ? Mathf.Sin((cyc / 0.82f) * Mathf.PI * 0.5f) * 0.18f : (1f - (cyc - 0.82f) / 0.18f) * 0.05f;
                b.localScale = new Vector3(bScale, bScale * 0.65f, bScale);
            }

            if (_poolLight != null)
            {
                _poolLight.intensity = 1.85f * fadeScale * (0.85f + 0.15f * Mathf.Sin(Time.time * 11f));
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
