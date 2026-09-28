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
}
