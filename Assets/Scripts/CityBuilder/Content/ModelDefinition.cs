using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace CityBuilder.Content
{
    /// <summary>
    /// The behaviour and technical data that ships beside a model: Cottage.json next to Cottage.fbx
    /// in the same bundle. Read with <see cref="ModelDefinitionParser"/>.
    ///
    ///   {
    ///     "schemaVersion": 1,
    ///     "id": "cottage",
    ///     "displayName": "Cottage",
    ///     "footprint": { "width": 1, "depth": 1 },
    ///     "cost": 100,
    ///     "behaviors": [
    ///       { "type": "housing", "residents": 4 }
    ///     ]
    ///   }
    ///
    /// A starter layout, meant to grow: add a field here, or a behaviour type in
    /// <see cref="BehaviorConverter"/>, and older files still load.
    /// </summary>
    public class ModelDefinition
    {
        /// <summary>Bumped when a change would make older files mean something different.</summary>
        public int SchemaVersion = 1;

        /// <summary>Stable identifier, unique across all bundles; what saves and other data refer to.</summary>
        public string Id;

        /// <summary>Name shown to the player.</summary>
        public string DisplayName;

        public Footprint Footprint = new Footprint();

        /// <summary>Price to place one.</summary>
        public int Cost;

        public List<BehaviorDefinition> Behaviors = new List<BehaviorDefinition>();

        /// <summary>The first behaviour of type <typeparamref name="T"/>, or null.</summary>
        public T GetBehavior<T>() where T : BehaviorDefinition
        {
            foreach (BehaviorDefinition behavior in Behaviors)
            {
                if (behavior is T match) return match;
            }
            return null;
        }
    }

    /// <summary>Grid cells the model covers.</summary>
    public class Footprint
    {
        public int Width = 1;
        public int Depth = 1;
    }

    /// <summary>
    /// One thing a model does, picked by its "type" field. Types this build does not know become
    /// <see cref="UnknownBehavior"/> rather than failing, so content made for a newer version still loads.
    /// </summary>
    public abstract class BehaviorDefinition
    {
        public string Type;
    }

    /// <summary>"housing": people live here.</summary>
    public class HousingBehavior : BehaviorDefinition
    {
        public int Residents;
    }

    /// <summary>"jobs": people work here.</summary>
    public class JobsBehavior : BehaviorDefinition
    {
        public int Workers;
    }

    /// <summary>A behaviour type this build does not recognise, kept as raw JSON.</summary>
    public class UnknownBehavior : BehaviorDefinition
    {
        public JObject Data;
    }
}
