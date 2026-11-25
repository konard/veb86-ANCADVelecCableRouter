using System;
using System.Collections.Generic;
using System.Linq;

namespace VelecCableRouter.Core.Model
{
    /// <summary>Port of uzvcom.pas <c>getListSuperline</c>: distinct superline names in the order they are met.</summary>
    public static class SuperLineCollector
    {
        public static IReadOnlyList<string> GetSuperLineNames(IEnumerable<SuperLineSegment> segments)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            return segments.Select(s => s.Name).Where(seen.Add).ToList();
        }
    }
}
