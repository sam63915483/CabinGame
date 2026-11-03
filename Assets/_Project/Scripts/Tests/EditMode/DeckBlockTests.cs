using System.Linq;
using Cabin.Build;
using NUnit.Framework;
using UnityEngine;

namespace Cabin.Tests
{
    public class DeckBlockTests
    {
        [Test]
        public void Frustum_HasTheRightVolume_AndWindsOutward()
        {
            float b = 1f, t = 0.6f, h = 0.5f;
            var s = ConvexSolid.Frustum(b, t, h);
            float expected = h / 3f * (b * b + b * t + t * t);
            Assert.AreEqual(expected, s.Volume, 1e-6f);
            Assert.AreEqual(6, s.Faces.Count);
            foreach (var f in s.Faces)
                Assert.Greater(Vector3.Dot(s.FaceNormal(f), s.Vertices[f[0]]), 0f, "Faces wind outward");
            Assert.AreEqual(h * 0.5f, s.Extent(Vector3.up).y, 1e-6f, "Top face at +h/2");
        }

        [Test]
        public void DeckBlock_ScalesWithThePosts_AndSocketFitsA4x4()
        {
            var block = Stock.Shape(StockKind.DeckBlock, 0f);
            Assert.AreEqual(Stock.DeckBlockBase, block.Extent(Vector3.right).y * 2f, 1e-6f);
            Assert.AreEqual(Stock.DeckBlockHeight, block.Extent(Vector3.up).y - block.Extent(Vector3.up).x, 1e-6f);
            float post = Stock.Size(StockKind.FourByFour, 1f).z;
            Assert.Greater(Stock.DeckBlockTop, post, "The top face is wider than the post that seats in it");
            Assert.Greater(Stock.DeckBlockHeight, Stock.DeckBlockSocketDepth);
            Assert.IsTrue(Stock.IsConcrete(StockKind.DeckBlock));
            Assert.IsFalse(Stock.CanBePost(StockKind.DeckBlock));
            Assert.Greater(Stock.DeckBlockBase * Stock.DeckBlockBase * Stock.DeckBlockHeight * Stock.ConcreteDensity * 0.5f, 20f, "Heavy enough to matter");
        }

        [Test]
        public void Record_RoundTripsSeating()
        {
            var rec = PieceRecord.From(3, StockKind.FourByFour, Vector3.zero, Quaternion.identity, Stock.Shape(StockKind.FourByFour, 2f), true, false, seatedIn: 7);
            var back = JsonUtility.FromJson<PieceRecord>(JsonUtility.ToJson(rec));
            Assert.AreEqual(7, back.seatedIn);
            Assert.IsTrue(back.planted);
        }
    }
}
