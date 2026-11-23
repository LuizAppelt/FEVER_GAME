using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace StomachAbyss.Combat.Editor
{
    /// <summary>
    /// Imports PS1 blood textures & combat SFX WAV files with authentic settings,
    /// then finds or creates "Combat_Juice_Systems" in the active scene with
    /// CombatHitstopManager, BloodSplatterSystem, and CombatSfxBank serialized.
    /// </summary>
    public static class CombatPolishInstaller
    {
        private const string SystemObjectName = "Combat_Juice_Systems";

        private static readonly string[] SplatTexturePaths =
        {
            "Assets/StomachAbyss/Combat/Textures/tex_blood_splat_01.png",
            "Assets/StomachAbyss/Combat/Textures/tex_blood_splat_02.png",
            "Assets/StomachAbyss/Combat/Textures/tex_blood_splat_03.png",
            "Assets/StomachAbyss/Combat/Textures/tex_blood_splat_04.png"
        };

        private static readonly string[] SfxFileNames =
        {
            "SFX_Sword_Slash_Light.wav",
            "SFX_Sword_Slam_Heavy.wav",
            "SFX_Cross_Thrust.wav",
            "SFX_Razor_Throw.wav",
            "SFX_Razor_Return_Lock.wav",
            "SFX_Hit_Flesh_Cleave.wav",
            "SFX_Hit_Weakspot_Crit.wav",
            "SFX_Player_Dash.wav",
            "SFX_Player_Jump.wav",
            "SFX_Player_Hurt.wav",
            "SFX_Blood_Splatter.wav"
        };

        [MenuItem("Estomago/Combate/Instalar Combat Juice e Audio na Cena", priority = 30)]
        public static void InstallInCurrentScene()
        {
            ImportTexturesAndAudio();

            Scene activeScene = SceneManager.GetActiveScene();
            if (!activeScene.IsValid())
            {
                Debug.LogWarning("[CombatPolishInstaller] Nenhuma cena ativa valida encontrada.");
                return;
            }

            GameObject root = GameObject.Find(SystemObjectName);
            if (root == null)
            {
                root = new GameObject(SystemObjectName);
                Undo.RegisterCreatedObjectUndo(root, "Create Combat_Juice_Systems");
            }

            CombatHitstopManager hitstop = EnsureComponent<CombatHitstopManager>(root);
            BloodSplatterSystem blood = EnsureComponent<BloodSplatterSystem>(root);
            CombatSfxBank sfx = EnsureComponent<CombatSfxBank>(root);

            // Serialize the 4 PS1 blood textures cleanly
            if (blood.splatTextures == null || blood.splatTextures.Length != 4)
            {
                blood.splatTextures = new Texture2D[4];
            }
            for (int i = 0; i < SplatTexturePaths.Length; i++)
            {
                Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(SplatTexturePaths[i]);
                if (tex != null)
                {
                    blood.splatTextures[i] = tex;
                }
            }
            EditorUtility.SetDirty(blood);

            // Serialize all 11 synthesized combat AudioClips cleanly
            sfx.swordSlashLight = LoadSfxClip(SfxFileNames[0]);
            sfx.swordSlamHeavy = LoadSfxClip(SfxFileNames[1]);
            sfx.crossThrust = LoadSfxClip(SfxFileNames[2]);
            sfx.razorThrow = LoadSfxClip(SfxFileNames[3]);
            sfx.razorReturnLock = LoadSfxClip(SfxFileNames[4]);
            sfx.hitFleshCleave = LoadSfxClip(SfxFileNames[5]);
            sfx.hitWeakspotCrit = LoadSfxClip(SfxFileNames[6]);
            sfx.playerDash = LoadSfxClip(SfxFileNames[7]);
            sfx.playerJump = LoadSfxClip(SfxFileNames[8]);
            sfx.playerHurt = LoadSfxClip(SfxFileNames[9]);
            sfx.bloodSplatter = LoadSfxClip(SfxFileNames[10]);
            EditorUtility.SetDirty(sfx);

            EditorUtility.SetDirty(hitstop);
            EditorSceneManager.MarkSceneDirty(activeScene);

            Debug.Log("[CombatPolishInstaller] Combat_Juice_Systems instalado e configurado com sucesso na cena atual.");
        }

        private static void ImportTexturesAndAudio()
        {
            for (int i = 0; i < SplatTexturePaths.Length; i++)
            {
                string path = SplatTexturePaths[i];
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                TextureImporter ti = AssetImporter.GetAtPath(path) as TextureImporter;
                if (ti == null) continue;

                bool changed = false;
                if (ti.textureType != TextureImporterType.Default) { ti.textureType = TextureImporterType.Default; changed = true; }
                if (!ti.alphaIsTransparency) { ti.alphaIsTransparency = true; changed = true; }
                if (ti.filterMode != FilterMode.Point) { ti.filterMode = FilterMode.Point; changed = true; }
                if (ti.wrapMode != TextureWrapMode.Clamp) { ti.wrapMode = TextureWrapMode.Clamp; changed = true; }
                if (ti.mipmapEnabled) { ti.mipmapEnabled = false; changed = true; }
                if (ti.textureCompression != TextureImporterCompression.Uncompressed) { ti.textureCompression = TextureImporterCompression.Uncompressed; changed = true; }

                if (changed)
                {
                    ti.SaveAndReimport();
                }
            }

            for (int i = 0; i < SfxFileNames.Length; i++)
            {
                string path = $"{CombatSfxBank.SfxFolder}/{SfxFileNames[i]}";
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                AudioImporter ai = AssetImporter.GetAtPath(path) as AudioImporter;
                if (ai == null) continue;

                bool changed = false;
                if (!ai.forceToMono) { ai.forceToMono = true; changed = true; }
                if (ai.loadInBackground) { ai.loadInBackground = false; changed = true; }

                AudioImporterSampleSettings settings = ai.defaultSampleSettings;
                if (settings.loadType != AudioClipLoadType.DecompressOnLoad)
                {
                    settings.loadType = AudioClipLoadType.DecompressOnLoad;
                    settings.compressionFormat = AudioCompressionFormat.PCM;
                    ai.defaultSampleSettings = settings;
                    changed = true;
                }

                if (changed)
                {
                    ai.SaveAndReimport();
                }
            }
        }

        private static AudioClip LoadSfxClip(string fileName)
        {
            return AssetDatabase.LoadAssetAtPath<AudioClip>($"{CombatSfxBank.SfxFolder}/{fileName}");
        }

        private static T EnsureComponent<T>(GameObject go) where T : Component
        {
            T comp = go.GetComponent<T>();
            if (comp == null)
            {
                comp = Undo.AddComponent<T>(go);
            }
            return comp;
        }
    }
}
