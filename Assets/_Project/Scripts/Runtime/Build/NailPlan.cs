using UnityEngine;

namespace Cabin.Build
{
    /// <summary>Where a nail would go if fired now, whether it would hold, and what it would pull flush first.</summary>
    public struct NailPlan
    {
        public Ray Ray;
        public Piece First;
        public Piece Second;
        public Vector3 Entry;
        public Vector3 Direction;
        public Vector3 EntrySecond;
        public float Bite;
        /// <summary>Distance the pieces are drawn together before the nail goes in; 0 when they already touch.</summary>
        public float Pull;
        /// <summary>The structure that moves to close the gap (null when nothing needs to).</summary>
        public Structure Mover;
        /// <summary>True when the mover swings about a clamp instead of sliding (it's clamped at one end).</summary>
        public bool Pivot;
        public Vector3 PivotPoint;
        public Vector3 PivotAxis;
        public float PivotAngle;
        /// <summary>World direction the mover travels.</summary>
        public Vector3 MoveDirection;
        /// <summary>Null when the nail would hold.</summary>
        public string Reason;

        public bool Valid => Reason == null && First != null && Second != null;
    }
}
