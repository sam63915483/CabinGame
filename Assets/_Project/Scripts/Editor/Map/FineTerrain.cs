using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Cabin.Map;
using UnityEngine;

namespace Cabin.Editor.Map
{
    /// <summary>
    /// The heightmap at walking scale from the coarse, eroded land and its water. The coarse ground is upsampled
    /// smoothly, then given its fine detail — rolling bumps, rough rock up high — except near water, where the detail
    /// fades out and from every lake's waterline the land rises at a definite beach angle: a beach above, shallows
    /// below, and a clean waterline between. Lake beds are deepened from the shore in. River channels are carved along
    /// the smoothed courses: a rounded bed under the water, a short bank up to the land; at a waterfall the bed drops
    /// to the base level at the lip so the face is as steep as the ground allows, with a plunge pool below. Also the
    /// water grids the painter, the plants and the spawn use: the level of the water in each fine cell (NaN dry), and
    /// the lakes alone.
    /// </summary>
    public sealed class FineTerrain
    {
        public readonly Grid Heights, Water, Lakes;
        /// <summary>Per fine cell: the lake it is under, else −1.</summary>
        public readonly int[] LakeOf;

        public struct Settings
        {
            public float DetailHeight, DetailScale, RockDetailHeight, RockDetailScale, RockFrom, RockFull;
            public float BeachSlope, BeachWidth, BankWidth, LakeDepthSlope, LakeMaxDepth;
            public int Seed;

            public static Settings From(MapRecipe r) => new Settings
            {
                DetailHeight = r.detailHeight, DetailScale = r.detailScale, RockDetailHeight = r.rockDetailHeight, RockDetailScale = r.rockDetailScale,
                RockFrom = r.rockFrom, RockFull = r.rockFull, BeachSlope = r.beachSlope, BeachWidth = r.beachWidth, BankWidth = r.bankWidth,
                LakeDepthSlope = r.lakeDepthSlope, LakeMaxDepth = r.lakeMaxDepth, Seed = r.seed,
            };
        }

        FineTerrain(Grid heights, Grid water, Grid lakes, int[] lakeOf)
        {
            Heights = heights;
            Water = water;
            Lakes = lakes;
            LakeOf = lakeOf;
        }

        /// <summary>The steepest a river's valley side may be (degrees): an incised river gets a sloping gorge wall, not a 2 m-stepped cliff.</summary>
        const float MaxBankAngle = 42f;
        /// <summary>How far out from a channel the valley sides are shaped (metres).</summary>
        const float ValleyReach = 28f;
        /// <summary>How far (metres) from water the detail is damped.</summary>
        const float NearWater = 40f;

        public static FineTerrain Build(Grid coarse, WaterNetwork net, Settings s, int w, int h, float cell)
        {
            var heights = new Grid(w, h, cell, coarse.Origin);
            var water = new Grid(w, h, cell, coarse.Origin, float.NaN);
            var lakesGrid = new Grid(w, h, cell, coarse.Origin, float.NaN);
            var lakeOf = new int[w * h];
            Array.Fill(lakeOf, -1);

            // 1. The coarse land, smoothly.
            Parallel.For(0, h, z =>
            {
                for (int x = 0; x < w; x++) heights[x, z] = coarse.SampleCubic(heights.X(x), heights.Z(z));
            });
            var smooth = (float[])heights.V.Clone();

            // 2. The water level near each coarse cell (lakes and rivers; lakes alone), spread a little way inland.
            var nearAny = LevelsNear(coarse, net, true);

            // 3. Detail, damped at the waterline.
            var noise = new Noise(s.Seed);
            var rocks = new Noise(s.Seed + 101);
            Parallel.For(0, h, z =>
            {
                for (int x = 0; x < w; x++)
                {
                    float g = heights[x, z];
                    if (g <= 0f) continue;
                    float wx = heights.X(x), wz = heights.Z(z);
                    float land = Mathf.Clamp01(g / 3f);
                    int cx = Mathf.Clamp(Mathf.RoundToInt((wx - coarse.Origin.x) / coarse.Cell), 0, coarse.W - 1);
                    int cz = Mathf.Clamp(Mathf.RoundToInt((wz - coarse.Origin.y) / coarse.Cell), 0, coarse.H - 1);
                    float near = nearAny[cx, cz];
                    float damp = float.IsNaN(near) ? 1f : Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.3f, 2.5f, Mathf.Abs(g - near)));
                    float detail = s.DetailHeight * noise.Fbm(wx / s.DetailScale, wz / s.DetailScale, 4);
                    float rocky = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(s.RockFrom, s.RockFull, g));
                    float ridges = s.RockDetailHeight * rocky * rocks.Ridged(wx / s.RockDetailScale, wz / s.RockDetailScale, 4);
                    g += land * damp * (detail + ridges);

                    heights[x, z] = g;
                }
            });

            // 4. Lakes: every fine cell under a lake's level that connects to the lake — within the lake's own coarse
            // cells and the ring round them, so the fill can't slip out through the spill and run downhill. Judged on
            // the smooth ground (before detail), so the shoreline is a smooth contour.
            int ratio = Mathf.Max(1, Mathf.RoundToInt(coarse.Cell / cell));
            var queue = new Queue<int>();
            bool Near(int fx, int fz, int lakeId)
            {
                int cx = fx / ratio, cz = fz / ratio;
                for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = Mathf.Clamp(cx + dx, 0, coarse.W - 1), nz = Mathf.Clamp(cz + dz, 0, coarse.H - 1);
                    if (net.LakeOf[nz * coarse.W + nx] == lakeId) return true;
                }
                return false;
            }
            foreach (var lake in net.Lakes)
            {
                foreach (int c in lake.Cells)
                {
                    int fx = Mathf.Min(w - 1, c % coarse.W * ratio), fz = Mathf.Min(h - 1, c / coarse.W * ratio);
                    int f = fz * w + fx;
                    if (lakeOf[f] >= 0 || smooth[f] >= lake.Level) continue;
                    lakeOf[f] = lake.Id;
                    queue.Enqueue(f);
                }
                while (queue.Count > 0)
                {
                    int c = queue.Dequeue();
                    int cx = c % w, cz = c / w;
                    for (int k = 0; k < 4; k++)
                    {
                        int nx = cx + (k == 0 ? 1 : k == 1 ? -1 : 0), nz = cz + (k == 2 ? 1 : k == 3 ? -1 : 0);
                        if (nx < 0 || nz < 0 || nx >= w || nz >= h) continue;
                        int j = nz * w + nx;
                        if (lakeOf[j] >= 0 || smooth[j] >= lake.Level || !Near(nx, nz, lake.Id)) continue;
                        lakeOf[j] = lake.Id;
                        queue.Enqueue(j);
                    }
                }
            }

            // 5. The shore. Where the water meets the land is a smooth curve between the cells, not the edge of a
            // cell: along every edge between a wet cell and a dry one the crossing is where the smooth ground passes
            // the level, and the distance to that curve spreads out from there on both sides, carrying the lake. The
            // ground then slopes evenly through the waterline — the bed falling away at the beach angle into the
            // shallows, the beach rising at it for a few metres — so the terrain crosses the water surface exactly on
            // that curve. (Raising whole cells outside the lake made a 2 m sawtooth of every shore.)
            var shoreDist = new float[w * h];
            var shoreLake = new int[w * h];
            Array.Fill(shoreDist, 1e9f);
            Array.Fill(shoreLake, -1);
            for (int z = 0; z < h; z++)
            for (int x = 0; x < w; x++)
            {
                int i = z * w + x;
                if (lakeOf[i] < 0) continue;
                float level = net.Lakes[lakeOf[i]].Level;
                float inside = Mathf.Max(0f, level - smooth[i]);
                for (int k = 0; k < 4; k++)
                {
                    int nx = x + (k == 0 ? 1 : k == 1 ? -1 : 0), nz = z + (k == 2 ? 1 : k == 3 ? -1 : 0);
                    if (nx < 0 || nz < 0 || nx >= w || nz >= h) continue;
                    int j = nz * w + nx;
                    if (lakeOf[j] == lakeOf[i]) continue;
                    float outside = Mathf.Max(0f, smooth[j] - level);
                    float t = Mathf.Clamp(inside / Mathf.Max(1e-4f, inside + outside), 0.02f, 0.98f);
                    if (t * cell < shoreDist[i]) { shoreDist[i] = t * cell; shoreLake[i] = lakeOf[i]; }
                    if (lakeOf[j] < 0 && (1f - t) * cell < shoreDist[j]) { shoreDist[j] = (1f - t) * cell; shoreLake[j] = lakeOf[i]; }
                }
            }
            Spread(w, h, cell, shoreDist, shoreLake);
            float shoreSlope = Mathf.Tan(s.BeachSlope * Mathf.Deg2Rad);
            float bedSlope = Mathf.Max(shoreSlope, s.LakeDepthSlope);
            var beachOf = new int[w * h];
            Array.Fill(beachOf, -1);
            Parallel.For(0, h, z =>
            {
                for (int x = 0; x < w; x++)
                {
                    int i = z * w + x;
                    if (lakeOf[i] >= 0)
                    {
                        float level = net.Lakes[lakeOf[i]].Level;
                        float depth = Mathf.Min(s.LakeMaxDepth, 0.02f + shoreDist[i] * bedSlope);
                        heights.V[i] = Mathf.Min(heights.V[i], level - depth);
                        water.V[i] = level;
                        lakesGrid.V[i] = level;
                    }
                    else if (shoreLake[i] >= 0 && shoreDist[i] <= s.BeachWidth)
                    {
                        float level = net.Lakes[shoreLake[i]].Level;
                        heights.V[i] = Mathf.Max(heights.V[i], level + shoreDist[i] * shoreSlope);
                        beachOf[i] = shoreLake[i];
                    }
                }
            });
            shoreDist = null;
            shoreLake = null;

            // 6. River channels along the smoothed courses.
            Channels(heights, water, lakeOf, beachOf, net, s);

            return new FineTerrain(heights, water, lakesGrid, lakeOf);
        }

        /// <summary>Distance (metres, 8-connected chamfer) from every cell to the nearest seed, carrying the seed's id with it.</summary>
        static void Spread(int w, int h, float cell, float[] d, int[] id)
        {
            float diag = cell * 1.41421f;
            void Try(int i, int n, float cost)
            {
                if (d[n] + cost >= d[i]) return;
                d[i] = d[n] + cost;
                id[i] = id[n];
            }
            for (int z = 0; z < h; z++)
            for (int x = 0; x < w; x++)
            {
                int i = z * w + x;
                if (x > 0) Try(i, i - 1, cell);
                if (z > 0) Try(i, i - w, cell);
                if (x > 0 && z > 0) Try(i, i - w - 1, diag);
                if (x < w - 1 && z > 0) Try(i, i - w + 1, diag);
            }
            for (int z = h - 1; z >= 0; z--)
            for (int x = w - 1; x >= 0; x--)
            {
                int i = z * w + x;
                if (x < w - 1) Try(i, i + 1, cell);
                if (z < h - 1) Try(i, i + w, cell);
                if (x < w - 1 && z < h - 1) Try(i, i + w + 1, diag);
                if (x > 0 && z < h - 1) Try(i, i + w - 1, diag);
            }
        }

        /// <summary>The level of the nearest water (lakes and rivers, or lakes only) within <see cref="NearWater"/> of each coarse cell; NaN elsewhere.</summary>
        static Grid LevelsNear(Grid coarse, WaterNetwork net, bool rivers)
        {
            int w = coarse.W, h = coarse.H;
            var g = new Grid(w, h, coarse.Cell, coarse.Origin, float.NaN);
            foreach (var lake in net.Lakes)
                foreach (int c in lake.Cells) g.V[c] = lake.Level;
            if (rivers)
                foreach (var r in net.Rivers)
                    for (int i = 0; i < r.Points.Count; i++)
                    {
                        int x = Mathf.RoundToInt((r.Points[i].x - coarse.Origin.x) / coarse.Cell), z = Mathf.RoundToInt((r.Points[i].y - coarse.Origin.y) / coarse.Cell);
                        if (x < 0 || z < 0 || x >= w || z >= h) continue;
                        int c = z * w + x;
                        g.V[c] = float.IsNaN(g.V[c]) ? r.Levels[i] : Mathf.Max(g.V[c], r.Levels[i]);
                    }
            int rings = Mathf.CeilToInt(NearWater / coarse.Cell);
            var next = new float[w * h];
            for (int ring = 0; ring < rings; ring++)
            {
                Array.Copy(g.V, next, next.Length);
                Parallel.For(0, h, z =>
                {
                    for (int x = 0; x < w; x++)
                    {
                        int i = z * w + x;
                        if (!float.IsNaN(g.V[i])) continue;
                        float best = float.NaN;
                        for (int dz = -1; dz <= 1; dz++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int nx = x + dx, nz = z + dz;
                            if (nx < 0 || nz < 0 || nx >= w || nz >= h) continue;
                            float v = g.V[nz * w + nx];
                            if (!float.IsNaN(v) && (float.IsNaN(best) || v > best)) best = v;
                        }
                        next[i] = best;
                    }
                });
                Array.Copy(next, g.V, next.Length);
            }
            return g;
        }

        /// <summary>
        /// Carves the rivers. Every fine cell near a river knows its nearest course segment and distance; then the bed,
        /// bank and water are set from that segment's level, width and depth there. At a fall the bed is cut to the
        /// base level from the lip on, with a plunge pool at the base.
        /// </summary>
        static void Channels(Grid heights, Grid water, int[] lakeOf, int[] beachOf, WaterNetwork net, Settings s)
        {
            int w = heights.W, h = heights.H, n = w * h;
            float cell = heights.Cell;
            var dist = new float[n];
            var seg = new int[n];
            Array.Fill(dist, float.PositiveInfinity);
            var segments = new List<(int river, int k)>();
            foreach (var r in net.Rivers)
            {
                int ri = net.Rivers.IndexOf(r);
                for (int k = 0; k + 1 < r.Points.Count; k++)
                {
                    int id = segments.Count;
                    segments.Add((ri, k));
                    Vector2 p = r.Points[k], q = r.Points[k + 1], pq = q - p;
                    float reach = Mathf.Max(r.Widths[k], r.Widths[k + 1]) * 0.5f + ValleyReach + cell;
                    int x0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(p.x, q.x) - reach - heights.Origin.x) / cell));
                    int x1 = Mathf.Min(w - 1, Mathf.CeilToInt((Mathf.Max(p.x, q.x) + reach - heights.Origin.x) / cell));
                    int z0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(p.y, q.y) - reach - heights.Origin.y) / cell));
                    int z1 = Mathf.Min(h - 1, Mathf.CeilToInt((Mathf.Max(p.y, q.y) + reach - heights.Origin.y) / cell));
                    float len2 = Mathf.Max(1e-6f, pq.sqrMagnitude);
                    for (int z = z0; z <= z1; z++)
                    for (int x = x0; x <= x1; x++)
                    {
                        var c = new Vector2(heights.X(x), heights.Z(z));
                        float t = Mathf.Clamp01(Vector2.Dot(c - p, pq) / len2);
                        float d = (p + pq * t - c).magnitude;
                        int i = z * w + x;
                        if (d < dist[i]) { dist[i] = d; seg[i] = id; }
                    }
                }
            }
            // Fall stretches per river: between a lip and its base the bed drops to the base level.
            Parallel.For(0, h, z =>
            {
                for (int x = 0; x < w; x++)
                {
                    int i = z * w + x;
                    if (float.IsPositiveInfinity(dist[i]) || lakeOf[i] >= 0) continue;
                    var (ri, k) = segments[seg[i]];
                    var r = net.Rivers[ri];
                    Vector2 p = r.Points[k], q = r.Points[k + 1], pq = q - p;
                    var c = new Vector2(heights.X(x), heights.Z(z));
                    float t = Mathf.Clamp01(Vector2.Dot(c - p, pq) / Mathf.Max(1e-6f, pq.sqrMagnitude));
                    float level = Mathf.Lerp(r.Levels[k], r.Levels[k + 1], t);
                    float half = Mathf.Lerp(r.Widths[k], r.Widths[k + 1], t) * 0.5f;
                    float depth = Mathf.Lerp(r.Depths[k], r.Depths[k + 1], t);
                    // Over a fall the bed and the banks drop to the base level from the lip on: the gorge below a fall is
                    // cut at the plunge pool's level, its walls rising from there.
                    float bedLevel = level;
                    foreach (var fall in r.Falls)
                    {
                        if (k < fall.LipIndex || k >= fall.BaseIndex) continue;
                        bedLevel = fall.BaseLevel;
                        float pool = (c - fall.Base).magnitude / Mathf.Max(1f, 1.5f * half);
                        if (pool < 1f) depth *= Mathf.Lerp(1.8f, 1f, pool);
                        break;
                    }
                    // Any point within a pool's reach downstream of a base deepens too.
                    float d = dist[i];
                    float ground = heights.V[i];
                    if (d < half)
                    {
                        float bed = bedLevel - depth * (1f - (d / half) * (d / half));
                        heights.V[i] = Mathf.Min(ground, bed);
                        if (float.IsNaN(water.V[i])) water.V[i] = level;
                    }
                    else
                    {
                        float tBank = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(half, half + s.BankWidth, d));
                        float profile = Mathf.Lerp(bedLevel + 0.15f, ground, tBank);
                        float result = Mathf.Min(ground, profile);
                        // The valley side never steeper than the bank angle: a deeply cut river opens into a gorge
                        // with sloping walls rather than a slot the heightmap can only draw as stairs.
                        if (beachOf[i] < 0) result = Mathf.Min(result, bedLevel + 0.2f + (d - half) * Mathf.Tan(MaxBankAngle * Mathf.Deg2Rad));
                        // And it holds the water in: rising from the channel edge at least at the beach angle.
                        if (d < half + s.BankWidth) result = Mathf.Max(result, bedLevel + 0.2f + (d - half) * Mathf.Tan(s.BeachSlope * Mathf.Deg2Rad));
                        heights.V[i] = result;
                        if (d < half + cell * 0.5f && float.IsNaN(water.V[i])) water.V[i] = level;
                    }
                }
            });
        }
    }
}
