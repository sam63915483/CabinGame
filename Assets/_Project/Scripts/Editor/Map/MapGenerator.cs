using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Cabin.Core;
using Cabin.Forestry;
using Cabin.Map;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Cabin.Editor.Map
{
    /// <summary>
    /// Tools/Cabin/Map/Generate World: builds the World scene's map from the <see cref="MapRecipe"/>. Nova Scotia's real
    /// coastline and broad shape (<see cref="ElevationSource"/>, <see cref="ProvinceMask"/>) on a coarse grid; the land's
    /// relief by region (<see cref="BaseTerrain"/>); rivers cutting their valleys (<see cref="Erosion"/>); glacial lake
    /// basins (<see cref="Basins"/>); lakes, rivers and waterfalls wherever the drained land puts them
    /// (<see cref="WaterNetwork"/>); the fine heightmap with its shores and channels (<see cref="FineTerrain"/>); ground
    /// painted (<see cref="TerrainPainter"/>), trees and grass, a sea plane, lake and river meshes, fog, and the player
    /// at the spawn. Everything it writes lives in Assets/Generated/World and the World scene's "Map" object; re-runnable.
    /// </summary>
    public static class MapGenerator
    {
        public const string RecipePath = "Assets/_Project/Data/MapRecipe.asset";
        const string OutDir = "Assets/Generated/World";
        const string ScenePath = "Assets/_Project/Scenes/World.unity";
        const string TerrainMaterialPath = "Assets/_Project/Art/Materials/Terrain.mat";
        const string WaterMaterialPath = "Assets/_Project/Art/Materials/Water.mat";
        const string RootName = "Map";
        /// <summary>Where you start: Kejimkujik — lake country in the south-west interior.</summary>
        const double SpawnLongitude = -65.22, SpawnLatitude = 44.42;
        /// <summary>Kept clear of trees round the spawn: room to build.</summary>
        const float ClearingRadius = 30f;

        public static MapRecipe Recipe()
        {
            var recipe = AssetDatabase.LoadAssetAtPath<MapRecipe>(RecipePath);
            if (recipe != null) return recipe;
            SandboxAssetGenerator.EnsureFolder(Path.GetDirectoryName(RecipePath).Replace('\\', '/'));
            recipe = ScriptableObject.CreateInstance<MapRecipe>();
            AssetDatabase.CreateAsset(recipe, RecipePath);
            return recipe;
        }

        [MenuItem("Tools/Cabin/Map/Generate World", priority = 30)]
        public static void Generate()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[Cabin] Stop Play Mode before generating the world");
                return;
            }
            var recipe = Recipe();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                EditorUtility.DisplayProgressBar("Generate World", "Elevation", 0.02f);
                var src = ElevationSource.Load(recipe);
                var proj = new MapProjection(recipe);
                var real = RealElevation(recipe, src, proj);
                src = null;

                EditorUtility.DisplayProgressBar("Generate World", "Relief", 0.08f);
                var baseTerrain = BaseTerrain.Build(recipe, real);
                real = null;
                var coarse = baseTerrain.Heights;
                int cw = coarse.W, ch = coarse.H;
                bool Sea(int i) => coarse.V[i] < 0f;
                bool Fixed(int i) => coarse.V[i] < 0f || i % cw == 0 || i / cw == 0 || i % cw == cw - 1 || i / cw == ch - 1;

                EditorUtility.DisplayProgressBar("Generate World", $"Erosion ({recipe.erosionIterations} iterations)", 0.12f);
                var t0 = sw.Elapsed.TotalSeconds;
                Erosion.Run(coarse, baseTerrain.Hardness, Fixed, baseTerrain.MaxIncision(recipe), new Erosion.Settings
                {
                    Iterations = recipe.erosionIterations, Rate = recipe.erosionRate, Diffusion = recipe.diffusion, MinLand = 0.5f,
                });
                Debug.Log($"[Cabin] Erosion: {sw.Elapsed.TotalSeconds - t0:F0} s");

                EditorUtility.DisplayProgressBar("Generate World", "Lake basins", 0.3f);
                Basins.Scoop(coarse, baseTerrain, recipe, Vector2.zero, new Vector2(recipe.WorldWidth, recipe.WorldDepth));

                EditorUtility.DisplayProgressBar("Generate World", "Lakes and rivers", 0.33f);
                var net = WaterNetwork.Build(coarse, Sea, new WaterNetwork.Settings
                {
                    LakeMinDepth = recipe.lakeMinDepth, LakeMinCells = recipe.lakeMinCells, RiverCatchmentKm2 = recipe.riverCatchmentKm2,
                    FallSlope = recipe.fallSlope, FallMinHeight = recipe.fallMinHeight,
                });
                Debug.Log($"[Cabin] {net.Summary()}");

                EditorUtility.DisplayProgressBar("Generate World", "Fine ground", 0.4f);
                int res = recipe.heightmapResolution;
                int w = recipe.tilesX * (res - 1) + 1, h = recipe.tilesZ * (res - 1) + 1;
                var fine = FineTerrain.Build(coarse, net, FineTerrain.Settings.From(recipe), w, h, recipe.tileSize / (res - 1));
                var heights = fine.Heights;
                var water = fine.Water;
                baseTerrain = null;
                System.GC.Collect();

                Vector3 spawn = Spawn(heights, water, net, proj);
                EditorUtility.DisplayProgressBar("Generate World", "Terrain tiles", 0.55f);
                SandboxAssetGenerator.EnsureFolder(OutDir);
                var datas = Tiles(recipe, heights, water, new Vector2(spawn.x, spawn.z), ClearingRadius);

                EditorUtility.DisplayProgressBar("Generate World", "Trees", 0.85f);
                var rocks = recipe.rockDensity > 0f ? RockLibrary.Build() : new GameObject[0];
                var treePrefabs = TreeLibrary.Build();
                int trees = VegetationScatter.Apply(recipe, datas, treePrefabs, rocks, heights, water, new Vector2(spawn.x, spawn.z), ClearingRadius);
                Debug.Log($"[Cabin] {trees} trees and rocks");
                EditorUtility.DisplayProgressBar("Generate World", "Grass", 0.88f);
                GrassScatter.Apply(recipe, datas, heights, water);
                AssetDatabase.SaveAssets();

                EditorUtility.DisplayProgressBar("Generate World", "Scene", 0.9f);
                BuildScene(recipe, datas, fine.Lakes, net.Rivers, heights, spawn, treePrefabs);
                MapPreview.Write(recipe, fine, net, spawn, sw.Elapsed.TotalSeconds);
                System.GC.Collect();
                EditorUtility.UnloadUnusedAssetsImmediate();
                Debug.Log($"[Cabin] World generated in {sw.Elapsed.TotalSeconds:F0} s");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        /// <summary>The real elevation on the coarse grid: sampled, smoothed, and everything that isn't Nova Scotia sunk.</summary>
        static Grid RealElevation(MapRecipe r, ElevationSource src, MapProjection proj)
        {
            float cell = r.coarseCell;
            int w = Mathf.RoundToInt(r.WorldWidth / cell) + 1, h = Mathf.RoundToInt(r.WorldDepth / cell) + 1;
            var real = new Grid(w, h, cell, new Vector2(r.Origin.x, r.Origin.z));
            Parallel.For(0, h, z =>
            {
                for (int x = 0; x < w; x++)
                {
                    proj.ToGeo(real.X(x), real.Z(z), out double lon, out double lat);
                    real[x, z] = src.Sample(lon, lat);
                }
            });
            real.Blur(Mathf.Max(1, Mathf.RoundToInt(r.smoothing / cell / 2f)), 2);
            int sunk = ProvinceMask.Apply(r, proj, real, -40f);
            Debug.Log($"[Cabin] Not Nova Scotia, sunk: {sunk * cell * cell * r.realMetresPerMetre * r.realMetresPerMetre / 1e6f:F0} km²");
            return real;
        }

        /// <param name="clearing">The kept clearing at the spawn (world xz) and its radius: painted as open turf, as it is kept clear of trees.</param>
        static TerrainData[,] Tiles(MapRecipe r, Grid heights, Grid water, Vector2 clearing, float clearingRadius)
        {
            int res = r.heightmapResolution, ares = r.alphamapResolution;
            float sizeY = r.seaFloorDepth + r.maxHeight;
            var layers = TerrainTextures.Build(r.seed);
            var noise = new Noise(r.seed + 7);
            var datas = new TerrainData[r.tilesX, r.tilesZ];
            for (int tz = 0; tz < r.tilesZ; tz++)
            for (int tx = 0; tx < r.tilesX; tx++)
            {
                EditorUtility.DisplayProgressBar("Generate World", $"Tile {tx},{tz}", 0.55f + 0.3f * (tz * r.tilesX + tx) / (r.tilesX * r.tilesZ));
                // Saved first: alphamap textures set before the asset exists aren't kept with it.
                string path = $"{OutDir}/Tile_{tx}_{tz}.asset";
                if (AssetDatabase.LoadMainAssetAtPath(path) != null) AssetDatabase.DeleteAsset(path);
                var data = new TerrainData { heightmapResolution = res };
                data.size = new Vector3(r.tileSize, sizeY, r.tileSize);
                AssetDatabase.CreateAsset(data, path);
                var hm = new float[res, res];
                int ox = tx * (res - 1), oz = tz * (res - 1);
                Parallel.For(0, res, z =>
                {
                    for (int x = 0; x < res; x++)
                        hm[z, x] = Mathf.Clamp01((heights[ox + x, oz + z] + r.seaFloorDepth) / sizeY);
                });
                data.SetHeights(0, 0, hm);

                data.terrainLayers = layers;
                data.alphamapResolution = ares;
                var splat = new float[ares, ares, TerrainPainter.Layers];
                float x0 = r.Origin.x + tx * r.tileSize, z0 = r.Origin.z + tz * r.tileSize;
                Parallel.For(0, ares, j =>
                {
                    var wts = new float[TerrainPainter.Layers];
                    for (int i = 0; i < ares; i++)
                    {
                        float wx = x0 + i / (float)(ares - 1) * r.tileSize, wz = z0 + j / (float)(ares - 1) * r.tileSize;
                        int gx = Mathf.Clamp(Mathf.RoundToInt((wx - heights.Origin.x) / heights.Cell), 0, heights.W - 1);
                        int gz = Mathf.Clamp(Mathf.RoundToInt((wz - heights.Origin.y) / heights.Cell), 0, heights.H - 1);
                        float hgt = heights[gx, gz];
                        float level = WaterNear(water, wx, wz, out bool sea);
                        float open = TerrainPainter.Clearing(Vector2.Distance(new Vector2(wx, wz), clearing), clearingRadius);
                        TerrainPainter.Weights(r, noise, hgt, heights.Slope(gx, gz), level, sea, wx, wz, wts, open);
                        for (int k = 0; k < TerrainPainter.Layers; k++) splat[j, i, k] = wts[k];
                    }
                });
                data.SetAlphamaps(0, 0, splat);
                data.baseMapResolution = 512;
                EditorUtility.SetDirty(data);
                datas[tx, tz] = data;
            }
            AssetDatabase.SaveAssets();
            return datas;
        }

        /// <summary>The water level that matters here (a lake or river in this cell or the next, else the sea at 0).</summary>
        public static float WaterNear(Grid water, float wx, float wz, out bool sea)
        {
            int cx = Mathf.RoundToInt((wx - water.Origin.x) / water.Cell), cz = Mathf.RoundToInt((wz - water.Origin.y) / water.Cell);
            float best = float.NaN;
            for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
            {
                float v = water.At(cx + dx, cz + dz);
                if (!float.IsNaN(v) && (float.IsNaN(best) || v > best)) best = v;
            }
            sea = float.IsNaN(best);
            return sea ? 0f : best;
        }

        /// <summary>
        /// The ground's material (Cabin/Terrain, all eight layers in one pass): the engine binds layers 0-3 and the
        /// first control map per tile, so layers 4-7, every layer's tile and the macro noise go on the material here
        /// and <see cref="TerrainControl"/> binds each tile's second control map.
        /// </summary>
        public static Material TerrainMaterial(TerrainLayer[] layers)
        {
            var shader = Shader.Find("Cabin/Terrain");
            var m = AssetDatabase.LoadAssetAtPath<Material>(TerrainMaterialPath);
            if (m == null)
            {
                m = new Material(shader);
                AssetDatabase.CreateAsset(m, TerrainMaterialPath);
            }
            m.shader = shader;
            for (int i = 4; i < TerrainPainter.Layers; i++)
            {
                m.SetTexture($"_Splat{i}", layers[i].diffuseTexture);
                m.SetTexture($"_Normal{i}", layers[i].normalMapTexture);
                m.SetFloat($"_NormalScale{i}", layers[i].normalScale);
            }
            m.SetVector("_Tiles0", new Vector4(layers[0].tileSize.x, layers[1].tileSize.x, layers[2].tileSize.x, layers[3].tileSize.x));
            m.SetVector("_Tiles1", new Vector4(layers[4].tileSize.x, layers[5].tileSize.x, layers[6].tileSize.x, layers[7].tileSize.x));
            m.SetTexture("_MacroTex", TerrainTextures.Macro());
            m.SetFloat("_MacroScale", 260f);
            m.SetFloat("_MacroStrength", 0.3f);
            m.SetFloat("_MacroWarmth", 0.4f);
            m.SetVector("_CliffSlope", new Vector4(48f, 60f, 0f, 0f));
            m.SetFloat("_HeightTransition", 0.25f);
            m.SetFloat("_TriplanarSharpness", 4f);
            m.SetFloat("_Smoothness", 0.08f);
            m.SetFloat("_EnableInstancedPerPixelNormal", 1f);
            m.EnableKeyword("_TERRAIN_INSTANCED_PERPIXEL_NORMAL");
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <param name="lakes">The lakes' levels on the fine grid (NaN dry).</param>
        static void BuildScene(MapRecipe r, TerrainData[,] datas, Grid lakes, List<River> rivers, Grid heights, Vector3 spawn, GameObject[] treePrefabs)
        {
            var previous = EditorSceneManager.GetActiveScene().path;
            if (EditorSceneManager.GetActiveScene().isDirty) EditorSceneManager.SaveOpenScenes();
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            foreach (var go in scene.GetRootGameObjects())
                if (go.name == RootName || go.name == "Main Camera" || go.name == "Player" || go.name == "BuildSystem") Object.DestroyImmediate(go);

            var root = new GameObject(RootName);
            var material = TerrainMaterial(datas[0, 0].terrainLayers);
            root.AddComponent<Forest>().Set(WoodMaterial(), CutWoodMaterial(), SandboxAssetGenerator.Clips("Axe_Chop"), SandboxAssetGenerator.Clips("Tree_Creak"),
                SandboxAssetGenerator.Clips("Tree_Crash"));
            root.AddComponent<TreeColliders>();
            root.AddComponent<NearForest>();
            GrassScatter.Wire(root.AddComponent<GrassField>());
            var terrains = new Terrain[r.tilesX, r.tilesZ];
            for (int tz = 0; tz < r.tilesZ; tz++)
            for (int tx = 0; tx < r.tilesX; tx++)
            {
                var go = Terrain.CreateTerrainGameObject(datas[tx, tz]);
                go.name = $"Tile_{tx}_{tz}";
                go.transform.SetParent(root.transform, false);
                go.transform.position = r.Origin + new Vector3(tx * r.tileSize, 0f, tz * r.tileSize);
                go.isStatic = true;
                var t = go.GetComponent<Terrain>();
                t.materialTemplate = material;
                t.drawInstanced = true;
                t.heightmapPixelError = 4f;
                t.basemapDistance = 20000f; // the full material everywhere: Cabin/Terrain has no base map (the haze covers the distance anyway)
                go.AddComponent<TerrainControl>();
                t.groupingID = 1;
                t.treeDistance = 0f; // NearForest draws the real trees, FarForest the impostors beyond
                t.treeMaximumFullLODCount = 400;
                t.detailObjectDistance = 0f; // GrassField draws the grass from the detail maps
                t.allowAutoConnect = true;
                terrains[tx, tz] = t;
            }
            for (int tz = 0; tz < r.tilesZ; tz++)
            for (int tx = 0; tx < r.tilesX; tx++)
                terrains[tx, tz].SetNeighbors(
                    tx > 0 ? terrains[tx - 1, tz] : null, tz < r.tilesZ - 1 ? terrains[tx, tz + 1] : null,
                    tx < r.tilesX - 1 ? terrains[tx + 1, tz] : null, tz > 0 ? terrains[tx, tz - 1] : null);

            // Water: the sea as one plane, lakes and rivers per tile.
            string meshPath = $"{OutDir}/Water.asset";
            if (AssetDatabase.LoadMainAssetAtPath(meshPath) != null) AssetDatabase.DeleteAsset(meshPath);
            var container = ScriptableObject.CreateInstance<MeshContainer>();
            AssetDatabase.CreateAsset(container, meshPath);
            var waterMat = WaterMaterial();
            var waterRoot = new GameObject("Water");
            waterRoot.transform.SetParent(root.transform, false);
            var sea = WaterMeshes.Sea(heights, new Rect(r.Origin.x, r.Origin.z, r.WorldWidth, r.WorldDepth), 600f, 32f, 6000f);
            AssetDatabase.AddObjectToAsset(sea, container);
            Renderable(waterRoot.transform, "Sea", sea, waterMat);
            var extended = WaterMeshes.Extend(lakes, heights, 12);
            int cellsPerTile = Mathf.RoundToInt(r.tileSize / lakes.Cell);
            for (int tz = 0; tz < r.tilesZ; tz++)
            for (int tx = 0; tx < r.tilesX; tx++)
            {
                var mesh = WaterMeshes.Build(extended, heights, tx * cellsPerTile, tz * cellsPerTile, cellsPerTile, $"Water_{tx}_{tz}");
                if (mesh != null)
                {
                    AssetDatabase.AddObjectToAsset(mesh, container);
                    Renderable(waterRoot.transform, mesh.name, mesh, waterMat);
                }
                var tile = new Rect(r.Origin.x + tx * r.tileSize, r.Origin.z + tz * r.tileSize, r.tileSize, r.tileSize);
                var ribbon = WaterMeshes.Rivers(rivers, heights, tile, $"River_{tx}_{tz}");
                if (ribbon != null)
                {
                    AssetDatabase.AddObjectToAsset(ribbon, container);
                    Renderable(waterRoot.transform, ribbon.name, ribbon, waterMat);
                }
            }
            AssetDatabase.SaveAssets();

            // Sun, sky, haze and the post-processing look.
            WorldLook.Apply();
            // The far forest: impostors photographed in this light, drawn beyond the real trees.
            var impostors = TreeImpostors.Build(treePrefabs);
            root.AddComponent<FarForest>().Set(impostors, TreeImpostors.Material(impostors));

            // The player and the building kit, at the spawn.
            var player = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(SandboxAssetGenerator.PlayerPrefabPath), scene);
            player.transform.position = spawn;
            PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(SandboxAssetGenerator.BuildSystemPrefabPath), scene);
            if (player.GetComponentInChildren<Camera>() is Camera cam) cam.farClipPlane = 3000f;

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            if (!string.IsNullOrEmpty(previous) && previous != ScenePath) EditorSceneManager.OpenScene(previous, OpenSceneMode.Single);
        }

        /// <summary>The cut faces of a notch or stump: pale fresh wood.</summary>
        static Material CutWoodMaterial()
        {
            const string path = "Assets/_Project/Art/Materials/CutWood.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(m, path);
            }
            var wood = new Color(0.9f, 0.76f, 0.52f);
            m.SetColor("_BaseColor", wood);
            m.SetFloat("_Smoothness", 0.1f);
            // A little of its own light, so a notch on the shady side still reads as fresh wood.
            m.SetColor("_EmissionColor", wood * 0.22f);
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>Wood chips off the axe: lit particles, coloured by the particle colour.</summary>
        static Material WoodMaterial()
        {
            const string path = "Assets/_Project/Art/Materials/FreshWood.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Particles/Simple Lit"));
                AssetDatabase.CreateAsset(m, path);
            }
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Surface", 0f);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>Nova Scotia water: the Cabin/Water shader with its generated wave normal map.</summary>
        static Material WaterMaterial()
        {
            var shader = Shader.Find("Cabin/Water");
            var m = AssetDatabase.LoadAssetAtPath<Material>(WaterMaterialPath);
            if (m == null)
            {
                m = new Material(shader);
                AssetDatabase.CreateAsset(m, WaterMaterialPath);
            }
            m.shader = shader;
            m.SetTexture("_NormalMap", TerrainTextures.WaterWaves());
            // Shallows clearly water (see-through ones read as missing water at the edges).
            m.SetColor("_ShallowColor", new Color(0.16f, 0.32f, 0.33f, 0.72f));
            m.SetColor("_DeepColor", new Color(0.03f, 0.11f, 0.17f, 0.94f));
            m.SetFloat("_DeepAt", 2.5f);
            m.SetFloat("_FoamWidth", 0.07f);
            m.SetFloat("_FallSpeed", 3f);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        static void Renderable(Transform parent, string name, Mesh mesh, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.isStatic = true;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = material;
            mr.shadowCastingMode = ShadowCastingMode.Off;
        }

        /// <summary>
        /// The spawn: spiralling out from the spawn coordinates, the first dry, gentle spot within sight of a decent
        /// lake; failing that, any dry gentle spot.
        /// </summary>
        static Vector3 Spawn(Grid heights, Grid water, WaterNetwork net, MapProjection proj)
        {
            Vector2 p = proj.FromGeo(SpawnLongitude, SpawnLatitude);
            var lakes = new List<(Vector2 centre, float radius)>();
            foreach (var lake in net.Lakes)
            {
                if (lake.LengthMetres < 150f) continue;
                var c = Vector2.zero;
                foreach (int i in lake.Cells) c += new Vector2(net.Ground.X(i % net.Ground.W), net.Ground.Z(i / net.Ground.W));
                lakes.Add((c / lake.Cells.Count, lake.WidthMetres * 0.5f));
            }
            bool NearLake(float x, float z)
            {
                foreach (var (centre, radius) in lakes)
                    if ((centre - new Vector2(x, z)).magnitude < radius + 80f) return true;
                return false;
            }
            for (int pass = 0; pass < 2; pass++)
                for (float radius = 0f; radius < 2500f; radius += 8f)
                    for (int k = 0; k < 16; k++)
                    {
                        float a = k / 16f * Mathf.PI * 2f;
                        float x = p.x + Mathf.Cos(a) * radius, z = p.y + Mathf.Sin(a) * radius;
                        float h = heights.Sample(x, z);
                        int gx = Mathf.RoundToInt((x - heights.Origin.x) / heights.Cell), gz = Mathf.RoundToInt((z - heights.Origin.y) / heights.Cell);
                        float level = WaterNear(water, x, z, out _);
                        if (h > level + 1.5f && h > 2f && heights.Slope(gx, gz) < 12f && (pass == 1 || NearLake(x, z))) return new Vector3(x, h + 0.2f, z);
                    }
            return new Vector3(p.x, heights.Sample(p.x, p.y) + 2f, p.y);
        }
    }
}
