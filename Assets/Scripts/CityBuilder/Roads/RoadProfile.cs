using System;
using UnityEngine;

namespace CityBuilder.Roads
{
    /// <summary>Cross-section of a road: how wide the carriageway is and how far it stands proud of the ground.</summary>
    [Serializable]
    public struct RoadProfile
    {
        [Tooltip("World size of one grid cell.")]
        public float CellSize;

        [Tooltip("Width of the carriageway. Anything less than CellSize leaves a shoulder that reads as a curb.")]
        [Range(0.1f, 1f)] public float WidthFraction;

        [Tooltip("How far the road surface sits above the ground plane.")]
        public float Height;

        public float RoadWidth => CellSize * WidthFraction;
        public float HalfRoad => RoadWidth * 0.5f;

        public static RoadProfile Default => new RoadProfile
        {
            CellSize = 1f,
            WidthFraction = 0.82f,
            Height = 0.06f,
        };
    }
}
