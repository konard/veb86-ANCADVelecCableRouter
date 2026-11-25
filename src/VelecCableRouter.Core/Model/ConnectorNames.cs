using System;

namespace VelecCableRouter.Core.Model
{
    /// <summary>
    /// Names of the connection point marker inside a device block (issue #1, section 3):
    /// a nested block, a point (by layer) or an attribute definition (by tag) named CONNECTOR_POINT or CONNECTOR_SQUARE.
    /// </summary>
    public static class ConnectorNames
    {
        public const string Point = "CONNECTOR_POINT";
        public const string Square = "CONNECTOR_SQUARE";

        /// <summary>Maximum nesting depth of the recursive block search (protects against malformed drawings).</summary>
        public const int MaxSearchDepth = 16;

        /// <summary>Case-insensitive match that ignores surrounding spaces.</summary>
        public static bool IsConnector(string? name)
        {
            if (name == null)
            {
                return false;
            }

            var trimmed = name.Trim();
            return string.Equals(trimmed, Point, StringComparison.OrdinalIgnoreCase)
                || string.Equals(trimmed, Square, StringComparison.OrdinalIgnoreCase);
        }
    }
}
