# Unit / dimensional consistency — design note

**Status: not started.** This is the design for the roadmap's phase-3 flagship *Unit / dimensional
consistency* (§2, *Flagships*). It changes nothing about the sequencing: the roadmap still puts
extensibility first. Written 2026-10-02, after the work that measured what the check can rely on (see
*What it builds on*). Retire this note when the check ships, moving what is durable into the code,
`CODING_GUIDELINES.md` and a skill, as the earlier phase notes were.

## What it is for

`MLQT.Units.MissingUnit` asks whether a quantity **has** a unit. This check asks whether an equation's
units **agree**: `v = s / t` where `v` is `m/s`, `s` is `m` and `t` is `h` is consistent in dimension,
and `F = m * v` with `F` in `N` is not. It is the commonest modelling error a static tool can find
without simulating, and it is inside the boundary the roadmap keeps: no flattening, no solving, one
class's equations at a time, judged against the types its components are declared with.

**What it must never do is report correct code.** A unit checker that is wrong one time in a hundred
on MSL is switched off by everyone who tries it. Every decision below that trades coverage against
false positives takes the coverage loss.

## What it builds on (measured 2026-10-02)

| Need | Where it is | State |
|------|-------------|-------|
| The type of every component an equation names | `TypeResolver.ResolveWithInheritance` | **Complete for MSL and Buildings**: 0 unresolved of 29,463 component types in MSL 4.1.0's models, blocks, connectors and records, and of 70,230 in Buildings 13 |
| The unit that type fixes | `UnitResolver.ResolveAttributes` | Short aliases and, since `e0fda85`, long-form `type ... extends` |
| A component reference's element and type, segment by segment (`inertia1.flange_b.tau`) | `ClassElementResolver.ReferencesIn` | In place, with instance modifications applied to defaults |
| What an instance's modification changes | `ClassElement.Modifications` / `Redeclarations` (`4e77137`) | Recorded; see the next row |
| Whether a redeclaration changes a unit | — | **It does not, in either library.** Over 316 package redeclarations in MSL and 5,920 in Buildings, the 2,189 and 23,936 `Medium.X` members they reach have the same unit from the constraining type as from the redeclared one. So the check resolves against the constraining type and needs no redeclaration-aware resolution (roadmap, *Decided against*) |
| Array dimensions | `ClassElement.ArraySubscripts` | Recorded; the check does not need them (see *Not in scope*) |
| A finding, its severity and waiver | `Finding`, `RuleCatalog`, `__MLQT(suppress=...)` | In place; a new rule id brings the settings dialog entry, the Code Review filter and the baseline with it |
| The same answer on every surface | `StyleCheckRunner` / `StyleCheckContext` | In place; the check is a per-class rule run through it |

What does **not** exist: anything that reads a unit string, and anything that walks an equation's
expression tree. `BehaviorExtractor` slices equations as text for MCP; the check needs the parse tree.

## Design

### 1. Unit algebra — a value type and a parser

A unit is a **scale and a vector of integer exponents over the seven SI base units** (m, kg, s, A, K,
mol, cd), parsed from the MLS unit-expression syntax (MLS ch. 19): products with `.`, one `/`,
parentheses, integer exponents written directly after the symbol (`m2`, `s-1`), and SI prefixes
(`km`, `mm`, `mA`). The parser is a pure function in `ModelicaParser` (it needs no graph), held to that
assembly's 95% bar, and table-driven over the derived units (`N`, `J`, `W`, `Pa`, `V`, `Ohm`, `Hz`...).

What MSL actually writes decides the table, not the standard alone. It writes `unit=` **1,345 times
with 227 distinct strings**, among them `(J/kg)/(kg/m3)`, `1/(A.s)`, `m3.kg-1.K-1`, `km/h`, `kW.h`,
`rev/min`, `bar`, `l`, `h`, `min`, and the offset units `degC`, `degF` and `degRk`. The acceptance test
for the parser is that **every one of the 227 parses**, and that Buildings' set does too.

Two rules settle the hard cases:

- **An offset unit has no dimension vector.** `degC` is `K` shifted, so `T_C = T_K - 273.15` is
  consistent and dimension arithmetic cannot say so. An operand in an offset unit is **unknown** (see
  §2), never compared. MSL uses them almost only in its conversion functions, which is exactly where a
  naive check would report correct code.
- **A string that does not parse is unknown**, with a log line naming it. It is never a finding: the
  unit string is the library's, and MLQT not knowing it says nothing about the equation.

### 2. Typing an expression — three states, not two

Each sub-expression of an equation is given a unit bottom-up over the grammar's `arithmetic_expression`
/ `term` / `factor` / `primary`. The answer has three states, which is the roadmap's confidence
principle applied to units:

1. **Known** — a parsed unit.
2. **Unknown** — anything the check cannot vouch for. It absorbs: a sum or product with an unknown
   operand is unknown, and **an unknown never causes a finding.**
3. **Dimensionless** — known, and `1`.

Unknown comes from: a numeric literal; a `Real` that declares no unit (5,926 + 145 components in MSL,
which is what `MissingUnit` is for); a type that resolves to no loaded class (external, encrypted);
an offset or unparsed unit; an if-expression whose branches disagree; an array constructor; a function
whose output type fixes no unit; and anything below not listed as known.

| Construct | Unit |
|-----------|------|
| `a + b`, `a - b`, `a = b`, `a < b`, `min(a, b)`, `max(a, b)` | Both must agree. **The mismatch site**: a finding when both are known and their dimensions differ |
| `a * b`, `a / b` | Exponents add / subtract |
| `a ^ n` with `n` an integer literal | Exponents multiply; any other exponent makes the result unknown unless `a` is dimensionless |
| `der(x)` | `unit(x) / s` |
| `sqrt(a)` | Halves exponents; unknown if one is odd |
| `abs`, `sign`-free built-ins that preserve units (`abs`, `pre`, `delay(x, ...)`, `noEvent`, `smooth(_, x)`, `homotopy(a, b)`) | The argument's (both of `homotopy`'s must agree) |
| `sin`, `cos`, `tan`, `exp`, `log`, `log10`, `asin`... | Dimensionless result; argument expected dimensionless — see *Open questions* on `rad` |
| A user function call | Its single output's declared type; each argument checked against the declared input type as an `=` would be. Several outputs, or an output with no unit: unknown |
| A component reference | Via `ReferencesIn`: the last segment's declared type through `UnitResolver`, or the `unit` written on the declaration itself, which wins |
| A literal | Unknown (see *Open questions*) |

### 3. What is checked

**Each class's own equations**, in the equation sections of its own body, including those nested in
`if`, `for` and `when` equations — the same scope `BehaviorExtractor` reports, read from the tree.
`connect` is not an equation this check reads: connector compatibility is its own Wave-2 item.

**Not a base class's equations again in each derived class.** They are checked once, in the class that
writes them. A derived class *can* change a unit through a modification
(`extends Base(x(unit = "s"))`), and that case is missed; checking every inherited equation in every
derived class would multiply both the time and the findings (one defect reported in forty subclasses).
Revisit if a library turns up where it matters.

**Algorithm sections are a second increment.** `:=` is `=` for this purpose, but statements bring
loops and conditional assignment, and the equation case should be proven on the corpus first.

### 4. The finding

A new rule, `MLQT.Units.Inconsistent` (name to confirm), category Units, governed like the other unit
rule, **off by default** and Warning when on. One finding per mismatch site, carrying the equation's
line, the two units as written *and* as dimensions (`N` vs `kg.m/s` reads as "these differ by `1/s`"),
and the sub-expressions that disagree. Its fingerprint is the equation's normalised text plus the site,
so a reformat does not re-raise it. `__MLQT(suppress="MLQT.Units.Inconsistent")` waives it like any
other; registering it in `RuleCatalog` is the whole of making it appear in the settings dialog, the
Code Review filter and the baseline.

It runs per class through `StyleCheckRunner`, so the GUI, `mlqt check` and MCP report the same
findings. Its inputs go on `StyleCheckContext`: the existing unit lookup widened from
`(IsRealDerived, HasUnit)` to `UnitAttributes`, and the run's `InterfaceCache` and `AncestorCache` for
`ReferencesIn` — one `ComponentReferences` per class, dropped when the class is done.

### 5. Cost

The reference-library rule applies: **nothing may scale with the size of the graph**, only with the
classes checked. Per class it is one `ReferencesIn` plus a walk of its equations; type and unit answers
are cached for the run by the resolvers already. Budget: no more than 10% added to an MSL
`mlqt check --timings` run, measured before the rule is turned on anywhere. `MeasureNested` for the
lazily cached parts (B128).

## How it will be shown to be right

- **Unit tests per row of the table in §2**, and mutation testing over the algebra and the walker
  (`run-mutation.ps1 -Mutate`): this is exactly the code where a test that asserts current output
  instead of the rule passes and means nothing.
- **The corpus is the acceptance test.** Run over MSL 4.1.0 and Buildings 13 and **read every finding.**
  Each is either a real inconsistency, which is worth a sentence in the note that ships the rule, or a
  false positive, which blocks shipping until its cause is fixed or put under unknown. The target is
  that every finding on those two libraries is one their maintainers would accept as a defect.
- **The parser over every unit string** both libraries write (§1).
- Where Dymola is installed, a sample of models can be put through its check
  (`DymolaCheckingService`) to compare what it reports about units. A comparison aid, never a gate.

## Increments

1. **Unit algebra and parser** (`ModelicaParser`), with the 227-string acceptance test. Usable alone:
   MCP could report a component's unit as dimensions.
2. **Expression typing over one class's equations**, with component references through `ReferencesIn`,
   as a library call returning mismatch sites — no rule yet. Run over the corpus from here on.
3. **The rule**: id, catalogue entry, finding, context inputs, waivers, docs
   (`settings-reference.md`, `troubleshooting.md`).
4. **Function calls**: outputs and argument checks.
5. **Corpus triage** to the acceptance target, then the timings budget, then on by default or not —
   the user's decision, with the numbers.
6. **Algorithms**, if 5 says the equation case is trustworthy.

## Open questions — the user's to decide before increment 2

- **Is `rad` dimensionless?** In SI it is `m/m`. If it is, `sin(omega * t)` with `omega` in `rad/s`
  checks, but so does `omega = f` with `f` in `Hz`, which is the classic confusion the check could
  catch. Treating `rad` as its own base dimension catches that and reports `sin(phi)` for `phi` in `rad`
  unless the trigonometric functions accept `rad` as well as `1` — which is the proposal.
- **Literals.** Unknown is safe and loses `x = 2 * y` checks entirely. The alternative — a literal is
  dimensionless where a dimensionless is needed and unknown elsewhere — finds more and is where false
  positives would come from. The proposal is unknown, revisited after the corpus run.
- **Default on or off.** The proposal is off until increment 5 says otherwise.

## Not in scope

`displayUnit` (presentation only); unit conversion or any change to a file; array dimension
agreement (a different check, and `ArraySubscripts` is there for it); records and connectors as
quantities; anything requiring flattening — that is the equation-balance flagship's problem.
