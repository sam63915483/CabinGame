using System.Collections.Generic;
using System.Linq;
using Cabin.Core;
using Cabin.Forestry;
using UnityEditor;
using UnityEngine;

namespace Cabin.Editor.Map
{
    /// <summary>
    /// The trees the map uses — the ones Sam picked for Sound of Space (docs/ASSETS_FROM_SOS.md) — as terrain-tree
    /// prefabs: a variant of each pack prefab with its LODs retuned to carry far, and no collider (TreeColliders handles
    /// trunks round the player), carrying a <see cref="ChoppableTree"/> for the axe (a readable copy of the
    /// full-detail mesh in Generated/Trees, the trunk's radius up its first metres, the bark colour). Generated into
    /// Prefabs/Trees; the pack prefabs aren't touched.
    /// </summary>
    public static class TreeLibrary
    {
        const string FF = "Assets/External/FantasyForest/Prefabs/Vegetation/";
        const string FV = "Assets/External/FantasyValley/Prefabs/Vegetation/";
        public const string Dir = "Assets/_Project/Prefabs/Trees";
        const string MeshPath = "Assets/Generated/Trees/ChopMeshes.asset";
        /// <summary>How far up the trunk radius is measured (metres, unscaled).</summary>
        const float RadiusUpTo = 3f;

        /// <summary>Weights within each kind follow the audit's ranking for a Nova Scotia forest.</summary>
        public static readonly TreeSpecies[] All =
        {
            new TreeSpecies(FF + "Tree_05.prefab", TreeKind.Conifer, 5f),
            new TreeSpecies(FF + "Tree_04.prefab", TreeKind.Conifer, 4f),
            new TreeSpecies(FF + "Tree_07.prefab", TreeKind.Conifer, 3.5f),
            new TreeSpecies(FF + "Tree_01.prefab", TreeKind.Conifer, 3f),
            new TreeSpecies(FF + "Tree_02.prefab", TreeKind.Conifer, 3f),
            new TreeSpecies(FF + "Tree_03.prefab", TreeKind.Conifer, 2f),
            new TreeSpecies(FF + "Tree_06.prefab", TreeKind.Conifer, 1f),
            new TreeSpecies(FV + "Tree_06.prefab", TreeKind.Broadleaf, 3f),
            new TreeSpecies(FV + "Tree_01.prefab", TreeKind.Broadleaf, 2.5f),
            new TreeSpecies(FV + "Tree_04.prefab", TreeKind.Broadleaf, 2f),
            new TreeSpecies(FV + "Tree_05.prefab", TreeKind.Broadleaf, 2f),
            new TreeSpecies(FV + "Tree_02.prefab", TreeKind.Broadleaf, 1.5f),
            new TreeSpecies(FV + "Tree_03.prefab", TreeKind.Broadleaf, 0.3f),
            new TreeSpecies(FV + "Tree_08.prefab", TreeKind.Birch, 3f),
            new TreeSpecies(FV + "Tree_07.prefab", TreeKind.Birch, 2f),
            new TreeSpecies(FF + "Tree_08.prefab", TreeKind.Snag, 1f),
        };

        public static string PrefabPath(TreeSpecies s) => $"{Dir}/{Prefix(s.Source)}_{System.IO.Path.GetFileNameWithoutExtension(s.Source)}.prefab";

        static string Prefix(string source) => source.Contains("/FantasyForest/") ? "FF" : "FV";

        /// <summary>Builds (or rebuilds) the tree prefabs; returns them in <see cref="All"/>'s order.</summary>
        public static GameObject[] Build()
        {
            SandboxAssetGenerator.EnsureFolder(Dir);
            SandboxAssetGenerator.EnsureFolder("Assets/Generated/Trees");
            if (AssetDatabase.LoadMainAssetAtPath(MeshPath) != null) AssetDatabase.DeleteAsset(MeshPath);
            var container = ScriptableObject.CreateInstance<MeshContainer>();
            AssetDatabase.CreateAsset(container, MeshPath);
            var result = new List<GameObject>();
            foreach (var s in All)
            {
                var source = AssetDatabase.LoadAssetAtPath<GameObject>(s.Source);
                var go = (GameObject)PrefabUtility.InstantiatePrefab(source);
                try
                {
                    // Unity switches LODs by screen height, so at one setting a 4.7 m tree switched at 16 m and a 13 m one
                    // at 45 m (Sam: "some within 15 m, some 30–40"). Each species gets thresholds from its own size so
                    // every tree switches at the same distances, cross-faded over the metres before each switch.
                    var lodGroup = go.GetComponent<LODGroup>();
                    if (lodGroup != null)
                    {
                        var lods = lodGroup.GetLODs();
                        Thresholds(lodGroup.size, lods.Length, out var heights, out var widths);
                        for (int k = 0; k < lods.Length; k++)
                        {
                            lods[k].screenRelativeTransitionHeight = heights[k];
                            lods[k].fadeTransitionWidth = widths[k];
                        }
                        lodGroup.SetLODs(lods);
                        lodGroup.fadeMode = LODFadeMode.CrossFade;
                        lodGroup.animateCrossFading = false;
                    }
                    // No collider on the prefab: a quarter of a million terrain-tree colliders cost seconds at load and a lot of
                    // memory; TreeColliders puts capsules on the trunks round the player instead.

                    var lod0 = go.GetComponentsInChildren<MeshFilter>().OrderByDescending(f => f.sharedMesh != null ? f.sharedMesh.vertexCount : 0).First();
                    var copy = Readable(lod0.sharedMesh, go.transform.worldToLocalMatrix * lod0.transform.localToWorldMatrix);
                    copy.name = $"{Prefix(s.Source)}_{source.name}";
                    AssetDatabase.AddObjectToAsset(copy, container);
                    var mats = lod0.GetComponent<MeshRenderer>().sharedMaterials;
                    go.AddComponent<ChoppableTree>().Set(copy, mats, TrunkRadii(copy), BarkColor(copy, mats), copy.bounds.max.y);
                    result.Add(PrefabUtility.SaveAsPrefabAsset(go, PrefabPath(s)));
                }
                finally
                {
                    Object.DestroyImmediate(go);
                }
            }
            return result.ToArray();
        }

        /// <summary>Where the LODs switch (metres) and over how many metres each fades out before its switch, at the default 72° lens.</summary>
        public const float FullTo = 45f, MiddleTo = 140f, FadeMetres = 13f, FadeMetresFar = 25f;
        const float Fov = 72f;

        /// <summary>The screen height (0–1) an object of <paramref name="size"/> covers at <paramref name="distance"/> metres.</summary>
        public static float ScreenHeight(float size, float distance) => size / (distance * 2f * Mathf.Tan(Fov * 0.5f * Mathf.Deg2Rad));

        /// <summary>
        /// Per-LOD screen-height thresholds and fade widths (fractions of each LOD's own band) that put a species'
        /// switches at <see cref="FullTo"/> and <see cref="MiddleTo"/> metres whatever its size, the last LOD never
        /// culled (the near forest decides how far trees stand).
        /// </summary>
        public static void Thresholds(float size, int lodCount, out float[] heights, out float[] widths)
        {
            heights = new float[lodCount];
            widths = new float[lodCount];
            float full = ScreenHeight(size, FullTo), middle = ScreenHeight(size, MiddleTo);
            for (int k = 0; k < lodCount; k++)
            {
                float top = k == 0 ? 1f : heights[k - 1];
                heights[k] = k == 0 ? full : k == 1 ? middle : 0.001f;
                if (lodCount == 1) heights[k] = 0.001f;
                float fadeStart = k == 0 ? ScreenHeight(size, FullTo - FadeMetres) : k == 1 ? ScreenHeight(size, MiddleTo - FadeMetresFar) : heights[k];
                widths[k] = Mathf.Clamp01((fadeStart - heights[k]) / Mathf.Max(1e-5f, top - heights[k]));
            }
        }

        /// <summary>
        /// A readable copy of a mesh (imported ones aren't, and the axe cuts it up at run time), moved into tree space.
        /// </summary>
        static Mesh Readable(Mesh source, Matrix4x4 toTree)
        {
            var mesh = new Mesh { indexFormat = source.indexFormat };
            var v = source.vertices;
            for (int i = 0; i < v.Length; i++) v[i] = toTree.MultiplyPoint3x4(v[i]);
            mesh.vertices = v;
            var n = source.normals;
            for (int i = 0; i < n.Length; i++) n[i] = toTree.MultiplyVector(n[i]).normalized;
            if (n.Length == v.Length) mesh.normals = n;
            if (source.tangents.Length == v.Length) mesh.tangents = source.tangents;
            var uv = new List<Vector4>();
            source.GetUVs(0, uv);
            if (uv.Count == v.Length) mesh.SetUVs(0, uv);
            uv.Clear();
            source.GetUVs(1, uv);
            if (uv.Count == v.Length) mesh.SetUVs(1, uv);
            if (source.colors.Length == v.Length) mesh.colors = source.colors;
            mesh.subMeshCount = source.subMeshCount;
            for (int s = 0; s < source.subMeshCount; s++) mesh.SetTriangles(source.GetTriangles(s), s);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// The trunk's radius every <see cref="ChoppableTree.RadiusStep"/> up: the mesh cut through at that height and
        /// the trunk's outline measured the way the axe will (<see cref="TrunkSection"/>, which leaves branches out),
        /// averaged round it, smoothed and never growing upwards.
        /// </summary>
        static float[] TrunkRadii(Mesh mesh)
        {
            int n = Mathf.RoundToInt(RadiusUpTo / ChoppableTree.RadiusStep) + 1;
            var radii = new float[n];
            float last = 0.25f;
            var outline = new List<MeshSlicer.EdgePoint>();
            for (int k = 0; k < n; k++)
            {
                // Not right at the ground, where the mesh may end.
                float y = Mathf.Max(0.05f, k * ChoppableTree.RadiusStep);
                outline.Clear();
                MeshSlicer.Outline(mesh, y, outline);
                float best = 0f;
                if (outline.Count >= 3)
                {
                    var section = TrunkSection.From(outline, y, last);
                    best = section.Radius.Average();
                }
                radii[k] = best > 0.02f && best < 1.2f ? best : last;
                last = radii[k];
            }
            // A trunk never thickens going up: a bigger reading is a whorl of branches, so it's clipped (before smoothing,
            // so it can't spread down the trunk).
            for (int k = 1; k < n; k++) radii[k] = Mathf.Min(radii[k], radii[k - 1]);
            for (int k = 1; k < n - 1; k++) radii[k] = (radii[k - 1] + radii[k] * 2f + radii[k + 1]) * 0.25f;
            return radii;
        }

        /// <summary>The colour of the bark: the texture under the trunk's lowest vertices, averaged.</summary>
        static Color BarkColor(Mesh mesh, Material[] mats)
        {
            var tex = mats.SelectMany(m => m.GetTexturePropertyNames().Select(m.GetTexture)).OfType<Texture2D>().FirstOrDefault(t => !t.name.ToLower().Contains("normal"));
            var v = mesh.vertices;
            var uv = mesh.uv;
            if (tex == null || uv.Length != v.Length) return new Color(0.42f, 0.32f, 0.24f);
            var rt = RenderTexture.GetTemporary(256, 256, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var prev = RenderTexture.active;
            Graphics.Blit(tex, rt);
            RenderTexture.active = rt;
            var copy = new Texture2D(256, 256, TextureFormat.RGBA32, false);
            copy.ReadPixels(new Rect(0, 0, 256, 256), 0, 0);
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            Color sum = Color.clear;
            int count = 0;
            for (int i = 0; i < v.Length; i++)
            {
                if (v[i].y > 0.8f || new Vector2(v[i].x, v[i].z).magnitude > 0.9f) continue;
                sum += copy.GetPixelBilinear(uv[i].x, uv[i].y);
                count++;
            }
            Object.DestroyImmediate(copy);
            if (count == 0) return new Color(0.42f, 0.32f, 0.24f);
            var c = sum / count;
            c.a = 1f;
            return c;
        }

        public static IEnumerable<int> OfKind(TreeKind kind) => Enumerable.Range(0, All.Length).Where(i => All[i].Kind == kind);
    }
}
