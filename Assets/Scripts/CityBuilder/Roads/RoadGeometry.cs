using System.Collections.Generic;
using CityBuilder.Grid;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;

namespace CityBuilder.Roads
{
    /// <summary>
    /// Builds road geometry as splines between junctions.
    ///
    /// The layout is split by <see cref="RoadChains"/>. Junctions, dead ends and lone cells get the
    /// matching model from <see cref="RoadModels"/> when there is one, or the generated pieces of
    /// <see cref="RoadMeshBuilder"/> otherwise. Each run between them becomes a spline through its cell
    /// centres with rounded bends (<see cref="RoadSplines"/>), and the segment model is repeated along
    /// it, bent to follow the curve and stretched a little so a whole number of copies fits exactly.
    /// With no segment model a plain generated strip is bent instead.
    ///
    /// Everything comes out in world space with the grid's origin at zero, the same as
    /// <see cref="RoadMeshBuilder"/>.
    /// </summary>
    public static class RoadGeometry
    {
        const int SamplesPerCurve = 24;

        static readonly List<GridCoord> Nodes = new List<GridCoord>();
        static readonly List<RoadChain> Chains = new List<RoadChain>();
        static readonly List<Vector2> Crossings = new List<Vector2>();

        // Arc-length table for the spline being swept: distance along it at each (curve, t) sample.
        static readonly List<BezierCurve> Curves = new List<BezierCurve>();
        static readonly List<float> SampleDistance = new List<float>();
        static readonly List<int> SampleCurve = new List<int>();
        static readonly List<float> SampleT = new List<float>();

        static RoadPiece _generatedSegment;
        static RoadProfile _generatedFor;

        /// <summary>
        /// Adds geometry for <paramref name="cells"/> to <paramref name="buffers"/>. Links are read from
        /// <paramref name="query"/>, which may know about cells outside the list. <paramref name="models"/>
        /// may be null to generate everything. The spline for each run is added to
        /// <paramref name="splines"/> when it is given.
        /// </summary>
        public static void Build(RoadMeshBuffers buffers, IRoadQuery query, IEnumerable<GridCoord> cells,
                                 RoadProfile profile, RoadModels models, List<Spline> splines = null)
        {
            RoadChains.Extract(query, cells, Nodes, Chains, Crossings);
            float cs = profile.CellSize;

            foreach (GridCoord node in Nodes)
            {
                Dir8 mask = query.MaskAt(node);
                int turns = 0;
                RoadPiece junction = models != null ? models.JunctionFor(mask, out turns) : null;
                if (junction != null) Stamp(buffers, junction, CellCentre(node, cs), 90f * turns, cs);
                else RoadMeshBuilder.Append(buffers, node, mask, profile);
            }

            // Two diagonals crossing at a corner: the crossroads model turned onto the diagonals.
            foreach (Vector2 corner in Crossings)
            {
                if (models?.Cross != null) Stamp(buffers, models.Cross, new Vector3(corner.x * cs, 0f, corner.y * cs), 45f, cs);
                else RoadMeshBuilder.AppendCornerCrossing(buffers, corner, profile);
            }

            RoadPiece segment = models?.Segment ?? GeneratedSegment(profile);
            foreach (RoadChain chain in Chains)
            {
                Spline spline = RoadSplines.Build(chain, cs);
                if (spline == null) continue;

                splines?.Add(spline);
                Sweep(buffers, spline, segment, cs);
            }
        }

        static RoadPiece GeneratedSegment(RoadProfile profile)
        {
            bool stale = _generatedSegment == null
                      || _generatedFor.CellSize != profile.CellSize
                      || _generatedFor.WidthFraction != profile.WidthFraction
                      || _generatedFor.Height != profile.Height;
            if (stale)
            {
                _generatedSegment = RoadPiece.GeneratedSegment(profile);
                _generatedFor = profile;
            }
            return _generatedSegment;
        }

        static Vector3 CellCentre(GridCoord cell, float cs) => new Vector3((cell.X + 0.5f) * cs, 0f, (cell.Z + 0.5f) * cs);

        /// <summary>Places a junction model at <paramref name="centre"/>, turned clockwise seen from above by <paramref name="degrees"/>.</summary>
        static void Stamp(RoadMeshBuffers buffers, RoadPiece piece, Vector3 centre, float degrees, float cs)
        {
            Quaternion turn = Quaternion.Euler(0f, degrees, 0f);

            int first = buffers.Vertices.Count;
            for (int i = 0; i < piece.Vertices.Length; i++)
            {
                buffers.Vertices.Add(centre + turn * (piece.Vertices[i] * cs));
                buffers.Normals.Add(turn * piece.Normals[i]);
                buffers.Uvs.Add(piece.Uvs[i]);
            }
            AddParts(buffers, piece, first);
        }

        /// <summary>
        /// Repeats <paramref name="piece"/> along <paramref name="spline"/>. The piece's Z runs along
        /// the spline, X across it to the right and Y straight up, so the road stays flat on the
        /// ground however it bends.
        /// </summary>
        static void Sweep(RoadMeshBuffers buffers, Spline spline, RoadPiece piece, float cs)
        {
            float total = BuildLengthTable(spline);
            float pieceLength = piece.Bounds.size.z;
            if (total <= 1e-4f || pieceLength <= 1e-4f) return;

            int copies = Mathf.Max(1, Mathf.RoundToInt(total / (pieceLength * cs)));
            float scaleAlong = total / (copies * pieceLength);
            float zMin = piece.Bounds.min.z;

            for (int copy = 0; copy < copies; copy++)
            {
                int first = buffers.Vertices.Count;
                for (int i = 0; i < piece.Vertices.Length; i++)
                {
                    Vector3 v = piece.Vertices[i];
                    float distance = (copy * pieceLength + v.z - zMin) * scaleAlong;
                    Sample(distance, out Vector3 position, out Vector3 forward);

                    Vector3 right = Vector3.Cross(Vector3.up, forward);
                    Vector3 n = piece.Normals[i];

                    buffers.Vertices.Add(position + right * (v.x * cs) + Vector3.up * (v.y * cs));
                    buffers.Normals.Add((right * n.x + Vector3.up * n.y + forward * n.z).normalized);
                    buffers.Uvs.Add(piece.Uvs[i]);
                }
                AddParts(buffers, piece, first);
            }
        }

        static void AddParts(RoadMeshBuffers buffers, RoadPiece piece, int first)
        {
            for (int part = 0; part < piece.Triangles.Length; part++)
            {
                List<int> target = buffers.TrianglesFor(piece.Materials[part]);
                foreach (int index in piece.Triangles[part]) target.Add(first + index);
            }
        }

        /// <summary>Samples every curve of <paramref name="spline"/> and returns its total length.</summary>
        static float BuildLengthTable(Spline spline)
        {
            Curves.Clear();
            SampleDistance.Clear();
            SampleCurve.Clear();
            SampleT.Clear();

            int curveCount = spline.GetCurveCount();
            for (int i = 0; i < curveCount; i++) Curves.Add(spline.GetCurve(i));

            float distance = 0f;
            Vector3 last = (Vector3)CurveUtility.EvaluatePosition(Curves[0], 0f);
            AddSample(0f, 0, 0f);

            for (int c = 0; c < Curves.Count; c++)
            {
                for (int s = 1; s <= SamplesPerCurve; s++)
                {
                    float t = s / (float)SamplesPerCurve;
                    Vector3 point = (Vector3)CurveUtility.EvaluatePosition(Curves[c], t);
                    distance += Vector3.Distance(last, point);
                    last = point;
                    AddSample(distance, c, t);
                }
            }
            return distance;
        }

        static void AddSample(float distance, int curve, float t)
        {
            SampleDistance.Add(distance);
            SampleCurve.Add(curve);
            SampleT.Add(t);
        }

        /// <summary>Position and flat unit heading at <paramref name="distance"/> along the swept spline.</summary>
        static void Sample(float distance, out Vector3 position, out Vector3 forward)
        {
            int count = SampleDistance.Count;
            distance = Mathf.Clamp(distance, 0f, SampleDistance[count - 1]);

            // First sample at or past the distance.
            int lo = 0, hi = count - 1;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (SampleDistance[mid] < distance) lo = mid + 1;
                else hi = mid;
            }

            int curve = SampleCurve[lo];
            float t = SampleT[lo];
            if (lo > 0)
            {
                // The sample before may close the previous curve; that is t = 0 on this one.
                float t0 = SampleCurve[lo - 1] == curve ? SampleT[lo - 1] : 0f;
                float d0 = SampleDistance[lo - 1];
                float span = SampleDistance[lo] - d0;
                if (span > 1e-6f) t = Mathf.Lerp(t0, t, (distance - d0) / span);
            }

            BezierCurve bezier = Curves[curve];
            position = (Vector3)CurveUtility.EvaluatePosition(bezier, t);

            Vector3 tangent = (Vector3)CurveUtility.EvaluateTangent(bezier, t);
            tangent.y = 0f;
            if (tangent.sqrMagnitude < 1e-10f)
            {
                float3 chord = bezier.P3 - bezier.P0;
                tangent = new Vector3(chord.x, 0f, chord.z);
            }
            forward = tangent.normalized;
        }
    }
}
