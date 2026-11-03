using System.Collections.Generic;
using UnityEngine;

namespace Cabin.Build
{
    /// <summary>
    /// Finds the wood a piece is touching. Works whether the pieces are pressed together, just touching or
    /// overlapping slightly: closest points between two convex colliders come from alternating projections.
    /// </summary>
    public static class ContactFinder
    {
        /// <summary>Faces this close count as touching.</summary>
        public const float Tolerance = 0.008f;

        public static List<Contact> Touching(Piece piece, float tolerance = Tolerance)
        {
            var result = new List<Contact>();
            var bounds = piece.Collider.bounds;
            bounds.Expand(tolerance * 4f);
            var seen = new HashSet<Piece>();
            foreach (var col in Physics.OverlapBox(bounds.center, bounds.extents, Quaternion.identity, BuildLayers.PieceMask, QueryTriggerInteraction.Ignore))
            {
                if (!col.TryGetComponent<Piece>(out var other) || other == piece || !seen.Add(other)) continue;
                if (TryContact(piece, other, tolerance, out var c)) result.Add(c);
            }
            return result;
        }

        public static bool TryContact(Piece piece, Piece other, float tolerance, out Contact contact)
        {
            contact = default;
            var a = piece.Collider;
            var o = other.Collider;
            Vector3 ap = a.transform.position, op = o.transform.position;
            Quaternion ar = a.transform.rotation, or = o.transform.rotation;

            Vector3 pa = a.bounds.center;
            Vector3 po = Physics.ClosestPoint(pa, o, op, or);
            for (int i = 0; i < 16; i++)
            {
                pa = Physics.ClosestPoint(po, a, ap, ar);
                po = Physics.ClosestPoint(pa, o, op, or);
            }
            float d = (pa - po).magnitude;
            if (d > tolerance) return false;

            Vector3 normal;
            if (Physics.ComputePenetration(a, ap, ar, o, op, or, out var dir, out float depth) && depth > 1e-4f) normal = dir;
            else if (d > 1e-5f) normal = (pa - po) / d;
            else normal = SeparatingNormal(piece, other, po);

            var patch = Patch(piece, other, (pa + po) * 0.5f, normal);
            contact = new Contact { Other = other, Normal = normal, Patch = patch, Point = Centroid(patch) };
            return true;
        }

        /// <summary>
        /// For pieces exactly flush, the face they meet on: a face of either piece through the contact point with
        /// the whole of the other piece on its outside. On an edge or corner, more than one face passes through the
        /// point, and only the real contact face separates the pieces. Points from the other piece into this one.
        /// </summary>
        static Vector3 SeparatingNormal(Piece piece, Piece other, Vector3 point)
        {
            const float onPlane = 0.003f, slack = 0.004f;
            Vector3 best = (piece.Collider.bounds.center - point).normalized;
            float bestScore = float.MinValue;
            void Consider(Piece owner, Piece outside, float sign)
            {
                foreach (var f in owner.Solid.Faces)
                {
                    Vector3 n = owner.transform.TransformDirection(owner.Solid.FaceNormal(f));
                    Vector3 p0 = owner.transform.TransformPoint(owner.Solid.Vertices[f[0]]);
                    if (Mathf.Abs(Vector3.Dot(point - p0, n)) > onPlane) continue;
                    float min = float.MaxValue;
                    foreach (var v in outside.Solid.Vertices) min = Mathf.Min(min, Vector3.Dot(outside.transform.TransformPoint(v) - p0, n));
                    if (min < -slack) continue;
                    float score = -Mathf.Abs(min);
                    if (score > bestScore) { bestScore = score; best = n * sign; }
                }
            }
            Consider(other, piece, 1f);
            Consider(piece, other, -1f);
            return best;
        }

        /// <summary>
        /// The touching patch: <paramref name="a"/>'s face lying in the contact plane (facing the other piece) clipped
        /// by <paramref name="b"/>'s face lying in it (facing back), both convex, so the overlap is too. Exact for any
        /// angle between the pieces. When the pieces only meet along an edge or corner there's no such pair of faces;
        /// then it's a small square round the touching point.
        /// </summary>
        public static Vector3[] Patch(Piece a, Piece b, Vector3 point, Vector3 normal)
        {
            const float onPlane = 0.006f;
            Vector3 u = Vector3.Cross(normal, Mathf.Abs(normal.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            Vector3 v = Vector3.Cross(normal, u);
            float plane = Vector3.Dot(point, normal);
            var fa = FaceInPlane(a, -normal, normal, plane, onPlane, u, v);
            var fb = FaceInPlane(b, normal, normal, plane, onPlane, u, v);
            System.Collections.Generic.List<Vector2> poly = null;
            if (fa != null && fb != null) poly = Clip(fa, fb);
            if (poly == null || poly.Count < 3 || Area(poly) < 1e-6f)
            {
                const float r = 0.01f;
                Vector2 c = new Vector2(Vector3.Dot(point, u), Vector3.Dot(point, v));
                poly = new System.Collections.Generic.List<Vector2> { c + new Vector2(-r, -r), c + new Vector2(r, -r), c + new Vector2(r, r), c + new Vector2(-r, r) };
            }
            var result = new Vector3[poly.Count];
            for (int i = 0; i < poly.Count; i++) result[i] = normal * plane + u * poly[i].x + v * poly[i].y;
            return result;
        }

        /// <summary>The piece's face whose outward normal is <paramref name="outward"/> and which lies in the plane, as a CCW 2D polygon.</summary>
        static System.Collections.Generic.List<Vector2> FaceInPlane(Piece p, Vector3 outward, Vector3 planeNormal, float plane, float tolerance, Vector3 u, Vector3 v)
        {
            foreach (var f in p.Solid.Faces)
            {
                Vector3 n = p.transform.TransformDirection(p.Solid.FaceNormal(f));
                if (Vector3.Dot(n, outward) < 0.995f) continue;
                Vector3 p0 = p.transform.TransformPoint(p.Solid.Vertices[f[0]]);
                if (Mathf.Abs(Vector3.Dot(p0, planeNormal) - plane) > tolerance) continue;
                var poly = new System.Collections.Generic.List<Vector2>(f.Length);
                foreach (int i in f)
                {
                    Vector3 w = p.transform.TransformPoint(p.Solid.Vertices[i]);
                    poly.Add(new Vector2(Vector3.Dot(w, u), Vector3.Dot(w, v)));
                }
                if (Area(poly) < 0f) poly.Reverse();
                return poly;
            }
            return null;
        }

        /// <summary>Sutherland–Hodgman: a convex polygon clipped by another convex polygon (both CCW).</summary>
        static System.Collections.Generic.List<Vector2> Clip(System.Collections.Generic.List<Vector2> subject, System.Collections.Generic.List<Vector2> clipper)
        {
            var output = new System.Collections.Generic.List<Vector2>(subject);
            for (int i = 0; i < clipper.Count && output.Count > 0; i++)
            {
                Vector2 a = clipper[i], b = clipper[(i + 1) % clipper.Count];
                var input = output;
                output = new System.Collections.Generic.List<Vector2>();
                for (int j = 0; j < input.Count; j++)
                {
                    Vector2 p = input[j], q = input[(j + 1) % input.Count];
                    float sp = Cross(b - a, p - a), sq = Cross(b - a, q - a);
                    if (sp >= 0f) output.Add(p);
                    if ((sp >= 0f) != (sq >= 0f)) output.Add(p + (q - p) * (sp / (sp - sq)));
                }
            }
            return output;
        }

        static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

        static float Area(System.Collections.Generic.List<Vector2> poly)
        {
            float area = 0f;
            for (int i = 0; i < poly.Count; i++) area += Cross(poly[i], poly[(i + 1) % poly.Count]);
            return area * 0.5f;
        }

        /// <summary>Area centroid of a flat convex polygon.</summary>
        public static Vector3 Centroid(Vector3[] poly)
        {
            Vector3 origin = poly[0], sum = Vector3.zero;
            float total = 0f;
            for (int i = 1; i + 1 < poly.Length; i++)
            {
                float area = Vector3.Cross(poly[i] - origin, poly[i + 1] - origin).magnitude;
                sum += (origin + poly[i] + poly[i + 1]) / 3f * area;
                total += area;
            }
            if (total < 1e-9f)
            {
                foreach (var p in poly) sum += p;
                return sum / poly.Length;
            }
            return sum / total;
        }

        /// <summary>(min, max) of a piece's world vertices along a direction.</summary>
        public static Vector2 Span(Piece p, Vector3 dir)
        {
            float lo = float.MaxValue, hi = float.MinValue;
            foreach (var v in p.Solid.Vertices)
            {
                float d = Vector3.Dot(p.transform.TransformPoint(v), dir);
                if (d < lo) lo = d;
                if (d > hi) hi = d;
            }
            return new Vector2(lo, hi);
        }
    }
}
