# 3D 到 2D 视图导出 — 技术调研与迭代记录

> 记录 VIEWEXPORT / FLATEXPORT / HLREXPORT / MESHVIEWEXPORT 各命令的完整开发历程，
> 包括所有尝试、失败原因、最终方案和 GStarCAD API 限制发现。

## 目标

在浩辰 CAD 2022 中实现：选择 3D 实体 → 生成正交方向的 2D 投影（最终确定为国标第一角三视图：主/俯/左）→ 直接输出为 DWG 文件。

---

## 一、FLATEXPORT 命令尝试

| 迭代 | 方案 | 结果 | 失败原因 |
|------|------|------|----------|
| 1 | `ViewTableRecord.SetCurrentView()` 设正交视图 + `FLATSHOT` | 四个视图相同，3D 输出 | `SetCurrentView()` 在 GStarCAD 中不生效 |
| 2 | COM `VPOINT` 异步设视图 + `FLATSHOT` | 四个视图相同，3D 输出 | COM `SendCommand` 异步，VPOINT 未完成就执行 FLATSHOT |
| 3 | `Editor.Command("VPOINT", vp)` 同步 + `Editor.Command("FLATSHOT", ...)` | 待测试 | FLATSHOT 命令在 GStarCAD 中存在但可能始终弹出对话框 |

### FLATEXPORT 结论

FLATSHOT 命令的对话框无法通过 `CMDDIA` / `FILEDIA` 系统变量抑制。`Editor.Command("FLATSHOT", ins, 1, 1, 0)` 是否能在 GStarCAD 中无对话框执行尚未验证。该命令当前保留在 `FlatshotCommand.cs` 中，状态为"待测试"。

---

## 二、VIEWEXPORT 命令尝试

### 阶段 A：COM Section API（迭代 1-3）

| 迭代 | 方案 | 结果 | 失败原因 |
|------|------|------|----------|
| 1 | `dynamic` 调用 `section.GenerateSectionGeometry()` | 参数不匹配 | 方法需 6 参数（1 个 `GcadEntity` + 5 个 `ref object`），无无参重载 |
| 2 | `Type.InvokeMember` + `ParameterModifier` 反射调用 | `TargetInvocationException` | 内部异常"未实现该方法或操作" |
| 3 | 配置 `settings.CurrentSectionType = 2` (gcSectionType2dSection) + `GenerationOptions` 后调用 | E_NOTIMPL | **GStarCAD 2022 COM 层声明了 `GenerateSectionGeometry` 但未实现** |

### 阶段 B：原生命令组合（迭代 4-6）

| 迭代 | 方案 | 结果 | 失败原因 |
|------|------|------|----------|
| 4 | `SECTIONPLANE` (3 点) + `SECTIONPLANETOBLOCK _L` | 剖面平面创建成功，但"未生成任何几何体" | 平面未穿过实体或命令限制 |
| 5 | `SECTIONPLANE` + `(handent "handle")` + `SECTIONPLANETOBLOCK` | 选择成功，仍"未生成任何几何体" | 同上 |
| 6 | 扩大 3 倍范围的 3 点 SECTIONPLANE | 同上 | 同上 |

### 阶段 C：COM 互操作尝试（迭代 7-8）

| 迭代 | 方案 | 结果 | 失败原因 |
|------|------|------|----------|
| 7 | COM `HandleToObject` 获取实体引用 | `AccessViolationException` 崩溃 | GStarCAD COM IDispatch 与标准 AutoCAD COM 不兼容 |
| 8 | COM `SelectionSet.Select()` (全部模式：Crossing/All/Previous) | Count=0 或异常 | 同上，GStarCAD COM 层选择集 API 不可用 |

### 阶段 D：GStarCAD 脚本进程导出（迭代 9-11）

| 迭代 | 方案 | 结果 | 失败原因 |
|------|------|------|----------|
| 9 | 独立 GStarCAD 脚本进程：`OPEN → EXPORT .stp → QUIT` | EXPORT 不支持 STEP 格式 | GStarCAD EXPORT 命令无 STEP 选项 |
| 10 | `_.IGESOUT` | 命令不存在 | GStarCAD 无 IGESOUT 命令 |
| 11 | `_.ACISOUT` via LISP `(command "_.ACISOUT" ...)` | LISP 语法失败 | 路径反斜杠转义问题 + DDE/SDI 冲突 |

### 阶段 E：Editor.Command 同步导出（迭代 12-15）

| 迭代 | 方案 | 结果 | 失败原因 |
|------|------|------|----------|
| 12 | `Editor.Command("_.ACISOUT", satPath)` | SAT 路径被当作点选坐标 | `SetImpliedSelection` 未将选择传递给 ACISOUT 的文件名提示 |
| 13 | `Editor.Command("_.ACISOUT", "_ALL", ...)` | `eInvalidInput` | **GStarCAD 不支持 `_ALL` 关键字作为命令参数** |
| 14 | `SendStringToExecute("_.SCRIPT ...")` | 13ms 返回，未执行 | **GStarCAD 不支持 `_.SCRIPT` 命令** |
| 15 | `Editor.Command("_.OPEN", satPath)` | `eInvalidInput` | **GStarCAD 不支持 `Editor.Command` 打开非 DWG 文件** |

### 阶段 F：脚本进程 + OCCT 导出（迭代 16，已弃用）

| 步骤 | 方法 | 说明 |
|------|------|------|
| Step 1a | `FILEDIA 0` + `SendStringToExecute("_.ACISOUT _ALL\\n\\n{path}\\n_.FILEDIA 1")` + `DoEvents()` 消息泵 | 在当前文档中异步导出 SAT |
| Step 1b | `ConvertSatToStepViaScript()` — 独立 GStarCAD 脚本进程 | 打开 SAT → SAVEAS 2018 STEP |
| Step 2 | `RunOCCTTool()` — 调用 OCCT C++ 工具 | HLR 投影生成四个正交方向的 2D STEP |
| Step 3 | 直接输出 2D STEP 文件 | 用户用 GStarCAD 打开后 SAVEAS DWG |

**弃用原因**：依赖外部 OCCT 原生运行库、需启动第二个 GStarCAD 实例（DDE/SDI 冲突）、时序脆弱、最终仅输出 STEP 需手动另存 DWG。

### 阶段 G：MESHVIEWEXPORT 自包含托管管线（迭代 17，✅ 当前主线）

放弃所有依赖 GStarCAD 原生命令 / COM / 外部 OCCT 的路线，改为**纯托管、单进程、直出 DWG**的三视图消隐管线。

| 步骤 | 方法 | 说明 |
|------|------|------|
| Step 1 | 临时 `FACETRES=10` + `EXPORT` 导出 STL（导出后恢复） | 提高曲面镶嵌精度，仅复用已验证的 `FILEDIA 0 + DoEvents` 等待逻辑 |
| Step 2a | `MeshTopology` — 焊接顶点、构建 3D 无向边邻接表 | 用量化 3D 端点做键，记录每条边的相邻三角形与二面角锐边标志 |
| Step 2b | `OrthoProjector` — 特征边提取 | 每个视图仅保留**轮廓边**（相邻面一正一背）、**锐边**（二面角 > 25°）、**边界边**（单三角形），滤除曲面镶嵌噪声线 |
| Step 2c | `DepthBuffer` — Z-buffer 光栅化消隐 | 正对相机三角形光栅化到深度缓冲；每条候选边沿线采样，按深度切分为可见/隐藏段 |
| Step 3 | `ViewArranger` — 第一角投影布局 + 共线合并 + 直出 DWG | 主视图（look +Y）/俯视图（look -Z，正下方）/左视图（look +X，正右方），长对正·高平齐·宽相等；可见→`VISIBLE_EDGES`(实线)，隐藏→`HIDDEN_EDGES`(虚线) |

**优点**：无 OCCT、无第二个 GStarCAD 实例、无手动另存，绕开全部已证实的 GStarCAD API 缺陷。

**验证**：以合成网格离线单测——立方体前视图收敛为干净的 4 边方框（镶嵌对角线被正确滤除）；圆柱三视图得到正确的轮廓线与背侧隐藏边。

### 阶段 H：BREP 直取调研（迭代 18，不可行）

设想跳过 STL、直接遍历 `Solid3d` 的精确 BREP 边（直线/圆弧/样条）投影以获得更高画质。经反射检查 `GStarCad.Net 20.22.0`（`gmdb.dll` / `gmap.dll`）：**未暴露 `GrxCAD.BoundaryRepresentation` 命名空间及 `Brep`/`Face`/`Edge` 类型**（仅存在无关的 `ConvertToBrepAtSubentPaths` 字符串）。因此 BREP 直取路线在当前 API 下**不可行**，维持 STL 网格主线。

---

## 三、发现的 GStarCAD API 限制

| 限制 | 影响 | 详情 |
|------|------|------|
| COM `GenerateSectionGeometry` 未实现 | Section API 不可用 | IDL 声明了方法，运行时报 E_NOTIMPL |
| COM IDispatch 不兼容标准 AutoCAD COM | 所有 COM 互操作不可靠 | `HandleToObject` 崩溃，`SelectionSet.Select` 始终空 |
| `Editor.Command()` 不支持 `_ALL` 关键字 | 无法通过 Command 选择全部实体 | 返回 `eInvalidInput` |
| `Editor.Command()` 不支持 `_.OPEN` 非 DWG | 无法程序化打开 SAT/STEP 等文件 | 返回 `eInvalidInput` |
| `_.SCRIPT` 命令不工作 | 无法通过脚本批量执行命令 | 13ms 返回，命令未执行 |
| `SECTIONPLANETOBLOCK` 不生成几何体 | 原生命令方式也不可用 | 剖面平面可见，但转换失败 |
| FLATSHOT 对话框无法抑制 | 无法自动执行 FLATSHOT | `CMDDIA=0` 无效 |
| `SetCurrentView()` 不生效 | 无法程序化设置视图方向 | 调用后视图不变 |
| `SendStringToExecute` 异步 | 命令执行顺序不可靠 | 需 DoEvents 消息泵辅助 |
| 独立 GStarCAD 进程 DDE/SDI 冲突 | 无法同时运行两个实例 | 第二个实例报"另一个程序正在运行中" |

---

## 四、当前架构（MESHVIEWEXPORT 主线）

```
MESHVIEWEXPORT 命令
  ├── 用户交互选择 3DSOLID (Editor.GetSelection)
  ├── ExportStl() — FACETRES=10 + _.EXPORT → STL（FILEDIA 0 + DoEvents 等待，导出后恢复 FACETRES）
  ├── StlParser.Parse() → List<StlTriangle>
  ├── OrthoProjector.Project()
  │   ├── MeshTopology — 焊接顶点 + 3D 边邻接表 + 锐边(二面角>25°)标志
  │   └── 每个视图 (Front look+Y / Top look-Z / Left look+X)
  │       ├── 特征边提取：轮廓边 + 锐边 + 边界边（滤除镶嵌噪声）
  │       └── DepthBuffer（Z-buffer 光栅化）→ 逐边切分可见/隐藏段
  └── ViewArranger.ArrangeAndSave()
      ├── 第一角布局：俯视图正下方、左视图正右方（长对正/高平齐/宽相等）
      ├── MergeCollinear 共线合并
      ├── VISIBLE_EDGES(实线) / HIDDEN_EDGES(虚线) 分层
      └── db.SaveAs(...DwgVersion.Current) → 直出三视图 DWG
```

> `VIEWEXPORT` / `HLREXPORT` / `FLATEXPORT` 的旧架构（依赖脚本进程 + OCCT）已弃用，代码保留仅作参考。

---

## 五、参与方与依赖

| 组件 | 技术 | 角色 |
|------|------|------|
| GStarCad.Net 20.22.0 | .NET Framework 4.8 | 浩辰 CAD 插件 API |
| MESHVIEWEXPORT 托管管线 | 纯 C#（MeshTopology / OrthoProjector / DepthBuffer / ViewArranger） | **当前主线**：STL→特征边+Z-buffer→三视图 DWG，无外部依赖 |
| GrxCAD.Interop.dll | COM Interop | COM 层（已证实不可靠，已弃用） |
| OCCT (Open CASCADE) 7.x | C++ 原生 | HLR 隐藏线消除投影（旧路线，已弃用） |
| OCCTProxy.dll / OCCTTool.exe | C++/CLI + .NET 控制台 | OCCT 桥接与命令行封装（旧路线，已弃用） |

---

*最后更新：2026-07-23*
