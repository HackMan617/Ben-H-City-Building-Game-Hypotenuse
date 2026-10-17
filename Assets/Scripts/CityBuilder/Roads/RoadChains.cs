using System.Collections.Generic;
using CityBuilder.Grid;
using UnityEngine;

namespace CityBuilder.Roads
{
    /// <summary>
    /// A run of road between two junctions, as a polyline through cell centres. Points are in grid
    /// units on the ground plane: cell (x, z) has its centre at (x + 0.5, z + 0.5).
    /// </summary>
    public sealed class RoadChain
    {
        public readonly List<Vector2> Points = new List<Vector2>();

        /// <summary>A loop with no junction on it: the last point joins back to the first.</summary>
        public bool Closed;
    }

    /// <summary>
    /// Splits a road layout into the parts built different ways.
    ///
    ///   nodes   cells with any number of links but two -- junctions, dead ends, lone cells. These are
    ///           built as pieces sitting in their cell.
    ///   chains  runs of two-link cells between nodes. These become splines, so a turn reads as a
    ///           curve rather than a corner.
    ///
    /// A chain starts and ends where it meets a node or leaves the cell list, half way between the two
    /// cell centres -- the cell edge for a straight step, the corner for a diagonal. That is exactly
    /// where <see cref="RoadMeshBuilder"/> ends a node's arm, so the two meet flush.
    ///
    ///   crossings  cell corners where two diagonals cross in an X. No cell holds the junction, so
    ///              it sits on the corner itself, and every chain through it stops
    ///              <see cref="CrossingReach"/> short of the corner -- as far out as a junction's arms
    ///              reach in a cell -- leaving room for a crossroads piece turned 45 degrees.
    /// </summary>
    public static class RoadChains
    {
        static readonly HashSet<GridCoord> InList = new HashSet<GridCoord>();
        static readonly HashSet<GridCoord> Visited = new HashSet<GridCoord>();
        static readonly List<Vector2> Back = new List<Vector2>();
        static readonly HashSet<GridCoord> CrossingSeen = new HashSet<GridCoord>();

        /// <summary>How far, in cells, a corner crossing reaches along each of its four diagonals.</summary>
        public const float CrossingReach = 0.5f;

        /// <summary>True when <paramref name="mask"/> makes a junction, dead end or lone cell rather than part of a run.</summary>
        public static bool IsNode(Dir8 mask) => LinkCount(mask) != 2;

        public static int LinkCount(Dir8 mask)
        {
            int count = 0;
            for (int bits = (int)mask; bits != 0; bits &= bits - 1) count++;
            return count;
        }

        /// <summary>
        /// Fills <paramref name="nodes"/> and <paramref name="chains"/> for the road cells in
        /// <paramref name="cells"/>. Links are read from <paramref name="query"/>, which may know about
        /// cells outside the list; a chain stops at the edge of the list.
        /// </summary>
        public static void Extract(IRoadQuery query, IEnumerable<GridCoord> cells, List<GridCoord> nodes,
                                   List<RoadChain> chains, List<Vector2> crossings)
        {
            nodes.Clear();
            chains.Clear();
            crossings.Clear();
            InList.Clear();
            Visited.Clear();
            CrossingSeen.Clear();

            foreach (GridCoord cell in cells)
            {
                if (query.Has(cell)) InList.Add(cell);
            }

            foreach (GridCoord cell in InList)
            {
                Dir8 mask = query.MaskAt(cell);

                foreach (Dir8 dir in Dir8Util.All)
                {
                    if ((mask & dir) == 0 || !CrossesAtCorner(query, cell, dir)) continue;

                    // Named by the corner's own grid point, so the four cells round it agree.
                    GridCoord other = Dir8Util.Step(cell, dir);
                    var corner = new GridCoord(Mathf.Max(cell.X, other.X), Mathf.Max(cell.Z, other.Z));
                    if (CrossingSeen.Add(corner)) crossings.Add(new Vector2(corner.X, corner.Z));
                }

                if (IsNode(mask))
                {
                    nodes.Add(cell);
                    continue;
                }

                if (!Visited.Add(cell)) continue;

                FirstTwo(mask, out Dir8 ahead, out Dir8 behind);

                var chain = new RoadChain();
                chain.Points.Add(Centre(cell));

                if (Walk(query, cell, ahead, chain.Points))
                {
                    chain.Closed = true;
                }
                else
                {
                    Back.Clear();
                    Walk(query, cell, behind, Back);
                    Back.Reverse();
                    chain.Points.InsertRange(0, Back);
                }

                chains.Add(chain);
            }
        }

        /// <summary>
        /// Follows the run out of <paramref name="start"/> along <paramref name="dir"/>, adding a point
        /// per cell and a final one where it ends. Returns true when it comes back round to the start.
        /// </summary>
        static bool Walk(IRoadQuery query, GridCoord start, Dir8 dir, List<Vector2> points)
        {
            GridCoord current = start;

            while (true)
            {
                if (CrossesAtCorner(query, current, dir))
                {
                    points.Add(CrossingEnd(current, dir));
                    return false;
                }

                GridCoord next = Dir8Util.Step(current, dir);
                if (next == start) return true;

                Dir8 nextMask = InList.Contains(next) ? query.MaskAt(next) : Dir8.None;
                Dir8 back = Dir8Util.Opposite(dir);

                if (IsNode(nextMask) || (nextMask & back) == 0 || Visited.Contains(next))
                {
                    points.Add((Centre(current) + Centre(next)) * 0.5f);
                    return false;
                }

                Visited.Add(next);
                points.Add(Centre(next));

                current = next;
                dir = nextMask & ~back;
            }
        }

        /// <summary>
        /// True when the diagonal out of <paramref name="cell"/> along <paramref name="dir"/> is crossed
        /// at the cell corner by the other diagonal of the same 2x2 block.
        /// </summary>
        public static bool CrossesAtCorner(IRoadQuery query, GridCoord cell, Dir8 dir)
        {
            if (!Dir8Util.IsDiagonal(dir)) return false;

            GridCoord other = Dir8Util.Step(cell, dir);
            var c = new GridCoord(other.X, cell.Z);
            var d = new GridCoord(cell.X, other.Z);
            return RoadLink.TryBetween(c, d, out RoadLink link) && link.IsIn(query);
        }

        /// <summary>Where a chain heading from <paramref name="cell"/> into a corner crossing stops.</summary>
        static Vector2 CrossingEnd(GridCoord cell, Dir8 dir)
        {
            GridCoord o = Dir8Util.Offset(dir);
            var step = new Vector2(o.X, o.Z);
            Vector2 corner = Centre(cell) + step * 0.5f;
            return corner - step.normalized * CrossingReach;
        }

        static void FirstTwo(Dir8 mask, out Dir8 first, out Dir8 second)
        {
            first = Dir8.None;
            second = Dir8.None;
            foreach (Dir8 dir in Dir8Util.All)
            {
                if ((mask & dir) == 0) continue;
                if (first == Dir8.None) first = dir;
                else second = dir;
            }
        }

        public static Vector2 Centre(GridCoord cell) => new Vector2(cell.X + 0.5f, cell.Z + 0.5f);
    }
}
