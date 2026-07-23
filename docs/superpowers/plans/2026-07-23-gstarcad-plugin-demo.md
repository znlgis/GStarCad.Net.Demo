# GStarCAD 2022 Plugin Demo — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Create a GStarCAD 2022 plugin demo project with three example commands (HelloWorld, DrawEntity, ModifyEntity) using C#, .NET Framework 4.8, and GStarCad.Net 20.22.0.

**Architecture:** SDK-style .csproj targeting net48 with PackageReference to GStarCad.Net. Plugin entry via IExtensionApplication. Three command classes in a Commands/ folder, each registered with CommandMethodAttribute. Build + deploy via build.ps1, debug via launchSettings.json pointing to GStarCAD.exe.

**Tech Stack:** C#, .NET Framework 4.8, GStarCad.Net 20.22.0, GrxCAD API (GrxCAD.* namespaces)

**Important API note:** The GStarCad.Net package uses **`GrxCAD`** namespace prefix (not `GStarCAD`), mirroring the AutoCAD .NET API:
- `GrxCAD.ApplicationServices` → Application, Document
- `GrxCAD.DatabaseServices` → Transaction, Line, Circle, DBText, BlockTable, BlockTableRecord
- `GrxCAD.Runtime` → CommandMethodAttribute, IExtensionApplication
- `GrxCAD.EditorInput` → Editor, PromptEntityResult, PromptEntityOptions
- `GrxCAD.Geometry` → Point3d

## Global Constraints

- Target framework: `net48`
- NuGet package: `GStarCad.Net` version `20.22.0`
- SDK-style `.csproj` format
- `<CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>`
- Compatible with Visual Studio 2022 and `dotnet build`
- Default GStarCAD 2022 path: `C:\Program Files\GStarCAD\GStarCAD 2022`
- Default plugin directory: `C:\Program Files\GStarCAD\GStarCAD 2022\Plugins`
- No new dependencies beyond GStarCad.Net
- All code in Chinese locale environment

---
````

I need to verify my assumptions about the GrxCAD API before writing the plan. Let me think through each command carefully:

For HelloWorldCommand:
- Method decorated with `[CommandMethod("HELLO")]`
- Inside: get Editor via `Application.DocumentManager.MdiActiveDocument.Editor`
- `editor.WriteMessage("\nHello, GStarCAD!")` for command line output
- `Application.ShowAlertDialog("Hello, GStarCAD!")` for dialog

For DrawEntityCommand:
- `[CommandMethod("DRAWDEMO")]`
- Start transaction on the current database
- Get BlockTable, get ModelSpace BlockTableRecord
- Create Line (Point3d(0,0,0) to Point3d(100,100,0))
- Create Circle (center Point3d(50,50,0), radius 25)
- Create DBText (position Point3d(0,120,0), text "GStarCAD Demo")
- Append to model space, add to transaction, commit

For ModifyEntityCommand:
- `[CommandMethod("MODIFYDEMO")]`
- Prompt user: `editor.GetEntity("Select an entity: ")`
- If valid result, open entity for write in transaction
- Set ColorIndex = 1 (red)
- Set Layer = "0"
- Commit transaction

For the entry point:
- Class implementing `IExtensionApplication`
- `Initialize()` method - can be empty, commands auto-register via attributes

Let me write the complete plan now with all code.

Actually, wait - let me reconsider. I should check if there's a `CommandClass` attribute needed or if `CommandMethod` auto-discovery works by default. In AutoCAD .NET API, commands in any loaded assembly are discovered automatically. The same should hold for GStarCAD.

Also, I need to think about how GStarCAD loads the plugin. Typically:
1. The DLL is placed in a plugins directory
2. GStarCAD loads it via a registry entry or a .netload-like command
3. Or through the application's plugin loading mechanism

For a demo, the most practical approach is:
- Build the DLL
- Copy to the plugin directory
- Use `NETLOAD` command in GStarCAD to load it
- Or configure auto-loading

Let me also think about what the entry point looks like. In AutoCAD .NET API, IExtensionApplication has Initialize() and Terminate(). Let me assume the same for GrxCAD.

OK, I have enough to write the plan. Let me do it now.

For the plan structure, I'll have these tasks:
1. Create solution and project scaffolding
2. Create plugin entry point (ExtensionApplication)
3. Create HelloWorldCommand
4. Create DrawEntityCommand
5. Create ModifyEntityCommand
6. Create build.ps1 deployment script
7. Create .vscode/tasks.json
8. Create launchSettings.json
9. Build and verify

Let me write each task with full code.<｜end▁of▁thinking｜>

<｜｜DSML｜｜tool_calls>
<｜｜DSML｜｜invoke name="write">
<｜｜DSML｜｜parameter name="content" string="true"># GStarCAD 2022 Plugin Demo — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Create a GStarCAD 2022 plugin demo project with three example commands (HelloWorld, DrawEntity, ModifyEntity) using C#, .NET Framework 4.8, and GStarCad.Net 20.22.0.

**Architecture:** SDK-style .csproj targeting net48 with PackageReference to GStarCad.Net. Plugin entry via IExtensionApplication. Three command classes in a Commands/ folder, each registered with CommandMethodAttribute. Build + deploy via build.ps1, debug via launchSettings.json pointing to GStarCAD.exe.

**Tech Stack:** C#, .NET Framework 4.8, GStarCad.Net 20.22.0, GrxCAD API (GrxCAD.* namespaces mirroring AutoCAD .NET API)

## Global Constraints

- Target framework: `net48`
- NuGet package: `GStarCad.Net` version `20.22.0`
- SDK-style `.csproj` format with `<CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>`
- Compatible with Visual Studio 2022 and `dotnet build`
- Default GStarCAD 2022 install path: `C:\Program Files\GStarCAD\GStarCAD 2022`
- Default plugin directory: `C:\Program Files\GStarCAD\GStarCAD 2022\Plugins`
- No new dependencies beyond GStarCad.Net
- API uses `GrxCAD.*` namespace prefix (e.g. `GrxCAD.Runtime`, `GrxCAD.ApplicationServices`, `GrxCAD.DatabaseServices`)

---

### Task 1: Create Solution and Project Scaffolding

**Files:**
- Create: `GStarCad.Net.Demo.sln`
- Create: `src/GStarCad.Net.Demo/GStarCad.Net.Demo.csproj`

**Interfaces:**
- Produces: `GStarCad.Net.Demo.sln` referencing `src/GStarCad.Net.Demo/GStarCad.Net.Demo.csproj`
- Produces: `.csproj` targeting `net48`, referencing `GStarCad.Net` 20.22.0, with `<CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>`

- [ ] **Step 1: Create directory structure**

```powershell
New-Item -ItemType Directory -Force -Path "src\GStarCad.Net.Demo\Commands"
New-Item -ItemType Directory -Force -Path "src\GStarCad.Net.Demo\Properties"
New-Item -ItemType Directory -Force -Path ".vscode"
```

- [ ] **Step 2: Create solution file**

```powershell
dotnet new sln -n GStarCad.Net.Demo --force
```

Expected: Creates `GStarCad.Net.Demo.sln` in workspace root.

- [ ] **Step 3: Create the SDK-style .csproj**

Write file `src/GStarCad.Net.Demo/GStarCad.Net.Demo.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net48</TargetFramework>
    <AssemblyName>GStarCad.Net.Demo</AssemblyName>
    <RootNamespace>GStarCad.Net.Demo</RootNamespace>
    <CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="GStarCad.Net" Version="20.22.0" />
  </ItemGroup>

</Project>
```

- [ ] **Step 4: Add project to solution**

```powershell
dotnet sln GStarCad.Net.Demo.sln add src/GStarCad.Net.Demo/GStarCad.Net.Demo.csproj
```

Expected: `Project 'src\GStarCad.Net.Demo\GStarCad.Net.Demo.csproj' added to the solution.`

- [ ] **Step 5: Restore NuGet packages and build**

```powershell
dotnet restore src\GStarCad.Net.Demo\GStarCad.Net.Demo.csproj
dotnet build src\GStarCad.Net.Demo\GStarCad.Net.Demo.csproj -c Debug
```

Expected: Build succeeds. Output at `src/GStarCad.Net.Demo/bin/Debug/net48/GStarCad.Net.Demo.dll`.

- [ ] **Step 6: Commit**

```powershell
git add GStarCad.Net.Demo.sln src/GStarCad.Net.Demo/GStarCad.Net.Demo.csproj
git commit -m "feat: add solution and project scaffolding for GStarCAD plugin"
```

---

### Task 2: Create Plugin Entry Point

**Files:**
- Create: `src/GStarCad.Net.Demo/ExtensionApplication.cs`

**Interfaces:**
- Consumes: `.csproj` with `GStarCad.Net` package (provides `GrxCAD.Runtime.IExtensionApplication`)
- Produces: `ExtensionApplication` class implementing `IExtensionApplication` with `Initialize()` and `Terminate()`

- [ ] **Step 1: Write ExtensionApplication.cs**

Write file `src/GStarCad.Net.Demo/ExtensionApplication.cs`:

```csharp
using GrxCAD.Runtime;

namespace GStarCad.Net.Demo
{
    public class ExtensionApplication : IExtensionApplication
    {
        public void Initialize()
        {
            // Commands are auto-discovered via [CommandMethod] attributes.
            // No explicit registration needed.
        }

        public void Terminate()
        {
        }
    }
}
```

- [ ] **Step 2: Build to verify compilation**

```powershell
dotnet build src/GStarCad.Net.Demo/GStarCad.Net.Demo.csproj -c Debug
```

Expected: Build succeeds with no errors.

- [ ] **Step 3: Commit**

```powershell
git add src/GStarCad.Net.Demo/ExtensionApplication.cs
git commit -m "feat: add plugin entry point implementing IExtensionApplication"
```

---

### Task 3: Create HelloWorldCommand

**Files:**
- Create: `src/GStarCad.Net.Demo/Commands/HelloWorldCommand.cs`

**Interfaces:**
- Consumes: `GrxCAD.Runtime.CommandMethodAttribute`, `GrxCAD.ApplicationServices.Application`
- Produces: `HELLO` command — writes greeting to command line and shows alert dialog

- [ ] **Step 1: Write HelloWorldCommand.cs**

Write file `src/GStarCad.Net.Demo/Commands/HelloWorldCommand.cs`:

```csharp
using GrxCAD.ApplicationServices;
using GrxCAD.Runtime;

namespace GStarCad.Net.Demo.Commands
{
    public class HelloWorldCommand
    {
        [CommandMethod("HELLO")]
        public void Hello()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var ed = doc.Editor;

            ed.WriteMessage("\nHello, GStarCAD! 欢迎使用浩辰CAD插件开发.");
            Application.ShowAlertDialog("Hello, GStarCAD!\n欢迎使用浩辰CAD 2022插件演示.");
        }
    }
}
```

- [ ] **Step 2: Build to verify compilation**

```powershell
dotnet build src/GStarCad.Net.Demo/GStarCad.Net.Demo.csproj -c Debug
```

Expected: Build succeeds with no errors.

- [ ] **Step 3: Commit**

```powershell
git add src/GStarCad.Net.Demo/Commands/HelloWorldCommand.cs
git commit -m "feat: add HELLO command - greeting and alert dialog"
```

---

### Task 4: Create DrawEntityCommand

**Files:**
- Create: `src/GStarCad.Net.Demo/Commands/DrawEntityCommand.cs`

**Interfaces:**
- Consumes: `GrxCAD.DatabaseServices.*` (Transaction, Line, Circle, DBText, BlockTable, BlockTableRecord), `GrxCAD.Geometry.Point3d`
- Produces: `DRAWDEMO` command — draws one line, one circle, and one text entity in current space

- [ ] **Step 1: Write DrawEntityCommand.cs**

Write file `src/GStarCad.Net.Demo/Commands/DrawEntityCommand.cs`:

```csharp
using GrxCAD.ApplicationServices;
using GrxCAD.DatabaseServices;
using GrxCAD.Geometry;
using GrxCAD.Runtime;

namespace GStarCad.Net.Demo.Commands
{
    public class DrawEntityCommand
    {
        [CommandMethod("DRAWDEMO")]
        public void DrawDemo()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                var line = new Line(new Point3d(0, 0, 0), new Point3d(100, 100, 0));
                line.ColorIndex = 1; // Red

                var circle = new Circle(new Point3d(50, 50, 0), Vector3d.ZAxis, 25);
                circle.ColorIndex = 3; // Green

                var text = new DBText();
                text.Position = new Point3d(0, 120, 0);
                text.Height = 10;
                text.TextString = "GStarCAD Demo - 浩辰CAD演示";

                btr.AppendEntity(line);
                tr.AddNewlyCreatedDBObject(line, true);

                btr.AppendEntity(circle);
                tr.AddNewlyCreatedDBObject(circle, true);

                btr.AppendEntity(text);
                tr.AddNewlyCreatedDBObject(text, true);

                tr.Commit();
            }

            ed.WriteMessage("\n绘制完成: 一条直线、一个圆、一行文字.");
        }
    }
}
```

- [ ] **Step 2: Build to verify compilation**

```powershell
dotnet build src/GStarCad.Net.Demo/GStarCad.Net.Demo.csproj -c Debug
```

Expected: Build succeeds with no errors.

- [ ] **Step 3: Commit**

```powershell
git add src/GStarCad.Net.Demo/Commands/DrawEntityCommand.cs
git commit -m "feat: add DRAWDEMO command - draw line, circle, and text"
```

---

### Task 5: Create ModifyEntityCommand

**Files:**
- Create: `src/GStarCad.Net.Demo/Commands/ModifyEntityCommand.cs`

**Interfaces:**
- Consumes: `GrxCAD.EditorInput.Editor.GetEntity()`, `GrxCAD.DatabaseServices.Entity.ColorIndex/Layer`
- Produces: `MODIFYDEMO` command — prompts user to select an entity, changes its color to red and layer to "0"

- [ ] **Step 1: Write ModifyEntityCommand.cs**

Write file `src/GStarCad.Net.Demo/Commands/ModifyEntityCommand.cs`:

```csharp
using GrxCAD.ApplicationServices;
using GrxCAD.DatabaseServices;
using GrxCAD.EditorInput;
using GrxCAD.Runtime;

namespace GStarCad.Net.Demo.Commands
{
    public class ModifyEntityCommand
    {
        [CommandMethod("MODIFYDEMO")]
        public void ModifyDemo()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            var opt = new PromptEntityOptions("\n请选择一个实体: ");
            opt.SetRejectMessage("\n无效选择, 请重新选择一个实体.");
            opt.AllowNone = false;

            var res = ed.GetEntity(opt);
            if (res.Status != PromptStatus.OK)
            {
                ed.WriteMessage("\n未选择任何实体.");
                return;
            }

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ent = (Entity)tr.GetObject(res.ObjectId, OpenMode.ForWrite);
                ent.ColorIndex = 1; // Red
                ent.Layer = "0";

                tr.Commit();
            }

            ed.WriteMessage("\n实体已修改: 颜色改为红色, 图层改为0.");
        }
    }
}
```

- [ ] **Step 2: Build to verify compilation**

```powershell
dotnet build src/GStarCad.Net.Demo/GStarCad.Net.Demo.csproj -c Debug
```

Expected: Build succeeds with no errors.

- [ ] **Step 3: Commit**

```powershell
git add src/GStarCad.Net.Demo/Commands/ModifyEntityCommand.cs
git commit -m "feat: add MODIFYDEMO command - change entity color and layer"
```

---

### Task 6: Create Build & Deploy Script

**Files:**
- Create: `build.ps1`

**Interfaces:**
- Consumes: none
- Produces: PowerShell script that builds the project and copies output to GStarCAD plugin directory

- [ ] **Step 1: Write build.ps1**

Write file `build.ps1`:

```powershell
param(
    [string]$Configuration = "Debug",
    [string]$GStarCADPath = "C:\Program Files\GStarCAD\GStarCAD 2022",
    [switch]$NoDeploy
)

$ErrorActionPreference = "Stop"
$projectDir = "$PSScriptRoot\src\GStarCad.Net.Demo"
$outputDir = "$projectDir\bin\$Configuration\net48"

Write-Host "=== GStarCAD Plugin Build ===" -ForegroundColor Cyan
Write-Host "Configuration: $Configuration"
Write-Host ""

Write-Host "[1/2] Building..." -ForegroundColor Yellow
dotnet build $projectDir\GStarCad.Net.Demo.csproj -c $Configuration
if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed!" -ForegroundColor Red
    exit $LASTEXITCODE
}
Write-Host "Build succeeded." -ForegroundColor Green

if ($NoDeploy) {
    Write-Host "[2/2] Skipping deploy (--NoDeploy)." -ForegroundColor Yellow
    Write-Host ""
    Write-Host "Output: $outputDir" -ForegroundColor Cyan
    exit 0
}

$pluginDir = "$GStarCADPath\Plugins"
Write-Host "[2/2] Deploying to $pluginDir ..." -ForegroundColor Yellow

if (-not (Test-Path $pluginDir)) {
    New-Item -ItemType Directory -Force -Path $pluginDir | Out-Null
}

Copy-Item -Path "$outputDir\*" -Destination $pluginDir -Recurse -Force
Write-Host "Deploy complete." -ForegroundColor Green
Write-Host ""
Write-Host "Plugin deployed to: $pluginDir" -ForegroundColor Cyan
Write-Host "Use NETLOAD command in GStarCAD to load GStarCad.Net.Demo.dll" -ForegroundColor Cyan
```

- [ ] **Step 2: Commit**

```powershell
git add build.ps1
git commit -m "feat: add build.ps1 script for build and deploy"
```

---

### Task 7: Create VS Code Build Task

**Files:**
- Create: `.vscode/tasks.json`

**Interfaces:**
- Consumes: `.csproj` at `src/GStarCad.Net.Demo/GStarCad.Net.Demo.csproj`
- Produces: VS Code task `build` that runs `dotnet build`

- [ ] **Step 1: Write .vscode/tasks.json**

Write file `.vscode/tasks.json`:

```json
{
    "version": "2.0.0",
    "tasks": [
        {
            "label": "build",
            "command": "dotnet",
            "type": "process",
            "args": [
                "build",
                "src/GStarCad.Net.Demo/GStarCad.Net.Demo.csproj",
                "-c",
                "Debug"
            ],
            "problemMatcher": "$msCompile",
            "group": {
                "kind": "build",
                "isDefault": true
            }
        },
        {
            "label": "deploy",
            "command": "powershell",
            "type": "process",
            "args": [
                "-ExecutionPolicy",
                "Bypass",
                "-File",
                "build.ps1"
            ],
            "problemMatcher": [],
            "dependsOn": []
        }
    ]
}
```

- [ ] **Step 2: Commit**

```powershell
git add .vscode/tasks.json
git commit -m "feat: add VS Code build and deploy tasks"
```

---

### Task 8: Create Debug Launch Configuration

**Files:**
- Create: `src/GStarCad.Net.Demo/Properties/launchSettings.json`

**Interfaces:**
- Consumes: GStarCAD 2022 installation at default path, build output DLL
- Produces: F5 debug profile that launches GStarCAD.exe with output directory as working directory

- [ ] **Step 1: Write launchSettings.json**

Write file `src/GStarCad.Net.Demo/Properties/launchSettings.json`:

```json
{
    "profiles": {
        "GStarCAD 2022": {
            "commandName": "Executable",
            "executablePath": "C:\\Program Files\\GStarCAD\\GStarCAD 2022\\GstarCAD.exe",
            "workingDirectory": "$(OutputPath)"
        }
    }
}
```

> **Note:** The `$(OutputPath)` MSBuild variable resolves to `bin\Debug\net48\` at debug time. If VS doesn't resolve it, replace with the full absolute output path. The working directory is set to the output directory so GStarCAD can find the plugin DLLs.

- [ ] **Step 2: Commit**

```powershell
git add src/GStarCad.Net.Demo/Properties/launchSettings.json
git commit -m "feat: add VS debug launch profile for GStarCAD 2022"
```

---

### Task 9: Final Build Verification

**Files:**
- Verify: all source files exist and project builds cleanly

- [ ] **Step 1: Full clean build**

```powershell
dotnet clean src/GStarCad.Net.Demo/GStarCad.Net.Demo.csproj
dotnet build src/GStarCad.Net.Demo/GStarCad.Net.Demo.csproj -c Debug
```

Expected: Build succeeds with zero errors and zero warnings.

- [ ] **Step 2: Verify output files exist**

```powershell
Test-Path src/GStarCad.Net.Demo/bin/Debug/net48/GStarCad.Net.Demo.dll
```

Expected: `True`. The DLL and its dependencies (gmap.dll, gmdb.dll, GrxCAD.Interop.dll) should all be present.

- [ ] **Step 3: Verify file structure matches design**

```powershell
Get-ChildItem -Recurse src\ -Include *.cs | Select-Object FullName
```

Expected output should include:
- `src\GStarCad.Net.Demo\ExtensionApplication.cs`
- `src\GStarCad.Net.Demo\Commands\HelloWorldCommand.cs`
- `src\GStarCad.Net.Demo\Commands\DrawEntityCommand.cs`
- `src\GStarCad.Net.Demo\Commands\ModifyEntityCommand.cs`

- [ ] **Step 4: Commit**

```powershell
git add -A
git status
# Only commit if there are uncommitted changes
git commit -m "chore: final verification - all files in place, build succeeds"
```

---

## Usage After Implementation

1. Run `.\build.ps1` to build and deploy
2. Start GStarCAD 2022
3. Enter `NETLOAD` command, browse to `Plugins\GStarCad.Net.Demo.dll`
4. Test commands: `HELLO`, `DRAWDEMO`, `MODIFYDEMO`
5. For debugging: open solution in Visual Studio, set breakpoints, press F5 with "GStarCAD 2022" profile
