using System.IO;
using System.Linq;
using Cabin.Build;
using Cabin.Core;
using Cabin.Player;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Cabin.Editor
{
    /// <summary>
    /// Generates everything the free-build sandbox needs: procedural wood/ground textures, materials, the nail
    /// head prefab, the Player and BuildSystem prefabs. Re-runnable; assets are updated in place so GUIDs survive.
    /// </summary>
    public static class SandboxAssetGenerator
    {
        public const string TexturesDir = "Assets/_Project/Art/Textures";
        public const string MaterialsDir = "Assets/_Project/Art/Materials";
        public const string MeshesDir = "Assets/_Project/Art/Meshes";
        public const string PrefabsDir = "Assets/_Project/Prefabs";
        public const string AudioDir = "Assets/_Project/Audio";
        public const string PlayerPrefabPath = PrefabsDir + "/Player.prefab";
        public const string BuildSystemPrefabPath = PrefabsDir + "/BuildSystem.prefab";
        public const string NailHeadPrefabPath = PrefabsDir + "/NailHead.prefab";
        public const string NailShotPrefabPath = PrefabsDir + "/NailShot.prefab";
        /// <summary>How far behind the camera the held-arm rig's head sits: where the body's does.</summary>
        public const string TreePrefabPath = PrefabsDir + "/Tree_Placeholder.prefab";

        [MenuItem("Tools/Cabin/Build/Generate Sandbox Assets", priority = 10)]
        public static void GenerateAll()
        {
            // In Play Mode, components added while building the prefabs run their Awake and save runtime-only
            // objects (the spray mist, its audio) into the assets.
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[Cabin] Stop Play Mode before generating sandbox assets");
                return;
            }
            EnsureLayer(BuildLayers.Piece, BuildLayers.PieceName);
            EnsureLayer(BuildLayers.Nail, BuildLayers.NailName);
            EnsureLayer(BuildLayers.Player, BuildLayers.PlayerName);
            EnsureLayer(BuildLayers.HeldPiece, BuildLayers.HeldPieceName);
            EnsureLayer(BuildLayers.Clamp, BuildLayers.ClampName);
            EnsureLayer(ToolViewModel.Layer, ToolViewModel.LayerName);
            foreach (var d in new[] { TexturesDir, MaterialsDir, MeshesDir, PrefabsDir }) EnsureFolder(d);

            var wood = Texture("Wood", 512, 512, WoodPixel);
            var ply = Texture("Plywood", 512, 512, PlywoodPixel);
            var grass = Texture("Grass", 256, 256, GrassPixel);
            var dirt = Texture("Dirt", 256, 256, DirtPixel);
            var concreteTex = Texture("Concrete", 256, 256, ConcretePixel);

            var lumber = Lit("Lumber", Color.white, wood, 0.12f);
            var plywood = Lit("Plywood", Color.white, ply, 0.10f);
            Lit("Ground_Grass", Color.white, grass, 0.05f);
            Lit("Ground_Dirt", Color.white, dirt, 0.05f);
            var concrete = Lit("Concrete", Color.white, concreteTex, 0.08f);
            var steel = Lit("Steel", new Color(0.22f, 0.23f, 0.26f), null, 0.75f);
            var clampOrange = Lit("ClampFrame", new Color(0.95f, 0.42f, 0.06f), null, 0.45f);
            var bark = Lit("Bark", new Color(0.36f, 0.26f, 0.18f), null, 0.05f);
            var needles = Lit("Needles", new Color(0.16f, 0.34f, 0.17f), null, 0.08f);
            Lit("Bench", new Color(0.62f, 0.48f, 0.33f), wood, 0.1f);

            var ghostValid = Unlit("Ghost_Valid", new Color(0.35f, 1f, 0.45f, 0.6f));
            var ghostInvalid = Unlit("Ghost_Invalid", new Color(1f, 0.3f, 0.3f, 0.6f));
            var blade = Unlit("Ghost_Blade", new Color(1f, 0.9f, 0.3f, 0.45f));
            var paint = LitTransparent("SprayPaint", 0.45f);
            var mist = Particles("SprayMist");

            var cone = SaveMesh(Cone(16), MeshesDir + "/Cone.asset");
            SaveMesh(Stock.Shape(StockKind.DeckBlock, 0f).ToMesh("DeckBlock"), MeshesDir + "/DeckBlock.asset");
            var nailHead = BuildNailHeadPrefab(steel);
            var nailShot = BuildNailShotPrefab(steel, Particles("NailTrail"));
            BuildTreePrefab(cone, bark, needles);
            ToolModelGenerator.GenerateAll();
            BuildPlayerPrefab();
            BuildSystemPrefab(lumber, plywood, concrete, clampOrange, steel, paint, mist, nailHead, nailShot, ghostValid, ghostInvalid, blade);

            AssetDatabase.SaveAssets();
            Debug.Log("[Cabin] Sandbox assets generated");
        }

        // ------------------------------------------------------------------ textures

        delegate Color PixelFn(float u, float v);

        static Color WoodPixel(float u, float v)
        {
            // Grain runs along u. ~40 growth rings per metre across v, wandering with the length.
            float wander = Mathf.PerlinNoise(u * 3.1f + 7.3f, v * 9.7f) * 2.2f + Mathf.PerlinNoise(u * 11f, v * 23f) * 0.5f;
            float ring = 0.5f + 0.5f * Mathf.Sin((v * 42f + wander) * Mathf.PI * 2f);
            ring = Mathf.Pow(ring, 2.2f);
            float fine = Mathf.PerlinNoise(u * 180f, v * 900f) * 0.10f - 0.05f;
            float knotty = Mathf.PerlinNoise(u * 1.3f + 3f, v * 1.3f) ;
            var light = new Color(0.83f, 0.66f, 0.45f);
            var dark = new Color(0.60f, 0.43f, 0.26f);
            var c = Color.Lerp(light, dark, ring * 0.75f) * (1f + fine) * Mathf.Lerp(0.94f, 1.04f, knotty);
            c.a = 1f;
            return c;
        }

        static Color PlywoodPixel(float u, float v)
        {
            float wander = Mathf.PerlinNoise(u * 2.2f, v * 2.6f + 4f) * 6f;
            float ring = 0.5f + 0.5f * Mathf.Sin((v * 9f + wander) * Mathf.PI * 2f);
            float fine = Mathf.PerlinNoise(u * 260f, v * 260f) * 0.08f - 0.04f;
            var light = new Color(0.87f, 0.76f, 0.57f);
            var dark = new Color(0.74f, 0.60f, 0.42f);
            var c = Color.Lerp(light, dark, Mathf.Pow(ring, 3f) * 0.6f) * (1f + fine);
            c.a = 1f;
            return c;
        }

        static Color GrassPixel(float u, float v)
        {
            float n = Mathf.PerlinNoise(u * 14f, v * 14f) * 0.5f + Mathf.PerlinNoise(u * 60f, v * 60f) * 0.35f + Mathf.PerlinNoise(u * 200f, v * 200f) * 0.15f;
            var c = Color.Lerp(new Color(0.27f, 0.40f, 0.16f), new Color(0.46f, 0.56f, 0.24f), n);
            c.a = 1f;
            return c;
        }

        static Color ConcretePixel(float u, float v)
        {
            float n = Mathf.PerlinNoise(u * 30f + 3f, v * 30f) * 0.5f + Mathf.PerlinNoise(u * 140f, v * 140f + 9f) * 0.5f;
            float speck = Mathf.PerlinNoise(u * 400f, v * 400f) > 0.72f ? -0.08f : 0f;
            var c = Color.Lerp(new Color(0.56f, 0.56f, 0.55f), new Color(0.70f, 0.69f, 0.66f), n) + new Color(speck, speck, speck);
            c.a = 1f;
            return c;
        }

        static Color DirtPixel(float u, float v)
        {
            float n = Mathf.PerlinNoise(u * 20f, v * 20f) * 0.6f + Mathf.PerlinNoise(u * 120f, v * 120f) * 0.4f;
            var c = Color.Lerp(new Color(0.33f, 0.25f, 0.17f), new Color(0.50f, 0.40f, 0.28f), n);
            c.a = 1f;
            return c;
        }

        static Texture2D Texture(string name, int w, int h, PixelFn fn)
        {
            string path = $"{TexturesDir}/{name}.png";
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    px[y * w + x] = fn(x / (float)w, y / (float)h);
            tex.SetPixels(px);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            if (AssetImporter.GetAtPath(path) is TextureImporter imp && (imp.wrapMode != TextureWrapMode.Repeat || imp.anisoLevel < 8))
            {
                imp.wrapMode = TextureWrapMode.Repeat;
                imp.anisoLevel = 8;
                imp.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // ------------------------------------------------------------------ materials

        static Material Lit(string name, Color color, Texture2D map, float smoothness)
        {
            var m = LoadOrCreateMaterial($"{MaterialsDir}/{name}.mat", "Universal Render Pipeline/Lit");
            m.SetColor("_BaseColor", color);
            m.SetTexture("_BaseMap", map);
            m.SetFloat("_Smoothness", smoothness);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>URP Lit, alpha-blended: spray paint overlays (texture alpha = paint coverage).</summary>
        static Material LitTransparent(string name, float smoothness)
        {
            var m = LoadOrCreateMaterial($"{MaterialsDir}/{name}.mat", "Universal Render Pipeline/Lit");
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Smoothness", smoothness);
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>URP particle material, alpha-blended, tinted by particle colour: the visible spray mist.</summary>
        static Material Particles(string name)
        {
            var m = LoadOrCreateMaterial($"{MaterialsDir}/{name}.mat", "Universal Render Pipeline/Particles/Unlit");
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material Unlit(string name, Color color)
        {
            var m = LoadOrCreateMaterial($"{MaterialsDir}/{name}.mat", "Universal Render Pipeline/Unlit");
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_Cull", (float)CullMode.Off);
            m.SetOverrideTag("RenderType", "Transparent");
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent;
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material LoadOrCreateMaterial(string path, string shader)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m != null) return m;
            m = new Material(Shader.Find(shader));
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        // ------------------------------------------------------------------ meshes

        /// <summary>Cone with its base at y = 0 and apex at y = 1, radius 1.</summary>
        static Mesh Cone(int sides)
        {
            var verts = new System.Collections.Generic.List<Vector3>();
            var normals = new System.Collections.Generic.List<Vector3>();
            var tris = new System.Collections.Generic.List<int>();
            for (int i = 0; i < sides; i++)
            {
                float a0 = i / (float)sides * Mathf.PI * 2f, a1 = (i + 1) / (float)sides * Mathf.PI * 2f;
                var b0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0));
                var b1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
                var apex = Vector3.up;
                Vector3 n = Vector3.Cross(b1 - b0, apex - b0).normalized;
                n = -n;
                int s = verts.Count;
                verts.Add(b0); verts.Add(apex); verts.Add(b1);
                normals.Add(n); normals.Add(n); normals.Add(n);
                tris.AddRange(new[] { s, s + 1, s + 2 });
                int c = verts.Count;
                verts.Add(b0); verts.Add(b1); verts.Add(Vector3.zero);
                normals.Add(Vector3.down); normals.Add(Vector3.down); normals.Add(Vector3.down);
                tris.AddRange(new[] { c, c + 1, c + 2 });
            }
            var mesh = new Mesh { name = "Cone" };
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        static Mesh SaveMesh(Mesh mesh, string path)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(mesh, path);
                return mesh;
            }
            EditorUtility.CopySerialized(mesh, existing);
            Object.DestroyImmediate(mesh);
            EditorUtility.SetDirty(existing);
            return existing;
        }

        // ------------------------------------------------------------------ prefabs

        static GameObject BuildNailHeadPrefab(Material steel)
        {
            var root = new GameObject("NailHead") { layer = BuildLayers.Nail };
            var sphere = root.AddComponent<SphereCollider>();
            sphere.radius = 0.03f;
            root.AddComponent<NailHead>();
            var head = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.DestroyImmediate(head.GetComponent<Collider>());
            head.name = "Head";
            head.layer = BuildLayers.Nail;
            head.transform.SetParent(root.transform, false);
            head.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            head.transform.localScale = new Vector3(0.016f, 0.0012f, 0.016f) * (Stock.CrossSectionScale / 2f);
            head.GetComponent<MeshRenderer>().sharedMaterial = steel;
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, NailHeadPrefabPath);
            Object.DestroyImmediate(root);
            return prefab;
        }

        /// <summary>
        /// A fired nail: head at the origin, a full-length shank running forward (+z) from it into whatever it hits,
        /// and a short bright streak behind it in flight. No collider: it never gets in the way.
        /// </summary>
        static GameObject BuildNailShotPrefab(Material steel, Material trailMaterial)
        {
            var root = new GameObject("NailShot");
            root.AddComponent<NailShot>();
            float s = Stock.CrossSectionScale / 2f;
            var head = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.DestroyImmediate(head.GetComponent<Collider>());
            head.name = "Head";
            head.transform.SetParent(root.transform, false);
            head.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            head.transform.localScale = new Vector3(0.016f, 0.0012f, 0.016f) * s;
            head.GetComponent<MeshRenderer>().sharedMaterial = steel;
            var shank = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.DestroyImmediate(shank.GetComponent<Collider>());
            shank.name = "Shank";
            shank.transform.SetParent(root.transform, false);
            shank.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            shank.transform.localPosition = new Vector3(0f, 0f, NailRules.NailLength * 0.5f);
            shank.transform.localScale = new Vector3(0.0033f * s * 2f, NailRules.NailLength * 0.5f, 0.0033f * s * 2f);
            shank.GetComponent<MeshRenderer>().sharedMaterial = steel;
            var trail = root.AddComponent<TrailRenderer>();
            trail.time = 0.05f;
            trail.widthMultiplier = 0.006f;
            trail.widthCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);
            trail.colorGradient = new Gradient
            {
                colorKeys = new[] { new GradientColorKey(new Color(0.9f, 0.9f, 0.85f), 0f), new GradientColorKey(new Color(0.9f, 0.9f, 0.85f), 1f) },
                alphaKeys = new[] { new GradientAlphaKey(0.7f, 0f), new GradientAlphaKey(0f, 1f) },
            };
            trail.minVertexDistance = 0.02f;
            trail.shadowCastingMode = ShadowCastingMode.Off;
            trail.sharedMaterial = trailMaterial;
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, NailShotPrefabPath);
            Object.DestroyImmediate(root);
            return prefab;
        }

        static void BuildTreePrefab(Mesh cone, Material bark, Material needles)
        {
            var root = new GameObject("Tree");
            var trunk = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.DestroyImmediate(trunk.GetComponent<Collider>());
            trunk.name = "Trunk";
            trunk.transform.SetParent(root.transform, false);
            trunk.transform.localPosition = new Vector3(0f, 1.6f, 0f);
            trunk.transform.localScale = new Vector3(0.36f, 1.6f, 0.36f);
            trunk.GetComponent<MeshRenderer>().sharedMaterial = bark;
            float[] radii = { 2.3f, 1.8f, 1.3f, 0.8f };
            float[] bases = { 1.4f, 3.6f, 5.8f, 8.0f };
            for (int i = 0; i < radii.Length; i++)
            {
                var tier = new GameObject("Canopy" + i);
                tier.transform.SetParent(root.transform, false);
                tier.transform.localPosition = new Vector3(0f, bases[i], 0f);
                tier.transform.localScale = new Vector3(radii[i], 3.4f, radii[i]);
                tier.AddComponent<MeshFilter>().sharedMesh = cone;
                tier.AddComponent<MeshRenderer>().sharedMaterial = needles;
            }
            var col = root.AddComponent<CapsuleCollider>();
            col.center = new Vector3(0f, 1.6f, 0f);
            col.height = 3.2f;
            col.radius = 0.2f;
            PrefabUtility.SaveAsPrefabAsset(root, TreePrefabPath);
            Object.DestroyImmediate(root);
        }

        /// <summary>Just the player (and the character it wears): after a change to the body, the arms or the legs.</summary>
        [MenuItem("Tools/Cabin/Build/Generate Player", priority = 11)]
        public static void GeneratePlayer()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[Cabin] Stop Play Mode before generating the player");
                return;
            }
            BuildPlayerPrefab();
            AssetDatabase.SaveAssets();
            Debug.Log("[Cabin] Player generated");
        }

        static void BuildPlayerPrefab()
        {
            var root = new GameObject("Player") { layer = BuildLayers.Player };
            var cc = root.AddComponent<CharacterController>();
            cc.height = 1.8f;
            cc.radius = 0.3f;
            cc.center = new Vector3(0f, 0.9f, 0f);
            cc.stepOffset = 0.35f;
            cc.slopeLimit = 50f;
            cc.skinWidth = 0.03f;

            var camGo = new GameObject("Camera") { tag = "MainCamera", layer = BuildLayers.Player };
            camGo.transform.SetParent(root.transform, false);
            camGo.transform.localPosition = new Vector3(0f, 1.7f, 0f);
            var cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 1500f;
            cam.fieldOfView = 72f;
            camGo.AddComponent<AudioListener>();
            var baseData = camGo.AddComponent<UniversalAdditionalCameraData>();
            baseData.renderPostProcessing = true;
            cam.cullingMask &= ~(1 << ToolViewModel.Layer);

            // The tool in your hand is drawn by an overlay camera on its own layer, so it never pokes into walls.
            var vmGo = new GameObject("ViewModelCamera") { layer = BuildLayers.Player };
            vmGo.transform.SetParent(camGo.transform, false);
            var vmCam = vmGo.AddComponent<Camera>();
            vmCam.cullingMask = 1 << ToolViewModel.Layer;
            vmCam.nearClipPlane = 0.01f;
            vmCam.farClipPlane = 3f;
            // The hands keep their own, narrower view: their poses were set up at it, and a wide lens stretches them.
            vmCam.fieldOfView = 62f;
            var vmData = vmGo.AddComponent<UniversalAdditionalCameraData>();
            vmData.renderType = CameraRenderType.Overlay;
            vmData.renderShadows = false;
            // In a camera stack URP applies post-processing from the last camera, so the overlay must ask for it too.
            vmData.renderPostProcessing = true;
            baseData.cameraStack.Add(vmCam);
            var controller = CharacterGenerator.Generate();
            var viewModel = vmGo.AddComponent<ToolViewModel>();
            viewModel.Set(AssetDatabase.LoadAssetAtPath<GameObject>(ToolModelGenerator.NailGunPrefabPath),
                AssetDatabase.LoadAssetAtPath<GameObject>(ToolModelGenerator.SprayCanPrefabPath),
                AssetDatabase.LoadAssetAtPath<GameObject>(ToolModelGenerator.AxePrefabPath));

            var fpc = root.AddComponent<FirstPersonController>();
            Set(fpc, "cameraRoot", camGo.transform);

            // Footsteps, jump and landing: the player's own, so not positional.
            var audio = root.AddComponent<AudioSource>();
            audio.playOnAwake = false;
            audio.spatialBlend = 0f;
            root.AddComponent<SettingsMenu>();
            var sounds = root.AddComponent<PlayerSounds>();
            // Landing is a footstep come down hard: the landing recordings had a grunt in them (Sam: remove the "ugh").
            sounds.Set(fpc, Clips("Footstep_Grass"), Clips("Footstep_Wood"), Clips("Jump"),
                Clips("Footstep_Grass"), Clips("Footstep_Wood"), BuildLayers.PieceMask);
            sounds.SetPunches(Clips("Punch_Whoosh"), Clips("Punch_Hit_Wood"));
            sounds.SetTools(Clips("NailGun_Shot"), Clips("Saw_Cut"));

            // The lumberjack, if he's been generated: you see his body (not his head) in first person.
            if (controller != null && CharacterGenerator.BodyPrefab != null)
            {
                var model = (GameObject)PrefabUtility.InstantiatePrefab(CharacterGenerator.BodyPrefab);
                model.name = "Body";
                model.transform.SetParent(root.transform, false);
                model.transform.localScale = Vector3.one * CharacterGenerator.Scale;
                foreach (var t in model.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = BuildLayers.Player;
                if (!model.TryGetComponent<Animator>(out var animator)) animator = model.AddComponent<Animator>();
                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                var playerBody = model.AddComponent<PlayerBody>();
                var renderers = model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                SkinnedMeshRenderer Mesh(string n) => System.Array.Find(renderers, r => r.name == n);
                var bones = Mesh(CharacterMeshes.FullName).bones;
                Transform Bone(string n) => System.Array.Find(bones, b => b.name == n);
                playerBody.Set(fpc, animator, Mesh(CharacterMeshes.FullName), Mesh(CharacterMeshes.TorsoName), Mesh(CharacterMeshes.ArmName),
                    Mesh(CharacterMeshes.LeftArmName), Bone("Head"), Bone("Hips"), Bone("Spine02"));
                // Where a handle sits in each closed hand: worked out from the fingers as the grip bends them.
                CharacterMeshes.GripFrame(false, out var rightCentre, out var rightAxis);
                CharacterMeshes.GripFrame(true, out var leftCentre, out var leftAxis);
                playerBody.SetGrips(rightCentre, rightAxis, leftCentre, leftAxis);
                var top = CharacterGenerator.TopSpeeds;
                playerBody.SetStrides(top.x, top.y, top.z);
            }
            PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
            Object.DestroyImmediate(root);
        }

        /// <summary>The numbered sounds in Audio whose names start with <paramref name="prefix"/> (Footstep_Wood_1, _2…).</summary>
        public static AudioClip[] Clips(string prefix) =>
            AssetDatabase.FindAssets(prefix + "_ t:AudioClip", new[] { AudioDir })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileNameWithoutExtension(p), "^" + prefix + @"_\d+$"))
                .OrderBy(p => p)
                .Select(AssetDatabase.LoadAssetAtPath<AudioClip>)
                .ToArray();

        static void BuildSystemPrefab(Material lumber, Material plywood, Material concrete, Material clamp, Material steel, Material paint, Material mist, GameObject nailHead, GameObject nailShot, Material valid, Material invalid, Material blade)
        {
            var root = new GameObject("BuildSystem");
            var site = root.AddComponent<BuildSite>();
            var carry = root.AddComponent<CarryController>();
            var ghost = root.AddComponent<GhostPreview>();
            var can = root.AddComponent<SprayCan>();
            var belt = root.AddComponent<BuildToolbelt>();
            var hud = root.AddComponent<BuildHud>();

            Set(site, "lumberMaterial", lumber);
            Set(site, "plywoodMaterial", plywood);
            Set(site, "concreteMaterial", concrete);
            Set(site, "clampMaterial", clamp);
            Set(site, "steelMaterial", steel);
            Set(site, "paintMaterial", paint);
            Set(can, "site", site);
            Set(can, "mistMaterial", mist);
            Set(site, "nailHeadPrefab", nailHead);
            Set(site, "nailShotPrefab", nailShot);
            Set(carry, "site", site);
            Set(ghost, "validMaterial", valid);
            Set(ghost, "invalidMaterial", invalid);
            Set(ghost, "bladeMaterial", blade);
            Set(ghost, "cubeMesh", Resources.GetBuiltinResource<Mesh>("Cube.fbx"));
            Set(belt, "site", site);
            Set(belt, "carry", carry);
            Set(belt, "ghost", ghost);
            Set(belt, "can", can);
            Set(belt, "nailGunIcon", AssetDatabase.LoadAssetAtPath<Texture2D>(IconGenerator.NailGunPath));
            Set(belt, "sawIcon", AssetDatabase.LoadAssetAtPath<Texture2D>(IconGenerator.SawPath));
            Set(belt, "sprayIcon", AssetDatabase.LoadAssetAtPath<Texture2D>(IconGenerator.SprayCanPath));
            Set(belt, "axeIcon", AssetDatabase.LoadAssetAtPath<Texture2D>(IconGenerator.AxePath));
            Set(hud, "toolbelt", belt);

            PrefabUtility.SaveAsPrefabAsset(root, BuildSystemPrefabPath);
            Object.DestroyImmediate(root);
        }

        static void Set(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            so.FindProperty(field).objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ------------------------------------------------------------------ project plumbing

        public static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        static void EnsureLayer(int index, string name)
        {
            var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var layer = tagManager.FindProperty("layers").GetArrayElementAtIndex(index);
            if (layer.stringValue == name) return;
            if (!string.IsNullOrEmpty(layer.stringValue))
                throw new System.InvalidOperationException($"Layer {index} is already '{layer.stringValue}'");
            layer.stringValue = name;
            tagManager.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
