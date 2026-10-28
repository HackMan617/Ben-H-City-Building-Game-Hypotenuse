using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CityBuilder.Roads
{
    /// <summary>
    /// Vertex and index lists a road mesh is gathered into before it is written out, one submesh per
    /// material. Submesh 0 is the base material -- generated pieces, and model parts with no material
    /// of their own -- so a mesh built with no models has exactly one submesh.
    /// </summary>
    public sealed class RoadMeshBuffers
    {
        public readonly List<Vector3> Vertices = new List<Vector3>();
        public readonly List<Vector3> Normals = new List<Vector3>();
        public readonly List<Vector2> Uvs = new List<Vector2>();

        readonly List<List<int>> _triangles = new List<List<int>>();
        readonly List<Material> _materials = new List<Material>();
        int _used;

        public RoadMeshBuffers() => Clear(null);

        /// <summary>Triangles in the base submesh.</summary>
        public List<int> Base => _triangles[0];

        public void Clear(Material baseMaterial)
        {
            Vertices.Clear();
            Normals.Clear();
            Uvs.Clear();
            _materials.Clear();
            _materials.Add(baseMaterial);

            // Index lists are kept between builds so a drag does not churn the heap.
            if (_triangles.Count == 0) _triangles.Add(new List<int>());
            foreach (List<int> list in _triangles) list.Clear();
            _used = 1;
        }

        /// <summary>The triangle list for <paramref name="material"/>, starting a submesh for it if needed. Null is the base.</summary>
        public List<int> TrianglesFor(Material material)
        {
            if (material == null) return Base;

            int index = _materials.IndexOf(material);
            if (index >= 0) return _triangles[index];

            _materials.Add(material);
            if (_triangles.Count < _materials.Count) _triangles.Add(new List<int>());
            return _triangles[_used++];
        }

        public int SubmeshCount => _used;

        /// <summary>The triangle list of submesh <paramref name="index"/>.</summary>
        public List<int> Submesh(int index) => _triangles[index];

        /// <summary>The material for each submesh, in order. The base slot may be null.</summary>
        public IReadOnlyList<Material> Materials => _materials;

        public void WriteTo(Mesh mesh)
        {
            mesh.Clear();
            mesh.indexFormat = Vertices.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(Vertices);
            mesh.SetNormals(Normals);
            mesh.SetUVs(0, Uvs);

            mesh.subMeshCount = _used;
            for (int i = 0; i < _used; i++) mesh.SetTriangles(_triangles[i], i, false);
            mesh.RecalculateBounds();
        }
    }
}
