using System.Collections;
using Cabin.Build;
using Cabin.Player;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Cabin.Tests
{
    /// <summary>The held piece is a physics object: it presses against posts instead of passing through, and drops onto the ground.</summary>
    public class CarryTests
    {
        const float In = Units.Inch;
        BuildSite site;
        CarryController carry;
        Transform cam;

        [UnitySetUp]
        public IEnumerator Load()
        {
            yield return SceneManager.LoadSceneAsync("TestPlatform", LoadSceneMode.Single);
            yield return null;
            site = Object.FindFirstObjectByType<BuildSite>();
            carry = Object.FindFirstObjectByType<CarryController>();
            Object.FindFirstObjectByType<FirstPersonController>().enabled = false;
            cam = Camera.main.transform;
            site.ClearAll();
            yield return null;
        }

        static float GroundY(Vector3 at) =>
            Physics.Raycast(at + Vector3.up * 50f, Vector3.down, out var hit, 100f, BuildLayers.GroundMask) ? hit.point.y : 0f;

        AimInfo Aim()
        {
            var aim = new AimInfo();
            if (Physics.Raycast(new Ray(cam.position, cam.forward), out var hit, 4f, BuildLayers.AimMask, QueryTriggerInteraction.Ignore))
            {
                aim.Hit = true;
                aim.RaycastHit = hit;
                aim.Piece = hit.collider.GetComponent<Piece>();
            }
            return aim;
        }

        [UnityTest]
        public IEnumerator HeldBoard_StopsAgainstAPost_InsteadOfPassingThrough()
        {
            float ground = GroundY(new Vector3(4f, 0f, 4f));
            var post = site.TakeStock(StockKind.FourByFour, 8f * Units.Foot, new Vector3(4f, ground - 18f * In + 4f * Units.Foot, 4f), Quaternion.Euler(0f, 0f, 90f));
            site.Plant(post);

            // Stand 2.5 m from the post looking straight at it, holding a board across the view with the hands
            // 3 m out: that is inside the post, so the board must stop on its near face.
            cam.position = new Vector3(1.5f, ground + 1.2f, 4f);
            cam.rotation = Quaternion.LookRotation(Vector3.right);
            var board = site.TakeStock(StockKind.TwoByFour, 8f * Units.Foot, cam.position + Vector3.right, carry.DefaultRotation(StockKind.TwoByFour));
            carry.PickUp(board, keepRotation: true);
            carry.SetDistance(3f);
            Assert.AreEqual(BuildLayers.HeldPiece, board.gameObject.layer);

            for (int i = 0; i < 90; i++)
            {
                carry.Tick(Aim());
                yield return new WaitForFixedUpdate();
            }
            Physics.SyncTransforms();
            float halfPost = Stock.Size(StockKind.FourByFour, 1f).z * 0.5f;
            float halfBoard = Stock.Size(StockKind.TwoByFour, 1f).z * 0.5f;
            float nearFace = 4f - halfPost;
            float boardFarFace = board.transform.position.x + halfBoard;
            Assert.LessOrEqual(boardFarFace, nearFace + 0.01f, "Board stayed on the near side of the post");
            Assert.Greater(boardFarFace, nearFace - 0.08f, "…pressed up against it");
            bool overlapping = Physics.ComputePenetration(board.Collider, board.transform.position, board.transform.rotation,
                post.Collider, post.transform.position, post.transform.rotation, out _, out float depth) && depth > 0.01f;
            Assert.IsFalse(overlapping, $"No interpenetration (depth {depth:F3})");

            // LMB: it clamps to the post it's pressed against, exactly where physics left it.
            Assert.AreEqual(1, carry.Touching, "The HUD knows it's touching one piece");
            StringAssert.StartsWith("Clamped", carry.Place());
            Assert.IsTrue(board.Clamped);
            Assert.AreEqual(post, site.ClampContacts[0].Other);
            Assert.AreEqual(1, site.ClampCount);
            Assert.IsTrue(board.Structure.Body.isKinematic);
            Assert.AreEqual(BuildLayers.Piece, board.gameObject.layer);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator DroppedBoard_RestsOnTheGround()
        {
            float ground = GroundY(new Vector3(-4f, 0f, -4f));
            cam.position = new Vector3(-4f, ground + 1.7f, -4f);
            cam.rotation = Quaternion.LookRotation(new Vector3(0f, -0.9f, 0.4f)); // looking down at your feet
            var board = site.TakeStock(StockKind.TwoByFour, 8f * Units.Foot, cam.position + cam.forward, carry.DefaultRotation(StockKind.TwoByFour));
            carry.PickUp(board, keepRotation: true);
            for (int i = 0; i < 60; i++)
            {
                carry.Tick(Aim());
                yield return new WaitForFixedUpdate();
            }
            carry.Drop();
            Assert.IsFalse(board.Structure.Body.isKinematic);
            yield return new WaitForSeconds(2.5f);
            float lowest = board.LowestWorldY();
            float groundUnder = GroundY(board.transform.position);
            Assert.GreaterOrEqual(lowest, groundUnder - 0.03f, "The board is lying on the ground, not through it");
            Assert.Less(lowest, groundUnder + 0.3f, "…and has come to rest on it");
        }
    }
}
