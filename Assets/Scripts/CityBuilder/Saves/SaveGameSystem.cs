using System;
using System.Collections.Generic;
using CityBuilder.Buildings;
using CityBuilder.BuildTools;
using CityBuilder.Content;
using CityBuilder.Roads;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CityBuilder.Saves
{
    /// <summary>
    /// Saves the city to the saves folder (<see cref="SaveFiles"/>) and loads it back. F5 quick-saves
    /// and F9 quick-loads; the HUD's Saves panel does named saves.
    ///
    /// Loading replaces the whole city and clears undo, since the history belongs to the old one.
    /// </summary>
    public class SaveGameSystem : MonoBehaviour
    {
        public const string QuickSaveName = "quicksave";

        [SerializeField] RoadSystem _roads;
        [SerializeField] BuildingBuildTool _buildings;

        readonly List<string> _problems = new List<string>();
        List<ModelDefinition> _models;

        /// <summary>What the last save or load did, for the HUD; null before the first.</summary>
        public string LastMessage { get; private set; }

        /// <summary>Raised after a save or load, so UI can refresh its list.</summary>
        public event Action Changed;

        void Awake()
        {
            if (_roads == null) _roads = FindAnyObjectByType<RoadSystem>();
            if (_buildings == null) _buildings = FindAnyObjectByType<BuildingBuildTool>();
            _models = BuildingPalette.Create();
        }

        void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return;

            if (keyboard.f5Key.wasPressedThisFrame) Save(QuickSaveName);
            else if (keyboard.f9Key.wasPressedThisFrame) Load(QuickSaveName);
        }

        public List<SaveInfo> ListSaves() => SaveFiles.List();

        public bool Save(string name)
        {
            if (_roads == null || _roads.Network == null) return Report(false, "Nothing to save: no road system.");

            IEnumerable<PlacedBuilding> buildings = _buildings != null ? _buildings.Placed : Array.Empty<PlacedBuilding>();
            SaveData data = CitySave.Capture(_roads.Network, buildings, _roads.SelectedRoadType?.Name,
                                             _roads.Grid != null ? _roads.Grid.Terrain : null);

            try
            {
                string path = SaveFiles.Write(data, name);
                Debug.Log($"Saved the city to {path}");
            }
            catch (Exception e) when (e is System.IO.IOException || e is UnauthorizedAccessException)
            {
                Debug.LogError($"Could not save '{name}': {e.Message}");
                return Report(false, $"Could not save: {e.Message}");
            }
            return Report(true, $"Saved '{SaveFiles.CleanName(name)}'");
        }

        public bool Load(string name)
        {
            if (_roads == null || _roads.Network == null) return Report(false, "Cannot load: no road system.");

            SaveData data = SaveFiles.Read(name, out string error);
            if (data == null)
            {
                Debug.LogWarning($"Could not load '{name}': {error}");
                return Report(false, $"Could not load '{name}': {error}");
            }

            _problems.Clear();

            // Terrain first: the roads are lifted onto it as they rebuild.
            if (_roads.Grid != null) CitySave.RestoreTerrain(data, _roads.Grid.Terrain);
            _roads.Replace(network => CitySave.RestoreRoads(data, network, _problems));
            _buildings?.ReplacePlaced(CitySave.RestoreBuildings(data, _models, _problems));

            if (!string.IsNullOrEmpty(data.RoadType))
            {
                foreach (BundleInfo type in _roads.RoadTypes)
                {
                    if (type.Name == data.RoadType) _roads.SelectRoadType(type);
                }
            }

            foreach (string problem in _problems) Debug.LogWarning($"Loading '{name}': {problem}");

            string message = $"Loaded '{SaveFiles.CleanName(name)}'";
            if (_problems.Count > 0) message += $" ({_problems.Count} items dropped, see console)";
            return Report(true, message);
        }

        /// <summary>Clears the city back to empty, flat ground: no roads, buildings or raised terrain.</summary>
        public void NewCity()
        {
            if (_roads == null) return;

            _roads.Grid?.Terrain.Clear();
            _roads.ClearAll();
            _buildings?.ReplacePlaced(Array.Empty<PlacedBuilding>());
            Report(true, "Started a new city");
        }

        bool Report(bool ok, string message)
        {
            LastMessage = message;
            Changed?.Invoke();
            return ok;
        }
    }
}
