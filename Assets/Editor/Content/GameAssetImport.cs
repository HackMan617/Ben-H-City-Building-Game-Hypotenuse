using System.Collections.Generic;
using System.IO;
using CityBuilder.Content;
using UnityEditor;

namespace CityBuilder.EditorTools
{
    /// <summary>
    /// Mobile-friendly import defaults for models and textures dropped into Assets/GameAssets.
    ///
    /// Applied only the first time a file is imported, so anything changed by hand in the Inspector
    /// afterwards sticks.
    /// </summary>
    public class GameAssetImport : AssetPostprocessor
    {
        public const string Root = "Assets/GameAssets";

        /// <summary>The bundle's own definition, at the top of each bundle folder.</summary>
        public const string BundleDefinitionFile = BundleLoader.BundleDefinitionName + BundleLoader.DefinitionExtension;

        /// <summary>Largest texture side on import; phones rarely gain from more and pay for it in memory.</summary>
        const int MaxTextureSize = 2048;

        public static bool IsGameAsset(string assetPath) => assetPath.StartsWith(Root + "/");

        /// <summary>
        /// The bundle folder <paramref name="assetPath"/> sits in, e.g. Assets/GameAssets/BasicRoad, or
        /// null for anything not inside one.
        /// </summary>
        public static string BundleFolder(string assetPath)
        {
            if (!IsGameAsset(assetPath)) return null;

            int slash = assetPath.IndexOf('/', Root.Length + 1);
            return slash < 0 ? null : assetPath.Substring(0, slash);
        }

        /// <summary>
        /// The "type" in the asset.json of <paramref name="bundleFolder"/>, read straight from disk so it
        /// works before that file is imported. Null when it is missing or broken.
        /// </summary>
        public static ModelType? BundleType(string bundleFolder)
        {
            string path = bundleFolder + "/" + BundleDefinitionFile;
            if (!File.Exists(path)) return null;

            ModelDefinition definition = ModelDefinitionParser.Parse(File.ReadAllText(path), path, new List<string>());
            return definition?.Type;
        }

        void OnPreprocessModel()
        {
            if (!IsGameAsset(assetPath) || !assetImporter.importSettingsMissing) return;

            var model = (ModelImporter)assetImporter;

            // Smaller meshes on disk, and no CPU-side copy kept in memory once uploaded to the GPU.
            model.meshCompression = ModelImporterMeshCompression.Medium;
            // Path models -- roads -- are copied and bent along splines at runtime, which needs the mesh
            // data on the CPU. Put the bundle's asset.json in place before its models so this sees it.
            string bundle = BundleFolder(assetPath);
            model.isReadable = bundle != null && BundleType(bundle) == ModelType.Path;
            model.optimizeMeshPolygons = true;
            model.optimizeMeshVertices = true;

            // Scene clutter an .fbx often carries that a city piece never needs.
            model.importCameras = false;
            model.importLights = false;
            model.importVisibility = false;

            // Builds URP materials from the file's own material descriptions.
            model.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
        }

        void OnPreprocessTexture()
        {
            if (!IsGameAsset(assetPath) || !assetImporter.importSettingsMissing) return;

            var texture = (TextureImporter)assetImporter;
            texture.maxTextureSize = MaxTextureSize;
            texture.mipmapEnabled = true;
            texture.isReadable = false;
        }
    }
}
