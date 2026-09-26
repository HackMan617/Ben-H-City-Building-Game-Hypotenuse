using System.Collections.Generic;
using System.IO;
using CityBuilder.Content;
using CityBuilder.EditorTools;
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
        public void MissingOptionalFieldsTakeDefaults()
        {
            var errors = new List<string>();
            ModelDefinition definition = Parse(@"{ ""id"": ""tree"" }", errors);

            Assert.IsEmpty(errors);
            Assert.AreEqual(1, definition.Footprint.Width);
            Assert.AreEqual(1, definition.Footprint.Depth);
            Assert.AreEqual(0, definition.Cost);
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
                "Assets/GameAssets/DefaultModels/Cottage.fbx",
                "Assets/GameAssets/DefaultModels/Cottage.json",
            }, errors);

            Assert.IsEmpty(errors);
            Assert.AreEqual("defaultmodels", builds[0].assetBundleName);
            CollectionAssert.AreEquivalent(new[] { "Cottage", "Cottage.json" }, builds[0].addressableNames);
            Assert.AreEqual("Cottage.json", BundleLoader.DefinitionAssetName("Cottage"));
        }
    }
}
