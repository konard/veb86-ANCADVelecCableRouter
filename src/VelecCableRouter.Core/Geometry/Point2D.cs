using System;
using System.Globalization;

namespace VelecCableRouter.Core.Geometry
{
    /// <summary>A point in the drawing XY plane (Z is always dropped, issue #1, section 3.1).</summary>
    public readonly struct Point2D : IEquatable<Point2D>
    {
        public Point2D(double x, double y)
        {
            X = x;
            Y = y;
        }

        public double X { get; }

        public double Y { get; }

        public double DistanceTo(Point2D other)
        {
            var dx = other.X - X;
            var dy = other.Y - Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        public static Point2D operator +(Point2D a, Vector2D b) => new Point2D(a.X + b.X, a.Y + b.Y);

        public static Point2D operator -(Point2D a, Vector2D b) => new Point2D(a.X - b.X, a.Y - b.Y);

        public static Vector2D operator -(Point2D a, Point2D b) => new Vector2D(a.X - b.X, a.Y - b.Y);

        public static bool operator ==(Point2D a, Point2D b) => a.Equals(b);

        public static bool operator !=(Point2D a, Point2D b) => !a.Equals(b);

        public bool Equals(Point2D other) => X.Equals(other.X) && Y.Equals(other.Y);

        public override bool Equals(object? obj) => obj is Point2D other && Equals(other);

        public override int GetHashCode() => unchecked((X.GetHashCode() * 397) ^ Y.GetHashCode());

        public override string ToString() =>
            string.Format(CultureInfo.InvariantCulture, "({0:0.###}; {1:0.###})", X, Y);
    }

    /// <summary>A 2D direction/offset.</summary>
    public readonly struct Vector2D
    {
        public Vector2D(double x, double y)
        {
            X = x;
            Y = y;
        }

        public double X { get; }

        public double Y { get; }

        public double Length => Math.Sqrt(X * X + Y * Y);

        public double Dot(Vector2D other) => X * other.X + Y * other.Y;

        /// <summary>Z component of the 3D cross product.</summary>
        public double Cross(Vector2D other) => X * other.Y - Y * other.X;

        public static Vector2D operator *(Vector2D v, double k) => new Vector2D(v.X * k, v.Y * k);

        public Vector2D Normalized()
        {
            var length = Length;
            return length > 0 ? new Vector2D(X / length, Y / length) : this;
        }
    }

    /// <summary>Axis-aligned rectangle in the XY plane.</summary>
    public readonly struct Bounds2D
    {
        public Bounds2D(double minX, double minY, double maxX, double maxY)
        {
            MinX = Math.Min(minX, maxX);
            MinY = Math.Min(minY, maxY);
            MaxX = Math.Max(minX, maxX);
            MaxY = Math.Max(minY, maxY);
        }

        public double MinX { get; }

        public double MinY { get; }

        public double MaxX { get; }

        public double MaxY { get; }

        /// <summary>The ZCAD "eps box": a square with half-size <paramref name="halfSize"/> around a point.</summary>
        public static Bounds2D Around(Point2D center, double halfSize) =>
            new Bounds2D(center.X - halfSize, center.Y - halfSize, center.X + halfSize, center.Y + halfSize);

        public bool Contains(Point2D p) => p.X >= MinX && p.X <= MaxX && p.Y >= MinY && p.Y <= MaxY;

        public bool Intersects(Bounds2D other) =>
            MinX <= other.MaxX && other.MinX <= MaxX && MinY <= other.MaxY && other.MinY <= MaxY;

        public override string ToString() =>
            string.Format(CultureInfo.InvariantCulture, "[{0:0.###}; {1:0.###}]-[{2:0.###}; {3:0.###}]", MinX, MinY, MaxX, MaxY);
    }
}
