namespace MLQT.Services.DataTypes;

/// <summary>
/// Represents a loaded Modelica library with its metadata and graph data.
/// </summary>
public class LoadedLibrary
{
    /// <summary>
    /// Unique identifier for this library instance.
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Display name of the library (typically the top-level package name).
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Source path or identifier (file path, directory path, or revision control URL).
    /// </summary>
    public string SourcePath { get; set; } = "";

    /// <summary>
    /// Type of source (File, Directory, Zip, Git, SVN).
    /// </summary>
    public LibrarySourceType SourceType { get; set; }

    /// <summary>
    /// Revision identifier for version-controlled libraries.
    /// </summary>
    public string? Revision { get; set; }

    /// <summary>
    /// Set of model IDs that belong to this library.
    /// The actual ModelNode objects are stored in the CombinedGraph.
    /// </summary>
    public HashSet<string> ModelIds { get; set; } = new();

    /// <summary>
    /// Dictionary tracking parent-child relationships for models in this library.
    /// Key is parent model ID, value is list of child model IDs.
    /// </summary>
    public Dictionary<string, List<string>> ChildrenByParent { get; set; } = new();

    /// <summary>
    /// List of top-level model IDs (models without parents).
    /// </summary>
    public List<string> TopLevelModelIds { get; set; } = new();

    /// <summary>
    /// ID of the repository this library belongs to, if any.
    /// Null for libraries loaded directly (not from a repository).
    /// </summary>
    public string? RepositoryId { get; set; }

    /// <summary>
    /// Relative path within the repository where this library is located.
    /// Empty string if library is at repository root.
    /// </summary>
    public string? RelativePathInRepository { get; set; }

    /// <summary>
    /// Loaded only so that references out of the user's own code resolve — a tool's installed library
    /// folder, listed under <b>Settings → Reference Libraries</b>. Never checked, measured, formatted
    /// or written to.
    ///
    /// <para>A fact about the <em>library</em>, and there was nowhere to record one. MLQT had two
    /// other ways of saying "not the user's code" — a repository marked <c>IsReferenceOnly</c>, and
    /// <see cref="ModelicaGraph.DataTypes.ModelNode.IsExternalStub"/> for a class rebuilt from a
    /// vendor's documentation — and a <b>readable</b> library from the reference folder is neither,
    /// because it has no repository at all. The reference folder holds readable libraries by design
    /// (Dymola's <c>Modelica\Library</c>, the example the settings page gives, ships MSL as source),
    /// so the loader knew and threw the knowledge away, leaving every consumer to re-derive it and
    /// the Metrics tab to count a vendor's library as the user's own.</para>
    ///
    /// <para>Ask <c>ReferenceOnlyScope</c> rather than this flag directly: it is one of three answers
    /// to the same question and the only one that covers all of them.</para>
    /// </summary>
    public bool IsReferenceOnly { get; set; }

    /// <summary>
    /// For an encrypted library, how many classes the vendor's documentation described — whether or
    /// not they became nodes. Null for a library loaded from source.
    ///
    /// <para>It is what separates "this library ships nothing we can read" from "we already have all
    /// of it from source". Both leave <see cref="ModelIds"/> empty, and only the first is worth
    /// telling anyone about.</para>
    /// </summary>
    public int? DocumentedClassCount { get; set; }
}
