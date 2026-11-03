using UnityEngine;

namespace Cabin.Build
{
    /// <summary>
    /// Dimensions of each stock kind. A piece's local frame is the stock's frame: x along the length,
    /// y across the wide face (height when lying flat), z through the thickness. Deck blocks stand on -y.
    /// </summary>
    public static class Stock
    {
        const float In = Units.Inch;

        /// <summary>
        /// Cross-sections are exaggerated by this much (lengths stay real) because true-to-life lumber reads as
        /// matchsticks in first person. 2× after playtest 1, 3× after playtest 2 (Sam, 2026-10-29). Nails and
        /// deck blocks follow it.
        /// </summary>
        public const float CrossSectionScale = 3f;

        /// <summary>
        /// Lengths are stretched by this much too: an "8 ft" board is 10 ft of world, because at eye height 8 ft
        /// read as 6 (Sam, playtest 3). Racks and the HUD show nominal lengths — see <see cref="Nominal"/>.
        /// </summary>
        public const float LengthScale = 1.25f;

        /// <summary>World metres → the nominal length the player sees ("8 ft" for an 8 ft board).</summary>
        public static float Nominal(float worldMetres) => worldMetres / LengthScale;

        /// <summary>Spruce-pine-fir and plywood both sit near this.</summary>
        public const float WoodDensity = 480f;
        public const float ConcreteDensity = 2300f;

        /// <summary>Lumber lengths the racks offer, 8 ft to 20 ft (handoff v2).</summary>
        public static readonly float[] LumberLengths = { 8f * Units.Foot, 10f * Units.Foot, 12f * Units.Foot, 14f * Units.Foot, 16f * Units.Foot, 20f * Units.Foot };

        /// <summary>Plywood sheet lengths the racks offer (all 4 ft wide): 4×8, 4×10, 4×12.</summary>
        public static readonly float[] PlywoodLengths = { 8f * Units.Foot, 10f * Units.Foot, 12f * Units.Foot };

        /// <summary>Full-size box of a stock piece: (length, width, thickness). Deck block: (base, height, base).</summary>
        public static Vector3 Size(StockKind kind, float length)
        {
            float s = CrossSectionScale, l = LengthScale;
            return kind switch
            {
                StockKind.TwoByFour => new Vector3(length * l, 3.5f * In * s, 1.5f * In * s),
                StockKind.FourByFour => new Vector3(length * l, 3.5f * In * s, 3.5f * In * s),
                StockKind.Plywood => new Vector3((length > 0f ? length : 8f * Units.Foot) * l, 4f * Units.Foot * l, 0.5f * In * s),
                StockKind.DeckBlock => new Vector3(DeckBlockBase, DeckBlockHeight, DeckBlockBase),
                _ => Vector3.one,
            };
        }

        // Deck block: a truncated pyramid like the real 11 in × 7.5 in piers, with a 4×4 socket on top.
        public static float DeckBlockBase => 11f * In * CrossSectionScale;
        public static float DeckBlockTop => 8f * In * CrossSectionScale;
        public static float DeckBlockHeight => 7.5f * In * CrossSectionScale;
        /// <summary>How far a post seats down into the block's socket.</summary>
        public static float DeckBlockSocketDepth => 2f * In * CrossSectionScale;
        /// <summary>How far below the lowest ground point under it a placed block sinks, so no corner floats.</summary>
        public static float DeckBlockSink => 0.75f * In * CrossSectionScale;

        public static float Density(StockKind kind) => kind == StockKind.DeckBlock ? ConcreteDensity : WoodDensity;

        public static string Name(StockKind kind) => kind switch
        {
            StockKind.TwoByFour => "2×4",
            StockKind.FourByFour => "4×4",
            StockKind.Plywood => "Plywood",
            StockKind.DeckBlock => "Deck block",
            _ => kind.ToString(),
        };

        /// <summary>Only a 4×4 seats in a deck block as a post (handoff v2 + playtest 3: no planting in bare ground).</summary>
        public static bool CanBePost(StockKind kind) => kind == StockKind.FourByFour;

        public static bool IsConcrete(StockKind kind) => kind == StockKind.DeckBlock;

        /// <summary>The shape a fresh piece of this stock has.</summary>
        public static ConvexSolid Shape(StockKind kind, float length) =>
            kind == StockKind.DeckBlock
                ? ConvexSolid.Frustum(DeckBlockBase, DeckBlockTop, DeckBlockHeight)
                : ConvexSolid.Box(Size(kind, length));
    }
}
