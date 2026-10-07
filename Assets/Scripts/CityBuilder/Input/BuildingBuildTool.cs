using System;
using System.Collections.Generic;
using CityBuilder.Buildings;
using CityBuilder.Content;
using CityBuilder.Grid;
using CityBuilder.Roads;
using CityBuilder.View;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CityBuilder.BuildTools
{
    public class BuildingBuildTool : MonoBehaviour
    {
        [SerializeField] CityGrid _grid;
        [SerializeField] RoadSystem _roads;
        [SerializeField] BuildingGhost _ghost;
        [SerializeField] Camera _camera;
        [SerializeField] BuildMenu _buildMenu;

        readonly List<PlacedBuilding> _placed = new List<PlacedBuilding>();
        readonly List<GridCoord> _previewCells = new List<GridCoord>();

        ModelDefinition _selected;
        int _rotation;
        bool _awaitingConfirm;
        GridCoord _confirmCell;
        PlacementProblem _lastProblem;

        public ModelDefinition Selected => _selected;
        public int Rotation => _rotation;
        public bool IsAwaitingConfirm => _awaitingConfirm;
        public PlacementProblem LastProblem => _lastProblem;
        public int PlacedCount => _placed.Count;
        public IReadOnlyList<PlacedBuilding> Placed => _placed;

        public Vector3 PendingEnd => _previewCells.Count > 0
            ? _grid.CellToWorld(_previewCells[_previewCells.Count - 1])
            : Vector3.zero;

        public event Action<PlacedBuilding> BuildingPlaced;

        void Awake()
        {
            if (_grid == null) _grid = FindAnyObjectByType<CityGrid>();
            if (_roads == null) _roads = FindAnyObjectByType<RoadSystem>();
            if (_ghost == null) _ghost = FindAnyObjectByType<BuildingGhost>();
            if (_camera == null) _camera = Camera.main;
            if (_buildMenu == null) _buildMenu = FindAnyObjectByType<BuildMenu>();
        }

        void Update()
        {
            if (_grid == null) return;

            bool active = _buildMenu != null && _buildMenu.Active == BuildCategory.Buildings;

            if (!active)
            {
                if (_awaitingConfirm) CancelPending();
                if (_selected != null) HideGhost();
                return;
            }

            if (_selected == null)
            {
                HideGhost();
                return;
            }

            if (_awaitingConfirm)
            {
                UpdateConfirm();
                return;
            }

            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.rKey.wasPressedThisFrame)
                    _rotation = (_rotation + 1) % 4;

                if (keyboard.escapeKey.wasPressedThisFrame)
                {
                    Deselect();
                    _buildMenu.EscapeConsumed = true;
                    return;
                }
            }

            Mouse mouse = Mouse.current;
            if (mouse == null) return;

            bool onPlane = _grid.TryScreenToWorld(_camera, mouse.position.ReadValue(), out Vector3 world);
            GridCoord cursor = onPlane ? _grid.WorldToCell(world) : default;
            bool onGround = onPlane && _grid.InBounds(cursor);

            if (!onGround)
            {
                HideGhost();
                return;
            }

            _previewCells.Clear();
            _previewCells.AddRange(BuildingPlacement.CellsAt(_selected.Footprint, cursor, _rotation));

            _lastProblem = BuildingPlacement.Check(
                _selected, cursor, _rotation,
                _grid.InBounds, _roads.Network, _placed);

            bool valid = _lastProblem == PlacementProblem.None;
            _ghost?.Show(_previewCells, _grid.CellSize, valid);

            if (mouse.leftButton.wasPressedThisFrame && valid)
            {
                _confirmCell = cursor;
                _awaitingConfirm = true;
            }
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
                _buildMenu.EscapeConsumed = true;
            }
        }

        public void ConfirmPending()
        {
            if (!_awaitingConfirm || _selected == null) return;

            var building = new PlacedBuilding(_selected, _confirmCell, _rotation);
            _placed.Add(building);
            BuildingPlaced?.Invoke(building);

            _awaitingConfirm = false;
        }

        public void CancelPending()
        {
            _awaitingConfirm = false;
        }

        public void Select(ModelDefinition definition)
        {
            _selected = definition;
            _rotation = 0;
            _awaitingConfirm = false;
        }

        public void Deselect()
        {
            _selected = null;
            _awaitingConfirm = false;
            HideGhost();
        }

        void HideGhost()
        {
            _previewCells.Clear();
            _ghost?.Hide();
        }
    }
}
