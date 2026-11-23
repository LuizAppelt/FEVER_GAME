#pragma warning disable 0618
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace MapaEstomago.Editor
{
    public static class SpineViaductSetup
    {
        private const string TexturesFolder = "Assets/PilotChamber/Textures";
        private const string MaterialsFolder = "Assets/PilotChamber/Materials";
        private const string PrefabsFolder = "Assets/PilotChamber/Prefabs";
        private const string VertebraGlbPath = "Assets/PilotChamber/Spinal_Vertebra_Unit.glb";
        private const string VertebraPrefabPath = "Assets/PilotChamber/Prefabs/Spinal_Vertebra_Unit.prefab";
        private const string ViaductRootName = "Viaduto_Espinha_Dorsal";

        private static readonly string[] ViaductTextureFiles = new[]
        {
            "tex_vertebra_scorched_bone.png",
            "tex_spine_marrow_deck.png",
            "tex_infernal_iron_bone.png",
            "tex_hell_ember_lantern.png"
        };

        [MenuItem("Estomago/Viaduto da Espinha Dorsal/Construir Viaduto da Espinha Dorsal na Cena", priority = 20)]
        public static void SetupSpineViaductInScene()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ConfigureViaductTextures();
            Dictionary<string, Material> mats = CreateOrUpdateViaductMaterials();
            ConfigureGlbImporter();

            GameObject prefabAsset = CreateOrUpdateVertebraPrefab(mats);
            if (prefabAsset == null)
            {
                Debug.LogError("[SpineViaductSetup] Falha ao criar Prefab da Vertebra modular!");
                return;
            }

            GameObject viaductRoot = BuildViaductFromDuplicatedVertebrae(prefabAsset);

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveOpenScenes();

            Selection.activeGameObject = viaductRoot;
            Debug.Log("<color=#FF7722><b>[SpineViaductSetup]</b> Viaduto da Espinha Dorsal construido com sucesso duplicando e escalonando a vertebra modular!</color>");
        }

        private static void ConfigureViaductTextures()
        {
            foreach (string file in ViaductTextureFiles)
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
                    if (changed)
                    {
                        ti.SaveAndReimport();
                    }
                }
            }
        }

        private static void ConfigureGlbImporter()
        {
            AssetImporter importer = AssetImporter.GetAtPath(VertebraGlbPath);
            if (importer != null)
            {
                importer.SaveAndReimport();
            }
        }

        private static Dictionary<string, Material> CreateOrUpdateViaductMaterials()
        {
            Shader fleshShader = Shader.Find("MapaEstomago/URP/PS1_LivingFlesh");
            Shader triplanarShader = Shader.Find("MapaEstomago/URP/PS1_TriplanarFlesh");
            if (fleshShader == null) fleshShader = Shader.Find("Universal Render Pipeline/Lit");

            var dict = new Dictionary<string, Material>();

            // 1. Corpo Vertebral e Espinhos (Osso carbonizado e poroso)
            dict["Mat_Vertebra_ScorchedBone"] = CreateOrUpdateMat("Mat_Vertebra_ScorchedBone", fleshShader, "tex_vertebra_scorched_bone.png", m =>
            {
                m.SetColor("_BaseColor", new Color(1.0f, 0.96f, 0.90f, 1f));
                m.SetFloat("_VertexSnap", 200f);
                m.SetFloat("_BreathSpeed", 0.7f);
                m.SetFloat("_BreathAmp", 0.004f);
                m.SetFloat("_SplitAmount", 0.0f);
                m.SetFloat("_Wetness", 0.30f);
                m.SetFloat("_AmbientFloor", 0.44f);
                m.SetColor("_EmissionColor", new Color(0.05f, 0.02f, 0.01f, 1f));
                m.SetFloat("_UseVertexColor", 1f);
                m.SetFloat("_EnableBG3Cutout", 1f);
            });

            // 2. Pista Caminhavel Intervertebral (Medula + Lajes com frestas de brasa) - NUNCA CORTA O CHAO
            dict["Mat_Spine_MarrowDeck"] = CreateOrUpdateMat("Mat_Spine_MarrowDeck", fleshShader, "tex_spine_marrow_deck.png", m =>
            {
                m.SetColor("_BaseColor", Color.white);
                m.SetFloat("_VertexSnap", 220f);
                m.SetFloat("_BreathSpeed", 0.0f);
                m.SetFloat("_BreathAmp", 0.0f);
                m.SetFloat("_SplitAmount", 0.0f);
                m.SetFloat("_Wetness", 0.40f);
                m.SetFloat("_AmbientFloor", 0.50f);
                m.SetColor("_EmissionColor", new Color(0.24f, 0.06f, 0.015f, 1f));
                m.SetFloat("_UseVertexColor", 1f);
                m.SetFloat("_EnableBG3Cutout", 0f);
            });

            // 3. Arcos de Costelas Goticas Infernais - Cortam dinamicamente estilo BG3
            dict["Mat_Vertebra_RibArch"] = CreateOrUpdateMat("Mat_Vertebra_RibArch", fleshShader, "tex_vertebra_scorched_bone.png", m =>
            {
                m.SetColor("_BaseColor", new Color(0.98f, 0.92f, 0.85f, 1f));
                m.SetFloat("_VertexSnap", 200f);
                m.SetFloat("_BreathSpeed", 0.95f);
                m.SetFloat("_BreathAmp", 0.009f);
                m.SetFloat("_SplitAmount", 0.0f);
                m.SetFloat("_Wetness", 0.28f);
                m.SetFloat("_AmbientFloor", 0.46f);
                m.SetColor("_EmissionColor", new Color(0.07f, 0.02f, 0.01f, 1f));
                m.SetFloat("_UseVertexColor", 1f);
                m.SetFloat("_EnableBG3Cutout", 1f);
            });

            // 4. Ferragens Infernais (Grilhoes, Correntes, Suportes de Braseiros)
            dict["Mat_Infernal_IronBone"] = CreateOrUpdateMat("Mat_Infernal_IronBone", fleshShader, "tex_infernal_iron_bone.png", m =>
            {
                m.SetColor("_BaseColor", Color.white);
                m.SetFloat("_VertexSnap", 220f);
                m.SetFloat("_BreathSpeed", 0.0f);
                m.SetFloat("_BreathAmp", 0.0f);
                m.SetFloat("_SplitAmount", 0.0f);
                m.SetFloat("_Wetness", 0.48f);
                m.SetFloat("_AmbientFloor", 0.38f);
                m.SetColor("_EmissionColor", new Color(0.06f, 0.015f, 0.005f, 1f));
                m.SetFloat("_UseVertexColor", 1f);
                m.SetFloat("_EnableBG3Cutout", 1f);
            });

            // 5. Nucleos de Brasa & Fissuras Infernais (Emissivo quente)
            dict["Mat_Hell_EmberLantern"] = CreateOrUpdateMat("Mat_Hell_EmberLantern", fleshShader, "tex_hell_ember_lantern.png", m =>
            {
                m.SetColor("_BaseColor", Color.white);
                m.SetFloat("_VertexSnap", 180f);
                m.SetFloat("_BreathSpeed", 2.4f);
                m.SetFloat("_BreathAmp", 0.015f);
                m.SetFloat("_SplitAmount", 0.0f);
                m.SetFloat("_Wetness", 0.15f);
                m.SetFloat("_AmbientFloor", 0.85f);
                m.SetColor("_EmissionColor", new Color(1.85f, 0.55f, 0.08f, 1f));
                m.SetFloat("_UseVertexColor", 1f);
                m.SetFloat("_EnableBG3Cutout", 0f);
            });

            // Materiais Triplanares opcionais para uso em pincel/ProBuilder
            if (triplanarShader != null)
            {
                CreateOrUpdateMat("Mat_Triplanar_VertebraBone", triplanarShader, "tex_vertebra_scorched_bone.png", m =>
                {
                    m.SetFloat("_TextureScale", 2.0f);
                    m.SetFloat("_Wetness", 0.30f);
                    m.SetFloat("_OrganicBulge", 0.0f);
                    m.SetFloat("_BreathAmp", 0.0f);
                    m.SetFloat("_EnableBG3Cutout", 1f);
                });
                CreateOrUpdateMat("Mat_Triplanar_SpineMarrowDeck", triplanarShader, "tex_spine_marrow_deck.png", m =>
                {
                    m.SetFloat("_TextureScale", 2.2f);
                    m.SetFloat("_Wetness", 0.40f);
                    m.SetFloat("_OrganicBulge", 0.0f);
                    m.SetFloat("_BreathAmp", 0.0f);
                    m.SetFloat("_EnableBG3Cutout", 0f);
                });
            }

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
            if (tex != null)
            {
                mat.SetTexture("_BaseMap", tex);
            }

            configure?.Invoke(mat);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static GameObject CreateOrUpdateVertebraPrefab(Dictionary<string, Material> mats)
        {
            GameObject glbAsset = AssetDatabase.LoadAssetAtPath<GameObject>(VertebraGlbPath);
            if (glbAsset == null)
            {
                Debug.LogError($"[SpineViaductSetup] Nao foi possivel carregar {VertebraGlbPath}!");
                return null;
            }

            if (!AssetDatabase.IsValidFolder(PrefabsFolder))
            {
                AssetDatabase.CreateFolder("Assets/PilotChamber", "Prefabs");
            }

            GameObject tempInstance = (GameObject)PrefabUtility.InstantiatePrefab(glbAsset);
            tempInstance.name = "Spinal_Vertebra_Unit";

            // Desempacota o vinculo temporario ao GLB para gerar um Prefab standalone limpo e customizavel
            if (PrefabUtility.IsPartOfAnyPrefab(tempInstance))
            {
                PrefabUtility.UnpackPrefabInstance(tempInstance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            }

            var nodeToMat = new Dictionary<string, string>
            {
                { "Vertebra_Body_And_Spines", "Mat_Vertebra_ScorchedBone" },
                { "Vertebra_Walkway_Slab", "Mat_Spine_MarrowDeck" },
                { "Vertebra_Rib_Arches", "Mat_Vertebra_RibArch" },
                { "Vertebra_Infernal_Hardware", "Mat_Infernal_IronBone" },
                { "Vertebra_Ember_Core", "Mat_Hell_EmberLantern" }
            };

            foreach (MeshRenderer mr in tempInstance.GetComponentsInChildren<MeshRenderer>(true))
            {
                string nodeName = mr.gameObject.name;
                if (nodeToMat.TryGetValue(nodeName, out string matKey) && mats.TryGetValue(matKey, out Material mat) && mat != null)
                {
                    mr.sharedMaterial = mat;
                }

                // Colisor apenas no deck caminhavel e corpo/meio-fio da vertebra
                if (nodeName == "Vertebra_Walkway_Slab" || nodeName == "Vertebra_Body_And_Spines")
                {
                    MeshFilter mf = mr.GetComponent<MeshFilter>();
                    if (mf != null && mf.sharedMesh != null)
                    {
                        MeshCollider mc = mr.GetComponent<MeshCollider>();
                        if (mc == null) mc = mr.gameObject.AddComponent<MeshCollider>();
                        mc.sharedMesh = mf.sharedMesh;
                    }
                }
            }

            // Luz de Brasa embutida no Prefab (ativada estrategicamente nos segmentos-chave)
            GameObject emberLightGo = new GameObject("Vertebra_Ember_Light");
            emberLightGo.transform.SetParent(tempInstance.transform, false);
            emberLightGo.transform.localPosition = new Vector3(0f, 3.1f, 0f);
            Light emberLight = emberLightGo.AddComponent<Light>();
            emberLight.type = LightType.Point;
            emberLight.color = new Color(1.0f, 0.42f, 0.12f);
            emberLight.range = 17f;
            emberLight.intensity = 10f;
            emberLight.shadows = LightShadows.None;
            emberLight.enabled = false;

            GameObject savedPrefab = PrefabUtility.SaveAsPrefabAsset(tempInstance, VertebraPrefabPath);
            UnityEngine.Object.DestroyImmediate(tempInstance);
            return savedPrefab;
        }

        private static GameObject BuildViaductFromDuplicatedVertebrae(GameObject vertebraPrefab)
        {
            GameObject existing = GameObject.Find(ViaductRootName);
            if (existing != null)
            {
                Undo.DestroyObjectImmediate(existing);
            }

            GameObject root = new GameObject(ViaductRootName);
            Undo.RegisterCreatedObjectUndo(root, "Criar Viaduto da Espinha Dorsal");

            // Pontos de controle da curva S dramatica conectando a ponte de cartilagem ate a escadaria da Catedral
            Vector3 p0 = new Vector3(114.0f, 0.12f, 41.2f);
            Vector3 p1 = new Vector3(115.2f, 1.05f, 68.5f);
            Vector3 p2 = new Vector3(99.2f, 1.40f, 99.5f);
            Vector3 p3 = new Vector3(100.4f, 0.35f, 128.4f);

            // Tabela de comprimento de arco para distribuicao precisa
            const int sampleSteps = 600;
            Vector3[] samples = new Vector3[sampleSteps + 1];
            float[] arcLengths = new float[sampleSteps + 1];
            samples[0] = p0;
            arcLengths[0] = 0f;
            for (int i = 1; i <= sampleSteps; i++)
            {
                float t = i / (float)sampleSteps;
                samples[i] = EvalCubicBezier(p0, p1, p2, p3, t);
                arcLengths[i] = arcLengths[i - 1] + Vector3.Distance(samples[i - 1], samples[i]);
            }
            float totalLength = arcLengths[sampleSteps];

            // Comprimento util de 1 unidade = 4.4m (com abas de encaixe ate +-2.55m = 5.1m)
            // Usamos passo de 3.85m * scaleZ para manter sobreposicao solida sem frestas mesmo nas curvas!
            const float stepFactor = 3.85f;
            float cursorS = 1.8f; // Comeca com o primeiro segmento assentado sobre o cais de cartilagem
            int index = 0;

            var placed = new List<(Vector3 pos, Vector3 tan, float t, Vector3 scale)>();
            while (cursorS < totalLength - 0.8f)
            {
                float t = FindTForArcLength(arcLengths, cursorS);
                Vector3 pos = EvalCubicBezier(p0, p1, p2, p3, t);
                Vector3 tan = EvalCubicBezierTangent(p0, p1, p2, p3, t).normalized;
                Vector3 scale = ComputeVertebraScale(t, index);

                placed.Add((pos, tan, t, scale));

                float currentHalf = 0.5f * stepFactor * scale.z;
                float nextEstimateT = Mathf.Clamp01((cursorS + currentHalf * 1.9f) / totalLength);
                Vector3 nextScale = ComputeVertebraScale(nextEstimateT, index + 1);
                float nextHalf = 0.5f * stepFactor * nextScale.z;

                cursorS += (currentHalf + nextHalf) * 0.98f;
                index++;
                if (index > 45) break;
            }

            // Garante que a ultima vertebra monumental encaixe firmemente no degrau inferior da Catedral
            for (int i = 0; i < placed.Count; i++)
            {
                var item = placed[i];
                GameObject seg = (GameObject)PrefabUtility.InstantiatePrefab(vertebraPrefab, root.transform);
                string role = i == 0 ? "_Entrada" : (i == placed.Count - 1 ? "_PortalCatedral" : "");
                seg.name = $"Vertebra_{(i + 1):00}{role}_Escala_{item.scale.x:F2}";
                seg.transform.position = item.pos;

                // Mantem o deck lateralmente nivelado (Vector3.up) acompanhando suavemente a rampa (pitch)
                Quaternion lookRot = Quaternion.LookRotation(item.tan, Vector3.up);
                seg.transform.rotation = lookRot;
                seg.transform.localScale = item.scale;

                // Ativa luz de brasa quente nas entradas, no portal final e a cada 3 vertebras
                bool isLit = (i == 0) || (i == placed.Count - 1) || (i == placed.Count - 3) || (i % 3 == 0);
                Light pt = seg.GetComponentInChildren<Light>(true);
                if (pt != null)
                {
                    pt.enabled = isLit;
                    if (isLit)
                    {
                        pt.range = 16f * Mathf.Max(1f, item.scale.x);
                        pt.intensity = (i >= placed.Count - 2) ? 18f : 11f * item.scale.x;
                    }
                }
            }

            // Adiciona 2 pontos de luz verde-acida sob o arco central para reflexo dramatico Estomago + Inferno
            CreateUnderBridgeAcidLight(root.transform, "UnderBridge_AcidGlow_South", new Vector3(112f, -0.1f, 65f));
            CreateUnderBridgeAcidLight(root.transform, "UnderBridge_AcidGlow_North", new Vector3(103f, -0.1f, 102f));

            Debug.Log($"[SpineViaductSetup] Geradas {placed.Count} vertebras duplicadas com escala variavel sobre {totalLength:F1}m.");
            return root;
        }

        private static Vector3 ComputeVertebraScale(float t, int idx)
        {
            // Perfil anatomico + monumental de tamanho ao longo do viaduto:
            // - Entrada (t=0): ~1.10 (acolhe o jogador vindo da ponte de cartilagem)
            // - Miolo toracico/lombar (t=0.15..0.68): ondulacao organica entre ~0.82 e 1.08
            // - Aproximacao Sacral / Catedral (t=0.68..1.0): cresce grandiosamente ate ~1.68!
            float baseSize;
            if (t < 0.18f)
            {
                float k = t / 0.18f;
                baseSize = Mathf.Lerp(1.12f, 0.92f, Mathf.SmoothStep(0f, 1f, k));
            }
            else if (t < 0.68f)
            {
                float k = (t - 0.18f) / 0.50f;
                // Leve bojo lombar no centro + ritmo alternado organico entre vertebras
                float lumbarSwell = 0.14f * Mathf.Sin(k * Mathf.PI);
                float rhythmicPulse = 0.06f * Mathf.Sin(idx * 1.65f);
                baseSize = 0.88f + lumbarSwell + rhythmicPulse;
            }
            else
            {
                float k = (t - 0.68f) / 0.32f;
                baseSize = Mathf.Lerp(0.95f, 1.68f, k * k * (3f - 2f * k));
            }

            float widthScale = baseSize;
            float heightScale = baseSize * (1.0f + 0.15f * t); // Costelas finais ficam ainda mais imponentes
            float lengthScale = Mathf.Lerp(baseSize, 1.0f + 0.35f * (baseSize - 1f), 0.35f);

            return new Vector3(widthScale, heightScale, lengthScale);
        }

        private static void CreateUnderBridgeAcidLight(Transform parent, string name, Vector3 worldPos)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, true);
            go.transform.position = worldPos;
            Light l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = new Color(0.20f, 1.0f, 0.35f);
            l.range = 28f;
            l.intensity = 14f;
            l.shadows = LightShadows.None;
        }

        private static Vector3 EvalCubicBezier(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float u = 1f - t;
            float tt = t * t;
            float uu = u * u;
            return (uu * u) * p0 + (3f * uu * t) * p1 + (3f * u * tt) * p2 + (tt * t) * p3;
        }

        private static Vector3 EvalCubicBezierTangent(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float u = 1f - t;
            return 3f * (u * u) * (p1 - p0) + 6f * u * t * (p2 - p1) + 3f * (t * t) * (p3 - p2);
        }

        private static float FindTForArcLength(float[] arcLengths, float targetS)
        {
            int n = arcLengths.Length - 1;
            if (targetS <= 0f) return 0f;
            if (targetS >= arcLengths[n]) return 1f;

            int lo = 0, hi = n;
            while (lo < hi - 1)
            {
                int mid = (lo + hi) / 2;
                if (arcLengths[mid] < targetS) lo = mid;
                else hi = mid;
            }
            float segLen = Mathf.Max(1e-5f, arcLengths[hi] - arcLengths[lo]);
            float frac = (targetS - arcLengths[lo]) / segLen;
            return (lo + frac) / n;
        }
    }
}
