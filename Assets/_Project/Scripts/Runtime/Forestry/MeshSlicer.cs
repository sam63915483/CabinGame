using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Cabin.Forestry
{
    /// <summary>
    /// Cuts a mesh with a horizontal plane and keeps one side: triangles across the plane are clipped, the new
    /// vertices interpolated (position, normal, tangent, UVs, colour) along the cut edges. No cap — what's cut away is
    /// replaced by the chop band. Mesh space.
    /// </summary>
    public static class MeshSlicer
    {
        /// <summary>Where the plane cut one of the mesh's edges: the point, the submesh it was on, its texture coordinate.</summary>
        public struct EdgePoint
        {
            public Vector3 Position;
            public int Submesh;
            public Vector2 UV;
        }

        /// <summary>Just the points where y = <paramref name="height"/> cuts the mesh's edges (each edge once), into <paramref name="edge"/>.</summary>
        public static void Outline(Mesh source, float height, List<EdgePoint> edge)
        {
            var pos = new List<Vector3>(); source.GetVertices(pos);
            var uv0 = new List<Vector2>(); source.GetUVs(0, uv0);
            var seen = new HashSet<long>();
            for (int s = 0; s < source.subMeshCount; s++)
            {
                var tris = source.GetTriangles(s);
                for (int i = 0; i < tris.Length; i += 3)
                for (int k = 0; k < 3; k++)
                {
                    int a = tris[i + k], b = tris[i + (k + 1) % 3];
                    float da = pos[a].y - height, db = pos[b].y - height;
                    if ((da >= 0f) == (db >= 0f)) continue;
                    long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                    if (!seen.Add(key)) continue;
                    float t = da / (da - db);
                    edge.Add(new EdgePoint
                    {
                        Position = Vector3.Lerp(pos[a], pos[b], t),
                        Submesh = s,
                        UV = uv0.Count > 0 ? Vector2.Lerp(uv0[a], uv0[b], t) : Vector2.zero,
                    });
                }
            }
        }

        /// <summary>
        /// The part of <paramref name="source"/> above (or below) y = <paramref name="height"/>. The points where the
        /// plane cut through go in <paramref name="edge"/>, if given (the outline of whatever it went through).
        /// </summary>
        public static Mesh Keep(Mesh source, float height, bool above, string name, List<EdgePoint> edge = null)
        {
            var pos = new List<Vector3>(); source.GetVertices(pos);
            var nrm = new List<Vector3>(); source.GetNormals(nrm);
            var tan = new List<Vector4>(); source.GetTangents(tan);
            var uv0 = new List<Vector4>(); source.GetUVs(0, uv0);
            var uv1 = new List<Vector4>(); source.GetUVs(1, uv1);
            var col = new List<Color>(); source.GetColors(col);
            int original = pos.Count;
            var side = new float[original];
            for (int i = 0; i < original; i++) side[i] = above ? pos[i].y - height : height - pos[i].y;
            var sides = new List<float>(side);
            var cuts = new Dictionary<long, int>();
            int sub = 0;

            int Cut(int a, int b)
            {
                long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                if (cuts.TryGetValue(key, out int made)) return made;
                float t = sides[a] / (sides[a] - sides[b]);
                pos.Add(Vector3.Lerp(pos[a], pos[b], t));
                if (nrm.Count > 0) nrm.Add(Vector3.Lerp(nrm[a], nrm[b], t).normalized);
                if (tan.Count > 0) tan.Add(Vector4.Lerp(tan[a], tan[b], t));
                if (uv0.Count > 0) uv0.Add(Vector4.Lerp(uv0[a], uv0[b], t));
                if (uv1.Count > 0) uv1.Add(Vector4.Lerp(uv1[a], uv1[b], t));
                if (col.Count > 0) col.Add(Color.Lerp(col[a], col[b], t));
                sides.Add(0f);
                edge?.Add(new EdgePoint { Position = pos[pos.Count - 1], Submesh = sub, UV = uv0.Count > 0 ? (Vector2)uv0[uv0.Count - 1] : Vector2.zero });
                return cuts[key] = pos.Count - 1;
            }

            var subs = new List<int>[source.subMeshCount];
            var poly = new List<int>(4);
            for (int s = 0; s < source.subMeshCount; s++)
            {
                sub = s;
                var tris = source.GetTriangles(s);
                var kept = subs[s] = new List<int>();
                for (int i = 0; i < tris.Length; i += 3)
                {
                    poly.Clear();
                    for (int k = 0; k < 3; k++)
                    {
                        int a = tris[i + k], b = tris[i + (k + 1) % 3];
                        if (sides[a] >= 0f) poly.Add(a);
                        if ((sides[a] >= 0f) != (sides[b] >= 0f)) poly.Add(Cut(a, b));
                    }
                    for (int k = 1; k + 1 < poly.Count; k++) kept.AddRange(new[] { poly[0], poly[k], poly[k + 1] });
                }
            }

            var mesh = new Mesh { name = name, indexFormat = pos.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(pos);
            if (nrm.Count > 0) mesh.SetNormals(nrm);
            if (tan.Count > 0) mesh.SetTangents(tan);
            if (uv0.Count > 0) mesh.SetUVs(0, uv0);
            if (uv1.Count > 0) mesh.SetUVs(1, uv1);
            if (col.Count > 0) mesh.SetColors(col);
            mesh.subMeshCount = subs.Length;
            for (int s = 0; s < subs.Length; s++) mesh.SetTriangles(subs[s], s);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
