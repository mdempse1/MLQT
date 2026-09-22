namespace ModelicaParser.Visitors;

/// <summary>
/// The layout decisions <see cref="ModelicaRenderer"/> makes when it writes a class out: which
/// sections it may merge, and what order it puts things in.
///
/// <para>They travel together because they are one decision — a repository's layout convention — and
/// because they were previously threaded through a dozen signatures as parallel <c>bool</c>s, which
/// meant every new option was a change to every method between the settings and the renderer, and a
/// call site that passed them in the wrong order compiled perfectly. Adding
/// <see cref="InitialSectionsLast"/> is what made that cost visible.</para>
///
/// <para>All of them default to off, which is the renderer's "leave the source as it is" behaviour: with
/// <see cref="OneOfEachSection"/> off it does not reorder anything at all, so the rest have no effect.
/// That is why <see cref="None"/> is the right thing to pass wherever the caller only wants the text
/// re-rendered rather than reformatted.</para>
/// </summary>
/// <param name="OneOfEachSection">
/// Merge multiple <c>public</c>/<c>protected</c>/<c>equation</c> sections into one of each. This is
/// also the master switch for reordering: with it off the renderer writes the composition in source
/// order and the other three options do nothing.
/// </param>
/// <param name="ImportsFirst">Put <c>import</c> statements first in each section, then <c>extends</c>.</param>
/// <param name="ComponentsBeforeClasses">
/// Put component declarations before nested class definitions. A formatter-only choice — no rule
/// checks it, so it never produces a finding and CI cannot see it.
///
/// <para><b>It refines <see cref="ImportsFirst"/> rather than competing with it.</b> The renderer
/// reads this option only inside the branch <see cref="ImportsFirst"/> selects, so with imports-first
/// off it changes nothing at all. This was documented here, and in two places in
/// <c>settings-reference.md</c>, as "mutually exclusive with ImportsFirst in the settings UI" —
/// wrong twice over: the dialog enforces no exclusion between the two (unlike the initial-section
/// pair, which really does turn its opposite off), and nesting is a dependency, not an exclusion.
/// Every renderer test passes the two together for that reason.</para>
/// </param>
/// <param name="DeclarationOrder">
/// Within the component group, write inputs and outputs first, then constants, then parameters,
/// then variables, then components — the finer order <see cref="ComponentsBeforeClasses"/> is the
/// last boundary of.
///
/// <para><b>It refines <see cref="ComponentsBeforeClasses"/> the way that refines
/// <see cref="ImportsFirst"/>.</b> The renderer reads this option only inside the branch that one
/// selects, so on its own it changes nothing. Telling a variable from a component needs the
/// declared type resolved, which needs a graph, so a renderer given no <c>isSimpleType</c> callback
/// recognises only the predefined types as quantities — and <c>MLQT.Style.DeclarationOrder</c> is
/// given the same callback, so the two always agree about where a declaration belongs.</para>
/// </param>
/// <param name="InitialSectionsLast">
/// Write <c>initial equation</c>/<c>initial algorithm</c> after the ordinary equation and algorithm
/// sections rather than before them. Off means before, which is the convention
/// <c>MLQT.Style.InitialEqAlgoFirst</c> checks; on matches <c>MLQT.Style.InitialEqAlgoLast</c>. The
/// two rules are mutually exclusive, so at most one of them is ever enabled.
/// </param>
public sealed record FormattingOptions(
    bool OneOfEachSection = false,
    bool ImportsFirst = false,
    bool ComponentsBeforeClasses = false,
    bool InitialSectionsLast = false,
    bool DeclarationOrder = false)
{
    /// <summary>Reorder nothing — render the class as it is written.</summary>
    public static readonly FormattingOptions None = new();
}
