using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Cabin.Editor.Map
{
    /// <summary>
    /// The rocks the map scatters — FantasyValley's boulders, which share the trees' style and have LODs — as
    /// terrain-tree prefabs: a variant of each pack prefab with a capsule on its root (terrain trees only collide
    /// through a collider on the root). Generated into Prefabs/Rocks; the pack prefabs aren't touched. Three sizes:
    /// big (4–6 m outcrops), mid (1–2 m boulders), small (stones).
    /// </summary>
    public static class RockLibrary
    {
        const string FV = "Assets/External/FantasyValley/Prefabs/Vegetation/";
        public const string Dir = "Assets/_Project/Prefabs/Rocks";
        public const int PerSize = 5;
        /// <summary>Index ranges into <see cref="Build"/>'s result.</summary>
        public const int BigFrom = 0, MidFrom = PerSize, SmallFrom = PerSize * 2;

        static readonly string[] Sources =
        {
            "BigRock_01", "BigRock_02", "BigRock_03", "BigRock_04", "BigRock_05",
            "Rock_01", "Rock_02", "Rock_03", "Rock_04", "Rock_05",
            "SmallRock_01", "SmallRock_02", "SmallRock_03", "SmallRock_04", "SmallRock_05",
        };

        /// <summary>Builds (or rebuilds) the rock prefabs, big first, then mid, then small.</summary>
        public static GameObject[] Build()
        {
            SandboxAssetGenerator.EnsureFolder(Dir);
            var result = new List<GameObject>();
            foreach (var name in Sources)
            {
                var source = AssetDatabase.LoadAssetAtPath<GameObject>(FV + name + ".prefab");
                var go = (GameObject)PrefabUtility.InstantiatePrefab(source);
                try
                {
                    var b = new Bounds(go.transform.position, Vector3.zero);
                    foreach (var r in go.GetComponentsInChildren<Renderer>()) b.Encapsulate(r.bounds);
                    var capsule = go.AddComponent<CapsuleCollider>();
                    capsule.radius = Mathf.Max(0.15f, Mathf.Min(b.size.x, b.size.z) * 0.42f);
                    capsule.height = Mathf.Max(b.size.y, capsule.radius * 2f);
                    capsule.center = new Vector3(b.center.x - go.transform.position.x, b.center.y - go.transform.position.y, b.center.z - go.transform.position.z);
                    result.Add(PrefabUtility.SaveAsPrefabAsset(go, $"{Dir}/FV_{name}.prefab"));
                }
                finally
                {
                    Object.DestroyImmediate(go);
                }
            }
            return result.ToArray();
        }
    }
}
