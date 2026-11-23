using UnityEngine;
using UnityEngine.Rendering;

namespace BossTenia
{
    /// <summary>
    /// Procedural PS1-style ground telegraph showing a blinking segmented warning ring,
    /// central reticle, and an expanding inner fill disc synchronized to acid impact.
    /// </summary>
    public class TeniaAcidTelegraph : MonoBehaviour
    {
        private const float GroundOffsetY = 0.088f;
        private const float CriticalWindowSeconds = 0.28f;
        private const float OverdueSafetyPadding = 0.6f;

        private static readonly Color AcidGreen = new Color(0.38f, 1.0f, 0.14f, 1f);
        private static readonly Color AlarmYellow = new Color(1.0f, 0.92f, 0.12f, 1f);
        private static readonly Color CriticalOrangeRed = new Color(1.0f, 0.28f, 0.08f, 1f);

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        private static readonly int MainTexId = Shader.PropertyToID("_MainTex");

        private static Mesh s_outerRingMesh;
        private static Mesh s_reticleMesh;
        private static Mesh s_fillDiscMesh;

        private float _radius;
        private float _duration;
        private bool _phase2Enraged;
        private float _elapsed;
        private float _blinkPhase;
        private bool _completed;

        private Transform _fillTransform;
        private MeshRenderer _outerRenderer;
        private MeshRenderer _reticleRenderer;
        private MeshRenderer _fillRenderer;
        private Material _ringMaterial;
        private Material _fillMaterial;
        private Light _pointLight;
        private MaterialPropertyBlock _mpb;

        /// <summary>
        /// Spawns a blinking ground telegraph at <paramref name="groundWorldPos"/>.
        /// </summary>
        public static TeniaAcidTelegraph Spawn(Vector3 groundWorldPos, float radius, float duration, bool phase2Enraged = false)
        {
            GameObject go = new GameObject("TeniaAcidTelegraph");
            go.transform.position = new Vector3(groundWorldPos.x, groundWorldPos.y + GroundOffsetY, groundWorldPos.z);
            go.transform.rotation = Quaternion.identity;

            TeniaAcidTelegraph telegraph = go.AddComponent<TeniaAcidTelegraph>();
            telegraph.Initialize(radius, duration, phase2Enraged);
            return telegraph;
        }

        /// <summary>
        /// Immediately completes and destroys this telegraph.
        /// </summary>
        public void CompleteAndDestroy()
        {
            if (_completed) return;
            _completed = true;
            Destroy(gameObject);
        }

        private void Initialize(float radius, float duration, bool phase2Enraged)
        {
            _radius = Mathf.Max(0.25f, radius);
            _duration = Mathf.Max(0.08f, duration);
            _phase2Enraged = phase2Enraged;
            _mpb = new MaterialPropertyBlock();

            EnsureCachedMeshes();

            _ringMaterial = CreateSafeUrpTelegraphMaterial();
            _fillMaterial = CreateSafeUrpTelegraphMaterial();

            _outerRenderer = CreateLayer("OuterWarningRing", s_outerRingMesh, _ringMaterial, Vector3.one * _radius, 0f);
            _reticleRenderer = CreateLayer("CentralReticle", s_reticleMesh, _ringMaterial, Vector3.one * Mathf.Max(0.35f, _radius * 0.34f), 0.016f);

            float initialFillRadius = _radius * 0.08f;
            _fillRenderer = CreateLayer("FillDisc", s_fillDiscMesh, _fillMaterial, new Vector3(initialFillRadius, 1f, initialFillRadius), 0.008f);
            _fillTransform = _fillRenderer.transform;

            GameObject lightGo = new GameObject("TelegraphLight");
            lightGo.transform.SetParent(transform, false);
            lightGo.transform.localPosition = new Vector3(0f, 0.75f, 0f);
            lightGo.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            _pointLight = lightGo.AddComponent<Light>();
            _pointLight.type = LightType.Point;
            _pointLight.range = Mathf.Max(2.5f, _radius * 1.75f);
            _pointLight.shadows = LightShadows.None;
            _pointLight.color = AcidGreen;
            _pointLight.intensity = 1.6f;

            ApplyVisualState(0f, 1f);
        }

        private MeshRenderer CreateLayer(string childName, Mesh mesh, Material mat, Vector3 localScale, float localY)
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
            mr.receiveShadows = false;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return mr;
        }

        private void Update()
        {
            if (_completed) return;

            float dt = Time.deltaTime;
            _elapsed += dt;

            if (_elapsed > _duration + OverdueSafetyPadding)
            {
                CompleteAndDestroy();
                return;
            }

            float normalized = Mathf.Clamp01(_elapsed / _duration);

            // Blink frequency accelerates smoothly from 3.5 Hz at start to 15 Hz for normalized >= 0.72.
            float freqT = Mathf.Clamp01(normalized / 0.72f);
            float blinkHz = Mathf.Lerp(3.5f, 15.0f, freqT * freqT);
            if (_phase2Enraged)
            {
                blinkHz *= 1.18f;
            }

            _blinkPhase += dt * blinkHz * Mathf.PI * 2f;
            float blinkWave = 0.5f + 0.5f * Mathf.Sin(_blinkPhase);

            ApplyVisualState(normalized, blinkWave);
        }

        private void ApplyVisualState(float normalized, float blinkWave)
        {
            if (_fillTransform != null)
            {
                float fillRadius = Mathf.Lerp(0.08f, 1.0f, normalized) * _radius;
                _fillTransform.localScale = new Vector3(fillRadius, 1f, fillRadius);
            }

            float remaining = Mathf.Max(0f, _duration - _elapsed);
            Color baseColor;
            if (remaining <= CriticalWindowSeconds)
            {
                float criticalT = 1f - Mathf.Clamp01(remaining / CriticalWindowSeconds);
                baseColor = Color.Lerp(AlarmYellow, CriticalOrangeRed, criticalT);
            }
            else
            {
                float preCriticalSpan = Mathf.Max(0.01f, _duration - CriticalWindowSeconds);
                float warnT = Mathf.Clamp01(_elapsed / preCriticalSpan);
                baseColor = Color.Lerp(AcidGreen, AlarmYellow, warnT);
            }

            float enragedBoost = _phase2Enraged ? 1.35f : 1.0f;
            float ringAlpha = Mathf.Lerp(0.32f, 0.96f, blinkWave) * (_phase2Enraged ? 1.0f : 0.92f);
            float reticleAlpha = Mathf.Lerp(0.45f, 1.0f, blinkWave);
            float fillAlpha = Mathf.Lerp(0.14f, 0.36f, blinkWave) * Mathf.Lerp(0.55f, 1.0f, normalized);

            Color ringColor = new Color(
                baseColor.r * enragedBoost,
                baseColor.g * enragedBoost,
                baseColor.b * enragedBoost,
                Mathf.Clamp01(ringAlpha));

            Color reticleColor = new Color(
                baseColor.r * enragedBoost,
                baseColor.g * enragedBoost,
                baseColor.b * enragedBoost,
                Mathf.Clamp01(reticleAlpha));

            Color fillColor = new Color(
                baseColor.r * enragedBoost,
                baseColor.g * enragedBoost,
                baseColor.b * enragedBoost,
                Mathf.Clamp01(fillAlpha));

            ApplyColor(_outerRenderer, ringColor);
            ApplyColor(_reticleRenderer, reticleColor);
            ApplyColor(_fillRenderer, fillColor);

            if (_pointLight != null)
            {
                _pointLight.color = baseColor;
                float minIntensity = _phase2Enraged ? 0.9f : 0.45f;
                float maxIntensity = _phase2Enraged ? 3.8f : 2.5f;
                _pointLight.intensity = Mathf.Lerp(minIntensity, maxIntensity, blinkWave);
            }
        }

        private void ApplyColor(MeshRenderer renderer, Color color)
        {
            if (renderer == null || _mpb == null) return;
            renderer.GetPropertyBlock(_mpb);
            _mpb.SetColor(BaseColorId, color);
            _mpb.SetColor(ColorId, color);
            renderer.SetPropertyBlock(_mpb);
        }

        private static Material CreateSafeUrpTelegraphMaterial()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null)
            {
                shader = Shader.Find("Sprites/Default");
            }

            Material mat = new Material(shader);
            mat.hideFlags = HideFlags.DontSave;
            mat.renderQueue = 3050;

            if (mat.HasProperty(BaseMapId))
            {
                mat.SetTexture(BaseMapId, Texture2D.whiteTexture);
            }
            if (mat.HasProperty(MainTexId))
            {
                mat.SetTexture(MainTexId, Texture2D.whiteTexture);
            }

            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
            if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 2f);
            if (mat.HasProperty("_SrcBlend")) mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            if (mat.HasProperty("_DstBlend")) mat.SetInt("_DstBlend", (int)BlendMode.One);
            if (mat.HasProperty("_ZWrite")) mat.SetInt("_ZWrite", 0);
            if (mat.HasProperty("_Cull")) mat.SetInt("_Cull", (int)CullMode.Off);
            if (mat.HasProperty("_AlphaClip")) mat.SetFloat("_AlphaClip", 0f);

            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.SetColor(BaseColorId, AcidGreen);
            mat.SetColor(ColorId, AcidGreen);
            return mat;
        }

        private static void EnsureCachedMeshes()
        {
            if (s_outerRingMesh == null)
            {
                s_outerRingMesh = BuildSegmentedRingMesh(16, 0.85f, 1.0f, 0.64f);
            }

            if (s_reticleMesh == null)
            {
                s_reticleMesh = BuildReticleMesh();
            }

            if (s_fillDiscMesh == null)
            {
                s_fillDiscMesh = BuildDiscMesh(20, 1.0f);
            }
        }

        private static Mesh BuildSegmentedRingMesh(int segmentCount, float innerRadius, float outerRadius, float dutyCycle)
        {
            Mesh mesh = new Mesh
            {
                name = "TeniaTelegraph_OuterRing",
                hideFlags = HideFlags.HideAndDontSave
            };

            Vector3[] verts = new Vector3[segmentCount * 4];
            Vector2[] uvs = new Vector2[segmentCount * 4];
            int[] tris = new int[segmentCount * 6];

            float step = Mathf.PI * 2f / segmentCount;
            float arcSpan = step * Mathf.Clamp(dutyCycle, 0.2f, 0.95f);

            for (int i = 0; i < segmentCount; i++)
            {
                float a0 = i * step;
                float a1 = a0 + arcSpan;

                float c0 = Mathf.Cos(a0);
                float s0 = Mathf.Sin(a0);
                float c1 = Mathf.Cos(a1);
                float s1 = Mathf.Sin(a1);

                int v = i * 4;
                verts[v + 0] = new Vector3(c0 * innerRadius, 0f, s0 * innerRadius);
                verts[v + 1] = new Vector3(c0 * outerRadius, 0f, s0 * outerRadius);
                verts[v + 2] = new Vector3(c1 * outerRadius, 0f, s1 * outerRadius);
                verts[v + 3] = new Vector3(c1 * innerRadius, 0f, s1 * innerRadius);

                uvs[v + 0] = new Vector2(0f, 0f);
                uvs[v + 1] = new Vector2(0f, 1f);
                uvs[v + 2] = new Vector2(1f, 1f);
                uvs[v + 3] = new Vector2(1f, 0f);

                int t = i * 6;
                tris[t + 0] = v + 0;
                tris[t + 1] = v + 1;
                tris[t + 2] = v + 2;
                tris[t + 3] = v + 0;
                tris[t + 4] = v + 2;
                tris[t + 5] = v + 3;
            }

            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh BuildReticleMesh()
        {
            Mesh mesh = new Mesh
            {
                name = "TeniaTelegraph_Reticle",
                hideFlags = HideFlags.HideAndDontSave
            };

            const int ringSegments = 12;
            const int tickCount = 4;
            int totalQuads = ringSegments + tickCount;

            Vector3[] verts = new Vector3[totalQuads * 4];
            Vector2[] uvs = new Vector2[totalQuads * 4];
            int[] tris = new int[totalQuads * 6];

            float step = Mathf.PI * 2f / ringSegments;
            const float innerR = 0.30f;
            const float outerR = 0.44f;

            for (int i = 0; i < ringSegments; i++)
            {
                float a0 = i * step;
                float a1 = (i + 1) * step;

                float c0 = Mathf.Cos(a0);
                float s0 = Mathf.Sin(a0);
                float c1 = Mathf.Cos(a1);
                float s1 = Mathf.Sin(a1);

                int v = i * 4;
                verts[v + 0] = new Vector3(c0 * innerR, 0f, s0 * innerR);
                verts[v + 1] = new Vector3(c0 * outerR, 0f, s0 * outerR);
                verts[v + 2] = new Vector3(c1 * outerR, 0f, s1 * outerR);
                verts[v + 3] = new Vector3(c1 * innerR, 0f, s1 * innerR);

                uvs[v + 0] = new Vector2(0f, 0f);
                uvs[v + 1] = new Vector2(0f, 1f);
                uvs[v + 2] = new Vector2(1f, 1f);
                uvs[v + 3] = new Vector2(1f, 0f);

                int t = i * 6;
                tris[t + 0] = v + 0;
                tris[t + 1] = v + 1;
                tris[t + 2] = v + 2;
                tris[t + 3] = v + 0;
                tris[t + 4] = v + 2;
                tris[t + 5] = v + 3;
            }

            // 4 cardinal crosshair ticks
            const float tickHalfWidth = 0.055f;
            const float tickStart = 0.10f;
            const float tickEnd = 0.92f;
            Vector3[] dirs =
            {
                Vector3.forward,
                Vector3.back,
                Vector3.right,
                Vector3.left
            };

            for (int k = 0; k < tickCount; k++)
            {
                int q = ringSegments + k;
                Vector3 fwd = dirs[k];
                Vector3 right = new Vector3(fwd.z, 0f, -fwd.x) * tickHalfWidth;

                int v = q * 4;
                verts[v + 0] = fwd * tickStart - right;
                verts[v + 1] = fwd * tickEnd - right;
                verts[v + 2] = fwd * tickEnd + right;
                verts[v + 3] = fwd * tickStart + right;

                uvs[v + 0] = new Vector2(0f, 0f);
                uvs[v + 1] = new Vector2(0f, 1f);
                uvs[v + 2] = new Vector2(1f, 1f);
                uvs[v + 3] = new Vector2(1f, 0f);

                int t = q * 6;
                tris[t + 0] = v + 0;
                tris[t + 1] = v + 1;
                tris[t + 2] = v + 2;
                tris[t + 3] = v + 0;
                tris[t + 4] = v + 2;
                tris[t + 5] = v + 3;
            }

            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh BuildDiscMesh(int segments, float radius)
        {
            Mesh mesh = new Mesh
            {
                name = "TeniaTelegraph_FillDisc",
                hideFlags = HideFlags.HideAndDontSave
            };

            Vector3[] verts = new Vector3[segments + 1];
            Vector2[] uvs = new Vector2[segments + 1];
            int[] tris = new int[segments * 3];

            verts[0] = Vector3.zero;
            uvs[0] = new Vector2(0.5f, 0.5f);

            float step = Mathf.PI * 2f / segments;
            for (int i = 0; i < segments; i++)
            {
                float a = i * step;
                float c = Mathf.Cos(a);
                float s = Mathf.Sin(a);
                verts[i + 1] = new Vector3(c * radius, 0f, s * radius);
                uvs[i + 1] = new Vector2(0.5f + c * 0.5f, 0.5f + s * 0.5f);

                int t = i * 3;
                int next = (i + 1) % segments + 1;
                tris[t + 0] = 0;
                tris[t + 1] = next;
                tris[t + 2] = i + 1;
            }

            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private void OnDestroy()
        {
            if (_ringMaterial != null) Destroy(_ringMaterial);
            if (_fillMaterial != null) Destroy(_fillMaterial);
        }
    }
}