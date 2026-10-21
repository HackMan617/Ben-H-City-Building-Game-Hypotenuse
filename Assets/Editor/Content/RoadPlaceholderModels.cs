using System.Collections.Generic;
using System.IO;
using CityBuilder.Roads;
using UnityEditor;
using UnityEngine;

namespace CityBuilder.EditorTools
{
    /// <summary>
    /// Writes a stand-in road type into Assets/GameAssets/BasicRoad so there is a road to pick until
    /// real models arrive: an asset.json marking the bundle as a path, and a RoadSegment prefab with an
    /// asphalt strip and a dashed centre line, sized to the default road profile. Replace
    /// RoadSegment.prefab with a real model of the same name -- an .fbx called RoadSegment works -- and
    /// rebuild the bundles. See <see cref="RoadModels"/> for what else the bundle may hold.
    /// </summary>
    public static class RoadPlaceholderModels
    {
        const string FolderName = "BasicRoad";
        const string Folder = GameAssetImport.Root + "/" + FolderName;

        const string Definition = @"{
  ""schemaVersion"": 1,
  ""type"": ""path"",
  ""id"": ""basic-road"",
  ""displayName"": ""Basic Road""
}
";

        [MenuItem("City Builder/Asset Bundles/Create Placeholder Road Models")]
        public static void Create()
        {
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder(GameAssetImport.Root, FolderName);

            // Written first, so models imported into the folder afterwards get Read/Write.
            string definitionPath = $"{Folder}/{GameAssetImport.BundleDefinitionFile}";
            if (!File.Exists(definitionPath))
            {
                File.WriteAllText(definitionPath, Definition);
                AssetDatabase.ImportAsset(definitionPath);
            }

            Material asphalt = CreateMaterial("RoadAsphalt", new Color(0.2f, 0.21f, 0.23f), 0.18f);
            Material marking = CreateMaterial("RoadMarking", new Color(0.92f, 0.9f, 0.82f), 0.3f);

            Mesh mesh = BuildSegmentMesh(RoadProfile.Default);
            string meshPath = $"{Folder}/RoadSegmentMesh.asset";
            AssetDatabase.DeleteAsset(meshPath);
            AssetDatabase.CreateAsset(mesh, meshPath);

            var go = new GameObject(RoadModels.SegmentName);
            try
            {
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterials = new[] { asphalt, marking };
                PrefabUtility.SaveAsPrefabAsset(go, $"{Folder}/{RoadModels.SegmentName}.prefab");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"Placeholder road models written to {Folder}. Build the asset bundles to use them.");
        }

        /// <summary>The generated strip as part 0, plus a dashed centre line laid just on top as part 1.</summary>
        static Mesh BuildSegmentMesh(RoadProfile profile)
        {
            RoadPiece strip = RoadPiece.GeneratedSegment(profile);

            var vertices = new List<Vector3>(strip.Vertices);
            var normals = new List<Vector3>(strip.Normals);
            var uvs = new List<Vector2>(strip.Uvs);
            var dash = new List<int>();

            const int rings = 4;
            const float halfWidth = 0.03f;
            const float start = 0.25f;
            const float end = 0.75f;
            float y = profile.Height / profile.CellSize + 0.002f;

            int first = vertices.Count;
            for (int r = 0; r <= rings; r++)
            {
                float z = Mathf.Lerp(start, end, r / (float)rings);
                vertices.Add(new Vector3(-halfWidth, y, z));
                vertices.Add(new Vector3(halfWidth, y, z));
                normals.Add(Vector3.up);
                normals.Add(Vector3.up);
                uvs.Add(new Vector2(0f, z));
                uvs.Add(new Vector2(1f, z));
            }
            for (int r = 0; r < rings; r++)
            {
                int a = first + r * 2;
                dash.AddRange(new[] { a, a + 2, a + 3, a, a + 3, a + 1 });
            }

            var mesh = new Mesh { name = "RoadSegmentMesh" };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = 2;
            mesh.SetTriangles(strip.Triangles[0], 0);
            mesh.SetTriangles(dash, 1);
            mesh.RecalculateBounds();
            return mesh;
        }

        static Material CreateMaterial(string name, Color colour, float smoothness)
        {
            string path = $"{Folder}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(material, path);
            }

            material.SetColor("_BaseColor", colour);
            material.SetFloat("_Smoothness", smoothness);
            EditorUtility.SetDirty(material);
            return material;
        }
    }
}
