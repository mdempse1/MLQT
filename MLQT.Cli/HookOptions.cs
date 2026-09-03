namespace MLQT.Cli;

/// <summary>
/// Parsed options for <c>mlqt hook</c>. The check-shaped ones are the subset that makes sense to
/// bake into a hook: what to check, what counts as failure, and what to compare against.
/// </summary>
internal sealed record HookOptions
{
    public required string LibraryPath { get; init; }

    /// <summary>What blocks a commit. `error` by default, matching `mlqt check`.</summary>
    public FailOnLevel FailOn { get; init; } = FailOnLevel.Error;

    /// <summary>Baseline to classify against, so accepted debt does not block every commit.</summary>
    public string? BaselinePath { get; init; }

    /// <summary>VCS ref to diff against, for touched-debt escalation.</summary>
    public string? ChangedFrom { get; init; }

    /// <summary>Libraries loaded so references resolve, never reported on.</summary>
    public IReadOnlyList<string> DependencyPaths { get; init; } = [];

    /// <summary>Replace a hook mlqt did not write, or delete one.</summary>
    public bool Force { get; init; }

    public static bool TryParse(IReadOnlyList<string> args, out HookOptions? options, out string? error)
    {
        options = null;
        error = null;

        string? path = null, baseline = null, changedFrom = null;
        var failOn = FailOnLevel.Error;
        var force = false;
        var dependencies = new List<string>();

        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "--baseline":
                    if (!Next(args, ref i, out baseline, out error)) return false;
                    break;
                case "--changed-from":
                    if (!Next(args, ref i, out changedFrom, out error)) return false;
                    break;
                case "--dependency":
                    if (!Next(args, ref i, out var dependency, out error)) return false;
                    dependencies.Add(dependency!);
                    break;
                case "--force":
                    force = true;
                    break;
                case "--fail-on":
                    if (!Next(args, ref i, out var level, out error)) return false;
                    if (!TryParseFailOn(level!, out failOn))
                    {
                        error = $"invalid --fail-on '{level}' (expected off|warning|error)";
                        return false;
                    }
                    break;
                default:
                    if (arg.StartsWith('-'))
                    {
                        error = $"unknown option '{arg}'";
                        return false;
                    }
                    if (path is not null)
                    {
                        error = $"unexpected argument '{arg}'";
                        return false;
                    }
                    path = arg;
                    break;
            }
        }

        // The library defaults to the working directory, which is where someone standing in their
        // repository would expect `mlqt hook install` to act.
        options = new HookOptions
        {
            LibraryPath = path ?? Directory.GetCurrentDirectory(),
            FailOn = failOn,
            BaselinePath = baseline is null ? null : Path.GetFullPath(RepoPath.Resolve(path ?? ".", baseline)),
            ChangedFrom = changedFrom,
            DependencyPaths = dependencies.Select(d => Path.GetFullPath(RepoPath.Resolve(path ?? ".", d))).ToList(),
            Force = force
        };
        return true;
    }

    private static bool Next(IReadOnlyList<string> args, ref int i, out string? value, out string? error)
    {
        error = null;
        if (i + 1 >= args.Count)
        {
            value = null;
            error = $"option '{args[i]}' requires a value";
            return false;
        }
        value = args[++i];
        return true;
    }

    private static bool TryParseFailOn(string s, out FailOnLevel level)
    {
        switch (s.ToLowerInvariant())
        {
            case "off": level = FailOnLevel.Off; return true;
            case "warning": level = FailOnLevel.Warning; return true;
            case "error": level = FailOnLevel.Error; return true;
            default: level = FailOnLevel.Error; return false;
        }
    }
}
