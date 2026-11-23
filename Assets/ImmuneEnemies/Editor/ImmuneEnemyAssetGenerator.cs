using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace ImmuneEnemies.Editor
{
    public class ImmuneMaterialSet
    {
        public Material MacrophageFlesh;
        public Material MacrophageMaw;
        public Material MacrophageGutAcid;
        public Material MacrophageTeeth;
        public Material EosinophilMembrane;
        public Material EosinophilGranules;
        public Material EosinophilWings;
    }

    /// <summary>
    /// Gera texturas pixel-art de alta densidade estilo PS1 (256x256, Bayer 4x4 dither + FilterMode.Point)
    /// com veias ramificadas nitidas, sombreamento de cavidade, nucleo bilobado e ossos ensanguentados.
    /// </summary>
    public static class ImmuneEnemyAssetGenerator
    {
        private const string TexturesFolder = "Assets/ImmuneEnemies/Textures";
        private const string MaterialsFolder = "Assets/ImmuneEnemies/Materials";
        private const string PrimaryShaderName = "MapaEstomago/URP/PS1_LivingFlesh";
        private const string FallbackShaderName = "Universal Render Pipeline/Lit";

        private static readonly float[] Bayer4x4 =
        {
             0f / 16f,  8f / 16f,  2f / 16f, 10f / 16f,
            12f / 16f,  4f / 16f, 14f / 16f,  6f / 16f,
             3f / 16f, 11f / 16f,  1f / 16f,  9f / 16f,
            15f / 16f,  7f / 16f, 13f / 16f,  5f / 16f
        };

        private struct VeinSegment
        {
            public Vector2 A;
            public Vector2 B;
            public float Radius;
            public float Intensity;
        }

        public static ImmuneMaterialSet GenerateAll()
        {
            EnsureFolder(TexturesFolder);
            EnsureFolder(MaterialsFolder);

            List<VeinSegment> macroVeins = GenerateBranchingVeinTree(seed: 2048, rootCount: 18, maxDepth: 4, baseRadius: 0.014f);
            List<VeinSegment> eosVeins = GenerateBranchingVeinTree(seed: 5055, rootCount: 12, maxDepth: 3, baseRadius: 0.007f);
            List<VeinSegment> wingVeins = GenerateWingVeinTree(seed: 7711);

            RasterizedVeins macroMap = RasterizeVeinTree(256, macroVeins, tileUV: true);
            RasterizedVeins eosMap = RasterizeVeinTree(256, eosVeins, tileUV: true);
            RasterizedVeins wingMap = RasterizeVeinTree(256, wingVeins, tileUV: false);

            Texture2D texMacroFlesh = WritePs1Texture("tex_macrophage_flesh.png", 256, 0.025f, 28, (u, v) => SampleMacrophageFlesh(u, v, macroMap));
            Texture2D texMacroMaw = WritePs1Texture("tex_macrophage_maw.png", 128, 0.028f, 24, SampleMacrophageMaw);
            Texture2D texMacroAcid = WritePs1Texture("tex_macrophage_gut_acid.png", 128, 0.030f, 24, SampleAcidGut);
            Texture2D texTeeth = WritePs1Texture("tex_immune_bone_teeth.png", 128, 0.024f, 22, SampleBoneTeeth);
            Texture2D texEosBody = WritePs1Texture("tex_eosinophil_membrane.png", 256, 0.024f, 28, (u, v) => SampleEosinophilMembrane(u, v, eosMap));
            Texture2D texEosGran = WritePs1Texture("tex_eosinophil_granules.png", 128, 0.028f, 24, SampleToxicGranules);
            Texture2D texEosWing = WritePs1Texture("tex_eosinophil_wing.png", 256, 0.025f, 26, (u, v) => SampleWingMembrane(u, v, wingMap));

            AssetDatabase.Refresh();

            var set = new ImmuneMaterialSet
            {
                MacrophageFlesh = SaveMaterial("mat_macrophage_flesh.mat", texMacroFlesh, 1.8f, 0.014f, 0.25f, 0.48f, new Color(0.08f, 0.06f, 0.07f)),
                MacrophageMaw = SaveMaterial("mat_macrophage_maw.mat", texMacroMaw, 2.2f, 0.016f, 0.45f, 0.42f, new Color(0.10f, 0.01f, 0.02f)),
                MacrophageGutAcid = SaveMaterial("mat_macrophage_gut_acid.mat", texMacroAcid, 3.8f, 0.022f, 0.45f, 0.55f, new Color(0.32f, 1.15f, 0.12f)),
                MacrophageTeeth = SaveMaterial("mat_immune_bone_teeth.mat", texTeeth, 0f, 0f, 0.22f, 0.54f, new Color(0.06f, 0.05f, 0.04f)),
                EosinophilMembrane = SaveMaterial("mat_eosinophil_membrane.mat", texEosBody, 2.4f, 0.014f, 0.04f, 0.52f, new Color(0.14f, 0.08f, 0.09f)),
                EosinophilGranules = SaveMaterial("mat_eosinophil_granules.mat", texEosGran, 3.8f, 0.016f, 0.25f, 0.50f, new Color(0.24f, 0.30f, 0.04f)),
                EosinophilWings = SaveMaterial("mat_eosinophil_wings.mat", texEosWing, 1.0f, 0.005f, 0.08f, 0.50f, new Color(0.12f, 0.06f, 0.07f))
            };

            AssetDatabase.SaveAssets();
            return set;
        }

        private static Texture2D WritePs1Texture(string fileName, int size, float ditherStrength, int quantLevels, Func<float, float, Color> sampler)
        {
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            for (int y = 0; y < size; y++)
            {
                float v = (float)y / size;
                for (int x = 0; x < size; x++)
                {
                    float u = (float)x / size;
                    Color c = sampler(u, v);
                    float dither = (Bayer4x4[(y & 3) * 4 + (x & 3)] - 0.5f) * ditherStrength;
                    c.r = Quantize(c.r + dither, quantLevels);
                    c.g = Quantize(c.g + dither, quantLevels);
                    c.b = Quantize(c.b + dither, quantLevels);
                    c.a = 1f;
                    tex.SetPixel(x, y, c);
                }
            }
            tex.Apply();

            string path = $"{TexturesFolder}/{fileName}";
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null)
            {
                importer.filterMode = FilterMode.Point;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.mipmapEnabled = false;
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static Material SaveMaterial(string assetName, Texture2D mainTex, float breathSpeed, float breathAmp, float wetness, float ambientFloor, Color emission)
        {
            string path = $"{MaterialsFolder}/{assetName}";
            Shader sh = Shader.Find(PrimaryShaderName) ?? Shader.Find(FallbackShaderName);
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(sh);
                AssetDatabase.CreateAsset(mat, path);
            }
            else
            {
                mat.shader = sh;
            }

            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", mainTex);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
            if (mat.HasProperty("_VertexSnap")) mat.SetFloat("_VertexSnap", 180f);
            if (mat.HasProperty("_BreathSpeed")) mat.SetFloat("_BreathSpeed", breathSpeed);
            if (mat.HasProperty("_BreathAmp")) mat.SetFloat("_BreathAmp", breathAmp);
            if (mat.HasProperty("_SplitAmount")) mat.SetFloat("_SplitAmount", 0f);
            if (mat.HasProperty("_Wetness")) mat.SetFloat("_Wetness", wetness);
            if (mat.HasProperty("_AmbientFloor")) mat.SetFloat("_AmbientFloor", ambientFloor);
            if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", emission);
            if (mat.HasProperty("_UseVertexColor")) mat.SetFloat("_UseVertexColor", 1f);

            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>
        /// SmoothStep estilo GLSL: retorna 0..1 entre edge0 e edge1 (inclusive reverso quando edge0 > edge1).
        /// </summary>
        private static float SmoothStep01(float edge0, float edge1, float x)
        {
            float denom = edge1 - edge0;
            if (Mathf.Abs(denom) < 1e-6f) return x >= edge1 ? 1f : 0f;
            float t = Mathf.Clamp01((x - edge0) / denom);
            return t * t * (3f - 2f * t);
        }

        private static List<VeinSegment> GenerateBranchingVeinTree(int seed, int rootCount, int maxDepth, float baseRadius)
        {
            var rng = new System.Random(seed);
            var list = new List<VeinSegment>(220);

            void SpawnBranch(Vector2 start, float angle, float len, float rad, float inten, int depth)
            {
                int steps = 3;
                Vector2 cur = start;
                float curAng = angle;
                for (int s = 0; s < steps; s++)
                {
                    curAng += (float)(rng.NextDouble() - 0.5) * 0.72f;
                    Vector2 next = cur + new Vector2(Mathf.Cos(curAng), Mathf.Sin(curAng)) * (len / steps);
                    list.Add(new VeinSegment
                    {
                        A = cur,
                        B = next,
                        Radius = rad * Mathf.Lerp(1f, 0.75f, s / (float)steps),
                        Intensity = inten
                    });
                    cur = next;
                }

                if (depth < maxDepth)
                {
                    int branches = depth == 0 ? 2 : (rng.NextDouble() < 0.78 ? 2 : 1);
                    for (int b = 0; b < branches; b++)
                    {
                        float offset = (b == 0 ? 1f : -1f) * (0.42f + (float)rng.NextDouble() * 0.55f);
                        SpawnBranch(cur, curAng + offset, len * (0.72f + (float)rng.NextDouble() * 0.14f), rad * 0.70f, inten * 0.88f, depth + 1);
                    }
                }
            }

            for (int i = 0; i < rootCount; i++)
            {
                Vector2 p = new Vector2((float)rng.NextDouble(), (float)rng.NextDouble());
                float ang = (float)(rng.NextDouble() * Mathf.PI * 2.0);
                SpawnBranch(p, ang, 0.15f + (float)rng.NextDouble() * 0.10f, baseRadius, 1f, 0);
            }
            return list;
        }

        private static List<VeinSegment> GenerateWingVeinTree(int seed)
        {
            var rng = new System.Random(seed);
            var list = new List<VeinSegment>(90);

            void Branch(Vector2 a, float ang, float len, float r, int depth)
            {
                Vector2 b = a + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * len;
                list.Add(new VeinSegment { A = a, B = b, Radius = r, Intensity = 1f - depth * 0.15f });
                if (depth < 3)
                {
                    Branch(b, ang + 0.36f + (float)rng.NextDouble() * 0.25f, len * 0.70f, r * 0.68f, depth + 1);
                    Branch(b, ang - 0.34f - (float)rng.NextDouble() * 0.25f, len * 0.68f, r * 0.68f, depth + 1);
                }
            }

            Branch(new Vector2(0.02f, 0.82f), -0.18f, 0.30f, 0.015f, 0);
            Branch(new Vector2(0.02f, 0.54f), 0.02f, 0.28f, 0.014f, 0);
            Branch(new Vector2(0.02f, 0.28f), 0.22f, 0.26f, 0.013f, 0);
            return list;
        }

        private class RasterizedVeins
        {
            public int Size;
            public float[] Core;
            public float[] Halo;
        }

        private static RasterizedVeins RasterizeVeinTree(int size, List<VeinSegment> segs, bool tileUV)
        {
            float[] core = new float[size * size];
            float[] halo = new float[size * size];
            int minW = tileUV ? -1 : 0;
            int maxW = tileUV ? 1 : 0;

            for (int i = 0; i < segs.Count; i++)
            {
                VeinSegment s = segs[i];
                float maxR = s.Radius * 2.8f;
                for (int wx = minW; wx <= maxW; wx++)
                {
                    for (int wy = minW; wy <= maxW; wy++)
                    {
                        Vector2 a = s.A + new Vector2(wx, wy);
                        Vector2 b = s.B + new Vector2(wx, wy);
                        int x0 = Mathf.Clamp(Mathf.FloorToInt((Mathf.Min(a.x, b.x) - maxR) * size), 0, size - 1);
                        int x1 = Mathf.Clamp(Mathf.CeilToInt((Mathf.Max(a.x, b.x) + maxR) * size), 0, size - 1);
                        int y0 = Mathf.Clamp(Mathf.FloorToInt((Mathf.Min(a.y, b.y) - maxR) * size), 0, size - 1);
                        int y1 = Mathf.Clamp(Mathf.CeilToInt((Mathf.Max(a.y, b.y) + maxR) * size), 0, size - 1);
                        if (x0 > x1 || y0 > y1) continue;

                        for (int py = y0; py <= y1; py++)
                        {
                            float v = (py + 0.5f) / size;
                            int row = py * size;
                            for (int px = x0; px <= x1; px++)
                            {
                                float u = (px + 0.5f) / size;
                                float d = DistToSegment(new Vector2(u, v), a, b);
                                if (d < maxR)
                                {
                                    float c = SmoothStep01(s.Radius * 1.15f, s.Radius * 0.22f, d) * s.Intensity;
                                    float h = SmoothStep01(s.Radius * 2.6f, s.Radius * 0.7f, d) * s.Intensity;
                                    int idx = row + px;
                                    if (c > core[idx]) core[idx] = c;
                                    if (h > halo[idx]) halo[idx] = h;
                                }
                            }
                        }
                    }
                }
            }
            return new RasterizedVeins { Size = size, Core = core, Halo = halo };
        }

        private static float SampleVeinMap(float u, float v, RasterizedVeins map, out float halo)
        {
            int px = Mathf.Clamp(Mathf.FloorToInt(u * map.Size), 0, map.Size - 1);
            int py = Mathf.Clamp(Mathf.FloorToInt(v * map.Size), 0, map.Size - 1);
            int idx = py * map.Size + px;
            halo = map.Halo[idx];
            return map.Core[idx];
        }

        private static float DistToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float sq = ab.sqrMagnitude;
            if (sq < 1e-6f) return Vector2.Distance(p, a);
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / sq);
            return Vector2.Distance(p, a + ab * t);
        }

        private static Color SampleMacrophageFlesh(float u, float v, RasterizedVeins veins)
        {
            float n1 = Mathf.PerlinNoise(u * 8f + 1.3f, v * 8f + 2.7f);
            float n2 = Mathf.PerlinNoise(u * 24f + 5.1f, v * 24f + 9.4f);
            float pores = Mathf.PerlinNoise(u * 52f, v * 52f);

            // Carne cadaverica clara/palida para dar contraste extremo com as veias vermelhas!
            Color paleSkin = new Color(0.86f, 0.84f, 0.85f);
            Color shadowedSkin = new Color(0.56f, 0.54f, 0.58f);
            Color baseCol = Color.Lerp(shadowedSkin, paleSkin, Mathf.Clamp01(n1 * 0.65f + n2 * 0.22f + pores * 0.13f));

            // Halo arroxeado + veia carmesim viva bem desenhada
            float veinCore = SampleVeinMap(u, v, veins, out float veinHalo);
            Color bruisingCol = new Color(0.32f, 0.10f, 0.14f);
            Color crimsonVein = new Color(0.82f, 0.04f, 0.08f);
            Color veinHighlight = new Color(1.00f, 0.24f, 0.22f);

            baseCol = Color.Lerp(baseCol, bruisingCol, veinHalo * 0.65f);
            baseCol = Color.Lerp(baseCol, crimsonVein, veinCore * 0.95f);
            baseCol = Color.Lerp(baseCol, veinHighlight, Mathf.Pow(veinCore, 2.2f) * 0.45f);

            // Dobras escuras sutis
            float foldNoise = SmoothStep01(0.60f, 0.86f, Mathf.PerlinNoise(u * 5f + 12f, v * 12f));
            baseCol = Color.Lerp(baseCol, new Color(0.28f, 0.20f, 0.22f), foldNoise * 0.28f);

            return baseCol;
        }

        private static Color SampleMacrophageMaw(float u, float v)
        {
            float n = Mathf.PerlinNoise(u * 14f, v * 18f);
            float edge = Mathf.Abs(u - 0.5f) * 2f;
            Color throatDark = new Color(0.14f, 0.02f, 0.04f);
            Color wetGum = new Color(0.68f, 0.11f, 0.16f);
            Color fleshRim = new Color(0.82f, 0.26f, 0.30f);

            Color c = Color.Lerp(throatDark, wetGum, SmoothStep01(0.15f, 0.65f, edge));
            c = Color.Lerp(c, fleshRim, SmoothStep01(0.75f, 1.0f, edge) * (0.65f + 0.35f * n));
            return c;
        }

        private static Color SampleAcidGut(float u, float v)
        {
            float n = Mathf.PerlinNoise(u * 6f + 4.1f, v * 6f + 9.2f);
            float bubbles = SmoothStep01(0.58f, 0.76f, Mathf.PerlinNoise(u * 18f + 2f, v * 18f + 7f));
            Color darkBile = new Color(0.12f, 0.38f, 0.04f);
            Color neonAcid = new Color(0.58f, 1.0f, 0.14f);
            Color hotCore = new Color(0.90f, 1.0f, 0.52f);

            Color c = Color.Lerp(darkBile, neonAcid, n);
            return Color.Lerp(c, hotCore, bubbles * 0.92f);
        }

        private static Color SampleBoneTeeth(float u, float v)
        {
            float grain = Mathf.PerlinNoise(u * 10f, v * 28f);
            Color rootBlood = new Color(0.45f, 0.08f, 0.08f);
            Color tartarBone = new Color(0.78f, 0.70f, 0.52f);
            Color sharpTip = new Color(0.98f, 0.96f, 0.86f);

            Color c = Color.Lerp(tartarBone, sharpTip, SmoothStep01(0.22f, 0.90f, v) * (0.78f + 0.22f * grain));
            return Color.Lerp(rootBlood, c, SmoothStep01(0.0f, 0.26f, v));
        }

        private static Color SampleEosinophilMembrane(float u, float v, RasterizedVeins veins)
        {
            float n = Mathf.PerlinNoise(u * 7f + 2.2f, v * 7f + 6.4f);
            float pores = Mathf.PerlinNoise(u * 28f, v * 28f);
            Color paleMembrane = new Color(0.94f, 0.82f, 0.80f);
            Color roseMembrane = new Color(0.76f, 0.55f, 0.57f);
            Color baseCol = Color.Lerp(roseMembrane, paleMembrane, n * 0.7f + pores * 0.3f);

            // Nucleo bilobado em formato de dois rins exatamente na FRENTE (+Z -> u = 0.50, v = 0.50)
            Vector2 p = new Vector2(u, v);
            float lobeL = EvalKidneyLobe(p, new Vector2(0.42f, 0.50f), -0.32f, new Vector2(0.068f, 0.16f));
            float lobeR = EvalKidneyLobe(p, new Vector2(0.58f, 0.50f), 0.32f, new Vector2(0.068f, 0.16f));
            float bridge = SmoothStep01(0.055f, 0.015f, DistToSegment(p, new Vector2(0.43f, 0.43f), new Vector2(0.57f, 0.43f)));
            float nucleusMask = Mathf.Clamp01(Mathf.Max(Mathf.Max(lobeL, lobeR), bridge * 0.75f));

            Color nucleusCol = new Color(0.46f, 0.12f, 0.18f);
            Color nucleusCore = new Color(0.28f, 0.05f, 0.10f);
            baseCol = Color.Lerp(baseCol, nucleusCol, nucleusMask * 0.82f);
            baseCol = Color.Lerp(baseCol, nucleusCore, Mathf.Pow(nucleusMask, 2f) * 0.58f);

            // Capilares finos visiveis na membrana
            float veinCore = SampleVeinMap(u, v, veins, out float veinHalo);
            baseCol = Color.Lerp(baseCol, new Color(0.52f, 0.12f, 0.16f), veinHalo * 0.35f + veinCore * 0.70f);

            if (v < 0.22f)
            {
                baseCol = Color.Lerp(baseCol, new Color(0.44f, 0.22f, 0.26f), (0.22f - v) * 2.2f);
            }

            return baseCol;
        }

        private static float EvalKidneyLobe(Vector2 p, Vector2 center, float tiltRad, Vector2 radii)
        {
            Vector2 d = p - center;
            float cs = Mathf.Cos(tiltRad);
            float sn = Mathf.Sin(tiltRad);
            Vector2 rot = new Vector2(d.x * cs - d.y * sn, d.x * sn + d.y * cs);
            rot.x += Mathf.Cos(rot.y / Mathf.Max(0.01f, radii.y)) * 0.014f;
            float dist = Mathf.Sqrt((rot.x * rot.x) / (radii.x * radii.x) + (rot.y * rot.y) / (radii.y * radii.y));
            return SmoothStep01(1.30f, 0.45f, dist);
        }

        private static Color SampleToxicGranules(float u, float v)
        {
            float n = Mathf.PerlinNoise(u * 8f + 1.8f, v * 8f + 5.1f);
            float edgeGlow = Mathf.Max(Mathf.Abs(u - 0.5f), Mathf.Abs(v - 0.5f)) * 2f;
            Color brightAcid = new Color(0.48f, 1.0f, 0.12f);
            Color deepEmber = new Color(1.0f, 0.22f, 0.04f);
            // Gradiente de laranja-brasa na base/lados ate verde-acido no topo da gema!
            Color baseCrystal = Color.Lerp(deepEmber, brightAcid, Mathf.Clamp01(v * 0.72f + n * 0.35f));
            return Color.Lerp(baseCrystal, new Color(0.95f, 1.0f, 0.55f), SmoothStep01(0.65f, 1.0f, edgeGlow) * 0.40f);
        }

        private static Color SampleWingMembrane(float u, float v, RasterizedVeins veins)
        {
            float n = Mathf.PerlinNoise(u * 11f, v * 11f);
            Color parchment = new Color(0.90f, 0.75f, 0.72f);
            Color darkMembrane = new Color(0.62f, 0.38f, 0.42f);
            Color c = Color.Lerp(darkMembrane, parchment, n);

            float veinCore = SampleVeinMap(u, v, veins, out float veinHalo);
            c = Color.Lerp(c, new Color(0.42f, 0.08f, 0.12f), veinHalo * 0.45f + veinCore * 0.88f);

            if (v < 0.16f)
            {
                c = Color.Lerp(c, new Color(0.48f, 0.22f, 0.26f), (0.16f - v) * 2.5f);
            }
            return c;
        }

        private static float Quantize(float val, int levels)
        {
            return Mathf.Clamp01(Mathf.Round(Mathf.Clamp01(val) * levels) / levels);
        }

        private static void EnsureFolder(string assetPath)
        {
            string[] parts = assetPath.Split('/');
            string cur = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = $"{cur}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(cur, parts[i]);
                }
                cur = next;
            }
        }
    }
}
