using UnityEngine;

namespace Cabin.Build
{
    /// <summary>What the crosshair is on this frame.</summary>
    public struct AimInfo
    {
        public bool Hit;
        public RaycastHit RaycastHit;
        public Vector3 Point => RaycastHit.point;
        public Vector3 Normal => RaycastHit.normal;
        public float Distance => RaycastHit.distance;
        public Piece Piece;
        /// <summary>The nail you're aiming at — exactly, or the nearest one close to the crosshair (see <see cref="NailDirect"/>).</summary>
        public NailHead Nail;
        /// <summary>The crosshair is right on <see cref="Nail"/>'s head, not just near it.</summary>
        public bool NailDirect;
        /// <summary>A clamp under the crosshair (closer than any wood behind it).</summary>
        public ClampVisual Clamp;
        public IInteractable Interactable;
        public CuttingTable Table;

        public bool OnGround => Hit && Piece == null && Nail == null && Interactable == null;
    }
}
