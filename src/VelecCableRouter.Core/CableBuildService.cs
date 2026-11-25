using System;
using System.Collections.Generic;
using System.Linq;
using VelecCableRouter.Core.Graph;
using VelecCableRouter.Core.Model;
using VelecCableRouter.Core.Routing;
using VelecCableRouter.Core.XData;

namespace VelecCableRouter.Core
{
    public sealed class CableBuildOptions
    {
        public const double DefaultEpsilon = 0.5;

        /// <summary>Tolerance of the graph builder (ZCAD Epsilon), drawing units.</summary>
        public double Epsilon { get; set; } = DefaultEpsilon;

        /// <summary>The tolerance must be a finite positive number.</summary>
        public static bool IsValidEpsilon(double epsilon) => epsilon > 0 && !double.IsInfinity(epsilon);
    }

    /// <summary>Result for one superline: graph, cables and the report lines.</summary>
    public sealed class SuperLineReport
    {
        public SuperLineReport(string name, TopologyGraph graph, IReadOnlyList<CableGroup> groups, IReadOnlyList<CableRoute> cables, IReadOnlyList<Diagnostic> diagnostics)
        {
            Name = name;
            Graph = graph;
            Groups = groups;
            Cables = cables;
            Diagnostics = diagnostics;
        }

        public string Name { get; }

        public TopologyGraph Graph { get; }

        public int VertexCount => Graph.Vertices.Count;

        public int EdgeCount => Graph.Edges.Count;

        public IReadOnlyList<CableGroup> Groups { get; }

        public IReadOnlyList<CableRoute> Cables { get; }

        public IReadOnlyList<Diagnostic> Diagnostics { get; }

        public int WarningCount => Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Warning);

        public int ErrorCount => Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error);
    }

    public sealed class CableBuildResult
    {
        public CableBuildResult(DrawingModel model, IReadOnlyList<SuperLineReport> superLines, IReadOnlyList<Diagnostic> diagnostics)
        {
            Model = model;
            SuperLines = superLines;
            Diagnostics = diagnostics;
        }

        public DrawingModel Model { get; }

        public IReadOnlyList<SuperLineReport> SuperLines { get; }

        /// <summary>Problems not tied to a superline.</summary>
        public IReadOnlyList<Diagnostic> Diagnostics { get; }

        public IEnumerable<CableRoute> Cables => SuperLines.SelectMany(s => s.Cables);
    }

    /// <summary>
    /// The whole VELEC_BUILD_CABLES pipeline without AutoCAD: read entities, then for every superline
    /// (4.1) filter the participants (4.2), build the graph (4.3) and route the cables (4.4).
    /// </summary>
    public static class CableBuildService
    {
        public static CableBuildResult Build(IEnumerable<RawEntity> entities, CableBuildOptions? options = null)
        {
            options ??= new CableBuildOptions();
            if (!CableBuildOptions.IsValidEpsilon(options.Epsilon))
            {
                throw new ArgumentOutOfRangeException(nameof(options), options.Epsilon, "Epsilon must be a positive number.");
            }

            var model = DrawingModelReader.Read(entities);
            var global = new List<Diagnostic>(model.Diagnostics);
            var names = SuperLineCollector.GetSuperLineNames(model.Segments);
            var known = new HashSet<string>(names, StringComparer.Ordinal);
            foreach (var missing in model.Devices
                         .SelectMany(d => d.Connections.Select(c => (Device: d, Connection: c)))
                         .Where(x => !known.Contains(x.Connection.SuperLineName))
                         .GroupBy(x => x.Connection.SuperLineName, StringComparer.Ordinal))
            {
                var first = missing.First().Device;
                global.Add(Diagnostic.Warning(
                    $"Суперлиния «{missing.Key}» указана у устройств ({missing.Count()}, например «{first.DisplayName}»), но её трассы нет в выделении.",
                    first.Handle,
                    first.Connector));
            }

            var reports = new List<SuperLineReport>();
            foreach (var name in names)
            {
                var diagnostics = new List<Diagnostic>();
                var participants = GraphParticipantFilter.Filter(model, name);
                var graph = GraphBuilder.Build(participants, options.Epsilon, diagnostics);
                var routing = CableRouter.Route(graph, participants, diagnostics);
                var cables = new List<CableRoute>();
                foreach (var cable in routing.Cables)
                {
                    var error = BobrovXData.Validate(BobrovXData.BuildCableUnchecked(cable.SuperLineName, cable.HeadDeviceName, cable.GroupName, Guid.Empty));
                    if (error != null)
                    {
                        diagnostics.Add(Diagnostic.Error($"Кабель группы «{cable.HeadDeviceName}»/«{cable.GroupName}» не создан: {error}", null, cable.Points[0]));
                        continue;
                    }

                    cables.Add(cable);
                }

                if (routing.Groups.Count == 0)
                {
                    diagnostics.Add(Diagnostic.Warning("Нет устройств с HeadDeviceName для этой суперлинии: кабели не построены."));
                }

                reports.Add(new SuperLineReport(name, graph, routing.Groups, cables, diagnostics));
            }

            if (names.Count == 0)
            {
                global.Add(Diagnostic.Warning("В выделении нет трасс (TEMPLATE = SuperLine) с NMO_Name."));
            }

            return new CableBuildResult(model, reports, global);
        }
    }
}
