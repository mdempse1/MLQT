# MCP tool consolidation — design note

**Status: steps 1 and 2 done; step 3's harness comparison done (2026-10-19, results under each); the scenarios on the consolidated set next.** This is the plan for
roadmap item **B503**: the MCP server's tool surface goes
from 66 tools to 16. It has two aims. The first is to close the gaps that send an agent round MLQT to
its own shell and file tools. The second is to merge the tools that overlap. Written 2026-10-18, from
the measurements below. Retire this note when the consolidated set ships: move what is durable into
`Documentation/mcp-server.md`, `MLQT.McpServer/README.md`, the server instructions and
`mlqt_get_guidance`.

**The decisions are MLQT's.** A host built on `AddMlqtMcpServer` gets the new names and adapts. A host
moves with the release, using the mapping table below, and no host's current use shapes the choices
here.

## Why

Anthropic's guidance on tools for agents says three things that bear on this:

- *Writing effective tools for agents*: "More tools don't always lead to better outcomes". Overlapping
  tools "distract agents from pursuing efficient strategies". Build fewer tools around workflows.
- The tool-search documentation: selection accuracy "degrades once you exceed 30–50 available tools".
  Definitions over about 10k tokens are worth deferring. MLQT's descriptions alone come to about 12.7k
  tokens.
- **Claude Code already defers MCP tools** (Tool Search, the default). So context size is not where
  66 tools hurt most here. The cost is choosing between near-duplicates, and the steps a workflow
  needs that no tool offers.

The measurements bear the last point out.

## What was measured

Two sources, both from `build/mcp-eval.py`, and both kept in `McpEvalRuns/` (git-ignored):

- **The harness baseline** (`20261017-151151-baseline`): 12 small tasks against `TestFixtures/McpEval`,
  3 runs each.
- **Four real model-building sessions** (2026-10-18), with Dymola or OpenModelica beside MLQT: a
  Formula 1 car (twice), a Stewart motion platform and a McPherson strut. They were read with
  `mcp-eval.py analyse`.

| Finding | Evidence |
|---|---|
| **The used core is small** | Baseline: 21 of 66 tools called. Real sessions: 7–22 per session. `load_*`, `create_class`, `get_class_source`, `get_class_interface` and `reload` carry almost all of it |
| **Editing existing code goes round MLQT** | At least 117 writes to `.mo` files in the four sessions went through Claude's own `Edit`, `sed -i` and Python find-and-replace, against 12 calls to `update_class_source`. `reload` followed, to bring MLQT back in step: 16 times in one session. The count is a floor, because a call's input is kept to 200 characters in the report |
| **The element-level edit tools are not chosen** | `add_component`, `add_connection`, `set_component_modifier` and `batch_edit` were called 0 times in four multi-hour sessions. In the baseline they were used only where a task named one exact change |
| **Code search goes round MLQT** | About 240 reads used grep, sed, cat or `Select-String`, aimed at `.mo` files, often MSL's own in the Dymola or OpenModelica install folder. They looked for where a name is used, a `Placement`, `connect` lines or parameter declarations. MLQT searches class names and documentation prose, but not code. Some sessions never loaded the MSL into MLQT at all |
| **Some work takes one call per class** | `create_class` was called 27–64 times per session. `format_class` was called 97 times in one session, 78 of them in a row. Before B503's first step, `validate_class_references` took 5–8 calls per task; that step is done (commit 492deb5), and halved it |
| **Some answers are far too big** | `get_resource_warnings` returned 51k characters, `load_repository` 31k, `get_package_tree` 30k and `list_libraries` 13k |
| **Tool selection is not failing** | 0–1 MLQT errors per session. 4–12 Tool Search calls per multi-hour session. The baseline passed 36 of 36 runs |
| **Overlapping tools go unused, or one is chosen and its siblings never are** | `get_class_source` was chosen over the five "views" for explaining a model (3 of 3 runs). `search_text` and `search_by_interface` were never called; Grep was used instead (3 of 3 runs on the search task) |

The conclusion: merging tools alone would change little, because tool selection is not what goes
wrong. The changes that matter are the missing capabilities: a text patch, code search, many classes
in one call, and bounded answers. Merging is what keeps the surface from growing as those arrive.

## Principles

1. **One tool per workflow step.** The steps are: load, find, read, write, edit, refactor, format,
   check, and follow dependencies. A tool takes a mode or a list of views rather than being cloned per
   variant.
2. **Accept the agent's own shape for edits.** Claude edits text by old → new replacement. The edit
   tool does the same inside one class, and adds what its own tool cannot: the parse check, the
   encoding and line endings, read-only refusal, and the graph kept in step.
3. **Batch by default.** Each tool that acts on a class takes a list of them, and each tool that
   acts on many takes a library.
4. **Bounded answers.** Every listing pages with `limit`/`offset`. Every large answer has a
   `response_format` of `concise` (the default) or `detailed`.
5. **No hidden preconditions.** Dependency analysis runs when a tool needs it
   (`EnsureDependenciesAnalyzedAsync` is already idempotent). An agent never has to call a tool first
   to stop another from failing.
6. **Say whether a tool writes.** Each tool carries the MCP annotations `readOnlyHint` and
   `destructiveHint`, so a client can ask permission only for writes. None of today's tools do. A
   tool that writes to disk has a `preview`.
7. **Descriptions under about 1,000 characters, with one example each.** Recipes go in
   `mlqt_get_guidance`, and the server instructions stay under their 2,000-character budget.

## The consolidated set

| # | Tool | Writes | Replaces | New behaviour |
|--:|---|---|---|---|
| 1 | `mlqt_load` | memory | `load_repository`, `load_library`, `discover_libraries` | Finds the kind from the path (working copy, directory of libraries, one library, one file). Takes **a list of paths**, all loaded explicitly, or a startup `script` to read (*When the simulator's session decides*). `discover_only` lists without loading. `with_dependencies` (default on) loads what the `uses` annotations name, searched as *Finding dependencies* below; `tool` (`dymola` / `openmodelica` / `none`) says whose library folders to search |
| 2 | `mlqt_session` | memory | `server_info`, `list_libraries`, `list_repositories`, `unload_library`, `reload` | `action`: `status` (the default) / `unload` / `reload`. `status` is concise: names, versions, counts and dependencies still missing |
| 3 | `mlqt_create_library` | disk | — | Unchanged |
| 4 | `mlqt_find` | no | `search_classes`, `search_text`, `search_by_interface`, `list_classes` | `match`: `name` / `text` / `code` / `interface`, with the filters each has today. **`code` is new**: it returns matching lines of class source as class id, line and snippet, the grep an agent runs today, but over every loaded class and the MSL |
| 5 | `mlqt_browse` | no | `get_package_tree` | `root`, `depth`, paged, concise by default |
| 6 | `mlqt_get_class` | no | `get_class_info`, `get_class_source`, `get_class_interface`, `list_class_elements`, `get_class_documentation`, `get_class_behavior`, `list_connections`, `get_diagram_layout`, `get_class_resources` | Takes **one or more** class ids, and `views` from `source`, `interface`, `elements`, `behavior`, `connections`, `documentation`, `layout`, `resources` and `info`. The default is `info` + `source`, which is what agents ask for. Each view's fields stay as they are |
| 7 | `mlqt_get_diagram_image` | no | — | Unchanged (it returns an image) |
| 8 | `mlqt_write_class` | disk | `create_class`, `update_class_source` | Takes **one or more** whole classes, each created in or replacing at its parent. Replacing needs `replace: true`, so a typo in a name cannot overwrite a class |
| 9 | `mlqt_edit_class` | disk | `batch_edit`, `add_component`, `remove_component`, `set_component_modifier`, `add_extends`, `add_import`, `add_equation`, `add_statement`, `add_connection`, `remove_connection`, `set_component_placement`, `set_class_description`, `set_component_description`, `set_class_documentation`, `suppress_rule`, `accept_spelling_in_class`, `correct_spelling` | A list of operations on one class, applied all together or not at all. **The new first operation is `replace`** {`old`, `new`}: text that must occur once in the class. The rest are today's `batch_edit` operations plus the four documentation and suppression ones. Every result is parse-checked before it is written |
| 10 | `mlqt_refactor_class` | disk | `rename_class`, `move_class`, `delete_class` | `action`: `rename` / `move` / `delete`. Each keeps today's reference rewriting and refusals |
| 11 | `mlqt_format` | disk unless `source` | `format_class`, `format_code` | Takes a class, **a library**, or source text (stateless) |
| 12 | `mlqt_check` | no | `check_class`, `check_library`, `check_style`, `spell_check`, `validate_class_references`, `get_resource_warnings`, `spelling_suggestions` | Takes a class, a library or source text, and `checks` from `style`, `spelling`, `references` and `resources`. Findings are paged and stored. A spelling finding carries its suggestions |
| 13 | `mlqt_list_findings` | no | — | Unchanged, concise by default |
| 14 | `mlqt_style_settings` | disk when `settings` is given | `get_style_settings`, `set_style_settings` | Reads, or writes when `settings` is passed |
| 15 | `mlqt_dependencies` | no | `analyze_dependencies`, `get_dependencies`, `find_usages`, `analyze_impact`, `analyze_change_impact`, `get_changed_classes`, `find_resource_usages` | `direction`: `uses` / `used_by`. `transitive`. The target is class ids, a working copy's changes (with an optional `revision`), or a resource path. It runs the analysis itself |
| 16 | `mlqt_get_guidance` | no | — | Topics rewritten for the new tools |

That is all 66 tools, with none left out. `mlqt_spelling_suggestions` and `mlqt_get_resource_warnings`
become parts of `mlqt_check`'s answer.

**Kept separate on purpose.** `mlqt_get_diagram_image` returns an image, which no other read does.
`mlqt_create_library` starts a project. `mlqt_list_findings` reads stored results without re-running
a check. Folding any of these into a neighbour would give that neighbour a second job with a
different answer shape.

## Finding dependencies

`mlqt_load` loads a library's dependencies itself, so an agent no longer has to find the MSL. Without
them, types do not resolve, and in the 2026-10-18 sessions agents read the MSL straight from the
install folders instead.

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
- An encrypted library (`package.moe`, common in Dymola's library folder) loads read-only, as it does
  today.

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
  unless `replace: true`. Two explicit copies disagreeing means the caller has to choose.
- **By search:** found for a `uses`, by the order above. It never replaces anything.

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
2. Leave `with_dependencies` on, so the search fills in only what the script or the simulator did not
   load.
3. If the simulator's set changes, for example when the user switches to another project's script,
   load again. The explicit libraries replace what was there.
4. With no script and no running simulator, fall back to the search alone, and say in the answer
   which versions it found. The user can then correct them.

## How it is built

**One implementation, two surfaces, for one release only.** The new tools are thin facades over the
code the current tools already call. Where that code is inside a tool method today, it moves into a
helper both can call; the repository's rule against two implementations of one behaviour applies.
`MlqtMcpServerOptions.Toolset` (`Current` / `Consolidated`), and the `MLQT_MCP_TOOLSET` environment
variable for MLQT's own executable, choose which set is registered. **This switch exists only to
measure the two sets against each other, and it is deleted at the cut-over** rather than kept as a
compatibility mode.

The tests follow the code. Each behaviour keeps the test it has, moved to call the helper or the new
tool, and each new tool gets a test per mode or view. `ToolCountTests`, `ToolNamingTests` (which fails
on any document naming a tool that does not exist) and `MlqtMcpServerSurfaceTests` change at the
cut-over, which is how every stale mention is found.

**Obfuscation.** The new parameter and result types follow `CODING_GUIDELINES.md` §Names That Must
Survive Obfuscation: names are explicit, and enum text is not read from metadata. The protected-build
comparison (`Design/release-checklist.md` §7) is re-run against the new set before release.

## Steps

Each step is measured before the next one starts. "Measured" means a harness run (`mcp-eval.py run
--repeats 3`) compared with the baseline, plus, from step 1 on, the two real-world scenarios of step 0 re-run
and read with `analyse --dedicated`.

### 0. Make the harness able to see the change

- Add tasks for the gaps:
  - patch part of an existing class (a parameter and an equation);
  - find every use of a name in code;
  - build a package of four or more classes;
  - format a whole library;
  - load a library together with its dependencies;
  - load the versions a simulator has loaded, when a search would find another version first. The
    fixture gets a small dependency in two versions, one beside `EvalLib` and one elsewhere, and the
    prompt says which one the simulator has loaded.
  Each new task must pass `check-tasks`.
- Fixtures for the script reader, in `TestFixtures/StartupScripts`. Done 2026-10-19.
  - Three real users' scripts, anonymised, plus one synthetic script for what they do not cover:
    `RunScript`, `AddModelicaPath` with `openModelFile` by name, relative paths and `import`.
  - Dummy libraries at the paths the scripts open.
  - The expected result of reading each script.
  - Absolute paths are written as `{root}`, which a test replaces with a temporary copy of the folder.
- Give the harness `--server-env KEY=VALUE` (passed into the MCP config's `env`), so a run can choose
  `MLQT_MCP_TOOLSET`.
- Write **two real-world scenarios** in `TestFixtures/McpEval/scenarios/`, one with Dymola and one
  with OpenModelica. Each is cut down from one of the 2026-10-18 sessions to 30–60 minutes of work.
  The full sessions took 1–27 hours and 32–329 million input tokens, too much to repeat at every
  comparison. Each scenario is run by hand and read with `analyse --dedicated`. Run each once on the
  current set to give the baseline that steps 1 and 3 compare against.
- Re-run the baseline with the new tasks, with Tool Search on and with it off
  (`--tool-search false`).

### 1. Close the gaps, adding to the current set

This step needs no switch, and it pays off on its own.

- Add `mlqt_edit_class`, with `replace` and the `batch_edit` operations. `batch_edit` stays until the
  cut-over.
- Add `match: code` to search, as a mode of a new `mlqt_find` that sits beside the search tools for
  now.
- Let `create_class` take several classes, and let `format_class` take a library.
- Make `get_resource_warnings`, `load_repository`, `get_package_tree` and `list_libraries` concise by
  default, and page them.
- Let `load_library` and `load_repository` load the dependencies their `uses` annotations name, in
  the order and by the version rules of *Finding dependencies*, with the `tool` parameter. Record
  whether each library came explicitly or by search, let an explicit load replace a searched one,
  take a list of paths, read a Dymola startup script, and update the `simulators` guidance to the
  order above.

**Exit criterion:** compared with the scenarios' step 0 runs, at least half fewer writes to `.mo` files
outside MLQT and at least half fewer `reload` calls. Fewer reads outside MLQT. No drop in harness pass
rate.

#### Step 1 results (2026-10-19) — met

Step 1 added five tools beside the 66, so these runs offered 71. `mlqt_load`, `mlqt_find`,
`mlqt_edit_class`, `mlqt_write_class` and `mlqt_format` are already the shape of members of the
consolidated set. Each scenario's runs, with every figure, are in its file under
`TestFixtures/McpEval/scenarios/`.

| Measure | Step 0 | After step 1 |
|---|---|---|
| Harness, 18 tasks × 3, Tool Search on: calls per run | 6.8 | 4.6 |
| …bypass calls, all runs | 51 | 25 |
| …cost per run | $0.171 | $0.158 |
| Harness, Tool Search off: calls per run | 6.1 | 4.0 |
| …bypass calls, all runs | 40 | 8 |
| OpenModelica axle: *Done when* | 8 of 8 | 8 of 8 |
| …MLQT calls / bypass writes / bypass reads / `reload` | 26 / 9 / 3 / 1 | 12 / 2 / 0 / 0 |
| …input tokens | 4.4 M | 2.5 M |
| Dymola energy recovery: *Done when* | 4 of 5 | 5 of 5 |
| …MLQT calls / bypass writes / bypass reads / `reload` | 2 / 10 / 7 / 0 | 27 / 1 / 1 / 1 |
| …minutes | 25 | 14 |

The Dymola column is the last of five runs, with every fix below in it; the OpenModelica one needed
none of them. Every harness run passed in both columns. The two bypass writes in the OpenModelica run
were to the agent's own scratch folder, not the library.

What the runs taught, in the order it was learned:

- **Deferred tools lose to the agent's own.** With Tool Search on, both search tasks used Grep in
  every run, because nothing told the agent `mlqt_find` existed until it searched for it. Marking
  `mlqt_find` and `mlqt_load` to load up front (`anthropic/alwaysLoad`) fixed that, and was reverted:
  it puts their definitions into every session the server is registered for, Modelica or not. The
  connect-time instructions are sent anyway, so they are where to steer.
- **The instructions must name the work, not the question.** An opening sentence leading with
  questions and searching moved the search tasks to `mlqt_find` and lost the Dymola scenario: two runs
  with 2 and 0 MLQT calls, and 19 and 13 library writes around it. The wording that won both names the
  work - "read, search, create or edit Modelica code" - keeps the reasons a write through MLQT is safe,
  and, for deferred tools, names one tool to load for each kind of work: `mlqt_load`, `mlqt_find`,
  `mlqt_get_class_source`, `mlqt_edit_class`. The first tool search then loaded MLQT's tools before
  any read. **The consolidated set's instructions must keep that shape under the new names.**
- **Listing an edit tool's operations matters.** With `mlqt_edit_class`'s operations cut from the
  instructions to save characters, `add-and-connect` searched for the separate tools and took 8.2
  calls instead of 4.
- **The newest install found need not be the one running (B504).** Detection did not find a beta's
  folder, so the dependency search used an older release's libraries. Fixed: every release folder is
  ranked, the answer says why that install was chosen, and a load naming another install searches
  again in place of what the first search found.
- **A repeated text needs `replace` with `all: true`.** The last `.mo` write round MLQT was a `sed`
  renaming one name throughout a class, which `replace` refused because it occurred twice.
- **Agents still pick the old single-purpose tools where they exist** - `set_component_modifier`,
  `set_class_description`, `rename_class`, `get_class_source`, `get_package_tree` - beside the new
  ones. Step 3 shows whether the consolidated set, which has only the new shapes, does better.
- **The tool count was mostly hidden** by Tool Search. Step 3 runs with it off as well, where the
  count costs most.

### 2. Build the consolidated set behind the switch

All 16 tools, the rewritten instructions and guidance, and the annotations. Both sets pass the full
test suite.

#### Step 2 results (2026-10-19) — built

`MlqtMcpServerOptions.Toolset` and `MLQT_MCP_TOOLSET=consolidated` serve the 16; the current set, the
default, is still the 71. Every tool of the assembly is registered and the set not chosen is removed
afterwards, because the two share tools - sometimes in one class with a tool only one set has.

- **Nine tools are in both sets**: the five step 1 built, and the four kept separate on purpose.
- **Seven are facades** - `mlqt_session`, `mlqt_browse`, `mlqt_style_settings`, `mlqt_get_class`,
  `mlqt_refactor_class`, `mlqt_check`, `mlqt_dependencies` - that call the current tools rather than
  repeat them, so the sets cannot drift while they are compared. Each view, check, action and mode is
  tested against the tool it replaces.
- **The instructions keep the shape step 1 found**: the work named, and one tool to load for each kind
  of it (`mlqt_load`, `mlqt_find`, `mlqt_get_class`, `mlqt_edit_class`). **The guidance** has the same
  12 topics, rewritten; a test fails if a topic or the instructions name a tool the set does not serve.
  `mlqt_get_guidance` picks its topics from the tools the server actually registered.
- **Annotations**: every one of the 16 says whether it is read-only, destructive and idempotent.
- **The harness** needs no second task list: each expected-tools group lists the consolidated tool beside
  the current ones.

Three things the table above did not say, found while building:
- **`mlqt_load` takes a working copy or a folder of libraries**, as the table has it replacing
  `load_repository` and `discover_libraries` (with `discoverOnly`). Without that no repository - so no
  style settings and no working-copy changes - could be reached in the consolidated set.
- **The facades that need the dependency analysis run it**: `mlqt_dependencies`, `mlqt_refactor_class`,
  and the resources view and check. The current tools' error points at `mlqt_analyze_dependencies`,
  which the consolidated set does not have.
- **`mlqt_edit_class` changes one class.** `batch_edit`'s operations across classes have no home in the
  16; an agent calls `mlqt_edit_class` once per class. Step 3 shows whether that costs calls.

### 3. Compare the two sets

Run the harness, with all tasks 3 times each, under both values of `MLQT_MCP_TOOLSET`, with Tool
Search on and off. Then re-run the two real-world scenarios on each set.

**Exit criterion for the consolidated set:**
- pass rate no lower;
- tool calls and tokens per task no higher;
- invalid-parameter errors no higher (the risk of a union-typed operation list);
- bypasses no higher.

If it fails on parameter errors, fix the descriptions and examples and run again before going back on
any merge.

#### Step 3 harness results (2026-10-19)

All 18 tasks 3 times each, both sets, Tool Search on and off: 216 runs, every one passed.

| Measure, per run | Current (71), TS on | Consolidated (16), TS on | Current, TS off | Consolidated, TS off |
|---|--:|--:|--:|--:|
| Pass rate | 100% | 100% | 100% | 100% |
| Tool calls | 4.9 | 4.3 | 4.0 | 3.6 |
| Tool errors | 0.02 | 0.00 | 0.00 | 0.00 |
| Bypass calls, all 54 runs | 11 | 9 | 4 | 6 |
| Input tokens | 205 k | 185 k | 386 k | 271 k |
| Cost | $0.166 | $0.157 | $0.256 | $0.199 |

**On the totals the consolidated set meets every criterion but one**: as many passes, fewer calls, fewer
tokens and no parameter errors in either mode. With Tool Search off it costs 30% fewer input tokens and
22% less, which is where the tool count was expected to tell. The exception is bypasses with Tool Search
off, 6 against 4.

**Per task it is not uniform.** The largest gains are `rename-class` (11.7 → 5.3 calls with Tool Search
on: one `mlqt_refactor_class`, which runs the analysis itself), `impact` (6.0 → 3.7), `build-model`
(6.7 → 4.0) and `build-package` (6.7 → 5.0). Six tasks take 0.3 to 1 call more, which with three
repeats is one or three calls in all:
- `read-interface` (+1, Tool Search on): the agent called `mlqt_load` and `mlqt_get_class` at once, so the
  read answered before the load had finished and was repeated.
- `describe-class`, `load-dependencies`, `create-block`: a call checking the work just done - a
  `get_class` after `edit_class`, an `mlqt_session` after `mlqt_load`, and for `create-block` a shell read
  of the new file and `package.order` (the bypasses, 0 → 1 and 0 → 0.3).
- `find-by-description` with Tool Search off: an `mlqt_browse` before the read.
- `rename-class` with Tool Search off: bypasses 1.3 → 1.7, the same `grep` for anything left that the
  current set's runs make.

**Found while reading the runs: messages name tools the consolidated set does not serve.** The error for
an unknown class says to find it with `mlqt_search_classes`; others say to run `mlqt_analyze_dependencies`
or load with `mlqt_load_library`. About a hundred string literals in the shared code do this, most in
`ToolDiagnostics`, `EditTools`, `EntityResolver` and `SessionTools`. No run failed on one, but each is a
pointer to nothing. **Fixed before the scenarios**, in two layers:
- where one wording is right in both sets, at the source: every hint to load or to search now names
  `mlqt_load` or `mlqt_find`, which both sets serve;
- for the rest, `ConsolidatedNames` rewrites each current-only tool in an answer to the consolidated
  tool that does its job - the plan's mapping table, held by a test to every current-only tool - while
  the consolidated set is served. It goes at the cut-over, with the messages it rewrites.

#### Step 3 scenario results (2026-10-19)

Each set's runs with the same fixes in (B504, the work-naming instructions); the full records are in the
scenario files.

| Run | *Done when* | MLQT calls | Bypass writes | Bypass reads | Input tokens |
|---|--:|--:|--:|--:|--:|
| OpenModelica, current (step 1) | 8 of 8 | 12 | 2, scratch only | 0 | 2.5 M |
| OpenModelica, consolidated | 8 of 8 | 19 | 0 | 0 | 2.8 M |
| Dymola, current (bb7d8c2) | 5 of 5 | 31 | 0 | 0 | 10.0 M |
| Dymola, current (e10d10a) | 5 of 5 | 27 | 1 | 1 | 7.8 M |
| Dymola, consolidated, steered once | 4 of 5 | 16 | 0 | 5 | 9.1 M |
| Dymola, consolidated, server reused | 5 of 5 | 18 | 7 | 5 | 19.3 M |
| Dymola, consolidated, clean (2026-10-20) | 5 of 5 | 15 | 1 | 5 | 7.8 M |

**The scenarios do not bear the harness out.** OpenModelica is even. In Dymola all three consolidated
runs read the classes the change runs through with the shell, 5 reads each against 0 and 1. Most of
those reads came before or instead of `mlqt_get_class`. The second run also patched the test it had just
created, and its own documentation, with Python and `sed`. The criterion - bypasses no higher - is not
met on this scenario.

The first two runs were not clean: one was steered by an early question, and the other inherited the
previous run's server. The third was clean, and it was the fastest and cheapest Dymola run on either
set, but it read the same way. In it the agent loaded with MLQT first, listed the library, said it was
small and read the files directly. So the reads are not about the state of the server, or about not
knowing MLQT is there.

The step 1 lesson points at a cause: the agent reaches for MLQT where the tool it needs is named for
the work. The current set names a tool for reading source, and its description says to use it
"rather than reading the .mo file", because a file may hold many classes and their annotations.
`mlqt_get_class` is one tool for every read, its default answer is info and source together, and its
description says neither. The one bypass write in the clean run was also the server's doing:
`write_class` registered the new class in `package.order` without saying so, and the agent added it a
second time.

### 4. Cut over

- Register only the consolidated set, and delete the switch and the old tool methods.
- Update `Documentation/mcp-server.md`, `MLQT.McpServer/README.md`, `ToolCountTests`'s stated count,
  and the harness tasks' `expected_tools`.
- The release notes carry the mapping table above, old name → new tool and arguments. Hosts built on
  `AddMlqtMcpServer` move with that release.

## Risks

| Risk | What guards against it |
|---|---|
| A list of operations of several shapes is harder to call correctly | Each operation's missing or unknown fields come back as an error naming the fields it expects. One example per operation is in the description. Step 3 measures invalid-parameter errors specifically |
| A `replace` matches more than once, or not at all | It must match exactly once within the class, as Claude's own `Edit` requires, and the error says how many matches there were. The patched class must parse before anything is written |
| `mlqt_get_class` with many classes and views returns a huge answer | Concise by default. A total character budget per call, with a note naming what was left out and how to ask for it |
| Fewer, wider tools need longer descriptions | Kept under about 1,000 characters, with the detail in `mlqt_get_guidance`. Step 3 is run with Tool Search off too, so the up-front cost of the definitions is measured, not assumed |
| Loading dependencies finds the wrong version, or a stale sibling | A copy is taken only if its version satisfies `uses`. A version already loaded is never replaced. The answer names where each dependency came from, so a wrong pick is visible |
| A startup script computes a path the reader cannot work out | The line is reported with its number, and the answer says which libraries may be missing because of it. The agent can pass that path itself |
| An explicit load replaces a library other classes were analysed against | The replaced copy is named in the answer, and dependency analysis is re-run for the classes that used it |
| Sibling-first differs from Dymola's default order | It is Dymola's documented alternative order, and version matching makes the two agree whenever the sibling's version is the one `uses` asks for |
| The consolidated set is no better | Step 1 is worth having on its own. If step 3 fails twice, ship step 1, keep the current names, and record the merge under *Decided against* with the numbers |

## Decisions (2026-10-18)

The user's answers to the questions this note left open:

1. **The element-level operations stay** inside `mlqt_edit_class`, beside `replace`.
   `add_connection` refuses incompatible connectors and routes the line on the diagram, and
   `remove_component` takes the component's connections with it; a text patch does neither. They
   are re-evaluated after step 3, and any still uncalled are dropped then.
2. **Dependencies are searched** as *Finding dependencies* sets out: libraries already loaded,
   sibling directories, the simulation tool's library folders, then MODELICAPATH. Explicit loads,
   including those read from a Dymola startup script, win.
3. **The install searched** is chosen by a `toolInstall` parameter, then by the desktop app's
   External Tools paths (read only), then by the newest install detected.
4. **Startup-script fixtures** come from two or three of the user's real scripts, anonymised.
5. **The real-world checks** are two cut-down scenarios, not the four full sessions.
6. **The cut-over is a single release.** The old names are not kept alongside the new ones, and the
   `Toolset` switch exists only for the comparison in step 3. It is deleted at the cut-over with the
   old tools.
