using System;
using System.Collections.Generic;
using UnityEngine;

namespace Cabin.Editor.Map
{
    /// <summary>
    /// The lakes, rivers and waterfalls the land itself makes. The coarse ground is drained (<see cref="Drainage"/>):
    /// every basin fills to its spill and that is a lake, at that level; hollows too small to be lakes are filled in
    /// so nothing holds water there. Where enough country drains through a cell there is a river; each is traced from
    /// its mouth (the sea, a lake, a bigger river) upstream along the biggest flow, with the drainage's surface as its
    /// water level — never rising downstream by construction — and its width from its catchment. The courses are
    /// smoothed into curves, and a steep drop in the surface is a waterfall. Nothing here is drawn from an outline:
    /// the water is where the ground says it is.
    /// </summary>
    public sealed class WaterNetwork
    {
        public struct Settings
        {
            /// <summary>Water this deep over the ground counts as a lake (metres).</summary>
            public float LakeMinDepth;
            /// <summary>Lakes smaller than this many coarse cells are filled in.</summary>
            public int LakeMinCells;
            /// <summary>Country (game km²) that must drain through a cell for a river to run there.</summary>
            public float RiverCatchmentKm2;
            /// <summary>A stretch steeper than this (rise per run) is falling, not running.</summary>
            public float FallSlope;
            /// <summary>A falling stretch dropping at least this much is a waterfall (metres); less is a rapid.</summary>
            public float FallMinHeight;

            public static Settings Default => new Settings { LakeMinDepth = 0.25f, LakeMinCells = 20, RiverCatchmentKm2 = 0.3f, FallSlope = 0.35f, FallMinHeight = 2.5f };
        }

        public readonly List<Lake> Lakes = new List<Lake>();
        public readonly List<River> Rivers = new List<River>();
        /// <summary>Per coarse cell: the index of the lake it is under, else −1.</summary>
        public readonly int[] LakeOf;
        /// <summary>Per coarse cell: whether a river runs through it.</summary>
        public readonly bool[] IsRiver;
        public readonly Drainage Drain;
        public readonly Grid Ground;

        static readonly int[] DX = { 1, -1, 0, 0 }, DZ = { 0, 0, 1, -1 };

        WaterNetwork(Grid ground, Drainage drain)
        {
            Ground = ground;
            Drain = drain;
            LakeOf = new int[ground.W * ground.H];
            Array.Fill(LakeOf, -1);
            IsRiver = new bool[ground.W * ground.H];
        }

        /// <summary>Lakes first, then rivers. Fills in hollows too small to be lakes (modifies <paramref name="ground"/>).</summary>
        public static WaterNetwork Build(Grid ground, Func<int, bool> sea, Settings s)
        {
            var d = Drainage.Fill(ground, sea);
            var net = new WaterNetwork(ground, d);
            net.FindLakes(sea, s);
            net.TraceRivers(sea, s);
            return net;
        }

        void FindLakes(Func<int, bool> sea, Settings s)
        {
            int w = Ground.W, h = Ground.H, n = w * h;
            float[] hgt = Ground.V, filled = Drain.Filled;
            var seen = new bool[n];
            var queue = new Queue<int>();
            var cells = new List<int>();
            for (int i = 0; i < n; i++)
            {
                if (seen[i] || sea(i) || filled[i] - hgt[i] < s.LakeMinDepth) continue;
                cells.Clear();
                seen[i] = true;
                queue.Enqueue(i);
                float level = filled[i];
                while (queue.Count > 0)
                {
                    int c = queue.Dequeue();
                    cells.Add(c);
                    level = Mathf.Min(level, filled[c]);
                    int cx = c % w, cz = c / w;
                    for (int k = 0; k < 4; k++)
                    {
                        int nx = cx + DX[k], nz = cz + DZ[k];
                        if (nx < 0 || nz < 0 || nx >= w || nz >= h) continue;
                        int j = nz * w + nx;
                        if (seen[j] || sea(j) || filled[j] - hgt[j] < s.LakeMinDepth || Mathf.Abs(filled[j] - filled[i]) > 0.15f) continue;
                        seen[j] = true;
                        queue.Enqueue(j);
                    }
                }
                if (cells.Count < s.LakeMinCells)
                {
                    // A puddle: filled in, so the fine ground doesn't dip below any water level here.
                    foreach (int c in cells) hgt[c] = filled[c] + 0.1f;
                    continue;
                }
                var lake = new Lake { Id = Lakes.Count, Level = level };
                lake.Cells.AddRange(cells);
                foreach (int c in cells) LakeOf[c] = lake.Id;
                lake.WidthMetres = 2f * Mathf.Sqrt(cells.Count * Ground.Cell * Ground.Cell / Mathf.PI);
                int x0 = w, x1 = 0, z0 = h, z1 = 0;
                foreach (int c in cells)
                {
                    x0 = Mathf.Min(x0, c % w); x1 = Mathf.Max(x1, c % w);
                    z0 = Mathf.Min(z0, c / w); z1 = Mathf.Max(z1, c / w);
                }
                lake.LengthMetres = (Mathf.Max(x1 - x0, z1 - z0) + 1) * Ground.Cell;
                // Its outlet: follow the water out.
                int o = cells[0], steps = 0;
                while (o >= 0 && LakeOf[o] == lake.Id && steps++ < n) o = Drain.Receiver[o];
                lake.Outlet = o;
                Lakes.Add(lake);
            }
        }

        void TraceRivers(Func<int, bool> sea, Settings s)
        {
            int w = Ground.W, h = Ground.H, n = w * h;
            float cellKm2 = Ground.Cell * Ground.Cell / 1e6f;
            float[] hgt = Ground.V, filled = Drain.Filled, acc = Drain.Accumulation;
            int[] rcv = Drain.Receiver;
            for (int i = 0; i < n; i++)
                IsRiver[i] = !sea(i) && LakeOf[i] < 0 && hgt[i] >= 0.2f && acc[i] * cellKm2 >= s.RiverCatchmentKm2;
            // Who drains into each river cell.
            var donors = new List<int>[n];
            for (int i = 0; i < n; i++)
            {
                if (!IsRiver[i] || rcv[i] < 0) continue;
                donors[rcv[i]] ??= new List<int>();
                donors[rcv[i]].Add(i);
            }
            // Mouths: river cells whose water leaves the rivers (into the sea, a lake, or off the grid).
            var pending = new Stack<(int cell, int joins)>();
            for (int i = n - 1; i >= 0; i--)
                if (IsRiver[i] && (rcv[i] < 0 || !IsRiver[rcv[i]])) pending.Push((i, -1));
            var course = new List<int>();
            while (pending.Count > 0)
            {
                var (mouth, joins) = pending.Pop();
                course.Clear();
                int c = mouth;
                int river = Rivers.Count;
                while (true)
                {
                    course.Add(c);
                    var up = donors[c];
                    if (up == null) break;
                    int main = up[0];
                    foreach (int u in up) if (acc[u] > acc[main]) main = u;
                    foreach (int u in up) if (u != main) pending.Push((u, river));
                    c = main;
                }
                var r = new River { JoinsRiver = joins, CatchmentKm2 = acc[mouth] * cellKm2 };
                // Head first. Out of a lake? Its head cell is fed by lake cells.
                int head = course[course.Count - 1];
                int hx = head % w, hz = head / w;
                for (int k = 0; k < 4 && r.StartsFromLake < 0; k++)
                {
                    int nx = hx + DX[k], nz = hz + DZ[k];
                    if (nx < 0 || nz < 0 || nx >= w || nz >= h) continue;
                    int j = nz * w + nx;
                    if (LakeOf[j] >= 0 && rcv[j] >= 0 && (rcv[j] == head || Lakes[LakeOf[j]].Outlet == head)) r.StartsFromLake = LakeOf[j];
                }
                if (r.StartsFromLake >= 0)
                {
                    // Begin a cell inside the lake so the ribbon meets the lake's water.
                    var lake = Lakes[r.StartsFromLake];
                    int inside = -1;
                    for (int k = 0; k < 4 && inside < 0; k++)
                    {
                        int nx = hx + DX[k], nz = hz + DZ[k];
                        if (nx >= 0 && nz >= 0 && nx < w && nz < h && LakeOf[nz * w + nx] == lake.Id) inside = nz * w + nx;
                    }
                    if (inside >= 0) Add(r, inside, lake.Level, acc[head] * cellKm2);
                }
                for (int i = course.Count - 1; i >= 0; i--) Add(r, course[i], filled[course[i]], acc[course[i]] * cellKm2);
                // Its end: a step into the sea, the lake or the river it joins.
                int next = rcv[mouth];
                if (next < 0 || sea(next))
                {
                    r.EndsAtSea = true;
                    if (next >= 0) Add(r, next, 0f, r.CatchmentKm2);
                }
                else if (LakeOf[next] >= 0)
                {
                    r.EndsInLake = LakeOf[next];
                    Add(r, next, Lakes[r.EndsInLake].Level, r.CatchmentKm2);
                }
                else Add(r, next, filled[next], r.CatchmentKm2);
                if (r.Points.Count < 2) continue;
                Smooth(r);
                FindFalls(r, s);
                Rivers.Add(r);
            }
        }

        void Add(River r, int cell, float level, float km2)
        {
            r.Points.Add(new Vector2(Ground.X(cell % Ground.W), Ground.Z(cell / Ground.W)));
            r.Levels.Add(level);
            float width = Mathf.Clamp(3f + 12f * Mathf.Sqrt(Mathf.Max(0f, km2)), 3f, 18f);
            r.Widths.Add(width);
            r.Depths.Add(0.6f + 0.12f * width);
        }

        /// <summary>Two rounds of Chaikin's corner cutting on the course and everything along it (linear, so the levels stay non-increasing); endpoints kept.</summary>
        static void Smooth(River r)
        {
            for (int round = 0; round < 2; round++)
            {
                int n = r.Points.Count;
                if (n < 3) return;
                var pts = new List<Vector2>(n * 2) { r.Points[0] };
                var lv = new List<float>(n * 2) { r.Levels[0] };
                var wd = new List<float>(n * 2) { r.Widths[0] };
                var dp = new List<float>(n * 2) { r.Depths[0] };
                for (int i = 0; i + 1 < n; i++)
                {
                    pts.Add(Vector2.Lerp(r.Points[i], r.Points[i + 1], 0.25f));
                    pts.Add(Vector2.Lerp(r.Points[i], r.Points[i + 1], 0.75f));
                    lv.Add(Mathf.Lerp(r.Levels[i], r.Levels[i + 1], 0.25f));
                    lv.Add(Mathf.Lerp(r.Levels[i], r.Levels[i + 1], 0.75f));
                    wd.Add(Mathf.Lerp(r.Widths[i], r.Widths[i + 1], 0.25f));
                    wd.Add(Mathf.Lerp(r.Widths[i], r.Widths[i + 1], 0.75f));
                    dp.Add(Mathf.Lerp(r.Depths[i], r.Depths[i + 1], 0.25f));
                    dp.Add(Mathf.Lerp(r.Depths[i], r.Depths[i + 1], 0.75f));
                }
                pts.Add(r.Points[n - 1]);
                lv.Add(r.Levels[n - 1]);
                wd.Add(r.Widths[n - 1]);
                dp.Add(r.Depths[n - 1]);
                r.Points.Clear(); r.Points.AddRange(pts);
                r.Levels.Clear(); r.Levels.AddRange(lv);
                r.Widths.Clear(); r.Widths.AddRange(wd);
                r.Depths.Clear(); r.Depths.AddRange(dp);
            }
            // Smoothing is linear and so keeps a falling sequence falling; this only guards against rounding.
            for (int i = r.Levels.Count - 2; i >= 0; i--) r.Levels[i] = Mathf.Max(r.Levels[i], r.Levels[i + 1]);
        }

        /// <summary>Runs of steep segments; those dropping enough are falls, from the first steep point to the last.</summary>
        static void FindFalls(River r, Settings s)
        {
            int n = r.Points.Count;
            int start = -1;
            for (int i = 0; i <= n - 1; i++)
            {
                bool steep = false;
                if (i < n - 1)
                {
                    float run = Mathf.Max(0.01f, (r.Points[i + 1] - r.Points[i]).magnitude);
                    steep = (r.Levels[i] - r.Levels[i + 1]) / run > s.FallSlope;
                }
                if (steep && start < 0) start = i;
                if (!steep && start >= 0)
                {
                    float drop = r.Levels[start] - r.Levels[i];
                    if (drop >= s.FallMinHeight)
                        r.Falls.Add(new Fall
                        {
                            Lip = r.Points[start], Base = r.Points[i], LipLevel = r.Levels[start], BaseLevel = r.Levels[i],
                            Width = r.Widths[start], LipIndex = start, BaseIndex = i,
                        });
                    start = -1;
                }
            }
        }

        /// <summary>
        /// The closest never-rising sequence to <paramref name="v"/> (least squares; pool-adjacent-violators), in place.
        /// </summary>
        public static void Downhill(List<float> v)
        {
            var means = new List<float>();
            var counts = new List<int>();
            foreach (float x in v)
            {
                means.Add(x);
                counts.Add(1);
                while (means.Count > 1 && means[means.Count - 1] > means[means.Count - 2])
                {
                    int a = means.Count - 2, b = means.Count - 1;
                    int n = counts[a] + counts[b];
                    means[a] = (means[a] * counts[a] + means[b] * counts[b]) / n;
                    counts[a] = n;
                    means.RemoveAt(b);
                    counts.RemoveAt(b);
                }
            }
            int k = 0;
            for (int block = 0; block < means.Count; block++)
                for (int j = 0; j < counts[block]; j++) v[k++] = means[block];
        }

        /// <summary>Lakes, by width band, and the rivers and falls: for the log.</summary>
        public string Summary()
        {
            int small = 0, mid = 0, big = 0, falls = 0;
            float km = 0f;
            foreach (var l in Lakes)
            {
                if (l.LengthMetres < 100f) small++;
                else if (l.LengthMetres < 300f) mid++;
                else big++;
            }
            foreach (var r in Rivers)
            {
                km += r.Length / 1000f;
                falls += r.Falls.Count;
            }
            return $"{Lakes.Count} lakes ({small} under 100 m long, {mid} 100–300 m, {big} over 300 m), {Rivers.Count} rivers ({km:F1} km), {falls} waterfalls";
        }
    }
}
