using Antlr4.Runtime;
using Antlr4.Runtime.Misc;
using ModelicaParser.DataTypes;

namespace ModelicaParser.Visitors;

/// <summary>
/// Visitor that extracts all model definitions from a Modelica parse tree.
/// </summary>
public class ModelExtractorVisitor : modelicaBaseVisitor<object?>
{
    private readonly List<ModelInfo> _models = new();
    private readonly string _sourceCode;
    private readonly Stack<string> _parentModelNames = new();
    private string _withinPackage = string.Empty;
    private bool _hasElementPrefixes = false;
    private string _elementPrefix = string.Empty;

    /// <summary>
    /// Gets all models found in the parse tree.
    /// </summary>
    public List<ModelInfo> Models => _models;

    public ModelExtractorVisitor(string sourceCode)
    {
        _sourceCode = sourceCode;
    }

    public override object? VisitStored_definition([NotNull] modelicaParser.Stored_definitionContext context)
    {
        // Extract the package name from the 'within' statement
        var nameContexts = context.name();

        if (nameContexts != null && nameContexts.Length > 0)
        {
            // Get the first 'name' context (there may be multiple 'within' statements)
            var nameContext = nameContexts[0];

            // Build the full package name from the IDENT tokens
            var identTokens = nameContext.IDENT();
            if (identTokens != null && identTokens.Length > 0)
            {
                _withinPackage = string.Join(".", identTokens.Select(t => t.GetText()));
            }
        }

        return base.VisitStored_definition(context);
    }

    // Visit all class definitions
    public override object? VisitClass_definition([NotNull] modelicaParser.Class_definitionContext context)
    {
        // Get the class type (model, block, function, etc.)
        var classType = GetClassType(context.class_prefixes());

        // Get the class specifier to extract the name
        var specifier = context.class_specifier();

        if (specifier.long_class_specifier() != null)
        {
            var longSpec = specifier.long_class_specifier();
            var identTokens = longSpec.IDENT();

            if (identTokens != null && identTokens.Length > 0)
            {
                var modelName = identTokens[0].GetText();
                var sourceCode = GetSourceCode(context);

                var modelInfo = new ModelInfo(modelName, sourceCode, classType)
                {
                    StartLine = context.Start.Line,
                    StopLine = context.Stop?.Line ?? context.Start.Line,
                    IsNested = _parentModelNames.Count > 0,
                    ParentModelName = _parentModelNames.Count > 0 ? _parentModelNames.Peek() : _withinPackage,
                    CanBeStoredStandalone = !_hasElementPrefixes,
                    ElementPrefix = _elementPrefix
                };

                ExtractAnnotations(longSpec.composition(), modelInfo);

                _models.Add(modelInfo);

                // Push this model as parent for nested models
                _parentModelNames.Push(_parentModelNames.Count > 0 ? _parentModelNames.Peek() + "." + modelName : _withinPackage.Length > 0 ? _withinPackage + "." + modelName : modelName);

                // Visit children to find nested models
                base.VisitClass_definition(context);

                // Pop parent
                _parentModelNames.Pop();

                return null;
            }
        }
        else if (specifier.short_class_specifier() != null)
        {
            var shortSpec = specifier.short_class_specifier();
            var ident = shortSpec.IDENT();

            if (ident != null)
            {
                var modelName = ident.GetText();
                var sourceCode = GetSourceCode(context);

                var modelInfo = new ModelInfo(modelName, sourceCode, classType)
                {
                    StartLine = context.Start.Line,
                    StopLine = context.Stop?.Line ?? context.Start.Line,
                    IsNested = _parentModelNames.Count > 0,
                    ParentModelName = _parentModelNames.Count > 0 ? _parentModelNames.Peek() : _withinPackage,
                    CanBeStoredStandalone = !_hasElementPrefixes,
                    ElementPrefix = _elementPrefix
                };

                _models.Add(modelInfo);
            }
        }
        else if (specifier.der_class_specifier() != null)
        {
            var derSpec = specifier.der_class_specifier();
            var identTokens = derSpec.IDENT();

            if (identTokens != null && identTokens.Length > 0)
            {
                // First IDENT is the model name
                var modelName = identTokens[0].GetText();
                var sourceCode = GetSourceCode(context);

                var modelInfo = new ModelInfo(modelName, sourceCode, classType)
                {
                    StartLine = context.Start.Line,
                    StopLine = context.Stop?.Line ?? context.Start.Line,
                    IsNested = _parentModelNames.Count > 0,
                    ParentModelName = _parentModelNames.Count > 0 ? _parentModelNames.Peek() : _withinPackage,
                    CanBeStoredStandalone = !_hasElementPrefixes,
                    ElementPrefix = _elementPrefix
                };

                _models.Add(modelInfo);
            }
        }

        return base.VisitClass_definition(context);
    }

    // Visit element to detect if a class has element-level prefixes
    public override object? VisitElement([NotNull] modelicaParser.ElementContext context)
    {
        var text = context.GetText();
        bool hadPrefixes = _hasElementPrefixes;
        string hadPrefix = _elementPrefix;

        // Check for prefixes: replaceable, redeclare, inner, outer
        // When present, these keywords are part of the element rule but NOT part of
        // the class_definition rule. Class definitions with these prefixes are elements
        // of their parent class and should not be extracted as separate models.
        if (text.StartsWith("replaceable") || text.StartsWith("redeclare") ||
            text.StartsWith("inner") || text.StartsWith("outer") ||
            text.Contains("innerreplaceable") || text.Contains("outerreplaceable") ||
            text.Contains("redeclarefinal") || text.Contains("redeclareinner") ||
            text.Contains("redeclaredouter"))
        {
            _hasElementPrefixes = true;
            _elementPrefix = ExtractElementPrefix(context);
        }

        // Visit children (this will visit the class_definition if present)
        var result = base.VisitElement(context);

        // Restore the previous state
        _hasElementPrefixes = hadPrefixes;
        _elementPrefix = hadPrefix;

        return result;
    }

    private static void ExtractAnnotations(modelicaParser.CompositionContext? composition, ModelInfo modelInfo)
    {
        if (composition == null) return;

        var annotation = composition.annotation();
        if (annotation == null || annotation.Length == 0) return;

        // The class-level annotation is the last one in the composition
        var classAnnotation = annotation[^1];
        var classMod = classAnnotation.class_modification();
        var argList = classMod?.argument_list();
        if (argList == null) return;

        foreach (var argument in argList.argument())
        {
            var elemModOrRepl = argument.element_modification_or_replaceable();
            var elemMod = elemModOrRepl?.element_modification();
            if (elemMod == null) continue;

            var name = elemMod.name().GetText();

            if (name == "experiment")
            {
                modelInfo.HasExperimentAnnotation = true;
            }
            else if (name == "version")
            {
                // version="1.2.3" -> modification -> '=' modification_expression -> expression (STRING)
                var mod = elemMod.modification();
                var modExpr = mod?.modification_expression();
                if (modExpr != null)
                {
                    var text = modExpr.GetText().Trim('"');
                    modelInfo.Version = text;
                }
            }
            else if (name == "uses")
            {
                // uses(Modelica(version="4.0.0")) -> modification -> class_modification -> argument_list
                var mod = elemMod.modification();
                var usesMod = mod?.class_modification();
                var usesArgList = usesMod?.argument_list();
                if (usesArgList == null) continue;

                modelInfo.Uses = new Dictionary<string, string>();

                foreach (var usesArg in usesArgList.argument())
                {
                    var usesElemModOrRepl = usesArg.element_modification_or_replaceable();
                    var usesElemMod = usesElemModOrRepl?.element_modification();
                    if (usesElemMod == null) continue;

                    var packageName = usesElemMod.name().GetText();

                    // Modelica(version="4.0.0") -> modification -> class_modification -> argument_list
                    var pkgMod = usesElemMod.modification();
                    var pkgClassMod = pkgMod?.class_modification();
                    var pkgArgList = pkgClassMod?.argument_list();
                    if (pkgArgList == null) continue;

                    foreach (var pkgArg in pkgArgList.argument())
                    {
                        var pkgElemModOrRepl = pkgArg.element_modification_or_replaceable();
                        var pkgElemMod = pkgElemModOrRepl?.element_modification();
                        if (pkgElemMod == null) continue;

                        if (pkgElemMod.name().GetText() == "version")
                        {
                            var versionMod = pkgElemMod.modification();
                            var versionExpr = versionMod?.modification_expression();
                            if (versionExpr != null)
                            {
                                var versionText = versionExpr.GetText().Trim('"');
                                modelInfo.Uses[packageName] = versionText;
                            }
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Extracts the element-level prefix keywords from an element context by reading
    /// the source text between the element start and the class_definition start.
    /// For example, for "redeclare final model Foo", this returns "redeclare final".
    /// </summary>
    private string ExtractElementPrefix(modelicaParser.ElementContext context)
    {
        var classDef = context.class_definition();
        if (classDef == null)
            return string.Empty;

        var elementStart = context.Start.StartIndex;
        var classDefStart = classDef.Start.StartIndex;

        if (classDefStart <= elementStart)
            return string.Empty;

        var prefix = _sourceCode.Substring(elementStart, classDefStart - elementStart).Trim();
        return prefix;
    }

    private static string GetClassType(modelicaParser.Class_prefixesContext context)
    {
        var text = context.GetText();

        // Extract the main class type keyword
        if (text.Contains("model")) return "model";
        if (text.Contains("function")) return "function";
        if (text.Contains("block")) return "block";
        if (text.Contains("connector")) return "connector";
        if (text.Contains("record")) return "record";
        if (text.Contains("type")) return "type";
        if (text.Contains("package")) return "package";
        if (text.Contains("class")) return "class";

        return "class";
    }

    private string GetSourceCode(ParserRuleContext context)
    {
        var startIndex = context.Start.StartIndex;
        var stopIndex = context.Stop?.StopIndex ?? context.Start.StopIndex;

        if (startIndex < 0 || stopIndex < 0 || stopIndex >= _sourceCode.Length)
            return string.Empty;

        var length = stopIndex - startIndex + 1;
        if (length <= 0 || startIndex + length > _sourceCode.Length)
            return string.Empty;

        // The class_definition grammar rule does not include the trailing ";",
        // which belongs to the parent rule (stored_definition or element).
        // For long_class_specifier (end IDENT;), the ";" is usually the next
        // character, but for short_class_specifier the ";" may be separated by
        // whitespace, or a constrainedby clause may appear before it (for
        // replaceable elements). Instead of scanning forward (which risks
        // including constrainedby/annotation content), just append ";" if the
        // extracted text doesn't already end with one.
        var text = _sourceCode.Substring(startIndex, length);
        if (!text.TrimEnd().EndsWith(";"))
        {
            text = text.TrimEnd() + ";";
        }
        return text;
    }
}
