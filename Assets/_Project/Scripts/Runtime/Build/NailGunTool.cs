using UnityEngine;

namespace Cabin.Build
{
    /// <summary>
    /// Fires whenever you pull the trigger. Aimed through one piece into another, the nail joins them; anywhere else it
    /// just sticks in whatever it hits. Either way you see it fly.
    /// </summary>
    public sealed class NailGunTool : BuildTool
    {
        public override string Name => "Nail gun";
        public override string Hint => "LMB: fire — through one piece into another nails them together · X: pull a nail";

        /// <summary>How far a stray nail flies before it's gone.</summary>
        const float Range = 40f;

        public override void Tick()
        {
            ref var aim = ref Belt.AimRef;
            if (aim.Nail != null && Belt.RemovePressed)
            {
                Belt.Site.RemoveNail(aim.Nail.Nail.id);
                Belt.Say("Pulled the nail");
                return;
            }
            // Right on a nail head: it's in the way. Merely near one (aim assist): nail beside it as usual.
            if (aim.Nail != null && aim.NailDirect)
            {
                Status = "[X] Pull nail";
                if (Belt.UsePressed) Fire(null);
                return;
            }

            var plan = Belt.Site.PlanNail(Belt.AimRay, Belt.Reach);
            if (plan.First == null)
            {
                Status = aim.Nail != null ? "[X] Pull nail" : "";
                if (Belt.UsePressed) Fire(null);
                return;
            }
            Belt.Ghost.DrawNail(plan.Entry, plan.Direction, plan.Valid);
            string pull = plan.Pull > 0f && plan.Mover != null
                ? $" · pulls the {(plan.Mover == plan.First.Structure ? Stock.Name(plan.First.Kind) : Stock.Name(plan.Second.Kind))} {Units.FeetInches(Stock.Nominal(plan.Pull))} to flush"
                : aim.Nail != null ? "[X] Pull nail" : "";
            // A joint gets a prompt; anything else just fires a loose nail, which needs no explaining.
            Status = plan.Valid
                ? $"[LMB] Nail {Stock.Name(plan.First.Kind)} → {Stock.Name(plan.Second.Kind)}\n{Units.FeetInches(Stock.Nominal(plan.Bite))} into the second piece{pull}"
                : "";

            if (!Belt.UsePressed) return;
            // Not a joint (one piece, too thick, too far apart…): it still fires, the nail just sticks in the wood.
            var nail = plan.Valid ? Belt.Site.DriveNail(plan, out _) : null;
            Fire(nail);
            if (nail != null) Belt.Say(plan.Pull > 0f ? "Pulled flush and nailed" : "Nailed");
        }

        void Fire(NailRecord driven)
        {
            var cam = Camera.main.transform;
            Vector3 muzzle = Belt.ViewModel != null && Belt.ViewModel.TryGetMuzzle(out var nose)
                ? nose
                : cam.position + cam.right * 0.2f - cam.up * 0.15f + cam.forward * 0.5f;
            Belt.Site.ShootNail(muzzle, Belt.AimRay, Range, driven);
            Belt.ViewModel?.Kick();
            Belt.Body?.Hammer();
            Belt.Sounds?.NailGunFired();
        }
    }
}
