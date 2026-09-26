using CityBuilder.BuildTools;
using CityBuilder.Roads;
using UnityEngine;

namespace CityBuilder.View
{
    /// <summary>
    /// Prototype-grade IMGUI readout: the control list and a live cell count.
    /// Deliberately throwaway -- swap for UI Toolkit once the toolbar has real content.
    /// </summary>
    public class BuildHud : MonoBehaviour
    {
        [SerializeField] RoadSystem _roads;
        [SerializeField] RoadBuildTool _tool;

        GUIStyle _panel;
        GUIStyle _label;

        void Awake()
        {
            if (_roads == null) _roads = FindAnyObjectByType<RoadSystem>();
            if (_tool == null) _tool = FindAnyObjectByType<RoadBuildTool>();
        }

        void OnGUI()
        {
            EnsureStyles();

            const float width = 260f;
            var rect = new Rect(16f, 16f, width, 228f);
            GUI.Box(rect, GUIContent.none, _panel);

            GUILayout.BeginArea(new Rect(rect.x + 14f, rect.y + 12f, width - 28f, rect.height - 24f));

            GUILayout.Label("DRAG TO BUILD", _label);
            GUILayout.Space(6f);
            GUILayout.Label("Left drag      draw road", _label);
            GUILayout.Label("Right drag     erase", _label);
            GUILayout.Label("Middle drag    pan", _label);
            GUILayout.Label("Scroll         zoom", _label);
            GUILayout.Label("Ctrl+Z / Y     undo / redo", _label);
            GUILayout.Label("G              grid lines", _label);

            GUILayout.Space(8f);
            int committed = _roads != null && _roads.Network != null ? _roads.Network.Count : 0;
            int pending = _tool != null ? _tool.PendingCellCount : 0;
            GUILayout.Label(pending > 0 ? $"{committed} cells  (+{pending} pending)" : $"{committed} cells", _label);
            if (_tool != null && _tool.IsDragging && _tool.IsBlocked)
                GUILayout.Label("Blocked: overlaps a road it doesn't join", _label);

            GUILayout.EndArea();
        }

        void EnsureStyles()
        {
            if (_panel != null) return;

            var background = new Texture2D(1, 1);
            background.SetPixel(0, 0, new Color(0.05f, 0.06f, 0.07f, 0.82f));
            background.Apply();

            _panel = new GUIStyle(GUI.skin.box);
            _panel.normal.background = background;

            _label = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                richText = false,
            };
            _label.normal.textColor = new Color(0.85f, 0.88f, 0.9f);
        }
    }
}
