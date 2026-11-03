using System.Collections;
using System.Linq;
using Cabin.Forestry;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Cabin.Tests
{
    /// <summary>A chopped-through tree goes over away from whoever chopped it and ends up lying on the ground beside its stump.</summary>
    public class TreeFallTests
    {
        GameObject ground, prefab;
        Mesh mesh;
        Material material;

        [TearDown]
        public void CleanUp()
        {
            foreach (var go in Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
                if (go != null && (go.name.StartsWith("Felled_") || go.name.StartsWith("Chopped_"))) Object.Destroy(go);
            Object.Destroy(ground);
            Object.Destroy(prefab);
            Object.Destroy(mesh);
            Object.Destroy(material);
        }

        static Mesh Cylinder(float radius, float height)
        {
            const int sides = 24;
            int rows = Mathf.RoundToInt(height / 0.25f) + 1;
            var v = new System.Collections.Generic.List<Vector3>();
            var t = new System.Collections.Generic.List<int>();
            for (int r = 0; r < rows; r++)
            for (int i = 0; i <= sides; i++)
            {
                float a = i * Mathf.PI * 2f / sides;
                v.Add(new Vector3(Mathf.Cos(a) * radius, r * 0.25f, Mathf.Sin(a) * radius));
            }
            for (int r = 0; r + 1 < rows; r++)
            for (int i = 0; i < sides; i++)
            {
                int a = r * (sides + 1) + i, b = a + 1, c = a + sides + 1, d = c + 1;
                t.AddRange(new[] { a, c, b, b, c, d });
            }
            var m = new Mesh();
            m.SetVertices(v);
            m.SetTriangles(t, 0);
            m.RecalculateNormals();
            return m;
        }

        [UnityTest]
        public IEnumerator ChoppedTree_FallsAwayFromTheAxe_AndLiesBesideTheStump()
        {
            Vector3 at = new Vector3(0f, -800f, 0f);
            ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.transform.position = at + Vector3.down * 0.5f;
            ground.transform.localScale = new Vector3(60f, 1f, 60f);

            mesh = Cylinder(0.2f, 9f);
            material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            prefab = new GameObject("FallTree");
            var data = prefab.AddComponent<ChoppableTree>();
            data.Set(mesh, new[] { material }, Enumerable.Repeat(0.2f, 31).ToArray(), Color.gray, 9f);

            var tree = ChoppingTree.Create(null, data, at, Quaternion.identity, Vector3.one, at.y + 0.7f, material);
            Vector3 axe = at + new Vector3(-1.2f, 1.6f, 0.3f); // the chopper stands at −x
            for (int n = 0; n < 40 && !tree.Fallen; n++) tree.Chop(axe, 1f, out _, out _);
            Assert.IsTrue(tree.Fallen);

            var felled = GameObject.Find("Felled_FallTree");
            Assert.IsNotNull(felled);
            yield return new WaitForSeconds(5f);

            Vector3 up = felled.transform.up;
            Assert.Less(Mathf.Abs(up.y), 0.35f, "It ends up lying down");
            Assert.Greater(up.x, 0.8f, "Its top lies away from the chopper");
            Assert.Less(Vector3.Distance(felled.transform.position, at), 3f, "Its butt stays by the stump (not thrown)");
            Assert.Greater(felled.transform.position.y, at.y - 0.5f, "On the ground, not through it");
        }
    }
}
