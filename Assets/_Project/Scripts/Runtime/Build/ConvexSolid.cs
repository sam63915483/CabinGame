using System.Collections.Generic;
using UnityEngine;

namespace Cabin.Build
{
    /// <summary>
    /// A convex polyhedron: vertices plus faces as index loops, counter-clockwise seen from outside.
    /// Any straight cut through a convex solid yields two convex solids, so every piece keeps an exact
    /// convex collider no matter how many times it has been sawn.
    /// </summary>
    public sealed class ConvexSolid
    {
        /// <summary>On-plane tolerance; wider than the vertex-merge grid in <see cref="Intern"/> so a vertex never straddles both.</summary>
        const float Eps = 1e-4f;

        public readonly List<Vector3> Vertices = new List<Vector3>();
        public readonly List<int[]> Faces = new List<int[]>();

        public static ConvexSolid Box(Vector3 size)
        {
            var s = new ConvexSolid();
            Vector3 h = size * 0.5f;
            s.Vertices.AddRange(new[]
            {
                new Vector3(-h.x, -h.y, -h.z), new Vector3(h.x, -h.y, -h.z), new Vector3(h.x, h.y, -h.z), new Vector3(-h.x, h.y, -h.z),
                new Vector3(-h.x, -h.y, h.z), new Vector3(h.x, -h.y, h.z), new Vector3(h.x, h.y, h.z), new Vector3(-h.x, h.y, h.z),
            });
            s.Faces.Add(new[] { 0, 3, 2, 1 }); // -z
            s.Faces.Add(new[] { 4, 5, 6, 7 }); // +z
            s.Faces.Add(new[] { 0, 1, 5, 4 }); // -y
            s.Faces.Add(new[] { 3, 7, 6, 2 }); // +y
            s.Faces.Add(new[] { 0, 4, 7, 3 }); // -x
            s.Faces.Add(new[] { 1, 2, 6, 5 }); // +x
            return s;
        }

        /// <summary>A square truncated pyramid standing on -y, centred on the origin: the deck block.</summary>
        public static ConvexSolid Frustum(float baseSize, float topSize, float height)
        {
            var s = new ConvexSolid();
            float b = baseSize * 0.5f, t = topSize * 0.5f, h = height * 0.5f;
            s.Vertices.AddRange(new[]
            {
                new Vector3(-b, -h, -b), new Vector3(b, -h, -b), new Vector3(b, -h, b), new Vector3(-b, -h, b),
                new Vector3(-t, h, -t), new Vector3(t, h, -t), new Vector3(t, h, t), new Vector3(-t, h, t),
            });
            s.Faces.Add(new[] { 0, 1, 2, 3 }); // bottom
            s.Faces.Add(new[] { 4, 7, 6, 5 }); // top
            s.Faces.Add(new[] { 0, 4, 5, 1 }); // -z
            s.Faces.Add(new[] { 1, 5, 6, 2 }); // +x
            s.Faces.Add(new[] { 2, 6, 7, 3 }); // +z
            s.Faces.Add(new[] { 3, 7, 4, 0 }); // -x
            return s;
        }

        public Bounds Bounds
        {
            get
            {
                if (Vertices.Count == 0) return new Bounds();
                var b = new Bounds(Vertices[0], Vector3.zero);
                foreach (var v in Vertices) b.Encapsulate(v);
                return b;
            }
        }

        /// <summary>Index (0 = x, 1 = y, 2 = z) of the longest bounds axis: the grain direction for lumber.</summary>
        public int LongestAxis
        {
            get
            {
                Vector3 s = Bounds.size;
                return s.x >= s.y && s.x >= s.z ? 0 : s.y >= s.z ? 1 : 2;
            }
        }

        /// <summary>Signed volume by the divergence theorem over fan-triangulated faces.</summary>
        public float Volume
        {
            get
            {
                float v = 0f;
                foreach (var f in Faces)
                    for (int i = 1; i + 1 < f.Length; i++)
                        v += Vector3.Dot(Vertices[f[0]], Vector3.Cross(Vertices[f[i]], Vertices[f[i + 1]]));
                return v / 6f;
            }
        }

        public Vector3 FaceNormal(int[] face) => AreaNormal(face).normalized;

        /// <summary>Newell's method: the face normal scaled by twice its area. Robust for any planar polygon.</summary>
        Vector3 AreaNormal(int[] face)
        {
            Vector3 n = Vector3.zero;
            for (int i = 0; i < face.Length; i++)
            {
                Vector3 a = Vertices[face[i]], b = Vertices[face[(i + 1) % face.Length]];
                n.x += (a.y - b.y) * (a.z + b.z);
                n.y += (a.z - b.z) * (a.x + b.x);
                n.z += (a.x - b.x) * (a.y + b.y);
            }
            return n;
        }

        /// <summary>Extent of the solid along a direction: (min, max) of the projection.</summary>
        public Vector2 Extent(Vector3 direction)
        {
            float lo = float.MaxValue, hi = float.MinValue;
            foreach (var v in Vertices)
            {
                float d = Vector3.Dot(v, direction);
                if (d < lo) lo = d;
                if (d > hi) hi = d;
            }
            return new Vector2(lo, hi);
        }

        public bool Contains(Vector3 point, float tolerance = 1e-4f)
        {
            foreach (var f in Faces)
                if (Vector3.Dot(point - Vertices[f[0]], FaceNormal(f)) > tolerance) return false;
            return true;
        }

        /// <summary>
        /// The part of the solid on the positive side of the plane (where <c>plane.GetDistanceToPoint &gt;= 0</c>),
        /// closed with a new cap face. Null when nothing of substance is on that side.
        /// </summary>
        public ConvexSolid Clip(Plane plane)
        {
            var result = new ConvexSolid();
            var index = new Dictionary<(int, int, int), int>();
            var capIds = new HashSet<int>();
            var dist = new float[Vertices.Count];
            for (int i = 0; i < Vertices.Count; i++) dist[i] = plane.GetDistanceToPoint(Vertices[i]);

            foreach (var face in Faces)
            {
                var loop = new List<int>();
                for (int i = 0; i < face.Length; i++)
                {
                    int ic = face[i], inx = face[(i + 1) % face.Length];
                    float dc = dist[ic], dn = dist[inx];
                    bool curOn = Mathf.Abs(dc) <= Eps, nextOn = Mathf.Abs(dn) <= Eps;
                    bool curIn = dc > Eps, nextIn = dn > Eps;
                    if (curIn || curOn)
                    {
                        int id = result.Intern(Vertices[ic], index);
                        loop.Add(id);
                        if (curOn) capIds.Add(id);
                    }
                    // Vertices on the plane are their own intersection; only a true crossing makes a new point.
                    if (!curOn && !nextOn && curIn != nextIn)
                    {
                        int id = result.Intern(Intersect(ic, inx, dist), index);
                        loop.Add(id);
                        capIds.Add(id);
                    }
                }
                result.AddFace(loop);
            }
            if (result.Faces.Count == 0) return null;

            if (capIds.Count >= 3)
                result.AddFace(result.OrderAround(new List<int>(capIds), -plane.normal));
            return result.Volume > 1e-9f ? result : null;
        }

        /// <summary>Where edge (a, b) crosses the plane, computed the same way whichever face asks.</summary>
        Vector3 Intersect(int a, int b, float[] dist)
        {
            if (a > b) (a, b) = (b, a);
            Vector3 pa = Vertices[a], pb = Vertices[b];
            float t = dist[a] / (dist[a] - dist[b]);
            return pa + (pb - pa) * t;
        }

        /// <summary>Both halves of a cut, with a saw kerf removed between them. Either may be null.</summary>
        public void Cut(Plane plane, float kerf, out ConvexSolid positive, out ConvexSolid negative)
        {
            float half = kerf * 0.5f;
            positive = Clip(new Plane(plane.normal, plane.distance - half));
            negative = Clip(new Plane(-plane.normal, -plane.distance - half));
        }

        void AddFace(List<int> ids)
        {
            var loop = new List<int>();
            foreach (int id in ids)
                if (loop.Count == 0 || loop[loop.Count - 1] != id) loop.Add(id);
            while (loop.Count > 1 && loop[0] == loop[loop.Count - 1]) loop.RemoveAt(loop.Count - 1);
            if (loop.Count < 3) return;
            var arr = loop.ToArray();
            if (AreaNormal(arr).sqrMagnitude < 1e-16f) return; // no area at all: collinear
            Faces.Add(arr);
        }

        int Intern(Vector3 p, Dictionary<(int, int, int), int> index)
        {
            var key = (Mathf.RoundToInt(p.x * 20000f), Mathf.RoundToInt(p.y * 20000f), Mathf.RoundToInt(p.z * 20000f));
            if (index.TryGetValue(key, out int id)) return id;
            id = Vertices.Count;
            Vertices.Add(p);
            index[key] = id;
            return id;
        }

        /// <summary>Orders coplanar vertices counter-clockwise as seen from <paramref name="outward"/>.</summary>
        List<int> OrderAround(List<int> ids, Vector3 outward)
        {
            Vector3 c = Vector3.zero;
            foreach (int id in ids) c += Vertices[id];
            c /= ids.Count;
            Vector3 u = Vector3.Cross(outward, Mathf.Abs(outward.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            Vector3 v = Vector3.Cross(outward, u);
            ids.Sort((a, b) =>
            {
                float aa = Mathf.Atan2(Vector3.Dot(Vertices[a] - c, v), Vector3.Dot(Vertices[a] - c, u));
                float ab = Mathf.Atan2(Vector3.Dot(Vertices[b] - c, v), Vector3.Dot(Vertices[b] - c, u));
                return aa.CompareTo(ab);
            });
            // Sorting by angle in the (u, v) basis gives counter-clockwise about u×v. Flip if that isn't outward.
            if (Vector3.Dot(Vector3.Cross(u, v), outward) < 0f) ids.Reverse();
            return ids;
        }

        /// <summary>
        /// Flat-shaded mesh. UV0 runs along the grain (longest axis) in metres so textures tile in world scale; UV1,
        /// when an atlas is given, is each point's unique spot in the paint texture.
        /// </summary>
        public Mesh ToMesh(string name = "Piece", PaintAtlas atlas = null)
        {
            int grain = LongestAxis;
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var uv2 = new List<Vector2>();
            var tris = new List<int>();
            for (int fi = 0; fi < Faces.Count; fi++)
            {
                var face = Faces[fi];
                Vector3 n = FaceNormal(face);
                int across = AcrossAxis(n, grain);
                int uAxis = Mathf.Abs(n[grain]) > 0.9f ? (grain + 1) % 3 : grain;
                int start = verts.Count;
                foreach (int i in face)
                {
                    Vector3 p = Vertices[i];
                    verts.Add(p);
                    normals.Add(n);
                    uvs.Add(new Vector2(p[uAxis], p[across]));
                    if (atlas != null) uv2.Add(atlas.UV(fi, p));
                }
                for (int i = 1; i + 1 < face.Length; i++)
                    tris.AddRange(new[] { start, start + i, start + i + 1 });
            }
            var mesh = new Mesh { name = name };
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            if (atlas != null) mesh.SetUVs(1, uv2);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }

        static int AcrossAxis(Vector3 normal, int grain)
        {
            int a = (grain + 1) % 3, b = (grain + 2) % 3;
            if (Mathf.Abs(normal[grain]) > 0.9f) return b;
            return Mathf.Abs(normal[a]) < Mathf.Abs(normal[b]) ? a : b;
        }

        /// <summary>Moves every vertex by an offset (re-centring a piece after a cut).</summary>
        public void Translate(Vector3 offset)
        {
            for (int i = 0; i < Vertices.Count; i++) Vertices[i] += offset;
        }

        public ConvexSolid Clone()
        {
            var c = new ConvexSolid();
            c.Vertices.AddRange(Vertices);
            foreach (var f in Faces) c.Faces.Add((int[])f.Clone());
            return c;
        }
    }
}
