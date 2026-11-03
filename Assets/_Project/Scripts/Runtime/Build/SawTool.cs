using Cabin.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Cabin.Build
{
    /// <summary>
    /// Cuts any piece, anywhere, along a plane. The plane starts square across the grain at the aimed point;
    /// hold RMB and move the mouse to angle it, scroll to slide it along the piece, R to cut across a different axis.
    /// </summary>
    public sealed class SawTool : BuildTool
    {
        const float In = Units.Inch;

        Piece target;
        int axis;              // 0 = across the grain, 1/2 = the other two local axes
        float slide;           // offset along the plane normal from the aimed point, metres
        Quaternion tilt = Quaternion.identity;
        InputAction altUse, adjust, modifier, rotate;

        public override string Name => "Saw";
        public override string Hint => "LMB: cut · scroll: slide the cut (Shift: 1/8 in) · hold RMB + mouse: angle it · R: cut along a different axis";

        public override void OnSelected()
        {
            base.OnSelected();
            altUse ??= Controls.Get("Player/AltUse");
            adjust ??= Controls.Get("Build/Adjust");
            modifier ??= Controls.Get("Build/Modifier");
            rotate ??= Controls.Get("Build/Rotate");
        }

        public override void OnDeselected() => LookLock.Held = false;

        public override void Tick()
        {
            ref var aim = ref Belt.AimRef;
            var piece = aim.Piece;
            if (piece == null)
            {
                target = null;
                LookLock.Held = false;
                Status = "";
                return;
            }
            if (piece != target)
            {
                target = piece;
                axis = 0;
                slide = 0f;
                tilt = Quaternion.identity;
            }

            float v = adjust.ReadValue<float>();
            int scroll = v > 0.01f ? 1 : v < -0.01f ? -1 : 0;
            if (scroll != 0) slide += scroll * (modifier.IsPressed() ? 0.125f * In : 1f * In);
            if (rotate.WasPressedThisFrame())
            {
                axis = (axis + 1) % 3;
                tilt = Quaternion.identity;
            }
            bool rotating = altUse.IsPressed();
            LookLock.Held = rotating;
            if (rotating && Mouse.current != null)
            {
                Vector2 d = Mouse.current.delta.ReadValue() * 0.25f;
                tilt = Quaternion.AngleAxis(d.x, Vector3.up) * Quaternion.AngleAxis(-d.y, Camera.main.transform.right) * tilt;
            }

            // Plane normal: a local axis of the piece (grain first), tilted by the mouse.
            int grain = piece.Solid.LongestAxis;
            int localAxis = (grain + axis) % 3;
            Vector3 normal = tilt * piece.transform.TransformDirection(Axis(localAxis));
            Vector3 point = aim.Point + normal * slide;
            var plane = new Plane(normal, point);

            // Preview: a thin blade through the piece's bounds, plus the two lengths it would leave.
            var b = piece.Solid.Bounds;
            float diag = b.size.magnitude * 1.1f;
            Belt.Ghost.DrawBlade(point, Quaternion.LookRotation(normal, Vector3.Cross(normal, Camera.main.transform.right).sqrMagnitude > 0.01f ? Vector3.Cross(normal, Camera.main.transform.right) : Vector3.up), diag);

            Vector3 grainWorld = piece.GrainAxisWorld;
            var ext = piece.Solid.Extent(piece.GrainAxisLocal);
            float along = Vector3.Dot(piece.transform.InverseTransformPoint(point), piece.GrainAxisLocal);
            float left = along - ext.x, right = ext.y - along;
            string sides = axis == 0 && Mathf.Abs(Vector3.Dot(normal, grainWorld)) > 0.99f
                ? $"{Units.FeetInches(Stock.Nominal(left))} | {Units.FeetInches(Stock.Nominal(right))}"
                : "angled cut";
            string where = piece.Planted && !piece.IsConcrete ? " · seated post: the top comes off" : "";
            Status = $"[LMB] Cut · {sides}{where}";

            if (Belt.UsePressed)
            {
                string reason = Belt.Site.Cut(piece, plane, out var offcut);
                if (reason != null) Belt.Say(reason);
                else
                {
                    Belt.Sounds?.SawCut();
                    Belt.Say($"Cut: {Units.FeetInches(Stock.Nominal(piece.Length))} and {Units.FeetInches(Stock.Nominal(offcut.Length))}");
                    target = null;
                }
            }
        }

        static Vector3 Axis(int i) => i == 0 ? Vector3.right : i == 1 ? Vector3.up : Vector3.forward;
    }
}
