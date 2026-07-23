# OCCT 2D 视图投影命令设计文档

> 基于 Open CASCADE Technology 实现 3D→2D 正交投影，输出 DWG

## 概述

GStarCAD 内置的 FLATSHOT / SECTIONPLANE 命令不稳定。改用 OCCT 的 `HlrBRep`（隐藏线消除）算法做几何投影运算，用 `STEP` 作为 GStarCAD 与 OCCT 之间的数据交换格式，最终输出 DWG。

## 架构

```
GStarCAD 插件 (.NET 4.8)
  ├── 用户选择 3D 实体
  ├── 导出为 STEP (BRepTools / STEPControl)
  ├── Process.Start 调用 OCCT 工具 (.exe)
  └── 工具返回的 DWG 即为最终输出

OCCT 投影工具 (独立 C++/CLI .exe)
  ├── 读取 STEP 文件
  ├── 四个方向 HLR 计算 (Front/Back/Left/Right)
  ├── 四宫格排列
  └── 输出 DWG
```

采用独立进程架构避免 C++/CLI 与 GStarCAD 托管进程的位数/运行时冲突。

## 文件

| 文件 | 说明 |
|------|------|
| `OCCT-samples-csharp/OCCTProxy/OCCTProxy.cpp` | 扩展投影 + DXF 导出方法 |
| `OCCT-samples-csharp/OCCTProxy/OCCTProxy.vcxproj` | 添加 TKHLR、TKDXF 链接库 |
| `src/GStarCad.Net.Demo/Commands/ViewsExportCommand.cs` | 重写为 OCCT 流程 |
| `tools/OCCTTool/` | 新建独立 OCCT 调用工具（C++/CLI .exe） |

## 投影方向

| 视图 | 投影方向 (HlrBRep) |
|------|-------------------|
| Front | (0, -1, 0) — Yneg |
| Back | (0, 1, 0) — Ypos |
| Left | (-1, 0, 0) — Xneg |
| Right | (1, 0, 0) — Xpos |

## 输出

- 目标格式：DWG
- 输出路径：`{程序目录}\temp\{原文件名}_views.dwg`
- 容纳四个视图的网格排列（2×2）

## 约束

- .NET Framework 4.8 主工程不变
- 不引入新 NuGet 依赖
- OCCT 部分需 C++/CLI 和 VC++ 工具链
- 输出路径自动创建
