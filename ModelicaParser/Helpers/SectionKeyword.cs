using Antlr4.Runtime.Tree;

namespace ModelicaParser.Helpers;

/// <summary>
/// The keyword a child of a class body is, if it is one — <c>public</c>, <c>protected</c>,
/// <c>external</c> — or the empty string for anything else.
/// </summary>
/// <remarks>
/// <para><b>Why this exists.</b> Four visitors walk a <c>composition</c>'s children to find its section
/// keywords, and each of them asked every child for its text: <c>child.GetText() == "public"</c>. For
/// a keyword that is one token. For the other children — whole element lists, equation sections — it
/// rebuilds the child's entire source by concatenating every token under it, and an outer class's
/// body contains its nested classes, each of which is then walked again. Measured on Claytex with
/// <c>dotnet-trace</c>: <c>RuleContext.GetText</c> was <b>30% of all non-idle time in a check</b>, and
/// these loops were most of it — <c>OneOfEachSection</c> 13.6%, and
/// <c>PublicParametersAndConstantsHaveDescription</c> and <c>FollowNamingConvention</c> 4% each.</para>
///
/// <para>The keywords are terminals in the grammar and nothing that is not a terminal can spell one, so
/// asking only terminals gives the same answer without building any text.</para>
/// </remarks>
public static class SectionKeyword
{
    /// <summary>The terminal's text, or <see cref="string.Empty"/> for a rule node.</summary>
    public static string Of(IParseTree child) =>
        child is ITerminalNode terminal ? terminal.Symbol.Text ?? string.Empty : string.Empty;
}
