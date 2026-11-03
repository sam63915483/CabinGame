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
    /// <summary>Playtest 6: offcuts handle from their own middle, stock comes off the rack clear, the held piece turns with you, rotate keys.</summary>
    public class HandlingTests
    {
        const float In = Units.Inch;
        static readonly float HalfPost = Stock.Size(StockKind.FourByFour, 1f).z * 0.5f;
        static readonly float HalfBoard = Stock.Size(StockKind.TwoByFour, 1f).z * 0.5f;

        BuildSite site;
        CarryController carry;
        Transform player, cam;

        [UnitySetUp]
        public IEnumerator Load()
        {
            yield return SceneManager.LoadSceneAsync("TestPlatform", LoadSceneMode.Single);
            yield return null;
            site = Object.FindFirstObjectByType<BuildSite>();
            carry = Object.FindFirstObjectByType<CarryController>();
            var fpc = Object.FindFirstObjectByType<FirstPersonController>();
            fpc.enabled = false;
            player = fpc.transform;
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

        IEnumerator Hold(int frames)
        {
            for (int i = 0; i < frames; i++)
            {
                carry.Tick(Aim());
                yield return new WaitForFixedUpdate();
            }
        }

        static bool Penetrates(Piece p, float tolerance)
        {
            Physics.SyncTransforms();
            var b = p.Collider.bounds;
            int mask = Physics.DefaultRaycastLayers & ~BuildLayers.PlayerMask & ~BuildLayers.ClampMask & ~BuildLayers.NailMask;
            foreach (var col in Physics.OverlapBox(b.center, b.extents, Quaternion.identity, mask, QueryTriggerInteraction.Ignore))
            {
                if (col == p.Collider) continue;
                if (Physics.ComputePenetration(p.Collider, p.transform.position, p.transform.rotation, col, col.transform.position, col.transform.rotation, out _, out float depth) && depth > tolerance)
                    return true;
            }
            return false;
        }

        [UnityTest]
        public IEnumerator Offcut_IsRecentred_NailsDontMove_AndItCarriesFromItsMiddle()
        {
            float g = GroundY(new Vector3(2f, 0f, 2f));
            var post = site.TakeStock(StockKind.FourByFour, 8f * Units.Foot, new Vector3(2f, g - 18f * In + 1.2f, 2f), Quaternion.Euler(0f, 0f, 90f));
            site.Plant(post);
            float y = g + 1f;
            var board = site.TakeStock(StockKind.TwoByFour, 8f * Units.Foot, new Vector3(2f + HalfPost + HalfBoard, y, 2f), Quaternion.Euler(0f, 90f, 0f));
            Physics.SyncTransforms();
            Assert.IsNull(site.TryClamp(board));
            var nail = site.DriveNail(site.PlanNail(new Ray(new Vector3(3f, y, 2f), Vector3.left), 2f));
            Assert.IsNotNull(nail);
            site.Unclamp();
            Vector3 nailWorld = board.transform.TransformPoint(nail.entryA);

            // Saw a 0.4 m nub off the far end of the board (it runs along z, so cut across z).
            float boardEnd = 2f + Stock.Size(StockKind.TwoByFour, 8f * Units.Foot).x * 0.5f;
            Assert.IsNull(site.Cut(board, new Plane(Vector3.forward, new Vector3(0f, 0f, boardEnd - 0.4f)), out var nub));
            var stub = board.Length < nub.Length ? board : nub;
            var rest = stub == board ? nub : board;
            Assert.Less(stub.Solid.Bounds.center.magnitude, 1e-4f, "Offcut pivots about its own middle");
            Assert.Less(rest.Solid.Bounds.center.magnitude, 1e-4f, "…and so does the piece it came off");
            var n = site.Nails[nail.id];
            var nailed = site.Pieces[n.a];
            Assert.Less(Vector3.Distance(nailWorld, nailed.transform.TransformPoint(n.entryA)), 1e-4f, "The nail hasn't moved");
            Assert.AreEqual(rest, nailed, "The nail stayed in the long half");

            // Pick the nub up: it should float with its middle at the hands, not swing round a far-off pivot.
            cam.position = new Vector3(4f, y + 0.6f, boardEnd);
            cam.rotation = Quaternion.LookRotation(Vector3.left);
            carry.PickUp(stub, keepRotation: true);
            carry.SetDistance(1.5f);
            yield return Hold(60);
            Vector3 middle = stub.transform.TransformPoint(stub.Solid.Bounds.center);
            Assert.Less(Vector3.Distance(middle, carry.HoldPoint), 0.06f, "Held by its middle");
            carry.Drop();
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator TakingFromARack_LookingDownAtThePile_StartsClear()
        {
            var rack = Object.FindObjectsByType<StockRack>(FindObjectsSortMode.None)
                .First(r => r.Kind == StockKind.TwoByFour && Mathf.Approximately(r.Length, 8f * Units.Foot));
            Vector3 top = rack.GetComponent<BoxCollider>().bounds.center;
            cam.position = top + new Vector3(0f, 1.2f, -1.2f);
            cam.rotation = Quaternion.LookRotation(top - cam.position);
            var piece = carry.TakeNew(StockKind.TwoByFour, 8f * Units.Foot);
            Assert.IsFalse(Penetrates(piece, 0.005f), "Spawned clear of the pile and the ground");
            yield return Hold(40);
            Assert.IsFalse(Penetrates(piece, 0.02f), "…and still clear while held looking at the pile");
            carry.Drop();
        }

        [UnityTest]
        public IEnumerator TurningAround_KeepsTheSameFaceTowardsYou()
        {
            float g = GroundY(Vector3.zero);
            player.SetPositionAndRotation(new Vector3(0f, g, -4f), Quaternion.identity);
            cam.localPosition = new Vector3(0f, 1.7f, 0f);
            cam.localRotation = Quaternion.identity;
            var sheet = carry.TakeNew(StockKind.Plywood, 8f * Units.Foot);
            yield return Hold(30);
            float before = Vector3.Dot(sheet.transform.forward, cam.forward);
            Assert.Greater(Mathf.Abs(before), 0.95f, "Picked up face on");

            player.rotation = Quaternion.Euler(0f, 90f, 0f);
            yield return Hold(60);
            float after = Vector3.Dot(sheet.transform.forward, cam.forward);
            Assert.Greater(Mathf.Abs(after), 0.95f, "Still face on after turning 90°");
            Assert.AreEqual(Mathf.Sign(before), Mathf.Sign(after), "Same face");
            carry.Drop();
        }

        [UnityTest]
        public IEnumerator RotateKeys_TurnTipRoll_AboutThePlayersAxes()
        {
            float g = GroundY(Vector3.zero);
            player.SetPositionAndRotation(new Vector3(0f, g, -4f), Quaternion.identity);
            cam.localPosition = new Vector3(0f, 1.7f, 0f);
            cam.localRotation = Quaternion.identity;
            var board = carry.TakeNew(StockKind.TwoByFour, 8f * Units.Foot);
            Vector3 Grain() => carry.WorldRotation * Vector3.right;
            Assert.Greater(Mathf.Abs(Vector3.Dot(Grain(), cam.right)), 0.99f, "Starts across the view");

            carry.RotateHeld(CarryController.Axis.Turn, 90f);
            Assert.Greater(Mathf.Abs(Vector3.Dot(Grain(), Vector3.forward)), 0.99f, "R: pointing away from you");
            carry.RotateHeld(CarryController.Axis.Tip, 90f);
            Assert.Greater(Mathf.Abs(Grain().y), 0.99f, "T: stood upright");
            carry.RotateHeld(CarryController.Axis.Tip, -90f);
            carry.RotateHeld(CarryController.Axis.Turn, -90f);
            carry.RotateHeld(CarryController.Axis.Roll, 90f);
            Assert.Greater(Mathf.Abs(Grain().y), 0.99f, "Y: rolled upright");
            carry.RotateHeld(CarryController.Axis.Roll, 15f);
            float fromVertical = Vector3.Angle(Grain(), Vector3.up);
            Assert.AreEqual(15f, Mathf.Min(fromVertical, 180f - fromVertical), 0.1f, "Shift steps are 15°");
            yield return null;
            carry.Drop();
        }
    }
}
