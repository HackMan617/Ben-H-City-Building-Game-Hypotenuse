using System.Collections.Generic;
using CityBuilder.Grid;
using CityBuilder.Roads;
using NUnit.Framework;
using UnityEngine;

namespace CityBuilder.Tests
{
    /// <summary>
    /// Freehand drags, fed as pointer positions in grid units the way the tool feeds them each frame.
    /// </summary>
    public class FreehandPathTests
    {
        static GridCoord C(int x, int z) => new GridCoord(x, z);

        /// <summary>Moves the pointer from <paramref name="from"/> to <paramref name="to"/> in small increments.</summary>
        static void Drag(FreehandPath path, Vector2 from, Vector2 to, float increment = 0.05f)
        {
            int samples = Mathf.CeilToInt(Vector2.Distance(from, to) / increment);
            for (int i = 1; i <= samples; i++) path.Follow(Vector2.Lerp(from, to, i / (float)samples));
        }

        static List<Dir8> Steps(FreehandPath path)
        {
            var steps = new List<Dir8>();
            for (int i = 1; i < path.Cells.Count; i++) steps.Add(Dir8Util.FromDelta(path.Cells[i - 1], path.Cells[i]));
            return steps;
        }

        [Test]
        public void DiagonalDrag_LaysADiagonalNotAStaircase()
        {
            var path = new FreehandPath();
            path.Begin(C(0, 0));
            Drag(path, new Vector2(0.5f, 0.5f), new Vector2(5.5f, 5.5f));

            CollectionAssert.AreEqual(new[] { C(0, 0), C(1, 1), C(2, 2), C(3, 3), C(4, 4), C(5, 5) }, path.Cells);
        }

        [Test]
        public void EveryDirection_StepsThatWay()
        {
            foreach (Dir8 dir in Dir8Util.All)
            {
                var path = new FreehandPath();
                path.Begin(C(0, 0));
                GridCoord o = Dir8Util.Offset(dir);
                Drag(path, new Vector2(0.5f, 0.5f), new Vector2(0.5f + 3 * o.X, 0.5f + 3 * o.Z));

                Assert.AreEqual(4, path.Cells.Count, dir.ToString());
                Assert.IsTrue(Steps(path).TrueForAll(step => step == dir), dir.ToString());
            }
        }

        [Test]
        public void WobblyStraightDrag_StaysStraight()
        {
            var path = new FreehandPath();
            path.Begin(C(0, 0));

            // East with the hand drifting a third of a cell either side.
            var points = new[]
            {
                new Vector2(0.5f, 0.5f), new Vector2(2f, 0.8f), new Vector2(3.5f, 0.2f),
                new Vector2(5f, 0.8f), new Vector2(6.5f, 0.3f), new Vector2(8.5f, 0.5f),
            };
            for (int i = 1; i < points.Length; i++) Drag(path, points[i - 1], points[i]);

            Assert.IsTrue(Steps(path).TrueForAll(step => step == Dir8.E), string.Join(",", Steps(path)));
            Assert.AreEqual(C(8, 0), path.Cells[path.Cells.Count - 1]);
        }

        [Test]
        public void RightAngleTurn_KeepsItsSquaredCorner()
        {
            var path = new FreehandPath();
            path.Begin(C(0, 0));
            Drag(path, new Vector2(0.5f, 0.5f), new Vector2(4.5f, 0.5f));
            Drag(path, new Vector2(4.5f, 0.5f), new Vector2(4.5f, 4.5f));

            CollectionAssert.Contains(path.Cells, C(4, 0), "the corner cell should be on the path");
            Assert.IsFalse(Steps(path).Exists(Dir8Util.IsDiagonal), string.Join(",", Steps(path)));
        }

        [Test]
        public void StraightThenDiagonal_TurnsOnce()
        {
            var path = new FreehandPath();
            path.Begin(C(0, 0));
            Drag(path, new Vector2(0.5f, 0.5f), new Vector2(3.5f, 0.5f));
            Drag(path, new Vector2(3.5f, 0.5f), new Vector2(6.5f, 3.5f));

            CollectionAssert.AreEqual(new[] { Dir8.E, Dir8.E, Dir8.E, Dir8.NE, Dir8.NE, Dir8.NE }, Steps(path));
        }

        [Test]
        public void BackingUp_ReelsThePathIn()
        {
            var path = new FreehandPath();
            path.Begin(C(0, 0));
            Drag(path, new Vector2(0.5f, 0.5f), new Vector2(5.5f, 0.5f));
            Drag(path, new Vector2(5.5f, 0.5f), new Vector2(2.5f, 0.5f));

            CollectionAssert.AreEqual(new[] { C(0, 0), C(1, 0), C(2, 0) }, path.Cells);
        }

        [Test]
        public void PointerJump_FillsEveryCellBetween()
        {
            var path = new FreehandPath();
            path.Begin(C(0, 0));
            path.Follow(new Vector2(6.5f, 0.5f));

            CollectionAssert.AreEqual(new[] { C(0, 0), C(1, 0), C(2, 0), C(3, 0), C(4, 0), C(5, 0), C(6, 0) }, path.Cells);
        }

        [Test]
        public void SmallMovement_DoesNotStep()
        {
            var path = new FreehandPath();
            path.Begin(C(0, 0));

            Assert.IsFalse(path.Follow(new Vector2(0.95f, 0.9f)), "still inside the anchor cell");
            Assert.AreEqual(1, path.Cells.Count);
        }

        [Test]
        public void OutOfBounds_StopsAtTheEdge()
        {
            var bounded = new FreehandPath();
            bounded.Begin(C(0, 0));
            bounded.Follow(new Vector2(8.5f, 0.5f), cell => cell.X < 3);

            CollectionAssert.AreEqual(new[] { C(0, 0), C(1, 0), C(2, 0) }, bounded.Cells);
        }
    }
}
