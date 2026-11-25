using System.Collections.Generic;
using VelecCableRouter.Core.Geometry;
using VelecCableRouter.Core.XData;

namespace VelecCableRouter.Core.Model
{
    public enum RawEntityKind
    {
        Other,
        Line,
        Polyline,
        BlockReference,
    }

    /// <summary>
    /// What the AutoCAD layer reads from one selected entity. It carries no AutoCAD types, so the whole
    /// pipeline can be tested without AutoCAD.
    /// </summary>
    public sealed class RawEntity
    {
        public RawEntity(string handle, RawEntityKind kind, IEnumerable<XDataValue>? xdata)
        {
            Handle = handle;
            Kind = kind;
            XData = xdata == null ? new List<XDataValue>() : new List<XDataValue>(xdata);
        }

        public string Handle { get; }

        public RawEntityKind Kind { get; }

        public IReadOnlyList<XDataValue> XData { get; }

        public string Layer { get; set; } = "0";

        /// <summary>Line: start and end; Polyline: vertices in order (XY only).</summary>
        public IReadOnlyList<Point2D> Vertices { get; set; } = new List<Point2D>();

        public bool IsClosed { get; set; }

        /// <summary>The polyline has arc segments; they are routed along their chords.</summary>
        public bool HasArcs { get; set; }

        /// <summary>BlockReference.Position.</summary>
        public Point2D? Insertion { get; set; }

        /// <summary>World position of the nested CONNECTOR_POINT / CONNECTOR_SQUARE, when found.</summary>
        public Point2D? Connector { get; set; }

        /// <summary>Geometric extents of the block reference.</summary>
        public Bounds2D? Extents { get; set; }

        public string? BlockName { get; set; }

        public static RawEntity Line(string handle, Point2D start, Point2D end, IEnumerable<XDataValue>? xdata, string layer = "0") =>
            new RawEntity(handle, RawEntityKind.Line, xdata) { Vertices = new[] { start, end }, Layer = layer };

        public static RawEntity Polyline(string handle, IEnumerable<Point2D> vertices, IEnumerable<XDataValue>? xdata, bool closed = false, string layer = "0") =>
            new RawEntity(handle, RawEntityKind.Polyline, xdata) { Vertices = new List<Point2D>(vertices), IsClosed = closed, Layer = layer };

        public static RawEntity Block(string handle, Point2D insertion, IEnumerable<XDataValue>? xdata, Point2D? connector = null, Bounds2D? extents = null, string layer = "0") =>
            new RawEntity(handle, RawEntityKind.BlockReference, xdata) { Insertion = insertion, Connector = connector, Extents = extents, Layer = layer };
    }
}
