#pragma warning disable 0618
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering;
using UnityEngine.ProBuilder;
using UnityEngine.ProBuilder.MeshOperations;

namespace MapaEstomago.Editor
{
    public class StomachLevelBuilderWindow : EditorWindow
    {
        private const string MaterialsFolder = "Assets/PilotChamber/Materials";
        private const string TexturesFolder = "Assets/PilotChamber/Textures";
        private const string TriplanarShaderName = "MapaEstomago/URP/PS1_TriplanarFlesh";

        public enum StomachMaterialType
        {
            FleshFloor,
            FleshWall,
            HellOrgans,
            Cartilage,
            DigestedCalcified,
            Pustule
        }

        private Vector3 spawnSize = new Vector3(4f, 2f, 4f);
        private StomachMaterialType selectedMaterialType = StomachMaterialType.FleshWall;
        private Vector2 scrollPos;

        // Parametros de Organificacao
        private float organifyBulge = 0.22f;
        private float organifyNoiseScale = 0.65f;
        private bool organifySubdivide = true;
        private bool organifySmoothNormals = true;

        // Parametros de Fusao / Mesclagem
        private float mergeWeldDist = 0.35f;
        private bool mergeOrganifySeam = true;

        [MenuItem("Estomago/Construtor de Fases (ProBuilder + Paleta) %#b", priority = 10)]
        public static void OpenWindow()
        {
            StomachLevelBuilderWindow win = GetWindow<StomachLevelBuilderWindow>("Construtor Estomago");
            win.minSize = new Vector2(360, 600);
            win.Show();
        }

        private void OnGUI()
        {
            scrollPos = EditorGUILayout.BeginScrollView(scrollPos);

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("🥩 CONSTRUTOR DE FASES PS1 ORGÂNICO", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Ferramenta de construção orgânica para o Estômago:\n" +
                "• Modele livremente com ProBuilder.\n" +
                "• Texturas Triplanares se ajustam SOZINHAS (nunca esticam).\n" +
                "• Botão 'Organificar' transforma caixas retas em massas de carne viva.\n" +
                "• Botão 'Mesclar' funde objetos adjacentes em uma única carne contínua!",
                MessageType.Info);

            EditorGUILayout.Space(8);
            DrawProBuilderSection();

            EditorGUILayout.Space(8);
            DrawSpawnShapeSection();

            EditorGUILayout.Space(8);
            DrawOrganifySection();

            EditorGUILayout.Space(8);
            DrawMergeSection();

            EditorGUILayout.Space(8);
            DrawMaterialPaletteSection();

            EditorGUILayout.EndScrollView();
        }

        private void DrawProBuilderSection()
        {
            EditorGUILayout.LabelField("1. Ferramentas de Modelagem ProBuilder", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("Abrir Barra ProBuilder", GUILayout.Height(26)))
            {
                EditorApplication.ExecuteMenuItem("Tools/ProBuilder/Edit/Edit Shape");
                EditorApplication.ExecuteMenuItem("Tools/ProBuilder/Editors/Create Shape/Cube  %#K");
            }

            if (GUILayout.Button("Desenhar Polígono (PolyShape)", GUILayout.Height(26)))
            {
                EditorApplication.ExecuteMenuItem("Tools/ProBuilder/Editors/Create PolyShape");
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Extrudar Face (Ctrl+E)"))
            {
                EditorApplication.ExecuteMenuItem("Tools/ProBuilder/Geometry/Extrude %E");
            }
            if (GUILayout.Button("Chanfrar Bordas (Bevel)"))
            {
                EditorApplication.ExecuteMenuItem("Tools/ProBuilder/Geometry/Bevel Edges");
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawSpawnShapeSection()
        {
            EditorGUILayout.LabelField("2. Criar Formas Prontas na Cena", EditorStyles.boldLabel);
            spawnSize = EditorGUILayout.Vector3Field("Tamanho Inicial (m)", spawnSize);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("🧱 Bloco / Chão", GUILayout.Height(28)))
            {
                SpawnProBuilderShape(ShapeType.Cube, "Flesh_Block", spawnSize);
            }
            if (GUILayout.Button("🪜 Rampa / Escada", GUILayout.Height(28)))
            {
                SpawnProBuilderShape(ShapeType.Stair, "Flesh_Stair", spawnSize);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("🏛️ Arco / Túnel", GUILayout.Height(28)))
            {
                SpawnProBuilderShape(ShapeType.Arch, "Flesh_Arch", spawnSize);
            }
            if (GUILayout.Button("柱 Pilar Muscular", GUILayout.Height(28)))
            {
                SpawnProBuilderShape(ShapeType.Cylinder, "Flesh_Pillar", spawnSize);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("🔺 Prisma / Rampa", GUILayout.Height(24)))
            {
                SpawnProBuilderShape(ShapeType.Prism, "Flesh_Prism", spawnSize);
            }
            if (GUILayout.Button("⭕ Tubo / Intestino", GUILayout.Height(24)))
            {
                SpawnProBuilderShape(ShapeType.Pipe, "Flesh_Pipe", spawnSize);
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawOrganifySection()
        {
            EditorGUILayout.LabelField("3. ✨ Organificação Automática (Tirar Aspecto Quadrado)", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Deforma vértices com calombos orgânicos e arredonda quinas duras de 90°.", MessageType.None);

            organifyBulge = EditorGUILayout.Slider("Intensidade dos Calombos", organifyBulge, 0.05f, 0.6f);
            organifyNoiseScale = EditorGUILayout.Slider("Escala do Relevo", organifyNoiseScale, 0.2f, 1.8f);

            EditorGUILayout.BeginHorizontal();
            organifySubdivide = EditorGUILayout.ToggleLeft("Subdividir Malha (+Resolução)", organifySubdivide);
            organifySmoothNormals = EditorGUILayout.ToggleLeft("Suavizar Normais (Pele Lisa)", organifySmoothNormals);
            EditorGUILayout.EndHorizontal();

            GUI.backgroundColor = new Color(0.95f, 0.55f, 0.45f);
            if (GUILayout.Button("🥩 Organificar Objeto(s) Selecionado(s)", GUILayout.Height(32)))
            {
                OrganifySelection(organifyBulge, organifyNoiseScale, organifySubdivide, organifySmoothNormals);
            }
            GUI.backgroundColor = Color.white;
        }

        private void DrawMergeSection()
        {
            EditorGUILayout.LabelField("4. 🧬 Mesclar e Fundir Objetos Próximos", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Aproxime 2 ou mais blocos (encostando ou se sobrepondo) e selecione todos. " +
                "Este botão funde a geometria em uma única peça, solda os vértices na junção e suaviza a transição como um único tecido vivo!",
                MessageType.None);

            mergeWeldDist = EditorGUILayout.Slider("Distância de Fusão (m)", mergeWeldDist, 0.05f, 1.2f);
            mergeOrganifySeam = EditorGUILayout.ToggleLeft("Aplicar Relevo Orgânico na Junção", mergeOrganifySeam);

            GUI.backgroundColor = new Color(0.55f, 0.85f, 0.55f);
            if (GUILayout.Button("🔗 Mesclar e Fundir Objetos Selecionados", GUILayout.Height(32)))
            {
                MergeSelectedObjects(mergeWeldDist, mergeOrganifySeam);
            }
            GUI.backgroundColor = Color.white;
        }

        private void DrawMaterialPaletteSection()
        {
            EditorGUILayout.LabelField("5. Paleta de Materiais Triplanares (Auto-Ajuste)", EditorStyles.boldLabel);

            selectedMaterialType = (StomachMaterialType)EditorGUILayout.EnumPopup("Tipo Selecionado:", selectedMaterialType);

            EditorGUILayout.Space(2);
            DrawMaterialButton("🥩 Chão de Carne (Walkable)", StomachMaterialType.FleshFloor, new Color(0.85f, 0.45f, 0.45f));
            DrawMaterialButton("🩸 Parede Muscular (Corte BG3)", StomachMaterialType.FleshWall, new Color(0.95f, 0.35f, 0.35f));
            DrawMaterialButton("🫀 Órgãos Vivos (Hell Organs)", StomachMaterialType.HellOrgans, new Color(0.65f, 0.35f, 0.45f));
            DrawMaterialButton("🦴 Cartilagem / Osso Calcificado", StomachMaterialType.Cartilage, new Color(0.85f, 0.85f, 0.70f));
            DrawMaterialButton("🪨 Calcificado Digestivo (Rochas)", StomachMaterialType.DigestedCalcified, new Color(0.75f, 0.70f, 0.60f));
            DrawMaterialButton("☣️ Pústulas Tóxicas (Brilho)", StomachMaterialType.Pustule, new Color(0.85f, 0.80f, 0.30f));

            EditorGUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Aplicar no Objeto Inteiro", GUILayout.Height(26)))
            {
                ApplyMaterialToSelection(selectedMaterialType, onlySelectedFaces: false);
            }

            if (GUILayout.Button("Aplicar nas Faces Selecionadas", GUILayout.Height(26)))
            {
                ApplyMaterialToSelection(selectedMaterialType, onlySelectedFaces: true);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);
            if (GUILayout.Button("Atualizar / Gerar Todos os Materiais Triplanares"))
            {
                EnsureAllTriplanarMaterials();
                ShowNotification(new GUIContent("Materiais Triplanares Atualizados!"));
            }
        }

        private void DrawMaterialButton(string label, StomachMaterialType type, Color col)
        {
            GUI.backgroundColor = col;
            if (GUILayout.Button(label, GUILayout.Height(24)))
            {
                selectedMaterialType = type;
                ApplyMaterialToSelection(type, onlySelectedFaces: false);
            }
            GUI.backgroundColor = Color.white;
        }

        public static void OrganifySelection(float bulge, float noiseScale, bool subdivide, bool smoothNormals)
        {
            GameObject[] selected = Selection.gameObjects;
            if (selected == null || selected.Length == 0)
            {
                EditorUtility.DisplayDialog("Nenhum Objeto Selecionado", "Selecione pelo menos um objeto na Scene para organificar.", "OK");
                return;
            }

            int count = 0;
            foreach (GameObject go in selected)
            {
                ProBuilderMesh pb = go.GetComponent<ProBuilderMesh>();
                if (pb == null)
                {
                    MeshFilter mf = go.GetComponent<MeshFilter>();
                    if (mf != null && mf.sharedMesh != null)
                    {
                        var importer = new MeshImporter(go);
                        importer.Import(null);
                        pb = go.GetComponent<ProBuilderMesh>();
                    }
                }

                if (pb == null) continue;

                Undo.RecordObject(go, "Organify Mesh");
                Undo.RecordObject(pb, "Organify Mesh");

                if (subdivide)
                {
                    SubdivideMesh(pb);
                }

                if (smoothNormals)
                {
                    foreach (Face f in pb.faces)
                    {
                        f.smoothingGroup = 1;
                    }
                }

                IList<Vector3> positions = pb.positions;
                IList<Vector3> normals = pb.GetNormals();
                Vector3[] newPositions = new Vector3[positions.Count];

                for (int i = 0; i < positions.Count; i++)
                {
                    Vector3 wp = pb.transform.TransformPoint(positions[i]);
                    float n1 = Mathf.PerlinNoise(wp.x * noiseScale, wp.z * noiseScale) * 2f - 1f;
                    float n2 = Mathf.PerlinNoise(wp.y * noiseScale * 1.4f, (wp.x + wp.z) * noiseScale) * 2f - 1f;
                    float n3 = Mathf.Sin(wp.x * 1.8f + wp.z * 1.3f) * 0.4f;

                    float totalDisp = (n1 * 0.65f + n2 * 0.35f + n3) * bulge;
                    Vector3 disp = normals[i] * totalDisp;
                    newPositions[i] = positions[i] + pb.transform.InverseTransformDirection(disp);
                }

                pb.positions = newPositions;
                pb.ToMesh();
                pb.Refresh();

                MeshCollider mc = go.GetComponent<MeshCollider>();
                if (mc != null)
                {
                    mc.sharedMesh = pb.GetComponent<MeshFilter>().sharedMesh;
                }

                EditorUtility.SetDirty(go);
                count++;
            }

            Debug.Log($"<color=#FF8866>[StomachLevelBuilder] {count} objeto(s) organificado(s) com sucesso!</color>");
        }

        public static void MergeSelectedObjects(float weldDistance, bool organifySeam)
        {
            GameObject[] selected = Selection.gameObjects;
            if (selected == null || selected.Length < 2)
            {
                EditorUtility.DisplayDialog(
                    "Selecione 2 ou mais objetos",
                    "Para mesclar, selecione pelo menos 2 objetos adjacentes ou que se sobrepõem na cena.",
                    "OK");
                return;
            }

            List<ProBuilderMesh> meshesToCombine = new List<ProBuilderMesh>();
            List<GameObject> objectsToDestroy = new List<GameObject>();
            Material dominantMaterial = null;

            foreach (GameObject go in selected)
            {
                ProBuilderMesh pb = go.GetComponent<ProBuilderMesh>();
                if (pb == null)
                {
                    MeshFilter mf = go.GetComponent<MeshFilter>();
                    if (mf != null && mf.sharedMesh != null)
                    {
                        var importer = new MeshImporter(go);
                        importer.Import(null);
                        pb = go.GetComponent<ProBuilderMesh>();
                    }
                }

                if (pb != null)
                {
                    meshesToCombine.Add(pb);
                    objectsToDestroy.Add(go);
                    MeshRenderer mr = go.GetComponent<MeshRenderer>();
                    if (dominantMaterial == null && mr != null)
                    {
                        dominantMaterial = mr.sharedMaterial;
                    }
                }
            }

            if (meshesToCombine.Count < 2)
            {
                EditorUtility.DisplayDialog("Aviso", "Não foi possível encontrar pelo menos 2 malhas ProBuilder válidas na seleção.", "OK");
                return;
            }

            List<ProBuilderMesh> combinedList = CombineMeshes.Combine(meshesToCombine);
            if (combinedList == null || combinedList.Count == 0) return;

            ProBuilderMesh merged = combinedList[0];
            merged.gameObject.name = "Fleshy_Merged_Structure";
            Undo.RegisterCreatedObjectUndo(merged.gameObject, "Merge Flesh Structures");

            List<int> allIndices = new List<int>();
            for (int i = 0; i < merged.vertexCount; i++) allIndices.Add(i);
            VertexEditing.WeldVertices(merged, allIndices, weldDistance);

            foreach (Face f in merged.faces)
            {
                f.smoothingGroup = 1;
            }

            if (organifySeam)
            {
                SubdivideMesh(merged);
                IList<Vector3> positions = merged.positions;
                IList<Vector3> normals = merged.GetNormals();
                Vector3[] newPositions = new Vector3[positions.Count];
                float noiseScale = 0.75f;
                float bulge = 0.18f;

                for (int i = 0; i < positions.Count; i++)
                {
                    Vector3 wp = merged.transform.TransformPoint(positions[i]);
                    float n1 = Mathf.PerlinNoise(wp.x * noiseScale, wp.z * noiseScale) * 2f - 1f;
                    float n2 = Mathf.PerlinNoise(wp.y * noiseScale * 1.5f, (wp.x + wp.z) * noiseScale) * 2f - 1f;
                    Vector3 disp = normals[i] * ((n1 * 0.7f + n2 * 0.3f) * bulge);
                    newPositions[i] = positions[i] + merged.transform.InverseTransformDirection(disp);
                }
                merged.positions = newPositions;
            }

            merged.ToMesh();
            merged.Refresh();

            MeshRenderer mergedMr = merged.GetComponent<MeshRenderer>();
            if (mergedMr != null)
            {
                if (dominantMaterial == null) dominantMaterial = GetTriplanarMaterial(StomachMaterialType.FleshWall);
                mergedMr.sharedMaterial = dominantMaterial;
                mergedMr.shadowCastingMode = ShadowCastingMode.Off;
            }

            MeshCollider mc = merged.GetComponent<MeshCollider>();
            if (mc == null) mc = merged.gameObject.AddComponent<MeshCollider>();
            mc.sharedMesh = merged.GetComponent<MeshFilter>().sharedMesh;

            foreach (GameObject oldGo in objectsToDestroy)
            {
                if (oldGo != null && oldGo != merged.gameObject)
                {
                    Undo.DestroyObjectImmediate(oldGo);
                }
            }

            Selection.activeGameObject = merged.gameObject;
            Debug.Log("<color=#66FF66>[StomachLevelBuilder] Objetos mesclados com sucesso em uma única estrutura orgânica contínua!</color>");
        }

        private static void SubdivideMesh(ProBuilderMesh pb)
        {
            try
            {
                Assembly asm = typeof(ProBuilderMesh).Assembly;
                Type subType = asm.GetType("UnityEngine.ProBuilder.MeshOperations.Subdivision");
                if (subType != null)
                {
                    MethodInfo m = subType.GetMethod("Subdivide", new[] { typeof(ProBuilderMesh) });
                    if (m != null)
                    {
                        m.Invoke(null, new object[] { pb });
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[StomachLevelBuilder] Subdivisão ignorada: {ex.Message}");
            }
        }

        public static GameObject SpawnProBuilderShape(ShapeType shapeType, string prefix, Vector3 size)
        {
            EnsureAllTriplanarMaterials();

            ProBuilderMesh pb = ShapeGenerator.CreateShape(shapeType);
            if (pb == null) return null;

            GameObject go = pb.gameObject;
            go.name = $"{prefix}_{DateTime.Now:mmss}";
            Undo.RegisterCreatedObjectUndo(go, $"Spawn {prefix}");

            SceneView view = SceneView.lastActiveSceneView;
            if (view != null)
            {
                go.transform.position = view.pivot;
            }
            else
            {
                go.transform.position = Vector3.zero;
            }

            go.transform.localScale = size;

            Material mat = GetTriplanarMaterial(StomachMaterialType.FleshWall);
            MeshRenderer mr = go.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                mr.sharedMaterial = mat;
                mr.shadowCastingMode = ShadowCastingMode.Off;
            }

            MeshCollider mc = go.GetComponent<MeshCollider>();
            if (mc == null) mc = go.AddComponent<MeshCollider>();

            pb.ToMesh();
            pb.Refresh();
            if (mc != null) mc.sharedMesh = pb.GetComponent<MeshFilter>().sharedMesh;

            Selection.activeGameObject = go;
            return go;
        }

        public static void ApplyMaterialToSelection(StomachMaterialType type, bool onlySelectedFaces)
        {
            Material mat = GetTriplanarMaterial(type);
            if (mat == null) return;

            GameObject[] selected = Selection.gameObjects;
            if (selected == null || selected.Length == 0) return;

            foreach (GameObject go in selected)
            {
                ProBuilderMesh pb = go.GetComponent<ProBuilderMesh>();
                if (pb != null && onlySelectedFaces && pb.selectedFaceCount > 0)
                {
                    Undo.RecordObject(pb, "Apply Material to Faces");
                    pb.SetMaterial(pb.GetSelectedFaces(), mat);
                    pb.ToMesh();
                    pb.Refresh();
                }
                else
                {
                    MeshRenderer mr = go.GetComponent<MeshRenderer>();
                    if (mr != null)
                    {
                        Undo.RecordObject(mr, "Apply Material");
                        mr.sharedMaterial = mat;
                        if (type == StomachMaterialType.FleshWall)
                        {
                            mr.shadowCastingMode = ShadowCastingMode.Off;
                        }
                    }
                }
            }
        }

        public static Material GetTriplanarMaterial(StomachMaterialType type)
        {
            string matName = "Mat_Triplanar_" + type;
            string path = $"{MaterialsFolder}/{matName}.mat";
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                EnsureAllTriplanarMaterials();
                mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            }
            return mat;
        }

        public static void EnsureAllTriplanarMaterials()
        {
            if (!AssetDatabase.IsValidFolder(MaterialsFolder))
            {
                AssetDatabase.CreateFolder("Assets/PilotChamber", "Materials");
            }

            CreateTriplanarMat("Mat_Triplanar_FleshFloor", "tex_flesh_floor.png", m =>
            {
                m.SetFloat("_TextureScale", 2.0f);
                m.SetFloat("_BreathSpeed", 1.2f);
                m.SetFloat("_BreathAmp", 0.005f);
                m.SetFloat("_OrganicBulge", 0.04f);
                m.SetFloat("_Wetness", 0.30f);
                m.SetFloat("_EnableBG3Cutout", 0f);
            });

            CreateTriplanarMat("Mat_Triplanar_FleshWall", "tex_flesh_wall.png", m =>
            {
                m.SetFloat("_TextureScale", 2.4f);
                m.SetFloat("_BreathSpeed", 1.5f);
                m.SetFloat("_BreathAmp", 0.015f);
                m.SetFloat("_OrganicBulge", 0.08f);
                m.SetFloat("_Wetness", 0.40f);
                m.SetFloat("_EnableBG3Cutout", 1f);
            });

            CreateTriplanarMat("Mat_Triplanar_HellOrgans", "tex_hell_organs.png", m =>
            {
                m.SetFloat("_TextureScale", 2.0f);
                m.SetFloat("_BreathSpeed", 1.6f);
                m.SetFloat("_BreathAmp", 0.012f);
                m.SetFloat("_OrganicBulge", 0.06f);
                m.SetFloat("_Wetness", 0.35f);
                m.SetFloat("_EnableBG3Cutout", 0f);
            });

            CreateTriplanarMat("Mat_Triplanar_Cartilage", "tex_cartilage.png", m =>
            {
                m.SetFloat("_TextureScale", 2.2f);
                m.SetFloat("_BreathSpeed", 1.0f);
                m.SetFloat("_BreathAmp", 0.004f);
                m.SetFloat("_OrganicBulge", 0.03f);
                m.SetFloat("_Wetness", 0.25f);
                m.SetFloat("_EnableBG3Cutout", 0f);
            });

            CreateTriplanarMat("Mat_Triplanar_DigestedCalcified", "tex_digested_calcified.png", m =>
            {
                m.SetFloat("_TextureScale", 2.0f);
                m.SetFloat("_BreathSpeed", 1.0f);
                m.SetFloat("_BreathAmp", 0.005f);
                m.SetFloat("_OrganicBulge", 0.04f);
                m.SetFloat("_Wetness", 0.45f);
                m.SetFloat("_EnableBG3Cutout", 0f);
            });

            CreateTriplanarMat("Mat_Triplanar_Pustule", "tex_pustule.png", m =>
            {
                m.SetFloat("_TextureScale", 1.8f);
                m.SetFloat("_BreathSpeed", 2.2f);
                m.SetFloat("_BreathAmp", 0.020f);
                m.SetFloat("_OrganicBulge", 0.12f);
                m.SetFloat("_Wetness", 0.75f);
                m.SetColor("_EmissionColor", new Color(0.55f, 0.42f, 0.06f, 1f));
                m.SetFloat("_EnableBG3Cutout", 0f);
            });

            AssetDatabase.SaveAssets();
        }

        private static Material CreateTriplanarMat(string matName, string texFileName, Action<Material> customize = null)
        {
            string matPath = $"{MaterialsFolder}/{matName}.mat";
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            Shader s = Shader.Find(TriplanarShaderName);

            if (mat == null)
            {
                mat = new Material(s != null ? s : Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, matPath);
            }
            else if (s != null && mat.shader != s)
            {
                mat.shader = s;
            }

            string texPath = $"{TexturesFolder}/{texFileName}";
            Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
            if (tex != null)
            {
                mat.SetTexture("_BaseMap", tex);
            }

            customize?.Invoke(mat);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // Atalhos de Menu no topo da Unity
        [MenuItem("Estomago/Modelagem/🥩 Organificar Seleção (Curvas e Relevo de Carne)", priority = 10)]
        public static void MenuOrganify() => OrganifySelection(0.22f, 0.65f, true, true);

        [MenuItem("Estomago/Modelagem/🔗 Mesclar e Fundir Objetos Selecionados", priority = 11)]
        public static void MenuMerge() => MergeSelectedObjects(0.35f, true);

        [MenuItem("Estomago/Construir Forma/Bloco ou Chão de Carne", priority = 20)]
        public static void MenuSpawnBlock() => SpawnProBuilderShape(ShapeType.Cube, "Flesh_Block", new Vector3(4f, 1f, 4f));

        [MenuItem("Estomago/Construir Forma/Rampa ou Escada Orgânica", priority = 21)]
        public static void MenuSpawnStair() => SpawnProBuilderShape(ShapeType.Stair, "Flesh_Stair", new Vector3(3f, 2f, 5f));

        [MenuItem("Estomago/Construir Forma/Arco ou Túnel Digestivo", priority = 22)]
        public static void MenuSpawnArch() => SpawnProBuilderShape(ShapeType.Arch, "Flesh_Arch", new Vector3(5f, 4f, 6f));

        [MenuItem("Estomago/Construir Forma/Pilar Muscular", priority = 23)]
        public static void MenuSpawnPillar() => SpawnProBuilderShape(ShapeType.Cylinder, "Flesh_Pillar", new Vector3(2f, 6f, 2f));
    }
}
