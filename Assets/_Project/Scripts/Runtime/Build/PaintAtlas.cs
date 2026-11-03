using System.Collections.Generic;
using UnityEngine;

namespace Cabin.Build
{
    /// <summary>
    /// A unique 2D layout of every face of a <see cref="ConvexSolid"/>, so each point on the surface has its own
    /// spot in a paint texture. Each face is unfolded flat onto its own chart (in metres, true scale) and the charts
    /// are shelf-packed into one rectangle. Built deterministically from the solid, so a saved paint texture lines up
    /// again after loading.
    /// </summary>
    public sealed class PaintAtlas
    {
        /// <summary>World size of one paint texel: fine enough to write with a spray can.</summary>
        public const float Texel = 0.004f;
        /// <summary>Gap between charts, in texels, so bilinear filtering never mixes neighbouring faces.</summary>
        const int PadTexels = 3;
        const int MaxTexels = 2048;

        public struct Chart
        {
            public int Face;
            public Vector3 Origin, U, V, Normal;
            /// <summary>Chart-space (U, V) extent of the face, metres.</summary>
            public Vector2 Min, Max;
            /// <summary>Where the chart's Min sits in the atlas, metres.</summary>
            public Vector2 Offset;
        }

        public readonly List<Chart> Charts = new List<Chart>();
        public Vector2 SizeMetres { get; private set; }
        /// <summary>Metres per texel actually used (bigger than <see cref="Texel"/> only for huge pieces).</summary>
        public float TexelSize { get; private set; } = Texel;
        public int Width { get; private set; }
        public int Height { get; private set; }

        public static PaintAtlas Build(ConvexSolid solid, Vector3 grain)
        {
            var atlas = new PaintAtlas();
            for (int i = 0; i < solid.Faces.Count; i++)
            {
                var f = solid.Faces[i];
                Vector3 n = solid.FaceNormal(f);
                Vector3 u = Vector3.ProjectOnPlane(grain, n);
                if (u.sqrMagnitude < 0.01f) u = Vector3.ProjectOnPlane(Mathf.Abs(n.y) < 0.9f ? Vector3.up : Vector3.right, n);
                u.Normalize();
                Vector3 v = Vector3.Cross(n, u);
                Vector3 o = solid.Vertices[f[0]];
                var min = new Vector2(float.MaxValue, float.MaxValue);
                var max = new Vector2(float.MinValue, float.MinValue);
                foreach (int vi in f)
                {
                    Vector3 d = solid.Vertices[vi] - o;
                    var p = new Vector2(Vector3.Dot(d, u), Vector3.Dot(d, v));
                    min = Vector2.Min(min, p);
                    max = Vector2.Max(max, p);
                }
                atlas.Charts.Add(new Chart { Face = i, Origin = o, U = u, V = v, Normal = n, Min = min, Max = max });
            }
            atlas.Pack();
            return atlas;
        }

        /// <summary>Shelf packing, tallest charts first, into a roughly square sheet.</summary>
        void Pack()
        {
            float area = 0f, widest = 0f;
            foreach (var c in Charts)
            {
                var s = c.Max - c.Min;
                area += (s.x + 0.02f) * (s.y + 0.02f);
                widest = Mathf.Max(widest, s.x);
            }
            for (int attempt = 0; attempt < 4; attempt++)
            {
                float pad = PadTexels * TexelSize;
                float rowWidth = Mathf.Max(widest + 2f * pad, Mathf.Sqrt(area) * 1.15f);
                var order = new List<int>();
                for (int i = 0; i < Charts.Count; i++) order.Add(i);
                order.Sort((a, b) => (Charts[b].Max.y - Charts[b].Min.y).CompareTo(Charts[a].Max.y - Charts[a].Min.y));
                float x = pad, y = pad, rowH = 0f, usedW = 0f;
                foreach (int i in order)
                {
                    var c = Charts[i];
                    var s = c.Max - c.Min;
                    if (x + s.x + pad > rowWidth && x > pad)
                    {
                        x = pad;
                        y += rowH + pad;
                        rowH = 0f;
                    }
                    c.Offset = new Vector2(x, y);
                    Charts[i] = c;
                    x += s.x + pad;
                    rowH = Mathf.Max(rowH, s.y);
                    usedW = Mathf.Max(usedW, x);
                }
                SizeMetres = new Vector2(usedW, y + rowH + pad);
                Width = Mathf.CeilToInt(SizeMetres.x / TexelSize);
                Height = Mathf.CeilToInt(SizeMetres.y / TexelSize);
                if (Width <= MaxTexels && Height <= MaxTexels) return;
                TexelSize *= Mathf.Max(Width, Height) / (float)MaxTexels * 1.01f;
            }
            Width = Mathf.Min(Width, MaxTexels);
            Height = Mathf.Min(Height, MaxTexels);
        }

        /// <summary>Normalised atlas coordinate of a local point on a given face.</summary>
        public Vector2 UV(int chartIndex, Vector3 local)
        {
            var c = Charts[chartIndex];
            Vector3 d = local - c.Origin;
            var p = new Vector2(Vector3.Dot(d, c.U), Vector3.Dot(d, c.V)) - c.Min + c.Offset;
            return new Vector2(p.x / SizeMetres.x, p.y / SizeMetres.y);
        }

        /// <summary>Finds the face a local surface point lies on (optionally with a normal hint) and its texel.</summary>
        public bool Locate(Vector3 local, Vector3 normalHint, out int chartIndex, out Vector2Int texel)
        {
            chartIndex = -1;
            texel = default;
            float best = 0.004f;
            for (int i = 0; i < Charts.Count; i++)
            {
                var c = Charts[i];
                if (normalHint != Vector3.zero && Vector3.Dot(c.Normal, normalHint) < 0.9f) continue;
                float dist = Mathf.Abs(Vector3.Dot(local - c.Origin, c.Normal));
                if (dist > best) continue;
                Vector3 d = local - c.Origin;
                var p = new Vector2(Vector3.Dot(d, c.U), Vector3.Dot(d, c.V));
                if (p.x < c.Min.x - 0.002f || p.y < c.Min.y - 0.002f || p.x > c.Max.x + 0.002f || p.y > c.Max.y + 0.002f) continue;
                best = dist;
                chartIndex = i;
            }
            if (chartIndex < 0) return false;
            var uv = UV(chartIndex, local);
            texel = new Vector2Int(Mathf.Clamp((int)(uv.x * Width), 0, Width - 1), Mathf.Clamp((int)(uv.y * Height), 0, Height - 1));
            return true;
        }

        /// <summary>The local point a texel of a chart stands for (clamped onto the face so padding repeats the edge).</summary>
        public Vector3 PointAt(int chartIndex, int tx, int ty)
        {
            var c = Charts[chartIndex];
            var m = new Vector2((tx + 0.5f) * TexelSize, (ty + 0.5f) * TexelSize) - c.Offset + c.Min;
            m.x = Mathf.Clamp(m.x, c.Min.x, c.Max.x);
            m.y = Mathf.Clamp(m.y, c.Min.y, c.Max.y);
            return c.Origin + c.U * m.x + c.V * m.y;
        }

        /// <summary>Texel rectangle a chart covers, including its share of padding.</summary>
        public RectInt TexelRect(int chartIndex)
        {
            var c = Charts[chartIndex];
            var s = c.Max - c.Min;
            int pad = PadTexels - 1;
            int x0 = Mathf.Max(0, Mathf.FloorToInt(c.Offset.x / TexelSize) - pad);
            int y0 = Mathf.Max(0, Mathf.FloorToInt(c.Offset.y / TexelSize) - pad);
            int x1 = Mathf.Min(Width, Mathf.CeilToInt((c.Offset.x + s.x) / TexelSize) + pad);
            int y1 = Mathf.Min(Height, Mathf.CeilToInt((c.Offset.y + s.y) / TexelSize) + pad);
            return new RectInt(x0, y0, x1 - x0, y1 - y0);
        }

        /// <summary>Unit direction, in texels, that a world direction runs across a chart (for drips running down).</summary>
        public Vector2 ChartDirection(int chartIndex, Vector3 localDirection)
        {
            var c = Charts[chartIndex];
            var d = new Vector2(Vector3.Dot(localDirection, c.U), Vector3.Dot(localDirection, c.V));
            return d.sqrMagnitude > 1e-6f ? d.normalized : Vector2.zero;
        }
    }
}
