using System.Collections.Generic;
using System.Linq;
using Cabin.Map;
using UnityEngine;

namespace Cabin.Editor.Map
{
    /// <summary>
    /// Trees for every tile, by rule, deterministic from the recipe seed. Dense where the ground is painted forest
    /// floor, thin over the grassy openings, clustered by noise; none in water, on beaches, on rock faces or in the
    /// clearing kept at the spawn. Mostly spruce and fir; birch and maple by water and in the valleys; conifers only up
    /// high; the odd dead snag.
    /// </summary>
    public static class VegetationScatter
    {
        /// <summary>Candidate spacing (metres): one try per cell of this size.</summary>
        const float Spacing = 3.9f;
        const float MaxSlope = 38f;

        public static int Apply(MapRecipe r, TerrainData[,] datas, GameObject[] prefabs, GameObject[] rocks, Grid heights, Grid water, Vector2 clearing, float clearingRadius)
        {
            var protos = prefabs.Concat(rocks).Select(p => new TreePrototype { prefab = p }).ToArray();
            var patches = new Noise(r.seed + 7); // the same patches TerrainPainter paints forest floor on
            var clusters = new Noise(r.seed + 23);
            var kinds = new Noise(r.seed + 41);
            int total = 0;
            for (int tz = 0; tz < r.tilesZ; tz++)
            for (int tx = 0; tx < r.tilesX; tx++)
            {
                var data = datas[tx, tz];
                data.treePrototypes = protos;
                var rng = new System.Random(r.seed * 7919 + tz * 1000 + tx);
                var trees = new List<TreeInstance>();
                float x0 = r.Origin.x + tx * r.tileSize, z0 = r.Origin.z + tz * r.tileSize;
                int n = Mathf.FloorToInt(r.tileSize / Spacing);
                for (int j = 0; j < n; j++)
                for (int i = 0; i < n; i++)
                {
                    float wx = x0 + (i + (float)rng.NextDouble()) * Spacing, wz = z0 + (j + (float)rng.NextDouble()) * Spacing;
                    float roll = (float)rng.NextDouble();
                    if ((new Vector2(wx, wz) - clearing).sqrMagnitude < clearingRadius * clearingRadius) continue;
                    float h = heights.Sample(wx, wz);
                    float level = MapGenerator.WaterNear(water, wx, wz, out _);
                    if (h < level + 0.9f || h < 1.6f) continue;
                    int gx = Mathf.Clamp(Mathf.RoundToInt((wx - heights.Origin.x) / heights.Cell), 0, heights.W - 1);
                    int gz = Mathf.Clamp(Mathf.RoundToInt((wz - heights.Origin.y) / heights.Cell), 0, heights.H - 1);
                    float slope = heights.Slope(gx, gz);
                    if (slope > MaxSlope) continue;

                    float forest = TerrainPainter.Forest(patches, wx, wz); // the painter's patches and clearings, exactly
                    float cluster = clusters.Fbm(wx / 38f, wz / 38f, 2) * 0.5f + 0.5f;
                    float density = Mathf.Lerp(0.05f, 1f, forest) * Mathf.Lerp(0.55f, 1.25f, cluster);
                    density *= 1f - Mathf.InverseLerp(MaxSlope - 10f, MaxSlope, slope) * 0.7f;
                    if (roll > density) continue;

                    var kind = Pick(h, h - level, kinds.Fbm(wx / 90f, wz / 90f, 2), (float)rng.NextDouble());
                    int proto = PickSpecies(kind, (float)rng.NextDouble());
                    float scale = Mathf.Lerp(0.8f, 1.25f, (float)rng.NextDouble());
                    trees.Add(new TreeInstance
                    {
                        prototypeIndex = proto,
                        position = new Vector3((wx - x0) / r.tileSize, (h - r.Origin.y) / data.size.y, (wz - z0) / r.tileSize),
                        widthScale = scale * Mathf.Lerp(0.9f, 1.1f, (float)rng.NextDouble()),
                        heightScale = scale,
                        rotation = (float)rng.NextDouble() * Mathf.PI * 2f,
                        color = Color.Lerp(Color.white, new Color(0.85f, 0.9f, 0.85f), (float)rng.NextDouble()),
                        lightmapColor = Color.white,
                    });
                }
                RockScatter.Place(r, rng, heights, water, prefabs.Length, rocks.Length, x0, z0, data.size.y, clearing, clearingRadius, trees);
                data.SetTreeInstances(trees.ToArray(), true);
                total += trees.Count;
            }
            return total;
        }

        /// <summary>What grows here: conifers dominate; broadleaf and birch by water and low down; none but conifers up high.</summary>
        static TreeKind Pick(float height, float aboveWater, float mix, float roll)
        {
            if (roll < 0.03f) return TreeKind.Snag;
            float broad = 0.18f + Mathf.InverseLerp(10f, 2f, aboveWater) * 0.35f + Mathf.InverseLerp(35f, 10f, height) * 0.15f + mix * 0.2f;
            broad *= Mathf.InverseLerp(110f, 60f, height);
            if (roll < 0.03f + broad) return roll < 0.03f + broad * 0.4f ? TreeKind.Birch : TreeKind.Broadleaf;
            return TreeKind.Conifer;
        }

        static int PickSpecies(TreeKind kind, float roll)
        {
            var options = TreeLibrary.OfKind(kind).ToArray();
            float total = options.Sum(i => TreeLibrary.All[i].Weight), t = roll * total;
            foreach (int i in options)
            {
                t -= TreeLibrary.All[i].Weight;
                if (t <= 0f) return i;
            }
            return options[options.Length - 1];
        }
    }
}
