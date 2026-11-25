using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using VelecCableRouter.Core.Model;

namespace VelecCableRouter.Core.Reporting
{
    /// <summary>Command line report of VELEC_BUILD_CABLES (issue #1, section 5).</summary>
    public static class ReportFormatter
    {
        public static IReadOnlyList<string> Format(CableBuildResult result, int createdCables = -1)
        {
            var lines = new List<string>();
            foreach (var diagnostic in result.Diagnostics)
            {
                lines.Add(FormatDiagnostic(diagnostic, string.Empty));
            }

            foreach (var report in result.SuperLines)
            {
                lines.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "Суперлиния «{0}»: вершин {1}, рёбер {2}, групп {3}, кабелей {4}, предупреждений {5}, ошибок {6}.",
                    report.Name,
                    report.VertexCount,
                    report.EdgeCount,
                    report.Groups.Count,
                    report.Cables.Count,
                    report.WarningCount,
                    report.ErrorCount));
                foreach (var group in report.Groups)
                {
                    var cables = report.Cables.Where(c => c.HeadDeviceName == group.HeadDeviceName && c.GroupName == group.GroupName).ToList();
                    lines.Add(string.Format(
                        CultureInfo.InvariantCulture,
                        "  Группа {0}: устройств {1}, полилиний {2}, длина {3:0.##}.",
                        group.DisplayName,
                        group.Devices.Count,
                        cables.Count,
                        cables.Sum(c => c.Length)));
                }

                foreach (var diagnostic in report.Diagnostics)
                {
                    lines.Add(FormatDiagnostic(diagnostic, "  "));
                }
            }

            var total = result.SuperLines.Sum(s => s.Cables.Count);
            var warnings = result.Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Warning) + result.SuperLines.Sum(s => s.WarningCount);
            var errors = result.Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error) + result.SuperLines.Sum(s => s.ErrorCount);
            lines.Add(string.Format(
                CultureInfo.InvariantCulture,
                "Итого: суперлиний {0}, кабелей {1}{2}, предупреждений {3}, ошибок {4}.",
                result.SuperLines.Count,
                total,
                createdCables >= 0 && createdCables != total ? " (создано " + createdCables.ToString(CultureInfo.InvariantCulture) + ")" : string.Empty,
                warnings,
                errors));
            return lines;
        }

        public static string FormatDiagnostic(Diagnostic diagnostic, string indent)
        {
            var label = diagnostic.Severity == DiagnosticSeverity.Error ? "Ошибка" : diagnostic.Severity == DiagnosticSeverity.Warning ? "Предупреждение" : "Сведения";
            var handle = diagnostic.Handle == null ? string.Empty : " [handle " + diagnostic.Handle + "]";
            var location = diagnostic.Location == null ? string.Empty : " " + diagnostic.Location.Value;
            return indent + label + ": " + diagnostic.Message + handle + location;
        }
    }
}
