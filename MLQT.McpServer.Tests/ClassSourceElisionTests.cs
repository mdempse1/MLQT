using MLQT.McpServer.Dtos;
using MLQT.McpServer.Tools;
using ModelicaParser.Helpers;

namespace MLQT.McpServer.Tests;

/// <summary>
/// What <c>get_class_source</c> hands an agent when it takes the annotations out (B218).
///
/// <para>It used to re-render the class through <c>ModelicaRenderer</c>, which rebuilds every line
/// it emits — so the text was a reformat of the class rather than the class, and its line numbers
/// described neither the file nor the <c>modelLine</c> the same server reports against a finding.
/// These are the three properties that replaced it: the kept lines are verbatim, they are still at
/// their own numbers, and what comes back parses.</para>
/// </summary>
public class ClassSourceElisionTests
{
    /// <summary>
    /// Annotations in the three places they appear: on a declaration across its own lines, on a
    /// connect equation sharing the line with it, and on the class at the end.
    /// </summary>
    private const string Annotated = """
        within;
        package P "p"
          model M "m"
            parameter Real gain = 1 "the gain"
              annotation (Dialog(group="Tuning"));
            Real y "output"
              annotation (Placement(transformation(extent={{-10,-10},{10,10}})));
            parameter Real undescribed = 2;
          equation
            y = gain*time annotation (Line(points={{-1,-1},{1,1}}));
            annotation (Icon(graphics={
              Rectangle(extent={{-100,-100},{100,100}})}));
          end M;

          function F "f"
            input Real u;
            output Real v;
          external "C" v = f_impl(u)
            annotation (Library="mylib",
              Include="#include <f_impl.h>");
          end F;
        end P;
        """;

    private static TestHost Load(string packageMo, string order)
    {
        var host = new TestHost();
        var dir = host.WriteLibraryDir(new Dictionary<string, string>
        {
            ["package.mo"] = packageMo,
            ["package.order"] = order,
        });
        host.Libraries.AddLibraryFromDirectoryAsync(dir).GetAwaiter().GetResult();
        return host;
    }

    private static (string Verbatim, string Stripped) Both(TestHost host, string classId)
    {
        var query = new ClassQueryTools(host.Libraries);
        return (
            ToolAssert.Ok<ClassSourceResult>(query.GetClassSource(classId, includeAnnotations: true)).Source,
            ToolAssert.Ok<ClassSourceResult>(query.GetClassSource(classId, includeAnnotations: false)).Source);
    }

    private static string[] Lines(string source) =>
        ModelicaParserHelper.NormalizeLineEndings(source).Split('\n');

    [Fact]
    public void EveryLineThatSurvivesIsTheClassesOwnTextAtItsOwnNumber()
    {
        using var host = Load(Annotated, "M\nF\n");
        var (verbatim, stripped) = Both(host, "P.M");

        var before = Lines(verbatim);
        var after = Lines(stripped);

        // Same count, so line n of one is line n of the other - which is what makes a finding's
        // modelLine index this text.
        Assert.Equal(before.Length, after.Length);

        // And nothing was added or moved: the stripped text is the original with characters taken
        // out, in order. The renderer failed this on the first line it emitted, because re-indenting
        // and re-spacing are what rendering is.
        Assert.True(
            IsSubsequence(stripped, ModelicaParserHelper.NormalizeLineEndings(verbatim)),
            "the stripped source is not a deletion of the original");
    }

    /// <summary>Whether <paramref name="part"/> is <paramref name="whole"/> with characters removed.</summary>
    private static bool IsSubsequence(string part, string whole)
    {
        var i = 0;
        foreach (var c in whole)
            if (i < part.Length && part[i] == c)
                i++;
        return i == part.Length;
    }

    [Fact]
    public void TheAnnotationsAreGoneAndTheCodeIsNot()
    {
        using var host = Load(Annotated, "M\nF\n");
        var (_, stripped) = Both(host, "P.M");

        Assert.DoesNotContain("Dialog", stripped);
        Assert.DoesNotContain("Placement", stripped);
        Assert.DoesNotContain("Icon", stripped);
        Assert.DoesNotContain("Line(points", stripped);

        Assert.Contains("parameter Real gain = 1 \"the gain\"", stripped);
        Assert.Contains("Real y \"output\"", stripped);
        Assert.Contains("y = gain*time", stripped);

        var (_, external) = Both(host, "P.F");
        Assert.DoesNotContain("Library=", external);
        Assert.DoesNotContain("Include=", external);
        Assert.Contains("external \"C\" v = f_impl(u)", external);
    }

    /// <summary>
    /// The stripped text is offered to an agent as something it can edit and hand back to
    /// <c>update_class_source</c>, so it has to parse. The trap is the semicolon that terminates a
    /// declaration and sits after the annotation on the annotation's own line: taken out with it,
    /// the declaration above runs into the next one.
    /// </summary>
    [Theory]
    [InlineData("P.M")]   // a declaration's semicolon, and a class annotation that owns its own
    [InlineData("P.F")]   // the semicolon that closes an external clause
    public void WhatComesBackStillParses(string classId)
    {
        using var host = Load(Annotated, "M\nF\n");
        var (_, stripped) = Both(host, classId);

        var (_, _, errors) = ModelicaParserHelper.ParseWithTokensAndErrors(stripped);
        Assert.Empty(errors);
    }

    [Fact]
    public void AClassWithNoAnnotationsComesBackUnchanged()
    {
        using var host = Load("within;\npackage P \"p\"\n  model M \"m\"\n    Real x;\n  end M;\nend P;\n", "M\n");
        var (verbatim, stripped) = Both(host, "P.M");

        Assert.Equal(ModelicaParserHelper.NormalizeLineEndings(verbatim), stripped);
    }
}
