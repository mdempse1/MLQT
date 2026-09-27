using Bunit;
using MLQT.Services.Checking;
using MLQT.Shared.Pages;
using ModelicaGraph.DataTypes;
using ModelicaParser.DataTypes;

namespace MLQT.Shared.Tests.Pages;

/// <summary>
/// B412 - the parse-error banner above a class names the same line as the class's row in the
/// Findings panel.
///
/// <para>The banner read the raw <see cref="ParserError.Line"/>, which for an error the load recorded
/// in a class nested in a <c>package.mo</c> is the line in the file, while the row is made by
/// <see cref="ParserErrorReporter"/> and carries the line in the class. They agreed only for a
/// top-level class, where the two are the same.</para>
/// </summary>
public class CodeReviewParserBannerTests : CodeReviewTestBase
{
    // The error is in B, which starts on the file's sixth line: `Real b = ;` is file line 7 and
    // line 2 of B.
    private const string File = """
        package Lib "lib"
          model A "a"
            Real a = 1;
          end A;

          model B "b"
            Real b = ;
          end B;
        end Lib;
        """;

    [Fact]
    public void TheBannerGivesTheLineTheFindingsPanelGives()
    {
        LoadFile("Lib/package.mo", File);
        var node = Graph.GetNode<ModelNode>("Lib.B")!;
        var error = node.Definition.ParserErrors.First();
        Assert.Equal(7, error.Line);   // the precondition: the load recorded the file's line

        ParserErrorReporter.Refresh(Findings, [node]);
        var row = Findings.LogMessages.First(m => m.ModelName == "Lib.B");
        Assert.Equal(2, row.LineNumber);

        var page = RenderPage();
        Select(page, "Lib.B");

        Eventually(page, () =>
        {
            var alert = page.Find(".mud-alert").TextContent;
            Assert.Contains("(first at line 2)", alert);
        });
    }

    [Fact]
    public void ErrorsAreCountedAsTheFindingsPanelCountsThem()
    {
        var node = new ModelNode("Lib.C", "C", "model C end C;") { StartLine = 10 };
        node.Definition.ParserErrors.Add(new ParserError { Line = 12, Message = "one" });
        node.Definition.ParserErrors.Add(new ParserError { Line = 14, Message = "two" });

        Assert.Equal("This model has 2 parser errors (first at line 3). See the Findings panel for details.",
            CodeReview.ParserErrorBannerText(node));
    }

    [Fact]
    public void OneErrorIsAnError()
    {
        var node = new ModelNode("Lib.C", "C", "model C end C;") { StartLine = 1 };
        node.Definition.ParserErrors.Add(new ParserError { Line = 1, Message = "one" });

        Assert.Equal("This model has 1 parser error (first at line 1). See the Findings panel for details.",
            CodeReview.ParserErrorBannerText(node));
    }
}
