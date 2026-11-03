using UnityEngine;

namespace Cabin.Build
{
    /// <summary>
    /// Empty hands: take stock, pick pieces up, carry and place them, pull nails — and punch: hold LMB to draw the fist
    /// back (longer = harder, full at <see cref="FullCharge"/> s), let go to throw it — a straight, a jab or a hook at
    /// random. It lands as the fist reaches full stretch and shoves whatever loose wood it hits. The power bar only
    /// shows once it's clearly being held (<see cref="ShowBarAfter"/>), not on a click.
    /// </summary>
    public sealed class HandsTool : BuildTool
    {
        public override string Name => "Hands";
        public override string Hint => Belt.Carry.Holding
            ? ""
            : "E: pick up a piece / take from a rack · X: pull the nail you're looking at · LMB: punch (hold to wind up)";

        /// <summary>How wound up the punch is, 0–1, once it's been held long enough to show; −1 otherwise.</summary>
        public float PunchBar => Charging && Time.time - chargeStart >= ShowBarAfter ? Power : -1f;

        const float FullCharge = 0.8f;
        const float ShowBarAfter = 0.25f;
        /// <summary>How far the fist reaches from the eye.</summary>
        const float Reach = 1.3f;
        /// <summary>Shove given to what you hit (N·s), from a tap to a full wind-up.</summary>
        const float WeakImpulse = 6f, StrongImpulse = 70f;

        float chargeStart = -1f;
        float landAt = -1f, landPower;

        bool Charging => chargeStart >= 0f;
        float Power => Charging ? Mathf.Clamp01((Time.time - chargeStart) / FullCharge) : 0f;

        public override void OnDeselected()
        {
            chargeStart = -1f;
            Belt.Body?.CancelPunch();
        }

        /// <summary>Hold to wind up, let go to throw; the hit lands a moment later, as the fist reaches full stretch.</summary>
        void Punching()
        {
            if (landAt >= 0f && Time.time >= landAt)
            {
                Land(landPower);
                landAt = -1f;
            }
            if (!Charging && Belt.UsePressed)
            {
                chargeStart = Time.time;
                Belt.Body?.ChargePunch();
            }
            else if (Charging && (Belt.UseReleased || !Belt.UseHeld))
            {
                landPower = Power;
                chargeStart = -1f;
                landAt = Time.time + (Belt.Body != null ? Belt.Body.ThrowPunch(landPower) : 0.15f);
                Belt.Sounds?.PunchThrown(landPower);
            }
        }

        void Land(float power)
        {
            var ray = Belt.AimRay;
            if (!Physics.Raycast(ray, out var hit, Reach, BuildLayers.AimMask, QueryTriggerInteraction.Ignore)) return;
            Belt.Sounds?.PunchLanded(power);
            var piece = hit.collider.GetComponentInParent<Piece>();
            var body = piece != null && piece.Structure != null ? piece.Structure.Body : hit.rigidbody;
            if (body == null || body.isKinematic)
            {
                if (piece != null) Belt.Say(power > 0.9f ? "THUD" : "Thud");
                return;
            }
            body.AddForceAtPosition(ray.direction * Mathf.Lerp(WeakImpulse, StrongImpulse, power), hit.point, ForceMode.Impulse);
        }

        public override void Tick()
        {
            var carry = Belt.Carry;
            ref var aim = ref Belt.AimRef;

            if (carry.Holding)
            {
                if (Charging)
                {
                    chargeStart = -1f;
                    Belt.Body?.CancelPunch();
                }
                carry.Tick(aim);
                foreach (var g in carry.Guides) Belt.Ghost.DrawDotted(g.From, g.To, g.Aligned);
                if (Belt.UsePressed)
                {
                    if (aim.Interactable is CuttingTable table && carry.Current != CarryController.Placement.Table)
                        table.Interact(Belt);
                    else
                        Belt.Say(carry.Place());
                }
                else if (Belt.InteractPressed)
                {
                    carry.Drop();
                    Belt.Say("Dropped");
                }
                else if (Belt.PilotPressed)
                {
                    carry.TogglePilot();
                    Belt.Say(carry.Piloting ? "Piloting — G to come back" : "Back in your body");
                }
                else if (Belt.AlignPressed)
                    Belt.Say(carry.AlignToAim(aim) ? "Aligned to that piece" : "Look at a piece to align to it");
                Status = carry.Prompt();
                return;
            }

            Punching();
            if (PunchBar >= 0f)
            {
                Status = "";
                return;
            }

            if (aim.Clamp != null)
            {
                var clamped = Belt.Site.ClampedPiece;
                int nails = clamped != null ? Belt.Site.NailCount(clamped.Id) : 0;
                Status = nails > 0 ? "[E] Take the clamps off" : "[E] Take the clamps off\nnot nailed yet — it'll fall";
                if (Belt.InteractPressed)
                {
                    Belt.Site.Unclamp();
                    Belt.Say("Clamps off");
                }
                return;
            }

            if (aim.Nail != null)
            {
                Status = "[X] Pull nail";
                if (Belt.RemovePressed)
                {
                    Belt.Site.RemoveNail(aim.Nail.Nail.id);
                    Belt.Say("Pulled the nail");
                }
                return;
            }

            if (aim.Piece != null)
            {
                var p = aim.Piece;
                string blocker = PickupBlocker(p);
                Status = blocker != null ? $"{blocker}\n{Describe(p)}" : $"[E] Pick up\n{Describe(p)}";
                if (Belt.InteractPressed)
                {
                    if (blocker != null) Belt.Say(blocker);
                    else carry.PickUp(p, keepRotation: true);
                }
                return;
            }

            if (aim.Interactable != null)
            {
                Status = aim.Interactable.Prompt(Belt) ?? "";
                if (Belt.InteractPressed) aim.Interactable.Interact(Belt);
                return;
            }
            Status = "";
        }

        string Describe(Piece p)
        {
            var site = Belt.Site;
            int nailCount = site.NailCount(p.Id);
            string state = p.Kind == StockKind.DeckBlock
                ? p.Planted ? (site.PostIn(p) != null ? "set, post seated" : "set, socket empty") : "loose"
                : p.SeatedIn != 0 ? "post in a deck block"
                : p.Planted ? "planted"
                : p.Clamped ? "clamped"
                : p.OnTable ? "on the table" : "loose";
            string size = p.Kind == StockKind.DeckBlock ? "" : $" · {Units.FeetInches(Stock.Nominal(p.Length))}";
            string nails = p.IsConcrete ? "" : $" · {nailCount} nail{(nailCount == 1 ? "" : "s")}";
            return $"{Stock.Name(p.Kind)}{size} · {state}{nails}";
        }

        /// <summary>Why this piece can't be picked up right now, or null.</summary>
        string PickupBlocker(Piece p)
        {
            var site = Belt.Site;
            int nailCount = site.NailCount(p.Id);
            if (nailCount > 0) return $"Nailed on ({nailCount}) — [X] pull the nails first";
            if (p.Kind == StockKind.DeckBlock && site.PostIn(p) != null) return "Lift the post out first";
            return null;
        }
    }
}
