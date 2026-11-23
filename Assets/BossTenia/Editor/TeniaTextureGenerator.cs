using System;
using System.IO;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace BossTenia
{
    /// <summary>
    /// Holds all generated PS1-era materials for the Boss Tenia encounter.
    /// </summary>
    [Serializable]
    public class TeniaMaterialSet
    {
        public Material PaleSkin;
        public Material AcidOrgan;
        public Material ScolexMaw;
        public Material Teeth;
        public Material Vestments;
        public Material RustedIron;
    }

    /// <summary>
    /// Deterministically generates PS1-era pixel-art textures and URP PS1_LivingFlesh materials for Boss Tenia.
    /// </summary>
    public static class TeniaTextureGenerator
    {
        private const string TexturesFolder = "Assets/BossTenia/Textures";
        private const string MaterialsFolder = "Assets/BossTenia/Materials";

        private const string PrimaryShaderName = "MapaEstomago/URP/PS1_LivingFlesh";
        private const string FallbackShaderName = "Universal Render Pipeline/Lit";

        private delegate Color PixelSampler(int x, int y, int width, int height);

        private readonly struct TextureSpec
        {
            public readonly string FileName;
            public readonly int Size;
            public readonly bool ClampVertical;
            public readonly float DitherStrength;
            public readonly int QuantLevels;
            public readonly PixelSampler Sampler;

            public TextureSpec(string fileName, int size, bool clampVertical, float ditherStrength, int quantLevels, PixelSampler sampler)
            {
                FileName = fileName;
                Size = size;
                ClampVertical = clampVertical;
                DitherStrength = ditherStrength;
                QuantLevels = quantLevels;
                Sampler = sampler;
            }
        }

        private readonly struct MaterialSpec
        {
            public readonly string AssetName;
            public readonly string TextureFile;
            public readonly float BreathSpeed;
            public readonly float BreathAmp;
            public readonly float Wetness;
            public readonly Color EmissionColor;
            public readonly Action<TeniaMaterialSet, Material> AssignField;

            public MaterialSpec(
                string assetName,
                string textureFile,
                float breathSpeed,
                float breathAmp,
                float wetness,
                Color emissionColor,
                Action<TeniaMaterialSet, Material> assignField)
            {
                AssetName = assetName;
                TextureFile = textureFile;
                BreathSpeed = breathSpeed;
                BreathAmp = breathAmp;
                Wetness = wetness;
                EmissionColor = emissionColor;
                AssignField = assignField;
            }
        }

        // Classic 4x4 ordered Bayer matrix normalized to [0, 1) for authentic PS1 15-bit dithering.
        private static readonly float[] Bayer4x4 =
        {
             0f / 16f,  8f / 16f,  2f / 16f, 10f / 16f,
            12f / 16f,  4f / 16f, 14f / 16f,  6f / 16f,
             3f / 16f, 11f / 16f,  1f / 16f,  9f / 16f,
            15f / 16f,  7f / 16f, 13f / 16f,  5f / 16f
        };

        private static readonly TextureSpec[] TextureSpecs =
        {
            new TextureSpec("tex_tenia_pale_skin.png",   128, false, 0.036f, 28, SamplePaleSkin),
            new TextureSpec("tex_tenia_acid_organ.png",  128, false, 0.038f, 28, SampleAcidOrgan),
            new TextureSpec("tex_tenia_scolex_maw.png",   64, false, 0.034f, 28, SampleScolexMaw),
            new TextureSpec("tex_tenia_teeth.png",        64, false, 0.032f, 28, SampleTeeth),
            new TextureSpec("tex_tenia_vestments.png",   128, true,  0.026f, 32, SampleVestments),
            new TextureSpec("tex_tenia_rusted_iron.png",  64, false, 0.032f, 28, SampleRustedIron)
        };

        private static readonly MaterialSpec[] MaterialSpecs =
        {
            new MaterialSpec(
                "Mat_Tenia_PaleSkin",
                "tex_tenia_pale_skin.png",
                1.6f, 0.025f, 0.55f,
                new Color(0f, 0f, 0f, 1f),
                (set, mat) => set.PaleSkin = mat),
            new MaterialSpec(
                "Mat_Tenia_AcidOrgan",
                "tex_tenia_acid_organ.png",
                2.8f, 0.04f, 0.75f,
                new Color(0.35f, 1.9f, 0.2f, 1.0f),
                (set, mat) => set.AcidOrgan = mat),
            new MaterialSpec(
                "Mat_Tenia_ScolexMaw",
                "tex_tenia_scolex_maw.png",
                2.0f, 0.015f, 0.8f,
                new Color(0f, 0f, 0f, 1f),
                (set, mat) => set.ScolexMaw = mat),
            new MaterialSpec(
                "Mat_Tenia_Teeth",
                "tex_tenia_teeth.png",
                0f, 0f, 0.4f,
                new Color(0f, 0f, 0f, 1f),
                (set, mat) => set.Teeth = mat),
            new MaterialSpec(
                "Mat_Tenia_Vestments",
                "tex_tenia_vestments.png",
                0.8f, 0.01f, 0.1f,
                new Color(0f, 0f, 0f, 1f),
                (set, mat) => set.Vestments = mat),
            new MaterialSpec(
                "Mat_Tenia_RustedIron",
                "tex_tenia_rusted_iron.png",
                0f, 0f, 0.2f,
                new Color(0f, 0f, 0f, 1f),
                (set, mat) => set.RustedIron = mat)
        };

        /// <summary>
        /// Creates or updates all Boss Tenia PS1 textures and materials, returning the populated material set.
        /// </summary>
        public static TeniaMaterialSet CreateOrUpdateTexturesAndMaterials()
        {
            Directory.CreateDirectory(TexturesFolder);
            Directory.CreateDirectory(MaterialsFolder);

            var set = new TeniaMaterialSet();

#if UNITY_EDITOR
            for (int i = 0; i < TextureSpecs.Length; i++)
            {
                TextureSpec spec = TextureSpecs[i];
                string texPath = $"{TexturesFolder}/{spec.FileName}";
                GenerateAndSavePng(texPath, spec);
            }

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            for (int i = 0; i < TextureSpecs.Length; i++)
            {
                TextureSpec spec = TextureSpecs[i];
                ConfigureTextureImporter($"{TexturesFolder}/{spec.FileName}", spec.ClampVertical);
            }

            Shader targetShader = Shader.Find(PrimaryShaderName);
            bool usingPrimaryShader = targetShader != null;
            if (!usingPrimaryShader)
            {
                targetShader = Shader.Find(FallbackShaderName) ?? Shader.Find("Standard");
            }

            for (int i = 0; i < MaterialSpecs.Length; i++)
            {
                MaterialSpec matSpec = MaterialSpecs[i];
                Material mat = CreateOrUpdateMaterial(matSpec, targetShader, usingPrimaryShader);
                matSpec.AssignField(set, mat);
            }

            AssetDatabase.SaveAssets();
#endif
            return set;
        }

#if UNITY_EDITOR
        [MenuItem("BossTenia/Generate Textures and Materials", priority = 10)]
        private static void MenuCreateOrUpdate()
        {
            CreateOrUpdateTexturesAndMaterials();
            Debug.Log("[TeniaTextureGenerator] Generated Boss Tenia PS1 textures and materials.");
        }

        private static void GenerateAndSavePng(string assetPath, TextureSpec spec)
        {
            int size = spec.Size;
            Color32[] pixels = new Color32[size * size];

            for (int y = 0; y < size; y++)
            {
                int rowOffset = y * size;
                for (int x = 0; x < size; x++)
                {
                    Color sample = spec.Sampler(x, y, size, size);
                    Color dithered = ApplyPS1Dither(sample, x, y, spec.DitherStrength, spec.QuantLevels);
                    pixels[rowOffset + x] = dithered;
                }
            }

            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            try
            {
                tex.SetPixels32(pixels);
                tex.Apply(false, false);
                byte[] pngBytes = tex.EncodeToPNG();
                File.WriteAllBytes(assetPath, pngBytes);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(tex);
            }
        }

        private static void ConfigureTextureImporter(string assetPath, bool clampVertical)
        {
            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null)
            {
                return;
            }

            bool dirty = false;

            if (importer.filterMode != FilterMode.Point)
            {
                importer.filterMode = FilterMode.Point;
                dirty = true;
            }

            if (importer.textureCompression != TextureImporterCompression.Uncompressed)
            {
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                dirty = true;
            }

            if (importer.mipmapEnabled)
            {
                importer.mipmapEnabled = false;
                dirty = true;
            }

            TextureWrapMode targetV = clampVertical ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
            if (importer.wrapModeU != TextureWrapMode.Repeat)
            {
                importer.wrapModeU = TextureWrapMode.Repeat;
                dirty = true;
            }

            if (importer.wrapModeV != targetV)
            {
                importer.wrapModeV = targetV;
                dirty = true;
            }

            if (importer.wrapModeW != TextureWrapMode.Repeat)
            {
                importer.wrapModeW = TextureWrapMode.Repeat;
                dirty = true;
            }

            if (dirty)
            {
                importer.SaveAndReimport();
            }
        }

        private static Material CreateOrUpdateMaterial(MaterialSpec spec, Shader shader, bool usingPrimaryShader)
        {
            string matPath = $"{MaterialsFolder}/{spec.AssetName}.mat";
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);

            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, matPath);
            }
            else if (shader != null && mat.shader != shader)
            {
                mat.shader = shader;
            }

            Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexturesFolder}/{spec.TextureFile}");
            if (tex != null && mat.HasProperty("_BaseMap"))
            {
                mat.SetTexture("_BaseMap", tex);
            }

            if (mat.HasProperty("_BaseColor"))
            {
                mat.SetColor("_BaseColor", Color.white);
            }

            if (mat.HasProperty("_UseVertexColor"))
            {
                mat.SetFloat("_UseVertexColor", 0f);
            }

            if (mat.HasProperty("_VertexSnap"))
            {
                mat.SetFloat("_VertexSnap", 180f);
            }

            if (mat.HasProperty("_AmbientFloor"))
            {
                mat.SetFloat("_AmbientFloor", 0.28f);
            }

            if (mat.HasProperty("_BreathSpeed"))
            {
                mat.SetFloat("_BreathSpeed", spec.BreathSpeed);
            }

            if (mat.HasProperty("_BreathAmp"))
            {
                mat.SetFloat("_BreathAmp", spec.BreathAmp);
            }

            if (mat.HasProperty("_Wetness"))
            {
                mat.SetFloat("_Wetness", spec.Wetness);
            }

            if (mat.HasProperty("_EnableBG3Cutout"))
            {
                mat.SetFloat("_EnableBG3Cutout", 0f);
            }

            if (!usingPrimaryShader && mat.HasProperty("_Smoothness"))
            {
                mat.SetFloat("_Smoothness", spec.Wetness);
            }

            if (mat.HasProperty("_EmissionColor"))
            {
                mat.SetColor("_EmissionColor", spec.EmissionColor);
                bool hasEmission = spec.EmissionColor.r > 0.001f || spec.EmissionColor.g > 0.001f || spec.EmissionColor.b > 0.001f;
                if (hasEmission)
                {
                    mat.EnableKeyword("_EMISSION");
                }
                else
                {
                    mat.DisableKeyword("_EMISSION");
                }
            }

            EditorUtility.SetDirty(mat);
            return mat;
        }
#endif

        // =====================================================================
        // Pixel Art Texture Samplers
        // =====================================================================

        private static Color SamplePaleSkin(int x, int y, int width, int height)
        {
            float u = (x + 0.5f) / width;
            float v = (y + 0.5f) / height;

            // Subtle vertical proglottid ring creases with organic wobble.
            float ringWobble = (TileableFbm(u, v, 4, 4, 3, 101) - 0.5f) * 0.032f;
            float ringPhase = Mathf.Repeat((u + ringWobble) * 4f, 1f);
            float creaseDist = Mathf.Min(ringPhase, 1f - ringPhase);
            float creaseMask = 1f - SmoothStep(0.0f, 0.055f, creaseDist);
            float lobeBulge = Mathf.Sin(ringPhase * Mathf.PI);

            float mottle = TileableFbm(u, v, 6, 6, 4, 113);
            float bruiseNoise = TileableFbm(u + 0.31f, v + 0.17f, 3, 3, 3, 127);
            float bruiseMask = SmoothStep(0.48f, 0.78f, bruiseNoise) * (0.45f + 0.55f * (1f - lobeBulge));

            Color paleHighlight = Hex(0xE2, 0xDA, 0xC0);
            Color bruisedParchment = Hex(0xB8, 0xAA, 0x88);
            Color greenBruise = Hex(0x8B, 0x98, 0x65);
            Color creaseShadow = Hex(0x6D, 0x5C, 0x47);

            float skinMix = Mathf.Clamp01(0.24f + lobeBulge * 0.52f + (mottle - 0.5f) * 0.46f);
            Color col = Color.Lerp(bruisedParchment, paleHighlight, skinMix);
            col = Color.Lerp(col, greenBruise, bruiseMask * 0.46f);

            // Subtle pale rim just outside each ring crease, then deep crease shadow.
            float rimMask = SmoothStep(0.05f, 0.09f, creaseDist) * (1f - SmoothStep(0.09f, 0.17f, creaseDist));
            col = Color.Lerp(col, MultiplyRgb(paleHighlight, 1.03f), rimMask * 0.25f);
            col = Color.Lerp(col, creaseShadow, creaseMask * 0.68f);

            // Branching dark crimson/necrotic veins via domain-warped cellular ridges.
            float wu = u + (TileableFbm(u, v, 4, 4, 3, 139) - 0.5f) * 0.12f;
            float wv = v + (TileableFbm(u + 0.5f, v + 0.2f, 4, 4, 3, 149) - 0.5f) * 0.12f;
            TileableCellular(wu, wv, 4, 4, 157, out float edgePrimary);
            TileableCellular(wu + 0.19f, wv + 0.41f, 8, 8, 163, out float edgeSecondary);

            float primaryVein = 1f - SmoothStep(0.014f, 0.064f, edgePrimary);
            float secondaryVein = (1f - SmoothStep(0.01f, 0.046f, edgeSecondary)) * 0.58f;
            float veinMask = Mathf.Clamp01(Mathf.Max(primaryVein, secondaryVein) * (0.55f + 0.45f * bruiseNoise));

            Color veinColor = Color.Lerp(Hex(0x6E, 0x1F, 0x22), Hex(0x3D, 0x11, 0x16), primaryVein);
            col = Color.Lerp(col, veinColor, veinMask * 0.86f);

            // Fine pore grain.
            float pore = (Hash01(x, y, 173) - 0.5f) * 0.045f;
            return OffsetBrightness(col, pore);
        }

        private static Color SampleAcidOrgan(int x, int y, int width, int height)
        {
            float u = (x + 0.5f) / width;
            float v = (y + 0.5f) / height;

            float wu = u + (TileableFbm(u, v, 4, 4, 3, 211) - 0.5f) * 0.10f;
            float wv = v + (TileableFbm(u + 0.37f, v + 0.61f, 4, 4, 3, 223) - 0.5f) * 0.10f;

            float f1Large = TileableCellular(wu, wv, 4, 4, 229, out float sacEdgeLarge);
            float f1Small = TileableCellular(wu + 0.25f, wv + 0.15f, 7, 7, 233, out _);

            float sacGlowLarge = SmoothStep(0.55f, 0.10f, f1Large);
            float sacGlowSmall = SmoothStep(0.45f, 0.08f, f1Small) * 0.72f;
            float sacGlow = Mathf.Clamp01(Mathf.Max(sacGlowLarge, sacGlowSmall));

            float memNoise = TileableFbm(u, v, 5, 5, 3, 241);

            Color deepBile = Hex(0x0D, 0x1C, 0x08);
            Color sickMembrane = Hex(0x2B, 0x52, 0x1A);
            Color acidNeon = Hex(0x4C, 0xFF, 0x2E);
            Color acidCore = Hex(0xA8, 0xFF, 0x50);
            Color bubbleGlint = Hex(0xD4, 0xFF, 0x98);

            Color col = Color.Lerp(deepBile, sickMembrane, SmoothStep(0.24f, 0.78f, memNoise));
            col = Color.Lerp(col, acidNeon, SmoothStep(0.18f, 0.62f, sacGlow));
            col = Color.Lerp(col, acidCore, SmoothStep(0.60f, 0.92f, sacGlow));

            if (sacGlow > 0.55f && Hash01(x, y, 251) > 0.972f)
            {
                col = bubbleGlint;
            }

            // Deep bile troughs framing the sac clusters.
            float trough = 1f - SmoothStep(0.0f, 0.10f, sacEdgeLarge);
            col = Color.Lerp(col, deepBile, trough * 0.58f);

            // Dark branching veins running across the bioluminescent sacs.
            float vu = u + (TileableFbm(u, v, 5, 5, 3, 263) - 0.5f) * 0.14f;
            float vv = v + (TileableFbm(u + 0.43f, v + 0.19f, 5, 5, 3, 269) - 0.5f) * 0.14f;
            TileableCellular(vu, vv, 5, 5, 277, out float veinEdge1);
            TileableCellular(vu + 0.17f, vv + 0.53f, 9, 9, 281, out float veinEdge2);

            float vein1 = 1f - SmoothStep(0.015f, 0.068f, veinEdge1);
            float vein2 = (1f - SmoothStep(0.01f, 0.048f, veinEdge2)) * 0.65f;
            float veinMask = Mathf.Max(vein1, vein2);

            Color darkVein = Color.Lerp(Hex(0x14, 0x0A, 0x0A), Hex(0x2D, 0x11, 0x0F), sacGlow);
            return Color.Lerp(col, darkVein, veinMask * 0.90f);
        }

        private static Color SampleScolexMaw(int x, int y, int width, int height)
        {
            float u = (x + 0.5f) / width;
            float v = (y + 0.5f) / height;

            float warp = (TileableFbm(u, v, 4, 4, 3, 307) - 0.5f) * 0.08f;
            float fold = 0.5f + 0.5f * Mathf.Sin((u + warp) * 6f * Mathf.PI * 2f);
            float microFolds = TileableFbm(u, v, 6, 8, 3, 311);
            float f1 = TileableCellular(u + warp, v, 5, 5, 317, out _);
            float lump = SmoothStep(0.52f, 0.06f, f1);

            // Subtle throat depth darkening toward bottom/inner gullet folds.
            float throatDepth = SmoothStep(0.58f, 0.0f, v);

            Color throatDark = Hex(0x26, 0x04, 0x07);
            Color deepCrimson = Hex(0x6E, 0x10, 0x15);
            Color rawCrimson = Hex(0xA8, 0x1E, 0x24);
            Color wetPink = Hex(0xDB, 0x5B, 0x60);
            Color wetGlint = Hex(0xFF, 0xCC, 0xCE);

            float ridgeVal = Mathf.Clamp01(fold * 0.56f + microFolds * 0.26f + lump * 0.28f - throatDepth * 0.34f);
            Color col = Color.Lerp(deepCrimson, rawCrimson, SmoothStep(0.28f, 0.78f, ridgeVal));
            col = Color.Lerp(col, throatDark, Mathf.Clamp01(throatDepth * 0.56f + (1f - fold) * 0.36f));

            float wetRim = SmoothStep(0.66f, 0.88f, ridgeVal);
            col = Color.Lerp(col, wetPink, wetRim * 0.62f);

            if (ridgeVal > 0.77f && Hash01(x, y, 331) > 0.70f)
            {
                col = wetGlint;
            }

            return col;
        }

        private static Color SampleTeeth(int x, int y, int width, int height)
        {
            float u = (x + 0.5f) / width;
            float v = (y + 0.5f) / height;

            float grain = TileableFbm(u, v * 0.35f, 10, 2, 3, 401);
            float crackNoise = TileableFbm(u * 1.4f, v * 0.5f, 8, 4, 2, 409);

            // Jagged organic gum-line near base (v near 0).
            float gumLine = 0.18f + (TileableFbm(u, 0.5f, 6, 1, 2, 419) - 0.5f) * 0.10f;
            float gumMask = 1f - SmoothStep(gumLine - 0.05f, gumLine + 0.06f, v);
            float stainFade = SmoothStep(gumLine - 0.02f, gumLine + 0.28f, v);

            Color darkGum = Hex(0x36, 0x13, 0x12);
            Color tartarBrown = Hex(0x6F, 0x4D, 0x2E);
            Color dirtyBone = Hex(0xD8, 0xCF, 0xAF);
            Color agedEnamel = Hex(0xAE, 0x9C, 0x6F);
            Color tipIvory = Hex(0xEC, 0xE5, 0xCE);

            Color enamel = Color.Lerp(agedEnamel, dirtyBone, SmoothStep(0.24f, 0.76f, grain));
            enamel = Color.Lerp(enamel, tipIvory, SmoothStep(0.68f, 0.98f, v) * 0.52f);

            if (v > gumLine && Mathf.Abs(crackNoise - 0.5f) < 0.024f)
            {
                enamel = Color.Lerp(enamel, Hex(0x6D, 0x5B, 0x42), 0.65f);
            }

            Color col = Color.Lerp(tartarBrown, enamel, stainFade);
            col = Color.Lerp(col, darkGum, gumMask * 0.92f);

            if (v < gumLine + 0.14f && Hash01(x, y, 431) > 0.87f)
            {
                col = Color.Lerp(col, Hex(0x5C, 0x14, 0x16), 0.72f);
            }

            return col;
        }

        private static Color SampleVestments(int x, int y, int width, int height)
        {
            float u = (x + 0.5f) / width;
            float v = (y + 0.5f) / height;

            // Seamless horizontal ragged tear edge at the bottom hem (clamped vertically).
            float tearNoise = TileableFbm(u, 0.25f, 8, 1, 3, 503);
            float sharpNotch = Mathf.Abs(Mathf.Sin(u * Mathf.PI * 6f + tearNoise * 2.4f)) * 0.06f;
            float tearV = 0.06f + tearNoise * 0.10f + sharpNotch;

            // Two deeper vertical cloth rips biting upward into the hem.
            float ripLeft = SmoothStep(0.035f, 0.0f, Mathf.Abs(u - 0.22f)) * 0.075f;
            float ripRight = SmoothStep(0.035f, 0.0f, Mathf.Abs(u - 0.78f)) * 0.075f;
            tearV += ripLeft + ripRight;

            if (v < tearV)
            {
                // Stray frayed threads immediately below the tear silhouette.
                if (tearV - v < 0.022f && Hash01(x, y, 509) > 0.56f)
                {
                    return Hash01(x, y, 513) > 0.82f ? Hex(0x82, 0x62, 0x20) : Hex(0x3D, 0x07, 0x0A);
                }

                // Deep void shadow with alpha 0 for optional cutout compatibility.
                return new Color(0.04f, 0.01f, 0.015f, 0f);
            }

            // Small moth-eaten / battle-torn holes inside the fabric.
            float holeField = TileableFbm(u + 0.11f, v + 0.43f, 9, 9, 3, 517);
            if (v > 0.22f && v < 0.82f && holeField > 0.79f)
            {
                return new Color(0.05f, 0.012f, 0.018f, 0f);
            }

            // Dark crimson liturgical cloth with drapery folds and diagonal twill weave.
            float foldWave = 0.5f + 0.5f * Mathf.Sin(u * Mathf.PI * 8f + (TileableFbm(u, v, 3, 2, 2, 521) - 0.5f) * 1.2f);
            Color clothLight = Hex(0x68, 0x0D, 0x14);
            Color clothDark = Hex(0x42, 0x07, 0x0B);

            Color col = Color.Lerp(clothDark, clothLight, 0.24f + foldWave * 0.66f);
            bool twill = ((x + y) & 3) == 0 || ((x - y) & 7) == 0;
            col = MultiplyRgb(col, twill ? 1.07f : 0.93f);

            // Ornate faded gold gothic cross & trim embroidery.
            int goldType = EvaluateVestmentsGoldMask(x, y); // 0 = none, 1 = shadow outline, 2 = gold body
            if (goldType > 0)
            {
                float wear = TileableFbm(u, v, 8, 8, 3, 541);
                float threadVisibility = SmoothStep(0.28f, 0.55f, wear);
                if (threadVisibility > 0.05f)
                {
                    Color goldBase = Hex(0xC8, 0x9F, 0x38);
                    Color goldShadow = Hex(0x78, 0x58, 0x1A);
                    Color goldHighlight = Hex(0xE6, 0xC4, 0x62);

                    Color stitchColor = goldType == 1
                        ? goldShadow
                        : (((x ^ y) & 1) == 0 ? Color.Lerp(goldBase, goldHighlight, 0.45f) : Color.Lerp(goldBase, goldShadow, 0.25f));

                    col = Color.Lerp(col, stitchColor, threadVisibility * 0.92f);
                }
            }

            // Grime, bile, and dried blood splatters concentrated lower down.
            float grimeNoise = TileableFbm(u + 0.13f, v + 0.27f, 5, 5, 4, 557);
            float bloodBlotch = TileableFbm(u, v, 7, 7, 3, 569);
            float lowerWeight = SmoothStep(0.62f, 0.16f, v);

            float grimeMask = SmoothStep(0.48f, 0.78f, grimeNoise + lowerWeight * 0.22f);
            col = Color.Lerp(col, Hex(0x2D, 0x22, 0x0D), grimeMask * 0.58f);

            float bloodMask = SmoothStep(0.60f, 0.78f, bloodBlotch + lowerWeight * 0.14f);
            col = Color.Lerp(col, Hex(0x24, 0x03, 0x05), bloodMask * 0.80f);

            // Rotted scorch/blood soaking right along the torn bottom edge.
            float edgeRot = 1f - SmoothStep(0.0f, 0.045f, v - tearV);
            col = Color.Lerp(col, Hex(0x20, 0x03, 0x05), edgeRot * 0.82f);

            return col;
        }

        private static int EvaluateVestmentsGoldMask(int x, int y)
        {
            // Top collar border band + gothic diamond pips.
            if (y >= 117 && y <= 123 && y != 120)
            {
                return (y == 117 || y == 123) ? 1 : 2;
            }
            if (y >= 113 && y <= 116 && ((x + 2) & 7) <= 2 && (y - 113) <= 2 - Mathf.Abs(((x + 2) & 7) - 1))
            {
                return 2;
            }

            // Lower hem border band + repeating gothic chevron peaks.
            if (y >= 26 && y <= 32 && y != 29)
            {
                return (y == 26 || y == 32) ? 1 : 2;
            }
            if (y >= 33 && y <= 37)
            {
                int localX = (x & 7) - 4;
                if (Mathf.Abs(localX) <= 37 - y)
                {
                    return (Mathf.Abs(localX) == 37 - y) ? 1 : 2;
                }
            }

            // Vertical flanking trim stripes (frame the cross seamlessly on horizontal repeat).
            if ((x >= 15 && x <= 18) || (x >= 109 && x <= 112))
            {
                if (y >= 32 && y <= 117)
                {
                    if (x == 15 || x == 18 || x == 109 || x == 112) return 1;
                    return 2;
                }
            }

            // Centerpiece Ornate Gothic Cross at (64, 84).
            int dx = Mathf.Abs(x - 64);
            int dy = y - 84;
            int ady = Mathf.Abs(dy);

            int vHalfW = 3 + (y >= 100 ? (y - 99) / 2 : 0) + (y <= 50 ? (51 - y) / 2 : 0);
            int hHalfH = 3 + (dx >= 18 ? (dx - 17) / 2 : 0);

            bool inVerticalBeam = y >= 42 && y <= 108 && dx <= vHalfW;
            bool inHorizontalBeam = x >= 38 && x <= 90 && ady <= hHalfH;
            int manhattan = dx + ady;
            bool inCenterMedallion = manhattan <= 9 && manhattan >= 4;
            bool inCenterGem = manhattan <= 1;

            if (inVerticalBeam || inHorizontalBeam || inCenterMedallion || inCenterGem)
            {
                // Thin dark-gold embroidery shadow outline around the cross contour and medallion.
                bool vOutline = inVerticalBeam && (dx == vHalfW || y == 42 || y == 108) && !inHorizontalBeam;
                bool hOutline = inHorizontalBeam && (ady == hHalfH || x == 38 || x == 90) && !inVerticalBeam;
                bool mOutline = inCenterMedallion && (manhattan == 9 || manhattan == 4);
                return (vOutline || hOutline || mOutline) ? 1 : 2;
            }

            return 0;
        }

        private static Color SampleRustedIron(int x, int y, int width, int height)
        {
            float u = (x + 0.5f) / width;
            float v = (y + 0.5f) / height;

            float metalVar = TileableFbm(u, v, 4, 4, 3, 601);
            float pitNoise = TileableFbm(u + 0.21f, v + 0.41f, 10, 10, 3, 607);
            float rustField = TileableFbm(u + 0.63f, v + 0.13f, 3, 3, 4, 613);
            float rustCell = TileableCellular(u, v, 5, 5, 619, out _);

            Color ironDark = Hex(0x2B, 0x26, 0x24);
            Color ironLight = Hex(0x48, 0x43, 0x40);
            Color ironPit = Hex(0x16, 0x13, 0x12);
            Color rustDeep = Hex(0x52, 0x22, 0x10);
            Color rustMid = Hex(0x7A, 0x3B, 0x1E);
            Color rustBright = Hex(0x9E, 0x51, 0x28);
            Color scratchMetal = Hex(0x8D, 0x87, 0x82);
            Color scratchGroove = Hex(0x14, 0x11, 0x10);

            Color col = Color.Lerp(ironDark, ironLight, SmoothStep(0.32f, 0.78f, metalVar));

            if (pitNoise < 0.35f)
            {
                col = Color.Lerp(col, ironPit, SmoothStep(0.35f, 0.15f, pitNoise));
            }

            float rustMask = SmoothStep(0.42f, 0.74f, rustField * 0.74f + (1f - rustCell) * 0.34f);
            Color rustCol = Color.Lerp(rustDeep, rustMid, SmoothStep(0.24f, 0.66f, pitNoise));
            rustCol = Color.Lerp(rustCol, rustBright, SmoothStep(0.70f, 0.92f, rustField));
            col = Color.Lerp(col, rustCol, rustMask);

            // Deterministic wrapping metallic edge scratches cutting across the rusted iron.
            float scratchDist = EvaluateTileableScratches(x, y, width, height);
            if (scratchDist < 0.62f)
            {
                float gleam = 1f - SmoothStep(0.15f, 0.62f, scratchDist);
                col = Color.Lerp(col, scratchMetal, 0.55f + gleam * 0.45f);
            }
            else if (scratchDist < 1.35f)
            {
                float groove = 1f - SmoothStep(0.62f, 1.35f, scratchDist);
                col = Color.Lerp(col, scratchGroove, groove * 0.45f);
            }

            return col;
        }

        private static float EvaluateTileableScratches(int x, int y, int w, int h)
        {
            // Fixed deterministic scratch segments in [0, 64) coordinates, wrapped seamlessly across tiles.
            float minDist = 999f;
            for (int i = 0; i < 9; i++)
            {
                float sx = Hash01(i, 11, 641) * w;
                float sy = Hash01(i, 23, 641) * h;
                float angle = (Hash01(i, 37, 641) - 0.5f) * Mathf.PI * 1.4f;
                float len = 9f + Hash01(i, 53, 641) * 16f;
                float ex = sx + Mathf.Cos(angle) * len;
                float ey = sy + Mathf.Sin(angle) * len;

                float d = WrappedDistanceToSegment(x, y, sx, sy, ex, ey, w, h);
                if (d < minDist)
                {
                    minDist = d;
                }
            }
            return minDist;
        }

        private static float WrappedDistanceToSegment(float px, float py, float ax, float ay, float bx, float by, int w, int h)
        {
            float abx = bx - ax;
            float aby = by - ay;
            float abLenSq = abx * abx + aby * aby;
            if (abLenSq < 0.0001f)
            {
                return 999f;
            }

            float best = 999f;
            for (int oy = -1; oy <= 1; oy++)
            {
                for (int ox = -1; ox <= 1; ox++)
                {
                    float qx = px + ox * w;
                    float qy = py + oy * h;
                    float t = Mathf.Clamp01(((qx - ax) * abx + (qy - ay) * aby) / abLenSq);
                    float cx = ax + t * abx;
                    float cy = ay + t * aby;
                    float dx = qx - cx;
                    float dy = qy - cy;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    if (dist < best)
                    {
                        best = dist;
                    }
                }
            }
            return best;
        }

        // =====================================================================
        // Deterministic Tileable Noise & PS1 Dithering Utilities
        // =====================================================================

        private static float TileableFbm(float u, float v, int basePeriodX, int basePeriodY, int octaves, int seed)
        {
            float sum = 0f;
            float amp = 0.5f;
            float norm = 0f;
            int px = Mathf.Max(1, basePeriodX);
            int py = Mathf.Max(1, basePeriodY);

            for (int o = 0; o < octaves; o++)
            {
                sum += TileableValueNoise(u, v, px, py, seed + o * 31) * amp;
                norm += amp;
                amp *= 0.5f;
                px *= 2;
                py *= 2;
            }

            return norm > 0f ? sum / norm : 0f;
        }

        private static float TileableValueNoise(float u, float v, int periodX, int periodY, int seed)
        {
            float gx = Wrap01(u) * periodX;
            float gy = Wrap01(v) * periodY;

            int ix0 = Mathf.FloorToInt(gx);
            int iy0 = Mathf.FloorToInt(gy);
            float tx = gx - ix0;
            float ty = gy - iy0;

            float sx = tx * tx * (3f - 2f * tx);
            float sy = ty * ty * (3f - 2f * ty);

            int x0 = PositiveMod(ix0, periodX);
            int y0 = PositiveMod(iy0, periodY);
            int x1 = PositiveMod(x0 + 1, periodX);
            int y1 = PositiveMod(y0 + 1, periodY);

            float n00 = Hash01(x0, y0, seed);
            float n10 = Hash01(x1, y0, seed);
            float n01 = Hash01(x0, y1, seed);
            float n11 = Hash01(x1, y1, seed);

            float nx0 = Mathf.Lerp(n00, n10, sx);
            float nx1 = Mathf.Lerp(n01, n11, sx);
            return Mathf.Lerp(nx0, nx1, sy);
        }

        private static float TileableCellular(float u, float v, int cellsX, int cellsY, int seed, out float f2MinusF1)
        {
            float gx = Wrap01(u) * cellsX;
            float gy = Wrap01(v) * cellsY;

            int ix = Mathf.FloorToInt(gx);
            int iy = Mathf.FloorToInt(gy);
            float fx = gx - ix;
            float fy = gy - iy;

            float f1 = 99f;
            float f2 = 99f;

            for (int dy = -1; dy <= 1; dy++)
            {
                int cy = PositiveMod(iy + dy, cellsY);
                for (int dx = -1; dx <= 1; dx++)
                {
                    int cx = PositiveMod(ix + dx, cellsX);
                    float px = 0.14f + 0.72f * Hash01(cx, cy, seed);
                    float py = 0.14f + 0.72f * Hash01(cx, cy, seed + 17);

                    float ox = dx + px - fx;
                    float oy = dy + py - fy;
                    float dist = Mathf.Sqrt(ox * ox + oy * oy);

                    if (dist < f1)
                    {
                        f2 = f1;
                        f1 = dist;
                    }
                    else if (dist < f2)
                    {
                        f2 = dist;
                    }
                }
            }

            f2MinusF1 = f2 - f1;
            return f1;
        }

        private static Color ApplyPS1Dither(Color c, int x, int y, float ditherStrength, int quantLevels)
        {
            float bayer = Bayer4x4[(y & 3) * 4 + (x & 3)] - 0.5f;
            float offset = bayer * ditherStrength;
            float step = 1f / Mathf.Max(2, quantLevels - 1);

            float r = Mathf.Clamp01(Mathf.Round((Mathf.Clamp01(c.r) + offset) / step) * step);
            float g = Mathf.Clamp01(Mathf.Round((Mathf.Clamp01(c.g) + offset) / step) * step);
            float b = Mathf.Clamp01(Mathf.Round((Mathf.Clamp01(c.b) + offset) / step) * step);
            return new Color(r, g, b, Mathf.Clamp01(c.a));
        }

        private static float Hash01(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)x * 374761393u + (uint)y * 668265263u + (uint)seed * 1442695041u;
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0x00FFFFFFu) / 16777216f;
            }
        }

        private static float SmoothStep(float edge0, float edge1, float x)
        {
            if (Mathf.Approximately(edge0, edge1))
            {
                return x >= edge1 ? 1f : 0f;
            }

            float t = Mathf.Clamp01((x - edge0) / (edge1 - edge0));
            return t * t * (3f - 2f * t);
        }

        private static float Wrap01(float value)
        {
            float r = value - Mathf.Floor(value);
            return r < 0f ? r + 1f : r;
        }

        private static int PositiveMod(int value, int modulus)
        {
            int r = value % modulus;
            return r < 0 ? r + modulus : r;
        }

        private static Color Hex(byte r, byte g, byte b)
        {
            return new Color(r / 255f, g / 255f, b / 255f, 1f);
        }

        private static Color OffsetBrightness(Color c, float delta)
        {
            return new Color(
                Mathf.Clamp01(c.r + delta),
                Mathf.Clamp01(c.g + delta),
                Mathf.Clamp01(c.b + delta),
                c.a);
        }

        private static Color MultiplyRgb(Color c, float factor)
        {
            return new Color(
                Mathf.Clamp01(c.r * factor),
                Mathf.Clamp01(c.g * factor),
                Mathf.Clamp01(c.b * factor),
                c.a);
        }
    }
}
