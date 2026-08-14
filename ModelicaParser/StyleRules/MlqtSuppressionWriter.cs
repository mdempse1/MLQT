using Antlr4.Runtime.Misc;
using ModelicaParser.Helpers;

namespace ModelicaParser.StyleRules;

/// <summary>
/// Adds or merges a <c>__MLQT(suppress="…")</c> vendor annotation onto a class or one of its
/// components. Operates on a single class's source (the extracted class body). The caller should
/// persist the result through a path that re-parses/validates (e.g. the MCP ClassBodyEditor), so a
/// malformed splice is caught rather than written.
/// </summary>
public static class MlqtSuppressionWriter
{
    /// <summary>
    /// Returns the class source with <paramref name="ruleId"/> suppressed on the class (when
    /// <paramref name="component"/> is null) or on that component. Merges into an existing annotation,
    /// an existing <c>__MLQT</c>, or an existing <c>suppress</c> list rather than duplicating.
    /// </summary>
    public static bool TryAddSuppression(
        string classCode, string? component, string ruleId, string? reason,
        out string newCode, out string? error)
    {
        newCode = classCode;
        error = null;

        var tree = ModelicaParserHelper.Parse(classCode);
        if (tree is null)
        {
            error = "could not parse the class source";
            return false;
        }

        var locator = new Locator(component);
        locator.Visit(tree);

        var target = component is null ? locator.ClassTarget : locator.ComponentTarget;
        if (target is null)
        {
            error = component is null
                ? "could not locate the class body"
                : $"component '{component}' was not found in the class";
            return false;
        }

        newCode = target.Apply(classCode, ruleId, reason);
        return true;
    }

    // The located edit target: where and how to add the directive.
    private sealed class Target
    {
        public bool IsComponent;
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

            // Existing annotation without __MLQT → add __MLQT as a new argument.
            if (AnnotationArgsStart is { } annAt)
                return code[..annAt] + Directive(ruleId, reason) + ", " + code[annAt..];

            // No annotation on the target → create one.
            return IsComponent
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
        private readonly string? _component;
        private int _classDepth;

        public Target? ClassTarget { get; private set; }
        public Target? ComponentTarget { get; private set; }

        public Locator(string? component) => _component = component;

        public override object? VisitClass_definition([NotNull] modelicaParser.Class_definitionContext context)
        {
            _classDepth++;
            if (_classDepth > 1) // only the outermost class of this extracted source is the target
            {
                _classDepth--;
                return null;
            }
            var r = base.VisitClass_definition(context);
            _classDepth--;
            return r;
        }

        public override object? VisitComposition([NotNull] modelicaParser.CompositionContext context)
        {
            if (_component is null && _classDepth == 1 && ClassTarget is null)
            {
                var target = new Target { InsertOffset = context.Stop.StopIndex + 1 };
                var annotations = context.annotation();
                FillFromAnnotation(target, annotations.Length > 0 ? annotations[0] : null);
                ClassTarget = target;
            }
            return base.VisitComposition(context);
        }

        public override object? VisitComponent_declaration([NotNull] modelicaParser.Component_declarationContext context)
        {
            if (_component is not null && _classDepth == 1 && ComponentTarget is null &&
                StripQuotes(context.declaration()?.IDENT()?.GetText() ?? "") == _component)
            {
                var target = new Target { IsComponent = true, InsertOffset = context.Stop.StopIndex + 1 };
                FillFromAnnotation(target, context.comment()?.annotation());
                ComponentTarget = target;
            }
            return base.VisitComponent_declaration(context);
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
