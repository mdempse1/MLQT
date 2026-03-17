namespace RevisionControl;

public enum ConflictResolutionChoice
{
    AcceptIncoming,   // Use the incoming (source branch) version
    KeepMine,         // Keep the current working copy version
    MarkResolved      // Mark as resolved after manual editing
}
