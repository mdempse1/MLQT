namespace MLQT.McpServer.Helpers;

/// <summary>Which copy of a library a search takes, where it was, by which rule, and every copy seen.</summary>
internal sealed record LibraryPick(LibraryCandidate? Chosen, string? FoundBy, string? Rule, IReadOnlyList<LibraryCandidate> Seen);

/// <summary>
/// The one rule for choosing among copies of a library found along a list of folders, used by the
/// dependency search and by a startup script's <c>openModelFile</c> and <c>import</c> by name
/// (<c>.claude/skills/mcp-library-loading/SKILL.md</c>, *Which version is taken*). Folders count in the order given.
/// </summary>
/// <remarks>
/// With a version asked for: the first folder holding that release wins, and the largest build of it;
/// failing that, the first folder holding a copy whose <c>conversion(noneFromVersion=...)</c> names it,
/// and the newest such; failing that, nothing. With none asked for: the first folder holding any copy,
/// preferring one whose name carries no version, else the newest.
/// </remarks>
internal static class LibraryChoice
{
    public static LibraryPick Choose(string name, ModelicaVersion? wanted, IEnumerable<(string Folder, string FoundBy)> folders)
    {
        var byFolder = new List<(string FoundBy, IReadOnlyList<LibraryCandidate> Candidates)>();
        var seenFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (folder, foundBy) in folders)
        {
            if (string.IsNullOrWhiteSpace(folder) || !seenFolders.Add(Path.GetFullPath(folder)))
                continue;
            byFolder.Add((foundBy, LibraryCandidates.Find(folder, name)));
        }
        var seen = byFolder.SelectMany(f => f.Candidates).ToList();

        if (wanted is null)
        {
            foreach (var (foundBy, candidates) in byFolder.Where(f => f.Candidates.Count > 0))
            {
                var unversioned = candidates.FirstOrDefault(c => IsUnversioned(c.Path, name));
                return new LibraryPick(unversioned ?? candidates.OrderByDescending(c => c.Version).First(), foundBy,
                    unversioned is not null ? "no version asked for" : "no version asked for; the newest", seen);
            }
            return new LibraryPick(null, null, null, seen);
        }

        foreach (var (foundBy, candidates) in byFolder)
        {
            var exact = candidates.Where(c => c.Version is not null && c.Version.SameReleaseAs(wanted))
                .OrderByDescending(c => c.Version).FirstOrDefault();
            if (exact is not null)
                return new LibraryPick(exact, foundBy, "the version asked for", seen);
        }
        foreach (var (foundBy, candidates) in byFolder)
        {
            var compatible = candidates.Where(c => c.NoneFromVersions.Any(v => v.SameReleaseAs(wanted)))
                .OrderByDescending(c => c.Version).FirstOrDefault();
            if (compatible is not null)
                return new LibraryPick(compatible, foundBy,
                    $"version {compatible.Version?.Text ?? "?"}, which declares conversion(noneFromVersion=\"{wanted.Text}\")", seen);
        }
        return new LibraryPick(null, null, null, seen);
    }

    private static bool IsUnversioned(string path, string name) =>
        Path.GetFileName(path.TrimEnd('/', '\\')) is var leaf && (leaf == name || leaf == name + ".mo");
}
