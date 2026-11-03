using UnityEngine;

namespace Cabin.Player
{
    /// <summary>
    /// One hand's fingers: the three joints of each finger (grafted onto the skeleton by the editor's FingerRig, each
    /// bending towards the palm about its own +x, the thumb across it), closed into a <see cref="HandShape"/> and eased
    /// there from whatever shape they were in. Applied after the animation and the arm's reach, so the fingers close
    /// round whatever the hand is holding.
    /// </summary>
    public sealed class HandPose
    {
        static readonly string[] Names = { "Thumb", "Index", "Middle", "Ring", "Pinky" };
        public const int Fingers = 5, Joints = 3;
        const int Thumb = 0, Index = 1;

        /// <summary>Degrees each finger joint bends (knuckle first) and the thumb swings in, round a tool handle. The editor works out where a handle sits in the hand from the same numbers.</summary>
        public static readonly float[] GripBend = { 42f, 52f, 30f }, GripThumb = { 12f, 15f, 10f };
        public static readonly float[] FistBend = { 62f, 70f, 42f }, FistThumb = { 15f, 16f, 12f };
        static readonly float[] RelaxedBend = { 12f, 18f, 12f }, RelaxedThumb = { 4f, 6f, 4f };
        /// <summary>The index finger resting on a trigger: crooked, not closed.</summary>
        static readonly float[] TriggerBend = { 14f, 34f, 18f };
        /// <summary>Round a spray can, wider than a handle; the index hooked over the button on top.</summary>
        static readonly float[] CanBend = { 28f, 36f, 22f }, CanThumb = { 8f, 12f, 8f }, ButtonBend = { 22f, 30f, 20f };

        readonly Transform[,] bones = new Transform[Fingers, Joints];
        readonly Quaternion[,] rest = new Quaternion[Fingers, Joints];
        readonly float[,] current = new float[Fingers, Joints];
        float[,] target = Angles(HandShape.Relaxed);

        /// <summary>Whether this hand has its finger bones (an old body without them is left alone).</summary>
        public bool Valid { get; }

        /// <param name="hand">The hand bone; its fingers are found under it by name.</param>
        public HandPose(Transform hand, bool left)
        {
            if (hand == null) return;
            var all = hand.GetComponentsInChildren<Transform>(true);
            string side = left ? "Left" : "Right";
            bool found = true;
            for (int f = 0; f < Fingers; f++)
            for (int j = 0; j < Joints; j++)
            {
                string name = $"{side}Hand{Names[f]}{j + 1}";
                var bone = System.Array.Find(all, t => t.name == name);
                bones[f, j] = bone;
                if (bone == null) { found = false; continue; }
                rest[f, j] = bone.localRotation;
            }
            Valid = found;
            System.Array.Copy(target, current, target.Length);
        }

        /// <summary>The angles (degrees, [finger, joint], thumb first) a shape closes the hand to.</summary>
        public static float[,] Angles(HandShape shape)
        {
            var a = new float[Fingers, Joints];
            void Finger(int f, float[] bend) { for (int j = 0; j < Joints; j++) a[f, j] = bend[j]; }
            void Others(float[] bend) { for (int f = Index; f < Fingers; f++) Finger(f, bend); }
            switch (shape)
            {
                case HandShape.Fist: Others(FistBend); Finger(Thumb, FistThumb); break;
                case HandShape.Grip: Others(GripBend); Finger(Thumb, GripThumb); break;
                case HandShape.Pistol: Others(GripBend); Finger(Index, TriggerBend); Finger(Thumb, GripThumb); break;
                case HandShape.Can: Others(CanBend); Finger(Index, ButtonBend); Finger(Thumb, CanThumb); break;
                default: Others(RelaxedBend); Finger(Thumb, RelaxedThumb); break;
            }
            return a;
        }

        public void Set(HandShape shape) => target = Angles(shape);

        /// <summary>Eases the fingers towards the shape (<paramref name="rate"/>: how fast, per second) and bends the bones.</summary>
        public void Apply(float dt, float rate = 16f)
        {
            if (!Valid) return;
            float k = 1f - Mathf.Exp(-rate * dt);
            for (int f = 0; f < Fingers; f++)
            for (int j = 0; j < Joints; j++)
            {
                current[f, j] = Mathf.Lerp(current[f, j], target[f, j], k);
                bones[f, j].localRotation = rest[f, j] * Quaternion.AngleAxis(current[f, j], Vector3.right);
            }
        }
    }
}
