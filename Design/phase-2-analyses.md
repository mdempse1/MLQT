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

## 1. External-resource validation — done (2026-10-29)

Built as designed below, with the change recorded under *The rules*: a library with no binary is not
reported. The durable parts are in the code, the `external-resources` skill, and
`Documentation/external-resources.md` / `settings-reference.md`. When the phase ships, this section
needs nothing kept.

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
    directory branch makes an edge only when `Directory.Exists`. Now recorded;
  - a `Library="name"` with no binary under any library directory: `ResolveAllLibraryFiles` finds
    nothing and no edge is made. Left unrecorded, deliberately (below).
- **Existence goes stale in the desktop app (B521).** `DirectedGraph.GetOrCreateResource*Node`
  reads the disk only when it creates a node and returns the existing node afterwards, and nothing
  else ever updates `FileExists` or `DirectoryExists`. The resource watchers record each change in a
  pending list that nothing reads; the handler's comment says it fires an event and it only logs. So
  restoring or deleting a referenced file changes nothing until the project is reloaded.

### The rules

| Rule id | Reports | Severity when on |
|---|---|---|
| `MLQT.Resource.Missing` | A reference that resolved to a place in a loaded library, or to a path on disk, where nothing is: a file, a directory annotation, or a header in the declared include directories | Error: a model that loads a missing file fails at run time or will not compile |
| `MLQT.Resource.AbsolutePath` | A reference written as an absolute file-system path, which works only on the machine it was written on | Warning |

Both are attributed to the class holding the reference, at the line where the raw path first
appears in its source, else line 1. The finding's element is the reference as written (the raw
path) and its discriminator the reference kind, so the fingerprint does not move when the file is
reformatted or the library moves on disk.

**Not reported:**
- a reference into a library that is not loaded (cannot tell);
- a header the C compiler supplies (B172 already keeps those out);
- **a `Library="name"` with no binary under the library's directories.** A tool also links the
  system's own libraries (`Library="m"`, `Library="Ws2_32"`), so the absence of a binary from
  `Resources/Library` is often normal, and MLQT cannot tell which case it is. Decided while
  implementing, reversing the first version of this note, for the reason B172 gives for headers;
- the contents of a scanned directory, which no model references by name.

**Attributed to the innermost class.** Found while building it: `ModelAnalyzer` deliberately reads
resources from the whole of a class's source, nested classes included, so a package stored in one
file had an edge for every reference its nested classes make, and each of them had its own. The
first run reported each reference once per enclosing class. `ResourceProblems` gives a reference
to the innermost class that makes it, and the tab's warnings follow the same rule.

**B522, found by the rule's journey.** Removing a class from the graph never removed its resource
references, so a class loaded again under the same id kept its old ones beside its new ones: an
edited `loadResource` path, a reloaded library, another checkout of the same library. The journey
passed alone and failed after the other journeys, which had loaded libraries called `Lib` from
checkouts since deleted. `DirectedGraph.RemoveNode`/`RemoveNodes` now drop them.

**Measured on real libraries.** Over MSL 4.1 and Buildings, with both rules on, the first run
reported one false positive: an example image in `UsersGuide.Conventions` written as HTML shown as
text (`&lt;img src=&quot;…&quot;&gt;`), which B209 had taken for a reference. Every such URI is now
skipped, as `CheckModelReferences` always did, through one shared test. What is left is five files
that really are missing, one in MSL and four in Buildings. The analyzer first cost 1.6 s of the MSL
check, because finding a class's references and the classes reaching a resource were both scans of
every resource edge; `DirectedGraph` now indexes both, and it costs 0.2 s, most of it reading the disk.

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

### Recording a missing directory annotation

`GraphBuilder` records a directory annotation that resolves but does not exist as a
`ResourceDirectoryNode` whose `DirectoryExists` is false, with nothing scanned in it. The tab then
shows it, as it shows a missing file. A library with no binary stays unrecorded, as above.

## 2. Connection integrity — incompatible and duplicate done (2026-10-29)

`MLQT.Connection.Incompatible` (Error when on) and `MLQT.Connection.Duplicate` (Warning when on), off
by default, in a new **Connections** category. `ConnectionIntegrityAnalyzer` reads each class's
connects through `ConnectClauses.WithEquations`, the one reader, nested ones included.

**One signature.** The MCP server's `ConnectorCompatibility` had a private signature for
`add_connection`. Promoting it found a bug: an alias connector (`connector Frame_a = Frame`) was the
text `alias:Frame`, which nothing equals, so wiring an alias to its base was refused - a rule built
on it would have reported every such connection. It also ignored `stream`. `ConnectorSignature`
(ModelicaGraph) is now the one definition, and the MCP helper delegates to it: members by name with
`flow`/`stream`, inherited ones included; a value connector its predefined type (causality and unit
are separate questions); an enumeration itself; an alias its base. Member types are not compared,
which keeps it lenient - what it reports is two connectors with different members.

**Three states.** A port resolves through `ClassElementResolver.ReferencesIn`, so through
inheritance and with subscripts dropped. No signature - a connector not loaded, encrypted, with an
unreadable base, or `expandable` - is "cannot tell". A port that does not resolve is
`MLQT.Reference.Undeclared`'s, and whatever a redeclaration decides is left to it: a `replaceable`
port, one reached through a replaceable component, one whose class is replaceable.

**One answer to "which connector is this port?"** `ConnectorSignature.ConnectorOf`, asked of a port
resolved with `ReferencesIn`, by the rule and by `add_connection` alike. The review found the MCP
tool still resolved ports its own way: it split `a[1].p` into a segment `a[1]`, which no component
is called, and refused a valid array connect, and it judged a replaceable port the rule leaves
alone. Its walk now only words the error when a port does not resolve.

**Duplicates** are the unordered pair, spacing removed outside quoted names, under branches that
apply at the same time: the same `for`/`if`/`when` headers and, for each, the same earlier conditions
false (`ConnectScope.PreviousConditions`, `Applies`). The same pair in both branches of an `if`
never exists twice; the first version also keyed an `else` on the word alone, so the `else`
branches of two different statements read as one branch. Two `if useB` statements still match: both
connect when `useB` holds. Reported on the second occurrence, naming the first's line. Incompatible
uses the same ordered pair, so `connect(a,b)` and `connect(b,a)` are one incompatible connection.

**Measured.** MSL 4.1: 99% of 9,627 connects judged, nothing reported, 0.9 s after running a class at
a time in parallel (5.7 s serially). Buildings: no incompatible connection and 19 duplicates, sampled
against the source and real.

**Not done, and why.** *Direction* (an inside `input` joined to an inside `input`, and the rest of MLS
§9.3) needs to know whether each end is an inside or outside connector and its causality through the
whole chain of short classes. *Unconnected connectors* needs the whole instance - a connector left
open in a partial model is connected in the class that extends it - which no analysis here has. Each
is an increment of its own if wanted.

## 3. Deprecated-API usage — obsolete classes done (2026-10-29)

`MLQT.Reference.Obsolete`, Warning when on, off by default, in **Correctness**.
`ObsoleteClassAnalyzer`. Nothing in MLQT read the `obsolete` annotation before.

**Read at load.** `ModelExtractorVisitor` reads the class annotation's `obsolete` with the
`experiment` it already looked for, onto `ModelInfo.ObsoleteMessage` and `ModelNode.ObsoleteMessage`:
the message, empty for a mark that is not one string literal, null for none. Kept on the class like
`HasExperimentAnnotation`, so asking costs no parse. The review found two places it was not read: a
short class (`type OldAngle = Real(...) annotation(obsolete=...)`), whose annotation is in its
comment, and a leading class annotation when the class also has a trailing one - only the trailing
one was read, for `experiment` as well. Both are read now.

**What is obsolete.** The class marked, and everything inside it, with the outermost mark winning so
the reader is told about the package. The annotation is not inherited (MLS §18.1 does not say it is,
and a class extending an obsolete one is the user to tell).

**Finding the uses.** The dependency edges say which classes a class uses, so a class using nothing
obsolete is passed over without reading its text - which is nearly every class. Where one does, the
`ReferenceLocator` the rename and move tools use finds each site, resolved as dependency analysis
resolves it; a class's own stored text carries no `within`, so the locator is told its package
(`Within`, internal). A package's text holds its inline children, which are checked as themselves, so
only sites whose scope is the class itself count. One finding per obsolete class per user, at its
first use.

**Three states.** Obsolete is something MLQT reads, never infers: a class not loaded or encrypted
shows no annotation and is never reported. That can only under-report.

**Not reported:** a use from a class that is itself obsolete, or inside one. Exempting a package called
`Obsolete`, or the obsolete class's sibling `Validation` package, was considered and not done - the
same reasoning as `Examples` in §4: it is a convention, not the language, and `__MLQT(suppress=...)` on
the package waives every class inside it.

**Measured.** MSL 4.1: nothing (its obsolete `TLine1`-`3` are unused). Buildings: 84, every one inside
`Buildings.Obsolete` - validation models and the old G36 sequences using the obsolete blocks they sit
beside, sampled against the source. 0.2 s.

**Not done: compatibility with a given MSL version.** MSL's `conversion` annotation and its
3.2.3-to-4.0.0 script say which names a library written for an older version must change. A library
loaded against a newer MSL than it declares is already refused by the CLI unless
`--allow-version-mismatch`, and a name the newer version removed is already
`MLQT.Reference.Undeclared`. What is left is a better message for that finding - "renamed to X in
4.0.0", read from the script - and judging a class added after the declared version, which needs the
declared version loaded. Either is an increment of its own if wanted.

## 4. Cyclic-dependency detection — done (2026-10-29)

`MLQT.Structure.DependencyCycle`, Warning when on, off by default. `DependencyCycleAnalyzer` builds
on the dependency edges that already exist. Nothing in MLQT detected a cycle before; several places
only guard against one.

**What is a cycle worth reporting.** Packages that depend on each other, not classes: classes that
refer to each other are ordinary Modelica. The first run compared at whatever level two names
parted, and 21 of its 45 MSL findings were classes naming each other - inverse functions, the
`dp_*_DP`/`dp_*_MFLOW` pairs of `Fluid.Dissipation`, `Matrices.inv` and `norm`. So both sides at that
level must be packages. A dependency is counted at that one level only, so a deep cycle is reported
once. A class using its own enclosing scope counts for nothing.

**Examples.** A class with `experiment(...)` contributes nothing, the exemption `MLQT.Unused.Class`
makes: without it `Blocks.Examples` building circuits from `Electrical` and `Mechanics` made most of
MSL's top level one cycle. A helper inside an `Examples` package with no experiment of its own still
counts (one of MSL's 24, one of Buildings' 20). Guessing from a package being called `Examples` was
considered and not done: it is a convention, not a rule of the language, and `__MLQT` waives a cycle
that is meant.

**Three states.** Dependency edges exist only between loaded classes, so a library that is not
loaded can hide a cycle but never invent one.

**What is followed, and where it is reported** (changed after the branch review, 2026-10-29). A
cycle inside a library is found over the whole library and reported when a checked class is inside
one of its packages: the first version reported every cycle in a library for a check of one changed
class. Between libraries, edges are followed across every loaded library and the cycle is reported on
each checked library in it. The first version followed only the checked libraries and reported on
the first of them, so the MCP server, which checks one library per call, never saw a cycle the
desktop app (a repository at a time) reported - the same libraries, different findings by tool. A
reference or excluded library is followed and never reported on.

**The finding.** On the parent package (for libraries depending on each other, on each checked one),
`ElementPath` the sorted members - a different route through the same packages is the same cycle -
and the message the shortest route round it with the first-by-name class making each step. Measured
on MSL 4.1 and Buildings: 24 and 20 cycles, all package-level coupling such as `Rotational.Interfaces`
using `Rotational.Components.Fixed`, at 0.1 s each. Following loaded libraries that are not checked
added one on MSL: `Modelica` and `ModelicaServices` use each other, which is how MSL is split.

## 5. The rest of broken references — done (2026-10-29)

Names in code shipped on 2026-10-25 as `MLQT.Reference.Undeclared`. `modelica://` links to classes,
`MLQT.Reference.ModelReferences`, are now on the same three-state footing.

**The verdict.** `CheckModelReferences` is a per-class rule in ModelicaParser, which has no graph, so
it was given the set of loaded ids and reported every name not in it. It now asks a verdict
(`ClassReferenceTarget`: found, missing, cannot tell) built once per check in ModelicaGraph
(`ClassReferences`, through `StyleChecking.CreateClassReferenceLookup` and `StyleCheckContext`, so the
desktop app, the CLI and MCP all get it). The name is walked a segment at a time with
`TypeResolver.MemberClass`, which finds inherited classes; a first segment not loaded cannot be told;
a segment found nowhere is missing only if the class it was looked for in is complete.

**Not `uses(...)`.** `MLQT.Reference.Undeclared` reports a first segment the library's `uses(...)`
rules out, and the review asked why links do not. Applying it was tried and measured: MSL went from
none to the same seven findings - its links into ModelicaReference, a documentation library its
`uses` rightly leaves out. Code needs what it names; a link in documentation does not, so `uses` is
not evidence about it. `ClassReferencesTests` holds it.

**Inherited classes in an encrypted library.** The review asked whether a recovered class whose help
page gives no bases could hide an inherited class. Treating "no bases given" as unknown was tried and
reverted: Dymola's help leaves out the "Extends from" line only when there is no base, and a class
that does inherit - a medium's `ThermodynamicState` - has the line, so its stub extends its base and
the class is found (`ClassReferencesTests.AClassAnEncryptedMediumInherits_IsFound_ThroughItsDocumentedBase`).
As unknown, every link into a documented package became unjudgeable.

**One "complete", with a choice.** The completeness test was private to `UndeclaredNameAnalyzer`; it
is now `ClassCompleteness`, shared. Building this found a decision the test suite already held: a
link into an encrypted library's documented package, naming a class the documentation does not
list, is reported (`EncryptedLibraryEndToEndTests`). The help HTML lists a package's classes but not
every member, so for a class link a recovered class counts as known, and for an undeclared name it
does not. `ClassCompleteness` takes which question it answers.

**API.** `CheckModelReferences.WithVerdict` is a factory rather than a second constructor: two public
overloads with optional parameters are a source break (RS0026/RS0027). `RunStyleCheckingFindings`
gained a trailing optional `classReferenceTarget`, its shipped line moved to Unshipped; a trailing
optional parameter is source-compatible for every caller that calls it.

**Measured.** MSL 4.1: 7 findings before, all into ModelicaReference, not loaded; none after, and none
with ModelicaReference loaded either, its quoted names (`'assert()'`) resolving. Buildings: 42 before
and after, all broken, sampled against the source.
