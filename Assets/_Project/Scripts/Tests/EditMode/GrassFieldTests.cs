using Cabin.Forestry;
using NUnit.Framework;
using UnityEngine;

namespace Cabin.Tests
{
    public class GrassFieldTests
    {
        [Test]
        public void Fade_IsFullNear_ThinsToTheFloorAtTheEdge_AndNeverRises()
        {
            Assert.AreEqual(1f, GrassField.Fade(0f, 80f), 1e-6f);
            Assert.AreEqual(1f, GrassField.Fade(80f * GrassField.FadeStart, 80f), 1e-6f);
            Assert.AreEqual(GrassField.FadeFloor, GrassField.Fade(80f, 80f), 1e-6f);
            float last = 1f;
            for (float d = 0f; d <= 80f; d += 1f)
            {
                float f = GrassField.Fade(d, 80f);
                Assert.That(f, Is.LessThanOrEqualTo(last + 1e-6f), $"Not thicker further out at {d}");
                last = f;
            }
            Assert.AreEqual(0f, GrassField.Fade(10f, 0f), "No range, no grass");
        }

        [Test]
        public void Tint_BlendsTheLayersTintsByWeight_WhiteWhereUnpainted()
        {
            var tints = new[] { Color.white, new Color(0.5f, 0.4f, 0.3f), Color.red };
            var splat = new float[1, 2, 3];
            splat[0, 0, 0] = 0.5f;
            splat[0, 0, 1] = 0.5f;
            var t = GrassField.Tint(splat, 0, 0, tints);
            Assert.AreEqual(0.75f, t.x, 1e-5f);
            Assert.AreEqual(0.7f, t.y, 1e-5f);
            Assert.AreEqual(0.65f, t.z, 1e-5f);
            Assert.AreEqual(1f, t.w, 1e-5f);
            Assert.AreEqual(Vector4.one, GrassField.Tint(splat, 0, 1, tints), "Nothing painted: no tint");
            Assert.AreEqual(Vector4.one, GrassField.Tint(splat, 0, 0, null), "No tints: no tint");
            splat[0, 1, 2] = 0.2f;
            Assert.AreEqual((Vector4)Color.red, GrassField.Tint(splat, 0, 1, tints), "Weights are normalised");
        }

        [Test]
        public void Fullness_IsFullInsideAPatch_LowAtItsEdge_AndAloneTuftsDrawSmall()
        {
            const int cells = 12, full = 10;
            var layer = new byte[cells * cells];
            // A full 6 × 6 patch in the corner, one lone tuft far from it.
            for (int y = 0; y < 6; y++) for (int x = 0; x < 6; x++) layer[y * cells + x] = (byte)full;
            layer[10 * cells + 10] = 1;
            var layers = new[] { layer };
            Assert.AreEqual(1f, GrassField.Fullness(layers, cells, 2, 2, 2, full), 1e-5f, "Deep inside, more than full");
            float edge = GrassField.Fullness(layers, cells, 5, 2, 2, full);
            Assert.That(edge, Is.InRange(0.5f, 0.75f), "At the edge about half the window is empty");
            float corner = GrassField.Fullness(layers, cells, 5, 5, 2, full);
            Assert.Less(corner, edge, "A corner is emptier than an edge");
            Assert.Less(GrassField.Fullness(layers, cells, 10, 10, 2, full), 0.01f, "A lone tuft");
            Assert.AreEqual(0f, GrassField.Fullness(layers, cells, 8, 8, 0, 0), "No full count, no fullness");

            Assert.AreEqual(1f, GrassField.SizeScale(1f, 0.4f), 1e-5f);
            Assert.AreEqual(0.4f, GrassField.SizeScale(0f, 0.4f), 1e-5f);
            Assert.AreEqual(0.4f, GrassField.SizeScale(0.05f, 0.4f), 1e-5f, "Still the fringe size just above empty");
            float last = 0f;
            for (float f = 0f; f <= 1f; f += 0.05f) { float sc = GrassField.SizeScale(f, 0.4f); Assert.GreaterOrEqual(sc, last - 1e-6f, "Grows with fullness"); last = sc; }
        }

        [Test]
        public void Lean_StandsOnTheGroundsNormal_KeepsItsYaw_AndStaysUprightAtZero()
        {
            var slope = new Vector3(0.5f, 1f, 0.2f).normalized;
            var full = GrassField.Lean(slope, 40f, 1f);
            Assert.AreEqual(0f, Vector3.Angle(full * Vector3.up, slope), 1e-3f, "Square on the ground");
            var half = GrassField.Lean(slope, 40f, 0.5f);
            Assert.AreEqual(Vector3.Angle(Vector3.up, slope) * 0.5f, Vector3.Angle(half * Vector3.up, Vector3.up), 0.05f, "Half way over");
            var none = GrassField.Lean(slope, 40f, 0f);
            Assert.AreEqual(0f, Vector3.Angle(none * Vector3.up, Vector3.up), 1e-4f, "Straight up at zero");
            Assert.AreEqual(0f, Vector3.Angle(none * Vector3.forward, Quaternion.Euler(0f, 40f, 0f) * Vector3.forward), 1e-3f, "Yaw kept");
            Assert.AreEqual(0f, Vector3.Angle(GrassField.Lean(Vector3.up, 40f, 1f) * Vector3.up, Vector3.up), 1e-4f, "Flat ground: upright");
        }

        [Test]
        public void Count_RoundsTheFraction_ButKeepsOneWhereThereWasGrass()
        {
            Assert.AreEqual(0, GrassField.Count(0, 1f));
            Assert.AreEqual(10, GrassField.Count(10, 1f));
            Assert.AreEqual(5, GrassField.Count(10, 0.5f));
            Assert.AreEqual(1, GrassField.Count(3, 0.1f));
            Assert.AreEqual(0, GrassField.Count(3, 0f));
            Assert.AreEqual(10, GrassField.Count(10, 2f), "Never more than placed");
        }

        [Test]
        public void Tuft_IsDeterministic_InsideItsCell_AndWithinThePrototypeSizes()
        {
            GrassField.Tuft(12345, 7, 9, 1, 3, out var a, out float yawA, out float wA, out float hA);
            GrassField.Tuft(12345, 7, 9, 1, 3, out var b, out float yawB, out float wB, out float hB);
            Assert.AreEqual(a, b);
            Assert.AreEqual(yawA, yawB);
            Assert.AreEqual(wA, wB);
            Assert.AreEqual(hA, hB);
            for (int i = 0; i < 200; i++)
            {
                GrassField.Tuft(99, i % 13, i / 13, i % 3, i % 7, out var at, out float yaw, out float w, out float h);
                Assert.That(at.x, Is.InRange(0f, 1f));
                Assert.That(at.y, Is.InRange(0f, 1f));
                Assert.That(yaw, Is.InRange(0f, 360f));
                Assert.That(w, Is.InRange(GrassField.MinWidth, GrassField.MaxWidth));
                Assert.That(h, Is.InRange(GrassField.MinHeight, GrassField.MaxHeight));
            }
            GrassField.Tuft(12345, 7, 9, 1, 4, out var next, out _, out _, out _);
            Assert.AreNotEqual(a, next, "The next tuft in the cell sits somewhere else");
        }

        [Test]
        public void Key_IsUnique_AcrossTilesAndChunks()
        {
            var seen = new System.Collections.Generic.HashSet<long>();
            for (int t = 0; t < 60; t++)
            for (int cz = 0; cz < 64; cz += 7)
            for (int cx = 0; cx < 64; cx += 5)
                Assert.IsTrue(seen.Add(GrassField.Key(t, cx, cz)));
        }
    }
}
