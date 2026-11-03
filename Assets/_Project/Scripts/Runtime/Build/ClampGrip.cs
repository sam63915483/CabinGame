using UnityEngine;

namespace Cabin.Build
{
    /// <summary>One clamp's job: squeeze the clamped piece against another across this line.</summary>
    public struct ClampGrip
    {
        public Piece Other;
        /// <summary>World point on the contact patch the clamp squeezes through.</summary>
        public Vector3 Point;
        /// <summary>From the other piece into the clamped one; the clamp's jaws close along it.</summary>
        public Vector3 Normal;
        /// <summary>Signed distances along <see cref="Normal"/> from <see cref="Point"/> to the two outer faces.</summary>
        public float JawHeld, JawOther;
        /// <summary>Direction the clamp's frame wraps round the wood, and how far out it has to go.</summary>
        public Vector3 Side;
        public float Reach;
    }
}
