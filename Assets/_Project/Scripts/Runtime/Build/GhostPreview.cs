using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Cabin.Build
{
    /// <summary>Translucent previews (nail line, saw blade) drawn with instanced draws, cleared every frame.</summary>
    public sealed class GhostPreview : MonoBehaviour
    {
        [SerializeField] Material validMaterial;
        [SerializeField] Material invalidMaterial;
        [SerializeField] Material bladeMaterial;
        [SerializeField] Mesh cubeMesh;

        readonly List<(Material, Matrix4x4)> queued = new List<(Material, Matrix4x4)>();

        public void DrawNail(Vector3 entry, Vector3 direction, bool valid)
        {
            var rot = Quaternion.LookRotation(direction);
            float s = Stock.CrossSectionScale;
            queued.Add((valid ? validMaterial : invalidMaterial, Matrix4x4.TRS(entry + direction * NailRules.NailLength * 0.5f, rot, new Vector3(0.006f * s, 0.006f * s, NailRules.NailLength))));
            queued.Add((valid ? validMaterial : invalidMaterial, Matrix4x4.TRS(entry, rot, new Vector3(0.03f * s, 0.03f * s, 0.003f))));
        }

        /// <summary>A dashed line on the ground between two points: green when in line, yellow when not.</summary>
        public void DrawDotted(Vector3 a, Vector3 b, bool good)
        {
            Vector3 d = b - a;
            float len = d.magnitude;
            if (len < 0.01f) return;
            var rot = Quaternion.LookRotation(d / len);
            const float dash = 0.12f, gap = 0.08f, thick = 0.03f;
            var mat = good ? validMaterial : bladeMaterial;
            for (float t = 0f; t < len; t += dash + gap)
            {
                float l = Mathf.Min(dash, len - t);
                queued.Add((mat, Matrix4x4.TRS(a + d / len * (t + l * 0.5f), rot, new Vector3(thick, thick, l))));
            }
        }

        public void DrawBlade(Vector3 point, Quaternion rotation, float size) =>
            queued.Add((bladeMaterial, Matrix4x4.TRS(point, rotation, new Vector3(size, size, 0.0015f))));

        void LateUpdate()
        {
            foreach (var (mat, m) in queued)
            {
                if (mat == null) continue;
                var rp = new RenderParams(mat) { shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false };
                Graphics.RenderMesh(rp, cubeMesh, 0, m);
            }
            queued.Clear();
        }
    }
}
