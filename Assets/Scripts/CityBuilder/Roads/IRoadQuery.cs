using CityBuilder.Grid;

namespace CityBuilder.Roads
{
    /// <summary>
    /// Read-only view of a road layout. The committed network and the drag preview both implement
    /// this, so the mesh builder never needs to know which one it is looking at.
    /// </summary>
    public interface IRoadQuery
    {
        bool Has(GridCoord cell);

        /// <summary>Which neighbours this cell is joined to. Drives junction geometry.</summary>
        Dir8 MaskAt(GridCoord cell);
    }
}
