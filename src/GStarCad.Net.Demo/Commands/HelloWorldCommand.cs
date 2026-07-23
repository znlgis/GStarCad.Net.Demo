using GrxCAD.ApplicationServices;
using GrxCAD.Runtime;

namespace GStarCad.Net.Demo.Commands
{
    public class HelloWorldCommand
    {
        [CommandMethod("HELLO")]
        public void Hello()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var ed = doc.Editor;

            ed.WriteMessage("\nHello, GStarCAD! 欢迎使用浩辰CAD插件开发.");
            Application.ShowAlertDialog("Hello, GStarCAD!\n欢迎使用浩辰CAD 2022插件演示.");
        }
    }
}