using System.Text.RegularExpressions;
using Xunit;

namespace MLQT.Shared.Tests;

/// <summary>
/// The decisions the Linux installer carries, which building it cannot check.
/// </summary>
/// <remarks>
/// <para>Phase 7b-7, and the counterpart of <see cref="WindowsInstallerTests"/>. Between them,
/// <c>build/package-deb.sh</c> and its inputs make three kinds of claim: some are checked when the
/// package is built (the staging tree has all three tools), some when its smoke tests run (each tool
/// runs from the packaged layout, and the GUI passes all 16 <c>/selftest</c> probes), and some are
/// settings that compile perfectly whichever way they are set. The third kind is what is here.</para>
///
/// <para><b>Most of these guard one mechanism, and it is a functional one rather than a cosmetic
/// one.</b> On a Wayland session Photino's <c>SetIconFile</c> is a no-op — there is no protocol for a
/// client to hand the compositor an icon for its own window — so the shell matches the window's
/// <c>app_id</c> to an installed desktop entry and takes the icon from there, for the dock, Alt-Tab
/// and the window list alike (B134). The chain is: the executable is named <c>MLQT.Photino</c>, GTK
/// reports that as <c>app_id</c>, the entry must therefore be called <c>MLQT.Photino.desktop</c>, and
/// its <c>Icon=</c> must name an icon the theme actually holds. Break any link and MLQT is unbranded
/// everywhere on Linux, with nothing in a log to say why.</para>
///
/// <para>Reading files as text is crude, and it is the right crudeness here for the same reason it is
/// on Windows: these are settings, and the failure mode is somebody changing one without meaning
/// to.</para>
/// </remarks>
public class DebianPackageTests
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

    private static string Read(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { RepositoryRoot() }.Concat(parts).ToArray()))
            .Replace("\r\n", "\n");

    private static string Script() => Read("build", "package-deb.sh");
    private static string DesktopEntry() => Read("build", "packaging", "linux", "MLQT.Photino.desktop");
    private static string Control() => Read("build", "packaging", "linux", "control.in");

    /// <summary>
    /// The value of the script's <c>depends=</c> assignment, which is what ends up in the control
    /// file's <c>Depends:</c> field.
    /// </summary>
    /// <remarks>
    /// Parsed rather than searched for in the file, and that is not fussiness: the first version of
    /// the dependency test asserted that each package name appeared <i>somewhere</i> in the script,
    /// and it passed with <c>libnotify4</c> deleted from the declaration — because the paragraph
    /// above it explaining why libnotify4 matters still said the word. Exactly the trap
    /// <see cref="WindowsInstallerTests.TheInstallerAndTheStagingScriptAgreeOnTheExecutableNames"/>
    /// had already been caught by once.
    /// </remarks>
    private static string[] Depends()
    {
        var value = Regex.Match(Script(), @"^depends=""(?<value>[^""]*)""$", RegexOptions.Multiline);
        Assert.True(value.Success, "build/package-deb.sh no longer declares a depends= line");

        return value.Groups["value"].Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>The GUI executable's file name, which is also the window's <c>app_id</c>.</summary>
    private const string GuiExecutable = "MLQT.Photino";

    [Fact]
    public void TheDesktopEntryIsNamedAfterTheWindowsAppId()
    {
        // The whole mechanism in one assertion. GNOME has nothing but app_id to identify the window
        // by, and app_id is GTK's g_get_prgname() - the base name of the executable, observed on the
        // wire as `xdg_toplevel.set_app_id("MLQT.Photino")` rather than inferred. The entry it looks
        // for is that name plus ".desktop", so renaming either the file or the executable without the
        // other silently removes MLQT's icon from every part of the desktop.
        Assert.True(
            File.Exists(Path.Combine(RepositoryRoot(), "build", "packaging", "linux", $"{GuiExecutable}.desktop")),
            $"the desktop entry must be called {GuiExecutable}.desktop, because that is the app_id the window reports");
    }

    [Fact]
    public void TheDesktopEntryLaunchesTheBinaryDirectlyRatherThanTheWrapper()
    {
        // Exec must be the real executable. Launched through anything with a different file name -
        // a symlink, a wrapper called mlqt-gui - the process reports that name as its app_id, the
        // entry matches nothing and the icon disappears. Measured with WAYLAND_DEBUG=1:
        //
        //   through a symlink named mlqt-gui:  set_app_id("mlqt-gui")
        //   through `exec -a MLQT.Photino`:    set_app_id("MLQT.Photino")
        Assert.Contains($"Exec=/opt/mlqt/{GuiExecutable}\n", DesktopEntry());

        // X11 delivers the same value as WM_CLASS, and GNOME matches on that instead there.
        Assert.Contains($"StartupWMClass={GuiExecutable}\n", DesktopEntry());
    }

    [Fact]
    public void TheTerminalLauncherKeepsTheApplicationsOwnIdentity()
    {
        var wrapper = Read("build", "packaging", "linux", "mlqt-gui");

        // The consequence of the above: /usr/bin/mlqt-gui cannot be a symlink. It is a wrapper that
        // overrides argv[0], which is safe because .NET's apphost resolves its base directory from
        // /proc/self/exe - all 16 self-test probes pass when it is launched this way.
        Assert.Contains($"exec -a {GuiExecutable} /opt/mlqt/{GuiExecutable}", wrapper);

        // `exec -a` is a bashism, and /bin/sh on Debian and Ubuntu is dash, which answers
        // "exec: -a: not found". The shebang is load-bearing.
        Assert.StartsWith("#!/bin/bash", wrapper);

        // And the packaging script must install it rather than link it.
        Assert.Contains("install -m 755 \"$packaging/mlqt-gui\" \"$root/usr/bin/mlqt-gui\"", Script());
    }

    [Fact]
    public void TheIconTheEntryNamesIsTheIconThePackageInstalls()
    {
        // A theme name rather than an absolute path - the absolute path is right for a tarball and
        // wrong for a .deb, because it stops the shell choosing the size it wants. Which means the
        // name in the entry and the file name under hicolor are two halves of one contract with
        // nothing between them.
        var name = Regex.Match(DesktopEntry(), @"^Icon=(?<name>\S+)$", RegexOptions.Multiline).Groups["name"].Value;

        Assert.False(string.IsNullOrEmpty(name), "the desktop entry names no icon");
        Assert.DoesNotContain('/', name);

        // The install line, not the file anywhere - a comment mentioning the name would satisfy a
        // plain substring search while the package installed something else entirely.
        Assert.Contains($"\"$root/usr/share/icons/hicolor/${{size}}x${{size}}/apps/{name}.png\"", Script());
    }

    [Fact]
    public void EverySizeTheScriptInstallsExistsInBranding()
    {
        // The script fails if one is missing, but only when somebody runs it on a Linux machine -
        // which is not the common case for a change to Branding/. Checked here so that removing an
        // asset breaks the build rather than the release.
        var sizes = Regex.Match(Script(), @"^icon_sizes=""(?<sizes>[\d ]+)""$", RegexOptions.Multiline)
            .Groups["sizes"].Value
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);

        Assert.NotEmpty(sizes);

        foreach (var size in sizes)
            Assert.True(File.Exists(Path.Combine(RepositoryRoot(), "Branding", $"mlqt-{size}.png")),
                $"build/package-deb.sh installs a {size}x{size} icon, but Branding/mlqt-{size}.png does not exist");
    }

    [Fact]
    public void TheRuntimeIsBundledAndTheWebviewIsDeclared()
    {
        var depends = Depends();

        // webkit2gtk 4.1 and GTK3, which is what Photino.Native links - and which puts the floor at
        // Ubuntu 22.04 and Debian 12. Ubuntu 20.04 ships webkit2gtk 4.0 and cannot run this at all.
        Assert.Contains("libwebkit2gtk-4.1-0", depends);

        // libnotify4 is the one nothing else pulls in. libwebkit2gtk-4.1-0 does not depend on it and
        // Photino.Native links it directly, so a machine without it gets a GUI that cannot load its
        // native library. Not a guess: it is why the desktop-selftest job failed the first time it
        // ran on Linux, on a runner that already had webkit. Every developer desktop has libnotify4
        // for unrelated reasons, so nothing else was ever going to find it.
        Assert.Contains("libnotify4", depends);

        // The pre-t64 names, which the renamed packages on Ubuntu 24.04 and later still Provide, so
        // one dependency line resolves on both sides of that transition. Verified against a real
        // archive: libgtk-3-0t64 declares "Provides: libgtk-3-0".
        Assert.Contains("libgtk-3-0", depends);
        Assert.DoesNotContain("libgtk-3-0t64", depends);

        // Not declared, deliberately: neither Ubuntu nor Debian carries .NET 10, so a dependency on
        // it would mean asking every user to add Microsoft's apt feed before `apt install` would work
        // at all. The package is published self-contained instead.
        Assert.DoesNotContain(depends, d => d.StartsWith("dotnet"));
        Assert.DoesNotContain("dotnet-runtime", Control());
    }

    [Fact]
    public void TheVersionControlClientsAreRecommendedRatherThanRequired()
    {
        // A Git-only user should not be made to install Subversion, and on Linux neither client is
        // bundled: SvnToolLocator finds svn on PATH, which is what a Linux user expects and avoids
        // shipping someone else's binaries. Windows is the other way round and bundles SlikSVN
        // (B144), because there is no package manager to get it from.
        var recommends = Regex.Match(Control(), @"^Recommends:(?<value>.*)$", RegexOptions.Multiline)
            .Groups["value"].Value
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        Assert.Contains("git", recommends);
        Assert.Contains("subversion", recommends);
        Assert.DoesNotContain("subversion", Depends());
        Assert.DoesNotContain("git", Depends());
    }

    [Fact]
    public void TheMcpServerAndTheCliGetStablePathsOnPath()
    {
        // Agents register the MCP server by path, and /opt/mlqt is on nobody's PATH. The CLI is what
        // CI and the pre-commit hook run, so it has to be `mlqt` and nothing else.
        var script = Script();

        Assert.Contains("ln -s /opt/mlqt/mlqt           \"$root/usr/bin/mlqt\"", script);
        Assert.Contains("ln -s /opt/mlqt/MLQT.McpServer \"$root/usr/bin/mlqt-mcp-server\"", script);
    }

    [Fact]
    public void ItRefusesToPackageATreeMissingATool()
    {
        // The counterpart of the Inno script's compile-time guard. The payload is copied wholesale,
        // so a publish that stopped producing one of the three would package without it and nothing
        // would say so until a user went looking - which is how B144 shipped a host with no svn
        // client through an entire phase.
        var script = Script();

        Assert.Contains("for f in MLQT.Photino mlqt MLQT.McpServer; do", script);
        Assert.Contains("is missing from the staging tree", script);

        // And without wwwroot the window opens and loads nothing at all, with no error (B133).
        Assert.Contains("wwwroot/index.html", script);
    }

    [Fact]
    public void ItProvesEachToolRunsFromThePackagedLayoutBeforeCallingItBuilt()
    {
        // The same argument as publish-tools.ps1's smoke tests, one layer further out: that script
        // proves the three tools run, this one proves the package puts them somewhere they still
        // run from. Each marker occurs once, so removing the check removes the marker.
        var script = Script();

        var checks = new (string What, string Marker)[]
        {
            ("dpkg can read the control file", "dpkg-deb --info \"$deb\" >/dev/null"),
            ("the symlinks resolve inside the package", "which the package does not contain"),
            ("the desktop entry is valid", "desktop-file-validate"),
            ("the icon the entry names is installed", "is not in the package"),
            ("the CLI runs and reports the packaged version", "mlqt --version failed"),
            ("the MCP server completes a handshake", @"""method"":""initialize"""),
            ("the GUI runs its self-test probes", "MLQT_SELFTEST_OUT=\"$report\""),
            ("the probe results are judged", "p[\"Status\"] != \"Pass\""),
        };

        foreach (var (what, marker) in checks)
            Assert.True(script.Contains(marker, StringComparison.Ordinal),
                $"package-deb.sh no longer checks that {what}");
    }

    [Fact]
    public void TheTwoInstallersAgreeOnWhatTheyArePackaging()
    {
        // Three ends of one contract in three languages with no compiler between them:
        // build/publish-tools.ps1 produces the tree, build/installer/mlqt.iss packages it on Windows
        // and build/package-deb.sh on Linux. A rename anywhere is silent, and each script's own
        // guard only helps if it is looking for the right name to begin with.
        var staging = Read("build", "publish-tools.ps1");

        var staged = Regex.Matches(staging, @"File\s*=\s*""(?<name>[^""$]+)[$]exe""")
            .Select(m => m.Groups["name"].Value)
            .Order(StringComparer.Ordinal)
            .ToList();

        var packaged = Regex.Match(Script(), @"^for f in (?<names>.+); do$", RegexOptions.Multiline)
            .Groups["names"].Value
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Equal(3, staged.Count);
        Assert.Equal(staged, packaged);
    }

    [Fact]
    public void TheReleaseWorkflowBuildsBothInstallersAndShipsThem()
    {
        // dpkg-deb does not exist on a Windows runner, so the release is two jobs feeding one
        // release. The failure this guards is the quiet one: a Linux job that publishes but never
        // packages, or packages but never uploads, leaving a release with a Windows installer and
        // nothing for Linux - which is what every release before this one was.
        var workflow = Read(".github", "workflows", "release.yml");

        Assert.Contains("build/package-deb.sh", workflow);
        Assert.Contains("runs-on: ubuntu-", workflow);
        Assert.Contains("_amd64.deb", workflow);
    }
}
