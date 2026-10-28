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
    /// Drag a rectangle to mark cells for demolition, then confirm. Erases roads on every cell
    /// inside the rectangle, and every building with any cell inside it.
    /// </summary>
    public class DemolishTool : MonoBehaviour
    {
        [SerializeField] CityGrid _grid;
        [SerializeField] RoadSystem _roads;
        [SerializeField] RoadGhost _ghost;
        [SerializeField] Camera _camera;
        [SerializeField] BuildMenu _buildMenu;
        [SerializeField] BuildingBuildTool _buildings;
        [SerializeField] BuildingGhost _buildingGhost;

        readonly List<GridCoord> _cells = new List<GridCoord>();
        readonly RoadEdit _preview = new RoadEdit();
        readonly List<PlacedBuilding> _doomed = new List<PlacedBuilding>();
        readonly List<GridCoord> _doomedCells = new List<GridCoord>();

        GridCoord _corner;
        GridCoord _cursor;
        bool _dragging;
        bool _awaitingConfirm;

        public bool IsDragging => _dragging;
        public bool IsAwaitingConfirm => _awaitingConfirm;
        public int PendingCellCount => _preview.Removed.Count;
        public int PendingBuildingCount => _doomed.Count;

        public Vector3 PendingEnd =>
            _cells.Count > 0 ? _grid.CellToWorld(_cells[_cells.Count - 1]) : Vector3.zero;

        void Awake()
        {
            if (_grid == null) _grid = FindAnyObjectByType<CityGrid>();
            if (_roads == null) _roads = FindAnyObjectByType<RoadSystem>();
            if (_ghost == null) _ghost = FindAnyObjectByType<RoadGhost>();
            if (_camera == null) _camera = Camera.main;
            if (_buildMenu == null) _buildMenu = FindAnyObjectByType<BuildMenu>();
            if (_buildings == null) _buildings = FindAnyObjectByType<BuildingBuildTool>();
            if (_buildingGhost == null) _buildingGhost = FindAnyObjectByType<BuildingGhost>();
        }

        void Update()
        {
            if (_grid == null || _roads == null) return;

            bool active = _buildMenu != null && _buildMenu.Active == BuildCategory.Demolish;

            if (_awaitingConfirm)
            {
                UpdateConfirm();
                return;
            }

            if (!active)
            {
                if (_dragging) CancelDrag();
                return;
            }

            Mouse mouse = Mouse.current;
            if (mouse == null) return;

            bool onPlane = _grid.TryScreenToSurface(_camera, mouse.position.ReadValue(), out Vector3 world);
            GridCoord cursor = onPlane ? _grid.WorldToCell(world) : default;
            bool onGround = onPlane && _grid.InBounds(cursor);

            if (_dragging)
            {
                UpdateDrag(mouse, onGround, cursor);
            }
            else
            {
                UpdateIdle(mouse, onGround, cursor);
            }
        }

        void UpdateIdle(Mouse mouse, bool onGround, GridCoord cursor)
        {
            if (!onGround)
            {
                HideGhosts();
                return;
            }

            if (mouse.leftButton.wasPressedThisFrame)
            {
                _dragging = true;
                _corner = cursor;
                _cursor = cursor;
                RebuildRect();
                ShowGhost();
                return;
            }

            _cells.Clear();
            _cells.Add(cursor);
            ShowGhost();
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
                RebuildRect();
                ShowGhost();
            }

            if (mouse.leftButton.wasReleasedThisFrame)
            {
                if (_preview.Removed.Count == 0 && _doomed.Count == 0)
                {
                    CancelDrag();
                    return;
                }

                _dragging = false;
                _awaitingConfirm = true;
            }
        }

        void RebuildRect()
        {
            int minX = Math.Min(_corner.X, _cursor.X);
            int maxX = Math.Max(_corner.X, _cursor.X);
            int minZ = Math.Min(_corner.Z, _cursor.Z);
            int maxZ = Math.Max(_corner.Z, _cursor.Z);

            _cells.Clear();
            for (int z = minZ; z <= maxZ; z++)
            for (int x = minX; x <= maxX; x++)
                _cells.Add(new GridCoord(x, z));
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
            _roads.ApplyStroke(_cells, true);
            _buildings?.Remove(_doomed);
            CancelDrag();
        }

        public void CancelPending()
        {
            if (!_awaitingConfirm) return;
            CancelDrag();
        }

        void ShowGhost()
        {
            RoadStroke.Plan(_roads.Network, _cells, true, _preview);
            _ghost?.Show(_roads.Network, _preview, _grid.Profile, true);

            if (_buildings != null) _buildings.FindTouching(_cells, _doomed);
            _doomedCells.Clear();
            float bottom = float.MaxValue;
            float top = 0f;
            foreach (PlacedBuilding building in _doomed)
            {
                _doomedCells.AddRange(building.Cells);
                float ground = _grid.HeightAt(building.Anchor);
                bottom = Mathf.Min(bottom, ground);
                top = Mathf.Max(top, ground + building.Definition.Footprint.Height * _grid.CellSize);
            }

            // One ghost covers every doomed building, from the lowest one's ground to the tallest one's roof.
            if (_doomedCells.Count > 0)
                _buildingGhost?.Show(_doomedCells, _grid.CellSize, top - bottom, false, bottom);
            else
                _buildingGhost?.Hide();
        }

        void HideGhosts()
        {
            _ghost?.Hide();
            _buildingGhost?.Hide();
        }

        void CancelDrag()
        {
            _dragging = false;
            _awaitingConfirm = false;
            _cells.Clear();
            _preview.Clear();
            _doomed.Clear();
            HideGhosts();
        }
    }
}
