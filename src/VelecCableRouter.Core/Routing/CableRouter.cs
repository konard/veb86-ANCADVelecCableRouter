using System;
using System.Collections.Generic;
using System.Linq;
using VelecCableRouter.Core.Geometry;
using VelecCableRouter.Core.Graph;
using VelecCableRouter.Core.Model;

namespace VelecCableRouter.Core.Routing
{
    /// <summary>One cable polyline to create in the drawing.</summary>
    public sealed class CableRoute
    {
        public CableRoute(string superLineName, string headDeviceName, string groupName, IReadOnlyList<Point2D> points, string? layer, IReadOnlyList<string> deviceHandles)
        {
            SuperLineName = superLineName;
            HeadDeviceName = headDeviceName;
            GroupName = groupName;
            Points = points;
            Layer = layer;
            DeviceHandles = deviceHandles;
        }

        public string SuperLineName { get; }

        public string HeadDeviceName { get; }

        public string GroupName { get; }

        /// <summary>Simplified 2D polyline vertices (at least two).</summary>
        public IReadOnlyList<Point2D> Points { get; }

        /// <summary>Layer of the superline the cable runs along (used by the "superline layer" option).</summary>
        public string? Layer { get; }

        /// <summary>Group devices whose connectors are vertices of this polyline.</summary>
        public IReadOnlyList<string> DeviceHandles { get; }

        public double Length
        {
            get
            {
                var length = 0.0;
                for (var i = 1; i < Points.Count; i++)
                {
                    length += Points[i - 1].DistanceTo(Points[i]);
                }

                return length;
            }
        }
    }

    /// <summary>Devices of one (HeadDeviceName, GroupName) pair of a superline.</summary>
    public sealed class CableGroup
    {
        public CableGroup(string headDeviceName, string groupName)
        {
            HeadDeviceName = headDeviceName;
            GroupName = groupName;
        }

        public string HeadDeviceName { get; }

        public string GroupName { get; }

        public List<DeviceInfo> Devices { get; } = new List<DeviceInfo>();

        public string DisplayName => $"«{HeadDeviceName}» / «{(GroupName.Length == 0 ? "без группы" : GroupName)}»";
    }

    /// <summary>Routing result for one superline.</summary>
    public sealed class RoutingResult
    {
        public RoutingResult(IReadOnlyList<CableGroup> groups, IReadOnlyList<CableRoute> cables)
        {
            Groups = groups;
            Cables = cables;
        }

        public IReadOnlyList<CableGroup> Groups { get; }

        public IReadOnlyList<CableRoute> Cables { get; }
    }

    /// <summary>
    /// Builds cable polylines inside the graph of one superline (issue #1, section 4.4):
    /// groups devices by (HeadDeviceName, GroupName), finds the head vertex, builds the Dijkstra tree from it to the
    /// group devices, merges adjacent physical edges into polylines (breaking on riser edges and branch points)
    /// and removes collinear vertices.
    /// </summary>
    public static class CableRouter
    {
        public static IReadOnlyList<CableGroup> GroupDevices(GraphParticipants participants)
        {
            var groups = new List<CableGroup>();
            var byKey = new Dictionary<(string Head, string Group), CableGroup>();
            foreach (var device in participants.Devices)
            {
                foreach (var connection in device.Connections)
                {
                    if (!GraphParticipantFilter.IsFor(connection, participants.SuperLineName) || connection.HeadDeviceName == null)
                    {
                        continue;
                    }

                    if (string.Equals(device.Name, connection.HeadDeviceName, StringComparison.Ordinal))
                    {
                        continue; // the head device itself
                    }

                    var key = (connection.HeadDeviceName, connection.GroupName ?? string.Empty);
                    if (!byKey.TryGetValue(key, out var group))
                    {
                        group = new CableGroup(key.HeadDeviceName, key.Item2);
                        byKey.Add(key, group);
                        groups.Add(group);
                    }

                    if (!group.Devices.Contains(device))
                    {
                        group.Devices.Add(device);
                    }
                }
            }

            return groups;
        }

        public static RoutingResult Route(TopologyGraph graph, GraphParticipants participants, ICollection<Diagnostic> diagnostics)
        {
            var groups = GroupDevices(participants);
            var cables = new List<CableRoute>();
            var trees = new Dictionary<int, ShortestPathTree>();
            foreach (var group in groups)
            {
                var heads = participants.Devices.Where(d => string.Equals(d.Name, group.HeadDeviceName, StringComparison.Ordinal)).ToList();
                if (heads.Count == 0)
                {
                    var first = group.Devices[0];
                    diagnostics.Add(Diagnostic.Error($"Группа {group.DisplayName}: головное устройство «{group.HeadDeviceName}» не найдено в выделении; кабели группы не построены.", first.Handle, first.Connector));
                    continue;
                }

                var head = heads[0];
                if (heads.Count > 1)
                {
                    diagnostics.Add(Diagnostic.Warning($"Найдено несколько головных устройств «{group.HeadDeviceName}»; используется первое.", head.Handle, head.Connector));
                }

                var headVertex = graph.VertexOf(head);
                if (!trees.TryGetValue(headVertex, out var tree))
                {
                    tree = Dijkstra.Run(graph, headVertex);
                    trees.Add(headVertex, tree);
                }

                var treeEdges = new HashSet<GraphEdge>();
                foreach (var device in group.Devices)
                {
                    var vertex = graph.VertexOf(device);
                    if (!tree.IsReachable(vertex))
                    {
                        diagnostics.Add(Diagnostic.Error($"Группа {group.DisplayName}: нет пути от «{head.DisplayName}» до устройства «{device.DisplayName}».", device.Handle, device.Connector));
                        continue;
                    }

                    if (vertex == headVertex)
                    {
                        diagnostics.Add(Diagnostic.Warning($"Группа {group.DisplayName}: устройство «{device.DisplayName}» совпадает с точкой подключения головного устройства.", device.Handle, device.Connector));
                        continue;
                    }

                    foreach (var edge in tree.PathTo(vertex))
                    {
                        treeEdges.Add(edge);
                    }
                }

                foreach (var points in BuildPolylines(graph, tree, treeEdges))
                {
                    var simplified = GeometryUtils.SimplifyCollinear(points.Points);
                    if (simplified.Count < 2)
                    {
                        continue;
                    }

                    var handles = points.Vertices
                        .SelectMany(v => graph.Vertices[v].Devices)
                        .Where(group.Devices.Contains)
                        .Select(d => d.Handle)
                        .Distinct()
                        .ToList();
                    cables.Add(new CableRoute(participants.SuperLineName, group.HeadDeviceName, group.GroupName, simplified, points.Layer, handles));
                }
            }

            return new RoutingResult(groups, cables);
        }

        /// <summary>
        /// Splits the routing tree into polylines. A polyline continues through vertices with one child; at a branch
        /// the straightest child continues it and every other child starts a new polyline at the branch point.
        /// A riser edge ends the polyline and is not drawn: a new polyline starts after the riser.
        /// </summary>
        internal static IReadOnlyList<PolylinePath> BuildPolylines(TopologyGraph graph, ShortestPathTree tree, ISet<GraphEdge> treeEdges)
        {
            var children = new Dictionary<int, List<GraphEdge>>();
            foreach (var edge in treeEdges.OrderBy(e => e.Index))
            {
                var child = tree.PreviousEdge[edge.From] == edge ? edge.From : edge.To;
                var parent = edge.Other(child);
                if (!children.TryGetValue(parent, out var list))
                {
                    list = new List<GraphEdge>();
                    children.Add(parent, list);
                }

                list.Add(edge);
            }

            var result = new List<PolylinePath>();
            var pending = new Stack<(int Start, GraphEdge Edge)>();
            foreach (var edge in ChildrenOf(children, tree.Source).Reverse())
            {
                pending.Push((tree.Source, edge));
            }

            while (pending.Count > 0)
            {
                var (start, firstEdge) = pending.Pop();
                if (firstEdge.IsVirtualRiserEdge)
                {
                    var after = firstEdge.Other(start);
                    foreach (var edge in ChildrenOf(children, after).Reverse())
                    {
                        pending.Push((after, edge));
                    }

                    continue;
                }

                var path = new PolylinePath();
                path.Add(graph.Vertices[start]);
                var current = start;
                var edgeToNext = firstEdge;
                while (edgeToNext != null)
                {
                    var next = edgeToNext.Other(current);
                    path.Add(graph.Vertices[next]);
                    path.Layer ??= edgeToNext.Segment?.Layer;
                    var incoming = graph.Vertices[next].Point - graph.Vertices[current].Point;
                    current = next;
                    edgeToNext = null;
                    var physical = new List<GraphEdge>();
                    foreach (var child in ChildrenOf(children, current))
                    {
                        if (child.IsVirtualRiserEdge)
                        {
                            pending.Push((current, child));
                        }
                        else
                        {
                            physical.Add(child);
                        }
                    }

                    if (physical.Count == 0)
                    {
                        break;
                    }

                    var straightest = physical
                        .OrderByDescending(e => Cosine(incoming, graph.Vertices[e.Other(current)].Point - graph.Vertices[current].Point))
                        .ThenBy(e => e.Index)
                        .First();
                    for (var i = physical.Count - 1; i >= 0; i--)
                    {
                        if (physical[i] != straightest)
                        {
                            pending.Push((current, physical[i]));
                        }
                    }

                    edgeToNext = straightest;
                }

                result.Add(path);
            }

            return result;
        }

        private static IEnumerable<GraphEdge> ChildrenOf(Dictionary<int, List<GraphEdge>> children, int vertex) =>
            children.TryGetValue(vertex, out var list) ? list : Enumerable.Empty<GraphEdge>();

        private static double Cosine(Vector2D a, Vector2D b)
        {
            var length = a.Length * b.Length;
            return length == 0 ? -1 : a.Dot(b) / length;
        }

        internal sealed class PolylinePath
        {
            public List<int> Vertices { get; } = new List<int>();

            public List<Point2D> Points { get; } = new List<Point2D>();

            public string? Layer { get; set; }

            public void Add(GraphVertex vertex)
            {
                Vertices.Add(vertex.Index);
                Points.Add(vertex.Point);
            }
        }
    }
}
