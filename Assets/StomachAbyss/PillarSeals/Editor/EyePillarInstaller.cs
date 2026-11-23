using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace StomachAbyss.PillarSeals.Editor
{
    public static class EyePillarInstaller
    {
        private const string MeshFolder = "Assets/StomachAbyss/PillarSeals/Meshes";
        private const string MatFolder = "Assets/StomachAbyss/PillarSeals/Materials";

        [MenuItem("StomachAbyss/Install 3 Eye Pillars & Seal Gate")]
        public static void InstallAll()
        {
            EnsureFolders();

            // 1. Load or create materials
            Material eyeballMat = LoadEyeballMaterial();
            Material monolithMat = LoadOrCreateMaterial(
                MatFolder + "/Mat_EyePillar_Monolith.mat",
                "Assets/PilotChamber/Materials/Mat_Vertebra_ScorchedBone.mat",
                new Color(0.11f, 0.16f, 0.15f, 1f),
                0.22f
            );
            Material chainMat = LoadOrCreateMaterial(
                MatFolder + "/Mat_EyePillar_Chain.mat",
                "Assets/PilotChamber/Materials/Mat_Infernal_IronBone.mat",
                new Color(0.28f, 0.24f, 0.20f, 1f),
                0.55f
            );
            Material rockMat = LoadOrCreateMaterial(
                MatFolder + "/Mat_EyePillar_AnchorRock.mat",
                "Assets/StomachAbyss/Materials/Mat_Abyss_OssifiedStone.mat",
                new Color(0.25f, 0.24f, 0.21f, 1f),
                0.10f
            );
            Material socketMat = LoadOrCreateMaterial(
                MatFolder + "/Mat_EyePillar_SocketFlesh.mat",
                "Assets/StomachAbyss/Materials/Mat_Abyss_VeinTendon.mat",
                new Color(0.32f, 0.08f, 0.08f, 1f),
                0.35f
            );

            // 2. Build & persist meshes
            Mesh monolithMesh = GetOrCreateMesh(MeshFolder + "/Mesh_EyePillar_Monolith.asset", BuildJaggedMonolithMesh);
            Mesh eyeballMesh = GetOrCreateMesh(MeshFolder + "/Mesh_EyePillar_Eyeball.asset", BuildFrontUvEyeballMesh);
            Mesh socketMesh = GetOrCreateMesh(MeshFolder + "/Mesh_EyePillar_SocketRing.asset", BuildTorusSocketMesh);
            Mesh collarMesh = GetOrCreateMesh(MeshFolder + "/Mesh_EyePillar_CollarRing.asset", BuildChainCollarMesh);
            Mesh chainSegmentMesh = GetOrCreateMesh(MeshFolder + "/Mesh_EyePillar_ChainSpan.asset", BuildChainSpanMesh);
            Mesh rockMesh = GetOrCreateMesh(MeshFolder + "/Mesh_EyePillar_AnchorRock.asset", BuildAnchorRockMesh);

            // 3. Root container for the 3 pillars
            GameObject pillarsRoot = GameObject.Find("09_EyePillar_Seals");
            if (pillarsRoot != null)
            {
                Object.DestroyImmediate(pillarsRoot);
            }
            pillarsRoot = new GameObject("09_EyePillar_Seals");

            // Grounded positions on the 3 islands (West, East, South)
            var placements = new (string name, string label, Vector3 approxPos, float yaw, float phase)[]
            {
                ("EyePillar_West",  "Pilar Ocular Oeste", new Vector3(-35.50f, 4.00f,  1.50f),  35f, 0.0f),
                ("EyePillar_East",  "Pilar Ocular Leste", new Vector3( 30.50f, 5.40f, 14.40f), -45f, 2.1f),
                ("EyePillar_South", "Pilar Ocular Sul",   new Vector3(  0.00f, 3.20f,-15.60f), 180f, 4.2f)
            };

            List<EyePillarController> createdPillars = new List<EyePillarController>();
            for (int i = 0; i < placements.Length; i++)
            {
                var p = placements[i];
                Vector3 groundedPos = RaycastGround(p.approxPos);
                EyePillarController ctrl = CreateSinglePillar(
                    pillarsRoot.transform,
                    p.name,
                    p.label,
                    groundedPos,
                    p.yaw,
                    p.phase,
                    monolithMesh,
                    eyeballMesh,
                    socketMesh,
                    collarMesh,
                    chainSegmentMesh,
                    rockMesh,
                    monolithMat,
                    eyeballMat,
                    socketMat,
                    chainMat,
                    rockMat
                );
                createdPillars.Add(ctrl);
            }

            // 4. Configure GateRig (1) as the sealed gate unlocked by the 3 pillars
            SetupGateRig1(createdPillars, eyeballMesh, eyeballMat);

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();
            Debug.Log("[EyePillarInstaller] 3 Eye Pillars & GateRig (1) seal installed and saved successfully!");
        }

        private static EyePillarController CreateSinglePillar(
            Transform parent,
            string goName,
            string label,
            Vector3 worldPos,
            float yawDeg,
            float phaseOffset,
            Mesh monolithMesh,
            Mesh eyeballMesh,
            Mesh socketMesh,
            Mesh collarMesh,
            Mesh chainSpanMesh,
            Mesh rockMesh,
            Material monolithMat,
            Material eyeballMat,
            Material socketMat,
            Material chainMat,
            Material rockMat)
        {
            GameObject root = new GameObject(goName);
            root.transform.SetParent(parent, false);
            root.transform.position = worldPos;
            root.transform.rotation = Quaternion.Euler(0f, yawDeg, 0f);

            // Physical collider so player & sword swings hit the monolith cleanly
            CapsuleCollider col = root.AddComponent<CapsuleCollider>();
            col.center = new Vector3(0f, 2.35f, 0f);
            col.radius = 1.05f;
            col.height = 4.6f;

            EyePillarController ctrl = root.AddComponent<EyePillarController>();
            ctrl.pillarLabel = label;
            ctrl.maxHealth = 200f;
            ctrl.currentHealth = 200f;
            ctrl.phaseOffset = phaseOffset;
            ctrl.hitCollider = col;

            // Hovering Monolith Root
            GameObject monolith = new GameObject("Monolith_Core");
            monolith.transform.SetParent(root.transform, false);
            monolith.transform.localPosition = new Vector3(0f, 2.55f, 0f);
            ctrl.monolithRoot = monolith.transform;

            MeshFilter mf = monolith.AddComponent<MeshFilter>();
            mf.sharedMesh = monolithMesh;
            MeshRenderer mr = monolith.AddComponent<MeshRenderer>();
            mr.sharedMaterial = monolithMat;

            // Upper & lower iron binding collars wrapped around the monolith
            CreateStaticPart(monolith.transform, "Chain_Collar_Upper", collarMesh, chainMat, new Vector3(0f, 1.05f, 0f), Vector3.zero, new Vector3(1.02f, 1f, 1.02f));
            CreateStaticPart(monolith.transform, "Chain_Collar_Lower", collarMesh, chainMat, new Vector3(0f, -0.78f, 0f), new Vector3(0f, 35f, 0f), new Vector3(1.06f, 1f, 1.06f));

            // 6 Embedded Eyeballs around the monolith at varied heights/angles
            var eyeSpecs = new (Vector3 localPos, float yaw, float scale)[]
            {
                (new Vector3( 0.00f,  0.95f,  0.52f),   0f, 0.36f),
                (new Vector3( 0.50f,  0.35f,  0.25f),  62f, 0.40f),
                (new Vector3(-0.50f,  0.48f,  0.22f), -65f, 0.38f),
                (new Vector3( 0.42f, -0.35f, -0.38f), 132f, 0.37f),
                (new Vector3(-0.44f, -0.22f, -0.36f),-130f, 0.36f),
                (new Vector3( 0.00f,  0.05f, -0.56f), 180f, 0.42f)
            };

            for (int i = 0; i < eyeSpecs.Length; i++)
            {
                var es = eyeSpecs[i];
                // Fleshy socket ring facing outward
                GameObject socket = new GameObject($"EyeSocket_{i}");
                socket.transform.SetParent(monolith.transform, false);
                socket.transform.localPosition = es.localPos;
                socket.transform.localRotation = Quaternion.Euler(0f, es.yaw, 0f);
                socket.transform.localScale = Vector3.one * (es.scale * 1.18f);
                socket.AddComponent<MeshFilter>().sharedMesh = socketMesh;
                socket.AddComponent<MeshRenderer>().sharedMaterial = socketMat;

                // Eyeball inside socket (rotates freely toward player)
                GameObject eye = new GameObject($"Tracking_Eyeball_{i}");
                eye.transform.SetParent(monolith.transform, false);
                eye.transform.localPosition = es.localPos + Quaternion.Euler(0f, es.yaw, 0f) * new Vector3(0f, 0f, 0.04f);
                eye.transform.localRotation = Quaternion.Euler(0f, es.yaw, 0f);
                eye.transform.localScale = Vector3.one * es.scale;
                eye.AddComponent<MeshFilter>().sharedMesh = eyeballMesh;
                eye.AddComponent<MeshRenderer>().sharedMaterial = eyeballMat;
                ctrl.eyeballs.Add(eye.transform);
            }

            // 3 Heavy Chains + 3 Ground Anchor Rocks (120 degrees apart)
            float[] anchorAngles = { 25f, 145f, 265f };
            float anchorRadius = 2.45f;

            for (int i = 0; i < 3; i++)
            {
                float rad = anchorAngles[i] * Mathf.Deg2Rad;
                Vector3 anchorLocal = new Vector3(Mathf.Sin(rad) * anchorRadius, 0.05f, Mathf.Cos(rad) * anchorRadius);

                // Ground Rock Plinth
                GameObject rock = new GameObject($"Anchor_Rock_{i}");
                rock.transform.SetParent(root.transform, false);
                rock.transform.localPosition = anchorLocal;
                rock.transform.localRotation = Quaternion.Euler(0f, anchorAngles[i] + 40f, 0f);
                rock.transform.localScale = new Vector3(0.95f, 0.78f, 0.95f);
                rock.AddComponent<MeshFilter>().sharedMesh = rockMesh;
                rock.AddComponent<MeshRenderer>().sharedMaterial = rockMat;

                // Chain attaching monolith collar to anchor rock
                Vector3 topLocal = new Vector3(Mathf.Sin(rad) * 0.56f, (i == 0 ? 3.55f : 1.82f), Mathf.Cos(rad) * 0.56f);
                Vector3 botLocal = anchorLocal + new Vector3(0f, 0.25f, 0f);
                Vector3 midLocal = (topLocal + botLocal) * 0.5f;
                Vector3 span = topLocal - botLocal;
                float len = span.magnitude;

                GameObject chain = new GameObject($"Anchor_Chain_{i}");
                chain.transform.SetParent(root.transform, false);
                chain.transform.localPosition = midLocal;
                chain.transform.localRotation = Quaternion.FromToRotation(Vector3.up, span.normalized);
                chain.transform.localScale = new Vector3(1f, len, 1f);
                chain.AddComponent<MeshFilter>().sharedMesh = chainSpanMesh;
                chain.AddComponent<MeshRenderer>().sharedMaterial = chainMat;

                ctrl.chains.Add(chain.transform);
            }

            // Eerie eerie glow light
            GameObject lightGo = new GameObject("Pillar_AuraLight");
            lightGo.transform.SetParent(monolith.transform, false);
            lightGo.transform.localPosition = new Vector3(0f, 0.2f, 0f);
            Light l = lightGo.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = new Color(0.85f, 0.28f, 0.16f, 1f);
            l.intensity = 3.2f;
            l.range = 8.5f;
            ctrl.auraLight = l;

            return ctrl;
        }

        private static void SetupGateRig1(List<EyePillarController> pillars, Mesh eyeballMesh, Material eyeballMat)
        {
            GameObject gateRig1 = GameObject.Find("GateRig (1)");
            if (gateRig1 == null)
            {
                Debug.LogWarning("[EyePillarInstaller] GateRig (1) não encontrado na cena!");
                return;
            }

            // Unparent GateRig (1) from Cathedral_Dramatic_BoneGate so it operates independently in the main map
            if (gateRig1.transform.parent != null && gateRig1.transform.parent.name == "Cathedral_Dramatic_BoneGate")
            {
                gateRig1.transform.SetParent(null, true);
            }

            // Add or reuse blocking BoxCollider
            BoxCollider box = gateRig1.GetComponent<BoxCollider>();
            if (box == null) box = gateRig1.AddComponent<BoxCollider>();
            box.isTrigger = false;
            box.center = new Vector3(0f, 3.40f, 0f);
            box.size = new Vector3(8.20f, 6.80f, 1.50f);

            AudioSource audio = gateRig1.GetComponent<AudioSource>();
            if (audio == null) audio = gateRig1.AddComponent<AudioSource>();
            audio.spatialBlend = 0.35f;
            audio.playOnAwake = false;

            EyePillarGateSealController sealCtrl = gateRig1.GetComponent<EyePillarGateSealController>();
            if (sealCtrl == null) sealCtrl = gateRig1.AddComponent<EyePillarGateSealController>();

            sealCtrl.pillars.Clear();
            sealCtrl.pillars.AddRange(pillars);
            sealCtrl.blockingCollider = box;
            sealCtrl.audioSource = audio;
            sealCtrl.leftWingPivot = gateRig1.transform.Find("LeftWingPivot");
            sealCtrl.rightWingPivot = gateRig1.transform.Find("RightWingPivot");
            sealCtrl.upperPortcullis = gateRig1.transform.Find("UpperPortcullis");
            Transform fx = gateRig1.transform.Find("GateSlam_BloodDust_FX");
            if (fx != null) sealCtrl.gateDustParticles = fx.GetComponent<ParticleSystem>();

            // Remove old seal orbs if reinstalling
            Transform oldOrbs = gateRig1.transform.Find("Gate_Seal_Orbs");
            if (oldOrbs != null) Object.DestroyImmediate(oldOrbs.gameObject);

            GameObject orbsParent = new GameObject("Gate_Seal_Orbs");
            orbsParent.transform.SetParent(gateRig1.transform, false);
            orbsParent.transform.localPosition = Vector3.zero;

            sealCtrl.sealEyeOrbs.Clear();
            sealCtrl.sealOrbLights.Clear();

            float[] orbX = { -2.2f, 0.0f, 2.2f };
            float[] orbY = { 3.3f, 4.1f, 3.3f };

            for (int i = 0; i < 3; i++)
            {
                GameObject orb = new GameObject($"Seal_EyeOrb_{i}");
                orb.transform.SetParent(orbsParent.transform, false);
                orb.transform.localPosition = new Vector3(orbX[i], orbY[i], 0.55f);
                orb.transform.localScale = Vector3.one * 0.58f;
                orb.AddComponent<MeshFilter>().sharedMesh = eyeballMesh;
                orb.AddComponent<MeshRenderer>().sharedMaterial = eyeballMat;

                Light ol = orb.AddComponent<Light>();
                ol.type = LightType.Point;
                ol.color = new Color(1.0f, 0.25f, 0.14f, 1f);
                ol.intensity = 2.8f;
                ol.range = 5.5f;

                sealCtrl.sealEyeOrbs.Add(orb.transform);
                sealCtrl.sealOrbLights.Add(ol);
            }

            EditorUtility.SetDirty(gateRig1);
        }

        private static Vector3 RaycastGround(Vector3 approxPos)
        {
            if (Physics.Raycast(new Vector3(approxPos.x, approxPos.y + 8f, approxPos.z), Vector3.down, out RaycastHit hit, 24f))
            {
                if (!hit.collider.isTrigger)
                {
                    return hit.point;
                }
            }
            return approxPos;
        }

        private static void CreateStaticPart(Transform parent, string name, Mesh mesh, Material mat, Vector3 localPos, Vector3 localEuler, Vector3 localScale)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.Euler(localEuler);
            go.transform.localScale = localScale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        }

        private static Material LoadEyeballMaterial()
        {
            Material m = AssetDatabase.LoadAssetAtPath<Material>("Assets/PilotChamber/Materials/Mat_Eyeball.mat");
            if (m != null) return m;

            Shader sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh == null) sh = Shader.Find("Standard");
            m = new Material(sh);
            Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/PilotChamber/Textures/tex_eyeball.png");
            if (tex != null) m.mainTexture = tex;
            AssetDatabase.CreateAsset(m, MatFolder + "/Mat_EyePillar_EyeballFallback.mat");
            return m;
        }

        private static Material LoadOrCreateMaterial(string targetPath, string preferredSourcePath, Color fallbackColor, float smoothness)
        {
            Material src = AssetDatabase.LoadAssetAtPath<Material>(preferredSourcePath);
            if (src != null) return src;

            Material existing = AssetDatabase.LoadAssetAtPath<Material>(targetPath);
            if (existing != null) return existing;

            Shader sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh == null) sh = Shader.Find("Standard");
            Material m = new Material(sh);
            m.color = fallbackColor;
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
            AssetDatabase.CreateAsset(m, targetPath);
            return m;
        }

        private static Mesh GetOrCreateMesh(string assetPath, System.Func<Mesh> builder)
        {
            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);
            if (existing != null) return existing;
            Mesh m = builder();
            AssetDatabase.CreateAsset(m, assetPath);
            return m;
        }

        private static void EnsureFolders()
        {
            if (!AssetDatabase.IsValidFolder("Assets/StomachAbyss/PillarSeals"))
                AssetDatabase.CreateFolder("Assets/StomachAbyss", "PillarSeals");
            if (!AssetDatabase.IsValidFolder(MeshFolder))
                AssetDatabase.CreateFolder("Assets/StomachAbyss/PillarSeals", "Meshes");
            if (!AssetDatabase.IsValidFolder(MatFolder))
                AssetDatabase.CreateFolder("Assets/StomachAbyss/PillarSeals", "Materials");
        }

        // ---------------- PROCEDURAL MESH BUILDERS ----------------

        private static Mesh BuildJaggedMonolithMesh()
        {
            Mesh m = new Mesh { name = "EyePillar_Monolith" };
            int rings = 9;
            int sides = 10;
            float[] heights = { -1.65f, -1.25f, -0.75f, -0.20f, 0.35f, 0.90f, 1.35f, 1.72f, 2.05f };
            float[] radii   = {  0.22f,  0.56f,  0.64f,  0.58f, 0.62f, 0.54f, 0.44f, 0.25f, 0.04f };

            List<Vector3> verts = new List<Vector3>();
            List<Vector2> uvs = new List<Vector2>();
            List<int> tris = new List<int>();

            for (int r = 0; r < rings; r++)
            {
                for (int s = 0; s < sides; s++)
                {
                    float angle = (s / (float)sides) * Mathf.PI * 2f;
                    float jitter = 1f + 0.14f * Mathf.Sin(r * 2.3f + s * 1.9f);
                    float rad = radii[r] * jitter;
                    float x = Mathf.Sin(angle) * rad;
                    float z = Mathf.Cos(angle) * rad;
                    verts.Add(new Vector3(x, heights[r], z));
                    uvs.Add(new Vector2(s / (float)sides * 2f, r / (float)(rings - 1) * 3f));
                }
            }

            for (int r = 0; r < rings - 1; r++)
            {
                for (int s = 0; s < sides; s++)
                {
                    int next = (s + 1) % sides;
                    int a = r * sides + s;
                    int b = r * sides + next;
                    int c = (r + 1) * sides + s;
                    int d = (r + 1) * sides + next;
                    tris.Add(a); tris.Add(c); tris.Add(b);
                    tris.Add(b); tris.Add(c); tris.Add(d);
                }
            }

            m.SetVertices(verts);
            m.SetUVs(0, uvs);
            m.SetTriangles(tris, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        private static Mesh BuildFrontUvEyeballMesh()
        {
            Mesh m = new Mesh { name = "EyePillar_Eyeball" };
            int latSegs = 12;
            int lonSegs = 18;
            float radius = 0.5f;

            List<Vector3> verts = new List<Vector3>();
            List<Vector2> uvs = new List<Vector2>();
            List<int> tris = new List<int>();

            for (int lat = 0; lat <= latSegs; lat++)
            {
                float theta = (lat / (float)latSegs) * Mathf.PI;
                float sinT = Mathf.Sin(theta);
                float cosT = Mathf.Cos(theta);

                for (int lon = 0; lon <= lonSegs; lon++)
                {
                    float phi = (lon / (float)lonSegs) * Mathf.PI * 2f;
                    float x = sinT * Mathf.Sin(phi);
                    float y = cosT;
                    float z = sinT * Mathf.Cos(phi);

                    verts.Add(new Vector3(x * radius, y * radius, z * radius));
                    // Planar-spherical front projection along +Z so iris sits dead-center at (0.5, 0.5)
                    float u = 0.5f + x * 0.45f;
                    float v = 0.5f + y * 0.45f;
                    if (z < 0f)
                    {
                        u = Mathf.Lerp(u, 0.08f, Mathf.Clamp01(-z));
                        v = Mathf.Lerp(v, 0.08f, Mathf.Clamp01(-z));
                    }
                    uvs.Add(new Vector2(u, v));
                }
            }

            int row = lonSegs + 1;
            for (int lat = 0; lat < latSegs; lat++)
            {
                for (int lon = 0; lon < lonSegs; lon++)
                {
                    int a = lat * row + lon;
                    int b = a + 1;
                    int c = a + row;
                    int d = c + 1;
                    tris.Add(a); tris.Add(b); tris.Add(c);
                    tris.Add(b); tris.Add(d); tris.Add(c);
                }
            }

            m.SetVertices(verts);
            m.SetUVs(0, uvs);
            m.SetTriangles(tris, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        private static Mesh BuildTorusSocketMesh()
        {
            return BuildTorusMesh("EyePillar_Socket", 0.50f, 0.16f, 14, 8, Vector3.forward);
        }

        private static Mesh BuildChainCollarMesh()
        {
            return BuildTorusMesh("EyePillar_Collar", 0.64f, 0.095f, 16, 6, Vector3.up);
        }

        private static Mesh BuildTorusMesh(string name, float majorR, float minorR, int majorSegs, int minorSegs, Vector3 normalAxis)
        {
            Mesh m = new Mesh { name = name };
            List<Vector3> verts = new List<Vector3>();
            List<Vector2> uvs = new List<Vector2>();
            List<int> tris = new List<int>();

            Quaternion align = Quaternion.FromToRotation(Vector3.up, normalAxis);

            for (int i = 0; i <= majorSegs; i++)
            {
                float u = (i / (float)majorSegs) * Mathf.PI * 2f;
                Vector3 ringCenter = new Vector3(Mathf.Cos(u) * majorR, 0f, Mathf.Sin(u) * majorR);
                Vector3 radial = new Vector3(Mathf.Cos(u), 0f, Mathf.Sin(u));

                for (int j = 0; j <= minorSegs; j++)
                {
                    float v = (j / (float)minorSegs) * Mathf.PI * 2f;
                    Vector3 pt = ringCenter + radial * (Mathf.Cos(v) * minorR) + Vector3.up * (Mathf.Sin(v) * minorR);
                    verts.Add(align * pt);
                    uvs.Add(new Vector2(i / (float)majorSegs * 3f, j / (float)minorSegs));
                }
            }

            int stride = minorSegs + 1;
            for (int i = 0; i < majorSegs; i++)
            {
                for (int j = 0; j < minorSegs; j++)
                {
                    int a = i * stride + j;
                    int b = a + stride;
                    int c = a + 1;
                    int d = b + 1;
                    tris.Add(a); tris.Add(b); tris.Add(c);
                    tris.Add(b); tris.Add(d); tris.Add(c);
                }
            }

            m.SetVertices(verts);
            m.SetUVs(0, uvs);
            m.SetTriangles(tris, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        private static Mesh BuildChainSpanMesh()
        {
            Mesh m = new Mesh { name = "EyePillar_ChainSpan" };
            int links = 10;
            List<CombineInstance> combines = new List<CombineInstance>();
            Mesh singleLink = BuildTorusMesh("TempLink", 0.12f, 0.038f, 10, 5, Vector3.forward);

            for (int i = 0; i < links; i++)
            {
                float y = -0.45f + (i / (float)(links - 1)) * 0.90f;
                float yaw = (i % 2 == 0) ? 0f : 90f;
                Matrix4x4 mat = Matrix4x4.TRS(new Vector3(0f, y, 0f), Quaternion.Euler(0f, yaw, 0f), new Vector3(0.75f, 1.15f, 0.75f));
                combines.Add(new CombineInstance { mesh = singleLink, transform = mat });
            }

            m.CombineMeshes(combines.ToArray(), true, true);
            Object.DestroyImmediate(singleLink);
            m.RecalculateBounds();
            return m;
        }

        private static Mesh BuildAnchorRockMesh()
        {
            Mesh m = new Mesh { name = "EyePillar_AnchorRock" };
            Vector3[] verts =
            {
                new Vector3(-0.75f, 0f, -0.65f),
                new Vector3( 0.78f, 0f, -0.60f),
                new Vector3( 0.65f, 0f,  0.75f),
                new Vector3(-0.70f, 0f,  0.72f),
                new Vector3(-0.20f, 0.95f, -0.15f),
                new Vector3( 0.25f, 0.82f,  0.18f)
            };
            int[] tris =
            {
                0, 4, 1,  1, 4, 5,  1, 5, 2,  2, 5, 3,  3, 5, 4,  3, 4, 0,
                0, 1, 2,  0, 2, 3
            };
            Vector2[] uvs =
            {
                new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1),
                new Vector2(0, 1), new Vector2(0.4f, 0.6f), new Vector2(0.7f, 0.7f)
            };

            m.vertices = verts;
            m.triangles = tris;
            m.uv = uvs;
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }
    }
}
