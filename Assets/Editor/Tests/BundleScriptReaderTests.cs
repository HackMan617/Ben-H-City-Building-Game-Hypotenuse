using System.Collections.Generic;
using CityBuilder.Content;
using NUnit.Framework;
using UnityEngine;

namespace CityBuilder.Tests
{
    /// <summary>
    /// How <see cref="BundleScriptReader"/> reads the scripts off a model's prefab: which it lists, which
    /// it drives, and how it reports ones that throw. Missing-reference counting is exercised by the game
    /// against real bundles; here the scripts all resolve, since the test types live in this assembly.
    /// </summary>
    public class BundleScriptReaderTests
    {
        readonly List<GameObject> _spawned = new List<GameObject>();

        GameObject NewModel(string name = "Model")
        {
            var go = new GameObject(name);
            _spawned.Add(go);
            return go;
        }

        [TearDown]
        public void DestroySpawned()
        {
            foreach (GameObject go in _spawned)
            {
                if (go != null) Object.DestroyImmediate(go);
            }
            _spawned.Clear();
        }

        /// <summary>A bundle script that records the context it was handed.</summary>
        class RecordingScript : MonoBehaviour, IBundleScript
        {
            public BundleScriptContext Received;
            public int InitializeCount;

            public void Initialize(BundleScriptContext context)
            {
                Received = context;
                InitializeCount++;
            }
        }

        class ThrowingScript : MonoBehaviour, IBundleScript
        {
            public void Initialize(BundleScriptContext context) => throw new System.InvalidOperationException("boom");
        }

        /// <summary>A plain MonoBehaviour that is not part of the content pipeline.</summary>
        class PlainScript : MonoBehaviour { }

        static BundleScriptContext Context(ModelDefinition definition = null) =>
            new BundleScriptContext(definition, "houses", "Cottage");

        [Test]
        public void ReadFindsBundleScriptsAndListsEveryMonoBehaviour()
        {
            GameObject model = NewModel();
            model.AddComponent<RecordingScript>();
            model.AddComponent<PlainScript>();

            BundleScriptReader.Report report = BundleScriptReader.Read(model);

            Assert.IsTrue(report.IsFunctional);
            Assert.AreEqual(1, report.BundleScripts.Count);
            CollectionAssert.Contains(report.Scripts, nameof(RecordingScript));
            CollectionAssert.Contains(report.Scripts, nameof(PlainScript));
            Assert.IsFalse(report.HasMissingScripts);
            Assert.IsEmpty(report.Errors);
        }

        [Test]
        public void ReadReachesScriptsOnChildren()
        {
            GameObject model = NewModel();
            var child = new GameObject("Chimney");
            child.transform.SetParent(model.transform);
            child.AddComponent<RecordingScript>();

            BundleScriptReader.Report report = BundleScriptReader.Read(model);

            Assert.AreEqual(1, report.BundleScripts.Count);
        }

        [Test]
        public void AModelWithoutBundleScriptsIsNotFunctional()
        {
            GameObject model = NewModel();
            model.AddComponent<PlainScript>();

            BundleScriptReader.Report report = BundleScriptReader.Read(model);

            Assert.IsFalse(report.IsFunctional);
            Assert.IsEmpty(report.BundleScripts);
            Assert.IsFalse(report.HasMissingScripts);
        }

        [Test]
        public void InitializeHandsEachBundleScriptTheContextOnce()
        {
            GameObject model = NewModel();
            RecordingScript script = model.AddComponent<RecordingScript>();
            var definition = new ModelDefinition { Id = "cottage" };

            BundleScriptReader.Report report = BundleScriptReader.Initialize(model, Context(definition));

            Assert.AreEqual(1, script.InitializeCount);
            Assert.AreSame(definition, script.Received.Definition);
            Assert.AreEqual("houses", script.Received.BundleName);
            Assert.AreEqual("Cottage", script.Received.ModelName);
            Assert.IsEmpty(report.Errors);
        }

        [Test]
        public void AScriptThatThrowsIsReportedButDoesNotStopTheOthers()
        {
            GameObject model = NewModel();
            model.AddComponent<ThrowingScript>();
            RecordingScript survivor = model.AddComponent<RecordingScript>();

            BundleScriptReader.Report report = BundleScriptReader.Initialize(model, Context());

            Assert.AreEqual(1, report.Errors.Count);
            StringAssert.Contains(nameof(ThrowingScript), report.Errors[0]);
            Assert.AreEqual(1, survivor.InitializeCount);
        }
    }
}
