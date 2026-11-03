using System.IO;
using System.Text;
using Cabin.Map;
using UnityEditor;
using UnityEngine;

namespace Cabin.Editor.Map
{
    /// <summary>
    /// A picture of the generated map to judge it by without playing: the land hillshaded and tinted by height, the sea
    /// dark, lakes and rivers blue, waterfalls red, the spawn yellow. Written to Temp/map_preview.png by Generate
    /// World, with the numbers (lakes by size, rivers, falls, how much of the land is walkable) in Temp/map_stats.txt.
    /// Tools/Cabin/Map/Preview PNG redraws it from the World scene's terrain tiles and water meshes.
    /// </summary>
    public static class MapPreview
    {
        public const string ImagePath = "Temp/map_preview.png";
        public const string StatsPath = "Temp/map_stats.txt";
        /// <summary>Fine cells per pixel.</summary>
        const int Step = 4;

        public static void Write(MapRecipe r, FineTerrain fine, WaterNetwork net, Vector3 spawn, double seconds)
        {
            var heights = fine.Heights;
            int pw = (heights.W - 1) / Step, ph = (heights.H - 1) / Step;
            var tex = new Texture2D(pw, ph, TextureFormat.RGB24, false);
            var px = new Color32[pw * ph];
            int land = 0, walkable = 0;
            for (int j = 0; j < ph; j++)
            for (int i = 0; i < pw; i++)
            {
                int x = i * Step, z = j * Step;
                float h = heights[x, z];
                Color c;
                if (!float.IsNaN(fine.Water[x, z])) c = new Color(0.2f, 0.5f, 0.9f);
                else if (h <= 0f) c = new Color(0.05f, 0.12f, 0.25f);
                else
                {
                    land++;
                    float slope = heights.Slope(x, z);
                    if (slope < 25f) walkable++;
                    c = Shade(heights, x, z, h);
                }
                px[j * pw + i] = c;
            }
            foreach (var river in net.Rivers)
                foreach (var fall in river.Falls) Dot(px, pw, ph, heights, fall.Lip, new Color32(255, 40, 40, 255), 2);
            Dot(px, pw, ph, heights, new Vector2(spawn.x, spawn.z), new Color32(255, 230, 40, 255), 3);
            tex.SetPixels32(px);
            tex.Apply();
            Directory.CreateDirectory("Temp");
            File.WriteAllBytes(ImagePath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);

            var sb = new StringBuilder();
            sb.AppendLine($"Generated in {seconds:F0} s, seed {r.seed}");
            sb.AppendLine(net.Summary());
            sb.AppendLine($"Land under 25° slope: {100f * walkable / Mathf.Max(1, land):F0} %");
            foreach (var river in net.Rivers)
                foreach (var fall in river.Falls) sb.AppendLine($"  fall {fall.Height:F1} m at ({fall.Lip.x:F0}, {fall.Lip.y:F0}), river {river.Width:F0} m wide");
            int big = 0;
            foreach (var lake in net.Lakes) if (lake.LengthMetres >= 300f) sb.AppendLine($"  lake {lake.LengthMetres:F0} m long, {lake.WidthMetres:F0} m wide if round, at level {lake.Level:F1}, {++big}");
            File.WriteAllText(StatsPath, sb.ToString());
            Debug.Log($"[Cabin] {net.Summary()}; land under 25°: {100f * walkable / Mathf.Max(1, land):F0} %. Preview: {ImagePath}");
        }

        /// <summary>Hillshade from the north-west, tinted green low to brown and grey high.</summary>
        static Color Shade(Grid heights, int x, int z, float h)
        {
            float dx = (heights.At(x + Step, z) - heights.At(x - Step, z)) / (2f * Step * heights.Cell);
            float dz = (heights.At(x, z + Step) - heights.At(x, z - Step)) / (2f * Step * heights.Cell);
            var normal = new Vector3(-dx, 1f, -dz).normalized;
            float light = Mathf.Clamp01(Vector3.Dot(normal, new Vector3(-0.5f, 0.6f, 0.6f).normalized)) * 0.75f + 0.25f;
            float t = Mathf.InverseLerp(0f, 200f, h);
            Color tint = Color.Lerp(new Color(0.35f, 0.55f, 0.25f), new Color(0.55f, 0.45f, 0.3f), Mathf.SmoothStep(0f, 1f, t));
            tint = Color.Lerp(tint, new Color(0.6f, 0.6f, 0.6f), Mathf.InverseLerp(40f, 60f, heights.Slope(x, z)));
            return tint * light;
        }

        static void Dot(Color32[] px, int pw, int ph, Grid heights, Vector2 at, Color32 colour, int radius)
        {
            int cx = Mathf.RoundToInt((at.x - heights.Origin.x) / heights.Cell / Step), cz = Mathf.RoundToInt((at.y - heights.Origin.y) / heights.Cell / Step);
            for (int dz = -radius; dz <= radius; dz++)
            for (int dx = -radius; dx <= radius; dx++)
            {
                int x = cx + dx, z = cz + dz;
                if (x >= 0 && z >= 0 && x < pw && z < ph && dx * dx + dz * dz <= radius * radius) px[z * pw + x] = colour;
            }
        }

        /// <summary>Redraws the preview from the World scene as it stands: the tiles' heights, and water wherever a water mesh has a vertex.</summary>
        [MenuItem("Tools/Cabin/Map/Preview PNG", priority = 31)]
        public static void FromScene()
        {
            var root = GameObject.Find("Map");
            if (root == null)
            {
                Debug.LogWarning("[Cabin] Open the World scene (with a generated Map) first");
                return;
            }
            var r = MapGenerator.Recipe();
            int res = r.heightmapResolution;
            int w = r.tilesX * (res - 1) + 1, h = r.tilesZ * (res - 1) + 1;
            float cell = r.tileSize / (res - 1);
            var heights = new Grid(w, h, cell, new Vector2(r.Origin.x, r.Origin.z));
            var water = new Grid(w, h, cell, heights.Origin, float.NaN);
            foreach (var t in root.GetComponentsInChildren<Terrain>())
            {
                var hm = t.terrainData.GetHeights(0, 0, res, res);
                int ox = Mathf.RoundToInt((t.transform.position.x - r.Origin.x) / r.tileSize) * (res - 1);
                int oz = Mathf.RoundToInt((t.transform.position.z - r.Origin.z) / r.tileSize) * (res - 1);
                for (int z = 0; z < res; z++)
                for (int x = 0; x < res; x++)
                    heights[ox + x, oz + z] = hm[z, x] * t.terrainData.size.y - r.seaFloorDepth;
            }
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null || mf.name == "Sea") continue;
                foreach (var v in mf.sharedMesh.vertices)
                {
                    int x = Mathf.RoundToInt((v.x - heights.Origin.x) / cell), z = Mathf.RoundToInt((v.z - heights.Origin.y) / cell);
                    if (x >= 0 && z >= 0 && x < w && z < h) water[x, z] = v.y;
                }
            }
            var player = GameObject.Find("Player");
            var spawn = player != null ? player.transform.position : Vector3.zero;
            int pw = (w - 1) / Step, ph = (h - 1) / Step;
            var tex = new Texture2D(pw, ph, TextureFormat.RGB24, false);
            var px = new Color32[pw * ph];
            for (int j = 0; j < ph; j++)
            for (int i = 0; i < pw; i++)
            {
                int x = i * Step, z = j * Step;
                float hgt = heights[x, z];
                bool wet = false;
                for (int dz = 0; dz < Step && !wet; dz++)
                for (int dx = 0; dx < Step && !wet; dx++)
                    wet = !float.IsNaN(water.At(x + dx, z + dz));
                px[j * pw + i] = wet ? new Color(0.2f, 0.5f, 0.9f) : hgt <= 0f ? new Color(0.05f, 0.12f, 0.25f) : Shade(heights, x, z, hgt);
            }
            Dot(px, pw, ph, heights, new Vector2(spawn.x, spawn.z), new Color32(255, 230, 40, 255), 3);
            tex.SetPixels32(px);
            tex.Apply();
            Directory.CreateDirectory("Temp");
            File.WriteAllBytes(ImagePath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            Debug.Log($"[Cabin] Preview written: {ImagePath}");
        }
    }
}
