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
            try
            {
                var pluginDir = Path.GetDirectoryName(
                    Assembly.GetExecutingAssembly().Location);
                var logDir = Path.Combine(pluginDir, "logs");
                Directory.CreateDirectory(logDir);

                GlobalContext.Properties["LogPath"] = pluginDir;

                var configPath = Path.Combine(pluginDir, "log4net.config");
                if (File.Exists(configPath))
                    XmlConfigurator.Configure(new FileInfo(configPath));
                else
                    BasicConfigurator.Configure();
            }
            catch
            {
                /* don't let logging init crash the plugin */
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