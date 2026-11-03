using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Cabin.Build
{
    /// <summary>
    /// Decides where clamps go and how big they are, the way you'd do it by hand: one per piece being touched (the
    /// three nearest), or two along a long overlap when the held piece only touches one. Each clamp sits at the edge
    /// of the touching patch and wraps over whichever edge needs the shortest reach without passing through other
    /// wood or the ground; its jaws and reach are measured locally at that spot, so a long or angled piece never
    /// stretches it.
    /// </summary>
    public static class ClampLayout
    {
        public const int MaxClamps = 3;
        /// <summary>A single contact longer than this gets two clamps.</summary>
        public const float LongOverlap = 1.0f;
        /// <summary>A clamp's pads sit this far inside the patch edge.</summary>
        static float Margin => 0.03f * K;
        static float K => Stock.CrossSectionScale / 3f;
        /// <summary>Clamp parts' sizes (also used by <see cref="ClampVisual"/>).</summary>
        public static float PadRadius => 0.035f * K;
        public static float PadThickness => 0.012f * K;
        public static float ArmThickness => 0.022f * K;
        public static float ArmWidth => 0.045f * K;
        public static float ScrewLength => 0.07f * K;
        /// <summary>Gap between the wood and the clamp's bar.</summary>
        static float Clearance => 0.025f * K;
        /// <summary>
        /// The deepest a real clamp's throat goes. A spot that needs more gets no clamp: it never stretches.
        /// </summary>
        public static float MaxReach => 0.35f * K;
        /// <summary>A ray this long finds a piece's far side from outside it.</summary>
        const float Probe = 3f;

        public static List<ClampGrip> Plan(Piece held, IReadOnlyList<Contact> contacts)
        {
            var grips = new List<ClampGrip>();
            Vector3 center = held.Collider.bounds.center;
            var chosen = contacts.OrderBy(c => (c.Point - center).sqrMagnitude).Take(MaxClamps).ToList();

            if (chosen.Count == 1)
            {
                // One long overlap (a board laid along a beam): a clamp near each end, wrapping across it.
                var c = chosen[0];
                Vector3 axis = Vector3.ProjectOnPlane(held.GrainAxisWorld, c.Normal);
                if (axis.sqrMagnitude > 0.1f)
                {
                    axis.Normalize();
                    var (lo, hi) = Along(c.Patch, axis);
                    if (hi - lo > LongOverlap)
                    {
                        foreach (float t in new[] { 0.15f, 0.85f })
                        {
                            Vector3 anchor = c.Point + axis * (Mathf.Lerp(lo, hi, t) - Vector3.Dot(c.Point, axis));
                            if (TryGrip(held, c, anchor, axis, out var g)) grips.Add(g);
                        }
                        return grips;
                    }
                }
            }
            foreach (var c in chosen)
                if (TryGrip(held, c, c.Point, null, out var g)) grips.Add(g);
            return grips;
        }

        /// <summary>
        /// The best clamp for one contact: try wrapping over each edge direction in the contact plane (the pieces'
        /// own axes), slide to the patch edge that way, measure, and keep the shortest unobstructed reach.
        /// <paramref name="along"/>, if set, is a direction the clamp must not wrap along (it's spread along it).
        /// </summary>
        static bool TryGrip(Piece held, in Contact c, Vector3 anchor, Vector3? along, out ClampGrip best)
        {
            Vector3 n = c.Normal;
            best = default;
            float bestScore = float.MaxValue;
            foreach (var d in Directions(held, c.Other, n))
            {
                if (along.HasValue && Mathf.Abs(Vector3.Dot(d, along.Value)) > 0.3f) continue;
                // Slide out to the patch edge this way (less a margin), so the clamp bites near the edge.
                float edge = Along(c.Patch, d).hi - Vector3.Dot(anchor, d) - Margin;
                Vector3 p = anchor + d * Mathf.Max(0f, edge);

                float jawHeld = Thickness(held, p, n);
                float jawOther = -Thickness(c.Other, p, -n);
                float reach = Mathf.Max(Extent(held, p, d, n, jawHeld), Extent(c.Other, p, d, -n, -jawOther)) + Clearance;
                if (reach > MaxReach || Blocked(held, c.Other, p, n, d, reach, jawHeld, jawOther)) continue;
                float score = reach;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = new ClampGrip
                    {
                        Other = c.Other, Point = p, Normal = n, JawHeld = jawHeld, JawOther = jawOther, Side = d,
                        Reach = Mathf.Max(reach, PadRadius + Clearance),
                    };
                }
            }
            return bestScore < float.MaxValue;
        }

        /// <summary>
        /// The pieces' axes laid into the contact plane, both ways round, plus the diagonals between them: where a
        /// board crosses a post neither straight way round clears both (the post goes on up, the board goes on
        /// along), but over the corner does.
        /// </summary>
        static List<Vector3> Directions(Piece a, Piece b, Vector3 normal)
        {
            var dirs = new List<Vector3>();
            foreach (var piece in new[] { a, b })
                foreach (var local in new[] { Vector3.right, Vector3.up, Vector3.forward })
                {
                    Vector3 d = Vector3.ProjectOnPlane(piece.transform.TransformDirection(local), normal);
                    if (d.sqrMagnitude < 0.25f) continue;
                    d.Normalize();
                    foreach (var s in new[] { d, -d })
                        if (!dirs.Any(e => Vector3.Dot(e, s) > 0.995f)) dirs.Add(s);
                }
            int axes = dirs.Count;
            for (int i = 0; i < axes; i++)
                for (int j = i + 1; j < axes; j++)
                {
                    if (Mathf.Abs(Vector3.Dot(dirs[i], dirs[j])) > 0.2f) continue;
                    Vector3 diag = (dirs[i] + dirs[j]).normalized;
                    if (!dirs.Any(e => Vector3.Dot(e, diag) > 0.995f)) dirs.Add(diag);
                }
            if (dirs.Count == 0)
            {
                Vector3 d = Vector3.Cross(normal, Mathf.Abs(normal.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
                dirs.Add(d);
                dirs.Add(-d);
            }
            return dirs;
        }

        static (float lo, float hi) Along(Vector3[] poly, Vector3 dir)
        {
            float lo = float.MaxValue, hi = float.MinValue;
            foreach (var p in poly)
            {
                float t = Vector3.Dot(p, dir);
                lo = Mathf.Min(lo, t);
                hi = Mathf.Max(hi, t);
            }
            return (lo, hi);
        }

        /// <summary>How thick the piece is here, from the contact plane at <paramref name="p"/> along <paramref name="into"/>.</summary>
        static float Thickness(Piece piece, Vector3 p, Vector3 into)
        {
            var ray = new Ray(p + into * Probe, -into);
            if (piece.Collider.Raycast(ray, out var hit, Probe)) return Mathf.Max(0.002f, Probe - hit.distance);
            Vector2 span = ContactFinder.Span(piece, into);
            return Mathf.Max(0.002f, span.y - Vector3.Dot(p, into));
        }

        /// <summary>
        /// How far the piece reaches from <paramref name="p"/> in direction <paramref name="d"/>, measured through its
        /// thickness (at a few depths into it along <paramref name="into"/>) — what the clamp's frame has to get round.
        /// </summary>
        static float Extent(Piece piece, Vector3 p, Vector3 d, Vector3 into, float depth)
        {
            float most = 0f;
            foreach (float f in new[] { 0.15f, 0.5f, 0.85f })
            {
                Vector3 q = p + into * (depth * f);
                Vector3 start = q + d * Probe;
                if ((piece.Collider.ClosestPoint(start) - start).sqrMagnitude < 1e-8f) return Probe; // goes on past the probe
                if (piece.Collider.Raycast(new Ray(start, -d), out var hit, Probe)) most = Mathf.Max(most, Probe - hit.distance);
            }
            return most;
        }

        /// <summary>Whether the clamp's frame and screw would pass through anything but the two pieces it grips.</summary>
        static bool Blocked(Piece held, Piece other, Vector3 p, Vector3 n, Vector3 d, float reach, float jawHeld, float jawOther)
        {
            float zLo = jawOther - PadThickness - ArmThickness, zHi = jawHeld + PadThickness + ScrewLength;
            float xLo = -PadRadius, xHi = reach + ArmThickness;
            var rot = Quaternion.LookRotation(n, Vector3.Cross(n, d)); // x = d, y = across, z = n
            Vector3 center = p + rot * new Vector3((xLo + xHi) * 0.5f, 0f, (zLo + zHi) * 0.5f);
            Vector3 half = new Vector3((xHi - xLo) * 0.5f, ArmWidth * 0.5f, (zHi - zLo) * 0.5f) - Vector3.one * 0.004f;
            int mask = BuildLayers.PieceMask | BuildLayers.GroundMask;
            foreach (var col in Physics.OverlapBox(center, half, rot, mask, QueryTriggerInteraction.Ignore))
            {
                if (col == held.Collider || col == other.Collider) continue;
                return true;
            }
            return false;
        }
    }
}
