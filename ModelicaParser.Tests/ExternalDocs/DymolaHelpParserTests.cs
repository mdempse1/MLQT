using ModelicaParser.ExternalDocs;
using Xunit;

namespace ModelicaParser.Tests.ExternalDocs;

/// <summary>
/// Unit tests for <see cref="DymolaHelpParser"/> against hand-written fixtures covering each
/// marker and each malformed variant seen in real generated documentation.
/// </summary>
public class DymolaHelpParserTests
{
    private const string GeneratorHead =
        "<html><head><meta name=\"HTML-Generator\" content=\"Dymola\"></head><body>";

    private static string Page(string body) => GeneratorHead + body + "</body></html>";

    /// <summary>
    /// A page-owning package heading followed by one class heading — the shape of every real file.
    /// </summary>
    private static string TwoClassPage(string classHeading) => Page(
        "<h2><a name=\"Lib.Pack\"></a><a href=\"Lib.html#Lib\">Lib</a>.Pack</h2>" +
        "<p><span class=\"ModelicaDescription\">A package</span></p>" +
        classHeading);

    #region Generator detection

    [Fact]
    public void ParseFile_NotDymolaGenerated_ReturnsNoClasses()
    {
        var html = "<html><head><meta name=\"HTML-Generator\" content=\"Sphinx\"></head>" +
                   "<body><h2><a name=\"Lib.Pack\"></a>Pack</h2></body></html>";

        var result = DymolaHelpParser.ParseFile(html);

        Assert.Empty(result.Classes);
    }

    [Fact]
    public void ParseFile_EmptyInput_ReturnsNoClasses()
    {
        Assert.Empty(DymolaHelpParser.ParseFile(string.Empty).Classes);
    }

    #endregion

    #region Names, descriptions, icons

    [Fact]
    public void ParseFile_ReadsFullNameFromAnchor()
    {
        var result = DymolaHelpParser.ParseFile(TwoClassPage(
            "<h2><a name=\"Lib.Pack.Thing\"></a><a href=\"x.html#Lib.Pack\">Lib.Pack</a>.Thing</h2>"));

        Assert.Equal(["Lib.Pack", "Lib.Pack.Thing"], result.Classes.Select(c => c.FullName));
    }

    [Fact]
    public void ParseFile_DecodesEntitiesInDescription()
    {
        var result = DymolaHelpParser.ParseFile(TwoClassPage(
            "<h2><a name=\"Lib.Pack.Thing\"></a>Thing</h2>" +
            "<p><span class=\"ModelicaDescription\">&#39;input Real&#39; as connector</span></p>"));

        Assert.Equal("'input Real' as connector", result.Classes[1].Description);
    }

    [Fact]
    public void ParseFile_HeadingImage_MarksClassAsHavingIcon()
    {
        var result = DymolaHelpParser.ParseFile(TwoClassPage(
            "<h2><img src=\"Lib.Pack.Th4fc66a467a6f2f69ingI.png\" alt=\"Lib.Pack.Thing\" width=\"80\">" +
            "<a name=\"Lib.Pack.Thing\"></a>Thing</h2>"));

        var thing = result.Classes.Single(c => c.FullName == "Lib.Pack.Thing");
        Assert.True(thing.HasIcon);
        Assert.Equal("Lib.Pack.Th4fc66a467a6f2f69ingI.png", thing.IconImagePath);
    }

    [Fact]
    public void ParseFile_NoHeadingImageOnNonPackage_MeansNoIcon()
    {
        var result = DymolaHelpParser.ParseFile(TwoClassPage(
            "<h2><a name=\"Lib.Pack.Thing\"></a>Thing</h2>"));

        Assert.False(result.Classes.Single(c => c.FullName == "Lib.Pack.Thing").HasIcon);
    }

    [Fact]
    public void ParseFile_PageOwningPackage_IconIsUnknownNotAbsent()
    {
        // The generator never draws an icon on the heading of the class that owns the page, so
        // its absence there says nothing — reporting "no icon" would be a fabricated finding.
        var result = DymolaHelpParser.ParseFile(TwoClassPage(
            "<h2><a name=\"Lib.Pack.Thing\"></a>Thing</h2>"));

        Assert.Null(result.Classes.Single(c => c.FullName == "Lib.Pack").HasIcon);
    }

    [Fact]
    public void ParseFile_HeadingImageForADifferentClass_IsIgnored()
    {
        // Icons are deduplicated behind mangled names, so the file name is meaningless; only the
        // alt attribute states which class an image belongs to.
        var result = DymolaHelpParser.ParseFile(TwoClassPage(
            "<h2><img src=\"Other.png\" alt=\"Lib.Pack.Other\"><a name=\"Lib.Pack.Thing\"></a>Thing</h2>"));

        Assert.False(result.Classes.Single(c => c.FullName == "Lib.Pack.Thing").HasIcon);
    }

    #endregion

    #region Base classes

    [Fact]
    public void ParseFile_NoBaseClassSpan_LeavesExtendsUnknown()
    {
        var result = DymolaHelpParser.ParseFile(TwoClassPage(
            "<h2><a name=\"Lib.Pack.Thing\"></a>Thing</h2>"));

        Assert.Null(result.Classes.Single(c => c.FullName == "Lib.Pack.Thing").ExtendsClasses);
    }

    [Fact]
    public void ParseFile_LinkedBaseClass_UsesHrefFragment()
    {
        var result = DymolaHelpParser.ParseFile(TwoClassPage(
            "<h2><a name=\"Lib.Pack.Thing\"></a>Thing</h2>" +
            "<p><span class=\"ModelicaBaseClass\">Extends from <a href=\"Lib_Base.html#Lib.Base.Root\"\n" +
            ">Lib.Base.Root</a> (A root).</span></p>"));

        Assert.Equal(["Lib.Base.Root"], result.Classes.Single(c => c.FullName == "Lib.Pack.Thing").ExtendsClasses);
    }

    [Fact]
    public void ParseFile_CrossLibraryRelativeHref_StillYieldsQualifiedName()
    {
        var result = DymolaHelpParser.ParseFile(TwoClassPage(
            "<h2><a name=\"Lib.Pack.Thing\"></a>Thing</h2>" +
            "<p><span class=\"ModelicaBaseClass\">Extends from " +
            "<a href=\"../../VehicleInterfaces%202.0.2/help/VehicleInterfaces.html#VehicleInterfaces.Chassis\"" +
            ">VehicleInterfaces.Chassis</a> (Chassis).</span></p>"));

        Assert.Equal(["VehicleInterfaces.Chassis"],
            result.Classes.Single(c => c.FullName == "Lib.Pack.Thing").ExtendsClasses);
    }

    [Fact]
    public void ParseFile_UnlinkedBaseClass_ReadsQualifiedNameFromText()
    {
        // A base class in a library outside the generated doc set is emitted as plain text.
        var result = DymolaHelpParser.ParseFile(TwoClassPage(
            "<h2><a name=\"Lib.Pack.Thing\"></a>Thing</h2>" +
            "<p><span class=\"ModelicaBaseClass\">Extends from " +
            "DymolaModels.Icons.Templates.Box_Bottom (Box with name at bottom).</span></p>"));

        Assert.Equal(["DymolaModels.Icons.Templates.Box_Bottom"],
            result.Classes.Single(c => c.FullName == "Lib.Pack.Thing").ExtendsClasses);
    }

    [Fact]
    public void ParseFile_MultipleBaseClasses_WithCommasInsideDescriptions()
    {
        // The separator between entries is a comma, and so is the one inside "(a, b)" — splitting
        // naively tears the second entry in half and invents a base class called "with icons".
        var result = DymolaHelpParser.ParseFile(TwoClassPage(
            "<h2><a name=\"Lib.Pack.Thing\"></a>Thing</h2>" +
            "<p><span class=\"ModelicaBaseClass\">Extends from Lib.A (First, with a comma), " +
            "Lib.B (Second).</span></p>"));

        Assert.Equal(["Lib.A", "Lib.B"],
            result.Classes.Single(c => c.FullName == "Lib.Pack.Thing").ExtendsClasses);
    }

    [Fact]
    public void ParseFile_PredefinedTypeBaseClass_IsDropped()
    {
        // Modelica.Blocks.Interfaces.RealInput really does document as "Extends from Real."
        // Synthesizing `extends Real;` onto a class does not parse, and Real is not resolvable.
        var result = DymolaHelpParser.ParseFile(TwoClassPage(
            "<h2><a name=\"Lib.Pack.Thing\"></a>Thing</h2>" +
            "<p><span class=\"ModelicaBaseClass\">Extends from Real.</span></p>"));

        Assert.Empty(result.Classes.Single(c => c.FullName == "Lib.Pack.Thing").ExtendsClasses!);
    }

    #endregion

    #region Tables

    [Fact]
    public void ParseFile_PackageContent_YieldsChildrenAndIconMap()
    {
        var result = DymolaHelpParser.ParseFile(Page(
            "<h2><a name=\"Lib.Pack\"></a>Pack</h2>" +
            "<h3>Package Content</h3>" +
            "<table summary=\"Package Content\" class=\"ModelicaTablePackageContent\">\n" +
            "<tr><th>Name</th><th>Description</th></tr>\n" +
            "<tr>\n<td><img src=\"Lib.Pack.AS.png\" alt=\"Lib.Pack.A\" width=\"20\">&nbsp;" +
            "<a href=\"Lib_Pack.html#Lib.Pack.A\"\n>A</a>\n</td>\n<td>First</td>\n</tr>\n" +
            "</table>"));

        var pack = result.Classes.Single();
        Assert.Equal(["Lib.Pack.A"], pack.Children);
        Assert.Equal(DocumentedClass.KindPackage, pack.Kind);
        Assert.Equal("Lib.Pack.AS.png", result.IconByClass["Lib.Pack.A"]);
    }

    [Fact]
    public void ParseFile_ParameterTable_ReadsNamesDescriptionsAndUnits()
    {
        var result = DymolaHelpParser.ParseFile(TwoClassPage(
            "<h2><a name=\"Lib.Pack.Thing\"></a>Thing</h2>" +
            "<h3>Parameters</h3>" +
            "<table summary=\"Parameters\" class=\"ModelicaTableParameters\">\n" +
            "<tr><th>Name</th><th>Description</th></tr>\n" +
            "<tr class=\"ModelicaVariability::ParameterGroup\"><td colspan=\"2\">Group</td></tr>\n" +
            "<tr><td>T_start</td><td>Temperature threshold [K]</td></tr>\n" +
            "<tr><td>count</td><td>Number of things</td></tr>\n" +
            "</table>"));

        var thing = result.Classes.Single(c => c.FullName == "Lib.Pack.Thing");
        Assert.Equal(2, thing.Parameters.Count);
        Assert.Equal("T_start", thing.Parameters[0].Name);
        Assert.Equal("Temperature threshold", thing.Parameters[0].Description);
        Assert.Equal("K", thing.Parameters[0].Unit);
        Assert.Null(thing.Parameters[1].Unit);
    }

    [Fact]
    public void ParseFile_EmptyCellFiller_BecomesNullDescription()
    {
        var result = DymolaHelpParser.ParseFile(TwoClassPage(
            "<h2><a name=\"Lib.Pack.Thing\"></a>Thing</h2>" +
            "<h3>Connectors</h3>" +
            "<table summary=\"Connectors\" class=\"ModelicaTableConnectors\">\n" +
            "<tr><th>Name</th><th>Description</th></tr>\n" +
            "<tr><td>bus</td><td>&nbsp;</td></tr>\n</table>"));

        var connector = result.Classes.Single(c => c.FullName == "Lib.Pack.Thing").Connectors.Single();
        Assert.Equal("bus", connector.Name);
        Assert.Null(connector.Description);
    }

    [Fact]
    public void ParseFile_FunctionTables_InferFunctionKind()
    {
        var result = DymolaHelpParser.ParseFile(TwoClassPage(
            "<h2><a name=\"Lib.Pack.F\"></a>F</h2>" +
            "<h3>Inputs</h3><table summary=\"Inputs\" class=\"ModelicaTableInputs\">\n" +
            "<tr><th>Name</th><th>Description</th></tr>\n<tr><td>a_in[:]</td><td>Input array</td></tr>\n</table>" +
            "<h3>Outputs</h3><table summary=\"Outputs\" class=\"ModelicaTableOutputs\">\n" +
            "<tr><th>Name</th><th>Description</th></tr>\n<tr><td>a_out</td><td>Result</td></tr>\n</table>"));

        var function = result.Classes.Single(c => c.FullName == "Lib.Pack.F");
        Assert.Equal(DocumentedClass.KindFunction, function.Kind);
        Assert.Equal("a_in[:]", function.Inputs.Single().Name);
        Assert.Equal("a_out", function.Outputs.Single().Name);
    }

    [Fact]
    public void ParseFile_NoTables_LeavesKindUnknown()
    {
        var result = DymolaHelpParser.ParseFile(TwoClassPage(
            "<h2><a name=\"Lib.Pack.T\"></a>T</h2>"));

        Assert.Equal(DocumentedClass.KindUnknown,
            result.Classes.Single(c => c.FullName == "Lib.Pack.T").Kind);
    }

    #endregion

    #region Malformed input

    [Fact]
    public void ParseFile_NewlinesReplacedByJunkToken_StillParses()
    {
        // Dymola 2024x Refresh 1 emits a literal numeric token where newlines belong — ~57k times
        // in the Modelica Standard Library alone. It always lands between tags, so a tag-oriented
        // scanner is unaffected; anything line-based reads the whole table as one blob.
        const string junk = "0000000140695720";
        var result = DymolaHelpParser.ParseFile(Page(
            "<h2><a name=\"Lib.Pack\"></a>Pack</h2>" + junk +
            "<h2><a name=\"Lib.Pack.Thing\"></a>Thing</h2>" + junk +
            "<p><span class=\"ModelicaDescription\">A thing</span></p>" + junk +
            "<h3>Parameters</h3>" + junk +
            "<table summary=\"Parameters\" class=\"ModelicaTableParameters\">" + junk +
            "<tr><th>Name</th><th>Description</th></tr>" + junk +
            "<tr><td>gain</td><td>Integrator gain [1]</td></tr>" + junk +
            "</table>" + junk));

        var thing = result.Classes.Single(c => c.FullName == "Lib.Pack.Thing");
        Assert.Equal("A thing", thing.Description);
        Assert.Equal("gain", thing.Parameters.Single().Name);
        Assert.Equal("1", thing.Parameters.Single().Unit);
    }

    [Fact]
    public void ParseFile_HeadingWithoutAnchor_IsSkipped()
    {
        var result = DymolaHelpParser.ParseFile(Page(
            "<h2><a name=\"Lib.Pack\"></a>Pack</h2><h2>Not a class</h2>"));

        Assert.Equal(["Lib.Pack"], result.Classes.Select(c => c.FullName));
    }

    [Fact]
    public void ParseFile_HeadingInsideVendorDocumentation_DoesNotEndTheSection()
    {
        // A class's Documentation(info=…) is author-written HTML emitted verbatim, and vendors put
        // their own headings in it. Treating one as a class boundary cuts the section short and
        // discards everything after it — which for a package is its entire content table.
        var result = DymolaHelpParser.ParseFile(Page(
            "<h2><a name=\"Lib.Pack\"></a>Pack</h2>" +
            "<h3>Information</h3>" +
            "<h2><font color=\"#008000\">A heading the vendor wrote</font></h2>" +
            "<p>Some prose.</p>" +
            "<h3>Package Content</h3>" +
            "<table summary=\"Package Content\" class=\"ModelicaTablePackageContent\">\n" +
            "<tr><th>Name</th><th>Description</th></tr>\n" +
            "<tr><td><img src=\"Lib.Pack.AS.png\" alt=\"Lib.Pack.A\">&nbsp;" +
            "<a href=\"Lib_Pack.html#Lib.Pack.A\">A</a></td><td>First</td></tr>\n" +
            "</table>"));

        var pack = result.Classes.Single();
        Assert.Equal(["Lib.Pack.A"], pack.Children);
        Assert.Equal("Lib.Pack.AS.png", result.IconByClass["Lib.Pack.A"]);
    }

    [Fact]
    public void ParseFile_QuotedIdentifierClassName_SplitsOnTheRightDot()
    {
        // Operator overloads are named with quoted identifiers, and those can contain anything —
        // including the dot that would otherwise look like a package separator.
        var result = DymolaHelpParser.ParseFile(TwoClassPage(
            "<h2><a name=\"Testing.Time.DateTime.'&lt;='\"></a>'&lt;='</h2>"));

        var overload = result.Classes.Single(c => c.FullName.Contains('<'));
        Assert.Equal("Testing.Time.DateTime.'<='", overload.FullName);
        Assert.Equal("'<='", overload.SimpleName);
        Assert.Equal("Testing.Time.DateTime", overload.ParentName);
    }

    [Fact]
    public void DocumentedClass_QuotedIdentifierContainingADot_IsNotSplitInsideTheQuotes()
    {
        var documented = new DocumentedClass(
            "Lib.Pack.'a.b'", null, null, null, null, DocumentedClass.KindUnknown,
            [], [], [], [], [], []);

        Assert.Equal("'a.b'", documented.SimpleName);
        Assert.Equal("Lib.Pack", documented.ParentName);
    }

    [Fact]
    public void ParseFile_UnterminatedTable_DoesNotBleedIntoNextClass()
    {
        var result = DymolaHelpParser.ParseFile(Page(
            "<h2><a name=\"Lib.Pack\"></a>Pack</h2>" +
            "<table class=\"ModelicaTableParameters\"><tr><td>a</td><td>A</td></tr>" +
            "<h2><a name=\"Lib.Pack.Thing\"></a>Thing</h2>" +
            "<p><span class=\"ModelicaDescription\">A thing</span></p>"));

        Assert.Equal("A thing", result.Classes.Single(c => c.FullName == "Lib.Pack.Thing").Description);
    }

    #endregion
}
