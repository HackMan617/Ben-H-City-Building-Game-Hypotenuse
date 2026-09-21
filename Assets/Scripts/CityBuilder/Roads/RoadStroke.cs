using System;
using System.Collections.Generic;
using CityBuilder.Grid;

namespace CityBuilder.Roads
{
    /// <summary>
    /// Turns a resolved drag path into the edit it would make. The preview and the commit both go
    /// through here, so what the ghost shows is exactly what release will build.
    /// </summary>
    public static class RoadStroke
    {
        static readonly HashSet<GridCoord> SeenCells = new HashSet<GridCoord>();
        static readonly HashSet<RoadLink> SeenLinks = new HashSet<RoadLink>();

        /// <summary>
        /// Fills <paramref name="into"/> with the delta a stroke along <paramref name="path"/> makes
        /// to <paramref name="network"/>. Cells failing <paramref name="inBounds"/> are skipped, and
        /// no link is made across the gap they leave.
        /// </summary>
        public static void Plan(IRoadQuery network, IReadOnlyList<GridCoord> path, bool erase, RoadEdit into,
                                Func<GridCoord, bool> inBounds = null)
        {
            into.Clear();
            SeenCells.Clear();
            SeenLinks.Clear();
            if (path == null) return;

            if (erase) PlanErase(network, path, into, inBounds);
            else PlanBuild(network, path, into, inBounds);
        }

        /// <summary>Erasing a cell takes every link touching it; they are recorded so undo can rejoin them.</summary>
        static void PlanErase(IRoadQuery network, IReadOnlyList<GridCoord> path, RoadEdit into,
                              Func<GridCoord, bool> inBounds)
        {
            foreach (GridCoord cell in path)
            {
                if (inBounds != null && !inBounds(cell)) continue;
                if (!network.Has(cell) || !SeenCells.Add(cell)) continue;

                into.Removed.Add(cell);

                Dir8 mask = network.MaskAt(cell);
                foreach (Dir8 dir in Dir8Util.All)
                {
                    if ((mask & dir) == 0) continue;
                    var link = new RoadLink(cell, dir);
                    if (SeenLinks.Add(link)) into.Unlinked.Add(link);
                }
            }
        }

        /// <summary>
        /// Every cell on the path becomes road and each step joins the cell before it, so a stroke
        /// that runs into existing road forms a junction there and one laid alongside does not.
        /// </summary>
        static void PlanBuild(IRoadQuery network, IReadOnlyList<GridCoord> path, RoadEdit into,
                              Func<GridCoord, bool> inBounds)
        {
            for (int i = 0; i < path.Count; i++)
            {
                GridCoord cell = path[i];
                if (inBounds != null && !inBounds(cell)) continue;

                AddCell(network, cell, into);
                if (i == 0) continue;

                GridCoord previous = path[i - 1];
                if (inBounds != null && !inBounds(previous)) continue;
                if (!RoadLink.TryBetween(previous, cell, out RoadLink step)) continue;

                AddLink(network, step, into);
                if (step.IsDiagonal) JoinCornerCrossing(network, previous, cell, into);
            }
        }

        /// <summary>
        /// A diagonal step from <paramref name="a"/> to <paramref name="b"/> crosses the other
        /// diagonal of the same 2x2 block at the shared cell corner. When that other diagonal is
        /// already road, the two would cross with no cell in common -- overlapping but unconnected.
        /// Instead, all four cells become road and join around the block, so the crossing is a
        /// real junction.
        /// </summary>
        static void JoinCornerCrossing(IRoadQuery network, GridCoord a, GridCoord b, RoadEdit into)
        {
            var c = new GridCoord(b.X, a.Z);
            var d = new GridCoord(a.X, b.Z);
            if (!RoadLink.TryBetween(c, d, out RoadLink crossing)) return;
            if (!IsLinked(network, crossing)) return;

            AddCell(network, c, into);
            AddCell(network, d, into);
            AddLink(network, a, c, into);
            AddLink(network, c, b, into);
            AddLink(network, b, d, into);
            AddLink(network, d, a, into);
        }

        static bool IsLinked(IRoadQuery network, RoadLink link) => link.IsIn(network) || SeenLinks.Contains(link);

        static void AddCell(IRoadQuery network, GridCoord cell, RoadEdit into)
        {
            if (!network.Has(cell) && SeenCells.Add(cell)) into.Added.Add(cell);
        }

        static void AddLink(IRoadQuery network, GridCoord a, GridCoord b, RoadEdit into)
        {
            if (RoadLink.TryBetween(a, b, out RoadLink link)) AddLink(network, link, into);
        }

        static void AddLink(IRoadQuery network, RoadLink link, RoadEdit into)
        {
            if (link.IsIn(network) || !SeenLinks.Add(link)) return;
            into.Linked.Add(link);
        }
    }
}
