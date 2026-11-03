using UnityEngine;

namespace Cabin.Player
{
    /// <summary>
    /// One of the body's own arms reaching for what it holds: each frame from rest, two-bone IK puts the wrist where
    /// the grip is with the elbow bent towards a pole, and the hand takes the grip's turn — or, for a handle, turns so
    /// the hollow of its curled fingers lies along it. Works on the skeleton the body animates, so what you see in
    /// first person, your shadow and your body seen from outside are the same arm.
    /// </summary>
    public sealed class ArmReach
    {
        readonly Transform upper, lower, hand;
        readonly Quaternion upperRest, lowerRest, handRest;
        readonly Vector3 gripCentre, gripAxis;

        public bool Valid => upper != null && lower != null && hand != null;
        /// <summary>How much of the hand's twist about the forearm the forearm itself turns through (the rest is the wrist): forearms turn along their length.</summary>
        public float ForearmShare = 0.75f;

        /// <param name="centre">Where a handle sits in the gripping hand (the hollow of the curled fingers), hand bone space.</param>
        /// <param name="axis">The way through that hollow, hand bone space.</param>
        public ArmReach(Transform upper, Transform lower, Transform hand, Vector3 centre, Vector3 axis)
        {
            this.upper = upper;
            this.lower = lower;
            this.hand = hand;
            gripCentre = centre;
            gripAxis = axis.sqrMagnitude > 1e-6f ? axis.normalized : Vector3.forward;
            if (!Valid) return;
            upperRest = upper.localRotation;
            lowerRest = lower.localRotation;
            handRest = hand.localRotation;
        }

        /// <summary>
        /// Turns the forearm about its own length through <see cref="ForearmShare"/> of the hand's twist from rest, then
        /// puts the hand back as it was: the hand doesn't move, the twist is spread over the forearm instead of all at
        /// the wrist. The wrist stays where it is (the forearm turns about the line through it).
        /// </summary>
        void ShareTwist(Quaternion handWorld)
        {
            Vector3 along = hand.position - lower.position;
            if (along.sqrMagnitude < 1e-8f || ForearmShare <= 0f) return;
            // The hand's turn away from how it sits on the forearm at rest, in the forearm's frame.
            var deviation = hand.localRotation * Quaternion.Inverse(handRest);
            float twist = TwistAngle(deviation, lower.InverseTransformDirection(along).normalized);
            lower.rotation = Quaternion.AngleAxis(twist * ForearmShare, along.normalized) * lower.rotation;
            hand.rotation = handWorld;
        }

        /// <summary>The part of a rotation that turns about <paramref name="axis"/> (degrees, signed; −180…180): the swing–twist split.</summary>
        public static float TwistAngle(Quaternion q, Vector3 axis)
        {
            axis.Normalize();
            Vector3 v = new Vector3(q.x, q.y, q.z);
            float p = Vector3.Dot(v, axis);
            float angle = 2f * Mathf.Atan2(p, q.w) * Mathf.Rad2Deg;
            return Mathf.DeltaAngle(0f, angle);
        }

        void FromRest()
        {
            upper.localRotation = upperRest;
            lower.localRotation = lowerRest;
            hand.localRotation = handRest;
        }

        /// <summary>Reaches for the grip; returns where the wrist ended up (the grip itself unless out of reach).</summary>
        public Vector3 Hold(Vector3 wrist, Quaternion grip, Vector3 pole)
        {
            if (!Valid) return wrist;
            FromRest();
            TwoBoneIK.Reach(upper, lower, hand, wrist, pole);
            hand.rotation = grip;
            ShareTwist(grip);
            return hand.position;
        }

        /// <summary>
        /// Closes the hand round a handle at <paramref name="point"/>, running <paramref name="along"/> (the way the
        /// thumb points): the fingers reach across the handle from the shoulder's side and curl round it. Returns how
        /// far the hand fell short (zero unless the handle is out of reach).
        /// </summary>
        public Vector3 HoldHandle(Vector3 point, Vector3 along, Vector3 pole, Vector3 forward)
        {
            if (!Valid) return Vector3.zero;
            FromRest();
            along.Normalize();
            Vector3 reach = Vector3.ProjectOnPlane(point - upper.position, along);
            if (reach.sqrMagnitude < 1e-6f) reach = Vector3.ProjectOnPlane(forward, along);
            Vector3 fingersLocal = Vector3.ProjectOnPlane(Vector3.up, gripAxis);
            var turn = Quaternion.LookRotation(along, reach.normalized) * Quaternion.Inverse(Quaternion.LookRotation(gripAxis, fingersLocal.normalized));
            Vector3 wrist = point - turn * Vector3.Scale(hand.lossyScale, gripCentre);
            TwoBoneIK.Reach(upper, lower, hand, wrist, pole);
            hand.rotation = turn;
            ShareTwist(turn);
            return hand.position - wrist;
        }
    }
}
