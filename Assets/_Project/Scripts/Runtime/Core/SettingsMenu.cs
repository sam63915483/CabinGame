using System.Collections.Generic;
using System.Linq;
using Cabin.Player;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Cabin.Core
{
    /// <summary>
    /// Esc opens the settings: screen (resolution, window mode, vsync, frame cap — applied with the button), quality
    /// tier, anti-aliasing and shadows on their own, render scale, tree and grass distance, grass density, the far forest, the post-processing look, field of view and
    /// mouse sensitivity (applied as they change). Esc or Back closes it and gives the mouse back to the game. Drawn
    /// with the same immediate-mode GUI as the HUD; values live in <see cref="GameSettings"/>. On the Player.
    /// </summary>
    public sealed class SettingsMenu : MonoBehaviour
    {

        InputAction cancel;
        FirstPersonController controller;
        Camera view;
        Volume look;
        bool open;
        Resolution[] resolutions;
        int resolutionIndex;
        GUIStyle title, label, button, value;
        Vector2 scroll;

        public bool Open => open;
        /// <summary>Whether any settings menu is open: the HUD hides behind it.</summary>
        public static bool IsOpen { get; private set; }


        void Awake()
        {
            cancel = Controls.Get("Player/Cancel");
            controller = GetComponent<FirstPersonController>();
            view = GetComponentInChildren<Camera>();
            GameSettings.Load();
            resolutions = Screen.resolutions.Select(r => (r.width, r.height)).Distinct().Select(r => new Resolution { width = r.width, height = r.height }).ToArray();
            if (resolutions.Length == 0) resolutions = new[] { new Resolution { width = Screen.width, height = Screen.height } };
            resolutionIndex = Nearest(GameSettings.Width, GameSettings.Height);
        }

        void Start()
        {
            look = FindLook();
            GameSettings.ApplyWorld(look, view, controller);
            QualitySettings.vSyncCount = GameSettings.VSync ? 1 : 0;
            Application.targetFrameRate = GameSettings.FrameCap > 0 ? GameSettings.FrameCap : -1;
        }

        static Volume FindLook()
        {
            var go = GameObject.Find("Look");
            return go != null ? go.GetComponent<Volume>() : null;
        }

        void Update()
        {
            if (cancel.WasPressedThisFrame())
            {
                if (open) Close();
                else Show();
            }
        }

        public void Show()
        {
            open = true;
            IsOpen = true;
            CursorCapture.UiOpen = true;
            CursorCapture.Release();
            if (look == null) look = FindLook();
        }

        public void Close()
        {
            open = false;
            IsOpen = false;
            CursorCapture.UiOpen = false;
            GameSettings.Save();
            CursorCapture.Capture();
        }

        int Nearest(int w, int h)
        {
            int best = 0;
            long bestD = long.MaxValue;
            for (int i = 0; i < resolutions.Length; i++)
            {
                long d = (long)(resolutions[i].width - w) * (resolutions[i].width - w) + (long)(resolutions[i].height - h) * (resolutions[i].height - h);
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        void Styles(float scale)
        {
            if (title != null && Mathf.Approximately(title.fontSize, 30 * scale)) return;
            title = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(30 * scale), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            label = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(18 * scale), alignment = TextAnchor.MiddleLeft };
            value = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(18 * scale), alignment = TextAnchor.MiddleCenter };
            button = new GUIStyle(GUI.skin.button) { fontSize = Mathf.RoundToInt(18 * scale) };
            foreach (var s in new[] { title, label, value }) s.normal.textColor = Color.white;
        }

        void OnGUI()
        {
            if (!open) return;
            float scale = Mathf.Max(0.75f, Screen.height / 1080f);
            Styles(scale);
            float w = 760 * scale, h = Mathf.Min(Screen.height - 40 * scale, 860 * scale);
            var r = new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f, w, h);
            GUI.Box(r, GUIContent.none);
            GUI.Box(r, GUIContent.none);
            GUILayout.BeginArea(new Rect(r.x + 24 * scale, r.y + 16 * scale, w - 48 * scale, h - 32 * scale));
            GUILayout.Label("Settings", title, GUILayout.Height(44 * scale));
            scroll = GUILayout.BeginScrollView(scroll);
            bool changed = false;

            Section("Screen", scale);
            var res = resolutions[resolutionIndex];
            Stepper("Resolution", $"{res.width} × {res.height}", scale, d => resolutionIndex = Mathf.Clamp(resolutionIndex + d, 0, resolutions.Length - 1));
            Stepper("Window", ModeName(GameSettings.WindowMode), scale, d => GameSettings.WindowMode = StepMode(GameSettings.WindowMode, d));
            if (Toggle("VSync", GameSettings.VSync, scale, v => GameSettings.VSync = v)) changed = true;
            Stepper("Frame cap", GameSettings.FrameCap == 0 ? "Off" : GameSettings.FrameCap.ToString(), scale, d =>
            {
                int i = System.Array.IndexOf(GameSettings.FrameCaps, GameSettings.FrameCap);
                GameSettings.FrameCap = GameSettings.FrameCaps[Mathf.Clamp(i + d, 0, GameSettings.FrameCaps.Length - 1)];
                changed = true;
            });
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Apply screen", button, GUILayout.Width(200 * scale), GUILayout.Height(36 * scale)))
            {
                GameSettings.Width = resolutions[resolutionIndex].width;
                GameSettings.Height = resolutions[resolutionIndex].height;
                GameSettings.ApplyScreen();
                GameSettings.Save();
            }
            GUILayout.EndHorizontal();

            Section("Graphics", scale);
            var names = QualitySettings.names;
            Stepper("Quality", names[Mathf.Clamp(GameSettings.Quality, 0, names.Length - 1)], scale, d => { GameSettings.Quality = Mathf.Clamp(GameSettings.Quality + d, 0, names.Length - 1); changed = true; });
            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            int tierMsaa = RenderTier.TierMsaa(urp), tierShadows = RenderTier.TierShadowLevel(urp);
            int msaa = GameSettings.Msaa < 0 ? tierMsaa : GameSettings.MsaaLevels[GameSettings.Msaa];
            Stepper("Anti-aliasing (MSAA)", (msaa <= 1 ? "Off" : $"{msaa}×") + (GameSettings.Msaa < 0 ? "  (tier)" : ""), scale, d =>
            {
                int i = System.Array.IndexOf(GameSettings.MsaaLevels, msaa);
                GameSettings.Msaa = Mathf.Clamp(i + d, 0, GameSettings.MsaaLevels.Length - 1);
                changed = true;
            });
            int shadows = GameSettings.Shadows < 0 ? tierShadows : GameSettings.Shadows;
            Stepper("Shadows", GameSettings.ShadowNames[shadows] + (GameSettings.Shadows < 0 ? "  (tier)" : ""), scale, d =>
            {
                GameSettings.Shadows = Mathf.Clamp(shadows + d, 0, GameSettings.ShadowNames.Length - 1);
                changed = true;
            });
            if (Slider("Render scale", ref GameSettings.RenderScale, GameSettings.RenderScaleMin, GameSettings.RenderScaleMax, $"{GameSettings.RenderScale * 100f:F0} %", scale)) changed = true;
            float trees = GameSettings.TreeDistance;
            if (Slider("Tree detail distance", ref trees, GameSettings.TreeDistanceMin, GameSettings.TreeDistanceMax, $"{trees:F0} m", scale)) { GameSettings.TreeDistance = Mathf.RoundToInt(trees / 50f) * 50; changed = true; }
            if (Toggle("Far forest", GameSettings.FarForest, scale, v => GameSettings.FarForest = v)) changed = true;
            float grass = GameSettings.GrassDistance;
            if (Slider("Grass distance", ref grass, GameSettings.GrassDistanceMin, GameSettings.GrassDistanceMax, $"{grass:F0} m", scale)) { GameSettings.GrassDistance = Mathf.RoundToInt(grass / 10f) * 10; changed = true; }
            if (Toggle("Post-processing look", GameSettings.PostProcessing, scale, v => GameSettings.PostProcessing = v)) changed = true;
            Section("Atmosphere", scale);
            if (Toggle("Atmosphere (all of the below)", GameSettings.Atmosphere, scale, v => GameSettings.Atmosphere = v)) changed = true;
            if (Toggle("Sun shafts", GameSettings.SunShafts, scale, v => GameSettings.SunShafts = v)) changed = true;
            if (Toggle("Haze", GameSettings.Haze, scale, v => GameSettings.Haze = v)) changed = true;
            if (Toggle("Sun glow", GameSettings.SunGlow, scale, v => GameSettings.SunGlow = v)) changed = true;
            if (Toggle("Clouds", GameSettings.Clouds, scale, v => GameSettings.Clouds = v)) changed = true;
            if (Toggle("Cloud shadows", GameSettings.CloudShadows, scale, v => GameSettings.CloudShadows = v)) changed = true;
            if (Slider("Grass density", ref GameSettings.GrassDensity, GameSettings.GrassDensityMin, GameSettings.GrassDensityMax, $"{GameSettings.GrassDensity * 100f:F0} %", scale)) changed = true;

            Section("Camera", scale);
            if (Slider("Field of view", ref GameSettings.Fov, GameSettings.FovMin, GameSettings.FovMax, $"{GameSettings.Fov:F0}°", scale)) changed = true;
            if (Slider("Mouse sensitivity", ref GameSettings.Sensitivity, GameSettings.SensitivityMin, GameSettings.SensitivityMax, $"{GameSettings.Sensitivity / 0.08f:F2}×", scale)) changed = true;

            GUILayout.EndScrollView();
            GUILayout.Space(8 * scale);
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{1f / Mathf.Max(0.0001f, Time.smoothDeltaTime):F0} fps", label, GUILayout.Width(120 * scale));
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Back  (Esc)", button, GUILayout.Width(200 * scale), GUILayout.Height(40 * scale))) Close();
            GUILayout.EndHorizontal();
            GUILayout.EndArea();

            if (changed) GameSettings.ApplyWorld(look, view, controller);
        }

        void Section(string name, float scale)
        {
            GUILayout.Space(14 * scale);
            GUILayout.Label(name, title, GUILayout.Height(30 * scale));
        }

        void Stepper(string name, string shown, float scale, System.Action<int> step)
        {
            GUILayout.BeginHorizontal(GUILayout.Height(36 * scale));
            GUILayout.Label(name, label, GUILayout.Width(260 * scale));
            if (GUILayout.Button("<", button, GUILayout.Width(44 * scale))) step(-1);
            GUILayout.Label(shown, value, GUILayout.Width(300 * scale));
            if (GUILayout.Button(">", button, GUILayout.Width(44 * scale))) step(1);
            GUILayout.EndHorizontal();
        }

        bool Toggle(string name, bool on, float scale, System.Action<bool> set)
        {
            GUILayout.BeginHorizontal(GUILayout.Height(36 * scale));
            GUILayout.Label(name, label, GUILayout.Width(260 * scale));
            bool now = GUILayout.Toggle(on, on ? "  On" : "  Off", GUILayout.Width(120 * scale));
            GUILayout.EndHorizontal();
            if (now == on) return false;
            set(now);
            return true;
        }

        bool Slider(string name, ref float v, float min, float max, string shown, float scale)
        {
            GUILayout.BeginHorizontal(GUILayout.Height(36 * scale));
            GUILayout.Label(name, label, GUILayout.Width(260 * scale));
            float next = GUILayout.HorizontalSlider(v, min, max, GUILayout.Width(300 * scale));
            GUILayout.Label(shown, value, GUILayout.Width(100 * scale));
            GUILayout.EndHorizontal();
            if (Mathf.Approximately(next, v)) return false;
            v = next;
            return true;
        }

        static string ModeName(FullScreenMode m) => m switch
        {
            FullScreenMode.ExclusiveFullScreen => "Fullscreen",
            FullScreenMode.FullScreenWindow => "Borderless window",
            _ => "Windowed",
        };

        static FullScreenMode StepMode(FullScreenMode m, int d)
        {
            var modes = new List<FullScreenMode> { FullScreenMode.ExclusiveFullScreen, FullScreenMode.FullScreenWindow, FullScreenMode.Windowed };
            int i = Mathf.Clamp(modes.IndexOf(m) + d, 0, modes.Count - 1);
            return modes[i];
        }
    }
}
