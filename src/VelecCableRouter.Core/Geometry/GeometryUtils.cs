using System;
using System.Collections.Generic;

namespace VelecCableRouter.Core.Geometry
{
    /// <summary>2D geometry used by the graph builder (ports of the uzvcom.pas helpers) and the router.</summary>
    public static class GeometryUtils
    {
        /// <summary>Tolerance for removing collinear polyline vertices (issue #1, section 4.4).</summary>
        public const double CollinearTolerance = 1e-6;

        private const double ParallelTolerance = 1e-12;
        private const double ParameterTolerance = 1e-9;

        /// <summary>
        /// ZCAD <c>dublicateVertex</c>: two points coincide when both coordinate differences
        /// are within <paramref name="epsilon"/> (a square box, not a circle).
        /// </summary>
        public static bool IsWithinBox(Point2D a, Point2D b, double epsilon) =>
            Math.Abs(a.X - b.X) <= epsilon && Math.Abs(a.Y - b.Y) <= epsilon;

        /// <summary>ZCAD <c>extendedLineFunc</c>: lengthens a segment by <paramref name="distance"/> at both ends.</summary>
        public static (Point2D Start, Point2D End) Extend(Point2D start, Point2D end, double distance)
        {
            var direction = (end - start).Normalized();
            if (direction.Length == 0)
            {
                return (start, end);
            }

            return (start - direction * distance, end + direction * distance);
        }

        /// <summary>
        /// Intersection of two finite segments (ZCAD <c>intercept3d</c> in the XY plane).
        /// Parallel and collinear segments do not intersect.
        /// </summary>
        public static bool TryIntersect(Point2D a1, Point2D a2, Point2D b1, Point2D b2, out Point2D intersection)
        {
            intersection = default;
            var r = a2 - a1;
            var s = b2 - b1;
            var denominator = r.Cross(s);
            var scale = r.Length * s.Length;
            if (scale == 0 || Math.Abs(denominator) <= ParallelTolerance * scale)
            {
                return false;
            }

            var qp = b1 - a1;
            var t = qp.Cross(s) / denominator;
            var u = qp.Cross(r) / denominator;
            if (t < -ParameterTolerance || t > 1 + ParameterTolerance || u < -ParameterTolerance || u > 1 + ParameterTolerance)
            {
                return false;
            }

            intersection = a1 + r * t;
            return true;
        }

        /// <summary>
        /// ZCAD <c>vertexPointInAreaRectangle</c>: is <paramref name="point"/> inside the rectangle built around the
        /// segment, lengthened by <paramref name="accuracy"/> at both ends and <paramref name="accuracy"/> wide on each
        /// side? <paramref name="distanceFromStart"/> is the projection onto the segment, used for ordering.
        /// </summary>
        public static bool IsInSegmentArea(Point2D point, Point2D start, Point2D end, double accuracy, out double distanceFromStart)
        {
            var axis = end - start;
            var length = axis.Length;
            var offset = point - start;
            if (length == 0)
            {
                distanceFromStart = offset.Length;
                return IsWithinBox(point, start, accuracy);
            }

            var direction = axis * (1 / length);
            distanceFromStart = offset.Dot(direction);
            var perpendicular = Math.Abs(direction.Cross(offset));
            return distanceFromStart >= -accuracy && distanceFromStart <= length + accuracy && perpendicular <= accuracy;
        }

        /// <summary>Distance from <paramref name="point"/> to the infinite line through two points.</summary>
        public static double DistanceToLine(Point2D point, Point2D lineStart, Point2D lineEnd)
        {
            var axis = lineEnd - lineStart;
            var length = axis.Length;
            if (length == 0)
            {
                return point.DistanceTo(lineStart);
            }

            return Math.Abs(axis.Cross(point - lineStart)) / length;
        }

        /// <summary>
        /// Removes repeated vertices and intermediate vertices lying on the straight line between their neighbours
        /// (within <paramref name="tolerance"/>). A vertex where the polyline turns back is kept.
        /// </summary>
        public static List<Point2D> SimplifyCollinear(IReadOnlyList<Point2D> points, double tolerance = CollinearTolerance)
        {
            var result = new List<Point2D>(points.Count);
            foreach (var point in points)
            {
                if (result.Count > 0 && result[result.Count - 1].DistanceTo(point) <= tolerance)
                {
                    continue;
                }

                while (result.Count >= 2 && IsRedundant(result[result.Count - 2], result[result.Count - 1], point, tolerance))
                {
                    result.RemoveAt(result.Count - 1);
                }

                result.Add(point);
            }

            return result;
        }

        private static bool IsRedundant(Point2D previous, Point2D middle, Point2D next, double tolerance) =>
            DistanceToLine(middle, previous, next) <= tolerance && (middle - previous).Dot(next - middle) > 0;
    }
}
