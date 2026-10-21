using System.Collections.Generic;
using CityBuilder.Buildings;
using CityBuilder.Content;
using CityBuilder.Grid;
using CityBuilder.Roads;
using NUnit.Framework;

namespace CityBuilder.Tests
{
    /// <summary>
    /// Footprint layout under rotation, and each placement rule a model definition can carry.
    /// </summary>
    public class BuildingPlacementTests
    {
        static GridCoord C(int x, int z) => new GridCoord(x, z);

        static ModelDefinition Model(string json)
        {
            var errors = new List<string>();
            ModelDefinition definition = ModelDefinitionParser.Parse(json, "test.json", errors);
            Assert.IsEmpty(errors);
            return definition;
        }

        static bool InBounds(GridCoord cell) => cell.X >= -20 && cell.X < 20 && cell.Z >= -20 && cell.Z < 20;

        static RoadNetwork Roads(params GridCoord[] cells)
        {
            var roads = new RoadNetwork();
            foreach (GridCoord cell in cells) roads.Add(cell);
            return roads;
        }

        static PlacementProblem Check(ModelDefinition model, GridCoord at, int rotation = 0, RoadNetwork roads = null,
                                      params PlacedBuilding[] buildings) =>
            BuildingPlacement.Check(model, at, rotation, InBounds, roads ?? new RoadNetwork(), buildings);

        // An L: covered at (0,0), (1,0) and (0,1); anchored on (0,0).
        static readonly ModelDefinition L = Model(@"{
            ""type"": ""structure"",
            ""id"": ""l"",
            ""footprint"": { ""width"": 2, ""depth"": 2, ""cells"": [ ""X."", ""XX"" ] }
        }");

        [Test]
        public void TheAnchorLandsOnTheTargetCell()
        {
            ModelDefinition wide = Model(@"{ ""type"": ""structure"", ""id"": ""w"", ""footprint"": { ""width"": 3, ""anchor"": { ""x"": 1, ""z"": 0 } } }");
            CollectionAssert.AreEquivalent(new[] { C(4, 5), C(5, 5), C(6, 5) },
                                           BuildingPlacement.CellsAt(wide.Footprint, C(5, 5), 0));
        }

        [Test]
        public void IrregularShapesCoverOnlyTheirCells()
        {
            CollectionAssert.AreEquivalent(new[] { C(0, 0), C(1, 0), C(0, 1) },
                                           BuildingPlacement.CellsAt(L.Footprint, C(0, 0), 0));
        }

        [Test]
        public void RotationTurnsClockwise()
        {
            // North arm swings to the east, the east arm to the south.
            CollectionAssert.AreEquivalent(new[] { C(0, 0), C(0, -1), C(1, 0) },
                                           BuildingPlacement.CellsAt(L.Footprint, C(0, 0), 1));
            CollectionAssert.AreEquivalent(BuildingPlacement.CellsAt(L.Footprint, C(0, 0), 0),
                                           BuildingPlacement.CellsAt(L.Footprint, C(0, 0), 4));
            CollectionAssert.AreEquivalent(BuildingPlacement.CellsAt(L.Footprint, C(0, 0), 3),
                                           BuildingPlacement.CellsAt(L.Footprint, C(0, 0), -1));

            Assert.AreEqual(Dir8.S, BuildingPlacement.FrontAt(L.Footprint, 0));
            Assert.AreEqual(Dir8.W, BuildingPlacement.FrontAt(L.Footprint, 1));
            Assert.AreEqual(Dir8.E, BuildingPlacement.FrontAt(L.Footprint, 3));
        }

        [Test]
        public void FreeGroundIsFine()
        {
            Assert.AreEqual(PlacementProblem.None, Check(L, C(0, 0)));
        }

        [Test]
        public void EveryCoveredCellMustBeInBoundsAndFree()
        {
            Assert.AreEqual(PlacementProblem.OutOfBounds, Check(L, C(19, 0)));
            Assert.AreEqual(PlacementProblem.OnRoad, Check(L, C(0, 0), roads: Roads(C(0, 1))));
            Assert.AreEqual(PlacementProblem.Overlaps, Check(L, C(0, 0), 0, null, new PlacedBuilding(L, C(1, 0), 0)));

            // The open corner of the L is not covered.
            Assert.AreEqual(PlacementProblem.None, Check(L, C(0, 0), roads: Roads(C(1, 1))));
        }

        [Test]
        public void RoadAccessOnAnySide()
        {
            ModelDefinition hut = Model(@"{ ""type"": ""structure"", ""id"": ""hut"", ""placement"": { ""roadAccess"": {} } }");

            Assert.AreEqual(PlacementProblem.NoRoadAccess, Check(hut, C(0, 0)));
            Assert.AreEqual(PlacementProblem.None, Check(hut, C(0, 0), roads: Roads(C(-1, 0))));
            Assert.AreEqual(PlacementProblem.NoRoadAccess, Check(hut, C(0, 0), roads: Roads(C(1, 1))), "diagonal is not access");
            Assert.AreEqual(PlacementProblem.NoRoadAccess, Check(hut, C(0, 0), roads: Roads(C(0, 2))), "out of reach");
        }

        [Test]
        public void RoadAccessOnTheFrontFollowsRotation()
        {
            ModelDefinition shop = Model(@"{ ""type"": ""structure"", ""id"": ""shop"", ""placement"": { ""roadAccess"": { ""side"": ""front"" } } }");
            RoadNetwork south = Roads(C(0, -1));

            Assert.AreEqual(PlacementProblem.None, Check(shop, C(0, 0), 0, south));
            Assert.AreEqual(PlacementProblem.NoRoadAccess, Check(shop, C(0, 0), 0, Roads(C(0, 1))));

            // A quarter turn clockwise points the front west.
            Assert.AreEqual(PlacementProblem.NoRoadAccess, Check(shop, C(0, 0), 1, south));
            Assert.AreEqual(PlacementProblem.None, Check(shop, C(0, 0), 1, Roads(C(-1, 0))));
        }

        [Test]
        public void RoadAccessReachesOverDistanceUnlessABuildingIsInTheWay()
        {
            ModelDefinition farm = Model(@"{ ""type"": ""structure"", ""id"": ""farm"", ""placement"": { ""roadAccess"": { ""distance"": 3 } } }");
            ModelDefinition hut = Model(@"{ ""type"": ""structure"", ""id"": ""hut"" }");
            RoadNetwork roads = Roads(C(3, 0));

            Assert.AreEqual(PlacementProblem.None, Check(farm, C(0, 0), 0, roads));
            Assert.AreEqual(PlacementProblem.NoRoadAccess, Check(farm, C(0, 0), 0, roads, new PlacedBuilding(hut, C(2, 0), 0)));
            Assert.AreEqual(PlacementProblem.NoRoadAccess, Check(farm, C(-1, 0), 0, roads), "four cells away");
        }

        [Test]
        public void ClearanceIsKeptFromEitherSide()
        {
            ModelDefinition hut = Model(@"{ ""type"": ""structure"", ""id"": ""hut"" }");
            ModelDefinition villa = Model(@"{ ""type"": ""structure"", ""id"": ""villa"", ""placement"": { ""clearance"": 2 } }");

            Assert.AreEqual(PlacementProblem.None, Check(hut, C(0, 0), 0, null, new PlacedBuilding(hut, C(1, 1), 0)));
            Assert.AreEqual(PlacementProblem.TooClose, Check(villa, C(0, 0), 0, null, new PlacedBuilding(hut, C(2, 2), 0)));
            Assert.AreEqual(PlacementProblem.TooClose, Check(hut, C(0, 0), 0, null, new PlacedBuilding(villa, C(-2, 1), 0)));
            Assert.AreEqual(PlacementProblem.None, Check(villa, C(0, 0), 0, null, new PlacedBuilding(hut, C(3, 0), 0)));

            // Roads may run through the clearance.
            Assert.AreEqual(PlacementProblem.None, Check(villa, C(0, 0), roads: Roads(C(1, 0))));
        }

        [Test]
        public void KeepAwayMatchesByTagOrIdFromEitherSide()
        {
            ModelDefinition house = Model(@"{
                ""type"": ""structure"",
                ""id"": ""house"", ""tags"": [ ""residential"" ],
                ""placement"": { ""keepAwayFrom"": [ { ""tag"": ""Industrial"", ""distance"": 3 } ] }
            }");
            ModelDefinition factory = Model(@"{ ""type"": ""structure"", ""id"": ""factory"", ""tags"": [ ""industrial"" ] }");
            ModelDefinition school = Model(@"{
                ""type"": ""structure"",
                ""id"": ""school"",
                ""placement"": { ""keepAwayFrom"": [ { ""id"": ""factory"", ""distance"": 1 } ] }
            }");

            Assert.AreEqual(PlacementProblem.KeptAway, Check(house, C(0, 0), 0, null, new PlacedBuilding(factory, C(3, 0), 0)));
            Assert.AreEqual(PlacementProblem.None, Check(house, C(0, 0), 0, null, new PlacedBuilding(factory, C(4, 0), 0)));
            Assert.AreEqual(PlacementProblem.KeptAway, Check(factory, C(0, 0), 0, null, new PlacedBuilding(house, C(0, 3), 0)));
            Assert.AreEqual(PlacementProblem.KeptAway, Check(school, C(0, 0), 0, null, new PlacedBuilding(factory, C(1, 1), 0)));
            Assert.AreEqual(PlacementProblem.None, Check(school, C(0, 0), 0, null, new PlacedBuilding(house, C(1, 1), 0)));
        }

        [Test]
        public void MaxCountLimitsHowManyACityHas()
        {
            ModelDefinition hall = Model(@"{ ""type"": ""structure"", ""id"": ""townhall"", ""placement"": { ""maxCount"": 1 } }");

            Assert.AreEqual(PlacementProblem.None, Check(hall, C(0, 0)));
            Assert.AreEqual(PlacementProblem.LimitReached, Check(hall, C(0, 0), 0, null, new PlacedBuilding(hall, C(10, 10), 0)));
        }

        [Test]
        public void SeveralProblemsAreReportedTogether()
        {
            ModelDefinition shop = Model(@"{ ""type"": ""structure"", ""id"": ""shop"", ""placement"": { ""roadAccess"": {} } }");
            Assert.AreEqual(PlacementProblem.OutOfBounds | PlacementProblem.NoRoadAccess, Check(shop, C(40, 0)));
        }
    }
}
