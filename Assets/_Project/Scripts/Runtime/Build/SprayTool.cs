using Cabin.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Cabin.Build
{
    /// <summary>
    /// Spray paint (4): hold LMB to spray where the crosshair is. Scroll through the palette, R swaps the fine cap
    /// (lettering) and the wide cap (coverage), C opens a colour picker.
    /// </summary>
    public sealed class SprayTool : BuildTool
    {
        static readonly (string name, Color color)[] Palette =
        {
            ("Red", new Color(0.80f, 0.08f, 0.07f)),
            ("Safety orange", new Color(1.00f, 0.38f, 0.02f)),
            ("Yellow", new Color(0.98f, 0.82f, 0.08f)),
            ("Green", new Color(0.13f, 0.55f, 0.18f)),
            ("Sky blue", new Color(0.25f, 0.62f, 0.92f)),
            ("Blue", new Color(0.08f, 0.20f, 0.70f)),
            ("Purple", new Color(0.45f, 0.15f, 0.62f)),
            ("Pink", new Color(0.95f, 0.40f, 0.65f)),
            ("Brown", new Color(0.36f, 0.22f, 0.12f)),
            ("Grey", new Color(0.50f, 0.50f, 0.50f)),
            ("White", new Color(0.95f, 0.95f, 0.93f)),
            ("Black", new Color(0.05f, 0.05f, 0.05f)),
        };

        int paletteIndex;
        Color color = Palette[0].color;
        string colorName = Palette[0].name;
        SprayCan.Cap cap = SprayCan.Cap.Fine;
        bool pickerOpen;
        float hue, sat, val;
        InputAction adjust, capToggle, picker;
        Texture2D swatch;

        public Color Color => color;
        public SprayCan.Cap Cap => cap;

        public override string Name => "Spray paint";
        public override string Hint => "Hold LMB to spray · scroll colour · R fine/wide cap · C colour picker";

        public override void OnSelected()
        {
            base.OnSelected();
            adjust ??= Controls.Get("Build/Adjust");
            capToggle ??= Controls.Get("Build/Rotate");
            picker ??= Controls.Get("Build/Picker");
        }

        public override void OnDeselected() => ClosePicker();

        public override void Tick()
        {
            float v = adjust.ReadValue<float>();
            int scroll = v > 0.01f ? 1 : v < -0.01f ? -1 : 0;
            if (scroll != 0)
            {
                paletteIndex = (paletteIndex + scroll + Palette.Length) % Palette.Length;
                SetColor(Palette[paletteIndex].color, Palette[paletteIndex].name);
                Belt.Say(colorName, 1.2f);
            }
            if (capToggle.WasPressedThisFrame())
            {
                cap = cap == SprayCan.Cap.Fine ? SprayCan.Cap.Wide : SprayCan.Cap.Fine;
                Belt.Say(cap == SprayCan.Cap.Fine ? "Fine cap — for lettering and lines" : "Wide cap — for covering");
            }
            if (picker.WasPressedThisFrame())
            {
                OpenPicker();
                return;
            }

            Status = ""; // the colour is the dot on the hotbar slot
            if (!Belt.UseHeld) return;

            var cam = Camera.main.transform;
            var ray = Belt.AimRay;
            Vector3 target = Physics.Raycast(ray, out var hit, SprayCan.Range + 0.5f, BuildLayers.AimMask, QueryTriggerInteraction.Ignore)
                ? hit.point
                : ray.origin + ray.direction * SprayCan.Range;
            // The can's nozzle, where the can is in your hand (or where it would be, if there's no model).
            Vector3 nozzle = Belt.ViewModel != null && Belt.ViewModel.TryGetNozzle(out var tip)
                ? tip
                : cam.position + cam.right * 0.163f - cam.up * 0.027f + cam.forward * 0.405f;
            Belt.Can.Spray(nozzle, target, Time.deltaTime, color, cap);
        }

        void SetColor(Color c, string name)
        {
            color = c;
            colorName = name;
            Color.RGBToHSV(c, out hue, out sat, out val);
        }

        void OpenPicker()
        {
            pickerOpen = true;
            Color.RGBToHSV(color, out hue, out sat, out val);
            CursorCapture.UiOpen = true;
            CursorCapture.Release();
        }

        void ClosePicker()
        {
            if (!pickerOpen) return;
            pickerOpen = false;
            CursorCapture.UiOpen = false;
            CursorCapture.Capture();
        }

        public override void OnGui(float scale)
        {
            if (swatch == null)
            {
                swatch = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
                swatch.SetPixel(0, 0, Color.white);
                swatch.Apply();
            }
            // (The current colour is the dot on the hotbar slot.)
            if (!pickerOpen) return;

            var e = Event.current;
            if (e.type == EventType.KeyDown && (e.keyCode == KeyCode.C || e.keyCode == KeyCode.Escape || e.keyCode == KeyCode.Return))
            {
                ClosePicker();
                e.Use();
                return;
            }

            float w = 420f * scale, h = 360f * scale;
            var panel = new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f, w, h);
            DrawBox(panel, new Color(0f, 0f, 0f, 0.75f));
            float pad = 14f * scale, y = panel.y + pad;
            var label = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(18 * scale) };
            label.normal.textColor = Color.white;
            GUI.Label(new Rect(panel.x + pad, y, w, 26f * scale), "Spray colour", label);
            y += 32f * scale;

            float chip = (w - pad * 2f - 5f * 6f * scale) / 6f;
            for (int i = 0; i < Palette.Length; i++)
            {
                var r = new Rect(panel.x + pad + (i % 6) * (chip + 6f * scale), y + (i / 6) * (chip * 0.6f + 6f * scale), chip, chip * 0.6f);
                DrawBox(r, Palette[i].color);
                if (GUI.Button(r, GUIContent.none, GUIStyle.none))
                {
                    paletteIndex = i;
                    SetColor(Palette[i].color, Palette[i].name);
                }
            }
            y += 2f * (chip * 0.6f + 6f * scale) + 10f * scale;

            float sliderW = w - pad * 2f - 90f * scale;
            float nh = Slider(panel.x + pad, ref y, sliderW, "Hue", hue, label, scale);
            float ns = Slider(panel.x + pad, ref y, sliderW, "Sat", sat, label, scale);
            float nv = Slider(panel.x + pad, ref y, sliderW, "Bright", val, label, scale);
            if (!Mathf.Approximately(nh, hue) || !Mathf.Approximately(ns, sat) || !Mathf.Approximately(nv, val))
            {
                hue = nh; sat = ns; val = nv;
                color = Color.HSVToRGB(hue, sat, val);
                colorName = $"#{ColorUtility.ToHtmlStringRGB(color)}";
            }

            DrawBox(new Rect(panel.x + pad, y + 4f * scale, w - pad * 2f, 34f * scale), color);
            y += 46f * scale;
            if (GUI.Button(new Rect(panel.x + w - pad - 120f * scale, panel.yMax - pad - 34f * scale, 120f * scale, 34f * scale), "Done (C)"))
                ClosePicker();
        }

        static float Slider(float x, ref float y, float width, string name, float value, GUIStyle label, float scale)
        {
            GUI.Label(new Rect(x, y, 80f * scale, 26f * scale), name, label);
            float v = GUI.HorizontalSlider(new Rect(x + 80f * scale, y + 8f * scale, width, 20f * scale), value, 0f, 1f);
            y += 34f * scale;
            return v;
        }

        void DrawBox(Rect r, Color c)
        {
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, swatch);
            GUI.color = old;
        }
    }
}
