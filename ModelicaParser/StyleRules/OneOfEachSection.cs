using Antlr4.Runtime.Misc;
using ModelicaParser.DataTypes;

namespace ModelicaParser.StyleRules;

/// <summary>
/// Visitor that checks there is no more than one of public, protected, equation and algorithm sections.
/// </summary>
public class OneOfEachSection : VisitorWithModelNameTracking
{
    private readonly bool _onePublicSection;
    private readonly bool _oneProtectedSection;
    private readonly bool _oneEquationSection;
    private readonly bool _oneInitialEquationSection;
    private readonly bool _allowEquationAndAlgorithm;

    /// <summary>
    /// Creates a new instance with the specified options and optional base package prefix.
    /// </summary>
    /// <param name="onePublicSection">Check for single public section.</param>
    /// <param name="oneProtectedSection">Check for single protected section.</param>
    /// <param name="oneEquationSection">Check for single equation section.</param>
    /// <param name="oneInitialEquationSection">Check for single initial equation section.</param>
    /// <param name="allowEquationAndAlgorithm">Allow both equation and algorithm sections.</param>
    /// <param name="basePackage">The package prefix to use when the code doesn't have a within clause.</param>
    public OneOfEachSection(
        bool onePublicSection,
        bool oneProtectedSection,
        bool oneEquationSection,
        bool oneInitialEquationSection,
        bool allowEquationAndAlgorithm,
        string basePackage = "") : base(basePackage)
    {
        _onePublicSection = onePublicSection;
        _oneProtectedSection = oneProtectedSection;
        _oneEquationSection = oneEquationSection;
        _oneInitialEquationSection = oneInitialEquationSection;
        _allowEquationAndAlgorithm = allowEquationAndAlgorithm;
    }

    public override object? VisitComposition([NotNull] modelicaParser.CompositionContext context)
    {
        var children = context.children;
        if (children == null)
            return base.VisitComposition(context);

        var tracker = new SectionTracker();
        for (int i = 0; i < children.Count; i++)
        {
            var child = children[i];
            var text = child.GetText();

            if (text == "public")
            {
                CheckPublicSection(context, tracker);
                tracker.PublicSection++;
            }
            else if (text == "protected")
            {
                CheckProtectedSection(context, tracker);
                tracker.ProtectedSection++;
            }
            else if (child is modelicaParser.Equation_sectionContext)
            {
                CheckEquationSection(context, tracker, child.GetChild(0).GetText() == "initial");
            }
            else if (child is modelicaParser.Algorithm_sectionContext)
            {
                CheckAlgorithmSection(context, tracker, child.GetChild(0).GetText() == "initial");
            }
            else if (child is modelicaParser.Element_listContext && text.Length > 0 &&
                     tracker.PublicSection == 0 && tracker.ProtectedSection == 0)
            {
                tracker.PublicSection++;
            }
        }

        return base.VisitComposition(context);
    }

    private void CheckPublicSection(modelicaParser.CompositionContext context, SectionTracker tracker)
    {
        if (tracker.PublicSection == 1 && _onePublicSection)
        {
            AddViolation(context.Start.Line, "This class contains more than 1 public section");
        }
    }

    private void CheckProtectedSection(modelicaParser.CompositionContext context, SectionTracker tracker)
    {
        if (tracker.ProtectedSection == 1 && _oneProtectedSection)
        {
            AddViolation(context.Start.Line, "This class contains more than 1 protected section");
        }
    }

    private void CheckEquationSection(modelicaParser.CompositionContext context, SectionTracker tracker, bool isInitial)
    {
        if (isInitial)
        {
            if (tracker.InitialEquationSection == 1 && _oneInitialEquationSection)
            {
                AddViolation(context.Start.Line, "This class contains more than 1 initial equation section");
            }
            if (tracker.InitialAlgorithmSection == 1 && !_allowEquationAndAlgorithm)
            {
                AddViolation(context.Start.Line, "This class contains both an initial algorithm and an initial equation section");
            }
            tracker.InitialEquationSection++;
        }
        else
        {
            if (tracker.EquationSection == 1 && _oneEquationSection)
            {
                AddViolation(context.Start.Line, "This class contains more than 1 equation section");
            }
            if (tracker.AlgorithmSection == 1 && !_allowEquationAndAlgorithm)
            {
                AddViolation(context.Start.Line, "This class contains both an algorithm and an equation section");
            }
            tracker.EquationSection++;
        }
    }

    private void CheckAlgorithmSection(modelicaParser.CompositionContext context, SectionTracker tracker, bool isInitial)
    {
        if (isInitial)
        {
            if (tracker.InitialAlgorithmSection == 1 && _oneInitialEquationSection)
            {
                AddViolation(context.Start.Line, "This class contains more than 1 initial algorithm section");
            }
            if (tracker.InitialEquationSection == 1 && !_allowEquationAndAlgorithm)
            {
                AddViolation(context.Start.Line, "This class contains both an initial algorithm and an initial equation section");
            }
            tracker.InitialAlgorithmSection++;
        }
        else
        {
            if (tracker.AlgorithmSection == 1 && _oneEquationSection)
            {
                AddViolation(context.Start.Line, "This class contains more than 1 algorithm section");
            }
            if (tracker.EquationSection == 1 && !_allowEquationAndAlgorithm)
            {
                AddViolation(context.Start.Line, "This class contains both an algorithm and an equation section");
            }
            tracker.AlgorithmSection++;
        }
    }
}
