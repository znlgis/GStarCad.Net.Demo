using GrxCAD.ApplicationServices;
using GrxCAD.DatabaseServices;
using GrxCAD.EditorInput;
using GrxCAD.Geometry;
using GrxCAD.Runtime;
using System;
using System.Globalization;
using System.IO;

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

            var center = new Point3d(
                (minPt.Value.X + maxPt.Value.X) / 2.0,
                (minPt.Value.Y + maxPt.Value.Y) / 2.0,
                (minPt.Value.Z + maxPt.Value.Z) / 2.0);

            // Suppress FLATSHOT dialog
            Application.SetSystemVariable("CMDDIA", 0);
            Application.SetSystemVariable("FILEDIA", 0);
            try
            {
                var views = new[]
                {
                    new { Name = "前视图", Dir = new Vector3d(0,  1, 0) },
                    new { Name = "后视图", Dir = new Vector3d(0, -1, 0) },
                    new { Name = "左视图", Dir = new Vector3d(1,  0, 0) },
                    new { Name = "右视图", Dir = new Vector3d(-1, 0, 0) },
                };

                foreach (var view in views)
                {
                    try
                    {
                        SetViewAndFlatshot(doc, ed, center, view.Dir);
                        ed.WriteMessage(string.Format("\n{0} — 生成成功.", view.Name));
                    }
                    catch (System.Exception ex)
                    {
                        ed.WriteMessage(string.Format("\n{0} — 失败: {1}", view.Name, ex.Message));
                    }
                }

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

                db.SaveAs(outputPath, DwgVersion.Current);

                ed.WriteMessage(string.Format("\n视图导出完成. 输出文件: {0}", outputPath));
            }
            finally
            {
                Application.SetSystemVariable("CMDDIA", 1);
                Application.SetSystemVariable("FILEDIA", 1);
            }
        }

        private void SetViewAndFlatshot(Document doc, Editor ed, Point3d center, Vector3d dir)
        {
            // 1. Set camera to orthographic view direction
            var view = new ViewTableRecord();
            view.ViewDirection = dir;
            view.Target = center;
            view.CenterPoint = new Point2d(0, 0);
            view.Height = 200;
            view.Width = 200;
            ed.SetCurrentView(view);

            // 2. Run FLATSHOT to generate 2D projection from the current view.
            // FLATSHOT captures the current viewport's display and flattens it to 2D.
            doc.SendStringToExecute(
                "FLATSHOT ", true, false, false);
        }
    }
}
