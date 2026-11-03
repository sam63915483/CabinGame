using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Cabin.Editor
{
    /// <summary>
    /// The hotbar icons: the nail gun, spray can and axe rendered from their prefabs, three-quarter view, on transparent
    /// 256² PNGs in Art/UI. The saw has no model yet; its icon is a drawing (Art/UI/Source/Icon_Saw.svg, exported to
    /// Icon_Saw.png). Re-runnable.
    /// </summary>
    public static class IconGenerator
    {
        public const string Dir = "Assets/_Project/Art/UI";
        public const string NailGunPath = Dir + "/Icon_NailGun.png";
        public const string SprayCanPath = Dir + "/Icon_SprayCan.png";
        public const string SawPath = Dir + "/Icon_Saw.png";
        public const string AxePath = Dir + "/Icon_Axe.png";
        const int Size = 256;
        /// <summary>A layer nothing else uses, so the icon camera sees only the model.</summary>
        const int IconLayer = 31;

        [MenuItem("Tools/Cabin/Build/Generate Hotbar Icons", priority = 13)]
        public static void GenerateAll()
        {
            SandboxAssetGenerator.EnsureFolder(Dir);
            Render(ToolModelGenerator.NailGunPrefabPath, NailGunPath, Quaternion.Euler(0f, -90f, 0f));
            Render(ToolModelGenerator.SprayCanPrefabPath, SprayCanPath, Quaternion.Euler(0f, -20f, 0f) * Quaternion.Euler(-12f, 0f, 0f));
            Render(ToolModelGenerator.AxePrefabPath, AxePath, Quaternion.Euler(0f, 0f, -40f) * Quaternion.Euler(0f, 90f, 0f));
            if (System.IO.File.Exists(SawPath)) Import(SawPath);
            Debug.Log("[Cabin] Hotbar icons generated");
        }

        static void Render(string prefabPath, string output, Quaternion pose)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null) return;
            var model = Object.Instantiate(prefab);
            var lightGo = new GameObject("IconLight");
            var camGo = new GameObject("IconCamera");
            var rt = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 8 };
            try
            {
                model.transform.SetPositionAndRotation(new Vector3(2000f, 2000f, 2000f), pose);
                foreach (var t in model.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = IconLayer;
                var bounds = new Bounds(model.transform.position, Vector3.zero);
                foreach (var r in model.GetComponentsInChildren<Renderer>()) bounds.Encapsulate(r.bounds);

                var light = lightGo.AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1.6f;
                light.cullingMask = 1 << IconLayer;
                lightGo.transform.rotation = Quaternion.Euler(35f, -40f, 0f);

                var cam = camGo.AddComponent<Camera>();
                camGo.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = false;
                cam.cullingMask = 1 << IconLayer;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = Color.clear;
                cam.orthographic = true;
                cam.orthographicSize = Mathf.Max(bounds.extents.x, bounds.extents.y, bounds.extents.z) * 1.15f;
                cam.transform.position = bounds.center + new Vector3(-0.5f, 0.3f, -1f).normalized * 3f;
                cam.transform.LookAt(bounds.center);
                cam.nearClipPlane = 0.01f;
                cam.farClipPlane = 10f;
                cam.targetTexture = rt;
                cam.Render();

                var previous = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
                tex.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
                RenderTexture.active = previous;
                System.IO.File.WriteAllBytes(output, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
            }
            finally
            {
                Object.DestroyImmediate(model);
                Object.DestroyImmediate(lightGo);
                Object.DestroyImmediate(camGo);
                rt.Release();
            }
            Import(output);
        }

        static void Import(string path)
        {
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.GUI;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }
    }
}
