using System;
using System.Collections.Generic;
using CityBuilder.Grid;
using Newtonsoft.Json.Linq;

namespace CityBuilder.Content
{
    /// <summary>
    /// The behaviour and technical data that ships beside a model: Cottage.json next to Cottage.fbx
    /// in the same bundle. Read with <see cref="ModelDefinitionParser"/>.
    ///
    ///   {
    ///     "schemaVersion": 1,
    ///     "id": "cottage",
    ///     "displayName": "Cottage",
    ///     "tags": [ "residential" ],
    ///     "footprint": { "width": 2, "depth": 2, "height": 1, "front": "south" },
    ///     "placement": {
    ///       "roadAccess": { "side": "front" },
    ///       "keepAwayFrom": [ { "tag": "industrial", "distance": 4 } ]
    ///     },
    ///     "cost": 100,
    ///     "behaviors": [
    ///       { "type": "housing", "residents": 4 }
    ///     ]
    ///   }
    ///
    /// A starter layout, meant to grow: add a field here, or a behaviour type in
    /// <see cref="BehaviorConverter"/>, and older files still load.
    /// </summary>
    public class ModelDefinition
    {
        /// <summary>Bumped when a change would make older files mean something different.</summary>
        public int SchemaVersion = 1;

        /// <summary>Stable identifier, unique across all bundles; what saves and other data refer to.</summary>
        public string Id;

        /// <summary>Name shown to the player.</summary>
        public string DisplayName;

        /// <summary>
        /// Free-form labels such as "residential" or "industrial" that placement rules can refer to.
        /// Compared ignoring case.
        /// </summary>
        public List<string> Tags = new List<string>();

        public Footprint Footprint = new Footprint();

        public PlacementRules Placement = new PlacementRules();

        /// <summary>Price to place one.</summary>
        public int Cost;

        public List<BehaviorDefinition> Behaviors = new List<BehaviorDefinition>();

        /// <summary>The first behaviour of type <typeparamref name="T"/>, or null.</summary>
        public T GetBehavior<T>() where T : BehaviorDefinition
        {
            foreach (BehaviorDefinition behavior in Behaviors)
            {
                if (behavior is T match) return match;
            }
            return null;
        }

        public bool HasTag(string tag)
        {
            foreach (string own in Tags)
            {
                if (string.Equals(own, tag, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }
    }

    /// <summary>
    /// Grid cells the model covers, as laid out before the player rotates it: x runs east across
    /// <see cref="Width"/>, z runs north across <see cref="Depth"/>.
    /// </summary>
    public class Footprint
    {
        public int Width = 1;
        public int Depth = 1;

        /// <summary>How tall the model stands, in cells.</summary>
        public int Height = 1;

        /// <summary>
        /// Which cells are covered, for shapes that are not a full rectangle. One string per row,
        /// written as the rows appear on the map -- north row first -- with 'X' for covered and '.'
        /// for open:
        ///
        ///   "cells": [ "XX.",
        ///              "XXX" ]    // width 3, depth 2: an L with the north-east corner open
        ///
        /// Left out, every cell of the rectangle is covered.
        /// </summary>
        public List<string> Cells;

        /// <summary>The cell that sits under the cursor when placing. Must be a covered cell.</summary>
        public GridCoord Anchor;

        /// <summary>The side the entrance faces before rotation. Road access on the front is checked here.</summary>
        public Facing Front = Facing.South;

        public bool IsCovered(int x, int z)
        {
            if (x < 0 || x >= Width || z < 0 || z >= Depth) return false;
            if (Cells == null) return true;

            // Rows are listed north first, so the last row is z = 0.
            string row = Cells[Depth - 1 - z];
            return char.ToUpperInvariant(row[x]) == 'X';
        }
    }

    /// <summary>A side of a footprint, clockwise from north like <see cref="Dir8Util.Orthogonal"/>.</summary>
    public enum Facing
    {
        North,
        East,
        South,
        West,
    }

    /// <summary>Where a model may be put down. Everything is optional; left out, it goes anywhere free.</summary>
    public class PlacementRules
    {
        /// <summary>When set, the model must reach a road. Left out, no road is needed.</summary>
        public RoadAccessRule RoadAccess;

        /// <summary>Cells that must stay free of other buildings all round it. Roads may run through them.</summary>
        public int Clearance;

        /// <summary>Buildings, by id or tag, that may not be placed within a distance of this one.</summary>
        public List<KeepAwayRule> KeepAwayFrom = new List<KeepAwayRule>();

        /// <summary>How many a city may have; 0 means no limit.</summary>
        public int MaxCount;
    }

    public class RoadAccessRule
    {
        /// <summary>Which edges the road must be on: any edge, or only the <see cref="Footprint.Front"/>.</summary>
        public RoadSide Side = RoadSide.Any;

        /// <summary>
        /// How many cells straight out from that edge the road may be; 1 means it must touch. The
        /// cells in between must not hold a building.
        /// </summary>
        public int Distance = 1;
    }

    public enum RoadSide
    {
        Any,
        Front,
    }

    /// <summary>
    /// Keeps buildings with the given <see cref="Id"/> or <see cref="Tag"/> -- exactly one is set -- more than
    /// <see cref="Distance"/> cells away, counted like a king moves in chess. Checked both ways: neither can
    /// be placed that close to the other.
    /// </summary>
    public class KeepAwayRule
    {
        public string Id;
        public string Tag;
        public int Distance;

        public bool Matches(ModelDefinition other) =>
            !string.IsNullOrWhiteSpace(Id) ? string.Equals(Id, other.Id, StringComparison.OrdinalIgnoreCase) : other.HasTag(Tag);
    }

    /// <summary>
    /// One thing a model does, picked by its "type" field. Types this build does not know become
    /// <see cref="UnknownBehavior"/> rather than failing, so content made for a newer version still loads.
    /// </summary>
    public abstract class BehaviorDefinition
    {
        public string Type;
    }

    /// <summary>"housing": people live here.</summary>
    public class HousingBehavior : BehaviorDefinition
    {
        public int Residents;
    }

    /// <summary>"jobs": people work here.</summary>
    public class JobsBehavior : BehaviorDefinition
    {
        public int Workers;
    }

    /// <summary>A behaviour type this build does not recognise, kept as raw JSON.</summary>
    public class UnknownBehavior : BehaviorDefinition
    {
        public JObject Data;
    }
}
