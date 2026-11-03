using System.Collections;
using System.Collections.Generic;
using System.IO;
using Cabin.Core;
using Cabin.Forestry;
using Cabin.Player;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Cabin.Bench
{
    /// <summary>
    /// The automated playtest. Only when the player is launched with <c>-cabin-bench plan.json</c> (nothing in a
    /// normal launch): loads the world, then at every stop in the plan teleports the player, faces each heading,
    /// applies each settings combo, waits for the grass and trees to stream in, samples the frame for a while
    /// (fps, the slowest 1 %, main-thread / render-thread / GPU milliseconds, what the grass and far forest drew),
    /// takes a screenshot, and writes results.csv and results.json to <c>-cabin-bench-out dir</c> as it goes.
    /// Writes done.txt and quits at the end. Never saves the settings, so the player's own are untouched.
    /// </summary>
    public sealed class BenchRunner : MonoBehaviour
    {
        const string PlanArg = "-cabin-bench", OutArg = "-cabin-bench-out";

        BenchPlan plan;
        string outDir;
        readonly BenchReport report = new BenchReport();
        readonly List<float> frameTimes = new List<float>();
        readonly BenchCpu cpu = new BenchCpu();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var args = System.Environment.GetCommandLineArgs();
            string planPath = null, dir = null;
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == PlanArg) planPath = args[i + 1];
                if (args[i] == OutArg) dir = args[i + 1];
            }
            if (planPath == null) return;
            var go = new GameObject("Bench");
            DontDestroyOnLoad(go);
            var runner = go.AddComponent<BenchRunner>();
            runner.plan = JsonUtility.FromJson<BenchPlan>(File.ReadAllText(planPath));
            runner.outDir = dir ?? Path.Combine(Path.GetDirectoryName(planPath), "out");
            Directory.CreateDirectory(runner.outDir);
            Debug.Log($"[Bench] plan {planPath}: {runner.plan.stops.Length} stops, {runner.plan.combos.Length} combos → {runner.outDir}");
        }

        void Start() => StartCoroutine(Run());

        IEnumerator Run()
        {
            float t0 = Time.realtimeSinceStartup;
            while (!SceneManager.GetSceneByName(BootLoader.WorldSceneName).isLoaded) yield return null;
            FirstPersonController controller;
            while ((controller = FindFirstObjectByType<FirstPersonController>()) == null) yield return null;
            while (Camera.main == null) yield return null;
            yield return new WaitForSecondsRealtime(2f); // everyone's Start, and the first-frame stalls
            report.loadSeconds = Time.realtimeSinceStartup - t0;
            report.device = SystemInfo.graphicsDeviceName;
            report.api = SystemInfo.graphicsDeviceType.ToString();
            report.cpu = SystemInfo.processorType;
            report.started = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            report.power = $"{SystemInfo.batteryStatus} {(SystemInfo.batteryLevel < 0f ? "" : $"{SystemInfo.batteryLevel * 100f:F0} %")}".Trim();
            if (SystemInfo.batteryStatus == BatteryStatus.Discharging) Debug.LogWarning("[Bench] ON BATTERY: numbers will run slow; plug in for real ones");
            Screen.SetResolution(plan.width, plan.height, FullScreenMode.FullScreenWindow);
            yield return null;
            report.width = Screen.width;
            report.height = Screen.height;
            CursorCapture.Capture();
            var look = GameObject.Find("Look") is GameObject lookGo ? lookGo.GetComponent<Volume>() : null;
            var experiments = new BenchExperiments();
            experiments.Capture();
            cpu.Start();
            File.WriteAllText(Path.Combine(outDir, "results.csv"), BenchResult.Header + "\n");
            File.WriteAllText(Path.Combine(outDir, "results.json"), JsonUtility.ToJson(report, true));

            if (plan.stops.Length > 0 && plan.warmupSeconds > 0f)
            {
                var first = plan.stops[0];
                BenchCombo.Baseline();
                GameSettings.ApplyWorld(look, Camera.main, controller);
                controller.Teleport(Ground(first.x, first.z), first.yaws.Length > 0 ? first.yaws[0] : 0f, first.pitch);
                yield return Settle();
                yield return new WaitForSecondsRealtime(plan.warmupSeconds);
            }

            foreach (var stop in plan.stops)
            {
                var combos = stop.sweep ? plan.combos : new[] { plan.combos[0] };
                foreach (float yaw in stop.yaws)
                foreach (var combo in combos)
                {
                    BenchCombo.Baseline();
                    GrassField.Enabled = combo.ApplyTo();
                    GameSettings.ApplyWorld(look, Camera.main, controller);
                    experiments.Apply(combo);
                    controller.Teleport(Ground(stop.x, stop.z), yaw, stop.pitch);
                    yield return Settle();
                    var r = new BenchResult { stop = stop.name, combo = combo.name, yaw = yaw };
                    yield return Sample(r);
                    if (plan.screenshots && (plan.screenshotAll || combo == plan.combos[0]))
                    {
                        r.shot = $"{stop.name}_{yaw:F0}_{combo.name}.png";
                        ScreenCapture.CaptureScreenshot(Path.Combine(outDir, r.shot));
                        yield return null;
                        yield return null;
                    }
                    report.results.Add(r);
                    File.AppendAllText(Path.Combine(outDir, "results.csv"), r.Csv() + "\n");
                    File.WriteAllText(Path.Combine(outDir, "results.json"), JsonUtility.ToJson(report, true));
                    Debug.Log($"[Bench] {r.stop} {r.yaw:F0}° {r.combo}: {r.fps:F0} fps (1% {r.low1Fps:F0}) main {r.mainMs:F1} render {r.renderMs:F1} gpu {r.gpuMs:F1}");
                }
            }
            experiments.Restore();
            cpu.Dispose();
            File.WriteAllText(Path.Combine(outDir, "done.txt"), $"{report.results.Count} results");
            yield return null;
            Application.Quit();
        }

        /// <summary>Waits for the grass and near trees to finish streaming, then the plan's settle time.</summary>
        IEnumerator Settle()
        {
            float t0 = Time.realtimeSinceStartup;
            var grass = FindFirstObjectByType<GrassField>();
            var trees = FindFirstObjectByType<NearForest>();
            while (Time.realtimeSinceStartup - t0 < 15f && ((grass != null && grass.Pending > 0) || (trees != null && trees.Pending > 0))) yield return null;
            yield return new WaitForSecondsRealtime(plan.settleSeconds);
        }

        IEnumerator Sample(BenchResult r)
        {
            frameTimes.Clear();
            float main = 0f, render = 0f, gpu = 0f;
            int timed = 0;
            float t0 = Time.realtimeSinceStartup;
            yield return null;
            cpu.Begin();
            while (Time.realtimeSinceStartup - t0 < plan.sampleSeconds)
            {
                frameTimes.Add(Time.unscaledDeltaTime);
                cpu.Frame();
                if (FrameStats.Raw(out float m, out float rt, out float g))
                {
                    main += m;
                    render += rt;
                    gpu += g;
                    timed++;
                }
                yield return null;
            }
            BenchResult.Summarise(frameTimes, out r.fps, out r.low1Fps);
            r.cpu = cpu.Summary();
            if (timed > 0) { r.mainMs = main / timed; r.renderMs = render / timed; r.gpuMs = gpu / timed; }
            r.grassTufts = GrassField.DrawnTufts;
            r.grassDraws = GrassField.DrawnCalls;
            r.farBatches = FarForest.DrawnBatches;
            foreach (var t in Terrain.activeTerrains) r.terrainTrees += t.terrainData.treeInstanceCount;
        }

        static Vector3 Ground(float x, float z)
        {
            foreach (var t in Terrain.activeTerrains)
            {
                Vector3 o = t.transform.position, s = t.terrainData.size;
                if (x >= o.x && x <= o.x + s.x && z >= o.z && z <= o.z + s.z)
                    return new Vector3(x, t.SampleHeight(new Vector3(x, 0f, z)) + o.y + 0.3f, z);
            }
            return new Vector3(x, 50f, z);
        }
    }
}
