using System.Collections.Generic;
using CityBuilder.Grid;
using CityBuilder.Roads;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;

namespace CityBuilder.Tests
{
    /// <summary>
    /// How a layout splits into junction cells and spline runs, and the shape of those splines.
    /// A run that ends in the wrong place leaves a gap or an overlap against the junction beside it.
    /// </summary>
    public class RoadSplineTests
    {
        static GridCoord C(int x, int z) => new GridCoord(x, z);

        static RoadNetwork Network(params GridCoord[] path)
        {
            var network = new RoadNetwork();
            var edit = new RoadEdit();
            RoadStroke.Plan(network, path, false, edit);
            edit.Apply(network);
            return network;
        }

        static void Add(RoadNetwork network, params GridCoord[] path)
        {
            var edit = new RoadEdit();
            RoadStroke.Plan(network, path, false, edit);
            edit.Apply(network);
        }

        static void Extract(RoadNetwork network, out List<GridCoord> nodes, out List<RoadChain> chains)
        {
            nodes = new List<GridCoord>();
            chains = new List<RoadChain>();
            RoadChains.Extract(network, new List<GridCoord>(network.Cells), nodes, chains, new List<Vector2>());
        }

        [Test]
        public void DiagonalsCrossingAtACorner_MeetAtAJunctionOnTheCorner()
        {
            RoadNetwork network = Network(C(-1, -1), C(0, 0), C(1, 1), C(2, 2));
            Add(network, C(2, -1), C(1, 0), C(0, 1), C(-1, 2));

            var nodes = new List<GridCoord>();
            var chains = new List<RoadChain>();
            var crossings = new List<Vector2>();
            RoadChains.Extract(network, new List<GridCoord>(network.Cells), nodes, chains, crossings);

            Assert.AreEqual(1, crossings.Count);
            Assert.AreEqual(new Vector2(1f, 1f), crossings[0]);

            // Each road is cut in two at the crossing, and every cut end stops half a cell out from
            // the corner along its diagonal -- where the crossroads piece's arms end.
            Assert.AreEqual(4, chains.Count);
            int endsAtCrossing = 0;
            foreach (RoadChain chain in chains)
            {
                foreach (Vector2 end in new[] { chain.Points[0], chain.Points[chain.Points.Count - 1] })
                {
                    if (Vector2.Distance(end, crossings[0]) < 0.6f)
                    {
                        Assert.AreEqual(RoadChains.CrossingReach, Vector2.Distance(end, crossings[0]), 1e-4f);
                        endsAtCrossing++;
                    }
                }
            }
            Assert.AreEqual(4, endsAtCrossing);
        }

        [Test]
        public void DeadEnd_IsRoundedOff()
        {
            RoadNetwork network = Network(C(0, 0), C(1, 0));
            var mesh = new Mesh();
            RoadMeshBuilder.Build(mesh, network, new[] { C(0, 0) }, RoadProfile.Default);

            // Behind the centre of the end cell (0, 0), whose one arm runs east, everything lies
            // within half a road-width of the centre, and the cap reaches all the way out to it.
            RoadProfile profile = RoadProfile.Default;
            var centre = new Vector2(0.5f, 0.5f) * profile.CellSize;
            float furthestBack = 0f;
            foreach (Vector3 v in mesh.vertices)
            {
                Vector2 p = new Vector2(v.x, v.z) - centre;
                if (p.x >= -1e-4f) continue;
                Assert.LessOrEqual(p.magnitude, profile.HalfRoad + 1e-4f, $"vertex {v} squares off the end");
                furthestBack = Mathf.Max(furthestBack, -p.x);
            }
            Assert.AreEqual(profile.HalfRoad, furthestBack, 1e-4f);

            Object.DestroyImmediate(mesh);
        }

        [Test]
        public void IsolatedCell_IsARoundDisc()
        {
            RoadNetwork network = Network(C(0, 0));
            var mesh = new Mesh();
            RoadMeshBuilder.Build(mesh, network, new[] { C(0, 0) }, RoadProfile.Default);

            RoadProfile profile = RoadProfile.Default;
            var centre = new Vector2(0.5f, 0.5f) * profile.CellSize;
            foreach (Vector3 v in mesh.vertices)
            {
                float r = (new Vector2(v.x, v.z) - centre).magnitude;
                Assert.That(r, Is.LessThan(1e-4f).Or.EqualTo(profile.HalfRoad).Within(1e-4f), $"vertex {v} is off the disc");
            }
            AssertFrontFacing(network);

            Object.DestroyImmediate(mesh);
        }

        [Test]
        public void CornerCrossingGeometry_FacesTheWayItsNormalsSay()
        {
            RoadNetwork network = Network(C(-1, -1), C(0, 0), C(1, 1), C(2, 2));
            Add(network, C(2, -1), C(1, 0), C(0, 1), C(-1, 2));
            AssertFrontFacing(network);
        }

        [Test]
        public void StraightRun_IsOneChainBetweenItsDeadEnds()
        {
            RoadNetwork network = Network(C(0, 0), C(1, 0), C(2, 0), C(3, 0));
            Extract(network, out List<GridCoord> nodes, out List<RoadChain> chains);

            CollectionAssert.AreEquivalent(new[] { C(0, 0), C(3, 0) }, nodes);
            Assert.AreEqual(1, chains.Count);
            Assert.IsFalse(chains[0].Closed);

            // Ends sit on the edges shared with the dead-end cells, where their arms stop.
            List<Vector2> points = chains[0].Points;
            Vector2 a = points[0], b = points[points.Count - 1];
            Assert.That(new[] { a.x, b.x }, Is.EquivalentTo(new[] { 1f, 3f }));
            Assert.AreEqual(0.5f, a.y, 1e-5f);
        }

        [Test]
        public void TJunction_SplitsIntoThreeChains()
        {
            RoadNetwork network = Network(C(-3, 0), C(-2, 0), C(-1, 0), C(0, 0), C(1, 0), C(2, 0), C(3, 0));
            Add(network, C(0, 0), C(0, 1), C(0, 2), C(0, 3));
            Extract(network, out List<GridCoord> nodes, out List<RoadChain> chains);

            CollectionAssert.Contains(nodes, C(0, 0));
            Assert.AreEqual(3, chains.Count);
        }

        [Test]
        public void Loop_IsOneClosedChain()
        {
            RoadNetwork network = Network(C(0, 0), C(1, 0), C(2, 0), C(2, 1), C(2, 2), C(1, 2), C(0, 2), C(0, 1), C(0, 0));
            Extract(network, out List<GridCoord> nodes, out List<RoadChain> chains);

            Assert.AreEqual(0, nodes.Count);
            Assert.AreEqual(1, chains.Count);
            Assert.IsTrue(chains[0].Closed);
            Assert.AreEqual(8, chains[0].Points.Count);
        }

        [Test]
        public void Bend_IsRoundedInsideItsCell()
        {
            RoadNetwork network = Network(C(0, 0), C(1, 0), C(2, 0), C(2, 1), C(2, 2));
            Extract(network, out _, out List<RoadChain> chains);

            Spline spline = RoadSplines.Build(chains[0], 1f);
            Assert.IsNotNull(spline);

            // The corner cell is (2, 0); its centre is cut off, and every point on the curve stays
            // inside the cells the road owns.
            var corner = new float3(2.5f, 0f, 0.5f);
            float closest = float.MaxValue;
            for (int i = 0; i <= 200; i++)
            {
                float3 p = spline.EvaluatePosition(i / 200f);
                closest = Mathf.Min(closest, math.distance(p, corner));
                Assert.That(p.x, Is.InRange(0.999f, 3.001f));
                Assert.That(p.z, Is.InRange(-0.001f, 2.001f));
                Assert.AreEqual(0f, p.y, 1e-5f);
            }
            Assert.Greater(closest, 0.1f);
        }

        [Test]
        public void StraightRun_SplineIsStraightAndFullLength()
        {
            RoadNetwork network = Network(C(0, 0), C(1, 0), C(2, 0), C(3, 0));
            Extract(network, out _, out List<RoadChain> chains);

            Spline spline = RoadSplines.Build(chains[0], 2f);
            Assert.AreEqual(4f, spline.GetLength(), 1e-3f);
            for (int i = 0; i <= 20; i++) Assert.AreEqual(1f, spline.EvaluatePosition(i / 20f).z, 1e-4f);
        }

        [Test]
        public void Geometry_FacesTheWayItsNormalsSay()
        {
            AssertFrontFacing(Network(C(0, 0), C(1, 0), C(2, 0), C(2, 1), C(2, 2), C(3, 3)));
        }

        static void AssertFrontFacing(RoadNetwork network)
        {
            var buffers = new RoadMeshBuffers();
            RoadGeometry.Build(buffers, network, new List<GridCoord>(network.Cells), RoadProfile.Default, null);

            var mesh = new Mesh();
            buffers.WriteTo(mesh);
            Assert.Greater(mesh.vertexCount, 0);

            Vector3[] v = mesh.vertices;
            Vector3[] n = mesh.normals;
            int[] t = mesh.triangles;
            for (int i = 0; i < t.Length; i += 3)
            {
                Vector3 face = Vector3.Cross(v[t[i + 1]] - v[t[i]], v[t[i + 2]] - v[t[i]]);

                // At full road width a half-cell bend pinches its inner edge to a point, and the cubic's
                // tiny departure from a true circle can tip those slivers either way. They have no area
                // to see; anything bigger must face the right way.
                if (face.magnitude * 0.5f < 2e-3f) continue;
                Assert.GreaterOrEqual(Vector3.Dot(face, n[t[i]]), 0f, $"triangle {i / 3} is flipped");
            }
            Object.DestroyImmediate(mesh);
        }
    }
}
