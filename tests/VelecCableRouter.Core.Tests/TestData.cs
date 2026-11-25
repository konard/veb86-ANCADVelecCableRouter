using System;
using System.Collections.Generic;
using System.Linq;
using VelecCableRouter.Core.Geometry;
using VelecCableRouter.Core.Model;
using VelecCableRouter.Core.XData;

namespace VelecCableRouter.Core.Tests
{
    /// <summary>Builders of BobrovXDATA v2 objects as AutoCAD would return them.</summary>
    internal static class TestData
    {
        private static int handle = 0x100;

        public static Point2D P(double x, double y) => new Point2D(x, y);

        public static string NextHandle() => (handle++).ToString("X");

        public static List<XDataValue> Header(string template)
        {
            return new List<XDataValue>
            {
                XDataValue.App(BobrovXData.RegAppName),
                XDataValue.Int16(2),
                XDataValue.Str("DEVICE_ID"), XDataValue.Str(Guid.NewGuid().ToString("D")),
                XDataValue.Str("TEMPLATE"), XDataValue.Str(template),
            };
        }

        public static List<XDataValue> SuperLineXData(string? name)
        {
            var values = Header("SuperLine");
            if (name != null)
            {
                values.Add(XDataValue.Str("NMO_Name"));
                values.Add(XDataValue.Str(name));
            }

            return values;
        }

        public static List<XDataValue> DeviceXData(string? name, IEnumerable<(string SuperLine, string? Head, string? Group)>? connections = null, string? riser = null, double? elevation = null)
        {
            var values = Header("Device");
            if (name != null)
            {
                values.Add(XDataValue.Str("NMO_Name"));
                values.Add(XDataValue.Str(name));
            }

            var index = 1;
            foreach (var (superLine, head, group) in connections ?? Enumerable.Empty<(string, string?, string?)>())
            {
                values.Add(XDataValue.Str($"conn_{index}_SuperLine"));
                values.Add(XDataValue.Str(superLine));
                if (head != null)
                {
                    values.Add(XDataValue.Str($"conn_{index}_HeadDevice"));
                    values.Add(XDataValue.Str(head));
                }

                if (group != null)
                {
                    values.Add(XDataValue.Str($"conn_{index}_Group"));
                    values.Add(XDataValue.Str(group));
                }

                index++;
            }

            if (riser != null)
            {
                values.Add(XDataValue.Str("RiserName"));
                values.Add(XDataValue.Str(riser));
            }

            if (elevation != null)
            {
                values.Add(XDataValue.Str("Elevation"));
                values.Add(XDataValue.Real(elevation.Value));
            }

            return values;
        }

        public static RawEntity Line(string superLine, Point2D a, Point2D b, string layer = "Трассы") =>
            RawEntity.Line(NextHandle(), a, b, SuperLineXData(superLine), layer);

        public static RawEntity Polyline(string superLine, params Point2D[] points) =>
            RawEntity.Polyline(NextHandle(), points, SuperLineXData(superLine), false, "Трассы");

        /// <summary>A device block 2×2 around <paramref name="connector"/> connected to one group.</summary>
        public static RawEntity Device(string name, Point2D connector, string superLine, string? head, string? group, double size = 1) =>
            RawEntity.Block(NextHandle(), connector, DeviceXData(name, new[] { (superLine, head, group) }), connector, Bounds2D.Around(connector, size));

        public static RawEntity Head(string name, Point2D connector, string superLine, double size = 1) =>
            RawEntity.Block(NextHandle(), connector, DeviceXData(name, new[] { (superLine, (string?)null, (string?)null) }), connector, Bounds2D.Around(connector, size));

        public static RawEntity Riser(string name, Point2D connector, string riser, double elevation, string? superLine = null, double size = 1)
        {
            var connections = superLine == null ? null : new[] { (superLine, (string?)null, (string?)null) };
            return RawEntity.Block(NextHandle(), connector, DeviceXData(name, connections, riser, elevation), connector, Bounds2D.Around(connector, size));
        }
    }
}
