using System.Threading.Tasks;
using Cabin.Map;
using UnityEngine;

namespace Cabin.Editor.Map
{
    /// <summary>
    /// The land before the rivers get at it, on the coarse grid. Nova Scotia's real elevation, shrunk 68 times across,
    /// is too gentle to explore, so it only decides the coastline and the broad shape — the highlands of Cape Breton,
    /// the Cobequids, North and South Mountain stay high, the valleys low — and noise gives the shape at walking scale
    /// by region: rolling rises and low hills in the lowlands; bigger hills, ridged rocky mountains and terraced steps
    /// in the high country, with bluffs on rugged stretches of coast. Calm at the shore so beaches stay beaches. Also
    /// says how hard the rock is: soft lowland, and in the highlands a hard band capping every terrace step, which the
    /// erosion respects — that is where the cliffs and waterfalls come from. Deterministic from the recipe's seed.
    /// </summary>
    public sealed class BaseTerrain
    {
        /// <summary>Coarse heights (metres, sea level 0), hardness 0–1, how much high country (0–1), metres inland from the sea, metres out to sea.</summary>
        public readonly Grid Heights, Hardness, Highland, Inland, Offshore;

        BaseTerrain(Grid heights, Grid hardness, Grid highland, Grid inland, Grid offshore)
        {
            Heights = heights;
            Hardness = hardness;
            Highland = highland;
            Inland = inland;
            Offshore = offshore;
        }

        /// <summary>Game height (metres, sea level 0) for a real elevation: land lifted clear of the sea and exaggerated more the higher it is.</summary>
        public static float Curve(MapRecipe r, float real) => real > r.landAbove
            ? 0.8f + r.linearScale * (real - r.landAbove) + r.quadraticScale * (real - r.landAbove) * (real - r.landAbove)
            : Mathf.Clamp(-1.2f + real * 0.2f, -r.seaFloorDepth + 2f, -1.2f);

        /// <param name="real">Real elevation (metres) on the coarse grid, smoothed, with everything that isn't Nova Scotia sunk.</param>
        public static BaseTerrain Build(MapRecipe r, Grid real)
        {
            int w = real.W, h = real.H, n = w * h;
            float cell = real.Cell;
            var highland = new Grid(w, h, cell, real.Origin);
            var land = new bool[n];
            for (int i = 0; i < n; i++)
            {
                highland.V[i] = Mathf.Max(0f, real.V[i]);
                land[i] = real.V[i] > r.landAbove;
            }
            highland.Blur(Mathf.Max(1, Mathf.RoundToInt(r.regionScale / cell / 2f)), 3);
            for (int i = 0; i < n; i++) highland.V[i] = Mathf.Clamp01(highland.V[i] / r.regionHigh);

            var inland = new Grid(w, h, cell, real.Origin);
            var offshore = new Grid(w, h, cell, real.Origin);
            var d = new float[n];
            for (int i = 0; i < n; i++) d[i] = land[i] ? 1e9f : 0f;
            Chamfer(w, h, d);
            for (int i = 0; i < n; i++) inland.V[i] = d[i] * cell;
            for (int i = 0; i < n; i++) d[i] = land[i] ? 0f : 1e9f;
            Chamfer(w, h, d);
            for (int i = 0; i < n; i++) offshore.V[i] = d[i] * cell;

            var heights = new Grid(w, h, cell, real.Origin);
            var hardness = new Grid(w, h, cell, real.Origin);
            var warp = new Noise(r.seed + 501);
            var broad = new Noise(r.seed + 502);
            var hills = new Noise(r.seed + 503);
            var ridges = new Noise(r.seed + 504);
            var mask = new Noise(r.seed + 505);
            var coast = new Noise(r.seed + 507);
            var terrace = new Noise(r.seed + 508);
            var rock = new Noise(r.seed + 509);
            var ribMask = new Noise(r.seed + 510);
            var ribs = new Noise(r.seed + 511);
            Parallel.For(0, h, z =>
            {
                for (int x = 0; x < w; x++)
                {
                    int i = z * w + x;
                    float wx = real.X(x), wz = real.Z(z);
                    float g = Curve(r, real.V[i]);
                    if (g < 0f)
                    {
                        // At sea the floor shelves away from the shore (the source has no depths in places).
                        heights.V[i] = Mathf.Max(-r.seaFloorDepth + 2f, Mathf.Min(g, -1.2f - offshore.V[i] * 0.06f));
                        hardness.V[i] = 0.5f;
                        continue;
                    }
                    float shore = inland.V[i];
                    float high = highland.V[i];
                    float landness = Mathf.Clamp01(g / 3f);

                    // Warp the coordinates so nothing lines up on the noise grid.
                    float px = wx + r.warpAmount * warp.Fbm(wx / 340f, wz / 340f, 3);
                    float pz = wz + r.warpAmount * warp.Fbm(wx / 340f + 31.7f, wz / 340f - 12.3f, 3);

                    float rise = r.lowBroad * broad.Fbm(px / 900f, pz / 900f, 3);
                    float hill = r.lowHill * Mathf.Lerp(1f, r.highHillGain, high) * hills.Fbm(px / 230f, pz / 230f, 4);
                    // Rocky ridged mountains in the high country, and here and there elsewhere.
                    float mountainous = Mathf.Max(Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.18f, 0.7f, high)),
                        0.35f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.35f, 0.6f, mask.Fbm(wx / 1600f, wz / 1600f, 2))));
                    float ridge = r.mountainHeight * mountainous * Mathf.Pow(ridges.Ridged(px / 420f, pz / 420f, 3), 2.2f);

                    float relief = (rise + hill + ridge) * Mathf.SmoothStep(0f, 1f, shore / r.shoreCalm);
                    // Bluffs on the rugged stretches of coast: the land rises quickly just inland.
                    float rugged = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.15f, 0.4f, coast.Fbm(wx / 700f, wz / 700f, 2)));
                    relief += rugged * r.seaCliffHeight * Mathf.SmoothStep(0f, 1f, shore / 35f);
                    // Bedrock ribs: in patches of the lowland the granite shows through as sharp whaleback crests,
                    // the way it does across the South Shore and the interior barrens. Hard rock, so the rivers go round.
                    float ribPatch = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 0.72f, 0.5f + 0.5f * ribMask.Fbm(wx / 900f + 7.7f, wz / 900f - 3.1f, 2)));
                    float rib = r.ribHeight * ribPatch * Mathf.Pow(ribs.Ridged(px / 180f, pz / 180f, 3), 3f);
                    relief += rib;
                    float height = g + landness * relief;

                    // Highland terraces: the height pulled towards steps, each step flat for most of its rise then a
                    // steep riser near the top, the step edges wandering with the noise. The hard band is that riser.
                    float wobble = r.terraceWarp * terrace.Fbm(wx / 260f, wz / 260f, 3);
                    float u = (height + wobble) / r.terraceStep;
                    float frac = u - Mathf.Floor(u);
                    float stepped = Mathf.Floor(u) * r.terraceStep + r.terraceStep * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 0.95f, frac)) - wobble;
                    float terracing = r.terraceBlend * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.1f, 0.5f, high)) * landness;
                    height = Mathf.Lerp(height, stepped, terracing);

                    // Never dig the land down to the sea.
                    height = Mathf.Max(height, 0.8f + g * 0.3f);
                    heights.V[i] = height;

                    float soft = 0.35f + 0.15f * rock.Fbm(wx / 300f, wz / 300f, 3);
                    float band = frac > 1f - r.hardBand / r.terraceStep ? 0.95f : 0.25f;
                    float hard = Mathf.Lerp(soft, band, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.1f, 0.5f, high)));
                    if (rib > r.ribHeight * 0.3f) hard = Mathf.Max(hard, 0.9f);
                    hardness.V[i] = Mathf.Clamp01(hard);
                }
            });
            return new BaseTerrain(heights, hardness, highland, inland, offshore);
        }

        /// <summary>How deep rivers may cut here: shallow valleys in the lowlands, gorges in the highlands.</summary>
        public Grid MaxIncision(MapRecipe r)
        {
            var g = new Grid(Heights.W, Heights.H, Heights.Cell, Heights.Origin);
            for (int i = 0; i < g.V.Length; i++) g.V[i] = Mathf.Lerp(r.maxIncisionLow, r.maxIncisionHigh, Highland.V[i]);
            return g;
        }

        /// <summary>Chamfer distance (cells, 8-connected) from the zero cells, in place.</summary>
        public static void Chamfer(int w, int h, float[] d)
        {
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
        }
    }
}
