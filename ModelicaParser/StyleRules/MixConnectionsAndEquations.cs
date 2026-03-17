using Antlr4.Runtime.Misc;
using ModelicaParser.DataTypes;

namespace ModelicaParser.StyleRules;

internal class Tracker
{
    public bool FoundConnection {get;set;}= false;
    public bool FoundEquation {get;set;}= false;
    public bool LoggedError {get;set;}= false;
}

/// <summary>
/// Visitor that checks equation sections don't mix connect statements and equations.
/// </summary>
public class MixConnectionsAndEquations : VisitorWithModelNameTracking
{
    private readonly Stack<Tracker> _tracker = new();
    private bool _isInitial = false;

    /// <summary>
    /// Creates a new instance with an optional base package prefix.
    /// </summary>
    /// <param name="basePackage">The package prefix to use when the code doesn't have a within clause</param>
    public MixConnectionsAndEquations(string basePackage = "") : base(basePackage) { }

    protected override void OnClassEntered()
    {
        _tracker.Push(new Tracker());
    }

    protected override void OnClassExited()
    {
        _tracker.Pop();
    }

    public override object? VisitEquation_section([NotNull] modelicaParser.Equation_sectionContext context)
    {
        if (context.GetChild(0).GetText() == "initial")
            _isInitial = true;
        else
            _isInitial = false;

        return base.VisitEquation_section(context);
    }

    public override object? VisitEquation([NotNull] modelicaParser.EquationContext context)
    {
        if (!_isInitial)
        {
            var thisClassTracker = _tracker.Peek();
            if (context.connect_clause() != null)
                thisClassTracker.FoundConnection = true;
            else if (context.simple_expression() != null)
                thisClassTracker.FoundEquation = true;

            if (thisClassTracker.FoundConnection && thisClassTracker.FoundEquation && !thisClassTracker.LoggedError)
            {
                AddViolation(context.Start.Line, "This class contains both equations and connect equations");
                thisClassTracker.LoggedError = true;
            }
        }
        return base.VisitEquation(context);
    }
}
