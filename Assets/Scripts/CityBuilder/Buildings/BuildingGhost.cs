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

        Mesh _mesh;
        MeshFilter _filter;
        MeshRenderer _renderer;

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

        public void Show(List<GridCoord> cells, float cellSize, bool valid)
        {
            if (cells == null || cells.Count == 0)
            {
                Hide();
                return;
            }

            BuildMesh(cells, cellSize);

            Material mat = valid ? _validMaterial : _blockedMaterial;
            if (mat != null) _renderer.sharedMaterial = mat;

            transform.position = new Vector3(0f, _zOffset, 0f);
            _renderer.enabled = true;
        }

        public void Hide()
        {
            if (_renderer != null) _renderer.enabled = false;
        }

        void BuildMesh(List<GridCoord> cells, float cellSize)
        {
            int count = cells.Count;
            var verts = new Vector3[count * 4];
            var tris = new int[count * 6];

            const float inset = 0.04f;

            for (int i = 0; i < count; i++)
            {
                GridCoord cell = cells[i];
                float x0 = cell.X * cellSize + inset;
                float z0 = cell.Z * cellSize + inset;
                float x1 = (cell.X + 1) * cellSize - inset;
                float z1 = (cell.Z + 1) * cellSize - inset;

                int vi = i * 4;
                verts[vi + 0] = new Vector3(x0, 0f, z0);
                verts[vi + 1] = new Vector3(x1, 0f, z0);
                verts[vi + 2] = new Vector3(x1, 0f, z1);
                verts[vi + 3] = new Vector3(x0, 0f, z1);

                int ti = i * 6;
                tris[ti + 0] = vi;
                tris[ti + 1] = vi + 2;
                tris[ti + 2] = vi + 1;
                tris[ti + 3] = vi;
                tris[ti + 4] = vi + 3;
                tris[ti + 5] = vi + 2;
            }

            _mesh.Clear();
            _mesh.vertices = verts;
            _mesh.triangles = tris;
            _mesh.RecalculateNormals();
        }

        void OnDestroy()
        {
            if (_mesh != null) Destroy(_mesh);
        }
    }
}
