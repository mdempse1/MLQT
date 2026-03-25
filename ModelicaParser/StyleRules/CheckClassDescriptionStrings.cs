using Antlr4.Runtime.Misc;
using ModelicaParser.DataTypes;

namespace ModelicaParser.StyleRules;

/// <summary>
/// Visitor that checks all class definitions have description strings.
/// </summary>
public class CheckClassDescriptionStrings : VisitorWithModelNameTracking
{
    /// <summary>
    /// Creates a new instance with an optional base package prefix.
    /// </summary>
    /// <param name="basePackage">The package prefix to use when the code doesn't have a within clause</param>
    public CheckClassDescriptionStrings(string basePackage = "") : base(basePackage) { }

    public override object? VisitLong_class_specifier([NotNull] modelicaParser.Long_class_specifierContext context)
    {
        if (context.string_comment() == null || context.string_comment().GetText().Length == 0)
        {
            // For replaceable/redeclare elements, the description may be on the
            // element's constraining clause comment rather than the class specifier
            if (!HasElementLevelDescription(context))
                AddViolation(context.Start.Line, "The class is missing a description string");
        }
        return base.VisitLong_class_specifier(context);
    }

    public override object? VisitShort_class_specifier([NotNull] modelicaParser.Short_class_specifierContext context)
    {
        if (context.comment() == null || context.comment().string_comment() == null || context.comment().string_comment().GetText().Length == 0)
        {
            // For replaceable/redeclare elements, the description may be on the
            // element's constraining clause comment rather than the class specifier
            if (!HasElementLevelDescription(context))
                AddViolation(context.Start.Line, "The class is missing a description string");
        }
        return base.VisitShort_class_specifier(context);
    }

    public override object? VisitDer_class_specifier([NotNull] modelicaParser.Der_class_specifierContext context)
    {
        if (context.comment() == null || context.comment().string_comment() == null || context.comment().string_comment().GetText().Length == 0)
        {
            if (!HasElementLevelDescription(context))
                AddViolation(context.Start.Line, "The class is missing a description string");
        }
        return base.VisitDer_class_specifier(context);
    }

    /// <summary>
    /// Checks whether a class specifier's parent element has a description string
    /// on its constraining clause comment. This handles the Modelica pattern:
    /// <code>replaceable model X = Y constrainedby Z "description";</code>
    /// where the description is on the element, not the short class specifier.
    /// </summary>
    private static bool HasElementLevelDescription(Antlr4.Runtime.ParserRuleContext context)
    {
        // Walk up: *_class_specifier → class_specifier → class_definition → element
        var element = context.Parent?.Parent?.Parent as modelicaParser.ElementContext;
        if (element == null)
            return false;

        var comment = element.comment();
        return comment?.string_comment() != null && comment.string_comment().GetText().Length > 0;
    }
}
