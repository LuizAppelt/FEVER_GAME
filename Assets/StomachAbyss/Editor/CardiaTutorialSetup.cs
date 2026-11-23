#pragma warning disable 0618
using System;
using System.Collections.Generic;
using System.IO;
using ImmuneEnemies;
using StomachAbyss.Atmosphere;
using StomachAbyss.Tutorial;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace MapaEstomago.Editor
{
    /// <summary>
    /// Constroi a Camara da Cardia em escala completa centrada em Origin = (-280, 10, 0),
    /// reutilizando os modelos .glb esculpidos, materiais PS1, atmosfera e inimigos de Stomach_Abyss,
    /// conecta a saida ao Osseario Sul e posiciona as duas aparicoes de Virgil.
    /// </summary>
    public static class CardiaTutorialSetup
    {
        private const string RootName = "00_Cardia_Tutorial_Chamber";
        private const string ConnectionsGroupName = "06_Ossuary_And_Viaduct_Connections";

        private const string ModelsFolder = "Assets/StomachAbyss/Models";
        private const string AbyssMaterialsFolder = "Assets/StomachAbyss/Materials";
        private const string AtmosphereFolder = "Assets/StomachAbyss/Atmosphere";
        private const string PilotMaterialsFolder = "Assets/PilotChamber/Materials";
        private const string PilotTexturesFolder = "Assets/PilotChamber/Textures";
        private const string TutorialMeshesFolder = "Assets/StomachAbyss/TutorialMeshes";

        private const string ActiveAcidLakeMatPath = AtmosphereFolder + "/Mat_Abyss_AcidLake_Active.mat";
        private const string VaporMatPath = AtmosphereFolder + "/Mat_Abyss_AcidVapor_PS1.mat";
        private const string DropletMatPath = AtmosphereFolder + "/Mat_Abyss_AcidDroplet_PS1.mat";

        private const string DoorPrefabPath = "Assets/PilotChamber/Flesh_Eye_Door.prefab";
        private const string VirusPrefabPath = "Assets/PilotChamber/Virus_Player.prefab";
        private const string MacrophagePrefabPath = "Assets/ImmuneEnemies/Prefabs/Enemy_AbadeMacrofago.prefab";
        private const string EosinophilPrefabPath = "Assets/ImmuneEnemies/Prefabs/Enemy_EosinofiloBilis.prefab";
        private const string VirgilMatPath = AbyssMaterialsFolder + "/Mat_VirgilNpc.mat";

        private static readonly Vector3 Origin = new Vector3(-280f, 10f, 0f);
        private static readonly Vector3 PlayerSpawnPos = Origin + new Vector3(0f, 2.85f, -49.0f);
        private static readonly Vector3 OssuaryCheckpointPos = new Vector3(-0.9f, 2.5f, -49.8f);

        private static readonly HashSet<string> ColliderSkipNodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Abyss_Acid_Lake",
            "Waterfall_Acid_Sheet",
            "Waterfall_BloodAcid_Foam",
            "Waterfall_Splash_Ring",
            "VeinBridge_HangingTendons",
            "BoneWing_Tendon_Webs",
            "Colossus_Lashing_Cables",
            "Colossus_Acid_Glow_And_Orbs",
            "Portal_Vortex_Core",
            "Portal_AcidRunes",
            "Skull_Eye_And_Maw_Glow",
            "Ceiling_Visceral_WebBeams",
            "Ceiling_Backlit_Alveoli"
        };

        private sealed class TutorialMaterials
        {
            public Dictionary<string, Material> AbyssByKey;
            public Material ActiveAcidLake;
            public Material Cartilage;
            public Material DigestedCalcified;
            public Material OssifiedStone;
            public Material FleshWall;
            public Material Teeth;
            public Material VeinTendon;
            public Material HornedBone;
            public Material AcidGlow;
            public Material Pustule;
            public Material PixelAcid;
            public Material PixelBlood;
            public Material AcidVapor;
            public Material AcidDroplet;
            public Material VirgilNpc;
        }

        private sealed class TutorialMeshes
        {
            public Mesh SphincterTorus;
            public Mesh ToothFang;
            public Mesh TendonStrand;
            public Mesh CystCluster;
        }

        [MenuItem("Estomago/Novo Mapa/Construir Camara da Cardia (Tutorial)", priority = 21)]
        public static void BuildCardiaTutorialInScene()
        {
            Scene activeScene = EditorSceneManager.GetActiveScene();
            if (!activeScene.IsValid())
            {
                Debug.LogError("[CardiaTutorialSetup] Nenhuma cena ativa valida.");
                return;
            }

            EnsureFolderHierarchy(TutorialMeshesFolder);
            EnsureFolderHierarchy(AbyssMaterialsFolder);

            CleanupExistingObjects(activeScene);

            TutorialMaterials mats = LoadTutorialMaterials();
            TutorialMeshes meshes = CreateAndSaveTutorialMeshes();
            AssetDatabase.SaveAssets();

            GameObject doorPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(DoorPrefabPath);
            GameObject virusPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(VirusPrefabPath);
            GameObject macrophagePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(MacrophagePrefabPath);
            GameObject eosinophilPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(EosinophilPrefabPath);

            GameObject root = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(root, "Build Cardia Tutorial Chamber");
            root.transform.position = Vector3.zero;

            // 1. Caverna Visceral Completa, Lago de Acido, Cachoeiras, Monumentos e Ilhotas
            var waterfallRenderers = new List<Renderer>();
            MeshRenderer lakeRenderer = BuildCavernAndAcidBasin(root.transform, mats, waterfallRenderers);

            // 2. 4 Arenas Esculpidas + 5 Pontes + Inimigos + Selos + Barreira + Porta de Saida
            FleshEyeDoorController cardiaExitDoor = BuildArenasAndBridges(
                root.transform,
                mats,
                meshes,
                doorPrefab,
                virusPrefab,
                macrophagePrefab,
                eosinophilPrefab);

            // 3. Atmosfera VFX, Vapores Toxicos, Luzes Causticas e Pulsacao de Membranas
            BuildAtmosphereAndLights(root.transform, mats, lakeRenderer, waterfallRenderers);

            // 4. Conexoes com o Osseario Sul + Segunda Aparicao de Virgil no Viaduto
            BuildOssuaryAndViaductConnections(root.transform, mats, doorPrefab, virusPrefab, cardiaExitDoor);

            // 5. Jogador e Camera Isometrica
            ConfigurePlayerAndCamera();

            EditorUtility.SetDirty(root);
            EditorSceneManager.MarkSceneDirty(activeScene);
            Selection.activeGameObject = root;

            Debug.Log($"[CardiaTutorialSetup] Camara da Cardia em escala completa construida com sucesso em {Origin}.");
        }

        // ------------------------------------------------------------------
        // LIMPEZA IDEMPOTENTE
        // ------------------------------------------------------------------
        private static void CleanupExistingObjects(Scene scene)
        {
            var toDestroy = new List<GameObject>();
            var namesToClean = new HashSet<string>(StringComparer.Ordinal)
            {
                RootName,
                "Flesh_Eye_Door_OssuaryArrival",
                "Cardia_Ossuary_Arrival_Checkpoint",
                "Virgil_NPC_Viaduct"
            };

            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                if (roots[i] == null) continue;
                if (namesToClean.Contains(roots[i].name))
                {
                    toDestroy.Add(roots[i]);
                    continue;
                }

                Transform[] all = roots[i].GetComponentsInChildren<Transform>(true);
                for (int j = 0; j < all.Length; j++)
                {
                    if (all[j] != null && namesToClean.Contains(all[j].name))
                    {
                        toDestroy.Add(all[j].gameObject);
                    }
                }
            }

            for (int i = 0; i < toDestroy.Count; i++)
            {
                if (toDestroy[i] != null)
                {
                    Undo.DestroyObjectImmediate(toDestroy[i]);
                }
            }
        }

        // ------------------------------------------------------------------
        // CARREGAMENTO DE MATERIAIS & VIRGIL
        // ------------------------------------------------------------------
        private static TutorialMaterials LoadTutorialMaterials()
        {
            Shader litFallback = Shader.Find("Universal Render Pipeline/Lit");
            Material fallback = new Material(litFallback != null ? litFallback : Shader.Find("Standard"))
            {
                color = new Color(0.35f, 0.14f, 0.14f, 1f)
            };

            var abyssDict = new Dictionary<string, Material>(StringComparer.Ordinal)
            {
                ["Mat_FleshFloor"] = LoadMat(AbyssMaterialsFolder + "/Mat_Abyss_FleshFloor.mat", fallback),
                ["Mat_FleshWall"] = LoadMat(AbyssMaterialsFolder + "/Mat_Abyss_FleshWall.mat", fallback),
                ["Mat_DigestedCalcified"] = LoadMat(AbyssMaterialsFolder + "/Mat_Abyss_DigestedCalcified.mat", fallback),
                ["Mat_Cartilage"] = LoadMat(AbyssMaterialsFolder + "/Mat_Abyss_Cartilage.mat", fallback),
                ["Mat_OssifiedStone"] = LoadMat(AbyssMaterialsFolder + "/Mat_Abyss_OssifiedStone.mat", fallback),
                ["Mat_Teeth"] = LoadMat(AbyssMaterialsFolder + "/Mat_Abyss_Teeth.mat", fallback),
                ["Mat_VeinTendon"] = LoadMat(AbyssMaterialsFolder + "/Mat_Abyss_VeinTendon.mat", fallback),
                ["Mat_HornedBone"] = LoadMat(AbyssMaterialsFolder + "/Mat_Abyss_HornedBone.mat", fallback),
                ["Mat_MembraneBacklit"] = LoadMat(AbyssMaterialsFolder + "/Mat_Abyss_MembraneBacklit.mat", fallback),
                ["Mat_PortalVortex"] = LoadMat(AbyssMaterialsFolder + "/Mat_Abyss_PortalVortex.mat", fallback),
                ["Mat_Abyss_AcidLake"] = LoadMat(AbyssMaterialsFolder + "/Mat_Abyss_AcidLake.mat", fallback),
                ["Mat_Abyss_AcidWaterfall"] = LoadMat(AbyssMaterialsFolder + "/Mat_Abyss_AcidWaterfall.mat", fallback),
                ["Mat_Abyss_AcidGlow"] = LoadMat(AbyssMaterialsFolder + "/Mat_Abyss_AcidGlow.mat", fallback),
                ["Mat_BloodWaves"] = LoadMat(AbyssMaterialsFolder + "/Mat_Abyss_BloodAcidFoam.mat", fallback)
            };

            Material activeAcidLake = AssetDatabase.LoadAssetAtPath<Material>(ActiveAcidLakeMatPath);
            if (activeAcidLake == null)
            {
                activeAcidLake = abyssDict["Mat_Abyss_AcidLake"];
            }
            abyssDict["Mat_Abyss_AcidLake"] = activeAcidLake;

            Material pixelAcid = LoadMat(PilotMaterialsFolder + "/Mat_PixelAcidParticle.mat", null);
            Material pixelBlood = LoadMat(PilotMaterialsFolder + "/Mat_PixelBloodParticle.mat", null);
            if (pixelBlood == null) pixelBlood = pixelAcid;

            return new TutorialMaterials
            {
                AbyssByKey = abyssDict,
                ActiveAcidLake = activeAcidLake,
                Cartilage = abyssDict["Mat_Cartilage"],
                DigestedCalcified = abyssDict["Mat_DigestedCalcified"],
                OssifiedStone = abyssDict["Mat_OssifiedStone"],
                FleshWall = abyssDict["Mat_FleshWall"],
                Teeth = abyssDict["Mat_Teeth"],
                VeinTendon = abyssDict["Mat_VeinTendon"],
                HornedBone = abyssDict["Mat_HornedBone"],
                AcidGlow = abyssDict["Mat_Abyss_AcidGlow"],
                Pustule = LoadMat(PilotMaterialsFolder + "/Mat_Pustule.mat", abyssDict["Mat_FleshWall"]),
                PixelAcid = pixelAcid,
                PixelBlood = pixelBlood,
                AcidVapor = AssetDatabase.LoadAssetAtPath<Material>(VaporMatPath),
                AcidDroplet = AssetDatabase.LoadAssetAtPath<Material>(DropletMatPath),
                VirgilNpc = CreateOrUpdateVirgilMaterial(litFallback)
            };
        }

        private static Material LoadMat(string path, Material fallback)
        {
            Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
            return m != null ? m : fallback;
        }

        private static Material CreateOrUpdateVirgilMaterial(Shader fallbackShader)
        {
            Shader virusShader = Shader.Find("MapaEstomago/URP/PS1_VirusPlayer");
            if (virusShader == null) virusShader = fallbackShader;

            Material srcPlayerMat = AssetDatabase.LoadAssetAtPath<Material>(PilotMaterialsFolder + "/Mat_VirusPlayer.mat");
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(VirgilMatPath);

            if (mat == null)
            {
                mat = srcPlayerMat != null ? new Material(srcPlayerMat) : new Material(virusShader);
                mat.name = "Mat_VirgilNpc";
                AssetDatabase.CreateAsset(mat, VirgilMatPath);
            }

            if (virusShader != null && mat.shader != virusShader)
            {
                mat.shader = virusShader;
            }

            Texture2D srcAlbedo = null;
            Texture2D srcEmissive = null;

            if (srcPlayerMat != null)
            {
                if (srcPlayerMat.HasProperty("_BaseMap")) srcAlbedo = srcPlayerMat.GetTexture("_BaseMap") as Texture2D;
                if (srcPlayerMat.HasProperty("_EmissionMap")) srcEmissive = srcPlayerMat.GetTexture("_EmissionMap") as Texture2D;
            }
            if (srcAlbedo == null) srcAlbedo = AssetDatabase.LoadAssetAtPath<Texture2D>(PilotTexturesFolder + "/tex_virus_albedo.png");
            if (srcEmissive == null) srcEmissive = AssetDatabase.LoadAssetAtPath<Texture2D>(PilotTexturesFolder + "/tex_virus_emissive.png");

            Texture2D virgilAlbedo = CreateRecoloredVirgilTexture(
                srcAlbedo,
                "Assets/StomachAbyss/Textures/tex_virgil_albedo.png",
                new Color(0.78f, 0.70f, 0.56f, 1f),
                isEmissive: false);
            Texture2D virgilEmissive = CreateRecoloredVirgilTexture(
                srcEmissive,
                "Assets/StomachAbyss/Textures/tex_virgil_emissive.png",
                new Color(1.0f, 0.68f, 0.18f, 1f),
                isEmissive: true);

            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", virgilAlbedo != null ? virgilAlbedo : srcAlbedo);
            if (mat.HasProperty("_EmissionMap")) mat.SetTexture("_EmissionMap", virgilEmissive != null ? virgilEmissive : srcEmissive);

            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", new Color(0.92f, 0.85f, 0.72f, 1f));
            if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", new Color(1.75f, 1.10f, 0.25f, 1f));
            if (mat.HasProperty("_PulseSpeed")) mat.SetFloat("_PulseSpeed", 1.1f);
            if (mat.HasProperty("_Wetness")) mat.SetFloat("_Wetness", 0.25f);
            if (mat.HasProperty("_AmbientFloor")) mat.SetFloat("_AmbientFloor", 0.38f);
            if (mat.HasProperty("_HitFlash")) mat.SetFloat("_HitFlash", 0f);

            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static Texture2D CreateRecoloredVirgilTexture(Texture2D source, string destAssetPath, Color targetTint, bool isEmissive)
        {
            if (source == null) return null;
            EnsureFolderHierarchy("Assets/StomachAbyss/Textures");

            int w = Mathf.Max(4, source.width);
            int h = Mathf.Max(4, source.height);

            RenderTexture tmp = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(source, tmp);
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = tmp;

            Texture2D copy = new Texture2D(w, h, TextureFormat.RGBA32, false);
            copy.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            copy.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(tmp);

            Color[] pixels = copy.GetPixels();
            for (int i = 0; i < pixels.Length; i++)
            {
                Color c = pixels[i];
                float lum = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
                if (isEmissive)
                {
                    pixels[i] = new Color(lum * targetTint.r, lum * targetTint.g, lum * targetTint.b, 1f);
                }
                else
                {
                    float aged = Mathf.Lerp(0.18f, 0.92f, lum);
                    pixels[i] = new Color(aged * targetTint.r, aged * targetTint.g, aged * targetTint.b, c.a);
                }
            }
            copy.SetPixels(pixels);
            copy.Apply();

            string fullPath = Path.Combine(Directory.GetCurrentDirectory(), destAssetPath);
            File.WriteAllBytes(fullPath, copy.EncodeToPNG());
            DestroyImmediateSafe(copy);

            AssetDatabase.ImportAsset(destAssetPath, ImportAssetOptions.ForceSynchronousImport);
            TextureImporter ti = AssetImporter.GetAtPath(destAssetPath) as TextureImporter;
            if (ti != null)
            {
                ti.filterMode = FilterMode.Point;
                ti.textureCompression = TextureImporterCompression.Uncompressed;
                ti.mipmapEnabled = false;
                ti.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Texture2D>(destAssetPath);
        }

        // ------------------------------------------------------------------
        // 1. CAVERNA VISCERAL, BACIA DE ACIDO, MONUMENTOS & ILHOTAS
        // ------------------------------------------------------------------
        private static MeshRenderer BuildCavernAndAcidBasin(
            Transform root,
            TutorialMaterials mats,
            List<Renderer> waterfallRenderers)
        {
            GameObject grp = CreateSubGroup(root, "01_Cardia_Cavern_And_Acid", Vector3.zero);

            // Lago de Acido principal
            GameObject lakeGo = PlaceGlb(
                grp.transform, "Abyss_Acid_Lake", "Cardia_Abyss_Acid_Lake",
                Origin, Quaternion.identity, Vector3.one, mats.AbyssByKey, addColliders: false);

            MeshRenderer lakeRenderer = null;
            if (lakeGo != null)
            {
                MeshRenderer[] lakeMrs = lakeGo.GetComponentsInChildren<MeshRenderer>(true);
                for (int i = 0; i < lakeMrs.Length; i++)
                {
                    MeshRenderer mr = lakeMrs[i];
                    if (mr == null) continue;
                    if (mats.ActiveAcidLake != null) mr.sharedMaterial = mats.ActiveAcidLake;
                    mr.shadowCastingMode = ShadowCastingMode.Off;
                    if (string.Equals(mr.gameObject.name, "Abyss_Acid_Lake", StringComparison.OrdinalIgnoreCase))
                    {
                        lakeRenderer = mr;
                    }
                }
                if (lakeRenderer == null && lakeMrs.Length > 0) lakeRenderer = lakeMrs[0];
            }

            // Bacia solida de seguranca + zona de dano acido
            GameObject safetyBed = new GameObject("Cardia_AcidBed_SafetyCollider");
            safetyBed.transform.SetParent(grp.transform, false);
            safetyBed.transform.position = Origin + new Vector3(0f, -1.5f, 0f);
            BoxCollider bedCol = safetyBed.AddComponent<BoxCollider>();
            bedCol.isTrigger = false;
            bedCol.size = new Vector3(180f, 1f, 210f);

            GameObject acidZoneGo = new GameObject("Cardia_Ambient_AcidDamageZone");
            acidZoneGo.transform.SetParent(grp.transform, false);
            acidZoneGo.transform.position = Origin + new Vector3(0f, -0.35f, 0f);
            BoxCollider acidCol = acidZoneGo.AddComponent<BoxCollider>();
            acidCol.isTrigger = true;
            acidCol.size = new Vector3(180f, 0.9f, 210f);
            AcidDamageZone dmgZone = acidZoneGo.AddComponent<AcidDamageZone>();
            dmgZone.damagePerSecondInside = 18f;
            dmgZone.lingerDurationAfterExit = 1.5f;
            dmgZone.lingerDamagePerSecond = 8f;
            dmgZone.logDebugTicks = false;

            // 2x Cachoeiras de Acido & Sangue
            GameObject wfWest = PlaceGlb(
                grp.transform, "Waterfall_AcidBlood", "Cardia_Waterfall_Acid_West",
                Origin + new Vector3(-56.5f, 0f, -14f), Quaternion.Euler(0f, 76f, 0f), new Vector3(1.30f, 1.42f, 1.30f),
                mats.AbyssByKey);
            GameObject wfEast = PlaceGlb(
                grp.transform, "Waterfall_AcidBlood", "Cardia_Waterfall_Acid_East",
                Origin + new Vector3(56.5f, 0f, 30f), Quaternion.Euler(0f, -104f, 0f), new Vector3(1.25f, 1.48f, 1.25f),
                mats.AbyssByKey);

            ConfigureWaterfallScroll(wfWest, waterfallRenderers);
            ConfigureWaterfallScroll(wfEast, waterfallRenderers);

            // Shell da Caverna (paredes, costelas e abobada membranosa)
            PlaceGlb(grp.transform, "Cavern_Wall_Long", "Cardia_Cavern_Wall_West",
                Origin + new Vector3(-74f, 0f, 6f), Quaternion.Euler(0f, 90f, 0f), Vector3.one, mats.AbyssByKey);
            PlaceGlb(grp.transform, "Cavern_Wall_Long", "Cardia_Cavern_Wall_East",
                Origin + new Vector3(74f, 0f, 6f), Quaternion.Euler(0f, -90f, 0f), Vector3.one, mats.AbyssByKey);
            PlaceGlb(grp.transform, "Cavern_Wall_End", "Cardia_Cavern_Wall_North",
                Origin + new Vector3(0f, 0f, 89f), Quaternion.Euler(0f, 180f, 0f), Vector3.one, mats.AbyssByKey);
            PlaceGlb(grp.transform, "Cavern_Wall_End", "Cardia_Cavern_Wall_South",
                Origin + new Vector3(0f, 0f, -83f), Quaternion.identity, Vector3.one, mats.AbyssByKey);

            PlaceGlb(grp.transform, "Cavern_RibArch", "Cardia_Cavern_RibArch_South",
                Origin + new Vector3(0f, 0f, -32f), Quaternion.identity, new Vector3(1.08f, 1.06f, 1.0f), mats.AbyssByKey);
            PlaceGlb(grp.transform, "Cavern_RibArch", "Cardia_Cavern_RibArch_Center",
                Origin + new Vector3(0f, 0f, 8f), Quaternion.Euler(0f, 4f, 0f), new Vector3(1.08f, 1.06f, 1.0f), mats.AbyssByKey);
            PlaceGlb(grp.transform, "Cavern_RibArch", "Cardia_Cavern_RibArch_North",
                Origin + new Vector3(0f, 0f, 44f), Quaternion.Euler(0f, -4f, 0f), new Vector3(1.08f, 1.06f, 1.0f), mats.AbyssByKey);

            PlaceGlb(grp.transform, "Cavern_Membrane_CeilingVault", "Cardia_Cavern_Membrane_CeilingVault",
                Origin + new Vector3(0f, 0f, 4f), Quaternion.identity, Vector3.one, mats.AbyssByKey, addColliders: false);

            // Monumentos Colossais e Portal
            GameObject southButtress = PlaceGlb(grp.transform, "Monument_GothicButtress_South", "Cardia_Monument_GothicButtress_South",
                Origin + new Vector3(0f, 0f, -60.5f), Quaternion.identity, Vector3.one, mats.AbyssByKey);
            if (southButtress != null)
            {
                foreach (MeshRenderer mr in southButtress.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (mr != null && mr.GetComponent<BG3WallCutoutController>() == null)
                    {
                        mr.gameObject.AddComponent<BG3WallCutoutController>();
                    }
                }
            }
            PlaceGlb(grp.transform, "Monument_GiantSkull_West", "Cardia_Monument_GiantSkull_West",
                Origin + new Vector3(-45f, 0f, 2f), Quaternion.Euler(0f, 82f, 0f), new Vector3(1.22f, 1.22f, 1.22f), mats.AbyssByKey);
            PlaceGlb(grp.transform, "Monument_GiantBoneWing_East", "Cardia_Monument_GiantBoneWing_East",
                Origin + new Vector3(45f, 0f, -7f), Quaternion.Euler(0f, -96f, 0f), new Vector3(1.24f, 1.24f, 1.24f), mats.AbyssByKey);
            PlaceGlb(grp.transform, "Monument_HornedColossus_North", "Cardia_Monument_HornedColossus_North",
                Origin + new Vector3(0f, 2.4f, 72.5f), Quaternion.Euler(0f, 180f, 0f), new Vector3(1.26f, 1.26f, 1.26f), mats.AbyssByKey);
            PlaceGlb(grp.transform, "Portal_Exit_Gate", "Cardia_Portal_Exit_Gate",
                Origin + new Vector3(0f, 6.2f, 64.8f), Quaternion.Euler(0f, 180f, 0f), new Vector3(1.12f, 1.12f, 1.12f), mats.AbyssByKey);

            // 4x Pinaculos Afundados
            PlaceGlb(grp.transform, "Monument_SunkenSpire_A", "Cardia_Spire_Sunken_SW",
                Origin + new Vector3(-23f, 0f, -31f), Quaternion.Euler(0f, 34f, 0f), new Vector3(1.15f, 1.28f, 1.15f), mats.AbyssByKey);
            PlaceGlb(grp.transform, "Monument_SunkenSpire_B", "Cardia_Spire_Sunken_SE",
                Origin + new Vector3(21.5f, 0f, -29f), Quaternion.Euler(0f, -42f, 0f), new Vector3(1.22f, 1.20f, 1.22f), mats.AbyssByKey);
            PlaceGlb(grp.transform, "Monument_SunkenSpire_A", "Cardia_Spire_Sunken_NW",
                Origin + new Vector3(-30f, 0f, 38f), Quaternion.Euler(0f, 128f, 0f), new Vector3(1.28f, 1.42f, 1.28f), mats.AbyssByKey);
            PlaceGlb(grp.transform, "Monument_SunkenSpire_B", "Cardia_Spire_Sunken_NE",
                Origin + new Vector3(27f, 0f, 40f), Quaternion.Euler(0f, -112f, 0f), new Vector3(1.15f, 1.28f, 1.15f), mats.AbyssByKey);

            // 8x Ilhotas Flutuantes decorativas (livres das ilhotas dos selos Leste (49.5,15) e Oeste (-54,1))
            PlaceFloatingIslet(grp.transform, "Prop_FloatingIslet_A", "Cardia_FloatingIslet_01", Origin + new Vector3(-18f, 0f, -14f), 25f, 1.05f, mats.AbyssByKey);
            PlaceFloatingIslet(grp.transform, "Prop_FloatingIslet_B", "Cardia_FloatingIslet_02", Origin + new Vector3(18f, 0f, -14f), -52f, 1.10f, mats.AbyssByKey);
            PlaceFloatingIslet(grp.transform, "Prop_FloatingIslet_A", "Cardia_FloatingIslet_03", Origin + new Vector3(-27f, 0f, -43f), 110f, 1.20f, mats.AbyssByKey);
            PlaceFloatingIslet(grp.transform, "Prop_FloatingIslet_B", "Cardia_FloatingIslet_04", Origin + new Vector3(25f, 0f, -43f), -135f, 1.15f, mats.AbyssByKey);
            PlaceFloatingIslet(grp.transform, "Prop_FloatingIslet_B", "Cardia_FloatingIslet_05", Origin + new Vector3(-16f, 0f, 32f), 72f, 0.95f, mats.AbyssByKey);
            PlaceFloatingIslet(grp.transform, "Prop_FloatingIslet_A", "Cardia_FloatingIslet_06", Origin + new Vector3(19f, 0f, 32f), -18f, 1.02f, mats.AbyssByKey);
            PlaceFloatingIslet(grp.transform, "Prop_FloatingIslet_A", "Cardia_FloatingIslet_07", Origin + new Vector3(36f, 0f, 28f), 154f, 1.28f, mats.AbyssByKey);
            PlaceFloatingIslet(grp.transform, "Prop_FloatingIslet_B", "Cardia_FloatingIslet_08", Origin + new Vector3(-37f, 0f, 25f), -76f, 1.24f, mats.AbyssByKey);

            return lakeRenderer;
        }

        private static void ConfigureWaterfallScroll(GameObject wfGo, List<Renderer> targetList)
        {
            if (wfGo == null) return;
            MeshRenderer[] mrs = wfGo.GetComponentsInChildren<MeshRenderer>(true);
            for (int i = 0; i < mrs.Length; i++)
            {
                MeshRenderer mr = mrs[i];
                if (mr == null) continue;
                string n = mr.gameObject.name;

                if (n.StartsWith("Waterfall_Acid_Sheet", StringComparison.OrdinalIgnoreCase)
                    || n.StartsWith("Waterfall_BloodAcid_Foam", StringComparison.OrdinalIgnoreCase)
                    || n.StartsWith("Waterfall_Splash_Ring", StringComparison.OrdinalIgnoreCase))
                {
                    targetList.Add(mr);

                    if (!n.StartsWith("Waterfall_Splash_Ring", StringComparison.OrdinalIgnoreCase))
                    {
                        AcidScroll scroll = mr.GetComponent<AcidScroll>();
                        if (scroll == null) scroll = mr.gameObject.AddComponent<AcidScroll>();
                        scroll.scrollSpeed = new Vector2(0.01f, 0.34f);
                        scroll.pulseAmplitude = 0f;
                        EditorUtility.SetDirty(scroll);
                    }
                }
            }
        }

        // ------------------------------------------------------------------
        // 2. 4 ARENAS + 5 PONTES + INIMIGOS + SELOS + BARREIRA + SAIDA
        // ------------------------------------------------------------------
        private static FleshEyeDoorController BuildArenasAndBridges(
            Transform root,
            TutorialMaterials mats,
            TutorialMeshes meshes,
            GameObject doorPrefab,
            GameObject virusPrefab,
            GameObject macrophagePrefab,
            GameObject eosinophilPrefab)
        {
            GameObject grp = CreateSubGroup(root, "02_Cardia_Arenas_And_Bridges", Vector3.zero);

            // --- ARENA 1: LEITO DA QUEDA (deck y = Origin.y + 2.0 = 12.0) ---
            PlaceGlb(grp.transform, "Arena_South_OssuaryEntry", "Cardia_Arena_01_LeitoDaQueda",
                Origin + new Vector3(0f, 0f, -48f), Quaternion.identity, Vector3.one, mats.AbyssByKey);

            BuildDigestedCrater(grp.transform, Origin + new Vector3(0f, 2.02f, -49.0f), mats);
            BuildCardiaSphincterOverhead(grp.transform, Origin + new Vector3(0f, 23.5f, -49.0f), mats, meshes);

            Vector3 pedestalPos = Origin + new Vector3(2.6f, 2.0f, -44.2f);
            BuildVirgilPedestal(grp.transform, pedestalPos, mats);
            Vector3 virgilPos = pedestalPos + new Vector3(0f, 0.36f, 0f);
            Vector3 toSpawn = PlayerSpawnPos - virgilPos;
            toSpawn.y = 0f;
            Quaternion virgilRot = toSpawn.sqrMagnitude > 0.001f
                ? Quaternion.LookRotation(toSpawn.normalized, Vector3.up)
                : Quaternion.Euler(0f, -155f, 0f);
            InstantiateVirgilNpc(
                grp.transform, "Virgil_NPC_Cardia", virgilPos, virgilRot,
                VirgilAppearanceRole.CardiaTutorial, 2.8f, mats.VirgilNpc, virusPrefab);

            // --- PONTE 1: ESCADARIA DO REFLUXO ---
            PlaceGlb(grp.transform, "Bridge_OssuaryStairs_South", "Cardia_Bridge_01_EscadariaRefluxo",
                Origin + new Vector3(0f, 0f, -33f), Quaternion.identity, Vector3.one, mats.AbyssByKey);

            // Obstaculo de salto (~0.72m altura, 3.6m largura) para praticar [Q] Pulo
            BuildJumpHurdle(grp.transform, Origin + new Vector3(0f, 2.55f, -33.2f), mats);

            // Geiser na borda norte da escadaria para praticar [Espaco] Esquiva
            BuildAcidGeyser(grp.transform, "Cardia_Geyser_StairsNorth",
                Origin + new Vector3(0f, 3.15f, -27.6f), mats,
                warningDur: 1.6f, eruptionDur: 1.0f, dormantMin: 2.2f, dormantMax: 2.8f, radius: 2.1f, dps: 20f);

            // --- ARENA 2: PATIO DOS DEGOLADOS (deck y = Origin.y + 3.2 = 13.2) ---
            PlaceGlb(grp.transform, "Arena_SouthCentral_Hub", "Cardia_Arena_02_PatioDosDegolados",
                Origin + new Vector3(0f, 0f, -16f), Quaternion.identity, Vector3.one, mats.AbyssByKey);

            SpawnMacrophage(grp.transform, "Cardia_Abade_Arena2", macrophagePrefab,
                Origin + new Vector3(-5.0f, 3.2f, -16.5f), 45f, 95f, dropHealOrb: true);
            SpawnEosinophil(grp.transform, "Cardia_Eosinofilo_Arena2", eosinophilPrefab,
                Origin + new Vector3(5.2f, 5.65f, -14.0f), -45f, 75f, dropHealOrb: true);

            // --- PONTE 2: GRANDE VIADUTO DE TENDOES ---
            PlaceGlb(grp.transform, "Bridge_VeinTendon_Grand", "Cardia_Bridge_02_ViadutoTendoes",
                Origin + new Vector3(0f, 0f, 0.5f), Quaternion.identity, Vector3.one, mats.AbyssByKey);

            // Sincroniza colliders recem-adicionados para posicionar os geiseres rentes ao arco da ponte
            Physics.SyncTransforms();

            Vector3 geyser2South = SnapToWalkableSurface(Origin + new Vector3(0f, 3.9f, -2.5f), Origin.y + 4.46f);
            Vector3 geyser2North = SnapToWalkableSurface(Origin + new Vector3(0f, 4.4f, 3.5f), Origin.y + 4.84f);
            BuildAcidGeyser(grp.transform, "Cardia_Geyser_VeinBridge_South", geyser2South, mats,
                warningDur: 1.5f, eruptionDur: 1.0f, dormantMin: 2.4f, dormantMax: 3.2f, radius: 2.0f, dps: 22f);
            BuildAcidGeyser(grp.transform, "Cardia_Geyser_VeinBridge_North", geyser2North, mats,
                warningDur: 1.5f, eruptionDur: 1.0f, dormantMin: 2.6f, dormantMax: 3.4f, radius: 2.0f, dps: 22f);

            // --- ARENA 3: PLATO DO RITUAL VISCERAL (deck y = Origin.y + 4.6 = 14.6) ---
            PlaceGlb(grp.transform, "Arena_Central_RitualPlateau", "Cardia_Arena_03_PlatoRitual",
                Origin + new Vector3(0f, 0f, 18f), Quaternion.identity, Vector3.one, mats.AbyssByKey);
            PlaceGlb(grp.transform, "Prop_RitualCircle_Center", "Cardia_Prop_RitualCircle_Center",
                Origin + new Vector3(0f, 4.62f, 18f), Quaternion.identity, Vector3.one, mats.AbyssByKey);

            BuildPlateauAreaBanner(grp.transform, Origin + new Vector3(0f, 5.2f, 11.5f));

            // --- ASA LESTE (SELO 2) ---
            // EastOverlook_Deck alinhado com o offset real de Stomach_Abyss (+6.03, 0, +0.72) -> deck y = 15.4
            PlaceGlb(grp.transform, "Arena_East_TendonOverlook", "Cardia_Arena_East_TendonOverlook",
                Origin + new Vector3(34.03f, 0f, 14.72f), Quaternion.identity, Vector3.one, mats.AbyssByKey);

            // Plank span Leste conectando CentralPlateau (x~14.5, y=4.6) -> EastOverlook_Deck (x~25.2, y=5.4) sem degrau nem gap
            PlaceGlb(grp.transform, "Bridge_PlankSpan_East", "Cardia_Bridge_East_PlankSpan",
                Origin + new Vector3(19.90f, 0f, 17.10f), Quaternion.Euler(0f, 12f, 0f), new Vector3(1.22f, 1.0f, 0.70f),
                mats.AbyssByKey);

            SpawnMacrophage(grp.transform, "Cardia_Abade_EastWing", macrophagePrefab,
                Origin + new Vector3(33.5f, 5.4f, 14.5f), -90f, 100f, dropHealOrb: true);
            SpawnEosinophil(grp.transform, "Cardia_Eosinofilo_EastWing", eosinophilPrefab,
                Origin + new Vector3(36.5f, 7.85f, 18.0f), -110f, 80f, dropHealOrb: false);

            PlaceGlb(grp.transform, "Prop_FloatingIslet_B", "East_Seal_Islet",
                Origin + new Vector3(49.5f, 2.8f, 15.0f), Quaternion.Euler(0f, -35f, 0f), Vector3.one * 0.72f, mats.AbyssByKey);

            // --- ASA OESTE (SELO 3) ---
            // WestBastion_Deck alinhado com o offset real de Stomach_Abyss (-13.92, 0, -9.77) -> deck y = 14.0
            PlaceGlb(grp.transform, "Arena_West_CraterBastion", "Cardia_Arena_West_CraterBastion",
                Origin + new Vector3(-38.92f, 0f, 1.23f), Quaternion.identity, Vector3.one, mats.AbyssByKey);

            // Sub-estrutura organica de sustentacao sob as pranchas oeste (reutiliza WestBridge_UnderRibs de Bridge_StairSpan_West)
            GameObject westUnderRibs = PlaceGlb(grp.transform, "Bridge_StairSpan_West", "Cardia_Bridge_West_UnderRibs",
                Origin + new Vector3(-22.5f, -0.35f, 10.6f), Quaternion.Euler(0f, -27f, 0f), new Vector3(1.25f, 1.0f, 0.95f),
                mats.AbyssByKey, addColliders: false);
            if (westUnderRibs != null)
            {
                Transform stairsChild = westUnderRibs.transform.Find("WestBridge_Stairs");
                if (stairsChild != null) DestroyImmediateSafe(stairsChild.gameObject);
            }

            // 2 lances de pranchas oeste conectando CentralPlateau (y=4.6) -> WestBastion_Deck (y=4.0) com transicao continua
            PlaceGlb(grp.transform, "Bridge_PlankSpan_East", "Cardia_Bridge_West_Plank_01",
                Origin + new Vector3(-18.50f, -0.58f, 12.60f), Quaternion.Euler(0f, 333f, -2.6f), new Vector3(1.05f, 1.0f, 0.685f),
                mats.AbyssByKey);
            PlaceGlb(grp.transform, "Bridge_PlankSpan_East", "Cardia_Bridge_West_Plank_02",
                Origin + new Vector3(-27.30f, -0.83f, 8.20f), Quaternion.Euler(0f, 333f, -2.6f), new Vector3(1.15f, 1.0f, 0.685f),
                mats.AbyssByKey);

            SpawnEosinophil(grp.transform, "Cardia_Eosinofilo_WestWing", eosinophilPrefab,
                Origin + new Vector3(-42.0f, 6.45f, -1.5f), 90f, 80f, dropHealOrb: true);
            BuildAcidGeyser(grp.transform, "Cardia_Geyser_WestBastion",
                Origin + new Vector3(-34.0f, 4.05f, 4.8f), mats,
                warningDur: 1.5f, eruptionDur: 1.0f, dormantMin: 2.3f, dormantMax: 3.1f, radius: 2.0f, dps: 20f);

            PlaceGlb(grp.transform, "Prop_FloatingIslet_A", "West_Seal_Islet",
                Origin + new Vector3(-54.0f, 1.8f, 1.0f), Quaternion.Euler(0f, 42f, 0f), Vector3.one * 0.72f, mats.AbyssByKey);

            // --- PONTE 3 + ARENA 4: ALTAR DO TITA & SAIDA (deck y = Origin.y + 6.2 = 16.2) ---
            PlaceGlb(grp.transform, "Bridge_Stairs_North", "Cardia_Bridge_03_EscadariaNorte",
                Origin + new Vector3(0f, 0f, 38f), Quaternion.identity, Vector3.one, mats.AbyssByKey);
            PlaceGlb(grp.transform, "Arena_North_TitanAltar", "Cardia_Arena_04_AltarDoTita",
                Origin + new Vector3(0f, 0f, 57f), Quaternion.identity, Vector3.one, mats.AbyssByKey);

            SpawnMacrophage(grp.transform, "Cardia_Abade_NorthAltar", macrophagePrefab,
                Origin + new Vector3(-5.0f, 6.2f, 56.5f), 150f, 110f, dropHealOrb: true);
            SpawnEosinophil(grp.transform, "Cardia_Eosinofilo_NorthAltar", eosinophilPrefab,
                Origin + new Vector3(4.5f, 8.65f, 58.5f), -150f, 85f, dropHealOrb: false);

            // Porta de Saida e Barreira ligada aos 3 Selos
            FleshEyeDoorController exitDoor = BuildCardiaExitDoor(
                grp.transform, Origin + new Vector3(0f, 6.2f, 63.0f), mats, doorPrefab);
            Collider doorTriggerCollider = exitDoor != null ? GetDoorTriggerCollider(exitDoor.gameObject) : null;
            if (doorTriggerCollider != null)
            {
                doorTriggerCollider.enabled = false;
            }

            TutorialGateBarrier barrier = BuildTutorialGateBarrier(
                grp.transform, Origin + new Vector3(0f, 6.2f, 60.5f), mats, meshes, doorTriggerCollider);

            TutorialCystTarget meleeCyst = BuildCystTarget(
                grp.transform, "Cyst_Melee_Node",
                Origin + new Vector3(0f, 3.2f, -15.5f),
                TutorialTargetKind.MeleeNode, 85f, 1.3f,
                new Color(1.0f, 0.28f, 0.18f, 1f),
                mats.Pustule, mats.FleshWall, meshes.CystCluster, barrier);

            TutorialCystTarget eastSeal = BuildCystTarget(
                grp.transform, "Cyst_Ranged_Seal_East",
                Origin + new Vector3(49.5f, 4.3f, 15.0f),
                TutorialTargetKind.RangedSeal, 45f, 1.35f,
                new Color(0.45f, 1.0f, 0.24f, 1f),
                mats.AcidGlow, mats.Pustule, meshes.CystCluster, barrier);

            TutorialCystTarget westSeal = BuildCystTarget(
                grp.transform, "Cyst_Ranged_Seal_West",
                Origin + new Vector3(-54.0f, 3.3f, 1.0f),
                TutorialTargetKind.RangedSeal, 45f, 1.35f,
                new Color(0.45f, 1.0f, 0.24f, 1f),
                mats.AcidGlow, mats.Pustule, meshes.CystCluster, barrier);

            barrier.requiredTargets = new List<TutorialCystTarget> { meleeCyst, eastSeal, westSeal };
            EditorUtility.SetDirty(barrier);

            return exitDoor;
        }

        private static Vector3 SnapToWalkableSurface(Vector3 pos, float fallbackWorldY)
        {
            if (Physics.Raycast(new Vector3(pos.x, pos.y + 6.0f, pos.z), Vector3.down, out RaycastHit hit, 14.0f, ~0, QueryTriggerInteraction.Ignore))
            {
                return new Vector3(pos.x, hit.point.y, pos.z);
            }
            return new Vector3(pos.x, fallbackWorldY, pos.z);
        }

        private static void SpawnMacrophage(
            Transform parent,
            string name,
            GameObject prefab,
            Vector3 worldPos,
            float yawDeg,
            float maxHealth,
            bool dropHealOrb)
        {
            if (prefab == null)
            {
                Debug.LogWarning($"[CardiaTutorialSetup] Prefab nao encontrado: {MacrophagePrefabPath}");
                return;
            }

            GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            go.name = name;
            go.transform.position = worldPos;
            go.transform.rotation = Quaternion.Euler(0f, yawDeg, 0f);

            MacrophageEnemyAI ai = go.GetComponent<MacrophageEnemyAI>();
            if (ai != null)
            {
                ai.maxHealth = maxHealth;
                ai.currentHealth = maxHealth;
                ai.dropMinorHealOrbOnDeath = dropHealOrb;
                EditorUtility.SetDirty(ai);
            }
            EditorUtility.SetDirty(go);
        }

        private static void SpawnEosinophil(
            Transform parent,
            string name,
            GameObject prefab,
            Vector3 worldPos,
            float yawDeg,
            float maxHealth,
            bool dropHealOrb)
        {
            if (prefab == null)
            {
                Debug.LogWarning($"[CardiaTutorialSetup] Prefab nao encontrado: {EosinophilPrefabPath}");
                return;
            }

            GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            go.name = name;
            go.transform.position = worldPos;
            go.transform.rotation = Quaternion.Euler(0f, yawDeg, 0f);

            EosinophilEnemyAI ai = go.GetComponent<EosinophilEnemyAI>();
            if (ai != null)
            {
                ai.maxHealth = maxHealth;
                ai.currentHealth = maxHealth;
                ai.dropMinorHealOrbOnDeath = dropHealOrb;
                EditorUtility.SetDirty(ai);
            }
            EditorUtility.SetDirty(go);
        }

        private static void BuildPlateauAreaBanner(Transform parent, Vector3 worldPos)
        {
            GameObject bannerGo = new GameObject("Cardia_Plateau_Banner_Checkpoint");
            bannerGo.transform.SetParent(parent, false);
            bannerGo.transform.position = worldPos;

            BoxCollider col = bannerGo.AddComponent<BoxCollider>();
            col.isTrigger = true;
            col.size = new Vector3(8.5f, 4.5f, 4.5f);
        }

        private static void BuildJumpHurdle(Transform parent, Vector3 baseWorldPos, TutorialMaterials mats)
        {
            GameObject hurdle = new GameObject("Jump_Obstacle_Ribs");
            hurdle.transform.SetParent(parent, false);
            hurdle.transform.position = baseWorldPos;

            // Viga principal de cartilagem (~0.72m acima da escadaria, 3.6m de largura) com BoxCollider solido
            GameObject beam = GameObject.CreatePrimitive(PrimitiveType.Cube);
            beam.name = "Jump_Ribs_Bar";
            beam.transform.SetParent(hurdle.transform, false);
            beam.transform.localPosition = new Vector3(0f, 0.36f, 0f);
            beam.transform.localScale = new Vector3(3.6f, 0.72f, 0.65f);
            if (mats.Cartilage != null) beam.GetComponent<Renderer>().sharedMaterial = mats.Cartilage;

            // Costelas laterais esculpidas em osso para leitura visual clara do obstaculo
            for (int i = -2; i <= 2; i++)
            {
                GameObject rib = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                rib.name = $"Jump_Rib_Spur_{i + 2:00}";
                rib.transform.SetParent(hurdle.transform, false);
                rib.transform.localPosition = new Vector3(i * 0.78f, 0.42f, 0f);
                rib.transform.localRotation = Quaternion.Euler(22f, 0f, i * 6f);
                rib.transform.localScale = new Vector3(0.24f, 0.46f, 0.24f);
                DestroyImmediateSafe(rib.GetComponent<Collider>());
                if (mats.HornedBone != null) rib.GetComponent<Renderer>().sharedMaterial = mats.HornedBone;
            }

            EditorUtility.SetDirty(hurdle);
        }

        private static void BuildVirgilPedestal(Transform parent, Vector3 pedestalPos, TutorialMaterials mats)
        {
            GameObject pedestal = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pedestal.name = "Virgil_Cartilage_Pedestal";
            pedestal.transform.SetParent(parent, false);
            pedestal.transform.position = pedestalPos + new Vector3(0f, 0.16f, 0f);
            pedestal.transform.localScale = new Vector3(2.3f, 0.16f, 2.3f);
            if (mats.Cartilage != null) pedestal.GetComponent<Renderer>().sharedMaterial = mats.Cartilage;

            GameObject pedestalStep = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pedestalStep.name = "Virgil_Cartilage_Pedestal_Inner";
            pedestalStep.transform.SetParent(pedestal.transform, false);
            pedestalStep.transform.localPosition = new Vector3(0f, 1.1f, 0f);
            pedestalStep.transform.localScale = new Vector3(0.68f, 0.75f, 0.68f);
            if (mats.OssifiedStone != null) pedestalStep.GetComponent<Renderer>().sharedMaterial = mats.OssifiedStone;
        }

        private static void BuildDigestedCrater(Transform parent, Vector3 pos, TutorialMaterials mats)
        {
            GameObject crater = new GameObject("Cratera_Restos_Digeridos");
            crater.transform.SetParent(parent, false);
            crater.transform.position = pos;

            GameObject stain = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            stain.name = "Blood_Stain_Pool";
            stain.transform.SetParent(crater.transform, false);
            stain.transform.localPosition = new Vector3(0f, 0.02f, 0f);
            stain.transform.localScale = new Vector3(4.2f, 0.02f, 4.2f);
            DestroyImmediateSafe(stain.GetComponent<Collider>());
            if (mats.VeinTendon != null) stain.GetComponent<Renderer>().sharedMaterial = mats.VeinTendon;

            for (int i = 0; i < 12; i++)
            {
                float angle = (i / 12f) * Mathf.PI * 2f;
                float r = 2.0f + (i % 3) * 0.22f;
                Vector3 localPos = new Vector3(Mathf.Cos(angle) * r, 0.12f, Mathf.Sin(angle) * r);

                GameObject chunk = GameObject.CreatePrimitive(i % 2 == 0 ? PrimitiveType.Cube : PrimitiveType.Capsule);
                chunk.name = $"Digested_Remnant_{i:00}";
                chunk.transform.SetParent(crater.transform, false);
                chunk.transform.localPosition = localPos;
                chunk.transform.localRotation = Quaternion.Euler(18f * (i % 3), angle * Mathf.Rad2Deg, 25f);
                chunk.transform.localScale = i % 2 == 0
                    ? new Vector3(0.55f, 0.24f, 0.42f)
                    : new Vector3(0.20f, 0.42f, 0.20f);
                DestroyImmediateSafe(chunk.GetComponent<Collider>());
                chunk.GetComponent<Renderer>().sharedMaterial = (i % 3 == 0) ? mats.HornedBone : mats.DigestedCalcified;
            }

            AddPointLight(crater.transform, "Light_Crater_BloodGlow", new Vector3(0f, 1.3f, 0f),
                new Color(0.92f, 0.18f, 0.14f), 2.4f, 7.0f);
        }

        private static void BuildCardiaSphincterOverhead(Transform parent, Vector3 pos, TutorialMaterials mats, TutorialMeshes meshes)
        {
            GameObject sphincter = new GameObject("Esfincter_Da_Cardia");
            sphincter.transform.SetParent(parent, false);
            sphincter.transform.position = pos;

            GameObject ring = CreateMeshObj(sphincter.transform, "Sphincter_Flesh_Ring", meshes.SphincterTorus, mats.FleshWall);
            ring.transform.localPosition = Vector3.zero;
            ring.transform.localScale = Vector3.one;

            GameObject throat = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            throat.name = "Esophagus_Dark_Throat";
            throat.transform.SetParent(sphincter.transform, false);
            throat.transform.localPosition = new Vector3(0f, 3.8f, 0f);
            throat.transform.localScale = new Vector3(5.6f, 3.6f, 5.6f);
            DestroyImmediateSafe(throat.GetComponent<Collider>());
            if (mats.VeinTendon != null) throat.GetComponent<Renderer>().sharedMaterial = mats.VeinTendon;

            for (int i = 0; i < 12; i++)
            {
                float angle = (i / 12f) * Mathf.PI * 2f;
                float radius = 2.45f;
                Vector3 dirInward = new Vector3(-Mathf.Cos(angle), -0.65f, -Mathf.Sin(angle)).normalized;

                GameObject tooth = CreateMeshObj(sphincter.transform, $"Sphincter_Tooth_{i:00}", meshes.ToothFang, mats.Teeth);
                tooth.transform.localPosition = new Vector3(Mathf.Cos(angle) * radius, -0.35f, Mathf.Sin(angle) * radius);
                tooth.transform.localRotation = Quaternion.FromToRotation(Vector3.up, dirInward);
                tooth.transform.localScale = new Vector3(0.42f, 1.05f, 0.42f);
            }

            GameObject dripGo = new GameObject("Sphincter_Drip_Particles");
            dripGo.transform.SetParent(sphincter.transform, false);
            dripGo.transform.localPosition = new Vector3(0f, -0.7f, 0f);

            ParticleSystem ps = dripGo.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = true;
            main.startLifetime = 2.8f;
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 1.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.18f, 0.34f);
            main.startColor = new Color(0.55f, 0.85f, 0.22f, 0.92f);
            main.gravityModifier = 0.62f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 48;

            var emission = ps.emission;
            emission.rateOverTime = 5.5f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 1.6f;
            shape.rotation = new Vector3(90f, 0f, 0f);

            ParticleSystemRenderer psr = dripGo.GetComponent<ParticleSystemRenderer>();
            psr.renderMode = ParticleSystemRenderMode.Billboard;
            if (mats.PixelBlood != null)
            {
                psr.sharedMaterial = mats.PixelBlood;
            }
        }

        private static void BuildAcidGeyser(
            Transform parent,
            string name,
            Vector3 pos,
            TutorialMaterials mats,
            float warningDur,
            float eruptionDur,
            float dormantMin,
            float dormantMax,
            float radius,
            float dps)
        {
            GameObject geyserGo = new GameObject(name);
            geyserGo.transform.SetParent(parent, false);
            geyserGo.transform.position = pos;

            GameObject rim = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            rim.name = "Geyser_Vent_Crater";
            rim.transform.SetParent(geyserGo.transform, false);
            rim.transform.localPosition = new Vector3(0f, 0.02f, 0f);
            rim.transform.localScale = new Vector3(2.3f, 0.04f, 2.3f);
            DestroyImmediateSafe(rim.GetComponent<Collider>());
            if (mats.DigestedCalcified != null) rim.GetComponent<Renderer>().sharedMaterial = mats.DigestedCalcified;

            GameObject coreGlow = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            coreGlow.name = "Geyser_Acid_Core";
            coreGlow.transform.SetParent(geyserGo.transform, false);
            coreGlow.transform.localPosition = new Vector3(0f, 0.05f, 0f);
            coreGlow.transform.localScale = new Vector3(1.2f, 0.03f, 1.2f);
            DestroyImmediateSafe(coreGlow.GetComponent<Collider>());
            if (mats.AcidGlow != null) coreGlow.GetComponent<Renderer>().sharedMaterial = mats.AcidGlow;

            Light warnLight = AddPointLight(geyserGo.transform, "Geyser_Warning_Light", new Vector3(0f, 0.9f, 0f),
                new Color(0.35f, 1.0f, 0.16f), 1.4f, 6.0f);

            AcidGeyserHazard hazard = geyserGo.AddComponent<AcidGeyserHazard>();
            hazard.warningDuration = warningDur;
            hazard.eruptionDuration = eruptionDur;
            hazard.dormantDurationMin = dormantMin;
            hazard.dormantDurationMax = dormantMax;
            hazard.hazardRadius = radius;
            hazard.damagePerSecond = dps;
            hazard.warningLight = warnLight;
            hazard.pixelParticleMaterial = mats.PixelAcid;
        }

        private static TutorialCystTarget BuildCystTarget(
            Transform parent,
            string name,
            Vector3 worldPos,
            TutorialTargetKind kind,
            float maxHealth,
            float colliderRadius,
            Color glowColor,
            Material primaryMat,
            Material secondaryMat,
            Mesh clusterMesh,
            TutorialGateBarrier linkedBarrier)
        {
            GameObject cystGo = new GameObject(name);
            cystGo.transform.SetParent(parent, false);
            cystGo.transform.position = worldPos;

            SphereCollider col = cystGo.AddComponent<SphereCollider>();
            col.radius = colliderRadius;
            col.center = kind == TutorialTargetKind.MeleeNode ? new Vector3(0f, 0.85f, 0f) : Vector3.zero;

            GameObject visualRoot = new GameObject("VisualRoot");
            visualRoot.transform.SetParent(cystGo.transform, false);
            visualRoot.transform.localPosition = col.center;

            GameObject mainBulb = CreateMeshObj(visualRoot.transform, "Cyst_MainCluster", clusterMesh, primaryMat);
            mainBulb.transform.localScale = Vector3.one * (colliderRadius * 0.92f);
            DestroyImmediateSafe(mainBulb.GetComponent<Collider>());

            for (int i = 0; i < 4; i++)
            {
                float a = (i / 4f) * Mathf.PI * 2f + 0.4f;
                GameObject lobe = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                lobe.name = $"Cyst_Lobe_{i:00}";
                lobe.transform.SetParent(visualRoot.transform, false);
                lobe.transform.localPosition = new Vector3(Mathf.Cos(a) * 0.55f, -0.15f + (i % 2) * 0.35f, Mathf.Sin(a) * 0.55f);
                lobe.transform.localScale = Vector3.one * (0.58f + (i % 2) * 0.16f);
                DestroyImmediateSafe(lobe.GetComponent<Collider>());
                lobe.GetComponent<Renderer>().sharedMaterial = (i % 2 == 0) ? primaryMat : secondaryMat;
            }

            Light pulseLight = AddPointLight(visualRoot.transform, "Cyst_Pulse_Light", new Vector3(0f, 0.5f, 0f),
                glowColor, 3.2f, 8.5f);

            TutorialCystTarget target = cystGo.AddComponent<TutorialCystTarget>();
            target.kind = kind;
            target.maxHealth = maxHealth;
            target.linkedBarrier = linkedBarrier;
            target.visualRoot = visualRoot.transform;
            target.pulseLight = pulseLight;
            target.baseGlowColor = glowColor;

            EditorUtility.SetDirty(cystGo);
            return target;
        }

        private static TutorialGateBarrier BuildTutorialGateBarrier(
            Transform parent,
            Vector3 worldPos,
            TutorialMaterials mats,
            TutorialMeshes meshes,
            Collider doorTriggerCollider)
        {
            GameObject barrierGo = new GameObject("Tutorial_Gate_Barrier");
            barrierGo.transform.SetParent(parent, false);
            barrierGo.transform.position = worldPos;

            BoxCollider blockingCol = barrierGo.AddComponent<BoxCollider>();
            blockingCol.isTrigger = false;
            blockingCol.center = new Vector3(0f, 1.8f, 0f);
            blockingCol.size = new Vector3(5.8f, 3.6f, 0.9f);

            CreateSolidBox(barrierGo.transform, "Gate_Arch_Pillar_West", new Vector3(-2.65f, 1.8f, 0f), new Vector3(0.8f, 3.8f, 1.0f), mats.HornedBone);
            CreateSolidBox(barrierGo.transform, "Gate_Arch_Pillar_East", new Vector3(2.65f, 1.8f, 0f), new Vector3(0.8f, 3.8f, 1.0f), mats.HornedBone);
            CreateSolidBox(barrierGo.transform, "Gate_Arch_Lintel", new Vector3(0f, 3.85f, 0f), new Vector3(6.2f, 0.85f, 1.1f), mats.HornedBone);

            var tendrilVisualList = new List<GameObject>();
            for (int i = 0; i < 8; i++)
            {
                float t = i / 7f;
                float y = Mathf.Lerp(0.45f, 3.35f, t);
                float tilt = (i % 2 == 0 ? 12f : -12f) + (t - 0.5f) * 8f;

                GameObject tendril = CreateMeshObj(barrierGo.transform, $"Barrier_Tendon_Strand_{i:00}", meshes.TendonStrand, mats.VeinTendon);
                tendril.transform.localPosition = new Vector3(0f, y, (i % 2 == 0) ? -0.08f : 0.08f);
                tendril.transform.localRotation = Quaternion.Euler(0f, 0f, 90f + tilt);
                tendril.transform.localScale = new Vector3(0.32f, 2.7f, 0.32f);
                DestroyImmediateSafe(tendril.GetComponent<Collider>());
                tendrilVisualList.Add(tendril);
            }

            for (int j = 0; j < 2; j++)
            {
                GameObject sealNode = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                sealNode.name = $"Barrier_Seal_Node_{j:00}";
                sealNode.transform.SetParent(barrierGo.transform, false);
                sealNode.transform.localPosition = new Vector3(j == 0 ? -0.95f : 0.95f, 1.75f, -0.15f);
                sealNode.transform.localScale = new Vector3(0.62f, 0.72f, 0.52f);
                DestroyImmediateSafe(sealNode.GetComponent<Collider>());
                sealNode.GetComponent<Renderer>().sharedMaterial = mats.Pustule;
                tendrilVisualList.Add(sealNode);
            }

            Light statusLight = AddPointLight(barrierGo.transform, "Barrier_Status_Light", new Vector3(0f, 2.4f, -0.9f),
                new Color(1.0f, 0.25f, 0.18f), 3.8f, 9.0f);

            TutorialGateBarrier barrier = barrierGo.AddComponent<TutorialGateBarrier>();
            barrier.blockingCollider = blockingCol;
            barrier.tendrilVisuals = tendrilVisualList.ToArray();
            barrier.gateStatusLight = statusLight;
            barrier.doorTriggerToEnable = doorTriggerCollider;

            EditorUtility.SetDirty(barrierGo);
            return barrier;
        }

        private static FleshEyeDoorController BuildCardiaExitDoor(Transform parent, Vector3 worldPos, TutorialMaterials mats, GameObject doorPrefab)
        {
            GameObject doorGo = InstantiateDoorPrefab(parent, "Flesh_Eye_Door_CardiaExit", worldPos, Quaternion.Euler(0f, 180f, 0f), mats, doorPrefab);
            return doorGo != null ? doorGo.GetComponent<FleshEyeDoorController>() : null;
        }

        // ------------------------------------------------------------------
        // 3. ATMOSFERA VFX, VAPORES, LUZES CAUSTICAS & MEMBRANAS PULSANTES
        // ------------------------------------------------------------------
        private static void BuildAtmosphereAndLights(
            Transform root,
            TutorialMaterials mats,
            MeshRenderer lakeRenderer,
            List<Renderer> waterfallRenderers)
        {
            GameObject grp = CreateSubGroup(root, "03_Cardia_Atmosphere_And_Lights", Vector3.zero);

            // Controlador de movimento do lago e cachoeiras
            if (lakeRenderer != null)
            {
                AbyssAcidMotionController motion = lakeRenderer.GetComponent<AbyssAcidMotionController>();
                if (motion == null) motion = lakeRenderer.gameObject.AddComponent<AbyssAcidMotionController>();
                motion.SetTargets(lakeRenderer, waterfallRenderers);
                EditorUtility.SetDirty(motion);
            }

            // Campos de vapor toxico e goticulas ferventes
            GameObject vaporGroup = CreateSubGroup(grp.transform, "Cardia_Toxic_Vapor_Fields", Vector3.zero);
            if (mats.AcidVapor != null)
            {
                SpawnVaporZone(vaporGroup.transform, "Vapor_Cardia_LakeMist",
                    Origin + new Vector3(0f, 1.0f, 4f), new Vector3(108f, 1.5f, 128f),
                    VaporZonePreset.LowerArenaLakeMist, mats.AcidVapor);

                SpawnVaporZone(vaporGroup.transform, "Vapor_Cardia_Waterfall_WestSpray",
                    Origin + new Vector3(-53.0f, 2.2f, -14.0f), new Vector3(9f, 5.0f, 12f),
                    VaporZonePreset.WaterfallSpray, mats.AcidVapor);

                SpawnVaporZone(vaporGroup.transform, "Vapor_Cardia_Waterfall_EastSpray",
                    Origin + new Vector3(53.0f, 2.2f, 30.0f), new Vector3(9f, 5.0f, 12f),
                    VaporZonePreset.WaterfallSpray, mats.AcidVapor);
            }

            if (mats.AcidDroplet != null)
            {
                SpawnVaporZone(vaporGroup.transform, "Droplets_Cardia_LowerLake_Surface",
                    Origin + new Vector3(0f, 0.35f, 4f), new Vector3(96f, 0.4f, 120f),
                    VaporZonePreset.BoilingAcidDroplets, mats.AcidDroplet);
            }

            // Luzes Causticas Subterraneas + Luzes Viscerais (paridade com StomachAbyssSetup)
            GameObject causticGroup = CreateSubGroup(grp.transform, "Cardia_Caustic_Underglow_Lights", Vector3.zero);
            var causticLights = new List<Light>
            {
                SpawnWorldPointLight(causticGroup.transform, "Light_Cardia_AcidGlow_SouthBridge",
                    Origin + new Vector3(0f, 2.2f, -33f), new Color(0.42f, 1.0f, 0.22f), 26f, 34f),
                SpawnWorldPointLight(causticGroup.transform, "Light_Cardia_AcidLake_UnderVeinBridge",
                    Origin + new Vector3(0f, 1.5f, 0.5f), new Color(0.36f, 1.0f, 0.18f), 32f, 32f),
                SpawnWorldPointLight(causticGroup.transform, "CausticBounce_Cardia_VeinBridgeUnderglow",
                    Origin + new Vector3(0f, 2.8f, 0.5f), new Color(0.36f, 1.0f, 0.20f), 34f, 36f),
                SpawnWorldPointLight(causticGroup.transform, "CausticBounce_Cardia_SkullWest",
                    Origin + new Vector3(-34f, 4.5f, 2.0f), new Color(0.32f, 1.0f, 0.24f), 32f, 34f),
                SpawnWorldPointLight(causticGroup.transform, "Light_Cardia_GiantSkull_AcidMaw",
                    Origin + new Vector3(-31f, 7.5f, 3.5f), new Color(0.38f, 1.0f, 0.20f), 48f, 38f),
                SpawnWorldPointLight(causticGroup.transform, "Light_Cardia_HornedColossus_AcidOrbs",
                    Origin + new Vector3(0f, 19.5f, 67.5f), new Color(0.42f, 1.0f, 0.22f), 64f, 44f)
            };

            AcidCausticUnderglow causticCtrl = causticGroup.AddComponent<AcidCausticUnderglow>();
            causticCtrl.SetTargetLights(causticLights);
            EditorUtility.SetDirty(causticCtrl);

            // Luzes Viscerais de Arenas, Monumentos e Abobada Membranosa
            GameObject visceralLights = CreateSubGroup(grp.transform, "Cardia_Visceral_And_Vault_Lights", Vector3.zero);
            SpawnWorldPointLight(visceralLights.transform, "Light_Cardia_OssuaryEntry_Warm",
                Origin + new Vector3(0f, 9.5f, -48f), new Color(1.0f, 0.58f, 0.34f), 36f, 34f);
            SpawnWorldPointLight(visceralLights.transform, "Light_Cardia_VeinBridge_CrimsonCenter",
                Origin + new Vector3(0f, 9.5f, 0.5f), new Color(1.0f, 0.28f, 0.22f), 42f, 32f);
            SpawnWorldPointLight(visceralLights.transform, "Light_Cardia_CentralArena_WarmTorch",
                Origin + new Vector3(0f, 11.5f, 18f), new Color(1.0f, 0.62f, 0.34f), 45f, 38f);
            SpawnWorldPointLight(visceralLights.transform, "Light_Cardia_EastOverlook_Warm",
                Origin + new Vector3(34f, 10.5f, 15f), new Color(1.0f, 0.56f, 0.32f), 32f, 30f);
            SpawnWorldPointLight(visceralLights.transform, "Light_Cardia_WestBastion_Warm",
                Origin + new Vector3(-38f, 9.5f, 1.5f), new Color(1.0f, 0.52f, 0.30f), 32f, 30f);
            SpawnWorldPointLight(visceralLights.transform, "Light_Cardia_BoneWing_CrimsonGlow",
                Origin + new Vector3(31f, 7.5f, -5.5f), new Color(1.0f, 0.38f, 0.24f), 42f, 38f);
            SpawnWorldPointLight(visceralLights.transform, "Light_Cardia_NorthAltar_WarmCrown",
                Origin + new Vector3(0f, 13.0f, 56f), new Color(1.0f, 0.66f, 0.36f), 44f, 38f);
            SpawnWorldPointLight(visceralLights.transform, "Light_Cardia_PortalExit_CyanGlow",
                Origin + new Vector3(0f, 9.5f, 63.0f), new Color(0.35f, 1.0f, 0.88f), 36f, 26f);

            Light ceilingMemSouth = SpawnWorldPointLight(visceralLights.transform, "Light_Cardia_CeilingMembrane_South",
                Origin + new Vector3(0f, 38f, -18f), new Color(1.0f, 0.44f, 0.18f), 110f, 75f);
            Light ceilingMemNorth = SpawnWorldPointLight(visceralLights.transform, "Light_Cardia_CeilingMembrane_North",
                Origin + new Vector3(0f, 38f, 36f), new Color(1.0f, 0.44f, 0.18f), 110f, 75f);

            // Pulsacao nos alveolos retroiluminados da abobada e paredes dentro da Camara da Cardia
            MeshRenderer[] chamberRenderers = root.GetComponentsInChildren<MeshRenderer>(true);
            int podIndex = 0;
            for (int i = 0; i < chamberRenderers.Length; i++)
            {
                MeshRenderer mr = chamberRenderers[i];
                if (mr == null) continue;

                bool isMembraneMat = mr.sharedMaterial != null
                    && mr.sharedMaterial.name.IndexOf("MembraneBacklit", StringComparison.OrdinalIgnoreCase) >= 0;
                bool isAlveoliObj = mr.gameObject.name.IndexOf("Alveoli", StringComparison.OrdinalIgnoreCase) >= 0;
                if (!isMembraneMat && !isAlveoliObj) continue;

                MembranePodPulse pulse = mr.GetComponent<MembranePodPulse>();
                if (pulse == null) pulse = mr.gameObject.AddComponent<MembranePodPulse>();

                var linked = new List<Light>();
                if (mr.gameObject.name.IndexOf("Ceiling_Backlit_Alveoli", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    if (ceilingMemSouth != null) linked.Add(ceilingMemSouth);
                    if (ceilingMemNorth != null) linked.Add(ceilingMemNorth);
                }

                float staggeredPhase = (podIndex * 0.23f) % 1f;
                pulse.Configure(mr, linked, staggeredPhase);
                EditorUtility.SetDirty(pulse);
                podIndex++;
            }
        }

        private static void SpawnVaporZone(
            Transform parent,
            string name,
            Vector3 worldPos,
            Vector3 boxSize,
            VaporZonePreset preset,
            Material mat)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = worldPos;

            go.AddComponent<ParticleSystem>();
            AcidToxicVaporField field = go.AddComponent<AcidToxicVaporField>();
            field.Configure(preset, boxSize, mat);
            EditorUtility.SetDirty(go);
        }

        // ------------------------------------------------------------------
        // 4. CONEXOES COM OSSEARIO SUL & SEGUNDA APARICAO DE VIRGIL NO VIADUTO
        // ------------------------------------------------------------------
        private static void BuildOssuaryAndViaductConnections(
            Transform root,
            TutorialMaterials mats,
            GameObject doorPrefab,
            GameObject virusPrefab,
            FleshEyeDoorController cardiaExitDoor)
        {
            GameObject connGroup = CreateSubGroup(root, ConnectionsGroupName, Vector3.zero);

            GameObject arrivalDoorGo = InstantiateDoorPrefab(
                connGroup.transform,
                "Flesh_Eye_Door_OssuaryArrival",
                new Vector3(-0.9f, 2.0f, -54.0f),
                Quaternion.identity,
                mats,
                doorPrefab);

            FleshEyeDoorController arrivalCtrl = arrivalDoorGo != null ? arrivalDoorGo.GetComponent<FleshEyeDoorController>() : null;
            if (arrivalDoorGo != null)
            {
                DoorTransitionTrigger arrivalTrig = arrivalDoorGo.GetComponentInChildren<DoorTransitionTrigger>(true);
                if (arrivalTrig != null)
                {
                    arrivalTrig.enabled = false;
                    Collider col = arrivalTrig.GetComponent<Collider>();
                    if (col != null) col.enabled = false;
                }
            }

            if (cardiaExitDoor != null)
            {
                DoorTransitionTrigger exitTrig = EnsureDoorTrigger(cardiaExitDoor.gameObject);
                exitTrig.mode = TransitionMode.TeleportTargetInScene;
                exitTrig.destinationDoor = arrivalCtrl;
                exitTrig.spitLandingDistance = 4.2f;
                EditorUtility.SetDirty(exitTrig);
            }

            GameObject checkpointGo = new GameObject("Cardia_Ossuary_Arrival_Checkpoint");
            checkpointGo.transform.SetParent(connGroup.transform, false);
            checkpointGo.transform.position = OssuaryCheckpointPos;

            BoxCollider cpCol = checkpointGo.AddComponent<BoxCollider>();
            cpCol.isTrigger = true;
            cpCol.size = new Vector3(6f, 4f, 6f);

            CardiaArrivalCheckpoint checkpoint = checkpointGo.AddComponent<CardiaArrivalCheckpoint>();
            checkpoint.respawnPoint = checkpointGo.transform;

            InstantiateVirgilNpc(
                connGroup.transform,
                "Virgil_NPC_Viaduct",
                new Vector3(18.6f, 65.53f, 204.2f),
                Quaternion.Euler(0f, 145f, 0f),
                VirgilAppearanceRole.CathedralBridge,
                2.6f,
                mats.VirgilNpc,
                virusPrefab);
        }

        // ------------------------------------------------------------------
        // 5. PLAYER & CAMERA
        // ------------------------------------------------------------------
        private static void ConfigurePlayerAndCamera()
        {
            GameObject player = GameObject.Find("Test_Player (1)");
            if (player == null) player = GameObject.FindGameObjectWithTag("Player");
            if (player == null) player = GameObject.Find("Test_Player");

            if (player != null)
            {
                CharacterController cc = player.GetComponent<CharacterController>();
                bool prevCcEnabled = cc != null && cc.enabled;
                if (cc != null) cc.enabled = false;

                Undo.RecordObject(player.transform, "Move Player to Cardia Spawn");
                player.transform.position = PlayerSpawnPos;
                player.transform.rotation = Quaternion.identity;

                if (cc != null) cc.enabled = prevCcEnabled;

                SimplePlayerController spc = player.GetComponent<SimplePlayerController>();
                if (spc != null)
                {
                    Undo.RecordObject(spc, "Set Cardia Respawn Position");
                    spc.respawnPosition = PlayerSpawnPos;
                    EditorUtility.SetDirty(spc);
                }

                EditorUtility.SetDirty(player);
            }
            else
            {
                Debug.LogWarning("[CardiaTutorialSetup] Player 'Test_Player (1)' nao encontrado na cena.");
            }

            GameObject camGo = GameObject.Find("Main Camera");
            if (camGo == null && Camera.main != null) camGo = Camera.main.gameObject;

            if (camGo != null)
            {
                IsometricCameraFollow iso = camGo.GetComponent<IsometricCameraFollow>();
                if (iso != null)
                {
                    Undo.RecordObject(iso, "Target Player in Cardia");
                    if (player != null) iso.target = player.transform;
                    iso.SnapToTargetImmediate();
                    EditorUtility.SetDirty(iso);
                }

                BG3WallCutoutController bg3 = camGo.GetComponent<BG3WallCutoutController>();
                if (bg3 != null && player != null)
                {
                    bg3.playerTarget = player.transform;
                    bg3.UpdateShaderGlobals();
                    EditorUtility.SetDirty(bg3);
                }

                EditorUtility.SetDirty(camGo);
            }
        }

        // ------------------------------------------------------------------
        // HELPERS DE GLB (IDENTICO AO STOMACHABYSSSETUP)
        // ------------------------------------------------------------------
        private static void PlaceFloatingIslet(
            Transform parent,
            string glbName,
            string objName,
            Vector3 worldPos,
            float yawDeg,
            float uniformScale,
            Dictionary<string, Material> mats)
        {
            PlaceGlb(parent, glbName, objName, worldPos, Quaternion.Euler(0f, yawDeg, 0f), Vector3.one * uniformScale, mats);
        }

        private static GameObject PlaceGlb(
            Transform parent,
            string glbBaseName,
            string instanceName,
            Vector3 worldPos,
            Quaternion worldRot,
            Vector3 worldScale,
            Dictionary<string, Material> mats,
            bool addColliders = true)
        {
            string glbPath = $"{ModelsFolder}/{glbBaseName}.glb";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(glbPath);
            if (prefab == null)
            {
                Debug.LogError($"[CardiaTutorialSetup] GLB nao encontrado: {glbPath}");
                return null;
            }

            GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.name = instanceName;
            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

            go.transform.SetParent(parent, false);
            go.transform.position = worldPos;
            go.transform.rotation = worldRot;
            go.transform.localScale = worldScale;

            MeshRenderer[] renderers = go.GetComponentsInChildren<MeshRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                MeshRenderer mr = renderers[i];
                if (mr == null) continue;

                Material[] currentMats = mr.sharedMaterials;
                bool swapped = false;
                for (int m = 0; m < currentMats.Length; m++)
                {
                    Material cur = currentMats[m];
                    if (cur == null) continue;

                    string key = NormalizeMaterialKey(cur.name, glbBaseName, mr.gameObject.name);
                    if (mats != null && mats.TryGetValue(key, out Material replacement) && replacement != null)
                    {
                        currentMats[m] = replacement;
                        swapped = true;
                    }
                }
                if (swapped)
                {
                    mr.sharedMaterials = currentMats;
                }

                if (mr.gameObject.name == "Portal_Vortex_Core" ||
                    mr.gameObject.name == "Abyss_Acid_Lake" ||
                    mr.gameObject.name.Contains("Glow") ||
                    mr.gameObject.name == "Ceiling_Backlit_Alveoli")
                {
                    mr.shadowCastingMode = ShadowCastingMode.Off;
                }

                if (addColliders && !ColliderSkipNodes.Contains(mr.gameObject.name))
                {
                    MeshFilter mf = mr.GetComponent<MeshFilter>();
                    if (mf != null && mf.sharedMesh != null && mr.GetComponent<MeshCollider>() == null)
                    {
                        MeshCollider mc = mr.gameObject.AddComponent<MeshCollider>();
                        mc.sharedMesh = mf.sharedMesh;
                    }
                }
            }

            return go;
        }

        private static string NormalizeMaterialKey(string curMatName, string glbBaseName, string nodeName)
        {
            if (curMatName.Contains("GastricAcid"))
            {
                if (glbBaseName == "Abyss_Acid_Lake") return "Mat_Abyss_AcidLake";
                if (glbBaseName == "Waterfall_AcidBlood") return "Mat_Abyss_AcidWaterfall";
                return "Mat_Abyss_AcidGlow";
            }

            string[] knownKeys =
            {
                "Mat_FleshFloor",
                "Mat_FleshWall",
                "Mat_DigestedCalcified",
                "Mat_Cartilage",
                "Mat_OssifiedStone",
                "Mat_VeinTendon",
                "Mat_Teeth",
                "Mat_HornedBone",
                "Mat_MembraneBacklit",
                "Mat_PortalVortex",
                "Mat_BloodWaves"
            };

            for (int i = 0; i < knownKeys.Length; i++)
            {
                if (curMatName.Contains(knownKeys[i]))
                {
                    return knownKeys[i];
                }
            }

            return curMatName;
        }

        // ------------------------------------------------------------------
        // HELPERS DE VIRGIL & PORTA DE CARNE
        // ------------------------------------------------------------------
        private static GameObject InstantiateVirgilNpc(
            Transform parent,
            string name,
            Vector3 worldPos,
            Quaternion worldRot,
            VirgilAppearanceRole role,
            float lightIntensity,
            Material virgilMat,
            GameObject virusPrefab)
        {
            GameObject npcGo;
            if (virusPrefab != null)
            {
                npcGo = (GameObject)PrefabUtility.InstantiatePrefab(virusPrefab, parent);
                if (PrefabUtility.IsPartOfPrefabInstance(npcGo))
                {
                    PrefabUtility.UnpackPrefabInstance(npcGo, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                }
            }
            else
            {
                npcGo = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                npcGo.transform.SetParent(parent, false);
            }

            npcGo.name = name;
            npcGo.transform.position = worldPos;
            npcGo.transform.rotation = worldRot;
            npcGo.transform.localScale = Vector3.one * 1.18f;

            SetTagRecursive(npcGo.transform, "Untagged");
            StripPlayerComponents(npcGo);

            Transform visualRoot = npcGo.transform.Find("Virus_Model");
            if (visualRoot == null) visualRoot = npcGo.transform;
            visualRoot.localRotation = Quaternion.Euler(10f, 0f, -5f);

            Renderer[] renderers = npcGo.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null && virgilMat != null)
                {
                    renderers[i].sharedMaterial = virgilMat;
                }
            }

            Light biolum = npcGo.GetComponentInChildren<Light>(true);
            if (biolum == null)
            {
                biolum = AddPointLight(npcGo.transform, "Virgil_Biolum_Light", new Vector3(0f, 1.35f, 0.25f),
                    new Color(1.0f, 0.72f, 0.30f), lightIntensity, 8f);
            }
            else
            {
                biolum.type = LightType.Point;
                biolum.color = new Color(1.0f, 0.72f, 0.30f);
                biolum.range = 8f;
                biolum.intensity = lightIntensity;
                biolum.shadows = LightShadows.None;
            }

            CapsuleCollider bodyCol = npcGo.AddComponent<CapsuleCollider>();
            bodyCol.center = new Vector3(0f, 0.85f, 0f);
            bodyCol.radius = 0.48f;
            bodyCol.height = 1.7f;

            VirgilNpcController ctrl = npcGo.AddComponent<VirgilNpcController>();
            ctrl.role = role;
            ctrl.npcName = "VESTÍGIO";
            ctrl.npcTitle = "O Primeiro Quebrado • Cepa Ancestral";
            ctrl.interactRadius = 4.0f;
            ctrl.visualRoot = visualRoot;
            ctrl.biolumLight = biolum;

            EditorUtility.SetDirty(npcGo);
            return npcGo;
        }

        private static void StripPlayerComponents(GameObject go)
        {
            DestroyImmediateSafe(go.GetComponent<SimplePlayerController>());
            DestroyImmediateSafe(go.GetComponent<CharacterController>());
            DestroyImmediateSafe(go.GetComponent<CrossRazorGreatswordController>());
            DestroyImmediateSafe(go.GetComponent<AudioListener>());

            Collider[] cols = go.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < cols.Length; i++)
            {
                DestroyImmediateSafe(cols[i]);
            }

            Rigidbody[] rbs = go.GetComponentsInChildren<Rigidbody>(true);
            for (int i = 0; i < rbs.Length; i++)
            {
                DestroyImmediateSafe(rbs[i]);
            }
        }

        private static GameObject InstantiateDoorPrefab(
            Transform parent,
            string name,
            Vector3 worldPos,
            Quaternion worldRot,
            TutorialMaterials mats,
            GameObject doorPrefab)
        {
            if (doorPrefab == null)
            {
                Debug.LogWarning($"[CardiaTutorialSetup] Prefab nao encontrado em {DoorPrefabPath} para {name}.");
                return null;
            }

            GameObject doorGo = (GameObject)PrefabUtility.InstantiatePrefab(doorPrefab, parent);
            doorGo.name = name;
            doorGo.transform.position = worldPos;
            doorGo.transform.rotation = worldRot;

            FleshEyeDoorController ctrl = doorGo.GetComponent<FleshEyeDoorController>();
            if (ctrl != null)
            {
                ctrl.BindParts();
                if (mats.PixelAcid != null) ctrl.spitParticleMaterial = mats.PixelAcid;
            }

            EditorUtility.SetDirty(doorGo);
            return doorGo;
        }

        private static DoorTransitionTrigger EnsureDoorTrigger(GameObject doorRoot)
        {
            DoorTransitionTrigger existing = doorRoot.GetComponentInChildren<DoorTransitionTrigger>(true);
            if (existing != null) return existing;

            Transform t = doorRoot.transform.Find("Door_Transition_Trigger");
            GameObject trigGo = t != null ? t.gameObject : new GameObject("Door_Transition_Trigger");
            if (t == null)
            {
                trigGo.transform.SetParent(doorRoot.transform, false);
                trigGo.transform.localPosition = new Vector3(0f, 0.85f, 0.30f);
            }

            BoxCollider col = trigGo.GetComponent<BoxCollider>();
            if (col == null) col = trigGo.AddComponent<BoxCollider>();
            col.isTrigger = true;
            col.size = new Vector3(2.2f, 1.8f, 1.6f);

            return trigGo.AddComponent<DoorTransitionTrigger>();
        }

        private static Collider GetDoorTriggerCollider(GameObject doorRoot)
        {
            DoorTransitionTrigger trig = doorRoot.GetComponentInChildren<DoorTransitionTrigger>(true);
            if (trig != null)
            {
                Collider c = trig.GetComponent<Collider>();
                if (c != null) return c;
            }

            Transform t = doorRoot.transform.Find("Door_Transition_Trigger");
            return t != null ? t.GetComponent<Collider>() : null;
        }

        // ------------------------------------------------------------------
        // MALHAS PROCEDURAIS (ESFINCTER, DENTES, TENDOES DA BARREIRA, CISTOS)
        // ------------------------------------------------------------------
        private static TutorialMeshes CreateAndSaveTutorialMeshes()
        {
            return new TutorialMeshes
            {
                SphincterTorus = SaveOrUpdateMeshAsset(BuildSphincterTorusMesh(), TutorialMeshesFolder + "/Mesh_Cardia_SphincterTorus.asset"),
                ToothFang = SaveOrUpdateMeshAsset(BuildToothFangMesh(), TutorialMeshesFolder + "/Mesh_Cardia_ToothFang.asset"),
                TendonStrand = SaveOrUpdateMeshAsset(BuildTendonStrandMesh(), TutorialMeshesFolder + "/Mesh_Cardia_TendonStrand.asset"),
                CystCluster = SaveOrUpdateMeshAsset(BuildCystClusterMesh(), TutorialMeshesFolder + "/Mesh_Cardia_CystCluster.asset")
            };
        }

        private static Mesh SaveOrUpdateMeshAsset(Mesh generated, string assetPath)
        {
            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);
            if (existing != null)
            {
                existing.Clear();
                existing.indexFormat = generated.indexFormat;
                existing.vertices = generated.vertices;
                existing.normals = generated.normals;
                existing.uv = generated.uv;
                existing.colors = generated.colors;
                existing.triangles = generated.triangles;
                existing.RecalculateBounds();
                EditorUtility.SetDirty(existing);
                UnityEngine.Object.DestroyImmediate(generated);
                return existing;
            }

            AssetDatabase.CreateAsset(generated, assetPath);
            return AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);
        }

        private static Mesh BuildSphincterTorusMesh()
        {
            const int majorSegs = 24;
            const int minorSegs = 12;
            const float majorRadius = 3.25f;
            const float minorRadius = 1.05f;

            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var cols = new List<Color>();
            var tris = new List<int>();

            for (int i = 0; i <= majorSegs; i++)
            {
                float u = i / (float)majorSegs;
                float theta = u * Mathf.PI * 2f;
                Vector3 ringCenter = new Vector3(Mathf.Cos(theta) * majorRadius, 0f, Mathf.Sin(theta) * majorRadius);
                Vector3 radial = new Vector3(Mathf.Cos(theta), 0f, Mathf.Sin(theta));

                for (int j = 0; j <= minorSegs; j++)
                {
                    float v = j / (float)minorSegs;
                    float phi = v * Mathf.PI * 2f;
                    float fold = 1f + 0.16f * Mathf.Sin(theta * 6f) + 0.10f * Mathf.Cos(phi * 3f);
                    float r = minorRadius * fold;

                    Vector3 offset = radial * (Mathf.Cos(phi) * r) + Vector3.up * (Mathf.Sin(phi) * r * 0.85f);
                    verts.Add(ringCenter + offset);
                    uvs.Add(new Vector2(u * 4f, v * 2f));
                    float shade = Mathf.Lerp(0.55f, 0.92f, 0.5f + 0.5f * Mathf.Sin(phi));
                    cols.Add(new Color(shade, shade * 0.72f, shade * 0.70f, 1f));
                }
            }

            int stride = minorSegs + 1;
            for (int i = 0; i < majorSegs; i++)
            {
                for (int j = 0; j < minorSegs; j++)
                {
                    int a = i * stride + j;
                    int b = (i + 1) * stride + j;
                    int c = (i + 1) * stride + (j + 1);
                    int d = i * stride + (j + 1);

                    tris.Add(a); tris.Add(c); tris.Add(b);
                    tris.Add(a); tris.Add(d); tris.Add(c);
                }
            }

            Mesh m = new Mesh { name = "Mesh_Cardia_SphincterTorus" };
            m.SetVertices(verts);
            m.SetUVs(0, uvs);
            m.SetColors(cols);
            m.SetTriangles(tris, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        private static Mesh BuildToothFangMesh()
        {
            const int radialSegs = 8;
            const int heightSegs = 5;

            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var cols = new List<Color>();
            var tris = new List<int>();

            for (int y = 0; y <= heightSegs; y++)
            {
                float t = y / (float)heightSegs;
                float r = Mathf.Lerp(0.45f, 0.03f, Mathf.Pow(t, 0.8f));
                float bendZ = Mathf.Pow(t, 1.6f) * 0.35f;

                for (int i = 0; i <= radialSegs; i++)
                {
                    float u = i / (float)radialSegs;
                    float a = u * Mathf.PI * 2f;
                    verts.Add(new Vector3(Mathf.Cos(a) * r, t * 1.4f, Mathf.Sin(a) * r + bendZ));
                    uvs.Add(new Vector2(u, t));
                    cols.Add(Color.Lerp(new Color(0.75f, 0.52f, 0.45f, 1f), new Color(1f, 0.96f, 0.88f, 1f), t));
                }
            }

            int stride = radialSegs + 1;
            for (int y = 0; y < heightSegs; y++)
            {
                for (int i = 0; i < radialSegs; i++)
                {
                    int a = y * stride + i;
                    int b = y * stride + (i + 1);
                    int c = (y + 1) * stride + (i + 1);
                    int d = (y + 1) * stride + i;
                    tris.Add(a); tris.Add(c); tris.Add(b);
                    tris.Add(a); tris.Add(d); tris.Add(c);
                }
            }

            Mesh m = new Mesh { name = "Mesh_Cardia_ToothFang" };
            m.SetVertices(verts);
            m.SetUVs(0, uvs);
            m.SetColors(cols);
            m.SetTriangles(tris, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        private static Mesh BuildTendonStrandMesh()
        {
            const int heightSegs = 8;
            const int radialSegs = 8;

            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var cols = new List<Color>();
            var tris = new List<int>();

            for (int y = 0; y <= heightSegs; y++)
            {
                float t = y / (float)heightSegs;
                float pinch = 1.0f - 0.55f * Mathf.Sin(t * Mathf.PI);
                float r = 0.36f * pinch;

                for (int i = 0; i <= radialSegs; i++)
                {
                    float u = i / (float)radialSegs;
                    float a = u * Mathf.PI * 2f;
                    verts.Add(new Vector3(Mathf.Cos(a) * r, (t - 0.5f) * 2.0f, Mathf.Sin(a) * r));
                    uvs.Add(new Vector2(u, t * 2f));
                    cols.Add(new Color(0.90f, 0.52f, 0.48f, 1f));
                }
            }

            int stride = radialSegs + 1;
            for (int y = 0; y < heightSegs; y++)
            {
                for (int i = 0; i < radialSegs; i++)
                {
                    int a = y * stride + i;
                    int b = y * stride + (i + 1);
                    int c = (y + 1) * stride + (i + 1);
                    int d = (y + 1) * stride + i;
                    tris.Add(a); tris.Add(c); tris.Add(b);
                    tris.Add(a); tris.Add(d); tris.Add(c);
                }
            }

            Mesh m = new Mesh { name = "Mesh_Cardia_TendonStrand" };
            m.SetVertices(verts);
            m.SetUVs(0, uvs);
            m.SetColors(cols);
            m.SetTriangles(tris, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        private static Mesh BuildCystClusterMesh()
        {
            GameObject temp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Mesh src = temp.GetComponent<MeshFilter>().sharedMesh;
            Mesh m = UnityEngine.Object.Instantiate(src);
            m.name = "Mesh_Cardia_CystCluster";
            DestroyImmediateSafe(temp);

            Vector3[] verts = m.vertices;
            Color[] cols = new Color[verts.Length];
            for (int i = 0; i < verts.Length; i++)
            {
                Vector3 v = verts[i];
                float bump = 1f + 0.18f * Mathf.Sin(v.x * 7.2f + v.y * 5.4f) * Mathf.Cos(v.z * 6.8f);
                verts[i] = v * (0.95f * bump);
                cols[i] = new Color(0.95f, 0.85f, 0.82f, 1f);
            }

            m.vertices = verts;
            m.colors = cols;
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        // ------------------------------------------------------------------
        // UTILITARIOS DE CENA
        // ------------------------------------------------------------------
        private static GameObject CreateSubGroup(Transform parent, string name, Vector3 localPos)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            return go;
        }

        private static GameObject CreateSolidBox(Transform parent, string name, Vector3 localCenter, Vector3 size, Material mat)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localCenter;
            go.transform.localScale = size;
            if (mat != null) go.GetComponent<Renderer>().sharedMaterial = mat;
            return go;
        }

        private static GameObject CreateMeshObj(Transform parent, string name, Mesh mesh, Material mat)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            MeshFilter mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            MeshRenderer mr = go.AddComponent<MeshRenderer>();
            if (mat != null) mr.sharedMaterial = mat;
            return go;
        }

        private static Light AddPointLight(Transform parent, string name, Vector3 localPos, Color color, float intensity, float range)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            Light l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = color;
            l.intensity = intensity;
            l.range = range;
            l.shadows = LightShadows.None;
            return l;
        }

        private static Light SpawnWorldPointLight(Transform parent, string name, Vector3 worldPos, Color color, float intensity, float range)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = worldPos;
            Light l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = color;
            l.intensity = intensity;
            l.range = range;
            l.shadows = LightShadows.None;
            EditorUtility.SetDirty(go);
            return l;
        }

        private static void SetTagRecursive(Transform root, string tag)
        {
            root.gameObject.tag = tag;
            for (int i = 0; i < root.childCount; i++)
            {
                SetTagRecursive(root.GetChild(i), tag);
            }
        }

        private static void DestroyImmediateSafe(UnityEngine.Object obj)
        {
            if (obj != null)
            {
                UnityEngine.Object.DestroyImmediate(obj);
            }
        }

        private static void EnsureFolderHierarchy(string assetFolderPath)
        {
            string[] parts = assetFolderPath.Split('/');
            string cur = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = cur + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(cur, parts[i]);
                }
                cur = next;
            }
        }
    }
}
