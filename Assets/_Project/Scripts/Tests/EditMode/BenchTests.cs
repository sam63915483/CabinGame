using System.Collections.Generic;
using Cabin.Bench;
using Cabin.Core;
using NUnit.Framework;
using UnityEngine;

namespace Cabin.Tests
{
    public class BenchTests
    {
        [Test]
        public void Combo_OverridesOnlyWhatItNames()
        {
            BenchCombo.Baseline();
            bool grass = new BenchCombo { name = "shadows_off", shadows = 0 }.ApplyTo();
            Assert.AreEqual(0, GameSettings.Shadows);
            Assert.AreEqual(-1, GameSettings.Msaa, "Untouched: still follows the tier");
            Assert.AreEqual(2, GameSettings.Quality);
            Assert.AreEqual(120, GameSettings.GrassDistance);
            Assert.IsTrue(grass);
            Assert.IsFalse(new BenchCombo { name = "grass_off", grass = 0 }.ApplyTo());
            new BenchCombo { msaa = -1 }.ApplyTo();
            Assert.AreEqual(-1, GameSettings.Msaa, "-1 is a real value (follow the tier); -2 is untouched");
            BenchCombo.Baseline();
            Assert.AreEqual(-1, GameSettings.Shadows);
        }

        [Test]
        public void Plan_RoundTripsThroughJson_WithDefaultsForMissingFields()
        {
            var plan = new BenchPlan
            {
                stops = new[] { new BenchStop { name = "hill", x = 1f, z = 2f, yaws = new[] { 10f, 100f }, pitch = 8f, sweep = true } },
                combos = new[] { new BenchCombo(), new BenchCombo { name = "msaa_off", msaa = 0 } },
            };
            var back = JsonUtility.FromJson<BenchPlan>(JsonUtility.ToJson(plan));
            Assert.AreEqual(1920, back.width);
            Assert.AreEqual("hill", back.stops[0].name);
            Assert.AreEqual(2, back.stops[0].yaws.Length);
            Assert.IsTrue(back.stops[0].sweep);
            Assert.AreEqual(0, back.combos[1].msaa);
            Assert.AreEqual(-2, back.combos[1].shadows, "Untouched stays untouched");
            Assert.AreEqual(-1, back.combos[1].depthTexture, "Experiments default to as built");
            Assert.AreEqual(-1f, back.combos[1].basemapDistance);
            var sparse = JsonUtility.FromJson<BenchPlan>("{\"stops\":[{\"name\":\"a\"}]}");
            Assert.AreEqual(2.5f, sparse.settleSeconds, 1e-6f);
            Assert.AreEqual(1, sparse.combos.Length);
        }

        [Test]
        public void Summarise_GivesAverageFps_AndTheSlowestPercent()
        {
            var times = new List<float>();
            for (int i = 0; i < 200; i++) times.Add(0.005f); // 200 fps
            times[17] = 0.05f; // one 20 fps hitch
            times[90] = 0.05f;
            BenchResult.Summarise(times, out float fps, out float low1);
            Assert.That(fps, Is.InRange(180f, 190f));
            Assert.AreEqual(20f, low1, 0.5f, "The worst 1 % (two of 200 frames) are the hitches");
            BenchResult.Summarise(new List<float>(), out fps, out low1);
            Assert.AreEqual(0f, fps);
        }

        [Test]
        public void Csv_HasAColumnPerHeaderField()
        {
            var r = new BenchResult { stop = "spawn", combo = "default", yaw = 90f, fps = 123.4f, shot = "x.png" };
            Assert.AreEqual(BenchResult.Header.Split(',').Length, r.Csv().Split(',').Length);
        }
    }
}
