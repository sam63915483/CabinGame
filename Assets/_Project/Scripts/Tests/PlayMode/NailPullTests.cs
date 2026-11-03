using System.Collections;
using Cabin.Build;
using Cabin.Player;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Cabin.Tests
{
    /// <summary>
    /// Nailing across a small gap draws the free piece flush — sliding it, or swinging it round its clamp when it's
    /// clamped at the other end; fixed pieces refuse; pilot mode moves the camera and restores it.
    /// </summary>
    public class NailPullTests
    {
        const float In = Units.Inch;
        static readonly float HalfPost = Stock.Size(StockKind.FourByFour, 1f).z * 0.5f;
        static readonly float HalfBoard = Stock.Size(StockKind.TwoByFour, 1f).z * 0.5f;

        BuildSite site;
        CarryController carry;

        [UnitySetUp]
        public IEnumerator Load()
        {
            yield return SceneManager.LoadSceneAsync("TestPlatform", LoadSceneMode.Single);
            yield return null;
            site = Object.FindFirstObjectByType<BuildSite>();
            carry = Object.FindFirstObjectByType<CarryController>();
            Object.FindFirstObjectByType<FirstPersonController>().enabled = false;
            site.ClearAll();
            yield return null;
        }

        static float GroundY(Vector3 at) =>
            Physics.Raycast(at + Vector3.up * 50f, Vector3.down, out var hit, 100f, BuildLayers.GroundMask) ? hit.point.y : 0f;

        Piece PlantedPost(Vector3 at)
        {
            float g = GroundY(at);
            var post = site.TakeStock(StockKind.FourByFour, 8f * Units.Foot, new Vector3(at.x, g - 18f * In + 4f * Units.Foot, at.z), Quaternion.Euler(0f, 0f, 90f));
            site.Plant(post);
            return post;
        }

        [UnityTest]
        public IEnumerator HeldBoard_WithAGap_IsSlidFlushAndNailed()
        {
            var post = PlantedPost(new Vector3(2f, 0f, 2f));
            float y = GroundY(new Vector3(2f, 0f, 2f)) + 1f;
            float gap = 1.5f * In * Stock.CrossSectionScale;
            // On edge across the post's +x face: grain along z, wide face vertical, thickness along x.
            var board = site.TakeStock(StockKind.TwoByFour, 8f * Units.Foot, new Vector3(2f + HalfPost + HalfBoard + gap, y, 2f), Quaternion.Euler(0f, 90f, 0f));
            site.SetOnTable(board, true); // pinned without clamps, so the pull can slide it
            yield return new WaitForFixedUpdate();
            Physics.SyncTransforms();

            var ray = new Ray(new Vector3(3f, y, 2f), Vector3.left);
            var plan = site.PlanNail(ray, 2f);
            Assert.IsNull(plan.Reason, plan.Reason);
            Assert.AreEqual(gap, plan.Pull, 0.002f, "The plan knows the gap");
            Assert.AreEqual(board.Structure, plan.Mover, "The pinned board moves, the planted post doesn't");
            Assert.IsFalse(plan.Pivot, "Nothing in the way: it slides");

            var nail = site.DriveNail(plan, out string reason);
            Assert.IsNotNull(nail, reason);
            float faceGap = (board.transform.position.x - HalfBoard) - (post.transform.position.x + HalfPost);
            Assert.AreEqual(0f, faceGap, 0.003f, "Drawn flush");
            Assert.AreEqual(1, site.StructureCount);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator BoardClampedToOnePost_IsSwungInToTheOther()
        {
            // Two posts 1.6 m apart; the second sits back by a gap, so a straight board touches only the first.
            float gap = 1f * In * Stock.CrossSectionScale;
            var post1 = PlantedPost(new Vector3(2f, 0f, 1.2f));
            var post2 = PlantedPost(new Vector3(2f - gap, 0f, 2.8f));
            float y = GroundY(new Vector3(2f, 0f, 2f)) + 1f;
            var board = site.TakeStock(StockKind.TwoByFour, 8f * Units.Foot, new Vector3(2f + HalfPost + HalfBoard, y, 2f), Quaternion.Euler(0f, 90f, 0f));
            Physics.SyncTransforms();
            Assert.IsNull(site.TryClamp(board));
            Assert.AreEqual(1, site.ClampCount);
            Assert.AreEqual(post1, site.ClampContacts[0].Other, "Clamped to the post it touches");
            Assert.Greater(Vector3.Dot(site.ClampContacts[0].Normal, Vector3.right), 0.99f, "Flush face, even though the board overhangs the post's edges");
            Assert.AreEqual(1.2f, site.ClampContacts[0].Point.z, 0.01f, "Clamp centred on the post");
            yield return new WaitForFixedUpdate();
            Physics.SyncTransforms();

            var plan = site.PlanNail(new Ray(new Vector3(3f, y, 2.8f), Vector3.left), 2f);
            Assert.IsNull(plan.Reason, plan.Reason);
            Assert.AreEqual(post2, plan.Second);
            Assert.IsTrue(plan.Pivot, "Sliding is blocked by post 1, so it swings round the clamp");

            var nail = site.DriveNail(plan, out string reason);
            Assert.IsNotNull(nail, reason);
            Physics.SyncTransforms();
            Assert.IsTrue(ContactFinder.TryContact(board, post2, 0.02f, out _), "Now touching post 2");
            Assert.IsTrue(ContactFinder.TryContact(board, post1, 0.02f, out _), "…and still on post 1");
            Assert.AreEqual(board, site.ClampedPiece, "Still clamped");
            Assert.Greater(site.ClampCount, 0);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator TwoFixedPieces_WithAGap_Refuse()
        {
            var post = PlantedPost(new Vector3(-2f, 0f, 2f));
            float y = GroundY(new Vector3(-2f, 0f, 2f)) + 1f;
            float gap = 1f * In * Stock.CrossSectionScale;
            // A board that is itself anchored (planted straight into the ground, as code can) with a gap to the post.
            var other = site.TakeStock(StockKind.TwoByFour, 8f * Units.Foot, new Vector3(-2f + HalfPost + HalfBoard + gap, y, 2f), Quaternion.Euler(0f, 90f, 0f));
            site.Plant(other);
            yield return new WaitForFixedUpdate();
            Physics.SyncTransforms();
            var plan = site.PlanNail(new Ray(new Vector3(-1f, y, 2f), Vector3.left), 2f);
            Assert.IsNotNull(plan.Reason);
            StringAssert.Contains("fixed", plan.Reason);
            Assert.IsNull(site.DriveNail(plan, out _));
            _ = post;
        }

        [UnityTest]
        public IEnumerator PilotMode_MovesTheCameraToOrbitThePiece_AndRestoresIt()
        {
            var cam = Camera.main.transform;
            float g = GroundY(Vector3.zero);
            cam.position = new Vector3(0f, g + 1.7f, -3f);
            cam.rotation = Quaternion.LookRotation(Vector3.forward);
            Vector3 camLocal = cam.localPosition;
            var board = site.TakeStock(StockKind.TwoByFour, 8f * Units.Foot, new Vector3(0f, g + 1.2f, 0f), Quaternion.identity);
            carry.PickUp(board, keepRotation: true);
            carry.TogglePilot();
            Assert.IsTrue(carry.Piloting);
            yield return null;
            yield return null;
            Vector3 center = board.transform.position;
            Assert.Less(Vector3.Angle(cam.forward, center - cam.position), 2f, "Camera looks at the piece");
            Assert.Greater(Vector3.Distance(cam.position, center), 1f, "…from a distance");
            carry.TogglePilot();
            Assert.IsFalse(carry.Piloting);
            Assert.Less(Vector3.Distance(cam.localPosition, camLocal), 1e-4f, "Camera back on the body");
            carry.Drop();
        }
    }
}
