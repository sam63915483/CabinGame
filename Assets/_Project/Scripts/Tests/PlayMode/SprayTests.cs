using System.Collections;
using Cabin.Build;
using Cabin.Player;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Cabin.Tests
{
    /// <summary>Spray paint in the real scene: a tight spot up close, a light wide mist far off, shadows, drips, cuts, save/load.</summary>
    public class SprayTests
    {
        BuildSite site;
        SprayCan can;
        static readonly Color Red = new Color(0.8f, 0.05f, 0.05f);

        [UnitySetUp]
        public IEnumerator Load()
        {
            yield return SceneManager.LoadSceneAsync("TestPlatform", LoadSceneMode.Single);
            yield return null;
            site = Object.FindFirstObjectByType<BuildSite>();
            can = Object.FindFirstObjectByType<SprayCan>();
            Object.FindFirstObjectByType<FirstPersonController>().enabled = false;
            site.ClearAll();
            yield return null;
        }

        static float GroundY(Vector3 at) =>
            Physics.Raycast(at + Vector3.up * 50f, Vector3.down, out var hit, 100f, BuildLayers.GroundMask) ? hit.point.y : 0f;

        /// <summary>A plywood sheet standing upright, face towards -z, pinned in place.</summary>
        Piece Wall(Vector3 centre)
        {
            var sheet = site.TakeStock(StockKind.Plywood, 8f * Units.Foot, centre, Quaternion.identity);
            site.SetOnTable(sheet, true); // pinned without needing anything to clamp to
            Physics.SyncTransforms();
            return sheet;
        }

        static float CoverageAt(Piece p, Vector3 world, Vector3 normal)
        {
            if (p.Paint == null) return 0f;
            Vector3 local = p.transform.InverseTransformPoint(world);
            Vector3 n = p.transform.InverseTransformDirection(normal);
            return p.Atlas.Locate(local, n, out _, out var t) ? p.Paint.Coverage(t) : -1f;
        }

        /// <summary>Best coverage within a small square patch around a point (a single texel is too luck-dependent for mist).</summary>
        static float PatchCoverage(Piece p, Vector3 world, Vector3 normal, Vector3 across, Vector3 up, float half)
        {
            float best = 0f;
            for (float a = -half; a <= half; a += 0.004f)
                for (float b = -half; b <= half; b += 0.004f)
                    best = Mathf.Max(best, CoverageAt(p, world + across * a + up * b, normal));
            return best;
        }

        void SprayFor(Vector3 nozzle, Vector3 at, float seconds, SprayCan.Cap cap)
        {
            const float dt = 1f / 60f;
            for (float t = 0f; t < seconds; t += dt) can.Spray(nozzle, at, dt, Red, cap);
        }

        [UnityTest]
        public IEnumerator FineCapUpClose_PaintsATightHeavySpot()
        {
            float g = GroundY(new Vector3(0f, 0f, 5f));
            var wall = Wall(new Vector3(0f, g + 1.2f, 5f));
            float face = 5f - Stock.Size(StockKind.Plywood, 0f).z * 0.5f;
            var at = new Vector3(0f, g + 1.2f, face);
            SprayFor(at - Vector3.forward * 0.15f, at, 0.4f, SprayCan.Cap.Fine);
            Assert.Greater(CoverageAt(wall, at, Vector3.back), 0.85f, "Solid at the centre");
            Assert.Less(CoverageAt(wall, at + Vector3.right * 0.05f, Vector3.back), 0.25f, "A tight line: little 5 cm away");
            Assert.AreEqual(0f, CoverageAt(wall, at + Vector3.right * 0.15f, Vector3.back), "Nothing 15 cm away");
            Assert.AreEqual(0f, CoverageAt(wall, at + Vector3.forward * Stock.Size(StockKind.Plywood, 0f).z, Vector3.forward), "Back of the sheet untouched");
            yield return null;
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator WideCapFromFarAway_IsALightWideMist()
        {
            float g = GroundY(new Vector3(-3f, 0f, 5f));
            var wall = Wall(new Vector3(-3f, g + 1.2f, 5f));
            float face = 5f - Stock.Size(StockKind.Plywood, 0f).z * 0.5f;
            var at = new Vector3(-3f, g + 1.2f, face);
            SprayFor(at - Vector3.forward * 1.8f, at, 0.25f, SprayCan.Cap.Wide);
            float centre = CoverageAt(wall, at, Vector3.back);
            float off = PatchCoverage(wall, at + Vector3.right * 0.15f, Vector3.back, Vector3.right, Vector3.up, 0.03f);
            Assert.Less(centre, 0.6f, "A quick pass from far away is light");
            Assert.Greater(off, 0.0f, "…and spread wide");
            yield return null;
        }

        [UnityTest]
        public IEnumerator SomethingInFront_ShadowsWhatsBehind()
        {
            float g = GroundY(new Vector3(3f, 0f, 5f));
            var back = Wall(new Vector3(3f, g + 1.2f, 5f));
            float face = 5f - Stock.Size(StockKind.Plywood, 0f).z * 0.5f;
            // A 4×4 standing 30 cm in front of the sheet, between it and the can.
            var post = site.TakeStock(StockKind.FourByFour, 8f * Units.Foot, new Vector3(3f, g + 1.2f, face - 0.3f), Quaternion.Euler(0f, 0f, 90f));
            site.SetOnTable(post, true);
            Physics.SyncTransforms();
            var at = new Vector3(3f, g + 1.2f, face);
            SprayFor(at - Vector3.forward * 0.6f, at, 0.5f, SprayCan.Cap.Fine);
            Assert.Greater(CoverageAt(post, new Vector3(3f, g + 1.2f, face - 0.3f - Stock.Size(StockKind.FourByFour, 1f).z * 0.5f), Vector3.back), 0.5f, "The post took the paint");
            Assert.LessOrEqual(CoverageAt(back, at, Vector3.back), 0f, "The sheet behind it got none");
            yield return null;
        }

        [UnityTest]
        public IEnumerator TooMuchPaint_OnAWall_Drips()
        {
            float g = GroundY(new Vector3(0f, 0f, 8f));
            var wall = Wall(new Vector3(0f, g + 1.3f, 8f));
            float face = 8f - Stock.Size(StockKind.Plywood, 0f).z * 0.5f;
            var at = new Vector3(0f, g + 1.3f, face);
            bool dripped = false;
            const float dt = 1f / 60f;
            for (float t = 0f; t < 3f; t += dt)
            {
                can.Spray(at - Vector3.forward * 0.12f, at, dt, Red, SprayCan.Cap.Fine);
                can.RunDrips(dt);
                dripped |= can.ActiveDrips > 0;
            }
            for (float t = 0f; t < 2f; t += dt) can.RunDrips(dt);
            Assert.IsTrue(dripped, "Over-spraying a vertical face makes runs");
            Assert.Greater(CoverageAt(wall, at + Vector3.down * 0.04f, Vector3.back), 0.3f, "Paint ran down below the spot");
            Assert.AreEqual(0f, CoverageAt(wall, at + Vector3.up * 0.08f, Vector3.back), "…not up");
            yield return null;
        }

        [UnityTest]
        public IEnumerator Paint_SurvivesACut_AndSaveLoad()
        {
            float g = GroundY(new Vector3(0f, 0f, 11f));
            var wall = Wall(new Vector3(0f, g + 1.2f, 11f));
            float face = 11f - Stock.Size(StockKind.Plywood, 0f).z * 0.5f;
            var spot = new Vector3(0.6f, g + 1.2f, face);
            SprayFor(spot - Vector3.forward * 0.15f, spot, 0.4f, SprayCan.Cap.Fine);
            Assert.Greater(CoverageAt(wall, spot, Vector3.back), 0.8f);

            // Saw the sheet in half left of the spot; the spot's half keeps its paint, the cut edge is bare.
            Assert.IsNull(site.Cut(wall, new Plane(Vector3.right, new Vector3(0.2f, 0f, 0f)), out var offcut));
            Physics.SyncTransforms();
            var spotHalf = Vector3.Dot(wall.transform.TransformPoint(wall.Solid.Bounds.center) - new Vector3(0.2f, 0f, 0f), Vector3.right) > 0f ? wall : offcut;
            Assert.Greater(CoverageAt(spotHalf, spot, Vector3.back), 0.8f, "Paint stayed put through the cut");
            Assert.AreEqual(0f, CoverageAt(spotHalf, new Vector3(0.2f + 0.002f, g + 1.2f, 11f), Vector3.left), "Fresh cut face is bare");

            var json = JsonUtility.ToJson(site.ToSaveData());
            site.ClearAll();
            yield return null;
            site.Load(JsonUtility.FromJson<BuildSaveData>(json));
            yield return null;
            Physics.SyncTransforms();
            Piece reloaded = null;
            foreach (var p in site.Pieces.Values)
                if (CoverageAt(p, spot, Vector3.back) > 0.8f) reloaded = p;
            Assert.IsNotNull(reloaded, "Paint came back after loading");
        }
    }
}
