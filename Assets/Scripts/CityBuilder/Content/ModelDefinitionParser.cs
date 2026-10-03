using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace CityBuilder.Content
{
    /// <summary>
    /// Reads and checks <see cref="ModelDefinition"/> JSON. The same parser runs when bundles are
    /// built, so a broken file fails the build instead of the game.
    /// </summary>
    public static class ModelDefinitionParser
    {
        static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            ContractResolver = new DefaultContractResolver { NamingStrategy = new CamelCaseNamingStrategy() },
            Converters = { new BehaviorConverter() },

            // Fields this build does not know are skipped, so newer content still loads.
            MissingMemberHandling = MissingMemberHandling.Ignore,
        };

        /// <summary>
        /// Parses <paramref name="json"/>. Returns null and fills <paramref name="errors"/> when the file
        /// is malformed or its values make no sense; <paramref name="source"/> names it in messages.
        /// </summary>
        public static ModelDefinition Parse(string json, string source, List<string> errors)
        {
            ModelDefinition definition;
            try
            {
                definition = JsonConvert.DeserializeObject<ModelDefinition>(json, Settings);
            }
            catch (JsonException e)
            {
                errors.Add($"{source}: {e.Message}");
                return null;
            }

            if (definition == null)
            {
                errors.Add($"{source}: file is empty.");
                return null;
            }

            int before = errors.Count;
            Validate(definition, source, errors);
            return errors.Count == before ? definition : null;
        }

        static void Validate(ModelDefinition definition, string source, List<string> errors)
        {
            if (string.IsNullOrWhiteSpace(definition.Id)) errors.Add($"{source}: \"id\" is required.");
            if (definition.Cost < 0) errors.Add($"{source}: \"cost\" cannot be negative.");

            definition.Tags ??= new List<string>();
            if (definition.Tags.Exists(string.IsNullOrWhiteSpace)) errors.Add($"{source}: \"tags\" cannot hold empty entries.");

            definition.Footprint ??= new Footprint();
            ValidateFootprint(definition.Footprint, source, errors);

            definition.Placement ??= new PlacementRules();
            ValidatePlacement(definition.Placement, source, errors);

            definition.Behaviors ??= new List<BehaviorDefinition>();
            for (int i = 0; i < definition.Behaviors.Count; i++)
            {
                if (definition.Behaviors[i] == null) errors.Add($"{source}: behaviors[{i}] is empty.");
            }
        }

        static void ValidateFootprint(Footprint footprint, string source, List<string> errors)
        {
            if (footprint.Width < 1 || footprint.Depth < 1)
            {
                errors.Add($"{source}: \"footprint\" width and depth must be at least 1.");
                return;
            }
            if (footprint.Height < 1) errors.Add($"{source}: \"footprint\" height must be at least 1.");
            if (!Enum.IsDefined(typeof(Facing), footprint.Front))
                errors.Add($"{source}: \"footprint\" front must be north, east, south or west.");

            if (footprint.Cells != null)
            {
                if (footprint.Cells.Count != footprint.Depth)
                {
                    errors.Add($"{source}: \"footprint\" cells has {footprint.Cells.Count} rows but depth is {footprint.Depth}.");
                    return;
                }
                bool anyCovered = false;
                for (int row = 0; row < footprint.Cells.Count; row++)
                {
                    string cells = footprint.Cells[row] ?? string.Empty;
                    if (cells.Length != footprint.Width || !IsCellRow(cells))
                    {
                        errors.Add($"{source}: \"footprint\" cells[{row}] must be {footprint.Width} characters of 'X' or '.'.");
                        return;
                    }
                    anyCovered |= cells.IndexOf('X') >= 0 || cells.IndexOf('x') >= 0;
                }
                if (!anyCovered) errors.Add($"{source}: \"footprint\" cells must cover at least one cell.");
            }

            if (!footprint.IsCovered(footprint.Anchor.X, footprint.Anchor.Z))
                errors.Add($"{source}: \"footprint\" anchor {footprint.Anchor} must be a covered cell.");
        }

        static bool IsCellRow(string cells)
        {
            foreach (char c in cells)
            {
                if (c != 'X' && c != 'x' && c != '.') return false;
            }
            return true;
        }

        static void ValidatePlacement(PlacementRules placement, string source, List<string> errors)
        {
            if (placement.RoadAccess != null)
            {
                if (!Enum.IsDefined(typeof(RoadSide), placement.RoadAccess.Side))
                    errors.Add($"{source}: \"roadAccess\" side must be any or front.");
                if (placement.RoadAccess.Distance < 1)
                    errors.Add($"{source}: \"roadAccess\" distance must be at least 1.");
            }
            if (placement.Clearance < 0) errors.Add($"{source}: \"clearance\" cannot be negative.");
            if (placement.MaxCount < 0) errors.Add($"{source}: \"maxCount\" cannot be negative.");

            placement.KeepAwayFrom ??= new List<KeepAwayRule>();
            for (int i = 0; i < placement.KeepAwayFrom.Count; i++)
            {
                KeepAwayRule rule = placement.KeepAwayFrom[i];
                if (rule == null)
                {
                    errors.Add($"{source}: keepAwayFrom[{i}] is empty.");
                    continue;
                }
                if (string.IsNullOrWhiteSpace(rule.Id) == string.IsNullOrWhiteSpace(rule.Tag))
                    errors.Add($"{source}: keepAwayFrom[{i}] needs exactly one of \"id\" or \"tag\".");
                if (rule.Distance < 1) errors.Add($"{source}: keepAwayFrom[{i}] distance must be at least 1.");
            }
        }
    }

    /// <summary>
    /// Picks the <see cref="BehaviorDefinition"/> class from each entry's "type". Register a new
    /// behaviour by adding it to <see cref="Types"/>.
    /// </summary>
    public class BehaviorConverter : JsonConverter<BehaviorDefinition>
    {
        static readonly Dictionary<string, Type> Types = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
        {
            { "housing", typeof(HousingBehavior) },
            { "jobs", typeof(JobsBehavior) },
        };

        public override bool CanWrite => false;

        public override BehaviorDefinition ReadJson(JsonReader reader, Type objectType, BehaviorDefinition existingValue,
                                                    bool hasExistingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null) return null;

            JObject data = JObject.Load(reader);
            string type = (string)data["type"];
            if (string.IsNullOrWhiteSpace(type)) throw new JsonSerializationException("A behavior is missing its \"type\".");

            if (!Types.TryGetValue(type, out Type concrete)) return new UnknownBehavior { Type = type, Data = data };

            var behavior = (BehaviorDefinition)Activator.CreateInstance(concrete);
            serializer.Populate(data.CreateReader(), behavior);
            return behavior;
        }

        public override void WriteJson(JsonWriter writer, BehaviorDefinition value, JsonSerializer serializer) =>
            throw new NotSupportedException();
    }
}
