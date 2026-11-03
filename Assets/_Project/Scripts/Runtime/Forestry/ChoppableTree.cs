using UnityEngine;

namespace Cabin.Forestry
{
    /// <summary>
    /// What the axe needs to know about a kind of tree, kept on its terrain-tree prefab (terrain trees ignore it): a
    /// readable copy of its full-detail mesh and materials to cut up, how thick the trunk is up its first couple of
    /// metres, and its bark colour. Filled in by the map generator.
    /// </summary>
    public sealed class ChoppableTree : MonoBehaviour
    {
        [SerializeField] Mesh mesh;
        [SerializeField] Material[] materials;
        [Tooltip("Trunk radius (metres, unscaled) every RadiusStep up from the ground.")]
        [SerializeField] float[] radii;
        [SerializeField] Color bark = new Color(0.4f, 0.3f, 0.22f);
        [SerializeField] float height;

        public const float RadiusStep = 0.1f;

        public Mesh Mesh => mesh;
        public Material[] Materials => materials;
        public Color Bark => bark;
        /// <summary>Unscaled height of the tree.</summary>
        public float Height => height;

        public void Set(Mesh fullMesh, Material[] mats, float[] trunkRadii, Color barkColor, float treeHeight)
        {
            mesh = fullMesh;
            materials = mats;
            radii = trunkRadii;
            bark = barkColor;
            height = treeHeight;
        }

        /// <summary>Trunk radius at a height above the base (unscaled), interpolated.</summary>
        public float RadiusAt(float y)
        {
            if (radii == null || radii.Length == 0) return 0.2f;
            float f = Mathf.Clamp(y / RadiusStep, 0f, radii.Length - 1);
            int i = Mathf.Min((int)f, radii.Length - 2);
            return i < 0 ? radii[0] : Mathf.Lerp(radii[i], radii[i + 1], f - i);
        }
    }
}
