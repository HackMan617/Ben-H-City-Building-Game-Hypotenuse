Game assets that ship in asset bundles.

Put each group of assets in its own folder here; every folder becomes one bundle,
named after the folder in lower case:

    Assets/GameAssets/Houses/Cottage.fbx   ->  bundle "houses", asset "Cottage"
    Assets/GameAssets/Houses/Textures/...      (subfolders go in the same bundle)

.fbx models get mobile-friendly import settings the first time they are imported.
Build with City Builder > Asset Bundles > Build for Editor / Android / iOS, then load in
code with BundleLoader.LoadAssetAsync<GameObject>("houses", "Cottage").

Files sitting directly in this folder, like this one, are not bundled.

DefaultModels/ holds the models the game ships with (bundle "defaultmodels").
