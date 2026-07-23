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

            // Collect entity handles for COM export and compute bounding box
            var handles = new List<string>();
            Point3d? minPt = null;
            Point3d? maxPt = null;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                foreach (SelectedObject selObj in selRes.Value)
                {
                    var ent = (Entity)tr.GetObject(selObj.ObjectId, OpenMode.ForRead);
                    handles.Add(ent.Handle.ToString());

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

            Log.Debug(string.Format("Selection: {0} entities, handles: [{1}], bbox min: {2}, max: {3}",
                handles.Count,
                string.Join(", ", handles),
                minPt,
                maxPt));

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

            var tempStep3D = Path.Combine(tempDir, baseName + "_3d.step");
            var tempStep2D = Path.Combine(tempDir, baseName + "_2d.step");
            var outputPath = Path.Combine(tempDir, baseName + "_views.dwg");

            Log.Debug(string.Format("Temp dir: {0}, files: 3D={1}, 2D={2}, DWG={3}",
                tempDir, tempStep3D, tempStep2D, outputPath));

            // Step 1: Export selected 3D solids to STEP via isolated GStarCAD process
            var stepSw = Stopwatch.StartNew();
            ed.WriteMessage("\n[1/3] 导出3D STEP...");
            Log.Debug("Step 1: Exporting 3D STEP via isolated GStarCAD script...");
            try
            {
                if (!ExportToStep(db, tempStep3D, tempDir, ed))
                {
                    stepSw.Stop();
                    Log.Error(string.Format("Step 1 failed after {0}ms", stepSw.ElapsedMilliseconds));
                    ed.WriteMessage("\n3D STEP导出失败.");
                    return;
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

            // Step 3: Convert 2D STEP to DWG via COM
            stepSw.Restart();
            ed.WriteMessage("\n[3/3] 转换STEP至DWG...");
            Log.Debug("Step 3: Converting STEP to DWG...");
            try
            {
                if (!ConvertStepToDwg(tempStep2D, outputPath, ed))
                {
                    stepSw.Stop();
                    Log.Error(string.Format("Step 3 failed after {0}ms. 2D STEP at: {1}",
                        stepSw.ElapsedMilliseconds, tempStep2D));
                    ed.WriteMessage(string.Format(
                        "\nSTEP→DWG转换失败. 2D STEP文件位于: {0}", tempStep2D));
                    return;
                }
                stepSw.Stop();
                Log.Debug(string.Format("Step 3 complete in {0}ms", stepSw.ElapsedMilliseconds));
            }
            catch (System.Exception ex)
            {
                stepSw.Stop();
                Log.Error(string.Format("Step 3 failed after {0}ms", stepSw.ElapsedMilliseconds), ex);
                throw;
            }

            // Cleanup temp files
            TryDeleteFile(tempStep3D);
            TryDeleteFile(tempStep2D);

            sw.Stop();
            Log.Info(string.Format("ViewsExport completed successfully in {0}ms, output: {1}",
                sw.ElapsedMilliseconds, outputPath));
            ed.WriteMessage(string.Format(
                "\n视图导出完成. 输出文件: {0}", outputPath));
        }

        private bool ExportToStep(Database db, string stepPath, string tempDir, Editor ed)
        {
            Log.Debug(string.Format("ExportToStep (script): => {0}", stepPath));

            var gcadExe = Process.GetCurrentProcess().MainModule.FileName;
            Log.Debug(string.Format("GStarCAD exe: {0}", gcadExe));

            // Save current document to temp copy to avoid file lock conflicts
            var sourceDwg = db.Filename;
            var tempDwg = sourceDwg;
            if (string.IsNullOrEmpty(sourceDwg) || !File.Exists(sourceDwg))
            {
                tempDwg = Path.Combine(tempDir, "_export_temp.dwg");
                Log.Debug(string.Format("Saving document to temp copy: {0}", tempDwg));
                try { db.SaveAs(tempDwg, DwgVersion.Current); }
                catch (System.Exception ex)
                {
                    Log.Error("SaveAs temp DWG failed.", ex);
                    ed.WriteMessage("\n无法保存文档临时副本.");
                    return false;
                }
            }
            else
            {
                Log.Debug("Document already on disk, no save needed.");
            }

            var scriptPath = Path.Combine(tempDir, "_gcad_export.scr");
            var script = string.Format(CultureInfo.InvariantCulture,
                // FILEDIA 0: suppress file dialogs
                // _.OPEN: open the temp copy
                // _.ZOOM _E: ensure all entities visible
                // _.-EXPORT: command-line export (prompts: filename, format, selection)
                // STEP: format keyword
                // _X 3DSOLID: select all 3D solids via filter
                // _.QUIT Y: quit without saving
                "FILEDIA 0\n_.OPEN \"{0}\"\n_.ZOOM _E\n_.-EXPORT \"{1}\" STEP _X 3DSOLID \n_.QUIT Y\n",
                tempDwg, stepPath);

            Log.Debug(string.Format("Script: {0}", script.Replace("\n", "\\n")));
            File.WriteAllText(scriptPath, script, System.Text.Encoding.ASCII);

            var psi = new ProcessStartInfo
            {
                FileName = gcadExe,
                Arguments = string.Format("/b \"{0}\"", scriptPath),
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Minimized,
            };

            try
            {
                using (var proc = Process.Start(psi))
                {
                    if (proc == null)
                    {
                        Log.Error("Export script: Process.Start returned null.");
                        TryDeleteFile(scriptPath);
                        return false;
                    }

                    Log.Debug(string.Format("Export script PID={0}, waiting up to 90000ms...", proc.Id));
                    if (!proc.WaitForExit(90000))
                    {
                        Log.Error("Export script timed out.");
                        ed.WriteMessage("\n3D STEP导出超时.");
                        try { proc.Kill(); } catch { }
                        TryDeleteFile(scriptPath);
                        return false;
                    }

                    Log.Debug(string.Format("Export script exit code: {0}", proc.ExitCode));
                }
            }
            catch (System.Exception ex)
            {
                Log.Error("Export script: process launch failed.", ex);
                TryDeleteFile(scriptPath);
                return false;
            }

            TryDeleteFile(scriptPath);

            // Clean up temp DWG copy if we created one
            if (tempDwg != sourceDwg)
            {
                TryDeleteFile(tempDwg);
            }

            var result = File.Exists(stepPath);
            if (result)
            {
                Log.Debug(string.Format("ExportToStep success ({0} bytes).", new FileInfo(stepPath).Length));
            }
            else
            {
                Log.Error("Export script completed but no STEP output file found.");
            }
            return result;
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

        private bool ConvertStepToDwg(string stepPath, string dwgPath, Editor ed)
        {
            Log.Debug(string.Format("ConvertStepToDwg: STEP='{0}', DWG='{1}'", stepPath, dwgPath));

            try
            {
                // Launch an isolated GStarCAD process with a script file to avoid
                // COM re-entrancy and document switching during active command execution.
                var gcadExe = Process.GetCurrentProcess().MainModule.FileName;
                Log.Debug(string.Format("GStarCAD executable: {0}", gcadExe));

                var scriptDir = Path.GetDirectoryName(dwgPath);
                var scriptPath = Path.Combine(scriptDir, "_gcad_convert.scr");

                var script = string.Format(CultureInfo.InvariantCulture,
                    "FILEDIA 0\n_.OPEN \"{0}\"\n_.SAVEAS 2018 \"{1}\"\n_.QUIT Y\n",
                    stepPath, dwgPath);
                Log.Debug(string.Format("Writing script to {0}: {1}", scriptPath, script.Replace("\n", "\\n")));
                File.WriteAllText(scriptPath, script);

                var psi = new ProcessStartInfo
                {
                    FileName = gcadExe,
                    Arguments = string.Format("/b \"{0}\"", scriptPath),
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Minimized,
                };

                Log.Debug(string.Format("Starting GStarCAD: {0} /b \"{1}\"", gcadExe, scriptPath));

                using (var proc = Process.Start(psi))
                {
                    if (proc == null)
                    {
                        Log.Error("Process.Start returned null for GStarCAD conversion process.");
                        ed.WriteMessage("\n无法启动GStarCAD进程进行转换.");
                        TryDeleteFile(scriptPath);
                        return false;
                    }

                    Log.Debug(string.Format("GStarCAD conversion process started, PID={0}, waiting up to 60000ms...", proc.Id));

                    if (!proc.WaitForExit(60000))
                    {
                        Log.Error("GStarCAD conversion process timed out after 60000ms.");
                        ed.WriteMessage("\nGStarCAD转换进程超时.");
                        try { proc.Kill(); } catch { }
                        TryDeleteFile(scriptPath);
                        return false;
                    }

                    Log.Debug(string.Format("GStarCAD conversion process exited with code {0}", proc.ExitCode));
                }

                TryDeleteFile(scriptPath);

                var result = File.Exists(dwgPath);
                if (result)
                {
                    Log.Debug(string.Format("ConvertStepToDwg succeeded: {0}", dwgPath));
                }
                else
                {
                    Log.Error(string.Format("ConvertStepToDwg: GStarCAD exited but no DWG output at {0}", dwgPath));
                }
                return result;
            }
            catch (System.Exception ex)
            {
                Log.Error("ConvertStepToDwg failed with exception.", ex);
                ed.WriteMessage(string.Format("\nSTEP→DWG转换失败: {0}", ex.Message));
                return false;
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
