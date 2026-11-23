using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace MapaEstomago.Editor
{
    public static class StomachChamberSetup
    {
        private const string BaseFolder = "Assets/PilotChamber";
        private const string TexturesFolder = BaseFolder + "/Textures";
        private const string MaterialsFolder = BaseFolder + "/Materials";
        private const string AnimationsFolder = BaseFolder + "/Animations";
        private const string GlbPath = BaseFolder + "/Stomach_Pilot_Chamber.glb";
        private const string MainDomeGlbPath = BaseFolder + "/Stomach_Main_Dome.glb";
        private const string DoorGlbPath = BaseFolder + "/Flesh_Eye_Door.glb";
        private const string DoorPrefabPath = BaseFolder + "/Flesh_Eye_Door.prefab";
        private const string VirusGlbPath = BaseFolder + "/Virus_Player.glb";
        private const string VirusPrefabPath = BaseFolder + "/Virus_Player.prefab";
        private const string VirusControllerPath = AnimationsFolder + "/VirusPlayer_Animator.controller";

        [InitializeOnLoadMethod]
        private static void RegisterAutoSetupOnEditMode()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.delayCall += TryEnsureSceneUpgradedInEditMode;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredEditMode)
            {
                EditorApplication.delayCall += TryEnsureSceneUpgradedInEditMode;
            }
        }

        private static void TryEnsureSceneUpgradedInEditMode()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (EditorSceneManager.GetActiveScene().name != "SampleScene") return;
            if (!File.Exists(VirusGlbPath)) return;

            bool changed = false;
            GameObject p = GameObject.Find("Test_Player");
            if (p != null && p.transform.Find("Virus_Model") == null)
            {
                SetupVirusPlayerInScene();
                changed = true;
            }

            if (Object.FindFirstObjectByType<StomachSilentHillFog>(FindObjectsInactive.Include) == null)
            {
                SetupSilentHillStomachFog();
                changed = true;
            }

            Camera mainCam = Camera.main != null ? Camera.main : Object.FindFirstObjectByType<Camera>();
            if (mainCam != null && mainCam.GetComponent<BG3WallCutoutController>() == null)
            {
                SetupDomeAndBG3Camera(silent: true);
                changed = true;
            }

            if (changed)
            {
                EditorSceneManager.SaveOpenScenes();
            }
        }

        [MenuItem("Estomago/Criar Prefab Porta Carne + Olho (URP)")]
        public static void SetupFleshEyeDoorMenu()
        {
            ConfigurePixelTextures();
            if (!AssetDatabase.IsValidFolder(MaterialsFolder))
            {
                AssetDatabase.CreateFolder(BaseFolder, "Materials");
            }

            Material matFleshWall = CreateFleshWallMaterial();
            Material matHellOrgans = CreateHellOrgansMaterial();
            bool ok = BuildOrUpdateFleshEyeDoorPrefab(matFleshWall, matHellOrgans);
            AssetDatabase.SaveAssets();

            if (!ok)
            {
                EditorUtility.DisplayDialog(
                    "Porta ainda nao importada",
                    $"Nao foi possivel carregar {DoorGlbPath}.\nVerifique se o pacote glTFast esta instalado.",
                    "Entendi"
                );
                return;
            }

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(DoorPrefabPath);
            if (prefab != null)
            {
                EditorGUIUtility.PingObject(prefab);
            }
            EditorUtility.DisplayDialog(
                "Porta Criada!",
                $"Prefab pronto em:\n{DoorPrefabPath}\n\n" +
                "Basta arrastar o Prefab para dentro da cena no lugar desejado!\n" +
                "- Pulsacao organica\n" +
                "- Boca e lingua se mexendo levemente\n" +
                "- Olho piscando e seguindo o jogador",
                "Ir para o Projeto"
            );
        }

        [MenuItem("Estomago/Criar Player de Teste na Cena")]
        public static void CreateTestPlayerMenu()
        {
            GameObject playerGo = SetupVirusPlayerInScene();
            if (playerGo != null)
            {
                EditorUtility.DisplayDialog(
                    "Virus Player Configurado!",
                    $"O '{playerGo.name}' (VirusMaligno) foi configurado na cena com todas as 5 animacoes!\n\n" +
                    $"Posicao: {playerGo.transform.position}\n" +
                    "- Animacoes: Idle, Walk (acelera no Shift), Attack, Hit e Death\n" +
                    "- Movimento isometrico (W, A, S, D), Corrida (Shift), Pulo (Espaco)\n" +
                    "- Ataque Viral (Clique Esquerdo / J / Enter) que estoura bolhas, ativa crateras e faz orgaos reagirem\n" +
                    "- Reacao Hit ao pisar no acido e animacao Death completa antes de renascer!",
                    "Excelente!"
                );
            }
        }

        [MenuItem("Estomago/Equipar Chicote Mini Tenia no Player")]
        public static void EquipTeniaWhipOnPlayerMenu()
        {
            SetupTeniaWhipAndArsenal(showDialog: true);
        }

        [MenuItem("Estomago/Armas/Equipar Lamina Ardente (Slot 3)")]
        public static void EquipLaminaArdenteOnPlayerMenu()
        {
            SetupLaminaArdenteAndArsenal(showDialog: true);
        }

        [MenuItem("Estomago/Armas/Criar Bonecos de Treino do Chicote")]
        public static void SpawnWhipTrainingDummiesMenu()
        {
            GameObject playerGo = GameObject.Find("Test_Player");
            if (playerGo == null) playerGo = GameObject.FindGameObjectWithTag("Player");
            if (playerGo == null) return;

            Material matHellOrgans = AssetDatabase.LoadAssetAtPath<Material>($"{MaterialsFolder}/Mat_HellOrgans.mat");
            Material matFleshWall = AssetDatabase.LoadAssetAtPath<Material>($"{MaterialsFolder}/Mat_FleshWall.mat");

            Vector3 fwd = playerGo.transform.forward;
            Vector3 right = playerGo.transform.right;
            SpawnWhipTrainingDummy("Whip_Training_Dummy_01", playerGo.transform.position + fwd * 5.0f - right * 1.4f, matHellOrgans ?? matFleshWall);
            SpawnWhipTrainingDummy("Whip_Training_Dummy_02", playerGo.transform.position + fwd * 8.0f + right * 1.6f, matHellOrgans ?? matFleshWall);
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        }

        public static void SetupTeniaWhipAndArsenal(bool showDialog)
        {
            GameObject playerGo = ResolveOrCreatePlayer();
            if (playerGo == null) return;

            if (!AssetDatabase.IsValidFolder(MaterialsFolder))
            {
                AssetDatabase.CreateFolder(BaseFolder, "Materials");
            }

            // Materiais proprios de arma (sem corte BG3Cutout e sem inflacao excessiva de parede)
            string proglottidTex = File.Exists($"{TexturesFolder}/tex_tenia_proglottid.png")
                ? "tex_tenia_proglottid.png"
                : "tex_flesh_floor.png";

            Material matProglottid = CreateWeaponLivingFleshMaterial("Mat_TeniaProglottid", proglottidTex, 240f, 0.55f, 0.48f, Color.black);
            Material matWhipFlesh = CreateWeaponLivingFleshMaterial("Mat_WhipFlesh", "tex_flesh_wall.png", 240f, 0.52f, 0.45f, Color.black);
            Material matWhipCartilage = CreateWeaponLivingFleshMaterial("Mat_WhipCartilage", "tex_cartilage.png", 240f, 0.35f, 0.48f, Color.black);
            Material matWhipHellOrgans = CreateWeaponLivingFleshMaterial("Mat_WhipHellOrgans", "tex_hell_organs.png", 240f, 0.65f, 0.42f, Color.black);

            Material matGastricAcid = AssetDatabase.LoadAssetAtPath<Material>($"{MaterialsFolder}/Mat_GastricAcid.mat");
            Material matPixelAcid = AssetDatabase.LoadAssetAtPath<Material>($"{MaterialsFolder}/Mat_PixelAcidParticle.mat");
            Material matHellOrgans = AssetDatabase.LoadAssetAtPath<Material>($"{MaterialsFolder}/Mat_HellOrgans.mat");
            Material matFleshWall = AssetDatabase.LoadAssetAtPath<Material>($"{MaterialsFolder}/Mat_FleshWall.mat");

            Transform weaponMount = EnsureWeaponMountPivot(playerGo);

            // Remove instancia anterior de Tenia_Whip se existir
            Transform oldWhip = weaponMount.Find("Tenia_Whip");
            if (oldWhip != null)
            {
                Undo.DestroyObjectImmediate(oldWhip.gameObject);
            }

            Transform whipInstanceTf = null;
            string whipGlbPath = $"{BaseFolder}/Tenia_Whip.glb";
            if (File.Exists(whipGlbPath))
            {
                AssetDatabase.ImportAsset(whipGlbPath, ImportAssetOptions.ForceUpdate);
                GameObject whipAsset = AssetDatabase.LoadAssetAtPath<GameObject>(whipGlbPath);
                if (whipAsset != null)
                {
                    GameObject whipGo = (GameObject)PrefabUtility.InstantiatePrefab(whipAsset, weaponMount);
                    if (whipGo == null) whipGo = Object.Instantiate(whipAsset, weaponMount);
                    whipGo.name = "Tenia_Whip";
                    whipGo.transform.localPosition = Vector3.zero;
                    whipGo.transform.localRotation = Quaternion.identity;
                    whipGo.transform.localScale = Vector3.one * 0.78f;
                    whipInstanceTf = whipGo.transform;

                    AssignTeniaWhipMaterials(whipGo, matProglottid, matWhipFlesh, matWhipCartilage, matWhipHellOrgans, matGastricAcid);
                }
            }
            else
            {
                Debug.LogWarning($"[StomachChamberSetup] {whipGlbPath} nao encontrado; TeniaWhipWeapon sera configurado sem malha GLB.");
            }

            TeniaWhipWeapon whipWeapon = EnsureComponent<TeniaWhipWeapon>(playerGo);
            whipWeapon.weaponMount = weaponMount;
            whipWeapon.whipRoot = whipInstanceTf;
            whipWeapon.acidMaterial = matGastricAcid;
            whipWeapon.pixelParticleMaterial = matPixelAcid;
            EditorUtility.SetDirty(whipWeapon);

            SyncWeaponArsenalSlots(playerGo, weaponMount, activeWeaponId: "tenia_whip");

            // Spawna 2 bonecos de treino apenas se nao houver nenhum alvo/inimigo IAcidDamageable na cena
            bool hasOtherCombatTargets = false;
            MonoBehaviour[] sceneBehaviours = Object.FindObjectsByType<MonoBehaviour>();
            for (int i = 0; i < sceneBehaviours.Length; i++)
            {
                if (sceneBehaviours[i] is IAcidDamageable && sceneBehaviours[i].gameObject != playerGo)
                {
                    hasOtherCombatTargets = true;
                    break;
                }
            }

            if (!hasOtherCombatTargets)
            {
                Vector3 fwd = playerGo.transform.forward;
                Vector3 right = playerGo.transform.right;
                SpawnWhipTrainingDummy("Whip_Training_Dummy_01", playerGo.transform.position + fwd * 5.0f - right * 1.4f, matHellOrgans ?? matFleshWall);
                SpawnWhipTrainingDummy("Whip_Training_Dummy_02", playerGo.transform.position + fwd * 8.0f + right * 1.6f, matHellOrgans ?? matFleshWall);
            }

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveOpenScenes();
            Selection.activeGameObject = playerGo;

            if (showDialog)
            {
                CrossRazorGreatswordController swordCtrl = playerGo.GetComponent<CrossRazorGreatswordController>();
                string slotSummary = swordCtrl != null
                    ? "Troca de Arma: [1] Espada Cruz  |  [2] Mini Tenia (equipada agora)\n\n"
                    : "Arma Equipada: [1] Mini Tenia\n\n";

                EditorUtility.DisplayDialog(
                    "Chicote Mini Tenia + Arsenal Configurado!",
                    slotSummary +
                    "- Botao Esq (sem mirar): Combo de 3 chicotadas (reduz Fadiga ao acertar)\n" +
                    "- Segurar Botao Dir: Mira estilo Espada Cruz + Circulo no local do acido\n" +
                    "- Dir + toque Esq: Bolha Acida Pequena (3 cargas)\n" +
                    "- Dir + segurar Esq (0.55s): Bolha Pesada do Boss + Poca de 3.0s",
                    "Vamos Testar!"
                );
            }
        }

        public static void SetupLaminaArdenteAndArsenal(bool showDialog)
        {
            ConfigurePixelTextures();
            GameObject playerGo = ResolveOrCreatePlayer();
            if (playerGo == null) return;

            if (!AssetDatabase.IsValidFolder(MaterialsFolder))
            {
                AssetDatabase.CreateFolder(BaseFolder, "Materials");
            }

            string hiltTex = ResolveWeaponTexture("tex_lamina_ardente_hilt.png", "tex_lamina_ardente.png", "tex_flesh_wall.png");
            string bladeTex = ResolveWeaponTexture("tex_lamina_ardente_blade.png", "tex_lamina_ardente.png", "tex_flesh_wall.png");
            string emberTex = ResolveWeaponTexture("tex_lamina_ardente_ember.png", "tex_lamina_ardente.png", "tex_flesh_wall.png");

            Material matHilt = CreateWeaponLivingFleshMaterial(
                "Mat_LaminaArdente_Hilt",
                hiltTex,
                240f,
                0.40f,
                0.48f,
                Color.black
            );
            Material matBlade = CreateWeaponLivingFleshMaterial(
                "Mat_LaminaArdente_Blade",
                bladeTex,
                260f,
                0.65f,
                0.52f,
                new Color(0.04f, 0.008f, 0.005f, 1f)
            );
            Material matEmber = CreateWeaponLivingFleshMaterial(
                "Mat_LaminaArdente_Ember",
                emberTex,
                260f,
                0.50f,
                0.55f,
                new Color(0.35f, 0.06f, 0.01f, 1f)
            );

            Material matPixelFire = AssetDatabase.LoadAssetAtPath<Material>($"{MaterialsFolder}/Mat_PixelFireParticle.mat");
            if (matPixelFire == null && File.Exists($"{TexturesFolder}/tex_pixel_particle.png"))
            {
                matPixelFire = CreateOrUpdateMaterial("Mat_PixelFireParticle", "MapaEstomago/URP/PS1_PixelParticle", "tex_pixel_particle.png", m =>
                {
                    m.SetColor("_BaseColor", Color.white);
                    m.SetFloat("_EmissionBoost", 2.0f);
                    m.SetFloat("_Cutoff", 0.32f);
                });
            }

            Transform weaponMount = EnsureWeaponMountPivot(playerGo);

            Transform oldArdente = weaponMount.Find("Lamina_Ardente");
            if (oldArdente != null)
            {
                Undo.DestroyObjectImmediate(oldArdente.gameObject);
            }

            Transform ardenteInstanceTf = null;
            string ardenteGlbPath = $"{BaseFolder}/Lamina_Ardente.glb";
            if (File.Exists(ardenteGlbPath))
            {
                AssetDatabase.ImportAsset(ardenteGlbPath, ImportAssetOptions.ForceUpdate);
                GameObject ardenteAsset = AssetDatabase.LoadAssetAtPath<GameObject>(ardenteGlbPath);
                if (ardenteAsset != null)
                {
                    GameObject ardenteGo = (GameObject)PrefabUtility.InstantiatePrefab(ardenteAsset, weaponMount);
                    if (ardenteGo == null) ardenteGo = Object.Instantiate(ardenteAsset, weaponMount);
                    ardenteGo.name = "Lamina_Ardente";
                    ardenteGo.transform.localPosition = Vector3.zero;
                    ardenteGo.transform.localRotation = Quaternion.identity;
                    ardenteGo.transform.localScale = Vector3.one * 0.72f;
                    ardenteInstanceTf = ardenteGo.transform;

                    AssignLaminaArdenteMaterials(ardenteGo, matHilt, matBlade, matEmber);
                }
            }
            else
            {
                Debug.LogWarning($"[StomachChamberSetup] {ardenteGlbPath} nao encontrado; LaminaArdenteWeapon sera configurado sem malha GLB.");
            }

            LaminaArdenteWeapon ardenteWeapon = EnsureComponent<LaminaArdenteWeapon>(playerGo);
            ardenteWeapon.weaponMount = weaponMount;
            ardenteWeapon.swordRoot = ardenteInstanceTf;
            ardenteWeapon.bladeMaterial = matBlade;
            ardenteWeapon.emberCoreMaterial = matEmber;
            ardenteWeapon.pixelFireMaterial = matPixelFire;
            ardenteWeapon.ResolveSwordHierarchy();
            EditorUtility.SetDirty(ardenteWeapon);

            SyncWeaponArsenalSlots(playerGo, weaponMount, activeWeaponId: "lamina_ardente");

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveOpenScenes();
            Selection.activeGameObject = playerGo;

            if (showDialog)
            {
                EditorUtility.DisplayDialog(
                    "Lamina Ardente (Slot 3) Configurada!",
                    "Arsenal atualizado mantendo Espada Cruz [1], Mini Tenia [2] e Lamina Ardente [3] (equipada agora).\n\n" +
                    "- Agilidade: +8% de velocidade de movimento\n" +
                    "- Botao Esquerdo: Combo ultrarrapido de 4 golpes (sem projeteis; explode +10 de fogo em inimigos queimando)\n" +
                    "- Segurar & Soltar Botao Direito: Estocada de Fogo com rastro em chamas no chao, Burn DoT (4.0s) e Modo Ardente!",
                    "Testar Agora!"
                );
            }
        }

        private static GameObject ResolveOrCreatePlayer()
        {
            GameObject playerGo = null;
            try
            {
                playerGo = GameObject.FindGameObjectWithTag("Player");
            }
            catch (UnityException) { }

            if (playerGo == null)
            {
                playerGo = GameObject.Find("Test_Player");
            }

            if (playerGo == null)
            {
                CreateTestPlayerMenu();
                try { playerGo = GameObject.FindGameObjectWithTag("Player"); } catch (UnityException) { }
                if (playerGo == null) playerGo = GameObject.Find("Test_Player");
            }

            if (playerGo == null)
            {
                Debug.LogError("[StomachChamberSetup] Nao foi possivel encontrar ou criar o Test_Player.");
            }
            return playerGo;
        }

        private static Transform EnsureWeaponMountPivot(GameObject playerGo)
        {
            Transform legacyHand = playerGo.transform.Find("WeaponHand");
            if (legacyHand != null)
            {
                Undo.DestroyObjectImmediate(legacyHand.gameObject);
            }

            Transform weaponMount = playerGo.transform.Find("Weapon_Mount_Pivot");
            if (weaponMount == null)
            {
                GameObject mountGo = new GameObject("Weapon_Mount_Pivot");
                Undo.RegisterCreatedObjectUndo(mountGo, "Create Weapon_Mount_Pivot");
                weaponMount = mountGo.transform;
                weaponMount.SetParent(playerGo.transform, false);
                weaponMount.localPosition = new Vector3(0.46f, 0.92f, 0.34f);
                weaponMount.localRotation = Quaternion.identity;
            }
            return weaponMount;
        }

        private static void SyncWeaponArsenalSlots(GameObject playerGo, Transform weaponMount, string activeWeaponId)
        {
            PlayerWeaponArsenal arsenal = EnsureComponent<PlayerWeaponArsenal>(playerGo);
            arsenal.slots.Clear();

            CrossRazorGreatswordController swordCtrl = playerGo.GetComponent<CrossRazorGreatswordController>();
            if (swordCtrl != null)
            {
                swordCtrl.weaponMount = weaponMount;
                swordCtrl.ResolveSwordHierarchy();
                GameObject swordVis = swordCtrl.swordRoot != null
                    ? swordCtrl.swordRoot.gameObject
                    : weaponMount.Find("Cross_Razor_Greatsword")?.gameObject;
                arsenal.RegisterOrUpdateSlot(
                    "cross_razor",
                    "Espada Cruz",
                    swordCtrl,
                    swordVis,
                    unlocked: true,
                    equipNow: string.Equals(activeWeaponId, "cross_razor", System.StringComparison.OrdinalIgnoreCase)
                );
            }

            TeniaWhipWeapon whipWeapon = playerGo.GetComponent<TeniaWhipWeapon>();
            if (whipWeapon != null)
            {
                whipWeapon.weaponMount = weaponMount;
                GameObject whipVis = whipWeapon.whipRoot != null
                    ? whipWeapon.whipRoot.gameObject
                    : weaponMount.Find("Tenia_Whip")?.gameObject;
                arsenal.RegisterOrUpdateSlot(
                    "tenia_whip",
                    "Mini Tenia",
                    whipWeapon,
                    whipVis,
                    unlocked: true,
                    equipNow: string.Equals(activeWeaponId, "tenia_whip", System.StringComparison.OrdinalIgnoreCase)
                );
            }

            LaminaArdenteWeapon ardenteWeapon = playerGo.GetComponent<LaminaArdenteWeapon>();
            if (ardenteWeapon != null)
            {
                ardenteWeapon.weaponMount = weaponMount;
                ardenteWeapon.ResolveSwordHierarchy();
                GameObject ardenteVis = ardenteWeapon.swordRoot != null
                    ? ardenteWeapon.swordRoot.gameObject
                    : weaponMount.Find("Lamina_Ardente")?.gameObject;
                arsenal.RegisterOrUpdateSlot(
                    "lamina_ardente",
                    "Lamina Ardente",
                    ardenteWeapon,
                    ardenteVis,
                    unlocked: true,
                    equipNow: string.Equals(activeWeaponId, "lamina_ardente", System.StringComparison.OrdinalIgnoreCase)
                );
            }

            arsenal.ApplyCurrentSlotImmediate();
            EditorUtility.SetDirty(arsenal);

            SimplePlayerController spc = playerGo.GetComponent<SimplePlayerController>();
            if (spc != null)
            {
                spc.ResolveActiveWeapon();
                EditorUtility.SetDirty(spc);
            }
        }

        private static string ResolveWeaponTexture(params string[] candidateFileNames)
        {
            for (int i = 0; i < candidateFileNames.Length; i++)
            {
                string fileName = candidateFileNames[i];
                if (!string.IsNullOrEmpty(fileName) && File.Exists($"{TexturesFolder}/{fileName}"))
                {
                    return fileName;
                }
            }
            return "tex_flesh_wall.png";
        }

        private static Material CreateWeaponLivingFleshMaterial(
            string matName,
            string textureFileName,
            float vertexSnap,
            float wetness,
            float ambientFloor,
            Color emissionColor)
        {
            return CreateOrUpdateMaterial(matName, "MapaEstomago/URP/PS1_LivingFlesh", textureFileName, m =>
            {
                m.SetColor("_BaseColor", Color.white);
                m.SetFloat("_VertexSnap", vertexSnap);
                m.SetFloat("_BreathSpeed", 0f);
                m.SetFloat("_BreathAmp", 0f);
                m.SetFloat("_SplitAmount", 0f);
                m.SetFloat("_Wetness", wetness);
                m.SetFloat("_AmbientFloor", ambientFloor);
                m.SetColor("_EmissionColor", emissionColor);
                m.SetFloat("_UseVertexColor", 1f);
                m.SetFloat("_EnableBG3Cutout", 0f);
            });
        }

        private static void AssignLaminaArdenteMaterials(
            GameObject ardenteGo,
            Material matHilt,
            Material matBlade,
            Material matEmber)
        {
            Renderer[] renderers = ardenteGo.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer r = renderers[i];
                if (r == null) continue;
                string rawName = (r.gameObject.name.StartsWith("Primitive") && r.transform.parent != null)
                    ? r.transform.parent.name
                    : r.gameObject.name;
                string n = rawName.ToLowerInvariant();

                Material target = matBlade;
                if (n.Contains("ember") || n.Contains("core"))
                {
                    target = matEmber != null ? matEmber : matBlade;
                }
                else if (n.Contains("hilt") || n.Contains("guard"))
                {
                    target = matHilt != null ? matHilt : matBlade;
                }

                if (target == null) continue;

                Material[] mats = r.sharedMaterials;
                if (mats == null || mats.Length == 0)
                {
                    r.sharedMaterial = target;
                }
                else
                {
                    for (int m = 0; m < mats.Length; m++)
                    {
                        mats[m] = target;
                    }
                    r.sharedMaterials = mats;
                }
            }
        }

        private static void AssignTeniaWhipMaterials(
            GameObject whipGo,
            Material matProglottid,
            Material matFleshWall,
            Material matCartilage,
            Material matHellOrgans,
            Material matGastricAcid)
        {
            Renderer[] renderers = whipGo.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer r = renderers[i];
                if (r == null) continue;
                string rawName = (r.gameObject.name.StartsWith("Primitive") && r.transform.parent != null)
                    ? r.transform.parent.name
                    : r.gameObject.name;
                string n = rawName.ToLowerInvariant();
                Material target = matProglottid;

                if (n.Contains("acid") || n.Contains("sac") || n.Contains("gland"))
                {
                    target = matGastricAcid != null ? matGastricAcid : matProglottid;
                }
                else if (n.Contains("sucker"))
                {
                    target = matHellOrgans != null ? matHellOrgans : (matFleshWall != null ? matFleshWall : matProglottid);
                }
                else if (n.Contains("cartilage") || n.Contains("hook") || n.Contains("tooth"))
                {
                    target = matCartilage != null ? matCartilage : matProglottid;
                }
                else if (n.Contains("hilt"))
                {
                    target = matFleshWall != null ? matFleshWall : matProglottid;
                }
                else if (n.Contains("scolex") || n.Contains("seg"))
                {
                    target = matProglottid;
                }

                if (target == null) continue;

                Material[] mats = r.sharedMaterials;
                if (mats == null || mats.Length == 0)
                {
                    r.sharedMaterial = target;
                }
                else
                {
                    for (int m = 0; m < mats.Length; m++)
                    {
                        mats[m] = target;
                    }
                    r.sharedMaterials = mats;
                }
            }
        }

        private static void SpawnWhipTrainingDummy(string dummyName, Vector3 pos, Material fleshMat)
        {
            GameObject dummyGo = new GameObject(dummyName);
            Undo.RegisterCreatedObjectUndo(dummyGo, "Create Whip Training Dummy");
            dummyGo.transform.position = pos;

            GameObject stalk = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            stalk.name = "Dummy_Stalk";
            stalk.transform.SetParent(dummyGo.transform, false);
            stalk.transform.localPosition = new Vector3(0f, 0.55f, 0f);
            stalk.transform.localScale = new Vector3(0.55f, 0.55f, 0.55f);
            Object.DestroyImmediate(stalk.GetComponent<Collider>());
            if (fleshMat != null) stalk.GetComponent<Renderer>().sharedMaterial = fleshMat;

            GameObject head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            head.name = "Dummy_PustuleHead";
            head.transform.SetParent(dummyGo.transform, false);
            head.transform.localPosition = new Vector3(0f, 1.32f, 0f);
            head.transform.localScale = new Vector3(0.95f, 0.85f, 0.95f);
            Object.DestroyImmediate(head.GetComponent<Collider>());
            if (fleshMat != null) head.GetComponent<Renderer>().sharedMaterial = fleshMat;

            CapsuleCollider col = dummyGo.AddComponent<CapsuleCollider>();
            col.center = new Vector3(0f, 0.95f, 0f);
            col.radius = 0.52f;
            col.height = 1.9f;

            dummyGo.AddComponent<WhipTrainingDummy>();
        }

        [MenuItem("Estomago/Implementar Virus Player Completo (GLB + 5 Animacoes)")]
        public static GameObject SetupVirusPlayerInScene()
        {
            ConfigurePixelTextures();
            if (!AssetDatabase.IsValidFolder(MaterialsFolder))
            {
                AssetDatabase.CreateFolder(BaseFolder, "Materials");
            }
            if (!AssetDatabase.IsValidFolder(AnimationsFolder))
            {
                AssetDatabase.CreateFolder(BaseFolder, "Animations");
            }

            if (File.Exists(VirusGlbPath))
            {
                AssetDatabase.ImportAsset(VirusGlbPath, ImportAssetOptions.ForceUpdate);
            }

            // 1. Material customizado PS1_VirusPlayer com Albedo 128x128 + Emissive 128x128 bioluminescente
            Texture2D emissiveTex = AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexturesFolder}/tex_virus_emissive.png");
            Material matVirus = CreateOrUpdateMaterial("Mat_VirusPlayer", "MapaEstomago/URP/PS1_VirusPlayer", "tex_virus_albedo.png", m =>
            {
                m.SetColor("_BaseColor", Color.white);
                if (emissiveTex != null)
                {
                    m.SetTexture("_EmissionMap", emissiveTex);
                }
                m.SetColor("_EmissionColor", new Color(0.35f, 1.65f, 2.10f, 1f));
                m.SetFloat("_PulseSpeed", 2.8f);
                m.SetFloat("_VertexSnap", 200f);
                m.SetFloat("_Wetness", 0.55f);
                m.SetFloat("_AmbientFloor", 0.34f);
                m.SetFloat("_HitFlash", 0f);
            });

            // 2. Extrai os 5 AnimationClips (Idle, Walk, Attack, Hit, Death) e monta o AnimatorController
            AnimatorController controller = BuildVirusAnimatorController();

            // 3. Instancia ou atualiza o Test_Player na cena
            GameObject existingPlayer = GameObject.FindGameObjectWithTag("Player");
            if (existingPlayer == null)
            {
                existingPlayer = GameObject.Find("Test_Player");
            }

            Vector3 preservedPos = Vector3.zero;
            bool hadExistingPos = false;
            if (existingPlayer != null)
            {
                preservedPos = existingPlayer.transform.position;
                hadExistingPos = true;
            }

            GameObject playerGo = existingPlayer;
            if (playerGo == null)
            {
                playerGo = new GameObject("Test_Player");
                Undo.RegisterCreatedObjectUndo(playerGo, "Create Virus Player");
            }
            playerGo.tag = "Player";

            CharacterController cc = EnsureComponent<CharacterController>(playerGo);
            cc.height = 1.65f;
            cc.radius = 0.42f;
            cc.center = new Vector3(0f, 0.85f, 0f);
            cc.stepOffset = 0.35f;
            cc.slopeLimit = 50f;

            SimplePlayerController spc = EnsureComponent<SimplePlayerController>(playerGo);

            // Remove antigo visual placeholder (Capsula/Cubo) ou modelo anterior para atualizar limpo
            Transform oldVisual = playerGo.transform.Find("Visual");
            if (oldVisual != null) Object.DestroyImmediate(oldVisual.gameObject);
            Transform oldVirus = playerGo.transform.Find("Virus_Model");
            if (oldVirus != null) Object.DestroyImmediate(oldVirus.gameObject);

            GameObject virusGlbAsset = AssetDatabase.LoadAssetAtPath<GameObject>(VirusGlbPath);
            Transform visualRoot = null;
            Animator modelAnimator = null;

            if (virusGlbAsset != null)
            {
                GameObject modelInst = (GameObject)PrefabUtility.InstantiatePrefab(virusGlbAsset, playerGo.transform);
                if (modelInst != null)
                {
                    if (PrefabUtility.IsPartOfPrefabInstance(modelInst))
                    {
                        PrefabUtility.UnpackPrefabInstance(modelInst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                    }
                    modelInst.name = "Virus_Model";
                    modelInst.transform.localPosition = Vector3.zero;
                    modelInst.transform.localRotation = Quaternion.identity;
                    modelInst.transform.localScale = Vector3.one * 1.35f;
                    visualRoot = modelInst.transform;

                    SkinnedMeshRenderer[] smrs = modelInst.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                    foreach (SkinnedMeshRenderer smr in smrs)
                    {
                        smr.sharedMaterial = matVirus;
                        smr.updateWhenOffscreen = true;
                    }

                    MeshRenderer[] mrs = modelInst.GetComponentsInChildren<MeshRenderer>(true);
                    foreach (MeshRenderer mr in mrs)
                    {
                        mr.sharedMaterial = matVirus;
                    }

                    modelAnimator = EnsureComponent<Animator>(modelInst);
                    if (controller != null)
                    {
                        modelAnimator.runtimeAnimatorController = controller;
                    }
                    modelAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    modelAnimator.applyRootMotion = false;

                    // Adiciona luz bioluminescente ciano no EyeCore do Virus
                    Transform eyeCore = FindDeepChild(modelInst.transform, "EyeCore");
                    Transform lightParent = eyeCore != null ? eyeCore : modelInst.transform;
                    Transform existingGlow = lightParent.Find("Virus_Biolum_Light");
                    GameObject glowGo = existingGlow != null ? existingGlow.gameObject : new GameObject("Virus_Biolum_Light");
                    glowGo.transform.SetParent(lightParent, false);
                    glowGo.transform.localPosition = new Vector3(0f, 0.05f, 0.18f);
                    Light glowLight = EnsureComponent<Light>(glowGo);
                    glowLight.type = LightType.Point;
                    glowLight.color = new Color(0.18f, 0.90f, 1.0f, 1f);
                    glowLight.range = 5.5f;
                    glowLight.intensity = 2.2f;
                    spc.coreBiolumLight = glowLight;
                }
            }

            spc.visualRoot = visualRoot;
            spc.animator = modelAnimator;
            spc.BindVisualAndAnimator();

            // Determina a posicao de spawn (preserva se ja estava na cena em posicao valida)
            Vector3 spawnPos = preservedPos;
            if (!hadExistingPos)
            {
                GameObject pilotChamber = GameObject.Find("Stomach_Pilot_Chamber");
                GameObject mainDome = GameObject.Find("Stomach_Main_Dome");

                if (pilotChamber != null)
                {
                    spawnPos = pilotChamber.transform.position + new Vector3(0f, 0.5f, -1.6f);
                }
                else if (mainDome != null)
                {
                    spawnPos = mainDome.transform.position + new Vector3(-18f, 0.5f, -2f);
                }

                Ray ray = new Ray(spawnPos + Vector3.up * 4f, Vector3.down);
                if (Physics.Raycast(ray, out RaycastHit hit, 10f))
                {
                    spawnPos.y = hit.point.y + 0.05f;
                }
            }

            playerGo.transform.position = spawnPos;
            spc.SetRespawnPoint(spawnPos);

            // Salva tambem como Prefab pronto em Assets/PilotChamber/Virus_Player.prefab
            PrefabUtility.SaveAsPrefabAsset(playerGo, VirusPrefabPath);

            // Vincula a camera isometrica e a neblina Silent Hill 2 ao Player
            IsometricCameraFollow iso = Object.FindFirstObjectByType<IsometricCameraFollow>();
            if (iso != null)
            {
                iso.target = playerGo.transform;
            }

            StomachSilentHillFog fog = Object.FindFirstObjectByType<StomachSilentHillFog>();
            if (fog != null)
            {
                fog.playerTarget = playerGo.transform;
                fog.ApplyShaderGlobals();
            }

            Selection.activeGameObject = playerGo;
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();
            return playerGo;
        }

        private static Transform FindDeepChild(Transform parent, string targetName)
        {
            foreach (Transform t in parent.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == targetName) return t;
            }
            return null;
        }

        private static AnimatorController BuildVirusAnimatorController()
        {
            Object[] subAssets = AssetDatabase.LoadAllAssetsAtPath(VirusGlbPath);
            if (subAssets == null || subAssets.Length == 0) return null;

            AnimationClip clipIdle = null;
            AnimationClip clipWalk = null;
            AnimationClip clipAttack = null;
            AnimationClip clipHit = null;
            AnimationClip clipDeath = null;

            foreach (Object obj in subAssets)
            {
                if (obj is AnimationClip srcClip && !srcClip.name.StartsWith("__preview__"))
                {
                    bool shouldLoop = (srcClip.name == "Idle" || srcClip.name == "Walk");
                    AnimationClip savedClip = SaveOrUpdateClipCopy(srcClip, $"Virus_{srcClip.name}", shouldLoop);

                    switch (srcClip.name)
                    {
                        case "Idle": clipIdle = savedClip; break;
                        case "Walk": clipWalk = savedClip; break;
                        case "Attack": clipAttack = savedClip; break;
                        case "Hit": clipHit = savedClip; break;
                        case "Death": clipDeath = savedClip; break;
                    }
                }
            }

            DeleteAssetIfExists(VirusControllerPath);
            AnimatorController ctrl = AnimatorController.CreateAnimatorControllerAtPath(VirusControllerPath);

            ctrl.AddParameter("Speed", AnimatorControllerParameterType.Float);
            ctrl.AddParameter("MoveSpeedMultiplier", AnimatorControllerParameterType.Float);
            ctrl.AddParameter("IsMoving", AnimatorControllerParameterType.Bool);
            ctrl.AddParameter("IsDead", AnimatorControllerParameterType.Bool);
            ctrl.AddParameter("Attack", AnimatorControllerParameterType.Trigger);
            ctrl.AddParameter("Hit", AnimatorControllerParameterType.Trigger);

            // Define valor padrao de MoveSpeedMultiplier = 1.0
            AnimatorControllerParameter[] parameters = ctrl.parameters;
            for (int i = 0; i < parameters.Length; i++)
            {
                if (parameters[i].name == "MoveSpeedMultiplier")
                {
                    parameters[i].defaultFloat = 1.0f;
                }
            }
            ctrl.parameters = parameters;

            AnimatorStateMachine sm = ctrl.layers[0].stateMachine;

            AnimatorState stIdle = sm.AddState("Idle", new Vector3(260, 80, 0));
            stIdle.motion = clipIdle;
            sm.defaultState = stIdle;

            AnimatorState stWalk = sm.AddState("Walk", new Vector3(540, 80, 0));
            stWalk.motion = clipWalk;
            stWalk.speedParameterActive = true;
            stWalk.speedParameter = "MoveSpeedMultiplier";

            AnimatorState stAttack = sm.AddState("Attack", new Vector3(400, -50, 0));
            stAttack.motion = clipAttack;

            AnimatorState stHit = sm.AddState("Hit", new Vector3(400, 210, 0));
            stHit.motion = clipHit;

            AnimatorState stDeath = sm.AddState("Death", new Vector3(140, 240, 0));
            stDeath.motion = clipDeath;

            // Idle <-> Walk
            AnimatorStateTransition tIdleToWalk = stIdle.AddTransition(stWalk);
            tIdleToWalk.hasExitTime = false;
            tIdleToWalk.duration = 0.12f;
            tIdleToWalk.AddCondition(AnimatorConditionMode.If, 0f, "IsMoving");

            AnimatorStateTransition tWalkToIdle = stWalk.AddTransition(stIdle);
            tWalkToIdle.hasExitTime = false;
            tWalkToIdle.duration = 0.14f;
            tWalkToIdle.AddCondition(AnimatorConditionMode.IfNot, 0f, "IsMoving");

            // AnyState -> Attack -> retorno automatico para Walk ou Idle
            AnimatorStateTransition tAnyToAttack = sm.AddAnyStateTransition(stAttack);
            tAnyToAttack.hasExitTime = false;
            tAnyToAttack.duration = 0.06f;
            tAnyToAttack.canTransitionToSelf = false;
            tAnyToAttack.AddCondition(AnimatorConditionMode.If, 0f, "Attack");
            tAnyToAttack.AddCondition(AnimatorConditionMode.IfNot, 0f, "IsDead");

            AnimatorStateTransition tAttackToWalk = stAttack.AddTransition(stWalk);
            tAttackToWalk.hasExitTime = true;
            tAttackToWalk.exitTime = 0.92f;
            tAttackToWalk.duration = 0.10f;
            tAttackToWalk.AddCondition(AnimatorConditionMode.If, 0f, "IsMoving");

            AnimatorStateTransition tAttackToIdle = stAttack.AddTransition(stIdle);
            tAttackToIdle.hasExitTime = true;
            tAttackToIdle.exitTime = 0.92f;
            tAttackToIdle.duration = 0.12f;
            tAttackToIdle.AddCondition(AnimatorConditionMode.IfNot, 0f, "IsMoving");

            // AnyState -> Hit -> retorno automatico para Walk ou Idle
            AnimatorStateTransition tAnyToHit = sm.AddAnyStateTransition(stHit);
            tAnyToHit.hasExitTime = false;
            tAnyToHit.duration = 0.05f;
            tAnyToHit.canTransitionToSelf = false;
            tAnyToHit.AddCondition(AnimatorConditionMode.If, 0f, "Hit");
            tAnyToHit.AddCondition(AnimatorConditionMode.IfNot, 0f, "IsDead");

            AnimatorStateTransition tHitToWalk = stHit.AddTransition(stWalk);
            tHitToWalk.hasExitTime = true;
            tHitToWalk.exitTime = 0.90f;
            tHitToWalk.duration = 0.10f;
            tHitToWalk.AddCondition(AnimatorConditionMode.If, 0f, "IsMoving");

            AnimatorStateTransition tHitToIdle = stHit.AddTransition(stIdle);
            tHitToIdle.hasExitTime = true;
            tHitToIdle.exitTime = 0.90f;
            tHitToIdle.duration = 0.12f;
            tHitToIdle.AddCondition(AnimatorConditionMode.IfNot, 0f, "IsMoving");

            // AnyState -> Death e Death -> Idle ao renascer
            AnimatorStateTransition tAnyToDeath = sm.AddAnyStateTransition(stDeath);
            tAnyToDeath.hasExitTime = false;
            tAnyToDeath.duration = 0.08f;
            tAnyToDeath.canTransitionToSelf = false;
            tAnyToDeath.AddCondition(AnimatorConditionMode.If, 0f, "IsDead");

            AnimatorStateTransition tDeathToIdle = stDeath.AddTransition(stIdle);
            tDeathToIdle.hasExitTime = false;
            tDeathToIdle.duration = 0.12f;
            tDeathToIdle.AddCondition(AnimatorConditionMode.IfNot, 0f, "IsDead");

            EditorUtility.SetDirty(ctrl);
            return ctrl;
        }

        private static AnimationClip SaveOrUpdateClipCopy(AnimationClip src, string clipFileName, bool loop)
        {
            string path = $"{AnimationsFolder}/{clipFileName}.anim";
            AnimationClip existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (existing == null)
            {
                existing = new AnimationClip();
                EditorUtility.CopySerialized(src, existing);
                existing.name = clipFileName;
                AssetDatabase.CreateAsset(existing, path);
            }
            else
            {
                EditorUtility.CopySerialized(src, existing);
                existing.name = clipFileName;
            }

            AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(existing);
            settings.loopTime = loop;
            AnimationUtility.SetAnimationClipSettings(existing, settings);
            existing.wrapMode = loop ? WrapMode.Loop : WrapMode.ClampForever;
            EditorUtility.SetDirty(existing);
            return existing;
        }

        private static Material CreateFleshWallMaterial()
        {
            return CreateOrUpdateMaterial("Mat_FleshWall", "MapaEstomago/URP/PS1_LivingFlesh", "tex_flesh_wall.png", m =>
            {
                m.SetFloat("_BreathSpeed", 1.5f);
                m.SetFloat("_BreathAmp", 0.018f);
                m.SetFloat("_SplitAmount", 0.28f);
                m.SetFloat("_Wetness", 0.42f);
                m.SetFloat("_AmbientFloor", 0.22f);
                m.SetFloat("_EnableBG3Cutout", 1f);
            });
        }

        private static Material CreateHellOrgansMaterial()
        {
            return CreateOrUpdateMaterial("Mat_HellOrgans", "MapaEstomago/URP/PS1_LivingFlesh", "tex_hell_organs.png", m =>
            {
                m.SetFloat("_BreathSpeed", 1.6f);
                m.SetFloat("_BreathAmp", 0.015f);
                m.SetFloat("_SplitAmount", 0.25f);
                m.SetFloat("_Wetness", 0.30f);
                m.SetFloat("_AmbientFloor", 0.22f);
                m.SetFloat("_EnableBG3Cutout", 0f);
            });
        }

        private static Material CreateDomeRibArchesMaterial()
        {
            return CreateOrUpdateMaterial("Mat_DomeRibArches", "MapaEstomago/URP/PS1_LivingFlesh", "tex_hell_organs.png", m =>
            {
                m.SetFloat("_BreathSpeed", 1.6f);
                m.SetFloat("_BreathAmp", 0.015f);
                m.SetFloat("_SplitAmount", 0.25f);
                m.SetFloat("_Wetness", 0.30f);
                m.SetFloat("_AmbientFloor", 0.22f);
                m.SetFloat("_EnableBG3Cutout", 1f);
            });
        }

        private static Material CreateBloodDripsMaterial()
        {
            return CreateOrUpdateMaterial("Mat_BloodDrips", "MapaEstomago/URP/PS1_BloodDrips", "tex_blood_drips.png", m =>
            {
                m.SetColor("_BaseColor", new Color(0.95f, 0.85f, 0.86f, 1f));
                m.SetFloat("_Cutoff", 0.45f);
                m.SetFloat("_LaneCount", 4f);
                m.SetFloat("_DripSpeed", 0.036f);
                m.SetFloat("_DripSpeedFast", 0.082f);
                m.SetFloat("_SurgeAmount", 0.32f);
                m.SetFloat("_SurgeFreq", 1.45f);
                m.SetFloat("_WaveSpeed", 1.1f);
                m.SetFloat("_WaveAmp", 0.008f);
                m.SetFloat("_SwayAmp", 0.0f);
                m.SetFloat("_Cull", 2f); // Back culling (renderiza face interna)
                m.SetFloat("_EnableBG3Cutout", 1f);
            });
        }

        private static Material CreateBloodWavesMaterial()
        {
            return CreateOrUpdateMaterial("Mat_BloodWaves", "MapaEstomago/URP/PS1_BloodDrips", "tex_blood_waves.png", m =>
            {
                m.SetColor("_BaseColor", new Color(0.92f, 0.82f, 0.84f, 1f));
                m.SetFloat("_Cutoff", 0.45f);
                m.SetFloat("_LaneCount", 3f);
                m.SetFloat("_DripSpeed", 0.048f);
                m.SetFloat("_DripSpeedFast", 0.095f);
                m.SetFloat("_SurgeAmount", 0.35f);
                m.SetFloat("_SurgeFreq", 1.25f);
                m.SetFloat("_WaveSpeed", 1.5f);
                m.SetFloat("_WaveAmp", 0.016f);
                m.SetFloat("_SwayAmp", 0.0f);
                m.SetFloat("_Cull", 2f); // Back culling (renderiza face interna)
                m.SetFloat("_EnableBG3Cutout", 1f);
            });
        }

        private static Material CreatePustuleMaterial()
        {
            return CreateOrUpdateMaterial("Mat_Pustule", "MapaEstomago/URP/PS1_LivingFlesh", "tex_pustule.png", m =>
            {
                m.SetFloat("_BreathSpeed", 2.2f);
                m.SetFloat("_BreathAmp", 0.022f);
                m.SetFloat("_SplitAmount", 0.20f);
                m.SetFloat("_Wetness", 0.78f);
                m.SetFloat("_AmbientFloor", 0.32f);
                m.SetColor("_EmissionColor", new Color(0.55f, 0.42f, 0.06f, 1f));
            });
        }

        private static Material CreateDigestedCalcifiedMaterial()
        {
            return CreateOrUpdateMaterial("Mat_DigestedCalcified", "MapaEstomago/URP/PS1_LivingFlesh", "tex_digested_calcified.png", m =>
            {
                m.SetFloat("_BreathSpeed", 1.0f);
                m.SetFloat("_BreathAmp", 0.005f);
                m.SetFloat("_SplitAmount", 0.15f);
                m.SetFloat("_Wetness", 0.48f);
                m.SetFloat("_AmbientFloor", 0.28f);
            });
        }

        [MenuItem("Estomago/Configurar Camara Piloto Completa (URP + Animacoes)")]
        public static void SetupPilotChamber()
        {
            // 1. Garantir reimportacao do .glb atualizado e verificar glTFast
            if (File.Exists(GlbPath))
            {
                AssetDatabase.ImportAsset(GlbPath, ImportAssetOptions.ForceUpdate);
            }
            GameObject glbAsset = AssetDatabase.LoadAssetAtPath<GameObject>(GlbPath);
            GameObject chamberInScene = GameObject.Find("Stomach_Pilot_Chamber");

            if (chamberInScene == null && glbAsset == null)
            {
                EditorUtility.DisplayDialog(
                    "glTFast nao instalado ainda",
                    "A Unity ainda nao reconhece arquivos .glb como modelos 3D.\n\n" +
                    "Instale o pacote oficial primeiro:\n" +
                    "1. Window -> Package Manager\n" +
                    "2. Clique no '+' (canto superior esquerdo) -> Add package by name...\n" +
                    "3. Digite: com.unity.cloud.gltfast  (deixe Version vazio) e clique em Add.\n" +
                    "4. Depois que terminar de instalar, clique neste menu novamente!",
                    "Entendi"
                );
                return;
            }

            // Remover assets legados de blood strands se ainda existirem no projeto
            DeleteAssetIfExists($"{MaterialsFolder}/Mat_BloodStrands.mat");
            DeleteAssetIfExists($"{TexturesFolder}/tex_blood_strands.png");

            // Se ja houver uma instancia antiga na cena, permitir substituir pelo .glb atualizado
            if (chamberInScene != null && glbAsset != null)
            {
                bool replaceExisting = EditorUtility.DisplayDialog(
                    "Atualizar Camara Piloto na Cena?",
                    "Ja existe um objeto 'Stomach_Pilot_Chamber' na cena.\n\n" +
                    "Deseja substituir pela versao mais recente do arquivo .glb " +
                    "(necessario para aplicar novos UVs/malhas como o sangue corrigido)?",
                    "Substituir pelo novo .glb",
                    "Manter malha atual"
                );

                if (replaceExisting)
                {
                    Undo.DestroyObjectImmediate(chamberInScene);
                    chamberInScene = null;
                }
            }

            // 2. Configurar todas as texturas como Point (Pixel Art PS1 sem borrao)
            ConfigurePixelTextures();

            // 3. Criar Materiais URP com nossos Shaders animados
            if (!AssetDatabase.IsValidFolder(MaterialsFolder))
            {
                AssetDatabase.CreateFolder(BaseFolder, "Materials");
            }

            Material matFleshFloor = CreateOrUpdateMaterial("Mat_FleshFloor", "MapaEstomago/URP/PS1_LivingFlesh", "tex_flesh_floor.png", m =>
            {
                m.SetFloat("_BreathSpeed", 1.2f);
                m.SetFloat("_BreathAmp", 0.006f);
                m.SetFloat("_SplitAmount", 0.25f);
                m.SetFloat("_Wetness", 0.35f);
                m.SetFloat("_AmbientFloor", 0.25f);
            });

            Material matFleshWall = CreateFleshWallMaterial();

            Material matCartilage = CreateOrUpdateMaterial("Mat_Cartilage", "MapaEstomago/URP/PS1_LivingFlesh", "tex_cartilage.png", m =>
            {
                m.SetFloat("_BreathSpeed", 1.0f);
                m.SetFloat("_BreathAmp", 0.004f);
                m.SetFloat("_SplitAmount", 0.25f);
                m.SetFloat("_Wetness", 0.25f);
                m.SetFloat("_AmbientFloor", 0.28f);
            });

            Material matHellOrgans = CreateHellOrgansMaterial();

            Material matGastricAcid = CreateOrUpdateMaterial("Mat_GastricAcid", "MapaEstomago/URP/PS1_GastricAcid", "tex_acid.png", m =>
            {
                m.SetColor("_BaseColor", new Color(0.85f, 1f, 0.72f, 1f));
                m.SetColor("_EmissionColor", new Color(0.28f, 1.35f, 0.12f, 1f));
                m.SetVector("_ScrollSpeedA", new Vector4(0.045f, 0.032f, 0f, 0f));
                m.SetVector("_ScrollSpeedB", new Vector4(-0.028f, 0.052f, 0f, 0f));
                m.SetFloat("_WaveSpeed", 2.8f);
                m.SetFloat("_WaveAmp", 0.032f);
            });

            Material matBloodDrips = CreateBloodDripsMaterial();
            Material matBloodWaves = CreateBloodWavesMaterial();

            Material matPixelAcid = CreateOrUpdateMaterial("Mat_PixelAcidParticle", "MapaEstomago/URP/PS1_PixelParticle", "tex_pixel_particle.png", m =>
            {
                m.SetColor("_BaseColor", new Color(0.65f, 1f, 0.35f, 1f));
                m.SetFloat("_EmissionBoost", 2.0f);
            });

            Material matPixelBlood = CreateOrUpdateMaterial("Mat_PixelBloodParticle", "MapaEstomago/URP/PS1_PixelParticle", "tex_pixel_particle.png", m =>
            {
                m.SetColor("_BaseColor", new Color(0.95f, 0.14f, 0.16f, 1f));
                m.SetFloat("_EmissionBoost", 1.1f);
            });

            AssetDatabase.SaveAssets();

            // 4. Instanciar na cena se necessario e desempacotar Prefab
            if (chamberInScene == null && glbAsset != null)
            {
                chamberInScene = (GameObject)PrefabUtility.InstantiatePrefab(glbAsset);
                chamberInScene.name = "Stomach_Pilot_Chamber";
                chamberInScene.transform.position = Vector3.zero;
            }

            if (PrefabUtility.IsPartOfPrefabInstance(chamberInScene))
            {
                PrefabUtility.UnpackPrefabInstance(chamberInScene, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            }

            // 5. Configurar todas as submalhas, colisores e scripts animados
            Transform[] allTransforms = chamberInScene.GetComponentsInChildren<Transform>(true);
            int bubbleCount = 0;

            foreach (Transform t in allTransforms)
            {
                if (t == null) continue;
                string n = t.gameObject.name;
                string parentName = t.parent != null ? t.parent.gameObject.name : "";
                MeshRenderer mr = t.GetComponent<MeshRenderer>();
                MeshFilter mf = t.GetComponent<MeshFilter>();

                // glTFast pode colocar MeshRenderer em um filho "Primitive"
                string keyName = (mr != null && n.StartsWith("Primitive")) ? parentName : n;

                if (mr != null && mf != null)
                {
                    if (keyName == "Floor_Flesh_Walkable")
                    {
                        mr.sharedMaterial = matFleshFloor;
                        EnsureMeshCollider(t.gameObject);
                    }
                    else if (keyName == "Bridges_Cartilage")
                    {
                        mr.sharedMaterial = matCartilage;
                        EnsureMeshCollider(t.gameObject);
                    }
                    else if (keyName == "Wall_Flesh_Back" || keyName == "Wall_Cutaway_Front")
                    {
                        mr.sharedMaterial = matFleshWall;
                        mr.shadowCastingMode = ShadowCastingMode.Off;
                        EnsureMeshCollider(t.gameObject);
                    }
                    else if (keyName.StartsWith("Sphincter_"))
                    {
                        mr.sharedMaterial = matFleshWall;
                        EnsureMeshCollider(t.gameObject);
                    }
                    else if (keyName.StartsWith("Props_Mucosal_Pillar"))
                    {
                        mr.sharedMaterial = matFleshWall;
                        EnsureMeshCollider(t.gameObject);
                    }
                    else if (keyName == "Props_Acid_Vents")
                    {
                        mr.sharedMaterial = matHellOrgans;
                        EnsureMeshCollider(t.gameObject);
                    }
                    else if (keyName == "Wall_Blood_Drips")
                    {
                        mr.sharedMaterial = matBloodDrips;
                        mr.shadowCastingMode = ShadowCastingMode.Off;
                    }
                    else if (keyName == "Wall_Blood_Waves")
                    {
                        mr.sharedMaterial = matBloodWaves;
                        mr.shadowCastingMode = ShadowCastingMode.Off;
                    }
                    else if (keyName == "Wall_Blood_Strands")
                    {
                        GameObject rootToRemove = (n.StartsWith("Primitive") && t.parent != null) ? t.parent.gameObject : t.gameObject;
                        Undo.DestroyObjectImmediate(rootToRemove);
                        continue;
                    }
                    else if (keyName == "Acid_Lake")
                    {
                        mr.sharedMaterial = matGastricAcid;
                        ConfigureAcidLakeNode(t.gameObject);
                    }
                    else if (keyName == "Props_Acid_Crater_Pools")
                    {
                        mr.sharedMaterial = matGastricAcid;
                    }
                    else if (keyName.StartsWith("Acid_Bubble_"))
                    {
                        mr.sharedMaterial = matGastricAcid;
                        GameObject bubbleRoot = (n.StartsWith("Primitive") && t.parent != null) ? t.parent.gameObject : t.gameObject;
                        AcidBubblePopper popper = EnsureComponent<AcidBubblePopper>(bubbleRoot);
                        popper.pixelParticleMaterial = matPixelAcid;
                        bubbleCount++;
                    }
                }

                // Pulsacao organica nos nos raiz dos orgaos (com pivo centralizado!)
                if (n == "Sphincter_Entry" || n == "Sphincter_Exit")
                {
                    OrganPulse pulse = EnsureComponent<OrganPulse>(t.gameObject);
                    pulse.beatsPerSecond = 0.72f;
                    pulse.lateralAmplitude = 0.06f;
                    pulse.verticalAmplitude = 0.04f;
                    pulse.phaseOffset = n.EndsWith("Exit") ? 0.35f : 0f;
                }
                else if (n.StartsWith("Props_Mucosal_Pillar_"))
                {
                    OrganPulse pulse = EnsureComponent<OrganPulse>(t.gameObject);
                    pulse.beatsPerSecond = 0.72f;
                    pulse.lateralAmplitude = 0.045f;
                    pulse.verticalAmplitude = 0.025f;
                    pulse.phaseOffset = n.EndsWith("02") ? 0.5f : 0.15f;
                }
                else if (n.StartsWith("Ulcer_Vent_Anchor_"))
                {
                    UlcerEruption erupt = EnsureComponent<UlcerEruption>(t.gameObject);
                    erupt.pixelParticleMaterial = matPixelAcid;
                    Light l = EnsureComponent<Light>(t.gameObject);
                    l.type = LightType.Point;
                    l.color = new Color(0.32f, 1.0f, 0.15f);
                    l.range = 6.5f;
                    l.intensity = 2.5f;
                    erupt.craterGlowLight = l;
                }
                else if (n == "Blood_Drip_Anchor")
                {
                    BloodDripEmitter emitter = EnsureComponent<BloodDripEmitter>(t.gameObject);
                    emitter.pixelParticleMaterial = matPixelBlood;
                    emitter.dripsPerSecond = 9f;
                }
            }

            // 6. Luzes ambientais do acido, neblina infernal, Bloom URP e Camera Isometrica
            SetupAtmosphereAndLights(chamberInScene.transform);
            SetupIsometricCamera(22f, chamberInScene.transform.position);
            EnableSubtleBloom();
            bool doorReady = BuildOrUpdateFleshEyeDoorPrefab(matFleshWall, matHellOrgans);
            AssetDatabase.SaveAssets();
            GameObject doorPrefab = doorReady ? AssetDatabase.LoadAssetAtPath<GameObject>(DoorPrefabPath) : null;
            ConnectMapDoorsInScene(doorPrefab, matPixelAcid);

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();

            string doorMsg = doorReady
                ? $"- Prefab da Porta ('{DoorPrefabPath}') atualizado e pronto para arrastar!\n"
                : "";

            EditorUtility.DisplayDialog(
                "Camara Piloto Configurada!",
                $"Configuracao concluida com sucesso:\n" +
                $"- Texturas configuradas como Point (PS1)\n" +
                $"- Materiais URP animados (carne viva, acido fervendo, sangue escorrendo)\n" +
                $"- {bubbleCount} bolhas explosivas e 3 crateras eruptivas ativadas\n" +
                $"- Colisoes e dano continuo no Acid_Lake configurados\n" +
                $"- Camera isometrica e iluminacao infernal aplicadas\n" +
                doorMsg + "\n" +
                $"Aperte PLAY na Unity para ver o sangue escorrendo e o acido borbulhando!",
                "Ir para a Cena"
            );
        }

        [MenuItem("Estomago/Configurar Mapa Principal - Cupula Gigante (URP + Animacoes)")]
        public static void SetupMainDomeChamber()
        {
            if (File.Exists(MainDomeGlbPath))
            {
                AssetDatabase.ImportAsset(MainDomeGlbPath, ImportAssetOptions.ForceUpdate);
            }

            GameObject glbAsset = AssetDatabase.LoadAssetAtPath<GameObject>(MainDomeGlbPath);
            GameObject domeInScene = GameObject.Find("Stomach_Main_Dome");

            if (domeInScene == null && glbAsset == null)
            {
                EditorUtility.DisplayDialog(
                    "Arquivo da Cupula Gigante nao encontrado",
                    $"Nao foi possivel carregar '{MainDomeGlbPath}'.\n\n" +
                    "Verifique se:\n" +
                    "1. O arquivo Stomach_Main_Dome.glb foi gerado em Assets/PilotChamber\n" +
                    "2. O pacote com.unity.cloud.gltfast esta instalado no Package Manager.",
                    "Entendi"
                );
                return;
            }

            Vector3 savedDomePos = Vector3.zero;
            Quaternion savedDomeRot = Quaternion.identity;
            bool hadExistingDome = domeInScene != null;
            if (hadExistingDome)
            {
                savedDomePos = domeInScene.transform.position;
                savedDomeRot = domeInScene.transform.rotation;
            }
            else
            {
                GameObject pilotInScene = GameObject.Find("Stomach_Pilot_Chamber");
                if (pilotInScene != null)
                {
                    savedDomePos = pilotInScene.transform.position + new Vector3(62f, 0f, 0f);
                }
            }

            if (domeInScene != null && glbAsset != null)
            {
                bool replaceExisting = EditorUtility.DisplayDialog(
                    "Atualizar Cupula Gigante na Cena?",
                    "Ja existe um objeto 'Stomach_Main_Dome' na cena.\n\n" +
                    "Deseja substituir pela versao mais recente do arquivo .glb (mantendo a posicao onde voce colocou)?",
                    "Substituir pelo novo .glb",
                    "Manter malha atual"
                );

                if (replaceExisting)
                {
                    Undo.DestroyObjectImmediate(domeInScene);
                    domeInScene = null;
                }
            }

            ConfigurePixelTextures();

            if (!AssetDatabase.IsValidFolder(MaterialsFolder))
            {
                AssetDatabase.CreateFolder(BaseFolder, "Materials");
            }

            Material matFleshFloor = CreateOrUpdateMaterial("Mat_FleshFloor", "MapaEstomago/URP/PS1_LivingFlesh", "tex_flesh_floor.png", m =>
            {
                m.SetFloat("_BreathSpeed", 1.2f);
                m.SetFloat("_BreathAmp", 0.006f);
                m.SetFloat("_SplitAmount", 0.25f);
                m.SetFloat("_Wetness", 0.35f);
                m.SetFloat("_AmbientFloor", 0.25f);
            });

            Material matFleshWall = CreateFleshWallMaterial();

            Material matCartilage = CreateOrUpdateMaterial("Mat_Cartilage", "MapaEstomago/URP/PS1_LivingFlesh", "tex_cartilage.png", m =>
            {
                m.SetFloat("_BreathSpeed", 1.0f);
                m.SetFloat("_BreathAmp", 0.004f);
                m.SetFloat("_SplitAmount", 0.25f);
                m.SetFloat("_Wetness", 0.25f);
                m.SetFloat("_AmbientFloor", 0.28f);
            });

            Material matHellOrgans = CreateHellOrgansMaterial();
            Material matDomeRibArches = CreateDomeRibArchesMaterial();
            Material matPustule = CreatePustuleMaterial();
            Material matDigestedCalcified = CreateDigestedCalcifiedMaterial();

            Material matGastricAcid = CreateOrUpdateMaterial("Mat_GastricAcid", "MapaEstomago/URP/PS1_GastricAcid", "tex_acid.png", m =>
            {
                m.SetColor("_BaseColor", new Color(0.85f, 1f, 0.72f, 1f));
                m.SetColor("_EmissionColor", new Color(0.28f, 1.35f, 0.12f, 1f));
                m.SetVector("_ScrollSpeedA", new Vector4(0.045f, 0.032f, 0f, 0f));
                m.SetVector("_ScrollSpeedB", new Vector4(-0.028f, 0.052f, 0f, 0f));
                m.SetFloat("_WaveSpeed", 2.8f);
                m.SetFloat("_WaveAmp", 0.032f);
            });

            Material matBloodDrips = CreateBloodDripsMaterial();
            Material matBloodWaves = CreateBloodWavesMaterial();

            Material matPixelAcid = CreateOrUpdateMaterial("Mat_PixelAcidParticle", "MapaEstomago/URP/PS1_PixelParticle", "tex_pixel_particle.png", m =>
            {
                m.SetColor("_BaseColor", new Color(0.65f, 1f, 0.35f, 1f));
                m.SetFloat("_EmissionBoost", 2.0f);
            });

            Material matPixelBlood = CreateOrUpdateMaterial("Mat_PixelBloodParticle", "MapaEstomago/URP/PS1_PixelParticle", "tex_pixel_particle.png", m =>
            {
                m.SetColor("_BaseColor", new Color(0.95f, 0.14f, 0.16f, 1f));
                m.SetFloat("_EmissionBoost", 1.1f);
            });

            bool doorReady = BuildOrUpdateFleshEyeDoorPrefab(matFleshWall, matHellOrgans);
            AssetDatabase.SaveAssets();
            GameObject doorPrefab = doorReady ? AssetDatabase.LoadAssetAtPath<GameObject>(DoorPrefabPath) : null;

            if (domeInScene == null && glbAsset != null)
            {
                domeInScene = (GameObject)PrefabUtility.InstantiatePrefab(glbAsset);
                domeInScene.name = "Stomach_Main_Dome";
                domeInScene.transform.position = savedDomePos;
                domeInScene.transform.rotation = savedDomeRot;
            }

            if (PrefabUtility.IsPartOfPrefabInstance(domeInScene))
            {
                PrefabUtility.UnpackPrefabInstance(domeInScene, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            }

            Transform[] allTransforms = domeInScene.GetComponentsInChildren<Transform>(true);
            int bubbleCount = 0;
            int geyserCount = 0;
            int floatingIslandCount = 0;
            int pillarCount = 0;
            int pustuleCount = 0;

            foreach (Transform t in allTransforms)
            {
                if (t == null) continue;
                string n = t.gameObject.name;
                string parentName = t.parent != null ? t.parent.gameObject.name : "";
                MeshRenderer mr = t.GetComponent<MeshRenderer>();
                MeshFilter mf = t.GetComponent<MeshFilter>();

                string keyName = (mr != null && n.StartsWith("Primitive")) ? parentName : n;

                if (mr != null && mf != null)
                {
                    if (keyName == "Dome_Floor_Walkable")
                    {
                        mr.sharedMaterial = matFleshFloor;
                        EnsureMeshCollider(t.gameObject);
                    }
                    else if (keyName == "Dome_Bridges_Cartilage")
                    {
                        mr.sharedMaterial = matCartilage;
                        EnsureMeshCollider(t.gameObject);
                    }
                    else if (keyName == "Dome_Bridges_Tendon")
                    {
                        mr.sharedMaterial = matHellOrgans;
                        EnsureMeshCollider(t.gameObject);
                    }
                    else if (keyName == "Dome_Stepping_Stones_Calcified")
                    {
                        mr.sharedMaterial = matDigestedCalcified;
                        EnsureMeshCollider(t.gameObject);
                    }
                    else if (keyName.StartsWith("Dome_Floating_Island_"))
                    {
                        mr.sharedMaterial = matDigestedCalcified;
                        EnsureMeshCollider(t.gameObject);
                    }
                    else if (keyName == "Dome_Wall_Flesh_Back" || keyName == "Dome_Wall_Cutaway_Front")
                    {
                        mr.sharedMaterial = matFleshWall;
                        mr.shadowCastingMode = ShadowCastingMode.Off;
                        EnsureMeshCollider(t.gameObject);
                    }
                    else if (keyName == "Dome_Rib_Arches")
                    {
                        mr.sharedMaterial = matDomeRibArches;
                        mr.shadowCastingMode = ShadowCastingMode.Off;
                        EnsureMeshCollider(t.gameObject);
                    }
                    else if (keyName.StartsWith("Dome_Mucosal_Pillar_"))
                    {
                        mr.sharedMaterial = matFleshWall;
                        EnsureMeshCollider(t.gameObject);
                    }
                    else if (keyName.StartsWith("Dome_Pustule_Clusters_"))
                    {
                        mr.sharedMaterial = matPustule;
                        EnsureMeshCollider(t.gameObject);
                    }
                    else if (keyName == "Dome_Acid_Vents")
                    {
                        mr.sharedMaterial = matHellOrgans;
                        EnsureMeshCollider(t.gameObject);
                    }
                    else if (keyName == "Dome_Wall_Blood_Drips")
                    {
                        mr.sharedMaterial = matBloodDrips;
                        mr.shadowCastingMode = ShadowCastingMode.Off;
                    }
                    else if (keyName == "Dome_Wall_Blood_Waves")
                    {
                        mr.sharedMaterial = matBloodWaves;
                        mr.shadowCastingMode = ShadowCastingMode.Off;
                    }
                    else if (keyName == "Dome_Acid_Lake")
                    {
                        mr.sharedMaterial = matGastricAcid;
                        ConfigureAcidLakeNode(t.gameObject);
                    }
                    else if (keyName == "Dome_Acid_Crater_Pools")
                    {
                        mr.sharedMaterial = matGastricAcid;
                    }
                    else if (keyName.StartsWith("Dome_Acid_Bubble_"))
                    {
                        mr.sharedMaterial = matGastricAcid;
                        GameObject bubbleRoot = (n.StartsWith("Primitive") && t.parent != null) ? t.parent.gameObject : t.gameObject;
                        AcidBubblePopper popper = EnsureComponent<AcidBubblePopper>(bubbleRoot);
                        popper.pixelParticleMaterial = matPixelAcid;
                        bubbleCount++;
                    }
                }

                // Scripts nos nos raiz das estruturas e ancoras
                if (n.StartsWith("Dome_Floating_Island_"))
                {
                    FloatingFleshPlatform platform = EnsureComponent<FloatingFleshPlatform>(t.gameObject);
                    platform.bobAmplitude = 0.14f;
                    platform.bobFrequency = 0.55f;
                    platform.tiltAmplitudeDeg = 2.8f;
                    platform.tiltFrequency = 0.42f;
                    platform.phaseOffset = floatingIslandCount * 1.85f;
                    floatingIslandCount++;
                }
                else if (n.StartsWith("Dome_Mucosal_Pillar_"))
                {
                    OrganPulse pulse = EnsureComponent<OrganPulse>(t.gameObject);
                    pulse.beatsPerSecond = 0.72f;
                    pulse.lateralAmplitude = 0.045f;
                    pulse.verticalAmplitude = 0.025f;
                    pulse.phaseOffset = pillarCount * 0.25f;
                    pillarCount++;
                }
                else if (n.StartsWith("Dome_Pustule_Clusters_"))
                {
                    OrganPulse pulse = EnsureComponent<OrganPulse>(t.gameObject);
                    pulse.beatsPerSecond = 1.15f;
                    pulse.lateralAmplitude = 0.075f;
                    pulse.verticalAmplitude = 0.055f;
                    pulse.phaseOffset = pustuleCount * 0.35f;
                    pustuleCount++;
                }
                else if (n.StartsWith("Acid_Vent_Anchor_"))
                {
                    UlcerEruption erupt = EnsureComponent<UlcerEruption>(t.gameObject);
                    erupt.pixelParticleMaterial = matPixelAcid;
                    Light l = EnsureComponent<Light>(t.gameObject);
                    l.type = LightType.Point;
                    l.color = new Color(0.32f, 1.0f, 0.15f);
                    l.range = 7.5f;
                    l.intensity = 2.6f;
                    erupt.craterGlowLight = l;
                }
                else if (n.StartsWith("Island_Geyser_Anchor_"))
                {
                    AcidGeyserHazard geyser = EnsureComponent<AcidGeyserHazard>(t.gameObject);
                    geyser.pixelParticleMaterial = matPixelAcid;
                    Light l = EnsureComponent<Light>(t.gameObject);
                    l.type = LightType.Point;
                    l.color = new Color(0.32f, 1.0f, 0.16f);
                    l.range = 4f;
                    l.intensity = 0.4f;
                    geyser.warningLight = l;
                    geyserCount++;
                }
                else if (n.StartsWith("Dome_Blood_Drip_Anchor_"))
                {
                    BloodDripEmitter emitter = EnsureComponent<BloodDripEmitter>(t.gameObject);
                    emitter.pixelParticleMaterial = matPixelBlood;
                    emitter.dripsPerSecond = 12f;
                    emitter.spawnRadius = 9.5f;
                    emitter.dropHeight = 7.8f;
                }
                else if (n == "Entry_Flesh_Door_Anchor" || n == "Exit_Flesh_Door_Anchor")
                {
                    if (doorPrefab != null)
                    {
                        Transform existingDoor = t.Find("Flesh_Eye_Door");
                        GameObject doorInstance = existingDoor != null ? existingDoor.gameObject : null;
                        if (doorInstance == null)
                        {
                            doorInstance = (GameObject)PrefabUtility.InstantiatePrefab(doorPrefab, t);
                            if (doorInstance != null)
                            {
                                doorInstance.name = "Flesh_Eye_Door";
                            }
                        }

                        if (doorInstance != null)
                        {
                            doorInstance.transform.position = t.position;
                            Vector3 domeCenter = new Vector3(domeInScene.transform.position.x, t.position.y, domeInScene.transform.position.z);
                            Vector3 toCenter = domeCenter - t.position;
                            if (toCenter.sqrMagnitude > 0.0001f)
                            {
                                doorInstance.transform.rotation = Quaternion.LookRotation(toCenter, Vector3.up);
                            }

                            FleshEyeDoorController ctrl = doorInstance.GetComponent<FleshEyeDoorController>();
                            if (ctrl != null)
                            {
                                ctrl.phaseOffset = n.StartsWith("Exit") ? 0.4f : 0f;
                                ctrl.spitParticleMaterial = matPixelAcid;
                            }
                        }
                    }
                }
            }

            SetupMainDomeAtmosphereAndLights(domeInScene.transform);
            SetupIsometricCamera(28f, domeInScene.transform.position);
            EnableSubtleBloom();
            ConnectMapDoorsInScene(doorPrefab, matPixelAcid);

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();

            EditorUtility.DisplayDialog(
                "Cupula Gigante do Estomago Configurada!",
                $"Configuracao do Mapa Principal concluida:\n" +
                $"- {floatingIslandCount} ilhas flutuantes vivas (FloatingFleshPlatform)\n" +
                $"- {geyserCount} geiseres de acido interativos (AcidGeyserHazard)\n" +
                $"- {bubbleCount} bolhas explosivas + crateras eruptivas\n" +
                $"- Portas vivas de Carne + Olho posicionadas na Entrada e Saida\n" +
                $"- Iluminacao zonada (lago verde toxico + destaques carmesim/ambar) e Camera (dist=28)\n\n" +
                $"Aperte PLAY na Unity para testar a arena completa!",
                "Ir para a Cena"
            );
        }

        private static bool BuildOrUpdateFleshEyeDoorPrefab(Material matFleshWall, Material matHellOrgans)
        {
            if (File.Exists(DoorGlbPath))
            {
                AssetDatabase.ImportAsset(DoorGlbPath, ImportAssetOptions.ForceUpdate);
            }
            GameObject doorGlb = AssetDatabase.LoadAssetAtPath<GameObject>(DoorGlbPath);
            if (doorGlb == null) return false;

            Material matTeeth = CreateOrUpdateMaterial("Mat_Teeth", "MapaEstomago/URP/PS1_LivingFlesh", "tex_teeth.png", m =>
            {
                m.SetFloat("_BreathSpeed", 1.4f);
                m.SetFloat("_BreathAmp", 0.002f);
                m.SetFloat("_Wetness", 0.45f);
                m.SetFloat("_AmbientFloor", 0.28f);
            });

            Material matEyeball = CreateOrUpdateMaterial("Mat_Eyeball", "MapaEstomago/URP/PS1_LivingFlesh", "tex_eyeball.png", m =>
            {
                m.SetFloat("_BreathSpeed", 0f);
                m.SetFloat("_BreathAmp", 0f);
                m.SetFloat("_Wetness", 0.85f);
                m.SetFloat("_AmbientFloor", 0.34f);
            });

            GameObject tempDoor = (GameObject)PrefabUtility.InstantiatePrefab(doorGlb);
            if (tempDoor == null) return false;
            tempDoor.name = "Flesh_Eye_Door";

            if (PrefabUtility.IsPartOfPrefabInstance(tempDoor))
            {
                PrefabUtility.UnpackPrefabInstance(tempDoor, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            }

            Transform[] parts = tempDoor.GetComponentsInChildren<Transform>(true);
            foreach (Transform t in parts)
            {
                if (t == null) continue;
                MeshRenderer mr = t.GetComponent<MeshRenderer>();
                MeshFilter mf = t.GetComponent<MeshFilter>();
                if (mr == null || mf == null) continue;

                string keyName = t.gameObject.name.StartsWith("Primitive") && t.parent != null
                    ? t.parent.gameObject.name
                    : t.gameObject.name;

                if (keyName == "Door_Flesh_Frame")
                {
                    mr.sharedMaterial = matFleshWall;
                    EnsureMeshCollider(t.gameObject);
                }
                else if (keyName == "Door_Eyelid_Upper" || keyName == "Door_Eyelid_Lower" ||
                         keyName == "Door_Upper_Gum" || keyName == "Door_Lower_Jaw")
                {
                    mr.sharedMaterial = matFleshWall;
                }
                else if (keyName == "Door_Tendons")
                {
                    mr.sharedMaterial = matHellOrgans;
                }
                else if (keyName == "Door_Upper_Teeth")
                {
                    mr.sharedMaterial = matTeeth;
                }
                else if (keyName == "Door_Eyeball")
                {
                    mr.sharedMaterial = matEyeball;
                }
            }

            FleshEyeDoorController ctrl = EnsureComponent<FleshEyeDoorController>(tempDoor);
            ctrl.BindParts();

            Transform triggerTrans = tempDoor.transform.Find("Door_Transition_Trigger");
            GameObject triggerGo = triggerTrans != null ? triggerTrans.gameObject : new GameObject("Door_Transition_Trigger");
            triggerGo.transform.SetParent(tempDoor.transform, false);
            triggerGo.transform.localPosition = new Vector3(0f, 0.85f, 0.30f);
            BoxCollider trigCol = EnsureComponent<BoxCollider>(triggerGo);
            trigCol.isTrigger = true;
            trigCol.size = new Vector3(2.2f, 1.8f, 1.6f);
            EnsureComponent<DoorTransitionTrigger>(triggerGo);

            PrefabUtility.SaveAsPrefabAsset(tempDoor, DoorPrefabPath);
            UnityEngine.Object.DestroyImmediate(tempDoor);
            return true;
        }

        private static void ConfigurePixelTextures()
        {
            string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { TexturesFolder });
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null) continue;

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
                if (Path.GetFileName(path) == "tex_pixel_particle.png" && !importer.alphaIsTransparency)
                {
                    importer.alphaIsTransparency = true;
                    dirty = true;
                }

                if (dirty)
                {
                    importer.SaveAndReimport();
                }
            }
        }

        private static Material CreateOrUpdateMaterial(string matName, string shaderName, string textureFileName, System.Action<Material> configure)
        {
            string matPath = $"{MaterialsFolder}/{matName}.mat";
            Shader shader = Shader.Find(shaderName);
            if (shader == null)
            {
                Debug.LogWarning($"[StomachChamberSetup] Shader nao encontrado: {shaderName}. Usando URP/Lit como fallback.");
                shader = Shader.Find("Universal Render Pipeline/Lit");
            }

            Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, matPath);
            }
            else if (mat.shader != shader)
            {
                mat.shader = shader;
            }

            Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexturesFolder}/{textureFileName}");
            if (tex != null)
            {
                mat.SetTexture("_BaseMap", tex);
            }

            configure?.Invoke(mat);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static void DeleteAssetIfExists(string assetPath)
        {
            if (AssetDatabase.LoadMainAssetAtPath(assetPath) != null)
            {
                AssetDatabase.DeleteAsset(assetPath);
            }
        }

        private static void EnsureMeshCollider(GameObject go)
        {
            MeshCollider mc = go.GetComponent<MeshCollider>();
            if (mc == null) mc = go.AddComponent<MeshCollider>();
            mc.convex = false;
            mc.isTrigger = false;
        }

        private static void ConfigureAcidLakeNode(GameObject go)
        {
            MeshCollider oldMc = go.GetComponent<MeshCollider>();
            if (oldMc != null) Object.DestroyImmediate(oldMc);

            BoxCollider box = EnsureComponent<BoxCollider>(go);
            box.isTrigger = true;
            box.center = new Vector3(0f, -0.55f, 0f);

            Vector3 boxSize = new Vector3(28f, 0.55f, 24f);
            MeshFilter mf = go.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null)
            {
                Bounds bounds = mf.sharedMesh.bounds;
                boxSize.x = Mathf.Max(boxSize.x, bounds.size.x);
                boxSize.z = Mathf.Max(boxSize.z, bounds.size.z);
            }
            box.size = boxSize;

            AcidDamageZone dmg = EnsureComponent<AcidDamageZone>(go);
            dmg.damagePerSecondInside = 20f;
            dmg.lingerDurationAfterExit = 3.0f;
            dmg.lingerDamagePerSecond = 10f;
        }

        private static T EnsureComponent<T>(GameObject go) where T : Component
        {
            T comp = go.GetComponent<T>();
            if (comp == null) comp = go.AddComponent<T>();
            return comp;
        }

        private static void SetupAtmosphereAndLights(Transform chamberRoot)
        {
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogColor = new Color(0.06f, 0.012f, 0.022f, 1f);
            RenderSettings.fogDensity = 0.020f;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.18f, 0.05f, 0.07f, 1f);

            Light[] allLights = Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
            foreach (Light l in allLights)
            {
                if (l.type == LightType.Directional)
                {
                    l.color = new Color(1.0f, 0.72f, 0.68f);
                    l.intensity = 1.05f;
                    l.transform.rotation = Quaternion.Euler(52f, -35f, 0f);
                    break;
                }
            }

            Transform vfxFolder = chamberRoot.Find("Stomach_Acid_Ambient_Lights");
            if (vfxFolder == null)
            {
                GameObject folderGo = new GameObject("Stomach_Acid_Ambient_Lights");
                folderGo.transform.SetParent(chamberRoot, false);
                vfxFolder = folderGo.transform;

                CreatePointLight(vfxFolder, "AcidGlow_CenterFront", new Vector3(0f, 1.4f, -6.5f), new Color(0.28f, 1.0f, 0.14f), 11f, 3.2f);
                CreatePointLight(vfxFolder, "AcidGlow_North", new Vector3(0f, 1.6f, 4.8f), new Color(0.25f, 1.0f, 0.15f), 12f, 3.0f);
                CreatePointLight(vfxFolder, "FleshGlow_Warm", new Vector3(0f, 3.5f, 0f), new Color(1.0f, 0.25f, 0.20f), 16f, 2.2f);
            }

            SetupSilentHillStomachFog();
        }

        [MenuItem("Estomago/Alternar Neblina (ON ou OFF) %#f")]
        public static void ToggleStomachFogMenu()
        {
            ToggleStomachFog();
        }

        public static bool ToggleStomachFog()
        {
            StomachSilentHillFog fog = Object.FindFirstObjectByType<StomachSilentHillFog>(FindObjectsInactive.Include);
            if (fog == null)
            {
                Debug.LogWarning("[StomachChamberSetup] Objeto Stomach_SilentHill_Fog nao encontrado na cena.");
                return false;
            }

            bool willBeActive = !fog.gameObject.activeSelf || !fog.enabled || !fog.enableFog;
            fog.enableFog = willBeActive;
            fog.enabled = willBeActive;
            fog.gameObject.SetActive(willBeActive);

            if (willBeActive)
            {
                fog.ApplyShaderGlobals();
                Debug.Log("<color=#66FF66>[StomachChamberSetup] Neblina de Gases ATIVADA (Atmosfera Silent Hill 2 ligada).</color>");
            }
            else
            {
                fog.ClearShaderGlobals();
                Debug.Log("<color=#FF6666>[StomachChamberSetup] Neblina de Gases DESATIVADA (Cenario 100% limpo para visualizacao/edicao).</color>");
            }

            EditorUtility.SetDirty(fog);
            EditorUtility.SetDirty(fog.gameObject);
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            return willBeActive;
        }

        [MenuItem("Estomago/Configurar Neblina Silent Hill 2 (Gases do Estomago PS1)")]
        public static void SetupSilentHillStomachFogMenu()
        {
            StomachSilentHillFog fog = SetupSilentHillStomachFog();
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();
            if (fog != null)
            {
                Selection.activeGameObject = fog.gameObject;
            }
        }

        public static StomachSilentHillFog SetupSilentHillStomachFog()
        {
            if (!AssetDatabase.IsValidFolder(MaterialsFolder))
            {
                AssetDatabase.CreateFolder(BaseFolder, "Materials");
            }

            Material matGasWisp = CreateOrUpdateMaterial("Mat_StomachGasWisp", "MapaEstomago/URP/PS1_StomachGasWisp", "tex_acid.png", m =>
            {
                m.SetColor("_GasColor", new Color(0.26f, 0.48f, 0.18f, 0.22f));
                m.SetFloat("_VertexSnap", 0f);
                m.SetFloat("_DriftSpeed", 0.55f);
                m.SetFloat("_WaveAmp", 0.16f);
            });

            GameObject fogGo = GameObject.Find("Stomach_SilentHill_Fog");
            if (fogGo == null)
            {
                fogGo = new GameObject("Stomach_SilentHill_Fog");
                Undo.RegisterCreatedObjectUndo(fogGo, "Create Stomach Silent Hill Fog");
            }

            StomachSilentHillFog fog = EnsureComponent<StomachSilentHillFog>(fogGo);
            fog.clearRadius = 5.5f;
            fog.midRadius = 13.5f;
            fog.maxRadius = 23.5f;
            fog.midDensity = 0.55f;
            fog.maxDensity = 0.96f;
            fog.gasColorMid = new Color(0.30f, 0.52f, 0.22f, 1f);
            fog.gasColorFar = new Color(0.18f, 0.33f, 0.14f, 1f);
            fog.gasColorHighlight = new Color(0.42f, 0.68f, 0.26f, 1f);
            fog.heightFalloff = 2.8f;
            fog.turbulenceAmount = 0.32f;
            fog.noiseScale = 0.15f;
            fog.ps1DitherStrength = 1.0f;
            fog.lowPolyGasMaterial = matGasWisp;
            fog.enableLowPolyGasClouds = true;

            fog.EnsureLowPolyGasClouds();
            fog.ApplyShaderGlobals();
            EditorUtility.SetDirty(fog);
            return fog;
        }

        private static void SetupMainDomeAtmosphereAndLights(Transform domeRoot)
        {
            SetupSilentHillStomachFog();
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogColor = new Color(0.055f, 0.014f, 0.020f, 1f);
            RenderSettings.fogDensity = 0.014f;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.20f, 0.06f, 0.07f, 1f);

            Light[] allLights = Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
            foreach (Light l in allLights)
            {
                if (l.type == LightType.Directional)
                {
                    l.color = new Color(1.0f, 0.74f, 0.66f);
                    l.intensity = 1.12f;
                    l.transform.rotation = Quaternion.Euler(50f, -35f, 0f);
                    break;
                }
            }

            Transform vfxFolder = domeRoot.Find("Stomach_Dome_Zoned_Lights");
            if (vfxFolder != null)
            {
                Object.DestroyImmediate(vfxFolder.gameObject);
            }

            GameObject folderGo = new GameObject("Stomach_Dome_Zoned_Lights");
            folderGo.transform.SetParent(domeRoot, false);
            vfxFolder = folderGo.transform;

            // Brilho verde toxico sobre o lago de acido gastrico
            CreatePointLight(vfxFolder, "AcidLakeGlow_SouthFront", new Vector3(0f, 1.5f, -11.5f), new Color(0.26f, 1.0f, 0.14f), 16f, 3.6f);
            CreatePointLight(vfxFolder, "AcidLakeGlow_NorthBack", new Vector3(0f, 1.8f, 11.5f), new Color(0.24f, 1.0f, 0.16f), 16f, 3.4f);
            CreatePointLight(vfxFolder, "AcidLakeGlow_West", new Vector3(-12.5f, 1.6f, 0f), new Color(0.28f, 1.0f, 0.15f), 15f, 3.2f);
            CreatePointLight(vfxFolder, "AcidLakeGlow_East", new Vector3(12.5f, 1.6f, 0f), new Color(0.28f, 1.0f, 0.15f), 15f, 3.2f);

            // Destaques carmesim e ambar sobre as ilhas de combate e costelas da cupula
            CreatePointLight(vfxFolder, "ArenaHighlight_CenterAmber", new Vector3(0f, 4.8f, 0f), new Color(1.0f, 0.58f, 0.24f), 20f, 3.0f);
            CreatePointLight(vfxFolder, "ArenaHighlight_WestCrimson", new Vector3(-8.5f, 4.2f, 2.0f), new Color(1.0f, 0.24f, 0.20f), 16f, 2.6f);
            CreatePointLight(vfxFolder, "ArenaHighlight_EastCrimson", new Vector3(8.5f, 4.2f, -2.0f), new Color(1.0f, 0.24f, 0.20f), 16f, 2.6f);
            CreatePointLight(vfxFolder, "ArenaHighlight_UpperDomeCrimson", new Vector3(0f, 9.5f, 6.0f), new Color(0.95f, 0.22f, 0.18f), 22f, 2.4f);
        }

        private static void CreatePointLight(Transform parent, string name, Vector3 localPos, Color color, float range, float intensity)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            Light l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = color;
            l.range = range;
            l.intensity = intensity;
        }

        [MenuItem("Estomago/Conectar Portas Entre Mapas (Engolir + Mastigar + Cuspir)")]
        public static void ConnectMapDoorsMenu()
        {
            Material matFleshWall = CreateFleshWallMaterial();
            Material matHellOrgans = CreateHellOrgansMaterial();
            Material matPixelAcid = AssetDatabase.LoadAssetAtPath<Material>($"{MaterialsFolder}/Mat_PixelAcidParticle.mat");
            BuildOrUpdateFleshEyeDoorPrefab(matFleshWall, matHellOrgans);
            AssetDatabase.SaveAssets();

            GameObject doorPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(DoorPrefabPath);
            int connected = ConnectMapDoorsInScene(doorPrefab, matPixelAcid);
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

            EditorUtility.DisplayDialog(
                "Portas Conectadas!",
                $"Foram configuradas e interligadas {connected} portas vivas na cena!\n\n" +
                "Ao entrar na boca de uma porta (ou chegar perto e apertar F no modo Camera Livre):\n" +
                "1. A porta fecha os dentes e mastiga 3 vezes;\n" +
                "2. A tela escurece (Fade to Black);\n" +
                "3. Corta para a porta do outro mapa, que abre a boca e COSPE voce para fora!",
                "Perfeito!"
            );
        }

        private static int ConnectMapDoorsInScene(GameObject doorPrefab, Material matPixelAcid)
        {
            // Se a Camara Piloto estiver na cena e ainda nao tiver uma Flesh_Eye_Door, coloca na saida dela
            GameObject pilotRoot = GameObject.Find("Stomach_Pilot_Chamber");
            if (pilotRoot != null && doorPrefab != null)
            {
                FleshEyeDoorController existingPilotDoor = pilotRoot.GetComponentInChildren<FleshEyeDoorController>(true);
                if (existingPilotDoor == null)
                {
                    Transform exitSphincter = null;
                    foreach (Transform t in pilotRoot.GetComponentsInChildren<Transform>(true))
                    {
                        if (t.name == "Sphincter_Exit")
                        {
                            exitSphincter = t;
                            break;
                        }
                    }

                    Transform parentAnchor = exitSphincter != null ? exitSphincter : pilotRoot.transform;
                    GameObject doorInst = (GameObject)PrefabUtility.InstantiatePrefab(doorPrefab, parentAnchor);
                    if (doorInst != null)
                    {
                        doorInst.name = "Flesh_Eye_Door_PilotExit";
                        Vector3 pos = exitSphincter != null
                            ? exitSphincter.position
                            : pilotRoot.transform.position + new Vector3(8.5f, 0.15f, 1.5f);
                        pos.y = pilotRoot.transform.position.y + 0.15f;
                        doorInst.transform.position = pos;

                        Vector3 center = new Vector3(pilotRoot.transform.position.x, pos.y, pilotRoot.transform.position.z);
                        Vector3 toCenter = center - pos;
                        if (toCenter.sqrMagnitude > 0.001f)
                        {
                            doorInst.transform.rotation = Quaternion.LookRotation(toCenter.normalized, Vector3.up);
                        }
                    }
                }
            }

            // Garante que todas as portas na cena tenham o DoorTransitionTrigger configurado
            FleshEyeDoorController[] allDoors = Object.FindObjectsByType<FleshEyeDoorController>(FindObjectsSortMode.None);
            FleshEyeDoorController pilotDoor = null;
            FleshEyeDoorController domeEntryDoor = null;
            FleshEyeDoorController domeExitDoor = null;

            foreach (FleshEyeDoorController d in allDoors)
            {
                if (d == null) continue;
                d.BindParts();
                if (matPixelAcid != null) d.spitParticleMaterial = matPixelAcid;

                DoorTransitionTrigger trig = EnsureDoorHasTrigger(d.gameObject);
                string hierarchyPath = GetHierarchyPath(d.transform);

                if (hierarchyPath.Contains("Entry_Flesh_Door_Anchor"))
                {
                    domeEntryDoor = d;
                }
                else if (hierarchyPath.Contains("Exit_Flesh_Door_Anchor"))
                {
                    domeExitDoor = d;
                }
                else if (pilotDoor == null)
                {
                    pilotDoor = d;
                }
            }

            // Interliga as portas automaticamente:
            // Porta da Camara Piloto <-> Porta de Entrada da Cupula Gigante
            if (pilotDoor != null && domeEntryDoor != null)
            {
                DoorTransitionTrigger tPilot = EnsureDoorHasTrigger(pilotDoor.gameObject);
                DoorTransitionTrigger tDomeEntry = EnsureDoorHasTrigger(domeEntryDoor.gameObject);

                tPilot.destinationDoor = domeEntryDoor;
                tDomeEntry.destinationDoor = pilotDoor;

                if (domeExitDoor != null)
                {
                    DoorTransitionTrigger tDomeExit = EnsureDoorHasTrigger(domeExitDoor.gameObject);
                    tDomeExit.destinationDoor = pilotDoor;
                }
            }
            else if (domeEntryDoor != null && domeExitDoor != null)
            {
                // Se so houver a Cupula Gigante na cena, liga Entrada <-> Saida para testar!
                DoorTransitionTrigger tDomeEntry = EnsureDoorHasTrigger(domeEntryDoor.gameObject);
                DoorTransitionTrigger tDomeExit = EnsureDoorHasTrigger(domeExitDoor.gameObject);
                if (tDomeEntry.destinationDoor == null) tDomeEntry.destinationDoor = domeExitDoor;
                if (tDomeExit.destinationDoor == null) tDomeExit.destinationDoor = domeEntryDoor;
            }
            else if (allDoors.Length >= 2)
            {
                for (int i = 0; i < allDoors.Length; i++)
                {
                    DoorTransitionTrigger trig = EnsureDoorHasTrigger(allDoors[i].gameObject);
                    trig.destinationDoor = allDoors[(i + 1) % allDoors.Length];
                }
            }

            foreach (FleshEyeDoorController d in allDoors)
            {
                if (d != null) EditorUtility.SetDirty(d.gameObject);
            }

            return allDoors.Length;
        }

        private static DoorTransitionTrigger EnsureDoorHasTrigger(GameObject doorRoot)
        {
            DoorTransitionTrigger existing = doorRoot.GetComponentInChildren<DoorTransitionTrigger>(true);
            if (existing != null) return existing;

            Transform triggerTrans = doorRoot.transform.Find("Door_Transition_Trigger");
            GameObject triggerGo = triggerTrans != null ? triggerTrans.gameObject : new GameObject("Door_Transition_Trigger");
            triggerGo.transform.SetParent(doorRoot.transform, false);
            triggerGo.transform.localPosition = new Vector3(0f, 0.85f, 0.30f);

            BoxCollider trigCol = EnsureComponent<BoxCollider>(triggerGo);
            trigCol.isTrigger = true;
            trigCol.size = new Vector3(2.2f, 1.8f, 1.6f);

            return EnsureComponent<DoorTransitionTrigger>(triggerGo);
        }

        private static string GetHierarchyPath(Transform t)
        {
            string path = t.name;
            while (t.parent != null)
            {
                t = t.parent;
                path = t.name + "/" + path;
            }
            return path;
        }

        [MenuItem("Estomago/Configurar Cupula + Corte BG3 + Trava de Camera")]
        public static void SetupDomeAndBG3CameraMenu()
        {
            SetupDomeAndBG3Camera(silent: false);
        }

        public static void SetupDomeAndBG3Camera(bool silent = false)
        {
            if (!AssetDatabase.IsValidFolder(MaterialsFolder))
            {
                AssetDatabase.CreateFolder(BaseFolder, "Materials");
            }

            Material matFleshWall = CreateFleshWallMaterial();
            CreateHellOrgansMaterial();
            Material matDomeRibArches = CreateDomeRibArchesMaterial();
            Material matBloodDrips = CreateBloodDripsMaterial();
            Material matBloodWaves = CreateBloodWavesMaterial();

            // Garantir que pisos/pontes/elementos andaveis nao sejam recortados pelo raio BG3
            string[] walkableMats = { "Mat_FleshFloor", "Mat_Cartilage", "Mat_HellOrgans", "Mat_Pustule", "Mat_DigestedCalcified" };
            foreach (string matName in walkableMats)
            {
                Material m = AssetDatabase.LoadAssetAtPath<Material>(MaterialsFolder + "/" + matName + ".mat");
                if (m != null && m.HasProperty("_EnableBG3Cutout"))
                {
                    m.SetFloat("_EnableBG3Cutout", 0f);
                    EditorUtility.SetDirty(m);
                }
            }

            MeshRenderer[] renderers = Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None);
            int updatedRenderers = 0;
            foreach (MeshRenderer mr in renderers)
            {
                if (mr == null) continue;
                string n = mr.gameObject.name;
                string parentName = mr.transform.parent != null ? mr.transform.parent.gameObject.name : "";
                string keyName = n.StartsWith("Primitive") ? parentName : n;

                if (keyName == "Wall_Flesh_Back" || keyName == "Wall_Cutaway_Front" ||
                    keyName == "Dome_Wall_Flesh_Back" || keyName == "Dome_Wall_Cutaway_Front")
                {
                    mr.sharedMaterial = matFleshWall;
                    mr.shadowCastingMode = ShadowCastingMode.Off;
                    MeshFilter mf = mr.GetComponent<MeshFilter>();
                    MeshCollider mc = mr.GetComponent<MeshCollider>();
                    if (mc != null && mf != null) mc.sharedMesh = mf.sharedMesh;
                    EditorUtility.SetDirty(mr);
                    updatedRenderers++;
                }
                else if (keyName == "Dome_Rib_Arches")
                {
                    mr.sharedMaterial = matDomeRibArches;
                    mr.shadowCastingMode = ShadowCastingMode.Off;
                    MeshFilter mf = mr.GetComponent<MeshFilter>();
                    MeshCollider mc = mr.GetComponent<MeshCollider>();
                    if (mc != null && mf != null) mc.sharedMesh = mf.sharedMesh;
                    EditorUtility.SetDirty(mr);
                    updatedRenderers++;
                }
                else if (keyName == "Wall_Blood_Drips" || keyName == "Dome_Wall_Blood_Drips")
                {
                    mr.sharedMaterial = matBloodDrips;
                    mr.shadowCastingMode = ShadowCastingMode.Off;
                    EditorUtility.SetDirty(mr);
                    updatedRenderers++;
                }
                else if (keyName == "Wall_Blood_Waves" || keyName == "Dome_Wall_Blood_Waves")
                {
                    mr.sharedMaterial = matBloodWaves;
                    mr.shadowCastingMode = ShadowCastingMode.Off;
                    EditorUtility.SetDirty(mr);
                    updatedRenderers++;
                }
            }

            Camera cam = Camera.main;
            if (cam == null) cam = Object.FindFirstObjectByType<Camera>();
            if (cam != null)
            {
                IsometricCameraFollow iso = EnsureComponent<IsometricCameraFollow>(cam.gameObject);
                iso.pitchAngle = 44f;
                iso.yawAngle = 35f;
                iso.fieldOfView = 38f;
                iso.clampMovement = false;
                iso.clampToActiveChamber = true;
                iso.allowFreeRotationAndZoom = false;
                iso.AutoDetectChambers();
                if (iso.target == null)
                {
                    GameObject player = GameObject.FindWithTag("Player");
                    if (player != null) iso.target = player.transform;
                }
                iso.SnapToTargetImmediate();

                BG3WallCutoutController bg3 = EnsureComponent<BG3WallCutoutController>(cam.gameObject);
                bg3.enableCutout = true;
                bg3.cutoutRadiusAtPlayer = 4.8f;
                bg3.cameraConeMultiplier = 1.45f;
                bg3.ditherFeatherWidth = 2.4f;
                bg3.UpdateShaderGlobals();

                EditorUtility.SetDirty(cam.gameObject);
            }

            AssetDatabase.SaveAssets();
            if (!Application.isPlaying)
            {
                EditorSceneManager.MarkAllScenesDirty();
            }

            if (!silent)
            {
                Debug.Log($"[StomachChamberSetup] Cupula + Corte BG3 + Trava de Camera configurados ({updatedRenderers} renderers atualizados).");
            }
        }

        private static void SetupIsometricCamera(float distance = 22f, Vector3? focusCenter = null)
        {
            Camera cam = Camera.main;
            if (cam == null) cam = Object.FindFirstObjectByType<Camera>();
            if (cam == null) return;

            cam.backgroundColor = new Color(0.04f, 0.008f, 0.014f, 1f);
            cam.clearFlags = CameraClearFlags.SolidColor;

            IsometricCameraFollow iso = EnsureComponent<IsometricCameraFollow>(cam.gameObject);
            iso.pitchAngle = 44f;
            iso.yawAngle = 35f;
            iso.distance = distance;
            iso.fieldOfView = 38f;
            iso.clampMovement = false;
            iso.clampToActiveChamber = true;
            iso.allowFreeRotationAndZoom = false;
            iso.AutoDetectChambers();

            BG3WallCutoutController bg3 = EnsureComponent<BG3WallCutoutController>(cam.gameObject);
            bg3.enableCutout = true;
            bg3.UpdateShaderGlobals();

            Vector3 center = focusCenter ?? Vector3.zero;
            center.y = 0f;
            Quaternion rot = Quaternion.Euler(iso.pitchAngle, iso.yawAngle, 0f);
            cam.transform.rotation = rot;
            cam.transform.position = center - (rot * Vector3.forward) * iso.distance;
            cam.fieldOfView = iso.fieldOfView;
            iso.SetFreePivot(center, snapImmediate: true);
        }

        private static void EnableSubtleBloom()
        {
            VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>("Assets/Settings/DefaultVolumeProfile.asset");
            if (profile != null && profile.TryGet(out Bloom bloom))
            {
                bloom.active = true;
                bloom.threshold.Override(0.95f);
                bloom.intensity.Override(0.65f);
                bloom.scatter.Override(0.65f);
                EditorUtility.SetDirty(profile);
            }
        }
    }
}
