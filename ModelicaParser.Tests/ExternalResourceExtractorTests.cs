using ModelicaParser.DataTypes;
using ModelicaParser.Helpers;
using ModelicaParser.Visitors;

namespace ModelicaParser.Tests;

public class ExternalResourceExtractorTests
{
    private List<ExternalResourceInfo> ExtractResources(string modelicaCode)
    {
        var parseTree = ModelicaParserHelper.Parse(modelicaCode);
        var extractor = new ExternalResourceExtractor();
        extractor.Visit(parseTree);
        return extractor.Resources;
    }

    [Fact]
    public void ExtractLoadResource_SingleCall()
    {
        var code = @"
model TestModel
  parameter String fileName = Modelica.Utilities.Files.loadResource(""modelica://Modelica/Resources/Data/test.mat"");
end TestModel;";

        var resources = ExtractResources(code);

        Assert.Single(resources);
        Assert.Equal("modelica://Modelica/Resources/Data/test.mat", resources[0].RawPath);
        Assert.Equal(ResourceReferenceType.LoadResource, resources[0].ReferenceType);
        Assert.Null(resources[0].ParameterName);
    }

    [Fact]
    public void ExtractLoadResourceModification_SingleCall()
    {
        var code = @"
model TestModel
  extends BaseModel(fileName = Modelica.Utilities.Files.loadResource(""modelica://Modelica/Resources/Data/test.mat""));
end TestModel;";

        var resources = ExtractResources(code);

        Assert.Single(resources);
        Assert.Equal("modelica://Modelica/Resources/Data/test.mat", resources[0].RawPath);
        Assert.Equal(ResourceReferenceType.LoadResource, resources[0].ReferenceType);
        Assert.Null(resources[0].ParameterName);
    }

    [Fact]
    public void ExtractLoadResource_MultipleCallsSameModel()
    {
        var code = @"
model TestModel
  parameter String file1 = Modelica.Utilities.Files.loadResource(""modelica://MyLib/Resources/data1.mat"");
  parameter String file2 = Modelica.Utilities.Files.loadResource(""modelica://MyLib/Resources/data2.csv"");
end TestModel;";

        var resources = ExtractResources(code);

        Assert.Equal(2, resources.Count);
        Assert.Contains(resources, r => r.RawPath == "modelica://MyLib/Resources/data1.mat");
        Assert.Contains(resources, r => r.RawPath == "modelica://MyLib/Resources/data2.csv");
        Assert.All(resources, r => Assert.Equal(ResourceReferenceType.LoadResource, r.ReferenceType));
    }

    [Fact]
    public void ExtractModelicaUri_InAnnotationString()
    {
        var code = @"
model TestModel
  annotation(Documentation(info=""<html><p><img src=\""modelica://Modelica/Resources/Images/test.png\""></p></html>""));
end TestModel;";

        var resources = ExtractResources(code);

        Assert.Single(resources);
        Assert.Equal("modelica://Modelica/Resources/Images/test.png", resources[0].RawPath);
        Assert.Equal(ResourceReferenceType.UriReference, resources[0].ReferenceType);
    }

    [Fact]
    public void ExtractModelicaUri_SkipsModelReferences()
    {
        // modelica:// URIs without file extensions are model references, not resource files
        var code = @"
model TestModel
  annotation(Documentation(info=""<html><p>See <a href=\""modelica://Modelica.Blocks.Continuous\"">docs</a></p></html>""));
end TestModel;";

        var resources = ExtractResources(code);

        Assert.Empty(resources);
    }

    [Fact]
    public void ExtractModelicaUri_MultipleUrisInOneString()
    {
        var code = @"
model TestModel
  annotation(Documentation(info=""<html><img src=\""modelica://Lib/Resources/img1.png\""><img src=\""modelica://Lib/Resources/img2.svg\""></html>""));
end TestModel;";

        var resources = ExtractResources(code);

        Assert.Equal(2, resources.Count);
        Assert.Contains(resources, r => r.RawPath == "modelica://Lib/Resources/img1.png");
        Assert.Contains(resources, r => r.RawPath == "modelica://Lib/Resources/img2.svg");
    }

    [Fact]
    public void NoResources_PlainModel()
    {
        var code = @"
model TestModel
  Real x;
equation
  x = 1.0;
end TestModel;";

        var resources = ExtractResources(code);

        Assert.Empty(resources);
    }

    [Fact]
    public void ExtractLoadResource_WithConcatenation_NotSupported()
    {
        // loadResource with a single string argument (most common case)
        var code = @"
model TestModel
  parameter String fileName = Modelica.Utilities.Files.loadResource(""modelica://MyLib/Resources/"" + ""data.txt"");
end TestModel;";

        var resources = ExtractResources(code);

        Assert.Equal(2, resources.Count);
        Assert.Equal("modelica://MyLib/Resources/", resources[0].RawPath);
        Assert.Equal("data.txt", resources[1].RawPath);
    }

    [Fact]
    public void ExtractModelicaUri_VariousFileExtensions()
    {
        var code = @"
model TestModel
  annotation(Documentation(info=""<html>
    <img src=\""modelica://Lib/Resources/img.png\"">
    <a href=\""modelica://Lib/Resources/doc.pdf\"">PDF</a>
    <a href=\""modelica://Lib/Resources/data.mat\"">Data</a>
  </html>""));
end TestModel;";

        var resources = ExtractResources(code);

        Assert.Equal(3, resources.Count);
        Assert.Contains(resources, r => r.RawPath.EndsWith("img.png"));
        Assert.Contains(resources, r => r.RawPath.EndsWith("doc.pdf"));
        Assert.Contains(resources, r => r.RawPath.EndsWith("data.mat"));
    }

    [Fact]
    public void ExtractExternalAnnotation_IncludeDirectorySingleString()
    {
        var code = @"
function TestFunc
  external ""C""
    annotation(IncludeDirectory=""modelica://Modelica/Resources/C-Sources"");
end TestFunc;";

        var resources = ExtractResources(code);

        Assert.Single(resources);
        Assert.Equal("modelica://Modelica/Resources/C-Sources", resources[0].RawPath);
        Assert.Equal(ResourceReferenceType.ExternalIncludeDirectory, resources[0].ReferenceType);
    }

    [Fact]
    public void ExtractExternalAnnotationNoLanguageSpecifier_IncludeDirectorySingleString()
    {
        var code = @"
function TestFunc
  external annotation(IncludeDirectory=""modelica://Modelica/Resources/C-Sources"");
end TestFunc;";

        var resources = ExtractResources(code);

        Assert.Single(resources);
        Assert.Equal("modelica://Modelica/Resources/C-Sources", resources[0].RawPath);
        Assert.Equal(ResourceReferenceType.ExternalIncludeDirectory, resources[0].ReferenceType);
    }

    [Fact]
    public void ExtractExternalAnnotation_IncludeSingleString()
    {
        var code = @"
function TestFunc
  external ""C""
    annotation(Include=""#include \""ModelicaStandardTables.h\"""");
end TestFunc;";

        var resources = ExtractResources(code);

        Assert.Single(resources);
        // ANTLR preserves escaped quotes as \" in the token text
        Assert.Equal("#include \\\"ModelicaStandardTables.h\\\"", resources[0].RawPath);
        Assert.Equal(ResourceReferenceType.ExternalInclude, resources[0].ReferenceType);
    }

    [Fact]
    public void ExtractExternalAnnotation_LibrarySingleString()
    {
        var code = @"
function TestFunc
  external ""C""
    annotation(Library=""ModelicaStandardTables"");
end TestFunc;";

        var resources = ExtractResources(code);

        Assert.Single(resources);
        Assert.Equal("ModelicaStandardTables", resources[0].RawPath);
        Assert.Equal(ResourceReferenceType.ExternalLibrary, resources[0].ReferenceType);
    }

    [Fact]
    public void ExtractExternalAnnotation_LibraryArray()
    {
        var code = @"
function TestFunc
  external ""C""
    annotation(Library={""ModelicaStandardTables"", ""ModelicaIO"", ""ModelicaMatIO"", ""zlib""});
end TestFunc;";

        var resources = ExtractResources(code);

        Assert.Equal(4, resources.Count);
        Assert.All(resources, r => Assert.Equal(ResourceReferenceType.ExternalLibrary, r.ReferenceType));
        Assert.Contains(resources, r => r.RawPath == "ModelicaStandardTables");
        Assert.Contains(resources, r => r.RawPath == "ModelicaIO");
        Assert.Contains(resources, r => r.RawPath == "ModelicaMatIO");
        Assert.Contains(resources, r => r.RawPath == "zlib");
    }

    [Fact]
    public void ExtractExternalAnnotation_LibraryDirectorySingleString()
    {
        var code = @"
function TestFunc
  external ""C""
    annotation(LibraryDirectory=""modelica://Modelica/Resources/Library"");
end TestFunc;";

        var resources = ExtractResources(code);

        Assert.Single(resources);
        Assert.Equal("modelica://Modelica/Resources/Library", resources[0].RawPath);
        Assert.Equal(ResourceReferenceType.ExternalLibraryDirectory, resources[0].ReferenceType);
    }

    [Fact]
    public void ExtractExternalAnnotation_MultipleKeys()
    {
        var code = @"
function TestFunc
  external ""C"" der_y = ModelicaStandardTables_CombiTimeTable_getDerValue(tableID, icol, timeIn, nextTimeEvent, pre_nextTimeEvent, der_timeIn)
    annotation(
      IncludeDirectory=""modelica://Modelica/Resources/C-Sources"",
      Include=""#include \""ModelicaStandardTables.h\"""",
      Library={""ModelicaStandardTables"", ""ModelicaIO"", ""ModelicaMatIO"", ""zlib""}
    );
end TestFunc;";

        var resources = ExtractResources(code);

        Assert.Equal(6, resources.Count);

        // IncludeDirectory
        Assert.Single(resources, r => r.ReferenceType == ResourceReferenceType.ExternalIncludeDirectory);
        Assert.Contains(resources, r => r.RawPath == "modelica://Modelica/Resources/C-Sources"
                                     && r.ReferenceType == ResourceReferenceType.ExternalIncludeDirectory);

        // Include
        Assert.Single(resources, r => r.ReferenceType == ResourceReferenceType.ExternalInclude);

        // Library (4 items)
        Assert.Equal(4, resources.Count(r => r.ReferenceType == ResourceReferenceType.ExternalLibrary));
    }

    [Fact]
    public void ExtractExternalAnnotation_NoAnnotation_NoResources()
    {
        var code = @"
function TestFunc
  input Real x;
  output Real y;
  external ""C"" y = testfunc(x);
end TestFunc;";

        var resources = ExtractResources(code);

        Assert.Empty(resources);
    }

    [Fact]
    public void ExtractExternalAnnotation_UnrelatedAnnotationKeys_Ignored()
    {
        var code = @"
function TestFunc
  external ""C""
    annotation(version=""1.0"", Documentation(info=""<html>test</html>""));
end TestFunc;";

        var resources = ExtractResources(code);

        Assert.Empty(resources);
    }

    [Fact]
    public void ExtractExternalAnnotation_WithClassAnnotation()
    {
        // Ensure class-level annotation is not confused with external annotation
        var code = @"
function TestFunc
  external ""C""
    annotation(Library=""mylib"");
  annotation(Documentation(info=""<html><img src=\""modelica://Lib/Resources/icon.png\""></html>""));
end TestFunc;";

        var resources = ExtractResources(code);

        Assert.Equal(2, resources.Count);
        Assert.Contains(resources, r => r.RawPath == "mylib"
                                     && r.ReferenceType == ResourceReferenceType.ExternalLibrary);
        Assert.Contains(resources, r => r.RawPath == "modelica://Lib/Resources/icon.png"
                                     && r.ReferenceType == ResourceReferenceType.UriReference);
    }

    [Fact]
    public void ExtractExternalAnnotation_WithDocumentationModelicaLink()
    {
        // Ensure class-level annotation is not confused with external annotation
        var code = @"
function TestFunc
  external ""C""
    annotation(Library=""mylib"");
  annotation(Documentation(info=""<html><a href=\""modelica://Buildings.Air.Systems.SingleZone.VAV.Example\"">Link</a></html>""));
end TestFunc;";

        var resources = ExtractResources(code);

        Assert.Single(resources);
        Assert.Contains(resources, r => r.RawPath == "mylib"
                                     && r.ReferenceType == ResourceReferenceType.ExternalLibrary);
    }

    [Fact]
    public void ExtractExternalAnnotation_IncludeDirectoryArray()
    {
        var code = @"
function TestFunc
  external ""C""
    annotation(IncludeDirectory={""modelica://Modelica/Resources/C-Sources"", ""modelica://Modelica/Resources/C-Sources/extra""});
end TestFunc;";

        var resources = ExtractResources(code);

        Assert.Equal(2, resources.Count);
        Assert.All(resources, r => Assert.Equal(ResourceReferenceType.ExternalIncludeDirectory, r.ReferenceType));
    }

    [Fact]
    public void ExtractExternalAnnotation_SourceDirectorySingleString()
    {
        var code = @"
function TestFunc
  external ""C""
    annotation(SourceDirectory=""modelica://Modelica/Resources/Source"");
end TestFunc;";

        var resources = ExtractResources(code);

        Assert.Single(resources);
        Assert.Equal("modelica://Modelica/Resources/Source", resources[0].RawPath);
        Assert.Equal(ResourceReferenceType.ExternalSourceDirectory, resources[0].ReferenceType);
    }

    [Fact]
    public void ExtractExternalAnnotation_SourceDirectoryArray()
    {
        var code = @"
function TestFunc
  external ""C""
    annotation(SourceDirectory={""modelica://Modelica/Resources/Source"", ""modelica://Modelica/Resources/Source/extra""});
end TestFunc;";

        var resources = ExtractResources(code);

        Assert.Equal(2, resources.Count);
        Assert.All(resources, r => Assert.Equal(ResourceReferenceType.ExternalSourceDirectory, r.ReferenceType));
        Assert.Contains(resources, r => r.RawPath == "modelica://Modelica/Resources/Source");
        Assert.Contains(resources, r => r.RawPath == "modelica://Modelica/Resources/Source/extra");
    }

    [Fact]
    public void ExtractExternalAnnotation_AllDirectoryTypes()
    {
        var code = @"
function TestFunc
  external ""C""
    annotation(
      IncludeDirectory=""modelica://Lib/Resources/Include"",
      LibraryDirectory=""modelica://Lib/Resources/Library"",
      SourceDirectory=""modelica://Lib/Resources/Source""
    );
end TestFunc;";

        var resources = ExtractResources(code);

        Assert.Equal(3, resources.Count);
        Assert.Contains(resources, r => r.RawPath == "modelica://Lib/Resources/Include"
                                     && r.ReferenceType == ResourceReferenceType.ExternalIncludeDirectory);
        Assert.Contains(resources, r => r.RawPath == "modelica://Lib/Resources/Library"
                                     && r.ReferenceType == ResourceReferenceType.ExternalLibraryDirectory);
        Assert.Contains(resources, r => r.RawPath == "modelica://Lib/Resources/Source"
                                     && r.ReferenceType == ResourceReferenceType.ExternalSourceDirectory);
    }

    [Fact]
    public void ExtractLoadResource_InEquation()
    {
        var code = @"
model TestModel
  String path;
equation
  path = Modelica.Utilities.Files.loadResource(""modelica://MyLib/Resources/config.txt"");
end TestModel;";

        var resources = ExtractResources(code);

        Assert.Single(resources);
        Assert.Equal("modelica://MyLib/Resources/config.txt", resources[0].RawPath);
        Assert.Equal(ResourceReferenceType.LoadResource, resources[0].ReferenceType);
    }

    [Fact]
    public void ExtractLoadResource_InAlgorithm()
    {
        var code = @"
model TestModel
  String path;
algorithm
  path := Modelica.Utilities.Files.loadResource(""modelica://MyLib/Resources/script.txt"");
end TestModel;";

        var resources = ExtractResources(code);

        Assert.Single(resources);
        Assert.Equal("modelica://MyLib/Resources/script.txt", resources[0].RawPath);
    }

    [Fact]
    public void ExtractLoadResource_InFunctionArgument()
    {
        var code = @"
model TestModel
  Real data[:] = Modelica.Blocks.Tables.CombiTable1Ds(
    fileName=Modelica.Utilities.Files.loadResource(""modelica://MyLib/Resources/data.txt""));
end TestModel;";

        var resources = ExtractResources(code);

        Assert.Single(resources);
        Assert.Equal("modelica://MyLib/Resources/data.txt", resources[0].RawPath);
        Assert.Equal(ResourceReferenceType.LoadResource, resources[0].ReferenceType);
    }

    [Fact]
    public void ExtractLoadResource_WithAbsolutePath()
    {
        var code = @"
model TestModel
  parameter String fileName = Modelica.Utilities.Files.loadResource(""C:/Data/test.mat"");
end TestModel;";

        var resources = ExtractResources(code);

        Assert.Single(resources);
        Assert.Equal("C:/Data/test.mat", resources[0].RawPath);
        Assert.Equal(ResourceReferenceType.LoadResource, resources[0].ReferenceType);
    }

    [Fact]
    public void ExtractLoadResource_WithEmptyString_NoResource()
    {
        var code = @"
model TestModel
  parameter String fileName = Modelica.Utilities.Files.loadResource("""");
end TestModel;";

        var resources = ExtractResources(code);

        // Empty strings should be filtered out
        Assert.Empty(resources);
    }

    [Fact]
    public void ExtractModelicaUri_InBitmapAnnotation()
    {
        var code = @"
model TestModel
  annotation(Icon(graphics={
    Bitmap(extent={{-100,-100},{100,100}}, fileName=""modelica://MyLib/Resources/Images/icon.png"")
  }));
end TestModel;";

        var resources = ExtractResources(code);

        Assert.Single(resources);
        Assert.Equal("modelica://MyLib/Resources/Images/icon.png", resources[0].RawPath);
        Assert.Equal(ResourceReferenceType.UriReference, resources[0].ReferenceType);
    }

    [Fact]
    public void ExtractModelicaUri_WithQueryParameters_Excluded()
    {
        // Model references with query-like parameters should still be excluded
        var code = @"
model TestModel
  annotation(Documentation(info=""<html><a href=\""modelica://Modelica.Blocks.Sources\"">Link</a></html>""));
end TestModel;";

        var resources = ExtractResources(code);

        Assert.Empty(resources);
    }

    [Fact]
    public void ExtractModelicaUri_VariousImageFormats()
    {
        var code = @"
model TestModel
  annotation(Documentation(info=""<html>
    <img src=\""modelica://Lib/Resources/Images/photo.jpg\"">
    <img src=\""modelica://Lib/Resources/Images/icon.gif\"">
    <img src=\""modelica://Lib/Resources/Images/diagram.bmp\"">
    <img src=\""modelica://Lib/Resources/Images/vector.svg\"">
  </html>""));
end TestModel;";

        var resources = ExtractResources(code);

        Assert.Equal(4, resources.Count);
        Assert.Contains(resources, r => r.RawPath.EndsWith("photo.jpg"));
        Assert.Contains(resources, r => r.RawPath.EndsWith("icon.gif"));
        Assert.Contains(resources, r => r.RawPath.EndsWith("diagram.bmp"));
        Assert.Contains(resources, r => r.RawPath.EndsWith("vector.svg"));
    }

    [Fact]
    public void ExtractExternalAnnotation_WithFortranLanguage()
    {
        var code = @"
function TestFunc
  external ""FORTRAN 77""
    annotation(Library=""myfortranlib"");
end TestFunc;";

        var resources = ExtractResources(code);

        Assert.Single(resources);
        Assert.Equal("myfortranlib", resources[0].RawPath);
        Assert.Equal(ResourceReferenceType.ExternalLibrary, resources[0].ReferenceType);
    }

    [Fact]
    public void ExtractExternalAnnotation_IncludeWithMultipleDirectives()
    {
        var code = @"
function TestFunc
  external ""C""
    annotation(Include=""#include \""header1.h\""\n#include \""header2.h\"""");
end TestFunc;";

        var resources = ExtractResources(code);

        // The Include annotation value is stored as a single string containing all directives
        Assert.Single(resources);
        Assert.Equal(ResourceReferenceType.ExternalInclude, resources[0].ReferenceType);
    }

    [Fact]
    public void ExtractModelicaUri_CaseSensitivity()
    {
        var code = @"
model TestModel
  annotation(Documentation(info=""<html>
    <img src=\""Modelica://Lib/Resources/test.PNG\"">
    <img src=\""MODELICA://Lib/Resources/test2.png\"">
  </html>""));
end TestModel;";

        var resources = ExtractResources(code);

        // modelica:// URI detection should be case-insensitive
        Assert.Equal(2, resources.Count);
    }

    [Fact]
    public void ExtractLoadResource_DuplicateResources_NotDeduplicated()
    {
        // The extractor should extract all occurrences, deduplication is a higher-level concern
        var code = @"
model TestModel
  parameter String file1 = Modelica.Utilities.Files.loadResource(""modelica://Lib/Resources/data.mat"");
  parameter String file2 = Modelica.Utilities.Files.loadResource(""modelica://Lib/Resources/data.mat"");
end TestModel;";

        var resources = ExtractResources(code);

        Assert.Equal(2, resources.Count);
        Assert.All(resources, r => Assert.Equal("modelica://Lib/Resources/data.mat", r.RawPath));
    }

    [Fact]
    public void ExtractModelicaUri_InTextAnnotation()
    {
        var code = @"
model TestModel
  annotation(Icon(graphics={
    Text(extent={{-100,-120},{100,-100}}, textString=""See modelica://Lib/Resources/docs/manual.pdf"")
  }));
end TestModel;";

        var resources = ExtractResources(code);

        Assert.Single(resources);
        Assert.Equal("modelica://Lib/Resources/docs/manual.pdf", resources[0].RawPath);
    }

    [Fact]
    public void ExtractExternalAnnotation_RelativeLibraryPath()
    {
        var code = @"
function TestFunc
  external ""C""
    annotation(Library=""../lib/mylib"");
end TestFunc;";

        var resources = ExtractResources(code);

        Assert.Single(resources);
        Assert.Equal("../lib/mylib", resources[0].RawPath);
        Assert.Equal(ResourceReferenceType.ExternalLibrary, resources[0].ReferenceType);
    }

    [Fact]
    public void ExtractLoadResource_ModelicaServicesVersion()
    {
        // Some users use ModelicaServices.ExternalReferences.loadResource instead of
        // Modelica.Utilities.Files.loadResource - both should be detected
        var code = @"
model TestModel
  parameter String fileName = ModelicaServices.ExternalReferences.loadResource(""modelica://Modelica/Resources/Data/test.mat"");
end TestModel;";

        var resources = ExtractResources(code);

        Assert.Single(resources);
        Assert.Equal("modelica://Modelica/Resources/Data/test.mat", resources[0].RawPath);
        Assert.Equal(ResourceReferenceType.LoadResource, resources[0].ReferenceType);
    }

    [Fact]
    public void ExtractLoadResource_PropagatedParameter_SingleResource()
    {
        // When a parameter uses loadResource() and is propagated to a component's parameter
        // via a variable reference (not a loadResource call), we should only detect one resource.
        // The inner component's parameter has no default value and no loadSelector annotation.
        var code = @"
model OuterModel
  parameter String dataFile = ModelicaServices.ExternalReferences.loadResource(
    ""modelica://MyLib/Resources/Data/experiment.mat"");
  InnerModel inner(fileName=dataFile);
end OuterModel;";

        var resources = ExtractResources(code);

        // Should only detect the single loadResource call in the parameter declaration
        // The modification fileName=dataFile is just a variable reference, not a resource
        Assert.Single(resources);
        Assert.Equal("modelica://MyLib/Resources/Data/experiment.mat", resources[0].RawPath);
        Assert.Equal(ResourceReferenceType.LoadResource, resources[0].ReferenceType);
    }

    [Fact]
    public void ExtractLoadResource_LinkedParameter_SingleResource()
    {
        // When a parameter uses loadResource() and is propagated to a component's parameter
        // via a variable reference (not a loadResource call), we should only detect one resource.
        // The inner component's parameter has no default value and no loadSelector annotation.
        var code = @"
package Tests
  model OuterModel
    Data data;
    InnerModel inner(fileName=data.dataFile);
  end OuterModel;
  
  record Data
    parameter String dataFile = ModelicaServices.ExternalReferences.loadResource(
      ""modelica://MyLib/Resources/Data/experiment.mat"");
  end Data;
end Tests;";

        var resources = ExtractResources(code);

        // Should only detect the single loadResource call in the parameter declaration
        // The modification fileName=dataFile is just a variable reference, not a resource
        Assert.Single(resources);
        Assert.Equal("modelica://MyLib/Resources/Data/experiment.mat", resources[0].RawPath);
        Assert.Equal(ResourceReferenceType.LoadResource, resources[0].ReferenceType);
    }
}
