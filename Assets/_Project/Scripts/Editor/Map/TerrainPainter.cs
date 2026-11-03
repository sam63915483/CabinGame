using Cabin.Map;
using UnityEngine;

namespace Cabin.Editor.Map
{
    /// <summary>
    /// Ground textures by rule (<see cref="TerrainTextures"/>). Gentle ground is turf: lush grass and dry meadow in
    /// fields and patches, and forest floor with moss where the forest stands (the same patches
    /// <see cref="VegetationScatter"/> plants). From <see cref="MapRecipe.dirtSlope"/> the turf dries to heath with
    /// bedrock showing through in patches, and from <see cref="MapRecipe.rockSlope"/> it is rock with turf holding on
    /// in pockets — never a band of bare earth following the contour. High ground is barrens: rock in patches. Bare
    /// earth comes only in small patches. Sea shores are sand; lake and river edges a thin band of gravel over dirt,
    /// so the water's outline reads. The shader's height blend does the rest at every edge.
    /// </summary>
    public static class TerrainPainter
    {
        public const int Layers = 8;
        public const int Grass = 0, ForestFloor = 1, Dirt = 2, Rock = 3, Sand = 4, Meadow = 5, Moss = 6, Gravel = 7;
        /// <summary>How far above a lake or river the stony beach goes (metres): wide enough to blend smoothly across the 2 m paint cells.</summary>
        const float FreshShore = 1.3f;

        /// <summary>
        /// How forested a point is (0–1): the same patches the trees are planted by, broken by clearings at a larger
        /// scale (about a fifth of the woods opens into meadow in 300–500 m sweeps), so a hillside view reads as
        /// woods and fields rather than an even sprinkle.
        /// </summary>
        public static float Forest(Noise patches, float wx, float wz)
        {
            float patch = patches.Fbm(wx / 160f, wz / 160f, 3) * 0.5f + 0.5f;
            float forest = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.35f, 0.65f, patch));
            float open = patches.Fbm(wx / 420f + 31.7f, wz / 420f - 17.3f, 2) * 0.5f + 0.5f;
            return forest * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.3f, 0.42f, open));
        }

        /// <summary>How much a point lies in a kept clearing (1 inside <paramref name="radius"/>, fading to 0 over the <paramref name="fringe"/> beyond it): the forest paint gives way to open turf there, as the trees do.</summary>
        public static float Clearing(float distance, float radius, float fringe = 15f)
        {
            return 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(radius, radius + fringe, distance));
        }

        /// <summary>Layer weights at a point (sum 1). <paramref name="clearing"/>: how much the point lies in a kept clearing (<see cref="Clearing"/>), where the woods' floor gives way to turf.</summary>
        public static void Weights(MapRecipe r, Noise noise, float height, float slope, float waterLevel, bool sea, float wx, float wz, float[] w, float clearing = 0f)
        {
            System.Array.Clear(w, 0, Layers);
            float aboveWater = height - waterLevel;
            float shore = sea ? r.beachHeight : FreshShore;
            if (aboveWater < shore)
            {
                // Shore and bed: sand by the sea; gravel at a lake's or river's edge, going to dirt under the water.
                int edge = sea ? Sand : Gravel, bed = sea ? Sand : Dirt;
                float under = Mathf.InverseLerp(0f, -1.2f, aboveWater);
                w[edge] = 1f - under;
                w[bed] += under;
                if (aboveWater > 0f)
                {
                    float t = Mathf.SmoothStep(0f, 1f, aboveWater / shore);
                    for (int k = 0; k < Layers; k++) w[k] *= 1f - t * 0.85f;
                    w[Grass] += t * 0.85f;
                }
                return;
            }
            float forest = Forest(noise, wx, wz) * (1f - clearing);

            // Slope, wobbled a few degrees by a small noise so the bands never follow the contour lines.
            float s = slope + noise.Fbm(wx / 18f + 7.7f, wz / 18f - 3.1f, 2) * 4f;
            float steep = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(r.dirtSlope, r.rockSlope, s));
            float cliff = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(r.rockSlope, r.rockSlope + 10f, s));
            // Bedrock through the heath on the steep band, in patches; everything above rockSlope.
            float outcrop = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.45f, 0.7f, noise.Fbm(wx / 32f + 21.3f, wz / 32f - 5.5f, 3) * 0.5f + 0.5f));
            float rock = Mathf.Max(cliff, steep * outcrop * 0.85f);
            // Barrens: high ground shows its rock in patches, not a grey cap.
            float patch = noise.Fbm(wx / 70f + 13.1f, wz / 70f - 4.7f, 3) * 0.5f + 0.5f;
            rock = Mathf.Max(rock, Mathf.InverseLerp(r.rockFrom, r.rockFull + 40f, height) * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.5f, 0.68f, patch)) * 0.85f);
            // Bare earth: small patches, a little commoner on the steep band, and round the roots in the woods.
            float bare = noise.Fbm(wx / 25f + 3.3f, wz / 25f + 8.8f, 2) * 0.5f + 0.5f;
            float dirt = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.62f, 0.75f, bare)) * (0.35f + 0.3f * steep) * (1f - rock);
            w[Rock] = rock;
            w[Dirt] = dirt;
            float soil = Mathf.Max(0f, 1f - rock - dirt);

            // In the woods: forest floor, mossy in damp hollows and patches. The floor reaches a little past the
            // trees' own density curve (forest^0.7) so the ground under a thinning wood is still shaded litter, which
            // is what makes a far hillside of trees read as forest rather than trees on a lawn.
            float moss = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 0.72f, noise.Fbm(wx / 45f - 6.1f, wz / 45f + 2.4f, 3) * 0.5f + 0.5f));
            float floor = Mathf.Pow(forest, 0.7f);
            w[ForestFloor] = soil * floor * (1f - moss * 0.8f);
            w[Moss] = soil * floor * moss * 0.8f;
            // In the open: lush grass and dry meadow in fields, broken up at two sizes; on the steep band the turf
            // dries to heath.
            float dry = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.45f, 0.65f, noise.Fbm(wx / 300f + 1.7f, wz / 300f - 9.2f, 3) * 0.5f + 0.5f
                + 0.25f * noise.Fbm(wx / 40f, wz / 40f, 2)));
            dry = Mathf.Max(dry, steep * 0.9f);
            w[Meadow] = soil * (1f - floor) * dry;
            w[Grass] = soil * (1f - floor) * (1f - dry);
        }
    }
}
