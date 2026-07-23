using GrxCAD.ApplicationServices;
using GrxCAD.DatabaseServices;
using GrxCAD.EditorInput;
using GrxCAD.Geometry;
using GrxCAD.Runtime;
using System;
using System.Globalization;
using System.IO;
using System.Reflection;

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

            var views = new[]
            {
                new { Name = "前视图", Dir = new Vector3d(0, 1, 0) },
                new { Name = "后视图", Dir = new Vector3d(0, -1, 0) },
                new { Name = "左视图", Dir = new Vector3d(1, 0, 0) },
                new { Name = "右视图", Dir = new Vector3d(-1, 0, 0) },
            };

            foreach (var view in views)
            {
                try
                {
                    GenerateViewByCOM(doc, center, view.Dir, minPt.Value, maxPt.Value);
                    ed.WriteMessage(string.Format("\n{0} — COM方式生成成功.", view.Name));
                }
                catch (System.Exception ex)
                {
                    ed.WriteMessage(string.Format("\n{0} — COM方式失败: {1}", view.Name, ex.Message));
                    try
                    {
                        GenerateViewBySendCommand(doc, center, view.Dir, minPt.Value, maxPt.Value);
                        ed.WriteMessage(string.Format("\n{0} — SendCommand方式生成成功.", view.Name));
                    }
                    catch (System.Exception ex2)
                    {
                        ed.WriteMessage(string.Format("\n{0} — SendCommand方式也失败: {1}", view.Name, ex2.Message));
                    }
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

        private void GenerateViewByCOM(Document doc, Point3d center, Vector3d dir,
            Point3d minPt, Point3d maxPt)
        {
            dynamic comDoc = doc.AcadDocument;
            var normal = dir.GetNormal();

            Vector3d uRef = Math.Abs(normal.X) < 0.9 ? Vector3d.XAxis : Vector3d.ZAxis;
            Vector3d u = normal.CrossProduct(uRef).GetNormal();
            var halfSize = center.DistanceTo(maxPt) * ViewScaleFactor;

            var fromPt = center + u * halfSize;
            var toPt = center - u * halfSize;
            var viewSide = center + normal * halfSize;

            double[] fromArr = { fromPt.X, fromPt.Y, fromPt.Z };
            double[] toArr = { toPt.X, toPt.Y, toPt.Z };
            double[] viewArr = { viewSide.X, viewSide.Y, viewSide.Z };

            dynamic section = comDoc.ModelSpace.AddSection(fromArr, toArr, viewArr);
            if (section == null)
            {
                throw new InvalidOperationException("COM AddSection returned null.");
            }

            try
            {
                section.Enabled = true;
            }
            catch
            {
                // COM Enabled property may not be supported on all versions; non-fatal.
            }

            object sectionObj = (object)section;
            Type sectionType = sectionObj.GetType();

            object[] args = new object[6];
            args[0] = sectionObj;  // pEntity: section plane itself
            args[1] = null;  // pIntersectionBoundaryObjs (ref)
            args[2] = null;  // pIntersectionFillObjs (ref)
            args[3] = null;  // pBackgroudnObjs (ref)
            args[4] = null;  // pForegroudObjs (ref)
            args[5] = null;  // pCurveTangencyObjs (ref)

            ParameterModifier[] mods = new ParameterModifier[1];
            mods[0] = new ParameterModifier(6);
            mods[0][1] = true;
            mods[0][2] = true;
            mods[0][3] = true;
            mods[0][4] = true;
            mods[0][5] = true;

            sectionType.InvokeMember(
                "GenerateSectionGeometry",
                BindingFlags.InvokeMethod,
                null,
                sectionObj,
                args,
                mods,
                null,
                null);
        }

        private void GenerateViewBySendCommand(Document doc, Point3d center, Vector3d dir,
            Point3d minPt, Point3d maxPt)
        {
            var normal = dir.GetNormal();
            Vector3d uRef = Math.Abs(normal.X) < 0.9 ? Vector3d.XAxis : Vector3d.ZAxis;
            Vector3d u = normal.CrossProduct(uRef).GetNormal();
            var halfSize = center.DistanceTo(maxPt) * ViewScaleFactor;

            var fromPt = center + u * halfSize;
            var toPt = center - u * halfSize;
            var viewSide = center + normal * halfSize;

            var cmd = string.Format(CultureInfo.InvariantCulture,
                "SECTIONPLANE {0:F6},{1:F6},{2:F6} {3:F6},{4:F6},{5:F6} {6:F6},{7:F6},{8:F6} ",
                fromPt.X, fromPt.Y, fromPt.Z,
                toPt.X, toPt.Y, toPt.Z,
                viewSide.X, viewSide.Y, viewSide.Z);
            doc.SendStringToExecute(cmd, true, false, false);
        }
    }
}
