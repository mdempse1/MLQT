using ModelicaParser.DataTypes;

namespace MLQT.Services.Checking;

/// <summary>
/// What a "suppress this rule" action covers, and which findings it therefore waives.
/// </summary>
/// <remarks>
/// <para>Backlog B119, the remainder of B20 for <c>CodeReview</c>. The writing of an <c>__MLQT</c>
/// annotation was already <c>MlqtSuppressionWriter</c>'s job; what was still inline in the page were
/// the two decisions around it — how far the waiver reaches, and which findings on screen it has just
/// made obsolete. Both are ordinary rules with edge cases, and neither was reachable without
/// rendering the page and clicking a menu item.</para>
///
/// <para>The stakes are one-sided: removing too few findings leaves stale rows that a re-check would
/// clear, which is untidy. Removing too many silently hides findings the user never waived, and they
/// come back only after the next full check — long after anybody would connect the two.</para>
/// </remarks>
public static class SuppressionScope
{
    /// <summary>
    /// The component a waiver should name, or null to waive the rule for the whole class.
    /// </summary>
    /// <param name="elementPath">The finding's element path.</param>
    /// <remarks>
    /// Only a *simple* element name scopes a waiver. A dotted path names something inside a component
    /// (<c>port.medium</c>) or an inherited element, which <c>MlqtSuppressionWriter</c> cannot locate
    /// a declaration for — annotating the class is the honest fallback, and the one the caller takes
    /// when the component-scoped write fails.
    /// </remarks>
    public static string? ComponentFor(string? elementPath) =>
        elementPath is { Length: > 0 } path && !path.Contains('.') ? path : null;

    /// <summary>
    /// Whether <paramref name="finding"/> is waived by suppressing <paramref name="ruleId"/> on
    /// <paramref name="modelId"/>, scoped to <paramref name="component"/> when there is one.
    /// </summary>
    /// <remarks>
    /// <para>Scoped to the model, because a class-level waiver is written into that class and says
    /// nothing about its siblings — including a class of the same name in another library, which is
    /// why the comparison is ordinal and exact rather than by leaf name.</para>
    ///
    /// <para>And scoped to the component when the annotation named one: suppressing a rule on
    /// <c>gain</c> must not clear the same rule on <c>offset</c> two lines below.</para>
    /// </remarks>
    public static bool Waives(LogMessage finding, string modelId, string ruleId, string? component) =>
        string.Equals(finding.ModelName, modelId, StringComparison.Ordinal)
        && string.Equals(finding.RuleId, ruleId, StringComparison.Ordinal)
        && (component is null || string.Equals(finding.ElementPath, component, StringComparison.Ordinal));

    /// <summary>A predicate for removing the findings a waiver has just made obsolete.</summary>
    public static Func<LogMessage, bool> WaivedBy(string modelId, string ruleId, string? component) =>
        finding => Waives(finding, modelId, ruleId, component);

    /// <summary>How the action describes itself once it has succeeded.</summary>
    public static string Describe(string ruleId, string? component) =>
        component is null
            ? $"Suppressed rule '{ruleId}'."
            : $"Suppressed rule '{ruleId}' on '{component}'.";
}
