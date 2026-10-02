using ModelicaParser.Helpers;
using Xunit;

namespace ModelicaParser.Tests.Helpers;

/// <summary>
/// Splitting a fully-qualified Modelica name. Trivial arithmetic, written out at six sites before
/// this — and the interesting cases are the degenerate ones the copies each answered for themselves:
/// a top-level name with no dot at all, and an empty id.
/// </summary>
public class ModelicaNameTests
{
    [Theory]
    [InlineData("Modelica.Blocks.Sources.Ramp", "Modelica.Blocks.Sources")]
    [InlineData("Modelica.Blocks", "Modelica")]
    // A top-level class is inside nothing. Empty, not null: it goes straight to a rule visitor's
    // basePackage, whose whole constructor surface defaults it to "" to mean exactly this.
    [InlineData("Modelica", "")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void EnclosingPackage(string? id, string expected) =>
        Assert.Equal(expected, ModelicaName.EnclosingPackageOf(id));

    [Theory]
    [InlineData("Modelica.Blocks.Sources.Ramp", "Ramp")]
    [InlineData("Modelica", "Modelica")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Leaf(string? id, string expected) =>
        Assert.Equal(expected, ModelicaName.LeafOf(id));

    [Theory]
    [InlineData("Modelica.Blocks.Sources.Ramp", "Modelica")]
    [InlineData("Modelica", "Modelica")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void RootLibrary(string? id, string expected) =>
        Assert.Equal(expected, ModelicaName.RootLibraryOf(id));

    /// <summary>
    /// A leading dot is not a name Modelica produces, but a malformed id must not take a segment off
    /// the front and leave the caller resolving against something that does not exist.
    /// </summary>
    [Fact]
    public void ALeadingDotDoesNotProduceAnEmptyPackage()
    {
        Assert.Equal("", ModelicaName.EnclosingPackageOf(".Ramp"));
        Assert.Equal("Ramp", ModelicaName.LeafOf(".Ramp"));
        Assert.Equal(".Ramp", ModelicaName.RootLibraryOf(".Ramp"));
    }

    // ---- subtree membership (B274) ------------------------------------------------

    /// <summary>
    /// The case the whole helper exists for: a namesake shares a prefix and is not inside.
    /// </summary>
    [Theory]
    [InlineData("Root.Src", "Root.Src", true)]              // itself
    [InlineData("Root.Src.Widget", "Root.Src", true)]       // a child
    [InlineData("Root.Src.A.B", "Root.Src", true)]          // a grandchild
    [InlineData("Root.SrcExtra", "Root.Src", false)]        // a namesake
    [InlineData("Root.SrcExtra.Keeper", "Root.Src", false)] // inside a namesake
    [InlineData("Root", "Root.Src", false)]                 // its parent
    [InlineData("Other.Src", "Root.Src", false)]
    [InlineData("", "Root.Src", false)]
    public void IsInSubtreeIsDecidedByTheSeparator(string id, string root, bool inside)
    {
        Assert.Equal(inside, ModelicaName.IsInSubtree(id, root));
    }

    /// <summary>
    /// The strict form excludes the root itself and agrees with the other everywhere else, which is
    /// the only difference between them worth stating.
    /// </summary>
    [Theory]
    [InlineData("Root.Src", "Root.Src", false)]
    [InlineData("Root.Src.Widget", "Root.Src", true)]
    [InlineData("Root.SrcExtra", "Root.Src", false)]
    public void IsStrictlyInsideExcludesTheRoot(string id, string root, bool inside)
    {
        Assert.Equal(inside, ModelicaName.IsStrictlyInside(id, root));
    }

    [Theory]
    [InlineData("Root.Src", "Root.Src", "Dst.Src", "Dst.Src")]
    [InlineData("Root.Src.Widget", "Root.Src", "Dst.Src", "Dst.Src.Widget")]
    [InlineData("Root.Src.A.B", "Root.Src", "Dst.Src", "Dst.Src.A.B")]
    public void ReRootRewritesOnlyThePrefix(string id, string oldRoot, string newRoot, string expected)
    {
        Assert.Equal(expected, ModelicaName.ReRoot(id, oldRoot, newRoot));
    }

    /// <summary>
    /// Null rather than a wrong answer: a name outside the subtree has no re-rooted form, and
    /// returning one is how a namesake's id gets rewritten by a move it had nothing to do with.
    /// </summary>
    [Theory]
    [InlineData("Root.SrcExtra", "Root.Src")]
    [InlineData("Root.SrcExtra.Keeper", "Root.Src")]
    [InlineData("Other.Thing", "Root.Src")]
    public void ReRootRefusesANameThatIsNotInTheSubtree(string id, string oldRoot)
    {
        Assert.Null(ModelicaName.ReRoot(id, oldRoot, "Dst.Src"));
    }

    [Fact]
    public void TheThreePartsReassemble()
    {
        const string id = "Modelica.Blocks.Sources.Ramp";
        Assert.Equal(id, $"{ModelicaName.EnclosingPackageOf(id)}.{ModelicaName.LeafOf(id)}");
        Assert.StartsWith(ModelicaName.RootLibraryOf(id), ModelicaName.EnclosingPackageOf(id), StringComparison.Ordinal);
    }
    // A quoted identifier may contain a dot (MLS 2.3.1), and every one of these took Lib.'a.b'.C for
    // four segments before ModelicaName knew about quotes - each place that split a name differently.
    [Theory]
    [InlineData("Lib.'a.b'.C", new[] { "Lib", "'a.b'", "C" })]
    [InlineData("'x.y'", new[] { "'x.y'" })]
    [InlineData(".Lib.M", new[] { "Lib", "M" })]
    [InlineData(@"Lib.'it\'s.odd'.M", new[] { "Lib", @"'it\'s.odd'", "M" })]
    [InlineData("A", new[] { "A" })]
    public void Segments_KeepAQuotedIdentifierWhole(string name, string[] expected) =>
        Assert.Equal(expected, ModelicaName.Segments(name));

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Segments_OfNothing_IsEmpty(string? name) =>
        Assert.Empty(ModelicaName.Segments(name));

    [Fact]
    public void Join_IsTheInverseOfSegments()
    {
        const string name = "Lib.'a.b'.C";
        Assert.Equal(name, ModelicaName.Join(ModelicaName.Segments(name)));
    }

    [Theory]
    [InlineData("Lib.'a.b'.C", "Lib.'a.b'", "C", "Lib")]
    [InlineData("Lib.'a.b'", "Lib", "'a.b'", "Lib")]
    [InlineData("'a.b'.C", "'a.b'", "C", "'a.b'")]
    [InlineData("'a.b'", "", "'a.b'", "'a.b'")]
    public void QuotedDots_AreNotSeparators(string id, string enclosing, string leaf, string root)
    {
        Assert.Equal(enclosing, ModelicaName.EnclosingPackageOf(id));
        Assert.Equal(leaf, ModelicaName.LeafOf(id));
        Assert.Equal(root, ModelicaName.RootLibraryOf(id));
    }

    [Theory]
    [InlineData("a.b", 1, 1)]
    [InlineData("'a.b'", -1, -1)]
    [InlineData("'a.b'.c.d", 5, 7)]
    // A leading dot is a global marker, not a separator.
    [InlineData(".a.b", 2, 2)]
    [InlineData("", -1, -1)]
    [InlineData(null, -1, -1)]
    public void Separators(string? name, int first, int last)
    {
        Assert.Equal(first, ModelicaName.FirstSeparator(name));
        Assert.Equal(last, ModelicaName.LastSeparator(name));
    }

    [Theory]
    // Short enough, or no middle to leave out: as it is.
    [InlineData("Modelica.Blocks.Sources.Ramp", "Modelica.Blocks.Sources.Ramp")]
    [InlineData("Modelica.AVeryLongPackageNameIndeedThatGoesOnAndOn", "Modelica.AVeryLongPackageNameIndeedThatGoesOnAndOn")]
    // The first two segments and the last, as the two pages that shorten names always wrote them.
    [InlineData("Modelica.Fluid.Examples.HeatingSystem.Components.Pipe", "Modelica.Fluid....Pipe")]
    // A quoted segment is never cut: the old shorteners split 'x.y' and kept half of it.
    [InlineData("Lib.'a.b'.Components.Subsystems.Deeper.Still.Model", "Lib.'a.b'....Model")]
    [InlineData("Lib.Components.Subsystems.Deeper.Still.Models.'m.1'", "Lib.Components....'m.1'")]
    public void Abbreviated_KeepsTheEndsOfALongName(string name, string expected) =>
        Assert.Equal(expected, ModelicaName.Abbreviated(name));

    [Fact]
    public void EnclosingNames_AreInnermostFirst_AndExcludeTheClass() =>
        Assert.Equal(["Lib.'a.b'.C", "Lib.'a.b'", "Lib"], ModelicaName.EnclosingNamesOf("Lib.'a.b'.C.D"));

    [Theory]
    [InlineData("Lib")]
    [InlineData("'a.b'")]
    [InlineData("")]
    [InlineData(null)]
    public void ATopLevelName_HasNoEnclosingNames(string? name) =>
        Assert.Empty(ModelicaName.EnclosingNamesOf(name));
}
