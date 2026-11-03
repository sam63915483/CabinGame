using Cabin.Editor.Map;
using Cabin.Map;
using NUnit.Framework;
using UnityEngine;

namespace Cabin.Tests
{
    public class TerrainPainterTests
    {
        const int Samples = 400;

        static MapRecipe Recipe() => ScriptableObject.CreateInstance<MapRecipe>();

        /// <summary>Average layer weights over a square of ground at one height and slope (the noises vary across it).</summary>
        static float[] Average(MapRecipe r, float height, float slope, float waterLevel, bool sea)
        {
            var noise = new Noise(r.seed + 7);
            var sum = new float[TerrainPainter.Layers];
            var w = new float[TerrainPainter.Layers];
            int n = 0;
            for (int j = 0; j < 20; j++)
            for (int i = 0; i < 20; i++)
            {
                TerrainPainter.Weights(r, noise, height, slope, waterLevel, sea, 100f + i * 37f, -300f + j * 41f, w);
                float total = 0f;
                for (int k = 0; k < w.Length; k++) { Assert.GreaterOrEqual(w[k], 0f); total += w[k]; }
                Assert.AreEqual(1f, total, 1e-3f, "Weights sum to 1");
                for (int k = 0; k < w.Length; k++) sum[k] += w[k];
                n++;
            }
            Assert.AreEqual(Samples, n);
            for (int k = 0; k < sum.Length; k++) sum[k] /= n;
            return sum;
        }

        [Test]
        public void GentleOpenGround_IsTurf_NotBareOrRock()
        {
            var r = Recipe();
            var w = Average(r, 20f, 8f, 0f, true);
            Assert.Greater(w[TerrainPainter.Grass] + w[TerrainPainter.Meadow] + w[TerrainPainter.ForestFloor] + w[TerrainPainter.Moss], 0.85f, "Mostly turf and woods");
            Assert.Less(w[TerrainPainter.Rock], 0.02f, "No rock on gentle low ground");
            Assert.Less(w[TerrainPainter.Dirt], 0.15f, "Bare earth only in small patches");
            Object.DestroyImmediate(r);
        }

        [Test]
        public void SteepBand_IsHeathWithRockPatches_NotADirtBand()
        {
            var r = Recipe();
            float mid = 0.5f * (r.dirtSlope + r.rockSlope);
            var w = Average(r, 20f, mid, 0f, true);
            Assert.Greater(w[TerrainPainter.Meadow], w[TerrainPainter.Grass], "The turf dries to heath on the steep band");
            Assert.Greater(w[TerrainPainter.Rock], 0.1f, "Bedrock shows through in patches");
            Assert.Less(w[TerrainPainter.Rock], 0.7f, "…but it is not a rock face yet");
            Assert.Less(w[TerrainPainter.Dirt], 0.3f, "Bare earth stays in patches, not a band");
            Object.DestroyImmediate(r);
        }

        [Test]
        public void Cliffs_AreRock()
        {
            var r = Recipe();
            var w = Average(r, 20f, r.rockSlope + 20f, 0f, true);
            Assert.Greater(w[TerrainPainter.Rock], 0.9f);
            Object.DestroyImmediate(r);
        }

        [Test]
        public void RockGrowsWithSlope()
        {
            var r = Recipe();
            float previous = -1f;
            for (float slope = 0f; slope <= 60f; slope += 10f)
            {
                float rock = Average(r, 20f, slope, 0f, true)[TerrainPainter.Rock];
                Assert.GreaterOrEqual(rock, previous - 1e-3f, $"Rock does not fall from {slope - 10f} to {slope} degrees");
                previous = rock;
            }
            Object.DestroyImmediate(r);
        }

        [Test]
        public void Shores_SeaIsSand_LakeIsGravel_BedIsDirt()
        {
            var r = Recipe();
            Assert.Greater(Average(r, 0.3f, 5f, 0f, true)[TerrainPainter.Sand], 0.6f, "Sea shore");
            Assert.Greater(Average(r, 60.3f, 5f, 60f, false)[TerrainPainter.Gravel], 0.6f, "Lake edge");
            Assert.Greater(Average(r, 58f, 5f, 60f, false)[TerrainPainter.Dirt], 0.9f, "Lake bed");
            Object.DestroyImmediate(r);
        }

        [Test]
        public void Clearing_IsOpenTurf_NotForestFloor()
        {
            var r = Recipe();
            var noise = new Noise(r.seed + 7);
            var w = new float[TerrainPainter.Layers];
            // A spot in the woods: with the clearing it is turf, without it litter.
            float wx = 0f, wz = 0f, best = 0f;
            for (int i = 0; i < 400 && best < 0.9f; i++) { float f = TerrainPainter.Forest(noise, i * 53f, i * 29f); if (f > best) { best = f; wx = i * 53f; wz = i * 29f; } }
            Assert.Greater(best, 0.9f, "Found deep woods");
            TerrainPainter.Weights(r, noise, 20f, 5f, 0f, true, wx, wz, w, 1f);
            Assert.Less(w[TerrainPainter.ForestFloor] + w[TerrainPainter.Moss], 0.01f, "No litter in the clearing");
            Assert.Greater(w[TerrainPainter.Grass] + w[TerrainPainter.Meadow], 0.6f, "Turf in the clearing");
            TerrainPainter.Weights(r, noise, 20f, 5f, 0f, true, wx, wz, w);
            Assert.Greater(w[TerrainPainter.ForestFloor] + w[TerrainPainter.Moss], 0.5f, "Litter outside it");
            Assert.AreEqual(1f, TerrainPainter.Clearing(10f, 30f), 1e-5f);
            Assert.AreEqual(0f, TerrainPainter.Clearing(60f, 30f), 1e-5f);
            Assert.That(TerrainPainter.Clearing(37f, 30f), Is.InRange(0.2f, 0.8f), "Fades over the fringe");
            Object.DestroyImmediate(r);
        }

        [Test]
        public void HighGround_ShowsRockInPatches()
        {
            var r = Recipe();
            float rock = Average(r, r.rockFull + 40f, 8f, 0f, true)[TerrainPainter.Rock];
            Assert.Greater(rock, 0.1f, "Barrens on the tops");
            Assert.Less(rock, 0.6f, "In patches, not a cap");
            Object.DestroyImmediate(r);
        }
    }
}
