using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using VelecCableRouter.Core.Geometry;
using VelecCableRouter.Core.XData;

namespace VelecCableRouter.Core.Model
{
    /// <summary>
    /// Turns selected entities into superline segments and devices according to the BobrovXDATA v2 contract
    /// (issue #1, sections 3 and 3.1).
    /// </summary>
    public static class DrawingModelReader
    {
        private static readonly Regex RepeatKey = new Regex(@"^(?<prefix>.+?)_(?<index>\d+)_(?<field>.+)$", RegexOptions.CultureInvariant);

        public static DrawingModel Read(IEnumerable<RawEntity> entities)
        {
            var segments = new List<SuperLineSegment>();
            var devices = new List<DeviceInfo>();
            var diagnostics = new List<Diagnostic>();
            foreach (var entity in entities)
            {
                var record = BobrovXData.Parse(entity.XData);
                if (record.Format == BobrovXDataFormat.None)
                {
                    continue;
                }

                if (record.Format == BobrovXDataFormat.V1)
                {
                    if (LooksLikeRouterObject(entity))
                    {
                        foreach (var warning in record.Warnings)
                        {
                            diagnostics.Add(Diagnostic.Warning(warning, entity.Handle));
                        }
                    }

                    continue;
                }

                if (record.IsTemplate(BobrovXData.TemplateSuperLine))
                {
                    AddWarnings(record, entity, diagnostics);
                    ReadSuperLine(entity, record, segments, diagnostics);
                }
                else if (record.IsTemplate(BobrovXData.TemplateDevice))
                {
                    AddWarnings(record, entity, diagnostics);
                    ReadDevice(entity, record, devices, diagnostics);
                }
            }

            return new DrawingModel(segments, devices, diagnostics);
        }

        /// <summary>
        /// Reads device connections: repeat items <c>&lt;prefix&gt;_&lt;n&gt;_SuperLine</c>/<c>_HeadDevice</c>/<c>_Group</c>
        /// (the <c>...Name</c> spellings are accepted too) and the scalar fields SuperLineName/HeadDeviceName/GroupName.
        /// Connections without a superline name are skipped; duplicates are merged.
        /// </summary>
        public static IReadOnlyList<DeviceConnection> ReadConnections(BobrovXDataRecord record, ICollection<string>? warnings = null)
        {
            var items = new Dictionary<string, RepeatItem>(StringComparer.OrdinalIgnoreCase);
            var order = new List<string>();
            foreach (var pair in record.Parameters)
            {
                var match = RepeatKey.Match(pair.Key);
                if (!match.Success)
                {
                    continue;
                }

                var field = ConnectionField(match.Groups["field"].Value);
                if (field == null)
                {
                    continue;
                }

                var key = match.Groups["prefix"].Value + "_" + int.Parse(match.Groups["index"].Value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
                if (!items.TryGetValue(key, out var item))
                {
                    item = new RepeatItem(match.Groups["prefix"].Value, int.Parse(match.Groups["index"].Value, CultureInfo.InvariantCulture));
                    items.Add(key, item);
                    order.Add(key);
                }

                var text = pair.Value.AsString()?.Trim();
                if (string.IsNullOrEmpty(text) || item.Fields.ContainsKey(field))
                {
                    continue;
                }

                item.Fields.Add(field, text!);
            }

            var result = new List<DeviceConnection>();
            foreach (var item in order.Select(k => items[k]).OrderBy(i => i.Prefix, StringComparer.OrdinalIgnoreCase).ThenBy(i => i.Index))
            {
                item.Fields.TryGetValue(SuperLineField, out var superLine);
                item.Fields.TryGetValue(HeadField, out var head);
                item.Fields.TryGetValue(GroupField, out var group);
                var source = item.Prefix + "_" + item.Index.ToString(CultureInfo.InvariantCulture);
                if (superLine == null)
                {
                    if (head != null || group != null)
                    {
                        warnings?.Add($"Подключение {source} без имени суперлинии пропущено.");
                    }

                    continue;
                }

                AddDistinct(result, new DeviceConnection(superLine, head, group, source));
            }

            var scalarSuperLine = record.GetText(BobrovXData.SuperLineNameKey);
            var scalarHead = record.GetText(BobrovXData.HeadDeviceNameKey);
            var scalarGroup = record.GetText(BobrovXData.GroupNameKey);
            if (scalarSuperLine != null)
            {
                AddDistinct(result, new DeviceConnection(scalarSuperLine, scalarHead, scalarGroup, BobrovXData.SuperLineNameKey));
            }
            else if (scalarHead != null || scalarGroup != null)
            {
                warnings?.Add("Поля HeadDeviceName/GroupName без SuperLineName пропущены.");
            }

            return result;
        }

        private const string SuperLineField = "SuperLine";
        private const string HeadField = "HeadDevice";
        private const string GroupField = "Group";

        private static string? ConnectionField(string field)
        {
            if (field.Equals("SuperLine", StringComparison.OrdinalIgnoreCase) || field.Equals("SuperLineName", StringComparison.OrdinalIgnoreCase))
            {
                return SuperLineField;
            }

            if (field.Equals("HeadDevice", StringComparison.OrdinalIgnoreCase) || field.Equals("HeadDeviceName", StringComparison.OrdinalIgnoreCase))
            {
                return HeadField;
            }

            if (field.Equals("Group", StringComparison.OrdinalIgnoreCase) || field.Equals("GroupName", StringComparison.OrdinalIgnoreCase))
            {
                return GroupField;
            }

            return null;
        }

        private static void AddDistinct(List<DeviceConnection> connections, DeviceConnection connection)
        {
            if (!connections.Contains(connection))
            {
                connections.Add(connection);
            }
        }

        private static bool LooksLikeRouterObject(RawEntity entity) => entity.Kind != RawEntityKind.Other;

        private static void AddWarnings(BobrovXDataRecord record, RawEntity entity, List<Diagnostic> diagnostics)
        {
            foreach (var warning in record.Warnings)
            {
                diagnostics.Add(Diagnostic.Warning(warning, entity.Handle));
            }
        }

        private static void ReadSuperLine(RawEntity entity, BobrovXDataRecord record, List<SuperLineSegment> segments, List<Diagnostic> diagnostics)
        {
            if (entity.Kind != RawEntityKind.Line && entity.Kind != RawEntityKind.Polyline)
            {
                diagnostics.Add(Diagnostic.Warning("Трасса (SuperLine) должна быть отрезком или 2D-полилинией; объект пропущен.", entity.Handle));
                return;
            }

            var first = entity.Vertices.Count > 0 ? entity.Vertices[0] : (Point2D?)null;
            var name = record.GetText(BobrovXData.NameKey);
            if (name == null)
            {
                diagnostics.Add(Diagnostic.Warning("Отрезок трассы без NMO_Name игнорирован.", entity.Handle, first));
                return;
            }

            if (entity.HasArcs)
            {
                diagnostics.Add(Diagnostic.Warning($"Полилиния трассы «{name}» содержит дуги: трассировка идёт по их хордам.", entity.Handle, first));
            }

            var vertices = entity.Vertices;
            var count = vertices.Count;
            var segmentCount = entity.IsClosed && count > 2 ? count : count - 1;
            var added = 0;
            for (var i = 0; i < segmentCount; i++)
            {
                var start = vertices[i];
                var end = vertices[(i + 1) % count];
                if (start.DistanceTo(end) <= GeometryUtils.CollinearTolerance)
                {
                    continue;
                }

                segments.Add(new SuperLineSegment(entity.Handle, i, name, start, end, entity.Layer));
                added++;
            }

            if (added == 0)
            {
                diagnostics.Add(Diagnostic.Warning($"Трасса «{name}» нулевой длины пропущена.", entity.Handle, first));
            }
        }

        private static void ReadDevice(RawEntity entity, BobrovXDataRecord record, List<DeviceInfo> devices, List<Diagnostic> diagnostics)
        {
            if (entity.Kind != RawEntityKind.BlockReference || entity.Insertion == null)
            {
                diagnostics.Add(Diagnostic.Warning("Устройство (Device) должно быть вхождением блока; объект пропущен.", entity.Handle));
                return;
            }

            var warnings = new List<string>();
            var connections = ReadConnections(record, warnings);
            var name = record.GetText(BobrovXData.NameKey);
            var device = new DeviceInfo(entity.Handle, name, entity.Connector ?? entity.Insertion.Value, entity.Insertion.Value, connections)
            {
                Extents = entity.Extents,
                RiserName = record.GetText(BobrovXData.RiserNameKey),
                Elevation = record.GetDouble(BobrovXData.ElevationKey),
                Layer = entity.Layer,
            };
            foreach (var warning in warnings)
            {
                diagnostics.Add(Diagnostic.Warning($"Устройство «{device.DisplayName}»: {warning}", entity.Handle, device.Connector));
            }

            if (name == null && connections.Count > 0)
            {
                diagnostics.Add(Diagnostic.Warning("Устройство без NMO_Name: в отчётах оно обозначено handle.", entity.Handle, device.Connector));
            }

            if (record.TryGet(BobrovXData.ElevationKey, out var rawElevation) && device.Elevation == null && !string.IsNullOrWhiteSpace(rawElevation.AsString()))
            {
                diagnostics.Add(Diagnostic.Warning($"Устройство «{device.DisplayName}»: Elevation «{rawElevation.AsString()}» не является числом.", entity.Handle, device.Connector));
            }

            devices.Add(device);
        }

        private sealed class RepeatItem
        {
            public RepeatItem(string prefix, int index)
            {
                Prefix = prefix;
                Index = index;
            }

            public string Prefix { get; }

            public int Index { get; }

            public Dictionary<string, string> Fields { get; } = new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }
}
