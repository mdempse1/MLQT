using Xunit;

namespace MLQT.Shared.Tests;

/// <summary>
/// The decisions the Windows installer script carries, which a compile cannot check.
/// </summary>
/// <remarks>
/// <para>Phase 7b-7. <c>build/installer/mlqt.iss</c> checks the things it can at build time — it
/// refuses to compile if the staging tree is missing any of the three tools, which was verified by
/// hiding one and watching it fail. What is left is a set of choices that compile perfectly whichever
/// way they are set, and each of which has a way of going wrong that is invisible until a user hits
/// it.</para>
///
/// <para>Reading the script as text is crude, and it is the right crudeness here: these are settings,
/// not behaviour, and the failure mode is somebody changing one without meaning to.</para>
/// </remarks>
public class WindowsInstallerTests
{
    private static string Script()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "build", "installer", "mlqt.iss");
            if (File.Exists(candidate))
                return File.ReadAllText(candidate);
            dir = dir.Parent;
        }
        throw new InvalidOperationException("build/installer/mlqt.iss not found");
    }

    [Fact]
    public void TheAppIdNeverChanges()
    {
        // The AppId is what makes an upgrade replace the previous install rather than sit beside it,
        // and what the uninstaller is registered under. Change it and every existing installation
        // becomes an orphan that only its own uninstaller can remove - and the user gets two MLQTs in
        // Apps & Features. Pinned here so that changing it has to be deliberate.
        Assert.Contains("AppId={{8B0E5A6C-2F41-4E2B-9B77-3C6D5A1E9F04}", Script());
    }

    [Fact]
    public void ItInstallsForTheCurrentUserByDefaultAndOffersAllUsers()
    {
        var script = Script();

        // "Just for me" without elevation, with per-machine offered in the same dialog. Dropping the
        // second line silently removes the choice; raising the first silently demands admin.
        Assert.Contains("PrivilegesRequired=lowest", script);
        Assert.Contains("PrivilegesRequiredOverridesAllowed=dialog", script);
    }

    [Fact]
    public void TheStartMenuShortcutNamesItsIcon()
    {
        // B132: Windows invented a Start Menu shortcut aimed at a build from before the icon existed,
        // and because the taskbar takes a running window's icon from its matching shortcut, every copy
        // of MLQT showed the generic placeholder. An installer that creates the shortcut properly is
        // what stops that recurring - but only if it names the icon rather than leaving it to be
        // inferred from the target.
        var shortcut = Script()
            .Split('\n')
            .Single(l => l.TrimStart().StartsWith("Name: \"{autoprograms}"));

        Assert.Contains("IconFilename:", shortcut);
    }

    [Fact]
    public void TheVersionComesFromTheCommandLine()
    {
        var script = Script();

        // Tagged releases pass /DAppVersion. A literal here would ship the same number for ever, which
        // is the defect B145 was: every release said 1.0.0 because nothing passed the version through.
        Assert.Contains("AppVersion={#AppVersion}", script);
        Assert.Contains("OutputBaseFilename=MLQT-{#AppVersion}-win-x64-setup", script);
    }

    [Fact]
    public void ThePrerequisitesComeFromMicrosoftsStableLinks()
    {
        var script = Script();

        // Both were checked by following them: aka.ms resolves to the current .NET 10 runtime and the
        // fwlink to the WebView2 Evergreen bootstrapper. A pinned build number would rot; these do not.
        Assert.Contains("https://aka.ms/dotnet/10.0/dotnet-runtime-win-x64.exe", script);
        Assert.Contains("https://go.microsoft.com/fwlink/p/?LinkId=2124703", script);

        // The base runtime, not the desktop one: both apps reference only Microsoft.NETCore.App, and
        // the desktop runtime is a larger download that would install WPF and WinForms for nothing.
        Assert.DoesNotContain("windowsdesktop-runtime", script);
    }

    [Fact]
    public void UninstallingTakesTheToolBackOutOfPath()
    {
        var script = Script();

        // Inno appends to PATH but does not un-append. Measured by installing and uninstalling and
        // looking: the fragment stayed behind, pointing at a folder that had just been deleted.
        Assert.Contains("procedure RemoveFromPath", script);
        Assert.Contains("procedure CurUninstallStepChanged", script);
        Assert.Contains("RemoveFromPath(ExpandConstant('{app}'))", script);
    }

    [Fact]
    public void ItRefusesToBuildAnInstallerMissingATool()
    {
        var script = Script();

        // The compile-time half, asserted here so that deleting it is not silent. [Files] copies the
        // staging tree wholesale, so a publish that stopped producing one of the three would ship an
        // installer missing a tool with nothing to say so.
        foreach (var exe in new[] { "GuiExe", "CliExe", "McpExe" })
            Assert.Contains($"#if !FileExists(AddBackslash(StageDir) + {exe})", script);
    }
}
