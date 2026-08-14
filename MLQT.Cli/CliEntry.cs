namespace MLQT.Cli;

/// <summary>Top-level dispatch for the `mlqt` command. Kept separate from Program.cs so it is testable.</summary>
internal static class CliEntry
{
    public static async Task<int> RunAsync(string[] args, TextWriter stdout, TextWriter stderr)
    {
        if (args.Length == 0)
        {
            stderr.WriteLine(Usage);
            return ExitCodes.Error;
        }

        switch (args[0])
        {
            case "-h" or "--help" or "help":
                stdout.WriteLine(Usage);
                return ExitCodes.Ok;

            case "--version":
                stdout.WriteLine("mlqt 0.1.0");
                return ExitCodes.Ok;

            case "check":
                if (!CheckOptions.TryParse(args[1..], out var opts, out var error))
                {
                    stderr.WriteLine($"error: {error}");
                    stderr.WriteLine(Usage);
                    return ExitCodes.Error;
                }
                return await CheckRunner.RunAsync(opts!, stdout, stderr);

            default:
                stderr.WriteLine($"error: unknown command '{args[0]}'");
                stderr.WriteLine(Usage);
                return ExitCodes.Error;
        }
    }

    public const string Usage = """
        mlqt — Modelica library quality checks

        Usage:
          mlqt check <library-path> [options]

        Options:
          --config <path>               Settings file (default: <library-path>/.mlqt/settings.json)
          --format console|json|junit   Output format (default: console)
          --out <file>                  Write output to a file instead of stdout
          --fail-on off|warning|error   Exit non-zero when findings reach this level (default: error)
          --no-color                    Disable coloured console output
          -h, --help                    Show this help

        Exit codes: 0 = passed, 1 = findings at/above --fail-on, 2 = usage/load error
        """;
}

internal static class ExitCodes
{
    public const int Ok = 0;
    public const int GateFailed = 1;
    public const int Error = 2;
}
