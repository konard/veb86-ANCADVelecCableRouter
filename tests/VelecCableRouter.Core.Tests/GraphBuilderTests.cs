using System;
using System.Linq;
using VelecCableRouter.Core.Geometry;
using VelecCableRouter.Core.Graph;
using VelecCableRouter.Core.Model;
using Xunit;
using static VelecCableRouter.Core.Tests.TestData;

namespace VelecCableRouter.Core.Tests
{
    public class GraphBuilderTests
    {
        private static TopologyGraph Build(double epsilon, params RawEntity[] entities)
        {
            var model = DrawingModelReader.Read(entities);
            return GraphBuilder.Build(GraphParticipantFilter.Filter(model, "ОПС"), epsilon);
        }

        private static bool HasEdge(TopologyGraph graph, Point2D a, Point2D b) =>
            graph.Edges.Any(e =>
            {
                var p = graph.Vertices[e.From].Point;
                var q = graph.Vertices[e.To].Point;
                return (Near(p, a) && Near(q, b)) || (Near(p, b) && Near(q, a));
            });

        private static bool Near(Point2D a, Point2D b) => a.DistanceTo(b) < 1e-6;

        [Fact]
        public void ConnectorsOnLine_SplitSegmentIntoChain()
        {
            var graph = Build(
                0.5,
                Line("ОПС", P(0, 0), P(30, 0)),
                Head("ППК-1", P(0, 0), "ОПС"),
                Device("ИП-1", P(10, 0.3), "ОПС", "ППК-1", "ШС1"),
                Device("ИП-2", P(20, 0), "ОПС", "ППК-1", "ШС1"));

            Assert.Equal(3, graph.Vertices.Count);
            Assert.Equal(2, graph.Edges.Count);
            Assert.True(HasEdge(graph, P(0, 0), P(10, 0.3)));
            Assert.True(HasEdge(graph, P(10, 0.3), P(20, 0)));
            Assert.All(graph.Edges, e => Assert.Equal(graph.Vertices[e.From].Point.DistanceTo(graph.Vertices[e.To].Point), e.Weight, 9));
            Assert.All(graph.Edges, e => Assert.Equal(EdgeKind.Segment, e.Kind));
        }

        [Fact]
        public void DeviceAwayFromLine_IsNotConnected()
        {
            var graph = Build(0.5, Line("ОПС", P(0, 0), P(10, 0)), Head("ППК-1", P(0, 0), "ОПС"), Device("ИП-1", P(5, 2), "ОПС", "ППК-1", "1"));
            Assert.Empty(graph.EdgesOf(graph.Vertices.Single(v => v.Devices.Any(d => d.Name == "ИП-1")).Index));
        }

        [Fact]
        public void TJunction_CreatesIntersectionVertex()
        {
            var graph = Build(
                0.5,
                Line("ОПС", P(0, 0), P(20, 0)),
                Line("ОПС", P(10, 0.2), P(10, 10)),
                Head("ППК-1", P(0, 0), "ОПС"),
                Device("ИП-1", P(20, 0), "ОПС", "ППК-1", "1"),
                Device("ИП-2", P(10, 10), "ОПС", "ППК-1", "1"));

            var junction = graph.Vertices.Single(v => v.Kind == VertexKind.Intersection);
            Assert.Equal(P(10, 0), junction.Point);
            Assert.Equal(3, graph.EdgesOf(junction.Index).Count);
        }

        [Fact]
        public void CrossingLines_CreateOneIntersectionWithFourEdges()
        {
            var graph = Build(
                0.5,
                Line("ОПС", P(0, 0), P(20, 0)),
                Line("ОПС", P(10, -10), P(10, 10)),
                Head("ППК-1", P(0, 0), "ОПС"),
                Device("ИП-1", P(20, 0), "ОПС", "ППК-1", "1"),
                Device("ИП-2", P(10, 10), "ОПС", "ППК-1", "1"),
                Device("ИП-3", P(10, -10), "ОПС", "ППК-1", "1"));

            var cross = graph.Vertices.Single(v => v.Kind == VertexKind.Intersection);
            Assert.Equal(4, graph.EdgesOf(cross.Index).Count);
        }

        [Fact]
        public void SegmentsOfOtherSuperLines_AreNotUsed()
        {
            var graph = Build(
                0.5,
                Line("ОПС", P(0, 0), P(20, 0)),
                Line("ЭС", P(10, -10), P(10, 10)),
                Head("ППК-1", P(0, 0), "ОПС"),
                Device("ИП-1", P(20, 0), "ОПС", "ППК-1", "1"));

            Assert.Equal(2, graph.Vertices.Count);
            Assert.Single(graph.Edges);
        }

        [Fact]
        public void CollinearContinuation_JoinsSegmentEnds()
        {
            var graph = Build(
                0.5,
                Line("ОПС", P(0, 0), P(10, 0)),
                Line("ОПС", P(10.2, 0), P(20, 0)),
                Head("ППК-1", P(0, 0), "ОПС"),
                Device("ИП-1", P(20, 0), "ОПС", "ППК-1", "1"));

            Assert.Equal(3, graph.Vertices.Count);
            Assert.Contains(graph.Vertices, v => v.Kind == VertexKind.SegmentEnd && Near(v.Point, P(10.2, 0)));
            Assert.Equal(2, graph.Edges.Count);
        }

        [Fact]
        public void SegmentEndInsideDeviceBlock_GetsTapEdgeToConnector()
        {
            // Line stops at the block outline (block 4×4 around the connector at (12, 0)).
            var device = RawEntity.Block(NextHandle(), P(12, 0), DeviceXData("ИП-1", new[] { ("ОПС", (string?)"ППК-1", (string?)"1") }), P(12, 0), Bounds2D.Around(P(12, 0), 2));
            var graph = Build(0.5, Line("ОПС", P(0, 0), P(10, 0)), Head("ППК-1", P(0, 0), "ОПС"), device);

            var tap = Assert.Single(graph.Edges, e => e.Kind == EdgeKind.DeviceTap);
            Assert.True(HasEdge(graph, P(10, 0), P(12, 0)));
            Assert.Equal(2, tap.Weight, 9);
            Assert.False(tap.IsVirtualRiserEdge);
        }

        [Fact]
        public void SegmentEndInsideTwoDevices_PicksNearestInsertionPoint()
        {
            var far = RawEntity.Block(NextHandle(), P(13, 0), DeviceXData("ИП-1", new[] { ("ОПС", (string?)"ППК-1", (string?)"1") }), P(13, 0), new Bounds2D(9, -3, 16, 3));
            var near = RawEntity.Block(NextHandle(), P(11, 1), DeviceXData("ИП-2", new[] { ("ОПС", (string?)"ППК-1", (string?)"1") }), P(11, 1), new Bounds2D(9, -1, 12, 2));
            var graph = Build(0.5, Line("ОПС", P(0, 0), P(10, 0)), Head("ППК-1", P(0, 0), "ОПС"), far, near);

            Assert.True(HasEdge(graph, P(10, 0), P(11, 1)));
            Assert.False(HasEdge(graph, P(10, 0), P(13, 0)));
        }

        [Fact]
        public void Risers_AreLinkedByVirtualEdgesSortedByElevation()
        {
            var graph = Build(
                0.5,
                Line("ОПС", P(0, 0), P(10, 0)),
                Riser("Ст-1/3", P(10, 200), "Ст-1", 6.0),
                Riser("Ст-1/1", P(10, 0), "Ст-1", 0.0),
                Riser("Ст-1/2", P(10, 100), "Ст-1", 3.0));

            var risers = graph.Edges.Where(e => e.IsVirtualRiserEdge).ToList();
            Assert.Equal(2, risers.Count);
            Assert.True(HasEdge(graph, P(10, 0), P(10, 100)));
            Assert.True(HasEdge(graph, P(10, 100), P(10, 200)));
            Assert.False(HasEdge(graph, P(10, 0), P(10, 200)));
            Assert.All(risers, e => Assert.Equal(3.0, e.Weight, 9));
        }

        [Fact]
        public void SingleRiser_IsReported()
        {
            var model = DrawingModelReader.Read(new[] { Line("ОПС", P(0, 0), P(10, 0)), Riser("Ст", P(10, 0), "Ст-1", 0) });
            var diagnostics = new System.Collections.Generic.List<Diagnostic>();
            GraphBuilder.Build(GraphParticipantFilter.Filter(model, "ОПС"), 0.5, diagnostics);
            Assert.Contains(diagnostics, d => d.Message.Contains("Ст-1"));
        }

        [Fact]
        public void DevicesInsideOneEpsBox_ShareVertex()
        {
            var graph = Build(
                0.5,
                Line("ОПС", P(0, 0), P(10, 0)),
                Head("ППК-1", P(0, 0), "ОПС"),
                Device("ИП-1", P(10, 0), "ОПС", "ППК-1", "1"),
                Device("ИП-2", P(10.1, 0.1), "ОПС", "ППК-1", "1"));

            var shared = graph.Vertices.Single(v => v.Devices.Count == 2);
            Assert.Equal(P(10, 0), shared.Point);
        }

        [Fact]
        public void Epsilon_MustBePositive()
        {
            var participants = new GraphParticipants("ОПС", Array.Empty<SuperLineSegment>(), Array.Empty<DeviceInfo>());
            Assert.Throws<ArgumentOutOfRangeException>(() => GraphBuilder.Build(participants, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => GraphBuilder.Build(participants, double.NaN));
        }
    }
}
