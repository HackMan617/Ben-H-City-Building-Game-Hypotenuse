using System;
using System.Collections.Generic;
using CityBuilder.Buildings;
using CityBuilder.Content;
using CityBuilder.Grid;
using CityBuilder.Roads;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;

namespace CityBuilder.Saves
{
    /// <summary>
    /// Turns the city into a <see cref="SaveData"/> and back, and a <see cref="SaveData"/> into JSON
    /// and back. Pure: it touches no files and no scene, so <see cref="SaveFiles"/> and
    /// <see cref="SaveGameSystem"/> do that.
    /// </summary>
    public static class CitySave
    {
        static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            ContractResolver = new DefaultContractResolver { NamingStrategy = new CamelCaseNamingStrategy() },
            Converters = { new StringEnumConverter() },
            Formatting = Formatting.Indented,

            // Fields this build does not know are skipped, so a save from a newer build still loads.
            MissingMemberHandling = MissingMemberHandling.Ignore,
        };

        public static SaveData Capture(RoadNetwork roads, IEnumerable<PlacedBuilding> buildings, string roadType)
        {
            var data = new SaveData
            {
                SavedAt = DateTime.UtcNow.ToString("o"),
                RoadType = roadType,
            };

            foreach (GridCoord cell in roads.Cells)
            {
                data.RoadCells.Add(cell);

                // Only the canonical half of each link, so it is written once.
                Dir8 mask = roads.MaskAt(cell);
                foreach (Dir8 dir in Dir8Util.All)
                {
                    if ((mask & dir) != 0 && Dir8Util.BitIndex(dir) < 4)
                    {
                        data.RoadLinks.Add(new SavedLink { X = cell.X, Z = cell.Z, Dir = dir });
                    }
                }
            }

            foreach (PlacedBuilding building in buildings)
            {
                data.Buildings.Add(new SavedBuilding
                {
                    Id = building.Definition.Id,
                    X = building.Anchor.X,
                    Z = building.Anchor.Z,
                    Rotation = building.Rotation,
                });
            }

            return data;
        }

        /// <summary>
        /// Replaces the contents of <paramref name="roads"/> with the saved road. A link whose cells are
        /// missing is dropped and reported in <paramref name="problems"/>.
        /// </summary>
        public static void RestoreRoads(SaveData data, RoadNetwork roads, List<string> problems)
        {
            roads.Clear();
            foreach (GridCoord cell in data.RoadCells) roads.Add(cell);

            foreach (SavedLink saved in data.RoadLinks)
            {
                var link = new RoadLink(new GridCoord(saved.X, saved.Z), saved.Dir);
                if (!roads.Has(link.Cell) || !roads.Has(link.Other))
                {
                    problems.Add($"Road link {link} joins a cell that is not road; dropped.");
                    continue;
                }
                roads.Link(link);
            }
        }

        /// <summary>
        /// The saved buildings, matched to their models in <paramref name="models"/> by id. One whose
        /// model is not there is dropped and reported in <paramref name="problems"/>.
        /// </summary>
        public static List<PlacedBuilding> RestoreBuildings(SaveData data, IReadOnlyList<ModelDefinition> models,
                                                            List<string> problems)
        {
            var buildings = new List<PlacedBuilding>();
            foreach (SavedBuilding saved in data.Buildings)
            {
                ModelDefinition model = null;
                foreach (ModelDefinition candidate in models)
                {
                    if (string.Equals(candidate.Id, saved.Id, StringComparison.OrdinalIgnoreCase)) model = candidate;
                }

                if (model == null)
                {
                    problems.Add($"Building '{saved.Id}' at ({saved.X}, {saved.Z}) has no model in this build; dropped.");
                    continue;
                }
                buildings.Add(new PlacedBuilding(model, new GridCoord(saved.X, saved.Z), saved.Rotation));
            }
            return buildings;
        }

        public static string ToJson(SaveData data) => JsonConvert.SerializeObject(data, Settings);

        /// <summary>Parses a save file's text. Returns null, with the reason in <paramref name="error"/>, when it cannot.</summary>
        public static SaveData FromJson(string json, out string error)
        {
            error = null;
            SaveData data;
            try
            {
                data = JsonConvert.DeserializeObject<SaveData>(json, Settings);
            }
            catch (JsonException e)
            {
                error = e.Message;
                return null;
            }

            if (data == null)
            {
                error = "file is empty.";
                return null;
            }
            if (data.Version > SaveData.CurrentVersion)
            {
                error = $"saved by a newer version of the game (save version {data.Version}).";
                return null;
            }

            data.RoadCells ??= new List<GridCoord>();
            data.RoadLinks ??= new List<SavedLink>();
            data.Buildings ??= new List<SavedBuilding>();
            return data;
        }
    }
}
