using Antlr4.Runtime;
using Antlr4.Runtime.Tree;

namespace ModelicaGraph.ParserVisitors;

/// <summary>What a name written in a class is used as, which decides how it is looked up.</summary>
internal enum NameUseKind
{
    /// <summary>A component reference in an expression, a binding, a subscript or a connect.</summary>
    Value,
    /// <summary>The name a function call, a record constructor or a call statement calls.</summary>
    Call,
    /// <summary>A type: of a component, a short class, a constraining clause.</summary>
    Type,
    /// <summary>The base class of an extends clause, or of a short class definition.</summary>
    Base,
    /// <summary>What an import clause names, which is looked up from the top.</summary>
    Import,
    /// <summary>The target of a modification: a member of <see cref="NameUse.OwnerType"/>.</summary>
    Modifier,
}

/// <summary>
/// One name a class uses. <see cref="Text"/> is the name as written with subscripts and white space
/// removed; for a <see cref="NameUseKind.Modifier"/> it is the type the modification is written on,
/// and <see cref="Path"/> the members it reaches through, outermost first.
/// </summary>
internal sealed record NameUse(NameUseKind Kind, string Text, int Line)
{
    /// <summary>For a modifier, the members from the modified type down to the one modified.</summary>
    public IReadOnlyList<string> Path { get; init; } = [];

    /// <summary>For a modifier: whether <see cref="Text"/> is a base class rather than a component type,
    /// which is looked up as an extends clause's name is.</summary>
    public bool OwnerIsBase { get; init; }

    /// <summary>The type a modifier is written on - <see cref="Text"/> - or null for any other use.</summary>
    public string? OwnerType => Kind == NameUseKind.Modifier ? Text : null;
}

/// <summary>
/// Lists every name one class uses, for <c>UndeclaredNameAnalyzer</c>: references in declarations,
/// modifications, conditions, subscripts, equations and algorithms; the names called; types, bases and
/// imports; and the target of every modification.
/// </summary>
/// <remarks>
/// <para><b>What is left out is what is not looked up in the class's scope.</b> Annotations, whose
/// content is annotation grammar rather than Modelica names; descriptions; a named argument's name,
/// which is the called function's input; an external clause's function, which is a C name; the names a
/// <c>for</c> loop or a comprehension binds, inside it; and every nested class, which has a node of its
/// own and is looked up from there.</para>
/// </remarks>
internal static class NameUseCollector
{
    public static List<NameUse> Collect(modelicaParser.Stored_definitionContext tree)
    {
        var uses = new List<NameUse>();
        if (tree.class_definition() is [var root, ..])
            new Walker(uses, root).Walk(root, Bound.None);
        return uses;
    }

    // The iterator names in force, innermost last. Immutable, so a branch cannot leak into its sibling.
    private sealed class Bound
    {
        public static readonly Bound None = new(null, []);
        private readonly Bound? _outer;
        private readonly IReadOnlyList<string> _names;

        private Bound(Bound? outer, IReadOnlyList<string> names) { _outer = outer; _names = names; }

        public Bound With(modelicaParser.For_indicesContext? indices) =>
            indices is null ? this : new Bound(this, indices.for_index().Select(i => i.IDENT().GetText()).ToList());

        public bool Contains(string name)
        {
            for (var b = this; b is not null; b = b._outer)
                if (b._names.Contains(name))
                    return true;
            return false;
        }
    }

    private sealed class Walker(List<NameUse> uses, modelicaParser.Class_definitionContext root)
    {
        public void Walk(IParseTree node, Bound bound)
        {
            switch (node)
            {
                case ITerminalNode:
                case modelicaParser.AnnotationContext:
                case modelicaParser.CommentContext:
                case modelicaParser.String_commentContext:
                case modelicaParser.Inheritence_modificationContext:
                    return;

                case modelicaParser.Class_definitionContext nested when !ReferenceEquals(nested, root):
                    return;

                case modelicaParser.Import_clauseContext import:
                    if (ModelicaGraph.ClassImports.Describe(import) is { } described)
                        uses.Add(new NameUse(NameUseKind.Import, described, import.Start.Line));
                    return;

                case modelicaParser.Long_class_specifierContext longClass:
                    // A class extends (`redeclare model extends M(...)`): what it modifies is the replaced
                    // class's, which is not looked up by name. Its values are expressions here.
                    if (longClass.class_modification() is { } classExtendsMods)
                        WalkArguments(classExtendsMods, ownerType: null, ownerIsBase: true, [], bound);
                    Walk(longClass.composition(), bound);
                    return;

                case modelicaParser.Short_class_specifierContext shortClass:
                    if (shortClass.type_specifier() is { } shortBase)
                    {
                        var baseName = Text(shortBase);
                        uses.Add(new NameUse(NameUseKind.Base, baseName, shortBase.Start.Line));
                        WalkChildren(shortClass.array_subscripts(), bound);
                        if (shortClass.class_modification() is { } shortMods)
                            WalkArguments(shortMods, baseName, ownerIsBase: true, [], bound);
                    }
                    return;

                case modelicaParser.Der_class_specifierContext der:
                    AddType(der.type_specifier());
                    return;

                case modelicaParser.Extends_clauseContext extends:
                    var extendsName = Text(extends.type_specifier());
                    uses.Add(new NameUse(NameUseKind.Base, extendsName, extends.Start.Line));
                    if (extends.class_or_inheritence_modification()?.argument_or_inheritence_list() is { } list)
                        foreach (var argument in list.argument())
                            WalkArgument(argument, extendsName, ownerIsBase: true, [], bound);
                    return;

                case modelicaParser.Constraining_clauseContext constraining:
                    var constrainingName = AddType(constraining.type_specifier());
                    if (constraining.class_modification() is { } constrainingMods)
                        WalkArguments(constrainingMods, constrainingName, ownerIsBase: false, [], bound);
                    return;

                case modelicaParser.Component_clauseContext clause:
                    var typeName = AddType(clause.type_specifier());
                    WalkChildren(clause.array_subscripts(), bound);
                    foreach (var declaration in clause.component_list().component_declaration())
                    {
                        WalkDeclaration(declaration.declaration(), typeName, bound);
                        if (declaration.condition_attribute() is { } condition)
                            Walk(condition.expression(), bound);
                    }
                    return;

                case modelicaParser.For_equationContext forEquation:
                    WalkFor(forEquation, forEquation.for_indices(), bound);
                    return;

                case modelicaParser.For_statementContext forStatement:
                    WalkFor(forStatement, forStatement.for_indices(), bound);
                    return;

                case modelicaParser.Function_argumentsContext arguments when arguments.for_indices() is { } indices:
                    WalkFor(arguments, indices, bound);
                    return;

                case modelicaParser.Array_argumentsContext arguments when arguments.for_indices() is { } indices:
                    WalkFor(arguments, indices, bound);
                    return;

                case modelicaParser.Named_argumentContext named:
                    Walk(named.function_argument(), bound);
                    return;

                case modelicaParser.Function_partial_applicationContext partial:
                    AddCall(partial.type_specifier());
                    if (partial.named_arguments() is { } partialArguments)
                        Walk(partialArguments, bound);
                    return;

                case modelicaParser.External_function_callContext external:
                    if (external.component_reference() is { } result)
                        Walk(result, bound);
                    if (external.expression_list() is { } externalArguments)
                        Walk(externalArguments, bound);
                    return;

                case modelicaParser.PrimaryContext primary when primary.function_call_args() is { } args:
                    if (primary.component_reference() is { } callee)
                        AddCallee(callee, bound);
                    Walk(args, bound);
                    return;

                case modelicaParser.EquationContext equation when equation.function_call_args() is { } args:
                    AddCallee(equation.component_reference(), bound);
                    Walk(args, bound);
                    return;

                case modelicaParser.StatementContext statement when statement.function_call_args() is [var args]
                                                                   && !IsDerStatement(statement):
                    // `f(x);` and `(a, b) := f(x);` call a function; `der(x) := e` assigns.
                    AddCallee(statement.component_reference(), bound);
                    if (statement.output_expression_list() is { } outputs)
                        Walk(outputs, bound);
                    Walk(args, bound);
                    return;

                case modelicaParser.Component_referenceContext reference:
                    AddValue(reference, bound);
                    WalkChildren(reference.array_subscripts(), bound);
                    return;

                default:
                    for (var i = 0; i < node.ChildCount; i++)
                        Walk(node.GetChild(i), bound);
                    return;
            }
        }

        private void WalkFor(ParserRuleContext loop, modelicaParser.For_indicesContext indices, Bound bound)
        {
            // The iterators are bound for the ranges too: a later index's range may name an earlier
            // index, and binding them for every range errs towards reporting less.
            var inner = bound.With(indices);
            for (var i = 0; i < loop.ChildCount; i++)
                Walk(loop.GetChild(i), inner);
        }

        private void WalkChildren(IEnumerable<IParseTree>? nodes, Bound bound)
        {
            if (nodes is null)
                return;
            foreach (var node in nodes)
                Walk(node, bound);
        }

        private void WalkChildren(IParseTree? node, Bound bound)
        {
            if (node is not null)
                Walk(node, bound);
        }

        private void WalkDeclaration(modelicaParser.DeclarationContext declaration, string? typeName, Bound bound)
        {
            WalkChildren(declaration.array_subscripts(), bound);
            if (declaration.modification() is { } modification)
                WalkModification(modification, typeName, ownerIsBase: false, [], bound);
        }

        private void WalkModification(
            modelicaParser.ModificationContext modification, string? ownerType, bool ownerIsBase,
            IReadOnlyList<string> path, Bound bound)
        {
            if (modification.class_modification() is { } classModification)
                WalkArguments(classModification, ownerType, ownerIsBase, path, bound);
            if (modification.modification_expression()?.expression() is { } value)
                Walk(value, bound);
        }

        private void WalkArguments(
            modelicaParser.Class_modificationContext modification, string? ownerType, bool ownerIsBase,
            IReadOnlyList<string> path, Bound bound)
        {
            if (modification.argument_list() is { } list)
                foreach (var argument in list.argument())
                    WalkArgument(argument, ownerType, ownerIsBase, path, bound);
        }

        private void WalkArgument(
            modelicaParser.ArgumentContext argument, string? ownerType, bool ownerIsBase,
            IReadOnlyList<string> path, Bound bound)
        {
            if (argument.element_modification_or_replaceable() is { } modOrReplaceable)
            {
                if (modOrReplaceable.element_modification() is { } element)
                {
                    var target = Extend(path, element.name().IDENT().Select(i => i.GetText()));
                    AddModifier(ownerType, ownerIsBase, target, element.Start.Line);
                    if (element.modification() is { } nested)
                        WalkModification(nested, ownerType, ownerIsBase, target, bound);
                }
                else if (modOrReplaceable.element_replaceable() is { } replaceable)
                {
                    WalkRedeclared(replaceable.short_class_definition(), replaceable.component_clause1(), ownerType, ownerIsBase, path, bound);
                    WalkChildren(replaceable.constraining_clause(), bound);
                }
            }
            else if (argument.element_redeclaration() is { } redeclaration)
            {
                if (redeclaration.element_replaceable() is { } replaceable)
                {
                    WalkRedeclared(replaceable.short_class_definition(), replaceable.component_clause1(), ownerType, ownerIsBase, path, bound);
                    WalkChildren(replaceable.constraining_clause(), bound);
                }
                else
                {
                    WalkRedeclared(redeclaration.short_class_definition(), redeclaration.component_clause1(), ownerType, ownerIsBase, path, bound);
                }
            }
        }

        // `redeclare package Medium = Water` or `redeclare Real x = 1` inside a modification: the name is a
        // member of the modified type, and what it is redeclared as is a type written here.
        private void WalkRedeclared(
            modelicaParser.Short_class_definitionContext? shortClass, modelicaParser.Component_clause1Context? component,
            string? ownerType, bool ownerIsBase, IReadOnlyList<string> path, Bound bound)
        {
            if (shortClass?.short_class_specifier() is { } specifier)
            {
                AddModifier(ownerType, ownerIsBase, Extend(path, [specifier.IDENT().GetText()]), specifier.Start.Line);
                Walk(specifier, bound);
            }
            else if (component is not null)
            {
                var declaration = component.component_declaration1().declaration();
                AddModifier(ownerType, ownerIsBase, Extend(path, [declaration.IDENT().GetText()]), declaration.Start.Line);
                var redeclaredType = AddType(component.type_specifier());
                WalkDeclaration(declaration, redeclaredType, bound);
            }
        }

        private static IReadOnlyList<string> Extend(IReadOnlyList<string> path, IEnumerable<string> more) =>
            [.. path, .. more];

        private void AddModifier(string? ownerType, bool ownerIsBase, IReadOnlyList<string> path, int line)
        {
            if (ownerType is not null && path.Count > 0)
                uses.Add(new NameUse(NameUseKind.Modifier, ownerType, line) { Path = path, OwnerIsBase = ownerIsBase });
        }

        private string? AddType(modelicaParser.Type_specifierContext? type)
        {
            if (type is null)
                return null;
            var name = Text(type);
            uses.Add(new NameUse(NameUseKind.Type, name, type.Start.Line));
            return name;
        }

        private void AddCall(modelicaParser.Type_specifierContext? type)
        {
            if (type is not null)
                uses.Add(new NameUse(NameUseKind.Call, Text(type), type.Start.Line));
        }

        private void AddCallee(modelicaParser.Component_referenceContext? callee, Bound bound)
        {
            if (callee is null)
                return;
            var name = ReferenceText(callee);
            if (!bound.Contains(FirstSegment(callee)))
                uses.Add(new NameUse(NameUseKind.Call, name, callee.Start.Line));
            WalkChildren(callee.array_subscripts(), bound);
        }

        private void AddValue(modelicaParser.Component_referenceContext reference, Bound bound)
        {
            if (!bound.Contains(FirstSegment(reference)))
                uses.Add(new NameUse(NameUseKind.Value, ReferenceText(reference), reference.Start.Line));
        }

        private static bool IsDerStatement(modelicaParser.StatementContext statement) =>
            statement.GetChild(0) is ITerminalNode { Symbol.Text: "der" };

        private static string FirstSegment(modelicaParser.Component_referenceContext reference) =>
            reference.IDENT(0).GetText();

        // The reference's identifiers joined by dots, a leading dot kept, subscripts left out.
        private static string ReferenceText(modelicaParser.Component_referenceContext reference)
        {
            var names = string.Join('.', reference.IDENT().Select(i => i.GetText()));
            return reference.GetChild(0) is ITerminalNode { Symbol.Text: "." } ? "." + names : names;
        }

        // A type name has no subscripts or white space inside it, so its text is the name.
        private static string Text(modelicaParser.Type_specifierContext type) => type.GetText();
    }
}
