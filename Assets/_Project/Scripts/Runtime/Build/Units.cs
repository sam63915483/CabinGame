using UnityEngine;

namespace Cabin.Build
{
    /// <summary>Real-world units (handoff Appendix A). Everything in the game is metres.</summary>
    public static class Units
    {
        public const float Inch = 0.0254f;
        public const float Foot = 0.3048f;

        public static float SnapToInch(float metres) => Mathf.Round(metres / Inch) * Inch;

        /// <summary>Formats metres as feet and inches to the nearest 1/8 in, e.g. 7' 8 5/8".</summary>
        public static string FeetInches(float metres)
        {
            int eighths = Mathf.RoundToInt(Mathf.Abs(metres) / Inch * 8f);
            int feet = eighths / 96;
            int remEighths = eighths % 96;
            int inches = remEighths / 8;
            int frac = remEighths % 8;
            string fracText = "";
            if (frac != 0)
            {
                int num = frac, den = 8;
                while (num % 2 == 0) { num /= 2; den /= 2; }
                fracText = $" {num}/{den}";
            }
            string sign = metres < 0f ? "-" : "";
            if (feet == 0) return $"{sign}{inches}{fracText}\"";
            return $"{sign}{feet}' {inches}{fracText}\"";
        }
    }
}
