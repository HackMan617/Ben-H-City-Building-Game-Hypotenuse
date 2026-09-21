using System.Collections.Generic;
using CityBuilder.Grid;
using CityBuilder.Roads;
using NUnit.Framework;

namespace CityBuilder.Tests
{
    /// <summary>
    /// Locks down the drag shapes. These are the rules the tool feel rests on, and they are pure
    /// data -- no scene, no camera, no input -- so they are cheap to keep honest.
    /// </summary>
    public class RoadPathResolverTests
    {
        readonly List<GridCoord> _path = new List<GridCoord>();

        /// <summary>Every stroke must be walkable: consecutive cells exactly one step apart.</summary>
        static void AssertContiguous(IReadOnlyList<GridCoord> path, bool allowDiagonal)
        {
            for (int i = 1; i < path.Count; i++)
            {
                Dir8 step = Dir8Util.FromDelta(path[i - 1], path[i]);
                Assert.AreNotEqual(Dir8.None, step,
                    $"cells {path[i - 1]} -> {path[i]} are not one step apart");

                if (!allowDiagonal)
                {
                    Assert.IsFalse(Dir8Util.IsDiagonal(step),
                        $"unexpected diagonal step {path[i - 1]} -> {path[i]}");
                }
            }
        }

        /// <summary>Counts how many times the stroke changes direction.</summary>
        static int CountTurns(IReadOnlyList<GridCoord> path)
        {
            int turns = 0;
            Dir8 previous = Dir8.None;
            for (int i = 1; i < path.Count; i++)
            {
                Dir8 step = Dir8Util.FromDelta(path[i - 1], path[i]);
                if (previous != Dir8.None && step != previous) turns++;
                previous = step;
            }
            return turns;
        }

        [Test]
        public void SingleClick_ProducesExactlyTheAnchorCell()
        {
            var anchor = new GridCoord(3, -2);
            RoadPathResolver.Resolve(anchor, anchor, PathMode.ElbowAxisOnly, _path);

            Assert.AreEqual(1, _path.Count);
            Assert.AreEqual(anchor, _path[0]);
        }

        [Test]
        public void PureHorizontalDrag_RunsStraightWithNoTurns()
        {
            RoadPathResolver.Resolve(new GridCoord(0, 0), new GridCoord(5, 0), PathMode.ElbowAxisOnly, _path);

            Assert.AreEqual(6, _path.Count);
            Assert.AreEqual(0, CountTurns(_path));
            foreach (GridCoord cell in _path) Assert.AreEqual(0, cell.Z);
            AssertContiguous(_path, allowDiagonal: false);
        }

        [Test]
        public void Elbow_ReachesCursorWithASingleSquaredCorner()
        {
            var anchor = new GridCoord(0, 0);
            var cursor = new GridCoord(6, 3);
            RoadPathResolver.Resolve(anchor, cursor, PathMode.ElbowAxisOnly, _path);

            Assert.AreEqual(anchor, _path[0]);
            Assert.AreEqual(cursor, _path[_path.Count - 1]);
            Assert.AreEqual(1, CountTurns(_path), "an elbow must turn exactly once");
            AssertContiguous(_path, allowDiagonal: false);

            // Manhattan distance plus the starting cell: no cell is visited twice.
            Assert.AreEqual(6 + 3 + 1, _path.Count);
        }

        [Test]
        public void Elbow_LeadsWithTheDominantAxis()
        {
            // Wider than tall: the first step should be horizontal.
            RoadPathResolver.Resolve(new GridCoord(0, 0), new GridCoord(8, 2), PathMode.ElbowAxisOnly, _path);
            Assert.AreEqual(Dir8.E, Dir8Util.FromDelta(_path[0], _path[1]));

            // Taller than wide: the first step should be vertical.
            RoadPathResolver.Resolve(new GridCoord(0, 0), new GridCoord(2, 8), PathMode.ElbowAxisOnly, _path);
            Assert.AreEqual(Dir8.N, Dir8Util.FromDelta(_path[0], _path[1]));
        }

        [Test]
        public void Elbow_OnASquareDrag_HonoursTheRememberedAxis()
        {
            // A 5x5 drag has no dominant axis; the caller's preference decides, which is what stops
            // the elbow flipping back and forth while the pointer sits on the diagonal.
            RoadPathResolver.Resolve(new GridCoord(0, 0), new GridCoord(5, 5), PathMode.ElbowAxisOnly,
                                     _path, preferHorizontalFirst: true);
            Assert.AreEqual(Dir8.E, Dir8Util.FromDelta(_path[0], _path[1]));

            RoadPathResolver.Resolve(new GridCoord(0, 0), new GridCoord(5, 5), PathMode.ElbowAxisOnly,
                                     _path, preferHorizontalFirst: false);
            Assert.AreEqual(Dir8.N, Dir8Util.FromDelta(_path[0], _path[1]));
        }

        [Test]
        public void Elbow_HandlesAllFourQuadrants()
        {
            var anchor = new GridCoord(0, 0);
            GridCoord[] cursors =
            {
                new GridCoord( 7,  4), new GridCoord(-7,  4),
                new GridCoord( 7, -4), new GridCoord(-7, -4),
            };

            foreach (GridCoord cursor in cursors)
            {
                RoadPathResolver.Resolve(anchor, cursor, PathMode.ElbowAxisOnly, _path);
                Assert.AreEqual(cursor, _path[_path.Count - 1], $"failed to reach {cursor}");
                Assert.AreEqual(1, CountTurns(_path), $"wrong turn count reaching {cursor}");
                AssertContiguous(_path, allowDiagonal: false);
            }
        }

        [Test]
        public void StraightMode_DropsTheMinorAxisEntirely()
        {
            RoadPathResolver.Resolve(new GridCoord(0, 0), new GridCoord(6, 2), PathMode.StraightAxisOnly, _path);

            Assert.AreEqual(0, CountTurns(_path));
            Assert.AreEqual(new GridCoord(6, 0), _path[_path.Count - 1], "the drag should stay on z = 0");
        }

        [Test]
        public void DiagonalMode_RunsDiagonalThenStraightAndStillLandsOnTheCursor()
        {
            var cursor = new GridCoord(8, 3);
            RoadPathResolver.Resolve(new GridCoord(0, 0), cursor, PathMode.ElbowWithDiagonal, _path);

            Assert.AreEqual(cursor, _path[_path.Count - 1]);
            AssertContiguous(_path, allowDiagonal: true);

            // Three diagonal steps consume the shorter axis, then five straight steps finish the run.
            Assert.AreEqual(3 + 5 + 1, _path.Count);
            Assert.AreEqual(Dir8.NE, Dir8Util.FromDelta(_path[0], _path[1]));
            Assert.AreEqual(Dir8.E, Dir8Util.FromDelta(_path[_path.Count - 2], _path[_path.Count - 1]));
        }

        [Test]
        public void DiagonalMode_OnASquareDragIsOneCleanRun()
        {
            RoadPathResolver.Resolve(new GridCoord(0, 0), new GridCoord(5, 5), PathMode.ElbowWithDiagonal, _path);

            Assert.AreEqual(6, _path.Count);
            Assert.AreEqual(0, CountTurns(_path));
            AssertContiguous(_path, allowDiagonal: true);
        }

        [Test]
        public void DiagonalMode_StraightFirstSwapsTheLegsButKeepsTheEnds()
        {
            var cursor = new GridCoord(-8, 3);
            RoadPathResolver.Resolve(new GridCoord(0, 0), cursor, PathMode.ElbowWithDiagonal, _path,
                                     diagonalFirst: false);

            Assert.AreEqual(new GridCoord(0, 0), _path[0]);
            Assert.AreEqual(cursor, _path[_path.Count - 1]);
            AssertContiguous(_path, allowDiagonal: true);

            Assert.AreEqual(3 + 5 + 1, _path.Count);
            Assert.AreEqual(1, CountTurns(_path), "one 135-degree turn, nothing else");
            Assert.AreEqual(Dir8.W, Dir8Util.FromDelta(_path[0], _path[1]));
            Assert.AreEqual(Dir8.NW, Dir8Util.FromDelta(_path[_path.Count - 2], _path[_path.Count - 1]));
        }

        [Test]
        public void DiagonalMode_AxisAlignedDragHasNoDiagonalLeg()
        {
            foreach (bool diagonalFirst in new[] { true, false })
            {
                RoadPathResolver.Resolve(new GridCoord(0, 0), new GridCoord(0, -4), PathMode.ElbowWithDiagonal,
                                         _path, diagonalFirst: diagonalFirst);

                Assert.AreEqual(5, _path.Count);
                AssertContiguous(_path, allowDiagonal: false);
            }
        }
    }
}
