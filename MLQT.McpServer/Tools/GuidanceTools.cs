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
        ["overview", "workflows", "views", "editing", "dependencies", "style", "spelling", "formatting", "vcs", "resources"];

    private static readonly Dictionary<string, string> Guidance = new(StringComparer.OrdinalIgnoreCase)
    {
        ["overview"] = """
            MLQT MCP server — author, check, format and analyse Modelica code. Its focus is editing and
            quality of the .mo files; generic Git/SVN work is left to your own CLI.

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

            Call get_guidance with a topic for recipes: workflows, views, editing, dependencies, style,
            spelling, formatting, vcs, resources.
            """,

        ["workflows"] = """
            Common workflows:

            Explore a library:
              load_repository / load_library -> get_package_tree or list_classes / search_classes
              -> get_class_info -> get_class_source (include_annotations=false for compact structural code).

            Find a class when you don't know its name:
              search_classes (by id substring), search_text (by description/documentation prose, e.g.
              'PID controller'), or search_by_interface (by shape: class type, has connectors/parameters,
              simulatable via an experiment annotation).

            Understand how to USE a class without reading its source (cheap):
              get_class_interface (parameters, connectors, function signature) and get_class_documentation
              (prose). list_class_elements for the full declaration list. This is far smaller than
              get_class_source — see the 'views' topic.

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

            Edit a class and re-check it:
              get_class_source (read) -> update_class_source (write the new source, same class name;
              validated + verbatim) -> optionally format_class -> check_class + spell_check (re-check just
              that class).

            Rename a class (updates references too):
              analyze_dependencies (once) -> rename_class(classId, newName) [preview first] -> the
              declaration and every resolved reference are rewritten and dependencies refreshed. Precise:
              a same-named unrelated class is not touched. Read-only files abort the rename.

            Start a new library/project:
              create_library(name, directory) writes an empty top-level library on disk (package.mo +
              package.order) and loads it -> add classes with create_class(name, source) -> add nested
              packages the same way (create_class with a 'package ... end ...;' source). Storage defaults to
              one class per file: a class becomes its own .mo file and a sub-package becomes its own
              directory package (folder + package.mo), so the structure stays one-per-file all the way down.
              Classes that cannot be standalone (a replaceable/redeclare/inner/outer prefix) are nested.

            Author a new class in your library:
              get_class_interface on the classes you'll reference (learn their API) -> create_class(parentId,
              source) -> check_class + spell_check + validate_class_references (validate) -> format_class.
              A directory package gets a standalone .mo file; otherwise the class is nested.

            Build up a model incrementally (surgical edits — no need to resend the whole class):
              create_class -> add_component (instantiate parts, e.g. a source and an integrator) ->
              add_connection(model, 'source1.y', 'integrator1.u') [refuses incompatible connectors] ->
              set_component_modifier to set parameters -> add_equation/add_statement for behaviour ->
              list_connections to review. Also add_extends / add_import / remove_component / remove_connection.
              Each edit is parse-checked and refreshes dependencies; use preview=true to see the result first.
              To build a whole model in one shot, pass the same operations to batch_edit — they apply
              atomically (all-or-nothing) and later ops see earlier ones (add components then connect them).
              Give components positions with set_component_placement so the diagram is usable
              (get_diagram_layout shows the current arrangement); there is no auto-layout.
              Document as you go: description strings come from create_class source, add_component's
              description, or set_class_description / set_component_description; set the rich HTML help with
              set_class_documentation; the add_* tools take an optional comment to place a // note above the
              element.

            Restructure a library:
              analyze_dependencies (once) -> move_class(classId, newParentId) re-qualifies references to the
              moved class (its own refs to former siblings are reported, not auto-fixed); delete_class(classId)
              removes a class and reports what still references it. Use preview=true first.
            """,

        ["views"] = """
            Class "views" — compact projections so you don't have to read full source. All need only a
            loaded library (not analyze_dependencies):
            - get_class_interface(classId): the PUBLIC interface — settable parameters (name/type/default/
              description), connectors (with causality input/output and flow/stream), extends base classes,
              and for a function its input/output signature. The best first call to learn how to USE a class.
              Members INHERITED via extends are merged in and each marked with its base class in
              inheritedFrom (e.g. Integrator's u/y connectors come from Interfaces.SISO) — you get the whole
              picture without chasing base classes; pass include_inherited=false for own declarations only.
              Parameter defaults reflect extends-clause modifications, so the value shown is the effective
              one (extends Base(k = 10) reports k's default as 10, not the base's).
              A component counts as a connector if it has a causality or its type resolves to a loaded
              connector class (best-effort — load the type's library too).
            - list_class_elements(classId, includeProtected?, includeInherited?): every element (components,
              extends, imports, nested classes) with full detail; inherited members included by default and
              marked with inheritedFrom. Public only unless includeProtected=true.
            - get_class_documentation(classId, format=text|html): the class description plus the
              Documentation(info/revisions) prose. text strips HTML; html returns it raw.
            - get_class_behavior(classId): the class's OWN equations, connect() statements and algorithm
              statements. Inherited behaviour is NOT merged (unlike members) — basesWithBehavior points to
              the base classes that declare behaviour, query those directly for the full picture.
            - validate_class_references(classId): lists referenced types (component types + extends) that do
              not resolve to a loaded class — catches typos and missing dependencies after writing/editing.
              Best-effort: it does not model names inherited via extends, so treat hits as candidates and
              make sure referenced libraries are loaded.
            """,

        ["editing"] = """
            Editing Modelica on disk. EVERY editing tool: writes the .mo file, parse-checks the result and
            makes NO change if it would not parse, refuses read-only files (e.g. a reference library under
            Program Files), refreshes dependencies, and supports preview=true to see the result first.

            Start a project:
            - create_library(name, directory): write and load a new, empty top-level library on disk (the
              first step of a new project). Then add classes with create_class.

            Whole-class:
            - create_class(parentId, source): add a class from complete source; a directory package gets a
              standalone .mo file, otherwise it is nested (force with standalone true/false).
            - update_class_source(classId, newSource): replace a class's body verbatim, SAME name (to
              rename, use rename_class). Not reformatted — run format_class after if you want.
            - rename_class(classId, newName): rename the class AND rewrite every resolved reference; precise
              (a same-named unrelated class is untouched); whole directory packages too. Needs
              analyze_dependencies.
            - move_class(classId, newParentId): move to a new parent and re-qualify references (whole
              directory packages too). Needs analyze_dependencies. The moved class's own references to
              former siblings are reported, not auto-fixed.
            - delete_class(classId): remove a class (or a whole directory package) and report what still
              references it.

            Element-level (surgical — change one thing without resending the class):
            - add_component / remove_component / set_component_modifier (components, parameters, connectors).
              add_component covers the full declaration: visibility='protected' (a protected section is
              created if absent); prefix for keywords like 'parameter', 'constant', 'replaceable', 'final',
              'inner'/'outer', 'flow'/'stream' (space-separated in Modelica order); constrainedBy for a
              replaceable's constraining clause; condition for a conditional 'if <expr>' component. Pass the
              Modelica keywords you already know — the tool just places the declaration correctly.
              Restricted-class rules are enforced across the add_* tools: equations/connect() only in a
              model/block/class; algorithm statements also in a function; a package holds only constants; a
              function's public components must be input/output (locals go in protected); a record takes only
              public data; a block's connectors must be causal (RealInput/RealOutput, not acausal physical
              connectors). The tool refuses an illegal addition with a message saying what is allowed.
            - add_extends / add_import.
            - add_equation / add_statement (algorithm section).
            - add_connection(classId, portA, portB) / remove_connection / list_connections. add_connection
              resolves both ports' connector types and REFUSES incompatible ones (RealOutput->RealInput is
              fine; a signal port to a physical Pin is not).
            - The add_* tools take an optional comment placed as a // line above the element.

            Build a whole model atomically:
            - batch_edit(operations[]): apply a sequence of the surgical ops all-or-nothing; later ops see
              earlier ones (add components, then connect them). Any failure rolls the whole batch back.

            Documentation & comments:
            - set_class_description / set_component_description (the "..." strings), set_class_documentation
              (Documentation(info/revisions) HTML; read with get_class_documentation). Descriptions and //
              comments also survive create_class/update_class_source verbatim and appear in the views.

            Diagram layout:
            - set_component_placement(classId, name, x1,y1,x2,y2, rotation?) positions a component;
              get_diagram_layout shows the arrangement. No auto-layout — set placements explicitly.

            After an external change (a manual edit or VCS pull), reload(target?) re-reads from disk. Then
            validate: check_class + spell_check + validate_class_references. Type/unit/equation-balance and
            simulation are out of scope here — use a Modelica compiler/simulation tool for those.
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
            - After analyze_dependencies has run, all the editing tools (see the 'editing' topic)
              incrementally refresh the dependency graph for the files they change, so these queries stay
              current after an edit without a full re-analysis.
            """,

        ["style"] = """
            Style / quality checking (opt-in). Rules are PER-REPOSITORY:
            - Settings live in each repository's .mlqt/settings.json (the same file MLQT uses), loaded by
              load_repository. get_style_settings(repositoryId?) reads them; set_style_settings(settings,
              repositoryId?) updates the rule toggles + spell languages and writes them back to
              .mlqt/settings.json. repositoryId is optional when one repo is loaded.
            - check_class(classId, settings?) / check_library(libraryId?, settings?): by default use the
              repository's settings; pass a 'settings' object to override for one run. Results are stored
              and appear in list_issues.
            - check_style(source, settings?): stateless snippet check. Reference/icon rules need a loaded
              library and are inert here.
            - list_issues aggregates parse errors (available at load) plus style/spell violations from any
              check that has run. Filter by severity / source / classId.
            """,

        ["spelling"] = """
            Spell checking of description and Documentation prose:
            - The dictionary language(s) come from the repository's settings (SpellCheckLanguages, default
              en_US/en_GB). Change them with set_style_settings (see the 'style' topic); non-bundled
              languages must be imported as Hunspell dictionaries.
            - spell_check(classId | source): list misspelled words with line numbers. Covers class and
              component descriptions and Documentation info/revisions.
            - spelling_suggestions(word, repositoryId?): ranked corrections using the repository's
              configured language(s) plus your custom dictionary.
            - correct_spelling(classId, oldWord, newWord): whole-word, case-sensitive replacement across
              the file's prose (HTML tags, hrefs and code/pre blocks are preserved). By default it writes
              the file to disk and refreshes the graph; pass preview=true to just see the result.
            """,

        ["formatting"] = """
            Formatting. MLQT only formats COMPLETE class definitions, never loose fragments:
            - format_code(source, ...): stateless. The source must be one or more whole class definitions
              (model / block / package / record / function / connector / type ... end Name;). It CANNOT
              format a bare equation, a single declaration, or an expression — wrap the fragment in a class
              first, or it returns an error. Syntax errors are reported rather than silently formatted into
              malformed output. Nothing is written.
            - format_class(classId, ..., preview?): formats the .mo file that contains a loaded class and,
              unless preview=true, writes it to disk and refreshes the graph. Reformats the whole
              containing file (all classes stored in it), matching how MLQT saves files. If the file has
              syntax errors it reports them and writes nothing (fix them first).
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
                "workflows, views, editing, dependencies, style, spelling, formatting, vcs, resources.")]
    public object GetGuidance(
        [Description("Optional topic. Omit for the overview. One of: overview, workflows, views, editing, " +
                     "dependencies, style, spelling, formatting, vcs, resources.")]
        string? topic = null)
    {
        var key = string.IsNullOrWhiteSpace(topic) ? "overview" : topic.Trim();
        if (!Guidance.TryGetValue(key, out var text))
            return new ToolError($"Unknown topic '{topic}'. Available topics: {string.Join(", ", Topics)}.");

        return new { topic = key.ToLowerInvariant(), guidance = text, availableTopics = Topics };
    }
}
