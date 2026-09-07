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

    [Fact]
    public void TheThreePartsReassemble()
    {
        const string id = "Modelica.Blocks.Sources.Ramp";
        Assert.Equal(id, $"{ModelicaName.EnclosingPackageOf(id)}.{ModelicaName.LeafOf(id)}");
        Assert.StartsWith(ModelicaName.RootLibraryOf(id), ModelicaName.EnclosingPackageOf(id), StringComparison.Ordinal);
    }
}
