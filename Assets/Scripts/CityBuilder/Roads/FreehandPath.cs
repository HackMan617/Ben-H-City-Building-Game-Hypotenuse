using System;
using System.Collections.Generic;
using CityBuilder.Grid;
using UnityEngine;

namespace CityBuilder.Roads
{
    /// <summary>
    /// Builds a stroke by following the pointer, one straight or diagonal step at a time.
    ///
    /// Steps are chosen from the pointer's direction rather than from which cells it passes over.
    /// A hand moving at 45 degrees almost never crosses exactly through a cell corner, so tracing
    /// cells would turn every diagonal into a staircase of right angles. Instead the path waits until
    /// the pointer is most of a cell away from its end, then steps whichever of the eight ways points
    /// at it -- so a diagonal drag lays a diagonal, and a drag that runs east then turns north keeps a
    /// squared corner.
    ///
    /// Everything is in grid units: cell (x, z) spans x..x+1, z..z+1.
    /// </summary>
    public class FreehandPath
    {
        /// <summary>
        /// How far the pointer must get from the end cell's centre before the path steps. Past the
        /// half-diagonal (0.71) so a 45-degree drag is already inside the diagonal neighbour.
        /// </summary>
        public const float StepDistance = 0.8f;

        /// <summary>
        /// The path keeps its current heading while the pointer stays within this many degrees of
        /// it, so a wobbly hand on a straight run does not sprout zigzags.
        /// </summary>
        public const float KeepHeadingDegrees = 30f;

        // A pointer that jumps across the map in one frame still fills in, but never unboundedly.
        const int MaxStepsPerFollow = 1024;

        readonly List<GridCoord> _cells = new List<GridCoord>();
        Dir8 _heading;

        public IReadOnlyList<GridCoord> Cells => _cells;

        public void Begin(GridCoord anchor)
        {
            _cells.Clear();
            _cells.Add(anchor);
            _heading = Dir8.None;
        }

        public void Clear()
        {
            _cells.Clear();
            _heading = Dir8.None;
        }

        /// <summary>
        /// Extends the path towards <paramref name="point"/>. Heading back onto the previous cell
        /// takes the last one off instead, so the stroke can be reeled in mid-drag. Steps that fail
        /// <paramref name="inBounds"/> are refused and the path waits at the edge. Returns true when
        /// the path changed.
        /// </summary>
        public bool Follow(Vector2 point, Func<GridCoord, bool> inBounds = null)
        {
            if (_cells.Count == 0) return false;

            bool changed = false;
            for (int i = 0; i < MaxStepsPerFollow; i++)
            {
                GridCoord end = _cells[_cells.Count - 1];
                Vector2 offset = point - Centre(end);
                if (offset.magnitude < StepDistance) break;

                Dir8 dir = Heading(offset);
                GridCoord next = Dir8Util.Step(end, dir);

                if (_cells.Count >= 2 && next == _cells[_cells.Count - 2])
                {
                    _cells.RemoveAt(_cells.Count - 1);
                    _heading = _cells.Count >= 2
                        ? Dir8Util.FromDelta(_cells[_cells.Count - 2], _cells[_cells.Count - 1])
                        : Dir8.None;
                    changed = true;
                    continue;
                }

                if (inBounds != null && !inBounds(next)) break;

                _cells.Add(next);
                _heading = dir;
                changed = true;
            }
            return changed;
        }

        /// <summary>The current heading while the offset stays near it, else the nearest of the eight.</summary>
        Dir8 Heading(Vector2 offset)
        {
            if (_heading != Dir8.None && Vector2.Angle(offset, Unit(_heading)) <= KeepHeadingDegrees) return _heading;

            // Bit order runs clockwise from north; the angle here runs clockwise from north too.
            float degrees = Mathf.Atan2(offset.x, offset.y) * Mathf.Rad2Deg;
            int index = ((Mathf.RoundToInt(degrees / 45f) % 8) + 8) % 8;
            return Dir8Util.All[index];
        }

        static Vector2 Centre(GridCoord cell) => new Vector2(cell.X + 0.5f, cell.Z + 0.5f);

        static Vector2 Unit(Dir8 dir)
        {
            GridCoord o = Dir8Util.Offset(dir);
            return new Vector2(o.X, o.Z).normalized;
        }
    }
}
