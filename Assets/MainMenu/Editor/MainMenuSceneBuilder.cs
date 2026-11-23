using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace StomachAbyss.MainMenu.Editor
{
    public static class MainMenuSceneBuilder
    {
        private const string MainMenuScenePath = "Assets/Scenes/MainMenu.unity";
        private const string MeshFolder = "Assets/MainMenu/Meshes";
        private const string MatFolder = "Assets/MainMenu/Materials";

        [MenuItem("StomachAbyss/Build Complete Main Menu Scene")]
        public static void BuildMainMenuScene()
        {
            EnsureFolders();

            Scene current = EditorSceneManager.GetActiveScene();
            if (current.IsValid() && current.isDirty && !string.IsNullOrEmpty(current.path))
            {
                EditorSceneManager.SaveScene(current);
            }

            // 1. Create materials using project's custom PS1_LivingFlesh URP shader (guarantees zero cyan/glTF shader issues!)
            Material heartCoreMat = CreateLivingFleshMaterial(
                MatFolder + "/Mat_MainMenu_HeartCoreEmber.mat",
                "Assets/MainMenu/Textures/tex_heart_inner_ember.png",
                Color.white,
                new Color(0.58f, 0.24f, 0.08f, 1f),
                wetness: 0.34f,
                ambientFloor: 0.56f,
                useVertexColor: 1f
            );

            Material heartCageMat = CreateLivingFleshMaterial(
                MatFolder + "/Mat_MainMenu_HeartOuterMyocardium.mat",
                "Assets/MainMenu/Textures/tex_heart_outer_myocardium.png",
                new Color(0.76f, 0.32f, 0.34f, 1f),
                new Color(0.32f, 0.08f, 0.06f, 1f),
                wetness: 0.42f,
                ambientFloor: 0.46f,
                useVertexColor: 1f
            );

            Material heartGoldenFireMat = CreateLivingFleshMaterial(
                MatFolder + "/Mat_MainMenu_HeartGoldenFire.mat",
                "Assets/MainMenu/Textures/tex_heart_inner_ember.png",
                new Color(1.0f, 0.78f, 0.28f, 1f),
                new Color(1.85f, 0.92f, 0.22f, 1f),
                wetness: 0.20f,
                ambientFloor: 0.85f,
                useVertexColor: 1f
            );

            Material bloodCrystalMat = CreateLivingFleshMaterial(
                MatFolder + "/Mat_MainMenu_BloodCrystal.mat",
                "Assets/MainMenu/Textures/tex_blood_crystal.png",
                new Color(0.78f, 0.11f, 0.13f, 1f),
                new Color(0.38f, 0.04f, 0.05f, 1f),
                wetness: 0.92f,
                ambientFloor: 0.42f,
                useVertexColor: 0f
            );

            Material spineBoneMat = CreateLivingFleshMaterialFromSource(
                MatFolder + "/Mat_MainMenu_SpinalBone.mat",
                "Assets/StomachAbyss/Materials/Mat_Abyss_HornedBone.mat",
                new Color(0.84f, 0.72f, 0.70f, 1f),
                new Color(0.28f, 0.16f, 0.16f, 1f),
                wetness: 0.28f,
                ambientFloor: 0.65f
            );

            Material darkRibMat = CreateLivingFleshMaterialFromSource(
                MatFolder + "/Mat_MainMenu_DarkRibFrame.mat",
                "Assets/StomachAbyss/Materials/Mat_Abyss_OssifiedStone.mat",
                new Color(0.32f, 0.25f, 0.27f, 1f),
                new Color(0.06f, 0.03f, 0.03f, 1f),
                wetness: 0.25f,
                ambientFloor: 0.36f
            );

            Material cavernBackdropMat = CreateLivingFleshMaterialFromSource(
                MatFolder + "/Mat_MainMenu_CavernBackdrop.mat",
                "Assets/StomachAbyss/Materials/Mat_Abyss_FleshWall.mat",
                new Color(0.72f, 0.36f, 0.38f, 1f),
                new Color(0.42f, 0.16f, 0.18f, 1f),
                wetness: 0.35f,
                ambientFloor: 0.48f
            );

            Material veinMat = CreateLivingFleshMaterial(
                MatFolder + "/Mat_MainMenu_ArteryBranch.mat",
                "Assets/MainMenu/Textures/tex_heart_outer_myocardium.png",
                new Color(0.78f, 0.24f, 0.26f, 1f),
                new Color(0.44f, 0.10f, 0.11f, 1f),
                wetness: 0.72f,
                ambientFloor: 0.54f,
                useVertexColor: 0f
            );

            Material terrainMat = CreateLivingFleshMaterialFromSource(
                MatFolder + "/Mat_MainMenu_ForegroundGround.mat",
                "Assets/StomachAbyss/Materials/Mat_Abyss_OssifiedStone.mat",
                new Color(0.46f, 0.38f, 0.40f, 1f),
                new Color(0.10f, 0.06f, 0.07f, 1f),
                wetness: 0.28f,
                ambientFloor: 0.52f
            );

            // 2. Build & persist procedural meshes (overwriting so updates take effect cleanly)
            Mesh heartCoreMesh = BuildAndSaveMesh(MeshFolder + "/Mesh_HeartInnerCore.asset", BuildVisceralAnatomicalHeartCoreMesh);
            Mesh heartWebMesh = BuildAndSaveMesh(MeshFolder + "/Mesh_HeartOuterCage.asset", BuildVisceralHeartCrustAndCoronaryMesh);
            Mesh heartCrownMesh = BuildAndSaveMesh(MeshFolder + "/Mesh_HeartCrownArteries.asset", BuildHeartGoldenFireSeamsAndTendrilsMesh);
            Mesh spinalColumnMesh = BuildAndSaveMesh(MeshFolder + "/Mesh_AnatomicalSpinalColumn.asset", BuildAnatomicalSpinalColumnMesh);

            Mesh vascularTreeA = BuildAndSaveMesh(MeshFolder + "/Mesh_VascularTree_A.asset", () => MainMenuMeshGenerator.BuildVascularBranchTreeMesh(1337, 11.5f, 0.42f, 4, 40f));
            Mesh vascularTreeB = BuildAndSaveMesh(MeshFolder + "/Mesh_VascularTree_B.asset", () => MainMenuMeshGenerator.BuildVascularBranchTreeMesh(2741, 13.0f, 0.46f, 4, 44f));
            Mesh vascularTreeC = BuildAndSaveMesh(MeshFolder + "/Mesh_VascularTree_C.asset", () => MainMenuMeshGenerator.BuildVascularBranchTreeMesh(4919, 9.8f,  0.34f, 4, 36f));

            Mesh crystalTallMesh = BuildAndSaveMesh(MeshFolder + "/Mesh_BloodCrystal_Tall.asset", () => MainMenuMeshGenerator.BuildBloodCrystalMonolithMesh(4.6f, 0.68f, 6, 0.34f));
            Mesh crystalShortMesh = BuildAndSaveMesh(MeshFolder + "/Mesh_BloodCrystal_Short.asset", () => MainMenuMeshGenerator.BuildBloodCrystalMonolithMesh(3.4f, 0.60f, 6, 0.28f));
            Mesh crystalClusterMesh = BuildAndSaveMesh(MeshFolder + "/Mesh_BloodCrystal_Cluster.asset", () => MainMenuMeshGenerator.BuildBloodCrystalMonolithMesh(1.35f, 0.30f, 5, 0.30f));

            Mesh terrainMesh = BuildAndSaveMesh(MeshFolder + "/Mesh_ForegroundTerrain.asset", MainMenuMeshGenerator.BuildForegroundTerrainMesh);
            Mesh domeMesh = BuildAndSaveMesh(MeshFolder + "/Mesh_ConcentricCavernDome.asset", MainMenuMeshGenerator.BuildConcentricCavernDomeMesh);

            // 3. Create new empty scene
            Scene menuScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Warm dusty-crimson atmospheric fog matching the reference
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.36f, 0.16f, 0.18f, 1f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = new Color(0.46f, 0.20f, 0.22f, 1f);
            RenderSettings.fogDensity = 0.018f;

            // ---------------- 00. MAIN CAMERA ----------------
            GameObject camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            Camera cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.42f, 0.18f, 0.20f, 1f);
            cam.fieldOfView = 50f;
            cam.nearClipPlane = 0.15f;
            cam.farClipPlane = 200f;
            camGo.AddComponent<AudioListener>();

            var camData = camGo.AddComponent<UniversalAdditionalCameraData>();
            camData.renderPostProcessing = true;

            camGo.transform.position = new Vector3(0f, 2.25f, -9.8f);
            camGo.transform.rotation = Quaternion.Euler(-11.5f, 0f, 0f);

            MainMenuCameraParallax camParallax = camGo.AddComponent<MainMenuCameraParallax>();
            camParallax.targetCamera = cam;
            camParallax.SetAnchorPose(camGo.transform.position, camGo.transform.rotation, 50f);

            // ---------------- 01. COLOSSAL PULSATING HEART ----------------
            GameObject heartRoot = new GameObject("01_Colossal_Pulsating_Heart");
            heartRoot.transform.position = new Vector3(0f, 7.30f, 11.2f);
            heartRoot.transform.rotation = Quaternion.Euler(3f, 0f, -3f);
            heartRoot.transform.localScale = Vector3.one * 1.24f;

            ColossalHeartController heartCtrl = heartRoot.AddComponent<ColossalHeartController>();
            heartCtrl.heartRootTransform = heartRoot.transform;
            heartCtrl.beatsPerMinute = 54f;
            heartCtrl.baseEmissionColor = new Color(0.55f, 0.22f, 0.08f, 1f);
            heartCtrl.peakEmissionColor = new Color(1.25f, 0.55f, 0.18f, 1f);
            heartCtrl.baseEmissionIntensity = 0.78f;
            heartCtrl.peakEmissionIntensity = 1.45f;
            heartCtrl.baseCoreLightIntensity = 2.4f;
            heartCtrl.peakCoreLightIntensity = 4.8f;
            heartCtrl.baseBacklightIntensity = 22f;
            heartCtrl.peakBacklightIntensity = 36f;

            // 1) Smooth anatomical ventricular body with painted golden-orange LAD coronary tree
            // 2) Anatomical great vessels (Pulmonary Trunk, Aortic Arch with 3 branches, Right Atrium/Vena Cava, Left Auricle)
            // 3) Blazing golden-yellow/orange magma crevices & fiery vascular tendrils
            GameObject innerCoreGo = CreateMeshObject("Heart_Inner_Ember_Core", heartRoot.transform, heartCoreMesh, heartCoreMat, Vector3.zero, Vector3.zero, Vector3.one);
            GameObject outerCageGo = CreateMeshObject("Heart_Outer_Myocardium_Cage", heartRoot.transform, heartWebMesh, heartCageMat, Vector3.zero, Vector3.zero, Vector3.one);
            GameObject crownArteriesGo = CreateMeshObject("Heart_Crown_Aorta_Trunks", heartRoot.transform, heartCrownMesh, heartGoldenFireMat, Vector3.zero, Vector3.zero, Vector3.one);
            CreateHeartGoldenEmberSparks(heartRoot.transform);

            heartCtrl.innerCoreTransform = innerCoreGo.transform;
            heartCtrl.outerCageTransform = outerCageGo.transform;
            heartCtrl.crownArteriesTransform = crownArteriesGo.transform;
            heartCtrl.innerCoreRenderer = innerCoreGo.GetComponent<Renderer>();
            heartCtrl.outerCageRenderer = outerCageGo.GetComponent<Renderer>();
            heartCtrl.crownArteriesRenderer = crownArteriesGo.GetComponent<Renderer>();

            GameObject coreLightGo = new GameObject("Heart_Core_PointLight");
            coreLightGo.transform.SetParent(heartRoot.transform, false);
            coreLightGo.transform.localPosition = new Vector3(0f, 0.2f, -3.2f);
            Light coreLight = coreLightGo.AddComponent<Light>();
            coreLight.type = LightType.Point;
            coreLight.color = new Color(1.0f, 0.28f, 0.18f, 1f);
            coreLight.intensity = 1.8f;
            coreLight.range = 16f;
            heartCtrl.corePointLight = coreLight;

            GameObject backLightGo = new GameObject("Heart_Volumetric_Backlight");
            backLightGo.transform.SetParent(heartRoot.transform, false);
            backLightGo.transform.localPosition = new Vector3(0f, -0.5f, 3.0f);
            Light backLight = backLightGo.AddComponent<Light>();
            backLight.type = LightType.Point;
            backLight.color = new Color(1.0f, 0.48f, 0.42f, 1f);
            backLight.intensity = 32f;
            backLight.range = 44f;
            heartCtrl.backlight = backLight;

            camParallax.heartController = heartCtrl;
            camParallax.heartTarget = heartRoot.transform;

            // ---------------- 02. VASCULAR CEILING CANOPY ----------------
            GameObject canopyRoot = new GameObject("02_Vascular_Ceiling_Canopy");
            List<Transform> canopyBranches = new List<Transform>();

            var branchPlacements = new (string name, Mesh mesh, Vector3 pos, Vector3 rot, Vector3 scale)[]
            {
                ("Vascular_Crown_Left",       vascularTreeA, new Vector3(-1.4f,  9.5f, 11.2f), new Vector3( 15f,   0f,  52f), new Vector3(1.22f, 1.22f, 1.22f)),
                ("Vascular_Crown_Right",      vascularTreeB, new Vector3( 1.4f,  9.5f, 11.2f), new Vector3( 15f, 180f,  52f), new Vector3(1.22f, 1.22f, 1.22f)),
                ("Vascular_Crown_CenterUp",   vascularTreeC, new Vector3( 0.0f,  9.9f, 11.5f), new Vector3(-28f,   0f,   0f), new Vector3(1.25f, 1.12f, 1.25f)),
                ("Vascular_Arch_FarLeft",     vascularTreeB, new Vector3(-6.8f,  7.5f,  4.5f), new Vector3( 22f,  25f,  78f), new Vector3(1.15f, 1.20f, 1.15f)),
                ("Vascular_Arch_FarRight",    vascularTreeA, new Vector3( 6.8f,  7.5f,  4.5f), new Vector3( 22f, 155f,  78f), new Vector3(1.15f, 1.20f, 1.15f)),
                ("Vascular_Hanging_MidLeft",  vascularTreeC, new Vector3(-9.5f,  3.5f,  8.0f), new Vector3( 10f,  40f, 105f), new Vector3(1.05f, 1.10f, 1.05f)),
                ("Vascular_Hanging_MidRight", vascularTreeC, new Vector3( 9.5f,  3.5f,  8.0f), new Vector3( 10f, 140f, 105f), new Vector3(1.05f, 1.10f, 1.05f)),
                ("Vascular_Tendril_BottomL",  vascularTreeC, new Vector3(-3.8f,  6.2f, 11.0f), new Vector3(  8f,  15f, 118f), new Vector3(0.85f, 0.95f, 0.85f)),
                ("Vascular_Tendril_BottomR",  vascularTreeC, new Vector3( 3.8f,  6.2f, 11.0f), new Vector3(  8f, 165f, 118f), new Vector3(0.85f, 0.95f, 0.85f))
            };

            foreach (var bp in branchPlacements)
            {
                GameObject bGo = CreateMeshObject(bp.name, canopyRoot.transform, bp.mesh, veinMat, bp.pos, bp.rot, bp.scale);
                canopyBranches.Add(bGo.transform);
            }
            heartCtrl.vascularCanopyTransforms = canopyBranches.ToArray();

            // ---------------- 03. SPINAL COLUMN PEDESTAL ----------------
            GameObject spineRoot = new GameObject("03_Spinal_Column_Pedestal");
            spineRoot.transform.position = new Vector3(0.15f, -1.1f, 15.6f);

            CreateMeshObject("Anatomical_Spinal_Column", spineRoot.transform, spinalColumnMesh, spineBoneMat, Vector3.zero, Vector3.zero, new Vector3(1.15f, 1.12f, 1.15f));

            // ---------------- 04. FOREGROUND & BLOOD CRYSTALS ----------------
            GameObject fgRoot = new GameObject("04_Foreground_And_Blood_Crystals");

            CreateMeshObject("Foreground_Rocky_Terrain", fgRoot.transform, terrainMesh, terrainMat, new Vector3(0f, -1.35f, 4.0f), Vector3.zero, Vector3.one);

            // Two prominent dark-red Blood Crystal Monoliths on the bottom-right foreground
            CreateMeshObject(
                "BloodCrystal_Monolith_Primary_Right",
                fgRoot.transform,
                crystalTallMesh,
                bloodCrystalMat,
                new Vector3(2.45f, -1.45f, -3.15f),
                new Vector3(-4f, -22f, 10f),
                new Vector3(0.78f, 0.88f, 0.78f)
            );
            CreateMeshObject(
                "BloodCrystal_Monolith_Secondary_Right",
                fgRoot.transform,
                crystalShortMesh,
                bloodCrystalMat,
                new Vector3(3.38f, -1.55f, -2.85f),
                new Vector3(-3f, 14f, 8f),
                new Vector3(0.72f, 0.84f, 0.72f)
            );

            var clusterCoords = new (Vector3 pos, Vector3 rot, float scale)[]
            {
                (new Vector3(-3.8f, -1.55f, -1.5f), new Vector3(8f,  35f, -14f), 0.52f),
                (new Vector3(-1.9f, -1.42f,  0.8f), new Vector3(4f, -50f,  12f), 0.45f),
                (new Vector3( 0.4f, -1.48f, -0.4f), new Vector3(-6f, 18f,   8f), 0.48f),
                (new Vector3( 1.8f, -1.65f, -3.8f), new Vector3(12f,-15f,  18f), 0.42f),
                (new Vector3(-5.2f, -1.60f, -3.2f), new Vector3(6f,  75f, -10f), 0.56f)
            };

            for (int i = 0; i < clusterCoords.Length; i++)
            {
                var cc = clusterCoords[i];
                CreateMeshObject($"Crimson_Crystal_Cluster_{i + 1}", fgRoot.transform, crystalClusterMesh, bloodCrystalMat, cc.pos, cc.rot, Vector3.one * cc.scale);
            }

            // ---------------- 05. CAVERN VAULT & GIANT RIB ARCHES ----------------
            GameObject cavernRoot = new GameObject("05_Cavern_Vault_And_RibArches");
            CreateMeshObject("Concentric_Ribbed_Cavern_Dome", cavernRoot.transform, domeMesh, cavernBackdropMat, new Vector3(0f, 3.0f, 14.0f), Vector3.zero, Vector3.one);

            GameObject ribArchPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/StomachAbyss/Models/Cavern_RibArch.glb");
            if (ribArchPrefab != null)
            {
                GameObject ribNear = (GameObject)PrefabUtility.InstantiatePrefab(ribArchPrefab, cavernRoot.transform);
                ribNear.name = "Cavern_RibArch_ForegroundFrame";
                ribNear.transform.localPosition = new Vector3(0f, -2.5f, -1.5f);
                ribNear.transform.localRotation = Quaternion.identity;
                ribNear.transform.localScale = new Vector3(0.45f, 0.42f, 0.45f);
                AssignMaterialRecursively(ribNear.transform, darkRibMat);

                GameObject ribMid = (GameObject)PrefabUtility.InstantiatePrefab(ribArchPrefab, cavernRoot.transform);
                ribMid.name = "Cavern_RibArch_MidgroundFrame";
                ribMid.transform.localPosition = new Vector3(0f, -2.0f, 7.5f);
                ribMid.transform.localRotation = Quaternion.identity;
                ribMid.transform.localScale = new Vector3(0.55f, 0.52f, 0.55f);
                AssignMaterialRecursively(ribMid.transform, darkRibMat);
            }

            // ---------------- 06. ATMOSPHERE, LIGHTING & FOG ----------------
            GameObject atmoRoot = new GameObject("06_Atmosphere_Lighting_And_Fog");

            GameObject dirLightGo = new GameObject("Directional_Crimson_FillLight");
            dirLightGo.transform.SetParent(atmoRoot.transform, false);
            dirLightGo.transform.rotation = Quaternion.Euler(24f, -25f, 0f);
            Light dirLight = dirLightGo.AddComponent<Light>();
            dirLight.type = LightType.Directional;
            dirLight.color = new Color(0.95f, 0.44f, 0.46f, 1f);
            dirLight.intensity = 1.15f;

            GameObject crystalLightGo = new GameObject("Foreground_Crystals_RimLight");
            crystalLightGo.transform.SetParent(atmoRoot.transform, false);
            crystalLightGo.transform.position = new Vector3(1.8f, 0.5f, -4.5f);
            Light crystalLight = crystalLightGo.AddComponent<Light>();
            crystalLight.type = LightType.Point;
            crystalLight.color = new Color(1.0f, 0.28f, 0.24f, 1f);
            crystalLight.intensity = 5.5f;
            crystalLight.range = 10.5f;

            GameObject spineLightGo = new GameObject("Spine_Mist_GlowLight");
            spineLightGo.transform.SetParent(atmoRoot.transform, false);
            spineLightGo.transform.position = new Vector3(0f, 2.6f, 13.8f);
            Light spineLight = spineLightGo.AddComponent<Light>();
            spineLight.type = LightType.Point;
            spineLight.color = new Color(1.0f, 0.52f, 0.48f, 1f);
            spineLight.intensity = 18.0f;
            spineLight.range = 28f;

            GameObject volumeGo = new GameObject("Global_PostProcess_Volume");
            volumeGo.transform.SetParent(atmoRoot.transform, false);
            Volume vol = volumeGo.AddComponent<Volume>();
            vol.isGlobal = true;
            VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>("Assets/StomachAbyss/Atmosphere/StomachAbyssVolumeProfile.asset");
            if (profile != null)
            {
                vol.sharedProfile = profile;
            }

            CreateFloatingSporeParticles(atmoRoot.transform);

            // ---------------- 07. MAIN MENU AUDIO SYSTEM ----------------
            GameObject audioRoot = new GameObject("07_MainMenu_Audio_System");
            MainMenuAudioController audioCtrl = audioRoot.AddComponent<MainMenuAudioController>();
            audioCtrl.heartController = heartCtrl;
            audioCtrl.heartbeatClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/MainMenu/Audio/sfx_menu_heartbeat_lubdub.wav");
            audioCtrl.cavernDroneClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/MainMenu/Audio/amb_menu_cavern_drone.wav");
            audioCtrl.uiHoverClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/MainMenu/Audio/sfx_menu_ui_hover.wav");
            audioCtrl.uiClickPlayClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/MainMenu/Audio/sfx_menu_ui_click_play.wav");

            // ---------------- 08. MAIN MENU UI CANVAS ----------------
            GameObject uiRoot = new GameObject("08_MainMenu_UI_Canvas");
            MainMenuUIController uiCtrl = uiRoot.AddComponent<MainMenuUIController>();
            uiCtrl.gameplaySceneName = "Stomach_Abyss";
            uiCtrl.BuildOrBindUI();
            uiCtrl.BindControllers(heartCtrl, audioCtrl, camParallax);
            SetupTopLeftGameLogo(uiCtrl);

            EditorSceneManager.MarkSceneDirty(menuScene);
            EditorSceneManager.SaveScene(menuScene, MainMenuScenePath);
            RegisterScenesInBuildSettings();
            AssetDatabase.SaveAssets();

            Debug.Log("[MainMenuSceneBuilder] MainMenu.unity rebuilt with PS1_LivingFlesh materials and anatomical spinal column!");
        }

        private static void AssignMaterialRecursively(Transform root, Material mat)
        {
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r != null) r.sharedMaterial = mat;
            }
        }

        private static void SetupTopLeftGameLogo(MainMenuUIController uiCtrl)
        {
            const string logoTexPath = "Assets/MainMenu/Textures/tex_hell_flesh_logo.png";
            TextureImporter importer = AssetImporter.GetAtPath(logoTexPath) as TextureImporter;
            if (importer != null)
            {
                bool changed = false;
                if (importer.textureType != TextureImporterType.Sprite)
                {
                    importer.textureType = TextureImporterType.Sprite;
                    importer.spriteImportMode = SpriteImportMode.Single;
                    changed = true;
                }
                if (!importer.alphaIsTransparency)
                {
                    importer.alphaIsTransparency = true;
                    changed = true;
                }
                if (importer.mipmapEnabled)
                {
                    importer.mipmapEnabled = false;
                    changed = true;
                }
                if (changed)
                {
                    importer.SaveAndReimport();
                }
            }

            Sprite logoSprite = AssetDatabase.LoadAssetAtPath<Sprite>(logoTexPath);
            Texture2D logoTex = AssetDatabase.LoadAssetAtPath<Texture2D>(logoTexPath);
            if (uiCtrl == null || uiCtrl.mainCanvas == null || (logoSprite == null && logoTex == null))
                return;

            RectTransform anchor = uiCtrl.logoPlaceholderAnchor;
            if (anchor == null)
            {
                GameObject go = new GameObject("Logo_Placeholder_Anchor", typeof(RectTransform));
                go.transform.SetParent(uiCtrl.mainCanvas.transform, false);
                anchor = go.GetComponent<RectTransform>();
                uiCtrl.logoPlaceholderAnchor = anchor;
            }

            anchor.gameObject.SetActive(true);
            anchor.anchorMin = new Vector2(0f, 1f);
            anchor.anchorMax = new Vector2(0f, 1f);
            anchor.pivot = new Vector2(0f, 1f);
            anchor.anchoredPosition = new Vector2(58f, -38f);
            anchor.sizeDelta = new Vector2(370f, 257f);

            UnityEngine.UI.Image img = anchor.GetComponent<UnityEngine.UI.Image>();
            if (img == null) img = anchor.gameObject.AddComponent<UnityEngine.UI.Image>();
            if (logoSprite != null)
            {
                img.sprite = logoSprite;
                img.preserveAspect = true;
            }
            img.color = Color.white;
            img.raycastTarget = false;
        }

        /// <summary>
        /// Evaluates the smooth, plump anatomical human ventricular body matching the reference painting:
        /// slender vertical heart teardrop proportion (height 3.90 vs width 2.55), broad rounded left shoulder (-X),
        /// plump lower-right ventricular wall (+X), pointed-rounded bottom apex, and a smooth diagonal top conus
        /// merging seamlessly into the Pulmonary Trunk and Aorta!
        /// </summary>
        private static Vector3 EvaluateContinuousVisceralHeartPoint(float v, float theta, float radialOffset, out float lobeBulge, out float sulcusGroove)
        {
            float phi = (1f - v) * Mathf.PI; // PI (bottom apex, v=0) to 0 (top collar, v=1)
            float cosPhi = Mathf.Cos(phi);
            float sinPhi = Mathf.Sin(phi);

            // True vertical anatomical heart height profile (y in [-2.60, +1.35], height = 3.95)
            float y = -0.62f - 1.98f * cosPhi;

            // Slender anatomical heart teardrop radius (max baseR ~ 1.18, never wider than tall!):
            // Pointed-rounded bottom apex (v < 0.42), plump mid-upper ventricles (v ~ 0.64), smooth dome at top
            float baseR = sinPhi * (1.06f + 0.22f * Mathf.Sin(v * Mathf.PI * 0.82f));
            if (v < 0.42f)
            {
                baseR *= Mathf.Lerp(0.42f, 1.0f, Mathf.Pow(v / 0.42f, 0.85f));
            }

            // Smooth anatomical asymmetry:
            // 1. Rounded Left Ventricular Shoulder (viewer's left -X, theta ~ PI, v ~ 0.64)
            float dLeftShoulderV = (v - 0.64f) / 0.26f;
            float dLeftShoulderT = Mathf.DeltaAngle(theta * Mathf.Rad2Deg, 175f) * Mathf.Deg2Rad / 0.78f;
            float leftShoulderSwell = 0.24f * Mathf.Exp(-0.5f * (dLeftShoulderV * dLeftShoulderV + dLeftShoulderT * dLeftShoulderT)) * sinPhi;

            // 2. Plump Right-Lower Ventricular Belly (viewer's right +X, theta ~ -18 deg, v ~ 0.48)
            float dRightBellyV = (v - 0.48f) / 0.25f;
            float dRightBellyT = Mathf.DeltaAngle(theta * Mathf.Rad2Deg, -18f) * Mathf.Deg2Rad / 0.75f;
            float rightBellySwell = 0.20f * Mathf.Exp(-0.5f * (dRightBellyV * dRightBellyV + dRightBellyT * dRightBellyT)) * sinPhi;

            // 3. Conus Arteriosus (upper-center/right ventricular outflow tract rising smoothly into the Pulmonary Trunk at v ~ 0.82, theta ~ -85 deg)
            float dConusV = (v - 0.80f) / 0.18f;
            float dConusT = Mathf.DeltaAngle(theta * Mathf.Rad2Deg, -82f) * Mathf.Deg2Rad / 0.65f;
            float conusSwell = 0.22f * Mathf.Exp(-0.5f * (dConusV * dConusV + dConusT * dConusT));
            y += conusSwell * 0.65f;

            // 4. Smooth Anterior Interventricular Sulcus (LAD groove running diagonally down-right across the front face -Z)
            float ladTheta = -1.42f + (1f - v) * 0.14f;
            float dLad = Mathf.DeltaAngle(theta * Mathf.Rad2Deg, ladTheta * Mathf.Rad2Deg) * Mathf.Deg2Rad;
            sulcusGroove = Mathf.Exp(-(dLad * dLad) / 0.048f) * sinPhi;

            lobeBulge = leftShoulderSwell + rightBellySwell + conusSwell * sinPhi;
            float r = Mathf.Max(0.04f, baseR * (1.0f - 0.10f * sulcusGroove) + lobeBulge + radialOffset);

            // Anatomical oblique cardiac axis: upper shoulder leans slightly left (-X), lower apex points slightly right (+X)
            float xAxisShift = Mathf.Lerp(0.12f, -0.16f, v);
            float zFlatten = 0.82f;

            return new Vector3(
                Mathf.Cos(theta) * r + xAxisShift,
                y,
                Mathf.Sin(theta) * r * zFlatten
            );
        }

        /// <summary>
        /// Builds the smooth, plump anatomical ventricular body mesh with 1:1 front-projected spherical UVs
        /// (u=0.5 dead-center on the front -Z face!) so the painted golden-orange LAD coronary tree aligns 100%!
        /// </summary>
        private static Mesh BuildVisceralAnatomicalHeartCoreMesh()
        {
            List<Vector3> verts = new List<Vector3>();
            List<Vector2> uvs = new List<Vector2>();
            List<Color> colors = new List<Color>();
            List<int> tris = new List<int>();

            int latSegs = 72;
            int lonSegs = 84;

            for (int iy = 0; iy <= latSegs; iy++)
            {
                float v = iy / (float)latSegs;

                for (int ix = 0; ix <= lonSegs; ix++)
                {
                    float uRaw = ix / (float)lonSegs;
                    // Start seam at the back (+Z, theta = +PI/2) so u = 0.5 is at the front (-Z, theta = -PI/2) with zero seam on the front!
                    float theta = Mathf.PI * 0.5f - uRaw * Mathf.PI * 2f;

                    Vector3 pt = EvaluateContinuousVisceralHeartPoint(v, theta, 0f, out _, out _);
                    verts.Add(pt);
                    uvs.Add(new Vector2(1f - uRaw, v));
                    colors.Add(Color.white);
                }
            }

            int stride = lonSegs + 1;
            for (int iy = 0; iy < latSegs; iy++)
            {
                for (int ix = 0; ix < lonSegs; ix++)
                {
                    int a = iy * stride + ix;
                    int b = a + 1;
                    int c = a + stride;
                    int d = c + 1;
                    tris.Add(a); tris.Add(c); tris.Add(b);
                    tris.Add(b); tris.Add(c); tris.Add(d);
                }
            }

            Mesh m = new Mesh { name = "Heart_VisceralAnatomicalCore" };
            m.SetVertices(verts);
            m.SetUVs(0, uvs);
            m.SetColors(colors);
            m.SetTriangles(tris, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        /// <summary>
        /// Builds the Anatomical Great Vessels & Atrial Chambers crowning the upper half of the heart
        /// (Pulmonary Trunk arching up-right, Ascending Aorta & Aortic Arch with 3 superior arterial branches,
        /// Right Atrium/Auricle & Superior Vena Cava on the upper-left, and Left Auricle on the upper-right)
        /// matching the exact anatomy of the reference painting!
        /// </summary>
        private static Mesh BuildVisceralHeartCrustAndCoronaryMesh()
        {
            List<Vector3> verts = new List<Vector3>();
            List<Vector2> uvs = new List<Vector2>();
            List<Color> colors = new List<Color>();
            List<int> tris = new List<int>();

            // 1. Pulmonary Trunk (rises from upper-front center of ventricles and arches gracefully to the upper-right +X)
            Vector3[] pulmonaryTrunk =
            {
                new Vector3(-0.08f, 0.52f, -0.78f),
                new Vector3( 0.04f, 1.05f, -0.72f),
                new Vector3( 0.32f, 1.48f, -0.58f),
                new Vector3( 0.82f, 1.72f, -0.42f),
                new Vector3( 1.42f, 1.92f, -0.28f),
                new Vector3( 2.02f, 2.28f, -0.18f)
            };
            AppendSmoothAnatomicalVessel(verts, uvs, colors, tris, pulmonaryTrunk, 0.44f, 0.24f, 18, new Color(0.95f, 0.78f, 0.78f));

            // 2. Ascending Aorta & Aortic Arch (massive central arterial column behind the pulmonary root)
            Vector3[] aorticArch =
            {
                new Vector3(-0.22f, 0.72f, -0.35f),
                new Vector3(-0.18f, 1.38f, -0.32f),
                new Vector3(-0.04f, 2.05f, -0.24f),
                new Vector3( 0.18f, 2.52f, -0.12f),
                new Vector3( 0.32f, 2.28f,  0.22f)
            };
            AppendSmoothAnatomicalVessel(verts, uvs, colors, tris, aorticArch, 0.54f, 0.44f, 20, Color.white);

            // 3. The 3 Iconic Superior Aortic Arch Branches (Brachiocephalic curving up-left, Left Common Carotid center-up, Left Subclavian right-up)
            Vector3[] branchLeft =
            {
                new Vector3(-0.22f, 2.15f, -0.24f),
                new Vector3(-0.58f, 2.68f, -0.24f),
                new Vector3(-1.05f, 2.95f, -0.20f),
                new Vector3(-1.62f, 3.12f, -0.15f)
            };
            AppendSmoothAnatomicalVessel(verts, uvs, colors, tris, branchLeft, 0.32f, 0.16f, 14, Color.white);

            Vector3[] branchCenter =
            {
                new Vector3(-0.02f, 2.32f, -0.20f),
                new Vector3( 0.02f, 2.82f, -0.16f),
                new Vector3( 0.06f, 3.22f, -0.12f)
            };
            AppendSmoothAnatomicalVessel(verts, uvs, colors, tris, branchCenter, 0.26f, 0.20f, 14, Color.white);

            Vector3[] branchRight =
            {
                new Vector3( 0.26f, 2.28f, -0.14f),
                new Vector3( 0.45f, 2.75f, -0.10f),
                new Vector3( 0.68f, 3.12f, -0.06f)
            };
            AppendSmoothAnatomicalVessel(verts, uvs, colors, tris, branchRight, 0.24f, 0.18f, 14, Color.white);

            // 4. Right Atrium / Auricle & Superior Vena Cava (the two stacked diagonal muscular chambers on the upper-left shoulder -X)
            Vector3[] rightAtriumLower =
            {
                new Vector3(-1.45f, 0.62f, -0.42f),
                new Vector3(-1.18f, 1.05f, -0.48f),
                new Vector3(-0.78f, 1.42f, -0.45f)
            };
            AppendSmoothAnatomicalVessel(verts, uvs, colors, tris, rightAtriumLower, 0.44f, 0.36f, 16, new Color(0.92f, 0.72f, 0.72f));

            Vector3[] rightAtriumUpper =
            {
                new Vector3(-1.02f, 1.48f, -0.35f),
                new Vector3(-0.75f, 1.88f, -0.36f),
                new Vector3(-0.42f, 2.22f, -0.30f)
            };
            AppendSmoothAnatomicalVessel(verts, uvs, colors, tris, rightAtriumUpper, 0.42f, 0.32f, 16, new Color(0.95f, 0.78f, 0.75f));

            // 5. Left Auricle Chamber (oval chamber tucked on the upper-right shoulder under the Pulmonary Arch)
            Vector3[] leftAuricle =
            {
                new Vector3( 0.82f, 0.65f, -0.50f),
                new Vector3( 0.88f, 1.05f, -0.52f),
                new Vector3( 0.78f, 1.38f, -0.45f)
            };
            AppendSmoothAnatomicalVessel(verts, uvs, colors, tris, leftAuricle, 0.38f, 0.32f, 16, new Color(1.0f, 0.82f, 0.72f));

            Mesh m = new Mesh { name = "Heart_VisceralCrustAndCoronary" };
            m.SetVertices(verts);
            m.SetUVs(0, uvs);
            m.SetColors(colors);
            m.SetTriangles(tris, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        /// <summary>
        /// Builds the blazing golden-yellow/orange magma crevices between the great vessels and ventricles,
        /// plus the raised glowing LAD coronary artery and fiery vascular tendrils flaring off the right auricle and crown!
        /// </summary>
        private static Mesh BuildHeartGoldenFireSeamsAndTendrilsMesh()
        {
            List<Vector3> verts = new List<Vector3>();
            List<Vector2> uvs = new List<Vector2>();
            List<Color> colors = new List<Color>();
            List<int> tris = new List<int>();

            Color hotYellow = new Color(1.0f, 0.96f, 0.68f, 1f);
            Color fieryOrange = new Color(1.0f, 0.48f, 0.14f, 1f);

            // 1. Blazing Golden-White Magma Pocket in the arch under the Pulmonary Trunk & Left Auricle (signature bright glow in the reference!)
            Vector3[] pulmonaryArchFire =
            {
                new Vector3( 0.14f, 0.88f, -0.94f),
                new Vector3( 0.34f, 1.32f, -0.84f),
                new Vector3( 0.72f, 1.60f, -0.68f),
                new Vector3( 1.15f, 1.62f, -0.56f),
                new Vector3( 1.12f, 1.12f, -0.66f),
                new Vector3( 0.78f, 0.75f, -0.82f)
            };
            AppendSmoothAnatomicalVessel(verts, uvs, colors, tris, pulmonaryArchFire, 0.24f, 0.18f, 14, hotYellow);

            // 2. Blazing Magma Seam between Right Atrium (-X), Aorta, and Left Ventricular Shoulder
            Vector3[] leftCollarFire =
            {
                new Vector3(-1.32f, 0.58f, -0.68f),
                new Vector3(-0.92f, 0.96f, -0.82f),
                new Vector3(-0.32f, 1.28f, -0.86f),
                new Vector3(-0.36f, 1.78f, -0.62f),
                new Vector3(-0.68f, 2.15f, -0.48f)
            };
            AppendSmoothAnatomicalVessel(verts, uvs, colors, tris, leftCollarFire, 0.22f, 0.15f, 14, hotYellow);

            // 3. Organic curving fiery arterial tendrils around the Left Auricle (+X) and Aortic Crown
            Vector3[] rightTendrilA =
            {
                new Vector3(0.95f, 1.25f, -0.65f),
                new Vector3(1.42f, 1.45f, -0.52f),
                new Vector3(1.82f, 1.82f, -0.35f),
                new Vector3(2.05f, 2.22f, -0.22f)
            };
            AppendSmoothAnatomicalVessel(verts, uvs, colors, tris, rightTendrilA, 0.14f, 0.025f, 10, hotYellow);

            Vector3[] rightTendrilB =
            {
                new Vector3(0.88f, 0.85f, -0.72f),
                new Vector3(1.36f, 0.98f, -0.58f),
                new Vector3(1.75f, 1.32f, -0.42f),
                new Vector3(1.96f, 1.68f, -0.28f)
            };
            AppendSmoothAnatomicalVessel(verts, uvs, colors, tris, rightTendrilB, 0.12f, 0.020f, 10, fieryOrange);

            Mesh m = new Mesh { name = "Heart_GoldenFireSeamsAndTendrils" };
            m.SetVertices(verts);
            m.SetUVs(0, uvs);
            m.SetColors(colors);
            m.SetTriangles(tris, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        private static void AppendSmoothAnatomicalVessel(
            List<Vector3> verts,
            List<Vector2> uvs,
            List<Color> colors,
            List<int> tris,
            Vector3[] controlPts,
            float rStart,
            float rEnd,
            int sides,
            Color vertexTint)
        {
            // Subdivide control points into a smooth Catmull-Rom / quadratic curve with 16 rings
            int rings = 16;
            Vector3[] smoothPath = new Vector3[rings];
            for (int i = 0; i < rings; i++)
            {
                float t = i / (float)(rings - 1);
                float scaledT = t * (controlPts.Length - 1);
                int idx = Mathf.Clamp(Mathf.FloorToInt(scaledT), 0, controlPts.Length - 2);
                float frac = scaledT - idx;
                Vector3 p0 = controlPts[Mathf.Max(0, idx - 1)];
                Vector3 p1 = controlPts[idx];
                Vector3 p2 = controlPts[idx + 1];
                Vector3 p3 = controlPts[Mathf.Min(controlPts.Length - 1, idx + 2)];
                smoothPath[i] = 0.5f * (
                    (2f * p1) +
                    (-p0 + p2) * frac +
                    (2f * p0 - 5f * p1 + 4f * p2 - p3) * (frac * frac) +
                    (-p0 + 3f * p1 - 3f * p2 + p3) * (frac * frac * frac)
                );
            }

            int baseIdx = verts.Count;
            for (int r = 0; r < rings; r++)
            {
                float t = r / (float)(rings - 1);
                float rad = Mathf.Lerp(rStart, rEnd, t);
                // Smoothly round/cap the very start and end of the vessel
                if (r == 0 || r == rings - 1) rad *= 0.65f;

                Vector3 fwd = (r < rings - 1) ? (smoothPath[r + 1] - smoothPath[r]).normalized : (smoothPath[r] - smoothPath[r - 1]).normalized;
                if (fwd.sqrMagnitude < 0.001f) fwd = Vector3.up;
                Quaternion rot = Quaternion.FromToRotation(Vector3.up, fwd);

                for (int s = 0; s <= sides; s++)
                {
                    float u = s / (float)sides;
                    float ang = u * Mathf.PI * 2f;
                    Vector3 local = new Vector3(Mathf.Cos(ang) * rad, 0f, Mathf.Sin(ang) * rad);
                    verts.Add(smoothPath[r] + rot * local);
                    uvs.Add(new Vector2(u, t));
                    colors.Add(vertexTint);
                }
            }

            int stride = sides + 1;
            for (int r = 0; r < rings - 1; r++)
            {
                for (int s = 0; s < sides; s++)
                {
                    int a = baseIdx + r * stride + s;
                    int b = a + 1;
                    int c = a + stride;
                    int d = c + 1;
                    tris.Add(a); tris.Add(c); tris.Add(b);
                    tris.Add(b); tris.Add(c); tris.Add(d);
                }
            }
        }

        private static void CreateHeartGoldenEmberSparks(Transform heartParent)
        {
            GameObject psGo = new GameObject("Heart_Golden_Ember_Sparks");
            psGo.transform.SetParent(heartParent, false);
            psGo.transform.localPosition = new Vector3(0f, 0.2f, -0.5f);

            ParticleSystem ps = psGo.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.8f, 3.6f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.25f, 0.85f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.12f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(1.0f, 0.85f, 0.32f, 0.95f),
                new Color(1.0f, 0.38f, 0.12f, 0.85f)
            );
            main.maxParticles = 90;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var emission = ps.emission;
            emission.rateOverTime = 26f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 2.1f;

            ParticleSystemRenderer psr = psGo.GetComponent<ParticleSystemRenderer>();
            Material pMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/PilotChamber/Materials/Mat_PixelAcidParticle.mat");
            if (pMat == null) pMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/PilotChamber/Materials/Mat_PixelBloodParticle.mat");
            if (pMat != null) psr.sharedMaterial = pMat;
        }

        private static Mesh BuildOpenVascularHeartWebMesh()
        {
            List<Vector3> verts = new List<Vector3>();
            List<Vector2> uvs = new List<Vector2>();
            List<int> tris = new List<int>();

            // 18 winding coronary vessel bands wrapping around the heart profile
            int vesselCount = 18;
            int steps = 16;
            int tubeSides = 6;

            for (int v = 0; v < vesselCount; v++)
            {
                float startAngle = (v / (float)vesselCount) * Mathf.PI * 2f;
                float twist = ((v % 2 == 0) ? 1f : -1f) * 1.45f;
                float radiusThick = (v % 3 == 0) ? 0.22f : 0.13f;

                Vector3[] path = new Vector3[steps];
                for (int s = 0; s < steps; s++)
                {
                    float t = s / (float)(steps - 1);
                    float y = Mathf.Lerp(-2.65f, 2.15f, t);
                    float profileR = EvaluateHeartProfileRadius(y);
                    float angle = startAngle + t * twist + 0.25f * Mathf.Sin(t * Mathf.PI * 3f + v);
                    path[s] = new Vector3(Mathf.Sin(angle) * profileR, y, Mathf.Cos(angle) * profileR);
                }
                AppendTubeAlongPath(verts, uvs, tris, path, radiusThick * 0.45f, radiusThick, tubeSides);
            }

            // 6 dark coagulated myocardial nodules on the upper/lateral lobes (like the dark rocky bumps on the reference heart)
            var noduleSpecs = new (Vector3 pos, Vector3 scale)[]
            {
                (new Vector3(-1.35f,  0.85f,  0.55f), new Vector3(0.62f, 0.55f, 0.52f)),
                (new Vector3( 1.42f,  0.65f,  0.48f), new Vector3(0.68f, 0.58f, 0.55f)),
                (new Vector3(-1.55f, -0.25f,  0.30f), new Vector3(0.58f, 0.48f, 0.50f)),
                (new Vector3( 1.48f, -0.45f,  0.35f), new Vector3(0.55f, 0.46f, 0.48f)),
                (new Vector3( 0.00f,  1.55f,  0.95f), new Vector3(0.75f, 0.52f, 0.55f)),
                (new Vector3(-0.45f, -1.15f,  0.85f), new Vector3(0.52f, 0.45f, 0.44f))
            };

            foreach (var ns in noduleSpecs)
            {
                AppendLumpyNodule(verts, uvs, tris, ns.pos, ns.scale);
            }

            Mesh m = new Mesh { name = "Heart_OpenVascularWeb" };
            m.SetVertices(verts);
            m.SetUVs(0, uvs);
            m.SetTriangles(tris, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        private static float EvaluateHeartProfileRadius(float y)
        {
            float t = Mathf.InverseLerp(-2.75f, 2.25f, y);
            if (t < 0.12f) return Mathf.Lerp(0.18f, 0.78f, t / 0.12f);
            if (t > 0.88f) return Mathf.Lerp(2.05f, 1.35f, (t - 0.88f) / 0.12f);
            return Mathf.Lerp(0.78f, 2.08f, Mathf.Pow(t, 0.65f));
        }

        /// <summary>
        /// Builds the vertical stack of 6 colossal interlocking anatomical vertebrae + diagonal right bone strut
        /// directly beneath the pulsating heart, matching the reference image's central bone column!
        /// </summary>
        private static Mesh BuildAnatomicalSpinalColumnMesh()
        {
            List<Vector3> verts = new List<Vector3>();
            List<Vector2> uvs = new List<Vector2>();
            List<int> tris = new List<int>();

            // Central continuous bone marrow core column
            Vector3[] corePath =
            {
                new Vector3(0f, 0.0f, 0.1f),
                new Vector3(0.05f, 2.7f, 0.05f),
                new Vector3(0f, 5.5f, 0.0f)
            };
            AppendTubeAlongPath(verts, uvs, tris, corePath, 0.88f, 0.62f, 10);

            int vertebraCount = 6;
            for (int i = 0; i < vertebraCount; i++)
            {
                float yCenter = 0.55f + i * 0.92f;
                float scale = Mathf.Lerp(1.26f, 0.84f, i / (float)(vertebraCount - 1));
                float xOff = Mathf.Sin(i * 0.7f) * 0.10f;

                // Main chunky vertebral centrum body
                AppendLumpyNodule(verts, uvs, tris, new Vector3(xOff, yCenter, 0f), new Vector3(1.25f * scale, 0.44f * scale, 0.96f * scale));

                // Left & Right transverse processes (thick bone processes sticking out horizontally)
                AppendLumpyNodule(verts, uvs, tris, new Vector3(xOff - 1.12f * scale, yCenter + 0.05f, 0.10f), new Vector3(0.68f * scale, 0.34f * scale, 0.46f * scale));
                AppendLumpyNodule(verts, uvs, tris, new Vector3(xOff + 1.12f * scale, yCenter - 0.04f, 0.10f), new Vector3(0.68f * scale, 0.34f * scale, 0.46f * scale));

                // Anterior median ridge facing the camera
                AppendLumpyNodule(verts, uvs, tris, new Vector3(xOff, yCenter, -0.72f * scale), new Vector3(0.46f * scale, 0.45f * scale, 0.50f * scale));
            }

            // Diagonal bone strut extending down-right from behind the 3rd vertebra (visible in the reference image)
            Vector3[] strutPath =
            {
                new Vector3(0.75f, 3.1f, 0.25f),
                new Vector3(2.15f, 1.8f, 0.35f),
                new Vector3(3.65f, 0.4f, 0.45f)
            };
            AppendTubeAlongPath(verts, uvs, tris, strutPath, 0.28f, 0.22f, 7);

            Mesh m = new Mesh { name = "Anatomical_Spinal_Column" };
            m.SetVertices(verts);
            m.SetUVs(0, uvs);
            m.SetTriangles(tris, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        private static void AppendTubeAlongPath(List<Vector3> verts, List<Vector2> uvs, List<int> tris, Vector3[] path, float rStart, float rEnd, int sides)
        {
            int baseIdx = verts.Count;
            int rings = path.Length;

            for (int r = 0; r < rings; r++)
            {
                float t = r / (float)(rings - 1);
                float rad = Mathf.Lerp(rStart, rEnd, Mathf.Sin(t * Mathf.PI * 0.85f + 0.15f));
                Vector3 fwd = (r < rings - 1) ? (path[r + 1] - path[r]).normalized : (path[r] - path[r - 1]).normalized;
                if (fwd.sqrMagnitude < 0.001f) fwd = Vector3.up;
                Quaternion rot = Quaternion.FromToRotation(Vector3.up, fwd);

                for (int s = 0; s < sides; s++)
                {
                    float ang = (s / (float)sides) * Mathf.PI * 2f;
                    Vector3 local = new Vector3(Mathf.Cos(ang) * rad, 0f, Mathf.Sin(ang) * rad);
                    verts.Add(path[r] + rot * local);
                    uvs.Add(new Vector2(s / (float)sides, t * 2f));
                }
            }

            for (int r = 0; r < rings - 1; r++)
            {
                for (int s = 0; s < sides; s++)
                {
                    int next = (s + 1) % sides;
                    int a = baseIdx + r * sides + s;
                    int b = baseIdx + r * sides + next;
                    int c = baseIdx + (r + 1) * sides + s;
                    int d = baseIdx + (r + 1) * sides + next;
                    tris.Add(a); tris.Add(c); tris.Add(b);
                    tris.Add(b); tris.Add(c); tris.Add(d);
                }
            }
        }

        private static void AppendLumpyNodule(List<Vector3> verts, List<Vector2> uvs, List<int> tris, Vector3 center, Vector3 radii)
        {
            int baseIdx = verts.Count;
            int latSegs = 7;
            int lonSegs = 10;

            for (int lat = 0; lat <= latSegs; lat++)
            {
                float theta = (lat / (float)latSegs) * Mathf.PI;
                float sinT = Mathf.Sin(theta);
                float cosT = Mathf.Cos(theta);

                for (int lon = 0; lon <= lonSegs; lon++)
                {
                    float phi = (lon / (float)lonSegs) * Mathf.PI * 2f;
                    float bump = 1f + 0.10f * Mathf.Sin(lat * 2.5f + lon * 1.7f);
                    Vector3 dir = new Vector3(sinT * Mathf.Cos(phi) * radii.x, cosT * radii.y, sinT * Mathf.Sin(phi) * radii.z) * bump;
                    verts.Add(center + dir);
                    uvs.Add(new Vector2(lon / (float)lonSegs, lat / (float)latSegs));
                }
            }

            int stride = lonSegs + 1;
            for (int lat = 0; lat < latSegs; lat++)
            {
                for (int lon = 0; lon < lonSegs; lon++)
                {
                    int a = baseIdx + lat * stride + lon;
                    int b = a + 1;
                    int c = a + stride;
                    int d = c + 1;
                    tris.Add(a); tris.Add(c); tris.Add(b);
                    tris.Add(b); tris.Add(c); tris.Add(d);
                }
            }
        }

        private static void RegisterScenesInBuildSettings()
        {
            string[] desiredScenes =
            {
                "Assets/Scenes/MainMenu.unity",
                "Assets/Scenes/Stomach_Abyss.unity",
                "Assets/Scenes/SampleScene.unity"
            };

            List<EditorBuildSettingsScene> buildScenes = new List<EditorBuildSettingsScene>();
            foreach (string path in desiredScenes)
            {
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) != null)
                {
                    buildScenes.Add(new EditorBuildSettingsScene(path, true));
                }
            }
            EditorBuildSettings.scenes = buildScenes.ToArray();
        }

        private static void CreateFloatingSporeParticles(Transform parent)
        {
            GameObject psGo = new GameObject("Crimson_Spore_Particles");
            psGo.transform.SetParent(parent, false);
            psGo.transform.position = new Vector3(0f, 3.5f, 4.0f);

            ParticleSystem ps = psGo.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = true;
            main.startLifetime = 8.5f;
            main.startSpeed = 0.25f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.13f);
            main.startColor = new Color(1.0f, 0.32f, 0.26f, 0.55f);
            main.maxParticles = 140;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var emission = ps.emission;
            emission.rateOverTime = 16f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(22f, 14f, 18f);

            ParticleSystemRenderer psr = psGo.GetComponent<ParticleSystemRenderer>();
            Material pMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/PilotChamber/Materials/Mat_PixelBloodParticle.mat");
            if (pMat != null) psr.sharedMaterial = pMat;
        }

        private static GameObject CreateMeshObject(string name, Transform parent, Mesh mesh, Material mat, Vector3 localPos, Vector3 localEuler, Vector3 localScale)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.Euler(localEuler);
            go.transform.localScale = localScale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }

        private static Material CreateLivingFleshMaterialFromSource(
            string assetPath,
            string sourceMatPath,
            Color baseColor,
            Color emissionColor,
            float wetness,
            float ambientFloor)
        {
            Material src = AssetDatabase.LoadAssetAtPath<Material>(sourceMatPath);
            Texture tex = null;
            if (src != null)
            {
                if (src.HasProperty("_BaseMap")) tex = src.GetTexture("_BaseMap");
                if (tex == null) tex = src.mainTexture;
            }
            string texPath = tex != null ? AssetDatabase.GetAssetPath(tex) : "Assets/MainMenu/Textures/tex_heart_outer_myocardium.png";
            return CreateLivingFleshMaterial(assetPath, texPath, baseColor, emissionColor, wetness, ambientFloor, 0f);
        }

        private static Material CreateLivingFleshMaterial(
            string assetPath,
            string texPath,
            Color baseColor,
            Color emissionColor,
            float wetness,
            float ambientFloor,
            float useVertexColor)
        {
            Shader sh = Shader.Find("MapaEstomago/URP/PS1_LivingFlesh");
            if (sh == null) sh = Shader.Find("Universal Render Pipeline/Lit");

            Material m = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
            if (m == null)
            {
                m = new Material(sh);
                AssetDatabase.CreateAsset(m, assetPath);
            }
            else
            {
                m.shader = sh;
            }

            TextureImporter texImp = AssetImporter.GetAtPath(texPath) as TextureImporter;
            if (texImp != null && texImp.wrapMode != TextureWrapMode.Repeat)
            {
                texImp.wrapMode = TextureWrapMode.Repeat;
                texImp.SaveAndReimport();
            }

            Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
            if (tex == null)
            {
                tex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/MainMenu/Textures/tex_heart_outer_myocardium.png");
            }

            m.color = baseColor;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", baseColor);
            if (tex != null)
            {
                m.mainTexture = tex;
                m.mainTextureScale = Vector2.one;
                m.mainTextureOffset = Vector2.zero;
                if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
                if (m.HasProperty("_BaseMap_ST")) m.SetVector("_BaseMap_ST", new Vector4(1f, 1f, 0f, 0f));
            }
            if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", emissionColor);
            if (m.HasProperty("_Wetness")) m.SetFloat("_Wetness", wetness);
            if (m.HasProperty("_AmbientFloor")) m.SetFloat("_AmbientFloor", ambientFloor);
            if (m.HasProperty("_UseVertexColor")) m.SetFloat("_UseVertexColor", useVertexColor);
            if (m.HasProperty("_VertexSnap")) m.SetFloat("_VertexSnap", 0.0f);
            if (m.HasProperty("_BreathAmp")) m.SetFloat("_BreathAmp", 0.015f);
            if (m.HasProperty("_BreathSpeed")) m.SetFloat("_BreathSpeed", 1.4f);

            EditorUtility.SetDirty(m);
            return m;
        }

        private static Mesh BuildAndSaveMesh(string assetPath, System.Func<Mesh> builder)
        {
            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);
            if (existing != null)
            {
                AssetDatabase.DeleteAsset(assetPath);
            }
            Mesh m = builder();
            AssetDatabase.CreateAsset(m, assetPath);
            return m;
        }

        private static void EnsureFolders()
        {
            if (!AssetDatabase.IsValidFolder("Assets/MainMenu"))
                AssetDatabase.CreateFolder("Assets", "MainMenu");
            if (!AssetDatabase.IsValidFolder(MeshFolder))
                AssetDatabase.CreateFolder("Assets/MainMenu", "Meshes");
            if (!AssetDatabase.IsValidFolder(MatFolder))
                AssetDatabase.CreateFolder("Assets/MainMenu", "Materials");
        }
    }
}
