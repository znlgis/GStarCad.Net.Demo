# Task 6: OCCTTool/Program.cs — 修改调用新方法

**File to modify:** `tools/OCCTTool/Program.cs` (60 lines currently)

## Changes needed

Modify `Main` to detect input file extension and route accordingly:
- `.stl` input + `.csv` output → call `proxy.Generate2DViewsSTL(inputFile, outputFile)`
- All other extensions → call existing `proxy.Generate2DViews(inputFile, outputFile)` (legacy)

Also update usage message.

## Complete replacement code for Program.cs

```csharp
using System;
using System.IO;

namespace OCCTTool
{
    class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            if (args.Length < 2)
            {
                Console.Error.WriteLine("Usage: OCCTTool.exe <input.stl> <output.csv>");
                Console.Error.WriteLine("   or: OCCTTool.exe <input.stp> <output.stp>  (legacy STEP mode)");
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

                var ext = Path.GetExtension(inputFile).ToLowerInvariant();
                var outExt = Path.GetExtension(outputFile).ToLowerInvariant();

                bool result;

                if (ext == ".stl" && outExt == ".csv")
                {
                    result = proxy.Generate2DViewsSTL(inputFile, outputFile);
                }
                else
                {
                    // Legacy: STEP/BREP input -> STEP output
                    result = proxy.Generate2DViews(inputFile, outputFile);
                }

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
```

Note: Removed `using System.Reflection;` since it's no longer needed (wasn't used in the original either).

## Build Verification

```
dotnet build tools/OCCTTool/OCCTTool.csproj -c Release
```

## Global Constraints
- .NET Framework 4.8 (matches OCCTTool.csproj)
- No AI comments, no emoji
- Preserve OCCTProxy.Generate2DViews fallback for legacy .step/.stp files
