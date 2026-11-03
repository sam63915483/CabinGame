using UnityEngine;

namespace Cabin.Build
{
    /// <summary>
    /// A C-clamp built from primitives to fit one <see cref="ClampGrip"/>: pads on both outer faces, arms out to a
    /// bar that wraps round the wood, a screw and handle on the clamped piece's side. Grows in when placed and
    /// shrinks away when released. Has a trigger collider so the player can look at it.
    /// </summary>
    public sealed class ClampVisual : MonoBehaviour
    {
        const float GrowTime = 0.22f;

        float t;
        bool releasing;

        public static ClampVisual Create(Transform parent, in ClampGrip grip, Material frame, Material steel)
        {
            var go = new GameObject("Clamp") { layer = BuildLayers.Clamp };
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(grip.Point, Quaternion.LookRotation(grip.Normal, Vector3.Cross(grip.Normal, grip.Side)));
            // Local frame: z = jaw axis (other → held), x = towards the bar, y = across.
            if (Vector3.Dot(go.transform.right, grip.Side) < 0f)
                go.transform.rotation = Quaternion.LookRotation(grip.Normal, -Vector3.Cross(grip.Normal, grip.Side));

            float k = Stock.CrossSectionScale / 3f;
            float padR = ClampLayout.PadRadius, padT = ClampLayout.PadThickness, armT = ClampLayout.ArmThickness, armW = ClampLayout.ArmWidth;
            float screwR = 0.008f * k, screwL = ClampLayout.ScrewLength;
            float reach = grip.Reach;
            float zH = grip.JawHeld, zO = grip.JawOther;
            float armZH = zH + padT + armT * 0.5f, armZO = zO - padT - armT * 0.5f;

            Part(go, PrimitiveType.Cylinder, steel, new Vector3(0f, 0f, zH + padT * 0.5f), new Vector3(padR * 2f, padT * 0.5f, padR * 2f), true);
            Part(go, PrimitiveType.Cylinder, steel, new Vector3(0f, 0f, zO - padT * 0.5f), new Vector3(padR * 2f, padT * 0.5f, padR * 2f), true);
            Part(go, PrimitiveType.Cube, frame, new Vector3(reach * 0.5f, 0f, armZH), new Vector3(reach + armT, armW, armT), false);
            Part(go, PrimitiveType.Cube, frame, new Vector3(reach * 0.5f, 0f, armZO), new Vector3(reach + armT, armW, armT), false);
            Part(go, PrimitiveType.Cube, frame, new Vector3(reach, 0f, (armZH + armZO) * 0.5f), new Vector3(armT * 1.3f, armW, armZH - armZO + armT), false);
            Part(go, PrimitiveType.Cylinder, steel, new Vector3(0f, 0f, armZH + screwL * 0.5f), new Vector3(screwR * 2f, screwL * 0.5f, screwR * 2f), true);
            Part(go, PrimitiveType.Cube, frame, new Vector3(0f, 0f, armZH + screwL), new Vector3(armW * 1.8f, screwR * 2.4f, screwR * 2.4f), false);

            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.center = new Vector3(reach * 0.5f, 0f, (armZH + screwL + armZO) * 0.5f);
            box.size = new Vector3(reach + padR * 2f, armW * 1.5f, armZH + screwL - armZO + armT);

            var visual = go.AddComponent<ClampVisual>();
            go.transform.localScale = Vector3.one * 0.01f;
            return visual;
        }

        static void Part(GameObject parent, PrimitiveType type, Material mat, Vector3 localPos, Vector3 scale, bool alongZ)
        {
            var p = GameObject.CreatePrimitive(type);
            Destroy(p.GetComponent<Collider>());
            p.layer = BuildLayers.Clamp;
            p.transform.SetParent(parent.transform, false);
            p.transform.localPosition = localPos;
            if (alongZ) p.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            p.transform.localScale = scale;
            p.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }

        /// <summary>Shrink away, then destroy.</summary>
        public void Release()
        {
            releasing = true;
            t = Mathf.Min(t, GrowTime);
        }

        /// <summary>Remove now, no animation (loads, clears).</summary>
        public void Vanish() => Destroy(gameObject);

        void Update()
        {
            t += releasing ? -Time.deltaTime : Time.deltaTime;
            float s = Mathf.Clamp01(t / GrowTime);
            s = 1f - (1f - s) * (1f - s);
            transform.localScale = Vector3.one * Mathf.Max(0.01f, s);
            if (releasing && t <= 0f) Destroy(gameObject);
        }
    }
}
