using System;

namespace Cabin.Bench
{
    /// <summary>A place the bench stands: world x/z (the ground height is looked up), the headings to face, the pitch, and whether every combo is measured here or only the first.</summary>
    [Serializable]
    public sealed class BenchStop
    {
        public string name = "spawn";
        public float x, z;
        public float[] yaws = { 0f };
        public float pitch;
        public bool sweep;
    }
}
