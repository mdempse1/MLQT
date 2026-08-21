using ModelicaGraph;
using ModelicaGraph.DataTypes;
using ModelicaParser.DataTypes;
using MLQT.Services.Checking;

namespace MLQT.Services.Tests;

/// <summary>
/// Parser errors are the one class of issue a user cannot work around, so they have to reach the
/// issues list on every path. These cover the conversion; the wiring that calls it lives in MainLayout.
/// </summary>
public class ParserErrorReporterTests
{
    private static ModelNode NodeWith(string id, params ParserError[] errors)
    {
        var node = new ModelNode(id, id.Split('.')[^1], $"model {id} end {id};");
        foreach (var e in errors)
            node.Definition.ParserErrors.Add(e);
        return node;
    }

    [Fact]
    public void NoErrors_ProducesNoMessages()
    {
        Assert.Empty(ParserErrorReporter.ToLogMessages([NodeWith("A")]));
        Assert.Equal((0, 0), ParserErrorReporter.Count([NodeWith("A")]));
    }

    [Fact]
    public void RecoveredSyntaxError_BecomesAnErrorMessageStampedAsParser()
    {
        var node = NodeWith("Lib.A", new ParserError
        {
            Line = 42,
            Message = "something went wrong",
            Severity = ParserErrorSeverity.RecoveredSyntax
        });

        var message = Assert.Single(ParserErrorReporter.ToLogMessages([node]));

        // The id, not the short class name — NavState.ModelID holds the id, so row-click navigation
        // and the "only this model" filter both match against it.
        Assert.Equal("Lib.A", message.ModelName);
        Assert.Equal("Error", message.Severity);
        Assert.Equal("Parser error", message.Summary);
        Assert.Equal(42, message.LineNumber);
        Assert.Equal(ParserErrorReporter.SourceName, message.Source);
        Assert.Contains("something went wrong", message.Details);
    }

    [Fact]
    public void FatalParseFailure_IsDistinguishedFromARecoveredError()
    {
        var node = NodeWith("Lib.A", new ParserError
        {
            Line = 1,
            Message = "file is toast",
            Severity = ParserErrorSeverity.FatalParseFailure
        });

        var message = Assert.Single(ParserErrorReporter.ToLogMessages([node]));

        Assert.Equal("Fatal", message.Severity);
        Assert.Equal("Fatal parse failure", message.Summary);
    }

    [Fact]
    public void OffendingToken_IsIncludedInTheDetails()
    {
        var node = NodeWith("Lib.A", new ParserError { Line = 3, Message = "bad", OffendingToken = "<EOF>" });

        var message = Assert.Single(ParserErrorReporter.ToLogMessages([node]));

        Assert.Contains("<EOF>", message.Details);
    }

    [Fact]
    public void Count_SplitsFatalFromRecovered()
    {
        var nodes = new[]
        {
            NodeWith("A",
                new ParserError { Severity = ParserErrorSeverity.RecoveredSyntax },
                new ParserError { Severity = ParserErrorSeverity.FatalParseFailure }),
            NodeWith("B", new ParserError { Severity = ParserErrorSeverity.RecoveredSyntax }),
            NodeWith("C")
        };

        Assert.Equal((1, 2), ParserErrorReporter.Count(nodes));
    }

    [Fact]
    public void UnterminatedStringInAnnotation_ReachesTheIssuesListWithAnActionableMessage()
    {
        // The real-world case: a Documentation(info=...) annotation missing its closing quote. The
        // class still loads (the parser recovers), so nothing else flags it — the issues list is the
        // only place the user finds out.
        const string source = """
            within;
            package P "a package"
              model A "a model"
              end A;
              annotation(Documentation(info="<html><p>docs</p>));
            end P;
            """;

        var graph = new DirectedGraph();
        var ids = GraphBuilder.LoadModelicaFile(graph, "package.mo", source);
        var models = ids.Select(graph.GetNode<ModelNode>).Where(m => m is not null).Cast<ModelNode>().ToList();

        var messages = ParserErrorReporter.ToLogMessages(models);

        Assert.NotEmpty(messages);
        Assert.Contains(messages, m => m.Details.Contains("Unterminated string literal"));
        // Recovered, not fatal — the models were still extracted, which is why this is easy to miss.
        var (fatal, recovered) = ParserErrorReporter.Count(models);
        Assert.Equal(0, fatal);
        Assert.True(recovered > 0);
    }
}
