# Task 2: OrthoProjector — 正交投影引擎

**Files:**
- Create: `src/GStarCad.Net.Demo/Common/OrthoProjector.cs`

**Interfaces:**
- Consumes: `StlTriangle` (from Task 1), `Point3d`, `Vector3d`, `Point2d` (from GrxCAD.Geometry)
- Produces: `ProjectedEdge` struct, `ViewProjection` class, `OrthoProjector.Project(List<StlTriangle>)` → `List<ViewProjection>`

The StlTriangle struct is in `src/GStarCad.Net.Demo/Common/StlParser.cs`:
```csharp
public struct StlTriangle {
    public Vector3d Normal;
    public Point3d V1, V2, V3;
    public Point3d this[int index] { get; }
}
```

## Algorithm Summary

For each of 4 orthographic views (Front, Back, Left, Right):
1. Back-face culling: skip triangles whose normal faces away from camera
2. Edge collection: for each front-facing triangle, project its 3 edges to 2D, deduplicate by a canonical edge key
3. Silhouette detection: edges bordering a back-face triangle are always visible
4. Interior edge occlusion: for all-front-face edges, check if midpoint is covered by a closer triangle (barycentric point-in-triangle)
5. Output visible edges and hidden (dashed) edges

View definitions:
- Front: cameraDir=(0,1,0), DropAxis=Y, KeepH=X, KeepV=Z
- Back: cameraDir=(0,-1,0), DropAxis=Y, KeepH=X, KeepV=Z
- Left: cameraDir=(-1,0,0), DropAxis=X, KeepH=Y, KeepV=Z
- Right: cameraDir=(1,0,0), DropAxis=X, KeepH=Y, KeepV=Z

## Complete Implementation Code

```csharp
using System;
using System.Collections.Generic;
using GrxCAD.Geometry;

namespace GStarCad.Net.Demo.Common
{
    public struct ProjectedEdge
    {
        public Point2d Start;
        public Point2d End;
        public bool IsVisible;

        public ProjectedEdge(Point2d start, Point2d end, bool isVisible)
        {
            Start = start;
            End = end;
            IsVisible = isVisible;
        }
    }

    public class ViewProjection
    {
        public string Name;
        public List<ProjectedEdge> Edges;
    }

    public static class OrthoProjector
    {
        private static readonly ViewDef[] Views = new[]
        {
            new ViewDef("Front",  new Vector3d(0,  1, 0), AxisIndex.Y, AxisIndex.X, AxisIndex.Z),
            new ViewDef("Back",   new Vector3d(0, -1, 0), AxisIndex.Y, AxisIndex.X, AxisIndex.Z),
            new ViewDef("Left",   new Vector3d(-1, 0, 0), AxisIndex.X, AxisIndex.Y, AxisIndex.Z),
            new ViewDef("Right",  new Vector3d( 1, 0, 0), AxisIndex.X, AxisIndex.Y, AxisIndex.Z),
        };

        private enum AxisIndex { X = 0, Y = 1, Z = 2 }

        private struct ViewDef
        {
            public string Name;
            public Vector3d CameraDir; // object-to-camera
            public AxisIndex DropAxis;
            public AxisIndex KeepH;
            public AxisIndex KeepV;

            public ViewDef(string name, Vector3d cameraDir, AxisIndex drop, AxisIndex h, AxisIndex v)
            {
                Name = name;
                CameraDir = cameraDir;
                DropAxis = drop;
                KeepH = h;
                KeepV = v;
            }
        }

        public static List<ViewProjection> Project(List<StlTriangle> triangles)
        {
            var results = new List<ViewProjection>(4);

            foreach (var view in Views)
            {
                var proj = ProjectView(triangles, view);
                results.Add(proj);
            }

            return results;
        }

        private static ViewProjection ProjectView(List<StlTriangle> triangles, ViewDef view)
        {
            // Step A: Classify triangles (front-facing / back-facing)
            var triFlags = new bool[triangles.Count]; // true = front-facing

            for (var i = 0; i < triangles.Count; i++)
            {
                var dot = triangles[i].Normal.DotProduct(view.CameraDir);
                triFlags[i] = dot > 0; // normal faces toward camera
            }

            // Step B: Build edge map (keyed by normalized 2D endpoints)
            // Value: list of (triangle index, is front-facing)
            var edgeMap = new Dictionary<ulong, List<EdgeRef>>();

            for (var i = 0; i < triangles.Count; i++)
            {
                var tri = triangles[i];
                var isFront = triFlags[i];

                for (var e = 0; e < 3; e++)
                {
                    var a = ProjectPoint(tri[e], view);
                    var b = ProjectPoint(tri[(e + 1) % 3], view);
                    var key = EdgeKey(a, b);

                    if (!edgeMap.TryGetValue(key, out var refs))
                    {
                        refs = new List<EdgeRef>(2);
                        edgeMap[key] = refs;
                    }
                    refs.Add(new EdgeRef { TriIndex = i, A = a, B = b, IsFront = isFront });
                }
            }

            // Step C: Classify edges
            var edges = new List<ProjectedEdge>();

            foreach (var kv in edgeMap)
            {
                var refs = kv.Value;
                var frontCount = 0;
                var backCount = 0;

                foreach (var r in refs)
                {
                    if (r.IsFront) frontCount++;
                    else backCount++;
                }

                var a = refs[0].A;
                var b = refs[0].B;

                if (frontCount == 0)
                {
                    // All back-facing — hidden behind object, skip entirely
                    continue;
                }
                else if (backCount > 0 && frontCount > 0)
                {
                    // Silhouette edge — always visible
                    edges.Add(new ProjectedEdge(a, b, true));
                }
                else
                {
                    // Interior edge (all front-facing) — check occlusion
                    var visible = !IsOccluded(a, b, triangles, triFlags, view);
                    if (visible)
                        edges.Add(new ProjectedEdge(a, b, true));
                    else
                        edges.Add(new ProjectedEdge(a, b, false));
                }
            }

            return new ViewProjection { Name = view.Name, Edges = edges };
        }

        private static bool IsOccluded(Point2d edgeA, Point2d edgeB,
            List<StlTriangle> triangles, bool[] triFlags, ViewDef view)
        {
            // Check midpoint + quarter-points against closer triangles
            var mid = new Point2d(
                (edgeA.X + edgeB.X) * 0.5,
                (edgeA.Y + edgeB.Y) * 0.5);

            var q1 = new Point2d(
                edgeA.X * 0.75 + edgeB.X * 0.25,
                edgeA.Y * 0.75 + edgeB.Y * 0.25);

            var q3 = new Point2d(
                edgeA.X * 0.25 + edgeB.X * 0.75,
                edgeA.Y * 0.25 + edgeB.Y * 0.75);

            var checkPoints = new[] { mid, q1, q3 };

            // Build list of (depth, triIndex) for front-facing triangles, sorted closest-to-farthest
            var depthList = BuildDepthList(triangles, triFlags, view);

            foreach (var cp in checkPoints)
            {
                // Check if any CLOSER triangle covers this 2D point
                foreach (var entry in depthList)
                {
                    var tri = triangles[entry.TriIndex];
                    var t2d = new Point2d[3]
                    {
                        ProjectPoint(tri.V1, view),
                        ProjectPoint(tri.V2, view),
                        ProjectPoint(tri.V3, view)
                    };

                    if (PointInTriangle(cp, t2d[0], t2d[1], t2d[2]))
                        return true; // occluded by a closer triangle
                }
            }

            return false;
        }

        private struct DepthEntry
        {
            public double Depth;
            public int TriIndex;
        }

        private static List<DepthEntry> BuildDepthList(
            List<StlTriangle> triangles, bool[] triFlags, ViewDef view)
        {
            var list = new List<DepthEntry>();

            for (var i = 0; i < triangles.Count; i++)
            {
                if (!triFlags[i]) continue;

                var tri = triangles[i];
                var d = GetPointDepth(tri.V1, view.CameraDir)
                      + GetPointDepth(tri.V2, view.CameraDir)
                      + GetPointDepth(tri.V3, view.CameraDir);

                list.Add(new DepthEntry { Depth = d / 3.0, TriIndex = i });
            }

            // Sort: closest to camera first (lowest depth first)
            list.Sort((a, b) => a.Depth.CompareTo(b.Depth));
            return list;
        }

        private static double GetPointDepth(Point3d pt, Vector3d cameraDir)
        {
            // Depth = projection onto camera direction
            return pt.X * cameraDir.X + pt.Y * cameraDir.Y + pt.Z * cameraDir.Z;
        }

        private static bool PointInTriangle(Point2d p, Point2d a, Point2d b, Point2d c)
        {
            var d1 = Sign(p, a, b);
            var d2 = Sign(p, b, c);
            var d3 = Sign(p, c, a);
            var hasNeg = (d1 < 0) || (d2 < 0) || (d3 < 0);
            var hasPos = (d1 > 0) || (d2 > 0) || (d3 > 0);
            return !(hasNeg && hasPos);
        }

        private static double Sign(Point2d p1, Point2d p2, Point2d p3)
        {
            return (p1.X - p3.X) * (p2.Y - p3.Y) - (p2.X - p3.X) * (p1.Y - p3.Y);
        }

        private static Point2d ProjectPoint(Point3d pt, ViewDef view)
        {
            var coords = new double[] { pt.X, pt.Y, pt.Z };
            return new Point2d(
                coords[(int)view.KeepH],
                coords[(int)view.KeepV]);
        }

        private struct EdgeRef
        {
            public int TriIndex;
            public Point2d A, B;
            public bool IsFront;
        }

        private static ulong EdgeKey(Point2d a, Point2d b)
        {
            // Normalize: ensure a <= b for consistent key
            double x1, y1, x2, y2;
            if (a.X < b.X || (Math.Abs(a.X - b.X) < 1e-9 && a.Y <= b.Y))
            {
                x1 = a.X; y1 = a.Y; x2 = b.X; y2 = b.Y;
            }
            else
            {
                x1 = b.X; y1 = b.Y; x2 = a.X; y2 = a.Y;
            }

            // Combine into a single ulong via bit-interleaving of quantized ints
            unchecked
            {
                var ix1 = (long)(x1 * 10000);
                var iy1 = (long)(y1 * 10000);
                var ix2 = (long)(x2 * 10000);
                var iy2 = (long)(y2 * 10000);
                return (ulong)ix1 ^ ((ulong)iy1 << 11) ^ ((ulong)ix2 << 22) ^ ((ulong)iy2 << 33);
            }
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
- 使用 GrxCAD.Geometry 的 Vector3d / Point3d / Point2d 类型
