using System.Collections.Generic;
using CityBuilder.Content;
using CityBuilder.Grid;
using UnityEngine;

namespace CityBuilder.Buildings
{
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class BuildingGhost : MonoBehaviour
    {
        [SerializeField] Material _validMaterial;
        [SerializeField] Material _blockedMaterial;

        [Tooltip("Lifts the ghost clear of the ground so it does not z-fight.")]
        [SerializeField] float _zOffset = 0.005f;

        [Tooltip("Gap left between the ghost and the edge of its footprint.")]
        [SerializeField] float _inset = 0.04f;

        Mesh _mesh;
        MeshFilter _filter;
        MeshRenderer _renderer;

        readonly HashSet<GridCoord> _covered = new HashSet<GridCoord>();
        readonly List<Vector3> _verts = new List<Vector3>();
        readonly List<Vector3> _normals = new List<Vector3>();
        readonly List<int> _tris = new List<int>();

        void Awake()
        {
            _filter = GetComponent<MeshFilter>();
            _renderer = GetComponent<MeshRenderer>();

            _mesh = new Mesh { name = "BuildingGhost" };
            _mesh.MarkDynamic();
            _filter.sharedMesh = _mesh;

            transform.position = new Vector3(0f, _zOffset, 0f);
            Hide();
        }

        /// <summary>
        /// Shows a block over <paramref name="cells"/>, <paramref name="height"/> world units tall,
        /// standing on <paramref name="baseY"/>.
        /// </summary>
        public void Show(List<GridCoord> cells, float cellSize, float height, bool valid, float baseY = 0f)
        {
            if (cells == null || cells.Count == 0)
            {
                Hide();
                return;
            }

            BuildMesh(cells, cellSize, Mathf.Max(height, 0.01f));

            Material mat = valid ? _validMaterial : _blockedMaterial;
            if (mat != null) _renderer.sharedMaterial = mat;

            transform.position = new Vector3(0f, baseY + _zOffset, 0f);
            _renderer.enabled = true;
        }

        public void Hide()
        {
            if (_renderer != null) _renderer.enabled = false;
        }

        // One closed volume over the footprint: a roof per cell, and walls only on edges that face
        // an uncovered cell, so the see-through ghost shows no inner walls between its own cells.
        void BuildMesh(List<GridCoord> cells, float cellSize, float height)
        {
            _covered.Clear();
            foreach (GridCoord cell in cells) _covered.Add(cell);

            _verts.Clear();
            _normals.Clear();
            _tris.Clear();

            foreach (GridCoord cell in cells)
            {
                bool west = !_covered.Contains(new GridCoord(cell.X - 1, cell.Z));
                bool east = !_covered.Contains(new GridCoord(cell.X + 1, cell.Z));
                bool south = !_covered.Contains(new GridCoord(cell.X, cell.Z - 1));
                bool north = !_covered.Contains(new GridCoord(cell.X, cell.Z + 1));

                float x0 = cell.X * cellSize + (west ? _inset : 0f);
                float x1 = (cell.X + 1) * cellSize - (east ? _inset : 0f);
                float z0 = cell.Z * cellSize + (south ? _inset : 0f);
                float z1 = (cell.Z + 1) * cellSize - (north ? _inset : 0f);
                float y = height;

                AddQuad(new Vector3(x0, y, z0), new Vector3(x0, y, z1), new Vector3(x1, y, z1), new Vector3(x1, y, z0), Vector3.up);

                if (south) AddQuad(new Vector3(x0, 0f, z0), new Vector3(x0, y, z0), new Vector3(x1, y, z0), new Vector3(x1, 0f, z0), Vector3.back);
                if (north) AddQuad(new Vector3(x1, 0f, z1), new Vector3(x1, y, z1), new Vector3(x0, y, z1), new Vector3(x0, 0f, z1), Vector3.forward);
                if (west) AddQuad(new Vector3(x0, 0f, z1), new Vector3(x0, y, z1), new Vector3(x0, y, z0), new Vector3(x0, 0f, z0), Vector3.left);
                if (east) AddQuad(new Vector3(x1, 0f, z0), new Vector3(x1, y, z0), new Vector3(x1, y, z1), new Vector3(x1, 0f, z1), Vector3.right);
            }

            _mesh.Clear();
            _mesh.SetVertices(_verts);
            _mesh.SetNormals(_normals);
            _mesh.SetTriangles(_tris, 0);
            _mesh.RecalculateBounds();
        }

        // Corners in clockwise order as seen from the side the normal points to.
        void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal)
        {
            int vi = _verts.Count;
            _verts.Add(a);
            _verts.Add(b);
            _verts.Add(c);
            _verts.Add(d);
            for (int i = 0; i < 4; i++) _normals.Add(normal);

            _tris.Add(vi);
            _tris.Add(vi + 1);
            _tris.Add(vi + 2);
            _tris.Add(vi);
            _tris.Add(vi + 2);
            _tris.Add(vi + 3);
        }

        void OnDestroy()
        {
            if (_mesh != null) Destroy(_mesh);
        }
    }
}
