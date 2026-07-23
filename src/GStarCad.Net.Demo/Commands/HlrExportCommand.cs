using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using GrxCAD.ApplicationServices;
using GrxCAD.DatabaseServices;
using GrxCAD.EditorInput;
using GrxCAD.Geometry;
using GrxCAD.Runtime;
using log4net;
using Exception = System.Exception;

namespace GStarCad.Net.Demo.Commands
{
    public class HlrExportCommand
    {
        private const int ToolTimeoutMs = 120000;
        private static readonly ILog Log = LogManager.GetLogger(typeof(HlrExportCommand));

        [CommandMethod("HLREXPORT")]
        public void HlrExport()
        {
            Log.Debug("HlrExport() entered");
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
            var stlPath = Path.Combine(tempDir, string.Format("{0}_{1}_hlr.stl", originalName, timestamp));
            var csvPath = Path.Combine(tempDir, string.Format("{0}_{1}_hlr.csv", originalName, timestamp));
            var dwgPath = Path.Combine(tempDir, string.Format("{0}_{1}_hlr.dwg", originalName, timestamp));

            // Step 1: Export STL
            ed.WriteMessage("\n[1/4] 导出STL网格...");
            if (!ExportStl(doc, stlPath))
            {
                ed.WriteMessage("\nSTL导出失败: 文件未生成.");
                return;
            }
            ed.WriteMessage(" 完成.");

            // Step 2: Run OCCTTool
            ed.WriteMessage("\n[2/4] OCCT HLR投影...");

            var toolExitCode = RunOCCTTool(stlPath, csvPath, ed);
            if (toolExitCode != 0)
            {
                ed.WriteMessage(string.Format("\nOCCTTool 返回错误码 {0}.", toolExitCode));
                TryDeleteFile(stlPath);
                return;
            }

            if (!File.Exists(csvPath) || new FileInfo(csvPath).Length < 10)
            {
                ed.WriteMessage("\nOCCTTool 未生成有效的CSV输出.");
                TryDeleteFile(stlPath);
                return;
            }

            ed.WriteMessage(" 完成.");

            // Step 3: Parse CSV edges
            ed.WriteMessage("\n[3/4] 解析投影边...");

            var edges = ParseEdgesCsv(csvPath);
            if (edges.Count == 0)
            {
                ed.WriteMessage("\nCSV中无有效边.");
                Cleanup(stlPath, csvPath);
                return;
            }

            Log.Debug(string.Format("Parsed {0} edges from CSV", edges.Count));
            ed.WriteMessage(" 完成.");

            // Step 4: Build DWG
            ed.WriteMessage("\n[4/4] 生成DWG...");

            var visibleCount = BuildDwgFromEdges(edges, dwgPath);

            Cleanup(stlPath, csvPath);

            sw.Stop();
            ed.WriteMessage(" 完成.");
            ed.WriteMessage(string.Format("\n\n=== HLREXPORT 完成 ({0}ms) ===", sw.ElapsedMilliseconds));
            ed.WriteMessage(string.Format("\n输出文件: {0}", dwgPath));
            ed.WriteMessage(string.Format("\n可见边: {0}, 隐藏边: {1}", visibleCount, edges.Count - visibleCount));
        }

        private struct CsvEdge
        {
            public bool IsVisible;
            public Point3d Start, End;
        }

        private static List<CsvEdge> ParseEdgesCsv(string csvPath)
        {
            var edges = new List<CsvEdge>();

            foreach (var line in File.ReadLines(csvPath))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                var parts = line.Split(',');
                if (parts.Length < 7) continue;

                var isVisible = parts[0].Trim() == "V";

                var start = new Point3d(
                    double.Parse(parts[1], CultureInfo.InvariantCulture),
                    double.Parse(parts[2], CultureInfo.InvariantCulture),
                    double.Parse(parts[3], CultureInfo.InvariantCulture));

                var end = new Point3d(
                    double.Parse(parts[4], CultureInfo.InvariantCulture),
                    double.Parse(parts[5], CultureInfo.InvariantCulture),
                    double.Parse(parts[6], CultureInfo.InvariantCulture));

                edges.Add(new CsvEdge { IsVisible = isVisible, Start = start, End = end });
            }

            return edges;
        }

        /// <summary>Build DWG from edges. Returns count of visible edges.</summary>
        private static int BuildDwgFromEdges(List<CsvEdge> edges, string outputPath)
        {
            var visibleCount = 0;

            using (var db = new Database(true, true))
            {
                ObjectId visLayerId, hidLayerId;

                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForWrite);
                    visLayerId = GetOrCreateLayer(lt, tr, db, "VISIBLE_EDGES", "Continuous");
                    hidLayerId = GetOrCreateLayer(lt, tr, db, "HIDDEN_EDGES", "Hidden");
                    tr.Commit();
                }

                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    var btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                    foreach (var edge in edges)
                    {
                        var layerId = edge.IsVisible ? visLayerId : hidLayerId;

                        var line = new Line(edge.Start, edge.End);
                        line.LayerId = layerId;
                        btr.AppendEntity(line);
                        tr.AddNewlyCreatedDBObject(line, true);

                        if (edge.IsVisible) visibleCount++;
                    }

                    tr.Commit();
                }

                db.SaveAs(outputPath, DwgVersion.Current);
            }

            return visibleCount;
        }

        private static ObjectId GetOrCreateLayer(LayerTable lt, Transaction tr, Database db,
            string name, string linetypeName)
        {
            if (!lt.Has(name))
            {
                var ltr = new LayerTableRecord();
                ltr.Name = name;

                var ltt = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead);
                if (ltt.Has(linetypeName))
                    ltr.LinetypeObjectId = ltt[linetypeName];

                lt.Add(ltr);
                tr.AddNewlyCreatedDBObject(ltr, true);
            }

            return lt[name];
        }

        private static bool ExportStl(Document doc, string stlPath)
        {
            try
            {
                var ed = doc.Editor;
                ed.Command("_.FILEDIA", 0);
                ed.Command("_.EXPORT", stlPath.Replace('\\', '/'));
                ed.Command("_.FILEDIA", 1);

                return File.Exists(stlPath) && new FileInfo(stlPath).Length >= 100;
            }
            catch (Exception)
            {
                // EXPORT may fail on invalid extension; continue with empty result
                return false;
            }
        }

        private int RunOCCTTool(string stlPath, string csvPath, Editor ed)
        {
            var pluginDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

            var candidateDirs = new[]
            {
                Path.Combine(pluginDir, @"..\..\..\..\tools\OCCTTool\bin\Release\net48"),
                Path.Combine(pluginDir, @"..\..\..\..\tools\OCCTTool\bin\Debug\net48")
            };

            string toolDir = null;
            foreach (var dir in candidateDirs)
            {
                var fullPath = Path.GetFullPath(dir);
                var exePath = Path.Combine(fullPath, "OCCTTool.exe");
                if (File.Exists(exePath))
                {
                    toolDir = fullPath;
                    break;
                }
            }

            if (toolDir == null)
            {
                ed.WriteMessage("\n找不到 OCCTTool.exe.");
                return -1;
            }

            var exe = Path.Combine(toolDir, "OCCTTool.exe");
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = string.Format("\"{0}\" \"{1}\"", stlPath, csvPath),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = toolDir
            };

            try
            {
                using (var proc = Process.Start(psi))
                {
                    if (proc == null) return -2;

                    var stdout = proc.StandardOutput.ReadToEnd();
                    var stderr = proc.StandardError.ReadToEnd();

                    if (!proc.WaitForExit(ToolTimeoutMs))
                    {
                        try { proc.Kill(); } catch { /* best-effort process termination */ }
                        return -3;
                    }

                    if (!string.IsNullOrEmpty(stdout))
                        Log.Debug(string.Format("OCCTTool stdout: {0}", stdout.Trim()));
                    if (proc.ExitCode != 0 && !string.IsNullOrEmpty(stderr))
                    {
                        Log.Error(string.Format("OCCTTool stderr: {0}", stderr.Trim()));
                        ed.WriteMessage(string.Format("\nOCCTTool 错误: {0}", stderr.Trim()));
                    }

                    return proc.ExitCode;
                }
            }
            catch (Exception ex)
            {
                Log.Error("Exception starting OCCTTool process.", ex);
                ed.WriteMessage(string.Format("\n启动 OCCTTool 异常: {0}", ex.Message));
                return -4;
            }
        }

        private static void Cleanup(string stlPath, string csvPath)
        {
            TryDeleteFile(stlPath);
            TryDeleteFile(csvPath);
        }

        private static void TryDeleteFile(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch { /* best-effort temp file cleanup */ }
        }
    }
}
