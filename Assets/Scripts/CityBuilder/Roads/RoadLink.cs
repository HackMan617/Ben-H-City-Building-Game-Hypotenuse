using System;
using CityBuilder.Grid;

namespace CityBuilder.Roads
{
    /// <summary>
    /// A connection between two neighbouring cells, orthogonal or diagonal.
    ///
    /// Stored canonically -- always pointing N, NE, E or SE -- so the link from A to B and the link
    /// from B to A compare equal and a stroke never records the same connection twice.
    /// </summary>
    public readonly struct RoadLink : IEquatable<RoadLink>
    {
        public readonly GridCoord Cell;
        public readonly Dir8 Dir;

        public RoadLink(GridCoord cell, Dir8 dir)
        {
            // Bit indices 4..7 are S, SW, W, NW: flip those round to the far cell.
            if (Dir8Util.BitIndex(dir) >= 4)
            {
                cell = Dir8Util.Step(cell, dir);
                dir = Dir8Util.Opposite(dir);
            }

            Cell = cell;
            Dir = dir;
        }

        public GridCoord Other => Dir8Util.Step(Cell, Dir);
        public bool IsDiagonal => Dir8Util.IsDiagonal(Dir);

        /// <summary>The link joining two cells, or false when they are not neighbours.</summary>
        public static bool TryBetween(GridCoord a, GridCoord b, out RoadLink link)
        {
            Dir8 dir = Dir8Util.FromDelta(a, b);
            link = dir == Dir8.None ? default : new RoadLink(a, dir);
            return dir != Dir8.None;
        }

        /// <summary>True when <paramref name="query"/> has this link set on its first cell.</summary>
        public bool IsIn(IRoadQuery query) => (query.MaskAt(Cell) & Dir) != 0;

        public bool Equals(RoadLink other) => Cell == other.Cell && Dir == other.Dir;
        public override bool Equals(object obj) => obj is RoadLink other && Equals(other);
        public override int GetHashCode() => unchecked(Cell.GetHashCode() * 31 + (int)Dir);

        public override string ToString() => $"{Cell}-{Dir}";
    }
}
