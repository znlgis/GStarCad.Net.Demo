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
                line.ColorIndex = 1;

                var circle = new Circle(new Point3d(50, 50, 0), Vector3d.ZAxis, 25);
                circle.ColorIndex = 3;

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
