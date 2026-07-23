# MESHVIEWEXPORT / HLREXPORT 双命令设计文档

> 3D→2D 视图导出：纯 .NET 网格投影 + OCCT HLR 精确投影

## 概述

在 GStarCAD 2022 自身 API（COM Section、FLATSHOT、SECTIONPLANE）均不可用的前提下，采用"提取几何数据 → 外部计算投影 → 写回 DWG"的策略实现两个互补命令：

- `MESHVIEWEXPORT`：零外部依赖，纯 .NET 解析 STL 网格 + 正交投影，单进程内完成
- `HLREXPORT`：STL → OCCT HLR 精确隐藏线消除 → 边坐标提取 → C# 侧写 DWG

两个命令互不冲突，与现有 `VIEWEXPORT` / `FLATEXPORT` 并存。

## 约束

- 目标框架：.NET Framework 4.8
- NuGet 包：GStarCad.Net 20.22.0、log4net 3.3.2
- 输出目录：`{程序目录}\temp\`
- 文件名：`{原文件名}_{时间戳}_mesh.dwg` / `{原文件名}_{时间戳}_hlr.dwg`
- 四个视图：Front / Back / Left / Right，2×2 网格排列

---

## 一、MESHVIEWEXPORT — 纯 .NET STL 网格投影

### 架构

```
GStarCAD 主进程（唯一进程，零外部依赖）
│
├── 1. Editor.GetSelection(filter: "3DSOLID")
│     └── 复用现有选择逻辑（ViewsExportCommand.cs:32-44）
│
├── 2. 计算合并包围盒
│     └── 复用现有包围盒逻辑（ViewsExportCommand.cs:46-91）
│
├── 3. STL 导出
│     ├── Application.SetSystemVariable("FILEDIA", 0)
│     ├── doc.SendStringToExecute("_.EXPORT\n{stlPath}\n_ALL\n\n_.FILEDIA 1 ")
│     └── DoEvents() 轮询，等文件出现且 size > 100 bytes
│     └── 已验证路径：ViewsExportCommand.cs:142-156 的 STL fallback 逻辑
│
├── 4. STL 解析器 — 新类 StlParser
│     ├── 自动检测 ASCII / Binary 格式
│     ├── ASCII：逐行解析 facet normal + outer loop + vertex ×3
│     ├── Binary：读取 80-byte header + uint32 三角形计数 + 50-byte × N
│     └── 输出：List<StlTriangle>（每个含 3 顶点 + 法向量）
│
├── 5. 投影引擎 — 新类 OrthoProjector
│     ├── 4 个方向正交投影矩阵：
│     │   Front(0,-1,0)  Back(0,1,0)  Left(-1,0,0)  Right(1,0,0)
│     ├── 步骤 A：背面剔除
│     │   └── 法向量 · 视线方向 < 0 → 跳过该三角形
│     ├── 步骤 B：顶点投影
│     │   └── 丢弃对应轴坐标，保留另两轴 → 2D 点
│     ├── 步骤 C：边收集 + 去重
│     │   └── 每条边归一化 (min,max) 排序后入 HashSet
│     ├── 步骤 D：轮廓边检测
│     │   └── 边被 1 个前向面 + 1 个后向面共享 → 轮廓边（始终可见）
│     ├── 步骤 E：内边深度遮挡检测
│     │   └── 两前向面共享边 → 检查是否被更近三角形覆盖
│     │   └── 算法：中点 + 3 采样点，检查是否落在更近三角形的 2D 投影内部
│     └── 输出：List<ProjectedEdge>（可见 + 隐藏，含 2D 坐标 + 样式标记）
│
├── 6. DWG 构建 — 新类 ViewArranger
│     ├── 计算包围盒确定网格间距
│     ├── 对每个视图，偏移 edge 坐标到对应网格位置
│     ├── 可见边 → Line 实体（Continuous 线型）
│     ├── 隐藏边 → Line 实体（Hidden 线型）
│     └── 写入新 Database 的 ModelSpace
│
└── 7. db.SaveAs(outputPath, DwgVersion.Current)
```

### 文件清单

| 文件 | 说明 | 行数估算 |
|------|------|---------|
| `Commands/MeshViewExportCommand.cs` | 命令入口 + 主流程 | ~150 |
| `Common/StlParser.cs` | STL 解析器 | ~200 |
| `Common/OrthoProjector.cs` | 正交投影引擎 | ~350 |
| `Common/ViewArranger.cs` | 2D 视图网格排列 + DWG 构建 | ~150 |

### 关键数据结构

```csharp
struct StlTriangle
{
    Vector3d Normal;
    Point3d V1, V2, V3;
}

struct ProjectedEdge
{
    Point2d Start, End;
    bool IsVisible;  // true=实线, false=虚线
}

struct ViewProjection
{
    string Name;
    List<ProjectedEdge> Edges;
    Rect2d Bounds;
}
```

### 超时与错误处理

- STL 导出轮询：最大 30 秒，200ms 间隔
- STL 解析错误：文件为空或无效 → 提示并退出
- 0 个三角形：提示"模型无可投影面"并退出
- 所有异常向上冒泡，由 CommandMethod 框架捕获

---

## 二、HLREXPORT — STL + OCCT HLR 精确投影

### 架构

```
GStarCAD 主进程                          OCCTTool.exe
───────────                             ──────────────────────
1. Editor.GetSelection
2. 包围盒计算
3. SendStringToExecute + DoEvents
   → 导出 .stl
                                        4. StlAPI_Reader → TopoDS_Shape
4. Process.Start("OCCTTool.exe")        5. HLRBRep_Algo × 4 方向
   → 等待退出                           6. 提取 VCompound/HCompound 边端点
                                        7. 写入 edges.csv（每行：可见/隐藏, x1,y1,z1, x2,y2,z2）
                                        ──────────────────────
8. 读取 edges.csv
9. 创建 Line 实体 → 新 Database → 2×2 网格排列
10. db.SaveAs(dwgPath)
```

**核心改进**：OCCT 不再输出 STEP（需要用户手动 SAVEAS），而是输出一个简单的边坐标 CSV 文件。C# 侧读取 CSV 并直接创建 DWG。这消除了所有输出格式依赖。

### OCCTProxy 修改

文件：`D:\self\code\OCCT-samples-csharp\OCCTProxy\OCCTProxy.cpp`

#### 新增方法：`Generate2DViewsSTL`

```cpp
// 新增 include
#include <StlAPI_Reader.hxx>
#include <TopExp_Explorer.hxx>
#include <BRep_Tool.hxx>
#include <TopoDS_Edge.hxx>

// 新增 pragma (StlAPI_Reader 已在 TKDESTL 中，无需新增 lib)

bool Generate2DViewsSTL(System::String^ theInputStl, System::String^ theOutputCsv)
{
    // 1. 读取 STL 网格
    TopoDS_Shape aShape;
    StlAPI_Reader aStlReader;
    aStlReader.Read(aShape, toAsciiString(theInputStl).ToCString());
    if (aShape.IsNull()) return false;

    // 2. 4 方向 HLR 投影（复用现有逻辑）
    // ...

    // 3. 遍历 VCompound+HCompound 提取边端点
    //    写入 CSV: visible|hidden, x1,y1,z1, x2,y2,z2
    // ...
}
```

#### 修改 `Generate2DViews`（保留向后兼容）

现有方法保留不动。新增 `Generate2DViewsSTL` 方法与其并列。

### OCCTTool 修改

文件：`tools/OCCTTool/Program.cs`

- 参数形式改为：`OCCTTool.exe <input.stl> <output.csv>`
- 调用 `proxy.Generate2DViewsSTL(inputFile, outputFile)`

### 文件清单

| 文件 | 说明 | 行数估算 |
|------|------|---------|
| `Commands/HlrExportCommand.cs` | 命令入口 + 主流程 | ~180 |
| `OCCTProxy/OCCTProxy.cpp` | 新增 Generate2DViewsSTL | ~80（新增） |
| `tools/OCCTTool/Program.cs` | 修改参数 → 调用新方法 | ~10（修改） |

### HLR 边坐标提取细节

对每个视图方向执行 HLR 后，从 `VCompound` 和 `HCompound` 中提取边：

```cpp
void WriteEdgesToCsv(const TopoDS_Shape& theShape, bool isVisible,
                     std::ofstream& theFile)
{
    for (TopExp_Explorer exp(theShape, TopAbs_EDGE); exp.More(); exp.Next())
    {
        TopoDS_Edge edge = TopoDS::Edge(exp.Current());
        double first, last;
        Handle(Geom_Curve) curve = BRep_Tool::Curve(edge, first, last);
        if (curve.IsNull()) continue;

        gp_Pnt p1 = curve->Value(first);
        gp_Pnt p2 = curve->Value(last);
        theFile << (isVisible ? "V" : "H") << ","
                << p1.X() << "," << p1.Y() << "," << p1.Z() << ","
                << p2.X() << "," << p2.Y() << "," << p2.Z() << "\n";
    }
}
```

### CSV 格式

```
V,0.000,1.000,0.000,10.000,1.000,0.000
V,10.000,1.000,0.000,10.000,5.000,0.000
H,5.000,3.000,0.000,8.000,3.000,0.000
```

- 第一列：`V` = 可见边（实线），`H` = 隐藏边（虚线）
- 随后 6 列：起点 (x,y,z)，终点 (x,y,z)
- 坐标已投影到视图平面（对应轴坐标接近 0）

---

## 三、两个命令对比

| 维度 | MESHVIEWEXPORT | HLREXPORT |
|------|----------------|-----------|
| 命令名 | MESHVIEWEXPORT | HLREXPORT |
| 外部依赖 | 零 | OCCT 7.x DLL (TKernel/TKMath/TKBRep/TKHLR/TKDESTL) |
| 进程数 | 1 | 2 (GStarCAD + OCCTTool) |
| 投影算法 | 背面剔除 + 轮廓检测 + 深度遮挡近似 | OCCT HLRBRep_Algo 数学精确 |
| 隐藏线精度 | 轮廓边精确，内边近似 | 完全精确 |
| 输出格式 | DWG（直接可用） | DWG（直接可用） |
| 分发 | 单一 .dll | 需附带 OCCT DLL |
| 新代码量 | ~850 行 C# | ~80 行 C++/CLI + ~200 行 C# |

---

## 四、视图方向定义

两个命令共用同一套方向定义：

| 视图 | 视线方向 | 丢弃轴 | 保留轴 (水平, 垂直) |
|------|---------|--------|-------------------|
| Front | (0, -1, 0) | Y | X, Z |
| Back | (0, 1, 0) | Y | X, Z |
| Left | (-1, 0, 0) | X | Y, Z |
| Right | (1, 0, 0) | X | Y, Z |

---

## 五、验证计划

### MESHVIEWEXPORT
1. 在 GStarCAD 2022 中创建简单 3D 实体（Box + Sphere）
2. 运行 MESHVIEWEXPORT，选择实体
3. 验证 temp 目录生成 `*_mesh.dwg`
4. 打开 DWG，确认 4 个正交视图存在，轮廓清晰

### HLREXPORT
1. 确认 OCCT 环境可用（环境变量、DLL 路径）
2. 编译 OCCTProxy + OCCTTool
3. 手动测试：`OCCTTool.exe test.stl test.csv` 验证 CSV 输出
4. 在 GStarCAD 中运行 HLREXPORT，端到端验证

---

*创建于 2026-07-23*
