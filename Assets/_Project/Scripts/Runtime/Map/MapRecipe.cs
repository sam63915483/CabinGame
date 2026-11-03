using UnityEngine;

namespace Cabin.Map
{
    /// <summary>
    /// Everything the world is generated from (Tools/Cabin/Map/Generate World). Nova Scotia's real coastline and broad
    /// shape, scaled down so the whole province fits the map; the land's relief laid on by region (rolling lowlands,
    /// ridged and terraced highlands) and then eroded by its own rivers; glacial lake basins; lakes, rivers and
    /// waterfalls wherever the drained land puts them; textured by rule. Change a value, delete the asset, regenerate.
    /// </summary>
    [CreateAssetMenu(menuName = "Cabin/Map Recipe", fileName = "MapRecipe")]
    public sealed class MapRecipe : ScriptableObject
    {
        [Header("Source: AWS Terrain Tiles (Terrarium PNG), open data")]
        [Tooltip("Slippy-map zoom of the elevation tiles: 10 is ~110 m per sample at Nova Scotia's latitude.")]
        public int zoom = 10;
        [Tooltip("Centre of the map, degrees.")]
        public double centreLongitude = -63.03, centreLatitude = 45.25;
        [Tooltip("Real metres per game metre (horizontal). 68 fits Nova Scotia, Brier Island to Scatarie, across 8 km with sea all round.")]
        public float realMetresPerMetre = 68f;
        [Tooltip("Real elevation (metres) below which it's sea: the source has noisy near-zero stripes offshore.")]
        public float landAbove = 3f;

        [Header("World")]
        [Tooltip("Terrain tiles across (x) and up (z).")]
        public int tilesX = 8, tilesZ = 7;
        public float tileSize = 1024f;
        [Tooltip("Heightmap samples per tile edge (2^n + 1).")]
        public int heightmapResolution = 513;
        [Tooltip("Splat samples per tile edge.")]
        public int alphamapResolution = 512;
        [Tooltip("The sea floor sits this far below sea level (y = 0).")]
        public float seaFloorDepth = 30f;
        [Tooltip("Terrain y range above sea level.")]
        public float maxHeight = 270f;
        [Tooltip("The coarse grid the geology (relief, erosion, drainage) runs on (metres per cell).")]
        public float coarseCell = 8f;

        [Header("Broad shape: game height = linear × real + quadratic × real², then relief")]
        public float linearScale = 0.1f;
        public float quadraticScale = 0.00028f;
        [Tooltip("Blur (game metres) on the source before exaggerating, so sensor noise doesn't become spikes.")]
        public float smoothing = 10f;
        [Tooltip("How far (game metres) the real elevation is averaged to tell high country from low.")]
        public float regionScale = 500f;
        [Tooltip("Real elevation (metres, averaged) that counts as fully high country: mountains there.")]
        public float regionHigh = 250f;

        [Header("Relief: the land's shape at walking scale, by region")]
        [Tooltip("How far (metres) the noise is pushed about so nothing lines up.")]
        public float warpAmount = 70f;
        [Tooltip("Lowland: broad rises and basins (± metres, ~900 m across) and hills (± metres, ~230 m across).")]
        public float lowBroad = 30f, lowHill = 13f;
        [Tooltip("Highland hills are this many times the lowland's.")]
        public float highHillGain = 1.6f;
        [Tooltip("Ridged rocky mountains in the high country (metres).")]
        public float mountainHeight = 80f;
        [Tooltip("Highland terraces: step height (metres), how strongly the land is pulled into steps (0–1), and how far the step edges wander (metres).")]
        public float terraceStep = 28f, terraceBlend = 0.5f, terraceWarp = 40f;
        [Tooltip("The hard rock band capping each terrace step (metres of height): it holds against erosion and makes cliffs and falls.")]
        public float hardBand = 10f;
        [Tooltip("Relief fades in over this many metres inland from the sea.")]
        public float shoreCalm = 90f;
        [Tooltip("How high the sea cliffs on rugged coasts rise (metres).")]
        public float seaCliffHeight = 22f;
        [Tooltip("Bedrock ribs (whalebacks of granite) showing through the lowland in patches: their height (metres).")]
        public float ribHeight = 7f;

        [Header("Erosion: rivers cut their valleys")]
        public int erosionIterations = 80;
        [Tooltip("How fast rivers cut (per iteration, per √cell of catchment, soft rock).")]
        public float erosionRate = 0.02f;
        [Tooltip("How fast soft slopes slump and round off (per iteration).")]
        public float diffusion = 0.12f;
        [Tooltip("The deepest a valley is cut below the relief, in the lowlands and in the highlands (metres).")]
        public float maxIncisionLow = 25f, maxIncisionHigh = 80f;

        [Header("Lake basins: glacial scours on gentle lowland")]
        [Tooltip("Ponds: blob size (metres), how rare (threshold on 0–1 noise: its median is 0.5, its 95th percentile 0.66), how deep (metres).")]
        public float pondScale = 250f, pondThreshold = 0.54f, pondDepth = 5f;
        [Tooltip("Big lakes: blob size, how rare, how deep.")]
        public float lakeScale = 1500f, lakeThreshold = 0.55f, lakeDepth = 12f;
        [Tooltip("Where the lake country is (map fraction from the centre, x east, z north) and how much fewer lakes there are far from it (0–1).")]
        public Vector2 lakeCountryCentre = new Vector2(-0.3f, -0.35f);
        public float lakeCountryBias = 0.6f;

        [Header("Rivers and falls")]
        [Tooltip("Country (game km²) that must drain through a point for a river to run there. The whole province is only ~12 km² at this scale.")]
        public float riverCatchmentKm2 = 0.03f;
        [Tooltip("Water this deep over the ground is a lake (metres); lakes smaller than this many coarse cells are filled in.")]
        public float lakeMinDepth = 0.25f;
        public int lakeMinCells = 20;
        [Tooltip("A river stretch steeper than this (rise per run) is falling water; a fall dropping at least this much (metres) is a waterfall, less a rapid.")]
        public float fallSlope = 0.35f, fallMinHeight = 2.5f;

        [Header("Fine ground: detail and shores")]
        [Tooltip("Rolling detail everywhere on land (metres), and its size (metres).")]
        public float detailHeight = 1.6f, detailScale = 45f;
        [Tooltip("Ridged rock detail on high ground (metres), fading in from rockFrom to rockFull metres up.")]
        public float rockDetailHeight = 5f, rockDetailScale = 70f, rockFrom = 50f, rockFull = 150f;
        [Tooltip("The beach: from every lake's waterline the land rises at least this steeply (degrees) for this far (metres), so the water meets the ground along a definite line.")]
        public float beachSlope = 9f, beachWidth = 10f;
        [Tooltip("How far a river's bank blends into the land (metres).")]
        public float bankWidth = 4f;
        [Tooltip("Lake depth: grows by this per metre from shore, up to the max.")]
        public float lakeDepthSlope = 0.12f, lakeMaxDepth = 7f;

        [Header("Rocks")]
        [Tooltip("Boulders scattered on the land (0 = none; Sam found the pack's rocks out of place, so none by default).")]
        public float rockDensity = 0f;

        [Header("Look")]
        public int seed = 46045;
        [Tooltip("Slope (degrees) where the turf dries to heath with rock showing through, and where rock takes over.")]
        public float dirtSlope = 30f, rockSlope = 44f;
        [Tooltip("Sand on shores up to this far above the water.")]
        public float beachHeight = 1.4f;

        public float WorldWidth => tilesX * tileSize;
        public float WorldDepth => tilesZ * tileSize;
        /// <summary>The world's south-west corner: the map is centred on the origin.</summary>
        public Vector3 Origin => new Vector3(-WorldWidth * 0.5f, -seaFloorDepth, -WorldDepth * 0.5f);
    }
}
