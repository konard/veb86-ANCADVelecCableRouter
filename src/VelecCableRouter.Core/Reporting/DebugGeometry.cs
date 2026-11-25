using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using VelecCableRouter.Core.Geometry;
using VelecCableRouter.Core.Graph;
using VelecCableRouter.Core.Model;

namespace VelecCableRouter.Core.Reporting
{
    /// <summary>Names of the diagnostic layers VELEC_DEBUG_&lt;SuperLineName&gt;.</summary>
    public static class DebugLayers
    {
        public const string Prefix = "VELEC_DEBUG_";
        private const string InvalidChars = "<>/\\\":;?*|,=`";

        public static string ForSuperLine(string superLineName)
        {
            var builder = new StringBuilder(Prefix);
            foreach (var c in superLineName.Trim())
            {
                builder.Append(InvalidChars.IndexOf(c) >= 0 || char.IsControl(c) ? '_' : c);
            }

            var name = builder.ToString().TrimEnd();
            return name.Length > 255 ? name.Substring(0, 255) : name;
        }

        public static bool IsDebugLayer(string layerName) =>
            layerName.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>AutoCAD colour indices used by the diagnostic drawing.</summary>
    public static class DebugColors
    {
        public const short Red = 1;
        public const short Yellow = 2;
        public const short Green = 3;
        public const short Cyan = 4;
        public const short Magenta = 6;
    }

    public sealed class DebugCircle
    {
        public DebugCircle(Point2D center, double radius, short color)
        {
            Center = center;
            Radius = radius;
            Color = color;
        }

        public Point2D Center { get; }

        public double Radius { get; }

        public short Color { get; }
    }

    public sealed class DebugLine
    {
        public DebugLine(Point2D start, Point2D end, short color)
        {
            Start = start;
            End = end;
            Color = color;
        }

        public Point2D Start { get; }

        public Point2D End { get; }

        public short Color { get; }
    }

    public sealed class DebugText
    {
        public DebugText(Point2D position, double height, string text, short color)
        {
            Position = position;
            Height = height;
            Text = text;
            Color = color;
        }

        public Point2D Position { get; }

        public double Height { get; }

        public string Text { get; }

        public short Color { get; }
    }

    /// <summary>What to draw on the debug layer of one superline (ZCAD visualGraphVertex/Edge/Error).</summary>
    public sealed class DebugDrawing
    {
        public DebugDrawing(string layerName)
        {
            LayerName = layerName;
        }

        public string LayerName { get; }

        public List<DebugCircle> Circles { get; } = new List<DebugCircle>();

        public List<DebugLine> Lines { get; } = new List<DebugLine>();

        public List<DebugText> Texts { get; } = new List<DebugText>();
    }

    public static class DebugGeometryPlanner
    {
        /// <summary>
        /// Vertices are circles (green: device, yellow: other), edges are lines (cyan: physical, magenta: riser),
        /// error markers are red crosses with a circle and the device name.
        /// </summary>
        public static DebugDrawing Plan(SuperLineReport report, double epsilon)
        {
            var drawing = new DebugDrawing(DebugLayers.ForSuperLine(report.Name));
            var radius = Math.Max(epsilon, 1e-3);
            foreach (var vertex in report.Graph.Vertices)
            {
                drawing.Circles.Add(new DebugCircle(vertex.Point, radius, vertex.Kind == VertexKind.Device ? DebugColors.Green : DebugColors.Yellow));
            }

            foreach (var edge in report.Graph.Edges)
            {
                drawing.Lines.Add(new DebugLine(
                    report.Graph.Vertices[edge.From].Point,
                    report.Graph.Vertices[edge.To].Point,
                    edge.IsVirtualRiserEdge ? DebugColors.Magenta : DebugColors.Cyan));
            }

            foreach (var error in report.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error && d.Location.HasValue))
            {
                var p = error.Location!.Value;
                var size = radius * 4;
                drawing.Circles.Add(new DebugCircle(p, size, DebugColors.Red));
                drawing.Lines.Add(new DebugLine(new Point2D(p.X - size, p.Y - size), new Point2D(p.X + size, p.Y + size), DebugColors.Red));
                drawing.Lines.Add(new DebugLine(new Point2D(p.X - size, p.Y + size), new Point2D(p.X + size, p.Y - size), DebugColors.Red));
                drawing.Texts.Add(new DebugText(new Point2D(p.X + size, p.Y + size), radius * 2, error.Message, DebugColors.Red));
            }

            return drawing;
        }
    }
}
