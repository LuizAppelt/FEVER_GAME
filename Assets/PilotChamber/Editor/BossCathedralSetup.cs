#pragma warning disable 0618
using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.Rendering;

namespace MapaEstomago.Editor
{
    public static class BossCathedralSetup
    {
        private const string TexturesFolder = "Assets/PilotChamber/Textures";
        private const string MaterialsFolder = "Assets/PilotChamber/Materials";
        private const string CathedralGlbPath = "Assets/PilotChamber/Stomach_Boss_Cathedral.glb";
        private const string ScreenshotsFolder = "Assets/Screenshots";

        private static readonly string[] CathedralTextureFiles = new[]
        {
            "tex_flesh_bricks.png",
            "tex_ossified_stone.png",
            "tex_cathedral_stained_glass.png",
            "tex_cathedral_rose_window.png",
            "tex_cathedral_floor.png",
            "tex_cathedral_door.png",
            "tex_spire_shingles.png",
            "tex_organ_pipes.png"
        };

        [MenuItem("Estomago/Catedral do Boss/1. Construir e Configurar Catedral Completa na Cena", priority = 15)]
        public static void SetupBossCathedralInScene()
        {
            AssetDatabase.Refresh();
            ConfigureCathedralTextures();
            Dictionary<string, Material> mats = CreateOrUpdateCathedralMaterials();
            ConfigureGlbImporter();

            GameObject glbPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(CathedralGlbPath);
            if (glbPrefab == null)
            {
                Debug.LogError($"[BossCathedralSetup] Nao foi possivel carregar {CathedralGlbPath}!");
                return;
            }

            // Remove instancia anterior se existir
            GameObject existing = GameObject.Find("Stomach_Boss_Cathedral");
            if (existing != null)
            {
                Undo.DestroyObjectImmediate(existing);
            }

            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(glbPrefab);
            instance.name = "Stomach_Boss_Cathedral";
            Undo.RegisterCreatedObjectUndo(instance, "Criar Stomach_Boss_Cathedral");

            // Posiciona sobre o oceano de acido em area limpa (Y = 2.2 alinha a base Y = -2.2 do modelo com o lago de acido Y = 0)
            Vector3 cathedralWorldPos = new Vector3(195f, 2.2f, 0f);
            instance.transform.position = cathedralWorldPos;
            instance.transform.rotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;

            // Aplica materiais customizados e colisores em cada no
            AssignMaterialsAndColliders(instance, mats);

            // Cria iluminacao gotica dramática (Fachada + Nave + Altar + Orgao)
            CreateCathedralLighting(instance.transform);

            // Adiciona um manequim do Virus no topo da escadaria e outro na Nave para referencia de escala
            AddScaleMannequin(instance.transform, new Vector3(0f, 0.05f, -4.5f), "Player_Scale_Portal");
            AddScaleMannequin(instance.transform, new Vector3(0f, 0.05f, 22.0f), "Player_Scale_NaveCenter");

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();
            Debug.Log("<color=#00FF88><b>[BossCathedralSetup]</b> Stomach_Boss_Cathedral configurada com sucesso na cena!</color>");
            Selection.activeGameObject = instance;
        }

        private static void ConfigureCathedralTextures()
        {
            foreach (string file in CathedralTextureFiles)
            {
                string path = $"{TexturesFolder}/{file}";
                TextureImporter ti = AssetImporter.GetAtPath(path) as TextureImporter;
                if (ti != null)
                {
                    bool changed = false;
                    if (ti.filterMode != FilterMode.Point) { ti.filterMode = FilterMode.Point; changed = true; }
                    if (ti.textureCompression != TextureImporterCompression.Uncompressed) { ti.textureCompression = TextureImporterCompression.Uncompressed; changed = true; }
                    if (ti.wrapMode != TextureWrapMode.Repeat) { ti.wrapMode = TextureWrapMode.Repeat; changed = true; }
                    if (ti.mipmapEnabled) { ti.mipmapEnabled = false; changed = true; }
                    if (changed)
                    {
                        ti.SaveAndReimport();
                    }
                }
            }
        }

        private static void ConfigureGlbImporter()
        {
            AssetImporter importer = AssetImporter.GetAtPath(CathedralGlbPath);
            if (importer != null)
            {
                importer.SaveAndReimport();
            }
        }

        private static Dictionary<string, Material> CreateOrUpdateCathedralMaterials()
        {
            Shader fleshShader = Shader.Find("MapaEstomago/URP/PS1_LivingFlesh");
            Shader triplanarShader = Shader.Find("MapaEstomago/URP/PS1_TriplanarFlesh");
            Shader bloodWavesShader = Shader.Find("MapaEstomago/URP/PS1_BloodDrips");
            if (fleshShader == null) fleshShader = Shader.Find("Universal Render Pipeline/Lit");

            var dict = new Dictionary<string, Material>();

            // 1. Tijolos de Carne (Flesh Bricks) - Paredes cortam dinamicamente estilo BG3
            dict["Mat_FleshBricks"] = CreateOrUpdateMat("Mat_Cathedral_FleshBricks", fleshShader, "tex_flesh_bricks.png", m =>
            {
                m.SetColor("_BaseColor", new Color(1.0f, 0.95f, 0.95f, 1f));
                m.SetFloat("_VertexSnap", 220f);
                m.SetFloat("_BreathSpeed", 1.1f);
                m.SetFloat("_BreathAmp", 0.006f);
                m.SetFloat("_SplitAmount", 0.05f);
                m.SetFloat("_Wetness", 0.42f);
                m.SetFloat("_AmbientFloor", 0.34f);
                m.SetColor("_EmissionColor", new Color(0.06f, 0.01f, 0.01f, 1f));
                m.SetFloat("_UseVertexColor", 1f);
                m.SetFloat("_EnableBG3Cutout", 1f); // ATIVADO: paredes frontais abrem para revelar o jogador
            });

            // 2. Cantaria de Osso Gotico (Ossified Stone) - Pilares e contrafortes frontais cortam; chao protegido por normal
            dict["Mat_OssifiedStone"] = CreateOrUpdateMat("Mat_Cathedral_OssifiedStone", fleshShader, "tex_ossified_stone.png", m =>
            {
                m.SetColor("_BaseColor", new Color(1.0f, 0.98f, 0.94f, 1f));
                m.SetFloat("_VertexSnap", 240f);
                m.SetFloat("_BreathSpeed", 0.0f);
                m.SetFloat("_BreathAmp", 0.0f);
                m.SetFloat("_SplitAmount", 0.0f);
                m.SetFloat("_Wetness", 0.22f);
                m.SetFloat("_AmbientFloor", 0.42f);
                m.SetColor("_EmissionColor", new Color(0.08f, 0.07f, 0.06f, 1f));
                m.SetFloat("_UseVertexColor", 1f);
                m.SetFloat("_EnableBG3Cutout", 1f); // ATIVADO
            });

            // 3. Piso Cerimonial da Catedral - NUNCA CORTA O CHAO
            dict["Mat_CathedralFloor"] = CreateOrUpdateMat("Mat_Cathedral_Floor", fleshShader, "tex_cathedral_floor.png", m =>
            {
                m.SetColor("_BaseColor", Color.white);
                m.SetFloat("_VertexSnap", 240f);
                m.SetFloat("_BreathSpeed", 0.0f);
                m.SetFloat("_BreathAmp", 0.0f);
                m.SetFloat("_SplitAmount", 0.0f);
                m.SetFloat("_Wetness", 0.55f);
                m.SetFloat("_AmbientFloor", 0.40f);
                m.SetColor("_EmissionColor", new Color(0.05f, 0.02f, 0.02f, 1f));
                m.SetFloat("_UseVertexColor", 1f);
                m.SetFloat("_EnableBG3Cutout", 0f); // DESATIVADO: piso permanece 100% solido e visivel
            });

            // 4. Vitrais Goticos (Stained Glass) - Abrem junto com a parede frontal
            dict["Mat_StainedGlass"] = CreateOrUpdateMat("Mat_Cathedral_StainedGlass", fleshShader, "tex_cathedral_stained_glass.png", m =>
            {
                m.SetColor("_BaseColor", Color.white);
                m.SetFloat("_VertexSnap", 240f);
                m.SetFloat("_BreathSpeed", 0.8f);
                m.SetFloat("_BreathAmp", 0.002f);
                m.SetFloat("_SplitAmount", 0.0f);
                m.SetFloat("_Wetness", 0.75f);
                m.SetFloat("_AmbientFloor", 0.65f);
                m.SetColor("_EmissionColor", new Color(0.85f, 0.78f, 0.65f, 1f));
                m.SetFloat("_UseVertexColor", 1f);
                m.SetFloat("_EnableBG3Cutout", 1f); // ATIVADO
            });

            // 5. Grande Rosacea ("O Olho do Juizo") - Abre se estiver na frente da camera; no chao e protegida
            dict["Mat_RoseWindow"] = CreateOrUpdateMat("Mat_Cathedral_RoseWindow", fleshShader, "tex_cathedral_rose_window.png", m =>
            {
                m.SetColor("_BaseColor", Color.white);
                m.SetFloat("_VertexSnap", 240f);
                m.SetFloat("_BreathSpeed", 1.4f);
                m.SetFloat("_BreathAmp", 0.004f);
                m.SetFloat("_SplitAmount", 0.0f);
                m.SetFloat("_Wetness", 0.80f);
                m.SetFloat("_AmbientFloor", 0.70f);
                m.SetColor("_EmissionColor", new Color(0.95f, 0.82f, 0.68f, 1f));
                m.SetFloat("_UseVertexColor", 1f);
                m.SetFloat("_EnableBG3Cutout", 1f); // ATIVADO
            });

            // 6. Portao Monumental e Mobiliario do Coro
            dict["Mat_CathedralDoor"] = CreateOrUpdateMat("Mat_Cathedral_Door", fleshShader, "tex_cathedral_door.png", m =>
            {
                m.SetColor("_BaseColor", Color.white);
                m.SetFloat("_VertexSnap", 220f);
                m.SetFloat("_BreathSpeed", 0.0f);
                m.SetFloat("_BreathAmp", 0.0f);
                m.SetFloat("_SplitAmount", 0.0f);
                m.SetFloat("_Wetness", 0.35f);
                m.SetFloat("_AmbientFloor", 0.36f);
                m.SetColor("_EmissionColor", new Color(0.04f, 0.02f, 0.01f, 1f));
                m.SetFloat("_UseVertexColor", 1f);
                m.SetFloat("_EnableBG3Cutout", 1f); // ATIVADO
            });

            // 7. Telhas de Escamas das Agulhas e Teto (Spire Shingles) - Teto abre ao olhar o interior
            dict["Mat_SpireShingles"] = CreateOrUpdateMat("Mat_Cathedral_SpireShingles", fleshShader, "tex_spire_shingles.png", m =>
            {
                m.SetColor("_BaseColor", Color.white);
                m.SetFloat("_VertexSnap", 220f);
                m.SetFloat("_BreathSpeed", 0.0f);
                m.SetFloat("_BreathAmp", 0.0f);
                m.SetFloat("_SplitAmount", 0.0f);
                m.SetFloat("_Wetness", 0.48f);
                m.SetFloat("_AmbientFloor", 0.38f);
                m.SetColor("_EmissionColor", new Color(0.02f, 0.09f, 0.08f, 1f));
                m.SetFloat("_UseVertexColor", 1f);
                m.SetFloat("_EnableBG3Cutout", 1f); // ATIVADO
            });

            // 8. Tubos do Orgao de Traqueias e Candelabros
            dict["Mat_OrganPipes"] = CreateOrUpdateMat("Mat_Cathedral_OrganPipes", fleshShader, "tex_organ_pipes.png", m =>
            {
                m.SetColor("_BaseColor", Color.white);
                m.SetFloat("_VertexSnap", 240f);
                m.SetFloat("_BreathSpeed", 0.9f);
                m.SetFloat("_BreathAmp", 0.003f);
                m.SetFloat("_SplitAmount", 0.0f);
                m.SetFloat("_Wetness", 0.72f);
                m.SetFloat("_AmbientFloor", 0.44f);
                m.SetColor("_EmissionColor", new Color(0.12f, 0.09f, 0.04f, 1f));
                m.SetFloat("_UseVertexColor", 1f);
                m.SetFloat("_EnableBG3Cutout", 0f);
            });

            // Materiais existentes do projeto
            dict["Mat_HellOrgans"] = AssetDatabase.LoadAssetAtPath<Material>($"{MaterialsFolder}/Mat_HellOrgans.mat");
            dict["Mat_Cartilage"] = AssetDatabase.LoadAssetAtPath<Material>($"{MaterialsFolder}/Mat_Cartilage.mat");
            dict["Mat_DigestedCalcified"] = AssetDatabase.LoadAssetAtPath<Material>($"{MaterialsFolder}/Mat_DigestedCalcified.mat");
            dict["Mat_GastricAcid"] = AssetDatabase.LoadAssetAtPath<Material>($"{MaterialsFolder}/Mat_GastricAcid.mat");
            dict["Mat_BloodWaves"] = AssetDatabase.LoadAssetAtPath<Material>($"{MaterialsFolder}/Mat_BloodWaves.mat");

            // Tambem cria versoes Triplanares para o ProBuilder
            if (triplanarShader != null)
            {
                CreateOrUpdateMat("Mat_Triplanar_FleshBricks", triplanarShader, "tex_flesh_bricks.png", m =>
                {
                    m.SetFloat("_TextureScale", 2.0f);
                    m.SetFloat("_Wetness", 0.42f);
                    m.SetFloat("_OrganicBulge", 0.02f);
                    m.SetFloat("_EnableBG3Cutout", 1f);
                });
                CreateOrUpdateMat("Mat_Triplanar_OssifiedStone", triplanarShader, "tex_ossified_stone.png", m =>
                {
                    m.SetFloat("_TextureScale", 2.2f);
                    m.SetFloat("_Wetness", 0.25f);
                    m.SetFloat("_OrganicBulge", 0.0f);
                    m.SetFloat("_BreathAmp", 0.0f);
                    m.SetFloat("_EnableBG3Cutout", 1f);
                });
                CreateOrUpdateMat("Mat_Triplanar_CathedralFloor", triplanarShader, "tex_cathedral_floor.png", m =>
                {
                    m.SetFloat("_TextureScale", 2.5f);
                    m.SetFloat("_Wetness", 0.55f);
                    m.SetFloat("_OrganicBulge", 0.0f);
                    m.SetFloat("_BreathAmp", 0.0f);
                    m.SetFloat("_EnableBG3Cutout", 0f);
                });
            }

            AssetDatabase.SaveAssets();
            return dict;
        }

        private static Material CreateOrUpdateMat(string matName, Shader shader, string texFile, Action<Material> configure)
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

            Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexturesFolder}/{texFile}");
            if (tex != null)
            {
                mat.SetTexture("_BaseMap", tex);
            }

            configure?.Invoke(mat);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static void AssignMaterialsAndColliders(GameObject root, Dictionary<string, Material> mats)
        {
            // Mapeamento exato de cada sub-malha para seu material
            var nodeToMatKey = new Dictionary<string, string>
            {
                // Exterior
                { "Exterior_Cliff_Foundation", "Mat_DigestedCalcified" },
                { "Exterior_Grand_Staircase", "Mat_OssifiedStone" },
                { "Exterior_Streetlamp_Shafts", "Mat_OrganPipes" },
                { "Exterior_Facade_FleshBricks", "Mat_FleshBricks" },
                { "Exterior_Facade_OssifiedStone", "Mat_OssifiedStone" },
                { "Exterior_Lancet_StainedGlass", "Mat_StainedGlass" },
                { "Exterior_RoseWindow_Glass", "Mat_RoseWindow" },
                { "Exterior_Cathedral_Doors", "Mat_CathedralDoor" },
                { "Exterior_Belfries_And_Pinnacles", "Mat_OssifiedStone" },
                { "Exterior_Twin_Spire_Roofs", "Mat_SpireShingles" },
                { "Exterior_CastleKeep_Walls", "Mat_FleshBricks" },
                { "Exterior_CastleKeep_Stonework", "Mat_OssifiedStone" },
                { "Exterior_Visceral_Tendrils", "Mat_HellOrgans" },
                { "Exterior_Bone_Spikes_And_Finials", "Mat_Cartilage" },

                // Interior
                { "Cathedral_Floor_Walkable", "Mat_CathedralFloor" },
                { "Cathedral_Floor_Medallion", "Mat_RoseWindow" },
                { "Cathedral_Interior_Acid_Channels", "Mat_GastricAcid" },
                { "Cathedral_Nave_Columns_And_Arches", "Mat_OssifiedStone" },
                { "Cathedral_Walls_FleshMasonry", "Mat_FleshBricks" },
                { "Cathedral_Walls_StoneTracery", "Mat_OssifiedStone" },
                { "Cathedral_Wall_Right_Cutaway", "Mat_OssifiedStone" },
                { "Cathedral_StainedGlass_Windows", "Mat_StainedGlass" },
                { "Cathedral_GodRays", "Mat_BloodWaves" },
                { "Cathedral_Ceiling_Cutaway", "Mat_OssifiedStone" },
                { "Cathedral_Chandeliers", "Mat_OrganPipes" },
                { "Cathedral_Chandeliers_LanternGlass", "Mat_StainedGlass" },
                { "Cathedral_Bone_Horns_And_Cartilage", "Mat_Cartilage" },
                { "Cathedral_Sanctuary_And_Altar_Stone", "Mat_OssifiedStone" },
                { "Cathedral_Woodwork_Choir_And_OrganCase", "Mat_CathedralDoor" },
                { "Cathedral_Colossal_Organ_Pipes", "Mat_OrganPipes" },
                { "Cathedral_Visceral_Growths_And_Bellows", "Mat_HellOrgans" }
            };

            MeshRenderer[] renderers = root.GetComponentsInChildren<MeshRenderer>(true);
            foreach (MeshRenderer mr in renderers)
            {
                string n = mr.gameObject.name;
                if (nodeToMatKey.TryGetValue(n, out string matKey) && mats.TryGetValue(matKey, out Material targetMat) && targetMat != null)
                {
                    mr.sharedMaterial = targetMat;
                }

                // Adiciona MeshCollider nas superficies caminhaveis e paredes principais
                if (n == "Exterior_Grand_Staircase" ||
                    n == "Exterior_Cliff_Foundation" ||
                    n == "Exterior_Facade_FleshBricks" ||
                    n == "Exterior_Facade_OssifiedStone" ||
                    n == "Cathedral_Floor_Walkable" ||
                    n == "Cathedral_Nave_Columns_And_Arches" ||
                    n == "Cathedral_Walls_FleshMasonry" ||
                    n == "Cathedral_Walls_StoneTracery" ||
                    n == "Cathedral_Sanctuary_And_Altar_Stone" ||
                    n == "Cathedral_Woodwork_Choir_And_OrganCase")
                {
                    MeshFilter mf = mr.GetComponent<MeshFilter>();
                    if (mf != null && mf.sharedMesh != null)
                    {
                        MeshCollider mc = mr.GetComponent<MeshCollider>();
                        if (mc == null) mc = mr.gameObject.AddComponent<MeshCollider>();
                        mc.sharedMesh = mf.sharedMesh;
                    }
                }

                if (n == "Cathedral_GodRays")
                {
                    mr.shadowCastingMode = ShadowCastingMode.Off;
                    mr.gameObject.SetActive(false); // Mantem a visao interna da Nave 100% limpa
                }
            }
        }

        private static void CreateCathedralLighting(Transform cathedralRoot)
        {
            Transform oldLights = cathedralRoot.Find("Cathedral_Gothic_Lights");
            if (oldLights != null)
            {
                UnityEngine.Object.DestroyImmediate(oldLights.gameObject);
            }

            GameObject lightsGroup = new GameObject("Cathedral_Gothic_Lights");
            lightsGroup.transform.SetParent(cathedralRoot, false);

            // 1. Iluminacao Frontal da Fachada e Escadaria
            AddPointLight(lightsGroup.transform, "Facade_Portal_WarmGlow", new Vector3(0f, 5.5f, -5.0f), new Color(1.0f, 0.55f, 0.32f), 18f, 22f);
            AddPointLight(lightsGroup.transform, "Facade_RoseWindow_EyeGlow", new Vector3(0f, 17.2f, -4.5f), new Color(1.0f, 0.35f, 0.25f), 22f, 26f);
            AddPointLight(lightsGroup.transform, "Facade_LeftTower_Moonlight", new Vector3(-13.5f, 26.0f, -9.0f), new Color(0.35f, 0.95f, 0.78f), 20f, 32f);
            AddPointLight(lightsGroup.transform, "Facade_RightTower_Moonlight", new Vector3(13.5f, 26.0f, -9.0f), new Color(0.35f, 0.95f, 0.78f), 20f, 32f);
            AddPointLight(lightsGroup.transform, "Facade_Keep_EerieGlow", new Vector3(0f, 40.0f, -2.0f), new Color(0.30f, 0.95f, 0.72f), 25f, 40f);

            // 2. Lustres da Nave Central (5 Lustres)
            float[] chandelierZ = new[] { 5f, 13f, 21f, 29f, 37f };
            for (int i = 0; i < chandelierZ.Length; i++)
            {
                Color c = (i == 2) ? new Color(1.0f, 0.38f, 0.28f) : new Color(1.0f, 0.68f, 0.38f);
                AddPointLight(lightsGroup.transform, $"Nave_Chandelier_Light_{i + 1}", new Vector3(0f, 8.8f, chandelierZ[i]), c, 16f, 24f);
            }

            // 3. Brilho Verde dos Canais de Acido Internos
            AddPointLight(lightsGroup.transform, "AcidChannel_Glow_West_01", new Vector3(-14.2f, 1.5f, 14f), new Color(0.42f, 1.0f, 0.25f), 10f, 18f);
            AddPointLight(lightsGroup.transform, "AcidChannel_Glow_West_02", new Vector3(-14.2f, 1.5f, 30f), new Color(0.42f, 1.0f, 0.25f), 10f, 18f);
            AddPointLight(lightsGroup.transform, "AcidChannel_Glow_East_01", new Vector3(14.2f, 1.5f, 14f), new Color(0.42f, 1.0f, 0.25f), 10f, 18f);
            AddPointLight(lightsGroup.transform, "AcidChannel_Glow_East_02", new Vector3(14.2f, 1.5f, 30f), new Color(0.42f, 1.0f, 0.25f), 10f, 18f);

            // 4. Altar-Mor do Boss e Orgao Monumental de Traqueias
            AddPointLight(lightsGroup.transform, "Sanctuary_BossAltar_CrimsonLight", new Vector3(0f, 5.5f, 46.5f), new Color(1.0f, 0.25f, 0.22f), 22f, 26f);
            AddPointLight(lightsGroup.transform, "PipeOrgan_GoldenUplight_Center", new Vector3(0f, 12.0f, 50.5f), new Color(1.0f, 0.72f, 0.38f), 24f, 28f);
            AddPointLight(lightsGroup.transform, "PipeOrgan_Bellows_Left", new Vector3(-7.5f, 6.0f, 50.0f), new Color(0.95f, 0.30f, 0.25f), 14f, 18f);
            AddPointLight(lightsGroup.transform, "PipeOrgan_Bellows_Right", new Vector3(7.5f, 6.0f, 50.0f), new Color(0.95f, 0.30f, 0.25f), 14f, 18f);
        }

        private static void AddPointLight(Transform parent, string name, Vector3 localPos, Color color, float intensity, float range)
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
        }

        private static void AddScaleMannequin(Transform parent, Vector3 localPos, string name)
        {
            Transform old = parent.Find(name);
            if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);

            GameObject playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/PilotChamber/Virus_Player.glb");
            if (playerPrefab != null)
            {
                GameObject m = (GameObject)PrefabUtility.InstantiatePrefab(playerPrefab);
                m.name = name;
                m.transform.SetParent(parent, false);
                m.transform.localPosition = localPos;
                m.transform.localScale = Vector3.one * 0.85f;
            }
        }

        [MenuItem("Estomago/Catedral do Boss/2. Capturar Fotos da Fachada e do Interior (5 Angulos)", priority = 16)]
        public static void CaptureCathedralPhotos()
        {
            if (!Directory.Exists(ScreenshotsFolder))
            {
                Directory.CreateDirectory(ScreenshotsFolder);
            }

            GameObject cathedral = GameObject.Find("Stomach_Boss_Cathedral");
            if (cathedral == null)
            {
                SetupBossCathedralInScene();
                cathedral = GameObject.Find("Stomach_Boss_Cathedral");
            }

            Camera cam = Camera.main;
            if (cam == null) cam = UnityEngine.Object.FindFirstObjectByType<Camera>();
            if (cam == null)
            {
                Debug.LogError("[BossCathedralSetup] Nenhuma Camera encontrada!");
                return;
            }

            Vector3 origPos = cam.transform.position;
            Quaternion origRot = cam.transform.rotation;
            float origFov = cam.fieldOfView;
            float origFar = cam.farClipPlane;
            cam.farClipPlane = 500f;

            Vector3 basePos = cathedral.transform.position;

            // Referencias para malhas de cutaway (teto e parede direita) para as fotos internas isometricas
            Transform ceilingCutaway = cathedral.transform.Find("Cathedral_Ceiling_Cutaway");
            Transform rightWallCutaway = cathedral.transform.Find("Cathedral_Wall_Right_Cutaway");

            int width = 1280;
            int height = 720;
            RenderTexture rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            RenderTexture prevRt = cam.targetTexture;
            cam.targetTexture = rt;
            Texture2D tex = new Texture2D(width, height, TextureFormat.RGB24, false);

            void RenderShot(Vector3 camWorldPos, Vector3 lookTarget, float fov, string fileName)
            {
                cam.transform.position = camWorldPos;
                cam.transform.rotation = Quaternion.LookRotation((lookTarget - camWorldPos).normalized, Vector3.up);
                cam.fieldOfView = fov;
                cam.Render();

                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                tex.Apply();

                string fullPath = $"{ScreenshotsFolder}/{fileName}";
                File.WriteAllBytes(fullPath, tex.EncodeToPNG());
                Debug.Log($"<color=#00FFAA>[Foto Catedral Salva]</color> {fullPath}");
            }

            // Garante teto e parede ativos para fotos externas
            if (ceilingCutaway != null) ceilingCutaway.gameObject.SetActive(true);
            if (rightWallCutaway != null) rightWallCutaway.gameObject.SetActive(true);

            // FOTO 1: Fachada Frontal Completa (Estilo Catedral da Se + Castelo Sombrio)
            RenderShot(
                basePos + new Vector3(0f, 15.0f, -64.0f),
                basePos + new Vector3(0f, 22.0f, 0f),
                48f,
                "Cathedral_01_Front_Facade_Full.png"
            );

            // FOTO 2: Close-up Dramatico no Portal Ogival, Escadaria e Grande Rosacea ("O Olho do Juizo")
            RenderShot(
                basePos + new Vector3(4.2f, 4.8f, -27.5f),
                basePos + new Vector3(0f, 11.2f, -1.5f),
                44f,
                "Cathedral_02_Portal_And_RoseWindow.png"
            );

            // FOTO 3: Visao Isometrica 3/4 Externa da Catedral e Castelo no Lago de Acido
            RenderShot(
                basePos + new Vector3(-44.0f, 36.0f, -52.0f),
                basePos + new Vector3(0f, 16.0f, 6.0f),
                42f,
                "Cathedral_05_Exterior_Isometric_Overview.png"
            );

            // FOTO 4: Visao Panoramica Superior da Grande Nave (Mostrando a Rosacea Ritual no Piso, as 12 Colunas Goticas, Naves Laterais e Lustres)
            RenderShot(
                basePos + new Vector3(0f, 11.2f, 2.8f),
                basePos + new Vector3(0f, 1.5f, 25.0f),
                58f,
                "Cathedral_03_Interior_Nave_Isometric.png"
            );

            // FOTO 5: Interior da Nave olhando para o Altar-Mor do Boss, Baldaquino de Costelas e o Orgao Monumental de Traqueias
            RenderShot(
                basePos + new Vector3(0f, 4.8f, 14.0f),
                basePos + new Vector3(0f, 9.2f, 48.0f),
                48f,
                "Cathedral_04_Boss_Altar_And_PipeOrgan.png"
            );

            cam.targetTexture = prevRt;
            RenderTexture.active = null;
            UnityEngine.Object.DestroyImmediate(rt);
            UnityEngine.Object.DestroyImmediate(tex);

            cam.transform.position = origPos;
            cam.transform.rotation = origRot;
            cam.fieldOfView = origFov;
            cam.farClipPlane = origFar;

            AssetDatabase.Refresh();
            Debug.Log("<color=#00FF88><b>[BossCathedralSetup] Todas as 5 fotos da Catedral foram salvas em Assets/Screenshots/!</b></color>");
        }

        [MenuItem("Estomago/Catedral do Boss/Focar na Fachada Frontal", priority = 30)]
        public static void FocusFacade()
        {
            GameObject c = GameObject.Find("Stomach_Boss_Cathedral");
            if (c != null && SceneView.lastActiveSceneView != null)
            {
                SceneView.lastActiveSceneView.LookAt(c.transform.position + new Vector3(0f, 18f, 0f), Quaternion.Euler(12f, 0f, 0f), 55f);
            }
        }

        [MenuItem("Estomago/Catedral do Boss/Focar na Arena Interna do Boss (Nave)", priority = 31)]
        public static void FocusInteriorNave()
        {
            GameObject c = GameObject.Find("Stomach_Boss_Cathedral");
            if (c != null && SceneView.lastActiveSceneView != null)
            {
                SceneView.lastActiveSceneView.LookAt(c.transform.position + new Vector3(0f, 4f, 26f), Quaternion.Euler(35f, -40f, 0f), 38f);
            }
        }

        [MenuItem("Estomago/Catedral do Boss/Alternar Teto e Parede Direita (Cutaway Iso)", priority = 32)]
        public static void ToggleCutaway()
        {
            GameObject c = GameObject.Find("Stomach_Boss_Cathedral");
            if (c == null) return;
            Transform ceil = c.transform.Find("Cathedral_Ceiling_Cutaway");
            Transform rightWall = c.transform.Find("Cathedral_Wall_Right_Cutaway");
            bool nextState = ceil != null ? !ceil.gameObject.activeSelf : true;
            if (ceil != null) ceil.gameObject.SetActive(nextState);
            if (rightWall != null) rightWall.gameObject.SetActive(nextState);
        }
    }
}
