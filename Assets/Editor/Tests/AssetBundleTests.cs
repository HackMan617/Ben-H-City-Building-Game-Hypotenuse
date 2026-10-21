using System.Collections.Generic;
using System.IO;
using CityBuilder.Content;
using CityBuilder.EditorTools;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CityBuilder.Tests
{
    /// <summary>
    /// How Assets/GameAssets is grouped into bundles, and where the game looks for them.
    /// </summary>
    public class AssetBundleTests
    {
        static List<AssetBundleBuild> Plan(List<string> errors, params string[] paths) =>
            AssetBundleBuilder.Plan(paths, errors);

        [Test]
        public void EachTopFolderIsOneBundleNamedInLowerCase()
        {
            var errors = new List<string>();
            List<AssetBundleBuild> builds = Plan(errors,
                "Assets/GameAssets/Houses/asset.json",
                "Assets/GameAssets/Houses/Cottage.fbx",
                "Assets/GameAssets/Houses/Textures/Brick.png",
                "Assets/GameAssets/Trees/asset.json",
                "Assets/GameAssets/Trees/Oak.fbx");

            Assert.IsEmpty(errors);
            Assert.AreEqual(2, builds.Count);

            Assert.AreEqual("houses", builds[0].assetBundleName);
            CollectionAssert.AreEqual(
                new[] { "Assets/GameAssets/Houses/Cottage.fbx", "Assets/GameAssets/Houses/Textures/Brick.png",
                        "Assets/GameAssets/Houses/asset.json" },
                builds[0].assetNames);
            CollectionAssert.AreEqual(new[] { "Cottage", "Brick", "asset.json" }, builds[0].addressableNames);

            Assert.AreEqual("trees", builds[1].assetBundleName);
            CollectionAssert.AreEqual(new[] { "Oak", "asset.json" }, builds[1].addressableNames);
        }

        [Test]
        public void LooseFilesScriptsAndOtherFoldersAreSkipped()
        {
            var errors = new List<string>();
            List<AssetBundleBuild> builds = Plan(errors,
                "Assets/GameAssets/README.txt",
                "Assets/GameAssets/Houses/HouseSpin.cs",
                "Assets/Materials/Road.mat",
                "Assets/GameAssetsExtra/Thing.fbx");

            Assert.IsEmpty(errors);
            Assert.IsEmpty(builds);
        }

        [Test]
        public void SameFileNameTwiceInOneBundleIsAnError()
        {
            var errors = new List<string>();
            Plan(errors,
                "Assets/GameAssets/Houses/asset.json",
                "Assets/GameAssets/Houses/Cottage.fbx",
                "Assets/GameAssets/Houses/Old/Cottage.fbx");

            Assert.AreEqual(1, errors.Count);
            StringAssert.Contains("Cottage", errors[0]);
        }

        [Test]
        public void SameFileNameInDifferentBundlesIsFine()
        {
            var errors = new List<string>();
            Plan(errors,
                "Assets/GameAssets/Houses/asset.json",
                "Assets/GameAssets/Houses/Roof.fbx",
                "Assets/GameAssets/Shops/asset.json",
                "Assets/GameAssets/Shops/Roof.fbx");

            Assert.IsEmpty(errors);
        }

        [Test]
        public void EveryBundleNeedsAnAssetJsonAtItsTop()
        {
            var errors = new List<string>();
            Plan(errors,
                "Assets/GameAssets/Houses/asset.json",
                "Assets/GameAssets/Houses/Cottage.fbx",
                "Assets/GameAssets/BasicRoad/RoadSegment.fbx",
                "Assets/GameAssets/BasicRoad/Old/asset.json");

            Assert.AreEqual(1, errors.Count);
            StringAssert.Contains("'basicroad'", errors[0]);
            StringAssert.Contains("Assets/GameAssets/BasicRoad/asset.json", errors[0]);
        }

        [TestCase(BuildTarget.Android, RuntimePlatform.Android)]
        [TestCase(BuildTarget.iOS, RuntimePlatform.IPhonePlayer)]
        [TestCase(BuildTarget.StandaloneWindows64, RuntimePlatform.WindowsPlayer)]
        [TestCase(BuildTarget.StandaloneWindows64, RuntimePlatform.WindowsEditor)]
        [TestCase(BuildTarget.StandaloneOSX, RuntimePlatform.OSXEditor)]
        public void BuilderAndGameAgreeOnPlatformFolder(BuildTarget target, RuntimePlatform platform)
        {
            Assert.AreEqual(AssetBundleBuilder.PlatformName(target), GameAssetPaths.PlatformName(platform));
        }

        [Test]
        public void GameAssetFolderIsCreatedOnTheDevice()
        {
            GameAssetPaths.EnsureFolders();

            Assert.IsTrue(Directory.Exists(GameAssetPaths.DownloadedBundles));
            StringAssert.StartsWith(Application.persistentDataPath, GameAssetPaths.Root);
        }

        [Test]
        public void DownloadedBundlesWinOverShippedOnes()
        {
            GameAssetPaths.EnsureFolders();
            string name = "test-bundle-" + System.Guid.NewGuid().ToString("N");
            string downloaded = Path.Combine(GameAssetPaths.DownloadedBundles, name);

            Assert.AreEqual(Path.Combine(GameAssetPaths.ShippedBundles, name), GameAssetPaths.Resolve(name));

            File.WriteAllText(downloaded, string.Empty);
            try
            {
                Assert.AreEqual(downloaded, GameAssetPaths.Resolve(name));
            }
            finally
            {
                File.Delete(downloaded);
            }
        }
    }
}
