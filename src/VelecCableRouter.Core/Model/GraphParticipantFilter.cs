using System;
using System.Collections.Generic;
using System.Linq;

namespace VelecCableRouter.Core.Model
{
    /// <summary>Segments and devices that take part in the graph of one superline.</summary>
    public sealed class GraphParticipants
    {
        public GraphParticipants(string superLineName, IReadOnlyList<SuperLineSegment> segments, IReadOnlyList<DeviceInfo> devices)
        {
            SuperLineName = superLineName;
            Segments = segments;
            Devices = devices;
        }

        public string SuperLineName { get; }

        public IReadOnlyList<SuperLineSegment> Segments { get; }

        public IReadOnlyList<DeviceInfo> Devices { get; }
    }

    /// <summary>
    /// Selects the participants of the graph for <c>currentSuperLineName</c> (issue #1, section 4.2).
    /// Names are compared exactly (ordinal), like the string comparison in uzvcom.pas.
    /// </summary>
    public static class GraphParticipantFilter
    {
        /// <summary>
        /// A segment takes part when its NMO_Name equals the superline name. A device takes part when
        /// <list type="bullet">
        /// <item>one of its connections refers to the superline, or</item>
        /// <item>it is the head device (HeadDeviceName) of a connection to the superline, or</item>
        /// <item>it is a riser (RiserName set) without connections: risers are shared by all systems.</item>
        /// </list>
        /// </summary>
        public static GraphParticipants Filter(DrawingModel model, string superLineName)
        {
            var segments = model.Segments.Where(s => string.Equals(s.Name, superLineName, StringComparison.Ordinal)).ToList();
            var heads = new HashSet<string>(
                model.Devices
                    .SelectMany(d => d.Connections)
                    .Where(c => IsFor(c, superLineName) && c.HeadDeviceName != null)
                    .Select(c => c.HeadDeviceName!),
                StringComparer.Ordinal);
            var devices = model.Devices
                .Where(d => d.Connections.Any(c => IsFor(c, superLineName))
                            || (d.Name != null && heads.Contains(d.Name))
                            || (d.RiserName != null && d.Connections.Count == 0))
                .ToList();
            return new GraphParticipants(superLineName, segments, devices);
        }

        public static bool IsFor(DeviceConnection connection, string superLineName) =>
            string.Equals(connection.SuperLineName, superLineName, StringComparison.Ordinal);
    }
}
