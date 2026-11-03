using Cabin.Editor.Map;
using NUnit.Framework;
using UnityEngine;
using Grid = Cabin.Editor.Map.Grid;

namespace Cabin.Tests
{
    public class DrainageTests
    {
        static Grid Plane(int w, int h, System.Func<int, int, float> f)
        {
            var g = new Grid(w, h, 1f, Vector2.zero);
            for (int z = 0; z < h; z++)
            for (int x = 0; x < w; x++)
                g[x, z] = f(x, z);
            return g;
        }

        [Test]
        public void TiltedPlane_EverythingDrainsToLowEdge()
        {
            const int n = 16;
            var g = Plane(n, n, (x, z) => x);
            var d = Drainage.Fill(g, i => i % n == 0);
            for (int i = 0; i < n * n; i++)
            {
                int c = i, steps = 0;
                while (d.Receiver[c] >= 0 && steps++ < n * n) c = d.Receiver[c];
                Assert.AreEqual(0, c % n, $"Cell {i} ends at the low edge");
            }
            float total = 0f;
            for (int z = 0; z < n; z++) total += d.Accumulation[z * n];
            Assert.AreEqual(n * n, total, 1e-3f, "Everything passes through the outlets");
        }

        [Test]
        public void Pit_FillsToSpill()
        {
            const int n = 16;
            var g = Plane(n, n, (x, z) => 10f);
            for (int z = 6; z <= 8; z++)
            for (int x = 6; x <= 8; x++)
                g[x, z] = 2f;
            for (int x = 9; x < n; x++) g[x, 7] = 6f; // a channel from the rim's low point out to the edge
            var d = Drainage.Fill(g, i => i % n == 0 || i / n == 0 || i % n == n - 1 || i / n == n - 1);
            for (int z = 6; z <= 8; z++)
            for (int x = 6; x <= 8; x++)
                Assert.AreEqual(6f, d.Filled[z * n + x], 0.02f, "The pit fills to its spill");
            for (int i = 0; i < n * n; i++) Assert.GreaterOrEqual(d.Filled[i], g.V[i], "Never below the ground");
        }

        [Test]
        public void Filled_NeverRisesAlongReceivers_AndOrderIsTopological()
        {
            const int n = 32;
            var rng = new System.Random(7);
            var g = Plane(n, n, (x, z) => 5f + 0.3f * x + 4f * (float)rng.NextDouble());
            var d = Drainage.Fill(g, i => i % n == 0);
            var rank = new int[n * n];
            for (int k = 0; k < n * n; k++) rank[d.Order[k]] = k;
            for (int i = 0; i < n * n; i++)
            {
                if (d.IsOutlet(i)) continue;
                Assert.Less(d.Filled[d.Receiver[i]], d.Filled[i], "Receiver is lower");
                Assert.Less(rank[d.Receiver[i]], rank[i], "Receiver comes first in the order");
            }
        }

        [Test]
        public void Accumulation_AtTheMouthIsTheCatchment()
        {
            const int n = 12;
            // A valley down the middle: everything drains to (6, 0).
            var g = Plane(n, n, (x, z) => z + Mathf.Abs(x - 6) * 0.5f);
            var d = Drainage.Fill(g, i => i == 6);
            Assert.AreEqual(n * n, d.Accumulation[6], 1e-3f);
        }
    }
}
