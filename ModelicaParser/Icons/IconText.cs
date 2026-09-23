using System.Text;

namespace ModelicaParser.Icons;

/// <summary>
/// The substitutions a <c>Text</c> primitive's string carries, which are what make an icon say
/// something about the component in front of you rather than about its class.
///
/// <para>Modelica writes <c>%name</c> for the component's own name, <c>%class</c> for its class,
/// <c>%%</c> for a literal per cent, and <c>%</c> followed by an identifier for the value that
/// variable was given — so an inertia labelled <c>J=%J</c> reads <c>J=1</c> in a model that declared
/// <c>Inertia inertia1(J=1)</c>. Substituting the name and not the values, which is where this
/// started, gives a diagram whose every parameter reads <c>%J</c>: legible, and silent about the
/// model in front of you.</para>
///
/// <para><b>An unknown name is left as it was written.</b> Blanking it would say the parameter has no
/// value, and a reader cannot tell that from a tool that failed to find one; the literal
/// <c>%J</c> at least says which parameter is meant.</para>
/// </summary>
public static class IconText
{
    /// <summary>
    /// <paramref name="text"/> with its substitutions resolved.
    /// </summary>
    /// <param name="componentName">What <c>%name</c> stands for.</param>
    /// <param name="className">What <c>%class</c> stands for, or null to leave it as written.</param>
    /// <param name="valueOf">The value a variable was given, or null when it is not known.</param>
    public static string Resolve(
        string text, string? componentName, string? className = null, Func<string, string?>? valueOf = null)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (!text.Contains('%', StringComparison.Ordinal))
            return text;

        var result = new StringBuilder(text.Length);

        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '%')
            {
                result.Append(text[i]);
                continue;
            }

            if (i + 1 < text.Length && text[i + 1] == '%')
            {
                result.Append('%');
                i++;
                continue;
            }

            var start = i + 1;
            var end = start;
            while (end < text.Length && (char.IsLetterOrDigit(text[end]) || text[end] == '_'))
                end++;

            if (end == start)
            {
                result.Append('%');          // a lone per cent, which is not a substitution
                continue;
            }

            var name = text[start..end];
            var value = name switch
            {
                "name" => componentName,
                "class" => className,
                _ => valueOf?.Invoke(name),
            };

            result.Append(value is null ? text[i..end] : Display(value));
            i = end - 1;
        }

        return result.ToString();
    }

    /// <summary>
    /// A value as a label shows it, which for a <b>qualified name</b> is its last segment.
    ///
    /// <para>The spec says a substituted value is displayed as the parameter dialog displays it, and
    /// the case that makes the difference is an enumeration: MSL's PID example sets
    /// <c>controllerType=Modelica.Blocks.Types.SimpleController.PI</c> and the block is labelled
    /// <b>PI</b>, not with forty characters of package path across the diagram. A constant referred
    /// to by its full name reads the same way.</para>
    ///
    /// <para>Only a name, and only one that is entirely dotted identifiers — an expression, a
    /// literal, an array or a call is shown as written.</para>
    /// </summary>
    private static string Display(string value)
    {
        var trimmed = value.Trim();
        var dot = trimmed.LastIndexOf('.');
        if (dot <= 0 || dot == trimmed.Length - 1)
            return trimmed;

        foreach (var c in trimmed)
            if (!char.IsLetterOrDigit(c) && c != '_' && c != '.')
                return trimmed;

        return char.IsDigit(trimmed[0]) ? trimmed : trimmed[(dot + 1)..];
    }
}
