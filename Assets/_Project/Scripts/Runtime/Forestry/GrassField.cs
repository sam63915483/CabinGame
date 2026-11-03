using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Cabin.Forestry
{
    /// <summary>
    /// Draws the grass itself, the way Sound of Space did, instead of leaving it to the terrain's detail system. The
    /// terrain draws every tuft out to the grass distance at full mesh, which is what the laptop's GPU was spending
    /// its frame on. Here the placed grass (the tiles' detail maps, read as needed) is built into 16 m chunks around
    /// the player and drawn instanced: a chunk out of view is skipped, a chunk past <see cref="Distance"/> is
    /// dropped, a far chunk wears the light mesh, and from <see cref="FadeStart"/> of the distance outward fewer of a
    /// chunk's tufts are drawn (its tufts are shuffled, so the first n are a fair sample). <see cref="Density"/>
    /// thins the same way, so the setting costs nothing. Each tuft is tinted by the ground it stands on (the tile's
    /// paint blended through <see cref="layerTints"/>, one colour per terrain layer: golden on meadow, dark on the
    /// forest floor) so the grass never glows over brown earth, and sized by how full the patch around it is
    /// (<see cref="Fullness"/>): deep in a meadow full size, at a patch's fringe or standing alone shrinking toward
    /// <see cref="fringeSize"/>, so patches thin and dwindle at their edges instead of ending in a wall. The terrain's
    /// own detail distance is set to zero. On the World scene's map.
    /// </summary>
    public sealed class GrassField : MonoBehaviour
    {
        /// <summary>Detail cells per chunk edge: 16 cells of 2 m. Bigger chunks mean fewer draw calls (each ~15 µs of main thread); a species with more than 1023 tufts in a chunk is drawn in slices.</summary>
        public const int ChunkCells = 16;
        /// <summary>Thinning starts at this fraction of the grass distance and reaches <see cref="FadeFloor"/> at the edge: nothing, so a chunk arriving at the edge grows in rather than popping. Tunable for the bench.</summary>
        public static float FadeStart = 0.5f, FadeFloor = 0f;
        /// <summary>Chunks nearer than this wear the detailed mesh; beyond it the light one (metres).</summary>
        public static float NearMesh = 30f;
        /// <summary>Tuft sizes, as the detail prototypes had them (metres).</summary>
        public const float MinWidth = 1.2f, MaxWidth = 1.9f, MinHeight = 1f, MaxHeight = 1.6f;
        const int BuildPerFrame = 2, MaxPerDraw = 1023;
        const float Margin = 20f, RescanStep = 4f;

        /// <summary>How far grass draws (metres) and how much of it (0–1): the settings.</summary>
        public static float Distance = 80f, Density = 1f;
        public static bool Enabled = true;
        /// <summary>What the last frame sent to the GPU, for the F3 readout.</summary>
        public static int DrawnTufts, DrawnCalls;

        [SerializeField] Mesh[] near, far;
        [SerializeField] Material[] materials;
        [SerializeField] Color[] layerTints;
        [SerializeField] float fringeSize = 0.4f;
        [SerializeField] float slopeFollow = 1f;
        [SerializeField] int fullCell = 10, fullnessWindow = 2;
        static MaterialPropertyBlock block; // made on first use: Unity forbids it in a MonoBehaviour's field initialisers
        static readonly int TintId = Shader.PropertyToID("_Tint");

        sealed class Tile
        {
            public Terrain Terrain;
            public TerrainData Data;
            public byte[][] Layers;
            public int Cells, ChunksAcross;
            public float CellMetres;
        }

        sealed class Chunk
        {
            public long Key;
            public Vector3 Centre;
            public float HalfReach;
            public Bounds Bounds;
            public Matrix4x4[][] Matrices;
            public Vector4[][] Tints;
            public int[] Counts;
            public int Total;
        }

        readonly List<Tile> tiles = new List<Tile>();
        readonly Dictionary<long, Chunk> active = new Dictionary<long, Chunk>();
        readonly List<(long key, Tile tile, int cx, int cz, float d2)> wanted = new List<(long, Tile, int, int, float)>();
        readonly List<long> dropping = new List<long>();
        readonly Plane[] planes = new Plane[6];
        readonly List<Matrix4x4>[] scratch = { new List<Matrix4x4>(), new List<Matrix4x4>(), new List<Matrix4x4>(), new List<Matrix4x4>() };
        readonly List<Vector4>[] scratchTints = { new List<Vector4>(), new List<Vector4>(), new List<Vector4>(), new List<Vector4>() };
        Vector3 lastEye = new Vector3(float.NaN, 0f, 0f);
        float lastDistance = -1f;
        bool started;

        /// <param name="tints">One tint per terrain layer, in the painter's layer order (white keeps the tuft's own colour).</param>
        /// <param name="fringe">How small a tuft gets (of full size) where the patch around it is empty.</param>
        /// <param name="fullPerCell">Tufts per cell that count as a full patch.</param>
        /// <param name="window">Cells each way that a tuft looks at to judge its patch's fullness.</param>
        /// <param name="follow">How far a tuft leans to stand square on the ground (0 straight up, 1 along the ground's normal).</param>
        public void Set(Mesh[] nearMeshes, Mesh[] farMeshes, Material[] grassMaterials, Color[] tints, float fringe, int fullPerCell, int window, float follow)
        {
            slopeFollow = follow;
            near = nearMeshes;
            far = farMeshes;
            materials = grassMaterials;
            layerTints = tints;
            fringeSize = fringe;
            fullCell = fullPerCell;
            fullnessWindow = window;
        }

        /// <summary>
        /// How full the grass is round a cell (0–1): every species' tufts in the cells within <paramref name="window"/>
        /// of it (inside the tile), against <paramref name="fullPerCell"/> tufts in each of them.
        /// </summary>
        public static float Fullness(byte[][] layers, int cells, int x, int y, int window, int fullPerCell)
        {
            int sum = 0, counted = 0;
            for (int dy = -window; dy <= window; dy++)
            for (int dx = -window; dx <= window; dx++)
            {
                int cx = x + dx, cy = y + dy;
                if (cx < 0 || cy < 0 || cx >= cells || cy >= cells) continue;
                counted++;
                for (int k = 0; k < layers.Length; k++) sum += layers[k][cy * cells + cx];
            }
            if (counted == 0 || fullPerCell <= 0) return 0f;
            return Mathf.Clamp01(sum / (float)(counted * fullPerCell));
        }

        /// <summary>A tuft's rotation: turned by its yaw, then leant from straight up toward the ground's normal by <paramref name="follow"/>, so on a slope it stands square on the ground rather than poking out of it.</summary>
        public static Quaternion Lean(Vector3 groundNormal, float yaw, float follow)
        {
            var upright = Quaternion.Euler(0f, yaw, 0f);
            if (follow <= 0f || groundNormal.sqrMagnitude < 1e-6f) return upright;
            var tilt = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(Vector3.up, groundNormal.normalized), Mathf.Clamp01(follow));
            return tilt * upright;
        }

        /// <summary>A tuft's size (of full) for the fullness round it: <paramref name="fringe"/> where the patch is empty, full from well inside it.</summary>
        public static float SizeScale(float fullness, float fringe)
        {
            return Mathf.Lerp(fringe, 1f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.1f, 0.8f, fullness)));
        }

        /// <summary>A tuft's tint from the paint weights at its cell: the layers' tints blended by weight (white where nothing is painted).</summary>
        public static Vector4 Tint(float[,,] splat, int y, int x, Color[] tints)
        {
            if (tints == null || tints.Length == 0) return Vector4.one;
            int layers = Mathf.Min(splat.GetLength(2), tints.Length);
            Vector4 sum = Vector4.zero;
            float total = 0f;
            for (int k = 0; k < layers; k++)
            {
                float w = splat[y, x, k];
                if (w <= 0f) continue;
                sum += (Vector4)tints[k] * w;
                total += w;
            }
            if (total <= 1e-4f) return Vector4.one;
            sum /= total;
            sum.w = 1f;
            return sum;
        }

        public int ActiveChunks => active.Count;
        /// <summary>Chunks still to build round the player: zero once the grass has streamed in.</summary>
        public int Pending => wanted.Count;

        void Update()
        {
            if (!started)
            {
                started = true;
                foreach (var t in GetComponentsInChildren<Terrain>())
                {
                    t.detailObjectDistance = 0f; // this draws the grass, not the terrain
                    var d = t.terrainData;
                    tiles.Add(new Tile { Terrain = t, Data = d, Cells = d.detailWidth, ChunksAcross = Mathf.CeilToInt(d.detailWidth / (float)ChunkCells), CellMetres = d.size.x / d.detailWidth });
                }
            }
            if (!Enabled || near == null || near.Length == 0) return;
            var cam = Camera.main;
            if (cam == null) return;
            Vector3 eye = cam.transform.position;
            if (float.IsNaN(lastEye.x) || (eye - lastEye).sqrMagnitude >= RescanStep * RescanStep || !Mathf.Approximately(lastDistance, Distance))
            {
                lastEye = eye;
                lastDistance = Distance;
                Rescan(eye);
            }
            for (int i = 0; i < BuildPerFrame && wanted.Count > 0; i++)
            {
                var w = wanted[wanted.Count - 1];
                wanted.RemoveAt(wanted.Count - 1);
                if (!active.ContainsKey(w.key)) active[w.key] = Build(w.tile, w.cx, w.cz, w.key);
            }
        }

        /// <summary>Which chunks should be live for this eye: new ones queued nearest-last (so popped first), far ones dropped.</summary>
        void Rescan(Vector3 eye)
        {
            float reach = Distance + Margin;
            dropping.Clear();
            foreach (var kv in active)
                if (Flat(kv.Value.Centre, eye) > reach + kv.Value.HalfReach) dropping.Add(kv.Key);
            foreach (var k in dropping) active.Remove(k);
            wanted.Clear();
            for (int ti = 0; ti < tiles.Count; ti++)
            {
                var tile = tiles[ti];
                Vector3 o = tile.Terrain.transform.position;
                float chunkMetres = ChunkCells * tile.CellMetres;
                int x0 = Mathf.Max(0, Mathf.FloorToInt((eye.x - reach - o.x) / chunkMetres)), x1 = Mathf.Min(tile.ChunksAcross - 1, Mathf.FloorToInt((eye.x + reach - o.x) / chunkMetres));
                int z0 = Mathf.Max(0, Mathf.FloorToInt((eye.z - reach - o.z) / chunkMetres)), z1 = Mathf.Min(tile.ChunksAcross - 1, Mathf.FloorToInt((eye.z + reach - o.z) / chunkMetres));
                for (int cz = z0; cz <= z1; cz++)
                for (int cx = x0; cx <= x1; cx++)
                {
                    float wx = o.x + (cx + 0.5f) * chunkMetres, wz = o.z + (cz + 0.5f) * chunkMetres;
                    float d2 = (wx - eye.x) * (wx - eye.x) + (wz - eye.z) * (wz - eye.z);
                    if (d2 > reach * reach) continue;
                    long key = Key(ti, cx, cz);
                    if (!active.ContainsKey(key)) wanted.Add((key, tile, cx, cz, d2));
                }
            }
            wanted.Sort((a, b) => b.d2.CompareTo(a.d2));
        }

        Chunk Build(Tile tile, int cx, int cz, long key)
        {
            if (tile.Layers == null)
            {
                var d = tile.Data;
                tile.Layers = new byte[d.detailPrototypes.Length][];
                for (int k = 0; k < tile.Layers.Length; k++)
                {
                    var map = d.GetDetailLayer(0, 0, d.detailWidth, d.detailHeight, k);
                    var bytes = new byte[d.detailWidth * d.detailHeight];
                    for (int y = 0; y < d.detailHeight; y++)
                    for (int x = 0; x < d.detailWidth; x++)
                        bytes[y * d.detailWidth + x] = (byte)Mathf.Min(255, map[y, x]);
                    tile.Layers[k] = bytes;
                }
            }
            int species = Mathf.Min(tile.Layers.Length, near.Length);
            for (int k = 0; k < species; k++) { scratch[k].Clear(); scratchTints[k].Clear(); }
            Vector3 o = tile.Terrain.transform.position;
            Vector3 size = tile.Data.size;
            float cell = tile.CellMetres;
            float minY = float.MaxValue, maxY = float.MinValue;
            int tileSeed = tile.Terrain.name.GetHashCode();
            int y0 = cz * ChunkCells, y1 = Mathf.Min(tile.Cells, (cz + 1) * ChunkCells);
            int x0 = cx * ChunkCells, x1 = Mathf.Min(tile.Cells, (cx + 1) * ChunkCells);
            // The paint under this chunk, for the tints (the alphamap may be coarser or finer than the detail cells).
            int aw = tile.Data.alphamapWidth, ah = tile.Data.alphamapHeight;
            int ax0 = x0 * aw / tile.Cells, ax1 = Mathf.Min(aw - 1, (x1 - 1) * aw / tile.Cells);
            int ay0 = y0 * ah / tile.Cells, ay1 = Mathf.Min(ah - 1, (y1 - 1) * ah / tile.Cells);
            var splat = tile.Data.GetAlphamaps(ax0, ay0, ax1 - ax0 + 1, ay1 - ay0 + 1);
            for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++)
            {
                var tint = Tint(splat, Mathf.Min(ay1, y * ah / tile.Cells) - ay0, Mathf.Min(ax1, x * aw / tile.Cells) - ax0, layerTints);
                float sizeScale = SizeScale(Fullness(tile.Layers, tile.Cells, x, y, fullnessWindow, fullCell), fringeSize);
                for (int k = 0; k < species; k++)
                {
                    int n = tile.Layers[k][y * tile.Cells + x];
                    for (int i = 0; i < n; i++)
                    {
                        Tuft(tileSeed, x, y, k, i, out var at, out float yaw, out float w, out float h);
                        w *= sizeScale;
                        h *= sizeScale;
                        float wx = o.x + (x + at.x) * cell, wz = o.z + (y + at.y) * cell;
                        float u = (wx - o.x) / size.x, v = (wz - o.z) / size.z;
                        float wy = o.y + tile.Data.GetInterpolatedHeight(u, v);
                        if (wy < minY) minY = wy;
                        if (wy > maxY) maxY = wy;
                        var lean = Lean(tile.Data.GetInterpolatedNormal(u, v), yaw, slopeFollow);
                        scratch[k].Add(Matrix4x4.TRS(new Vector3(wx, wy, wz), lean, new Vector3(w, h, w)));
                        scratchTints[k].Add(tint);
                    }
                }
            }
            var c = new Chunk { Key = key, Matrices = new Matrix4x4[species][], Tints = new Vector4[species][], Counts = new int[species] };
            for (int k = 0; k < species; k++)
            {
                var list = scratch[k];
                Shuffle(list, scratchTints[k], tileSeed ^ (cx * 73856093) ^ (cz * 19349663) ^ k);
                c.Matrices[k] = list.ToArray();
                c.Tints[k] = scratchTints[k].ToArray();
                c.Counts[k] = list.Count;
                c.Total += list.Count;
            }
            float chunkMetres = ChunkCells * cell;
            c.Centre = new Vector3(o.x + (cx + 0.5f) * chunkMetres, 0f, o.z + (cz + 0.5f) * chunkMetres);
            c.HalfReach = chunkMetres * 0.75f;
            if (c.Total == 0) { minY = o.y; maxY = o.y; }
            c.Bounds = new Bounds(new Vector3(c.Centre.x, (minY + maxY + MaxHeight * 1.6f) * 0.5f, c.Centre.z), new Vector3(chunkMetres + MaxWidth, maxY - minY + MaxHeight * 1.6f, chunkMetres + MaxWidth));
            return c;
        }

        void LateUpdate()
        {
            DrawnTufts = 0;
            DrawnCalls = 0;
            if (!Enabled || active.Count == 0 || materials == null) return;
            if (block == null) block = new MaterialPropertyBlock();
            var cam = Camera.main;
            if (cam == null) return;
            GeometryUtility.CalculateFrustumPlanes(cam, planes);
            Vector3 eye = cam.transform.position;
            foreach (var c in active.Values)
            {
                if (c.Total == 0) continue;
                float d = Mathf.Max(0f, Flat(c.Centre, eye) - c.HalfReach); // to the chunk's near side, roughly
                if (d > Distance) continue;
                if (!GeometryUtility.TestPlanesAABB(planes, c.Bounds)) continue;
                var meshes = d < NearMesh ? near : far;
                float fraction = Fade(d, Distance) * Density;
                for (int k = 0; k < c.Counts.Length; k++)
                {
                    int count = Count(c.Counts[k], fraction);
                    for (int start = 0; start < count; start += MaxPerDraw)
                    {
                        int n = Mathf.Min(MaxPerDraw, count - start);
                        DrawnTufts += n;
                        DrawnCalls++;
                        block.Clear();
                        if (start == 0)
                        {
                            block.SetVectorArray(TintId, c.Tints[k]);
                            Graphics.DrawMeshInstanced(meshes[k], 0, materials[k], c.Matrices[k], n, block, ShadowCastingMode.Off, true, 0, cam);
                        }
                        else
                        {
                            var slice = new Matrix4x4[n];
                            var tints = new Vector4[n];
                            System.Array.Copy(c.Matrices[k], start, slice, 0, n);
                            System.Array.Copy(c.Tints[k], start, tints, 0, n);
                            block.SetVectorArray(TintId, tints);
                            Graphics.DrawMeshInstanced(meshes[k], 0, materials[k], slice, n, block, ShadowCastingMode.Off, true, 0, cam);
                        }
                    }
                }
            }
        }

        static float Flat(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        public static long Key(int tile, int cx, int cz) => ((long)tile << 40) | ((long)cz << 20) | (uint)cx;

        /// <summary>How much of a chunk's grass is drawn at a distance: all of it until <see cref="FadeStart"/>, thinning to <see cref="FadeFloor"/> at the edge.</summary>
        public static float Fade(float distance, float range)
        {
            if (range <= 0f) return 0f;
            float t = Mathf.InverseLerp(FadeStart * range, range, distance);
            return Mathf.Lerp(1f, FadeFloor, t);
        }

        /// <summary>Tufts drawn of n at a fraction: rounded, never none where there was grass and the fraction is above zero.</summary>
        public static int Count(int n, float fraction)
        {
            if (n == 0 || fraction <= 0f) return 0;
            return Mathf.Clamp(Mathf.RoundToInt(n * fraction), 1, n);
        }

        /// <summary>Where the i-th tuft of a species sits in its cell (0–1 across), how it is turned (degrees) and its width and height: the same every time.</summary>
        public static void Tuft(int tileSeed, int x, int y, int species, int i, out Vector2 at, out float yaw, out float width, out float height)
        {
            uint h = Hash((uint)(tileSeed * 31 + x * 1973 + y * 9277 + species * 26699 + i * 92821));
            at = new Vector2(Unit(h), Unit(Hash(h ^ 0x9E3779B9u)));
            uint h2 = Hash(h + 0x7F4A7C15u);
            yaw = Unit(h2) * 360f;
            uint h3 = Hash(h2 + 0x165667B1u);
            width = Mathf.Lerp(MinWidth, MaxWidth, Unit(h3));
            height = Mathf.Lerp(MinHeight, MaxHeight, Unit(Hash(h3 ^ 0xD3A2646Cu)));
        }

        static uint Hash(uint x)
        {
            x ^= x >> 16;
            x *= 0x7FEB352Du;
            x ^= x >> 15;
            x *= 0x846CA68Bu;
            x ^= x >> 16;
            return x;
        }

        static float Unit(uint h) => (h & 0xFFFFFFu) / 16777216f;

        static void Shuffle(List<Matrix4x4> list, List<Vector4> along, int seed)
        {
            uint s = (uint)seed;
            for (int i = list.Count - 1; i > 0; i--)
            {
                s = Hash(s + 0x9E3779B9u);
                int j = (int)(s % (uint)(i + 1));
                (list[i], list[j]) = (list[j], list[i]);
                (along[i], along[j]) = (along[j], along[i]);
            }
        }
    }
}
