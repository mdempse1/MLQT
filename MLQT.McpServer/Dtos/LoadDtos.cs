namespace MLQT.McpServer.Dtos;

/// <summary>A library <c>mlqt_load</c> has loaded or found loaded: how it came in (<c>Origin</c>:
/// explicit or search), where it was found (<c>FoundBy</c>), by which version rule, and for which
/// library's <c>uses</c>.</summary>
public sealed record LoadedLibraryItem(
    string Name, string? Version, string Path, string Origin, string FoundBy, string? Rule, string? RequiredBy,
    bool Encrypted);

/// <summary>A library an explicit load replaced: the copy that was there, and the one in its place.</summary>
public sealed record ReplacedLibraryItem(string Name, string? OldVersion, string OldPath, string? NewVersion, string NewPath);

/// <summary>A copy of a library seen during the search, whether or not it was taken.</summary>
public sealed record CandidateItem(string Path, string? Version);

/// <summary>A dependency the search could not satisfy, with every copy of it the search saw.</summary>
public sealed record MissingDependency(string Name, string? Version, string RequiredBy, IReadOnlyList<CandidateItem> Candidates);

/// <summary>
/// What <c>mlqt_load</c> did: the libraries it loaded, in order, and those already loaded that a
/// dependency was satisfied by; what an explicit load replaced; what it could not find; and the tool
/// folders it searched.
/// </summary>
public sealed record LoadResult(
    IReadOnlyList<LoadedLibraryItem> Loaded,
    IReadOnlyList<ReplacedLibraryItem> Replaced,
    IReadOnlyList<MissingDependency> NotFound,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors,
    string? ToolInstall,
    IReadOnlyList<string> ToolFolders,
    ScriptReadingResult? Script = null);

/// <summary>A library a startup script opens: its path, the script line, and how (with the nested script
/// and line when <c>RunScript</c> did it).</summary>
public sealed record ScriptLoadItem(string Path, int Line, string Via);

/// <summary>A startup-script line that loads nothing, or that could not be worked out, and why.</summary>
public sealed record ScriptLineItem(string File, int Line, string Text, string Reason);

/// <summary>What reading a startup script found: the libraries it opens, in order; the lines that load
/// nothing; the lines that could not be worked out; and where it leaves the current directory and the
/// ModelicaPath.</summary>
public sealed record ScriptReadingResult(
    string Script,
    IReadOnlyList<ScriptLoadItem> Loads,
    IReadOnlyList<ScriptLineItem> Ignored,
    IReadOnlyList<ScriptLineItem> Unresolved,
    string CurrentDirectory,
    string? ModelicaPathSet,
    IReadOnlyList<string> ModelicaPathAdded);
