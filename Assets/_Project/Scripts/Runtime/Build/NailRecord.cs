using System;
using UnityEngine;

namespace Cabin.Build
{
    /// <summary>A nail driven through piece <see cref="a"/> into piece <see cref="b"/>, stored in each piece's local frame.</summary>
    [Serializable]
    public sealed class NailRecord
    {
        public int id;
        public int a;
        public int b;
        /// <summary>Entry point on A's surface and the nail's direction, in A's local frame.</summary>
        public Vector3 entryA;
        public Vector3 directionA;
        /// <summary>Entry point on B's surface in B's local frame: decides which half keeps the nail when B is cut.</summary>
        public Vector3 entryB;
    }
}
