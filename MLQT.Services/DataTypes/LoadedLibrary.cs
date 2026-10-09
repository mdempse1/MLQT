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
    /// Whether the library is one <c>.mo</c> file rather than a package directory.
    ///
    /// <para><b>Asked of the path, not the source type.</b> A library found in a repository has its
    /// source type overwritten with Git or SVN whatever shape it has on disk, so
    /// <see cref="LibrarySourceType.File"/> is only what a library opened on its own says (B306,
    /// B428). A path ending in <c>.mo</c> is a file unless a directory of that name exists.</para>
    /// </summary>
    public bool IsSingleFile =>
        SourceType == LibrarySourceType.File
        || (SourcePath.EndsWith(".mo", StringComparison.OrdinalIgnoreCase) && !Directory.Exists(SourcePath));

    /// <summary>
    /// The directory the library's <c>modelica://</c> URIs and relative paths resolve against: the
    /// directory holding the file for a <see cref="IsSingleFile">single-file</see> library,
    /// <see cref="SourcePath"/> itself for every other. The one answer to that question — resource
    /// analysis, icon bitmaps and exported finding paths all ask it here (B428).
    /// </summary>
    public string RootDirectory =>
        IsSingleFile ? Path.GetDirectoryName(SourcePath) ?? SourcePath : SourcePath;

    /// <summary>
    /// The library's version: what its read-only source says, or the <c>version</c> annotation of its
    /// top-level package. Null when neither says.
    /// </summary>
    public string? Version { get; set; }

    /// <summary>
    /// What kind of read-only library this is, or null for one loaded from source. Set by the loader
    /// of an <see cref="ModelicaGraph.IReadOnlyClassSource"/>; every class it supplies is a
    /// <see cref="ModelicaGraph.DataTypes.ModelNode.IsExternalStub"/> of that kind
    /// (<see cref="ModelicaGraph.ReadOnlySources.KindOf"/>).
    ///
    /// <para>An <see cref="LibrarySourceType.EncryptedDirectory"/> library is recovered from
    /// documentation whether or not anything set this — there is no other way to read one — so a
    /// library described only by its source type still answers <see cref="IsReadOnly"/> truthfully.</para>
    /// </summary>
    public ModelicaGraph.ReadOnlySourceKind? ReadOnlySource
    {
        get => _readOnlySource
               ?? (SourceType == LibrarySourceType.EncryptedDirectory
                   ? ModelicaGraph.ReadOnlySourceKind.RecoveredFromDocumentation
                   : null);
        set => _readOnlySource = value;
    }

    private ModelicaGraph.ReadOnlySourceKind? _readOnlySource;

    /// <summary>
    /// Whether the library is never formatted, written, checked or spelled — the vendor's library
    /// rebuilt from its documentation, or one a host supplied from memory. <b>Ask this</b>, not
    /// <see cref="SourceType"/>, wherever the question is "may this be touched?": the source type
    /// says where a library came from, and more than one place is read-only.
    /// </summary>
    public bool IsReadOnly => ReadOnlySource is not null;

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
    /// For a library recovered from its documentation, how many classes the vendor's documentation
    /// described — whether or not they became nodes. Null for every other library.
    ///
    /// <para>It is what separates "this library ships nothing we can read" from "we already have all
    /// of it from source". Both leave <see cref="ModelIds"/> empty, and only the first is worth
    /// telling anyone about.</para>
    /// </summary>
    public int? DocumentedClassCount { get; set; }

    /// <summary>
    /// For a read-only library that was not used because a copy of the same library that outranks it
    /// is loaded (<see cref="SourceSupersedesEncrypted"/>), where that copy is. Null for every library
    /// that is in use.
    ///
    /// <para>Such a library has an empty <see cref="ModelIds"/>, and so does one that ships no
    /// documentation — and the two need opposite messages: one is a vendor library MLQT cannot read,
    /// the other is working exactly as intended (B268). A caller reporting on a library it asked to
    /// load asks this before reading the empty index as a problem.</para>
    /// </summary>
    public string? SupersededBy { get; set; }
}
