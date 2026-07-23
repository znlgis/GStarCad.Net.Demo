# 在浩辰 CAD 中实现 3D 到 2D 视图导出的一次实践

> 记录 VIEWEXPORT 命令开发过程中尝试过的方案、遇到的问题，以及最终可行的做法。

---

## 引言

浩辰 CAD（GStarCAD）的 .NET API 用 `GrxCAD.*` 命名空间对应了 AutoCAD .NET API 的接口。在浩辰 CAD 2022 中开发一个"选择 3D 实体 → 生成前/后/左/右四个正交方向的 2D 投影 → 输出为文件"的命令时，我们尝试了多条路径，发现了一些与 AutoCAD 行为不一致的地方。

以下是开发过程中的方案记录。

---

## 需求与环境

**需求**：浩辰 CAD 2022 插件 —— 用户选择 3D 实体 → 自动生成前、后、左、右四个正交方向的 2D 投影 → 输出文件。

**环境**：C# / .NET Framework 4.8 / GStarCad.Net 20.22.0（NuGet 包，包含 `GrxCAD.Interop.dll`、`gmap.dll`、`gmdb.dll` 三个 DLL）。

在 AutoCAD 中，这类需求通常有三条路径：`FLATSHOT`（平面摄影）、`SECTIONPLANE` + `SECTIONPLANETOBLOCK`、或者 COM `GenerateSectionGeometry` API。我们在浩辰 CAD 中分别做了尝试。

---

## 路径一：FLATSHOT 命令

FLATSHOT 是 AutoCAD 中将当前视图方向的 3D 模型投影为 2D 线框图块的命令：

```csharp
ed.Command("VPOINT", "0,-1,0");
ed.Command("FLATSHOT", ins, 1, 1, 0);
```

尝试过程：

- 用 .NET API `ViewTableRecord.SetCurrentView()` 设正交视图 —— 四个方向输出相同，`SetCurrentView()` 在 GStarCAD 中不生效。
- 改用 COM 异步 `VPOINT` —— 仍输出相同，COM `SendCommand` 是异步的，VPOINT 执行完之前 FLATSHOT 就已经跑了。
- 用 `Editor.Command("VPOINT", vp)` 同步执行 —— FLATSHOT 对话框弹了出来。`CMDDIA=0` 和 `FILEDIA=0` 无法抑制。

**结论**：`SetCurrentView()` 不生效、FLATSHOT 对话框无法抑制。这条路径在浩辰 CAD 中不可用。

---

## 路径二：COM Section API

`GrxCAD.Interop.dll` 中声明了 Section 相关的 COM 接口：

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

尝试过程：

- `dynamic section.GenerateSectionGeometry()` —— 报错"缺少某个必需的参数"。方法有 6 个必需参数，无重载。
- `dynamic` 不支持 `ref` 参数传递，改用 `Type.InvokeMember` + `ParameterModifier` 反射调用：

```csharp
object[] args = new object[6];
args[0] = (object)section;
args[1] = null; args[2] = null;
ParameterModifier[] mods = new ParameterModifier[1];
mods[0] = new ParameterModifier(6);
mods[0][1] = true; mods[0][2] = true; // mark ref params
section.GetType().InvokeMember("GenerateSectionGeometry",
    BindingFlags.InvokeMethod, null, (object)section, args, mods, null, null);
```

- 反射调用成功，内部抛出 `TargetInvocationException`，内层异常是 E_NOTIMPL（未实现）。
- 在调用前设置 Section 类型为 2D 投影模式（`gcSectionType2dSection`），配置 `GenerationOptions`、`SourceObjects`、`DestinationBlock` 后调用 —— 仍然是 E_NOTIMPL。

**结论**：GStarCAD 2022 的 COM IDL 声明了 `GenerateSectionGeometry` 方法，但运行时未实现。API 声明存在，调用返回 E_NOTIMPL。

---

## 路径三：原生命令 SECTIONPLANE + SECTIONPLANETOBLOCK

不依赖 COM，直接使用 GStarCAD 原生命令。

尝试过程：

- `SECTIONPLANE`（3 点法）创建剖面平面成功，可视化正确。执行 `SECTIONPLANETOBLOCK`，报"截面创建操作因截面对象的位置未生成任何几何体"。
- 扩大剖面平面范围到包围盒的 3 倍 —— 同样结果。
- 用 `(handent "handle")` LISP 语法精确选择剖面平面后执行 SECTIONPLANETOBLOCK —— 选择成功，仍不生成几何体。

**结论**：SECTIONPLANE 能可视化，但 `SECTIONPLANETOBLOCK` 在这些条件下不生成 2D 投影。

---

## 路径四：COM 直接操作实体

尝试直接通过 COM 操作实体引用：

- `comDoc.HandleToObject(handleStr)` —— 触发 `AccessViolationException`，原生内存访问违规。
- COM `SelectionSet.Select()` 尝试全部选择模式（Crossing、All、Previous），所有模式 Count=0 或抛异常。

GStarCAD 的 COM IDispatch 实现与标准 AutoCAD COM 有较大差异。通过 `dynamic` 调用的 COM 方法行为不可预期。

**结论**：COM 互操作在浩辰 CAD 中不可靠，应避免使用。

---

## 路径五：文件格式导出

在内存/COM 操作都不行的情况下，考虑将实体导出为文件，用外部工具处理。

### 导出格式

| 尝试 | 结果 |
|------|------|
| `_.EXPORT .stp` / `.step` | GStarCAD EXPORT 不支持 STEP 格式 |
| `_.IGESOUT` | 命令不存在 |
| `_.ACISOUT` | 导出成功 |

ACISOUT 可以导出 SAT（ACIS 实体模型，精确 B-Rep 格式）。

### Editor.Command 参数限制

在将文件路径传给导出命令时，遇到了一些与 AutoCAD 不一致的行为：

| 调用 | 结果 |
|------|------|
| `Editor.Command("_.ACISOUT", "_ALL", "", satPath)` | `eInvalidInput` |
| `Editor.Command("_.OPEN", satPath)` | `eInvalidInput` |
| `Editor.Command("_.ACISOUT", satPath)` | 路径被当作点选坐标 |
| `SendStringToExecute("_.SCRIPT ...")` | 命令不执行 |

GStarCAD 的 `Editor.Command` 不支持 `_ALL` 关键字、不支持打开非 DWG 文件、不支持 `_.SCRIPT`。`SetImpliedSelection` 不会将当前选择传递给导出命令的文件名提示。

### 可用的做法：SendStringToExecute + DoEvents

`SendStringToExecute` 可以将命令字符串送入 GStarCAD 的命令队列，`_ALL` 关键字在这个过程中可以正常工作（不同于 `Editor.Command`）。由于它是异步的，需要通过 `Application.DoEvents()` 泵送消息队列，让命令在 `CommandMethod` 返回前执行完：

```csharp
ed.Command("_.FILEDIA", 0);

doc.SendStringToExecute(
    string.Format("_.ACISOUT _ALL\n\n{0}\n_.FILEDIA 1 ",
        satPath.Replace('\\', '/')),
    false, false, false);

// 消息泵等待文件生成
var deadline = DateTime.Now.AddSeconds(30);
while (DateTime.Now < deadline)
{
    System.Windows.Forms.Application.DoEvents();
    if (File.Exists(satPath) && new FileInfo(satPath).Length > 100)
        break;
    Thread.Sleep(200);
}
```

### SAT → STEP 转换

ACISOUT 输出的是 SAT 格式，后续的投影工具 OCCT 的原生输入格式是 STEP。需要一个独立的 GStarCAD 脚本进程做格式转换：

```csharp
var script = "FILEDIA 0\n_.OPEN \"satFile.sat\"\n_.SAVEAS 2018 \"stepFile.stp\"\n_.QUIT Y\n";
Process.Start(gcadExe, "/b " + scriptPath);
```

注意第二个 GStarCAD 实例可能因为 DDE/SDI 冲突而报"另一个程序正在运行中"，需在脚本中设置 `SDI=0`。

---

## 投影引擎：Open CASCADE (OCCT)

GStarCAD 自身不能生成 2D 投影，引入 Open CASCADE Technology —— 工业级开源几何内核，原生支持 HLR（隐藏线消除）投影。

在 OCCTProxy.dll（C++/CLI 桥接）中实现投影逻辑：

```cpp
void OCCTProxy::Generate2DViews(const char* inputFile, const char* outputFile)
{
    // 读取 STEP
    STEPControl_Reader reader;
    reader.ReadFile(inputFile);
    reader.TransferRoots();
    TopoDS_Shape shape = reader.OneShape();

    // 四个正交方向
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
        HLRBRep_Algo algo;
        algo.Add(shape);
        HLRAlgo_Projector projector(gp_Ax2(gp_Pnt(0,0,0), dirs[i]));
        algo.Projector(projector);
        algo.Update();
        algo.ShowAll();

        HLRBRep_HLRToShape hlrToShape(&algo);
        builder.Add(result, hlrToShape.VCompound());  // 可见边
        builder.Add(result, hlrToShape.HCompound());  // 隐藏边
    }

    STEPControl_Writer writer;
    writer.Transfer(result, STEPControl_AsIs);
    writer.Write(outputFile);
}
```

使用 `HLRBRep_Algo` 精确算法（不需要三角化），四个方向分别投影，可见边和隐藏边合并到输出文件。

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
  ├── [Step 2] OCCTTool.exe
  │   └── OCCTProxy.dll (C++/CLI)
  │       └── Open CASCADE (C++/HLRBRep_Algo)
  │
  └── [Step 3] 输出 2D STEP → 用户 SAVEAS DWG
```

完全绕过了 GStarCAD 的 COM 层、FLATSHOT 和 SECTIONPLANETOBLOCK。GStarCAD 只负责 Step 1 的 ACISOUT 导出，投影逻辑全部由 OCCT 完成。

---

## GStarCAD API 行为差异记录

| # | 差异 | 影响 |
|---|------|------|
| 1 | COM `GenerateSectionGeometry` 声明但运行时 E_NOTIMPL | Section COM API 不可用 |
| 2 | COM IDispatch 不兼容标准 AutoCAD COM | 所有 COM 互操作不可靠 |
| 3 | `Editor.Command` 不支持 `_ALL` 关键字 | 无法通过 Command 做全选 |
| 4 | `Editor.Command` 不支持 `_.OPEN` 非 DWG 文件 | 无法程序化打开 STEP/SAT |
| 5 | `_.SCRIPT` 命令不工作 | 无法通过脚本批量执行 |
| 6 | `SECTIONPLANETOBLOCK` 不生成几何体 | 原生命令投影不可用 |
| 7 | FLATSHOT 对话框无法抑制 | 无法自动化 FLATSHOT |
| 8 | `ViewTableRecord.SetCurrentView()` 不生效 | 无法程序化设视图方向 |
| 9 | `SendStringToExecute` 异步 | 命令顺序需额外处理 |
| 10 | 独立 GStarCAD 进程 DDE/SDI 冲突 | 需 SDI=0 处理 |

---

## 总结

### 在浩辰 CAD 开发中的几个注意点

**避免 COM Interop。** `GrxCAD.Interop.dll` 中的 COM 接口行为不可预期，`HandleToObject` 可能崩溃，`SelectionSet.Select` 可能空返回，部分 API 只声明未实现。

**`SendStringToExecute` 可以作为备选。** 当 `Editor.Command` 不支持某些关键字时，`SendStringToExecute` 将原始命令字符串送入 GStarCAD 解释器，绕过了 .NET Command 层的参数检查。代价是异步执行，需要 `DoEvents` 配合。

**独立脚本进程适用于格式转换。** 对于 SAT→STEP、STEP→DWG 这类转换，启动独立的 GStarCAD 脚本进程用 `/b` 参数执行 `.scr` 脚本，不干扰当前命令处理器。

**核心功能考虑外部工具。** 如果 GStarCAD 自身不支持某类操作，可以考虑引入专门的工具。本项目中的 HLR 投影由 Open CASCADE 完成。

**每个 API 调用需要验证。** 在浩辰 CAD 中，AutoCAD API 的等价调用行为并非总是相同。建议对关键 API 调用加上错误处理和日志记录。

---

*全部源码：[GStarCad.Net.Demo](https://github.com/znlgis/GStarCad.Net.Demo)*
