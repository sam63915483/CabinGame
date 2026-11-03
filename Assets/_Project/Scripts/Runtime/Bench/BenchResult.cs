using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Cabin.Bench
{
    /// <summary>One measurement: where, facing which way, under which combo, and what the frame cost.</summary>
    [Serializable]
    public sealed class BenchResult
    {
        public string stop, combo;
        public float yaw;
        public float fps, low1Fps, mainMs, renderMs, gpuMs;
        public int grassTufts, grassDraws, farBatches, terrainTrees;
        public string shot;
        /// <summary>Main-thread profiler markers, "name=ms;…" (development builds).</summary>
        public string cpu;

        public const string Header = "stop,yaw,combo,fps,low1,main_ms,render_ms,gpu_ms,grass_tufts,grass_draws,far_batches,terrain_trees,shot,cpu";

        public string Csv() => string.Join(",", stop, yaw.ToString("F0", CultureInfo.InvariantCulture), combo,
            fps.ToString("F1", CultureInfo.InvariantCulture), low1Fps.ToString("F1", CultureInfo.InvariantCulture),
            mainMs.ToString("F2", CultureInfo.InvariantCulture), renderMs.ToString("F2", CultureInfo.InvariantCulture), gpuMs.ToString("F2", CultureInfo.InvariantCulture),
            grassTufts, grassDraws, farBatches, terrainTrees, shot, cpu);

        /// <summary>Average fps and the fps of the slowest 1 % of frames, from the frame times of a sample.</summary>
        public static void Summarise(List<float> frameTimes, out float fps, out float low1Fps)
        {
            if (frameTimes == null || frameTimes.Count == 0) { fps = 0f; low1Fps = 0f; return; }
            float sum = 0f;
            foreach (float t in frameTimes) sum += t;
            fps = frameTimes.Count / Mathf.Max(1e-6f, sum);
            var sorted = new List<float>(frameTimes);
            sorted.Sort();
            int worst = Mathf.Max(1, Mathf.CeilToInt(sorted.Count * 0.01f));
            float worstSum = 0f;
            for (int i = 0; i < worst; i++) worstSum += sorted[sorted.Count - 1 - i];
            low1Fps = worst / Mathf.Max(1e-6f, worstSum);
        }
    }
}
