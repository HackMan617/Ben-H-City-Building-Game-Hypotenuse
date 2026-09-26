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
    /// Press to start, drag, release to commit. The road follows the pointer, straight or diagonal,
    /// and backing up over it reels it in -- see <see cref="FreehandPath"/>.
    ///
    ///   left button   draw        right button  erase
    ///   Ctrl+Z        undo        Ctrl+Y        redo
    ///   Escape        cancel the stroke in progress
    /// </summary>
    public class RoadBuildTool : MonoBehaviour
    {
        [SerializeField] CityGrid _grid;
        [SerializeField] RoadSystem _roads;
        [SerializeField] RoadGhost _ghost;
        [SerializeField] Camera _camera;

        readonly List<GridCoord> _path = new List<GridCoord>();
        readonly RoadEdit _preview = new RoadEdit();
        readonly FreehandPath _trace = new FreehandPath();

        bool _dragging;
        bool _erasing;

        public bool IsDragging => _dragging;

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
                // The trace follows the pointer even past the edge, stopping at the last cell in bounds.
                UpdateTrace(onPlane, world);
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
                _trace.Begin(cursor);
                _path.Clear();
                _path.AddRange(_trace.Cells);
                ShowGhost();
                return;
            }

            // Idle hover: a single-cell ghost so it is always clear which cell a press would land on.
            _path.Clear();
            _path.Add(cursor);
            _erasing = false;
            ShowGhost();
        }

        /// <summary>Extends or reels in the traced path as the pointer moves.</summary>
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
