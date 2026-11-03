using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Unity.Profiling;

namespace Cabin.Bench
{
    /// <summary>
    /// Where the main thread's time goes, from the engine's own profiler markers (a development build has them
    /// all; a release build only a few). Read each frame of a sample and averaged, so the bench can say "culling
    /// 1.8 ms, scripts 1.1 ms, waiting for the GPU 3 ms" instead of just "main 6 ms".
    /// </summary>
    public sealed class BenchCpu
    {
        static readonly (ProfilerCategory category, string name)[] Markers =
        {
            (ProfilerCategory.Render, "Camera.Render"),
            (ProfilerCategory.Render, "Gfx.WaitForPresentOnGfxThread"),
            (ProfilerCategory.Render, "Gfx.WaitForRenderThread"),
            (ProfilerCategory.Render, "CullScriptable"),
            (ProfilerCategory.Render, "Culling"),
            (ProfilerCategory.Render, "TerrainCulling"),
            (ProfilerCategory.Render, "Shadows.Draw"),
            (ProfilerCategory.Render, "RenderPipelineManager.DoRenderLoop"),
            (ProfilerCategory.Render, "UniversalRenderPipeline.RenderSingleCamera"),
            (ProfilerCategory.Render, "Terrain.Render"),
            (ProfilerCategory.Render, "Terrain.Details"),
            (ProfilerCategory.Render, "Terrain.Trees"),
            (ProfilerCategory.Render, "DrawMeshInstanced"),
            (ProfilerCategory.Render, "GPUResidentDrawer"),
            (ProfilerCategory.Scripts, "BehaviourUpdate"),
            (ProfilerCategory.Scripts, "LateBehaviourUpdate"),
            (ProfilerCategory.Scripts, "FixedBehaviourUpdate"),
            (ProfilerCategory.Scripts, "Update.ScriptRunBehaviourUpdate"),
            (ProfilerCategory.Scripts, "PreLateUpdate.ScriptRunBehaviourLateUpdate"),
            (ProfilerCategory.Physics, "Physics.Processing"),
            (ProfilerCategory.Physics, "FixedUpdate.PhysicsFixedUpdate"),
            (ProfilerCategory.Animation, "Animators.Update"),
            (ProfilerCategory.Animation, "Director.ProcessFrame"),
            (ProfilerCategory.Animation, "PreLateUpdate.DirectorUpdateAnimationEnd"),
            (ProfilerCategory.Gui, "GUI.Repaint"),
            (ProfilerCategory.Gui, "PlayerLoop.UpdateScreenManagerAndInput"),
            (ProfilerCategory.Audio, "Audio.Update"),
            (ProfilerCategory.Input, "InputUpdate"),
            (ProfilerCategory.Internal, "PlayerLoop"),
            (ProfilerCategory.Internal, "WaitForTargetFPS"),
            (ProfilerCategory.Memory, "GC.Collect"),
        };

        readonly List<(string name, ProfilerRecorder recorder, double sum)> live = new List<(string, ProfilerRecorder, double)>();
        int frames;

        public void Start()
        {
            foreach (var (category, name) in Markers)
            {
                var r = ProfilerRecorder.StartNew(category, name);
                if (r.Valid) live.Add((name, r, 0.0));
                else r.Dispose();
            }
        }

        public void Begin()
        {
            frames = 0;
            for (int i = 0; i < live.Count; i++) live[i] = (live[i].name, live[i].recorder, 0.0);
        }

        /// <summary>Once a frame during a sample.</summary>
        public void Frame()
        {
            frames++;
            for (int i = 0; i < live.Count; i++)
            {
                var (name, r, sum) = live[i];
                if (r.Valid && r.Count > 0) live[i] = (name, r, sum + r.LastValue / 1e6);
            }
        }

        /// <summary>"name=ms;name=ms" for every marker that recorded anything, biggest first.</summary>
        public string Summary()
        {
            if (frames == 0) return "";
            var rows = new List<(string name, double ms)>();
            foreach (var (name, _, sum) in live) if (sum > 0.0) rows.Add((name, sum / frames));
            rows.Sort((a, b) => b.ms.CompareTo(a.ms));
            var sb = new StringBuilder();
            foreach (var (name, ms) in rows)
            {
                if (sb.Length > 0) sb.Append(';');
                sb.Append(name).Append('=').Append(ms.ToString("F2", CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }

        public void Dispose()
        {
            foreach (var (_, r, _) in live) r.Dispose();
            live.Clear();
        }
    }
}
