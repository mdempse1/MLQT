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
        bool oneOfEachSection = false,
        bool importsFirst = false,
        bool componentsBeforeClasses = false)
    {
        _renderForCodeEditor = renderForCodeEditor;
        _showAnnotations = showAnnotations;
        _excludeClassDefinitions = excludeClassDefinitions;
        _tokenStream = tokenStream;
        _classNamesToExclude = classNamesToExclude;
        _maxLineLength = maxLineLength;
        _oneOfEachSection = oneOfEachSection;
        _importsFirst = importsFirst;
        _componentsBeforeClasses = componentsBeforeClasses;
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
                    // Handle leading C comments (// and /* */)
                    Visit(cCommentCtx);
                    EmitLine();
                }
                else if (child is ITerminalNode terminal && terminal.GetText() == "within")
                {
                    Write(Keyword("within"));
                    Space();
                    if (i + 1 < children.Count && children[i + 1] is modelicaParser.NameContext nameCtx)
                    {
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
        Visit(context.class_specifier());
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

                if (useMultiLine)
                {
                    EmitLine();
                    _indentLevel++;
                }

                Visit(context.enum_list());

                if (useMultiLine)
                {
                    EmitLine();
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

    private enum Element
    {
        Any,
        Imports,
        Extends,
        Components,
        Classes,
        ClassAndComponents
    }

    private bool WriteComposition(
        [NotNull] modelicaParser.CompositionContext context, 
        CodeSection section, 
        Element elements,
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
            var text = child.GetText();

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

                // Handle optional annotation
                var externalAnnotations = context.annotation();
                if (externalAnnotations != null && externalAnnotations.Length > 0)
                {
                    // The first annotation is part of the external clause
                    EmitLine();
                    _withAnnotation = true;
                    Indent();
                    Visit(externalAnnotations[0]);
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
                     (section == CodeSection.Any || section == CodeSection.External))
            {
                Visit(child);
            }
            
        }    

        _currentElement.Pop();
        return (section == CodeSection.External || section == CodeSection.Any) ? externalElement : alreadyWrittenSectionMarker;
    }

    public override object? VisitComposition([NotNull] modelicaParser.CompositionContext context)
    {
        bool externalElement = false;
        // Handle public/protected sections
        if (context.children != null)
        {
            if (_oneOfEachSection)
            {
                _writeFinalComments = false;

                if (_importsFirst) {
                    //Collect all imports at the top of the class
                    WriteComposition(context, CodeSection.Public, Element.Imports, true);
                    WriteComposition(context, CodeSection.Protected, Element.Imports, true);

                    //Public section
                    WriteComposition(context, CodeSection.Public, Element.Extends, true);
                    if (_componentsBeforeClasses) {
                        WriteComposition(context, CodeSection.Public, Element.Components, true);
                        _writeFinalComments = true;
                        WriteComposition(context, CodeSection.Public, Element.Classes, true);
                    }
                    else {
                        _writeFinalComments = true;
                        WriteComposition(context, CodeSection.Public, Element.ClassAndComponents, true);
                    }
                }
                else 
                    WriteComposition(context, CodeSection.Public, Element.Any, true);

                //Protected section
                _writeFinalComments = false;
                if (_importsFirst) {
                    var alreadyWrittenSectionMarker = WriteComposition(context, CodeSection.Protected, Element.Extends);
                    if (_componentsBeforeClasses) {
                        alreadyWrittenSectionMarker = WriteComposition(context, CodeSection.Protected, Element.Components, alreadyWrittenSectionMarker);
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

                //Initial Equation/Algorithm
                _currentSection.Push(CodeSection.InitialEquation);        
                _writtenSectionHeader = false;               
                WriteComposition(context, CodeSection.InitialEquation, Element.Any);
                _currentSection.Pop();

                _currentSection.Push(CodeSection.InitialAlgorithm);                       
                _writtenSectionHeader = false;               
                WriteComposition(context, CodeSection.InitialAlgorithm, Element.Any);
                _currentSection.Pop();

                //Equation/Algorithm/External
                _currentSection.Push(CodeSection.Equation);                       
                _writtenSectionHeader = false;               
                WriteComposition(context, CodeSection.Equation, Element.Any);
                _currentSection.Pop();

                _currentSection.Push(CodeSection.Algorithm);                       
                _writtenSectionHeader = false;               
                WriteComposition(context, CodeSection.Algorithm, Element.Any);
                _currentSection.Pop();

                externalElement = WriteComposition(context, CodeSection.External, Element.Any);
            }
            else 
            {
                _currentSection.Push(CodeSection.Any);        
                externalElement = WriteComposition(context, CodeSection.Any, Element.Any);
                _currentSection.Pop();
            }
        }

        // Handle final annotation (if present and not part of external clause)
        if (_showAnnotations) {
            var annotations = context.annotation();
            if (annotations != null && annotations.Length > 0)
            {
                // If there's an external clause, the first annotation was already handled
                // Otherwise, or if there are multiple annotations, handle the remaining ones
                int startIdx = externalElement ? 1 : 0;
                for (int i = startIdx; i < annotations.Length; i++)
                {
                    _classAnnotation = true;
                    EmitEmptyLine();
                    Indent();
                    Visit(annotations[i]);
                    Write(";");
                    EmitLine();
                    Dedent();
                    _classAnnotation = false;
                }
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
                else if (_currentElement.Peek() == Element.Extends && element.extends_clause() != null)
                {
                    WriteCommentIfProceedsThisElement(context, i - 1);
                    WriteElement(element);
                }
                else if ((_currentElement.Peek() == Element.Components || _currentElement.Peek() == Element.ClassAndComponents) && element.component_clause() != null)
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

        // Special case: simple 2-argument graphics elements (like Line) stay on one line
        if (_inGraphicsAnnotationLevel == 2 && numArguments == 2)
        {
            Write("(");
            if (context.argument_or_inheritence_list() != null)
                Visit(context.argument_or_inheritence_list());
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
        if (useMultiLineParens)
        {
            EmitLine();
            Indent();
        }
        if (context.argument_or_inheritence_list() != null)
            Visit(context.argument_or_inheritence_list());
        if (useMultiLineParens)
        {
            EmitLine();
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

            for (int i = 0; i < children.Count; i++)
            {
                var child = children[i];
                if (child is modelicaParser.ArgumentContext || child is modelicaParser.Inheritence_modificationContext)
                {
                    if (!first)
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
            var stringCommentText = context.comment().string_comment()?.GetText() ?? "";
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

        // Special case: simple 2-argument graphics elements (like Line) stay on one line
        if (_inGraphicsAnnotationLevel == 2 && numArguments == 2)
        {
            Write("(");
            if (context.argument_list() != null)
                Visit(context.argument_list());
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
        if (useMultiLineParens)
        {
            EmitLine();
            Indent();
        }
        if (context.argument_list() != null)
            Visit(context.argument_list());
        if (useMultiLineParens)
        {
            EmitLine();
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

        if (arguments != null)
        {
            for (int i = 0; i < arguments.Length; i++)
            {
                if (i > 0)
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
                    Visit(arguments[i]);
                }
            }
        }
        return null;
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
            _isFunction = true;
            Visit(context.component_reference());
            _isFunction = false;
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
            _isFunction = true;
            Visit(context.component_reference());
            _isFunction = false;
            Visit(functionCallArgs[0]);
        }
        else if (context.component_reference() != null)
        {
            _isFunction = true;
            Visit(context.component_reference());
            _isFunction = false;

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
        if (context.c_comment() != null && !string.IsNullOrWhiteSpace(context.c_comment().GetText()))
            Visit(context.c_comment());
        else
        {
            Visit(context.equation());
            Write(";");
            EmitLine();
        }
        return null;
    }

    public override object? VisitStatement_or_comment([NotNull] modelicaParser.Statement_or_commentContext context)
    {
        if (context.c_comment() != null && !string.IsNullOrWhiteSpace(context.c_comment().GetText()))
            Visit(context.c_comment());
        else
        {
            Visit(context.statement());
            Write(";");
            EmitLine();
        }
        return null;
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
                _isFunction = true;
                Visit(context.component_reference());
                _isFunction = false;
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
        }
        else if (context.GetText().StartsWith('['))
        {
            // Array expression [expression_list (';' expression_list)*]
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
                        Space();
                    }
                    Visit(expressionLists[i]);
                }
            }
            _bracketDepth--;
            Write("]");
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
            Write("{");
            if (resetGraphicsFlag && !singleLineGraphics)
            {
                EmitLine();
                Indent();
            }
            if (context.array_arguments() != null)
                Visit(context.array_arguments());
            if (resetGraphicsFlag) {
                _inGraphicsAnnotationLevel = 0;
                _classAnnotation = true;
                if (!singleLineGraphics)
                {
                    EmitLine();
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
                    Visit(arrSubs);
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

        Write("(");
        if (useMultiLine)
        {
            EmitLine();
            Indent();
        }
        if (context.function_arguments() != null)
            Visit(context.function_arguments());
        if (useMultiLine)
        {
            EmitLine();
            Dedent();
        }
        Write(")");

        // Restore previous state
        _inSingleLineGraphicsElement = previousSingleLineState;

        return null;
    }

    public override object? VisitFunction_arguments([NotNull] modelicaParser.Function_argumentsContext context)
    {
        // Grammar: expression (',' function_argument)* (',' named_arguments)? ('for' for_indices)?
        //        | function_partial_application (',' function_argument)* (',' named_arguments)?
        //        | named_arguments
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
                foreach (var arg in funcArgs)
                {
                    Write(",");
                    if (_inGraphicsAnnotationLevel >= 1 && _inGraphicsAnnotationLevel <= 2 && !_inSingleLineGraphicsElement)
                        EmitLine();
                    else
                        Space();
                    Visit(arg);
                }
            }

            // Visit named arguments if present
            if (context.named_arguments() != null)
            {
                Write(",");
                if (_inGraphicsAnnotationLevel >= 1 && _inGraphicsAnnotationLevel <= 2 && !_inSingleLineGraphicsElement)
                    EmitLine();
                else
                    Space();
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
                foreach (var arg in funcArgs)
                {
                    Write(",");
                    Space();
                    Visit(arg);
                }
            }

            if (context.named_arguments() != null)
            {
                Write(",");
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
        if (expressions != null && expressions.Length > 0)
        {
            for (int i = 0; i < expressions.Length; i++)
            {
                if (i > 0)
                {
                    Write(",");
                    if (_inGraphicsAnnotationLevel >= 1 && _inGraphicsAnnotationLevel <= 2 && !_inSingleLineGraphicsElement)
                        EmitLine();
                    else
                        Space();
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

        Visit(namedArgs[0]);

        for (int i = 1; i < namedArgs.Length; i++)
        {
            Write(",");

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
        if (expressions != null)
        {
            for (int i = 0; i < expressions.Length; i++)
            {
                if (i > 0)
                {
                    Write(",");
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
        var enumLiterals = context.enumeration_literal();
        if (enumLiterals != null)
        {
            for (int i = 0; i < enumLiterals.Length; i++)
            {
                if (i > 0)
                {
                    Write(",");
                    EmitLine();
                }
                Visit(enumLiterals[i]);
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
