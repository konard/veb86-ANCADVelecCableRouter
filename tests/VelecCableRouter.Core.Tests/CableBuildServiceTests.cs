using System;
using System.Linq;
using VelecCableRouter.Core.Geometry;
using VelecCableRouter.Core.Model;
using VelecCableRouter.Core.Reporting;
using VelecCableRouter.Core.XData;
using Xunit;
using static VelecCableRouter.Core.Tests.TestData;

namespace VelecCableRouter.Core.Tests
{
    public class CableBuildServiceTests
    {
        private static RawEntity[] TwoSuperLines() => new[]
        {
            Line("ОПС", P(0, 0), P(30, 0)),
            Line("ЭС", P(0, 10), P(30, 10), "Трассы ЭС"),
            Head("ППК-1", P(0, 0), "ОПС"),
            Device("ИП-1", P(15, 0), "ОПС", "ППК-1", "ШС1"),
            Device("ИП-2", P(30, 0), "ОПС", "ППК-1", "ШС1"),
            Head("ЩР-1", P(0, 10), "ЭС"),
            Device("Св-1", P(30, 10), "ЭС", "ЩР-1", "Гр.1"),
        };

        [Fact]
        public void EachSuperLine_IsProcessedIndependently()
        {
            var result = CableBuildService.Build(TwoSuperLines());

            Assert.Equal(new[] { "ОПС", "ЭС" }, result.SuperLines.Select(s => s.Name));
            var ops = result.SuperLines[0];
            Assert.Equal(3, ops.VertexCount);
            Assert.Equal(2, ops.EdgeCount);
            Assert.Equal(new[] { P(0, 0), P(30, 0) }, Assert.Single(ops.Cables).Points);
            var es = Assert.Single(result.SuperLines[1].Cables);
            Assert.Equal(new[] { P(0, 10), P(30, 10) }, es.Points);
            Assert.Equal("Трассы ЭС", es.Layer);
            Assert.Equal("Гр.1", es.GroupName);
            Assert.Equal(0, ops.ErrorCount + ops.WarningCount);
            Assert.Empty(result.Diagnostics);
        }

        [Fact]
        public void CableXData_FollowsContract()
        {
            var cable = CableBuildService.Build(TwoSuperLines()).Cables.First();
            var record = BobrovXData.Parse(BobrovXData.BuildCable(cable.SuperLineName, cable.HeadDeviceName, cable.GroupName, Guid.NewGuid()));

            Assert.Equal(BobrovXDataFormat.V2, record.Format);
            Assert.True(record.IsTemplate(BobrovXData.TemplateCable));
            Assert.Equal("ОПС", record.GetString("NMO_Name"));
            Assert.Equal("ОПС", record.GetString("SuperLineName"));
            Assert.Equal("ППК-1", record.GetString("HeadDeviceName"));
            Assert.Equal("ШС1", record.GetString("GroupName"));
        }

        [Fact]
        public void Report_ListsSuperLinesGroupsAndTotals()
        {
            var lines = ReportFormatter.Format(CableBuildService.Build(TwoSuperLines()), 2);

            Assert.Contains("Суперлиния «ОПС»: вершин 3, рёбер 2, групп 1, кабелей 1, предупреждений 0, ошибок 0.", lines);
            Assert.Contains("  Группа «ППК-1» / «ШС1»: устройств 2, полилиний 1, длина 30.", lines);
            Assert.Equal("Итого: суперлиний 2, кабелей 2, предупреждений 0, ошибок 0.", lines.Last());
        }

        [Fact]
        public void Report_ShowsDiagnosticsWithHandles()
        {
            var lost = Device("ИП-9", P(90, 90), "ОПС", "ППК-1", "ШС1");
            var result = CableBuildService.Build(TwoSuperLines().Append(lost));
            var lines = ReportFormatter.Format(result, 1);

            Assert.Contains(lines, l => l.StartsWith("  Ошибка: ", StringComparison.Ordinal) && l.Contains("ИП-9") && l.Contains("[handle " + lost.Handle + "]"));
            Assert.EndsWith("кабелей 2 (создано 1), предупреждений 0, ошибок 1.", lines.Last());
        }

        [Fact]
        public void DeviceOfSuperLineOutsideSelection_IsReported()
        {
            var result = CableBuildService.Build(TwoSuperLines().Append(Device("ИП-5", P(5, 5), "СКС", "К-1", "1")));
            Assert.Contains(result.Diagnostics, d => d.Severity == DiagnosticSeverity.Warning && d.Message.Contains("СКС"));
        }

        [Fact]
        public void NoSuperLines_IsReported()
        {
            var result = CableBuildService.Build(new[] { Head("ППК-1", P(0, 0), "ОПС") });
            Assert.Empty(result.SuperLines);
            Assert.Contains(result.Diagnostics, d => d.Message.Contains("нет трасс"));
        }

        [Fact]
        public void LargeTolerance_ConnectsDistantDevice()
        {
            var entities = new[]
            {
                Line("ОПС", P(0, 0), P(30, 0)),
                Head("ППК-1", P(0, 0), "ОПС"),
                Device("ИП-1", P(15, 2), "ОПС", "ППК-1", "ШС1"),
            };

            Assert.Equal(1, CableBuildService.Build(entities).SuperLines[0].ErrorCount);
            var report = CableBuildService.Build(entities, new CableBuildOptions { Epsilon = 3 }).SuperLines[0];
            Assert.Equal(0, report.ErrorCount);
            Assert.Equal(new[] { P(0, 0), P(15, 2) }, Assert.Single(report.Cables).Points);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        public void InvalidTolerance_Throws(double epsilon)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => CableBuildService.Build(TwoSuperLines(), new CableBuildOptions { Epsilon = epsilon }));
        }

        [Fact]
        public void DebugLayerNames_AreSanitized()
        {
            Assert.Equal("VELEC_DEBUG_ОПС", DebugLayers.ForSuperLine("ОПС"));
            Assert.Equal("VELEC_DEBUG_A_B_C", DebugLayers.ForSuperLine("A/B:C"));
            Assert.True(DebugLayers.IsDebugLayer("velec_debug_x"));
            Assert.False(DebugLayers.IsDebugLayer("Трассы"));
            Assert.True(DebugLayers.ForSuperLine(new string('x', 400)).Length <= 255);
        }

        [Fact]
        public void DebugPlan_DrawsVerticesEdgesAndErrorMarkers()
        {
            var entities = TwoSuperLines().Append(Device("ИП-9", P(90, 90), "ОПС", "ППК-1", "ШС1")).ToArray();
            var report = CableBuildService.Build(entities).SuperLines[0];
            var drawing = DebugGeometryPlanner.Plan(report, 0.5);

            Assert.Equal("VELEC_DEBUG_ОПС", drawing.LayerName);
            Assert.Equal(report.VertexCount + 1, drawing.Circles.Count);
            Assert.Equal(report.VertexCount, drawing.Circles.Count(c => c.Color == DebugColors.Green)); // all vertices are device vertices here
            Assert.Equal(report.EdgeCount + 2, drawing.Lines.Count);
            var text = Assert.Single(drawing.Texts);
            Assert.Contains("ИП-9", text.Text);
            Assert.Equal(DebugColors.Red, text.Color);
        }
    }
}
