using Antlr4.Runtime.Misc;
using ModelicaParser.SpellChecking;

namespace ModelicaParser.StyleRules;

/// <summary>
/// Shared scope handling for the spell-check visitors: the identifiers that count as valid words
/// while a class is being visited, so that prose referring to the class's own members is not read as
/// a set of misspellings.
///
/// <para>A class's element names are collected when the class is entered rather than as the walk
/// reaches each declaration. The class's own description string is written before any of them, and a
/// <c>Documentation</c> annotation can be too, so a name collected on arrival is a name the earlier
/// text never sees.</para>
///
/// <para>Names inherited through <c>extends</c> arrive via <c>inheritedElementNames</c>: resolving a
/// base class needs the dependency graph, which the parser layer has no access to, so the caller
/// supplies the lookup. Without one only the class's own declarations are known — and in a library
/// built on base classes, a description of an inherited port or parameter is exactly the prose that
/// gets flagged.</para>
/// </summary>
public abstract class SpellCheckVisitorBase : VisitorWithModelNameTracking
{
    private readonly SpellChecker _spellChecker;
    private readonly IReadOnlySet<string>? _knownModelNames;
    private readonly Func<string, IReadOnlySet<string>>? _inheritedElementNames;
    private readonly Stack<HashSet<string>> _scopedNames = new();

    protected SpellCheckVisitorBase(
        SpellChecker spellChecker,
        IReadOnlySet<string>? knownModelNames,
        string basePackage,
        Func<string, IReadOnlySet<string>>? inheritedElementNames)
        : base(basePackage)
    {
        _spellChecker = spellChecker;
        _knownModelNames = knownModelNames;
        _inheritedElementNames = inheritedElementNames;
    }

    protected override void OnClassEntered()
    {
        // Ordinal: these are Modelica identifiers, and Modelica is case sensitive. Matching them
        // loosely would let a real misspelling through whenever it differed from a name in scope
        // only by case.
        _scopedNames.Push(new HashSet<string>(StringComparer.Ordinal));
    }

    protected override void OnClassExited()
    {
        if (_scopedNames.Count > 0)
            _scopedNames.Pop();
    }

    public sealed override object? VisitLong_class_specifier(
        [NotNull] modelicaParser.Long_class_specifierContext context)
    {
        CollectClassScope(context.composition());
        OnClassScopeReady(context);
        return base.VisitLong_class_specifier(context);
    }

    /// <summary>
    /// Called once the names valid inside the class are known and before its body is walked.
    /// Override to check text that belongs to the class itself.
    /// </summary>
    protected virtual void OnClassScopeReady(modelicaParser.Long_class_specifierContext context)
    {
    }

    /// <summary>
    /// Whether a word is spelled correctly, given the names in scope.
    ///
    /// <para>The scopes are consulted directly rather than merged with the known model names into one
    /// set: that set holds every class in the graph — tens of thousands of them for a project with
    /// reference libraries loaded — and merging it built a fresh copy for every description string in
    /// the library.</para>
    /// </summary>
    protected bool IsSpelledCorrectly(string word)
    {
        if (IsNameInScope(word))
            return true;

        // The possessive of a name in scope reads as prose about that element ("the port's
        // temperature"), so accept it the same way the spell checker accepts a possessive of a word
        // it knows.
        var possessiveBase = SpellChecker.PossessiveBaseOf(word);
        if (possessiveBase is not null && IsNameInScope(possessiveBase))
            return true;

        return _spellChecker.IsCorrect(word, _knownModelNames);
    }

    private bool IsNameInScope(string word)
    {
        foreach (var scope in _scopedNames)
        {
            if (scope.Contains(word))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Adds an element name to the class currently being visited. Kept for names that only become
    /// known during the walk, such as those in a class body this visitor reaches by another route.
    /// </summary>
    protected void AddNameToScope(string? name)
    {
        if (!string.IsNullOrEmpty(name) && _scopedNames.Count > 0)
            _scopedNames.Peek().Add(name);
    }

    /// <summary>
    /// Records everything declared in the class — its own components and nested classes, plus
    /// whatever it inherits — before any of its text is checked.
    /// </summary>
    private void CollectClassScope(modelicaParser.CompositionContext? composition)
    {
        if (_scopedNames.Count == 0)
            return;

        var scope = _scopedNames.Peek();

        var inherited = _inheritedElementNames?.Invoke(CurrentModelName);
        if (inherited is not null)
        {
            foreach (var name in inherited)
                scope.Add(name);
        }

        if (composition == null)
            return;

        foreach (var list in composition.element_list())
        {
            foreach (var element in list.element())
            {
                CollectElementNames(element, scope);
            }
        }
    }

    private static void CollectElementNames(modelicaParser.ElementContext element, HashSet<string> scope)
    {
        if (element.component_clause()?.component_list() is { } components)
        {
            foreach (var declaration in components.component_declaration())
            {
                var name = declaration.declaration()?.IDENT()?.GetText();
                if (!string.IsNullOrEmpty(name))
                    scope.Add(name);
            }
        }

        // Nested classes are elements of this one too, and a description may well name one.
        if (element.class_definition()?.class_specifier() is { } specifier)
        {
            var name = specifier.long_class_specifier()?.IDENT()?.FirstOrDefault()?.GetText()
                ?? specifier.short_class_specifier()?.IDENT()?.GetText()
                ?? specifier.der_class_specifier()?.IDENT()?.FirstOrDefault()?.GetText();
            if (!string.IsNullOrEmpty(name))
                scope.Add(name);
        }
    }
}
