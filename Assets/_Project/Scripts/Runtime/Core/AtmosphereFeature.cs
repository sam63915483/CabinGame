using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

namespace Cabin.Core
{
    /// <summary>
    /// The atmosphere's render passes (the <c>Cabin/Atmosphere</c> material, on the project's renderer; set up by
    /// WorldLook, driven by <see cref="Atmosphere"/>). Before post-processing: the camera's colour is copied; when the
    /// sun is on screen and the shafts are on, a mask of open sky near the sun is drawn at half resolution and
    /// smeared toward the sun by <see cref="BlurIterations"/> radial blurs of <see cref="BlurTaps"/> taps each, the
    /// step growing <see cref="BlurTaps"/>-fold each time so they compose into <see cref="TotalTaps"/> taps along the
    /// ray; then the atmosphere pass writes the colour back with the haze, clouds, glow and shafts added.
    /// </summary>
    public sealed class AtmosphereFeature : ScriptableRendererFeature
    {
        public const int BlurIterations = 3, BlurTaps = 8;
        /// <summary>How many taps the composed blur amounts to along each ray.</summary>
        public static readonly int TotalTaps = (int)Mathf.Pow(BlurTaps, BlurIterations);

        public Material material;

        AtmospherePass pass;

        public override void Create()
        {
            pass = new AtmospherePass(name) { renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (material == null || pass == null) return;
            var cam = renderingData.cameraData;
            if (cam.cameraType == CameraType.Preview || cam.cameraType == CameraType.Reflection) return;
            if (cam.targetTexture != null && cam.targetTexture.format == RenderTextureFormat.Depth) return;
            pass.ConfigureInput(ScriptableRenderPassInput.Depth);
            pass.requiresIntermediateTexture = true;
            pass.Material = material;
            renderer.EnqueuePass(pass);
        }

        /// <summary>
        /// The blur's parameters for one iteration: x the step per tap as a fraction of the way to the sun, z the
        /// weight kept per tap, so that over all the iterations the taps span <paramref name="length"/> of the way and
        /// the far end weighs <paramref name="endWeight"/> of the near end.
        /// </summary>
        public static Vector4 BlurStep(int iteration, float length, float endWeight)
        {
            float span = Mathf.Pow(BlurTaps, iteration); // one tap here covers this many of the composed taps
            return new Vector4(length / TotalTaps * span, 0f, Mathf.Pow(Mathf.Max(endWeight, 1e-4f), span / TotalTaps), 0f);
        }

        sealed class AtmospherePass : ScriptableRenderPass
        {
            public Material Material;

            static readonly MaterialPropertyBlock Block = new MaterialPropertyBlock();
            static readonly int BlitTextureId = Shader.PropertyToID("_BlitTexture"), BlitScaleBiasId = Shader.PropertyToID("_BlitScaleBias"),
                ShaftTexId = Shader.PropertyToID("_ShaftTex"), ShaftStepId = Shader.PropertyToID("_ShaftStep"),
                TogglesId = Shader.PropertyToID("_Toggles"), SunScreenId = Shader.PropertyToID("_SunScreen"),
                IntensityId = Shader.PropertyToID("_ShaftIntensity"), LengthId = Shader.PropertyToID("_ShaftLength"), DecayId = Shader.PropertyToID("_ShaftDecay");
            const int CompositePass = 0, MaskPass = 1, BlurPass = 2;

            class PassData
            {
                public Material Material;
                public int Pass;
                public TextureHandle Source, Shafts;
                public Vector4 Step;
            }

            public AtmospherePass(string name)
            {
                profilingSampler = new ProfilingSampler(name);
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var resources = frameData.Get<UniversalResourceData>();
                var cameraData = frameData.Get<UniversalCameraData>();
                if (!resources.cameraColor.IsValid()) return;

                // The colour so far, copied so the last pass can read it while writing the camera's colour.
                var colourDesc = renderGraph.GetTextureDesc(resources.cameraColor);
                colourDesc.name = "_AtmosphereColor";
                colourDesc.clearBuffer = false;
                var colour = renderGraph.CreateTexture(colourDesc);
                renderGraph.AddBlitPass(resources.activeColorTexture, colour, Vector2.one, Vector2.zero, passName: "Atmosphere copy");

                var depth = resources.cameraDepthTexture;
                var shafts = TextureHandle.nullHandle;
                // The toggles and the sun's place come from Atmosphere each frame in Play; without them (the editor) there are no shafts.
                bool wanted = Material.HasVector(TogglesId) && Material.HasVector(SunScreenId)
                    && Material.GetVector(TogglesId).x > 0.5f && Material.GetVector(SunScreenId).z > 0.001f && Material.GetFloat(IntensityId) > 0f;
                if (wanted && depth.IsValid())
                {
                    var desc = cameraData.cameraTargetDescriptor;
                    desc.width = Mathf.Max(1, desc.width / 2);
                    desc.height = Mathf.Max(1, desc.height / 2);
                    desc.graphicsFormat = GraphicsFormat.R8_UNorm;
                    desc.depthStencilFormat = GraphicsFormat.None;
                    desc.msaaSamples = 1;
                    desc.useMipMap = false;
                    var a = UniversalRenderer.CreateRenderGraphTexture(renderGraph, desc, "_ShaftMaskA", false, FilterMode.Bilinear);
                    var b = UniversalRenderer.CreateRenderGraphTexture(renderGraph, desc, "_ShaftMaskB", false, FilterMode.Bilinear);
                    Add(renderGraph, "Shaft mask", MaskPass, TextureHandle.nullHandle, TextureHandle.nullHandle, depth, a, Vector4.zero);
                    float length = Material.GetFloat(LengthId), decay = Material.GetFloat(DecayId);
                    for (int k = 0; k < BlurIterations; k++)
                    {
                        bool even = (k & 1) == 0;
                        Add(renderGraph, "Shaft blur", BlurPass, even ? a : b, TextureHandle.nullHandle, TextureHandle.nullHandle, even ? b : a, BlurStep(k, length, decay));
                    }
                    shafts = (BlurIterations & 1) == 1 ? b : a;
                }
                Add(renderGraph, "Atmosphere", CompositePass, colour, shafts, depth, resources.activeColorTexture, Vector4.zero);
            }

            void Add(RenderGraph renderGraph, string name, int pass, TextureHandle source, TextureHandle shafts, TextureHandle depth, TextureHandle target, Vector4 step)
            {
                using var builder = renderGraph.AddRasterRenderPass<PassData>(name, out var data, profilingSampler);
                data.Material = Material;
                data.Pass = pass;
                data.Source = source;
                data.Shafts = shafts;
                data.Step = step;
                if (source.IsValid()) builder.UseTexture(source);
                if (shafts.IsValid()) builder.UseTexture(shafts);
                if (depth.IsValid()) builder.UseTexture(depth);
                builder.SetRenderAttachment(target, 0, AccessFlags.Write);
                builder.SetRenderFunc((PassData d, RasterGraphContext context) => Execute(d, context));
            }

            static void Execute(PassData d, RasterGraphContext context)
            {
                Block.Clear();
                if (d.Source.IsValid())
                {
                    RTHandle source = d.Source;
                    Block.SetTexture(BlitTextureId, source);
                }
                if (d.Shafts.IsValid())
                {
                    RTHandle shafts = d.Shafts;
                    Block.SetTexture(ShaftTexId, shafts);
                }
                else Block.SetTexture(ShaftTexId, Texture2D.blackTexture);
                Block.SetVector(BlitScaleBiasId, new Vector4(1f, 1f, 0f, 0f));
                Block.SetVector(ShaftStepId, d.Step);
                context.cmd.DrawProcedural(Matrix4x4.identity, d.Material, d.Pass, MeshTopology.Triangles, 3, 1, Block);
            }
        }
    }
}
