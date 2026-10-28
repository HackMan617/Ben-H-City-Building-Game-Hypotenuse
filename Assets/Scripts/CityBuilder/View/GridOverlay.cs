using CityBuilder.Grid;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CityBuilder.View
{
    /// <summary>
    /// The ground plane and its grid lines. G toggles the lines; the ground itself stays.
    ///
    /// Lines are drawn procedurally in the shader from world position, so there is no line geometry
    /// to build and the grid costs the same whether it covers ten cells or ten thousand.
    /// </summary>
    [RequireComponent(typeof(MeshRenderer))]
    public class GridOverlay : MonoBehaviour
    {
        static readonly int GridStrengthId = Shader.PropertyToID("_GridStrength");
        static readonly int CellSizeId = Shader.PropertyToID("_CellSize");

        [SerializeField] CityGrid _grid;

        [Tooltip("Raised terrain shares the ground's grid material, so it fades its lines with the ground.")]
        [SerializeField] TerrainView _terrain;

        [SerializeField] bool _visible = true;
        [SerializeField] Key _toggleKey = Key.G;
        [SerializeField] float _fadeSpeed = 10f;

        MeshRenderer _renderer;
        MaterialPropertyBlock _block;
        float _strength;

        public bool Visible
        {
            get => _visible;
            set => _visible = value;
        }

        void Awake()
        {
            _renderer = GetComponent<MeshRenderer>();
            _block = new MaterialPropertyBlock();
            if (_grid == null) _grid = FindAnyObjectByType<CityGrid>();
            if (_terrain == null) _terrain = FindAnyObjectByType<TerrainView>();

            _strength = _visible ? 1f : 0f;
        }

        // In Start rather than Awake: the terrain's renderer is only fetched in its own Awake.
        void Start() => Apply();

        void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard[_toggleKey].wasPressedThisFrame) _visible = !_visible;

            float target = _visible ? 1f : 0f;
            if (Mathf.Abs(_strength - target) < 0.001f) return;

            _strength = Mathf.MoveTowards(_strength, target, _fadeSpeed * Time.unscaledDeltaTime);
            Apply();
        }

        void Apply()
        {
            _renderer.GetPropertyBlock(_block);
            _block.SetFloat(GridStrengthId, _strength);
            if (_grid != null) _block.SetFloat(CellSizeId, _grid.CellSize);
            _renderer.SetPropertyBlock(_block);

            // Submesh 0 only: the cliff material has no grid to fade.
            if (_terrain != null && _terrain.Renderer != null) _terrain.Renderer.SetPropertyBlock(_block, 0);
        }
    }
}
