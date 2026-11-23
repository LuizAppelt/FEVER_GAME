using System;
using System.Collections.Generic;
using System.IO;
using BossTenia;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace StomachAbyss.Cathedral.Editor
{
    /// <summary>
    /// Installs and wires the Dramatic Tendon-Bone Cathedral Gate and Dark Souls 3 Pipe Organ
    /// Boss Music Director inside the currently open Stomach_Abyss scene.
    /// </summary>
    public static class CathedralDramaticInstaller
    {
        private const string AudioFolder = "Assets/StomachAbyss/Audio/Music";
        private const string MeshesFolder = "Assets/StomachAbyss/Cathedral/Meshes";
        private const string MaterialsFolder = "Assets/StomachAbyss/Cathedral/Materials";
        private const string ParticleMatPath = MaterialsFolder + "/Mat_CathedralGate_BloodDust.mat";

        private const string GateSlamAudioPath = AudioFolder + "/SFX_Cathedral_Gate_Slam.wav";
        private const string IntroOrganAudioPath = AudioFolder + "/SFX_Organ_Cathedral_IntroBlast.wav";
        private const string BossLoopAudioPath = AudioFolder + "/Music_Boss_Malphas_DS3.wav";

        [MenuItem("Estomago/Cathedral/Install Gate & Pipe Organ Boss Music", priority = 30)]
        public static void InstallInCurrentSceneMenu()
        {
            InstallInCurrentScene();
        }

        public static void InstallInCurrentScene()
        {
            EnsureFolderHierarchy("Assets/StomachAbyss/Cathedral");
            EnsureFolderHierarchy(MeshesFolder);
            EnsureFolderHierarchy(MaterialsFolder);
            EnsureFolderHierarchy(AudioFolder);

            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);

            ConfigureAudioImporter(GateSlamAudioPath, AudioClipLoadType.DecompressOnLoad, AudioCompressionFormat.PCM, 1.0f);
            ConfigureAudioImporter(IntroOrganAudioPath, AudioClipLoadType.DecompressOnLoad, AudioCompressionFormat.Vorbis, 0.92f);
            ConfigureAudioImporter(BossLoopAudioPath, AudioClipLoadType.CompressedInMemory, AudioCompressionFormat.Vorbis, 0.94f);

            AudioClip gateSlamClip = AssetDatabase.LoadAssetAtPath<AudioClip>(GateSlamAudioPath);
            AudioClip introOrganClip = AssetDatabase.LoadAssetAtPath<AudioClip>(IntroOrganAudioPath);
            AudioClip bossLoopClip = AssetDatabase.LoadAssetAtPath<AudioClip>(BossLoopAudioPath);

            GameObject cathedralRoot = FindSceneObject("Stomach_Boss_Cathedral (1)", "Stomach_Boss_Cathedral");
            if (cathedralRoot == null)
            {
                throw new InvalidOperationException("[CathedralDramaticInstaller] Could not find Stomach_Boss_Cathedral (1) or Stomach_Boss_Cathedral in the active scene.");
            }

            TeniaBossAI bossAI = UnityEngine.Object.FindAnyObjectByType<TeniaBossAI>();
            GameObject bossGo = bossAI != null
                ? bossAI.gameObject
                : FindSceneObject("Boss_Tenia_Liturgica (1)", "Boss_Tenia_Liturgica");

            TeniaBossHealth bossHealth = bossGo != null ? bossGo.GetComponent<TeniaBossHealth>() : UnityEngine.Object.FindAnyObjectByType<TeniaBossHealth>();
            TeniaArenaBounds arenaBounds = bossGo != null ? bossGo.GetComponent<TeniaArenaBounds>() : UnityEngine.Object.FindAnyObjectByType<TeniaArenaBounds>();
            if (bossAI == null && bossGo != null) bossAI = bossGo.GetComponent<TeniaBossAI>();

            GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
            if (playerObj == null) playerObj = FindSceneObject("Test_Player (1)", "Test_Player");
            Transform playerTransform = playerObj != null ? playerObj.transform : null;

            // Persist clean procedural meshes as assets so scene save preserves them reliably
            Mesh leftWingMesh = SaveOrUpdateMeshAsset(CathedralGateDramaticTrigger.CreateBoneWingMesh(false), MeshesFolder + "/Mesh_CathedralGate_LeftWing.asset");
            Mesh rightWingMesh = SaveOrUpdateMeshAsset(CathedralGateDramaticTrigger.CreateBoneWingMesh(true), MeshesFolder + "/Mesh_CathedralGate_RightWing.asset");
            Mesh fangMesh = SaveOrUpdateMeshAsset(CathedralGateDramaticTrigger.CreateCurvedFangMesh(), MeshesFolder + "/Mesh_CathedralGate_CurvedFang.asset");
            Mesh upperBeamMesh = SaveOrUpdateMeshAsset(CathedralGateDramaticTrigger.CreateUpperPortcullisBeamMesh(), MeshesFolder + "/Mesh_CathedralGate_UpperBeam.asset");
            Mesh tendonMesh = SaveOrUpdateMeshAsset(CathedralGateDramaticTrigger.CreateTendonStrandMesh(), MeshesFolder + "/Mesh_CathedralGate_CrimsonTendon.asset");
            Mesh anchorBulbMesh = SaveOrUpdateMeshAsset(CathedralGateDramaticTrigger.CreateAnchorBulbMesh(), MeshesFolder + "/Mesh_CathedralGate_AnchorBulb.asset");
            AssetDatabase.SaveAssets();

            // 1. Setup Cathedral_Dramatic_BoneGate inside Stomach_Boss_Cathedral (1)
            Transform existingGateTf = cathedralRoot.transform.Find("Cathedral_Dramatic_BoneGate");
            GameObject gateGo = existingGateTf != null ? existingGateTf.gameObject : new GameObject("Cathedral_Dramatic_BoneGate");
            if (existingGateTf == null)
            {
                Undo.RegisterCreatedObjectUndo(gateGo, "Create Cathedral_Dramatic_BoneGate");
                gateGo.transform.SetParent(cathedralRoot.transform, false);
            }
            gateGo.transform.localPosition = new Vector3(0f, 0f, 1.50f);
            gateGo.transform.localRotation = Quaternion.identity;
            gateGo.transform.localScale = Vector3.one;

            CathedralGateDramaticTrigger gate = gateGo.GetComponent<CathedralGateDramaticTrigger>();
            if (gate == null) gate = gateGo.AddComponent<CathedralGateDramaticTrigger>();

            gate.cathedralAnchor = cathedralRoot.transform;
            gate.arenaBounds = arenaBounds;
            gate.bossAI = bossAI;
            gate.bossHealth = bossHealth;
            gate.playerTarget = playerTransform;
            gate.gateSlamClip = gateSlamClip;

            gate.teethMaterial = LoadFirstAvailableMaterial(
                "Assets/StomachAbyss/Materials/Mat_Abyss_Teeth.mat",
                "Assets/BossTenia/Materials/Mat_Tenia_Teeth.mat");
            gate.hornedBoneMaterial = LoadFirstAvailableMaterial(
                "Assets/StomachAbyss/Materials/Mat_Abyss_HornedBone.mat",
                "Assets/StomachAbyss/Materials/Mat_Abyss_Teeth.mat");
            gate.ossifiedStoneMaterial = LoadFirstAvailableMaterial(
                "Assets/StomachAbyss/Materials/Mat_Abyss_OssifiedStone.mat",
                "Assets/StomachAbyss/Materials/Mat_Abyss_HornedBone.mat");
            gate.fleshWallMaterial = LoadFirstAvailableMaterial(
                "Assets/StomachAbyss/Materials/Mat_Abyss_FleshWall.mat",
                "Assets/StomachAbyss/Materials/Mat_Abyss_VeinTendon.mat");
            gate.veinTendonMaterial = LoadFirstAvailableMaterial(
                "Assets/StomachAbyss/Materials/Mat_Abyss_VeinTendon.mat",
                "Assets/PilotChamber/Materials/Mat_BloodStrands.mat");
            gate.particleMaterial = EnsureParticleMaterialAsset();

            gate.leftWingMesh = leftWingMesh;
            gate.rightWingMesh = rightWingMesh;
            gate.fangMesh = fangMesh;
            gate.upperBeamMesh = upperBeamMesh;
            gate.tendonMesh = tendonMesh;
            gate.anchorBulbMesh = anchorBulbMesh;

            gate.BuildGateRig();
            EditorUtility.SetDirty(gateGo);
            EditorUtility.SetDirty(gate);

            // Wire TeniaArenaBounds cleanly so it delegates to the dramatic bone gate
            if (arenaBounds != null)
            {
                arenaBounds.cathedralAnchor = cathedralRoot.transform;
                arenaBounds.dramaticGate = gate;
                if (arenaBounds.portalBarrierObject != null &&
                    arenaBounds.portalBarrierObject.name == "CathedralPortalAcidBarrier")
                {
                    arenaBounds.portalBarrierObject.SetActive(false);
                }
                arenaBounds.SyncWithCathedralAnchor(false);
                EditorUtility.SetDirty(arenaBounds);
            }

            // 2. Setup Cathedral_PipeOrgan_MusicDirector inside Stomach_Boss_Cathedral (1)
            Transform existingMusicTf = cathedralRoot.transform.Find("Cathedral_PipeOrgan_MusicDirector");
            GameObject musicGo = existingMusicTf != null ? existingMusicTf.gameObject : new GameObject("Cathedral_PipeOrgan_MusicDirector");
            if (existingMusicTf == null)
            {
                Undo.RegisterCreatedObjectUndo(musicGo, "Create Cathedral_PipeOrgan_MusicDirector");
                musicGo.transform.SetParent(cathedralRoot.transform, false);
            }
            musicGo.transform.localPosition = Vector3.zero;
            musicGo.transform.localRotation = Quaternion.identity;
            musicGo.transform.localScale = Vector3.one;

            CathedralPipeOrganBossMusic music = musicGo.GetComponent<CathedralPipeOrganBossMusic>();
            if (music == null) music = musicGo.AddComponent<CathedralPipeOrganBossMusic>();

            music.introBlastClip = introOrganClip;
            music.bossLoopClip = bossLoopClip;
            music.bossAI = bossAI;
            music.bossHealth = bossHealth;
            music.arenaBounds = arenaBounds;
            music.dramaticGate = gate;
            music.playerTarget = playerTransform;

            Transform organPipesTf = FindChildRecursive(cathedralRoot.transform, "Cathedral_Colossal_Organ_Pipes");
            if (organPipesTf == null)
            {
                GameObject foundOrgan = GameObject.Find("Cathedral_Colossal_Organ_Pipes");
                if (foundOrgan != null) organPipesTf = foundOrgan.transform;
            }
            if (organPipesTf != null)
            {
                music.organPipesTransform = organPipesTf;
                music.organPipesRenderer = organPipesTf.GetComponent<Renderer>();
            }

            music.organReactiveLights = new List<Light>(5);
            AddSceneLightIfPresent(music.organReactiveLights, "PipeOrgan_GoldenUplight_Center");
            AddSceneLightIfPresent(music.organReactiveLights, "PipeOrgan_Bellows_Left");
            AddSceneLightIfPresent(music.organReactiveLights, "PipeOrgan_Bellows_Right");
            AddSceneLightIfPresent(music.organReactiveLights, "Facade_RoseWindow_EyeGlow");
            AddSceneLightIfPresent(music.organReactiveLights, "Sanctuary_BossAltar_CrimsonLight");

            EditorUtility.SetDirty(musicGo);
            EditorUtility.SetDirty(music);

            Scene activeScene = SceneManager.GetActiveScene();
            if (activeScene.IsValid())
            {
                EditorSceneManager.MarkSceneDirty(activeScene);
            }

            Debug.Log(
                $"[CathedralDramaticInstaller] Installed cleanly in '{activeScene.name}'. " +
                $"Gate='{gateGo.name}' (clip={(gateSlamClip != null ? gateSlamClip.name : "MISSING")}), " +
                $"Music='{musicGo.name}' (intro={(introOrganClip != null ? introOrganClip.name : "MISSING")}, " +
                $"loop={(bossLoopClip != null ? bossLoopClip.name : "MISSING")}, " +
                $"organPipes={(organPipesTf != null ? organPipesTf.name : "MISSING")}, " +
                $"reactiveLights={music.organReactiveLights.Count}).");
        }

        private static void ConfigureAudioImporter(
            string assetPath,
            AudioClipLoadType loadType,
            AudioCompressionFormat compressionFormat,
            float quality)
        {
            AudioImporter importer = AssetImporter.GetAtPath(assetPath) as AudioImporter;
            if (importer == null)
            {
                Debug.LogWarning($"[CathedralDramaticInstaller] AudioImporter not found at '{assetPath}'.");
                return;
            }

            bool dirty = false;
            if (importer.forceToMono)
            {
                importer.forceToMono = false;
                dirty = true;
            }

            AudioImporterSampleSettings settings = importer.defaultSampleSettings;
            if (settings.loadType != loadType ||
                settings.compressionFormat != compressionFormat ||
                settings.sampleRateSetting != AudioSampleRateSetting.PreserveSampleRate ||
                (compressionFormat == AudioCompressionFormat.Vorbis && Mathf.Abs(settings.quality - quality) > 0.01f))
            {
                settings.loadType = loadType;
                settings.compressionFormat = compressionFormat;
                settings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
                settings.quality = quality;
                importer.defaultSampleSettings = settings;
                dirty = true;
            }

            if (dirty)
            {
                importer.SaveAndReimport();
            }
        }

        private static Mesh SaveOrUpdateMeshAsset(Mesh runtimeMesh, string assetPath)
        {
            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);
            if (existing != null)
            {
                existing.Clear();
                EditorUtility.CopySerialized(runtimeMesh, existing);
                existing.name = Path.GetFileNameWithoutExtension(assetPath);
                EditorUtility.SetDirty(existing);
                UnityEngine.Object.DestroyImmediate(runtimeMesh);
                return existing;
            }

            runtimeMesh.name = Path.GetFileNameWithoutExtension(assetPath);
            AssetDatabase.CreateAsset(runtimeMesh, assetPath);
            return AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);
        }

        private static Material EnsureParticleMaterialAsset()
        {
            Material existing = AssetDatabase.LoadAssetAtPath<Material>(ParticleMatPath);
            if (existing != null)
            {
                return existing;
            }

            Shader sh = Shader.Find("MapaEstomago/URP/PS1_PixelParticle");
            if (sh == null) sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (sh == null) return null;

            Material mat = new Material(sh) { name = "Mat_CathedralGate_BloodDust" };
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
            if (mat.HasProperty("_EmissionBoost")) mat.SetFloat("_EmissionBoost", 1.35f);
            if (mat.HasProperty("_Cutoff")) mat.SetFloat("_Cutoff", 0.35f);

            AssetDatabase.CreateAsset(mat, ParticleMatPath);
            AssetDatabase.SaveAssets();
            return AssetDatabase.LoadAssetAtPath<Material>(ParticleMatPath);
        }

        private static Material LoadFirstAvailableMaterial(params string[] paths)
        {
            for (int i = 0; i < paths.Length; i++)
            {
                Material m = AssetDatabase.LoadAssetAtPath<Material>(paths[i]);
                if (m != null) return m;
            }
            return null;
        }

        private static GameObject FindSceneObject(params string[] names)
        {
            for (int i = 0; i < names.Length; i++)
            {
                GameObject go = GameObject.Find(names[i]);
                if (go != null) return go;
            }
            return null;
        }

        private static Transform FindChildRecursive(Transform root, string exactName)
        {
            if (root == null) return null;
            Transform[] children = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < children.Length; i++)
            {
                if (children[i] != null && children[i].name == exactName)
                {
                    return children[i];
                }
            }
            return null;
        }

        private static void AddSceneLightIfPresent(List<Light> list, string goName)
        {
            GameObject go = GameObject.Find(goName);
            if (go != null && go.TryGetComponent<Light>(out var lightComp) && !list.Contains(lightComp))
            {
                list.Add(lightComp);
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
