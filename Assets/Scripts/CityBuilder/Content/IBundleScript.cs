namespace CityBuilder.Content
{
    /// <summary>
    /// A script that ships inside a functional asset bundle. A model's prefab -- Cottage.fbx turned
    /// prefab, say -- can carry MonoBehaviour components that implement this interface; the bundle
    /// records which scripts are attached and their serialized values, and the game runs them when the
    /// model is placed, just like any other Unity script.
    ///
    /// The component's <em>code</em> is compiled into the app, in the same assembly as the rest of the
    /// game: a bundle cannot bring new C# to a phone, because iOS and Android compile ahead of time.
    /// What the bundle adds is the wiring -- which of the game's scripts a model uses, and with what
    /// values -- so new content behaves without a new app build.
    ///
    /// When <see cref="BundleScriptReader"/> loads the prefab it hands every bundle script its model's
    /// <see cref="ModelDefinition"/> through <see cref="Initialize"/>, once, before the object's own
    /// Start runs. Read what you need off the context there rather than in Awake, where the definition
    /// is not available yet.
    /// </summary>
    public interface IBundleScript
    {
        /// <summary>
        /// Called once, right after the model is instantiated, with the data that shipped beside it.
        /// Throwing here is caught and reported by the reader; it does not stop the rest of the model's
        /// scripts from initializing.
        /// </summary>
        void Initialize(BundleScriptContext context);
    }

    /// <summary>What a bundle script is told about the model it belongs to when it is initialized.</summary>
    public readonly struct BundleScriptContext
    {
        /// <summary>The model's JSON definition, or null when the model shipped without one.</summary>
        public ModelDefinition Definition { get; }

        /// <summary>The bundle the model was loaded from.</summary>
        public string BundleName { get; }

        /// <summary>The model's load name, i.e. its file name without extension.</summary>
        public string ModelName { get; }

        public BundleScriptContext(ModelDefinition definition, string bundleName, string modelName)
        {
            Definition = definition;
            BundleName = bundleName;
            ModelName = modelName;
        }
    }
}
