using System.Collections.Generic;
using CityBuilder.Content;
using CityBuilder.Grid;
using UnityEngine;
using UnityEngine.Splines;

namespace CityBuilder.Roads
{
    /// <summary>
    /// Owns the committed road network, its geometry, and the undo stack.
    ///
    /// Geometry comes from <see cref="RoadGeometry"/>: runs of road are splines with the road model
    /// from the "roads" bundle bent along them, junctions are models or generated pieces. The bundle
    /// loads in the background; until it lands, or when there is none, everything is generated. The
    /// splines are kept on a child <see cref="SplineContainer"/> so they can be seen in the Scene view.
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

        [Tooltip("Load road models from the \"roads\" asset bundle. Off, every road is generated.")]
        [SerializeField] bool _useRoadModels = true;

        readonly List<GridCoord> _scratch = new List<GridCoord>();
        readonly RoadMeshBuffers _buffers = new RoadMeshBuffers();
        readonly List<Spline> _splines = new List<Spline>();
        readonly List<Material> _materials = new List<Material>();

        RoadModels _models;
        SplineContainer _splineView;

        Mesh _mesh;
        MeshFilter _filter;
        MeshRenderer _renderer;

        public RoadNetwork Network { get; private set; }
        public EditHistory History { get; private set; }
        public CityGrid Grid => _grid;

        /// <summary>The spline of every run of road, in this object's space, as of the last rebuild.</summary>
        public IReadOnlyList<Spline> Splines => _splines;

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

            var view = new GameObject("RoadSplines");
            view.transform.SetParent(transform, false);
            _splineView = view.AddComponent<SplineContainer>();
            _splineView.Splines = _splines;
        }

        async void Start()
        {
            if (!_useRoadModels) return;

            RoadModels models = await RoadModels.LoadAsync(BundleLoader.Shared);
            if (this == null || models == null) return;

            _models = models;
            Rebuild();
        }

        /// <summary>
        /// Commits a stroke along <paramref name="cells"/>. <paramref name="erase"/> removes instead
        /// of adding. Anything already in the requested state is dropped, so the recorded edit is the
        /// real delta. A stroke that would overlap road it does not join is refused outright.
        /// </summary>
        public void ApplyStroke(IReadOnlyList<GridCoord> cells, bool erase)
        {
            if (cells == null || cells.Count == 0) return;

            var edit = new RoadEdit();
            RoadStroke.Plan(Network, cells, erase, edit, _grid.InBounds);
            if (edit.IsEmpty || RoadStroke.IsBlocked(Network, edit)) return;

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

            _buffers.Clear(_roadMaterial);
            _splines.Clear();
            RoadGeometry.Build(_buffers, Network, _scratch, _grid.Profile, _models, _splines);
            _buffers.WriteTo(_mesh);

            _materials.Clear();
            _materials.AddRange(_buffers.Materials);
            _renderer.SetSharedMaterials(_materials);

            if (_splineView != null) _splineView.Splines = _splines;
            Changed?.Invoke();
        }

        void OnDestroy()
        {
            if (_mesh != null) Destroy(_mesh);
        }
    }
}
