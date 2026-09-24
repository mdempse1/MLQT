namespace OpenModelicaInterface.Interfaces;

/// <summary>
/// The omc session as its callers use it: load a file, check a class, read and drain the error
/// string, clear the session.
/// </summary>
/// <remarks>
/// <para><b>This exists so the checking service can be tested without omc.</b>
/// <see cref="IOpenModelicaInterfaceFactory.GetOrCreateAsync"/> used to hand back the concrete
/// <see cref="OpenModelicaInterface"/>, whose methods all end in a ZeroMQ round trip to a running
/// compiler — so every path in <c>OpenModelicaCheckingService</c> past the first call needed a live
/// tool, and no automated run has one. Mutation testing measured the result: of the mutants in that
/// service, 177 were not covered by any test at all (B229).</para>
///
/// <para>It carries <b>only what the service calls</b>. It is not a description of omc, and a
/// member belongs here when a caller outside <c>OpenModelicaInterface</c> needs it, not when the
/// session happens to offer it.</para>
/// </remarks>
public interface IOpenModelicaInterface
{
    /// <summary>Loads a <c>.mo</c> file into the session.</summary>
    /// <exception cref="TimeoutException">omc did not answer within the session's time limit, and the
    /// session has been closed.</exception>
    Task<bool> LoadFileAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>Runs <c>checkModel</c> on a class. True means it checked, not that it was silent.</summary>
    /// <param name="cancellationToken">Gives up on the check; once it has been sent, that closes the
    /// session, because omc cannot be interrupted and its socket cannot be reused.</param>
    /// <exception cref="TimeoutException">omc did not answer within the session's time limit, and the
    /// session has been closed.</exception>
    Task<bool> CheckModelAsync(string modelName, CancellationToken cancellationToken = default);

    /// <summary>
    /// The accumulated messages — and <b>empties the buffer</b>, so a caller that wants them twice
    /// has to keep the first answer.
    /// </summary>
    Task<string> GetErrorStringAsync();

    /// <summary>Clears the session's loaded classes.</summary>
    Task<bool> ClearAsync();
}
