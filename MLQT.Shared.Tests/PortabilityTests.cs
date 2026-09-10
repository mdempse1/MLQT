using System.Text.RegularExpressions;
using Xunit;

namespace MLQT.Shared.Tests;

/// <summary>
/// Nothing in this repository depends on MAUI, and nothing may start to.
/// </summary>
/// <remarks>
/// <para>A tombstone. Until phase 7b-8 this class asked a narrower question — whether the projects
/// the Linux jobs build had stayed clear of MAUI, while the <c>MLQT</c> application deliberately had
/// not. That application is deleted and <c>MLQT.Photino</c> has taken its place, so the answer for
/// every project is now the same one, and the test says so directly.</para>
///
/// <para>It is worth keeping in that form because the failure it guards against is not a mistake, it
/// is a *reasonable-looking decision*: somebody wanting a native dialog, a splash screen or a tray
/// icon finds that MAUI has one, adds the package, and it builds on Windows. What happens next is
/// that the Linux jobs fail on a missing workload, at which point the obvious fix is to install the
/// workload in CI — which buries the regression rather than showing it. No CI job installs one, and
/// this test is what names the cause before a runner has to.</para>
///
/// <para>The window, the icon, the file picker and the power-management hold all have Photino or
/// plain-.NET answers now; see <c>MLQT.Photino/Services/</c>.</para>
/// </remarks>
public class PortabilityTests
{
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

    /// <summary>Every project in the repository — there is no longer a portable subset.</summary>
    private static List<string> EveryProject() =>
        Directory.EnumerateFiles(RepositoryRoot(), "*.csproj", SearchOption.AllDirectories)
                 .Where(p => !p.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar))
                 .Where(p => !p.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar))
                 .Order(StringComparer.OrdinalIgnoreCase)
                 .ToList();

    [Fact]
    public void TheSweepSeesEveryProject()
    {
        // Guards the enumeration itself: a sweep that found nothing would make every test below pass
        // over an empty list, which is the failure shape this repository keeps finding.
        var projects = EveryProject().Select(Path.GetFileName).ToList();

        Assert.Contains("MLQT.Shared.csproj", projects);
        Assert.Contains("MLQT.Photino.csproj", projects);
        Assert.Contains("ModelicaParser.csproj", projects);
        Assert.True(projects.Count >= 20, $"only swept {projects.Count} projects: {string.Join(", ", projects)}");
    }

    [Fact]
    public void NoProjectDeclaresTheMauiWorkload()
    {
        var offenders = EveryProject()
            .Where(p => Regex.IsMatch(File.ReadAllText(p), @"<UseMaui\w*>\s*true", RegexOptions.IgnoreCase))
            .Select(Path.GetFileName)
            .ToList();

        Assert.True(offenders.Count == 0,
            "These declare UseMaui, which brings back a workload no CI job installs: "
            + string.Join(", ", offenders));
    }

    [Fact]
    public void NoProjectReferencesAMauiPackage()
    {
        // The package half of the same declaration. Read as XML and asked of the references rather
        // than searched for in the file's text, because several projects mention MAUI in a comment
        // explaining what they replaced — and a text match would fail a project for its own history.
        var offenders = new List<string>();

        foreach (var project in EveryProject())
        {
            var packages = System.Xml.Linq.XDocument.Load(project)
                .Descendants()
                .Where(e => e.Name.LocalName is "PackageReference" or "PackageVersion")
                .Select(e => e.Attribute("Include")?.Value ?? string.Empty);

            if (packages.Any(id => id.StartsWith("Microsoft.Maui", StringComparison.OrdinalIgnoreCase)))
                offenders.Add(Path.GetFileName(project));
        }

        Assert.True(offenders.Count == 0,
            "These reference a Microsoft.Maui package: " + string.Join(", ", offenders));
    }

    [Fact]
    public void EveryProjectTargetsPlainNet10()
    {
        // What the migration bought, stated as a fact about the whole repository: not one project
        // targets a platform-specific framework. `net10.0-windows...` is what the MAUI application
        // targeted and always had, and it is the reason there was never a Linux UI.
        //
        // The declared target framework, not the file's text: MLQT.McpTester's csproj says in a
        // comment what it used to target, and matching that would fail the project for explaining
        // itself.
        var offenders = new List<string>();

        foreach (var project in EveryProject())
        {
            var frameworks = Regex.Matches(File.ReadAllText(project), @"<TargetFrameworks?>([^<]+)</TargetFrameworks?>")
                                  .SelectMany(m => m.Groups[1].Value.Split(';', StringSplitOptions.RemoveEmptyEntries))
                                  .Select(f => f.Trim())
                                  .ToList();

            Assert.True(frameworks.Count > 0, $"{Path.GetFileName(project)} declares no target framework");

            offenders.AddRange(frameworks
                .Where(f => f.Contains('-'))
                .Select(f => $"{Path.GetFileName(project)} ({f})"));
        }

        Assert.True(offenders.Count == 0,
            "These target a platform-specific framework, which is how a project stops building on "
            + "Linux: " + string.Join(", ", offenders));
    }

    [Fact]
    public void TheMauiApplicationIsGone()
    {
        // The narrow, literal tombstone: the deleted project, by the path it lived at. Restoring it
        // is a decision that would have to argue with this test, which is the whole intent of the
        // cutover being irreversible in practice (7b-8).
        var root = RepositoryRoot();

        Assert.False(File.Exists(Path.Combine(root, "MLQT", "MLQT.csproj")),
            "MLQT/MLQT.csproj is back. The MAUI host was replaced by MLQT.Photino in phase 7b; if it "
            + "is needed again, that is a decision to record in Design/design-phase7b-photino.md.");

        var offenders = EveryProject()
            .Where(p => Regex.IsMatch(File.ReadAllText(p), @"ProjectReference[^>]*MLQT[\/]MLQT\.csproj"))
            .Select(Path.GetFileName)
            .ToList();

        Assert.True(offenders.Count == 0,
            "These reference the deleted MAUI application: " + string.Join(", ", offenders));
    }
}
