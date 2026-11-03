using System.Collections;
using Cabin.Build;
using Cabin.Player;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Cabin.Tests
{
    /// <summary>Playtest 7: guide lines between deck blocks, snapping into line, and posts square with their block.</summary>
    public class BlockAlignmentTests
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

        void LookAt(Vector3 target)
        {
            cam.position = target + new Vector3(0f, 1.6f, -1.4f);
            cam.rotation = Quaternion.LookRotation(target - cam.position);
        }

        /// <summary>Holds a deck block aimed at a ground point with a given yaw and returns it, still held.</summary>
        IEnumerator HoldBlockAt(Vector3 xz, float yaw, System.Action<Piece> got)
        {
            var target = new Vector3(xz.x, GroundY(xz), xz.z);
            LookAt(target);
            var block = carry.TakeNew(StockKind.DeckBlock, 0f);
            carry.WorldRotation = Quaternion.Euler(0f, yaw, 0f);
            for (int i = 0; i < 3; i++) { carry.Tick(Aim()); yield return null; }
            got(block);
        }

        IEnumerator SetBlockAt(Vector3 xz, float yaw, System.Action<Piece> got)
        {
            Piece b = null;
            yield return HoldBlockAt(xz, yaw, p => b = p);
            Assert.AreEqual(CarryController.Placement.Block, carry.Current);
            carry.Place();
            got(b);
        }

        [UnityTest]
        public IEnumerator BlockNearlyInLine_SnapsSquare_WithAGreenGuide()
        {
            Piece a = null;
            yield return SetBlockAt(new Vector3(0f, 0f, 4f), 20f, p => a = p);

            // 2.4 m along A's right, but 2° off and turned differently: should snap onto A's line and take A's rotation.
            Vector3 dir = Quaternion.AngleAxis(2f, Vector3.up) * a.transform.right;
            Vector3 aim = a.transform.position + dir * 2.4f;
            Piece b = null;
            yield return HoldBlockAt(aim, 47f, p => b = p);
            Assert.AreEqual(CarryController.Placement.Block, carry.Current);
            Assert.AreEqual(1, carry.Guides.Count, "One guide back to the set block");
            Assert.IsTrue(carry.Guides[0].Aligned, $"Snapped in line ({carry.Guides[0].Degrees:F2}°)");
            Assert.AreEqual(2.4f / Stock.LengthScale, carry.Guides[0].NominalDistance, 0.05f, "Distance in nominal units");
            carry.Place();

            Vector3 offset = b.transform.position - a.transform.position;
            offset.y = 0f;
            Assert.Less(Vector3.Angle(offset, a.transform.right), 0.05f, "On A's line");
            Assert.Less(Quaternion.Angle(a.transform.rotation, b.transform.rotation), 0.05f, "Same rotation as A");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator BlockWellOffLine_IsLeftAlone_WithAYellowGuide()
        {
            Piece a = null;
            yield return SetBlockAt(new Vector3(-3f, 0f, 4f), 0f, p => a = p);
            Vector3 aim = a.transform.position + (Quaternion.AngleAxis(8f, Vector3.up) * Vector3.right) * 2.4f;
            yield return HoldBlockAt(aim, 0f, _ => { });
            Assert.AreEqual(1, carry.Guides.Count);
            Assert.IsFalse(carry.Guides[0].Aligned);
            Assert.AreEqual(8f, carry.Guides[0].Degrees, 0.5f, "Reads how far off it is");
            carry.Drop();
        }

        [UnityTest]
        public IEnumerator PostSeatsSquareWithTheBlock_WhateverWayItsHeld()
        {
            Piece block = null;
            yield return SetBlockAt(new Vector3(3f, 0f, 4f), 25f, p => block = p);
            LookAt(block.transform.position);
            cam.rotation = Quaternion.LookRotation(block.SocketTop - cam.position);
            var post = carry.TakeNew(StockKind.FourByFour, 8f * Units.Foot);
            carry.RotateHeld(CarryController.Axis.Turn, 33f);
            for (int i = 0; i < 3; i++) { carry.Tick(Aim()); yield return null; }
            Assert.AreEqual(CarryController.Placement.PostInBlock, carry.Current);
            carry.Place();

            Assert.Greater(Vector3.Dot(post.GrainAxisWorld, Vector3.up), 0.9999f, "Plumb");
            float a = Vector3.SignedAngle(block.transform.right, post.transform.up, Vector3.up);
            float off = Mathf.Abs(a - Mathf.Round(a / 90f) * 90f);
            Assert.Less(off, 0.05f, $"Square with the block ({a:F2}°)");
        }
    }
}
