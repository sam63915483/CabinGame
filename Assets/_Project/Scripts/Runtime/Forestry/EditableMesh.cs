using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Cabin.Forestry
{
    /// <summary>
    /// A mesh as plain lists, for splitting triangles along planes without changing its look: the new vertices lie on
    /// the old triangles' edges with every attribute interpolated, so the surface, shading and texturing are exactly as
    /// they were — there are just more vertices to move.
    /// </summary>
    public sealed class EditableMesh
    {
        public readonly List<Vector3> Positions = new List<Vector3>();
        public readonly List<Vector3> Normals = new List<Vector3>();
        public readonly List<Vector4> Tangents = new List<Vector4>();
        public readonly List<Vector4> UV0 = new List<Vector4>();
        public readonly List<Vector4> UV1 = new List<Vector4>();
        public readonly List<Color> Colors = new List<Color>();
        public readonly List<List<int>> Submeshes = new List<List<int>>();

        public int VertexCount => Positions.Count;

        public static EditableMesh From(Mesh mesh)
        {
            var m = new EditableMesh();
            mesh.GetVertices(m.Positions);
            mesh.GetNormals(m.Normals);
            mesh.GetTangents(m.Tangents);
            mesh.GetUVs(0, m.UV0);
            mesh.GetUVs(1, m.UV1);
            mesh.GetColors(m.Colors);
            for (int s = 0; s < mesh.subMeshCount; s++) m.Submeshes.Add(new List<int>(mesh.GetTriangles(s)));
            return m;
        }

        /// <summary>
        /// Splits every triangle that crosses the plane (normal · p = <paramref name="distance"/>) and that
        /// <paramref name="only"/> picks (given its three corners) into pieces on either side. Both sides are kept.
        /// </summary>
        public void Split(Vector3 normal, float distance, Func<Vector3, Vector3, Vector3, bool> only)
        {
            var cuts = new Dictionary<long, int>();
            float Side(int i) => Vector3.Dot(Positions[i], normal) - distance;

            int Cut(int a, int b)
            {
                long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                if (cuts.TryGetValue(key, out int made)) return made;
                float sa = Side(a), sb = Side(b);
                float t = sa / (sa - sb);
                Positions.Add(Vector3.Lerp(Positions[a], Positions[b], t));
                if (Normals.Count > 0)
                {
                    // Opposite normals (a crease) would average to nothing: keep the nearer one's.
                    var n = Vector3.Lerp(Normals[a], Normals[b], t);
                    Normals.Add(n.sqrMagnitude > 1e-6f ? n.normalized : Normals[t < 0.5f ? a : b]);
                }
                if (Tangents.Count > 0) Tangents.Add(Vector4.Lerp(Tangents[a], Tangents[b], t));
                if (UV0.Count > 0) UV0.Add(Vector4.Lerp(UV0[a], UV0[b], t));
                if (UV1.Count > 0) UV1.Add(Vector4.Lerp(UV1[a], UV1[b], t));
                if (Colors.Count > 0) Colors.Add(Color.Lerp(Colors[a], Colors[b], t));
                return cuts[key] = Positions.Count - 1;
            }

            var above = new List<int>(4);
            var below = new List<int>(4);
            foreach (var tris in Submeshes)
            {
                int count = tris.Count;
                var result = new List<int>(count + count / 4);
                for (int i = 0; i < count; i += 3)
                {
                    int a = tris[i], b = tris[i + 1], c = tris[i + 2];
                    float sa = Side(a), sb = Side(b), sc = Side(c);
                    bool crosses = (sa > 0f || sb > 0f || sc > 0f) && (sa < 0f || sb < 0f || sc < 0f);
                    if (!crosses || !only(Positions[a], Positions[b], Positions[c]))
                    {
                        result.Add(a); result.Add(b); result.Add(c);
                        continue;
                    }
                    above.Clear();
                    below.Clear();
                    int[] corner = { a, b, c };
                    for (int k = 0; k < 3; k++)
                    {
                        int p = corner[k], q = corner[(k + 1) % 3];
                        float sp = Side(p), sq = Side(q);
                        if (sp >= 0f) above.Add(p);
                        if (sp <= 0f) below.Add(p);
                        if ((sp > 0f && sq < 0f) || (sp < 0f && sq > 0f))
                        {
                            int m = Cut(p, q);
                            above.Add(m);
                            below.Add(m);
                        }
                    }
                    Fan(above, result);
                    Fan(below, result);
                }
                tris.Clear();
                tris.AddRange(result);
            }
        }

        static void Fan(List<int> poly, List<int> into)
        {
            for (int k = 1; k + 1 < poly.Count; k++)
            {
                into.Add(poly[0]);
                into.Add(poly[k]);
                into.Add(poly[k + 1]);
            }
        }

        public EditableMesh Clone()
        {
            var m = new EditableMesh();
            m.Positions.AddRange(Positions);
            m.Normals.AddRange(Normals);
            m.Tangents.AddRange(Tangents);
            m.UV0.AddRange(UV0);
            m.UV1.AddRange(UV1);
            m.Colors.AddRange(Colors);
            foreach (var tris in Submeshes) m.Submeshes.Add(new List<int>(tris));
            return m;
        }

        /// <summary>A copy of vertex <paramref name="i"/> (every attribute), for a face that needs its own corners; its index.</summary>
        public int CopyVertex(int i)
        {
            Positions.Add(Positions[i]);
            if (Normals.Count > 0) Normals.Add(Normals[i]);
            if (Tangents.Count > 0) Tangents.Add(Tangents[i]);
            if (UV0.Count > 0) UV0.Add(UV0[i]);
            if (UV1.Count > 0) UV1.Add(UV1[i]);
            if (Colors.Count > 0) Colors.Add(Colors[i]);
            return Positions.Count - 1;
        }

        public Mesh ToMesh(Mesh mesh, string name)
        {
            mesh ??= new Mesh();
            mesh.Clear();
            mesh.name = name;
            int n = Positions.Count;
            mesh.indexFormat = n > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(Positions);
            if (Normals.Count == n) mesh.SetNormals(Normals);
            if (Tangents.Count == n) mesh.SetTangents(Tangents);
            if (UV0.Count == n) mesh.SetUVs(0, UV0);
            if (UV1.Count == n) mesh.SetUVs(1, UV1);
            if (Colors.Count == n) mesh.SetColors(Colors);
            mesh.subMeshCount = Submeshes.Count;
            for (int s = 0; s < Submeshes.Count; s++) mesh.SetTriangles(Submeshes[s], s, false);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
