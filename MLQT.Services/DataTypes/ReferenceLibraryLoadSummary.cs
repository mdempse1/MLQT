namespace MLQT.Services.DataTypes;

/// <summary>What <see cref="Interfaces.IReferenceLibraryLoader.LoadAsync"/> did.</summary>
public sealed class ReferenceLibraryLoadSummary
{
    /// <summary>Libraries loaded by this call.</summary>
    public int Loaded { get; init; }

    /// <summary>Classes those libraries contributed.</summary>
    public int Classes { get; init; }

    /// <summary>Libraries found and not loaded, because they or another copy were loaded already.</summary>
    public int Skipped { get; init; }

    /// <summary>Configured folders that do not exist, for the caller to tell the user about.</summary>
    public IReadOnlyList<string> MissingFolders { get; init; } = [];
}
