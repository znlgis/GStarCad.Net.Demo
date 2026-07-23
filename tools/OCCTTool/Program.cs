using System;
using System.IO;
using System.Reflection;

namespace OCCTTool
{
    class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            if (args.Length < 2)
            {
                Console.Error.WriteLine("Usage: OCCTTool.exe <input.step> <output.step>");
                return 1;
            }

            var inputFile = args[0];
            var outputFile = args[1];

            if (!File.Exists(inputFile))
            {
                Console.Error.WriteLine("Input file not found: " + inputFile);
                return 2;
            }

            // Ensure output directory exists
            var outputDir = Path.GetDirectoryName(outputFile);
            if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
                Directory.CreateDirectory(outputDir);

            // Set native DLL search path for OCCT DLLs
            var occtBin = @"D:\self\code\OCCT\build\win64\vc14\bin";
            Environment.SetEnvironmentVariable("PATH",
                occtBin + ";" + Environment.GetEnvironmentVariable("PATH"));

            try
            {
                var proxy = new OCCTProxy();
                var result = proxy.Generate2DViews(inputFile, outputFile);

                if (result)
                {
                    Console.WriteLine("OK: " + outputFile);
                    return 0;
                }
                else
                {
                    Console.Error.WriteLine("Generate2DViews returned false");
                    return 3;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("ERROR: " + ex.Message);
                return 4;
            }
        }
    }
}
