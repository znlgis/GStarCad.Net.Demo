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
                ent.ColorIndex = 1;
                ent.Layer = "0";

                tr.Commit();
            }

            ed.WriteMessage("\n实体已修改: 颜色改为红色, 图层改为0.");
        }
    }
}
