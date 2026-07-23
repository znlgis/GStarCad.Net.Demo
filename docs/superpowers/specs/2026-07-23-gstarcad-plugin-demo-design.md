# GStarCAD 2022 Plugin Demo — Design Spec

## Overview

Create a demonstration GStarCAD 2022 plugin project using C#, .NET Framework 4.8, and the `GStarCad.Net` NuGet package (v20.22.0). The project serves as both a learning reference and a starter template for GStarCAD plugin development.

## Architecture

### Project Structure

```
GStarCad.Net.Demo/
├── GStarCad.Net.Demo.sln
├── build.ps1                          # Build + deploy script
├── .vscode/
│   └── tasks.json                     # VS Code build task
├── src/
│   └── GStarCad.Net.Demo/
│       ├── GStarCad.Net.Demo.csproj   # SDK-style, TargetFramework=net48
│       ├── Properties/
│       │   └── launchSettings.json    # VS F5 debug: launch GStarCAD.exe
│       └── Commands/
│           ├── HelloWorldCommand.cs   # Command 1: greeting
│           ├── DrawEntityCommand.cs   # Command 2: draw entities
│           └── ModifyEntityCommand.cs # Command 3: modify entity properties
```

### Build

- SDK-style `.csproj` targeting `net48`
- NuGet reference: `<PackageReference Include="GStarCad.Net" Version="20.22.0" />`
- `<CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>` ensures all dependency DLLs are in the output directory
- Compatible with both `dotnet build` (CLI) and Visual Studio 2022

### Deployment

- `build.ps1` script: runs `dotnet build -c Debug`, then copies output DLLs to the default GStarCAD 2022 plugin directory
- Default install path: `C:\Program Files\GStarCAD\GStarCAD 2022\`

### Debugging

- `launchSettings.json` profile "GStarCAD 2022" with `executablePath` pointing to GStarCAD.exe
- Enables F5 debugging in Visual Studio with breakpoint support

## Components

### Three Demo Commands

| Command | Registration | Behavior |
|---------|-------------|----------|
| `HelloWorldCommand` | `HELLO` | Writes "Hello, GStarCAD!" to command line and shows a message box |
| `DrawEntityCommand` | `DRAWDEMO` | Draws a line, a circle, and a text entity in the current space |
| `ModifyEntityCommand` | `MODIFYDEMO` | Prompts user to select an entity, changes its color to red and layer to "0" |

### Command Registration

All commands use `CommandMethod` attribute on public instance methods, following the standard GStarCAD/AutoCAD .NET API pattern.

### Plugin Entry Point

A class implementing the appropriate `GStarCad.Net` extension application interface (e.g., `IExtensionApplication` with `Initialize()` method) handles plugin loading.

## APIs Used (Expected)

- `GStarCAD.ApplicationServices.Application` — document/editor access
- `GStarCAD.DatabaseServices` — entity creation, transaction management
- `GStarCAD.EditorInput` — user input (entity selection)
- `GStarCAD.Runtime.CommandMethod` — command registration
- `GStarCAD.Geometry` — point/vector types

## Error Handling

- Commands wrap entity operations in transactions with `Commit()`/`Abort()`
- User prompts check for null/empty input before proceeding
- Debug build only: no production hardening needed for a demo

## Non-Goals

- No custom UI (palettes, ribbon, etc.)
- No persistent settings or configuration files
- No multi-language support
