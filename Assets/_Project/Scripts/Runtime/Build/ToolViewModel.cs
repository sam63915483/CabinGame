using Cabin.Player;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;

namespace Cabin.Build
{
    /// <summary>
    /// The tool in the player's right hand, drawn by an overlay camera so it never clips into walls. Shows the nail
    /// gun or spray can for the selected tool, sways a little behind the mouse, kicks back when a nail is driven,
    /// presses the can's button while spraying and colours its label with the current paint. It's the player's
    /// <see cref="IHeldItem"/>: the body's right arm reaches for its grip, and if the arm can't reach, the tool goes
    /// to the hand. The axe is held in both: the left hand low by the knob, the right up the handle. Up against a wall
    /// the tool is drawn back and down towards you instead of going through it.
    /// </summary>
    public sealed class ToolViewModel : MonoBehaviour, IHeldItem
    {
        public const int Layer = 13;
        public const string LayerName = "ViewModel";
        /// <summary>The spray can's nozzle tip in its own space (see ToolModelGenerator).</summary>
        static readonly Vector3 NozzleTip = new Vector3(0f, 0.101f, 0.0125f);
        /// <summary>The nail gun's nose, where nails come out, in its own space.</summary>
        static readonly Vector3 GunMuzzle = new Vector3(0f, 0.05f, 0.25f);

        [SerializeField] GameObject nailGunPrefab;
        [SerializeField] GameObject sprayCanPrefab;
        [SerializeField] GameObject axePrefab;
        [Tooltip("Where the right wrist sits on the axe handle and how the hand is turned, in the axe's own space (how far up the handle comes from the pose: it slides).")]
        [SerializeField] Vector3 axeWrist = new Vector3(0.03f, 0.2f, -0.135f);
        [SerializeField] Vector3 axeHand = new Vector3(270f, 180f, 0f);
        [Tooltip("Where the left wrist sits on the axe handle (down by the knob) and how that hand is turned, in the axe's own space.")]
        [SerializeField] Vector3 axeLeftWrist = new Vector3(-0.03f, -0.06f, -0.135f);
        [SerializeField] Vector3 axeLeftHand = new Vector3(270f, 180f, 0f);
        [SerializeField] Vector3 gunPosition = new Vector3(0.16f, -0.15f, 0.42f);
        [Tooltip("The gun turns to point at whatever the crosshair is on; only its roll (z) is used, canted a little.")]
        [SerializeField] Vector3 gunEuler = new Vector3(5f, -20f, -8f);
        [Tooltip("Nearer than this the gun aims as if at this distance, so it doesn't swing wildly up close.")]
        [SerializeField] float minAimDistance = 1.2f;
        [Tooltip("Nothing under the crosshair: aim at a point this far out.")]
        [SerializeField] float openAimDistance = 25f;
        [SerializeField] Vector3 canPosition = new Vector3(0.15f, -0.1f, 0.4f);
        [SerializeField] Vector3 canEuler = new Vector3(10f, -15f, 0f);
        [Tooltip("Where the right wrist sits on each tool and how the hand is turned, in the tool's own space.")]
        [SerializeField] Vector3 gunWrist = new Vector3(0.03f, -0.035f, -0.09f);
        [SerializeField] Vector3 gunHand = new Vector3(284f, 180f, 0f);
        [SerializeField] Vector3 canWrist = new Vector3(0.045f, -0.06f, -0.125f);
        [SerializeField] Vector3 canHand = new Vector3(290f, 180f, 0f);
        [Tooltip("Chopping: how far (m) the hands reach out from between the shoulders towards the spot you're chopping at (the arms nearly straight).")]
        [SerializeField] float chopReach = 0.5f;
        [Tooltip("How far out in front the tool reaches (m): a wall nearer than this pushes it back towards you.")]
        [SerializeField] float wallReach = 0.65f;
        [Tooltip("How much of a look up / down the tool follows: it stays out in front of the chest, where the arms reach it, rather than going up into your face or down into your belly.")]
        [Range(0f, 1f)] [SerializeField] float followUp = 0.65f, followDown = 0.65f;

        BuildToolbelt belt;
        FirstPersonController player;
        PlayerBody body;
        [Tooltip("How much of the chest's stride bob and sway (position, turn) the tool rides with: the hands and tool move together as you walk and run.")]
        [Range(0f, 1.5f)] [SerializeField] float gaitFollow = 1f, gaitTurnFollow = 1f;
        /// <summary>From view space to the space the tool is carried in: the view pitched back down by the part of a look up the tool doesn't follow.</summary>
        Quaternion carry = Quaternion.identity;
        GameObject gun, can, axe;
        Material labelMaterial;
        Transform actuator;
        Vector3 actuatorRest;
        float kick;
        Vector2 sway;
        Vector3 aimLocal = Vector3.forward;
        Transform active;
        Vector3 wristLocal, handEuler;
        bool twoHanded;
        float slide;
        float pullBack;
        HandShape shape;

        public void Set(GameObject nailGun, GameObject sprayCan, GameObject axeModel)
        {
            nailGunPrefab = nailGun;
            sprayCanPrefab = sprayCan;
            axePrefab = axeModel;
        }

        /// <summary>
        /// The hands and tool are drawn by the main camera, not an overlay camera of their own. The overlay cost a
        /// second camera's culling and render setup every frame (~1.3 ms of main thread, 0.9 ms of render thread and
        /// 0.7 ms of GPU on the laptop), for a narrower lens on the hands and never clipping a wall. The prefab still
        /// carries the overlay camera; it is taken out of the stack here so the prefab needs no regeneration.
        /// </summary>
        public const bool OverlayCamera = false;

        void Awake()
        {
            if (OverlayCamera || !TryGetComponent<Camera>(out var overlay)) return;
            var main = transform.parent != null ? transform.parent.GetComponent<Camera>() : null;
            if (main == null) return;
            if (main.TryGetComponent<UniversalAdditionalCameraData>(out var stack)) stack.cameraStack.Remove(overlay);
            overlay.enabled = false;
            main.cullingMask |= 1 << Layer;
        }

        void Start()
        {
            belt = FindFirstObjectByType<BuildToolbelt>();
            player = GetComponentInParent<FirstPersonController>();
            body = player != null ? player.GetComponentInChildren<PlayerBody>(true) : null;
            if (nailGunPrefab != null) gun = Spawn(nailGunPrefab);
            if (axePrefab != null) axe = Spawn(axePrefab);
            if (sprayCanPrefab != null)
            {
                can = Spawn(sprayCanPrefab);
                // Its own label material: per-renderer overrides don't survive the GPU Resident Drawer.
                var label = can.transform.Find("Label")?.GetComponent<Renderer>();
                if (label != null) labelMaterial = label.material;
                actuator = can.transform.Find("Actuator");
                if (actuator != null) actuatorRest = actuator.localPosition;
            }
        }

        GameObject Spawn(GameObject prefab)
        {
            var go = Instantiate(prefab, transform);
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = Layer;
            go.SetActive(false);
            return go;
        }

        /// <summary>Where the spray leaves the can in hand (its nozzle tip), if the can is out.</summary>
        public bool TryGetNozzle(out Vector3 nozzle)
        {
            bool shown = can != null && can.activeSelf;
            nozzle = shown ? can.transform.TransformPoint(NozzleTip) : Vector3.zero;
            return shown;
        }

        /// <summary>Where nails leave the gun in hand, if the gun is out.</summary>
        public bool TryGetMuzzle(out Vector3 muzzle)
        {
            bool shown = gun != null && gun.activeSelf;
            muzzle = shown ? gun.transform.TransformPoint(GunMuzzle) : Vector3.zero;
            return shown;
        }

        /// <summary>Recoil when the nail gun fires.</summary>
        public void Kick() => kick = 1f;

        void OnDestroy()
        {
            if (labelMaterial != null) Destroy(labelMaterial);
        }

        public bool TryGetGrip(out Vector3 wrist, out Quaternion hand)
        {
            wrist = active != null ? active.TransformPoint(wristLocal) : Vector3.zero;
            hand = active != null ? active.rotation * Quaternion.Euler(handEuler) : Quaternion.identity;
            return active != null;
        }

        public HandShape Shape => shape;

        /// <summary>The axe's handle runs up its y; the left hand low by the knob, the right a hand up (sliding).</summary>
        public bool TryGetHandle(bool left, out Vector3 point, out Vector3 along)
        {
            bool held = active != null && twoHanded;
            point = held ? active.TransformPoint(new Vector3(0f, left ? axeLeftWrist.y : slide, 0f)) : Vector3.zero;
            along = held ? active.up : Vector3.up;
            return held;
        }

        public bool TryGetSecondGrip(out Vector3 wrist, out Quaternion hand)
        {
            bool held = active != null && twoHanded;
            wrist = held ? active.TransformPoint(axeLeftWrist) : Vector3.zero;
            hand = held ? active.rotation * Quaternion.Euler(axeLeftHand) : Quaternion.identity;
            return held;
        }

        public void MoveGripTo(Vector3 wrist)
        {
            if (active != null) active.position += wrist - active.TransformPoint(wristLocal);
        }

        // The axe in both hands, at rest and through a swing, in view space: where its grip point is (the left hand sits
        // just below it), which way the handle runs (towards the head), which way the blade faces, and how far up the
        // handle the right hand is. At rest it's carried across the body, head up by the right shoulder; a chop winds
        // back over the right shoulder and comes round flat into the trunk, the right hand sliding down to the left; a
        // plain swing comes over the top and down. Worked out so both hands stay within reach of their shoulders
        // (otherwise the arm can't get there and the axe is pulled back to the hand).
        static readonly AxePose AxeRest = new AxePose(new Vector3(-0.020f, -0.190f, 0.330f), new Vector3(0.860f, 0.320f, 0.360f), new Vector3(-0.450f, 0.250f, 0.850f), 0.170f);
        static readonly AxePose ChopBack = new AxePose(new Vector3(0.226f, -0.113f, 0.133f), new Vector3(0.750f, 0.450f, -0.450f), new Vector3(-0.200f, 0.000f, 1.000f), 0.200f);
        static readonly AxePose ChopHit = new AxePose(new Vector3(0.048f, -0.163f, 0.318f), new Vector3(-0.200f, 0.120f, 0.970f), new Vector3(-1.000f, 0.000f, -0.200f), 0.040f);
        static readonly AxePose SwingBack = new AxePose(new Vector3(0.258f, -0.112f, 0.009f), new Vector3(0.300f, 0.800f, -0.520f), new Vector3(0.000f, 0.600f, 0.800f), 0.200f);
        static readonly AxePose SwingHit = new AxePose(new Vector3(0.060f, -0.157f, 0.300f), new Vector3(0.000f, -0.120f, 0.990f), new Vector3(0.000f, -1.000f, -0.120f), 0.060f);

        void PoseAxe(AxeTool tool, Quaternion sway)
        {
            float t = tool.SwingT;
            AxePose pose = AxeRest;
            if (t >= 0f)
            {
                float total = AxeTool.WindUp + AxeTool.Strike + AxeTool.Recover;
                float s = t * total;
                var back = tool.Chopping ? ChopBack : SwingBack;
                var hit = tool.Chopping ? ChopHit : SwingHit;
                // Chopping: the strike comes down on the trunk where you're looking (in the carried frame, as the pose is).
                if (tool.Chopping && tool.TryGetStrikePoint(out var strike) && body != null && body.HasShoulders)
                {
                    var toCarried = Quaternion.Inverse(carry);
                    Vector3 target = toCarried * transform.InverseTransformPoint(strike);
                    Vector3 shoulders = toCarried * transform.InverseTransformPoint(body.Shoulders);
                    hit = hit.AimFrom(shoulders, chopReach, target);
                }
                if (s < AxeTool.WindUp) pose = AxePose.Lerp(AxeRest, back, Mathf.SmoothStep(0f, 1f, s / AxeTool.WindUp));
                else if (s < AxeTool.WindUp + AxeTool.Strike)
                {
                    float u = (s - AxeTool.WindUp) / AxeTool.Strike;
                    pose = AxePose.Lerp(back, hit, u * u);
                }
                else pose = AxePose.Lerp(hit, AxeRest, Mathf.SmoothStep(0f, 1f, (s - AxeTool.WindUp - AxeTool.Strike) / AxeTool.Recover));
            }
            axe.transform.localPosition = carry * pose.Hand;
            axe.transform.localRotation = sway * carry * pose.Rotation;
            slide = pose.Slide;
        }

        /// <summary>The tool moves with the chest's stride (last frame's, from the body), so it bobs and sways in the hands.</summary>
        void RideGait()
        {
            if (body == null || active == null) return;
            active.position += body.GaitOffset * gaitFollow;
            active.rotation = Quaternion.Slerp(Quaternion.identity, body.GaitTurn, gaitTurnFollow) * active.rotation;
        }

        /// <summary>A wall (or tree, or piece) closer than the tool reaches: the tool comes back and down towards you, and the arm with it.</summary>
        void KeepOutOfWalls()
        {
            int mask = BuildLayers.AimMask & ~(1 << Layer);
            float wanted = 0f;
            if (Physics.SphereCast(transform.position, 0.06f, transform.forward, out var hit, wallReach, mask, QueryTriggerInteraction.Ignore))
                wanted = Mathf.Clamp(wallReach - hit.distance, 0f, wallReach * 0.7f);
            pullBack = Mathf.Lerp(pullBack, wanted, 1f - Mathf.Exp(-(wanted > pullBack ? 30f : 10f) * Time.deltaTime));
            if (pullBack > 1e-4f) active.localPosition += new Vector3(0f, -0.35f * pullBack, -pullBack);
        }

        void Grip(Transform tool, Vector3 wrist, Vector3 hand)
        {
            active = tool;
            wristLocal = wrist;
            handEuler = hand;
        }

        /// <summary>The direction, in view space, from <paramref name="from"/> (view space) to what the crosshair is on.</summary>
        Vector3 AimFrom(Vector3 from)
        {
            var ray = new Ray(transform.position, transform.forward);
            float distance = Physics.Raycast(ray, out var hit, openAimDistance, BuildLayers.AimMask & ~BuildLayers.NailMask, QueryTriggerInteraction.Ignore)
                ? Mathf.Max(hit.distance, minAimDistance)
                : openAimDistance;
            return (Vector3.forward * distance - from).normalized;
        }

        void LateUpdate()
        {
            var tool = belt != null ? belt.Current : null;
            bool hands = belt == null || belt.Carry.Holding || belt.Carry.Piloting;
            bool showGun = !hands && tool is NailGunTool;
            bool showCan = !hands && tool is SprayTool;
            bool showAxe = !hands && tool is AxeTool;
            if (axe != null && axe.activeSelf != showAxe) axe.SetActive(showAxe);
            if (gun != null && gun.activeSelf != showGun) gun.SetActive(showGun);
            if (can != null && can.activeSelf != showCan) can.SetActive(showCan);
            twoHanded = showAxe;
            if (!showGun && !showCan && !showAxe)
            {
                active = null;
                pullBack = 0f;
                return;
            }

            // Lag a touch behind the mouse, ease back to rest.
            Vector2 d = Mouse.current != null && Cabin.Core.CursorCapture.Captured ? Mouse.current.delta.ReadValue() : Vector2.zero;
            sway = Vector2.Lerp(sway, Vector2.ClampMagnitude(d * 0.04f, 2.5f), 1f - Mathf.Exp(-12f * Time.deltaTime));
            kick = Mathf.MoveTowards(kick, 0f, Time.deltaTime * 6f);
            float k = kick * kick;
            Quaternion swayRot = Quaternion.Euler(sway.y, -sway.x, 0f);
            float pitch = player != null ? player.Pitch : 0f;
            carry = Quaternion.Euler(-pitch * (1f - (pitch < 0f ? followUp : followDown)), 0f, 0f);

            if (showAxe)
            {
                PoseAxe((AxeTool)tool, swayRot);
                Grip(axe.transform, new Vector3(axeWrist.x, slide, axeWrist.z), axeHand);
                shape = HandShape.Grip;
            }
            else if (showGun)
            {
                gun.transform.localPosition = carry * (gunPosition + new Vector3(0f, 0.01f * k, -0.05f * k));
                // The nose points at what the crosshair is on (where the nail goes), easing over when that jumps.
                aimLocal = Vector3.Slerp(aimLocal, AimFrom(gun.transform.localPosition + GunMuzzle.y * Vector3.up), 1f - Mathf.Exp(-25f * Time.deltaTime));
                var aim = Quaternion.LookRotation(aimLocal, Vector3.up) * Quaternion.Euler(0f, 0f, gunEuler.z);
                gun.transform.localRotation = swayRot * aim * Quaternion.Euler(-12f * k, 0f, 0f);
                Grip(gun.transform, gunWrist, gunHand);
                shape = HandShape.Pistol;
            }
            else
            {
                var spray = (SprayTool)tool;
                can.transform.localPosition = carry * canPosition;
                can.transform.localRotation = swayRot * carry * Quaternion.Euler(canEuler);
                if (labelMaterial != null) labelMaterial.color = spray.Color;
                if (actuator != null)
                    actuator.localPosition = actuatorRest + (belt.UseHeld ? Vector3.down * 0.0025f : Vector3.zero);
                Grip(can.transform, canWrist, canHand);
                shape = HandShape.Can;
            }
            RideGait();
            KeepOutOfWalls();
        }
    }
}
