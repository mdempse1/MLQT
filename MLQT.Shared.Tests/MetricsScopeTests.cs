using MLQT.Shared.Pages;

namespace MLQT.Shared.Tests;

/// <summary>
/// Which classes the Coverage tab counts when a scope is chosen.
/// </summary>
/// <remarks>
/// <para><b>Why this exists (B276).</b> <see cref="MetricsDashboard.InScope"/> carried a remark
/// making the case for itself — <i>"The dot matters. A scope of <c>Modelica.Blocks</c> covers
/// <c>Modelica.Blocks.Sources.Sine</c> and must not cover <c>Modelica.BlocksExtra</c>"</i> — and had
/// no test at all, while three other places in the same file asked the same question inline rather
/// than calling it. Everything now goes through it, and it through
/// <see cref="ModelicaParser.Helpers.ModelicaName.IsInSubtree"/>, so this is the one place the
/// dashboard's scope rule is stated.</para>
///
/// <para>A namesake counted here is not a crash: it is a coverage percentage on screen that quietly
/// includes a library the user did not select, which is the kind of wrong that gets believed.</para>
/// </remarks>
public class MetricsScopeTests
{
    [Theory]
    // The scope itself and everything under it.
    [InlineData("Modelica.Blocks", "Modelica.Blocks", true)]
    [InlineData("Modelica.Blocks.Sources.Sine", "Modelica.Blocks", true)]
    // ...and the namesake the remark names.
    [InlineData("Modelica.BlocksExtra", "Modelica.Blocks", false)]
    [InlineData("Modelica.BlocksExtra.Thing", "Modelica.Blocks", false)]
    // Neither a parent nor a stranger is in scope.
    [InlineData("Modelica", "Modelica.Blocks", false)]
    [InlineData("Buildings.Fluid", "Modelica.Blocks", false)]
    public void AScopeCoversItsOwnSubtreeAndNothingThatMerelyStartsTheSame(
        string modelId, string scope, bool inScope)
    {
        Assert.Equal(inScope, MetricsDashboard.InScope(modelId, scope));
    }

    /// <summary>
    /// No scope means every loaded library, which is what the tab shows before one is picked — and
    /// the reason <c>InScope</c> cannot simply be the shared helper.
    /// </summary>
    [Theory]
    [InlineData("Modelica.Blocks.Sources.Sine")]
    [InlineData("Buildings.Fluid")]
    public void AnEmptyScopeCoversEverything(string modelId)
    {
        Assert.True(MetricsDashboard.InScope(modelId, ""));
    }
}
