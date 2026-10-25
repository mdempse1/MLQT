using ModelicaParser.Helpers;
using Xunit;

namespace ModelicaParser.Tests.Helpers;

public class ToolBuiltInsTests
{
    [Theory]
    [InlineData("classDirectory", ToolBuiltIns.Dymola)]
    [InlineData(".classDirectory", ToolBuiltIns.Dymola)]
    [InlineData("Utilities.getenv", ToolBuiltIns.Dymola)]
    [InlineData("LinePattern.Solid", ToolBuiltIns.Dymola)]
    [InlineData("MarkerStyle.None", ToolBuiltIns.Dymola)]
    [InlineData("Advanced.Translation.SparseActivate", ToolBuiltIns.Dymola)]
    [InlineData("Hidden.PrintFailureToDifferentiate", ToolBuiltIns.Dymola)]
    [InlineData("Dymola_AST_ComponentAnnotation", ToolBuiltIns.Dymola)]
    [InlineData("Dymola_AST_Anything.member", ToolBuiltIns.Dymola)]
    [InlineData("OpenModelica.Scripting.getVersion", ToolBuiltIns.OpenModelica)]
    [InlineData("OpenModelica", ToolBuiltIns.OpenModelica)]
    public void AToolsOwnName_IsThatTools(string name, string tool)
    {
        Assert.Equal(tool, ToolBuiltIns.ToolOf(name));
    }

    [Theory]
    [InlineData("sin")]               // the language's own, not a tool's
    [InlineData("Advanced")]          // the flag package's name on its own is no flag
    [InlineData("Utilities.other")]
    [InlineData("Modelica.Utilities.getenv")]
    [InlineData("classDirectoryX")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void AnyOtherName_IsNoTools(string? name)
    {
        Assert.Null(ToolBuiltIns.ToolOf(name));
    }

    [Fact]
    public void ANameIsAccepted_OnlyForAToolTheRepositoryAccepts()
    {
        Assert.True(ToolBuiltIns.IsAccepted("classDirectory", [ToolBuiltIns.Dymola]));
        Assert.True(ToolBuiltIns.IsAccepted("classDirectory", ["dymola"]));  // a hand-written settings file
        Assert.False(ToolBuiltIns.IsAccepted("classDirectory", [ToolBuiltIns.OpenModelica]));
        Assert.False(ToolBuiltIns.IsAccepted("classDirectory", []));
        Assert.False(ToolBuiltIns.IsAccepted("classDirectory", null));
        Assert.False(ToolBuiltIns.IsAccepted("sin", [ToolBuiltIns.Dymola, ToolBuiltIns.OpenModelica]));
    }

    [Fact]
    public void DymolasNames_AreReadOnly_AndAreWhatToolOfMatches()
    {
        // A caller cannot change what every other caller is told, not even by casting back.
        Assert.IsNotType<System.Collections.Generic.HashSet<string>>(ToolBuiltIns.DymolaFunctions, exactMatch: false);
        Assert.Throws<System.NotSupportedException>(
            () => ((System.Collections.Generic.ICollection<string>)ToolBuiltIns.DymolaFunctions).Add("injected"));
        Assert.DoesNotContain("injected", ToolBuiltIns.DymolaFunctions);

        Assert.Contains("classDirectory", ToolBuiltIns.DymolaFunctions);
        Assert.Contains("Utilities.getenv", ToolBuiltIns.DymolaFunctions);
        Assert.DoesNotContain("sin", ToolBuiltIns.DymolaFunctions);
        Assert.All(ToolBuiltIns.DymolaFunctions, name => Assert.Equal(ToolBuiltIns.Dymola, ToolBuiltIns.ToolOf(name)));

        Assert.Equal(["Advanced", "Hidden"], ToolBuiltIns.DymolaFlagPackages.Order(System.StringComparer.Ordinal));
        Assert.All(ToolBuiltIns.DymolaFlagPackages, p => Assert.Equal(ToolBuiltIns.Dymola, ToolBuiltIns.ToolOf($"{p}.Flag")));
        Assert.Equal(ToolBuiltIns.Dymola, ToolBuiltIns.ToolOf($"{ToolBuiltIns.DymolaFunctionPrefix}Anything"));
    }

    [Fact]
    public void TheToolsOffered_AreTheOnesThereAreListsFor()
    {
        Assert.Equal([ToolBuiltIns.Dymola, ToolBuiltIns.OpenModelica], ToolBuiltIns.Tools);
    }
}
