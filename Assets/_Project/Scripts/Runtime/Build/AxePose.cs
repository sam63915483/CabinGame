using UnityEngine;

namespace Cabin.Build
{
    /// <summary>
    /// The axe in view space, held in both hands: where its grip point is (the left hand, low by the knob), its rotation
    /// (handle along +y, blade along +z), and how far up the handle the right hand is (it slides down to the left at
    /// the end of a swing, as a real two-handed swing does).
    /// </summary>
    public readonly struct AxePose
    {
        public readonly Vector3 Hand;
        public readonly Quaternion Rotation;
        /// <summary>The right hand's place up the handle from the grip point (metres, axe space).</summary>
        public readonly float Slide;

        /// <param name="handle">Which way the handle runs, from the hand to the head.</param>
        /// <param name="blade">Roughly which way the blade faces (squared up against the handle).</param>
        public AxePose(Vector3 hand, Vector3 handle, Vector3 blade, float slide)
        {
            Hand = hand;
            handle.Normalize();
            Vector3 b = Vector3.ProjectOnPlane(blade, handle);
            Rotation = Quaternion.LookRotation(b.sqrMagnitude > 1e-6f ? b.normalized : Vector3.forward, handle);
            Slide = slide;
        }

        AxePose(Vector3 hand, Quaternion rotation, float slide)
        {
            Hand = hand;
            Rotation = rotation;
            Slide = slide;
        }

        /// <summary>The middle of the blade's edge in the axe's own space (see ToolModelGenerator: the head's eye 0.615 m up the handle, the edge 13 cm in front of it).</summary>
        public static readonly Vector3 Edge = new Vector3(0f, 0.606f, 0.131f);

        /// <summary>Where the edge is in this pose (the pose's space).</summary>
        public Vector3 EdgeAt => Hand + Rotation * Edge;

        /// <summary>
        /// This strike, reaching for <paramref name="target"/>: the hands go to the nearest place to where this strike
        /// has them from which the axe's length just reaches it — no further than <paramref name="armReach"/> from the
        /// shoulders (<paramref name="shoulders"/>, between them) — and the axe swings round them so the edge comes down
        /// on it, the blade facing as near the way it did as the new handle allows. Out of reach, the edge points at it.
        /// </summary>
        public AxePose AimFrom(Vector3 shoulders, float armReach, Vector3 target)
        {
            float length = Edge.magnitude;
            Vector3 fromTarget = Hand - target;
            if (fromTarget.sqrMagnitude < 1e-8f) fromTarget = shoulders - target;
            Vector3 hand = target + fromTarget.normalized * length;
            Vector3 reach = hand - shoulders;
            if (reach.magnitude > armReach) hand = shoulders + reach.normalized * armReach;
            Vector3 dir = (target - hand).sqrMagnitude > 1e-8f ? (target - hand).normalized : Rotation * Vector3.up;
            // The edge sits off the handle towards the blade: tip the handle back from the line by that much.
            Vector3 blade = Vector3.ProjectOnPlane(Rotation * Vector3.forward, dir);
            if (blade.sqrMagnitude < 1e-6f) blade = Vector3.ProjectOnPlane(Rotation * Vector3.right, dir);
            blade.Normalize();
            Vector3 handle = dir;
            for (int i = 0; i < 3; i++)
            {
                handle = (dir * length - blade * Edge.z).normalized;
                blade = Vector3.ProjectOnPlane(blade, handle).normalized;
            }
            return new AxePose(hand, Quaternion.LookRotation(blade, handle), Slide);
        }

        public static AxePose Lerp(AxePose a, AxePose b, float t) =>
            new AxePose(Vector3.Lerp(a.Hand, b.Hand, t), Quaternion.Slerp(a.Rotation, b.Rotation, t), Mathf.Lerp(a.Slide, b.Slide, t));
    }
}
