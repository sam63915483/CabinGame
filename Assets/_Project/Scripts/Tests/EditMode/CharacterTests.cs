using System.Linq;
using Cabin.Editor;
using Cabin.Player;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Cabin.Tests
{
    public class CharacterTests
    {
        [Test]
        public void Body_HasFullTorsoAndArms_OnOneSkeleton()
        {
            if (CharacterGenerator.Generate() == null) Assert.Ignore("No rigged lumberjack in Art/Models");
            var body = CharacterGenerator.BodyPrefab;
            var renderers = body.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var full = renderers.Single(r => r.name == CharacterMeshes.FullName);
            var torso = renderers.Single(r => r.name == CharacterMeshes.TorsoName);
            var arms = renderers.Single(r => r.name == CharacterMeshes.ArmName);
            var leftArm = renderers.Single(r => r.name == CharacterMeshes.LeftArmName);
            CollectionAssert.AreEqual(full.bones, torso.bones);
            CollectionAssert.AreEqual(full.bones, arms.bones);
            CollectionAssert.AreEqual(full.bones, leftArm.bones);
            Assert.AreEqual(0, renderers.Length - 4, "Nothing else draws");

            int Index(string name) => System.Array.FindIndex(full.bones, b => b.name == name);
            int head = Index("Head"), hand = Index("RightHand"), foot = Index("LeftFoot"), leftHand = Index("LeftHand");
            int[] Dominant(SkinnedMeshRenderer r) => r.sharedMesh.triangles.Select(v => r.sharedMesh.boneWeights[v].boneIndex0).ToArray();
            var torsoBones = Dominant(torso);
            var armBones = Dominant(arms);
            var leftArmBones = Dominant(leftArm);
            // Nothing inside the hand-placed head-cut boxes is drawn in first person.
            var bindTorso = torso.sharedMesh.bindposes;
            var skin = full.bones.Select((b, i) => b.localToWorldMatrix * bindTorso[i]).ToArray();
            var tv = torso.sharedMesh.vertices;
            var tw = torso.sharedMesh.boneWeights;
            var used = torso.sharedMesh.triangles.Distinct().Select(v => skin[tw[v].boneIndex0].MultiplyPoint3x4(tv[v])).ToArray();
            var cuts = CharacterGenerator.HeadCuts();
            Assert.Greater(cuts.Length, 0, "There is a head cut");
            Assert.IsTrue(used.All(p => CharacterMeshes.OutsideAll(cuts, p) > -1e-3f), "Nothing inside a cut box");
            CollectionAssert.DoesNotContain(torsoBones, hand, "The right arm is its own mesh");
            CollectionAssert.DoesNotContain(torsoBones, leftHand, "The left arm is its own mesh too (it holds a two-handed tool)");
            CollectionAssert.Contains(leftArmBones, leftHand);
            CollectionAssert.DoesNotContain(leftArmBones, hand);
            CollectionAssert.DoesNotContain(leftArmBones, head);
            CollectionAssert.Contains(torsoBones, foot, "You can see your boots");
            CollectionAssert.Contains(armBones, hand);
            CollectionAssert.DoesNotContain(armBones, head);
            CollectionAssert.DoesNotContain(armBones, leftHand);

            var material = full.sharedMaterial;
            var texture = (Texture2D)material.GetTexture("_BaseMap");
            Assert.LessOrEqual(texture.width, CharacterMeshes.TextureSize, "PS2-sized texture");
            Assert.AreEqual(0f, material.GetFloat("_Smoothness"));
        }

        [Test]
        public void Controller_HasLocomotionCrouchJumpAndUpperBodyHammer()
        {
            var controller = CharacterGenerator.Generate() as AnimatorController;
            if (controller == null) Assert.Ignore("No rigged lumberjack in Art/Models");
            CollectionAssert.AreEquivalent(new[] { "Speed", "MoveScale", "Jump", "Hammer", "Crouch", "PunchCharge", "Punch", "PunchSpeed", "PunchVariant" }, controller.parameters.Select(p => p.name));
            var states = controller.layers[0].stateMachine.states.Select(s => s.state.name).ToArray();
            CollectionAssert.AreEquivalent(new[] { "Locomotion", "Crouch", "Jump" }, states);
            Assert.AreEqual(2, controller.layers.Length);
            var upperStates = controller.layers[1].stateMachine.states.Select(s => s.state.name).ToArray();
            CollectionAssert.Contains(upperStates, "Hammer");
            foreach (var p in CharacterGenerator.Punches)
            {
                CollectionAssert.Contains(upperStates, "PunchCharge" + p.Name);
                CollectionAssert.Contains(upperStates, "Punch" + p.Name);
            }
            var mask = controller.layers[1].avatarMask;
            Assert.IsNotNull(mask);
            for (int i = 0; i < mask.transformCount; i++)
            {
                string path = mask.GetTransformPath(i);
                if (path.EndsWith("RightHand")) Assert.IsTrue(mask.GetTransformActive(i), "Arms swing");
                if (path.EndsWith("LeftFoot")) Assert.IsFalse(mask.GetTransformActive(i), "Legs keep walking");
            }

            // The jump clip never lifts the hips above standing: the controller does the lifting.
            var jump = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/_Project/Art/Models/Lumberjack_Jump.anim");
            var hipsY = AnimationUtility.GetCurveBindings(jump).First(b => b.path.EndsWith("Hips") && b.propertyName == "m_LocalPosition.y");
            float max = AnimationUtility.GetEditorCurve(jump, hipsY).keys.Max(k => k.value);
            var source = AssetDatabase.LoadAllAssetsAtPath("Assets/_Project/Art/Models/Lumberjack_v3_Jump.glb").OfType<AnimationClip>()
                .First(c => !c.name.StartsWith("__preview__"));
            Assert.LessOrEqual(max, AnimationUtility.GetEditorCurve(source, hipsY).Evaluate(0f) + 1e-3f);
        }

        [Test]
        public void TwoBoneIK_ReachesTheTarget_BendingTowardsThePole()
        {
            var root = new GameObject("Shoulder");
            try
            {
                var upper = root.transform;
                upper.position = new Vector3(0.2f, 1.5f, 0f);
                var lower = new GameObject("Elbow").transform;
                lower.SetParent(upper, false);
                lower.localPosition = new Vector3(0f, -0.3f, 0f);
                var end = new GameObject("Wrist").transform;
                end.SetParent(lower, false);
                end.localPosition = new Vector3(0f, -0.28f, 0f);

                var target = new Vector3(0.25f, 1.3f, 0.4f);
                TwoBoneIK.Reach(upper, lower, end, target, upper.position + new Vector3(0.5f, -0.6f, -0.3f));
                Assert.Less((end.position - target).magnitude, 1e-3f, "Wrist on the target");
                Assert.AreEqual(0.3f, Vector3.Distance(upper.position, lower.position), 1e-4f, "Bones keep their length");
                Assert.Greater(lower.position.x, 0.2f, "Elbow out towards the pole");

                var far = upper.position + new Vector3(0f, 0f, 2f);
                TwoBoneIK.Reach(upper, lower, end, far, upper.position + Vector3.down);
                Assert.AreEqual(0.58f, Vector3.Distance(upper.position, end.position), 1e-3f, "Out of reach: straight arm");
                Assert.Greater(Vector3.Dot((end.position - upper.position).normalized, Vector3.forward), 0.999f, "pointing at it");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }
    }
}
