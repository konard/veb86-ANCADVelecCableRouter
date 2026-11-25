using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using VelecCableRouter.Core.Model;

namespace VelecCableRouter.AutoCAD
{
    /// <summary>
    /// Finds the connection point of a device block (issue #1, section 3): the definition is searched recursively,
    /// nested blocks are transformed with their <see cref="BlockReference.BlockTransform"/>. A nested block named
    /// CONNECTOR_POINT/CONNECTOR_SQUARE, an attribute definition with such a tag or a point on such a layer wins;
    /// direct children are checked before deeper levels. When nothing is found the caller falls back to Position.
    /// </summary>
    internal static class ConnectorFinder
    {
        public static Point3d? Find(Transaction transaction, BlockReference block) =>
            Find(transaction, block.BlockTableRecord, block.BlockTransform, 0);

        public static string EffectiveName(Transaction transaction, BlockReference block)
        {
            var id = block.IsDynamicBlock ? block.DynamicBlockTableRecord : block.BlockTableRecord;
            return ((BlockTableRecord)transaction.GetObject(id, OpenMode.ForRead)).Name;
        }

        private static Point3d? Find(Transaction transaction, ObjectId definitionId, Matrix3d transform, int depth)
        {
            if (depth >= ConnectorNames.MaxSearchDepth || definitionId.IsNull)
            {
                return null;
            }

            var definition = (BlockTableRecord)transaction.GetObject(definitionId, OpenMode.ForRead);
            foreach (var id in definition)
            {
                switch (transaction.GetObject(id, OpenMode.ForRead))
                {
                    case BlockReference nested when ConnectorNames.IsConnector(EffectiveName(transaction, nested)):
                        return nested.Position.TransformBy(transform);
                    case AttributeDefinition attribute when ConnectorNames.IsConnector(attribute.Tag):
                        return attribute.Position.TransformBy(transform);
                    case DBPoint point when ConnectorNames.IsConnector(point.Layer):
                        return point.Position.TransformBy(transform);
                }
            }

            foreach (var id in definition)
            {
                if (transaction.GetObject(id, OpenMode.ForRead) is BlockReference nested)
                {
                    var found = Find(transaction, nested.BlockTableRecord, transform * nested.BlockTransform, depth + 1);
                    if (found.HasValue)
                    {
                        return found;
                    }
                }
            }

            return null;
        }
    }
}
