using System.Collections.Generic;
using UnityEngine;

namespace Cabin.Editor.Map
{
    /// <summary>
    /// Line segments (game x/z) in a grid of buckets, for finding the nearest one to a point fast: a river's course or
    /// a lake's shoreline, so the ground can be shaped by the exact distance to the water's real edge (measured on
    /// the 4 m water grid, shores came out in steps and sawteeth).
    /// </summary>
    public sealed class EdgeIndex
    {
        const float Bucket = 16f;

        readonly List<(Vector2 a, Vector2 b, int tag, float va, float vb)> segments = new List<(Vector2, Vector2, int, float, float)>();
        readonly Dictionary<Vector2Int, List<int>> buckets = new Dictionary<Vector2Int, List<int>>();

        static Vector2Int Key(Vector2 p) => new Vector2Int(Mathf.FloorToInt(p.x / Bucket), Mathf.FloorToInt(p.y / Bucket));

        /// <summary>A segment from a to b, carrying a tag and a value at each end (e.g. a river's level).</summary>
        public void Add(Vector2 a, Vector2 b, int tag, float va = 0f, float vb = 0f)
        {
            int id = segments.Count;
            segments.Add((a, b, tag, va, vb));
            Vector2Int lo = Key(Vector2.Min(a, b)), hi = Key(Vector2.Max(a, b));
            for (int z = lo.y; z <= hi.y; z++)
            for (int x = lo.x; x <= hi.x; x++)
            {
                var k = new Vector2Int(x, z);
                if (!buckets.TryGetValue(k, out var list)) buckets[k] = list = new List<int>();
                list.Add(id);
            }
        }

        /// <summary>
        /// The nearest segment within <paramref name="reach"/> metres: its distance, tag, the value there (interpolated
        /// between its ends) and which side the point is on (positive: left of a → b). False if none is that close.
        /// </summary>
        public bool Nearest(Vector2 p, float reach, out float distance, out int tag, out float value, out float side)
        {
            distance = reach;
            tag = -1;
            value = 0f;
            side = 0f;
            int r = Mathf.CeilToInt(reach / Bucket);
            var c = Key(p);
            bool found = false;
            for (int dz = -r; dz <= r; dz++)
            for (int dx = -r; dx <= r; dx++)
            {
                if (!buckets.TryGetValue(new Vector2Int(c.x + dx, c.y + dz), out var list)) continue;
                foreach (int id in list)
                {
                    var s = segments[id];
                    Vector2 ab = s.b - s.a;
                    float t = Mathf.Clamp01(Vector2.Dot(p - s.a, ab) / Mathf.Max(1e-8f, ab.sqrMagnitude));
                    float d = (s.a + ab * t - p).magnitude;
                    if (d >= distance) continue;
                    distance = d;
                    tag = s.tag;
                    value = Mathf.Lerp(s.va, s.vb, t);
                    side = ab.x * (p.y - s.a.y) - ab.y * (p.x - s.a.x);
                    found = true;
                }
            }
            return found;
        }
    }
}
