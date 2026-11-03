using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Cabin.Editor
{
    /// <summary>
    /// Turns the generated lumberjack (Meshy image-to-3D from the concept in Art/Models/Reference, auto-rigged by
    /// Meshy, clips applied from Meshy's library) into what the player prefab uses: the body prefab with its PS2 look
    /// and first-person meshes (<see cref="CharacterMeshes"/>), the clips, and an Animator Controller — a 1D
    /// blend on "Speed" (metres per second along the way the legs face; negative backing up: back-run, back-walk, idle,
    /// walk, run), each clip placed at the speed its feet actually move over the ground (<see cref="StrideSpeed"/>) and
    /// played up to half as fast again before the next takes over, so the feet don't slide; past the fastest the
    /// "MoveScale" parameter speeds the whole state up. A crouch blend on the same (backing up plays the crouch walk
    /// in reverse) while "Crouch" is set,
    /// a "Jump" trigger, and on the upper body only a "Hammer" trigger and a punch: "PunchCharge" (bool) winds the fist
    /// back and holds it there, the "Punch" trigger throws it at "PunchSpeed"; "PunchVariant" picks which of
    /// <see cref="Punches"/> it is.
    /// What first person cuts off (the head) is placed by hand: <see cref="HeadCutPath"/> holds the lumberjack and red
    /// boxes; everything inside a box is removed (Tools/Cabin/Build/Edit First-Person Head Cut).
    /// </summary>
    public static class CharacterGenerator
    {
        const string Dir = "Assets/_Project/Art/Models";
        const string Model = Dir + "/Lumberjack_v3";
        public const string RiggedModelPath = Model + "_Rigged.glb";
        public const string BodyPrefabPath = Dir + "/Lumberjack.prefab";
        public const string ControllerPath = Dir + "/Lumberjack.controller";
        const string MeshPath = Dir + "/Lumberjack_FirstPerson.asset";
        const string TexturePath = Dir + "/Lumberjack_Albedo.png";
        const string MaterialPath = "Assets/_Project/Art/Materials/Lumberjack.mat";
        const string MaskPath = Dir + "/Lumberjack_UpperBody.mask";
        /// <summary>Sam's cut: the lumberjack at rest plus boxes named HeadCut*; not generated once it exists.</summary>
        public const string HeadCutPath = Dir + "/Lumberjack_HeadCut.prefab";
        const string HeadCutMaterialPath = "Assets/_Project/Art/Materials/HeadCutMarker.mat";
        const string CutPrefix = "HeadCut";
        const string ReferenceName = "Lumberjack (reference, leave in place)";
        /// <summary>Rigged at 1.85 m; the player is 1.8 m with eyes at 1.7 m.</summary>
        public const float Scale = 1.8f / 1.85f;
        /// <summary>Keys per second when cutting a clip: plenty for a PS2 character, and keeps the files small.</summary>
        const float SampleRate = 30f;
        /// <summary>The crouch walk at this speed (m/s) plays at its natural pace (the controller's crouch speed).</summary>
        const float CrouchWalkSpeed = 2f;

        /// <summary>
        /// The punches, all right-handed from a boxing guard (Meshy clips): where the fist is drawn back (the wind-up
        /// held while charging ends there), where the strike ends, and how long after the wind-up the fist is fully out.
        /// PlayerBody picks one at random each time.
        /// </summary>
        public static readonly (string Name, string Clip, float WindStart, float WindEnd, float StrikeEnd, float Lands)[] Punches =
        {
            ("Straight", "_Punch.glb", 1.2f, 1.73f, 2.6f, 0.2f), // Boxing_Guard_Prep_Straight_Punch (210)
            ("Jab", "_Jab.glb", 0.0f, 0.41f, 1.1f, 0.27f),       // Right_Jab_from_Guard (192)
            ("Hook", "_Hook.glb", 0.0f, 0.41f, 1.2f, 0.18f),     // Right_Upper_Hook_from_Guard (195)
        };

        public static bool Available => AssetDatabase.LoadAssetAtPath<GameObject>(RiggedModelPath) != null;

        /// <summary>From the last <see cref="Generate"/>: the top speed (m/s) each blend plays at its natural pace — forward, backing up, crouching — for PlayerBody.SetStrides.</summary>
        public static Vector3 TopSpeeds { get; private set; } = new Vector3(6.5f, 4.5f, 2f);
        /// <summary>How much faster than recorded a walk or run clip plays before the next one takes over.</summary>
        const float Stretch = 1.5f;

        /// <summary>The body the player wears (null until generated).</summary>
        public static GameObject BodyPrefab => AssetDatabase.LoadAssetAtPath<GameObject>(BodyPrefabPath);

        /// <summary>One of the generated first-person / fist meshes by name (null until generated).</summary>
        public static Mesh GeneratedMesh(string name) =>
            AssetDatabase.LoadAllAssetsAtPath(MeshPath).OfType<Mesh>().FirstOrDefault(m => m.name == name);

        [MenuItem("Tools/Cabin/Build/Generate Player Character", priority = 12)]
        public static RuntimeAnimatorController Generate()
        {
            if (!Available)
            {
                Debug.LogWarning($"[Cabin] No character at {RiggedModelPath}");
                return null;
            }
            CharacterMeshes.Build(RiggedModelPath, HeadCuts(), MeshPath, TexturePath, MaterialPath, BodyPrefabPath);

            // Meshy's idle stands with the hips turned up to 60°; straightened so the legs and the punch face forward.
            // Meshy's idle stands relaxed, then looks right round to the left and the right (the face through 240°,
            // the shoulders 155°): Sam wanted a chiller one. Its stillest moment with the face forward — the relaxed
            // stance it starts and ends in — is held; PlayerBody breathes on top of it.
            var idle = Copy(Model + "_Idle.glb", Dir + "/Lumberjack_Idle.anim", true, 0f, -1f, false, straightenHips: true);
            if (idle != null) idle = HoldStill(idle, StillestTime(idle));
            var walk = Copy(Model + "_Rigged_walking.glb", Dir + "/Lumberjack_Walk.anim", true);
            var run = Copy(Model + "_Rigged_running.glb", Dir + "/Lumberjack_Run.anim", true);
            var walkBack = Copy(Model + "_WalkBack.glb", Dir + "/Lumberjack_WalkBack.anim", true);
            var crouchWalk = Copy(Model + "_CrouchWalk.glb", Dir + "/Lumberjack_CrouchWalk.anim", true);
            // Crouching still: one frame of the crouch walk, feet together.
            var crouchIdle = Copy(Model + "_CrouchWalk.glb", Dir + "/Lumberjack_CrouchIdle.anim", true, 0f, 0f);
            // Regular_Jump: crouch 0–0.45 s, air 0.5–1.1 s, landing to 1.75 s. The controller does the crouch-free
            // launch and the height, so start at take-off and keep the hips from rising off the ground.
            var jump = Copy(Model + "_Jump.glb", Dir + "/Lumberjack_Jump.anim", false, 0.42f, 1.75f, true);
            // Heavy_Hammer_Swing: wind-up from 0.7 s, strike at 1.6 s, recovered by 1.85 s.
            var hammer = Copy(Model + "_Hammer.glb", Dir + "/Lumberjack_Hammer.anim", false, 0.7f, 1.85f, false);
            // Each punch stands side-on in a guard, turning the hips; the upper-body layer leaves the hips to the legs, so
            // the hips' turn is folded into the spine or the punch would go off to the side.
            var winds = new AnimationClip[Punches.Length];
            var strikes = new AnimationClip[Punches.Length];
            for (int i = 0; i < Punches.Length; i++)
            {
                var p = Punches[i];
                winds[i] = Copy(Model + p.Clip, $"{Dir}/Lumberjack_Punch{p.Name}Charge.anim", false, p.WindStart, p.WindEnd, false, foldHips: true);
                strikes[i] = Copy(Model + p.Clip, $"{Dir}/Lumberjack_Punch{p.Name}.anim", false, p.WindEnd, p.StrikeEnd, false, foldHips: true);
            }

            // Built at a scratch path (a controller's states must be saved as they're made), then moved over the real one.
            string scratch = Scratch(ControllerPath);
            if (AssetDatabase.LoadMainAssetAtPath(scratch) != null) AssetDatabase.DeleteAsset(scratch);
            var controller = AnimatorController.CreateAnimatorControllerAtPath(scratch);
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            controller.AddParameter(new AnimatorControllerParameter { name = "MoveScale", type = AnimatorControllerParameterType.Float, defaultFloat = 1f });
            controller.AddParameter("Jump", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Hammer", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Crouch", AnimatorControllerParameterType.Bool);
            controller.AddParameter("PunchCharge", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Punch", AnimatorControllerParameterType.Trigger);
            controller.AddParameter(new AnimatorControllerParameter { name = "PunchSpeed", type = AnimatorControllerParameterType.Float, defaultFloat = 1f });
            controller.AddParameter("PunchVariant", AnimatorControllerParameterType.Int);

            // Where each clip sits: the speed its planted foot moves at (falling back to the old guesses).
            float Measured(AnimationClip c, float fallback)
            {
                float v = StrideSpeed(c);
                return v > 0.3f && v < 12f ? v : fallback;
            }
            float walkSpeed = Measured(walk, 2.5f);
            float runSpeed = Mathf.Max(Measured(run, 6.5f), walkSpeed * Stretch + 0.1f);
            float backSpeed = Measured(walkBack, 2.5f);
            float crouchSpeed = Measured(crouchWalk, CrouchWalkSpeed);
            TopSpeeds = new Vector3(runSpeed * Stretch, backSpeed * Stretch, crouchSpeed);
            Debug.Log($"[Cabin] Stride speeds (m/s): walk {walkSpeed:F2}, run {runSpeed:F2}, back {backSpeed:F2}, crouch {crouchSpeed:F2}");

            var machine = controller.layers[0].stateMachine;
            var locomotion = controller.CreateBlendTreeInController("Locomotion", out var tree);
            tree.blendType = BlendTreeType.Simple1D;
            tree.blendParameter = "Speed";
            tree.useAutomaticThresholds = false;
            // Thresholds ascending; each clip at its own pace, then played up to Stretch as fast before the next.
            var scales = new List<float>();
            void Add(AnimationClip c, float at, float scale)
            {
                if (c == null) return;
                tree.AddChild(c, at);
                scales.Add(scale);
            }
            Add(walkBack, -backSpeed * Stretch, Stretch);
            Add(walkBack, -backSpeed, 1f);
            Add(idle, 0f, 1f);
            Add(walk, walkSpeed, 1f);
            Add(walk, walkSpeed * Stretch, Stretch);
            Add(run, runSpeed, 1f);
            Add(run, runSpeed * Stretch, Stretch);
            for (int i = 0; i < scales.Count; i++) if (!Mathf.Approximately(scales[i], 1f)) SetTimeScale(tree, i, scales[i]);
            locomotion.speedParameterActive = true;
            locomotion.speedParameter = "MoveScale";
            machine.defaultState = locomotion;
            if (crouchWalk != null)
            {
                var crouching = controller.CreateBlendTreeInController("Crouch", out var crouchTree);
                crouchTree.blendType = BlendTreeType.Simple1D;
                crouchTree.blendParameter = "Speed";
                crouchTree.useAutomaticThresholds = false;
                crouchTree.AddChild(crouchWalk, -crouchSpeed);
                crouchTree.AddChild(crouchIdle, 0f);
                crouchTree.AddChild(crouchWalk, crouchSpeed);
                SetTimeScale(crouchTree, 0, -1f);
                crouching.speedParameterActive = true;
                crouching.speedParameter = "MoveScale";
                var down = locomotion.AddTransition(crouching);
                down.AddCondition(AnimatorConditionMode.If, 0f, "Crouch");
                down.hasExitTime = false;
                down.duration = 0.2f;
                var up = crouching.AddTransition(locomotion);
                up.AddCondition(AnimatorConditionMode.IfNot, 0f, "Crouch");
                up.hasExitTime = false;
                up.duration = 0.2f;
            }
            if (jump != null)
            {
                var jumping = machine.AddState("Jump");
                jumping.motion = jump;
                var up = locomotion.AddTransition(jumping);
                up.AddCondition(AnimatorConditionMode.If, 0f, "Jump");
                up.hasExitTime = false;
                up.duration = 0.08f;
                var down = jumping.AddTransition(locomotion);
                down.hasExitTime = true;
                down.exitTime = 0.85f;
                down.duration = 0.2f;
            }

            if (hammer != null)
            {
                var upper = new AnimatorStateMachine { name = "UpperBody", hideFlags = HideFlags.HideInHierarchy };
                AssetDatabase.AddObjectToAsset(upper, controller);
                controller.AddLayer(new AnimatorControllerLayer
                {
                    name = "UpperBody",
                    defaultWeight = 1f,
                    avatarMask = UpperBodyMask(),
                    stateMachine = upper,
                });
                var empty = upper.AddState("Empty");
                var swing = upper.AddState("Hammer");
                swing.motion = hammer;
                swing.speed = 1.4f;
                upper.defaultState = empty;
                var start = empty.AddTransition(swing);
                start.AddCondition(AnimatorConditionMode.If, 0f, "Hammer");
                start.hasExitTime = false;
                start.duration = 0.1f;
                var end = swing.AddTransition(empty);
                end.hasExitTime = true;
                end.exitTime = 0.85f;
                end.duration = 0.2f;

                for (int i = 0; i < Punches.Length; i++)
                {
                    if (strikes[i] == null) continue;
                    var winding = upper.AddState("PunchCharge" + Punches[i].Name);
                    winding.motion = winds[i];
                    var throwing = upper.AddState("Punch" + Punches[i].Name);
                    throwing.motion = strikes[i];
                    throwing.speedParameterActive = true;
                    throwing.speedParameter = "PunchSpeed";
                    var wind = empty.AddTransition(winding);
                    wind.AddCondition(AnimatorConditionMode.If, 0f, "PunchCharge");
                    wind.AddCondition(AnimatorConditionMode.Equals, i, "PunchVariant");
                    wind.hasExitTime = false;
                    wind.duration = 0.08f;
                    // A tap too quick to wind up still throws.
                    var tap = empty.AddTransition(throwing);
                    tap.AddCondition(AnimatorConditionMode.If, 0f, "Punch");
                    tap.AddCondition(AnimatorConditionMode.Equals, i, "PunchVariant");
                    tap.hasExitTime = false;
                    tap.duration = 0.05f;
                    // Release throws; listed before the cancel so it wins when both hold.
                    var release = winding.AddTransition(throwing);
                    release.AddCondition(AnimatorConditionMode.If, 0f, "Punch");
                    release.hasExitTime = false;
                    release.duration = 0.04f;
                    var cancel = winding.AddTransition(empty);
                    cancel.AddCondition(AnimatorConditionMode.IfNot, 0f, "PunchCharge");
                    cancel.hasExitTime = false;
                    cancel.duration = 0.2f;
                    var back = throwing.AddTransition(empty);
                    back.hasExitTime = true;
                    back.exitTime = 0.8f;
                    back.duration = 0.2f;
                }
            }
            controller = (AnimatorController)MoveOver(scratch, ControllerPath);
            Debug.Log("[Cabin] Player character generated");
            return controller;
        }

        /// <summary>
        /// How fast a locomotion clip carries the body over the ground (m/s, at the player's scale): sampled through
        /// the cycle on the rig, the speed of whichever foot is lower (the planted one) relative to the hips, the
        /// median over the cycle. Zero without the clip.
        /// </summary>
        public static float StrideSpeed(AnimationClip clip)
        {
            if (clip == null || clip.length <= 0f) return 0f;
            var rig = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(RiggedModelPath));
            try
            {
                rig.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                var all = rig.GetComponentsInChildren<Transform>(true);
                Transform Find(string n) => System.Array.Find(all, t => t.name == n);
                Transform hipsBone = Find("Hips"), left = Find("LeftFoot"), right = Find("RightFoot");
                if (hipsBone == null || left == null || right == null) return 0f;
                const int samples = 120;
                float dt = clip.length / samples;
                var speeds = new List<float>();
                Vector3 lastL = Vector3.zero, lastR = Vector3.zero;
                for (int k = 0; k <= samples; k++)
                {
                    clip.SampleAnimation(rig, k * dt);
                    Vector3 l = left.position - hipsBone.position, r = right.position - hipsBone.position;
                    l.y = 0f;
                    r.y = 0f;
                    if (k > 0) speeds.Add(((left.position.y < right.position.y ? l - lastL : r - lastR).magnitude) / dt);
                    lastL = l;
                    lastR = r;
                }
                speeds.Sort();
                return speeds[speeds.Count / 2] * Scale;
            }
            finally
            {
                Object.DestroyImmediate(rig);
            }
        }

        /// <summary>
        /// Which way the shoulders and the face are turned at each moment of a clip (degrees about vertical from
        /// square to the front; sampled on the rig).
        /// </summary>
        public static (float time, float shoulders, float face)[] Facing(AnimationClip clip, int samples)
        {
            var rig = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(RiggedModelPath));
            try
            {
                rig.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                var all = rig.GetComponentsInChildren<Transform>(true);
                Transform Find(string n) => System.Array.Find(all, t => t.name == n);
                Transform left = Find("LeftArm"), right = Find("RightArm"), head = Find("Head"), front = Find("headfront");
                var result = new (float, float, float)[samples + 1];
                for (int k = 0; k <= samples; k++)
                {
                    float t = clip.length * k / samples;
                    clip.SampleAnimation(rig, t);
                    Vector3 across = right.position - left.position, look = front.position - head.position;
                    across.y = 0f;
                    look.y = 0f;
                    result[k] = (t, Vector3.SignedAngle(Vector3.right, across, Vector3.up), Vector3.SignedAngle(Vector3.forward, look, Vector3.up));
                }
                return result;
            }
            finally
            {
                Object.DestroyImmediate(rig);
            }
        }

        /// <summary>The moment the pose is stillest (the shoulders and face turning least) with the face forward.</summary>
        public static float StillestTime(AnimationClip clip)
        {
            var f = Facing(clip, 80);
            float best = float.MaxValue, at = 0f;
            for (int k = 1; k < f.Length - 1; k++)
            {
                float motion = Mathf.Abs(f[k + 1].shoulders - f[k - 1].shoulders) + Mathf.Abs(f[k + 1].face - f[k - 1].face);
                float score = Mathf.Abs(f[k].face) + 3f * motion;
                if (score < best) { best = score; at = f[k].time; }
            }
            return at;
        }

        /// <summary>The clip held at one moment for its whole length: every curve flat at its value then.</summary>
        static AnimationClip HoldStill(AnimationClip clip, float at)
        {
            var still = new AnimationClip { frameRate = SampleRate, name = clip.name };
            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            {
                float v = AnimationUtility.GetEditorCurve(clip, binding).Evaluate(at);
                AnimationUtility.SetEditorCurve(still, binding, new AnimationCurve(new Keyframe(0f, v), new Keyframe(clip.length, v)));
            }
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            AnimationUtility.SetAnimationClipSettings(still, settings);
            return Save(still, AssetDatabase.GetAssetPath(clip));
        }

        static void SetTimeScale(BlendTree tree, int child, float scale)
        {
            var children = tree.children;
            children[child].timeScale = scale;
            tree.children = children;
        }

        /// <summary>
        /// Moves the clip's hip turn up into the lower spine, keeping the chest where the clip has it: the hips become
        /// <c>h'</c> and Spine02 becomes h'⁻¹ · hips · spine. Fully (h' = rest: for an upper-body-only clip, whose hips
        /// are the legs' business) or only the turn about vertical (h' = hips without its yaw: legs face forward).
        /// </summary>
        static void FoldHipsIntoSpine(AnimationClip clip, bool yawOnly)
        {
            var bindings = AnimationUtility.GetCurveBindings(clip);
            EditorCurveBinding[] Rotation(string bone) => new[] { "x", "y", "z", "w" }
                .Select(c => bindings.First(b => b.path.EndsWith("/" + bone) && b.propertyName == "m_LocalRotation." + c)).ToArray();
            var hipsB = Rotation("Hips");
            var spineB = Rotation("Spine02");
            var hips = hipsB.Select(b => AnimationUtility.GetEditorCurve(clip, b)).ToArray();
            var spine = spineB.Select(b => AnimationUtility.GetEditorCurve(clip, b)).ToArray();
            var rig = AssetDatabase.LoadAssetAtPath<GameObject>(RiggedModelPath);
            var rest = rig.GetComponentsInChildren<Transform>().First(t => t.name == "Hips").localRotation;
            var restForward = rest * Vector3.forward;

            var times = spine[0].keys.Select(k => k.time).ToArray();
            var outKeys = new Keyframe[4][];
            var hipKeys = new Keyframe[4][];
            for (int c = 0; c < 4; c++)
            {
                outKeys[c] = new Keyframe[times.Length];
                hipKeys[c] = new Keyframe[times.Length];
            }
            for (int i = 0; i < times.Length; i++)
            {
                float t = times[i];
                var h = new Quaternion(hips[0].Evaluate(t), hips[1].Evaluate(t), hips[2].Evaluate(t), hips[3].Evaluate(t));
                var s = new Quaternion(spine[0].Evaluate(t), spine[1].Evaluate(t), spine[2].Evaluate(t), spine[3].Evaluate(t));
                h.Normalize();
                Quaternion hNew = rest;
                if (yawOnly)
                {
                    float yaw = Vector3.SignedAngle(Vector3.ProjectOnPlane(restForward, Vector3.up),
                        Vector3.ProjectOnPlane(h * Vector3.forward, Vector3.up), Vector3.up);
                    hNew = Quaternion.AngleAxis(-yaw, Vector3.up) * h;
                }
                var q = (Quaternion.Inverse(hNew) * h * s.normalized).normalized;
                hipKeys[0][i] = new Keyframe(t, hNew.x);
                hipKeys[1][i] = new Keyframe(t, hNew.y);
                hipKeys[2][i] = new Keyframe(t, hNew.z);
                hipKeys[3][i] = new Keyframe(t, hNew.w);
                outKeys[0][i] = new Keyframe(t, q.x);
                outKeys[1][i] = new Keyframe(t, q.y);
                outKeys[2][i] = new Keyframe(t, q.z);
                outKeys[3][i] = new Keyframe(t, q.w);
            }
            for (int c = 0; c < 4; c++)
            {
                var curve = new AnimationCurve(outKeys[c]);
                for (int i = 0; i < curve.length; i++) curve.SmoothTangents(i, 0f);
                AnimationUtility.SetEditorCurve(clip, spineB[c], curve);
                if (!yawOnly) continue;
                var hipCurve = new AnimationCurve(hipKeys[c]);
                for (int i = 0; i < hipCurve.length; i++) hipCurve.SmoothTangents(i, 0f);
                AnimationUtility.SetEditorCurve(clip, hipsB[c], hipCurve);
            }
        }

        /// <summary>
        /// Saves a freshly built asset to <paramref name="path"/>. If one is already there its file is overwritten
        /// rather than the asset deleted and recreated, so its GUID — which the controller, prefabs and the Player's
        /// Animator point at — survives a regenerate. (Copying into the old object in place drops or bloats data
        /// for clips and controllers; writing the file is exact.)
        /// </summary>
        static T Save<T>(T fresh, string path) where T : Object
        {
            if (!System.IO.File.Exists(path))
            {
                fresh.name = System.IO.Path.GetFileNameWithoutExtension(path);
                AssetDatabase.CreateAsset(fresh, path);
                return fresh;
            }
            string scratch = Scratch(path);
            if (AssetDatabase.LoadMainAssetAtPath(scratch) != null) AssetDatabase.DeleteAsset(scratch);
            fresh.name = System.IO.Path.GetFileNameWithoutExtension(scratch);
            AssetDatabase.CreateAsset(fresh, scratch);
            return MoveOver(scratch, path) as T;
        }

        static string Scratch(string path) =>
            path.Substring(0, path.LastIndexOf('.')) + "_Building" + System.IO.Path.GetExtension(path);

        /// <summary>Copies the scratch asset's file over the real one (whose .meta stays) and deletes the scratch.</summary>
        static Object MoveOver(string scratch, string path)
        {
            AssetDatabase.SaveAssets();
            if (!System.IO.File.Exists(path))
            {
                AssetDatabase.MoveAsset(scratch, path);
                return AssetDatabase.LoadMainAssetAtPath(path);
            }
            // Same file but for the name the scratch copy was given.
            string text = System.IO.File.ReadAllText(scratch)
                .Replace("m_Name: " + System.IO.Path.GetFileNameWithoutExtension(scratch),
                    "m_Name: " + System.IO.Path.GetFileNameWithoutExtension(path));
            System.IO.File.WriteAllText(path, text);
            AssetDatabase.DeleteAsset(scratch);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            return AssetDatabase.LoadMainAssetAtPath(path);
        }

        [MenuItem("Tools/Cabin/Build/Edit First-Person Head Cut", priority = 13)]
        static void EditHeadCut()
        {
            EnsureHeadCut();
            AssetDatabase.OpenAsset(AssetDatabase.LoadAssetAtPath<GameObject>(HeadCutPath));
            Debug.Log("[Cabin] Move/rotate/scale the red box (duplicate it for more cuts), save the prefab, then run " +
                "Tools/Cabin/Build/Generate Player Character.");
        }

        /// <summary>
        /// The cut boxes from the head-cut prefab, as matrices taking a point (rig at the origin, at rest) into each box's
        /// unit cube. Measured relative to the reference model, so it doesn't matter if the whole prefab moved.
        /// </summary>
        public static Matrix4x4[] HeadCuts()
        {
            EnsureHeadCut();
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(HeadCutPath);
            var model = root.transform.Find(ReferenceName);
            var toModel = model != null ? model.worldToLocalMatrix : root.transform.worldToLocalMatrix;
            return root.GetComponentsInChildren<Transform>(true)
                .Where(t => t.name.StartsWith(CutPrefix) && t.gameObject.activeSelf) // prefab assets are never "in hierarchy"
                .Select(t => (toModel * t.localToWorldMatrix).inverse)
                .ToArray();
        }

        /// <summary>
        /// First time only: the lumberjack and one red box over his head, its bottom tilted along the jaw — under the
        /// chin at the front, the base of the skull at the back.
        /// </summary>
        static void EnsureHeadCut()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(HeadCutPath) != null || !Available) return;
            var root = new GameObject("Lumberjack_HeadCut");
            try
            {
                var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(RiggedModelPath), root.transform);
                model.name = ReferenceName;
                var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Object.DestroyImmediate(box.GetComponent<Collider>());
                box.name = CutPrefix;
                box.transform.SetParent(root.transform, false);
                box.transform.SetPositionAndRotation(new Vector3(0.004f, 1.76f, 0.056f), Quaternion.Euler(18f, 0f, 0f));
                box.transform.localScale = new Vector3(0.24f, 0.3f, 0.36f);
                box.GetComponent<Renderer>().sharedMaterial = MarkerMaterial();
                PrefabUtility.SaveAsPrefabAsset(root, HeadCutPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>See-through red, so the head shows through the box.</summary>
        static Material MarkerMaterial()
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(HeadCutMaterialPath);
            if (m != null) return m;
            m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            m.SetColor("_BaseColor", new Color(1f, 0.1f, 0.1f, 0.35f));
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            AssetDatabase.CreateAsset(m, HeadCutMaterialPath);
            return m;
        }

        /// <summary>Everything from the lower spine up: the hammer swing leaves the legs to the walk cycle.</summary>
        static AvatarMask UpperBodyMask()
        {
            var body = BodyPrefab;
            var paths = new List<string>();
            foreach (var t in body.GetComponentsInChildren<Transform>(true))
                if (t != body.transform) paths.Add(AnimationUtility.CalculateTransformPath(t, body.transform));
            var mask = new AvatarMask { transformCount = paths.Count };
            for (int i = 0; i < paths.Count; i++)
            {
                mask.SetTransformPath(i, paths[i]);
                mask.SetTransformActive(i, paths[i].Contains("/Spine02"));
            }
            return Save(mask, MaskPath);
        }

        /// <summary>
        /// The imported clip is read-only; copy it into an .anim of our own, optionally looping, cut to
        /// [start, end] seconds, and with the hips held down so the clip doesn't add height to a real jump.
        /// </summary>
        static AnimationClip Copy(string glbPath, string animPath, bool loop, float start = 0f, float end = -1f, bool groundHips = false,
            bool foldHips = false, bool straightenHips = false)
        {
            var source = AssetDatabase.LoadAllAssetsAtPath(glbPath).OfType<AnimationClip>()
                .FirstOrDefault(c => !c.name.StartsWith("__preview__"));
            if (source == null) return null;
            AnimationClip copy;
            if (start <= 0f && end < 0f && !groundHips && !straightenHips)
                copy = Object.Instantiate(source);
            else
            {
                copy = new AnimationClip { frameRate = SampleRate };
                if (end < 0f) end = source.length;
                foreach (var binding in AnimationUtility.GetCurveBindings(source))
                {
                    var curve = AnimationUtility.GetEditorCurve(source, binding);
                    bool hipsY = groundHips && binding.path.EndsWith("Hips") && binding.propertyName == "m_LocalPosition.y";
                    float rest = curve.Evaluate(0f);
                    var keys = new List<Keyframe>();
                    for (float t = start; t <= end + 1e-4f; t += 1f / SampleRate)
                    {
                        float v = curve.Evaluate(t);
                        if (hipsY) v = Mathf.Min(v, rest);
                        keys.Add(new Keyframe(t - start, v));
                    }
                    var cut = new AnimationCurve(keys.ToArray());
                    for (int i = 0; i < cut.length; i++) cut.SmoothTangents(i, 0f);
                    AnimationUtility.SetEditorCurve(copy, binding, cut);
                }
                if (foldHips) FoldHipsIntoSpine(copy, yawOnly: false);
                if (straightenHips) FoldHipsIntoSpine(copy, yawOnly: true);
            }
            copy.name = System.IO.Path.GetFileNameWithoutExtension(animPath);
            var settings = AnimationUtility.GetAnimationClipSettings(copy);
            settings.loopTime = loop;
            AnimationUtility.SetAnimationClipSettings(copy, settings);

            return Save(copy, animPath);
        }
    }
}
