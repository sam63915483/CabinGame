using Cabin.Editor.Map;
using NUnit.Framework;

namespace Cabin.Tests
{
    public class RockScatterTests
    {
        [Test]
        public void NoRocks_InTheSea_OrUnderWater()
        {
            Assert.AreEqual(0f, RockScatter.Density(-2f, 5f, 30f, 1f, 1f));
            Assert.AreEqual(0f, RockScatter.Density(20f, 0.05f, 30f, 1f, 1f));
        }

        [Test]
        public void SteepGround_IsRockier_ThanGentle()
        {
            Assert.Greater(RockScatter.Density(20f, 10f, 38f, 0.3f, 0.3f), RockScatter.Density(20f, 10f, 5f, 0.3f, 0.3f));
        }

        [Test]
        public void BarrenPatches_AndStonyShores_AndHighCountry_HaveRocks()
        {
            float plain = RockScatter.Density(20f, 10f, 5f, 0.3f, 0.3f);
            Assert.Greater(RockScatter.Density(20f, 10f, 5f, 0.85f, 0.3f), plain, "Barren patch");
            Assert.Greater(RockScatter.Density(20f, 1.5f, 5f, 0.3f, 0.9f), plain, "Stony shore");
            Assert.Greater(RockScatter.Density(160f, 10f, 5f, 0.3f, 0.3f), plain, "High country");
            Assert.That(plain, Is.InRange(0.01f, 0.1f), "A few stray boulders in plain woods");
        }
    }
}
