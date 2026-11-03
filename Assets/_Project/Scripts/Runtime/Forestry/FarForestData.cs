using UnityEngine;

namespace Cabin.Forestry
{
    /// <summary>
    /// The far forest's impostors: every tree species photographed from <see cref="Views"/> directions into one
    /// atlas, and each species' size, so a quad the size of the tree can wear the right picture for the way it is
    /// seen. Generated into Assets/Generated/Trees by the map generator.
    /// </summary>
    public sealed class FarForestData : ScriptableObject
    {
        public Texture2D Atlas;
        [Tooltip("Pictures taken round each tree.")]
        public int Views = 8;
        [Tooltip("Cells across and down the atlas.")]
        public int Columns = 16, Rows = 8;
        [Tooltip("Per species: the tree's width and height at scale 1 (metres).")]
        public float[] Widths, Heights;

        public int Species => Widths != null ? Widths.Length : 0;

        /// <summary>Where a species' view sits in the atlas (0–1 UV rect, row 0 at the top of the image).</summary>
        public static Rect Cell(int species, int view, int views, int columns, int rows)
        {
            int index = species * views + view;
            int col = index % columns, row = index / columns;
            return new Rect(col / (float)columns, 1f - (row + 1) / (float)rows, 1f / columns, 1f / rows);
        }

        public Rect Cell(int species, int view) => Cell(species, view, Views, Columns, Rows);
    }
}
