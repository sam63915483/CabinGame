using System.IO;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace Cabin.Editor
{
    /// <summary>
    /// Writes every test run's results to <c>Temp/cabin_test_results.txt</c> so tooling (Coplay, CLI)
    /// can read them, including PlayMode runs that survive a domain reload.
    /// </summary>
    [InitializeOnLoad]
    public sealed class TestResultLog : ICallbacks
    {
        public const string OutputPath = "Temp/cabin_test_results.txt";

        static TestResultLog()
        {
            ScriptableObject.CreateInstance<TestRunnerApi>().RegisterCallbacks(new TestResultLog());
        }

        /// <summary>
        /// Saves open scenes, then runs one test assembly. EditMode runs synchronously so the runner's
        /// save-scenes check happens before a caller (e.g. Coplay) can dirty the scene again and block
        /// on a "Scene(s) Have Been Modified" dialog.
        /// </summary>
        public static void Run(TestMode mode)
        {
            UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();
            var assembly = mode == TestMode.PlayMode ? "Cabin.Tests.PlayMode" : "Cabin.Tests.EditMode";
            var settings = new ExecutionSettings(new Filter { testMode = mode, assemblyNames = new[] { assembly } })
            {
                runSynchronously = mode == TestMode.EditMode,
            };
            ScriptableObject.CreateInstance<TestRunnerApi>().Execute(settings);
        }

        [MenuItem("Tools/Cabin/Tests/Run EditMode")]
        static void RunEditMode() => Run(TestMode.EditMode);

        [MenuItem("Tools/Cabin/Tests/Run PlayMode")]
        static void RunPlayMode() => Run(TestMode.PlayMode);

        public void RunStarted(ITestAdaptor testsToRun) => File.WriteAllText(OutputPath, $"RUN {testsToRun.Name}\n");

        public void TestStarted(ITestAdaptor test) { }

        public void TestFinished(ITestResultAdaptor result)
        {
            if (!result.HasChildren)
                File.AppendAllText(OutputPath, $"{result.TestStatus} {result.Test.FullName} {result.Message}\n");
        }

        public void RunFinished(ITestResultAdaptor result) =>
            File.AppendAllText(OutputPath, $"DONE pass={result.PassCount} fail={result.FailCount} skip={result.SkipCount}\n");
    }
}
