using UnityEngine;

namespace Cabin.Player
{
    /// <summary>
    /// The legs after the animation, the way a game like Lethal Company does them: each foot finds the real ground
    /// under it and goes there (up a step, down a slope) with the sole turned to the slope, the hips dropping as far
    /// as the lower foot needs; standing still the feet stay planted where they are while the body turns and sways,
    /// and when one has been left too far behind or twisted too far round it steps — lifted in a short arc onto
    /// where the animation would have it — one foot at a time. Off the ground (a jump) it lets the animation be.
    /// </summary>
    public sealed class Legs
    {
        readonly Transform hips;
        readonly Transform[] upper = new Transform[2], lower = new Transform[2], foot = new Transform[2];
        /// <summary>Each ankle's height above the sole at rest (world units).</summary>
        readonly float[] ankle = new float[2];
        readonly Vector3[] planted = new Vector3[2];
        readonly Quaternion[] plantedTurn = new Quaternion[2];
        readonly float[] arc = new float[2];
        readonly Vector3[] animated = new Vector3[2], target = new Vector3[2], normal = new Vector3[2];
        readonly Quaternion[] animatedTurn = new Quaternion[2], footTurn = new Quaternion[2];
        readonly float[] need = new float[2], raised = new float[2];
        bool hasPlant;
        int stepping = -1;
        float stepT, groundWeight = 1f, plantWeight, hipsOffset;
        Vector3 stepFrom;
        Quaternion stepFromTurn;

        /// <summary>How far (metres) or how much turned (degrees) a planted foot may be from where the animation has it before it steps; how long a step takes (s) and how high it lifts (m).</summary>
        public float StepDistance = 0.22f, StepAngle = 30f, StepTime = 0.24f, StepLift = 0.07f;
        /// <summary>How far the hips may drop to let a foot reach lower ground (m), and how far a sole tilts to the slope (degrees).</summary>
        public float MaxDrop = 0.35f, MaxTilt = 30f;

        public bool Valid { get; }
        /// <summary>A foot is mid-step (for footstep sounds and tests).</summary>
        public bool Stepping => stepping >= 0;

        /// <param name="groundY">The height of the ground under the body as it stands now (at rest), to measure the ankles from.</param>
        public Legs(Transform hips, Transform leftUpLeg, Transform leftLeg, Transform leftFoot, Transform rightUpLeg, Transform rightLeg, Transform rightFoot, float groundY)
        {
            this.hips = hips;
            upper[0] = leftUpLeg; lower[0] = leftLeg; foot[0] = leftFoot;
            upper[1] = rightUpLeg; lower[1] = rightLeg; foot[1] = rightFoot;
            Valid = hips != null && leftUpLeg != null && leftLeg != null && leftFoot != null && rightUpLeg != null && rightLeg != null && rightFoot != null;
            if (!Valid) return;
            for (int i = 0; i < 2; i++) ankle[i] = Mathf.Max(0.02f, foot[i].position.y - groundY);
        }

        /// <summary>A step's lift at <paramref name="t"/> (0–1 through it): up and down in one smooth arc.</summary>
        public static float StepArc(float t, float lift) => Mathf.Sin(Mathf.Clamp01(t) * Mathf.PI) * lift;

        /// <summary>How far the hips drop so the lowest foot can reach: the most any foot needs to go down, none if they all go up, at most <paramref name="maxDrop"/>.</summary>
        public static float HipsDrop(float[] footNeeds, float maxDrop)
        {
            float lowest = 0f;
            foreach (float n in footNeeds) lowest = Mathf.Min(lowest, n);
            return Mathf.Max(lowest, -maxDrop);
        }

        /// <summary>How far past its limits a planted foot is (above 1: time to step): the larger of distance over <paramref name="maxDistance"/> and turn over <paramref name="maxAngle"/>.</summary>
        public static float Strain(Vector3 planted, Vector3 natural, Quaternion plantedTurn, Quaternion naturalTurn, float maxDistance, float maxAngle)
        {
            Vector3 d = natural - planted;
            d.y = 0f;
            Vector3 a = plantedTurn * Vector3.forward, b = naturalTurn * Vector3.forward;
            a.y = 0f;
            b.y = 0f;
            float turn = a.sqrMagnitude > 1e-6f && b.sqrMagnitude > 1e-6f ? Vector3.Angle(a, b) : 0f;
            return Mathf.Max(d.magnitude / Mathf.Max(maxDistance, 1e-4f), turn / Mathf.Max(maxAngle, 1e-4f));
        }

        /// <summary>
        /// Places the feet for this frame, after the animation. <paramref name="baseY"/>: the height the body stands at
        /// (the controller's feet); <paramref name="still"/>: standing rather than walking, so the feet plant;
        /// <paramref name="forward"/>: which way the legs face (knees bend that way).
        /// </summary>
        public void Solve(Vector3 forward, float baseY, bool grounded, bool still, int groundMask, float dt)
        {
            if (!Valid) return;
            Vector3 up = Vector3.up;
            groundWeight = Mathf.MoveTowards(groundWeight, grounded ? 1f : 0f, dt * 6f);
            plantWeight = Mathf.MoveTowards(plantWeight, still && grounded ? 1f : 0f, dt * (still ? 5f : 10f));
            for (int i = 0; i < 2; i++)
            {
                animated[i] = foot[i].position;
                animatedTurn[i] = foot[i].rotation;
                arc[i] = 0f;
            }

            Plant(dt);

            for (int i = 0; i < 2; i++)
            {
                Vector3 at = animated[i];
                Quaternion turn = animatedTurn[i];
                if (hasPlant)
                {
                    at = Vector3.Lerp(animated[i], planted[i], plantWeight);
                    turn = Quaternion.Slerp(animatedTurn[i], plantedTurn[i], plantWeight);
                }
                footTurn[i] = turn;
                // What the animation lifts the foot by (mid-stride), kept on top of the ground; planted feet sit flat.
                float lift = Mathf.Max(0f, animated[i].y - baseY - ankle[i]) * (1f - plantWeight) + arc[i];
                float groundAt = baseY;
                normal[i] = up;
                if (Physics.Raycast(new Vector3(at.x, Mathf.Max(at.y, baseY) + 0.6f, at.z), Vector3.down, out var hit, 1.6f, groundMask, QueryTriggerInteraction.Ignore))
                {
                    groundAt = hit.point.y;
                    normal[i] = hit.normal;
                }
                raised[i] = lift;
                target[i] = new Vector3(at.x, groundAt + ankle[i] + lift, at.z);
                need[i] = target[i].y - animated[i].y;
            }

            float drop = HipsDrop(need, MaxDrop) * groundWeight;
            hipsOffset = Mathf.Lerp(hipsOffset, drop, 1f - Mathf.Exp(-15f * dt));
            hips.position += up * hipsOffset;

            for (int i = 0; i < 2; i++)
            {
                Vector3 goal = Vector3.Lerp(animated[i] + up * hipsOffset, target[i], groundWeight);
                // The knee bends the way the animation bends it (never backwards), or forward when the leg is straight.
                Vector3 hip = upper[i].position, knee = lower[i].position;
                Vector3 bend = knee - (hip + foot[i].position) * 0.5f;
                if (bend.sqrMagnitude < 1e-4f || Vector3.Dot(bend, forward) < 0f) bend = forward;
                TwoBoneIK.Reach(upper[i], lower[i], foot[i], goal, knee + bend.normalized * 0.5f);
                // The sole lies on the slope, the less so the higher the foot is off the ground.
                float flat = groundWeight * (1f - Mathf.Clamp01(raised[i] / 0.1f));
                var tilt = Quaternion.FromToRotation(up, normal[i]);
                float angle = Quaternion.Angle(Quaternion.identity, tilt);
                if (angle > MaxTilt) tilt = Quaternion.Slerp(Quaternion.identity, tilt, MaxTilt / angle);
                foot[i].rotation = Quaternion.Slerp(Quaternion.identity, tilt, flat) * footTurn[i];
            }
        }

        /// <summary>Standing still: keeps the feet where they were put down, stepping the one most strained when it must.</summary>
        void Plant(float dt)
        {
            if (plantWeight <= 0f)
            {
                hasPlant = false;
                stepping = -1;
                return;
            }
            if (!hasPlant)
            {
                for (int i = 0; i < 2; i++)
                {
                    planted[i] = animated[i];
                    plantedTurn[i] = animatedTurn[i];
                }
                hasPlant = true;
            }
            if (stepping < 0)
            {
                float worst = 1f;
                for (int i = 0; i < 2; i++)
                {
                    float strain = Strain(planted[i], animated[i], plantedTurn[i], animatedTurn[i], StepDistance, StepAngle);
                    if (strain > worst)
                    {
                        worst = strain;
                        stepping = i;
                    }
                }
                if (stepping >= 0)
                {
                    stepT = 0f;
                    stepFrom = planted[stepping];
                    stepFromTurn = plantedTurn[stepping];
                }
            }
            if (stepping < 0) return;
            int s = stepping;
            stepT += dt / Mathf.Max(StepTime, 1e-3f);
            float t = Mathf.Clamp01(stepT), e = Mathf.SmoothStep(0f, 1f, t);
            planted[s] = Vector3.Lerp(stepFrom, animated[s], e);
            plantedTurn[s] = Quaternion.Slerp(stepFromTurn, animatedTurn[s], e);
            arc[s] = StepArc(t, StepLift);
            if (stepT >= 1f) stepping = -1;
        }
    }
}
