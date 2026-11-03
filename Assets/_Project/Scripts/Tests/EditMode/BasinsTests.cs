using Cabin.Editor.Map;
using Cabin.Map;
using NUnit.Framework;
using UnityEngine;
using Grid = Cabin.Editor.Map.Grid;

namespace Cabin.Tests
{
    public class BasinsTests
    {
        const int N = 64;

        /// <summary>Gentle lowland in the west half, steep highland in the east, sea along the south edge.</summary>
        static (Grid real, MapRecipe recipe) Land()
        {
            var recipe = ScriptableObject.CreateInstance<MapRecipe>();
            recipe.pondThreshold = 0.5f; // plenty of ponds on a small grid
            recipe.lakeCountryBias = 0f;
            recipe.regionScale = 100f; // a 64-cell grid: the 500 m regional blur would make all of it highland
            var real = new Grid(N, N, 8f, new Vector2(-N * 4f, -N * 4f));
            for (int z = 0; z < N; z++)
            for (int x = 0; x < N; x++)
                real[x, z] = z < 4 ? -20f : (x < N / 2 ? 30f + z * 0.5f : 400f + z * 2f + x * 3f);
            return (real, recipe);
        }

        [Test]
        public void Scoops_OnlyLowerGentleLowland_NeverNearTheSea()
        {
            var (real, recipe) = Land();
            var b = BaseTerrain.Build(recipe, real);
            var h = new Grid(N, N, 8f, real.Origin);
            System.Array.Copy(b.Heights.V, h.V, h.V.Length);
            var before = (float[])h.V.Clone();
            Basins.Scoop(h, b, recipe, Vector2.zero, new Vector2(N * 8f, N * 8f));
            int lowered = 0;
            for (int i = 0; i < N * N; i++)
            {
                Assert.LessOrEqual(h.V[i], before[i] + 1e-4f, "Scoops only lower");
                Assert.GreaterOrEqual(h.V[i], before[i] - recipe.pondDepth - recipe.lakeDepth - 1e-3f, "No deeper than both scoops together");
                if (h.V[i] < before[i] - 0.01f)
                {
                    lowered++;
                    Assert.Greater(before[i], 1.5f, "Not at the beach");
                    Assert.GreaterOrEqual(b.Inland.V[i], 20f, "Not by the sea");
                    Assert.Less(b.Highland.V[i], 0.6f, "Not on steep highland");
                }
            }
            Assert.Greater(lowered, 20, "Some ponds were scooped");
        }

        [Test]
        public void Deterministic()
        {
            var (real, recipe) = Land();
            var b = BaseTerrain.Build(recipe, real);
            var a1 = new Grid(N, N, 8f, real.Origin);
            var a2 = new Grid(N, N, 8f, real.Origin);
            System.Array.Copy(b.Heights.V, a1.V, a1.V.Length);
            System.Array.Copy(b.Heights.V, a2.V, a2.V.Length);
            Basins.Scoop(a1, b, recipe, Vector2.zero, new Vector2(N * 8f, N * 8f));
            Basins.Scoop(a2, b, recipe, Vector2.zero, new Vector2(N * 8f, N * 8f));
            CollectionAssert.AreEqual(a1.V, a2.V);
        }

        [Test]
        public void BaseTerrain_HighlandIsHigher_WithHardBandsAndSoftLayers_SeaStaysSea()
        {
            var (real, recipe) = Land();
            var b = BaseTerrain.Build(recipe, real);
            float lowMean = 0f, highMean = 0f;
            int lowCount = 0, highCount = 0, hard = 0, soft = 0;
            for (int z = 8; z < N; z++)
            for (int x = 0; x < N; x++)
            {
                int i = z * N + x;
                if (x < N / 4) { lowMean += b.Heights.V[i]; lowCount++; }
                if (x > 3 * N / 4)
                {
                    highMean += b.Heights.V[i];
                    highCount++;
                    if (b.Hardness.V[i] > 0.8f) hard++;
                    if (b.Hardness.V[i] < 0.4f) soft++;
                }
            }
            Assert.Greater(highMean / highCount, lowMean / lowCount, "Highland is higher");
            Assert.Greater(hard, highCount / 10, "Hard bands cap the highland's terrace steps");
            Assert.Greater(soft, highCount / 10, "With soft layers between them");
            for (int x = 0; x < N; x++) Assert.Less(b.Heights[x, 1], 0f, "The sea is below sea level");
            for (int i = 0; i < N * N; i++) Assert.IsFalse(float.IsNaN(b.Heights.V[i]), "No NaN heights");
        }
    }
}
