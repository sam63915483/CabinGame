using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Cabin.Editor
{
    /// <summary>
    /// The lumberjack's fingers, from Mixamo. Meshy's rig has no finger bones; Sam ran the same mesh through Mixamo's
    /// auto-rigger (Art/Models/Lumberjack_v3_Mixamo.fbx), which gave every finger three joints and smooth skin weights.
    /// Those are matched onto our mesh vertex by vertex (same mesh, same rest pose) and grafted onto our skeleton as
    /// real finger bones (<see cref="Graft"/>), so the game poses the hands at run time (Cabin.Player.HandPose). The
    /// skeleton's other bones and the animations don't change. <see cref="Close"/> bends a scratch copy of the mesh
    /// the same way, to find where a handle sits in the closed hand.
    /// </summary>
    public sealed class FingerRig
    {
        public const string MixamoPath = "Assets/_Project/Art/Models/Lumberjack_v3_Mixamo.fbx";
        /// <summary>Thumb first, then index to little finger, as Mixamo names them.</summary>
        static readonly string[] Fingers = { "Thumb", "Index", "Middle", "Ring", "Pinky" };
        const int Thumb = 0, Index = 1, Middle = 2, Pinky = 4;

        /// <summary>One hand: its finger joints (rest, model space; four per finger, the last the tip end), palm, frames.</summary>
        sealed class Hand
        {
            public readonly Vector3[,] Joints = new Vector3[5, 4];
            public Vector3 Palm;
            public Matrix4x4 WorldToMesh, WorldToHand;
        }

        readonly Hand[] hands = new Hand[2]; // 0 right, 1 left
        /// <summary>Per vertex of the source mesh: up to four (hand, finger, joint, weight).</summary>
        readonly List<(int hand, int finger, int joint, float weight)>[] influences;

        FingerRig(int vertices) => influences = new List<(int, int, int, float)>[vertices];

        /// <summary>
        /// The fingers for <paramref name="full"/> (the lumberjack's full body, its bones at rest), or null if the Mixamo
        /// rig isn't in the project.
        /// </summary>
        public static FingerRig Load(SkinnedMeshRenderer full)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(MixamoPath);
            if (source == null) return null;
            var mixamo = Object.Instantiate(source);
            var baked = new Mesh();
            try
            {
                var mSkin = mixamo.GetComponentInChildren<SkinnedMeshRenderer>();
                mSkin.BakeMesh(baked, true);
                var mPos = baked.vertices;
                for (int i = 0; i < mPos.Length; i++) mPos[i] = mSkin.transform.TransformPoint(mPos[i]);

                // Ours at rest, in the same space as the hands' matrices.
                var mesh = full.sharedMesh;
                var bones = full.bones;
                var bind = mesh.bindposes;
                var ourPos = mesh.vertices;
                var ourWeights = mesh.boneWeights;
                for (int i = 0; i < ourPos.Length; i++)
                    ourPos[i] = (bones[ourWeights[i].boneIndex0].localToWorldMatrix * bind[ourWeights[i].boneIndex0]).MultiplyPoint3x4(ourPos[i]);

                // Line Mixamo's up with ours (it comes in at a hundredth of the size): by their bounds.
                Bounds mb = Bounds(mPos), ob = Bounds(ourPos);
                float k = ob.size.y / mb.size.y;
                Vector3 Map(Vector3 p) => (p - mb.center) * k + ob.center;
                for (int i = 0; i < mPos.Length; i++) mPos[i] = Map(mPos[i]);

                var rig = new FingerRig(ourPos.Length);
                string[] sides = { "Right", "Left" };
                var bonesBy = new Dictionary<string, Transform>();
                foreach (var b in mixamo.GetComponentsInChildren<Transform>()) bonesBy[b.name.Substring(b.name.IndexOf(':') + 1)] = b;
                for (int h = 0; h < 2; h++)
                {
                    var hand = new Hand();
                    for (int f = 0; f < Fingers.Length; f++)
                        for (int j = 0; j < 4; j++)
                            hand.Joints[f, j] = Map(bonesBy[$"{sides[h]}Hand{Fingers[f]}{j + 1}"].position);
                    int handBone = System.Array.FindIndex(bones, b => b.name == $"{sides[h]}Hand");
                    var skin = bones[handBone].localToWorldMatrix * bind[handBone];
                    hand.WorldToMesh = skin.inverse;
                    hand.WorldToHand = bones[handBone].worldToLocalMatrix;
                    // The palm faces the hand bone's +x on the right, −x on the left (Meshy's rig).
                    hand.Palm = bones[handBone].TransformDirection(new Vector3(h == 0 ? 1f : -1f, 0f, 0f)).normalized;
                    rig.hands[h] = hand;
                }

                // Which Mixamo bone is which finger joint.
                var mBones = mSkin.bones;
                var joint = new (int hand, int finger, int joint)[mBones.Length];
                for (int b = 0; b < mBones.Length; b++)
                {
                    joint[b] = (-1, -1, -1);
                    string n = mBones[b].name.Substring(mBones[b].name.IndexOf(':') + 1);
                    for (int h = 0; h < 2; h++)
                        for (int f = 0; f < Fingers.Length; f++)
                        {
                            string prefix = $"{sides[h]}Hand{Fingers[f]}";
                            if (!n.StartsWith(prefix) || !int.TryParse(n.Substring(prefix.Length), out int j)) continue;
                            joint[b] = (h, f, Mathf.Clamp(j - 1, 0, 2));
                        }
                }

                // Each of our vertices takes the finger weights of the Mixamo vertex in the same place.
                const float cell = 0.002f;
                var grid = new Dictionary<Vector3Int, List<int>>();
                Vector3Int Key(Vector3 p) => new Vector3Int(Mathf.FloorToInt(p.x / cell), Mathf.FloorToInt(p.y / cell), Mathf.FloorToInt(p.z / cell));
                for (int i = 0; i < mPos.Length; i++)
                {
                    var key = Key(mPos[i]);
                    if (!grid.TryGetValue(key, out var list)) grid[key] = list = new List<int>();
                    list.Add(i);
                }
                var mWeights = baked.boneWeights.Length > 0 ? baked.boneWeights : mSkin.sharedMesh.boneWeights;
                for (int i = 0; i < ourPos.Length; i++)
                {
                    var key = Key(ourPos[i]);
                    int nearest = -1;
                    float best = cell * cell;
                    for (int dz = -1; dz <= 1; dz++)
                    for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (!grid.TryGetValue(key + new Vector3Int(dx, dy, dz), out var list)) continue;
                        foreach (int m in list)
                        {
                            float d = (mPos[m] - ourPos[i]).sqrMagnitude;
                            if (d < best) { best = d; nearest = m; }
                        }
                    }
                    if (nearest < 0) continue;
                    var w = mWeights[nearest];
                    void Add(int b, float wt)
                    {
                        if (wt <= 0f || joint[b].hand < 0) return;
                        rig.influences[i] ??= new List<(int, int, int, float)>();
                        rig.influences[i].Add((joint[b].hand, joint[b].finger, joint[b].joint, wt));
                    }
                    Add(w.boneIndex0, w.weight0);
                    Add(w.boneIndex1, w.weight1);
                    Add(w.boneIndex2, w.weight2);
                    Add(w.boneIndex3, w.weight3);
                }
                return rig;
            }
            finally
            {
                Object.DestroyImmediate(mixamo);
                Object.DestroyImmediate(baked);
            }
        }

        /// <summary>The name a grafted finger joint gets: Right/Left, Hand, the finger, 1–3 from the knuckle (Mixamo's names).</summary>
        public static string JointName(bool left, int finger, int joint) => $"{(left ? "Left" : "Right")}Hand{Fingers[finger]}{joint + 1}";

        /// <summary>
        /// Gives <paramref name="full"/> real finger bones: three joints per finger under each hand bone, placed where
        /// Mixamo put them and turned so each bends towards the palm (the thumb across it) about its own +x, with the
        /// finger running along its +y. The mesh is re-skinned with Mixamo's finger weights (the rest of each vertex's
        /// weight stays where it was, scaled down), at most four influences. Returns the new mesh, which
        /// <paramref name="full"/> now draws; its bones are the old ones plus the fingers, so old indices still hold.
        /// </summary>
        public Mesh Graft(SkinnedMeshRenderer full)
        {
            var bones = new List<Transform>(full.bones);
            var bind = new List<Matrix4x4>(full.sharedMesh.bindposes);
            var index = new int[2, 5, 3];
            for (int h = 0; h < 2; h++)
            {
                var hand = hands[h];
                var handBone = bones.Find(b => b.name == (h == 0 ? "RightHand" : "LeftHand"));
                // Each finger joint skins its vertices at rest exactly where the hand bone does: the rig needn't be
                // in its bind pose (Meshy's isn't), so the joint's bind pose is taken through the hand's skin matrix.
                var handSkin = hand.WorldToMesh.inverse;
                for (int f = 0; f < Fingers.Length; f++)
                {
                    Transform parent = handBone;
                    for (int j = 0; j < 3; j++)
                    {
                        Vector3 p = hand.Joints[f, j], d = (hand.Joints[f, j + 1] - p).normalized;
                        Vector3 towards = f == Thumb ? (hand.Joints[Middle, 1] - p).normalized : hand.Palm;
                        Vector3 axis = Vector3.Cross(d, towards).normalized;
                        if (Vector3.Dot(Quaternion.AngleAxis(10f, axis) * d - d, towards) < 0f) axis = -axis;
                        var go = new GameObject(JointName(h == 1, f, j));
                        go.transform.SetParent(parent, true);
                        go.transform.SetPositionAndRotation(p, Quaternion.LookRotation(Vector3.Cross(axis, d), d));
                        index[h, f, j] = bones.Count;
                        bones.Add(go.transform);
                        bind.Add(go.transform.worldToLocalMatrix * handSkin);
                        parent = go.transform;
                    }
                }
            }

            var mesh = Object.Instantiate(full.sharedMesh);
            mesh.name = full.sharedMesh.name;
            var old = mesh.boneWeights;
            var weights = new BoneWeight[old.Length];
            var pairs = new List<(int bone, float weight)>(8);
            for (int i = 0; i < old.Length; i++)
            {
                var list = i < influences.Length ? influences[i] : null;
                float total = 0f;
                if (list != null) foreach (var x in list) total += x.weight;
                total = Mathf.Clamp01(total);
                pairs.Clear();
                var w = old[i];
                void Keep(int b, float wt) { if (wt > 0f) pairs.Add((b, wt * (1f - total))); }
                Keep(w.boneIndex0, w.weight0);
                Keep(w.boneIndex1, w.weight1);
                Keep(w.boneIndex2, w.weight2);
                Keep(w.boneIndex3, w.weight3);
                if (list != null) foreach (var (h, f, j, wt) in list) pairs.Add((index[h, f, j], wt));
                weights[i] = TopFour(pairs);
            }
            mesh.boneWeights = weights;
            mesh.bindposes = bind.ToArray();
            full.bones = bones.ToArray();
            full.sharedMesh = mesh;
            return mesh;
        }

        /// <summary>The four heaviest influences (the same bone merged), normalised to sum 1.</summary>
        public static BoneWeight TopFour(List<(int bone, float weight)> pairs)
        {
            var merged = new Dictionary<int, float>();
            foreach (var (b, wt) in pairs) merged[b] = (merged.TryGetValue(b, out var have) ? have : 0f) + wt;
            var top = new List<KeyValuePair<int, float>>(merged);
            top.Sort((a, b) => b.Value.CompareTo(a.Value));
            float sum = 0f;
            for (int k = 0; k < top.Count && k < 4; k++) sum += top[k].Value;
            if (sum <= 0f) return new BoneWeight { boneIndex0 = top.Count > 0 ? top[0].Key : 0, weight0 = 1f };
            var bw = new BoneWeight();
            for (int k = 0; k < top.Count && k < 4; k++)
            {
                float v = top[k].Value / sum;
                switch (k)
                {
                    case 0: bw.boneIndex0 = top[k].Key; bw.weight0 = v; break;
                    case 1: bw.boneIndex1 = top[k].Key; bw.weight1 = v; break;
                    case 2: bw.boneIndex2 = top[k].Key; bw.weight2 = v; break;
                    default: bw.boneIndex3 = top[k].Key; bw.weight3 = v; break;
                }
            }
            return bw;
        }

        static Bounds Bounds(Vector3[] points)
        {
            var b = new Bounds(points[0], Vector3.zero);
            foreach (var p in points) b.Encapsulate(p);
            return b;
        }

        /// <summary>
        /// Closes one hand of <paramref name="mesh"/> (built on the source mesh: its first vertices are the source's):
        /// each finger's three joints bent towards the palm by <paramref name="bend"/> (degrees), the thumb's swung in
        /// across the fingers by <paramref name="thumb"/>. Also where a handle would sit in the closed hand (the middle
        /// of the curled fingers, hand bone space) and the way through it (towards the index finger).
        /// </summary>
        public void Close(Mesh mesh, bool left, float[] bend, float[] thumb, out Vector3 hollow, out Vector3 through)
        {
            var hand = hands[left ? 1 : 0];
            int h = left ? 1 : 0;

            // Each joint's transform: rotate about the joint, after everything nearer the wrist has moved it.
            var pose = new Matrix4x4[5, 3];
            for (int f = 0; f < Fingers.Length; f++)
            {
                var parent = Matrix4x4.identity;
                for (int j = 0; j < 3; j++)
                {
                    Vector3 p = hand.Joints[f, j], d = (hand.Joints[f, j + 1] - p).normalized;
                    // Fingers fold towards the palm; the thumb across it, towards the middle finger.
                    Vector3 towards = f == Thumb ? (hand.Joints[Middle, 1] - p).normalized : hand.Palm;
                    var axis = Vector3.Cross(d, towards).normalized;
                    float angle = f == Thumb ? thumb[j] : bend[j];
                    if (Vector3.Dot(Quaternion.AngleAxis(angle, axis) * d - d, towards) < 0f) angle = -angle;
                    var turn = Matrix4x4.Translate(p) * Matrix4x4.Rotate(Quaternion.AngleAxis(angle, axis)) * Matrix4x4.Translate(-p);
                    parent = parent * turn;
                    pose[f, j] = parent;
                }
            }

            var v = mesh.vertices;
            var n = mesh.normals;
            var toWorld = hand.WorldToMesh.inverse;
            int count = Mathf.Min(v.Length, influences.Length);
            for (int i = 0; i < count; i++)
            {
                var list = influences[i];
                if (list == null) continue;
                Vector3 rest = toWorld.MultiplyPoint3x4(v[i]), restN = toWorld.MultiplyVector(n[i]);
                Vector3 p = Vector3.zero, q = Vector3.zero;
                float total = 0f;
                foreach (var (ih, f, j, w) in list)
                {
                    if (ih != h) continue;
                    p += pose[f, j].MultiplyPoint3x4(rest) * w;
                    q += pose[f, j].MultiplyVector(restN) * w;
                    total += w;
                }
                if (total <= 0f) continue;
                p += rest * (1f - total);
                q += restN * (1f - total);
                v[i] = hand.WorldToMesh.MultiplyPoint3x4(p);
                n[i] = hand.WorldToMesh.MultiplyVector(q).normalized;
            }
            mesh.vertices = v;
            mesh.normals = n;
            mesh.RecalculateBounds();

            // The handle: the circle the bent joints of each finger lie on, averaged over the four.
            hollow = Vector3.zero;
            for (int f = Index; f <= Pinky; f++)
            {
                Vector3 a = hand.Joints[f, 0], b = pose[f, 0].MultiplyPoint3x4(hand.Joints[f, 1]), c = pose[f, 1].MultiplyPoint3x4(hand.Joints[f, 2]);
                hollow += Circumcentre(a, b, c);
            }
            hollow = hand.WorldToHand.MultiplyPoint3x4(hollow / 4f);
            through = hand.WorldToHand.MultiplyVector(hand.Joints[Index, 0] - hand.Joints[Pinky, 0]).normalized;
        }

        static Vector3 Circumcentre(Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 ab = b - a, ac = c - a, nrm = Vector3.Cross(ab, ac);
            float d = 2f * nrm.sqrMagnitude;
            if (d < 1e-12f) return (a + b + c) / 3f;
            return a + (Vector3.Cross(nrm, ab) * ac.sqrMagnitude + Vector3.Cross(ac, nrm) * ab.sqrMagnitude) / d;
        }
    }
}
