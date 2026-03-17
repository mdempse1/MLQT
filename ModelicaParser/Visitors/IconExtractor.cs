using System.Globalization;
using System.Text.RegularExpressions;
using ModelicaParser.Icons;
using ModelicaParser.Helpers;

namespace ModelicaParser.Visitors;

/// <summary>
/// Extracts Icon annotation data from Modelica class definitions.
/// Parses the Icon graphics array and converts it to structured IconData.
/// Also extracts extends clause information for icon inheritance.
/// Only extracts from the top-level class, not from nested classes.
/// </summary>
public class IconExtractor : modelicaBaseVisitor<object?>
{
    private readonly List<string> _extendsClasses = new();
    private IconData? _currentIcon;
    private int _classDepth = 0;
    private string? _withinPackage;

    /// <summary>
    /// Extracts Icon data from a Modelica class definition string.
    /// </summary>
    /// <param name="modelicaCode">The Modelica source code.</param>
    /// <returns>IconData if an Icon annotation was found, null otherwise.</returns>
    public static IconData? ExtractIcon(string modelicaCode)
    {
        var result = ExtractIconWithInheritance(modelicaCode);
        return result?.Icon;
    }

    /// <summary>
    /// Extracts Icon data from a pre-parsed Modelica parse tree.
    /// </summary>
    /// <param name="parseTree">The pre-parsed ANTLR4 parse tree.</param>
    /// <returns>IconData if an Icon annotation was found, null otherwise.</returns>
    public static IconData? ExtractIcon(modelicaParser.Stored_definitionContext parseTree)
    {
        var result = ExtractIconWithInheritance(parseTree);
        return result?.Icon;
    }

    /// <summary>
    /// Extracts Icon data and extends clause information from a Modelica class definition.
    /// </summary>
    /// <param name="modelicaCode">The Modelica source code.</param>
    /// <returns>IconExtractionResult containing icon and extends information.</returns>
    public static IconExtractionResult? ExtractIconWithInheritance(string modelicaCode)
    {
        if (string.IsNullOrWhiteSpace(modelicaCode))
            return null;

        try
        {
            var parseTree = ModelicaParserHelper.Parse(modelicaCode);
            return ExtractIconWithInheritance(parseTree);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Extracts Icon data and extends clause information from a pre-parsed Modelica parse tree.
    /// </summary>
    /// <param name="parseTree">The pre-parsed ANTLR4 parse tree.</param>
    /// <returns>IconExtractionResult containing icon and extends information.</returns>
    public static IconExtractionResult? ExtractIconWithInheritance(modelicaParser.Stored_definitionContext parseTree)
    {
        try
        {
            var extractor = new IconExtractor();
            extractor.Visit(parseTree);
            return new IconExtractionResult
            {
                Icon = extractor._currentIcon,
                ExtendsClasses = extractor._extendsClasses,
                WithinPackage = extractor._withinPackage
            };
        }
        catch
        {
            return null;
        }
    }

    public override object? VisitStored_definition(modelicaParser.Stored_definitionContext context)
    {
        // Capture the package from the 'within' clause (e.g. "within Modelica.Blocks;")
        // The name() array contains only names from within clauses in this rule context.
        var names = context.name();
        if (names != null && names.Length > 0)
            _withinPackage = names[0].GetText();
        return base.VisitStored_definition(context);
    }

    public override object? VisitClass_definition(modelicaParser.Class_definitionContext context)
    {
        _classDepth++;
        try
        {
            return base.VisitClass_definition(context);
        }
        finally
        {
            _classDepth--;
        }
    }

    public override object? VisitExtends_clause(modelicaParser.Extends_clauseContext context)
    {
        // Only extract extends from the top-level class (depth 1), not nested classes
        if (_classDepth != 1)
            return base.VisitExtends_clause(context);

        // Extract the base class name from extends clause
        var typeSpec = context.type_specifier();
        if (typeSpec != null)
        {
            var baseClassName = typeSpec.GetText();
            if (!string.IsNullOrEmpty(baseClassName))
            {
                _extendsClasses.Add(baseClassName);
            }
        }
        return base.VisitExtends_clause(context);
    }

    public override object? VisitAnnotation(modelicaParser.AnnotationContext context)
    {
        // Only extract Icon annotation from the top-level class (depth 1), not nested classes
        if (_classDepth != 1)
            return base.VisitAnnotation(context);

        // Look for Icon in the class modification
        var classMod = context.class_modification();
        if (classMod != null)
        {
            ProcessClassModificationForIcon(classMod);
        }
        return base.VisitAnnotation(context);
    }

    private void ProcessClassModificationForIcon(modelicaParser.Class_modificationContext classMod)
    {
        var argList = classMod.argument_list();
        if (argList == null) return;

        foreach (var arg in argList.argument())
        {
            var elementMod = arg.element_modification_or_replaceable()?.element_modification();
            if (elementMod != null)
            {
                var name = elementMod.name()?.GetText();
                if (name == "Icon")
                {
                    _currentIcon = new IconData();
                    var modification = elementMod.modification();
                    if (modification?.class_modification() != null)
                    {
                        ProcessIconModification(modification.class_modification());
                    }
                }
            }
        }
    }

    private void ProcessIconModification(modelicaParser.Class_modificationContext classMod)
    {
        var argList = classMod.argument_list();
        if (argList == null) return;

        foreach (var arg in argList.argument())
        {
            var elementMod = arg.element_modification_or_replaceable()?.element_modification();
            if (elementMod != null)
            {
                var name = elementMod.name()?.GetText();
                var modification = elementMod.modification();

                switch (name)
                {
                    case "coordinateSystem":
                        if (modification?.class_modification() != null)
                        {
                            ProcessCoordinateSystem(modification.class_modification());
                        }
                        break;
                    case "graphics":
                        if (modification?.modification_expression()?.expression() != null)
                        {
                            ProcessGraphicsArray(modification.modification_expression().expression());
                        }
                        break;
                }
            }
        }
    }

    private void ProcessCoordinateSystem(modelicaParser.Class_modificationContext classMod)
    {
        var argList = classMod.argument_list();
        if (argList == null || _currentIcon == null) return;

        foreach (var arg in argList.argument())
        {
            var elementMod = arg.element_modification_or_replaceable()?.element_modification();
            if (elementMod != null)
            {
                var name = elementMod.name()?.GetText();
                var modification = elementMod.modification();
                var exprText = modification?.modification_expression()?.expression()?.GetText();

                switch (name)
                {
                    case "extent":
                        _currentIcon.CoordinateExtent = ParseExtent(exprText);
                        break;
                    case "preserveAspectRatio":
                        _currentIcon.PreserveAspectRatio = exprText?.ToLower() == "true";
                        break;
                    case "initialScale":
                        if (double.TryParse(exprText, NumberStyles.Float, CultureInfo.InvariantCulture, out var scale))
                            _currentIcon.InitialScale = scale;
                        break;
                }
            }
        }
    }

    private void ProcessGraphicsArray(modelicaParser.ExpressionContext expr)
    {
        if (_currentIcon == null) return;

        // The graphics value is an array like {Rectangle(...), Line(...), ...}
        var primary = FindPrimary(expr);
        if (primary?.array_arguments() != null)
        {
            ProcessArrayArguments(primary.array_arguments());
        }
    }

    private modelicaParser.PrimaryContext? FindPrimary(modelicaParser.ExpressionContext expr)
    {
        // Navigate through the expression hierarchy to find the primary
        var simpleExpr = expr.simple_expression();
        if (simpleExpr == null) return null;

        var logicalExpr = simpleExpr.logical_expression().FirstOrDefault();
        if (logicalExpr == null) return null;

        var logicalTerm = logicalExpr.logical_term().FirstOrDefault();
        if (logicalTerm == null) return null;

        var logicalFactor = logicalTerm.logical_factor().FirstOrDefault();
        if (logicalFactor == null) return null;

        var relation = logicalFactor.relation();
        if (relation == null) return null;

        var arithExpr = relation.arithmetic_expression().FirstOrDefault();
        if (arithExpr == null) return null;

        var term = arithExpr.term().FirstOrDefault();
        if (term == null) return null;

        var factor = term.factor().FirstOrDefault();
        if (factor == null) return null;

        return factor.primary().FirstOrDefault();
    }

    private void ProcessArrayArguments(modelicaParser.Array_argumentsContext arrayArgs)
    {
        // Grammar: expression (',' expression)* — iterate over all expressions
        var expressions = arrayArgs.expression();
        if (expressions != null)
        {
            foreach (var expr in expressions)
            {
                ProcessGraphicsPrimitive(expr);
            }
        }
    }

    private void ProcessGraphicsPrimitive(modelicaParser.ExpressionContext expr)
    {
        var primary = FindPrimary(expr);
        if (primary == null) return;

        // Check for function call like Rectangle(...)
        var compRef = primary.component_reference();
        var funcArgs = primary.function_call_args();

        if (compRef != null && funcArgs != null)
        {
            var primitiveType = compRef.GetText();
            var args = funcArgs.function_arguments();

            var primitive = CreatePrimitive(primitiveType);
            if (primitive != null && args != null)
            {
                ProcessPrimitiveArguments(primitive, args);
                _currentIcon?.Graphics.Add(primitive);
            }
        }
    }

    private GraphicsPrimitive? CreatePrimitive(string type)
    {
        return type switch
        {
            "Rectangle" => new RectanglePrimitive(),
            "Ellipse" => new EllipsePrimitive(),
            "Line" => new LinePrimitive(),
            "Polygon" => new PolygonPrimitive(),
            "Text" => new TextPrimitive(),
            "Bitmap" => new BitmapPrimitive(),
            _ => null
        };
    }

    private void ProcessPrimitiveArguments(GraphicsPrimitive primitive, modelicaParser.Function_argumentsContext args)
    {
        // function_arguments can be: expression, function_partial_application, or named_arguments
        // For Icon graphics, we expect named_arguments
        var namedArgs = args.named_arguments();
        if (namedArgs != null)
        {
            ProcessNamedArguments(primitive, namedArgs);
        }
    }

    private void ProcessNamedArguments(GraphicsPrimitive primitive, modelicaParser.Named_argumentsContext namedArgs)
    {
        // Grammar: named_argument (',' named_argument)*
        var args = namedArgs.named_argument();
        if (args != null)
        {
            foreach (var arg in args)
            {
                ProcessNamedArgument(primitive, arg);
            }
        }
    }

    private void ProcessNamedArgument(GraphicsPrimitive primitive, modelicaParser.Named_argumentContext namedArg)
    {
        // named_argument : IDENT '=' function_argument
        // function_argument : function_partial_application | expression
        var name = namedArg.IDENT()?.GetText();
        var funcArg = namedArg.function_argument();
        var exprText = funcArg?.expression()?.GetText();

        if (name == null || exprText == null) return;

        // Common properties
        switch (name)
        {
            case "visible":
                primitive.Visible = exprText.ToLower() == "true";
                return;
            case "origin":
                primitive.Origin = ParsePoint(exprText);
                return;
            case "rotation":
                if (double.TryParse(exprText, NumberStyles.Float, CultureInfo.InvariantCulture, out var rotation))
                    primitive.Rotation = rotation;
                return;
            case "lineColor":
                primitive.LineColor = ParseColor(exprText);
                return;
            case "fillColor":
                primitive.FillColor = ParseColor(exprText);
                return;
            case "fillPattern":
                primitive.FillPattern = ParseEnumValue(exprText);
                return;
            case "pattern":
            case "linePattern":
                primitive.LinePattern = ParseEnumValue(exprText);
                return;
            case "lineThickness":
            case "thickness":
                if (double.TryParse(exprText, NumberStyles.Float, CultureInfo.InvariantCulture, out var thickness))
                    primitive.LineThickness = thickness;
                return;
        }

        // Type-specific properties
        switch (primitive)
        {
            case RectanglePrimitive rect:
                ProcessRectangleProperty(rect, name, exprText);
                break;
            case EllipsePrimitive ellipse:
                ProcessEllipseProperty(ellipse, name, exprText);
                break;
            case LinePrimitive line:
                ProcessLineProperty(line, name, exprText);
                break;
            case PolygonPrimitive polygon:
                ProcessPolygonProperty(polygon, name, exprText);
                break;
            case TextPrimitive text:
                ProcessTextProperty(text, name, exprText);
                break;
            case BitmapPrimitive bitmap:
                ProcessBitmapProperty(bitmap, name, exprText);
                break;
        }
    }

    private void ProcessRectangleProperty(RectanglePrimitive rect, string name, string exprText)
    {
        switch (name)
        {
            case "extent":
                rect.Extent = ParseExtent(exprText);
                break;
            case "borderPattern":
                rect.BorderPattern = ParseEnumValue(exprText);
                break;
            case "radius":
                if (double.TryParse(exprText, NumberStyles.Float, CultureInfo.InvariantCulture, out var radius))
                    rect.Radius = radius;
                break;
        }
    }

    private void ProcessEllipseProperty(EllipsePrimitive ellipse, string name, string exprText)
    {
        switch (name)
        {
            case "extent":
                ellipse.Extent = ParseExtent(exprText);
                break;
            case "startAngle":
                if (double.TryParse(exprText, NumberStyles.Float, CultureInfo.InvariantCulture, out var startAngle))
                    ellipse.StartAngle = startAngle;
                break;
            case "endAngle":
                if (double.TryParse(exprText, NumberStyles.Float, CultureInfo.InvariantCulture, out var endAngle))
                    ellipse.EndAngle = endAngle;
                break;
            case "closure":
                ellipse.Closure = ParseEnumValue(exprText);
                break;
        }
    }

    private void ProcessLineProperty(LinePrimitive line, string name, string exprText)
    {
        switch (name)
        {
            case "points":
                line.Points = ParsePoints(exprText);
                break;
            case "arrow":
                var arrows = ParseArrows(exprText);
                if (arrows.Length >= 2)
                {
                    line.ArrowStart = arrows[0];
                    line.ArrowEnd = arrows[1];
                }
                break;
            case "arrowSize":
                if (double.TryParse(exprText, NumberStyles.Float, CultureInfo.InvariantCulture, out var arrowSize))
                    line.ArrowSize = arrowSize;
                break;
            case "smooth":
                line.Smooth = ParseEnumValue(exprText);
                break;
            case "color":
                line.LineColor = ParseColor(exprText);
                break;
        }
    }

    private void ProcessPolygonProperty(PolygonPrimitive polygon, string name, string exprText)
    {
        switch (name)
        {
            case "points":
                polygon.Points = ParsePoints(exprText);
                break;
            case "smooth":
                polygon.Smooth = ParseEnumValue(exprText);
                break;
        }
    }

    private void ProcessTextProperty(TextPrimitive text, string name, string exprText)
    {
        switch (name)
        {
            case "extent":
                text.Extent = ParseExtent(exprText);
                break;
            case "textString":
            case "string":
                text.TextString = ParseStringValue(exprText);
                break;
            case "fontSize":
                if (double.TryParse(exprText, NumberStyles.Float, CultureInfo.InvariantCulture, out var fontSize))
                    text.FontSize = fontSize;
                break;
            case "fontName":
                text.FontName = ParseStringValue(exprText);
                break;
            case "textStyle":
                text.FontStyles = ParseTextStyles(exprText);
                break;
            case "horizontalAlignment":
                text.HorizontalAlignment = ParseEnumValue(exprText);
                break;
            case "textColor":
                text.TextColor = ParseColor(exprText);
                break;
        }
    }

    private void ProcessBitmapProperty(BitmapPrimitive bitmap, string name, string exprText)
    {
        switch (name)
        {
            case "extent":
                bitmap.Extent = ParseExtent(exprText);
                break;
            case "fileName":
                bitmap.FileName = ParseStringValue(exprText);
                break;
            case "imageSource":
                bitmap.ImageSource = ParseStringValue(exprText);
                break;
        }
    }

    #region Parsing Helpers

    private static double[] ParseExtent(string? text)
    {
        // Parse {{x1,y1},{x2,y2}} format
        if (string.IsNullOrEmpty(text)) return new double[] { -100, -100, 100, 100 };

        var numbers = ExtractNumbers(text);
        if (numbers.Count >= 4)
        {
            return new double[] { numbers[0], numbers[1], numbers[2], numbers[3] };
        }
        return new double[] { -100, -100, 100, 100 };
    }

    private static double[] ParsePoint(string text)
    {
        // Parse {x,y} format
        var numbers = ExtractNumbers(text);
        if (numbers.Count >= 2)
        {
            return new double[] { numbers[0], numbers[1] };
        }
        return new double[] { 0, 0 };
    }

    private static List<double[]> ParsePoints(string text)
    {
        // Parse {{x1,y1},{x2,y2},...} format
        var result = new List<double[]>();
        var numbers = ExtractNumbers(text);

        for (int i = 0; i + 1 < numbers.Count; i += 2)
        {
            result.Add(new double[] { numbers[i], numbers[i + 1] });
        }
        return result;
    }

    private static int[] ParseColor(string text)
    {
        // Parse {r,g,b} format
        var numbers = ExtractNumbers(text);
        if (numbers.Count >= 3)
        {
            return new int[] { (int)numbers[0], (int)numbers[1], (int)numbers[2] };
        }
        return new int[] { 0, 0, 0 };
    }

    private static List<double> ExtractNumbers(string text)
    {
        var numbers = new List<double>();
        var matches = Regex.Matches(text, @"-?\d+\.?\d*");
        foreach (Match match in matches)
        {
            if (double.TryParse(match.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var num))
            {
                numbers.Add(num);
            }
        }
        return numbers;
    }

    private static string ParseEnumValue(string text)
    {
        // Parse FillPattern.Solid -> Solid
        var parts = text.Split('.');
        return parts.Length > 1 ? parts[^1] : text;
    }

    private static string ParseStringValue(string text)
    {
        // Remove surrounding quotes
        if (text.Length >= 2 && text.StartsWith("\"") && text.EndsWith("\""))
        {
            return text[1..^1];
        }
        return text;
    }

    private static string[] ParseArrows(string text)
    {
        // Parse {Arrow.None, Arrow.Filled} format
        var result = new List<string>();
        var parts = text.Split(',');
        foreach (var part in parts)
        {
            result.Add(ParseEnumValue(part.Trim().Trim('{', '}')));
        }
        return result.ToArray();
    }

    private static List<string> ParseTextStyles(string text)
    {
        // Parse {TextStyle.Bold, TextStyle.Italic} format
        var result = new List<string>();
        var parts = text.Split(',');
        foreach (var part in parts)
        {
            var style = ParseEnumValue(part.Trim().Trim('{', '}'));
            if (!string.IsNullOrEmpty(style))
            {
                result.Add(style);
            }
        }
        return result;
    }

    #endregion
}
