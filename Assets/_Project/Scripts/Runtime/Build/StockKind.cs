namespace Cabin.Build
{
    /// <summary>The kinds of stock the racks hold (FreeBuild v2 handoff + deck blocks).</summary>
    public enum StockKind
    {
        TwoByFour,
        FourByFour,
        Plywood,
        /// <summary>Concrete pier with a square socket for a 4×4: the level, planted base a post stands in.</summary>
        DeckBlock,
    }
}
