using System.Collections;
using System.Linq;
using Cabin.Build;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Cabin.Tests
{
    /// <summary>The whole flow in the real scene: plant a post, clamp a board to it, nail, unclamp, cut, save, load.</summary>
    public class FreeBuildTests
    {
        const float In = Units.Inch;
        static readonly float HalfPost = Stock.Size(StockKind.FourByFour, 1f).z * 0.5f;
        static readonly float HalfBoard = Stock.Size(StockKind.TwoByFour, 1f).z * 0.5f;

        BuildSite site;

        [UnitySetUp]
        public IEnumerator Load()
        {
            yield return SceneManager.LoadSceneAsync("TestPlatform", LoadSceneMode.Single);
            yield return null;
            site = Object.FindFirstObjectByType<BuildSite>();
            Assert.IsNotNull(site);
            site.ClearAll();
            yield return null;
        }

        static float GroundY(Vector3 at) =>
            Physics.Raycast(at + Vector3.up * 50f, Vector3.down, out var hit, 100f, BuildLayers.GroundMask) ? hit.point.y : 0f;

        [UnityTest]
        public IEnumerator PlantedPost_HoldsANailedBoard_AndACutFreesTheTop()
        {
            // A 4×4 standing in the ground at the clearing's centre, 8 ft long, 18 in buried.
            float ground = GroundY(new Vector3(2f, 0f, 2f));
            var post = site.TakeStock(StockKind.FourByFour, 8f * Units.Foot, new Vector3(2f, ground - 18f * In + 4f * Units.Foot, 2f), Quaternion.Euler(0f, 0f, 90f));
            site.Plant(post);
            Assert.IsTrue(post.Structure.Body.isKinematic);

            // A 2×4 with its wide face against the post's +x face, 1 m above ground, clamped there, then nailed.
            float boardY = ground + 1f;
            var board = site.TakeStock(StockKind.TwoByFour, 8f * Units.Foot, new Vector3(2f + HalfPost + HalfBoard, boardY, 2f), Quaternion.Euler(90f, 0f, 90f));
            Physics.SyncTransforms();
            Assert.IsNull(site.TryClamp(board));
            Assert.AreEqual(1, site.ClampCount, "One clamp: it touches one post");
            Assert.AreEqual(post, site.ClampContacts[0].Other);
            Assert.Greater(Vector3.Dot(site.ClampContacts[0].Normal, Vector3.right), 0.99f, "Clamped across the face they meet on, not along an edge");
            Assert.AreEqual(2f, site.ClampContacts[0].Point.z, 0.01f, "Clamp in the middle of the contact patch");
            Assert.IsTrue(board.Structure.Body.isKinematic, "Clamped = held still");
            yield return new WaitForFixedUpdate();
            Physics.SyncTransforms();

            var ray = new Ray(new Vector3(2.5f, boardY, 2f), Vector3.left);
            var plan = site.PlanNail(ray, 2f);
            Assert.IsNull(plan.Reason, plan.Reason);
            Assert.AreEqual(board, plan.First);
            Assert.AreEqual(post, plan.Second);
            Assert.IsNotNull(site.DriveNail(plan));
            Assert.AreEqual(1, site.StructureCount, "Nailed pieces share one structure");

            site.Unclamp();
            Assert.AreEqual(0, site.ClampCount);
            Vector3 before = board.transform.position;
            yield return new WaitForSeconds(0.6f);
            Assert.Less(Vector3.Distance(before, board.transform.position), 0.001f, "The post holds the board up");
            Assert.IsTrue(board.Structure.Body.isKinematic);

            // Saw the post 0.5 m above the ground: the top of the post and the board come down together.
            string reason = site.Cut(post, new Plane(Vector3.up, new Vector3(0f, ground + 0.5f, 0f)), out var offcut);
            Assert.IsNull(reason, reason);
            var bottom = post.Planted ? post : offcut;
            var top = post.Planted ? offcut : post;
            Assert.IsTrue(bottom.Planted);
            Assert.IsFalse(top.Planted);
            Assert.AreEqual(2, site.StructureCount);
            Assert.AreEqual(top.Structure, board.Structure, "The nail went with the top half");
            Assert.IsFalse(top.Structure.Body.isKinematic);
            yield return new WaitForSeconds(1.0f);
            // A square-cut post top just settles onto the stump: it drops by the saw kerf, no more.
            Assert.Less(board.transform.position.y, before.y - 0.002f, "The top of the post settled with the board on it");
            Assert.Greater(board.transform.position.y, before.y - 0.05f, "…but it is resting on the stump, not falling through it");
            Assert.IsTrue(bottom.Structure.Body.isKinematic);

            // Save, wipe, load: same pieces, same nail, same anchoring.
            var json = JsonUtility.ToJson(site.ToSaveData());
            site.ClearAll();
            yield return null;
            Assert.AreEqual(0, site.Pieces.Count);
            site.Load(JsonUtility.FromJson<BuildSaveData>(json));
            yield return null;
            Assert.AreEqual(3, site.Pieces.Count);
            Assert.AreEqual(1, site.Nails.Count);
            Assert.AreEqual(1, site.Pieces.Values.Count(p => p.Planted));
            Assert.AreEqual(2, site.StructureCount);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator NothingTouching_CannotBeClamped()
        {
            float ground = GroundY(Vector3.zero);
            var a = site.TakeStock(StockKind.TwoByFour, 8f * Units.Foot, new Vector3(0f, ground + 1.5f, 0f), Quaternion.identity);
            Physics.SyncTransforms();
            Assert.IsNotNull(site.TryClamp(a));
            Assert.IsNull(site.ClampedPiece);
            Assert.IsFalse(a.Structure.Body.isKinematic);
            yield return null;
        }

        [UnityTest]
        public IEnumerator ClampingASecondPiece_ReleasesTheFirst()
        {
            float ground = GroundY(new Vector3(0f, 0f, 4f));
            var post = site.TakeStock(StockKind.FourByFour, 8f * Units.Foot, new Vector3(0f, ground - 18f * In + 4f * Units.Foot, 4f), Quaternion.Euler(0f, 0f, 90f));
            site.Plant(post);
            float y = ground + 1.5f;
            var a = site.TakeStock(StockKind.TwoByFour, 8f * Units.Foot, new Vector3(HalfPost + HalfBoard, y, 4f), Quaternion.Euler(90f, 0f, 90f));
            var b = site.TakeStock(StockKind.TwoByFour, 8f * Units.Foot, new Vector3(-HalfPost - HalfBoard, y, 4f), Quaternion.Euler(90f, 0f, 90f));
            Physics.SyncTransforms();
            Assert.IsNull(site.TryClamp(a));
            Assert.IsNull(site.TryClamp(b));
            Assert.AreEqual(b, site.ClampedPiece);
            Assert.IsFalse(a.Clamped);
            Assert.IsFalse(a.Structure.Body.isKinematic);
            Assert.IsTrue(b.Structure.Body.isKinematic);
            yield return new WaitForSeconds(1f);
            Assert.Less(a.transform.position.y, y - 0.2f, "The first board fell");
            Assert.AreEqual(y, b.transform.position.y, 0.001f, "The second stays clamped");
        }

        [UnityTest]
        public IEnumerator PullingTheOnlyNail_DropsTheBoard()
        {
            float ground = GroundY(new Vector3(-3f, 0f, 0f));
            var post = site.TakeStock(StockKind.FourByFour, 8f * Units.Foot, new Vector3(-3f, ground - 18f * In + 4f * Units.Foot, 0f), Quaternion.Euler(0f, 0f, 90f));
            site.Plant(post);
            float boardY = ground + 1f;
            var board = site.TakeStock(StockKind.TwoByFour, 8f * Units.Foot, new Vector3(-3f + HalfPost + HalfBoard, boardY, 0f), Quaternion.Euler(90f, 0f, 90f));
            Physics.SyncTransforms();
            Assert.IsNull(site.TryClamp(board));
            yield return new WaitForFixedUpdate();
            Physics.SyncTransforms();
            var nail = site.DriveNail(site.PlanNail(new Ray(new Vector3(-2.5f, boardY, 0f), Vector3.left), 2f));
            Assert.IsNotNull(nail);
            site.Unclamp();
            yield return new WaitForSeconds(0.3f);
            Assert.IsTrue(site.RemoveNail(nail.id));
            Assert.AreEqual(2, site.StructureCount);
            yield return new WaitForSeconds(1f);
            Assert.Less(board.transform.position.y, boardY - 0.05f);
        }
    }
}
