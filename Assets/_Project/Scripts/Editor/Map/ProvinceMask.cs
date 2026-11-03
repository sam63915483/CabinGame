using System.Collections.Generic;
using Cabin.Map;
using UnityEngine;

namespace Cabin.Editor.Map
{
    /// <summary>
    /// Only Nova Scotia is land: the elevation data also has New Brunswick, Prince Edward Island and bits of the
    /// Magdalens, which would run off the map's edges and hide the province's shape. Land is kept if it's joined to
    /// Nova Scotia (flooded out from a few towns, not across the New Brunswick border on the Chignecto isthmus) or is
    /// an island just off its coast; everything else goes under the sea.
    /// </summary>
    public static class ProvinceMask
    {
        /// <summary>Places that are certainly Nova Scotia, to flood out from (longitude, latitude).</summary>
        static readonly Vector2[] Seeds =
        {
            new Vector2(-63.57f, 44.65f), // Halifax
            new Vector2(-65.22f, 44.42f), // Kejimkujik
            new Vector2(-66.12f, 43.84f), // Yarmouth
            new Vector2(-63.28f, 45.36f), // Truro
            new Vector2(-61.99f, 45.62f), // Antigonish
            new Vector2(-60.75f, 46.10f), // Baddeck, Cape Breton
            new Vector2(-60.19f, 46.14f), // Sydney
            new Vector2(-60.90f, 46.70f), // Cape Breton Highlands
        };

        /// <summary>The New Brunswick border across the isthmus: Missaguash River mouth to Tidnish, run out into the water both ends.</summary>
        static readonly Vector2 BorderSouth = new Vector2(-64.36f, 45.79f), BorderNorth = new Vector2(-63.95f, 46.02f);

        /// <summary>Islands this close to the province (real km) are part of it.</summary>
        const float IslandKm = 8f;

        /// <summary>Sinks land that isn't Nova Scotia in <paramref name="real"/> (real elevations) to <paramref name="seaDepth"/>.</summary>
        public static int Apply(MapRecipe r, MapProjection proj, Grid real, float seaDepth)
        {
            int w = real.W, h = real.H, n = w * h;
            var land = new bool[n];
            for (int i = 0; i < n; i++) land[i] = real.V[i] > r.landAbove;

            // The border, a few cells thick, as a wall the flood can't cross.
            var wall = new bool[n];
            Vector2 a = Cell(real, proj.FromGeo(BorderSouth.x, BorderSouth.y)), b = Cell(real, proj.FromGeo(BorderNorth.x, BorderNorth.y));
            Vector2 dir = (b - a).normalized;
            a -= dir * 20f;
            b += dir * 20f;
            int steps = Mathf.CeilToInt((b - a).magnitude * 2f);
            for (int s = 0; s <= steps; s++)
            {
                Vector2 p = Vector2.Lerp(a, b, s / (float)steps);
                for (int dz = -2; dz <= 2; dz++)
                for (int dx = -2; dx <= 2; dx++)
                {
                    int x = Mathf.RoundToInt(p.x) + dx, z = Mathf.RoundToInt(p.y) + dz;
                    if (x >= 0 && z >= 0 && x < w && z < h) wall[z * w + x] = true;
                }
            }

            // Flood the mainland and Cape Breton from the seeds.
            var keep = new bool[n];
            var queue = new Queue<int>();
            foreach (var seed in Seeds)
            {
                var c = Cell(real, proj.FromGeo(seed.x, seed.y));
                int i = Index(w, h, Mathf.RoundToInt(c.x), Mathf.RoundToInt(c.y));
                if (i < 0 || !land[i] || keep[i]) continue;
                keep[i] = true;
                queue.Enqueue(i);
                Flood(w, h, queue, j => land[j] && !wall[j] && !keep[j], keep);
            }

            // Islands just offshore: any other piece of land within reach that doesn't touch the border wall.
            float reachCells = IslandKm * 1000f / r.realMetresPerMetre / real.Cell;
            var near = Near(w, h, keep, reachCells);
            var seen = new bool[n];
            var piece = new List<int>();
            for (int i = 0; i < n; i++)
            {
                if (!land[i] || keep[i] || seen[i]) continue;
                piece.Clear();
                bool close = false, border = false;
                seen[i] = true;
                queue.Enqueue(i);
                while (queue.Count > 0)
                {
                    int c = queue.Dequeue();
                    piece.Add(c);
                    close |= near[c];
                    int cx = c % w, cz = c / w;
                    for (int k = 0; k < 4; k++)
                    {
                        int j = Index(w, h, cx + (k == 0 ? 1 : k == 1 ? -1 : 0), cz + (k == 2 ? 1 : k == 3 ? -1 : 0));
                        if (j < 0 || seen[j]) continue;
                        if (wall[j]) border = true;
                        if (!land[j]) continue;
                        seen[j] = true;
                        queue.Enqueue(j);
                    }
                }
                if (close && !border) foreach (int c in piece) keep[c] = true;
            }

            int sunk = 0;
            for (int i = 0; i < n; i++)
            {
                if (!land[i] || keep[i]) continue;
                real.V[i] = Mathf.Min(real.V[i], seaDepth);
                sunk++;
            }
            return sunk;
        }

        static Vector2 Cell(Grid g, Vector2 game) => new Vector2((game.x - g.Origin.x) / g.Cell, (game.y - g.Origin.y) / g.Cell);

        static int Index(int w, int h, int x, int z) => x < 0 || z < 0 || x >= w || z >= h ? -1 : z * w + x;

        static void Flood(int w, int h, Queue<int> queue, System.Func<int, bool> open, bool[] mark)
        {
            while (queue.Count > 0)
            {
                int c = queue.Dequeue();
                int cx = c % w, cz = c / w;
                for (int k = 0; k < 4; k++)
                {
                    int j = Index(w, h, cx + (k == 0 ? 1 : k == 1 ? -1 : 0), cz + (k == 2 ? 1 : k == 3 ? -1 : 0));
                    if (j < 0 || !open(j)) continue;
                    mark[j] = true;
                    queue.Enqueue(j);
                }
            }
        }

        /// <summary>Cells within <paramref name="reach"/> cells of a marked one (chamfer distance, two passes).</summary>
        static bool[] Near(int w, int h, bool[] from, float reach)
        {
            var d = new float[w * h];
            for (int i = 0; i < d.Length; i++) d[i] = from[i] ? 0f : 1e9f;
            const float diag = 1.41421f;
            for (int z = 0; z < h; z++)
            for (int x = 0; x < w; x++)
            {
                int i = z * w + x;
                if (x > 0) d[i] = Mathf.Min(d[i], d[i - 1] + 1f);
                if (z > 0) d[i] = Mathf.Min(d[i], d[i - w] + 1f);
                if (x > 0 && z > 0) d[i] = Mathf.Min(d[i], d[i - w - 1] + diag);
                if (x < w - 1 && z > 0) d[i] = Mathf.Min(d[i], d[i - w + 1] + diag);
            }
            for (int z = h - 1; z >= 0; z--)
            for (int x = w - 1; x >= 0; x--)
            {
                int i = z * w + x;
                if (x < w - 1) d[i] = Mathf.Min(d[i], d[i + 1] + 1f);
                if (z < h - 1) d[i] = Mathf.Min(d[i], d[i + w] + 1f);
                if (x < w - 1 && z < h - 1) d[i] = Mathf.Min(d[i], d[i + w + 1] + diag);
                if (x > 0 && z < h - 1) d[i] = Mathf.Min(d[i], d[i + w - 1] + diag);
            }
            var near = new bool[w * h];
            for (int i = 0; i < d.Length; i++) near[i] = d[i] <= reach;
            return near;
        }
    }
}
