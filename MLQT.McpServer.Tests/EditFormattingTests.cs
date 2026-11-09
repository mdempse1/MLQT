using MLQT.McpServer.Dtos;
using MLQT.McpServer.Helpers;
using MLQT.McpServer.Tools;
using MLQT.Services.Helpers;

namespace MLQT.McpServer.Tests;

/// <summary>
/// An edit writes its file already formatted with the rules of the repository the class is in, so
/// the agent need not call mlqt_format after it - and only where the desktop app would format the
/// file: the repository applies its formatting rules and nothing in the file opts out. A library from
/// no repository has no rules, and is written as composed.
/// </summary>
public class EditFormattingTests
{
    private static string Library(TestHost host, string model = "model M \"m\"\n  Real x;\nequation\nend M;\n")
        => host.WriteLibraryDir(new Dictionary<string, string>
        {
            ["P/package.mo"] = "within;\npackage P \"p\"\nend P;\n",
            ["P/M.mo"] = "within P;\n" + model,
            ["P/package.order"] = "M\n",
        });

    /// <summary>Loads the library as a repository, with its formatting rules on or off.</summary>
    private static string FromRepository(TestHost host, bool applyFormattingRules, string? model = null)
    {
        var root = model is null ? Library(host) : Library(host, model);
        var repository = host.Repositories.AddRepositoryAsync(root, startMonitoring: false)
            .GetAwaiter().GetResult().Repository!;
        host.Repositories.LoadLibrariesAsync(repository.Id).GetAwaiter().GetResult();
        repository.StyleSettings!.ApplyFormattingRules = applyFormattingRules;
        return Path.Combine(root, "P", "M.mo");
    }

    private static StructureEditTools Edit(TestHost host) => new(host.Libraries, host.Resources, host.Session);

    /// <summary>The file as the formatter writes it: a formatted file renders to itself.</summary>
    private static void AssertFormatted(string file)
    {
        var text = File.ReadAllText(file).ReplaceLineEndings("\n");
        var rendered = ModelicaPackageSaver.RenderFileSource(text, "P", new ModelicaGraph.StyleCheckingSettings().ToFormattingOptions());
        Assert.Equal(rendered.TrimEnd(), text.TrimEnd());
    }

    [Fact]
    public async Task AnEdit_InARepositoryApplyingItsRules_IsWrittenFormatted()
    {
        using var host = new TestHost();
        var file = FromRepository(host, applyFormattingRules: true);

        ToolAssert.Ok<StructureEditResult>(await Edit(host).AddEquation("P.M", "x=1"));

        Assert.DoesNotContain("x=1;", File.ReadAllText(file));
        AssertFormatted(file);
    }

    [Fact]
    public async Task APreview_ShowsTheFileFormatted_AsItWouldLand()
    {
        using var host = new TestHost();
        var file = FromRepository(host, applyFormattingRules: true);
        var before = File.ReadAllText(file);

        var res = ToolAssert.Ok<StructureEditResult>(await Edit(host).AddEquation("P.M", "x=1", preview: true));

        Assert.DoesNotContain("x=1;", res.NewFileContent);
        Assert.Contains("x = 1;", res.NewFileContent);
        Assert.Equal(before, File.ReadAllText(file));
    }

    [Fact]
    public async Task WritingAWholeClass_InARepositoryApplyingItsRules_IsWrittenFormatted()
    {
        using var host = new TestHost();
        var file = FromRepository(host, applyFormattingRules: true);
        var edit = new EditTools(host.Libraries, host.Resources, host.Session);

        ToolAssert.Ok<UpdateClassSourceResult>(await edit.UpdateClassSource(
            "P.M", "model M \"m\"\nReal x;Real y;\nequation\nx=1;y=x;\nend M;"));

        AssertFormatted(file);
    }

    private static EditClassTools Batch(TestHost host) =>
        new(host.Libraries, host.Repositories, host.CustomDictionary, host.DictionaryManager, host.Resources, host.Session);

    [Fact]
    public async Task ABatch_CanReplaceTextAnEarlierOperationWrote_AndIsFormattedOnceAtTheEnd()
    {
        // Each operation re-opens the class from the file. Formatted after every one, the first
        // operation's `x=1` was `x = 1` by the time the second looked for it, and the batch rolled back.
        using var host = new TestHost();
        var file = FromRepository(host, applyFormattingRules: true);

        ToolAssert.Ok<EditClassResult>(await Batch(host).EditClass("P.M",
        [
            new() { Op = "add_equation", Equation = "x=1" },
            new() { Op = "replace", Old = "x=1;", New = "x=2;" },
        ]));

        Assert.Contains("x = 2;", File.ReadAllText(file));
        AssertFormatted(file);
    }

    [Fact]
    public async Task ABatchPreview_ShowsTheFileFormatted_AndLeavesItAlone()
    {
        using var host = new TestHost();
        var file = FromRepository(host, applyFormattingRules: true);
        var before = File.ReadAllText(file);

        var res = ToolAssert.Ok<EditClassResult>(await Batch(host).EditClass("P.M",
            [new() { Op = "add_equation", Equation = "x=1" }], preview: true));

        Assert.Contains("x = 1;", res.NewFileContent);
        Assert.Equal(before, File.ReadAllText(file));
    }

    [Fact]
    public void AClassMovedIn_ThatTheRepositoryExcludesByItsOldId_IsNotFormatted()
    {
        // The excluded list names a class by id, and a move gives it a new one, so the list is asked
        // under the ids the class came in with.
        using var host = new TestHost();
        FromRepository(host, applyFormattingRules: true);
        host.Repositories.GetRepository(host.Libraries.GetOwningLibrary("P.M")!.RepositoryId!)!
            .StyleSettings!.FormattingExcludedModels.Add("Elsewhere.Moved");
        const string composed = "within P;\nmodel M \"m\"\nReal x;\nequation\nx=1;\nend M;\n";

        var kept = EditFormatting.Apply(host.Libraries, host.Session, "P.M", "P.M", null, composed, "P",
            incomingIds: ["Elsewhere.Moved"]);
        var formatted = EditFormatting.Apply(host.Libraries, host.Session, "P.M", "P.M", null, composed, "P");

        Assert.Equal(composed, kept);
        Assert.NotEqual(composed, formatted);
    }

    [Fact]
    public async Task AnEdit_InARepositoryNotApplyingItsRules_IsWrittenAsComposed()
    {
        using var host = new TestHost();
        var file = FromRepository(host, applyFormattingRules: false);

        ToolAssert.Ok<StructureEditResult>(await Edit(host).AddEquation("P.M", "x=1"));

        Assert.Contains("x=1;", File.ReadAllText(file));
    }

    [Fact]
    public async Task AnEdit_ToALibraryFromNoRepository_IsWrittenAsComposed()
    {
        using var host = new TestHost();
        var root = Library(host);
        await host.Libraries.AddLibraryFromDirectoryAsync(Path.Combine(root, "P"));

        ToolAssert.Ok<StructureEditResult>(await Edit(host).AddEquation("P.M", "x=1"));

        Assert.Contains("x=1;", File.ReadAllText(Path.Combine(root, "P", "M.mo")));
    }

    [Fact]
    public async Task AnEdit_ToAClassThatOptsOutOfFormatting_IsWrittenAsComposed()
    {
        using var host = new TestHost();
        var file = FromRepository(host, applyFormattingRules: true,
            "model M \"m\"\n  Real x;\nequation\n  annotation (__MLQT(format=false));\nend M;\n");

        ToolAssert.Ok<StructureEditResult>(await Edit(host).AddEquation("P.M", "x=1"));

        Assert.Contains("x=1;", File.ReadAllText(file));
    }
}
