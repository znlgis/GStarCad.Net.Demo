using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using GrxCAD.ApplicationServices;
using GrxCAD.Colors;
using GrxCAD.DatabaseServices;
using GrxCAD.EditorInput;
using GrxCAD.Geometry;
using GrxCAD.Runtime;
using Exception = System.Exception;

namespace GStarCad.Net.Demo.Commands
{
    public class SolprofExportCommand
    {
        private const double ViewScaleFactor = 1.5;

        [CommandMethod("SOLPROFEXPORT")]
        public void SolprofExport()
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

            var solidIds = selRes.Value.GetObjectIds();

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

            var assemblyDir = Path.GetDirectoryName(
                Assembly.GetExecutingAssembly().Location);
            var tempDir = Path.Combine(assemblyDir, "temp");
            if (!Directory.Exists(tempDir)) Directory.CreateDirectory(tempDir);

            var originalName = Path.GetFileNameWithoutExtension(db.Filename);
            if (string.IsNullOrEmpty(originalName)) originalName = "untitled";
            var timestamp = DateTime.Now.ToString("yyyyMMddHHmmss");
            var outputPath = Path.Combine(tempDir,
                string.Format("{0}_{1}_solprof.dwg", originalName, timestamp));

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

            var viewDefs = new[]
            {
                new { Name = "俯视图", Key = "_TOP",    GridX = 0.0, GridY = 0.0 },
                new { Name = "前视图", Key = "_FRONT",  GridX = 1.0, GridY = 0.0 },
                new { Name = "仰视图", Key = "_BOTTOM", GridX = 2.0, GridY = 0.0 },
                new { Name = "左视图", Key = "_LEFT",   GridX = 0.0, GridY = 1.0 },
                new { Name = "后视图", Key = "_BACK",   GridX = 1.0, GridY = 1.0 },
                new { Name = "右视图", Key = "_RIGHT",  GridX = 2.0, GridY = 1.0 }
            };

            var tileSpan = extent * 3;

            var lm = LayoutManager.Current;
            var tempLayoutName = "SOLPROF_TEMP_" + DateTime.Now.Ticks;
            var tempLayoutId = lm.CreateLayout(tempLayoutName);
            lm.CurrentLayout = tempLayoutName;

            ObjectId layoutBtrId;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var layout = (Layout)tr.GetObject(tempLayoutId, OpenMode.ForRead);
                layoutBtrId = layout.BlockTableRecordId;
                tr.Commit();
            }

            ObjectId vpId;
            string vpHandleStr;
            var vpCenterPt = new Point3d(extent * 20, extent * 20, 0);
            var vpWidth = extent * 3;
            var vpHeight = extent * 3;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var btr = (BlockTableRecord)tr.GetObject(layoutBtrId, OpenMode.ForWrite);

                var vp = new Viewport();
                vp.CenterPoint = vpCenterPt;
                vp.Width = vpWidth;
                vp.Height = vpHeight;
                vp.On = true;

                btr.AppendEntity(vp);
                tr.AddNewlyCreatedDBObject(vp, true);
                vpId = vp.ObjectId;
                vpHandleStr = vp.Handle.ToString();

                tr.Commit();
            }

            var successCount = 0;
            var processedIds = new HashSet<ObjectId>();

            foreach (var view in viewDefs)
            {
                try
                {
                    ed.Command("MSPACE");
                    Thread.Sleep(200);

                    ed.Command("-VIEW", view.Key);
                    Thread.Sleep(300);

                    ed.Command("ZOOM", "E");
                    Thread.Sleep(200);

                    ed.CurrentUserCoordinateSystem = Matrix3d.Identity;

                    var args = new List<object> { "SOLPROF" };
                    foreach (var id in solidIds)
                        args.Add(id);
                    args.Add("");
                    args.Add("Y");
                    args.Add("Y");
                    args.Add("Y");
                    ed.Command(args.ToArray());

                    Thread.Sleep(500);

                    ed.Command("PSPACE");
                    Thread.Sleep(200);

                    ed.CurrentUserCoordinateSystem = Matrix3d.Identity;

                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                        var ltt = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead);

                        var layerVis = "PV-" + vpHandleStr;
                        var layerHid = "PH-" + vpHandleStr;

                        ObjectId hiddenLtId;
                        if (ltt.Has("HIDDEN"))
                            hiddenLtId = ltt["HIDDEN"];
                        else if (ltt.Has("DASHED"))
                            hiddenLtId = ltt["DASHED"];
                        else
                            hiddenLtId = ltt["Continuous"];

                        if (lt.Has(layerVis))
                        {
                            var ltr = (LayerTableRecord)tr.GetObject(lt[layerVis], OpenMode.ForWrite);
                            ltr.Color = Color.FromColorIndex(ColorMethod.ByAci, 7);
                            ltr.LinetypeObjectId = ltt["Continuous"];
                        }

                        if (lt.Has(layerHid))
                        {
                            var ltr = (LayerTableRecord)tr.GetObject(lt[layerHid], OpenMode.ForWrite);
                            ltr.Color = Color.FromColorIndex(ColorMethod.ByAci, 5);
                            ltr.LinetypeObjectId = hiddenLtId;
                        }

                        tr.Commit();
                    }

                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                        var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);

                        var layerVis = "PV-" + vpHandleStr;
                        var layerHid = "PH-" + vpHandleStr;

                        var toMove = new List<ObjectId>();

                        void ScanBlock(ObjectId btrId)
                        {
                            var btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead);
                            foreach (ObjectId objId in btr)
                            {
                                if (!objId.IsValid || objId == vpId) continue;
                                if (processedIds.Contains(objId)) continue;

                                var ent = (Entity)tr.GetObject(objId, OpenMode.ForRead);
                                var ltr = (LayerTableRecord)tr.GetObject(ent.LayerId, OpenMode.ForRead);
                                var name = ltr.Name;
                                if (name == layerVis || name == layerHid)
                                    toMove.Add(objId);
                            }
                        }

                        ScanBlock(bt[BlockTableRecord.PaperSpace]);
                        if (toMove.Count == 0)
                            ScanBlock(layoutBtrId);
                        if (toMove.Count == 0)
                            ScanBlock(bt[BlockTableRecord.ModelSpace]);

                        ed.WriteMessage(string.Format(
                            "\n  [DEBUG] 本轮找到实体: {0}", toMove.Count));

                        if (toMove.Count > 0)
                        {
                            Extents3d groupExt = default;
                            var first = true;

                            foreach (ObjectId objId in toMove)
                            {
                                var ent = (Entity)tr.GetObject(objId, OpenMode.ForRead);
                                var ext = ent.GeometricExtents;
                                if (first)
                                {
                                    groupExt = ext;
                                    first = false;
                                }
                                else
                                {
                                    groupExt.AddPoint(ext.MinPoint);
                                    groupExt.AddPoint(ext.MaxPoint);
                                }
                            }

                            var curCenterX = (groupExt.MinPoint.X + groupExt.MaxPoint.X) / 2.0;
                            var curCenterY = (groupExt.MinPoint.Y + groupExt.MaxPoint.Y) / 2.0;
                            var curCenterZ = (groupExt.MinPoint.Z + groupExt.MaxPoint.Z) / 2.0;

                            var centerPt = new Point3d(curCenterX, curCenterY, curCenterZ);

                            var dx = groupExt.MaxPoint.X - groupExt.MinPoint.X;
                            var dy = groupExt.MaxPoint.Y - groupExt.MinPoint.Y;
                            var dz = groupExt.MaxPoint.Z - groupExt.MinPoint.Z;
                            var maxDim = Math.Max(dx, Math.Max(dy, dz));

                            Matrix3d rotMat;
                            string planeInfo;
                            if (maxDim < 1e-9)
                            {
                                rotMat = Matrix3d.Identity;
                                planeInfo = "退化为点";
                            }
                            else if (dz > maxDim * 0.1 && dy < maxDim * 0.1)
                            {
                                rotMat = Matrix3d.Rotation(-Math.PI / 2.0, Vector3d.XAxis, centerPt);
                                planeInfo = "XZ→XY";
                            }
                            else if (dz > maxDim * 0.1 && dx < maxDim * 0.1)
                            {
                                rotMat = Matrix3d.Rotation(-Math.PI / 2.0, Vector3d.YAxis, centerPt);
                                planeInfo = "YZ→XY";
                            }
                            else
                            {
                                rotMat = Matrix3d.Identity;
                                planeInfo = "XY";
                            }

                            var targetX = view.GridX * tileSpan;
                            var targetY = view.GridY * tileSpan;

                            var translateMat = Matrix3d.Displacement(new Vector3d(
                                targetX - curCenterX, targetY - curCenterY, -curCenterZ));

                            var mat = rotMat * translateMat;

                            foreach (ObjectId objId in toMove)
                            {
                                var ent = (Entity)tr.GetObject(objId, OpenMode.ForWrite);
                                ent.TransformBy(mat);
                                processedIds.Add(objId);
                            }

                            ed.WriteMessage(string.Format(
                                " [{0}]", planeInfo));
                        }

                        tr.Commit();
                    }

                    ed.WriteMessage(string.Format("\n{0} — SOLPROF 完成.", view.Name));
                    successCount++;
                }
                catch (Exception ex)
                {
                    ed.WriteMessage(string.Format(
                        "\n{0} — 失败: {1}", view.Name, ex.Message));

                    try { ed.Command("PSPACE"); }
                    catch { }
                }
            }

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var vp = (Viewport)tr.GetObject(vpId, OpenMode.ForWrite);
                vp.Erase();
                tr.Commit();
            }

            using (var tr = db.TransactionManager.StartTransaction())
            {
                foreach (ObjectId id in solidIds)
                {
                    var ent = (Entity)tr.GetObject(id, OpenMode.ForWrite);
                    ent.Erase();
                }

                tr.Commit();
            }

            if (successCount == 0)
            {
                ed.WriteMessage("\n所有视图生成失败, 未输出文件.");
                return;
            }

            db.SaveAs(outputPath, DwgVersion.Current);

            ed.WriteMessage(string.Format(
                "\nSOLPROFEXPORT 完成 ({0}/6). 输出文件: {1}", successCount, outputPath));
        }
    }
}
