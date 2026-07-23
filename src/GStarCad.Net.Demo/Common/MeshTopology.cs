using System;
using System.Collections.Generic;
using GrxCAD.Geometry;

namespace GStarCad.Net.Demo.Common
{
    /// <summary>
    /// Builds welded vertex/edge topology for a triangle soup (STL) so that feature edges can
    /// be extracted. Coincident vertices are merged on a quantization grid, and every undirected
    /// edge records the (up to two) triangles that share it together with a sharp-crease flag.
    /// </summary>
    internal sealed class MeshTopology
    {
        public struct Edge
        {
            public int P0;      // welded point index (P0 < P1)
            public int P1;
            public int TriA;    // first adjacent triangle
            public int TriB;    // second adjacent triangle, or -1 for a boundary edge
            public bool IsSharp; // dihedral angle between TriA and TriB exceeds threshold
        }

        public readonly List<StlTriangle> Triangles;
        public readonly List<Point3d> Points = new List<Point3d>();
        public readonly List<Vector3d> Normals = new List<Vector3d>();
        public readonly List<Edge> Edges = new List<Edge>();

        // Per-triangle welded point indices.
        private readonly int[][] _triPoints;

        public MeshTopology(List<StlTriangle> triangles, double weldTolerance)
        {
            Triangles = triangles;
            var inv = weldTolerance > 0 ? 1.0 / weldTolerance : 1e6;

            var vertexMap = new Dictionary<long, int>(triangles.Count * 3);
            _triPoints = new int[triangles.Count][];

            for (var i = 0; i < triangles.Count; i++)
            {
                var tri = triangles[i];
                _triPoints[i] = new[]
                {
                    WeldVertex(tri.V1, inv, vertexMap),
                    WeldVertex(tri.V2, inv, vertexMap),
                    WeldVertex(tri.V3, inv, vertexMap)
                };
                Normals.Add(ComputeNormal(tri));
            }

            BuildEdges();
        }

        private int WeldVertex(Point3d p, double inv, Dictionary<long, int> map)
        {
            // Hash on a coarse grid; exact grid-cell collisions weld vertices together.
            long qx = (long)Math.Round(p.X * inv);
            long qy = (long)Math.Round(p.Y * inv);
            long qz = (long)Math.Round(p.Z * inv);
            unchecked
            {
                long key = qx * 73856093L ^ qy * 19349663L ^ qz * 83492791L;
                if (map.TryGetValue(key, out var idx)) return idx;
                idx = Points.Count;
                Points.Add(p);
                map[key] = idx;
                return idx;
            }
        }

        private static Vector3d ComputeNormal(StlTriangle tri)
        {
            var u = tri.V2 - tri.V1;
            var v = tri.V3 - tri.V1;
            var n = u.CrossProduct(v);
            var len = n.Length;
            if (len < 1e-12)
            {
                // Degenerate triangle — fall back to the stored facet normal if usable.
                return tri.Normal.Length > 1e-9 ? tri.Normal.GetNormal() : new Vector3d(0, 0, 1);
            }
            return n / len;
        }

        private void BuildEdges()
        {
            var cosSharp = Math.Cos(OrthoConstants.SharpAngleRad);
            var edgeMap = new Dictionary<long, int>(Triangles.Count * 3);

            for (var t = 0; t < _triPoints.Length; t++)
            {
                var pts = _triPoints[t];
                for (var e = 0; e < 3; e++)
                {
                    var a = pts[e];
                    var b = pts[(e + 1) % 3];
                    if (a == b) continue; // degenerate

                    var p0 = Math.Min(a, b);
                    var p1 = Math.Max(a, b);
                    long key = ((long)p0 << 32) | (uint)p1;

                    if (edgeMap.TryGetValue(key, out var existing))
                    {
                        var edge = Edges[existing];
                        if (edge.TriB < 0)
                        {
                            edge.TriB = t;
                            var dot = Normals[edge.TriA].DotProduct(Normals[t]);
                            edge.IsSharp = dot < cosSharp;
                            Edges[existing] = edge;
                        }
                    }
                    else
                    {
                        edgeMap[key] = Edges.Count;
                        Edges.Add(new Edge { P0 = p0, P1 = p1, TriA = t, TriB = -1, IsSharp = false });
                    }
                }
            }
        }
    }

    internal static class OrthoConstants
    {
        // Kept in one place so MeshTopology and OrthoProjector agree on the sharp threshold.
        public const double SharpAngleDeg = 25.0;
        public static readonly double SharpAngleRad = SharpAngleDeg * Math.PI / 180.0;
    }
}
