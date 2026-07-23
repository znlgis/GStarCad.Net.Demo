using System;
using System.Collections.Generic;
using GrxCAD.Geometry;

namespace GStarCad.Net.Demo.Common
{
    /// <summary>
    /// Orthographic depth buffer used for hidden-line removal. Front-facing triangles are
    /// rasterized into a regular grid storing the nearest depth per cell; candidate edges are
    /// then sampled and split into visible / hidden segments by comparing their depth against
    /// the buffer. Because the projection is orthographic, depth varies affinely across a
    /// triangle in screen space, so linear interpolation is exact.
    /// </summary>
    internal sealed class DepthBuffer
    {
        private const int Resolution = 900;      // cells along the larger screen dimension
        private const double MinDepthGap = 1e-9;

        private readonly double[] _depth;        // nearest depth per cell (+inf if empty)
        private readonly int _w;
        private readonly int _h;
        private readonly double _minH;
        private readonly double _minV;
        private readonly double _cell;
        private readonly double _bias;
        private readonly bool _empty;

        private DepthBuffer(bool empty)
        {
            _empty = empty;
        }

        private DepthBuffer(double[] depth, int w, int h, double minH, double minV, double cell, double bias)
        {
            _depth = depth;
            _w = w;
            _h = h;
            _minH = minH;
            _minV = minV;
            _cell = cell;
            _bias = bias;
        }

        public static DepthBuffer Build(MeshTopology mesh, bool[] triFront,
            Vector3d right, Vector3d up, Vector3d look)
        {
            double minH = double.MaxValue, minV = double.MaxValue;
            double maxH = double.MinValue, maxV = double.MinValue;
            double minD = double.MaxValue, maxD = double.MinValue;
            var any = false;

            for (var i = 0; i < mesh.Triangles.Count; i++)
            {
                if (!triFront[i]) continue;
                var tri = mesh.Triangles[i];
                foreach (var p in new[] { tri.V1, tri.V2, tri.V3 })
                {
                    var hh = p.DotProduct(right);
                    var vv = p.DotProduct(up);
                    var dd = p.DotProduct(look);
                    if (hh < minH) minH = hh;
                    if (hh > maxH) maxH = hh;
                    if (vv < minV) minV = vv;
                    if (vv > maxV) maxV = vv;
                    if (dd < minD) minD = dd;
                    if (dd > maxD) maxD = dd;
                    any = true;
                }
            }

            if (!any) return new DepthBuffer(true);

            var sizeH = Math.Max(maxH - minH, 1e-9);
            var sizeV = Math.Max(maxV - minV, 1e-9);
            var cell = Math.Max(sizeH, sizeV) / Resolution;
            if (cell <= 0) cell = 1e-6;

            var w = (int)Math.Ceiling(sizeH / cell) + 2;
            var h = (int)Math.Ceiling(sizeV / cell) + 2;
            var depth = new double[w * h];
            for (var i = 0; i < depth.Length; i++) depth[i] = double.PositiveInfinity;

            var range = Math.Max(maxD - minD, 1e-9);
            var bias = range * 1e-3 + MinDepthGap;

            var buffer = new DepthBuffer(depth, w, h, minH, minV, cell, bias);

            for (var i = 0; i < mesh.Triangles.Count; i++)
            {
                if (!triFront[i]) continue;
                var tri = mesh.Triangles[i];
                buffer.Rasterize(
                    ToScreen(tri.V1, right, up, look),
                    ToScreen(tri.V2, right, up, look),
                    ToScreen(tri.V3, right, up, look));
            }

            return buffer;
        }

        private static Vec3 ToScreen(Point3d p, Vector3d right, Vector3d up, Vector3d look)
        {
            return new Vec3(p.DotProduct(right), p.DotProduct(up), p.DotProduct(look));
        }

        private struct Vec3
        {
            public readonly double H, V, D;
            public Vec3(double h, double v, double d) { H = h; V = v; D = d; }
        }

        private void Rasterize(Vec3 a, Vec3 b, Vec3 c)
        {
            // Pixel-space triangle bounds.
            var ax = (a.H - _minH) / _cell;
            var ay = (a.V - _minV) / _cell;
            var bx = (b.H - _minH) / _cell;
            var by = (b.V - _minV) / _cell;
            var cx = (c.H - _minH) / _cell;
            var cy = (c.V - _minV) / _cell;

            var x0 = (int)Math.Floor(Math.Min(ax, Math.Min(bx, cx)));
            var x1 = (int)Math.Ceiling(Math.Max(ax, Math.Max(bx, cx)));
            var y0 = (int)Math.Floor(Math.Min(ay, Math.Min(by, cy)));
            var y1 = (int)Math.Ceiling(Math.Max(ay, Math.Max(by, cy)));

            if (x0 < 0) x0 = 0;
            if (y0 < 0) y0 = 0;
            if (x1 > _w - 1) x1 = _w - 1;
            if (y1 > _h - 1) y1 = _h - 1;

            var denom = (by - cy) * (ax - cx) + (cx - bx) * (ay - cy);
            if (Math.Abs(denom) < 1e-12) return; // degenerate in screen space
            var invDenom = 1.0 / denom;

            for (var py = y0; py <= y1; py++)
            {
                var sy = py + 0.5;
                for (var px = x0; px <= x1; px++)
                {
                    var sx = px + 0.5;
                    var l0 = ((by - cy) * (sx - cx) + (cx - bx) * (sy - cy)) * invDenom;
                    var l1 = ((cy - ay) * (sx - cx) + (ax - cx) * (sy - cy)) * invDenom;
                    var l2 = 1.0 - l0 - l1;
                    if (l0 < -1e-6 || l1 < -1e-6 || l2 < -1e-6) continue;

                    var d = l0 * a.D + l1 * b.D + l2 * c.D;
                    var idx = py * _w + px;
                    if (d < _depth[idx]) _depth[idx] = d;
                }
            }
        }

        /// <summary>Sample the edge and append visible / hidden segments to <paramref name="output"/>.</summary>
        public void SplitEdge(Point2d a, Point2d b, double depthA, double depthB, List<ProjectedEdge> output)
        {
            if (_empty)
            {
                output.Add(new ProjectedEdge(a, b, true));
                return;
            }

            var pixLen = Math.Sqrt(
                (a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y)) / _cell;
            var samples = (int)Math.Ceiling(pixLen) + 1;
            if (samples < 2) samples = 2;
            if (samples > 4000) samples = 4000;

            var startT = 0.0;
            var prev = IsVisibleAt(a, b, depthA, depthB, 0.0);

            for (var s = 1; s <= samples; s++)
            {
                var t = (double)s / samples;
                var cur = IsVisibleAt(a, b, depthA, depthB, t);
                if (cur != prev)
                {
                    Emit(a, b, startT, t, prev, output);
                    startT = t;
                    prev = cur;
                }
            }

            Emit(a, b, startT, 1.0, prev, output);
        }

        private bool IsVisibleAt(Point2d a, Point2d b, double depthA, double depthB, double t)
        {
            var h = a.X + (b.X - a.X) * t;
            var v = a.Y + (b.Y - a.Y) * t;
            var d = depthA + (depthB - depthA) * t;

            var px = (int)((h - _minH) / _cell);
            var py = (int)((v - _minV) / _cell);
            if (px < 0 || py < 0 || px >= _w || py >= _h) return true;

            var nearest = _depth[py * _w + px];
            if (double.IsPositiveInfinity(nearest)) return true;
            return d <= nearest + _bias;
        }

        private static void Emit(Point2d a, Point2d b, double t0, double t1, bool visible,
            List<ProjectedEdge> output)
        {
            if (t1 - t0 < 1e-9) return;
            var p0 = new Point2d(a.X + (b.X - a.X) * t0, a.Y + (b.Y - a.Y) * t0);
            var p1 = new Point2d(a.X + (b.X - a.X) * t1, a.Y + (b.Y - a.Y) * t1);
            output.Add(new ProjectedEdge(p0, p1, visible));
        }
    }
}
