using UnityEngine;

namespace Cabin.Forestry
{
    /// <summary>A tree the axe could chop: a terrain tree (by tile and instance index), or one already being chopped.</summary>
    public struct TreeTarget
    {
        public Terrain Terrain;
        public int Index;
        public ChoppingTree Chopping;
        public ChoppableTree Data;
        /// <summary>The base of the trunk (world).</summary>
        public Vector3 Base;
        public Quaternion Rotation;
        public Vector3 Scale;

        public static TreeTarget Of(ChoppingTree tree) => new TreeTarget
        {
            Chopping = tree,
            Data = tree.Data,
            Base = tree.transform.position,
            Rotation = tree.transform.rotation,
            Scale = tree.transform.lossyScale,
        };

        /// <summary>Trunk radius (world) at a world height.</summary>
        public float RadiusAt(float worldY) => Data.RadiusAt((worldY - Base.y) / Scale.y) * Scale.x;
    }
}
