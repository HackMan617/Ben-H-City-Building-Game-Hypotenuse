using System.Collections.Generic;
using System.IO;
using CityBuilder.Buildings;
using CityBuilder.Content;
using CityBuilder.Grid;
using CityBuilder.Roads;
using CityBuilder.Saves;
using NUnit.Framework;

namespace CityBuilder.Tests
{
    /// <summary>
    /// Saving a city and loading it back must give the same roads, links and buildings; the files must
    /// land in the folder asked for and survive odd names.
    /// </summary>
    public class SaveTests
    {
        string _folder;

        static GridCoord C(int x, int z) => new GridCoord(x, z);

        [SetUp]
        public void CreateFolder()
        {
            _folder = Path.Combine(Path.GetTempPath(), "CityBuilderSaveTests_" + System.Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void DeleteFolder()
        {
            if (Directory.Exists(_folder)) Directory.Delete(_folder, true);
        }

        static RoadNetwork SampleRoads()
        {
            var network = new RoadNetwork();
            var edit = new RoadEdit();
            RoadStroke.Plan(network, new[] { C(-1, 0), C(0, 0), C(1, 0) }, false, edit);
            edit.Apply(network);

            edit = new RoadEdit();
            RoadStroke.Plan(network, new[] { C(0, 0), C(1, -1), C(2, -2) }, false, edit);
            edit.Apply(network);

            network.Add(C(5, 5));   // a lone cell, linked to nothing
            return network;
        }

        static SaveData RoundTrip(SaveData data, out string error) => CitySave.FromJson(CitySave.ToJson(data), out error);

        [Test]
        public void Roads_ComeBackWithTheSameCellsAndLinks()
        {
            RoadNetwork original = SampleRoads();
            SaveData loaded = RoundTrip(CitySave.Capture(original, new PlacedBuilding[0], "basicroad"), out string error);
            Assert.IsNull(error);

            var restored = new RoadNetwork();
            var problems = new List<string>();
            CitySave.RestoreRoads(loaded, restored, problems);

            Assert.IsEmpty(problems);
            CollectionAssert.AreEquivalent(original.Cells, restored.Cells);
            foreach (GridCoord cell in original.Cells)
            {
                Assert.AreEqual(original.MaskAt(cell), restored.MaskAt(cell), $"links at {cell}");
            }
            Assert.AreEqual("basicroad", loaded.RoadType);
        }

        [Test]
        public void EachLink_IsWrittenOnce()
        {
            SaveData data = CitySave.Capture(SampleRoads(), new PlacedBuilding[0], null);

            // Three cells east-west and three on the diagonal, sharing the middle one: four links.
            Assert.AreEqual(4, data.RoadLinks.Count);
        }

        [Test]
        public void Buildings_ComeBackOnTheirCellsAndTurned()
        {
            List<ModelDefinition> models = BuildingPalette.Create();
            ModelDefinition factory = models.Find(m => m.Id == "factory");
            var placed = new[] { new PlacedBuilding(factory, C(4, -3), 3) };

            SaveData loaded = RoundTrip(CitySave.Capture(new RoadNetwork(), placed, null), out _);
            var problems = new List<string>();
            List<PlacedBuilding> restored = CitySave.RestoreBuildings(loaded, BuildingPalette.Create(), problems);

            Assert.IsEmpty(problems);
            Assert.AreEqual(1, restored.Count);
            Assert.AreEqual("factory", restored[0].Definition.Id);
            Assert.AreEqual(C(4, -3), restored[0].Anchor);
            Assert.AreEqual(3, restored[0].Rotation);
            CollectionAssert.AreEquivalent(placed[0].Cells, restored[0].Cells);
        }

        [Test]
        public void UnknownBuilding_IsDroppedAndReported()
        {
            var data = new SaveData();
            data.Buildings.Add(new SavedBuilding { Id = "castle", X = 1, Z = 2 });

            var problems = new List<string>();
            List<PlacedBuilding> restored = CitySave.RestoreBuildings(data, BuildingPalette.Create(), problems);

            Assert.IsEmpty(restored);
            Assert.AreEqual(1, problems.Count);
        }

        [Test]
        public void SaveFromANewerVersion_IsRefused()
        {
            SaveData data = CitySave.FromJson("{ \"version\": 999 }", out string error);
            Assert.IsNull(data);
            Assert.IsNotNull(error);
        }

        [Test]
        public void BrokenFile_IsRefusedNotThrown()
        {
            Assert.IsNull(CitySave.FromJson("{ not json", out string error));
            Assert.IsNotNull(error);
        }

        [Test]
        public void Write_PutsAFileInTheFolderThatReadsBack()
        {
            SaveData data = CitySave.Capture(SampleRoads(), new PlacedBuilding[0], null);
            string path = SaveFiles.Write(data, "My City", _folder);

            Assert.AreEqual(Path.Combine(_folder, "My City.json"), path);
            Assert.IsTrue(File.Exists(path));

            SaveData read = SaveFiles.Read("My City", out string error, _folder);
            Assert.IsNull(error);
            Assert.AreEqual(data.RoadCells.Count, read.RoadCells.Count);
        }

        [Test]
        public void Write_ReplacesASaveOfTheSameName()
        {
            SaveFiles.Write(CitySave.Capture(SampleRoads(), new PlacedBuilding[0], null), "city", _folder);
            SaveFiles.Write(CitySave.Capture(new RoadNetwork(), new PlacedBuilding[0], null), "city", _folder);

            Assert.AreEqual(1, SaveFiles.List(_folder).Count);
            Assert.AreEqual(0, SaveFiles.Read("city", out _, _folder).RoadCells.Count);
            Assert.IsEmpty(Directory.GetFiles(_folder, "*.tmp"));
        }

        [Test]
        public void List_IsEmptyWithNoFolder()
        {
            Assert.IsEmpty(SaveFiles.List(_folder));
        }

        [Test]
        public void ReadingAMissingSave_ReportsIt()
        {
            Assert.IsNull(SaveFiles.Read("nothing", out string error, _folder));
            Assert.IsNotNull(error);
        }

        [Test]
        public void CleanName_KeepsFilesInsideTheFolder()
        {
            Assert.AreEqual("save", SaveFiles.CleanName("   "));
            Assert.AreEqual("save", SaveFiles.CleanName(null));
            Assert.IsFalse(SaveFiles.CleanName("../../evil").Contains("/"));
            Assert.IsFalse(SaveFiles.CleanName("a\\b:c").Contains("\\"));
        }
    }
}
