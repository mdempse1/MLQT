namespace DymolaInterface.Interfaces;

/// <summary>
/// The Dymola session as its callers use it: open a model, check a class, and read or clear the log.
/// </summary>
/// <remarks>
/// <para><b>This exists so the checking service can be tested without Dymola.</b>
/// <see cref="IDymolaInterfaceFactory.GetOrCreateAsync"/> used to hand back the concrete
/// <see cref="DymolaInterface"/>, whose methods all end in an HTTP JSON-RPC call to a running
/// Dymola — so every path in <c>DymolaCheckingService</c> past the first call needed a live tool,
/// and no automated run has one. Mutation testing measured the result: of the mutants in that
/// service, most were not covered by any test at all (B229).</para>
///
/// <para>It carries <b>only what the service calls</b>. It is not a description of Dymola, and a
/// member belongs here when a caller outside <c>DymolaInterface</c> needs it, not when the session
/// happens to offer it.</para>
/// </remarks>
public interface IDymolaInterface
{
    /// <summary>Opens a <c>.mo</c> file in the session.</summary>
    Task<bool> OpenModelAsync(string path, bool mustRead = true, bool changeDirectory = true);

    /// <summary>Runs <c>checkModel</c> on a class. True means it checked, not that it was silent.</summary>
    Task<bool> CheckModelAsync(string problem, bool simulate = false, bool constraint = false);

    /// <summary>Clears the session's loaded classes.</summary>
    Task<bool> ClearAsync(bool fast = false);

    /// <summary>Empties the log, so the next read belongs to the command that follows it.</summary>
    Task<bool> ClearLogAsync();

    /// <summary>The log for the commands since it was last cleared.</summary>
    Task<string> GetLastErrorAsync();
}
