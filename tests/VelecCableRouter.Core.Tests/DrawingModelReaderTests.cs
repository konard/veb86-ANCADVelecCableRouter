using System.Linq;
using VelecCableRouter.Core.Geometry;
using VelecCableRouter.Core.Model;
using VelecCableRouter.Core.XData;
using Xunit;
using static VelecCableRouter.Core.Tests.TestData;

namespace VelecCableRouter.Core.Tests
{
    public class DrawingModelReaderTests
    {
        [Fact]
        public void Polyline_IsSplitIntoSegments()
        {
            var model = DrawingModelReader.Read(new[] { Polyline("ОПС", P(0, 0), P(10, 0), P(10, 0), P(10, 5)) });
            Assert.Equal(2, model.Segments.Count);
            Assert.All(model.Segments, s => Assert.Equal("ОПС", s.Name));
            Assert.Equal(P(10, 0), model.Segments[1].Start);
            Assert.Equal(P(10, 5), model.Segments[1].End);
        }

        [Fact]
        public void ClosedPolyline_AddsClosingSegment()
        {
            var entity = RawEntity.Polyline("A1", new[] { P(0, 0), P(10, 0), P(10, 10) }, SuperLineXData("ЭС"), closed: true);
            Assert.Equal(3, DrawingModelReader.Read(new[] { entity }).Segments.Count);
        }

        [Fact]
        public void SegmentWithoutName_IsIgnoredWithWarning()
        {
            var model = DrawingModelReader.Read(new[] { RawEntity.Line("A1", P(0, 0), P(1, 0), SuperLineXData(null)) });
            Assert.Empty(model.Segments);
            var warning = Assert.Single(model.Diagnostics);
            Assert.Equal(DiagnosticSeverity.Warning, warning.Severity);
            Assert.Contains("NMO_Name", warning.Message);
            Assert.Equal("A1", warning.Handle);
        }

        [Fact]
        public void ObjectsWithoutBobrovXData_AreSkipped()
        {
            var model = DrawingModelReader.Read(new[]
            {
                RawEntity.Line("A1", P(0, 0), P(1, 0), null),
                RawEntity.Line("A2", P(0, 0), P(1, 0), Header("Cable")),
            });
            Assert.Empty(model.Segments);
            Assert.Empty(model.Devices);
            Assert.Empty(model.Diagnostics);
        }

        [Fact]
        public void Device_UsesConnectorOrInsertionPoint()
        {
            var withConnector = RawEntity.Block("B1", P(0, 0), DeviceXData("ИП-1"), connector: P(1, 2));
            var withoutConnector = RawEntity.Block("B2", P(5, 5), DeviceXData("ИП-2"));
            var model = DrawingModelReader.Read(new[] { withConnector, withoutConnector });
            Assert.Equal(P(1, 2), model.Devices[0].Connector);
            Assert.Equal(P(5, 5), model.Devices[1].Connector);
        }

        [Fact]
        public void Device_ReadsRepeatConnectionsRiserAndElevation()
        {
            var xdata = DeviceXData("ИП-1", new[] { ("ОПС", (string?)"ППК-1", (string?)"ШС1"), ("ЭС", "ЩР-1", "Гр.3") }, riser: "Ст-1", elevation: 3.3);
            var device = DrawingModelReader.Read(new[] { RawEntity.Block("B1", P(0, 0), xdata) }).Devices.Single();
            Assert.Equal("ИП-1", device.Name);
            Assert.Equal("Ст-1", device.RiserName);
            Assert.Equal(3.3, device.Elevation);
            Assert.Equal(2, device.Connections.Count);
            Assert.Equal("ОПС", device.Connections[0].SuperLineName);
            Assert.Equal("ППК-1", device.Connections[0].HeadDeviceName);
            Assert.Equal("ШС1", device.Connections[0].GroupName);
            Assert.Equal("conn_1", device.Connections[0].Source);
            Assert.Equal("Гр.3", device.Connections[1].GroupName);
        }

        [Fact]
        public void Device_ReadsScalarConnectionAndNameSuffixedRepeatFields()
        {
            var xdata = Header("device");
            xdata.AddRange(new[]
            {
                XDataValue.Str("NMO_Name"), XDataValue.Str("ИП-2"),
                XDataValue.Str("SuperLineName"), XDataValue.Str("ОПС"),
                XDataValue.Str("HeadDeviceName"), XDataValue.Str("ППК-1"),
                XDataValue.Str("GroupName"), XDataValue.Int16(2),
                XDataValue.Str("CONN_2_SUPERLINENAME"), XDataValue.Str("ЭС"),
                XDataValue.Str("conn_2_HeadDeviceName"), XDataValue.Str("ЩР-1"),
                XDataValue.Str("conn_2_GroupName"), XDataValue.Str("1"),
            });
            var device = DrawingModelReader.Read(new[] { RawEntity.Block("B1", P(0, 0), xdata) }).Devices.Single();
            Assert.Equal(2, device.Connections.Count);
            Assert.Equal("ЭС", device.Connections[0].SuperLineName);
            Assert.Equal("ЩР-1", device.Connections[0].HeadDeviceName);
            Assert.Equal("ОПС", device.Connections[1].SuperLineName);
            Assert.Equal("2", device.Connections[1].GroupName);
        }

        [Fact]
        public void Device_DuplicateConnectionsAreMerged()
        {
            var xdata = DeviceXData("ИП-1", new[] { ("ОПС", (string?)"ППК-1", (string?)"ШС1"), ("ОПС", "ППК-1", "ШС1") });
            var device = DrawingModelReader.Read(new[] { RawEntity.Block("B1", P(0, 0), xdata) }).Devices.Single();
            Assert.Single(device.Connections);
        }

        [Fact]
        public void WrongGeometry_IsReported()
        {
            var model = DrawingModelReader.Read(new[]
            {
                RawEntity.Line("A1", P(0, 0), P(1, 0), DeviceXData("ИП-1")),
                RawEntity.Block("B1", P(0, 0), SuperLineXData("ОПС")),
            });
            Assert.Empty(model.Devices);
            Assert.Empty(model.Segments);
            Assert.Equal(2, model.Diagnostics.Count);
        }

        [Fact]
        public void SuperLineCollector_ReturnsDistinctNamesInOrder()
        {
            var model = DrawingModelReader.Read(new[]
            {
                Line("ОПС", P(0, 0), P(1, 0)),
                Line("ЭС", P(0, 0), P(1, 0)),
                Line("ОПС", P(1, 0), P(2, 0)),
                Line("ЭМ", P(0, 0), P(1, 0)),
            });
            Assert.Equal(new[] { "ОПС", "ЭС", "ЭМ" }, SuperLineCollector.GetSuperLineNames(model.Segments));
        }

        [Fact]
        public void Filter_SelectsSegmentsConnectedDevicesHeadsAndSharedRisers()
        {
            var model = DrawingModelReader.Read(new[]
            {
                Line("ОПС", P(0, 0), P(10, 0)),
                Line("ЭС", P(0, 5), P(10, 5)),
                RawEntity.Block("H1", P(0, 0), DeviceXData("ППК-1")),
                Device("ИП-1", P(10, 0), "ОПС", "ППК-1", "ШС1"),
                Device("Р-1", P(10, 5), "ЭС", "ЩР-1", "Гр.1"),
                Riser("Ст", P(5, 0), "Ст-1", 0),
                Riser("Ст", P(5, 5), "Ст-2", 0, superLine: "ЭС"),
            });
            var participants = GraphParticipantFilter.Filter(model, "ОПС");
            Assert.Single(participants.Segments);
            Assert.Equal(new[] { "ППК-1", "ИП-1", "Ст" }, participants.Devices.Select(d => d.Name));
            Assert.Equal("Ст-1", participants.Devices[2].RiserName);
        }
    }
}
