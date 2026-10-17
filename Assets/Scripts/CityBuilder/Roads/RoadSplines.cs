using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;

namespace CityBuilder.Roads
{
    /// <summary>
    /// Turns a <see cref="RoadChain"/> into a <see cref="Spline"/> on the ground plane.
    ///
    /// Straight stretches stay dead straight -- cells in a line drop out, and each straight curve has
    /// its handles at the thirds, so it runs at an even speed. Every bend is rounded with a circular
    /// arc that starts and ends <see cref="CornerRadius"/> cells either side of the bend, or less where
    /// two bends or the chain's end leave less room. Half a cell keeps a 90-degree turn inside its own
    /// cell, so a curve never cuts across a neighbour a building may stand on.
    ///
    /// Knots use <see cref="TangentMode.Broken"/> since each side of a knot is set by its own curve.
    /// </summary>
    public static class RoadSplines
    {
        /// <summary>Default for how far, in cells, a rounded bend reaches back along each leg.</summary>
        public const float CornerRadius = 0.5f;

        const float Epsilon = 1e-4f;

        static readonly List<Vector2> Corners = new List<Vector2>();
        static readonly List<Cubic> Curves = new List<Cubic>();

        struct Cubic
        {
            public Vector2 P0, P1, P2, P3;
        }

        /// <summary>
        /// Builds the spline for <paramref name="chain"/>, scaled from grid units to world units by
        /// <paramref name="cellSize"/>, at height zero. Returns null when the chain has no length.
        /// </summary>
        public static Spline Build(RoadChain chain, float cellSize, float cornerRadius = CornerRadius)
        {
            Simplify(chain.Points, chain.Closed, Corners);
            int count = Corners.Count;
            if (count < 2) return null;

            Curves.Clear();

            if (chain.Closed)
            {
                if (count < 3) return null;

                // Every point on a loop is a bend: arc round each, straight on to the next.
                for (int i = 0; i < count; i++)
                {
                    Fillet(i, true, cornerRadius, out _, out Vector2 leave, out Cubic arc);
                    Fillet((i + 1) % count, true, cornerRadius, out Vector2 nextEnter, out _, out _);
                    AddCurve(arc);
                    AddLine(leave, nextEnter);
                }
            }
            else
            {
                Vector2 from = Corners[0];
                for (int i = 1; i < count - 1; i++)
                {
                    Fillet(i, false, cornerRadius, out Vector2 enter, out Vector2 leave, out Cubic arc);
                    AddLine(from, enter);
                    AddCurve(arc);
                    from = leave;
                }
                AddLine(from, Corners[count - 1]);
            }

            if (Curves.Count == 0) return null;

            var spline = new Spline { Closed = chain.Closed };
            for (int i = 0; i < Curves.Count; i++)
            {
                Cubic curve = Curves[i];
                Cubic previous = Curves[(i - 1 + Curves.Count) % Curves.Count];

                // Open splines have nothing coming into their first knot.
                Vector2 tangentIn = i == 0 && !chain.Closed ? Vector2.zero : previous.P2 - previous.P3;
                spline.Add(Knot(curve.P0, tangentIn, curve.P1 - curve.P0, cellSize), TangentMode.Broken);
            }

            if (!chain.Closed)
            {
                Cubic last = Curves[Curves.Count - 1];
                spline.Add(Knot(last.P3, last.P2 - last.P3, Vector2.zero, cellSize), TangentMode.Broken);
            }

            return spline;
        }

        /// <summary>
        /// Rounds bend <paramref name="i"/>: the arc runs from <paramref name="enter"/> on the incoming
        /// leg to <paramref name="leave"/> on the outgoing one, both the same distance from the bend.
        /// </summary>
        static void Fillet(int i, bool closed, float radius, out Vector2 enter, out Vector2 leave, out Cubic arc)
        {
            int count = Corners.Count;
            Vector2 corner = Corners[i];
            Vector2 prev = Corners[(i - 1 + count) % count];
            Vector2 next = Corners[(i + 1) % count];

            float inLength = Vector2.Distance(prev, corner);
            float outLength = Vector2.Distance(corner, next);
            Vector2 din = (corner - prev) / inLength;
            Vector2 dout = (next - corner) / outLength;

            // A leg shared with another bend is split between the two; a leg to the chain's end is all ours.
            bool prevIsBend = closed || i - 1 > 0;
            bool nextIsBend = closed || i + 1 < count - 1;
            float reach = Mathf.Min(radius,
                                    prevIsBend ? inLength * 0.5f : inLength,
                                    nextIsBend ? outLength * 0.5f : outLength);

            enter = corner - din * reach;
            leave = corner + dout * reach;

            // A circular arc turning through `turn` radians, of radius reach / tan(turn / 2), is
            // matched by a cubic whose handles are (4/3) tan(turn / 4) times that radius.
            float turn = Mathf.Acos(Mathf.Clamp(Vector2.Dot(din, dout), -1f, 1f));
            float arcRadius = reach / Mathf.Tan(turn * 0.5f);
            float handle = 4f / 3f * Mathf.Tan(turn * 0.25f) * arcRadius;

            arc = new Cubic { P0 = enter, P1 = enter + din * handle, P2 = leave - dout * handle, P3 = leave };
        }

        static void AddLine(Vector2 a, Vector2 b)
        {
            Vector2 third = (b - a) / 3f;
            AddCurve(new Cubic { P0 = a, P1 = a + third, P2 = b - third, P3 = b });
        }

        static void AddCurve(Cubic curve)
        {
            if (Vector2.Distance(curve.P0, curve.P3) > Epsilon) Curves.Add(curve);
        }

        static BezierKnot Knot(Vector2 position, Vector2 tangentIn, Vector2 tangentOut, float scale) =>
            new BezierKnot(World(position, scale), World(tangentIn, scale), World(tangentOut, scale), quaternion.identity);

        static float3 World(Vector2 p, float scale) => new float3(p.x * scale, 0f, p.y * scale);

        /// <summary>Copies the points that matter -- the ends and every bend -- dropping ones in a straight line.</summary>
        static void Simplify(List<Vector2> points, bool closed, List<Vector2> result)
        {
            result.Clear();
            int count = points.Count;

            for (int i = 0; i < count; i++)
            {
                bool end = !closed && (i == 0 || i == count - 1);
                if (!end)
                {
                    Vector2 a = points[(i - 1 + count) % count];
                    Vector2 b = points[(i + 1) % count];
                    Vector2 p = points[i];
                    Vector2 din = (p - a).normalized;
                    Vector2 dout = (b - p).normalized;
                    if (Vector2.Dot(din, dout) > 1f - Epsilon) continue;
                }
                result.Add(points[i]);
            }
        }
    }
}
