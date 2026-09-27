using Antlr4.Runtime;
using Antlr4.Runtime.Misc;
using Antlr4.Runtime.Tree;
using System.Text;
using ModelicaParser.Helpers;

namespace ModelicaParser.Visitors;

/// <summary>
/// Visitor that generates formatted Modelica source code from a parse tree.
/// Supports two modes: pure Modelica code and markup mode for code editors.
/// </summary>
public class ModelicaRenderer : modelicaBaseVisitor<object?>
{
    private readonly bool _renderForCodeEditor;
    private readonly bool _showAnnotations;
    private readonly List<string> _code = new();
    private readonly StringBuilder _currentLine = new();
    private int _indentLevel = 0;
    private bool _classAnnotation = false;
    private int _inGraphicsAnnotationLevel = 0;
    private bool _inSingleLineGraphicsElement = false;
    private bool _withAnnotation = false;
    // Set when comments before 'constrainedby' (B432) have already ended the line the clause starts
    // on, so the clause does not end it again and leave a blank line.
    private bool _constrainedbyOnFreshLine = false;
    // Whether the function call being written puts one argument a line, which is where a comment
    // between its arguments (B431) leaves the next one: on its own line, or a level in.
    private bool _callUsingMultiLine = false;
    // The lines a matrix has already ended (B462) - its first line and each row before its last -
    // and the indent level they were ended at. Its last line is ended by whoever writes what
    // follows, possibly after an argument list wrapped for length has gone back a level, so the
    // next EmitLine moves these lines to the level that line is written at.
    private (int Start, int End, int Level)? _pendingMatrixLines;
    private bool _inDeclaration = false;
    private bool _excludeClassDefinitions = false;
    private readonly HashSet<string>? _classNamesToExclude;
    private readonly BufferedTokenStream? _tokenStream;
    private bool _parentUsingMultiLine = false;
    private bool _inAnnotation = false;
    private bool _inClassAnnotationIcon = false;
    private bool _suppressNextIndentation = false;
    private bool _inDocumentationAnnotation = false;
    private readonly HashSet<int> _noPostIndentLines = new(); // Lines exempt from public/protected post-processing indent
    private int _bracketDepth = 0;
    private int _equationContinuationIndent = 0;
    private bool _isFunction = false;
    private bool _nameAsType = false;

    //Style settings
    private const int IndentSpaces = 2;
    private readonly int _maxLineLength;
    private readonly bool _oneOfEachSection;
    private Stack<CodeSection> _currentSection = new();
    private bool _writtenSectionHeader = false;
    private Stack<Element> _currentElement = new();
    private bool _writeFinalComments = false;
    private bool _importsFirst;
    private bool _componentsBeforeClasses;

    /// <summary>
    /// Write <c>initial equation</c>/<c>initial algorithm</c> after the ordinary equation and
    /// algorithm sections rather than before them, for a repository whose convention is
    /// <c>MLQT.Style.InitialEqAlgoLast</c>.
    ///
    /// <para>Until this existed the renderer always wrote them first, which meant the formatter
    /// actively defeated that rule: a repository could enable it, apply formatting, and have every
    /// class with an initial section reported forever, the violation reintroduced on each save. The
    /// two ordering rules are mutually exclusive in the settings UI, so at most one of them is ever
    /// on.</para>
    /// </summary>
    private readonly bool _initialSectionsLast;

    /// <summary>
    /// Write the finer declaration order inside the component group — see
    /// <see cref="FormattingOptions.DeclarationOrder"/>. Read only where
    /// <see cref="_componentsBeforeClasses"/> is.
    /// </summary>
    private readonly bool _declarationOrder;

    /// <summary>
    /// Given the class being written and a declared type name, whether that type is a simple type
    /// rather than a structured class — the one part of <see cref="DeclarationKind"/> that cannot be
    /// read off the grammar. Null when the caller has no graph, which leaves only the predefined
    /// types recognised.
    ///
    /// <para><b>The rule is given the same callback</b>, so the arrangement this writes and the one
    /// <c>MLQT.Style.DeclarationOrder</c> asks for cannot come apart — which they would the moment
    /// the two resolved a type differently, leaving a finding the formatter does not clear.</para>
    /// </summary>
    private readonly Func<string, string, bool>? _isSimpleType;

    /// <summary>
    /// The id of the class currently being written, innermost last. Kept because the type lookup is
    /// scope-sensitive: a nested class has its own imports, so asking as its parent can resolve
    /// <c>SI.Length</c> differently from the way the rule, which checks that nested class on its
    /// own, resolves it.
    /// </summary>
    private readonly Stack<string> _classPath = new();

    /// <summary>Class-definition nesting depth; the outermost class is 1.</summary>
    private int _classDepth;

    /// <summary>
    /// The type lookup bound to the class currently being written, or null when the caller supplied
    /// none. <see cref="DeclarationKinds"/> then recognises only the predefined types, which is the
    /// same answer <c>MLQT.Style.DeclarationOrder</c> gives without a graph.
    /// </summary>
    private Func<string, bool>? TypeLookup()
        => _isSimpleType is null || _classPath.Count == 0
            ? null
            : typeName => _isSimpleType(_classPath.Peek(), typeName);

    /// <summary>
    /// Gets the rendered code lines.
    /// </summary>
    public List<string> Code => _code;

    public ModelicaRenderer(
        bool renderForCodeEditor = false, 
        bool showAnnotations = true, 
        bool excludeClassDefinitions = false, 
        BufferedTokenStream? tokenStream = null, 
        HashSet<string>? classNamesToExclude = null, 
        int maxLineLength = 100, 
        FormattingOptions? formatting = null,
        string? rootClassId = null,
        Func<string, string, bool>? isSimpleType = null)
    {
        _renderForCodeEditor = renderForCodeEditor;
        _showAnnotations = showAnnotations;
        _excludeClassDefinitions = excludeClassDefinitions;
        _tokenStream = tokenStream;
        _classNamesToExclude = classNamesToExclude;
        _maxLineLength = maxLineLength;

        var layout = formatting ?? FormattingOptions.None;
        _oneOfEachSection = layout.OneOfEachSection;
        _importsFirst = layout.ImportsFirst;
        _componentsBeforeClasses = layout.ComponentsBeforeClasses;
        _initialSectionsLast = layout.InitialSectionsLast;
        _declarationOrder = layout.DeclarationOrder;
        _isSimpleType = isSimpleType;
        if (!string.IsNullOrEmpty(rootClassId))
            _classPath.Push(rootClassId);
    }

    #region Helper Methods

    private string Keyword(string text)
    {
        return _renderForCodeEditor ? $"<KEYWORD>{text}</KEYWORD>" : text;
    }

    private string Ident(string text)
    {
        return _renderForCodeEditor ? $"<IDENT>{text}</IDENT>" : text;
    }

    private string Name(string text)
    {
        return _renderForCodeEditor ? $"<NAME>{text}</NAME>" : text;
    }

    private string Type(string text)
    {
        return _renderForCodeEditor ? $"<TYPE>{text}</TYPE>" : text;
    }

    private string FunctionCall(string text)
    {
        return _renderForCodeEditor ? $"<FUNCTION>{text}</FUNCTION>" : text;
    }

    private string Operator(string text, bool spaceAround=true)
    {
        switch (text)
        {
            case "^":
            case "*":
            case "/":
                return _renderForCodeEditor ? $"<OPERATOR>{text}</OPERATOR>" : text;
            default:
            if (spaceAround)
                return _renderForCodeEditor ? $" <OPERATOR>{text}</OPERATOR> " : " " + text + " ";
            else
                return _renderForCodeEditor ? $"<OPERATOR>{text}</OPERATOR>" : text;
        }
    }

    private string Sign(string text)
    {
        return _renderForCodeEditor ? $"<OPERATOR>{text}</OPERATOR>" : text;
    }

    private string Literal(string text)
    {
        if (text.StartsWith("\"") || text.StartsWith("'"))
            return _renderForCodeEditor ? $"<STRING>{text}</STRING>" : text;
        else
            return _renderForCodeEditor ? $"<NUMBER>{text}</NUMBER>" : text;
    }

    private string Comment(string text)
    {
        return _renderForCodeEditor ? $"<COMMENT>{text}</COMMENT>" : text;
    }

    private void Write(string text)
    {
        _currentLine.Append(text);
    }

    private void Space()
    {
        if (_currentLine.Length > 0 && _currentLine[_currentLine.Length - 1] != ' ')
        {
            _currentLine.Append(' ');
        }
    }

    private void EmitLine(bool ignoreIndentation=false)
    {
        if (_pendingMatrixLines is { } matrix)
        {
            _pendingMatrixLines = null;
            if (_indentLevel < matrix.Level)
                MoveLinesBack(matrix.Start, matrix.End, (matrix.Level - _indentLevel) * IndentSpaces);
        }

        var indent = new string(' ', _indentLevel * IndentSpaces);
        var line = _currentLine.ToString().TrimEnd();

        // Check if we should suppress indentation for this line
        bool shouldIgnoreIndent = ignoreIndentation || _suppressNextIndentation;
        _suppressNextIndentation = false; // Reset the flag

        if (!string.IsNullOrWhiteSpace(line) && shouldIgnoreIndent)
        {
            _code.Add(line);
            // Mark this line as exempt from post-processing indentation
            // (used for multi-line string content in public/protected sections)
            _noPostIndentLines.Add(_code.Count - 1);
        }
        else if (!string.IsNullOrWhiteSpace(line))
            _code.Add(indent + line);
        else if (line == string.Empty && _code.Count > 0)
            _code.Add("");

        _currentLine.Clear();
    }

    private void EmitEmptyLine()
    {
        //Avoid adding consecutive empty lines
        if (_code.Count > 0 && _code[_code.Count - 1].Trim() == "")
            return;
        _code.Add("");
    }

    /// <summary>
    /// Extracts the class name from a class_definition context.
    /// </summary>
    private string? GetClassNameFromDefinition(modelicaParser.Class_definitionContext context)
    {
        var specifier = context.class_specifier();
        if (specifier == null)
            return null;

        // Handle long class specifier
        if (specifier.long_class_specifier() != null)
        {
            var identTokens = specifier.long_class_specifier().IDENT();
            if (identTokens != null && identTokens.Length > 0)
            {
                return identTokens[0].GetText();
            }
        }
        // Handle short class specifier
        else if (specifier.short_class_specifier() != null)
        {
            var ident = specifier.short_class_specifier().IDENT();
            if (ident != null)
            {
                return ident.GetText();
            }
        }
        // Handle der class specifier
        else if (specifier.der_class_specifier() != null)
        {
            var identTokens = specifier.der_class_specifier().IDENT();
            if (identTokens != null && identTokens.Length > 0)
            {
                return identTokens[0].GetText();
            }
        }

        return null;
    }

    /// <summary>
    /// Get the plain text from the current line (without markup tags)
    /// </summary>
    private string GetCurrentLinePlainText()
    {
        var line = _currentLine.ToString();
        if (!_renderForCodeEditor)
            return line;

        // Remove markup tags to get plain text
        return System.Text.RegularExpressions.Regex.Replace(
            line,
            @"<(KEYWORD|IDENT|NAME|TYPE|OPERATOR|NUMBER|STRING|COMMENT)>(.*?)</\1>",
            "$2");
    }

    /// <summary>
    /// Get the length of the current line in plain text (excluding markup and indentation)
    /// </summary>
    private int GetCurrentLinePlainTextLength()
    {
        return GetCurrentLinePlainText().TrimStart().Length;
    }

    /// <summary>
    /// Check if current line exceeds maximum line length
    /// </summary>
    private bool IsLineTooLong()
    {
        if (_inDocumentationAnnotation)
            return false; // Don't wrap Documentation annotations
        return GetCurrentLinePlainTextLength() > _maxLineLength;
    }

    private void WriteMultiLineString(string stringLiteral)
    {
        // Check if the string contains newlines (multi-line string)
        if (!stringLiteral.Contains('\n'))
        {
            // Single-line string, just write it normally
            Write(Literal(stringLiteral).TrimEnd());
            return;
        }

        // For code editor rendering, wrap each line individually with <STRING> tags
        if (_renderForCodeEditor)
        {
            // Multi-line string with markup - wrap each line in its own STRING tag
            // IMPORTANT: Multi-line strings must NOT have code-level indentation added
            // because the string content already contains its own whitespace.
            var lines = stringLiteral.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                // Remove any trailing \r from each line
                var line = lines[i].TrimEnd('\r');

                if (i == 0)
                {
                    // First line - write with closing tag
                    Write($"<STRING>{line.TrimEnd()}</STRING>");
                }
                else
                {
                    // Emit the previous line. For the transition from line 0 to 1:
                    // - Line 0 contains actual code (like annotation structure) so it NEEDS normal indentation
                    // For line 1+ transitions:
                    // - The previous line contains pure string content, so ignore indentation
                    bool ignoreIndent = i >= 2;
                    EmitLine(ignoreIndentation: ignoreIndent);
                    Write($"<STRING>{line.TrimEnd()}</STRING>");

                    // For the last line, set flag to suppress indentation for whatever follows
                    // on the same physical line (e.g., the closing paren of the annotation)
                    if (i == lines.Length - 1)
                    {
                        _suppressNextIndentation = true;
                    }
                }
            }
        }
        else
        {
            // Non-editor rendering - preserve string content exactly as-is
            // IMPORTANT: Multi-line strings must NOT have code-level indentation added
            // because the string content already contains its own whitespace.
            // Adding code indentation would cause indentation to grow on each read/write cycle.
            var lines = stringLiteral.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                if (i == 0)
                {
                    // First line - write it to current line (code before string + first string part)
                    Write(lines[i].TrimEnd());
                }
                else
                {
                    // Emit the previous line. For the transition from line 0 to 1:
                    // - Line 0 contains actual code (like "assert(...)") so it NEEDS normal indentation
                    // For line 1+ transitions:
                    // - The previous line contains pure string content, so ignore indentation
                    bool ignoreIndent = i >= 2;
                    EmitLine(ignoreIndentation: ignoreIndent);
                    Write(lines[i].TrimEnd());

                    // For the last line, set flag to suppress indentation for whatever follows
                    // on the same physical line (e.g., the closing " and rest of the expression)
                    if (i == lines.Length - 1)
                    {
                        _suppressNextIndentation = true;
                    }
                }
            }
        }
    }

    private void InsertLineAt(string line, int lineNumber)
    {
        _code.Insert(lineNumber, line);

        // Shift all exempt line indices that are >= lineNumber by 1
        // since the insert moved them down
        var shiftedIndices = new HashSet<int>();
        foreach (var idx in _noPostIndentLines)
        {
            if (idx >= lineNumber)
                shiftedIndices.Add(idx + 1);
            else
                shiftedIndices.Add(idx);
        }
        _noPostIndentLines.Clear();
        foreach (var idx in shiftedIndices)
            _noPostIndentLines.Add(idx);
    }

    private void AddIndentAtLineStart(int lineNumber)
    {
        if (lineNumber < 0 || lineNumber >= _code.Count)
            return;

        // Skip lines that were marked as exempt from post-processing indent
        // (e.g., multi-line string content that should preserve its whitespace)
        if (_noPostIndentLines.Contains(lineNumber))
            return;

        var line = _code[lineNumber];
        // Don't add indentation to empty lines (blank line separators should stay empty)
        if (string.IsNullOrEmpty(line))
            return;
        _code[lineNumber] = new string(' ', IndentSpaces) + line;
    }

    /// <summary>
    /// Moves lines <paramref name="start"/> to <paramref name="end"/> (exclusive) back by
    /// <paramref name="spaces"/> of their indent (B462). Only ever back: the line that ends a matrix
    /// is written at the level the matrix began at or an enclosing one, never a deeper one (none
    /// was, over the 8,367 files of MSL and Buildings), so there is nothing to move along.
    /// </summary>
    private void MoveLinesBack(int start, int end, int spaces)
    {
        for (int i = start; i < end; i++)
        {
            var line = _code[i];
            int leading = line.Length - line.TrimStart(' ').Length;
            _code[i] = line[Math.Min(leading, spaces)..];
        }
    }

    /// <summary>
    /// Indents a line a matrix has just started (B462) - after a row break or a comment, so nothing
    /// is on it yet but its continuation indent - by the leading spaces of the line the matrix
    /// began on, so its rows stay a level in from that line when it is itself a continuation.
    /// </summary>
    private void IndentMatrixLine(int baseSpaces)
        => _currentLine.Insert(0, new string(' ', baseSpaces));

    /// <summary>
    /// Ends the line and starts the next a level in from the line the matrix began on, for a row
    /// that starts a line in the source (B462, B463).
    /// </summary>
    private void StartMatrixRowLine(int baseSpaces)
    {
        EmitLine();
        AddIndentToCurrentLine();
        IndentMatrixLine(baseSpaces);
    }

    private void AddIndentToCurrentLine()
    {
        var indent = new string(' ', IndentSpaces);
        var line = indent + _currentLine.ToString();
        _currentLine.Clear();
        _currentLine.Append(line);
    }

    private void Indent()
    {
        _indentLevel++;
    }

    private void Dedent()
    {
        if (_indentLevel > 0)
            _indentLevel--;
    }

    #endregion

    #region Top-Level and Class Definition Visitors
    
    public override object? VisitStored_definition([NotNull] modelicaParser.Stored_definitionContext context)
    {
        var children = context.children;
        if (children != null)
        {
            for (int i = 0; i < children.Count; i++)
            {
                var child = children[i];
                if (child is modelicaParser.C_commentContext cCommentCtx)
                {
                    // Handle leading C comments (// and /* */).
                    // VisitC_comment ends the line itself, so do not end it again — that put a blank
                    // line after every comment at the top of a file, including between consecutive
                    // lines of the same comment block.
                    Visit(cCommentCtx);
                }
                else if (child is ITerminalNode terminal && terminal.GetText() == "within")
                {
                    Write(Keyword("within"));

                    // The space belongs to the name, not to the keyword. A top-level file has no
                    // name, and writing it anyway produced `within ;` - valid Modelica, but a
                    // different spelling from the `within;` WithinClause.Ensure writes, which is
                    // the one place CLAUDE.md says adds this clause. Every save re-renders the
                    // whole file, so the difference reached every top-level package.mo MLQT
                    // touched (B227).
                    if (i + 1 < children.Count && children[i + 1] is modelicaParser.NameContext nameCtx)
                    {
                        Space();
                        Visit(nameCtx);
                        i++;
                    }
                    while (i + 1 < children.Count && children[i + 1].GetText() != ";") i++;
                    if (i + 1 < children.Count && children[i + 1].GetText() == ";")
                    {
                        Write(";");
                        i++;
                    }
                    EmitLine();
                }
                else if (child.GetText() == "final")
                {
                    Write(Keyword("final"));
                    Space();
                }
                else if (child is modelicaParser.Class_definitionContext classDef)
                {
                    Visit(classDef);
                    Write(";");
                    EmitLine();

                    if (i + 1 < children.Count && children[i + 1].GetText() == ";") i++;

                    // Add a blank line between class definitions, but not after the last one
                    bool hasMoreClassDefs = false;
                    for (int j = i + 1; j < children.Count; j++)
                    {
                        if (children[j] is modelicaParser.Class_definitionContext)
                        {
                            hasMoreClassDefs = true;
                            break;
                        }
                    }
                    if (hasMoreClassDefs)
                        EmitEmptyLine();
                }
            }
        }
        return null;
    }

    public override object? VisitClass_definition([NotNull] modelicaParser.Class_definitionContext context)
    {
        if (context.GetChild(0)?.GetText() == "encapsulated")
        {
            Write(Keyword("encapsulated"));
            Space();
        }
        Visit(context.class_prefixes());
        Space();

        // The outermost class is the root the caller named; anything below it is a nested class,
        // which resolves type names in its own scope. Only extended when there is a path to extend —
        // with no root the lookup has nothing to key on and is not called at all.
        _classDepth++;
        var extended = _classDepth > 1 && _classPath.Count > 0
            && GetClassNameFromDefinition(context) is { } name;
        if (extended)
            _classPath.Push(_classPath.Peek() + "." + GetClassNameFromDefinition(context));
        try
        {
            Visit(context.class_specifier());
        }
        finally
        {
            if (extended)
                _classPath.Pop();
            _classDepth--;
        }
        return null;
    }

    public override object? VisitClass_prefixes([NotNull] modelicaParser.Class_prefixesContext context)
    {
        foreach (var child in context.children)
        {
            var text = child.GetText();
            if (!string.IsNullOrEmpty(text))
            {
                Write(Keyword(text));
                Space();
            }
        }
        return null;
    }

    public override object? VisitLong_class_specifier([NotNull] modelicaParser.Long_class_specifierContext context)
    {
        var identTokens = context.IDENT();
        if (context.GetChild(0)?.GetText() == "extends")
        {
            Write(Keyword("extends"));
            Space();
            if (identTokens != null && identTokens.Length >= 1)
                Write(Ident(identTokens[0].GetText()));
            if (context.class_modification() != null)
                Visit(context.class_modification());
            if (context.string_comment() != null) {
                Space();
                Visit(context.string_comment());
            }
            EmitLine();
        }
        else
        {
            if (identTokens != null && identTokens.Length >= 1)
                Write(Ident(identTokens[0].GetText()));
            Space();
            Visit(context.string_comment());
            EmitLine();
        }

        Visit(context.composition());

        Write(Keyword("end"));
        Space();
        if (identTokens != null && identTokens.Length >= 2)
            Write(Ident(identTokens[^1].GetText()));
        else if (identTokens != null && identTokens.Length >= 1)
            Write(Ident(identTokens[0].GetText()));
        return null;
    }

    public override object? VisitShort_class_specifier([NotNull] modelicaParser.Short_class_specifierContext context)
    {
        var ident = context.IDENT();
        if (ident != null)
            Write(Ident(ident.GetText()));
        Write(Operator("="));
        if (context.GetChild(2)?.GetText() == "enumeration")
        {
            Write(Keyword("enumeration"));
            Write("(");
            if (context.enum_list() != null)
            {
                // Check if we should format as multi-line
                var enumLiterals = context.enum_list().enumeration_literal();
                bool useMultiLine = enumLiterals != null && enumLiterals.Length > 0;

                // Comments after the '(' and before the ')' (B431): the run before the list and
                // the run after it (the one after that, before the description, cannot happen).
                var runs = CommentRuns(context);
                var opening = runs?[0] ?? default;
                var closing = runs is { Length: > 1 } ? runs[1] : default;

                if (opening.Any)
                    WriteOpeningComments(opening, useMultiLine);
                if (useMultiLine)
                {
                    if (!opening.Any)
                        EmitLine();
                    _indentLevel++;
                }

                Visit(context.enum_list());

                if (closing.Any)
                    WriteListComments(closing, useMultiLine, beforeClose: true);
                if (useMultiLine)
                {
                    EndLineBeforeClose(closing.Any);
                    _indentLevel--;
                }
            }
            else if (context.GetChild(4)?.GetText() == ":")
                Write(":");
            Write(")");
        }
        else
        {
            Visit(context.base_prefix());
            Visit(context.type_specifier());
            if (context.array_subscripts() != null)
                Visit(context.array_subscripts());
            if (context.class_modification() != null)
                Visit(context.class_modification());
        }
        Visit(context.comment());
        return null;
    }

    public override object? VisitDer_class_specifier([NotNull] modelicaParser.Der_class_specifierContext context)
    {
        var idents = context.IDENT();
        // First IDENT is the type name being defined
        if (idents != null && idents.Length >= 1)
            Write(Ident(idents[0].GetText()));
        Write(Operator("="));
        Write(Keyword("der"));
        Write("(");
        // Visit the base type_specifier
        Visit(context.type_specifier());
        // Write remaining IDENTs (derivative variables) separated by commas
        if (idents != null)
        {
            for (int i = 1; i < idents.Length; i++)
            {
                Write(",");
                Space();
                Write(Ident(idents[i].GetText()));
            }
        }
        Write(")");
        Visit(context.comment());
        return null;
    }
    #endregion

    #region Composition and Element Visitors
    private enum CodeSection
    {
        Any,
        Public,
        Protected,
        Equation,
        Algorithm,
        InitialEquation,
        InitialAlgorithm,
        External
    }

    /// <summary>
    /// Which elements one <see cref="WriteComposition"/> pass writes. The five declaration kinds are
    /// here rather than inside <see cref="Components"/> because writing them in order means writing
    /// them in five passes — the renderer's way of ordering anything is to walk the section once per
    /// group, which is what kept all declarations in one undifferentiated group until B252.
    /// </summary>
    private enum Element
    {
        Any,
        Imports,
        Extends,
        Components,
        Classes,
        ClassAndComponents,
        InputsOutputs,
        Constants,
        Parameters,
        Variables,
        ComponentInstances
    }

    /// <summary>The declaration kind one <see cref="Element"/> pass writes, or null if it is not a
    /// declaration pass.</summary>
    private static DeclarationKind? KindWritten(Element element) => element switch
    {
        Element.InputsOutputs => DeclarationKind.InputOutput,
        Element.Constants => DeclarationKind.Constant,
        Element.Parameters => DeclarationKind.Parameter,
        Element.Variables => DeclarationKind.Variable,
        Element.ComponentInstances => DeclarationKind.Component,
        _ => null
    };

    /// <summary>The passes that write the declaration group, in the order they are written.</summary>
    private static readonly Element[] DeclarationPasses =
    {
        Element.InputsOutputs, Element.Constants, Element.Parameters,
        Element.Variables, Element.ComponentInstances
    };

    /// <summary>
    /// How the declaration group is written: as one pass in source order, or as one pass per kind.
    /// One call site per section so the public and protected halves cannot be given different
    /// conventions — which is how the two initial-section orderings came to need
    /// <see cref="WriteInitialSections"/>.
    ///
    /// <para>A record is always written in source order, because its field order is its
    /// constructor's signature — see <see cref="DeclarationKinds.KeepsSourceOrder"/>, which the rule
    /// asks too (B304).</para>
    /// </summary>
    private IEnumerable<Element> DeclarationGroupPasses(modelicaParser.CompositionContext context)
        => _declarationOrder && !DeclarationKinds.KeepsSourceOrder(context)
            ? DeclarationPasses
            : new[] { Element.Components };

    /// <summary>
    /// Writes the <c>initial equation</c> and <c>initial algorithm</c> sections. Called either before
    /// or after the ordinary ones — see <see cref="_initialSectionsLast"/> — so the two orderings
    /// cannot drift apart, which they would the moment this was written out twice.
    /// </summary>
    private void WriteInitialSections(modelicaParser.CompositionContext context)
    {
        _currentSection.Push(CodeSection.InitialEquation);
        _writtenSectionHeader = false;
        WriteComposition(context, CodeSection.InitialEquation, Element.Any);
        _currentSection.Pop();

        _currentSection.Push(CodeSection.InitialAlgorithm);
        _writtenSectionHeader = false;
        WriteComposition(context, CodeSection.InitialAlgorithm, Element.Any);
        _currentSection.Pop();
    }

    private bool WriteComposition(
        [NotNull] modelicaParser.CompositionContext context, 
        CodeSection section, 
        Element elements,
        // Only ever read for CodeSection.Protected - the public branch writes no marker at all,
        // because public is Modelica's default section and MLQT does not announce it. It used to
        // be passed as `true` at six public call sites, where it did nothing and read as though it
        // did; those now leave it alone (B227).
        bool alreadyWrittenSectionMarker = false)
    {
        var elementList = context.element_list();
        var children = context.children;
        var elementCounter = 0;
        bool externalElement = false;
        
        _currentElement.Push(elements);
        
        for (int i = 0; i < children.Count; i++)
        {
            var child = children[i];
            var text = SectionKeyword.Of(child);   // never GetText() on a rule node: see SectionKeyword

            if (text == "public" && (section == CodeSection.Any || section==CodeSection.Public))
            {
                //We should only write public if there are elements in the public section
                //There might not be if we are excluding class definitions from the code generation
                //We also don't want the public keyword if we are forcing the code order
                int numberOfLines = _code.Count;
                Visit(elementList[elementCounter]);
                if (_code.Count > numberOfLines) {
                    if (section != CodeSection.Public) {
                        InsertLineAt(Keyword("public"), numberOfLines);
                        numberOfLines++;
                    }
                    for (int j=numberOfLines; j < _code.Count; j++) {
                        AddIndentAtLineStart(j);
                    }
                }
                elementCounter++;
                i++;
            }
            else if (text == "public")
            {
                elementCounter++;
                i++;
            }
            else if (text == "protected" && (section == CodeSection.Any || section==CodeSection.Protected))
            {
                //We should only write protected if there are elements in the protected section
                //There might not be if we are excluding class definitions from the code generation
                int numberOfLines = _code.Count;
                Visit(elementList[elementCounter]);
                if (_code.Count > numberOfLines) {
                    if (section==CodeSection.Any || !alreadyWrittenSectionMarker) {
                        InsertLineAt(Keyword("protected"), numberOfLines);
                        numberOfLines++;
                        alreadyWrittenSectionMarker = true;
                    }
                    for (int j=numberOfLines; j < _code.Count; j++) {
                        AddIndentAtLineStart(j);
                    }
                }
                elementCounter++;
                i++;
            }
            else if (text == "protected")
            {
                elementCounter++;
                i++;
            }
            else if (child is modelicaParser.Equation_sectionContext && child.GetChild(0)?.GetText() == "initial" && !_excludeClassDefinitions  && (section == CodeSection.Any || section==CodeSection.InitialEquation))
            {         
                Visit(child);
            }
            else if (child is modelicaParser.Equation_sectionContext && child.GetChild(0)?.GetText() != "initial" &&!_excludeClassDefinitions  && (section == CodeSection.Any || section==CodeSection.Equation))
            {                
                Visit(child);
            }
            else if (child is modelicaParser.Algorithm_sectionContext && child.GetChild(0)?.GetText() == "initial" && !_excludeClassDefinitions  && (section == CodeSection.Any || section==CodeSection.InitialAlgorithm))
            {
                Visit(child);
            }
            else if (child is modelicaParser.Algorithm_sectionContext && child.GetChild(0)?.GetText() != "initial" && !_excludeClassDefinitions  && (section == CodeSection.Any || section==CodeSection.Algorithm))
            {
                Visit(child);
            }
            else if (text == "external" && !_excludeClassDefinitions  && (section == CodeSection.Any || section==CodeSection.External))
            {
                externalElement = true;
                Write(Keyword("external"));

                // Handle optional language specification (e.g., "C")
                if (context.language_specification() != null)
                {
                    Space();
                    Visit(context.language_specification());
                }

                // Handle optional external function call (e.g., externalFunction(Integer x))
                if (context.external_function_call() != null)
                {
                    Space();
                    Visit(context.external_function_call());
                }

                // The clause's own annotation, found by where it stands: the composition's first
                // annotation is the leading class annotation when there is one (B446).
                if (CompositionAnnotations.External(context) is { } externalAnnotation)
                {
                    EmitLine();
                    _withAnnotation = true;
                    Indent();
                    Visit(externalAnnotation);
                }

                Write(";");
                EmitLine();
                if (_withAnnotation) {
                    Dedent();
                    _withAnnotation = false;
                }
            }
            else if (child is modelicaParser.Element_listContext && (section == CodeSection.Any || section==CodeSection.Public))
            {
                //default element list handling
                Indent();
                Visit(elementList[elementCounter]);
                Dedent();
                elementCounter++;
            }
            else if (child is modelicaParser.Element_listContext)
            {
                elementCounter++;
            }
            else if (child is modelicaParser.C_commentContext &&
                     (section == CodeSection.Any || section == CodeSection.External) &&
                     !IsBeforeElementList(context, i))
            {
                // Comments before the leading annotation (B432) are written with it, at the end.
                Visit(child);
            }
            
        }    

        _currentElement.Pop();
        return (section == CodeSection.External || section == CodeSection.Any) ? externalElement : alreadyWrittenSectionMarker;
    }

    public override object? VisitComposition([NotNull] modelicaParser.CompositionContext context)
    {
        // Handle public/protected sections
        // A leading annotation will be automatically pushed to the end of the file as per the Modelica spec
        // even though it is allowed in the grammar for compatibility with Dymola
        if (context.children != null)
        {
            if (_oneOfEachSection)
            {
                _writeFinalComments = false;

                if (_importsFirst) {
                    //Collect all imports at the top of the class
                    WriteComposition(context, CodeSection.Public, Element.Imports);
                    // True, and it matters here: this lifts the protected imports to the top of the
                    // file, above where the protected keyword will go, so the section must not be
                    // announced around them. The public calls below pass nothing because the flag
                    // is never read for a public section.
                    WriteComposition(context, CodeSection.Protected, Element.Imports, alreadyWrittenSectionMarker: true);

                    //Public section
                    WriteComposition(context, CodeSection.Public, Element.Extends);
                    if (_componentsBeforeClasses) {
                        foreach (var pass in DeclarationGroupPasses(context))
                            WriteComposition(context, CodeSection.Public, pass);
                        _writeFinalComments = true;
                        WriteComposition(context, CodeSection.Public, Element.Classes);
                    }
                    else {
                        _writeFinalComments = true;
                        WriteComposition(context, CodeSection.Public, Element.ClassAndComponents);
                    }
                }
                else 
                    WriteComposition(context, CodeSection.Public, Element.Any);

                //Protected section
                _writeFinalComments = false;
                if (_importsFirst) {
                    var alreadyWrittenSectionMarker = WriteComposition(context, CodeSection.Protected, Element.Extends);
                    if (_componentsBeforeClasses) {
                        foreach (var pass in DeclarationGroupPasses(context))
                            alreadyWrittenSectionMarker = WriteComposition(context, CodeSection.Protected, pass, alreadyWrittenSectionMarker);
                        _writeFinalComments = true;
                        WriteComposition(context, CodeSection.Protected, Element.Classes, alreadyWrittenSectionMarker);
                    }
                    else {
                        _writeFinalComments = true;
                        WriteComposition(context, CodeSection.Protected, Element.ClassAndComponents, false);
                    }
                }
                else
                    WriteComposition(context, CodeSection.Protected, Element.Any, false);

                // Initial and ordinary equation/algorithm sections, in whichever order the
                // repository's convention asks for. External always goes last: it is the class's
                // implementation, not a section anyone orders against.
                if (!_initialSectionsLast)
                    WriteInitialSections(context);

                //Equation/Algorithm/External
                _currentSection.Push(CodeSection.Equation);                       
                _writtenSectionHeader = false;               
                WriteComposition(context, CodeSection.Equation, Element.Any);
                _currentSection.Pop();

                _currentSection.Push(CodeSection.Algorithm);                       
                _writtenSectionHeader = false;               
                WriteComposition(context, CodeSection.Algorithm, Element.Any);
                _currentSection.Pop();

                if (_initialSectionsLast)
                    WriteInitialSections(context);

                WriteComposition(context, CodeSection.External, Element.Any);
            }
            else 
            {
                _currentSection.Push(CodeSection.Any);        
                WriteComposition(context, CodeSection.Any, Element.Any);
                _currentSection.Pop();
            }
        }

        // The class's own annotations, leading and trailing. The external clause's was written with
        // the clause, and is told apart by its position rather than its index (B446).
        if (_showAnnotations) {
            var annotations = CompositionAnnotations.Of(context);
            foreach (var annotation in annotations.ClassLevel)
            {
                _classAnnotation = true;
                // The leading annotation moves to the end, and the comments before it (B432)
                // move with it, each on a line of its own. They go above the blank line, where
                // a comment before a trailing annotation is written: anywhere else, the next
                // save would read them as that and move them again.
                // Asked by position, not as Leading: an annotation alone in its body is the trailing
                // one (B457), and its comments are still before the element list, skipped there.
                if (context.children is { } children && IsBeforeElementList(context, children.IndexOf(annotation)))
                {
                    Indent();
                    foreach (var comment in children.TakeWhile(c => !ReferenceEquals(c, annotation)).OfType<modelicaParser.C_commentContext>())
                        Visit(comment);
                    Dedent();
                }
                EmitEmptyLine();
                Indent();
                Visit(annotation);
                Write(";");
                EmitLine();
                Dedent();
                _classAnnotation = false;
            }
        }

        //Handle final c style comments if any
        var cComments = context.final_comment();
        if (cComments != null)
        {
            Visit(cComments);
        }

        return null;
    }

    /// <summary>
    /// True when the composition's child at <paramref name="index"/> comes before its first
    /// element_list - the leading annotation and the comments before it (B432). An element_list is
    /// always there, possibly empty, so everything after the leading part comes after one.
    /// </summary>
    private static bool IsBeforeElementList(modelicaParser.CompositionContext context, int index)
    {
        for (int j = 0; j < index && j < context.children.Count; j++)
            if (context.children[j] is modelicaParser.Element_listContext)
                return false;
        return index >= 0;
    }

    public override object? VisitFinal_comment([NotNull] modelicaParser.Final_commentContext context)
    {      
        if (context.c_comment() != null) {
            foreach (var cComment in context.c_comment())
            {
                if (!string.IsNullOrWhiteSpace(cComment.GetText()))
                    Visit(cComment);
            }
        }
        return null;
    }

    public override object? VisitLanguage_specification([NotNull] modelicaParser.Language_specificationContext context)
    {
        // Language specification is a STRING like "C", "FORTRAN 77", etc.
        var stringToken = context.STRING();
        if (stringToken != null)
        {
            Write(Literal(stringToken.GetText()));
        }
        return null;
    }

    public override object? VisitExternal_function_call([NotNull] modelicaParser.External_function_callContext context)
    {
        // Handle optional return value: component_reference '='
        if (context.component_reference() != null)
        {
            Visit(context.component_reference());
            Write(Operator("="));
        }

        // Handle function name (IDENT)
        var identToken = context.IDENT();
        if (identToken != null)
        {
            Write(Ident(identToken.GetText()));
        }

        // Handle function arguments: '(' (expression_list)? ')'
        Write("(");
        if (context.expression_list() != null)
        {
            Visit(context.expression_list());
        }
        Write(")");

        return null;
    }

    private void WriteElement([NotNull] modelicaParser.ElementContext context)
    {
        Visit(context);
        if (_currentLine.Length == 0)
            return;
        Write(";");
        EmitLine();
    }

    private void WriteCommentIfProceedsThisElement([NotNull] modelicaParser.Element_listContext context, int i)
    {
        //Find out how many lines of comments there are       
        int firstComment = i;
        var child = context.GetChild(firstComment);
        if (child is modelicaParser.C_commentContext) {
            while (child is modelicaParser.C_commentContext)
            {
                firstComment--;
                if (firstComment > 0)
                    child = context.GetChild(firstComment);
                else {
                    firstComment = 0;
                    break;
                }
            }
            for (int j = firstComment; j <= i; j++)
            {
                child = context.GetChild(j);
                Visit(child);
            }
        }
    }

    public override object? VisitElement_list([NotNull] modelicaParser.Element_listContext context)
    {
        int elementCounter=0;

        // A record's extends clause is written where it stands among its fields, not lifted to the
        // top: the inherited fields take its place in the constructor's inputs (B378).
        var extendsInPlace = context.Parent is modelicaParser.CompositionContext composition
                             && DeclarationKinds.KeepsSourceOrder(composition);

        // Process all children (elements and comments) in order
        for (int i = 0; i < context.ChildCount; i++)
        {
            var child = context.GetChild(i);

            // Visit c_comment nodes
            if (child is modelicaParser.C_commentContext)
            {
                if (_currentElement.Peek() == Element.Any)
                {
                    Visit(child);
                }
            }
            // Visit element nodes
            else if (child is modelicaParser.ElementContext)
            {
                modelicaParser.ElementContext element = context.element()[elementCounter];
                if (_currentElement.Peek() == Element.Any)
                {
                    WriteElement(element);
                } 
                else if (_currentElement.Peek() == Element.Imports && element.import_clause() != null)
                {
                    WriteCommentIfProceedsThisElement(context, i - 1);
                    WriteElement(element);
                }
                else if (_currentElement.Peek() == Element.Extends && element.extends_clause() != null && !extendsInPlace)
                {
                    WriteCommentIfProceedsThisElement(context, i - 1);
                    WriteElement(element);
                }
                else if ((_currentElement.Peek() == Element.Components || _currentElement.Peek() == Element.ClassAndComponents)
                         && (element.component_clause() != null || (extendsInPlace && element.extends_clause() != null)))
                {
                    WriteCommentIfProceedsThisElement(context, i - 1);
                    WriteElement(element);
                }
                else if (KindWritten(_currentElement.Peek()) is { } wanted
                         && element.component_clause() is { } clause
                         && DeclarationKinds.KindOf(clause, TypeLookup()) == wanted)
                {
                    WriteCommentIfProceedsThisElement(context, i - 1);
                    WriteElement(element);
                }
                else if ((_currentElement.Peek() == Element.Classes || _currentElement.Peek() == Element.ClassAndComponents) && element.class_definition() != null)
                {
                    WriteCommentIfProceedsThisElement(context, i - 1);
                    WriteElement(element);
                }
                elementCounter++;
            }
        }

        //Write any final comments
        if (_currentElement.Peek() != Element.Any && _writeFinalComments)
        {
            var child = context.GetChild(context.ChildCount - 1);
            if (child is modelicaParser.C_commentContext)
            {
                WriteCommentIfProceedsThisElement(context, context.ChildCount - 2);
                Visit(child);
            }
        }

        return null;
    }

    public override object? VisitElement([NotNull] modelicaParser.ElementContext context)
    {
        // Handle import clause
        if (context.import_clause() != null)
        {
            Visit(context.import_clause());
            return null;
        }

        // Handle extends clause
        if (context.extends_clause() != null)
        {
            Visit(context.extends_clause());
            return null;
        }

        // Collect element-level prefix keywords (redeclare/final/inner/outer/replaceable).
        // These are part of the element rule, not the class_definition or component_clause rules.
        bool hasReplaceable = false;
        bool hasAnyPrefix = false;
        if (!_excludeClassDefinitions)
        {
            var children = context.children;
            if (children != null)
            {
                foreach (var child in children)
                {
                    var text = child.GetText();
                    if (text == "replaceable")
                        hasReplaceable = true;
                    if (text == "redeclare" || text == "final" || text == "inner" || text == "outer" || text == "replaceable")
                        hasAnyPrefix = true;
                }
            }
        }

        void WriteElementPrefixes()
        {
            if (!hasAnyPrefix) return;
            var children = context.children;
            if (children == null) return;
            foreach (var child in children)
            {
                var text = child.GetText();
                if (text == "redeclare" || text == "final" || text == "inner" || text == "outer" || text == "replaceable")
                {
                    Write(Keyword(text));
                    Space();
                }
            }
        }

        // Handle class definition or component clause
        if (context.class_definition() != null)
        {
            // Check if this specific class should be excluded
            bool shouldExclude = _excludeClassDefinitions;

            if (_classNamesToExclude != null && !shouldExclude)
            {
                // Get the class name from the class_definition
                var className = GetClassNameFromDefinition(context.class_definition());
                if (className != null && _classNamesToExclude.Contains(className))
                {
                    shouldExclude = true;
                }
            }

            if (!shouldExclude)
            {
                // Replaceable classes and short class definitions (e.g. type Foo = Real;) are
                // treated like declarations (no blank line separator).
                // Only long class definitions (with end keyword) get a blank line separator.
                if (!hasReplaceable && context.class_definition().class_specifier().long_class_specifier() != null)
                    EmitEmptyLine();
                WriteElementPrefixes();
                Visit(context.class_definition());
            }
        }
        else if (context.component_clause() != null)
        {
            WriteElementPrefixes();
            Visit(context.component_clause());
        }

        // Handle constraining clause (for replaceable elements)
        if (context.constraining_clause() != null && !_excludeClassDefinitions)
        {
            // Comments before 'constrainedby' (B432), each where it stood, a level in as the
            // clause is. They end their line, so the clause must not end it again.
            if (context.c_comment() is { Length: > 0 } comments)
            {
                WriteLeadingComments(comments);
                _constrainedbyOnFreshLine = true;
            }
            Visit(context.constraining_clause());

            // Only visit comment if it has actual content
            if (context.comment() != null && !string.IsNullOrWhiteSpace(context.comment().GetText()))
            {
                Visit(context.comment());
            }
        }

        return null;
    }

    public override object? VisitImport_clause([NotNull] modelicaParser.Import_clauseContext context)
    {
        Write(Keyword("import"));
        Space();

        // Check for different import patterns
        if (context.IDENT() != null)
        {
            // import A = B.C;
            Write(Ident(context.IDENT().GetText()));
            Write(Operator("="));
        }

        _nameAsType=true;
        Visit(context.name());
        _nameAsType=false;

        if (context.GetText().Contains(".*"))
            Write(".*");

        else if (context.import_list() != null)
        {
            Write(".{");
            Visit(context.import_list());
            Write("}");
        }

        // Only visit comment if it has actual content
        if (context.comment() != null && !string.IsNullOrWhiteSpace(context.comment().GetText()))
        {
            Space();
            Visit(context.comment());
        }

        return null;
    }

    public override object? VisitImport_list([NotNull] modelicaParser.Import_listContext context)
    {
        var idents = context.IDENT();
        if (idents != null)
        {
            for (int i = 0; i < idents.Length; i++)
            {
                if (i > 0)
                {
                    Write(",");
                    Space();
                }
                Write(Ident(idents[i].GetText()));
            }
        }
        return null;
    }

    public override object? VisitExtends_clause([NotNull] modelicaParser.Extends_clauseContext context)
    {
        Write(Keyword("extends"));
        Space();
        Visit(context.type_specifier());
        if (context.class_or_inheritence_modification() != null){
            Visit(context.class_or_inheritence_modification());
        }
        if (_showAnnotations && context.annotation() != null)
        {
            _withAnnotation = true;
            // Comments before the annotation (B432) go with it and are hidden with it, as in
            // VisitComment. They end their line, so it is not ended again.
            if (context.c_comment() is { Length: > 0 } comments)
            {
                WriteLeadingComments(comments);
                EndLineIfAny();
            }
            else
                EmitLine();
            Indent();
            Visit(context.annotation());
            Dedent();
            //Handle the case where the annotation is a single line and we need to add the indentation
            //But skip if the annotation ended with a multi-line string (which sets _suppressNextIndentation)
            if (_currentLine.Length > 0 && !_suppressNextIndentation)
                AddIndentToCurrentLine();
        }

        return null;
    }

    public override object? VisitClass_or_inheritence_modification([NotNull] modelicaParser.Class_or_inheritence_modificationContext context)
    {
        // Count arguments (both regular arguments and inheritence modifications)
        int numArguments = ModelicaRendererHelper.CountArgumentsInInheritenceList(context.argument_or_inheritence_list());

        // Comments after the '(' and before the ')' (B431), as in VisitClass_modification.
        var runs = CommentRuns(context);
        var opening = runs?[0] ?? default;
        var closing = runs is { Length: > 1 } ? runs[^1] : default;

        // Special case: simple 2-argument graphics elements (like Line) stay on one line
        if (_inGraphicsAnnotationLevel == 2 && numArguments == 2)
        {
            Write("(");
            if (opening.Any)
                WriteOpeningComments(opening, multiLine: false);
            if (context.argument_or_inheritence_list() != null)
                Visit(context.argument_or_inheritence_list());
            if (closing.Any)
                WriteListComments(closing, multiLine: false, beforeClose: true);
            Write(")");
            return null;
        }

        // Check if this is Icon with a single-line graphics array
        bool isIconWithSingleLineGraphics = GetCurrentLinePlainText().Trim().EndsWith("Icon") && ModelicaRendererHelper.HasSingleLineGraphicsInInheritence(context);

        // Check if this is an annotation containing only Icon with single-line graphics (entire annotation fits on one line)
        bool isAnnotationWithSingleLineIcon = GetCurrentLinePlainText().Trim().EndsWith("annotation") && ModelicaRendererHelper.HasOnlyIconWithSingleLineGraphicsInInheritence(context);

        // Calculate nesting depth to determine if we should use multi-line formatting
        int maxNestingDepth = ModelicaRendererHelper.GetMaxNestingDepthInInheritence(context);

        // Check if the modification content would exceed the max line length
        var modificationText = context.argument_or_inheritence_list()?.GetText() ?? "";
        bool wouldExceedLineLength = !_inDocumentationAnnotation && numArguments >= 1 &&
                                     (GetCurrentLinePlainTextLength() + 1 + modificationText.Length + 1) > _maxLineLength;

        // Check if this is a class annotation (detected by current line ending with "annotation" and _classAnnotation flag)
        // OR if we're inside a class annotation visiting Icon
        // OR if we're inside Icon at class annotation level (for children like coordinateSystem)
        bool isInClassAnnotation = (GetCurrentLinePlainText().Trim().EndsWith("annotation") && _classAnnotation) ||
                                   (GetCurrentLinePlainText().Trim().EndsWith("Icon") && _classAnnotation) ||
                                   _inClassAnnotationIcon;

        // Use multi-line formatting (opening/closing parens on separate lines) if:
        // 1. More than 5 arguments, OR
        // 2. Nesting depth >= 2 AND we have multiple args, BUT not for 2-arg graphics elements or Icon/annotation with single-line graphics
        // 3. In class annotation context with >= 1 arguments (class annotations and Icon always use multi-line),
        //    BUT allow Icon/annotation with single-line graphics to stay on one line
        // 4. In graphics annotation with complex structure, OR
        // 5. Parent is using multi-line and we have >= 2 arguments, OR
        // 6. Line would exceed max length with the modification content (e.g. long extends clauses)
        bool useMultiLineParens = !isIconWithSingleLineGraphics && !isAnnotationWithSingleLineIcon && (
                                  numArguments > 5 ||
                                  (maxNestingDepth >= 2 && numArguments >= 2 && !(_inGraphicsAnnotationLevel == 2 && numArguments == 2)) ||
                                  (_inAnnotation && numArguments >= 2 && _inGraphicsAnnotationLevel != 2) ||
                                  (isInClassAnnotation && numArguments >= 1) ||
                                  (_inGraphicsAnnotationLevel <= 1 && !_inDeclaration && numArguments > 2) ||
                                  (_parentUsingMultiLine && numArguments >= 2 && !(_inGraphicsAnnotationLevel == 2 && numArguments == 2)) ||
                                  wouldExceedLineLength);

        if (useMultiLineParens)
            _inDeclaration = false;

        // Save and set parent multi-line state
        // Note: Use useMultiLineParens here (not oneModifierPerLine) so child knows parent called Indent()
        bool previousParentState = _parentUsingMultiLine;
        _parentUsingMultiLine = useMultiLineParens;

        // Track if we're entering Icon at class annotation level
        bool wasInClassAnnotationIcon = _inClassAnnotationIcon;
        if (GetCurrentLinePlainText().Trim().EndsWith("Icon") && _classAnnotation)
            _inClassAnnotationIcon = true;

        Write("(");
        if (opening.Any)
            WriteOpeningComments(opening, useMultiLineParens);
        if (useMultiLineParens)
        {
            if (!opening.Any)
                EmitLine();
            Indent();
        }
        if (context.argument_or_inheritence_list() != null)
            Visit(context.argument_or_inheritence_list());
        if (closing.Any)
            WriteListComments(closing, useMultiLineParens, beforeClose: true);
        if (useMultiLineParens)
        {
            EndLineBeforeClose(closing.Any);
            Dedent();
            Write(")");
        }
        else
            Write(")");

        // Restore previous state
        _parentUsingMultiLine = previousParentState;
        _inClassAnnotationIcon = wasInClassAnnotationIcon;
        return null;
    }

    public override object? VisitArgument_or_inheritence_list([NotNull] modelicaParser.Argument_or_inheritence_listContext context)
    {
        // Format like argument_list - respect parent multi-line formatting
        var children = context.children;
        if (children != null)
        {
            bool first = true;
            int argCount = 0;

            // Count total arguments first
            for (int i = 0; i < children.Count; i++)
            {
                if (children[i] is modelicaParser.ArgumentContext || children[i] is modelicaParser.Inheritence_modificationContext)
                    argCount++;
            }

            var runs = CommentRuns(context);
            int item = 0;
            for (int i = 0; i < children.Count; i++)
            {
                var child = children[i];
                if (child is modelicaParser.ArgumentContext || child is modelicaParser.Inheritence_modificationContext)
                {
                    var run = runs?[item] ?? default;
                    item++;
                    if (!first && run.Any)
                    {
                        // Comments after the ',' (B431), as in VisitArgument_list.
                        Write(",");
                        WriteListComments(run, _parentUsingMultiLine);
                        Visit(child);
                    }
                    else if (!first)
                    {
                        Write(",");

                        // Check if line is too long or will be too long with next argument
                        var nextArgText = child.GetText();
                        var estimatedLength = GetCurrentLinePlainTextLength() + 1 + nextArgText.Length;
                        bool needsWrapForLength = !_inDocumentationAnnotation && estimatedLength > (_maxLineLength - 3);

                        // Decide whether to write on one line or multiple lines
                        // Wrap if: parent is using multi-line mode, more than 2 args, line starts with ), OR line is too long
                        bool shouldWrap = _parentUsingMultiLine || argCount > 2 || GetCurrentLinePlainText().StartsWith(")") || needsWrapForLength;

                        if (shouldWrap)
                        {
                            // If wrapping due to line length (not already in multi-line mode), add continuation indent.
                            // EmitLine must be called BEFORE Indent so the current line is flushed at the
                            // existing indent level — Indent only affects the continuation line.
                            bool needsExtraIndent = needsWrapForLength && !_parentUsingMultiLine;

                            EmitLine();
                            if (needsExtraIndent)
                                Indent();
                            // Only add continuation indent if wrapping due to line length
                            // When wrapping due to argCount > 2, parent class_or_inheritence_modification already called Indent()
                            if (needsExtraIndent)
                                AddIndentToCurrentLine();

                            Visit(child);

                            if (needsExtraIndent)
                                Dedent();
                        }
                        else
                        {
                            Space();
                            Visit(child);
                        }
                    }
                    else
                    {
                        Visit(child);
                    }
                    first = false;
                }
            }
        }
        return null;
    }

    public override object? VisitInheritence_modification([NotNull] modelicaParser.Inheritence_modificationContext context)
    {
        Write(Keyword("break"));
        Space();
        if (context.connect_clause() != null)
            Visit(context.connect_clause());
        else if (context.IDENT() != null)
            Write(Ident(context.IDENT().GetText()));
        return null;
    }

    public override object? VisitConstraining_clause([NotNull] modelicaParser.Constraining_clauseContext context)
    {
        if (_constrainedbyOnFreshLine)
        {
            _constrainedbyOnFreshLine = false;
            _currentLine.Clear();
        }
        else
            EmitLine();
        AddIndentToCurrentLine();
        Write(Keyword("constrainedby"));
        Space();
        if (context.type_specifier() != null)
            Visit(context.type_specifier());
        if (context.class_modification() != null)
            Visit(context.class_modification());

        return null;
    }

    #endregion

    #region Component and Declaration Visitors

    public override object? VisitComponent_clause([NotNull] modelicaParser.Component_clauseContext context)
    {
        // Type prefix (flow, stream, discrete, parameter, constant, input, output)
        if (context.type_prefix() != null)
            Visit(context.type_prefix());

        // Type specifier (the type name)
        if (context.type_specifier() != null)
            Visit(context.type_specifier());

        // Array subscripts for the type
        if (context.array_subscripts() != null)
            Visit(context.array_subscripts());

        Space();

        // Component list (variable declarations)
        if (context.component_list() != null)
            Visit(context.component_list());

        return null;
    }

    public override object? VisitComponent_declaration1([NotNull] modelicaParser.Component_declaration1Context context)
    {
        //declaration comment
        Space();
        if (context.declaration() != null)
            Visit(context.declaration());

        if (context.comment() != null)
            Visit(context.comment());


        return null;
    }

    public override object? VisitType_prefix([NotNull] modelicaParser.Type_prefixContext context)
    {
        var children = context.children;
        if (children != null)
        {
            foreach (var child in children)
            {
                var text = child.GetText();
                if (!string.IsNullOrEmpty(text))
                {
                    Write(Keyword(text));
                    Space();
                }
            }
        }
        return null;
    }

    public override object? VisitType_specifier([NotNull] modelicaParser.Type_specifierContext context)
    {
        // Handle optional leading '.' for absolute type paths
        if (context.GetChild(0)?.GetText() == ".")
        {
            Write(".");
        }
        if (context.name() != null)
        {
            Write(Type(context.name().GetText()));
        }
        return null;
    }

    public override object? VisitComponent_list([NotNull] modelicaParser.Component_listContext context)
    {
        var declarations = context.component_declaration();
        if (declarations != null)
        {
            for (int i = 0; i < declarations.Length; i++)
            {
                if (i > 0)
                {
                    Write(",");
                    Space();
                }
                Visit(declarations[i]);
            }
        }
        return null;
    }

    public override object? VisitComponent_declaration([NotNull] modelicaParser.Component_declarationContext context)
    {
        // Declaration (name, array subscripts, modification)
        if (context.declaration() != null)
            Visit(context.declaration());

        // Check if line is too long after declaration - if so, wrap before condition_attribute
        bool wrappedBeforeCondition = false;
        if (context.condition_attribute() != null)
        {
            if (IsLineTooLong())
            {
                EmitLine();
                Indent();
                AddIndentToCurrentLine();
                wrappedBeforeCondition = true;
            }
            else
            {
                Space();
            }
            Visit(context.condition_attribute());
        }

        // Check if line is too long or will be too long with comment - if so, wrap before comment
        if (context.comment() != null && !string.IsNullOrWhiteSpace(context.comment().GetText()))
        {
            // Estimate the length with the comment added (comment text + space before it)
            // Only consider string_comment, not annotation (annotation always goes on new line)
            var stringCommentText = DescriptionText(context.comment().string_comment());
            var estimatedLength = GetCurrentLinePlainTextLength() + 1 + stringCommentText.Length;
            bool willBeTooLong = !_inDocumentationAnnotation && estimatedLength > _maxLineLength;

            if (IsLineTooLong() || willBeTooLong)
            {
                EmitLine();
                // Add continuation indent without changing indent level
                // AddIndentToCurrentLine() adds 2 spaces, then EmitLine() will add (_indentLevel * 2) spaces
                // For models without public/protected: _indentLevel=1, so total = 2 + 2 = 4 spaces
                // For models with public/protected: _indentLevel=0, total = 2 + 0 = 2, then AddIndentAtLineStart adds 2 more = 4 spaces
                AddIndentToCurrentLine();
            }
            Visit(context.comment());
        }

        // Dedent if we wrapped before condition
        if (wrappedBeforeCondition)
            Dedent();

        return null;
    }

    public override object? VisitDeclaration([NotNull] modelicaParser.DeclarationContext context)
    {
        // Variable name
        var ident = context.IDENT();
        if (ident != null)
            Write(Ident(ident.GetText()));

        // Array subscripts
        if (context.array_subscripts() != null)
            Visit(context.array_subscripts());

        // Modification (initialization, etc.)
        bool wrappedBeforeModification = false;
        if (context.modification() != null)
        {
            // Check if line is too long - if so, wrap before modification
            if (IsLineTooLong())
            {
                EmitLine();
                Indent();
                AddIndentToCurrentLine();
                wrappedBeforeModification = true;
            }

            _inDeclaration=true;
            Visit(context.modification());
            _inDeclaration=false;

            if (wrappedBeforeModification)
                Dedent();
        }

        return null;
    }

    public override object? VisitModification([NotNull] modelicaParser.ModificationContext context)
    {
        // Class modification: (x=1, y=2)
        if (context.class_modification() != null)
        {
            Visit(context.class_modification());
            if (context.modification_expression() != null)
            {
                if (context.children[0].GetText() == ":=")
                    Write(Operator(":=", false));
                else
                    Write(Operator("=", false));
                Visit(context.modification_expression());
            }
        }
        // Assignment: = modification_expression
        else if (context.GetChild(0)?.GetText() == "=")
        {
            Write(Operator("=", false));
            if (context.modification_expression() != null)
                Visit(context.modification_expression());
        }
        // := modification_expression
        else if (context.GetChild(0)?.GetText() == ":=")
        {
            Write(Operator(":="));
            if (context.modification_expression() != null)
                Visit(context.modification_expression());
        }

        return null;
    }

    public override object? VisitModification_expression([NotNull] modelicaParser.Modification_expressionContext context)
    {
        // modification_expression can be either expression or 'break'
        if (context.expression() != null)
        {
            Visit(context.expression());
        }
        else if (context.GetText() == "break")
        {
            Write(Keyword("break"));
        }
        return null;
    }

    public override object? VisitClass_modification([NotNull] modelicaParser.Class_modificationContext context)
    {
        int numArguments = 0;
        if (context.argument_list() != null && context.argument_list().argument() != null)
            numArguments = context.argument_list().argument().Length;

        // Comments after the '(' and before the ')' (B431): the run before the list, and the run
        // after it - or, with no list, the one run.
        var runs = CommentRuns(context);
        var opening = runs?[0] ?? default;
        var closing = runs is { Length: > 1 } ? runs[^1] : default;

        // Special case: simple 2-argument graphics elements (like Line) stay on one line
        if (_inGraphicsAnnotationLevel == 2 && numArguments == 2)
        {
            Write("(");
            if (opening.Any)
                WriteOpeningComments(opening, multiLine: false);
            if (context.argument_list() != null)
                Visit(context.argument_list());
            if (closing.Any)
                WriteListComments(closing, multiLine: false, beforeClose: true);
            Write(")");
            return null;
        }

        // Check if this is Icon with a single-line graphics array
        bool isIconWithSingleLineGraphics = GetCurrentLinePlainText().Trim().EndsWith("Icon") && ModelicaRendererHelper.HasSingleLineGraphics(context);

        // Check if this is an annotation containing only Icon with single-line graphics (entire annotation fits on one line)
        bool isAnnotationWithSingleLineIcon = GetCurrentLinePlainText().Trim().EndsWith("annotation") && ModelicaRendererHelper.HasOnlyIconWithSingleLineGraphics(context);

        // Calculate nesting depth to determine if we should use multi-line formatting
        int maxNestingDepth = ModelicaRendererHelper.GetMaxNestingDepth(context);

        // Check if this is a class annotation (detected by current line ending with "annotation" and _classAnnotation flag)
        // OR if we're inside a class annotation visiting Icon
        // OR if we're inside Icon at class annotation level (for children like coordinateSystem)
        bool isInClassAnnotation = (GetCurrentLinePlainText().Trim().EndsWith("annotation") && _classAnnotation) ||
                                   (GetCurrentLinePlainText().Trim().EndsWith("Icon") && _classAnnotation) ||
                                   _inClassAnnotationIcon;

        // Use multi-line formatting (opening/closing parens on separate lines) if:
        // 1. More than 5 arguments, OR
        // 2. Nesting depth >= 2 AND we have multiple args, BUT not for 2-arg graphics elements or Icon/annotation with single-line graphics
        // 3. In class annotation context with >= 1 arguments (class annotations and Icon always use multi-line),
        //    BUT allow Icon/annotation with single-line graphics to stay on one line
        // 4. In graphics annotation with complex structure, OR
        // 5. Parent is using multi-line and we have >= 2 arguments
        bool useMultiLineParens = !isIconWithSingleLineGraphics && !isAnnotationWithSingleLineIcon && (
                                  numArguments > 5 ||
                                  (maxNestingDepth >= 2 && numArguments >= 2 && !(_inGraphicsAnnotationLevel == 2 && numArguments == 2)) ||
                                  (_inAnnotation && numArguments >= 2 && _inGraphicsAnnotationLevel != 2) ||
                                  (isInClassAnnotation && numArguments >= 1) ||
                                  (_inGraphicsAnnotationLevel <= 1 && !_inDeclaration && numArguments > 2) ||
                                  (_parentUsingMultiLine && numArguments >= 2 && !(_inGraphicsAnnotationLevel == 2 && numArguments == 2)));

        if (useMultiLineParens)
            _inDeclaration = false;

        // Save and set parent multi-line state
        // Note: Use useMultiLineParens here (not oneModifierPerLine) so child knows parent called Indent()
        bool previousParentState = _parentUsingMultiLine;
        _parentUsingMultiLine = useMultiLineParens;

        // Track if we're entering Icon at class annotation level
        bool wasInClassAnnotationIcon = _inClassAnnotationIcon;
        if (GetCurrentLinePlainText().Trim().EndsWith("Icon") && _classAnnotation)
            _inClassAnnotationIcon = true;

        Write("(");
        if (opening.Any)
            WriteOpeningComments(opening, useMultiLineParens);
        if (useMultiLineParens)
        {
            if (!opening.Any)
                EmitLine();
            Indent();
        }
        if (context.argument_list() != null)
            Visit(context.argument_list());
        if (closing.Any)
            WriteListComments(closing, useMultiLineParens, beforeClose: true);
        if (useMultiLineParens)
        {
            EndLineBeforeClose(closing.Any);
            Dedent();
            Write(")");
        }
        else
            Write(")");

        // Restore previous state
        _parentUsingMultiLine = previousParentState;
        _inClassAnnotationIcon = wasInClassAnnotationIcon;
        return null;
    }

    public override object? VisitArgument_list([NotNull] modelicaParser.Argument_listContext context)
    {
        var arguments = context.argument();
        var runs = CommentRuns(context);

        if (arguments != null)
        {
            for (int i = 0; i < arguments.Length; i++)
            {
                if (i > 0 && runs != null && runs[i].Any)
                {
                    // Comments after the ',' (B431) are written each where it stood, and the
                    // argument starts the line they leave.
                    Write(",");
                    WriteListComments(runs[i], _parentUsingMultiLine);
                    Visit(arguments[i]);
                }
                else if (i > 0)
                {
                    Write(",");

                    // Check if line is too long or will be too long with next argument
                    var nextArgText = arguments[i].GetText();
                    var estimatedLength = GetCurrentLinePlainTextLength() + 1 + nextArgText.Length;
                    bool needsWrapForLength = !_inDocumentationAnnotation && estimatedLength > (_maxLineLength - 3);

                    // Decide whether to write on one line or multiple lines
                    // Wrap if: parent is using multi-line mode, more than 2 args, line starts with ), OR line is too long
                    bool shouldWrap = _parentUsingMultiLine || arguments.Length > 2 || GetCurrentLinePlainText().StartsWith(")") || needsWrapForLength;

                    if (shouldWrap)
                    {
                        // If wrapping due to line length (not already in multi-line mode), add continuation indent.
                        // EmitLine must be called BEFORE Indent so the current line is flushed at the
                        // existing indent level — Indent only affects the continuation line.
                        bool needsExtraIndent = needsWrapForLength && !_parentUsingMultiLine;

                        EmitLine();
                        if (needsExtraIndent)
                            Indent();
                        // Add indent to current line unless parent is multi-line (parent already set up indent via Indent())
                        if (!_parentUsingMultiLine)
                            AddIndentToCurrentLine();

                        Visit(arguments[i]);

                        if (needsExtraIndent)
                            Dedent();
                    }
                    else
                    {
                        Space();
                        Visit(arguments[i]);
                    }
                }
                else
                {
                    VisitFirstArgument(arguments[i], arguments.Length);
                }
            }
        }
        return null;
    }

    /// <summary>
    /// Writes the first argument of a list, and moves it to a continuation line of its own, a level
    /// in, when the list has to wrap and the argument took the opening line past the maximum length
    /// (B464) - as each later argument that does not fit is. Only an argument written on one line
    /// is moved: one the renderer has already broken over lines (a nested modification, a data
    /// table) has its own layout, and nothing but the '(' may have been written since the list
    /// opened, so a comment after the '(' keeps the line it leaves. A list inside one written an
    /// argument a line, and a graphics annotation's lists, are laid out by their own rules rather
    /// than wrapped for length, and are left as they were.
    /// </summary>
    private void VisitFirstArgument(IParseTree first, int argumentCount)
    {
        if (argumentCount < 2 || _parentUsingMultiLine || _inGraphicsAnnotationLevel > 0
            || !GetCurrentLinePlainText().EndsWith('('))
        {
            Visit(first);
            return;
        }

        int start = _currentLine.Length;
        int lines = _code.Count;
        Visit(first);
        // The ',' that follows is on this line too.
        if (_code.Count != lines || GetCurrentLinePlainTextLength() + 1 <= _maxLineLength)
            return;

        var argument = _currentLine.ToString(start, _currentLine.Length - start);
        _currentLine.Length = start;
        EmitLine();
        AddIndentToCurrentLine();
        _currentLine.Append(argument);
    }

    public override object? VisitArgument([NotNull] modelicaParser.ArgumentContext context)
    {
        // Element modification argument
        if (context.element_modification_or_replaceable() != null)
        {
            Visit(context.element_modification_or_replaceable());
        }
        // Element redeclaration argument
        else if (context.element_redeclaration() != null)
        {
            Visit(context.element_redeclaration());
        }

        return null;
    }

    public override object? VisitElement_modification_or_replaceable([NotNull] modelicaParser.Element_modification_or_replaceableContext context)
    {
        // Handle each/final keywords
        if (context.GetChild(0)?.GetText() == "each")
        {
            Write(Keyword("each"));
            Space();
        }
        if (context.GetChild(0)?.GetText() == "final" || context.GetChild(1)?.GetText() == "final")
        {
            Write(Keyword("final"));
            Space();
        }

        // Element modification
        if (context.element_modification() != null)
            Visit(context.element_modification());
        else
            Visit(context.element_replaceable());

        return null;
    }

    public override object? VisitElement_modification([NotNull] modelicaParser.Element_modificationContext context)
    {
        // Check if this is a Documentation annotation
        bool isDocumentation = context.name()?.GetText() == "Documentation";
        bool previousDocState = _inDocumentationAnnotation;
        if (isDocumentation)
            _inDocumentationAnnotation = true;

        // Name (possibly qualified)
        if (context.name() != null)
            Visit(context.name());

        // Modification
        if (context.modification() != null)
        {
            Visit(context.modification());
        }

        // String comment
        if (context.string_comment() != null && context.string_comment().GetText() != "")
        {
            Space();
            Visit(context.string_comment());
        }

        // Restore previous Documentation state
        if (isDocumentation)
            _inDocumentationAnnotation = previousDocState;

        return null;
    }

    public override object? VisitElement_redeclaration([NotNull] modelicaParser.Element_redeclarationContext context)
    {
        Write(Keyword("redeclare"));
        Space();

        if (context.GetChild(1)?.GetText() == "each")
        {
            Write(Keyword("each"));
            Space();
        }
        if (context.GetChild(1)?.GetText() == "final" || context.GetChild(2)?.GetText() == "final")
        {
            Write(Keyword("final"));
            Space();
        }

        // Short or long class definition or component clause
        if (context.short_class_definition() != null)
            Visit(context.short_class_definition());
        else if (context.component_clause1() != null)
            Visit(context.component_clause1());
        else if (context.element_replaceable() != null)
            Visit(context.element_replaceable());

        return null;
    }

    public override object? VisitElement_replaceable([NotNull] modelicaParser.Element_replaceableContext context)
    {
        Write(Keyword("replaceable"));
        Space();

        // Short or component clause
        if (context.short_class_definition() != null)
            Visit(context.short_class_definition());
        else if (context.component_clause1() != null)
            Visit(context.component_clause1());

        if (context.constraining_clause() !=null) {
            // Comments before 'constrainedby' (B431), as VisitElement writes them (B432).
            if (context.c_comment() is { Length: > 0 } comments)
            {
                WriteLeadingComments(comments);
                _constrainedbyOnFreshLine = true;
            }
            Visit(context.constraining_clause());
        }
        return null;
    }

    public override object? VisitCondition_attribute([NotNull] modelicaParser.Condition_attributeContext context)
    {
        Write(Keyword("if"));
        Space();
        if (context.expression() != null)
            Visit(context.expression());
        return null;
    }

    public override object? VisitArray_subscripts([NotNull] modelicaParser.Array_subscriptsContext context)
    {
        Write("[");
        var subscripts = context.subscript_();
        if (subscripts != null)
        {
            for (int i = 0; i < subscripts.Length; i++)
            {
                if (i > 0)
                {
                    Write(",");
                    Space();
                }
                Visit(subscripts[i]);
            }
        }
        Write("]");
        return null;
    }

    public override object? VisitSubscript_([NotNull] modelicaParser.Subscript_Context context)
    {
        if (context.GetText() == ":")
        {
            Write(":");
        }
        else if (context.expression() != null)
        {
            Visit(context.expression());
        }
        return null;
    }

    #endregion

    #region Equation and Algorithm Visitors

    public override object? VisitEquation_section([NotNull] modelicaParser.Equation_sectionContext context)
    {
        if (_currentSection.Peek() == CodeSection.InitialEquation && context.GetChild(0)?.GetText() == "initial")
        {
            if (!_writtenSectionHeader) {
                EmitEmptyLine();
                Write(Keyword("initial"));
                Space();
                Write(Keyword("equation"));
                EmitLine();
                _writtenSectionHeader = true;
            }
            Indent();

            // Process all children (equations and comments) in order
            foreach (var eq in context.equation_or_comment())
            {
                Visit(eq);
            }

            Dedent();                     
        } 
        else if (_currentSection.Peek() == CodeSection.Equation && context.GetChild(0)?.GetText() != "initial")
        {
            if (!_writtenSectionHeader) {
                EmitEmptyLine();
                Write(Keyword("equation"));
                EmitLine();
                _writtenSectionHeader = true;
            }

            Indent();

            // Process all children (equations and comments) in order
            foreach (var eq in context.equation_or_comment())
            {
                Visit(eq);
            }

            Dedent();                 
        }
        else    //CodeSection.Any
        {
            EmitEmptyLine();
            if (context.GetChild(0)?.GetText() == "initial")
            {
                Write(Keyword("initial"));
                Space();
            }
            Write(Keyword("equation"));
            EmitLine();

            Indent();

            // Process all children (equations and comments) in order
            foreach (var eq in context.equation_or_comment())
            {
                Visit(eq);
            }

            Dedent();            
        }
        return null;
    }

    public override object? VisitAlgorithm_section([NotNull] modelicaParser.Algorithm_sectionContext context)
    {
        if (_currentSection.Peek() == CodeSection.InitialAlgorithm && context.GetChild(0)?.GetText() == "initial")
        {
            if (!_writtenSectionHeader) {
                EmitEmptyLine();
                Write(Keyword("initial"));
                Space();
                Write(Keyword("algorithm"));
                EmitLine();
                _writtenSectionHeader = true;
            }
            Indent();

            // Process all children (equations and comments) in order
            foreach (var eq in context.statement_or_comment())
            {
                Visit(eq);
            }

            Dedent();                     
        } 
        else if (_currentSection.Peek() == CodeSection.Algorithm && context.GetChild(0)?.GetText() != "initial")
        {
            if (!_writtenSectionHeader) {
                EmitEmptyLine();
                Write(Keyword("algorithm"));
                EmitLine();
                _writtenSectionHeader = true;
            }

            Indent();

            // Process all children (equations and comments) in order
            foreach (var eq in context.statement_or_comment())
            {
                Visit(eq);
            }

            Dedent();                 
        }
        else    //CodeSection.Any
        {
            EmitEmptyLine();
            if (context.GetChild(0)?.GetText() == "initial")
            {
                Write(Keyword("initial"));
                Space();
            }
            Write(Keyword("algorithm"));
            EmitLine();

            Indent();

            // Process all children (equations and comments) in order
            foreach (var eq in context.statement_or_comment())
            {
                Visit(eq);
            }

            Dedent();            
        }
        return null;
    }

    public override object? VisitEquation([NotNull] modelicaParser.EquationContext context)
    {
        // Set up continuation indent for equation wrapping
        _equationContinuationIndent = 1;

        // Simple equation, if-equation, for-equation, connect-clause, when-equation
        if (context.simple_expression() != null)
        {
            Visit(context.simple_expression());
            if (context.GetText().Contains('=') && context.expression() != null)
            {
                // Check if we should wrap before the = sign
                // Wrap if left side is > 20 chars and adding the RHS would make line too long
                var lhsLength = GetCurrentLinePlainTextLength();
                var rhsText = context.expression().GetText();
                var estimatedTotalLength = lhsLength + 3 + rhsText.Length; // +3 for " = "
                bool wrapBeforeEquals = lhsLength > 20 && estimatedTotalLength > _maxLineLength;

                if (wrapBeforeEquals)
                {
                    Space(); // Add trailing space on LHS line
                    EmitLine();
                    Indent(); // Single indent for continuation
                    AddIndentToCurrentLine();
                    Write(Operator("=", false)); // No leading space, just "="
                    Space(); // Add space after =
                }
                else
                {
                    Write(Operator("=")); // Normal " = " with spaces
                }

                Visit(context.expression());

                if (wrapBeforeEquals)
                {
                    Dedent();
                }
            }
        }
        else if (context.if_equation() != null)
        {
            Visit(context.if_equation());
        }
        else if (context.for_equation() != null)
        {
            Visit(context.for_equation());
        }
        else if (context.connect_clause() != null)
        {
            Visit(context.connect_clause());
        }
        else if (context.when_equation() != null)
        {
            Visit(context.when_equation());
        }
        else if (context.component_reference() != null && context.function_call_args() != null)
        {
            // Function call equation: component_reference function_call_args
            // Restored rather than cleared (B232): a call nested inside another reference's
            // subscripts used to switch the colouring off for the rest of the outer reference.
            var wasFunction = _isFunction;
            _isFunction = true;
            Visit(context.component_reference());
            _isFunction = wasFunction;
            Visit(context.function_call_args());
        }

        // Only visit comment if it has actual content
        if (context.comment() != null && !string.IsNullOrWhiteSpace(context.comment().GetText()))
            Visit(context.comment());

        // Reset continuation indent
        _equationContinuationIndent = 0;

        return null;
    }

    public override object? VisitStatement([NotNull] modelicaParser.StatementContext context)
    {
        // Set up continuation indent for statement wrapping
        _equationContinuationIndent = 1;

        var functionCallArgs = context.function_call_args();
        // Assignment, function call, if-statement, for-statement, while-statement, when-statement, break, return
        if (context.output_expression_list() != null) {
            Write("(");
            Visit(context.output_expression_list());
            Write(")");
            Write(Operator(":="));
            var wasFunctionInOutputList = _isFunction;
            _isFunction = true;
            Visit(context.component_reference());
            _isFunction = wasFunctionInOutputList;
            Visit(functionCallArgs[0]);
        }
        else if (context.component_reference() != null)
        {
            // `statement : component_reference (':=' expression | function_call_args)` — so the
            // reference is the thing being called in the second form and the thing being assigned to
            // in the first. Marking both made the variable on the left of every assignment a
            // function call: `y_dd := ...` in MultiBody's maxWithoutEvent_dd came out red (B254).
            var isCall = context.expression() is null && functionCallArgs is { Length: > 0 };

            var wasFunctionInStatement = _isFunction;
            _isFunction = isCall;
            Visit(context.component_reference());
            _isFunction = wasFunctionInStatement;

            if (context.expression() != null)
            {
                // Don't wrap before := - let expression wrapping handle line breaks
                Write(Operator(":=")); // Normal " := " with spaces
                Visit(context.expression());
            }
            else if (functionCallArgs != null)
            {
                Visit(functionCallArgs[0]);
            }
        }
        else if (context.GetText().StartsWith("der"))
        {
            // Handle der function call assignment
            Write(FunctionCall("der"));
            Visit(functionCallArgs[0]);

            if (context.GetText().Contains(":=") && context.expression() != null)
            {
                Write(Operator(":="));
                Visit(context.expression());
            }
            else if (functionCallArgs.Length > 1)
            {
                Visit(functionCallArgs[1]);
            }
        }
        else if (context.if_statement() != null)
        {
            Visit(context.if_statement());
        }
        else if (context.for_statement() != null)
        {
            Visit(context.for_statement());
        }
        else if (context.while_statement() != null)
        {
            Visit(context.while_statement());
        }
        else if (context.when_statement() != null)
        {
            Visit(context.when_statement());
        }
        else if (context.GetText().StartsWith("break"))
        {
            Write(Keyword("break"));
        }
        else if (context.GetText() == "return")
        {
            Write(Keyword("return"));
        }

        // Only visit comment if it has actual content
        if (context.comment() != null && !string.IsNullOrWhiteSpace(context.comment().GetText()))
            Visit(context.comment());

        // Reset continuation indent
        _equationContinuationIndent = 0;

        return null;
    }

    public override object? VisitIf_equation([NotNull] modelicaParser.If_equationContext context)
    {
        // if
        Write(Keyword("if"));
        Space();
        Visit(context.expression());
        Space();
        Write(Keyword("then"));
        EmitLine();

        Indent();
        foreach (var eq in context.equation_or_comment())
        {
            Visit(eq);
        }
        Dedent();

        // elseif clauses
        if (context.elseif_equation() != null)
        {
            var elseIfEqs = context.elseif_equation();
            foreach (var elseif in elseIfEqs)
            {
                Write(Keyword("elseif"));
                Space();
                Visit(elseif.expression());
                Space();
                Write(Keyword("then"));
                EmitLine();

                Indent();
                var equations = elseif.equation_or_comment();
                foreach (var eq in equations)
                {
                    Visit(eq);
                }
                Dedent();
            }
        }   

        // else clause
        if (context.else_equation() != null)
        {
            Write(Keyword("else"));
            EmitLine();
            Indent();
            foreach (var eq in context.else_equation().equation_or_comment())
            {
                Visit(eq);
            }
            Dedent();
        }   

        Write(Keyword("end"));
        Space();
        Write(Keyword("if"));

        return null;
    }

    public override object? VisitFor_equation([NotNull] modelicaParser.For_equationContext context)
    {
        Write(Keyword("for"));
        Space();

        if (context.for_indices() != null)
            Visit(context.for_indices());

        Space();
        Write(Keyword("loop"));
        EmitLine();

        Indent();
        var equations = context.equation_or_comment();
        if (equations != null)
        {
            foreach (var eq in equations)
            {
                Visit(eq);
            }
        }
        Dedent();

        Write(Keyword("end"));
        Space();
        Write(Keyword("for"));

        return null;
    }

    public override object? VisitFor_statement([NotNull] modelicaParser.For_statementContext context)
    {
        Write(Keyword("for"));
        Space();

        if (context.for_indices() != null)
            Visit(context.for_indices());

        Space();
        Write(Keyword("loop"));
        EmitLine();

        Indent();
        var statements = context.statement_or_comment();
        if (statements != null)
        {
            foreach (var stmt in statements)
            {
                Visit(stmt);
            }
        }
        Dedent();

        Write(Keyword("end"));
        Space();
        Write(Keyword("for"));

        return null;
    }

    public override object? VisitFor_indices([NotNull] modelicaParser.For_indicesContext context)
    {
        var indices = context.for_index();
        if (indices != null)
        {
            for (int i = 0; i < indices.Length; i++)
            {
                if (i > 0)
                {
                    Write(",");
                    Space();
                }
                Visit(indices[i]);
            }
        }
        return null;
    }

    public override object? VisitFor_index([NotNull] modelicaParser.For_indexContext context)
    {
        var ident = context.IDENT();
        if (ident != null)
            Write(Ident(ident.GetText()));

        if (context.expression() != null)
        {
            Space();
            Write(Keyword("in"));
            Space();
            Visit(context.expression());
        }

        return null;
    }

    public override object? VisitWhile_statement([NotNull] modelicaParser.While_statementContext context)
    {
        Write(Keyword("while"));
        Space();

        if (context.expression() != null)
            Visit(context.expression());

        Space();
        Write(Keyword("loop"));
        EmitLine();

        Indent();
        var statements = context.statement_or_comment();
        if (statements != null)
        {
            foreach (var stmt in statements)
            {
                Visit(stmt);
            }
        }
        Dedent();

        Write(Keyword("end"));
        Space();
        Write(Keyword("while"));

        return null;
    }

    public override object? VisitWhen_equation([NotNull] modelicaParser.When_equationContext context)
    {
        Write(Keyword("when"));
        Space();
        Visit(context.expression());
        Space();
        Write(Keyword("then"));
        EmitLine();

        Indent();
        foreach (var eq in context.equation_or_comment())
        {
            Visit(eq);
        }
        Dedent();

        if (context.elsewhen_equation() != null)
        {
            var elsewhenEqs = context.elsewhen_equation();
            foreach (var elsewhen in elsewhenEqs)
            {
                Write(Keyword("elsewhen"));
                Space();
                Visit(elsewhen.expression());
                Space();
                Write(Keyword("then"));
                EmitLine();

                Indent();
                var ewEquations = elsewhen.equation_or_comment();
                foreach (var ewEq in ewEquations)
                {
                    Visit(ewEq);
                }
                Dedent();
            }
        }

        Write(Keyword("end"));
        Space();
        Write(Keyword("when"));

        return null;
    }

    public override object? VisitWhen_statement([NotNull] modelicaParser.When_statementContext context)
    {
        Write(Keyword("when"));
        Space();
        Visit(context.expression());
        Space();
        Write(Keyword("then"));
        EmitLine();

        Indent();
        foreach (var stmt in context.statement_or_comment())
        {
            Visit(stmt);      
        }
        Dedent();

        if (context.elsewhen_statement() != null)
        {
            var elsewhenStmts = context.elsewhen_statement();
            foreach (var elsewhen in elsewhenStmts)
            {
                Write(Keyword("elsewhen"));
                Space();
                Visit(elsewhen.expression());
                Space();
                Write(Keyword("then"));
                EmitLine();

                Indent();
                var ewStatements = elsewhen.statement_or_comment();
                foreach (var ewStmt in ewStatements)
                {
                    Visit(ewStmt);
                }
                Dedent();
            }
        }

        Write(Keyword("end"));
        Space();
        Write(Keyword("when"));

        return null;
    }

    public override object? VisitIf_statement([NotNull] modelicaParser.If_statementContext context)
    {
        // if
        Write(Keyword("if"));
        Space();
        Visit(context.expression());
        Space();
        Write(Keyword("then"));
        EmitLine();

        Indent();
        foreach (var statement in context.statement_or_comment())
        {
            Visit(statement);
        }
        Dedent();

        if (context.elseif_statement() != null)
        {
            foreach (var elseif in context.elseif_statement())
            {
                Write(Keyword("elseif"));
                Space();
                Visit(elseif.expression());
                Space();
                Write(Keyword("then"));
                EmitLine();

                Indent();
                foreach (var stmt in elseif.statement_or_comment())
                {
                    Visit(stmt);
                }
                Dedent();
            }
        }   

        // else clause
        if (context.else_statement() != null)
        {
            Write(Keyword("else"));
            EmitLine();
            Indent();
            foreach (var stmt in context.else_statement().statement_or_comment())
            {
                Visit(stmt);      
            }
            Dedent();
        }

        Write(Keyword("end"));
        Space();
        Write(Keyword("if"));

        return null;
    }

    public override object? VisitConnect_clause([NotNull] modelicaParser.Connect_clauseContext context)
    {
        Write(Keyword("connect"));
        Write("(");

        var componentRefs = context.component_reference();
        if (componentRefs != null && componentRefs.Length >= 2)
        {
            Visit(componentRefs[0]);
            Write(",");
            Space();
            Visit(componentRefs[1]);
        }

        Write(")");
        return null;
    }

    public override object? VisitEquation_or_comment([NotNull] modelicaParser.Equation_or_commentContext context)
    {
        // Comment-only when there is no equation: since B432 an equation may carry comments too.
        if (context.equation() is { } equation)
        {
            Visit(equation);
            WriteSemicolonAndComments(context.c_comment());
        }
        else
        {
            foreach (var comment in context.c_comment())
                if (!string.IsNullOrWhiteSpace(comment.GetText()))
                    Visit(comment);
        }
        return null;
    }

    public override object? VisitStatement_or_comment([NotNull] modelicaParser.Statement_or_commentContext context)
    {
        if (context.statement() is { } statement)
        {
            Visit(statement);
            WriteSemicolonAndComments(context.c_comment());
        }
        else
        {
            foreach (var comment in context.c_comment())
                if (!string.IsNullOrWhiteSpace(comment.GetText()))
                    Visit(comment);
        }
        return null;
    }

    /// <summary>
    /// Ends an equation or a statement. Comments between it and its ';' (B432) are written after
    /// the ';' rather than before it - a line comment ends its line, so kept in place it would leave
    /// the ';' alone on the next one - and each on a line of its own, which is how a comment after
    /// the ';' is written. Written any other way, the next save would move them again.
    /// </summary>
    private void WriteSemicolonAndComments(modelicaParser.C_commentContext[] comments)
    {
        Write(";");
        EmitLine();
        foreach (var comment in comments)
            Visit(comment);
    }

    #endregion

    #region Expression Visitors

    public override object? VisitExpression([NotNull] modelicaParser.ExpressionContext context)
    {
        if (context.simple_expression() != null)
        {
            Visit(context.simple_expression());
        }
        else
        {
            // if expression then expression elseif ... else expression
            var expressions = context.expression();
            Write(Keyword("if"));
            Space();
            if (expressions != null && expressions.Length > 0)
                Visit(expressions[0]);
            Space();
            Write(Keyword("then"));
            Space();
            if (expressions != null && expressions.Length > 1)
                Visit(expressions[1]);
            Space();

            // Handle elseif and else clauses
            if (context.elseif_expression() != null)
            {
                foreach (var elseif in context.elseif_expression())
                {
                    Write(Keyword("elseif"));
                    Space();
                    var elseifExpressions = elseif.expression();
                    if (elseifExpressions != null && elseifExpressions.Length > 0)
                        Visit(elseifExpressions[0]);
                    Space();
                    Write(Keyword("then"));
                    Space();
                    if (elseifExpressions != null && elseifExpressions.Length > 1)
                        Visit(elseifExpressions[1]);
                    Space();
                }
            }

            Write(Keyword("else"));
            Space();
            if (expressions != null && expressions.Length == 3)
                Visit(expressions[2]);

        }

        return null;
    }

    public override object? VisitSimple_expression([NotNull] modelicaParser.Simple_expressionContext context)
    {
        var logicalExpressions = context.logical_expression();
        if (logicalExpressions != null && logicalExpressions.Length > 0)
        {
            Visit(logicalExpressions[0]);

            // Handle range operator (..)
            if (logicalExpressions.Length > 1)
            {
                Write(Operator(":", false));
                Visit(logicalExpressions[1]);
            }
            if (logicalExpressions.Length > 2)
            {
                Write(Operator(":", false));
                Visit(logicalExpressions[2]);
            }
        }

        return null;
    }

    public override object? VisitLogical_expression([NotNull] modelicaParser.Logical_expressionContext context)
    {
        var logicalTerms = context.logical_term();
        if (logicalTerms != null)
        {
            for (int i = 0; i < logicalTerms.Length; i++)
            {
                if (i > 0)
                {
                    Space();
                    Write(Keyword("or"));
                    Space();
                }
                Visit(logicalTerms[i]);
            }
        }
        return null;
    }

    public override object? VisitLogical_term([NotNull] modelicaParser.Logical_termContext context)
    {
        var logicalFactors = context.logical_factor();
        if (logicalFactors != null)
        {
            for (int i = 0; i < logicalFactors.Length; i++)
            {
                if (i > 0)
                {
                    Space();
                    Write(Keyword("and"));
                    Space();
                }
                Visit(logicalFactors[i]);
            }
        }
        return null;
    }

    public override object? VisitLogical_factor([NotNull] modelicaParser.Logical_factorContext context)
    {
        if (context.GetChild(0)?.GetText() == "not")
        {
            Write(Keyword("not"));
            Space();
        }
        if (context.relation() != null)
            Visit(context.relation());
        return null;
    }

    public override object? VisitRelation([NotNull] modelicaParser.RelationContext context)
    {
        var arithmeticExpressions = context.arithmetic_expression();
        if (arithmeticExpressions != null && arithmeticExpressions.Length > 0)
        {
            Visit(arithmeticExpressions[0]);

            if (arithmeticExpressions.Length > 1)
            {
                // Get the operator between the expressions
                var relOp = context.rel_op();
                if (relOp != null)
                    Visit(relOp);
                Visit(arithmeticExpressions[1]);
            }
        }
        return null;
    }

    public override object? VisitRel_op([NotNull] modelicaParser.Rel_opContext context)
    {
        var op = context.GetText();
        Write(Operator(op));
        return null;
    }

    public override object? VisitArithmetic_expression([NotNull] modelicaParser.Arithmetic_expressionContext context)
    {
        var terms = context.term();
        var addOps = context.add_op();
        int addOpsOffset = -1;

        if (terms != null && terms.Length > 0)
        {
            if (addOps.Length >= terms.Length)
            {
                Write(Sign(context.GetChild(0).GetText()));
                addOpsOffset = 0;
            }

            Visit(terms[0]);

            for (int i = 1; i < terms.Length; i++)
            {
                // Check if we should wrap before this add_op
                // Estimate total length: current line + operator (3 chars for " + ") + next term
                var currentLength = GetCurrentLinePlainTextLength();
                var operatorText = addOps[i + addOpsOffset].GetText();
                var termText = terms[i].GetText();
                var estimatedLength = currentLength + 1 + operatorText.Length + 1 + termText.Length; // +1 for spaces

                // Only wrap if: estimated length exceeds max (with small margin), we're not inside brackets, and we're in an equation/statement
                // Use a small margin (e.g., 3 chars) to account for semicolons and other trailing punctuation
                bool shouldWrap = estimatedLength > (_maxLineLength - 3) && _bracketDepth == 0 && _equationContinuationIndent > 0;

                if (shouldWrap)
                {
                    EmitLine();
                    // Add the continuation indent
                    for (int j = 0; j < _equationContinuationIndent; j++)
                        Indent();
                    AddIndentToCurrentLine();
                    // Write operator without leading space (we're at start of line)
                    Write(Operator(addOps[i + addOpsOffset].GetText(), false));
                    Space(); // Add space after operator

                    // Remove the continuation indent
                    for (int j = 0; j < _equationContinuationIndent; j++)
                        Dedent();
                }
                else
                {
                    // Normal case: visit operator with spaces
                    Visit(addOps[i + addOpsOffset]);
                }

                Visit(terms[i]);
            }
        }

        return null;
    }

    public override object? VisitAdd_op([NotNull] modelicaParser.Add_opContext context)
    {
        var op = context.GetText();
        Write(Operator(op));
        return null;
    }

    public override object? VisitTerm([NotNull] modelicaParser.TermContext context)
    {
        var factors = context.factor();
        var mulOps = context.mul_op();

        if (factors != null && factors.Length > 0)
        {
            Visit(factors[0]);

            for (int i = 1; i < factors.Length; i++)
            {
                if (mulOps != null && i - 1 < mulOps.Length)
                    Visit(mulOps[i - 1]);
                Visit(factors[i]);
            }
        }

        return null;
    }

    public override object? VisitMul_op([NotNull] modelicaParser.Mul_opContext context)
    {
        var op = context.GetText();
        Write(Operator(op));
        return null;
    }

    public override object? VisitFactor([NotNull] modelicaParser.FactorContext context)
    {
        var primaries = context.primary();
        if (primaries != null && primaries.Length > 0)
        {
            Visit(primaries[0]);

            // Handle exponentiation (^ or .^)
            if (primaries.Length > 1)
            {
                if (context.GetText().Contains(".^"))
                    Write(Operator(".^", false));
                else
                    Write(Operator("^"));
                Visit(primaries[1]);
            }
        }

        return null;
    }

    public override object? VisitPrimary([NotNull] modelicaParser.PrimaryContext context)
    {
        // Handle different primary expression types
        if (context.UNSIGNED_NUMBER() != null)
        {
            Write(Literal(context.UNSIGNED_NUMBER().GetText()));
        }
        else if (context.STRING() != null)
        {
            WriteMultiLineString(context.STRING().GetText());
        }
        else if (context.GetText() == "false" || context.GetText() == "true")
        {
            Write(Keyword(context.GetText()));
        }
        else if (context.function_call_args() !=null)
        {
            //(component_reference | 'der' | 'initial' | 'pure') function_call_args
            if (context.component_reference()!=null) 
            {
                var wasFunctionInCall = _isFunction;
                _isFunction = true;
                Visit(context.component_reference());
                _isFunction = wasFunctionInCall;
            }
            else
            {
                var keyword = context.children[0].GetText();
                if (keyword == "initial")
                    Write(FunctionCall("initial"));
                else if (keyword == "der")
                    Write(FunctionCall("der"));
                else if (keyword == "pure")
                    Write(FunctionCall("pure"));
            }
            Visit(context.function_call_args());
        }
        else if (context.component_reference() != null)
        {
            Visit(context.component_reference());
        }
        else if (context.GetText().StartsWith('('))
        {
            // Parenthesized expression or output expression list
            Write("(");
            _bracketDepth++;
            if (context.output_expression_list() != null)
                Visit(context.output_expression_list());
            _bracketDepth--;
            Write(")");
            if (context.array_arguments() != null) {
                Write("[");
                Visit(context.array_arguments());
                Write("]");
            }
        }
        else if (context.GetText().StartsWith('['))
        {
            // Array expression [expression_list (';' expression_list)*]
            // Comments after the '[', after a row's ';' and before the ']' (B431): run i
            // is what comes before row i, and the last run what comes after the last row. A data
            // table keeps the rows its author wrote (B462): a row that starts a line in the source
            // starts one here, a level in - the first row too, when it is below the '[' (B463) -
            // and rows the author ran together stay together - a
            // comment ends its line the same way. Read from the tokens, so a table written one
            // row a line keeps its rows on every save, and one written on one line stays there.
            var runs = CommentRuns(context);
            int firstLine = _code.Count;
            int level = _indentLevel;
            // A line the table starts is a level in from the line it began on, which may itself
            // be a continuation line carrying its indent as leading spaces.
            int baseSpaces = _currentLine.Length - _currentLine.ToString().TrimStart(' ').Length;
            Write("[");
            _bracketDepth++;
            var expressionLists = context.expression_list();
            if (expressionLists != null)
            {
                for (int i = 0; i < expressionLists.Length; i++)
                {
                    if (i > 0)
                    {
                        Write(";");
                        if (runs == null || !runs[i].Any)
                        {
                            if (RowStartsLine(expressionLists[i - 1].Stop, expressionLists[i]))
                                StartMatrixRowLine(baseSpaces);
                            else
                                Space();
                        }
                    }
                    else if ((runs == null || !runs[0].Any) && RowStartsLine(context.Start, expressionLists[0]))
                    {
                        // A first row written on the line after the '[' stays there (B463).
                        StartMatrixRowLine(baseSpaces);
                    }
                    if (runs != null && runs[i].Any)
                    {
                        if (i == 0)
                            WriteOpeningComments(runs[i], multiLine: false);
                        else
                            WriteListComments(runs[i], multiLine: false);
                        IndentMatrixLine(baseSpaces);
                    }
                    Visit(expressionLists[i]);
                }
            }
            if (runs != null && runs[^1].Any)
            {
                WriteListComments(runs[^1], multiLine: false, beforeClose: true);
                IndentMatrixLine(baseSpaces);
            }
            _bracketDepth--;
            Write("]");
            if (_code.Count > firstLine)
                _pendingMatrixLines = (firstLine, _code.Count, level);
        }
        else if (context.GetText().StartsWith('{'))
        {
            // Array constructor
            bool resetGraphicsFlag = false;
            bool singleLineGraphics = false;
            if (_classAnnotation && GetCurrentLinePlainText().EndsWith("graphics="))
            {
                _inGraphicsAnnotationLevel = 1;
                _classAnnotation = false;
                resetGraphicsFlag = true;

                // Check if this should be formatted as a single line
                singleLineGraphics = ModelicaRendererHelper.IsSingleLineGraphicsArray(context.array_arguments());
            }
            // Comments after the '{' and before the '}' (B431), as in VisitClass_modification.
            var runs = CommentRuns(context);
            var opening = runs?[0] ?? default;
            var closing = runs is { Length: > 1 } ? runs[^1] : default;
            bool multiLine = resetGraphicsFlag && !singleLineGraphics;

            Write("{");
            if (opening.Any)
                WriteOpeningComments(opening, multiLine);
            if (multiLine)
            {
                if (!opening.Any)
                    EmitLine();
                Indent();
            }
            if (context.array_arguments() != null)
                Visit(context.array_arguments());
            if (closing.Any)
                WriteListComments(closing, multiLine, beforeClose: true);
            if (resetGraphicsFlag) {
                _inGraphicsAnnotationLevel = 0;
                _classAnnotation = true;
                if (!singleLineGraphics)
                {
                    EndLineBeforeClose(closing.Any);
                    Dedent();
                }
            }
            Write("}");
        }
        else if (context.GetText() == "end")
        {
            Write(Keyword("end"));
        }

        return null;
    }

    public override object? VisitComponent_reference([NotNull] modelicaParser.Component_referenceContext context)
    {
        var children = context.children;
        if (children != null)
        {
            for (int i = 0; i < children.Count; i++)
            {
                var child = children[i];
                if (child is ITerminalNode terminal)
                {
                    var text = terminal.GetText();
                    if (text == ".")
                    {
                        Write(".");
                    }
                    else if (terminal.Symbol.Type == modelicaParser.IDENT)
                    {
                        if (_isFunction && (!_inAnnotation || (_inAnnotation && _inGraphicsAnnotationLevel >2)))
                            Write(FunctionCall(text));
                        else
                            Write(Ident(text));
                    }
                }
                else if (child is modelicaParser.Array_subscriptsContext arrSubs)
                {
                    // A subscript is not part of the call being made (B232). `den2[i] := …` visits
                    // the subscript while _isFunction is still set for the reference, so `i` was
                    // coloured as a function call — 377 tokens in MSL, 512 in Buildings, and the
                    // whole residue of the classifier's 99.998% agreement measurement.
                    var wasFunction = _isFunction;
                    _isFunction = false;
                    Visit(arrSubs);
                    _isFunction = wasFunction;
                }
            }
        }
        return null;
    }

    public override object? VisitFunction_call_args([NotNull] modelicaParser.Function_call_argsContext context)
    {
        // Count arguments to determine formatting
        int argCount = ModelicaRendererHelper.CountFunctionArguments(context.function_arguments());

        // Special case: 2-argument graphics elements at level 2 stay on one line
        bool useSingleLine = _inGraphicsAnnotationLevel == 2 && argCount == 2;

        bool useMultiLine = (_inGraphicsAnnotationLevel >= 1 && _inGraphicsAnnotationLevel <= 2 && !useSingleLine) ||
                            (_parentUsingMultiLine && !useSingleLine && argCount >= 2 && !_inAnnotation);

        // Set flag for child visitors
        bool previousSingleLineState = _inSingleLineGraphicsElement;
        if (useSingleLine)
            _inSingleLineGraphicsElement = true;
        bool previousCallState = _callUsingMultiLine;
        _callUsingMultiLine = useMultiLine;

        // Comments after the '(' and before the ')' (B431), as in VisitClass_modification.
        var runs = CommentRuns(context);
        var opening = runs?[0] ?? default;
        var closing = runs is { Length: > 1 } ? runs[^1] : default;

        Write("(");
        if (opening.Any)
            WriteOpeningComments(opening, useMultiLine);
        if (useMultiLine)
        {
            if (!opening.Any)
                EmitLine();
            Indent();
        }
        if (context.function_arguments() != null)
            Visit(context.function_arguments());
        if (closing.Any)
            WriteListComments(closing, useMultiLine, beforeClose: true);
        if (useMultiLine)
        {
            EndLineBeforeClose(closing.Any);
            Dedent();
        }
        Write(")");

        // Restore previous state
        _inSingleLineGraphicsElement = previousSingleLineState;
        _callUsingMultiLine = previousCallState;

        return null;
    }

    public override object? VisitFunction_arguments([NotNull] modelicaParser.Function_argumentsContext context)
    {
        // Grammar: expression (',' function_argument)* (',' named_arguments)? ('for' for_indices)?
        //        | function_partial_application (',' function_argument)* (',' named_arguments)?
        //        | named_arguments
        // Comments after a ',' (B431): run i is what comes before item i, counting the
        // first argument, each function_argument and the named_arguments in that order.
        var runs = CommentRuns(context);
        if (context.expression() != null)
        {
            if (_inGraphicsAnnotationLevel > 0)
                _inGraphicsAnnotationLevel++;

            Visit(context.expression());

            if (_inGraphicsAnnotationLevel > 0)
                _inGraphicsAnnotationLevel--;

            // Visit additional function arguments
            var funcArgs = context.function_argument();
            if (funcArgs != null)
            {
                for (int j = 0; j < funcArgs.Length; j++)
                {
                    Write(",");
                    if (!WroteSeparatorComments(runs, 1 + j))
                        SeparateGraphicsArgument();
                    Visit(funcArgs[j]);
                }
            }

            // Visit named arguments if present
            if (context.named_arguments() != null)
            {
                Write(",");
                if (!WroteSeparatorComments(runs, 1 + (funcArgs?.Length ?? 0)))
                    SeparateGraphicsArgument();
                Visit(context.named_arguments());
            }

            if (context.for_indices() != null)
            {
                Space();
                Write(Keyword("for"));
                Space();
                Visit(context.for_indices());
            }
        }
        else if (context.function_partial_application() != null)
        {
            Visit(context.function_partial_application());

            var funcArgs = context.function_argument();
            if (funcArgs != null)
            {
                for (int j = 0; j < funcArgs.Length; j++)
                {
                    Write(",");
                    if (!WroteSeparatorComments(runs, 1 + j))
                        Space();
                    Visit(funcArgs[j]);
                }
            }

            if (context.named_arguments() != null)
            {
                Write(",");
                if (!WroteSeparatorComments(runs, 1 + (funcArgs?.Length ?? 0)))
                    Space();
                Visit(context.named_arguments());
            }
        }
        else if (context.named_arguments() != null)
        {
            Visit(context.named_arguments());
        }

        return null;
    }

    /// <summary>
    /// Writes the comments before item <paramref name="item"/> of a function call's arguments
    /// (B431), if there are any, leaving the line ready for the item; false when there are none and
    /// the caller separates the item as it always has.
    /// </summary>
    private bool WroteSeparatorComments(CommentRun[]? runs, int item)
    {
        if (runs == null || item >= runs.Length || !runs[item].Any)
            return false;
        WriteListComments(runs[item], _callUsingMultiLine);
        return true;
    }

    /// <summary>
    /// What follows the ',' before a positional argument: a new line in a multi-line graphics
    /// element, otherwise a space.
    /// </summary>
    private void SeparateGraphicsArgument()
    {
        if (_inGraphicsAnnotationLevel >= 1 && _inGraphicsAnnotationLevel <= 2 && !_inSingleLineGraphicsElement)
            EmitLine();
        else
            Space();
    }

    public override object? VisitFunction_argument([NotNull] modelicaParser.Function_argumentContext context)
    {
        // function_partial_application | expression
        if (context.function_partial_application() != null)
        {
            Visit(context.function_partial_application());
        }
        else if (context.expression() != null)
        {
            if (_inGraphicsAnnotationLevel>0)
            {
                _inGraphicsAnnotationLevel++;
            }

            Visit(context.expression());

            if (_inGraphicsAnnotationLevel>0)
            {
                _inGraphicsAnnotationLevel--;
            }
        }

        return null;
    }

    public override object? VisitFunction_partial_application([NotNull] modelicaParser.Function_partial_applicationContext context)
    {
        // 'function' type_specifier '(' (named_arguments)? ')'
        Write(Keyword("function"));
        Space();
        if (context.type_specifier() != null)
            Visit(context.type_specifier());
        Write("(");
        if (context.named_arguments() != null)
            Visit(context.named_arguments());
        Write(")");
        return null;
    }

    public override object? VisitArray_arguments([NotNull] modelicaParser.Array_argumentsContext context)
    {
        // Grammar: expression (',' expression)* ('for' for_indices)?
        var expressions = context.expression();
        // Comments after a ',' (B431). A multi-line graphics array already puts one
        // element a line; anywhere else the element after a comment continues a level in.
        var runs = CommentRuns(context);
        if (expressions != null && expressions.Length > 0)
        {
            for (int i = 0; i < expressions.Length; i++)
            {
                if (i > 0)
                {
                    Write(",");
                    if (runs != null && runs[i].Any)
                        WriteListComments(runs[i],
                            _inGraphicsAnnotationLevel >= 1 && _inGraphicsAnnotationLevel <= 2 && !_inSingleLineGraphicsElement);
                    else
                        SeparateGraphicsArgument();
                }

                if (_inGraphicsAnnotationLevel > 0)
                    _inGraphicsAnnotationLevel++;

                Visit(expressions[i]);

                if (_inGraphicsAnnotationLevel > 0)
                    _inGraphicsAnnotationLevel--;
            }

            if (context.for_indices() != null)
            {
                Space();
                Write(Keyword("for"));
                Space();
                Visit(context.for_indices());
            }
        }

        return null;
    }

    public override object? VisitNamed_arguments([NotNull] modelicaParser.Named_argumentsContext context)
    {
        // Grammar: named_argument (',' named_argument)*
        var namedArgs = context.named_argument();
        if (namedArgs == null || namedArgs.Length == 0)
            return null;

        VisitFirstArgument(namedArgs[0], namedArgs.Length);
        var runs = CommentRuns(context);

        for (int i = 1; i < namedArgs.Length; i++)
        {
            Write(",");

            // Comments after the ',' (B431), as in VisitFunction_arguments.
            if (WroteSeparatorComments(runs, i))
            {
                Visit(namedArgs[i]);
                continue;
            }

            // Check if line is too long or will be too long with next argument
            var nextArgText = namedArgs[i].GetText() ?? "";
            var estimatedLength = GetCurrentLinePlainTextLength() + 1 + nextArgText.Length; // +1 for space
            bool needsWrapForLength = !_inDocumentationAnnotation && estimatedLength > (_maxLineLength - 3);

            // Determine if we need to wrap
            bool shouldWrap = (_inGraphicsAnnotationLevel > 0 && !_inSingleLineGraphicsElement) ||
                             (_parentUsingMultiLine && !_inSingleLineGraphicsElement) ||
                             needsWrapForLength;

            if (shouldWrap)
            {
                bool needsExtraIndent = needsWrapForLength && !_parentUsingMultiLine;

                EmitLine();
                if (needsExtraIndent)
                    Indent();
                if (needsExtraIndent)
                    AddIndentToCurrentLine();

                Visit(namedArgs[i]);

                if (needsExtraIndent)
                    Dedent();
            }
            else
            {
                Space();
                Visit(namedArgs[i]);
            }
        }

        return null;
    }

    public override object? VisitNamed_argument([NotNull] modelicaParser.Named_argumentContext context)
    {
        // IDENT '=' function_argument
        var ident = context.IDENT();
        if (ident != null)
        {
            Write(Ident(ident.GetText()));
            Write(Operator("=", false));
        }
        if (context.function_argument() != null)
            Visit(context.function_argument());

        return null;
    }

    public override object? VisitOutput_expression_list([NotNull] modelicaParser.Output_expression_listContext context)
    {
        var expressions = context.expression();
        int expressionIdx = 0;
        foreach (var child in context.children)
        {
            if (child.GetText() == ",") {
                Write(",");
                Space();
            }
            else
            {
                Visit(expressions[expressionIdx]);
                expressionIdx++;
            }
        }
        return null;
    }

    public override object? VisitExpression_list([NotNull] modelicaParser.Expression_listContext context)
    {
        var expressions = context.expression();
        // Comments after a ',' (B431) - in a matrix row, or an external call's arguments.
        var runs = CommentRuns(context);
        if (expressions != null)
        {
            for (int i = 0; i < expressions.Length; i++)
            {
                if (i > 0)
                {
                    Write(",");
                    if (runs != null && runs[i].Any)
                        WriteListComments(runs[i], multiLine: false);
                    else
                        Space();
                }
                Visit(expressions[i]);
            }
        }
        return null;
    }

    #endregion

    #region Name, Comment, and Utility Visitors

    public override object? VisitName([NotNull] modelicaParser.NameContext context)
    {
        var children = context.children;
        if (children != null)
        {
            for (int i = 0; i < children.Count; i++)
            {
                var child = children[i];
                if (child is ITerminalNode terminal)
                {
                    var text = terminal.GetText();
                    if (text == ".")
                    {
                        Write(".");
                    }
                    else if (terminal.Symbol.Type == modelicaParser.IDENT)
                    {
                        if (_nameAsType)
                            Write(Type(text));
                        else
                            Write(Name(text));
                    }
                }
            }
        }
        return null;
    }

    public override object? VisitComment([NotNull] modelicaParser.CommentContext context)
    {
        if (context.string_comment() != null && context.string_comment().GetText() != "")
        {
            Space();
            Visit(context.string_comment());
        }
        if (_showAnnotations && context.annotation() != null)
        {
            // Comments before the annotation (B409) go with it, and are hidden with it.
            if (context.c_comment() is { Length: > 0 } comments)
                WriteLeadingComments(comments);

            // Flush current line content before starting annotation on a new line.
            // If _currentLine is whitespace-only (e.g., indent left over after the parent
            // visitor already wrapped a long line), clear it instead of emitting a blank line.
            if (_currentLine.ToString().TrimEnd().Length > 0)
                EmitLine();
            else
                _currentLine.Clear();
            _withAnnotation = true;
            Indent();
            Visit(context.annotation());
            Dedent();
            //Handle the case where the annotation is a single line and we need to add the indentation
            //But skip if the annotation ended with a multi-line string (which sets _suppressNextIndentation)
            //Note: We do NOT reset _suppressNextIndentation here - it needs to persist until the actual
            //EmitLine() is called (which may happen later in a parent visitor like VisitElement_list)
            if (_currentLine.Length > 0 && !_suppressNextIndentation)
                AddIndentToCurrentLine();
        }
        return null;
    }

    public override object? VisitString_comment([NotNull] modelicaParser.String_commentContext context)
    {
        // Comments before the description (B409) are written first, each where it stood: one that
        // shared a line with what came before it stays on that line, one that started a line of its
        // own starts one here too, a level in. The description then follows on the next line, a
        // level in, because a comment ends its line. Dropping them would lose the user's text on save.
        var comments = context.c_comment();
        if (comments is { Length: > 0 })
        {
            WriteLeadingComments(comments);
            AddIndentToCurrentLine();
        }

        var strings = context.STRING();
        if (strings != null && strings.Length > 0)
        {
            for (int i = 0; i < strings.Length; i++)
            {
                if (i > 0)
                    Space();
                Write(Literal(strings[i].GetText().TrimEnd()));
            }
        }
        return null;
    }

    /// <summary>
    /// Writes comments that stand before a description or an annotation (B409), each where it
    /// stood: one that shared a line with what came before it stays on that line, one that started a
    /// line of its own starts one here too, a level in. Every comment ends its line, so whatever
    /// follows starts a new one.
    /// <para>The same placement serves the other positions a comment was accepted in by B432 - after
    /// a ',' in an enumeration, before 'constrainedby', and before an equation's ';' - where one on
    /// a line of its own lines up with what surrounds it rather than a level in
    /// (<paramref name="levelIn"/> false).</para>
    /// </summary>
    private void WriteLeadingComments(modelicaParser.C_commentContext[] comments, bool levelIn = true)
        => WriteComments(comments, LastLineOfTokenBefore(comments[0]), levelIn);

    /// <summary>
    /// <see cref="WriteLeadingComments"/> with the line of the token before the first comment
    /// already known - a list's runs know it from the walk that found them, and asking the tree
    /// instead is a search of the list per run.
    /// </summary>
    private void WriteComments(modelicaParser.C_commentContext[] comments, int? previousLine, bool levelIn)
    {
        foreach (var comment in comments)
        {
            if (previousLine is null || comment.Start.Line > previousLine)
            {
                EndLineIfAny();
                if (levelIn)
                    Indent();
                Visit(comment);
                if (levelIn)
                    Dedent();
            }
            else
            {
                Space();
                Visit(comment);
            }
            previousLine = LastLineOf(comment.Start);
        }
    }

    /// <summary>
    /// Ends the current line if anything is on it, and otherwise discards the indent left on it.
    /// After a comment the line is already ended, and an unconditional <see cref="EmitLine"/> there
    /// writes a blank line.
    /// </summary>
    private void EndLineIfAny()
    {
        if (_currentLine.ToString().Trim().Length > 0)
            EmitLine();
        else
            _currentLine.Clear();
    }

    /// <summary>
    /// A run of comments inside a bracketed list (B431), with the line of the token before it.
    /// </summary>
    private readonly record struct CommentRun(modelicaParser.C_commentContext[] Comments, int? PreviousLine)
    {
        public bool Any => Comments is { Length: > 0 };
    }

    /// <summary>
    /// The comments among <paramref name="list"/>'s children, grouped by the item they come before:
    /// run <c>i</c> is what stands between item <c>i - 1</c> and item <c>i</c>, and the last run is
    /// what follows the last item. An item is any child rule that is
    /// not a comment, so the same walk serves a list (its arguments) and the rule that brackets one
    /// (the list itself: the run before it is the opening run and the run after it the closing one).
    /// Null when there are no comments, which is every list the grammar accepted before B431, so a
    /// caller can leave its layout exactly as it was.
    /// </summary>
    private static CommentRun[]? CommentRuns(ParserRuleContext? list)
    {
        var children = list?.children;
        if (children == null)
            return null;

        int items = 0;
        bool any = false;
        foreach (var child in children)
        {
            if (child is modelicaParser.C_commentContext)
                any = true;
            else if (child is ParserRuleContext)
                items++;
        }
        if (!any)
            return null;

        var runs = new CommentRun[items + 1];
        var pending = new List<modelicaParser.C_commentContext>();
        int? lastLine = null;
        bool lastLineKnown = false;
        int? runPreviousLine = null;
        int item = 0;
        foreach (var child in children)
        {
            if (child is modelicaParser.C_commentContext comment)
            {
                if (pending.Count == 0)
                    runPreviousLine = lastLineKnown ? lastLine : LastLineOfTokenBefore(list!);
                pending.Add(comment);
                continue;
            }
            if (child is ParserRuleContext rule)
            {
                runs[item++] = new CommentRun(pending.ToArray(), runPreviousLine);
                pending.Clear();
                if (rule.Stop != null && rule.Stop.TokenIndex >= rule.Start.TokenIndex)
                {
                    lastLine = LastLineOf(rule.Stop);
                    lastLineKnown = true;
                }
            }
            else if (child is ITerminalNode terminal)
            {
                lastLine = LastLineOf(terminal.Symbol);
                lastLineKnown = true;
            }
        }
        runs[item] = new CommentRun(pending.ToArray(), runPreviousLine);
        return runs;
    }

    /// <summary>
    /// Writes a run of comments inside a bracketed list (B431), each where it stood, and leaves the
    /// line ready for what follows. A comment ends its line, so what follows starts a new one: in a
    /// list already written one item a line (<paramref name="multiLine"/>) that line is the next
    /// item's; otherwise it is a continuation line, a level in, as a list wrapped for length has.
    /// The closing bracket goes back to the line's own level (<paramref name="beforeClose"/>).
    /// </summary>
    private void WriteListComments(CommentRun run, bool multiLine, bool beforeClose = false)
    {
        WriteComments(run.Comments, run.PreviousLine, levelIn: !multiLine);
        if (!multiLine && !beforeClose)
            AddIndentToCurrentLine();
    }

    /// <summary>
    /// Writes the comments after a list's opening bracket (B431). They come before any indent for the
    /// list, so one on a line of its own goes a level in either way; one on the bracket's line stays
    /// there. In a list written one item a line the bracket's line is then ended, as it would have
    /// been; otherwise the first item continues a level in.
    /// </summary>
    private void WriteOpeningComments(CommentRun run, bool multiLine)
    {
        WriteComments(run.Comments, run.PreviousLine, levelIn: true);
        if (!multiLine)
            AddIndentToCurrentLine();
    }

    /// <summary>
    /// Ends the line before a multi-line list's closing bracket: a comment has already ended it when
    /// the list closed on one (B431), and ending it again writes a blank line.
    /// </summary>
    private void EndLineBeforeClose(bool afterComments)
    {
        if (afterComments)
            EndLineIfAny();
        else
            EmitLine();
    }

    /// <summary>
    /// Whether a matrix row starts a line of its own in the source (B462): on a later line than
    /// <paramref name="previous"/> ends on - the last token of the row before it, whichever side of
    /// the ';' the break was written, or the '[' for the first row (B463).
    /// </summary>
    private static bool RowStartsLine(IToken previous, ParserRuleContext row)
        => row.Start.Line > LastLineOf(previous);

    /// <summary>The line a token ends on — a block comment or a string can span several.</summary>
    private static int LastLineOf(IToken token)
        => token.Line + (token.Text?.Count(c => c == '\n') ?? 0);

    /// <summary>
    /// The last line of the token that comes before <paramref name="node"/> in the tree, or null
    /// when nothing does. Read from the tree rather than the token stream, which a caller may not
    /// have given the renderer.
    /// </summary>
    private static int? LastLineOfTokenBefore(IParseTree node)
    {
        var current = node;
        while (current.Parent is ParserRuleContext parent && parent.children != null)
        {
            for (int j = parent.children.IndexOf(current) - 1; j >= 0; j--)
            {
                switch (parent.children[j])
                {
                    case ITerminalNode terminal:
                        return LastLineOf(terminal.Symbol);
                    case ParserRuleContext rule when rule.Stop != null && rule.Stop.TokenIndex >= rule.Start.TokenIndex:
                        return LastLineOf(rule.Stop);
                }
            }
            current = parent;
        }
        return null;
    }

    /// <summary>The description strings of a string_comment, without any comments before them.</summary>
    private static string DescriptionText(modelicaParser.String_commentContext? context)
        => context?.STRING() is { Length: > 0 } strings
            ? string.Join(" ", strings.Select(s => s.GetText()))
            : "";

    public override object? VisitAnnotation([NotNull] modelicaParser.AnnotationContext context)
    {
        Write(Keyword("annotation"));
        Space();

        // Set annotation flag so nested modifications format correctly
        bool previousAnnotationState = _inAnnotation;
        _inAnnotation = true;

        if (context.class_modification() != null)
            Visit(context.class_modification());

        _inAnnotation = previousAnnotationState;
        return null;
    }

    public override object? VisitC_comment([NotNull] modelicaParser.C_commentContext context)
    {
        var commentText = context.GetText();

        // Handle multi-line comments /* */
        if (commentText.StartsWith("/*"))
        {
            // Split the comment text by lines
            var lines = commentText.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            bool firstLine = true;
            foreach (var line in lines)
            {
                Write(Comment(line.TrimEnd()));
                EmitLine(!firstLine);
                firstLine = false;
            }
        }
        // Handle single-line comments //
        else if (commentText.StartsWith("//"))
        {
            Write(Comment(commentText.TrimEnd()));
            EmitLine();
        }

        return null;
    }

    public override object? VisitBase_prefix([NotNull] modelicaParser.Base_prefixContext context)
    {
        var text = context.GetText();
        if (!string.IsNullOrEmpty(text))
        {
            Write(Keyword(text));
            Space();
        }
        return null;
    }

    public override object? VisitEnum_list([NotNull] modelicaParser.Enum_listContext context)
    {
        // Walked in order, because since B432 a ',' may have comments after it. Each keeps its line:
        // one after the ',' stays on the literal's line, one on a line of its own gets one here.
        var children = context.children;
        if (children == null)
            return null;
        var pending = new List<modelicaParser.C_commentContext>();
        foreach (var child in children)
        {
            switch (child)
            {
                case ITerminalNode terminal when terminal.GetText() == ",":
                    Write(",");
                    break;
                case modelicaParser.C_commentContext comment:
                    pending.Add(comment);
                    break;
                case modelicaParser.Enumeration_literalContext literal:
                    if (pending.Count > 0)
                    {
                        WriteLeadingComments(pending.ToArray(), levelIn: false);
                        pending.Clear();
                    }
                    else
                        EndLineIfAny();
                    Visit(literal);
                    break;
            }
        }
        return null;
    }

    public override object? VisitEnumeration_literal([NotNull] modelicaParser.Enumeration_literalContext context)
    {
        var ident = context.IDENT();
        if (ident != null)
            Write(Ident(ident.GetText()));
        // Only visit comment if it has actual content
        if (context.comment() != null && !string.IsNullOrWhiteSpace(context.comment().GetText()))
            Visit(context.comment());
        return null;
    }

    #endregion
}
