using System.Collections.Generic;
using System.Linq;
using Cabin.Editor;
using Cabin.Player;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Cabin.Tests
{
    public class PlayerRigTests
    {
        [Test]
        public void HandShapes_CloseInOrder_AndTheTriggerFingerStaysCrooked()
        {
            float Sum(float[,] a, int f) => a[f, 0] + a[f, 1] + a[f, 2];
            var relaxed = HandPose.Angles(HandShape.Relaxed);
            var grip = HandPose.Angles(HandShape.Grip);
            var fist = HandPose.Angles(HandShape.Fist);
            var pistol = HandPose.Angles(HandShape.Pistol);
            var can = HandPose.Angles(HandShape.Can);
            for (int f = 1; f < HandPose.Fingers; f++)
            {
                Assert.Less(Sum(relaxed, f), Sum(grip, f), "Relaxed is more open than a grip");
                Assert.Less(Sum(grip, f), Sum(fist, f), "A grip is more open than a fist");
            }
            Assert.Less(Sum(pistol, 1), Sum(pistol, 2), "The index rests on the trigger, the rest close round the grip");
            Assert.Less(Sum(can, 2), Sum(grip, 2), "A can is wider than a handle");
            CollectionAssert.AreEqual(HandPose.GripBend, new[] { grip[2, 0], grip[2, 1], grip[2, 2] }, "The grip the editor measures the handle by");
        }

        [Test]
        public void HandPose_BendsEachFingerTowardsThePalm()
        {
            var hand = new GameObject("RightHand").transform;
            try
            {
                string[] names = { "Thumb", "Index", "Middle", "Ring", "Pinky" };
                var tips = new Transform[5];
                for (int f = 0; f < 5; f++)
                {
                    Transform parent = hand;
                    for (int j = 0; j < 3; j++)
                    {
                        var joint = new GameObject($"RightHand{names[f]}{j + 1}").transform;
                        joint.SetParent(parent, false);
                        joint.localPosition = j == 0 ? new Vector3(f * 0.02f, 0.1f, 0f) : new Vector3(0f, 0.03f, 0f);
                        parent = joint;
                    }
                    var tip = new GameObject("tip").transform;
                    tip.SetParent(parent, false);
                    tip.localPosition = new Vector3(0f, 0.03f, 0f);
                    tips[f] = tip;
                }
                var pose = new HandPose(hand, false);
                Assert.IsTrue(pose.Valid);
                Vector3 open = tips[2].position;
                pose.Set(HandShape.Fist);
                for (int i = 0; i < 60; i++) pose.Apply(1f / 30f);
                Vector3 closed = tips[2].position;
                Assert.Greater(closed.z - open.z, 0.03f, "The middle finger curls towards the palm (+z of its joints)");
                Assert.Less(closed.y, open.y, "and its tip comes back down towards the knuckles");
                Assert.IsFalse(new HandPose(new GameObject("Bare").transform, true).Valid, "A hand without fingers is left alone");
            }
            finally
            {
                Object.DestroyImmediate(hand.gameObject);
                var bare = GameObject.Find("Bare");
                if (bare != null) Object.DestroyImmediate(bare);
            }
        }

        [Test]
        public void Legs_StepArc_HipsDrop_AndStrain()
        {
            Assert.AreEqual(0f, Legs.StepArc(0f, 0.07f), 1e-6f);
            Assert.AreEqual(0.07f, Legs.StepArc(0.5f, 0.07f), 1e-6f, "Highest half way");
            Assert.AreEqual(0f, Legs.StepArc(1f, 0.07f), 1e-5f);
            Assert.AreEqual(-0.12f, Legs.HipsDrop(new[] { -0.12f, 0.05f }, 0.35f), 1e-6f, "Down as far as the lower foot needs");
            Assert.AreEqual(0f, Legs.HipsDrop(new[] { 0.1f, 0.05f }, 0.35f), 1e-6f, "Never up");
            Assert.AreEqual(-0.35f, Legs.HipsDrop(new[] { -0.8f, 0f }, 0.35f), 1e-6f, "At most the limit");
            var ahead = Quaternion.identity;
            Assert.AreEqual(0f, Legs.Strain(Vector3.zero, Vector3.zero, ahead, ahead, 0.22f, 30f), 1e-6f);
            Assert.AreEqual(1f, Legs.Strain(Vector3.zero, new Vector3(0.22f, 0.5f, 0f), ahead, ahead, 0.22f, 30f), 1e-4f, "Height doesn't count");
            Assert.AreEqual(1.5f, Legs.Strain(Vector3.zero, Vector3.zero, ahead, Quaternion.Euler(0f, 45f, 0f), 0.22f, 30f), 1e-3f, "Turning counts");
        }

        [Test]
        public void MoveScale_IsOneUpToTheTopSpeed_ThenInProportion()
        {
            Assert.AreEqual(1f, PlayerBody.MoveScale(3f, false, 6f, 4f, 2f), 1e-6f);
            Assert.AreEqual(7f / 6f, PlayerBody.MoveScale(7f, false, 6f, 4f, 2f), 1e-5f, "Sprinting past the run");
            Assert.AreEqual(1.25f, PlayerBody.MoveScale(-5f, false, 6f, 4f, 2f), 1e-5f, "Backing up uses the back-walk's top");
            Assert.AreEqual(1.5f, PlayerBody.MoveScale(3f, true, 6f, 4f, 2f), 1e-5f, "Crouching uses the crouch walk's");
            Assert.AreEqual(1f, PlayerBody.MoveScale(0f, false, 6f, 4f, 2f), 1e-6f);
        }

        [Test]
        public void Eyes_AreAtTheEyesLevel_AndComeForwardOverTheChestLookingDown()
        {
            // The lumberjack at player scale: head bone at 1.551 m, 6.9 cm behind the middle; his eyes 1.655 m up;
            // the front of his chest 9–12 cm in front of the middle from the collar (1.47 m) down; the neck's stump
            // (the head cut) no further forward than 8 cm.
            Vector3 head = new Vector3(0f, 1.551f, -0.069f);
            Vector3 level = head + PlayerBody.EyeOffset(0f);
            Assert.That(level.y, Is.InRange(1.66f, 1.72f), "Level: at the eyes or a touch above (Sam felt short at the eyes)");
            Assert.That(level.z, Is.InRange(0.03f, 0.08f), "Level: just behind the face");
            Vector3 down = head + PlayerBody.EyeOffset(89f);
            Assert.Greater(down.y, 1.47f + 0.08f, "Looking down: well above the collar, so the shirt is below");
            Assert.That(down.z, Is.InRange(0.09f, 0.16f), "Looking down: over the front of the chest");
            Assert.Greater(down.z, 0.08f, "and in front of the neck's stump");
            Vector3 up = head + PlayerBody.EyeOffset(-89f);
            Assert.Less(up.z, level.z, "Looking up: the head tips back");
        }

        [Test]
        public void TwistAngle_IsThePartAboutTheAxis()
        {
            Assert.AreEqual(40f, ArmReach.TwistAngle(Quaternion.AngleAxis(40f, Vector3.up), Vector3.up), 1e-3f);
            Assert.AreEqual(-70f, ArmReach.TwistAngle(Quaternion.AngleAxis(70f, Vector3.down), Vector3.up), 1e-3f);
            Assert.AreEqual(0f, ArmReach.TwistAngle(Quaternion.AngleAxis(50f, Vector3.right), Vector3.up), 1e-3f, "A pure swing has no twist");
            var both = Quaternion.AngleAxis(30f, Vector3.right) * Quaternion.AngleAxis(25f, Vector3.up);
            Assert.AreEqual(25f, ArmReach.TwistAngle(both, Vector3.up), 3f, "Swing then twist: about the twist");
        }

        [Test]
        public void ArmReach_PutsTheWristOnTheGrip_AndTakesItsTurn()
        {
            var upper = new GameObject("Shoulder").transform;
            try
            {
                upper.position = new Vector3(0.2f, 1.5f, 0f);
                var lower = new GameObject("Elbow").transform;
                lower.SetParent(upper, false);
                lower.localPosition = new Vector3(0f, -0.3f, 0f);
                var hand = new GameObject("Wrist").transform;
                hand.SetParent(lower, false);
                hand.localPosition = new Vector3(0f, -0.28f, 0f);
                var reach = new ArmReach(upper, lower, hand, new Vector3(0f, -0.08f, 0.03f), Vector3.forward);
                var wrist = new Vector3(0.25f, 1.3f, 0.4f);
                var grip = Quaternion.Euler(20f, 30f, 0f);
                Vector3 got = reach.Hold(wrist, grip, upper.position + new Vector3(0.5f, -0.6f, -0.3f));
                Assert.Less((got - wrist).magnitude, 1e-3f, "Wrist on the grip");
                Assert.Less(Quaternion.Angle(hand.rotation, grip), 0.01f, "Hand turned as the grip says");
                // The forearm took part of the twist: the hand sits less twisted on it than all-at-the-wrist would.
                var noShare = new ArmReach(upper, lower, hand, new Vector3(0f, -0.08f, 0.03f), Vector3.forward) { ForearmShare = 0f };
                noShare.Hold(wrist, grip, upper.position + new Vector3(0.5f, -0.6f, -0.3f));
                float wristAlone = Quaternion.Angle(hand.localRotation, Quaternion.identity);
                reach.Hold(wrist, grip, upper.position + new Vector3(0.5f, -0.6f, -0.3f));
                float shared = Quaternion.Angle(hand.localRotation, Quaternion.identity);
                Assert.LessOrEqual(shared, wristAlone + 1e-3f, "The wrist bends less with the forearm sharing the twist");
                Assert.Less((hand.position - wrist).magnitude, 1e-3f, "and the wrist is still on the grip");
                // A handle: the hollow of the hand ends up on it.
                var point = new Vector3(0.2f, 1.25f, 0.35f);
                Vector3 shortBy = reach.HoldHandle(point, Vector3.up, upper.position + new Vector3(0.5f, -0.6f, -0.3f), Vector3.forward);
                Assert.Less(shortBy.magnitude, 1e-3f, "Within reach");
                Assert.Less((hand.TransformPoint(new Vector3(0f, -0.08f, 0.03f)) - point).magnitude, 1e-3f, "The hollow is on the handle");
                Assert.Greater(Vector3.Dot(hand.TransformDirection(Vector3.forward), Vector3.up), 0.999f, "and runs along it");
            }
            finally
            {
                Object.DestroyImmediate(upper.gameObject);
            }
        }

        [Test]
        public void Idle_StandsStill_FacingTheFront()
        {
            if (CharacterGenerator.Generate() == null) Assert.Ignore("No rigged lumberjack in Art/Models");
            var idle = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/_Project/Art/Models/Lumberjack_Idle.anim");
            var facing = CharacterGenerator.Facing(idle, 40);
            Assert.Less(facing.Max(x => x.shoulders) - facing.Min(x => x.shoulders), 1f, "The shoulders don't swing");
            Assert.Less(facing.Max(x => x.face) - facing.Min(x => x.face), 1f, "nor the head");
            Assert.Less(Mathf.Abs(facing[0].face), 10f, "The face looks ahead");
            Assert.Less(Mathf.Abs(facing[0].shoulders), 25f, "the shoulders near square (a relaxed stance)");
            Assert.IsTrue(AnimationUtility.GetAnimationClipSettings(idle).loopTime, "Still loops (a held pose)");
        }

        [Test]
        public void AxeStrike_SwingsRoundTheHands_AndTheEdgeLandsOnTheSpot()
        {
            var hit = new Cabin.Build.AxePose(new Vector3(0.05f, -0.16f, 0.32f), new Vector3(-0.2f, 0.12f, 0.97f), new Vector3(-1f, 0f, -0.2f), 0.04f);
            var shoulders = new Vector3(0f, -0.4f, -0.15f);
            // Within reach of arms and axe: the edge lands on it, the hands on the line from the shoulders.
            var near = new Vector3(0.05f, -1.1f, 0.6f);
            var a = hit.AimFrom(shoulders, 0.6f, near);
            Assert.Less((a.EdgeAt - near).magnitude, 0.01f, "The edge comes down on the spot");
            Assert.LessOrEqual((a.Hand - shoulders).magnitude, 0.6f + 1e-4f, "The hands no further than the arms reach");
            Assert.Less((a.Hand - hit.Hand).magnitude, 0.4f, "The hands stay near where the strike has them (this target is 0.9 m below them)");
            Assert.Greater(Vector3.Dot(a.Rotation * Vector3.forward, hit.Rotation * Vector3.forward), 0f, "The blade still faces about the way it did");
            // Out of reach: arms straight, the edge pointing at it.
            var far = new Vector3(0.3f, -1.4f, 2.2f);
            var b = hit.AimFrom(shoulders, 0.5f, far);
            Assert.AreEqual(0.5f, (b.Hand - shoulders).magnitude, 1e-3f);
            Assert.Less(Vector3.Angle(b.EdgeAt - b.Hand, far - b.Hand), 1f);
        }

        [Test]
        public void Chop_BendsForLowCuts_AndLeansInAndOut()
        {
            Assert.AreEqual(0f, Cabin.Build.AxeTool.BendFor(1.2f), 1e-4f, "Waist-high cut: upright");
            Assert.Greater(Cabin.Build.AxeTool.BendFor(0.4f), Cabin.Build.AxeTool.BendFor(0.8f), "Lower cut, deeper bend");
            Assert.AreEqual(0f, Cabin.Build.AxeTool.LeanIn(0f), 1e-4f);
            Assert.AreEqual(1f, Cabin.Build.AxeTool.LeanIn(Cabin.Build.AxeTool.WindUp + Cabin.Build.AxeTool.Strike * 0.5f), 1e-4f, "Fully in at the strike");
            Assert.AreEqual(0f, Cabin.Build.AxeTool.LeanIn(Cabin.Build.AxeTool.WindUp + Cabin.Build.AxeTool.Strike + Cabin.Build.AxeTool.Recover), 1e-4f, "Back up after");
        }

        [Test]
        public void TopFour_MergesTheSameBone_KeepsTheHeaviest_AndSumsToOne()
        {
            var w = FingerRig.TopFour(new List<(int, float)> { (3, 0.2f), (5, 0.1f), (3, 0.2f), (7, 0.15f), (9, 0.05f), (11, 0.3f) });
            Assert.AreEqual(3, w.boneIndex0);
            Assert.AreEqual(11, w.boneIndex1);
            Assert.AreEqual(7, w.boneIndex2);
            Assert.AreEqual(5, w.boneIndex3);
            Assert.AreEqual(1f, w.weight0 + w.weight1 + w.weight2 + w.weight3, 1e-5f);
            Assert.Greater(w.weight0, w.weight1);
        }

        [Test]
        public void Body_HasRealFingerBones_SkinnedToTheHands_AndClipsMeasureTheirStrides()
        {
            if (CharacterGenerator.Generate() == null) Assert.Ignore("No rigged lumberjack in Art/Models");
            if (AssetDatabase.LoadAssetAtPath<GameObject>(FingerRig.MixamoPath) == null) Assert.Ignore("No Mixamo finger rig");
            var full = CharacterGenerator.BodyPrefab.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single(r => r.name == CharacterMeshes.FullName);
            var bones = full.bones;
            Assert.AreEqual(bones.Length, full.sharedMesh.bindposes.Length);
            foreach (bool left in new[] { false, true })
            {
                var hand = bones.Single(b => b.name == (left ? "LeftHand" : "RightHand"));
                for (int f = 0; f < 5; f++)
                for (int j = 0; j < 3; j++)
                {
                    string name = FingerRig.JointName(left, f, j);
                    var joint = bones.SingleOrDefault(b => b.name == name);
                    Assert.IsNotNull(joint, name);
                    Assert.IsTrue(joint.IsChildOf(hand), $"{name} moves with the hand");
                }
            }
            var fingerIndices = new HashSet<int>(Enumerable.Range(0, bones.Length).Where(i => bones[i].name.Contains("Hand") && char.IsDigit(bones[i].name[bones[i].name.Length - 1])));
            int skinned = full.sharedMesh.boneWeights.Count(w =>
                (fingerIndices.Contains(w.boneIndex0) && w.weight0 > 0.3f) || (fingerIndices.Contains(w.boneIndex1) && w.weight1 > 0.3f));
            Assert.Greater(skinned, 100, "The fingers carry their own vertices");

            // At rest the re-skinned body is exactly the model it came from: no vertex thrown anywhere.
            var ours = Object.Instantiate(CharacterGenerator.BodyPrefab);
            var theirs = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(CharacterGenerator.RiggedModelPath));
            var a = new Mesh();
            var b = new Mesh();
            try
            {
                ours.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single(r => r.name == CharacterMeshes.FullName).BakeMesh(a, true);
                theirs.GetComponentInChildren<SkinnedMeshRenderer>().BakeMesh(b, true);
                var va = a.vertices;
                var vb = b.vertices;
                Assert.AreEqual(vb.Length, va.Length);
                float worst = 0f;
                for (int i = 0; i < va.Length; i++) worst = Mathf.Max(worst, (va[i] - vb[i]).magnitude);
                Assert.Less(worst, 0.002f, "Every vertex where the model has it");
            }
            finally
            {
                Object.DestroyImmediate(ours);
                Object.DestroyImmediate(theirs);
                Object.DestroyImmediate(a);
                Object.DestroyImmediate(b);
            }

            float walk = CharacterGenerator.StrideSpeed(AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/_Project/Art/Models/Lumberjack_Walk.anim"));
            float run = CharacterGenerator.StrideSpeed(AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/_Project/Art/Models/Lumberjack_Run.anim"));
            Assert.That(walk, Is.InRange(0.5f, 3f), "A walking pace");
            Assert.Greater(run, walk * 1.3f, "Running covers more ground");
        }
    }
}
