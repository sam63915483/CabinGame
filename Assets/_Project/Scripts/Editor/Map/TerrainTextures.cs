using System;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Cabin.Editor.Map
{
    /// <summary>
    /// The ground's textures, made here (the packs' terrain textures are near-white tiles tinted one flat colour each):
    /// lush grass, dry meadow grass, forest floor, moss, dirt, gravel, sand and rock, each a tileable colour texture
    /// with real detail — blades, needles, pebbles, strata — its height in the alpha (for the shader's height blend)
    /// and a normal map from the same height, as terrain layers in Art/Terrain; plus the macro noise the shader
    /// drifts the ground's colour with. Stylised to sit with the low-poly trees, not photographic. Seeded; re-runnable.
    /// </summary>
    public static class TerrainTextures
    {
        const string Dir = "Assets/_Project/Art/Terrain";
        const int Size = 512;

        /// <summary>One texture being made: colour and height per texel, wrapping at the edges.</summary>
        sealed class Canvas
        {
            public readonly Color[] Color = new Color[Size * Size];
            public readonly float[] Height = new float[Size * Size];
            public static int Wrap(int i) => ((i % Size) + Size) % Size;
            public int Index(int x, int y) => Wrap(y) * Size + Wrap(x);
        }

        /// <summary>
        /// Builds all the layers (in <see cref="TerrainPainter"/>'s order) and returns them. Each is a tileable texture of
        /// <see cref="Size"/>² over the given number of metres.
        /// </summary>
        public static TerrainLayer[] Build(int seed)
        {
            SandboxAssetGenerator.EnsureFolder(Dir);
            var layers = new TerrainLayer[TerrainPainter.Layers];
            // Each layer's height (the colour's alpha) sits in its own band so the Cabin/Terrain height blend knows
            // what pokes through what: grass blades over earth, stones over sand, rock over everything.
            layers[TerrainPainter.Grass] = Layer("Grass", seed + 1, 6f, 0.35f, 0.35f, 0.45f, c => Grass(c, seed + 1, new Color(0.26f, 0.45f, 0.12f), new Color(0.38f, 0.58f, 0.17f), new Color(0.52f, 0.70f, 0.24f)));
            layers[TerrainPainter.Meadow] = Layer("Meadow", seed + 2, 6f, 0.35f, 0.35f, 0.45f, c => Grass(c, seed + 2, new Color(0.42f, 0.42f, 0.16f), new Color(0.58f, 0.54f, 0.24f), new Color(0.72f, 0.66f, 0.36f)));
            layers[TerrainPainter.ForestFloor] = Layer("ForestFloor", seed + 3, 5f, 0.4f, 0.2f, 0.4f, c => ForestFloor(c, seed + 3));
            layers[TerrainPainter.Moss] = Layer("Moss", seed + 4, 5f, 0.35f, 0.3f, 0.35f, c => Moss(c, seed + 4));
            layers[TerrainPainter.Dirt] = Layer("Dirt", seed + 5, 5f, 0.5f, 0.1f, 0.3f, c => Pebbly(c, seed + 5, new Color(0.42f, 0.33f, 0.22f), new Color(0.53f, 0.43f, 0.29f), 220, 1.5f, 3.5f));
            layers[TerrainPainter.Gravel] = Layer("Gravel", seed + 6, 3f, 0.8f, 0.25f, 0.5f, c => Pebbly(c, seed + 6, new Color(0.38f, 0.36f, 0.32f), new Color(0.48f, 0.45f, 0.40f), 2600, 2.5f, 6f));
            layers[TerrainPainter.Sand] = Layer("Sand", seed + 7, 4f, 0.5f, 0.15f, 0.25f, c => Sand(c, seed + 7));
            layers[TerrainPainter.Rock] = Layer("Rock", seed + 8, 8f, 1.4f, 0.3f, 0.55f, c => Rock(c, seed + 8));
            AssetDatabase.SaveAssets();
            return layers;
        }

        /// <summary>
        /// The macro variation: a tileable fractal noise the terrain shader lays over the world at a few hundred
        /// metres a repeat to drift the ground's brightness and warmth, so no hillside is one flat tone. Linear.
        /// </summary>
        public static Texture2D Macro()
        {
            SandboxAssetGenerator.EnsureFolder(Dir);
            string path = $"{Dir}/Macro.png";
            var noise = new Periodic(9007);
            var px = new Color[Size * Size];
            Parallel.For(0, Size, y =>
            {
                for (int x = 0; x < Size; x++)
                {
                    float n = noise.Fbm(x / (float)Size, y / (float)Size, 3, 5) * 0.5f + 0.5f;
                    px[y * Size + x] = new Color(n, n, n, 1f);
                }
            });
            Save(path, px, false);
            Import(path, false, linear: true);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>
        /// The water's waves: a tileable normal map of soft, rounded ripples (several sizes of value noise), for the
        /// Cabin/Water shader to drift across itself.
        /// </summary>
        public static Texture2D WaterWaves()
        {
            SandboxAssetGenerator.EnsureFolder(Dir);
            string path = $"{Dir}/WaterWaves_Normal.png";
            var noise = new Periodic(9001);
            var height = new float[Size * Size];
            Parallel.For(0, Size, y =>
            {
                for (int x = 0; x < Size; x++)
                {
                    float u = x / (float)Size, v = y / (float)Size;
                    // Ridged a little: wave crests sharper than troughs.
                    float n = noise.Fbm(u, v, 4, 4) * 0.7f + noise.Fbm(u + 0.3f, v + 0.6f, 12, 2) * 0.3f;
                    height[y * Size + x] = 1f - Mathf.Abs(n) * 1.4f;
                }
            });
            Save(path, Normals(height, 6f), false);
            Import(path, true);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static TerrainLayer Layer(string name, int seed, float metres, float bump, float heightBase, float heightRange, Action<Canvas> paint)
        {
            var canvas = new Canvas();
            paint(canvas);
            string colourPath = $"{Dir}/{name}.png", normalPath = $"{Dir}/{name}_Normal.png";
            Save(colourPath, WithHeight(canvas, heightBase, heightRange), true);
            Save(normalPath, Normals(canvas.Height, bump), false);
            Import(colourPath, false);
            Import(normalPath, true);

            string layerPath = $"{Dir}/{name}.terrainlayer";
            var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(layerPath);
            if (layer == null)
            {
                layer = new TerrainLayer();
                AssetDatabase.CreateAsset(layer, layerPath);
            }
            layer.diffuseTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(colourPath);
            layer.normalMapTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath);
            layer.normalScale = 1f;
            layer.tileSize = new Vector2(metres, metres);
            layer.tileOffset = Vector2.zero;
            layer.smoothness = 0f;
            layer.metallic = 0f;
            layer.diffuseRemapMin = Vector4.zero;
            layer.diffuseRemapMax = Vector4.one;
            EditorUtility.SetDirty(layer);
            return layer;
        }

        /// <summary>The colour with the height in its alpha: the canvas's height range mapped to base … base + range.</summary>
        static Color[] WithHeight(Canvas c, float heightBase, float heightRange)
        {
            float lo = float.MaxValue, hi = float.MinValue;
            foreach (float h in c.Height) { lo = Mathf.Min(lo, h); hi = Mathf.Max(hi, h); }
            float span = Mathf.Max(hi - lo, 1e-4f);
            var px = new Color[c.Color.Length];
            for (int i = 0; i < px.Length; i++)
            {
                var col = c.Color[i];
                col.a = Mathf.Clamp01(heightBase + heightRange * (c.Height[i] - lo) / span);
                px[i] = col;
            }
            return px;
        }

        // ------------------------------------------------------------------ painting

        static void Grass(Canvas c, int seed, Color dark, Color mid, Color light)
        {
            Base(c, seed, (u, v, n1, n2) =>
            {
                var col = Color.Lerp(dark, mid, Mathf.SmoothStep(0f, 1f, n1 * 0.5f + 0.5f));
                return Color.Lerp(col, dark * 0.85f, Mathf.Clamp01(n2 * 1.5f) * 0.5f);
            }, 0.15f);
            // Blades: short strokes leaning every way, light and dark, raised.
            var rng = new System.Random(seed);
            for (int i = 0; i < 2600; i++)
            {
                float x = rng.Next(Size), y = rng.Next(Size), a = (float)(rng.NextDouble() * Math.PI * 2.0);
                float len = 5f + (float)rng.NextDouble() * 9f;
                float t = (float)rng.NextDouble();
                var col = t < 0.45f ? Color.Lerp(mid, light, (float)rng.NextDouble()) : Color.Lerp(dark * 0.8f, mid, (float)rng.NextDouble());
                Stroke(c, x, y, Mathf.Cos(a), Mathf.Sin(a), len, 1.4f, col, 0.6f + t * 0.4f, 0.35f);
            }
        }

        static void ForestFloor(Canvas c, int seed)
        {
            var earth = new Color(0.21f, 0.16f, 0.10f);
            var litter = new Color(0.31f, 0.23f, 0.14f);
            var moss = new Color(0.24f, 0.32f, 0.13f);
            Base(c, seed, (u, v, n1, n2) =>
            {
                var col = Color.Lerp(earth, litter, n1 * 0.5f + 0.5f);
                return Color.Lerp(col, moss, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.25f, 0.55f, n2)) * 0.7f);
            }, 0.2f);
            var rng = new System.Random(seed);
            // Needles: thin, short, rusty and dark, every which way.
            for (int i = 0; i < 4000; i++)
            {
                float a = (float)(rng.NextDouble() * Math.PI * 2.0);
                float t = (float)rng.NextDouble();
                var col = t < 0.5f ? new Color(0.48f, 0.30f, 0.16f) : t < 0.8f ? new Color(0.16f, 0.12f, 0.08f) : new Color(0.55f, 0.42f, 0.24f);
                Stroke(c, rng.Next(Size), rng.Next(Size), Mathf.Cos(a), Mathf.Sin(a), 6f + (float)rng.NextDouble() * 6f, 0.9f, col, 0.4f, 0.45f);
            }
            // Fallen leaves and cones: small blobs.
            for (int i = 0; i < 180; i++)
            {
                float t = (float)rng.NextDouble();
                var col = t < 0.5f ? new Color(0.50f, 0.33f, 0.14f) : new Color(0.38f, 0.22f, 0.10f);
                Blob(c, rng.Next(Size), rng.Next(Size), 2f + (float)rng.NextDouble() * 3f, col, 0.5f);
            }
        }

        static void Moss(Canvas c, int seed)
        {
            Base(c, seed, (u, v, n1, n2) =>
            {
                var col = Color.Lerp(new Color(0.15f, 0.26f, 0.08f), new Color(0.30f, 0.42f, 0.13f), n1 * 0.5f + 0.5f);
                return Color.Lerp(col, new Color(0.40f, 0.50f, 0.18f), Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.3f, 0.6f, n2)) * 0.6f);
            }, 0.6f);
            var rng = new System.Random(seed);
            for (int i = 0; i < 900; i++)
                Blob(c, rng.Next(Size), rng.Next(Size), 1.5f + (float)rng.NextDouble() * 3f,
                    Color.Lerp(new Color(0.18f, 0.30f, 0.09f), new Color(0.38f, 0.50f, 0.17f), (float)rng.NextDouble()), 0.6f);
        }

        /// <summary>Earth with stones in it: <paramref name="stones"/> of them, <paramref name="minR"/>–<paramref name="maxR"/> texels.</summary>
        static void Pebbly(Canvas c, int seed, Color dark, Color light, int stones, float minR, float maxR)
        {
            Base(c, seed, (u, v, n1, n2) => Color.Lerp(dark, light, Mathf.Clamp01(n1 * 0.5f + 0.5f + n2 * 0.2f)), 0.3f);
            var rng = new System.Random(seed);
            for (int i = 0; i < stones; i++)
            {
                float grey = 0.32f + (float)rng.NextDouble() * 0.3f;
                var col = new Color(grey * 1.05f, grey, grey * 0.92f);
                Blob(c, rng.Next(Size), rng.Next(Size), minR + (float)rng.NextDouble() * (maxR - minR), col, 1f);
            }
        }

        static void Sand(Canvas c, int seed)
        {
            var noise = new Periodic(seed);
            Parallel.For(0, Size, y =>
            {
                for (int x = 0; x < Size; x++)
                {
                    float u = x / (float)Size, v = y / (float)Size;
                    float n = noise.Fbm(u, v, 4, 4);
                    // Ripples, wavy.
                    float ripple = Mathf.Sin((v * 14f + noise.Fbm(u, v, 2, 2) * 0.6f) * Mathf.PI * 2f);
                    float grain = Periodic.Hash01(x, y, seed);
                    var col = Color.Lerp(new Color(0.66f, 0.58f, 0.42f), new Color(0.80f, 0.73f, 0.55f), n * 0.5f + 0.5f);
                    col *= 0.94f + grain * 0.12f;
                    col.a = 1f;
                    c.Color[y * Size + x] = col;
                    c.Height[y * Size + x] = ripple * 0.3f + n * 0.3f + grain * 0.15f;
                }
            });
        }

        /// <summary>
        /// Granite, the way Firewatch paints a cliff: broad faces of warm grey, soft strata, a fine speckle and pale
        /// lichen in patches — no cracks at all: any web of fissures, however thin, reads as a tortoise shell from a
        /// hillside away. Projected from three sides by the shader, so it tiles at 8 m with no stretch.
        /// </summary>
        static void Rock(Canvas c, int seed)
        {
            var noise = new Periodic(seed);
            var pale = new Color(0.58f, 0.56f, 0.52f);
            var dark = new Color(0.38f, 0.37f, 0.35f);
            var lichenCol = new Color(0.58f, 0.58f, 0.40f);
            Parallel.For(0, Size, y =>
            {
                for (int x = 0; x < Size; x++)
                {
                    float u = x / (float)Size, v = y / (float)Size;
                    float warp = noise.Fbm(u, v, 3, 3);
                    // Strata: faint bands across, wavy.
                    float band = Mathf.Sin((v * 5f + warp * 0.7f) * Mathf.PI * 2f) * 0.5f + 0.5f;
                    float n = noise.Fbm(u, v, 6, 4);
                    float fine = noise.Fbm(u + 0.41f, v + 0.13f, 24, 2);
                    float lichen = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.52f, 0.68f, noise.Fbm(u + 0.7f, v + 0.2f, 8, 2) * 0.5f + 0.5f));
                    var col = Color.Lerp(dark, pale, Mathf.Clamp01(0.5f + band * 0.2f + n * 0.55f + fine * 0.2f));
                    col *= 0.95f + Periodic.Hash01(x, y, seed) * 0.1f;
                    col = Color.Lerp(col, lichenCol, lichen * 0.45f);
                    col.a = 1f;
                    c.Color[y * Size + x] = col;
                    c.Height[y * Size + x] = band * 0.35f + n * 1.0f + fine * 0.25f;
                }
            });
        }

        // ------------------------------------------------------------------ drawing

        /// <summary>A ground colour from two noise fields (a broad one and a blotchy one); height from the first.</summary>
        static void Base(Canvas c, int seed, Func<float, float, float, float, Color> colour, float relief)
        {
            var noise = new Periodic(seed);
            Parallel.For(0, Size, y =>
            {
                for (int x = 0; x < Size; x++)
                {
                    float u = x / (float)Size, v = y / (float)Size;
                    float n1 = noise.Fbm(u, v, 4, 4), n2 = noise.Fbm(u + 0.5f, v + 0.25f, 8, 3);
                    var col = colour(u, v, n1, n2);
                    col *= 0.95f + Periodic.Hash01(x, y, seed) * 0.1f;
                    col.a = 1f;
                    c.Color[y * Size + x] = col;
                    c.Height[y * Size + x] = n1 * relief;
                }
            });
        }

        /// <summary>A short line from (x, y) along (dx, dy): coloured, raised, tapering.</summary>
        static void Stroke(Canvas c, float x, float y, float dx, float dy, float length, float width, Color col, float height, float alpha)
        {
            int steps = Mathf.CeilToInt(length * 2f);
            for (int s = 0; s <= steps; s++)
            {
                float t = s / (float)steps;
                float px = x + dx * length * t, py = y + dy * length * t;
                float w = width * (1f - t * 0.6f);
                int r = Mathf.CeilToInt(w);
                for (int oy = -r; oy <= r; oy++)
                for (int ox = -r; ox <= r; ox++)
                {
                    float d = Mathf.Sqrt(ox * ox + oy * oy);
                    if (d > w) continue;
                    int i = c.Index(Mathf.RoundToInt(px) + ox, Mathf.RoundToInt(py) + oy);
                    float a = alpha * (1f - d / (w + 0.5f));
                    c.Color[i] = Color.Lerp(c.Color[i], col, a);
                    c.Height[i] = Mathf.Max(c.Height[i], height * (1f - t * 0.5f));
                }
            }
        }

        /// <summary>A rounded lump: a pebble, a leaf, a moss cushion.</summary>
        static void Blob(Canvas c, float x, float y, float radius, Color col, float height)
        {
            int r = Mathf.CeilToInt(radius);
            for (int oy = -r; oy <= r; oy++)
            for (int ox = -r; ox <= r; ox++)
            {
                float d = Mathf.Sqrt(ox * ox + oy * oy) / radius;
                if (d > 1f) continue;
                int i = c.Index(Mathf.RoundToInt(x) + ox, Mathf.RoundToInt(y) + oy);
                float dome = Mathf.Sqrt(1f - d * d);
                // Lit from the top-left a touch, darker underneath: reads as a lump even without the normal map.
                float shade = 1f + 0.18f * (-ox - oy) / (radius * 1.4f);
                var lit = col * shade;
                lit.a = 1f;
                c.Color[i] = Color.Lerp(c.Color[i], lit, Mathf.SmoothStep(1f, 0.7f, d));
                c.Height[i] = Mathf.Max(c.Height[i], height * dome);
            }
        }

        static Color[] Normals(float[] height, float bump)
        {
            var px = new Color[Size * Size];
            Parallel.For(0, Size, y =>
            {
                for (int x = 0; x < Size; x++)
                {
                    float hl = height[y * Size + Canvas.Wrap(x - 1)], hr = height[y * Size + Canvas.Wrap(x + 1)];
                    float hd = height[Canvas.Wrap(y - 1) * Size + x], hu = height[Canvas.Wrap(y + 1) * Size + x];
                    var n = new Vector3((hl - hr) * bump, (hd - hu) * bump, 1f).normalized;
                    px[y * Size + x] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f);
                }
            });
            return px;
        }

        static void Save(string path, Color[] pixels, bool alpha)
        {
            var tex = new Texture2D(Size, Size, alpha ? TextureFormat.RGBA32 : TextureFormat.RGB24, false);
            tex.SetPixels(pixels);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
        }

        static void Import(string path, bool normal, bool linear = false)
        {
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.sRGBTexture = !normal && !linear;
            importer.alphaIsTransparency = false;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.mipmapEnabled = true;
            importer.anisoLevel = 4;
            importer.maxTextureSize = Size;
            importer.textureCompression = TextureImporterCompression.Compressed;
            importer.SaveAndReimport();
        }

        /// <summary>Value noise that wraps: the lattice repeats every <c>period</c> cells across the texture.</summary>
        readonly struct Periodic
        {
            readonly int seed;
            public Periodic(int seed) => this.seed = seed;

            public static float Hash01(int x, int y, int seed)
            {
                unchecked
                {
                    uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1442695041);
                    h = (h ^ (h >> 13)) * 1274126177u;
                    return ((h ^ (h >> 16)) & 0xFFFFFF) / (float)0xFFFFFF;
                }
            }

            float At(float u, float v, int period, int salt)
            {
                float x = u * period, y = v * period;
                int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
                float fx = x - x0, fy = y - y0;
                float sx = fx * fx * (3f - 2f * fx), sy = fy * fy * (3f - 2f * fy);
                int W(int i) => ((i % period) + period) % period;
                float a = Hash01(W(x0), W(y0), seed + salt), b = Hash01(W(x0 + 1), W(y0), seed + salt);
                float c = Hash01(W(x0), W(y0 + 1), seed + salt), d = Hash01(W(x0 + 1), W(y0 + 1), seed + salt);
                return Mathf.Lerp(Mathf.Lerp(a, b, sx), Mathf.Lerp(c, d, sx), sy) * 2f - 1f;
            }

            /// <summary>Fractal sum, −1…1, starting at <paramref name="period"/> cells across and doubling.</summary>
            public float Fbm(float u, float v, int period, int octaves)
            {
                float sum = 0f, amp = 0.5f, norm = 0f;
                for (int i = 0; i < octaves; i++)
                {
                    sum += At(u, v, period << i, i * 101) * amp;
                    norm += amp;
                    amp *= 0.5f;
                }
                return sum / norm;
            }
        }
    }
}
