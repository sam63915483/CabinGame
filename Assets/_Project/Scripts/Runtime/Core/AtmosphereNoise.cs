using UnityEngine;

namespace Cabin.Core
{
    /// <summary>Tiling value noise for the clouds: the same lattice wraps at <c>period</c> on every octave, so a texture made from it repeats seamlessly.</summary>
    public static class AtmosphereNoise
    {
        static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 2246822519);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFFu) / 16777216f;
            }
        }

        static float Smooth(float t) => t * t * (3f - 2f * t);

        /// <summary>Value noise at (x, y) in lattice units, wrapping every <paramref name="period"/> units.</summary>
        public static float Value(float x, float y, int period, int seed)
        {
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = Smooth(x - x0), fy = Smooth(y - y0);
            int ax = ((x0 % period) + period) % period, ay = ((y0 % period) + period) % period;
            int bx = (ax + 1) % period, by = (ay + 1) % period;
            float v00 = Hash(ax, ay, seed), v10 = Hash(bx, ay, seed), v01 = Hash(ax, by, seed), v11 = Hash(bx, by, seed);
            return Mathf.Lerp(Mathf.Lerp(v00, v10, fx), Mathf.Lerp(v01, v11, fx), fy);
        }

        /// <summary>Fractal noise in 0–1 over the unit square (u, v in 0–1), tiling at 1: <paramref name="period"/> lattice cells on the first octave, doubling each octave.</summary>
        public static float Fbm(float u, float v, int period, int octaves, int seed)
        {
            float sum = 0f, amp = 0.5f, norm = 0f;
            int p = period;
            for (int o = 0; o < octaves; o++)
            {
                sum += Value(u * p, v * p, p, seed + o * 101) * amp;
                norm += amp;
                amp *= 0.5f;
                p *= 2;
            }
            return sum / norm;
        }

        /// <summary>A tiling cloud texture: R is the fractal density (0–1, stretched so its range is used).</summary>
        public static Texture2D CloudTexture(int size, int period, int seed)
        {
            var tex = new Texture2D(size, size, TextureFormat.R8, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, name = "Clouds" };
            var px = new Color32[size * size];
            float min = 1f, max = 0f;
            var raw = new float[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float f = Fbm((x + 0.5f) / size, (y + 0.5f) / size, period, 5, seed);
                raw[y * size + x] = f;
                if (f < min) min = f;
                if (f > max) max = f;
            }
            for (int i = 0; i < raw.Length; i++)
            {
                byte b = (byte)Mathf.RoundToInt(Mathf.InverseLerp(min, max, raw[i]) * 255f);
                px[i] = new Color32(b, b, b, 255);
            }
            tex.SetPixels32(px);
            tex.Apply(true, false);
            return tex;
        }

        /// <summary>The light cookie matching a cloud texture: white where the sky is clear, darkened under cloud by <paramref name="shadowStrength"/>.</summary>
        public static Texture2D CookieTexture(Texture2D clouds, float coverage, float softness, float shadowStrength)
        {
            int size = clouds.width;
            var src = clouds.GetPixels32();
            var px = new Color32[src.Length];
            for (int i = 0; i < src.Length; i++)
            {
                float d = src[i].r / 255f;
                float cover = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(coverage, coverage + softness, d));
                byte b = (byte)Mathf.RoundToInt((1f - shadowStrength * cover) * 255f);
                px[i] = new Color32(b, b, b, 255);
            }
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, name = "Cloud shadows" };
            tex.SetPixels32(px);
            tex.Apply(false, false);
            return tex;
        }
    }
}
