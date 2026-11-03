using UnityEngine;

namespace Cabin.Editor.Map
{
    /// <summary>A waterfall on a river: the lip it goes over and the base it lands at (world x/z and surface levels).</summary>
    public sealed class Fall
    {
        public Vector2 Lip, Base;
        public float LipLevel, BaseLevel;
        /// <summary>The river's width here (metres).</summary>
        public float Width;
        /// <summary>Index of the lip point and the base point in the river's course.</summary>
        public int LipIndex, BaseIndex;
        public float Height => LipLevel - BaseLevel;
    }
}
