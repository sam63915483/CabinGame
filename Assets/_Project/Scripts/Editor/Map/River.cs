using System.Collections.Generic;
using UnityEngine;

namespace Cabin.Editor.Map
{
    /// <summary>
    /// A river from its head to its mouth: a smoothed course (world x/z), the water surface along it (never rising
    /// downstream), its width and depth at each point (growing with the country draining through), and where it ends.
    /// Index 0 is the head.
    /// </summary>
    public sealed class River
    {
        public readonly List<Vector2> Points = new List<Vector2>();
        public readonly List<float> Levels = new List<float>();
        public readonly List<float> Widths = new List<float>();
        public readonly List<float> Depths = new List<float>();
        public readonly List<Fall> Falls = new List<Fall>();
        /// <summary>The lake it flows out of, if its head is a lake's outlet; else −1.</summary>
        public int StartsFromLake = -1;
        /// <summary>The lake it flows into, else −1.</summary>
        public int EndsInLake = -1;
        public bool EndsAtSea;
        /// <summary>The river it joins as a tributary (index into the network's rivers), else −1.</summary>
        public int JoinsRiver = -1;
        /// <summary>Country draining through the mouth (game km²).</summary>
        public float CatchmentKm2;

        /// <summary>The widest it gets (at the mouth).</summary>
        public float Width => Widths.Count > 0 ? Widths[Widths.Count - 1] : 0f;

        /// <summary>Length along the course (metres).</summary>
        public float Length
        {
            get
            {
                float l = 0f;
                for (int i = 1; i < Points.Count; i++) l += (Points[i] - Points[i - 1]).magnitude;
                return l;
            }
        }
    }
}
