#pragma warning disable 0618
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace MapaEstomago.Editor
{
    public static class CrossRazorSwordSetup
    {
        private const string TexturesFolder = "Assets/PilotChamber/Textures";
        private const string MaterialsFolder = "Assets/PilotChamber/Materials";
        private const string PrefabsFolder = "Assets/PilotChamber/Prefabs";
        private const string SwordGlbPath = "Assets/PilotChamber/Cross_Razor_Greatsword.glb";
        private const string SwordPrefabPath = "Assets/PilotChamber/Prefabs/Cross_Razor_Greatsword.prefab";

        private static readonly string[] SwordTextureFiles = new[]
        {
            "tex_cross_razor_hilt.png",
            "tex_razor_steel.png"
        };

        [MenuItem("Estomago/Armas/Configurar Espada Cruz no Jogador", priority = 25)]
        public static void SetupCrossRazorSwordOnPlayer()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ConfigureSwordTextures();
            Dictionary<string, Material> mats = CreateOrUpdateSwordMaterials();

            AssetImporter importer = AssetImporter.GetAtPath(SwordGlbPath);
            if (importer != null) importer.SaveAndReimport();

            GameObject swordPrefab = CreateOrUpdateSwordPrefab(mats);
            if (swordPrefab == null)
            {
                Debug.LogError("[CrossRazorSwordSetup] Nao foi possivel criar o Prefab da Espada!");
                return;
            }

            GameObject player = GameObject.Find("Test_Player");
            if (player == null) player = GameObject.FindWithTag("Player");

            if (player != null)
            {
                AttachSwordToPlayer(player, swordPrefab, mats["Mat_Razor_Steel"]);
            }
            else
            {
                Debug.LogWarning("[CrossRazorSwordSetup] Test_Player nao encontrado na cena; Prefab criado apenas no projeto.");
            }

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveOpenScenes();
            Debug.Log("<color=#66DDFF><b>[CrossRazorSwordSetup]</b> Espada Cruz + 3 Laminas Flutuantes configurada com sucesso!</color>");
        }

        private static void ConfigureSwordTextures()
        {
            foreach (string file in SwordTextureFiles)
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
                    if (changed) ti.SaveAndReimport();
                }
            }
        }

        private static Dictionary<string, Material> CreateOrUpdateSwordMaterials()
        {
            Shader fleshShader = Shader.Find("MapaEstomago/URP/PS1_LivingFlesh");
            if (fleshShader == null) fleshShader = Shader.Find("Universal Render Pipeline/Lit");

            var dict = new Dictionary<string, Material>();

            dict["Mat_Cross_Razor_Hilt"] = CreateOrUpdateMat("Mat_Cross_Razor_Hilt", fleshShader, "tex_cross_razor_hilt.png", m =>
            {
                m.SetColor("_BaseColor", Color.white);
                m.SetFloat("_VertexSnap", 240f);
                m.SetFloat("_BreathSpeed", 0f);
                m.SetFloat("_BreathAmp", 0f);
                m.SetFloat("_SplitAmount", 0f);
                m.SetFloat("_Wetness", 0.35f);
                m.SetFloat("_AmbientFloor", 0.52f);
                m.SetColor("_EmissionColor", Color.black);
                m.SetFloat("_UseVertexColor", 1f);
                m.SetFloat("_EnableBG3Cutout", 0f); // Espada do jogador nunca sofre recorte
            });

            dict["Mat_Razor_Steel"] = CreateOrUpdateMat("Mat_Razor_Steel", fleshShader, "tex_razor_steel.png", m =>
            {
                m.SetColor("_BaseColor", Color.white);
                m.SetFloat("_VertexSnap", 260f);
                m.SetFloat("_BreathSpeed", 0f);
                m.SetFloat("_BreathAmp", 0f);
                m.SetFloat("_SplitAmount", 0f);
                m.SetFloat("_Wetness", 0.65f);
                m.SetFloat("_AmbientFloor", 0.58f);
                m.SetColor("_EmissionColor", new Color(0.03f, 0.035f, 0.045f, 1f));
                m.SetFloat("_UseVertexColor", 1f);
                m.SetFloat("_EnableBG3Cutout", 0f);
            });

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
            if (tex != null) mat.SetTexture("_BaseMap", tex);

            configure?.Invoke(mat);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static GameObject CreateOrUpdateSwordPrefab(Dictionary<string, Material> mats)
        {
            GameObject glb = AssetDatabase.LoadAssetAtPath<GameObject>(SwordGlbPath);
            if (glb == null) return null;

            if (!AssetDatabase.IsValidFolder(PrefabsFolder))
            {
                AssetDatabase.CreateFolder("Assets/PilotChamber", "Prefabs");
            }

            GameObject temp = (GameObject)PrefabUtility.InstantiatePrefab(glb);
            temp.name = "Cross_Razor_Greatsword";
            if (PrefabUtility.IsPartOfAnyPrefab(temp))
            {
                PrefabUtility.UnpackPrefabInstance(temp, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            }

            foreach (MeshRenderer mr in temp.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (mr.gameObject.name.Contains("Cross"))
                {
                    mr.sharedMaterial = mats["Mat_Cross_Razor_Hilt"];
                }
                else
                {
                    mr.sharedMaterial = mats["Mat_Razor_Steel"];
                }
            }

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(temp, SwordPrefabPath);
            UnityEngine.Object.DestroyImmediate(temp);
            return saved;
        }

        private static void AttachSwordToPlayer(GameObject player, GameObject swordPrefab, Material razorMat)
        {
            Transform mountTf = player.transform.Find("Weapon_Mount_Pivot");
            if (mountTf == null)
            {
                GameObject mount = new GameObject("Weapon_Mount_Pivot");
                Undo.RegisterCreatedObjectUndo(mount, "Criar Weapon_Mount_Pivot");
                mountTf = mount.transform;
                mountTf.SetParent(player.transform, false);
                mountTf.localPosition = new Vector3(0.82f, 1.55f, -0.05f);
                mountTf.localRotation = Quaternion.identity;
            }

            Transform oldSword = mountTf.Find("Cross_Razor_Greatsword");
            if (oldSword != null)
            {
                Undo.DestroyObjectImmediate(oldSword.gameObject);
            }

            GameObject swordInst = (GameObject)PrefabUtility.InstantiatePrefab(swordPrefab, mountTf);
            swordInst.name = "Cross_Razor_Greatsword";
            swordInst.transform.localPosition = Vector3.zero;
            swordInst.transform.localRotation = Quaternion.Euler(34f, -18f, -36f);
            swordInst.transform.localScale = Vector3.one * 0.80f;

            CrossRazorGreatswordController ctrl = player.GetComponent<CrossRazorGreatswordController>();
            if (ctrl == null) ctrl = Undo.AddComponent<CrossRazorGreatswordController>(player);
            ctrl.weaponMount = mountTf;
            ctrl.swordRoot = swordInst.transform;
            ctrl.razorBladeMaterial = razorMat;
            ctrl.razorSlots = new Transform[]
            {
                swordInst.transform.Find("Razor_01_Base"),
                swordInst.transform.Find("Razor_02_Mid"),
                swordInst.transform.Find("Razor_03_Tip"),
            };
            ctrl.ResolveSwordHierarchy();

            // Registra no PlayerWeaponArsenal preservando o Chicote Mini Tenia se ja estiver presente
            PlayerWeaponArsenal arsenal = player.GetComponent<PlayerWeaponArsenal>();
            if (arsenal == null) arsenal = Undo.AddComponent<PlayerWeaponArsenal>(player);
            arsenal.RegisterOrUpdateSlot("cross_razor", "Espada Cruz", ctrl, swordInst, unlocked: true, equipNow: true);

            TeniaWhipWeapon whip = player.GetComponent<TeniaWhipWeapon>();
            if (whip != null)
            {
                whip.weaponMount = mountTf;
                GameObject whipVis = whip.whipRoot != null ? whip.whipRoot.gameObject : mountTf.Find("Tenia_Whip")?.gameObject;
                arsenal.RegisterOrUpdateSlot("tenia_whip", "Mini Tenia", whip, whipVis, unlocked: true, equipNow: false);
            }

            LaminaArdenteWeapon ardente = player.GetComponent<LaminaArdenteWeapon>();
            if (ardente != null)
            {
                ardente.weaponMount = mountTf;
                ardente.ResolveSwordHierarchy();
                GameObject ardenteVis = ardente.swordRoot != null ? ardente.swordRoot.gameObject : mountTf.Find("Lamina_Ardente")?.gameObject;
                arsenal.RegisterOrUpdateSlot("lamina_ardente", "Lamina Ardente", ardente, ardenteVis, unlocked: true, equipNow: false);
            }

            arsenal.ApplyCurrentSlotImmediate();
            EditorUtility.SetDirty(arsenal);
            EditorUtility.SetDirty(player);
        }
    }
}
