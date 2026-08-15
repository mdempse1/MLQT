using Antlr4.Runtime.Misc;
using ModelicaParser.Helpers;

namespace ModelicaParser.StyleRules;

/// <summary>
/// Adds or merges a <c>__MLQT(suppress="…")</c> vendor annotation onto a class or one of its
/// components. Can operate either on a single class's source (the extracted class body) or on a
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
    /// Suppress <paramref name="ruleId"/> on a class located by <paramref name="classPath"/> within
    /// <paramref name="sourceCode"/> (each segment names a nested class, relative to the outermost
    /// class; <c>null</c>/empty targets the outermost class itself), or on that class's
    /// <paramref name="component"/>. Merges into an existing annotation, an existing <c>__MLQT</c>,
    /// or an existing <c>suppress</c> list rather than duplicating.
    /// </summary>
    public static bool TryAddSuppression(
        string sourceCode, string[]? classPath, string? component, string ruleId, string? reason,
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

        var locator = new Locator(classPath, component);
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

        newCode = target.Apply(sourceCode, ruleId, reason);
        return true;
    }

    // The located edit target: where and how to add the directive.
    private sealed class Target
    {
        public bool Inline;                  // insert " annotation(…)" inline (component / short class) vs a new class-body line
        public int InsertOffset;             // create a brand-new annotation here (no annotation yet)
        public int? AnnotationArgsStart;     // start of an existing annotation's argument list (no __MLQT yet)
        public int? MlqtArgsStart;           // start of an existing __MLQT argument list (no suppress yet)
        public int? SuppressValueStop;       // stop index of an existing suppress="…" value (append here)

        public string Apply(string code, string ruleId, string? reason)
        {
            // Existing suppress list → append the rule before its closing quote.
            if (SuppressValueStop is { } stop)
                return code[..stop] + "," + ruleId + code[stop..];

            // Existing __MLQT without a suppress arg → add one.
            if (MlqtArgsStart is { } mlqtAt)
                return code[..mlqtAt] + $"suppress=\"{ruleId}\", " + code[mlqtAt..];

            // Existing annotation without __MLQT → add __MLQT as a new argument. When the annotation is
            // laid out multi-line (the first argument sits on its own indented line), put __MLQT on its
            // own line with the same indentation; otherwise keep it inline.
            if (AnnotationArgsStart is { } annAt)
            {
                var lineStart = code.LastIndexOf('\n', annAt - 1) + 1;
                var indent = code[lineStart..annAt];
                var separator = indent.Length > 0 && indent.All(char.IsWhiteSpace) ? ",\n" + indent : ", ";
                return code[..annAt] + Directive(ruleId, reason) + separator + code[annAt..];
            }

            // No annotation on the target → create one.
            return Inline
                ? code[..InsertOffset] + " annotation(" + Directive(ruleId, reason) + ")" + code[InsertOffset..]
                : code[..InsertOffset] + "\n  annotation(" + Directive(ruleId, reason) + ");" + code[InsertOffset..];
        }

        private static string Directive(string ruleId, string? reason)
            => string.IsNullOrWhiteSpace(reason)
                ? $"__MLQT(suppress=\"{ruleId}\")"
                : $"__MLQT(suppress=\"{ruleId}\", reason=\"{reason.Replace("\"", "\\\"")}\")";
    }

    private sealed class Locator : modelicaBaseVisitor<object?>
    {
        private readonly string _targetRelative;      // "" = outermost class; "A.B" = nested path
        private readonly string? _component;
        private readonly List<string> _names = new(); // class-name stack, outermost first
        private bool _found;
        private bool _inTarget;                        // directly inside the target class (for components)

        public Target? ClassTarget { get; private set; }
        public Target? ComponentTarget { get; private set; }

        public Locator(string[]? classPath, string? component)
        {
            _targetRelative = classPath is null ? "" : string.Join('.', classPath);
            _component = component;
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
                FillFromAnnotation(target, context.comment()?.annotation());
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
                // The class's own annotation is the trailing one in the composition.
                var annotations = composition.annotation();
                FillFromAnnotation(target, annotations.Length > 0 ? annotations[^1] : null);
                ClassTarget = target;
            }
            else
            {
                // Short class (type X = …) or der class: the annotation lives in the trailing comment,
                // appended inline just before the element's terminating ';'.
                var comment = cs.short_class_specifier()?.comment() ?? cs.der_class_specifier()?.comment();
                var target = new Target { Inline = true, InsertOffset = cs.Stop.StopIndex + 1 };
                FillFromAnnotation(target, comment?.annotation());
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

        private static void FillFromAnnotation(Target target, modelicaParser.AnnotationContext? annotation)
        {
            var args = annotation?.class_modification()?.argument_list();
            if (args is null)
                return;

            target.AnnotationArgsStart = args.Start.StartIndex;

            foreach (var arg in args.argument())
            {
                var elemMod = arg.element_modification_or_replaceable()?.element_modification();
                if (elemMod?.name()?.GetText() != "__MLQT")
                    continue;

                var mlqtArgs = elemMod.modification()?.class_modification()?.argument_list();
                target.MlqtArgsStart = mlqtArgs?.Start.StartIndex;

                foreach (var mlqtArg in mlqtArgs?.argument() ?? [])
                {
                    var m = mlqtArg.element_modification_or_replaceable()?.element_modification();
                    if (m?.name()?.GetText() != "suppress")
                        continue;
                    // The suppress value expression's last token is the closing quote of "a,b".
                    var expr = m.modification()?.modification_expression();
                    if (expr is not null)
                        target.SuppressValueStop = expr.Stop.StopIndex;
                }
                return;
            }
        }

        private static string StripQuotes(string s)
            => s.Length >= 2 && s[0] == '\'' && s[^1] == '\'' ? s[1..^1] : s;
    }
}
