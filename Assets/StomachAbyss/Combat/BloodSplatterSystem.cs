using System.Collections.Generic;
using BossTenia;
using ImmuneEnemies;
using MapaEstomago;
using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace StomachAbyss.Combat
{
    /// <summary>
    /// Visceral PS1 persistent blood & ichor splatter system:
    /// - Emits a world-space directional spray of pixelated blood droplets on hit
    /// - Raycasts angled spray arcs onto walkable stone/bone/bridge geometry (ignoring chasm/acid surfaces)
    /// - Pools up to 180 persistent floor decals in a ring buffer with brief spread-in & coagulation darkening
    /// </summary>
    public class BloodSplatterSystem : MonoBehaviour
    {
        public const int MaxDecals = 180;
        public const string TexturesFolder = "Assets/StomachAbyss/Combat/Textures";
        private const float SpreadAnimDuration = 0.18f;
        private const float CoagulateDuration = 3.2f;

        public static BloodSplatterSystem Instance { get; private set; }

        [Header("PS1 Blood Splat Textures (256x256 RGBA)")]
        public Texture2D[] splatTextures = new Texture2D[4];

        [Header("Pool & Tuning")]
        [Range(32, 300)]
        public int maxPoolSize = MaxDecals;

        [Range(0.5f, 2.0f)]
        public float globalDecalScaleMultiplier = 1.0f;

        private struct DecalSlot
        {
            public GameObject Go;
            public Transform Tr;
            public MeshRenderer Renderer;
            public MaterialPropertyBlock Mpb;
            public Vector3 TargetScale;
            public Color FreshColor;
            public Color CoagulatedColor;
            public float TintRemap;
            public float Age;
            public bool Active;
            public bool Animating;
        }

        private DecalSlot[] _pool;
        private int _ringCursor;
        private readonly List<int> _animatingIndices = new List<int>(48);

        private Transform _poolRoot;
        private Mesh _xzQuadMesh;
        private Material[] _decalMaterials;
        private Texture2D _dropletParticleTex;
        private Material _particleMaterial;
        private ParticleSystem _sprayParticles;
        private bool _usingFallbackShader;
        private bool _initialized;

        private static readonly RaycastHit[] s_rayHits = new RaycastHit[24];
        private static readonly int PropBaseMap = Shader.PropertyToID("_BaseMap");
        private static readonly int PropMainTex = Shader.PropertyToID("_MainTex");
        private static readonly int PropBaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int PropColor = Shader.PropertyToID("_Color");
        private static readonly int PropCutoff = Shader.PropertyToID("_Cutoff");
        private static readonly int PropEmissionBoost = Shader.PropertyToID("_EmissionBoost");
        private static readonly int PropTintRemap = Shader.PropertyToID("_TintRemap");

        // Attack context passed implicitly from melee/boomerang callers to IAcidDamageable handlers
        private static bool s_hasAttackContext;
        private static Vector3 s_contextAttackDir;
        private static Vector3 s_contextHitPoint;
        private static float s_contextIntensityMult = 1f;
        private static bool s_contextIsHeavy;
        private static bool s_entityHandledInContext;

        public static bool HasActiveAttackContext => s_hasAttackContext;
        public static Vector3 ContextAttackDir => s_contextAttackDir;
        public static Vector3 ContextHitPoint => s_contextHitPoint;
        public static float ContextIntensityMult => s_contextIntensityMult;
        public static bool ContextIsHeavy => s_contextIsHeavy;
        public static bool WasEntityHandledInContext => s_entityHandledInContext;

        public static void BeginAttackBurstContext(Vector3 attackDir, Vector3 hitPoint, float intensityMult = 1f, bool isHeavy = false)
        {
            s_hasAttackContext = true;
            s_contextAttackDir = attackDir.sqrMagnitude > 0.001f ? attackDir.normalized : Vector3.forward;
            s_contextHitPoint = hitPoint;
            s_contextIntensityMult = Mathf.Max(0.25f, intensityMult);
            s_contextIsHeavy = isHeavy;
            s_entityHandledInContext = false;
        }

        public static void MarkEntityHandledInContext()
        {
            if (s_hasAttackContext)
            {
                s_entityHandledInContext = true;
            }
        }

        public static void EndAttackBurstContext()
        {
            s_hasAttackContext = false;
            s_entityHandledInContext = false;
            s_contextIntensityMult = 1f;
            s_contextIsHeavy = false;
        }

        public static BloodSplatterSystem EnsureExists()
        {
            if (Instance != null) return Instance;

            Instance = FindAnyObjectByType<BloodSplatterSystem>();
            if (Instance != null)
            {
                Instance.EnsureInitialized();
                return Instance;
            }

            GameObject container = GameObject.Find("Combat_Juice_Systems");
            if (container == null)
            {
                container = new GameObject("Combat_Juice_Systems");
            }

            Instance = container.GetComponent<BloodSplatterSystem>();
            if (Instance == null)
            {
                Instance = container.AddComponent<BloodSplatterSystem>();
            }
            Instance.EnsureInitialized();
            return Instance;
        }

        /// <summary>
        /// Spawns a directional gush of blood droplets + persistent floor/bridge splatter decals.
        /// </summary>
        public static void SpawnBloodBurst(
            Vector3 hitPos,
            Vector3 attackDir,
            float intensity = 1f,
            Color? bloodTint = null,
            bool isWeakSpot = false)
        {
            if (!Application.isPlaying) return;

            BloodSplatterSystem sys = EnsureExists();
            if (sys == null || !sys.isActiveAndEnabled) return;

            sys.SpawnBurstInternal(hitPos, attackDir, intensity, bloodTint, isWeakSpot);
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;
            EnsureInitialized();
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
            if (_xzQuadMesh != null)
            {
                Destroy(_xzQuadMesh);
                _xzQuadMesh = null;
            }
            if (_dropletParticleTex != null)
            {
                Destroy(_dropletParticleTex);
                _dropletParticleTex = null;
            }
            if (_particleMaterial != null)
            {
                Destroy(_particleMaterial);
                _particleMaterial = null;
            }
            if (_decalMaterials != null)
            {
                for (int i = 0; i < _decalMaterials.Length; i++)
                {
                    if (_decalMaterials[i] != null)
                    {
                        Destroy(_decalMaterials[i]);
                        _decalMaterials[i] = null;
                    }
                }
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            LoadEditorTexturesIfMissing();
        }

        public void LoadEditorTexturesIfMissing()
        {
            if (splatTextures == null || splatTextures.Length != 4)
            {
                splatTextures = new Texture2D[4];
            }

            string[] names =
            {
                "tex_blood_splat_01.png",
                "tex_blood_splat_02.png",
                "tex_blood_splat_03.png",
                "tex_blood_splat_04.png"
            };

            for (int i = 0; i < 4; i++)
            {
                if (splatTextures[i] == null)
                {
                    splatTextures[i] = AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexturesFolder}/{names[i]}");
                }
            }
        }
#endif

        public void EnsureInitialized()
        {
            if (_initialized) return;
            _initialized = true;

#if UNITY_EDITOR
            LoadEditorTexturesIfMissing();
#endif
            EnsureTexturesValid();
            BuildXzQuadMesh();
            BuildDecalMaterials();
            BuildParticleSystem();
            InitializePool();
        }

        private void Update()
        {
            if (!_initialized || _animatingIndices.Count == 0) return;

            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            for (int i = _animatingIndices.Count - 1; i >= 0; i--)
            {
                int slotIdx = _animatingIndices[i];
                if (slotIdx < 0 || slotIdx >= _pool.Length)
                {
                    _animatingIndices.RemoveAt(i);
                    continue;
                }

                DecalSlot slot = _pool[slotIdx];
                if (!slot.Active || !slot.Animating || slot.Tr == null || slot.Renderer == null)
                {
                    _animatingIndices.RemoveAt(i);
                    continue;
                }

                slot.Age += dt;

                // 1. Puddle spread scale-in over 0.18s
                float spreadT = Mathf.Clamp01(slot.Age / SpreadAnimDuration);
                float easedSpread = 1f - (1f - spreadT) * (1f - spreadT) * (1f - spreadT);
                float scaleFactor = Mathf.Lerp(0.42f, 1.0f, easedSpread);
                slot.Tr.localScale = slot.TargetScale * scaleFactor;

                // 2. Fresh scarlet -> coagulated dark crimson over 3.2s
                float coagT = Mathf.Clamp01(slot.Age / CoagulateDuration);
                Color col = Color.Lerp(slot.FreshColor, slot.CoagulatedColor, coagT);
                ApplyColorToSlot(ref slot, col);

                if (slot.Age >= CoagulateDuration)
                {
                    slot.Tr.localScale = slot.TargetScale;
                    slot.Animating = false;
                    _pool[slotIdx] = slot;
                    _animatingIndices.RemoveAt(i);
                }
                else
                {
                    _pool[slotIdx] = slot;
                }
            }
        }

        private void SpawnBurstInternal(
            Vector3 hitPos,
            Vector3 attackDir,
            float intensity,
            Color? bloodTint,
            bool isWeakSpot)
        {
            EnsureInitialized();

            float effIntensity = Mathf.Clamp(intensity * (isWeakSpot ? 1.40f : 1.0f), 0.25f, 2.8f);
            Vector3 baseDir = attackDir.sqrMagnitude > 0.001f ? attackDir.normalized : Vector3.forward;

            bool hasCustomTint = bloodTint.HasValue;
            Color tintVal = hasCustomTint ? bloodTint.Value : new Color(0.82f, 0.07f, 0.09f, 1f);
            bool useLumaRemap = hasCustomTint && (tintVal.g > tintVal.r * 0.9f || tintVal.b > tintVal.r * 0.9f);

            // 1. Directional blood gush particles ("sangue que jorra")
            EmitDirectionalSprayParticles(hitPos, baseDir, effIntensity, tintVal, isWeakSpot);

            // 2. Persistent floor & bridge splatter decals (3 to 7 angled raycasts)
            int rayCount = Mathf.Clamp(Mathf.RoundToInt(3f + effIntensity * 2.1f + (isWeakSpot ? 1.5f : 0f)), 3, 7);
            Vector3 flatDir = new Vector3(baseDir.x, 0f, baseDir.z);
            if (flatDir.sqrMagnitude < 0.001f)
            {
                flatDir = new Vector3(Random.Range(-1f, 1f), 0f, Random.Range(-1f, 1f)).normalized;
            }
            else
            {
                flatDir.Normalize();
            }

            Color freshCol;
            Color coagulatedCol;
            if (useLumaRemap)
            {
                freshCol = new Color(tintVal.r * 1.18f, tintVal.g * 1.18f, tintVal.b * 1.18f, 1f);
                coagulatedCol = new Color(tintVal.r * 0.56f, tintVal.g * 0.56f, tintVal.b * 0.56f, 1f);
            }
            else if (hasCustomTint)
            {
                // Slight crimson variation without crushing PNG arterial contrast
                freshCol = new Color(
                    Mathf.Lerp(1.14f, tintVal.r * 1.25f, 0.35f),
                    Mathf.Lerp(0.98f, tintVal.g * 1.25f, 0.25f),
                    Mathf.Lerp(0.98f, tintVal.b * 1.25f, 0.25f),
                    1f);
                coagulatedCol = new Color(freshCol.r * 0.56f, freshCol.g * 0.48f, freshCol.b * 0.50f, 1f);
            }
            else
            {
                freshCol = isWeakSpot ? new Color(1.24f, 1.04f, 0.98f, 1f) : new Color(1.12f, 0.98f, 0.98f, 1f);
                coagulatedCol = new Color(0.62f, 0.48f, 0.50f, 1f);
            }

            for (int r = 0; r < rayCount; r++)
            {
                Vector3 rayOrigin;
                Vector3 rayDir;
                Vector3 sprayDirXZ;

                if (r == 0)
                {
                    // Base puddle directly under struck enemy/wound
                    sprayDirXZ = flatDir;
                    rayOrigin = hitPos + Vector3.up * 0.85f + flatDir * Random.Range(0.05f, 0.45f);
                    rayDir = Vector3.down;
                }
                else
                {
                    float yawSpread = Random.Range(-48f, 48f);
                    sprayDirXZ = (Quaternion.Euler(0f, yawSpread, 0f) * flatDir).normalized;
                    float forwardTilt = Random.Range(0.20f, 0.88f) * Mathf.Clamp(effIntensity, 0.65f, 1.35f);
                    rayOrigin = hitPos + Vector3.up * 0.90f + sprayDirXZ * Random.Range(0.15f, 0.65f);
                    rayDir = (Vector3.down + sprayDirXZ * forwardTilt).normalized;
                }

                if (TryFindWalkableDecalSurface(rayOrigin, rayDir, 13.5f, out RaycastHit floorHit))
                {
                    int texIdx = ChooseTextureIndexForRay(r, rayCount);
                    PlaceDecal(floorHit, sprayDirXZ, texIdx, effIntensity, isWeakSpot, freshCol, coagulatedCol, useLumaRemap ? 1f : 0f);
                }
            }

            // 3. Wet blood splatter droplet SFX (cooldown-protected)
            CombatSfxBank.PlayAt(
                CombatSfxId.BloodSplatter,
                hitPos,
                Mathf.Clamp(0.32f + effIntensity * 0.16f, 0.28f, 0.62f),
                0.88f,
                1.14f,
                0.45f,
                0.075f
            );
        }

        private int ChooseTextureIndexForRay(int rayIndex, int totalRays)
        {
            if (rayIndex == 0)
            {
                return totalRays >= 5 ? 3 : 0; // Core arterial burst or irregular pool
            }
            if (rayIndex == 1 || (rayIndex == 2 && Random.value < 0.65f))
            {
                return 1; // Directional slash spray streak
            }
            return Random.value < 0.55f ? 2 : 0; // Scattered droplets or irregular pool
        }

        private void EmitDirectionalSprayParticles(
            Vector3 hitPos,
            Vector3 baseDir,
            float effIntensity,
            Color tintVal,
            bool isWeakSpot)
        {
            if (_sprayParticles == null) return;

            int count = Mathf.Clamp(Mathf.RoundToInt(18f + effIntensity * 14f + (isWeakSpot ? 8f : 0f)), 18, 44);
            float speedMult = Mathf.Clamp(0.85f + effIntensity * 0.35f, 0.8f, 1.65f);

            Color brightCol = new Color(
                Mathf.Clamp01(tintVal.r * 1.18f + 0.08f),
                Mathf.Clamp01(tintVal.g * 1.10f),
                Mathf.Clamp01(tintVal.b * 1.10f),
                1f);
            Color darkCol = new Color(
                Mathf.Clamp01(tintVal.r * 0.48f),
                Mathf.Clamp01(tintVal.g * 0.35f),
                Mathf.Clamp01(tintVal.b * 0.35f),
                1f);

            Vector3 primaryJet = (baseDir * 0.88f + Vector3.up * 0.44f).normalized;

            for (int i = 0; i < count; i++)
            {
                bool isPrimaryGush = i < (count * 7) / 10;
                Vector3 dir;
                float speed;

                if (isPrimaryGush)
                {
                    Vector3 jitter = Random.insideUnitSphere * 0.38f;
                    dir = (primaryJet + jitter).normalized;
                    if (dir.y < 0.06f) dir.y = Mathf.Abs(dir.y) + 0.14f;
                    speed = Random.Range(4.8f, 10.6f) * speedMult;
                }
                else
                {
                    // Wound backsplash / aerosol droplets
                    Vector3 backDir = (-baseDir * 0.40f + Vector3.up * 0.55f + Random.insideUnitSphere * 0.65f).normalized;
                    dir = backDir;
                    speed = Random.Range(2.4f, 6.2f) * speedMult;
                }

                ParticleSystem.EmitParams ep = new ParticleSystem.EmitParams
                {
                    position = hitPos + Random.insideUnitSphere * 0.18f,
                    velocity = dir * speed,
                    startSize = Random.Range(isPrimaryGush ? 0.10f : 0.08f, isWeakSpot ? 0.28f : 0.23f),
                    startLifetime = Random.Range(0.28f, 0.62f),
                    startColor = Color.Lerp(darkCol, brightCol, Random.value)
                };
                _sprayParticles.Emit(ep, 1);
            }
        }

        private bool TryFindWalkableDecalSurface(Vector3 origin, Vector3 direction, float maxDist, out RaycastHit bestHit)
        {
            bestHit = default;
            int count = Physics.RaycastNonAlloc(origin, direction, s_rayHits, maxDist, ~0, QueryTriggerInteraction.Ignore);
            if (count <= 0) return false;

            float bestDist = float.MaxValue;
            bool found = false;

            for (int i = 0; i < count; i++)
            {
                RaycastHit h = s_rayHits[i];
                if (h.collider == null) continue;
                if (!IsValidDecalSurface(h.collider, h.normal)) continue;

                if (h.distance < bestDist)
                {
                    bestDist = h.distance;
                    bestHit = h;
                    found = true;
                }
            }
            return found;
        }

        private static bool IsValidDecalSurface(Collider col, Vector3 normal)
        {
            if (col == null || col.isTrigger) return false;
            if (normal.y < 0.35f) return false;

            // Ignore dynamic actors & projectiles
            if (col.GetComponentInParent<SimplePlayerController>() != null) return false;
            if (col.CompareTag("Player")) return false;
            if (col.GetComponentInParent<TeniaBossHealth>() != null) return false;
            if (col.GetComponentInParent<TeniaHitboxRelay>() != null) return false;
            if (col.GetComponentInParent<TeniaBossController>() != null) return false;
            if (col.GetComponentInParent<ImmuneEnemyBase>() != null) return false;
            if (col.GetComponentInParent<CrossRazorBoomerangProjectile>() != null) return false;
            if (col.GetComponentInParent<TeniaAcidProjectile>() != null) return false;
            if (col.GetComponentInParent<TeniaAcidPuddle>() != null) return false;
            if (col.GetComponentInParent<TeniaAcidTelegraph>() != null) return false;
            if (col.GetComponentInParent<TeniaSoulOrb>() != null) return false;

            // Ignore acid lakes, channels, waterfalls, and portal vortices so blood never floats over chasms
            string n = col.name;
            if (ContainsIgnoreCase(n, "Acid_Channel") ||
                ContainsIgnoreCase(n, "Acid_Lake") ||
                ContainsIgnoreCase(n, "AcidLake") ||
                ContainsIgnoreCase(n, "Abyss_Acid") ||
                ContainsIgnoreCase(n, "Waterfall") ||
                ContainsIgnoreCase(n, "Acid_Foam") ||
                ContainsIgnoreCase(n, "Portal_Vortex"))
            {
                return false;
            }

            Transform root = col.transform.root;
            if (root != null && root != col.transform)
            {
                string rn = root.name;
                if (ContainsIgnoreCase(rn, "Acid_Lake") ||
                    ContainsIgnoreCase(rn, "Acid_Channel") ||
                    ContainsIgnoreCase(rn, "Boss_Tenia"))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool ContainsIgnoreCase(string source, string needle)
        {
            return !string.IsNullOrEmpty(source) &&
                   source.IndexOf(needle, System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void PlaceDecal(
            RaycastHit hit,
            Vector3 sprayDirXZ,
            int texIndex,
            float effIntensity,
            bool isWeakSpot,
            Color freshCol,
            Color coagulatedCol,
            float tintRemap)
        {
            if (_pool == null || _pool.Length == 0) return;

            int slotIdx = _ringCursor;
            _ringCursor = (_ringCursor + 1) % _pool.Length;

            DecalSlot slot = _pool[slotIdx];
            if (slot.Go == null) return;

            // Slight staggered polygon offset along hit.normal to completely prevent Z-fighting
            float normalLift = 0.022f + (slotIdx % 16) * 0.0012f;
            slot.Tr.position = hit.point + hit.normal * normalLift;

            // Orient along hit surface; align directional streak (texIndex == 1) with spray trajectory
            Quaternion surfaceRot;
            if (texIndex == 1)
            {
                Vector3 projDir = Vector3.ProjectOnPlane(sprayDirXZ, hit.normal);
                if (projDir.sqrMagnitude < 0.001f)
                {
                    projDir = Vector3.ProjectOnPlane(Vector3.forward, hit.normal);
                }
                projDir.Normalize();
                surfaceRot = Quaternion.LookRotation(projDir, hit.normal) * Quaternion.Euler(0f, Random.Range(-16f, 16f), 0f);
            }
            else
            {
                surfaceRot = Quaternion.FromToRotation(Vector3.up, hit.normal) * Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            }
            slot.Tr.rotation = surfaceRot;

            float baseSize = Mathf.Lerp(0.82f, 1.85f, Mathf.Clamp01((effIntensity - 0.35f) / 1.45f));
            baseSize *= Random.Range(0.82f, 1.24f) * (isWeakSpot ? 1.15f : 1.0f) * globalDecalScaleMultiplier;
            float sizeX = Mathf.Clamp(texIndex == 1 ? baseSize * 0.84f : baseSize * Random.Range(0.90f, 1.12f), 0.75f, 2.40f);
            float sizeZ = Mathf.Clamp(texIndex == 1 ? baseSize * 1.28f : baseSize * Random.Range(0.90f, 1.12f), 0.75f, 2.40f);
            slot.TargetScale = new Vector3(sizeX, 1f, sizeZ);
            slot.Tr.localScale = slot.TargetScale * 0.42f;

            slot.Renderer.sharedMaterial = _decalMaterials[Mathf.Clamp(texIndex, 0, _decalMaterials.Length - 1)];
            slot.FreshColor = freshCol;
            slot.CoagulatedColor = coagulatedCol;
            slot.TintRemap = _usingFallbackShader ? 0f : tintRemap;
            slot.Age = 0f;
            slot.Active = true;
            slot.Animating = true;

            ApplyColorToSlot(ref slot, freshCol);

            if (!slot.Go.activeSelf)
            {
                slot.Go.SetActive(true);
            }

            _pool[slotIdx] = slot;
            if (!_animatingIndices.Contains(slotIdx))
            {
                _animatingIndices.Add(slotIdx);
            }
        }

        private void ApplyColorToSlot(ref DecalSlot slot, Color col)
        {
            if (slot.Renderer == null || slot.Mpb == null) return;
            slot.Mpb.SetColor(PropBaseColor, col);
            slot.Mpb.SetColor(PropColor, col);
            if (!_usingFallbackShader)
            {
                slot.Mpb.SetFloat(PropTintRemap, slot.TintRemap);
            }
            slot.Renderer.SetPropertyBlock(slot.Mpb);
        }

        private void InitializePool()
        {
            int capacity = Mathf.Clamp(maxPoolSize, 32, 300);
            if (_pool != null && _pool.Length == capacity) return;

            Transform existingRoot = transform.Find("BloodDecalPool");
            if (existingRoot != null)
            {
                _poolRoot = existingRoot;
            }
            else
            {
                GameObject rootGo = new GameObject("BloodDecalPool");
                rootGo.transform.SetParent(transform, false);
                _poolRoot = rootGo.transform;
            }

            _pool = new DecalSlot[capacity];
            _ringCursor = 0;
            _animatingIndices.Clear();

            for (int i = 0; i < capacity; i++)
            {
                GameObject go = new GameObject($"BloodDecal_{i:000}");
                go.transform.SetParent(_poolRoot, false);
                go.SetActive(false);

                MeshFilter mf = go.AddComponent<MeshFilter>();
                mf.sharedMesh = _xzQuadMesh;

                MeshRenderer mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = _decalMaterials[0];
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveShadows = false;

                _pool[i] = new DecalSlot
                {
                    Go = go,
                    Tr = go.transform,
                    Renderer = mr,
                    Mpb = new MaterialPropertyBlock(),
                    TargetScale = Vector3.one,
                    FreshColor = Color.white,
                    CoagulatedColor = new Color(0.6f, 0.5f, 0.5f, 1f),
                    Active = false,
                    Animating = false
                };
            }
        }

        private void BuildParticleSystem()
        {
            Transform existing = transform.Find("BloodSprayParticleSystem");
            GameObject go = existing != null ? existing.gameObject : new GameObject("BloodSprayParticleSystem");
            if (existing == null)
            {
                go.transform.SetParent(transform, false);
            }

            _sprayParticles = go.GetComponent<ParticleSystem>();
            if (_sprayParticles == null)
            {
                _sprayParticles = go.AddComponent<ParticleSystem>();
            }
            _sprayParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = _sprayParticles.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = 1.0f;
            main.maxParticles = 450;
            main.gravityModifier = 1.8f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var emission = _sprayParticles.emission;
            emission.enabled = false;

            var shape = _sprayParticles.shape;
            shape.enabled = false;

            var sizeOverLife = _sprayParticles.sizeOverLifetime;
            sizeOverLife.enabled = true;
            sizeOverLife.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 1.0f),
                new Keyframe(0.65f, 0.72f),
                new Keyframe(1.0f, 0.18f)
            ));

            BuildDropletParticleTexture();
            Shader sh = Shader.Find("MapaEstomago/URP/PS1_PixelParticle");
            if (sh == null) sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (sh == null) sh = Shader.Find("Sprites/Default");

            if (sh != null)
            {
                _particleMaterial = new Material(sh);
                _particleMaterial.hideFlags = HideFlags.DontSave;
                if (_particleMaterial.HasProperty(PropBaseMap)) _particleMaterial.SetTexture(PropBaseMap, _dropletParticleTex);
                if (_particleMaterial.HasProperty(PropMainTex)) _particleMaterial.SetTexture(PropMainTex, _dropletParticleTex);
                if (_particleMaterial.HasProperty(PropBaseColor)) _particleMaterial.SetColor(PropBaseColor, Color.white);
                if (_particleMaterial.HasProperty(PropColor)) _particleMaterial.SetColor(PropColor, Color.white);
                if (_particleMaterial.HasProperty(PropEmissionBoost)) _particleMaterial.SetFloat(PropEmissionBoost, 1.15f);
                if (_particleMaterial.HasProperty(PropCutoff)) _particleMaterial.SetFloat(PropCutoff, 0.40f);
            }

            ParticleSystemRenderer pr = go.GetComponent<ParticleSystemRenderer>();
            if (pr != null && _particleMaterial != null)
            {
                pr.renderMode = ParticleSystemRenderMode.Billboard;
                pr.shadowCastingMode = ShadowCastingMode.Off;
                pr.receiveShadows = false;
                pr.sharedMaterial = _particleMaterial;
            }
        }

        private void BuildDecalMaterials()
        {
            Shader sh = Shader.Find("MapaEstomago/URP/PS1_BloodDecal");
            _usingFallbackShader = false;

            if (sh == null)
            {
                sh = Shader.Find("MapaEstomago/URP/PS1_PixelParticle");
                _usingFallbackShader = true;
            }
            if (sh == null)
            {
                sh = Shader.Find("Universal Render Pipeline/Unlit");
                _usingFallbackShader = true;
            }
            if (sh == null)
            {
                sh = Shader.Find("Sprites/Default");
                _usingFallbackShader = true;
            }

            _decalMaterials = new Material[4];
            for (int i = 0; i < 4; i++)
            {
                Material mat = new Material(sh);
                mat.hideFlags = HideFlags.DontSave;
                Texture2D tex = splatTextures[i];

                if (mat.HasProperty(PropBaseMap)) mat.SetTexture(PropBaseMap, tex);
                if (mat.HasProperty(PropMainTex)) mat.SetTexture(PropMainTex, tex);
                if (mat.HasProperty(PropBaseColor)) mat.SetColor(PropBaseColor, Color.white);
                if (mat.HasProperty(PropColor)) mat.SetColor(PropColor, Color.white);
                if (mat.HasProperty(PropCutoff)) mat.SetFloat(PropCutoff, 0.42f);
                if (mat.HasProperty(PropEmissionBoost)) mat.SetFloat(PropEmissionBoost, 1.0f);
                if (mat.HasProperty(PropTintRemap)) mat.SetFloat(PropTintRemap, 0f);

                if (mat.HasProperty("_AlphaClip"))
                {
                    mat.SetFloat("_AlphaClip", 1f);
                    mat.EnableKeyword("_ALPHATEST_ON");
                }
                if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);
                if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", (float)CullMode.Off);

                _decalMaterials[i] = mat;
            }
        }

        private void BuildXzQuadMesh()
        {
            if (_xzQuadMesh != null) return;
            _xzQuadMesh = new Mesh { name = "PS1_BloodDecal_XZQuad" };
            _xzQuadMesh.vertices = new[]
            {
                new Vector3(-0.5f, 0f, -0.5f),
                new Vector3( 0.5f, 0f, -0.5f),
                new Vector3( 0.5f, 0f,  0.5f),
                new Vector3(-0.5f, 0f,  0.5f)
            };
            _xzQuadMesh.normals = new[]
            {
                Vector3.up, Vector3.up, Vector3.up, Vector3.up
            };
            _xzQuadMesh.uv = new[]
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(1f, 1f),
                new Vector2(0f, 1f)
            };
            // Clockwise from +Y looking down -> front normal +Y
            _xzQuadMesh.triangles = new[] { 0, 3, 2, 0, 2, 1 };
            _xzQuadMesh.RecalculateBounds();
        }

        private void BuildDropletParticleTexture()
        {
            if (_dropletParticleTex != null) return;
            const int size = 16;
            _dropletParticleTex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "PS1_BloodDroplet_Fallback",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };

            Color32[] pixels = new Color32[size * size];
            Vector2 center = new Vector2(7.5f, 7.5f);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - center.x) / 6.2f;
                    float dy = (y - center.y) / 6.8f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float n = (((x * 73856093) ^ (y * 19349663)) & 7) / 18f;
                    bool solid = (d + n * 0.25f) < 0.85f;
                    byte v = (byte)Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(255f, 165f, d)), 150, 255);
                    pixels[y * size + x] = solid ? new Color32(v, v, v, 255) : new Color32(0, 0, 0, 0);
                }
            }
            _dropletParticleTex.SetPixels32(pixels);
            _dropletParticleTex.Apply(false, true);
        }

        private void EnsureTexturesValid()
        {
            if (splatTextures == null || splatTextures.Length != 4)
            {
                splatTextures = new Texture2D[4];
            }

            for (int i = 0; i < 4; i++)
            {
                if (splatTextures[i] == null)
                {
                    splatTextures[i] = BuildFallbackSplatTexture(i);
                }
            }
        }

        private static Texture2D BuildFallbackSplatTexture(int variant)
        {
            const int res = 64;
            Texture2D tex = new Texture2D(res, res, TextureFormat.RGBA32, false)
            {
                name = $"Fallback_BloodSplat_{variant + 1}",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };

            Color32[] px = new Color32[res * res];
            System.Random rng = new System.Random(700 + variant * 131);
            for (int y = 0; y < res; y++)
            {
                for (int x = 0; x < res; x++)
                {
                    float u = (x / (float)(res - 1)) * 2f - 1f;
                    float v = (y / (float)(res - 1)) * 2f - 1f;
                    float dist = Mathf.Sqrt(u * u + v * v);
                    float ang = Mathf.Atan2(v, u);
                    float lobe = 0.62f + 0.18f * Mathf.Sin(ang * (4f + variant) + variant);
                    float dither = (((x ^ y) & 1) == 0 ? 0.04f : -0.04f);
                    bool hit = (dist + dither) < lobe && Mathf.Max(Mathf.Abs(u), Mathf.Abs(v)) < 0.92f;
                    if (!hit && rng.NextDouble() < 0.015 && dist < 0.88f) hit = true;

                    if (hit)
                    {
                        byte r = (byte)Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(190f, 70f, dist)), 55, 220);
                        px[y * res + x] = new Color32(r, 12, 16, 255);
                    }
                    else
                    {
                        px[y * res + x] = new Color32(0, 0, 0, 0);
                    }
                }
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return tex;
        }
    }
}
