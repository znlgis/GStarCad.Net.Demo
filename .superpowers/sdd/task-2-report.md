# Task 2 Report: OrthoProjector

## Status
**DONE**

## Summary
Created `src/GStarCad.Net.Demo/Common/OrthoProjector.cs` with `ProjectedEdge`, `ViewProjection`, and `OrthoProjector` types. OrthoProjector consumes `StlTriangle` and performs 4-view orthographic projection with back-face culling, edge deduplication, silhouette detection, and interior-edge occlusion testing.

No commits — file creation only.

## Build Verification
```
dotnet build src/GStarCad.Net.Demo/GStarCad.Net.Demo.csproj
```
Result: 已成功生成。0 warnings, 0 errors.

## Changes
- `src/GStarCad.Net.Demo/Common/OrthoProjector.cs` (new) — OrthoProjector static class with `Project(List<StlTriangle>)` → `List<ViewProjection>`, plus `ProjectedEdge` struct and `ViewProjection` class.

## Concerns
None.
