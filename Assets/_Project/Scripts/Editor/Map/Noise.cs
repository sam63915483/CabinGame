using UnityEngine;

namespace Cabin.Editor.Map
{
    /// <summary>
    /// Seeded 2D gradient noise, thread-safe (the generator fills its grids in parallel, and Mathf.PerlinNoise is
    /// main-thread only). Values roughly −1…1.
    /// </summary>
    public readonly struct Noise
    {
        readonly int seed;

        public Noise(int seed) => this.seed = seed;

        static float Fade(float t) => t * t * t * (t * (t * 6f - 15f) + 10f);

        float Grad(int x, int y, float dx, float dy)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1442695041);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                float a = (h & 0xffff) / 65536f * Mathf.PI * 2f;
                return Mathf.Cos(a) * dx + Mathf.Sin(a) * dy;
            }
        }

        public float At(float x, float y)
        {
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            float u = Fade(fx), v = Fade(fy);
            float a = Grad(x0, y0, fx, fy), b = Grad(x0 + 1, y0, fx - 1f, fy);
            float c = Grad(x0, y0 + 1, fx, fy - 1f), d = Grad(x0 + 1, y0 + 1, fx - 1f, fy - 1f);
            return Mathf.Lerp(Mathf.Lerp(a, b, u), Mathf.Lerp(c, d, u), v) * 1.4f;
        }

        /// <summary>Fractal sum, roughly −1…1.</summary>
        public float Fbm(float x, float y, int octaves)
        {
            float sum = 0f, amp = 0.5f, norm = 0f;
            for (int i = 0; i < octaves; i++)
            {
                sum += At(x, y) * amp;
                norm += amp;
                x = x * 2.03f + 17.1f;
                y = y * 2.03f - 9.7f;
                amp *= 0.5f;
            }
            return sum / norm;
        }

        /// <summary>Ridged fractal, 0…1: sharp crests like rocky ridges.</summary>
        public float Ridged(float x, float y, int octaves)
        {
            float sum = 0f, amp = 0.5f, norm = 0f;
            for (int i = 0; i < octaves; i++)
            {
                float r = 1f - Mathf.Abs(At(x, y));
                sum += r * r * amp;
                norm += amp;
                x = x * 2.1f + 5.3f;
                y = y * 2.1f + 11.9f;
                amp *= 0.5f;
            }
            return sum / norm;
        }
    }
}
