using VelecCableRouter.Core.Geometry;
using Xunit;
using static VelecCableRouter.Core.Tests.TestData;

namespace VelecCableRouter.Core.Tests
{
    public class GeometryTests
    {
        [Fact]
        public void IsWithinBox_UsesSquareBoxLikeZcadDublicateVertex()
        {
            Assert.True(GeometryUtils.IsWithinBox(P(0, 0), P(0.5, 0.5), 0.5));
            Assert.False(GeometryUtils.IsWithinBox(P(0, 0), P(0.51, 0), 0.5));
        }

        [Fact]
        public void Extend_LengthensBothEnds()
        {
            var (start, end) = GeometryUtils.Extend(P(0, 0), P(10, 0), 0.5);
            Assert.Equal(P(-0.5, 0), start);
            Assert.Equal(P(10.5, 0), end);
        }

        [Fact]
        public void TryIntersect_CrossingSegments()
        {
            Assert.True(GeometryUtils.TryIntersect(P(0, 0), P(10, 10), P(0, 10), P(10, 0), out var p));
            Assert.Equal(5, p.X, 9);
            Assert.Equal(5, p.Y, 9);
        }

        [Fact]
        public void TryIntersect_ParallelAndDisjointSegmentsDoNotIntersect()
        {
            Assert.False(GeometryUtils.TryIntersect(P(0, 0), P(10, 0), P(0, 1), P(10, 1), out _));
            Assert.False(GeometryUtils.TryIntersect(P(0, 0), P(10, 0), P(5, 0), P(15, 0), out _));
            Assert.False(GeometryUtils.TryIntersect(P(0, 0), P(4, 0), P(5, -1), P(5, 1), out _));
        }

        [Fact]
        public void TryIntersect_TJunctionFoundOnlyAfterExtension()
        {
            // The branch stops 0.3 before the main line: found only with the ZCAD extension by eps.
            Assert.False(GeometryUtils.TryIntersect(P(0, 0), P(10, 0), P(5, 0.3), P(5, 5), out _));
            var branch = GeometryUtils.Extend(P(5, 0.3), P(5, 5), 0.5);
            Assert.True(GeometryUtils.TryIntersect(P(0, 0), P(10, 0), branch.Start, branch.End, out var p));
            Assert.Equal(P(5, 0), p);
        }

        [Theory]
        [InlineData(5, 0.4, true)]
        [InlineData(5, 0.6, false)]
        [InlineData(-0.4, 0, true)]
        [InlineData(10.6, 0, false)]
        public void IsInSegmentArea_RectangleAroundSegment(double x, double y, bool inside)
        {
            Assert.Equal(inside, GeometryUtils.IsInSegmentArea(P(x, y), P(0, 0), P(10, 0), 0.5, out _));
        }

        [Fact]
        public void IsInSegmentArea_WorksForSlantedSegmentAndReturnsDistance()
        {
            Assert.True(GeometryUtils.IsInSegmentArea(P(3, 4), P(0, 0), P(6, 8), 0.1, out var distance));
            Assert.Equal(5, distance, 9);
        }

        [Fact]
        public void SimplifyCollinear_RemovesStraightIntermediateVertices()
        {
            var result = GeometryUtils.SimplifyCollinear(new[] { P(0, 0), P(1, 0), P(2, 0), P(2, 0), P(2, 5), P(2, 7) });
            Assert.Equal(new[] { P(0, 0), P(2, 0), P(2, 7) }, result);
        }

        [Fact]
        public void SimplifyCollinear_UsesTolerance1e6()
        {
            Assert.Equal(2, GeometryUtils.SimplifyCollinear(new[] { P(0, 0), P(5, 5e-7), P(10, 0) }).Count);
            Assert.Equal(3, GeometryUtils.SimplifyCollinear(new[] { P(0, 0), P(5, 1e-3), P(10, 0) }).Count);
        }

        [Fact]
        public void SimplifyCollinear_KeepsTurnBackVertex()
        {
            var result = GeometryUtils.SimplifyCollinear(new[] { P(0, 0), P(10, 0), P(5, 0) });
            Assert.Equal(3, result.Count);
        }

        [Fact]
        public void Bounds_IntersectsAndContains()
        {
            var box = Bounds2D.Around(P(0, 0), 1);
            Assert.True(box.Contains(P(1, -1)));
            Assert.True(box.Intersects(new Bounds2D(1, 1, 3, 3)));
            Assert.False(box.Intersects(new Bounds2D(1.1, 0, 3, 3)));
        }
    }
}
