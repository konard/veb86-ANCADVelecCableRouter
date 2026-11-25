using System.Collections.Generic;
using System.Linq;
using VelecCableRouter.Core.Geometry;
using VelecCableRouter.Core.Graph;
using VelecCableRouter.Core.Model;
using VelecCableRouter.Core.Routing;
using Xunit;
using static VelecCableRouter.Core.Tests.TestData;

namespace VelecCableRouter.Core.Tests
{
    public class CableRouterTests
    {
        private static (RoutingResult Result, List<Diagnostic> Diagnostics) Route(params RawEntity[] entities)
        {
            var model = DrawingModelReader.Read(entities);
            var participants = GraphParticipantFilter.Filter(model, "ОПС");
            var diagnostics = new List<Diagnostic>();
            var graph = GraphBuilder.Build(participants, 0.5, diagnostics);
            return (CableRouter.Route(graph, participants, diagnostics), diagnostics);
        }

        [Fact]
        public void DaisyChain_IsOnePolylineWithoutCollinearVertices()
        {
            var (result, diagnostics) = Route(
                Line("ОПС", P(0, 0), P(40, 0)),
                Head("ППК-1", P(0, 0), "ОПС"),
                Device("ИП-1", P(10, 0), "ОПС", "ППК-1", "ШС1"),
                Device("ИП-2", P(20, 0), "ОПС", "ППК-1", "ШС1"),
                Device("ИП-3", P(30, 0), "ОПС", "ППК-1", "ШС1"));

            Assert.Empty(diagnostics);
            var cable = Assert.Single(result.Cables);
            Assert.Equal(new[] { P(0, 0), P(30, 0) }, cable.Points);
            Assert.Equal("ОПС", cable.SuperLineName);
            Assert.Equal("ППК-1", cable.HeadDeviceName);
            Assert.Equal("ШС1", cable.GroupName);
            Assert.Equal("Трассы", cable.Layer);
            Assert.Equal(3, cable.DeviceHandles.Count);
            Assert.Equal(30, cable.Length, 9);
        }

        [Fact]
        public void PolylineCorners_AreKeptAndCollinearVerticesRemoved()
        {
            var (result, _) = Route(
                Polyline("ОПС", P(0, 0), P(5, 0), P(10, 0), P(10, 5), P(10, 10)),
                Head("ППК-1", P(0, 0), "ОПС"),
                Device("ИП-1", P(10, 10), "ОПС", "ППК-1", "ШС1"));

            Assert.Equal(new[] { P(0, 0), P(10, 0), P(10, 10) }, Assert.Single(result.Cables).Points);
        }

        [Fact]
        public void ShortestPath_IsChosen()
        {
            var (result, _) = Route(
                Line("ОПС", P(0, 0), P(10, 0)),
                Polyline("ОПС", P(0, 0), P(0, 10), P(10, 10), P(10, 0)),
                Head("ППК-1", P(0, 0), "ОПС"),
                Device("ИП-1", P(10, 0), "ОПС", "ППК-1", "ШС1"));

            Assert.Equal(new[] { P(0, 0), P(10, 0) }, Assert.Single(result.Cables).Points);
        }

        [Fact]
        public void Branch_ContinuesStraightAndStartsNewPolylineForSideBranch()
        {
            var (result, _) = Route(
                Line("ОПС", P(0, 0), P(20, 0)),
                Line("ОПС", P(10, 0), P(10, 10)),
                Head("ППК-1", P(0, 0), "ОПС"),
                Device("ИП-1", P(20, 0), "ОПС", "ППК-1", "ШС1"),
                Device("ИП-2", P(10, 10), "ОПС", "ППК-1", "ШС1"));

            Assert.Equal(2, result.Cables.Count);
            Assert.Contains(result.Cables, c => c.Points.SequenceEqual(new[] { P(0, 0), P(20, 0) }));
            Assert.Contains(result.Cables, c => c.Points.SequenceEqual(new[] { P(10, 0), P(10, 10) }));
        }

        [Fact]
        public void Groups_AreRoutedSeparately()
        {
            var (result, _) = Route(
                Line("ОПС", P(0, 0), P(20, 0)),
                Head("ППК-1", P(0, 0), "ОПС"),
                Device("ИП-1", P(10, 0), "ОПС", "ППК-1", "ШС1"),
                Device("ИП-2", P(20, 0), "ОПС", "ППК-1", "ШС2"));

            Assert.Equal(new[] { "ШС1", "ШС2" }, result.Groups.Select(g => g.GroupName));
            Assert.Equal(new[] { P(0, 0), P(10, 0) }, result.Cables.Single(c => c.GroupName == "ШС1").Points);
            Assert.Equal(new[] { P(0, 0), P(20, 0) }, result.Cables.Single(c => c.GroupName == "ШС2").Points);
        }

        [Fact]
        public void MissingHead_IsReportedAndOtherGroupsAreStillRouted()
        {
            var (result, diagnostics) = Route(
                Line("ОПС", P(0, 0), P(20, 0)),
                Head("ППК-1", P(0, 0), "ОПС"),
                Device("ИП-1", P(10, 0), "ОПС", "ППК-9", "ШС1"),
                Device("ИП-2", P(20, 0), "ОПС", "ППК-1", "ШС2"));

            var error = Assert.Single(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
            Assert.Contains("ППК-9", error.Message);
            Assert.Equal(P(10, 0), error.Location);
            Assert.Equal("ШС2", Assert.Single(result.Cables).GroupName);
        }

        [Fact]
        public void UnreachableDevice_IsReportedAndReachableDevicesAreRouted()
        {
            var (result, diagnostics) = Route(
                Line("ОПС", P(0, 0), P(20, 0)),
                Head("ППК-1", P(0, 0), "ОПС"),
                Device("ИП-1", P(20, 0), "ОПС", "ППК-1", "ШС1"),
                Device("ИП-2", P(50, 50), "ОПС", "ППК-1", "ШС1"));

            var error = Assert.Single(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
            Assert.Contains("ИП-2", error.Message);
            Assert.Equal(P(50, 50), error.Location);
            Assert.Equal(new[] { P(0, 0), P(20, 0) }, Assert.Single(result.Cables).Points);
        }

        [Fact]
        public void RiserEdge_BreaksPolylineAndIsNotDrawn()
        {
            var (result, diagnostics) = Route(
                Line("ОПС", P(0, 0), P(10, 0)),
                Line("ОПС", P(100, 0), P(100, 10)),
                Head("ППК-1", P(0, 0), "ОПС"),
                Riser("Ст-1 эт.1", P(10, 0), "Ст-1", 0),
                Riser("Ст-1 эт.2", P(100, 0), "Ст-1", 3),
                Device("ИП-1", P(100, 10), "ОПС", "ППК-1", "ШС1"));

            Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
            Assert.Equal(2, result.Cables.Count);
            Assert.Equal(new[] { P(0, 0), P(10, 0) }, result.Cables[0].Points);
            Assert.Equal(new[] { P(100, 0), P(100, 10) }, result.Cables[1].Points);
        }

        [Fact]
        public void DevicesWithoutHead_DoNotFormGroups()
        {
            var (result, _) = Route(
                Line("ОПС", P(0, 0), P(20, 0)),
                Head("ППК-1", P(0, 0), "ОПС"),
                Device("ИП-1", P(10, 0), "ОПС", null, "ШС1"));

            Assert.Empty(result.Groups);
            Assert.Empty(result.Cables);
        }

        [Fact]
        public void HeadWithItsOwnHeadDeviceName_IsNotRoutedToItself()
        {
            var head = RawEntity.Block(NextHandle(), P(0, 0), DeviceXData("ППК-1", new[] { ("ОПС", (string?)"ППК-1", (string?)"ШС1") }), P(0, 0), Bounds2D.Around(P(0, 0), 1));
            var (result, diagnostics) = Route(
                Line("ОПС", P(0, 0), P(20, 0)),
                head,
                Device("ИП-1", P(20, 0), "ОПС", "ППК-1", "ШС1"));

            Assert.Empty(diagnostics);
            Assert.Single(Assert.Single(result.Groups).Devices);
            Assert.Single(result.Cables);
        }
    }
}
