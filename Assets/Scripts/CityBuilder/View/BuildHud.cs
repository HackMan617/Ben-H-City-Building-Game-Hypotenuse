using CityBuilder.BuildTools;
using CityBuilder.Roads;
using UnityEngine;

namespace CityBuilder.View
{
    public class BuildHud : MonoBehaviour
    {
        [SerializeField] RoadSystem _roads;
        [SerializeField] RoadBuildTool _tool;
        [SerializeField] DemolishTool _demolish;
        [SerializeField] Camera _camera;
        [SerializeField] BuildMenu _menu;

        GUIStyle _panel;
        GUIStyle _label;
        GUIStyle _categoryButton;
        GUIStyle _categoryButtonActive;
        GUIStyle _buildButton;

        void Awake()
        {
            if (_roads == null) _roads = FindAnyObjectByType<RoadSystem>();
            if (_tool == null) _tool = FindAnyObjectByType<RoadBuildTool>();
            if (_demolish == null) _demolish = FindAnyObjectByType<DemolishTool>();
            if (_camera == null) _camera = Camera.main;
            if (_menu == null) _menu = FindAnyObjectByType<BuildMenu>();
            if (_menu == null)
            {
                _menu = gameObject.AddComponent<BuildMenu>();
            }
        }

        void OnGUI()
        {
            EnsureStyles();
            DrawBuildButton();

            if (_menu.IsOpen) DrawCategoryMenu();
            if (_menu.Active != BuildCategory.None) DrawToolStatus();
            if (_tool != null && _tool.IsAwaitingConfirm) DrawRoadConfirmPrompt();
            if (_demolish != null && _demolish.IsAwaitingConfirm) DrawDemolishConfirmPrompt();
        }

        void DrawBuildButton()
        {
            float w = 100f;
            float h = 40f;
            float x = Screen.width * 0.5f - w * 0.5f;
            float y = Screen.height - h - 16f;

            string label = _menu.IsOpen ? "Close" : "Build";
            if (GUI.Button(new Rect(x, y, w, h), label, _buildButton))
            {
                _menu.Toggle();
            }
        }

        void DrawCategoryMenu()
        {
            float btnW = 120f;
            float btnH = 36f;
            float spacing = 8f;
            var categories = new[] { BuildCategory.Roads, BuildCategory.Buildings, BuildCategory.Terrain, BuildCategory.Demolish };
            float totalW = categories.Length * btnW + (categories.Length - 1) * spacing;
            float startX = Screen.width * 0.5f - totalW * 0.5f;
            float y = Screen.height - 40f - 16f - btnH - 12f;

            float panelPad = 10f;
            GUI.Box(new Rect(startX - panelPad, y - panelPad, totalW + panelPad * 2, btnH + panelPad * 2), GUIContent.none, _panel);

            for (int i = 0; i < categories.Length; i++)
            {
                float x = startX + i * (btnW + spacing);
                bool active = _menu.Active == categories[i];
                GUIStyle style = active ? _categoryButtonActive : _categoryButton;
                if (GUI.Button(new Rect(x, y, btnW, btnH), categories[i].ToString(), style))
                {
                    if (active) _menu.Select(BuildCategory.None);
                    else _menu.Select(categories[i]);
                }
            }
        }

        void DrawToolStatus()
        {
            const float width = 240f;
            var rect = new Rect(16f, 16f, width, 100f);
            GUI.Box(rect, GUIContent.none, _panel);

            GUILayout.BeginArea(new Rect(rect.x + 14f, rect.y + 12f, width - 28f, rect.height - 24f));
            GUILayout.Label(_menu.Active.ToString().ToUpper(), _label);
            GUILayout.Space(4f);

            switch (_menu.Active)
            {
                case BuildCategory.Roads:
                    int committed = _roads != null && _roads.Network != null ? _roads.Network.Count : 0;
                    int pending = _tool != null ? _tool.PendingCellCount : 0;
                    GUILayout.Label("Left drag: draw    Right drag: erase", _label);
                    GUILayout.Label(pending > 0 ? $"{committed} cells  (+{pending} pending)" : $"{committed} cells", _label);
                    if (_tool != null && _tool.IsDragging && _tool.IsBlocked)
                        GUILayout.Label("Blocked: overlaps a road", _label);
                    break;
                case BuildCategory.Buildings:
                    GUILayout.Label("Select a building to place", _label);
                    break;
                case BuildCategory.Terrain:
                    GUILayout.Label("Terrain tools coming soon", _label);
                    break;
                case BuildCategory.Demolish:
                    GUILayout.Label("Left drag: select area to demolish", _label);
                    int demolishPending = _demolish != null ? _demolish.PendingCellCount : 0;
                    if (demolishPending > 0)
                        GUILayout.Label($"{demolishPending} cells to demolish", _label);
                    break;
            }

            GUILayout.EndArea();
        }

        void DrawRoadConfirmPrompt()
        {
            int cells = _tool.PendingCellCount;
            string verb = _tool.IsErasing ? "Erase" : "Build";
            string label = cells == 1 ? $"{verb} 1 cell?" : $"{verb} {cells} cells?";
            DrawConfirmPopup(_tool.PendingEnd, label, verb, _tool.ConfirmPending, _tool.CancelPending);
        }

        void DrawDemolishConfirmPrompt()
        {
            int cells = _demolish.PendingCellCount;
            string label = cells == 1 ? "Demolish 1 cell?" : $"Demolish {cells} cells?";
            DrawConfirmPopup(_demolish.PendingEnd, label, "Demolish", _demolish.ConfirmPending, _demolish.CancelPending);
        }

        void DrawConfirmPopup(Vector3 worldAnchor, string label, string verb,
                              System.Action onConfirm, System.Action onCancel)
        {
            const float width = 180f;
            const float height = 64f;

            Vector2 anchor = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            if (_camera != null)
            {
                Vector3 screen = _camera.WorldToScreenPoint(worldAnchor);
                if (screen.z > 0f) anchor = new Vector2(screen.x, Screen.height - screen.y);
            }

            float x = Mathf.Clamp(anchor.x + 24f, 8f, Screen.width - width - 8f);
            float y = Mathf.Clamp(anchor.y - height * 0.5f, 8f, Screen.height - height - 8f);
            var rect = new Rect(x, y, width, height);
            GUI.Box(rect, GUIContent.none, _panel);

            GUILayout.BeginArea(new Rect(rect.x + 10f, rect.y + 8f, width - 20f, height - 16f));
            GUILayout.Label(label, _label);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(verb)) onConfirm();
            if (GUILayout.Button("Cancel")) onCancel();
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        void EnsureStyles()
        {
            if (_panel != null) return;

            const int radius = 8;

            _panel = new GUIStyle(GUI.skin.box);
            _panel.normal.background = RoundedRect(64, 64, new Color(0.05f, 0.06f, 0.07f, 0.82f), radius);
            _panel.border = new RectOffset(radius, radius, radius, radius);

            _label = new GUIStyle(GUI.skin.label) { fontSize = 12, richText = false };
            _label.normal.textColor = new Color(0.85f, 0.88f, 0.9f);

            const int btnR = 6;
            var btnBg = RoundedRect(48, 48, new Color(0.15f, 0.16f, 0.18f, 0.9f), btnR);
            var btnHover = RoundedRect(48, 48, new Color(0.25f, 0.27f, 0.30f, 0.95f), btnR);
            var btnActive = RoundedRect(48, 48, new Color(0.20f, 0.45f, 0.70f, 0.95f), btnR);

            _categoryButton = new GUIStyle(GUI.skin.button) { fontSize = 13 };
            _categoryButton.normal.background = btnBg;
            _categoryButton.normal.textColor = new Color(0.85f, 0.88f, 0.9f);
            _categoryButton.hover.background = btnHover;
            _categoryButton.hover.textColor = Color.white;
            _categoryButton.active.background = btnActive;
            _categoryButton.border = new RectOffset(btnR, btnR, btnR, btnR);

            _categoryButtonActive = new GUIStyle(_categoryButton);
            _categoryButtonActive.normal.background = btnActive;
            _categoryButtonActive.normal.textColor = Color.white;
            _categoryButtonActive.hover.background = btnActive;

            const int buildR = 8;
            var buildBg = RoundedRect(48, 48, new Color(0.20f, 0.50f, 0.30f, 0.95f), buildR);
            var buildHover = RoundedRect(48, 48, new Color(0.25f, 0.60f, 0.35f, 0.95f), buildR);

            _buildButton = new GUIStyle(GUI.skin.button) { fontSize = 16, fontStyle = FontStyle.Bold };
            _buildButton.normal.background = buildBg;
            _buildButton.normal.textColor = Color.white;
            _buildButton.hover.background = buildHover;
            _buildButton.hover.textColor = Color.white;
            _buildButton.active.background = btnActive;
            _buildButton.border = new RectOffset(buildR, buildR, buildR, buildR);
        }

        static Texture2D RoundedRect(int w, int h, Color fill, int r)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            var clear = new Color(0, 0, 0, 0);

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float dx = 0f, dy = 0f;
                    if (x < r) dx = r - x - 0.5f;
                    else if (x >= w - r) dx = x - (w - r) + 0.5f;
                    if (y < r) dy = r - y - 0.5f;
                    else if (y >= h - r) dy = y - (h - r) + 0.5f;

                    if (dx > 0f && dy > 0f)
                    {
                        float dist = Mathf.Sqrt(dx * dx + dy * dy);
                        if (dist > r)
                            tex.SetPixel(x, y, clear);
                        else if (dist > r - 1f)
                            tex.SetPixel(x, y, new Color(fill.r, fill.g, fill.b, fill.a * (r - dist)));
                        else
                            tex.SetPixel(x, y, fill);
                    }
                    else
                    {
                        tex.SetPixel(x, y, fill);
                    }
                }
            }

            tex.Apply();
            return tex;
        }
    }
}
