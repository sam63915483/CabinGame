using UnityEngine;

namespace Cabin.Player
{
    /// <summary>Analytic two-bone IK (shoulder–elbow–wrist), bending towards a pole.</summary>
    public static class TwoBoneIK
    {
        /// <summary>
        /// Turns <paramref name="upper"/> and <paramref name="lower"/> so <paramref name="end"/> lands on the target,
        /// or as near as the limb reaches, with the middle joint bent towards <paramref name="pole"/>.
        /// </summary>
        public static void Reach(Transform upper, Transform lower, Transform end, Vector3 target, Vector3 pole)
        {
            Vector3 a = upper.position;
            float upperLength = Vector3.Distance(a, lower.position);
            float lowerLength = Vector3.Distance(lower.position, end.position);
            Vector3 toTarget = target - a;
            float d = Mathf.Clamp(toTarget.magnitude, Mathf.Abs(upperLength - lowerLength) + 1e-4f, upperLength + lowerLength - 1e-4f);
            Vector3 dir = toTarget.normalized;
            Vector3 bend = Vector3.ProjectOnPlane(pole - a, dir);
            if (bend.sqrMagnitude < 1e-8f) bend = Vector3.ProjectOnPlane(Vector3.down, dir);
            bend.Normalize();
            float cos = Mathf.Clamp((upperLength * upperLength + d * d - lowerLength * lowerLength) / (2f * upperLength * d), -1f, 1f);
            Vector3 elbow = a + dir * (upperLength * cos) + bend * (upperLength * Mathf.Sqrt(1f - cos * cos));

            upper.rotation = Quaternion.FromToRotation(lower.position - a, elbow - a) * upper.rotation;
            lower.rotation = Quaternion.FromToRotation(end.position - lower.position, a + dir * d - lower.position) * lower.rotation;
        }
    }
}
