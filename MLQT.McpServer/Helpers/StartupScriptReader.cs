using System.Text;
using System.Text.RegularExpressions;
using ModelicaParser.Helpers;
using ModelicaParser.Visitors;

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
/// <para><b>A loader written as Modelica is followed.</b> Some users keep the loading in a small library of
/// their own: the script opens it and calls a function that opens the rest, sometimes through other
/// functions of the same library. Such a call is followed when the function is in a library the script has
/// opened: its inputs are bound to the call's arguments or their defaults, string and Boolean assignments
/// are followed, the <c>if</c> branch its condition picks is taken - a condition of Boolean values,
/// literals, <c>not</c>, <c>and</c> and <c>or</c> - and calls to other functions of the same library are
/// followed, five deep at most. Anything else in it, a loop or a condition on a number, is reported as
/// unresolved, with the function and its line.</para>
/// </remarks>
internal sealed class StartupScriptReader
{
    private const int MaxCallDepth = 5;

    private static readonly Regex Import = new(@"^import\s+([A-Za-z_][\w.]*)\s*$", RegexOptions.CultureInvariant);
    private static readonly Regex Assignment = new(@"^([A-Za-z_][\w.]*)\s*:?=(?!=)\s*(.+)$", RegexOptions.CultureInvariant | RegexOptions.Singleline);
    private static readonly Regex Call = new(@"^([A-Za-z_][\w.]*)\s*\((.*)\)\s*$", RegexOptions.CultureInvariant | RegexOptions.Singleline);
    private static readonly Regex Command = new(@"^(cd|RunScript|openModel)\s+(\S.*)$", RegexOptions.CultureInvariant);
    private static readonly Regex Identifier = new(@"^[A-Za-z_]\w*$", RegexOptions.CultureInvariant);
    private static readonly Regex IfHead = new(@"^if\s+(.+?)\s+then\b\s*(.*)$", RegexOptions.CultureInvariant | RegexOptions.Singleline);
    private static readonly Regex ElseIfHead = new(@"^elseif\s+(.+?)\s+then\b\s*(.*)$", RegexOptions.CultureInvariant | RegexOptions.Singleline);
    private static readonly Regex ElseHead = new(@"^else\b\s*(.*)$", RegexOptions.CultureInvariant | RegexOptions.Singleline);
    private static readonly Regex EndWord = new(@"^end\s+(if|for|while)$", RegexOptions.CultureInvariant);

    /// <summary>Where a statement is being read: the script itself, or a function a script called. A
    /// function sees only its own inputs and locals, as in Modelica.</summary>
    private sealed record Frame(Dictionary<string, string> Variables, string? Chain, int Depth);

    private readonly Func<string, string?> _environment;
    private readonly List<string> _modelicaPath;
    private readonly Frame _script;
    private readonly Dictionary<string, string> _opened = new(StringComparer.Ordinal); // name -> library folder or file
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
        _script = new Frame(new Dictionary<string, string>(StringComparer.Ordinal), Chain: null, Depth: 0);
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
            Interpret(statement, Path.GetFileName(file), line, outerLine ?? line,
                viaPrefix is null ? "" : $"{viaPrefix} line {line}: ", _script);
    }

    private void Interpret(string statement, string file, int line, int reportLine, string via, Frame frame)
    {
        void Ignore(string reason) => _ignored.Add(new ScriptLine(file, line, statement, reason));
        void Unresolve(string reason) => _unresolved.Add(new ScriptLine(file, line, statement, reason));
        // In a script a load is named for its command; in a function, by the call chain and the command
        // as written, so the answer says which function opened what.
        void Load(string path, string command, string written)
        {
            _loads.Add(new ScriptLoad(path, reportLine, via + (frame.Chain is null ? command : frame.Chain + written)));
            var root = DependencyLoader.LibraryRoot(path) ?? path;
            _opened[DependencyLoader.NameFromPath(root)] = root;
        }

        if (Import.Match(statement) is { Success: true } import)
        {
            var library = import.Groups[1].Value.Split('.')[0];
            if (_opened.ContainsKey(library))
            {
                Ignore("import of a library already loaded; it loads nothing");
                return;
            }
            var pick = LibraryChoice.Choose(library, null, _modelicaPath.Select(f => (f, "ModelicaPath")));
            if (pick.Chosen is null)
                Unresolve($"import {library}: no copy of it is on the script's ModelicaPath");
            else
                Load(pick.Chosen.Path, "import", "import");
            return;
        }

        if (Command.Match(statement) is { Success: true } command && !statement.Contains('('))
            statement = $"{command.Groups[1].Value}(\"{command.Groups[2].Value.Trim().Trim('"')}\")";

        if (Call.Match(statement) is { Success: true } call)
        {
            var name = call.Groups[1].Value;
            var args = Arguments(call.Groups[2].Value);
            switch (Leaf(name))
            {
                case "openModel":
                {
                    if (Value(Argument(args, 0, "path"), frame) is not { } path)
                    {
                        Unresolve("openModel: the path could not be worked out");
                        return;
                    }
                    var resolved = Resolve(path);
                    Load(resolved, "openModel", name);
                    if (BoolValue(Argument(args, 2, "changeDirectory"), frame) ?? true)
                        _cwd = Path.GetDirectoryName(resolved) ?? _cwd;
                    return;
                }
                case "openModelFile":
                {
                    if (Value(Argument(args, 1, "path"), frame) is { Length: > 0 } path)
                    {
                        Load(Resolve(path), "openModelFile", name);
                        return;
                    }
                    if (Value(Argument(args, 0, "modelName"), frame) is not { Length: > 0 } modelName)
                    {
                        Unresolve("openModelFile: the model name could not be worked out");
                        return;
                    }
                    var library = modelName.Split('.')[0];
                    var version = ModelicaVersion.Parse(Value(Argument(args, 2, "version"), frame));
                    var pick = LibraryChoice.Choose(library, version, _modelicaPath.Select(f => (f, "ModelicaPath")));
                    if (pick.Chosen is null)
                        Unresolve($"openModelFile: no copy of {library}{(version is null ? "" : " " + version.Text)} is on the script's ModelicaPath");
                    else
                        Load(pick.Chosen.Path, "openModelFile by name, along the ModelicaPath", name);
                    return;
                }
                case "cd":
                {
                    var dir = Value(Argument(args, 0, "dir"), frame);
                    if (dir is null)
                        Unresolve("cd: the directory could not be worked out");
                    else if (dir.Length > 0)
                        _cwd = Resolve(dir);
                    return;
                }
                case "RunScript":
                {
                    if (Value(Argument(args, 0, "script"), frame) is not { } nested)
                    {
                        Unresolve("RunScript: the script's path could not be worked out");
                        return;
                    }
                    var folder = Value(Argument(args, 2, "scriptDir"), frame) is { Length: > 0 } scriptDir ? Resolve(scriptDir) : _cwd;
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
                    if (Value(Argument(args, 0, "path"), frame) is not { } added)
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
                    var variable = Value(Argument(args, 0, "name"), frame);
                    if (!string.Equals(variable, "MODELICAPATH", StringComparison.OrdinalIgnoreCase))
                    {
                        Ignore("sets an environment variable other than MODELICAPATH; it loads nothing");
                        return;
                    }
                    if (Value(Argument(args, 1, "value"), frame) is not { } value)
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

            if (_opened.TryGetValue(name.Split('.')[0], out var libraryRoot) && name.Contains('.'))
                Follow(name, call.Groups[2].Value, args, libraryRoot, file, line, reportLine, via, frame);
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
            var expression = assignment.Groups[2].Value;
            if (Value(expression, frame) is { } value)
                frame.Variables[target] = value;
            else if (BoolValue(expression, frame) is { } flag)
                frame.Variables[target] = flag ? "true" : "false";
            else if (LooksLikeString(expression))
            {
                frame.Variables.Remove(target);
                Unresolve($"{target}: the value could not be worked out");
            }
            else
            {
                frame.Variables.Remove(target);
                Ignore("an assignment that is not a string; it loads nothing");
            }
            return;
        }

        Unresolve("not a statement the reader knows");
    }

    // ------------------------------------------------------------------ following a function

    private abstract record Node(int Line);
    private sealed record SimpleNode(string Text, int Line) : Node(Line);
    private sealed record IfNode(List<(string Condition, List<Node> Body)> Branches, List<Node>? Else, int Line) : Node(Line);
    private sealed record UnsupportedNode(string Text, string Reason, int Line) : Node(Line);

    private void Follow(string name, string argsText, List<(string? Name, string Value)> args, string libraryRoot,
        string file, int line, int reportLine, string via, Frame caller)
    {
        if (caller.Depth >= MaxCallDepth)
        {
            _unresolved.Add(new ScriptLine(file, line, name, $"calls {name} more than {MaxCallDepth} functions deep; not followed"));
            return;
        }
        if (FunctionSource(libraryRoot, name) is not { } code)
        {
            _unresolved.Add(new ScriptLine(file, line, name, $"calls {name}, whose source was not found in its library"));
            return;
        }

        var elements = ClassInterfaceExtractor.Extract(ModelicaParserHelper.Parse(code)).Elements;
        var inputs = elements.Where(e => e.Causality == "input").ToList();
        var frame = new Frame(new Dictionary<string, string>(StringComparer.Ordinal),
            (caller.Chain ?? "") + $"{name}({argsText.Trim()}) > ", caller.Depth + 1);

        // Arguments are worked out where the call is written; defaults, in the function.
        var positional = args.Where(a => a.Name is null).Select(a => a.Value).ToList();
        for (var i = 0; i < inputs.Count; i++)
        {
            var given = args.FirstOrDefault(a => a.Name == inputs[i].Name) is { Name: not null } named ? named.Value
                : i < positional.Count ? positional[i] : null;
            var (expression, scope) = given is not null ? (given, caller) : (inputs[i].DefaultValue, frame);
            if (expression is null)
                continue;
            if (inputs[i].Type == "Boolean" && BoolValue(expression, scope) is { } flag)
                frame.Variables[inputs[i].Name] = flag ? "true" : "false";
            else if (Value(expression, scope) is { } text)
                frame.Variables[inputs[i].Name] = text;
        }

        var function = $"{name}";
        if (AlgorithmOf(code, Leaf(name)) is not { } algorithm)
            return;
        var pieces = Statements(algorithm.Text, newlineEnds: false)
            .Select(s => (Text: Regex.Replace(s.Statement, @"\s+", " ").Trim(), Line: s.Line + algorithm.FirstLine - 1))
            .ToList();
        var index = 0;
        var nodes = ParseNodes(pieces, ref index);
        Run(nodes, function, reportLine, via, frame);
    }

    private void Run(List<Node> nodes, string function, int reportLine, string via, Frame frame)
    {
        foreach (var node in nodes)
        {
            switch (node)
            {
                case SimpleNode simple when simple.Text.StartsWith("annotation", StringComparison.Ordinal):
                    break;
                case SimpleNode simple:
                    Interpret(simple.Text, function, simple.Line, reportLine, via, frame);
                    break;
                case UnsupportedNode unsupported:
                    _unresolved.Add(new ScriptLine(function, unsupported.Line, unsupported.Text, unsupported.Reason));
                    break;
                case IfNode branch:
                {
                    List<Node>? taken = branch.Else;
                    foreach (var (condition, body) in branch.Branches)
                    {
                        var value = BoolValue(condition, frame);
                        if (value is null)
                        {
                            _unresolved.Add(new ScriptLine(function, branch.Line, $"if {condition} then",
                                $"the condition '{condition}' could not be worked out, so no branch of it was followed"));
                            taken = null;
                            break;
                        }
                        if (value == true)
                        {
                            taken = body;
                            break;
                        }
                    }
                    if (taken is not null)
                        Run(taken, function, reportLine, via, frame);
                    break;
                }
            }
        }
    }

    // Statements, with if / elseif / else / end if made into a tree. A loop is not followed.
    private static List<Node> ParseNodes(List<(string Text, int Line)> pieces, ref int i)
    {
        var nodes = new List<Node>();
        while (i < pieces.Count)
        {
            var (text, line) = pieces[i];
            if (text.StartsWith("elseif ", StringComparison.Ordinal) || text == "else" || text.StartsWith("else ", StringComparison.Ordinal)
                || EndWord.IsMatch(text))
                return nodes;

            if (IfHead.Match(text) is { Success: true } head)
            {
                var branches = new List<(string, List<Node>)>();
                List<Node>? otherwise = null;
                string? condition = head.Groups[1].Value;   // null once in the else branch
                Continue(pieces, ref i, head.Groups[2].Value);
                while (true)
                {
                    var body = ParseNodes(pieces, ref i);
                    if (condition is not null)
                        branches.Add((condition, body));
                    else
                        otherwise = body;
                    if (i >= pieces.Count)
                        break;
                    var next = pieces[i].Text;
                    if (EndWord.Match(next) is { Success: true } end && end.Groups[1].Value == "if")
                    {
                        i++;
                        break;
                    }
                    if (condition is not null && ElseIfHead.Match(next) is { Success: true } elseif)
                    {
                        condition = elseif.Groups[1].Value;
                        Continue(pieces, ref i, elseif.Groups[2].Value);
                        continue;
                    }
                    if (condition is not null && ElseHead.Match(next) is { Success: true } @else)
                    {
                        condition = null;
                        Continue(pieces, ref i, @else.Groups[1].Value);
                        continue;
                    }
                    break; // an end that is not this if's: leave it to the caller
                }
                nodes.Add(new IfNode(branches, otherwise, line));
                continue;
            }

            if (text.StartsWith("for ", StringComparison.Ordinal) || text.StartsWith("while ", StringComparison.Ordinal))
            {
                var keyword = text.StartsWith("for ", StringComparison.Ordinal) ? "for" : "while";
                var depth = 1;
                i++;
                while (i < pieces.Count && depth > 0)
                {
                    if (pieces[i].Text.StartsWith(keyword + " ", StringComparison.Ordinal))
                        depth++;
                    if (pieces[i].Text.Contains($"end {keyword}", StringComparison.Ordinal))
                        depth--;
                    i++;
                }
                nodes.Add(new UnsupportedNode(text, $"a {keyword} loop; loops are not followed", line));
                continue;
            }

            nodes.Add(new SimpleNode(text, line));
            i++;
        }
        return nodes;

        // What follows "then" or "else" on the same piece is the branch's first statement.
        static void Continue(List<(string Text, int Line)> pieces, ref int i, string rest)
        {
            if (rest.Trim().Length > 0)
                pieces[i] = (rest.Trim(), pieces[i].Line);
            else
                i++;
        }
    }

    /// <summary>The source of function <paramref name="name"/> in the library at <paramref name="root"/>,
    /// following the folders of its packages and then the files that could hold it.</summary>
    private static string? FunctionSource(string root, string name)
    {
        var segments = name.Split('.')[1..];
        if (segments.Length == 0)
            return null;
        var files = new List<string>();
        if (File.Exists(root))
        {
            files.Add(root);
        }
        else
        {
            var dir = root;
            var i = 0;
            while (i < segments.Length - 1 && Directory.Exists(Path.Combine(dir, segments[i])))
                dir = Path.Combine(dir, segments[i++]);
            files.Add(Path.Combine(dir, segments[i] + ".mo"));
            if (i < segments.Length - 1)
                files.Add(Path.Combine(dir, segments[i], "package.mo"));
            files.Add(Path.Combine(dir, "package.mo"));
        }
        var leaf = segments[^1];
        foreach (var file in files.Where(File.Exists))
        {
            var model = ModelicaParserHelper.ExtractModels(ModelicaFileEncoding.ReadAllTextOnly(file))
                .FirstOrDefault(m => m.Name == leaf || m.Name.EndsWith("." + leaf, StringComparison.Ordinal));
            if (model is not null)
                return model.SourceCode;
        }
        return null;
    }

    /// <summary>The text of a function's algorithm section, and the line it starts on within the function.</summary>
    private static (string Text, int FirstLine)? AlgorithmOf(string code, string leaf)
    {
        var start = Regex.Match(code, @"^\s*algorithm\b", RegexOptions.Multiline);
        if (!start.Success)
            return null;
        var bodyStart = start.Index + start.Length;
        var end = Regex.Matches(code, $@"\bend\s+{Regex.Escape(leaf)}\s*;").LastOrDefault();
        var text = code[bodyStart..(end?.Index ?? code.Length)];
        var firstLine = code.AsSpan(0, bodyStart).Count('\n') + 1;
        return (text, firstLine);
    }

    // ------------------------------------------------------------------ values

    // A path relative to the current directory, as Dymola reads one; forward and back slashes both work.
    private string Resolve(string path)
    {
        var p = path.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        return Path.GetFullPath(Path.IsPathRooted(p) ? p : Path.Combine(_cwd, p));
    }

    private static string Leaf(string name) => name.Contains('.') ? name[(name.LastIndexOf('.') + 1)..] : name;

    // The value of a string expression: literals, string variables and getEnvironmentVariable joined by '+'.
    private string? Value(string? expression, Frame frame)
    {
        if (expression is null)
            return null;
        var builder = new StringBuilder();
        foreach (var term in Split(expression, '+'))
        {
            var t = term.Trim();
            if (t.Length >= 2 && t[0] == '"' && t[^1] == '"')
                builder.Append(Unescape(t[1..^1]));
            else if (Identifier.IsMatch(t) && frame.Variables.TryGetValue(t, out var variable) && variable is not ("true" or "false"))
                builder.Append(variable);
            else if (Call.Match(t) is { Success: true } call && Leaf(call.Groups[1].Value) == "getEnvironmentVariable"
                     && Value(Argument(Arguments(call.Groups[2].Value), 0, "name"), frame) is { } name
                     && _environment(name) is { } environmentValue)
                builder.Append(environmentValue);
            else
                return null;
        }
        return builder.ToString();
    }

    // A Boolean expression of true, false, Boolean variables, not, and, or and brackets; null when it
    // holds anything else.
    private static bool? BoolValue(string? expression, Frame frame)
    {
        if (expression is null)
            return null;
        var tokens = Regex.Matches(expression, @"\(|\)|[A-Za-z_][\w.]*|\S").Select(m => m.Value).ToList();
        var at = 0;
        var result = Or();
        return at == tokens.Count ? result : null;

        bool? Or()
        {
            var left = And();
            while (at < tokens.Count && tokens[at] == "or")
            {
                at++;
                var right = And();
                left = left is null || right is null ? null : left.Value || right.Value;
            }
            return left;
        }

        bool? And()
        {
            var left = Not();
            while (at < tokens.Count && tokens[at] == "and")
            {
                at++;
                var right = Not();
                left = left is null || right is null ? null : left.Value && right.Value;
            }
            return left;
        }

        bool? Not()
        {
            if (at < tokens.Count && tokens[at] == "not")
            {
                at++;
                return Not() is { } v ? !v : null;
            }
            return Primary();
        }

        bool? Primary()
        {
            if (at >= tokens.Count)
                return null;
            var token = tokens[at++];
            if (token == "(")
            {
                var inner = Or();
                if (at < tokens.Count && tokens[at] == ")")
                {
                    at++;
                    return inner;
                }
                return null;
            }
            return token switch
            {
                "true" => true,
                "false" => false,
                _ => frame.Variables.TryGetValue(token, out var v) && v is "true" or "false" ? v == "true" : null,
            };
        }
    }

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
    /// The statements of a script or an algorithm, with the line each starts on. A statement ends at a
    /// <c>;</c>; in a script, also at the end of a line outside brackets unless the line ends with an
    /// operator or a comma (a script's commands need no <c>;</c>). Comments are dropped.
    /// </summary>
    internal static IEnumerable<(string Statement, int Line)> Statements(string text, bool newlineEnds = true)
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
                    if (newlineEnds && depth <= 0 && started && last is not ('+' or ',' or '='))
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
