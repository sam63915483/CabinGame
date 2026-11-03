using System;
using Cabin.Core;

namespace Cabin.Bench
{
    /// <summary>
    /// One settings combination the bench measures: every field is an override of the baseline, and a negative
    /// value (or -2 for the two that use -1 to mean "follow the tier") leaves the baseline alone. Plain fields so
    /// JsonUtility reads it from the plan file.
    /// </summary>
    [Serializable]
    public sealed class BenchCombo
    {
        public string name = "default";
        public int quality = -1;
        public int msaa = -2;
        public int shadows = -2;
        public float renderScale = -1f;
        public int treeDistance = -1;
        public int farForest = -1;
        public int grassDistance = -1;
        public float grassDensity = -1f;
        /// <summary>0 draws no grass at all (not a menu setting; the field's own switch).</summary>
        public int grass = -1;
        public int post = -1;
        public int atmosphere = -1;
        public int sunShafts = -1, haze = -1, clouds = -1, cloudShadows = -1, sunGlow = -1;

        // Experiments beyond the menu (applied by BenchExperiments; negative leaves the scene as built).
        /// <summary>URP's per-frame depth texture and opaque colour copy (0 off, 1 on).</summary>
        public int depthTexture = -1, opaqueTexture = -1;
        public int hdr = -1;
        /// <summary>The view-model overlay camera and the water meshes (0 off).</summary>
        public int viewModel = -1, water = -1;
        /// <summary>Whether the terrain casts shadows (0 off), and whether it draws its trees at all (0 off: bare, but the cost of Unity's tree renderer shows).</summary>
        public int terrainShadows = -1, terrainTrees = -1;
        /// <summary>Terrain distance beyond which the cheap baked base map is used, and its mesh pixel error.</summary>
        public float basemapDistance = -1f, pixelError = -1f;
        /// <summary>LOD bias (under 1 switches LODs sooner).</summary>
        public float lodBias = -1f;
        /// <summary>Explicit shadow setup over the tier's: distance (m), cascades, map size.</summary>
        public float shadowDistance = -1f;
        public int shadowCascades = -1, shadowResolution = -1;
        /// <summary>Grass field tuning: where the light mesh takes over (m), and the thinning curve.</summary>
        public float grassNearMesh = -1f, grassFadeStart = -1f, grassFadeFloor = -1f;

        /// <summary>The defaults a fresh install plays with: the number Sam wants is this one.</summary>
        public static void Baseline()
        {
            GameSettings.Quality = 2;
            GameSettings.Msaa = -1;
            GameSettings.Shadows = -1;
            GameSettings.RenderScale = 1f;
            GameSettings.TreeDistance = 300;
            GameSettings.FarForest = true;
            GameSettings.GrassDistance = 120;
            GameSettings.GrassDensity = 1f;
            GameSettings.PostProcessing = true;
            GameSettings.Atmosphere = true;
            GameSettings.SunShafts = GameSettings.Haze = GameSettings.Clouds = GameSettings.CloudShadows = GameSettings.SunGlow = true;
            GameSettings.Fov = 72f;
            GameSettings.VSync = false;
            GameSettings.FrameCap = 0;
        }

        /// <summary>Lays this combo's overrides over whatever the settings hold. Returns whether grass is drawn.</summary>
        public bool ApplyTo()
        {
            if (quality >= 0) GameSettings.Quality = quality;
            if (msaa >= -1) GameSettings.Msaa = msaa;
            if (shadows >= -1) GameSettings.Shadows = shadows;
            if (renderScale > 0f) GameSettings.RenderScale = renderScale;
            if (treeDistance > 0) GameSettings.TreeDistance = treeDistance;
            if (farForest >= 0) GameSettings.FarForest = farForest != 0;
            if (grassDistance > 0) GameSettings.GrassDistance = grassDistance;
            if (grassDensity > 0f) GameSettings.GrassDensity = grassDensity;
            if (post >= 0) GameSettings.PostProcessing = post != 0;
            if (atmosphere >= 0) GameSettings.Atmosphere = atmosphere != 0;
            if (sunShafts >= 0) GameSettings.SunShafts = sunShafts != 0;
            if (haze >= 0) GameSettings.Haze = haze != 0;
            if (clouds >= 0) GameSettings.Clouds = clouds != 0;
            if (cloudShadows >= 0) GameSettings.CloudShadows = cloudShadows != 0;
            if (sunGlow >= 0) GameSettings.SunGlow = sunGlow != 0;
            return grass != 0;
        }
    }
}
