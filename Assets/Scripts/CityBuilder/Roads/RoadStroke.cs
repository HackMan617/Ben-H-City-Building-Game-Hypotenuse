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

        /// <summary>
        /// True when applying <paramref name="edit"/> would leave a 45-degree road overlapping road
        /// it is not joined to. Blocked strokes are previewed but never committed.
        ///
        /// A diagonal step from a to b is wide enough to spill over the corners of the other two
        /// cells in its 2x2 block, c and d. Road in c or d is fine when it is joined to a or b -- that
        /// is a junction, and the overlap reads as one -- but anything else would merge visually
        /// with the diagonal without being connected to it. That covers two parallel diagonals one
        /// cell apart as well as a diagonal brushing past the corner of a straight road.
        ///
        ///     d  b        a-b  the diagonal
        ///     a  c        c, d the cells whose corners it covers
        ///
        /// Only diagonals the edit could have affected are checked, so a stroke is never blocked by
        /// something elsewhere on the map. Erasing only ever removes overlaps, so it is never blocked.
        /// </summary>
        public static bool IsBlocked(IRoadQuery network, RoadEdit edit)
        {
            if (edit.Added.Count == 0 && edit.Linked.Count == 0) return false;

            if (_result == null || _resultBase != network)
            {
                _result = new RoadOverlay(network);
                _resultBase = network;
            }
            _result.Reset();
            _result.Apply(edit);

            // New diagonals, plus existing diagonals whose block a new cell has just moved into.
            Candidates.Clear();
            foreach (RoadLink link in edit.Linked)
            {
                if (link.IsDiagonal) Candidates.Add(link);
            }
            foreach (GridCoord cell in edit.Added)
            {
                for (int dx = -1; dx <= 0; dx++)
                for (int dz = -1; dz <= 0; dz++)
                {
                    var corner = new GridCoord(cell.X + dx, cell.Z + dz);
                    var rising = new RoadLink(corner, Dir8.NE);
                    var falling = new RoadLink(new GridCoord(corner.X + 1, corner.Z), Dir8.NW);
                    if (rising.IsIn(_result)) Candidates.Add(rising);
                    if (falling.IsIn(_result)) Candidates.Add(falling);
                }
            }

            foreach (RoadLink diagonal in Candidates)
            {
                if (Overlaps(_result, diagonal)) return true;
            }
            return false;
        }

        static RoadOverlay _result;
        static IRoadQuery _resultBase;
        static readonly HashSet<RoadLink> Candidates = new HashSet<RoadLink>();

        static bool Overlaps(IRoadQuery query, RoadLink diagonal)
        {
            GridCoord a = diagonal.Cell;
            GridCoord b = diagonal.Other;
            return Intrudes(query, new GridCoord(b.X, a.Z), a, b) || Intrudes(query, new GridCoord(a.X, b.Z), a, b);
        }

        static bool Intrudes(IRoadQuery query, GridCoord cell, GridCoord a, GridCoord b) =>
            query.Has(cell) && !Joined(query, cell, a) && !Joined(query, cell, b);

        static bool Joined(IRoadQuery query, GridCoord x, GridCoord y) =>
            RoadLink.TryBetween(x, y, out RoadLink link) && link.IsIn(query);

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
