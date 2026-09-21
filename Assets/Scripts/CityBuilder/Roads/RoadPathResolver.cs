using System.Collections.Generic;
using CityBuilder.Grid;
using UnityEngine;

namespace CityBuilder.Roads
{
    public enum PathMode
    {
        /// <summary>One run along the dominant axis, then one 90-degree turn onto the cursor.</summary>
        ElbowAxisOnly,

        /// <summary>Locks to a single axis for the whole drag; sideways movement is ignored.</summary>
        StraightAxisOnly,

        /// <summary>
        /// A 45-degree run covering the shorter axis plus a straight run covering the rest, joined
        /// by one 135-degree turn -- the metromapmaker shape. Which comes first is up to the caller.
        /// </summary>
        ElbowWithDiagonal,
    }

    /// <summary>
    /// Turns an anchor cell plus a cursor cell into the ordered run of cells a drag would fill.
    ///
    /// Output is always a list of cells one step apart, so the direction of travel into and out of
    /// every cell is recoverable with <see cref="Dir8Util.FromDelta(GridCoord, GridCoord)"/>. That is
    /// what keeps diagonals a data change rather than a rewrite: a diagonal step is just another
    /// unit step in the same list.
    /// </summary>
    public static class RoadPathResolver
    {
        /// <summary>
        /// Resolves a drag. <paramref name="preferHorizontalFirst"/> breaks the tie when an
        /// axis-only drag is square, and lets the caller keep the elbow stable instead of flipping
        /// mid-drag. <paramref name="diagonalFirst"/> picks which leg of a diagonal elbow leaves
        /// the anchor.
        /// </summary>
        public static void Resolve(GridCoord anchor, GridCoord cursor, PathMode mode,
                                   List<GridCoord> into, bool preferHorizontalFirst = true,
                                   bool diagonalFirst = true)
        {
            into.Clear();
            into.Add(anchor);

            int dx = cursor.X - anchor.X;
            int dz = cursor.Z - anchor.Z;
            if (dx == 0 && dz == 0) return;

            switch (mode)
            {
                case PathMode.StraightAxisOnly:
                    ResolveStraight(anchor, dx, dz, into);
                    break;
                case PathMode.ElbowWithDiagonal:
                    ResolveDiagonalElbow(anchor, dx, dz, into, diagonalFirst);
                    break;
                default:
                    ResolveElbow(anchor, dx, dz, into, preferHorizontalFirst);
                    break;
            }
        }

        /// <summary>Whichever axis the cursor travelled further along wins; the other is dropped.</summary>
        static void ResolveStraight(GridCoord anchor, int dx, int dz, List<GridCoord> into)
        {
            if (Mathf.Abs(dx) >= Mathf.Abs(dz)) Run(ref anchor, dx, 0, into);
            else Run(ref anchor, 0, dz, into);
        }

        /// <summary>
        /// Dominant axis first, then a single square corner onto the cursor. Leading with the
        /// dominant axis puts the corner nearer the cursor, which is what makes the elbow track the
        /// hand instead of snapping about while you drag.
        /// </summary>
        static void ResolveElbow(GridCoord anchor, int dx, int dz, List<GridCoord> into, bool preferHorizontalFirst)
        {
            bool horizontalFirst = Mathf.Abs(dx) != Mathf.Abs(dz)
                ? Mathf.Abs(dx) > Mathf.Abs(dz)
                : preferHorizontalFirst;

            if (horizontalFirst)
            {
                Run(ref anchor, dx, 0, into);
                Run(ref anchor, 0, dz, into);
            }
            else
            {
                Run(ref anchor, 0, dz, into);
                Run(ref anchor, dx, 0, into);
            }
        }

        /// <summary>
        /// The diagonal leg is as long as the shorter axis, so the straight leg covers exactly what
        /// is left and the path still lands on the cursor. A square drag is one clean diagonal, and a
        /// drag straight along an axis has no diagonal leg at all.
        /// </summary>
        static void ResolveDiagonalElbow(GridCoord anchor, int dx, int dz, List<GridCoord> into, bool diagonalFirst)
        {
            int diagonal = Mathf.Min(Mathf.Abs(dx), Mathf.Abs(dz));
            int sx = System.Math.Sign(dx);
            int sz = System.Math.Sign(dz);

            int remainingX = dx - sx * diagonal;
            int remainingZ = dz - sz * diagonal;

            if (diagonalFirst)
            {
                Run(ref anchor, sx * diagonal, sz * diagonal, into);
                Run(ref anchor, remainingX, remainingZ, into);
            }
            else
            {
                Run(ref anchor, remainingX, remainingZ, into);
                Run(ref anchor, sx * diagonal, sz * diagonal, into);
            }
        }

        /// <summary>
        /// Steps one cell at a time towards the offset, appending each cell it lands on. The offset
        /// must lie along an axis or exactly on a diagonal.
        /// </summary>
        static void Run(ref GridCoord from, int dx, int dz, List<GridCoord> into)
        {
            int steps = Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz));
            if (steps == 0) return;

            int sx = System.Math.Sign(dx);
            int sz = System.Math.Sign(dz);
            for (int i = 0; i < steps; i++)
            {
                from = new GridCoord(from.X + sx, from.Z + sz);
                into.Add(from);
            }
        }
    }
}
