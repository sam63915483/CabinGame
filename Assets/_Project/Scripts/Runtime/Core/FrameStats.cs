using UnityEngine;

namespace Cabin.Core
{
    /// <summary>
    /// Where the frame goes, for the F3 readout: the main thread, the render thread and the GPU in milliseconds
    /// (smoothed; from Unity's frame timing stats, on in the player settings), so a slow build says which of the
    /// three it is waiting on instead of just a frame rate. Sampled by whoever shows it, once a frame.
    /// </summary>
    public static class FrameStats
    {
        public static float MainMs, RenderMs, GpuMs;
        static readonly FrameTiming[] timings = new FrameTiming[1];
        static int sampledFrame = -1;

        static bool captured;

        /// <summary>The latest frame's timings, unsmoothed; false when the stats aren't available yet.</summary>
        public static bool Raw(out float mainMs, out float renderMs, out float gpuMs)
        {
            if (sampledFrame != Time.frameCount)
            {
                sampledFrame = Time.frameCount;
                FrameTimingManager.CaptureFrameTimings();
                captured = FrameTimingManager.GetLatestTimings(1, timings) > 0;
            }
            mainMs = captured ? (float)timings[0].cpuMainThreadFrameTime : 0f;
            renderMs = captured ? (float)timings[0].cpuRenderThreadFrameTime : 0f;
            gpuMs = captured ? (float)timings[0].gpuFrameTime : 0f;
            return captured;
        }

        /// <summary>The latest timings, smoothed. Safe to call every frame from more than one place.</summary>
        public static void Sample()
        {
            if (!Raw(out float m, out float r, out float g)) return;
            const float k = 0.08f;
            MainMs = Mathf.Lerp(MainMs, m, k);
            RenderMs = Mathf.Lerp(RenderMs, r, k);
            GpuMs = Mathf.Lerp(GpuMs, g, k);
        }

        /// <summary>One line: which of the three the frame waits on is the biggest number.</summary>
        public static string Line() =>
            $"main {MainMs:F1} · render {RenderMs:F1} · gpu {GpuMs:F1} ms · grass {Forestry.GrassField.DrawnTufts / 1000f:F0}k tufts / {Forestry.GrassField.DrawnCalls} draws · far forest {Forestry.FarForest.DrawnBatches} batches";

        /// <summary>Which GPU and graphics API the game is actually running on: on a laptop with two GPUs, the first thing to check.</summary>
        public static string Device() => $"{SystemInfo.graphicsDeviceName} · {SystemInfo.graphicsDeviceType} · {Screen.width}x{Screen.height} · vsync {QualitySettings.vSyncCount} · cap {Application.targetFrameRate}";
    }
}
