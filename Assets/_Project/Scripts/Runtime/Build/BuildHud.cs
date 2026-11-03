using Cabin.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Cabin.Build
{
    /// <summary>
    /// The HUD (Sam's pick "A · Clean" from the mockups): a dot crosshair, an 8-slot hotbar at the bottom with the
    /// item's name fading in above it on a switch, one short prompt under the crosshair only when there's something
    /// to do (keys drawn as caps: "[E] Pick up"), a message pill, the punch bar, and the deck-block guide labels.
    /// F1 shows the controls, F3 the dev readout (pieces, nails, fps). IMGUI.
    /// </summary>
    public sealed class BuildHud : MonoBehaviour
    {
        [SerializeField] BuildToolbelt toolbelt;

        const int Slots = BuildToolbelt.SlotCount;
        const float NameHold = 1.4f, NameFade = 0.4f;
        static readonly Color SlotFill = new Color(0.04f, 0.05f, 0.06f, 0.42f);
        static readonly Color SlotFillSelected = new Color(0.04f, 0.05f, 0.06f, 0.68f);
        static readonly Color SlotEdge = new Color(1f, 1f, 1f, 0.17f);
        static readonly Color Shadow = new Color(0f, 0f, 0f, 0.8f);

        static readonly string[] Controls =
        {
            "WASD move · Space jump · Shift sprint · Ctrl crouch · Tab fly (editor) · Esc free the mouse",
            "1–8 hotbar (an empty slot is your hands) · F1 this help · F3 dev readout",
            "Hands — E pick up / take from a rack / drop / take clamps off · LMB clamp or place · hold LMB punch · X pull a nail",
            "Holding — scroll distance · R turn · T tip · Y roll (Shift 15°) · V square up · F align (Shift+F across) · RMB drag turn/tip · G pilot",
            "Nail gun — LMB fire (through one piece into another nails them together) · X pull a nail",
            "Saw — LMB cut · scroll slide the cut (Shift 1/8 in) · hold RMB + mouse angle it · R cut along another axis",
            "Spray paint — hold LMB spray · scroll colour · R fine/wide cap · C colour picker",
            "F5 save · F9 load · F8 twice clear everything",
        };

        // Not kept across script reloads: a half-restored set from an older HUD would skip MakeStyles.
        [System.NonSerialized] GUIStyle text, small, keyCap, nameStyle, center;
        float fps;
        bool dev, help;
        int shownSlot = -1;
        float nameShownAt = -10f;

        void Update()
        {
            fps = Mathf.Lerp(fps, 1f / Mathf.Max(Time.unscaledDeltaTime, 1e-4f), 0.05f);
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.f1Key.wasPressedThisFrame) help = !help;
                if (kb.f3Key.wasPressedThisFrame) dev = !dev;
            }
            if (toolbelt != null && toolbelt.CurrentIndex != shownSlot)
            {
                if (shownSlot >= 0) nameShownAt = Time.time; // not on the first frame
                shownSlot = toolbelt.CurrentIndex;
            }
        }

        void OnGUI()
        {
            if (SettingsMenu.IsOpen) return; // the settings menu has the screen
            if (toolbelt == null || toolbelt.Tools == null) return;
            float scale = Mathf.Max(0.75f, Screen.height / 1080f);
            if (text == null) MakeStyles();
            text.fontSize = Mathf.RoundToInt(17 * scale);
            center.fontSize = text.fontSize;
            small.fontSize = Mathf.RoundToInt(14 * scale);
            keyCap.fontSize = Mathf.RoundToInt(13 * scale);
            nameStyle.fontSize = Mathf.RoundToInt(18 * scale);

            float cx = Screen.width / 2f, cy = Screen.height / 2f;
            Crosshair(cx, cy, scale);
            Guides(scale);

            var tool = toolbelt.Current;
            Prompt(tool.Status, cx, cy + 30 * scale, scale);
            if (tool is HandsTool hands && hands.PunchBar >= 0f) PunchBar(hands.PunchBar, cx, cy + 74 * scale, scale);
            if (toolbelt.Message != null) Pill(toolbelt.Message, cx, Screen.height * 0.3f, scale);

            float hotbarTop = Hotbar(scale);
            ItemName(cx, hotbarTop - 12 * scale, scale);

            if (dev)
            {
                var site = toolbelt.Site;
                FrameStats.Sample();
                Label(new Rect(Screen.width - 820 * scale - 12 * scale, 8 * scale, 820 * scale, 30 * scale),
                    $"{fps:F0} fps · {site.Pieces.Count} pieces · {site.Nails.Count} nails", small, TextAnchor.UpperRight);
                Label(new Rect(Screen.width - 820 * scale - 12 * scale, 30 * scale, 820 * scale, 30 * scale), FrameStats.Line(), small, TextAnchor.UpperRight);
                Label(new Rect(Screen.width - 820 * scale - 12 * scale, 52 * scale, 820 * scale, 30 * scale), FrameStats.Device(), small, TextAnchor.UpperRight);
            }
            if (!CursorCapture.Captured && !CursorCapture.UiOpen)
            {
                Pill("Click to play", cx, Screen.height * 0.4f, scale);
                Label(new Rect(0, Screen.height * 0.4f + 34 * scale, Screen.width, 30 * scale), "F1 controls", small, TextAnchor.UpperCenter);
            }
            if (help) Help(scale);
            tool.OnGui(scale);
        }

        // ---------------------------------------------------------------- pieces

        static void Box(Rect r, Color c, float radius, float border = 0f) =>
            GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, c, border, radius);

        static void Crosshair(float cx, float cy, float scale)
        {
            float d = 6f * scale, ring = d + 3f * scale;
            Box(new Rect(cx - ring / 2f, cy - ring / 2f, ring, ring), new Color(0f, 0f, 0f, 0.45f), ring / 2f);
            Box(new Rect(cx - d / 2f, cy - d / 2f, d, d), Color.white, d / 2f);
        }

        /// <summary>The slots, centred at the bottom. Returns the top edge.</summary>
        float Hotbar(float scale)
        {
            float size = 58f * scale, gap = 6f * scale, radius = 8f * scale;
            float width = Slots * size + (Slots - 1) * gap;
            float x0 = (Screen.width - width) / 2f, y = Screen.height - Screen.height * 0.032f - size;
            for (int i = 0; i < Slots; i++)
            {
                var r = new Rect(x0 + i * (size + gap), y, size, size);
                bool selected = i == toolbelt.CurrentIndex;
                Box(r, selected ? SlotFillSelected : SlotFill, radius);
                Box(r, selected ? Color.white : SlotEdge, radius, selected ? 2f * scale : 1f);
                var item = toolbelt.SlotTool(i);
                if (item != null && item.Icon != null)
                {
                    float inset = size * 0.13f;
                    GUI.DrawTexture(new Rect(r.x + inset, r.y + inset, size - 2 * inset, size - 2 * inset), item.Icon, ScaleMode.ScaleToFit, true);
                }
                if (item is SprayTool spray)
                {
                    float s = 10f * scale;
                    var dot = new Rect(r.xMax - s - 5 * scale, r.yMax - s - 5 * scale, s, s);
                    Box(new Rect(dot.x - 1.5f * scale, dot.y - 1.5f * scale, s + 3 * scale, s + 3 * scale), new Color(0, 0, 0, 0.7f), s);
                    Box(dot, spray.Color, s / 2f);
                }
                var prev = small.normal.textColor;
                small.normal.textColor = new Color(1f, 1f, 1f, 0.65f);
                small.fontSize = Mathf.RoundToInt(11 * scale);
                GUI.Label(new Rect(r.x + 5 * scale, r.y + 2 * scale, size, 16 * scale), (i + 1).ToString(), small);
                small.fontSize = Mathf.RoundToInt(14 * scale);
                small.normal.textColor = prev;
            }
            return y;
        }

        void ItemName(float cx, float bottom, float scale)
        {
            float age = Time.time - nameShownAt;
            if (age > NameHold + NameFade) return;
            float alpha = age < NameHold ? 1f : 1f - (age - NameHold) / NameFade;
            var item = toolbelt.Current;
            var prev = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, alpha);
            Label(new Rect(cx - 300 * scale, bottom - 26 * scale, 600 * scale, 26 * scale), item.Name, nameStyle, TextAnchor.LowerCenter);
            GUI.color = prev;
        }

        /// <summary>
        /// A prompt line: "[E] Pick up · 2×4" draws E as a key cap. A second line (after a newline) is smaller and
        /// dimmer — a readout, not an instruction.
        /// </summary>
        void Prompt(string prompt, float cx, float top, float scale)
        {
            if (string.IsNullOrEmpty(prompt)) return;
            var lines = prompt.Split('\n');
            DrawKeyed(lines[0], cx, top, scale, text);
            if (lines.Length > 1)
            {
                var prev = small.normal.textColor;
                small.normal.textColor = new Color(1f, 1f, 1f, 0.7f);
                Label(new Rect(cx - 400 * scale, top + 26 * scale, 800 * scale, 24 * scale), lines[1], small, TextAnchor.UpperCenter);
                small.normal.textColor = prev;
            }
        }

        void DrawKeyed(string line, float cx, float top, float scale, GUIStyle style)
        {
            // Split into text runs and [KEY] caps, measure, then draw centred.
            var parts = new System.Collections.Generic.List<(string s, bool cap)>();
            int i = 0;
            while (i < line.Length)
            {
                int open = line.IndexOf('[', i);
                int close = open >= 0 ? line.IndexOf(']', open) : -1;
                if (open < 0 || close < 0) { parts.Add((line.Substring(i), false)); break; }
                if (open > i) parts.Add((line.Substring(i, open - i), false));
                parts.Add((line.Substring(open + 1, close - open - 1), true));
                i = close + 1;
            }
            float pad = 6f * scale, h = 24f * scale;
            float total = 0f;
            foreach (var p in parts)
                total += p.cap ? keyCap.CalcSize(new GUIContent(p.s)).x + 2 * pad + 6 * scale : style.CalcSize(new GUIContent(p.s)).x;
            float x = cx - total / 2f;
            foreach (var p in parts)
            {
                if (p.cap)
                {
                    float w = keyCap.CalcSize(new GUIContent(p.s)).x + 2 * pad;
                    var r = new Rect(x, top + 1 * scale, w, h - 2 * scale);
                    Box(new Rect(r.x + 1, r.y + 2, r.width, r.height), Shadow, 4 * scale);
                    Box(r, Color.white, 4 * scale);
                    GUI.Label(r, p.s, keyCap);
                    x += w + 6 * scale;
                }
                else
                {
                    float w = style.CalcSize(new GUIContent(p.s)).x;
                    Label(new Rect(x, top, w + 2, h), p.s, style);
                    x += w;
                }
            }
        }

        void Pill(string message, float cx, float y, float scale)
        {
            var size = text.CalcSize(new GUIContent(message));
            float w = size.x + 24 * scale, h = 30 * scale;
            var r = new Rect(cx - w / 2f, y, w, h);
            Box(r, new Color(0f, 0f, 0f, 0.55f), h / 2f);
            GUI.Label(r, message, center);
        }

        static void PunchBar(float fill, float cx, float y, float scale)
        {
            float w = 120f * scale, h = 4f * scale;
            var r = new Rect(cx - w / 2f, y, w, h);
            Box(r, new Color(1f, 1f, 1f, 0.22f), h / 2f);
            Box(new Rect(r.x, r.y, w * Mathf.Clamp01(fill), h), Color.white, h / 2f);
        }

        void Guides(float scale)
        {
            var cam = Camera.main;
            if (cam == null) return;
            foreach (var g in toolbelt.Carry.Guides)
            {
                Vector3 sp = cam.WorldToScreenPoint((g.From + g.To) * 0.5f + Vector3.up * 0.15f);
                if (sp.z <= 0f) continue;
                string label = g.Aligned ? $"in line ✓  {Units.FeetInches(g.NominalDistance)}" : $"{g.Degrees:F1}° off  {Units.FeetInches(g.NominalDistance)}";
                var prev = small.normal.textColor;
                small.normal.textColor = g.Aligned ? new Color(0.55f, 1f, 0.6f) : new Color(1f, 0.85f, 0.3f);
                Label(new Rect(sp.x - 150 * scale, Screen.height - sp.y - 12 * scale, 300 * scale, 24 * scale), label, small, TextAnchor.UpperCenter);
                small.normal.textColor = prev;
            }
        }

        void Help(float scale)
        {
            float w = Mathf.Min(Screen.width - 40 * scale, 1100 * scale), line = 26 * scale;
            float h = (Controls.Length + 2) * line + 20 * scale;
            var r = new Rect((Screen.width - w) / 2f, Screen.height * 0.18f, w, h);
            Box(r, new Color(0.04f, 0.05f, 0.06f, 0.82f), 10 * scale);
            Label(new Rect(r.x + 20 * scale, r.y + 12 * scale, w, line), "Controls — F1 to close", nameStyle);
            for (int i = 0; i < Controls.Length; i++)
                Label(new Rect(r.x + 20 * scale, r.y + 12 * scale + (i + 1.4f) * line, w - 40 * scale, line), Controls[i], small);
        }

        static void Label(Rect r, string s, GUIStyle style, TextAnchor? anchor = null)
        {
            var oldAnchor = style.alignment;
            if (anchor.HasValue) style.alignment = anchor.Value;
            var color = style.normal.textColor;
            style.normal.textColor = new Color(0f, 0f, 0f, 0.8f * color.a);
            GUI.Label(new Rect(r.x + 1, r.y + 2, r.width, r.height), s, style);
            style.normal.textColor = color;
            GUI.Label(r, s, style);
            style.alignment = oldAnchor;
        }

        void MakeStyles()
        {
            text = new GUIStyle(GUI.skin.label) { richText = false, wordWrap = false, clipping = TextClipping.Overflow };
            text.normal.textColor = Color.white;
            center = new GUIStyle(text) { alignment = TextAnchor.MiddleCenter };
            small = new GUIStyle(text);
            nameStyle = new GUIStyle(text) { fontStyle = FontStyle.Bold };
            keyCap = new GUIStyle(text) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            keyCap.normal.textColor = new Color(0.07f, 0.07f, 0.08f);
        }
    }
}
