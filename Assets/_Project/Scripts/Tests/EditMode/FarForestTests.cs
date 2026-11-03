using Cabin.Forestry;
using NUnit.Framework;
using UnityEngine;

namespace Cabin.Tests
{
    public class FarForestTests
    {
        [Test]
        public void AtlasCells_TileTheAtlas_WithoutOverlap_RowZeroAtTheTop()
        {
            const int views = 8, columns = 16, rows = 8;
            var seen = new System.Collections.Generic.HashSet<Vector2>();
            for (int s = 0; s < columns * rows / views; s++)
            for (int v = 0; v < views; v++)
            {
                var r = FarForestData.Cell(s, v, views, columns, rows);
                Assert.AreEqual(1f / columns, r.width, 1e-6f);
                Assert.AreEqual(1f / rows, r.height, 1e-6f);
                Assert.That(r.xMin, Is.InRange(0f, 1f - r.width + 1e-6f));
                Assert.That(r.yMin, Is.InRange(0f, 1f - r.height + 1e-6f));
                Assert.IsTrue(seen.Add(new Vector2(Mathf.Round(r.x * 1000f), Mathf.Round(r.y * 1000f))), "Every cell is its own");
            }
            Assert.AreEqual(1f - 1f / rows, FarForestData.Cell(0, 0, views, columns, rows).y, 1e-6f, "First cell at the top-left");
            Assert.AreEqual(0f, FarForestData.Cell(0, 0, views, columns, rows).x, 1e-6f);
            Assert.AreEqual(1f / columns, FarForestData.Cell(0, 1, views, columns, rows).x, 1e-6f, "Next view to the right");
            Assert.AreEqual(1f - 2f / rows, FarForestData.Cell(2, 0, views, columns, rows).y, 1e-6f, "Third species starts the second row");
        }
    }
}
