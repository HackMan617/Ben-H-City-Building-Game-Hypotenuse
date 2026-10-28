using System;
using System.Collections.Generic;
using CityBuilder.Grid;
using UnityEngine;

namespace CityBuilder.Roads
{
    /// <summary>
    /// Fits roads to the terrain, slopes included.
    ///
    /// A road climbs one level per cell, straight: where a road cell links to a neighbour one level
    /// up, the lower cell becomes a ramp, its surface rising from its own level at the far edge to the
    /// neighbour's at the shared edge. A ramp cell is otherwise straight -- it links only along the
    /// slope -- so it never carries a junction, and ramps can follow one another to climb several
    /// levels. <see cref="SlopeOk"/> holds those rules; the terrain view draws the ground under ramps
    /// from the same <see cref="RampAt"/>.
    ///
    /// Road geometry is built flat at y = 0 and lifted here. Height only bends at cell edges, so
    /// triangles near a ramp are first cut along the cell edges they cross, leaving each piece in
    /// one cell where the surface is a plane.
    /// </summary>
    public static class RoadTerrain
    {
        const float Epsilon = 1e-4f;

        static readonly List<int> Polygon = new List<int>(4);
        static readonly List<int> Below = new List<int>(5);
        static readonly List<int> Above = new List<int>(5);
        static readonly List<int> Output = new List<int>();
        static readonly Stack<int> Pending = new Stack<int>();

        /// <summary>
        /// Whether <paramref name="cell"/> is a ramp in <paramref name="roads"/>, and which way is up:
        /// the direction of its link to a road cell one level higher.
        /// </summary>
        public static bool RampAt(IRoadQuery roads, TerrainMap terrain, GridCoord cell, out Dir8 uphill)
        {
            uphill = Dir8.None;
            if (!roads.Has(cell)) return false;

            Dir8 mask = roads.MaskAt(cell);
            int level = terrain.LevelAt(cell);
            foreach (Dir8 dir in Dir8Util.Orthogonal)
            {
                if ((mask & dir) != 0 && terrain.LevelAt(Dir8Util.Step(cell, dir)) == level + 1)
                {
                    uphill = dir;
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Whether the road at <paramref name="cell"/> keeps to the slope rules: level links, or
        /// straight orthogonal ones a single level apart with no other link off the ramp.
        /// </summary>
        public static bool SlopeOk(IRoadQuery roads, TerrainMap terrain, GridCoord cell)
        {
            Dir8 mask = roads.MaskAt(cell);
            int level = terrain.LevelAt(cell);

            foreach (Dir8 dir in Dir8Util.All)
            {
                if ((mask & dir) == 0) continue;

                int rise = terrain.LevelAt(Dir8Util.Step(cell, dir)) - level;
                if (rise == 0) continue;
                if (Dir8Util.IsDiagonal(dir) || Math.Abs(rise) > 1) return false;
                if (rise < 0) continue;   // the cell below is the ramp; it checks itself

                // This cell is a ramp up to the next level: straight through, and not up both ways.
                Dir8 back = Dir8Util.Opposite(dir);
                if ((mask & ~(dir | back)) != 0) return false;
                if ((mask & back) != 0 && terrain.LevelAt(Dir8Util.Step(cell, back)) > level) return false;
            }
            return true;
        }

        /// <summary>True when applying <paramref name="edit"/> to <paramref name="network"/> would leave a road off the slope rules.</summary>
        public static bool BreaksSlopes(IRoadQuery network, RoadEdit edit, TerrainMap terrain)
        {
            if (terrain.Count == 0) return false;

            var after = new RoadOverlay(network);
            after.Apply(edit);

            var touched = new HashSet<GridCoord>(edit.Added);
            foreach (RoadLink link in edit.Linked)
            {
                touched.Add(link.Cell);
                touched.Add(link.Other);
            }

            // A new link can turn a neighbour into an illegal ramp, so the cells round each are checked too.
            var check = new HashSet<GridCoord>();
            foreach (GridCoord cell in touched)
            {
                check.Add(cell);
                foreach (Dir8 dir in Dir8Util.All) check.Add(Dir8Util.Step(cell, dir));
            }

            foreach (GridCoord cell in check)
            {
                if (after.Has(cell) && !SlopeOk(after, terrain, cell)) return true;
            }
            return false;
        }

        /// <summary>
        /// Height of a cell's surface at (<paramref name="u"/>, <paramref name="v"/>), each 0..1
        /// across the cell east and north: flat at its level, or rising one level towards
        /// <paramref name="uphill"/> on a ramp.
        /// </summary>
        public static float SurfaceHeight(CityGrid grid, int level, Dir8 uphill, float u, float v)
        {
            float rise;
            switch (uphill)
            {
                case Dir8.N: rise = v; break;
                case Dir8.S: rise = 1f - v; break;
                case Dir8.E: rise = u; break;
                case Dir8.W: rise = 1f - u; break;
                default: rise = 0f; break;
            }
            return grid.HeightOf(level) + rise * grid.LevelHeight;
        }

        /// <summary>
        /// Raises each vertex onto the surface of the road cell it belongs to, tilting normals up the
        /// ramps. A vertex outside every road cell -- the edge of a diagonal, cutting the corner of its
        /// neighbours -- belongs to the nearest road cell round it.
        /// </summary>
        public static void Lift(RoadMeshBuffers buffers, CityGrid grid, IRoadQuery roads)
        {
            TerrainMap terrain = grid.Terrain;
            if (terrain.Count == 0) return;

            float cs = grid.CellSize;
            SplitAtRampEdges(buffers, grid, roads);

            List<Vector3> vertices = buffers.Vertices;
            List<Vector3> normals = buffers.Normals;
            float slope = grid.LevelHeight / cs;

            for (int i = 0; i < vertices.Count; i++)
            {
                Vector3 p = vertices[i];
                GridCoord cell = grid.WorldToCell(p);
                if (!roads.Has(cell)) cell = NearestRoad(cell, p, cs, roads);

                RampAt(roads, terrain, cell, out Dir8 uphill);
                float u = Mathf.Clamp01(p.x / cs - cell.X);
                float v = Mathf.Clamp01(p.z / cs - cell.Z);
                p.y += SurfaceHeight(grid, terrain.LevelAt(cell), uphill, u, v);
                vertices[i] = p;

                if (uphill != Dir8.None)
                {
                    // The ramp shears the road upward along the slope; normals take the inverse transpose.
                    GridCoord step = Dir8Util.Step(default, uphill);
                    Vector3 n = normals[i];
                    normals[i] = (n - new Vector3(step.X, 0f, step.Z) * (slope * n.y)).normalized;
                }
            }
        }

        static GridCoord NearestRoad(GridCoord cell, Vector3 p, float cs, IRoadQuery roads)
        {
            GridCoord best = cell;
            float bestDistance = float.MaxValue;
            for (int dz = -1; dz <= 1; dz++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    var candidate = new GridCoord(cell.X + dx, cell.Z + dz);
                    if (!roads.Has(candidate)) continue;

                    float ox = (candidate.X + 0.5f) * cs - p.x;
                    float oz = (candidate.Z + 0.5f) * cs - p.z;
                    float distance = ox * ox + oz * oz;
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        best = candidate;
                    }
                }
            }
            return best;
        }

        /// <summary>Cuts every triangle touching a ramp cell along the cell edges it crosses.</summary>
        static void SplitAtRampEdges(RoadMeshBuffers buffers, CityGrid grid, IRoadQuery roads)
        {
            for (int s = 0; s < buffers.SubmeshCount; s++)
            {
                List<int> triangles = buffers.Submesh(s);
                Output.Clear();
                Pending.Clear();
                for (int i = triangles.Count - 1; i >= 0; i--) Pending.Push(triangles[i]);

                while (Pending.Count > 0)
                {
                    int a = Pending.Pop(), b = Pending.Pop(), c = Pending.Pop();
                    if (!TrySplit(buffers, grid, roads, a, b, c))
                    {
                        Output.Add(a);
                        Output.Add(b);
                        Output.Add(c);
                    }
                }

                triangles.Clear();
                triangles.AddRange(Output);
            }
        }

        /// <summary>Splits one triangle at the first cell edge through it, pushing the pieces back to be checked again.</summary>
        static bool TrySplit(RoadMeshBuffers buffers, CityGrid grid, IRoadQuery roads, int a, int b, int c)
        {
            float cs = grid.CellSize;
            Vector3 pa = buffers.Vertices[a], pb = buffers.Vertices[b], pc = buffers.Vertices[c];
            float minX = Mathf.Min(pa.x, Mathf.Min(pb.x, pc.x)), maxX = Mathf.Max(pa.x, Mathf.Max(pb.x, pc.x));
            float minZ = Mathf.Min(pa.z, Mathf.Min(pb.z, pc.z)), maxZ = Mathf.Max(pa.z, Mathf.Max(pb.z, pc.z));

            // Only near ramps: everywhere else the surface is flat and needs no extra vertices.
            if (!AnyRamp(grid, roads, minX - Epsilon, maxX + Epsilon, minZ - Epsilon, maxZ + Epsilon)) return false;

            for (int axis = 0; axis < 2; axis++)
            {
                float lo = axis == 0 ? minX : minZ;
                float hi = axis == 0 ? maxX : maxZ;
                for (int k = Mathf.CeilToInt((lo + Epsilon) / cs); k * cs < hi - Epsilon; k++)
                {
                    if (Clip(buffers, a, b, c, axis, k * cs)) return true;
                }
            }
            return false;
        }

        static bool AnyRamp(CityGrid grid, IRoadQuery roads, float minX, float maxX, float minZ, float maxZ)
        {
            float cs = grid.CellSize;
            for (int z = Mathf.FloorToInt(minZ / cs); z <= Mathf.FloorToInt(maxZ / cs); z++)
            {
                for (int x = Mathf.FloorToInt(minX / cs); x <= Mathf.FloorToInt(maxX / cs); x++)
                {
                    if (RampAt(roads, grid.Terrain, new GridCoord(x, z), out _)) return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Cuts triangle (a, b, c) by the line where the X (axis 0) or Z (axis 1) coordinate is
        /// <paramref name="at"/>, fanning each side back into triangles with the same winding.
        /// False when the line does not pass through it.
        /// </summary>
        static bool Clip(RoadMeshBuffers buffers, int a, int b, int c, int axis, float at)
        {
            Polygon.Clear();
            Polygon.Add(a);
            Polygon.Add(b);
            Polygon.Add(c);

            bool anyBelow = false, anyAbove = false;
            foreach (int index in Polygon)
            {
                float side = Coordinate(buffers.Vertices[index], axis) - at;
                anyBelow |= side < -Epsilon;
                anyAbove |= side > Epsilon;
            }
            if (!anyBelow || !anyAbove) return false;

            Below.Clear();
            Above.Clear();
            for (int i = 0; i < 3; i++)
            {
                int p = Polygon[i], q = Polygon[(i + 1) % 3];
                float sp = Side(buffers.Vertices[p], axis, at);
                float sq = Side(buffers.Vertices[q], axis, at);

                if (sp <= 0f) Below.Add(p);
                if (sp >= 0f) Above.Add(p);
                if (sp * sq < 0f)
                {
                    float t = sp / (sp - sq);
                    int r = Interpolate(buffers, p, q, t);
                    Below.Add(r);
                    Above.Add(r);
                }
            }

            PushFan(Below);
            PushFan(Above);
            return true;
        }

        static float Coordinate(Vector3 p, int axis) => axis == 0 ? p.x : p.z;

        // Signed distance from the line, snapped to zero within epsilon so on-line vertices go to both sides.
        static float Side(Vector3 p, int axis, float at)
        {
            float side = Coordinate(p, axis) - at;
            return Mathf.Abs(side) <= Epsilon ? 0f : side;
        }

        static int Interpolate(RoadMeshBuffers buffers, int p, int q, float t)
        {
            buffers.Vertices.Add(Vector3.Lerp(buffers.Vertices[p], buffers.Vertices[q], t));
            buffers.Normals.Add(Vector3.Lerp(buffers.Normals[p], buffers.Normals[q], t).normalized);
            buffers.Uvs.Add(Vector2.Lerp(buffers.Uvs[p], buffers.Uvs[q], t));
            return buffers.Vertices.Count - 1;
        }

        // Pushed in reverse so they pop in order; a piece already on one side pops straight to the output.
        static void PushFan(List<int> polygon)
        {
            for (int i = polygon.Count - 2; i >= 1; i--)
            {
                Pending.Push(polygon[i + 1]);
                Pending.Push(polygon[i]);
                Pending.Push(polygon[0]);
            }
        }
    }
}
