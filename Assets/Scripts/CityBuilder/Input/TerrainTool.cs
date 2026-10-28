using System;
using System.Collections.Generic;
using CityBuilder.Buildings;
using CityBuilder.Grid;
using CityBuilder.Roads;
using CityBuilder.View;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CityBuilder.BuildTools
{
    /// <summary>
    /// Drag a rectangle to raise or lower the terrain one level, then confirm.
    ///
    /// Cells under a road or building are left alone, so nothing ends up floating or buried, and
    /// cells already at the top or bottom level stay where they are.
    ///
    ///   left button   raise       right button  lower
    ///   Enter         apply       Escape        cancel
    /// </summary>
    public class TerrainTool : MonoBehaviour
    {
        [SerializeField] CityGrid _grid;
        [SerializeField] RoadSystem _roads;
        [SerializeField] BuildingBuildTool _buildings;
        [SerializeField] BuildingGhost _ghost;
        [SerializeField] Camera _camera;
        [SerializeField] BuildMenu _buildMenu;

        readonly List<GridCoord> _changed = new List<GridCoord>();
        readonly HashSet<GridCoord> _built = new HashSet<GridCoord>();

        GridCoord _corner;
        GridCoord _cursor;
        bool _dragging;
        bool _lowering;
        bool _awaitingConfirm;
        bool _wasActive;

        public bool IsDragging => _dragging;
        public bool IsAwaitingConfirm => _awaitingConfirm;
        public bool IsLowering => _lowering;

        /// <summary>Cells the pending edit would move.</summary>
        public int PendingCellCount => _changed.Count;

        /// <summary>Cells inside the rectangle that a road or building keeps from moving.</summary>
        public int BlockedCellCount { get; private set; }

        public Vector3 PendingEnd => _changed.Count > 0 ? _grid.CellToWorld(_changed[_changed.Count - 1]) : Vector3.zero;

        void Awake()
        {
            if (_grid == null) _grid = FindAnyObjectByType<CityGrid>();
            if (_roads == null) _roads = FindAnyObjectByType<RoadSystem>();
            if (_buildings == null) _buildings = FindAnyObjectByType<BuildingBuildTool>();
            if (_ghost == null) _ghost = FindAnyObjectByType<BuildingGhost>();
            if (_camera == null) _camera = Camera.main;
            if (_buildMenu == null) _buildMenu = FindAnyObjectByType<BuildMenu>();
        }

        void Update()
        {
            if (_grid == null) return;

            bool active = _buildMenu != null && _buildMenu.Active == BuildCategory.Terrain;

            if (_awaitingConfirm)
            {
                UpdateConfirm();
                return;
            }

            // The ghost is shared with the other tools, so only hide it on the frame this one loses focus.
            if (!active)
            {
                if (_wasActive) CancelDrag();
                _wasActive = false;
                return;
            }
            _wasActive = true;

            Mouse mouse = Mouse.current;
            if (mouse == null) return;

            bool onPlane = _grid.TryScreenToSurface(_camera, mouse.position.ReadValue(), out Vector3 world);
            GridCoord cursor = onPlane ? _grid.WorldToCell(world) : default;
            bool onGround = onPlane && _grid.InBounds(cursor);

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

            bool raise = mouse.leftButton.wasPressedThisFrame;
            bool lower = mouse.rightButton.wasPressedThisFrame;

            // Idle hover previews a raise, so it is always clear which cell a press would land on.
            _lowering = lower;
            _corner = cursor;
            _cursor = cursor;
            Plan();

            if (raise || lower) _dragging = true;
        }

        void UpdateDrag(Mouse mouse, bool onGround, GridCoord cursor)
        {
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                CancelDrag();
                if (_buildMenu != null) _buildMenu.EscapeConsumed = true;
                return;
            }

            if (onGround && !cursor.Equals(_cursor))
            {
                _cursor = cursor;
                Plan();
            }

            bool released = _lowering ? mouse.rightButton.wasReleasedThisFrame : mouse.leftButton.wasReleasedThisFrame;
            if (!released) return;

            if (_changed.Count == 0)
            {
                CancelDrag();
                return;
            }

            _dragging = false;
            _awaitingConfirm = true;
        }

        /// <summary>Works out which cells in the rectangle would move, and previews them.</summary>
        void Plan()
        {
            _built.Clear();
            if (_buildings != null)
            {
                foreach (PlacedBuilding building in _buildings.Placed)
                {
                    foreach (GridCoord cell in building.Cells) _built.Add(cell);
                }
            }

            int minX = Math.Min(_corner.X, _cursor.X);
            int maxX = Math.Max(_corner.X, _cursor.X);
            int minZ = Math.Min(_corner.Z, _cursor.Z);
            int maxZ = Math.Max(_corner.Z, _cursor.Z);

            TerrainMap terrain = _grid.Terrain;
            int delta = _lowering ? -1 : 1;
            int lowest = int.MaxValue;
            int highest = int.MinValue;

            _changed.Clear();
            BlockedCellCount = 0;
            for (int z = minZ; z <= maxZ; z++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    var cell = new GridCoord(x, z);
                    if (!_grid.InBounds(cell)) continue;

                    if ((_roads != null && _roads.Network.Has(cell)) || _built.Contains(cell))
                    {
                        BlockedCellCount++;
                        continue;
                    }

                    int level = terrain.LevelAt(cell);
                    int next = level + delta;
                    if (next < 0 || next > TerrainMap.MaxLevel) continue;

                    _changed.Add(cell);
                    lowest = Math.Min(lowest, Math.Min(level, next));
                    highest = Math.Max(highest, Math.Max(level, next));
                }
            }

            if (_changed.Count == 0)
            {
                _ghost?.Hide();
                return;
            }

            // Raising shows the new ground in green; lowering shows, in red, the slice to be cut away.
            float bottom = _lowering ? _grid.HeightOf(lowest) : 0f;
            float top = _grid.HeightOf(highest);
            _ghost?.Show(_changed, _grid.CellSize, top - bottom, !_lowering, bottom);
        }

        void UpdateConfirm()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return;

            if (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame)
                ConfirmPending();
            else if (keyboard.escapeKey.wasPressedThisFrame)
            {
                CancelPending();
                if (_buildMenu != null) _buildMenu.EscapeConsumed = true;
            }
        }

        public void ConfirmPending()
        {
            if (!_awaitingConfirm) return;
            _grid.Terrain.Shift(_changed, _lowering ? -1 : 1);
            CancelDrag();
        }

        public void CancelPending()
        {
            if (!_awaitingConfirm) return;
            CancelDrag();
        }

        void CancelDrag()
        {
            _dragging = false;
            _awaitingConfirm = false;
            _changed.Clear();
            BlockedCellCount = 0;
            _ghost?.Hide();
        }
    }
}
