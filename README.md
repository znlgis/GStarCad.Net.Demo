# GStarCad.Net.Demo

浩辰 CAD (GStarCAD) 2022 插件开发示例项目，使用 .NET Framework 4.8 和 C# 编写，基于
[GStarCad.Net](https://www.nuget.org/packages/GStarCad.Net/) NuGet 包（MIT 许可证）进行二次开发。

## 前置条件

- Windows 操作系统（7 / 10 / 11）
- [.NET Framework 4.8 SDK](https://dotnet.microsoft.com/download/dotnet-framework/net48)（或 Visual Studio 2019/2022）
- [GStarCAD 2022](https://www.gstarcad.com/) 已安装（默认路径 `C:\Program Files\GStarCAD\GStarCAD 2022`）

## 技术栈

| 组件 | 版本 |
| --- | --- |
| 目标框架 | .NET Framework 4.8 |
| 语言 | C# 7.3+ |
| 构建工具 | .NET CLI / Visual Studio |
| CAD 平台 | GStarCAD 2022 |
| CAD API 包 | GStarCad.Net 20.22.0（MIT 许可证） |

## 项目结构

```
GStarCad.Net.Demo/
├── .vscode/
│   └── tasks.json              # VS Code 构建与部署任务
├── src/
│   └── GStarCad.Net.Demo/
│       ├── Commands/
│       │   ├── DrawEntityCommand.cs     # 绘图命令
│       │   ├── HelloWorldCommand.cs     # 入门命令
│       │   └── ModifyEntityCommand.cs   # 修改实体命令
│       ├── Properties/
│       │   └── launchSettings.json      # VS 调试启动配置
│       ├── ExtensionApplication.cs      # 插件入口点
│       └── GStarCad.Net.Demo.csproj     # 项目文件 (SDK 风格)
├── build.ps1                  # 构建与部署脚本
├── GStarCad.Net.Demo.slnx     # 解决方案文件
├── LICENSE                    # MIT 许可证
└── README.md
```

## 演示命令

插件注册了三个 `CommandMethod`，在浩辰 CAD 命令行输入对应命令即可执行。

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

加载成功后，在命令行依次输入 `HELLO`、`DRAWDEMO`、`MODIFYDEMO` 体验各命令功能。

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

## 许可证

本项目及所用 NuGet 包 [GStarCad.Net](https://www.nuget.org/packages/GStarCad.Net/) 均采用 MIT 许可证。详见 [LICENSE](./LICENSE) 文件。
