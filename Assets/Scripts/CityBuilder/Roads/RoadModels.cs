using System.Collections.Generic;
using CityBuilder.Content;
using CityBuilder.Grid;
using UnityEngine;

namespace CityBuilder.Roads
{
    /// <summary>
    /// A road model's geometry, copied out of its prefab into plain arrays so it can be bent along a
    /// spline or stamped into a cell. Positions are in cell units: 1 is one grid cell, and the piece
    /// is scaled up by the grid's cell size when used.
    /// </summary>
    public sealed class RoadPiece
    {
        public Vector3[] Vertices;
        public Vector3[] Normals;
        public Vector2[] Uvs;

        /// <summary>One index list per part; each draws with the material in the same slot of <see cref="Materials"/>.</summary>
        public int[][] Triangles;

        /// <summary>Per part; null means the road's base material.</summary>
        public Material[] Materials;

        public Bounds Bounds;

        /// <summary>
        /// Gathers every mesh under <paramref name="prefab"/> into one piece, in the prefab root's own
        /// space -- so the axis turn and scale an .fbx often puts on its child node are baked in.
        /// Returns null, with a warning, when there is no mesh or one cannot be read.
        /// </summary>
        public static RoadPiece FromPrefab(GameObject prefab)
        {
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int[]>();
            var materials = new List<Material>();

            Matrix4x4 toRoot = prefab.transform.worldToLocalMatrix;

            foreach (MeshFilter filter in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh mesh = filter.sharedMesh;
                if (mesh == null) continue;

                if (!mesh.isReadable)
                {
                    Debug.LogWarning($"Road model '{prefab.name}' has a mesh that cannot be read. Tick Read/Write " +
                                     "in its import settings; until then the road is generated instead.");
                    return null;
                }

                Matrix4x4 matrix = toRoot * filter.transform.localToWorldMatrix;
                int offset = vertices.Count;

                Vector3[] meshNormals = mesh.normals;
                Vector2[] meshUvs = mesh.uv;
                Vector3[] meshVertices = mesh.vertices;
                for (int i = 0; i < meshVertices.Length; i++)
                {
                    vertices.Add(matrix.MultiplyPoint3x4(meshVertices[i]));
                    normals.Add(i < meshNormals.Length ? matrix.MultiplyVector(meshNormals[i]).normalized : Vector3.up);
                    uvs.Add(i < meshUvs.Length ? meshUvs[i] : Vector2.zero);
                }

                var renderer = filter.GetComponent<MeshRenderer>();
                Material[] shared = renderer != null ? renderer.sharedMaterials : null;

                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    int[] indices = mesh.GetTriangles(sub);
                    for (int i = 0; i < indices.Length; i++) indices[i] += offset;
                    triangles.Add(indices);
                    materials.Add(shared != null && sub < shared.Length ? shared[sub] : null);
                }
            }

            if (vertices.Count == 0)
            {
                Debug.LogWarning($"Road model '{prefab.name}' has no mesh; the road is generated instead.");
                return null;
            }

            return Create(vertices, normals, uvs, triangles, materials);
        }

        /// <summary>
        /// The stand-in segment when no model is loaded: a plain strip, the profile's width and curb
        /// height, running one cell along +Z with enough rings to bend smoothly round a turn.
        /// </summary>
        public static RoadPiece GeneratedSegment(RoadProfile profile)
        {
            const int rings = 8;
            float hw = profile.WidthFraction * 0.5f;
            float h = profile.CellSize > 0f ? profile.Height / profile.CellSize : profile.Height;

            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();

            // Top, then the east and west curbs. Each strip is a column of quads along +Z.
            AddStrip(i => new Vector3(-hw, h, i), i => new Vector3(hw, h, i), Vector3.up);
            if (h > 0.0001f)
            {
                AddStrip(i => new Vector3(hw, h, i), i => new Vector3(hw, 0f, i), Vector3.right);
                AddStrip(i => new Vector3(-hw, 0f, i), i => new Vector3(-hw, h, i), Vector3.left);
            }

            return Create(vertices, normals, uvs, new List<int[]> { tris.ToArray() }, new List<Material> { null });

            // Quads between a left and right edge at each ring, clockwise seen from the outside.
            void AddStrip(System.Func<float, Vector3> left, System.Func<float, Vector3> right, Vector3 normal)
            {
                int start = vertices.Count;
                for (int r = 0; r <= rings; r++)
                {
                    float z = r / (float)rings;
                    vertices.Add(left(z));
                    vertices.Add(right(z));
                    normals.Add(normal);
                    normals.Add(normal);
                    uvs.Add(new Vector2(0f, z));
                    uvs.Add(new Vector2(1f, z));
                }
                for (int r = 0; r < rings; r++)
                {
                    int a = start + r * 2;
                    tris.Add(a);
                    tris.Add(a + 2);
                    tris.Add(a + 3);
                    tris.Add(a);
                    tris.Add(a + 3);
                    tris.Add(a + 1);
                }
            }
        }

        static RoadPiece Create(List<Vector3> vertices, List<Vector3> normals, List<Vector2> uvs,
                                List<int[]> triangles, List<Material> materials)
        {
            var bounds = new Bounds(vertices[0], Vector3.zero);
            foreach (Vector3 v in vertices) bounds.Encapsulate(v);

            return new RoadPiece
            {
                Vertices = vertices.ToArray(),
                Normals = normals.ToArray(),
                Uvs = uvs.ToArray(),
                Triangles = triangles.ToArray(),
                Materials = materials.ToArray(),
                Bounds = bounds,
            };
        }
    }

    /// <summary>
    /// The models of one road type. Each road type is its own bundle -- a folder under
    /// Assets/GameAssets such as BasicRoad -- whose asset.json has "type": "path"; the player picks
    /// between them. Every model is optional; whatever is missing is generated.
    ///
    ///   RoadSegment   a stretch of road, bent along every spline. Runs along +Z, centred on X, its
    ///                 road surface up from Y = 0; any length, repeated to fill the spline. Give it a
    ///                 few edge loops along its length or it cannot bend smoothly round a turn.
    ///   RoadCross     a 4-way crossing. Also used turned 45 degrees where two diagonals cross at a
    ///                 cell corner, its arms then reaching 0.5 along each diagonal.
    ///   RoadTee       a 3-way junction with arms east, south and west; the closed side faces north.
    ///   RoadEnd       a dead end, its one arm reaching north.
    ///
    /// All are modelled for a 1-unit cell. Junctions are centred on the origin and their arms reach
    /// the cell edge (0.5), where the neighbouring segment begins; they are turned to fit and only
    /// used for straight, non-diagonal junctions. Make the road the same width and height as the grid's
    /// road profile so models and generated pieces line up.
    /// </summary>
    public sealed class RoadModels
    {
        public const string SegmentName = "RoadSegment";
        public const string CrossName = "RoadCross";
        public const string TeeName = "RoadTee";
        public const string EndName = "RoadEnd";

        public RoadPiece Segment;
        public RoadPiece Cross;
        public RoadPiece Tee;
        public RoadPiece End;

        /// <summary>
        /// Loads what the road type's bundle has. Returns null when there is no such bundle, which is
        /// not an error: the roads are generated.
        /// </summary>
        public static async Awaitable<RoadModels> LoadAsync(BundleLoader loader, string bundleName)
        {
            if (!loader.Initialize() || !loader.HasBundle(bundleName)) return null;

            var models = new RoadModels
            {
                Segment = await LoadPieceAsync(loader, bundleName, SegmentName),
                Cross = await LoadPieceAsync(loader, bundleName, CrossName),
                Tee = await LoadPieceAsync(loader, bundleName, TeeName),
                End = await LoadPieceAsync(loader, bundleName, EndName),
            };
            return models;
        }

        // The geometry is copied out, but the bundle stays open: model materials and textures live there.
        static async Awaitable<RoadPiece> LoadPieceAsync(BundleLoader loader, string bundleName, string name)
        {
            GameObject prefab = await loader.LoadOptionalAssetAsync<GameObject>(bundleName, name);
            return prefab != null ? RoadPiece.FromPrefab(prefab) : null;
        }

        /// <summary>
        /// The junction model for <paramref name="mask"/> and how many quarter turns clockwise it needs,
        /// or null when there is none -- a diagonal arm, a shape with no model, or that model missing.
        /// </summary>
        public RoadPiece JunctionFor(Dir8 mask, out int quarterTurns)
        {
            quarterTurns = 0;
            if ((mask & Dir8.Diagonal) != 0) return null;

            RoadPiece piece;
            Dir8 basis;
            switch (RoadChains.LinkCount(mask))
            {
                case 4: piece = Cross; basis = Dir8.Orthogonal; break;
                case 3: piece = Tee; basis = Dir8.E | Dir8.S | Dir8.W; break;
                case 1: piece = End; basis = Dir8.N; break;
                default: return null;
            }
            if (piece == null) return null;

            for (int turn = 0; turn < 4; turn++)
            {
                if (Rotate(basis, turn) == mask)
                {
                    quarterTurns = turn;
                    return piece;
                }
            }
            return null;
        }

        /// <summary>Turns a mask clockwise seen from above by quarter turns: N becomes E.</summary>
        static Dir8 Rotate(Dir8 mask, int quarterTurns)
        {
            int shift = (quarterTurns & 3) * 2;
            int bits = (int)mask;
            return (Dir8)(((bits << shift) | (bits >> (8 - shift))) & 0xFF);
        }
    }
}
