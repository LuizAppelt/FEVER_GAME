using System;
using System.Collections.Generic;
using System.IO;
using MapaEstomago;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace StomachAbyss.Atmosphere.Editor
{
    /// <summary>
    /// Idempotently configures Stomach_Abyss atmosphere, PS1 shaders, rising toxic vapor fields,
    /// undulating acid lake motion, subterranean caustic underglow lights, URP post-processing Bloom,
    /// and pulsing backlit membrane wall pods without modifying SampleScene or PilotChamber assets.
    /// </summary>
    public static class AbyssAtmosphereInstaller
    {
        private const string AtmosphereFolder = "Assets/StomachAbyss/Atmosphere";
        private const string TexturesFolder = AtmosphereFolder + "/Textures";
        private const string VaporTexPath = TexturesFolder + "/tex_acid_vapor_ps1.png";
        private const string DropletTexPath = TexturesFolder + "/tex_acid_droplet_ps1.png";

        private const string AbyssAcidShaderName = "MapaEstomago/URP/PS1_AbyssAcid";
        private const string VaporParticleShaderName = "MapaEstomago/URP/PS1_AcidVaporParticle";

        private const string BaseAcidLakeMatPath = "Assets/StomachAbyss/Materials/Mat_Abyss_AcidLake.mat";
        private const string ActiveAcidLakeMatPath = AtmosphereFolder + "/Mat_Abyss_AcidLake_Active.mat";
        private const string VaporMatPath = AtmosphereFolder + "/Mat_Abyss_AcidVapor_PS1.mat";
        private const string DropletMatPath = AtmosphereFolder + "/Mat_Abyss_AcidDroplet_PS1.mat";
        private const string VolumeProfilePath = AtmosphereFolder + "/StomachAbyssVolumeProfile.asset";

        private const string EnhancementRootName = "08_Atmosphere_VFX_Enhancements";

        [MenuItem("Estomago/Atmosfera/Instalar Atmosfera e VFX em Stomach_Abyss", priority = 30)]
        public static void InstallInCurrentScene()
        {
            Scene activeScene = EditorSceneManager.GetActiveScene();
            if (!activeScene.IsValid())
            {
                Debug.LogError("[AbyssAtmosphereInstaller] No valid active scene open.");
                return;
            }

            if (string.Equals(activeScene.name, "SampleScene", StringComparison.OrdinalIgnoreCase))
            {
                Debug.LogError("[AbyssAtmosphereInstaller] Refusing to run on SampleScene.unity. Open Assets/Scenes/Stomach_Abyss.unity first.");
                return;
            }

            EnsureFolder(AtmosphereFolder);
            EnsureFolder(TexturesFolder);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            // 1. Configure PS1 sprite textures
            Texture2D vaporTex = ConfigurePs1SpriteTexture(VaporTexPath);
            Texture2D dropletTex = ConfigurePs1SpriteTexture(DropletTexPath);

            // 2. Create/update materials
            Material activeAcidMat = CreateOrUpdateActiveAcidLakeMaterial();
            Material vaporMat = CreateOrUpdateParticleMaterial(VaporMatPath, vaporTex, additive: false);
            Material dropletMat = CreateOrUpdateParticleMaterial(DropletMatPath, dropletTex, additive: true);

            // 3. Configure root hierarchy group
            GameObject rootGroup = GetOrCreateSceneRoot(activeScene, EnhancementRootName);

            // 4. Acid lake & waterfall motion
            SetupAcidLakeAndWaterfalls(activeAcidMat);

            // 5. URP Volume, Post-Processing, Camera & Dark Atmospheric Fog
            VolumeProfile profile = CreateOrUpdateVolumeProfile();
            SetupVolumeAndAtmosphericFog(rootGroup.transform, profile);

            // 6. Toxic vapor plumes, mist, spray, cathedral fumes & boiling droplets
            SetupToxicVaporFields(rootGroup.transform, vaporMat, dropletMat);

            // 7. Subterranean caustic underglow lights
            SetupCausticUnderglowLights(rootGroup.transform);

            // 8. Pulsing orange backlit wall & ceiling alveoli pods
            SetupMembranePodPulses(rootGroup.transform);

            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(activeScene);

            Debug.Log("<color=#33FF99><b>[AbyssAtmosphereInstaller]</b> Atmosphere, URP Bloom, Acid Lake Swells, Toxic Vapors, Caustic Underglow, and Membrane Pod Pulses installed in '" + activeScene.name + "'.</color>");
        }

        private static Texture2D ConfigurePs1SpriteTexture(string assetPath)
        {
            TextureImporter ti = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (ti == null)
            {
                Debug.LogWarning("[AbyssAtmosphereInstaller] Texture not found at: " + assetPath);
                return null;
            }

            bool changed = false;
            if (ti.textureType != TextureImporterType.Sprite) { ti.textureType = TextureImporterType.Sprite; changed = true; }
            if (ti.spriteImportMode != SpriteImportMode.Single) { ti.spriteImportMode = SpriteImportMode.Single; changed = true; }
            if (!ti.alphaIsTransparency) { ti.alphaIsTransparency = true; changed = true; }
            if (ti.filterMode != FilterMode.Point) { ti.filterMode = FilterMode.Point; changed = true; }
            if (ti.mipmapEnabled) { ti.mipmapEnabled = false; changed = true; }
            if (ti.wrapMode != TextureWrapMode.Clamp) { ti.wrapMode = TextureWrapMode.Clamp; changed = true; }
            if (ti.textureCompression != TextureImporterCompression.Uncompressed) { ti.textureCompression = TextureImporterCompression.Uncompressed; changed = true; }

            if (changed)
            {
                ti.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
        }

        private static Material CreateOrUpdateActiveAcidLakeMaterial()
        {
            Shader abyssAcidShader = Shader.Find(AbyssAcidShaderName);
            if (abyssAcidShader == null)
            {
                Debug.LogError("[AbyssAtmosphereInstaller] Shader not found: " + AbyssAcidShaderName);
                return AssetDatabase.LoadAssetAtPath<Material>(BaseAcidLakeMatPath);
            }

            Material mat = AssetDatabase.LoadAssetAtPath<Material>(ActiveAcidLakeMatPath);
            Material baseMat = AssetDatabase.LoadAssetAtPath<Material>(BaseAcidLakeMatPath);

            if (mat == null)
            {
                mat = baseMat != null ? new Material(baseMat) : new Material(abyssAcidShader);
                mat.shader = abyssAcidShader;
                AssetDatabase.CreateAsset(mat, ActiveAcidLakeMatPath);
            }
            else if (mat.shader != abyssAcidShader)
            {
                mat.shader = abyssAcidShader;
            }

            Texture baseTex = baseMat != null && baseMat.HasProperty("_BaseMap") ? baseMat.GetTexture("_BaseMap") : null;
            if (baseTex == null)
            {
                baseTex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/PilotChamber/Textures/tex_acid.png");
            }
            if (baseTex != null)
            {
                mat.SetTexture("_BaseMap", baseTex);
            }

            mat.SetTextureScale("_BaseMap", new Vector2(1.8f, 3.6f));
            mat.SetColor("_BaseColor", new Color(0.56f, 0.94f, 0.34f, 1f));
            mat.SetColor("_DeepColor", new Color(0.012f, 0.070f, 0.008f, 1f));
            mat.SetColor("_EmissionColor", new Color(0.42f, 1.85f, 0.16f, 1f));
            mat.SetVector("_ScrollSpeedA", new Vector4(0.036f, 0.025f, 0f, 0f));
            mat.SetVector("_ScrollSpeedB", new Vector4(-0.024f, 0.044f, 0f, 0f));
            mat.SetFloat("_WarpStrength", 0.14f);
            mat.SetFloat("_WarpFreq", 1.65f);
            mat.SetFloat("_WaveSpeed", 1.45f);
            mat.SetFloat("_WaveAmp", 0.48f);
            mat.SetFloat("_VertexSnap", 200f);
            mat.SetFloat("_PulseSpeed", 2.1f);
            mat.SetFloat("_FlowMod", 0f);
            mat.SetFloat("_ColorQuantSteps", 24f);
            mat.SetFloat("_DitherStrength", 0.85f);

            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static Material CreateOrUpdateParticleMaterial(string matPath, Texture2D spriteTex, bool additive)
        {
            Shader particleShader = Shader.Find(VaporParticleShaderName);
            if (particleShader == null)
            {
                Debug.LogError("[AbyssAtmosphereInstaller] Shader not found: " + VaporParticleShaderName);
                return null;
            }

            Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
            {
                mat = new Material(particleShader);
                AssetDatabase.CreateAsset(mat, matPath);
            }
            else if (mat.shader != particleShader)
            {
                mat.shader = particleShader;
            }

            if (spriteTex != null)
            {
                mat.SetTexture("_BaseMap", spriteTex);
            }

            mat.renderQueue = (int)RenderQueue.Transparent;

            if (additive)
            {
                mat.SetColor("_BaseColor", new Color(0.76f, 1.00f, 0.32f, 0.92f));
                mat.SetColor("_EmissionColor", new Color(0.45f, 1.65f, 0.18f, 1f));
                mat.SetFloat("_EdgeSoftness", 0.80f);
                mat.SetFloat("_DitherFade", 0.70f);
                mat.SetFloat("_AlphaLevels", 8f);
                mat.SetFloat("_AlphaCutoff", 0.04f);
                mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                mat.SetFloat("_DstBlend", (float)BlendMode.One);
                mat.SetFloat("_AdditiveMode", 1f);
            }
            else
            {
                mat.SetColor("_BaseColor", new Color(0.54f, 0.96f, 0.32f, 0.72f));
                mat.SetColor("_EmissionColor", new Color(0.14f, 0.55f, 0.07f, 1f));
                mat.SetFloat("_EdgeSoftness", 1.20f);
                mat.SetFloat("_DitherFade", 0.88f);
                mat.SetFloat("_AlphaLevels", 12f);
                mat.SetFloat("_AlphaCutoff", 0.02f);
                mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                mat.SetFloat("_AdditiveMode", 0f);
            }

            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static void SetupAcidLakeAndWaterfalls(Material activeAcidMat)
        {
            MeshRenderer lakeRenderer = null;
            var waterfallRenderers = new List<Renderer>();

            MeshRenderer[] allRenderers = UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include);
            for (int i = 0; i < allRenderers.Length; i++)
            {
                MeshRenderer mr = allRenderers[i];
                if (mr == null) continue;

                string goName = mr.gameObject.name;
                if (string.Equals(goName, "Abyss_Acid_Lake", StringComparison.OrdinalIgnoreCase))
                {
                    lakeRenderer = mr;
                    if (activeAcidMat != null)
                    {
                        mr.sharedMaterial = activeAcidMat;
                        mr.shadowCastingMode = ShadowCastingMode.Off;
                        EditorUtility.SetDirty(mr);
                    }
                }
                else if (goName.StartsWith("Waterfall_Acid_Sheet", StringComparison.OrdinalIgnoreCase)
                      || goName.StartsWith("Waterfall_BloodAcid_Foam", StringComparison.OrdinalIgnoreCase)
                      || goName.StartsWith("Waterfall_Splash_Ring", StringComparison.OrdinalIgnoreCase))
                {
                    waterfallRenderers.Add(mr);

                    // Add AcidScroll to vertical sheets & foam without vertical bobbing so they stay pinned to rock spouts
                    if (!goName.StartsWith("Waterfall_Splash_Ring", StringComparison.OrdinalIgnoreCase))
                    {
                        AcidScroll scroll = mr.GetComponent<AcidScroll>();
                        if (scroll == null) scroll = mr.gameObject.AddComponent<AcidScroll>();
                        scroll.scrollSpeed = new Vector2(0.01f, 0.34f);
                        scroll.pulseAmplitude = 0f;
                        EditorUtility.SetDirty(scroll);
                    }
                }
            }

            if (lakeRenderer != null)
            {
                AbyssAcidMotionController motion = lakeRenderer.GetComponent<AbyssAcidMotionController>();
                if (motion == null) motion = lakeRenderer.gameObject.AddComponent<AbyssAcidMotionController>();
                motion.SetTargets(lakeRenderer, waterfallRenderers);
                EditorUtility.SetDirty(motion);
            }
            else
            {
                Debug.LogWarning("[AbyssAtmosphereInstaller] Could not find Abyss_Acid_Lake MeshRenderer in current scene.");
            }
        }

        private static VolumeProfile CreateOrUpdateVolumeProfile()
        {
            VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumeProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, VolumeProfilePath);
            }

            Bloom bloom = GetOrAddVolumeComponent<Bloom>(profile);
            bloom.active = true;
            bloom.threshold.Override(0.92f);
            bloom.intensity.Override(0.48f);
            bloom.scatter.Override(0.62f);
            bloom.highQualityFiltering.Override(true);
            EditorUtility.SetDirty(bloom);

            Tonemapping tonemapping = GetOrAddVolumeComponent<Tonemapping>(profile);
            tonemapping.active = true;
            tonemapping.mode.Override(TonemappingMode.ACES);
            EditorUtility.SetDirty(tonemapping);

            Vignette vignette = GetOrAddVolumeComponent<Vignette>(profile);
            vignette.active = true;
            vignette.intensity.Override(0.22f);
            vignette.smoothness.Override(0.32f);
            vignette.color.Override(new Color(0.03f, 0.01f, 0.015f, 1f));
            EditorUtility.SetDirty(vignette);

            ColorAdjustments colorAdj = GetOrAddVolumeComponent<ColorAdjustments>(profile);
            colorAdj.active = true;
            colorAdj.contrast.Override(8f);
            colorAdj.saturation.Override(6f);
            EditorUtility.SetDirty(colorAdj);

            EditorUtility.SetDirty(profile);
            return profile;
        }

        private static T GetOrAddVolumeComponent<T>(VolumeProfile profile) where T : VolumeComponent
        {
            if (!profile.TryGet<T>(out T comp) || comp == null)
            {
                comp = profile.Add<T>(true);
            }

            comp.hideFlags = HideFlags.HideInHierarchy | HideFlags.HideInInspector;
            if (!AssetDatabase.Contains(comp))
            {
                AssetDatabase.AddObjectToAsset(comp, profile);
            }
            return comp;
        }

        private static void SetupVolumeAndAtmosphericFog(Transform rootGroup, VolumeProfile profile)
        {
            // Disable StomachSilentHillFog in Stomach_Abyss if present so it cannot overwrite RenderSettings.fog
            // with heavy 0.018 density fog or clip the Cathedral at 23.5m.
            StomachSilentHillFog[] fogControllers = UnityEngine.Object.FindObjectsByType<StomachSilentHillFog>(FindObjectsInactive.Include);
            for (int i = 0; i < fogControllers.Length; i++)
            {
                StomachSilentHillFog fogCtrl = fogControllers[i];
                if (fogCtrl == null) continue;
                fogCtrl.enableFog = false;
                fogCtrl.ClearShaderGlobals();
                fogCtrl.enabled = false;
                EditorUtility.SetDirty(fogCtrl);
                EditorUtility.SetDirty(fogCtrl.gameObject);
            }

            // Clear any residual StomachFog shader globals
            Shader.SetGlobalVector("_StomachFogPlayerPos", Vector4.zero);
            Shader.SetGlobalVector("_StomachFogRadii", Vector4.zero);
            Shader.SetGlobalVector("_StomachFogParams", Vector4.zero);
            Shader.SetGlobalVector("_StomachFogAnim", Vector4.zero);

            // Global URP Volume
            GameObject volGo = GetOrCreateChild(rootGroup, "GlobalVolume_StomachAbyss", clearChildren: false);
            Volume vol = volGo.GetComponent<Volume>();
            if (vol == null) vol = volGo.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.priority = 1f;
            vol.weight = 1f;
            vol.sharedProfile = profile;
            EditorUtility.SetDirty(vol);

            // Ensure Main Camera enables URP post-processing and HDR
            Camera mainCam = Camera.main;
            if (mainCam == null) mainCam = UnityEngine.Object.FindAnyObjectByType<Camera>();
            if (mainCam != null)
            {
                mainCam.allowHDR = true;
                mainCam.clearFlags = CameraClearFlags.SolidColor;
                mainCam.backgroundColor = new Color(0.048f, 0.028f, 0.022f, 1f);

                UniversalAdditionalCameraData camData = mainCam.GetComponent<UniversalAdditionalCameraData>();
                if (camData == null) camData = mainCam.gameObject.AddComponent<UniversalAdditionalCameraData>();
                camData.renderPostProcessing = true;
                EditorUtility.SetDirty(mainCam);
                EditorUtility.SetDirty(camData);
            }

            // Subtle dark visceral crimson-green atmospheric fog keeping Cathedral razor-clear from Z=196
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = new Color(0.055f, 0.042f, 0.030f, 1f);
            RenderSettings.fogDensity = 0.0045f;

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.40f, 0.16f, 0.14f, 1f);
            RenderSettings.ambientEquatorColor = new Color(0.28f, 0.14f, 0.12f, 1f);
            RenderSettings.ambientGroundColor = new Color(0.14f, 0.32f, 0.10f, 1f);
        }

        private static void SetupToxicVaporFields(Transform rootGroup, Material vaporMat, Material dropletMat)
        {
            GameObject group = GetOrCreateChild(rootGroup, "Toxic_Vapor_Fields", clearChildren: true);

            // 1. Boss Dome / Viaduct Acid Basin (rising tall plumes visible from bridge camera)
            SpawnVaporZone(group.transform, "Vapor_BossViaduct_BasinMain",
                new Vector3(0f, 2.0f, 278f), new Vector3(88f, 2.0f, 130f),
                VaporZonePreset.BossViaductPlumes, vaporMat);

            SpawnVaporZone(group.transform, "Vapor_BossViaduct_WestGorge",
                new Vector3(-24f, 18.0f, 268f), new Vector3(26f, 6.0f, 95f),
                VaporZonePreset.BossViaductPlumes, vaporMat, new Color(0.46f, 0.90f, 0.24f, 1f));

            SpawnVaporZone(group.transform, "Vapor_BossViaduct_EastGorge",
                new Vector3(24f, 18.0f, 268f), new Vector3(26f, 6.0f, 95f),
                VaporZonePreset.BossViaductPlumes, vaporMat, new Color(0.46f, 0.90f, 0.24f, 1f));

            // 2. Lower Arena Lake Mist & Waterfall Spray
            SpawnVaporZone(group.transform, "Vapor_LowerArena_LakeMist",
                new Vector3(0f, 1.0f, 4f), new Vector3(108f, 1.5f, 128f),
                VaporZonePreset.LowerArenaLakeMist, vaporMat);

            SpawnVaporZone(group.transform, "Vapor_Waterfall_WestSpray",
                new Vector3(-53.0f, 2.2f, -14.0f), new Vector3(9f, 5.0f, 12f),
                VaporZonePreset.WaterfallSpray, vaporMat);

            SpawnVaporZone(group.transform, "Vapor_Waterfall_EastSpray",
                new Vector3(53.0f, 2.2f, 30.0f), new Vector3(9f, 5.0f, 12f),
                VaporZonePreset.WaterfallSpray, vaporMat);

            // 3. Cathedral Side Acid Channel Fumes (X = -13.6 and +14.77, Y = 55.6, Z = 363..388)
            SpawnVaporZone(group.transform, "Vapor_Cathedral_Channel_West",
                new Vector3(-13.6f, 55.6f, 375.5f), new Vector3(2.2f, 0.5f, 24f),
                VaporZonePreset.CathedralAcidChannels, vaporMat);

            SpawnVaporZone(group.transform, "Vapor_Cathedral_Channel_East",
                new Vector3(14.77f, 55.6f, 375.5f), new Vector3(2.2f, 0.5f, 24f),
                VaporZonePreset.CathedralAcidChannels, vaporMat);

            // 4. Boiling Acid Droplets popping off acid surface with gravity
            SpawnVaporZone(group.transform, "Droplets_BossBasin_Surface",
                new Vector3(0f, 0.35f, 276f), new Vector3(82f, 0.4f, 120f),
                VaporZonePreset.BoilingAcidDroplets, dropletMat);

            SpawnVaporZone(group.transform, "Droplets_LowerLake_Surface",
                new Vector3(0f, 0.35f, 4f), new Vector3(96f, 0.4f, 120f),
                VaporZonePreset.BoilingAcidDroplets, dropletMat);
        }

        private static void SpawnVaporZone(
            Transform parent,
            string name,
            Vector3 worldPos,
            Vector3 boxSize,
            VaporZonePreset preset,
            Material mat,
            Color? overrideTint = null)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = worldPos;

            go.AddComponent<ParticleSystem>();
            AcidToxicVaporField field = go.AddComponent<AcidToxicVaporField>();
            field.Configure(preset, boxSize, mat, overrideTint);
            EditorUtility.SetDirty(go);
        }

        private static void SetupCausticUnderglowLights(Transform rootGroup)
        {
            GameObject group = GetOrCreateChild(rootGroup, "Caustic_Underglow_Lights", clearChildren: true);

            var causticLights = new List<Light>();

            // Collect existing scene acid lights
            Light[] allLights = UnityEngine.Object.FindObjectsByType<Light>(FindObjectsInactive.Include);
            for (int i = 0; i < allLights.Length; i++)
            {
                Light l = allLights[i];
                if (l == null || l.type != LightType.Point) continue;

                string n = l.gameObject.name;
                if (n.StartsWith("UnderBridge_AcidGlow_", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(n, "Light_AcidLake_UnderVeinBridge", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(n, "Light_AcidGlow_SouthBridge", StringComparison.OrdinalIgnoreCase)
                    || n.StartsWith("AcidChannel_Glow_", StringComparison.OrdinalIgnoreCase))
                {
                    causticLights.Add(l);
                }
            }

            // Spawn subterranean caustic bounce lights under Viaduct, Cathedral foundations, and Monuments
            causticLights.Add(SpawnPointLight(group.transform, "CausticBounce_Viaduct_South",
                new Vector3(6f, 42f, 222f), new Color(0.34f, 1.00f, 0.22f), 38f, 48f));
            causticLights.Add(SpawnPointLight(group.transform, "CausticBounce_Viaduct_MidSouth",
                new Vector3(-8f, 40f, 252f), new Color(0.26f, 1.00f, 0.30f), 42f, 52f));
            causticLights.Add(SpawnPointLight(group.transform, "CausticBounce_Viaduct_MidNorth",
                new Vector3(8f, 38f, 286f), new Color(0.36f, 1.00f, 0.20f), 44f, 54f));
            causticLights.Add(SpawnPointLight(group.transform, "CausticBounce_Viaduct_North",
                new Vector3(-4f, 42f, 318f), new Color(0.28f, 1.00f, 0.28f), 40f, 50f));

            causticLights.Add(SpawnPointLight(group.transform, "CausticBounce_Cathedral_FacadeBase",
                new Vector3(0.69f, 46f, 340f), new Color(0.38f, 1.00f, 0.22f), 36f, 46f));
            causticLights.Add(SpawnPointLight(group.transform, "CausticBounce_Cathedral_FlankWest",
                new Vector3(-20f, 48f, 366f), new Color(0.24f, 1.00f, 0.32f), 30f, 38f));
            causticLights.Add(SpawnPointLight(group.transform, "CausticBounce_Cathedral_FlankEast",
                new Vector3(21f, 48f, 366f), new Color(0.24f, 1.00f, 0.32f), 30f, 38f));

            causticLights.Add(SpawnPointLight(group.transform, "CausticBounce_Lower_VeinBridgeUnderglow",
                new Vector3(0f, 2.8f, 0.5f), new Color(0.36f, 1.00f, 0.20f), 34f, 36f));
            causticLights.Add(SpawnPointLight(group.transform, "CausticBounce_Lower_SkullWest",
                new Vector3(-34f, 4.5f, 2.0f), new Color(0.32f, 1.00f, 0.24f), 32f, 34f));
            causticLights.Add(SpawnPointLight(group.transform, "CausticBounce_Upper_SkullWest",
                new Vector3(-34f, 4.5f, 243.8f), new Color(0.32f, 1.00f, 0.24f), 36f, 38f));

            AcidCausticUnderglow causticController = group.GetComponent<AcidCausticUnderglow>();
            if (causticController == null) causticController = group.AddComponent<AcidCausticUnderglow>();
            causticController.SetTargetLights(causticLights);
            EditorUtility.SetDirty(causticController);
        }

        private static void SetupMembranePodPulses(Transform rootGroup)
        {
            GameObject group = GetOrCreateChild(rootGroup, "Membrane_Pod_Pulses", clearChildren: true);

            // Spawn 4 dedicated warm pulsing orange point lights tucked near cupula chefe flanking wall alveoli alcoves
            Light podLightNorthWest = SpawnPointLight(group.transform, "PodLight_Cupula_NorthWest",
                new Vector3(-62f, 82f, 423f), new Color(1.00f, 0.44f, 0.14f), 85f, 78f);
            Light podLightNorthEast = SpawnPointLight(group.transform, "PodLight_Cupula_NorthEast",
                new Vector3(62f, 82f, 423f), new Color(1.00f, 0.44f, 0.14f), 85f, 78f);
            Light podLightSouthWest = SpawnPointLight(group.transform, "PodLight_Cupula_SouthFlankWest",
                new Vector3(-62f, 80f, 198f), new Color(1.00f, 0.42f, 0.12f), 65f, 68f);
            Light podLightSouthEast = SpawnPointLight(group.transform, "PodLight_Cupula_SouthFlankEast",
                new Vector3(62f, 80f, 198f), new Color(1.00f, 0.42f, 0.12f), 65f, 68f);

            Light ceilingMemSouth = FindLightByName("Light_CeilingMembrane_South");
            Light ceilingMemNorth = FindLightByName("Light_CeilingMembrane_North");

            MeshRenderer[] allRenderers = UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include);
            int podIndex = 0;

            for (int i = 0; i < allRenderers.Length; i++)
            {
                MeshRenderer mr = allRenderers[i];
                if (mr == null) continue;

                bool isMembraneMat = mr.sharedMaterial != null
                    && mr.sharedMaterial.name.IndexOf("MembraneBacklit", StringComparison.OrdinalIgnoreCase) >= 0;
                bool isAlveoliObj = mr.gameObject.name.IndexOf("Alveoli", StringComparison.OrdinalIgnoreCase) >= 0;

                if (!isMembraneMat && !isAlveoliObj) continue;

                MembranePodPulse pulse = mr.GetComponent<MembranePodPulse>();
                if (pulse == null) pulse = mr.gameObject.AddComponent<MembranePodPulse>();

                string hierarchyPath = GetHierarchyPath(mr.transform);
                var linkedLights = new List<Light>();

                if (hierarchyPath.IndexOf("cupula chefe", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    if (hierarchyPath.IndexOf("Cavern_Wall_North", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        linkedLights.Add(podLightNorthWest);
                        linkedLights.Add(podLightNorthEast);
                    }
                    else if (hierarchyPath.IndexOf("Cavern_Wall_South", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        linkedLights.Add(podLightSouthWest);
                        linkedLights.Add(podLightSouthEast);
                    }
                }
                else if (mr.gameObject.name.IndexOf("Ceiling_Backlit_Alveoli", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    if (ceilingMemSouth != null) linkedLights.Add(ceilingMemSouth);
                    if (ceilingMemNorth != null) linkedLights.Add(ceilingMemNorth);
                }

                float staggeredPhase = (podIndex * 0.23f) % 1f;
                pulse.Configure(mr, linkedLights, staggeredPhase);
                EditorUtility.SetDirty(pulse);
                podIndex++;
            }
        }

        private static Light SpawnPointLight(Transform parent, string name, Vector3 worldPos, Color color, float intensity, float range)
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

        private static Light FindLightByName(string objectName)
        {
            Light[] lights = UnityEngine.Object.FindObjectsByType<Light>(FindObjectsInactive.Include);
            for (int i = 0; i < lights.Length; i++)
            {
                if (lights[i] != null && string.Equals(lights[i].gameObject.name, objectName, StringComparison.OrdinalIgnoreCase))
                {
                    return lights[i];
                }
            }
            return null;
        }

        private static GameObject GetOrCreateSceneRoot(Scene scene, string rootName)
        {
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                if (roots[i] != null && string.Equals(roots[i].name, rootName, StringComparison.Ordinal))
                {
                    return roots[i];
                }
            }

            GameObject go = new GameObject(rootName);
            SceneManager.MoveGameObjectToScene(go, scene);
            return go;
        }

        private static GameObject GetOrCreateChild(Transform parent, string childName, bool clearChildren)
        {
            Transform existing = parent.Find(childName);
            GameObject go;
            if (existing != null)
            {
                go = existing.gameObject;
                if (clearChildren)
                {
                    for (int i = go.transform.childCount - 1; i >= 0; i--)
                    {
                        UnityEngine.Object.DestroyImmediate(go.transform.GetChild(i).gameObject);
                    }
                }
            }
            else
            {
                go = new GameObject(childName);
                go.transform.SetParent(parent, false);
            }
            return go;
        }

        private static string GetHierarchyPath(Transform t)
        {
            if (t == null) return string.Empty;
            string path = t.name;
            Transform cur = t.parent;
            while (cur != null)
            {
                path = cur.name + "/" + path;
                cur = cur.parent;
            }
            return path;
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
