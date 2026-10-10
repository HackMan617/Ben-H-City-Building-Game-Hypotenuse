using System.Collections.Generic;
using UnityEngine;

namespace CityBuilder.Content
{
    /// <summary>
    /// Loads assets out of asset bundles, pulling in the bundles they depend on and unloading
    /// bundles once nothing uses them.
    ///
    /// Models arrive as prefabs: an .fbx under Assets/GameAssets is imported by the editor and packed
    /// into a bundle, so <c>LoadAssetAsync&lt;GameObject&gt;("houses", "Cottage")</c> returns the
    /// model ready to instantiate. The device never parses FBX itself. A model's behaviour and
    /// technical data ships beside it as Cottage.json -- see <see cref="LoadDefinitionAsync"/>.
    ///
    /// Every successful load holds its bundle open; call <see cref="Release"/> once per load when
    /// the asset and anything instantiated from it are gone. Releasing early unloads the meshes and
    /// textures out from under live objects.
    ///
    /// A bundle is <em>functional</em> when its prefab carries MonoBehaviour components:
    /// <see cref="InstantiateModelAsync"/> loads such a prefab, instantiates it, and runs those scripts
    /// through <see cref="BundleScriptReader"/>, handing each its definition. The scripts' code lives in
    /// the app, not the bundle -- see <see cref="IBundleScript"/>.
    ///
    /// Everything runs on the main thread. Loads are asynchronous so a large bundle does not stall a
    /// frame, and two loads from the same bundle share one open.
    /// </summary>
    public sealed class BundleLoader
    {
        class Entry
        {
            public AssetBundleCreateRequest Request;
            public AssetBundle Bundle;
            public int Refs;
        }

        readonly Dictionary<string, Entry> _bundles = new Dictionary<string, Entry>();
        readonly HashSet<string> _built = new HashSet<string>();
        AssetBundle _manifestBundle;
        AssetBundleManifest _manifest;

        public bool IsInitialized => _manifest != null;

        /// <summary>Bundles currently open, including ones still loading.</summary>
        public int OpenBundleCount => _bundles.Count;

        /// <summary>
        /// Reads the build manifest, which lists every bundle and what it depends on. Returns false
        /// when no bundles have been built for this platform.
        /// </summary>
        public bool Initialize()
        {
            if (IsInitialized) return true;

            GameAssetPaths.EnsureFolders();

            // The builder names the manifest bundle after its output folder, i.e. the platform.
            string path = GameAssetPaths.Resolve(GameAssetPaths.Platform);
            _manifestBundle = AssetBundle.LoadFromFile(path);
            if (_manifestBundle == null)
            {
                Debug.LogWarning($"No asset bundles for {GameAssetPaths.Platform}. " +
                                 "Build them with City Builder > Asset Bundles.");
                return false;
            }

            _manifest = _manifestBundle.LoadAsset<AssetBundleManifest>("AssetBundleManifest");
            if (_manifest != null)
            {
                _built.UnionWith(_manifest.GetAllAssetBundles());
                return true;
            }

            _manifestBundle.Unload(true);
            _manifestBundle = null;
            Debug.LogError($"Asset bundle manifest at {path} is unreadable.");
            return false;
        }

        /// <summary>True when a bundle of this name was built.</summary>
        public bool HasBundle(string bundleName) => _built.Contains(bundleName);

        /// <summary>
        /// Loads <paramref name="assetName"/> (its file name, without extension) from
        /// <paramref name="bundleName"/>. Returns null on failure, and holds nothing open then.
        /// </summary>
        public async Awaitable<T> LoadAssetAsync<T>(string bundleName, string assetName) where T : Object
        {
            if (!IsInitialized && !Initialize()) return null;

            AssetBundle bundle = await AcquireAsync(bundleName);
            if (bundle == null) return null;

            AssetBundleRequest request = bundle.LoadAssetAsync<T>(assetName);
            while (!request.isDone) await Awaitable.NextFrameAsync();

            if (request.asset is T asset) return asset;

            Debug.LogError($"Asset '{assetName}' of type {typeof(T).Name} is not in bundle '{bundleName}'.");
            Release(bundleName);
            return null;
        }

        /// <summary>
        /// Loads and parses the JSON definition that ships beside <paramref name="modelName"/>, e.g.
        /// Cottage.json for Cottage.fbx. Returns null if it is missing or invalid. Holds nothing open
        /// afterwards: the result is plain data, so there is nothing to release.
        /// </summary>
        public async Awaitable<ModelDefinition> LoadDefinitionAsync(string bundleName, string modelName)
        {
            string assetName = DefinitionAssetName(modelName);
            TextAsset file = await LoadAssetAsync<TextAsset>(bundleName, assetName);
            if (file == null) return null;

            string json = file.text;
            Release(bundleName);

            var errors = new List<string>();
            ModelDefinition definition = ModelDefinitionParser.Parse(json, $"{bundleName}/{assetName}", errors);
            foreach (string error in errors) Debug.LogError(error);
            return definition;
        }

        /// <summary>
        /// Loads a model's prefab and its definition from a functional bundle, instantiates the prefab,
        /// and runs the scripts riding on it: every <see cref="IBundleScript"/> component is handed the
        /// definition through <see cref="BundleScriptReader"/>. Returns the live instance, or null when
        /// the prefab is missing.
        ///
        /// The bundle stays open for as long as the instance lives -- its meshes and textures come from
        /// there -- so call <see cref="Release"/> with the same <paramref name="bundleName"/> once, after
        /// the instance is destroyed. A model with no JSON definition still loads; its scripts get a null
        /// <see cref="BundleScriptContext.Definition"/>. Missing script references and scripts that throw
        /// while initializing are logged but do not fail the load.
        /// </summary>
        public async Awaitable<GameObject> InstantiateModelAsync(string bundleName, string modelName, Transform parent = null)
        {
            // Holds one reference on the bundle, carried by the instance below until the caller releases.
            GameObject prefab = await LoadAssetAsync<GameObject>(bundleName, modelName);
            if (prefab == null) return null;

            // Acquires and releases on its own, so the prefab's hold is the only one left afterwards.
            ModelDefinition definition = await LoadDefinitionAsync(bundleName, modelName);

            GameObject instance = Object.Instantiate(prefab, parent);
            instance.name = prefab.name;

            var context = new BundleScriptContext(definition, bundleName, modelName);
            BundleScriptReader.Report report = BundleScriptReader.Initialize(instance, context);
            foreach (string error in report.Errors) Debug.LogError(error);

            return instance;
        }

        /// <summary>The name a model's JSON definition is loaded by: its file name, extension included.</summary>
        public static string DefinitionAssetName(string modelName) => modelName + DefinitionExtension;

        public const string DefinitionExtension = ".json";

        /// <summary>Gives back one load's hold on <paramref name="bundleName"/> and its dependencies.</summary>
        public void Release(string bundleName)
        {
            if (!IsInitialized) return;

            ReleaseOne(bundleName);
            foreach (string dependency in _manifest.GetAllDependencies(bundleName)) ReleaseOne(dependency);
        }

        /// <summary>
        /// Unloads every bundle and everything loaded from them, e.g. when leaving a city. Anything
        /// still using those assets loses its meshes and textures.
        /// </summary>
        public void UnloadAll()
        {
            foreach (Entry entry in _bundles.Values)
            {
                if (entry.Bundle != null) entry.Bundle.Unload(true);
            }
            _bundles.Clear();

            if (_manifestBundle != null) _manifestBundle.Unload(true);
            _manifestBundle = null;
            _manifest = null;
            _built.Clear();
        }

        /// <summary>Opens a bundle after its dependencies, so references between bundles resolve.</summary>
        async Awaitable<AssetBundle> AcquireAsync(string bundleName)
        {
            if (!HasBundle(bundleName))
            {
                Debug.LogError($"No asset bundle named '{bundleName}' was built for {GameAssetPaths.Platform}.");
                return null;
            }

            // GetAllDependencies is already transitive, so each is opened once, directly.
            string[] dependencies = _manifest.GetAllDependencies(bundleName);
            var acquired = new List<string>(dependencies.Length + 1);

            foreach (string name in dependencies)
            {
                if (await AcquireOneAsync(name) == null)
                {
                    foreach (string taken in acquired) ReleaseOne(taken);
                    return null;
                }
                acquired.Add(name);
            }

            AssetBundle bundle = await AcquireOneAsync(bundleName);
            if (bundle == null)
            {
                foreach (string taken in acquired) ReleaseOne(taken);
            }
            return bundle;
        }

        async Awaitable<AssetBundle> AcquireOneAsync(string bundleName)
        {
            if (!_bundles.TryGetValue(bundleName, out Entry entry))
            {
                // LZ4-compressed bundles load from file without being decompressed into memory whole.
                entry = new Entry { Request = AssetBundle.LoadFromFileAsync(GameAssetPaths.Resolve(bundleName)) };
                _bundles.Add(bundleName, entry);
            }

            // Counted before waiting, so a release racing this load cannot unload it underneath.
            entry.Refs++;

            if (entry.Bundle == null)
            {
                while (!entry.Request.isDone) await Awaitable.NextFrameAsync();
                entry.Bundle = entry.Request.assetBundle;
            }

            if (entry.Bundle != null) return entry.Bundle;

            Debug.LogError($"Asset bundle '{bundleName}' failed to open.");
            ReleaseOne(bundleName);
            return null;
        }

        static void UnloadLate(AssetBundleCreateRequest request)
        {
            AssetBundle bundle = request.assetBundle;
            if (bundle != null) bundle.Unload(true);
        }

        void ReleaseOne(string bundleName)
        {
            if (!_bundles.TryGetValue(bundleName, out Entry entry)) return;
            if (--entry.Refs > 0) return;

            _bundles.Remove(bundleName);

            // Still loading: unload as soon as it lands, since nobody is waiting for it any more.
            if (entry.Bundle != null) entry.Bundle.Unload(true);
            else if (!entry.Request.isDone) entry.Request.completed += _ => UnloadLate(entry.Request);
        }
    }
}
