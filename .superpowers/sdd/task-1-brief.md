# Task 1: StlParser — STL 格式解析器

**Files:**
- Create: `src/GStarCad.Net.Demo/Common/StlParser.cs`

**Interfaces:**
- Consumes: 无外部依赖（仅 System.IO, System.Collections.Generic）
- Produces: `StlTriangle` struct, `StlParser.Parse(string filePath)` → `List<StlTriangle>`

### Step 1: 创建 StlParser.cs

完整实现代码：

```csharp
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using GrxCAD.Geometry;

namespace GStarCad.Net.Demo.Common
{
    public struct StlTriangle
    {
        public Vector3d Normal;
        public Point3d V1, V2, V3;

        public Point3d this[int index]
        {
            get
            {
                switch (index)
                {
                    case 0: return V1;
                    case 1: return V2;
                    case 2: return V3;
                    default: throw new IndexOutOfRangeException();
                }
            }
        }
    }

    public static class StlParser
    {
        public static List<StlTriangle> Parse(string filePath)
        {
            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
            using (var br = new BinaryReader(fs))
            {
                var header = new string(Encoding.ASCII.GetChars(br.ReadBytes(5)), 0, 5);
                br.BaseStream.Seek(0, SeekOrigin.Begin);

                if (header.StartsWith("solid", StringComparison.OrdinalIgnoreCase))
                    return ParseAscii(br);
                else
                    return ParseBinary(br);
            }
        }

        private static List<StlTriangle> ParseAscii(BinaryReader br)
        {
            var triangles = new List<StlTriangle>();
            var reader = new StreamReader(br.BaseStream, Encoding.ASCII);

            StlTriangle current = default;
            var vertexCount = 0;
            var inFacet = false;

            string line;
            while ((line = reader.ReadLine()) != null)
            {
                line = line.Trim();
                if (line.Length == 0) continue;

                var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

                if (parts.Length >= 4 && parts[0] == "facet" && parts[1] == "normal")
                {
                    inFacet = true;
                    vertexCount = 0;
                    current = new StlTriangle
                    {
                        Normal = new Vector3d(
                            ParseDouble(parts[2]),
                            ParseDouble(parts[3]),
                            ParseDouble(parts[4]))
                    };
                }
                else if (inFacet && parts.Length >= 3 && parts[0] == "vertex")
                {
                    var pt = new Point3d(
                        ParseDouble(parts[1]),
                        ParseDouble(parts[2]),
                        ParseDouble(parts[3]));

                    switch (vertexCount)
                    {
                        case 0: current.V1 = pt; break;
                        case 1: current.V2 = pt; break;
                        case 2: current.V3 = pt; break;
                    }
                    vertexCount++;
                }
                else if (inFacet && parts.Length >= 1 && parts[0] == "endfacet")
                {
                    triangles.Add(current);
                    inFacet = false;
                }
            }

            return triangles;
        }

        private static List<StlTriangle> ParseBinary(BinaryReader br)
        {
            br.BaseStream.Seek(80, SeekOrigin.Begin);
            var count = br.ReadUInt32();
            var triangles = new List<StlTriangle>((int)count);

            for (uint i = 0; i < count; i++)
            {
                var tri = new StlTriangle
                {
                    Normal = new Vector3d(br.ReadSingle(), br.ReadSingle(), br.ReadSingle()),
                    V1 = new Point3d(br.ReadSingle(), br.ReadSingle(), br.ReadSingle()),
                    V2 = new Point3d(br.ReadSingle(), br.ReadSingle(), br.ReadSingle()),
                    V3 = new Point3d(br.ReadSingle(), br.ReadSingle(), br.ReadSingle())
                };
                br.ReadUInt16(); // attribute byte count
                triangles.Add(tri);
            }

            return triangles;
        }

        private static double ParseDouble(string s)
        {
            return double.Parse(s, CultureInfo.InvariantCulture);
        }
    }
}
```

### Step 2: 验证编译

构建解决方案，确认 StlParser.cs 无编译错误。
```
dotnet build src/GStarCad.Net.Demo/GStarCad.Net.Demo.csproj
```

## Global Constraints
- 目标框架：.NET Framework 4.8
- NuGet 依赖：仅 GStarCad.Net 20.22.0 + log4net 3.3.2
- 命名空间：GrxCAD.*（Runtime, ApplicationServices, DatabaseServices, EditorInput, Geometry）
- 无 AI 注释、无 emoji、无 catch-all 文件
- 代码使用 GrxCAD.Geometry 的 Vector3d / Point3d
