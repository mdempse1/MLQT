# MCP tool consolidation — design note

**Status: not started.** This is the plan for roadmap item **B503**: the MCP server's tool surface goes
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
| 1 | `mlqt_load` | memory | `load_repository`, `load_library`, `discover_libraries` | Finds the kind from the path (working copy, directory of libraries, one library, one file). `discover_only` lists without loading. `with_dependencies` loads what the `uses` annotation names when it can find it, and says which it could not |
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
--repeats 3`) compared with the baseline, plus, from step 1 on, the four real sessions' prompts re-run
and read with `analyse --dedicated`.

### 0. Make the harness able to see the change

- Add tasks for the gaps:
  - patch part of an existing class (a parameter and an equation);
  - find every use of a name in code;
  - build a package of four or more classes;
  - format a whole library;
  - load a library together with its dependencies.
  Each new task must pass `check-tasks`.
- Give the harness `--server-env KEY=VALUE` (passed into the MCP config's `env`), so a run can choose
  `MLQT_MCP_TOOLSET`.
- Save the prompts of the four real sessions as `TestFixtures/McpEval/scenarios/`. They need Dymola or
  OpenModelica, so they are run by hand and read with `analyse --dedicated`.
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
- Let `load_library` and `load_repository` load the dependencies their `uses` annotations name.

**Exit criterion:** compared with the 2026-10-18 sessions, at least half fewer writes to `.mo` files
outside MLQT and at least half fewer `reload` calls. Fewer reads outside MLQT. No drop in harness pass
rate.

### 2. Build the consolidated set behind the switch

All 16 tools, the rewritten instructions and guidance, and the annotations. Both sets pass the full
test suite.

### 3. Compare the two sets

Run the harness, with all tasks 3 times each, under both values of `MLQT_MCP_TOOLSET`, with Tool
Search on and off. Then re-run two of the real scenarios on each set.

**Exit criterion for the consolidated set:**
- pass rate no lower;
- tool calls and tokens per task no higher;
- invalid-parameter errors no higher (the risk of a union-typed operation list);
- bypasses no higher.

If it fails on parameter errors, fix the descriptions and examples and run again before going back on
any merge.

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
| Loading dependencies finds the wrong MSL version | Load only a version that matches the `uses` annotation. Otherwise report what was found and load nothing |
| The consolidated set is no better | Step 1 is worth having on its own. If step 3 fails twice, ship step 1, keep the current names, and record the merge under *Decided against* with the numbers |

## For the user to decide before step 2

1. **The element-level operations.** Keep the structural operations inside `mlqt_edit_class`, or keep
   only `replace`? The recommendation is to keep them. `add_connection` refuses incompatible
   connectors and routes the line on the diagram, and `remove_component` takes the component's
   connections with it; a text patch does neither. Drop any that are still uncalled after step 3.
2. **Where dependencies are looked for.** `MODELICAPATH`, the libraries folders of the installed
   Dymola and OpenModelica, and the repositories already loaded, in that order? Or only paths the user
   configures?
3. **Cut over in one release, or keep the old names alongside for one release?** The recommendation is
   one release: a second surface kept "for compatibility" is exactly the two-implementations shape this
   repository avoids, and every host known today builds MLQT from source.
