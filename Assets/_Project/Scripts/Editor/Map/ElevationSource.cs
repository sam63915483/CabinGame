using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Cabin.Map;
using UnityEditor;
using UnityEngine;

namespace Cabin.Editor.Map
{
    /// <summary>
    /// Real elevation for the map: AWS Terrain Tiles (open data, Terrarium PNG: metres = R·256 + G + B/256 − 32768)
    /// covering the recipe's area, downloaded once and cached in Library/CabinElevation (not in git — regenerating
    /// downloads them again). Samples by longitude/latitude, bilinear, in metres above sea level.
    /// </summary>
    public sealed class ElevationSource
    {
        const string Url = "https://s3.amazonaws.com/elevation-tiles-prod/terrarium/{0}/{1}/{2}.png";
        const int TilePixels = 256;
        static string CacheDir => Path.Combine(Application.dataPath, "..", "Library", "CabinElevation");

        readonly int zoom, x0, y0, width, height;
        readonly float[] metres;

        ElevationSource(int zoom, int x0, int y0, int tilesWide, int tilesHigh)
        {
            this.zoom = zoom;
            this.x0 = x0;
            this.y0 = y0;
            width = tilesWide * TilePixels;
            height = tilesHigh * TilePixels;
            metres = new float[width * height];
        }

        /// <summary>Loads (downloading what's missing) the tiles covering the recipe's map.</summary>
        public static ElevationSource Load(MapRecipe recipe)
        {
            var proj = new MapProjection(recipe);
            proj.GeoBounds(out double west, out double south, out double east, out double north);
            int z = recipe.zoom;
            int tx0 = (int)Math.Floor(TileX(west, z)), tx1 = (int)Math.Floor(TileX(east, z));
            int ty0 = (int)Math.Floor(TileY(north, z)), ty1 = (int)Math.Floor(TileY(south, z));
            var src = new ElevationSource(z, tx0, ty0, tx1 - tx0 + 1, ty1 - ty0 + 1);
            Directory.CreateDirectory(CacheDir);
            Download(z, tx0, tx1, ty0, ty1);
            var tex = new Texture2D(2, 2, TextureFormat.RGB24, false);
            try
            {
                for (int ty = ty0; ty <= ty1; ty++)
                for (int tx = tx0; tx <= tx1; tx++)
                {
                    EditorUtility.DisplayProgressBar("Elevation", $"Decoding tile {tx},{ty}", (float)((ty - ty0) * (tx1 - tx0 + 1) + tx - tx0) / ((ty1 - ty0 + 1) * (tx1 - tx0 + 1)));
                    if (!tex.LoadImage(File.ReadAllBytes(TilePath(z, tx, ty)))) throw new Exception($"Bad elevation tile {z}/{tx}/{ty}");
                    var px = tex.GetPixels32();
                    int ox = (tx - tx0) * TilePixels, oy = (ty - ty0) * TilePixels;
                    for (int j = 0; j < TilePixels; j++)
                    for (int i = 0; i < TilePixels; i++)
                    {
                        // Texture rows run bottom-up; tile rows run top-down.
                        var c = px[(TilePixels - 1 - j) * TilePixels + i];
                        src.metres[(oy + j) * src.width + ox + i] = c.r * 256f + c.g + c.b / 256f - 32768f;
                    }
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(tex);
                EditorUtility.ClearProgressBar();
            }
            return src;
        }

        static string TilePath(int z, int x, int y) => Path.Combine(CacheDir, $"{z}_{x}_{y}.png");

        static void Download(int z, int x0, int x1, int y0, int y1)
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            var pending = new System.Collections.Generic.List<Task>();
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                string path = TilePath(z, x, y);
                if (File.Exists(path) && new FileInfo(path).Length > 0) continue;
                string url = string.Format(Url, z, x, y);
                pending.Add(Task.Run(async () =>
                {
                    for (int attempt = 0; ; attempt++)
                    {
                        try
                        {
                            var bytes = await http.GetByteArrayAsync(url);
                            File.WriteAllBytes(path, bytes);
                            return;
                        }
                        catch when (attempt < 3) { await Task.Delay(500 * (attempt + 1)); }
                    }
                }));
                if (pending.Count >= 16)
                {
                    Task.WaitAll(pending.ToArray());
                    pending.Clear();
                }
            }
            Task.WaitAll(pending.ToArray());
        }

        // Web Mercator tile coordinates (fractional) for a longitude / latitude.
        public static double TileX(double lon, int z) => (lon + 180.0) / 360.0 * (1 << z);
        public static double TileY(double lat, int z)
        {
            double r = lat * Math.PI / 180.0;
            return (1.0 - Math.Log(Math.Tan(r) + 1.0 / Math.Cos(r)) / Math.PI) / 2.0 * (1 << z);
        }

        /// <summary>Real elevation (metres) at a longitude / latitude, bilinear between samples.</summary>
        public float Sample(double lon, double lat)
        {
            double fx = (TileX(lon, zoom) - x0) * TilePixels - 0.5, fy = (TileY(lat, zoom) - y0) * TilePixels - 0.5;
            int ix = (int)Math.Floor(fx), iy = (int)Math.Floor(fy);
            float tx = (float)(fx - ix), ty = (float)(fy - iy);
            float a = At(ix, iy), b = At(ix + 1, iy), c = At(ix, iy + 1), d = At(ix + 1, iy + 1);
            return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), ty);
        }

        /// <summary>
        /// Whether the source is flat here (all four neighbours within 0.15 m): lakes in this dataset are flattened to
        /// their surface, so large flat areas above sea level are lakes. Nearest sample.
        /// </summary>
        public bool Flat(double lon, double lat, out float level)
        {
            int ix = (int)Math.Floor((TileX(lon, zoom) - x0) * TilePixels), iy = (int)Math.Floor((TileY(lat, zoom) - y0) * TilePixels);
            level = At(ix, iy);
            const float eps = 0.15f;
            return Mathf.Abs(At(ix + 1, iy) - level) < eps && Mathf.Abs(At(ix - 1, iy) - level) < eps
                && Mathf.Abs(At(ix, iy + 1) - level) < eps && Mathf.Abs(At(ix, iy - 1) - level) < eps;
        }

        float At(int x, int y)
        {
            x = Mathf.Clamp(x, 0, width - 1);
            y = Mathf.Clamp(y, 0, height - 1);
            return metres[y * width + x];
        }
    }
}
