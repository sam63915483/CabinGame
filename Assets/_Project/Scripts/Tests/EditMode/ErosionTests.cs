using Cabin.Editor.Map;
using NUnit.Framework;
using UnityEngine;
using Grid = Cabin.Editor.Map.Grid;

namespace Cabin.Tests
{
    public class ErosionTests
    {
        const int N = 48;

        /// <summary>A plane sloping to a sea along z = 0, with seeded bumps.</summary>
        static Grid Land(int seed, out Grid hardness)
        {
            var rng = new System.Random(seed);
            var g = new Grid(N, N, 1f, Vector2.zero);
            hardness = new Grid(N, N, 1f, Vector2.zero);
            for (int z = 0; z < N; z++)
            for (int x = 0; x < N; x++)
            {
                g[x, z] = z == 0 ? -5f : 2f + z * 0.8f + 3f * (float)rng.NextDouble();
                hardness[x, z] = 0.3f;
            }
            return g;
        }

        static Grid Flat(float value) => new Grid(N, N, 1f, Vector2.zero, value);

        static Erosion.Settings Settings(int iterations) => new Erosion.Settings { Iterations = iterations, Rate = 0.05f, Diffusion = 0.05f, MinLand = 0.5f };

        static bool Fixed(Grid g, int i) => g.V[i] < 0f || i % N == 0 || i % N == N - 1 || i / N == N - 1;

        [Test]
        public void FixedCells_Unchanged_AndLandNeverBelowMin()
        {
            var g = Land(1, out var hard);
            var before = (float[])g.V.Clone();
            Erosion.Run(g, hard, i => Fixed(g, i), Flat(1000f), Settings(20));
            for (int i = 0; i < N * N; i++)
            {
                if (Fixed(g, i)) Assert.AreEqual(before[i], g.V[i], "Fixed cells stay");
                else Assert.GreaterOrEqual(g.V[i], 0.5f, "Land never cut below the minimum");
            }
        }

        [Test]
        public void Channel_EndsLowerThanRidges()
        {
            var g = Land(2, out var hard);
            Erosion.Run(g, hard, i => Fixed(g, i), Flat(1000f), Settings(30));
            // The cell with the most water through it, a row up from the sea, against the ground six cells either side.
            var d = Drainage.Fill(g, i => Fixed(g, i));
            int best = -1;
            for (int x = 8; x < N - 8; x++)
            {
                int c = 2 * N + x;
                if (best < 0 || d.Accumulation[c] > d.Accumulation[best]) best = c;
            }
            float sides = 0.5f * (g.V[best - 6] + g.V[best + 6]);
            Assert.Less(g.V[best], sides, "The channel is cut below the ground beside it");
        }

        [Test]
        public void Deterministic()
        {
            var a = Land(3, out var hardA);
            var b = Land(3, out var hardB);
            Erosion.Run(a, hardA, i => Fixed(a, i), Flat(1000f), Settings(10));
            Erosion.Run(b, hardB, i => Fixed(b, i), Flat(1000f), Settings(10));
            CollectionAssert.AreEqual(a.V, b.V);
        }

        [Test]
        public void IncisionCap_Holds()
        {
            var g = Land(4, out var hard);
            var before = (float[])g.V.Clone();
            Erosion.Run(g, hard, i => Fixed(g, i), Flat(3f), Settings(40));
            for (int i = 0; i < N * N; i++) Assert.LessOrEqual(before[i] - g.V[i], 3f + 1e-3f, "Cut no deeper than the cap");
        }

        [Test]
        public void HardBand_ErodesLess()
        {
            var g = Land(5, out var hard);
            var soft = Land(5, out var hardSoft);
            for (int i = 0; i < N * N; i++) hard.V[i] = 0.95f;
            Erosion.Run(g, hard, i => Fixed(g, i), Flat(1000f), Settings(20));
            Erosion.Run(soft, hardSoft, i => Fixed(soft, i), Flat(1000f), Settings(20));
            float cutHard = 0f, cutSoft = 0f;
            var start = Land(5, out _);
            for (int i = 0; i < N * N; i++)
            {
                cutHard += start.V[i] - g.V[i];
                cutSoft += start.V[i] - soft.V[i];
            }
            Assert.Less(cutHard, cutSoft * 0.5f, "Hard rock loses much less");
        }
    }
}
