using System.Collections.Generic;
using UnityEngine;

namespace Cabin.Forestry
{
    /// <summary>
    /// A tree being chopped. Made from a terrain tree on the first chop: its own full mesh, ready to have the notch cut
    /// into the trunk (<see cref="NotchedMesh"/> — it looks exactly as it stood until then). Every chop bites into the
    /// side facing the axe; when too little trunk is left to hold it, the mesh is cut through at the chopping line
    /// into stump and top, each with a fresh-wood face, and the top goes over away from you.
    /// </summary>
    public sealed class ChoppingTree : MonoBehaviour
    {
        /// <summary>Half the notch's height (metres): a real felling notch is about a hand and a half across.</summary>
        const float NotchHalf = 0.16f;
        /// <summary>How deep one full chop bites (metres): a thin birch takes three or four, a big spruce eight or so.</summary>
        const float Bite = 0.045f;
        /// <summary>It goes over when this little of the cross-section is left.</summary>
        const float Holds = 0.32f;
        const float WoodDensity = 450f;

        ChoppableTree data;
        Forest forest;
        float cutY, half, y0, y1;
        readonly float[] depth = new float[ChopBand.Sectors];
        /// <summary>Each chop's face: the way it came from (trunk space, unit) and how far in it reached (fractions of the radius, 0–2).</summary>
        readonly List<Vector2> faceDirs = new List<Vector2>();
        readonly List<float> faceDepths = new List<float>();
        MeshFilter tree;
        NotchedMesh notched;
        GameObject top;
        CapsuleCollider trunk;
        Material woodMaterial;
        TrunkSection lo, hi;

        public bool Fallen { get; private set; }
        /// <summary>World height of the chopping line.</summary>
        public float CutHeight => transform.TransformPoint(new Vector3(0f, cutY, 0f)).y;
        public ChoppableTree Data => data;
        /// <summary>Trunk radius in the world at a world height.</summary>
        public float RadiusAtWorld(float worldY) => data.RadiusAt((worldY - transform.position.y) / transform.lossyScale.y) * transform.lossyScale.x;

        public static ChoppingTree Create(Forest forest, ChoppableTree data, Vector3 basePos, Quaternion rotation, Vector3 scale, float cutWorldY, Material woodMaterial)
        {
            var go = new GameObject($"Chopped_{data.name}");
            go.transform.SetPositionAndRotation(basePos, rotation);
            go.transform.localScale = scale;
            var t = go.AddComponent<ChoppingTree>();
            t.forest = forest;
            t.data = data;
            t.woodMaterial = woodMaterial;
            t.cutY = (cutWorldY - basePos.y) / scale.y;
            t.half = NotchHalf / scale.y;
            t.y0 = t.cutY - t.half;
            t.y1 = t.cutY + t.half;
            t.Build();
            return t;
        }

        void Build()
        {
            var below = new List<MeshSlicer.EdgePoint>();
            var above = new List<MeshSlicer.EdgePoint>();
            MeshSlicer.Outline(data.Mesh, y0, below);
            MeshSlicer.Outline(data.Mesh, y1, above);
            lo = TrunkSection.From(below, y0, data.RadiusAt(y0));
            hi = TrunkSection.From(above, y1, data.RadiusAt(y1));
            notched = new NotchedMesh(data.Mesh, lo, hi);
            tree = Part("Tree", notched.Build(null, depth, cutY, half), transform, Materials()).GetComponent<MeshFilter>();

            // Stands in for the terrain tree's collider until it goes over.
            trunk = gameObject.AddComponent<CapsuleCollider>();
            trunk.radius = Average(lo);
            trunk.height = Mathf.Min(data.Height, 8f / transform.lossyScale.y);
            trunk.center = new Vector3(lo.Centre.x, trunk.height * 0.5f, lo.Centre.y);
        }

        static GameObject Part(string name, Mesh mesh, Transform parent, Material[] materials)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = materials;
            return go;
        }

        /// <summary>The tree's own, then fresh wood for the notch.</summary>
        Material[] Materials()
        {
            var mats = new List<Material>(data.Materials ?? new Material[0]) { woodMaterial };
            return mats.ToArray();
        }

        static void Discard(Object o)
        {
            if (Application.isPlaying) Destroy(o);
            else DestroyImmediate(o);
        }

        static float Average(TrunkSection s)
        {
            float sum = 0f;
            foreach (float r in s.Radius) sum += r;
            return sum / s.Radius.Length;
        }

        /// <summary>
        /// A chop from <paramref name="from"/> (a world point, where the axe comes from). The notch's face is flat, square
        /// to the way you're chopping from: each chop takes it <see cref="Bite"/> further in (more with more
        /// <paramref name="power"/>), so it widens as it deepens, like a real felling notch. Chopping from another side
        /// starts from wherever the notch already reaches that way. Returns where it bit (world) and whether that
        /// brought it down.
        /// </summary>
        public bool Chop(Vector3 from, float power, out Vector3 point, out Vector3 normal)
        {
            Vector3 local = transform.InverseTransformPoint(from) - new Vector3(lo.Centre.x, 0f, lo.Centre.y);
            float facing = Mathf.Atan2(local.z, local.x);
            var face = new Vector2(Mathf.Cos(facing), Mathf.Sin(facing));
            float radius = Average(lo) * transform.lossyScale.x;

            // How far in the notch already reaches from this side: the furthest of the earlier faces, less how far round
            // from here it was cut (a face cut from the side only helps once it's past the middle).
            float reached = 0f;
            for (int k = 0; k < faceDirs.Count; k++)
                reached = Mathf.Max(reached, faceDepths[k] - (1f - Vector2.Dot(faceDirs[k], face)));
            float reach = reached + Bite * power * Random.Range(0.85f, 1.15f) / Mathf.Max(0.05f, radius);
            faceDirs.Add(face);
            faceDepths.Add(reach);
            // Every sector facing this way cut back to the face (a chord across the trunk, 1 − reach from its middle).
            for (int i = 0; i < depth.Length; i++)
            {
                float along = Vector2.Dot(Ray(i), face);
                if (along > 1e-3f) depth[i] = Mathf.Clamp01(Mathf.Max(depth[i], 1f - (1f - reach) / along));
            }
            var dir = new Vector3(face.x, 0f, face.y);
            int s = TrunkSection.Sector(facing);
            TrunkSection.At(lo, hi, cutY, s, out var c, out float r);
            point = transform.TransformPoint(new Vector3(c.x, cutY, c.y) + dir * r * (1f - Mathf.Min(0.95f, depth[s]) * 0.6f));
            normal = transform.TransformDirection(dir).normalized;
            tree.sharedMesh = notched.Build(tree.sharedMesh, depth, cutY, half);
            if (Left() > Holds) return false;
            Fell();
            return true;
        }

        static Vector2 Ray(int sector)
        {
            float a = sector * Mathf.PI * 2f / ChopBand.Sectors;
            return new Vector2(Mathf.Cos(a), Mathf.Sin(a));
        }

        /// <summary>Points spread evenly over the unit disc (a sunflower spiral), for measuring what's left.</summary>
        static readonly Vector2[] DiscSamples = Disc(256);

        static Vector2[] Disc(int n)
        {
            var p = new Vector2[n];
            float golden = Mathf.PI * (3f - Mathf.Sqrt(5f));
            for (int i = 0; i < n; i++)
            {
                float r = Mathf.Sqrt((i + 0.5f) / n), a = i * golden;
                p[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
            }
            return p;
        }

        /// <summary>How much of the trunk's cross-section the notch has left (0–1): what's behind every chop's face.</summary>
        public float Left()
        {
            int kept = 0;
            foreach (var p in DiscSamples)
            {
                bool cut = false;
                for (int k = 0; k < faceDirs.Count && !cut; k++) cut = Vector2.Dot(p, faceDirs[k]) > 1f - faceDepths[k];
                if (!cut) kept++;
            }
            return kept / (float)DiscSamples.Length;
        }

        /// <summary>
        /// The top goes over away from the notch (away from whoever chopped it), tipping on the wood left at the back,
        /// then falls free.
        /// </summary>
        void Fell()
        {
            Fallen = true;
            Vector3 fall = -ChopBand.Weakest(depth);
            // Cut through at the chopping line: the stump keeps what's below, the top takes the rest; both get a face.
            var whole = tree.sharedMesh;
            tree.sharedMesh = MeshSlicer.Keep(whole, cutY, false, "Stump");
            tree.name = "Stump";
            top = Part("Top", MeshSlicer.Keep(whole, cutY, true, "Top"), transform, Materials());
            Discard(whole);
            Part("StumpFace", ChopBand.Cap(lo, hi, cutY, depth, true), tree.transform, new[] { woodMaterial });
            Part("ButtFace", ChopBand.Cap(lo, hi, cutY, depth, false), top.transform, new[] { woodMaterial });

            trunk.height = cutY;
            trunk.center = new Vector3(lo.Centre.x, trunk.height * 0.5f, lo.Centre.y);

            top.transform.SetParent(null, true);
            top.name = $"Felled_{data.name}";
            Colliders(top);

            var body = top.AddComponent<Rigidbody>();
            Vector3 s = transform.lossyScale;
            float r = Average(hi) * s.x, h = (data.Height - y1) * s.y;
            body.mass = Mathf.Clamp(Mathf.PI * r * r * h * WoodDensity, 40f, 3000f);
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            // Weight a touch past the hinge, the way it's to fall: on its own a notched trunk tips into the notch (onto
            // whoever cut it). Like a tree with a natural lean — you can't see where its weight is.
            body.centerOfMass = new Vector3(hi.Centre.x, (data.Height - y1) * 0.35f + y1, hi.Centre.y) + fall * Average(lo) * 1.3f;

            // Hinge on the far edge of the trunk, across the way it falls.
            TrunkSection.At(lo, hi, cutY, 0, out var c, out _);
            var joint = top.AddComponent<HingeJoint>();
            joint.anchor = new Vector3(c.x, cutY, c.y) + fall * Average(lo) * 0.85f;
            joint.axis = Vector3.Cross(Vector3.up, fall);
            joint.autoConfigureConnectedAnchor = true;
            top.AddComponent<FallingTree>().Begin(forest, transform.TransformDirection(fall));
            body.AddTorque(transform.TransformDirection(Vector3.Cross(Vector3.up, fall)).normalized * body.mass * 0.6f, ForceMode.Impulse);
        }

        /// <summary>
        /// The felled top's colliders: a capsule down its trunk, and a fatter one through the crown so it lies on its
        /// branches. The crown's shrinks until it clears the trees round it (starting inside one would throw it).
        /// </summary>
        void Colliders(GameObject felled)
        {
            float length = data.Height - y1;
            var log = felled.AddComponent<CapsuleCollider>();
            log.direction = 1;
            log.radius = Average(hi);
            log.height = length;
            log.center = new Vector3(hi.Centre.x, y1 + length * 0.5f, hi.Centre.y);
            Physics.IgnoreCollision(log, trunk);

            var bounds = felled.GetComponent<MeshFilter>().sharedMesh.bounds;
            float bottom = Mathf.Lerp(y1, data.Height, 0.3f);
            var crown = felled.AddComponent<CapsuleCollider>();
            crown.direction = 1;
            crown.radius = Mathf.Min(bounds.extents.x, bounds.extents.z) * 0.5f;
            crown.height = data.Height - bottom;
            crown.center = new Vector3(bounds.center.x, (bottom + data.Height) * 0.5f, bounds.center.z);
            Physics.IgnoreCollision(crown, trunk);
            for (int tries = 0; tries < 8 && Blocked(crown); tries++) crown.radius *= 0.7f;
            if (Blocked(crown)) Discard(crown);
        }

        bool Blocked(CapsuleCollider capsule)
        {
            var t = capsule.transform;
            float r = capsule.radius * Mathf.Max(t.lossyScale.x, t.lossyScale.z);
            float half = Mathf.Max(0f, capsule.height * 0.5f * t.lossyScale.y - r);
            Vector3 centre = t.TransformPoint(capsule.center);
            foreach (var hit in Physics.OverlapCapsule(centre - t.up * half, centre + t.up * half, r, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                if (hit.transform != t && hit != trunk) return true;
            return false;
        }
    }
}
