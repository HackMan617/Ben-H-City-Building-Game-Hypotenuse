using System.Collections.Generic;
using CityBuilder.Grid;

namespace CityBuilder.Roads
{
    /// <summary>
    /// The committed road layout: which cells hold road, and which neighbours each one is joined to.
    ///
    /// Every link is explicit and comes from a stroke. Links used to be derived from occupancy, but
    /// once 45-degree roads exist that rule sprouts stubs wherever a diagonal passes beside a
    /// straight road, so two roads now join only where a stroke actually runs into the other.
    /// </summary>
    public class RoadNetwork : IRoadQuery
    {
        readonly HashSet<GridCoord> _cells = new HashSet<GridCoord>();
        readonly Dictionary<GridCoord, Dir8> _links = new Dictionary<GridCoord, Dir8>();

        public IReadOnlyCollection<GridCoord> Cells => _cells;
        public int Count => _cells.Count;

        public bool Has(GridCoord cell) => _cells.Contains(cell);

        public Dir8 MaskAt(GridCoord cell) => _links.TryGetValue(cell, out Dir8 mask) ? mask : Dir8.None;

        public bool HasLink(RoadLink link) => link.IsIn(this);

        /// <summary>Adds a cell with no connections. Returns false when it was already road.</summary>
        public bool Add(GridCoord cell) => _cells.Add(cell);

        /// <summary>Removes a cell and every link touching it. Returns false when it was empty.</summary>
        public bool Remove(GridCoord cell)
        {
            if (!_cells.Remove(cell)) return false;

            Dir8 mask = MaskAt(cell);
            _links.Remove(cell);

            foreach (Dir8 dir in Dir8Util.All)
            {
                if ((mask & dir) != 0) ClearBit(Dir8Util.Step(cell, dir), Dir8Util.Opposite(dir));
            }
            return true;
        }

        /// <summary>
        /// Joins two neighbouring cells. Both must already be road. Returns false when either is
        /// missing or the link already exists.
        /// </summary>
        public bool Link(RoadLink link)
        {
            if (!_cells.Contains(link.Cell) || !_cells.Contains(link.Other)) return false;
            if (HasLink(link)) return false;

            SetBit(link.Cell, link.Dir);
            SetBit(link.Other, Dir8Util.Opposite(link.Dir));
            return true;
        }

        /// <summary>Breaks a link, leaving both cells in place. Returns false when there was none.</summary>
        public bool Unlink(RoadLink link)
        {
            if (!HasLink(link)) return false;

            ClearBit(link.Cell, link.Dir);
            ClearBit(link.Other, Dir8Util.Opposite(link.Dir));
            return true;
        }

        public void Clear()
        {
            _cells.Clear();
            _links.Clear();
        }

        void SetBit(GridCoord cell, Dir8 dir)
        {
            _links.TryGetValue(cell, out Dir8 mask);
            _links[cell] = mask | dir;
        }

        void ClearBit(GridCoord cell, Dir8 dir)
        {
            if (!_links.TryGetValue(cell, out Dir8 mask)) return;

            Dir8 trimmed = mask & ~dir;
            if (trimmed == Dir8.None) _links.Remove(cell);
            else _links[cell] = trimmed;
        }
    }
}
