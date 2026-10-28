using System;
using System.Collections.Generic;

namespace CityBuilder.Grid
{
    /// <summary>
    /// How high each cell stands, in whole levels: 0 is the ground plane, <see cref="MaxLevel"/> the
    /// top. Cells step straight from one level to the next, like terraces, so a cell is always flat
    /// and roads and buildings on it need no slope handling.
    ///
    /// Only raised cells are stored, so an untouched map costs nothing.
    /// </summary>
    public sealed class TerrainMap
    {
        public const int MaxLevel = 8;

        readonly Dictionary<GridCoord, int> _levels = new Dictionary<GridCoord, int>();

        /// <summary>Raised after any change, once per batch, so the view can remesh.</summary>
        public event Action Changed;

        public int Count => _levels.Count;

        /// <summary>Every cell above level 0, with its level.</summary>
        public IEnumerable<KeyValuePair<GridCoord, int>> Raised => _levels;

        public int Highest
        {
            get
            {
                int highest = 0;
                foreach (int level in _levels.Values) highest = Math.Max(highest, level);
                return highest;
            }
        }

        public int LevelAt(GridCoord cell) => _levels.TryGetValue(cell, out int level) ? level : 0;

        /// <summary>Moves every cell in <paramref name="cells"/> up or down by <paramref name="delta"/> levels, within 0..MaxLevel.</summary>
        public void Shift(IEnumerable<GridCoord> cells, int delta)
        {
            foreach (GridCoord cell in cells) Write(cell, LevelAt(cell) + delta);
            Changed?.Invoke();
        }

        /// <summary>Swaps in a whole new map, as when a save is loaded.</summary>
        public void Replace(IEnumerable<KeyValuePair<GridCoord, int>> levels)
        {
            _levels.Clear();
            foreach (KeyValuePair<GridCoord, int> pair in levels) Write(pair.Key, pair.Value);
            Changed?.Invoke();
        }

        public void Clear()
        {
            _levels.Clear();
            Changed?.Invoke();
        }

        void Write(GridCoord cell, int level)
        {
            level = Math.Clamp(level, 0, MaxLevel);
            if (level == 0) _levels.Remove(cell);
            else _levels[cell] = level;
        }
    }
}
