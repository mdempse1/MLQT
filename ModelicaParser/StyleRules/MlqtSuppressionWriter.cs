using Antlr4.Runtime.Misc;
using ModelicaParser.Helpers;

namespace ModelicaParser.StyleRules;

/// <summary>
/// Adds or merges a <c>__MLQT(suppress="…")</c> — or <c>__MLQT(spelling="…")</c> — vendor annotation
/// onto a class or one of its components. Can operate either on a single class's source (the extracted class body) or on a
/// whole file's source with a path to the nested class to target — the latter avoids any
/// substring matching, so it is robust when a nested class's stored slice is not a verbatim
/// substring of its containing file (e.g. re-indented on extraction). Handles long classes,
/// short class definitions (<c>type X = …</c>) and der classes. The caller should persist the
/// result through a path that re-parses/validates (e.g. the MCP ClassBodyEditor), so a malformed
/// splice is caught rather than written.
/// </summary>
public static class MlqtSuppressionWriter
{
    /// <summary>
    /// Suppress <paramref name="ruleId"/> on the outermost class of <paramref name="classCode"/>
    /// (when <paramref name="component"/> is null) or on that component.
    /// </summary>
    public static bool TryAddSuppression(
        string classCode, string? component, string ruleId, string? reason,
        out string newCode, out string? error)
        => TryAddSuppression(classCode, classPath: null, component, ruleId, reason, out newCode, out error);

    /// <summary>
    /// Accept <paramref name="word"/> as spelled correctly in the class located by
    /// <paramref name="classPath"/> within a whole <em>file's</em> text, through
    /// <c>__MLQT(spelling="…")</c>. Scoped to the one word rather than to the spelling rule, so
    /// everything else in the class is still checked.
    /// </summary>
    public static bool TryAddSpellingExceptionToFile(
        string fileContent, string[]? classPath, string word, string? reason,
        out string newContent, out string? error)
    {
        if (!IsRecordableWord(word))
        {
            newContent = fileContent;
            error = "the word cannot be recorded in an annotation (it is empty, or contains a quote or comma)";
            return false;
        }

        return EditFile(fileContent, classPath, component: null, SpellingArgument, word.Trim(), reason,
            out newContent, out error);
    }

    /// <summary>
    /// Accept <paramref name="word"/> as spelled correctly in the outermost class of
    /// <paramref name="classCode"/>.
    /// </summary>
    public static bool TryAddSpellingException(
        string classCode, string word, string? reason, out string newCode, out string? error)
    {
        if (!IsRecordableWord(word))
        {
            newCode = classCode;
            error = "the word cannot be recorded in an annotation (it is empty, or contains a quote or comma)";
            return false;
        }

        return Edit(classCode, classPath: null, component: null, SpellingArgument, word.Trim(), reason,
            out newCode, out error);
    }

    // The list is a comma-separated Modelica string, so a word carrying a quote or a comma would
    // either break the file or silently split into two entries.
    private static bool IsRecordableWord(string word)
        => !string.IsNullOrWhiteSpace(word) && !word.Contains('"') && !word.Contains(',');

    /// <summary>
    /// Suppress <paramref name="ruleId"/> on a class within a whole <em>file's</em> text, preserving the
    /// file's existing line endings and trailing content so the on-disk change is minimal — only the
    /// inserted annotation, not a whole-file line-ending/trailing rewrite. Use this when writing back to
    /// a source file (the editing splice itself works in LF, then the original CRLF/LF style is restored).
    /// </summary>
    public static bool TryAddSuppressionToFile(
        string fileContent, string[]? classPath, string? component, string ruleId, string? reason,
        out string newContent, out string? error)
        => EditFile(fileContent, classPath, component, SuppressArgument, ruleId, reason, out newContent, out error);

    /// <summary>
    /// Make the class-level <c>suppress</c> list of the class located by <paramref name="classPath"/>
    /// exactly <paramref name="entries"/>, in a whole <em>file's</em> text — for an editor that turns
    /// rules on and off, where adding one at a time cannot take one away.
    ///
    /// <para>An empty list removes the argument, and tidies up as removing <c>format=false</c> does:
    /// an <c>__MLQT</c> left holding only its <c>reason</c> goes too, since a reason for nothing is not
    /// something anyone would write. A non-empty <paramref name="reason"/> replaces the class's
    /// reason; a blank one leaves an existing reason where it is.</para>
    ///
    /// <para>A file with a syntax error is refused, and so is a result that has one: the result is
    /// re-parsed before it is returned. The file's line endings are kept.</para>
    /// </summary>
    public static bool TrySetClassSuppressionsToFile(
        string fileContent, string[]? classPath, IReadOnlyCollection<string> entries, string? reason,
        out string newContent, out string? error)
    {
        newContent = fileContent;
        error = null;

        // No backslash either: no rule id has one, and the list is read back without unescaping.
        if (entries.FirstOrDefault(e => !IsRecordableWord(e) || e.Contains('\\')) is { } bad)
        {
            error = $"'{bad}' cannot be recorded in an annotation (it is empty, or contains a quote, comma or backslash)";
            return false;
        }

        var usedCrlf = fileContent.Contains("\r\n");
        var code = fileContent.Replace("\r\n", "\n").Replace("\r", "\n");

        // Errors, not a null tree: the parser recovers from a syntax error and still returns one,
        // whose offsets are no basis for a splice into the user's file.
        if (ModelicaParserHelper.ParseWithErrors(code).errors.Count > 0)
        {
            error = "could not parse the source";
            return false;
        }

        // The list, then its reason: set beside a list, or dropped with the last entry.
        var list = entries.Count == 0 ? null : string.Join(",", entries.Select(e => e.Trim()));
        if (!TrySetArgument(ref code, classPath, SuppressArgument, list, out error)
            || (list is not null && !string.IsNullOrWhiteSpace(reason)
                && !TrySetArgument(ref code, classPath, ReasonArgument, reason.Trim(), out error))
            || (list is null && !TryDropLoneReason(ref code, classPath, out error)))
            return false;

        if (ModelicaParserHelper.ParseWithErrors(code).errors.Count > 0)
        {
            error = "the change would have left the file unparseable, so it was not made";
            return false;
        }

        newContent = usedCrlf ? code.Replace("\n", "\r\n") : code;
        return true;
    }

    /// <summary>
    /// Sets a quoted <c>__MLQT</c> argument on the class to <paramref name="value"/>, or removes it
    /// when that is null. Works in LF.
    /// </summary>
    private static bool TrySetArgument(
        ref string code, string[]? classPath, string argument, string? value, out string? error)
    {
        if (!TryLocateClass(code, classPath, argument, out var target, out error))
            return false;

        if (value is null)
        {
            code = target.Removal(code) ?? code;
            return true;
        }

        if (target.ValueSpan is { } span)
        {
            code = code[..span.Start] + Quoted(value) + code[(span.Stop + 1)..];
            return true;
        }

        code = target.Apply(code, argument, Escaped(value), reason: null);
        return true;
    }

    /// <summary>Removes the class's <c>reason</c> when it is all its <c>__MLQT</c> still holds.</summary>
    private static bool TryDropLoneReason(ref string code, string[]? classPath, out string? error)
    {
        if (!TryLocateClass(code, classPath, ReasonArgument, out var target, out error))
            return false;

        if (target.ArgumentSpan is not null && target.MlqtArgCount == 1)
            code = target.Removal(code) ?? code;
        return true;
    }

    private static bool TryLocateClass(
        string code, string[]? classPath, string argument, out Target target, out string? error)
    {
        target = null!;
        error = null;

        var locator = new Locator(classPath, component: null, argument);
        locator.Visit(ModelicaParserHelper.Parse(code));
        if (locator.ClassTarget is not { } found)
        {
            error = classPath is { Length: > 0 }
                ? $"could not locate the class '{string.Join('.', classPath)}' in the source"
                : "could not locate the class body";
            return false;
        }

        target = found;
        return true;
    }

    // A Modelica string escapes a backslash as well as a quote: `"a\"` is an unterminated string.
    private static string Escaped(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static string Quoted(string value) => "\"" + Escaped(value) + "\"";

    private const string SuppressArgument = "suppress";
    private const string ReasonArgument = "reason";
    private const string SpellingArgument = "spelling";
    private const string FormatArgument = "format";

    /// <summary>
    /// Write <c>__MLQT(format=false)</c> onto a class in a whole <em>file's</em> text — the
    /// rename-safe way to take a class out of formatting, and the one the documentation steers
    /// people to (B175). The name list in <c>StyleCheckingSettings.FormattingExcludedModels</c> does
    /// the same job and does not survive the class being renamed or moved.
    ///
    /// <para><b>A bare directive, not a list.</b> <c>suppress</c> and <c>spelling</c> accumulate
    /// comma-separated entries inside one quoted string; this is a boolean written unquoted, so it
    /// merges into an existing <c>__MLQT</c> as a new argument and is never appended to. A class
    /// that already carries it is left exactly as it is rather than gaining a second copy.</para>
    /// </summary>
    public static bool TryAddFormattingOptOutToFile(
        string fileContent, string[]? classPath, out string newContent, out string? error)
        => EditFile(fileContent, classPath, component: null, FormatArgument, "false", reason: null,
            out newContent, out error);

    /// <summary>
    /// Remove <c>format=false</c> from a class, putting it back under the formatter — the other half
    /// of the toggle, which the suppression writer had no need for until now.
    ///
    /// <para>It tidies up after itself: an <c>__MLQT</c> left with no arguments goes, and an
    /// annotation left with no arguments goes with it, along with the <c>;</c> that terminated it
    /// when it was a class-body annotation. The alternative is leaving <c>annotation(__MLQT());</c>
    /// behind in the user's file — legal, and something no one would write on purpose.</para>
    ///
    /// <para>The result is re-parsed before it is returned. This rewrites a file rather than adding
    /// to it, so a splice that produced something unparseable would replace a class's source with
    /// broken text; failing here means the file is not written at all.</para>
    /// </summary>
    public static bool TryRemoveFormattingOptOutFromFile(
        string fileContent, string[]? classPath, out string newContent, out string? error)
    {
        newContent = fileContent;
        error = null;

        var usedCrlf = fileContent.Contains("\r\n");
        var lf = fileContent.Replace("\r\n", "\n").Replace("\r", "\n");

        var tree = ModelicaParserHelper.Parse(lf);
        if (tree is null)
        {
            error = "could not parse the source";
            return false;
        }

        var locator = new Locator(classPath, component: null, FormatArgument);
        locator.Visit(tree);

        if (locator.ClassTarget is not { } target)
        {
            error = classPath is { Length: > 0 }
                ? $"could not locate the class '{string.Join('.', classPath)}' in the source"
                : "could not locate the class body";
            return false;
        }

        if (target.Removal(lf) is not { } newLf)
            return true;   // Not there: nothing to remove, and nothing to report either.

        if (ModelicaParserHelper.Parse(newLf) is null)
        {
            error = "removing the annotation would have left the file unparseable, so it was not changed";
            return false;
        }

        newContent = usedCrlf ? newLf.Replace("\n", "\r\n") : newLf;
        return true;
    }

    private static bool EditFile(
        string fileContent, string[]? classPath, string? component, string argument, string value, string? reason,
        out string newContent, out string? error)
    {
        newContent = fileContent;

        // Edit in LF (the parser and the splice offsets work in LF), then restore the file's style.
        var usedCrlf = fileContent.Contains("\r\n");
        var lf = fileContent.Replace("\r\n", "\n").Replace("\r", "\n");

        if (!Edit(lf, classPath, component, argument, value, reason, out var newLf, out error))
            return false;

        newContent = usedCrlf ? newLf.Replace("\n", "\r\n") : newLf;
        return true;
    }

    /// <summary>
    /// Suppress <paramref name="ruleId"/> on a class located by <paramref name="classPath"/> within
    /// <paramref name="sourceCode"/> (each segment names a nested class, relative to the outermost
    /// class; <c>null</c>/empty targets the outermost class itself), or on that class's
    /// <paramref name="component"/>. Merges into an existing annotation, an existing <c>__MLQT</c>,
    /// or an existing <c>suppress</c> list rather than duplicating.
    /// </summary>
    public static bool TryAddSuppression(
        string sourceCode, string[]? classPath, string? component, string ruleId, string? reason,
        out string newCode, out string? error)
        => Edit(sourceCode, classPath, component, SuppressArgument, ruleId, reason, out newCode, out error);

    private static bool Edit(
        string sourceCode, string[]? classPath, string? component, string argument, string value, string? reason,
        out string newCode, out string? error)
    {
        newCode = sourceCode;
        error = null;

        var tree = ModelicaParserHelper.Parse(sourceCode);
        if (tree is null)
        {
            error = "could not parse the source";
            return false;
        }

        var locator = new Locator(classPath, component, argument);
        locator.Visit(tree);

        if (locator.ClassTarget is null)
        {
            error = classPath is { Length: > 0 }
                ? $"could not locate the class '{string.Join('.', classPath)}' in the source"
                : "could not locate the class body";
            return false;
        }

        var target = component is null ? locator.ClassTarget : locator.ComponentTarget;
        if (target is null)
        {
            error = $"component '{component}' was not found in the class";
            return false;
        }

        newCode = target.Apply(sourceCode, argument, value, reason);
        return true;
    }

    // The located edit target: where and how to add the directive.
    private sealed class Target
    {
        public bool Inline;                  // insert " annotation(…)" inline (component / short class) vs a new class-body line
        public int InsertOffset;             // create a brand-new annotation here (no annotation yet)
        public int? AnnotationArgsStart;     // start of an existing annotation's argument list (no __MLQT yet)
        public int? MlqtArgsStart;           // start of an existing __MLQT argument list (argument not there yet)
        public int? ValueStop;               // stop index of an existing suppress/spelling value (append here)
        public (int Start, int Stop)? ValueSpan;       // the whole existing value, quotes included

        // Spans, for removing an argument again rather than adding one (B175).
        public (int Start, int Stop)? ArgumentSpan;    // the whole `format=false` argument
        public (int Start, int Stop)? MlqtSpan;        // the whole `__MLQT(…)` argument
        public (int Start, int Stop)? AnnotationSpan;  // the whole `annotation(…)`, without its `;`
        public int MlqtArgCount;
        public int AnnotationArgCount;

        public string Apply(string code, string argument, string value, string? reason)
        {
            // A bare directive (`format=false`) is a value, not a list: if it is already there the
            // class already says what we are about to say, so leave the source alone rather than
            // appending into the middle of `false`.
            if (IsBare(argument))
            {
                if (ArgumentSpan is not null)
                    return code;
            }
            // Existing list → append the entry before its closing quote.
            else if (ValueStop is { } stop)
                return code[..stop] + "," + value + code[stop..];

            // Existing __MLQT without this argument → add it.
            if (MlqtArgsStart is { } mlqtAt)
                return code[..mlqtAt] + Assignment(argument, value) + ", " + code[mlqtAt..];

            // Existing annotation without __MLQT → add __MLQT as a new argument. When the annotation is
            // laid out multi-line (the first argument sits on its own indented line), put __MLQT on its
            // own line with the same indentation; otherwise keep it inline.
            if (AnnotationArgsStart is { } annAt)
            {
                var lineStart = code.LastIndexOf('\n', annAt - 1) + 1;
                var indent = code[lineStart..annAt];
                var separator = indent.Length > 0 && indent.All(char.IsWhiteSpace) ? ",\n" + indent : ", ";
                return code[..annAt] + Directive(argument, value, reason) + separator + code[annAt..];
            }

            // No annotation on the target → create one.
            return Inline
                ? code[..InsertOffset] + " annotation(" + Directive(argument, value, reason) + ")" + code[InsertOffset..]
                : code[..InsertOffset] + "\n  annotation(" + Directive(argument, value, reason) + ");" + code[InsertOffset..];
        }

        /// <summary>A directive whose value is written unquoted, because it is not a list of names.</summary>
        private static bool IsBare(string argument) => argument == FormatArgument;

        private static string Assignment(string argument, string value)
            => IsBare(argument) ? $"{argument}={value}" : $"{argument}=\"{value}\"";

        private static string Directive(string argument, string value, string? reason)
            => string.IsNullOrWhiteSpace(reason)
                ? $"__MLQT({Assignment(argument, value)})"
                : $"__MLQT({Assignment(argument, value)}, reason=\"{reason.Replace("\"", "\\\"")}\")";

        /// <summary>
        /// <paramref name="code"/> with the located argument removed, or null when it is not there.
        ///
        /// <para>Removal widens as each container empties: the argument alone while <c>__MLQT</c>
        /// still holds something else, the whole <c>__MLQT(…)</c> when it does not, and the whole
        /// annotation when that was all the annotation held. Each step takes the separator with it,
        /// which is what stops the result being <c>annotation(Icon(…), )</c>.</para>
        /// </summary>
        public string? Removal(string code)
        {
            if (ArgumentSpan is not { } arg)
                return null;

            // Still something else inside __MLQT: take just this argument and one separator.
            if (MlqtArgCount > 1)
                return Splice(code, arg.Start, arg.Stop);

            if (MlqtSpan is not { } mlqt)
                return Splice(code, arg.Start, arg.Stop);

            // __MLQT held nothing else, and neither did the annotation: the annotation goes.
            if (AnnotationArgCount <= 1 && AnnotationSpan is { } annotation)
                return RemoveAnnotation(code, annotation);

            return Splice(code, mlqt.Start, mlqt.Stop);
        }

        /// <summary>
        /// Cuts <c>[start..stop]</c> together with the comma that joins it to its neighbours —
        /// the one that follows it, or failing that the one before it, so removing the last item in
        /// a list does not leave a trailing comma.
        /// </summary>
        private static string Splice(string code, int start, int stop)
        {
            var end = stop + 1;
            while (end < code.Length && char.IsWhiteSpace(code[end])) end++;
            if (end < code.Length && code[end] == ',')
            {
                end++;
                while (end < code.Length && (code[end] == ' ' || code[end] == '\t')) end++;
                if (end < code.Length && code[end] == '\n') end++;
                return code[..start] + code[end..];
            }

            var from = start;
            while (from > 0 && char.IsWhiteSpace(code[from - 1])) from--;
            if (from > 0 && code[from - 1] == ',')
                return code[..(from - 1)] + code[(stop + 1)..];

            return code[..start] + code[(stop + 1)..];
        }

        /// <summary>
        /// Removes a whole annotation. A class-body annotation is a statement, so its terminating
        /// <c>;</c> and the blank line it sat on go with it; an inline one belongs to an element
        /// whose <c>;</c> is not the annotation's to take.
        /// </summary>
        private string RemoveAnnotation(string code, (int Start, int Stop) annotation)
        {
            var end = annotation.Stop + 1;

            if (!Inline)
            {
                while (end < code.Length && (code[end] == ' ' || code[end] == '\t')) end++;
                if (end < code.Length && code[end] == ';') end++;
            }

            var start = annotation.Start;
            while (start > 0 && (code[start - 1] == ' ' || code[start - 1] == '\t')) start--;
            if (!Inline && start > 0 && code[start - 1] == '\n')
                start--;

            return code[..start] + code[end..];
        }
    }

    private sealed class Locator : modelicaBaseVisitor<object?>
    {
        private readonly string _targetRelative;      // "" = outermost class; "A.B" = nested path
        private readonly string? _component;
        private readonly string _argument;
        private readonly List<string> _names = new(); // class-name stack, outermost first
        private bool _found;
        private bool _inTarget;                        // directly inside the target class (for components)

        public Target? ClassTarget { get; private set; }
        public Target? ComponentTarget { get; private set; }

        public Locator(string[]? classPath, string? component, string argument)
        {
            _targetRelative = classPath is null ? "" : string.Join('.', classPath);
            _component = component;
            _argument = argument;
        }

        public override object? VisitClass_definition([NotNull] modelicaParser.Class_definitionContext context)
        {
            var name = ClassName(context);
            if (name is null)
                return base.VisitClass_definition(context);

            _names.Add(name);
            // Relative path of this class = the name stack below the outermost class.
            var relative = string.Join('.', _names.Skip(1));
            var isTarget = !_found && relative == _targetRelative;
            if (isTarget)
            {
                _found = true;
                CaptureClassTarget(context.class_specifier());
            }

            var prevInTarget = _inTarget;
            _inTarget = isTarget;              // components matched only when directly in the target class
            base.VisitClass_definition(context);
            _inTarget = prevInTarget;

            _names.RemoveAt(_names.Count - 1);
            return null;
        }

        public override object? VisitComponent_declaration([NotNull] modelicaParser.Component_declarationContext context)
        {
            if (_inTarget && _component is not null && ComponentTarget is null &&
                StripQuotes(context.declaration()?.IDENT()?.GetText() ?? "") == _component)
            {
                var target = new Target { Inline = true, InsertOffset = context.Stop.StopIndex + 1 };
                FillFromAnnotation(target, context.comment()?.annotation(), _argument);
                ComponentTarget = target;
            }
            return base.VisitComponent_declaration(context);
        }

        private void CaptureClassTarget(modelicaParser.Class_specifierContext cs)
        {
            if (cs.long_class_specifier() is { } lng)
            {
                var composition = lng.composition();
                var target = new Target { Inline = false, InsertOffset = composition.Stop.StopIndex + 1 };
                // The class's own annotation: the trailing one, else a leading one - never the
                // external clause's, which is the last in the composition when the class has no
                // trailing annotation (B446).
                FillFromAnnotation(target, CompositionAnnotations.Of(composition).Class, _argument);
                ClassTarget = target;
            }
            else
            {
                // Short class (type X = …) or der class: the annotation lives in the trailing comment,
                // appended inline just before the element's terminating ';'.
                var comment = cs.short_class_specifier()?.comment() ?? cs.der_class_specifier()?.comment();
                var target = new Target { Inline = true, InsertOffset = cs.Stop.StopIndex + 1 };
                FillFromAnnotation(target, comment?.annotation(), _argument);
                ClassTarget = target;
            }
        }

        private static string? ClassName(modelicaParser.Class_definitionContext context)
        {
            var cs = context.class_specifier();
            if (cs.long_class_specifier() is { } lng)
                return lng.IDENT() is { Length: > 0 } ids ? ids[0].GetText() : null;
            if (cs.short_class_specifier() is { } sht)
                return sht.IDENT()?.GetText();
            if (cs.der_class_specifier() is { } der)
                return der.IDENT() is { Length: > 0 } ids2 ? ids2[0].GetText() : null;
            return null;
        }

        private static void FillFromAnnotation(
            Target target, modelicaParser.AnnotationContext? annotation, string argument)
        {
            if (annotation is not null)
                target.AnnotationSpan = (annotation.Start.StartIndex, annotation.Stop.StopIndex);

            var args = annotation?.class_modification()?.argument_list();
            if (args is null)
                return;

            target.AnnotationArgsStart = args.Start.StartIndex;
            target.AnnotationArgCount = args.argument().Length;

            foreach (var arg in args.argument())
            {
                var elemMod = arg.element_modification_or_replaceable()?.element_modification();
                if (elemMod?.name()?.GetText() != "__MLQT")
                    continue;

                target.MlqtSpan = (arg.Start.StartIndex, arg.Stop.StopIndex);

                var mlqtArgs = elemMod.modification()?.class_modification()?.argument_list();
                target.MlqtArgsStart = mlqtArgs?.Start.StartIndex;
                target.MlqtArgCount = mlqtArgs?.argument().Length ?? 0;

                foreach (var mlqtArg in mlqtArgs?.argument() ?? [])
                {
                    var m = mlqtArg.element_modification_or_replaceable()?.element_modification();
                    if (m?.name()?.GetText() != argument)
                        continue;

                    target.ArgumentSpan = (mlqtArg.Start.StartIndex, mlqtArg.Stop.StopIndex);

                    // The value expression's last token is the closing quote of "a,b".
                    var expr = m.modification()?.modification_expression();
                    if (expr is not null)
                    {
                        target.ValueStop = expr.Stop.StopIndex;
                        target.ValueSpan = (expr.Start.StartIndex, expr.Stop.StopIndex);
                    }
                }
                return;
            }
        }

        private static string StripQuotes(string s)
            => s.Length >= 2 && s[0] == '\'' && s[^1] == '\'' ? s[1..^1] : s;
    }
}
