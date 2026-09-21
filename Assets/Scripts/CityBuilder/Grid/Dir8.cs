using System;
using UnityEngine;

namespace CityBuilder.Grid
{
    /// <summary>
    /// Connection mask for a grid cell. Eight bits from the start so that 45-degree links can be
    /// switched on later without touching the data model, the mesh lookup, or the tools -- only the
    /// path resolver and the diagonal geometry in <see cref="Roads.RoadMeshBuilder"/> need to learn them.
    /// </summary>
    [Flags]
    public enum Dir8 : byte
    {
        None = 0,
        N  = 1 << 0,
        NE = 1 << 1,
        E  = 1 << 2,
        SE = 1 << 3,
        S  = 1 << 4,
        SW = 1 << 5,
        W  = 1 << 6,
        NW = 1 << 7,

        Orthogonal = N | E | S | W,
        Diagonal   = NE | SE | SW | NW,
        All        = Orthogonal | Diagonal,
    }

    public static class Dir8Util
    {
        /// <summary>All eight directions in clockwise order starting at north.</summary>
        public static readonly Dir8[] All =
        {
            Dir8.N, Dir8.NE, Dir8.E, Dir8.SE, Dir8.S, Dir8.SW, Dir8.W, Dir8.NW
        };

        /// <summary>The four axis-aligned directions, clockwise from north.</summary>
        public static readonly Dir8[] Orthogonal = { Dir8.N, Dir8.E, Dir8.S, Dir8.W };

        /// <summary>Cell offset for each direction, indexed by bit position.</summary>
        static readonly GridCoord[] Offsets =
        {
            new GridCoord( 0,  1), // N
            new GridCoord( 1,  1), // NE
            new GridCoord( 1,  0), // E
            new GridCoord( 1, -1), // SE
            new GridCoord( 0, -1), // S
            new GridCoord(-1, -1), // SW
            new GridCoord(-1,  0), // W
            new GridCoord(-1,  1), // NW
        };

        public static int BitIndex(Dir8 dir)
        {
            switch (dir)
            {
                case Dir8.N:  return 0;
                case Dir8.NE: return 1;
                case Dir8.E:  return 2;
                case Dir8.SE: return 3;
                case Dir8.S:  return 4;
                case Dir8.SW: return 5;
                case Dir8.W:  return 6;
                case Dir8.NW: return 7;
                default: return -1;
            }
        }

        public static GridCoord Offset(Dir8 dir)
        {
            int i = BitIndex(dir);
            return i < 0 ? default : Offsets[i];
        }

        public static GridCoord Step(GridCoord from, Dir8 dir) => from + Offset(dir);

        /// <summary>Rotate a direction 180 degrees. Used to keep links symmetric between two cells.</summary>
        public static Dir8 Opposite(Dir8 dir)
        {
            int i = BitIndex(dir);
            if (i < 0) return Dir8.None;
            return All[(i + 4) % 8];
        }

        public static bool IsDiagonal(Dir8 dir) => (dir & Dir8.Diagonal) != 0;

        /// <summary>
        /// Direction of a single step. Returns <see cref="Dir8.None"/> when the delta is not a unit
        /// step in one of the eight directions.
        /// </summary>
        public static Dir8 FromDelta(int dx, int dz)
        {
            if (Mathf.Abs(dx) > 1 || Mathf.Abs(dz) > 1 || (dx == 0 && dz == 0)) return Dir8.None;
            for (int i = 0; i < Offsets.Length; i++)
            {
                if (Offsets[i].X == dx && Offsets[i].Z == dz) return All[i];
            }
            return Dir8.None;
        }

        public static Dir8 FromDelta(GridCoord from, GridCoord to) => FromDelta(to.X - from.X, to.Z - from.Z);

        /// <summary>Outward horizontal normal for a direction, in world space.</summary>
        public static Vector3 Normal(Dir8 dir)
        {
            GridCoord o = Offset(dir);
            return new Vector3(o.X, 0f, o.Z).normalized;
        }
    }
}
