# Metrics and Coverage

The Metrics tab answers a different question from the Code Review tab. Code Review lists what is
wrong *right now*, one finding at a time. Metrics tells you **how much of the library is in the state
you want**, as a percentage per dimension, and — once you have a few snapshots — **which way the
numbers are going**.

That second half is the point. On a library of any size the finding list is too long to read as
progress: fixing two hundred missing descriptions out of nine thousand does not feel like anything.
The same work is a visible step on a burndown.

To open this view, click the **Metrics** tab (the bar-chart icon) in the right panel.

## Coverage

Each dimension is a percentage of the classes (or declarations) that are eligible for it:

```
Class description     ████████████████████░░░░   82.4%  (7412/8996)
```

The bar shows the percentage; the numbers beside it are **compliant / eligible**, which is usually
the more useful pair — it tells you how much work the remaining percent actually is.

| Dimension | Counts a class as compliant when |
|-----------|----------------------------------|
| **Class description** | It has a description string. |
| **Documentation info** | It has a `Documentation(info=...)` annotation. |
| **Documentation revisions** | It has a `Documentation(revisions=...)` annotation. |
| **Icon** | It has `Icon` graphics of its own, **or inherits them** through `extends`. |
| **Parameter description** | Every *public* parameter it declares has a description. |
| **Constant description** | Every *public* constant it declares has a description. |
| **Unit** | Every `Real`-derived variable it declares — public and protected — either declares a `unit` inline or has a type that fixes one. |
| **Imports first** / **Extends at top** | Its `import` statements come before everything else, and its `extends` clauses come next. |
| **One of each section** | It has at most one `public`, one `protected` and one `equation`/`algorithm` section. |
| **Initial sections first** / **last** | Its `initial equation`/`initial algorithm` section is on the side the rule asks for. |
| **Equation/algorithm not mixed** | It does not have both an `equation` and an `algorithm` section. |
| **Connections not mixed** | Its equation sections do not mix `connect` statements with other equations. |

Three things decide whether a dimension is listed at all:

- **The rule has to be on.** A rule set to **Off** in [repository settings](settings-reference.md) is
  you saying that gap does not matter here, so the dimension is dropped rather than reported at 40%
  and dragging the average. Turn the rule on and the dimension appears.
- **The formatter must not be closing the gap.** With **Apply formatting rules** on, the layout
  dimensions (imports, extends, sections, and both initial-section orders) are rewritten on every
  save, so reporting them would measure the moment before the save rather than the library. They all
  drop off. The renderer only reorders a class in its one-of-each-section mode, so with
  **One of each section** off the formatter leaves layout alone and the dimensions stay.
- **The class has to be in a repository.** A reference library, or anything marked
  **Reference only**, is loaded so references resolve and is measured for nothing — a vendor's
  descriptions are neither your achievement nor your debt. Classes recovered from an
  [encrypted library](encrypted-libraries.md) are excluded for the same reason.
- **The class must not be excluded from checking.** A library named in `ExcludedLibraries` — usually
  the examples or the test library sharing the repository — has no rule reporting on it, so it is
  measured for nothing either. It is still counted in the **Size** panel: it is your code, and only
  the quality judgement is suppressed. See
  [settings-reference.md](settings-reference.md#excluding-whole-libraries-from-the-checks).

### Active rule findings

Beneath the bars is the number of findings still open in the current scope, after suppression.

It is worth reading alongside the percentages, because it moves when they barely do. On a large
library a day's work might shift *Class description* by 0.3% while taking 27 findings off the list.
It also drops when a finding is **deliberately waived** with `__MLQT(suppress=...)`, which the
coverage percentages do not: coverage is the true state of the source, findings are what is left to
decide about.

If it reads `—` with a **Run analysis** button, style checking has not run for the loaded libraries
yet. Press it, or open Code Review.

## Size

The panel on the right counts the classes in scope, broken down by kind — `package`, `model`,
`function`, `record`, `connector`, and so on. It is the denominator for everything on the left, and
worth a glance when a percentage looks surprising: a scope that turns out to be 90% `record` explains
a lot about its icon coverage.

**What it counts is your code**, and the two ways of taking a class out of the *coverage* figures
differ here — which is the one place on this page where they do:

- A library named in `ExcludedLibraries` **is** counted. It is yours; only the quality judgement is
  suppressed.
- A **reference library**, and anything in a repository marked **Reference only**, is **not**. It is
  not yours to count, any more than it is yours to improve — the same reason it is measured for
  nothing. Classes recovered from an [encrypted library](encrypted-libraries.md) are left out too.

So pointing MLQT at a tool's installed library folder does not add tens of thousands of classes to
this panel or offer their packages in the scope box.

## Scope: the whole project, one library, or one package

The **Scope (package)** box at the top narrows everything on the page — coverage, size, findings and
the trend — to one package and everything beneath it. Leave it empty for every loaded library
together.

Scope is remembered for the session, so switching tabs and coming back keeps your place.

### Comparing sub-libraries

**Compare sub-libraries** puts every package one level below the current scope side by side, one row
each, with a column per dimension:

| Package | Classes | Class description | Icon | Unit |
|---------|--------:|------------------:|-----:|-----:|
| Blocks | 1204 | 94% | 88% | — |
| Fluid | 876 | 61% | 72% | — |
| Mechanics | 933 | 88% | 91% | — |

This is the fastest way to find where the debt actually is. A library-wide 78% rarely means every
package is at 78%; far more often two packages are at 95% and one is at 30%, and only the second
reading tells you what to do on Monday. Click a package name to focus the whole dashboard on it.

A `—` means that package tracks no such dimension.

## The trend

Two or more snapshots for the current scope produce a chart and a table beneath it.

The chart plots one line per dimension over time. Its y-axis **fits the data rather than starting at
zero** — on a library sitting between 80% and 90%, a zero-based axis draws every line as flat. Click
a series in the legend to hide it and refit to what is left, which is how you read one dimension out
of fourteen.

**Outstanding counts** switches the y-axis from percentages to the raw number of findings still open
per dimension (eligible − compliant), plus a total line. Use it for the same reason as the finding
count above: on a big library the counts move well before the percentages do. Snapshots taken before
counts were recorded show as `—` in this view.

The table below lists exact values, most recent first, with the revision each snapshot was taken at.

### Saving snapshots

**Save snapshot** records the current numbers for the current scope into the repository's
`.mlqt/metrics-history.json`.

That file is **meant to be committed**. It lives in the repository so that everyone reviewing the
library sees the same burndown, and so the trend survives a fresh checkout. The "all libraries" view
aggregates the per-repository files rather than keeping a file of its own.

### Letting CI keep the history instead

Pressing a button is a poor way to build a trend — it happens when someone remembers, which is not
often, and never on the commits that matter. `mlqt check --metrics` writes the same file from CI, so
a point is recorded per commit automatically:

```bash
mlqt check ./MyLibrary --metrics
```

An unchanged point is skipped, so a job that commits the file cannot re-trigger itself in a loop.
See [cli.md](cli.md#recording-the-coverage-trend) for `--metrics-out` and `--metrics-force`, and
[ci-quality-gate.md](ci-quality-gate.md) for a worked pipeline.

The same numbers can also **gate** a build — `--min-coverage 80`, or `--coverage-ratchet` to fail
only when a dimension goes backwards from the last recorded snapshot. The ratchet is the one a legacy
library can adopt on day one, since it demands no particular number, only that today is not worse
than yesterday.

## Why the numbers can differ from the finding count

Coverage is measured by walking each class's structure, not by counting findings, so the two answer
slightly different questions and are meant to:

- **A waived finding still counts against coverage.** `__MLQT(suppress=...)` says "do not tell me
  about this again", not "this class now has a description".
- **Except where no finding could ever be raised.** The exception, and the only one: a class taken
  out of formatting — by the **Exclude from Formatting** toggle or by
  [`__MLQT(format=false)`](code-formatting.md#in-the-source-instead-__mlqtformatfalse) — has its
  layout rules skipped by the checker, so it is left off the layout dimensions too. Counting it would
  show a gap nothing will ever name, which is worse than not counting it. The same goes for a whole
  library named in `ExcludedLibraries`: nothing in it is reported on, so nothing in it is measured.
  An audit run (`mlqt check --no-suppress`) reads no directives and so keeps those rows.
- **Coverage counts every eligible class; a finding needs a rule to be on.** A dimension whose rule
  you switch off disappears from coverage entirely rather than reading 100%.
- **A class that could not be parsed is in neither.** It appears in Code Review as a
  [diagnostic](cli.md#diagnostics) instead, and the totals around it are short by however much it
  would have contributed.

## See also

- [code-review.md](code-review.md) — the finding list these numbers summarise
- [settings-reference.md](settings-reference.md) — turning rules on, which decides the dimensions
- [cli.md](cli.md) — `--metrics`, `--min-coverage`, `--coverage-ratchet`
- [ci-quality-gate.md](ci-quality-gate.md) — building the burndown from CI
