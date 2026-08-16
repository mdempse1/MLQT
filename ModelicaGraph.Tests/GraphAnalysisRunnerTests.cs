using ModelicaGraph.Analysis;
using ModelicaGraph.DataTypes;
using ModelicaParser.DataTypes;
using Xunit;

namespace ModelicaGraph.Tests;

/// <summary>Phase 6a: the graph-analyzer seam — gating, severity stamping, suppression.</summary>
public class GraphAnalysisRunnerTests
{
    private sealed class StubAnalyzer : IGraphAnalyzer
    {
        private readonly Finding[] _findings;
        public StubAnalyzer(params Finding[] findings) => _findings = findings;
        public IReadOnlyList<string> RuleIds => new[] { "MLQT.Test.Stub" };
        public IEnumerable<Finding> Analyze(GraphAnalysisContext context) => _findings;
    }

    private static (DirectedGraph graph, GraphAnalysisContext ctx) Setup(string modelId, string code, StyleCheckingSettings settings)
    {
        var graph = new DirectedGraph();
        graph.AddNode(new ModelNode(modelId, modelId, code));
        var models = new List<ModelNode> { graph.GetNode<ModelNode>(modelId)! };
        return (graph, new GraphAnalysisContext(graph, settings, models));
    }

    [Fact]
    public void EnabledAnalyzer_Findings_FlowThrough_WithStampedSeverity()
    {
        var settings = new StyleCheckingSettings();
        settings.RuleSeverities["MLQT.Test.Stub"] = RuleSeverity.Error;
        var (_, ctx) = Setup("M", "model M\n  Real x;\nend M;", settings);
        var stub = new StubAnalyzer(new Finding { RuleId = "MLQT.Test.Stub", ModelId = "M", Message = "hi" });

        var result = GraphAnalysisRunner.Run(ctx, new IGraphAnalyzer[] { stub });

        var f = Assert.Single(result);
        Assert.Equal(RuleSeverity.Error, f.Severity);   // stamped from the map
    }

    [Fact]
    public void DisabledAnalyzer_IsSkipped()
    {
        var settings = new StyleCheckingSettings();   // MLQT.Test.Stub absent → Off
        var (_, ctx) = Setup("M", "model M\n  Real x;\nend M;", settings);
        var stub = new StubAnalyzer(new Finding { RuleId = "MLQT.Test.Stub", ModelId = "M", Message = "hi" });

        Assert.Empty(GraphAnalysisRunner.Run(ctx, new IGraphAnalyzer[] { stub }));
    }

    [Fact]
    public void SuppressedGraphFinding_IsDropped()
    {
        var settings = new StyleCheckingSettings();
        settings.RuleSeverities["MLQT.Test.Stub"] = RuleSeverity.Warning;
        var code = "model M\n  Real x;\n  annotation(__MLQT(suppress=\"MLQT.Test.Stub\"));\nend M;";
        var (_, ctx) = Setup("M", code, settings);
        var stub = new StubAnalyzer(new Finding { RuleId = "MLQT.Test.Stub", ModelId = "M", Message = "hi" });

        Assert.Empty(GraphAnalysisRunner.Run(ctx, new IGraphAnalyzer[] { stub }, honorSuppressions: true));
    }

    [Fact]
    public void SuppressionNotHonoured_WhenDisabled()
    {
        var settings = new StyleCheckingSettings();
        settings.RuleSeverities["MLQT.Test.Stub"] = RuleSeverity.Warning;
        var code = "model M\n  Real x;\n  annotation(__MLQT(suppress=\"MLQT.Test.Stub\"));\nend M;";
        var (_, ctx) = Setup("M", code, settings);
        var stub = new StubAnalyzer(new Finding { RuleId = "MLQT.Test.Stub", ModelId = "M", Message = "hi" });

        Assert.Single(GraphAnalysisRunner.Run(ctx, new IGraphAnalyzer[] { stub }, honorSuppressions: false));
    }

    [Fact]
    public void BuiltInList_IsEmpty_ForNow_SoRunIsNoOp()
    {
        var (_, ctx) = Setup("M", "model M\n  Real x;\nend M;", new StyleCheckingSettings());
        Assert.Empty(GraphAnalysisRunner.Run(ctx));
    }
}
