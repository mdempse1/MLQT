using MLQT.McpServer.Dtos;
using MLQT.McpServer.Tools;

namespace MLQT.McpServer.Tests;

public class SearchToolsTests
{
    private const string Package = """
        within;
        package S "s"
          connector RealInput = input Real;
          connector RealOutput = output Real;
          block Pid "a PID controller for tuning"
            parameter Real kp = 1;
            parameter Real ki = 0;
            RealInput u;
            RealOutput y;
            annotation (Documentation(info="<html><p>Proportional-integral-derivative control.</p></html>"));
          end Pid;
          model Plant "the process"
            RealInput u;
            annotation (experiment(StopTime = 10));
          end Plant;
          record Data "just data"
            Real a;
          end Data;
        end S;
        """;

    private static SearchTools Load(TestHost h)
    {
        var dir = h.WriteLibraryDir(new Dictionary<string, string> { ["package.mo"] = Package });
        h.Libraries.AddLibraryFromDirectoryAsync(dir).GetAwaiter().GetResult();
        return new SearchTools(h.Libraries);
    }

    [Fact]
    public void SearchText_MatchesDescription()
    {
        using var host = new TestHost();
        var res = ToolAssert.Ok<TextSearchResult>(Load(host).SearchText("PID controller"));
        Assert.Contains(res.Items, i => i.Id == "S.Pid" && i.MatchedIn == "description");
    }

    [Fact]
    public void SearchText_MatchesDocumentationProse()
    {
        using var host = new TestHost();
        var res = ToolAssert.Ok<TextSearchResult>(Load(host).SearchText("proportional-integral"));
        var hit = res.Items.Single(i => i.Id == "S.Pid");
        Assert.Equal("documentation", hit.MatchedIn);
        Assert.DoesNotContain("<html>", hit.Snippet); // HTML stripped
    }

    [Fact]
    public void SearchByInterface_BlockWithConnectorsAndParameters()
    {
        using var host = new TestHost();
        var res = ToolAssert.Ok<InterfaceSearchResult>(
            Load(host).SearchByInterface(classType: "block", minConnectors: 1, minParameters: 1));

        var pid = res.Items.Single(i => i.Id == "S.Pid");
        Assert.Equal(2, pid.ParameterCount);
        Assert.Equal(2, pid.ConnectorCount);
        Assert.DoesNotContain(res.Items, i => i.Id == "S.Data"); // a record, filtered out by classType
    }

    [Fact]
    public void SearchByInterface_HasExperiment()
    {
        using var host = new TestHost();
        var res = ToolAssert.Ok<InterfaceSearchResult>(Load(host).SearchByInterface(hasExperiment: true));
        Assert.Contains(res.Items, i => i.Id == "S.Plant" && i.HasExperiment);
        Assert.DoesNotContain(res.Items, i => i.Id == "S.Pid");
    }
}
