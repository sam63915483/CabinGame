using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Cabin.Core
{
    /// <summary>
    /// Anti-aliasing and shadows as their own settings, laid over whichever quality tier's URP asset is active, so the
    /// cost of each can be measured on its own. A shadow level is a distance, a shadow-map size and a cascade count;
    /// Off is a distance of zero, which URP takes as no main-light shadows. -1 for either leaves the tier's own value.
    /// The tier assets are project files, so in the editor their original values are put back when play stops.
    /// </summary>
    public static class RenderTier
    {
        public struct ShadowLevel
        {
            public float Distance;
            public int Resolution, Cascades;
        }

        /// <summary>Off, Low, Medium, High: Low/Medium/High match the tier assets as generated.</summary>
        public static readonly ShadowLevel[] ShadowLevels =
        {
            new ShadowLevel { Distance = 0f, Resolution = 512, Cascades = 1 },
            new ShadowLevel { Distance = 50f, Resolution = 1024, Cascades = 1 },
            new ShadowLevel { Distance = 80f, Resolution = 2048, Cascades = 2 },
            new ShadowLevel { Distance = 120f, Resolution = 2048, Cascades = 2 },
        };

        struct Original
        {
            public int Msaa, Resolution, Cascades;
            public float Distance;
        }

        static readonly Dictionary<UniversalRenderPipelineAsset, Original> originals = new Dictionary<UniversalRenderPipelineAsset, Original>();
        static bool restoring;

        /// <summary>The MSAA sample count for an index into <see cref="GameSettings.MsaaLevels"/>; -1 is the tier's own.</summary>
        public static int MsaaSamples(int index, int tierSamples) =>
            index < 0 ? tierSamples : GameSettings.MsaaLevels[Mathf.Clamp(index, 0, GameSettings.MsaaLevels.Length - 1)];

        /// <summary>The shadow level nearest a shadow distance (so the menu can show the tier's own).</summary>
        public static int NearestShadowLevel(float distance)
        {
            if (distance <= 0f) return 0;
            int best = 1;
            for (int i = 1; i < ShadowLevels.Length; i++)
                if (Mathf.Abs(ShadowLevels[i].Distance - distance) < Mathf.Abs(ShadowLevels[best].Distance - distance)) best = i;
            return best;
        }

        public static void Apply(UniversalRenderPipelineAsset urp, int msaa, int shadows)
        {
            if (urp == null) return;
            if (!originals.ContainsKey(urp))
            {
                originals[urp] = new Original { Msaa = urp.msaaSampleCount, Distance = urp.shadowDistance, Resolution = urp.mainLightShadowmapResolution, Cascades = urp.shadowCascadeCount };
                if (!restoring)
                {
                    restoring = true;
                    Application.quitting += RestoreAll;
                }
            }
            var o = originals[urp];
            urp.msaaSampleCount = MsaaSamples(msaa, o.Msaa);
            if (shadows < 0)
            {
                urp.shadowDistance = o.Distance;
                urp.mainLightShadowmapResolution = o.Resolution;
                urp.shadowCascadeCount = o.Cascades;
            }
            else
            {
                var level = ShadowLevels[Mathf.Clamp(shadows, 0, ShadowLevels.Length - 1)];
                urp.shadowDistance = level.Distance;
                urp.mainLightShadowmapResolution = level.Resolution;
                urp.shadowCascadeCount = level.Cascades;
            }
        }

        /// <summary>The tier's own shadow level and MSAA, for the menu to show while a setting follows the tier.</summary>
        public static int TierShadowLevel(UniversalRenderPipelineAsset urp) =>
            urp == null ? 2 : NearestShadowLevel(originals.TryGetValue(urp, out var o) ? o.Distance : urp.shadowDistance);

        public static int TierMsaa(UniversalRenderPipelineAsset urp) =>
            urp == null ? 4 : originals.TryGetValue(urp, out var o) ? o.Msaa : urp.msaaSampleCount;

        static void RestoreAll()
        {
            foreach (var kv in originals)
            {
                if (kv.Key == null) continue;
                kv.Key.msaaSampleCount = kv.Value.Msaa;
                kv.Key.shadowDistance = kv.Value.Distance;
                kv.Key.mainLightShadowmapResolution = kv.Value.Resolution;
                kv.Key.shadowCascadeCount = kv.Value.Cascades;
            }
            originals.Clear();
            Application.quitting -= RestoreAll;
            restoring = false;
        }
    }
}
