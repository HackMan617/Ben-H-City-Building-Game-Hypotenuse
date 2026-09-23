using System.Collections.Generic;
using CityBuilder.Grid;
using CityBuilder.Roads;
using NUnit.Framework;
using UnityEngine;

namespace CityBuilder.Tests
{
    /// <summary>
    /// Links, strokes, erase, preview overlays, undo and mesh output. These decide what geometry
    /// gets built, so a regression here shows up as visibly wrong roads.
    /// </summary>
    public class RoadNetworkTests
    {
        static GridCoord C(int x, int z) => new GridCoord(x, z);

        /// <summary>Plans and applies one build stroke along exactly these cells.</summary>
        static RoadEdit Stroke(RoadNetwork network, params GridCoord[] path)
        {
            var edit = new RoadEdit();
            RoadStroke.Plan(network, path, false, edit);
            edit.Apply(network);
            return edit;
        }

        static RoadEdit Erase(RoadNetwork network, params GridCoord[] cells)
        {
            var edit = new RoadEdit();
            RoadStroke.Plan(network, cells, true, edit);
            edit.Apply(network);
            return edit;
        }

        static RoadNetwork NetworkWithStroke(params GridCoord[] path)
        {
            var network = new RoadNetwork();
            Stroke(network, path);
            return network;
        }

        // ---- Links ------------------------------------------------------------------------------

        [Test]
        public void IsolatedCell_HasNoConnections()
        {
            var network = NetworkWithStroke(C(0, 0));
            Assert.AreEqual(Dir8.None, network.MaskAt(C(0, 0)));
        }

        [Test]
        public void EmptyCell_ReportsNoMask()
        {
            var network = NetworkWithStroke(C(0, 0));
            Assert.AreEqual(Dir8.None, network.MaskAt(C(5, 5)));
            Assert.IsFalse(network.Has(C(5, 5)));
        }

        [Test]
        public void Link_IsCanonicalWhicheverEndItIsNamedFrom()
        {
            Assert.AreEqual(new RoadLink(C(0, 0), Dir8.NE), new RoadLink(C(1, 1), Dir8.SW));
            Assert.AreEqual(new RoadLink(C(0, 0), Dir8.E), new RoadLink(C(1, 0), Dir8.W));
            Assert.AreNotEqual(new RoadLink(C(0, 0), Dir8.NE), new RoadLink(C(0, 0), Dir8.N));
        }

        [Test]
        public void Link_IsSymmetricOnBothCells()
        {
            var network = new RoadNetwork();
            network.Add(C(0, 0));
            network.Add(C(1, 1));

            Assert.IsTrue(network.Link(new RoadLink(C(0, 0), Dir8.NE)));
            Assert.AreEqual(Dir8.NE, network.MaskAt(C(0, 0)));
            Assert.AreEqual(Dir8.SW, network.MaskAt(C(1, 1)));
        }

        [Test]
        public void Link_NeedsRoadOnBothEnds()
        {
            var network = new RoadNetwork();
            network.Add(C(0, 0));

            Assert.IsFalse(network.Link(new RoadLink(C(0, 0), Dir8.E)));
            Assert.AreEqual(Dir8.None, network.MaskAt(C(0, 0)));
        }

        [Test]
        public void Link_IsClearedWhenEitherCellGoes()
        {
            var network = NetworkWithStroke(C(0, 0), C(1, 1));
            network.Remove(C(1, 1));

            Assert.AreEqual(Dir8.None, network.MaskAt(C(0, 0)),
                "a link must not survive the cell on its far end");
        }

        [Test]
        public void AddingAnExistingCell_IsANoOp()
        {
            var network = NetworkWithStroke(C(0, 0));
            Assert.IsFalse(network.Add(C(0, 0)));
            Assert.AreEqual(1, network.Count);
        }

        // ---- Strokes ----------------------------------------------------------------------------

        [Test]
        public void StraightStroke_ConnectsAlongItsAxisOnly()
        {
            var network = NetworkWithStroke(C(0, -1), C(0, 0), C(0, 1));
            Assert.AreEqual(Dir8.N | Dir8.S, network.MaskAt(C(0, 0)));
            Assert.AreEqual(Dir8.N, network.MaskAt(C(0, -1)));
        }

        [Test]
        public void CornerStroke_ConnectsOnTwoPerpendicularSides()
        {
            var network = NetworkWithStroke(C(0, -1), C(0, 0), C(1, 0));
            Assert.AreEqual(Dir8.S | Dir8.E, network.MaskAt(C(0, 0)));
        }

        [Test]
        public void DiagonalStroke_LinksAt45Degrees()
        {
            var network = NetworkWithStroke(C(0, 0), C(1, 1), C(2, 2));
            Assert.AreEqual(Dir8.NE | Dir8.SW, network.MaskAt(C(1, 1)));
        }

        [Test]
        public void StrokeStartingOnARoad_FormsATJunction()
        {
            var network = NetworkWithStroke(C(-1, 0), C(0, 0), C(1, 0));
            Stroke(network, C(0, 0), C(0, 1), C(0, 2));

            Assert.AreEqual(Dir8.W | Dir8.E | Dir8.N, network.MaskAt(C(0, 0)));
        }

        [Test]
        public void StrokeCrossingARoad_FormsACrossroads()
        {
            var network = NetworkWithStroke(C(-1, 0), C(0, 0), C(1, 0));
            Stroke(network, C(0, -1), C(0, 0), C(0, 1));

            Assert.AreEqual(Dir8.Orthogonal, network.MaskAt(C(0, 0)));
        }

        [Test]
        public void StrokeLaidAlongside_DoesNotFuse()
        {
            // Two parallel roads one cell apart stay two roads, each with its own curb.
            var network = NetworkWithStroke(C(0, 0), C(1, 0), C(2, 0));
            Stroke(network, C(0, 1), C(1, 1), C(2, 1));

            Assert.AreEqual(Dir8.W | Dir8.E, network.MaskAt(C(1, 0)));
            Assert.AreEqual(Dir8.W | Dir8.E, network.MaskAt(C(1, 1)));
        }

        [Test]
        public void DiagonalCrossingAStraightRoad_SproutsNoStubs()
        {
            // The case that forced explicit links: under the old occupancy rule (0,0) would have
            // joined (0,1) above it, and (2,2) would have joined (2,1) below it.
            var network = NetworkWithStroke(C(0, 1), C(1, 1), C(2, 1));
            Stroke(network, C(0, 0), C(1, 1), C(2, 2));

            Assert.AreEqual(Dir8.NE, network.MaskAt(C(0, 0)));
            Assert.AreEqual(Dir8.SW, network.MaskAt(C(2, 2)));
            Assert.AreEqual(Dir8.W | Dir8.E | Dir8.NE | Dir8.SW, network.MaskAt(C(1, 1)));
        }

        [Test]
        public void DiagonalsCrossingAtACorner_CrossWithoutFillingTheBlock()
        {
            var network = NetworkWithStroke(C(0, 0), C(1, 1));
            RoadEdit edit = Stroke(network, C(1, 0), C(0, 1));

            // An X like any crossroads: each cell keeps just its own diagonal, no links round the block.
            Assert.AreEqual(4, network.Count);
            Assert.AreEqual(Dir8.NE, network.MaskAt(C(0, 0)));
            Assert.AreEqual(Dir8.NW, network.MaskAt(C(1, 0)));
            Assert.AreEqual(Dir8.SW, network.MaskAt(C(1, 1)));
            Assert.AreEqual(Dir8.SE, network.MaskAt(C(0, 1)));

            Assert.AreEqual(1, edit.Linked.Count, "just the new diagonal");
        }

        [Test]
        public void DiagonalBesideAnUnlinkedPair_IsNotACrossing()
        {
            // (1,0) and (0,1) are road but not joined to each other, so nothing crosses -- the
            // stroke is refused instead, see the overlap tests below.
            var network = NetworkWithStroke(C(1, 0));
            Stroke(network, C(0, 1));

            RoadEdit edit = Plan(network, C(0, 0), C(1, 1));
            Assert.AreEqual(1, edit.Linked.Count, "just the diagonal itself");
            Assert.IsTrue(RoadStroke.IsBlocked(network, edit));
        }

        // ---- Overlap blocking -------------------------------------------------------------------

        static RoadEdit Plan(RoadNetwork network, params GridCoord[] path)
        {
            var edit = new RoadEdit();
            RoadStroke.Plan(network, path, false, edit);
            return edit;
        }

        [Test]
        public void ParallelDiagonalsOneCellApart_AreBlocked()
        {
            // Centre lines 0.71 cells apart, road a full cell wide: they would overlap.
            var network = NetworkWithStroke(C(0, 0), C(1, 1), C(2, 2));

            Assert.IsTrue(RoadStroke.IsBlocked(network, Plan(network, C(1, 0), C(2, 1), C(3, 2))), "offset east");
            Assert.IsTrue(RoadStroke.IsBlocked(network, Plan(network, C(0, 1), C(1, 2), C(2, 3))), "offset north");
        }

        [Test]
        public void ParallelDiagonalsTwoCellsApart_AreAllowed()
        {
            var network = NetworkWithStroke(C(0, 0), C(1, 1), C(2, 2));
            Assert.IsFalse(RoadStroke.IsBlocked(network, Plan(network, C(2, 0), C(3, 1), C(4, 2))));
        }

        [Test]
        public void DiagonalBrushingAnUnjoinedStraight_IsBlockedWhicheverIsDrawnFirst()
        {
            // (1,0) sits beside the diagonal step (0,0)->(1,1) without joining it.
            var straightFirst = NetworkWithStroke(C(1, 0), C(2, 0), C(3, 0));
            Assert.IsTrue(RoadStroke.IsBlocked(straightFirst, Plan(straightFirst, C(0, 0), C(1, 1), C(2, 2))));

            var diagonalFirst = NetworkWithStroke(C(0, 0), C(1, 1), C(2, 2));
            Assert.IsTrue(RoadStroke.IsBlocked(diagonalFirst, Plan(diagonalFirst, C(1, 0), C(2, 0), C(3, 0))));
            Assert.IsTrue(RoadStroke.IsBlocked(diagonalFirst, Plan(diagonalFirst, C(1, 0))), "even a single cell");
        }

        [Test]
        public void DiagonalJoiningOrCrossingAStraight_IsAllowed()
        {
            var network = NetworkWithStroke(C(-1, 1), C(0, 1), C(1, 1), C(2, 1), C(3, 1));

            Assert.IsFalse(RoadStroke.IsBlocked(network, Plan(network, C(0, 0), C(1, 1))), "ending on it");
            Assert.IsFalse(RoadStroke.IsBlocked(network, Plan(network, C(0, 0), C(1, 1), C(2, 2))), "crossing it");
        }

        [Test]
        public void DiagonalForkingOffAStraight_IsAllowed()
        {
            var network = NetworkWithStroke(C(0, -2), C(0, -1), C(0, 0), C(0, 1), C(0, 2));
            Assert.IsFalse(RoadStroke.IsBlocked(network, Plan(network, C(0, 0), C(1, 1), C(2, 2))));
        }

        [Test]
        public void DiagonalsCrossingAtACorner_AreAllowed()
        {
            // Each diagonal only overlaps the other one where they cross, which is the intersection.
            var network = NetworkWithStroke(C(-1, -1), C(0, 0), C(1, 1), C(2, 2));
            Assert.IsFalse(RoadStroke.IsBlocked(network, Plan(network, C(2, -1), C(1, 0), C(0, 1), C(-1, 2))));
        }

        [Test]
        public void Erasing_IsNeverBlocked()
        {
            var network = NetworkWithStroke(C(0, 0), C(1, 1), C(2, 2));
            var edit = new RoadEdit();
            RoadStroke.Plan(network, new[] { C(1, 1) }, true, edit);
            Assert.IsFalse(RoadStroke.IsBlocked(network, edit));
        }

        [Test]
        public void ExistingOverlapElsewhere_DoesNotBlockAnUnrelatedStroke()
        {
            // An overlap already in the network (built by hand here) is none of this stroke's business.
            var network = NetworkWithStroke(C(0, 0), C(1, 1));
            Stroke(network, C(1, 0));

            Assert.IsFalse(RoadStroke.IsBlocked(network, Plan(network, C(10, 10), C(11, 10))));
        }

        [Test]
        public void RedrawingExistingRoad_IsAnEmptyEdit()
        {
            var network = NetworkWithStroke(C(0, 0), C(1, 0), C(2, 0));
            RoadEdit edit = Stroke(network, C(0, 0), C(1, 0), C(2, 0));
            Assert.IsTrue(edit.IsEmpty);
        }

        [Test]
        public void Stroke_DoesNotLinkAcrossOutOfBoundsGaps()
        {
            var network = new RoadNetwork();
            var edit = new RoadEdit();
            RoadStroke.Plan(network, new[] { C(0, 0), C(1, 0), C(2, 0) }, false, edit, cell => cell.X != 1);
            edit.Apply(network);

            Assert.IsFalse(network.Has(C(1, 0)));
            Assert.AreEqual(Dir8.None, network.MaskAt(C(0, 0)));
            Assert.AreEqual(Dir8.None, network.MaskAt(C(2, 0)));
        }

        // ---- Erase ------------------------------------------------------------------------------

        [Test]
        public void Erasing_HealsTheMasksOfSurvivingNeighbours()
        {
            var network = NetworkWithStroke(C(0, 0), C(1, 0), C(2, 0));
            Erase(network, C(2, 0));

            Assert.AreEqual(Dir8.W, network.MaskAt(C(1, 0)), "the eastern link should be gone");
            Assert.IsFalse(network.Has(C(2, 0)));
        }

        // ---- Overlay ----------------------------------------------------------------------------

        [Test]
        public void Overlay_ShowsTheJunctionAPendingStrokeWouldCreate()
        {
            // An existing east-west run, previewed with a spur coming up from the south.
            var network = NetworkWithStroke(C(-1, 0), C(0, 0), C(1, 0));
            var edit = new RoadEdit();
            RoadStroke.Plan(network, new[] { C(0, -1), C(0, 0) }, false, edit);

            var overlay = new RoadOverlay(network);
            overlay.Apply(edit);

            Assert.AreEqual(Dir8.W | Dir8.E, network.MaskAt(C(0, 0)),
                "the committed network must not change while previewing");
            Assert.AreEqual(Dir8.W | Dir8.E | Dir8.S, overlay.MaskAt(C(0, 0)),
                "the preview should already show the T-junction");
        }

        [Test]
        public void Overlay_Reset_DropsPendingChanges()
        {
            var network = NetworkWithStroke(C(0, 0));
            var overlay = new RoadOverlay(network);

            overlay.Add(C(1, 0));
            overlay.Link(new RoadLink(C(0, 0), Dir8.E));
            Assert.IsTrue(overlay.Has(C(1, 0)));
            Assert.AreEqual(Dir8.E, overlay.MaskAt(C(0, 0)));

            overlay.Reset();
            Assert.IsFalse(overlay.Has(C(1, 0)));
            Assert.AreEqual(Dir8.None, overlay.MaskAt(C(0, 0)));
        }

        [Test]
        public void Overlay_RemovalHidesACommittedCellAndItsLinks()
        {
            var network = NetworkWithStroke(C(0, 0), C(1, 0));
            var overlay = new RoadOverlay(network);
            overlay.Remove(C(1, 0));

            Assert.IsFalse(overlay.Has(C(1, 0)));
            Assert.AreEqual(Dir8.None, overlay.MaskAt(C(0, 0)));
            Assert.IsTrue(network.Has(C(1, 0)), "the committed network is untouched");
        }

        // ---- Undo -------------------------------------------------------------------------------

        [Test]
        public void Undo_RestoresTheNetworkExactly()
        {
            var network = NetworkWithStroke(C(0, 0));
            var history = new EditHistory();

            history.Record(Stroke(network, C(0, 0), C(1, 0), C(2, 0)));
            Assert.AreEqual(3, network.Count);
            Assert.AreEqual(Dir8.E, network.MaskAt(C(0, 0)));

            history.Undo(network);
            Assert.AreEqual(1, network.Count);
            Assert.AreEqual(Dir8.None, network.MaskAt(C(0, 0)), "the link onto the old cell goes too");

            history.Redo(network);
            Assert.AreEqual(3, network.Count);
            Assert.AreEqual(Dir8.W | Dir8.E, network.MaskAt(C(1, 0)));
        }

        [Test]
        public void Undo_OfAnErasePutsTheRoadAndItsJunctionsBack()
        {
            var network = NetworkWithStroke(C(-1, 0), C(0, 0), C(1, 0));
            Stroke(network, C(0, 0), C(1, 1));
            var history = new EditHistory();

            history.Record(Erase(network, C(0, 0)));
            Assert.IsFalse(network.Has(C(0, 0)));
            Assert.AreEqual(Dir8.None, network.MaskAt(C(1, 1)));

            history.Undo(network);
            Assert.IsTrue(network.Has(C(0, 0)));
            Assert.AreEqual(Dir8.W | Dir8.E | Dir8.NE, network.MaskAt(C(0, 0)), "every link should heal back");
            Assert.AreEqual(Dir8.SW, network.MaskAt(C(1, 1)));
        }

        [Test]
        public void Undo_OfACornerCrossingTakesTheJunctionWithIt()
        {
            var network = NetworkWithStroke(C(0, 0), C(1, 1));
            var history = new EditHistory();

            history.Record(Stroke(network, C(1, 0), C(0, 1)));
            history.Undo(network);

            Assert.AreEqual(2, network.Count);
            Assert.AreEqual(Dir8.NE, network.MaskAt(C(0, 0)));
        }

        [Test]
        public void EmptyEdit_IsNotRecorded()
        {
            var history = new EditHistory();
            history.Record(new RoadEdit());
            Assert.IsFalse(history.CanUndo);
        }

        // ---- Mesh -------------------------------------------------------------------------------

        static Mesh BuildMesh(RoadNetwork network, IEnumerable<GridCoord> cells = null)
        {
            var mesh = new Mesh();
            RoadMeshBuilder.Build(mesh, network, cells ?? new List<GridCoord>(network.Cells), RoadProfile.Default);
            return mesh;
        }

        /// <summary>
        /// Every triangle must face the way its normal says -- tops up, curbs outward. A flipped
        /// piece renders as a hole from above, which is the easiest way to get diagonal geometry wrong.
        /// </summary>
        static void AssertFrontFacing(Mesh mesh)
        {
            Vector3[] vertices = mesh.vertices;
            Vector3[] normals = mesh.normals;
            int[] triangles = mesh.triangles;

            Assert.AreEqual(0, triangles.Length % 3, "triangle indices must come in threes");
            for (int i = 0; i < triangles.Length; i += 3)
            {
                Vector3 a = vertices[triangles[i]];
                Vector3 b = vertices[triangles[i + 1]];
                Vector3 c = vertices[triangles[i + 2]];
                Vector3 face = Vector3.Cross(b - a, c - a);

                Assert.Greater(face.magnitude, 1e-7f, $"degenerate triangle at index {i}");
                Assert.Greater(Vector3.Dot(face, normals[triangles[i]]), 0f, $"triangle at index {i} faces backwards");
            }
        }

        [Test]
        public void MeshBuilder_EveryJunctionCaseIsFrontFacing()
        {
            var network = new RoadNetwork();
            Stroke(network, C(-3, 0), C(-2, 0), C(-1, 0), C(0, 0), C(1, 0), C(2, 0), C(3, 0));
            Stroke(network, C(0, -2), C(0, -1), C(0, 0), C(0, 1), C(0, 2));       // crossroads
            Stroke(network, C(-3, -3), C(-2, -2), C(-1, -1), C(0, 0), C(1, 1));   // diagonal through it
            Stroke(network, C(3, 0), C(4, 1), C(5, 2), C(5, 3));                  // 135 turns
            Stroke(network, C(0, 2), C(1, 3));                                    // 45-degree fork
            Stroke(network, C(-5, 5));                                            // isolated
            Stroke(network, C(8, 0), C(9, 1));                                    // X at a corner
            Stroke(network, C(9, 0), C(8, 1));

            Mesh mesh = BuildMesh(network);
            Assert.Greater(mesh.vertexCount, 0);
            AssertFrontFacing(mesh);

            Object.DestroyImmediate(mesh);
        }

        [Test]
        public void MeshBuilder_StraightDiagonalStaysInsideItsStrip()
        {
            // No axis-aligned corner may poke out of a 45-degree road: its whole footprint, dead-end
            // caps included, lies within half a road-width of the line through the cell centres.
            var network = NetworkWithStroke(C(0, 0), C(1, 1), C(2, 2), C(3, 3));
            Mesh mesh = BuildMesh(network);

            RoadProfile profile = RoadProfile.Default;
            var axis = new Vector2(1f, 1f).normalized;
            var origin = new Vector2(0.5f, 0.5f) * profile.CellSize;

            foreach (Vector3 v in mesh.vertices)
            {
                Vector2 p = new Vector2(v.x, v.z) - origin;
                float offAxis = Mathf.Abs(p.x * axis.y - p.y * axis.x);
                Assert.LessOrEqual(offAxis, profile.HalfRoad + 1e-4f, $"vertex {v} pokes out of the road");
            }

            Object.DestroyImmediate(mesh);
        }

        [Test]
        public void MeshBuilder_CornerCrossingIsAnXOfTwoStrips()
        {
            var network = NetworkWithStroke(C(-1, -1), C(0, 0), C(1, 1), C(2, 2));
            Stroke(network, C(2, -1), C(1, 0), C(0, 1), C(-1, 2));
            Mesh mesh = BuildMesh(network);

            // Every vertex lies within one of the two crossing roads: nothing fills in the block.
            RoadProfile profile = RoadProfile.Default;
            var corner = new Vector2(1f, 1f) * profile.CellSize;
            var rising = new Vector2(1f, 1f).normalized;
            var falling = new Vector2(-1f, 1f).normalized;

            foreach (Vector3 v in mesh.vertices)
            {
                Vector2 p = new Vector2(v.x, v.z) - corner;
                float offRising = Mathf.Abs(p.x * rising.y - p.y * rising.x);
                float offFalling = Mathf.Abs(p.x * falling.y - p.y * falling.x);
                Assert.LessOrEqual(Mathf.Min(offRising, offFalling), profile.HalfRoad + 1e-4f,
                                   $"vertex {v} fills in the crossing");
            }

            Object.DestroyImmediate(mesh);
        }

        [Test]
        public void MeshBuilder_RightAngleKeepsItsSquaredCorner()
        {
            var network = NetworkWithStroke(C(0, 1), C(0, 0), C(1, 0));
            Mesh mesh = BuildMesh(network, new[] { C(0, 0) });

            RoadProfile profile = RoadProfile.Default;
            float lo = 0.5f * profile.CellSize - profile.HalfRoad;
            bool found = false;
            foreach (Vector3 v in mesh.vertices)
            {
                if (Mathf.Abs(v.x - lo) < 1e-4f && Mathf.Abs(v.z - lo) < 1e-4f && v.y > 0f) found = true;
            }

            Assert.IsTrue(found, "the outside of a 90-degree turn should be a sharp square corner");
            Object.DestroyImmediate(mesh);
        }

        [Test]
        public void MeshBuilder_SkipsCellsThatHoldNoRoad()
        {
            var network = NetworkWithStroke(C(0, 0));

            // Asking for a cell that is not road must contribute nothing rather than throw.
            Mesh mesh = BuildMesh(network, new List<GridCoord> { C(9, 9) });
            Assert.AreEqual(0, mesh.vertexCount);

            Object.DestroyImmediate(mesh);
        }
    }
}
