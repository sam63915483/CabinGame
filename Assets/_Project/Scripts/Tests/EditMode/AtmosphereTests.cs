using Cabin.Core;
using NUnit.Framework;
using UnityEngine;

namespace Cabin.Tests
{
    public class AtmosphereTests
    {
        [Test]
        public void Noise_TilesSeamlessly_AndStaysInRange()
        {
            for (int i = 0; i < 50; i++)
            {
                float v = i / 50f;
                Assert.AreEqual(AtmosphereNoise.Fbm(0f, v, 4, 5, 7), AtmosphereNoise.Fbm(1f, v, 4, 5, 7), 1e-5f, "Left edge meets right edge");
                Assert.AreEqual(AtmosphereNoise.Fbm(v, 0f, 4, 5, 7), AtmosphereNoise.Fbm(v, 1f, 4, 5, 7), 1e-5f, "Bottom meets top");
                float f = AtmosphereNoise.Fbm(v, v * 0.37f, 4, 5, 7);
                Assert.That(f, Is.InRange(0f, 1f));
            }
            Assert.AreNotEqual(AtmosphereNoise.Fbm(0.2f, 0.3f, 4, 5, 7), AtmosphereNoise.Fbm(0.2f, 0.3f, 4, 5, 8), "The seed matters");
        }

        [Test]
        public void SunVisibility_FullOnScreen_FadesOutside_ZeroBehind()
        {
            Assert.AreEqual(1f, Atmosphere.SunVisibility(new Vector3(0.5f, 0.5f, 1f)));
            Assert.AreEqual(0.5f, Atmosphere.SunVisibility(new Vector3(1.25f, 0.5f, 1f)), 1e-5f);
            Assert.AreEqual(0f, Atmosphere.SunVisibility(new Vector3(-0.6f, 0.5f, 1f)));
            Assert.AreEqual(0f, Atmosphere.SunVisibility(new Vector3(0.5f, 0.5f, -1f)), "Behind the camera");
        }

        [Test]
        public void BlurSteps_ComposeToTheFullLength_AndTheEndWeight()
        {
            const float length = 0.8f, endWeight = 0.12f;
            float reach = 0f, weight = 1f;
            for (int k = 0; k < AtmosphereFeature.BlurIterations; k++)
            {
                var step = AtmosphereFeature.BlurStep(k, length, endWeight);
                reach += step.x * (AtmosphereFeature.BlurTaps - 1);
                weight *= Mathf.Pow(step.z, AtmosphereFeature.BlurTaps - 1);
                if (k > 0) Assert.AreEqual(AtmosphereFeature.BlurTaps, step.x / AtmosphereFeature.BlurStep(k - 1, length, endWeight).x, 1e-4f, "Each iteration's step is the taps times the last");
            }
            Assert.AreEqual(512, AtmosphereFeature.TotalTaps);
            Assert.AreEqual(length, reach, length * 0.01f, "The composed taps span the length (bar the last tap)");
            Assert.AreEqual(endWeight, weight, endWeight * 0.05f, "The far end weighs the end weight");
        }

        [Test]
        public void Axes_AreUnitAndHorizontal_WithTheSunsElevation()
        {
            var go = new GameObject("sun");
            go.transform.rotation = Quaternion.Euler(36f, -128f, 0f);
            Atmosphere.Axes(go.transform, out var r, out var u, out float len);
            Assert.AreEqual(1f, r.magnitude, 1e-4f);
            Assert.AreEqual(1f, u.magnitude, 1e-4f);
            Assert.AreEqual(0f, Vector2.Dot(r, u), 1e-3f, "Perpendicular on the ground");
            Assert.AreEqual(Mathf.Sin(36f * Mathf.Deg2Rad), len, 1e-3f, "The up vector of a light pitched 36 degrees down has a ground-projected length of sin 36");
            Object.DestroyImmediate(go);
        }
    }
}
