using Cabin.Build;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Cabin.Editor
{
    /// <summary>
    /// Rebuilds the TestPlatform scene as a forest clearing: a gently rolling 240 m terrain with a level clearing
    /// in the middle, placeholder spruce around it, the lumber racks, the cutting table, the sun, the Player and
    /// the BuildSystem. Seeded, so it comes out the same every time.
    /// </summary>
    public static class TestPlatformBuilder
    {
        const string ScenePath = "Assets/_Project/Scenes/TestPlatform.unity";
        const string TerrainDir = "Assets/Generated/TestTerrain";
        const string TerrainDataPath = TerrainDir + "/TestTerrain.asset";
        const int Seed = 1207;
        const float TerrainSize = 240f;
        const float TerrainHeight = 24f;
        const int HeightRes = 257;
        const float ClearingRadius = 24f;
        const float ClearingBlend = 14f;

        [MenuItem("Tools/Cabin/Scenes/Rebuild TestPlatform", priority = 20)]
        public static void Rebuild()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var player = AssetDatabase.LoadAssetAtPath<GameObject>(SandboxAssetGenerator.PlayerPrefabPath);
            var buildSystem = AssetDatabase.LoadAssetAtPath<GameObject>(SandboxAssetGenerator.BuildSystemPrefabPath);
            var tree = AssetDatabase.LoadAssetAtPath<GameObject>(SandboxAssetGenerator.TreePrefabPath);
            if (player == null || buildSystem == null || tree == null)
            {
                SandboxAssetGenerator.GenerateAll();
                player = AssetDatabase.LoadAssetAtPath<GameObject>(SandboxAssetGenerator.PlayerPrefabPath);
                buildSystem = AssetDatabase.LoadAssetAtPath<GameObject>(SandboxAssetGenerator.BuildSystemPrefabPath);
                tree = AssetDatabase.LoadAssetAtPath<GameObject>(SandboxAssetGenerator.TreePrefabPath);
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            foreach (var go in scene.GetRootGameObjects())
            {
                if (go.TryGetComponent<Camera>(out _)) Object.DestroyImmediate(go);
                else if (go.TryGetComponent<Light>(out var sun))
                {
                    go.name = "Sun";
                    go.transform.rotation = Quaternion.Euler(42f, -40f, 0f);
                    sun.color = new Color(1f, 0.96f, 0.88f);
                    sun.intensity = 1.5f;
                    sun.shadows = LightShadows.Soft;
                }
            }
            RenderSettings.ambientMode = AmbientMode.Skybox;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = 70f;
            RenderSettings.fogEndDistance = 260f;
            RenderSettings.fogColor = new Color(0.70f, 0.78f, 0.86f);

            var terrain = BuildTerrain();
            ScatterTrees(terrain, tree);
            BuildWorkstations();

            var p = (GameObject)PrefabUtility.InstantiatePrefab(player, scene);
            p.transform.SetPositionAndRotation(new Vector3(0f, 0.1f, -7f), Quaternion.identity);
            PrefabUtility.InstantiatePrefab(buildSystem, scene);

            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log("[Cabin] TestPlatform rebuilt: forest clearing, racks, cutting table");
        }

        // ------------------------------------------------------------------ terrain

        static Terrain BuildTerrain()
        {
            SandboxAssetGenerator.EnsureFolder(TerrainDir);
            var data = AssetDatabase.LoadAssetAtPath<TerrainData>(TerrainDataPath);
            if (data == null)
            {
                data = new TerrainData();
                AssetDatabase.CreateAsset(data, TerrainDataPath);
            }
            data.heightmapResolution = HeightRes;
            data.size = new Vector3(TerrainSize, TerrainHeight, TerrainSize);

            float clearingH = 0.42f;
            var heights = new float[HeightRes, HeightRes];
            for (int z = 0; z < HeightRes; z++)
                for (int x = 0; x < HeightRes; x++)
                {
                    float wx = x / (float)(HeightRes - 1) * TerrainSize - TerrainSize * 0.5f;
                    float wz = z / (float)(HeightRes - 1) * TerrainSize - TerrainSize * 0.5f;
                    float h = 0.30f
                              + 0.45f * Mathf.PerlinNoise((wx + 1000f) * 0.011f + Seed, (wz + 1000f) * 0.011f)
                              + 0.14f * Mathf.PerlinNoise((wx + 500f) * 0.045f, (wz + 500f) * 0.045f + Seed)
                              + 0.04f * Mathf.PerlinNoise(wx * 0.18f, wz * 0.18f);
                    float r = Mathf.Sqrt(wx * wx + wz * wz);
                    float t = Mathf.Clamp01((r - ClearingRadius) / ClearingBlend);
                    t = t * t * (3f - 2f * t);
                    heights[z, x] = Mathf.Lerp(clearingH, h, t);
                }
            data.SetHeights(0, 0, heights);

            var grass = AssetDatabase.LoadAssetAtPath<Texture2D>(SandboxAssetGenerator.TexturesDir + "/Grass.png");
            var dirt = AssetDatabase.LoadAssetAtPath<Texture2D>(SandboxAssetGenerator.TexturesDir + "/Dirt.png");
            data.terrainLayers = new[] { TerrainLayer("Grass", grass, 4f), TerrainLayer("Dirt", dirt, 3f) };

            data.alphamapResolution = 256;
            var alpha = new float[256, 256, 2];
            for (int z = 0; z < 256; z++)
                for (int x = 0; x < 256; x++)
                {
                    float nx = x / 255f, nz = z / 255f;
                    float slope = data.GetSteepness(nx, nz) / 45f;
                    float patches = Mathf.PerlinNoise(nx * 9f + Seed, nz * 9f) ;
                    float d = Mathf.Clamp01(slope * 1.4f + Mathf.Max(0f, patches - 0.72f) * 2.5f);
                    alpha[z, x, 0] = 1f - d;
                    alpha[z, x, 1] = d;
                }
            data.SetAlphamaps(0, 0, alpha);
            EditorUtility.SetDirty(data);

            var go = Terrain.CreateTerrainGameObject(data);
            go.name = "Terrain";
            go.transform.position = new Vector3(-TerrainSize * 0.5f, -clearingH * TerrainHeight, -TerrainSize * 0.5f);
            var terrain = go.GetComponent<Terrain>();
            terrain.heightmapPixelError = 4f;
            terrain.basemapDistance = 400f;
            go.isStatic = true;
            return terrain;
        }

        static TerrainLayer TerrainLayer(string name, Texture2D diffuse, float tile)
        {
            string path = $"{TerrainDir}/Layer_{name}.terrainlayer";
            var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
            if (layer == null)
            {
                layer = new TerrainLayer();
                AssetDatabase.CreateAsset(layer, path);
            }
            layer.diffuseTexture = diffuse;
            layer.tileSize = new Vector2(tile, tile);
            layer.smoothness = 0f;
            EditorUtility.SetDirty(layer);
            return layer;
        }

        static void ScatterTrees(Terrain terrain, GameObject prefab)
        {
            var root = new GameObject("Trees");
            root.isStatic = true;
            var rng = new System.Random(Seed);
            int placed = 0, attempts = 0;
            while (placed < 420 && attempts++ < 20000)
            {
                float x = (float)(rng.NextDouble() * 2.0 - 1.0) * (TerrainSize * 0.5f - 6f);
                float z = (float)(rng.NextDouble() * 2.0 - 1.0) * (TerrainSize * 0.5f - 6f);
                float r = Mathf.Sqrt(x * x + z * z);
                if (r < ClearingRadius + 5f) continue;
                float density = Mathf.PerlinNoise(x * 0.03f + 11f, z * 0.03f + 5f);
                if (rng.NextDouble() > 0.35 + density * 0.6) continue;
                var pos = new Vector3(x, 0f, z);
                pos.y = terrain.SampleHeight(pos) + terrain.transform.position.y;
                var normal = terrain.terrainData.GetInterpolatedNormal((x + TerrainSize * 0.5f) / TerrainSize, (z + TerrainSize * 0.5f) / TerrainSize);
                if (Vector3.Angle(normal, Vector3.up) > 32f) continue;
                var tree = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                tree.transform.SetParent(root.transform);
                float s = 0.75f + (float)rng.NextDouble() * 0.6f;
                tree.transform.SetPositionAndRotation(pos - Vector3.up * 0.15f, Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f));
                tree.transform.localScale = Vector3.one * s;
                tree.isStatic = true;
                placed++;
            }
        }

        // ------------------------------------------------------------------ workstations

        static void BuildWorkstations()
        {
            var lumber = AssetDatabase.LoadAssetAtPath<Material>(SandboxAssetGenerator.MaterialsDir + "/Lumber.mat");
            var plywood = AssetDatabase.LoadAssetAtPath<Material>(SandboxAssetGenerator.MaterialsDir + "/Plywood.mat");
            var bench = AssetDatabase.LoadAssetAtPath<Material>(SandboxAssetGenerator.MaterialsDir + "/Bench.mat");
            var concrete = AssetDatabase.LoadAssetAtPath<Material>(SandboxAssetGenerator.MaterialsDir + "/Concrete.mat");
            var blockMesh = AssetDatabase.LoadAssetAtPath<Mesh>(SandboxAssetGenerator.MeshesDir + "/DeckBlock.asset");
            var root = new GameObject("Workstations");

            // One row of racks across the back of the clearing, boards pointing away from the player so the long
            // ones don't overlap: 2×4s on the left, 4×4s on the right, shortest nearest the middle.
            var lengths = Stock.LumberLengths;
            var side = Quaternion.Euler(0f, 90f, 0f);
            for (int i = 0; i < lengths.Length; i++)
            {
                float x = 2.2f + i * 1.8f;
                float z = 9f + Stock.Size(StockKind.TwoByFour, lengths[i]).x * 0.5f;
                Rack(root.transform, StockKind.TwoByFour, lengths[i], new Vector3(-x, 0f, z), lumber, bench, side);
                Rack(root.transform, StockKind.FourByFour, lengths[i], new Vector3(x, 0f, z), lumber, bench, side);
            }
            // Plywood 4×8, 4×10, 4×12 side by side on the left of the clearing.
            for (int i = 0; i < Stock.PlywoodLengths.Length; i++)
                Rack(root.transform, StockKind.Plywood, Stock.PlywoodLengths[i], new Vector3(-13.5f - i * 2.2f, 0f, 3f), plywood, bench, side);
            Table(root.transform, new Vector3(11f, 0f, 2f), Quaternion.Euler(0f, -90f, 0f), bench);
            BlockPallet(root.transform, new Vector3(-12f, 0f, -3f), concrete, bench, blockMesh);
        }

        static void Rack(Transform parent, StockKind kind, float length, Vector3 at, Material wood, Material benchMat, Quaternion? rotation = null)
        {
            var size = Stock.Size(kind, length);
            var go = new GameObject($"Rack_{kind}_{Units.FeetInches(size.x).Replace("\"", "").Replace("'", "ft")}");
            go.transform.SetParent(parent);
            go.transform.SetPositionAndRotation(at, rotation ?? Quaternion.identity);
            var rack = go.AddComponent<StockRack>();
            rack.Set(kind, length);
            go.isStatic = true;

            float supportH = 0.45f;
            float span = Mathf.Min(size.x - 0.6f, 3.0f);
            foreach (float sx in new[] { -span * 0.5f, span * 0.5f })
            {
                var sup = Block(go.transform, "Support", new Vector3(sx, supportH * 0.5f, 0f), new Vector3(0.12f, supportH, 0.9f), benchMat);
                sup.isStatic = true;
            }
            // A few sample boards on the cradle, so the rack reads as "that length".
            int count = kind == StockKind.Plywood ? 4 : 5;
            for (int i = 0; i < count; i++)
            {
                Vector3 pos, scale;
                if (kind == StockKind.Plywood)
                {
                    pos = new Vector3(0f, supportH + size.z * (i + 0.5f) + 0.002f * i, 0f);
                    scale = new Vector3(size.x, size.z, size.y);
                }
                else
                {
                    int row = i / 3, col = i % 3;
                    pos = new Vector3(0f, supportH + size.y * (row + 0.5f), (col - 1) * (size.z + 0.02f));
                    scale = new Vector3(size.x, size.y, size.z);
                }
                var board = Block(go.transform, "Board", pos, scale, wood);
                board.isStatic = true;
            }
            var box = go.AddComponent<BoxCollider>();
            float stackH = kind == StockKind.Plywood ? size.z * count : size.y * 2f;
            box.center = new Vector3(0f, supportH + stackH * 0.5f, 0f);
            box.size = new Vector3(size.x, stackH, kind == StockKind.Plywood ? size.y : size.z * 3f + 0.1f);
        }

        /// <summary>A pallet of deck blocks: a low platform with a 2 × 2 stack of blocks on it.</summary>
        static void BlockPallet(Transform parent, Vector3 at, Material concrete, Material benchMat, Mesh blockMesh)
        {
            var go = new GameObject("Rack_DeckBlock");
            go.transform.SetParent(parent);
            go.transform.position = at;
            go.isStatic = true;
            go.AddComponent<StockRack>().Set(StockKind.DeckBlock, 0f);
            float b = Stock.DeckBlockBase, h = Stock.DeckBlockHeight;
            Block(go.transform, "Pallet", new Vector3(0f, 0.06f, 0f), new Vector3(b * 2.3f, 0.12f, b * 2.3f), benchMat).isStatic = true;
            foreach (float sx in new[] { -b * 0.55f, b * 0.55f })
                foreach (float sz in new[] { -b * 0.55f, b * 0.55f })
                {
                    var blk = new GameObject("Block");
                    blk.transform.SetParent(go.transform, false);
                    blk.transform.localPosition = new Vector3(sx, 0.12f + h * 0.5f, sz);
                    blk.AddComponent<MeshFilter>().sharedMesh = blockMesh;
                    blk.AddComponent<MeshRenderer>().sharedMaterial = concrete;
                    blk.isStatic = true;
                }
            var col = go.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 0.06f + h * 0.5f, 0f);
            col.size = new Vector3(b * 2.3f, 0.12f + h, b * 2.3f);
        }

        static void Table(Transform parent, Vector3 at, Quaternion rotation, Material benchMat)
        {
            var go = new GameObject("CuttingTable");
            go.transform.SetParent(parent);
            go.transform.SetPositionAndRotation(at, rotation);
            go.isStatic = true;
            const float h = 0.9f, w = 2.6f, d = 1.1f, t = 0.08f;
            Block(go.transform, "Top", new Vector3(0f, h - t * 0.5f, 0f), new Vector3(w, t, d), benchMat).isStatic = true;
            foreach (float sx in new[] { -w * 0.5f + 0.1f, w * 0.5f - 0.1f })
                foreach (float sz in new[] { -d * 0.5f + 0.1f, d * 0.5f - 0.1f })
                    Block(go.transform, "Leg", new Vector3(sx, (h - t) * 0.5f, sz), new Vector3(0.1f, h - t, 0.1f), benchMat).isStatic = true;
            var topAnchor = new GameObject("TopSurface");
            topAnchor.transform.SetParent(go.transform, false);
            topAnchor.transform.localPosition = new Vector3(0f, h, 0f);
            go.AddComponent<CuttingTable>().Set(topAnchor.transform, new Vector2(w - 0.2f, d - 0.2f));
        }

        static GameObject Block(Transform parent, string name, Vector3 localPos, Vector3 scale, Material mat)
        {
            var b = GameObject.CreatePrimitive(PrimitiveType.Cube);
            b.name = name;
            b.transform.SetParent(parent, false);
            b.transform.localPosition = localPos;
            b.transform.localScale = scale;
            b.GetComponent<MeshRenderer>().sharedMaterial = mat;
            return b;
        }
    }
}
