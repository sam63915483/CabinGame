namespace Cabin.Editor.Map
{
    /// <summary>One tree the map scatters: its pack prefab, what kind it is, and how common within its kind.</summary>
    public readonly struct TreeSpecies
    {
        public readonly string Source;
        public readonly TreeKind Kind;
        public readonly float Weight;

        public TreeSpecies(string source, TreeKind kind, float weight)
        {
            Source = source;
            Kind = kind;
            Weight = weight;
        }
    }
}
