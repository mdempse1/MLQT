# Startup-script fixtures

Dymola cannot say which libraries it has loaded. For a user who opens their libraries with a startup
script, that script is the record. So `mlqt_load` **reads a startup script without running it**, and
loads what it opens (B503; how it reads one is in `.claude/skills/skill-mcp-library-loading.md`). These
fixtures are what that reader is tested against.

`startup1`–`startup3` are real users' scripts. `startup4.mos` is synthetic: it was written for the
tests, to cover what the real ones do not. **The real ones have been anonymised**: library, vendor and project names
are invented, and every absolute path starts with `{root}`. The libraries they open are small dummies,
each a `package.mo` with a `version` and a `uses` chain, at the paths the scripts expect.

## Using them in a test

A script names absolute paths, which is what makes it work in Dymola, and those paths would tie a
test to one checkout. So a test:
1. copies this folder to a new temporary folder;
2. replaces `{root}` in each `.mos` and `.mo` file of the copy with that folder's path;
3. reads the script, with an environment of its own: `USERNAME=testuser` for `startup3.mos`;
4. compares what was read with `expected/<script>.json`.

Paths in `expected/` are relative to the copy. Never point a test at this folder itself, or at a
fixed folder on the machine.

## What each script exercises

| Script | What it needs from the reader |
|---|---|
| `startup1.mos` | A string variable joined with `+`. **Windows backslashes in Modelica strings**: `"\CoreLib\package.mo"` is a path, not an escape. `openModel` changing directory by default |
| `startup2.mos` | **A function call.** It opens `MyTools`, then calls `MyTools.OpenTrunk()`. That function opens libraries in `if` branches decided by its inputs' defaults, through `DymolaCommands.SimulatorAPI.openModel`. It also calls another function of the same library, `OpenPowertrain`, which opens one more. Three libraries must **not** load, because their inputs default to false |
| `startup3.mos` | `changeDirectory=false` and `mustRead=false`. **Encrypted libraries**, both `package.moe` folders and single `.moe` files. Commented-out `openModel` lines, which must not load. A variable read with `getEnvironmentVariable("USERNAME")` and used in a `cd`. MODELICAPATH set with `Utilities.setenv`. Lines that load nothing, such as `Advanced.*` settings and an expression whose value is not kept, which are reported as ignored, not as lines the reader could not interpret |

| `startup4.mos` (synthetic) | `cd`, then `AddModelicaPath` and **`openModelFile` by name and version**, with two versions of `Common` on the path. **Relative paths** after `openModel` has changed directory. A nested script through **`RunScript`**, with `scriptDir`. **`import`**, which loads a library once and then does nothing |

## What `expected/` holds

For each script:
- `loads`: each library it opens, in order, with its version (from the folder name or the `version`
  annotation; null when neither gives one) and its path. Also the script line that opens it, and the
  call chain when a function does it (`via`).
- `notLoaded`: libraries that exist but must not load, each with the reason.
- `ignored` and `unresolved`: lines that load nothing, and lines the reader could not work out.
- `currentDirectory` and `modelicaPathSet`: where the script leaves Dymola.

**Check an expectation against Dymola before relying on it.** These files say what Dymola is
believed to do, and the reader is tested against them. If Dymola does otherwise, for example with a
backslash in a string, the expectation is what changes.
