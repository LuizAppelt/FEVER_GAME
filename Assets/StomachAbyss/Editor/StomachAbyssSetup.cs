#pragma warning disable 0618
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace MapaEstomago.Editor
{
    public static class StomachAbyssSetup
    {
        private const string BaseFolder = "Assets/StomachAbyss";
        private const string ModelsFolder = BaseFolder + "/Models";
        private const string MaterialsFolder = BaseFolder + "/Materials";
        private const string AbyssTexturesFolder = BaseFolder + "/Textures";
        private const string PilotTexturesFolder = "Assets/PilotChamber/Textures";
        private const string ScenePath = "Assets/Scenes/Stomach_Abyss.unity";

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

        [MenuItem("Estomago/Novo Mapa/Construir Cena Modular Stomach_Abyss", priority = 20)]
        public static void BuildStomachAbyssScene()
        {
            AssetDatabase.Refresh();
            EnsureFolder(BaseFolder);
            EnsureFolder(MaterialsFolder);
            EnsureFolder("Assets/Scenes");

            ConfigurePixelTextures();
            ReimportAllGlbModels();

            Dictionary<string, Material> mats = CreateOrUpdateAbyssMaterials();

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            GameObject grpAcid = CreateGroup("01_Acid_Lake_And_Falls");
            GameObject grpArenas = CreateGroup("02_Arenas_Combat");
            GameObject grpBridges = CreateGroup("03_Bridges_And_Stairs");
            GameObject grpMonuments = CreateGroup("04_Colossal_Monuments");
            GameObject grpIslets = CreateGroup("05_Floating_Islets");
            GameObject grpCavern = CreateGroup("06_Stomach_Cavern_Vault");
            GameObject grpAtmosphere = CreateGroup("07_Lighting_And_Atmosphere");

            // 1. Lago de Acido e Cachoeiras Viscerais (zero lava)
            PlaceGlb(grpAcid.transform, "Abyss_Acid_Lake", "Abyss_Acid_Lake",
                Vector3.zero, Quaternion.identity, Vector3.one, mats, addColliders: false);

            PlaceGlb(grpAcid.transform, "Waterfall_AcidBlood", "Waterfall_Acid_West",
                new Vector3(-56.5f, 0f, -14f), Quaternion.Euler(0f, 76f, 0f), new Vector3(1.30f, 1.42f, 1.30f), mats);

            PlaceGlb(grpAcid.transform, "Waterfall_AcidBlood", "Waterfall_Acid_East",
                new Vector3(56.5f, 0f, 30f), Quaternion.Euler(0f, -104f, 0f), new Vector3(1.25f, 1.48f, 1.25f), mats);

            // 2. 6 Arenas Amplas Interligadas de Combate (cada uma separada e editavel)
            PlaceGlb(grpArenas.transform, "Arena_South_OssuaryEntry", "Arena_South_OssuaryEntry",
                new Vector3(0f, 0f, -48f), Quaternion.identity, Vector3.one, mats);

            PlaceGlb(grpArenas.transform, "Arena_SouthCentral_Hub", "Arena_SouthCentral_Hub",
                new Vector3(0f, 0f, -16f), Quaternion.identity, Vector3.one, mats);

            PlaceGlb(grpArenas.transform, "Arena_Central_RitualPlateau", "Arena_Central_RitualPlateau",
                new Vector3(0f, 0f, 18f), Quaternion.identity, Vector3.one, mats);

            PlaceGlb(grpArenas.transform, "Arena_East_TendonOverlook", "Arena_East_TendonOverlook",
                new Vector3(28f, 0f, 14f), Quaternion.identity, Vector3.one, mats);

            PlaceGlb(grpArenas.transform, "Arena_West_CraterBastion", "Arena_West_CraterBastion",
                new Vector3(-25f, 0f, 11f), Quaternion.identity, Vector3.one, mats);

            PlaceGlb(grpArenas.transform, "Arena_North_TitanAltar", "Arena_North_TitanAltar",
                new Vector3(0f, 0f, 57f), Quaternion.identity, Vector3.one, mats);

            // 3. 5 Pontes e Escadarias Amplas Conectando as Arenas
            PlaceGlb(grpBridges.transform, "Bridge_OssuaryStairs_South", "Bridge_OssuaryStairs_South",
                new Vector3(0f, 0f, -33.0f), Quaternion.identity, Vector3.one, mats);

            PlaceGlb(grpBridges.transform, "Bridge_VeinTendon_Grand", "Bridge_VeinTendon_Grand",
                new Vector3(0f, 0f, 0.5f), Quaternion.identity, Vector3.one, mats);

            PlaceGlb(grpBridges.transform, "Bridge_PlankSpan_East", "Bridge_PlankSpan_East",
                new Vector3(16.5f, 0f, 14.0f), Quaternion.identity, Vector3.one, mats);

            PlaceGlb(grpBridges.transform, "Bridge_StairSpan_West", "Bridge_StairSpan_West",
                new Vector3(-15.5f, 0f, 11.0f), Quaternion.identity, Vector3.one, mats);

            PlaceGlb(grpBridges.transform, "Bridge_Stairs_North", "Bridge_Stairs_North",
                new Vector3(0f, 0f, 38.0f), Quaternion.identity, Vector3.one, mats);

            // 4. Monumentos Colossais, Circulo Ritual e Portal
            PlaceGlb(grpMonuments.transform, "Prop_RitualCircle_Center", "Prop_RitualCircle_Center",
                new Vector3(0f, 4.62f, 18.0f), Quaternion.identity, Vector3.one, mats);

            PlaceGlb(grpMonuments.transform, "Monument_GothicButtress_South", "Monument_GothicButtress_South",
                new Vector3(0f, 0f, -57.5f), Quaternion.identity, Vector3.one, mats);

            PlaceGlb(grpMonuments.transform, "Monument_GiantSkull_West", "Monument_GiantSkull_West",
                new Vector3(-42.5f, 0f, 2.0f), Quaternion.Euler(0f, 82f, 0f), new Vector3(1.22f, 1.22f, 1.22f), mats);

            PlaceGlb(grpMonuments.transform, "Monument_GiantBoneWing_East", "Monument_GiantBoneWing_East",
                new Vector3(41.5f, 0f, -7.0f), Quaternion.Euler(0f, -96f, 0f), new Vector3(1.24f, 1.24f, 1.24f), mats);

            PlaceGlb(grpMonuments.transform, "Monument_HornedColossus_North", "Monument_HornedColossus_North",
                new Vector3(0f, 2.4f, 72.5f), Quaternion.Euler(0f, 180f, 0f), new Vector3(1.26f, 1.26f, 1.26f), mats);

            PlaceGlb(grpMonuments.transform, "Portal_Exit_Gate", "Portal_Exit_Gate",
                new Vector3(10.8f, 6.2f, 57.5f), Quaternion.Euler(0f, 206f, 0f), new Vector3(1.06f, 1.06f, 1.06f), mats);

            PlaceGlb(grpMonuments.transform, "Monument_SunkenSpire_A", "Spire_Sunken_SW",
                new Vector3(-23.0f, 0f, -31.0f), Quaternion.Euler(0f, 34f, 0f), new Vector3(1.15f, 1.28f, 1.15f), mats);

            PlaceGlb(grpMonuments.transform, "Monument_SunkenSpire_B", "Spire_Sunken_SE",
                new Vector3(21.5f, 0f, -29.0f), Quaternion.Euler(0f, -42f, 0f), new Vector3(1.22f, 1.20f, 1.22f), mats);

            PlaceGlb(grpMonuments.transform, "Monument_SunkenSpire_A", "Spire_Sunken_NW",
                new Vector3(-30.0f, 0f, 38.0f), Quaternion.Euler(0f, 128f, 0f), new Vector3(1.28f, 1.42f, 1.28f), mats);

            PlaceGlb(grpMonuments.transform, "Monument_SunkenSpire_B", "Spire_Sunken_NE",
                new Vector3(27.0f, 0f, 40.0f), Quaternion.Euler(0f, -112f, 0f), new Vector3(1.15f, 1.28f, 1.15f), mats);

            // 5. Ilhotas e Rochas Flutuantes no Lago de Acido (cada uma independente)
            PlaceFloatingIslet(grpIslets.transform, "Prop_FloatingIslet_A", "FloatingIslet_01", new Vector3(-18f, 0f, -14f), 25f, 1.05f, mats);
            PlaceFloatingIslet(grpIslets.transform, "Prop_FloatingIslet_B", "FloatingIslet_02", new Vector3(18f, 0f, -14f), -52f, 1.10f, mats);
            PlaceFloatingIslet(grpIslets.transform, "Prop_FloatingIslet_A", "FloatingIslet_03", new Vector3(-27f, 0f, -43f), 110f, 1.20f, mats);
            PlaceFloatingIslet(grpIslets.transform, "Prop_FloatingIslet_B", "FloatingIslet_04", new Vector3(25f, 0f, -43f), -135f, 1.15f, mats);
            PlaceFloatingIslet(grpIslets.transform, "Prop_FloatingIslet_B", "FloatingIslet_05", new Vector3(-16f, 0f, 32f), 72f, 0.95f, mats);
            PlaceFloatingIslet(grpIslets.transform, "Prop_FloatingIslet_A", "FloatingIslet_06", new Vector3(19f, 0f, 32f), -18f, 1.02f, mats);
            PlaceFloatingIslet(grpIslets.transform, "Prop_FloatingIslet_A", "FloatingIslet_07", new Vector3(36f, 0f, 28f), 154f, 1.28f, mats);
            PlaceFloatingIslet(grpIslets.transform, "Prop_FloatingIslet_B", "FloatingIslet_08", new Vector3(-37f, 0f, 25f), -76f, 1.24f, mats);
            PlaceFloatingIslet(grpIslets.transform, "Prop_FloatingIslet_A", "FloatingIslet_09", new Vector3(-34f, 0f, -19f), 42f, 1.18f, mats);
            PlaceFloatingIslet(grpIslets.transform, "Prop_FloatingIslet_B", "FloatingIslet_10", new Vector3(33f, 0f, -24f), 196f, 1.14f, mats);

            // 6. Caverna Visceral, Arcos de Costelas e Abobada Retroiluminada (separados e alternaveis)
            PlaceGlb(grpCavern.transform, "Cavern_Wall_Long", "Cavern_Wall_West",
                new Vector3(-74f, 0f, 6f), Quaternion.Euler(0f, 90f, 0f), Vector3.one, mats);

            PlaceGlb(grpCavern.transform, "Cavern_Wall_Long", "Cavern_Wall_East",
                new Vector3(74f, 0f, 6f), Quaternion.Euler(0f, -90f, 0f), Vector3.one, mats);

            PlaceGlb(grpCavern.transform, "Cavern_Wall_End", "Cavern_Wall_North",
                new Vector3(0f, 0f, 89f), Quaternion.Euler(0f, 180f, 0f), Vector3.one, mats);

            PlaceGlb(grpCavern.transform, "Cavern_Wall_End", "Cavern_Wall_South",
                new Vector3(0f, 0f, -83f), Quaternion.identity, Vector3.one, mats);

            PlaceGlb(grpCavern.transform, "Cavern_RibArch", "Cavern_RibArch_South",
                new Vector3(0f, 0f, -32f), Quaternion.identity, new Vector3(1.08f, 1.06f, 1.0f), mats);

            PlaceGlb(grpCavern.transform, "Cavern_RibArch", "Cavern_RibArch_Center",
                new Vector3(0f, 0f, 8f), Quaternion.Euler(0f, 4f, 0f), new Vector3(1.08f, 1.06f, 1.0f), mats);

            PlaceGlb(grpCavern.transform, "Cavern_RibArch", "Cavern_RibArch_North",
                new Vector3(0f, 0f, 44f), Quaternion.Euler(0f, -4f, 0f), new Vector3(1.08f, 1.06f, 1.0f), mats);

            PlaceGlb(grpCavern.transform, "Cavern_Membrane_CeilingVault", "Cavern_Membrane_CeilingVault",
                new Vector3(0f, 0f, 4f), Quaternion.identity, Vector3.one, mats, addColliders: false);

            // 7. Iluminacao, Atmosfera e Camera Principal de Visualizacao (sem Player / sem dano)
            ConfigureLightingAtmosphereAndCamera(grpAtmosphere.transform);

            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();

            Debug.Log("<color=#33FF99><b>[StomachAbyssSetup]</b> Cena 'Assets/Scenes/Stomach_Abyss.unity' criada e configurada com todos os objetos modulares separados!</color>");
        }

        private static GameObject CreateGroup(string name)
        {
            return new GameObject(name);
        }

        private static void PlaceFloatingIslet(Transform parent, string glbName, string objName, Vector3 pos, float yawDeg, float uniformScale, Dictionary<string, Material> mats)
        {
            PlaceGlb(parent, glbName, objName, pos, Quaternion.Euler(0f, yawDeg, 0f), Vector3.one * uniformScale, mats);
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
                Debug.LogError($"[StomachAbyssSetup] GLB nao encontrado: {glbPath}");
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

                // Unpack material slots cleanly by checking current sharedMaterial names
                Material[] currentMats = mr.sharedMaterials;
                bool swapped = false;
                for (int m = 0; m < currentMats.Length; m++)
                {
                    Material cur = currentMats[m];
                    if (cur == null) continue;

                    string key = NormalizeMaterialKey(cur.name, glbBaseName, mr.gameObject.name);
                    if (mats.TryGetValue(key, out Material replacement) && replacement != null)
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

        private static Dictionary<string, Material> CreateOrUpdateAbyssMaterials()
        {
            Shader fleshShader = Shader.Find("MapaEstomago/URP/PS1_LivingFlesh");
            Shader acidShader = Shader.Find("MapaEstomago/URP/PS1_GastricAcid");
            if (fleshShader == null) fleshShader = Shader.Find("Universal Render Pipeline/Lit");
            if (acidShader == null) acidShader = fleshShader;

            var dict = new Dictionary<string, Material>();

            dict["Mat_FleshFloor"] = CreateMat("Mat_Abyss_FleshFloor", fleshShader, PilotTexturesFolder, "tex_flesh_floor.png", m =>
            {
                m.SetColor("_BaseColor", Color.white);
                m.SetFloat("_VertexSnap", 190f);
                m.SetFloat("_BreathSpeed", 1.15f);
                m.SetFloat("_BreathAmp", 0.005f);
                m.SetFloat("_SplitAmount", 0.16f);
                m.SetFloat("_Wetness", 0.42f);
                m.SetFloat("_AmbientFloor", 0.34f);
                m.SetColor("_EmissionColor", new Color(0.05f, 0.01f, 0.01f, 1f));
                m.SetFloat("_UseVertexColor", 1f);
                m.SetFloat("_EnableBG3Cutout", 0f);
            });

            dict["Mat_FleshWall"] = CreateMat("Mat_Abyss_FleshWall", fleshShader, PilotTexturesFolder, "tex_flesh_wall.png", m =>
            {
                m.SetColor("_BaseColor", Color.white);
                m.SetFloat("_VertexSnap", 190f);
                m.SetFloat("_BreathSpeed", 1.25f);
                m.SetFloat("_BreathAmp", 0.012f);
                m.SetFloat("_SplitAmount", 0.18f);
                m.SetFloat("_Wetness", 0.46f);
                m.SetFloat("_AmbientFloor", 0.32f);
                m.SetColor("_EmissionColor", new Color(0.06f, 0.01f, 0.01f, 1f));
                m.SetFloat("_UseVertexColor", 1f);
                m.SetFloat("_EnableBG3Cutout", 0f);
            });

            dict["Mat_DigestedCalcified"] = CreateMat("Mat_Abyss_DigestedCalcified", fleshShader, PilotTexturesFolder, "tex_digested_calcified.png", m =>
            {
                m.SetColor("_BaseColor", Color.white);
                m.SetFloat("_VertexSnap", 190f);
                m.SetFloat("_BreathSpeed", 0.4f);
                m.SetFloat("_BreathAmp", 0.003f);
                m.SetFloat("_SplitAmount", 0.10f);
                m.SetFloat("_Wetness", 0.40f);
                m.SetFloat("_AmbientFloor", 0.35f);
                m.SetColor("_EmissionColor", new Color(0.02f, 0.09f, 0.02f, 1f));
                m.SetFloat("_UseVertexColor", 1f);
                m.SetFloat("_EnableBG3Cutout", 0f);
            });

            dict["Mat_Cartilage"] = CreateMat("Mat_Abyss_Cartilage", fleshShader, PilotTexturesFolder, "tex_cartilage.png", m =>
            {
                m.SetColor("_BaseColor", new Color(1.0f, 0.96f, 0.92f, 1f));
                m.SetFloat("_VertexSnap", 200f);
                m.SetFloat("_BreathSpeed", 0f);
                m.SetFloat("_BreathAmp", 0f);
                m.SetFloat("_SplitAmount", 0.06f);
                m.SetFloat("_Wetness", 0.34f);
                m.SetFloat("_AmbientFloor", 0.38f);
                m.SetColor("_EmissionColor", new Color(0.05f, 0.03f, 0.02f, 1f));
                m.SetFloat("_UseVertexColor", 1f);
                m.SetFloat("_EnableBG3Cutout", 0f);
            });

            dict["Mat_OssifiedStone"] = CreateMat("Mat_Abyss_OssifiedStone", fleshShader, PilotTexturesFolder, "tex_ossified_stone.png", m =>
            {
                m.SetColor("_BaseColor", Color.white);
                m.SetFloat("_VertexSnap", 220f);
                m.SetFloat("_BreathSpeed", 0f);
                m.SetFloat("_BreathAmp", 0f);
                m.SetFloat("_SplitAmount", 0f);
                m.SetFloat("_Wetness", 0.24f);
                m.SetFloat("_AmbientFloor", 0.40f);
                m.SetColor("_EmissionColor", new Color(0.04f, 0.03f, 0.03f, 1f));
                m.SetFloat("_UseVertexColor", 1f);
                m.SetFloat("_EnableBG3Cutout", 0f);
            });

            dict["Mat_Teeth"] = CreateMat("Mat_Abyss_Teeth", fleshShader, PilotTexturesFolder, "tex_teeth.png", m =>
            {
                m.SetColor("_BaseColor", new Color(1.0f, 0.98f, 0.92f, 1f));
                m.SetFloat("_VertexSnap", 220f);
                m.SetFloat("_BreathSpeed", 0f);
                m.SetFloat("_BreathAmp", 0f);
                m.SetFloat("_SplitAmount", 0f);
                m.SetFloat("_Wetness", 0.62f);
                m.SetFloat("_AmbientFloor", 0.42f);
                m.SetColor("_EmissionColor", new Color(0.05f, 0.04f, 0.03f, 1f));
                m.SetFloat("_UseVertexColor", 1f);
                m.SetFloat("_EnableBG3Cutout", 0f);
            });

            dict["Mat_VeinTendon"] = CreateMat("Mat_Abyss_VeinTendon", fleshShader, AbyssTexturesFolder, "tex_vein_tendon_bridge.png", m =>
            {
                m.SetColor("_BaseColor", Color.white);
                m.SetFloat("_VertexSnap", 190f);
                m.SetFloat("_BreathSpeed", 1.75f);
                m.SetFloat("_BreathAmp", 0.016f);
                m.SetFloat("_SplitAmount", 0.14f);
                m.SetFloat("_Wetness", 0.72f);
                m.SetFloat("_AmbientFloor", 0.36f);
                m.SetColor("_EmissionColor", new Color(0.18f, 0.02f, 0.02f, 1f));
                m.SetFloat("_UseVertexColor", 1f);
                m.SetFloat("_EnableBG3Cutout", 0f);
            });

            dict["Mat_HornedBone"] = CreateMat("Mat_Abyss_HornedBone", fleshShader, AbyssTexturesFolder, "tex_horned_bone_bound.png", m =>
            {
                m.SetColor("_BaseColor", Color.white);
                m.SetFloat("_VertexSnap", 200f);
                m.SetFloat("_BreathSpeed", 0f);
                m.SetFloat("_BreathAmp", 0f);
                m.SetFloat("_SplitAmount", 0.04f);
                m.SetFloat("_Wetness", 0.32f);
                m.SetFloat("_AmbientFloor", 0.42f);
                m.SetColor("_EmissionColor", new Color(0.06f, 0.04f, 0.03f, 1f));
                m.SetFloat("_UseVertexColor", 1f);
                m.SetFloat("_EnableBG3Cutout", 0f);
            });

            dict["Mat_MembraneBacklit"] = CreateMat("Mat_Abyss_MembraneBacklit", fleshShader, AbyssTexturesFolder, "tex_membrane_backlit.png", m =>
            {
                m.SetColor("_BaseColor", new Color(1.0f, 0.88f, 0.82f, 1f));
                m.SetFloat("_VertexSnap", 180f);
                m.SetFloat("_BreathSpeed", 1.1f);
                m.SetFloat("_BreathAmp", 0.025f);
                m.SetFloat("_SplitAmount", 0.08f);
                m.SetFloat("_Wetness", 0.48f);
                m.SetFloat("_AmbientFloor", 0.65f);
                m.SetColor("_EmissionColor", new Color(1.15f, 0.38f, 0.12f, 1f));
                m.SetFloat("_UseVertexColor", 1f);
                m.SetFloat("_EnableBG3Cutout", 0f);
            });

            dict["Mat_PortalVortex"] = CreateMat("Mat_Abyss_PortalVortex", acidShader, AbyssTexturesFolder, "tex_portal_vortex.png", m =>
            {
                m.SetColor("_BaseColor", new Color(0.65f, 1.0f, 0.95f, 1f));
                m.SetColor("_EmissionColor", new Color(0.35f, 1.40f, 1.28f, 1f));
                m.SetVector("_ScrollSpeedA", new Vector4(0.02f, 0.03f, 0f, 0f));
                m.SetVector("_ScrollSpeedB", new Vector4(-0.02f, -0.02f, 0f, 0f));
                m.SetFloat("_WaveSpeed", 2.8f);
                m.SetFloat("_WaveAmp", 0.015f);
                m.SetFloat("_VertexSnap", 190f);
                m.SetFloat("_PulseSpeed", 3.2f);
            });

            dict["Mat_Abyss_AcidLake"] = CreateMat("Mat_Abyss_AcidLake", acidShader, PilotTexturesFolder, "tex_acid.png", m =>
            {
                m.SetTextureScale("_BaseMap", new Vector2(1.15f, 1.15f));
                m.SetColor("_BaseColor", new Color(0.64f, 0.88f, 0.50f, 1f));
                m.SetColor("_EmissionColor", new Color(0.14f, 0.72f, 0.07f, 1f));
                m.SetVector("_ScrollSpeedA", new Vector4(0.032f, 0.022f, 0f, 0f));
                m.SetVector("_ScrollSpeedB", new Vector4(-0.020f, 0.038f, 0f, 0f));
                m.SetFloat("_WaveSpeed", 1.85f);
                m.SetFloat("_WaveAmp", 0.045f);
                m.SetFloat("_VertexSnap", 180f);
                m.SetFloat("_PulseSpeed", 2.0f);
            });

            dict["Mat_Abyss_AcidWaterfall"] = CreateMat("Mat_Abyss_AcidWaterfall", acidShader, PilotTexturesFolder, "tex_acid.png", m =>
            {
                m.SetTextureScale("_BaseMap", new Vector2(1.2f, 2.4f));
                m.SetColor("_BaseColor", new Color(0.70f, 0.92f, 0.54f, 1f));
                m.SetColor("_EmissionColor", new Color(0.18f, 0.85f, 0.10f, 1f));
                m.SetVector("_ScrollSpeedA", new Vector4(0.01f, 0.42f, 0f, 0f));
                m.SetVector("_ScrollSpeedB", new Vector4(-0.015f, 0.58f, 0f, 0f));
                m.SetFloat("_WaveSpeed", 3.6f);
                m.SetFloat("_WaveAmp", 0.030f);
                m.SetFloat("_VertexSnap", 180f);
                m.SetFloat("_PulseSpeed", 2.8f);
            });

            dict["Mat_Abyss_AcidGlow"] = CreateMat("Mat_Abyss_AcidGlow", acidShader, PilotTexturesFolder, "tex_acid.png", m =>
            {
                m.SetColor("_BaseColor", new Color(0.88f, 1.0f, 0.72f, 1f));
                m.SetColor("_EmissionColor", new Color(0.32f, 1.35f, 0.16f, 1f));
                m.SetVector("_ScrollSpeedA", new Vector4(0.05f, 0.04f, 0f, 0f));
                m.SetVector("_ScrollSpeedB", new Vector4(-0.04f, 0.06f, 0f, 0f));
                m.SetFloat("_WaveSpeed", 2.4f);
                m.SetFloat("_WaveAmp", 0.008f);
                m.SetFloat("_VertexSnap", 200f);
                m.SetFloat("_PulseSpeed", 3.0f);
            });

            dict["Mat_BloodWaves"] = CreateMat("Mat_Abyss_BloodAcidFoam", acidShader, PilotTexturesFolder, "tex_acid.png", m =>
            {
                m.SetTextureScale("_BaseMap", new Vector2(1.6f, 3.0f));
                m.SetColor("_BaseColor", new Color(0.55f, 0.85f, 0.45f, 1f));
                m.SetColor("_EmissionColor", new Color(0.12f, 0.55f, 0.08f, 1f));
                m.SetVector("_ScrollSpeedA", new Vector4(0.01f, 0.52f, 0f, 0f));
                m.SetVector("_ScrollSpeedB", new Vector4(-0.02f, 0.68f, 0f, 0f));
                m.SetFloat("_WaveSpeed", 3.4f);
                m.SetFloat("_WaveAmp", 0.022f);
                m.SetFloat("_VertexSnap", 180f);
                m.SetFloat("_PulseSpeed", 2.6f);
            });

            AssetDatabase.SaveAssets();
            return dict;
        }

        private static Material CreateMat(string matName, Shader shader, string texFolder, string texFile, Action<Material> configure)
        {
            string matPath = $"{MaterialsFolder}/{matName}.mat";
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

            Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>($"{texFolder}/{texFile}");
            if (tex != null)
            {
                mat.SetTexture("_BaseMap", tex);
            }

            configure?.Invoke(mat);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static void ConfigurePixelTextures()
        {
            string[] newTextures =
            {
                $"{AbyssTexturesFolder}/tex_vein_tendon_bridge.png",
                $"{AbyssTexturesFolder}/tex_membrane_backlit.png",
                $"{AbyssTexturesFolder}/tex_horned_bone_bound.png",
                $"{AbyssTexturesFolder}/tex_portal_vortex.png"
            };

            foreach (string path in newTextures)
            {
                TextureImporter ti = AssetImporter.GetAtPath(path) as TextureImporter;
                if (ti == null) continue;

                bool changed = false;
                if (ti.filterMode != FilterMode.Point) { ti.filterMode = FilterMode.Point; changed = true; }
                if (ti.textureCompression != TextureImporterCompression.Uncompressed) { ti.textureCompression = TextureImporterCompression.Uncompressed; changed = true; }
                if (ti.wrapMode != TextureWrapMode.Repeat) { ti.wrapMode = TextureWrapMode.Repeat; changed = true; }
                if (changed) ti.SaveAndReimport();
            }
        }

        private static void ReimportAllGlbModels()
        {
            if (!Directory.Exists(ModelsFolder)) return;
            string[] glbs = Directory.GetFiles(ModelsFolder, "*.glb");
            for (int i = 0; i < glbs.Length; i++)
            {
                string assetPath = glbs[i].Replace('\\', '/');
                AssetImporter imp = AssetImporter.GetAtPath(assetPath);
                if (imp != null)
                {
                    imp.SaveAndReimport();
                }
            }
        }

        private static void ConfigureLightingAtmosphereAndCamera(Transform parent)
        {
            // Limpa qualquer StomachFog global residual
            Shader.SetGlobalVector("_StomachFogPlayerPos", Vector4.zero);
            Shader.SetGlobalVector("_StomachFogRadii", Vector4.zero);
            Shader.SetGlobalVector("_StomachFogParams", Vector4.zero);
            Shader.SetGlobalVector("_StomachFogAnim", Vector4.zero);

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.42f, 0.16f, 0.15f, 1f);
            RenderSettings.ambientEquatorColor = new Color(0.30f, 0.14f, 0.12f, 1f);
            RenderSettings.ambientGroundColor = new Color(0.14f, 0.34f, 0.10f, 1f);

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = new Color(0.14f, 0.045f, 0.04f, 1f);
            RenderSettings.fogDensity = 0.0052f;

            // Luz Direcional principal quente visceral
            GameObject dirGo = new GameObject("Sun_Visceral_Directional");
            dirGo.transform.SetParent(parent, false);
            dirGo.transform.rotation = Quaternion.Euler(48f, -32f, 0f);
            Light dirLight = dirGo.AddComponent<Light>();
            dirLight.type = LightType.Directional;
            dirLight.color = new Color(1.0f, 0.72f, 0.62f, 1f);
            dirLight.intensity = 1.20f;
            dirLight.shadows = LightShadows.Soft;

            // Luzes Pontuais Dramaticas para Destacar Monumentos, Pontes, Acido e Abobada
            AddPointLight(parent, "Light_AcidGlow_SouthBridge", new Vector3(0f, 2.2f, -33f), new Color(0.42f, 1.0f, 0.22f), 26f, 34f);
            AddPointLight(parent, "Light_VeinBridge_CrimsonCenter", new Vector3(0f, 9.5f, 0.5f), new Color(1.0f, 0.28f, 0.22f), 42f, 32f);
            AddPointLight(parent, "Light_AcidLake_UnderVeinBridge", new Vector3(0f, 1.5f, 0.5f), new Color(0.36f, 1.0f, 0.18f), 32f, 32f);
            AddPointLight(parent, "Light_CentralArena_WarmTorch", new Vector3(0f, 11.5f, 18f), new Color(1.0f, 0.62f, 0.34f), 45f, 38f);
            AddPointLight(parent, "Light_GiantSkull_AcidMaw", new Vector3(-31f, 7.5f, 3.5f), new Color(0.38f, 1.0f, 0.20f), 48f, 38f);
            AddPointLight(parent, "Light_BoneWing_CrimsonGlow", new Vector3(31f, 7.5f, -5.5f), new Color(1.0f, 0.38f, 0.24f), 42f, 38f);
            AddPointLight(parent, "Light_NorthAltar_WarmCrown", new Vector3(0f, 13.0f, 56f), new Color(1.0f, 0.66f, 0.36f), 44f, 38f);
            AddPointLight(parent, "Light_HornedColossus_AcidOrbs", new Vector3(0f, 19.5f, 67.5f), new Color(0.42f, 1.0f, 0.22f), 64f, 44f);
            AddPointLight(parent, "Light_PortalExit_CyanGlow", new Vector3(9.5f, 9.5f, 55.5f), new Color(0.35f, 1.0f, 0.88f), 36f, 26f);

            // Clarabóias quentes sob a abobada membranosa (Foto 2)
            AddPointLight(parent, "Light_CeilingMembrane_South", new Vector3(0f, 38f, -18f), new Color(1.0f, 0.44f, 0.18f), 110f, 75f);
            AddPointLight(parent, "Light_CeilingMembrane_North", new Vector3(0f, 38f, 36f), new Color(1.0f, 0.44f, 0.18f), 110f, 75f);

            // Camera Principal dentro da caverna com visao ampla do mapa
            GameObject camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            camGo.transform.SetParent(parent, false);
            camGo.transform.position = new Vector3(-16f, 23f, -42f);
            camGo.transform.LookAt(new Vector3(0f, 6.5f, 24f));

            Camera cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.055f, 0.014f, 0.016f, 1f);
            cam.fieldOfView = 55f;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 450f;
            camGo.AddComponent<UniversalAdditionalCameraData>();
            camGo.AddComponent<AudioListener>();
        }

        private static void AddPointLight(Transform parent, string name, Vector3 pos, Color color, float intensity, float range)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            Light l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = color;
            l.intensity = intensity;
            l.range = range;
            l.shadows = LightShadows.None;
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
            string leaf = Path.GetFileName(folder);
            if (!AssetDatabase.IsValidFolder(parent))
            {
                EnsureFolder(parent);
            }
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
