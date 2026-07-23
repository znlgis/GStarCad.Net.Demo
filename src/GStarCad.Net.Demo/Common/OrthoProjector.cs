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

    /// <summary>
    /// Converts a triangulated mesh (STL) into clean 2D orthographic engineering views.
    ///
    /// Pipeline per view:
    ///   1. Feature-edge extraction — only silhouette, sharp-crease and boundary edges are
    ///      kept, so the dense tessellation lines on curved faces (cylinders, fillets) are
    ///      discarded and the drawing stays clean.
    ///   2. Z-buffer hidden-line removal — front-facing triangles are rasterized into a depth
    ///      buffer; every candidate edge is sampled along its length and split into
    ///      visible / hidden segments by comparing depth against the buffer.
    ///
    /// The three views follow the Chinese national standard (first-angle projection):
    ///   Front (主视图, look +Y), Top (俯视图, look -Z), Left (左视图, look +X).
    /// </summary>
    public static class OrthoProjector
    {
        // Coordinate quantization (in model/drawing units) used to weld coincident STL
        // vertices into a shared 3D edge. STL stores each triangle independently, so shared
        // corners must be snapped together; 1e-6 is well below any real feature size while
        // still tolerating floating-point noise from the exporter.
        private const double WeldTolerance = 1e-6;

        private struct ViewDef
        {
            public string Name;
            public Vector3d Look;   // direction the camera looks along (depth axis, near = smaller depth)
            public Vector3d Right;  // maps 3D point to 2D horizontal
            public Vector3d Up;     // maps 3D point to 2D vertical

            public ViewDef(string name, Vector3d look, Vector3d right, Vector3d up)
            {
                Name = name;
                Look = look;
                Right = right;
                Up = up;
            }
        }

        private static readonly ViewDef[] Views =
        {
            // Front view: camera on -Y looking toward +Y. H = X, V = Z.
            new ViewDef("Front", new Vector3d(0, 1, 0), new Vector3d(1, 0, 0), new Vector3d(0, 0, 1)),
            // Top view (first-angle, placed below front): camera above looking down. H = X, V = -Y.
            new ViewDef("Top", new Vector3d(0, 0, -1), new Vector3d(1, 0, 0), new Vector3d(0, -1, 0)),
            // Left view (first-angle, placed right of front): camera on -X looking toward +X. H = Y, V = Z.
            new ViewDef("Left", new Vector3d(1, 0, 0), new Vector3d(0, 1, 0), new Vector3d(0, 0, 1)),
        };

        public static List<ViewProjection> Project(List<StlTriangle> triangles)
        {
            var mesh = new MeshTopology(triangles, WeldTolerance);

            var results = new List<ViewProjection>(Views.Length);
            foreach (var view in Views)
                results.Add(ProjectView(mesh, view));

            return results;
        }

        private static ViewProjection ProjectView(MeshTopology mesh, ViewDef view)
        {
            var look = view.Look;

            // Classify each triangle as front-facing (its outward normal points toward the camera).
            var triFront = new bool[mesh.Triangles.Count];
            for (var i = 0; i < mesh.Triangles.Count; i++)
                triFront[i] = mesh.Normals[i].DotProduct(look) < 0; // negative dot => normal points toward camera (opposite to look)

            // Collect candidate feature edges for this view.
            var candidates = new List<MeshTopology.Edge>();
            foreach (var edge in mesh.Edges)
            {
                if (edge.TriB < 0)
                {
                    // Boundary edge (referenced by a single triangle).
                    candidates.Add(edge);
                    continue;
                }

                var frontA = triFront[edge.TriA];
                var frontB = triFront[edge.TriB];

                // Silhouette edge: one adjacent face toward camera, the other away.
                if (frontA != frontB)
                {
                    candidates.Add(edge);
                    continue;
                }

                // Only edges shared by at least one visible (front) face can appear.
                if (!frontA && !frontB) continue;

                // Sharp feature edge: dihedral angle between the two faces exceeds threshold.
                if (edge.IsSharp) candidates.Add(edge);
            }

            // Rasterize front-facing triangles into a depth buffer for hidden-line removal.
            var buffer = DepthBuffer.Build(mesh, triFront, view.Right, view.Up, view.Look);

            var edges = new List<ProjectedEdge>();
            foreach (var edge in candidates)
            {
                var a = mesh.Points[edge.P0];
                var b = mesh.Points[edge.P1];

                var a2 = new Point2d(a.DotProduct(view.Right), a.DotProduct(view.Up));
                var b2 = new Point2d(b.DotProduct(view.Right), b.DotProduct(view.Up));
                var da = a.DotProduct(view.Look);
                var db = b.DotProduct(view.Look);

                buffer.SplitEdge(a2, b2, da, db, edges);
            }

            return new ViewProjection { Name = view.Name, Edges = edges };
        }
    }

    internal static class Vector3dExtensions
    {
        public static double DotProduct(this Point3d p, Vector3d v)
        {
            return p.X * v.X + p.Y * v.Y + p.Z * v.Z;
        }
    }
}
