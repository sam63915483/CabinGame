using System.Collections.Generic;
using System.Linq;
using Cabin.Editor.Map;
using Cabin.Forestry;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Cabin.Tests
{
    public class ForestryTests
    {
        readonly List<Object> made = new List<Object>();

        [TearDown]
        public void CleanUp()
        {
            foreach (var o in made) if (o != null) Object.DestroyImmediate(o);
            made.Clear();
            foreach (var go in Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
                if (go != null && (go.name.StartsWith("Felled_") || go.name.StartsWith("Chopped_"))) Object.DestroyImmediate(go);
        }

        /// <summary>An upright cylinder (no caps) round <paramref name="centre"/>, 32 sides, rows every 0.25 m.</summary>
        static Mesh Cylinder(float radius, float height, Vector2 centre)
        {
            const int sides = 32;
            int rows = Mathf.RoundToInt(height / 0.25f) + 1;
            var v = new List<Vector3>();
            var uv = new List<Vector2>();
            var t = new List<int>();
            for (int r = 0; r < rows; r++)
            for (int i = 0; i <= sides; i++)
            {
                float a = i * Mathf.PI * 2f / sides;
                v.Add(new Vector3(centre.x + Mathf.Cos(a) * radius, r * 0.25f, centre.y + Mathf.Sin(a) * radius));
                uv.Add(new Vector2(0.25f, 0.75f));
            }
            for (int r = 0; r + 1 < rows; r++)
            for (int i = 0; i < sides; i++)
            {
                int a = r * (sides + 1) + i, b = a + 1, c = a + sides + 1, d = c + 1;
                t.AddRange(new[] { a, c, b, b, c, d });
            }
            var mesh = new Mesh();
            mesh.SetVertices(v);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(t, 0);
            mesh.RecalculateNormals();
            return mesh;
        }

        [Test]
        public void MeshSlicer_KeepsOnlyTheSideAsked()
        {
            var mesh = Cylinder(0.3f, 4f, Vector2.zero);
            var above = MeshSlicer.Keep(mesh, 1.1f, true, "Above");
            var below = MeshSlicer.Keep(mesh, 1.1f, false, "Below");
            made.AddRange(new Object[] { mesh, above, below });
            var aboveUsed = above.triangles.Select(i => above.vertices[i]);
            var belowUsed = below.triangles.Select(i => below.vertices[i]);
            Assert.That(aboveUsed.All(p => p.y >= 1.1f - 1e-4f), "Above keeps nothing below the plane");
            Assert.That(belowUsed.All(p => p.y <= 1.1f + 1e-4f), "Below keeps nothing above it");
            Assert.That(aboveUsed.Any(p => Mathf.Abs(p.y - 1.1f) < 1e-4f), "The cut edge lies on the plane");
        }

        [Test]
        public void TrunkSection_FindsTheTrunksOutline_AndLeavesBranchesOut()
        {
            var mesh = Cylinder(0.3f, 4f, new Vector2(0.2f, -0.1f));
            made.Add(mesh);
            var outline = new List<MeshSlicer.EdgePoint>();
            MeshSlicer.Outline(mesh, 0.6f, outline);
            Assert.AreEqual(65, outline.Count, "Each side's upright edge and its quad's diagonal, plus the seam: every edge once");
            // A branch crossing the same plane, well out from the trunk.
            for (int i = 0; i < 4; i++) outline.Add(new MeshSlicer.EdgePoint { Position = new Vector3(1.6f + i * 0.05f, 0.6f, 0f), Submesh = 0 });
            var section = TrunkSection.From(outline, 0.6f, 1f);
            Assert.AreEqual(0.2f, section.Centre.x, 0.02f);
            Assert.AreEqual(-0.1f, section.Centre.y, 0.02f);
            foreach (float r in section.Radius) Assert.AreEqual(0.3f, r, 0.02f, "Every sector sits on the bark");
        }

        /// <summary>A trunk (cylinder r 0.3) with a low branch (a flat plank from 0.6 to 1.4 m out) through the chopping band.</summary>
        static Mesh TrunkWithBranch()
        {
            var trunk = Cylinder(0.3f, 4f, Vector2.zero);
            var branch = new Mesh();
            branch.SetVertices(new List<Vector3> { new Vector3(0.6f, 0.45f, 0f), new Vector3(1.4f, 0.45f, 0f), new Vector3(0.6f, 0.75f, 0f), new Vector3(1.4f, 0.75f, 0f) });
            branch.SetUVs(0, new List<Vector2> { Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero });
            branch.SetTriangles(new[] { 0, 2, 1, 1, 2, 3 }, 0);
            branch.RecalculateNormals();
            var mesh = new Mesh();
            mesh.CombineMeshes(new[] { new CombineInstance { mesh = trunk, transform = Matrix4x4.identity }, new CombineInstance { mesh = branch, transform = Matrix4x4.identity } }, true);
            Object.DestroyImmediate(trunk);
            Object.DestroyImmediate(branch);
            return mesh;
        }

        static NotchedMesh Notched(Mesh mesh, out TrunkSection lo, out TrunkSection hi)
        {
            var below = new List<MeshSlicer.EdgePoint>();
            var above = new List<MeshSlicer.EdgePoint>();
            MeshSlicer.Outline(mesh, 0.4f, below);
            MeshSlicer.Outline(mesh, 0.8f, above);
            lo = TrunkSection.From(below, 0.4f, 0.3f);
            hi = TrunkSection.From(above, 0.8f, 0.3f);
            return new NotchedMesh(mesh, lo, hi);
        }

        [Test]
        public void NotchedTree_LooksExactlyTheSameUntilChopped()
        {
            var source = TrunkWithBranch();
            var notched = Notched(source, out _, out _);
            var mesh = notched.Build(null, new float[ChopBand.Sectors], 0.6f, 0.2f);
            made.AddRange(new Object[] { source, mesh });
            Assert.Greater(notched.TrunkVertices, 28 * 6, "The band got rows and sectors of vertices to move");
            Assert.AreEqual(0, mesh.GetTriangles(mesh.subMeshCount - 1).Length, "No fresh wood before a chop");
            foreach (var p in mesh.vertices)
            {
                float r = new Vector2(p.x, p.z).magnitude;
                Assert.That(p.z == 0f && p.x >= 0.6f - 1e-4f || r <= 0.3f + 1e-4f, $"Every vertex on the trunk or the branch: {p}");
            }
            Assert.AreEqual(source.bounds.size.x, mesh.bounds.size.x, 1e-4f);
            Assert.AreEqual(source.bounds.size.y, mesh.bounds.size.y, 1e-4f);
        }

        [Test]
        public void NotchedTree_CutsTheTrunk_NotTheBranch()
        {
            var source = TrunkWithBranch();
            var notched = Notched(source, out _, out _);
            var depth = new float[ChopBand.Sectors];
            depth[0] = depth[1] = depth[ChopBand.Sectors - 1] = 0.5f;
            var mesh = notched.Build(null, depth, 0.6f, 0.2f);
            made.AddRange(new Object[] { source, mesh });
            Assert.Greater(mesh.GetTriangles(mesh.subMeshCount - 1).Length, 0, "The notch shows fresh wood");
            var v = mesh.vertices;
            float deepest = v.Where(p => Mathf.Abs(p.y - 0.6f) < 1e-4f && Mathf.Abs(p.z) < 1e-3f && p.x > 0f && p.x < 0.5f).Min(p => p.x);
            Assert.AreEqual(0.15f, deepest, 0.01f, "Half the radius in on the chopped side, at the chopping line");
            float back = v.Where(p => Mathf.Abs(p.y - 0.6f) < 1e-4f && p.x < -0.25f).Min(p => p.x);
            Assert.AreEqual(-0.3f, back, 0.01f, "The back of the trunk untouched");
            foreach (var corner in new[] { new Vector3(0.6f, 0.45f, 0f), new Vector3(1.4f, 0.45f, 0f), new Vector3(0.6f, 0.75f, 0f), new Vector3(1.4f, 0.75f, 0f) })
                Assert.That(v.Any(p => (p - corner).sqrMagnitude < 1e-8f), $"The branch is untouched at {corner}");
            Assert.Greater(Vector3.Dot(ChopBand.Weakest(depth), Vector3.right), 0.9f, "The cut side is +x");

            // Cut right through one side: tiny squeezed faces mustn't end up with zero normals (they light as a white blaze).
            for (int i = 0; i < depth.Length; i++) depth[i] = i < 10 ? 1f : 0f;
            notched.Build(mesh, depth, 0.6f, 0.2f);
            Assert.That(mesh.normals.All(n => Mathf.Abs(n.magnitude - 1f) < 0.01f), "Every normal is unit length");
        }

        /// <summary>A 10 m cylinder of a tree, far from anything (each at its own <paramref name="x"/>), cut line 0.7 m up.</summary>
        ChoppingTree StandingTree(float radius, float x = 0f)
        {
            var mesh = Cylinder(radius, 10f, Vector2.zero);
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            var prefab = new GameObject("TestTree");
            var data = prefab.AddComponent<ChoppableTree>();
            data.Set(mesh, new[] { mat }, Enumerable.Repeat(radius, 31).ToArray(), Color.gray, 10f);
            made.AddRange(new Object[] { mesh, mat, prefab });
            return ChoppingTree.Create(null, data, new Vector3(x, -500f, 0f), Quaternion.identity, Vector3.one, -499.3f, mat);
        }

        [Test]
        public void ChoppingTree_ComesDownAfterAHandfulOfChops_ThickerTakesMore()
        {
            int Chops(float radius, float x)
            {
                var tree = StandingTree(radius, x);
                made.Add(tree.gameObject);
                Vector3 from = tree.transform.position + new Vector3(-1.2f, 0.7f, 0f);
                for (int n = 1; n <= 40; n++)
                {
                    if (!tree.Chop(from, 1f, out _, out var normal)) continue;
                    Assert.Greater(Vector3.Dot(normal, Vector3.left), 0.9f, "Chips fly back towards the axe");
                    return n;
                }
                return 99;
            }
            int thin = Chops(0.12f, 0f), thick = Chops(0.3f, 50f);
            Assert.That(thin, Is.InRange(2, 6), "A thin trunk");
            Assert.That(thick, Is.InRange(6, 14), "A thick trunk");
            Assert.Greater(thick, thin);
        }

        [Test]
        public void FelledTree_FallsAwayFromTheAxe_OnAHingeAtTheBack()
        {
            var tree = StandingTree(0.2f);
            made.Add(tree.gameObject);
            Vector3 from = tree.transform.position + new Vector3(-1.2f, 0.7f, 0f);
            for (int n = 0; n < 40 && !tree.Fallen; n++) tree.Chop(from, 1f, out _, out _);
            Assert.IsTrue(tree.Fallen);
            var felled = GameObject.Find("Felled_TestTree");
            Assert.IsNotNull(felled, "The top comes free of the stump");
            made.Add(felled);
            Assert.IsNotNull(felled.GetComponent<Rigidbody>());
            var hinge = felled.GetComponent<HingeJoint>();
            Assert.IsNotNull(hinge);
            Assert.Greater(hinge.anchor.x, 0.1f, "Hinged on the far side from the chopper (who's at −x)");
            Assert.IsNotNull(tree.transform.Find("Stump"), "The stump stays");
        }

        [Test]
        public void TreePrefabs_AreChoppable_WithReadableMeshesAndRealTrunks()
        {
            foreach (var species in TreeLibrary.All)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(TreeLibrary.PrefabPath(species));
                Assert.IsNotNull(prefab, species.Source);
                var c = prefab.GetComponent<ChoppableTree>();
                Assert.IsNotNull(c, $"{prefab.name} can be chopped");
                Assert.IsNotNull(c.Mesh, prefab.name);
                Assert.IsTrue(c.Mesh.isReadable, $"{prefab.name}: the axe cuts its mesh at run time");
                Assert.Greater(c.Materials.Length, 0, prefab.name);
                Assert.That(c.RadiusAt(0.7f), Is.InRange(0.04f, 0.8f), $"{prefab.name} trunk radius");
                Assert.Greater(c.Height, 2f, prefab.name);
            }
        }

        [Test]
        public void MapProjection_RoundTrips()
        {
            var recipe = ScriptableObject.CreateInstance<Cabin.Map.MapRecipe>();
            made.Add(recipe);
            var proj = new Cabin.Map.MapProjection(recipe);
            foreach (var p in new[] { new Vector2(0f, 0f), new Vector2(-1500f, 800f), new Vector2(1900f, -1700f) })
            {
                proj.ToGeo(p.x, p.y, out double lon, out double lat);
                var back = proj.FromGeo(lon, lat);
                Assert.AreEqual(p.x, back.x, 0.01f);
                Assert.AreEqual(p.y, back.y, 0.01f);
            }
        }

        [Test]
        public void MinHeap_PopsLowestFirst()
        {
            var heap = new MinHeap(16);
            var keys = new[] { 5f, -1f, 3f, 9f, 0f, 3.5f, -7f, 2f };
            for (int i = 0; i < keys.Length; i++) heap.Push(keys[i], i);
            var order = new List<float>();
            while (heap.Count > 0) order.Add(keys[heap.Pop()]);
            CollectionAssert.AreEqual(keys.OrderBy(k => k).ToList(), order);
        }
    }
}
