using System.Collections.Generic;
using UnityEngine;

namespace MapaEstomago
{
    /// <summary>
    /// Procedural 3D visual builder for the upgraded HELLFLESH FleshEyeDoor (Variation B):
    /// - Grotesque veined crimson flesh facade with sculpted eye cavity & heart-pupil eyeball
    /// - Swollen upper gum + 5 large cracked human-like upper teeth
    /// - Articulated lower jaw with lower teeth (closes & chews during bite!)
    /// - Retractable wet fleshy tongue rolling onto the floor (retracts while chewing, extends on spit!)
    /// - Massive curved segmented esophagus tunnel behind the door braced by curved bone ribs & tendons
    ///   plunging downward into the glowing green gastric acid lake.
    /// Uses the 5 new PS1 textures in Resources/Textures/FleshDoor/.
    /// </summary>
    public static class FleshEyeDoorVisualBuilder
    {
        public struct BuiltDoorRig
        {
            public Transform visualRoot;
            public Transform eyeball;
            public Transform eyelidUpper;
            public Transform eyelidLower;
            public Transform upperGum;
            public Transform upperTeeth;
            public Transform lowerJaw;
            public Transform tongue;
            public Transform tunnelRoot;
        }

        private static Material s_fleshMat;
        private static Material s_darkThroatMat;
        private static Material s_eyeMat;
        private static Material s_teethMat;
        private static Material s_tongueMat;
        private static Material s_ribBoneMat;
        private static Material s_tendonMat;

        private static Mesh s_eyeMesh;
        private static Mesh s_eyelidMesh;
        private static Mesh s_facadeArchMesh;
        private static Mesh s_gumMesh;
        private static Mesh s_toothMesh;
        private static Mesh s_lowerJawMesh;
        private static Mesh s_tongueMesh;
        private static Mesh s_esophagusTunnelMesh;
        private static Mesh s_ribArchMesh;
        private static Mesh s_tendonMesh;

        public static BuiltDoorRig BuildOrReplaceVisuals(Transform doorRoot, float visualScale = 1.65f)
        {
            BuiltDoorRig rig = new BuiltDoorRig();
            if (doorRoot == null) return rig;

            EnsureMaterials();
            EnsureMeshes();

            // 1. Hide old blocky legacy renderers while keeping colliders & DoorTransitionTrigger untouched
            Transform existing = doorRoot.Find("FleshEyeDoor_V2_Visuals");
            if (existing != null)
            {
                existing.name = "FleshEyeDoor_V2_Visuals_OLD";
                existing.SetParent(null, false);
                if (Application.isPlaying) Object.Destroy(existing.gameObject);
                else Object.DestroyImmediate(existing.gameObject);
            }

            Renderer[] oldRenderers = doorRoot.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < oldRenderers.Length; i++)
            {
                Renderer r = oldRenderers[i];
                if (r == null) continue;
                if (r is ParticleSystemRenderer) continue;
                r.enabled = false;
            }

            // 2. Build clean V2 visual root
            GameObject rootGo = new GameObject("FleshEyeDoor_V2_Visuals");
            rootGo.transform.SetParent(doorRoot, false);
            rootGo.transform.localPosition = Vector3.zero;
            rootGo.transform.localRotation = Quaternion.identity;
            float clampedScale = visualScale <= 0.05f ? 1.65f : Mathf.Clamp(visualScale, 0.25f, 4.0f);
            rootGo.transform.localScale = Vector3.one * clampedScale;
            rig.visualRoot = rootGo.transform;

            // --- A. Sculpted Crimson Flesh Facade & Dark Throat ---
            CreateMeshPart("Door_Flesh_Facade", rig.visualRoot, s_facadeArchMesh, s_fleshMat, Vector3.zero, Quaternion.identity, Vector3.one);

            // Deep dark fleshy throat backing inside the mouth opening
            GameObject throat = GameObject.CreatePrimitive(PrimitiveType.Cube);
            throat.name = "Door_Dark_Throat";
            throat.transform.SetParent(rig.visualRoot, false);
            StripCollider(throat);
            throat.transform.localPosition = new Vector3(0f, 1.05f, -0.45f);
            throat.transform.localScale = new Vector3(1.85f, 1.95f, 0.35f);
            Renderer tr = throat.GetComponent<Renderer>();
            if (tr != null) tr.sharedMaterial = s_darkThroatMat;

            // --- B. Eye Socket, Heart-Pupil Eyeball & Fleshy Eyelids ---
            Transform eyeSocket = new GameObject("Door_Eye_Socket").transform;
            eyeSocket.SetParent(rig.visualRoot, false);
            eyeSocket.localPosition = new Vector3(0f, 2.38f, 0.24f);

            rig.eyeball = CreateMeshPart("Door_Eyeball_V2", eyeSocket, s_eyeMesh, s_eyeMat, Vector3.zero, Quaternion.identity, Vector3.one * 0.56f);
            rig.eyelidUpper = CreateMeshPart("Door_Eyelid_Upper_V2", eyeSocket, s_eyelidMesh, s_fleshMat, new Vector3(0f, 0.04f, 0.02f), Quaternion.Euler(-22f, 0f, 0f), Vector3.one * 0.63f);
            rig.eyelidLower = CreateMeshPart("Door_Eyelid_Lower_V2", eyeSocket, s_eyelidMesh, s_fleshMat, new Vector3(0f, -0.04f, 0.02f), Quaternion.Euler(202f, 0f, 0f), Vector3.one * 0.63f);

            // --- C. Swollen Upper Gum & 5 Large Cracked Human Teeth ---
            rig.upperGum = CreateMeshPart("Door_Upper_Gum_V2", rig.visualRoot, s_gumMesh, s_fleshMat, new Vector3(0f, 1.62f, 0.36f), Quaternion.identity, Vector3.one);

            GameObject upperTeethRoot = new GameObject("Door_Upper_Teeth_V2");
            upperTeethRoot.transform.SetParent(rig.visualRoot, false);
            upperTeethRoot.transform.localPosition = new Vector3(0f, 1.48f, 0.44f);
            rig.upperTeeth = upperTeethRoot.transform;

            // 5 grotesque oversized weathered human teeth curving along the upper gum
            float[] tx = { -0.66f, -0.34f, 0.0f, 0.34f, 0.66f };
            float[] tz = { -0.10f, 0.04f, 0.09f, 0.04f, -0.10f };
            float[] tyScale = { 0.44f, 0.50f, 0.55f, 0.49f, 0.43f };
            float[] txScale = { 0.27f, 0.29f, 0.31f, 0.29f, 0.27f };
            float[] tTilt = { -10f, -4f, 1f, 5f, 11f };
            for (int i = 0; i < 5; i++)
            {
                CreateMeshPart(
                    $"Upper_Tooth_{i}",
                    rig.upperTeeth,
                    s_toothMesh,
                    s_teethMat,
                    new Vector3(tx[i], -0.12f, tz[i]),
                    Quaternion.Euler(8f, -tTilt[i] * 1.4f, tTilt[i]),
                    new Vector3(txScale[i], tyScale[i], 0.20f)
                );
            }

            // --- D. Articulated Lower Jaw with Lower Teeth (closes upward on bite/chew!) ---
            rig.lowerJaw = CreateMeshPart("Door_Lower_Jaw_V2", rig.visualRoot, s_lowerJawMesh, s_fleshMat, new Vector3(0f, 0.16f, 0.30f), Quaternion.identity, Vector3.one);
            for (int i = 0; i < 5; i++)
            {
                CreateMeshPart(
                    $"Lower_Tooth_{i}",
                    rig.lowerJaw,
                    s_toothMesh,
                    s_teethMat,
                    new Vector3(tx[i] * 0.90f, 0.22f, tz[i] * 0.85f + 0.12f),
                    Quaternion.Euler(172f, tTilt[i] * 1.2f, -tTilt[i]),
                    new Vector3(txScale[i] * 0.82f, tyScale[i] * 0.62f, 0.17f)
                );
            }

            // --- E. Retractable Wet Fleshy Tongue (extends onto floor; retracts inside during chew!) ---
            rig.tongue = CreateMeshPart(
                "Door_Tongue_V2",
                rig.visualRoot,
                s_tongueMesh,
                s_tongueMat,
                new Vector3(0f, 0.12f, 0.42f),
                Quaternion.identity,
                Vector3.one
            );

            // --- F. Rib-Caged Curving Esophagus Tunnel Plunging into the Acid Lake Behind (-Z, -Y) ---
            GameObject tunnelRootGo = new GameObject("Door_Esophagus_Tunnel_V2");
            tunnelRootGo.transform.SetParent(rig.visualRoot, false);
            tunnelRootGo.transform.localPosition = Vector3.zero;
            rig.tunnelRoot = tunnelRootGo.transform;

            CreateMeshPart("Esophagus_Tube_Mesh", rig.tunnelRoot, s_esophagusTunnelMesh, s_fleshMat, Vector3.zero, Quaternion.identity, Vector3.one);

            // 6 curved bone rib arches hugging the descending esophagus tunnel
            const int ribCount = 6;
            for (int i = 0; i < ribCount; i++)
            {
                float t = Mathf.Lerp(0.10f, 0.82f, i / (float)(ribCount - 1));
                Vector3 pos = EvalTunnelCenter(t);
                Vector3 tan = EvalTunnelTangent(t);
                Quaternion rot = Quaternion.LookRotation(tan, Vector3.up);
                float ribScale = Mathf.Lerp(1.12f, 0.96f, t);
                CreateMeshPart($"Tunnel_RibArch_{i}", rig.tunnelRoot, s_ribArchMesh, s_ribBoneMat, pos, rot, Vector3.one * ribScale);
            }

            // Anchoring crimson tendons gripping the cliff/island on left & right sides
            Vector3[] tendonStarts = {
                new Vector3(-1.18f, 2.25f, 0.10f),
                new Vector3( 1.18f, 2.25f, 0.10f),
                new Vector3(-1.15f, 1.50f, -0.95f),
                new Vector3( 1.15f, 1.50f, -0.95f),
                new Vector3(-1.05f, 0.70f, -2.20f),
                new Vector3( 1.05f, 0.70f, -2.20f),
            };
            Vector3[] tendonEnds = {
                new Vector3(-1.75f, 0.02f, 0.45f),
                new Vector3( 1.75f, 0.02f, 0.45f),
                new Vector3(-1.85f, -0.25f, -0.60f),
                new Vector3( 1.85f, -0.25f, -0.60f),
                new Vector3(-1.60f, -0.95f, -1.85f),
                new Vector3( 1.60f, -0.95f, -1.85f),
            };
            for (int i = 0; i < tendonStarts.Length; i++)
            {
                Vector3 a = tendonStarts[i];
                Vector3 b = tendonEnds[i];
                Vector3 mid = (a + b) * 0.5f;
                Vector3 dir = b - a;
                float len = dir.magnitude;
                Quaternion rot = Quaternion.LookRotation(dir.normalized, Vector3.up) * Quaternion.Euler(90f, 0f, 0f);
                CreateMeshPart($"Anchor_Tendon_{i}", rig.tunnelRoot, s_tendonMesh, s_tendonMat, mid, rot, new Vector3(0.11f, len * 0.5f, 0.11f));
            }

            // Subtle emerald acid underglow where the tunnel plunges beneath the acid
            GameObject glowGo = new GameObject("Tunnel_AcidGlow");
            glowGo.transform.SetParent(rig.tunnelRoot, false);
            glowGo.transform.localPosition = new Vector3(0f, -0.35f, -3.6f);
            Light l = glowGo.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = new Color(0.28f, 0.96f, 0.22f);
            l.intensity = 1.6f;
            l.range = 6.5f;
            l.shadows = LightShadows.None;

            // Apply DontSave flags recursively to the entire V2 hierarchy in Edit Mode
            // so NO child objects ever leak into .unity scene files when saving!
            if (!Application.isPlaying)
            {
                SetHideFlagsRecursive(rootGo, HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild);
            }

            return rig;
        }

        private static void SetHideFlagsRecursive(GameObject go, HideFlags flags)
        {
            if (go == null) return;
            go.hideFlags = flags;
            Transform t = go.transform;
            for (int i = 0; i < t.childCount; i++)
            {
                SetHideFlagsRecursive(t.GetChild(i).gameObject, flags);
            }
        }

        private static Transform CreateMeshPart(string name, Transform parent, Mesh mesh, Material mat, Vector3 localPos, Quaternion localRot, Vector3 localScale)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = localRot;
            go.transform.localScale = localScale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.allowOcclusionWhenDynamic = false;
            return go.transform;
        }

        private static void StripCollider(GameObject go)
        {
            Collider c = go.GetComponent<Collider>();
            if (c != null)
            {
                if (Application.isPlaying) Object.Destroy(c);
                else Object.DestroyImmediate(c);
            }
        }

        private static Vector3 EvalTunnelCenter(float t)
        {
            // Smooth cubic Bezier curving out the back (-Z) and plunging down (-Y) beneath the acid lake
            Vector3 p0 = new Vector3(0f, 1.38f, -0.35f);
            Vector3 p1 = new Vector3(0f, 1.55f, -2.10f);
            Vector3 p2 = new Vector3(0f, 0.25f, -3.75f);
            Vector3 p3 = new Vector3(0f, -2.65f, -4.95f);
            float u = 1f - t;
            return u * u * u * p0 + 3f * u * u * t * p1 + 3f * u * t * t * p2 + t * t * t * p3;
        }

        private static Vector3 EvalTunnelTangent(float t)
        {
            float eps = 0.02f;
            Vector3 a = EvalTunnelCenter(Mathf.Clamp01(t - eps));
            Vector3 b = EvalTunnelCenter(Mathf.Clamp01(t + eps));
            return (b - a).normalized;
        }

        // =====================================================================
        // MATERIALS (Using our 5 new PS1 Textures from Resources/Textures/FleshDoor)
        // =====================================================================
        private static void EnsureMaterials()
        {
            if (s_fleshMat != null && s_eyeMat != null && s_teethMat != null && s_tongueMat != null && s_ribBoneMat != null) return;

            Shader fleshShader = Shader.Find("MapaEstomago/URP/PS1_LivingFlesh")
                ?? Shader.Find("Universal Render Pipeline/Lit")
                ?? Shader.Find("Standard");

            s_fleshMat = MakeMat(fleshShader, "Textures/FleshDoor/tex_fleshdoor_veined_flesh", new Color(0.88f, 0.72f, 0.72f, 1f), new Color(0.14f, 0.02f, 0.02f, 1f), 0.58f);
            s_darkThroatMat = MakeMat(fleshShader, "Textures/FleshDoor/tex_fleshdoor_veined_flesh", new Color(0.12f, 0.02f, 0.02f, 1f), Color.black, 0.25f);
            s_eyeMat = MakeMat(fleshShader, "Textures/FleshDoor/tex_fleshdoor_heart_eye", Color.white, new Color(0.16f, 0.04f, 0.05f, 1f), 0.78f);
            s_teethMat = MakeMat(fleshShader, "Textures/FleshDoor/tex_fleshdoor_cracked_teeth", new Color(0.95f, 0.90f, 0.80f, 1f), new Color(0.06f, 0.04f, 0.02f, 1f), 0.42f);
            s_tongueMat = MakeMat(fleshShader, "Textures/FleshDoor/tex_fleshdoor_wet_tongue", new Color(0.96f, 0.80f, 0.80f, 1f), new Color(0.16f, 0.03f, 0.04f, 1f), 0.76f);
            s_ribBoneMat = MakeMat(fleshShader, "Textures/FleshDoor/tex_fleshdoor_aged_rib_bone", new Color(0.88f, 0.84f, 0.72f, 1f), new Color(0.04f, 0.05f, 0.02f, 1f), 0.30f);
            s_tendonMat = MakeMat(fleshShader, "Textures/FleshDoor/tex_fleshdoor_veined_flesh", new Color(0.62f, 0.14f, 0.14f, 1f), new Color(0.12f, 0.01f, 0.02f, 1f), 0.65f);
        }

        private static Material MakeMat(Shader shader, string resTexPath, Color baseColor, Color emissionColor, float wetness)
        {
            Material m = new Material(shader);
            m.hideFlags = HideFlags.DontSave;
            Texture2D tex = Resources.Load<Texture2D>(resTexPath);
            if (tex != null)
            {
                tex.filterMode = FilterMode.Point; // Crisp PS1 texture feel
                if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
                if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", tex);
            }
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", baseColor);
            if (m.HasProperty("_Color")) m.SetColor("_Color", baseColor);
            if (m.HasProperty("_EmissionColor"))
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", emissionColor);
            }
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", wetness);
            if (m.HasProperty("_Wetness")) m.SetFloat("_Wetness", wetness);
            if (m.HasProperty("_AmbientFloor")) m.SetFloat("_AmbientFloor", 0.36f);
            return m;
        }

        // =====================================================================
        // PROCEDURAL 3D MESHES
        // =====================================================================
        private static void EnsureMeshes()
        {
            if (s_esophagusTunnelMesh != null && s_facadeArchMesh != null && s_eyeMesh != null) return;

            s_eyeMesh = MarkPersistent(BuildFrontUvSphereMesh(18, 14));
            s_eyelidMesh = MarkPersistent(BuildEyelidHemisphereMesh(14, 7));
            s_facadeArchMesh = MarkPersistent(BuildOrganicFacadeMesh());
            s_gumMesh = MarkPersistent(BuildSwollenGumMesh());
            s_toothMesh = MarkPersistent(BuildCrackedHumanToothMesh());
            s_lowerJawMesh = MarkPersistent(BuildLowerJawMesh());
            s_tongueMesh = MarkPersistent(BuildWetTongueMesh());
            s_esophagusTunnelMesh = MarkPersistent(BuildEsophagusTunnelMesh());
            s_ribArchMesh = MarkPersistent(BuildRibArchMesh());
            s_tendonMesh = MarkPersistent(BuildCylinderTubeMesh(8, 8));
        }

        private static Mesh MarkPersistent(Mesh m)
        {
            if (m != null)
            {
                m.hideFlags = HideFlags.DontSave;
                m.RecalculateBounds();
                // Generous bounds so the door & plunging esophagus tunnel are never frustum-culled at edge of screen
                m.bounds = new Bounds(Vector3.zero, Vector3.one * 28f);
            }
            return m;
        }

        private static Mesh BuildFrontUvSphereMesh(int segs, int rings)
        {
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();

            for (int iy = 0; iy <= rings; iy++)
            {
                float v = iy / (float)rings;
                float phi = v * Mathf.PI;
                for (int ix = 0; ix <= segs; ix++)
                {
                    float u = ix / (float)segs;
                    float theta = u * Mathf.PI * 2f;
                    float x = Mathf.Sin(phi) * Mathf.Sin(theta);
                    float y = Mathf.Cos(phi);
                    float z = Mathf.Sin(phi) * Mathf.Cos(theta);
                    verts.Add(new Vector3(x, y, z));
                    // Planar front projection onto +Z so the heart-pupil is dead-center facing forward!
                    float uvx = 0.5f + x * 0.48f;
                    float uvy = 0.5f - y * 0.48f;
                    uvs.Add(new Vector2(uvx, uvy));
                }
            }

            int stride = segs + 1;
            for (int iy = 0; iy < rings; iy++)
            {
                for (int ix = 0; ix < segs; ix++)
                {
                    int a = iy * stride + ix;
                    int b = a + 1;
                    int c = (iy + 1) * stride + ix;
                    int d = c + 1;
                    tris.Add(a); tris.Add(c); tris.Add(b);
                    tris.Add(b); tris.Add(c); tris.Add(d);
                }
            }

            Mesh m = new Mesh { name = "Door_HeartEyeball_Mesh" };
            m.SetVertices(verts); m.SetUVs(0, uvs); m.SetTriangles(tris, 0);
            m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        private static Mesh BuildEyelidHemisphereMesh(int segs, int rings)
        {
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();

            // Upper quarter-cap shell wrapping the eyeball
            for (int iy = 0; iy <= rings; iy++)
            {
                float v = iy / (float)rings;
                float phi = v * (Mathf.PI * 0.46f);
                for (int ix = 0; ix <= segs; ix++)
                {
                    float u = ix / (float)segs;
                    float theta = Mathf.Lerp(-Mathf.PI * 0.62f, Mathf.PI * 0.62f, u);
                    float r = 1.0f + Mathf.Sin(v * Mathf.PI) * 0.06f;
                    float x = r * Mathf.Sin(phi) * Mathf.Sin(theta);
                    float y = r * Mathf.Cos(phi);
                    float z = r * Mathf.Sin(phi) * Mathf.Cos(theta);
                    verts.Add(new Vector3(x, y, z));
                    uvs.Add(new Vector2(u, v));
                }
            }

            int stride = segs + 1;
            for (int iy = 0; iy < rings; iy++)
            {
                for (int ix = 0; ix < segs; ix++)
                {
                    int a = iy * stride + ix;
                    int b = a + 1;
                    int c = (iy + 1) * stride + ix;
                    int d = c + 1;
                    tris.Add(a); tris.Add(c); tris.Add(b);
                    tris.Add(b); tris.Add(c); tris.Add(d);
                }
            }

            Mesh m = new Mesh { name = "Door_Eyelid_Mesh" };
            m.SetVertices(verts); m.SetUVs(0, uvs); m.SetTriangles(tris, 0);
            m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        private static Mesh BuildOrganicFacadeMesh()
        {
            // Thick sculpted organic horseshoe archway with a fleshy brow housing the eye cavity
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();

            const int arcSegs = 22;
            const int tubeSegs = 12;

            for (int i = 0; i <= arcSegs; i++)
            {
                float u = i / (float)arcSegs; // 0 = left foot, 0.5 = crown above eye, 1 = right foot
                float ang = Mathf.Lerp(Mathf.PI, 0f, u);
                float cx = Mathf.Cos(ang) * 1.18f;
                float cy = (u < 0.2f || u > 0.8f)
                    ? Mathf.Lerp(0.05f, 1.35f, (u < 0.2f ? u : 1f - u) / 0.2f)
                    : 1.35f + Mathf.Sin(ang) * 1.48f;
                float cz = 0.05f + Mathf.Sin(ang) * 0.16f;

                Vector3 center = new Vector3(cx, cy, cz);
                Vector3 radialOut = new Vector3(Mathf.Cos(ang), Mathf.Sin(ang), 0f).normalized;
                Vector3 fwd = Vector3.forward;

                // Thicker fleshy brow around the top eye socket
                float browBulk = 1f + 0.42f * Mathf.Pow(Mathf.Sin(u * Mathf.PI), 2f);
                float rx = 0.44f * browBulk;
                float rz = 0.58f * browBulk;

                for (int j = 0; j <= tubeSegs; j++)
                {
                    float v = j / (float)tubeSegs;
                    float a = v * Mathf.PI * 2f;
                    float wobble = 1f + 0.10f * Mathf.Sin(u * 26f + a * 3f);
                    Vector3 offset = radialOut * (Mathf.Cos(a) * rx * wobble) + fwd * (Mathf.Sin(a) * rz * wobble);
                    verts.Add(center + offset);
                    uvs.Add(new Vector2(u * 3f, v * 2f));
                }
            }

            int stride = tubeSegs + 1;
            for (int i = 0; i < arcSegs; i++)
            {
                for (int j = 0; j < tubeSegs; j++)
                {
                    int a = i * stride + j;
                    int b = a + 1;
                    int c = (i + 1) * stride + j;
                    int d = c + 1;
                    tris.Add(a); tris.Add(b); tris.Add(c);
                    tris.Add(b); tris.Add(d); tris.Add(c);
                }
            }

            Mesh m = new Mesh { name = "Door_FleshFacade_Mesh" };
            m.SetVertices(verts); m.SetUVs(0, uvs); m.SetTriangles(tris, 0);
            m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        private static Mesh BuildSwollenGumMesh()
        {
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            const int segs = 16;
            const int ring = 10;

            for (int i = 0; i <= segs; i++)
            {
                float u = i / (float)segs;
                float x = Mathf.Lerp(-0.92f, 0.92f, u);
                float z = -0.18f + Mathf.Sin(u * Mathf.PI) * 0.24f;
                float y = -0.06f + Mathf.Sin(u * Mathf.PI) * 0.14f;
                Vector3 c = new Vector3(x, y, z);
                float rad = 0.22f * (0.75f + 0.35f * Mathf.Sin(u * Mathf.PI));

                for (int j = 0; j <= ring; j++)
                {
                    float v = j / (float)ring;
                    float a = v * Mathf.PI * 2f;
                    float bulge = 1f + 0.12f * Mathf.Sin(u * 15f);
                    verts.Add(c + new Vector3(0f, Mathf.Sin(a) * rad * 0.85f * bulge, Mathf.Cos(a) * rad * bulge));
                    uvs.Add(new Vector2(u * 2f, v));
                }
            }

            int stride = ring + 1;
            for (int i = 0; i < segs; i++)
            {
                for (int j = 0; j < ring; j++)
                {
                    int a = i * stride + j;
                    int b = a + 1;
                    int c = (i + 1) * stride + j;
                    int d = c + 1;
                    tris.Add(a); tris.Add(c); tris.Add(b);
                    tris.Add(b); tris.Add(c); tris.Add(d);
                }
            }

            Mesh m = new Mesh { name = "Door_SwollenGum_Mesh" };
            m.SetVertices(verts); m.SetUVs(0, uvs); m.SetTriangles(tris, 0);
            m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        private static Mesh BuildCrackedHumanToothMesh()
        {
            // Chiseled slightly tapered human-like molar/incisor block (rounded edges, wider at root, chisel at bottom)
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            const int rings = 6;
            const int segs = 10;

            for (int iy = 0; iy <= rings; iy++)
            {
                float v = iy / (float)rings; // 0 = top root, 1 = bottom biting edge
                float y = Mathf.Lerp(0.5f, -0.5f, v);
                float wx = Mathf.Lerp(0.54f, 0.44f, v * v);
                float wz = Mathf.Lerp(0.46f, 0.20f, v);
                for (int ix = 0; ix <= segs; ix++)
                {
                    float u = ix / (float)segs;
                    float a = u * Mathf.PI * 2f;
                    // Squircle cross-section for authentic thick human tooth look
                    float ca = Mathf.Cos(a);
                    float sa = Mathf.Sin(a);
                    float sx = Mathf.Sign(ca) * Mathf.Pow(Mathf.Abs(ca), 0.72f) * wx;
                    float sz = Mathf.Sign(sa) * Mathf.Pow(Mathf.Abs(sa), 0.72f) * wz;
                    verts.Add(new Vector3(sx, y, sz));
                    uvs.Add(new Vector2(u, v));
                }
            }

            int stride = segs + 1;
            for (int iy = 0; iy < rings; iy++)
            {
                for (int ix = 0; ix < segs; ix++)
                {
                    int a = iy * stride + ix;
                    int b = a + 1;
                    int c = (iy + 1) * stride + ix;
                    int d = c + 1;
                    tris.Add(a); tris.Add(b); tris.Add(c);
                    tris.Add(b); tris.Add(d); tris.Add(c);
                }
            }

            Mesh m = new Mesh { name = "Door_HumanTooth_Mesh" };
            m.SetVertices(verts); m.SetUVs(0, uvs); m.SetTriangles(tris, 0);
            m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        private static Mesh BuildLowerJawMesh()
        {
            // Thick U-shaped fleshy lower jaw lip that hinges upward to close the mouth during chew!
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            const int segs = 16;
            const int ring = 10;

            for (int i = 0; i <= segs; i++)
            {
                float u = i / (float)segs;
                float x = Mathf.Lerp(-0.92f, 0.92f, u);
                float z = -0.15f + Mathf.Sin(u * Mathf.PI) * 0.34f;
                float y = 0.04f;
                Vector3 c = new Vector3(x, y, z);
                float rad = 0.24f * (0.80f + 0.30f * Mathf.Sin(u * Mathf.PI));

                for (int j = 0; j <= ring; j++)
                {
                    float v = j / (float)ring;
                    float a = v * Mathf.PI * 2f;
                    verts.Add(c + new Vector3(0f, Mathf.Sin(a) * rad * 0.80f, Mathf.Cos(a) * rad));
                    uvs.Add(new Vector2(u * 2f, v));
                }
            }

            int stride = ring + 1;
            for (int i = 0; i < segs; i++)
            {
                for (int j = 0; j < ring; j++)
                {
                    int a = i * stride + j;
                    int b = a + 1;
                    int c = (i + 1) * stride + j;
                    int d = c + 1;
                    tris.Add(a); tris.Add(c); tris.Add(b);
                    tris.Add(b); tris.Add(c); tris.Add(d);
                }
            }

            Mesh m = new Mesh { name = "Door_LowerJaw_Mesh" };
            m.SetVertices(verts); m.SetUVs(0, uvs); m.SetTriangles(tris, 0);
            m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        private static Mesh BuildWetTongueMesh()
        {
            // Wide flat-curved wet tongue rolling out forward (+Z) onto the floor like a welcome mat!
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            const int lenSegs = 12;
            const int radSegs = 12;

            for (int i = 0; i <= lenSegs; i++)
            {
                float u = i / (float)lenSegs; // 0 = throat root, 1 = rounded tip on floor
                float z = u * 1.45f;
                float y = Mathf.Lerp(0.16f, 0.01f, Mathf.Pow(u, 0.75f));
                float width = Mathf.Sin(Mathf.Pow(u, 0.65f) * Mathf.PI * 0.85f + 0.15f) * 0.64f;
                if (u > 0.96f) width = 0.02f;
                float thick = Mathf.Lerp(0.16f, 0.05f, u);

                for (int j = 0; j <= radSegs; j++)
                {
                    float v = j / (float)radSegs;
                    float a = v * Mathf.PI * 2f;
                    float x = Mathf.Cos(a) * width;
                    float dy = Mathf.Sin(a) * thick;
                    // Slight center groove on top of tongue
                    if (dy > 0f) dy *= (1f - 0.25f * Mathf.Exp(-(x * x) * 14f));
                    verts.Add(new Vector3(x, y + dy, z));
                    uvs.Add(new Vector2(0.5f + Mathf.Cos(a) * 0.45f, u));
                }
            }

            int stride = radSegs + 1;
            for (int i = 0; i < lenSegs; i++)
            {
                for (int j = 0; j < radSegs; j++)
                {
                    int a = i * stride + j;
                    int b = a + 1;
                    int c = (i + 1) * stride + j;
                    int d = c + 1;
                    tris.Add(a); tris.Add(c); tris.Add(b);
                    tris.Add(b); tris.Add(c); tris.Add(d);
                }
            }

            Mesh m = new Mesh { name = "Door_WetTongue_Mesh" };
            m.SetVertices(verts); m.SetUVs(0, uvs); m.SetTriangles(tris, 0);
            m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        private static Mesh BuildEsophagusTunnelMesh()
        {
            // Massive segmented muscular esophagus curving out the back (-Z) and plunging down (-Y) into the acid lake!
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            const int pathSegs = 22;
            const int radSegs = 16;

            for (int i = 0; i <= pathSegs; i++)
            {
                float t = i / (float)pathSegs;
                Vector3 c = EvalTunnelCenter(t);
                Vector3 fwd = EvalTunnelTangent(t);
                Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;
                if (right.sqrMagnitude < 0.01f) right = Vector3.right;
                Vector3 up = Vector3.Cross(fwd, right).normalized;

                // Peristaltic segmented rings along the esophagus tube
                float ringBulge = 1f + 0.11f * Mathf.Sin(t * Mathf.PI * 12f);
                float baseR = Mathf.Lerp(1.18f, 0.92f, t) * ringBulge;

                for (int j = 0; j <= radSegs; j++)
                {
                    float v = j / (float)radSegs;
                    float a = v * Mathf.PI * 2f;
                    float organic = 1f + 0.06f * Mathf.Sin(a * 5f + t * 14f);
                    Vector3 offset = (right * Mathf.Cos(a) + up * Mathf.Sin(a)) * (baseR * organic);
                    verts.Add(c + offset);
                    uvs.Add(new Vector2(v * 2.5f, t * 4f));
                }
            }

            int stride = radSegs + 1;
            for (int i = 0; i < pathSegs; i++)
            {
                for (int j = 0; j < radSegs; j++)
                {
                    int a = i * stride + j;
                    int b = a + 1;
                    int c = (i + 1) * stride + j;
                    int d = c + 1;
                    tris.Add(a); tris.Add(b); tris.Add(c);
                    tris.Add(b); tris.Add(d); tris.Add(c);
                }
            }

            Mesh m = new Mesh { name = "Door_EsophagusTunnel_Mesh" };
            m.SetVertices(verts); m.SetUVs(0, uvs); m.SetTriangles(tris, 0);
            m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        private static Mesh BuildRibArchMesh()
        {
            // Curved ancient bone rib arch wrapping ~220 degrees over the top & sides of the esophagus tunnel
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            const int arcSegs = 16;
            const int tubeSegs = 8;

            for (int i = 0; i <= arcSegs; i++)
            {
                float u = i / (float)arcSegs;
                float ang = Mathf.Lerp(-0.22f * Mathf.PI, 1.22f * Mathf.PI, u);
                float r = 1.26f;
                Vector3 c = new Vector3(Mathf.Cos(ang) * r, Mathf.Sin(ang) * r, 0f);
                Vector3 radOut = new Vector3(Mathf.Cos(ang), Mathf.Sin(ang), 0f).normalized;
                float thick = Mathf.Lerp(0.11f, 0.18f, Mathf.Sin(u * Mathf.PI));

                for (int j = 0; j <= tubeSegs; j++)
                {
                    float v = j / (float)tubeSegs;
                    float a = v * Mathf.PI * 2f;
                    Vector3 off = radOut * (Mathf.Cos(a) * thick) + Vector3.forward * (Mathf.Sin(a) * thick * 1.35f);
                    verts.Add(c + off);
                    uvs.Add(new Vector2(u * 2f, v));
                }
            }

            int stride = tubeSegs + 1;
            for (int i = 0; i < arcSegs; i++)
            {
                for (int j = 0; j < tubeSegs; j++)
                {
                    int a = i * stride + j;
                    int b = a + 1;
                    int c = (i + 1) * stride + j;
                    int d = c + 1;
                    tris.Add(a); tris.Add(b); tris.Add(c);
                    tris.Add(b); tris.Add(d); tris.Add(c);
                }
            }

            Mesh m = new Mesh { name = "Door_BoneRibArch_Mesh" };
            m.SetVertices(verts); m.SetUVs(0, uvs); m.SetTriangles(tris, 0);
            m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        private static Mesh BuildCylinderTubeMesh(int segs, int rings)
        {
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            for (int iy = 0; iy <= rings; iy++)
            {
                float v = iy / (float)rings;
                float y = Mathf.Lerp(-1f, 1f, v);
                float r = 1f - 0.25f * Mathf.Sin(v * Mathf.PI);
                for (int ix = 0; ix <= segs; ix++)
                {
                    float u = ix / (float)segs;
                    float a = u * Mathf.PI * 2f;
                    verts.Add(new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r));
                    uvs.Add(new Vector2(u, v * 2f));
                }
            }
            int stride = segs + 1;
            for (int iy = 0; iy < rings; iy++)
            {
                for (int ix = 0; ix < segs; ix++)
                {
                    int a = iy * stride + ix;
                    int b = a + 1;
                    int c = (iy + 1) * stride + ix;
                    int d = c + 1;
                    tris.Add(a); tris.Add(b); tris.Add(c);
                    tris.Add(b); tris.Add(d); tris.Add(c);
                }
            }
            Mesh m = new Mesh { name = "Door_Tendon_Mesh" };
            m.SetVertices(verts); m.SetUVs(0, uvs); m.SetTriangles(tris, 0);
            m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

#if UNITY_EDITOR
        private static readonly HashSet<string> s_orphanV2Names = new HashSet<string>
        {
            "Door_Flesh_Facade",
            "Door_Dark_Throat",
            "Door_Eye_Socket",
            "Door_Eyeball_V2",
            "Door_Eyelid_Upper_V2",
            "Door_Eyelid_Lower_V2",
            "Door_Upper_Gum_V2",
            "Door_Upper_Teeth_V2",
            "Door_Lower_Jaw_V2",
            "Door_Tongue_V2",
            "Door_Esophagus_Tunnel_V2",
            "Esophagus_Tube_Mesh",
            "Tunnel_AcidGlow"
        };

        [UnityEditor.InitializeOnLoadMethod]
        private static void RegisterEditorRefresh()
        {
            UnityEditor.EditorApplication.delayCall += RefreshAllDoorsInEditor;
        }

        private static void RefreshAllDoorsInEditor()
        {
            if (Application.isPlaying) return;

            // 1. Clean up any leaked root orphan V2 objects from before recursive DontSave fix
            var activeScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (activeScene.IsValid() && activeScene.isLoaded)
            {
                GameObject[] roots = activeScene.GetRootGameObjects();
                bool removedAny = false;
                for (int i = 0; i < roots.Length; i++)
                {
                    GameObject r = roots[i];
                    if (r == null) continue;
                    if (s_orphanV2Names.Contains(r.name) ||
                        r.name.StartsWith("Upper_Tooth_") ||
                        r.name.StartsWith("Lower_Tooth_") ||
                        r.name.StartsWith("Tunnel_RibArch_") ||
                        r.name.StartsWith("Anchor_Tendon_"))
                    {
                        Object.DestroyImmediate(r);
                        removedAny = true;
                    }
                }
                if (removedAny)
                {
                    UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(activeScene);
                }
            }

            // 2. Force all 4 FleshEyeDoorController doors in the scene to build V2 preview immediately
            FleshEyeDoorController[] doors = Object.FindObjectsByType<FleshEyeDoorController>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None
            );
            for (int i = 0; i < doors.Length; i++)
            {
                if (doors[i] != null)
                {
                    doors[i].EnsureEditorPreview();
                }
            }
            UnityEditor.SceneView.RepaintAll();
        }
#endif
    }
}
