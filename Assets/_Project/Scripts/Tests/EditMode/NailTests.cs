using Cabin.Build;
using NUnit.Framework;

namespace Cabin.Tests
{
    public class NailTests
    {
        const float In = Units.Inch;
        static readonly float Board = Stock.Size(StockKind.TwoByFour, 1f).z;
        static readonly float Post = Stock.Size(StockKind.FourByFour, 1f).z;
        static readonly float Sheet = Stock.Size(StockKind.Plywood, 0f).z;

        [Test]
        public void TwoByFour_IntoAFourByFour_Holds()
        {
            Assert.IsNull(NailRules.Check(exitFirst: Board, enterSecond: Board, out float bite, out float pull));
            Assert.AreEqual(NailRules.NailLength - Board, bite, 1e-6f);
            Assert.AreEqual(0f, pull);
        }

        [Test]
        public void Plywood_IntoAJoist_Holds()
        {
            Assert.IsNull(NailRules.Check(Sheet, Sheet, out _, out _));
        }

        [Test]
        public void ThroughAFourByFour_DoesNotReach()
        {
            Assert.Greater(Post, NailRules.NailLength - NailRules.MinBite, "A 3.5 in nail can't go through a 3.5 in post and still bite");
            StringAssert.Contains("barely", NailRules.Check(Post, Post, out _, out _));
        }

        [Test]
        public void SmallGap_IsNailedAsIs_BiggerGap_IsPulled_HugeGap_Fails()
        {
            Assert.IsNull(NailRules.Check(Board, Board + NailRules.MaxGap * 0.5f, out _, out float pull0));
            Assert.AreEqual(0f, pull0, "Within the free gap: nothing moves");

            float gap = 1.5f * In * Stock.CrossSectionScale;
            Assert.IsNull(NailRules.Check(Board, Board + gap, out float bite, out float pull1));
            Assert.AreEqual(gap, pull1, 1e-6f, "Pulled flush by the gap");
            Assert.AreEqual(NailRules.NailLength - Board, bite, 1e-6f, "Bite is measured after pulling");

            StringAssert.Contains("too far", NailRules.Check(Board, Board + NailRules.PullGap * 1.5f, out _, out _));
        }

        [Test]
        public void TooThick_Fails()
        {
            StringAssert.Contains("thick", NailRules.Check(float.PositiveInfinity, Board, out _, out _));
        }

        [Test]
        public void NailGraph_GroupsNailedPieces()
        {
            var comps = NailGraph.Components(new[] { 1, 2, 3, 4, 5 }, new[] { (1, 2), (2, 3), (4, 5), (9, 1) });
            Assert.AreEqual(2, comps.Count);
            CollectionAssert.AreEquivalent(new[] { 1, 2, 3 }, comps[0]);
            CollectionAssert.AreEquivalent(new[] { 4, 5 }, comps[1]);
        }

        [Test]
        public void NailGraph_PullingANail_SplitsTheGroup()
        {
            var comps = NailGraph.Components(new[] { 1, 2, 3 }, new[] { (1, 2) });
            Assert.AreEqual(2, comps.Count);
        }

        [Test]
        public void Stock_Dimensions_AreReal()
        {
            float k = Stock.CrossSectionScale, l = Stock.LengthScale;
            var s = Stock.Size(StockKind.TwoByFour, 8f * Units.Foot);
            Assert.AreEqual(2.4384f * l, s.x, 1e-5f, "Lengths carry the length scale");
            Assert.AreEqual(8f * Units.Foot, Stock.Nominal(s.x), 1e-5f, "…but read as nominal 8 ft");
            Assert.AreEqual(3.5f * In * k, s.y, 1e-6f);
            Assert.AreEqual(1.5f * In * k, s.z, 1e-6f);
            Assert.AreEqual(3.5f * In * k, Stock.Size(StockKind.FourByFour, 1f).z, 1e-6f);
            Assert.AreEqual(0.5f * In * k, Stock.Size(StockKind.Plywood, 0f).z, 1e-6f);
            Assert.AreEqual(8f * Units.Foot * l, Stock.Size(StockKind.Plywood, 0f).x, 1e-5f);
            Assert.IsTrue(Stock.CanBePost(StockKind.FourByFour));
            Assert.IsFalse(Stock.CanBePost(StockKind.TwoByFour));
            Assert.AreEqual(6, Stock.LumberLengths.Length);
            Assert.AreEqual(20f * Units.Foot, Stock.LumberLengths[^1], 1e-6f);
        }

        [Test]
        public void SquareUp_SnapsToWorldAxes()
        {
            var q = CarryController.SquareUp(UnityEngine.Quaternion.Euler(7f, 86f, -4f));
            var e = q.eulerAngles;
            Assert.AreEqual(0f, UnityEngine.Mathf.Repeat(e.x + 0.5f, 90f) - 0.5f, 1e-3f);
            Assert.AreEqual(0f, UnityEngine.Mathf.Repeat(e.y + 0.5f, 90f) - 0.5f, 1e-3f);
            Assert.AreEqual(0f, UnityEngine.Mathf.Repeat(e.z + 0.5f, 90f) - 0.5f, 1e-3f);
        }

        [Test]
        public void AlignTo_ParallelMatchesGrain_AcrossStandsOnEdge()
        {
            var post = UnityEngine.Quaternion.Euler(0f, 0f, 90f); // grain (x) up
            var parallel = CarryController.AlignTo(post, across: false);
            Assert.Greater(UnityEngine.Vector3.Dot(parallel * UnityEngine.Vector3.right, UnityEngine.Vector3.up), 0.9999f);
            var across = CarryController.AlignTo(post, across: true);
            Assert.Less(UnityEngine.Mathf.Abs(UnityEngine.Vector3.Dot(across * UnityEngine.Vector3.right, UnityEngine.Vector3.up)), 1e-4f, "Grain horizontal");
            Assert.Greater(UnityEngine.Mathf.Abs(UnityEngine.Vector3.Dot(across * UnityEngine.Vector3.up, UnityEngine.Vector3.up)), 0.9999f, "Wide face vertical: on edge");
        }
    }
}
