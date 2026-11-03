using System.Collections.Generic;
using Cabin.Map;
using UnityEngine;

namespace Cabin.Editor.Map
{
    /// <summary>
    /// Boulders and outcrops, by rule, deterministic from the seed, placed as terrain trees alongside the real trees.
    /// Nova Scotia is granite country: rock shows on steep ground and ridge crests, in bare rocky patches of the
    /// barrens, up in the highlands, along stony lake shores and river banks. Big outcrops where the ground is
    /// rockiest, boulders and stones around them; a few stray boulders in the woods. Rocks sit a little into the
    /// ground so they read as bedded, not dropped.
    /// </summary>
    public static class RockScatter
    {
        /// <summary>Candidate spacing (metres): one try per cell of this size.</summary>
        const float Spacing = 5f;
        const float MaxSlope = 55f;

        /// <summary>
        /// How rocky a point is (0–1): steep ground, bare rocky patches (<paramref name="patch"/> 0–1 noise), high
        /// country, and stony shores (<paramref name="shoreNoise"/> 0–1) just above the water.
        /// </summary>
        public static float Density(float height, float aboveWater, float slope, float patch, float shoreNoise)
        {
            if (height < 0.5f || aboveWater < 0.15f) return 0f;
            float steep = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(22f, 40f, slope));
            float barren = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.6f, 0.78f, patch)) * 0.7f;
            float high = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(70f, 150f, height)) * 0.45f;
            float shore = aboveWater < 3f ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.62f, 0.8f, shoreNoise)) * Mathf.InverseLerp(3f, 1f, aboveWater) * 0.8f : 0f;
            float stray = 0.03f;
            return Mathf.Clamp01(Mathf.Max(steep, barren, high, shore) + stray);
        }

        public static int Place(MapRecipe r, System.Random rng, Grid heights, Grid water, int protoOffset, int rockCount, float x0, float z0, float sizeY,
            Vector2 clearing, float clearingRadius, List<TreeInstance> into)
        {
            if (rockCount < RockLibrary.SmallFrom + 1) return 0;
            var patches = new Noise(r.seed + 61);
            var shores = new Noise(r.seed + 67);
            int n = Mathf.FloorToInt(r.tileSize / Spacing), placed = 0;
            for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
            {
                float wx = x0 + (i + (float)rng.NextDouble()) * Spacing, wz = z0 + (j + (float)rng.NextDouble()) * Spacing;
                float roll = (float)rng.NextDouble(), kindRoll = (float)rng.NextDouble(), sizeRoll = (float)rng.NextDouble();
                if ((new Vector2(wx, wz) - clearing).sqrMagnitude < clearingRadius * clearingRadius) continue;
                float h = heights.Sample(wx, wz);
                float level = MapGenerator.WaterNear(water, wx, wz, out _);
                int gx = Mathf.Clamp(Mathf.RoundToInt((wx - heights.Origin.x) / heights.Cell), 0, heights.W - 1);
                int gz = Mathf.Clamp(Mathf.RoundToInt((wz - heights.Origin.y) / heights.Cell), 0, heights.H - 1);
                float slope = heights.Slope(gx, gz);
                if (slope > MaxSlope) continue;
                float patch = patches.Fbm(wx / 70f, wz / 70f, 3) * 0.5f + 0.5f;
                float shoreNoise = shores.Fbm(wx / 40f, wz / 40f, 2) * 0.5f + 0.5f;
                float density = Density(h, h - level, slope, patch, shoreNoise) * r.rockDensity;
                if (roll > density * 0.3f) continue;
                // Outcrops where it's rockiest, boulders mostly, stones everywhere rocky.
                // The pack's big rocks are stacked slabs that read as masonry up close: rare, only where it is rockiest, and mostly buried.
                int proto = kindRoll < 0.06f * density * density ? RockLibrary.BigFrom : kindRoll < 0.6f ? RockLibrary.MidFrom : RockLibrary.SmallFrom;
                proto += Mathf.Min(RockLibrary.PerSize - 1, Mathf.FloorToInt(sizeRoll * RockLibrary.PerSize));
                float scale = proto < RockLibrary.MidFrom ? Mathf.Lerp(0.5f, 0.9f, (float)rng.NextDouble()) : Mathf.Lerp(0.7f, 1.5f, (float)rng.NextDouble());
                float sink = proto < RockLibrary.MidFrom ? 1.8f : proto < RockLibrary.SmallFrom ? 0.25f : 0.08f;
                // Weathered granite: darker and browner than the pack's grey, mossy here and there.
                var tint = Color.Lerp(new Color(0.62f, 0.6f, 0.56f), new Color(0.5f, 0.56f, 0.42f), (float)rng.NextDouble());
                into.Add(new TreeInstance
                {
                    prototypeIndex = protoOffset + proto,
                    position = new Vector3((wx - x0) / r.tileSize, (h - sink * scale + r.seaFloorDepth) / sizeY, (wz - z0) / r.tileSize),
                    widthScale = scale * Mathf.Lerp(0.85f, 1.15f, (float)rng.NextDouble()),
                    heightScale = scale,
                    rotation = (float)rng.NextDouble() * Mathf.PI * 2f,
                    color = tint,
                    lightmapColor = Color.white,
                });
                placed++;
            }
            return placed;
        }
    }
}
