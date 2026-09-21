using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace CityBuilder.EditorTools
{
    /// <summary>
    /// Rebuilds the prototype scene from scratch: grid, ground, road system, preview, camera, HUD.
    ///
    /// Kept as a script rather than a hand-authored scene so the setup is reviewable, re-runnable,
    /// and survives anyone wrecking the scene while experimenting.
    /// </summary>
    public static class CityPrototypeSetup
    {
        const string ScenePath = "Assets/Scenes/CityPrototype.unity";
        const string MaterialFolder = "Assets/Materials";

        /// <summary>
        /// Builds the scene once, on the first domain reload after the scripts land. Guarded on the
        /// scene file being absent, so re-running it later is an explicit choice via the menu item
        /// rather than something that quietly overwrites work.
        /// </summary>
        [InitializeOnLoadMethod]
        static void BuildOnceIfMissing()
        {
            if (System.IO.File.Exists(ScenePath)) return;

            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) return;
                if (System.IO.File.Exists(ScenePath)) return;
                Build();
            };
        }

        [MenuItem("City Builder/Rebuild Prototype Scene")]
        public static void Build()
        {
            if (!AssetDatabase.IsValidFolder(MaterialFolder))
                AssetDatabase.CreateFolder("Assets", "Materials");

            Material road = MakeLit(MaterialFolder + "/Road.mat", new Color(0.20f, 0.21f, 0.23f, 1f), false);

            // The ghost has its own shader: road pieces overlap, and URP Lit would blend them twice.
            Shader ghostShader = Shader.Find("CityBuilder/GhostRoad");
            if (ghostShader == null)
            {
                Debug.LogError("CityPrototypeSetup: shader 'CityBuilder/GhostRoad' not found.");
                return;
            }

            Material ghostBuild = MakeGhost(ghostShader, MaterialFolder + "/GhostBuild.mat", new Color(0.35f, 0.90f, 0.72f, 0.50f));
            Material ghostErase = MakeGhost(ghostShader, MaterialFolder + "/GhostErase.mat", new Color(1.00f, 0.36f, 0.30f, 0.50f));
            Material ghostBlocked = MakeGhost(ghostShader, MaterialFolder + "/GhostBlocked.mat", new Color(0.85f, 0.10f, 0.16f, 0.38f));

            Shader gridShader = Shader.Find("CityBuilder/GridGround");
            if (gridShader == null)
            {
                Debug.LogError("CityPrototypeSetup: shader 'CityBuilder/GridGround' not found.");
                return;
            }

            Material ground = MakeAsset(new Material(gridShader), MaterialFolder + "/GridGround.mat");

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Grid definition: cell size, bounds, and screen-to-cell resolution.
            var gridGo = new GameObject("CityGrid");
            var cityGrid = gridGo.AddComponent<CityBuilder.Grid.CityGrid>();

            // Ground plane. Grid lines are drawn procedurally by the shader, not by line geometry.
            var groundGo = GameObject.CreatePrimitive(PrimitiveType.Plane);
            groundGo.name = "Ground";
            groundGo.transform.localScale = new Vector3(14f, 1f, 14f);
            Object.DestroyImmediate(groundGo.GetComponent<MeshCollider>());
            groundGo.GetComponent<MeshRenderer>().sharedMaterial = ground;
            Wire(groundGo.AddComponent<CityBuilder.View.GridOverlay>(), "_grid", cityGrid);

            // Committed road geometry.
            var roadsGo = new GameObject("Roads", typeof(MeshFilter), typeof(MeshRenderer));
            var roadSystem = roadsGo.AddComponent<CityBuilder.Roads.RoadSystem>();
            roadsGo.GetComponent<MeshRenderer>().sharedMaterial = road;
            Wire(roadSystem, "_grid", cityGrid);
            Wire(roadSystem, "_roadMaterial", road);

            // Translucent preview of the stroke in progress.
            var ghostGo = new GameObject("RoadGhost", typeof(MeshFilter), typeof(MeshRenderer));
            var ghost = ghostGo.AddComponent<CityBuilder.Roads.RoadGhost>();
            var ghostRenderer = ghostGo.GetComponent<MeshRenderer>();
            ghostRenderer.sharedMaterial = ghostBuild;
            ghostRenderer.shadowCastingMode = ShadowCastingMode.Off;
            ghostRenderer.receiveShadows = false;
            Wire(ghost, "_buildMaterial", ghostBuild);
            Wire(ghost, "_eraseMaterial", ghostErase);
            Wire(ghost, "_blockedMaterial", ghostBlocked);

            var camGo = new GameObject("Build Camera", typeof(Camera));
            camGo.tag = "MainCamera";
            var cam = camGo.GetComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.07f, 0.08f, 0.10f, 1f);
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 400f;
            Wire(camGo.AddComponent<CityBuilder.View.CameraRig>(), "_grid", cityGrid);

            // CameraRig derives its pose at Awake, which never runs in edit mode. Seed the same pose
            // on the transform so the scene frames correctly before pressing play.
            const float pitch = 58f;
            const float distance = 26f;
            Quaternion camRotation = Quaternion.Euler(pitch, 0f, 0f);
            camGo.transform.SetPositionAndRotation(-(camRotation * Vector3.forward) * distance, camRotation);

            var lightGo = new GameObject("Sun", typeof(Light));
            var sun = lightGo.GetComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.15f;
            sun.color = new Color(1f, 0.97f, 0.90f);
            sun.shadows = LightShadows.Soft;
            lightGo.transform.rotation = Quaternion.Euler(52f, -35f, 0f);

            var toolGo = new GameObject("BuildTool");
            var tool = toolGo.AddComponent<CityBuilder.BuildTools.RoadBuildTool>();
            Wire(tool, "_grid", cityGrid);
            Wire(tool, "_roads", roadSystem);
            Wire(tool, "_ghost", ghost);
            Wire(tool, "_camera", cam);

            var hud = toolGo.AddComponent<CityBuilder.View.BuildHud>();
            Wire(hud, "_roads", roadSystem);
            Wire(hud, "_tool", tool);

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.34f, 0.36f, 0.40f);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("CityPrototypeSetup: built " + ScenePath);
        }

        /// <summary>Assigns a private [SerializeField] reference without making it public.</summary>
        static void Wire(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(field);
            if (prop == null)
            {
                Debug.LogError("CityPrototypeSetup: no field '" + field + "' on " + target.GetType().Name);
                return;
            }
            prop.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static Material MakeLit(string path, Color colour, bool transparent)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            var material = new Material(shader);
            material.SetColor("_BaseColor", colour);
            material.SetFloat("_Smoothness", 0.18f);
            material.SetFloat("_Metallic", 0f);

            if (transparent)
            {
                material.SetFloat("_Surface", 1f);
                material.SetFloat("_Blend", 0f);
                material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                material.SetFloat("_ZWrite", 0f);
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.renderQueue = (int)RenderQueue.Transparent;
            }

            return MakeAsset(material, path);
        }

        static Material MakeGhost(Shader shader, string path, Color colour)
        {
            var material = new Material(shader);
            material.SetColor("_BaseColor", colour);
            return MakeAsset(material, path);
        }

        static Material MakeAsset(Material material, string path)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
            {
                existing.shader = material.shader;
                EditorUtility.CopySerialized(material, existing);
                Object.DestroyImmediate(material);
                return existing;
            }

            AssetDatabase.CreateAsset(material, path);
            return material;
        }
    }
}
