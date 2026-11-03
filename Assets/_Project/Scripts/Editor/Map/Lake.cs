using System.Collections.Generic;

namespace Cabin.Editor.Map
{
    /// <summary>A lake on the coarse grid: a basin the drainage filled, with the level it fills to.</summary>
    public sealed class Lake
    {
        public int Id;
        /// <summary>The water surface (metres): the basin's spill height.</summary>
        public float Level;
        /// <summary>Coarse cells under water.</summary>
        public readonly List<int> Cells = new List<int>();
        /// <summary>The first cell outside the lake that its water drains to (−1 if it drains straight off the grid).</summary>
        public int Outlet = -1;
        /// <summary>Its width if it were round (metres).</summary>
        public float WidthMetres;
        /// <summary>How far it stretches at its longest (metres, along x or z).</summary>
        public float LengthMetres;
    }
}
