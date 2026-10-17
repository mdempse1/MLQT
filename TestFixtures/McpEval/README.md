# MCP evaluation fixture

The library and the tasks [build/mcp-eval.py](../../build/mcp-eval.py) runs an agent against, to measure
how well it uses the MLQT MCP server. See [Measuring how well the toolset works](../../Documentation/mcp-server.md#measuring-how-well-the-toolset-works-evaluation-harness).

`EvalLib` is small and **deliberately imperfect**. Each flaw is there for a task, so do not fix one:

| Flaw | Task |
|---|---|
| `Examples.SimpleLoop` has no description string | `describe-class` |
| `Blocks.FirstOrderLag` misspells "First" and "Time" (three times) | `fix-spelling` |
| `Examples.NoisySource` declares `Modelica.Blocks.Sources.Sinee`, which does not exist | `fix-broken-reference` |
| `Blocks.FirstOrderLag.y_start` has no description | free for a style task |

It uses the Modelica Standard Library 4.1.0, which is not in the repository: the harness is told
where it is with `--msl`.

## Adding a task

A task in `tasks.json` has an `id`, a `group`, a `prompt` (the shared `preamble` goes in front of it,
with `{lib}` and `{msl}` filled in), the `expected_tools` it was written for (alternatives separated
by `|`, matched without the `mlqt_` prefix), and `checks`:

| Check | Passes when |
|---|---|
| `answer_matches` / `answer_not_matches` | the regex `pattern` is / is not in the final answer |
| `file_matches` / `file_not_matches` | the regex is / is not in `path` (relative to the work directory) |
| `glob_matches` / `glob_not_matches` | some / no file matching `glob` contains the regex |
| `file_exists` | `path` exists |
| `command` | the shell `command` exits 0 in the work directory |

Check against what the task asks for, not how a tool happens to write it: allow either order of a
`connect`, either spacing of a modifier, either file a new class might land in. Then run
`python build/mcp-eval.py check-tasks`, which fails if any task passes on the untouched fixture.
