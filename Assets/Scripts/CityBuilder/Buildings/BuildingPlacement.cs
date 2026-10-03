using System;
using System.Collections.Generic;
using CityBuilder.Content;
using CityBuilder.Grid;
using CityBuilder.Roads;

namespace CityBuilder.Buildings
{
    /// <summary>Why a building cannot go where the player is pointing. Several can apply at once.</summary>
    [Flags]
    public enum PlacementProblem
    {
        None = 0,

        /// <summary>Part of the footprint is off the buildable area.</summary>
        OutOfBounds = 1 << 0,

        /// <summary>Part of the footprint is on a road.</summary>
        OnRoad = 1 << 1,

        /// <summary>Part of the footprint is on another building.</summary>
        Overlaps = 1 << 2,

        /// <summary>The model needs a road and none is in reach.</summary>
        NoRoadAccess = 1 << 3,

        /// <summary>Inside the clearance of this building or a neighbour.</summary>
        TooClose = 1 << 4,

        /// <summary>Breaks a keepAwayFrom rule -- this building's or a neighbour's.</summary>
        KeptAway = 1 << 5,

        /// <summary>The city already has as many as maxCount allows.</summary>
        LimitReached = 1 << 6,
    }

    /// <summary>
    /// Checks a model's footprint and placement rules (see <see cref="ModelDefinition"/>) against the
    /// city. Pure: it reads the roads and buildings it is given and changes nothing, so the build tool
    /// can call it every frame to colour its preview.
    ///
    /// A footprint is laid out with its anchor cell on the target cell and then turned clockwise, seen
    /// from above, in quarter turns. Distances are counted like a king moves in chess, so a building
    /// diagonally next to another is 1 cell away.
    /// </summary>
    public static class BuildingPlacement
    {
        public static int NormalizeRotation(int quarterTurns) => ((quarterTurns % 4) + 4) % 4;

        /// <summary>Turns an offset clockwise, seen from above: north becomes east.</summary>
        public static GridCoord Rotate(GridCoord offset, int quarterTurns)
        {
            for (int i = NormalizeRotation(quarterTurns); i > 0; i--) offset = new GridCoord(offset.Z, -offset.X);
            return offset;
        }

        /// <summary>The cells <paramref name="footprint"/> covers with its anchor on <paramref name="at"/>.</summary>
        public static List<GridCoord> CellsAt(Footprint footprint, GridCoord at, int rotation)
        {
            var cells = new List<GridCoord>();
            for (int z = 0; z < footprint.Depth; z++)
            {
                for (int x = 0; x < footprint.Width; x++)
                {
                    if (footprint.IsCovered(x, z)) cells.Add(at + Rotate(new GridCoord(x, z) - footprint.Anchor, rotation));
                }
            }
            return cells;
        }

        /// <summary>The way the front faces once turned.</summary>
        public static Dir8 FrontAt(Footprint footprint, int rotation) =>
            Dir8Util.Orthogonal[((int)footprint.Front + NormalizeRotation(rotation)) % 4];

        /// <summary>
        /// What stops <paramref name="definition"/> going on <paramref name="at"/> turned by
        /// <paramref name="rotation"/>; <see cref="PlacementProblem.None"/> when it fits.
        /// </summary>
        public static PlacementProblem Check(ModelDefinition definition, GridCoord at, int rotation,
                                             Func<GridCoord, bool> inBounds, IRoadQuery roads,
                                             IReadOnlyCollection<PlacedBuilding> buildings)
        {
            var problems = PlacementProblem.None;
            PlacementRules rules = definition.Placement;

            List<GridCoord> cells = CellsAt(definition.Footprint, at, rotation);
            var own = new HashSet<GridCoord>(cells);
            var occupied = new HashSet<GridCoord>();
            foreach (PlacedBuilding building in buildings)
            {
                foreach (GridCoord cell in building.Cells) occupied.Add(cell);
            }

            foreach (GridCoord cell in cells)
            {
                if (!inBounds(cell)) problems |= PlacementProblem.OutOfBounds;
                if (roads.Has(cell)) problems |= PlacementProblem.OnRoad;
                if (occupied.Contains(cell)) problems |= PlacementProblem.Overlaps;
            }

            if (rules.RoadAccess != null &&
                !HasRoadAccess(cells, own, occupied, roads, rules.RoadAccess, FrontAt(definition.Footprint, rotation)))
            {
                problems |= PlacementProblem.NoRoadAccess;
            }

            Bounds(cells, out GridCoord min, out GridCoord max);
            int sameModel = 0;
            foreach (PlacedBuilding other in buildings)
            {
                if (string.Equals(other.Definition.Id, definition.Id, StringComparison.OrdinalIgnoreCase)) sameModel++;

                int clearance = Math.Max(rules.Clearance, other.Definition.Placement.Clearance);
                int keepAway = Math.Max(KeepAwayDistance(definition, other.Definition),
                                        KeepAwayDistance(other.Definition, definition));
                int reach = Math.Max(clearance, keepAway);
                if (reach == 0 || BoxGap(min, max, other.Min, other.Max) > reach) continue;

                // Overlap is already reported; being "too close" to what you sit on says nothing more.
                int gap = Gap(cells, other.Cells);
                if (gap == 0) continue;
                if (gap <= clearance) problems |= PlacementProblem.TooClose;
                if (gap <= keepAway) problems |= PlacementProblem.KeptAway;
            }

            if (rules.MaxCount > 0 && sameModel >= rules.MaxCount) problems |= PlacementProblem.LimitReached;
            return problems;
        }

        /// <summary>
        /// Whether a road lies within <see cref="RoadAccessRule.Distance"/> cells straight out from an
        /// exposed edge on the allowed sides. A building in the way blocks the view, as does the
        /// footprint itself, so the inside of a U-shaped building does not count as its front.
        /// </summary>
        static bool HasRoadAccess(List<GridCoord> cells, HashSet<GridCoord> own, HashSet<GridCoord> occupied,
                                  IRoadQuery roads, RoadAccessRule rule, Dir8 front)
        {
            Dir8[] sides = rule.Side == RoadSide.Front ? new[] { front } : Dir8Util.Orthogonal;
            foreach (GridCoord cell in cells)
            {
                foreach (Dir8 side in sides)
                {
                    GridCoord probe = cell;
                    for (int step = 1; step <= rule.Distance; step++)
                    {
                        probe = Dir8Util.Step(probe, side);
                        if (own.Contains(probe) || occupied.Contains(probe)) break;
                        if (roads.Has(probe)) return true;
                    }
                }
            }
            return false;
        }

        /// <summary>The largest distance <paramref name="from"/>'s keepAwayFrom rules ask of <paramref name="to"/>.</summary>
        static int KeepAwayDistance(ModelDefinition from, ModelDefinition to)
        {
            int distance = 0;
            foreach (KeepAwayRule rule in from.Placement.KeepAwayFrom)
            {
                if (rule.Matches(to)) distance = Math.Max(distance, rule.Distance);
            }
            return distance;
        }

        /// <summary>Distance between the nearest two cells; 0 when they share one.</summary>
        static int Gap(IReadOnlyList<GridCoord> a, IReadOnlyList<GridCoord> b)
        {
            int best = int.MaxValue;
            foreach (GridCoord p in a)
            {
                foreach (GridCoord q in b)
                {
                    best = Math.Min(best, Math.Max(Math.Abs(p.X - q.X), Math.Abs(p.Z - q.Z)));
                    if (best == 0) return 0;
                }
            }
            return best;
        }

        /// <summary>Distance between two boxes; never more than <see cref="Gap"/> for the cells inside them.</summary>
        static int BoxGap(GridCoord aMin, GridCoord aMax, GridCoord bMin, GridCoord bMax)
        {
            int dx = Math.Max(0, Math.Max(bMin.X - aMax.X, aMin.X - bMax.X));
            int dz = Math.Max(0, Math.Max(bMin.Z - aMax.Z, aMin.Z - bMax.Z));
            return Math.Max(dx, dz);
        }

        internal static void Bounds(IReadOnlyList<GridCoord> cells, out GridCoord min, out GridCoord max)
        {
            min = new GridCoord(int.MaxValue, int.MaxValue);
            max = new GridCoord(int.MinValue, int.MinValue);
            foreach (GridCoord cell in cells)
            {
                min = new GridCoord(Math.Min(min.X, cell.X), Math.Min(min.Z, cell.Z));
                max = new GridCoord(Math.Max(max.X, cell.X), Math.Max(max.Z, cell.Z));
            }
        }
    }
}
