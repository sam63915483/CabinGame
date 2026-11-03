using System.Collections.Generic;
using Cabin.Build;
using UnityEngine;

namespace Cabin.Editor
{
    /// <summary>
    /// Small hard-surface modelling kit for props built in code: chamfered and tapered convex blocks (via
    /// <see cref="ConvexSolid"/> clipping) and lathed (turned) parts with smooth sides and crisp profile edges.
    /// </summary>
    public static class ModelKit
    {
        /// <summary>A box with every edge chamfered by <paramref name="bevel"/>.</summary>
        public static ConvexSolid Chamfered(Vector3 size, float bevel)
        {
            var s = ConvexSolid.Box(size);
            Vector3 h = size * 0.5f;
            for (int a = 0; a < 3; a++)
                for (int b = a + 1; b < 3; b++)
                    foreach (float sa in new[] { -1f, 1f })
                        foreach (float sb in new[] { -1f, 1f })
                        {
                            var n = Vector3.zero;
                            n[a] = sa;
                            n[b] = sb;
                            n.Normalize();
                            var p = Vector3.zero;
                            p[a] = sa * (h[a] - bevel);
                            p[b] = sb * h[b];
                            s = s.Clip(new Plane(-n, p)) ?? s;
                        }
            return s;
        }

        /// <summary>A chamfered box whose +z end is shrunk to <paramref name="endScale"/> (x, y) — a tapering nose.</summary>
        public static ConvexSolid Tapered(Vector3 size, Vector2 endScale, float bevel)
        {
            var s = Chamfered(size, bevel);
            Vector3 h = size * 0.5f;
            // Slope the four long sides in towards +z.
            for (int a = 0; a < 2; a++)
                foreach (float sign in new[] { -1f, 1f })
                {
                    float inset = h[a] * (1f - endScale[a]);
                    var start = Vector3.zero; start[a] = sign * h[a]; start.z = -h.z;
                    var end = Vector3.zero; end[a] = sign * (h[a] - inset); end.z = h.z;
                    Vector3 along = (end - start).normalized;
                    var side = Vector3.zero; side[a] = sign;
                    Vector3 n = (side - along * Vector3.Dot(side, along)).normalized;
                    s = s.Clip(new Plane(-n, start)) ?? s;
                }
            return s;
        }

        /// <summary>Flat-shaded mesh of a convex solid, optionally moved and turned.</summary>
        public static Mesh Solid(string name, ConvexSolid solid) => solid.ToMesh(name);

        /// <summary>
        /// A turned part: <paramref name="profile"/> is (radius, height) from bottom to top, revolved about +y.
        /// Smooth around, crisp along the profile. Ends at radius 0 close themselves; otherwise caps are added.
        /// </summary>
        public static Mesh Lathe(string name, IList<Vector2> profile, int segments = 40, bool capBottom = true, bool capTop = true)
        {
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();

            for (int i = 0; i + 1 < profile.Count; i++)
            {
                Vector2 p0 = profile[i], p1 = profile[i + 1];
                Vector2 d = p1 - p0;
                if (d.sqrMagnitude < 1e-12f) continue;
                var n2 = new Vector2(d.y, -d.x).normalized; // outward in the (r, y) plane for an upward profile
                int start = verts.Count;
                for (int s = 0; s <= segments; s++)
                {
                    float a = s / (float)segments * Mathf.PI * 2f;
                    float c = Mathf.Cos(a), sn = Mathf.Sin(a);
                    verts.Add(new Vector3(p0.x * c, p0.y, p0.x * sn));
                    verts.Add(new Vector3(p1.x * c, p1.y, p1.x * sn));
                    var n = new Vector3(n2.x * c, n2.y, n2.x * sn);
                    normals.Add(n);
                    normals.Add(n);
                    uvs.Add(new Vector2(s / (float)segments, i / (float)profile.Count));
                    uvs.Add(new Vector2(s / (float)segments, (i + 1) / (float)profile.Count));
                }
                for (int s = 0; s < segments; s++)
                {
                    int a = start + s * 2;
                    AddOutward(verts, normals, tris, a, a + 2, a + 1);
                    AddOutward(verts, normals, tris, a + 1, a + 2, a + 3);
                }
            }
            if (capBottom && profile[0].x > 1e-5f) Cap(verts, normals, uvs, tris, profile[0], Vector3.down, segments);
            if (capTop && profile[profile.Count - 1].x > 1e-5f) Cap(verts, normals, uvs, tris, profile[profile.Count - 1], Vector3.up, segments);

            var mesh = new Mesh { name = name };
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }

        static void Cap(List<Vector3> verts, List<Vector3> normals, List<Vector2> uvs, List<int> tris, Vector2 ring, Vector3 normal, int segments)
        {
            int c = verts.Count;
            verts.Add(new Vector3(0f, ring.y, 0f));
            normals.Add(normal);
            uvs.Add(new Vector2(0.5f, 0.5f));
            for (int s = 0; s <= segments; s++)
            {
                float a = s / (float)segments * Mathf.PI * 2f;
                verts.Add(new Vector3(ring.x * Mathf.Cos(a), ring.y, ring.x * Mathf.Sin(a)));
                normals.Add(normal);
                uvs.Add(new Vector2(Mathf.Cos(a) * 0.5f + 0.5f, Mathf.Sin(a) * 0.5f + 0.5f));
            }
            for (int s = 0; s < segments; s++) AddOutward(verts, normals, tris, c, c + 1 + s, c + 2 + s);
        }

        /// <summary>Adds a triangle wound so its face normal agrees with the vertex normals (Unity: clockwise = front).</summary>
        static void AddOutward(List<Vector3> verts, List<Vector3> normals, List<int> tris, int a, int b, int c)
        {
            Vector3 face = Vector3.Cross(verts[b] - verts[a], verts[c] - verts[a]);
            Vector3 avg = normals[a] + normals[b] + normals[c];
            if (Vector3.Dot(face, avg) >= 0f) tris.AddRange(new[] { a, b, c });
            else tris.AddRange(new[] { a, c, b });
        }

        /// <summary>A plain cylinder along +y, centred, for small round details.</summary>
        public static Mesh Cylinder(string name, float radius, float height, int segments = 24) =>
            Lathe(name, new[] { new Vector2(radius, -height * 0.5f), new Vector2(radius, height * 0.5f) }, segments);
    }
}
