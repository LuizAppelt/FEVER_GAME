using System;
using System.Collections.Generic;
using MapaEstomago;
using UnityEngine;
using UnityEngine.Rendering;

namespace BossTenia
{
    /// <summary>
    /// Procedural PS1-style simmering acid puddle that deals periodic tick damage
    /// to IAcidDamageable targets (excluding Boss Tenia) and shrinks away at end of life.
    /// </summary>
    public class TeniaAcidPuddle : MonoBehaviour
    {
        private const float GroundOffsetY = 0.085f;
        private const float TickInterval = 0.45f;
        private const float ShrinkWindowSeconds = 1.0f;
        private const float PlayerFallbackRadiusPadding = 0.35f;
        private const float PlayerFallbackMaxVerticalDelta = 2.5f;

        private static readonly Color AcidBaseColor = new Color(0.82f, 1.0f, 0.68f, 1f);
        private static readonly Color AcidEmissionColor = new Color(0.35f, 1.45f, 0.12f, 1f);
        private static readonly Color RimBaseColor = new Color(0.45f, 0.85f, 0.25f, 1f);
        private static readonly Color RimEmissionColor = new Color(0.18f, 0.95f, 0.06f, 1f);

        private static Mesh s_octagonDiscMesh;
        private static Mesh s_octagonRimMesh;
        private static Texture2D s_acidSurfaceTexture;
        private static Texture2D s_bubbleParticleTexture;

        private float _radius;
        private float _duration;
        private float _damagePerSecond;
        private Transform _ignoreOwnerRoot;

        private float _elapsed;
        private float _tickTimer;
        private bool _hasPlayerTagMissingWarning;

        private Material _discMaterial;
        private Material _rimMaterial;
        private Material _bubbleMaterial;
        private ParticleSystem _bubbleParticles;
        private Light _puddleLight;
        private readonly HashSet<IAcidDamageable> _tickDamaged = new HashSet<IAcidDamageable>();

        /// <summary>
        /// Spawns a simmering PS1 acid puddle at <paramref name="groundPos"/>.
        /// </summary>
        public static TeniaAcidPuddle Spawn(
            Vector3 groundPos,
            float radius,
            float duration,
            float damagePerSecond,
            Transform ignoreOwnerRoot = null)
        {
            GameObject go = new GameObject("TeniaAcidPuddle");
            go.transform.position = new Vector3(groundPos.x, groundPos.y + GroundOffsetY, groundPos.z);
            go.transform.rotation = Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f);

            TeniaAcidPuddle puddle = go.AddComponent<TeniaAcidPuddle>();
            puddle.Initialize(radius, duration, damagePerSecond, ignoreOwnerRoot);
            return puddle;
        }

        private void Initialize(float radius, float duration, float damagePerSecond, Transform ignoreOwnerRoot)
        {
            _radius = Mathf.Max(0.35f, radius);
            _duration = Mathf.Max(0.25f, duration);
            _damagePerSecond = Mathf.Max(0f, damagePerSecond);
            _ignoreOwnerRoot = ignoreOwnerRoot;
            _tickTimer = TickInterval;

            EnsureCachedAssets();

            _discMaterial = CreateAcidSurfaceMaterial(AcidBaseColor, AcidEmissionColor, 0.007f, 2.8f);
            _rimMaterial = CreateAcidSurfaceMaterial(RimBaseColor, RimEmissionColor, 0.004f, 2.0f);
            _bubbleMaterial = CreatePixelParticleMaterial();

            CreateMeshChild("PuddleDisc", s_octagonDiscMesh, _discMaterial, new Vector3(_radius, 1f, _radius), 0f);
            CreateMeshChild("PuddleOuterRing", s_octagonRimMesh, _rimMaterial, new Vector3(_radius, 1f, _radius), 0.022f);

            CreateBubbleParticles();
            CreatePuddleLight();
        }

        private void CreateMeshChild(string childName, Mesh mesh, Material mat, Vector3 localScale, float localY)
        {
            GameObject child = new GameObject(childName);
            child.transform.SetParent(transform, false);
            child.transform.localPosition = new Vector3(0f, localY, 0f);
            child.transform.localScale = localScale;

            MeshFilter mf = child.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;

            MeshRenderer mr = child.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = true;
        }

        private void CreateBubbleParticles()
        {
            GameObject fxObj = new GameObject("AcidBubbleFX");
            fxObj.transform.SetParent(transform, false);
            fxObj.transform.localPosition = new Vector3(0f, 0.04f, 0f);

            _bubbleParticles = fxObj.AddComponent<ParticleSystem>();
            var main = _bubbleParticles.main;
            main.playOnAwake = true;
            main.loop = true;
            main.duration = 1.0f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.90f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.55f, 1.35f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.10f, 0.22f);
            main.gravityModifier = -0.15f;
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.32f, 1.0f, 0.14f, 1f),
                new Color(0.78f, 1.0f, 0.35f, 1f));
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var emission = _bubbleParticles.emission;
            emission.enabled = true;
            emission.rateOverTime = Mathf.Clamp(_radius * 4.5f, 6f, 16f);

            var shape = _bubbleParticles.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 8f;
            shape.radius = _radius * 0.78f;
            shape.rotation = new Vector3(-90f, 0f, 0f);

            var sizeOverLife = _bubbleParticles.sizeOverLifetime;
            sizeOverLife.enabled = true;
            sizeOverLife.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.55f, 1f, 0.12f));

            ParticleSystemRenderer psRenderer = fxObj.GetComponent<ParticleSystemRenderer>();
            if (psRenderer != null)
            {
                psRenderer.renderMode = ParticleSystemRenderMode.Billboard;
                psRenderer.sharedMaterial = _bubbleMaterial;
            }
        }

        private void CreatePuddleLight()
        {
            GameObject lightGo = new GameObject("PuddleGlowLight");
            lightGo.transform.SetParent(transform, false);
            lightGo.transform.localPosition = new Vector3(0f, 0.45f, 0f);

            _puddleLight = lightGo.AddComponent<Light>();
            _puddleLight.type = LightType.Point;
            _puddleLight.color = new Color(0.38f, 1.0f, 0.15f);
            _puddleLight.intensity = 1.35f;
            _puddleLight.range = Mathf.Max(2.2f, _radius * 1.45f);
            _puddleLight.shadows = LightShadows.None;
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

            // Smooth shrink during the final 1.0s of lifetime.
            if (remaining <= ShrinkWindowSeconds)
            {
                float shrinkT = Mathf.Clamp01(remaining / ShrinkWindowSeconds);
                transform.localScale = Vector3.one * shrinkT;

                if (_puddleLight != null)
                {
                    _puddleLight.intensity = 1.35f * shrinkT;
                }

                if (_bubbleParticles != null && remaining <= 0.35f && _bubbleParticles.isEmitting)
                {
                    _bubbleParticles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                }
            }
            else if (_puddleLight != null)
            {
                _puddleLight.intensity = 1.25f + 0.25f * Mathf.Sin(Time.time * 5.2f);
            }

            if (_damagePerSecond > 0f)
            {
                _tickTimer -= dt;
                if (_tickTimer <= 0f)
                {
                    _tickTimer += TickInterval;
                    ApplyPuddleTickDamage();
                }
            }
        }

        private void ApplyPuddleTickDamage()
        {
            float tickDamage = _damagePerSecond * TickInterval;
            if (tickDamage <= 0f) return;

            _tickDamaged.Clear();

            Vector3 center = transform.position + Vector3.up * 0.25f;
            float activeRadius = _radius * transform.localScale.x;
            if (activeRadius <= 0.05f) return;

            Collider[] hits = Physics.OverlapSphere(center, activeRadius);
            if (hits != null)
            {
                for (int i = 0; i < hits.Length; i++)
                {
                    Collider col = hits[i];
                    if (col == null) continue;
                    if (ShouldIgnoreTransform(col.transform)) continue;

                    IAcidDamageable dmg = col.GetComponentInParent<IAcidDamageable>();
                    if (dmg != null && _tickDamaged.Add(dmg))
                    {
                        dmg.TakeAcidDamage(tickDamage);
                    }
                }
            }

            // Fallback XZ distance check against Player tag while puddle is active.
            GameObject playerObj = SafeFindPlayerByTag();
            if (playerObj != null && !ShouldIgnoreTransform(playerObj.transform))
            {
                Vector3 playerPos = playerObj.transform.position;
                float dx = playerPos.x - transform.position.x;
                float dz = playerPos.z - transform.position.z;
                float xzDistSq = dx * dx + dz * dz;
                float maxDist = activeRadius + PlayerFallbackRadiusPadding;

                if (xzDistSq <= maxDist * maxDist &&
                    Mathf.Abs(playerPos.y - transform.position.y) <= PlayerFallbackMaxVerticalDelta)
                {
                    IAcidDamageable playerDmg = playerObj.GetComponentInParent<IAcidDamageable>();
                    if (playerDmg != null && _tickDamaged.Add(playerDmg))
                    {
                        playerDmg.TakeAcidDamage(tickDamage);
                    }
                }
            }
        }

        private bool ShouldIgnoreTransform(Transform target)
        {
            if (target == null) return true;
            if (_ignoreOwnerRoot != null && target.IsChildOf(_ignoreOwnerRoot)) return true;
            if (target.GetComponentInParent<TeniaBossController>() != null) return true;
            if (target.GetComponentInParent<ImmuneEnemies.ImmuneEnemyBase>() != null) return true;
            return false;
        }

        private GameObject SafeFindPlayerByTag()
        {
            if (_hasPlayerTagMissingWarning) return null;

            try
            {
                return GameObject.FindGameObjectWithTag("Player");
            }
            catch (UnityException)
            {
                _hasPlayerTagMissingWarning = true;
                return null;
            }
        }

        private static Material CreateAcidSurfaceMaterial(Color baseColor, Color emissionColor, float waveAmp, float waveSpeed)
        {
            Shader shader = Shader.Find("MapaEstomago/URP/PS1_GastricAcid");
            if (shader == null)
            {
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            }
            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }

            Material mat = new Material(shader);
            mat.hideFlags = HideFlags.DontSave;

            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", s_acidSurfaceTexture);
            if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", s_acidSurfaceTexture);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", baseColor);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", baseColor);
            if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", emissionColor);
            if (mat.HasProperty("_WaveAmp")) mat.SetFloat("_WaveAmp", waveAmp);
            if (mat.HasProperty("_WaveSpeed")) mat.SetFloat("_WaveSpeed", waveSpeed);
            if (mat.HasProperty("_VertexSnap")) mat.SetFloat("_VertexSnap", 160f);
            if (mat.HasProperty("_PulseSpeed")) mat.SetFloat("_PulseSpeed", 3.2f);
            if (mat.HasProperty("_ScrollSpeedA")) mat.SetVector("_ScrollSpeedA", new Vector4(0.08f, 0.05f, 0f, 0f));
            if (mat.HasProperty("_ScrollSpeedB")) mat.SetVector("_ScrollSpeedB", new Vector4(-0.06f, 0.09f, 0f, 0f));

            return mat;
        }

        private static Material CreatePixelParticleMaterial()
        {
            Shader shader = Shader.Find("MapaEstomago/URP/PS1_PixelParticle");
            bool isFallback = false;
            if (shader == null)
            {
                shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
                isFallback = true;
            }
            if (shader == null)
            {
                shader = Shader.Find("Sprites/Default");
                isFallback = true;
            }

            Material mat = new Material(shader);
            mat.hideFlags = HideFlags.DontSave;

            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", s_bubbleParticleTexture);
            if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", s_bubbleParticleTexture);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", Color.white);
            if (mat.HasProperty("_EmissionBoost")) mat.SetFloat("_EmissionBoost", 1.65f);
            if (mat.HasProperty("_Cutoff")) mat.SetFloat("_Cutoff", 0.35f);

            if (isFallback)
            {
                if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
                if (mat.HasProperty("_SrcBlend")) mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                if (mat.HasProperty("_DstBlend")) mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                if (mat.HasProperty("_ZWrite")) mat.SetInt("_ZWrite", 0);
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            }

            return mat;
        }

        private static void EnsureCachedAssets()
        {
            if (s_octagonDiscMesh == null)
            {
                s_octagonDiscMesh = BuildOctagonDiscMesh();
            }

            if (s_octagonRimMesh == null)
            {
                s_octagonRimMesh = BuildOctagonRimMesh(0.86f, 1.02f);
            }

            if (s_acidSurfaceTexture == null)
            {
                s_acidSurfaceTexture = BuildProceduralAcidTexture();
            }

            if (s_bubbleParticleTexture == null)
            {
                s_bubbleParticleTexture = BuildProceduralBubbleTexture();
            }
        }

        private static Mesh BuildOctagonDiscMesh()
        {
            const int sides = 8;
            Mesh mesh = new Mesh
            {
                name = "TeniaAcidPuddle_OctagonDisc",
                hideFlags = HideFlags.HideAndDontSave
            };

            // Center + inner ring (0.52) + outer ring (0.94) so PS1 vertex boiling animates nicely.
            int vertCount = 1 + sides * 2;
            Vector3[] verts = new Vector3[vertCount];
            Vector2[] uvs = new Vector2[vertCount];
            Color[] colors = new Color[vertCount];
            int[] tris = new int[sides * 3 + sides * 6];

            verts[0] = Vector3.zero;
            uvs[0] = new Vector2(0.5f, 0.5f);
            colors[0] = new Color(1.0f, 1.0f, 0.9f, 1f);

            float step = Mathf.PI * 2f / sides;
            for (int i = 0; i < sides; i++)
            {
                float angle = i * step + step * 0.5f;
                float c = Mathf.Cos(angle);
                float s = Mathf.Sin(angle);

                int innerIdx = 1 + i;
                int outerIdx = 1 + sides + i;

                verts[innerIdx] = new Vector3(c * 0.52f, 0f, s * 0.52f);
                uvs[innerIdx] = new Vector2(0.5f + c * 0.26f, 0.5f + s * 0.26f);
                colors[innerIdx] = new Color(0.95f, 1.0f, 0.85f, 1f);

                verts[outerIdx] = new Vector3(c * 0.94f, 0f, s * 0.94f);
                uvs[outerIdx] = new Vector2(0.5f + c * 0.47f, 0.5f + s * 0.47f);
                colors[outerIdx] = new Color(0.75f, 0.95f, 0.65f, 1f);
            }

            int t = 0;
            for (int i = 0; i < sides; i++)
            {
                int next = (i + 1) % sides;
                int in0 = 1 + i;
                int in1 = 1 + next;
                int out0 = 1 + sides + i;
                int out1 = 1 + sides + next;

                // Inner fan
                tris[t++] = 0;
                tris[t++] = in1;
                tris[t++] = in0;

                // Outer ring strip
                tris[t++] = in0;
                tris[t++] = in1;
                tris[t++] = out1;

                tris[t++] = in0;
                tris[t++] = out1;
                tris[t++] = out0;
            }

            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.colors = colors;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh BuildOctagonRimMesh(float innerRadius, float outerRadius)
        {
            const int sides = 8;
            Mesh mesh = new Mesh
            {
                name = "TeniaAcidPuddle_OctagonRim",
                hideFlags = HideFlags.HideAndDontSave
            };

            Vector3[] verts = new Vector3[sides * 2];
            Vector2[] uvs = new Vector2[sides * 2];
            Color[] colors = new Color[sides * 2];
            int[] tris = new int[sides * 6];

            float step = Mathf.PI * 2f / sides;
            for (int i = 0; i < sides; i++)
            {
                float angle = i * step + step * 0.5f;
                float c = Mathf.Cos(angle);
                float s = Mathf.Sin(angle);

                int vIn = i * 2;
                int vOut = vIn + 1;

                verts[vIn] = new Vector3(c * innerRadius, 0.008f, s * innerRadius);
                verts[vOut] = new Vector3(c * outerRadius, -0.004f, s * outerRadius);

                uvs[vIn] = new Vector2((float)i / sides, 0f);
                uvs[vOut] = new Vector2((float)i / sides, 1f);

                colors[vIn] = new Color(0.95f, 1.0f, 0.72f, 1f);
                colors[vOut] = new Color(0.55f, 0.85f, 0.40f, 1f);

                int next = (i + 1) % sides;
                int nextIn = next * 2;
                int nextOut = nextIn + 1;

                int t = i * 6;
                tris[t + 0] = vIn;
                tris[t + 1] = nextIn;
                tris[t + 2] = nextOut;
                tris[t + 3] = vIn;
                tris[t + 4] = nextOut;
                tris[t + 5] = vOut;
            }

            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.colors = colors;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Texture2D BuildProceduralAcidTexture()
        {
            const int size = 32;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "TeniaAcidPuddle_Tex",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Repeat,
                hideFlags = HideFlags.HideAndDontSave
            };

            Color[] pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = (float)x / size;
                    float v = (float)y / size;
                    float wave = Mathf.Sin(u * Mathf.PI * 4f + Mathf.Cos(v * Mathf.PI * 4f)) * 0.5f + 0.5f;
                    float cell = Mathf.Abs(Mathf.Sin((u * 3f + v * 2f) * Mathf.PI) * Mathf.Cos((v * 3f - u * 2f) * Mathf.PI));
                    float lum = Mathf.Clamp01(0.45f + wave * 0.35f + cell * 0.30f);

                    // Quantize to 6 levels for crisp PS1 pixel aesthetic.
                    lum = Mathf.Floor(lum * 6f) / 6f;
                    pixels[y * size + x] = new Color(0.55f * lum, 0.95f * lum + 0.05f, 0.20f * lum, 1f);
                }
            }

            tex.SetPixels(pixels);
            tex.Apply(false, true);
            return tex;
        }

        private static Texture2D BuildProceduralBubbleTexture()
        {
            const int size = 16;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "TeniaAcidBubble_ParticleTex",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };

            Color[] pixels = new Color[size * size];
            float center = (size - 1) * 0.5f;
            float maxR = size * 0.44f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - center;
                    float dy = y - center;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    if (dist <= maxR)
                    {
                        bool highlight = (x - center) < -1.2f && (y - center) > 1.2f && dist < maxR * 0.65f;
                        bool rim = dist >= maxR * 0.72f;
                        if (highlight)
                        {
                            pixels[y * size + x] = new Color(0.95f, 1.0f, 0.78f, 1f);
                        }
                        else if (rim)
                        {
                            pixels[y * size + x] = new Color(0.32f, 0.92f, 0.14f, 1f);
                        }
                        else
                        {
                            pixels[y * size + x] = new Color(0.58f, 1.0f, 0.25f, 1f);
                        }
                    }
                    else
                    {
                        pixels[y * size + x] = new Color(0f, 0f, 0f, 0f);
                    }
                }
            }

            tex.SetPixels(pixels);
            tex.Apply(false, true);
            return tex;
        }

        private void OnDestroy()
        {
            if (_discMaterial != null) Destroy(_discMaterial);
            if (_rimMaterial != null) Destroy(_rimMaterial);
            if (_bubbleMaterial != null) Destroy(_bubbleMaterial);
        }
    }
}