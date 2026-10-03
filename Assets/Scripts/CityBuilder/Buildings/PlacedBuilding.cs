using System.Collections.Generic;
using CityBuilder.Content;
using CityBuilder.Grid;

namespace CityBuilder.Buildings
{
    /// <summary>
    /// A building standing in the city: which model, which cell its footprint anchor sits on, and how
    /// many quarter turns clockwise it was rotated. The cells it covers are worked out once, up front.
    /// </summary>
    public sealed class PlacedBuilding
    {
        public ModelDefinition Definition { get; }
        public GridCoord Anchor { get; }
        public int Rotation { get; }

        public IReadOnlyList<GridCoord> Cells { get; }

        /// <summary>Corners of the box around <see cref="Cells"/>, so distance checks can skip far buildings.</summary>
        public GridCoord Min { get; }
        public GridCoord Max { get; }

        public PlacedBuilding(ModelDefinition definition, GridCoord anchor, int rotation)
        {
            Definition = definition;
            Anchor = anchor;
            Rotation = BuildingPlacement.NormalizeRotation(rotation);

            List<GridCoord> cells = BuildingPlacement.CellsAt(definition.Footprint, anchor, Rotation);
            Cells = cells;
            BuildingPlacement.Bounds(cells, out GridCoord min, out GridCoord max);
            Min = min;
            Max = max;
        }
    }
}
