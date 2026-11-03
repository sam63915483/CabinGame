using UnityEngine;

namespace Cabin.Build
{
    /// <summary>
    /// A bench with a flat top. A piece set on it is onTable (held still) so it can be sawn; the Saw works on
    /// pieces anywhere, the table just keeps them from rolling off.
    /// </summary>
    public sealed class CuttingTable : MonoBehaviour, IInteractable
    {
        [SerializeField] Transform top;
        [SerializeField] Vector2 topSize = new Vector2(2.4f, 1.0f);

        public Transform Top => top;

        public void Set(Transform topTransform, Vector2 size)
        {
            top = topTransform;
            topSize = size;
        }

        /// <summary>World pose that lays a held piece flat on the table at the aimed point, grain along the table.</summary>
        public bool SnapPose(Piece piece, Vector3 aimPoint, out Vector3 position, out Quaternion rotation)
        {
            Vector3 local = top.InverseTransformPoint(aimPoint);
            position = Vector3.zero;
            rotation = Quaternion.identity;
            if (Mathf.Abs(local.x) > topSize.x * 0.5f + 0.2f || Mathf.Abs(local.z) > topSize.y * 0.5f + 0.2f) return false;

            // Local grain axis → along the table, local thinnest axis → up.
            var size = piece.Solid.Bounds.size;
            int grain = piece.Solid.LongestAxis;
            int thin = size.x <= size.y && size.x <= size.z ? 0 : size.y <= size.z ? 1 : 2;
            if (thin == grain) thin = (grain + 1) % 3;
            Vector3 g = Axis(grain), t = Axis(thin);
            Quaternion pieceFrame = Quaternion.LookRotation(Vector3.Cross(g, t), t); // right = g, up = t
            rotation = top.rotation * Quaternion.Inverse(pieceFrame);

            var b = piece.Solid.Bounds;
            Vector3 centerOffset = rotation * b.center;
            float halfThick = size[thin] * 0.5f;
            float x = Mathf.Clamp(local.x, -topSize.x * 0.5f, topSize.x * 0.5f);
            position = top.TransformPoint(new Vector3(x, 0f, 0f)) + top.up * (halfThick + 0.002f) - centerOffset;
            return true;
        }

        static Vector3 Axis(int i) => i == 0 ? Vector3.right : i == 1 ? Vector3.up : Vector3.forward;

        public string Prompt(BuildToolbelt belt) =>
            belt.Carry.Holding ? "[LMB] Set it on the table" : "Cutting table";

        public void Interact(BuildToolbelt belt)
        {
            if (!belt.Carry.Holding) return;
            belt.Carry.PutOnTable(this);
        }
    }
}
