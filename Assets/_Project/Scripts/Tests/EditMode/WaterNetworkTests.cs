using System.Collections.Generic;
using System.Linq;
using Cabin.Editor.Map;
using NUnit.Framework;
using UnityEngine;
using Grid = Cabin.Editor.Map.Grid;

namespace Cabin.Tests
{
    public class WaterNetworkTests
    {
        const int N = 32;

        /// <summary>Land rising away from a sea along z = 0, one metre cells.</summary>
        static Grid Slope(float baseHeight, System.Func<int, int, float> extra)
        {
            var g = new Grid(N, N, 1f, Vector2.zero);
            for (int z = 0; z < N; z++)
            for (int x = 0; x < N; x++)
                g[x, z] = z == 0 ? -5f : baseHeight + 0.1f * z + extra(x, z);
            return g;
        }

        static bool Sea(Grid g, int i) => g.V[i] < 0f;

        static WaterNetwork.Settings Settings(float catchmentCells) => new WaterNetwork.Settings
        {
            LakeMinDepth = 0.25f, LakeMinCells = 20, RiverCatchmentKm2 = catchmentCells * 1e-6f, FallSlope = 0.35f, FallMinHeight = 2.5f,
        };

        [Test]
        public void BowlWithNotch_OneLakeAtTheNotch_WithAnOutletRiverToTheSea()
        {
            // A bowl 8 across and 10 deep in the middle, with a channel at height 7 out of its downhill side falling to the sea.
            var g = Slope(10f, (x, z) =>
            {
                float r = Mathf.Sqrt((x - 16) * (x - 16) + (z - 16) * (z - 16));
                return r < 8f ? -10f * (1f - r / 8f) : 0f;
            });
            for (int z = 1; z <= 12; z++) g[16, z] = Mathf.Min(g[16, z], 7f - 0.3f * Mathf.Max(0, 8 - z));
            var net = WaterNetwork.Build(g, i => Sea(g, i), Settings(40));

            Assert.AreEqual(1, net.Lakes.Count, "One lake");
            var lake = net.Lakes[0];
            Assert.AreEqual(7f, lake.Level, 0.05f, "At the notch's level");
            foreach (int c in lake.Cells) Assert.Less(g.V[c], lake.Level, "Every lake cell is under water");
            Assert.GreaterOrEqual(lake.Cells.Count, 20);
            Assert.IsTrue(lake.Outlet >= 0 && net.LakeOf[lake.Outlet] < 0, "Outlet is outside the lake");

            var outflow = net.Rivers.Where(r => r.StartsFromLake == 0).ToList();
            Assert.AreEqual(1, outflow.Count, "One river leaves the lake");
            Assert.IsTrue(outflow[0].EndsAtSea, "And reaches the sea");
            Assert.AreEqual(lake.Level, outflow[0].Levels[0], 0.05f, "Starting at the lake's level");
            foreach (var r in net.Rivers)
                for (int i = 1; i < r.Levels.Count; i++) Assert.LessOrEqual(r.Levels[i], r.Levels[i - 1] + 1e-4f, "Levels never rise downstream");
        }

        [Test]
        public void SmallHollow_IsFilledIn_NoLake()
        {
            var g = Slope(10f, (x, z) => x >= 10 && x <= 11 && z >= 20 && z <= 21 ? -2f : 0f);
            float before = g[10, 20];
            var net = WaterNetwork.Build(g, i => Sea(g, i), Settings(1000));
            Assert.AreEqual(0, net.Lakes.Count);
            Assert.Greater(g[10, 20], before + 1.5f, "The hollow's ground is raised to its spill");
        }

        [Test]
        public void StepInAChannel_IsOneFallOfTheStepsHeight()
        {
            // Everything drains to the channel at x = 8; the channel drops 5 m at z = 10.
            var g = Slope(1f, (x, z) => 0.3f * Mathf.Abs(x - 8) + 0.1f * z + (z >= 10 ? 5f : 0f));
            var net = WaterNetwork.Build(g, i => Sea(g, i), Settings(60));
            Assert.AreEqual(1, net.Rivers.Count, "One river");
            var r = net.Rivers[0];
            Assert.AreEqual(1, r.Falls.Count, "One fall");
            Assert.That(r.Falls[0].Height, Is.InRange(4f, 7f), "About the step's height");
            Assert.Greater(r.Falls[0].LipLevel, r.Falls[0].BaseLevel);
            Assert.IsTrue(r.EndsAtSea);
        }

        [Test]
        public void Smoothing_KeepsLevelsNonIncreasing_AndWidthsGrow()
        {
            var g = Slope(1f, (x, z) => 0.3f * Mathf.Abs(x - 20) + 0.1f * z);
            var net = WaterNetwork.Build(g, i => Sea(g, i), Settings(60));
            Assert.GreaterOrEqual(net.Rivers.Count, 1);
            foreach (var r in net.Rivers)
            {
                for (int i = 1; i < r.Levels.Count; i++) Assert.LessOrEqual(r.Levels[i], r.Levels[i - 1] + 1e-4f);
                Assert.GreaterOrEqual(r.Widths[r.Widths.Count - 1], r.Widths[0], "Wider at the mouth");
                Assert.Greater(r.Points.Count, 4, "Smoothed into a curve");
            }
        }

        [Test]
        public void Downhill_PoolsAdjacentViolators()
        {
            var ground = new List<float> { 20f, 19f, 18f, 23f, 24f, 17f, 16f, 15f };
            var level = new List<float>(ground);
            WaterNetwork.Downhill(level);
            for (int i = 1; i < level.Count; i++) Assert.LessOrEqual(level[i], level[i - 1], "Never rises");
            Assert.AreEqual(ground.Sum(), level.Sum(), 1e-3f, "As close to the ground as it can be: cuts and fills balance");
            Assert.AreEqual(15f, level[7], 1e-3f, "Untouched downstream of the bump");
            Assert.Less(Mathf.Abs(level[0] - ground[0]), 1.5f, "Only nudged upstream of it");
            Assert.Greater(level[2], 18f, "The bump is evened out, not cut down to the lowest point after it");
        }
    }
}
