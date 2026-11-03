using UnityEngine;
using UnityEngine.Rendering;

namespace Cabin.Player
{
    /// <summary>
    /// The player's character, one body for everything: what you see in first person, your shadow and the man others
    /// would see are the same skeleton. Drives idle/walk/run (and crouch) from how the player moves, the playback
    /// sped up past the fastest clip so the feet keep pace with the ground; a jump when they leave the ground going
    /// up, a hammer swing and punches on the upper body. Moving any way but straight ahead, the hips (and so the legs)
    /// turn towards where you're going while the spine turns back so the chest faces the way you look.
    /// After the animation, each frame: the chest leans with the look; the body slides so its neck is over the middle
    /// of the player; the legs find the real ground (<see cref="Legs"/>: feet on slopes and steps,
    /// planted standing still, stepping when turned); the chest leans with your look, so the shoulders go where
    /// you're looking; the body's own arms reach for whatever is in hand (<see cref="ArmReach"/>, both for a
    /// two-handed tool) and the fingers close on it (<see cref="HandPose"/>: round the axe handle, a finger on the
    /// nail gun's trigger, round the can) or ball into fists for a punch; and the camera goes to the eyes, on the neck
    /// as it now is, the head nodding through half the look (<see cref="EyeOffset"/>) so looking down your eyes come
    /// forward over your chest.
    /// In first person the full mesh only casts the shadow, and a copy without the head is drawn instead. When the
    /// camera leaves the body (pilot or fly mode) the whole character is shown standing where you left it.
    /// </summary>
    [DefaultExecutionOrder(100)] // after the held item has placed itself for the frame
    public sealed class PlayerBody : MonoBehaviour
    {
        static readonly int SpeedId = Animator.StringToHash("Speed");
        static readonly int MoveScaleId = Animator.StringToHash("MoveScale");
        static readonly int JumpId = Animator.StringToHash("Jump");
        static readonly int HammerId = Animator.StringToHash("Hammer");
        static readonly int CrouchId = Animator.StringToHash("Crouch");
        static readonly int PunchChargeId = Animator.StringToHash("PunchCharge");
        static readonly int PunchId = Animator.StringToHash("Punch");
        static readonly int PunchSpeedId = Animator.StringToHash("PunchSpeed");
        static readonly int PunchVariantId = Animator.StringToHash("PunchVariant");
        /// <summary>
        /// For each punch (as CharacterGenerator.Punches orders them: straight, jab, hook), seconds from the throw at
        /// normal speed to the fist being fully out.
        /// </summary>
        static readonly float[] PunchLands = { 0.2f, 0.27f, 0.18f };
        /// <summary>
        /// Where the eyes are from the top of the neck (the head bone, where a head nods), looking level (metres, at the
        /// player's scale: measured on the lumberjack, eyes 1.655 m up and 5.6 cm in front of his middle, the head bone
        /// 1.551 m up and 6.9 cm behind it; set 5 cm above the eyes at Sam's word — at the eyes he felt short).
        /// Nodding about the top of the neck keeps the eyes above the collar looking down, over the front of the chest,
        /// so you see your shirt (about the neck's base they dropped below the collar and out past the chest).
        /// </summary>
        public static readonly Vector3 EyeFromHead = new Vector3(0f, 0.154f, 0.125f);
        /// <summary>How much of a look up or down the head itself nods (about the neck); the eyes turn the rest.</summary>
        public const float HeadNod = 0.5f;

        [SerializeField] FirstPersonController controller;
        [SerializeField] Animator animator;
        [SerializeField] SkinnedMeshRenderer full, torso, arm, leftArm;
        [SerializeField] Transform head, hips, spine;
        [Tooltip("Where a handle sits in each closed hand (hand bone space) and the way through it: from the fingers, by the editor.")]
        [SerializeField] Vector3 rightGripCentre, rightGripAxis = Vector3.forward, leftGripCentre, leftGripAxis = Vector3.forward;
        [Tooltip("The fastest each locomotion blend plays at its natural pace (m/s): forward, backing up, crouching. Faster than that, the clips play quicker so the feet keep up.")]
        [SerializeField] float forwardTop = 6.5f, backTop = 4.5f, crouchTop = 2f;
        [SerializeField] float damping = 0.12f;
        [Tooltip("Standing still: a slow breath — how far the chest rises and falls (degrees) and how long one breath takes (s).")]
        [SerializeField] float breathDegrees = 1.2f, breathSeconds = 4.5f;
        [Tooltip("How quickly the eyes follow the neck up and down (per second): high enough to crouch, low enough to smooth the walk's bob.")]
        [SerializeField] float eyeFollow = 12f;
        [Tooltip("The first-person swing is toned down so the tool in hand doesn't jump about while you aim.")]
        [Range(0f, 1f)] [SerializeField] float firstPersonSwing = 0.4f;
        [Tooltip("How quickly the legs turn to a new direction (per second).")]
        [SerializeField] float legTurnRate = 10f;
        [Tooltip("How much of the look the chest follows, looking down and up, and the most it leans each way (degrees). Little looking down: a hunched chest curls under the eyes and out of view.")]
        [SerializeField] float leanDown = 0.15f, leanUp = 0.5f, maxLeanDown = 10f, maxLeanUp = 30f;
        [Tooltip("Where each elbow points from its shoulder, in the chest's frame (x out to the right): out, down and back.")]
        [SerializeField] Vector3 elbowPole = new Vector3(0.5f, -0.6f, -0.3f);

        float legYaw;
        bool backing;
        bool charging;
        int punchVariant = -1;
        float fullSwingUntil;
        float upperWeight;
        float lean;
        float breath;
        Vector3 chestAverage;
        Quaternion chestAverageTurn = Quaternion.identity;
        bool chestTracked;

        /// <summary>
        /// The quick part of the chest's movement (world space): the bob and sway of each stride, without slow changes
        /// like crouching. Measured in the body's own frame (it turns with you, not with the look), from the chest as
        /// the animation has it before the look leans it, so looking up and down doesn't register. The tool in hand
        /// rides with it, so the hands and tool move together instead of the wrists twisting.
        /// </summary>
        public Vector3 GaitOffset { get; private set; }
        /// <summary>Degrees to bend forward at the waist (a chop leaning into a low cut): the camera goes with the head.</summary>
        public float ChopBend { get; set; }
        /// <summary>Between the shoulders (world), as they were placed last frame: where a two-handed swing reaches from.</summary>
        public Vector3 Shoulders { get; private set; }
        public bool HasShoulders { get; private set; }
        public Quaternion GaitTurn { get; private set; } = Quaternion.identity;
        Vector3 chestNow;
        Quaternion chestTurnNow = Quaternion.identity;
        bool hasMoveScale;

        CharacterController body;
        IHeldItem held;
        Vector3 restPosition;
        bool? shownFully;
        bool wasGrounded = true;
        bool still = true;
        Transform[] spineChain;
        Transform neck;
        float eyeHeight = float.NaN;
        Transform rightShoulder, leftShoulder;
        ArmReach rightReach, leftReach;
        HandPose rightHand, leftHand;
        Legs legs;
        int groundMask;

        public void Set(FirstPersonController fpc, Animator anim, SkinnedMeshRenderer fullMesh, SkinnedMeshRenderer torsoMesh,
            SkinnedMeshRenderer rightArmMesh, SkinnedMeshRenderer leftArmMesh, Transform headBone, Transform hipsBone, Transform spineBone)
        {
            hips = hipsBone;
            spine = spineBone;
            controller = fpc;
            animator = anim;
            full = fullMesh;
            torso = torsoMesh;
            arm = rightArmMesh;
            leftArm = leftArmMesh;
            head = headBone;
        }

        /// <summary>Where a handle sits in each closed hand, and the way through it (hand bone space).</summary>
        public void SetGrips(Vector3 rightCentre, Vector3 rightAxis, Vector3 leftCentre, Vector3 leftAxis)
        {
            rightGripCentre = rightCentre;
            rightGripAxis = rightAxis;
            leftGripCentre = leftCentre;
            leftGripAxis = leftAxis;
        }

        /// <summary>The top speed (m/s) each locomotion blend plays at its natural pace: forward, backing up, crouching.</summary>
        public void SetStrides(float forward, float back, float crouch)
        {
            forwardTop = forward;
            backTop = back;
            crouchTop = crouch;
        }

        Transform Bone(string name) => full != null ? System.Array.Find(full.bones, b => b != null && b.name == name) : null;

        void Awake()
        {
            body = controller != null ? controller.GetComponent<CharacterController>() : null;
            if (animator != null)
            {
                animator.applyRootMotion = false;
                foreach (var p in animator.parameters) if (p.nameHash == MoveScaleId) hasMoveScale = true;
            }
            restPosition = transform.localPosition;
            spineChain = new[] { Bone("Spine"), Bone("Spine01"), Bone("Spine02") };
            neck = Bone("neck") ?? Bone("Neck");
            if (controller != null && neck != null) controller.BodyDrivesCamera = true;
            rightShoulder = Bone("RightArm");
            leftShoulder = Bone("LeftArm");
            rightReach = new ArmReach(rightShoulder, Bone("RightForeArm"), Bone("RightHand"), rightGripCentre, rightGripAxis);
            leftReach = new ArmReach(leftShoulder, Bone("LeftForeArm"), Bone("LeftHand"), leftGripCentre, leftGripAxis);
            rightHand = new HandPose(Bone("RightHand"), false);
            leftHand = new HandPose(Bone("LeftHand"), true);
            float groundY = controller != null ? controller.transform.position.y : transform.position.y;
            legs = new Legs(hips, Bone("LeftUpLeg"), Bone("LeftLeg"), Bone("LeftFoot"), Bone("RightUpLeg"), Bone("RightLeg"), Bone("RightFoot"), groundY);
            // The feet stand on anything solid you could aim at: ground, pieces, trees; not you, what you carry or the tool in hand.
            groundMask = Cabin.Build.BuildLayers.AimMask & ~(1 << Cabin.Build.ToolViewModel.Layer);
        }

        void Start()
        {
            held = controller != null ? controller.GetComponentInChildren<IHeldItem>(true) : null;
        }

        /// <summary>The camera isn't yours: pilot or fly mode, or the controller is off (something else — a test, a cutscene — has the camera).</summary>
        bool OutOfBody => controller != null && (controller.ExternalControl || controller.Flying || !controller.isActiveAndEnabled);

        void Update()
        {
            bool outOfBody = OutOfBody;
            if (shownFully != outOfBody)
            {
                shownFully = outOfBody;
                if (full != null) full.shadowCastingMode = outOfBody ? ShadowCastingMode.On : ShadowCastingMode.ShadowsOnly;
                if (torso != null) torso.enabled = !outOfBody;
                // The arms are part of the body in first person too; out of body the full mesh draws them.
                if (arm != null) arm.enabled = !outOfBody;
                if (leftArm != null) leftArm.enabled = !outOfBody;
            }
            transform.localPosition = restPosition;

            if (animator == null) return;
            float speed = 0f, yaw = 0f;
            if (body != null && body.enabled && !outOfBody)
            {
                Vector3 v = transform.parent != null ? transform.parent.InverseTransformDirection(body.velocity) : body.velocity;
                v.y = 0f;
                speed = v.magnitude;
                if (speed > 0.3f)
                {
                    // Which way the legs face: forward for anything from straight ahead round to the sides, the
                    // back-walk once clearly backing up (with a margin so pure sideways doesn't flicker between them).
                    float angle = Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg;
                    backing = backing ? Mathf.Abs(angle) > 100f : Mathf.Abs(angle) > 120f;
                    yaw = backing ? Mathf.DeltaAngle(180f, angle) : Mathf.Clamp(angle, -90f, 90f);
                    if (backing) speed = -speed;
                }
            }
            still = Mathf.Abs(speed) < 0.25f;
            bool crouching = !outOfBody && controller != null && controller.Crouching;
            legYaw = Mathf.LerpAngle(legYaw, yaw, 1f - Mathf.Exp(-legTurnRate * Time.deltaTime));
            animator.SetFloat(SpeedId, speed, damping, Time.deltaTime);
            if (hasMoveScale) animator.SetFloat(MoveScaleId, MoveScale(speed, crouching, forwardTop, backTop, crouchTop), damping, Time.deltaTime);
            animator.SetBool(CrouchId, crouching);
            if (animator.layerCount > 1)
            {
                // A punch is seen in full; the hammer swing only partly, so the aim doesn't jump about.
                bool punching = Punching;
                float target = outOfBody || punching ? 1f : firstPersonSwing;
                upperWeight = Mathf.MoveTowards(upperWeight, target, Time.deltaTime * 8f);
                animator.SetLayerWeight(1, upperWeight);
            }

            // Leaving the ground going up is a jump.
            bool grounded = body == null || !body.enabled || body.isGrounded;
            if (wasGrounded && !grounded && body.velocity.y > 1f && !outOfBody) animator.SetTrigger(JumpId);
            wasGrounded = grounded;
        }

        bool Punching => charging || Time.time < fullSwingUntil;

        /// <summary>
        /// How much faster than recorded the legs play: 1 up to the blend's top natural speed, then in proportion, so
        /// the feet move over the ground as fast as the body does.
        /// </summary>
        public static float MoveScale(float speed, bool crouching, float forwardTop, float backTop, float crouchTop)
        {
            float top = crouching ? crouchTop : speed >= 0f ? forwardTop : backTop;
            float s = Mathf.Abs(speed);
            return top > 0.01f && s > top ? s / top : 1f;
        }

        void LateUpdate()
        {
            bool outOfBody = OutOfBody;
            float dt = Time.deltaTime;
            TurnLegs();
            SampleChest();
            // The chest leans first, then the body is slid so the neck is over the middle of the player, then the
            // feet are put on the ground (sliding the body after would drag them off where they were planted), and
            // last the eyes go where the neck now is, so the camera is where the head is whatever the pose.
            if (!outOfBody) LeanWithLook(dt);
            Breathe(dt);
            if (!outOfBody) CentreOnNeck();
            if (controller != null && !controller.Flying)
            {
                bool grounded = body == null || !body.enabled || body.isGrounded;
                Vector3 forward = Quaternion.AngleAxis(legYaw, Vector3.up) * Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
                legs.Solve(forward, controller.transform.position.y, grounded, still || outOfBody, groundMask, dt);
            }
            if (!outOfBody) Bend();
            if (!outOfBody) PlaceEyes(dt);
            TrackChest(outOfBody, dt);

            bool holding = !outOfBody && held != null && held.TryGetGrip(out _, out _);
            bool twoHanded = holding && held.TryGetSecondGrip(out _, out _);
            bool punching = Punching;
            rightHand.Set(holding ? held.Shape : punching ? HandShape.Fist : HandShape.Relaxed);
            leftHand.Set(twoHanded ? HandShape.Grip : punching ? HandShape.Fist : HandShape.Relaxed);
            if (holding) Reach(twoHanded);
            rightHand.Apply(dt);
            leftHand.Apply(dt);
        }

        /// <summary>The body's own arms take hold of the tool: the right always, the left too on a two-handed one.</summary>
        void Reach(bool twoHanded)
        {
            var chest = Quaternion.AngleAxis(lean, controller.transform.right) * controller.transform.rotation;
            Vector3 forward = chest * Vector3.forward;
            Vector3 Pole(Transform shoulder, bool left) =>
                shoulder.position + chest * new Vector3(left ? -elbowPole.x : elbowPole.x, elbowPole.y, elbowPole.z);
            held.TryGetGrip(out var wrist, out var grip);
            if (held.TryGetHandle(false, out var point, out var along))
            {
                // A handle: the hand closes round it; out of reach, the tool comes to the hand.
                Vector3 shortBy = rightReach.HoldHandle(point, along, Pole(rightShoulder, false), forward);
                if (shortBy.sqrMagnitude > 1e-6f) held.MoveGripTo(wrist + shortBy);
                if (twoHanded && held.TryGetHandle(true, out var leftPoint, out var leftAlong))
                    leftReach.HoldHandle(leftPoint, leftAlong, Pole(leftShoulder, true), forward);
                return;
            }
            Vector3 reached = rightReach.Hold(wrist, grip, Pole(rightShoulder, false));
            // Out of reach: the tool comes to the hand rather than floating off it.
            if ((reached - wrist).sqrMagnitude > 1e-6f) held.MoveGripTo(reached);
            if (twoHanded && held.TryGetSecondGrip(out var leftWrist, out var leftGrip)) leftReach.Hold(leftWrist, leftGrip, Pole(leftShoulder, true));
        }

        /// <summary>The chest leans with the look, spread up the spine: the shoulders follow your eyes up and down.</summary>
        void LeanWithLook(float dt)
        {
            if (controller == null) return;
            float pitch = controller.Pitch;
            float want = pitch > 0f ? Mathf.Min(pitch * leanDown, maxLeanDown) : Mathf.Max(pitch * leanUp, -maxLeanUp);
            lean = Mathf.Lerp(lean, want, 1f - Mathf.Exp(-20f * dt));
            Vector3 axis = controller.transform.right;
            float[] share = { 0.25f, 0.35f, 0.4f };
            for (int i = 0; i < spineChain.Length; i++)
                if (spineChain[i] != null) spineChain[i].rotation = Quaternion.AngleAxis(lean * share[i], axis) * spineChain[i].rotation;
        }

        /// <summary>The chest as the animation has it (before the look leans it), relative to the neck, in the body's frame.</summary>
        void SampleChest()
        {
            var chest = spineChain != null ? spineChain[spineChain.Length - 1] : null;
            var player = controller != null ? controller.transform : null;
            if (chest == null || player == null) return;
            var pivot = neck != null ? neck : chest;
            chestNow = player.InverseTransformDirection(chest.position - pivot.position) + Vector3.up * player.InverseTransformPoint(chest.position).y;
            chestTurnNow = Quaternion.Inverse(player.rotation) * chest.rotation;
        }

        /// <summary>The chest's pose in the body's frame, less its slow average: what the stride does to it (as world offsets).</summary>
        void TrackChest(bool outOfBody, float dt)
        {
            var player = controller != null ? controller.transform : null;
            if (outOfBody || player == null || spineChain == null || spineChain[spineChain.Length - 1] == null)
            {
                chestTracked = false;
                GaitOffset = Vector3.zero;
                GaitTurn = Quaternion.identity;
                return;
            }
            Vector3 p = chestNow;
            Quaternion r = chestTurnNow;
            if (!chestTracked)
            {
                chestAverage = p;
                chestAverageTurn = r;
                chestTracked = true;
            }
            float k = 1f - Mathf.Exp(-2.5f * dt);
            chestAverage = Vector3.Lerp(chestAverage, p, k);
            chestAverageTurn = Quaternion.Slerp(chestAverageTurn, r, k);
            GaitOffset = player.TransformDirection(Vector3.ClampMagnitude(p - chestAverage, 0.12f));
            GaitTurn = player.rotation * (r * Quaternion.Inverse(chestAverageTurn)) * Quaternion.Inverse(player.rotation);
        }

        /// <summary>Bends forward at the waist by <see cref="ChopBend"/> (after the body is centred, so the shoulders really go forward); notes where the shoulders are.</summary>
        void Bend()
        {
            var waist = spineChain != null ? spineChain[0] : null;
            if (waist != null && ChopBend > 0.01f && controller != null)
                waist.rotation = Quaternion.AngleAxis(ChopBend, controller.transform.right) * waist.rotation;
            if (rightShoulder != null && leftShoulder != null)
            {
                Shoulders = (rightShoulder.position + leftShoulder.position) * 0.5f;
                HasShoulders = true;
            }
        }

        /// <summary>Standing still (the idle is held still), the chest rises and falls slowly; it fades out as you move.</summary>
        void Breathe(float dt)
        {
            breath = Mathf.MoveTowards(breath, still ? 1f : 0f, dt * 2f);
            if (breath <= 0f || spineChain == null || controller == null) return;
            float angle = Mathf.Sin(Time.time * 2f * Mathf.PI / Mathf.Max(breathSeconds, 0.5f)) * breathDegrees * breath;
            Vector3 axis = controller.transform.right;
            // The chest lifts back a touch on the in-breath, the neck turning back so the head stays level.
            var chest = spineChain[spineChain.Length - 1];
            if (chest != null) chest.rotation = Quaternion.AngleAxis(-angle, axis) * chest.rotation;
            if (neck != null) neck.rotation = Quaternion.AngleAxis(angle, axis) * neck.rotation;
        }

        /// <summary>Turns the hips (legs) towards the way you're moving and the spine back, so the chest faces forward.</summary>
        void TurnLegs()
        {
            if (hips == null || spine == null || Mathf.Abs(legYaw) < 0.1f) return;
            Vector3 up = transform.up;
            hips.rotation = Quaternion.AngleAxis(legYaw, up) * hips.rotation;
            spine.rotation = Quaternion.AngleAxis(-legYaw, up) * spine.rotation;
        }

        /// <summary>Slides the animated body so its neck is over the middle of the player (horizontally; the feet stay down).</summary>
        void CentreOnNeck()
        {
            var pivot = neck != null ? neck : head;
            if (pivot == null || controller == null) return;
            Vector3 shift = controller.transform.position - pivot.position;
            shift.y = 0f;
            transform.position += shift;
        }

        /// <summary>Where the eyes are for a look: from the head bone (the top of the neck), nodded by <see cref="HeadNod"/> of the pitch (degrees, down positive), in the player's frame.</summary>
        public static Vector3 EyeOffset(float pitch) => Quaternion.Euler(pitch * HeadNod, 0f, 0f) * EyeFromHead;

        /// <summary>
        /// Puts the camera at the eyes: on the head bone (the top of the neck) as it is this frame (its height smoothed, so the walk's bob is
        /// gentle), the head nodding through part of the look so looking down brings the eyes forward over the chest.
        /// </summary>
        void PlaceEyes(float dt)
        {
            var eye = controller != null ? controller.CameraRoot : null;
            var pivot = head != null ? head : neck;
            if (eye == null || pivot == null) return;
            float height = controller.transform.InverseTransformPoint(pivot.position).y;
            eyeHeight = float.IsNaN(eyeHeight) ? height : Mathf.Lerp(eyeHeight, height, 1f - Mathf.Exp(-eyeFollow * dt));
            // The head is over the middle of the player (CentreOnNeck) unless the body is bent into a chop: then forward with it.
            Vector3 lean = neck != null ? controller.transform.InverseTransformPoint(neck.position) : Vector3.zero;
            lean.y = 0f;
            eye.localPosition = new Vector3(lean.x, eyeHeight, lean.z) + EyeOffset(controller.Pitch);
        }

        /// <summary>
        /// Picks a punch at random (never the same twice running), draws the fist back and holds it there until
        /// <see cref="ThrowPunch"/>.
        /// </summary>
        public void ChargePunch()
        {
            if (animator == null) return;
            charging = true;
            int pick = Random.Range(0, PunchLands.Length - 1);
            punchVariant = punchVariant >= 0 && pick >= punchVariant ? pick + 1 : pick;
            animator.SetInteger(PunchVariantId, punchVariant);
            animator.ResetTrigger(PunchId);
            animator.SetBool(PunchChargeId, true);
        }

        /// <summary>
        /// Throws the punch; <paramref name="power"/> 0–1 (how long it was wound up) makes it quicker. Returns the
        /// seconds until the fist is fully out — when it lands.
        /// </summary>
        public float ThrowPunch(float power)
        {
            if (animator == null) return 0.15f;
            if (!charging) ChargePunch(); // a tap: pick the punch now
            charging = false;
            float speed = Mathf.Lerp(1.1f, 1.8f, power);
            animator.SetFloat(PunchSpeedId, speed);
            animator.SetBool(PunchChargeId, false);
            animator.SetTrigger(PunchId);
            fullSwingUntil = Time.time + 0.8f;
            return PunchLands[punchVariant] / speed;
        }

        public void CancelPunch()
        {
            if (animator == null || !charging) return;
            charging = false;
            animator.SetBool(PunchChargeId, false);
        }

        /// <summary>Swing the hammer (upper body only; the trigger and the state share the name), unless a swing is already under way.</summary>
        public void Hammer()
        {
            if (animator == null || animator.layerCount < 2) return;
            if (animator.IsInTransition(1) || animator.GetCurrentAnimatorStateInfo(1).shortNameHash == HammerId) return;
            animator.SetTrigger(HammerId);
        }
    }
}
