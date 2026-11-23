using MapaEstomago;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BossTenia.Editor
{
    [CustomEditor(typeof(TeniaArenaBounds))]
    public class TeniaArenaBoundsEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            EditorGUILayout.Space(8f);
            EditorGUILayout.HelpBox(
                "Se voce moveu ou copiou Stomach_Boss_Cathedral para outro lugar/cena, clique abaixo para alinhar o retangulo verde (nave), gatilho azul, 8 pilares e o spawn do boss.",
                MessageType.Info);

            GUI.backgroundColor = new Color(0.35f, 0.95f, 0.55f);
            if (GUILayout.Button("Alinhar Arena, Pilares e Boss na Catedral da Cena", GUILayout.Height(30f)))
            {
                AlignSceneCathedral((TeniaArenaBounds)target);
            }
            GUI.backgroundColor = Color.white;
        }

        [MenuItem("Tools/Boss Tenia/Alinhar Arena e Boss na Catedral Atual")]
        public static void AlignActiveSceneMenu()
        {
            TeniaArenaBounds bounds = Object.FindAnyObjectByType<TeniaArenaBounds>();
            if (bounds == null)
            {
                EditorUtility.DisplayDialog(
                    "Boss Tenia",
                    "Nenhum componente TeniaArenaBounds encontrado na cena ativa.",
                    "OK");
                return;
            }

            if (!AlignSceneCathedral(bounds))
            {
                EditorUtility.DisplayDialog(
                    "Boss Tenia",
                    "Nao foi encontrado um objeto 'Stomach_Boss_Cathedral' na cena ativa. Arraste a catedral para o campo 'Cathedral Anchor' do TeniaArenaBounds.",
                    "OK");
            }
        }

        public static bool AlignSceneCathedral(TeniaArenaBounds bounds)
        {
            if (bounds == null) return false;

            Undo.RecordObject(bounds, "Alinhar Arena a Catedral");
            bool aligned = bounds.SyncWithCathedralAnchor(true);
            if (!aligned) return false;
            if (bounds.blockAltarStairs)
            {
                bounds.EnsureAltarStairBarrier();
            }

            EditorUtility.SetDirty(bounds);

            TeniaBossAI ai = bounds.GetComponent<TeniaBossAI>() ?? Object.FindAnyObjectByType<TeniaBossAI>();
            if (ai != null)
            {
                Undo.RecordObject(ai, "Alinhar Spawn Dormant do Boss");
                Undo.RecordObject(ai.transform, "Mover Boss para Catedral");
                Vector3 spawn = bounds.GetDormantSpawnWorldPos();
                Vector3 face = -bounds.GetCathedralForwardXZ();
                ai.dormantSpawnPosition = spawn;
                ai.dormantFacingDirection = face;
                ai.transform.position = spawn;
                if (face.sqrMagnitude > 0.001f)
                {
                    ai.transform.rotation = Quaternion.LookRotation(face.normalized, Vector3.up);
                }
                EditorUtility.SetDirty(ai);
                EditorUtility.SetDirty(ai.transform);
            }

            IsometricCameraFollow isoCam = Object.FindAnyObjectByType<IsometricCameraFollow>();
            if (isoCam != null)
            {
                Undo.RecordObject(isoCam, "Sincronizar Zoom Area da Camera");
                isoCam.SyncCathedralZoomArea();
                EditorUtility.SetDirty(isoCam);
            }

            if (bounds.gameObject.scene.IsValid())
            {
                EditorSceneManager.MarkSceneDirty(bounds.gameObject.scene);
            }

            SceneView.RepaintAll();
            return true;
        }
    }
}
