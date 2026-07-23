using GrxCAD.ApplicationServices;
using GrxCAD.DatabaseServices;
using GrxCAD.EditorInput;
using GrxCAD.Geometry;
using GrxCAD.Runtime;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading;

namespace GStarCad.Net.Demo.Commands
{
    public class ViewsExportCommand
    {
        private const int ToolTimeoutMs = 120000;

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

            if (minPt == null || maxPt == null)
            {
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

            dynamic comDoc = doc.AcadDocument;

            // Step 1: Export selected 3D solids to STEP
            ed.WriteMessage("\n[1/3] 导出3D STEP...");
            if (!ExportToStep(comDoc, handles, tempStep3D, ed))
            {
                ed.WriteMessage("\n3D STEP导出失败.");
                return;
            }
            ed.WriteMessage(" 完成.");

            // Step 2: Run OCCTTool for HLR projection
            ed.WriteMessage("\n[2/3] 运行HLC投影...");
            var toolExitCode = RunOCCTTool(tempStep3D, tempStep2D, ed);
            if (toolExitCode != 0)
            {
                ed.WriteMessage(string.Format("\nOCCTTool 返回错误码 {0}.", toolExitCode));
                return;
            }

            if (!File.Exists(tempStep2D))
            {
                ed.WriteMessage("\nOCCTTool 未生成输出文件.");
                return;
            }
            ed.WriteMessage(" 完成.");

            // Step 3: Convert 2D STEP to DWG via COM
            ed.WriteMessage("\n[3/3] 转换STEP至DWG...");
            if (!ConvertStepToDwg(tempStep2D, outputPath, ed))
            {
                ed.WriteMessage(string.Format(
                    "\nSTEP→DWG转换失败. 2D STEP文件位于: {0}", tempStep2D));
                return;
            }

            // Cleanup temp files
            TryDeleteFile(tempStep3D);
            TryDeleteFile(tempStep2D);

            ed.WriteMessage(string.Format(
                "\n视图导出完成. 输出文件: {0}", outputPath));
        }

        private bool ExportToStep(dynamic comDoc, List<string> handles, string stepPath, Editor ed)
        {
            const int maxRetries = 2;
            const int waitMs = 500;

            for (int attempt = 0; attempt < maxRetries; attempt++)
            {
                try
                {
                    // Build a COM SelectionSet with the entity handles
                    dynamic ss = null;
                    try { ss = comDoc.SelectionSets.Item("OCCT_SS"); ss.Delete(); } catch { }

                    ss = comDoc.SelectionSets.Add("OCCT_SS");
                    var items = new object[handles.Count];
                    for (int i = 0; i < handles.Count; i++)
                    {
                        items[i] = comDoc.HandleToObject(handles[i]);
                    }
                    ss.AddItems(items);

                    comDoc.Export(stepPath, "STEP", ss);

                    ss.Delete();

                    if (File.Exists(stepPath))
                        return true;
                }
                catch
                {
                    // Fallback: use command-line EXPORT with current selection
                    if (attempt == maxRetries - 1)
                    {
                        try
                        {
                            // Re-select entities via a window crossing (bounding box approach)
                            // Use SendCommand with EXPORT which reads current selection
                            var cmd = string.Format(CultureInfo.InvariantCulture,
                                "_-EXPORT {0} ", stepPath);
                            comDoc.SendCommand(cmd);
                            Thread.Sleep(waitMs * 2);
                            comDoc.SendCommand(" ");
                            Thread.Sleep(waitMs * 2);
                            comDoc.SendCommand(" ");
                            Thread.Sleep(waitMs);

                            if (File.Exists(stepPath))
                                return true;
                        }
                        catch (System.Exception ex)
                        {
                            ed.WriteMessage(string.Format("\nCOM Export失败: {0}", ex.Message));
                        }
                    }
                }
            }

            return File.Exists(stepPath);
        }

        private int RunOCCTTool(string inputStep, string outputStep, Editor ed)
        {
            var pluginDir = Path.GetDirectoryName(
                Assembly.GetExecutingAssembly().Location);

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
                if (File.Exists(exePath))
                {
                    toolDir = fullPath;
                    break;
                }
            }

            if (toolDir == null)
            {
                ed.WriteMessage("\n找不到 OCCTTool.exe. 已搜索:");
                foreach (var dir in candidateDirs)
                {
                    ed.WriteMessage(string.Format("\n  {0}", Path.GetFullPath(dir)));
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

            try
            {
                using (var proc = Process.Start(psi))
                {
                    if (proc == null)
                    {
                        ed.WriteMessage("\n无法启动 OCCTTool.exe.");
                        return -2;
                    }

                    var stdout = proc.StandardOutput.ReadToEnd();
                    var stderr = proc.StandardError.ReadToEnd();

                    if (!proc.WaitForExit(ToolTimeoutMs))
                    {
                        ed.WriteMessage("\nOCCTTool 超时.");
                        try { proc.Kill(); } catch { }
                        return -3;
                    }

                    if (proc.ExitCode != 0)
                    {
                        if (!string.IsNullOrEmpty(stderr))
                        {
                            ed.WriteMessage(string.Format("\nOCCTTool 错误: {0}", stderr.Trim()));
                        }
                    }

                    return proc.ExitCode;
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage(string.Format("\n启动 OCCTTool 异常: {0}", ex.Message));
                return -4;
            }
        }

        private bool ConvertStepToDwg(string stepPath, string dwgPath, Editor ed)
        {
            try
            {
                // Launch an isolated GStarCAD process with a script file to avoid
                // COM re-entrancy and document switching during active command execution.
                var gcadExe = Process.GetCurrentProcess().MainModule.FileName;
                var scriptDir = Path.GetDirectoryName(dwgPath);
                var scriptPath = Path.Combine(scriptDir, "_gcad_convert.scr");

                var script = string.Format(CultureInfo.InvariantCulture,
                    "FILEDIA 0\n_.OPEN \"{0}\"\n_.SAVEAS 2018 \"{1}\"\n_.QUIT Y\n",
                    stepPath, dwgPath);
                File.WriteAllText(scriptPath, script);

                var psi = new ProcessStartInfo
                {
                    FileName = gcadExe,
                    Arguments = string.Format("/b \"{0}\"", scriptPath),
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Minimized,
                };

                using (var proc = Process.Start(psi))
                {
                    if (proc == null)
                    {
                        ed.WriteMessage("\n无法启动GStarCAD进程进行转换.");
                        TryDeleteFile(scriptPath);
                        return false;
                    }

                    if (!proc.WaitForExit(60000))
                    {
                        ed.WriteMessage("\nGStarCAD转换进程超时.");
                        try { proc.Kill(); } catch { }
                        TryDeleteFile(scriptPath);
                        return false;
                    }
                }

                TryDeleteFile(scriptPath);

                return File.Exists(dwgPath);
            }
            catch (System.Exception ex)
            {
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
