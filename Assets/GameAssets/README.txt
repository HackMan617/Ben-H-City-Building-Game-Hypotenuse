Game assets that ship in asset bundles.

Put each group of assets in its own folder here; every folder becomes one bundle,
named after the folder in lower case:

    Assets/GameAssets/Houses/Cottage.fbx   ->  bundle "houses", asset "Cottage"
    Assets/GameAssets/Houses/Textures/...      (subfolders go in the same bundle)

.fbx models get mobile-friendly import settings the first time they are imported.
Build with City Builder > Asset Bundles > Build for Editor / Android / iOS, then load in
code with BundleLoader.LoadAssetAsync<GameObject>("houses", "Cottage").

Files sitting directly in this folder, like this one, are not bundled.

Each model can have a JSON definition beside it with the same name -- Cottage.json next to
Cottage.fbx -- holding its behaviours and technical data. Copy ModelDefinition.example.json to
start one. Every definition is checked when bundles are built; load one with
BundleLoader.LoadDefinitionAsync("houses", "Cottage"). Behaviour types this build doesn't know
are kept, not rejected, so newer content still loads.

DefaultModels/ holds the models the game ships with (bundle "defaultmodels").
