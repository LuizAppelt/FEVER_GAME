using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace ImmuneEnemies.Editor
{
    /// <summary>
    /// Monta os prefabs 3D dos inimigos comuns (Abade Macrofago e Eosinofilo da Bilis)
    /// e popula as arenas da cena Stomach_Abyss com grupos prontos para combate.
    /// </summary>
    public static class ImmuneEnemySetup
    {
        private const string PrefabsFolder = "Assets/ImmuneEnemies/Prefabs";
        private const string MacrophagePrefabPath = PrefabsFolder + "/Enemy_AbadeMacrofago.prefab";
        private const string EosinophilPrefabPath = PrefabsFolder + "/Enemy_EosinofiloBilis.prefab";
        private const string SceneGroupName = "08_Immune_Enemies";

        [MenuItem("Tools/Immune Enemies/Gerar Modelos, Prefabs e Popular Arenas")]
        public static string SetupAll()
        {
            if (!AssetDatabase.IsValidFolder(PrefabsFolder))
            {
                ImmuneEnemyMeshBuilder.EnsureMeshesFolder();
                AssetDatabase.CreateFolder("Assets/ImmuneEnemies", "Prefabs");
            }

            ImmuneMaterialSet mats = ImmuneEnemyAssetGenerator.GenerateAll();
            GameObject macroPrefab = BuildMacrophagePrefab(mats);
            GameObject eosPrefab = BuildEosinophilPrefab(mats);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            int spawned = PopulateCurrentScene(macroPrefab, eosPrefab);
            return $"Prefabs gerados (Abade Macrofago & Eosinofilo da Bilis) e {spawned} inimigos posicionados em {SceneGroupName}!";
        }

        private static GameObject BuildMacrophagePrefab(ImmuneMaterialSet mats)
        {
            Mesh torsoMesh = ImmuneEnemyMeshBuilder.BuildMacrophageTorso();
            Mesh gutMesh = ImmuneEnemyMeshBuilder.BuildMacrophageAcidCore();
            Mesh slimeMesh = ImmuneEnemyMeshBuilder.BuildMacrophageSlimeStrands();
            Mesh jawLMesh = ImmuneEnemyMeshBuilder.BuildMacrophageJawHalf(-1f);
            Mesh jawRMesh = ImmuneEnemyMeshBuilder.BuildMacrophageJawHalf(1f);
            Mesh armLMesh = ImmuneEnemyMeshBuilder.BuildMacrophageArm(-1f);
            Mesh armRMesh = ImmuneEnemyMeshBuilder.BuildMacrophageArm(1f);
            Mesh legLMesh = ImmuneEnemyMeshBuilder.BuildMacrophageLeg(-1f);
            Mesh legRMesh = ImmuneEnemyMeshBuilder.BuildMacrophageLeg(1f);

            GameObject root = new GameObject("Enemy_AbadeMacrofago");
            CapsuleCollider hitCol = root.AddComponent<CapsuleCollider>();
            hitCol.center = new Vector3(0f, 1.18f, 0f);
            hitCol.radius = 0.94f;
            hitCol.height = 2.42f;

            MacrophageEnemyAI ai = root.AddComponent<MacrophageEnemyAI>();

            Transform body = CreateChild(root.transform, "Body_Root", Vector3.zero);
            Material[] torsoMats = { mats.MacrophageFlesh, mats.MacrophageMaw };
            CreateMultiMatMeshChild(body, "Torso", torsoMesh, torsoMats, Vector3.zero);
            Transform gut = CreateMeshChild(body, "DigestedGutCore", gutMesh, mats.MacrophageGutAcid, new Vector3(0f, 1.24f, 0.24f));
            CreateMeshChild(body, "MouthSlimeStrands", slimeMesh, mats.MacrophageGutAcid, new Vector3(0f, 1.32f, 0.62f));

            Material[] jawMats = { mats.MacrophageMaw, mats.MacrophageTeeth };
            Transform jawL = CreateMultiMatMeshChild(body, "Belly_Jaw_L", jawLMesh, jawMats, new Vector3(-0.16f, 1.32f, 0.54f));
            Transform jawR = CreateMultiMatMeshChild(body, "Belly_Jaw_R", jawRMesh, jawMats, new Vector3(0.16f, 1.32f, 0.54f));
            Transform armL = CreateMeshChild(body, "Arm_L", armLMesh, mats.MacrophageFlesh, new Vector3(-0.92f, 1.88f, 0.04f));
            Transform armR = CreateMeshChild(body, "Arm_R", armRMesh, mats.MacrophageFlesh, new Vector3(0.92f, 1.88f, 0.04f));
            Transform legL = CreateMeshChild(root.transform, "Leg_L", legLMesh, mats.MacrophageFlesh, new Vector3(-0.46f, 0.60f, 0f));
            Transform legR = CreateMeshChild(root.transform, "Leg_R", legRMesh, mats.MacrophageFlesh, new Vector3(0.46f, 0.60f, 0f));

            GameObject lightObj = new GameObject("BellyAcidLight");
            lightObj.transform.SetParent(gut, false);
            lightObj.transform.localPosition = new Vector3(0f, 0f, -0.05f);
            Light light = lightObj.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(0.42f, 1.0f, 0.16f);
            light.intensity = 0.28f;
            light.range = 1.25f;
            light.shadows = LightShadows.None;

            ai.bodyRoot = body;
            ai.jawLeft = jawL;
            ai.jawRight = jawR;
            ai.armLeft = armL;
            ai.armRight = armR;
            ai.legLeft = legL;
            ai.legRight = legR;
            ai.digestedGutCore = gut;
            ai.bellyLight = light;

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, MacrophagePrefabPath);
            UnityEngine.Object.DestroyImmediate(root);
            return saved;
        }

        private static GameObject BuildEosinophilPrefab(ImmuneMaterialSet mats)
        {
            Mesh cellMesh = ImmuneEnemyMeshBuilder.BuildEosinophilFacetedCell();
            Mesh granMesh = ImmuneEnemyMeshBuilder.BuildEosinophilCrystalPustule();
            Mesh wingLMesh = ImmuneEnemyMeshBuilder.BuildEosinophilCiliatedWing(-1f);
            Mesh wingRMesh = ImmuneEnemyMeshBuilder.BuildEosinophilCiliatedWing(1f);
            Mesh mandibleMesh = ImmuneEnemyMeshBuilder.BuildEosinophilMandibleCluster();

            GameObject root = new GameObject("Enemy_EosinofiloBilis");
            SphereCollider hitCol = root.AddComponent<SphereCollider>();
            hitCol.center = new Vector3(0f, 0.05f, 0f);
            hitCol.radius = 0.85f;

            EosinophilEnemyAI ai = root.AddComponent<EosinophilEnemyAI>();

            Transform hover = CreateMeshChild(root.transform, "HoverCellBody", cellMesh, mats.EosinophilMembrane, Vector3.zero);
            Material[] wingMats = { mats.EosinophilWings, mats.EosinophilMembrane };
            Transform wingL = CreateMultiMatMeshChild(hover, "Wing_L", wingLMesh, wingMats, new Vector3(-0.62f, 0.12f, -0.04f));
            Transform wingR = CreateMultiMatMeshChild(hover, "Wing_R", wingRMesh, wingMats, new Vector3(0.62f, 0.12f, -0.04f));
            Transform mandibles = CreateMeshChild(hover, "Mandible_Cluster", mandibleMesh, mats.MacrophageTeeth, new Vector3(0f, -0.58f, 0.08f));

            // 11 pustulas cristalinas apontando para fora ao longo da normal da esfera superior/lateral
            Vector3[] granDirs =
            {
                new Vector3( 0.00f,  0.96f,  0.22f),
                new Vector3(-0.42f,  0.86f,  0.34f),
                new Vector3( 0.42f,  0.86f,  0.34f),
                new Vector3( 0.68f,  0.64f,  0.36f),
                new Vector3(-0.68f,  0.64f,  0.36f),
                new Vector3( 0.25f,  0.74f, -0.62f),
                new Vector3(-0.25f,  0.74f, -0.62f),
                new Vector3( 0.82f,  0.38f, -0.18f),
                new Vector3(-0.82f,  0.38f, -0.18f),
                new Vector3( 0.56f,  0.18f,  0.74f),
                new Vector3( 0.00f,  0.68f,  0.70f)
            };
            float[] granScales = { 1.32f, 0.92f, 1.18f, 1.25f, 0.86f, 1.05f, 0.95f, 1.12f, 0.88f, 0.82f, 0.90f };

            Material[] granMats = { mats.EosinophilMembrane, mats.EosinophilGranules };
            List<Transform> granList = new List<Transform>(granDirs.Length);
            for (int i = 0; i < granDirs.Length; i++)
            {
                Vector3 n = granDirs[i].normalized;
                Vector3 surfPos = new Vector3(n.x * 0.68f, n.y * 0.62f, n.z * 0.68f);
                Transform g = CreateMultiMatMeshChild(hover, $"Granule_{i + 1:00}", granMesh, granMats, surfPos);
                g.localRotation = Quaternion.FromToRotation(Vector3.up, n);
                g.localScale = Vector3.one * granScales[i];
                granList.Add(g);
            }

            GameObject lightObj = new GameObject("GranuleGlowLight");
            lightObj.transform.SetParent(hover, false);
            lightObj.transform.localPosition = new Vector3(0f, 0.52f, 0f);
            Light light = lightObj.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(0.65f, 1.0f, 0.20f);
            light.intensity = 0.52f;
            light.range = 2.4f;
            light.shadows = LightShadows.None;

            ai.hoverBody = hover;
            ai.wingLeft = wingL;
            ai.wingRight = wingR;
            ai.mandibleCluster = mandibles;
            ai.granules = granList.ToArray();
            ai.glowLight = light;

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, EosinophilPrefabPath);
            UnityEngine.Object.DestroyImmediate(root);
            return saved;
        }

        private static int PopulateCurrentScene(GameObject macroPrefab, GameObject eosPrefab)
        {
            GameObject existingGroup = GameObject.Find(SceneGroupName);
            if (existingGroup != null)
            {
                Undo.DestroyObjectImmediate(existingGroup);
            }

            GameObject group = new GameObject(SceneGroupName);
            Undo.RegisterCreatedObjectUndo(group, "Popular Inimigos Imunes");

            // Pontos curados nas arenas inferiores de combate + plataforma leste perto do player
            var spawns = new (Vector3 approxPos, bool isMacrophage, float yaw)[]
            {
                // Arena Sul Entrada (Ossuary Entry)
                (new Vector3(-3.5f, 4f, -47.0f), true, 15f),
                (new Vector3(4.2f, 4f, -49.5f), false, -20f),

                // Hub Sul-Central
                (new Vector3(-5.0f, 5f, -16.0f), true, 45f),
                (new Vector3(4.8f, 5f, -14.0f), false, -35f),
                (new Vector3(0.0f, 5f, -21.0f), false, 0f),

                // Plato Ritual Central
                (new Vector3(-6.2f, 7f, 18.0f), true, 80f),
                (new Vector3(6.5f, 7f, 19.5f), true, -80f),
                (new Vector3(0.5f, 7f, 24.5f), false, 180f),

                // Mirante Leste & Bastião Oeste
                (new Vector3(33.5f, 8f, 14.5f), true, -90f),
                (new Vector3(36.0f, 8f, 18.5f), false, -110f),
                (new Vector3(-39.0f, 6f, 1.0f), true, 90f),
                (new Vector3(-37.0f, 6f, -3.5f), false, 70f),

                // Altar Norte antes do viaduto
                (new Vector3(-5.0f, 8f, 56.5f), true, 150f),
                (new Vector3(4.5f, 8f, 58.5f), false, -150f),

                // Deck elevado leste perto do viaduto (onde Test_Player (1) esta atualmente!)
                (new Vector3(31.5f, 68f, 192.0f), true, -115f),
                (new Vector3(26.5f, 68f, 198.5f), false, -135f)
            };

            int count = 0;
            for (int i = 0; i < spawns.Length; i++)
            {
                var s = spawns[i];
                Vector3 groundPos = s.approxPos;
                if (Physics.Raycast(new Vector3(s.approxPos.x, s.approxPos.y + 8f, s.approxPos.z), Vector3.down, out var hit, 25f, ~0, QueryTriggerInteraction.Ignore))
                {
                    groundPos = hit.point;
                }

                GameObject prefab = s.isMacrophage ? macroPrefab : eosPrefab;
                GameObject inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, group.transform);
                inst.transform.position = s.isMacrophage ? groundPos : (groundPos + Vector3.up * 2.45f);
                inst.transform.rotation = Quaternion.Euler(0f, s.yaw, 0f);
                inst.name = $"{(s.isMacrophage ? "AbadeMacrofago" : "EosinofiloBilis")}_{count + 1:00}";
                count++;
            }

            EditorSceneManager.MarkSceneDirty(group.scene);
            return count;
        }

        private static Transform CreateChild(Transform parent, string name, Vector3 localPos)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            return go.transform;
        }

        private static Transform CreateMeshChild(Transform parent, string name, Mesh mesh, Material mat, Vector3 localPos)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            MeshFilter mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            MeshRenderer mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.On;
            mr.receiveShadows = true;
            return go.transform;
        }

        private static Transform CreateMultiMatMeshChild(Transform parent, string name, Mesh mesh, Material[] sharedMats, Vector3 localPos)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            MeshFilter mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            MeshRenderer mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = sharedMats;
            mr.shadowCastingMode = ShadowCastingMode.On;
            mr.receiveShadows = true;
            return go.transform;
        }
    }
}
