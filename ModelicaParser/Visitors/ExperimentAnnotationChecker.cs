using ModelicaParser.Helpers;

namespace ModelicaParser.Visitors;

/// <summary>
/// ANTLR visitor that checks class-level annotations for the standard Modelica
/// <c>experiment(...)</c> annotation, indicating a simulatable model.
/// Only inspects the outermost class definition — nested classes are skipped.
/// Subclasses can override <see cref="CheckAnnotationArgument"/> to detect
/// additional vendor-specific annotations.
/// </summary>
public class ExperimentAnnotationChecker : modelicaBaseVisitor<object?>
{
    private int _classDepth;

    /// <summary>Whether the outermost class has an <c>experiment(...)</c> annotation.</summary>
    public bool HasExperimentAnnotation { get; protected set; }

    /// <summary>
    /// Parses Modelica source code and checks annotations on the outermost class.
    /// </summary>
    public static ExperimentAnnotationChecker Check(string modelicaCode)
    {
        var parseTree = ModelicaParserHelper.Parse(modelicaCode);
        var checker = new ExperimentAnnotationChecker();
        checker.Visit(parseTree);
        return checker;
    }

    public override object? VisitClass_definition(modelicaParser.Class_definitionContext context)
    {
        _classDepth++;
        if (_classDepth > 1)
        {
            // Skip nested class definitions — we only inspect the outermost class.
            _classDepth--;
            return null;
        }

        var result = base.VisitClass_definition(context);
        _classDepth--;
        return result;
    }

    public override object? VisitComposition(modelicaParser.CompositionContext context)
    {
        foreach (var annotation in context.annotation())
        {
            CheckAnnotation(annotation);
        }

        return base.VisitComposition(context);
    }

    private void CheckAnnotation(modelicaParser.AnnotationContext annotation)
    {
        var argList = annotation.class_modification()?.argument_list();
        if (argList == null) return;

        foreach (var arg in argList.argument())
        {
            var elemMod = arg.element_modification_or_replaceable()?.element_modification();
            if (elemMod == null) continue;

            var name = elemMod.name()?.GetText();
            CheckAnnotationArgument(name, elemMod);
        }
    }

    /// <summary>
    /// Called for each top-level argument in a class-level annotation.
    /// Override to detect additional vendor-specific annotations.
    /// </summary>
    /// <param name="name">The annotation argument name (e.g. "experiment", "Documentation").</param>
    /// <param name="elemMod">The element modification context for accessing nested parameters.</param>
    protected virtual void CheckAnnotationArgument(string? name, modelicaParser.Element_modificationContext elemMod)
    {
        if (name == "experiment")
        {
            HasExperimentAnnotation = true;
        }
    }

    /// <summary>
    /// Helper to extract a boolean value from an element modification (e.g. <c>doNotTest=true</c>).
    /// </summary>
    protected static bool GetBooleanValue(modelicaParser.Element_modificationContext elemMod)
    {
        var expr = elemMod.modification()?.modification_expression()?.expression();
        return expr?.GetText() == "true";
    }
}
