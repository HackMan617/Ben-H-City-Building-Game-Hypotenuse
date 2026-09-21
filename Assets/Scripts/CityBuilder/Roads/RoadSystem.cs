using System.Collections.Generic;
using CityBuilder.Grid;
using UnityEngine;

namespace CityBuilder.Roads
{
    /// <summary>
    /// Owns the committed road network, its geometry, and the undo stack.
    ///
    /// The whole network is remeshed on every change. That is well inside budget for a prototype at
    /// a few thousand cells; the point where it stops being fine is the point to split the network
    /// into chunks and rebuild only the ones a stroke touched.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class RoadSystem : MonoBehaviour
    {
        [SerializeField] CityGrid _grid;
        [SerializeField] Material _roadMaterial;

        readonly List<GridCoord> _scratch = new List<GridCoord>();

        Mesh _mesh;
        MeshFilter _filter;
        MeshRenderer _renderer;

        public RoadNetwork Network { get; private set; }
        public EditHistory History { get; private set; }
        public CityGrid Grid => _grid;

        /// <summary>Raised after the network changes, so UI can refresh counts and undo state.</summary>
        public event System.Action Changed;

        void Awake()
        {
            if (_grid == null) _grid = FindAnyObjectByType<CityGrid>();

            Network = new RoadNetwork();
            History = new EditHistory();

            _filter = GetComponent<MeshFilter>();
            _renderer = GetComponent<MeshRenderer>();

            _mesh = new Mesh { name = "Roads" };
            _mesh.MarkDynamic();
            _filter.sharedMesh = _mesh;

            if (_roadMaterial != null) _renderer.sharedMaterial = _roadMaterial;
        }

        /// <summary>
        /// Commits a stroke along <paramref name="cells"/>. <paramref name="erase"/> removes instead
        /// of adding. Anything already in the requested state is dropped, so the recorded edit is the
        /// real delta.
        /// </summary>
        public void ApplyStroke(IReadOnlyList<GridCoord> cells, bool erase)
        {
            if (cells == null || cells.Count == 0) return;

            var edit = new RoadEdit();
            RoadStroke.Plan(Network, cells, erase, edit, _grid.InBounds);
            if (edit.IsEmpty) return;

            edit.Apply(Network);
            History.Record(edit);
            Rebuild();
        }

        public void Undo()
        {
            if (History.Undo(Network) != null) Rebuild();
        }

        public void Redo()
        {
            if (History.Redo(Network) != null) Rebuild();
        }

        public void ClearAll()
        {
            Network.Clear();
            History.Clear();
            Rebuild();
        }

        public void Rebuild()
        {
            _scratch.Clear();
            _scratch.AddRange(Network.Cells);
            RoadMeshBuilder.Build(_mesh, Network, _scratch, _grid.Profile);
            Changed?.Invoke();
        }

        void OnDestroy()
        {
            if (_mesh != null) Destroy(_mesh);
        }
    }
}
