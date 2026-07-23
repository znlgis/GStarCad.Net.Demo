using GrxCAD.ApplicationServices;
using GrxCAD.DatabaseServices;
using GrxCAD.EditorInput;
using GrxCAD.Geometry;
using GrxCAD.Runtime;
using System;
using System.Globalization;
using System.IO;
using System.Threading;

using Thread = System.Threading.Thread;
using SysException = System.Exception;

namespace GStarCad.Net.Demo.Commands
{
    public class FlatExportCommand
    {
        private const double ViewScaleFactor = 1.5;

        [CommandMethod("FLATEXPORT")]
        public void FlatExport()
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

            var center = new Point3d(
                (minPt.Value.X + maxPt.Value.X) / 2.0,
                (minPt.Value.Y + maxPt.Value.Y) / 2.0,
                (minPt.Value.Z + maxPt.Value.Z) / 2.0);

            var extent = center.DistanceTo(maxPt.Value) * ViewScaleFactor * 2;

            var views = new[]
            {
                new { Name = "前视图", Dir = new Vector3d(0, -1, 0), InsX = 0.0, InsY = 0.0 },
                new { Name = "后视图", Dir = new Vector3d(0,  1, 0), InsX = extent * 3, InsY = 0.0 },
                new { Name = "左视图", Dir = new Vector3d(-1, 0, 0), InsX = 0.0, InsY = extent * 3 },
                new { Name = "右视图", Dir = new Vector3d(1,  0, 0), InsX = extent * 3, InsY = extent * 3 },
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
                string.Format("{0}_{1}_flat.dwg", originalName, timestamp));

            try { Application.SetSystemVariable("CMDDIA", 0); } catch { }
            try { Application.SetSystemVariable("FILEDIA", 0); } catch { }
            try { Application.SetSystemVariable("ATTDIA", 0); } catch { }

            dynamic comDoc = doc.AcadDocument;
            int successCount = 0;

            foreach (var view in views)
            {
                try
                {
                    SetOrthographicView(ed, center, view.Dir, extent);
                    Thread.Sleep(200);

                    var cmdFlat = string.Format(CultureInfo.InvariantCulture,
                        "FLATSHOT {0:F6},{1:F6},0 1 1 0 ",
                        view.InsX, view.InsY);
                    comDoc.SendCommand(cmdFlat);

                    ed.WriteMessage(string.Format("\n{0} — FLATSHOT 已发送.", view.Name));
                    successCount++;
                }
                catch (SysException ex)
                {
                    ed.WriteMessage(string.Format(
                        "\n{0} — 失败: {1}", view.Name, ex.Message));
                }
            }

            if (successCount == 0)
            {
                ed.WriteMessage("\n所有视图生成失败, 未输出文件.");
                return;
            }

            db.SaveAs(outputPath, DwgVersion.Current);

            ed.WriteMessage(string.Format(
                "\nFLATEXPORT 完成 ({0}/4). 输出文件: {1}", successCount, outputPath));
        }

        private void SetOrthographicView(Editor ed, Point3d target,
            Vector3d viewDir, double extent)
        {
            var viewRec = new ViewTableRecord();
            viewRec.Target = target;
            viewRec.ViewDirection = viewDir;
            viewRec.Height = extent;
            viewRec.Width = extent;
            viewRec.CenterPoint = new Point2d(0, 0);
            ed.SetCurrentView(viewRec);
        }
    }
}
