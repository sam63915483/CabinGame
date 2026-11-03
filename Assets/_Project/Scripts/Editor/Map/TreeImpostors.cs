using System.IO;
using System.Linq;
using Cabin.Forestry;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Cabin.Editor.Map
{
    /// <summary>
    /// Photographs every tree prefab from eight directions, in the open scene's light, into one atlas, and records
    /// each species' size: the <see cref="FarForestData"/> the far forest draws its distant trees with. Each tree is
    /// stood far above the map for its sitting so nothing else is in shot. Writes Assets/Generated/Trees/Impostors.png
    /// and Impostors.asset, and the impostor material.
    /// </summary>
    public static class TreeImpostors
    {
        const string Dir = "Assets/Generated/Trees";
        const string AtlasPath = Dir + "/Impostors.png";
        const string DataPath = Dir + "/Impostors.asset";
        public const string MaterialPath = "Assets/_Project/Art/Materials/Impostor.mat";
        const int Views = 8, Columns = 16, Rows = 8, CellW = 256, CellH = 512;
        static readonly Vector3 Studio = new Vector3(0f, 4000f, 0f);

        public static FarForestData Build(GameObject[] treePrefabs)
        {
            SandboxAssetGenerator.EnsureFolder(Dir);
            int species = Mathf.Min(treePrefabs.Length, Columns * Rows / Views);
            var atlas = new Texture2D(Columns * CellW, Rows * CellH, TextureFormat.RGBA32, false);
            var clear = new Color32[CellW * CellH];
            var widths = new float[species];
            var heights = new float[species];

            var camGo = new GameObject("ImpostorCamera");
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 200f;
            cam.allowMSAA = false;
            var camData = camGo.AddComponent<UniversalAdditionalCameraData>();
            camData.renderPostProcessing = false;
            camData.renderShadows = false;
            var rt = new RenderTexture(CellW, CellH, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            bool fog = RenderSettings.fog;
            RenderSettings.fog = false;
            // The atmosphere pass would fill every picture's background with sky and haze: off while the trees are photographed.
            var atmosphere = AssetDatabase.LoadAllAssetsAtPath("Assets/_Project/Settings/URP_Renderer.asset").OfType<ScriptableRendererFeature>().FirstOrDefault(x => x.name == "Atmosphere");
            if (atmosphere != null) atmosphere.SetActive(false);
            var cell = new Texture2D(CellW, CellH, TextureFormat.RGBA32, false);
            try
            {
                for (int s = 0; s < species; s++)
                {
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(treePrefabs[s]);
                    try
                    {
                        go.transform.position = Studio;
                        var b = new Bounds(Studio, Vector3.zero);
                        foreach (var r in go.GetComponentsInChildren<Renderer>()) b.Encapsulate(r.bounds);
                        float w = Mathf.Max(b.size.x, b.size.z), h = b.max.y - Studio.y;
                        widths[s] = w;
                        heights[s] = h;
                        // The quad in game spans width w about the trunk and height h from the ground: frame exactly that.
                        cam.orthographicSize = h * 0.5f;
                        cam.aspect = w / h;
                        var lod = go.GetComponent<LODGroup>();
                        if (lod != null) lod.ForceLOD(0);
                        for (int v = 0; v < Views; v++)
                        {
                            go.transform.rotation = Quaternion.Euler(0f, v * 360f / Views, 0f);
                            // Camera at −Z looking +Z, level with the tree's middle.
                            cam.transform.position = Studio + new Vector3(0f, h * 0.5f, -100f);
                            cam.transform.rotation = Quaternion.identity;
                            cam.Render();
                            var prev = RenderTexture.active;
                            RenderTexture.active = rt;
                            cell.ReadPixels(new Rect(0, 0, CellW, CellH), 0, 0);
                            RenderTexture.active = prev;
                            var rect = FarForestData.Cell(s, v, Views, Columns, Rows);
                            atlas.SetPixels32(Mathf.RoundToInt(rect.x * atlas.width), Mathf.RoundToInt(rect.y * atlas.height), CellW, CellH, cell.GetPixels32());
                        }
                    }
                    finally
                    {
                        Object.DestroyImmediate(go);
                    }
                }
                for (int i = species * Views; i < Columns * Rows; i++)
                {
                    var rect = FarForestData.Cell(i / Views, i % Views, Views, Columns, Rows);
                    atlas.SetPixels32(Mathf.RoundToInt(rect.x * atlas.width), Mathf.RoundToInt(rect.y * atlas.height), CellW, CellH, clear);
                }
            }
            finally
            {
                RenderSettings.fog = fog;
                if (atmosphere != null) atmosphere.SetActive(true);
                cam.targetTexture = null;
                Object.DestroyImmediate(rt);
                Object.DestroyImmediate(cell);
                Object.DestroyImmediate(camGo);
            }
            Bleed(atlas);
            File.WriteAllBytes(AtlasPath, atlas.EncodeToPNG());
            Object.DestroyImmediate(atlas);
            AssetDatabase.ImportAsset(AtlasPath);
            var importer = (TextureImporter)AssetImporter.GetAtPath(AtlasPath);
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = true;
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = 4;
            importer.textureCompression = TextureImporterCompression.Compressed;
            importer.maxTextureSize = 4096;
            importer.SaveAndReimport();

            var data = AssetDatabase.LoadAssetAtPath<FarForestData>(DataPath);
            if (data == null)
            {
                data = ScriptableObject.CreateInstance<FarForestData>();
                AssetDatabase.CreateAsset(data, DataPath);
            }
            data.Atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(AtlasPath);
            data.Views = Views;
            data.Columns = Columns;
            data.Rows = Rows;
            data.Widths = widths;
            data.Heights = heights;
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();
            return data;
        }

        /// <summary>Spreads edge colours into the transparent pixels so mipmaps and bilinear filtering don't darken the outlines.</summary>
        static void Bleed(Texture2D tex)
        {
            var px = tex.GetPixels32();
            int w = tex.width, h = tex.height;
            var next = (Color32[])px.Clone();
            for (int pass = 0; pass < 3; pass++)
            {
                for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    if (px[i].a > 0) continue;
                    int r = 0, g = 0, b = 0, n = 0;
                    for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                        var c = px[ny * w + nx];
                        if (c.a == 0) continue;
                        r += c.r; g += c.g; b += c.b; n++;
                    }
                    if (n > 0) next[i] = new Color32((byte)(r / n), (byte)(g / n), (byte)(b / n), 0);
                }
                System.Array.Copy(next, px, px.Length);
            }
            tex.SetPixels32(px);
        }

        public static Material Material(FarForestData data)
        {
            var shader = Shader.Find("Cabin/Impostor");
            var m = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (m == null)
            {
                m = new Material(shader);
                AssetDatabase.CreateAsset(m, MaterialPath);
            }
            m.shader = shader;
            m.SetTexture("_Atlas", data.Atlas);
            m.SetFloat("_Views", data.Views);
            m.SetFloat("_Columns", data.Columns);
            m.SetFloat("_Rows", data.Rows);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }
    }
}
