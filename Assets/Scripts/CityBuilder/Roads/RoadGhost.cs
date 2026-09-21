using System.Collections.Generic;
using CityBuilder.Grid;
using UnityEngine;

namespace CityBuilder.Roads
{
    /// <summary>
    /// The translucent preview of a stroke in progress.
    ///
    /// It renders through a <see cref="RoadOverlay"/> rather than off the raw cell list, so the ghost
    /// shows the junctions the stroke would actually create -- run a preview into an existing road
    /// and you see the T-junction form before you release the button.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class RoadGhost : MonoBehaviour
    {
        [SerializeField] Material _buildMaterial;
        [SerializeField] Material _eraseMaterial;

        [Tooltip("Lifts the preview clear of committed road so the two do not z-fight.")]
        [SerializeField] float _zOffset = 0.004f;

        readonly List<GridCoord> _visible = new List<GridCoord>();
        readonly HashSet<GridCoord> _visibleSet = new HashSet<GridCoord>();

        Mesh _mesh;
        MeshFilter _filter;
        MeshRenderer _renderer;
        RoadOverlay _overlay;

        void Awake()
        {
            _filter = GetComponent<MeshFilter>();
            _renderer = GetComponent<MeshRenderer>();

            _mesh = new Mesh { name = "RoadGhost" };
            _mesh.MarkDynamic();
            _filter.sharedMesh = _mesh;

            transform.position = new Vector3(0f, _zOffset, 0f);
            Hide();
        }

        /// <summary>
        /// Shows a planned stroke against <paramref name="network"/>. In erase mode the removed cells
        /// are highlighted; otherwise the ghost draws every new cell plus any existing cell the
        /// stroke joins, so a junction forming on committed road is visible before release.
        /// </summary>
        public void Show(RoadNetwork network, RoadEdit edit, RoadProfile profile, bool erase)
        {
            if (edit == null || edit.IsEmpty)
            {
                Hide();
                return;
            }

            _visible.Clear();
            _visibleSet.Clear();

            IRoadQuery query;
            if (erase)
            {
                // Erase previews read off the untouched network: removal has not applied yet.
                foreach (GridCoord cell in edit.Removed) AddVisible(cell);
                query = network;
            }
            else
            {
                _overlay ??= new RoadOverlay(network);
                _overlay.Reset();
                _overlay.Apply(edit);

                foreach (GridCoord cell in edit.Added) AddVisible(cell);
                foreach (RoadLink link in edit.Linked)
                {
                    AddVisible(link.Cell);
                    AddVisible(link.Other);
                }
                query = _overlay;
            }

            if (_visible.Count == 0)
            {
                Hide();
                return;
            }

            RoadMeshBuilder.Build(_mesh, query, _visible, profile);

            Material material = erase ? _eraseMaterial : _buildMaterial;
            if (material != null) _renderer.sharedMaterial = material;

            transform.position = new Vector3(0f, _zOffset, 0f);
            _renderer.enabled = true;
        }

        void AddVisible(GridCoord cell)
        {
            if (_visibleSet.Add(cell)) _visible.Add(cell);
        }

        public void Hide()
        {
            if (_renderer != null) _renderer.enabled = false;
        }

        void OnDestroy()
        {
            if (_mesh != null) Destroy(_mesh);
        }
    }
}
