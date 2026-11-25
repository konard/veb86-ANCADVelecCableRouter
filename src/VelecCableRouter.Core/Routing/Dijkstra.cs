using System.Collections.Generic;
using VelecCableRouter.Core.Graph;

namespace VelecCableRouter.Core.Routing
{
    /// <summary>Shortest-path tree of a <see cref="TopologyGraph"/> from one source vertex.</summary>
    public sealed class ShortestPathTree
    {
        internal ShortestPathTree(int source, double[] distance, GraphEdge?[] previousEdge)
        {
            Source = source;
            Distance = distance;
            PreviousEdge = previousEdge;
        }

        public int Source { get; }

        /// <summary>Distance from the source; <see cref="double.PositiveInfinity"/> when unreachable.</summary>
        public IReadOnlyList<double> Distance { get; }

        /// <summary>Edge used to reach each vertex (null for the source and unreachable vertices).</summary>
        public IReadOnlyList<GraphEdge?> PreviousEdge { get; }

        public bool IsReachable(int vertex) => !double.IsPositiveInfinity(Distance[vertex]);

        /// <summary>Edges from the source to <paramref name="target"/>, in order from the source.</summary>
        public IReadOnlyList<GraphEdge> PathTo(int target)
        {
            var path = new List<GraphEdge>();
            if (!IsReachable(target))
            {
                return path;
            }

            var vertex = target;
            while (vertex != Source)
            {
                var edge = PreviousEdge[vertex]!;
                path.Add(edge);
                vertex = edge.Other(vertex);
            }

            path.Reverse();
            return path;
        }
    }

    /// <summary>Dijkstra's algorithm with a binary heap (deterministic: ties go to the lower vertex index).</summary>
    public static class Dijkstra
    {
        public static ShortestPathTree Run(TopologyGraph graph, int source)
        {
            var count = graph.Vertices.Count;
            var distance = new double[count];
            var previous = new GraphEdge?[count];
            var done = new bool[count];
            for (var i = 0; i < count; i++)
            {
                distance[i] = double.PositiveInfinity;
            }

            distance[source] = 0;
            var heap = new MinHeap();
            heap.Push(0, source);
            while (heap.Count > 0)
            {
                var (d, vertex) = heap.Pop();
                if (done[vertex] || d > distance[vertex])
                {
                    continue;
                }

                done[vertex] = true;
                foreach (var edge in graph.EdgesOf(vertex))
                {
                    var next = edge.Other(vertex);
                    var candidate = d + edge.Weight;
                    if (!done[next] && candidate < distance[next])
                    {
                        distance[next] = candidate;
                        previous[next] = edge;
                        heap.Push(candidate, next);
                    }
                }
            }

            return new ShortestPathTree(source, distance, previous);
        }

        private sealed class MinHeap
        {
            private readonly List<(double Key, int Vertex)> items = new List<(double Key, int Vertex)>();

            public int Count => items.Count;

            public void Push(double key, int vertex)
            {
                items.Add((key, vertex));
                var i = items.Count - 1;
                while (i > 0)
                {
                    var parent = (i - 1) / 2;
                    if (!Less(items[i], items[parent]))
                    {
                        break;
                    }

                    (items[i], items[parent]) = (items[parent], items[i]);
                    i = parent;
                }
            }

            public (double Key, int Vertex) Pop()
            {
                var top = items[0];
                var last = items[items.Count - 1];
                items.RemoveAt(items.Count - 1);
                if (items.Count > 0)
                {
                    items[0] = last;
                    var i = 0;
                    while (true)
                    {
                        var left = 2 * i + 1;
                        var right = left + 1;
                        var smallest = i;
                        if (left < items.Count && Less(items[left], items[smallest]))
                        {
                            smallest = left;
                        }

                        if (right < items.Count && Less(items[right], items[smallest]))
                        {
                            smallest = right;
                        }

                        if (smallest == i)
                        {
                            break;
                        }

                        (items[i], items[smallest]) = (items[smallest], items[i]);
                        i = smallest;
                    }
                }

                return top;
            }

            private static bool Less((double Key, int Vertex) a, (double Key, int Vertex) b) =>
                a.Key < b.Key || (a.Key == b.Key && a.Vertex < b.Vertex);
        }
    }
}
