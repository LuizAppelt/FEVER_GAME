using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace ImmuneEnemies.Editor
{
    /// <summary>
    /// Construtor de malhas anatomicas low-poly PS1 de alto detalhe para Abade Macrofago e Eosinofilo da Bilis.
    /// Inclui dobras de carne, garras com 4 dedos, cavidade bucal, dentes em gancho,
    /// celula flat-shaded, pustulas cristalinas, asas de morcego com nervuras 3D e pincas curvas.
    /// </summary>
    public static class ImmuneEnemyMeshBuilder
    {
        private const string MeshesFolder = "Assets/ImmuneEnemies/Meshes";

        public static void EnsureMeshesFolder()
        {
            if (!AssetDatabase.IsValidFolder("Assets/ImmuneEnemies"))
                AssetDatabase.CreateFolder("Assets", "ImmuneEnemies");
            if (!AssetDatabase.IsValidFolder(MeshesFolder))
                AssetDatabase.CreateFolder("Assets/ImmuneEnemies", "Meshes");
        }

        // =====================================================================
        // ABADE MACROFAGO
        // =====================================================================

        public static Mesh BuildMacrophageTorso()
        {
            EnsureMeshesFolder();
            List<Vector3> verts = new List<Vector3>();
            List<Vector2> uvs = new List<Vector2>();
            List<Color> cols = new List<Color>();
            List<int> fleshTris = new List<int>();
            List<int> mawTris = new List<int>();

            // Silhueta continua corcunda e monstruosa com peitorais caidos, dobras na barriga e veias em relevo
            int rings = 16;
            int slices = 22;

            for (int r = 0; r <= rings; r++)
            {
                float v = (float)r / rings;
                float y = Mathf.Lerp(0.36f, 2.48f, v);

                // Perfil em gota corcunda arredondada (ombros largos e cupula fechada redonda no topo)
                float baseEnv = Mathf.Pow(Mathf.Sin(Mathf.Lerp(0.30f, Mathf.PI * 0.996f, v)), 0.56f);
                float bellySag = v < 0.65f ? Mathf.Sin((v / 0.65f) * Mathf.PI) * 0.24f : 0f;
                float shoulderBulge = (v > 0.50f && v < 0.90f) ? Mathf.Sin(((v - 0.50f) / 0.40f) * Mathf.PI) * 0.18f : 0f;

                float radiusX = baseEnv * 0.96f + bellySag * 0.36f + shoulderBulge;
                float radiusZ = baseEnv * 0.82f + bellySag * 0.72f;

                // Corcunda inclinada para frente no topo
                float hunchZ = Mathf.Sin(v * Mathf.PI) * 0.08f + Mathf.Pow(Mathf.Max(0f, v - 0.52f), 1.3f) * 0.14f;

                // Fenda da bocarra na frente (Z+), fechando completamente na testa (v>=0.86) e na base (v<=0.12)
                float gap = 0.36f;
                if (v > 0.76f) gap = Mathf.Lerp(0.36f, 0f, Mathf.Clamp01((v - 0.76f) / 0.10f));
                else if (v < 0.12f) gap = Mathf.Lerp(0f, 0.36f, v / 0.12f);

                for (int s = 0; s <= slices; s++)
                {
                    float u = (float)s / slices;
                    float ang = Mathf.Lerp(gap, Mathf.PI * 2f - gap, u);
                    float sinA = Mathf.Sin(ang);
                    float cosA = Mathf.Cos(ang);

                    // Escultura organica de dobras de carne, peitorais caidos e sulco dorsal
                    float localRScale = 1f;

                    // Dois peitorais flacidos latero-frontais em v=[0.50..0.70]
                    float chestBand = Mathf.Exp(-Mathf.Pow((v - 0.60f) / 0.10f, 2f));
                    float chestSides = Mathf.Exp(-Mathf.Pow((Mathf.Abs(sinA) - 0.55f) / 0.28f, 2f)) * (cosA > 0f ? 1f : 0f);
                    localRScale += chestBand * chestSides * 0.16f;

                    // Sulco escuro abaixo do peitoral
                    float underChest = Mathf.Exp(-Mathf.Pow((v - 0.49f) / 0.045f, 2f)) * (cosA > 0f ? 1f : 0f);
                    localRScale -= underChest * 0.09f;

                    // Dobras de gordura nas laterais/costas
                    float ribFolds = Mathf.Sin(v * 26f + Mathf.Abs(sinA) * 2f) * 0.035f * (1f - Mathf.Abs(v - 0.5f));
                    localRScale += ribFolds;

                    // Sulco da coluna vertebral atras (cosA < -0.7)
                    float spineGroove = Mathf.Pow(Mathf.Max(0f, -cosA), 4f) * 0.10f;
                    localRScale -= spineGroove;

                    // Saliencia organica ao redor da borda da boca
                    float lipRimMask = (u < 0.08f || u > 0.92f) ? 1f : 0f;

                    float x = sinA * radiusX * localRScale;
                    float z = cosA * radiusZ * localRScale + hunchZ;

                    // Sombreamento de oclusao (AO) nas dobras + brilho verde digestivo na lateral da barriga!
                    float ao = 1f;
                    ao -= underChest * 0.42f;
                    ao -= spineGroove * 2.2f;
                    ao -= Mathf.Clamp01(-ribFolds * 9f) * 0.22f;
                    ao -= Mathf.Pow(Mathf.Max(0f, 1f - v), 1.8f) * 0.25f;
                    ao = Mathf.Clamp(ao, 0.42f, 1f);

                    Color vc = new Color(ao, ao * 0.96f, ao * 0.98f);
                    if (lipRimMask > 0.5f)
                    {
                        vc = Color.Lerp(vc, new Color(0.75f, 0.38f, 0.42f), 0.55f);
                    }

                    // Manchas verdes translucidas de acido digestivo na barriga lateral (como no concept!)
                    float bellyAcidLeft = Mathf.Exp(-Mathf.Pow((v - 0.34f) / 0.13f, 2f) - Mathf.Pow((sinA + 0.52f) / 0.30f, 2f)) * (cosA > -0.1f ? 1f : 0f);
                    float bellyAcidRight = Mathf.Exp(-Mathf.Pow((v - 0.28f) / 0.11f, 2f) - Mathf.Pow((sinA - 0.58f) / 0.26f, 2f)) * (cosA > -0.1f ? 1f : 0f);
                    float bellyAcidMask = Mathf.Clamp01((bellyAcidLeft + bellyAcidRight) * 1.15f);
                    if (bellyAcidMask > 0.05f)
                    {
                        vc = Color.Lerp(vc, new Color(0.55f, 1.0f, 0.22f), bellyAcidMask * 0.72f);
                    }

                    verts.Add(new Vector3(x, y, z));
                    uvs.Add(new Vector2(u * 2f, v * 2f));
                    cols.Add(vc);
                }
            }

            int stride = slices + 1;
            for (int r = 0; r < rings; r++)
            {
                for (int s = 0; s < slices; s++)
                {
                    int a = r * stride + s;
                    int b = a + stride;
                    fleshTris.Add(a); fleshTris.Add(b); fleshTris.Add(a + 1);
                    fleshTris.Add(a + 1); fleshTris.Add(b); fleshTris.Add(b + 1);
                }
            }

            // Cavidade bucal profunda interna (Submesh 1 = MacrophageMaw) ligando as bordas da fenda por dentro
            int mawStart = verts.Count;
            int mawSlices = 8;
            for (int r = 0; r <= rings; r++)
            {
                float v = (float)r / rings;
                float y = Mathf.Lerp(0.42f, 2.40f, v);
                float baseEnv = Mathf.Sin(Mathf.Lerp(0.24f, Mathf.PI * 0.96f, v));
                float bellySag = v < 0.62f ? Mathf.Sin((v / 0.62f) * Mathf.PI) * 0.28f : 0f;
                float radiusX = baseEnv * 0.90f + bellySag * 0.42f;
                float radiusZ = baseEnv * 0.80f + bellySag * 0.82f;
                float hunchZ = Mathf.Sin(v * Mathf.PI) * 0.08f + Mathf.Pow(Mathf.Max(0f, v - 0.52f), 1.4f) * 0.18f;

                float gap = 0.35f;
                if (v > 0.84f) gap = Mathf.Lerp(0.35f, 0f, Mathf.Clamp01((v - 0.84f) / 0.14f));
                else if (v < 0.12f) gap = Mathf.Lerp(0f, 0.35f, v / 0.12f);

                float edgeX = Mathf.Sin(gap) * radiusX;
                float edgeZ = Mathf.Cos(gap) * radiusZ + hunchZ;
                float deepZ = edgeZ - Mathf.Lerp(0.18f, 0.82f, Mathf.Sin(v * Mathf.PI));

                for (int ms = 0; ms <= mawSlices; ms++)
                {
                    float mu = (float)ms / mawSlices;
                    float mx = Mathf.Lerp(-edgeX, edgeX, mu);
                    float arch = Mathf.Sin(mu * Mathf.PI);
                    float mz = Mathf.Lerp(edgeZ, deepZ, arch);

                    Color mc = Color.Lerp(new Color(0.55f, 0.18f, 0.22f), new Color(0.06f, 0.01f, 0.02f), arch);
                    verts.Add(new Vector3(mx, y, mz));
                    uvs.Add(new Vector2(mu, v));
                    cols.Add(mc);
                }
            }

            int mStride = mawSlices + 1;
            for (int r = 0; r < rings; r++)
            {
                for (int ms = 0; ms < mawSlices; ms++)
                {
                    int a = mawStart + r * mStride + ms;
                    int b = a + mStride;
                    mawTris.Add(a); mawTris.Add(a + 1); mawTris.Add(b);
                    mawTris.Add(a + 1); mawTris.Add(b + 1); mawTris.Add(b);
                }
            }

            return FinalizeTwoSubmeshAssetMesh("mesh_macrophage_torso.asset", "Macrophage_Torso", verts, uvs, cols, fleshTris, mawTris);
        }

        public static Mesh BuildMacrophageJawHalf(float sideSign)
        {
            EnsureMeshesFolder();
            List<Vector3> verts = new List<Vector3>();
            List<Vector2> uvs = new List<Vector2>();
            List<Color> cols = new List<Color>();
            List<int> lipTris = new List<int>();
            List<int> teethTris = new List<int>();

            // Tubo labial organico continuo (sem buracos!) curvado em arco da garganta ate a barriga
            int arcSegs = 14;
            int tubeSides = 8;
            int baseV = verts.Count;

            for (int i = 0; i <= arcSegs; i++)
            {
                float t = (float)i / arcSegs;
                float y = Mathf.Lerp(-0.74f, 0.74f, t);
                float bow = Mathf.Sin(t * Mathf.PI);
                Vector3 center = new Vector3(
                    sideSign * (0.02f + bow * 0.14f),
                    y,
                    0.06f + bow * 0.30f);

                float thickX = Mathf.Lerp(0.05f, 0.14f, bow);
                float thickZ = Mathf.Lerp(0.06f, 0.17f, bow);

                for (int s = 0; s <= tubeSides; s++)
                {
                    float u = (float)s / tubeSides;
                    float ang = u * Mathf.PI * 2f;
                    float lx = Mathf.Cos(ang) * thickX;
                    float lz = Mathf.Sin(ang) * thickZ;

                    // Borda interna (virada para o centro da boca) fica vermelha gengiva, externa fica mais palida
                    float inner = (lx * -sideSign) > 0f ? 1f : 0.3f;
                    Color lipCol = Color.Lerp(new Color(0.68f, 0.42f, 0.44f), new Color(0.74f, 0.14f, 0.18f), inner);
                    verts.Add(center + new Vector3(lx, 0f, lz));
                    uvs.Add(new Vector2(u, t));
                    cols.Add(lipCol);
                }
            }

            int stride = tubeSides + 1;
            for (int i = 0; i < arcSegs; i++)
            {
                for (int s = 0; s < tubeSides; s++)
                {
                    int a = baseV + i * stride + s;
                    int b = a + stride;
                    lipTris.Add(a); lipTris.Add(a + 1); lipTris.Add(b);
                    lipTris.Add(a + 1); lipTris.Add(b + 1); lipTris.Add(b);
                }
            }

            // 11 dentes curvos calcificados em gancho apontando para dentro e ligeiramente para tras
            int toothCount = 11;
            for (int i = 0; i < toothCount; i++)
            {
                float t = Mathf.Lerp(0.08f, 0.92f, i / (float)(toothCount - 1));
                float y = Mathf.Lerp(-0.74f, 0.74f, t);
                float bow = Mathf.Sin(t * Mathf.PI);
                float sizeScale = Mathf.Lerp(0.62f, 1.18f, bow) * ((i % 2 == 0) ? 1.05f : 0.84f);

                Vector3 root = new Vector3(sideSign * (0.02f + bow * 0.10f), y, 0.10f + bow * 0.32f);
                Vector3 mid = root + new Vector3(-sideSign * 0.16f * sizeScale, (i - 5) * 0.008f, 0.03f);
                Vector3 tip = mid + new Vector3(-sideSign * 0.14f * sizeScale, (sideSign > 0 ? 0.02f : -0.02f), -0.05f);

                AppendCurvedClawOrTooth(verts, uvs, cols, teethTris, root, mid, tip, 0.056f * sizeScale,
                    new Color(0.70f, 0.32f, 0.28f), new Color(1.0f, 0.96f, 0.84f), 5);
            }

            string file = sideSign < 0 ? "mesh_macrophage_jaw_L.asset" : "mesh_macrophage_jaw_R.asset";
            return FinalizeTwoSubmeshAssetMesh(file, "Macrophage_Jaw", verts, uvs, cols, lipTris, teethTris);
        }

        public static Mesh BuildMacrophageArm(float sideSign)
        {
            EnsureMeshesFolder();
            List<Vector3> verts = new List<Vector3>();
            List<Vector2> uvs = new List<Vector2>();
            List<Color> cols = new List<Color>();
            List<int> tris = new List<int>();

            // Braco articulado com deltoide, dobra de cotovelo flexionada, antebraco grosso e palma da mao
            Vector3[] path =
            {
                new Vector3(0f, 0.06f, 0f),                         // topo ombro
                new Vector3(sideSign * 0.08f, -0.18f, 0.02f),       // deltoide
                new Vector3(sideSign * 0.12f, -0.46f, 0.06f),       // biceps / cotovelo
                new Vector3(sideSign * 0.06f, -0.74f, 0.18f),       // antebraco inchado
                new Vector3(-sideSign * 0.02f, -1.00f, 0.28f),      // pulso
                new Vector3(-sideSign * 0.06f, -1.16f, 0.34f)       // centro da mao
            };
            float[] radiiX = { 0.26f, 0.31f, 0.25f, 0.29f, 0.21f, 0.22f };
            float[] radiiZ = { 0.26f, 0.30f, 0.24f, 0.27f, 0.19f, 0.16f };
            Color[] ringCols =
            {
                new Color(0.84f, 0.80f, 0.82f),
                new Color(0.95f, 0.92f, 0.94f),
                new Color(0.72f, 0.65f, 0.68f),
                new Color(0.90f, 0.86f, 0.88f),
                new Color(0.78f, 0.70f, 0.72f),
                new Color(0.72f, 0.56f, 0.58f)
            };

            AppendSweptTube(verts, uvs, cols, tris, path, radiiX, radiiZ, ringCols, 10, true);

            // 4 dedos curvados com garras pontiagudas saindo da mao
            Vector3 handPos = path[5];
            Vector3[] fingerOffsets =
            {
                new Vector3(-sideSign * 0.12f, -0.02f, 0.08f),   // polegar opositor
                new Vector3(-sideSign * 0.06f, -0.05f, 0.12f),   // indicador
                new Vector3(0f, -0.06f, 0.10f),                  // medio
                new Vector3(sideSign * 0.08f, -0.05f, 0.06f)     // anelar
            };
            for (int f = 0; f < fingerOffsets.Length; f++)
            {
                Vector3 fRoot = handPos + fingerOffsets[f];
                Vector3 fMid = fRoot + new Vector3(-sideSign * 0.04f, -0.14f, 0.08f);
                Vector3 fTip = fMid + new Vector3(-sideSign * 0.08f, -0.12f, -0.02f);
                AppendCurvedClawOrTooth(verts, uvs, cols, tris, fRoot, fMid, fTip, f == 0 ? 0.058f : 0.050f,
                    new Color(0.74f, 0.58f, 0.60f), new Color(0.45f, 0.18f, 0.20f), 5);
            }

            string file = sideSign < 0 ? "mesh_macrophage_arm_L.asset" : "mesh_macrophage_arm_R.asset";
            return FinalizeAssetMesh(file, "Macrophage_Arm", verts, uvs, cols, tris);
        }

        public static Mesh BuildMacrophageLeg(float sideSign)
        {
            EnsureMeshesFolder();
            List<Vector3> verts = new List<Vector3>();
            List<Vector2> uvs = new List<Vector2>();
            List<Color> cols = new List<Color>();
            List<int> tris = new List<int>();

            // Perna atarracada com joelho saliente e pe largo plantado no chao (y = -0.62)
            Vector3[] path =
            {
                new Vector3(0f, 0.04f, 0f),
                new Vector3(sideSign * 0.03f, -0.20f, 0.06f),   // coxa / joelho
                new Vector3(sideSign * 0.04f, -0.44f, 0.02f),   // panturrilha
                new Vector3(sideSign * 0.04f, -0.58f, 0.08f)    // tornozelo / dorso do pe
            };
            float[] rx = { 0.34f, 0.31f, 0.27f, 0.30f };
            float[] rz = { 0.34f, 0.32f, 0.27f, 0.36f };
            Color[] rc =
            {
                new Color(0.78f, 0.74f, 0.76f),
                new Color(0.92f, 0.88f, 0.90f),
                new Color(0.74f, 0.68f, 0.70f),
                new Color(0.68f, 0.54f, 0.56f)
            };

            AppendSweptTube(verts, uvs, cols, tris, path, rx, rz, rc, 10, true);

            // Base do pe achatada + 4 dedos gordinhos na frente (+Z)
            Vector3 footCenter = new Vector3(sideSign * 0.04f, -0.56f, 0.14f);
            for (int t = 0; t < 4; t++)
            {
                float lat = Mathf.Lerp(-0.18f, 0.18f, t / 3f);
                Vector3 tRoot = footCenter + new Vector3(lat, -0.01f, 0.18f);
                Vector3 tMid = tRoot + new Vector3(lat * 0.2f, -0.02f, 0.12f);
                Vector3 tTip = tMid + new Vector3(0f, -0.03f, 0.10f);
                AppendCurvedClawOrTooth(verts, uvs, cols, tris, tRoot, tMid, tTip, 0.072f,
                    new Color(0.78f, 0.68f, 0.70f), new Color(0.48f, 0.22f, 0.24f), 5);
            }

            string file = sideSign < 0 ? "mesh_macrophage_leg_L.asset" : "mesh_macrophage_leg_R.asset";
            return FinalizeAssetMesh(file, "Macrophage_Leg", verts, uvs, cols, tris);
        }

        public static Mesh BuildMacrophageAcidCore()
        {
            EnsureMeshesFolder();
            List<Vector3> verts = new List<Vector3>();
            List<Vector2> uvs = new List<Vector2>();
            List<Color> cols = new List<Color>();
            List<int> tris = new List<int>();

            int rings = 8;
            int slices = 10;
            for (int r = 0; r <= rings; r++)
            {
                float v = (float)r / rings;
                float phi = v * Mathf.PI;
                for (int s = 0; s <= slices; s++)
                {
                    float u = (float)s / slices;
                    float theta = u * Mathf.PI * 2f;
                    float blob = 1f + 0.18f * Mathf.Sin(phi * 4f + theta * 3f) * Mathf.Cos(theta * 2f);
                    Vector3 p = new Vector3(
                        Mathf.Sin(phi) * Mathf.Cos(theta) * 0.34f * blob,
                        Mathf.Cos(phi) * 0.56f * blob,
                        Mathf.Sin(phi) * Mathf.Sin(theta) * 0.28f * blob);
                    verts.Add(p);
                    uvs.Add(new Vector2(u, v));
                    cols.Add(Color.Lerp(new Color(0.42f, 0.95f, 0.15f), new Color(0.90f, 1.0f, 0.45f), blob - 0.8f));
                }
            }

            int stride = slices + 1;
            for (int r = 0; r < rings; r++)
            {
                for (int s = 0; s < slices; s++)
                {
                    int a = r * stride + s;
                    int b = a + stride;
                    tris.Add(a); tris.Add(a + 1); tris.Add(b);
                    tris.Add(a + 1); tris.Add(b + 1); tris.Add(b);
                }
            }
            return FinalizeAssetMesh("mesh_macrophage_gut.asset", "Macrophage_GutCore", verts, uvs, cols, tris);
        }

        public static Mesh BuildMacrophageSlimeStrands()
        {
            EnsureMeshesFolder();
            List<Vector3> verts = new List<Vector3>();
            List<Vector2> uvs = new List<Vector2>();
            List<Color> cols = new List<Color>();
            List<int> tris = new List<int>();

            // 5 fios finos de muco acido esticando entre as bordas da bocarra vertical
            var strands = new (Vector3 a, Vector3 b, float sag, float r)[]
            {
                (new Vector3(-0.16f,  0.46f, 0.04f), new Vector3( 0.15f,  0.34f, 0.05f), 0.08f, 0.016f),
                (new Vector3( 0.18f,  0.18f, 0.08f), new Vector3(-0.19f,  0.04f, 0.09f), 0.11f, 0.020f),
                (new Vector3(-0.17f, -0.10f, 0.08f), new Vector3( 0.17f, -0.22f, 0.07f), 0.09f, 0.018f),
                (new Vector3( 0.12f, -0.34f, 0.05f), new Vector3(-0.14f, -0.46f, 0.04f), 0.07f, 0.014f),
                (new Vector3(-0.04f,  0.54f, 0.02f), new Vector3( 0.04f,  0.16f, 0.09f), 0.03f, 0.014f),
            };

            Color slimeCol = new Color(0.65f, 1.0f, 0.25f);
            for (int i = 0; i < strands.Length; i++)
            {
                var st = strands[i];
                Vector3 mid = (st.a + st.b) * 0.5f + new Vector3(0f, -st.sag, 0.03f);
                AppendTaperedBoneStrut(verts, uvs, cols, tris, st.a, mid, st.r, st.r * 0.55f, slimeCol, slimeCol);
                AppendTaperedBoneStrut(verts, uvs, cols, tris, mid, st.b, st.r * 0.55f, st.r, slimeCol, slimeCol);
            }

            return FinalizeAssetMesh("mesh_macrophage_slime.asset", "Macrophage_SlimeStrands", verts, uvs, cols, tris);
        }

        // =====================================================================
        // EOSINOFILO DA BILIS
        // =====================================================================

        public static Mesh BuildEosinophilFacetedCell()
        {
            EnsureMeshesFolder();
            int rings = 9;
            int slices = 14;

            Vector3[,] gridP = new Vector3[rings + 1, slices + 1];
            Vector2[,] gridUV = new Vector2[rings + 1, slices + 1];
            Color[,] gridCol = new Color[rings + 1, slices + 1];

            for (int r = 0; r <= rings; r++)
            {
                float v = (float)r / rings;
                float phi = v * Mathf.PI;
                for (int s = 0; s <= slices; s++)
                {
                    float u = (float)s / slices;
                    // Alinha u = 0.5 exatamente para a FRENTE (+Z) para o nucleo bilobado da textura aparecer na cara!
                    float theta = u * Mathf.PI * 2f - Mathf.PI * 0.5f;

                    float lump = 1f + 0.055f * Mathf.Sin(phi * 3f + theta * 2f) + 0.03f * Mathf.Cos(theta * 5f);
                    Vector3 p = new Vector3(
                        Mathf.Sin(phi) * Mathf.Cos(theta) * 0.74f * lump,
                        Mathf.Cos(phi) * 0.68f * lump,
                        Mathf.Sin(phi) * Mathf.Sin(theta) * 0.74f * lump);

                    gridP[r, s] = p;
                    gridUV[r, s] = new Vector2(u, 1f - v);

                    // AO na parte de baixo (mandibulas) e leve brilho rosado em cima
                    Color top = new Color(1.0f, 0.96f, 0.96f);
                    Color bot = new Color(0.64f, 0.44f, 0.48f);
                    gridCol[r, s] = Color.Lerp(top, bot, Mathf.Pow(v, 1.3f));
                }
            }

            // Constroi triangulos com vertices desacoplados para FLAT SHADING perfeito estilo PS1 (identico ao concept!)
            List<Vector3> verts = new List<Vector3>();
            List<Vector2> uvs = new List<Vector2>();
            List<Color> cols = new List<Color>();
            List<int> tris = new List<int>();

            for (int r = 0; r < rings; r++)
            {
                for (int s = 0; s < slices; s++)
                {
                    AddFlatTriOutward(verts, uvs, cols, tris,
                        gridP[r, s], gridP[r, s + 1], gridP[r + 1, s],
                        gridUV[r, s], gridUV[r, s + 1], gridUV[r + 1, s],
                        gridCol[r, s], gridCol[r, s + 1], gridCol[r + 1, s]);

                    AddFlatTriOutward(verts, uvs, cols, tris,
                        gridP[r, s + 1], gridP[r + 1, s + 1], gridP[r + 1, s],
                        gridUV[r, s + 1], gridUV[r + 1, s + 1], gridUV[r + 1, s],
                        gridCol[r, s + 1], gridCol[r + 1, s + 1], gridCol[r + 1, s]);
                }
            }

            return FinalizeAssetMesh("mesh_eosinophil_cell.asset", "Eosinophil_Cell", verts, uvs, cols, tris);
        }

        public static Mesh BuildEosinophilCrystalPustule()
        {
            EnsureMeshesFolder();
            List<Vector3> verts = new List<Vector3>();
            List<Vector2> uvs = new List<Vector2>();
            List<Color> cols = new List<Color>();
            List<int> rimTris = new List<int>();
            List<int> crystalTris = new List<int>();

            const int sides = 6;
            float baseR = 0.17f;
            float midR = 0.145f;
            float topR = 0.08f;

            // Submesh 0: Colarinho de carne inflamada/escura na base da pustula
            for (int i = 0; i < sides; i++)
            {
                float a0 = (i / (float)sides) * Mathf.PI * 2f;
                float a1 = ((i + 1) / (float)sides) * Mathf.PI * 2f;

                Vector3 p0Out = new Vector3(Mathf.Cos(a0) * baseR * 1.15f, -0.05f, Mathf.Sin(a0) * baseR * 1.15f);
                Vector3 p1Out = new Vector3(Mathf.Cos(a1) * baseR * 1.15f, -0.05f, Mathf.Sin(a1) * baseR * 1.15f);
                Vector3 p0In = new Vector3(Mathf.Cos(a0) * baseR * 0.85f, 0.03f, Mathf.Sin(a0) * baseR * 0.85f);
                Vector3 p1In = new Vector3(Mathf.Cos(a1) * baseR * 0.85f, 0.03f, Mathf.Sin(a1) * baseR * 0.85f);

                Color darkFlesh = new Color(0.45f, 0.16f, 0.18f);
                AddFlatQuad(verts, uvs, cols, rimTris, p0Out, p0In, p1In, p1Out,
                    new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0),
                    darkFlesh, darkFlesh, darkFlesh, darkFlesh);
            }

            // Submesh 1: Gema cristalina toxica flat-shaded de 3 andares (base -> cintura -> topo facetado)
            Vector3 apex = new Vector3(0.01f, 0.17f, -0.01f);
            Color baseC = new Color(0.95f, 0.42f, 0.12f);
            Color midC = new Color(0.68f, 1.0f, 0.18f);
            Color tipC = new Color(0.92f, 1.0f, 0.55f);

            for (int i = 0; i < sides; i++)
            {
                float a0 = (i / (float)sides) * Mathf.PI * 2f;
                float a1 = ((i + 1) / (float)sides) * Mathf.PI * 2f;

                Vector3 b0 = new Vector3(Mathf.Cos(a0) * baseR * 0.85f, 0.01f, Mathf.Sin(a0) * baseR * 0.85f);
                Vector3 b1 = new Vector3(Mathf.Cos(a1) * baseR * 0.85f, 0.01f, Mathf.Sin(a1) * baseR * 0.85f);
                Vector3 m0 = new Vector3(Mathf.Cos(a0) * midR, 0.085f, Mathf.Sin(a0) * midR);
                Vector3 m1 = new Vector3(Mathf.Cos(a1) * midR, 0.085f, Mathf.Sin(a1) * midR);
                Vector3 t0 = new Vector3(Mathf.Cos(a0) * topR, 0.145f, Mathf.Sin(a0) * topR);
                Vector3 t1 = new Vector3(Mathf.Cos(a1) * topR, 0.145f, Mathf.Sin(a1) * topR);

                AddFlatQuad(verts, uvs, cols, crystalTris, b0, m0, m1, b1,
                    new Vector2(0, 0), new Vector2(0, 0.5f), new Vector2(1, 0.5f), new Vector2(1, 0),
                    baseC, midC, midC, baseC);

                AddFlatQuad(verts, uvs, cols, crystalTris, m0, t0, t1, m1,
                    new Vector2(0, 0.5f), new Vector2(0, 0.9f), new Vector2(1, 0.9f), new Vector2(1, 0.5f),
                    midC, tipC, tipC, midC);

                AddFlatTri(verts, uvs, cols, crystalTris, t0, apex, t1,
                    new Vector2(0, 0.9f), new Vector2(0.5f, 1f), new Vector2(1, 0.9f),
                    tipC, tipC, tipC);
            }

            return FinalizeTwoSubmeshAssetMesh("mesh_eosinophil_granule.asset", "Eosinophil_CrystalPustule",
                verts, uvs, cols, rimTris, crystalTris);
        }

        public static Mesh BuildEosinophilCiliatedWing(float sideSign)
        {
            EnsureMeshesFolder();
            List<Vector3> verts = new List<Vector3>();
            List<Vector2> uvs = new List<Vector2>();
            List<Color> cols = new List<Color>();
            List<int> memTris = new List<int>();
            List<int> boneTris = new List<int>();

            // Ponta 1 (ombro superior), Ponta 2 (extremo externo), Ponta 3 (lóbulo medio), Ponta 4 (lóbulo interno)
            Vector3 root = new Vector3(0f, 0.04f, 0f);
            Vector3 topElbow = new Vector3(sideSign * 0.58f, 0.42f, -0.06f);
            Vector3 topOuter = new Vector3(sideSign * 1.44f, 0.48f, -0.22f);

            Vector3 rib1Tip = new Vector3(sideSign * 1.28f, -0.10f, -0.20f);
            Vector3 rib2Tip = new Vector3(sideSign * 0.86f, -0.18f, -0.14f);
            Vector3 rib3Tip = new Vector3(sideSign * 0.36f, -0.20f, -0.06f);

            // Pontos concavos intermediarios entre os dedos/nervuras da asa (borda recortada tipo morcego!)
            Vector3 web12Mid = (topOuter + rib1Tip) * 0.5f + new Vector3(-sideSign * 0.10f, 0.04f, 0.02f);
            Vector3 web23Mid = (rib1Tip + rib2Tip) * 0.5f + new Vector3(0f, 0.09f, 0.02f);
            Vector3 web34Mid = (rib2Tip + rib3Tip) * 0.5f + new Vector3(0f, 0.08f, 0.02f);
            Vector3 webRootMid = (rib3Tip + new Vector3(0f, -0.18f, 0f)) * 0.5f + new Vector3(0f, 0.06f, 0f);

            // Submesh 1: Ossatura / nervuras 3D articuladas da asa (espinha superior + 3 dedos diagonais)
            Color boneDark = new Color(0.76f, 0.56f, 0.58f);
            Color boneTip = new Color(0.95f, 0.84f, 0.82f);

            AppendTaperedBoneStrut(verts, uvs, cols, boneTris, root, topElbow, 0.045f, 0.032f, boneDark, boneTip);
            AppendTaperedBoneStrut(verts, uvs, cols, boneTris, topElbow, topOuter, 0.032f, 0.015f, boneTip, boneTip);
            AppendTaperedBoneStrut(verts, uvs, cols, boneTris, topElbow, rib1Tip, 0.026f, 0.012f, boneDark, boneTip);
            AppendTaperedBoneStrut(verts, uvs, cols, boneTris, root + new Vector3(sideSign * 0.14f, 0.05f, 0f), rib2Tip, 0.026f, 0.012f, boneDark, boneTip);
            AppendTaperedBoneStrut(verts, uvs, cols, boneTris, root, rib3Tip, 0.024f, 0.012f, boneDark, boneTip);

            // Submesh 0: Membrana em leque ligando todos os gomos
            Color memLight = new Color(0.96f, 0.88f, 0.86f);
            Color memShadow = new Color(0.78f, 0.56f, 0.60f);

            void TriMem(Vector3 a, Vector3 b, Vector3 c, Vector2 uva, Vector2 uvb, Vector2 uvc, Color ca, Color cb, Color cc)
            {
                if (sideSign > 0f) AddFlatTri(verts, uvs, cols, memTris, a, b, c, uva, uvb, uvc, ca, cb, cc);
                else AddFlatTri(verts, uvs, cols, memTris, a, c, b, uva, uvc, uvb, ca, cc, cb);
            }

            Vector3 ribBase = root + new Vector3(sideSign * 0.14f, 0.05f, 0f);
            // Gomo externo (entre topOuter e rib1Tip)
            TriMem(topElbow, topOuter, web12Mid, new Vector2(0.4f, 0.9f), new Vector2(1f, 0.95f), new Vector2(0.88f, 0.45f), memLight, memLight, memShadow);
            TriMem(topElbow, web12Mid, rib1Tip, new Vector2(0.4f, 0.9f), new Vector2(0.88f, 0.45f), new Vector2(0.82f, 0.08f), memLight, memShadow, memShadow);
            // Gomo medio-externo (entre rib1Tip e rib2Tip)
            TriMem(topElbow, rib1Tip, web23Mid, new Vector2(0.4f, 0.9f), new Vector2(0.82f, 0.08f), new Vector2(0.65f, 0.22f), memLight, memShadow, memShadow);
            TriMem(topElbow, web23Mid, ribBase, new Vector2(0.4f, 0.9f), new Vector2(0.65f, 0.22f), new Vector2(0.15f, 0.55f), memLight, memShadow, memLight);
            TriMem(ribBase, web23Mid, rib2Tip, new Vector2(0.15f, 0.55f), new Vector2(0.65f, 0.22f), new Vector2(0.55f, 0.05f), memLight, memShadow, memShadow);
            // Gomo interno (entre rib2Tip e rib3Tip)
            TriMem(root, topElbow, ribBase, new Vector2(0f, 0.55f), new Vector2(0.4f, 0.9f), new Vector2(0.15f, 0.55f), memLight, memLight, memLight);
            TriMem(ribBase, rib2Tip, web34Mid, new Vector2(0.15f, 0.55f), new Vector2(0.55f, 0.05f), new Vector2(0.38f, 0.18f), memLight, memShadow, memShadow);
            TriMem(root, ribBase, web34Mid, new Vector2(0f, 0.55f), new Vector2(0.15f, 0.55f), new Vector2(0.38f, 0.18f), memLight, memLight, memShadow);
            TriMem(root, web34Mid, rib3Tip, new Vector2(0f, 0.55f), new Vector2(0.38f, 0.18f), new Vector2(0.24f, 0.05f), memLight, memShadow, memShadow);
            TriMem(root, rib3Tip, webRootMid, new Vector2(0f, 0.55f), new Vector2(0.24f, 0.05f), new Vector2(0.12f, 0.16f), memLight, memShadow, memShadow);

            // 16 cilios organicos finos e curvados ao longo da borda inferior recortada!
            Vector3[] edgePts = { webRootMid, rib3Tip, web34Mid, rib2Tip, web23Mid, rib1Tip, web12Mid, topOuter };
            for (int seg = 0; seg < edgePts.Length - 1; seg++)
            {
                int ciliaPerSeg = seg == 6 ? 3 : 2;
                for (int c = 0; c < ciliaPerSeg; c++)
                {
                    float lt = (c + 0.45f) / ciliaPerSeg;
                    Vector3 cRoot = Vector3.Lerp(edgePts[seg], edgePts[seg + 1], lt);
                    float cLen = 0.16f + ((seg + c) % 3) * 0.06f;
                    Vector3 cMid = cRoot + new Vector3(sideSign * 0.02f, -cLen * 0.55f, -0.02f);
                    Vector3 cTip = cMid + new Vector3(sideSign * 0.05f, -cLen * 0.50f, -0.03f);
                    AppendCurvedClawOrTooth(verts, uvs, cols, boneTris, cRoot, cMid, cTip, 0.015f,
                        new Color(0.86f, 0.64f, 0.66f), new Color(0.98f, 0.86f, 0.84f), 3);
                }
            }

            string file = sideSign < 0 ? "mesh_eosinophil_wing_L.asset" : "mesh_eosinophil_wing_R.asset";
            return FinalizeTwoSubmeshAssetMesh(file, "Eosinophil_Wing", verts, uvs, cols, memTris, boneTris);
        }

        public static Mesh BuildEosinophilMandibleCluster()
        {
            EnsureMeshesFolder();
            List<Vector3> verts = new List<Vector3>();
            List<Vector2> uvs = new List<Vector2>();
            List<Color> cols = new List<Color>();
            List<int> tris = new List<int>();

            Color rootCol = new Color(0.55f, 0.25f, 0.24f);
            Color tipCol = new Color(0.96f, 0.90f, 0.78f);

            // 2 grandes pincas laterais em forma de foice curva (como no concept art!)
            AppendCurvedClawOrTooth(verts, uvs, cols, tris,
                new Vector3(-0.26f, 0.06f, 0.10f),
                new Vector3(-0.24f, -0.18f, 0.14f),
                new Vector3(-0.08f, -0.38f, 0.06f),
                0.085f, rootCol, tipCol, 5);

            AppendCurvedClawOrTooth(verts, uvs, cols, tris,
                new Vector3(0.26f, 0.06f, 0.10f),
                new Vector3(0.24f, -0.18f, 0.14f),
                new Vector3(0.08f, -0.38f, 0.06f),
                0.085f, rootCol, tipCol, 5);

            // 4 dentinhos afiados centrais entre as pincas
            float[] xs = { -0.11f, -0.04f, 0.04f, 0.11f };
            for (int i = 0; i < xs.Length; i++)
            {
                float h = (i == 1 || i == 2) ? 0.19f : 0.23f;
                Vector3 r = new Vector3(xs[i], 0.03f, 0.15f);
                Vector3 m = r + new Vector3(0f, -h * 0.55f, 0.02f);
                Vector3 t = r + new Vector3(-xs[i] * 0.25f, -h, -0.02f);
                AppendCurvedClawOrTooth(verts, uvs, cols, tris, r, m, t, 0.034f, rootCol, tipCol, 4);
            }

            return FinalizeAssetMesh("mesh_eosinophil_mandibles.asset", "Eosinophil_Mandibles", verts, uvs, cols, tris);
        }

        // =====================================================================
        // HELPERS GEOMETRICOS
        // =====================================================================

        private static void AppendSweptTube(
            List<Vector3> verts, List<Vector2> uvs, List<Color> cols, List<int> tris,
            Vector3[] centers, float[] rx, float[] rz, Color[] ringCols, int sides, bool capEnds)
        {
            int baseIdx = verts.Count;
            int rings = centers.Length;
            for (int r = 0; r < rings; r++)
            {
                float v = r / (float)(rings - 1);
                for (int s = 0; s <= sides; s++)
                {
                    float u = s / (float)sides;
                    float ang = u * Mathf.PI * 2f;
                    Vector3 offset = new Vector3(Mathf.Cos(ang) * rx[r], 0f, Mathf.Sin(ang) * rz[r]);
                    verts.Add(centers[r] + offset);
                    uvs.Add(new Vector2(u, v * 2f));
                    cols.Add(ringCols[r]);
                }
            }

            int stride = sides + 1;
            for (int r = 0; r < rings - 1; r++)
            {
                for (int s = 0; s < sides; s++)
                {
                    int a = baseIdx + r * stride + s;
                    int b = a + stride;
                    tris.Add(a); tris.Add(a + 1); tris.Add(b);
                    tris.Add(a + 1); tris.Add(b + 1); tris.Add(b);
                }
            }

            if (capEnds)
            {
                int topCenter = verts.Count;
                verts.Add(centers[0] + new Vector3(0f, 0.05f, 0f));
                uvs.Add(new Vector2(0.5f, 0f));
                cols.Add(ringCols[0]);
                for (int s = 0; s < sides; s++)
                {
                    tris.Add(topCenter); tris.Add(baseIdx + s + 1); tris.Add(baseIdx + s);
                }

                int botCenter = verts.Count;
                verts.Add(centers[rings - 1] - new Vector3(0f, 0.04f, 0f));
                uvs.Add(new Vector2(0.5f, 1f));
                cols.Add(ringCols[rings - 1]);
                int lastRing = baseIdx + (rings - 1) * stride;
                for (int s = 0; s < sides; s++)
                {
                    tris.Add(botCenter); tris.Add(lastRing + s); tris.Add(lastRing + s + 1);
                }
            }
        }

        private static void AppendTaperedBoneStrut(
            List<Vector3> verts, List<Vector2> uvs, List<Color> cols, List<int> tris,
            Vector3 aPos, Vector3 bPos, float rA, float rB, Color colA, Color colB)
        {
            Vector3 dir = (bPos - aPos).normalized;
            Vector3 refAxis = Mathf.Abs(dir.y) > 0.85f ? Vector3.forward : Vector3.up;
            Vector3 right = Vector3.Cross(dir, refAxis).normalized;
            Vector3 fwd = Vector3.Cross(right, dir).normalized;

            const int sides = 5;
            int idxA = verts.Count;
            for (int i = 0; i <= sides; i++)
            {
                float u = i / (float)sides;
                float ang = u * Mathf.PI * 2f;
                Vector3 radial = right * Mathf.Cos(ang) + fwd * Mathf.Sin(ang);
                verts.Add(aPos + radial * rA); uvs.Add(new Vector2(u, 0.2f)); cols.Add(colA);
                verts.Add(bPos + radial * rB); uvs.Add(new Vector2(u, 0.9f)); cols.Add(colB);
            }
            for (int i = 0; i < sides; i++)
            {
                int i0 = idxA + i * 2;
                tris.Add(i0); tris.Add(i0 + 2); tris.Add(i0 + 1);
                tris.Add(i0 + 1); tris.Add(i0 + 2); tris.Add(i0 + 3);
            }
        }

        private static void AppendCurvedClawOrTooth(
            List<Vector3> verts, List<Vector2> uvs, List<Color> cols, List<int> tris,
            Vector3 rootPos, Vector3 midPos, Vector3 tipPos, float baseRadius, Color rootCol, Color tipCol, int sides)
        {
            Vector3 d0 = (midPos - rootPos).normalized;
            Vector3 d1 = (tipPos - midPos).normalized;
            Vector3 r0 = Vector3.Cross(d0, Mathf.Abs(d0.y) > 0.88f ? Vector3.forward : Vector3.up).normalized;
            Vector3 f0 = Vector3.Cross(r0, d0).normalized;
            Vector3 r1 = Vector3.Cross(d1, Mathf.Abs(d1.y) > 0.88f ? Vector3.forward : Vector3.up).normalized;
            Vector3 f1 = Vector3.Cross(r1, d1).normalized;

            int ring0 = verts.Count;
            for (int i = 0; i <= sides; i++)
            {
                float ang = (i / (float)sides) * Mathf.PI * 2f;
                verts.Add(rootPos + (r0 * Mathf.Cos(ang) + f0 * Mathf.Sin(ang)) * baseRadius);
                uvs.Add(new Vector2(i / (float)sides, 0f));
                cols.Add(rootCol);
            }

            int ring1 = verts.Count;
            Color midCol = Color.Lerp(rootCol, tipCol, 0.55f);
            for (int i = 0; i <= sides; i++)
            {
                float ang = (i / (float)sides) * Mathf.PI * 2f;
                verts.Add(midPos + (r1 * Mathf.Cos(ang) + f1 * Mathf.Sin(ang)) * (baseRadius * 0.55f));
                uvs.Add(new Vector2(i / (float)sides, 0.55f));
                cols.Add(midCol);
            }

            int tipIdx = verts.Count;
            verts.Add(tipPos);
            uvs.Add(new Vector2(0.5f, 1f));
            cols.Add(tipCol);

            for (int i = 0; i < sides; i++)
            {
                tris.Add(ring0 + i); tris.Add(ring0 + i + 1); tris.Add(ring1 + i);
                tris.Add(ring0 + i + 1); tris.Add(ring1 + i + 1); tris.Add(ring1 + i);
                tris.Add(ring1 + i); tris.Add(ring1 + i + 1); tris.Add(tipIdx);
            }
        }

        private static void AddFlatTri(
            List<Vector3> verts, List<Vector2> uvs, List<Color> cols, List<int> tris,
            Vector3 a, Vector3 b, Vector3 c,
            Vector2 uva, Vector2 uvb, Vector2 uvc,
            Color ca, Color cb, Color cc)
        {
            int idx = verts.Count;
            verts.Add(a); uvs.Add(uva); cols.Add(ca);
            verts.Add(b); uvs.Add(uvb); cols.Add(cb);
            verts.Add(c); uvs.Add(uvc); cols.Add(cc);
            tris.Add(idx); tris.Add(idx + 1); tris.Add(idx + 2);
        }

        private static void AddFlatTriOutward(
            List<Vector3> verts, List<Vector2> uvs, List<Color> cols, List<int> tris,
            Vector3 a, Vector3 b, Vector3 c,
            Vector2 uva, Vector2 uvb, Vector2 uvc,
            Color ca, Color cb, Color cc)
        {
            Vector3 n = Vector3.Cross(b - a, c - a);
            Vector3 centroid = (a + b + c) * 0.3333f;
            if (Vector3.Dot(n, centroid) < 0f)
            {
                AddFlatTri(verts, uvs, cols, tris, a, c, b, uva, uvc, uvb, ca, cc, cb);
            }
            else
            {
                AddFlatTri(verts, uvs, cols, tris, a, b, c, uva, uvb, uvc, ca, cb, cc);
            }
        }

        private static void AddFlatQuad(
            List<Vector3> verts, List<Vector2> uvs, List<Color> cols, List<int> tris,
            Vector3 a, Vector3 b, Vector3 c, Vector3 d,
            Vector2 uva, Vector2 uvb, Vector2 uvc, Vector2 uvd,
            Color ca, Color cb, Color cc, Color cd)
        {
            AddFlatTri(verts, uvs, cols, tris, a, b, c, uva, uvb, uvc, ca, cb, cc);
            AddFlatTri(verts, uvs, cols, tris, a, c, d, uva, uvc, uvd, ca, cc, cd);
        }

        private static Mesh FinalizeAssetMesh(string fileName, string meshName, List<Vector3> verts, List<Vector2> uvs, List<Color> cols, List<int> tris)
        {
            Mesh mesh = new Mesh { name = meshName };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetColors(cols);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            string path = $"{MeshesFolder}/{fileName}";
            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null)
            {
                existing.Clear();
                EditorUtility.CopySerialized(mesh, existing);
                UnityEngine.Object.DestroyImmediate(mesh);
                EditorUtility.SetDirty(existing);
                return existing;
            }

            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        private static Mesh FinalizeTwoSubmeshAssetMesh(
            string fileName,
            string meshName,
            List<Vector3> verts,
            List<Vector2> uvs,
            List<Color> cols,
            List<int> sub0,
            List<int> sub1)
        {
            Mesh mesh = new Mesh { name = meshName, subMeshCount = 2 };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetColors(cols);
            mesh.SetTriangles(sub0, 0);
            mesh.SetTriangles(sub1, 1);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            string path = $"{MeshesFolder}/{fileName}";
            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null)
            {
                existing.Clear();
                EditorUtility.CopySerialized(mesh, existing);
                UnityEngine.Object.DestroyImmediate(mesh);
                EditorUtility.SetDirty(existing);
                return existing;
            }

            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }
    }
}
