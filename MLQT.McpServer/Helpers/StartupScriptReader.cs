using System.Text;
using System.Text.RegularExpressions;
using ModelicaParser.Helpers;

namespace MLQT.McpServer.Helpers;

/// <summary>A library a script opens: the path, the script line that opens it, and how.</summary>
internal sealed record ScriptLoad(string Path, int Line, string Via);

/// <summary>A script line that loads nothing, or that the reader could not work out.</summary>
internal sealed record ScriptLine(string File, int Line, string Text, string Reason);

/// <summary>What reading a startup script found, and where it leaves Dymola.</summary>
internal sealed record ScriptReading(
    string Script,
    IReadOnlyList<ScriptLoad> Loads,
    IReadOnlyList<ScriptLine> Ignored,
    IReadOnlyList<ScriptLine> Unresolved,
    string CurrentDirectory,
    string? ModelicaPathSet,
    IReadOnlyList<string> ModelicaPathAdded);

/// <summary>
/// Reads a Dymola startup script without running it, and says which libraries it opens
/// (<c>Design/mcp-tool-consolidation.md</c>, *When the simulator's session decides*). Dymola cannot report
/// which libraries it has loaded, so for a user who opens theirs with a script, the script is the record.
/// </summary>
/// <remarks>
/// <para>The script is walked in order, keeping what Dymola keeps as it runs one: the current directory,
/// the ModelicaPath, the string variables and the libraries opened. It reads <c>openModel</c>,
/// <c>openModelFile</c>, <c>import</c>, <c>cd</c>, <c>RunScript</c>, <c>AddModelicaPath</c> and setting
/// MODELICAPATH, each bare or as <c>DymolaCommands.…</c>, and a command written without parentheses.</para>
/// <para><b>A value is worked out, never guessed.</b> A path is read when it is built from string
/// literals, string variables and <c>getEnvironmentVariable</c>, joined with <c>+</c>. A backslash in a
/// string is a Windows path separator: only <c>\"</c> and <c>\\</c> are escapes. Anything else is reported
/// as unresolved, with its file and line, and lines known to load nothing are reported as ignored.</para>
/// <para>A call to a function of a library the script opened - a loader written as Modelica - is reported
/// as unresolved for now; following such calls is the next step (B503 step 1).</para>
/// </remarks>
internal sealed class StartupScriptReader
{
    private static readonly Regex Import = new(@"^import\s+([A-Za-z_][\w.]*)\s*$", RegexOptions.CultureInvariant);
    private static readonly Regex Assignment = new(@"^([A-Za-z_][\w.]*)\s*:?=(?!=)\s*(.+)$", RegexOptions.CultureInvariant | RegexOptions.Singleline);
    private static readonly Regex Call = new(@"^([A-Za-z_][\w.]*)\s*\((.*)\)\s*$", RegexOptions.CultureInvariant | RegexOptions.Singleline);
    private static readonly Regex Command = new(@"^(cd|RunScript|openModel)\s+(\S.*)$", RegexOptions.CultureInvariant);
    private static readonly Regex Identifier = new(@"^[A-Za-z_]\w*$", RegexOptions.CultureInvariant);

    private readonly Func<string, string?> _environment;
    private readonly List<string> _modelicaPath;
    private readonly Dictionary<string, string> _variables = new(StringComparer.Ordinal);
    private readonly HashSet<string> _opened = new(StringComparer.Ordinal);
    private readonly HashSet<string> _scriptsRead = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ScriptLoad> _loads = [];
    private readonly List<ScriptLine> _ignored = [];
    private readonly List<ScriptLine> _unresolved = [];
    private readonly List<string> _added = [];
    private string? _modelicaPathSet;
    private string _cwd = string.Empty;

    /// <param name="environment">The environment variables a script reads.</param>
    /// <param name="modelicaPath">Dymola's ModelicaPath before the script runs: its own library folder,
    /// then MODELICAPATH.</param>
    public StartupScriptReader(Func<string, string?> environment, IEnumerable<string> modelicaPath)
    {
        _environment = environment;
        _modelicaPath = modelicaPath.ToList();
    }

    /// <summary>Read <paramref name="script"/>. The current directory starts as <paramref name="startDirectory"/>
    /// if given, else the script's own folder.</summary>
    public ScriptReading Read(string script, string? startDirectory = null)
    {
        var full = Path.GetFullPath(script);
        _cwd = startDirectory ?? Path.GetDirectoryName(full) ?? Directory.GetCurrentDirectory();
        ReadFile(full, outerLine: null, viaPrefix: null);
        return new ScriptReading(full, _loads, _ignored, _unresolved, _cwd, _modelicaPathSet, _added);
    }

    private void ReadFile(string file, int? outerLine, string? viaPrefix)
    {
        if (!_scriptsRead.Add(file))
        {
            _ignored.Add(new ScriptLine(Path.GetFileName(file), 0, string.Empty, "already read; a script that runs itself is read once"));
            return;
        }
        var text = ModelicaFileEncoding.ReadAllTextOnly(file);
        foreach (var (statement, line) in Statements(text))
            Interpret(statement, Path.GetFileName(file), line, outerLine ?? line, viaPrefix is null ? "" : $"{viaPrefix} line {line}: ");
    }

    private void Interpret(string statement, string file, int line, int reportLine, string via)
    {
        void Ignore(string reason) => _ignored.Add(new ScriptLine(file, line, statement, reason));
        void Unresolve(string reason) => _unresolved.Add(new ScriptLine(file, line, statement, reason));
        void Load(string path, string how)
        {
            _loads.Add(new ScriptLoad(path, reportLine, via + how));
            _opened.Add(DependencyLoader.NameFromPath(DependencyLoader.LibraryRoot(path) ?? path));
        }

        if (Import.Match(statement) is { Success: true } import)
        {
            var library = import.Groups[1].Value.Split('.')[0];
            if (_opened.Contains(library))
            {
                Ignore("import of a library already loaded; it loads nothing");
                return;
            }
            var pick = LibraryChoice.Choose(library, null, _modelicaPath.Select(f => (f, "ModelicaPath")));
            if (pick.Chosen is null)
                Unresolve($"import {library}: no copy of it is on the script's ModelicaPath");
            else
                Load(pick.Chosen.Path, "import");
            return;
        }

        if (Command.Match(statement) is { Success: true } command && !statement.Contains('('))
        {
            statement = $"{command.Groups[1].Value}(\"{command.Groups[2].Value.Trim().Trim('"')}\")";
        }

        if (Call.Match(statement) is { Success: true } call)
        {
            var name = call.Groups[1].Value;
            var args = Arguments(call.Groups[2].Value);
            switch (Leaf(name))
            {
                case "openModel":
                {
                    if (Value(Argument(args, 0, "path")) is not { } path)
                    {
                        Unresolve("openModel: the path could not be worked out");
                        return;
                    }
                    var resolved = Resolve(path);
                    Load(resolved, "openModel");
                    if (Bool(Argument(args, 2, "changeDirectory")) ?? true)
                        _cwd = Path.GetDirectoryName(resolved) ?? _cwd;
                    return;
                }
                case "openModelFile":
                {
                    if (Value(Argument(args, 1, "path")) is { Length: > 0 } path)
                    {
                        Load(Resolve(path), "openModelFile");
                        return;
                    }
                    if (Value(Argument(args, 0, "modelName")) is not { Length: > 0 } modelName)
                    {
                        Unresolve("openModelFile: the model name could not be worked out");
                        return;
                    }
                    var library = modelName.Split('.')[0];
                    var version = ModelicaVersion.Parse(Value(Argument(args, 2, "version")));
                    var pick = LibraryChoice.Choose(library, version, _modelicaPath.Select(f => (f, "ModelicaPath")));
                    if (pick.Chosen is null)
                        Unresolve($"openModelFile: no copy of {library}{(version is null ? "" : " " + version.Text)} is on the script's ModelicaPath");
                    else
                        Load(pick.Chosen.Path, "openModelFile by name, along the ModelicaPath");
                    return;
                }
                case "cd":
                {
                    var dir = Value(Argument(args, 0, "dir"));
                    if (dir is null)
                        Unresolve("cd: the directory could not be worked out");
                    else if (dir.Length > 0)
                        _cwd = Resolve(dir);
                    return;
                }
                case "RunScript":
                {
                    if (Value(Argument(args, 0, "script")) is not { } nested)
                    {
                        Unresolve("RunScript: the script's path could not be worked out");
                        return;
                    }
                    var folder = Value(Argument(args, 2, "scriptDir")) is { Length: > 0 } scriptDir ? Resolve(scriptDir) : _cwd;
                    var nestedPath = Path.IsPathRooted(nested) ? nested : Path.Combine(folder, nested);
                    if (!File.Exists(nestedPath))
                    {
                        Unresolve($"RunScript: '{nestedPath}' does not exist");
                        return;
                    }
                    ReadFile(Path.GetFullPath(nestedPath), reportLine, $"{via}RunScript {Path.GetFileName(nestedPath)}");
                    return;
                }
                case "AddModelicaPath":
                {
                    if (Value(Argument(args, 0, "path")) is not { } added)
                    {
                        Unresolve("AddModelicaPath: the directory could not be worked out");
                        return;
                    }
                    var folder = Resolve(added);
                    _modelicaPath.Add(folder);
                    _added.Add(folder);
                    return;
                }
                case "setenv" or "setEnvironmentVariable":
                {
                    var variable = Value(Argument(args, 0, "name"));
                    if (!string.Equals(variable, "MODELICAPATH", StringComparison.OrdinalIgnoreCase))
                    {
                        Ignore("sets an environment variable other than MODELICAPATH; it loads nothing");
                        return;
                    }
                    if (Value(Argument(args, 1, "value")) is not { } value)
                    {
                        Unresolve("setting MODELICAPATH: the value could not be worked out");
                        return;
                    }
                    var folders = value.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Select(Resolve).ToList();
                    _modelicaPathSet = string.Join(Path.PathSeparator, folders);
                    _modelicaPath.Clear();
                    _modelicaPath.AddRange(folders);
                    return;
                }
                case "getEnvironmentVariable":
                    Ignore("an expression whose value is not kept; it loads nothing");
                    return;
                case "closeModel":
                    Ignore("closes a library; reading it loads nothing");
                    return;
            }

            if (_opened.Contains(name.Split('.')[0]))
                Unresolve($"calls {name}, a function of a library the script opened; following such calls is not done yet");
            else
                Unresolve($"{name} is not a command the reader knows");
            return;
        }

        if (Assignment.Match(statement) is { Success: true } assignment)
        {
            var target = assignment.Groups[1].Value;
            if (!Identifier.IsMatch(target))
            {
                Ignore("a Dymola setting; it loads nothing");
                return;
            }
            if (Value(assignment.Groups[2].Value) is { } value)
                _variables[target] = value;
            else if (LooksLikeString(assignment.Groups[2].Value))
                Unresolve($"{target}: the value could not be worked out");
            else
                Ignore("an assignment that is not a string; it loads nothing");
            return;
        }

        Unresolve("not a statement the reader knows");
    }

    // A path relative to the current directory, as Dymola reads one; forward and back slashes both work.
    private string Resolve(string path)
    {
        var p = path.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        return Path.GetFullPath(Path.IsPathRooted(p) ? p : Path.Combine(_cwd, p));
    }

    private static string Leaf(string name) => name.Contains('.') ? name[(name.LastIndexOf('.') + 1)..] : name;

    // The value of a string expression: literals, string variables and getEnvironmentVariable joined by '+'.
    private string? Value(string? expression)
    {
        if (expression is null)
            return null;
        var builder = new StringBuilder();
        foreach (var term in Split(expression, '+'))
        {
            var t = term.Trim();
            if (t.Length >= 2 && t[0] == '"' && t[^1] == '"')
                builder.Append(Unescape(t[1..^1]));
            else if (Identifier.IsMatch(t) && _variables.TryGetValue(t, out var variable))
                builder.Append(variable);
            else if (Call.Match(t) is { Success: true } call && Leaf(call.Groups[1].Value) == "getEnvironmentVariable"
                     && Value(Argument(Arguments(call.Groups[2].Value), 0, "name")) is { } name
                     && _environment(name) is { } environmentValue)
                builder.Append(environmentValue);
            else
                return null;
        }
        return builder.ToString();
    }

    private static bool? Bool(string? expression) => expression?.Trim() switch
    {
        "true" => true,
        "false" => false,
        _ => null,
    };

    private static bool LooksLikeString(string expression) => expression.Contains('"') || expression.Contains("getEnvironmentVariable", StringComparison.Ordinal);

    // Only \" and \\ are escapes: anything else after a backslash is a Windows path separator.
    private static string Unescape(string body)
    {
        var builder = new StringBuilder(body.Length);
        for (var i = 0; i < body.Length; i++)
        {
            if (body[i] == '\\' && i + 1 < body.Length && body[i + 1] is '"' or '\\')
            {
                builder.Append(body[i + 1]);
                i++;
            }
            else
            {
                builder.Append(body[i]);
            }
        }
        return builder.ToString();
    }

    private static List<(string? Name, string Value)> Arguments(string text) =>
        Split(text, ',').Where(a => a.Trim().Length > 0).Select(a =>
        {
            var named = Regex.Match(a, @"^\s*([A-Za-z_]\w*)\s*=(?!=)\s*(.+)$", RegexOptions.Singleline);
            return named.Success ? (named.Groups[1].Value, named.Groups[2].Value.Trim()) : ((string?)null, a.Trim());
        }).ToList();

    // An argument by name, else by its position among the unnamed ones.
    private static string? Argument(List<(string? Name, string Value)> args, int position, string name)
    {
        if (args.FirstOrDefault(a => a.Name == name) is { Name: not null } named)
            return named.Value;
        var positional = args.Where(a => a.Name is null).ToList();
        return position < positional.Count ? positional[position].Value : null;
    }

    // Split on a separator outside strings and brackets.
    private static List<string> Split(string text, char separator)
    {
        var parts = new List<string>();
        var depth = 0;
        var inString = false;
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (inString)
            {
                if (c == '\\' && i + 1 < text.Length && text[i + 1] is '"' or '\\')
                    i++;
                else if (c == '"')
                    inString = false;
                continue;
            }
            switch (c)
            {
                case '"': inString = true; break;
                case '(' or '{' or '[': depth++; break;
                case ')' or '}' or ']': depth--; break;
                default:
                    if (c == separator && depth == 0)
                    {
                        parts.Add(text[start..i]);
                        start = i + 1;
                    }
                    break;
            }
        }
        parts.Add(text[start..]);
        return parts;
    }

    /// <summary>
    /// The script's statements, with the line each starts on. A statement ends at a <c>;</c>, or at the end
    /// of a line outside brackets unless the line ends with an operator or a comma. Comments are dropped.
    /// </summary>
    internal static IEnumerable<(string Statement, int Line)> Statements(string text)
    {
        var current = new StringBuilder();
        var line = 1;
        var startLine = 1;
        var depth = 0;
        var inString = false;
        var started = false;   // whether the statement has anything but blanks yet
        var last = ' ';        // the last character that is not a blank

        IEnumerable<(string, int)> Emit()
        {
            var s = current.ToString().Trim();
            current.Clear();
            started = false;
            last = ' ';
            if (s.Length > 0)
                yield return (s, startLine);
        }

        // Everything kept goes through here, so a comment neither starts a statement nor ends a line's
        // last character.
        void Append(char ch)
        {
            if (!char.IsWhiteSpace(ch))
            {
                if (!started)
                {
                    started = true;
                    startLine = line;
                }
                last = ch;
            }
            current.Append(ch);
        }

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (inString)
            {
                Append(c);
                if (c == '\n')
                    line++;
                else if (c == '\\' && i + 1 < text.Length && text[i + 1] is '"' or '\\')
                    Append(text[++i]);
                else if (c == '"')
                    inString = false;
                continue;
            }
            if (c == '/' && i + 1 < text.Length && text[i + 1] == '/')
            {
                while (i < text.Length && text[i] != '\n')
                    i++;
                i--; // let the newline be handled below
                continue;
            }
            if (c == '/' && i + 1 < text.Length && text[i + 1] == '*')
            {
                i += 2;
                while (i < text.Length && !(text[i] == '*' && i + 1 < text.Length && text[i + 1] == '/'))
                {
                    if (text[i] == '\n')
                        line++;
                    i++;
                }
                i++;
                continue;
            }
            switch (c)
            {
                case '"':
                    inString = true;
                    Append(c);
                    break;
                case '(' or '{' or '[':
                    depth++;
                    Append(c);
                    break;
                case ')' or '}' or ']':
                    depth--;
                    Append(c);
                    break;
                case ';' when depth <= 0:
                    foreach (var s in Emit())
                        yield return s;
                    break;
                case '\n':
                    line++;
                    if (depth <= 0 && started && last is not ('+' or ',' or '='))
                    {
                        foreach (var s in Emit())
                            yield return s;
                    }
                    else
                    {
                        Append(' ');
                    }
                    break;
                case '\r':
                    break;
                default:
                    Append(c);
                    break;
            }
        }
        foreach (var s in Emit())
            yield return s;
    }
}
