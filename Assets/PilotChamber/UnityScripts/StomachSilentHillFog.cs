using UnityEngine;
using UnityEngine.Rendering;

namespace MapaEstomago
{
    /// <summary>
    /// Atmosfera de neblina estilo Silent Hill 2 adaptada para a estetica PS1 Low-Poly do Estomago:
    /// - Bolha limpa ao redor do jogador (sem neblina no curto alcance);
    /// - Densidade crescente no medio e longo alcance com bancos de vapor em movimento;
    /// - Tom esverdeado bilioso/acido simulando gases digestivos do estomago;
    /// - Dithering Bayer 4x4 + quantizacao 15-bit PS1 + camadas low-poly de vapor flutuante.
    /// </summary>
    [ExecuteAlways]
    public class StomachSilentHillFog : MonoBehaviour
    {
        [Header("Ativacao Global da Neblina")]
        [Tooltip("Desmarque para esconder completamente a neblina e os gases verdes da visualizacao/edicao.")]
        public bool enableFog = true;

        [Header("Foco da Neblina (Jogador)")]
        [Tooltip("Se vazio, localiza automaticamente o Test_Player (tag Player).")]
        public Transform playerTarget;

        [Header("Distancias Radiais (Estilo Silent Hill 2)")]
        [Tooltip("Raio ao redor do jogador onde NAO ha neblina (visao limpa).")]
        [Range(1.5f, 14f)]
        public float clearRadius = 5.5f;

        [Tooltip("Distancia de medio alcance onde os gases ganham corpo.")]
        [Range(5f, 25f)]
        public float midRadius = 13.5f;

        [Tooltip("Distancia de longo alcance onde a neblina fecha densamente.")]
        [Range(10f, 45f)]
        public float maxRadius = 23.5f;

        [Range(0f, 1f)]
        public float midDensity = 0.55f;

        [Range(0f, 1f)]
        public float maxDensity = 0.96f;

        [Header("Cores dos Gases do Estomago (Esverdeado)")]
        [Tooltip("Verde-bile umido do vapor gastrico no medio alcance.")]
        public Color gasColorMid = new Color(0.30f, 0.52f, 0.22f, 1f);

        [Tooltip("Verde musgo/oliva profundo e opressivo no longo alcance e fundo.")]
        public Color gasColorFar = new Color(0.18f, 0.33f, 0.14f, 1f);

        [Tooltip("Destaque toxico esverdeado nas nuvens baixas sobre o acido.")]
        public Color gasColorHighlight = new Color(0.42f, 0.68f, 0.26f, 1f);

        [Header("Vapor Acido Rasteiro & Turbulencia")]
        public float acidSurfaceHeight = -0.35f;
        [Range(0.5f, 10f)]
        public float heightFalloff = 2.8f;
        [Range(0f, 0.65f)]
        public float turbulenceAmount = 0.32f;
        [Range(0.04f, 0.5f)]
        public float noiseScale = 0.15f;
        public Vector2 windVelocity = new Vector2(0.38f, 0.24f);

        [Header("Estetica Retro PS1 Low-Poly")]
        [Tooltip("Intensidade do dithering ordenado Bayer 4x4 estilo PS1 na neblina.")]
        [Range(0f, 2f)]
        public float ps1DitherStrength = 1.0f;

        [Tooltip("Material das nuvens low-poly de vapor gastrico (PS1_StomachGasWisp).")]
        public Material lowPolyGasMaterial;

        [Tooltip("Gera camadas low-poly de gas flutuando no medio/longo alcance.")]
        public bool enableLowPolyGasClouds = true;

        private static readonly int PropPlayerPos = Shader.PropertyToID("_StomachFogPlayerPos");
        private static readonly int PropRadii = Shader.PropertyToID("_StomachFogRadii");
        private static readonly int PropParams = Shader.PropertyToID("_StomachFogParams");
        private static readonly int PropAnim = Shader.PropertyToID("_StomachFogAnim");
        private static readonly int PropColorNear = Shader.PropertyToID("_StomachFogColorNear");
        private static readonly int PropColorFar = Shader.PropertyToID("_StomachFogColorFar");
        private static readonly int PropColorHighlight = Shader.PropertyToID("_StomachFogColorHighlight");

        private Transform _cloudContainer;
        private Mesh _lowPolyCloudMesh;

        private void OnEnable()
        {
            if (!enableFog)
            {
                ClearShaderGlobals();
                return;
            }
            FindPlayerIfNeeded();
            EnsureLowPolyGasClouds();
            if (_cloudContainer != null) _cloudContainer.gameObject.SetActive(enableLowPolyGasClouds);
            ApplyShaderGlobals();
        }

        private void OnDisable()
        {
            ClearShaderGlobals();
        }

        private void OnDestroy()
        {
            ClearShaderGlobals();
        }

        private void OnValidate()
        {
            if (!enableFog || !isActiveAndEnabled)
            {
                ClearShaderGlobals();
            }
            else
            {
                FindPlayerIfNeeded();
                EnsureLowPolyGasClouds();
                if (_cloudContainer != null) _cloudContainer.gameObject.SetActive(enableLowPolyGasClouds);
                ApplyShaderGlobals();
            }
        }

        private void LateUpdate()
        {
            if (!enableFog)
            {
                ClearShaderGlobals();
                return;
            }
            FindPlayerIfNeeded();
            ApplyShaderGlobals();
            UpdateCloudAnchor();
        }

        public void ClearShaderGlobals()
        {
            Shader.SetGlobalVector(PropPlayerPos, Vector4.zero);
            Shader.SetGlobalVector(PropRadii, Vector4.zero);
            Shader.SetGlobalVector(PropParams, Vector4.zero);
            Shader.SetGlobalVector(PropAnim, Vector4.zero);

            if (_cloudContainer != null)
            {
                _cloudContainer.gameObject.SetActive(false);
            }
            else
            {
                Transform t = transform.Find("PS1_LowPoly_Gas_Layers");
                if (t != null) t.gameObject.SetActive(false);
            }

            RenderSettings.fog = false;

            Camera cam = Camera.main;
            if (cam == null) cam = FindFirstObjectByType<Camera>();
            if (cam != null)
            {
                cam.backgroundColor = new Color(0.04f, 0.008f, 0.014f, 1f);
            }
        }

        private void FindPlayerIfNeeded()
        {
            if (playerTarget != null) return;

            GameObject pObj = GameObject.FindGameObjectWithTag("Player");
            if (pObj == null) pObj = GameObject.Find("Test_Player");
            if (pObj != null)
            {
                playerTarget = pObj.transform;
            }
        }

        public Vector3 GetFocusPosition()
        {
            if (playerTarget != null)
            {
                return playerTarget.position;
            }

            IsometricCameraFollow iso = FindFirstObjectByType<IsometricCameraFollow>();
            if (iso != null)
            {
                if (iso.target != null) return iso.target.position;
                return iso.FreePivot;
            }

            return new Vector3(-7.5f, 0.1f, -1.0f);
        }

        public void ApplyShaderGlobals()
        {
            Vector3 focus = GetFocusPosition();
            float safeMid = Mathf.Max(clearRadius + 1.0f, midRadius);
            float safeMax = Mathf.Max(safeMid + 2.0f, maxRadius);

            Shader.SetGlobalVector(PropPlayerPos, new Vector4(focus.x, focus.y, focus.z, 1.0f));
            Shader.SetGlobalVector(PropRadii, new Vector4(clearRadius, safeMid, safeMax, midDensity));
            Shader.SetGlobalVector(PropParams, new Vector4(maxDensity, acidSurfaceHeight, heightFalloff, turbulenceAmount));
            Shader.SetGlobalVector(PropAnim, new Vector4(noiseScale, windVelocity.x, windVelocity.y, ps1DitherStrength));
            // Converte de sRGB para Linear para coincidir exatamente com Camera.backgroundColor no URP Linear
            Shader.SetGlobalColor(PropColorNear, gasColorMid.linear);
            Shader.SetGlobalColor(PropColorFar, gasColorFar.linear);
            Shader.SetGlobalColor(PropColorHighlight, gasColorHighlight.linear);

            // Sincroniza o fundo da camera e a luz ambiente com a neblina esverdeada profunda do estomago
            Camera cam = Camera.main;
            if (cam == null) cam = FindFirstObjectByType<Camera>();
            if (cam != null)
            {
                cam.backgroundColor = gasColorFar;
                cam.clearFlags = CameraClearFlags.SolidColor;
            }

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogColor = gasColorFar;
            RenderSettings.fogDensity = 0.018f;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.16f, 0.22f, 0.13f, 1f);
        }

        public void EnsureLowPolyGasClouds()
        {
            if (!enableLowPolyGasClouds) return;

            Transform existing = transform.Find("PS1_LowPoly_Gas_Layers");
            if (existing != null)
            {
                _cloudContainer = existing;
            }
            else
            {
                GameObject go = new GameObject("PS1_LowPoly_Gas_Layers");
                go.transform.SetParent(transform, false);
                _cloudContainer = go.transform;
            }

            if (lowPolyGasMaterial == null)
            {
                Shader s = Shader.Find("MapaEstomago/URP/PS1_StomachGasWisp");
                if (s != null)
                {
                    lowPolyGasMaterial = new Material(s);
                    lowPolyGasMaterial.name = "Mat_PS1_StomachGasWisp_Runtime";
                }
            }

            if (lowPolyGasMaterial == null) return;

            if (_lowPolyCloudMesh == null)
            {
                _lowPolyCloudMesh = BuildLowPolyGasGridMesh(7, 7, 16f);
            }

            // Cria 3 camadas horizontais low-poly em alturas diferentes para dar paralaxe volumetrica PS1
            float[] heights = { 0.25f, 1.05f, 2.15f };
            float[] scales = { 2.2f, 2.6f, 3.0f };
            float[] angles = { 0f, 37f, -28f };

            while (_cloudContainer.childCount < heights.Length)
            {
                int idx = _cloudContainer.childCount;
                GameObject layerGo = new GameObject($"GasLayer_LowPoly_{idx}");
                layerGo.transform.SetParent(_cloudContainer, false);
                MeshFilter mf = layerGo.AddComponent<MeshFilter>();
                MeshRenderer mr = layerGo.AddComponent<MeshRenderer>();
                mf.sharedMesh = _lowPolyCloudMesh;
                mr.sharedMaterial = lowPolyGasMaterial;
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveShadows = false;
            }

            for (int i = 0; i < heights.Length && i < _cloudContainer.childCount; i++)
            {
                Transform child = _cloudContainer.GetChild(i);
                child.localPosition = new Vector3(0f, heights[i], 0f);
                child.localRotation = Quaternion.Euler(0f, angles[i], 0f);
                child.localScale = new Vector3(scales[i], 1f, scales[i]);
                MeshRenderer mr = child.GetComponent<MeshRenderer>();
                if (mr != null && lowPolyGasMaterial != null)
                {
                    mr.sharedMaterial = lowPolyGasMaterial;
                }
                MeshFilter mf = child.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh == null)
                {
                    mf.sharedMesh = _lowPolyCloudMesh;
                }
            }
        }

        private void UpdateCloudAnchor()
        {
            if (_cloudContainer == null) return;
            _cloudContainer.gameObject.SetActive(enableLowPolyGasClouds);
            if (!enableLowPolyGasClouds) return;

            // Segue o jogador em passos discretos de 2m para manter o ruido no espaco do mundo estavel
            Vector3 focus = GetFocusPosition();
            float snappedX = Mathf.Round(focus.x * 0.5f) * 2.0f;
            float snappedZ = Mathf.Round(focus.z * 0.5f) * 2.0f;
            _cloudContainer.position = new Vector3(snappedX, 0f, snappedZ);
        }

        private static Mesh BuildLowPolyGasGridMesh(int segX, int segZ, float size)
        {
            Mesh m = new Mesh();
            m.name = "PS1_LowPoly_GasSheet";

            int vCountX = segX + 1;
            int vCountZ = segZ + 1;
            Vector3[] verts = new Vector3[vCountX * vCountZ];
            Vector2[] uvs = new Vector2[vCountX * vCountZ];
            Color[] cols = new Color[vCountX * vCountZ];
            int[] tris = new int[segX * segZ * 6];

            float half = size * 0.5f;
            int v = 0;
            for (int z = 0; z <= segZ; z++)
            {
                float fz = (float)z / segZ;
                for (int x = 0; x <= segX; x++)
                {
                    float fx = (float)x / segX;
                    float px = Mathf.Lerp(-half, half, fx);
                    float pz = Mathf.Lerp(-half, half, fz);
                    // Ondulacao vertical nos vertices para parecer uma camada de vapor low-poly facetada
                    float py = Mathf.Sin(fx * 6.28f + fz * 3.14f) * 0.22f;
                    verts[v] = new Vector3(px, py, pz);
                    uvs[v] = new Vector2(fx, fz);

                    // Desvanece nas bordas externas da folha
                    float dx = fx * 2f - 1f;
                    float dz = fz * 2f - 1f;
                    float edge = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dz * dz));
                    cols[v] = new Color(1f, 1f, 1f, edge);
                    v++;
                }
            }

            int t = 0;
            for (int z = 0; z < segZ; z++)
            {
                for (int x = 0; x < segX; x++)
                {
                    int i0 = z * vCountX + x;
                    int i1 = i0 + 1;
                    int i2 = (z + 1) * vCountX + x;
                    int i3 = i2 + 1;

                    tris[t++] = i0;
                    tris[t++] = i2;
                    tris[t++] = i1;

                    tris[t++] = i1;
                    tris[t++] = i2;
                    tris[t++] = i3;
                }
            }

            m.vertices = verts;
            m.uv = uvs;
            m.colors = cols;
            m.triangles = tris;
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }
    }
}
