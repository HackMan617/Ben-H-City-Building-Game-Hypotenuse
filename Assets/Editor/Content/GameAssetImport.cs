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

        /// <summary>Largest texture side on import; phones rarely gain from more and pay for it in memory.</summary>
        const int MaxTextureSize = 2048;

        public static bool IsGameAsset(string assetPath) => assetPath.StartsWith(Root + "/");

        void OnPreprocessModel()
        {
            if (!IsGameAsset(assetPath) || !assetImporter.importSettingsMissing) return;

            var model = (ModelImporter)assetImporter;

            // Smaller meshes on disk, and no CPU-side copy kept in memory once uploaded to the GPU.
            model.meshCompression = ModelImporterMeshCompression.Medium;
            model.isReadable = false;
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
