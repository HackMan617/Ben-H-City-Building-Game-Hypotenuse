using System.Collections.Generic;
using CityBuilder.Grid;

namespace CityBuilder.Roads
{
    /// <summary>
    /// A committed network seen through a set of pending changes. The drag preview renders through
    /// this, so a ghost road shows the junctions it would really produce -- an existing straight run
    /// visibly becomes a T where the pending stroke meets it.
    /// </summary>
    public class RoadOverlay : IRoadQuery
    {
        readonly IRoadQuery _baseLayer;
        readonly HashSet<GridCoord> _added = new HashSet<GridCoord>();
        readonly HashSet<GridCoord> _removed = new HashSet<GridCoord>();
        readonly Dictionary<GridCoord, Dir8> _linked = new Dictionary<GridCoord, Dir8>();
        readonly Dictionary<GridCoord, Dir8> _unlinked = new Dictionary<GridCoord, Dir8>();

        public RoadOverlay(IRoadQuery baseLayer)
        {
            _baseLayer = baseLayer;
        }

        public IReadOnlyCollection<GridCoord> Added => _added;
        public IReadOnlyCollection<GridCoord> Removed => _removed;

        public void Reset()
        {
            _added.Clear();
            _removed.Clear();
            _linked.Clear();
            _unlinked.Clear();
        }

        /// <summary>Layers a whole planned edit on top of the base network.</summary>
        public void Apply(RoadEdit edit)
        {
            foreach (RoadLink link in edit.Unlinked) Unlink(link);
            foreach (GridCoord cell in edit.Removed) Remove(cell);
            foreach (GridCoord cell in edit.Added) Add(cell);
            foreach (RoadLink link in edit.Linked) Link(link);
        }

        public void Add(GridCoord cell)
        {
            _removed.Remove(cell);
            if (!_baseLayer.Has(cell)) _added.Add(cell);
        }

        public void Remove(GridCoord cell)
        {
            _added.Remove(cell);
            if (_baseLayer.Has(cell)) _removed.Add(cell);
        }

        public void Link(RoadLink link)
        {
            Dir8 back = Dir8Util.Opposite(link.Dir);
            ClearBit(_unlinked, link.Cell, link.Dir);
            ClearBit(_unlinked, link.Other, back);
            SetBit(_linked, link.Cell, link.Dir);
            SetBit(_linked, link.Other, back);
        }

        public void Unlink(RoadLink link)
        {
            Dir8 back = Dir8Util.Opposite(link.Dir);
            ClearBit(_linked, link.Cell, link.Dir);
            ClearBit(_linked, link.Other, back);
            SetBit(_unlinked, link.Cell, link.Dir);
            SetBit(_unlinked, link.Other, back);
        }

        public bool Has(GridCoord cell)
        {
            if (_removed.Contains(cell)) return false;
            return _added.Contains(cell) || _baseLayer.Has(cell);
        }

        public Dir8 MaskAt(GridCoord cell)
        {
            if (!Has(cell)) return Dir8.None;

            Dir8 mask = _baseLayer.MaskAt(cell);
            if (_unlinked.TryGetValue(cell, out Dir8 cut)) mask &= ~cut;
            if (_linked.TryGetValue(cell, out Dir8 extra)) mask |= extra;

            // A link cannot outlive the cell on its far end.
            foreach (Dir8 dir in Dir8Util.All)
            {
                if ((mask & dir) != 0 && !Has(Dir8Util.Step(cell, dir))) mask &= ~dir;
            }
            return mask;
        }

        static void SetBit(Dictionary<GridCoord, Dir8> map, GridCoord cell, Dir8 dir)
        {
            map.TryGetValue(cell, out Dir8 mask);
            map[cell] = mask | dir;
        }

        static void ClearBit(Dictionary<GridCoord, Dir8> map, GridCoord cell, Dir8 dir)
        {
            if (!map.TryGetValue(cell, out Dir8 mask)) return;

            Dir8 trimmed = mask & ~dir;
            if (trimmed == Dir8.None) map.Remove(cell);
            else map[cell] = trimmed;
        }
    }
}
