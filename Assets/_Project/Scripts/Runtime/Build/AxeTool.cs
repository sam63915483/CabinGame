using Cabin.Forestry;
using UnityEngine;

namespace Cabin.Build
{
    /// <summary>
    /// The axe. LMB swings. Close to a tree and looking low on its trunk, it assists: the prompt says Chop, and the
    /// swing goes sideways into the trunk there (no guide drawn: Sam found the ring distracting). Each chop bites a notch; keep chopping until the trunk can't hold and the tree goes
    /// over towards the notch. The line stays where the first chop went. Anywhere else it's an ordinary swing that
    /// knocks loose wood about. A chop reaches for the spot you're looking at: you bend at the waist for a low cut and
    /// the strike comes down there (ToolViewModel, AxePose.AimFrom). It never moves you (Sam didn't want the step in).
    /// </summary>
    public sealed class AxeTool : BuildTool
    {
        public override string Name => "Axe";
        public override string Hint => "LMB: swing — near a tree's base it chops along the line";

        /// <summary>How far from your eye the trunk can be.</summary>
        const float Reach = 2.7f;
        /// <summary>The chopping line goes between these heights above the base (unless it's already been started).</summary>
        const float LowestCut = 0.35f, HighestCut = 1.15f;
        /// <summary>Swing timing: wind up, strike (the chop lands at its end), recover.</summary>
        public const float WindUp = 0.22f, Strike = 0.1f, Recover = 0.36f;
        const float Shove = 45f;

        Forest forest;
        float swingStart = -1f;
        bool landed;
        TreeTarget swingTarget;
        float swingCut;
        Vector3 strikePoint;
        Cabin.Player.FirstPersonController player;
        float bend;
        /// <summary>How far you bend for the lowest cut (degrees).</summary>
        const float MaxBend = 28f;

        /// <summary>Where this chop's blade bites (world): the face of the trunk towards you at the chopping line. False between chops and for a plain swing.</summary>
        public bool TryGetStrikePoint(out Vector3 point)
        {
            point = strikePoint;
            return swingStart >= 0f && Chopping;
        }

        /// <summary>Where in the swing (0–1), or −1 between swings.</summary>
        public float SwingT => swingStart < 0f ? -1f : Mathf.Clamp01((Time.time - swingStart) / (WindUp + Strike + Recover));
        /// <summary>This swing is a sideways chop into a tree (rather than an overhead swing).</summary>
        public bool Chopping { get; private set; }

        public override void OnDeselected()
        {
            swingStart = -1f;
            if (Belt.Body != null) Belt.Body.ChopBend = 0f;
        }

        /// <summary>How the body leans into a chop through the swing (0–1): into it over the wind-up, held through the strike, out over the recovery.</summary>
        public static float LeanIn(float t)
        {
            float a = WindUp + Strike;
            if (t <= 0f) return 0f;
            if (t < WindUp) return Mathf.SmoothStep(0f, 1f, t / WindUp);
            if (t < a) return 1f;
            return 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - a) / Recover));
        }

        /// <summary>How far to bend at the waist (degrees) for a cut this high above your feet: none from 1.1 m up, the most at 0.3 m.</summary>
        public static float BendFor(float heightAboveFeet) => Mathf.InverseLerp(1.1f, 0.3f, heightAboveFeet) * MaxBend;


        public override void Tick()
        {
            forest ??= Object.FindFirstObjectByType<Forest>();
            if (swingStart >= 0f)
            {
                float t = Time.time - swingStart;
                if (Chopping) LeanInto(t);
                if (!landed && t >= WindUp + Strike) Land();
                if (t >= WindUp + Strike + Recover)
                {
                    swingStart = -1f;
                    if (Belt.Body != null) Belt.Body.ChopBend = 0f;
                }
            }

            bool has = FindTarget(out var target, out float cut);
            Status = has ? "[LMB] Chop" : "";

            if (Belt.UsePressed && swingStart < 0f)
            {
                swingStart = Time.time;
                landed = false;
                Chopping = has;
                swingTarget = target;
                swingCut = cut;
                if (has)
                {
                    player ??= Object.FindFirstObjectByType<Cabin.Player.FirstPersonController>();
                    Vector3 feet = player != null ? player.transform.position : Belt.AimRay.origin;
                    bend = BendFor(cut - feet.y);
                    var eye = Belt.AimRay.origin;
                    Vector3 toEye = new Vector3(eye.x - target.Base.x, 0f, eye.z - target.Base.z);
                    toEye = toEye.sqrMagnitude > 1e-6f ? toEye.normalized : Vector3.forward;
                    strikePoint = new Vector3(target.Base.x, cut, target.Base.z) + toEye * target.RadiusAt(cut);
                }
                Belt.Body?.Hammer();
                Belt.Sounds?.PunchThrown(0.8f);
            }
        }

        /// <summary>Through a chop: the bend at the waist.</summary>
        void LeanInto(float t)
        {
            if (Belt.Body != null) Belt.Body.ChopBend = bend * LeanIn(t);
        }

        /// <summary>
        /// The tree you're looking at the trunk of, low enough to chop: the aim line passes close to its trunk, within
        /// reach, between the ground and a couple of metres up. Also where the chopping line goes on it.
        /// </summary>
        bool FindTarget(out TreeTarget target, out float cut)
        {
            target = default;
            cut = 0f;
            if (forest == null || Belt.Carry.Holding) return false;
            var ray = Belt.AimRay;
            Vector3 probe = ray.origin + Vector3.ProjectOnPlane(ray.direction, Vector3.up).normalized * 1.2f;
            if (!forest.Nearest(probe, Reach + 0.5f, out target)) return false;

            // Closest approach of the aim line to the trunk's vertical axis.
            Vector2 o = new Vector2(ray.origin.x - target.Base.x, ray.origin.z - target.Base.z);
            Vector2 d = new Vector2(ray.direction.x, ray.direction.z);
            if (d.sqrMagnitude < 1e-4f) return false; // looking straight down
            float t = Mathf.Max(0f, -Vector2.Dot(o, d) / d.sqrMagnitude);
            Vector3 p = ray.GetPoint(t);
            float miss = (o + d * t).magnitude;
            float up = p.y - target.Base.y;
            float radius = target.RadiusAt(target.Base.y + Mathf.Clamp(up, 0f, 2f));
            if (miss > radius + 0.4f || up < -0.4f || up > 2.3f || t - radius > Reach) return false;
            cut = target.Chopping != null ? target.Chopping.CutHeight : target.Base.y + Mathf.Clamp(up, LowestCut, HighestCut);
            return true;
        }

        /// <summary>The axe arrives: bites into the tree it was swung at, or knocks whatever it meets.</summary>
        void Land()
        {
            landed = true;
            var cam = Camera.main.transform;
            if (Chopping && forest != null && swingTarget.Data != null)
            {
                var tree = forest.Begin(swingTarget, swingCut);
                if (tree.Fallen) return;
                bool fell = tree.Chop(cam.position, 1f, out var point, out var normal);
                forest.Chips(point, normal, 1f);
                var clip = forest.ChopClip();
                if (clip != null) AudioSource.PlayClipAtPoint(clip, point, 0.9f);
                if (fell) Belt.Say("Timber!");
                return;
            }
            var ray = Belt.AimRay;
            if (!Physics.Raycast(ray, out var hit, 2.2f, BuildLayers.AimMask, QueryTriggerInteraction.Ignore)) return;
            Belt.Sounds?.PunchLanded(0.7f);
            var piece = hit.collider.GetComponentInParent<Piece>();
            var body = piece != null && piece.Structure != null ? piece.Structure.Body : hit.rigidbody;
            if (body != null && !body.isKinematic) body.AddForceAtPosition(ray.direction * Shove, hit.point, ForceMode.Impulse);
        }
    }
}
