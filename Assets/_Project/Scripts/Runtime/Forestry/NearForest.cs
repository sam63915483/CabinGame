using System.Collections.Generic;
using UnityEngine;

namespace Cabin.Forestry
{
    /// <summary>
    /// The real trees round the player, as ordinary renderers instead of terrain trees. Unity's terrain tree renderer
    /// costs the main thread and the GPU per tree it holds (a third of the frame in the woods); plain renderers go
    /// through the GPU Resident Drawer, which instances them and culls them on the GPU, occluded ones included.
    /// The terrain keeps its full tree lists (the axe and the colliders read them) but draws none; this streams
    /// pooled instances of each species' prefab for the trees within <see cref="Radius"/> of the camera, by 64 m
    /// cell, nearest first, a budget per frame. A chopped tree leaves (the <see cref="Forest"/> says where).
    /// <see cref="FarForest"/> draws the impostors beyond. On the World scene's map, beside the forest.
    /// </summary>
    [RequireComponent(typeof(Forest))]
    public sealed class NearForest : MonoBehaviour
    {
        public const float CellSize = 64f, MoveStep = 8f, Margin = 24f;
        /// <summary>How far real trees stand: the tree-distance setting plus a little, so the far forest's clip never shows bare ground.</summary>
        public static float Radius = 320f;
        /// <summary>Instances alive right now, for the readout.</summary>
        public static int Spawned;

        sealed class Tile
        {
            public Terrain Terrain;
            public Vector3 Origin, Size;
            public TreeInstance[] Trees;
            public GameObject[] Prefabs;
            public Dictionary<Vector2Int, List<int>> Cells = new Dictionary<Vector2Int, List<int>>();
            public HashSet<int> Dead = new HashSet<int>();
        }

        readonly List<Tile> tiles = new List<Tile>();
        readonly Dictionary<long, GameObject> alive = new Dictionary<long, GameObject>();
        readonly HashSet<long> activeCells = new HashSet<long>();
        readonly Queue<(Tile tile, int index)> queue = new Queue<(Tile, int)>();
        readonly Dictionary<GameObject, Stack<GameObject>> pools = new Dictionary<GameObject, Stack<GameObject>>();
        readonly List<(long key, float d2, Tile tile, Vector2Int cell)> wanted = new List<(long, float, Tile, Vector2Int)>();
        readonly List<long> dropping = new List<long>();
        Forest forest;
        Transform root;
        Vector3 lastEye = new Vector3(float.NaN, 0f, 0f);
        float lastRadius = -1f;
        bool started;

        public int Pending => queue.Count;

        void Awake()
        {
            forest = GetComponent<Forest>();
            forest.Removed += OnRemoved;
            root = new GameObject("NearForest").transform;
            root.SetParent(transform, false);
        }

        void OnDestroy()
        {
            if (forest != null) forest.Removed -= OnRemoved;
            Spawned = 0;
        }

        void Update()
        {
            if (!started)
            {
                started = true;
                foreach (var t in GetComponentsInChildren<Terrain>())
                {
                    t.treeDistance = 0f; // this draws the trees, not the terrain
                    var d = t.terrainData;
                    var tile = new Tile { Terrain = t, Origin = t.transform.position, Size = d.size, Trees = d.treeInstances, Prefabs = new GameObject[d.treePrototypes.Length] };
                    for (int k = 0; k < tile.Prefabs.Length; k++) tile.Prefabs[k] = d.treePrototypes[k].prefab;
                    for (int i = 0; i < tile.Trees.Length; i++)
                    {
                        var key = Cell(tile.Trees[i].position, tile.Size);
                        if (!tile.Cells.TryGetValue(key, out var list)) tile.Cells[key] = list = new List<int>();
                        list.Add(i);
                    }
                    tiles.Add(tile);
                }
            }
            var cam = Camera.main;
            if (cam == null) return;
            Vector3 eye = cam.transform.position;
            if (float.IsNaN(lastEye.x) || (eye - lastEye).sqrMagnitude >= MoveStep * MoveStep || !Mathf.Approximately(lastRadius, Radius))
            {
                lastEye = eye;
                lastRadius = Radius;
                Rescan(eye);
            }
            int budget = queue.Count > 1500 ? 200 : 30;
            for (int n = 0; n < budget && queue.Count > 0; n++)
            {
                var (tile, i) = queue.Dequeue();
                Spawn(tile, i);
            }
        }

        /// <summary>Cells within reach come in (their trees queued nearest cell first); cells past reach go.</summary>
        void Rescan(Vector3 eye)
        {
            float reach = Radius + Margin;
            dropping.Clear();
            foreach (long key in activeCells)
                if (CellDistance(key, eye) > reach) dropping.Add(key);
            foreach (long key in dropping) Drop(key);
            wanted.Clear();
            for (int ti = 0; ti < tiles.Count; ti++)
            {
                var tile = tiles[ti];
                foreach (var kv in tile.Cells)
                {
                    long key = Key(ti, kv.Key);
                    if (activeCells.Contains(key)) continue;
                    float d = CellDistance(key, eye);
                    if (d <= Radius) wanted.Add((key, d, tile, kv.Key));
                }
            }
            wanted.Sort((a, b) => a.d2.CompareTo(b.d2));
            foreach (var w in wanted)
            {
                activeCells.Add(w.key);
                foreach (int i in w.tile.Cells[w.cell])
                    if (!w.tile.Dead.Contains(i)) queue.Enqueue((w.tile, i));
            }
        }

        /// <summary>Horizontal distance from the eye to the nearest point of a cell.</summary>
        float CellDistance(long key, Vector3 eye)
        {
            var tile = tiles[(int)(key >> 40)];
            int cx = (int)((key >> 20) & 0xFFFFF), cz = (int)(key & 0xFFFFF);
            float x0 = tile.Origin.x + cx * CellSize, z0 = tile.Origin.z + cz * CellSize;
            float dx = Mathf.Max(x0 - eye.x, 0f, eye.x - (x0 + CellSize)), dz = Mathf.Max(z0 - eye.z, 0f, eye.z - (z0 + CellSize));
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        void Drop(long key)
        {
            activeCells.Remove(key);
            var tile = tiles[(int)(key >> 40)];
            var cell = new Vector2Int((int)((key >> 20) & 0xFFFFF), (int)(key & 0xFFFFF));
            if (!tile.Cells.TryGetValue(cell, out var list)) return;
            int ti = tiles.IndexOf(tile);
            foreach (int i in list) Despawn(TreeKey(ti, i), tile, i);
        }

        void Spawn(Tile tile, int i)
        {
            int ti = tiles.IndexOf(tile);
            long key = TreeKey(ti, i);
            if (alive.ContainsKey(key) || tile.Dead.Contains(i)) return;
            // Still wanted? The cell may have gone out of reach while this tree sat in the queue.
            var inst = tile.Trees[i];
            if (!activeCells.Contains(Key(ti, Cell(inst.position, tile.Size)))) return;
            var prefab = tile.Prefabs[inst.prototypeIndex];
            if (prefab == null) return;
            if (!pools.TryGetValue(prefab, out var pool)) pools[prefab] = pool = new Stack<GameObject>();
            GameObject go = pool.Count > 0 ? pool.Pop() : Make(prefab);
            Place(inst, tile.Origin, tile.Size, out var position, out var rotation, out var scale);
            go.transform.SetPositionAndRotation(position, rotation);
            go.transform.localScale = scale;
            go.SetActive(true);
            alive[key] = go;
            Spawned = alive.Count;
        }

        /// <summary>A fresh instance for the pool: the lowest LOD casts no shadow (far trees' shadows are a smear of pixels for a third of the shadow pass).</summary>
        GameObject Make(GameObject prefab)
        {
            var go = Instantiate(prefab, root);
            if (go.TryGetComponent<LODGroup>(out var lodGroup))
            {
                var lods = lodGroup.GetLODs();
                if (lods.Length > 1)
                    foreach (var r in lods[lods.Length - 1].renderers)
                        if (r != null) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            return go;
        }

        void Despawn(long key, Tile tile, int i)
        {
            if (!alive.TryGetValue(key, out var go)) return;
            alive.Remove(key);
            go.SetActive(false);
            var prefab = tile.Prefabs[tile.Trees[i].prototypeIndex];
            if (prefab != null) pools[prefab].Push(go);
            else Destroy(go);
            Spawned = alive.Count;
        }

        /// <summary>A tree was chopped (it left the terrain's list): it leaves here for good.</summary>
        void OnRemoved(Terrain terrain, Vector3 basePosition)
        {
            int ti = tiles.FindIndex(t => t.Terrain == terrain);
            if (ti < 0) return;
            var tile = tiles[ti];
            var local = basePosition - tile.Origin;
            var cell = new Vector2Int(Mathf.FloorToInt(local.x / CellSize), Mathf.FloorToInt(local.z / CellSize));
            int best = -1;
            float bestD = 0.25f;
            for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (!tile.Cells.TryGetValue(new Vector2Int(cell.x + dx, cell.y + dz), out var list)) continue;
                foreach (int i in list)
                {
                    if (tile.Dead.Contains(i)) continue;
                    Place(tile.Trees[i], tile.Origin, tile.Size, out var p, out _, out _);
                    float d = (p - basePosition).sqrMagnitude;
                    if (d < bestD) { bestD = d; best = i; }
                }
            }
            if (best < 0) return;
            tile.Dead.Add(best);
            Despawn(TreeKey(ti, best), tile, best);
        }

        /// <summary>Which 64 m cell of its tile a tree (normalised position) is in.</summary>
        public static Vector2Int Cell(Vector3 normalised, Vector3 size) =>
            new Vector2Int(Mathf.FloorToInt(normalised.x * size.x / CellSize), Mathf.FloorToInt(normalised.z * size.z / CellSize));

        public static long Key(int tile, Vector2Int cell) => ((long)tile << 40) | ((long)cell.x << 20) | (uint)cell.y;
        static long TreeKey(int tile, int index) => ((long)tile << 32) | (uint)index;

        /// <summary>Where a terrain tree instance stands in the world, how it is turned and scaled.</summary>
        public static void Place(TreeInstance inst, Vector3 origin, Vector3 size, out Vector3 position, out Quaternion rotation, out Vector3 scale)
        {
            position = origin + Vector3.Scale(inst.position, size);
            rotation = Quaternion.Euler(0f, inst.rotation * Mathf.Rad2Deg, 0f);
            scale = new Vector3(inst.widthScale, inst.heightScale, inst.widthScale);
        }
    }
}
