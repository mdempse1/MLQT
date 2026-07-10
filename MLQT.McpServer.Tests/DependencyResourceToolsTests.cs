using MLQT.McpServer.Dtos;
using MLQT.McpServer.Tools;

namespace MLQT.McpServer.Tests;

public class DependencyResourceToolsTests
{
    private const string DepPackage = """
        within;
        package DepLib "d"
          model Base "b"
            Real b "s";
          equation
            b = time;
          end Base;

          model Middle "m"
            Base base1 "a base";
          end Middle;

          model WithRes "r"
            parameter String f =
              Modelica.Utilities.Files.loadResource("modelica://DepLib/Resources/missing.txt") "data";
          end WithRes;
        end DepLib;
        """;

    private static (DependencyTools deps, ResourceTools res) Load(TestHost host)
    {
        var dir = host.WriteLibraryDir(new Dictionary<string, string> { ["package.mo"] = DepPackage });
        host.Libraries.AddLibraryFromDirectoryAsync(dir).GetAwaiter().GetResult();
        return (new DependencyTools(host.Libraries, host.Impact, host.Resources, host.Session),
                new ResourceTools(host.Libraries, host.Resources, host.Session));
    }

    [Fact]
    public void GetDependencies_BeforeAnalyze_TellsYouToAnalyze()
    {
        using var host = new TestHost();
        var (deps, _) = Load(host);
        // The class exists, but analysis has not run — guide to analyze_dependencies, not "not found".
        var err = ToolAssert.Error(deps.GetDependencies("DepLib.Middle"));
        Assert.Contains("analyze_dependencies", err.Error);
    }

    [Fact]
    public void GetDependencies_NoLibrary_TellsYouToLoad()
    {
        using var host = new TestHost();
        var deps = new DependencyTools(host.Libraries, host.Impact, host.Resources, host.Session);
        var err = ToolAssert.Error(deps.GetDependencies("Modelica.Blocks.Continuous.Integrator"));
        Assert.Contains("load_", err.Error);
    }

    [Fact]
    public async Task AnalyzeDependencies_ThenQueries()
    {
        using var host = new TestHost();
        var (deps, _) = Load(host);

        var summary = ToolAssert.Ok<AnalyzeDependenciesResult>(await deps.AnalyzeDependencies());
        Assert.True(summary.Models >= 3);
        Assert.True(summary.DependencyEdges >= 1);
        Assert.True(host.Session.DependenciesAnalyzed);

        var used = ToolAssert.Ok<DependencyResult>(deps.GetDependencies("DepLib.Middle"));
        Assert.True(used.DependenciesAnalyzed);
        Assert.Contains(used.Items, i => i.Id == "DepLib.Base");

        var usages = ToolAssert.Ok<DependencyResult>(deps.FindUsages("DepLib.Base"));
        Assert.Contains(usages.Items, i => i.Id == "DepLib.Middle");

        var impact = ToolAssert.Ok<ImpactResult>(deps.AnalyzeImpact(["DepLib.Base"]));
        Assert.True(impact.ImpactedModelsCount >= 1);
        Assert.Contains(impact.ImpactDetails, d => d.ModelId == "DepLib.Middle");
    }

    [Fact]
    public async Task AnalyzeDependencies_NoLibraries_Errors()
    {
        using var host = new TestHost();
        var deps = new DependencyTools(host.Libraries, host.Impact, host.Resources, host.Session);
        Assert.IsType<ToolError>(await deps.AnalyzeDependencies());
    }

    [Fact]
    public void Dependencies_MissingClass_Errors()
    {
        using var host = new TestHost();
        var (deps, _) = Load(host);
        Assert.IsType<ToolError>(deps.GetDependencies("DepLib.Nope"));
        Assert.IsType<ToolError>(deps.FindUsages("DepLib.Nope"));
    }

    [Fact]
    public void AnalyzeImpact_Validation()
    {
        using var host = new TestHost();
        var (deps, _) = Load(host);
        Assert.IsType<ToolError>(deps.AnalyzeImpact([]));
        Assert.IsType<ToolError>(deps.AnalyzeImpact(["DepLib.Nope"]));
    }

    [Fact]
    public async Task Resources_Analyzed_ReportsRefsAndWarnings()
    {
        using var host = new TestHost();
        var (deps, res) = Load(host);
        await deps.AnalyzeDependencies();

        var classRes = ToolAssert.Ok<ClassResourcesResult>(res.GetClassResources("DepLib.WithRes"));
        Assert.True(classRes.ResourcesAnalyzed);
        Assert.Contains(classRes.Resources, r => r.RawPath.Contains("missing.txt"));

        var resolved = classRes.Resources[0].ResolvedPath!;
        var usages = res.FindResourceUsages(resolved);
        Assert.IsNotType<ToolError>(usages);

        var warnings = ToolAssert.Ok<ResourceWarningsResult>(res.GetResourceWarnings());
        Assert.True(warnings.Total >= 1);
    }

    [Fact]
    public void Resources_Validation()
    {
        using var host = new TestHost();
        var (_, res) = Load(host);
        Assert.IsType<ToolError>(res.GetClassResources("DepLib.Nope"));
        Assert.IsType<ToolError>(res.FindResourceUsages(" "));
    }
}
