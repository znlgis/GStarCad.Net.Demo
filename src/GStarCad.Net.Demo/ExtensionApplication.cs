using System;
using System.IO;
using System.Reflection;
using GrxCAD.Runtime;
using log4net;
using log4net.Config;

namespace GStarCad.Net.Demo
{
    public class ExtensionApplication : IExtensionApplication
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(ExtensionApplication));

        public void Initialize()
        {
            var configPath = Path.Combine(
                Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),
                "log4net.config");
            if (File.Exists(configPath))
            {
                XmlConfigurator.Configure(new FileInfo(configPath));
            }
            else
            {
                BasicConfigurator.Configure();
            }
            Log.Info("=== GStarCad.Net.Demo plugin initialized ===");
        }

        public void Terminate()
        {
            Log.Info("=== GStarCad.Net.Demo plugin terminated ===");
            LogManager.Shutdown();
        }
    }
}
