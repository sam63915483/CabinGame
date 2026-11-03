using System.Collections.Generic;
using Cabin.Core;
using Cabin.Player;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Cabin.Build
{
    /// <summary>
    /// The piece in the player's hands. It is a real physics body steered towards where the player looks, so it
    /// presses against wood instead of passing through it; the hands never go into the ground or the racks.
    /// Its rotation is held relative to the player's facing, so turning round keeps the same face towards you.
    /// Scroll sets how far away. Rotation: R turns it about the vertical, T tips it towards/away from you, Y rolls it
    /// (each 90°, Shift for 15°), V squares it to the world, F aligns it to the piece under the crosshair (Shift+F:
    /// across it); hold RMB and drag to turn it freely, locked to whichever way you drag first (Shift: 15° steps).
    /// G enters pilot mode: the camera orbits the piece, WASD / Space / Ctrl move it, the mouse orbits.
    /// E drops it. LMB places it, depending on the piece and the aim:
    ///  - a deck block aimed at the ground sits level, sunk so no corner floats, and is planted;
    ///  - a 4×4 aimed at an empty, planted deck block seats in its socket, plumb, and is planted;
    ///  - anything aimed at the cutting table lies flat on it;
    ///  - anything else is clamped where it is to whatever it's pressed against (nothing touching: nothing happens).
    /// </summary>
    public sealed class CarryController : MonoBehaviour
    {
        [SerializeField] BuildSite site;
        [SerializeField] float minDistance = 0.8f;
        [SerializeField] float maxDistance = 6f;
        [SerializeField] float moveGain = 14f;
        [SerializeField] float maxSpeed = 7f;
        [SerializeField] float turnGain = 14f;
        [SerializeField] float maxTurnSpeed = 12f;
        [SerializeField] float dragDegreesPerPixel = 0.3f;
        [SerializeField] float pilotSpeed = 2.4f;
        [SerializeField] float pilotSlowSpeed = 0.5f;
        [SerializeField] float orbitDegreesPerPixel = 0.15f;

        public enum Placement { Air, PostInBlock, Block, Table }

        const float FineStep = 15f;
        const float DragLockPixels = 6f;
        /// <summary>Deck blocks this close are lined up against; within <see cref="AlignSnapDegrees"/> the new one snaps square.</summary>
        const float GuideRange = 8f;
        const int MaxGuides = 4;
        const float AlignSnapDegrees = 3f;

        Camera cam;
        FirstPersonController body;
        Piece held;
        float distance = 2.2f;
        /// <summary>Rotation relative to the player's facing (normal mode) — see <see cref="WorldRotation"/>.</summary>
        Quaternion relativeRotation = Quaternion.identity;
        /// <summary>World rotation while piloting: orbiting the camera must not turn the piece.</summary>
        Quaternion pilotRotation = Quaternion.identity;
        Vector3 targetPosition;
        Quaternion targetRotation;
        Placement placement;
        Piece targetBlock;
        int touching;
        readonly System.Collections.Generic.List<BlockGuide> guides = new System.Collections.Generic.List<BlockGuide>();
        InputAction altUse, adjust, modifier, turn, tip, roll, squareUp, move, jump, crouch;

        // RMB drag: locked to one axis, measured from where the drag started.
        bool dragging;
        int dragAxis;          // 0 undecided, 1 turn (horizontal drag), 2 tip (vertical drag)
        Vector2 dragPixels;
        Quaternion dragStart;

        // Pilot mode
        bool piloting;
        Vector3 pilotTarget;
        float orbitYaw, orbitPitch, orbitDistance = 3f;
        Vector3 savedCamLocalPos;
        Quaternion savedCamLocalRot;

        public bool Holding => held != null;
        public Piece Held => held;
        public Placement Current => placement;
        /// <summary>How many pieces the held piece is touching right now (what LMB would clamp it to).</summary>
        public int Touching => touching;
        /// <summary>Lines from set deck blocks to the block being placed (empty otherwise).</summary>
        public System.Collections.Generic.IReadOnlyList<BlockGuide> Guides => guides;
        public float Distance => distance;
        public bool Rotating { get; private set; }
        public bool Piloting => piloting;

        /// <summary>Where the hands are. The piece is pushed towards this and stops against whatever is in the way.</summary>
        public Vector3 HoldPoint
        {
            get
            {
                if (cam == null) cam = Camera.main;
                if (cam == null) return transform.position;
                if (piloting) return pilotTarget;
                return cam.transform.position + cam.transform.forward * distance;
            }
        }

        /// <summary>Horizontal frame the rotation keys work in: the way the player (or the pilot camera) faces.</summary>
        Quaternion Frame
        {
            get
            {
                if (cam == null) cam = Camera.main;
                Vector3 f = Vector3.ProjectOnPlane(cam != null ? cam.transform.forward : Vector3.forward, Vector3.up);
                return f.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(f.normalized, Vector3.up) : Quaternion.identity;
            }
        }

        /// <summary>The held piece's intended world rotation. Normal mode follows the player's facing.</summary>
        public Quaternion WorldRotation
        {
            get => piloting ? pilotRotation : Frame * relativeRotation;
            set
            {
                if (piloting) pilotRotation = value;
                else relativeRotation = Quaternion.Inverse(Frame) * value;
            }
        }

        public void SetDistance(float metres) => distance = Mathf.Clamp(metres, minDistance, maxDistance);

        void Awake()
        {
            altUse = Controls.Get("Player/AltUse");
            adjust = Controls.Get("Build/Adjust");
            modifier = Controls.Get("Build/Modifier");
            turn = Controls.Get("Build/Rotate");
            tip = Controls.Get("Build/Tip");
            roll = Controls.Get("Build/Roll");
            squareUp = Controls.Get("Build/SquareUp");
            move = Controls.Get("Player/Move");
            jump = Controls.Get("Player/Jump");
            crouch = Controls.Get("Player/Crouch");
        }

        void Start()
        {
            cam = Camera.main;
            body = cam != null ? cam.GetComponentInParent<FirstPersonController>() : null;
        }

        /// <summary>
        /// How a fresh piece sits in the hands: square to the player — grain (local x) across the view, its face
        /// (local z is the thickness) towards the player. A plywood sheet is held upright, face on.
        /// </summary>
        public Quaternion DefaultRotation(StockKind kind) => Frame;

        public void PickUp(Piece piece, bool keepRotation)
        {
            if (held != null) Drop();
            if (cam == null) cam = Camera.main;
            site.Isolate(piece);
            site.SetHeld(piece, true);
            held = piece;
            dragging = false;
            dragAxis = 0;
            dragPixels = Vector2.zero;
            WorldRotation = keepRotation ? piece.transform.rotation : DefaultRotation(piece.Kind);
            distance = Mathf.Clamp(Vector3.Distance(cam.transform.position, piece.transform.position), minDistance, maxDistance);
            targetPosition = piece.transform.position;
            targetRotation = WorldRotation;
            placement = Placement.Air;
        }

        /// <summary>
        /// Takes a new piece of stock into the hands at a spot in front of the player that's clear of the rack,
        /// the ground and everything else, so it never starts wedged in the pile.
        /// </summary>
        public Piece TakeNew(StockKind kind, float length)
        {
            if (cam == null) cam = Camera.main;
            var rot = DefaultRotation(kind);
            var piece = site.TakeStock(kind, length, cam.transform.position + Frame * Vector3.forward * 2f, rot);
            Vector3 flat = Frame * Vector3.forward;
            Vector3 spot = piece.transform.position;
            bool found = false;
            foreach (float lift in new[] { 0f, 0.25f, 0.5f, 0.8f, 1.2f, 1.7f })
            {
                foreach (float d in new[] { 2.2f, 1.7f, 2.8f, 1.3f, 3.4f })
                {
                    Vector3 p = cam.transform.position + flat * d + Vector3.up * (lift - 0.35f);
                    if (Clear(piece, p, rot)) { spot = p; found = true; break; }
                }
                if (found) break;
            }
            piece.Structure.transform.SetPositionAndRotation(spot, rot);
            Physics.SyncTransforms();
            PickUp(piece, keepRotation: true);
            distance = Mathf.Clamp(Vector3.Distance(cam.transform.position, spot), minDistance, maxDistance);
            return piece;
        }

        /// <summary>True when the piece at this pose overlaps nothing solid (player excluded).</summary>
        static bool Clear(Piece piece, Vector3 position, Quaternion rotation)
        {
            var b = piece.Solid.Bounds;
            Vector3 center = position + rotation * b.center;
            int mask = Physics.DefaultRaycastLayers & ~BuildLayers.PlayerMask & ~BuildLayers.ClampMask & ~BuildLayers.HeldMask & ~BuildLayers.NailMask;
            foreach (var col in Physics.OverlapBox(center, b.extents + Vector3.one * 0.01f, rotation, mask, QueryTriggerInteraction.Ignore))
            {
                if (col == piece.Collider) continue;
                if (Physics.ComputePenetration(piece.Collider, position, rotation, col, col.transform.position, col.transform.rotation, out _, out float depth) && depth > 0.003f)
                    return false;
            }
            return true;
        }

        /// <summary>E: lets go. The piece falls from wherever it is, keeping any motion it had.</summary>
        public void Drop()
        {
            if (held == null) return;
            var p = held;
            held = null;
            ExitPilot();
            Rotating = false;
            LookLock.Held = false;
            p.Structure.SetCarryKinematic(false);
            LiftOutOfGround(p);
            site.SetHeld(p, false);
            p.ClearTint();
        }

        /// <summary>
        /// LMB: seat, set down or clamp depending on where the piece is. Clamping needs something to clamp to; when
        /// the piece isn't touching anything it stays in the hands and the reason comes back.
        /// </summary>
        public string Place()
        {
            if (held == null) return null;
            if (placement == Placement.Air)
            {
                Physics.SyncTransforms();
                if (ContactFinder.Touching(held).Count == 0) return "Press it against another piece to clamp it";
            }
            var p = held;
            held = null;
            ExitPilot();
            Rotating = false;
            LookLock.Held = false;
            var structure = p.Structure;
            if (placement != Placement.Air)
                structure.transform.SetPositionAndRotation(targetPosition, targetRotation);
            structure.SetCarryKinematic(true);
            site.SetHeld(p, false);
            switch (placement)
            {
                case Placement.PostInBlock:
                    site.Plant(p, targetBlock);
                    return "Post seated in the deck block";
                case Placement.Block:
                    site.Plant(p);
                    return "Deck block set, level";
                case Placement.Table:
                    site.SetOnTable(p, true);
                    return "Set on the cutting table";
                default:
                    string reason = site.TryClamp(p);
                    if (reason != null) return reason;
                    int n = site.ClampCount;
                    return $"Clamped ({n} clamp{(n == 1 ? "" : "s")}) — nail it, then E on a clamp to take them off";
            }
        }

        public void PutOnTable(CuttingTable t)
        {
            if (held == null) return;
            if (!t.SnapPose(held, HoldPoint, out targetPosition, out targetRotation)) return;
            placement = Placement.Table;
            Place();
        }

        // ---------------------------------------------------------------- rotation

        /// <summary>Axes the rotation keys turn about, in the player's horizontal frame.</summary>
        public enum Axis { Turn, Tip, Roll }

        /// <summary>Turns the held piece about a frame axis: Turn = vertical, Tip = the player's right, Roll = the player's forward.</summary>
        public void RotateHeld(Axis axis, float degrees)
        {
            var frame = Frame;
            Vector3 a = axis switch
            {
                Axis.Turn => Vector3.up,
                Axis.Tip => frame * Vector3.right,
                _ => frame * Vector3.forward,
            };
            WorldRotation = Quaternion.AngleAxis(degrees, a) * WorldRotation;
        }

        void RotationInput()
        {
            float step = modifier.IsPressed() ? FineStep : 90f;
            if (turn.WasPressedThisFrame()) RotateHeld(Axis.Turn, step);
            if (tip.WasPressedThisFrame()) RotateHeld(Axis.Tip, step);
            if (roll.WasPressedThisFrame()) RotateHeld(Axis.Roll, step);
            if (squareUp.WasPressedThisFrame()) WorldRotation = SquareUp(WorldRotation);

            Rotating = altUse.IsPressed();
            if (!Rotating)
            {
                dragging = false;
                dragAxis = 0;
                dragPixels = Vector2.zero;
                return;
            }
            if (!dragging)
            {
                dragging = true;
                dragStart = WorldRotation;
            }
            dragPixels += Mouse.current != null ? Mouse.current.delta.ReadValue() : Vector2.zero;
            if (dragAxis == 0)
            {
                if (dragPixels.magnitude < DragLockPixels) return;
                dragAxis = Mathf.Abs(dragPixels.x) >= Mathf.Abs(dragPixels.y) ? 1 : 2;
            }
            float angle = (dragAxis == 1 ? dragPixels.x : -dragPixels.y) * dragDegreesPerPixel;
            if (modifier.IsPressed()) angle = Mathf.Round(angle / FineStep) * FineStep;
            Vector3 axis = dragAxis == 1 ? Vector3.up : Frame * Vector3.right;
            WorldRotation = Quaternion.AngleAxis(angle, axis) * dragStart;
        }

        // ---------------------------------------------------------------- pilot mode

        /// <summary>G: leave the body and steer the held piece from an orbiting camera (or come back).</summary>
        public void TogglePilot()
        {
            if (held == null) return;
            if (piloting) { ExitPilot(); return; }
            if (cam == null) cam = Camera.main;
            pilotRotation = WorldRotation;
            piloting = true;
            pilotTarget = held.transform.position;
            Vector3 back = cam.transform.position - pilotTarget;
            orbitDistance = Mathf.Clamp(back.magnitude, 1.5f, 8f);
            Vector3 flat = Vector3.ProjectOnPlane(back, Vector3.up);
            orbitYaw = flat.sqrMagnitude > 1e-4f ? Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg : cam.transform.eulerAngles.y + 180f;
            orbitPitch = Mathf.Clamp(Mathf.Asin(Mathf.Clamp(back.normalized.y, -1f, 1f)) * Mathf.Rad2Deg, -80f, 80f);
            savedCamLocalPos = cam.transform.localPosition;
            savedCamLocalRot = cam.transform.localRotation;
            if (body != null) body.ExternalControl = true;
        }

        void ExitPilot()
        {
            if (!piloting) return;
            var world = pilotRotation;
            piloting = false;
            if (cam != null)
            {
                cam.transform.localPosition = savedCamLocalPos;
                cam.transform.localRotation = savedCamLocalRot;
            }
            if (body != null) body.ExternalControl = false;
            if (held != null)
            {
                WorldRotation = world;
                distance = Mathf.Clamp(Vector3.Distance(cam.transform.position, held.transform.position), minDistance, maxDistance);
            }
        }

        void PilotInput()
        {
            Vector2 mouse = Mouse.current != null ? Mouse.current.delta.ReadValue() : Vector2.zero;
            if (!Rotating)
            {
                orbitYaw += mouse.x * orbitDegreesPerPixel;
                orbitPitch = Mathf.Clamp(orbitPitch - mouse.y * orbitDegreesPerPixel, -80f, 80f);
            }
            int scroll = ScrollSteps();
            if (scroll != 0) orbitDistance = Mathf.Clamp(orbitDistance - scroll * 0.4f, 1.2f, 10f);

            // WASD in the orbit camera's horizontal frame; Space up, Ctrl down; Shift for fine moves.
            Vector2 wasd = move.ReadValue<Vector2>();
            Vector3 dir = Frame * new Vector3(wasd.x, 0f, wasd.y);
            if (jump.IsPressed()) dir += Vector3.up;
            if (crouch.IsPressed()) dir += Vector3.down;
            float speed = modifier.IsPressed() ? pilotSlowSpeed : pilotSpeed;
            pilotTarget += Vector3.ClampMagnitude(dir, 1f) * speed * Time.deltaTime;
        }

        void PlaceOrbitCamera()
        {
            if (held == null || cam == null) return;
            Vector3 center = held.transform.TransformPoint(held.Solid.Bounds.center);
            Quaternion orbit = Quaternion.Euler(orbitPitch, orbitYaw + 180f, 0f);
            Vector3 pos = center + orbit * new Vector3(0f, 0f, -orbitDistance);
            // Keep the camera out of the ground.
            if (Physics.Raycast(pos + Vector3.up * 20f, Vector3.down, out var hit, 40f, BuildLayers.GroundMask, QueryTriggerInteraction.Ignore))
                pos.y = Mathf.Max(pos.y, hit.point.y + 0.3f);
            cam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(center - pos, Vector3.up));
        }

        // ---------------------------------------------------------------- per frame

        /// <summary>Reads input and decides this frame's target pose. Called by the toolbelt with the aim.</summary>
        public void Tick(in AimInfo aim)
        {
            if (held == null) return;
            if (cam == null) cam = Camera.main;

            RotationInput();
            LookLock.Held = Rotating || piloting;

            if (piloting) PilotInput();
            else
            {
                int scroll = ScrollSteps();
                if (scroll != 0)
                {
                    float step = modifier.IsPressed() ? 0.05f : 0.25f;
                    distance = Mathf.Clamp(distance + scroll * step, minDistance, maxDistance);
                }
            }

            var rotation = WorldRotation;
            placement = Placement.Air;
            targetBlock = null;
            guides.Clear();
            targetRotation = rotation;
            targetPosition = HoldPoint;
            bool inReach = aim.Hit && aim.Distance <= maxDistance + 1f;

            // The hands stop short of the ground, racks and benches; only wood can be pressed against.
            if (!piloting && aim.Hit && aim.Piece == null && aim.Distance < distance)
            {
                // Back off along the view until the piece, at its intended rotation, clears the surface it's aimed at.
                Vector3 n = aim.Normal;
                Vector3 h = held.Solid.Bounds.extents;
                float support = Mathf.Abs(Vector3.Dot(rotation * Vector3.right, n)) * h.x
                                + Mathf.Abs(Vector3.Dot(rotation * Vector3.up, n)) * h.y
                                + Mathf.Abs(Vector3.Dot(rotation * Vector3.forward, n)) * h.z + 0.01f;
                float along = Mathf.Max(0.2f, Mathf.Abs(Vector3.Dot(cam.transform.forward, n)));
                targetPosition = cam.transform.position + cam.transform.forward * Mathf.Max(minDistance, aim.Distance - support / along);
            }

            if (aim.Table != null && aim.Table.SnapPose(held, aim.Point, out var tPos, out var tRot))
            {
                placement = Placement.Table;
                targetPosition = tPos;
                targetRotation = tRot;
            }
            else if (held.Kind == StockKind.DeckBlock && aim.OnGround && inReach && BlockPose(aim.Point, rotation, out var bPos, out var bRot))
            {
                placement = Placement.Block;
                if (!modifier.IsPressed()) SnapBlockInLine(ref bPos, ref bRot);
                targetPosition = bPos;
                targetRotation = bRot;
                BuildGuides(bPos);
            }
            else if (Stock.CanBePost(held.Kind) && inReach && aim.Piece != null && aim.Piece.Kind == StockKind.DeckBlock
                     && aim.Piece.Planted && site.PostIn(aim.Piece) == null)
            {
                placement = Placement.PostInBlock;
                targetBlock = aim.Piece;
                targetRotation = SquareTo(Plumb(rotation), aim.Piece.transform.rotation);
                Vector3 grainWorld = targetRotation * held.GrainAxisLocal;
                var ext = held.Solid.Extent(held.GrainAxisLocal);
                float toBottom = grainWorld.y > 0f ? ext.x : -ext.y; // signed offset along the grain from origin to the bottom end
                Vector3 centerOffset = targetRotation * held.Solid.Bounds.center;
                centerOffset.y = 0f;
                Vector3 bottom = aim.Piece.SocketTop - Vector3.up * Stock.DeckBlockSocketDepth;
                targetPosition = bottom - Vector3.up * toBottom - centerOffset;
            }

            touching = placement == Placement.Air ? ContactFinder.Touching(held).Count : 0;
            held.SetTint(placement != Placement.Air || touching > 0 ? BuildSite.PreviewTint : Color.white);

            // Snapped previews are posed directly; free carrying is physical.
            var structure = held.Structure;
            if (placement != Placement.Air)
            {
                structure.SetCarryKinematic(true);
                structure.transform.SetPositionAndRotation(targetPosition, targetRotation);
            }
            else structure.SetCarryKinematic(false);
        }

        /// <summary>F: turn the held piece to match the piece under the crosshair (Shift: across it, on edge).</summary>
        public bool AlignToAim(in AimInfo aim)
        {
            if (held == null || aim.Piece == null) return false;
            WorldRotation = AlignTo(aim.Piece.transform.rotation, modifier.IsPressed());
            return true;
        }

        void LateUpdate()
        {
            if (piloting) PlaceOrbitCamera();
        }

        void FixedUpdate()
        {
            if (held == null || placement != Placement.Air) return;
            var rb = held.Structure.Body;
            if (rb.isKinematic) return;

            Vector3 toTarget = targetPosition - rb.position;
            rb.linearVelocity = Vector3.ClampMagnitude(toTarget * moveGain, maxSpeed);

            Quaternion delta = targetRotation * Quaternion.Inverse(rb.rotation);
            delta.ToAngleAxis(out float angle, out Vector3 axis);
            if (angle > 180f) angle -= 360f;
            if (float.IsNaN(axis.x) || Mathf.Abs(angle) < 1e-3f) rb.angularVelocity = Vector3.zero;
            else rb.angularVelocity = Vector3.ClampMagnitude(axis.normalized * (angle * Mathf.Deg2Rad * turnGain), maxTurnSpeed);
        }

        // ---------------------------------------------------------------- helpers

        /// <summary>
        /// A deck block at the aimed point: level, keeping its yaw, its base below the lowest ground under its
        /// footprint by <see cref="Stock.DeckBlockSink"/> so no corner is left in the air.
        /// </summary>
        static bool BlockPose(Vector3 aimPoint, Quaternion rotation, out Vector3 position, out Quaternion rot)
        {
            float yaw = rotation.eulerAngles.y;
            rot = Quaternion.Euler(0f, yaw, 0f);
            float half = Stock.DeckBlockBase * 0.5f;
            float lowest = aimPoint.y;
            bool any = false;
            foreach (var corner in new[] { new Vector3(-half, 0f, -half), new Vector3(half, 0f, -half), new Vector3(half, 0f, half), new Vector3(-half, 0f, half), Vector3.zero })
            {
                Vector3 at = aimPoint + rot * corner;
                if (!Physics.Raycast(at + Vector3.up * 5f, Vector3.down, out var hit, 10f, BuildLayers.GroundMask, QueryTriggerInteraction.Ignore)) continue;
                lowest = any ? Mathf.Min(lowest, hit.point.y) : hit.point.y;
                any = true;
            }
            position = Vector3.zero;
            if (!any) return false;
            float baseY = lowest - Stock.DeckBlockSink;
            position = new Vector3(aimPoint.x, baseY + Stock.DeckBlockHeight * 0.5f, aimPoint.z);
            return true;
        }

        /// <summary>Set deck blocks within range, nearest first.</summary>
        System.Collections.Generic.List<Piece> NearbyBlocks(Vector3 at)
        {
            var list = new System.Collections.Generic.List<Piece>();
            foreach (var p in site.Pieces.Values)
                if (p.Kind == StockKind.DeckBlock && p.Planted && p != held && Flat(p.transform.position - at).magnitude < GuideRange)
                    list.Add(p);
            list.Sort((a, b) => Flat(a.transform.position - at).sqrMagnitude.CompareTo(Flat(b.transform.position - at).sqrMagnitude));
            if (list.Count > MaxGuides) list.RemoveRange(MaxGuides, list.Count - MaxGuides);
            return list;
        }

        /// <summary>
        /// Degrees between a horizontal offset and the nearest of a block's four square directions, and that direction.
        /// </summary>
        static float OffSquare(Piece block, Vector3 offset, out Vector3 direction)
        {
            direction = block.transform.right;
            float best = float.MaxValue;
            foreach (var d in new[] { block.transform.right, -block.transform.right, block.transform.forward, -block.transform.forward })
            {
                Vector3 fd = Flat(d).normalized;
                float a = Vector3.Angle(Flat(offset), fd);
                if (a < best) { best = a; direction = fd; }
            }
            return best;
        }

        /// <summary>Within a few degrees of square with the nearest set block, slides onto its line and takes its rotation.</summary>
        void SnapBlockInLine(ref Vector3 position, ref Quaternion rot)
        {
            foreach (var n in NearbyBlocks(position))
            {
                Vector3 offset = Flat(position - n.transform.position);
                if (offset.magnitude < Stock.DeckBlockBase) continue;
                if (OffSquare(n, offset, out var dir) > AlignSnapDegrees) continue;
                Vector3 snapped = n.transform.position + dir * Vector3.Dot(offset, dir);
                if (!BlockPose(new Vector3(snapped.x, position.y, snapped.z), n.transform.rotation, out var p, out var r)) continue;
                position = p;
                rot = r;
                return;
            }
        }

        void BuildGuides(Vector3 at)
        {
            float lift = Stock.DeckBlockHeight * 0.5f + 0.02f;
            foreach (var n in NearbyBlocks(at))
            {
                Vector3 offset = Flat(at - n.transform.position);
                if (offset.magnitude < Stock.DeckBlockBase) continue;
                guides.Add(new BlockGuide
                {
                    From = n.transform.position + Vector3.up * lift,
                    To = at + Vector3.up * lift,
                    Degrees = OffSquare(n, offset, out _),
                    NominalDistance = Stock.Nominal(offset.magnitude),
                });
            }
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        /// <summary>
        /// Turns an upright post about the vertical so its square sides line up with the block's: whichever of the
        /// four square orientations is nearest to how it's held.
        /// </summary>
        Quaternion SquareTo(Quaternion upright, Quaternion block)
        {
            Vector3 side = Flat(upright * Vector3.up);
            if (side.sqrMagnitude < 1e-6f) side = Flat(upright * Vector3.forward);
            Vector3 blockRight = Flat(block * Vector3.right).normalized;
            float a = Vector3.SignedAngle(blockRight, side.normalized, Vector3.up);
            float snapped = Mathf.Round(a / 90f) * 90f;
            return Quaternion.AngleAxis(snapped - a, Vector3.up) * upright;
        }

        /// <summary>A piece that ended up below the surface (looking down at your feet) is raised to sit on it.</summary>
        void LiftOutOfGround(Piece p)
        {
            float lowest = p.LowestWorldY();
            Vector3 c = p.transform.TransformPoint(p.Solid.Bounds.center);
            if (!Physics.Raycast(c + Vector3.up * 20f, Vector3.down, out var hit, 40f, BuildLayers.GroundMask, QueryTriggerInteraction.Ignore)) return;
            if (lowest >= hit.point.y - 0.01f) return;
            p.Structure.transform.position += Vector3.up * (hit.point.y - lowest + 0.02f);
        }

        int ScrollSteps()
        {
            float v = adjust.ReadValue<float>();
            return v > 0.01f ? 1 : v < -0.01f ? -1 : 0;
        }

        /// <summary>The smallest turn that stands the grain straight up (whichever end is nearer the sky goes up).</summary>
        Quaternion Plumb(Quaternion rot)
        {
            Vector3 grain = rot * held.GrainAxisLocal;
            Vector3 target = Vector3.Dot(grain, Vector3.up) >= 0f ? Vector3.up : Vector3.down;
            return Quaternion.FromToRotation(grain, target) * rot;
        }

        /// <summary>
        /// Matches another piece's orientation. Parallel: grain to grain, face to face. Across: our grain runs
        /// along their width, so a board lands on edge across a post's face.
        /// </summary>
        public static Quaternion AlignTo(Quaternion other, bool across) =>
            across ? other * Quaternion.Euler(0f, 0f, 90f) : other;

        /// <summary>Snaps each of the piece's axes to the nearest world axis.</summary>
        public static Quaternion SquareUp(Quaternion rot)
        {
            Vector3 f = Nearest(rot * Vector3.forward);
            Vector3 u = Nearest(rot * Vector3.up);
            if (Mathf.Abs(Vector3.Dot(f, u)) > 0.5f) u = Nearest(Vector3.ProjectOnPlane(rot * Vector3.up, f));
            return Quaternion.LookRotation(f, u);
        }

        static Vector3 Nearest(Vector3 v)
        {
            Vector3 a = new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
            if (a.x >= a.y && a.x >= a.z) return new Vector3(Mathf.Sign(v.x), 0f, 0f);
            if (a.y >= a.z) return new Vector3(0f, Mathf.Sign(v.y), 0f);
            return new Vector3(0f, 0f, Mathf.Sign(v.z));
        }

        /// <summary>How far off level/plumb the held piece is right now, from its actual (physics) pose.</summary>
        public string LevelReadout()
        {
            if (held == null || held.Kind == StockKind.DeckBlock) return "";
            Vector3 grain = held.GrainAxisWorld;
            Vector3 wide = held.transform.TransformDirection(Vector3.up);
            float grainTilt = Mathf.Asin(Mathf.Clamp(Mathf.Abs(grain.y), 0f, 1f)) * Mathf.Rad2Deg;
            bool plumb = grainTilt > 45f;
            float offAxis = plumb ? 90f - grainTilt : grainTilt;
            float r = Mathf.Asin(Mathf.Clamp(Mathf.Abs(wide.y), 0f, 1f)) * Mathf.Rad2Deg;
            r = Mathf.Min(r, 90f - r);
            string first = plumb ? (offAxis < 0.5f ? "plumb ✓" : $"off plumb {offAxis:F1}°") : (offAxis < 0.5f ? "level ✓" : $"tilt {offAxis:F1}°");
            string second = r < 0.5f ? "square ✓" : $"roll {r:F1}°";
            return $"{first} · {second}";
        }

        /// <summary>The prompt while holding a piece: what LMB does here, and the level readout.</summary>
        public string Prompt()
        {
            if (held == null) return "";
            string action = placement switch
            {
                Placement.PostInBlock => "[LMB] Seat the post",
                Placement.Block => guides.Count > 0 && guides[0].Aligned ? "[LMB] Set the block · in line ✓" : "[LMB] Set the block",
                Placement.Table => "[LMB] Set it on the table",
                _ => touching > 0 ? $"[LMB] Clamp ({touching})  [E] Drop" : "[E] Drop · press it against wood to clamp",
            };
            if (piloting) action += "  [G] Back";
            string readout = LevelReadout();
            return string.IsNullOrEmpty(readout) ? action : $"{action}\n{readout}";
        }
    }
}
