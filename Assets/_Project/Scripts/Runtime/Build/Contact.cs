using UnityEngine;

namespace Cabin.Build
{
    /// <summary>Where a piece touches another: the touching patch, its middle, and the normal pointing from the other piece into this one.</summary>
    public struct Contact
    {
        public Piece Other;
        /// <summary>The middle (area centroid) of the touching patch.</summary>
        public Vector3 Point;
        public Vector3 Normal;
        /// <summary>
        /// The touching patch: where the two faces overlap, as a convex polygon in the contact plane (world points).
        /// A small square round <see cref="Point"/> when they only meet along an edge.
        /// </summary>
        public Vector3[] Patch;
    }
}
