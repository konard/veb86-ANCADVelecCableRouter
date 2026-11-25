using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace VelecCableRouter.Core.XData
{
    /// <summary>Which BobrovXDATA layout an object carries (same rules as acadPaletteSetXDATA <c>XDataV2.FormatOf</c>).</summary>
    public enum BobrovXDataFormat
    {
        /// <summary>No BobrovXDATA section, or the section is empty.</summary>
        None,

        /// <summary>Legacy "key=value" strings; not supported by the router.</summary>
        V1,

        /// <summary>(1070 . 2) header followed by Id/value pairs.</summary>
        V2,

        /// <summary>(1070 . N), N &gt; 2: read as v2 with a warning.</summary>
        Newer,
    }

    /// <summary>Parsed BobrovXDATA section of one object.</summary>
    public sealed class BobrovXDataRecord
    {
        private readonly List<KeyValuePair<string, XDataValue>> parameters;
        private readonly Dictionary<string, XDataValue> byId;

        internal BobrovXDataRecord(
            BobrovXDataFormat format,
            int? version,
            List<KeyValuePair<string, XDataValue>> parameters,
            List<string> warnings)
        {
            Format = format;
            Version = version;
            this.parameters = parameters;
            Warnings = warnings;
            byId = new Dictionary<string, XDataValue>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in parameters)
            {
                if (!byId.ContainsKey(pair.Key))
                {
                    byId.Add(pair.Key, pair.Value);
                }
            }
        }

        public BobrovXDataFormat Format { get; }

        public int? Version { get; }

        /// <summary>Id/value pairs in file order (duplicates included; lookups use the first one).</summary>
        public IReadOnlyList<KeyValuePair<string, XDataValue>> Parameters => parameters;

        public IReadOnlyList<string> Warnings { get; }

        public string? DeviceId => GetString(BobrovXData.DeviceIdKey);

        public string? Template => GetString(BobrovXData.TemplateKey);

        /// <summary>Case-insensitive template check ("Device" and "DEVICE" are the same template).</summary>
        public bool IsTemplate(string template) =>
            string.Equals(Template?.Trim(), template, StringComparison.OrdinalIgnoreCase);

        /// <summary>Case-insensitive Id lookup.</summary>
        public bool TryGet(string id, out XDataValue value) => byId.TryGetValue(id, out value);

        /// <summary>Value as text (numbers are converted, so a numeric GroupName "1" is still readable).</summary>
        public string? GetString(string id) => TryGet(id, out var value) ? value.AsString() : null;

        /// <summary>Trimmed text value, <c>null</c> when missing or blank.</summary>
        public string? GetText(string id)
        {
            var text = GetString(id)?.Trim();
            return string.IsNullOrEmpty(text) ? null : text;
        }

        public double? GetDouble(string id) => TryGet(id, out var value) ? value.AsDouble() : null;
    }

    /// <summary>BobrovXDATA v2 contract: parser and writer of the flat (DXF code, value) list.</summary>
    public static class BobrovXData
    {
        public const string RegAppName = "BobrovXDATA";
        public const short FormatVersion = 2;
        public const string DeviceIdKey = "DEVICE_ID";
        public const string TemplateKey = "TEMPLATE";
        public const string TemplateSuperLine = "SuperLine";
        public const string TemplateDevice = "Device";
        public const string TemplateCable = "Cable";
        public const string NameKey = "NMO_Name";
        public const string SuperLineNameKey = "SuperLineName";
        public const string HeadDeviceNameKey = "HeadDeviceName";
        public const string GroupNameKey = "GroupName";
        public const string RiserNameKey = "RiserName";
        public const string ElevationKey = "Elevation";

        /// <summary>AutoCAD limit for one 1000 string.</summary>
        public const int MaxStringBytes = 255;

        /// <summary>AutoCAD limit for all XDATA of one object.</summary>
        public const int MaxXDataBytes = 16383;

        /// <summary>
        /// Parses the BobrovXDATA section. <paramref name="values"/> may be the whole <c>Entity.XData</c> (all RegApps),
        /// the result of <c>GetXDataForApplication</c>, or the section body without the (1001 . "BobrovXDATA") item.
        /// Never throws: damaged data becomes warnings.
        /// </summary>
        public static BobrovXDataRecord Parse(IEnumerable<XDataValue>? values)
        {
            var warnings = new List<string>();
            var parameters = new List<KeyValuePair<string, XDataValue>>();
            var section = ExtractSection(values?.ToList() ?? new List<XDataValue>(), warnings);
            if (section.Count == 0)
            {
                return new BobrovXDataRecord(BobrovXDataFormat.None, null, parameters, warnings);
            }

            var first = section[0];
            var version = first.TypeCode == DxfCode.Int16 ? first.AsDouble() : null;
            if (version == null)
            {
                warnings.Add("Формат BobrovXDATA v1 не поддерживается: выполните перенос в v2 (VELEC_MIGRATE в палитре AcadPaletteSetXData).");
                return new BobrovXDataRecord(BobrovXDataFormat.V1, 1, parameters, warnings);
            }

            var format = BobrovXDataFormat.V2;
            var versionNumber = (int)version.Value;
            if (versionNumber > FormatVersion)
            {
                format = BobrovXDataFormat.Newer;
                warnings.Add($"Версия формата BobrovXDATA {versionNumber} новее поддерживаемой ({FormatVersion}): данные прочитаны как v2.");
            }
            else if (versionNumber != FormatVersion)
            {
                warnings.Add($"Неизвестная версия формата BobrovXDATA {versionNumber}: данные прочитаны как v2.");
            }

            ParsePairs(section, 1, parameters, warnings);
            return new BobrovXDataRecord(format, versionNumber, parameters, warnings);
        }

        /// <summary>
        /// Builds the complete XDATA of a cable polyline (issue #1, section 3.1.3), starting with
        /// (1001 . "BobrovXDATA") so it can be assigned to <c>Entity.XData</c> as is.
        /// </summary>
        public static IReadOnlyList<XDataValue> BuildCable(string superLineName, string headDeviceName, string groupName, Guid deviceId)
        {
            var values = BuildCableUnchecked(superLineName, headDeviceName, groupName, deviceId);
            var error = Validate(values);
            if (error != null)
            {
                throw new ArgumentException(error);
            }

            return values;
        }

        /// <summary>Same as <see cref="BuildCable"/> without the limit check (use <see cref="Validate"/>).</summary>
        public static IReadOnlyList<XDataValue> BuildCableUnchecked(string superLineName, string headDeviceName, string groupName, Guid deviceId)
        {
            return new List<XDataValue>
            {
                XDataValue.App(RegAppName),
                XDataValue.Int16(FormatVersion),
                XDataValue.Str(DeviceIdKey), XDataValue.Str(deviceId.ToString("D")),
                XDataValue.Str(TemplateKey), XDataValue.Str(TemplateCable),
                XDataValue.Str(NameKey), XDataValue.Str(superLineName),
                XDataValue.Str(SuperLineNameKey), XDataValue.Str(superLineName),
                XDataValue.Str(HeadDeviceNameKey), XDataValue.Str(headDeviceName),
                XDataValue.Str(GroupNameKey), XDataValue.Str(groupName),
            };
        }

        /// <summary>Checks AutoCAD XDATA limits; returns the error text or <c>null</c>.</summary>
        public static string? Validate(IEnumerable<XDataValue> values)
        {
            var list = values.ToList();
            foreach (var value in list)
            {
                if (value.TypeCode == DxfCode.String && value.Value is string text && Encoding.UTF8.GetByteCount(text) > MaxStringBytes)
                {
                    return $"Строка XDATA длиннее {MaxStringBytes} байт UTF-8: \"{text.Substring(0, Math.Min(40, text.Length))}…\".";
                }
            }

            var size = EstimateSize(list);
            return size > MaxXDataBytes ? $"Размер XDATA {size} байт превышает {MaxXDataBytes}." : null;
        }

        /// <summary>Upper estimate of the XDATA size (same weights as acadPaletteSetXDATA <c>XDataV2.EstimateSize</c>).</summary>
        public static int EstimateSize(IEnumerable<XDataValue> values)
        {
            var size = 0;
            foreach (var value in values)
            {
                size += 2;
                switch (value.TypeCode)
                {
                    case DxfCode.Int16:
                        size += 2;
                        break;
                    case DxfCode.Int32:
                        size += 4;
                        break;
                    case DxfCode.Real:
                        size += 8;
                        break;
                    default:
                        size += value.Value is string s ? 3 + Encoding.UTF8.GetByteCount(s) : 24;
                        break;
                }
            }

            return size;
        }

        private static List<XDataValue> ExtractSection(List<XDataValue> values, List<string> warnings)
        {
            var hasAppHeaders = values.Any(v => v.TypeCode == DxfCode.RegAppName);
            if (!hasAppHeaders)
            {
                return values;
            }

            var section = new List<XDataValue>();
            var sections = 0;
            var inside = false;
            foreach (var value in values)
            {
                if (value.TypeCode == DxfCode.RegAppName)
                {
                    inside = string.Equals(value.AsString(), RegAppName, StringComparison.OrdinalIgnoreCase);
                    if (inside && ++sections > 1)
                    {
                        warnings.Add("Раздел BobrovXDATA повторяется; используется первый.");
                        inside = false;
                    }

                    continue;
                }

                if (inside && sections == 1)
                {
                    section.Add(value);
                }
            }

            return section;
        }

        private static void ParsePairs(List<XDataValue> section, int start, List<KeyValuePair<string, XDataValue>> parameters, List<string> warnings)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var index = start;
            while (index < section.Count)
            {
                var item = section[index];
                if (IsOpenBrace(item))
                {
                    warnings.Add("Список «{ … }» без Id пропущен.");
                    index = SkipList(section, index, warnings);
                    continue;
                }

                if (item.TypeCode != DxfCode.String || string.IsNullOrWhiteSpace(item.AsString()))
                {
                    warnings.Add($"Значение {item} без Id пропущено.");
                    index++;
                    continue;
                }

                var id = item.AsString()!.Trim();
                if (index + 1 >= section.Count)
                {
                    warnings.Add($"У параметра «{id}» нет значения.");
                    break;
                }

                var value = section[index + 1];
                if (IsOpenBrace(value))
                {
                    // Table parameters are not used by the router; keep a marker so the Id is known.
                    var end = SkipList(section, index + 1, warnings);
                    Add(parameters, seen, warnings, id, XDataValue.Control("{"));
                    index = end;
                    continue;
                }

                Add(parameters, seen, warnings, id, value);
                index += 2;
            }
        }

        private static void Add(List<KeyValuePair<string, XDataValue>> parameters, HashSet<string> seen, List<string> warnings, string id, XDataValue value)
        {
            if (!seen.Add(id))
            {
                warnings.Add($"Параметр «{id}» повторяется; используется первое значение.");
            }

            parameters.Add(new KeyValuePair<string, XDataValue>(id, value));
        }

        private static bool IsOpenBrace(XDataValue value) => value.TypeCode == DxfCode.ControlString && value.AsString() == "{";

        private static bool IsCloseBrace(XDataValue value) => value.TypeCode == DxfCode.ControlString && value.AsString() == "}";

        private static int SkipList(List<XDataValue> section, int openIndex, List<string> warnings)
        {
            var depth = 0;
            for (var i = openIndex; i < section.Count; i++)
            {
                if (IsOpenBrace(section[i]))
                {
                    depth++;
                }
                else if (IsCloseBrace(section[i]) && --depth == 0)
                {
                    return i + 1;
                }
            }

            warnings.Add("Незакрытый список «{».");
            return section.Count;
        }
    }
}
