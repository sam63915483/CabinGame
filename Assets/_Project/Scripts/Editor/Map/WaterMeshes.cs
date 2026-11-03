using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Cabin.Editor.Map
{
    /// <summary>
    /// Water surfaces, for the Cabin/Water shader. Lakes for one map tile from the lake-level grid: a quad per wet
    /// cell, grown out under the banks so there's no gap at the shore. Rivers as ribbons along their smoothed courses,
    /// width and level from the river at each point, continuing down the face of every waterfall as a sheet. The sea as
    /// a grid over the map with a wide skirt. Every vertex carries its depth (UV2.x: water level less the ground), how
    /// much it is falling water (UV2.y: 1 on a waterfall's sheet, 0 elsewhere) and its flow (UV1: the way the surface
    /// runs, metres per second; nothing on still water). World-space vertices.
    /// </summary>
    public static class WaterMeshes
    {
        /// <summary>Flow speed (m/s) per unit of surface slope, and the most a river runs at.</summary>
        const float FlowPerSlope = 260f, MaxFlow = 1.4f;

        static readonly int[] NX = { 0, -1, 1, 0 }, NZ = { -1, 0, 0, 1 };

        /// <summary>
        /// The water spread out from its cells as it would lie: flooded outwards through every cell where the ground
        /// dips below it (that is water too), up to <paramref name="rings"/> cells, and one ring further under the dry
        /// shore round all of it, hidden by the land — so the waterline you see is where the ground rises out of the
        /// water, never the edge of a cell. It stops at a rising bank (no water behind it) and at a drop (where it would
        /// hang in the air). Each new cell takes the level of the water it came from.
        /// </summary>
        public static Grid Extend(Grid water, Grid ground, int rings)
        {
            int w = water.W, h = water.H;
            var result = new Grid(w, h, water.Cell, water.Origin, float.NaN);
            System.Array.Copy(water.V, result.V, water.V.Length);
            var steps = new int[w * h];
            var visible = new bool[w * h];
            var queue = new Queue<int>();
            for (int i = 0; i < w * h; i++) if (!float.IsNaN(water.V[i])) queue.Enqueue(i);
            float hc = water.Cell * 0.5f;
            while (queue.Count > 0)
            {
                int c = queue.Dequeue();
                float level = result.V[c];
                int cx = c % w, cz = c / w;
                for (int k = 0; k < 4; k++)
                {
                    int nx = cx + NX[k], nz = cz + NZ[k];
                    if (nx < 0 || nz < 0 || nx >= w || nz >= h) continue;
                    int n = nz * w + nx;
                    if (!float.IsNaN(result.V[n])) continue;
                    float x = water.X(nx), z = water.Z(nz);
                    float lowest = Mathf.Min(Mathf.Min(ground.Sample(x - hc, z - hc), ground.Sample(x + hc, z - hc)),
                        Mathf.Min(ground.Sample(x - hc, z + hc), ground.Sample(x + hc, z + hc)));
                    if (lowest < level - 2.5f) continue; // a drop: water would hang
                    result.V[n] = level;
                    steps[n] = steps[c] + 1;
                    visible[n] = lowest < level;
                    // Below the water somewhere in this cell: it is water, keep spreading (straight across only, so it
                    // can't slip diagonally through a bank). Dry all over: the hidden shore.
                    if (visible[n] && steps[n] < rings) queue.Enqueue(n);
                }
            }
            // Trim spread water whose edge would show (it ran out of reach, or met a drop, while still over ground below
            // the water): peeled back until every edge of the added water is water or land that rises above it (which
            // hides the edge). The real water stays.
            bool Covered(int x, int z, float level) => x < 0 || z < 0 || x >= w || z >= h || !float.IsNaN(result[x, z])
                || ground.Sample(water.X(x), water.Z(z)) > level;
            for (bool changed = true; changed;)
            {
                changed = false;
                for (int i = 0; i < w * h; i++)
                {
                    if (!visible[i] || float.IsNaN(result.V[i]) || !float.IsNaN(water.V[i])) continue;
                    int x = i % w, z = i / w;
                    float lv = result.V[i];
                    if (Covered(x - 1, z, lv) && Covered(x + 1, z, lv) && Covered(x, z - 1, lv) && Covered(x, z + 1, lv)) continue;
                    result.V[i] = float.NaN;
                    changed = true;
                }
            }
            return result;
        }

        /// <summary>The lakes on one tile's cells: a quad per wet cell, corners at the mean level of the wet cells round them.</summary>
        public static Mesh Build(Grid water, Grid ground, int cx0, int cz0, int cells, string name)
        {
            int w = water.W, h = water.H;
            bool Wet(int x, int z) => x >= 0 && z >= 0 && x < w && z < h && !float.IsNaN(water[x, z]);
            float Corner(int x, int z)
            {
                float sum = 0f;
                int n = 0;
                for (int dz = -1; dz <= 0; dz++)
                for (int dx = -1; dx <= 0; dx++)
                    if (Wet(x + dx, z + dz))
                    {
                        sum += water[x + dx, z + dz];
                        n++;
                    }
                return n > 0 ? sum / n : float.NaN;
            }
            var verts = new List<Vector3>();
            var depths = new List<Vector2>();
            var flows = new List<Vector2>();
            var tris = new List<int>();
            var index = new Dictionary<int, int>();
            int Vertex(int x, int z, float fallback)
            {
                int key = z * (w + 1) + x;
                if (index.TryGetValue(key, out int i)) return i;
                float y = Corner(x, z);
                if (float.IsNaN(y)) y = fallback;
                float wx = water.Origin.x + (x - 0.5f) * water.Cell, wz = water.Origin.y + (z - 0.5f) * water.Cell;
                verts.Add(new Vector3(wx, y, wz));
                depths.Add(new Vector2(Mathf.Max(0f, y - ground.Sample(wx, wz)), 0f));
                flows.Add(Vector2.zero);
                index[key] = verts.Count - 1;
                return verts.Count - 1;
            }
            for (int z = cz0; z < cz0 + cells && z < h; z++)
            for (int x = cx0; x < cx0 + cells && x < w; x++)
            {
                float level = water[x, z];
                if (float.IsNaN(level)) continue;
                int a = Vertex(x, z, level), b = Vertex(x + 1, z, level), c = Vertex(x + 1, z + 1, level), d = Vertex(x, z + 1, level);
                tris.AddRange(new[] { a, d, c, a, c, b });
            }
            if (tris.Count == 0) return null;
            return Finish(name, verts, depths, flows, tris);
        }

        /// <summary>
        /// The sea: a grid of <paramref name="cell"/>-metre squares over the map and <paramref name="margin"/> beyond,
        /// at sea level, with its depth from the ground; then a deep skirt out to the horizon.
        /// </summary>
        public static Mesh Sea(Grid ground, Rect map, float margin, float cell, float horizon)
        {
            var verts = new List<Vector3>();
            var depths = new List<Vector2>();
            var flows = new List<Vector2>();
            var tris = new List<int>();
            float x0 = map.xMin - margin, z0 = map.yMin - margin;
            int nx = Mathf.CeilToInt((map.width + margin * 2f) / cell), nz = Mathf.CeilToInt((map.height + margin * 2f) / cell);
            for (int j = 0; j <= nz; j++)
            for (int i = 0; i <= nx; i++)
            {
                float wx = x0 + i * cell, wz = z0 + j * cell;
                bool onMap = map.Contains(new Vector2(wx, wz));
                float depth = onMap ? Mathf.Max(0f, -ground.Sample(wx, wz)) : 30f;
                verts.Add(new Vector3(wx, 0f, wz));
                depths.Add(new Vector2(depth, 0f));
                flows.Add(Vector2.zero);
            }
            for (int j = 0; j < nz; j++)
            for (int i = 0; i < nx; i++)
            {
                int a = j * (nx + 1) + i, b = a + 1, c = a + nx + 2, d = a + nx + 1;
                tris.AddRange(new[] { a, d, c, a, c, b });
            }
            // Skirt: four quads from the grid's edge out to the horizon.
            float x1 = x0 + nx * cell, z1 = z0 + nz * cell;
            var inner = new[] { new Vector2(x0, z0), new Vector2(x0, z1), new Vector2(x1, z1), new Vector2(x1, z0) };
            var outer = new[] { new Vector2(x0 - horizon, z0 - horizon), new Vector2(x0 - horizon, z1 + horizon), new Vector2(x1 + horizon, z1 + horizon), new Vector2(x1 + horizon, z0 - horizon) };
            int start = verts.Count;
            foreach (var p in inner) { verts.Add(new Vector3(p.x, 0f, p.y)); depths.Add(new Vector2(30f, 0f)); flows.Add(Vector2.zero); }
            foreach (var p in outer) { verts.Add(new Vector3(p.x, 0f, p.y)); depths.Add(new Vector2(30f, 0f)); flows.Add(Vector2.zero); }
            for (int k = 0; k < 4; k++)
            {
                int i0 = start + k, i1 = start + (k + 1) % 4, o0 = start + 4 + k, o1 = start + 4 + (k + 1) % 4;
                tris.AddRange(new[] { o0, o1, i1, o0, i1, i0 });
            }
            return Finish("Sea", verts, depths, flows, tris);
        }

        /// <summary>How far a fall's sheet look fades in above the lip and out below the base (metres along the course).</summary>
        const float FallFadeIn = 3f, FallFadeOut = 6f;

        /// <summary>
        /// The rivers on one tile as ribbons along their courses: the water across the channel and a little under each
        /// bank, level with the river at each point, the flow along it (faster where it falls), deepest in the middle
        /// and shallow at the edges (where the foam line shows). Over a waterfall the ribbon runs on down the face as a
        /// falling sheet. Only the stretches inside <paramref name="tile"/> (grown a little) are built; null if none.
        /// </summary>
        public static Mesh Rivers(List<River> rivers, Grid ground, Rect tile, string name)
        {
            var verts = new List<Vector3>();
            var depths = new List<Vector2>();
            var flows = new List<Vector2>();
            var tris = new List<int>();
            var grown = new Rect(tile.xMin - 20f, tile.yMin - 20f, tile.width + 40f, tile.height + 40f);
            foreach (var river in rivers)
            {
                int n = river.Points.Count;
                // Over a fall the sheet hangs off the lip: the course's run across the drop is squeezed to a few
                // metres so the water drops near-vertically to the plunge pool rather than sliding down a ramp.
                var points = new List<Vector2>(river.Points);
                foreach (var fall in river.Falls)
                {
                    Vector2 lip = river.Points[fall.LipIndex], toBase = river.Points[fall.BaseIndex] - lip;
                    float run = toBase.magnitude;
                    float want = Mathf.Min(run, 1.5f + fall.Height * 0.12f);
                    if (run < 1e-3f) continue;
                    for (int i = fall.LipIndex + 1; i <= fall.BaseIndex; i++)
                        points[i] = lip + (river.Points[i] - lip) * (want / run);
                }
                if (n < 2) continue;
                // Distance along the course, for the falls' fades.
                var along = new float[n];
                for (int i = 1; i < n; i++) along[i] = along[i - 1] + (points[i] - points[i - 1]).magnitude;
                var fallness = new float[n];
                foreach (var fall in river.Falls)
                {
                    float a0 = along[fall.LipIndex], a1 = along[fall.BaseIndex];
                    for (int i = 0; i < n; i++)
                    {
                        float f = along[i] < a0 ? Mathf.InverseLerp(a0 - FallFadeIn, a0, along[i])
                            : along[i] > a1 ? Mathf.InverseLerp(a1 + FallFadeOut, a1, along[i]) : 1f;
                        fallness[i] = Mathf.Max(fallness[i], f);
                    }
                }
                // Its direction smoothed over a few points either side, so the ribbon's edges don't cross at bends.
                var dirs = new Vector2[n];
                for (int i = 0; i < n; i++)
                {
                    Vector2 a = points[Mathf.Max(0, i - 3)], b = points[Mathf.Min(n - 1, i + 3)];
                    dirs[i] = (b - a).sqrMagnitude > 1e-6f ? (b - a).normalized : (i > 0 ? dirs[i - 1] : Vector2.right);
                }
                int runStart = -1;
                Vector2 lastLeft = Vector2.zero, lastRight = Vector2.zero;
                for (int i = 0; i <= n; i++)
                {
                    bool inside = i < n && grown.Contains(points[i]);
                    if (!inside)
                    {
                        // Close the run: triangles between consecutive emitted cross-sections.
                        if (runStart >= 0)
                            for (int v = runStart; v + 3 < verts.Count; v += 3)
                                tris.AddRange(new[] { v, v + 1, v + 3, v + 1, v + 4, v + 3, v + 1, v + 2, v + 4, v + 2, v + 5, v + 4 });
                        runStart = -1;
                        continue;
                    }
                    if (runStart < 0) runStart = verts.Count;
                    Vector2 prev = points[Mathf.Max(0, i - 1)], next = points[Mathf.Min(n - 1, i + 1)];
                    float run = (next - prev).magnitude;
                    Vector2 dir = dirs[i];
                    var across = new Vector2(-dir.y, dir.x);
                    float fall = run > 1e-4f ? (river.Levels[Mathf.Max(0, i - 1)] - river.Levels[Mathf.Min(n - 1, i + 1)]) / run : 0f;
                    var flow = dir * Mathf.Clamp(0.25f + fall * FlowPerSlope, 0.25f, MaxFlow);
                    float y = river.Levels[i];
                    float half = river.Widths[i] * 0.5f + 0.6f;
                    // Each side only as far as there is bank: where the ground falls away beside the river the water
                    // stops there rather than reaching out over the drop.
                    float Side(Vector2 d)
                    {
                        for (float r = half; r > 1f; r -= 0.5f)
                            if (ground.Sample(points[i].x + d.x * r, points[i].y + d.y * r) > y - 1f) return r;
                        return 1f;
                    }
                    Vector2 left = points[i] - across * Side(-across), right = points[i] + across * Side(across);
                    // An edge never steps back behind where it was (on the inside of a tight bend).
                    if (verts.Count > runStart)
                    {
                        if (Vector2.Dot(left - lastLeft, dir) < 0f) left = lastLeft;
                        if (Vector2.Dot(right - lastRight, dir) < 0f) right = lastRight;
                    }
                    lastLeft = left;
                    lastRight = right;
                    for (int s = -1; s <= 1; s++)
                    {
                        var p = s < 0 ? left : s > 0 ? right : points[i];
                        verts.Add(new Vector3(p.x, y, p.y));
                        depths.Add(new Vector2(s == 0 ? river.Depths[i] : Mathf.Max(0f, y - ground.Sample(p.x, p.y)), fallness[i]));
                        flows.Add(flow);
                    }
                }
            }
            if (tris.Count == 0) return null;
            return Finish(name, verts, depths, flows, tris);
        }

        static Mesh Finish(string name, List<Vector3> verts, List<Vector2> depths, List<Vector2> flows, List<int> tris)
        {
            var mesh = new Mesh { name = name, indexFormat = verts.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(verts);
            mesh.SetUVs(2, depths);
            mesh.SetUVs(1, flows);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
