using Antlr4.Runtime;
using System.Linq;

namespace ModelicaParser.Helpers;

/// <summary>
/// Helper class for ModelicaRenderer containing formatting and analysis functions.
/// </summary>
public static class ModelicaRendererHelper
{
    #region Argument Counting Functions

    /// <summary>
    /// Counts the number of arguments in a function_arguments context.
    /// Grammar: expression (',' function_argument)* (',' named_arguments)? ('for' for_indices)?
    ///        | function_partial_application (',' function_argument)* (',' named_arguments)?
    ///        | named_arguments
    /// </summary>
    public static int CountFunctionArguments(modelicaParser.Function_argumentsContext? context)
    {
        if (context == null)
            return 0;

        int count = 0;
        if (context.expression() != null)
        {
            // First expression + additional function_argument entries
            count = 1 + (context.function_argument()?.Length ?? 0);
        }
        else if (context.function_partial_application() != null)
        {
            count = 1 + (context.function_argument()?.Length ?? 0);
        }

        // Add named arguments if present
        if (context.named_arguments() != null)
        {
            count += context.named_arguments().named_argument()?.Length ?? 0;
        }

        return count;
    }

    /// <summary>
    /// Counts the number of arguments in an array_arguments context.
    /// Grammar: expression (',' expression)* ('for' for_indices)?
    /// </summary>
    public static int CountArrayArguments(modelicaParser.Array_argumentsContext? context)
    {
        if (context == null)
            return 0;

        return context.expression()?.Length ?? 0;
    }

    /// <summary>
    /// Counts the number of arguments (both regular arguments and inheritence modifications) in an argument_or_inheritence_list context.
    /// </summary>
    public static int CountArgumentsInInheritenceList(modelicaParser.Argument_or_inheritence_listContext? context)
    {
        if (context == null || context.children == null)
            return 0;

        int count = 0;
        for (int i = 0; i < context.children.Count; i++)
        {
            var child = context.children[i];
            if (child is modelicaParser.ArgumentContext || child is modelicaParser.Inheritence_modificationContext)
                count++;
        }
        return count;
    }

    #endregion

    #region Nesting Depth Functions

    /// <summary>
    /// Calculates the maximum nesting depth of class_modification contexts within the given context.
    /// </summary>
    public static int GetMaxNestingDepth(ParserRuleContext context, int currentDepth = 0)
    {
        int maxDepth = currentDepth;

        for (int i = 0; i < context.ChildCount; i++)
        {
            var child = context.GetChild(i);
            if (child is modelicaParser.Class_modificationContext classModCtx)
            {
                int childDepth = GetMaxNestingDepth(classModCtx, currentDepth + 1);
                maxDepth = Math.Max(maxDepth, childDepth);
            }
            else if (child is ParserRuleContext childContext)
            {
                int childDepth = GetMaxNestingDepth(childContext, currentDepth);
                maxDepth = Math.Max(maxDepth, childDepth);
            }
        }

        return maxDepth;
    }

    /// <summary>
    /// Calculates the maximum nesting depth of class_modification and class_or_inheritence_modification contexts within the given context.
    /// </summary>
    public static int GetMaxNestingDepthInInheritence(ParserRuleContext context, int currentDepth = 0)
    {
        int maxDepth = currentDepth;

        for (int i = 0; i < context.ChildCount; i++)
        {
            var child = context.GetChild(i);
            if (child is ParserRuleContext inheritCtx && (child is modelicaParser.Class_modificationContext || child is modelicaParser.Class_or_inheritence_modificationContext))
            {
                int childDepth = GetMaxNestingDepthInInheritence(inheritCtx, currentDepth + 1);
                maxDepth = Math.Max(maxDepth, childDepth);
            }
            else if (child is ParserRuleContext childContext)
            {
                int childDepth = GetMaxNestingDepthInInheritence(childContext, currentDepth);
                maxDepth = Math.Max(maxDepth, childDepth);
            }
        }

        return maxDepth;
    }

    #endregion

    #region Graphics Array Analysis Functions

    /// <summary>
    /// Determines if a graphics array should be formatted on a single line.
    /// This is true when there's exactly one element with 2 arguments.
    /// </summary>
    public static bool IsSingleLineGraphicsArray(modelicaParser.Array_argumentsContext? arrayContents)
    {
        if (arrayContents == null)
            return false;

        // Check if there's exactly one element
        var expressions = arrayContents.expression();
        if (expressions != null && expressions.Length == 1 && arrayContents.for_indices() == null)
        {
            // There's exactly one element. Check if it's a function call with 2 arguments
            var expr = expressions[0];
            if (expr != null)
            {
                // Navigate through the expression hierarchy to find primary
                var simpleExpr = expr.simple_expression();
                if (simpleExpr != null)
                {
                    var logicalExpr = simpleExpr.logical_expression().FirstOrDefault();
                    if (logicalExpr != null)
                    {
                        var logicalTerm = logicalExpr.logical_term().FirstOrDefault();
                        if (logicalTerm != null)
                        {
                            var logicalFactor = logicalTerm.logical_factor().FirstOrDefault();
                            if (logicalFactor != null)
                            {
                                var relation = logicalFactor.relation();
                                if (relation != null)
                                {
                                    var arithExpr = relation.arithmetic_expression().FirstOrDefault();
                                    if (arithExpr != null)
                                    {
                                        var term = arithExpr.term().FirstOrDefault();
                                        if (term != null)
                                        {
                                            var factor = term.factor().FirstOrDefault();
                                            if (factor != null)
                                            {
                                                var primary = factor.primary().FirstOrDefault();
                                                if (primary != null && primary.function_call_args() != null)
                                                {
                                                    int argCount = CountFunctionArguments(primary.function_call_args().function_arguments());
                                                    return argCount == 2;
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Determines if a class modification contains a single-line graphics array.
    /// This checks if there's a "graphics=" argument with a single-line array.
    /// </summary>
    public static bool HasSingleLineGraphics(modelicaParser.Class_modificationContext context)
    {
        if (context.argument_list() == null)
            return false;

        var arguments = context.argument_list().argument();
        foreach (var arg in arguments)
        {
            // Check if this is a "graphics=" element modification
            var elementMod = arg.element_modification_or_replaceable()?.element_modification();
            if (elementMod != null && elementMod.name()?.GetText() == "graphics")
            {
                // Check if the modification is an array expression
                var modification = elementMod.modification();
                if (modification != null && modification.modification_expression()?.expression() != null)
                {
                    // Navigate to find the array constructor
                    var expr = modification.modification_expression().expression();
                    var simpleExpr = expr.simple_expression();
                    if (simpleExpr != null)
                    {
                        var logicalExpr = simpleExpr.logical_expression().FirstOrDefault();
                        if (logicalExpr != null)
                        {
                            var logicalTerm = logicalExpr.logical_term().FirstOrDefault();
                            if (logicalTerm != null)
                            {
                                var logicalFactor = logicalTerm.logical_factor().FirstOrDefault();
                                if (logicalFactor != null)
                                {
                                    var relation = logicalFactor.relation();
                                    if (relation != null)
                                    {
                                        var arithExpr = relation.arithmetic_expression().FirstOrDefault();
                                        if (arithExpr != null)
                                        {
                                            var term = arithExpr.term().FirstOrDefault();
                                            if (term != null)
                                            {
                                                var factor = term.factor().FirstOrDefault();
                                                if (factor != null)
                                                {
                                                    var primary = factor.primary().FirstOrDefault();
                                                    if (primary != null && primary.GetText().StartsWith('{'))
                                                    {
                                                        // Found graphics array, check if it's single-line
                                                        return IsSingleLineGraphicsArray(primary.array_arguments());
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Determines if an annotation contains only Icon with single-line graphics.
    /// Returns true if there's exactly one Icon argument with single-line graphics.
    /// </summary>
    public static bool HasOnlyIconWithSingleLineGraphics(modelicaParser.Class_modificationContext context)
    {
        if (context.argument_list() == null)
            return false;

        var arguments = context.argument_list().argument();

        // Should have exactly one argument
        if (arguments.Length != 1)
            return false;

        var arg = arguments[0];
        var elementMod = arg.element_modification_or_replaceable()?.element_modification();

        // Check if it's Icon
        if (elementMod != null && elementMod.name()?.GetText() == "Icon")
        {
            // Check if Icon has a class modification
            var modification = elementMod.modification();
            if (modification != null && modification.class_modification() != null)
            {
                // Check if Icon's modification has single-line graphics
                return HasSingleLineGraphics(modification.class_modification());
            }
        }

        return false;
    }

    /// <summary>
    /// Determines if a class_or_inheritence_modification contains a single-line graphics array.
    /// This checks if there's a "graphics=" argument with a single-line array.
    /// </summary>
    public static bool HasSingleLineGraphicsInInheritence(modelicaParser.Class_or_inheritence_modificationContext context)
    {
        if (context.argument_or_inheritence_list() == null || context.argument_or_inheritence_list().children == null)
            return false;

        var children = context.argument_or_inheritence_list().children;
        foreach (var child in children)
        {
            if (child is modelicaParser.ArgumentContext arg)
            {
                // Check if this is a "graphics=" element modification
                var elementMod = arg.element_modification_or_replaceable()?.element_modification();
                if (elementMod != null && elementMod.name()?.GetText() == "graphics")
                {
                    // Check if the modification is an array expression
                    var modification = elementMod.modification();
                    if (modification != null && modification.modification_expression()?.expression() != null)
                    {
                        // Navigate to find the array constructor
                        var expr = modification.modification_expression().expression();
                        var simpleExpr = expr.simple_expression();
                        if (simpleExpr != null)
                        {
                            var logicalExpr = simpleExpr.logical_expression().FirstOrDefault();
                            if (logicalExpr != null)
                            {
                                var logicalTerm = logicalExpr.logical_term().FirstOrDefault();
                                if (logicalTerm != null)
                                {
                                    var logicalFactor = logicalTerm.logical_factor().FirstOrDefault();
                                    if (logicalFactor != null)
                                    {
                                        var relation = logicalFactor.relation();
                                        if (relation != null)
                                        {
                                            var arithExpr = relation.arithmetic_expression().FirstOrDefault();
                                            if (arithExpr != null)
                                            {
                                                var term = arithExpr.term().FirstOrDefault();
                                                if (term != null)
                                                {
                                                    var factor = term.factor().FirstOrDefault();
                                                    if (factor != null)
                                                    {
                                                        var primary = factor.primary().FirstOrDefault();
                                                        if (primary != null && primary.GetText().StartsWith('{'))
                                                        {
                                                            // Found graphics array, check if it's single-line
                                                            return IsSingleLineGraphicsArray(primary.array_arguments());
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Determines if an annotation contains only Icon with single-line graphics (for class_or_inheritence_modification).
    /// Returns true if there's exactly one Icon argument with single-line graphics.
    /// </summary>
    public static bool HasOnlyIconWithSingleLineGraphicsInInheritence(modelicaParser.Class_or_inheritence_modificationContext context)
    {
        if (context.argument_or_inheritence_list() == null || context.argument_or_inheritence_list().children == null)
            return false;

        var children = context.argument_or_inheritence_list().children;

        // Count regular arguments (not commas or other separators)
        int argCount = 0;
        modelicaParser.ArgumentContext? iconArg = null;

        foreach (var child in children)
        {
            if (child is modelicaParser.ArgumentContext arg)
            {
                argCount++;
                var elementMod = arg.element_modification_or_replaceable()?.element_modification();
                if (elementMod != null && elementMod.name()?.GetText() == "Icon")
                {
                    iconArg = arg;
                }
            }
        }

        // Should have exactly one argument and it should be Icon
        if (argCount != 1 || iconArg == null)
            return false;

        var iconElementMod = iconArg.element_modification_or_replaceable()?.element_modification();
        if (iconElementMod != null)
        {
            // Check if Icon has a class modification (not class_or_inheritence_modification)
            var modification = iconElementMod.modification();
            if (modification != null && modification.class_modification() != null)
            {
                // Check if Icon's modification has single-line graphics
                return HasSingleLineGraphics(modification.class_modification());
            }
        }

        return false;
    }

    #endregion

    #region Class Name Extraction

    /// <summary>
    /// Extracts the class name from a class_definition context.
    /// </summary>
    public static string? GetClassNameFromDefinition(modelicaParser.Class_definitionContext context)
    {
        // Check if this is a regular class definition (class_prefixes class_specifier)
        if (context.class_specifier() != null)
        {
            var specifier = context.class_specifier();

            // long_class_specifier: IDENT string_comment composition 'end' IDENT
            if (specifier.long_class_specifier() != null)
            {
                var longSpec = specifier.long_class_specifier();
                var ident = longSpec.IDENT();
                if (ident != null && ident.Length > 0)
                {
                    return ident[0].GetText();
                }
            }
            // short_class_specifier: IDENT '=' ...
            else if (specifier.short_class_specifier() != null)
            {
                var shortSpec = specifier.short_class_specifier();
                var ident = shortSpec.IDENT();
                if (ident != null)
                {
                    return ident.GetText();
                }
            }
            // der_class_specifier: IDENT '=' 'der' ...
            else if (specifier.der_class_specifier() != null)
            {
                var derSpec = specifier.der_class_specifier();
                var ident = derSpec.IDENT();
                if (ident != null && ident.Length > 0)
                {
                    return ident[0].GetText();
                }
            }
        }

        return null;
    }

    #endregion
}
