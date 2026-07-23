# Task 3: ViewArranger — 2×2 网格 DWG 构建

**Files:**
- Create: `src/GStarCad.Net.Demo/Common/ViewArranger.cs`

**Interfaces:**
- Consumes: `ViewProjection`, `ProjectedEdge`, `Point2d`, `Point3d` (from GrxCAD.Geometry + Task 2 OrthoProjector)
- Produces: `ViewArranger.ArrangeAndSave(List<ViewProjection>, string outputPath)`

The types from Task 2 (OrthoProjector.cs, same namespace GStarCad.Net.Demo.Common):
```csharp
public struct ProjectedEdge {
    public Point2d Start;
    public Point2d End;
    public bool IsVisible;
    public ProjectedEdge(Point2d start, Point2d end, bool isVisible);
}

public class ViewProjection {
    public string Name;         // "Front", "Back", "Left", "Right"
    public List<ProjectedEdge> Edges;
}
```

## Complete Implementation Code

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using GrxCAD.DatabaseServices;
using GrxCAD.Geometry;

namespace GStarCad.Net.Demo.Common
{
    public static class ViewArranger
    {
        private const double Spacing = 50.0;
        private const string HiddenLayerName = "HIDDEN_EDGES";
        private const string VisibleLayerName = "VISIBLE_EDGES";

        public static void ArrangeAndSave(List<ViewProjection> projections, string outputPath)
        {
            using (var db = new Database(true, true))
            {
                // Create layers
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForWrite);
                    CreateLayer(lt, tr, db, VisibleLayerName, "Continuous");
                    CreateLayer(lt, tr, db, HiddenLayerName, "Hidden");
                    tr.Commit();
                }

                // Compute 2x2 grid offsets
                var gridLayout = CalculateGridOffsets(projections);

                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    var btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                    var visibleLayer = GetLayerId(tr, db, VisibleLayerName);
                    var hiddenLayer = GetLayerId(tr, db, HiddenLayerName);

                    // Grid layout: Front(0,0) Back(1,0) Left(0,1) Right(1,1)
                    var gridIndex = new Dictionary<string, int>
                    {
                        { "Front", 0 }, { "Back", 1 }, { "Left", 2 }, { "Right", 3 }
                    };

                    foreach (var proj in projections)
                    {
                        if (!gridIndex.TryGetValue(proj.Name, out var idx)) continue;

                        var col = idx % 2;
                        var row = idx / 2;
                        var offsetX = col * gridLayout.GridWidth;
                        var offsetY = row * gridLayout.GridHeight;

                        foreach (var edge in proj.Edges)
                        {
                            var layerId = edge.IsVisible ? visibleLayer : hiddenLayer;

                            var line = new Line(
                                new Point3d(edge.Start.X + offsetX, edge.Start.Y + offsetY, 0),
                                new Point3d(edge.End.X + offsetX, edge.End.Y + offsetY, 0));

                            line.LayerId = layerId;
                            btr.AppendEntity(line);
                            tr.AddNewlyCreatedDBObject(line, true);
                        }
                    }

                    tr.Commit();
                }

                db.SaveAs(outputPath, DwgVersion.Current);
            }
        }

        private struct GridLayout
        {
            public double GridWidth, GridHeight;
        }

        private static GridLayout CalculateGridOffsets(List<ViewProjection> projections)
        {
            double globalMinX = double.MaxValue, globalMaxX = double.MinValue;
            double globalMinY = double.MaxValue, globalMaxY = double.MinValue;

            var viewShifts = new Dictionary<string, Point2d>();

            foreach (var proj in projections)
            {
                double minX = double.MaxValue, minY = double.MaxValue;
                double maxX = double.MinValue, maxY = double.MinValue;

                foreach (var edge in proj.Edges)
                {
                    minX = Math.Min(minX, Math.Min(edge.Start.X, edge.End.X));
                    maxX = Math.Max(maxX, Math.Max(edge.Start.X, edge.End.X));
                    minY = Math.Min(minY, Math.Min(edge.Start.Y, edge.End.Y));
                    maxY = Math.Max(maxY, Math.Max(edge.Start.Y, edge.End.Y));
                }

                if (minX == double.MaxValue) continue;

                globalMinX = Math.Min(globalMinX, minX);
                globalMaxX = Math.Max(globalMaxX, maxX);
                globalMinY = Math.Min(globalMinY, minY);
                globalMaxY = Math.Max(globalMaxY, maxY);

                // Store center for later centering
                viewShifts[proj.Name] = new Point2d((minX + maxX) * 0.5, (minY + maxY) * 0.5);
            }

            var viewSizeX = globalMaxX - globalMinX;
            var viewSizeY = globalMaxY - globalMinY;

            var gridW = viewSizeX + Spacing;
            var gridH = viewSizeY + Spacing;

            // Recenter each view in its cell
            var gridIndex = new Dictionary<string, int>
            {
                { "Front", 0 }, { "Back", 1 }, { "Left", 2 }, { "Right", 3 }
            };

            foreach (var proj in projections)
            {
                if (!viewShifts.TryGetValue(proj.Name, out var center)) continue;
                if (!gridIndex.TryGetValue(proj.Name, out var idx)) continue;

                var col = idx % 2;
                var row = idx / 2;
                var cellCx = col * gridW + gridW * 0.5;
                var cellCy = row * gridH + gridH * 0.5;
                var shiftX = cellCx - center.X;
                var shiftY = cellCy - center.Y;

                // NOTE: ProjectedEdge is a struct. Must use for-loop with index
                // to mutate the list elements in place (foreach gives a copy).
                for (var ei = 0; ei < proj.Edges.Count; ei++)
                {
                    var edge = proj.Edges[ei];
                    proj.Edges[ei] = new ProjectedEdge(
                        new Point2d(edge.Start.X + shiftX, edge.Start.Y + shiftY),
                        new Point2d(edge.End.X + shiftX, edge.End.Y + shiftY),
                        edge.IsVisible);
                }
            }

            return new GridLayout { GridWidth = gridW, GridHeight = gridH };
        }

        private static void CreateLayer(LayerTable lt, Transaction tr, Database db,
            string name, string linetypeName)
        {
            if (lt.Has(name)) return;

            var ltr = new LayerTableRecord();
            ltr.Name = name;

            var ltt = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead);
            if (ltt.Has(linetypeName))
                ltr.LinetypeObjectId = ltt[linetypeName];

            lt.Add(ltr);
            tr.AddNewlyCreatedDBObject(ltr, true);
        }

        private static ObjectId GetLayerId(Transaction tr, Database db, string name)
        {
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            return lt[name];
        }
    }
}
```

## Build Verification

```
dotnet build src/GStarCad.Net.Demo/GStarCad.Net.Demo.csproj
```

## Global Constraints
- 目标框架：.NET Framework 4.8
- NuGet 依赖：仅 GStarCad.Net 20.22.0 + log4net 3.3.2
- 命名空间：GrxCAD.* (Runtime, ApplicationServices, DatabaseServices, EditorInput, Geometry)
- 无 AI 注释、无 emoji、无 catch-all 文件
- 使用 GrxCAD.DatabaseServices 的 Database, Transaction, BlockTable, BlockTableRecord, LayerTable, LayerTableRecord, LinetypeTable, Line
