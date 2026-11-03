using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Cabin.Editor.Bench
{
    /// <summary>
    /// Tools/Cabin/Bench/Build: saves everything open, then builds the Windows player the bench runs into
    /// Builds/Bench (ignored by git). The outcome goes to Temp/bench/build.txt so a script can poll for it.
    /// </summary>
    public static class BenchBuild
    {
        public const string ExePath = "Builds/Bench/CabinGame.exe", ReportPath = "Temp/bench/build.txt";

        [MenuItem("Tools/Cabin/Bench/Build", priority = 1)]
        public static void Build() => Build(false);

        /// <summary>A development build: the bench can then read every profiler marker (where the main thread's time goes).</summary>
        [MenuItem("Tools/Cabin/Bench/Build (development, CPU markers)", priority = 2)]
        public static void BuildDevelopment() => Build(true);

        public static void Build(bool development)
        {
            Directory.CreateDirectory("Temp/bench");
            if (File.Exists(ReportPath)) File.Delete(ReportPath);
            if (EditorApplication.isPlaying) { File.WriteAllText(ReportPath, "FAILED: in play mode"); return; }
            EditorSceneManager.SaveOpenScenes();
            AssetDatabase.SaveAssets();
            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            double t0 = EditorApplication.timeSinceStartup;
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = ExePath,
                target = BuildTarget.StandaloneWindows64,
                options = development ? BuildOptions.Development : BuildOptions.None,
            });
            var s = report.summary;
            string line = $"{s.result}: {(development ? "development, " : "")}{s.totalSize / 1048576} MB in {EditorApplication.timeSinceStartup - t0:F0} s, {s.totalErrors} errors, {s.totalWarnings} warnings → {ExePath}";
            File.WriteAllText(ReportPath, line);
            Debug.Log("[Bench] " + line);
        }
    }
}
