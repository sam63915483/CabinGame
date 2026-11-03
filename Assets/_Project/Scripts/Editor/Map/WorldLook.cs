using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Cabin.Editor.Map
{
    /// <summary>
    /// How the world is lit and graded: an afternoon sun, a procedural sky that lights the scene, haze that matches
    /// it, and a global post-processing volume (ACES tonemapping, a little bloom, contrast and saturation, a soft
    /// vignette) with HDR colour grading. No ambient occlusion: Sam found it cost 8 fps and looked bad. Applied by Generate
    /// World and by Tools/Cabin/Map/Apply Look; everything it makes is regenerable.
    /// </summary>
    public static class WorldLook
    {
        const string SkyPath = "Assets/_Project/Art/Materials/Sky.mat";
        const string ProfilePath = "Assets/_Project/Settings/WorldLook.asset";
        static readonly string[] Pipelines = { "Assets/_Project/Settings/URP_Low.asset", "Assets/_Project/Settings/URP_Medium.asset", "Assets/_Project/Settings/URP_High.asset" };
        const string LookName = "Look";

        [MenuItem("Tools/Cabin/Map/Apply Look", priority = 32)]
        public static void ApplyToOpenScene()
        {
            Apply();
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        }

        /// <summary>Sets up the open scene's sun, sky, fog and look volume, and the project's HDR grading.</summary>
        public static void Apply()
        {
            // The sun: late afternoon from the south-west, warm and lower than before (longer shadows, more side light).
            var sun = Object.FindObjectsByType<Light>(FindObjectsSortMode.None).FirstOrDefault(l => l.type == LightType.Directional);
            if (sun == null) sun = new GameObject("Directional Light").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.transform.rotation = Quaternion.Euler(36f, -128f, 0f);
            sun.color = new Color(1f, 0.93f, 0.8f);
            sun.intensity = 2.0f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.9f;
            RenderSettings.sun = sun;

            // The sky, lighting the scene from above.
            var sky = AssetDatabase.LoadAssetAtPath<Material>(SkyPath);
            if (sky == null)
            {
                sky = new Material(Shader.Find("Skybox/Procedural"));
                AssetDatabase.CreateAsset(sky, SkyPath);
            }
            sky.shader = Shader.Find("Skybox/Procedural");
            sky.SetFloat("_SunSize", 0.04f);
            sky.SetFloat("_SunSizeConvergence", 5f);
            sky.SetFloat("_AtmosphereThickness", 1.05f);
            sky.SetColor("_SkyTint", new Color(0.52f, 0.64f, 0.8f));
            sky.SetColor("_GroundColor", new Color(0.38f, 0.42f, 0.38f));
            sky.SetFloat("_Exposure", 1.15f);
            EditorUtility.SetDirty(sky);
            RenderSettings.skybox = sky;
            RenderSettings.ambientMode = AmbientMode.Skybox;
            RenderSettings.ambientIntensity = 1.05f;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;

            // Haze is the atmosphere pass's job (sun-coloured, by distance and height); Unity's fog only returns when
            // that pass is switched off (Atmosphere puts it back).
            RenderSettings.fog = false;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = new Color(0.71f, 0.78f, 0.86f);
            RenderSettings.fogDensity = 0.00045f;
            DynamicGI.UpdateEnvironment();

            // The look: one global volume.
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, ProfilePath);
            }
            var tone = Component<Tonemapping>(profile);
            // Neutral, not ACES: ACES crushed the midtones of this bright flat-shaded world (Sam preferred no look at all).
            tone.mode.Override(TonemappingMode.Neutral);
            var bloom = Component<Bloom>(profile);
            bloom.threshold.Override(0.95f);
            bloom.intensity.Override(0.2f);
            bloom.scatter.Override(0.6f);
            var colour = Component<ColorAdjustments>(profile);
            colour.postExposure.Override(0.45f);
            colour.contrast.Override(6f);
            colour.saturation.Override(8f);
            var white = Component<WhiteBalance>(profile);
            white.temperature.Override(4f);
            var vignette = Component<Vignette>(profile);
            vignette.intensity.Override(0.14f);
            vignette.smoothness.Override(0.5f);
            EditorUtility.SetDirty(profile);
            var look = GameObject.Find(LookName);
            if (look == null) look = new GameObject(LookName);
            var volume = look.GetComponent<Volume>();
            if (volume == null) volume = look.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 0f;
            volume.sharedProfile = profile;
            var toggle = look.GetComponent<Cabin.Core.LookToggle>();
            if (toggle == null) toggle = look.AddComponent<Cabin.Core.LookToggle>();
            toggle.Set(volume);

            // The atmosphere: sun shafts, sun-coloured haze, clouds and their shadows, as one screen pass.
            var atmosphereMaterial = AtmosphereMaterial();
            var feature = AtmosphereFeature(atmosphereMaterial);
            var atmosphere = look.GetComponent<Cabin.Core.Atmosphere>();
            if (atmosphere == null) atmosphere = look.AddComponent<Cabin.Core.Atmosphere>();
            atmosphere.Set(atmosphereMaterial, sun, feature);

            // HDR grading.
            foreach (var path in Pipelines)
            {
                var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);
                if (asset == null) continue;
                asset.colorGradingMode = ColorGradingMode.HighDynamicRange;
                EditorUtility.SetDirty(asset);
            }
            AssetDatabase.SaveAssets();
        }

        const string AtmosphereMaterialPath = "Assets/_Project/Art/Materials/Atmosphere.mat";
        const string RendererPath = "Assets/_Project/Settings/URP_Renderer.asset";
        const string FeatureName = "Atmosphere";

        static Material AtmosphereMaterial()
        {
            var shader = Shader.Find("Cabin/Atmosphere");
            var m = AssetDatabase.LoadAssetAtPath<Material>(AtmosphereMaterialPath);
            if (m == null)
            {
                m = new Material(shader);
                AssetDatabase.CreateAsset(m, AtmosphereMaterialPath);
            }
            m.shader = shader;
            // The tuned look (regenerable): a soft halo, shafts that read as rays rather than a wash, haze that
            // layers the hills, clouds at about half cover.
            m.SetColor("_SunColor", new Color(1f, 0.9f, 0.72f));
            m.SetColor("_HazeColor", new Color(0.7f, 0.79f, 0.9f));
            m.SetFloat("_FogDensity", 0.0011f);
            m.SetFloat("_FogBase", 0f);
            m.SetFloat("_FogFalloff", 0.006f);
            m.SetFloat("_SunInscatter", 0.75f);
            m.SetFloat("_SunGlow", 0.8f);
            m.SetFloat("_ShaftIntensity", 0.4f);
            m.SetFloat("_ShaftLength", 0.8f);
            m.SetFloat("_ShaftDecay", 0.12f);
            m.SetFloat("_ShaftRadius", 0.7f);
            m.SetFloat("_CloudHeight", 900f);
            m.SetFloat("_CloudScale", 3000f);
            m.SetFloat("_CloudCoverage", 0.5f);
            m.SetFloat("_CloudSoftness", 0.14f);
            m.SetColor("_CloudColor", new Color(1f, 0.99f, 0.97f));
            m.SetColor("_CloudShade", new Color(0.62f, 0.68f, 0.8f));
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>The atmosphere's passes on the project's renderer (added once; found by name after). The first version was URP's single full-screen pass; one of those left by name goes.</summary>
        static ScriptableRendererFeature AtmosphereFeature(Material material)
        {
            var data = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(RendererPath);
            if (data == null) { Debug.LogError($"[Cabin] no renderer data at {RendererPath}"); return null; }
            var all = AssetDatabase.LoadAllAssetsAtPath(RendererPath);
            foreach (var old in all.OfType<FullScreenPassRendererFeature>().Where(f => f.name == FeatureName).ToArray()) RemoveFeature(data, old);
            var feature = all.OfType<Cabin.Core.AtmosphereFeature>().FirstOrDefault(f => f.name == FeatureName);
            if (feature == null)
            {
                feature = ScriptableObject.CreateInstance<Cabin.Core.AtmosphereFeature>();
                feature.name = FeatureName;
                AssetDatabase.AddObjectToAsset(feature, data);
                var so = new SerializedObject(data);
                var list = so.FindProperty("m_RendererFeatures");
                var map = so.FindProperty("m_RendererFeatureMap");
                int n = list.arraySize;
                list.arraySize = n + 1;
                list.GetArrayElementAtIndex(n).objectReferenceValue = feature;
                map.arraySize = n + 1;
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out _, out long localId);
                map.GetArrayElementAtIndex(n).longValue = localId;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            feature.material = material;
            feature.SetActive(true);
            EditorUtility.SetDirty(feature);
            EditorUtility.SetDirty(data);
            data.SetDirty();
            return feature;
        }

        static void RemoveFeature(ScriptableRendererData data, ScriptableRendererFeature old)
        {
            var so = new SerializedObject(data);
            var list = so.FindProperty("m_RendererFeatures");
            var map = so.FindProperty("m_RendererFeatureMap");
            for (int i = list.arraySize - 1; i >= 0; i--)
            {
                if (list.GetArrayElementAtIndex(i).objectReferenceValue != old) continue;
                list.GetArrayElementAtIndex(i).objectReferenceValue = null;
                list.DeleteArrayElementAtIndex(i);
                if (i < map.arraySize) map.DeleteArrayElementAtIndex(i);
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.RemoveObjectFromAsset(old);
            Object.DestroyImmediate(old, true);
            Debug.Log("[Cabin] Replaced the atmosphere's full-screen pass with its own feature");
        }

        static T Component<T>(VolumeProfile profile) where T : VolumeComponent
        {
            if (profile.TryGet(out T c)) return c;
            // A profile asset keeps its components only as sub-assets: added in memory alone they vanish on save,
            // leaving an empty profile and no look at all.
            c = profile.Add<T>(true);
            c.name = typeof(T).Name;
            AssetDatabase.AddObjectToAsset(c, profile);
            return c;
        }
    }
}
