using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace Cabin.Core
{
    /// <summary>
    /// F6 switches the post-processing look (tonemapping, bloom, grading, vignette) off and on while playing, with
    /// a corner label saying which, so the difference can be judged. Editor and CABIN_DEV builds only.
    /// </summary>
    public sealed class LookToggle : MonoBehaviour
    {
#if UNITY_EDITOR || CABIN_DEV
        [SerializeField] Volume volume;
        float shownUntil;

        public void Set(Volume v) => volume = v;

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null || volume == null || !kb.f6Key.wasPressedThisFrame) return;
            volume.enabled = !volume.enabled;
            shownUntil = Time.unscaledTime + 3f;
        }

        void OnGUI()
        {
            if (volume == null || Time.unscaledTime > shownUntil) return;
            var style = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold };
            style.normal.textColor = Color.white;
            GUI.Label(new Rect(20f, 20f, 400f, 40f), volume.enabled ? "Look: ON (F6)" : "Look: OFF (F6)", style);
        }
#endif
    }
}
