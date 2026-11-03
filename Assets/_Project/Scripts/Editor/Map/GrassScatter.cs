using System.Collections.Generic;
using System.Linq;
using Cabin.Map;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Cabin.Editor.Map
{
    /// <summary>
    /// Where the grass goes, written as the tiles' detail maps (the <see cref="Cabin.Forestry.GrassField"/> draws
    /// them; the terrain itself draws none): CartoonGrass tufts in full, dense patches — wide meadows on the
    /// grass ground, small thick clumps on the forest floor — with bare ground between, never a lone tuft; none on dirt,
    /// rock or sand. Where it goes is read off each tile's paint and two noise fields. The pack's grass prefabs are
    /// LOD groups and its materials don't instance, so plain single-mesh prefabs with materials on our own
    /// Cabin/Grass shader (tinted per tuft by the ground beneath) are generated into Prefabs/Grass.
    /// </summary>
    public static class GrassScatter
    {
        const string Dir = "Assets/_Project/Prefabs/Grass";
        static readonly string[] Sources =
        {
            "Assets/External/CartoonGrass/Prefabs/Grass_01.prefab",
            "Assets/External/CartoonGrass/Prefabs/Grass_02.prefab",
            "Assets/External/CartoonGrass/Prefabs/Grass_03.prefab",
        };
        /// <summary>Detail cells per tile edge (2 m cells on a 1024 m tile).</summary>
        const int Resolution = 512;
        /// <summary>Tufts per cell inside a patch: a meadow, a forest clump.</summary>
        const int Meadow = 16, Clump = 12;
        /// <summary>Tufts per cell on open grass between the meadows: thin cover rather than bare ground.</summary>
        const int Cover = 10; // dense everywhere it is grassy, never thin; fewer, bigger tufts than before (GPU cost)
        /// <summary>No grass on ground steeper than this (degrees): tufts on a steep face look wrong.</summary>
        const float MaxSlope = 27f;
        /// <summary>The grass's look, in one place: how dark the tufts are at the root (of their colour), how small a tuft gets at a patch's fringe (of full size), tufts per cell that count as a full patch, and how many cells each way a tuft judges its patch by.</summary>
        public const float RootShade = 0.55f, FringeSize = 0.4f;
        public const int FullCell = 12, FullnessWindow = 3;
        /// <summary>How far a tuft leans over to stand square on the ground under it (0 straight up, 1 along the ground's normal).</summary>
        public const float SlopeFollow = 1f;
        const string TerrainMaterialPath = "Assets/_Project/Art/Materials/Terrain.mat";
        /// <summary>Sizes (metres) of the meadow pattern and of the small clumps.</summary>
        const float MeadowScale = 75f, ClumpScale = 9f;

        public static void Apply(MapRecipe r, TerrainData[,] datas, Grid heights, Grid water)
        {
            var protos = Prototypes();
            var meadows = new Noise(r.seed + 401);
            var clumps = new Noise(r.seed + 402);
            for (int tz = 0; tz < r.tilesZ; tz++)
            for (int tx = 0; tx < r.tilesX; tx++)
            {
                var data = datas[tx, tz];
                data.detailPrototypes = protos;
                data.SetDetailScatterMode(DetailScatterMode.InstanceCountMode);
                data.SetDetailResolution(Resolution, 16);
                var splat = data.GetAlphamaps(0, 0, data.alphamapWidth, data.alphamapHeight);
                var rng = new System.Random(r.seed * 31 + tz * 131 + tx);
                var layers = new int[protos.Length][,];
                for (int k = 0; k < protos.Length; k++) layers[k] = new int[Resolution, Resolution];
                for (int y = 0; y < Resolution; y++)
                for (int x = 0; x < Resolution; x++)
                {
                    int ay = Mathf.Min(data.alphamapHeight - 1, y * data.alphamapHeight / Resolution);
                    int ax = Mathf.Min(data.alphamapWidth - 1, x * data.alphamapWidth / Resolution);
                    float grass = splat[ay, ax, TerrainPainter.Grass] + splat[ay, ax, TerrainPainter.Meadow];
                    float forest = splat[ay, ax, TerrainPainter.ForestFloor] + splat[ay, ax, TerrainPainter.Moss];
                    float wx = r.Origin.x + (tx + (x + 0.5f) / Resolution) * r.tileSize;
                    float wz = r.Origin.z + (tz + (y + 0.5f) / Resolution) * r.tileSize;
                    // None in or at the edge of water (the paint is coarser than the grass).
                    if (heights.Sample(wx, wz) < MapGenerator.WaterNear(water, wx, wz, out _) + 0.35f) continue;
                    int gx = Mathf.Clamp(Mathf.RoundToInt((wx - heights.Origin.x) / heights.Cell), 0, heights.W - 1);
                    int gz = Mathf.Clamp(Mathf.RoundToInt((wz - heights.Origin.y) / heights.Cell), 0, heights.H - 1);
                    if (heights.Slope(gx, gz) > MaxSlope) continue;
                    // Meadows: most of the open grass, broken into fields. Clumps: small and tight, mostly in the woods.
                    float m = meadows.Fbm(wx / MeadowScale, wz / MeadowScale, 3) + 0.12f * clumps.At(wx / ClumpScale, wz / ClumpScale);
                    float c = clumps.Fbm(wx / ClumpScale, wz / ClumpScale, 2);
                    float inMeadow = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.36f, -0.14f, m));
                    float inClump = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.05f, 0.18f, c));
                    // Open ground: dense all over, denser still in the meadows. Woods: tight clumps.
                    float count = grass * Mathf.Max(Cover, inMeadow * Meadow) + forest * inClump * Clump;
                    // A cell with only a tuft or two is a patch's fringe: kept, and drawn small there (GrassField.Fullness).
                    int n = Mathf.FloorToInt(count + (float)rng.NextDouble());
                    for (int i = 0; i < n; i++) layers[rng.Next(protos.Length)][y, x]++;
                }
                for (int k = 0; k < protos.Length; k++) data.SetDetailLayer(0, 0, k, layers[k]);
            }
        }

        /// <summary>Hands the field renderer each species' meshes (the middle LOD near, the lightest far) and material.</summary>
        public static void Wire(Cabin.Forestry.GrassField field)
        {
            var near = new Mesh[Sources.Length];
            var far = new Mesh[Sources.Length];
            var mats = new Material[Sources.Length];
            for (int i = 0; i < Sources.Length; i++)
            {
                var source = AssetDatabase.LoadAssetAtPath<GameObject>(Sources[i]);
                var byDetail = source.GetComponentsInChildren<MeshFilter>().Where(f => f.sharedMesh != null).OrderBy(f => f.sharedMesh.vertexCount).Select(f => f.sharedMesh).ToArray();
                far[i] = byDetail[0];
                near[i] = byDetail[Mathf.Min(1, byDetail.Length - 1)];
                mats[i] = AssetDatabase.LoadAssetAtPath<Material>($"{Dir}/{System.IO.Path.GetFileNameWithoutExtension(Sources[i])}.mat");
            }
            field.Set(near, far, mats, LayerTints(), FringeSize, FullCell, FullnessWindow, SlopeFollow);
        }

        static readonly string[] LayerNames = { "Grass", "ForestFloor", "Dirt", "Rock", "Sand", "Meadow", "Moss", "Gravel" };

        /// <summary>
        /// What the tufts are tinted on each ground (the painter's layer order), read off the ground textures
        /// themselves: each layer's mean colour over the grass layer's, so a tuft's mid-tone is the ground's — golden
        /// on dry meadow, dark in the forest floor's litter. On bare ground (rock, sand, gravel) only the brightness
        /// follows, not the hue, so a stray tuft on a ledge is a dull green rather than a grey one.
        /// </summary>
        public static Color[] LayerTints()
        {
            var tints = new Color[TerrainPainter.Layers];
            var grass = MeanLinear(LayerNames[TerrainPainter.Grass]);
            for (int k = 0; k < tints.Length; k++)
            {
                var mean = MeanLinear(LayerNames[k]);
                var tint = new Color(mean.r / Mathf.Max(grass.r, 1e-3f), mean.g / Mathf.Max(grass.g, 1e-3f), mean.b / Mathf.Max(grass.b, 1e-3f));
                if (k == TerrainPainter.Rock || k == TerrainPainter.Sand || k == TerrainPainter.Gravel)
                {
                    float l = Luminance(mean) / Mathf.Max(Luminance(grass), 1e-3f);
                    tint = new Color(l, l, l);
                }
                tints[k] = new Color(Mathf.Clamp(tint.r, 0.2f, 1.8f), Mathf.Clamp(tint.g, 0.2f, 1.8f), Mathf.Clamp(tint.b, 0.2f, 1.8f), 1f);
            }
            return tints;
        }

        /// <summary>The tufts' colours from the ground's grass texture: the base its darker third, the tips its brighter third (a touch lighter still, as tips catch the light). sRGB, for a material colour.</summary>
        public static void Palette(out Color bottom, out Color top)
        {
            var px = Pixels(LayerNames[TerrainPainter.Grass]);
            if (px == null) { bottom = new Color(0.3f, 0.44f, 0.15f); top = new Color(0.56f, 0.74f, 0.2f); return; }
            var byLight = px.OrderBy(Luminance).ToArray();
            int third = Mathf.Max(1, byLight.Length / 3);
            bottom = Mean(byLight.Take(third));
            top = Mean(byLight.Skip(byLight.Length - third)) * 1.08f;
            bottom.a = top.a = 1f;
        }

        static float Luminance(Color c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;

        static Color Mean(IEnumerable<Color> colours)
        {
            var sum = Color.black;
            int n = 0;
            foreach (var c in colours) { sum += c; n++; }
            return n == 0 ? Color.white : sum / n;
        }

        /// <summary>A layer's mean colour in linear light (its texture, read off disk: the import need not be readable).</summary>
        static Color MeanLinear(string layer)
        {
            var px = Pixels(layer);
            if (px == null) return Color.white;
            var sum = Color.black;
            foreach (var c in px) sum += c.linear;
            return sum / px.Length;
        }

        /// <summary>Every 4th texel of a ground texture's PNG, as stored (sRGB), or null without the file.</summary>
        static Color[] Pixels(string layer)
        {
            string path = $"Assets/_Project/Art/Terrain/{layer}.png";
            if (!System.IO.File.Exists(path)) { Debug.LogWarning($"[Cabin] no ground texture at {path}; the grass keeps its default colours"); return null; }
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            tex.LoadImage(System.IO.File.ReadAllBytes(path));
            var all = tex.GetPixels();
            Object.DestroyImmediate(tex);
            var px = new Color[(all.Length + 3) / 4];
            for (int i = 0; i < px.Length; i++) px[i] = all[i * 4];
            return px;
        }

        static DetailPrototype[] Prototypes()
        {
            SandboxAssetGenerator.EnsureFolder(Dir);
            var protos = new DetailPrototype[Sources.Length];
            for (int i = 0; i < Sources.Length; i++)
            {
                var source = AssetDatabase.LoadAssetAtPath<GameObject>(Sources[i]);
                // The middle LOD mesh: half the triangles of the full one, but blades still segmented enough to bend in the
                // wind (the lightest LOD sheared its long single-quad blades into stretched streaks).
                var lod0 = source.GetComponentsInChildren<MeshFilter>().Where(f => f.sharedMesh != null).OrderBy(f => f.sharedMesh.vertexCount).Skip(1).First();
                // Always the full-detail LOD's material: the lighter LODs carry materials on another shader that draws as
                // giant dark streaks under the terrain's detail renderer.
                var srcMat = source.GetComponentsInChildren<MeshFilter>().Where(f => f.sharedMesh != null).OrderByDescending(f => f.sharedMesh.vertexCount).First().GetComponent<MeshRenderer>().sharedMaterial;
                string name = System.IO.Path.GetFileNameWithoutExtension(Sources[i]);

                // Our own shader (Cabin/Grass: the pack's gradient and wind, plus a tint per tuft from the ground),
                // with the pack's gradient and wind numbers and colours pulled a little toward the ground's grass.
                string matPath = $"{Dir}/{name}.mat";
                var shader = Shader.Find("Cabin/Grass");
                var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                if (mat == null)
                {
                    mat = new Material(shader);
                    AssetDatabase.CreateAsset(mat, matPath);
                }
                mat.shader = shader;
                foreach (var f in new[] { "GradientHeight", "GradientSmoothness", "WindSpeed", "WindStrength" })
                    if (srcMat.HasFloat(f)) mat.SetFloat(f, srcMat.GetFloat(f));
                Palette(out var bottom, out var top);
                mat.SetColor("BottomColor", bottom);
                mat.SetColor("TopColor", top);
                mat.SetVector("WindDirection", new Vector4(1f, 0f, 0.3f, 0f));
                mat.SetFloat("_Smoothness", 0f); // no glint: it flared every blade when looking toward the sun
                mat.SetFloat("_RootShade", RootShade);
                var ground = AssetDatabase.LoadAssetAtPath<Material>(TerrainMaterialPath);
                if (ground != null)
                {
                    // The terrain's macro drift, so the grass darkens and warms with the hillside it stands on.
                    mat.SetTexture("_MacroTex", ground.GetTexture("_MacroTex"));
                    foreach (var f in new[] { "_MacroScale", "_MacroStrength", "_MacroWarmth" }) mat.SetFloat(f, ground.GetFloat(f));
                }
                mat.enableInstancing = true;
                EditorUtility.SetDirty(mat);

                var go = new GameObject(name);
                go.AddComponent<MeshFilter>().sharedMesh = lod0.sharedMesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = mat;
                mr.shadowCastingMode = ShadowCastingMode.Off;
                var prefab = PrefabUtility.SaveAsPrefabAsset(go, $"{Dir}/{name}.prefab");
                Object.DestroyImmediate(go);

                protos[i] = new DetailPrototype
                {
                    prototype = prefab,
                    usePrototypeMesh = true,
                    useInstancing = true,
                    renderMode = DetailRenderMode.VertexLit,
                    minWidth = 1.2f, maxWidth = 1.9f, minHeight = 1f, maxHeight = 1.6f,
                    noiseSpread = 0.3f,
                    healthyColor = Color.white,
                    dryColor = new Color(0.85f, 0.85f, 0.7f),
                    density = 1f,
                };
            }
            return protos;
        }
    }
}
