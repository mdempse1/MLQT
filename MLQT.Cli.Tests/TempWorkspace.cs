using System.Diagnostics;

namespace MLQT.Cli.Tests;

/// <summary>
/// A throwaway directory to run the CLI against, and the one place that knows how to make and remove
/// one.
///
/// <para>Twelve test files had grown their own <c>TempLibrary</c>, and they had diverged: some
/// cleared read-only attributes before deleting and some left a git repository undeletable behind
/// them; some created the <c>.mlqt</c> directory and some assumed it; two spelled the root property
/// differently. A fixture fixed in one file stayed broken in eleven, and each new test file started
/// by copying whichever one was nearest.</para>
///
/// <para>What a particular suite needs on top — a baseline path, a way to rewrite the library
/// between runs, a git history — belongs in that suite. This holds only what all of them need.</para>
/// </summary>
internal sealed class TempWorkspace : IDisposable
{
    public TempWorkspace(string prefix = "mlqt-test")
    {
        Root = Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Root);
    }

    /// <summary>The directory itself. Whether the library is here or in a subdirectory is the
    /// caller's business — a repository-shaped fixture usually wants the latter.</summary>
    public string Root { get; }

    /// <summary>A path inside the workspace. No file need exist at it.</summary>
    public string PathTo(params string[] parts) => Path.Combine([Root, .. parts]);

    /// <summary>Writes a file, creating whatever directories it needs.</summary>
    public TempWorkspace Write(string relativePath, string content)
    {
        var full = PathTo(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        return this;
    }

    /// <summary>Writes <c>.mlqt/settings.json</c>, under the workspace root or a subdirectory of it.</summary>
    public TempWorkspace WithSettings(string json, string? under = null)
        => Write(under is null ? Path.Combine(".mlqt", "settings.json")
                               : Path.Combine(under, ".mlqt", "settings.json"), json);

    // ---- the repository-shaped variant -----------------------------------------------------------

    /// <summary>
    /// Makes the workspace a git repository with everything in it committed, for the tests that need
    /// history: the pre-commit hook, and the review diff. Identity is set locally so the run does not
    /// depend on whoever's machine it is on.
    /// </summary>
    public TempWorkspace InitGit(string branch = "main")
    {
        Git($"init -q -b {branch}");
        Git("config user.email test@example.com");
        Git("config user.name Test");
        Git("add -A");
        Git("commit -qm initial");
        return this;
    }

    /// <summary>Runs git in the workspace and hands back what it said, exit code included.</summary>
    public (int Code, string Output) Git(string arguments)
    {
        var info = new ProcessStartInfo("git", arguments)
        {
            WorkingDirectory = Root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        // Some tests exercise a hook that falls back to `mlqt` on PATH, because the process that
        // installed it is a test host rather than the tool. The build output holds mlqt.exe.
        info.Environment["PATH"] =
            AppContext.BaseDirectory + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH");

        using var process = Process.Start(info)!;
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output);
    }

    public void Dispose()
    {
        try
        {
            // git marks objects in .git read-only, which stops a plain recursive delete. Clearing the
            // attribute first is why this lives in one place rather than twelve.
            foreach (var file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
            {
                try { File.SetAttributes(file, FileAttributes.Normal); } catch { /* keep going */ }
            }

            Directory.Delete(Root, recursive: true);
        }
        catch
        {
            // Best effort: a temp directory left behind is not worth failing a test over.
        }
    }
}

/// <summary>Running the CLI the way a user does — through the entry point, with both streams captured.</summary>
internal static class Cli
{
    public static (int code, string stdout, string stderr) Run(params string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var code = CliEntry.RunAsync(args, stdout, stderr).GetAwaiter().GetResult();
        return (code, stdout.ToString(), stderr.ToString());
    }
}
