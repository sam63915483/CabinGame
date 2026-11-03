using System;
using UnityEngine;

namespace Cabin.Map
{
    /// <summary>
    /// Game position ↔ longitude / latitude for a recipe: a local equirectangular projection about the map centre,
    /// north up (+z), scaled by <see cref="MapRecipe.realMetresPerMetre"/>. Close enough to true shape over a province.
    /// </summary>
    public readonly struct MapProjection
    {
        const double MetresPerDegree = 111320.0;

        readonly double lon0, lat0, scale, cosLat;
        readonly float halfW, halfD;

        public MapProjection(MapRecipe recipe)
        {
            lon0 = recipe.centreLongitude;
            lat0 = recipe.centreLatitude;
            scale = recipe.realMetresPerMetre;
            cosLat = Math.Cos(lat0 * Math.PI / 180.0);
            halfW = recipe.WorldWidth * 0.5f;
            halfD = recipe.WorldDepth * 0.5f;
        }

        public void ToGeo(float x, float z, out double lon, out double lat)
        {
            lon = lon0 + x * scale / (MetresPerDegree * cosLat);
            lat = lat0 + z * scale / MetresPerDegree;
        }

        public Vector2 FromGeo(double lon, double lat) => new Vector2(
            (float)((lon - lon0) * MetresPerDegree * cosLat / scale),
            (float)((lat - lat0) * MetresPerDegree / scale));

        /// <summary>The map's corners in degrees.</summary>
        public void GeoBounds(out double west, out double south, out double east, out double north)
        {
            ToGeo(-halfW, -halfD, out west, out south);
            ToGeo(halfW, halfD, out east, out north);
        }
    }
}
