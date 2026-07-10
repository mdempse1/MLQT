using System.ComponentModel;
using ModelContextProtocol.Server;
using MLQT.McpServer.Dtos;

namespace MLQT.McpServer.Tools;

/// <summary>
/// On-demand guidance for using this server effectively. Keeps per-tool descriptions short while
/// giving an agent a place to learn the intended sequencing and workflows.
/// </summary>
[McpServerToolType]
public sealed class GuidanceTools
{
    private static readonly IReadOnlyList<string> Topics =
        ["overview", "workflows", "dependencies", "style", "spelling", "formatting", "vcs", "resources"];

    private static readonly Dictionary<string, string> Guidance = new(StringComparer.OrdinalIgnoreCase)
    {
        ["overview"] = """
            MLQT MCP server — manage and analyse Modelica libraries under revision control.

            Core concepts:
            - Load first. Almost every tool operates on an in-memory graph. Use load_repository for a
              Git/SVN working copy or directory of libraries, or load_library for a single library
              directory (with package.mo) or .mo file. list_libraries shows what is loaded.
            - Class ids are fully-qualified dotted names, e.g. 'Modelica.Blocks.Continuous.Integrator'.
              Use search_classes to find one when you don't know its full path.
            - Analysis is opt-in. Loading only parses structure. Dependency edges, impact and external
              resources need analyze_dependencies to be run first (it can be slow on a big library).
              Style checking is also opt-in via check_class / check_library.
            - Generic git/svn operations (commit, log, push, branch) are intentionally NOT provided —
              use your normal CLI for those. The two VCS tools here add Modelica-awareness the CLI lacks.

            Call get_guidance with a topic for recipes: workflows, dependencies, style, spelling,
            formatting, vcs, resources.
            """,

        ["workflows"] = """
            Common workflows:

            Explore a library:
              load_repository / load_library -> get_package_tree or list_classes / search_classes
              -> get_class_info -> get_class_source (include_annotations=false for compact structural code).

            Understand impact of changing a class:
              analyze_dependencies (once) -> find_usages (direct dependents) or
              analyze_impact (full transitive blast radius).

            Review uncommitted changes before committing (via your CLI):
              load_repository -> get_changed_classes (what classes did I touch?) ->
              analyze_dependencies -> analyze_change_impact (what downstream is affected?).

            Quality pass on a library:
              get_style_settings -> enable rules -> check_library -> list_issues.
              Parse errors are available from list_issues immediately after loading (no check needed).

            Fix a spelling mistake:
              spell_check -> spelling_suggestions -> correct_spelling.
            """,

        ["dependencies"] = """
            Dependency, usage and impact:
            - Run analyze_dependencies ONCE after loading (opt-in; parses everything). Re-run after
              loading more libraries.
            - get_dependencies(classId): what a class directly uses (one hop).
            - find_usages(classId): direct dependents — who uses this class (one hop).
            - analyze_impact(classIds): full transitive set of classes that depend on the given
              class(es) — the complete blast radius, with the immediate source that pulled each in.
            - Empty results carry a dependenciesAnalyzed flag: false means "not analyzed yet",
              true means "genuinely none".
            """,

        ["style"] = """
            Style / quality checking (opt-in):
            - get_style_settings returns the current rule toggles (all default off). Flip the ones you
              want and pass the whole object as 'settings' to a check tool to re-check with new rules.
            - check_style(source, settings): stateless, for a snippet. Reference-validation and icon
              rules need a loaded library and are inert here.
            - check_class(classId, settings) / check_library(libraryId?, settings): check loaded models;
              results are stored and appear in list_issues.
            - list_issues aggregates parse errors (available at load) plus style/spell violations from
              any check that has run. Filter by severity / source / classId.
            """,

        ["spelling"] = """
            Spell checking of description and Documentation prose:
            - spell_check(classId | source): list misspelled words with line numbers. Covers class and
              component descriptions and Documentation info/revisions.
            - spelling_suggestions(word): ranked corrections from the bundled en_US/en_GB dictionaries
              plus your custom dictionary.
            - correct_spelling(classId, oldWord, newWord): whole-word, case-sensitive replacement across
              the file's prose (HTML tags, hrefs and code/pre blocks are preserved). By default it
              writes the file to disk and refreshes the graph; pass preview=true to just see the result.
            """,

        ["formatting"] = """
            Formatting:
            - format_code(source, ...): stateless, returns formatted text; nothing written.
            - format_class(classId, ..., preview?): formats the .mo file that contains the class and,
              unless preview=true, writes it to disk and refreshes the graph. Note this reformats the
              whole containing file (all classes stored in it), matching how MLQT saves files.
            - Ordering options: oneOfEachSection, importStatementsFirst, componentsBeforeClasses.
            """,

        ["vcs"] = """
            Modelica-aware version control (read-only):
            - get_changed_classes(repositoryId, revision?): maps changed .mo files to the classes they
              contain. No revision = uncommitted working copy; a revision = that commit's changes.
            - analyze_change_impact(repositoryId, revision?): changed classes -> full transitive impact.
              Requires analyze_dependencies.
            These bridge a diff to the semantic graph. For commit/log/push/branch, use your git/svn CLI.
            """,

        ["resources"] = """
            External resources (data files, C sources/libraries, images, directories):
            - Requires analyze_dependencies (it populates the resource graph and warnings).
            - get_class_resources(classId): resources a class references, with resolved paths and
              whether each file exists.
            - find_resource_usages(resolvedFilePath): reverse lookup — which classes use a resource.
            - get_resource_warnings(): missing files and non-portable absolute-path references.
            """,
    };

    [McpServerTool(Name = "get_guidance")]
    [Description("Get guidance on how to use this server's tools effectively. Call with no topic for an " +
                "overview and the list of topics, or a topic for focused recipes. Topics: overview, " +
                "workflows, dependencies, style, spelling, formatting, vcs, resources.")]
    public object GetGuidance(
        [Description("Optional topic. Omit for the overview. One of: overview, workflows, dependencies, " +
                     "style, spelling, formatting, vcs, resources.")]
        string? topic = null)
    {
        var key = string.IsNullOrWhiteSpace(topic) ? "overview" : topic.Trim();
        if (!Guidance.TryGetValue(key, out var text))
            return new ToolError($"Unknown topic '{topic}'. Available topics: {string.Join(", ", Topics)}.");

        return new { topic = key.ToLowerInvariant(), guidance = text, availableTopics = Topics };
    }
}
