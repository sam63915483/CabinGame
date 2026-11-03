using System.Linq;
using Cabin.Build;
using NUnit.Framework;
using UnityEngine;

namespace Cabin.Tests
{
    public class PaintAtlasTests
    {
        static readonly Vector3 Board = Stock.Size(StockKind.TwoByFour, 8f * Units.Foot);

        static Vector3 FaceCentre(ConvexSolid s, int f)
        {
            Vector3 c = Vector3.zero;
            foreach (int i in s.Faces[f]) c += s.Vertices[i];
            return c / s.Faces[f].Length;
        }

        [Test]
        public void Box_GetsOneChartPerFace_WithinTheTexture_NoOverlaps()
        {
            var s = ConvexSolid.Box(Board);
            var atlas = PaintAtlas.Build(s, Vector3.right);
            Assert.AreEqual(6, atlas.Charts.Count);
            Assert.LessOrEqual(atlas.TexelSize, PaintAtlas.Texel * 1.0001f, "Full resolution for an 8 ft board");
            for (int i = 0; i < 6; i++)
                for (int j = i + 1; j < 6; j++)
                {
                    var a = atlas.TexelRect(i);
                    var b = atlas.TexelRect(j);
                    var inner = new RectInt(a.x + 2, a.y + 2, a.width - 4, a.height - 4);
                    Assert.IsFalse(inner.Overlaps(new RectInt(b.x + 2, b.y + 2, b.width - 4, b.height - 4)), $"Charts {i} and {j} overlap");
                }
            var mesh = s.ToMesh("t", atlas);
            var uv2 = new System.Collections.Generic.List<Vector2>();
            mesh.GetUVs(1, uv2);
            Assert.AreEqual(mesh.vertexCount, uv2.Count);
            Assert.IsTrue(uv2.All(u => u.x >= 0f && u.x <= 1f && u.y >= 0f && u.y <= 1f));
            Object.DestroyImmediate(mesh);
        }

        [Test]
        public void Locate_FindsTheFaceAPointIsOn()
        {
            var s = ConvexSolid.Box(Board);
            var atlas = PaintAtlas.Build(s, Vector3.right);
            for (int f = 0; f < s.Faces.Count; f++)
            {
                Assert.IsTrue(atlas.Locate(FaceCentre(s, f), s.FaceNormal(s.Faces[f]), out int chart, out var texel));
                Assert.AreEqual(f, chart);
                Assert.IsTrue(atlas.TexelRect(f).Contains(texel));
            }
        }

        [Test]
        public void Transfer_KeepsPaintOnSurvivingFaces_CutFaceIsBare()
        {
            var s = ConvexSolid.Box(Board);
            var atlas = PaintAtlas.Build(s, Vector3.right);
            var px = new Color32[atlas.Width * atlas.Height];
            // Paint the whole +z face red.
            int plusZ = Enumerable.Range(0, s.Faces.Count).First(f => s.FaceNormal(s.Faces[f]).z > 0.9f);
            var r = atlas.TexelRect(plusZ);
            for (int y = r.yMin; y < r.yMax; y++)
                for (int x = r.xMin; x < r.xMax; x++)
                    px[y * atlas.Width + x] = new Color32(255, 0, 0, 255);

            var half = s.Clip(new Plane(Vector3.right, Vector3.zero));
            var halfAtlas = PaintAtlas.Build(half, Vector3.right);
            var moved = PaintLayer.Transfer(atlas, px, halfAtlas, Vector3.zero);

            for (int f = 0; f < half.Faces.Count; f++)
            {
                Vector3 n = half.FaceNormal(half.Faces[f]);
                Assert.IsTrue(halfAtlas.Locate(FaceCentre(half, f), n, out _, out var t));
                byte a = moved[t.y * halfAtlas.Width + t.x].a;
                if (n.z > 0.9f) Assert.AreEqual(255, a, "The painted face is still painted");
                else Assert.AreEqual(0, a, $"Face with normal {n} is bare");
            }
        }

        [Test]
        public void Transfer_FollowsARecentre()
        {
            var s = ConvexSolid.Box(Board);
            var atlas = PaintAtlas.Build(s, Vector3.right);
            var px = new Color32[atlas.Width * atlas.Height];
            Vector3 spot = new Vector3(0.5f, 0f, Board.z * 0.5f);
            Assert.IsTrue(atlas.Locate(spot, Vector3.forward, out _, out var t0));
            px[t0.y * atlas.Width + t0.x] = new Color32(0, 0, 255, 255);

            var shifted = s.Clone();
            Vector3 c = new Vector3(0.3f, 0f, 0f);
            shifted.Translate(-c);
            var shiftedAtlas = PaintAtlas.Build(shifted, Vector3.right);
            var moved = PaintLayer.Transfer(atlas, px, shiftedAtlas, c);
            Assert.IsTrue(shiftedAtlas.Locate(spot - c, Vector3.forward, out _, out var t1));
            Assert.AreEqual(255, moved[t1.y * shiftedAtlas.Width + t1.x].a, "Same spot, new frame");
        }
    }
}
