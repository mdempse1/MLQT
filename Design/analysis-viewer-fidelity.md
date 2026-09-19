# Analysis — a viewer mode that preserves the original formatting

**Status: analysis, and now a plan. No code has been changed.** Written on
2026-09-17 to feed the decision recorded as "the one decision that shapes the phase" in
`phase-1-release-feedback.md`, which is being edited elsewhere. Kept separate on purpose; if the
recommendation here is accepted, that note's section is what changes, and this file is retired.

**Part I (§1–§9) is the measurement** that says a fidelity *mode* is possible beside the rendered
view. **Part II (§10–§15), added 2026-09-18, is the stronger proposal and the plan**: run
`ModelicaRenderer` on the save path only, and show the file everywhere else. Part II supersedes Part
I's sizing in two places — §11a and §11b — and answers the three questions §8 left open. Read §14 for
the staging and §15 for what still needs a decision.

**Part III (§16–§18), added 2026-09-19, is S0 and S1 actually run. Both gates pass.** It corrects
Part I in three places (§16a–c) and **Part II's re-slice rule in §6c/§11d is wrong as written**
(§17.4) — read §17.4 before implementing B215. B216 is in scope; B230, B231 and B232 were opened on
the way.

The question asked: *the Code Review page reformats what it shows, so a user who has not enabled
"apply formatting" sees something very different from what their Modelica editor shows. What would it
take to have a view that preserves the original text and still highlights it?*

Everything below that carries a number was measured, against
`C:\Projects\Modelica\ModelicaStandardLibrary` and `C:\Projects\Modelica\Modelica-Buildings-Original`
— neither of which MLQT has ever formatted. The measuring harness is described at the end and its
essential part is reproduced in full, because it is small and it is the whole argument.

---

## 1. How different is "different"?

The viewer renders through `ModelicaRenderer` (`CodeReview.razor.cs:716`) with the repository's own
`FormattingOptions`. For a repository with formatting switched off the renderer still runs — the
options only control section ordering, not whether the text is rebuilt. Indentation, line wrapping,
operator spacing, annotation placement and blank lines are rebuilt every time.

Rendering each file and comparing it to its own source, line by line:

| | ModelicaStandardLibrary | Modelica-Buildings-Original |
|---|---|---|
| files sampled | 300 | 300 |
| comes back byte-identical | **3 (1.0%)** | **0 (0.0%)** |
| line *count* changes | 288 (96%) | 294 (98%) |
| total lines | 27,934 → 34,604 (**+23.9%**) | 42,295 → 49,320 (**+16.6%**) |
| line *n* of the view is line *n* of the file, same text | **5.8%** | **3.7%** |
| ...ignoring leading whitespace | 6.0% | 4.0% |

The last row is the one that matters. **Around 95% of what is on screen is not where the user's
editor puts it**, and a fifth of the lines do not exist in their file at all. This is not a
whitespace quibble; it is a different document.

The control: `C:\Projects\Modelica\MSL`, which *has* been through MLQT's formatter, comes back
**400/400 byte-identical**. The current design is correct exactly for repositories that have already
accepted MLQT's formatting — which is the assumption the whole formatting pipeline is built on, and
which is false for a user evaluating MLQT against an existing library. That is the population that
files this complaint.

## 2. The page already disagrees with itself

`LoadModelDiffAsync` (`CodeReview.razor.cs:553`) feeds `DiffViewer` the **raw** source on both sides:
`Definition.ModelicaCode` for the working copy, and the extracted class text from HEAD. Its own
comment says so — *"using the raw source code for both versions"*.

So the same class, in the same page, is shown as two different documents depending on which toolbar
button is pressed: verbatim in diff mode, rebuilt in plain mode. `ShowRawSource` is a third
representation again — the whole file from disk, HTML-encoded, with no highlighting at all, used when
a class fails to parse.

A fidelity mode does not add a representation. It **removes two**, by making the plain view agree
with the diff view and giving the parse-failure path something better than no colour at all.

## 3. The promise the renderer breaks

`Finding.LineNumber` is class-relative, against `Definition.ModelicaCode`. Its own doc comment names
the consumer:

> Surfaces that show the class — the app's code viewer, the MCP tools — want this number as it is; a
> report about files maps it with `ClassLocation`, which knows where the class starts.

The code viewer is named there as a surface that needs no mapping, and it is the one surface that
cannot use the number, because it does not show `Definition.ModelicaCode`. `ClassLocation` already
carries `LinesMapToFile` for the cases where the stored text stopped being the file's text. The
convention is in place, documented, and tested; the viewer is the one place standing outside it.

## 4. What a fidelity mode actually is

Not a second highlighter. **The same classification, emitted in place.**

The categories the renderer assigns — `KEYWORD`, `TYPE`, `IDENT`, `NAME`, `FUNCTION`, `OPERATOR`,
`NUMBER`, `STRING`, `COMMENT` — look semantic but are all positional. `TYPE` is "an `IDENT` under a
`name` reached through a `type_specifier`, or through an `import_clause`" (`ModelicaRenderer.cs:1167`,
`:1486`, `:3336`). `FUNCTION` is "an `IDENT` in a `component_reference` that a `function_call_args`
follows", suppressed inside annotations (`:3007`). Every one of those is a question about the parse
tree, which the viewer already has — `Definition.ParsedCode` is cached on the class.

So: walk the tree once recording *token index → category*, then walk the **character offsets of the
source string** emitting each token wrapped in its tag and everything between tokens verbatim. The
output is the same `<CAT>text</CAT>` markup `CodeViewer` already consumes, so `CodeViewer`,
`SyntaxHighlightingSettings`, the runtime CSS and the spell-check overlay are all unchanged.

Emitting from **character offsets, not token text**, is not a detail. A `.Text`-concatenating version
of this silently drops whatever the lexer skipped — it lost characters on 2 of 5 deliberately
malformed inputs. Driven by offsets, with the gaps copied through, the output is byte-exact by
construction whatever the lexer made of the file.

## 5. What the prototype showed

~120 lines, built against the real `ModelicaParser` (the code is in §9).

**Fidelity — every `.mo` file in both libraries, not a sample:**

| | files | lines | tags stripped == source |
|---|---|---|---|
| ModelicaStandardLibrary | 2,671 | 389,140 | **2,671 / 2,671 (100%)** |
| Modelica-Buildings-Original | 5,696 | 705,601 | **5,696 / 5,696 (100%)** |

**8,367 files, 1,094,760 lines, byte-exact, nothing thrown.**

**Agreement with the colours the page shows today** — comparing the `IDENT`/`NAME`/`TYPE`/`FUNCTION`
sequence from `ModelicaRenderer(renderForCodeEditor: true)` against the prototype's, over the same
parse tree:

| | identifier tokens | agree | residue |
|---|---|---|---|
| ModelicaStandardLibrary | 42,584 | **99.98%** | 8 |
| Modelica-Buildings-Original | 72,058 | **99.77%** | 166 |

The residue is **one known rule, in one direction**: the renderer re-enables `FUNCTION` colouring
deep inside graphics annotations (`_inGraphicsAnnotationLevel > 2`, `ModelicaRenderer.cs:3007`) and
the prototype does not track that counter. Adding it is a handful of lines. Nothing else disagreed.

An earlier version without any annotation tracking scored 98.2% / 98.6%, and the *entire* gap was
that one flag — which is worth recording, because it means the classification really is positional
and there is no hidden semantic knowledge in the renderer to reproduce.

**Cost**, `SingleGasesData.mo` (651 KB, 17,493 lines — the largest file in MSL), median of 7, warm:

| step | ms |
|---|---|
| lex only | 18 |
| parse (lex + parse) | 206 |
| **classify in place** | **73** → 279 total |
| render + highlight | 291 → 496 total |

The fidelity path is **~45% cheaper end to end** and the classify step alone is **4× cheaper** than
the render it replaces. Parse is the shared floor for both. It also produces 17–24% fewer lines, so
`CodeViewer` builds fewer DOM nodes.

**Degradation.** The lexer never failed — not on an unterminated string, not on `/* inline */` in a
position the grammar rejects, not on `@@@ not modelica at all <html>&amp;</html>`. All five malformed
inputs round-tripped byte-exact once the emitter was offset-driven. That gives the parse-failure path
a real answer: colour what lexed, show the rest verbatim, instead of today's no-colour
`ShowRawSource`.

That last case is worth dwelling on. `Real x /* inline */ = 1;` is **three parse errors** under this
grammar — `COMMENT` is on the default channel and only legal at `c_comment` positions
(`modelica.g4:43,109,115,128,369,374`). The renderer cannot show that comment. A fidelity view shows
it, because it never asks the tree what the text was.

## 6. What it would cost — the honest list

Ordered by how much thought each needs, not by lines.

**a. Multi-line tokens — mandatory, not an edge case.** A token whose text spans lines is only
0.18% of tokens, but it appears in **91% of MSL files and 99% of Buildings files**, and those tokens
cover **25–28% of all lines** (documentation strings, mostly). `CodeViewer` wraps each line in its own
`<div>` and runs its tag regex per line, so the emitter has to close and reopen the tag at every line
boundary inside such a token. Get this wrong and a quarter of the file loses its colour.

**b. The hide-annotations toggle.** **41–44% of lines sit wholly inside an annotation** and 60–62%
are touched by one, so this toggle earns its place. Today it is free — the renderer simply does not
visit them. In a verbatim view it becomes range elision: `AnnotationContext` gives exact token
bounds, so the mechanism is there, but ~19% of lines are *partly* annotation and need an in-line
edit, and dropping whole lines breaks the line-number identity that is the point of the mode. The
workable shape is: collapse each annotation to a one-line `annotation(…)` marker, drop the lines it
occupied, and keep the (monotone, trivially invertible) list of dropped source lines for navigation.
**This is the largest piece of new design in the mode** and the only place a renderer-style line map
is still needed — over a much simpler transformation than the renderer's.

**c. Which text is verbatim, and which is not.** `Definition.ModelicaCode` starts life as an exact
file slice (`ModelExtractorVisitor.GetSourceCode`), and `SourceMatchesFile` already records when it
stops being one. Two things rewrite it:

  - `ModelicaPackageSaver` / `IncrementalFormatter` after a formatted save — correct, because the
    file now says that too.
  - `PackageCodeTrimmer`, which the desktop host runs over the **whole graph** at startup
    (`MainLayout.razor.cs:808`). A package with inline standalone children ends up holding rendered,
    trimmed text.

  So the fidelity view needs a source rule: `SourceMatchesFile` → use the stored code; otherwise
  re-read the file and slice `[StartIndex..StopIndex]`, which is exactly what those fields were added
  for, and `LoadOriginalFileContents` already reads the file. 5% of MSL files and 15% of Buildings
  files hold more than one class, so for the large majority the slice is the whole file anyway.
  Packages are 19% / 33% of classes and only the ones with inline standalone children are affected.

**d. The `ElementPrefix` prepend.** `PrependElementPrefix` exists because the class slice excludes
`replaceable` / `redeclare`. Same need in the new path, same helper, but it must insert into verbatim
text rather than into a rendered first line.

**e. HTML encoding.** `ConvertLinesToHtml` encodes only the *inside* of a tag; anything the renderer
writes untagged (`.`, `(`, `,`) passes through raw. That is safe today because the renderer only ever
writes punctuation there. In the new path everything between tokens is source text, so either every
token is tagged (the prototype does this) **and** the inter-token gaps are encoded, or the gaps are
encoded on the way out. Cheap, but it is a correctness item, not a tidy-up.

**f. Where it lives, and the coverage bar.** The classifier belongs in `ModelicaParser` beside
`ModelicaRenderer` — `DiffViewer` should be able to reach it too (see §7). CLAUDE.md puts
`ModelicaParser` at **>95% per class**, which for ~200 lines of visitor is real test work, though it
is unusually easy to test: the round-trip property (*strip the tags, get the source back*) is a
one-line assertion that can be run over the whole fixture library, and the agreement property
(*same categories as `ModelicaRenderer` on already-formatted input*) pins the colours to the existing
behaviour without freezing a golden file.

**g. The user-facing control.** A per-repository setting is the natural home, beside
`ApplyFormattingRules` — "show me my code" vs "show me what the formatter would write" is the same
question as "do you want the formatter". `_showHighlighted` and the `_renderCache` key already have
room for another mode. `settings-reference.md` and `code-review.md` both change.

**Not affected:** `CodeViewer` itself, `SyntaxHighlightingSettings`, the runtime CSS, the spell-check
overlay (it matches *words*, not positions), `ModelicaRenderer`, the formatter, and every surface
below `MLQT.Services/Checking/`. Nothing in the check pipeline moves.

## 7. What this does to "the one decision"

The phase note frames three options and recommends (2), a rendered-line → source-line map inside
`ModelicaRenderer`. A fidelity mode is not a fourth option in that list — it **dissolves the
question**, and it is not what option (1) meant.

Option (1) in that note is *"stop reformatting — loses the highlighting, rejected."* The measurement
above says the second half of that sentence is not true: the highlighting does not come from the
reformat, it comes from the parse tree, and the reformat is only how the current code happens to walk
it. 99.8%+ of the colouring survives without it.

Taking the items that note groups under the decision:

| | under option (2), the renderer line map | under a fidelity mode |
|---|---|---|
| **B182** finding lines wrong in the viewer | build and maintain a line map through a 3,500-line renderer every surface shares | **no map**: `Finding.LineNumber` is already class-relative to the text on screen — identity |
| **B183** scroll to a finding's line | needs the map forwards | line number *is* the line number |
| **B185** plain source above a size threshold | the identity case of the map | already the fast path — and there is now a cheaper third tier (lex-only, 18 ms where parse is 206 ms) for files too large to parse |
| **B197** peek at a declaration | locate it in the rendered text via the map | locate it in the user's own text |
| **B178** two highlighters, one palette | unchanged: share the colour source only | unchanged for unified diff, but `DiffViewMode.SideBySideFull` shows whole documents that *are* `Definition.ModelicaCode` — the same classifier can colour that side properly instead of by regex |

The risk profile is also different in kind. Option (2) puts new state in the renderer that the
formatter, the CLI, the MCP server and `PackageCodeTrimmer` all run; a line map that drifts from what
the renderer emits is a silent wrong-line bug on every surface. The classifier is **additive** — a new
type, one new caller, and the existing renderer untouched.

**Where option (2) still earns its place:** if the "formatted" view is kept as a mode — and it should
be, it is the honest preview of what the formatter will write, and it is what a repository that *has*
accepted formatting wants — then findings shown against *that* view still need the map. The
difference is that it becomes an enhancement to a secondary mode rather than the foundation of the
page.

## 8. Where this leaves the decision

Three things are now settled by measurement rather than argument:

1. **The divergence is severe.** ~95% of displayed lines are not the user's lines; the document is
   17–24% longer. For an unformatted repository, the current view is not their code.
2. **The highlighting does not depend on the reformat.** 99.98% / 99.77% agreement, one known
   remaining rule, over 114,000 identifier tokens.
3. **It is cheaper, not dearer.** 45% less work end to end, 4× on the step it replaces, byte-exact
   over 1.09M lines, and it degrades on broken input instead of giving up.

What is *not* settled, and wants a decision before anything is built:

- **Is the formatted view kept?** Recommend yes, as a toggle, defaulting to fidelity for a repository
  with `ApplyFormattingRules` off and to formatted for one with it on. That default is the honest
  one: the formatted view is a preview of a transformation you have asked for.
- **What does hide-annotations do in fidelity mode?** §6b. Needs a shape agreed before it is coded,
  and it is the only genuinely new design in the mode.
- **Does `DiffViewer`'s side-by-side path adopt the classifier?** It can, and it would take one of the
  two highlighters out of B178's scope. It is separable and should not be allowed to grow the first
  change.

**Suggested sizing**, if it goes ahead: the classifier plus its two property tests is the bulk and is
self-contained (M); wiring it into `CodeReview` behind a mode is small (S) because the markup contract
and `CodeViewer` do not change; hide-annotations is its own piece (M) and could ship a release later
with the toggle simply disabled in fidelity mode; the `DiffViewer` side-by-side adoption is separate
(S) and optional.

Sequenced that way the first two land inside WP2's existing budget and **take B182, B183 and B185 off
the critical path instead of hanging them from a renderer change**.

## 9. Reproducing this

The harness was built in a scratch directory against `ModelicaParser.csproj` and is not in the
repository. It is reproduced here because it is the argument, and because it should be re-run before
any of this is acted on.

```csharp
// Classify every token from the parse tree, then emit the ORIGINAL source verbatim
// with each token wrapped in the tag ModelicaRenderer would have given it.
public static (List<Tok> toks, string html) Run(
    modelicaParser.Stored_definitionContext tree, BufferedTokenStream stream, string source)
{
    var cats = new Dictionary<int, string>();
    Walk(tree, cats, nameAsType: false, isFunction: false, inAnnotation: false);

    var toks = new List<Tok>();
    var sb = new StringBuilder();
    var src = ModelicaParserHelper.NormalizeLineEndings(source);
    int cursor = 0;
    for (int i = 0; i < stream.Size; i++)
    {
        var t = stream.Get(i);
        if (t.Type == TokenConstants.EOF) break;
        int a = t.StartIndex, b = t.StopIndex;
        if (a < 0 || b < a || b >= src.Length) continue;

        // Anything the lexer skipped (bad characters) is emitted verbatim and uncoloured,
        // so the view is byte-exact whatever the lexer made of the file. Concatenating
        // token.Text instead loses those characters — it did, on 2 of 5 malformed inputs.
        if (a > cursor) sb.Append(src, cursor, a - cursor);
        var text = src.Substring(a, b - a + 1);
        cursor = b + 1;

        if (t.Channel != TokenConstants.DefaultChannel) { sb.Append(text); continue; }

        var cat = cats.TryGetValue(i, out var c) ? c : Lexical(t);
        toks.Add(new Tok(i, text, cat));
        sb.Append($"<{cat}>{text}</{cat}>");
    }
    if (cursor < src.Length) sb.Append(src, cursor, src.Length - cursor);
    return (toks, sb.ToString());
}

static string Lexical(IToken t) => t.Type switch
{
    modelicaParser.STRING          => "STRING",
    modelicaParser.UNSIGNED_NUMBER => "NUMBER",
    modelicaParser.COMMENT         => "COMMENT",
    modelicaParser.LINE_COMMENT    => "COMMENT",
    modelicaParser.IDENT           => "IDENT",
    _ => (t.Text is { Length: > 0 } s && (char.IsLetter(s[0]) || s[0] == '_')) ? "KEYWORD" : "OPERATOR"
};

// The context flags mirror ModelicaRenderer's _nameAsType / _isFunction / _inAnnotation.
static void Walk(IParseTree node, Dictionary<int, string> cats,
                 bool nameAsType, bool isFunction, bool inAnnotation)
{
    if (node is ITerminalNode term)
    {
        var sym = term.Symbol;
        if (sym.TokenIndex < 0) return;
        if (sym.Type == modelicaParser.IDENT)
            cats[sym.TokenIndex] = term.Parent switch
            {
                modelicaParser.NameContext                => nameAsType ? "TYPE" : "NAME",
                modelicaParser.Component_referenceContext => (isFunction && !inAnnotation) ? "FUNCTION" : "IDENT",
                _                                        => "IDENT"
            };
        else
            cats[sym.TokenIndex] = Lexical(sym);
        return;
    }

    var ctx = node as ParserRuleContext;
    for (int i = 0; i < node.ChildCount; i++)
    {
        var child = node.GetChild(i);
        bool nat = nameAsType, fn = isFunction, ann = inAnnotation;

        if (ctx is modelicaParser.AnnotationContext) ann = true;
        if (ctx is modelicaParser.Type_specifierContext) nat = true;
        if (ctx is modelicaParser.Import_clauseContext && child is modelicaParser.NameContext) nat = true;
        if (child is modelicaParser.Component_referenceContext) fn = IsCallTarget(ctx, child);

        Walk(child, cats, nat, fn, ann);
    }
}

static bool IsCallTarget(ParserRuleContext parent, IParseTree cref) => parent switch
{
    modelicaParser.EquationContext eq => eq.function_call_args() != null,
    modelicaParser.StatementContext   => true,   // renderer sets _isFunction for call and assignment alike
    modelicaParser.PrimaryContext p   => p.function_call_args() != null,
    _ => false
};
```

Known gaps in the prototype, both named above: it does not track
`_inGraphicsAnnotationLevel > 2` (the whole of the 0.02% / 0.23% residue), and it does not split
tokens at line boundaries (§6a), which a real implementation must.

The two properties worth keeping as tests, whatever is built:

- **Round trip.** Strip the tags from the output and it equals the source, character for character.
  Run it over the fixture library; it is one assertion and it is the entire fidelity claim.
- **Agreement.** On input that is *already* formatted — where `ModelicaRenderer`'s output is
  byte-identical to its input, as `C:\Projects\Modelica\MSL` is — the classifier's category sequence
  matches the renderer's. This pins the colours to today's behaviour without a golden file, and it is
  how the one remaining rule was found.

---

# Part II — the stronger proposal, and a plan

**Added 2026-09-18**, after the question was put more sharply than §7 had framed it:

> *Run `ModelicaRenderer` on the **save path only**, when the repository has Apply Formatting on.
> Everywhere else — the code viewer, every diff view — show the bytes that are on disk (or in the
> revision) and colour them with the classifier. Feasible? Does it simplify things in the end? Would
> the diff views get the right highlighting? What are the risks?*

Part I asked whether a fidelity *mode* could exist beside the rendered one. This asks whether the
rendered one should exist on a read path at all. The four answers, short: **yes; yes, materially;
yes, and almost for free; and the risks are concentrated in three places that Part I named as small
and the survey below says are not.**

## 10. The survey — every place the renderer runs

The proposal sounds large. It is not, and the reason is worth stating first: **`ModelicaRenderer` has
exactly two callers on a read path in the whole product.**

| Caller | What it is for | Under the proposal |
|---|---|---|
| `CodeReview.razor.cs:716` | Colour the class for the viewer | **Replaced** by the classifier |
| `PackageCodeTrimmer.cs:90` (`MainLayout.razor.cs:829`, `CheckPipeline.cs:294`, `StyleTools.cs:226`) | Remove a package's inline standalone children before checking | **The one hard case — §11a** |
| `ClassQueryTools.cs:97` | Strip annotations from a class for an MCP agent | Elision (§11b) — and then an agent's line numbers match its findings too |
| `ModelicaPackageSaver`, `IncrementalFormatter` | Write formatted files | **Unchanged — this is the save path** |
| `SettingsRepositories.razor` | Nothing. A comment | — |

**Half the proposal is already true.** The save path is already gated on the toggle —
`FormattingPipeline.cs:94` and `:223`, `IncrementalFormatter.cs:51`, `MainLayout.razor.cs:1265` all
return early when `ApplyFormattingRules` is off. Nothing about writing needs to change. The proposal
is a change to reading only.

**And the formatted view is largely redundant already.** With formatting on, the file on disk *is*
the renderer's output — §1's control says so: `C:\Projects\Modelica\MSL`, which MLQT formats, renders
400/400 byte-identical. So a "formatted" mode differs from the file in only three situations:
formatting is on but *Format All Files* has never been run; a file was edited outside MLQT since; or
a formatting rule was changed a moment ago and nothing has been saved yet. That is an argument for
keeping a preview, but not for keeping it as a **mode of the code viewer**: it is a preview of a
transformation, and it belongs beside the button that performs the transformation (repository
settings, next to *Format All Files*), where its question — *what will this do to my files?* — is the
question the user is actually asking. Recommend building it there, later, or not at all; a `mlqt` run
and a diff answers it today.

## 10.1 What the proposal deletes

Not "makes optional" — deletes.

- **The per-repository `FormattingOptions` lookup in the viewer** (`CodeReview.razor.cs:711`) and with
  it the idea that a *display* has formatting settings at all.
- **`ShowRawSource`** (`CodeReview.razor.cs:836`) — the no-colour third representation. The lexer-only
  tier replaces it and colours what lexed. §5 measured that the lexer never failed, on any of five
  deliberately malformed inputs.
- **`DiffViewer.HighlightRawModelica`** and the keyword/number regexes (`DiffViewer.razor.cs:554`–`658`)
  — about 100 lines of a second, worse highlighter. This is the whole of **B178**.
- **The renderer line map that B182 was going to require.** Not deferred: never built. §7's table.
- **`RenderCacheKey`'s formatting dimension**, and much of the render cache's reason to exist — the
  classify step is 73 ms on the largest file in MSL against 291 ms for render+highlight.

Against that, one thing gets **harder**, not easier, and §11b is it.

## 11. What the survey found that Part I did not

Three things. The first two change the sizing; the third is a live defect.

### 11a. `PackageCodeTrimmer` is the second read-path renderer, and it is shared

Part I §6c mentions the trimmer as a reason the *stored* text may not be the file. That understates
it. The trimmer runs the renderer over every package with inline standalone children, sets
`SourceMatchesFile = false`, and is run by **all three surfaces** — desktop, CLI, MCP — precisely so
they check the same representation. So for those packages:

- The viewer cannot show the stored text and call it fidelity; it has to re-slice the file.
- But the **findings** were computed against the trimmed text, so their class-relative lines are
  against a document the viewer would no longer be showing. **B182 comes back for exactly this
  population** — which is why it has to be measured before anything is built (S1 below). Packages are
  19% / 33% of classes, and only those with inline standalone children are affected.
- `ClassLocation.LinesMapToFile` is already `false` for them, so every *file-based* report already
  falls back to the class declaration rather than pointing at a line. That is today's honest answer
  and it stays available.

**The clean fix is to stop rendering there too**: excise each standalone child's
`[StartIndex..StopIndex]` from the package's source and keep everything else verbatim. `ModelNode`
carries both offsets already (`ModelNode.cs:85`, `:92`). That would make the trimmed text a
*subsequence* of the file rather than a rewrite of it, restore `SourceMatchesFile` for those packages,
and improve the CLI and MCP reports — not just the viewer. It is the same drop-only mechanism §11b
needs.

**It is also the riskiest single change in the proposal**, because the trimmer feeds the check
pipeline that the GUI, `mlqt check` and the MCP server share, and finding-count parity across them is
a standing invariant (MSL = 34329). Two reassurances, neither sufficient on its own: the trimmer
passes `FormattingOptions.None`, which reorders nothing (`FormattingOptions.cs:13`–`16`), and no rule
in `RuleIds` is sensitive to whitespace or line length — the formatter-derived rules are all about
*section order*. So the prediction is that counts do not move. **Predictions of that kind are what S1
exists to replace with a number.**

### 11b. Elision is on the critical path, not a later piece

Part I §6b treats hide-annotations as "the largest piece of new design" and suggests it could ship a
release later with the toggle disabled. The survey says it cannot, because it is not one consumer but
four:

| Consumer | Today | Verbatim equivalent |
|---|---|---|
| Hide annotations (viewer) | renderer does not visit them | drop the annotation's source range |
| **Hide class definitions** — `excludeClassDefs` at `CodeReview.razor.cs:628`, **on for every package** | renderer does not visit them | drop each nested class's source range |
| MCP `get_class_source(includeAnnotations: false)` — `ClassQueryTools.cs:97` | renderer with `showAnnotations: false` | the same drop |
| The trimmer, if §11a is taken | renderer with `classNamesToExclude` | the same drop |

The second row is the one that moves the schedule: **every package opens with class definitions
hidden**, so a fidelity viewer with no elision would show a package's entire nested contents inline —
which for a top-level package is most of a library. Elision is not a toggle that can be disabled for a
release; it is how a package is displayed at all.

The compensation is that one mechanism serves all four, and it is the simplest kind: an ordered,
non-overlapping list of dropped source-line ranges, each optionally replaced by a single marker line
(`annotation(…)`). Monotone, therefore trivially invertible in both directions, and testable as a
property. This is a line map — §7 was right that one is needed — but over a **drop-only transformation
with an explicit range list**, not inferred from the emissions of a 3,500-line renderer that four
other surfaces share. That difference is the whole risk argument.

### 11c. The diff's working-copy side is already wrong for these classes

`LoadModelDiffAsync` feeds `DiffViewer` `Definition.ModelicaCode` as the working copy
(`CodeReview.razor.cs:590`–`595`) and a slice of the **file** at HEAD. For a trimmed package those two
are not comparable documents: HEAD carries the inline children, the working-copy side does not, so the
diff should be showing every standalone child as deleted. The same applies to any class whose stored
text has been through the formatter since it was read.

**This is predicted, not observed** — it needs five minutes with a package that has standalone children
and an uncommitted change. If it holds it is a bug to raise on its own, and the source rule in §6c
fixes it as a side effect: *use the stored code while `SourceMatchesFile`, otherwise re-slice the
file*.

### 11d. Two traps in the re-slice

- **Offsets and encoding.** `StartIndex`/`StopIndex` are documented as offsets into the file *read with
  Latin-1 to match the parser* (`ModelNode.cs:76`). `ModelicaFileEncoding` decodes per file, and a
  UTF-8 file with any multi-byte character decodes to a different character count. **Re-slicing must
  use the same decoder the offsets were recorded with**, or the slice is silently off by the number of
  multi-byte characters before it. A test with a `°` in a docstring ahead of the class start is the
  guard.
- **Line endings.** The round-trip claim in §5 is exact *after* `NormalizeLineEndings`. That is the
  right normalisation — `CodeViewer` works in lines — but "byte-exact" should be read as
  "character-exact modulo line endings" wherever it appears in this note.

## 12. Does it give the diff views the right highlighting?

**Yes, and it is the cheapest part of the whole proposal**, because `DiffViewer` already speaks the
markup. `ApplyModelicaSyntaxHighlighting` (`DiffViewer.razor.cs:562`) checks for `<KEYWORD>` and, when
it finds it, converts the tags to `code-*` spans exactly as `CodeViewer` does; the regex highlighter is
only the fallback for lines that arrive raw. Both diff sides are *already* raw source (§2), so they are
already the input the classifier wants.

So the work is: classify each side, hand `DiffViewer` tagged lines, and the fallback becomes
unreachable and is deleted. One new requirement — the HEAD side is a revision's text with no
`ModelNode` behind it and it may not parse — and the tiering answers it: parse tree if it parses,
lexer-only if it does not, verbatim if even that fails.

The same applies to `ChangeReview`'s file-level diff (`ChangeReview.razor.cs:316`), which is raw whole
files today and would become the first *coloured* file diff in the product. It is a separate, optional
step: whole-file classification is the same call, but its cost is per file rather than per class.

## 13. Risks and drawbacks

Ordered by what they could cost, not by likelihood.

1. **Findings still do not line up for trimmed packages** (§11a). The proposal fixes B182 for the large
   majority of classes and leaves this population exactly where it is — unless the trimmer is converted
   too, which is the riskiest change here. **Mitigation:** S1 measures the population and the parity
   before anything is built; if the trimmer cannot be converted safely, those packages keep today's
   honest fallback (`LinesMapToFile == false` → point at the class, not a line) and the viewer
   re-slices for display only, at the cost of finding lines in packages remaining unreliable. That is
   not a regression, but it is a promise not kept.
2. **Elision is real work and it is required on day one** (§11b). Four consumers, an invertible map, and
   a property test. **Mitigation:** build it once, in `ModelicaParser`, with the map as the public type;
   do not let each caller invent one — that is exactly the defect shape this repository keeps finding.
3. **Multi-line tokens** (§6a). 0.18% of tokens, 25–28% of lines, 91–99% of files. Get it wrong and a
   quarter of every file loses its colour; `CodeViewer._tagRegex` is per line and `(.*?)` does not cross
   one. **Mitigation:** it is a property, not a case — *every emitted line's tags balance* — and it can
   be asserted over the whole fixture library.
4. **Blast radius on the shared check pipeline**, if §11a is taken. **Mitigation:** the parity number is
   already the standing invariant; S1 runs it.
5. **The affordance that goes away.** No "what will the formatter do to this class?" view. For a
   repository with formatting on, the file already answers it. For one with formatting off, the answer
   is *nothing, that is what off means*. The genuine gap is "I am about to turn formatting on" — answer
   it beside *Format All Files*, not in the viewer.
6. **Presentation regressions, all of the form "the file is not laid out for a pane."** Dymola writes
   graphics annotations as single enormous lines, so horizontal scrolling appears where the renderer
   used to wrap; tabs are now the file's tabs; trailing whitespace is visible. Each is arguably correct
   — it is what the editor shows — but it is a visible change, and the hide-annotations default carries
   most of it (41–44% of lines are wholly inside an annotation).
7. **Encoding and offsets** (§11d). Silent, and off by a few characters, which is the worst kind.
8. **Test debt.** `MLQT.Shared.Tests` has exactly one test file over this area
   (`Components/CodeViewerHtmlTests.cs`); `CodeReview` itself has no component tests. Little to rework,
   nothing to lean on. The classifier lands in `ModelicaParser`, where the bar is **>95% per class**.
9. **Scope.** The proposal touches the viewer, both diff views, the MCP source tool and possibly the
   check pipeline. **Mitigation:** the staging below makes every step shippable alone, and S5/S6 are
   explicitly optional.

**What it is not a risk to:** `CodeViewer`, `SyntaxHighlightingSettings`, the runtime CSS, the
spell-check overlay, `ModelicaRenderer` itself, the formatter, and every rule and analysis. The markup
contract does not change.

## 14. The plan

Each stage ends somewhere the product is shippable. Gates are named because two of them can send the
plan back to Part I's milder shape.

| | Stage | Size | Gate |
|---|---|---|---|
| **S0** | Re-run §9's harness with the two known gaps closed: the `_inGraphicsAnnotationLevel > 2` counter, and per-line token splitting | S | Round trip 100% on both libraries; agreement ≥ 99.9%. **If agreement falls, stop** — the classification is not as positional as §5 says |
| **S1** | **Measure, before any code.** (i) How many classes carry `SourceMatchesFile == false` after a normal load of MSL and Buildings. (ii) Prototype the excision trimmer and run `mlqt check` over MSL: is the count still 34329, and does any finding's line move other than by the removed ranges? | S | (ii) clean → S6 is in scope. (ii) dirty → S6 is dropped and §13.1's fallback is what ships |
| **S2** | **`ModelicaTokenClassifier`** in `ModelicaParser`, beside `ModelicaRenderer`. Offset-driven; three tiers (parse tree → lexer only → verbatim); per-line tag splitting; inter-token gaps HTML-safe (§6e) | **M** | Two property tests over the fixture library: **round trip** (strip tags == source) and **agreement** (categories match `ModelicaRenderer` on already-formatted input). >95% class coverage, and a `run-mutation.ps1 -Mutate` pass over the new file — it is exactly the kind of code that was added for |
| **S3** | **`SourceElision`** — ordered dropped line ranges with optional marker lines, `ToSourceLine`/`ToDisplayLine`. One type, four consumers (§11b) | **M** | Invertibility as a property; a package renders with its nested classes collapsed and the map round-trips every displayed line |
| **S4** | **Wire `CodeReview`**: the source rule (stored while `SourceMatchesFile`, else re-slice by offsets with the matching decoder), classifier in place of `ModelicaRenderer`, `ShowRawSource` deleted, `PrependElementPrefix` inserting into verbatim text, findings mapped by identity or through the elision map | **S–M** | **B182, B183 and B185 close here**, and the first component tests for the page arrive with them |
| **S5** | **`DiffViewer` adopts the classifier** on both sides; `HighlightRawModelica` deleted | S | **B178 closes.** `ChangeReview`'s whole-file diff is a further optional step |
| **S6** | **The excision trimmer** (conditional on S1) | M | Parity holds; `SourceMatchesFile` becomes true for the converted packages; CLI and MCP line numbers improve with no change to either |

**Ordering.** S0 and S1 are a day between them and they are the whole of the risk. S2 and S3 are
independent of each other and can be built in either order or together; S4 needs both. S5 needs only
S2. S6 needs only S1's verdict and can be taken at any point after it — including much later.

**Against WP2's existing order**, this replaces steps 1–4 and 7 (measure, the B182 map, B183, B185,
B178) and leaves 5, 6 and 8 (panes, search, reveal-in-tree and the navigation stack) untouched — except
that B197's peek now lands in the user's own text, which is the point of it.

**Backlog.** New ids start at **B213**. The plan wants at least: the classifier, the elision type, the
viewer wiring, the diff adoption, the trimmer conversion, and — if §11c holds — the diff-of-a-trimmed-
package defect. This note is retired when S4 lands, with the durable parts moving into the classifier's
own documentation and a skill file.

**Documentation.** `code-review.md` (what the viewer shows, and that it is the file),
`settings-reference.md` (the formatting toggle no longer affects display), `code-formatting.md`
(formatting is a save-time transformation, full stop), and `mcp-server.md` if `get_class_source`
changes. The generated screenshots for Code Review are regenerated by `DocumentationScreenshots`, and
**their captions are the specification** — the captions are what to check first when the pictures
change.

## 15. What still needs deciding

Part I's three questions, answered under this proposal, plus one new one.

1. **Is the formatted view kept?** — **No, not as a viewer mode.** §10. Build a preview beside *Format
   All Files* if one is wanted at all. This is the decision that most wants confirming, because it is
   the one that cannot be undone cheaply once the mode is deleted.
2. **What does hide-annotations do?** — Range elision, and it is neither optional nor deferrable,
   because hiding class definitions in a package is the same mechanism and is on by default. §11b.
3. **Does `DiffViewer` adopt the classifier?** — **Yes.** It is S5, it is small, and it deletes more
   than it adds. §12.
4. **New: is the trimmer converted?** — S1 decides it with a number. Everything else in the plan works
   either way; only the promise in §13.1 changes.

---

# Part III — S0 and S1, run

**2026-09-19. Both gates pass, and the plan changes in four places.** The harnesses were built in a
scratch directory against `ModelicaParser.csproj` and `MLQT.Services.csproj`; what is durable about
them is reproduced below, in the same spirit as §9.

## 16. S0 — the classifier against the renderer

Every `.mo` file in both libraries, not a sample. Two properties, measured together.

| | ModelicaStandardLibrary | Modelica-Buildings-Original |
|---|---|---|
| files | 2,671 | 5,696 |
| lines | 391,811 | 711,297 |
| **round trip** (strip the tags, get the source) | **2,671 / 2,671 (100%)** | **5,696 / 5,696 (100%)** |
| files that threw | 0 | 0 |
| word tokens compared | 783,787 | 1,417,097 |
| **agreement**, renderer defects mirrored | **99.9989%** (residue 9) | **99.9984%** (residue 22) |
| **agreement**, not mirrored | 99.9519% (residue 377) | 99.9639% (residue 512) |
| misaligned tokens | **0** | **0** |

**Gate: PASS**, on both readings and both libraries. Round trip is exact over **8,367 files and
1,103,108 lines**.

Three things this run established that §5's prototype did not.

**a. The comparison has to be made at the same granularity, and over keywords too.** The renderer
emits a dotted name as one tag — `<TYPE>Modelica.Icons.IconsPackage</TYPE>` — where the classifier
tags each `IDENT`. Compared naively that reports every dotted name as a divergence and then loses
alignment for the rest of the file; §5's "98.2% before the annotation flag" was partly this.
Splitting the renderer's tag on the dots that separate names — *not* on a dot inside a quoted
identifier, and `ModelicaReference` really does contain a class called `'Connections.branch()'` —
brings misalignment to zero. Keywords belong in the comparison as well: that is what found (b).

**b. `der`, `initial` and `pure` are coloured as function calls.** They are keywords, not
identifiers, so a lexical fallback calls them `KEYWORD`; the renderer writes `FunctionCall` for them
in `primary: (component_reference | 'der' | 'initial' | 'pure') function_call_args`
(`ModelicaRenderer.cs:2899`), and they *are* calls. An identifier-only comparison cannot see this at
all.

**c. The graphics-annotation level has three increment sites, not one.** §6/§9 named
`_inGraphicsAnnotationLevel` as a single counter to add. It is incremented in three places, one level
per nested *argument*:

| | |
|---|---|
| `VisitFunction_arguments` :3066 | around the first positional expression |
| `VisitFunction_argument` :3149 | around each later one — **and this is how a *named* argument's value gets one**, since `named_argument : IDENT '=' function_argument` |
| `VisitArray_arguments` :3195 | around each array element |

The middle one is the one that matters: `DynamicSelect` inside
`Ellipse(lineColor=DynamicSelect(…))` is coloured as a function because its named argument's value
is a `function_argument`. With only the first site implemented, agreement was 99.59% and every
disagreement was a `DynamicSelect`.

### 16.1 The residue is two renderer defects, and the classifier is right in both

Nothing else disagreed. Both facets are the same field, `_isFunction`, being a mutable field rather
than a scoped fact:

- **It leaks into array subscripts.** `VisitComponent_reference` visits the reference's
  `array_subscripts` while `_isFunction` is still set (`:3007`), so in `den2[i] := …` the subscript
  `i` is coloured as a function call. **377 tokens in MSL, 512 in Buildings.**
- **A nested call then clears it for the rest of the reference.** In `m[integer(i),j] := …` the
  inner `integer(…)` sets the flag and clears it on the way out, so `j` — and everything after it in
  that same reference — loses the colouring the leak had given it. **9 tokens in MSL, 22 in
  Buildings**, and these are the ones that remain when the leak is mirrored.

They are worth **B232** on their own. The classifier should not reproduce either: a subscript is not
a call. That is a deliberate, tiny, visible change — 889 tokens across 1.1M lines — and it should be
stated in the commit rather than discovered.

## 17. S1 — the trimmed-package population, the excision trimmer, and the re-slice

### 17.1 How big the problem is, and how much of it is self-inflicted

| after a normal load + `PackageCodeTrimmer` | MSL `Modelica` | `Buildings` |
|---|---|---|
| classes | 6,487 | 7,510 |
| packages | 844 (13.0%) | 2,180 (29.0%) |
| classes with `SourceMatchesFile == false` **before** trimming | 0 | 0 |
| ...**after** | **688 (10.61%)** | **1,235 (16.44%)** |
| of those, rewritten with **no inline child to remove at all** | **377 (55%)** | **1,172 (95%)** |
| ...whose text got *longer* | 303 | 1,073 |
| time | 1,342 ms | 203 ms |

So **10.6% / 16.4% of classes lose their line mapping** — that is the population §11a predicted and
the size of what B215 alone cannot fix. And **the majority of it buys nothing**: a package whose
standalone children are already in their own files has nothing inline to trim, but the trimmer
renders it anyway, marks `SourceMatchesFile` false, and in Buildings makes 1,073 packages *longer*
than the file they came from. In Buildings **95%** of the damage is of that kind.

That is a defect in its own right, it is independent of B216, and it is a guard clause:
**B230**.

### 17.2 The excision trimmer

Same selection rule, excising each inline child's `[StartIndex..StopIndex]` instead of re-rendering.
Only children sharing the package's `ContainingFileId` are candidates — a child in its own file has
offsets into *that* file, and comparing them against this package's range is not merely useless but
can excise a range that is not a class at all.

| | MSL | Buildings |
|---|---|---|
| packages actually trimmed | 311 | 63 |
| time | **7 ms** (vs 1,342) | **9 ms** (vs 203) |
| `SourceMatchesFile` false afterwards | **0** | **0** |
| result still a subsequence of the file | 311 / 311 | 63 / 63 |
| **line map exact** | **311 / 311 packages** | **63 / 63 packages** |
| lines checked / wrong | 24,997 / **0** | 5,602 / **0** |

"The lines still map" is not an argument here, it is 30,599 lines checked one at a time: stored line
*k* is file line `StartLine + k - 1 + (lines removed above k)`, with the same text on it. The only
divergence is the **first** line of a nested class, which carries none of the file's indentation
because the slice begins at the class keyword — true of every class slice, nothing to do with
excision.

### 17.3 Parity — the gate for B216

Both trimmers, same settings (every configurable rule on), same models, same session.

| | MSL | Buildings |
|---|---|---|
| findings, render-trim | 26,557 | 31,823 |
| findings, excise-trim | **26,559** | **31,823** |
| delta | **+2** | **0** |
| findings lost | **0** | **0** |
| same finding, same line | 24,387 | 28,590 |
| same finding, line moved | 828 | — |

**Nothing is lost and two things appear**, both `MLQT.Style.OneOfEachSection`
(`Modelica.Media.Air.ReferenceAir`, `Modelica.Media.Common`). The cause is visible and benign:
excision leaves behind a section header whose only contents were standalone children, where
re-rendering dropped the now-empty section. The file really does have two `public` sections, so the
new findings are **correct** — but they are a +2 drift a baseline will report, and B216 must say so.

The 828 moved lines are **the fix, not a regression**: under excision a finding's line is the file's
line. That is the whole point, and it is why B216 cannot be judged by "the count did not move".

**Gate: PASS. B216 is in scope**, with a baseline-drift note.

### 17.4 The re-slice rule in §6c and §11d is wrong as written

B215's fallback — "re-read the file and slice `[StartIndex..StopIndex]`" — **does not work at all**:

| slicing the class out of its file | MSL | Buildings |
|---|---|---|
| file text as read, `[Start..Stop]` | **0 / 6,487** | **0 / 7,510** |
| line endings normalised, `[Start..Stop+1]` | **6,454 / 6,487 (99.49%)** | **7,388 / 7,510 (98.38%)** |

Two corrections, and the first is the dangerous one:

- **The offsets are into the line-ending-normalised text**, not the file as read. Every `.mo` file in
  both libraries is CRLF, and `PreprocessCode` normalises before the lexer ever assigns an offset, so
  slicing the file as read drifts by one character per line above the class. It is not slightly
  wrong: for a class 5,000 lines down a file the slice lands in the middle of some other class.
  §11d worried about multi-byte characters; the line endings are a much larger version of the same
  mistake, and every file has them.
- **`StopIndex` is one short of the class's last character in practice** — `[Start..Stop+1]` is the
  slice that matches. The field documents itself as the inclusive offset of the last character
  (`ModelNode.cs:90`); it is not, and a comment that is wrong about an offset is worth **B231**.

The 33 / 122 remaining are **short class definitions** (`model Cylinder = Cylinder_analytic_CAD;`),
where the stored text carries a trailing `;` the slice does not. A named case, not a mystery.

## 18. What the measurements change

Four things, none of them the shape of the plan.

1. **Both gates pass.** §14's S0 and S1 are done; B213 is unblocked and B216 is in scope.
2. **B213 grew a little and shrank a little.** Three increment sites rather than one, plus
   `der`/`initial`/`pure`, plus the dotted-name granularity rule — all of them in the *comparison*
   and the *walk*, none of them in the emitter. The emitter is unchanged from §9 and was exact first
   time, on 1.1M lines.
3. **B230 is new and is the cheapest thing in the package**: stop re-rendering a package that has no
   inline child. It removes 55% / 95% of the trimmed-package population on its own, before B216
   removes the rest, and it is a guard clause in front of an existing render.
4. **B215's source rule must be rewritten before it is implemented** (§17.4). As specified it would
   have produced a viewer that shows the wrong class entirely, on a CRLF library — which is all of
   them — and it would have looked like a classifier bug.

**Two new defects found on the way**, neither of which the plan was looking for: **B231** (the
offset documentation) and **B232** (the renderer's `_isFunction` leak). Both are recorded rather than
fixed here.

### 18.1 One thing B213 should know about parsing

`ModelDefinition` caches the parse tree but not the token stream, and the classifier needs both — the
tree for the categories, the stream for the offsets and for the characters the lexer skipped. The
viewer re-parses today (`CodeReview.razor.cs:713`), so parsing again is not a regression, and §5
measured the whole fidelity path as ~45% cheaper end to end regardless. But there is a cheaper
option worth trying: **lex alone (18 ms) and reuse the cached tree (206 ms saved)**, since the token
indices agree as long as both come from the same `PreprocessCode` output. Measure it before assuming
it; the correctness of the categories does not depend on it.
