using System;
using System.Linq;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using VelecCableRouter.Core.Reporting;
using VelecCableRouter.Core.Routing;
using VelecCableRouter.Core.XData;

namespace VelecCableRouter.AutoCAD
{
    /// <summary>Writes cables and diagnostic geometry inside the command transaction.</summary>
    internal sealed class DrawingWriter
    {
        private readonly Database database;
        private readonly Transaction transaction;
        private readonly BlockTableRecord space;

        public DrawingWriter(Database database, Transaction transaction)
        {
            this.database = database;
            this.transaction = transaction;
            space = (BlockTableRecord)transaction.GetObject(database.CurrentSpaceId, OpenMode.ForWrite);
        }

        /// <summary>Adds <see cref="BobrovXData.RegAppName"/> to the RegAppTable when it is missing.</summary>
        public void EnsureRegApp()
        {
            var table = (RegAppTable)transaction.GetObject(database.RegAppTableId, OpenMode.ForRead);
            if (table.Has(BobrovXData.RegAppName))
            {
                return;
            }

            table.UpgradeOpen();
            var record = new RegAppTableRecord { Name = BobrovXData.RegAppName };
            table.Add(record);
            transaction.AddNewlyCreatedDBObject(record, true);
        }

        /// <summary>Creates the cable polyline with BobrovXDATA v2 (TEMPLATE = Cable, a new DEVICE_ID).</summary>
        public Polyline AddCable(CableRoute cable, bool useSuperLineLayer)
        {
            var polyline = new Polyline(cable.Points.Count);
            polyline.SetDatabaseDefaults(database);
            for (var i = 0; i < cable.Points.Count; i++)
            {
                polyline.AddVertexAt(i, new Point2d(cable.Points[i].X, cable.Points[i].Y), 0, 0, 0);
            }

            polyline.Layer = useSuperLineLayer && cable.Layer != null && LayerExists(cable.Layer) ? cable.Layer : CurrentLayerName();
            space.AppendEntity(polyline);
            transaction.AddNewlyCreatedDBObject(polyline, true);
            var values = BobrovXData.BuildCable(cable.SuperLineName, cable.HeadDeviceName, cable.GroupName, Guid.NewGuid());
            using (var buffer = new ResultBuffer(values.Select(v => new TypedValue(v.TypeCode, v.Value)).ToArray()))
            {
                polyline.XData = buffer;
            }

            return polyline;
        }

        /// <summary>Replaces the geometry of the debug layer of one superline in the current space.</summary>
        public int DrawDebug(DebugDrawing drawing)
        {
            var layerId = EnsureLayer(drawing.LayerName);
            EraseOnLayers(space, name => string.Equals(name, drawing.LayerName, StringComparison.OrdinalIgnoreCase));
            var count = 0;
            foreach (var circle in drawing.Circles)
            {
                Add(new Circle(new Point3d(circle.Center.X, circle.Center.Y, 0), Vector3d.ZAxis, circle.Radius), layerId, circle.Color);
                count++;
            }

            foreach (var line in drawing.Lines)
            {
                Add(new Line(new Point3d(line.Start.X, line.Start.Y, 0), new Point3d(line.End.X, line.End.Y, 0)), layerId, line.Color);
                count++;
            }

            foreach (var text in drawing.Texts)
            {
                var value = text.Text.Length > 250 ? text.Text.Substring(0, 250) : text.Text;
                Add(new DBText { Position = new Point3d(text.Position.X, text.Position.Y, 0), Height = text.Height, TextString = value }, layerId, text.Color);
                count++;
            }

            return count;
        }

        /// <summary>Erases the entities of every layout whose layer matches <paramref name="isTarget"/>.</summary>
        public static int EraseFromLayouts(Database database, Transaction transaction, Func<string, bool> isTarget)
        {
            UnlockLayers(database, transaction, isTarget);
            var blocks = (BlockTable)transaction.GetObject(database.BlockTableId, OpenMode.ForRead);
            var erased = 0;
            foreach (var id in blocks)
            {
                var block = (BlockTableRecord)transaction.GetObject(id, OpenMode.ForRead);
                if (block.IsLayout)
                {
                    erased += EraseOnLayers(block, isTarget, transaction);
                }
            }

            return erased;
        }

        private int EraseOnLayers(BlockTableRecord block, Func<string, bool> isTarget) => EraseOnLayers(block, isTarget, transaction);

        private static int EraseOnLayers(BlockTableRecord block, Func<string, bool> isTarget, Transaction transaction)
        {
            var erased = 0;
            foreach (var id in block)
            {
                var entity = (Entity)transaction.GetObject(id, OpenMode.ForRead);
                if (isTarget(entity.Layer))
                {
                    entity.UpgradeOpen();
                    entity.Erase();
                    erased++;
                }
            }

            return erased;
        }

        private static void UnlockLayers(Database database, Transaction transaction, Func<string, bool> isTarget)
        {
            var layers = (LayerTable)transaction.GetObject(database.LayerTableId, OpenMode.ForRead);
            foreach (var id in layers)
            {
                var layer = (LayerTableRecord)transaction.GetObject(id, OpenMode.ForRead);
                if (layer.IsLocked && isTarget(layer.Name))
                {
                    layer.UpgradeOpen();
                    layer.IsLocked = false;
                }
            }
        }

        private void Add(Entity entity, ObjectId layerId, short color)
        {
            entity.SetDatabaseDefaults(database);
            entity.LayerId = layerId;
            entity.Color = Color.FromColorIndex(ColorMethod.ByAci, color);
            space.AppendEntity(entity);
            transaction.AddNewlyCreatedDBObject(entity, true);
        }

        private ObjectId EnsureLayer(string name)
        {
            var layers = (LayerTable)transaction.GetObject(database.LayerTableId, OpenMode.ForRead);
            if (layers.Has(name))
            {
                var existing = (LayerTableRecord)transaction.GetObject(layers[name], OpenMode.ForRead);
                if (existing.IsLocked || existing.IsFrozen || existing.IsOff)
                {
                    existing.UpgradeOpen();
                    existing.IsLocked = false;
                    existing.IsOff = false;
                    if (existing.ObjectId != database.Clayer)
                    {
                        existing.IsFrozen = false;
                    }
                }

                return existing.ObjectId;
            }

            layers.UpgradeOpen();
            var layer = new LayerTableRecord { Name = name, IsPlottable = false };
            var id = layers.Add(layer);
            transaction.AddNewlyCreatedDBObject(layer, true);
            return id;
        }

        private bool LayerExists(string name) =>
            ((LayerTable)transaction.GetObject(database.LayerTableId, OpenMode.ForRead)).Has(name);

        private string CurrentLayerName() =>
            ((LayerTableRecord)transaction.GetObject(database.Clayer, OpenMode.ForRead)).Name;
    }
}
