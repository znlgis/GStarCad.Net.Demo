using GrxCAD.ApplicationServices;
using GrxCAD.DatabaseServices;
using GrxCAD.EditorInput;
using GrxCAD.Geometry;
using GrxCAD.Runtime;
using log4net;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;

namespace GStarCad.Net.Demo.Commands
{
    public class ViewsExportCommand
    {
        private const int ToolTimeoutMs = 120000;
        private static readonly ILog Log = LogManager.GetLogger(typeof(ViewsExportCommand));

        [CommandMethod("VIEWEXPORT")]
        public void ViewsExport()
        {
            Log.Debug("ViewsExport() entered");
            var sw = Stopwatch.StartNew();

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
                Log.Info("No 3D solids selected. Exiting.");
                ed.WriteMessage("\n未选择任何3D实体.");
                return;
            }

            // Build ObjectId array from selection
            var objIds = new ObjectId[selRes.Value.Count];
            var idx = 0;
            foreach (SelectedObject selObj in selRes.Value)
                objIds[idx++] = selObj.ObjectId;
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

            Log.Debug(string.Format("Selection: {0} entities, bbox min: {1}, max: {2}",
                objIds.Length, minPt, maxPt));

            if (minPt == null || maxPt == null)
            {
                Log.Error("Bounding box computation failed — minPt or maxPt is null.");
                ed.WriteMessage("\n无法计算实体包围盒.");
                return;
            }

            // Prepare temp directory and file paths
            var assemblyDir = Path.GetDirectoryName(
                Assembly.GetExecutingAssembly().Location);
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
            var baseName = string.Format("{0}_{1}", originalName, timestamp);

            var tempStep3D = Path.Combine(tempDir, baseName + "_3d.stp");
            var stem = Path.Combine(tempDir, baseName + "_3d");
            var satPath = stem + ".sat";
            var stlPath = stem + ".stl";
            var tempStep2D = Path.Combine(tempDir, baseName + "_2d.stp");
            var outputPath = Path.Combine(tempDir, baseName + "_views.dwg");

            Log.Debug(string.Format("Temp dir: {0}, files: 3D={1}, 2D={2}, DWG={3}",
                tempDir, tempStep3D, tempStep2D, outputPath));

            // Step 1: Export 3D solids as SAT via in-process ACISOUT, then convert to STEP
            var stepSw = Stopwatch.StartNew();
            ed.WriteMessage("\n[1/3] 导出3D STEP...");
            Log.Debug("Step 1: ACISOUT in-process export...");
            try
            {
                ed.Command("_.FILEDIA", 0);
                ed.SetImpliedSelection(objIds);
                ed.Command("_.ACISOUT", satPath, "");

                if (!File.Exists(satPath) || new FileInfo(satPath).Length < 100)
                {
                    Log.Warn("ACISOUT failed, trying EXPORT STL...");
                    ed.Command("_.EXPORT", stlPath);
                    if (File.Exists(stlPath) && new FileInfo(stlPath).Length > 100)
                    {
                        File.Copy(stlPath, tempStep3D, true);
                        TryDeleteFile(stlPath);
                        TryDeleteFile(satPath);
                        Log.Debug("STL export succeeded as fallback.");
                    }
                    else
                    {
                        stepSw.Stop();
                        Log.Error("All 3D export methods failed.");
                        ed.WriteMessage("\n3D导出失败.");
                        return;
                    }
                }
                else
                {
                    // ACISOUT succeeded: SAT → STEP conversion
                    ed.Command("_.FILEDIA", 0);
                    ed.Command("_.OPEN", satPath);
                    ed.Command("_.SAVEAS", 2018, tempStep3D);
                    ed.Command("_.CLOSE");
                    TryDeleteFile(satPath);
                    Log.Debug("SAT→STEP conversion complete.");
                }
                stepSw.Stop();
                Log.Debug(string.Format("Step 1 complete in {0}ms", stepSw.ElapsedMilliseconds));
            }
            catch (System.Exception ex)
            {
                stepSw.Stop();
                Log.Error(string.Format("Step 1 failed after {0}ms", stepSw.ElapsedMilliseconds), ex);
                throw;
            }
            ed.WriteMessage(" 完成.");

            // Step 2: Run OCCTTool for HLR projection
            stepSw.Restart();
            ed.WriteMessage("\n[2/3] 运行HLC投影...");
            Log.Debug("Step 2: Running OCCTTool...");
            try
            {
                var toolExitCode = RunOCCTTool(tempStep3D, tempStep2D, ed);
                if (toolExitCode != 0)
                {
                    stepSw.Stop();
                    Log.Error(string.Format("Step 2 failed: OCCTTool exit code {0} after {1}ms",
                        toolExitCode, stepSw.ElapsedMilliseconds));
                    ed.WriteMessage(string.Format("\nOCCTTool 返回错误码 {0}.", toolExitCode));
                    return;
                }

                if (!File.Exists(tempStep2D))
                {
                    stepSw.Stop();
                    Log.Error(string.Format("Step 2 failed: OCCTTool produced no output after {0}ms",
                        stepSw.ElapsedMilliseconds));
                    ed.WriteMessage("\nOCCTTool 未生成输出文件.");
                    return;
                }
                stepSw.Stop();
                Log.Debug(string.Format("Step 2 complete in {0}ms", stepSw.ElapsedMilliseconds));
            }
            catch (System.Exception ex)
            {
                stepSw.Stop();
                Log.Error(string.Format("Step 2 failed after {0}ms", stepSw.ElapsedMilliseconds), ex);
                throw;
            }
            ed.WriteMessage(" 完成.");

            // Step 3: Output 2D IGES (skip DWG conversion to avoid second GStarCAD instance)
            Log.Debug("Pipeline complete, output: " + tempStep2D);

            // Cleanup temp 3D file
            TryDeleteFile(tempStep3D);

            sw.Stop();
            Log.Info(string.Format(CultureInfo.InvariantCulture,
                "ViewsExport completed in {0}ms. 2D: {1}", sw.ElapsedMilliseconds, tempStep2D));
            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n\n=== 导出完成 ({0}ms) ===", sw.ElapsedMilliseconds));
            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n输出文件: {0}", tempStep2D));
            ed.WriteMessage("\n用 GStarCAD 打开此文件可查看 4 个正交视图的 2D 投影.");
            ed.WriteMessage("\n需要 DWG 格式: 打开文件后执行 SAVEAS 保存.");
        }

        private int RunOCCTTool(string inputStep, string outputStep, Editor ed)
        {
            Log.Debug(string.Format("RunOCCTTool: input='{0}', output='{1}'", inputStep, outputStep));

            var pluginDir = Path.GetDirectoryName(
                Assembly.GetExecutingAssembly().Location);
            Log.Debug(string.Format("Plugin directory: {0}", pluginDir));

            // Walk up from bin/Debug/net48 or bin/Release/net48 to repo root
            var candidateDirs = new[]
            {
                Path.Combine(pluginDir, @"..\..\..\..\tools\OCCTTool\bin\Release\net48"),
                Path.Combine(pluginDir, @"..\..\..\..\tools\OCCTTool\bin\Debug\net48"),
            };

            string toolDir = null;
            foreach (var dir in candidateDirs)
            {
                var fullPath = Path.GetFullPath(dir);
                var exePath = Path.Combine(fullPath, "OCCTTool.exe");
                Log.Debug(string.Format("Searching for OCCTTool at: {0}", fullPath));
                if (File.Exists(exePath))
                {
                    toolDir = fullPath;
                    Log.Debug(string.Format("Found OCCTTool at: {0}", exePath));
                    break;
                }
            }

            if (toolDir == null)
            {
                Log.Error("OCCTTool.exe not found in any candidate directory.");
                ed.WriteMessage("\n找不到 OCCTTool.exe. 已搜索:");
                foreach (var dir in candidateDirs)
                {
                    var fullPath = Path.GetFullPath(dir);
                    Log.Error(string.Format("  Searched: {0}", fullPath));
                    ed.WriteMessage(string.Format("\n  {0}", fullPath));
                }
                return -1;
            }

            var exe = Path.Combine(toolDir, "OCCTTool.exe");
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = string.Format("\"{0}\" \"{1}\"", inputStep, outputStep),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = toolDir,
            };

            Log.Debug(string.Format("Starting OCCTTool: {0} {1}", exe, psi.Arguments));

            try
            {
                using (var proc = Process.Start(psi))
                {
                    if (proc == null)
                    {
                        Log.Error("Process.Start returned null for OCCTTool.");
                        ed.WriteMessage("\n无法启动 OCCTTool.exe.");
                        return -2;
                    }

                    Log.Debug(string.Format("OCCTTool process started, PID={0}", proc.Id));

                    var stdout = proc.StandardOutput.ReadToEnd();
                    var stderr = proc.StandardError.ReadToEnd();

                    if (!proc.WaitForExit(ToolTimeoutMs))
                    {
                        Log.Error(string.Format("OCCTTool timed out after {0}ms.", ToolTimeoutMs));
                        ed.WriteMessage("\nOCCTTool 超时.");
                        try { proc.Kill(); } catch { }
                        return -3;
                    }

                    Log.Debug(string.Format("OCCTTool exited with code {0}", proc.ExitCode));

                    if (!string.IsNullOrEmpty(stdout))
                    {
                        Log.Debug(string.Format("OCCTTool stdout: {0}", stdout.Trim()));
                    }
                    if (proc.ExitCode != 0)
                    {
                        if (!string.IsNullOrEmpty(stderr))
                        {
                            Log.Error(string.Format("OCCTTool stderr: {0}", stderr.Trim()));
                            ed.WriteMessage(string.Format("\nOCCTTool 错误: {0}", stderr.Trim()));
                        }
                    }

                    return proc.ExitCode;
                }
            }
            catch (System.Exception ex)
            {
                Log.Error("Exception starting OCCTTool process.", ex);
                ed.WriteMessage(string.Format("\n启动 OCCTTool 异常: {0}", ex.Message));
                return -4;
            }
        }

        private void TryDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // temp file cleanup is best-effort
            }
        }
    }
}
