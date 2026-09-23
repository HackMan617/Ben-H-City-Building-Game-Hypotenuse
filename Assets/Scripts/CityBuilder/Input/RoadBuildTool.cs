using System.Collections.Generic;
using CityBuilder.Grid;
using CityBuilder.Roads;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CityBuilder.BuildTools
{
    /// <summary>
    /// Click-drag road drawing.
    ///
    /// Press to drop an anchor, drag, release to commit. In freehand mode the road follows the
    /// pointer, straight or diagonal, and backing up over it reels it in -- see
    /// <see cref="FreehandPath"/>. The other modes snap the run between anchor and cursor to a
    /// fixed shape instead -- see <see cref="RoadPathResolver"/>.
    ///
    ///   left button   draw        right button  erase
    ///   F             flip which leg of a diagonal elbow comes first (elbow mode only)
    ///   Ctrl+Z        undo        Ctrl+Y        redo
    ///   Escape        cancel the stroke in progress
    /// </summary>
    public class RoadBuildTool : MonoBehaviour
    {
        [SerializeField] CityGrid _grid;
        [SerializeField] RoadSystem _roads;
        [SerializeField] RoadGhost _ghost;
        [SerializeField] Camera _camera;

        [Tooltip("How a drag between two cells is resolved.")]
        [SerializeField] PathMode _pathMode = PathMode.Freehand;

        readonly List<GridCoord> _path = new List<GridCoord>();
        readonly RoadEdit _preview = new RoadEdit();
        readonly FreehandPath _trace = new FreehandPath();

        bool _dragging;
        bool _erasing;
        GridCoord _anchor;
        GridCoord _lastCursor;

        // Remembered so a perfectly square drag does not flip its elbow back and forth under the hand.
        bool _preferHorizontalFirst = true;

        // Diagonal leg leaves the anchor unless F has flipped it for this stroke.
        bool _diagonalFirst = true;

        public PathMode PathMode
        {
            get => _pathMode;
            set => _pathMode = value;
        }

        public bool IsDragging => _dragging;
        public bool IsFreehand => _pathMode == PathMode.Freehand;
        public bool DiagonalFirst => _diagonalFirst;

        /// <summary>True when the stroke in progress overlaps road it does not join and cannot be built.</summary>
        public bool IsBlocked { get; private set; }

        /// <summary>Cells the stroke in progress would add, or remove when erasing.</summary>
        public int PendingCellCount => !_dragging ? 0 : _erasing ? _preview.Removed.Count : _preview.Added.Count;

        void Awake()
        {
            if (_grid == null) _grid = FindAnyObjectByType<CityGrid>();
            if (_roads == null) _roads = FindAnyObjectByType<RoadSystem>();
            if (_ghost == null) _ghost = FindAnyObjectByType<RoadGhost>();
            if (_camera == null) _camera = Camera.main;
        }

        void Update()
        {
            if (_grid == null || _roads == null) return;

            HandleHistoryKeys();

            Mouse mouse = Mouse.current;
            if (mouse == null) return;

            bool onPlane = _grid.TryScreenToWorld(_camera, mouse.position.ReadValue(), out Vector3 world);
            GridCoord cursor = onPlane ? _grid.WorldToCell(world) : default;
            bool onGround = onPlane && _grid.InBounds(cursor);

            if (_dragging)
            {
                // Freehand follows the pointer even past the edge, stopping at the last cell in bounds.
                if (IsFreehand) UpdateTrace(onPlane, world);
                else UpdateSnapped(onGround, cursor);
                UpdateRelease(mouse);
            }
            else UpdateIdle(mouse, onGround, cursor);
        }

        void UpdateIdle(Mouse mouse, bool onGround, GridCoord cursor)
        {
            if (!onGround)
            {
                _ghost?.Hide();
                return;
            }

            bool build = mouse.leftButton.wasPressedThisFrame;
            bool erase = mouse.rightButton.wasPressedThisFrame;

            if (build || erase)
            {
                _dragging = true;
                _erasing = erase;
                _anchor = cursor;
                _lastCursor = cursor;
                _preferHorizontalFirst = true;
                _diagonalFirst = true;
                _trace.Begin(cursor);
                ResolvePath(cursor);
                ShowGhost();
                return;
            }

            // Idle hover: a single-cell ghost so it is always clear which cell a press would land on.
            _path.Clear();
            _path.Add(cursor);
            _erasing = false;
            ShowGhost();
        }

        /// <summary>Freehand: extend or reel in the traced path as the pointer moves.</summary>
        void UpdateTrace(bool onPlane, Vector3 world)
        {
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                CancelDrag();
                return;
            }

            // Off the ground plane (cursor above the horizon) the path simply stops growing.
            if (!onPlane) return;

            var point = new Vector2(world.x, world.z) / _grid.CellSize;
            if (!_trace.Follow(point, _grid.InBounds)) return;

            _path.Clear();
            _path.AddRange(_trace.Cells);
            ShowGhost();
        }

        /// <summary>Elbow and straight modes: re-snap the whole run from the anchor to the cursor cell.</summary>
        void UpdateSnapped(bool onGround, GridCoord cursor)
        {
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                CancelDrag();
                return;
            }

            bool flip = Keyboard.current != null && Keyboard.current.fKey.wasPressedThisFrame;
            if (flip) _diagonalFirst = !_diagonalFirst;

            // Off the ground plane (cursor above the horizon) the last valid cell is held, so the
            // preview freezes rather than snapping somewhere absurd.
            bool moved = onGround && cursor != _lastCursor;
            if (moved) _lastCursor = cursor;

            if (moved || flip)
            {
                ResolvePath(_lastCursor);
                ShowGhost();
            }
        }

        /// <summary>Commits whatever the ghost is showing once the button that started the drag lets go.</summary>
        void UpdateRelease(Mouse mouse)
        {
            if (!_dragging) return;

            bool released = _erasing
                ? mouse.rightButton.wasReleasedThisFrame
                : mouse.leftButton.wasReleasedThisFrame;

            if (!released) return;

            _roads.ApplyStroke(_path, _erasing);

            _dragging = false;
            _path.Clear();
            _preview.Clear();
            _trace.Clear();
            _ghost?.Hide();
        }

        void ResolvePath(GridCoord cursor)
        {
            if (IsFreehand)
            {
                _path.Clear();
                _path.AddRange(_trace.Cells);
                return;
            }

            int dx = cursor.X - _anchor.X;
            int dz = cursor.Z - _anchor.Z;

            // Only re-commit to an axis when the drag is not square; on a tie the previous choice holds.
            if (Mathf.Abs(dx) != Mathf.Abs(dz)) _preferHorizontalFirst = Mathf.Abs(dx) > Mathf.Abs(dz);

            RoadPathResolver.Resolve(_anchor, cursor, _pathMode, _path, _preferHorizontalFirst, _diagonalFirst);

            // Trim anything the stroke would push outside the buildable area.
            for (int i = _path.Count - 1; i >= 0; i--)
            {
                if (!_grid.InBounds(_path[i])) _path.RemoveAt(i);
            }
        }

        void ShowGhost()
        {
            RoadStroke.Plan(_roads.Network, _path, _erasing, _preview);
            IsBlocked = !_erasing && RoadStroke.IsBlocked(_roads.Network, _preview);
            _ghost?.Show(_roads.Network, _preview, _grid.Profile, _erasing, IsBlocked);
        }

        void CancelDrag()
        {
            _dragging = false;
            _path.Clear();
            _preview.Clear();
            _trace.Clear();
            _ghost?.Hide();
        }

        void HandleHistoryKeys()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return;

            bool ctrl = keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed ||
                        keyboard.leftCommandKey.isPressed || keyboard.rightCommandKey.isPressed;
            if (!ctrl) return;

            bool shift = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;

            if (keyboard.zKey.wasPressedThisFrame)
            {
                if (shift) _roads.Redo();
                else _roads.Undo();
            }
            else if (keyboard.yKey.wasPressedThisFrame)
            {
                _roads.Redo();
            }
        }
    }
}
