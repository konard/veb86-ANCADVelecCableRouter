using VelecCableRouter.Core.Geometry;

namespace VelecCableRouter.Core.Model
{
    public enum DiagnosticSeverity
    {
        Info,
        Warning,
        Error,
    }

    /// <summary>One line of the command report (warning or error), optionally tied to an object and a location.</summary>
    public sealed class Diagnostic
    {
        public Diagnostic(DiagnosticSeverity severity, string message, string? handle = null, Point2D? location = null)
        {
            Severity = severity;
            Message = message;
            Handle = handle;
            Location = location;
        }

        public DiagnosticSeverity Severity { get; }

        public string Message { get; }

        /// <summary>Hex handle of the related object.</summary>
        public string? Handle { get; }

        /// <summary>Where a debug marker is drawn for errors.</summary>
        public Point2D? Location { get; }

        public static Diagnostic Warning(string message, string? handle = null, Point2D? location = null) =>
            new Diagnostic(DiagnosticSeverity.Warning, message, handle, location);

        public static Diagnostic Error(string message, string? handle = null, Point2D? location = null) =>
            new Diagnostic(DiagnosticSeverity.Error, message, handle, location);

        public override string ToString() => Severity + ": " + Message + (Handle == null ? string.Empty : " [" + Handle + "]");
    }
}
