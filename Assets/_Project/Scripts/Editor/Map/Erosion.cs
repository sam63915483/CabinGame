using System;
using System.Threading.Tasks;
using UnityEngine;

namespace Cabin.Editor.Map
{
    /// <summary>
    /// Rivers cutting valleys. Stream-power incision in the implicit form of Braun &amp; Willett (2013): each
    /// iteration the ground is drained (<see cref="Drainage"/>) and every cell is lowered towards its receiver by an
    /// amount that grows with the water through it (√ of the catchment) and shrinks with the rock's hardness, taking
    /// the receiver's new height so the scheme is stable at any rate. Soft rock under a big river cuts fast and makes
    /// a valley; a hard band holds, and the river above it stalls at a step — a waterfall. Hillslope diffusion rounds
    /// soft hills and leaves hard caps sharp. A per-cell cap on the total cut keeps the broad shape. Sea and edge cells
    /// are fixed. Deterministic.
    /// </summary>
    public static class Erosion
    {
        public struct Settings
        {
            public int Iterations;
            /// <summary>Incision per iteration for a cell with one cell of catchment and softness 1.</summary>
            public float Rate;
            /// <summary>Hillslope diffusion per iteration (fraction of the 4-neighbour Laplacian).</summary>
            public float Diffusion;
            /// <summary>Land is never cut below this (metres).</summary>
            public float MinLand;
        }

        /// <summary>How soft a cell is: hard rock (0.95) is some ninety times slower to cut than soft ground (0.35), so a hard band holds as a step while the soft reach below it is cut away.</summary>
        public static float Softness(float hardness) => (1.02f - hardness) * (1.02f - hardness);

        /// <param name="h">The heights; eroded in place.</param>
        /// <param name="hardness">0–1 per cell.</param>
        /// <param name="fixedCell">Cells that never change and where water leaves (the sea, the map edge).</param>
        /// <param name="maxIncision">The most each cell may be lowered in total (metres).</param>
        public static void Run(Grid h, Grid hardness, Func<int, bool> fixedCell, Grid maxIncision, Settings s)
        {
            int w = h.W, hh = h.H, n = w * hh;
            float[] v = h.V, soft = new float[n], floor = new float[n];
            var isFixed = new bool[n];
            for (int i = 0; i < n; i++)
            {
                isFixed[i] = fixedCell(i);
                soft[i] = Softness(hardness.V[i]);
                floor[i] = Mathf.Max(s.MinLand, v[i] - maxIncision.V[i]);
            }
            var next = new float[n];
            for (int it = 0; it < s.Iterations; it++)
            {
                var d = Drainage.Fill(h, i => isFixed[i]);
                // Incision, outlets first so every receiver is already at its new height.
                for (int k = 0; k < n; k++)
                {
                    int c = d.Order[k];
                    if (isFixed[c]) continue;
                    int r = d.Receiver[c];
                    float kk = s.Rate * soft[c] * Mathf.Sqrt(d.Accumulation[c]) / d.Run(c);
                    v[c] = Mathf.Max(floor[c], (v[c] + kk * v[r]) / (1f + kk));
                }
                // Diffusion: soft ground slumps towards its neighbours' mean; hard ground barely.
                Parallel.For(0, hh, z =>
                {
                    for (int x = 0; x < w; x++)
                    {
                        int c = z * w + x;
                        if (isFixed[c]) { next[c] = v[c]; continue; }
                        float sum = v[Mathf.Max(0, x - 1) + z * w] + v[Mathf.Min(w - 1, x + 1) + z * w]
                                  + v[x + Mathf.Max(0, z - 1) * w] + v[x + Mathf.Min(hh - 1, z + 1) * w];
                        float lap = sum - 4f * v[c];
                        float moved = v[c] + s.Diffusion * soft[c] * lap;
                        next[c] = Mathf.Max(floor[c], moved);
                    }
                });
                Array.Copy(next, v, n);
            }
        }
    }
}
