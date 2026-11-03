using System;

namespace Cabin.Bench
{
    /// <summary>What a bench run does: the screen size, how long to let streaming settle and how long to sample at each point, the stops and the settings combos. Read from the JSON file named on the command line.</summary>
    [Serializable]
    public sealed class BenchPlan
    {
        public int width = 1920, height = 1080;
        public float settleSeconds = 2.5f, sampleSeconds = 2f;
        /// <summary>Rendering at the first stop before anything is measured: the GPU's clocks take a while to come up.</summary>
        public float warmupSeconds = 20f;
        /// <summary>Screenshots of the first (default) combo at every stop and heading; <see cref="screenshotAll"/> for every combo too.</summary>
        public bool screenshots = true, screenshotAll;
        public BenchStop[] stops = Array.Empty<BenchStop>();
        public BenchCombo[] combos = { new BenchCombo() };
    }
}
