namespace ModelicaParser.Helpers;

/// <summary>
/// Whether a conditional component is there — <c>heatPort if useHeatPort</c> — given what its class's
/// parameters were set to.
///
/// <para><b>Deliberately a small evaluator, and it says so when it cannot tell.</b> Modelica allows
/// any boolean expression here; what libraries actually write is a boolean parameter, two joined by
/// <c>and</c>, or one negated. Anything past that answers null, and <b>a caller must take null as
/// "assume it is there"</b>: drawing a port that is switched off is a smaller lie than hiding one
/// that is switched on, and a connection into it is real either way.</para>
///
/// <para>Values come in as source text, because that is what a modification is until something
/// evaluates it. Only <c>true</c> and <c>false</c> are understood — a parameter set to an expression
/// is one of the cases this answers null for.</para>
/// </summary>
public static class ModelicaCondition
{
    /// <summary>
    /// Whether <paramref name="condition"/> holds, or null when it cannot be decided from the values
    /// given.
    /// </summary>
    /// <param name="condition">The expression as written, or null for an unconditional component,
    /// which is always there.</param>
    /// <param name="valueOf">The value bound to a name, as source text, or null when it has none.</param>
    public static bool? Evaluate(string? condition, Func<string, string?> valueOf)
    {
        ArgumentNullException.ThrowIfNull(valueOf);

        if (string.IsNullOrWhiteSpace(condition))
            return true;

        return EvaluateOr(Tokenize(condition), valueOf);
    }

    // or is the loosest binding, then and, then not, which is Modelica's precedence.
    private static bool? EvaluateOr(List<string> tokens, Func<string, string?> valueOf)
    {
        var parts = Split(tokens, "or");
        if (parts.Count == 1)
            return EvaluateAnd(parts[0], valueOf);

        var answer = (bool?)false;
        foreach (var part in parts)
        {
            var value = EvaluateAnd(part, valueOf);
            if (value == true)
                return true;           // true wins however little is known about the rest
            if (value is null)
                answer = null;
        }
        return answer;
    }

    private static bool? EvaluateAnd(List<string> tokens, Func<string, string?> valueOf)
    {
        var parts = Split(tokens, "and");
        if (parts.Count == 1)
            return EvaluateTerm(parts[0], valueOf);

        var answer = (bool?)true;
        foreach (var part in parts)
        {
            var value = EvaluateTerm(part, valueOf);
            if (value == false)
                return false;          // false wins, for the same reason
            if (value is null)
                answer = null;
        }
        return answer;
    }

    private static bool? EvaluateTerm(List<string> tokens, Func<string, string?> valueOf)
    {
        if (tokens.Count == 0)
            return null;

        if (tokens[0] == "not")
            return EvaluateTerm(tokens[1..], valueOf) switch { true => false, false => true, _ => null };

        // Anything that is not a single name or literal - a comparison, a call, an array index - is
        // past what this understands, and saying so is the point.
        if (tokens.Count != 1)
            return null;

        return Literal(tokens[0]) ?? Literal(valueOf(tokens[0]));
    }

    private static bool? Literal(string? text) => text?.Trim() switch
    {
        "true" => true,
        "false" => false,
        _ => null,
    };

    private static List<List<string>> Split(List<string> tokens, string separator)
    {
        var parts = new List<List<string>> { new List<string>() };
        foreach (var token in tokens)
        {
            if (token == separator)
                parts.Add(new List<string>());
            else
                parts[^1].Add(token);
        }
        return parts;
    }

    /// <summary>
    /// The expression as words and symbols. Parentheses are kept as tokens, which makes any
    /// parenthesised expression more than one token in a term and so undecidable — honest, and far
    /// cheaper than a parser for the one shape that would benefit.
    /// </summary>
    private static List<string> Tokenize(string condition)
    {
        var tokens = new List<string>();
        var word = new System.Text.StringBuilder();

        void Flush()
        {
            if (word.Length > 0)
            {
                tokens.Add(word.ToString());
                word.Clear();
            }
        }

        foreach (var c in condition)
        {
            if (char.IsLetterOrDigit(c) || c == '_' || c == '.' || c == '\'')
            {
                word.Append(c);
            }
            else
            {
                Flush();
                if (!char.IsWhiteSpace(c))
                    tokens.Add(c.ToString());
            }
        }

        Flush();
        return tokens;
    }
}
