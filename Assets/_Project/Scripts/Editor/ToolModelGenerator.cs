using System.Collections.Generic;
using System.Linq;
using Cabin.Build;
using Cabin.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Cabin.Editor
{
    /// <summary>
    /// Builds the first-person tool models from code: a framing nail gun and an aerosol spray can, at real size.
    /// Meshes go into one asset per model, materials into Art/Materials, prefabs into Prefabs/Tools. Re-runnable.
    /// Model space: +z is where the tool points (nail drive direction / nozzle), +y up, origin where the hand grips.
    /// </summary>
    public static class ToolModelGenerator
    {
        const string ModelsDir = "Assets/_Project/Art/Models";
        const string ToolsDir = "Assets/_Project/Prefabs/Tools";
        public const string NailGunPrefabPath = ToolsDir + "/NailGun.prefab";
        public const string SprayCanPrefabPath = ToolsDir + "/SprayCan.prefab";
        public const string AxePrefabPath = ToolsDir + "/Axe.prefab";

        [MenuItem("Tools/Cabin/Build/Generate Tool Models", priority = 11)]
        public static void GenerateAll()
        {
            SandboxAssetGenerator.EnsureFolder(ModelsDir);
            SandboxAssetGenerator.EnsureFolder(ToolsDir);
            BuildNailGun();
            BuildSprayCan();
            BuildAxe();
            AssetDatabase.SaveAssets();
            IconGenerator.GenerateAll();
            Debug.Log("[Cabin] Tool models generated");
        }

        // ------------------------------------------------------------------ nail gun

        static void BuildNailGun()
        {
            var red = Mat("Tool_RedPlastic", new Color(0.72f, 0.06f, 0.05f), 0f, 0.62f);
            var black = Mat("Tool_BlackRubber", new Color(0.045f, 0.045f, 0.05f), 0f, 0.22f);
            var blackGloss = Mat("Tool_BlackPlastic", new Color(0.03f, 0.03f, 0.035f), 0f, 0.55f);
            var steel = Mat("Tool_Steel", new Color(0.62f, 0.63f, 0.65f), 0.9f, 0.62f);
            var alu = Mat("Tool_Aluminium", new Color(0.55f, 0.57f, 0.6f), 0.85f, 0.45f);
            var brass = Mat("Tool_Brass", new Color(0.78f, 0.6f, 0.26f), 1f, 0.6f);

            var meshes = new List<Mesh>();
            var root = new GameObject("NailGun");

            // A framing nailer held at a wall is pistol-shaped: short piston head on top, the drive housing tapering
            // forward to the nose, the handle dropping down under the head, the magazine running diagonally from the
            // nose back to the bottom of the handle.

            // Piston head along the drive axis, above the hand.
            Lathed(root, meshes, "Head", red, new[]
            {
                new Vector2(0.000f, -0.060f), new Vector2(0.040f, -0.060f), new Vector2(0.049f, -0.054f),
                new Vector2(0.051f, -0.040f), new Vector2(0.051f, 0.035f), new Vector2(0.047f, 0.045f),
                new Vector2(0.040f, 0.050f), new Vector2(0.000f, 0.050f),
            }, new Vector3(0f, 0.070f, 0.000f), Quaternion.Euler(90f, 0f, 0f));
            // Black exhaust cap on the back of the head.
            Lathed(root, meshes, "Cap", blackGloss, new[]
            {
                new Vector2(0.000f, 0.000f), new Vector2(0.049f, 0.000f), new Vector2(0.0525f, 0.006f),
                new Vector2(0.0525f, 0.014f), new Vector2(0.044f, 0.020f), new Vector2(0.028f, 0.022f),
                new Vector2(0.000f, 0.022f),
            }, new Vector3(0f, 0.070f, -0.058f), Quaternion.Euler(-90f, 0f, 0f));
            // A black band round the head.
            Lathed(root, meshes, "HeadBand", blackGloss, new[] { new Vector2(0.0518f, 0f), new Vector2(0.0518f, 0.012f) },
                new Vector3(0f, 0.070f, 0.018f), Quaternion.Euler(90f, 0f, 0f), caps: false);

            // Drive housing, tapering forward from the head to the nose.
            Block(root, meshes, "DriveHousing", red, ModelKit.Tapered(new Vector3(0.066f, 0.100f, 0.150f), new Vector2(0.58f, 0.44f), 0.009f),
                new Vector3(0f, 0.050f, 0.125f), Quaternion.identity);
            // Steel nose and the spring safety yoke standing proud of it.
            Lathed(root, meshes, "Nose", steel, new[]
            {
                new Vector2(0.000f, 0.000f), new Vector2(0.017f, 0.000f), new Vector2(0.017f, 0.025f),
                new Vector2(0.012f, 0.045f), new Vector2(0.009f, 0.055f), new Vector2(0.000f, 0.055f),
            }, new Vector3(0f, 0.050f, 0.195f), Quaternion.Euler(90f, 0f, 0f));
            foreach (float side in new[] { -1f, 1f })
                Block(root, meshes, "Yoke", steel, ModelKit.Chamfered(new Vector3(0.004f, 0.012f, 0.070f), 0.0012f),
                    new Vector3(side * 0.0145f, 0.050f, 0.255f), Quaternion.identity);
            Block(root, meshes, "YokeTip", steel, ModelKit.Chamfered(new Vector3(0.033f, 0.012f, 0.006f), 0.0015f),
                new Vector3(0f, 0.050f, 0.290f), Quaternion.identity);
            // Depth-of-drive wheel on the side of the housing.
            Block(root, meshes, "DepthWheel", black, Disc(0.012f, 0.008f), new Vector3(0.026f, 0.040f, 0.165f), Quaternion.Euler(0f, 0f, 90f));

            // Pistol grip under the head, raked back a little, rubber over-moulded.
            var handleRot = Quaternion.Euler(14f, 0f, 0f);
            Block(root, meshes, "Handle", red, ModelKit.Chamfered(new Vector3(0.036f, 0.150f, 0.050f), 0.010f),
                new Vector3(0f, -0.030f, -0.015f), handleRot);
            Block(root, meshes, "Grip", black, ModelKit.Chamfered(new Vector3(0.041f, 0.100f, 0.055f), 0.012f),
                new Vector3(0f, -0.055f, -0.021f), handleRot);
            // Trigger in front of the grip.
            Block(root, meshes, "Trigger", blackGloss, ModelKit.Tapered(new Vector3(0.012f, 0.030f, 0.022f), new Vector2(0.8f, 0.6f), 0.003f),
                new Vector3(0f, 0.002f, 0.030f), handleRot);
            // Brass air fitting out of the bottom of the handle.
            Vector3 handleBottom = new Vector3(0f, -0.030f, -0.015f) + handleRot * new Vector3(0f, -0.075f, 0f);
            Lathed(root, meshes, "AirFitting", brass, new[]
            {
                new Vector2(0.000f, 0.000f), new Vector2(0.011f, 0.000f), new Vector2(0.011f, 0.010f),
                new Vector2(0.008f, 0.012f), new Vector2(0.008f, 0.024f), new Vector2(0.006f, 0.026f), new Vector2(0.000f, 0.026f),
            }, handleBottom, handleRot * Quaternion.Euler(180f, 0f, 0f));

            // Stick magazine from under the nose to the bottom of the handle, nail strip along its top edge.
            Vector3 magStart = new Vector3(0f, 0.020f, 0.200f), magEnd = new Vector3(0f, -0.095f, -0.002f);
            Vector3 magMid = (magStart + magEnd) * 0.5f;
            var magRot = Quaternion.LookRotation(magStart - magEnd, Vector3.up);
            float magLen = Vector3.Distance(magStart, magEnd);
            Block(root, meshes, "Magazine", alu, ModelKit.Chamfered(new Vector3(0.024f, 0.044f, magLen), 0.004f), magMid, magRot);
            Block(root, meshes, "MagazineFollower", black, ModelKit.Chamfered(new Vector3(0.028f, 0.022f, 0.040f), 0.004f),
                magEnd + (magStart - magEnd).normalized * 0.04f + magRot * new Vector3(0f, -0.018f, 0f), magRot);
            var nailMesh = ModelKit.Cylinder("NailHead", 0.0035f, 0.002f, 12);
            meshes.Add(nailMesh);
            for (int i = 0; i < 16; i++)
            {
                float t = 0.12f + i * 0.05f;
                Vector3 p = magStart + (magEnd - magStart) * t + magRot * new Vector3(0f, 0.023f, 0f);
                Part(root, "Nail", nailMesh, steel, p, magRot);
            }

            Save(root, meshes, ModelsDir + "/NailGun.asset", NailGunPrefabPath);
        }

        // ------------------------------------------------------------------ spray can

        static void BuildSprayCan()
        {
            var metal = Mat("Tool_CanMetal", new Color(0.78f, 0.79f, 0.8f), 0.95f, 0.72f);
            var label = Mat("Tool_CanLabel", Color.white, 0f, 0.45f);
            var white = Mat("Tool_CanTrim", new Color(0.92f, 0.92f, 0.9f), 0f, 0.5f);
            var nozzle = Mat("Tool_BlackPlastic", new Color(0.03f, 0.03f, 0.035f), 0f, 0.55f);

            var meshes = new List<Mesh>();
            var root = new GameObject("SprayCan");
            // Body, from the domed-in bottom up through the rolled rims and shoulder to the valve cup (metres, real size).
            Lathed(root, meshes, "Can", metal, new[]
            {
                new Vector2(0.000f, 0.007f), new Vector2(0.018f, 0.006f), new Vector2(0.027f, 0.003f),
                new Vector2(0.031f, 0.000f), new Vector2(0.0335f, 0.002f), new Vector2(0.0335f, 0.007f),
                new Vector2(0.0328f, 0.009f), new Vector2(0.0328f, 0.150f), new Vector2(0.0336f, 0.152f),
                new Vector2(0.0336f, 0.156f), new Vector2(0.031f, 0.161f), new Vector2(0.026f, 0.168f),
                new Vector2(0.018f, 0.174f), new Vector2(0.0135f, 0.176f), new Vector2(0.0135f, 0.181f),
                new Vector2(0.011f, 0.183f), new Vector2(0.000f, 0.183f),
            }, new Vector3(0f, -0.09f, 0f), Quaternion.identity, segments: 56);
            // Colour label band (takes the paint colour) and white trim rings at its edges.
            Lathed(root, meshes, "Label", label, new[] { new Vector2(0.0332f, 0.040f), new Vector2(0.0332f, 0.128f) },
                new Vector3(0f, -0.09f, 0f), Quaternion.identity, caps: false, segments: 56);
            foreach (float y in new[] { 0.038f, 0.128f })
                Lathed(root, meshes, "Trim", white, new[] { new Vector2(0.0334f, y), new Vector2(0.0334f, y + 0.004f) },
                    new Vector3(0f, -0.09f, 0f), Quaternion.identity, caps: false, segments: 56);
            // Actuator button with the nozzle facing forward.
            var actuator = Lathed(root, meshes, "Actuator", nozzle, new[]
            {
                new Vector2(0.000f, 0.183f), new Vector2(0.0092f, 0.183f), new Vector2(0.0092f, 0.195f),
                new Vector2(0.0082f, 0.199f), new Vector2(0.000f, 0.199f),
            }, new Vector3(0f, -0.09f, 0f), Quaternion.identity);
            var tip = Lathed(root, meshes, "NozzleTip", nozzle, new[] { new Vector2(0.0f, 0f), new Vector2(0.0028f, 0f), new Vector2(0.0028f, 0.004f), new Vector2(0f, 0.004f) },
                new Vector3(0f, 0.101f, 0.0085f), Quaternion.Euler(90f, 0f, 0f), segments: 16);
            tip.transform.SetParent(actuator.transform, true);

            Save(root, meshes, ModelsDir + "/SprayCan.asset", SprayCanPrefabPath);
        }

        // ------------------------------------------------------------------ helpers

        // ------------------------------------------------------------------ axe

        /// <summary>
        /// A felling axe, real size: a 30 in hickory handle swelling at the throat and the knob, a 3½ lb head with a
        /// ground bevel and a painted poll. Origin at the grip, the handle running up +y to the head, the blade edge
        /// facing +z (the way it swings).
        /// </summary>
        static void BuildAxe()
        {
            var hickory = Mat("Tool_Hickory", new Color(0.70f, 0.50f, 0.29f), 0f, 0.38f);
            var steel = Mat("Tool_AxeSteel", new Color(0.32f, 0.33f, 0.35f), 0.85f, 0.45f);
            var bright = Mat("Tool_AxeEdge", new Color(0.86f, 0.87f, 0.89f), 1f, 0.82f);
            var paint = Mat("Tool_AxePaint", new Color(0.58f, 0.06f, 0.05f), 0.1f, 0.55f);

            var meshes = new List<Mesh>();
            var root = new GameObject("Axe");

            // A felling axe, about 28 in (handle 0.77 m). Handle: hickory, oval (deeper front to back than side to side),
            // a gentle S down its length and a swelled knob hooking back at the end so it doesn't slip out of the hand.
            // y up the handle from the grip (origin), the blade towards +z.
            var spine = new List<(float y, float z, float side, float front)>
            {
                (-0.112f, -0.016f, 0.0170f, 0.0215f), (-0.104f, -0.015f, 0.0200f, 0.0255f), (-0.090f, -0.011f, 0.0195f, 0.0250f),
                (-0.070f, -0.006f, 0.0150f, 0.0195f), (-0.030f, -0.001f, 0.0137f, 0.0182f), (0.050f, 0.003f, 0.0133f, 0.0176f),
                (0.200f, 0.005f, 0.0128f, 0.0170f), (0.350f, 0.004f, 0.0130f, 0.0174f), (0.480f, 0.001f, 0.0142f, 0.0190f),
                (0.570f, 0.000f, 0.0160f, 0.0212f), (0.600f, 0.000f, 0.0150f, 0.0205f), (0.655f, 0.000f, 0.0150f, 0.0205f),
            };
            meshes.Add(Swept("Handle", spine, 14));
            Part(root, "Handle", meshes[meshes.Count - 1], hickory, Vector3.zero, Quaternion.identity);
            // The wedge driven into the top of the eye.
            Block(root, meshes, "Wedge", steel, ModelKit.Chamfered(new Vector3(0.006f, 0.006f, 0.034f), 0.0012f), new Vector3(0f, 0.658f, 0f), Quaternion.identity);

            // Head: lofted from the poll to the edge — square-ish round the eye, the cheeks thinning fast towards the bit
            // while it grows taller and drops a little (the beard), a ground bevel bright at the edge, the rest painted.
            Vector3 eye = new Vector3(0f, 0.615f, 0f);
            var head = new List<(float z, float width, float height, float drop)>
            {
                (-0.046f, 0.034f, 0.058f, 0f), (-0.040f, 0.037f, 0.063f, 0f), (-0.020f, 0.040f, 0.068f, 0f), (0.020f, 0.039f, 0.068f, 0f),
                (0.040f, 0.030f, 0.070f, 0.001f), (0.065f, 0.019f, 0.078f, 0.003f), (0.090f, 0.011f, 0.094f, 0.006f), (0.108f, 0.0065f, 0.108f, 0.008f),
            };
            var bevel = new List<(float z, float width, float height, float drop)>
            {
                (0.108f, 0.0065f, 0.108f, 0.008f), (0.124f, 0.0022f, 0.118f, 0.009f), (0.131f, 0.0006f, 0.120f, 0.009f),
            };
            meshes.Add(Lofted("Head", head, true, false));
            Part(root, "Head", meshes[meshes.Count - 1], paint, eye, Quaternion.identity);
            meshes.Add(Lofted("Bevel", bevel, false, true));
            Part(root, "Bevel", meshes[meshes.Count - 1], bright, eye, Quaternion.identity);
            // Bare steel on the poll's striking face.
            Block(root, meshes, "PollFace", steel, ModelKit.Chamfered(new Vector3(0.034f, 0.058f, 0.003f), 0.004f), eye + new Vector3(0f, 0f, -0.0475f), Quaternion.identity);

            Save(root, meshes, ModelsDir + "/Axe.asset", AxePrefabPath);
        }

        /// <summary>
        /// A tube along a curve in the y–z plane: at each station its height y, how far forward the middle is (z), and
        /// the oval's half-widths side to side (x) and front to back (z). Smooth round, closed both ends.
        /// </summary>
        static Mesh Swept(string name, List<(float y, float z, float side, float front)> stations, int segments)
        {
            var v = new List<Vector3>();
            var tris = new List<int>();
            int ring = segments + 1;
            foreach (var st in stations)
                for (int i = 0; i <= segments; i++)
                {
                    float a = i / (float)segments * Mathf.PI * 2f;
                    v.Add(new Vector3(Mathf.Cos(a) * st.side, st.y, st.z + Mathf.Sin(a) * st.front));
                }
            for (int k = 0; k + 1 < stations.Count; k++)
            for (int i = 0; i < segments; i++)
            {
                int a = k * ring + i, b = a + 1, c = a + ring, d = c + 1;
                tris.AddRange(new[] { a, c, b, b, c, d });
            }
            void Cap(int k, bool top)
            {
                int centre = v.Count;
                v.Add(new Vector3(0f, stations[k].y, stations[k].z));
                for (int i = 0; i < segments; i++)
                    tris.AddRange(top ? new[] { centre, k * ring + i + 1, k * ring + i } : new[] { centre, k * ring + i, k * ring + i + 1 });
            }
            Cap(0, false);
            Cap(stations.Count - 1, true);
            var mesh = new Mesh { name = name };
            mesh.SetVertices(v);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// A solid lofted along z through chamfered-rectangle sections (width across x, height up y, dropped by
        /// <c>drop</c>), flat-faced; capped at the start and/or end.
        /// </summary>
        static Mesh Lofted(string name, List<(float z, float width, float height, float drop)> sections, bool capStart, bool capEnd)
        {
            Vector3[] Section((float z, float width, float height, float drop) s)
            {
                float w = s.width * 0.5f, h = s.height * 0.5f, c = Mathf.Min(w, h) * 0.35f, y = -s.drop;
                return new[]
                {
                    new Vector3(w, y + h - c, s.z), new Vector3(w - c, y + h, s.z), new Vector3(-w + c, y + h, s.z), new Vector3(-w, y + h - c, s.z),
                    new Vector3(-w, y - h + c, s.z), new Vector3(-w + c, y - h, s.z), new Vector3(w - c, y - h, s.z), new Vector3(w, y - h + c, s.z),
                };
            }
            var v = new List<Vector3>();
            var tris = new List<int>();
            var rings = sections.Select(Section).ToList();
            for (int k = 0; k + 1 < rings.Count; k++)
            for (int i = 0; i < 8; i++)
            {
                int j = (i + 1) % 8, start = v.Count;
                v.Add(rings[k][i]); v.Add(rings[k][j]); v.Add(rings[k + 1][i]); v.Add(rings[k + 1][j]);
                tris.AddRange(new[] { start, start + 1, start + 2, start + 1, start + 3, start + 2 });
            }
            void Cap(Vector3[] r, bool facingBack)
            {
                int start = v.Count;
                v.AddRange(r);
                for (int i = 1; i + 1 < 8; i++)
                    tris.AddRange(facingBack ? new[] { start, start + i, start + i + 1 } : new[] { start, start + i + 1, start + i });
            }
            if (capStart) Cap(rings[0], true);
            if (capEnd) Cap(rings[rings.Count - 1], false);
            var mesh = new Mesh { name = name };
            mesh.SetVertices(v);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        static ConvexSolid Disc(float radius, float thickness)
        {
            // A 12-sided convex disc along y, so it stays a ConvexSolid.
            var s = ConvexSolid.Box(new Vector3(radius * 2f, thickness, radius * 2f));
            for (int i = 0; i < 12; i++)
            {
                float a = (i + 0.5f) / 12f * Mathf.PI * 2f;
                var n = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                s = s.Clip(new Plane(-n, n * radius * Mathf.Cos(Mathf.PI / 12f))) ?? s;
            }
            return s;
        }

        static GameObject Block(GameObject root, List<Mesh> meshes, string name, Material mat, ConvexSolid solid, Vector3 pos, Quaternion rot)
        {
            var mesh = ModelKit.Solid(name, solid);
            meshes.Add(mesh);
            return Part(root, name, mesh, mat, pos, rot);
        }

        static GameObject Lathed(GameObject root, List<Mesh> meshes, string name, Material mat, IList<Vector2> profile, Vector3 pos, Quaternion rot, bool caps = true, int segments = 40)
        {
            var mesh = ModelKit.Lathe(name, profile, segments, caps, caps);
            meshes.Add(mesh);
            return Part(root, name, mesh, mat, pos, rot);
        }

        static GameObject Part(GameObject root, string name, Mesh mesh, Material mat, Vector3 pos, Quaternion rot)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root.transform, false);
            go.transform.SetLocalPositionAndRotation(pos, rot);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            return go;
        }

        /// <summary>Writes all meshes as sub-assets of one container (replacing the old ones), then the prefab.</summary>
        static void Save(GameObject root, List<Mesh> meshes, string meshAssetPath, string prefabPath)
        {
            if (AssetDatabase.LoadMainAssetAtPath(meshAssetPath) != null) AssetDatabase.DeleteAsset(meshAssetPath);
            var container = ScriptableObject.CreateInstance<MeshContainer>();
            container.name = System.IO.Path.GetFileNameWithoutExtension(meshAssetPath);
            AssetDatabase.CreateAsset(container, meshAssetPath);
            var used = new HashSet<string>();
            foreach (var m in meshes)
            {
                string n = m.name;
                for (int i = 1; !used.Add(m.name); i++) m.name = $"{n}_{i}";
                AssetDatabase.AddObjectToAsset(m, container);
            }
            AssetDatabase.SaveAssets();
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            Object.DestroyImmediate(root);
        }

        static Material Mat(string name, Color color, float metallic, float smoothness)
        {
            string path = $"Assets/_Project/Art/Materials/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(m, path);
            }
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Metallic", metallic);
            m.SetFloat("_Smoothness", smoothness);
            EditorUtility.SetDirty(m);
            return m;
        }
    }
}
