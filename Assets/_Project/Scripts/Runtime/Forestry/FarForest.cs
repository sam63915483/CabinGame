using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Cabin.Forestry
{
    /// <summary>
    /// The forest beyond the real trees. Unity's terrain-tree system walks every tree within its tree distance on the
    /// CPU each frame, which with a quarter of a million trees was two thirds of the frame. So the terrain only draws
    /// real meshes close in, and this draws the rest: every tree as one camera-facing quad wearing its species'
    /// impostor (<see cref="FarForestData"/>), batched by 256 m chunk, each chunk's trees held in a GPU buffer and
    /// drawn with one call — the CPU touches chunks, never trees. Chunks out of view or beyond <see cref="FarRange"/> are skipped; quads nearer than the real-tree
    /// distance are clipped in the shader so the two never overlap. On the World scene's map.
    /// </summary>
    public sealed class FarForest : MonoBehaviour
    {
        /// <summary>Chunks are big and uncapped: RenderMeshPrimitives has no 1023 limit, and each call costs ~15 µs of main thread, so fewer calls beat finer culling (the shader clips what is too near; a quad too far is a few pixels).</summary>
        public const float ChunkSize = 512f, FarRange = 2600f;
        const int MaxPerBatch = 65536;

        [SerializeField] FarForestData data;
        [SerializeField] Material material;

        /// <summary>Nearer than this the terrain draws the real tree, so the impostor is clipped (metres).</summary>
        public static float NearDistance = 300f;
        public static bool Enabled = true;
        /// <summary>Chunk batches drawn last frame, for the F3 readout.</summary>
        public static int DrawnBatches;

        /// <summary>
        /// A chunk's trees live on the GPU: one buffer of (position, species/yaw/scales) uploaded once, drawn with
        /// RenderMeshPrimitives, so a frame costs one small call per visible chunk and no matrix copying (the old
        /// DrawMeshInstanced path copied 64 KB of matrices per chunk per frame: ~3 ms of main thread in the woods).
        /// </summary>
        sealed class Batch
        {
            public GraphicsBuffer Trees;
            public int Count;
            public Bounds Bounds;
            public MaterialPropertyBlock Block;
        }

        /// <summary>What the shader reads per tree: position, then species, yaw (radians), width scale, height scale.</summary>
        struct Tree
        {
            public Vector4 Position, Instance;
        }

        readonly List<Batch> batches = new List<Batch>();
        Mesh quad;
        readonly Plane[] planes = new Plane[6];
        static readonly int TreesId = Shader.PropertyToID("_Trees"), NearId = Shader.PropertyToID("_Near");

        public void Set(FarForestData forestData, Material forestMaterial)
        {
            data = forestData;
            material = forestMaterial;
        }

        void Start()
        {
            if (data == null || material == null || data.Atlas == null) return;
            quad = BuildQuad();
            material.SetTexture("_Atlas", data.Atlas);
            material.SetFloat("_Views", data.Views);
            material.SetFloat("_Columns", data.Columns);
            material.SetFloat("_Rows", data.Rows);
            var widths = new float[16];
            var heights = new float[16];
            for (int i = 0; i < Mathf.Min(16, data.Species); i++) { widths[i] = data.Widths[i]; heights[i] = data.Heights[i]; }
            material.SetFloatArray("_Width", widths);
            material.SetFloatArray("_Height", heights);
            foreach (var terrain in GetComponentsInChildren<Terrain>()) Gather(terrain);
        }

        /// <summary>Groups a tile's trees into chunk batches (position, species, yaw, scale), each with its bounds.</summary>
        void Gather(Terrain terrain)
        {
            var td = terrain.terrainData;
            var instances = td.treeInstances;
            var protos = td.treePrototypes;
            var origin = terrain.transform.position;
            var size = td.size;
            int chunksAcross = Mathf.CeilToInt(size.x / ChunkSize);
            var byChunk = new Dictionary<int, List<(Vector3 p, Vector4 i)>>();
            foreach (var t in instances)
            {
                if (t.prototypeIndex >= data.Species || protos[t.prototypeIndex].prefab == null || protos[t.prototypeIndex].prefab.GetComponent<ChoppableTree>() == null) continue;
                var p = origin + Vector3.Scale(t.position, size);
                int key = Mathf.FloorToInt((p.z - origin.z) / ChunkSize) * chunksAcross + Mathf.FloorToInt((p.x - origin.x) / ChunkSize);
                if (!byChunk.TryGetValue(key, out var list)) byChunk[key] = list = new List<(Vector3, Vector4)>();
                list.Add((p, new Vector4(t.prototypeIndex, t.rotation, t.widthScale, t.heightScale)));
            }
            foreach (var list in byChunk.Values)
                for (int start = 0; start < list.Count; start += MaxPerBatch)
                {
                    int n = Mathf.Min(MaxPerBatch, list.Count - start);
                    var trees = new Tree[n];
                    var bounds = new Bounds(list[start].p, Vector3.zero);
                    for (int i = 0; i < n; i++)
                    {
                        var (p, inst) = list[start + i];
                        trees[i] = new Tree { Position = new Vector4(p.x, p.y, p.z, 0f), Instance = inst };
                        float h = data.Heights[(int)inst.x] * inst.w, w = data.Widths[(int)inst.x] * inst.z;
                        bounds.Encapsulate(new Bounds(p + Vector3.up * h * 0.5f, new Vector3(w, h, w)));
                    }
                    var b = new Batch { Count = n, Bounds = bounds, Block = new MaterialPropertyBlock(), Trees = new GraphicsBuffer(GraphicsBuffer.Target.Structured, n, 32) };
                    b.Trees.SetData(trees);
                    b.Block.SetBuffer(TreesId, b.Trees);
                    batches.Add(b);
                }
        }

        void OnDestroy()
        {
            foreach (var b in batches) b.Trees?.Release();
            batches.Clear();
        }

        void LateUpdate()
        {
            DrawnBatches = 0;
            if (!Enabled || quad == null || batches.Count == 0) return;
            var cam = Camera.main;
            if (cam == null) return;
            GeometryUtility.CalculateFrustumPlanes(cam, planes);
            material.SetFloat(NearId, NearDistance - 15f);
            Vector3 eye = cam.transform.position;
            float far2 = FarRange * FarRange;
            var rp = new RenderParams(material) { camera = cam, shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false, layer = 0 };
            foreach (var b in batches)
            {
                if (b.Bounds.SqrDistance(eye) > far2) continue;
                // A chunk whose far corner is still inside the real-tree distance has nothing to show.
                var c = b.Bounds.center;
                float reach = b.Bounds.extents.x + b.Bounds.extents.z;
                var flat = new Vector3(c.x - eye.x, 0f, c.z - eye.z);
                if (flat.magnitude + reach < NearDistance - 15f) continue;
                if (!GeometryUtility.TestPlanesAABB(planes, b.Bounds)) continue;
                DrawnBatches++;
                rp.worldBounds = b.Bounds;
                rp.matProps = b.Block;
                Graphics.RenderMeshPrimitives(rp, quad, 0, b.Count);
            }
        }

        /// <summary>A unit quad standing on its base, the shader sizes and turns it.</summary>
        static Mesh BuildQuad()
        {
            var m = new Mesh { name = "Impostor quad" };
            m.vertices = new[] { new Vector3(-0.5f, 0f, 0f), new Vector3(0.5f, 0f, 0f), new Vector3(0.5f, 1f, 0f), new Vector3(-0.5f, 1f, 0f) };
            m.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
            m.triangles = new[] { 0, 2, 1, 0, 3, 2, 0, 1, 2, 0, 2, 3 };
            m.bounds = new Bounds(Vector3.up * 10f, new Vector3(40f, 40f, 40f));
            return m;
        }

        /// <summary>Which batches would be drawn from a point: for tests and the stats.</summary>
        public int BatchCount => batches.Count;
    }
}
