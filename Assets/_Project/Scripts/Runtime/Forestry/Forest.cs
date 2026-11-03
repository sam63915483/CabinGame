using System.Collections.Generic;
using UnityEngine;

namespace Cabin.Forestry
{
    /// <summary>
    /// The map's trees, for the axe. Finds the tree nearest a point — a terrain tree (through a spatial index of each
    /// tile's tree instances) or one already being chopped — and turns a terrain tree into a <see cref="ChoppingTree"/>
    /// on its first chop, taking it out of the terrain. The terrain data is copied first (once per tile), so play never
    /// changes the generated assets; chopped trees come back on the next run. Also owns the wood-chip burst and the
    /// chopping sounds. On the World scene's map.
    /// </summary>
    public sealed class Forest : MonoBehaviour
    {
        const float Cell = 8f;

        [Tooltip("Wood chips (a particle material).")]
        [SerializeField] Material chipMaterial;
        [Tooltip("The cut faces of a notch and a felled stump.")]
        [SerializeField] Material cutWoodMaterial;
        [SerializeField] AudioClip[] chopClips, fallClips, landClips;

        readonly Dictionary<Terrain, Dictionary<Vector2Int, List<int>>> index = new Dictionary<Terrain, Dictionary<Vector2Int, List<int>>>();

        /// <summary>A terrain tree was taken out of the terrain (chopped): the terrain and where it stood.</summary>
        public event System.Action<Terrain, Vector3> Removed;

        /// <summary>The terrain's tree list changed under us: forget its index.</summary>
        public void Invalidate(Terrain terrain) => index.Remove(terrain);
        readonly HashSet<Terrain> copied = new HashSet<Terrain>();
        readonly List<ChoppingTree> chopping = new List<ChoppingTree>();
        Terrain[] terrains;
        ParticleSystem chips;
        AudioClip lastChop;

        public void Set(Material chip, Material cutWood, AudioClip[] chop, AudioClip[] fall, AudioClip[] land)
        {
            chipMaterial = chip;
            cutWoodMaterial = cutWood;
            chopClips = chop;
            fallClips = fall;
            landClips = land;
        }

        void Awake()
        {
            terrains = GetComponentsInChildren<Terrain>();
            chips = BuildChips();
        }

        /// <summary>The tree whose trunk is nearest <paramref name="point"/> (horizontally), within <paramref name="radius"/>.</summary>
        public bool Nearest(Vector3 point, float radius, out TreeTarget target)
        {
            target = default;
            float best = radius * radius;
            foreach (var c in chopping)
            {
                if (c == null || c.Fallen) continue;
                Vector3 d = c.transform.position - point;
                d.y = 0f;
                if (d.sqrMagnitude < best)
                {
                    best = d.sqrMagnitude;
                    target = TreeTarget.Of(c);
                }
            }
            terrains ??= GetComponentsInChildren<Terrain>();
            foreach (var terrain in terrains)
            {
                var data = terrain.terrainData;
                Vector3 local = point - terrain.transform.position;
                if (local.x < -radius || local.z < -radius || local.x > data.size.x + radius || local.z > data.size.z + radius) continue;
                var cells = Index(terrain);
                int cx = Mathf.FloorToInt(local.x / Cell), cz = Mathf.FloorToInt(local.z / Cell);
                int reach = Mathf.CeilToInt(radius / Cell);
                for (int dz = -reach; dz <= reach; dz++)
                for (int dx = -reach; dx <= reach; dx++)
                {
                    if (!cells.TryGetValue(new Vector2Int(cx + dx, cz + dz), out var list)) continue;
                    foreach (int i in list)
                    {
                        var inst = data.GetTreeInstance(i);
                        Vector3 p = terrain.transform.position + Vector3.Scale(inst.position, data.size);
                        Vector3 d = p - point;
                        d.y = 0f;
                        if (d.sqrMagnitude >= best) continue;
                        var proto = data.treePrototypes[inst.prototypeIndex].prefab;
                        var choppable = proto != null ? proto.GetComponent<ChoppableTree>() : null;
                        if (choppable == null) continue;
                        best = d.sqrMagnitude;
                        target = new TreeTarget
                        {
                            Terrain = terrain,
                            Index = i,
                            Data = choppable,
                            Base = p,
                            Rotation = Quaternion.Euler(0f, inst.rotation * Mathf.Rad2Deg, 0f),
                            Scale = new Vector3(inst.widthScale, inst.heightScale, inst.widthScale),
                        };
                    }
                }
            }
            return target.Data != null;
        }


        /// <summary>Every standing terrain tree within <paramref name="radius"/> of <paramref name="point"/> (horizontally), added to <paramref name="into"/>.</summary>
        public void Within(Vector3 point, float radius, List<TreeTarget> into)
        {
            float r2 = radius * radius;
            terrains ??= GetComponentsInChildren<Terrain>();
            foreach (var terrain in terrains)
            {
                var data = terrain.terrainData;
                Vector3 local = point - terrain.transform.position;
                if (local.x < -radius || local.z < -radius || local.x > data.size.x + radius || local.z > data.size.z + radius) continue;
                var cells = Index(terrain);
                int cx = Mathf.FloorToInt(local.x / Cell), cz = Mathf.FloorToInt(local.z / Cell);
                int reach = Mathf.CeilToInt(radius / Cell);
                for (int dz = -reach; dz <= reach; dz++)
                for (int dx = -reach; dx <= reach; dx++)
                {
                    if (!cells.TryGetValue(new Vector2Int(cx + dx, cz + dz), out var list)) continue;
                    foreach (int i in list)
                    {
                        var inst = data.GetTreeInstance(i);
                        Vector3 p = terrain.transform.position + Vector3.Scale(inst.position, data.size);
                        Vector3 d = p - point;
                        d.y = 0f;
                        if (d.sqrMagnitude > r2) continue;
                        var proto = data.treePrototypes[inst.prototypeIndex].prefab;
                        var choppable = proto != null ? proto.GetComponent<ChoppableTree>() : null;
                        if (choppable == null) continue;
                        into.Add(new TreeTarget
                        {
                            Terrain = terrain,
                            Index = i,
                            Data = choppable,
                            Base = p,
                            Rotation = Quaternion.Euler(0f, inst.rotation * Mathf.Rad2Deg, 0f),
                            Scale = new Vector3(inst.widthScale, inst.heightScale, inst.widthScale),
                        });
                    }
                }
            }
        }

        /// <summary>The tree being chopped for this target, making it from the terrain tree on the first chop.</summary>
        public ChoppingTree Begin(TreeTarget t, float cutWorldY)
        {
            if (t.Chopping != null) return t.Chopping;
            var terrain = t.Terrain;
            if (copied.Add(terrain))
            {
                var copy = Instantiate(terrain.terrainData);
                terrain.terrainData = copy;
                if (terrain.TryGetComponent<TerrainCollider>(out var tc)) tc.terrainData = copy;
            }
            var data = terrain.terrainData;
            var trees = new List<TreeInstance>(data.treeInstances);
            trees.RemoveAt(t.Index);
            data.treeInstances = trees.ToArray();
            // The terrain collider keeps its tree colliders until it's rebuilt; left alone, the felled top would start
            // inside the old trunk's collider and be thrown.
            if (terrain.TryGetComponent<TerrainCollider>(out var ground))
            {
                ground.terrainData = null;
                ground.terrainData = data;
            }
            index.Remove(terrain);
            Removed?.Invoke(terrain, t.Base);
            var tree = ChoppingTree.Create(this, t.Data, t.Base, t.Rotation, t.Scale, cutWorldY, cutWoodMaterial);
            chopping.Add(tree);
            return tree;
        }

        Dictionary<Vector2Int, List<int>> Index(Terrain terrain)
        {
            if (index.TryGetValue(terrain, out var cells)) return cells;
            cells = new Dictionary<Vector2Int, List<int>>();
            var data = terrain.terrainData;
            var trees = data.treeInstances;
            for (int i = 0; i < trees.Length; i++)
            {
                Vector3 p = Vector3.Scale(trees[i].position, data.size);
                var key = new Vector2Int(Mathf.FloorToInt(p.x / Cell), Mathf.FloorToInt(p.z / Cell));
                if (!cells.TryGetValue(key, out var list)) cells[key] = list = new List<int>();
                list.Add(i);
            }
            index[terrain] = cells;
            return cells;
        }

        /// <summary>Chips flying off a chop, out along the cut's normal.</summary>
        public void Chips(Vector3 point, Vector3 normal, float power)
        {
            if (chips == null) return;
            chips.transform.SetPositionAndRotation(point, Quaternion.LookRotation(normal));
            chips.Emit(Mathf.RoundToInt(Mathf.Lerp(6f, 16f, power)));
        }

        public AudioClip ChopClip()
        {
            if (chopClips == null || chopClips.Length == 0) return null;
            var c = chopClips[Random.Range(0, chopClips.Length)];
            if (c == lastChop && chopClips.Length > 1) c = chopClips[(System.Array.IndexOf(chopClips, c) + 1) % chopClips.Length];
            return lastChop = c;
        }

        public AudioClip FallClip() => fallClips != null && fallClips.Length > 0 ? fallClips[Random.Range(0, fallClips.Length)] : null;
        public AudioClip LandClip() => landClips != null && landClips.Length > 0 ? landClips[Random.Range(0, landClips.Length)] : null;

        ParticleSystem BuildChips()
        {
            var go = new GameObject("WoodChips");
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2.2f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 4f);
            main.startSize3D = true;
            main.startSizeX = new ParticleSystem.MinMaxCurve(0.02f, 0.06f);
            main.startSizeY = new ParticleSystem.MinMaxCurve(0.01f, 0.025f);
            main.startSizeZ = new ParticleSystem.MinMaxCurve(0.02f, 0.05f);
            main.startRotation3D = true;
            main.startRotationX = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.93f, 0.83f, 0.62f), new Color(0.75f, 0.58f, 0.38f));
            main.gravityModifier = 1f;
            main.maxParticles = 300;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 35f;
            shape.radius = 0.03f;
            var rot = ps.rotationOverLifetime;
            rot.enabled = true;
            rot.separateAxes = true;
            rot.x = new ParticleSystem.MinMaxCurve(-8f, 8f);
            rot.y = new ParticleSystem.MinMaxCurve(-8f, 8f);
            var collision = ps.collision;
            collision.enabled = true;
            collision.type = ParticleSystemCollisionType.World;
            collision.quality = ParticleSystemCollisionQuality.Low;
            collision.bounce = 0.25f;
            collision.dampen = 0.6f;
            collision.lifetimeLoss = 0f;
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Mesh;
            renderer.mesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            renderer.sharedMaterial = chipMaterial;
            return ps;
        }
    }
}
