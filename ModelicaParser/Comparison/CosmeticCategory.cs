namespace ModelicaParser.Comparison;

/// <summary>
/// What a display-only annotation is about. Every name <see cref="SimulationAnnotations"/> vouches
/// for has exactly one, and a <see cref="ClassSignature"/> keeps a class's dropped annotations per
/// category, so a comparison can say <i>which</i> kind of cosmetic change an edit was.
/// </summary>
public enum CosmeticCategory
{
    /// <summary>Icon and diagram layers, placements and graphical primitives.</summary>
    Graphics,

    /// <summary><c>Documentation</c>, revisions, <c>obsolete</c> and the wording of diagnostics.</summary>
    Documentation,

    /// <summary>What a tool's parameter dialogs, menus and browsers offer.</summary>
    Dialog,

    /// <summary>Library bookkeeping a tool rewrites on save, and MLQT's own directives.</summary>
    Tooling
}
