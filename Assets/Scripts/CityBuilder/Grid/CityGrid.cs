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

        public RoadProfile Profile => _profile;
        public float CellSize => _profile.CellSize;
        public int HalfExtent => _halfExtent;

        /// <summary>The build surface. Everything snaps to y = 0.</summary>
        public Plane GroundPlane => new Plane(Vector3.up, Vector3.zero);

        public bool InBounds(GridCoord cell) =>
            cell.X >= -_halfExtent && cell.X < _halfExtent &&
            cell.Z >= -_halfExtent && cell.Z < _halfExtent;

        /// <summary>Centre of a cell, on the ground plane.</summary>
        public Vector3 CellToWorld(GridCoord cell) =>
            new Vector3((cell.X + 0.5f) * _profile.CellSize, 0f, (cell.Z + 0.5f) * _profile.CellSize);

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
            if (!TryScreenToWorld(camera, screenPoint, out Vector3 world)) return false;

            cell = WorldToCell(world);
            return InBounds(cell);
        }

        void OnValidate()
        {
            _profile.CellSize = Mathf.Max(0.01f, _profile.CellSize);
            _profile.Height = Mathf.Max(0f, _profile.Height);
            _halfExtent = Mathf.Max(1, _halfExtent);
        }
    }
}
