using System.Collections.Generic;
using CityBuilder.Content;
using CityBuilder.Grid;

namespace CityBuilder.Buildings
{
    public static class BuildingPalette
    {
        public static List<ModelDefinition> Create()
        {
            return new List<ModelDefinition>
            {
                new ModelDefinition
                {
                    Type = ModelType.Structure,
                    Id = "cottage",
                    DisplayName = "Cottage",
                    Tags = new List<string> { "residential" },
                    Footprint = new Footprint { Width = 2, Depth = 2, Height = 1, Anchor = new GridCoord(0, 0), Front = Facing.South },
                    Placement = new PlacementRules(),
                    Cost = 100,
                    Behaviors = new List<BehaviorDefinition> { new HousingBehavior { Type = "housing", Residents = 4 } },
                },
                new ModelDefinition
                {
                    Type = ModelType.Structure,
                    Id = "shop",
                    DisplayName = "Shop",
                    Tags = new List<string> { "commercial" },
                    Footprint = new Footprint { Width = 1, Depth = 1, Height = 1, Anchor = new GridCoord(0, 0), Front = Facing.South },
                    Placement = new PlacementRules(),
                    Cost = 200,
                    Behaviors = new List<BehaviorDefinition> { new JobsBehavior { Type = "jobs", Workers = 3 } },
                },
                new ModelDefinition
                {
                    Type = ModelType.Structure,
                    Id = "factory",
                    DisplayName = "Factory",
                    Tags = new List<string> { "industrial" },
                    Footprint = new Footprint { Width = 3, Depth = 2, Height = 2, Anchor = new GridCoord(0, 0), Front = Facing.South },
                    Placement = new PlacementRules { Clearance = 1 },
                    Cost = 500,
                    Behaviors = new List<BehaviorDefinition> { new JobsBehavior { Type = "jobs", Workers = 10 } },
                },
                new ModelDefinition
                {
                    Type = ModelType.Structure,
                    Id = "park",
                    DisplayName = "Park",
                    Tags = new List<string> { "leisure" },
                    Footprint = new Footprint { Width = 2, Depth = 2, Height = 1, Anchor = new GridCoord(0, 0), Front = Facing.South },
                    Placement = new PlacementRules(),
                    Cost = 150,
                },
            };
        }
    }
}
