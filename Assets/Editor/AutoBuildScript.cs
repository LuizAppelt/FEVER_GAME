#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace StomachAbyss.EditorTools
{
    public static class AutoBuildScript
    {
        [MenuItem("HELLFLESH/Build Standalone Windows64")]
        public static void BuildStandaloneWindows64()
        {
            string[] scenes = new string[]
            {
                "Assets/Scenes/MainMenu.unity",
                "Assets/Scenes/Stomach_Abyss.unity"
            };

            string buildPath = "C:/Users/user/IdeaProjects/Game final/HELLFLESH_game/HELLFLESH.exe";

            BuildPlayerOptions buildPlayerOptions = new BuildPlayerOptions();
            buildPlayerOptions.scenes = scenes;
            buildPlayerOptions.locationPathName = buildPath;
            buildPlayerOptions.target = BuildTarget.StandaloneWindows64;
            buildPlayerOptions.options = BuildOptions.None;

            Debug.Log("[AutoBuildScript] Iniciando Clean Build para: " + buildPath);
            BuildReport report = BuildPipeline.BuildPlayer(buildPlayerOptions);
            BuildSummary summary = report.summary;

            if (summary.result == BuildResult.Succeeded)
            {
                Debug.Log($"[AutoBuildScript] BUILD CONCLUIDA COM SUCESSO! Tamanho: {summary.totalSize} bytes. Tempo: {summary.totalTime.TotalSeconds:F1}s");
            }
            else if (summary.result == BuildResult.Failed)
            {
                Debug.LogError($"[AutoBuildScript] BUILD FALHOU! Erros: {summary.totalErrors}");
            }
        }
    }
}
#endif
