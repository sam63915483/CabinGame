using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Cabin.Tests
{
    /// <summary>
    /// Guards the M0 project configuration (handoff §6) so a stray settings change shows up as a red test.
    /// </summary>
    public class ProjectSetupTests
    {
        static readonly string[] TierNames = { "Low", "Medium", "High" };

        [Test]
        public void DefaultPipeline_IsUrpHigh()
        {
            var urp = GraphicsSettings.defaultRenderPipeline as UniversalRenderPipelineAsset;
            Assert.IsNotNull(urp, "Default render pipeline must be URP");
            Assert.AreEqual("URP_High", urp.name);
        }

        [Test]
        public void QualityLevels_AreLowMediumHigh_EachWithItsOwnUrpAsset()
        {
            CollectionAssert.AreEqual(TierNames, QualitySettings.names);
            for (int i = 0; i < TierNames.Length; i++)
            {
                var urp = QualitySettings.GetRenderPipelineAssetAt(i) as UniversalRenderPipelineAsset;
                Assert.IsNotNull(urp, $"{TierNames[i]} has no URP asset");
                Assert.AreEqual("URP_" + TierNames[i], urp.name);
            }
        }

        [Test]
        public void EveryTier_UsesForwardPlus_AndGpuResidentDrawerWithOcclusion()
        {
            for (int i = 0; i < TierNames.Length; i++)
            {
                var urp = (UniversalRenderPipelineAsset)QualitySettings.GetRenderPipelineAssetAt(i);
                Assert.AreEqual(GPUResidentDrawerMode.InstancedDrawing, urp.gpuResidentDrawerMode, urp.name);
                Assert.IsTrue(urp.gpuResidentDrawerEnableOcclusionCullingInCameras, urp.name);

                var renderer = new SerializedObject(urp.rendererDataList[0]);
                Assert.AreEqual((int)RenderingMode.ForwardPlus, renderer.FindProperty("m_RenderingMode").intValue, urp.name);
            }
        }

        [Test]
        public void BatchRendererGroupVariants_AreKeptForGpuResidentDrawer()
        {
            Assert.AreEqual(UnityEditor.Rendering.BatchRendererGroupStrippingMode.KeepAll,
                UnityEditor.Rendering.EditorGraphicsSettings.batchRendererGroupShaderStrippingMode);
        }

        [Test]
        public void HighTier_Shadows_120m_TwoCascades_Soft()
        {
            var high = (UniversalRenderPipelineAsset)QualitySettings.GetRenderPipelineAssetAt(2);
            Assert.AreEqual(120f, high.shadowDistance);
            Assert.AreEqual(2, high.shadowCascadeCount, "One cascade boundary (36 m) instead of two: fewer visible shadow changes walking up to a tree");
            Assert.IsTrue(high.supportsSoftShadows);
        }

        [Test]
        public void BuildScenes_AreBootWorldTestPlatform_InOrder()
        {
            var names = EditorBuildSettings.scenes
                .Select(s => System.IO.Path.GetFileNameWithoutExtension(s.path))
                .ToArray();
            CollectionAssert.AreEqual(new[] { "Boot", "World", "TestPlatform" }, names);
        }

        [Test]
        public void Serialization_IsForceText_WithVisibleMetaFiles()
        {
            Assert.AreEqual(SerializationMode.ForceText, EditorSettings.serializationMode);
            Assert.AreEqual("Visible Meta Files", VersionControlSettings.mode);
        }

        /// <summary>
        /// Play Mode starts from a clean reload of scripts and scene. With Unity 6's fast path (no reload) the editor
        /// crashed in a render job entering Play right after the generators rebuilt meshes in place.
        /// </summary>
        [Test]
        public void EnteringPlayMode_ReloadsScriptsAndScene()
        {
            Assert.IsFalse(EditorSettings.enterPlayModeOptionsEnabled);
        }
    }
}
