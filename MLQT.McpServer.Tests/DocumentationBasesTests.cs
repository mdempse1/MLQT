using MLQT.McpServer.Dtos;
using MLQT.McpServer.Tools;

namespace MLQT.McpServer.Tests;

/// <summary>
/// mlqt_get_class's documentation view with each base's documentation, nearest first. What a class does
/// is often written once, on the interface it extends - an interface, a library's own interface on it, a
/// template, an example - and an agent read class after class up the chain to find it (2026-11-27).
/// </summary>
public class DocumentationBasesTests
{
    private const string Package = """
        within;
        package P "p"
          partial model Icon "an icon only"
            annotation (Icon(graphics = {Rectangle(extent = {{-100, -100}, {100, 100}})}),
              Documentation(info = "<html><p>This icon indicates an engine.</p></html>"));
          end Icon;
          partial model DerivedIcon "an icon built on an icon"
            extends Icon;
            annotation (Documentation(info = "<html><p>This icon indicates a big engine.</p></html>"));
          end DerivedIcon;
          partial model Interface "the interface"
            Real speed "shaft speed";
            annotation (Documentation(info = "<html><p>Interface: the ports every engine has.</p></html>"));
          end Interface;
          partial model LibraryInterface "the library's interface"
            extends Interface;
            extends DerivedIcon;
            Real bus "the bus";
            annotation (Documentation(info = "<html><p>Library interface: adds a bus.</p></html>"));
          end LibraryInterface;
          partial model Template "a template"
            extends LibraryInterface;
            annotation (Documentation(info = "<html><p>Template: fill in the torque map.</p></html>"));
          end Template;
          model Example "an example"
            extends Template;
            annotation (Documentation(info = "<html><p>Example: a 2 l engine.</p></html>"));
          end Example;
          model Plain "no bases"
            annotation (Documentation(info = "<html><p>Plain.</p></html>"));
          end Plain;
          model Short = Template "a short class";
          model Sibling "another example on the same template"
            extends Template;
          end Sibling;
          model Orphan "extends what is not loaded"
            extends Template;
            extends NotLoaded.Base;
          end Orphan;
        end P;
        """;

    private static ClassFacadeTools Load(TestHost host)
    {
        var dir = host.WriteLibraryDir(new Dictionary<string, string> { ["package.mo"] = Package.ReplaceLineEndings("\n") });
        host.Libraries.AddLibraryFromDirectoryAsync(dir).GetAwaiter().GetResult();
        return new ClassFacadeTools(host.Libraries, host.Resources, host.Impact, host.Session);
    }

    private static ClassDocumentationResult Documentation(ClassFacadeTools tools, string id, bool? bases = null)
    {
        var result = bases is { } b
            ? tools.GetClass([id], ["documentation"], includeBaseDocumentation: b)
            : tools.GetClass([id], ["documentation"]);
        var views = Assert.Single(Assert.IsType<GetClassResult>(result.GetAwaiter().GetResult()).Classes);
        return Assert.IsType<ClassDocumentationResult>(views.Documentation);
    }

    [Fact]
    public void TheBases_AreGivenNearestFirst_EachLabelled_AndIconOnlyBasesAreSkipped()
    {
        // The icons are documented, as MSL's are ("This icon indicates Modelica functions."), and still
        // skipped: they declare nothing and extend only icons. The template declares nothing either, and
        // is kept, because what it extends does declare something.
        using var host = new TestHost();
        var doc = Documentation(Load(host), "P.Example");

        Assert.Equal("Example: a 2 l engine.", doc.Info);
        Assert.Equal(["P.Template", "P.LibraryInterface", "P.Interface"], doc.Bases!.Select(b => b.ClassId));
        Assert.Equal("Template: fill in the torque map.", doc.Bases![0].Info);
        Assert.Equal("a template", doc.Bases![0].Description);
        Assert.Equal(2, doc.SkippedBases);   // P.DerivedIcon, P.Icon
        Assert.Null(doc.MoreBases);
        Assert.Null(doc.UnloadedBases);
    }

    [Fact]
    public void ABaseThatIsNotLoaded_IsNamed_SoTheChainDoesNotReadAsComplete()
    {
        using var host = new TestHost();
        var doc = Documentation(Load(host), "P.Orphan");

        Assert.Equal(["NotLoaded.Base, extended by P.Orphan"], doc.UnloadedBases);
        Assert.Equal("P.Template", doc.Bases![0].ClassId);
    }

    [Fact]
    public void APredefinedTypeExtended_IsNotAnUnloadedBase()
    {
        // type Length = Real(unit = "m") extends the language's Real, which has no class to resolve to.
        using var host = new TestHost();
        var dir = host.WriteLibraryDir(new Dictionary<string, string>
        {
            ["package.mo"] = "within;\npackage T \"t\"\n  type Length = Real(unit = \"m\") \"a length\";\nend T;\n",
        });
        host.Libraries.AddLibraryFromDirectoryAsync(dir).GetAwaiter().GetResult();

        var doc = ToolAssert.Ok<ClassDocumentationResult>(new ViewTools(host.Libraries).GetClassDocumentation("T.Length", includeBases: true));

        Assert.Null(doc.UnloadedBases);
    }

    [Fact]
    public void InACallForSeveralClasses_EachBaseIsGivenOnce()
    {
        using var host = new TestHost();
        var result = Assert.IsType<GetClassResult>(Load(host).GetClass(["P.Example", "P.Sibling"], ["documentation"])
            .GetAwaiter().GetResult());
        var first = Assert.IsType<ClassDocumentationResult>(result.Classes[0].Documentation);
        var second = Assert.IsType<ClassDocumentationResult>(result.Classes[1].Documentation);

        Assert.All(first.Bases!, b => Assert.NotNull(b.Info));
        Assert.All(second.Bases!, b => Assert.Equal(("P.Example", null), (b.GivenWith, b.Info)));
        Assert.Equal(first.Bases!.Select(b => b.ClassId), second.Bases!.Select(b => b.ClassId));
    }

    [Fact]
    public void AnHtmlBaseCutForSize_IsNotCutInsideATag()
    {
        var paragraphs = string.Concat(Enumerable.Range(0, 2_000).Select(i => $"<p>para {i}</p>"));
        var package = "within;\npackage H \"h\"\n  partial model Big \"big\"\n    Real x;\n" +
                      $"    annotation (Documentation(info = \"<html>{paragraphs}</html>\"));\n  end Big;\n" +
                      "  model Leaf \"leaf\"\n    extends Big;\n  end Leaf;\nend H;\n";
        using var host = new TestHost();
        var dir = host.WriteLibraryDir(new Dictionary<string, string> { ["package.mo"] = package });
        host.Libraries.AddLibraryFromDirectoryAsync(dir).GetAwaiter().GetResult();

        var doc = ToolAssert.Ok<ClassDocumentationResult>(
            new ViewTools(host.Libraries).GetClassDocumentation("H.Leaf", format: "html", includeBases: true));

        var cut = Assert.Single(doc.Bases!).Info!;
        var body = cut[..cut.IndexOf(" ... (cut at", StringComparison.Ordinal)];
        Assert.True(body.LastIndexOf('<') < body.LastIndexOf('>'), "the cut left a tag open: " + body[^40..]);
    }

    [Fact]
    public void TheClassAlone_IsStillThere_AsAnOption()
    {
        using var host = new TestHost();
        var doc = Documentation(Load(host), "P.Example", bases: false);

        Assert.Equal("Example: a 2 l engine.", doc.Info);
        Assert.Null(doc.Bases);
        Assert.Null(doc.SkippedBases);
    }

    [Fact]
    public void AClassWithNoBases_HasNoBasesField()
    {
        using var host = new TestHost();
        var doc = Documentation(Load(host), "P.Plain");

        Assert.Null(doc.Bases);
        Assert.Null(doc.MoreBases);
    }

    [Fact]
    public void AShortClass_HasItsBasesDocumentation()
    {
        using var host = new TestHost();
        Assert.Equal(["P.Template", "P.LibraryInterface", "P.Interface"],
            Documentation(Load(host), "P.Short").Bases!.Select(b => b.ClassId));
    }

    [Fact]
    public void ALongChain_IsCutAtTheCap_AndTheRestAreNamed()
    {
        // Five bases of 5,000 characters each: two fit under the cap, the nearest first, and the rest are named.
        var text = new string('x', 5_000);
        var bases = string.Join("\n", Enumerable.Range(1, 5).Select(i =>
            $"  partial model B{i} \"b{i}\"\n    Real x{i};\n" + (i < 5 ? $"    extends B{i + 1};\n" : "") +
            $"    annotation (Documentation(info = \"<html><p>{text}</p></html>\"));\n  end B{i};"));
        var package = $"within;\npackage Q \"q\"\n{bases}\n  model Leaf \"leaf\"\n    extends B1;\n  end Leaf;\nend Q;\n";
        using var host = new TestHost();
        var dir = host.WriteLibraryDir(new Dictionary<string, string> { ["package.mo"] = package });
        host.Libraries.AddLibraryFromDirectoryAsync(dir).GetAwaiter().GetResult();

        var doc = ToolAssert.Ok<ClassDocumentationResult>(new ViewTools(host.Libraries).GetClassDocumentation("Q.Leaf", includeBases: true));

        Assert.Equal(["Q.B1", "Q.B2"], doc.Bases!.Select(b => b.ClassId));
        Assert.Equal(["Q.B3", "Q.B4", "Q.B5"], doc.MoreBases);
        Assert.True(doc.Bases!.Sum(b => b.Info!.Length) <= ViewTools.MaxBaseDocumentationCharacters);
    }

    [Fact]
    public void ANearestBaseOverTheCapAlone_IsCut_SayingWhereToReadIt()
    {
        var text = new string('y', ViewTools.MaxBaseDocumentationCharacters + 500);
        var package = "within;\npackage R \"r\"\n  partial model Big \"big\"\n    Real x;\n" +
                      $"    annotation (Documentation(info = \"<html><p>{text}</p></html>\"));\n  end Big;\n" +
                      "  model Leaf \"leaf\"\n    extends Big;\n  end Leaf;\nend R;\n";
        using var host = new TestHost();
        var dir = host.WriteLibraryDir(new Dictionary<string, string> { ["package.mo"] = package });
        host.Libraries.AddLibraryFromDirectoryAsync(dir).GetAwaiter().GetResult();

        var doc = ToolAssert.Ok<ClassDocumentationResult>(new ViewTools(host.Libraries).GetClassDocumentation("R.Leaf", includeBases: true));

        var only = Assert.Single(doc.Bases!);
        Assert.EndsWith("ask for R.Big's documentation view)", only.Info);
        Assert.True(only.Info!.Length < text.Length);
    }

    [Fact]
    public void TheBasesDocumentation_FollowsTheFormatAskedFor()
    {
        using var host = new TestHost();
        var tools = Load(host);
        var html = Assert.IsType<ClassDocumentationResult>(
            Assert.Single(Assert.IsType<GetClassResult>(
                tools.GetClass(["P.Example"], ["documentation"], documentationFormat: "html").GetAwaiter().GetResult()).Classes).Documentation);

        Assert.Equal("<html><p>Template: fill in the torque map.</p></html>", html.Bases![0].Info);
    }
}
