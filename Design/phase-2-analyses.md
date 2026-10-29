# Phase 2 — the Wave-2 analyses

The design for phase 2 of `Design/roadmap.md`: the analyses that depend on resolving names and
paths across libraries. The next release ships with all of it (decided 2026-10-29). **Retire this
note when the phase ships**, and move what is durable into the code, the skills and the guidelines.

## What every analysis here shares

Each one is an `IGraphAnalyzer` in `ModelicaGraph/Analysis/`, registered in
`GraphAnalysisRunner.BuiltIn`. That one registration is what puts its findings into the desktop
app, `mlqt check` and the MCP server alike, with the same severities, fingerprints, `__MLQT`
suppression and baseline handling as everything else. No surface gets work of its own.

Every rule id needs a `RuleCatalog` entry. The settings dialog and the Code Review filter are
derived from the catalogue, and the guards (`EveryRuleId_IsRegistered`,
`RuleSettingsLayout.UnreachableRules()`, `CodeReviewRuleFilterTests`) fail until it has one.

**Three-state reporting** (roadmap §2, *Guiding principle*). A question about something outside the
loaded libraries has three answers: yes, no and "cannot tell". Only "no" is a finding. A miss caused
by a library that is not loaded says nothing about the code being checked, and a rule that reported
those would flood every real repository on its first run.

**Every new rule is built off by default** until the release decision (roadmap, *Before the next
release*, item 3) is taken rule by rule.

## 1. External-resource validation — designed, in progress

### The gap

MLQT already finds a library's resources and resolves them (`ExternalResourceExtractor`, then
`GraphBuilder.CreateResourceNodeAndEdge`), and the External Resources tab shows the ones it cannot
find. But those are `ResourceWarning`s on one page. They have no rule id, so they never reach
`mlqt check`, cannot be baselined, and cannot fail a build. A library can lose a file from its
`Resources/` folder and every gate stays green.

### What was found while designing it

- **A reference into a library that is not loaded is dropped**, which is right: `ResolveModelicaUri`
  returns null and no edge is made. That is "cannot tell", and it must stay unreported.
- **Two kinds of missing resource are never recorded at all**, so neither the tab nor a rule could
  report them:
  - an `IncludeDirectory`, `LibraryDirectory` or `SourceDirectory` that does not exist: the
    directory branch makes an edge only when `Directory.Exists`;
  - a `Library="name"` with no binary under any library directory: `ResolveAllLibraryFiles` finds
    nothing and no edge is made.
- **Existence goes stale in the desktop app (B521).** `DirectedGraph.GetOrCreateResource*Node`
  reads the disk only when it creates a node and returns the existing node afterwards, and nothing
  else ever updates `FileExists` or `DirectoryExists`. The resource watchers record each change in a
  pending list that nothing reads; the handler's comment says it fires an event and it only logs. So
  restoring or deleting a referenced file changes nothing until the project is reloaded.

### The rules

| Rule id | Reports | Severity when on |
|---|---|---|
| `MLQT.Resource.Missing` | A reference that resolved to a place in a loaded library, or to a path on disk, where nothing is: a file, a directory annotation, a header in the declared include directories, or a library binary with no platform variant in any library directory | Error: a model that loads a missing file fails at run time or will not compile |
| `MLQT.Resource.AbsolutePath` | A reference written as an absolute file-system path, which works only on the machine it was written on | Warning |

Both are attributed to the class holding the reference, at the line where the raw path first
appears in its source, else line 1. The finding's element is the reference as written (the raw
path) and its discriminator the reference kind, so the fingerprint does not move when the file is
reformatted or the library moves on disk.

**Not reported:** a reference into a library that is not loaded (cannot tell); a header the C
compiler supplies (B172 already keeps those out); the contents of a scanned directory, which no
model references by name.

### One decision, shared

The tab and the rule must not disagree. **Sibling surfaces each keeping a private copy of a decision
is the defect shape this repository keeps finding.** So "is this reference a problem, and which" is
answered in one place, a primitive in `ModelicaGraph`, and both use it: the analyzer to make findings,
and `ExternalResourceService` to make its warnings. That primitive reads existence from the disk when
it is asked, and writes the answer back onto the node, so whichever runs, both are current.

### Fixing B521 with it

Reading the disk at analysis time makes the answer current whenever an analysis runs. It does not
make one run. The watcher's changes are raised as an event, and the desktop app re-runs the resource
analysis and the graph analyses for the affected classes, so a finding clears when its file comes
back and appears when its file goes.

### Recording the two missing kinds

`GraphBuilder` records a missing directory annotation as a `ResourceDirectoryNode` whose
`DirectoryExists` is false. It records a library with no binary as a node at the path where the
library would be expected in the first library directory searched, flagged as a library that was
not found under any platform. The tab then shows both, as it shows a missing file today.

## 2. Connection integrity — not designed yet

Unconnected, incompatible and duplicate `connect` equations, and direction. It promotes the
`ConnectorCompatibility` helper. Connector *types* may come from a library that is not loaded, so it
needs the same three-state gate. `ConnectClauses` (ModelicaParser) is the one reader of a class's
connections, nested ones included.

## 3. Deprecated-API usage — not designed yet

References to classes marked `obsolete`, and compatibility with a given MSL version. It needs the
target library visible, so a reference into one that is not loaded is "cannot tell".

## 4. Cyclic-dependency detection — not designed yet

Package dependency cycles, on the dependency graph that already exists. The smallest of the five.

## 5. The rest of broken references — not designed yet

Names in code shipped on 2026-10-25 as `MLQT.Reference.Undeclared`. What is left is putting
`modelica://` references to classes on the same three-state footing: `MLQT.Reference.ModelReferences`
(`CheckModelReferences`) reports every miss today, including one into a library that is not loaded.
