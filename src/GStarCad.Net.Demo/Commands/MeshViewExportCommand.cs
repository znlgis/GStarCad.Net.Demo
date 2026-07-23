using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
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
            ExportStl(doc, stlPath);

            if (!WaitForFile(stlPath, 100, 30000))
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
            foreach (var p in projections)
                totalEdges += p.Edges.Count;

            ed.WriteMessage(" 完成.");

            // Step 3: Arrange + Save DWG
            ed.WriteMessage("\n[3/3] 生成DWG...");

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
            ed.WriteMessage(string.Format("\n三角面数: {0}, 总边数: {1}", triangles.Count, totalEdges));
        }

        private static void ExportStl(Document doc, string stlPath)
        {
            try
            {
                Application.SetSystemVariable("FILEDIA", 0);
            }
            catch (Exception ex)
            {
                Log.Warn("Failed to set FILEDIA", ex);
            }

            doc.SendStringToExecute(
                string.Format("_.EXPORT\n{0}\n_ALL\n\n_.FILEDIA 1 ",
                    stlPath.Replace('\\', '/')),
                false, false, false);
        }

        private static bool WaitForFile(string path, long minSizeBytes, int timeoutMs)
        {
            var deadline = DateTime.Now.AddMilliseconds(timeoutMs);
            while (DateTime.Now < deadline)
            {
                System.Windows.Forms.Application.DoEvents();
                if (File.Exists(path) && new FileInfo(path).Length >= minSizeBytes)
                    return true;
                Thread.Sleep(200);
            }
            return false;
        }

        private static void TryDeleteFile(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch { }
        }
    }
}
