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
        MLQT reads, searches, writes, refactors, checks and formats Modelica (.mo) source. Whenever you
        read, search, create or edit Modelica code - a question about a library included - use these
        mlqt_ tools rather than file tools, grep or the shell: every write is parse-checked (nothing is
        written if it would not parse), a rename or move rewrites every reference, files keep their
        encoding and line endings, and read-only libraries are refused. If mlqt_ tools are deferred,
        load them with a tool search before you first read or change a .mo file: mlqt_load,
        mlqt_find, mlqt_get_class_source, mlqt_edit_class.

        MLQT starts EMPTY and keeps its own session: every mlqt_ tool sees only libraries loaded
        into MLQT. Load with mlqt_load: the paths of every library the user's
        simulator has loaded (OpenModelica: getLoadedLibraries(); Dymola: pass the user's startup script as script),
        and tool 'dymola' or 'openmodelica'; it loads their dependencies too, the MSL included. A
        simulator's load_library does NOT load anything here. mlqt_load_repository loads a working copy.

        Beside a simulator's server (OpenModelica, Dymola): MLQT owns the source files; the simulator
        compiles, checks equations and simulates. Write code with MLQT, then load or reload that FILE
        in the simulator before checking or simulating it - never author code through the
        simulator's load_string. mlqt_check_class is a style/spelling check, not a compile check.
        After a file changes outside MLQT, call mlqt_reload.

        Class ids are full dotted names (Modelica.Blocks.Continuous.Integrator). Find a class, or a
        line of code, with mlqt_find. Read with mlqt_get_class_interface (how to use a class) or
        mlqt_get_class_source; change part of a class with mlqt_edit_class (replace,
        add_component, add_connection), a whole one with
        mlqt_create_class. New project: mlqt_create_library.

        Call mlqt_get_guidance for recipes. Topics: overview, workflows, simulators, views, editing,
        diagrams, dependencies, style, spelling, formatting, vcs, resources.
        """;
}
