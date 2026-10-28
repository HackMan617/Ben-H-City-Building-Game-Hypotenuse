using System.Collections.Generic;
using CityBuilder.Buildings;
using CityBuilder.Content;
using CityBuilder.Grid;
using CityBuilder.Roads;
using CityBuilder.Saves;
using NUnit.Framework;

namespace CityBuilder.Tests
{
    /// <summary>
    /// Terrain levels: raising and lowering stay in range, roads cannot link across a level change,
    /// buildings need flat ground, and the levels survive a save.
    /// </summary>
    public class TerrainTests
    {
        static GridCoord C(int x, int z) => new GridCoord(x, z);

        [Test]
        public void ShiftingStaysWithinTheLevelRange()
        {
            var terrain = new TerrainMap();
            terrain.Shift(new[] { C(0, 0) }, -1);
            Assert.AreEqual(0, terrain.LevelAt(C(0, 0)));

            for (int i = 0; i < TerrainMap.MaxLevel + 3; i++) terrain.Shift(new[] { C(0, 0) }, 1);
            Assert.AreEqual(TerrainMap.MaxLevel, terrain.LevelAt(C(0, 0)));
            Assert.AreEqual(TerrainMap.MaxLevel, terrain.Highest);
        }

        [Test]
        public void LevelZeroCellsAreNotStored()
        {
            var terrain = new TerrainMap();
            terrain.Shift(new[] { C(1, 1), C(2, 2) }, 1);
            terrain.Shift(new[] { C(1, 1) }, -1);
            Assert.AreEqual(1, terrain.Count);
        }

        [Test]
        public void ChangedFiresOncePerShift()
        {
            var terrain = new TerrainMap();
            int fired = 0;
            terrain.Changed += () => fired++;
            terrain.Shift(new[] { C(0, 0), C(1, 0), C(2, 0) }, 1);
            Assert.AreEqual(1, fired);
        }

        static bool Breaks(TerrainMap terrain, RoadNetwork network, params GridCoord[] stroke)
        {
            var edit = new RoadEdit();
            RoadStroke.Plan(network, stroke, false, edit);
            return RoadTerrain.BreaksSlopes(network, edit, terrain);
        }

        static RoadNetwork Built(params GridCoord[] stroke)
        {
            var network = new RoadNetwork();
            var edit = new RoadEdit();
            RoadStroke.Plan(network, stroke, false, edit);
            edit.Apply(network);
            return network;
        }

        [Test]
        public void ARoadCanClimbOneLevelInAStraightLine()
        {
            var terrain = new TerrainMap();
            terrain.Shift(new[] { C(2, 0), C(3, 0) }, 1);

            Assert.IsFalse(Breaks(terrain, new RoadNetwork(), C(0, 0), C(1, 0), C(2, 0), C(3, 0)));

            RoadNetwork network = Built(C(0, 0), C(1, 0), C(2, 0), C(3, 0));
            Assert.IsTrue(RoadTerrain.RampAt(network, terrain, C(1, 0), out Dir8 uphill));
            Assert.AreEqual(Dir8.E, uphill);
            Assert.IsFalse(RoadTerrain.RampAt(network, terrain, C(2, 0), out _));
        }

        [Test]
        public void RampsCanFollowEachOtherUpSeveralLevels()
        {
            var terrain = new TerrainMap();
            terrain.Shift(new[] { C(0, 1), C(0, 2), C(0, 3) }, 1);
            terrain.Shift(new[] { C(0, 2), C(0, 3) }, 1);

            Assert.IsFalse(Breaks(terrain, new RoadNetwork(), C(0, 0), C(0, 1), C(0, 2), C(0, 3)));
        }

        [Test]
        public void ARoadCannotClimbTwoLevelsAtOnce()
        {
            var terrain = new TerrainMap();
            terrain.Shift(new[] { C(1, 0) }, 2);

            Assert.IsTrue(Breaks(terrain, new RoadNetwork(), C(0, 0), C(1, 0)));
        }

        [Test]
        public void ARoadCannotClimbDiagonally()
        {
            var terrain = new TerrainMap();
            terrain.Shift(new[] { C(1, 1) }, 1);

            Assert.IsTrue(Breaks(terrain, new RoadNetwork(), C(0, 0), C(1, 1)));
        }

        [Test]
        public void ARampCannotCarryAJunction()
        {
            var terrain = new TerrainMap();
            terrain.Shift(new[] { C(2, 0) }, 1);
            RoadNetwork network = Built(C(0, 0), C(1, 0), C(2, 0));

            // A branch off the ramp cell (1, 0) would make it a junction.
            Assert.IsTrue(Breaks(terrain, network, C(1, 0), C(1, 1)));

            // A branch off the flat cell before it is fine.
            Assert.IsFalse(Breaks(terrain, network, C(0, 0), C(0, 1)));
        }

        static bool HasTopAt(RoadMeshBuffers buffers, float x, float y)
        {
            foreach (UnityEngine.Vector3 v in buffers.Vertices)
            {
                if (UnityEngine.Mathf.Abs(v.x - x) < 1e-3f && UnityEngine.Mathf.Abs(v.y - y) < 1e-3f) return true;
            }
            return false;
        }

        [Test]
        public void ARampRisesAcrossItsCell()
        {
            var grid = new UnityEngine.GameObject("grid").AddComponent<CityGrid>();
            try
            {
                float low = RoadTerrain.SurfaceHeight(grid, 2, Dir8.E, 0f, 0.5f);
                float high = RoadTerrain.SurfaceHeight(grid, 2, Dir8.E, 1f, 0.5f);
                Assert.AreEqual(grid.HeightOf(2), low, 1e-5f);
                Assert.AreEqual(grid.HeightOf(3), high, 1e-5f);
                Assert.AreEqual(grid.HeightOf(2), RoadTerrain.SurfaceHeight(grid, 2, Dir8.None, 1f, 1f), 1e-5f);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(grid.gameObject);
            }
        }

        [Test]
        public void RoadsOnARampAreLiftedOntoTheSlope()
        {
            var grid = new UnityEngine.GameObject("grid").AddComponent<CityGrid>();
            try
            {
                grid.Terrain.Shift(new[] { C(2, 0), C(3, 0) }, 1);
                RoadNetwork network = Built(C(0, 0), C(1, 0), C(2, 0), C(3, 0));

                var buffers = new RoadMeshBuffers();
                RoadGeometry.Build(buffers, network, new List<GridCoord>(network.Cells), grid.Profile, null);
                RoadTerrain.Lift(buffers, grid, network);

                // The ramp is cell (1, 0): its road surface is on level 0 at x = 1 and on level 1 at
                // x = 2, where it meets the cell above, and every vertex between rides the slope.
                float roadTop = grid.Profile.Height;
                Assert.IsTrue(HasTopAt(buffers, 1f, roadTop), "road does not start the ramp at ground level");
                Assert.IsTrue(HasTopAt(buffers, 2f, grid.HeightOf(1) + roadTop), "road does not reach the upper level");
                foreach (UnityEngine.Vector3 v in buffers.Vertices)
                {
                    if (v.x <= 1f + 1e-3f || v.x >= 2f - 1e-3f) continue;
                    float above = v.y - (v.x - 1f) * grid.HeightOf(1);
                    Assert.IsTrue(UnityEngine.Mathf.Abs(above) < 1e-3f || UnityEngine.Mathf.Abs(above - roadTop) < 1e-3f,
                                  $"vertex {v} is off the slope");
                }

                // And every vertex sits within the ramp's rise, never below the ground or above the top level.
                foreach (UnityEngine.Vector3 v in buffers.Vertices)
                {
                    Assert.GreaterOrEqual(v.y, -1e-3f);
                    Assert.LessOrEqual(v.y, grid.HeightOf(1) + roadTop + 1e-3f);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(grid.gameObject);
            }
        }

        [Test]
        public void BuildingsNeedFlatGround()
        {
            var errors = new List<string>();
            ModelDefinition wide = ModelDefinitionParser.Parse(
                @"{ ""type"": ""structure"", ""id"": ""w"", ""footprint"": { ""width"": 2 } }", "test.json", errors);
            Assert.IsEmpty(errors);

            var terrain = new TerrainMap();
            terrain.Shift(new[] { C(1, 0) }, 1);

            PlacementProblem split = BuildingPlacement.Check(wide, C(0, 0), 0, _ => true, new RoadNetwork(),
                                                             new PlacedBuilding[0], terrain.LevelAt);
            Assert.IsTrue((split & PlacementProblem.Uneven) != 0);

            terrain.Shift(new[] { C(0, 0) }, 1);
            PlacementProblem flat = BuildingPlacement.Check(wide, C(0, 0), 0, _ => true, new RoadNetwork(),
                                                            new PlacedBuilding[0], terrain.LevelAt);
            Assert.IsTrue((flat & PlacementProblem.Uneven) == 0);
        }

        [Test]
        public void LevelsSurviveASave()
        {
            var terrain = new TerrainMap();
            terrain.Shift(new[] { C(3, -2), C(4, -2) }, 1);
            terrain.Shift(new[] { C(4, -2) }, 1);

            SaveData data = CitySave.Capture(new RoadNetwork(), new PlacedBuilding[0], null, terrain);
            SaveData loaded = CitySave.FromJson(CitySave.ToJson(data), out string error);
            Assert.IsNull(error);

            var restored = new TerrainMap();
            CitySave.RestoreTerrain(loaded, restored);
            Assert.AreEqual(1, restored.LevelAt(C(3, -2)));
            Assert.AreEqual(2, restored.LevelAt(C(4, -2)));
            Assert.AreEqual(2, restored.Count);
        }

        [Test]
        public void OlderSavesLoadFlat()
        {
            SaveData loaded = CitySave.FromJson(@"{ ""version"": 1, ""roadCells"": [] }", out string error);
            Assert.IsNull(error);

            var restored = new TerrainMap();
            CitySave.RestoreTerrain(loaded, restored);
            Assert.AreEqual(0, restored.Count);
        }
    }
}
