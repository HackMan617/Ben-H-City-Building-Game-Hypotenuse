using System.Collections.Generic;
using System.IO;
using System.Linq;
using CityBuilder.Content;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace CityBuilder.EditorTools
{
    /// <summary>
    /// Packs Assets/GameAssets into asset bundles, one per platform.
    ///
    /// Each folder directly under Assets/GameAssets becomes one bundle, named after the folder in
    /// lower case; everything inside it, subfolders included, goes in. So
    /// Assets/GameAssets/Houses/Cottage.fbx is loaded as bundle "houses", asset "Cottage". A model's
    /// JSON definition keeps its extension -- Cottage.json loads as "Cottage.json" -- so the pair can
    /// share a name, and every definition is parsed before building so a broken one stops the build.
    /// Every bundle needs an asset.json at the top of its folder, saying with "type" what it holds;
    /// the game reads it to tell road types from buildings. Files sitting directly in
    /// Assets/GameAssets belong to no bundle.
    ///
    /// Bundles are written to &lt;project&gt;/AssetBundles/&lt;Platform&gt; and LZ4-compressed, which
    /// lets a phone read them straight from storage instead of unpacking them into memory. Making a
    /// player build copies that platform's bundles -- and only that platform's -- into
    /// StreamingAssets for the build, then removes them again.
    /// </summary>
    public class AssetBundleBuilder : IPreprocessBuildWithReport, IPostprocessBuildWithReport
    {
        const string MenuRoot = "City Builder/Asset Bundles/";
        const string StreamingBundlesFolder = "Assets/StreamingAssets/" + GameAssetPaths.BundlesFolderName;

        static readonly string[] NotBundleable = { ".cs", ".asmdef", ".asmref", ".dll" };

        public int callbackOrder => 0;

        [MenuItem(MenuRoot + "Build for Editor")]
        static void BuildForEditor() => Build(EditorTarget());

        [MenuItem(MenuRoot + "Build for Android")]
        static void BuildForAndroid() => Build(BuildTarget.Android);

        [MenuItem(MenuRoot + "Build for iOS")]
        static void BuildForIos() => Build(BuildTarget.iOS);

        [MenuItem(MenuRoot + "Open Game Asset Folder")]
        static void OpenGameAssetFolder()
        {
            GameAssetPaths.EnsureFolders();
            EditorUtility.RevealInFinder(GameAssetPaths.DownloadedBundles);
        }

        /// <summary>Builds every bundle for <paramref name="target"/>. Returns false if nothing was built.</summary>
        public static bool Build(BuildTarget target)
        {
            var errors = new List<string>();
            List<AssetBundleBuild> bundles = Plan(FindGameAssetFiles(), errors);
            ValidateDefinitions(bundles, errors);

            foreach (string error in errors) Debug.LogError(error);
            if (errors.Count > 0) return false;

            if (bundles.Count == 0)
            {
                Debug.LogWarning($"Nothing to build: put models in a folder under {GameAssetImport.Root}, " +
                                 $"e.g. {GameAssetImport.Root}/Houses/Cottage.fbx.");
                return false;
            }

            string platform = PlatformName(target);
            string output = Path.Combine(GameAssetPaths.EditorOutputFolderName, platform);
            Directory.CreateDirectory(output);

            AssetBundleManifest manifest = BuildPipeline.BuildAssetBundles(
                output,
                bundles.ToArray(),
                BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.StrictMode,
                target);

            if (manifest == null)
            {
                Debug.LogError($"Building asset bundles for {platform} failed; see the errors above.");
                return false;
            }

            RemoveStaleBundles(output, platform, manifest.GetAllAssetBundles());
            Debug.Log($"Built {bundles.Count} asset bundle(s) for {platform} into {Path.GetFullPath(output)}.");
            return true;
        }

        /// <summary>
        /// Groups asset paths into bundles by their first folder under Assets/GameAssets. Scripts and
        /// files outside a group folder are skipped. Two assets in one bundle with the same load name
        /// are reported in <paramref name="errors"/>, since only one could be loaded; see <see cref="LoadName"/>.
        /// So is a bundle without an asset.json at the top of its folder.
        /// </summary>
        public static List<AssetBundleBuild> Plan(IEnumerable<string> assetPaths, List<string> errors)
        {
            var groups = new SortedDictionary<string, List<string>>();
            var folders = new Dictionary<string, string>();
            var described = new HashSet<string>();

            foreach (string path in assetPaths)
            {
                if (!GameAssetImport.IsGameAsset(path)) continue;
                if (NotBundleable.Contains(Path.GetExtension(path).ToLowerInvariant())) continue;

                string relative = path.Substring(GameAssetImport.Root.Length + 1);
                int slash = relative.IndexOf('/');
                if (slash < 0) continue;

                string bundle = relative.Substring(0, slash).ToLowerInvariant();
                if (!groups.TryGetValue(bundle, out List<string> members)) groups.Add(bundle, members = new List<string>());
                members.Add(path);

                folders[bundle] = GameAssetImport.Root + "/" + relative.Substring(0, slash);
                if (relative.Substring(slash + 1).Equals(GameAssetImport.BundleDefinitionFile, System.StringComparison.OrdinalIgnoreCase))
                    described.Add(bundle);
            }

            foreach (string bundle in groups.Keys.Where(b => !described.Contains(b)))
            {
                errors.Add($"Bundle '{bundle}' has no {GameAssetImport.BundleDefinitionFile}. Add " +
                           $"{folders[bundle]}/{GameAssetImport.BundleDefinitionFile} with an \"id\" and a " +
                           "\"type\" of structure or path.");
            }

            var builds = new List<AssetBundleBuild>();
            foreach (KeyValuePair<string, List<string>> group in groups)
            {
                List<string> members = group.Value;
                members.Sort(System.StringComparer.Ordinal);
                string[] names = members.Select(LoadName).ToArray();

                foreach (IGrouping<string, string> clash in members.GroupBy(LoadName).Where(g => g.Count() > 1))
                {
                    errors.Add($"Bundle '{group.Key}' has more than one asset named '{clash.Key}': " +
                               string.Join(", ", clash) + ". Rename one so each can be loaded by name.");
                }

                builds.Add(new AssetBundleBuild
                {
                    assetBundleName = group.Key,
                    assetNames = members.ToArray(),
                    addressableNames = names,
                });
            }
            return builds;
        }

        /// <summary>
        /// What an asset is loaded by: its file name without extension, except JSON definitions, which
        /// keep ".json" so Cottage.json and Cottage.fbx can sit side by side.
        /// </summary>
        public static string LoadName(string assetPath)
        {
            string name = Path.GetFileNameWithoutExtension(assetPath);
            return IsDefinition(assetPath) ? BundleLoader.DefinitionAssetName(name) : name;
        }

        static bool IsDefinition(string assetPath) =>
            Path.GetExtension(assetPath).Equals(BundleLoader.DefinitionExtension, System.StringComparison.OrdinalIgnoreCase);

        /// <summary>Parses every JSON definition going into a bundle, the same way the game will.</summary>
        static void ValidateDefinitions(List<AssetBundleBuild> bundles, List<string> errors)
        {
            foreach (AssetBundleBuild bundle in bundles)
            {
                foreach (string path in bundle.assetNames.Where(IsDefinition))
                    ModelDefinitionParser.Parse(File.ReadAllText(path), path, errors);
            }
        }

        /// <summary>The bundle folder name for <paramref name="target"/>; matches <see cref="GameAssetPaths.PlatformName"/>.</summary>
        public static string PlatformName(BuildTarget target)
        {
            switch (target)
            {
                case BuildTarget.Android: return "Android";
                case BuildTarget.iOS: return "iOS";
                case BuildTarget.StandaloneWindows:
                case BuildTarget.StandaloneWindows64: return "Windows";
                case BuildTarget.StandaloneOSX: return "OSX";
                case BuildTarget.StandaloneLinux64: return "Linux";
                case BuildTarget.WebGL: return "WebGL";
                default: return target.ToString();
            }
        }

        static BuildTarget EditorTarget()
        {
            switch (Application.platform)
            {
                case RuntimePlatform.OSXEditor: return BuildTarget.StandaloneOSX;
                case RuntimePlatform.LinuxEditor: return BuildTarget.StandaloneLinux64;
                default: return BuildTarget.StandaloneWindows64;
            }
        }

        static IEnumerable<string> FindGameAssetFiles()
        {
            if (!AssetDatabase.IsValidFolder(GameAssetImport.Root)) return Enumerable.Empty<string>();

            return AssetDatabase.FindAssets(string.Empty, new[] { GameAssetImport.Root })
                .Select(guid => AssetDatabase.GUIDToAssetPath(guid))
                .Where(path => !AssetDatabase.IsValidFolder(path))
                .Distinct();
        }

        /// <summary>Deletes bundles left over from groups that no longer exist.</summary>
        static void RemoveStaleBundles(string output, string platform, string[] built)
        {
            var keep = new HashSet<string>(built) { platform };
            foreach (string file in Directory.GetFiles(output))
            {
                string name = Path.GetFileName(file);
                if (name.EndsWith(".manifest")) name = name.Substring(0, name.Length - ".manifest".Length);
                if (!keep.Contains(name)) File.Delete(file);
            }
        }

        public void OnPreprocessBuild(BuildReport report)
        {
            RemoveStreamingBundles();

            string platform = PlatformName(report.summary.platform);
            string source = Path.Combine(GameAssetPaths.EditorOutputFolderName, platform);
            if (!Directory.Exists(source))
            {
                Debug.LogWarning($"No asset bundles built for {platform}; the app will ship without them. " +
                                 $"Use {MenuRoot}Build for {platform} first.");
                return;
            }

            string destination = Path.Combine(StreamingBundlesFolder, platform);
            Directory.CreateDirectory(destination);

            // The .manifest text files are build-time information only; the app reads the manifest bundle.
            foreach (string file in Directory.GetFiles(source).Where(f => !f.EndsWith(".manifest")))
                File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), true);
        }

        public void OnPostprocessBuild(BuildReport report) => RemoveStreamingBundles();

        static void RemoveStreamingBundles()
        {
            if (Directory.Exists(StreamingBundlesFolder)) Directory.Delete(StreamingBundlesFolder, true);
            if (File.Exists(StreamingBundlesFolder + ".meta")) File.Delete(StreamingBundlesFolder + ".meta");
        }
    }
}
