using MLQT.McpServer.Helpers;

namespace MLQT.McpServer.Services;

/// <summary>
/// The instructions returned in the MCP initialize response — the one thing an agent is told about
/// this server before it chooses a tool.
/// </summary>
/// <remarks>
/// <para><b>They must fit in <see cref="Budget"/> characters.</b> Claude Code keeps about the first
/// 2,048 characters of a server's instructions and drops the rest: the 5,000-character version
/// these replaced was cut off in the middle of its loading section, so every agent using it saw how
/// to load a library and nothing about reading, editing or the guidance tool. A test holds the
/// length. What does not fit belongs in <c>mlqt_get_guidance</c>, not here.</para>
/// <para><b>What they are for, in order.</b> When to reach for these tools at all, because an
/// agent's own file tools are always available and win by default; how to work beside a simulator's
/// MCP server, whose tool names overlap these (which is why every tool here is <c>mlqt_</c>
/// prefixed); then the minimum to start.</para>
/// </remarks>
internal static class ServerInstructions
{
    /// <summary>The most characters <see cref="Text"/> may run to. Under the 2,048 a client is known to keep.</summary>
    internal const int Budget = 2000;

    /// <summary>
    /// The instructions as sent: each paragraph on one line (see <see cref="Prose.Unwrap"/>). The
    /// source wraps them to fit the editor, and a raw string carries the file's line endings, so sent
    /// as written they would be CRLF on a Windows checkout and broken mid-sentence on every one.
    /// </summary>
    internal static readonly string Text = Prose.Unwrap(Raw);

    private const string Raw =
        """
        MLQT reads, writes, refactors, checks and formats Modelica (.mo) source on disk. Whenever you
        read, create or edit Modelica code, use these mlqt_ tools rather than file read/edit/write or
        shell tools: every write is parse-checked (nothing is written if it would not parse), a rename
        or move rewrites every reference, the file keeps its encoding and line endings, read-only
        libraries are refused, and the dependency graph stays current.

        Beside a simulator's server (OpenModelica, Dymola): MLQT owns the source files; the simulator
        compiles, checks equations and simulates. Write code with MLQT, then load or reload that FILE
        in the simulator before checking or simulating it - never author code through the
        simulator's load_string. mlqt_load_library loads from a path into MLQT only; a simulator's
        load_library is separate. mlqt_check_class is a style/spelling check, not a compile check.
        After a file changes outside MLQT, call mlqt_reload.

        Start: mlqt_load_library (a library directory, its package.mo, or one .mo file) or
        mlqt_load_repository (a Git/SVN working copy), then load its dependencies too - the load
        summary lists them, usually the MSL - or types will not resolve. Class ids are full dotted
        names (Modelica.Blocks.Continuous.Integrator); find one with mlqt_search_classes. Read with
        mlqt_get_class_interface (how to use a class) or mlqt_get_class_source; edit with
        mlqt_update_class_source, mlqt_create_class or the surgical tools (mlqt_add_component,
        mlqt_add_connection, mlqt_batch_edit ...). New project: mlqt_create_library.

        Call mlqt_get_guidance for recipes. Topics: overview, workflows, simulators, views, editing,
        diagrams, dependencies, style, spelling, formatting, vcs, resources.
        """;
}
