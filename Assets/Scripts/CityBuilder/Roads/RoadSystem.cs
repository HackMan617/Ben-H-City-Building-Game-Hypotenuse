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
    /// Geometry comes from <see cref="RoadGeometry"/>: runs of road are splines with the selected road
    /// type's model bent along them, junctions are models or generated pieces. Road types are the
    /// bundles whose asset.json says "type": "path"; they are found and the selected one loaded in the
    /// background, and until it lands, or when there are none, everything is generated. The whole
    /// network takes the selected type's look -- cells do not remember a type of their own yet. The
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

        [Tooltip("Load road models from the road type bundles. Off, every road is generated.")]
        [SerializeField] bool _useRoadModels = true;

        [Tooltip("Bundle name of the road type selected at start. Empty or missing, the first one found is used.")]
        [SerializeField] string _startRoadType = "basicroad";

        readonly List<GridCoord> _scratch = new List<GridCoord>();
        readonly RoadMeshBuffers _buffers = new RoadMeshBuffers();
        readonly List<Spline> _splines = new List<Spline>();
        readonly List<Material> _materials = new List<Material>();

        readonly List<BundleInfo> _roadTypes = new List<BundleInfo>();
        readonly Dictionary<string, RoadModels> _loadedModels = new Dictionary<string, RoadModels>();

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

        /// <summary>The road types the player can pick from, once found; empty when there are none.</summary>
        public IReadOnlyList<BundleInfo> RoadTypes => _roadTypes;

        /// <summary>The road type roads are built with, or null while none is found.</summary>
        public BundleInfo SelectedRoadType { get; private set; }

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

            List<BundleInfo> found = await BundleLoader.Shared.LoadBundleDefinitionsAsync(ModelType.Path);
            if (this == null || found.Count == 0) return;

            _roadTypes.AddRange(found);
            BundleInfo start = _roadTypes.Find(t => t.Name == _startRoadType) ?? _roadTypes[0];
            SelectRoadType(start);
        }

        /// <summary>
        /// Builds every road with <paramref name="roadType"/>'s models from now on. Its bundle loads in
        /// the background the first time; until then the roads keep their current look.
        /// </summary>
        public async void SelectRoadType(BundleInfo roadType)
        {
            if (roadType == null || roadType == SelectedRoadType) return;

            SelectedRoadType = roadType;
            Changed?.Invoke();

            if (!_loadedModels.TryGetValue(roadType.Name, out RoadModels models))
            {
                models = await RoadModels.LoadAsync(BundleLoader.Shared, roadType.Name);
                if (this == null) return;
                _loadedModels[roadType.Name] = models;
            }

            // Another type may have been picked while this one loaded.
            if (SelectedRoadType != roadType) return;

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

        /// <summary>
        /// Swaps in a whole new layout, as when a save is loaded: <paramref name="fill"/> writes it into
        /// the network. The undo history belonged to the old layout, so it goes.
        /// </summary>
        public void Replace(System.Action<RoadNetwork> fill)
        {
            Network.Clear();
            fill(Network);
            History.Clear();
            Rebuild();
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
