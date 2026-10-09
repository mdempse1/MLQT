namespace ModelicaGraph;

/// <summary>
/// One file's worth of supplied Modelica: a class with the classes nested in it, and the
/// <c>within</c> clause placing it, exactly as it would be written to a <c>.mo</c> file.
/// </summary>
/// <param name="RelativePath">Where the text would sit in the library, such as
/// <c>Lib/Sub/package.mo</c>. Names the class's file node under
/// <see cref="ReadOnlySources.InMemoryRoot"/>; it is never a path on disk.</param>
/// <param name="Text">The Modelica text.</param>
public sealed record SuppliedText(string RelativePath, string Text);
