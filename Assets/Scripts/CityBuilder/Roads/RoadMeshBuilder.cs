using System.Collections.Generic;
using CityBuilder.Grid;
using UnityEngine;

namespace CityBuilder.Roads
{
    /// <summary>
    /// Builds road geometry from a cell set and its junction masks.
    ///
    /// Every cell is built from three kinds of piece, all extruded to curb height:
    ///
    ///   arm    one strip per connection, from the cell centre to where it meets the neighbour --
    ///          the cell edge for N/E/S/W, the cell corner for a diagonal. The neighbour's arm
    ///          starts on the same line, so the two meet flush.
    ///   back   fills any gap wider than 180 degrees between two neighbouring arms, i.e. the
    ///          outside of a turn. A dead end's back is a half-round cap instead.
    ///   disc   an isolated cell with no connections at all, a road-width across.
    ///
    /// A 90-degree turn keeps the squared outer corner it always had; a 135-degree turn (diagonal
    /// into straight) gets a single sharp mitre; every dead end, straight or diagonal, is rounded off.
    ///
    /// A 45-degree road cannot stay inside its own square -- at this width it always spills over
    /// the corners of the two cells beside each diagonal step -- so pieces are allowed to overlap.
    /// Where they do, the tops are coplanar and the curb walls of one piece sit underneath the top
    /// of another, so from above the union reads as one solid road with every corner sharp.
    /// </summary>
    public static class RoadMeshBuilder
    {
        // Reused across rebuilds so a drag does not churn the heap every frame.
        static readonly RoadMeshBuffers Shared = new RoadMeshBuffers();

        // Where the pieces being built go: Shared for Build, the caller's buffers for Append.
        static RoadMeshBuffers _out = Shared;
        static List<Vector3> Vertices => _out.Vertices;
        static List<Vector3> Normals => _out.Normals;
        static List<Vector2> Uvs => _out.Uvs;
        static List<int> Triangles => _out.Base;

        static readonly Dir8[] Arms = new Dir8[8];
        static readonly List<Vector2> Outline = new List<Vector2>(4);

        const float Sqrt2 = 1.41421356f;

        /// <summary>Straight sides a dead end's half-round cap is made of.</summary>
        const int CapSegments = 12;

        /// <summary>
        /// Writes geometry for <paramref name="cells"/> into <paramref name="mesh"/>. Junction shape
        /// is read from <paramref name="query"/>, which may know about cells outside the list -- that
        /// is how a preview stroke picks up the T-junction where it meets committed road.
        /// </summary>
        public static void Build(Mesh mesh, IRoadQuery query, IEnumerable<GridCoord> cells, RoadProfile profile)
        {
            Shared.Clear(null);

            foreach (GridCoord cell in cells)
            {
                if (!query.Has(cell)) continue;
                Append(Shared, cell, query.MaskAt(cell), profile);
            }

            Shared.WriteTo(mesh);
        }

        /// <summary>Adds one cell's pieces to the base submesh of <paramref name="target"/>.</summary>
        public static void Append(RoadMeshBuffers target, GridCoord cell, Dir8 mask, RoadProfile profile)
        {
            _out = target;
            try
            {
                AppendCell(cell, mask, profile);
            }
            finally
            {
                _out = Shared;
            }
        }

        /// <summary>
        /// Adds the junction where two diagonals cross at a cell corner: four arms out from the corner,
        /// one along each diagonal, reaching <see cref="RoadChains.CrossingReach"/> cells to where the
        /// roads' splines begin. <paramref name="corner"/> is in grid units.
        /// </summary>
        public static void AppendCornerCrossing(RoadMeshBuffers target, Vector2 corner, RoadProfile profile)
        {
            _out = target;
            try
            {
                float cs = profile.CellSize;
                Vector2 c = corner * cs;
                float length = RoadChains.CrossingReach * cs;
                foreach (Dir8 dir in new[] { Dir8.NE, Dir8.SE, Dir8.SW, Dir8.NW })
                {
                    AppendStrip(c, Unit(dir), length, profile.HalfRoad, profile.Height, cs);
                }
            }
            finally
            {
                _out = Shared;
            }
        }

        static void AppendCell(GridCoord cell, Dir8 mask, RoadProfile profile)
        {
            float cs = profile.CellSize;
            float hr = profile.HalfRoad;
            float h = profile.Height;

            // Plan-view coordinates: Vector2.x is world X, Vector2.y is world Z.
            var centre = new Vector2((cell.X + 0.5f) * cs, (cell.Z + 0.5f) * cs);

            // Arms in clockwise order from north, which is the order the gaps are walked in below.
            int count = 0;
            foreach (Dir8 dir in Dir8Util.All)
            {
                if ((mask & dir) != 0) Arms[count++] = dir;
            }

            if (count == 0)
            {
                AppendIsolated(centre, hr, h, cs);
                return;
            }

            for (int i = 0; i < count; i++) AppendArm(centre, Arms[i], hr, h, cs);

            for (int i = 0; i < count; i++)
            {
                Dir8 a = Arms[i];
                Dir8 b = Arms[(i + 1) % count];

                // Gap from a clockwise round to b, in 45-degree steps. A lone arm faces a full turn.
                int steps = count == 1 ? 8 : (Dir8Util.BitIndex(b) - Dir8Util.BitIndex(a) + 8) % 8;

                // Up to 180 degrees the arms' own sides already close the outline.
                if (steps > 4) AppendBack(centre, a, b, steps, hr, h, cs);
            }
        }

        /// <summary>No connections: a round disc, curbed all the way round, matching the rounded dead ends.</summary>
        static void AppendIsolated(Vector2 c, float hr, float h, float cs)
        {
            // Clockwise from north seen from above, so a fan from the centre is front-facing.
            int segments = CapSegments * 2;
            for (int i = 0; i < segments; i++)
            {
                float a0 = 2f * Mathf.PI * i / segments;
                float a1 = 2f * Mathf.PI * (i + 1) / segments;
                Vector2 p = c + new Vector2(Mathf.Sin(a0), Mathf.Cos(a0)) * hr;
                Vector2 q = c + new Vector2(Mathf.Sin(a1), Mathf.Cos(a1)) * hr;

                AddTopTriangle(c, p, q, h, cs);
                if (h > 0.0001f) AddWall(p, q, ((p + q) * 0.5f - c).normalized, h, cs);
            }
        }

        /// <summary>
        /// Strip from the centre out to where this cell meets its neighbour. Only the long sides get
        /// curbs: the far end is flush against the neighbour's matching arm, and the root end is
        /// buried in the centre of the junction.
        /// </summary>
        static void AppendArm(Vector2 c, Dir8 dir, float hr, float h, float cs)
        {
            float length = 0.5f * cs * (Dir8Util.IsDiagonal(dir) ? Sqrt2 : 1f);
            AppendStrip(c, Unit(dir), length, hr, h, cs);
        }

        /// <summary>A strip <paramref name="length"/> long from <paramref name="c"/> along <paramref name="u"/>, curbed on its long sides.</summary>
        static void AppendStrip(Vector2 c, Vector2 u, float length, float hr, float h, float cs)
        {
            Vector2 r = Right(u);

            Vector2 leftRoot = c - r * hr;
            Vector2 rightRoot = c + r * hr;
            Vector2 leftEnd = leftRoot + u * length;
            Vector2 rightEnd = rightRoot + u * length;

            AddTopQuad(leftRoot, leftEnd, rightEnd, rightRoot, h, cs);

            if (h <= 0.0001f) return;
            AddWall(rightRoot, rightEnd, r, h, cs);
            AddWall(leftEnd, leftRoot, -r, h, cs);
        }

        /// <summary>
        /// Fills the gap on the outside of two neighbouring arms, from <paramref name="a"/>'s right
        /// side clockwise round to <paramref name="b"/>'s left side.
        ///
        ///   225 / 270   one corner where the two sides meet: a sharp 135-degree mitre, or the
        ///               squared back of a 90-degree turn.
        ///   315         those sides would meet far outside the cell, so the back is cut off square
        ///               instead, half a road-width behind the centre, square to the orthogonal arm
        ///               so the fork keeps a grid-aligned back.
        ///   360         a dead end: a half-round cap, half a road-width behind the centre.
        /// </summary>
        static void AppendBack(Vector2 c, Dir8 a, Dir8 b, int steps, float hr, float h, float cs)
        {
            Vector2 ua = Unit(a);
            Vector2 ub = Unit(b);
            Vector2 start = c + Right(ua) * hr;
            Vector2 end = c - Right(ub) * hr;

            Outline.Clear();
            Outline.Add(start);

            if (steps == 8)
            {
                // A dead end: half a circle, a road-width across, swung clockwise from the arm's right
                // side round behind the centre to its left. It stays inside the cell and the strip.
                Vector2 side = Right(ua);
                for (int i = 1; i < CapSegments; i++)
                {
                    float angle = Mathf.PI * i / CapSegments;
                    Outline.Add(c + (side * Mathf.Cos(angle) - ua * Mathf.Sin(angle)) * hr);
                }
            }
            else if (steps <= 6)
            {
                Outline.Add(Intersect(start, ua, end, ub));
            }
            else
            {
                Vector2 capNormal = !Dir8Util.IsDiagonal(a) ? ua : ub;
                Vector2 capPoint = c - capNormal * hr;
                Vector2 capAlong = Right(capNormal);

                Outline.Add(Intersect(start, ua, capPoint, capAlong));
                Outline.Add(Intersect(end, ub, capPoint, capAlong));
            }

            Outline.Add(end);

            // The outline runs clockwise round the centre, so a fan from the centre is front-facing.
            for (int i = 0; i + 1 < Outline.Count; i++) AddTopTriangle(c, Outline[i], Outline[i + 1], h, cs);

            if (h <= 0.0001f) return;
            for (int i = 0; i + 1 < Outline.Count; i++)
            {
                Vector2 p = Outline[i];
                Vector2 q = Outline[i + 1];
                Vector2 normal = Right((q - p).normalized);
                if (Vector2.Dot(normal, (p + q) * 0.5f - c) < 0f) normal = -normal;
                AddWall(p, q, normal, h, cs);
            }
        }

        /// <summary>Unit vector for a direction in plan view.</summary>
        static Vector2 Unit(Dir8 dir)
        {
            GridCoord o = Dir8Util.Offset(dir);
            return new Vector2(o.X, o.Z).normalized;
        }

        /// <summary>Rotates a plan-view vector 90 degrees clockwise seen from above: north becomes east.</summary>
        static Vector2 Right(Vector2 v) => new Vector2(v.y, -v.x);

        /// <summary>Where the line through <paramref name="p"/> along <paramref name="u"/> meets the one through <paramref name="q"/> along <paramref name="v"/>.</summary>
        static Vector2 Intersect(Vector2 p, Vector2 u, Vector2 q, Vector2 v)
        {
            float denominator = u.x * v.y - u.y * v.x;
            if (Mathf.Abs(denominator) < 1e-6f) return p;

            Vector2 w = q - p;
            float t = (w.x * v.y - w.y * v.x) / denominator;
            return p + u * t;
        }

        /// <summary>
        /// Upward-facing quad; corners must run clockwise seen from above, which is front-facing in
        /// the left-handed space Unity uses. UVs are world-planar so road texture runs unbroken
        /// across cells and pieces.
        /// </summary>
        static void AddTopQuad(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float y, float cellSize)
        {
            int b = Vertices.Count;
            AddTopVertex(p0, y, cellSize);
            AddTopVertex(p1, y, cellSize);
            AddTopVertex(p2, y, cellSize);
            AddTopVertex(p3, y, cellSize);
            AddQuadIndices(b);
        }

        static void AddTopTriangle(Vector2 p0, Vector2 p1, Vector2 p2, float y, float cellSize)
        {
            int b = Vertices.Count;
            AddTopVertex(p0, y, cellSize);
            AddTopVertex(p1, y, cellSize);
            AddTopVertex(p2, y, cellSize);
            Triangles.Add(b);
            Triangles.Add(b + 1);
            Triangles.Add(b + 2);
        }

        static void AddTopVertex(Vector2 p, float y, float cellSize)
        {
            Vertices.Add(new Vector3(p.x, y, p.y));
            Normals.Add(Vector3.up);
            Uvs.Add(new Vector2(p.x / cellSize, p.y / cellSize));
        }

        /// <summary>
        /// Vertical curb face between two plan-view points, facing outward along
        /// <paramref name="outward"/>. The face has to run along Cross(up, -normal) -- the right-hand
        /// direction for a viewer looking back at it -- to wind front-facing, so the endpoints are
        /// swapped when they arrive the other way round.
        /// </summary>
        static void AddWall(Vector2 from, Vector2 to, Vector2 outward, float height, float cellSize)
        {
            float length = Vector2.Distance(from, to);
            if (length <= 0.0001f) return;

            var normal = new Vector3(outward.x, 0f, outward.y).normalized;
            Vector3 along = Vector3.Cross(Vector3.up, -normal);

            var start = new Vector3(from.x, 0f, from.y);
            var finish = new Vector3(to.x, 0f, to.y);
            if (Vector3.Dot(finish - start, along) < 0f) (start, finish) = (finish, start);

            Vector3 up = Vector3.up * height;

            int b = Vertices.Count;
            Vertices.Add(start);
            Vertices.Add(start + up);
            Vertices.Add(finish + up);
            Vertices.Add(finish);

            for (int i = 0; i < 4; i++) Normals.Add(normal);

            Uvs.Add(new Vector2(0f, 0f));
            Uvs.Add(new Vector2(0f, height / cellSize));
            Uvs.Add(new Vector2(length / cellSize, height / cellSize));
            Uvs.Add(new Vector2(length / cellSize, 0f));

            AddQuadIndices(b);
        }

        static void AddQuadIndices(int b)
        {
            Triangles.Add(b);
            Triangles.Add(b + 1);
            Triangles.Add(b + 2);
            Triangles.Add(b);
            Triangles.Add(b + 2);
            Triangles.Add(b + 3);
        }
    }
}
