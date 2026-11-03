using System.Collections.Generic;
using UnityEngine;

namespace Cabin.Forestry
{
    /// <summary>
    /// The tree's own mesh with the notch cut into it. Made once on the first chop: the trunk's triangles round the
    /// chopping line get split into rows and sectors (more vertices, the same surface — branches are left alone), so
    /// with nothing cut it's exactly the tree as it stood. Each chop's mesh moves those trunk vertices in towards the
    /// trunk's middle by the notch's depth (a V: deepest at the line, nothing at the band's top and bottom), and the
    /// triangles that went in become fresh wood (an extra submesh, flat-shaded) — the same tree with a chunk out.
    /// </summary>
    public sealed class NotchedMesh
    {
        /// <summary>Rows of vertices up the band (the middle one on the chopping line).</summary>
        const int Rows = 6;
        /// <summary>Only the trunk is cut: triangles whose corners are all within this many radii of its middle.</summary>
        const float TrunkReach = 1.6f;
        /// <summary>A triangle cut in further than this (fraction of the radius) shows fresh wood.</summary>
        const float WoodFrom = 0.03f;

        readonly EditableMesh basis;
        readonly TrunkSection lo, hi;
        readonly float y0, y1;
        readonly List<int> trunk = new List<int>();

        public NotchedMesh(Mesh source, TrunkSection lo, TrunkSection hi)
        {
            this.lo = lo;
            this.hi = hi;
            y0 = lo.Y;
            y1 = hi.Y;
            basis = EditableMesh.From(source);
            const float eps = 1e-4f;

            // Rows: split where the trunk crosses each row's height.
            for (int k = 0; k <= Rows; k++)
            {
                float y = Mathf.Lerp(y0, y1, k / (float)Rows);
                basis.Split(Vector3.up, y, (a, b, c) => OnTrunk(a) && OnTrunk(b) && OnTrunk(c));
            }
            // Sectors: split the band's trunk triangles along vertical planes through its middle.
            TrunkSection.At(lo, hi, (y0 + y1) * 0.5f, 0, out var centre, out _);
            var mid = new Vector3(centre.x, 0f, centre.y);
            for (int s = 0; s < ChopBand.Sectors / 2; s++)
            {
                float angle = s * Mathf.PI * 2f / ChopBand.Sectors;
                var normal = new Vector3(-Mathf.Sin(angle), 0f, Mathf.Cos(angle));
                basis.Split(normal, Vector3.Dot(normal, mid), (a, b, c) => InBand(a) && InBand(b) && InBand(c) && OnTrunk(a) && OnTrunk(b) && OnTrunk(c));
            }
            for (int i = 0; i < basis.VertexCount; i++)
                if (InBand(basis.Positions[i]) && OnTrunk(basis.Positions[i])) trunk.Add(i);

            bool InBand(Vector3 p) => p.y >= y0 - eps && p.y <= y1 + eps;
        }

        /// <summary>Close enough to the trunk's middle (at its height) to be trunk rather than branch.</summary>
        bool OnTrunk(Vector3 p)
        {
            float y = Mathf.Clamp(p.y, y0, y1);
            Vector2 o = new Vector2(p.x, p.z);
            TrunkSection.At(lo, hi, y, 0, out var centre, out _);
            o -= centre;
            int s = TrunkSection.Sector(Mathf.Atan2(o.y, o.x));
            TrunkSection.At(lo, hi, y, s, out _, out float r);
            return o.magnitude <= r * TrunkReach;
        }

        /// <summary>How many vertices the cut can move (for tests).</summary>
        public int TrunkVertices => trunk.Count;

        /// <summary>
        /// The tree with the notch at this depth: per sector (fraction of the radius, see <see cref="ChoppingTree"/>), deepest at
        /// <paramref name="mid"/>, gone <paramref name="half"/> above and below. Fresh wood is an extra submesh after the tree's own.
        /// </summary>
        public Mesh Build(Mesh into, float[] depth, float mid, float half)
        {
            var m = basis.Clone();
            var cut = new Dictionary<int, float>(trunk.Count);
            foreach (int i in trunk)
            {
                Vector3 p = m.Positions[i];
                TrunkSection.At(lo, hi, p.y, 0, out var centre, out _);
                Vector2 o = new Vector2(p.x - centre.x, p.z - centre.y);
                float v = Mathf.Max(0f, 1f - Mathf.Abs(p.y - mid) / half);
                if (v <= 0f || o.sqrMagnitude < 1e-8f) continue;
                float f = Mathf.Repeat(Mathf.Atan2(o.y, o.x) / (Mathf.PI * 2f), 1f) * ChopBand.Sectors;
                int s0 = Mathf.FloorToInt(f) % ChopBand.Sectors, s1 = (s0 + 1) % ChopBand.Sectors;
                float c = Mathf.Min(0.96f, Mathf.Lerp(depth[s0], depth[s1], f - Mathf.Floor(f)) * v);
                if (c <= 0f) continue;
                cut[i] = c;
                o *= 1f - c;
                m.Positions[i] = new Vector3(centre.x + o.x, p.y, centre.y + o.y);
            }

            // Triangles that went in are fresh wood, on corners of their own with the face's normal.
            var wood = new List<int>();
            foreach (var tris in m.Submeshes)
            {
                var kept = new List<int>(tris.Count);
                for (int t = 0; t < tris.Count; t += 3)
                {
                    int a = tris[t], b = tris[t + 1], c = tris[t + 2];
                    cut.TryGetValue(a, out float ca);
                    cut.TryGetValue(b, out float cb);
                    cut.TryGetValue(c, out float cc);
                    if ((ca + cb + cc) / 3f < WoodFrom)
                    {
                        kept.Add(a); kept.Add(b); kept.Add(c);
                        continue;
                    }
                    Vector3 n = Vector3.Cross(m.Positions[b] - m.Positions[a], m.Positions[c] - m.Positions[a]);
                    // A sliver squeezed to nothing has no face to show (and no normal to light it by). Divided by hand:
                    // Normalize() gives zero for the tiny faces a notch makes, and a zero normal lights as a white blaze.
                    float area = n.magnitude;
                    if (area < 1e-10f) continue;
                    n /= area;
                    foreach (int corner in new[] { a, b, c })
                    {
                        int copy = m.CopyVertex(corner);
                        if (m.Normals.Count > 0) m.Normals[copy] = n;
                        wood.Add(copy);
                    }
                }
                tris.Clear();
                tris.AddRange(kept);
            }
            m.Submeshes.Add(wood);
            return m.ToMesh(into, "NotchedTree");
        }
    }
}
