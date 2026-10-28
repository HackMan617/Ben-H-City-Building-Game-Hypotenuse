using System.Collections.Generic;
using CityBuilder.Grid;
using CityBuilder.Roads;
using UnityEngine;

namespace CityBuilder.View
{
    /// <summary>
    /// Draws the raised ground of the <see cref="TerrainMap"/>: a top per cell, and a wall wherever a
    /// cell stands above its neighbour. Level 0 is the ground plane itself, so an untouched map draws
    /// nothing.
    ///
    /// Under a road ramp (see <see cref="RoadTerrain"/>) the top slopes up to the next level, and the
    /// walls along its sides follow it, so they come out as triangles. Every top is described by the
    /// height of its four corners and every wall by the corners either side of its edge, which covers
    /// flat cells and ramps alike.
    ///
    /// Tops are submesh 0 and share the ground's grid material, so the grid runs on across them;
    /// walls are submesh 1. Like the roads, the whole map is remeshed on every change.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class TerrainView : MonoBehaviour
    {
        [SerializeField] CityGrid _grid;
        [SerializeField] RoadSystem _roads;
        [SerializeField] Material _topMaterial;
        [SerializeField] Material _cliffMaterial;

        // Each side: the neighbour's offset, the edge's two corners in this cell (0 or 1 in x and z,
        // in the order that winds the wall outward), and the wall's normal.
        static readonly (GridCoord side, Vector2Int a, Vector2Int b, Vector3 normal)[] Sides =
        {
            (new GridCoord(0, -1), new Vector2Int(0, 0), new Vector2Int(1, 0), Vector3.back),
            (new GridCoord(0, 1), new Vector2Int(1, 1), new Vector2Int(0, 1), Vector3.forward),
            (new GridCoord(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, 0), Vector3.left),
            (new GridCoord(1, 0), new Vector2Int(1, 0), new Vector2Int(1, 1), Vector3.right),
        };

        readonly HashSet<GridCoord> _drawn = new HashSet<GridCoord>();
        readonly List<Vector3> _verts = new List<Vector3>();
        readonly List<Vector3> _normals = new List<Vector3>();
        readonly List<int> _tops = new List<int>();
        readonly List<int> _cliffs = new List<int>();

        Mesh _mesh;
        MeshRenderer _renderer;

        public MeshRenderer Renderer => _renderer;

        void Awake()
        {
            if (_grid == null) _grid = FindAnyObjectByType<CityGrid>();
            if (_roads == null) _roads = FindAnyObjectByType<RoadSystem>();

            _renderer = GetComponent<MeshRenderer>();
            _mesh = new Mesh { name = "Terrain" };
            _mesh.MarkDynamic();
            GetComponent<MeshFilter>().sharedMesh = _mesh;
            _renderer.sharedMaterials = new[] { _topMaterial, _cliffMaterial };

            transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            transform.localScale = Vector3.one;

            // Ramps come from the roads, so a road change can reshape the ground as well.
            if (_grid != null) _grid.Terrain.Changed += Rebuild;
            if (_roads != null) _roads.Changed += Rebuild;
            Rebuild();
        }

        void OnDestroy()
        {
            if (_grid != null) _grid.Terrain.Changed -= Rebuild;
            if (_roads != null) _roads.Changed -= Rebuild;
            if (_mesh != null) Destroy(_mesh);
        }

        public void Rebuild()
        {
            _verts.Clear();
            _normals.Clear();
            _tops.Clear();
            _cliffs.Clear();
            _drawn.Clear();

            if (_grid != null)
            {
                foreach (KeyValuePair<GridCoord, int> pair in _grid.Terrain.Raised) _drawn.Add(pair.Key);
                if (_roads != null && _roads.Network != null && _grid.Terrain.Count > 0)
                {
                    foreach (GridCoord cell in _roads.Network.Cells)
                    {
                        if (RoadTerrain.RampAt(_roads.Network, _grid.Terrain, cell, out _)) _drawn.Add(cell);
                    }
                }

                foreach (GridCoord cell in _drawn) AddCell(cell);
            }

            _mesh.Clear();
            _mesh.indexFormat = _verts.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            _mesh.SetVertices(_verts);
            _mesh.SetNormals(_normals);
            _mesh.subMeshCount = 2;
            _mesh.SetTriangles(_tops, 0, false);
            _mesh.SetTriangles(_cliffs, 1, false);
            _mesh.RecalculateBounds();
        }

        void AddCell(GridCoord cell)
        {
            float cs = _grid.CellSize;
            float x0 = cell.X * cs, x1 = x0 + cs;
            float z0 = cell.Z * cs, z1 = z0 + cs;

            float sw = Corner(cell, 0, 0), nw = Corner(cell, 0, 1), ne = Corner(cell, 1, 1), se = Corner(cell, 1, 0);
            Vector3 up = Vector3.Cross(new Vector3(0f, nw - sw, cs), new Vector3(cs, se - sw, 0f)).normalized;
            AddQuad(_tops, new Vector3(x0, sw, z0), new Vector3(x0, nw, z1), new Vector3(x1, ne, z1), new Vector3(x1, se, z0), up);

            foreach ((GridCoord side, Vector2Int a, Vector2Int b, Vector3 normal) in Sides)
            {
                GridCoord next = cell + side;
                float topA = Corner(cell, a.x, a.y);
                float topB = Corner(cell, b.x, b.y);
                float bottomA = Mathf.Min(topA, Corner(next, a.x - side.X, a.y - side.Z));
                float bottomB = Mathf.Min(topB, Corner(next, b.x - side.X, b.y - side.Z));
                if (topA <= bottomA && topB <= bottomB) continue;

                var pa = new Vector3(x0 + a.x * cs, 0f, z0 + a.y * cs);
                var pb = new Vector3(x0 + b.x * cs, 0f, z0 + b.y * cs);
                AddQuad(_cliffs,
                        new Vector3(pa.x, bottomA, pa.z), new Vector3(pa.x, topA, pa.z),
                        new Vector3(pb.x, topB, pb.z), new Vector3(pb.x, bottomB, pb.z), normal);
            }
        }

        /// <summary>Height of <paramref name="cell"/>'s top at one of its corners, (cx, cz) each 0 or 1.</summary>
        float Corner(GridCoord cell, int cx, int cz)
        {
            Dir8 uphill = Dir8.None;
            if (_roads != null && _roads.Network != null) RoadTerrain.RampAt(_roads.Network, _grid.Terrain, cell, out uphill);
            return RoadTerrain.SurfaceHeight(_grid, _grid.Terrain.LevelAt(cell), uphill, cx, cz);
        }

        // Corners in clockwise order as seen from the side the normal points to.
        void AddQuad(List<int> tris, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal)
        {
            int vi = _verts.Count;
            _verts.Add(a);
            _verts.Add(b);
            _verts.Add(c);
            _verts.Add(d);
            for (int i = 0; i < 4; i++) _normals.Add(normal);

            tris.Add(vi);
            tris.Add(vi + 1);
            tris.Add(vi + 2);
            tris.Add(vi);
            tris.Add(vi + 2);
            tris.Add(vi + 3);
        }
    }
}
