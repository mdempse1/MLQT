using Antlr4.Runtime.Misc;

namespace ModelicaParser.StyleRules;

/// <summary>
/// Visitor that checks class-level annotations for Documentation(info), Documentation(revisions), and Icon.
/// Only checks long_class_specifier classes (classes with a composition body).
/// </summary>
public class CheckClassAnnotations : VisitorWithModelNameTracking
{
    private readonly bool _checkDocumentationInfo;
    private readonly bool _checkDocumentationRevisions;
    private readonly bool _checkIcon;

    /// <summary>
    /// Creates a new instance with the specified checks and optional base package prefix.
    /// </summary>
    /// <param name="checkDocumentationInfo">Whether to check for Documentation(info=...)</param>
    /// <param name="checkDocumentationRevisions">Whether to check for Documentation(revisions=...)</param>
    /// <param name="checkIcon">Whether to check for Icon annotation</param>
    /// <param name="basePackage">The package prefix to use when the code doesn't have a within clause</param>
    public CheckClassAnnotations(bool checkDocumentationInfo, bool checkDocumentationRevisions, bool checkIcon, string basePackage = "")
        : base(basePackage)
    {
        _checkDocumentationInfo = checkDocumentationInfo;
        _checkDocumentationRevisions = checkDocumentationRevisions;
        _checkIcon = checkIcon;
    }

    public override object? VisitComposition([NotNull] modelicaParser.CompositionContext context)
    {
        bool hasDocInfo = false;
        bool hasDocRevisions = false;
        bool hasIcon = false;

        // Check all annotations in the composition (class-level and external)
        var annotations = context.annotation();
        foreach (var annotation in annotations)
        {
            CheckAnnotation(annotation, ref hasDocInfo, ref hasDocRevisions, ref hasIcon);
        }

        int line = context.Start.Line;
        if (_checkDocumentationInfo && !hasDocInfo)
            AddViolation(line, "The class is missing Documentation info");
        if (_checkDocumentationRevisions && !hasDocRevisions)
            AddViolation(line, "The class is missing Documentation revisions");
        if (_checkIcon && !hasIcon)
            AddViolation(line, "The class is missing an Icon annotation");

        return base.VisitComposition(context);
    }

    private static void CheckAnnotation(
        modelicaParser.AnnotationContext annotation,
        ref bool hasDocInfo,
        ref bool hasDocRevisions,
        ref bool hasIcon)
    {
        var classMod = annotation.class_modification();
        if (classMod == null) return;

        var argList = classMod.argument_list();
        if (argList == null) return;

        foreach (var arg in argList.argument())
        {
            var elemMod = arg.element_modification_or_replaceable()?.element_modification();
            if (elemMod == null) continue;

            var name = elemMod.name()?.GetText();
            if (name == "Documentation")
            {
                CheckDocumentationParameters(elemMod, ref hasDocInfo, ref hasDocRevisions);
            }
            else if (name == "Icon")
            {
                hasIcon = true;
            }
        }
    }

    private static void CheckDocumentationParameters(
        modelicaParser.Element_modificationContext elemMod,
        ref bool hasDocInfo,
        ref bool hasDocRevisions)
    {
        var modification = elemMod.modification();
        if (modification?.class_modification() == null) return;

        var docArgList = modification.class_modification().argument_list();
        if (docArgList == null) return;

        foreach (var docArg in docArgList.argument())
        {
            var docElemMod = docArg.element_modification_or_replaceable()?.element_modification();
            if (docElemMod == null) continue;

            var paramName = docElemMod.name()?.GetText();
            if (paramName == "info") hasDocInfo = true;
            if (paramName == "revisions") hasDocRevisions = true;
        }
    }
}
