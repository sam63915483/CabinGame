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
    /// <summary>
    /// Clamps fit the wood where they sit: jaws as deep as the pieces are thick there (never stretched by a long or
    /// angled piece), a short reach over the nearest corner, sitting on the touching patch, and their frame passing
    /// through nothing.
    /// </summary>
    public class ClampLayoutTests
    {
        static readonly float Post = Stock.Size(StockKind.FourByFour, 1f).z;
        static readonly float BoardT = Stock.Size(StockKind.TwoByFour, 1f).z;

        BuildSite site;

        [UnitySetUp]
        public IEnumerator Load()
        {
            yield return SceneManager.LoadSceneAsync("TestPlatform", LoadSceneMode.Single);
            yield return null;
            site = Object.FindFirstObjectByType<BuildSite>();
            Object.FindFirstObjectByType<FirstPersonController>().enabled = false;
            site.ClearAll();
            yield return null;
        }

        static float GroundY(Vector3 at) =>
            Physics.Raycast(at + Vector3.up * 50f, Vector3.down, out var hit, 100f, BuildLayers.GroundMask) ? hit.point.y : 0f;

        Piece PlantedPost(Vector3 at)
        {
            float g = GroundY(at);
            var post = site.TakeStock(StockKind.FourByFour, 8f * Units.Foot, new Vector3(at.x, g - 18f * Units.Inch + 4f * Units.Foot, at.z), Quaternion.Euler(0f, 0f, 90f));
            site.Plant(post);
            return post;
        }

        /// <summary>The clamp's frame and screw (as ClampLayout lays them out) overlap no collider at all, its own pieces included.</summary>
        static bool FrameClear(in ClampGrip g)
        {
            float zLo = g.JawOther - ClampLayout.PadThickness - ClampLayout.ArmThickness;
            float zHi = g.JawHeld + ClampLayout.PadThickness + ClampLayout.ScrewLength;
            var rot = Quaternion.LookRotation(g.Normal, Vector3.Cross(g.Normal, g.Side));
            // The bar: the part that crosses from one side of the wood to the other.
            Vector3 barCenter = g.Point + rot * new Vector3(g.Reach, 0f, (g.JawOther + g.JawHeld) * 0.5f);
            Vector3 barHalf = new Vector3(ClampLayout.ArmThickness * 0.5f, ClampLayout.ArmWidth * 0.5f, (g.JawHeld - g.JawOther) * 0.5f) - Vector3.one * 0.003f;
            int mask = BuildLayers.PieceMask | BuildLayers.GroundMask;
            if (Physics.OverlapBox(barCenter, barHalf, rot, mask, QueryTriggerInteraction.Ignore).Length > 0) return false;
            // The arms and screw run outside the two pieces' outer faces.
            foreach (float z in new[] { (zLo + g.JawOther) * 0.5f, (g.JawHeld + zHi) * 0.5f })
            {
                Vector3 c = g.Point + rot * new Vector3(g.Reach * 0.5f, 0f, z);
                Vector3 h = new Vector3(g.Reach * 0.5f, ClampLayout.ArmWidth * 0.5f, ClampLayout.ArmThickness * 0.3f);
                if (Physics.OverlapBox(c, h, rot, mask, QueryTriggerInteraction.Ignore).Length > 0) return false;
            }
            return true;
        }

        [UnityTest]
        public IEnumerator BoardAcrossAPost_ClampFitsItsThicknessAndGoesOverTheCorner()
        {
            var post = PlantedPost(new Vector3(2f, 0f, 2f));
            float y = GroundY(new Vector3(2f, 0f, 2f)) + 1f;
            var board = site.TakeStock(StockKind.TwoByFour, 8f * Units.Foot, new Vector3(2f + Post * 0.5f + BoardT * 0.5f, y, 2f), Quaternion.Euler(0f, 90f, 0f));
            yield return new WaitForFixedUpdate();
            Physics.SyncTransforms();

            var contacts = ContactFinder.Touching(board);
            Assert.AreEqual(1, contacts.Count);
            var grips = ClampLayout.Plan(board, contacts);
            Assert.AreEqual(1, grips.Count);
            var g = grips[0];
            Assert.AreEqual(BoardT, g.JawHeld, 0.004f, "Front jaw on the board's face");
            Assert.AreEqual(-Post, g.JawOther, 0.004f, "Back jaw on the post's back face");
            Assert.Less(g.Reach, Post, "A short reach — not stretched round the whole board or post");
            Assert.IsTrue(FrameClear(g), "The frame passes through nothing");
            Assert.Less(Mathf.Abs(Vector3.Dot(g.Point - contacts[0].Point, Vector3.right)), 0.003f, "On the contact plane");
        }

        [UnityTest]
        public IEnumerator AngledBrace_JawsMatchTheLocalThickness_NotTheWholeBoard()
        {
            var post = PlantedPost(new Vector3(-3f, 0f, 2f));
            float y = GroundY(new Vector3(-3f, 0f, 2f)) + 2f;
            // A brace on edge against the post's +x face, sloping at 45° (grain in the y–z plane).
            var brace = site.TakeStock(StockKind.TwoByFour, 10f * Units.Foot, new Vector3(-3f + Post * 0.5f + BoardT * 0.5f, y, 2f),
                Quaternion.Euler(45f, 0f, 0f) * Quaternion.Euler(0f, 90f, 0f));
            site.SetOnTable(brace, true); // pinned: held there, as if in your hands
            yield return new WaitForFixedUpdate();
            Physics.SyncTransforms();

            var contacts = ContactFinder.Touching(brace);
            Assert.AreEqual(1, contacts.Count);
            var g = ClampLayout.Plan(brace, contacts).Single();
            Assert.AreEqual(BoardT, g.JawHeld, 0.005f, "Front jaw on the brace's face, however long the brace");
            Assert.AreEqual(-Post, g.JawOther, 0.005f);
            Assert.Less(g.Reach, Post * 1.5f, "Over the corner, not round the whole brace");
            Assert.LessOrEqual(g.Reach, ClampLayout.MaxReach, "Never stretched");
            Assert.IsTrue(FrameClear(g), "The frame passes through nothing");
        }

        [UnityTest]
        public IEnumerator SomethingInTheWay_ClampGoesTheOtherWayRound()
        {
            var post = PlantedPost(new Vector3(6f, 0f, 2f));
            float y = GroundY(new Vector3(6f, 0f, 2f)) + 1f;
            var board = site.TakeStock(StockKind.TwoByFour, 8f * Units.Foot, new Vector3(6f + Post * 0.5f + BoardT * 0.5f, y, 2f), Quaternion.Euler(0f, 90f, 0f));
            yield return new WaitForFixedUpdate();
            Physics.SyncTransforms();
            var free = ClampLayout.Plan(board, ContactFinder.Touching(board)).Single();

            // Block that corner with another board right where the clamp went.
            Vector3 blockAt = free.Point + free.Side * free.Reach + free.Normal * ((free.JawHeld + free.JawOther) * 0.5f);
            var blocker = site.TakeStock(StockKind.TwoByFour, 2f * Units.Foot, blockAt, Quaternion.identity);
            site.SetOnTable(blocker, true);
            yield return new WaitForFixedUpdate();
            Physics.SyncTransforms();

            var contacts = ContactFinder.Touching(board).Where(c => c.Other == post).ToList();
            var g = ClampLayout.Plan(board, contacts).Single();
            Assert.Less(Vector3.Dot(g.Side, free.Side), 0.9f, "Wrapped a different way");
            Assert.IsTrue(FrameClear(g), "…through nothing");
        }

        [UnityTest]
        public IEnumerator BoardsLyingIntoEachOther_GetNoStretchedClamp()
        {
            float g0 = GroundY(new Vector3(-6f, 0f, -4f));
            // Two boards overlapping by most of their thickness along their whole length: no clamp can reach round.
            var a = site.TakeStock(StockKind.TwoByFour, 8f * Units.Foot, new Vector3(-6f, g0 + 1f, -4f), Quaternion.Euler(0f, 90f, 0f));
            var b = site.TakeStock(StockKind.TwoByFour, 8f * Units.Foot, new Vector3(-6f + BoardT * 0.3f, g0 + 1f, -4f), Quaternion.Euler(0f, 90f, 0f));
            site.SetOnTable(a, true);
            site.SetOnTable(b, true);
            yield return new WaitForFixedUpdate();
            Physics.SyncTransforms();
            foreach (var g in ClampLayout.Plan(b, ContactFinder.Touching(b)))
                Assert.LessOrEqual(g.Reach, ClampLayout.MaxReach, "No clamp stretches past a real clamp's throat");
        }

        [UnityTest]
        public IEnumerator BoardAlongABeam_TwoClampsNearTheEnds_OnThePatch()
        {
            float g0 = GroundY(new Vector3(0f, 0f, -4f));
            // A 2×4 laid flat (grain along x, thickness vertical) along the top of another, overlapping about 2 m.
            var flat = Quaternion.Euler(90f, 0f, 0f);
            var beam = site.TakeStock(StockKind.TwoByFour, 10f * Units.Foot, new Vector3(0f, g0 + 1f, -4f), flat);
            site.SetOnTable(beam, true);
            yield return new WaitForFixedUpdate();
            Physics.SyncTransforms();
            float top = ContactFinder.Span(beam, Vector3.up).y;
            var board = site.TakeStock(StockKind.TwoByFour, 8f * Units.Foot, new Vector3(1.2f, top + 0.5f, -4f), flat);
            Vector2 span = ContactFinder.Span(board, Vector3.up);
            board.transform.position += Vector3.up * (top - span.x);
            site.SetOnTable(board, true);
            yield return new WaitForFixedUpdate();
            Physics.SyncTransforms();

            var contacts = ContactFinder.Touching(board);
            Assert.AreEqual(1, contacts.Count);
            var grips = ClampLayout.Plan(board, contacts);
            Assert.AreEqual(2, grips.Count, "Two clamps along a long overlap");
            Vector3 grain = board.GrainAxisWorld;
            Assert.Greater(Mathf.Abs(Vector3.Dot(grips[0].Point - grips[1].Point, grain)), 1f, "Spread along it");
            foreach (var g in grips)
            {
                Assert.Less(Mathf.Abs(Vector3.Dot(g.Side, grain)), 0.3f, "Each wraps across the boards, not along them");
                Assert.IsTrue(FrameClear(g), "Frame through nothing");
            }
        }
    }
}
