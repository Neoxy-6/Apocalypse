using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Apocalypse.EditorTools
{
    public static class BuildScript
    {
        const string ScenePath = "Assets/Apocalypse/Main.unity";

        const string LitPath = "Assets/Apocalypse/Resources/ApocalypseLit.mat";

        public static void EnsureLitMaterial()
        {
            if (File.Exists(LitPath)) return;
            Directory.CreateDirectory(Path.GetDirectoryName(LitPath));
            var mat = new Material(Shader.Find("Standard"));
            mat.SetFloat("_Glossiness", 0.15f);
            AssetDatabase.CreateAsset(mat, LitPath);
            AssetDatabase.SaveAssets();
        }

        [MenuItem("Apocalypse/建立主場景")]
        public static void EnsureScene()
        {
            EnsureLitMaterial();
            if (!File.Exists(ScenePath))
            {
                var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
                EditorSceneManager.SaveScene(scene, ScenePath);
            }
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }

        [MenuItem("Apocalypse/建置 Windows 版")]
        // 預設輸出到 Build/；加 -buildOut <資料夾> 可以輸出到別處（遊戲開著時也能測試建置）
        static string OutPath()
        {
            var args = System.Environment.GetCommandLineArgs();
            int i = System.Array.IndexOf(args, "-buildOut");
            string dir = i >= 0 && i + 1 < args.Length ? args[i + 1] : Path.Combine(Application.dataPath, "..", "Build");
            return Path.GetFullPath(Path.Combine(dir, "Apocalypse.exe"));
        }

        public static void BuildWindows()
        {
            EnsureScene();
            PlayerSettings.runInBackground = true;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1920;
            PlayerSettings.defaultScreenHeight = 1080;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.allowFullscreenSwitch = true;
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = OutPath(),
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            });
            Debug.Log("[Apocalypse] Build result: " + report.summary.result);
            if (Application.isBatchMode && report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded) EditorApplication.Exit(1);
        }
    }
}
