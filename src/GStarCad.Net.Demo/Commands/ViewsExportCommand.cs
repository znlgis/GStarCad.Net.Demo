using GrxCAD.ApplicationServices;
using GrxCAD.DatabaseServices;
using GrxCAD.EditorInput;
using GrxCAD.Geometry;
using GrxCAD.Runtime;
using System;
using System.Globalization;
using System.IO;
using System.Threading;

namespace GStarCad.Net.Demo.Commands
{
    public class ViewsExportCommand
    {
        private const double ViewScaleFactor = 1.5;

        [CommandMethod("VIEWEXPORT")]
        public void ViewsExport()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            var selOpts = new PromptSelectionOptions();
            selOpts.MessageForAdding = "\n选择3D实体: ";

            var filter = new SelectionFilter(
                new TypedValue[] { new TypedValue((int)DxfCode.Start, "3DSOLID") });

            var selRes = ed.GetSelection(selOpts, filter);
            if (selRes.Status != PromptStatus.OK)
            {
                ed.WriteMessage("\n未选择任何3D实体.");
                return;
            }

            Point3d? minPt = null;
            Point3d? maxPt = null;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                foreach (SelectedObject selObj in selRes.Value)
                {
                    var ent = (Entity)tr.GetObject(selObj.ObjectId, OpenMode.ForRead);
                    var ext = ent.GeometricExtents;

                    if (minPt == null)
                    {
                        minPt = ext.MinPoint;
                        maxPt = ext.MaxPoint;
                    }
                    else
                    {
                        minPt = new Point3d(
                            Math.Min(minPt.Value.X, ext.MinPoint.X),
                            Math.Min(minPt.Value.Y, ext.MinPoint.Y),
                            Math.Min(minPt.Value.Z, ext.MinPoint.Z));
                        maxPt = new Point3d(
                            Math.Max(maxPt.Value.X, ext.MaxPoint.X),
                            Math.Max(maxPt.Value.Y, ext.MaxPoint.Y),
                            Math.Max(maxPt.Value.Z, ext.MaxPoint.Z));
                    }
                }
                tr.Commit();
            }

            if (minPt == null || maxPt == null)
            {
                ed.WriteMessage("\n无法计算实体包围盒.");
                return;
            }

            var sizeX = maxPt.Value.X - minPt.Value.X;
            var sizeY = maxPt.Value.Y - minPt.Value.Y;
            var gridOffset = Math.Max(sizeX, sizeY) * ViewScaleFactor;

            var center = new Point3d(
                (minPt.Value.X + maxPt.Value.X) / 2.0,
                (minPt.Value.Y + maxPt.Value.Y) / 2.0,
                (minPt.Value.Z + maxPt.Value.Z) / 2.0);

            // View direction + insertion point (2x2 grid layout)
            var viewLayouts = new[]
            {
                new { Name = "前视图", Dir = new Vector3d(0,  1, 0), InsX = 0.0, InsY = 0.0 },
                new { Name = "后视图", Dir = new Vector3d(0, -1, 0), InsX = gridOffset, InsY = 0.0 },
                new { Name = "左视图", Dir = new Vector3d(1,  0, 0), InsX = 0.0, InsY = gridOffset },
                new { Name = "右视图", Dir = new Vector3d(-1, 0, 0), InsX = gridOffset, InsY = gridOffset },
            };

            var assemblyDir = Path.GetDirectoryName(
                System.Reflection.Assembly.GetExecutingAssembly().Location);
            var tempDir = Path.Combine(assemblyDir, "temp");
            if (!Directory.Exists(tempDir))
            {
                Directory.CreateDirectory(tempDir);
            }

            var originalName = Path.GetFileNameWithoutExtension(db.Filename);
            if (string.IsNullOrEmpty(originalName))
            {
                originalName = "untitled";
            }
            var timestamp = DateTime.Now.ToString("yyyyMMddHHmmss");
            var outputPath = Path.Combine(tempDir,
                string.Format("{0}_{1}_views.dwg", originalName, timestamp));

            dynamic comDoc = doc.AcadDocument;
            int successCount = 0;

            foreach (var vl in viewLayouts)
            {
                try
                {
                    Generate2DViewSync(comDoc, center, vl.Dir, minPt.Value, maxPt.Value,
                        vl.InsX, vl.InsY);
                    ed.WriteMessage(string.Format("\n{0} — 生成成功.", vl.Name));
                    successCount++;
                }
                catch (System.Exception ex)
                {
                    ed.WriteMessage(string.Format(
                        "\n{0} — 失败: {1}", vl.Name, ex.Message));
                }
            }

            if (successCount == 0)
            {
                ed.WriteMessage("\n所有视图生成失败, 未输出文件.");
                return;
            }

            // Save current document (contains 3D solids + 2D blocks) as output
            db.SaveAs(outputPath, DwgVersion.Current);

            ed.WriteMessage(string.Format(
                "\n视图导出完成 ({0}/4). 输出文件: {1}", successCount, outputPath));
        }

        private void Generate2DViewSync(dynamic comDoc, Point3d center, Vector3d viewDir,
            Point3d minPt, Point3d maxPt, double insX, double insY)
        {
            var normal = viewDir.GetNormal();
            Vector3d uRef = Math.Abs(normal.X) < 0.9 ? Vector3d.XAxis : Vector3d.ZAxis;
            Vector3d u = normal.CrossProduct(uRef).GetNormal();
            var halfSize = center.DistanceTo(maxPt) * ViewScaleFactor;

            var fromPt = center + u * halfSize;
            var toPt = center - u * halfSize;

            // GStarCAD SECTIONPLANE takes 2 points (from, to) — no third point.
            var cmdPlane = string.Format(CultureInfo.InvariantCulture,
                "SECTIONPLANE {0:F6},{1:F6},{2:F6} {3:F6},{4:F6},{5:F6} ",
                fromPt.X, fromPt.Y, fromPt.Z,
                toPt.X, toPt.Y, toPt.Z);
            comDoc.SendCommand(cmdPlane);
            Thread.Sleep(500);
            comDoc.SendCommand("REGEN ");
            Thread.Sleep(300);

            // Use COM to get the section plane handle, then select by handle.
            // _L is unreliable due to COM SendCommand async ordering.
            var sectionHandle = GetLastEntityHandle(comDoc);
            var cmdBlock = string.Format(CultureInfo.InvariantCulture,
                "SECTIONPLANETOBLOCK (handent \"{0}\") {1:F6},{2:F6},0 1 1 0 ",
                sectionHandle, insX, insY);
            comDoc.SendCommand(cmdBlock);
        }
        private string GetLastEntityHandle(dynamic comDoc)
        {
            var ms = comDoc.ModelSpace;
            int count = ms.Count;
            if (count == 0)
            {
                throw new InvalidOperationException("No entities in model space.");
            }
            return (string)ms.Item(count - 1).Handle;
        }
    }
}
