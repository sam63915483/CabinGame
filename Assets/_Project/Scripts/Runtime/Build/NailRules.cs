namespace Cabin.Build
{
    /// <summary>What makes a nail hold: it must pass through the first piece and bite into the second.</summary>
    public static class NailRules
    {
        /// <summary>A 3.5 in framing nail, scaled with the lumber so it still goes through a 2×4 into a 4×4.</summary>
        public const float NailLength = 3.5f * Units.Inch * Stock.CrossSectionScale;
        /// <summary>The nail must reach at least this far into the second piece.</summary>
        public const float MinBite = 0.5f * Units.Inch * Stock.CrossSectionScale;
        /// <summary>Gaps up to this are nailed as they are.</summary>
        public const float MaxGap = 0.25f * Units.Inch * Stock.CrossSectionScale;
        /// <summary>Gaps up to this are closed by drawing the pieces together as the nail goes in.</summary>
        public const float PullGap = 2f * Units.Inch * Stock.CrossSectionScale;

        /// <summary>
        /// Distances are measured from the nail's entry point along its line. <paramref name="pull"/> is how far the
        /// pieces must be drawn together first (0 when they already touch). Returns null when the nail holds,
        /// otherwise the reason it doesn't.
        /// </summary>
        public static string Check(float exitFirst, float enterSecond, out float bite, out float pull)
        {
            pull = 0f;
            bite = 0f;
            if (exitFirst < 0f || exitFirst > NailLength) return "Nail doesn't go through — too thick here";
            float gap = enterSecond - exitFirst;
            if (gap > PullGap) return $"Gap of {Units.FeetInches(Stock.Nominal(gap))} — too far to pull together";
            if (gap > MaxGap) pull = gap;
            bite = NailLength - (enterSecond - pull);
            if (bite < MinBite - 1e-5f) return "Nail barely reaches the second piece";
            return null;
        }
    }
}
