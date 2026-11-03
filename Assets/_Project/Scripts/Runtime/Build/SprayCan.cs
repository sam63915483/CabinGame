using System.Collections.Generic;
using UnityEngine;

namespace Cabin.Build
{
    /// <summary>
    /// Spray paint that behaves like the real thing. Each frame thousands of droplets per second leave the nozzle in a
    /// narrow cone (gaussian spread) and travel in straight lines: each is a raycast, so only surfaces a droplet can
    /// reach get paint — anything in front shadows what's behind, back faces and grazing angles get little. Up close
    /// the spot is tight and heavy (lettering); further away it's wide and light (misting). Paint piling onto an
    /// already-covered spot on a steep face runs into drips. Also drives the visible mist and the hiss.
    /// </summary>
    public sealed class SprayCan : MonoBehaviour
    {
        public enum Cap { Fine, Wide }

        [SerializeField] BuildSite site;
        [SerializeField] Material mistMaterial;

        /// <summary>Droplets carry paint this far; past it they've dried to dust.</summary>
        public const float Range = 4.5f;
        /// <summary>Paint per droplet up close (playtest: covering solid took too long — doubled).</summary>
        const float DropletPaint = 0.4f;
        /// <summary>Paint per droplet falls off with distance over this many metres, to a light mist at the far end.</summary>
        const float Falloff = 2.6f;
        /// <summary>Wet paint (sprayed onto covered paint) needed at a spot before it runs.</summary>
        const float DripThreshold = 4f;
        const int MaxDrips = 48;

        struct Drip
        {
            public Piece Piece;
            public int Chart;
            public Vector2 Position;  // texels
            public Vector2 Direction; // texels, unit
            public float Speed;       // metres per second
            public float Paint;
            public Color Color;
        }

        readonly List<Drip> drips = new List<Drip>();
        float pending;
        AudioSource hiss;
        ParticleSystem mist;
        bool spraying;

        public int ActiveDrips => drips.Count;

        /// <summary>Gaussian spread of the cone (one standard deviation), degrees.</summary>
        public static float Spread(Cap cap) => cap == Cap.Fine ? 1.6f : 5f;
        /// <summary>Droplets per second.</summary>
        public static float Rate(Cap cap) => cap == Cap.Fine ? 3500f : 8000f;

        void Awake()
        {
            hiss = gameObject.AddComponent<AudioSource>();
            hiss.clip = HissClip();
            hiss.loop = true;
            hiss.volume = 0f;
            hiss.playOnAwake = false;
            mist = BuildMist();
        }

        /// <summary>
        /// Sprays for one frame from the nozzle towards a point. Returns how many droplets landed on wood or concrete.
        /// </summary>
        public int Spray(Vector3 nozzle, Vector3 towards, float dt, Color color, Cap cap)
        {
            Vector3 dir = (towards - nozzle).normalized;
            pending += Rate(cap) * dt;
            int count = Mathf.FloorToInt(pending);
            pending -= count;

            Vector3 right = Vector3.Cross(Mathf.Abs(dir.y) < 0.95f ? Vector3.up : Vector3.right, dir).normalized;
            Vector3 up = Vector3.Cross(dir, right);
            float sigma = Mathf.Tan(Spread(cap) * Mathf.Deg2Rad);
            int hits = 0;
            for (int i = 0; i < count; i++)
            {
                Vector2 g = Gaussian() * sigma;
                Vector3 d = (dir + right * g.x + up * g.y).normalized;
                if (!Physics.Raycast(nozzle, d, out var hit, Range, BuildLayers.AimMask, QueryTriggerInteraction.Ignore)) continue;
                if (!hit.collider.TryGetComponent<Piece>(out var piece) || piece.Held) continue;
                if (Vector3.Dot(hit.normal, d) >= 0f) continue;
                if (Land(piece, hit.point, hit.normal, hit.distance, color)) hits++;
            }
            spraying = true;
            EmitMist(nozzle, dir, color, cap, count);
            return hits;
        }

        /// <summary>One droplet arriving on a piece: paint at that spot, maybe the start of a run.</summary>
        bool Land(Piece piece, Vector3 point, Vector3 normal, float distance, Color color)
        {
            var layer = piece.EnsurePaint(site.PaintMaterial);
            Vector3 local = piece.transform.InverseTransformPoint(point);
            Vector3 localNormal = piece.transform.InverseTransformDirection(normal);
            if (!piece.Atlas.Locate(local, localNormal, out int chart, out var texel)) return false;
            float amount = DropletPaint * Mathf.Clamp(1.3f - distance / Falloff, 0.15f, 1f);
            float excess = layer.Deposit(texel, color, amount);
            if (excess > 0f && layer.Wetness(texel) > DripThreshold && Mathf.Abs(normal.y) < 0.75f && drips.Count < MaxDrips)
            {
                Vector2 down = piece.Atlas.ChartDirection(chart, piece.transform.InverseTransformDirection(Vector3.down));
                if (down != Vector2.zero)
                    drips.Add(new Drip
                    {
                        Piece = piece, Chart = chart, Position = texel, Direction = down,
                        Speed = Random.Range(0.035f, 0.07f), Paint = Random.Range(0.6f, 1.6f), Color = color,
                    });
                layer.Dry(texel);
            }
            return true;
        }

        void Update()
        {
            hiss.volume = Mathf.MoveTowards(hiss.volume, spraying ? 0.28f : 0f, Time.deltaTime * 4f);
            if (hiss.volume > 0f && !hiss.isPlaying) hiss.Play();
            else if (hiss.volume <= 0f && hiss.isPlaying) hiss.Stop();
            spraying = false;
            RunDrips(Time.deltaTime);
        }

        /// <summary>Runs move down the face, leaving a thin trail, slowing as their paint runs out.</summary>
        public void RunDrips(float dt)
        {
            for (int i = drips.Count - 1; i >= 0; i--)
            {
                var d = drips[i];
                if (d.Piece == null || d.Piece.Paint == null)
                {
                    drips.RemoveAt(i);
                    continue;
                }
                var atlas = d.Piece.Atlas;
                var rect = atlas.TexelRect(d.Chart);
                float steps = d.Speed * dt / atlas.TexelSize;
                bool done = false;
                for (float s = 0f; s < steps && !done; s += 1f)
                {
                    d.Position += d.Direction * Mathf.Min(1f, steps - s);
                    var t = new Vector2Int(Mathf.RoundToInt(d.Position.x), Mathf.RoundToInt(d.Position.y));
                    if (!rect.Contains(t)) { done = true; break; }
                    d.Piece.Paint.Deposit(t, d.Color, 0.5f, narrow: true);
                    d.Paint -= atlas.TexelSize * 4f;
                    if (d.Paint <= 0f) done = true;
                }
                d.Speed *= Mathf.Exp(-dt * 0.6f);
                if (done || d.Speed < 0.004f) drips.RemoveAt(i);
                else drips[i] = d;
            }
        }

        // ---------------------------------------------------------------- feel: mist and hiss

        ParticleSystem BuildMist()
        {
            var go = new GameObject("SprayMist");
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = false;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.18f, 0.4f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.006f, 0.02f);
            main.maxParticles = 4000;
            var emission = ps.emission;
            emission.enabled = false;
            var shape = ps.shape;
            shape.enabled = false;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0.35f, 0f), new GradientAlphaKey(0f, 1f) });
            col.color = grad;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mistMaterial;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return ps;
        }

        void EmitMist(Vector3 nozzle, Vector3 dir, Color color, Cap cap, int droplets)
        {
            if (mist == null || mistMaterial == null) return;
            int n = Mathf.Clamp(droplets / 25, 1, 12);
            float sigma = Mathf.Tan(Spread(cap) * Mathf.Deg2Rad) * 1.3f;
            Vector3 right = Vector3.Cross(Mathf.Abs(dir.y) < 0.95f ? Vector3.up : Vector3.right, dir).normalized;
            Vector3 up = Vector3.Cross(dir, right);
            var p = new ParticleSystem.EmitParams { startColor = color, applyShapeToPosition = false };
            for (int i = 0; i < n; i++)
            {
                Vector2 g = Gaussian() * sigma;
                Vector3 d = (dir + right * g.x + up * g.y).normalized;
                p.position = nozzle;
                p.velocity = d * Random.Range(5f, 9f);
                mist.Emit(p, 1);
            }
        }

        /// <summary>A second of soft hiss: filtered white noise.</summary>
        static AudioClip HissClip()
        {
            const int rate = 44100;
            var data = new float[rate];
            var rng = new System.Random(5);
            float lp = 0f, hp = 0f, prev = 0f;
            for (int i = 0; i < data.Length; i++)
            {
                float w = (float)(rng.NextDouble() * 2.0 - 1.0);
                lp += (w - lp) * 0.55f;
                hp = 0.92f * (hp + lp - prev);
                prev = lp;
                data[i] = hp * 0.6f;
            }
            var clip = AudioClip.Create("SprayHiss", rate, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        static Vector2 Gaussian()
        {
            float u1 = Mathf.Max(1e-6f, Random.value), u2 = Random.value;
            float r = Mathf.Sqrt(-2f * Mathf.Log(u1));
            float a = 2f * Mathf.PI * u2;
            return new Vector2(r * Mathf.Cos(a), r * Mathf.Sin(a));
        }
    }
}
