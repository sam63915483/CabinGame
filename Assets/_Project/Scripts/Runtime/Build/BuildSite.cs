using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Cabin.Build
{
    /// <summary>
    /// Everything built so far: pieces, nails and the structures they form. Seats posts, clamps one piece at a
    /// time to whatever it touches, drives and pulls nails (drawing pieces flush), saws pieces in two, and
    /// saves/loads the lot. No player logic lives here.
    /// </summary>
    public sealed class BuildSite : MonoBehaviour
    {
        [SerializeField] Material lumberMaterial;
        [SerializeField] Material plywoodMaterial;
        [SerializeField] Material concreteMaterial;
        [SerializeField] GameObject nailHeadPrefab;
        [SerializeField] GameObject nailShotPrefab;
        [SerializeField] Material clampMaterial;
        [SerializeField] Material steelMaterial;
        [SerializeField] Material paintMaterial;
        [SerializeField] float sawKerf = 0.125f * Units.Inch;

        public static readonly Color TableTint = new Color(1f, 0.95f, 0.75f);
        public static readonly Color PreviewTint = new Color(0.65f, 1f, 0.70f);

        readonly Dictionary<int, Piece> pieces = new Dictionary<int, Piece>();
        readonly Dictionary<int, NailRecord> nails = new Dictionary<int, NailRecord>();
        readonly Dictionary<int, NailHead> heads = new Dictionary<int, NailHead>();
        readonly List<ClampVisual> clampVisuals = new List<ClampVisual>();
        readonly List<Contact> clampContacts = new List<Contact>();
        int nextPieceId = 1;
        int nextNailId = 1;

        public IReadOnlyDictionary<int, Piece> Pieces => pieces;
        /// <summary>Template for spray paint overlays (URP Lit, transparent).</summary>
        public Material PaintMaterial => paintMaterial;
        public IReadOnlyDictionary<int, NailRecord> Nails => nails;
        /// <summary>The one piece held by clamps right now, if any.</summary>
        public Piece ClampedPiece { get; private set; }
        public int ClampCount => clampVisuals.Count;
        /// <summary>What the clamped piece is clamped against.</summary>
        public IReadOnlyList<Contact> ClampContacts => clampContacts;
        public int StructureCount => GetComponentsInChildren<Structure>().Count(s => s.Pieces.Count > 0);

        public static string QuickSavePath => Path.Combine(Application.persistentDataPath, "freebuild_quicksave.json");

        // ---------------------------------------------------------------- pieces

        public Piece TakeStock(StockKind kind, float length, Vector3 position, Quaternion rotation) =>
            CreatePiece(kind, Stock.Shape(kind, length), position, rotation);

        Piece CreatePiece(StockKind kind, ConvexSolid solid, Vector3 position, Quaternion rotation, int id = 0)
        {
            if (id == 0) id = nextPieceId++;
            else nextPieceId = Mathf.Max(nextPieceId, id + 1);
            var structure = NewStructure(position, rotation);
            var go = new GameObject("Piece");
            go.transform.SetParent(structure.transform, false);
            var piece = go.AddComponent<Piece>();
            piece.Init(id, kind, solid, MaterialFor(kind));
            pieces[id] = piece;
            structure.Pieces.Add(piece);
            structure.Refresh();
            return piece;
        }

        Material MaterialFor(StockKind kind) => kind switch
        {
            StockKind.Plywood => plywoodMaterial,
            StockKind.DeckBlock => concreteMaterial,
            _ => lumberMaterial,
        };

        /// <summary>The post standing in this deck block, if any.</summary>
        public Piece PostIn(Piece block) => pieces.Values.FirstOrDefault(p => p.SeatedIn == block.Id);

        Structure NewStructure(Vector3 position, Quaternion rotation)
        {
            var go = new GameObject("Structure");
            go.transform.SetParent(transform, false);
            go.transform.SetPositionAndRotation(position, rotation);
            return go.AddComponent<Structure>();
        }

        public void DestroyPiece(Piece piece)
        {
            foreach (var n in NailsOf(piece.Id).ToList()) RemoveNail(n.id);
            bool involved = ClampedPiece == piece || clampContacts.Any(c => c.Other == piece);
            pieces.Remove(piece.Id);
            if (involved) ReleaseClamps(animate: true);
            var structure = piece.Structure;
            Destroy(piece.gameObject);
            if (structure != null) structure.Pieces.Remove(piece);
            RebuildStructures();
        }

        public IEnumerable<NailRecord> NailsOf(int pieceId) => nails.Values.Where(n => n.a == pieceId || n.b == pieceId);

        public int NailCount(int pieceId) => nails.Values.Count(n => n.a == pieceId || n.b == pieceId);

        /// <summary>Gives the piece a structure of its own, with the piece at that structure's origin. Only for unnailed pieces.</summary>
        public void Isolate(Piece piece)
        {
            var old = piece.Structure;
            if (old != null && old.Pieces.Count == 1 && piece.transform.localPosition == Vector3.zero && piece.transform.localRotation == Quaternion.identity) return;
            var fresh = NewStructure(piece.transform.position, piece.transform.rotation);
            if (old != null) old.Pieces.Remove(piece);
            piece.transform.SetParent(fresh.transform, true);
            piece.transform.localPosition = Vector3.zero;
            piece.transform.localRotation = Quaternion.identity;
            fresh.Pieces.Add(piece);
            fresh.Refresh();
            if (old == null) return;
            if (old.Pieces.Count == 0) Destroy(old.gameObject);
            else old.Refresh();
        }

        // ---------------------------------------------------------------- holding in place

        void Awake() => BuildLayers.ApplyCollisionMatrix();

        /// <summary>Uploads paint sprayed this frame (once per painted piece, however many droplets landed).</summary>
        void LateUpdate()
        {
            foreach (var p in pieces.Values)
                if (p != null && p.Paint != null && p.Paint.Dirty) p.Paint.Flush();
        }

        public void SetHeld(Piece piece, bool held)
        {
            piece.Held = held;
            piece.gameObject.layer = held ? BuildLayers.HeldPiece : BuildLayers.Piece;
            if (held)
            {
                if (ClampedPiece == piece || clampContacts.Any(c => c.Other == piece)) ReleaseClamps(animate: true);
                piece.OnTable = false;
                piece.Planted = false;
                piece.SeatedIn = 0;
            }
            Retint(piece);
            piece.Structure?.Refresh();
        }

        // ---------------------------------------------------------------- clamps

        /// <summary>
        /// Clamps a piece to everything it's touching, where it is now. Only one piece is clamped at a time: the
        /// previous one is released (and falls unless it's been nailed). Returns null on success, else why not.
        /// </summary>
        public string TryClamp(Piece piece)
        {
            var contacts = ContactFinder.Touching(piece).Where(c => c.Other != null && c.Other != piece).ToList();
            if (contacts.Count == 0) return "Press it against another piece to clamp it";
            ReleaseClamps(animate: true);
            ClampedPiece = piece;
            piece.Clamped = true;
            clampContacts.AddRange(contacts);
            foreach (var grip in ClampLayout.Plan(piece, contacts))
                clampVisuals.Add(ClampVisual.Create(transform, grip, clampMaterial, steelMaterial));
            Retint(piece);
            piece.Structure?.Refresh();
            return null;
        }

        /// <summary>Takes the clamps off. The piece stays only if it's nailed to something that holds it.</summary>
        public void Unclamp() => ReleaseClamps(animate: true);

        void ReleaseClamps(bool animate)
        {
            foreach (var v in clampVisuals)
                if (v != null)
                {
                    if (animate) v.Release();
                    else v.Vanish();
                }
            clampVisuals.Clear();
            clampContacts.Clear();
            var p = ClampedPiece;
            ClampedPiece = null;
            if (p == null) return;
            p.Clamped = false;
            Retint(p);
            p.Structure?.Refresh();
        }

        /// <summary>Re-fits the clamps after the clamped piece (or what it's clamped to) moved or was cut.</summary>
        void RefitClamps()
        {
            var p = ClampedPiece;
            if (p == null) return;
            foreach (var v in clampVisuals) if (v != null) v.Vanish();
            clampVisuals.Clear();
            clampContacts.Clear();
            Physics.SyncTransforms();
            var contacts = ContactFinder.Touching(p);
            if (contacts.Count == 0)
            {
                ClampedPiece = null;
                p.Clamped = false;
                p.Structure?.Refresh();
                return;
            }
            clampContacts.AddRange(contacts);
            foreach (var grip in ClampLayout.Plan(p, contacts))
                clampVisuals.Add(ClampVisual.Create(transform, grip, clampMaterial, steelMaterial));
        }

        public void Plant(Piece piece, Piece inBlock = null)
        {
            piece.Planted = true;
            piece.SeatedIn = inBlock != null ? inBlock.Id : 0;
            Retint(piece);
            piece.Structure?.Refresh();
        }

        public void SetOnTable(Piece piece, bool onTable)
        {
            piece.OnTable = onTable;
            Retint(piece);
            piece.Structure?.Refresh();
        }

        public void Retint(Piece piece)
        {
            if (piece.OnTable) piece.SetTint(TableTint);
            else piece.ClearTint();
        }

        // ---------------------------------------------------------------- nails

        /// <summary>Works out the nail a shot along this ray would drive, without driving it.</summary>
        public NailPlan PlanNail(Ray ray, float reach)
        {
            var plan = new NailPlan { Ray = ray };
            if (!RaycastPiece(ray, reach, out var first, out var entry))
            {
                plan.Reason = "Aim at a piece of wood";
                return plan;
            }
            plan.First = first.Piece;
            plan.Entry = entry.point;
            plan.Direction = ray.direction;
            if (first.Piece.IsConcrete)
            {
                plan.Reason = "Can't nail concrete";
                return plan;
            }

            // The nail's far end, cast back towards the entry, finds where it leaves the first piece.
            Vector3 tip = entry.point + ray.direction * NailRules.NailLength;
            float exitFirst = first.Piece.Collider.Raycast(new Ray(tip, -ray.direction), out var exitHit, NailRules.NailLength)
                ? NailRules.NailLength - exitHit.distance
                : float.PositiveInfinity;

            // Look for the second piece as far as a gap could be pulled closed.
            const float skin = 0.0005f;
            float lookAhead = NailRules.NailLength + NailRules.PullGap;
            var behind = Physics.RaycastAll(new Ray(entry.point + ray.direction * skin, ray.direction), lookAhead, BuildLayers.PieceMask, QueryTriggerInteraction.Ignore)
                .Where(h => h.collider != first.Piece.Collider && h.collider.TryGetComponent<Piece>(out _))
                .OrderBy(h => h.distance)
                .ToArray();
            if (behind.Length == 0)
            {
                plan.Reason = float.IsInfinity(exitFirst) ? "Nail doesn't go through — too thick here" : "Nothing behind it to nail into";
                return plan;
            }
            plan.Second = behind[0].collider.GetComponent<Piece>();
            plan.EntrySecond = behind[0].point;
            if (plan.Second.IsConcrete)
            {
                plan.Reason = "Can't nail into concrete";
                return plan;
            }
            plan.Reason = NailRules.Check(exitFirst, behind[0].distance + skin, out plan.Bite, out plan.Pull);
            if (plan.Reason != null || plan.Pull <= 0f) return plan;

            // Something has to move to close the gap: the piece being nailed if it's free, else the one behind.
            var firstS = plan.First.Structure;
            var secondS = plan.Second.Structure;
            if (firstS != null && !firstS.Anchored && firstS != secondS)
            {
                plan.Mover = firstS;
                plan.MoveDirection = ray.direction;
            }
            else if (secondS != null && !secondS.Anchored && firstS != secondS)
            {
                plan.Mover = secondS;
                plan.MoveDirection = -ray.direction;
            }
            else
            {
                plan.Reason = $"Gap of {Units.FeetInches(Stock.Nominal(plan.Pull))} and both pieces are fixed";
                return plan;
            }
            var otherS = plan.Mover == firstS ? secondS : firstS;
            var mover = plan.Mover;
            Vector3 shift = plan.MoveDirection * plan.Pull;
            plan.Reason = MoveBlocker(mover, t => t.position += shift, otherS, null);
            if (plan.Reason != null && ClampedPiece != null && ClampedPiece.Structure == plan.Mover)
            {
                // Clamped at one end: swing the piece round the clamp instead, like bending a board in.
                Vector3 movePoint = plan.Mover == firstS ? plan.Entry : plan.EntrySecond;
                string pivotReason = PlanPivot(plan.Mover, movePoint, plan.MoveDirection, plan.Pull, otherS, out plan.PivotPoint, out plan.PivotAxis, out plan.PivotAngle);
                if (pivotReason == null)
                {
                    plan.Pivot = true;
                    plan.Reason = null;
                }
            }
            return plan;
        }

        /// <summary>
        /// A rotation about the clamp contact farthest from <paramref name="movePoint"/> that carries that point
        /// <paramref name="distance"/> along <paramref name="direction"/>. Checked against everything but the two
        /// pieces being joined and what the clamp holds it to.
        /// </summary>
        string PlanPivot(Structure mover, Vector3 movePoint, Vector3 direction, float distance, Structure other,
            out Vector3 pivot, out Vector3 axis, out float angle)
        {
            pivot = Vector3.zero;
            axis = Vector3.up;
            angle = 0f;
            Vector3 lever = Vector3.zero;
            foreach (var c in clampContacts)
            {
                Vector3 l = Vector3.ProjectOnPlane(movePoint - c.Point, direction);
                if (l.sqrMagnitude > lever.sqrMagnitude) { lever = l; pivot = c.Point; }
            }
            if (lever.magnitude < 0.2f) return "Too close to the clamp to bend it in";
            axis = Vector3.Cross(lever, direction).normalized;
            float a = Mathf.Atan2(distance, lever.magnitude) * Mathf.Rad2Deg;
            // Unity rotations are left-handed: pick the sign that actually moves the point along the direction.
            Vector3 moved = pivot + Quaternion.AngleAxis(a, axis) * (movePoint - pivot);
            angle = Vector3.Dot(moved - movePoint, direction) > 0f ? a : -a;

            // Swing until it touches: the far side of the other piece closes before the nail line does, so back off
            // to the largest swing that's clear. What's left at the nail is within the gap a nail takes as-is.
            Vector3 p = pivot, ax = axis;
            var soft = new HashSet<Piece>(clampContacts.Select(c => c.Other));
            string Try(float ang) => MoveBlocker(mover, t => t.RotateAround(p, ax, ang), other, soft);
            if (Try(angle) == null) return null;
            float lo = 0f, hi = 1f;
            for (int i = 0; i < 10; i++)
            {
                float mid = (lo + hi) * 0.5f;
                if (Try(angle * mid) == null) lo = mid;
                else hi = mid;
            }
            if (lo * distance < distance - NailRules.MaxGap) return "Something is in the way of bending it in";
            angle *= lo;
            return null;
        }

        /// <summary>
        /// Tries a move on the mover, measures how far it would end up inside other wood, and puts it back.
        /// Null when the move is clear. The piece being nailed to may be touched (a hair of overlap is fine);
        /// <paramref name="soft"/> pieces (what a clamp holds it against while it swings) get a little more.
        /// </summary>
        string MoveBlocker(Structure mover, System.Action<Transform> move, Structure other, HashSet<Piece> soft)
        {
            var t = mover.transform;
            Vector3 pos0 = t.position;
            Quaternion rot0 = t.rotation;
            move(t);
            Physics.SyncTransforms();
            string reason = null;
            foreach (var mp in mover.Pieces)
            {
                var b = mp.Collider.bounds;
                foreach (var col in Physics.OverlapBox(b.center, b.extents, Quaternion.identity, BuildLayers.PieceMask, QueryTriggerInteraction.Ignore))
                {
                    if (!col.TryGetComponent<Piece>(out var hit) || hit.Structure == mover) continue;
                    if (!Physics.ComputePenetration(mp.Collider, mp.transform.position, mp.transform.rotation, col, col.transform.position, col.transform.rotation, out _, out float depth)) continue;
                    bool isOther = other != null && hit.Structure == other;
                    float allowed = soft != null && soft.Contains(hit) ? 0.012f : 0.003f;
                    if (depth <= allowed) continue;
                    reason = isOther ? "They'd jam before they're flush — line them up first" : "Something is in the way of pulling them together";
                    break;
                }
                if (reason != null) break;
            }
            t.SetPositionAndRotation(pos0, rot0);
            Physics.SyncTransforms();
            return reason;
        }

        /// <summary>Drives the planned nail, drawing the pieces together first if the plan says so.</summary>
        public NailRecord DriveNail(in NailPlan plan, out string reason)
        {
            reason = plan.Reason;
            if (!plan.Valid) return null;
            var p = plan;
            if (p.Pull > 0f)
            {
                var t = p.Mover.transform;
                Vector3 pos0 = t.position;
                Quaternion rot0 = t.rotation;
                if (p.Pivot) t.RotateAround(p.PivotPoint, p.PivotAxis, p.PivotAngle);
                else t.position += p.MoveDirection * p.Pull;
                Physics.SyncTransforms();
                p = PlanNail(plan.Ray, 10f);
                if (!p.Valid || p.Pull > 0f || p.First != plan.First || p.Second != plan.Second)
                {
                    t.SetPositionAndRotation(pos0, rot0);
                    Physics.SyncTransforms();
                    reason = p.Reason ?? "Couldn't pull them flush";
                    return null;
                }
                if (ClampedPiece != null && ClampedPiece.Structure == plan.Mover) RefitClamps();
            }
            var rec = new NailRecord
            {
                id = nextNailId++,
                a = p.First.Id,
                b = p.Second.Id,
                entryA = p.First.transform.InverseTransformPoint(p.Entry),
                directionA = p.First.transform.InverseTransformDirection(p.Direction),
                entryB = p.Second.transform.InverseTransformPoint(p.EntrySecond),
            };
            AddNail(rec);
            RebuildStructures();
            return rec;
        }

        public NailRecord DriveNail(in NailPlan plan) => DriveNail(plan, out _);

        /// <summary>
        /// The nail head nearest the aim line — within <paramref name="maxAngle"/> degrees or <paramref name="maxOffset"/>
        /// metres of it, whichever is more generous — no farther than <paramref name="maxDistance"/> (so not one hidden
        /// behind the wood you're looking at). Null if none. Heads are a few centimetres across, so this is how you
        /// target one without pixel-hunting.
        /// </summary>
        public NailHead NearestNail(Ray ray, float maxDistance, float maxAngle = 3f, float maxOffset = 0.05f)
        {
            NailHead best = null;
            float bestScore = float.MaxValue;
            float tan = Mathf.Tan(maxAngle * Mathf.Deg2Rad);
            foreach (var head in heads.Values)
            {
                if (head == null) continue;
                Vector3 to = head.transform.position - ray.origin;
                float along = Vector3.Dot(to, ray.direction);
                if (along <= 0f || along > maxDistance) continue;
                float off = (to - ray.direction * along).magnitude;
                if (off > Mathf.Max(maxOffset, along * tan)) continue;
                float score = off / along; // angle from the crosshair: the one that looks closest to it wins
                if (score < bestScore)
                {
                    bestScore = score;
                    best = head;
                }
            }
            return best;
        }

        /// <summary>
        /// Fires a nail from <paramref name="muzzle"/>. With <paramref name="driven"/> (a nail just driven) it flies to
        /// that nail's head, which appears as it lands; otherwise it flies along the aim and sticks in whatever it hits
        /// — wood, ground, a tree — or flies off out of sight.
        /// </summary>
        public void ShootNail(Vector3 muzzle, Ray aim, float range, NailRecord driven = null)
        {
            if (nailShotPrefab == null) return;
            var shot = Instantiate(nailShotPrefab).GetComponent<NailShot>();
            if (driven != null && heads.TryGetValue(driven.id, out var head))
            {
                Vector3 at = head.transform.position;
                shot.Fire(muzzle, at, at - muzzle, null, head.gameObject);
                return;
            }
            int mask = BuildLayers.AimMask & ~BuildLayers.NailMask;
            if (Physics.Raycast(aim, out var hit, range, mask, QueryTriggerInteraction.Ignore))
            {
                Vector3 dir = hit.point - muzzle;
                var piece = hit.collider.GetComponentInParent<Piece>();
                Transform into = piece != null ? piece.transform : hit.collider.transform;
                shot.Fire(muzzle, hit.point - dir.normalized * 0.0008f, dir, into, null);
            }
            else
            {
                Vector3 end = aim.origin + aim.direction * range;
                shot.Fire(muzzle, end, end - muzzle, null, null);
                Destroy(shot.gameObject, range / NailShot.Speed + 0.1f);
            }
        }

        void AddNail(NailRecord rec)
        {
            nails[rec.id] = rec;
            nextNailId = Mathf.Max(nextNailId, rec.id + 1);
            var a = pieces[rec.a];
            Vector3 entry = a.transform.TransformPoint(rec.entryA);
            Vector3 dir = a.transform.TransformDirection(rec.directionA);
            var go = Instantiate(nailHeadPrefab, entry - dir * 0.0008f, Quaternion.LookRotation(dir), a.transform);
            var head = go.GetComponent<NailHead>();
            head.Init(rec);
            heads[rec.id] = head;
        }

        public bool RemoveNail(int id)
        {
            if (!nails.Remove(id)) return false;
            if (heads.TryGetValue(id, out var head))
            {
                heads.Remove(id);
                if (head != null) Destroy(head.gameObject);
            }
            RebuildStructures();
            return true;
        }

        // ---------------------------------------------------------------- cutting

        /// <summary>Saws a piece along a world-space plane. Returns null on success, else why it couldn't.</summary>
        public string Cut(Piece piece, Plane worldPlane, out Piece offcut)
        {
            offcut = null;
            if (piece.IsConcrete) return "Can't saw concrete";
            Vector3 anyPoint = worldPlane.normal * -worldPlane.distance;
            var local = new Plane(piece.transform.InverseTransformDirection(worldPlane.normal), piece.transform.InverseTransformPoint(anyPoint));
            piece.Solid.Cut(local, sawKerf, out var positive, out var negative);
            if (positive == null || negative == null) return "The cut misses the piece";

            bool keepPositive = positive.Volume >= negative.Volume;
            var kept = keepPositive ? positive : negative;
            var off = keepPositive ? negative : positive;

            var structure = piece.Structure;
            Vector3 vel = structure != null && !structure.Body.isKinematic ? structure.Body.linearVelocity : Vector3.zero;
            Vector3 angVel = structure != null && !structure.Body.isKinematic ? structure.Body.angularVelocity : Vector3.zero;

            var paintAtlas = piece.Paint?.Atlas;
            var paintPixels = piece.Paint?.Pixels;
            piece.SetSolid(kept);
            offcut = CreatePiece(piece.Kind, off, piece.transform.position, piece.transform.rotation);
            offcut.OnTable = piece.OnTable;
            if (paintPixels != null) offcut.AdoptPaint(paintMaterial, paintAtlas, paintPixels, Vector3.zero);

            // Nails stay with whichever half they're in.
            foreach (var n in NailsOf(piece.Id).ToList())
            {
                bool movesToOffcut;
                if (n.a == piece.Id) movesToOffcut = local.GetSide(n.entryA) != keepPositive;
                else movesToOffcut = local.GetSide(n.entryB) != keepPositive;
                if (!movesToOffcut) continue;
                if (n.a == piece.Id)
                {
                    n.a = offcut.Id;
                    if (heads.TryGetValue(n.id, out var head)) head.transform.SetParent(offcut.transform, true);
                }
                else n.b = offcut.Id;
            }
            foreach (var stray in piece.GetComponentsInChildren<NailShot>())
                if (local.GetSide(piece.transform.InverseTransformPoint(stray.transform.position)) != keepPositive)
                    stray.transform.SetParent(offcut.transform, true);

            // Each half pivots about its own middle, so a short offcut handles like a short piece, not like the
            // whole board it came from.
            Recenter(piece);
            Recenter(offcut);

            // A cut post stays planted below the saw; what's above it is free.
            if (piece.Planted && offcut.LowestWorldY() < piece.LowestWorldY())
            {
                offcut.Planted = true;
                piece.Planted = false;
            }
            Retint(piece);
            Retint(offcut);
            RebuildStructures(vel, angVel);
            if (ClampedPiece == piece || clampContacts.Any(c => c.Other == piece)) RefitClamps();
            return null;
        }

        /// <summary>
        /// Moves a piece's origin to the middle of its shape without moving anything in the world: the solid is
        /// shifted one way and the transform the other; nail records and nail heads on it are corrected.
        /// </summary>
        void Recenter(Piece piece)
        {
            Vector3 c = piece.Solid.Bounds.center;
            if (c.sqrMagnitude < 1e-10f) return;
            var solid = piece.Solid.Clone();
            solid.Translate(-c);
            piece.transform.position += piece.transform.rotation * c;
            piece.SetSolid(solid, c);
            foreach (Transform child in piece.transform)
                if (child.GetComponent<NailHead>() != null || child.GetComponent<NailShot>() != null) child.localPosition -= c;
            foreach (var n in nails.Values)
            {
                if (n.a == piece.Id) n.entryA -= c;
                if (n.b == piece.Id) n.entryB -= c;
            }
        }

        // ---------------------------------------------------------------- structures

        /// <summary>Regroups pieces into structures by their nails and updates each one's physics.</summary>
        public void RebuildStructures(Vector3? splitVelocity = null, Vector3? splitAngularVelocity = null)
        {
            var existing = GetComponentsInChildren<Structure>().ToList();
            var velocities = new Dictionary<Structure, (Vector3 v, Vector3 w)>();
            foreach (var s in existing)
                velocities[s] = s.Body.isKinematic ? (splitVelocity ?? Vector3.zero, splitAngularVelocity ?? Vector3.zero) : (s.Body.linearVelocity, s.Body.angularVelocity);

            var components = NailGraph.Components(pieces.Keys, nails.Values.Select(n => (n.a, n.b)));
            var used = new HashSet<Structure>();
            foreach (var comp in components)
            {
                var members = comp.Select(id => pieces[id]).ToList();
                var target = members.Select(p => p.Structure).Where(s => s != null && !used.Contains(s))
                    .GroupBy(s => s).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault();
                var donor = members.Select(p => p.Structure).FirstOrDefault(s => s != null);
                if (target == null) target = NewStructure(members[0].transform.position, Quaternion.identity);
                used.Add(target);

                foreach (var p in members)
                    if (p.transform.parent != target.transform) p.transform.SetParent(target.transform, true);
                target.Pieces.Clear();
                target.Pieces.AddRange(members);
                (Vector3 v, Vector3 w) inherited = donor != null && velocities.TryGetValue(donor, out var vel) ? vel : (Vector3.zero, Vector3.zero);
                target.Refresh(inherited.v, inherited.w);
            }
            foreach (var s in existing)
                if (!used.Contains(s))
                {
                    s.Pieces.Clear();
                    Destroy(s.gameObject);
                }
        }

        // ---------------------------------------------------------------- queries

        public bool RaycastPiece(Ray ray, float reach, out (Piece Piece, RaycastHit Hit) result, out RaycastHit hit)
        {
            result = default;
            if (Physics.Raycast(ray, out hit, reach, BuildLayers.PieceMask, QueryTriggerInteraction.Ignore) && hit.collider.TryGetComponent<Piece>(out var piece))
            {
                result = (piece, hit);
                return true;
            }
            return false;
        }

        // ---------------------------------------------------------------- save / load

        public BuildSaveData ToSaveData()
        {
            var data = new BuildSaveData { nextPieceId = nextPieceId, nextNailId = nextNailId };
            foreach (var p in pieces.Values.OrderBy(p => p.Id))
            {
                var r = PieceRecord.From(p.Id, p.Kind, p.transform.position, p.transform.rotation, p.Solid, p.Planted, p.OnTable, p.SeatedIn);
                r.clamped = p == ClampedPiece;
                if (p.Paint != null) r.paint = p.Paint.EncodeBase64();
                data.pieces.Add(r);
            }
            foreach (var n in nails.Values.OrderBy(n => n.id)) data.nails.Add(n);
            return data;
        }

        public void Load(BuildSaveData data)
        {
            ClearAll();
            nextPieceId = data.nextPieceId;
            nextNailId = data.nextNailId;
            foreach (var r in data.pieces)
            {
                var p = CreatePiece(r.kind, r.ToSolid(), r.position, r.rotation, r.id);
                p.Planted = r.planted;
                p.OnTable = r.onTable;
                p.SeatedIn = r.seatedIn;
                if (!string.IsNullOrEmpty(r.paint)) p.EnsurePaint(paintMaterial).Decode(r.paint);
                Retint(p);
            }
            foreach (var n in data.nails)
                if (pieces.ContainsKey(n.a) && pieces.ContainsKey(n.b)) AddNail(n);
            RebuildStructures();
            var clamped = data.pieces.FirstOrDefault(r => r.clamped);
            if (clamped != null && pieces.TryGetValue(clamped.id, out var cp))
            {
                Physics.SyncTransforms();
                TryClamp(cp);
            }
        }

        public void ClearAll()
        {
            NailShot.ClearAll();
            ReleaseClamps(animate: false);
            foreach (var h in heads.Values) if (h != null) Destroy(h.gameObject);
            heads.Clear();
            nails.Clear();
            foreach (var p in pieces.Values) if (p != null) Destroy(p.gameObject);
            pieces.Clear();
            foreach (var s in GetComponentsInChildren<Structure>()) Destroy(s.gameObject);
        }

        public void QuickSave() => File.WriteAllText(QuickSavePath, JsonUtility.ToJson(ToSaveData(), true));

        public bool QuickLoad()
        {
            if (!File.Exists(QuickSavePath)) return false;
            Load(JsonUtility.FromJson<BuildSaveData>(File.ReadAllText(QuickSavePath)));
            return true;
        }
    }
}
