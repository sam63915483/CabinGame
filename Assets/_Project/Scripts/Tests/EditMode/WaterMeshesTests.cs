using System.Collections.Generic;
using Cabin.Editor.Map;
using NUnit.Framework;
using UnityEngine;
using Grid = Cabin.Editor.Map.Grid;

namespace Cabin.Tests
{
    public class WaterMeshesTests
    {
        static River Straight(int points, float drop, Fall fall = null)
        {
            var r = new River();
            for (int i = 0; i < points; i++)
            {
                r.Points.Add(new Vector2(10f + i * 2f, 20f));
                r.Levels.Add(10f - i * drop);
                r.Widths.Add(4f);
                r.Depths.Add(1f);
            }
            if (fall != null) r.Falls.Add(fall);
            return r;
        }

        static Grid Ground(float height) => new Grid(40, 40, 1f, Vector2.zero, height);

        [Test]
        public void Ribbon_ThreeVerticesPerPoint_LevelsMatch_NoNaN()
        {
            var river = Straight(10, 0.1f);
            var mesh = WaterMeshes.Rivers(new List<River> { river }, Ground(8f), new Rect(0f, 0f, 40f, 40f), "R");
            Assert.IsNotNull(mesh);
            var verts = mesh.vertices;
            Assert.AreEqual(30, verts.Length);
            for (int i = 0; i < verts.Length; i++)
            {
                Assert.IsFalse(float.IsNaN(verts[i].x) || float.IsNaN(verts[i].y) || float.IsNaN(verts[i].z), "No NaN");
                Assert.AreEqual(river.Levels[i / 3], verts[i].y, 1e-4f, "At the river's level");
            }
            Assert.AreEqual(9 * 4 * 3, mesh.triangles.Length, "Four triangles between each pair of cross-sections");
            var uv = new List<Vector2>();
            mesh.GetUVs(2, uv);
            Assert.AreEqual(1f, uv[1].x, 1e-4f, "Centre depth is the river's depth");
            Assert.AreEqual(0f, uv[1].y, 1e-4f, "No fall");
            Object.DestroyImmediate(mesh);
        }

        [Test]
        public void Fall_VerticesCarryFallness_OnlyOnTheSheet()
        {
            var fall = new Fall { LipIndex = 4, BaseIndex = 6, LipLevel = 9.6f, BaseLevel = 4f };
            var river = Straight(12, 0.1f, fall);
            var mesh = WaterMeshes.Rivers(new List<River> { river }, Ground(2f), new Rect(0f, 0f, 40f, 40f), "R");
            var uv = new List<Vector2>();
            mesh.GetUVs(2, uv);
            Assert.AreEqual(1f, uv[4 * 3 + 1].y, 1e-4f, "On the sheet at the lip");
            Assert.AreEqual(1f, uv[5 * 3 + 1].y, 1e-4f, "On the sheet between");
            Assert.AreEqual(1f, uv[6 * 3 + 1].y, 1e-4f, "On the sheet at the base");
            Assert.AreEqual(0f, uv[0 * 3 + 1].y, 1e-4f, "Far upstream: running water");
            Assert.AreEqual(0f, uv[11 * 3 + 1].y, 1e-4f, "Far downstream: running water");
            Assert.That(uv[3 * 3 + 1].y, Is.InRange(0.01f, 0.99f), "Fading in just above the lip");
            Object.DestroyImmediate(mesh);
        }

        [Test]
        public void OutsideTheTile_NothingIsBuilt()
        {
            var river = Straight(10, 0.1f);
            Assert.IsNull(WaterMeshes.Rivers(new List<River> { river }, Ground(8f), new Rect(200f, 200f, 40f, 40f), "R"));
        }
    }
}
