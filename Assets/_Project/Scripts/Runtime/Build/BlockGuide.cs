using UnityEngine;

namespace Cabin.Build
{
    /// <summary>A line from a set deck block to the one being placed, for lining posts up.</summary>
    public struct BlockGuide
    {
        public Vector3 From;
        public Vector3 To;
        /// <summary>Degrees off the nearest of the set block's square directions.</summary>
        public float Degrees;
        /// <summary>Centre-to-centre distance as a nominal length (what a board between them would be called).</summary>
        public float NominalDistance;

        public bool Aligned => Degrees < 0.25f;
    }
}
