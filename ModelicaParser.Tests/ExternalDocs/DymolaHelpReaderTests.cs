using System.Security.Cryptography;
using ModelicaParser.ExternalDocs;
using Xunit;

namespace ModelicaParser.Tests.ExternalDocs;

/// <summary>
/// Reading a whole help directory: what an encrypted library's classes are recovered from.
///
/// <para>The library it describes cannot be read any other way, so every answer here is the only one
/// there will be. That makes the failure modes the interesting part — a directory that is not there,
/// a file that cannot be opened, an icon reference that points outside the folder — because each must
/// degrade to "we know nothing about this" rather than throwing or, worse, inventing.</para>
/// </summary>
public class DymolaHelpReaderTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "mlqt-help-reader", Guid.NewGuid().ToString("N"));

    public DymolaHelpReaderTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private const string Head =
        "<html><head><meta name=\"HTML-Generator\" content=\"Dymola\"></head><body>";

    private string WriteHelp(string name, string body)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, Head + body + "</body></html>");
        return path;
    }

    // The generator writes the rendered icon inside the heading, before the anchor.
    private static string ClassHeading(string id, string simpleName, string? icon = null) =>
        $"<h2>{(icon is null ? "" : $"<img src=\"{icon}\" alt=\"{id}\">")}" +
        $"<a name=\"{id}\"></a>{simpleName}</h2>" +
        "<p><span class=\"ModelicaDescription\">described</span></p>";

    private string WritePng(string name, byte[] content)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllBytes(path, content);
        return path;
    }

    // ── nothing to read ──

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ANameThatIsNotADirectory_IsAnEmptyDocument(string directory)
    {
        var document = DymolaHelpReader.Read(directory);

        Assert.Empty(document.Classes);
        Assert.Equal(0, document.FilesRead);
    }

    [Fact]
    public void ADirectoryThatDoesNotExist_IsAnEmptyDocument()
    {
        // A library that ships no help at all: the namespace stays opaque, which is honest. Claiming
        // an empty library would turn every reference into it into a fabricated broken reference.
        var document = DymolaHelpReader.Read(Path.Combine(_root, "no-help-here"));

        Assert.Empty(document.Classes);
    }

    [Fact]
    public void ADirectoryWithNoHtmlInIt_IsAnEmptyDocument()
    {
        File.WriteAllText(Path.Combine(_root, "readme.txt"), "not html");

        Assert.Empty(DymolaHelpReader.Read(_root).Classes);
    }

    [Fact]
    public void AFileThatIsNotGeneratedDocumentation_IsCountedAsSkipped()
    {
        File.WriteAllText(Path.Combine(_root, "hand-written.html"),
            "<html><body><h2>Not Dymola</h2></body></html>");

        var document = DymolaHelpReader.Read(_root);

        Assert.Empty(document.Classes);
        Assert.Equal(1, document.FilesSkipped);
    }

    // ── the ordinary case ──

    [Fact]
    public void ClassesAreRecoveredFromEveryFileInTheDirectory()
    {
        WriteHelp("Lib.html", ClassHeading("Lib", "Lib"));
        WriteHelp("Lib_Sub.html", ClassHeading("Lib.Sub.A", "A"));

        var document = DymolaHelpReader.Read(_root);

        Assert.Equal(2, document.FilesRead);
        Assert.Contains(document.Classes, c => c.FullName == "Lib");
        Assert.Contains(document.Classes, c => c.FullName == "Lib.Sub.A");
    }

    [Fact]
    public void AFileHeldOpenExclusively_IsSkippedRatherThanFailingTheWholeRead()
    {
        // One unreadable file must not cost the library every other class in it.
        WriteHelp("Lib.html", ClassHeading("Lib.A", "A"));
        var locked = WriteHelp("Locked.html", ClassHeading("Lib.B", "B"));
        using var hold = new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None);

        var document = DymolaHelpReader.Read(_root);

        Assert.Contains(document.Classes, c => c.FullName == "Lib.A");
        Assert.DoesNotContain(document.Classes, c => c.FullName == "Lib.B");
        Assert.Equal(1, document.FilesSkipped);
    }

    // ── icons, which decide whether a recovered class claims to have one ──

    // Each row of a package-content table names a child and shows the small image the generator
    // drew for it. That map is the only route to a package's own icon.
    private static string ContentRow(string id, string image) =>
        $"<tr><td><img src=\"{image}\" alt=\"{id}\"><a href=\"{id}.html#{id}\">{id}</a></td>" +
        "<td>a child</td></tr>";

    private static string ContentTable(params string[] rows) =>
        "<table class=\"ModelicaTablePackageContent\">" + string.Join("", rows) + "</table>";

    [Fact]
    public void AClassWhoseHeadingCarriesAnIcon_IsRecordedAsHavingOne()
    {
        // This is all the evidence there is: the class itself cannot be read, so whether MLQT reports
        // "no icon" against it rests entirely on what the generated page showed.
        WriteHelp("Lib.html", ClassHeading("Lib.Drawn", "Drawn", "real-icon.png"));

        var drawn = Assert.Single(DymolaHelpReader.Read(_root).Classes, c => c.FullName == "Lib.Drawn");

        Assert.NotEqual(false, drawn.HasIcon);
    }

    [Fact]
    public void AClassWhoseHeadingCarriesNoIcon_IsKnownToHaveNone()
    {
        // Every heading but the first carries its class's rendered icon, so its absence is evidence
        // rather than ignorance — and that is what lets the missing-icon rule report on a class
        // recovered from documentation at all.
        WriteHelp("Lib.html",
            ClassHeading("Lib.Pack", "Pack") +
            ClassHeading("Lib.Pack.Plain", "Plain"));

        var plain = Assert.Single(DymolaHelpReader.Read(_root).Classes, c => c.FullName == "Lib.Pack.Plain");

        Assert.False(plain.HasIcon);
    }

    [Fact]
    public void ThePageOwningClass_HasAnUnknownIconRatherThanNone()
    {
        // The heading of the class a page belongs to never carries an icon, whether or not it has
        // one. Reading that as "no icon" would report a missing icon against every package in a
        // vendor's library — so it stays unknown, to be answered from the parent page's content
        // table if that page was read.
        WriteHelp("Lib.html", ClassHeading("Lib.Pack", "Pack") + ClassHeading("Lib.Pack.A", "A"));

        var owner = Assert.Single(DymolaHelpReader.Read(_root).Classes, c => c.FullName == "Lib.Pack");

        Assert.Null(owner.HasIcon);
    }

    /// <summary>
    /// The library a package's icon has to be worked out from: Lib lists two children, one of which
    /// (Lib.Leaf) is documented on this same page with no heading icon and therefore certainly has
    /// none — which makes whatever image Lib showed for it a placeholder. Lib.Pack is a sub-package
    /// with its own page, so its heading says nothing, and the only evidence about it is the image
    /// its parent showed.
    /// </summary>
    private void WriteCalibratedLibrary(string packImage, string leafImage)
    {
        WriteHelp("Lib.html",
            ClassHeading("Lib", "Lib") +
            ContentTable(ContentRow("Lib.Pack", packImage), ContentRow("Lib.Leaf", leafImage)) +
            ClassHeading("Lib.Leaf", "Leaf"));
        WriteHelp("Lib_Pack.html", ClassHeading("Lib.Pack", "Pack"));
    }

    private static DocumentedClass ClassNamed(DymolaHelpDocument document, string name) =>
        Assert.Single(document.Classes, c => c.FullName == name);

    [Fact]
    public void APackageShownWithAnImageNoIconlessClassWasGiven_HasAnIcon()
    {
        WritePng("pack.png", RandomNumberGenerator.GetBytes(64));
        WritePng("blank.png", RandomNumberGenerator.GetBytes(64));
        WriteCalibratedLibrary("pack.png", "blank.png");

        var pack = ClassNamed(DymolaHelpReader.Read(_root), "Lib.Pack");

        Assert.True(pack.HasIcon);
        Assert.Equal("pack.png", pack.IconImagePath);
    }

    [Fact]
    public void APackageShownWithThePlaceholderImage_HasNone()
    {
        // Calibrated, not hard-coded: blank.png is known to be a placeholder only because Lib.Leaf,
        // which certainly has no icon, was drawn with it. The generator deduplicates identical
        // images behind a mangled name, so the two rows genuinely name the same file.
        WritePng("blank.png", RandomNumberGenerator.GetBytes(64));
        WriteCalibratedLibrary("blank.png", "blank.png");

        Assert.False(ClassNamed(DymolaHelpReader.Read(_root), "Lib.Pack").HasIcon);
    }

    [Fact]
    public void TwoPlaceholdersThatAreCopiesOfEachOther_AreBothRecognised()
    {
        // Deduplication is not guaranteed — a release that emits the same placeholder under two
        // names must not make one of them read as a real icon. The comparison is on content.
        var placeholder = RandomNumberGenerator.GetBytes(64);
        WritePng("blank.png", placeholder);
        WritePng("default_0.png", placeholder);
        WriteCalibratedLibrary("default_0.png", "blank.png");

        Assert.False(ClassNamed(DymolaHelpReader.Read(_root), "Lib.Pack").HasIcon);
    }

    [Fact]
    public void TheLibrarysRootPackage_HasNoParentToAnswerForIt()
    {
        // Nothing showed an icon for Lib, so the answer stays unknown. Exactly one class per library
        // ends up here, and reporting a missing icon against it would be a finding invented from an
        // absence of evidence.
        WritePng("pack.png", RandomNumberGenerator.GetBytes(64));
        WritePng("blank.png", RandomNumberGenerator.GetBytes(64));
        WriteCalibratedLibrary("pack.png", "blank.png");

        Assert.Null(ClassNamed(DymolaHelpReader.Read(_root), "Lib").HasIcon);
    }

    [Fact]
    public void AnIconReferenceOutsideTheHelpFolder_LeavesThePackageUnknown()
    {
        // A path into Resources or an absolute URL is not an icon render, so there is nothing to
        // calibrate against and no claim to make either way.
        WritePng("blank.png", RandomNumberGenerator.GetBytes(64));
        WriteCalibratedLibrary("../Resources/Images/logo.png", "blank.png");

        Assert.Null(ClassNamed(DymolaHelpReader.Read(_root), "Lib.Pack").HasIcon);
    }

    [Fact]
    public void AnIconFileThatIsNotThere_LeavesThePackageUnknown()
    {
        WritePng("blank.png", RandomNumberGenerator.GetBytes(64));
        WriteCalibratedLibrary("never-written.png", "blank.png");

        Assert.Null(ClassNamed(DymolaHelpReader.Read(_root), "Lib.Pack").HasIcon);
    }

    [Fact]
    public void APlaceholderImageThatCannotBeRead_CalibratesNothing()
    {
        // Without the placeholder's content there is no set to compare against, so the sub-package's
        // own image cannot be ruled a placeholder. Claiming an icon on that basis would be a guess.
        WritePng("pack.png", RandomNumberGenerator.GetBytes(64));
        WriteCalibratedLibrary("pack.png", "no-such-placeholder.png");

        Assert.True(ClassNamed(DymolaHelpReader.Read(_root), "Lib.Pack").HasIcon);
    }

    [Fact]
    public void AnIconFileHeldOpenExclusively_LeavesThePackageUnknown()
    {
        WritePng("blank.png", RandomNumberGenerator.GetBytes(64));
        var locked = WritePng("pack.png", RandomNumberGenerator.GetBytes(64));
        WriteCalibratedLibrary("pack.png", "blank.png");
        using var hold = new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None);

        Assert.Null(ClassNamed(DymolaHelpReader.Read(_root), "Lib.Pack").HasIcon);
    }

    // ── the document itself ──

    [Fact]
    public void TheEmptyDocument_KnowsNothingAndSaysSo()
    {
        Assert.Empty(DymolaHelpDocument.Empty.Classes);
        Assert.Equal(0, DymolaHelpDocument.Empty.FilesRead);
        Assert.Equal(0, DymolaHelpDocument.Empty.FilesSkipped);
    }
}
