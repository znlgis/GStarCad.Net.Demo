# VIEWEXPORT 命令设计文档

> 三维模型前/后/左/右正交视图导出为 2D DWG

## 概述

新增 `VIEWEXPORT` 命令：用户交互选择 3D 实体 → 用 COM Section API 生成四个正交方向的 2D 平面投影 → 网格排列输出到临时目录下的单个 DWG 文件。

## 约束

- 目标框架：.NET Framework 4.8
- NuGet 包：GStarCad.Net 20.22.0（含 GrxCAD.Interop.dll COM 层）
- 命名空间：GrxCAD.*（Runtime, ApplicationServices, DatabaseServices, EditorInput, Geometry）
- COM 命名空间：GrxCAD.Interop
- 输出目录：`{程序目录}\temp\`
- 文件名：`{当前文件名}_{时间戳}_views.dwg`

## 架构

### 单一文件

`src/GStarCad.Net.Demo/Commands/ViewsExportCommand.cs` — VIEWEXPORT 命令的完整实现。

### 执行流程

```
VIEWEXPORT
  → 1. Editor.GetSelection() 交互选择 3D 实体
  → 2. 计算合并包围盒（GeometricExtents）
  → 3. 获取 COM Document（doc.AcadDocument）
  → 4. 四个方向循环（前/后/左/右）：
       - 计算 Section 平面参数
       - comDoc.ModelSpace.AddSection(fromPt, toPt, planeVec)
       - 配置 section.Settings（2D投影 + 选中对象 + 新图块）
       - section.GenerateSectionGeometry()
       - 收集生成的 BlockReference ObjectId
  → 5. Database.SaveAs() 保存到 temp 目录
  → 6. WriteMessage 提示完成
```

### 四个方向定义

| 视图 | 平面法向量 | 说明 |
|------|-----------|------|
| Front | (0, 1, 0) | 从前方（+Y）看向原点 |
| Back | (0, -1, 0) | 从后方（-Y）看向原点 |
| Left | (1, 0, 0) | 从左方（+X）看向原点 |
| Right | (-1, 0, 0) | 从右方（-X）看向原点 |

## 关键技术决策

### COM Interop 访问

通过 `Document.AcadDocument` 属性获取 COM 对象，使用 `dynamic` 简化 COM 调用：

```csharp
dynamic comDoc = doc.AcadDocument;
dynamic ms = comDoc.ModelSpace;
```

### 备选方案：SendCommand

如果 Section API 在运行时不工作，降级使用 `SendCommand` 调用原生命令。

### 文件输出

使用 `Database.SaveAs()` 保存当前文档，或创建新 Database 用 WBlock 导出。

## 错误处理

- 无选择 → 提示"未选择任何实体"并退出
- 选择的实体非 3D → 过滤，仅处理 Solid3d
- COM 调用失败 → 捕获异常，提示错误信息
- 输出目录不存在 → 自动创建 temp 目录

## 验证

- 在 GStarCAD 2022 中打开含 3D 实体的 DWG
- 运行 VIEWEXPORT 命令
- 选择实体，验证 temp 目录生成 views.dwg
- 打开输出文件，确认四个方向投影存在
