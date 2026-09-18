namespace ModelicaParser.Helpers;

/// <summary>
/// Whether a call's argument is a path MLQT can know.
///
/// <para><b>Only a lone string literal is (B210).</b> <c>loadResource</c> is routinely handed a
/// composed value:</para>
/// <code>loadResource("modelica://" + packageName + "/package.mo")</code>
/// <para>which has a path only once the model is translated. The visitors captured every string
/// literal they met inside the call, so that one line produced two resources — <c>modelica://</c> and
/// <c>/package.mo</c> — and the second was then reported as a missing file. Claytex has several.</para>
///
/// <para>One implementation, because both <c>ExternalResourceExtractor</c> and <c>ModelAnalyzer</c>
/// walk these calls; they already held two copies of the URI scan, and fixing one of those and not
/// the other cost a round.</para>
/// </summary>
public static class ResourceArgument
{
    /// <summary>
    /// The string the arguments consist of, when they consist of exactly one string literal;
    /// <c>null</c> for anything else — a concatenation, a variable, a function call, or several
    /// arguments.
    /// </summary>
    /// <remarks>
    /// Decided by counting the tokens rather than by walking the grammar's argument rules: a lone
    /// literal is one token once the brackets are set aside, and any composition adds an operator or
    /// an identifier. That stays true whatever shape the argument rules take.
    /// </remarks>
    public static string? SoleStringLiteral(Antlr4.Runtime.Tree.IParseTree? arguments)
    {
        if (arguments is null)
            return null;

        string? literal = null;
        var meaningfulTokens = 0;

        if (!CountTokens(arguments, ref literal, ref meaningfulTokens))
            return null;

        if (meaningfulTokens != 1)
            return null;

        // An empty or blank literal is not a path either. loadResource("") is written as a
        // placeholder and was already filtered before this helper existed.
        return string.IsNullOrWhiteSpace(literal) ? null : literal;
    }

    /// <summary>
    /// Walks the terminals, ignoring the brackets that wrap every argument list. Returns false as
    /// soon as more than one meaningful token has been seen, so a long expression is not walked in
    /// full.
    /// </summary>
    private static bool CountTokens(
        Antlr4.Runtime.Tree.IParseTree node, ref string? literal, ref int meaningfulTokens)
    {
        if (node is Antlr4.Runtime.Tree.ITerminalNode terminal)
        {
            var text = terminal.GetText();
            if (text is "(" or ")" or "")
                return true;

            meaningfulTokens++;
            if (meaningfulTokens > 1)
                return false;

            if (text.Length >= 2 && text[0] == '"' && text[^1] == '"')
                literal = text[1..^1];

            return true;
        }

        for (var i = 0; i < node.ChildCount; i++)
        {
            if (!CountTokens(node.GetChild(i), ref literal, ref meaningfulTokens))
                return false;
        }

        return true;
    }
}
