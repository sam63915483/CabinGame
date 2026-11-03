using System.Collections;
using System.Linq;
using Cabin.Build;
using Cabin.Player;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Cabin.Tests
{
    /// <summary>Deck blocks sit level and sunk on sloped ground; a post seats in one, plumb and planted; nothing knocks it over.</summary>
    public class DeckBlockPlacementTests
    {
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
            if (Physics.Raycast(new Ray(cam.position, cam.forward), out var hit, 6f, BuildLayers.AimMask, QueryTriggerInteraction.Ignore))
            {
                aim.Hit = true;
                aim.RaycastHit = hit;
                aim.Piece = hit.collider.GetComponent<Piece>();
            }
            return aim;
        }

        /// <summary>A spot on the slope outside the clearing where the ground falls away across a block's footprint.</summary>
        static Vector3 FindSlope()
        {
            float half = Stock.DeckBlockBase * 0.5f;
            for (float r = 26f; r < 60f; r += 2f)
                for (float a = 0f; a < 360f; a += 30f)
                {
                    var p = new Vector3(Mathf.Cos(a * Mathf.Deg2Rad) * r, 0f, Mathf.Sin(a * Mathf.Deg2Rad) * r);
                    float spread = Mathf.Abs(GroundY(p + Vector3.right * half) - GroundY(p - Vector3.right * half));
                    if (spread > 0.05f && spread < 0.4f) return p;
                }
            return new Vector3(30f, 0f, 0f);
        }

        [UnityTest]
        public IEnumerator Block_OnASlope_IsLevel_AndNoCornerFloats_ThenAPostSeatsInIt()
        {
            Vector3 spot = FindSlope();
            float g = GroundY(spot);
            cam.position = spot + new Vector3(-2f, 1.7f, 0f);
            cam.rotation = Quaternion.LookRotation((spot + Vector3.up * g - cam.position).normalized);
            cam.position = new Vector3(cam.position.x, g + 1.7f, cam.position.z);
            cam.rotation = Quaternion.LookRotation((new Vector3(spot.x, g, spot.z) - cam.position).normalized);

            var block = site.TakeStock(StockKind.DeckBlock, 0f, carry.HoldPoint, carry.DefaultRotation(StockKind.DeckBlock));
            carry.PickUp(block, keepRotation: true);
            for (int i = 0; i < 5; i++) { carry.Tick(Aim()); yield return null; }
            Assert.AreEqual(CarryController.Placement.Block, carry.Current, "Aimed at the ground: the block previews as placed");
            carry.Place();
            Assert.IsTrue(block.Planted);
            Assert.IsTrue(block.Structure.Body.isKinematic);

            // Level: no pitch or roll.
            Vector3 up = block.transform.up;
            Assert.Greater(Vector3.Dot(up, Vector3.up), 0.9999f, "Dead level");

            // No corner floats: every bottom corner is at or below the ground beneath it.
            float half = Stock.DeckBlockBase * 0.5f;
            float bottomY = block.LowestWorldY();
            foreach (var c in new[] { new Vector3(-half, 0, -half), new Vector3(half, 0, -half), new Vector3(half, 0, half), new Vector3(-half, 0, half) })
            {
                Vector3 corner = block.transform.TransformPoint(c);
                Assert.LessOrEqual(bottomY, GroundY(corner) + 0.001f, $"Corner {c} is in the ground");
            }
            Assert.Greater(bottomY, g - 0.6f, "…but not buried");

            // Now a 4×4 held flat (how it comes off the rack) aimed at the block: it seats in the socket, plumb, planted.
            cam.rotation = Quaternion.LookRotation((block.SocketTop - cam.position).normalized);
            var post = site.TakeStock(StockKind.FourByFour, 8f * Units.Foot, carry.HoldPoint, carry.DefaultRotation(StockKind.FourByFour));
            carry.PickUp(post, keepRotation: true);
            Assert.Less(Mathf.Abs(Vector3.Dot(post.GrainAxisWorld, Vector3.up)), 0.1f, "Off the rack it lies flat like any board");
            for (int i = 0; i < 5; i++) { carry.Tick(Aim()); yield return null; }
            Assert.AreEqual(CarryController.Placement.PostInBlock, carry.Current, "A 4×4 aimed at an empty block previews seated whatever way it's held");
            carry.Place();
            Assert.IsTrue(post.Planted);
            Assert.AreEqual(block.Id, post.SeatedIn);
            Assert.AreEqual(post, site.PostIn(block));
            Assert.Greater(Vector3.Dot(post.GrainAxisWorld, Vector3.up), 0.9999f, "Plumb");
            Assert.AreEqual(block.SocketTop.y - Stock.DeckBlockSocketDepth, post.LowestWorldY(), 0.002f, "Seated to the socket depth");
            Assert.AreEqual(block.SocketTop.x, post.transform.position.x, 0.002f, "Centred on the socket");
            Assert.AreEqual(block.SocketTop.z, post.transform.position.z, 0.002f);

            // The block can't be lifted while the post is in it; the post can be lifted (and unseats).
            Assert.IsTrue(post.Structure.Body.isKinematic);
            var loose = site.TakeStock(StockKind.TwoByFour, 8f * Units.Foot, post.transform.position + Vector3.up * 2f + Vector3.right * 0.3f, Quaternion.identity);
            yield return new WaitForSeconds(1.0f);
            Assert.Greater(Vector3.Dot(post.GrainAxisWorld, Vector3.up), 0.9999f, "A board dropped on it doesn't knock it over");
            Assert.IsTrue(post.Structure.Body.isKinematic);
            _ = loose;
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator A4x4_AimedAtBareGround_NeverSnaps_AndCantBeClampedToNothing()
        {
            float g = GroundY(new Vector3(3f, 0f, -3f));
            cam.position = new Vector3(1f, g + 1.7f, -3f);
            cam.rotation = Quaternion.LookRotation((new Vector3(3f, g, -3f) - cam.position).normalized);

            // Held upright and aimed at the ground: no snap, no planting — free-form only.
            var post = site.TakeStock(StockKind.FourByFour, 8f * Units.Foot, carry.HoldPoint, Quaternion.Euler(0f, 0f, 90f));
            carry.PickUp(post, keepRotation: true);
            for (int i = 0; i < 3; i++) { carry.Tick(Aim()); yield return null; }
            Assert.AreEqual(CarryController.Placement.Air, carry.Current, "Bare ground is not a snap target");
            Assert.IsFalse(post.Structure.Body.isKinematic, "Still a free physics piece in the hands");
            string reason = carry.Place();
            StringAssert.Contains("Press it against", reason);
            Assert.IsTrue(carry.Holding, "Nothing to clamp to: it stays in your hands");
            Assert.IsFalse(post.Clamped);
            Assert.IsFalse(post.Planted);
            carry.Drop();
            Assert.AreEqual(0, site.Pieces.Values.Count(p => p.Planted));
        }
    }
}
