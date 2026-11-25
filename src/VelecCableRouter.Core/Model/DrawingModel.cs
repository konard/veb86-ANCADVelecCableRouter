using System;
using System.Collections.Generic;
using VelecCableRouter.Core.Geometry;

namespace VelecCableRouter.Core.Model
{
    /// <summary>One straight piece of a superline (a Line or one segment of a Polyline).</summary>
    public sealed class SuperLineSegment
    {
        public SuperLineSegment(string handle, int index, string name, Point2D start, Point2D end, string layer)
        {
            Handle = handle;
            Index = index;
            Name = name;
            Start = start;
            End = end;
            Layer = layer;
        }

        public string Handle { get; }

        /// <summary>Segment number inside the source polyline (0 for a Line).</summary>
        public int Index { get; }

        /// <summary>NMO_Name of the superline (cable system), e.g. "ОПС".</summary>
        public string Name { get; }

        public Point2D Start { get; }

        public Point2D End { get; }

        public string Layer { get; }

        public double Length => Start.DistanceTo(End);

        public override string ToString() => $"{Name} {Handle}#{Index} {Start}-{End}";
    }

    /// <summary>One connection of a device: superline, head device and group (loop) of the head device.</summary>
    public sealed class DeviceConnection : IEquatable<DeviceConnection>
    {
        public DeviceConnection(string superLineName, string? headDeviceName, string? groupName, string source)
        {
            SuperLineName = superLineName;
            HeadDeviceName = headDeviceName;
            GroupName = groupName;
            Source = source;
        }

        public string SuperLineName { get; }

        public string? HeadDeviceName { get; }

        public string? GroupName { get; }

        /// <summary>Where it was read from: "conn_1" for a repeat item, "SuperLineName" for scalar fields.</summary>
        public string Source { get; }

        public bool Equals(DeviceConnection? other) =>
            other != null
            && string.Equals(SuperLineName, other.SuperLineName, StringComparison.Ordinal)
            && string.Equals(HeadDeviceName ?? string.Empty, other.HeadDeviceName ?? string.Empty, StringComparison.Ordinal)
            && string.Equals(GroupName ?? string.Empty, other.GroupName ?? string.Empty, StringComparison.Ordinal);

        public override bool Equals(object? obj) => Equals(obj as DeviceConnection);

        public override int GetHashCode() =>
            unchecked((SuperLineName.GetHashCode() * 397) ^ (HeadDeviceName ?? string.Empty).GetHashCode() ^ ((GroupName ?? string.Empty).GetHashCode() * 31));

        public override string ToString() => $"{SuperLineName}/{HeadDeviceName}/{GroupName}";
    }

    /// <summary>A device (TEMPLATE "Device") read from a BlockReference.</summary>
    public sealed class DeviceInfo
    {
        public DeviceInfo(string handle, string? name, Point2D connector, Point2D insertion, IReadOnlyList<DeviceConnection> connections)
        {
            Handle = handle;
            Name = name;
            Connector = connector;
            Insertion = insertion;
            Connections = connections;
        }

        public string Handle { get; }

        /// <summary>NMO_Name, e.g. "ППК-1" or "ИП-1.1".</summary>
        public string? Name { get; }

        /// <summary>CONNECTOR_POINT/CONNECTOR_SQUARE world point or the insertion point (Z = 0).</summary>
        public Point2D Connector { get; }

        public Point2D Insertion { get; }

        public Bounds2D? Extents { get; set; }

        public string? RiserName { get; set; }

        public double? Elevation { get; set; }

        public string Layer { get; set; } = "0";

        public IReadOnlyList<DeviceConnection> Connections { get; }

        public string DisplayName => string.IsNullOrEmpty(Name) ? "<без имени " + Handle + ">" : Name!;

        public override string ToString() => $"{DisplayName} {Connector}";
    }

    /// <summary>Superline segments and devices found in the selection.</summary>
    public sealed class DrawingModel
    {
        public DrawingModel(IReadOnlyList<SuperLineSegment> segments, IReadOnlyList<DeviceInfo> devices, IReadOnlyList<Diagnostic> diagnostics)
        {
            Segments = segments;
            Devices = devices;
            Diagnostics = diagnostics;
        }

        public IReadOnlyList<SuperLineSegment> Segments { get; }

        public IReadOnlyList<DeviceInfo> Devices { get; }

        /// <summary>Problems not tied to one superline (unnamed segments, v1 data, wrong geometry...).</summary>
        public IReadOnlyList<Diagnostic> Diagnostics { get; }
    }
}
