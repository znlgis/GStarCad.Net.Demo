# Task 5: OCCTProxy.cpp — 新增 Generate2DViewsSTL

**File to modify:** `D:\self\code\OCCT-samples-csharp\OCCTProxy\OCCTProxy.cpp` (1148 lines)

## Step 1: Add new includes

**Location 1:** After line 51 `#include <StlAPI_Writer.hxx>`, add:
```cpp
#include <StlAPI_Reader.hxx>
```

**Location 2:** After line 57 `#include <gp_Dir.hxx>`, add:
```cpp
#include <TopExp_Explorer.hxx>
#include <BRep_Tool.hxx>
#include <TopoDS_Edge.hxx>
#include <Geom_Curve.hxx>
#include <fstream>
```

## Step 2: Add new methods after Generate2DViews ends (after line 1072)

The `Generate2DViews` method ends at line 1072 (`  }`). Insert the following code block between line 1072 and the `TranslateModel` comment block (currently line 1074):

```cpp

  /// <summary>Write edge endpoints from HLR shape to CSV stream.</summary>
  static void WriteEdgesToCsv(const TopoDS_Shape& theShape, bool theIsVisible,
                               std::ofstream& theFile)
  {
    for (TopExp_Explorer anExp(theShape, TopAbs_EDGE); anExp.More(); anExp.Next())
    {
      const TopoDS_Edge& anEdge = TopoDS::Edge(anExp.Current());
      Standard_Real aFirst = 0.0, aLast = 0.0;
      Handle(Geom_Curve) aCurve = BRep_Tool::Curve(anEdge, aFirst, aLast);
      if (aCurve.IsNull())
        continue;

      gp_Pnt aP1 = aCurve->Value(aFirst);
      gp_Pnt aP2 = aCurve->Value(aLast);

      theFile << (theIsVisible ? "V" : "H") << ","
              << aP1.X() << "," << aP1.Y() << "," << aP1.Z() << ","
              << aP2.X() << "," << aP2.Y() << "," << aP2.Z() << "\n";
    }
  }

  /// <summary>
  ///Generate 2D orthographic projections from an STL mesh file.
  ///Outputs edge coordinates to a CSV file (V=visible, H=hidden).
  ///Each line: V|H, x1,y1,z1, x2,y2,z2
  /// </summary>
  bool Generate2DViewsSTL(System::String^ theInputStl, System::String^ theOutputCsv)
  {
    const TCollection_AsciiString aInputPath = toAsciiString(theInputStl);

    // 1. Read STL mesh
    TopoDS_Shape aShape;
    StlAPI_Reader aStlReader;
    aStlReader.Read(aShape, aInputPath.ToCString());
    if (aShape.IsNull())
      return false;

    // 2. Define 4 orthographic view directions
    const gp_Dir aViewDirs[4] = {
      gp_Dir(0.0, -1.0, 0.0),  // Front
      gp_Dir(0.0,  1.0, 0.0),  // Back
      gp_Dir(-1.0, 0.0, 0.0),  // Left
      gp_Dir(1.0,  0.0, 0.0)   // Right
    };

    // 3. Open output CSV
    const TCollection_AsciiString aOutputPath = toAsciiString(theOutputCsv);
    std::ofstream aCsvFile(aOutputPath.ToCString());
    if (!aCsvFile.is_open())
      return false;

    // 4. For each view direction, compute HLR and write edges
    for (int i = 0; i < 4; i++)
    {
      Handle(HLRBRep_Algo) aHLR = new HLRBRep_Algo();
      aHLR->Add(aShape);

      gp_Ax2 aCoordSystem(gp_Pnt(0.0, 0.0, 0.0), aViewDirs[i]);
      HLRAlgo_Projector aProjector(aCoordSystem);
      aHLR->Projector(aProjector);

      aHLR->Update();
      aHLR->Hide();

      HLRBRep_HLRToShape aHLRToShape(aHLR);

      // Write visible edges (V)
      const TopoDS_Shape& aVisible = aHLRToShape.VCompound();
      if (!aVisible.IsNull())
        WriteEdgesToCsv(aVisible, true, aCsvFile);

      // Write hidden edges (H)
      const TopoDS_Shape& aHidden = aHLRToShape.HCompound();
      if (!aHidden.IsNull())
        WriteEdgesToCsv(aHidden, false, aCsvFile);
    }

    aCsvFile.close();
    return true;
  }

```

## Step 3: Verify

Lines should be correctly aligned. The new methods go between line 1072 (`  }` closing Generate2DViews) and line 1074 (`  /// <summary>` TranslateModel comment). Ensure no blank line gets doubled at the join points.

The file ends at line 1148 with `};`. Do not modify anything after the insertion point.

## Build Verification

```bash
msbuild D:\self\code\OCCT-samples-csharp\OCCTProxy\OCCTProxy.vcxproj /p:Configuration=Release /p:Platform=x64
```

Note: Requires OCCT 7.x SDK with CSF_OCCTIncludePath and CSF_OCCTLibPath environment variables set.
If the build environment is not available, just make the edits and report what was changed — we'll verify build separately.

## Global Constraints
- OCCT 7.x C++/CLI
- Match existing code style (4-space indent, K&R braces)
- No AI comments in new code
- The class ends at `};` on line 1148 — do not modify that
