using System.Collections.Generic;
using MapaEstomago;
using UnityEngine;
using UnityEngine.Rendering;

namespace BossTenia
{
    /// <summary>
    /// Parabolic PS1 acid projectile launched from Boss Tenia's mouth.
    /// Synchronizes with TeniaAcidTelegraph, deals AoE impact damage (excluding the boss),
    /// triggers a short-lived splash burst, and spawns a TeniaAcidPuddle.
    /// </summary>
    public class TeniaAcidProjectile : MonoBehaviour
    {
        private const float SplashBurstDuration = 0.65f;

        private static Mesh s_octahedronMesh;
        private static Texture2D s_acidCoreTexture;
        private static Texture2D s_pixelDropTexture;

        private Vector3 _spawnPos;
        private Vector3 _targetGroundPos;
        private float _flightDuration;
        private float _arcHeight;
        private float _impactRadius;
        private float _impactDamage;
        private float _puddleRadius;
        private float _puddleDuration;
        private float _puddleDps;
        private Transform _ignoreOwnerRoot;
        private bool _phase2Enraged;

        private float _elapsed;
        private bool _impacted;
        private TeniaAcidTelegraph _telegraph;

        private Transform _coreTransform;
        private Transform _orbitA;
        private Transform _orbitB;
        private Material _coreMaterial;
        private Material _trailMaterial;
        private ParticleSystem _trailParticles;
        private Light _glowLight;

        /// <summary>
        /// Launches a parabolic acid projectile from <paramref name="spawnPos"/> to <paramref name="targetGroundPos"/>
        /// and automatically spawns its synchronized ground telegraph.
        /// </summary>
        public static TeniaAcidProjectile Launch(
            Vector3 spawnPos,
            Vector3 targetGroundPos,
            float flightDuration,
            float arcHeight,
            float impactRadius,
            float impactDamage,
            float puddleRadius,
            float puddleDuration,
            float puddleDps,
            Transform ignoreOwnerRoot = null,
            bool phase2Enraged = false)
        {
            GameObject go = new GameObject("TeniaAcidProjectile");
            go.transform.position = spawnPos;

            TeniaAcidProjectile proj = go.AddComponent<TeniaAcidProjectile>();
            proj.Initialize(
                spawnPos,
                targetGroundPos,
                flightDuration,
                arcHeight,
                impactRadius,
                impactDamage,
                puddleRadius,
                puddleDuration,
                puddleDps,
                ignoreOwnerRoot,
                phase2Enraged);
            return proj;
        }

        private void Initialize(
            Vector3 spawnPos,
            Vector3 targetGroundPos,
            float flightDuration,
            float arcHeight,
            float impactRadius,
            float impactDamage,
            float puddleRadius,
            float puddleDuration,
            float puddleDps,
            Transform ignoreOwnerRoot,
            bool phase2Enraged)
        {
            _spawnPos = spawnPos;
            _targetGroundPos = targetGroundPos;
            _flightDuration = Mathf.Max(0.05f, flightDuration);
            _arcHeight = Mathf.Max(0.25f, arcHeight);
            _impactRadius = Mathf.Max(0.25f, impactRadius);
            _impactDamage = Mathf.Max(0f, impactDamage);
            _puddleRadius = Mathf.Max(0.35f, puddleRadius);
            _puddleDuration = Mathf.Max(0.25f, puddleDuration);
            _puddleDps = Mathf.Max(0f, puddleDps);
            _ignoreOwnerRoot = ignoreOwnerRoot;
            _phase2Enraged = phase2Enraged;

            float telegraphRadius = Mathf.Max(_impactRadius, _puddleRadius);
            _telegraph = TeniaAcidTelegraph.Spawn(_targetGroundPos, telegraphRadius, _flightDuration, _phase2Enraged);

            EnsureCachedAssets();
            BuildVisuals();
        }

        private void BuildVisuals()
        {
            Color baseColor = _phase2Enraged
                ? new Color(1.0f, 0.88f, 0.45f, 1f)
                : new Color(0.84f, 1.0f, 0.65f, 1f);
            Color emissionColor = _phase2Enraged
                ? new Color(1.15f, 1.55f, 0.14f, 1f)
                : new Color(0.38f, 1.45f, 0.12f, 1f);

            _coreMaterial = CreateAcidCoreMaterial(baseColor, emissionColor);
            _trailMaterial = CreatePixelParticleMaterial();

            // Spinning bilious octahedral core
            GameObject coreGo = new GameObject("BiliousCore");
            coreGo.transform.SetParent(transform, false);
            coreGo.transform.localScale = Vector3.one * (_phase2Enraged ? 0.56f : 0.48f);
            AddOctahedronRenderer(coreGo, _coreMaterial);
            _coreTransform = coreGo.transform;

            // 2 orbiting sub-droplets
            GameObject subA = new GameObject("SubDropletA");
            subA.transform.SetParent(transform, false);
            subA.transform.localScale = Vector3.one * 0.21f;
            AddOctahedronRenderer(subA, _coreMaterial);
            _orbitA = subA.transform;

            GameObject subB = new GameObject("SubDropletB");
            subB.transform.SetParent(transform, false);
            subB.transform.localScale = Vector3.one * 0.17f;
            AddOctahedronRenderer(subB, _coreMaterial);
            _orbitB = subB.transform;

            // Real-time PS1 pixel particle trail
            GameObject trailGo = new GameObject("AcidTrailFX");
            trailGo.transform.SetParent(transform, false);
            _trailParticles = trailGo.AddComponent<ParticleSystem>();

            var main = _trailParticles.main;
            main.playOnAwake = true;
            main.loop = true;
            main.duration = 1.0f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.24f, 0.44f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.25f, 0.85f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.14f, 0.28f);
            main.gravityModifier = 0.28f;
            main.startColor = _phase2Enraged
                ? new ParticleSystem.MinMaxGradient(new Color(0.85f, 1.0f, 0.14f, 1f), new Color(1.0f, 0.45f, 0.10f, 1f))
                : new ParticleSystem.MinMaxGradient(new Color(0.30f, 1.0f, 0.12f, 1f), new Color(0.82f, 1.0f, 0.36f, 1f));
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var emission = _trailParticles.emission;
            emission.enabled = true;
            emission.rateOverTime = _phase2Enraged ? 36f : 26f;

            var shape = _trailParticles.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.18f;

            var sizeOverLife = _trailParticles.sizeOverLifetime;
            sizeOverLife.enabled = true;
            sizeOverLife.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.15f));

            ParticleSystemRenderer trailRenderer = trailGo.GetComponent<ParticleSystemRenderer>();
            if (trailRenderer != null)
            {
                trailRenderer.renderMode = ParticleSystemRenderMode.Billboard;
                trailRenderer.sharedMaterial = _trailMaterial;
            }

            // Subtle projectile point light
            GameObject lightGo = new GameObject("ProjectileGlow");
            lightGo.transform.SetParent(transform, false);
            _glowLight = lightGo.AddComponent<Light>();
            _glowLight.type = LightType.Point;
            _glowLight.color = _phase2Enraged ? new Color(0.95f, 0.85f, 0.15f) : new Color(0.42f, 1.0f, 0.16f);
            _glowLight.intensity = 2.4f;
            _glowLight.range = 4.5f;
            _glowLight.shadows = LightShadows.None;
        }

        private static void AddOctahedronRenderer(GameObject target, Material mat)
        {
            MeshFilter mf = target.AddComponent<MeshFilter>();
            mf.sharedMesh = s_octahedronMesh;

            MeshRenderer mr = target.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }

        private void Update()
        {
            if (_impacted) return;

            float dt = Time.deltaTime;
            _elapsed += dt;

            float u = Mathf.Clamp01(_elapsed / _flightDuration);
            Vector3 nextPos = Vector3.Lerp(_spawnPos, _targetGroundPos, u)
                            + Vector3.up * (4f * _arcHeight * u * (1f - u));
            transform.position = nextPos;

            UpdateVisualSpin(dt);

            if (u >= 1f)
            {
                Impact();
            }
        }

        private void UpdateVisualSpin(float dt)
        {
            float spinSpeed = _phase2Enraged ? 460f : 340f;
            if (_coreTransform != null)
            {
                _coreTransform.Rotate(new Vector3(0.65f, 1.0f, 0.45f) * (spinSpeed * dt), Space.Self);
            }

            float orbitAngle = _elapsed * (_phase2Enraged ? 9.5f : 7.0f);
            if (_orbitA != null)
            {
                _orbitA.localPosition = new Vector3(
                    Mathf.Cos(orbitAngle) * 0.46f,
                    Mathf.Sin(orbitAngle * 1.3f) * 0.18f,
                    Mathf.Sin(orbitAngle) * 0.46f);
                _orbitA.Rotate(Vector3.one * (spinSpeed * 1.4f * dt), Space.Self);
            }

            if (_orbitB != null)
            {
                float angleB = orbitAngle + Mathf.PI;
                _orbitB.localPosition = new Vector3(
                    Mathf.Cos(angleB) * 0.38f,
                    Mathf.Cos(angleB * 1.5f) * 0.26f,
                    Mathf.Sin(angleB) * 0.38f);
                _orbitB.Rotate(new Vector3(-1f, 0.8f, 0.6f) * (spinSpeed * 1.6f * dt), Space.Self);
            }
        }

        private void Impact()
        {
            if (_impacted) return;
            _impacted = true;

            if (_telegraph != null)
            {
                _telegraph.CompleteAndDestroy();
                _telegraph = null;
            }

            Vector3 impactPos = _targetGroundPos;

            ApplyExplosionDamage(impactPos);
            SpawnImpactSplashBurst(impactPos);
            TeniaAcidPuddle.Spawn(impactPos, _puddleRadius, _puddleDuration, _puddleDps, _ignoreOwnerRoot);

            Destroy(gameObject);
        }

        private void ApplyExplosionDamage(Vector3 impactPos)
        {
            if (_impactDamage <= 0f) return;

            Collider[] hits = Physics.OverlapSphere(impactPos, _impactRadius);
            if (hits == null || hits.Length == 0) return;

            HashSet<IAcidDamageable> damagedTargets = new HashSet<IAcidDamageable>();
            for (int i = 0; i < hits.Length; i++)
            {
                Collider col = hits[i];
                if (col == null) continue;
                if (ShouldIgnoreCollider(col)) continue;

                IAcidDamageable dmg = col.GetComponentInParent<IAcidDamageable>();
                if (dmg != null && damagedTargets.Add(dmg))
                {
                    dmg.TakeAcidDamage(_impactDamage);
                }
            }
        }

        private bool ShouldIgnoreCollider(Collider col)
        {
            if (col == null) return true;
            if (_ignoreOwnerRoot != null && col.transform.IsChildOf(_ignoreOwnerRoot)) return true;
            if (col.GetComponentInParent<TeniaBossController>() != null) return true;
            if (col.GetComponentInParent<ImmuneEnemies.ImmuneEnemyBase>() != null) return true;
            return false;
        }

        private void SpawnImpactSplashBurst(Vector3 impactPos)
        {
            GameObject burstGo = new GameObject("TeniaAcidSplashBurst");
            burstGo.transform.position = impactPos + Vector3.up * 0.08f;

            Material burstMat = CreatePixelParticleMaterial();

            ParticleSystem ps = burstGo.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = SplashBurstDuration;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.28f, 0.56f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2.6f, 6.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.14f, 0.30f);
            main.gravityModifier = 1.45f;
            main.startColor = _phase2Enraged
                ? new ParticleSystem.MinMaxGradient(new Color(0.95f, 1.0f, 0.16f, 1f), new Color(1.0f, 0.35f, 0.08f, 1f))
                : new ParticleSystem.MinMaxGradient(new Color(0.32f, 1.0f, 0.14f, 1f), new Color(0.85f, 1.0f, 0.42f, 1f));
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var emission = ps.emission;
            emission.enabled = false;

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 58f;
            shape.radius = Mathf.Min(0.45f, _impactRadius * 0.35f);
            shape.rotation = new Vector3(-90f, 0f, 0f);

            var sizeOverLife = ps.sizeOverLifetime;
            sizeOverLife.enabled = true;
            sizeOverLife.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.12f));

            ParticleSystemRenderer psRenderer = burstGo.GetComponent<ParticleSystemRenderer>();
            if (psRenderer != null)
            {
                psRenderer.renderMode = ParticleSystemRenderMode.Billboard;
                psRenderer.sharedMaterial = burstMat;
            }

            Light flashLight = burstGo.AddComponent<Light>();
            flashLight.type = LightType.Point;
            flashLight.color = _phase2Enraged ? new Color(1.0f, 0.65f, 0.12f) : new Color(0.38f, 1.0f, 0.15f);
            flashLight.intensity = 4.2f;
            flashLight.range = Mathf.Max(3.5f, _impactRadius * 2.0f);
            flashLight.shadows = LightShadows.None;

            ps.Emit(_phase2Enraged ? 26 : 20);

            SplashBurstCleanup cleanup = burstGo.AddComponent<SplashBurstCleanup>();
            cleanup.Initialize(burstMat, flashLight, SplashBurstDuration);
        }

        private static Material CreateAcidCoreMaterial(Color baseColor, Color emissionColor)
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

            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", s_acidCoreTexture);
            if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", s_acidCoreTexture);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", baseColor);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", baseColor);
            if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", emissionColor);
            if (mat.HasProperty("_WaveAmp")) mat.SetFloat("_WaveAmp", 0.025f);
            if (mat.HasProperty("_WaveSpeed")) mat.SetFloat("_WaveSpeed", 5.5f);
            if (mat.HasProperty("_VertexSnap")) mat.SetFloat("_VertexSnap", 160f);
            if (mat.HasProperty("_PulseSpeed")) mat.SetFloat("_PulseSpeed", 4.5f);

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

            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", s_pixelDropTexture);
            if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", s_pixelDropTexture);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", Color.white);
            if (mat.HasProperty("_EmissionBoost")) mat.SetFloat("_EmissionBoost", 1.75f);
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
            if (s_octahedronMesh == null)
            {
                s_octahedronMesh = BuildOctahedronMesh();
            }

            if (s_acidCoreTexture == null)
            {
                s_acidCoreTexture = BuildProceduralCoreTexture();
            }

            if (s_pixelDropTexture == null)
            {
                s_pixelDropTexture = BuildProceduralDropletTexture();
            }
        }

        private static Mesh BuildOctahedronMesh()
        {
            Mesh mesh = new Mesh
            {
                name = "TeniaAcidProjectile_Octahedron",
                hideFlags = HideFlags.HideAndDontSave
            };

            Vector3 top = Vector3.up * 0.55f;
            Vector3 bottom = Vector3.down * 0.55f;
            Vector3 fwd = Vector3.forward * 0.5f;
            Vector3 back = Vector3.back * 0.5f;
            Vector3 right = Vector3.right * 0.5f;
            Vector3 left = Vector3.left * 0.5f;

            Vector3[] verts =
            {
                top, fwd, right,
                top, right, back,
                top, back, left,
                top, left, fwd,

                bottom, right, fwd,
                bottom, back, right,
                bottom, left, back,
                bottom, fwd, left
            };

            Vector2[] uvs = new Vector2[verts.Length];
            Color[] colors = new Color[verts.Length];
            int[] tris = new int[verts.Length];

            for (int i = 0; i < verts.Length; i += 3)
            {
                uvs[i + 0] = new Vector2(0.5f, 1.0f);
                uvs[i + 1] = new Vector2(0.0f, 0.0f);
                uvs[i + 2] = new Vector2(1.0f, 0.0f);

                colors[i + 0] = Color.white;
                colors[i + 1] = new Color(0.85f, 1.0f, 0.78f, 1f);
                colors[i + 2] = new Color(0.85f, 1.0f, 0.78f, 1f);

                tris[i + 0] = i + 0;
                tris[i + 1] = i + 1;
                tris[i + 2] = i + 2;
            }

            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.colors = colors;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Texture2D BuildProceduralCoreTexture()
        {
            const int size = 32;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "TeniaAcidProjectile_CoreTex",
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
                    float swirl = Mathf.Sin((u * 3f + v * 2f) * Mathf.PI) * Mathf.Cos((v * 4f - u) * Mathf.PI);
                    float lum = Mathf.Clamp01(0.65f + 0.35f * swirl);
                    lum = Mathf.Floor(lum * 5f) / 5f;
                    pixels[y * size + x] = new Color(0.52f * lum, 0.96f * lum + 0.04f, 0.18f * lum, 1f);
                }
            }

            tex.SetPixels(pixels);
            tex.Apply(false, true);
            return tex;
        }

        private static Texture2D BuildProceduralDropletTexture()
        {
            const int size = 16;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "TeniaAcidProjectile_DropTex",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };

            Color[] pixels = new Color[size * size];
            float center = (size - 1) * 0.5f;
            float maxR = size * 0.42f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - center;
                    float dy = y - center;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    if (dist <= maxR)
                    {
                        float t = 1f - dist / maxR;
                        pixels[y * size + x] = t > 0.55f
                            ? new Color(0.95f, 1.0f, 0.72f, 1f)
                            : new Color(0.45f, 1.0f, 0.18f, 1f);
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
            if (!_impacted && _telegraph != null)
            {
                _telegraph.CompleteAndDestroy();
                _telegraph = null;
            }

            if (_coreMaterial != null) Destroy(_coreMaterial);
            if (_trailMaterial != null) Destroy(_trailMaterial);
        }

        private sealed class SplashBurstCleanup : MonoBehaviour
        {
            private Material _material;
            private Light _flashLight;
            private float _duration;
            private float _initialIntensity;
            private float _elapsed;

            public void Initialize(Material material, Light flashLight, float duration)
            {
                _material = material;
                _flashLight = flashLight;
                _duration = Mathf.Max(0.1f, duration);
                _initialIntensity = flashLight != null ? flashLight.intensity : 0f;
            }

            private void Update()
            {
                _elapsed += Time.deltaTime;

                if (_flashLight != null)
                {
                    float fade = 1f - Mathf.Clamp01(_elapsed / (_duration * 0.45f));
                    _flashLight.intensity = _initialIntensity * fade;
                    if (fade <= 0f) _flashLight.enabled = false;
                }

                if (_elapsed >= _duration)
                {
                    Destroy(gameObject);
                }
            }

            private void OnDestroy()
            {
                if (_material != null)
                {
                    Destroy(_material);
                }
            }
        }
    }
}