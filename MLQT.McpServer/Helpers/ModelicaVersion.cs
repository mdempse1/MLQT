namespace MLQT.McpServer.Helpers;

/// <summary>
/// A library version as Modelica writes it (MLS §18.8.1): dot-separated release numbers, an optional
/// pre-release after <c>-</c> or a space, and an optional build after <c>+</c>, as in
/// <c>4.1.0+maint.om</c> or <c>2025.1</c>. Compares the way the specification orders releases: by the
/// numbers, a pre-release before its release, and - between copies of one release - a build after none.
/// </summary>
internal sealed record ModelicaVersion(IReadOnlyList<int> Numbers, string? PreRelease, string? Build, string Text)
    : IComparable<ModelicaVersion>
{
    public static ModelicaVersion? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var t = text.Trim();
        string? build = null;
        var plus = t.IndexOf('+');
        if (plus >= 0)
        {
            build = t[(plus + 1)..];
            t = t[..plus];
        }
        string? pre = null;
        var dash = t.IndexOfAny(['-', ' ']);
        if (dash >= 0)
        {
            pre = t[(dash + 1)..].Trim();
            t = t[..dash];
        }
        var numbers = new List<int>();
        foreach (var part in t.Split('.'))
        {
            if (!int.TryParse(part, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var n))
                return null;
            numbers.Add(n);
        }
        return numbers.Count == 0 ? null : new ModelicaVersion(numbers, string.IsNullOrEmpty(pre) ? null : pre, build, text.Trim());
    }

    /// <summary>Whether two versions are the same release: the numbers and pre-release, whatever the build.
    /// Trailing zeros do not count, so <c>4.1</c> is <c>4.1.0</c>.</summary>
    public bool SameReleaseAs(ModelicaVersion other) =>
        CompareNumbers(Numbers, other.Numbers) == 0 && string.Equals(PreRelease, other.PreRelease, StringComparison.Ordinal);

    public int CompareTo(ModelicaVersion? other)
    {
        if (other is null)
            return 1;
        var byNumbers = CompareNumbers(Numbers, other.Numbers);
        if (byNumbers != 0)
            return byNumbers;
        // A pre-release comes before its release.
        if ((PreRelease is null) != (other.PreRelease is null))
            return PreRelease is null ? 1 : -1;
        var byPre = string.CompareOrdinal(PreRelease, other.PreRelease);
        if (byPre != 0)
            return byPre;
        // A build of a release comes after the release itself.
        if ((Build is null) != (other.Build is null))
            return Build is null ? -1 : 1;
        return string.CompareOrdinal(Build, other.Build);
    }

    private static int CompareNumbers(IReadOnlyList<int> a, IReadOnlyList<int> b)
    {
        for (var i = 0; i < Math.Max(a.Count, b.Count); i++)
        {
            var x = i < a.Count ? a[i] : 0;
            var y = i < b.Count ? b[i] : 0;
            if (x != y)
                return x.CompareTo(y);
        }
        return 0;
    }

    public override string ToString() => Text;
}
