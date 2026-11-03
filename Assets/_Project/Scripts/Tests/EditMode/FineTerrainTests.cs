using System.Collections.Generic;
using Cabin.Editor.Map;
using Cabin.Map;
using NUnit.Framework;
using UnityEngine;
using Grid = Cabin.Editor.Map.Grid;

namespace Cabin.Tests
{
    public class FineTerrainTests
    {
        const int N = 32;
        const float Cell = 8f;

        /// <summary>A coarse slope to a sea at z = 0 with a bowl (a lake) and a notch channel out of it, and the fine terrain built from it.</summary>
        static (Grid coarse, WaterNetwork net, FineTerrain fine) Build()
        {
            var g = new Grid(N, N, Cell, Vector2.zero);
            for (int z = 0; z < N; z++)
            for (int x = 0; x < N; x++)
            {
                float r = Mathf.Sqrt((x - 16) * (x - 16) + (z - 18) * (z - 18));
                g[x, z] = z == 0 ? -5f : 12f + 0.3f * z + (r < 8f ? -14f * (1f - r / 8f) : 0f);
            }
            for (int z = 1; z <= 14; z++) g[16, z] = Mathf.Min(g[16, z], 9f - 0.5f * Mathf.Max(0, 10 - z));
            var settings = WaterNetwork.Settings.Default;
            settings.RiverCatchmentKm2 = 40 * Cell * Cell / 1e6f;
            var net = WaterNetwork.Build(g, i => g.V[i] < 0f, settings);
            var recipe = ScriptableObject.CreateInstance<MapRecipe>();
            int fw = (N - 1) * 4 + 1;
            var fine = FineTerrain.Build(g, net, FineTerrain.Settings.From(recipe), fw, fw, Cell / 4f);
            return (g, net, fine);
        }

        [Test]
        public void SampleCubic_ReproducesALinearRamp()
        {
            var g = new Grid(8, 8, 1f, Vector2.zero);
            for (int z = 0; z < 8; z++)
            for (int x = 0; x < 8; x++)
                g[x, z] = 2f * x + 0.5f * z;
            Assert.AreEqual(2f * 3.25f + 0.5f * 4.5f, g.SampleCubic(3.25f, 4.5f), 1e-4f);
        }

        [Test]
        public void TheLakeIsThere_EveryLakeCellUnderItsLevel_AndConnected()
        {
            var (_, net, fine) = Build();
            Assert.AreEqual(1, net.Lakes.Count, "The bowl is a lake");
            var lake = net.Lakes[0];
            int w = fine.Heights.W, count = 0;
            var seen = new HashSet<int>();
            var queue = new Queue<int>();
            for (int i = 0; i < fine.LakeOf.Length; i++)
            {
                if (fine.LakeOf[i] != 0) continue;
                count++;
                Assert.Less(fine.Heights.V[i], lake.Level, "Under water");
                Assert.AreEqual(lake.Level, fine.Water.V[i], "Water at the lake's level");
                Assert.AreEqual(lake.Level, fine.Lakes.V[i], "In the lakes grid too");
                if (seen.Count == 0) { seen.Add(i); queue.Enqueue(i); }
            }
            Assert.Greater(count, 100, "A real lake at fine resolution");
            while (queue.Count > 0)
            {
                int c = queue.Dequeue();
                foreach (int j in new[] { c - 1, c + 1, c - w, c + w })
                    if (j >= 0 && j < fine.LakeOf.Length && fine.LakeOf[j] == 0 && seen.Add(j)) queue.Enqueue(j);
            }
            Assert.AreEqual(count, seen.Count, "All one connected lake");
        }

        [Test]
        public void RiverBed_IsBelowTheSurface_AllTheWay()
        {
            var (_, net, fine) = Build();
            Assert.GreaterOrEqual(net.Rivers.Count, 1);
            foreach (var r in net.Rivers)
                for (int i = 0; i < r.Points.Count; i++)
                {
                    var p = r.Points[i];
                    if (p.x < 2f || p.y < 2f || p.x > fine.Heights.X(fine.Heights.W - 1) - 2f || p.y > fine.Heights.Z(fine.Heights.H - 1) - 2f) continue;
                    if (r.Levels[i] <= 0f) continue; // the step into the sea
                    int fx = Mathf.RoundToInt((p.x - fine.Heights.Origin.x) / fine.Heights.Cell), fz = Mathf.RoundToInt((p.y - fine.Heights.Origin.y) / fine.Heights.Cell);
                    if (fine.LakeOf[fz * fine.Heights.W + fx] >= 0) continue; // in the lake the lake owns the bed (shallows at the shore)
                    Assert.Less(fine.Heights.Sample(p.x, p.y), r.Levels[i] - 0.2f, $"Bed under the water at point {i}");
                }
        }

        [Test]
        public void NoNaN_AndShoresAreGentle()
        {
            var (_, net, fine) = Build();
            int w = fine.Heights.W, h = fine.Heights.H;
            int shoreCells = 0, gentle = 0;
            for (int z = 1; z < h - 1; z++)
            for (int x = 1; x < w - 1; x++)
            {
                int i = z * w + x;
                Assert.IsFalse(float.IsNaN(fine.Heights.V[i]), "No NaN heights");
                if (fine.LakeOf[i] >= 0) continue;
                bool byLake = fine.LakeOf[i - 1] >= 0 || fine.LakeOf[i + 1] >= 0 || fine.LakeOf[i - w] >= 0 || fine.LakeOf[i + w] >= 0;
                if (!byLake) continue;
                shoreCells++;
                if (fine.Heights.Slope(x, z) < 30f) gentle++;
            }
            Assert.Greater(shoreCells, 20);
            Assert.GreaterOrEqual(gentle / (float)shoreCells, 0.9f, "Nine in ten shore cells are a beach, not a cliff");
        }
    }
}
