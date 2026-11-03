using System.Collections.Generic;
using UnityEngine;

namespace Cabin.Forestry
{
    /// <summary>
    /// The chopping line's geometry: the trunk round it is worked in <see cref="Sectors"/> sectors, each cut in by its
    /// own depth (fraction of the radius). Which way the cut side faces, and the fresh-wood face left on the stump and
    /// on the butt of the felled top.
    /// </summary>
    public static class ChopBand
    {
        public const int Sectors = 28;

        /// <summary>The way the cut side faces (unit, trunk space, horizontal).</summary>
        public static Vector3 Weakest(float[] depth)
        {
            Vector3 sum = Vector3.zero;
            for (int i = 0; i < depth.Length; i++)
            {
                float a = i * Mathf.PI * 2f / depth.Length;
                sum += new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * depth[i];
            }
            return sum.sqrMagnitude > 1e-6f ? sum.normalized : Vector3.forward;
        }

        /// <summary>
        /// The flat cut face across the trunk at height <paramref name="y"/> (the chopping line), the outline what the
        /// notch has left there; facing up for the stump, down for the felled top.
        /// </summary>
        public static Mesh Cap(TrunkSection lo, TrunkSection hi, float y, float[] depth, bool up)
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();
            TrunkSection.At(lo, hi, y, 0, out var centre, out _);
            verts.Add(new Vector3(centre.x, y, centre.y));
            for (int i = 0; i <= Sectors; i++)
            {
                int s = i % Sectors;
                float a = s * Mathf.PI * 2f / Sectors;
                TrunkSection.At(lo, hi, y, s, out _, out float r);
                r *= 1f - Mathf.Min(0.96f, depth[s]);
                verts.Add(new Vector3(centre.x + Mathf.Cos(a) * r, y, centre.y + Mathf.Sin(a) * r));
            }
            for (int i = 0; i < Sectors; i++)
                tris.AddRange(up ? new[] { 0, i + 2, i + 1 } : new[] { 0, i + 1, i + 2 });
            var mesh = new Mesh { name = up ? "StumpFace" : "ButtFace" };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
