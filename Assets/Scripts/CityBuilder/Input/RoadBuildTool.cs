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
    /// Press to drop an anchor, drag to aim, release to commit. The run between anchor and cursor is
    /// snapped to a legal shape rather than tracing the pointer, which is what gives the tool its
    /// deliberate feel -- see <see cref="RoadPathResolver"/>.
    ///
    ///   left button   draw        right button  erase
    ///   F             flip which leg of a diagonal elbow comes first
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
        [SerializeField] PathMode _pathMode = PathMode.ElbowWithDiagonal;

        readonly List<GridCoord> _path = new List<GridCoord>();
        readonly RoadEdit _preview = new RoadEdit();

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

            bool onGround = _grid.TryScreenToCell(_camera, mouse.position.ReadValue(), out GridCoord cursor);

            if (_dragging) UpdateDrag(mouse, onGround, cursor);
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

        void UpdateDrag(Mouse mouse, bool onGround, GridCoord cursor)
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

            bool released = _erasing
                ? mouse.rightButton.wasReleasedThisFrame
                : mouse.leftButton.wasReleasedThisFrame;

            if (!released) return;

            ResolvePath(_lastCursor);
            _roads.ApplyStroke(_path, _erasing);

            _dragging = false;
            _path.Clear();
            _preview.Clear();
            _ghost?.Hide();
        }

        void ResolvePath(GridCoord cursor)
        {
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
