namespace MLQT.Services.DataTypes;

/// <summary>
/// Summary of pending changes across all monitored repositories.
/// </summary>
public class PendingChangesSummary
{
    public int AddedFiles { get; set; }
    public int ModifiedFiles { get; set; }
    public int DeletedFiles { get; set; }
    public int RenamedFiles { get; set; }
    public int AddedDirectories { get; set; }
    public int DeletedDirectories { get; set; }

    public int TotalChanges => AddedFiles + ModifiedFiles + DeletedFiles +
                               RenamedFiles + AddedDirectories + DeletedDirectories;

    public bool HasChanges => TotalChanges > 0;
}
