# Task 7 Report: HlrExportCommand — HLREXPORT 命令

## Status: DONE

## Summary
创建了 CommandMethod 为 "HLREXPORT" 的实施类 `HlrExportCommand.cs`，实现四步流水线：(1) STL 导出、(2) OCCTTool.exe 外部进程 HLR 投影、(3) CSV 边解析、(4) DWG 输出（VISIBLE_EDGES / HIDDEN_EDGES 图层）。遵循与 `MeshViewExportCommand.cs` 相同的模式，并使用 `using Exception = System.Exception;` 避免与 `GrxCAD.Runtime.Exception` 发生歧义。

## Commit Summary
`feat: add HLREXPORT command for OCCT-based hidden line removal projection`

- 创建 `src/GStarCad.Net.Demo/Commands/HlrExportCommand.cs`
- 注册 `[CommandMethod("HLREXPORT")]`，选择集过滤 3DSOLID 实体
- 步骤 1：通过 `SendStringToExecute("_.EXPORT")` 导出 STL
- 步骤 2：启动 `OCCTTool.exe`（发现位于 `tools/OCCTTool/bin/Release/net48/` 或 Debug），超时 120s
- 步骤 3：解析 CSV（格式：V|H, x1,y1,z1, x2,y2,z2）
- 步骤 4：创建 `VISIBLE_EDGES`（Continuous 线型）和 `HIDDEN_EDGES`（Hidden 线型）图层，写入 Line 实体，保存 DWG

## Build Verification
```
> dotnet build src/GStarCad.Net.Demo/GStarCad.Net.Demo.csproj
正在确定要还原的项目…
所有项目均是最新的，无法还原。
  GStarCad.Net.Demo -> D:\self\code\GStarCad.Net.Demo\src\GStarCad.Net.Demo\bin\Debug\net48\GStarCad.Net.Demo.dll

已成功生成。
    0 个警告
    0 个错误
已用时间 00:00:00.77
```

## Concerns
无。任务 7（共 7 个）——全部 7 个任务均已完成。
