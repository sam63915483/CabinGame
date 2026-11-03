using UnityEngine;
using UnityEngine.Rendering;

namespace Cabin.Build
{
    /// <summary>
    /// Spray paint on one piece: an RGBA texture laid out by the piece's <see cref="PaintAtlas"/>, shown by a thin
    /// transparent overlay that hugs the piece's faces. Alpha is coverage: coats build up, overspray stays faint.
    /// Created the first time a piece is painted.
    /// </summary>
    public sealed class PaintLayer
    {
        /// <summary>How far the overlay floats off the wood, so it never z-fights.</summary>
        const float Lift = 0.0006f;

        public PaintAtlas Atlas { get; private set; }
        public Color32[] Pixels { get; private set; }
        /// <summary>Paint piling up on already-covered spots; enough of it on a steep face starts a drip.</summary>
        float[] wet;

        readonly Piece owner;
        readonly Material material;
        Texture2D texture;
        GameObject overlay;
        Mesh overlayMesh;

        public bool Dirty { get; private set; }

        public PaintLayer(Piece owner, Material template)
        {
            this.owner = owner;
            material = new Material(template) { name = $"Paint_{owner.Id}" };
            Rebind(owner.Atlas, new Color32[owner.Atlas.Width * owner.Atlas.Height]);
        }

        /// <summary>Points the layer at an atlas and its pixels: new texture, new overlay mesh.</summary>
        void Rebind(PaintAtlas atlas, Color32[] pixels)
        {
            Atlas = atlas;
            Pixels = pixels;
            wet = new float[pixels.Length];
            if (texture != null) Object.Destroy(texture);
            texture = new Texture2D(atlas.Width, atlas.Height, TextureFormat.RGBA32, false, false)
            {
                name = $"PaintTex_{owner.Id}",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            material.SetTexture("_BaseMap", texture);
            BuildOverlay();
            Dirty = true;
        }

        void BuildOverlay()
        {
            if (overlay == null)
            {
                overlay = new GameObject("Paint");
                overlay.transform.SetParent(owner.transform, false);
                overlay.AddComponent<MeshFilter>();
                var r = overlay.AddComponent<MeshRenderer>();
                r.sharedMaterial = material;
                r.shadowCastingMode = ShadowCastingMode.Off;
            }
            overlay.transform.localPosition = Vector3.zero;
            overlay.transform.localRotation = Quaternion.identity;
            if (overlayMesh != null) Object.Destroy(overlayMesh);
            overlayMesh = OverlayMesh(owner.Solid, Atlas);
            overlay.GetComponent<MeshFilter>().sharedMesh = overlayMesh;
        }

        /// <summary>The piece's faces, lifted a hair, with UV0 = paint atlas coordinates.</summary>
        static Mesh OverlayMesh(ConvexSolid solid, PaintAtlas atlas)
        {
            var mesh = solid.ToMesh("PaintOverlay", atlas);
            var verts = mesh.vertices;
            var normals = mesh.normals;
            for (int i = 0; i < verts.Length; i++) verts[i] += normals[i] * Lift;
            var uv2 = new System.Collections.Generic.List<Vector2>();
            mesh.GetUVs(1, uv2);
            mesh.vertices = verts;
            mesh.SetUVs(0, uv2);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// Adds paint around one texel with a soft 3×3 footprint. Returns how much paint landed on already-covered
        /// paint (what makes runs).
        /// </summary>
        /// <param name="narrow">A plus-shaped footprint instead of 3×3 (the thin trail of a drip).</param>
        public float Deposit(Vector2Int t, Color color, float amount, bool narrow = false)
        {
            int w = Atlas.Width, h = Atlas.Height;
            float excess = 0f;
            for (int dy = -2; dy <= 2; dy++)
                for (int dx = -2; dx <= 2; dx++)
                {
                    int x = t.x + dx, y = t.y + dy;
                    if (x < 0 || y < 0 || x >= w || y >= h) continue;
                    int i = y * w + x;
                    int ring = Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy));
                    if (narrow && ring == 2) continue;
                    if (ring == 2 || narrow && dx != 0 && dy != 0)
                    {
                        // Outside the dot: give bare texels the colour (no alpha) so filtering doesn't fringe dark.
                        if (Pixels[i].a == 0) Pixels[i] = new Color32((byte)(color.r * 255f), (byte)(color.g * 255f), (byte)(color.b * 255f), 0);
                        continue;
                    }
                    float k = dx == 0 && dy == 0 ? 1f : (dx == 0 || dy == 0) ? 0.5f : 0.25f;
                    float add = amount * k;
                    Color32 p = Pixels[i];
                    float a0 = p.a / 255f;
                    float a1 = a0 + add * (1f - a0);
                    float mix = a1 > 1e-4f ? add / a1 : 1f;
                    var c0 = a0 > 0f ? (Color)p : color;
                    var c = Color.Lerp(c0, color, Mathf.Clamp01(mix));
                    Pixels[i] = new Color32((byte)(c.r * 255f), (byte)(c.g * 255f), (byte)(c.b * 255f), (byte)Mathf.Clamp(a1 * 255f + 0.5f, 0f, 255f));
                    if (a0 > 0.9f)
                    {
                        wet[i] += add;
                        excess += add;
                    }
                }
            Dirty = true;
            return excess;
        }

        /// <summary>How wet a texel is: paint sprayed onto it after it was already covered.</summary>
        public float Wetness(Vector2Int t) => wet[t.y * Atlas.Width + t.x];

        public void Dry(Vector2Int t) => wet[t.y * Atlas.Width + t.x] = 0f;

        public float Coverage(Vector2Int t) => Pixels[t.y * Atlas.Width + t.x].a / 255f;

        public void Flush()
        {
            if (!Dirty) return;
            texture.SetPixels32(Pixels);
            texture.Apply(false);
            Dirty = false;
        }

        /// <summary>The piece's shape changed (cut, re-centred): carry the paint across to the new layout.</summary>
        /// <param name="offset">New local point + offset = the same point in the old local frame.</param>
        public void Remap(PaintAtlas newAtlas, Vector3 offset) => Rebind(newAtlas, Transfer(Atlas, Pixels, newAtlas, offset));

        /// <summary>A fresh layer on <paramref name="target"/> carrying paint from another piece's layer (an offcut).</summary>
        public static PaintLayer CopyOnto(Piece target, Material template, PaintAtlas fromAtlas, Color32[] fromPixels, Vector3 offset)
        {
            var layer = new PaintLayer(target, template);
            layer.Rebind(target.Atlas, Transfer(fromAtlas, fromPixels, target.Atlas, offset));
            return layer;
        }

        /// <summary>
        /// For every texel of the new layout, finds the same spot on the old surface (if it existed there) and
        /// copies its paint. Faces made by a cut didn't exist before, so they come out bare.
        /// </summary>
        public static Color32[] Transfer(PaintAtlas from, Color32[] fromPixels, PaintAtlas to, Vector3 offset)
        {
            var result = new Color32[to.Width * to.Height];
            for (int ci = 0; ci < to.Charts.Count; ci++)
            {
                var rect = to.TexelRect(ci);
                Vector3 n = to.Charts[ci].Normal;
                for (int y = rect.yMin; y < rect.yMax; y++)
                    for (int x = rect.xMin; x < rect.xMax; x++)
                    {
                        Vector3 p = to.PointAt(ci, x, y) + offset;
                        if (!from.Locate(p, n, out _, out var src)) continue;
                        var c = fromPixels[src.y * from.Width + src.x];
                        if (c.a == 0 && c.r == 0 && c.g == 0 && c.b == 0) continue;
                        result[y * to.Width + x] = c;
                    }
            }
            return result;
        }

        public string EncodeBase64()
        {
            Flush();
            return System.Convert.ToBase64String(texture.EncodeToPNG());
        }

        /// <summary>Loads saved paint. Ignored if the layout no longer matches.</summary>
        public bool Decode(string base64)
        {
            var tmp = new Texture2D(2, 2, TextureFormat.RGBA32, false, false);
            bool ok = tmp.LoadImage(System.Convert.FromBase64String(base64)) && tmp.width == Atlas.Width && tmp.height == Atlas.Height;
            if (ok)
            {
                Pixels = tmp.GetPixels32();
                Dirty = true;
            }
            Object.Destroy(tmp);
            return ok;
        }

        public void Dispose()
        {
            if (texture != null) Object.Destroy(texture);
            if (overlayMesh != null) Object.Destroy(overlayMesh);
            if (material != null) Object.Destroy(material);
            if (overlay != null) Object.Destroy(overlay);
        }
    }
}
