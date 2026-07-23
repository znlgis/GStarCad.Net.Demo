using System;
using System.IO;
using System.Reflection;
using System.Threading;
using GrxCAD.ApplicationServices;
using GrxCAD.DatabaseServices;
using GrxCAD.EditorInput;
using GrxCAD.Geometry;
using GrxCAD.Runtime;
using Exception = System.Exception;

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
                new[] { new TypedValue((int)DxfCode.Start, "3DSOLID") });

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
                new { Name = "前视图", Vp = new Point3d(0, -1, 0), Ins = new Point3d(0, 0, 0) },
                new { Name = "后视图", Vp = new Point3d(0, 1, 0), Ins = new Point3d(extent * 3, 0, 0) },
                new { Name = "左视图", Vp = new Point3d(-1, 0, 0), Ins = new Point3d(0, extent * 3, 0) },
                new { Name = "右视图", Vp = new Point3d(1, 0, 0), Ins = new Point3d(extent * 3, extent * 3, 0) }
            };

            var assemblyDir = Path.GetDirectoryName(
                Assembly.GetExecutingAssembly().Location);
            var tempDir = Path.Combine(assemblyDir, "temp");
            if (!Directory.Exists(tempDir)) Directory.CreateDirectory(tempDir);

            var originalName = Path.GetFileNameWithoutExtension(db.Filename);
            if (string.IsNullOrEmpty(originalName)) originalName = "untitled";
            var timestamp = DateTime.Now.ToString("yyyyMMddHHmmss");
            var outputPath = Path.Combine(tempDir,
                string.Format("{0}_{1}_flat.dwg", originalName, timestamp));

            try
            {
                Application.SetSystemVariable("CMDDIA", 0);
            }
            catch
            {
            }

            try
            {
                Application.SetSystemVariable("FILEDIA", 0);
            }
            catch
            {
            }

            var successCount = 0;

            foreach (var view in views)
                try
                {
                    var vp = view.Vp;
                    var ins = view.Ins;

                    ed.Command("VPOINT", vp);
                    Thread.Sleep(200);

                    ed.Command("FLATSHOT", ins, 1.0, 1.0, 0.0);

                    ed.WriteMessage(string.Format("\n{0} — FLATSHOT 完成.", view.Name));
                    successCount++;
                }
                catch (Exception ex)
                {
                    ed.WriteMessage(string.Format(
                        "\n{0} — 失败: {1}", view.Name, ex.Message));
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
    }
}