using System.Collections.Generic;
using CityBuilder.Grid;
using CityBuilder.Roads;
using UnityEngine;
using UnityEditor;

namespace CityBuilder.EditorTools
{
    /// <summary>
    /// Stamps one of every junction case into the open scene as static geometry.
    ///
    /// Play mode is the only place road meshes normally exist, which makes tuning the cross-section
    /// a slow loop. This renders the same builder output at edit time so width, curb height and
    /// corner sharpness can be judged directly in the Scene view.
    /// </summary>
    public static class RoadJunctionPreview
    {
        const string ObjectName = "SampleJunctions";

        [MenuItem("City Builder/Preview Sample Junctions")]
        public static void Stamp()
        {
            Clear();

            // Laid down stroke by stroke, the way a player would, so the links come out the same.
            var network = new RoadNetwork();
            var path = new List<GridCoord>();
            var edit = new RoadEdit();
            foreach ((GridCoord from, GridCoord to, bool diagonalFirst) in SampleStrokes())
            {
                RoadPathResolver.Resolve(from, to, PathMode.ElbowWithDiagonal, path, diagonalFirst: diagonalFirst);
                RoadStroke.Plan(network, path, false, edit);
                edit.Apply(network);
            }

            var cells = new List<GridCoord>(network.Cells);
            var mesh = new Mesh { name = ObjectName };

            var grid = Object.FindAnyObjectByType<CityGrid>();
            RoadProfile profile = grid != null ? grid.Profile : RoadProfile.Default;
            RoadMeshBuilder.Build(mesh, network, cells, profile);

            var go = new GameObject(ObjectName, typeof(MeshFilter), typeof(MeshRenderer));
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            go.GetComponent<MeshRenderer>().sharedMaterial =
                AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Road.mat");

            Selection.activeGameObject = go;
            Debug.Log($"RoadJunctionPreview: stamped {cells.Count} cells covering every junction case.");
        }

        [MenuItem("City Builder/Clear Sample Junctions")]
        public static void Clear()
        {
            var existing = GameObject.Find(ObjectName);
            if (existing != null) Object.DestroyImmediate(existing);
        }

        /// <summary>
        /// Strokes that exercise every case the builder handles: isolated cell, dead ends on both
        /// kinds of arm, straight runs on both axes and both diagonals, 90- and 135-degree turns,
        /// a T, a crossroads, a diagonal crossing a straight, a 45-degree fork, and two diagonals
        /// crossing at a cell corner.
        /// </summary>
        static IEnumerable<(GridCoord from, GridCoord to, bool diagonalFirst)> SampleStrokes()
        {
            (GridCoord, GridCoord, bool) Stroke(int x0, int z0, int x1, int z1, bool diagonalFirst = true) =>
                (new GridCoord(x0, z0), new GridCoord(x1, z1), diagonalFirst);

            // Spine running east-west, with dead ends at both tips.
            yield return Stroke(-6, 0, 6, 0);

            // Full crossroads where a north-south run passes straight through the spine.
            yield return Stroke(-3, -4, -3, 4);

            // T-junction: a spur leaving the spine northward only.
            yield return Stroke(1, 0, 1, 4);

            // Squared corner at the eastern tip turning north, then a second closing back west, so an
            // inner and an outer corner sit side by side.
            yield return Stroke(6, 0, 6, 3);
            yield return Stroke(6, 3, 3, 3);

            // Isolated cell: no connections at all.
            yield return Stroke(-6, 4, -6, 4);

            // 135-degree turns: diagonal then straight, and straight then diagonal.
            yield return Stroke(-9, 7, -3, 10);
            yield return Stroke(-9, 12, -3, 15, diagonalFirst: false);

            // A diagonal cutting through a straight road at 45 degrees.
            yield return Stroke(0, 10, 8, 10);
            yield return Stroke(1, 7, 6, 12);

            // 45-degree fork off a north-south road.
            yield return Stroke(10, 5, 10, 13);
            yield return Stroke(10, 8, 13, 11);

            // Two diagonals crossing at a cell corner: the second fills out a 2x2 junction.
            yield return Stroke(-8, 17, -4, 21);
            yield return Stroke(-3, 17, -7, 21);
        }
    }
}
