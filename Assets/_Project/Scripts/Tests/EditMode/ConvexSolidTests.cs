using System.Collections.Generic;
using System.Linq;
using Cabin.Build;
using NUnit.Framework;
using UnityEngine;

namespace Cabin.Tests
{
    public class ConvexSolidTests
    {
        static readonly Vector3 TwoByFour = Stock.Size(StockKind.TwoByFour, 8f * Units.Foot);

        [Test]
        public void Box_HasSixFacesEightVertices_AndTheRightVolume()
        {
            var s = ConvexSolid.Box(TwoByFour);
            Assert.AreEqual(6, s.Faces.Count);
            Assert.AreEqual(8, s.Vertices.Count);
            Assert.AreEqual(TwoByFour.x * TwoByFour.y * TwoByFour.z, s.Volume, 1e-7f);
            Assert.AreEqual(0, s.LongestAxis);
            foreach (var f in s.Faces)
                Assert.Greater(Vector3.Dot(s.FaceNormal(f), s.Vertices[f[0]]), 0f, "Faces wind outward");
        }

        [Test]
        public void SquareCut_SplitsVolumeExactly()
        {
            var s = ConvexSolid.Box(TwoByFour);
            var plane = new Plane(Vector3.right, new Vector3(0.3f, 0f, 0f));
            var a = s.Clip(plane);
            var b = s.Clip(new Plane(-plane.normal, -plane.distance));
            Assert.AreEqual(s.Volume, a.Volume + b.Volume, 1e-7f);
            Assert.AreEqual((TwoByFour.x * 0.5f - 0.3f) * TwoByFour.y * TwoByFour.z, a.Volume, 1e-7f);
            Assert.AreEqual(6, a.Faces.Count, "Still a box");
            Assert.AreEqual(8, a.Vertices.Count);
        }

        [Test]
        public void Cut_RemovesTheKerf()
        {
            var s = ConvexSolid.Box(TwoByFour);
            float kerf = 0.125f * Units.Inch;
            s.Cut(new Plane(Vector3.right, Vector3.zero), kerf, out var a, out var b);
            Assert.AreEqual(s.Volume - kerf * TwoByFour.y * TwoByFour.z, a.Volume + b.Volume, 1e-7f);
            Assert.AreEqual(kerf, a.Extent(Vector3.right).x - b.Extent(Vector3.right).y, 1e-6f);
        }

        [Test]
        public void AngledCut_StaysClosedAndConvex()
        {
            var s = ConvexSolid.Box(TwoByFour);
            var normal = new Vector3(1f, 0.6f, 0.3f).normalized;
            var a = s.Clip(new Plane(normal, new Vector3(0.1f, 0.01f, 0f)));
            var b = s.Clip(new Plane(-normal, -new Plane(normal, new Vector3(0.1f, 0.01f, 0f)).distance));
            Assert.AreEqual(s.Volume, a.Volume + b.Volume, 1e-7f);
            AssertClosed(a);
            AssertClosed(b);
            AssertConvex(a);
            AssertConvex(b);
        }

        [Test]
        public void CutThroughACorner_StillValid()
        {
            var s = ConvexSolid.Box(Vector3.one);
            var normal = new Vector3(1f, 1f, 1f).normalized;
            var a = s.Clip(new Plane(normal, new Vector3(0.3f, 0.3f, 0.3f)));
            Assert.IsNotNull(a);
            AssertClosed(a);
            Assert.AreEqual(4, a.Faces.Count, "A tetrahedron off the corner");
        }

        [Test]
        public void CutThatMissesThePiece_ReturnsNullOnTheEmptySide()
        {
            var s = ConvexSolid.Box(TwoByFour);
            var plane = new Plane(Vector3.right, new Vector3(5f, 0f, 0f));
            Assert.IsNull(s.Clip(plane));
            var whole = s.Clip(new Plane(-plane.normal, -plane.distance));
            Assert.AreEqual(s.Volume, whole.Volume, 1e-7f);
        }

        [Test]
        public void ManyCuts_KeepVolumeAccounting()
        {
            var s = ConvexSolid.Box(TwoByFour);
            float total = s.Volume;
            var pieces = new List<ConvexSolid> { s };
            var rng = new System.Random(3);
            for (int i = 0; i < 12; i++)
            {
                var target = pieces[rng.Next(pieces.Count)];
                var normal = new Vector3((float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f).normalized;
                var point = target.Vertices[rng.Next(target.Vertices.Count)] * 0.5f;
                var a = target.Clip(new Plane(normal, point));
                var b = target.Clip(new Plane(-normal, -new Plane(normal, point).distance));
                if (a == null || b == null) continue;
                pieces.Remove(target);
                pieces.Add(a);
                pieces.Add(b);
                AssertClosed(a);
                AssertClosed(b);
            }
            Assert.AreEqual(total, pieces.Sum(p => p.Volume), 1e-6f);
        }

        [Test]
        public void Mesh_HasOneTriangleFanPerFace_AndOutwardNormals()
        {
            var s = ConvexSolid.Box(TwoByFour).Clip(new Plane(new Vector3(1f, 1f, 0f).normalized, Vector3.zero));
            var mesh = s.ToMesh();
            Assert.AreEqual(s.Faces.Sum(f => f.Length - 2) * 3, mesh.triangles.Length);
            var verts = mesh.vertices;
            var norms = mesh.normals;
            Vector3 c = Vector3.zero;
            foreach (var v in s.Vertices) c += v;
            c /= s.Vertices.Count;
            for (int i = 0; i < verts.Length; i++)
                Assert.Greater(Vector3.Dot(norms[i], verts[i] - c), 0f, "Normal points away from the centroid");
            Object.DestroyImmediate(mesh);
        }

        [Test]
        public void Record_RoundTripsThroughJson()
        {
            var s = ConvexSolid.Box(TwoByFour).Clip(new Plane(new Vector3(1f, 0.2f, 0.1f).normalized, new Vector3(0.2f, 0f, 0f)));
            var rec = PieceRecord.From(7, StockKind.TwoByFour, new Vector3(1, 2, 3), Quaternion.Euler(10, 20, 30), s, true, false);
            var back = JsonUtility.FromJson<PieceRecord>(JsonUtility.ToJson(rec)).ToSolid();
            Assert.AreEqual(s.Faces.Count, back.Faces.Count);
            Assert.AreEqual(s.Vertices.Count, back.Vertices.Count);
            Assert.AreEqual(s.Volume, back.Volume, 1e-9f);
        }

        /// <summary>Every edge is shared by exactly two faces, in opposite directions.</summary>
        static void AssertClosed(ConvexSolid s)
        {
            var edges = new Dictionary<(int, int), int>();
            foreach (var f in s.Faces)
                for (int i = 0; i < f.Length; i++)
                {
                    var e = (f[i], f[(i + 1) % f.Length]);
                    edges[e] = edges.TryGetValue(e, out int n) ? n + 1 : 1;
                }
            foreach (var kv in edges)
            {
                Assert.AreEqual(1, kv.Value, $"Edge {kv.Key} used twice in the same direction");
                Assert.IsTrue(edges.ContainsKey((kv.Key.Item2, kv.Key.Item1)), $"Edge {kv.Key} has no twin");
            }
            Assert.AreEqual(2, s.Vertices.Count - edges.Count / 2 + s.Faces.Count, "Euler characteristic");
        }

        static void AssertConvex(ConvexSolid s)
        {
            foreach (var f in s.Faces)
            {
                Vector3 n = s.FaceNormal(f);
                foreach (var v in s.Vertices)
                    Assert.LessOrEqual(Vector3.Dot(v - s.Vertices[f[0]], n), 1e-5f, "All vertices behind every face");
            }
        }
    }
}
