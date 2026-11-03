using System.Collections.Generic;
using UnityEngine;

namespace Cabin.Forestry
{
    /// <summary>
    /// The trunk's real outline at one height, read off the points where <see cref="MeshSlicer"/> cut through the
    /// tree's mesh: its centre (trunks lean and aren't on the origin) and its radius in each of the chop band's
    /// sectors: what the notch is cut back from, and what tells trunk from branch. Branches crossing the same plane
    /// are left out (too far from the trunk's middle).
    /// </summary>
    public sealed class TrunkSection
    {
        public float Y;
        public Vector2 Centre;
        public readonly float[] Radius = new float[ChopBand.Sectors];

        /// <summary>The section through the cut points at height <paramref name="y"/>; a round one of <paramref name="fallback"/> if there are none.</summary>
        public static TrunkSection From(List<MeshSlicer.EdgePoint> points, float y, float fallback)
        {
            var section = new TrunkSection { Y = y };
            for (int i = 0; i < ChopBand.Sectors; i++) section.Radius[i] = fallback;
            if (points == null || points.Count < 3) return section;

            // The trunk is the dense middle of the cut: centre on it, drop what's well outside the typical distance.
            Vector2 centre = Vector2.zero;
            foreach (var p in points) centre += new Vector2(p.Position.x, p.Position.z);
            centre /= points.Count;
            var kept = new List<MeshSlicer.EdgePoint>(points);
            for (int pass = 0; pass < 3; pass++)
            {
                var d = new List<float>();
                foreach (var p in kept) d.Add((new Vector2(p.Position.x, p.Position.z) - centre).magnitude);
                var sorted = new List<float>(d);
                sorted.Sort();
                float limit = Mathf.Max(sorted[sorted.Count / 2] * 1.5f, 0.05f);
                var next = new List<MeshSlicer.EdgePoint>();
                for (int i = 0; i < kept.Count; i++) if (d[i] <= limit) next.Add(kept[i]);
                if (next.Count < 3) break;
                kept = next;
                centre = Vector2.zero;
                foreach (var p in kept) centre += new Vector2(p.Position.x, p.Position.z);
                centre /= kept.Count;
            }
            section.Centre = centre;

            // Furthest kept point in each sector; empty sectors take their neighbours' (around the ring).
            var r = new float[ChopBand.Sectors];
            foreach (var p in kept)
            {
                Vector2 o = new Vector2(p.Position.x, p.Position.z) - centre;
                int s = Sector(Mathf.Atan2(o.y, o.x));
                r[s] = Mathf.Max(r[s], o.magnitude);
            }
            for (int i = 0; i < r.Length; i++)
            {
                if (r[i] > 0f) continue;
                int a = i, b = i;
                do a = (a + r.Length - 1) % r.Length; while (r[a] <= 0f && a != i);
                do b = (b + 1) % r.Length; while (r[b] <= 0f && b != i);
                if (r[a] > 0f) section.Radius[i] = (r[a] + r[b]) * 0.5f;
            }
            for (int i = 0; i < r.Length; i++) if (r[i] > 0f) section.Radius[i] = r[i];

            return section;
        }

        public static int Sector(float angle)
        {
            int s = Mathf.RoundToInt(angle / (Mathf.PI * 2f) * ChopBand.Sectors);
            return ((s % ChopBand.Sectors) + ChopBand.Sectors) % ChopBand.Sectors;
        }

        /// <summary>Between two sections, at height <paramref name="y"/>: the centre and one sector's radius.</summary>
        public static void At(TrunkSection lo, TrunkSection hi, float y, int sector, out Vector2 centre, out float radius)
        {
            float t = Mathf.Approximately(hi.Y, lo.Y) ? 0f : Mathf.Clamp01((y - lo.Y) / (hi.Y - lo.Y));
            centre = Vector2.Lerp(lo.Centre, hi.Centre, t);
            radius = Mathf.Lerp(lo.Radius[sector], hi.Radius[sector], t);
        }
    }
}
