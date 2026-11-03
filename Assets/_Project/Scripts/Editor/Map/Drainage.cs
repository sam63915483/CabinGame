using System;
using UnityEngine;

namespace Cabin.Editor.Map
{
    /// <summary>
    /// Where the water goes on a height grid. The ground is priority-flooded from the outlets (the sea, the map edge):
    /// every pit fills to its spill point and every flat gets a hair of fall (<c>eps</c> per cell) so that each cell
    /// has somewhere lower to drain to. Each cell's receiver is its steepest lower neighbour on that filled surface,
    /// <see cref="Order"/> lists the cells so that every receiver comes before the cells that drain into it, and
    /// <see cref="Accumulation"/> counts the cells draining through each one (itself included). Serial and
    /// deterministic; the erosion and the water network both run on it.
    /// </summary>
    public sealed class Drainage
    {
        public readonly int W, H;
        public readonly float Cell;
        /// <summary>The flooded surface: never below the ground, falling to an outlet from everywhere.</summary>
        public readonly float[] Filled;
        /// <summary>The cell each cell drains to; −1 at an outlet.</summary>
        public readonly int[] Receiver;
        /// <summary>Every cell, outlets first, each receiver before the cells that drain into it.</summary>
        public readonly int[] Order;
        /// <summary>Cells draining through each cell, itself included.</summary>
        public readonly float[] Accumulation;

        static readonly int[] DX = { 1, -1, 0, 0, 1, 1, -1, -1 }, DZ = { 0, 0, 1, -1, 1, -1, 1, -1 };
        const float Diag = 1.41421356f;

        Drainage(int w, int h, float cell)
        {
            W = w;
            H = h;
            Cell = cell;
            Filled = new float[w * h];
            Receiver = new int[w * h];
            Order = new int[w * h];
            Accumulation = new float[w * h];
        }

        public bool IsOutlet(int i) => Receiver[i] < 0;

        /// <summary>Distance in cells from a cell to its receiver (1 or √2).</summary>
        public float Run(int i)
        {
            int r = Receiver[i];
            if (r < 0) return 1f;
            return i % W != r % W && i / W != r / W ? Diag : 1f;
        }

        /// <param name="ground">The heights.</param>
        /// <param name="outlet">True where water leaves the grid (the sea, the map edge). There must be at least one.</param>
        /// <param name="eps">The fall given to each cell of a flat so it drains (metres).</param>
        public static Drainage Fill(Grid ground, Func<int, bool> outlet, float eps = 1e-3f)
        {
            int w = ground.W, h = ground.H, n = w * h;
            var d = new Drainage(w, h, ground.Cell);
            var done = new bool[n];
            var heap = new MinHeap(n);
            float[] filled = d.Filled, hgt = ground.V;
            int[] rcv = d.Receiver, order = d.Order;
            int count = 0;
            for (int i = 0; i < n; i++)
            {
                if (!outlet(i)) continue;
                done[i] = true;
                filled[i] = hgt[i];
                rcv[i] = -1;
                heap.Push(hgt[i], i);
            }
            if (heap.Count == 0) throw new ArgumentException("Drainage needs at least one outlet cell");
            while (heap.Count > 0)
            {
                int c = heap.Pop();
                order[count++] = c;
                int cx = c % w, cz = c / w;
                for (int k = 0; k < 8; k++)
                {
                    int nx = cx + DX[k], nz = cz + DZ[k];
                    if (nx < 0 || nz < 0 || nx >= w || nz >= h) continue;
                    int j = nz * w + nx;
                    if (done[j]) continue;
                    done[j] = true;
                    filled[j] = Mathf.Max(hgt[j], filled[c] + eps);
                    rcv[j] = c;
                    heap.Push(filled[j], j);
                }
            }
            // Steepest descent on the filled surface: the flood parent is always lower, but a lower neighbour the other
            // way may be steeper, and water runs down the steepest way. Still before the cell in the order (lower filled
            // level, so popped earlier).
            for (int c = 0; c < n; c++)
            {
                if (rcv[c] < 0) continue;
                int cx = c % w, cz = c / w;
                int best = rcv[c];
                float bestSlope = (filled[c] - filled[best]) / (cx != best % w && cz != best / w ? Diag : 1f);
                for (int k = 0; k < 8; k++)
                {
                    int nx = cx + DX[k], nz = cz + DZ[k];
                    if (nx < 0 || nz < 0 || nx >= w || nz >= h) continue;
                    int j = nz * w + nx;
                    if (filled[j] >= filled[c]) continue;
                    float slope = (filled[c] - filled[j]) / (k >= 4 ? Diag : 1f);
                    if (slope > bestSlope)
                    {
                        bestSlope = slope;
                        best = j;
                    }
                }
                rcv[c] = best;
            }
            float[] acc = d.Accumulation;
            for (int i = 0; i < n; i++) acc[i] = 1f;
            for (int k = count - 1; k >= 0; k--)
            {
                int c = order[k];
                if (rcv[c] >= 0) acc[rcv[c]] += acc[c];
            }
            return d;
        }
    }
}
