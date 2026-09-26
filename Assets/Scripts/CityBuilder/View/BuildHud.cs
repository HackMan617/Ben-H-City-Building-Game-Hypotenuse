using CityBuilder.BuildTools;
using CityBuilder.Roads;
using UnityEngine;

namespace CityBuilder.View
{
    /// <summary>
    /// Prototype-grade IMGUI readout: the control list, a live cell count, and the build prompt.
    /// Deliberately throwaway -- swap for UI Toolkit once the toolbar has real content.
    /// </summary>
    public class BuildHud : MonoBehaviour
    {
        [SerializeField] RoadSystem _roads;
        [SerializeField] RoadBuildTool _tool;
        [SerializeField] Camera _camera;

        GUIStyle _panel;
        GUIStyle _label;

        void Awake()
        {
            if (_roads == null) _roads = FindAnyObjectByType<RoadSystem>();
            if (_tool == null) _tool = FindAnyObjectByType<RoadBuildTool>();
            if (_camera == null) _camera = Camera.main;
        }

        void OnGUI()
        {
            EnsureStyles();

            const float width = 260f;
            var rect = new Rect(16f, 16f, width, 246f);
            GUI.Box(rect, GUIContent.none, _panel);

            GUILayout.BeginArea(new Rect(rect.x + 14f, rect.y + 12f, width - 28f, rect.height - 24f));

            GUILayout.Label("DRAG TO BUILD", _label);
            GUILayout.Space(6f);
            GUILayout.Label("Left drag      draw road", _label);
            GUILayout.Label("Right drag     erase", _label);
            GUILayout.Label("Enter / Esc    build / cancel", _label);
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

            if (_tool != null && _tool.IsAwaitingConfirm) DrawConfirmPrompt();
        }

        /// <summary>Build / Cancel beside the end of the proposed road.</summary>
        void DrawConfirmPrompt()
        {
            const float width = 180f;
            const float height = 64f;

            // Anchored to the road's last cell, falling back to screen centre if that is off camera.
            Vector2 anchor = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            if (_camera != null)
            {
                Vector3 screen = _camera.WorldToScreenPoint(_tool.PendingEnd);
                if (screen.z > 0f) anchor = new Vector2(screen.x, Screen.height - screen.y);
            }

            float x = Mathf.Clamp(anchor.x + 24f, 8f, Screen.width - width - 8f);
            float y = Mathf.Clamp(anchor.y - height * 0.5f, 8f, Screen.height - height - 8f);
            var rect = new Rect(x, y, width, height);
            GUI.Box(rect, GUIContent.none, _panel);

            GUILayout.BeginArea(new Rect(rect.x + 10f, rect.y + 8f, width - 20f, height - 16f));
            int cells = _tool.PendingCellCount;
            GUILayout.Label(cells == 1 ? "Build 1 cell?" : $"Build {cells} cells?", _label);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Build")) _tool.ConfirmPending();
            if (GUILayout.Button("Cancel")) _tool.CancelPending();
            GUILayout.EndHorizontal();
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
