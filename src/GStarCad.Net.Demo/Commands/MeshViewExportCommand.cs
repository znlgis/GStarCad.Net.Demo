using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using GrxCAD.ApplicationServices;
using GrxCAD.DatabaseServices;
using GrxCAD.EditorInput;
using GrxCAD.Geometry;
using GrxCAD.Runtime;
using GStarCad.Net.Demo.Common;
using log4net;
using Exception = System.Exception;

namespace GStarCad.Net.Demo.Commands
{
    public class MeshViewExportCommand
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(MeshViewExportCommand));

        [CommandMethod("MESHVIEWEXPORT")]
        public void MeshViewExport()
        {
            Log.Debug("MeshViewExport() entered");
            var sw = Stopwatch.StartNew();

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

            // Prepare paths
            var assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            var tempDir = Path.Combine(assemblyDir, "temp");
            if (!Directory.Exists(tempDir)) Directory.CreateDirectory(tempDir);

            var originalName = Path.GetFileNameWithoutExtension(db.Filename);
            if (string.IsNullOrEmpty(originalName)) originalName = "untitled";
            var timestamp = DateTime.Now.ToString("yyyyMMddHHmmss");
            var stlPath = Path.Combine(tempDir, string.Format("{0}_{1}_mesh.stl", originalName, timestamp));
            var dwgPath = Path.Combine(tempDir, string.Format("{0}_{1}_mesh.dwg", originalName, timestamp));

            // Step 1: Export STL
            ed.WriteMessage("\n[1/3] 导出STL网格...");
            if (!ExportStl(doc, stlPath))
            {
                ed.WriteMessage("\nSTL导出失败: 文件未生成.");
                return;
            }
            ed.WriteMessage(" 完成.");

            // Step 2: Parse + Project
            ed.WriteMessage("\n[2/3] 计算正交投影...");

            List<StlTriangle> triangles;
            try
            {
                triangles = StlParser.Parse(stlPath);
            }
            catch (Exception ex)
            {
                Log.Error("STL parse failed", ex);
                ed.WriteMessage(string.Format("\nSTL解析失败: {0}", ex.Message));
                TryDeleteFile(stlPath);
                return;
            }

            if (triangles.Count == 0)
            {
                ed.WriteMessage("\n模型无可投影面.");
                TryDeleteFile(stlPath);
                return;
            }

            var projections = OrthoProjector.Project(triangles);

            var totalEdges = 0;
            var visibleEdges = 0;
            foreach (var p in projections)
                foreach (var e in p.Edges)
                {
                    totalEdges++;
                    if (e.IsVisible) visibleEdges++;
                }

            ed.WriteMessage(" 完成.");

            // Step 3: Arrange + Save DWG
            ed.WriteMessage("\n[3/3] 生成三视图DWG...");

            try
            {
                ViewArranger.ArrangeAndSave(projections, dwgPath);
            }
            catch (Exception ex)
            {
                Log.Error("DWG save failed", ex);
                ed.WriteMessage(string.Format("\nDWG生成失败: {0}", ex.Message));
                TryDeleteFile(stlPath);
                return;
            }

            TryDeleteFile(stlPath);

            sw.Stop();
            ed.WriteMessage(" 完成.");
            ed.WriteMessage(string.Format("\n\n=== MESHVIEWEXPORT 完成 ({0}ms) ===", sw.ElapsedMilliseconds));
            ed.WriteMessage(string.Format("\n输出文件: {0}", dwgPath));
            ed.WriteMessage(string.Format("\n视图: 主/俯/左 (第一角). 三角面数: {0}, 可见边: {1}, 隐藏边: {2}",
                triangles.Count, visibleEdges, totalEdges - visibleEdges));
        }

        private static bool ExportStl(Document doc, string stlPath)
        {
            // Raise surface tessellation quality so curved faces (cylinders, fillets) export with
            // finer, smoother facets; the original value is restored afterwards.
            object savedFacetres = null;
            try
            {
                savedFacetres = Application.GetSystemVariable("FACETRES");
                Application.SetSystemVariable("FACETRES", 10.0); // 10 = maximum tessellation quality
            }
            catch
            {
                /* FACETRES may be unavailable; proceed with defaults. */
            }

            try
            {
                doc.SendStringToExecute(
                    string.Format("_.FILEDIA 0 _.EXPORT {0} _.FILEDIA 1 ", stlPath.Replace('\\', '/')),
                    false, false, false);

                var deadline = DateTime.Now.AddSeconds(60);
                while (DateTime.Now < deadline)
                {
                    System.Windows.Forms.Application.DoEvents();
                    if (File.Exists(stlPath) && new FileInfo(stlPath).Length >= 100)
                        return true;
                }
                return false;
            }
            finally
            {
                if (savedFacetres != null)
                {
                    try { Application.SetSystemVariable("FACETRES", savedFacetres); }
                    catch { /* best-effort restore */ }
                }
            }
        }

        private static void TryDeleteFile(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch { /* best-effort temp file cleanup */ }
        }
    }
}
