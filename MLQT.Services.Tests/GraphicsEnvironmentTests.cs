using MLQT.Services;
using Xunit;

namespace MLQT.Services.Tests;

/// <summary>
/// The startup line that says whether this machine can draw with a GPU (backlog B141).
/// </summary>
/// <remarks>
/// The case worth testing is the one this machine is not: no DRM render node, which is what the
/// Hyper-V guest every Linux judgement in phase 7b was made on looked like. That is why
/// <see cref="GraphicsEnvironment.Describe"/> takes the facts rather than gathering them.
/// </remarks>
public class GraphicsEnvironmentTests
{
    [Fact]
    public void OnWindowsItSaysNothing()
    {
        // Not "everything is fine": a line printed on every run everywhere is one people learn to
        // skip, and this one exists to be noticed on the day somebody is explaining a slow window.
        Assert.Null(GraphicsEnvironment.Describe(isLinux: false, "wayland", ["renderD128"]));
        Assert.Null(GraphicsEnvironment.Describe(isLinux: false, null, []));
    }

    [Fact]
    public void WithNoRenderNodeItSaysTheWebviewPaintsOnTheCpu()
    {
        var line = GraphicsEnvironment.Describe(isLinux: true, "wayland", []);

        Assert.NotNull(line);
        Assert.Contains("no DRM render node", line);
        Assert.Contains("lower bound", line);       // what the number from such a machine is worth
        Assert.Contains("B141", line);              // where the reasoning is written down
        Assert.Contains("session=wayland", line);
    }

    [Fact]
    public void WithARenderNodeItNamesIt()
    {
        var line = GraphicsEnvironment.Describe(isLinux: true, "x11", ["renderD129", "renderD128"]);

        Assert.NotNull(line);
        Assert.Contains("renderD128, renderD129", line);     // ordered, so two runs read the same
        Assert.Contains("session=x11", line);

        // It reports what was found and does not promise acceleration works: the machine in B141 had
        // a card node and still could not render, so a line claiming hardware acceleration would have
        // been the confident wrong answer.
        Assert.DoesNotContain("lower bound", line);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AMissingSessionTypeIsSaidToBeUnknown(string? sessionType)
    {
        // XDG_SESSION_TYPE is unset under a bare X server and in a container, and "session=" followed
        // by nothing reads as a truncated log line rather than as an answer.
        var line = GraphicsEnvironment.Describe(isLinux: true, sessionType, ["renderD128"]);

        Assert.NotNull(line);
        Assert.Contains("session=unknown", line);
    }

    [Fact]
    public void ProbeAnswersForThisMachineWithoutThrowing()
    {
        // The I/O half. On Windows the answer is null; on Linux it is a line naming this machine's
        // session. Either way it must not throw on the startup path of a desktop application.
        var line = GraphicsEnvironment.Probe();

        if (OperatingSystem.IsLinux())
            Assert.Contains("Graphics: session=", line);
        else
            Assert.Null(line);
    }
}
