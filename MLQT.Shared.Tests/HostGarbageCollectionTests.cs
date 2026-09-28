using System.Xml.Linq;
using Xunit;

namespace MLQT.Shared.Tests;

/// <summary>
/// Every shipping host runs server GC (B281).
/// </summary>
/// <remarks>
/// <para><b>Why a test for a build setting.</b> A check parses tens of thousands of classes from every
/// core, and parsing allocates heavily. Under the default workstation GC every thread stops for every
/// collection on one shared heap: profiled on Claytex, the thread running the whole-graph analyses spent
/// 53 of its 72 seconds suspended waiting for it. Server GC took the same check from 242s to 104s with
/// peak memory 8.2 GB against 8.5 GB and findings identical.</para>
///
/// <para>And the analyzers were then made parallel, which is only a gain <b>with</b> server GC: under
/// workstation GC the parallel version measured slower than the serial one (255s against 242s) and
/// peaked 3 GB higher. So a host that lost this setting would not merely give back the speed-up — it
/// would be worse off than before the work started, and nothing else would say why. The setting is a
/// line in a project file that looks optional, which is exactly the kind of thing that gets tidied
/// away.</para>
/// </remarks>
public class HostGarbageCollectionTests
{
    public static TheoryData<string> Hosts => new()
    {
        "MLQT.Cli/MLQT.Cli.csproj",
        "MLQT.McpServer/MLQT.McpServer.csproj",
        "MLQT.Photino/MLQT.Photino.csproj",
    };

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "MLQT.slnx")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("repository root not found");
    }

    [Theory]
    [MemberData(nameof(Hosts))]
    public void TheHostRunsServerGc(string project)
    {
        // Read as XML rather than searched as text: the project file explains the setting in a
        // comment, and a text match would pass on the explanation alone.
        var doc = XDocument.Load(Path.Combine(RepositoryRoot(), project));
        var values = doc.Descendants()
            .Where(e => e.Name.LocalName == "ServerGarbageCollection")
            .Select(e => e.Value.Trim())
            .ToList();

        Assert.True(values.Count > 0, $"{project} does not set ServerGarbageCollection");
        Assert.All(values, v => Assert.Equal("true", v, ignoreCase: true));
    }

    [Theory]
    [MemberData(nameof(Hosts))]
    public void TheHostDoesNotTurnOffConcurrentGc(string project)
    {
        // Server GC with background collection is what was measured. Turning concurrent GC off would
        // make every gen-2 collection stop the world, which in the desktop host is the UI thread.
        var doc = XDocument.Load(Path.Combine(RepositoryRoot(), project));

        Assert.DoesNotContain(doc.Descendants(), e =>
            e.Name.LocalName == "ConcurrentGarbageCollection" &&
            string.Equals(e.Value.Trim(), "false", StringComparison.OrdinalIgnoreCase));
    }
}
