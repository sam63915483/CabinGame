using System.Threading.Tasks;
using UnityEngine;

namespace Cabin.Editor.Map
{
    /// <summary>A regular grid of floats over the map (x east, z north), sample (0,0) at <see cref="Origin"/>.</summary>
    public sealed class Grid
    {
        public readonly int W, H;
        public readonly float Cell;
        public readonly Vector2 Origin;
        public readonly float[] V;

        public Grid(int w, int h, float cell, Vector2 origin, float fill = 0f)
        {
            W = w;
            H = h;
            Cell = cell;
            Origin = origin;
            V = new float[w * h];
            if (fill != 0f) System.Array.Fill(V, fill);
        }

        public float this[int x, int z]
        {
            get => V[z * W + x];
            set => V[z * W + x] = value;
        }

        public float X(int x) => Origin.x + x * Cell;
        public float Z(int z) => Origin.y + z * Cell;

        public float At(int x, int z) => V[Mathf.Clamp(z, 0, H - 1) * W + Mathf.Clamp(x, 0, W - 1)];

        /// <summary>Bilinear at a world position, clamped to the grid.</summary>
        public float Sample(float wx, float wz)
        {
            float fx = (wx - Origin.x) / Cell, fz = (wz - Origin.y) / Cell;
            int ix = Mathf.FloorToInt(fx), iz = Mathf.FloorToInt(fz);
            float tx = fx - ix, tz = fz - iz;
            return Mathf.Lerp(Mathf.Lerp(At(ix, iz), At(ix + 1, iz), tx), Mathf.Lerp(At(ix, iz + 1), At(ix + 1, iz + 1), tx), tz);
        }


        static float CatmullRom(float p0, float p1, float p2, float p3, float t) =>
            0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t * t + (-p0 + 3f * p1 - 3f * p2 + p3) * t * t * t);

        /// <summary>Bicubic (Catmull-Rom) at a world position, clamped to the grid: smooth enough to upsample from.</summary>
        public float SampleCubic(float wx, float wz)
        {
            float fx = (wx - Origin.x) / Cell, fz = (wz - Origin.y) / Cell;
            int ix = Mathf.FloorToInt(fx), iz = Mathf.FloorToInt(fz);
            float tx = fx - ix, tz = fz - iz;
            float r0 = CatmullRom(At(ix - 1, iz - 1), At(ix, iz - 1), At(ix + 1, iz - 1), At(ix + 2, iz - 1), tx);
            float r1 = CatmullRom(At(ix - 1, iz), At(ix, iz), At(ix + 1, iz), At(ix + 2, iz), tx);
            float r2 = CatmullRom(At(ix - 1, iz + 1), At(ix, iz + 1), At(ix + 1, iz + 1), At(ix + 2, iz + 1), tx);
            float r3 = CatmullRom(At(ix - 1, iz + 2), At(ix, iz + 2), At(ix + 1, iz + 2), At(ix + 2, iz + 2), tx);
            return CatmullRom(r0, r1, r2, r3, tz);
        }
        /// <summary>Slope at a sample, degrees.</summary>
        public float Slope(int x, int z)
        {
            float dx = (At(x + 1, z) - At(x - 1, z)) / (2f * Cell);
            float dz = (At(x, z + 1) - At(x, z - 1)) / (2f * Cell);
            return Mathf.Atan(Mathf.Sqrt(dx * dx + dz * dz)) * Mathf.Rad2Deg;
        }

        /// <summary>Separable box blur of the given radius (samples), repeated: close to a gaussian.</summary>
        public void Blur(int radius, int passes)
        {
            if (radius <= 0) return;
            var tmp = new float[V.Length];
            for (int p = 0; p < passes; p++)
            {
                Parallel.For(0, H, z =>
                {
                    float sum = 0f;
                    int row = z * W;
                    for (int i = -radius; i <= radius; i++) sum += V[row + Mathf.Clamp(i, 0, W - 1)];
                    for (int x = 0; x < W; x++)
                    {
                        tmp[row + x] = sum / (2 * radius + 1);
                        sum += V[row + Mathf.Min(x + radius + 1, W - 1)] - V[row + Mathf.Max(x - radius, 0)];
                    }
                });
                Parallel.For(0, W, x =>
                {
                    float sum = 0f;
                    for (int i = -radius; i <= radius; i++) sum += tmp[Mathf.Clamp(i, 0, H - 1) * W + x];
                    for (int z = 0; z < H; z++)
                    {
                        V[z * W + x] = sum / (2 * radius + 1);
                        sum += tmp[Mathf.Min(z + radius + 1, H - 1) * W + x] - tmp[Mathf.Max(z - radius, 0) * W + x];
                    }
                });
            }
        }
    }
}
