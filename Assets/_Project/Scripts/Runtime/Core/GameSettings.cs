using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Cabin.Core
{
    /// <summary>
    /// The player's settings: what the Esc menu edits, kept in PlayerPrefs and applied at start and whenever changed.
    /// Screen (resolution, window mode, vsync, frame cap), quality tier (the Low/Medium/High URP assets), render
    /// scale, anti-aliasing and shadows on their own (so each can be costed alone), how far real trees and grass draw
    /// and how thick the grass is, the far forest of impostors, the post-processing look, field of view and mouse
    /// sensitivity. Values are clamped so a stale or hand-edited pref can't break anything.
    /// </summary>
    public static class GameSettings
    {
        public const int TreeDistanceMin = 150, TreeDistanceMax = 400, GrassDistanceMin = 40, GrassDistanceMax = 250;
        public const float RenderScaleMin = 0.5f, RenderScaleMax = 1f, FovMin = 60f, FovMax = 100f, SensitivityMin = 0.02f, SensitivityMax = 0.2f;
        public static readonly int[] FrameCaps = { 0, 60, 120, 144, 240 };
        /// <summary>MSAA sample counts the anti-aliasing setting steps through.</summary>
        public static readonly int[] MsaaLevels = { 1, 2, 4, 8 };
        public static readonly string[] ShadowNames = { "Off", "Low", "Medium", "High" };

        public static int Width, Height;
        public static FullScreenMode WindowMode = FullScreenMode.FullScreenWindow;
        public static bool VSync;
        public static int FrameCap;
        public static int Quality = 2;
        /// <summary>Anti-aliasing: an index into <see cref="MsaaLevels"/>; -1 follows the quality tier.</summary>
        public static int Msaa = -1;
        /// <summary>Shadows: an index into <see cref="ShadowNames"/>; -1 follows the quality tier.</summary>
        public static int Shadows = -1;
        public static float RenderScale = 1f;
        public static int TreeDistance = 300, GrassDistance = 120;
        public static bool FarForest = true;
        public static bool PostProcessing = true;
        /// <summary>Sun shafts, sun-coloured haze, clouds and their shadows (the atmosphere pass).</summary>
        public static bool Atmosphere = true;
        /// <summary>The atmosphere's parts, each on its own so the before and after of each can be seen.</summary>
        public static bool SunShafts = true, Haze = true, Clouds = true, CloudShadows = true, SunGlow = true;
        /// <summary>How much of the placed grass is drawn (0.2–1).</summary>
        public static float GrassDensity = 1f;
        public const float GrassDensityMin = 0.2f, GrassDensityMax = 1f;
        public static float Fov = 72f, Sensitivity = 0.08f;

        static bool loaded;

        public static void Load()
        {
            if (loaded) return;
            loaded = true;
            Width = PlayerPrefs.GetInt("screen.w", Screen.currentResolution.width);
            Height = PlayerPrefs.GetInt("screen.h", Screen.currentResolution.height);
            WindowMode = (FullScreenMode)PlayerPrefs.GetInt("screen.mode", (int)FullScreenMode.FullScreenWindow);
            VSync = PlayerPrefs.GetInt("screen.vsync", 0) == 1;
            FrameCap = PlayerPrefs.GetInt("screen.cap", 0);
            Quality = PlayerPrefs.GetInt("quality", QualitySettings.GetQualityLevel());
            Msaa = PlayerPrefs.GetInt("msaa", -1);
            Shadows = PlayerPrefs.GetInt("shadows", -1);
            RenderScale = PlayerPrefs.GetFloat("renderScale", 1f);
            TreeDistance = PlayerPrefs.GetInt("treeDistance", 300);
            // Beyond 400 m the terrain's tree renderer costs the CPU too much per tree (35k trees at 700 m); the far forest covers the rest.
            if (TreeDistance > TreeDistanceMax) TreeDistance = 300;
            GrassDistance = PlayerPrefs.GetInt("grassDistance", 120);
            PostProcessing = PlayerPrefs.GetInt("post", 1) == 1;
            Atmosphere = PlayerPrefs.GetInt("atmosphere", 1) == 1;
            SunShafts = PlayerPrefs.GetInt("sunShafts", 1) == 1;
            Haze = PlayerPrefs.GetInt("haze", 1) == 1;
            Clouds = PlayerPrefs.GetInt("clouds", 1) == 1;
            CloudShadows = PlayerPrefs.GetInt("cloudShadows", 1) == 1;
            SunGlow = PlayerPrefs.GetInt("sunGlow", 1) == 1;
            FarForest = PlayerPrefs.GetInt("farForest", 1) == 1;
            GrassDensity = PlayerPrefs.GetFloat("grassDensity", 1f);
            Fov = PlayerPrefs.GetFloat("fov", 72f);
            Sensitivity = PlayerPrefs.GetFloat("sensitivity", 0.08f);
            Clamp();
        }

        public static void Save()
        {
            Clamp();
            PlayerPrefs.SetInt("screen.w", Width);
            PlayerPrefs.SetInt("screen.h", Height);
            PlayerPrefs.SetInt("screen.mode", (int)WindowMode);
            PlayerPrefs.SetInt("screen.vsync", VSync ? 1 : 0);
            PlayerPrefs.SetInt("screen.cap", FrameCap);
            PlayerPrefs.SetInt("quality", Quality);
            PlayerPrefs.SetInt("msaa", Msaa);
            PlayerPrefs.SetInt("shadows", Shadows);
            PlayerPrefs.SetFloat("renderScale", RenderScale);
            PlayerPrefs.SetInt("treeDistance", TreeDistance);
            PlayerPrefs.SetInt("farForest", FarForest ? 1 : 0);
            PlayerPrefs.SetInt("grassDistance", GrassDistance);
            PlayerPrefs.SetInt("post", PostProcessing ? 1 : 0);
            PlayerPrefs.SetInt("atmosphere", Atmosphere ? 1 : 0);
            PlayerPrefs.SetInt("sunShafts", SunShafts ? 1 : 0);
            PlayerPrefs.SetInt("haze", Haze ? 1 : 0);
            PlayerPrefs.SetInt("clouds", Clouds ? 1 : 0);
            PlayerPrefs.SetInt("cloudShadows", CloudShadows ? 1 : 0);
            PlayerPrefs.SetInt("sunGlow", SunGlow ? 1 : 0);
            PlayerPrefs.SetFloat("grassDensity", GrassDensity);
            PlayerPrefs.SetFloat("fov", Fov);
            PlayerPrefs.SetFloat("sensitivity", Sensitivity);
            PlayerPrefs.Save();
        }

        /// <summary>Everything into its allowed range.</summary>
        public static void Clamp()
        {
            Width = Mathf.Clamp(Width, 640, 7680);
            Height = Mathf.Clamp(Height, 480, 4320);
            if (WindowMode != FullScreenMode.ExclusiveFullScreen && WindowMode != FullScreenMode.FullScreenWindow && WindowMode != FullScreenMode.Windowed)
                WindowMode = FullScreenMode.FullScreenWindow;
            if (System.Array.IndexOf(FrameCaps, FrameCap) < 0) FrameCap = 0;
            Quality = Mathf.Clamp(Quality, 0, Mathf.Max(0, QualitySettings.names.Length - 1));
            Msaa = Mathf.Clamp(Msaa, -1, MsaaLevels.Length - 1);
            Shadows = Mathf.Clamp(Shadows, -1, ShadowNames.Length - 1);
            RenderScale = Mathf.Clamp(RenderScale, RenderScaleMin, RenderScaleMax);
            TreeDistance = Mathf.Clamp(TreeDistance, TreeDistanceMin, TreeDistanceMax);
            GrassDistance = Mathf.Clamp(GrassDistance, GrassDistanceMin, GrassDistanceMax);
            GrassDensity = Mathf.Clamp(GrassDensity, GrassDensityMin, GrassDensityMax);
            Fov = Mathf.Clamp(Fov, FovMin, FovMax);
            Sensitivity = Mathf.Clamp(Sensitivity, SensitivityMin, SensitivityMax);
        }

        /// <summary>Applies the screen settings (a resolution change is a mode switch, so only on request).</summary>
        public static void ApplyScreen()
        {
            Clamp();
            Screen.SetResolution(Width, Height, WindowMode);
            QualitySettings.vSyncCount = VSync ? 1 : 0;
            Application.targetFrameRate = FrameCap > 0 ? FrameCap : -1;
        }

        /// <summary>Applies everything but the screen: cheap, done whenever a value changes.</summary>
        public static void ApplyWorld(Volume look, Camera camera, Player.FirstPersonController controller)
        {
            Clamp();
            if (QualitySettings.GetQualityLevel() != Quality) QualitySettings.SetQualityLevel(Quality, true);
            if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
            {
                urp.renderScale = RenderScale;
                RenderTier.Apply(urp, Msaa, Shadows);
            }
            QualitySettings.vSyncCount = VSync ? 1 : 0;
            Application.targetFrameRate = FrameCap > 0 ? FrameCap : -1;
            foreach (var t in Terrain.activeTerrains) t.treeDistance = 0f; // NearForest draws the trees
            if (look != null) look.enabled = PostProcessing;
            Core.Atmosphere.Enabled = Atmosphere;
            Core.Atmosphere.Shafts = SunShafts;
            Core.Atmosphere.HazeOn = Haze;
            Core.Atmosphere.CloudsOn = Clouds;
            Core.Atmosphere.CloudShadows = CloudShadows;
            Core.Atmosphere.Glow = SunGlow;
            Forestry.GrassField.Distance = GrassDistance;
            Forestry.GrassField.Density = GrassDensity;
            Forestry.FarForest.NearDistance = TreeDistance;
            Forestry.NearForest.Radius = TreeDistance + Forestry.NearForest.Margin;
            Forestry.FarForest.Enabled = FarForest;
            if (camera != null) camera.fieldOfView = Fov;
            if (controller != null) controller.LookDegreesPerPixel = Sensitivity;
        }
    }
}
