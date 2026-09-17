# Analysis — a viewer mode that preserves the original formatting

**Status: analysis only. Nothing here is decided, and no code has been changed.** Written on
2026-09-17 to feed the decision recorded as "the one decision that shapes the phase" in
`phase-1-release-feedback.md`, which is being edited elsewhere. Kept separate on purpose; if the
recommendation here is accepted, that note's section is what changes, and this file is retired.

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
