using System.Collections.Generic;
using CityBuilder.Grid;

namespace CityBuilder.Roads
{
    /// <summary>
    /// One undoable stroke. Stores only what actually changed -- cells and links -- so redrawing
    /// over existing road is a no-op rather than a history entry that appears to do nothing.
    /// </summary>
    public class RoadEdit
    {
        public readonly List<GridCoord> Added = new List<GridCoord>();
        public readonly List<GridCoord> Removed = new List<GridCoord>();
        public readonly List<RoadLink> Linked = new List<RoadLink>();
        public readonly List<RoadLink> Unlinked = new List<RoadLink>();

        public bool IsEmpty => Added.Count == 0 && Removed.Count == 0 && Linked.Count == 0 && Unlinked.Count == 0;

        public void Clear()
        {
            Added.Clear();
            Removed.Clear();
            Linked.Clear();
            Unlinked.Clear();
        }

        // Cells go in before the links that need them, and links come out before their cells, so
        // both directions replay cleanly.

        public void Apply(RoadNetwork network)
        {
            foreach (RoadLink link in Unlinked) network.Unlink(link);
            foreach (GridCoord cell in Removed) network.Remove(cell);
            foreach (GridCoord cell in Added) network.Add(cell);
            foreach (RoadLink link in Linked) network.Link(link);
        }

        public void Revert(RoadNetwork network)
        {
            foreach (RoadLink link in Linked) network.Unlink(link);
            foreach (GridCoord cell in Added) network.Remove(cell);
            foreach (GridCoord cell in Removed) network.Add(cell);
            foreach (RoadLink link in Unlinked) network.Link(link);
        }
    }

    /// <summary>Stroke-level undo/redo. Redo is discarded as soon as a new stroke is committed.</summary>
    public class EditHistory
    {
        readonly List<RoadEdit> _undo = new List<RoadEdit>();
        readonly List<RoadEdit> _redo = new List<RoadEdit>();
        readonly int _limit;

        public EditHistory(int limit = 256)
        {
            _limit = UnityEngine.Mathf.Max(1, limit);
        }

        public bool CanUndo => _undo.Count > 0;
        public bool CanRedo => _redo.Count > 0;

        public void Record(RoadEdit edit)
        {
            if (edit == null || edit.IsEmpty) return;

            _undo.Add(edit);
            _redo.Clear();
            if (_undo.Count > _limit) _undo.RemoveAt(0);
        }

        public RoadEdit Undo(RoadNetwork network)
        {
            if (_undo.Count == 0) return null;

            RoadEdit edit = _undo[_undo.Count - 1];
            _undo.RemoveAt(_undo.Count - 1);
            edit.Revert(network);
            _redo.Add(edit);
            return edit;
        }

        public RoadEdit Redo(RoadNetwork network)
        {
            if (_redo.Count == 0) return null;

            RoadEdit edit = _redo[_redo.Count - 1];
            _redo.RemoveAt(_redo.Count - 1);
            edit.Apply(network);
            _undo.Add(edit);
            return edit;
        }

        public void Clear()
        {
            _undo.Clear();
            _redo.Clear();
        }
    }
}
