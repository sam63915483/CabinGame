using System.Collections.Generic;
using System.Linq;
using Cabin.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Cabin.Editor
{
    /// <summary>
    /// Builds the player's body prefab from the Meshy-rigged lumberjack: real finger bones grafted on from the Mixamo
    /// rig of the same mesh (<see cref="FingerRig.Graft"/>), the PS2 look (texture shrunk and cut to 16-bit colour, no
    /// shine) and, on the same skeleton, the meshes first person needs — the body without head or arms (what you see
    /// looking down: chest, legs, boots), and each arm on its own (drawn with it). Split by skin weights, so they move
    /// exactly with the full body. Re-runnable; assets keep their GUIDs.
    /// </summary>
    public static class CharacterMeshes
    {
        public const string FullName = "Body_Full";
        public const string TorsoName = "Body_FirstPersonTorso";
        public const string ArmName = "Body_FirstPersonRightArm";
        public const string LeftArmName = "Body_FirstPersonLeftArm";
        /// <summary>How far each finger joint bends round a handle (degrees, knuckle first) and the thumb swings in: the same as Cabin.Player.HandPose's grip, so the handle sits where <see cref="GripFrame"/> says.</summary>
        static readonly float[] GripBend = Cabin.Player.HandPose.GripBend, GripThumb = Cabin.Player.HandPose.GripThumb;
        public const int TextureSize = 512;
        /// <summary>Colour levels per channel: 32 is 16-bit colour, as on the PS2's framebuffer.</summary>
        const int Levels = 32;

        static readonly string[] ArmBones = { "RightArm", "RightForeArm", "RightHand" };
        static readonly string[] LeftArmBones = { "LeftArm", "LeftForeArm", "LeftHand" };
        public static GameObject Build(string rigPath, Matrix4x4[] cuts, string meshPath, string texturePath, string materialPath, string prefabPath)
        {
            var rig = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(rigPath));
            try
            {
                rig.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                var full = rig.GetComponentInChildren<SkinnedMeshRenderer>();
                // Real finger bones first, so every mesh split from the body bends its fingers.
                var fingers = FingerRig.Load(full);
                if (fingers == null) Debug.LogWarning($"[Cabin] No finger rig at {FingerRig.MixamoPath}: the hands won't close");
                else fingers.Graft(full).name = FullName;
                var source = full.sharedMesh;
                var bones = full.bones;

                float Share(BoneWeight w, HashSet<int> set) =>
                    (set.Contains(w.boneIndex0) ? w.weight0 : 0f) + (set.Contains(w.boneIndex1) ? w.weight1 : 0f) +
                    (set.Contains(w.boneIndex2) ? w.weight2 : 0f) + (set.Contains(w.boneIndex3) ? w.weight3 : 0f);
                var arm = Indices(bones, ArmBones);
                var leftArm = Indices(bones, LeftArmBones);
                var weights = source.boneWeights;
                bool IsArm(int v) => Share(weights[v], arm) > 0.6f;
                bool IsLeftArm(int v) => Share(weights[v], leftArm) > 0.6f;
                // The head goes: whatever lies inside the cut boxes. By shape, not weights — Meshy's rig hangs the
                // collar and upper chest off the neck and headfront bones. The neck is a few tall triangles, so
                // they're sliced along the box faces rather than dropped whole.
                var rest = RestPositions(source, bones);
                var torsoMesh = Subset(source, TorsoName, (a, b, c) => !(IsArm(a) && IsArm(b) && IsArm(c)) && !(IsLeftArm(a) && IsLeftArm(b) && IsLeftArm(c)));
                if (cuts.Length > 0) Slice(torsoMesh, rest, q => OutsideAll(cuts, q));
                var armMesh = Subset(source, ArmName, (a, b, c) => IsArm(a) && IsArm(b) && IsArm(c));
                var leftArmMesh = Subset(source, LeftArmName, (a, b, c) => IsLeftArm(a) && IsLeftArm(b) && IsLeftArm(c));
                SandboxAssetGenerator.EnsureFolder(System.IO.Path.GetDirectoryName(meshPath).Replace('\\', '/'));
                RemoveSubs(meshPath, "_Fist", "_Grip"); // the old baked closed hands: the fingers bend for real now
                if (fingers != null) full.sharedMesh = StoreSub(source, meshPath);
                torsoMesh = StoreSub(torsoMesh, meshPath);
                armMesh = StoreSub(armMesh, meshPath);
                leftArmMesh = StoreSub(leftArmMesh, meshPath);

                var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                if (material == null)
                {
                    material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                    AssetDatabase.CreateAsset(material, materialPath);
                }
                material.SetTexture("_BaseMap", Posterized(full.sharedMaterial, texturePath));
                material.SetColor("_BaseColor", Color.white);
                material.SetFloat("_Smoothness", 0f);
                material.SetFloat("_SpecularHighlights", 0f);
                material.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
                EditorUtility.SetDirty(material);

                full.name = FullName;
                full.sharedMaterials = new[] { material };
                Copy(full, TorsoName, torsoMesh);
                Copy(full, ArmName, armMesh);
                Copy(full, LeftArmName, leftArmMesh);
                rig.name = System.IO.Path.GetFileNameWithoutExtension(prefabPath);
                var prefab = PrefabUtility.SaveAsPrefabAsset(rig, prefabPath);
                AssetDatabase.SaveAssets();
                return prefab;
            }
            finally
            {
                Object.DestroyImmediate(rig);
            }
        }

        /// <summary>
        /// Where a handle goes in the gripping hand (<see cref="GripBend"/>), in the hand bone's space: the middle of the
        /// curled fingers, and the way through them (towards the index finger). False without the finger rig.
        /// </summary>
        public static bool GripFrame(bool left, out Vector3 centre, out Vector3 axis)
        {
            centre = Vector3.zero;
            axis = Vector3.forward;
            var rig = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(CharacterGenerator.RiggedModelPath));
            try
            {
                rig.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                var full = rig.GetComponentInChildren<SkinnedMeshRenderer>();
                var fingers = FingerRig.Load(full);
                if (fingers == null) return false;
                var copy = Object.Instantiate(full.sharedMesh);
                fingers.Close(copy, left, GripBend, GripThumb, out centre, out axis);
                Object.DestroyImmediate(copy);
                return true;
            }
            finally
            {
                Object.DestroyImmediate(rig);
            }
        }

        /// <summary>
        /// How far outside every cut box a point is (negative: inside one), measured in each box's own unit-cube
        /// space — exact enough for slicing along a face, and its sign is all that decides what's kept.
        /// </summary>
        public static float OutsideAll(Matrix4x4[] cuts, Vector3 p)
        {
            float d = float.MaxValue;
            foreach (var toBox in cuts)
            {
                Vector3 q = toBox.MultiplyPoint3x4(p);
                Vector3 e = new Vector3(Mathf.Abs(q.x) - 0.5f, Mathf.Abs(q.y) - 0.5f, Mathf.Abs(q.z) - 0.5f);
                float outside = Vector3.Max(e, Vector3.zero).magnitude + Mathf.Min(Mathf.Max(e.x, Mathf.Max(e.y, e.z)), 0f);
                d = Mathf.Min(d, outside);
            }
            return d;
        }

        /// <summary>Where each vertex is at rest, in world space (the rig is at the origin).</summary>
        static Vector3[] RestPositions(Mesh mesh, Transform[] bones)
        {
            var bind = mesh.bindposes;
            var skin = bones.Select((b, i) => b.localToWorldMatrix * bind[i]).ToArray();
            var v = mesh.vertices;
            var w = mesh.boneWeights;
            var rest = new Vector3[v.Length];
            for (int i = 0; i < v.Length; i++)
                rest[i] = skin[w[i].boneIndex0].MultiplyPoint3x4(v[i]) * w[i].weight0 + skin[w[i].boneIndex1].MultiplyPoint3x4(v[i]) * w[i].weight1
                    + skin[w[i].boneIndex2].MultiplyPoint3x4(v[i]) * w[i].weight2 + skin[w[i].boneIndex3].MultiplyPoint3x4(v[i]) * w[i].weight3;
            return rest;
        }

        /// <summary>The bones named, and the finger joints under any hand among them (RightHandIndex1…), which belong to that arm.</summary>
        static HashSet<int> Indices(Transform[] bones, string[] names) =>
            new HashSet<int>(Enumerable.Range(0, bones.Length).Where(i =>
                names.Contains(bones[i].name) || names.Any(n => n.EndsWith("Hand") && bones[i].name.StartsWith(n))));

        /// <summary>
        /// Cuts the mesh along the surface where <paramref name="keep"/> (of a vertex's rest position) is zero, keeping
        /// the positive side: triangles across it are sliced, with new vertices on the cut interpolated from the edge
        /// (skinned like the vertex on the kept side). At rest the skinning is one rigid transform, so slicing in rest
        /// space and interpolating mesh-space attributes agree.
        /// </summary>
        static void Slice(Mesh mesh, Vector3[] rest, System.Func<Vector3, float> keep)
        {
            var pos = new List<Vector3>(); mesh.GetVertices(pos);
            var nrm = new List<Vector3>(); mesh.GetNormals(nrm);
            var tan = new List<Vector4>(); mesh.GetTangents(tan);
            var uv = new List<Vector2>(); mesh.GetUVs(0, uv);
            var col = new List<Color>(); mesh.GetColors(col);
            var bw = new List<BoneWeight>(mesh.boneWeights);
            var side = rest.Select(keep).ToList();
            var cuts = new Dictionary<(int, int), int>();

            int Cut(int a, int b)
            {
                var key = a < b ? (a, b) : (b, a);
                if (cuts.TryGetValue(key, out int made)) return made;
                float t = side[a] / (side[a] - side[b]);
                pos.Add(Vector3.Lerp(pos[a], pos[b], t));
                if (nrm.Count > 0) nrm.Add(Vector3.Lerp(nrm[a], nrm[b], t).normalized);
                if (tan.Count > 0) tan.Add(Vector4.Lerp(tan[a], tan[b], t));
                if (uv.Count > 0) uv.Add(Vector2.Lerp(uv[a], uv[b], t));
                if (col.Count > 0) col.Add(Color.Lerp(col[a], col[b], t));
                bw.Add(side[a] >= 0f ? bw[a] : bw[b]);
                side.Add(0f);
                return cuts[key] = pos.Count - 1;
            }

            var subs = new List<int>[mesh.subMeshCount];
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                var tris = mesh.GetTriangles(s);
                var kept = subs[s] = new List<int>();
                var poly = new List<int>(4);
                for (int i = 0; i < tris.Length; i += 3)
                {
                    // Sutherland–Hodgman against one surface: the kept part of a triangle is a triangle or a quad.
                    poly.Clear();
                    for (int k = 0; k < 3; k++)
                    {
                        int a = tris[i + k], b = tris[i + (k + 1) % 3];
                        if (side[a] >= 0f) poly.Add(a);
                        if ((side[a] >= 0f) != (side[b] >= 0f)) poly.Add(Cut(a, b));
                    }
                    for (int k = 1; k + 1 < poly.Count; k++) kept.AddRange(new[] { poly[0], poly[k], poly[k + 1] });
                }
            }

            mesh.indexFormat = pos.Count > 65535 ? UnityEngine.Rendering.IndexFormat.UInt32 : mesh.indexFormat;
            mesh.SetVertices(pos);
            if (nrm.Count > 0) mesh.SetNormals(nrm);
            if (tan.Count > 0) mesh.SetTangents(tan);
            if (uv.Count > 0) mesh.SetUVs(0, uv);
            if (col.Count > 0) mesh.SetColors(col);
            mesh.boneWeights = bw.ToArray();
            for (int s = 0; s < subs.Length; s++) mesh.SetTriangles(subs[s], s);
            mesh.RecalculateBounds();
        }

        /// <summary>Deletes the meshes in the container at <paramref name="path"/> whose names end with any of <paramref name="suffixes"/>.</summary>
        static void RemoveSubs(string path, params string[] suffixes)
        {
            foreach (var m in AssetDatabase.LoadAllAssetsAtPath(path).OfType<Mesh>().ToArray())
                if (suffixes.Any(x => m.name.EndsWith(x))) Object.DestroyImmediate(m, true);
        }

        /// <summary>A copy of the mesh keeping only the triangles <paramref name="keep"/> accepts.</summary>
        static Mesh Subset(Mesh source, string name, System.Func<int, int, int, bool> keep)
        {
            var mesh = Object.Instantiate(source);
            mesh.name = name;
            for (int s = 0; s < source.subMeshCount; s++)
            {
                var tris = source.GetTriangles(s);
                var kept = new List<int>();
                for (int i = 0; i < tris.Length; i += 3)
                    if (keep(tris[i], tris[i + 1], tris[i + 2]))
                        kept.AddRange(new[] { tris[i], tris[i + 1], tris[i + 2] });
                mesh.SetTriangles(kept, s);
            }
            return mesh;
        }

        /// <summary>A second renderer on the same skeleton, for first person: no shadow (the full body casts it).</summary>
        static void Copy(SkinnedMeshRenderer full, string name, Mesh mesh)
        {
            var go = new GameObject(name);
            go.transform.SetParent(full.transform.parent, false);
            go.transform.localPosition = full.transform.localPosition;
            go.transform.localRotation = full.transform.localRotation;
            go.transform.localScale = full.transform.localScale;
            var smr = go.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = mesh;
            smr.bones = full.bones;
            smr.rootBone = full.rootBone;
            smr.sharedMaterials = full.sharedMaterials;
            smr.localBounds = full.localBounds;
            smr.updateWhenOffscreen = true;
            smr.shadowCastingMode = ShadowCastingMode.Off;
        }

        /// <summary>
        /// Writes a mesh into the container at <paramref name="path"/>, over the sub-asset of the same name if there
        /// is one, so prefabs that point at it keep pointing at it.
        /// </summary>
        static Mesh StoreSub(Mesh fresh, string path)
        {
            var container = AssetDatabase.LoadAssetAtPath<MeshContainer>(path);
            if (container == null)
            {
                container = ScriptableObject.CreateInstance<MeshContainer>();
                AssetDatabase.CreateAsset(container, path);
            }
            var existing = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Mesh>().FirstOrDefault(m => m.name == fresh.name);
            if (existing == null)
            {
                AssetDatabase.AddObjectToAsset(fresh, container);
                return fresh;
            }
            // Rebuilt through the Mesh API, not CopySerialized: a serialized copy changes the data but not what the
            // GPU holds, and a skinned renderer then refuses to draw it.
            existing.Clear();
            existing.indexFormat = fresh.indexFormat;
            existing.SetVertices(fresh.vertices);
            existing.SetNormals(fresh.normals);
            existing.SetTangents(fresh.tangents);
            existing.SetUVs(0, fresh.uv);
            existing.SetBoneWeights(fresh.GetBonesPerVertex(), fresh.GetAllBoneWeights());
            existing.bindposes = fresh.bindposes;
            existing.subMeshCount = fresh.subMeshCount;
            for (int sub = 0; sub < fresh.subMeshCount; sub++) existing.SetTriangles(fresh.GetTriangles(sub), sub);
            existing.RecalculateBounds();
            Object.DestroyImmediate(fresh);
            EditorUtility.SetDirty(existing);
            // Renderers already in the open scene keep skinning buffers sized for the old mesh (and refuse to draw if
            // the vertex count changed): re-bind them so they rebuild.
            foreach (var r in Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (r.sharedMesh != existing) continue;
                r.sharedMesh = null;
                r.sharedMesh = existing;
            }
            return existing;
        }

        /// <summary>The generated texture shrunk to PS2 size and cut to 16-bit colour, saved as a PNG.</summary>
        static Texture2D Posterized(Material source, string path)
        {
            var original = source.GetTexture("baseColorTexture") ?? source.mainTexture;
            var previous = RenderTexture.active;
            var rt = RenderTexture.GetTemporary(TextureSize, TextureSize, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(original, rt);
            RenderTexture.active = rt;
            var tex = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, TextureSize, TextureSize), 0, 0);
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);
            var pixels = tex.GetPixels32();
            int step = 256 / Levels;
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i].r = (byte)Mathf.Min(255, pixels[i].r / step * step + step / 2);
                pixels[i].g = (byte)Mathf.Min(255, pixels[i].g / step * step + step / 2);
                pixels[i].b = (byte)Mathf.Min(255, pixels[i].b / step * step + step / 2);
                pixels[i].a = 255;
            }
            tex.SetPixels32(pixels);
            System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.maxTextureSize = TextureSize;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.filterMode = FilterMode.Bilinear;
            importer.mipmapEnabled = true;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
    }
}
