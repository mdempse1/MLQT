---
name: mcp-library-loading
description: "How the MCP server's mlqt_load finds dependencies and which version it takes, reading a Dymola startup script, and what the B503 measurements taught about getting agents to use the MCP tools (instruction wording, descriptions, fresh servers for runs). Use when changing DependencyLoader, LibraryChoice, ToolLibraryFolders, StartupScriptReader, the server instructions, a tool description or the guidance topics."
---

# MCP server: loading libraries, and getting agents to use the tools

Load this when changing how `mlqt_load` finds libraries (`DependencyLoader`, `LibraryChoice`,
`ToolLibraryFolders`, `StartupScriptReader`), or the text an agent reads before choosing a tool (the
server instructions, a tool's description, the guidance topics). It carries what the tool
consolidation (B503, 66 tools to 16, shipped 2026-10) settled and measured; the plan itself was retired
when it shipped, and its history has the full record.

## Finding dependencies

`mlqt_load` loads a library's dependencies itself, so an agent does not have to find the MSL. Without
them, types do not resolve, and in the 2026-10-18 sessions, before it did, agents read the MSL straight
from the install folders instead.

**What the tools do** (from the Modelica specification 3.6, the Dymola 2026x Refresh 1 manuals and the
OpenModelica 1.27 User's Guide):

- **The specification** (§13.3) searches MODELICAPATH "in the specified order" and leaves what
  MODELICAPATH contains to the tool. It allows a version to guide the search. It names four forms a
  library may be stored in: `Name.mo`, `Name Version.mo`, `Name/package.mo` and `Name Version/package.mo`.
  A larger `versionBuild` is a later maintenance release (§18.8).
- **Dymola** searches MODELICAPATH, and puts `<install>/Modelica/Library` first in it. It searches the
  directories enclosing a loaded package **after** MODELICAPATH by default, or before it with
  `Advanced.Modelica.EnclosingDirectoryModelicaPath = 1`. The first library loaded fixes the MSL
  version for the session.
- **OpenModelica** searches its library path: `%AppData%\.openmodelica\libraries` on Windows,
  `~/.openmodelica/libraries` on Linux, or `OPENMODELICALIBRARY` when that is set. It does not read
  the `MODELICAPATH` variable. It also looks beside a package opened from disk, at a point the guide
  does not document. A versioned `uses` loads only a folder whose name carries that version. If that
  version is missing, it takes the newest installed version whose `conversion(noneFromVersion=...)`
  covers it. Its libraries carry a build suffix, as in `Modelica 4.1.0+maint.om`.

**The order MLQT searches**, first match wins:

1. **Libraries already loaded.** A dependency already in the session is used as it is if it was
   loaded explicitly (see below), whatever its version. One found by an earlier search is used if its
   version satisfies `uses`. A different searched version already loaded is reported as a conflict,
   and nothing is loaded over it, as Dymola refuses to mix MSL versions.
2. **Sibling directories:** the directory holding the library being loaded, then the root of its
   repository. This is Dymola's flag-1 order, not its default. It is chosen because a library
   checked out beside the one being worked on is the copy the project is developed against. Version
   matching keeps a stale sibling from winning: a sibling is taken only if its version satisfies
   `uses`.
3. **The simulation tool's library folders**, for the `tool` named. For Dymola that is
   `<install>/Modelica/Library`. For OpenModelica it is the user library path above. Which install is
   searched is decided in this order:
   1. `toolInstall` passed to `mlqt_load`: the install folder or executable. A machine often has
      many Dymola releases, and the one the user runs is not necessarily the newest.
   2. The tool paths in the desktop app's External Tools settings (`settings.json`), read and nothing
      else. The server keeps its own `mcp-settings.json` so that an agent does not inherit a project
      a person had open, and a tool's path is not a project.
   3. The newest install found by `DymolaSettings.FindInstalledDymola` or
      `OpenModelicaSettings.FindInstalledOmc`, which already exist.

   The answer always names the install it searched.
4. **`MODELICAPATH`**, in its order.

**Which version is taken.** In each place, all four storage forms are looked for. A version is read
from the folder or file name, then from the package's `version` annotation, and a build suffix is
ignored when comparing.

- An exact match is taken first. Among several, the one with the larger build is taken.
- Otherwise the newest version whose `conversion(noneFromVersion=...)` covers the required one is
  taken, as OpenModelica does, and the answer says so.
- Otherwise nothing is loaded. The answer lists every candidate seen and where.
- An unversioned copy is taken only when `uses` names no version.
- An encrypted library (`package.moe`, common in Dymola's library folder) loads read-only. One named
  beside the readable source of the same library is not another copy: the source is used, and the
  answer says so (B268).

Dependencies of dependencies are followed in the same way: the MSL's own `uses` names `Complex` and
`ModelicaServices`. **The answer names each dependency loaded, the version, where it was found and by
which rule.** It also names each dependency that was not found. A search an agent cannot see would be
no better than the guessing it replaces.

### When the simulator's session decides

Many Dymola users do not set MODELICAPATH to find the libraries they work with. Each project needs
different versions of them, so a **startup script** opens the right ones when Dymola starts: `.mos`
lines such as `openModel("C:/Libs/CoreLib 2026.1/package.mo")`. The Dymola MCP server can be told to
run the same script. For such a user **the libraries the simulator has loaded are the truth**, and no
search MLQT does will find them reliably. Its first search may even find another version first.

So a library can come into MLQT in two ways, and **MLQT records which way each one came**:

- **Explicitly:** a path the caller passed to `mlqt_load`. This wins. If the library is already
  loaded as a searched dependency, at another version or from another path, that copy is unloaded and
  the explicit one loaded in its place. The answer names both. Dependency analysis is re-run for the
  classes that used it. If an explicit copy is already loaded from another path, the call is refused
  unless `replace: true`. Two explicit copies disagreeing means the caller has to choose. A library
  the host **supplied from memory** counts as explicit, and is the one copy that cannot be loaded
  again, so it stays loaded until its replacement has loaded. **An encrypted build of the release it
  supplies never replaces it, even with `replace: true`.** The supplied copy is the vendor's own
  description of that release, and what MLQT rebuilds from the build's help pages only approximates
  it. So the build is reported in a warning, not refused, because a refusal would offer `replace`.
  Where either version is unknown, the supplied copy is kept, as `LibraryPrecedence` keeps it. A build
  of another release is what is installed, and `replace` may put it in the supplied copy's place.
- **By search:** found for a `uses`, by the order above. It never replaces anything.

Three rules keep this honest, each one learnt from a route that broke it:

- **A library is named by what its files declare** (`LibraryDiscovery.ReadLibraryName`): the
  outermost class of its `package.mo`, or an encrypted build's versioned folder name or
  `libraryinfo.mos`. Never by the folder name alone. Read from the folder, a folder named after a
  loaded library but holding another one was refused, or with `replace` it unloaded the loaded library.
  A copy in a folder with another name got past the check, and readable source then retired a supplied
  library without anyone asking.
- **A folder of libraries, or a working copy, is checked before it is loaded.** It is discovered
  first, and each library in it is held to the same rules as a path. Otherwise the library service's
  precedence rule decides alone. The installed build of another release retired a supplied library
  that way, and nothing said so.
- **`replaced` is the session compared before and after the call**, not a list each branch remembers
  to add to. A library can be retired by `LibraryPrecedence` inside the library service, which the
  loader never sees happen.

An explicit library whose version differs from what another library's `uses` asks for is kept, with a
warning in the answer: the simulator is using that version, so MLQT must use it too.

To support this, `mlqt_load` takes **a list of paths**. All of them are loaded explicitly first, and
only then is the search run, for what is still missing. One call reproduces a startup script's set
without the search getting in first. `mlqt_session status` shows each library's version, path and
whether it came explicitly or by search.

**Where the set comes from depends on the tool.**

- **OpenModelica can be asked.** `getLoadedLibraries()` lists each loaded library with its directory.
- **Dymola cannot.** It has no command that reports which libraries are loaded or where they came
  from. **The startup script is the record**, so `mlqt_load` takes `script`: the path of a `.mos` file,
  which it **reads but does not run**.

**What reading a script means.** The script is walked in order, keeping the three things Dymola keeps
as it runs one: the current directory, the ModelicaPath and the libraries opened. A function is
recognised bare (`openModel`) or fully qualified (`DymolaCommands.SimulatorAPI.openModel`). A Dymola
command written without parentheses, as `cd C:/Work`, is read too.

| Command | What reading it does |
|---|---|
| `openModel(path, mustRead, changeDirectory)` | Loads `path` explicitly, resolving a relative path against the current directory. Then, as Dymola does by default (`changeDirectory=true`), makes the file's folder the current directory |
| `openModelFile(modelName, path, version)` | With `path`, loads that file. Without it, finds `modelName` at `version` along the script's ModelicaPath, using the version rules above, and loads it explicitly |
| `DymolaCommands.System.AddModelicaPath(path)` | Appends the directory to the script's ModelicaPath. That path starts as Dymola's own: `<install>/Modelica/Library`, then `MODELICAPATH` |
| `cd(dir)` | Changes the current directory |
| `RunScript(script, silent, scriptDir)` | Reads that script too, in place, once only, so a script that runs itself does not loop |
| `import Name` | Loads the top-level library `Name` if it is not already loaded, as Dymola does in a script, and does nothing if it is. It is found along the script's ModelicaPath as it stands at that line, taking the first copy found, as Dymola's search does. `import A.B` loads `A`. It does not create an alias |
| `Utilities.setenv("MODELICAPATH", path)` | Sets the script's ModelicaPath to `path`, for any later line that opens a library by name |
| `Lib.F(args)`, a function of a library the script has opened | Follows the call; see below |

**Reading values.** A path argument is read when it is:
- a string literal;
- a string variable assigned earlier in the script, or in the function being followed;
- `Modelica.Utilities.System.getEnvironmentVariable(name)`, read from the server's environment, which
  is the user's on the same machine; or
- a `+` concatenation of these.

**A backslash in a string is a path separator.** Users write Windows paths as
`"C:\Libs\CoreLib\package.mo"`, and Dymola accepts them, although in Modelica a backslash starts an
escape. Only `\"` and `\\` are read as escapes. Any other backslash is kept, so a folder beginning
with `n` or `t` is not read as a control character.

**What the answer reports.** It lists every library loaded, with the script line that loaded it,
and the chain of calls when a function did. Every other line goes in one of two lists:
- **ignored**: lines known to load nothing, such as `Advanced.*` settings, a comment, or an
  expression whose value is not kept;
- **unresolved**: lines the reader could not work out, each with its line number, and nothing guessed.

The current directory starts as the script's own folder, unless the caller passes the directory
Dymola starts in.

**Following a function the script calls.** Some users keep the loading in a small library of their
own. The script opens it, then calls a function that opens the others, sometimes through further
functions of the same library. The reader follows such a call when the function belongs to a
library the script has already opened. It works out the function's algorithm by:
- binding each input to the value the call passes, or else to its default;
- following string and Boolean assignments;
- taking the `if` / `elseif` / `else` branch its condition picks. A condition may use Boolean
  inputs and variables, literals, `not`, `and` and `or`;
- following calls to other functions of the same library, to a depth of five.

Anything else stops that branch and is reported as unresolved, with the function and line: a loop, a
condition on a number, a value returned by a call. That covers a loader written as a list of choices,
which is what such libraries are, without building an interpreter. It is a separate step after the
plain reader.

**Getting the script.** The guidance tells the agent to ask the user for the startup script. If the
Dymola MCP server was given one, the agent uses that path. An agent cannot recover it from Dymola.

**The guidance gives this order.** It is in the `simulators` topic, and one sentence of the server
instructions points to it:

1. **Dymola:** ask the user for the startup script, and call `mlqt_load` with `script`.
   **OpenModelica:** call `getLoadedLibraries()`, and pass all the directories to `mlqt_load` in one
   call.
2. Leave `withDependencies` on, so the search fills in only what the script or the simulator did not
   load.
3. If the simulator's set changes, for example when the user switches to another project's script,
   load again. The explicit libraries replace what was there.
4. With no script and no running simulator, fall back to the search alone, and say in the answer
   which versions it found. The user can then correct them.

## What gets an agent to use these tools

Measured with `build/mcp-eval.py` and the two real-world scenarios in `TestFixtures/McpEval/scenarios/`
(their files hold every run). An agent's own file tools are always there and win by default, so what
the server says is the whole of the argument:

- **Name the work, not the server's strengths.** The opening sentence of the instructions that won says
  when: "whenever you read, search, create or edit Modelica code". A version that led with searching
  moved the search tasks to `mlqt_find` and lost the editing: two Dymola runs made 2 and 0 MLQT calls.
- **With deferred tools, name one tool to load for each kind of work.** Claude Code defers MCP tools
  until searched for, and an agent searches for the names the instructions give it: `mlqt_load,
  mlqt_find, mlqt_get_class, mlqt_edit_class`. Cutting the list of `mlqt_edit_class` operations from
  the instructions raised the add-and-connect task from 4 to 8.2 calls; it was put back.
- **A tool that does a job the agent would do with the shell says so in its description.** On the 16
  tools, the Dymola scenario read classes with `cat` and `grep` five times in each of three runs, where
  the 66 had read none or one. The difference was one sentence: the old source tool's description said
  "use this rather than reading the .mo file", and the merged read tool's did not. With it ("use this
  rather than cat, grep or reading the .mo files, however small the library"), the next two runs read 1
  and 0 times, and asked for seven classes in one call.
- **Say what a write did beside the file it wrote.** `mlqt_write_class` listed a new class in
  `package.order` without saying so, and the agent added it again with `sed`; the answer now names the
  `package.order`.
- **Marking tools to load at once (`alwaysLoad`) was rejected**: it costs every session of every
  project, Modelica or not.
- **A run is only comparable on a fresh server.** The server outlives `/clear` and a new session, so a
  run on a reused one started with the last run's libraries loaded (B505). Quit the client between runs;
  `analyse` warns when the server log has calls from before the session.
- **A scenario's outcome varies with the prompt, its tool counts do not.** The Dymola prompt left open
  where the extra energy comes from, and runs split on it: some asked, some raised a limit the model
  switches off. Time, tokens and pass rate followed that path. Shell reads and writes of Modelica files
  did not, and they are what a comparison of tool sets reads.
