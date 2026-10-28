using CityBuilder.Roads;
using UnityEngine;

namespace CityBuilder.Grid
{
    /// <summary>
    /// Bridges screen space, world space and grid space.
    ///
    /// Pointer input resolves by raycasting against the ground plane rather than by reading screen
    /// axes, so the build tool keeps working unchanged if the camera later gains yaw and tilt.
    /// </summary>
    public class CityGrid : MonoBehaviour
    {
        [SerializeField] RoadProfile _profile = RoadProfile.Default;

        [Tooltip("Half-width of the buildable area, in cells. Cells outside are rejected.")]
        [SerializeField] int _halfExtent = 60;

        [Tooltip("World height of one terrain level.")]
        [SerializeField] float _levelHeight = 0.5f;

        public RoadProfile Profile => _profile;
        public float CellSize => _profile.CellSize;
        public int HalfExtent => _halfExtent;
        public float LevelHeight => _levelHeight;

        /// <summary>How high each cell is raised. Level 0 is the ground plane.</summary>
        public TerrainMap Terrain { get; } = new TerrainMap();

        /// <summary>The level-0 surface. The camera pans against this, whatever the terrain does.</summary>
        public Plane GroundPlane => new Plane(Vector3.up, Vector3.zero);

        public bool InBounds(GridCoord cell) =>
            cell.X >= -_halfExtent && cell.X < _halfExtent &&
            cell.Z >= -_halfExtent && cell.Z < _halfExtent;

        public float HeightOf(int level) => level * _levelHeight;
        public float HeightAt(GridCoord cell) => HeightOf(Terrain.LevelAt(cell));

        /// <summary>Centre of a cell, on top of its terrain.</summary>
        public Vector3 CellToWorld(GridCoord cell) =>
            new Vector3((cell.X + 0.5f) * _profile.CellSize, HeightAt(cell), (cell.Z + 0.5f) * _profile.CellSize);

        public GridCoord WorldToCell(Vector3 world) => new GridCoord(
            Mathf.FloorToInt(world.x / _profile.CellSize),
            Mathf.FloorToInt(world.z / _profile.CellSize));

        /// <summary>
        /// Where a screen point meets the ground. False when the ray runs parallel to the plane or
        /// points away from it -- which happens whenever the cursor is above the horizon.
        /// </summary>
        public bool TryScreenToWorld(Camera camera, Vector2 screenPoint, out Vector3 world)
        {
            world = default;
            if (camera == null) return false;

            Ray ray = camera.ScreenPointToRay(screenPoint);
            if (!GroundPlane.Raycast(ray, out float distance)) return false;

            world = ray.GetPoint(distance);
            return true;
        }

        public bool TryScreenToCell(Camera camera, Vector2 screenPoint, out GridCoord cell)
        {
            cell = default;
            if (!TryScreenToSurface(camera, screenPoint, out Vector3 world)) return false;

            cell = WorldToCell(world);
            return InBounds(cell);
        }

        /// <summary>
        /// Where a screen point meets the terrain, raised cells included. The ray is tried against
        /// each level's plane from the top down; the first cell found standing at least that high is
        /// the one under the pointer, whether the ray landed on its top or ran into its side.
        /// </summary>
        public bool TryScreenToSurface(Camera camera, Vector2 screenPoint, out Vector3 world)
        {
            world = default;
            if (camera == null) return false;

            Ray ray = camera.ScreenPointToRay(screenPoint);
            for (int level = Terrain.Highest; level > 0; level--)
            {
                var plane = new Plane(Vector3.up, new Vector3(0f, HeightOf(level), 0f));
                if (!plane.Raycast(ray, out float distance)) continue;

                Vector3 hit = ray.GetPoint(distance);
                GridCoord cell = WorldToCell(hit);
                int cellLevel = Terrain.LevelAt(cell);
                if (cellLevel < level) continue;

                world = new Vector3(hit.x, HeightOf(cellLevel), hit.z);
                return true;
            }

            if (!TryScreenToWorld(camera, screenPoint, out world)) return false;
            world.y = HeightAt(WorldToCell(world));
            return true;
        }

        void OnValidate()
        {
            _levelHeight = Mathf.Max(0.01f, _levelHeight);
            _profile.CellSize = Mathf.Max(0.01f, _profile.CellSize);
            _profile.Height = Mathf.Max(0f, _profile.Height);
            _halfExtent = Mathf.Max(1, _halfExtent);
        }
    }
}
