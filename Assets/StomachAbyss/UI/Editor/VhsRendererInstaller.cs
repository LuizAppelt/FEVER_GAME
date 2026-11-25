using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace StomachAbyss.UI.Editor
{
    [InitializeOnLoad]
    public static class VhsRendererInstaller
    {
        private const string ShaderPath = "Assets/StomachAbyss/UI/Shaders/URP_VhsAnalog_Op2.shader";
        private const string MaterialPath = "Assets/StomachAbyss/UI/Materials/Mat_VHS_Analog_Op2.mat";
        private const string PcRendererPath = "Assets/Settings/PC_Renderer.asset";
        private const string MobileRendererPath = "Assets/Settings/Mobile_Renderer.asset";

        static VhsRendererInstaller()
        {
            EditorApplication.delayCall += EnsureInstalledSilent;
        }

        [MenuItem("StomachAbyss/UI/Install VHS Analog Filter (URP)")]
        public static void InstallFromMenu()
        {
            InstallInternal(logResult: true);
        }

        private static void EnsureInstalledSilent()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            InstallInternal(logResult: false);
        }

        private static void InstallInternal(bool logResult)
        {
            Shader shader = Shader.Find("StomachAbyss/URP_VhsAnalog_Op2");
            if (shader == null)
            {
                shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
            }
            if (shader == null) return;

            if (!AssetDatabase.IsValidFolder("Assets/StomachAbyss/UI/Materials"))
            {
                AssetDatabase.CreateFolder("Assets/StomachAbyss/UI", "Materials");
            }

            Material mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, MaterialPath);
            }
            else if (mat.shader != shader)
            {
                mat.shader = shader;
                EditorUtility.SetDirty(mat);
            }

            bool pcChanged = AttachFeatureToRenderer(PcRendererPath, mat);
            bool mobChanged = AttachFeatureToRenderer(MobileRendererPath, mat);

            if (pcChanged || mobChanged)
            {
                AssetDatabase.SaveAssets();
            }

            if (logResult || pcChanged || mobChanged)
            {
                Debug.Log("[VhsRendererInstaller] Filtro VHS Analogico (Opcao 2) instalado nos Renderers URP com sucesso!");
            }
        }

        private static bool AttachFeatureToRenderer(string rendererAssetPath, Material mat)
        {
            UniversalRendererData rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(rendererAssetPath);
            if (rendererData == null) return false;

            foreach (ScriptableRendererFeature existing in rendererData.rendererFeatures)
            {
                if (existing is VhsAnalogRendererFeature vhsExisting)
                {
                    if (vhsExisting.VhsMaterial != mat)
                    {
                        vhsExisting.VhsMaterial = mat;
                        EditorUtility.SetDirty(vhsExisting);
                        EditorUtility.SetDirty(rendererData);
                        return true;
                    }
                    return false;
                }
            }

            VhsAnalogRendererFeature feature = ScriptableObject.CreateInstance<VhsAnalogRendererFeature>();
            feature.name = "VhsAnalogRendererFeature";
            feature.VhsMaterial = mat;

            AssetDatabase.AddObjectToAsset(feature, rendererData);
            SerializedObject so = new SerializedObject(rendererData);
            SerializedProperty featuresProp = so.FindProperty("m_RendererFeatures");
            int idx = featuresProp.arraySize;
            featuresProp.InsertArrayElementAtIndex(idx);
            featuresProp.GetArrayElementAtIndex(idx).objectReferenceValue = feature;
            so.ApplyModifiedPropertiesWithoutUndo();

            rendererData.SetDirty();
            EditorUtility.SetDirty(rendererData);
            return true;
        }
    }
}
