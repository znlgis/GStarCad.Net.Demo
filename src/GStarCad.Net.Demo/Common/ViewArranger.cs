using System;
using System.Collections.Generic;
using GrxCAD.DatabaseServices;
using GrxCAD.Geometry;

namespace GStarCad.Net.Demo.Common
{
    /// <summary>
    /// Lays out the three orthographic views following the Chinese national standard
    /// (first-angle projection): the top view sits directly below the front view (长对正),
    /// the left view sits directly to the right of the front view (高平齐), and the top and
    /// left views share the depth dimension (宽相等). Views keep their true scale and the
    /// projection alignment relationships are preserved. Coincident collinear segments are
    /// merged before being written to the DWG to keep the entity count low.
    /// </summary>
    public static class ViewArranger
    {
        private const double Spacing = 30.0;
        private const string HiddenLayerName = "HIDDEN_EDGES";
        private const string VisibleLayerName = "VISIBLE_EDGES";

        public static void ArrangeAndSave(List<ViewProjection> projections, string outputPath)
        {
            LayoutFirstAngle(projections);

            foreach (var proj in projections)
                proj.Edges = MergeCollinear(proj.Edges);

            using (var db = new Database(true, true))
            {
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForWrite);
                    CreateLayer(lt, tr, db, VisibleLayerName, "Continuous");
                    CreateLayer(lt, tr, db, HiddenLayerName, "Hidden");
                    tr.Commit();
                }

                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    var btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                    var visibleLayer = GetLayerId(tr, db, VisibleLayerName);
                    var hiddenLayer = GetLayerId(tr, db, HiddenLayerName);

                    foreach (var proj in projections)
                    {
                        foreach (var edge in proj.Edges)
                        {
                            var line = new Line(
                                new Point3d(edge.Start.X, edge.Start.Y, 0),
                                new Point3d(edge.End.X, edge.End.Y, 0));
                            line.LayerId = edge.IsVisible ? visibleLayer : hiddenLayer;
                            btr.AppendEntity(line);
                            tr.AddNewlyCreatedDBObject(line, true);
                        }
                    }

                    tr.Commit();
                }

                db.SaveAs(outputPath, DwgVersion.Current);
            }
        }

        private struct Bounds
        {
            public double MinX, MinY, MaxX, MaxY;
            public bool Valid;
        }

        private static Bounds ComputeBounds(ViewProjection proj)
        {
            var b = new Bounds
            {
                MinX = double.MaxValue,
                MinY = double.MaxValue,
                MaxX = double.MinValue,
                MaxY = double.MinValue
            };

            foreach (var e in proj.Edges)
            {
                b.MinX = Math.Min(b.MinX, Math.Min(e.Start.X, e.End.X));
                b.MaxX = Math.Max(b.MaxX, Math.Max(e.Start.X, e.End.X));
                b.MinY = Math.Min(b.MinY, Math.Min(e.Start.Y, e.End.Y));
                b.MaxY = Math.Max(b.MaxY, Math.Max(e.Start.Y, e.End.Y));
                b.Valid = true;
            }

            return b;
        }

        private static ViewProjection Find(List<ViewProjection> projections, string name)
        {
            foreach (var p in projections)
                if (p.Name == name) return p;
            return null;
        }

        private static void LayoutFirstAngle(List<ViewProjection> projections)
        {
            var front = Find(projections, "Front");
            var top = Find(projections, "Top");
            var left = Find(projections, "Left");

            if (front == null) return;

            var fb = ComputeBounds(front);
            if (!fb.Valid) return;

            // Front view: place its min corner at the origin.
            var frontDx = -fb.MinX;
            var frontDy = -fb.MinY;
            Translate(front, frontDx, frontDy);
            var frontWidth = fb.MaxX - fb.MinX;

            // Top view: directly below the front view, X aligned (长对正).
            if (top != null)
            {
                var tb = ComputeBounds(top);
                if (tb.Valid)
                {
                    var dx = frontDx; // share the same horizontal origin as the front view
                    var dy = -Spacing - tb.MaxY;
                    Translate(top, dx, dy);
                }
            }

            // Left view: directly to the right of the front view, Z aligned (高平齐).
            if (left != null)
            {
                var lb = ComputeBounds(left);
                if (lb.Valid)
                {
                    var dx = frontWidth + Spacing - lb.MinX;
                    var dy = frontDy; // share the same vertical origin as the front view
                    Translate(left, dx, dy);
                }
            }
        }

        private static void Translate(ViewProjection proj, double dx, double dy)
        {
            for (var i = 0; i < proj.Edges.Count; i++)
            {
                var e = proj.Edges[i];
                proj.Edges[i] = new ProjectedEdge(
                    new Point2d(e.Start.X + dx, e.Start.Y + dy),
                    new Point2d(e.End.X + dx, e.End.Y + dy),
                    e.IsVisible);
            }
        }

        /// <summary>
        /// Merge collinear segments that touch or overlap, so the many small welded mesh edges
        /// along a straight boundary collapse into a few long lines. Gaps are never bridged.
        /// </summary>
        private static List<ProjectedEdge> MergeCollinear(List<ProjectedEdge> edges)
        {
            const double angQuant = 1e-4;
            const double posQuant = 1e-4;
            const double tol = 1e-6;

            var groups = new Dictionary<long, List<int>>();
            for (var i = 0; i < edges.Count; i++)
            {
                var e = edges[i];
                var dx = e.End.X - e.Start.X;
                var dy = e.End.Y - e.Start.Y;
                var len = Math.Sqrt(dx * dx + dy * dy);
                if (len < tol) continue;
                dx /= len;
                dy /= len;

                // Canonical direction (unsigned line orientation).
                if (dx < 0 || (Math.Abs(dx) < 1e-12 && dy < 0)) { dx = -dx; dy = -dy; }

                var ang = Math.Atan2(dy, dx);              // 0..pi
                var c = -dy * e.Start.X + dx * e.Start.Y;  // signed perpendicular offset
                var key = HashLine((long)Math.Round(ang / angQuant),
                                   (long)Math.Round(c / posQuant),
                                   e.IsVisible);

                if (!groups.TryGetValue(key, out var list))
                {
                    list = new List<int>();
                    groups[key] = list;
                }
                list.Add(i);
            }

            var result = new List<ProjectedEdge>(edges.Count);

            foreach (var kv in groups)
            {
                var indices = kv.Value;
                var first = edges[indices[0]];
                var fdx = first.End.X - first.Start.X;
                var fdy = first.End.Y - first.Start.Y;
                var flen = Math.Sqrt(fdx * fdx + fdy * fdy);
                fdx /= flen;
                fdy /= flen;
                if (fdx < 0 || (Math.Abs(fdx) < 1e-12 && fdy < 0)) { fdx = -fdx; fdy = -fdy; }

                var refP = first.Start;
                var visible = first.IsVisible;

                var intervals = new List<double[]>();
                foreach (var idx in indices)
                {
                    var e = edges[idx];
                    var t0 = (e.Start.X - refP.X) * fdx + (e.Start.Y - refP.Y) * fdy;
                    var t1 = (e.End.X - refP.X) * fdx + (e.End.Y - refP.Y) * fdy;
                    if (t0 > t1) { var tmp = t0; t0 = t1; t1 = tmp; }
                    intervals.Add(new[] { t0, t1 });
                }

                intervals.Sort((x, y) => x[0].CompareTo(y[0]));

                var curStart = intervals[0][0];
                var curEnd = intervals[0][1];
                for (var i = 1; i < intervals.Count; i++)
                {
                    if (intervals[i][0] <= curEnd + tol)
                    {
                        curEnd = Math.Max(curEnd, intervals[i][1]);
                    }
                    else
                    {
                        result.Add(MakeSegment(refP, fdx, fdy, curStart, curEnd, visible));
                        curStart = intervals[i][0];
                        curEnd = intervals[i][1];
                    }
                }
                result.Add(MakeSegment(refP, fdx, fdy, curStart, curEnd, visible));
            }

            return result;
        }

        private static long HashLine(long a, long c, bool visible)
        {
            unchecked
            {
                long h = a * 1000003L ^ c * 19349663L;
                return visible ? h : ~h;
            }
        }

        private static ProjectedEdge MakeSegment(Point2d refP, double dx, double dy,
            double t0, double t1, bool visible)
        {
            return new ProjectedEdge(
                new Point2d(refP.X + dx * t0, refP.Y + dy * t0),
                new Point2d(refP.X + dx * t1, refP.Y + dy * t1),
                visible);
        }

        private static void CreateLayer(LayerTable lt, Transaction tr, Database db,
            string name, string linetypeName)
        {
            if (lt.Has(name)) return;

            var ltr = new LayerTableRecord { Name = name };
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
