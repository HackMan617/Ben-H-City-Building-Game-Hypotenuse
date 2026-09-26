using System.IO;
using UnityEngine;

namespace CityBuilder.Content
{
    /// <summary>
    /// Where the game's asset bundles live, on the device and in the project.
    ///
    /// Bundles are looked for in three places, first match wins:
    ///
    ///   persistentDataPath/GameAssets/Bundles/&lt;Platform&gt;   downloaded or updated content; the game
    ///                                                      creates this folder on launch
    ///   &lt;project&gt;/AssetBundles/&lt;Platform&gt;                  editor play mode only, straight from the
    ///                                                      bundle builder's output
    ///   StreamingAssets/Bundles/&lt;Platform&gt;                  bundles shipped inside the app
    ///
    /// persistentDataPath is the one folder a game can always write to on Android, iOS and desktop.
    /// Bundles are built per platform -- textures and shaders differ -- so every path ends in the
    /// platform's folder name.
    /// </summary>
    public static class GameAssetPaths
    {
        public const string FolderName = "GameAssets";
        public const string BundlesFolderName = "Bundles";

        /// <summary>Project-root folder the editor bundle builder writes to.</summary>
        public const string EditorOutputFolderName = "AssetBundles";

        /// <summary>The game's own folder on the device.</summary>
        public static string Root => Path.Combine(Application.persistentDataPath, FolderName);

        /// <summary>Bundles downloaded or installed after the app shipped. Checked first.</summary>
        public static string DownloadedBundles => Path.Combine(Root, BundlesFolderName, Platform);

        /// <summary>Bundles shipped inside the app. On Android this is inside the APK, so it cannot be listed.</summary>
        public static string ShippedBundles => Path.Combine(Application.streamingAssetsPath, BundlesFolderName, Platform);

#if UNITY_EDITOR
        /// <summary>The bundle builder's output for the platform the editor itself runs on.</summary>
        public static string EditorBundles =>
            Path.Combine(Path.GetDirectoryName(Application.dataPath), EditorOutputFolderName, Platform);
#endif

        /// <summary>Folder name for bundles this build can load.</summary>
        public static string Platform => PlatformName(Application.platform);

        /// <summary>
        /// The folder name for bundles built for <paramref name="platform"/>. The editor counts as its
        /// desktop platform: bundles built for a phone do not render in the editor.
        /// </summary>
        public static string PlatformName(RuntimePlatform platform)
        {
            switch (platform)
            {
                case RuntimePlatform.Android: return "Android";
                case RuntimePlatform.IPhonePlayer: return "iOS";
                case RuntimePlatform.WindowsPlayer:
                case RuntimePlatform.WindowsEditor: return "Windows";
                case RuntimePlatform.OSXPlayer:
                case RuntimePlatform.OSXEditor: return "OSX";
                case RuntimePlatform.LinuxPlayer:
                case RuntimePlatform.LinuxEditor: return "Linux";
                case RuntimePlatform.WebGLPlayer: return "WebGL";
                default: return platform.ToString();
            }
        }

        /// <summary>Creates the game's folders on the device if they are missing. Safe to call repeatedly.</summary>
        public static void EnsureFolders()
        {
            Directory.CreateDirectory(DownloadedBundles);
        }

        /// <summary>
        /// Where to load <paramref name="fileName"/> from, following the lookup order above. Shipped
        /// bundles are returned without checking they exist, because Android cannot see into the APK;
        /// a missing one fails at load time instead.
        /// </summary>
        public static string Resolve(string fileName)
        {
            string downloaded = Path.Combine(DownloadedBundles, fileName);
            if (File.Exists(downloaded)) return downloaded;

#if UNITY_EDITOR
            string editor = Path.Combine(EditorBundles, fileName);
            if (File.Exists(editor)) return editor;
#endif

            return Path.Combine(ShippedBundles, fileName);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void CreateFoldersOnLaunch() => EnsureFolders();
    }
}
