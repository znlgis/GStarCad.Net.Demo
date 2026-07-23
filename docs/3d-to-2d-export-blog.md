# 19 次迭代，只为在浩辰 CAD 中实现 3D 到 2D 视图导出

> 一条 VIEWEXPORT 命令背后的 16 次失败、10 个 API 限制发现，以及一次逆向工程师的真实记录。

---

## 引言

浩辰 CAD（GStarCAD）是国内市场占有率领先的 CAD 平台。它的 .NET API 用 `GrxCAD.*` 命名空间镜像了 AutoCAD .NET API 的接口设计。但镜像毕竟是镜像 —— 当我们试图在浩辰 CAD 2022 中实现一个从"选择 3D 实体 → 生成前/后/左/右四个正交方向的 2D 投影 → 输出为文件"的命令时，API 表面的"兼容"面具开始一块块剥落。

这篇文章记录了我们 19 次迭代的全部历程：每个方案、每次崩溃、每条 API 限制发现，以及最终落地的架构。

---

## 目标与初始设计

**需求**：浩辰 CAD 2022 插件 —— 用户选择 3D 实体 → 自动生成前、后、左、右四个正交方向的 2D 投影 → 输出为单个文件。

**环境**：C# / .NET Framework 4.8 / GStarCad.Net 20.22.0（NuGet 包 263 次下载，MIT 协议，包含 `GrxCAD.Interop.dll`、`gmap.dll`、`gmdb.dll` 三个 DLL）。

在 AutoCAD 中，实现这个需求有三条天然路径：`FLATSHOT`（平面摄影）、`SECTIONPLANE` + `SECTIONPLANETOBLOCK`、或者 COM `GenerateSectionGeometry` API。我们把这三条路全走了一遍。

---

## 第一幕：FLATSHOT 的诱惑（FLATEXPORT，3 次迭代）

FLATSHOT 是 AutoCAD 的经典命令：将当前视图方向的 3D 模型投影为 2D 线框图块。逻辑简单：

```csharp
// 设置正视图方向
ed.Command("VPOINT", "0,-1,0");

// 执行 FLATSHOT，传入插入点和选项
ed.Command("FLATSHOT", ins, 1, 1, 0);
```

**迭代 1**：用 .NET API `ViewTableRecord.SetCurrentView()` 设正交视图，四个方向输出相同 —— `SetCurrentView()` 在 GStarCAD 中不生效。

**迭代 2**：改用 COM `VPOINT` 异步设视图，输出仍然相同 —— COM `SendCommand` 是异步的，VPOINT 还没执行完，FLATSHOT 就已经跑了。

**迭代 3**：用 `Editor.Command("VPOINT", vp)` 同步执行，结果 FLATSHOT 对话框弹了出来。`CMDDIA=0`、`FILEDIA=0` 全线无效。

**结论**：FLATSHOT 这条路在浩辰 CAD 中走不通 —— `SetCurrentView()` 不生效、FLATSHOT 对话框无法抑制、异步时序问题无法解决。

---

## 第二幕：COM Section API —— 声明但不实现（迭代 4-6）

.NET 走不通，转向 COM 互操作。`GrxCAD.Interop.dll` 中声明了完整的 Section API：

```
IGcadSection.GenerateSectionGeometry(
    GcadEntity pEntity,
    ref object pIntersectionBoundaryObjs,
    ref object pIntersectionFillObjs,
    ref object pBackgroudnObjs,
    ref object pForegroudObjs,
    ref object pCurveTangencyObjs
)
```

这是一个标准的 AutoCAD COM 剖面生成接口，6 个参数，5 个 `ref` 输出。听起来很美。

**迭代 4**：`dynamic section.GenerateSectionGeometry()` —— 报错"缺少某个必需的参数"。方法有 6 个必需参数，无重载。

**迭代 5**：`dynamic` 不支持 `ref` 参数传递。改用 `Type.InvokeMember` + `ParameterModifier` 反射调用：

```csharp
object[] args = new object[6];
args[0] = (object)section;
args[1] = null; args[2] = null; // ... 5 ref outputs
ParameterModifier[] mods = new ParameterModifier[1];
mods[0] = new ParameterModifier(6);
mods[0][1] = true; // mark ref params
section.GetType().InvokeMember("GenerateSectionGeometry",
    BindingFlags.InvokeMethod, null, (object)section, args, mods, null, null);
```

调用成功了 —— 但内部抛出了 `TargetInvocationException`，内层异常是 E_NOTIMPL。

**迭代 6**：在调用前设置 Section 类型为 2D 投影模式：

```csharp
section.Settings.CurrentSectionType = GcSectionType.gcSectionType2dSection;
// 配置 GenerationOptions、SourceObjects、DestinationBlock...
section.GenerateSectionGeometry(...); // 依然是 E_NOTIMPL
```

**结论**：GStarCAD 2022 的 COM IDL 声明了 `GenerateSectionGeometry` 方法，但运行时就是"未实现该方法或操作"。API 声明存在，实现不存在 —— 这是逆向工程师最常见也最无解的死胡同。

---

## 第三幕：原生命令 —— 平面的反叛（迭代 7-9）

既然 COM API 不实现，那就用 GStarCAD 自己的命令。

**迭代 7**：`SECTIONPLANE` (3 点) + `SECTIONPLANETOBLOCK`。剖面平面创建成功，可视化正确 —— 但 `SECTIONPLANETOBLOCK` 报"未生成任何几何体"。平面没穿过实体。

**迭代 8**：扩大 3 倍范围的 3 点 SECTIONPLANE。还是"未生成任何几何体"。

**迭代 9**：用 `(handent "handle")` 精确选择剖面平面后再执行 SECTIONPLANETOBLOCK。选择成功，几何体仍然不生成。

**结论**：GStarCAD 的 SECTIONPLANE 能创建可视化的剖面平面，但 `SECTIONPLANETOBLOCK` 命令似乎在任何条件下都拒绝生成 2D 投影几何体。

---

## 第四幕：COM Interop —— 完全崩溃（迭代 10-11）

回到 COM 层，尝试直接操作实体。

**迭代 10**：COM `HandleToObject`

```csharp
dynamic comDoc = doc.AcadDocument;
dynamic ent = comDoc.HandleToObject(handleStr);
```

直接触发 `AccessViolationException` —— 原生内存访问违规，进程级崩溃。

**迭代 11**：COM `SelectionSet.Select()` —— 尝试了全部模式：

| 模式 | 结果 |
|------|------|
| `acSelectionSetCrossing` (1) + 过滤器 | Count = 0 |
| `acSelectionSetAll` (4) + 过滤器 | 异常"值不在预期的范围内" |
| `acSelectionSetPrevious` (2) | Count = 0 |

GStarCAD 的 COM IDispatch 实现与标准 AutoCAD COM 完全不可互换。通过 `dynamic` 调用的任何 COM 方法都是俄罗斯轮盘赌 —— 要么返回空，要么抛异常，要么崩溃。

**结论**：GStarCAD 的 COM 层彻底不可用。所有 COM 互操作代码必须从插件中移除。

---

## 第五幕：文件格式的泥潭（迭代 12-18）

COM 不行，原生命令也不行。剩下的路：把实体导出为 3D 格式文件，用外部工具做投影，再导回来。

### 导出格式探索

| 尝试 | 结果 |
|------|------|
| `_.EXPORT .stp` | GStarCAD EXPORT 不支持 STEP 格式 |
| `_.EXPORT .step` | 同上 |
| `_.IGESOUT` | GStarCAD 无此命令 |
| `_.ACISOUT` (SAT) | 导出成功 | 

ACISOUT 能导出 SAT（ACIS 实体模型，精确 B-Rep 格式）。这个格式 OCCT 可以处理。

### Editor.Command 的陷坑

有了导出格式，需要把文件路径传给命令。AutoCAD 开发者会自然地写：

```csharp
ed.Command("_.ACISOUT", "_ALL", "", satPath);
```

在 GStarCAD 中：

| 尝试 | 结果 | 诊断 |
|------|------|------|
| `Editor.Command("_.OPEN", satPath)` | `eInvalidInput` | 不支持打开非 DWG 文件 |
| `Editor.Command("_.ACISOUT", "_ALL", ...)` | `eInvalidInput` | 不支持 `_ALL` 关键字 |
| `Editor.Command("_.ACISOUT", satPath)` | 路径被当点选坐标 | `SetImpliedSelection` 不传递选择到文件名提示 |
| `SendStringToExecute("_.SCRIPT ...")` | 13ms 返回，未执行 | GStarCAD 不支持 `_.SCRIPT` 命令 |

GStarCAD 的 `Editor.Command` 比 AutoCAD 瘦得多：不支持 `_ALL`、不支持打开非 DWG、不支持 SCRIPT、不支持 IGESOUT。Selection 和命令参数之间的桥梁几乎不存在。

### 最终绕行：SendStringToExecute + DoEvents

`SendStringToExecute` 是唯一能把命令字符串送入 GStarCAD 队列的方法 —— 但它异步执行。需要 `Application.DoEvents()` 来泵送消息队列，让命令在当前 `CommandMethod` 返回前执行完。

```csharp
ed.Command("_.FILEDIA", 0);

doc.SendStringToExecute(
    string.Format("_.ACISOUT _ALL\n\n{0}\n_.FILEDIA 1 ",
        satPath.Replace('\\', '/')),
    false, false, false);

// 消息泵等待 SAT 文件生成
var deadline = DateTime.Now.AddSeconds(30);
while (DateTime.Now < deadline)
{
    System.Windows.Forms.Application.DoEvents();
    if (File.Exists(satPath) && new FileInfo(satPath).Length > 100)
        break;
    Thread.Sleep(200);
}
```

`_ALL` 关键字在 `Editor.Command` 中报 eInvalidInput，但在 `SendStringToExecute` 中正常工作 —— 因为后者直接把字符串交给 GStarCAD 的命令解释器，绕过了 .NET Command 层的参数类型检查。

### SAT → STEP 转换

ACISOUT 输出的是 SAT，但 OCCT 的原生格式是 STEP。需要一个独立的 GStarCAD 脚本进程来转换：

```csharp
// 独立 GStarCAD 进程，用 /b 脚本模式
var script = "FILEDIA 0\n_.OPEN \"satFile.sat\"\n_.SAVEAS 2018 \"stepFile.stp\"\n_.QUIT Y\n";
Process.Start(gcadExe, "/b " + scriptPath);
```

但第二个 GStarCAD 实例会因为 DDE/SDI 冲突而报"另一个程序正在运行中"—— 需要设置 `SDI=0` 来允许。

---

## 第六幕：OCCT —— 真正的投影引擎

GStarCAD 自己不能做 2D 投影，我们引入 Open CASCADE Technology (OCCT) —— 工业级开源几何内核，原生支持 HLR（隐藏线消除）投影。

在 OCCTProxy.dll（C++/CLI 桥接）中实现了 `Generate2DViews` 方法：

```cpp
void OCCTProxy::Generate2DViews(const char* inputFile, const char* outputFile)
{
    // 1. 读取 STEP 文件
    STEPControl_Reader reader;
    reader.ReadFile(inputFile);
    reader.TransferRoots();
    TopoDS_Shape shape = reader.OneShape();

    // 2. 四个正交方向
    gp_Dir dirs[4] = {
        gp_Dir(0, -1, 0),   // Front
        gp_Dir(0,  1, 0),   // Back
        gp_Dir(-1, 0, 0),   // Left
        gp_Dir(1,  0, 0)    // Right
    };

    TopoDS_Compound result;
    BRep_Builder builder;
    builder.MakeCompound(result);

    for (int i = 0; i < 4; i++)
    {
        // 3. HLR 投影（精确算法，不需三角化）
        HLRBRep_Algo algo;
        algo.Add(shape);
        HLRAlgo_Projector projector(gp_Ax2(gp_Pnt(0,0,0), dirs[i]));
        algo.Projector(projector);
        algo.Update();
        algo.ShowAll();

        // 4. 提取可见边 + 隐藏边
        HLRBRep_HLRToShape hlrToShape(&algo);
        TopoDS_Shape visible = hlrToShape.VCompound();
        TopoDS_Shape hidden = hlrToShape.HCompound();

        builder.Add(result, visible);
        builder.Add(result, hidden);
    }

    // 5. 导出 2D STEP
    STEPControl_Writer writer;
    writer.Transfer(result, STEPControl_AsIs);
    writer.Write(outputFile);
}
```

四个方向的可见边和隐藏边合并到一个文件中，输出为 2D STEP。

---

## 最终架构

```
VIEWEXPORT 命令
  │
  ├── 用户交互选择 3DSOLID (Editor.GetSelection)
  │
  ├── [Step 1] SendStringToExecute + DoEvents
  │   ├── FILEDIA=0 → ACISOUT _ALL → SAT 文件
  │   ├── 独立 GStarCAD 脚本进程: OPEN SAT → SAVEAS STEP
  │   └── 回退: EXPORT STL
  │
  ├── [Step 2] OCCTTool.exe (命令行封装)
  │   └── OCCTProxy.dll (C++/CLI)
  │       └── Open CASCADE (C++)
  │           └── HLRBRep_Algo 精确投影 × 4 方向
  │
  └── [Step 3] 输出 2D STEP → 用户 SAVEAS DWG
```

完全绕过了 GStarCAD 的 COM 层、FLATSHOT、SECTIONPLANETOBLOCK。唯一依赖 GStarCAD 的环节是 Step 1 的 ACISOUT 导出 —— 而且通过 `SendStringToExecute` 而不是 `Editor.Command`。

---

## 10 条 GStarCAD API 限制发现

| # | 限制 | 严重度 |
|---|------|--------|
| 1 | COM `GenerateSectionGeometry` 声明但未实现 (E_NOTIMPL) | 阻断 |
| 2 | COM IDispatch 不兼容标准 AutoCAD COM（崩溃/空结果） | 阻断 |
| 3 | `Editor.Command` 不支持 `_ALL` 关键字 | 阻断 |
| 4 | `Editor.Command` 不支持 `_.OPEN` 非 DWG 文件 | 阻断 |
| 5 | `_.SCRIPT` 命令在 GStarCAD 中不工作 | 阻断 |
| 6 | `SECTIONPLANETOBLOCK` 在任何条件下不生成几何体 | 阻断 |
| 7 | FLATSHOT 对话框无法通过 `CMDDIA`/`FILEDIA` 抑制 | 阻断 |
| 8 | `ViewTableRecord.SetCurrentView()` 不生效 | 影响 |
| 9 | `SendStringToExecute` 异步导致命令执行顺序不可控 | 影响 |
| 10 | 独立 GStarCAD 进程有 DDE/SDI 冲突 | 影响 |

其中 7 条是阻断级的 —— 意味着对应路径完全走不通，不是"有限制"而是"不存在"。

---

## GrxCAD 与 AutoCAD：表面兼容，实质鸿沟

浩辰 CAD 的 .NET API 命名空间从 `Autodesk.AutoCAD.*` 转换为 `GrxCAD.*`，类名基本一致。这种映射给人一个强烈的"兼容"预期：

| AutoCAD API | 预期行为 | GrxCAD 实际 |
|-------------|----------|------------|
| `Editor.Command("_ALL")` | 选择全部实体 | `eInvalidInput` |
| `Editor.Command("_.OPEN", "file.stp")` | 打开 STEP 文件 | `eInvalidInput` |
| `COM Document.HandleToObject(handle)` | 返回实体引用 | AccessViolation 崩溃 |
| `COM SelectionSet.Select()` | 选择集操作 | 所有模式 Count=0 |
| `COM Section.GenerateSectionGeometry()` | 剖面生成 | E_NOTIMPL |
| `SendStringToExecute("_.SCRIPT ...")` | 执行脚本 | 不执行 |

关键发现：**命名空间映射不等于行为等价**。GrxCAD 只实现了 .NET API 的子集（`GetSelection`、`Transaction`、`Entity` 等基础操作），而高级互操作功能（COM 层、Section API、Script 命令、FLATSHOT 参数）签名存在但行为缺失或完全不同。

---

## 教训总结

### 1. 在浩辰 CAD 中永远不要用 COM Interop

`GrxCAD.Interop.dll` 只在文件系统中是 .dll。运行时不给你任何保证。`HandleToObject` 崩溃。`SelectionSet.Select` 空结果。`GenerateSectionGeometry` 是虚函数。凡是能通过 `doc.AcadDocument` 拿到的 COM 对象，都不要假设它有用。

### 2. SendStringToExecute 是最后的稻草

当 `Editor.Command` 拒绝所有有意义的参数时，`SendStringToExecute` 把原始命令字符串送入 GStarCAD 解释器。它绕过参数类型检查，`_ALL` 在 `Editor.Command` 中报错但在 `SendStringToExecute` 中正常工作。代价是异步 —— 需要 `DoEvents` 来泵送消息。

### 3. 独立脚本进程是最可靠的桥梁

对于格式转换（SAT→STEP、STEP→DWG），启动独立的 GStarCAD 脚本进程，用 `/b` 参数执行 `.scr` 脚本。它有自己的 STA 线程和文档上下文，不破坏当前命令处理器状态。

### 4. 把核心功能交给外部工具

如果 GStarCAD 不能做某件事 —— 不要试图绕。引入专门的工具（OCCT）做 2D 投影。GStarCAD 只负责最简单的输入（用户选择）和最简单的输出（文件导出），中间的投影逻辑全部由 OCCT 完成。

### 5. GStarCAD API 测试方法

在浩辰 CAD 中，永远不要假设任何一个 AutoCAD API 可直接用。对每个 API 调用包装 try/catch 并记录结果。用 `log4net` 记录每一步的输入输出，用 `Stopwatch` 计时，用 Catch-all 模式降级而不是崩溃。

---

## 结语

19 次迭代，16 次 VIEWEXPORT 尝试，3 次 FLATEXPORT 尝试，10 条 API 限制，1 次 AccessViolation 崩溃，3 个完全转向的方案。

这不是"API 设计不好"。这是"API 签名存在但行为不兼容" —— 最危险的一种兼容性声明。它诱惑你进入实施，然后在运行时让你崩溃。

浩辰 CAD 的 .NET API 在基础 CRUD 操作上可用。但当你踏入 COM Interop、高级几何操作、原生命令参数化这个领域时，准备好遇到 E_NOTIMPL。预期会失败的那个方案，可能反而是最快走通的路线。

---

*最终方案提交于 `636bfe4`，全部源码在 [GStarCad.Net.Demo](https://github.com/znlgis/GStarCad.Net.Demo)。*
