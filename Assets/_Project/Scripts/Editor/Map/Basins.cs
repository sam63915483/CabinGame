using System.Threading.Tasks;
using Cabin.Map;
using UnityEngine;

namespace Cabin.Editor.Map
{
    /// <summary>
    /// Lake basins. Nova Scotia's lakes are glacial scours in gentle lowland, not river lakes, so they are scooped out
    /// after the rivers have cut their valleys: blobs of low-frequency warped noise, at two sizes (ponds and big
    /// lakes), lower the ground where they fall on gentle lowland, most of all in the lake country round the spawn.
    /// The drainage then floods each scoop to its own spill level, so the shoreline is wherever the land crosses that
    /// level. Nothing near the sea or on steep highland. Deterministic from the seed.
    /// </summary>
    public static class Basins
    {
        public static void Scoop(Grid h, BaseTerrain b, MapRecipe r, Vector2 mapCentre, Vector2 mapSize)
        {
            int w = h.W, hh = h.H;
            var warp = new Noise(r.seed + 601);
            var ponds = new Noise(r.seed + 602);
            var lakes = new Noise(r.seed + 603);
            var country = new Noise(r.seed + 604);
            var before = (float[])h.V.Clone();
            var slopeOf = new Grid(w, hh, h.Cell, h.Origin);
            for (int z = 0; z < hh; z++)
            for (int x = 0; x < w; x++)
                slopeOf[x, z] = h.Slope(x, z);
            slopeOf.Blur(2, 1);
            // The land's local floor at each blob size: a basin is dug below the lowest ground round it, not below the
            // bumps, so it holds one sheet of water across the whole blob rather than a pool at its low end.
            var pondFloor = Lowest(h, Mathf.Max(1, Mathf.RoundToInt(r.pondScale / h.Cell / 4f)));
            var lakeFloor = Lowest(h, Mathf.Max(1, Mathf.RoundToInt(r.lakeScale / h.Cell / 4f)));
            Parallel.For(0, hh, z =>
            {
                for (int x = 0; x < w; x++)
                {
                    int i = z * w + x;
                    float ground = before[i];
                    if (ground < 1.5f || b.Inland.V[i] < 20f) continue;
                    float wx = h.X(x), wz = h.Z(z);
                    // Where lakes belong: gentle lowland, more of it in the lake country.
                    float gentle = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(12f, 6f, slopeOf.V[i]));
                    float lowland = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.25f, 0.6f, b.Highland.V[i]));
                    float patchy = 0.5f + 0.5f * country.Fbm(wx / 2500f, wz / 2500f, 2);
                    var rel = new Vector2((wx - mapCentre.x) / mapSize.x, (wz - mapCentre.y) / mapSize.y);
                    float far = (rel - r.lakeCountryCentre).magnitude;
                    float bias = 1f - r.lakeCountryBias * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.15f, 0.45f, far));
                    float density = gentle * lowland * patchy * bias;
                    if (density <= 0.01f) continue;

                    float px = wx + 60f * warp.Fbm(wx / 500f, wz / 500f, 2);
                    float pz = wz + 60f * warp.Fbm(wx / 500f + 17.3f, wz / 500f + 5.1f, 2);
                    // Where lakes are rarer the blobs must be stronger to count; where one counts, it is dug in full.
                    float harder = (1f - density) * 0.12f;
                    float pond = Blob(ponds, px, pz, r.pondScale, r.pondThreshold + harder);
                    float lake = Blob(lakes, px, pz, r.lakeScale, r.lakeThreshold + harder);
                    float target = ground;
                    if (pond > 0f) target = Mathf.Min(target, Mathf.Lerp(ground, pondFloor.V[i], pond) - r.pondDepth * pond);
                    if (lake > 0f) target = Mathf.Min(target, Mathf.Lerp(ground, lakeFloor.V[i], lake) - r.lakeDepth * lake);
                    h.V[i] = Mathf.Max(1.5f, target);
                }
            });
        }

        /// <summary>The lowest ground within <paramref name="radius"/> cells of each cell (a square window, two passes).</summary>
        static Grid Lowest(Grid h, int radius)
        {
            int w = h.W, hh = h.H;
            var tmp = new Grid(w, hh, h.Cell, h.Origin);
            var result = new Grid(w, hh, h.Cell, h.Origin);
            Parallel.For(0, hh, z =>
            {
                for (int x = 0; x < w; x++)
                {
                    float m = float.MaxValue;
                    for (int d = -radius; d <= radius; d++) m = Mathf.Min(m, h.At(x + d, z));
                    tmp[x, z] = m;
                }
            });
            Parallel.For(0, hh, z =>
            {
                for (int x = 0; x < w; x++)
                {
                    float m = float.MaxValue;
                    for (int d = -radius; d <= radius; d++) m = Mathf.Min(m, tmp.At(x, z + d));
                    result[x, z] = m;
                }
            });
            return result;
        }

        /// <summary>0 outside a blob, rising smoothly to 1 well inside it.</summary>
        static float Blob(Noise noise, float x, float z, float scale, float threshold)
        {
            float f = noise.Fbm(x / scale, z / scale, 3) * 0.5f + 0.5f;
            float e = (f - threshold) / Mathf.Max(0.01f, 1f - threshold);
            return e <= 0f ? 0f : Mathf.SmoothStep(0f, 1f, Mathf.Min(1f, e * 6f));
        }
    }
}
