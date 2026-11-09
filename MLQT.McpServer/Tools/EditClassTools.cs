using System.ComponentModel;
using System.Reflection;
using System.Text.RegularExpressions;
using ModelContextProtocol.Server;
using ModelicaParser.Helpers;
using MLQT.McpServer.Dtos;
using MLQT.McpServer.Helpers;
using MLQT.McpServer.Services;
using MLQT.Services.Interfaces;

namespace MLQT.McpServer.Tools;

/// <summary>
/// <c>mlqt_edit_class</c>: every change to part of one class, in one call. Its <c>replace</c> operation is
/// the text patch an agent otherwise makes with its own file tools - in the 2026-10-18 model-building
/// sessions, scripted find-and-replace on <c>.mo</c> files was the largest single way round MLQT
/// (B503). The other operations are the element, documentation and suppression edits each done by an
/// mlqt_ tool of the same name, which this calls rather than repeats.
/// </summary>
[Obfuscation]
[McpServerToolType]
public sealed class EditClassTools
{
    private readonly ILibraryDataService _libraries;
    private readonly IExternalResourceService _resources;
    private readonly SessionState _session;
    private readonly StructureEditTools _structure;
    private readonly DocumentationTools _documentation;
    private readonly DiagramTools _diagram;
    private readonly SuppressionTools _suppression;
    private readonly SpellingTools _spelling;

    public EditClassTools(
        ILibraryDataService libraries,
        IRepositoryService repositories,
        ICustomDictionaryService customDictionary,
        IDictionaryManagerService dictionaryManager,
        IExternalResourceService resources,
        SessionState session)
    {
        _libraries = libraries;
        _resources = resources;
        _session = session;
        _structure = new StructureEditTools(libraries, resources, session);
        _documentation = new DocumentationTools(libraries, resources, session);
        _diagram = new DiagramTools(libraries, resources, session);
        _suppression = new SuppressionTools(libraries, resources, session);
        _spelling = new SpellingTools(libraries, repositories, customDictionary, dictionaryManager, resources, session);
    }

    /// <summary>Each operation and the fields it must be given; the error for a missing one lists them.</summary>
    internal static readonly IReadOnlyDictionary<string, string[]> Operations = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["replace"] = ["old", "new", "all?", "wholeWord?"],
        ["add_component"] = ["type", "name"],
        ["remove_component"] = ["name"],
        ["set_component_modifier"] = ["name", "modifier"],
        ["add_equation"] = ["equation"],
        ["add_connection"] = ["portA", "portB"],
        ["remove_connection"] = ["portA", "portB"],
        ["set_component_placement"] = ["name", "x1", "y1", "x2", "y2"],
        ["set_class_description"] = ["description"],
        ["set_component_description"] = ["name", "description"],
        ["set_class_documentation"] = ["info or revisions"],
        ["suppress_rule"] = ["ruleId"],
        ["accept_spelling"] = ["word"],
        ["correct_spelling"] = ["word", "newWord"],
    };

    [McpServerTool(Name = "mlqt_edit_class", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false)]
    [Description("Change part of one Modelica class with a list of operations, applied in order and all or none. " +
                 "The usual one is replace: {\"op\":\"replace\",\"old\":\"y = k*u;\",\"new\":\"y = k*u + offset;\"} " +
                 "swaps text that must occur exactly once in the class - copy it from mlqt_get_class's source view; " +
                 "text whose spacing or line breaks differ from the file (a formatter reflowed it) is found too, and " +
                 "so is text copied with the view's annotations hidden, whose annotations are kept where they were - " +
                 "the answer says when either happened; with \"all\":true it swaps every " +
                 "occurrence, and says how many; \"wholeWord\":true matches only where old is not part of a longer " +
                 "name (Ti, not the Ti in TipIn). To rename a component and every reference to it, in other classes " +
                 "too, use mlqt_refactor_class's rename_component instead. An extends clause, an import or " +
                 "an algorithm statement is a replace. The others: add_component{type,name,modifier?,description?," +
                 "visibility?,prefix?,condition?,constrainedBy?,comment?} (prefix: parameter, input, flow...; " +
                 "condition: the if-expression of a conditional component), remove_component{name} (takes its " +
                 "connections with it), set_component_modifier{name,modifier}, add_equation{equation,comment?}, " +
                 "add_connection{portA,portB,comment?} (refuses incompatible connectors, and draws the line), " +
                 "remove_connection{portA,portB}, " +
                 "set_component_placement{name,x1,y1,x2,y2,rotation?}, set_class_description{description}, " +
                 "set_component_description{name,description}, set_class_documentation{info?,revisions?}, " +
                 "suppress_rule{ruleId,component?,reason?}, accept_spelling{word,reason?} (accepts it in this class " +
                 "and those nested in it; for every class of the repository, mlqt_style_settings' acceptSpellings), " +
                 "correct_spelling{word,newWord}. Each result is parse-checked before it is written; if any " +
                 "operation fails, nothing is changed. preview=true returns the file without keeping it.")]
    public async Task<object> EditClass(
        [Description("Fully-qualified id of the class to change.")] string classId,
        [Description("The operations, in order. Each is {op, ...its fields}.")]
        IReadOnlyList<ClassEditOperation> operations,
        [Description("Apply, return the resulting file and undo it. Default false.")] bool preview = false)
    {
        // First, as every editing tool does: a read-only class is refused whatever was asked of it.
        if (ReadOnlyClassGuard.Refuse(_libraries, classId, "edit this class") is { } readOnlyClass)
            return readOnlyClass;
        if (operations is null || operations.Count == 0)
            return new ToolError("operations must be a non-empty array.");

        // Everything an operation lacks is reported before anything is written.
        for (var i = 0; i < operations.Count; i++)
        {
            var name = OpName(operations[i].Op);
            if (!Operations.TryGetValue(name, out var fields))
                return new ToolError($"Operation {i} has unknown op '{operations[i].Op}'. " +
                                     $"Valid ops: {string.Join(", ", Operations.Keys)}.");
            if (Missing(name, operations[i]) is { } missing)
                return new ToolError($"Operation {i} ({name}) needs {missing}. It takes: {string.Join(", ", fields)}.");
        }

        var (ctx, error) = ClassBodyEditor.Open(_libraries, classId);
        if (error is not null)
            return error;
        var snapshots = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        await EditRollback.SnapshotAsync(snapshots, ctx!.FilePath);

        var notes = new List<string>();
        // Formatted once, after the last operation, not after each: each operation re-opens the class
        // from the file, and one whose text an earlier operation's format had changed - a `replace` of
        // `k=2` written as `k = 2` - would not find it (EditFormatting.Defer).
        using (EditFormatting.Defer())
        {
            for (var i = 0; i < operations.Count; i++)
            {
                var result = await Dispatch(classId, operations[i]);
                if (result is ToolError te)
                {
                    await EditRollback.RestoreAsync(snapshots, _libraries, _resources, _session);
                    return new ToolError($"Operation {i} ({OpName(operations[i].Op)}) failed: {te.Error} Nothing was changed.");
                }
                if (result is StructureEditResult { Note: { Length: > 0 } note })
                    notes.Add($"Operation {i}: {note}");
            }
        }

        if (await EditFormatting.FormatFileNowAsync(_libraries, _resources, _session, classId) is ToolError formatError)
        {
            await EditRollback.RestoreAsync(snapshots, _libraries, _resources, _session);
            return new ToolError($"{formatError.Error} Nothing was changed.");
        }

        string? content = null;
        if (preview)
        {
            content = ModelicaFileEncoding.ReadAllTextOnly(ctx.FilePath);
            await EditRollback.RestoreAsync(snapshots, _libraries, _resources, _session);
        }
        return new EditClassResult(classId, ctx.FilePath, preview, operations.Count, content, notes);
    }

    // The ops were tools of their own until the 16-tool set (B503), and an agent that has seen one may
    // still write its name, so an mlqt_ prefix is dropped, and accept_spelling_in_class is accept_spelling.
    private static string OpName(string? op)
    {
        var name = op is not null && op.StartsWith("mlqt_", StringComparison.Ordinal) ? op["mlqt_".Length..] : op ?? string.Empty;
        return name == "accept_spelling_in_class" ? "accept_spelling" : name;
    }

    private static string? Missing(string op, ClassEditOperation o)
    {
        static bool Blank(string? s) => string.IsNullOrWhiteSpace(s);
        var missing = op switch
        {
            "replace" => string.IsNullOrEmpty(o.Old) ? "old" : o.New is null ? "new" : null,
            "add_component" => Blank(o.Type) ? "type" : Blank(o.Name) ? "name" : null,
            "remove_component" => Blank(o.Name) ? "name" : null,
            "set_component_modifier" => Blank(o.Name) ? "name" : o.Modifier is null ? "modifier" : null,
            "add_equation" => Blank(o.Equation) ? "equation" : null,
            "add_connection" or "remove_connection" => Blank(o.PortA) ? "portA" : Blank(o.PortB) ? "portB" : null,
            "set_component_placement" => Blank(o.Name) ? "name"
                : o.X1 is null || o.Y1 is null || o.X2 is null || o.Y2 is null ? "x1, y1, x2 and y2" : null,
            "set_class_description" => Blank(o.Description) ? "description" : null,
            "set_component_description" => Blank(o.Name) ? "name" : Blank(o.Description) ? "description" : null,
            "set_class_documentation" => o.Info is null && o.Revisions is null ? "info or revisions" : null,
            "suppress_rule" => Blank(o.RuleId) ? "ruleId" : null,
            "accept_spelling" => Blank(o.Word) ? "word" : null,
            "correct_spelling" => Blank(o.Word) ? "word" : string.IsNullOrEmpty(o.NewWord) ? "newWord" : null,
            _ => null,
        };
        return missing;
    }

    private Task<object> Dispatch(string classId, ClassEditOperation o) => OpName(o.Op) switch
    {
        "replace" => Replace(classId, o.Old!, o.New!, o.All == true, o.WholeWord == true),
        "add_component" => _structure.AddComponent(classId, o.Type!, o.Name!, o.Modifier, o.Description, o.Comment,
            o.Visibility ?? "public", o.Prefix, o.ConstrainedBy, o.Condition),
        "remove_component" => _structure.RemoveComponent(classId, o.Name!),
        "set_component_modifier" => _structure.SetComponentModifier(classId, o.Name!, o.Modifier!),
        "add_equation" => _structure.AddEquation(classId, o.Equation!, o.Comment),
        "add_connection" => _structure.AddConnection(classId, o.PortA!, o.PortB!, o.Comment),
        "remove_connection" => _structure.RemoveConnection(classId, o.PortA!, o.PortB!),
        "set_component_placement" => _diagram.SetComponentPlacement(classId, o.Name!, o.X1!.Value, o.Y1!.Value,
            o.X2!.Value, o.Y2!.Value, o.Rotation ?? 0),
        "set_class_description" => _documentation.SetClassDescription(classId, o.Description!),
        "set_component_description" => _documentation.SetComponentDescription(classId, o.Name!, o.Description!),
        "set_class_documentation" => _documentation.SetClassDocumentation(classId, o.Info, o.Revisions),
        "suppress_rule" => _suppression.SuppressRule(classId, o.RuleId!, o.Component, o.Reason),
        "accept_spelling" => _suppression.AcceptSpellingInClass(classId, o.Word!, o.Reason),
        "correct_spelling" => _spelling.CorrectSpelling(classId, o.Word!, o.NewWord!),
        _ => Task.FromResult<object>(new ToolError($"Unknown operation '{o.Op}'.")),
    };

    /// <summary>
    /// Replace the one occurrence of <paramref name="oldText"/> in the class's source, or with
    /// <paramref name="all"/> every occurrence: a name used throughout a class is otherwise renamed with a
    /// shell <c>sed</c>, as an agent did in the Dymola scenario (B503). Either way text that is not there
    /// is refused, and the result is parse-checked before it is written. Line endings are
    /// matched whichever the caller used, since an agent's text has the line feeds it was given and the
    /// file may have carriage returns too. Nothing else is loose: indentation and spacing must match, so a
    /// near miss is an error naming what to do rather than a guess about which text was meant.
    /// </summary>
    /// <param name="wholeWord">Match <paramref name="oldText"/> only where it is not part of a longer name:
    /// a replace-all of <c>Ti</c> was refused because it would also have renamed the class <c>TipIn</c>.</param>
    private async Task<object> Replace(string classId, string oldText, string newText, bool all, bool wholeWord = false)
    {
        var (ctx, error) = ClassBodyEditor.Open(_libraries, classId);
        if (error is not null)
            return error;

        var code = ctx!.ClassCode;
        var eol = code.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var find = Normalise(oldText, eol);
        var with = Normalise(newText, eol);
        if (find == with)
            return new ToolError("old and new are the same, so there is nothing to change.");

        // The text as written first. Only where that is nowhere, the same Modelica tokens with any white
        // space between them: formatting reflows lines and spaces operators, so text copied from a view
        // taken before a format - `x=1;`, one long declaration - was "not in the class" afterwards, and
        // the agent had to search the raw file and try again.
        var matcher = Matcher(Regex.Escape(find), find, wholeWord);
        var count = matcher.Matches(code).Count;
        var loose = false;
        if (count == 0 && WhiteSpaceTolerant(find) is { } tolerant)
        {
            matcher = Matcher(tolerant, find, wholeWord);
            count = matcher.Matches(code).Count;
            loose = count > 0;
        }

        // Last, text copied from the source view, which hides annotations by default: the same tokens
        // with any annotation(...) the file has between them stepped over, and each one put back into the
        // new text where it was - `parameter Real k = 1 "gain";` from the view is the file's
        // `parameter Real k = 1 "gain" annotation(Dialog(...));`, and replacing it keeps the Dialog.
        if (count == 0 && AnnotationTolerant(find) is var (annotationPattern, oldTokens))
        {
            var withAnnotations = Matcher(annotationPattern, find, wholeWord);
            var found = withAnnotations.Matches(code);
            if (found.Count > 0)
            {
                if (found.Count > 1 && !all)
                    return new ToolError(
                        $"The text to replace occurs {found.Count} times in '{classId}' once the annotations the source view " +
                        "hides are allowed for. Include more of the text around it, or pass all=true.");
                string? unplaceable = null;
                var replaced = withAnnotations.Replace(code, m =>
                {
                    if (WithAnnotationsPutBack(with, oldTokens, m) is { } placed)
                        return placed;
                    unplaceable ??= m.Value;
                    return m.Value;
                }, all ? -1 : 1);
                if (unplaceable is not null)
                    return new ToolError(
                        $"old matched '{classId}' only with the annotations the source view hides, and new changes the text on " +
                        "both sides of where one of them is written, so there is no saying where it goes. Copy the text with " +
                        $"mlqt_get_class's source view and includeAnnotations=true, and include the annotation in new. The file has: {unplaceable}");
                return await ApplyReplacement(classId, ctx, replaced, found.Count, all,
                    "old matched once the annotations mlqt_get_class's source view hides were allowed for; they were kept in " +
                    "place. To change an annotation, read the source with includeAnnotations=true.");
            }
        }

        if (count == 0)
            return new ToolError(
                $"The text to replace is not in '{classId}'{(wholeWord ? " as a whole word" : "")}, even ignoring " +
                "differences in white space and line breaks. Copy it from mlqt_get_class's source view with " +
                "includeAnnotations=true: by default that hides annotations, so its text is not all of the class. To " +
                "change a declaration, set_component_modifier, set_component_description or mlqt_refactor_class's " +
                "rename_component name it instead of quoting it.");
        if (count > 1 && !all)
            return new ToolError(
                $"The text to replace occurs {count} times in '{classId}'{(wholeWord ? " as a whole word" : "")}. Include " +
                "more of the text around it, so that it occurs once, or pass all=true to replace every occurrence." +
                (wholeWord ? "" : " wholeWord=true leaves it alone where it is part of a longer name."));

        var newCode = matcher.Replace(code, with.Replace("$", "$$", StringComparison.Ordinal), all ? -1 : 1);
        return await ApplyReplacement(classId, ctx, newCode, count, all,
            loose
                ? "old matched only when differences in white space and line breaks were ignored - the class's layout has " +
                  "changed since it was read (formatting reflows it); read it again before quoting more of it."
                : null);
    }

    /// <summary>Writes a replace's result, with the notes the answer carries.</summary>
    private async Task<object> ApplyReplacement(
        string classId, ClassEditContext ctx, string newCode, int count, bool all, string? matchNote)
    {
        // A changed name would leave the graph holding a class that is no longer in the file, and miss
        // every reference to it; that is what mlqt_refactor_class's rename is for.
        var oldName = ModelicaName.LeafOf(classId);
        var newName = ModelicaParserHelper.ExtractModels(newCode).FirstOrDefault()?.Name;
        if (newName is not null && newName != oldName)
            return new ToolError(
                $"The replacement would rename the class from '{oldName}' to '{newName}'. Use mlqt_refactor_class's rename, " +
                "which also changes every reference to it.");

        var applied = await ClassBodyEditor.ApplyAsync(_libraries, _resources, _session, ctx, newCode, preview: false, "edit the class");
        // How many, as a note on the answer: replacing more than the caller meant is the risk of all=true.
        // And how old was found when it was not as written, so the agent knows what its copy lacks.
        var notes = new List<string>();
        if (all)
            notes.Add($"Replaced {count} occurrence{(count == 1 ? "" : "s")}.");
        if (matchNote is not null)
            notes.Add(matchNote);
        return notes.Count > 0 && applied is ClassEditResult result
            ? new StructureEditResult(classId, result.FilePath, result.PreviewOnly, Changed: true, result.AffectedCount,
                result.NewFileContent, string.Join(" ", notes))
            : applied;
    }

    // An annotation as written: the keyword and its parenthesised content, brackets balanced, strings and
    // quoted names (which may hold brackets) matched whole.
    // A class's own annotation owns the `;` after it, which the source view hides with it; a declaration's
    // annotation does not, and backtracking leaves that `;` to the declaration.
    private const string AnnotationConstruct =
        """annotation\s*\((?>[^()"']+|"(?:[^"\\]|\\.)*"|'(?:[^'\\]|\\.)*'|\((?<depth>)|\)(?<-depth>))*(?(depth)(?!))\)(?:\s*;)?""";

    /// <summary>
    /// A pattern for <paramref name="find"/>'s tokens, any white space between them, and between each two
    /// an optional annotation the file may have there - captured as <c>a1</c>, <c>a2</c>... by the token it
    /// comes before - with the tokens themselves. Null when there is nothing to step over: one token, or
    /// text that writes annotations itself, which the source view did not hide.
    /// </summary>
    private static (string Pattern, IReadOnlyList<string> Tokens)? AnnotationTolerant(string find)
    {
        var tokens = Token.Matches(find).Select(m => m.Value).ToList();
        if (tokens.Count < 2 || tokens.Contains("annotation"))
            return null;

        var pattern = new System.Text.StringBuilder(Regex.Escape(tokens[0]));
        for (var i = 1; i < tokens.Count; i++)
        {
            var together = IsNameChar(tokens[i - 1][^1]) && IsNameChar(tokens[i][0]);
            pattern.Append($@"(?:\s*(?<a{i}>{AnnotationConstruct}))?")
                .Append(together ? @"\s+" : @"\s*")
                .Append(Regex.Escape(tokens[i]));
        }
        return (pattern.ToString(), tokens);
    }

    /// <summary>
    /// <paramref name="newText"/> with each annotation <paramref name="match"/> stepped over put back:
    /// before the same tokens that followed it in old, where new ends with them (an annotation sits before
    /// the <c>;</c> it belongs to), else after the same tokens that went before it. Null when new changed
    /// both, and there is no saying where it goes.
    /// </summary>
    private static string? WithAnnotationsPutBack(string newText, IReadOnlyList<string> oldTokens, Match match)
    {
        var newTokens = Token.Matches(newText).ToList();
        var inserts = new List<(int At, string Text)>();
        for (var i = 1; i < oldTokens.Count; i++)
        {
            if (match.Groups[$"a{i}"] is not { Success: true } annotation)
                continue;

            var after = oldTokens.Count - i;   // the old tokens from the one the annotation came before
            var before = i;                    // the old tokens up to it
            if (newTokens.Count >= after
                && newTokens.Skip(newTokens.Count - after).Select(t => t.Value).SequenceEqual(oldTokens.Skip(i)))
            {
                // Before a token that starts its line - `end M;` after a class annotation - on a line of its
                // own at that indentation; otherwise beside the text before it.
                var next = newTokens[newTokens.Count - after];
                var lineStart = newText.LastIndexOf('\n', Math.Max(next.Index - 1, 0)) + 1;
                var indent = newText[lineStart..next.Index];
                inserts.Add(lineStart > 0 && indent.All(c => c is ' ' or '\t')
                    ? (next.Index, annotation.Value + "\n" + indent)
                    : (next.Index, " " + annotation.Value));
            }
            else if (newTokens.Count >= before
                     && newTokens.Take(before).Select(t => t.Value).SequenceEqual(oldTokens.Take(i)))
            {
                var previous = newTokens[before - 1];
                inserts.Add((previous.Index + previous.Length, " " + annotation.Value));
            }
            else
            {
                return null;
            }
        }

        var result = newText;
        foreach (var (at, text) in inserts.OrderByDescending(x => x.At))
            result = result.Insert(at, text);
        return result;
    }

    // A matcher for `pattern`, which matches `find`: with wholeWord, not where the character before or after
    // would make it part of a longer name. Only an end that is itself part of a name is bounded, so old
    // text that starts or ends with punctuation still matches beside a name.
    private static Regex Matcher(string pattern, string find, bool wholeWord)
    {
        if (wholeWord && find.Length > 0 && IsNameChar(find[0]))
            pattern = $"(?<![A-Za-z0-9_]){pattern}";
        if (wholeWord && find.Length > 0 && IsNameChar(find[^1]))
            pattern = $"{pattern}(?![A-Za-z0-9_])";
        return new Regex(pattern, RegexOptions.CultureInvariant);
    }

    // Modelica's tokens, as far as white space is concerned: a string or quoted name (matched exactly, its
    // spaces being its content), a name, a number, or one other character.
    private static readonly Regex Token = new(
        """
        "(?:[^"\\]|\\.)*"|'(?:[^'\\]|\\.)*'|[A-Za-z_][A-Za-z0-9_]*|(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?|\S
        """,
        RegexOptions.CultureInvariant);

    /// <summary>
    /// A pattern for <paramref name="find"/>'s tokens with any white space between them - at least some
    /// between two names or numbers, which would otherwise run together - or null when it is one token,
    /// which the exact match already looked for.
    /// </summary>
    private static string? WhiteSpaceTolerant(string find)
    {
        var tokens = Token.Matches(find).Select(m => m.Value).ToList();
        if (tokens.Count < 2)
            return null;

        var pattern = new System.Text.StringBuilder(Regex.Escape(tokens[0]));
        for (var i = 1; i < tokens.Count; i++)
        {
            var together = IsNameChar(tokens[i - 1][^1]) && IsNameChar(tokens[i][0]);
            pattern.Append(together ? @"\s+" : @"\s*").Append(Regex.Escape(tokens[i]));
        }
        return pattern.ToString();
    }

    private static string Normalise(string text, string eol) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", eol, StringComparison.Ordinal);

    private static bool IsNameChar(char c) => char.IsAsciiLetterOrDigit(c) || c == '_';
}
