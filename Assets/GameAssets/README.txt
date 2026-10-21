Game assets that ship in asset bundles.

Put each group of assets in its own folder here; every folder becomes one bundle,
named after the folder in lower case:

    Assets/GameAssets/Houses/Cottage.fbx   ->  bundle "houses", asset "Cottage"
    Assets/GameAssets/Houses/Textures/...      (subfolders go in the same bundle)

Every bundle folder needs an asset.json at its top saying what the bundle holds, or the build
stops. It is a definition like any other (see below) whose "type" is "structure" for buildings
and the like or "path" for a road type:

    Assets/GameAssets/Houses/asset.json      { "schemaVersion": 1, "type": "structure", "id": "houses" }

.fbx models get mobile-friendly import settings the first time they are imported.
Build with City Builder > Asset Bundles > Build for Editor / Android / iOS, then load in
code with BundleLoader.LoadAssetAsync<GameObject>("houses", "Cottage").

Files sitting directly in this folder, like this one, are not bundled.

Each model can have a JSON definition beside it with the same name -- Cottage.json next to
Cottage.fbx -- holding its behaviours and technical data. Copy ModelDefinition.example.json to
start one. Every definition, asset.json included, must give a "type": "structure" or "path". Every definition is checked when bundles are built; load one with
BundleLoader.LoadDefinitionAsync("houses", "Cottage"). Behaviour types this build doesn't know
are kept, not rejected, so newer content still loads.

A definition's "footprint" gives the cells the model covers (width x depth, or an irregular
"cells" mask written north row first), its height, the anchor cell that follows the cursor and
the side its entrance faces. "placement" says where it may go: "roadAccess" (any edge or only
the front, within a distance), "clearance" from other buildings, "keepAwayFrom" buildings by id
or tag, and "maxCount" per city. Every field is optional; BuildingPlacement.Check applies them.

Roads -- the "roads" bundle, Assets/GameAssets/Roads -- holds the road models. Roads are still
drawn cell by cell, but each run between junctions is built as a spline with rounded bends and
RoadSegment is repeated along it, bent to follow the curve. Optional junction models RoadCross,
RoadTee (arms east, south, west) and RoadEnd (one arm north) replace the generated pieces at
straight junctions and dead ends; anything missing is generated. Model everything for a 1-unit
cell, the segment running along +Z, junctions centred with arms reaching the cell edge, and match
the grid's road width and height. Models in this folder import with Read/Write on, which bending
needs. City Builder > Asset Bundles > Create Placeholder Road Models writes a stand-in RoadSegment;
replace it with a real model of the same name and rebuild the bundles.
