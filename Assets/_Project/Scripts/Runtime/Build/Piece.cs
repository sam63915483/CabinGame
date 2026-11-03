using UnityEngine;

namespace Cabin.Build
{
    /// <summary>
    /// One piece of lumber in the world. Its shape is a <see cref="ConvexSolid"/> in the original stock's frame
    /// (x along the grain), rendered flat-shaded and collided with an exact convex mesh collider.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider))]
    public sealed class Piece : MonoBehaviour
    {
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        public int Id { get; private set; }
        public StockKind Kind { get; private set; }
        public ConvexSolid Solid { get; private set; }

        /// <summary>A 4×4 set vertically into the ground: the anchor everything else hangs off.</summary>
        public bool Planted;
        /// <summary>Held by the clamps (one piece at a time).</summary>
        public bool Clamped;
        /// <summary>Lying on the cutting table.</summary>
        public bool OnTable;
        /// <summary>Carried by the player right now.</summary>
        public bool Held;
        /// <summary>For a post standing in a deck block: that block's id. 0 otherwise.</summary>
        public int SeatedIn;

        public bool HeldInPlace => Planted || Clamped || OnTable || Held;

        public MeshCollider Collider { get; private set; }
        /// <summary>Unique surface layout for spray paint, rebuilt with the mesh.</summary>
        public PaintAtlas Atlas { get; private set; }
        /// <summary>Spray paint on this piece, or null if it's never been painted.</summary>
        public PaintLayer Paint { get; private set; }
        public Structure Structure => transform.parent != null ? transform.parent.GetComponent<Structure>() : null;

        MeshFilter filter;
        MeshRenderer meshRenderer;
        MaterialPropertyBlock block;

        public float Volume => Solid.Volume;
        public float Mass => Volume * Stock.Density(Kind);
        public bool IsConcrete => Stock.IsConcrete(Kind);

        /// <summary>Length along the grain, in metres.</summary>
        public float Length
        {
            get
            {
                var e = Solid.Extent(GrainAxisLocal);
                return e.y - e.x;
            }
        }

        public Vector3 GrainAxisLocal
        {
            get
            {
                if (Kind == StockKind.DeckBlock) return Vector3.up;
                int a = Solid.LongestAxis;
                return a == 0 ? Vector3.right : a == 1 ? Vector3.up : Vector3.forward;
            }
        }

        /// <summary>World point at the centre of a deck block's top face (the socket).</summary>
        public Vector3 SocketTop => transform.TransformPoint(new Vector3(0f, Solid.Extent(Vector3.up).y, 0f));

        public Vector3 GrainAxisWorld => transform.TransformDirection(GrainAxisLocal);

        public void Init(int id, StockKind kind, ConvexSolid solid, Material material)
        {
            Id = id;
            Kind = kind;
            filter = GetComponent<MeshFilter>();
            meshRenderer = GetComponent<MeshRenderer>();
            Collider = GetComponent<MeshCollider>();
            Collider.convex = true;
            meshRenderer.sharedMaterial = material;
            gameObject.layer = BuildLayers.Piece;
            name = $"{Stock.Name(kind)}_{id}";
            SetSolid(solid);
        }

        /// <summary>Changes the shape. Paint follows it; faces that didn't exist before (cut faces) come out bare.</summary>
        /// <param name="offset">A point in the new local frame + offset = the same point in the old one (non-zero when re-centring).</param>
        public void SetSolid(ConvexSolid solid, Vector3 offset = default)
        {
            Solid = solid;
            Atlas = PaintAtlas.Build(solid, GrainAxisLocal);
            var old = filter.sharedMesh;
            var mesh = solid.ToMesh(name, Atlas);
            filter.sharedMesh = mesh;
            Collider.sharedMesh = mesh;
            if (old != null) Destroy(old);
            Paint?.Remap(Atlas, offset);
        }

        /// <summary>The paint layer, made on first use.</summary>
        public PaintLayer EnsurePaint(Material template) => Paint ??= new PaintLayer(this, template);

        /// <summary>Starts this piece's paint as a copy of another layout's (an offcut taking its share of the paint).</summary>
        public void AdoptPaint(Material template, PaintAtlas fromAtlas, Color32[] fromPixels, Vector3 offset)
        {
            Paint?.Dispose();
            Paint = PaintLayer.CopyOnto(this, template, fromAtlas, fromPixels, offset);
        }

        public void SetTint(Color color)
        {
            block ??= new MaterialPropertyBlock();
            block.SetColor(BaseColorId, color);
            meshRenderer.SetPropertyBlock(block);
        }

        public void ClearTint() => SetTint(Color.white);

        /// <summary>The lowest point of the piece in world space (used to decide which half of a cut post stays planted).</summary>
        public float LowestWorldY()
        {
            float y = float.MaxValue;
            foreach (var v in Solid.Vertices) y = Mathf.Min(y, transform.TransformPoint(v).y);
            return y;
        }

        void OnDestroy()
        {
            if (filter != null && filter.sharedMesh != null) Destroy(filter.sharedMesh);
            Paint?.Dispose();
        }
    }
}
