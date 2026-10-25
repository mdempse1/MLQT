using ModelicaParser.Helpers;
using Xunit;

namespace ModelicaParser.Tests.Helpers;

public class ToolBuiltInsTests
{
    [Theory]
    [InlineData("classDirectory", ToolBuiltIns.Dymola)]
    [InlineData(".classDirectory", ToolBuiltIns.Dymola)]
    [InlineData("Utilities.getenv", ToolBuiltIns.Dymola)]
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
    public void TheToolsOffered_AreTheOnesThereAreListsFor()
    {
        Assert.Equal([ToolBuiltIns.Dymola, ToolBuiltIns.OpenModelica], ToolBuiltIns.Tools);
    }
}
