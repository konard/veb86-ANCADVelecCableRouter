using System.Collections.Generic;
using System.Linq;
using VelecCableRouter.Core.Geometry;
using VelecCableRouter.Core.Model;

namespace VelecCableRouter.Core.Graph
{
    public enum VertexKind
    {
        /// <summary>Connector point of one or more devices.</summary>
        Device,

        /// <summary>Intersection of two superline segments.</summary>
        Intersection,

        /// <summary>Segment end joined with another segment end or with a device.</summary>
        SegmentEnd,
    }

    public enum EdgeKind
    {
        /// <summary>Part of a superline segment between two neighbouring vertices.</summary>
        Segment,

        /// <summary>Tap from a segment end inside a device to the device connector (ZCAD: cableEnt = nil).</summary>
        DeviceTap,

        /// <summary>Virtual edge between risers with the same RiserName (floor transition).</summary>
        Riser,
    }

    public sealed class GraphVertex
    {
        private readonly List<DeviceInfo> devices = new List<DeviceInfo>();

        internal GraphVertex(int index, Point2D point, VertexKind kind)
        {
            Index = index;
            Point = point;
            Kind = kind;
        }

        public int Index { get; }

        public Point2D Point { get; }

        public VertexKind Kind { get; internal set; }

        /// <summary>Devices whose connectors fall into this vertex (usually one).</summary>
        public IReadOnlyList<DeviceInfo> Devices => devices;

        internal void AddDevice(DeviceInfo device)
        {
            devices.Add(device);
            Kind = VertexKind.Device;
        }

        public override string ToString() => $"#{Index} {Kind} {Point}" + (devices.Count == 0 ? string.Empty : " " + string.Join(",", devices.Select(d => d.DisplayName)));
    }

    public sealed class GraphEdge
    {
        internal GraphEdge(int index, int from, int to, double weight, EdgeKind kind, SuperLineSegment? segment)
        {
            Index = index;
            From = from;
            To = to;
            Weight = weight;
            Kind = kind;
            Segment = segment;
        }

        public int Index { get; }

        public int From { get; }

        public int To { get; }

        public double Weight { get; internal set; }

        public EdgeKind Kind { get; }

        /// <summary>Riser transition: a cable polyline is broken on such an edge (issue #1, section 4.4).</summary>
        public bool IsVirtualRiserEdge => Kind == EdgeKind.Riser;

        /// <summary>Source superline segment (null for taps and risers).</summary>
        public SuperLineSegment? Segment { get; }

        public int Other(int vertex) => vertex == From ? To : From;

        public override string ToString() => $"{From}-{To} {Kind} {Weight:0.###}";
    }

    /// <summary>Topology graph of one superline (ZCAD <c>TGraphBuilder</c>).</summary>
    public sealed class TopologyGraph
    {
        private readonly List<GraphVertex> vertices = new List<GraphVertex>();
        private readonly List<GraphEdge> edges = new List<GraphEdge>();
        private readonly List<List<GraphEdge>> adjacency = new List<List<GraphEdge>>();
        private readonly Dictionary<long, GraphEdge> edgeByPair = new Dictionary<long, GraphEdge>();
        private readonly Dictionary<string, int> vertexByDevice = new Dictionary<string, int>();

        public TopologyGraph(string superLineName)
        {
            SuperLineName = superLineName;
        }

        public string SuperLineName { get; }

        public IReadOnlyList<GraphVertex> Vertices => vertices;

        public IReadOnlyList<GraphEdge> Edges => edges;

        public IReadOnlyList<GraphEdge> EdgesOf(int vertex) => adjacency[vertex];

        /// <summary>Vertex of a device (by handle), or -1.</summary>
        public int VertexOf(DeviceInfo device) => vertexByDevice.TryGetValue(device.Handle, out var index) ? index : -1;

        /// <summary>First vertex within the ZCAD eps box of <paramref name="point"/>, or -1.</summary>
        public int FindVertex(Point2D point, double epsilon)
        {
            for (var i = 0; i < vertices.Count; i++)
            {
                if (GeometryUtils.IsWithinBox(vertices[i].Point, point, epsilon))
                {
                    return i;
                }
            }

            return -1;
        }

        internal int AddVertex(Point2D point, VertexKind kind)
        {
            var vertex = new GraphVertex(vertices.Count, point, kind);
            vertices.Add(vertex);
            adjacency.Add(new List<GraphEdge>());
            return vertex.Index;
        }

        internal void AttachDevice(int vertex, DeviceInfo device)
        {
            vertices[vertex].AddDevice(device);
            vertexByDevice[device.Handle] = vertex;
        }

        /// <summary>Adds an edge; a second edge between the same vertices keeps the smaller weight (ZCAD <c>listHaveThisEdge</c>).</summary>
        internal GraphEdge? AddEdge(int from, int to, double weight, EdgeKind kind, SuperLineSegment? segment)
        {
            if (from == to)
            {
                return null;
            }

            var key = from < to ? ((long)from << 32) | (uint)to : ((long)to << 32) | (uint)from;
            if (edgeByPair.TryGetValue(key, out var existing))
            {
                if (weight < existing.Weight && existing.Kind == kind)
                {
                    existing.Weight = weight;
                }

                return existing;
            }

            var edge = new GraphEdge(edges.Count, from, to, weight, kind, segment);
            edges.Add(edge);
            edgeByPair.Add(key, edge);
            adjacency[from].Add(edge);
            adjacency[to].Add(edge);
            return edge;
        }
    }
}
