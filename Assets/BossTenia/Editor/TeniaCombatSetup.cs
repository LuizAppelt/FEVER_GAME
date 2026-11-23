using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BossTenia.Editor
{
    public static class TeniaCombatSetup
    {
        private const string PrefabPath = "Assets/BossTenia/Boss_Tenia_Liturgica.prefab";

        [MenuItem("Boss Tenia/Setup Combat Encounter & Prefab")]
        public static void SetupCombatEncounterMenu()
        {
            string summary = SetupCombatEncounter();
            Debug.Log("[TeniaCombatSetup] " + summary);
        }

        public static string SetupCombatEncounter()
        {
            GameObject prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefabAsset == null)
            {
                throw new System.InvalidOperationException($"Prefab nao encontrado em {PrefabPath}");
            }

            // 1. Atualiza o Prefab com os componentes de combate, weak-spots e hitbox relays
            GameObject prefabRoot = PrefabUtility.LoadPrefabContents(PrefabPath);
            int hitboxCount = 0;
            int weakSpotCount = 0;
            try
            {
                WireBossHierarchy(prefabRoot, out hitboxCount, out weakSpotCount);
                PrefabUtility.SaveAsPrefabAsset(prefabRoot, PrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefabRoot);
            }

            // 2. Limpa previews antigos da catedral e instancia o encontro jogavel em SampleScene
            DestroyIfExists("Boss_Tenia_GroundPreview");
            DestroyIfExists("Boss_Tenia_PillarPreview");
            DestroyIfExists("Boss_Tenia_Liturgica");

            GameObject loadedPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            GameObject bossInstance = (GameObject)PrefabUtility.InstantiatePrefab(loadedPrefab);
            bossInstance.name = "Boss_Tenia_Liturgica";
            bossInstance.transform.position = new Vector3(100.40f, 2.20f, 173.50f);
            bossInstance.transform.rotation = Quaternion.Euler(0f, 180f, 0f);

            WireBossHierarchy(bossInstance, out hitboxCount, out weakSpotCount);

            var controller = bossInstance.GetComponent<TeniaBossController>();
            var bounds = bossInstance.GetComponent<TeniaArenaBounds>();
            var ai = bossInstance.GetComponent<TeniaBossAI>();
            var health = bossInstance.GetComponent<TeniaBossHealth>();

            GameObject playerObj = GameObject.Find("Test_Player");
            if (playerObj == null) playerObj = GameObject.FindGameObjectWithTag("Player");
            GameObject naveAnchor = GameObject.Find("Nave_Center_Anchor");

            if (controller != null)
            {
                controller.arenaBounds = bounds;
                controller.aimTarget = playerObj != null ? playerObj.transform : null;
                controller.naveCenterFallback = naveAnchor != null ? naveAnchor.transform : null;
                controller.SetPoseImmediateGround();
            }

            if (ai != null)
            {
                ai.controller = controller;
                ai.health = health;
                ai.arenaBounds = bounds;
                ai.playerTarget = playerObj != null ? playerObj.transform : null;
            }

            Undo.RegisterCreatedObjectUndo(bossInstance, "Setup Malphas Boss Encounter");
            EditorSceneManager.MarkSceneDirty(bossInstance.scene);

            return $"Prefab e cena atualizados! Hitboxes={hitboxCount}, WeakSpots(AcidOrgans)={weakSpotCount}, Player={(playerObj != null ? playerObj.name : "null")}";
        }

        private static void WireBossHierarchy(GameObject root, out int hitboxCount, out int weakSpotCount)
        {
            hitboxCount = 0;
            weakSpotCount = 0;

            var controller = root.GetComponent<TeniaBossController>();
            var bounds = root.GetComponent<TeniaArenaBounds>();
            if (bounds == null) bounds = root.AddComponent<TeniaArenaBounds>();
            var health = root.GetComponent<TeniaBossHealth>();
            if (health == null) health = root.AddComponent<TeniaBossHealth>();
            health.maxHealth = 2400f;
            health.currentHealth = 2400f;
            var audio = root.GetComponent<TeniaAudioController>();
            if (audio == null) audio = root.AddComponent<TeniaAudioController>();
            var ai = root.GetComponent<TeniaBossAI>();
            if (ai == null) ai = root.AddComponent<TeniaBossAI>();
            ai.meleeSpeedP1 = 6.3f;
            ai.meleeSpeedP2 = 8.4f;
            ai.rangedRepositionSpeedP1 = 6.6f;
            ai.rangedRepositionSpeedP2 = 8.5f;
            ai.climbMoveSpeedP1 = 9.2f;
            ai.climbMoveSpeedP2 = 11.6f;
            ai.turnSpeedDegP1 = 175f;
            ai.turnSpeedDegP2 = 230f;
            ai.biteRange = 6.8f;
            ai.biteDamageP1 = 26f;
            ai.biteDamageP2 = 34f;
            ai.tailSweepRadius = 4.9f;
            ai.tailSweepDamageP1 = 23f;
            ai.tailSweepDamageP2 = 31f;
            ai.contactBodyDamageP1 = 14f;
            ai.contactBodyDamageP2 = 19f;
            ai.contactCooldown = 0.88f;
            ai.constrictGrabRadius = 3.55f;
            ai.constrictDuration = 2.65f;
            ai.constrictSqueezeInterval = 0.54f;
            ai.constrictSqueezeDamageP1 = 10f;
            ai.constrictSqueezeDamageP2 = 14f;
            ai.constrictEscapeDamageThreshold = 45f;
            ai.rangedVolleyIntervalP1 = 2.05f;
            ai.rangedVolleyIntervalP2 = 1.45f;
            ai.pillarVolleyIntervalP1 = 1.65f;
            ai.pillarVolleyIntervalP2 = 1.15f;
            ai.pillarLeapMaxRange = 24.0f;
            ai.pillarLeapImpactRadiusP1 = 4.6f;
            ai.pillarLeapImpactRadiusP2 = 5.4f;
            ai.pillarLeapDamageP1 = 32f;
            ai.pillarLeapDamageP2 = 42f;
            ai.spitTelegraphDurationP1 = 1.12f;
            ai.spitTelegraphDurationP2 = 0.88f;
            ai.acidImpactRadiusP1 = 2.60f;
            ai.acidImpactRadiusP2 = 3.05f;
            ai.acidImpactDamageP1 = 24f;
            ai.acidImpactDamageP2 = 32f;
            ai.puddleRadius = 2.55f;
            ai.puddleDuration = 7.0f;
            ai.puddleDps = 13f;

            if (controller != null)
            {
                controller.arenaBounds = bounds;
            }
            ai.controller = controller;
            ai.health = health;
            ai.arenaBounds = bounds;
            ai.audioController = audio;

            Transform[] children = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < children.Length; i++)
            {
                Transform t = children[i];
                if (t == null || t == root.transform) continue;

                string n = t.name;
                if (n == "Head" || n.StartsWith("Body_Segment_") || n == "Tail")
                {
                    var col = t.GetComponent<Collider>();
                    if (col == null)
                    {
                        var sc = t.gameObject.AddComponent<SphereCollider>();
                        sc.radius = n == "Head" ? 0.85f : (n == "Tail" ? 0.45f : 0.72f);
                        col = sc;
                    }
                    col.isTrigger = false;

                    var relay = t.GetComponent<TeniaHitboxRelay>();
                    if (relay == null) relay = t.gameObject.AddComponent<TeniaHitboxRelay>();
                    relay.ownerHealth = health;
                    relay.isWeakSpot = false;
                    relay.damageMultiplier = n == "Head" ? 1.25f : 1.0f;
                    hitboxCount++;
                }
                else if (n.StartsWith("AcidOrgan_") || n == "Acid_Maw_Gland")
                {
                    var col = t.GetComponent<Collider>();
                    if (col == null)
                    {
                        var sc = t.gameObject.AddComponent<SphereCollider>();
                        sc.radius = n == "Acid_Maw_Gland" ? 0.55f : 0.44f;
                        col = sc;
                    }
                    col.isTrigger = false;

                    var relay = t.GetComponent<TeniaHitboxRelay>();
                    if (relay == null) relay = t.gameObject.AddComponent<TeniaHitboxRelay>();
                    relay.ownerHealth = health;
                    relay.isWeakSpot = true;
                    relay.damageMultiplier = 1.75f;
                    weakSpotCount++;
                    hitboxCount++;
                }
            }
        }

        private static void DestroyIfExists(string name)
        {
            GameObject existing = GameObject.Find(name);
            if (existing != null)
            {
                Object.DestroyImmediate(existing);
            }
        }
    }
}
