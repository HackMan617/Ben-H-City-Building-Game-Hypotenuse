using System.Collections.Generic;
using CityBuilder.Buildings;
using CityBuilder.BuildTools;
using CityBuilder.Content;
using CityBuilder.Roads;
using CityBuilder.Saves;
using UnityEngine;

namespace CityBuilder.View
{
    public class BuildHud : MonoBehaviour
    {
        [SerializeField] RoadSystem _roads;
        [SerializeField] RoadBuildTool _tool;
        [SerializeField] DemolishTool _demolish;
        [SerializeField] TerrainTool _terrain;
        [SerializeField] BuildingBuildTool _buildingTool;
        [SerializeField] Camera _camera;
        [SerializeField] BuildMenu _menu;
        [SerializeField] SaveGameSystem _saves;
        [SerializeField] CameraRig _cameraRig;

        [Tooltip("Show the start screen when the game starts.")]
        [SerializeField] bool _showStartScreen = true;

        List<ModelDefinition> _palette;

        bool _startOpen;
        bool _startFromGame;
        Vector2 _startScroll;
        GUIStyle _title;

        bool _savesOpen;
        string _saveName = "My City";
        string _pendingLoad;
        Vector2 _saveScroll;
        List<SaveInfo> _saveList = new List<SaveInfo>();

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
            if (_terrain == null) _terrain = FindAnyObjectByType<TerrainTool>();
            if (_buildingTool == null) _buildingTool = FindAnyObjectByType<BuildingBuildTool>();
            if (_camera == null) _camera = Camera.main;
            if (_menu == null) _menu = FindAnyObjectByType<BuildMenu>();
            if (_menu == null)
            {
                _menu = gameObject.AddComponent<BuildMenu>();
            }

            if (_saves == null) _saves = FindAnyObjectByType<SaveGameSystem>();
            if (_saves == null) _saves = gameObject.AddComponent<SaveGameSystem>();
            _saves.Changed += RefreshSaveList;

            if (_cameraRig == null) _cameraRig = FindAnyObjectByType<CameraRig>();

            _palette = BuildingPalette.Create();

            if (_showStartScreen) OpenStartScreen(false);
        }

        void OnDestroy()
        {
            if (_saves != null) _saves.Changed -= RefreshSaveList;
        }

        void RefreshSaveList() => _saveList = _saves.ListSaves();

        void OnGUI()
        {
            EnsureStyles();
            if (_startOpen)
            {
                DrawStartScreen();
                return;
            }

            DrawBuildButton();
            DrawSavesButton();
            DrawMenuButton();
            if (_savesOpen) DrawSavesPanel();

            if (_menu.IsOpen) DrawCategoryMenu();
            if (_menu.Active == BuildCategory.Roads) DrawRoadTypePalette();
            if (_menu.Active == BuildCategory.Buildings) DrawBuildingPalette();
            if (_menu.Active != BuildCategory.None) DrawToolStatus();
            if (_tool != null && _tool.IsAwaitingConfirm) DrawRoadConfirmPrompt();
            if (_demolish != null && _demolish.IsAwaitingConfirm) DrawDemolishConfirmPrompt();
            if (_terrain != null && _terrain.IsAwaitingConfirm) DrawTerrainConfirmPrompt();
            if (_buildingTool != null && _buildingTool.IsAwaitingConfirm) DrawBuildingConfirmPrompt();
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

        void DrawSavesButton()
        {
            const float w = 90f;
            if (GUI.Button(new Rect(Screen.width - w - 16f, 16f, w, 32f), _savesOpen ? "Close" : "Saves",
                           _savesOpen ? _categoryButtonActive : _categoryButton))
            {
                _savesOpen = !_savesOpen;
                _pendingLoad = null;
                if (_savesOpen) RefreshSaveList();
            }
        }

        void DrawMenuButton()
        {
            const float w = 80f;
            if (GUI.Button(new Rect(Screen.width - 90f - 16f - 8f - w, 16f, w, 32f), "Menu", _categoryButton))
                OpenStartScreen(true);
        }

        /// <summary>
        /// Covers the city and stops the tools and camera. <paramref name="fromGame"/> is true when the
        /// player opened it mid-game, so there is a city to go back to and loading over it asks first.
        /// </summary>
        void OpenStartScreen(bool fromGame)
        {
            _startOpen = true;
            _startFromGame = fromGame;
            _savesOpen = false;
            _pendingLoad = null;

            _tool?.CancelPending();
            _demolish?.CancelPending();
            _terrain?.CancelPending();
            _buildingTool?.Deselect();
            _menu.Close();
            if (_cameraRig != null) _cameraRig.enabled = false;

            RefreshSaveList();
        }

        void CloseStartScreen()
        {
            _startOpen = false;
            _pendingLoad = null;
            if (_cameraRig != null) _cameraRig.enabled = true;
        }

        void LoadFromStart(string name)
        {
            if (!_saves.Load(name)) return;   // stays open, showing why
            _saveName = name;
            CloseStartScreen();
        }

        /// <summary>The title screen: start a new city, or carry on with a saved one.</summary>
        void DrawStartScreen()
        {
            GUI.Box(new Rect(0f, 0f, Screen.width, Screen.height), GUIContent.none, _panel);

            const float width = 380f;
            const float height = 460f;
            var rect = new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f, width, height);
            GUI.Box(rect, GUIContent.none, _panel);

            GUILayout.BeginArea(new Rect(rect.x + 20f, rect.y + 18f, width - 40f, height - 36f));
            GUILayout.Label("HYPOTENUSE", _title);
            GUILayout.Label("City Builder", _label);
            GUILayout.Space(16f);

            if (_startFromGame && GUILayout.Button("Continue", _buildButton, GUILayout.Height(40f)))
                CloseStartScreen();

            if (GUILayout.Button("New City", _startFromGame ? _categoryButton : _buildButton, GUILayout.Height(40f)))
            {
                if (_startFromGame) _pendingLoad = "";   // empty name: asking about a new city
                else StartNewCity();
            }

            GUILayout.Space(14f);
            GUILayout.Label("SAVED CITIES", _label);
            if (!string.IsNullOrEmpty(_saves.LastMessage) && _saves.LastMessage.StartsWith("Could not"))
                GUILayout.Label(_saves.LastMessage, _label);

            if (_pendingLoad != null)
            {
                // Mid-game, the city on screen would be thrown away, so it takes a second click.
                string what = _pendingLoad.Length == 0 ? "Start a new city?" : $"Load '{_pendingLoad}'?";
                GUILayout.Label($"{what} Unsaved changes are lost.", _label);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(_pendingLoad.Length == 0 ? "New City" : "Load", _categoryButtonActive, GUILayout.Height(30f)))
                {
                    if (_pendingLoad.Length == 0) StartNewCity();
                    else LoadFromStart(_pendingLoad);
                }
                if (GUILayout.Button("Cancel", _categoryButton, GUILayout.Height(30f))) _pendingLoad = null;
                GUILayout.EndHorizontal();
            }
            else if (_saveList.Count == 0)
            {
                GUILayout.Label("No saved cities yet. Save one from the Saves panel in game.", _label);
            }
            else
            {
                _startScroll = GUILayout.BeginScrollView(_startScroll);
                foreach (SaveInfo save in _saveList)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label($"{save.Name}\n{save.WrittenUtc.ToLocalTime():g}", _label);
                    if (GUILayout.Button("Load", _categoryButton, GUILayout.Width(64f), GUILayout.Height(32f)))
                    {
                        if (_startFromGame) _pendingLoad = save.Name;
                        else LoadFromStart(save.Name);
                    }
                    GUILayout.EndHorizontal();
                    GUILayout.Space(4f);
                }
                GUILayout.EndScrollView();
            }

            GUILayout.EndArea();
        }

        void StartNewCity()
        {
            _saves.NewCity();
            _saveName = "My City";
            CloseStartScreen();
        }

        /// <summary>Name and save the city, or pick a save to load. F5 / F9 quick-save and quick-load.</summary>
        void DrawSavesPanel()
        {
            const float width = 280f;
            const float height = 320f;
            var rect = new Rect(Screen.width - width - 16f, 56f, width, height);
            GUI.Box(rect, GUIContent.none, _panel);

            GUILayout.BeginArea(new Rect(rect.x + 12f, rect.y + 10f, width - 24f, height - 20f));
            GUILayout.Label("SAVES", _label);

            GUILayout.BeginHorizontal();
            _saveName = GUILayout.TextField(_saveName, 40);
            if (GUILayout.Button("Save", GUILayout.Width(60f)))
            {
                _pendingLoad = null;
                _saves.Save(_saveName);
            }
            GUILayout.EndHorizontal();

            GUILayout.Label("F5 quick-save    F9 quick-load", _label);
            if (!string.IsNullOrEmpty(_saves.LastMessage)) GUILayout.Label(_saves.LastMessage, _label);
            GUILayout.Space(4f);

            if (_pendingLoad != null)
            {
                // Loading throws the current city away, so it takes a second click.
                GUILayout.Label($"Load '{_pendingLoad}'? Unsaved changes are lost.", _label);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Load"))
                {
                    _tool?.CancelPending();
                    _demolish?.CancelPending();
                    _terrain?.CancelPending();
                    _saves.Load(_pendingLoad);
                    _pendingLoad = null;
                }
                if (GUILayout.Button("Cancel")) _pendingLoad = null;
                GUILayout.EndHorizontal();
            }
            else if (_saveList.Count == 0)
            {
                GUILayout.Label("No saves yet", _label);
            }
            else
            {
                _saveScroll = GUILayout.BeginScrollView(_saveScroll);
                foreach (SaveInfo save in _saveList)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label($"{save.Name}  ({save.WrittenUtc.ToLocalTime():g})", _label);
                    if (GUILayout.Button("Load", GUILayout.Width(56f))) _pendingLoad = save.Name;
                    GUILayout.EndHorizontal();
                }
                GUILayout.EndScrollView();
            }

            GUILayout.EndArea();
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
            var rect = new Rect(16f, 16f, width, 120f);
            GUI.Box(rect, GUIContent.none, _panel);

            GUILayout.BeginArea(new Rect(rect.x + 14f, rect.y + 12f, width - 28f, rect.height - 24f));
            GUILayout.Label(_menu.Active.ToString().ToUpper(), _label);
            GUILayout.Space(4f);

            switch (_menu.Active)
            {
                case BuildCategory.Roads:
                    int committed = _roads != null && _roads.Network != null ? _roads.Network.Count : 0;
                    int pending = _tool != null ? _tool.PendingCellCount : 0;
                    if (_roads != null && _roads.SelectedRoadType != null)
                        GUILayout.Label($"Road: {_roads.SelectedRoadType.DisplayName}", _label);
                    GUILayout.Label("Left drag: draw    Right drag: erase", _label);
                    GUILayout.Label(pending > 0 ? $"{committed} cells  (+{pending} pending)" : $"{committed} cells", _label);
                    if (_tool != null && _tool.IsDragging && _tool.IsBlocked)
                        GUILayout.Label(_tool.IsOffLevel ? "Blocked: slopes climb 1 level, straight" : "Blocked: overlaps a road", _label);
                    break;
                case BuildCategory.Buildings:
                    if (_buildingTool != null && _buildingTool.Selected != null)
                    {
                        GUILayout.Label($"Placing: {_buildingTool.Selected.DisplayName}", _label);
                        GUILayout.Label("Click to place  R to rotate  Esc to cancel", _label);
                        if (_buildingTool.LastProblem != PlacementProblem.None)
                            GUILayout.Label($"Blocked: {_buildingTool.LastProblem}", _label);
                    }
                    else
                    {
                        GUILayout.Label("Select a building to place", _label);
                    }
                    break;
                case BuildCategory.Terrain:
                    GUILayout.Label("Left drag: raise    Right drag: lower", _label);
                    if (_terrain != null && _terrain.PendingCellCount > 0)
                    {
                        string verb = _terrain.IsLowering ? "lower" : "raise";
                        GUILayout.Label($"{_terrain.PendingCellCount} cells to {verb}", _label);
                    }
                    if (_terrain != null && _terrain.BlockedCellCount > 0)
                        GUILayout.Label($"{_terrain.BlockedCellCount} cells kept: road or building", _label);
                    break;
                case BuildCategory.Demolish:
                    GUILayout.Label("Left drag: select area to demolish", _label);
                    string demolishPending = _demolish != null ? DemolishSummary() : "";
                    if (demolishPending.Length > 0)
                        GUILayout.Label($"{demolishPending} to demolish", _label);
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
            string label = $"Demolish {DemolishSummary()}?";
            DrawConfirmPopup(_demolish.PendingEnd, label, "Demolish", _demolish.ConfirmPending, _demolish.CancelPending);
        }

        void DrawTerrainConfirmPrompt()
        {
            int cells = _terrain.PendingCellCount;
            string verb = _terrain.IsLowering ? "Lower" : "Raise";
            string label = cells == 1 ? $"{verb} 1 cell?" : $"{verb} {cells} cells?";
            DrawConfirmPopup(_terrain.PendingEnd, label, verb, _terrain.ConfirmPending, _terrain.CancelPending);
        }

        // "3 cells", "1 building", or "3 cells, 1 building"; empty when nothing is marked.
        string DemolishSummary()
        {
            int cells = _demolish.PendingCellCount;
            int buildings = _demolish.PendingBuildingCount;
            string cellText = cells == 1 ? "1 cell" : $"{cells} cells";
            string buildingText = buildings == 1 ? "1 building" : $"{buildings} buildings";
            if (cells > 0 && buildings > 0) return $"{cellText}, {buildingText}";
            if (buildings > 0) return buildingText;
            return cells > 0 ? cellText : "";
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

        void DrawRoadTypePalette()
        {
            if (_roads == null || _roads.RoadTypes.Count == 0) return;

            IReadOnlyList<BundleInfo> types = _roads.RoadTypes;
            int clicked = DrawPaletteRow(types.Count,
                                         i => types[i].DisplayName,
                                         i => types[i] == _roads.SelectedRoadType);
            if (clicked >= 0) _roads.SelectRoadType(types[clicked]);
        }

        void DrawBuildingPalette()
        {
            if (_palette == null || _palette.Count == 0 || _buildingTool == null) return;

            int clicked = DrawPaletteRow(_palette.Count,
                                         i => _palette[i].DisplayName ?? _palette[i].Id,
                                         i => _buildingTool.Selected == _palette[i]);
            if (clicked < 0) return;

            ModelDefinition def = _palette[clicked];
            if (_buildingTool.Selected == def) _buildingTool.Deselect();
            else _buildingTool.Select(def);
        }

        /// <summary>A row of choices above the category menu. Returns the index clicked this frame, or -1.</summary>
        int DrawPaletteRow(int count, System.Func<int, string> label, System.Func<int, bool> isActive)
        {
            float btnW = 110f;
            float btnH = 32f;
            float spacing = 6f;
            float totalW = count * btnW + (count - 1) * spacing;
            float startX = Screen.width * 0.5f - totalW * 0.5f;
            float y = Screen.height - 40f - 16f - 36f - 12f - btnH - 12f;

            float panelPad = 8f;
            GUI.Box(new Rect(startX - panelPad, y - panelPad, totalW + panelPad * 2, btnH + panelPad * 2),
                    GUIContent.none, _panel);

            int clicked = -1;
            for (int i = 0; i < count; i++)
            {
                float x = startX + i * (btnW + spacing);
                GUIStyle style = isActive(i) ? _categoryButtonActive : _categoryButton;
                if (GUI.Button(new Rect(x, y, btnW, btnH), label(i), style)) clicked = i;
            }
            return clicked;
        }

        void DrawBuildingConfirmPrompt()
        {
            string label = $"Place {_buildingTool.Selected?.DisplayName ?? "building"}?";
            DrawConfirmPopup(_buildingTool.PendingEnd, label, "Place",
                             _buildingTool.ConfirmPending, _buildingTool.CancelPending);
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

            _title = new GUIStyle(_label) { fontSize = 30, fontStyle = FontStyle.Bold };
            _title.normal.textColor = Color.white;
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
