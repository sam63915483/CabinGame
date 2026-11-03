using System;
using System.Collections.Generic;

namespace Cabin.Bench
{
    /// <summary>A whole run: the machine it ran on and every result, as JSON beside the CSV.</summary>
    [Serializable]
    public sealed class BenchReport
    {
        public string device, api, cpu, started;
        /// <summary>Mains or battery, and the charge: a laptop on battery runs 20–30 % slower, so runs only compare like with like.</summary>
        public string power;
        public int width, height;
        public float loadSeconds;
        public List<BenchResult> results = new List<BenchResult>();
    }
}
