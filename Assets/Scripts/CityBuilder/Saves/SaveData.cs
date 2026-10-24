using System.Collections.Generic;
using CityBuilder.Grid;

namespace CityBuilder.Saves
{
    /// <summary>
    /// A saved city, as written to its JSON file by <see cref="CitySave"/>. Only what the player made
    /// is kept -- road cells, the links between them, buildings and the road type -- never geometry,
    /// which is rebuilt on load.
    ///
    /// Links are kept the way <see cref="Roads.RoadLink"/> stores them, always pointing N, NE, E or
    /// SE, so each appears once. Buildings name their model by id, so a save outlives changes to
    /// what the model looks like.
    /// </summary>
    public sealed class SaveData
    {
        /// <summary>Bumped when the layout changes in a way older files need converting for.</summary>
        public const int CurrentVersion = 1;

        public int Version = CurrentVersion;

        /// <summary>When it was written, UTC, as ISO 8601.</summary>
        public string SavedAt;

        /// <summary>Bundle name of the road type selected when saved; null when there was none.</summary>
        public string RoadType;

        public List<GridCoord> RoadCells = new List<GridCoord>();
        public List<SavedLink> RoadLinks = new List<SavedLink>();
        public List<SavedBuilding> Buildings = new List<SavedBuilding>();
    }

    public sealed class SavedLink
    {
        public int X;
        public int Z;
        public Dir8 Dir;
    }

    public sealed class SavedBuilding
    {
        /// <summary>The model's <see cref="Content.ModelDefinition.Id"/>.</summary>
        public string Id;

        /// <summary>The cell the footprint's anchor sits on.</summary>
        public int X;
        public int Z;

        /// <summary>Quarter turns clockwise seen from above.</summary>
        public int Rotation;
    }
}
