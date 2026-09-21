using System;
using UnityEngine;

namespace CityBuilder.Grid
{
    /// <summary>
    /// Integer cell address on the build grid. X runs east, Z runs north; Y is always the ground plane.
    /// </summary>
    [Serializable]
    public struct GridCoord : IEquatable<GridCoord>
    {
        public int X;
        public int Z;

        public GridCoord(int x, int z)
        {
            X = x;
            Z = z;
        }

        public static GridCoord operator +(GridCoord a, GridCoord b) => new GridCoord(a.X + b.X, a.Z + b.Z);
        public static GridCoord operator -(GridCoord a, GridCoord b) => new GridCoord(a.X - b.X, a.Z - b.Z);
        public static bool operator ==(GridCoord a, GridCoord b) => a.X == b.X && a.Z == b.Z;
        public static bool operator !=(GridCoord a, GridCoord b) => !(a == b);

        public bool Equals(GridCoord other) => X == other.X && Z == other.Z;
        public override bool Equals(object obj) => obj is GridCoord other && Equals(other);

        // Cheap spatial hash: keeps neighbouring cells in different buckets.
        public override int GetHashCode() => unchecked((X * 73856093) ^ (Z * 19349663));

        public override string ToString() => $"({X}, {Z})";
    }
}
