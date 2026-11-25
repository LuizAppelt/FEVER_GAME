using System.Collections.Generic;
using BossTenia;
using MapaEstomago;
using StomachAbyss.Combat;
using StomachAbyss.Progression;
using UnityEngine;
using UnityEngine.AI;

namespace ImmuneEnemies
{
    /// <summary>
    /// Classe base compartilhada para inimigos comuns do sistema imunologico infernal.
    /// Gerencia vida (IAcidDamageable compativel com Espada Cruz), hit-flash via MaterialPropertyBlock,
    /// barra de vida diegetica sobre o inimigo quando ferido, audio espacial, grounding e morte.
    /// </summary>
    public abstract class ImmuneEnemyBase : MonoBehaviour, IAcidDamageable
    {
        private static readonly int PropEmissionColor = Shader.PropertyToID("_EmissionColor");
        private static readonly List<ImmuneEnemyBase> s_allEnemies = new List<ImmuneEnemyBase>();

        private static GUIStyle s_labelStyle;
        private static Texture2D s_whiteTex;

        [Header("Identidade & Combate")]
        public string enemyDisplayName = "Guarda Imune";
        public float maxHealth = 120f;
        public float currentHealth = 120f;
        public float aggroRange = 17f;
        public float leashRadius = 26f;

        [Header("Recompensa & Feedback")]
        public int biomassReward = 30;
        public bool dropMinorHealOrbOnDeath = false;
        [Range(0f, 1f)]
        public float minorHealDropChance = 0.25f;
        public float minorHealAmount = 16f;
        public Color bloodParticleColor = new Color(0.75f, 0.95f, 0.25f, 1f);
        public Color hitFlashColor = new Color(1.8f, 0.35f, 0.25f, 1f);

        public bool IsDead { get; protected set; }
        public Vector3 AnchorPosition { get; protected set; }

        protected Transform PlayerTarget;
        protected SimplePlayerController PlayerController;
        protected AudioSource AudioSrc;
        protected Vector3 KnockbackVel;

        private readonly List<Renderer> _renderers = new List<Renderer>(16);
        private readonly List<Color> _baseEmissions = new List<Color>(16);
        private MaterialPropertyBlock _mpb;
        private float _hitFlashTimer;
        private float _hitRecoilTimer;
        private float _hitRecoilDuration = 0.16f;
        private float _hitRecoilStrength;
        private float _microStaggerTimer;
        private float _deathElapsed;
        private float _hpBarVisibleTimer;
        private Vector3 _initialScale;
        private Vector3 _spawnPosition;
        private Quaternion _spawnRotation;
        private readonly RaycastHit[] _groundHits = new RaycastHit[16];

        protected virtual void Awake()
        {
            currentHealth = maxHealth;
            AnchorPosition = transform.position;
            _spawnPosition = transform.position;
            _spawnRotation = transform.rotation;
            _initialScale = transform.localScale;
            _mpb = new MaterialPropertyBlock();

            if (biomassReward == 30)
            {
                biomassReward = maxHealth >= 150f ? 55 : (maxHealth <= 100f ? 25 : 35);
            }

            if (!s_allEnemies.Contains(this))
            {
                s_allEnemies.Add(this);
            }

            AudioSrc = GetComponent<AudioSource>();
            if (AudioSrc == null) AudioSrc = gameObject.AddComponent<AudioSource>();
            ImmuneEnemyAudio.ConfigureSpatialSource(AudioSrc);
            ImmuneEnemyAudio.EnsureInitialized();

            CacheRenderers();
        }

        protected virtual void OnDestroy()
        {
            s_allEnemies.Remove(this);
        }

        public int GetBiomassReward()
        {
            return Mathf.Max(0, biomassReward);
        }

        public static void RespawnAllEnemies()
        {
            for (int i = s_allEnemies.Count - 1; i >= 0; i--)
            {
                ImmuneEnemyBase enemy = s_allEnemies[i];
                if (enemy == null)
                {
                    s_allEnemies.RemoveAt(i);
                    continue;
                }

                enemy.RespawnAtOrigin();
            }
        }

        public virtual void RespawnAtOrigin()
        {
            StopAllCoroutines();

            if (!gameObject.activeSelf)
            {
                gameObject.SetActive(true);
            }

            currentHealth = maxHealth;
            IsDead = false;
            KnockbackVel = Vector3.zero;
            _hitRecoilTimer = 0f;
            _microStaggerTimer = 0f;
            _hitFlashTimer = 0f;
            _deathElapsed = 0f;
            _hpBarVisibleTimer = 0f;
            transform.localScale = _initialScale;

            Collider[] cols = GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < cols.Length; i++)
            {
                if (cols[i] != null) cols[i].enabled = true;
            }

            Renderer[] rends = GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < rends.Length; i++)
            {
                if (rends[i] != null)
                {
                    rends[i].enabled = true;
                    rends[i].SetPropertyBlock(null);
                }
            }

            NavMeshAgent agent = GetComponent<NavMeshAgent>();
            if (agent != null)
            {
                agent.enabled = false;
                transform.position = _spawnPosition;
                transform.rotation = _spawnRotation;
                agent.enabled = true;
                if (agent.isOnNavMesh)
                {
                    agent.Warp(_spawnPosition);
                }
            }
            else
            {
                transform.position = _spawnPosition;
                transform.rotation = _spawnRotation;
            }

            AnchorPosition = _spawnPosition;
        }

        protected virtual void Start()
        {
            ResolvePlayer();
        }

        protected virtual void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            ResolvePlayer();
            UpdateHitFlash(dt);

            if (IsDead)
            {
                TickDeathVisuals(dt);
                return;
            }

            if (_hpBarVisibleTimer > 0f)
            {
                _hpBarVisibleTimer -= dt;
            }

            UpdateHitRecoil(dt);

            if (KnockbackVel.sqrMagnitude > 0.0025f)
            {
                ApplyHorizontalStep(KnockbackVel * dt);
                KnockbackVel *= Mathf.Exp(-dt * 11f);
                if (KnockbackVel.sqrMagnitude <= 0.0025f)
                {
                    KnockbackVel = Vector3.zero;
                }
            }

            if (_microStaggerTimer > 0f)
            {
                _microStaggerTimer = Mathf.Max(0f, _microStaggerTimer - dt);
                return;
            }

            TickEnemyBehavior(dt);
        }

        protected abstract void TickEnemyBehavior(float dt);

        protected virtual float GetIncomingDamageMultiplier() => 1f;

        protected virtual bool IsDamageFlashCritical() => false;

        public virtual void TakeAcidDamage(float amount)
        {
            if (IsDead || amount <= 0f) return;

            BloodSplatterSystem.MarkEntityHandledInContext();

            float mult = GetIncomingDamageMultiplier();
            float finalDmg = amount * mult;
            currentHealth = Mathf.Max(0f, currentHealth - finalDmg);
            _hpBarVisibleTimer = 6.0f;
            _hitFlashTimer = 0.16f;

            bool isCrit = IsDamageFlashCritical() || CombatSfxBank.IsHeavyAttackContext || BloodSplatterSystem.ContextIsHeavy;
            Vector3 hitPos = BloodSplatterSystem.HasActiveAttackContext
                ? BloodSplatterSystem.ContextHitPoint
                : (transform.position + Vector3.up * 1.0f);

            CombatSfxBank.PlayImpact(isCrit, hitPos, isCrit ? 0.90f : 0.82f);
            SpawnSplashParticles(transform.position + Vector3.up * 1.0f, IsDamageFlashCritical() ? new Color(1f, 0.9f, 0.25f) : bloodParticleColor, 6);

            Vector3 attackDir;
            if (BloodSplatterSystem.HasActiveAttackContext)
            {
                attackDir = BloodSplatterSystem.ContextAttackDir;
            }
            else if (PlayerTarget != null)
            {
                Vector3 awayFromPlayer = transform.position - PlayerTarget.position;
                awayFromPlayer.y = 0.22f;
                attackDir = awayFromPlayer.sqrMagnitude > 0.001f ? awayFromPlayer.normalized : transform.forward;
            }
            else
            {
                attackDir = (-transform.forward + Vector3.up * 0.22f).normalized;
            }

            float burstIntensity = (isCrit ? 1.32f : 1.0f) * (BloodSplatterSystem.HasActiveAttackContext ? BloodSplatterSystem.ContextIntensityMult : 1.0f);
            BloodSplatterSystem.SpawnBloodBurst(hitPos, attackDir, burstIntensity, bloodParticleColor, isCrit);

            bool isHeavyHit = CombatSfxBank.IsHeavyAttackContext || amount >= 30f;
            bool isMediumHit = !isHeavyHit && amount >= 20f;

            Vector3 pushDir = Vector3.zero;
            if (PlayerTarget != null)
            {
                Vector3 away = transform.position - PlayerTarget.position;
                away.y = 0f;
                if (away.sqrMagnitude > 0.001f)
                {
                    pushDir = away.normalized;
                }
            }
            else if (BloodSplatterSystem.HasActiveAttackContext)
            {
                Vector3 flatDir = BloodSplatterSystem.ContextAttackDir;
                flatDir.y = 0f;
                if (flatDir.sqrMagnitude > 0.001f)
                {
                    pushDir = flatDir.normalized;
                }
            }

            if (pushDir.sqrMagnitude > 0.001f)
            {
                float knockbackStrength = isHeavyHit ? 7.5f : (isMediumHit ? 4.2f : 1.8f);
                ApplyKnockback(pushDir * knockbackStrength);
            }

            _hitRecoilDuration = isHeavyHit ? 0.19f : (isMediumHit ? 0.16f : 0.13f);
            _hitRecoilTimer = _hitRecoilDuration;
            _hitRecoilStrength = isHeavyHit ? 0.22f : (isMediumHit ? 0.15f : 0.09f);

            if (isHeavyHit && currentHealth > 0f)
            {
                _microStaggerTimer = Mathf.Max(_microStaggerTimer, 0.28f);
            }

            OnDamaged(finalDmg);

            if (currentHealth <= 0f)
            {
                BeginDeath();
            }
        }

        public void ApplyKnockback(Vector3 horizontalImpulse)
        {
            horizontalImpulse.y = 0f;
            KnockbackVel += horizontalImpulse;
            if (KnockbackVel.magnitude > 11f)
            {
                KnockbackVel = KnockbackVel.normalized * 11f;
            }
        }

        protected virtual void OnDamaged(float dmg) { }

        protected virtual void OnDeathTriggered() { }

        protected void BeginDeath()
        {
            if (IsDead) return;
            IsDead = true;
            _deathElapsed = 0f;
            _hpBarVisibleTimer = 0f;
            _hitRecoilTimer = 0f;
            _microStaggerTimer = 0f;
            transform.localScale = _initialScale;

            Collider[] cols = GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < cols.Length; i++)
            {
                if (cols[i] != null) cols[i].enabled = false;
            }

            BiomassProgressionSystem.AwardBiomass(GetBiomassReward(), transform.position + Vector3.up * 0.8f);

            ImmuneEnemyAudio.Play(AudioSrc, ImmuneEnemyAudio.ClipDeathBurst, 0.82f, 0.88f, 1.08f);
            SpawnSplashParticles(transform.position + Vector3.up * 1.0f, bloodParticleColor, 14);

            Vector3 deathDir = PlayerTarget != null
                ? ((transform.position - PlayerTarget.position).normalized + Vector3.up * 0.35f).normalized
                : (transform.forward + Vector3.up * 0.35f).normalized;
            BloodSplatterSystem.SpawnBloodBurst(transform.position + Vector3.up * 0.75f, deathDir, 1.48f, bloodParticleColor, true);

            if (dropMinorHealOrbOnDeath && Random.value <= minorHealDropChance)
            {
                Vector3 floorSpot = transform.position;
                if (TrySampleWalkableGround(floorSpot, out float floorY))
                {
                    floorSpot.y = floorY;
                }
                TeniaSoulOrb.Spawn(floorSpot, minorHealAmount, 24f, null);
            }

            OnDeathTriggered();
        }

        protected virtual void TickDeathVisuals(float dt)
        {
            _deathElapsed += dt;
            float t = Mathf.Clamp01(_deathElapsed / 0.85f);
            float squashY = Mathf.Lerp(1f, 0.08f, t);
            float spreadXZ = Mathf.Lerp(1f, 1.32f, t);
            transform.localScale = new Vector3(_initialScale.x * spreadXZ, _initialScale.y * squashY, _initialScale.z * spreadXZ);

            if (_deathElapsed >= 0.92f)
            {
                gameObject.SetActive(false);
            }
        }

        protected virtual void ApplyHorizontalStep(Vector3 deltaXZ)
        {
            Vector3 next = transform.position + new Vector3(deltaXZ.x, 0f, deltaXZ.z);
            if (TrySampleWalkableGround(next, out float groundY) && Mathf.Abs(groundY - transform.position.y) <= 1.45f)
            {
                next.y = groundY;
                transform.position = next;
            }
        }

        protected bool TrySampleWalkableGround(Vector3 worldPos, out float groundY)
        {
            groundY = worldPos.y;
            Vector3 origin = new Vector3(worldPos.x, transform.position.y + 2.2f, worldPos.z);
            int count = Physics.RaycastNonAlloc(origin, Vector3.down, _groundHits, 6.5f, ~0, QueryTriggerInteraction.Ignore);

            float bestDiff = float.MaxValue;
            bool found = false;
            for (int i = 0; i < count; i++)
            {
                Collider c = _groundHits[i].collider;
                if (c == null) continue;
                if (c.transform.IsChildOf(transform)) continue;
                if (c.GetComponentInParent<ImmuneEnemyBase>() != null) continue;
                if (c.GetComponentInParent<SimplePlayerController>() != null) continue;
                if (c.name.IndexOf("Acid_Channel", System.StringComparison.OrdinalIgnoreCase) >= 0) continue;

                float y = _groundHits[i].point.y;
                float diff = Mathf.Abs(y - transform.position.y);
                if (diff < bestDiff)
                {
                    bestDiff = diff;
                    groundY = y;
                    found = true;
                }
            }
            return found;
        }

        protected void ResolvePlayer()
        {
            if (PlayerTarget == null || !PlayerTarget.gameObject.activeInHierarchy)
            {
                GameObject go = null;
                try { go = GameObject.FindGameObjectWithTag("Player"); } catch { }
                if (go == null) go = GameObject.Find("Test_Player (1)") ?? GameObject.Find("Test_Player");
                if (go != null)
                {
                    PlayerTarget = go.transform;
                    PlayerController = go.GetComponent<SimplePlayerController>();
                }
            }
        }

        protected void RotateTowardXZ(Vector3 worldDir, float degPerSec, float dt)
        {
            Vector3 flat = new Vector3(worldDir.x, 0f, worldDir.z);
            if (flat.sqrMagnitude < 0.0001f) return;
            Quaternion targetRot = Quaternion.LookRotation(flat.normalized, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, degPerSec * dt);
        }

        protected float FlatDistance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        protected bool IsPlayerTargetAlive()
        {
            return PlayerTarget != null && PlayerTarget.gameObject.activeInHierarchy
                && (PlayerController == null || !PlayerController.IsDead);
        }

        protected void CacheRenderers()
        {
            _renderers.Clear();
            _baseEmissions.Clear();
            GetComponentsInChildren(true, _renderers);
            for (int i = 0; i < _renderers.Count; i++)
            {
                Renderer r = _renderers[i];
                Color em = Color.black;
                if (r != null && r.sharedMaterial != null && r.sharedMaterial.HasProperty(PropEmissionColor))
                {
                    em = r.sharedMaterial.GetColor(PropEmissionColor);
                }
                _baseEmissions.Add(em);
            }
        }

        protected void SetOverrideEmissionTint(Color tint, float strength)
        {
            if (_mpb == null) _mpb = new MaterialPropertyBlock();
            for (int i = 0; i < _renderers.Count; i++)
            {
                Renderer r = _renderers[i];
                if (r == null) continue;
                r.GetPropertyBlock(_mpb);
                Color finalEm = Color.Lerp(_baseEmissions[i], tint, Mathf.Clamp01(strength));
                _mpb.SetColor(PropEmissionColor, finalEm);
                r.SetPropertyBlock(_mpb);
            }
        }

        private void UpdateHitFlash(float dt)
        {
            if (_hitFlashTimer > 0f)
            {
                _hitFlashTimer = Mathf.Max(0f, _hitFlashTimer - dt);
                SetOverrideEmissionTint(hitFlashColor, _hitFlashTimer / 0.16f);
            }
        }

        private void UpdateHitRecoil(float dt)
        {
            if (_hitRecoilTimer <= 0f) return;

            _hitRecoilTimer = Mathf.Max(0f, _hitRecoilTimer - dt);
            if (_hitRecoilTimer <= 0f)
            {
                transform.localScale = _initialScale;
                return;
            }

            float normalized = _hitRecoilTimer / Mathf.Max(0.01f, _hitRecoilDuration);
            float squash = Mathf.Sin(normalized * Mathf.PI) * _hitRecoilStrength;
            float scaleXZ = 1f + squash;
            float scaleY = 1f - squash * 0.85f;
            transform.localScale = new Vector3(
                _initialScale.x * scaleXZ,
                _initialScale.y * scaleY,
                _initialScale.z * scaleXZ);
        }

        protected static void SpawnSplashParticles(Vector3 pos, Color color, int count)
        {
            GameObject go = new GameObject("ImmuneEnemySplashFX");
            go.transform.position = pos;
            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.loop = false;
            main.playOnAwake = false;
            main.duration = 0.6f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.28f, 0.55f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2.2f, 5.4f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.14f, 0.34f);
            main.gravityModifier = 1.15f;
            main.startColor = color;

            var emission = ps.emission;
            emission.enabled = false;
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.28f;

            var pr = ps.GetComponent<ParticleSystemRenderer>();
            if (pr != null)
            {
                Shader sh = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Particles/Standard Unlit");
                if (sh != null)
                {
                    Material mat = new Material(sh);
                    mat.color = color;
                    pr.sharedMaterial = mat;
                }
            }

            ps.Emit(count);
            Destroy(go, 1.1f);
        }

        private void OnGUI()
        {
            if (IsDead || _hpBarVisibleTimer <= 0f || currentHealth >= maxHealth) return;
            Camera cam = Camera.main;
            if (cam == null) return;

            Vector3 worldTop = transform.position + Vector3.up * 2.55f;
            if (Vector3.Distance(cam.transform.position, worldTop) > 36f) return;

            Vector3 sp = cam.WorldToScreenPoint(worldTop);
            if (sp.z <= 0.1f) return;

            EnsureGuiAssets();
            float barW = 86f;
            float barH = 9f;
            float x = sp.x - barW * 0.5f;
            float y = Screen.height - sp.y - 14f;

            GUI.color = new Color(0.08f, 0.05f, 0.06f, 0.88f);
            GUI.DrawTexture(new Rect(x - 2f, y - 2f, barW + 4f, barH + 4f), s_whiteTex);

            float ratio = Mathf.Clamp01(currentHealth / Mathf.Max(1f, maxHealth));
            GUI.color = new Color(0.88f, 0.22f, 0.20f, 0.96f);
            GUI.DrawTexture(new Rect(x, y, barW * ratio, barH), s_whiteTex);

            GUI.color = Color.white;
            GUI.Label(new Rect(x - 20f, y - 16f, barW + 40f, 16f), enemyDisplayName, s_labelStyle);
        }

        private static void EnsureGuiAssets()
        {
            if (s_whiteTex == null)
            {
                s_whiteTex = Texture2D.whiteTexture;
            }
            if (s_labelStyle == null)
            {
                s_labelStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 11,
                    fontStyle = FontStyle.Bold
                };
                s_labelStyle.normal.textColor = new Color(0.96f, 0.90f, 0.88f, 0.95f);
            }
        }
    }
}
