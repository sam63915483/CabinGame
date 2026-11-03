using Cabin.Core;
using NUnit.Framework;

namespace Cabin.Tests
{
    public class RenderTierTests
    {
        [Test]
        public void MsaaSamples_FollowsTheTier_UnlessSet()
        {
            Assert.AreEqual(4, RenderTier.MsaaSamples(-1, 4));
            Assert.AreEqual(1, RenderTier.MsaaSamples(0, 4), "Off is one sample");
            Assert.AreEqual(8, RenderTier.MsaaSamples(3, 1));
            Assert.AreEqual(8, RenderTier.MsaaSamples(99, 1), "Clamped");
        }

        [Test]
        public void ShadowLevels_OffIsNoDistance_AndTheRestMatchTheTiers()
        {
            Assert.AreEqual(0f, RenderTier.ShadowLevels[0].Distance);
            Assert.AreEqual(50f, RenderTier.ShadowLevels[1].Distance);
            Assert.AreEqual(80f, RenderTier.ShadowLevels[2].Distance);
            Assert.AreEqual(120f, RenderTier.ShadowLevels[3].Distance);
            Assert.AreEqual(2, RenderTier.ShadowLevels[3].Cascades, "Two cascades: one boundary at 36 m instead of two, fewer shadow changes as you walk up");
            Assert.AreEqual(GameSettings.ShadowNames.Length, RenderTier.ShadowLevels.Length);
        }

        [Test]
        public void NearestShadowLevel_PicksTheClosestNamedLevel()
        {
            Assert.AreEqual(0, RenderTier.NearestShadowLevel(0f));
            Assert.AreEqual(1, RenderTier.NearestShadowLevel(45f));
            Assert.AreEqual(2, RenderTier.NearestShadowLevel(90f));
            Assert.AreEqual(3, RenderTier.NearestShadowLevel(150f));
        }
    }
}
