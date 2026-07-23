# GStarCad.Net.Demo

浩辰 CAD (GStarCAD) 2022 插件开发示例项目，使用 .NET Framework 4.8 和 C# 编写，基于
[GStarCad.Net](https://www.nuget.org/packages/GStarCad.Net/) NuGet 包（MIT 许可证）进行二次开发。

## 前置条件

- Windows 操作系统（7 / 10 / 11）
- [.NET Framework 4.8 SDK](https://dotnet.microsoft.com/download/dotnet-framework/net48)（或 Visual Studio 2019/2022）
- [GStarCAD 2022](https://www.gstarcad.com/) 已安装（默认路径 `C:\Program Files\GStarCAD\GStarCAD 2022`）
- （可选）[Open CASCADE Technology 7.x](https://dev.opencascade.org/) — OCCTTool 外部工具依赖

## 技术栈

| 组件 | 版本 |
| --- | --- |
| 目标框架 | .NET Framework 4.8 |
| 语言 | C# 7.3+ |
| 构建工具 | .NET CLI / Visual Studio |
| CAD 平台 | GStarCAD 2022 |
| CAD API 包 | GStarCad.Net 20.22.0（MIT 许可证） |
| 日志 | log4net 3.3.2 |
| 3D 投影引擎 | Open CASCADE Technology 7.x（OCCTTool 外部工具） |

## 项目结构

```
GStarCad.Net.Demo/
├── .vscode/
│   └── tasks.json                  # VS Code 构建与部署任务
├── docs/
│   ├── 3d-to-2d-export-research.md # 3D→2D 导出技术调研记录
│   └── 3d-to-2d-export-blog.md     # 技术博客总结
├── src/
│   └── GStarCad.Net.Demo/
│       ├── Commands/
│       │   ├── DrawEntityCommand.cs     # 绘图命令
│       │   ├── FlatExportCommand.cs     # FLATEXPORT 命令（实验性）
│       │   ├── HelloWorldCommand.cs     # 入门命令
│       │   ├── ModifyEntityCommand.cs   # 修改实体命令
│       │   └── ViewsExportCommand.cs    # 三维视图导出命令
│       ├── Properties/
│       │   └── launchSettings.json      # VS 调试启动配置
│       ├── ExtensionApplication.cs      # 插件入口点
│       ├── GStarCad.Net.Demo.csproj     # 项目文件 (SDK 风格)
│       └── log4net.config               # 日志配置文件
├── tools/
│   └── OCCTTool/
│       ├── app.config               # 工具配置文件
│       ├── OCCTTool.csproj          # 项目文件
│       └── Program.cs               # HLR 4 视图投影入口
├── build.ps1                        # 构建与部署脚本
├── GStarCad.Net.Demo.slnx           # 解决方案文件
├── LICENSE                          # MIT 许可证
└── README.md
```

**外部依赖（非源码）：**
- `OCCTProxy.dll` — C++/CLI 桥接 DLL，封装 Open CASCADE HLR API
- Open CASCADE Technology 7.x 运行时库

## 演示命令

插件注册了五个 `CommandMethod`，在浩辰 CAD 命令行输入对应命令即可执行。

### HELLO -- 入门命令

在命令行输出欢迎信息，并弹出对话框。

```
命令: HELLO
```

功能：
- 在命令行打印 `Hello, GStarCAD! 欢迎使用浩辰CAD插件开发.`
- 弹出对话框显示欢迎信息

### DRAWDEMO -- 绘图命令

在模型空间中自动绘制三个实体：一条直线、一个圆、一行文字。

```
命令: DRAWDEMO
```

功能：
- 绘制一条从 `(0,0,0)` 到 `(100,100,0)` 的红色直线
- 绘制一个圆心 `(50,50,0)`、半径 25 的绿色圆
- 在 `(0,120,0)` 处写入文字 "GStarCAD Demo - 浩辰CAD演示"

### MODIFYDEMO -- 修改实体命令

交互式选择图中实体，将其颜色改为红色、图层改为 "0"。

```
命令: MODIFYDEMO
请选择一个实体:   (在绘图区拾取实体)
```

功能：
- 提示用户拾取一个实体
- 将实体颜色设为红色（ColorIndex = 1）
- 将实体图层设为 "0"

### VIEWEXPORT -- 三维视图导出命令

选择 3D 实体，生成前/后/左/右四个正交方向的 2D 平面投影，保存为 2D STEP 文件。

```
命令: VIEWEXPORT
选择对象:   (选择 3DSOLID 实体，支持多选)
```

功能：
- 交互式多选 3D 实体（3DSOLID）
- Step 1：`SendStringToExecute` + `DoEvents` 驱动 GStarCAD 后台导出 SAT/STL 中间文件
- Step 2：独立 GStarCAD 进程将 SAT 转换为 STEP 格式
- Step 3：调用外部工具 `OCCTTool.exe`（基于 Open CASCADE HLR）生成 4 视图 2D 投影
- 输出到 `temp\{原文件名}_{时间戳}_views.stp`（2D STEP 文件）
- 用户打开 STEP 文件后可用 `SaveAs` 转为 DWG 格式

### FLATEXPORT -- 平面投影命令（实验性）

实验性命令，使用 GStarCAD 原生 `FLATSHOT` + `VPOINT` 命令生成 2D 投影。

```
命令: FLATEXPORT
```

功能：
- 切换视图到四个正交方向并执行 FLATSHOT
- 当前状态：**未完全可用** — GStarCAD 的 FLATSHOT 对话框无法以编程方式抑制（缺乏 `-FLATSHOT` 命令行模式），导致流程中断
- 保留作为参考实现，待官方 API 更新后可能恢复

## 日志

插件使用 log4net 3.3.2 记录运行日志。

- 配置文件：`log4net.config`
- 输出路径：`logs/GStarCad.Net.Demo.log`
- 滚动策略：单个文件最大 10 MB，按日期滚动
- 日志级别：DEBUG

## 快速开始

### 1. 克隆仓库

```bash
git clone https://github.com/znlgis/GStarCad.Net.Demo.git
cd GStarCad.Net.Demo
```

### 2. 构建

使用 .NET CLI 构建：

```bash
dotnet build src/GStarCad.Net.Demo/GStarCad.Net.Demo.csproj -c Debug
```

或使用 PowerShell 构建脚本（自动复制到 GStarCAD 插件目录）：

```powershell
.\build.ps1
```

跳过部署步骤，仅构建：

```powershell
.\build.ps1 -NoDeploy
```

自定义 GStarCAD 安装路径：

```powershell
.\build.ps1 -GStarCADPath "D:\Program Files\GStarCAD\GStarCAD 2022"
```

### 3. 加载插件

在 GStarCAD 2022 中执行以下操作之一：

- **NETLOAD 命令**：在命令行输入 `NETLOAD`，选择 `bin/Debug/net48/GStarCad.Net.Demo.dll`
- **自动加载**：将 DLL 复制到 GStarCAD 安装目录下的 `Plugins` 文件夹，重启 CAD 后自动加载

### 4. 测试命令

加载成功后，在命令行依次输入 `HELLO`、`DRAWDEMO`、`MODIFYDEMO`、`VIEWEXPORT`、`FLATEXPORT` 体验各命令功能。

## 调试

### Visual Studio

`Properties/launchSettings.json` 已配置启动外部程序。按以下步骤操作：

1. 在 Visual Studio 中打开 `GStarCad.Net.Demo.slnx`
2. 将 "GStarCAD 2022" 设为启动项目配置文件
3. 按 F5 启动调试，VS 将自动启动 GStarCAD 并加载插件

### VS Code

`.vscode/tasks.json` 提供了两个任务：

- **build** (`Ctrl+Shift+B`)：执行 `dotnet build`
- **deploy**：先构建，再执行 `build.ps1` 部署到 GStarCAD 插件目录

## 文档

- `docs/3d-to-2d-export-research.md` — 3D 到 2D 视图导出的完整技术调研记录，涵盖所有尝试方案、失败原因和 GStarCAD API 限制发现
- `docs/3d-to-2d-export-blog.md` — 技术博客文章，总结 VIEWEXPORT / FLATEXPORT 的开发历程与选型决策

## 许可证

本项目及所用 NuGet 包 [GStarCad.Net](https://www.nuget.org/packages/GStarCad.Net/) 均采用 MIT 许可证。详见 [LICENSE](./LICENSE) 文件。
