using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using VelecCableRouter.Core;
using VelecCableRouter.Core.Geometry;
using VelecCableRouter.Core.Model;
using VelecCableRouter.Core.Reporting;
using VelecCableRouter.Core.XData;

// Runs VELEC_BUILD_CABLES without AutoCAD on a sample floor plan and writes the report, the cable XDATA and an SVG.
// Usage: dotnet run --project examples/VelecCableRouter.Sample -- [output directory]
var output = args.Length > 0 ? args[0] : "sample-output";
Directory.CreateDirectory(output);

var handle = 0x200;
string NextHandle() => (handle++).ToString("X", CultureInfo.InvariantCulture);
Point2D P(double x, double y) => new Point2D(x, y);

List<XDataValue> Header(string template) => new List<XDataValue>
{
    XDataValue.App(BobrovXData.RegAppName), XDataValue.Int16(BobrovXData.FormatVersion),
    XDataValue.Str("DEVICE_ID"), XDataValue.Str(Guid.NewGuid().ToString("D")),
    XDataValue.Str("TEMPLATE"), XDataValue.Str(template),
};

RawEntity Route(string superLine, params Point2D[] points)
{
    var xdata = Header("SuperLine");
    xdata.Add(XDataValue.Str("NMO_Name"));
    xdata.Add(XDataValue.Str(superLine));
    return points.Length == 2
        ? RawEntity.Line(NextHandle(), points[0], points[1], xdata, "Трассы")
        : RawEntity.Polyline(NextHandle(), points, xdata, false, "Трассы");
}

RawEntity Device(string name, Point2D at, string superLine, string? head, string? group, string? riser = null, double? elevation = null)
{
    var xdata = Header("Device");
    xdata.AddRange(new[] { XDataValue.Str("NMO_Name"), XDataValue.Str(name), XDataValue.Str("conn_1_SuperLine"), XDataValue.Str(superLine) });
    if (head != null)
    {
        xdata.AddRange(new[] { XDataValue.Str("conn_1_HeadDevice"), XDataValue.Str(head) });
    }

    if (group != null)
    {
        xdata.AddRange(new[] { XDataValue.Str("conn_1_Group"), XDataValue.Str(group) });
    }

    if (riser != null)
    {
        xdata.AddRange(new[] { XDataValue.Str("RiserName"), XDataValue.Str(riser), XDataValue.Str("Elevation"), XDataValue.Real(elevation ?? 0) });
    }

    return RawEntity.Block(NextHandle(), at, xdata, at, Bounds2D.Around(at, 1.5), "Устройства");
}

var entities = new List<RawEntity>
{
    // Fire alarm superline: a corridor with two side branches.
    Route("ОПС", P(0, 0), P(30, 0), P(60, 0)),
    Route("ОПС", P(20, 0.3), P(20, 25)),
    Route("ОПС", P(45, 0), P(45, -20)),
    Device("ППК-1", P(0, 0), "ОПС", null, null),
    Device("ИП-1", P(20, 12), "ОПС", "ППК-1", "ШС1"),
    Device("ИП-2", P(20, 25), "ОПС", "ППК-1", "ШС1"),
    Device("ИП-3", P(60, 0), "ОПС", "ППК-1", "ШС1"),
    Device("ИП-4", P(45, -20), "ОПС", "ППК-1", "ШС2"),
    Device("ИП-5", P(70, 25), "ОПС", "ППК-1", "ШС2"), // not on any route: reported as an error
    // Lighting superline on two floors linked by the riser Ст-1.
    Route("ЭС", P(0, 50), P(30, 50)),
    Route("ЭС", P(90, 50), P(120, 50), P(120, 70)),
    Device("ЩР-1", P(0, 50), "ЭС", null, null),
    Device("Ст-1 эт.1", P(30, 50), "ЭС", null, null, "Ст-1", 0),
    Device("Ст-1 эт.2", P(90, 50), "ЭС", null, null, "Ст-1", 3.3),
    Device("Св-1", P(105, 50), "ЭС", "ЩР-1", "Гр.1"),
    Device("Св-2", P(120, 70), "ЭС", "ЩР-1", "Гр.1"),
};

var options = new CableBuildOptions();
var result = CableBuildService.Build(entities, options);
var report = ReportFormatter.Format(result, result.Cables.Count());
File.WriteAllLines(Path.Combine(output, "report.txt"), report, new UTF8Encoding(false));
foreach (var line in report)
{
    Console.WriteLine(line);
}

var xdataDump = new StringBuilder();
foreach (var cable in result.Cables)
{
    var values = BobrovXData.BuildCable(cable.SuperLineName, cable.HeadDeviceName, cable.GroupName, Guid.NewGuid());
    xdataDump.AppendLine("Кабель " + string.Join(" → ", cable.Points));
    foreach (var value in values)
    {
        xdataDump.AppendLine(string.Format(CultureInfo.InvariantCulture, "  ({0} . {1})", value.TypeCode, value.Value));
    }
}

File.WriteAllText(Path.Combine(output, "cables-xdata.txt"), xdataDump.ToString(), new UTF8Encoding(false));
File.WriteAllText(Path.Combine(output, "plan.svg"), SvgPlan.Render(result, entities, options.Epsilon), new UTF8Encoding(false));
Console.WriteLine("Записано: " + Path.GetFullPath(output));

// Self-check so that CI fails if the sample stops producing the expected result.
var expectedCables = 5;
var errors = result.SuperLines.Sum(s => s.ErrorCount);
if (result.Cables.Count() != expectedCables || errors != 1)
{
    Console.Error.WriteLine($"Ожидалось кабелей {expectedCables} и 1 ошибка, получено {result.Cables.Count()} и {errors}.");
    return 1;
}

return 0;

internal static class SvgPlan
{
    private static readonly string[] Palette = { "#1f77b4", "#d62728", "#2ca02c", "#9467bd", "#ff7f0e", "#8c564b" };

    public static string Render(CableBuildResult result, IReadOnlyList<RawEntity> entities, double epsilon)
    {
        const double scale = 6;
        const double margin = 30;
        var points = result.Model.Segments.SelectMany(s => new[] { s.Start, s.End }).Concat(result.Model.Devices.Select(d => d.Connector)).ToList();
        var minX = points.Min(p => p.X);
        var maxX = points.Max(p => p.X);
        var minY = points.Min(p => p.Y);
        var maxY = points.Max(p => p.Y);
        string X(double x) => F(margin + (x - minX) * scale);
        string Y(double y) => F(margin + (maxY - y) * scale);
        var width = (maxX - minX) * scale + 2 * margin + 60;
        var height = (maxY - minY) * scale + 2 * margin + 70;
        var svg = new StringBuilder();
        svg.AppendLine($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{F(width)}\" height=\"{F(height)}\" font-family=\"DejaVu Sans, Arial\" font-size=\"11\">");
        svg.AppendLine($"<rect width=\"100%\" height=\"100%\" fill=\"white\"/>");
        foreach (var segment in result.Model.Segments)
        {
            svg.AppendLine($"<line x1=\"{X(segment.Start.X)}\" y1=\"{Y(segment.Start.Y)}\" x2=\"{X(segment.End.X)}\" y2=\"{Y(segment.End.Y)}\" stroke=\"#c8c8c8\" stroke-width=\"9\" stroke-linecap=\"round\"/>");
        }

        foreach (var report in result.SuperLines)
        {
            foreach (var edge in report.Graph.Edges.Where(e => e.IsVirtualRiserEdge))
            {
                var a = report.Graph.Vertices[edge.From].Point;
                var b = report.Graph.Vertices[edge.To].Point;
                svg.AppendLine($"<line x1=\"{X(a.X)}\" y1=\"{Y(a.Y)}\" x2=\"{X(b.X)}\" y2=\"{Y(b.Y)}\" stroke=\"#c000c0\" stroke-width=\"1.5\" stroke-dasharray=\"6 4\"/>");
            }
        }

        var groups = result.Cables.Select(c => (c.HeadDeviceName, c.GroupName)).Distinct().ToList();
        foreach (var cable in result.Cables)
        {
            var color = Palette[groups.IndexOf((cable.HeadDeviceName, cable.GroupName)) % Palette.Length];
            var path = string.Join(" ", cable.Points.Select(p => X(p.X) + "," + Y(p.Y)));
            svg.AppendLine($"<polyline points=\"{path}\" fill=\"none\" stroke=\"{color}\" stroke-width=\"3\"/>");
            foreach (var p in cable.Points)
            {
                svg.AppendLine($"<circle cx=\"{X(p.X)}\" cy=\"{Y(p.Y)}\" r=\"3\" fill=\"{color}\"/>");
            }
        }

        foreach (var device in result.Model.Devices)
        {
            var c = device.Connector;
            var fill = device.RiserName != null ? "#f3d9f3" : device.Connections.Any(k => k.HeadDeviceName != null) ? "#fff7cc" : "#d9ecff";
            svg.AppendLine($"<rect x=\"{F(double.Parse(X(c.X), CultureInfo.InvariantCulture) - 9)}\" y=\"{F(double.Parse(Y(c.Y), CultureInfo.InvariantCulture) - 9)}\" width=\"18\" height=\"18\" fill=\"{fill}\" stroke=\"#333\"/>");
            svg.AppendLine($"<text x=\"{F(double.Parse(X(c.X), CultureInfo.InvariantCulture) + 11)}\" y=\"{F(double.Parse(Y(c.Y), CultureInfo.InvariantCulture) - 11)}\">{Escape(device.DisplayName)}</text>");
        }

        foreach (var error in result.SuperLines.SelectMany(s => s.Diagnostics).Where(d => d.Severity == DiagnosticSeverity.Error && d.Location.HasValue))
        {
            var p = error.Location!.Value;
            var cx = double.Parse(X(p.X), CultureInfo.InvariantCulture);
            var cy = double.Parse(Y(p.Y), CultureInfo.InvariantCulture);
            svg.AppendLine($"<path d=\"M{F(cx - 14)},{F(cy - 14)} L{F(cx + 14)},{F(cy + 14)} M{F(cx - 14)},{F(cy + 14)} L{F(cx + 14)},{F(cy - 14)}\" stroke=\"red\" stroke-width=\"2.5\"/>");
            svg.AppendLine($"<circle cx=\"{F(cx)}\" cy=\"{F(cy)}\" r=\"20\" fill=\"none\" stroke=\"red\" stroke-width=\"1.5\"/>");
        }

        var legendY = (maxY - minY) * scale + 2 * margin + 10;
        svg.AppendLine($"<text x=\"{F(margin)}\" y=\"{F(legendY)}\">Серое — трассы (SuperLine); цветное — кабели по группам; пунктир — виртуальное ребро стояка; красный крест — ошибка.</text>");
        var x = margin;
        foreach (var (head, group) in groups)
        {
            var color = Palette[groups.IndexOf((head, group)) % Palette.Length];
            svg.AppendLine($"<line x1=\"{F(x)}\" y1=\"{F(legendY + 25)}\" x2=\"{F(x + 25)}\" y2=\"{F(legendY + 25)}\" stroke=\"{color}\" stroke-width=\"3\"/>");
            svg.AppendLine($"<text x=\"{F(x + 30)}\" y=\"{F(legendY + 29)}\">{Escape(head + " / " + group)}</text>");
            x += 140;
        }

        svg.AppendLine("</svg>");
        return svg.ToString();
    }

    private static string F(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private static string Escape(string text) => System.Security.SecurityElement.Escape(text) ?? string.Empty;
}
