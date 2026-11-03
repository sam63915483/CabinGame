using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Cabin.Bench
{
    /// <summary>
    /// The bench's experiments that aren't menu settings: pipeline copies, HDR, the overlay camera, the water, the
    /// terrain's shadows, base map distance, pixel error and LOD bias. Captures how the scene was built, applies a
    /// combo's overrides on top of that, and puts everything back for the next combo.
    /// </summary>
    public sealed class BenchExperiments
    {
        bool depth, opaque, hdr;
        GameObject viewModel, water;
        Terrain[] terrains;
        ShadowCastingMode[] terrainShadows;
        float basemap, pixelError, lodBias;
        float grassNear, grassStart, grassFloor;

        public void Capture()
        {
            if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
            {
                depth = urp.supportsCameraDepthTexture;
                opaque = urp.supportsCameraOpaqueTexture;
                hdr = urp.supportsHDR;
            }
            viewModel = GameObject.Find("ViewModelCamera");
            water = GameObject.Find("Map/Water");
            terrains = Terrain.activeTerrains;
            terrainShadows = new ShadowCastingMode[terrains.Length];
            for (int i = 0; i < terrains.Length; i++) terrainShadows[i] = terrains[i].shadowCastingMode;
            basemap = terrains.Length > 0 ? terrains[0].basemapDistance : 600f;
            pixelError = terrains.Length > 0 ? terrains[0].heightmapPixelError : 4f;
            lodBias = QualitySettings.lodBias;
            grassNear = Forestry.GrassField.NearMesh;
            grassStart = Forestry.GrassField.FadeStart;
            grassFloor = Forestry.GrassField.FadeFloor;
        }

        /// <summary>Back to how the scene was built, then this combo's overrides. Call after the quality tier is applied (it swaps the URP asset).</summary>
        public void Apply(BenchCombo c)
        {
            if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
            {
                urp.supportsCameraDepthTexture = c.depthTexture < 0 ? depth : c.depthTexture != 0;
                urp.supportsCameraOpaqueTexture = c.opaqueTexture < 0 ? opaque : c.opaqueTexture != 0;
                urp.supportsHDR = c.hdr < 0 ? hdr : c.hdr != 0;
                // Over whatever RenderTier just applied for the combo's shadow level.
                if (c.shadowDistance >= 0f) urp.shadowDistance = c.shadowDistance;
                if (c.shadowCascades > 0) urp.shadowCascadeCount = c.shadowCascades;
                if (c.shadowResolution > 0) urp.mainLightShadowmapResolution = c.shadowResolution;
            }
            Forestry.GrassField.NearMesh = c.grassNearMesh < 0f ? grassNear : c.grassNearMesh;
            Forestry.GrassField.FadeStart = c.grassFadeStart < 0f ? grassStart : c.grassFadeStart;
            Forestry.GrassField.FadeFloor = c.grassFadeFloor < 0f ? grassFloor : c.grassFadeFloor;
            if (viewModel != null) viewModel.SetActive(c.viewModel != 0);
            if (water != null) water.SetActive(c.water != 0);
            for (int i = 0; i < terrains.Length; i++)
            {
                var t = terrains[i];
                if (t == null) continue;
                t.shadowCastingMode = c.terrainShadows < 0 ? terrainShadows[i] : c.terrainShadows != 0 ? ShadowCastingMode.On : ShadowCastingMode.Off;
                if (c.terrainTrees == 0) t.treeDistance = 0f; // GameSettings.ApplyWorld set the real distance just before
                t.basemapDistance = c.basemapDistance < 0f ? basemap : c.basemapDistance;
                t.heightmapPixelError = c.pixelError < 0f ? pixelError : c.pixelError;
            }
            QualitySettings.lodBias = c.lodBias < 0f ? lodBias : c.lodBias;
        }

        /// <summary>Everything as built: for the end of the run (the URP assets are project files in the editor).</summary>
        public void Restore() => Apply(new BenchCombo());
    }
}
