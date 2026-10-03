using System.Collections.Generic;
using System.IO;
using CityBuilder.Content;
using CityBuilder.EditorTools;
using CityBuilder.Grid;
using NUnit.Framework;
using UnityEditor;

namespace CityBuilder.Tests
{
    /// <summary>
    /// The JSON that ships beside each model, and how it is packed next to the model.
    /// </summary>
    public class ModelDefinitionTests
    {
        static ModelDefinition Parse(string json, List<string> errors) =>
            ModelDefinitionParser.Parse(json, "test.json", errors);

        [Test]
        public void ReadsTheStarterLayout()
        {
            var errors = new List<string>();
            ModelDefinition definition = Parse(@"{
                ""schemaVersion"": 1,
                ""id"": ""cottage"",
                ""displayName"": ""Cottage"",
                ""footprint"": { ""width"": 2, ""depth"": 3 },
                ""cost"": 100,
                ""behaviors"": [
                    { ""type"": ""housing"", ""residents"": 4 },
                    { ""type"": ""jobs"", ""workers"": 2 }
                ]
            }", errors);

            Assert.IsEmpty(errors);
            Assert.AreEqual("cottage", definition.Id);
            Assert.AreEqual("Cottage", definition.DisplayName);
            Assert.AreEqual(2, definition.Footprint.Width);
            Assert.AreEqual(3, definition.Footprint.Depth);
            Assert.AreEqual(100, definition.Cost);
            Assert.AreEqual(4, definition.GetBehavior<HousingBehavior>().Residents);
            Assert.AreEqual(2, definition.GetBehavior<JobsBehavior>().Workers);
        }

        [Test]
        public void ReadsFootprintShapeAndPlacementRules()
        {
            var errors = new List<string>();
            ModelDefinition definition = Parse(@"{
                ""id"": ""workshop"",
                ""tags"": [ ""industrial"", ""small"" ],
                ""footprint"": {
                    ""width"": 3, ""depth"": 2, ""height"": 2,
                    ""cells"": [ ""XX."",
                                 ""XXX"" ],
                    ""anchor"": { ""x"": 1, ""z"": 0 },
                    ""front"": ""East""
                },
                ""placement"": {
                    ""roadAccess"": { ""side"": ""front"", ""distance"": 2 },
                    ""clearance"": 1,
                    ""keepAwayFrom"": [ { ""tag"": ""residential"", ""distance"": 5 }, { ""id"": ""school"", ""distance"": 3 } ],
                    ""maxCount"": 2
                }
            }", errors);

            Assert.IsEmpty(errors);
            Assert.IsTrue(definition.HasTag("INDUSTRIAL"));
            Footprint footprint = definition.Footprint;
            Assert.AreEqual(2, footprint.Height);
            Assert.AreEqual(new GridCoord(1, 0), footprint.Anchor);
            Assert.AreEqual(Facing.East, footprint.Front);
            Assert.IsTrue(footprint.IsCovered(2, 0), "south row is the last one listed");
            Assert.IsFalse(footprint.IsCovered(2, 1), "north row is the first one listed");

            PlacementRules placement = definition.Placement;
            Assert.AreEqual(RoadSide.Front, placement.RoadAccess.Side);
            Assert.AreEqual(2, placement.RoadAccess.Distance);
            Assert.AreEqual(1, placement.Clearance);
            Assert.AreEqual(2, placement.KeepAwayFrom.Count);
            Assert.AreEqual("residential", placement.KeepAwayFrom[0].Tag);
            Assert.AreEqual("school", placement.KeepAwayFrom[1].Id);
            Assert.AreEqual(2, placement.MaxCount);
        }

        [Test]
        public void MissingOptionalFieldsTakeDefaults()
        {
            var errors = new List<string>();
            ModelDefinition definition = Parse(@"{ ""id"": ""tree"" }", errors);

            Assert.IsEmpty(errors);
            Assert.AreEqual(1, definition.Footprint.Width);
            Assert.AreEqual(1, definition.Footprint.Depth);
            Assert.AreEqual(1, definition.Footprint.Height);
            Assert.AreEqual(Facing.South, definition.Footprint.Front);
            Assert.IsTrue(definition.Footprint.IsCovered(0, 0));
            Assert.AreEqual(0, definition.Cost);
            Assert.IsEmpty(definition.Tags);
            Assert.IsNull(definition.Placement.RoadAccess);
            Assert.AreEqual(0, definition.Placement.Clearance);
            Assert.IsEmpty(definition.Placement.KeepAwayFrom);
            Assert.AreEqual(0, definition.Placement.MaxCount);
            Assert.IsEmpty(definition.Behaviors);
        }

        [Test]
        public void UnknownBehaviorsAndFieldsAreKeptNotRejected()
        {
            var errors = new List<string>();
            ModelDefinition definition = Parse(@"{
                ""id"": ""tower"",
                ""someFutureField"": true,
                ""behaviors"": [ { ""type"": ""broadcast"", ""range"": 12 } ]
            }", errors);

            Assert.IsEmpty(errors);
            var unknown = definition.GetBehavior<UnknownBehavior>();
            Assert.AreEqual("broadcast", unknown.Type);
            Assert.AreEqual(12, (int)unknown.Data["range"]);
        }

        [Test]
        public void BrokenJsonIsAnError()
        {
            var errors = new List<string>();
            Assert.IsNull(Parse(@"{ ""id"": ""cottage"", ", errors));
            Assert.AreEqual(1, errors.Count);
            StringAssert.StartsWith("test.json", errors[0]);
        }

        [TestCase(@"{ ""cost"": 5 }", "\"id\"")]
        [TestCase(@"{ ""id"": ""a"", ""footprint"": { ""width"": 0, ""depth"": 1 } }", "footprint")]
        [TestCase(@"{ ""id"": ""a"", ""cost"": -1 }", "cost")]
        [TestCase(@"{ ""id"": ""a"", ""behaviors"": [ { ""residents"": 4 } ] }", "type")]
        [TestCase(@"{ ""id"": ""a"", ""footprint"": { ""height"": 0 } }", "height")]
        [TestCase(@"{ ""id"": ""a"", ""footprint"": { ""front"": ""up"" } }", "front")]
        [TestCase(@"{ ""id"": ""a"", ""footprint"": { ""front"": 7 } }", "front")]
        [TestCase(@"{ ""id"": ""a"", ""footprint"": { ""width"": 2, ""cells"": [ ""XX"", ""XX"" ] } }", "rows")]
        [TestCase(@"{ ""id"": ""a"", ""footprint"": { ""width"": 2, ""cells"": [ ""X"" ] } }", "cells[0]")]
        [TestCase(@"{ ""id"": ""a"", ""footprint"": { ""width"": 2, ""cells"": [ ""X#"" ] } }", "cells[0]")]
        [TestCase(@"{ ""id"": ""a"", ""footprint"": { ""width"": 2, ""cells"": [ "".."" ] } }", "at least one")]
        [TestCase(@"{ ""id"": ""a"", ""footprint"": { ""width"": 2, ""cells"": [ "".X"" ] } }", "anchor")]
        [TestCase(@"{ ""id"": ""a"", ""footprint"": { ""anchor"": { ""x"": 1, ""z"": 0 } } }", "anchor")]
        [TestCase(@"{ ""id"": ""a"", ""tags"": [ """" ] }", "tags")]
        [TestCase(@"{ ""id"": ""a"", ""placement"": { ""roadAccess"": { ""distance"": 0 } } }", "roadAccess")]
        [TestCase(@"{ ""id"": ""a"", ""placement"": { ""roadAccess"": { ""side"": ""back"" } } }", "side")]
        [TestCase(@"{ ""id"": ""a"", ""placement"": { ""clearance"": -1 } }", "clearance")]
        [TestCase(@"{ ""id"": ""a"", ""placement"": { ""maxCount"": -1 } }", "maxCount")]
        [TestCase(@"{ ""id"": ""a"", ""placement"": { ""keepAwayFrom"": [ { ""distance"": 2 } ] } }", "exactly one")]
        [TestCase(@"{ ""id"": ""a"", ""placement"": { ""keepAwayFrom"": [ { ""id"": ""b"", ""tag"": ""c"", ""distance"": 2 } ] } }", "exactly one")]
        [TestCase(@"{ ""id"": ""a"", ""placement"": { ""keepAwayFrom"": [ { ""tag"": ""c"" } ] } }", "distance")]
        public void NonsenseValuesAreErrors(string json, string mentions)
        {
            var errors = new List<string>();
            Assert.IsNull(Parse(json, errors));
            Assert.IsNotEmpty(errors);
            StringAssert.Contains(mentions, errors[0]);
        }

        [Test]
        public void TheExampleFileIsValid()
        {
            var errors = new List<string>();
            Parse(File.ReadAllText("Assets/GameAssets/ModelDefinition.example.json"), errors);
            Assert.IsEmpty(errors);
        }

        [Test]
        public void ADefinitionSitsBesideItsModelUnderItsOwnName()
        {
            var errors = new List<string>();
            List<AssetBundleBuild> builds = AssetBundleBuilder.Plan(new[]
            {
                "Assets/GameAssets/Houses/Cottage.fbx",
                "Assets/GameAssets/Houses/Cottage.json",
            }, errors);

            Assert.IsEmpty(errors);
            Assert.AreEqual("houses", builds[0].assetBundleName);
            CollectionAssert.AreEquivalent(new[] { "Cottage", "Cottage.json" }, builds[0].addressableNames);
            Assert.AreEqual("Cottage.json", BundleLoader.DefinitionAssetName("Cottage"));
        }
    }
}
