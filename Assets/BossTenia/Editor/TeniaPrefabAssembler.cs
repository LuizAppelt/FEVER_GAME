using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BossTenia
{
    /// <summary>
    /// Assembles the complete "Boss_Tenia_Liturgica" prefab (Option 3: Blind Scolex with Iron Nail Halo)
    /// from procedural PS1 textures/materials, watertight meshes, and TeniaBossController,
    /// and spawns GroundSlither + PillarCoiled preview instances inside Stomach_Boss_Cathedral.
    /// </summary>
    public static class TeniaPrefabAssembler
    {
        private const string RootFolder = "Assets/BossTenia";
        private const string MeshesFolder = "Assets/BossTenia/Meshes";
        private const string PrefabPath = "Assets/BossTenia/Boss_Tenia_Liturgica.prefab";

        private const int BodySegmentCount = 24;
        private const float BaseSegmentRadius = 0.66f;
        private const float BaseSegmentLength = 1.38f;
        private const float GroundSegmentSpacing = 0.90f;
        private const float PillarRadius = 0.98f;

        private static readonly Vector3 GroundPreviewWorldPos = new Vector3(99.50f, 2.20f, 166.50f);
        private static readonly Vector3 RightPierBay3WorldPos = new Vector3(109.90f, 2.20f, 170.60f);

        [MenuItem("BossTenia/Assemble Prefab and Scene Previews", priority = 20)]
        public static void AssembleAll()
        {
            EnsureFolderExists(RootFolder);
            EnsureFolderExists(MeshesFolder);

            // 1. Generate textures & materials and tune gothic lighting readability
            TeniaMaterialSet mats = TeniaTextureGenerator.CreateOrUpdateTexturesAndMaterials();
            TuneMaterialsForCathedral(mats);

            // 2. Build & persist all modular meshes cleanly
            Mesh rawHead = TeniaMeshBuilder.BuildScolexHeadMesh();
            Mesh headSplitMesh = SplitScolexHeadByUv(rawHead, lipThresholdU: 0.640f);
            Object.DestroyImmediate(rawHead);
            Mesh headMesh = SaveMeshAsset(headSplitMesh, $"{MeshesFolder}/Mesh_Tenia_ScolexHead.asset");

            Mesh teethMesh = SaveMeshAsset(TeniaMeshBuilder.BuildTeethRingMesh(), $"{MeshesFolder}/Mesh_Tenia_TeethRing.asset");
            Mesh haloMesh = SaveMeshAsset(TeniaMeshBuilder.BuildIronHaloMesh(), $"{MeshesFolder}/Mesh_Tenia_IronHalo.asset");
            Mesh tailMesh = SaveMeshAsset(
                TeniaMeshBuilder.BuildTailSegmentMesh(BaseSegmentRadius * 0.68f, 1.50f),
                $"{MeshesFolder}/Mesh_Tenia_TailSegment.asset");
            Mesh acidOrganMesh = SaveMeshAsset(
                TeniaMeshBuilder.BuildAcidOrganMesh(BaseSegmentRadius * 0.98f, BaseSegmentLength * 0.80f),
                $"{MeshesFolder}/Mesh_Tenia_AcidOrgan.asset");
            Mesh nailMesh = SaveMeshAsset(TeniaMeshBuilder.BuildBodyNailMesh(0.42f), $"{MeshesFolder}/Mesh_Tenia_BodyNail.asset");

            Mesh[] bodyMeshes = new Mesh[BodySegmentCount];
            for (int i = 0; i < BodySegmentCount; i++)
            {
                float radiusFactor = EvaluateSegmentRadiusFactor(i, BodySegmentCount);
                Mesh segMesh = TeniaMeshBuilder.BuildBodySegmentMesh(
                    BaseSegmentRadius * radiusFactor,
                    BaseSegmentLength,
                    seed: i + 1);
                bodyMeshes[i] = SaveMeshAsset(segMesh, $"{MeshesFolder}/Mesh_Tenia_BodySeg_{i:00}.asset");
            }

            // Variant 0 = long head stole; Variant 1 = long neck stole; Variants 2..5 = floor-safe compact body vestments
            Mesh[] vestmentMeshes = new Mesh[6];
            vestmentMeshes[0] = SaveMeshAsset(
                TeniaMeshBuilder.BuildVestmentBannerMesh(0.56f, 1.62f, seed: 3),
                $"{MeshesFolder}/Mesh_Tenia_Vestment_00.asset");
            vestmentMeshes[1] = SaveMeshAsset(
                TeniaMeshBuilder.BuildVestmentBannerMesh(0.58f, 1.24f, seed: 7),
                $"{MeshesFolder}/Mesh_Tenia_Vestment_01.asset");
            for (int v = 2; v < vestmentMeshes.Length; v++)
            {
                float w = 0.48f + 0.05f * (v % 3);
                float len = 0.78f + 0.04f * (v % 2);
                vestmentMeshes[v] = SaveMeshAsset(
                    TeniaMeshBuilder.BuildVestmentBannerMesh(w, len, seed: v * 5 + 11),
                    $"{MeshesFolder}/Mesh_Tenia_Vestment_{v:00}.asset");
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            // 3. Assemble clean Prefab root Hierarchy
            GameObject bossRoot = BuildBossHierarchy(
                "Boss_Tenia_Liturgica",
                mats,
                headMesh,
                teethMesh,
                haloMesh,
                tailMesh,
                acidOrganMesh,
                nailMesh,
                bodyMeshes,
                vestmentMeshes);

            GameObject savedPrefab = PrefabUtility.SaveAsPrefabAsset(bossRoot, PrefabPath);
            Object.DestroyImmediate(bossRoot);

            if (savedPrefab == null)
            {
                Debug.LogError($"[TeniaPrefabAssembler] Failed to save prefab at {PrefabPath}");
                return;
            }

            // 4. Spawn & pose Ground and Pillar previews inside Stomach_Boss_Cathedral
            PlaceScenePreviews(savedPrefab);

            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log($"[TeniaPrefabAssembler] Rebuilt {PrefabPath} ({BodySegmentCount} continuous segments) and placed Ground + Pillar previews.");
        }

        private static void TuneMaterialsForCathedral(TeniaMaterialSet mats)
        {
            if (mats.PaleSkin != null)
            {
                if (mats.PaleSkin.HasProperty("_AmbientFloor")) mats.PaleSkin.SetFloat("_AmbientFloor", 0.34f);
                EditorUtility.SetDirty(mats.PaleSkin);
            }

            if (mats.ScolexMaw != null)
            {
                if (mats.ScolexMaw.HasProperty("_UseVertexColor")) mats.ScolexMaw.SetFloat("_UseVertexColor", 1f);
                if (mats.ScolexMaw.HasProperty("_AmbientFloor")) mats.ScolexMaw.SetFloat("_AmbientFloor", 0.42f);
                if (mats.ScolexMaw.HasProperty("_EmissionColor"))
                {
                    mats.ScolexMaw.SetColor("_EmissionColor", new Color(0.11f, 0.015f, 0.020f, 1f));
                    mats.ScolexMaw.EnableKeyword("_EMISSION");
                }
                EditorUtility.SetDirty(mats.ScolexMaw);
            }

            if (mats.Teeth != null)
            {
                if (mats.Teeth.HasProperty("_AmbientFloor")) mats.Teeth.SetFloat("_AmbientFloor", 0.46f);
                EditorUtility.SetDirty(mats.Teeth);
            }

            if (mats.Vestments != null)
            {
                if (mats.Vestments.HasProperty("_AmbientFloor")) mats.Vestments.SetFloat("_AmbientFloor", 0.50f);
                if (mats.Vestments.HasProperty("_Wetness")) mats.Vestments.SetFloat("_Wetness", 0.25f);
                if (mats.Vestments.HasProperty("_EmissionColor"))
                {
                    mats.Vestments.SetColor("_EmissionColor", new Color(0.06f, 0.014f, 0.010f, 1f));
                    mats.Vestments.EnableKeyword("_EMISSION");
                }
                EditorUtility.SetDirty(mats.Vestments);
            }

            if (mats.RustedIron != null)
            {
                if (mats.RustedIron.HasProperty("_AmbientFloor")) mats.RustedIron.SetFloat("_AmbientFloor", 0.44f);
                if (mats.RustedIron.HasProperty("_Wetness")) mats.RustedIron.SetFloat("_Wetness", 0.48f);
                EditorUtility.SetDirty(mats.RustedIron);
            }

            if (mats.AcidOrgan != null)
            {
                if (mats.AcidOrgan.HasProperty("_EmissionColor"))
                {
                    mats.AcidOrgan.SetColor("_EmissionColor", new Color(0.28f, 0.90f, 0.16f, 1f));
                    mats.AcidOrgan.EnableKeyword("_EMISSION");
                }
                EditorUtility.SetDirty(mats.AcidOrgan);
            }
        }

        private static GameObject BuildBossHierarchy(
            string rootName,
            TeniaMaterialSet mats,
            Mesh headMesh,
            Mesh teethMesh,
            Mesh haloMesh,
            Mesh tailMesh,
            Mesh acidOrganMesh,
            Mesh nailMesh,
            Mesh[] bodyMeshes,
            Mesh[] vestmentMeshes)
        {
            GameObject root = new GameObject(rootName);
            TeniaBossController ctrl = root.AddComponent<TeniaBossController>();
            ctrl.segmentRadius = BaseSegmentRadius;
            ctrl.bodySegmentsOrderedHeadToTail = true;
            ctrl.segmentSpacing = GroundSegmentSpacing;
            ctrl.headElevation = 2.85f;
            ctrl.pillarRadius = PillarRadius;
            ctrl.coilStartHeight = 1.65f;
            ctrl.coilTopHeight = 8.85f;
            ctrl.coilTurns = 1.75f;
            ctrl.headHangOutward = 2.40f;
            ctrl.headHangDrop = 1.55f;
            ctrl.bodySegments = new List<Transform>(BodySegmentCount);
            ctrl.hangingVestments = new List<Transform>(18);

            // --- HEAD (Submesh 0 = PaleSkin cranium, Submesh 1 = ScolexMaw lip & throat) ---
            GameObject headGo = new GameObject("Head");
            headGo.transform.SetParent(root.transform, false);
            headGo.AddComponent<MeshFilter>().sharedMesh = headMesh;
            headGo.AddComponent<MeshRenderer>().sharedMaterials = new[] { mats.PaleSkin, mats.ScolexMaw };
            SphereCollider headCol = headGo.AddComponent<SphereCollider>();
            headCol.center = new Vector3(0f, 0f, 0.35f);
            headCol.radius = 0.88f;
            ctrl.headSegment = headGo.transform;

            GameObject teethGo = new GameObject("TeethRing");
            teethGo.transform.SetParent(headGo.transform, false);
            teethGo.AddComponent<MeshFilter>().sharedMesh = teethMesh;
            teethGo.AddComponent<MeshRenderer>().sharedMaterial = mats.Teeth;

            GameObject haloGo = new GameObject("IronHalo");
            haloGo.transform.SetParent(headGo.transform, false);
            haloGo.AddComponent<MeshFilter>().sharedMesh = haloMesh;
            haloGo.AddComponent<MeshRenderer>().sharedMaterial = mats.RustedIron;

            GameObject mouthAnchorGo = new GameObject("Acid_Mouth_Anchor");
            mouthAnchorGo.transform.SetParent(headGo.transform, false);
            mouthAnchorGo.transform.localPosition = new Vector3(0f, -0.05f, 1.05f);
            mouthAnchorGo.transform.localRotation = Quaternion.identity;
            ctrl.acidMouthAnchor = mouthAnchorGo.transform;

            // Deep gullet acid bile sac + subtle eerie green glow
            GameObject mawGlowGo = new GameObject("Acid_Maw_Gland");
            mawGlowGo.transform.SetParent(headGo.transform, false);
            mawGlowGo.transform.localPosition = new Vector3(0f, -0.04f, 0.42f);
            mawGlowGo.transform.localScale = new Vector3(0.30f, 0.28f, 0.36f);
            mawGlowGo.AddComponent<MeshFilter>().sharedMesh = acidOrganMesh;
            mawGlowGo.AddComponent<MeshRenderer>().sharedMaterial = mats.AcidOrgan;
            Light mawLight = mawGlowGo.AddComponent<Light>();
            mawLight.type = LightType.Point;
            mawLight.color = new Color(0.32f, 1.0f, 0.18f, 1f);
            mawLight.intensity = 1.55f;
            mawLight.range = 4.2f;

            // Pair of long priestly head stoles pinned right behind the iron halo
            AddPinnedVestment(
                headGo.transform,
                "Vestment_Head_L",
                new Vector3(-0.84f, -0.08f, -0.04f),
                vestmentMeshes[0],
                mats.Vestments,
                nailMesh,
                mats.RustedIron,
                ctrl.hangingVestments);
            AddPinnedVestment(
                headGo.transform,
                "Vestment_Head_R",
                new Vector3(0.84f, -0.08f, -0.04f),
                vestmentMeshes[0],
                mats.Vestments,
                nailMesh,
                mats.RustedIron,
                ctrl.hangingVestments);

            // --- BODY SEGMENTS (00 = neck .. BodySegmentCount-1 = tail base) ---
            for (int i = 0; i < BodySegmentCount; i++)
            {
                float radiusFactor = EvaluateSegmentRadiusFactor(i, BodySegmentCount);
                float segRadius = BaseSegmentRadius * radiusFactor;

                GameObject segGo = new GameObject($"Body_Segment_{i:00}");
                segGo.transform.SetParent(root.transform, false);
                segGo.AddComponent<MeshFilter>().sharedMesh = bodyMeshes[i];
                segGo.AddComponent<MeshRenderer>().sharedMaterial = mats.PaleSkin;

                SphereCollider segCol = segGo.AddComponent<SphereCollider>();
                segCol.center = Vector3.zero;
                segCol.radius = segRadius * 0.92f;

                ctrl.bodySegments.Add(segGo.transform);

                // Lateral/ventral bulging glowing gastric bile sacs on segments 3..9
                // Rotated onto lateral belly flanks (+-100 deg roll) so pale spine proglottids stay intact above
                if (i >= 3 && i <= 9)
                {
                    float bulgeSide = (i % 2 == 0) ? -1f : 1f;
                    GameObject organGo = new GameObject($"AcidOrgan_{i:00}");
                    organGo.transform.SetParent(segGo.transform, false);
                    organGo.transform.localPosition = new Vector3(bulgeSide * segRadius * 0.08f, -segRadius * 0.04f, 0f);
                    organGo.transform.localRotation = Quaternion.Euler(0f, 0f, bulgeSide * 102f);
                    organGo.transform.localScale = new Vector3(radiusFactor * 1.03f, radiusFactor * 1.01f, 0.86f);
                    organGo.AddComponent<MeshFilter>().sharedMesh = acidOrganMesh;
                    organGo.AddComponent<MeshRenderer>().sharedMaterial = mats.AcidOrgan;

                    if (i == 4 || i == 7)
                    {
                        Light sacLight = organGo.AddComponent<Light>();
                        sacLight.type = LightType.Point;
                        sacLight.color = new Color(0.32f, 1.0f, 0.20f, 1f);
                        sacLight.intensity = 1.45f;
                        sacLight.range = 4.0f;
                    }
                }

                // Tattered crimson/gold ecclesiastical vestments pinned along alternating flanks
                // Seg 1 is elevated neck/shoulder (long stole); floor segments use compact banners pinned upper-laterally
                if (i == 1 || i == 4 || i == 7 || i == 11 || i == 15 || i == 19)
                {
                    int side = (i == 1 || i == 7 || i == 15) ? -1 : 1;
                    int meshIdx = (i == 1) ? 1 : (2 + (i % (vestmentMeshes.Length - 2)));
                    float anchorY = (i == 1) ? -segRadius * 0.12f : segRadius * 0.24f;
                    AddPinnedVestment(
                        segGo.transform,
                        $"Vestment_Seg_{i:00}",
                        new Vector3(side * segRadius * 0.90f, anchorY, 0f),
                        vestmentMeshes[meshIdx],
                        mats.Vestments,
                        nailMesh,
                        mats.RustedIron,
                        ctrl.hangingVestments);
                }
            }

            // --- TAIL SEGMENT ---
            GameObject tailGo = new GameObject("Tail");
            tailGo.transform.SetParent(root.transform, false);
            tailGo.AddComponent<MeshFilter>().sharedMesh = tailMesh;
            tailGo.AddComponent<MeshRenderer>().sharedMaterial = mats.PaleSkin;
            SphereCollider tailCol = tailGo.AddComponent<SphereCollider>();
            tailCol.center = new Vector3(0f, 0f, -0.35f);
            tailCol.radius = BaseSegmentRadius * 0.55f;
            ctrl.tailSegment = tailGo.transform;

            ctrl.ApplyGroundPose(0f);
            return root;
        }

        private static void AddPinnedVestment(
            Transform parentSegment,
            string vestmentName,
            Vector3 localPos,
            Mesh vestmentMesh,
            Material vestmentMat,
            Mesh nailMesh,
            Material ironMat,
            List<Transform> registerList)
        {
            GameObject vestGo = new GameObject(vestmentName);
            vestGo.transform.SetParent(parentSegment, false);
            vestGo.transform.localPosition = localPos;
            vestGo.transform.localRotation = Quaternion.identity;
            vestGo.AddComponent<MeshFilter>().sharedMesh = vestmentMesh;
            vestGo.AddComponent<MeshRenderer>().sharedMaterial = vestmentMat;

            GameObject nailGo = new GameObject(vestmentName.Replace("Vestment_", "BodyNail_"));
            nailGo.transform.SetParent(vestGo.transform, false);
            nailGo.transform.localPosition = new Vector3(0f, -0.06f, 0.025f);
            nailGo.transform.localRotation = Quaternion.identity;
            nailGo.AddComponent<MeshFilter>().sharedMesh = nailMesh;
            nailGo.AddComponent<MeshRenderer>().sharedMaterial = ironMat;

            registerList.Add(vestGo.transform);
        }

        private static void PlaceScenePreviews(GameObject bossPrefab)
        {
            DestroyExistingByName("Boss_Tenia_GroundPreview");
            DestroyExistingByName("Boss_Tenia_PillarPreview");

            GameObject cathedral = GameObject.Find("Stomach_Boss_Cathedral");
            Transform naveAnchor = null;
            if (cathedral != null)
            {
                foreach (Transform t in cathedral.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name == "Nave_Center_Anchor")
                    {
                        naveAnchor = t;
                        break;
                    }
                }
            }

            // 1. GroundSlither preview poised across the Cathedral eye medallion facing the nave entrance
            GameObject groundInst = (GameObject)PrefabUtility.InstantiatePrefab(bossPrefab);
            groundInst.name = "Boss_Tenia_GroundPreview";
            Undo.RegisterCreatedObjectUndo(groundInst, "Create Boss Tenia Ground Preview");

            Vector3 groundPos = GroundPreviewWorldPos;
            if (Physics.Raycast(new Vector3(groundPos.x, groundPos.y + 8f, groundPos.z), Vector3.down, out RaycastHit floorHit, 20f))
            {
                groundPos.y = floorHit.point.y;
            }
            else if (cathedral != null)
            {
                groundPos.y = cathedral.transform.position.y;
            }

            groundInst.transform.position = groundPos;
            groundInst.transform.rotation = Quaternion.Euler(0f, 168f, 0f);
            TeniaBossController groundCtrl = groundInst.GetComponent<TeniaBossController>();
            groundCtrl.naveCenterFallback = naveAnchor;
            groundCtrl.aimTarget = null; // Level, cobra-like menacing stare down the nave
            groundCtrl.poseState = TeniaBossController.BossPoseState.GroundSlither;
            groundCtrl.ApplyGroundPose(0f);
            EditorUtility.SetDirty(groundCtrl);

            // 2. PillarCoiled preview tightly coiled around right nave pier bay 3 aiming into nave center
            GameObject pillarInst = (GameObject)PrefabUtility.InstantiatePrefab(bossPrefab);
            pillarInst.name = "Boss_Tenia_PillarPreview";
            Undo.RegisterCreatedObjectUndo(pillarInst, "Create Boss Tenia Pillar Preview");

            Vector3 pierPos = (cathedral != null)
                ? cathedral.transform.position + new Vector3(9.5f, 0f, 25.0f)
                : RightPierBay3WorldPos;

            pillarInst.transform.position = pierPos;
            pillarInst.transform.rotation = Quaternion.identity;
            TeniaBossController pillarCtrl = pillarInst.GetComponent<TeniaBossController>();
            pillarCtrl.pillarOriginIsWorld = false;
            pillarCtrl.pillarLocalOrigin = Vector3.zero;
            pillarCtrl.pillarRadius = PillarRadius;
            pillarCtrl.naveCenterFallback = naveAnchor;
            pillarCtrl.aimTarget = naveAnchor;
            pillarCtrl.poseState = TeniaBossController.BossPoseState.PillarCoiled;
            pillarCtrl.ApplyPillarCoiledPose(pierPos, PillarRadius);
            EditorUtility.SetDirty(pillarCtrl);
        }

        private static float EvaluateSegmentRadiusFactor(int idx, int total)
        {
            if (idx <= 1)
            {
                return idx == 0 ? 0.86f : 0.94f;
            }

            float t = idx / (float)(total - 1);
            if (t < 0.45f)
            {
                float swell = Mathf.Sin((t / 0.45f) * Mathf.PI * 0.5f);
                return Mathf.Lerp(0.95f, 1.18f, swell);
            }

            float taper = (t - 0.45f) / 0.55f;
            return Mathf.Lerp(1.18f, 0.70f, taper * taper * (3f - 2f * taper));
        }

        private static Mesh SplitScolexHeadByUv(Mesh src, float lipThresholdU)
        {
            Mesh m = new Mesh { name = src.name };
            m.SetVertices(src.vertices);
            m.SetUVs(0, src.uv);
            m.SetColors(src.colors);

            Vector2[] uvs = src.uv;
            int[] tris = src.triangles;
            List<int> sub0 = new List<int>(tris.Length);
            List<int> sub1 = new List<int>(tris.Length / 2);

            for (int i = 0; i < tris.Length; i += 3)
            {
                int a = tris[i];
                int b = tris[i + 1];
                int c = tris[i + 2];
                float avgU = (uvs[a].x + uvs[b].x + uvs[c].x) / 3f;

                if (avgU >= lipThresholdU)
                {
                    sub1.Add(a);
                    sub1.Add(b);
                    sub1.Add(c);
                }
                else
                {
                    sub0.Add(a);
                    sub0.Add(b);
                    sub0.Add(c);
                }
            }

            m.subMeshCount = 2;
            m.SetTriangles(sub0, 0);
            m.SetTriangles(sub1, 1);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        private static Mesh SaveMeshAsset(Mesh mesh, string assetPath)
        {
            if (AssetDatabase.LoadAssetAtPath<Object>(assetPath) != null)
            {
                AssetDatabase.DeleteAsset(assetPath);
            }

            AssetDatabase.CreateAsset(mesh, assetPath);
            return AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);
        }

        private static void DestroyExistingByName(string goName)
        {
            GameObject existing = GameObject.Find(goName);
            while (existing != null)
            {
                Undo.DestroyObjectImmediate(existing);
                existing = GameObject.Find(goName);
            }
        }

        private static void EnsureFolderExists(string folderPath)
        {
            if (AssetDatabase.IsValidFolder(folderPath))
            {
                return;
            }

            string normalized = folderPath.Replace('\\', '/').TrimEnd('/');
            int slash = normalized.LastIndexOf('/');
            string parent = normalized.Substring(0, slash);
            string child = normalized.Substring(slash + 1);
            EnsureFolderExists(parent);
            AssetDatabase.CreateFolder(parent, child);
        }
    }
}
