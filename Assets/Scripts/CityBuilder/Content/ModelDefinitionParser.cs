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
            if (definition.Footprint == null || definition.Footprint.Width < 1 || definition.Footprint.Depth < 1)
                errors.Add($"{source}: \"footprint\" width and depth must be at least 1.");
            if (definition.Cost < 0) errors.Add($"{source}: \"cost\" cannot be negative.");

            definition.Behaviors ??= new List<BehaviorDefinition>();
            for (int i = 0; i < definition.Behaviors.Count; i++)
            {
                if (definition.Behaviors[i] == null) errors.Add($"{source}: behaviors[{i}] is empty.");
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
