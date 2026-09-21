using CityBuilder.Grid;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CityBuilder.View
{
    /// <summary>
    /// Overhead build camera: pan, zoom, fixed tilt.
    ///
    /// The pose is derived every frame from focus + yaw + pitch + distance rather than accumulated
    /// onto the transform, and pan directions are taken from the camera basis rather than world axes.
    /// Orbit is therefore already wired: unlock <see cref="_yaw"/> and drive it from input, and pan,
    /// zoom and the build tool all keep working, because the tool reads the ground through a raycast.
    ///
    ///   WASD / arrows   pan          middle-drag   drag the ground
    ///   scroll          zoom         Q / E         yaw, when unlocked
    /// </summary>
    public class CameraRig : MonoBehaviour
    {
        [Header("Framing")]
        [SerializeField] Vector3 _focus = Vector3.zero;
        [Tooltip("Degrees below horizontal. 90 looks straight down.")]
        [Range(20f, 90f)] [SerializeField] float _pitch = 58f;
        [SerializeField] float _yaw;
        [SerializeField] float _distance = 26f;
        [SerializeField] float _minDistance = 5f;
        [SerializeField] float _maxDistance = 90f;

        [Header("Controls")]
        [SerializeField] float _panSpeed = 14f;
        [SerializeField] float _zoomSpeed = 4f;
        [SerializeField] float _yawSpeed = 90f;
        [Tooltip("Leave off for the fixed-heading build view; on, Q and E orbit the focus.")]
        [SerializeField] bool _allowYaw;
        [SerializeField] float _smoothing = 12f;

        [Header("Bounds")]
        [SerializeField] CityGrid _grid;

        Camera _camera;
        Vector3 _targetFocus;
        float _targetDistance;
        float _targetYaw;

        bool _panning;
        Vector3 _panGrabPoint;

        void Awake()
        {
            _camera = GetComponent<Camera>();
            if (_grid == null) _grid = FindAnyObjectByType<CityGrid>();

            _targetFocus = _focus;
            _targetDistance = _distance;
            _targetYaw = _yaw;
            ApplyPose(1f);
        }

        void Update()
        {
            HandleZoom();
            HandleYaw();
            HandleDragPan();
            HandleKeyPan();
            ClampFocus();
            ApplyPose(1f - Mathf.Exp(-_smoothing * Time.unscaledDeltaTime));
        }

        void HandleZoom()
        {
            Mouse mouse = Mouse.current;
            if (mouse == null) return;

            float scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) < 0.01f) return;

            // Scale the step by current distance so zooming feels even at every altitude.
            float step = Mathf.Sign(scroll) * _zoomSpeed * (_targetDistance / _maxDistance + 0.15f);
            _targetDistance = Mathf.Clamp(_targetDistance - step, _minDistance, _maxDistance);
        }

        void HandleYaw()
        {
            if (!_allowYaw) return;

            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return;

            float dir = 0f;
            if (keyboard.qKey.isPressed) dir -= 1f;
            if (keyboard.eKey.isPressed) dir += 1f;
            if (Mathf.Abs(dir) > 0.01f) _targetYaw += dir * _yawSpeed * Time.unscaledDeltaTime;
        }

        /// <summary>
        /// Middle-drag keeps the world point grabbed at press time under the cursor, so the ground
        /// tracks the hand exactly instead of moving at some arbitrary rate.
        /// </summary>
        void HandleDragPan()
        {
            Mouse mouse = Mouse.current;
            if (mouse == null || _grid == null) return;

            if (mouse.middleButton.wasPressedThisFrame)
            {
                if (_grid.TryScreenToWorld(_camera, mouse.position.ReadValue(), out Vector3 hit))
                {
                    _panning = true;
                    _panGrabPoint = hit;
                }
            }
            else if (mouse.middleButton.wasReleasedThisFrame)
            {
                _panning = false;
            }

            if (!_panning) return;

            if (_grid.TryScreenToWorld(_camera, mouse.position.ReadValue(), out Vector3 current))
            {
                Vector3 delta = _panGrabPoint - current;
                delta.y = 0f;
                _targetFocus += delta;

                // Snap the focus immediately: a smoothed grab-pan feels like the ground is sliding.
                _focus = _targetFocus;
            }
        }

        void HandleKeyPan()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return;

            float x = 0f;
            float z = 0f;
            if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) x -= 1f;
            if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) x += 1f;
            if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) z -= 1f;
            if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) z += 1f;

            if (Mathf.Abs(x) < 0.01f && Mathf.Abs(z) < 0.01f) return;

            // Flattened camera basis, so panning stays screen-relative once yaw is unlocked.
            Quaternion heading = Quaternion.Euler(0f, _targetYaw, 0f);
            Vector3 move = heading * new Vector3(x, 0f, z);
            if (move.sqrMagnitude > 1f) move.Normalize();

            // Pan faster when zoomed out, so crossing the map takes about the same time at any zoom.
            float speed = _panSpeed * Mathf.Max(0.35f, _targetDistance / 26f);
            _targetFocus += move * (speed * Time.unscaledDeltaTime);
        }

        void ClampFocus()
        {
            if (_grid == null) return;

            float limit = _grid.HalfExtent * _grid.CellSize;
            _targetFocus.x = Mathf.Clamp(_targetFocus.x, -limit, limit);
            _targetFocus.z = Mathf.Clamp(_targetFocus.z, -limit, limit);
            _targetFocus.y = 0f;
        }

        void ApplyPose(float t)
        {
            _focus = Vector3.Lerp(_focus, _targetFocus, t);
            _distance = Mathf.Lerp(_distance, _targetDistance, t);
            _yaw = Mathf.LerpAngle(_yaw, _targetYaw, t);

            Quaternion rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            transform.SetPositionAndRotation(_focus - rotation * Vector3.forward * _distance, rotation);
        }

        void OnValidate()
        {
            _minDistance = Mathf.Max(1f, _minDistance);
            _maxDistance = Mathf.Max(_minDistance + 1f, _maxDistance);
            _distance = Mathf.Clamp(_distance, _minDistance, _maxDistance);
        }
    }
}
