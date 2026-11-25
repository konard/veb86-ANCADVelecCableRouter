using System;
using System.Collections.Generic;
using System.Linq;
using VelecCableRouter.Core.Geometry;
using VelecCableRouter.Core.Model;

namespace VelecCableRouter.Core.Graph
{
    /// <summary>
    /// Port of uzvcom.pas <c>graphBulderFunc</c> (issue #1, section 4.3). Steps keep the ZCAD order:
    /// <list type="number">
    /// <item>device vertices at connector points (duplicates inside the eps box are merged);</item>
    /// <item><c>getListEdgeBreak</c>: virtual edges between risers with the same RiserName sorted by Elevation;</item>
    /// <item>intersections of the segments, each lengthened by eps (<c>extendedLineFunc</c> + <c>intercept3d</c>);</item>
    /// <item><c>getVertexConnectSL</c>: segment ends touching other segment ends;</item>
    /// <item><c>getListDeviceAndEdge</c>: segment ends inside a device get a tap edge to its connector;</item>
    /// <item><c>getListEdge</c>: vertices inside the eps rectangle of each segment, sorted along it, are chained by edges.</item>
    /// </list>
    /// </summary>
    public static class GraphBuilder
    {
        public static TopologyGraph Build(GraphParticipants participants, double epsilon, ICollection<Diagnostic>? diagnostics = null)
        {
            if (!(epsilon > 0) || double.IsInfinity(epsilon))
            {
                throw new ArgumentOutOfRangeException(nameof(epsilon), epsilon, "Epsilon must be a positive number.");
            }

            var graph = new TopologyGraph(participants.SuperLineName);
            var segments = participants.Segments;

            AddDeviceVertices(graph, participants.Devices, epsilon);
            AddRiserEdges(graph, participants.Devices, diagnostics);
            AddIntersectionVertices(graph, segments, epsilon);
            AddConnectedEndVertices(graph, segments, epsilon);
            AddDeviceTapEdges(graph, segments, participants.Devices, epsilon);
            AddSegmentEdges(graph, segments, epsilon);
            return graph;
        }

        private static int AddVertexIfNew(TopologyGraph graph, Point2D point, VertexKind kind, double epsilon)
        {
            var existing = graph.FindVertex(point, epsilon);
            return existing >= 0 ? existing : graph.AddVertex(point, kind);
        }

        private static void AddDeviceVertices(TopologyGraph graph, IReadOnlyList<DeviceInfo> devices, double epsilon)
        {
            foreach (var device in devices)
            {
                // ZCAD drops a second device inside the eps box; here it shares the vertex, so it can still be routed.
                var vertex = AddVertexIfNew(graph, device.Connector, VertexKind.Device, epsilon);
                graph.AttachDevice(vertex, device);
            }
        }

        private static void AddRiserEdges(TopologyGraph graph, IReadOnlyList<DeviceInfo> devices, ICollection<Diagnostic>? diagnostics)
        {
            var risers = devices
                .Where(d => d.RiserName != null)
                .Select((device, order) => (device, order))
                .GroupBy(x => x.device.RiserName!, StringComparer.Ordinal);
            foreach (var group in risers)
            {
                var sorted = group
                    .OrderBy(x => x.device.Elevation.HasValue ? 0 : 1)
                    .ThenBy(x => x.device.Elevation ?? 0)
                    .ThenBy(x => x.order)
                    .Select(x => x.device)
                    .ToList();
                if (sorted.Count < 2)
                {
                    diagnostics?.Add(Diagnostic.Warning($"Стояк «{group.Key}» найден только на одном устройстве «{sorted[0].DisplayName}»: переход не построен.", sorted[0].Handle, sorted[0].Connector));
                    continue;
                }

                foreach (var device in sorted.Where(d => d.Elevation == null))
                {
                    diagnostics?.Add(Diagnostic.Warning($"У стояка «{group.Key}» устройства «{device.DisplayName}» нет Elevation: оно поставлено после устройств с отметкой.", device.Handle, device.Connector));
                }

                for (var i = 0; i + 1 < sorted.Count; i++)
                {
                    var a = sorted[i];
                    var b = sorted[i + 1];
                    var weight = a.Elevation.HasValue && b.Elevation.HasValue ? Math.Abs(b.Elevation.Value - a.Elevation.Value) : 1.0;
                    graph.AddEdge(graph.VertexOf(a), graph.VertexOf(b), weight, EdgeKind.Riser, null);
                }
            }
        }

        private static void AddIntersectionVertices(TopologyGraph graph, IReadOnlyList<SuperLineSegment> segments, double epsilon)
        {
            for (var i = 0; i < segments.Count; i++)
            {
                var main = GeometryUtils.Extend(segments[i].Start, segments[i].End, epsilon);
                for (var j = i + 1; j < segments.Count; j++)
                {
                    var next = GeometryUtils.Extend(segments[j].Start, segments[j].End, epsilon);
                    if (GeometryUtils.TryIntersect(main.Start, main.End, next.Start, next.End, out var point))
                    {
                        AddVertexIfNew(graph, point, VertexKind.Intersection, epsilon);
                    }
                }
            }
        }

        private static void AddConnectedEndVertices(TopologyGraph graph, IReadOnlyList<SuperLineSegment> segments, double epsilon)
        {
            for (var i = 0; i < segments.Count; i++)
            {
                for (var j = 0; j < segments.Count; j++)
                {
                    if (i == j)
                    {
                        continue;
                    }

                    foreach (var end in Ends(segments[j]))
                    {
                        if (Ends(segments[i]).Any(other => GeometryUtils.IsWithinBox(end, other, epsilon)))
                        {
                            AddVertexIfNew(graph, end, VertexKind.SegmentEnd, epsilon);
                        }
                    }
                }
            }
        }

        private static void AddDeviceTapEdges(TopologyGraph graph, IReadOnlyList<SuperLineSegment> segments, IReadOnlyList<DeviceInfo> devices, double epsilon)
        {
            foreach (var segment in segments)
            {
                foreach (var end in Ends(segment))
                {
                    var area = Bounds2D.Around(end, epsilon);
                    DeviceInfo? nearest = null;
                    var nearestDistance = double.MaxValue;
                    foreach (var device in devices)
                    {
                        var box = device.Extents ?? Bounds2D.Around(device.Connector, epsilon);
                        if (!box.Intersects(area) && !area.Contains(device.Connector))
                        {
                            continue;
                        }

                        // ZCAD picks the device with the nearest insertion point when several are found.
                        var distance = end.DistanceTo(device.Insertion);
                        if (distance < nearestDistance)
                        {
                            nearest = device;
                            nearestDistance = distance;
                        }
                    }

                    if (nearest == null)
                    {
                        continue;
                    }

                    var deviceVertex = graph.VertexOf(nearest);
                    var endVertex = AddVertexIfNew(graph, end, VertexKind.SegmentEnd, epsilon);
                    if (endVertex != deviceVertex)
                    {
                        graph.AddEdge(endVertex, deviceVertex, end.DistanceTo(graph.Vertices[deviceVertex].Point), EdgeKind.DeviceTap, null);
                    }
                }
            }
        }

        private static void AddSegmentEdges(TopologyGraph graph, IReadOnlyList<SuperLineSegment> segments, double epsilon)
        {
            foreach (var segment in segments)
            {
                var onSegment = new List<(int Vertex, double Distance)>();
                foreach (var vertex in graph.Vertices)
                {
                    if (GeometryUtils.IsInSegmentArea(vertex.Point, segment.Start, segment.End, epsilon, out var distance))
                    {
                        onSegment.Add((vertex.Index, distance));
                    }
                }

                onSegment.Sort((a, b) => a.Distance != b.Distance ? a.Distance.CompareTo(b.Distance) : a.Vertex.CompareTo(b.Vertex));
                for (var i = 0; i + 1 < onSegment.Count; i++)
                {
                    var from = onSegment[i].Vertex;
                    var to = onSegment[i + 1].Vertex;
                    graph.AddEdge(from, to, graph.Vertices[from].Point.DistanceTo(graph.Vertices[to].Point), EdgeKind.Segment, segment);
                }
            }
        }

        private static IEnumerable<Point2D> Ends(SuperLineSegment segment)
        {
            yield return segment.Start;
            yield return segment.End;
        }
    }
}
