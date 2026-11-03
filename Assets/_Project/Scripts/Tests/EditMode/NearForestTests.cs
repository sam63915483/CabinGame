using Cabin.Forestry;
using NUnit.Framework;
using UnityEngine;

namespace Cabin.Tests
{
    public class NearForestTests
    {
        [Test]
        public void Place_PutsATreeWhereTheTerrainWould()
        {
            var inst = new TreeInstance { position = new Vector3(0.25f, 0.1f, 0.5f), rotation = Mathf.PI / 2f, widthScale = 1.2f, heightScale = 1.5f };
            NearForest.Place(inst, new Vector3(1000f, 0f, -2000f), new Vector3(1024f, 300f, 1024f), out var p, out var r, out var s);
            Assert.AreEqual(1256f, p.x, 1e-3f);
            Assert.AreEqual(30f, p.y, 1e-3f);
            Assert.AreEqual(-1488f, p.z, 1e-3f);
            Assert.AreEqual(90f, r.eulerAngles.y, 1e-3f);
            Assert.AreEqual(new Vector3(1.2f, 1.5f, 1.2f), s);
        }

        [Test]
        public void Cell_IsSixtyFourMetres_AndKeysAreUnique()
        {
            var size = new Vector3(1024f, 300f, 1024f);
            Assert.AreEqual(new Vector2Int(0, 0), NearForest.Cell(new Vector3(0.01f, 0f, 0.01f), size));
            Assert.AreEqual(new Vector2Int(15, 15), NearForest.Cell(new Vector3(0.999f, 0f, 0.999f), size));
            Assert.AreEqual(new Vector2Int(1, 0), NearForest.Cell(new Vector3(64.5f / 1024f, 0f, 0f), size));
            var seen = new System.Collections.Generic.HashSet<long>();
            for (int t = 0; t < 60; t++)
            for (int x = 0; x < 16; x++)
            for (int z = 0; z < 16; z++)
                Assert.IsTrue(seen.Add(NearForest.Key(t, new Vector2Int(x, z))));
        }
    }
}
