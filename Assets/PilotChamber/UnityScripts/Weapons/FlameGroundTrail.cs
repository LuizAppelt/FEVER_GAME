using System.Collections.Generic;
using BossTenia;
using StomachAbyss.Combat;
using UnityEngine;
using UnityEngine.Rendering;

namespace MapaEstomago
{
    /// <summary>
    /// Rastro de fogo no chao deixado ao longo do trajeto da Estocada de Fogo (Lamina Ardente).
    /// - Faz raycast limpo no piso (SampleGroundY), ignorando Player e inimigos.
    /// - Cria disco octagonal PS1 de brasas incandescentes (ShadowCastingMode.Off), particulas de fogo e luz laranja suave.
    /// - Causa dano continuo e aplica/renova BurnStatusEffect nos inimigos que pisarem dentro.
    /// </summary>
    public class FlameGroundTrail : MonoBehaviour
    {
        private const float GroundOffsetY = 0.055f;
        private const float TickInterval = 0.40f;
        private const float FadeWindowSeconds = 0.65f;
        private const float CrossPatchTickCooldown = 0.32f;

        private static Mesh s_octagonDiscMesh;
        private static Mesh s_octagonRimMesh;
        private static Texture2D s_moltenEmberTex;
        private static Material s_sharedEmberMat;
        private static Material s_sharedRimMat;
        private static readonly Dictionary<GameObject, float> s_lastPatchHitTimeByEntity = new Dictionary<GameObject, float>(32);

        private GameObject _owner;
        private float _radius = 1.15f;
        private float _duration = 3.2f;
        private float _dps = 16f;
        private Material _particleMat;

        private float _elapsed;
        private float _tickTimer;
        private float _baseLightIntensity = 0.92f;

        private Transform _visualRoot;
        private ParticleSystem _fireParticles;
        private Light _glowLight;
        private readonly HashSet<GameObject> _tickHitRoots = new HashSet<GameObject>();

        /// <summary>
        /// Cria uma placa de chao em chamas na posicao informada (encaixada automaticamente no piso).
        /// </summary>
        public static FlameGroundTrail SpawnPatch(
            Vector3 worldPos,
            GameObject owner,
            float radius = 1.15f,
            float duration = 3.2f,
            float dps = 16f,
            Material emberMat = null,
            Material particleMat = null)
        {
            float groundY = SampleGroundY(worldPos, owner, GroundOffsetY);

            GameObject go = new GameObject("Ardente_FlameGroundPatch");
            go.transform.position = new Vector3(worldPos.x, groundY, worldPos.z);
            go.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

            FlameGroundTrail patch = go.AddComponent<FlameGroundTrail>();
            patch.Initialize(owner, radius, duration, dps, emberMat, particleMat);
            return patch;
        }

        /// <summary>
        /// Faz raycast para baixo ignorando jogador, inimigos e triggers para encontrar a altura real do piso.
        /// </summary>
        public static float SampleGroundY(Vector3 worldPos, GameObject owner = null, float fallbackOffset = 0f)
        {
            Ray ray = new Ray(new Vector3(worldPos.x, worldPos.y + 4.5f, worldPos.z), Vector3.down);
            RaycastHit[] hits = Physics.RaycastAll(ray, 14.0f, ~0, QueryTriggerInteraction.Ignore);

            float bestDist = float.MaxValue;
            float bestY = worldPos.y;
            bool found = false;

            for (int i = 0; i < hits.Length; i++)
            {
                Collider c = hits[i].collider;
                if (c == null) continue;
                if (owner != null && (c.gameObject == owner || c.transform.IsChildOf(owner.transform))) continue;
                if (c.GetComponentInParent<SimplePlayerController>() != null) continue;
                if (c.GetComponentInParent<IAcidDamageable>() != null ||
                    c.GetComponentInParent<WhipTrainingDummy>() != null ||
                    c.GetComponentInParent<TeniaHitboxRelay>() != null ||
                    c.GetComponentInParent<AcidBubblePopper>() != null ||
                    c.GetComponentInParent<UlcerEruption>() != null)
                {
                    continue;
                }

                if (hits[i].distance < bestDist)
                {
                    bestDist = hits[i].distance;
                    bestY = hits[i].point.y;
                    found = true;
                }
            }

            return (found ? bestY : worldPos.y) + fallbackOffset;
        }

        private void Initialize(
            GameObject owner,
            float radius,
            float duration,
            float dps,
            Material emberMat,
            Material particleMat)
        {
            _owner = owner;
            _radius = Mathf.Max(0.45f, radius);
            _duration = Mathf.Max(0.40f, duration);
            _dps = Mathf.Max(0f, dps);
            _particleMat = BurnStatusEffect.GetOrCreateSharedFireParticleMaterial(particleMat);
            _tickTimer = TickInterval;

            EnsureCachedAssets();

            Material discMat = emberMat != null ? emberMat : s_sharedEmberMat;
            Material rimMat = s_sharedRimMat;

            _visualRoot = new GameObject("EmberVisuals").transform;
            _visualRoot.SetParent(transform, false);

            CreateMeshLayer("MoltenEmberDisc", s_octagonDiscMesh, discMat, new Vector3(_radius, 1f, _radius), 0f);
            CreateMeshLayer("MoltenEmberRim", s_octagonRimMesh, rimMat, new Vector3(_radius, 1f, _radius), 0.012f);

            CreateFireParticles();
            CreateWarmGlowLight();
        }

        private void CreateMeshLayer(string layerName, Mesh mesh, Material mat, Vector3 scale, float localY)
        {
            GameObject child = new GameObject(layerName);
            child.transform.SetParent(_visualRoot, false);
            child.transform.localPosition = new Vector3(0f, localY, 0f);
            child.transform.localScale = scale;

            MeshFilter mf = child.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;

            MeshRenderer mr = child.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }

        private void CreateFireParticles()
        {
            GameObject fxObj = new GameObject("TrailFireFX");
            fxObj.transform.SetParent(transform, false);
            fxObj.transform.localPosition = new Vector3(0f, 0.05f, 0f);

            _fireParticles = fxObj.AddComponent<ParticleSystem>();
            var main = _fireParticles.main;
            main.playOnAwake = true;
            main.loop = true;
            main.duration = 1.0f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.28f, 0.58f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.85f, 2.0f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.24f);
            main.gravityModifier = -0.32f;
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(1.0f, 0.78f, 0.20f, 1f),
                new Color(1.0f, 0.32f, 0.05f, 1f)
            );
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var emission = _fireParticles.emission;
            emission.enabled = true;
            emission.rateOverTime = 14f;

            var shape = _fireParticles.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = _radius * 0.76f;
            shape.rotation = new Vector3(-90f, 0f, 0f);

            var sizeOverLife = _fireParticles.sizeOverLifetime;
            sizeOverLife.enabled = true;
            sizeOverLife.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.85f, 1f, 0.10f));

            ParticleSystemRenderer psRend = fxObj.GetComponent<ParticleSystemRenderer>();
            if (psRend != null)
            {
                psRend.renderMode = ParticleSystemRenderMode.Billboard;
                psRend.sharedMaterial = _particleMat;
            }

            _fireParticles.Emit(6);
        }

        private void CreateWarmGlowLight()
        {
            GameObject lightGo = new GameObject("EmberGlowLight");
            lightGo.transform.SetParent(transform, false);
            lightGo.transform.localPosition = new Vector3(0f, 0.35f, 0f);

            _glowLight = lightGo.AddComponent<Light>();
            _glowLight.type = LightType.Point;
            _glowLight.color = new Color(1.0f, 0.44f, 0.10f, 1f);
            _glowLight.intensity = _baseLightIntensity;
            _glowLight.range = Mathf.Max(2.2f, _radius * 1.85f);
            _glowLight.shadows = LightShadows.None;
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            _elapsed += dt;

            float remaining = _duration - _elapsed;
            if (remaining <= 0f)
            {
                Destroy(gameObject);
                return;
            }

            if (remaining <= FadeWindowSeconds)
            {
                float fadeT = Mathf.Clamp01(remaining / FadeWindowSeconds);
                if (_visualRoot != null)
                {
                    _visualRoot.localScale = Vector3.one * fadeT;
                }
                if (_glowLight != null)
                {
                    _glowLight.intensity = _baseLightIntensity * fadeT;
                }
                if (_fireParticles != null && remaining <= 0.35f && _fireParticles.isEmitting)
                {
                    _fireParticles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                }
            }
            else
            {
                float pulse = 1f + Mathf.Sin(Time.time * 9.5f + transform.position.x) * 0.03f;
                if (_visualRoot != null)
                {
                    _visualRoot.localScale = new Vector3(pulse, 1f, pulse);
                }
                if (_glowLight != null)
                {
                    _glowLight.intensity = _baseLightIntensity + 0.18f * Mathf.Sin(Time.time * 14f + transform.position.z);
                }
            }

            _tickTimer -= dt;
            if (_tickTimer <= 0f)
            {
                _tickTimer += TickInterval;
                ApplyTrailBurnTick();
            }
        }

        private void ApplyTrailBurnTick()
        {
            float activeScale = _visualRoot != null ? _visualRoot.localScale.x : 1f;
            float activeRadius = _radius * Mathf.Max(0.2f, activeScale);
            float tickDamage = _dps * TickInterval;

            _tickHitRoots.Clear();
            Vector3 checkCenter = transform.position + Vector3.up * 0.35f;
            Collider[] cols = Physics.OverlapSphere(checkCenter, activeRadius, ~0, QueryTriggerInteraction.Collide);
            if (cols == null) return;

            float now = Time.time;

            for (int i = 0; i < cols.Length; i++)
            {
                Collider c = cols[i];
                if (c == null) continue;
                if (IsPlayerOrOwner(c.transform)) continue;

                IAcidDamageable dmg = c.GetComponentInParent<IAcidDamageable>();
                if (dmg == null) continue;

                GameObject targetRoot = ResolveTargetRoot(dmg);
                if (targetRoot == null || !_tickHitRoots.Add(targetRoot)) continue;

                // Evita multiplicacao de DPS se o inimigo estiver tocando duas placas vizinhas do mesmo rastro
                if (s_lastPatchHitTimeByEntity.TryGetValue(targetRoot, out float lastTime) &&
                    (now - lastTime) < CrossPatchTickCooldown)
                {
                    continue;
                }
                if (s_lastPatchHitTimeByEntity.Count > 64)
                {
                    s_lastPatchHitTimeByEntity.Clear();
                }
                s_lastPatchHitTimeByEntity[targetRoot] = now;

                if (tickDamage > 0f)
                {
                    BloodSplatterSystem.BeginAttackBurstContext(Vector3.up, checkCenter, 0.42f, false);
                    try
                    {
                        dmg.TakeAcidDamage(tickDamage);
                    }
                    finally
                    {
                        BloodSplatterSystem.EndAttackBurstContext();
                    }
                }

                BurnStatusEffect burn = targetRoot.GetComponent<BurnStatusEffect>();
                if (burn == null) burn = targetRoot.AddComponent<BurnStatusEffect>();
                burn.ApplyBurn(_owner, 4.0f, 18f, _particleMat);
            }
        }

        private bool IsPlayerOrOwner(Transform target)
        {
            if (target == null) return true;
            if (_owner != null && (target.gameObject == _owner || target.IsChildOf(_owner.transform))) return true;
            if (target.GetComponentInParent<SimplePlayerController>() != null) return true;
            try
            {
                if (target.CompareTag("Player")) return true;
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

        private static void EnsureCachedAssets()
        {
            if (s_octagonDiscMesh == null) s_octagonDiscMesh = BuildOctagonDiscMesh();
            if (s_octagonRimMesh == null) s_octagonRimMesh = BuildOctagonRimMesh(0.82f, 1.02f);
            if (s_moltenEmberTex == null) s_moltenEmberTex = BuildMoltenEmberTexture();

            if (s_sharedEmberMat == null)
            {
                s_sharedEmberMat = CreateMoltenSurfaceMaterial(
                    new Color(0.95f, 0.55f, 0.28f, 1f),
                    new Color(1.55f, 0.48f, 0.06f, 1f),
                    0.004f,
                    3.2f
                );
            }
            if (s_sharedRimMat == null)
            {
                s_sharedRimMat = CreateMoltenSurfaceMaterial(
                    new Color(0.55f, 0.14f, 0.05f, 1f),
                    new Color(0.95f, 0.22f, 0.02f, 1f),
                    0.003f,
                    2.4f
                );
            }
        }

        private static Material CreateMoltenSurfaceMaterial(Color baseColor, Color emissionColor, float waveAmp, float waveSpeed)
        {
            Shader shader = Shader.Find("MapaEstomago/URP/PS1_GastricAcid");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Standard");

            Material mat = new Material(shader);
            mat.hideFlags = HideFlags.DontSave;

            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", s_moltenEmberTex);
            if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", s_moltenEmberTex);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", baseColor);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", baseColor);
            if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", emissionColor);
            if (mat.HasProperty("_WaveAmp")) mat.SetFloat("_WaveAmp", waveAmp);
            if (mat.HasProperty("_WaveSpeed")) mat.SetFloat("_WaveSpeed", waveSpeed);
            if (mat.HasProperty("_VertexSnap")) mat.SetFloat("_VertexSnap", 160f);
            if (mat.HasProperty("_PulseSpeed")) mat.SetFloat("_PulseSpeed", 4.5f);
            if (mat.HasProperty("_ScrollSpeedA")) mat.SetVector("_ScrollSpeedA", new Vector4(0.09f, 0.04f, 0f, 0f));
            if (mat.HasProperty("_ScrollSpeedB")) mat.SetVector("_ScrollSpeedB", new Vector4(-0.07f, 0.08f, 0f, 0f));

            return mat;
        }

        private static Texture2D BuildMoltenEmberTexture()
        {
            const int size = 32;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.hideFlags = HideFlags.DontSave;
            tex.filterMode = FilterMode.Point;
            tex.wrapMode = TextureWrapMode.Repeat;

            Color[] pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = x / (float)size;
                    float v = y / (float)size;
                    float wave = Mathf.Sin(u * Mathf.PI * 6f) * Mathf.Cos(v * Mathf.PI * 6f);
                    float crack = Mathf.Abs(Mathf.Sin((u + v) * Mathf.PI * 4f + wave * 0.8f));
                    if (crack < 0.24f)
                    {
                        pixels[y * size + x] = new Color(1.0f, 0.86f, 0.30f, 1f);
                    }
                    else if (crack < 0.52f)
                    {
                        pixels[y * size + x] = new Color(0.95f, 0.34f, 0.06f, 1f);
                    }
                    else
                    {
                        pixels[y * size + x] = new Color(0.28f, 0.05f, 0.03f, 1f);
                    }
                }
            }

            tex.SetPixels(pixels);
            tex.Apply(false, false);
            return tex;
        }

        private static Mesh BuildOctagonDiscMesh()
        {
            const int sides = 8;
            Vector3[] verts = new Vector3[sides + 1];
            Vector2[] uvs = new Vector2[sides + 1];
            int[] tris = new int[sides * 3];

            verts[0] = Vector3.zero;
            uvs[0] = new Vector2(0.5f, 0.5f);

            for (int i = 0; i < sides; i++)
            {
                float ang = (i / (float)sides) * Mathf.PI * 2f;
                float c = Mathf.Cos(ang);
                float s = Mathf.Sin(ang);
                verts[i + 1] = new Vector3(c, 0f, s);
                uvs[i + 1] = new Vector2(0.5f + c * 0.5f, 0.5f + s * 0.5f);

                int next = (i + 1) % sides;
                tris[i * 3 + 0] = 0;
                tris[i * 3 + 1] = next + 1;
                tris[i * 3 + 2] = i + 1;
            }

            Mesh mesh = new Mesh { name = "FlameTrail_OctagonDisc", hideFlags = HideFlags.DontSave };
            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            return mesh;
        }

        private static Mesh BuildOctagonRimMesh(float innerR, float outerR)
        {
            const int sides = 8;
            Vector3[] verts = new Vector3[sides * 2];
            Vector2[] uvs = new Vector2[sides * 2];
            int[] tris = new int[sides * 6];

            for (int i = 0; i < sides; i++)
            {
                float ang = (i / (float)sides) * Mathf.PI * 2f;
                float c = Mathf.Cos(ang);
                float s = Mathf.Sin(ang);

                verts[i * 2 + 0] = new Vector3(c * innerR, 0f, s * innerR);
                verts[i * 2 + 1] = new Vector3(c * outerR, 0f, s * outerR);
                uvs[i * 2 + 0] = new Vector2(0.5f + c * 0.4f, 0.5f + s * 0.4f);
                uvs[i * 2 + 1] = new Vector2(0.5f + c * 0.5f, 0.5f + s * 0.5f);

                int next = (i + 1) % sides;
                int b = i * 6;
                tris[b + 0] = i * 2 + 0;
                tris[b + 1] = next * 2 + 1;
                tris[b + 2] = i * 2 + 1;
                tris[b + 3] = i * 2 + 0;
                tris[b + 4] = next * 2 + 0;
                tris[b + 5] = next * 2 + 1;
            }

            Mesh mesh = new Mesh { name = "FlameTrail_OctagonRim", hideFlags = HideFlags.DontSave };
            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            return mesh;
        }
    }
}
