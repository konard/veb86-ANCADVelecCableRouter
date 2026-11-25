using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using VelecCableRouter.Core.Geometry;
using VelecCableRouter.Core.Model;
using VelecCableRouter.Core.XData;

namespace VelecCableRouter.AutoCAD
{
    /// <summary>
    /// Reads the selected DWG primitives into AutoCAD-independent <see cref="RawEntity"/> objects (Z is dropped).
    /// Only objects with BobrovXDATA are opened further; everything else is reported as <see cref="RawEntityKind.Other"/>.
    /// </summary>
    internal static class AutoCadEntityReader
    {
        public static List<RawEntity> Read(Transaction transaction, IEnumerable<ObjectId> ids)
        {
            var result = new List<RawEntity>();
            foreach (var id in ids)
            {
                if (id.IsNull || id.IsErased)
                {
                    continue;
                }

                if (!(transaction.GetObject(id, OpenMode.ForRead, false) is Entity entity))
                {
                    continue;
                }

                var xdata = ReadXData(entity);
                if (xdata.Count == 0)
                {
                    continue; // no BobrovXDATA section: not a participant
                }

                var handle = entity.Handle.ToString();
                switch (entity)
                {
                    case Line line:
                        result.Add(RawEntity.Line(handle, P(line.StartPoint), P(line.EndPoint), xdata, line.Layer));
                        break;
                    case Polyline polyline:
                        result.Add(ReadPolyline(handle, polyline, xdata));
                        break;
                    case Polyline2d polyline2d:
                        result.Add(ReadPolyline2d(transaction, handle, polyline2d, xdata));
                        break;
                    case BlockReference block:
                        result.Add(ReadBlock(transaction, handle, block, xdata));
                        break;
                    default:
                        result.Add(new RawEntity(handle, RawEntityKind.Other, xdata) { Layer = entity.Layer });
                        break;
                }
            }

            return result;
        }

        private static List<XDataValue> ReadXData(Entity entity)
        {
            var values = new List<XDataValue>();
            using (var buffer = entity.GetXDataForApplication(BobrovXData.RegAppName))
            {
                if (buffer == null)
                {
                    return values;
                }

                foreach (TypedValue value in buffer)
                {
                    values.Add(new XDataValue(value.TypeCode, value.Value));
                }
            }

            return values;
        }

        private static RawEntity ReadPolyline(string handle, Polyline polyline, List<XDataValue> xdata)
        {
            var points = new List<Point2D>(polyline.NumberOfVertices);
            var hasArcs = false;
            for (var i = 0; i < polyline.NumberOfVertices; i++)
            {
                points.Add(P(polyline.GetPoint3dAt(i)));
                var isLastOpen = !polyline.Closed && i == polyline.NumberOfVertices - 1;
                hasArcs |= !isLastOpen && Math.Abs(polyline.GetBulgeAt(i)) > 1e-12;
            }

            var raw = RawEntity.Polyline(handle, points, xdata, polyline.Closed, polyline.Layer);
            raw.HasArcs = hasArcs;
            return raw;
        }

        private static RawEntity ReadPolyline2d(Transaction transaction, string handle, Polyline2d polyline, List<XDataValue> xdata)
        {
            var points = new List<Point2D>();
            var hasArcs = polyline.PolyType != Poly2dType.SimplePoly;
            foreach (ObjectId vertexId in polyline)
            {
                var vertex = (Vertex2d)transaction.GetObject(vertexId, OpenMode.ForRead);
                if (vertex.VertexType == Vertex2dType.SplineControlVertex)
                {
                    continue;
                }

                hasArcs |= Math.Abs(vertex.Bulge) > 1e-12;
                points.Add(P(polyline.VertexPosition(vertex)));
            }

            var raw = RawEntity.Polyline(handle, points, xdata, polyline.Closed, polyline.Layer);
            raw.HasArcs = hasArcs;
            return raw;
        }

        private static RawEntity ReadBlock(Transaction transaction, string handle, BlockReference block, List<XDataValue> xdata)
        {
            var connector = ConnectorFinder.Find(transaction, block);
            Bounds2D? extents = null;
            try
            {
                var box = block.GeometricExtents;
                extents = new Bounds2D(box.MinPoint.X, box.MinPoint.Y, box.MaxPoint.X, box.MaxPoint.Y);
            }
            catch (Autodesk.AutoCAD.Runtime.Exception)
            {
                // Empty or invisible blocks have no extents: the device then gets no tap edges (step 5 of the graph).
            }

            var raw = RawEntity.Block(handle, P(block.Position), xdata, connector.HasValue ? P(connector.Value) : (Point2D?)null, extents, block.Layer);
            raw.BlockName = ConnectorFinder.EffectiveName(transaction, block);
            return raw;
        }

        private static Point2D P(Point3d point) => new Point2D(point.X, point.Y);

        public static IEnumerable<ObjectId> Ids(Autodesk.AutoCAD.EditorInput.SelectionSet? selection) =>
            selection == null ? Enumerable.Empty<ObjectId>() : selection.GetObjectIds();
    }
}
