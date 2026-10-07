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
    /// <summary>
    /// The topics offered, in the order they are listed to an agent. Held to <c>Guidance</c>'s own
    /// keys by a test: the two are a list and a dictionary of the same thing, and a topic advertised
    /// with no text reads to an agent as a server that lost its documentation.
    /// </summary>
    internal static readonly IReadOnlyList<string> Topics =
        ["overview", "workflows", "simulators", "views", "editing", "diagrams", "dependencies", "style", "spelling", "formatting", "vcs", "resources"];

    internal static readonly Dictionary<string, string> Guidance = new(StringComparer.OrdinalIgnoreCase)
    {
        ["overview"] = """
            MLQT MCP server — read, author, refactor, check, format and analyse Modelica (.mo) source on
            disk. Its focus is the source files and their quality; compiling and simulating belong to a
            simulator (see the 'simulators' topic), and generic Git/SVN work to your own CLI.

            Use it for Modelica files instead of your own file tools. Reading a .mo file with a text
            reader gives you raw text; mlqt_get_class_interface, mlqt_list_class_elements and
            mlqt_get_class_behavior give you the class with its inherited members resolved, for a
            fraction of the size. Editing a .mo file as text writes whatever you send; every mlqt_ edit
            is parse-checked and writes nothing that would not parse, rewrites references on a rename or
            move, keeps the file's encoding and line endings, refuses read-only libraries, and keeps the
            dependency graph current so the next query sees the change.

            Core concepts:
            - Load first. Almost every tool operates on an in-memory graph. Use mlqt_load_repository for a
              Git/SVN working copy or directory of libraries, or mlqt_load_library for a single library
              directory (with package.mo) or .mo file. mlqt_list_libraries shows what is loaded. To start
              a NEW project, mlqt_create_library writes and loads an empty top-level library.
            - Load the dependencies too. Nearly every library builds on the Modelica Standard Library (MSL),
              and loading a library does NOT load what it uses — but its load summary lists them (from its
              `uses` annotation) with the version it expects. Load each one; ask the user for the path if
              you cannot find it, since the MSL version varies by project. With them loaded, search, the
              views, mlqt_validate_class_references and the connector checks resolve types across the
              whole model. Without them you are reduced to reading raw text.
            - Class ids are fully-qualified dotted names, e.g. 'Modelica.Blocks.Continuous.Integrator'.
              Find one by name with mlqt_search_classes, by documentation prose with mlqt_search_text, or
              by shape with mlqt_search_by_interface. Library and repository ids are the GUIDs from
              mlqt_list_libraries / mlqt_list_repositories, but their names (e.g. "Modelica") work too.
            - Analysis is opt-in. Loading only parses structure. Dependency edges, impact and external
              resources need mlqt_analyze_dependencies to be run first (it can be slow on a big library).
              Style checking is also opt-in via mlqt_check_class / mlqt_check_library, with each
              repository's rules from its .mlqt/settings.json.
            - After an external change (a manual edit, a VCS pull, a simulator writing a file), mlqt_reload
              re-reads from disk.
            - Generic git/svn operations (commit, log, push, branch) are intentionally NOT provided —
              use your normal CLI for those. The two VCS tools here add Modelica-awareness the CLI lacks.

            Call mlqt_get_guidance with a topic for recipes: workflows, simulators, views, editing,
            diagrams, dependencies, style, spelling, formatting, vcs, resources.
            """,

        ["simulators"] = """
            Working beside a simulator's MCP server (OpenModelica, Dymola, or any other Modelica tool).

            Who does what:
            - MLQT owns the SOURCE: reading classes, writing and refactoring them, formatting, style and
              spelling checks, references, dependencies and impact. Every change lands in a .mo file on disk.
            - The simulator owns TRANSLATION: checking a model compiles (types, units, equation balance),
              instantiating it, simulating it and reading its results.
            Neither knows what the other has loaded. They are two separate sessions over the same files.

            Names that look alike and are not:
            - mlqt_load_library takes a PATH and loads source into MLQT. A simulator's load_library usually
              takes a library NAME and loads it into the simulator. Load each library into each server that
              needs it.
            - mlqt_list_classes lists what MLQT has loaded; a simulator's list_classes lists its own.
            - mlqt_check_class / mlqt_check_library are STYLE and spelling checks. They do not translate
              the model and will pass a model that does not compile. To ask whether a model is correct
              Modelica, use the simulator's check_model (or equivalent).

            The edit-and-simulate loop:
              1. Read and edit with MLQT (mlqt_get_class_source, mlqt_update_class_source, the surgical
                 tools, mlqt_batch_edit). The result is written to disk.
              2. Load or reload that FILE in the simulator (e.g. load_file on the edited .mo, or the
                 library's package.mo). A simulator does not see an edit until it re-reads the file.
              3. Check or simulate there, and read its errors.
              4. Fix with MLQT, and go back to step 2.
            Do not author code by sending it to the simulator as a string (load_string or a raw command):
            it then exists only in the simulator's memory, is never written to the library, and MLQT
            cannot see, check or format it. Write it with mlqt_create_class first, then load the file.

            If the simulator (or anything else) writes a .mo file, call mlqt_reload so MLQT re-reads it
            before you read or edit that class again.
            """,

        ["workflows"] = """
            Common workflows:

            Explore a library:
              mlqt_load_repository / mlqt_load_library -> mlqt_get_package_tree or mlqt_list_classes / mlqt_search_classes
              -> mlqt_get_class_info -> mlqt_get_class_source (include_annotations=false for compact structural code).

            Find a class when you don't know its name:
              mlqt_search_classes (by id substring), mlqt_search_text (by description/documentation prose, e.g.
              'PID controller'), or mlqt_search_by_interface (by shape: class type, has connectors/parameters,
              simulatable via an experiment annotation).

            Understand how to USE a class without reading its source (cheap):
              mlqt_get_class_interface (parameters, connectors, function signature) and mlqt_get_class_documentation
              (prose). mlqt_list_class_elements for the full declaration list. This is far smaller than
              mlqt_get_class_source — see the 'views' topic.

            Understand impact of changing a class:
              mlqt_analyze_dependencies (once) -> mlqt_find_usages (direct dependents) or
              mlqt_analyze_impact (full transitive blast radius).

            Review uncommitted changes before committing (via your CLI):
              mlqt_load_repository -> mlqt_get_changed_classes (what classes did I touch?) ->
              mlqt_analyze_dependencies -> mlqt_analyze_change_impact (what downstream is affected?).

            Quality pass on a library:
              mlqt_get_style_settings -> enable rules -> mlqt_check_library -> mlqt_list_findings.
              Parse errors are available from mlqt_list_findings immediately after loading (no check needed).
              mlqt_check_library answers with a COUNT and a sample, not the findings: findingCount is the real
              total and findings holds only the first 200 (truncated says so). Read the rest through
              mlqt_list_findings, and narrow before you page — a real library runs to tens of thousands, which
              is worth nothing to you as a list. See the 'style' topic for how to page and what to filter
              on; if what you actually want is the whole set in a file, the mlqt CLI writes it in one go
              (mlqt check <lib> --format json --out findings.json) and costs you no context at all.

            Fix a spelling mistake:
              mlqt_spell_check -> mlqt_spelling_suggestions -> mlqt_correct_spelling.

            Edit a class and re-check it:
              mlqt_get_class_source (read) -> mlqt_update_class_source (write the new source, same class name;
              validated + verbatim) -> optionally mlqt_format_class -> mlqt_check_class + mlqt_spell_check (re-check just
              that class).

            Rename a class (updates references too):
              mlqt_analyze_dependencies (once) -> mlqt_rename_class(classId, newName) [preview first] -> the
              declaration and every resolved reference are rewritten and dependencies refreshed. Precise:
              a same-named unrelated class is not touched, nor an alias that shares the name; every
              redeclare of a replaceable class is renamed with it. A class in a file of its own name takes the
              file with it, and package.order follows. Read-only files abort the rename.

            Start a new library/project:
              mlqt_create_library(name, directory) writes an empty top-level library on disk (package.mo +
              package.order) and loads it -> add classes with mlqt_create_class(name, source) -> add nested
              packages the same way (mlqt_create_class with a 'package ... end ...;' source). Storage defaults to
              one class per file: a class becomes its own .mo file and a sub-package becomes its own
              directory package (folder + package.mo), so the structure stays one-per-file all the way down.
              Classes that cannot be standalone (a replaceable/redeclare/inner/outer prefix) are nested.

            Author a new class in your library:
              mlqt_get_class_interface on the classes you'll reference (learn their API) -> mlqt_create_class(parentId,
              source) -> mlqt_check_class + mlqt_spell_check + mlqt_validate_class_references (validate) -> mlqt_format_class.
              A directory package gets a standalone .mo file; otherwise the class is nested.

            Study the library's idioms BEFORE assembling a model. Incrementally wiring up primitives is
            easy but often the wrong altitude: a good library usually provides higher-level, aggregate
            components that are the intended, more efficient building blocks (e.g. MSL MultiBody offers
            analytic assembly joints for closed kinematic loops instead of hand-wiring individual joints).
            Find them: mlqt_search_classes / mlqt_search_by_interface for candidates, read example models in the
            library (mlqt_search_text over documentation, then mlqt_get_class_behavior / mlqt_get_class_documentation on
            the examples) to see which components the library itself uses for the problem you are modelling,
            and prefer those over composing low-level parts. (This requires the dependency libraries to be
            loaded — see the load instructions.)

            Build up a model incrementally (surgical edits — no need to resend the whole class):
              mlqt_create_class -> mlqt_add_component (instantiate parts, e.g. a source and an integrator) ->
              mlqt_add_connection(model, 'source1.y', 'integrator1.u') [refuses incompatible connectors] ->
              mlqt_set_component_modifier to set parameters -> mlqt_add_equation/mlqt_add_statement for behaviour ->
              mlqt_list_connections to review. Also mlqt_add_extends / mlqt_add_import / mlqt_remove_component / mlqt_remove_connection.
              Each edit is parse-checked and refreshes dependencies; use preview=true to see the result first.
              To build a whole model in one shot, pass the same operations to mlqt_batch_edit — they apply
              atomically (all-or-nothing) and later ops see earlier ones (add components then connect them).
              For a usable diagram, mlqt_set_component_placement each component; once both ends of a connection
              are placed its diagram line is drawn (and refreshed if a component moves) automatically.
              Give components positions with mlqt_set_component_placement so the diagram is usable
              (mlqt_get_diagram_layout shows the current arrangement); there is no auto-layout.
              Document as you go: description strings come from mlqt_create_class source, mlqt_add_component's
              description, or mlqt_set_class_description / mlqt_set_component_description; set the rich HTML help with
              mlqt_set_class_documentation; the add_* tools take an optional comment to place a // note above the
              element.

            Restructure a library:
              mlqt_analyze_dependencies (once) -> mlqt_move_class(classId, newParentId) re-qualifies references to the
              moved class (its own refs to former siblings are reported, not auto-fixed; a name reached
              through inheritance or an import is left as written; a full name that would mean something else where it
              is written refuses the move, listing the references); mlqt_delete_class(classId)
              removes a class and reports what still references it. Use preview=true first.
            """,

        ["views"] = """
            Class "views" — compact projections so you don't have to read full source. All need only a
            loaded library (not mlqt_analyze_dependencies):
            - mlqt_get_class_interface(classId): the PUBLIC interface — settable parameters (name/type/default/
              description), connectors (with causality input/output and flow/stream), extends base classes,
              and for a function its input/output signature. The best first call to learn how to USE a class.
              Members INHERITED via extends are merged in and each marked with its base class in
              inheritedFrom (e.g. Integrator's u/y connectors come from Interfaces.SISO) — you get the whole
              picture without chasing base classes; pass include_inherited=false for own declarations only.
              Parameter defaults reflect extends-clause modifications, so the value shown is the effective
              one (extends Base(k = 10) reports k's default as 10, not the base's).
              A component counts as a connector if it has a causality or its type resolves to a loaded
              connector class (best-effort — load the type's library too).
            - mlqt_list_class_elements(classId, includeProtected?, includeInherited?): every element (components,
              extends, imports, nested classes) with full detail; inherited members included by default and
              marked with inheritedFrom. Public only unless includeProtected=true.
            - mlqt_get_class_documentation(classId, format=text|html): the class description plus the
              Documentation(info/revisions) prose. text strips HTML; html returns it raw.
            - mlqt_get_class_behavior(classId): the class's OWN equations, connect() statements and algorithm
              statements. connections includes those inside a for/if/when equation, each with 'within'
              naming its branches (e.g. ['for i in 1:n']); that equation is also in equations, verbatim.
              Inherited behaviour is NOT merged (unlike members) — basesWithBehavior points to
              the base classes that declare behaviour, query those directly for the full picture.
            - mlqt_validate_class_references(classId): lists referenced types (component types + extends) that do
              not resolve to a loaded class — catches typos and missing dependencies after writing/editing.
              Classes a base declares (a replaceable Medium, say) are found as Modelica finds them; a
              redeclare is not modelled, so treat hits as candidates and make sure referenced libraries
              are loaded.
            """,

        ["editing"] = """
            Editing Modelica on disk. EVERY editing tool: writes the .mo file, parse-checks the result and
            makes NO change if it would not parse, refuses read-only files (e.g. a reference library under
            Program Files), refreshes dependencies, and supports preview=true to see the result first.

            Start a project:
            - mlqt_create_library(name, directory): write and load a new, empty top-level library on disk (the
              first step of a new project). Then add classes with mlqt_create_class.

            Whole-class:
            - mlqt_create_class(parentId, source): add a class from complete source; a directory package gets a
              standalone .mo file, otherwise it is nested (force with standalone true/false).
            - mlqt_update_class_source(classId, newSource): replace a class's body verbatim, SAME name (to
              rename, use mlqt_rename_class). Not reformatted — run mlqt_format_class after if you want.
            - mlqt_rename_class(classId, newName): rename the class AND rewrite every resolved reference; precise
              (a same-named unrelated class is untouched); whole directory packages too. Needs
              mlqt_analyze_dependencies.
            - mlqt_move_class(classId, newParentId): move to a new parent and re-qualify references (whole
              directory packages too). Needs mlqt_analyze_dependencies. The moved class's own references to
              former siblings are reported, not auto-fixed. Names reached through inheritance or an import
              are left as written; if a full name would not mean the class where it is written (a nearer element of
              the top-level name, or an encapsulated class) the move is refused and nothing changes.
            - mlqt_delete_class(classId): remove a class (or a whole directory package) and report what still
              references it.

            Element-level (surgical — change one thing without resending the class):
            - mlqt_add_component / mlqt_remove_component / mlqt_set_component_modifier (components, parameters, connectors).
              mlqt_remove_component also removes every connect() naming the component (nested ones included)
              and lists them in its note; other equations using it are left for you to change.
              mlqt_add_component covers the full declaration: visibility='protected' (a protected section is
              created if absent); prefix for keywords like 'parameter', 'constant', 'replaceable', 'final',
              'inner'/'outer', 'flow'/'stream' (space-separated in Modelica order); constrainedBy for a
              replaceable's constraining clause; condition for a conditional 'if <expr>' component. Pass the
              Modelica keywords you already know — the tool just places the declaration correctly.
              Restricted-class rules are enforced across the add_* tools: equations/connect() only in a
              model/block/class; algorithm statements also in a function; a package holds only constants; a
              function's public components must be input/output (locals go in protected); a record takes only
              public data; a block's connectors must be causal (RealInput/RealOutput, not acausal physical
              connectors). The tool refuses an illegal addition with a message saying what is allowed.
            - mlqt_add_extends / mlqt_add_import.
            - mlqt_add_equation / mlqt_add_statement (algorithm section).
            - mlqt_add_connection(classId, portA, portB) / mlqt_remove_connection / mlqt_list_connections. mlqt_add_connection
              resolves both ports' connector types and REFUSES incompatible ones (RealOutput->RealInput is
              fine; a signal port to a physical Pin is not). mlqt_list_connections includes connects inside a
              for/if/when equation, each with 'within'; mlqt_remove_connection removes one of those by its ports
              as listed (e.g. 'a[i]'), leaving the loop or branch in place.
            - The add_* tools take an optional comment placed as a // line above the element.

            Build a whole model atomically:
            - mlqt_batch_edit(operations[]): apply a sequence of the surgical ops all-or-nothing; later ops see
              earlier ones (add components, then connect them). Any failure rolls the whole batch back.

            Documentation & comments:
            - mlqt_set_class_description / mlqt_set_component_description (the "..." strings), mlqt_set_class_documentation
              (Documentation(info/revisions) HTML; read with mlqt_get_class_documentation). Descriptions and //
              comments also survive mlqt_create_class/mlqt_update_class_source verbatim and appear in the views.

            Diagram layout:
            - mlqt_set_component_placement(classId, name, x1,y1,x2,y2, rotation?) positions a component;
              mlqt_get_diagram_layout shows the arrangement and mlqt_get_diagram_image draws it. No auto-layout —
              set placements explicitly. See the 'diagrams' topic for the conventions to place by.

            After an external change (a manual edit or VCS pull), mlqt_reload(target?) re-reads from disk. Then
            validate: mlqt_check_class + mlqt_spell_check + mlqt_validate_class_references. Type/unit/equation-balance and
            simulation are out of scope here — reload the file in a simulator and check it there (see the
            'simulators' topic).
            """,

        ["diagrams"] = """
            Laying out a diagram that looks like the library it sits in.

            LOOK AT IT. mlqt_get_diagram_image(classId) renders the class as a PNG: components with their own
            types' icons at their placements, and the connection lines between them. mlqt_get_diagram_layout
            gives you the same arrangement as numbers. Coordinates do not tell you that two components
            overlap, that a signal runs backwards or that a connector ended up on the wrong edge, and the
            image does — check the picture after placing, not just the extents.

            The coordinate system. A diagram is {{-100,-100},{100,100}} unless the class declares otherwise,
            with x to the right and y UP (not down, as in most image formats). A component's Placement
            extent is where its type's icon is scaled to, so its size is your choice, not the type's.
            An extent written with an origin is RELATIVE to it - origin={60,-120} with
            extent={{-20,-20},{20,20}} is a 40x40 box on the bottom edge, not one in the middle - and
            mlqt_get_diagram_layout reports extents with the origin already added in, so what it gives you
            is where the component actually is.

            Inherited ports are part of the diagram. Most blocks declare no connector at all and get
            u and y from a base class (Modelica.Blocks.Interfaces.SISO and its neighbours);
            mlqt_get_diagram_layout lists those with the base class in inheritedFrom, and they are drawn.
            You cannot move one without redeclaring it, so lay the rest of the model out around them.

            Sizes and the grid. Place on a multiple of 10 and give an ordinary block a 20x20 extent
            ({{-10,-10},{10,10}} about its centre) — that is the size the Modelica Standard Library draws a
            block at inside a 200x200 canvas, and a model whose parts are all 20x20 reads as one drawing.
            Leave 20-30 units between neighbours: connection lines need somewhere to run, and components
            that touch look like one component.

            Signal flow runs LEFT TO RIGHT. Sources on the left, sinks and outputs on the right, each stage
            in between at the x where it belongs in the chain. This is not decoration: it is how a reader
            knows which way causality goes, and reversing it is the single thing that makes a generated
            diagram look wrong. Put a feedback path BELOW the forward path, not through it.

            Where connectors sit, which decides where a component must go:
            - A causal signal input is on the LEFT edge of its type's icon, an output on the RIGHT, both at
              mid-height. So placing a block to the right of the one feeding it makes the connection a
              straight horizontal line, and placing it to the left makes the line double back.
            - An acausal physical connector (a Pin, a flange, a fluid port) sits wherever the library put it
              — often left and right for a two-port component, top and bottom for one in a vertical chain.
              Read it: mlqt_get_class_interface lists the connectors, and mlqt_get_diagram_layout on an existing model
              in the same library shows how that library arranges them.
            - Rotation turns the connectors with the component. rotation=90 puts a left-edge input at the
              bottom; rotation=180 mirrors a component end for end, which is how a two-port component is
              turned round in a loop.
            - Ground, references and anything that terminates a physical branch go BELOW the branch, because
              that is where every electrical and mechanical library in Modelica draws them.

            Connections. mlqt_add_connection and mlqt_set_component_placement route the line for you, orthogonally,
            from the real connector positions — so position the components first and the wiring follows.
            A connect inside a for/if/when equation is left as you wrote it, Line or no Line.
            Modelica diagrams use horizontal and vertical segments, not diagonals; if a route looks wrong in
            the image, the fix is almost always to move a component rather than to hand-write points.

            Copy the library. The strongest thing you can do is open an existing model from the same library
            with mlqt_get_diagram_image and match its spacing, its sizes and the direction its signals run. Every
            library has house style, and a diagram that follows it is read as belonging.
            """,

        ["dependencies"] = """
            Dependency, usage and impact:
            - Run mlqt_analyze_dependencies ONCE after loading (opt-in; parses everything). Re-run after
              loading more libraries.
            - mlqt_get_dependencies(classId): what a class directly uses (one hop).
            - mlqt_find_usages(classId): direct dependents — who uses this class (one hop).
            - mlqt_analyze_impact(classIds): full transitive set of classes that depend on the given
              class(es) — the complete blast radius, with the immediate source that pulled each in.
            - Empty results carry a dependenciesAnalyzed flag: false means "not analyzed yet",
              true means "genuinely none".
            - After mlqt_analyze_dependencies has run, all the editing tools (see the 'editing' topic)
              incrementally refresh the dependency graph for the files they change, so these queries stay
              current after an edit without a full re-analysis.
            """,

        ["style"] = """
            Style / quality checking (opt-in). Rules are PER-REPOSITORY:
            - Settings live in each repository's .mlqt/settings.json (the same file MLQT uses), loaded by
              mlqt_load_repository. mlqt_get_style_settings(repositoryId?) reads them; mlqt_set_style_settings(settings,
              repositoryId?) updates the rule toggles + spell languages and writes them back to
              .mlqt/settings.json. repositoryId is optional when one repo is loaded.
            - mlqt_check_class(classId, settings?) / mlqt_check_library(libraryId?, settings?): by default use the
              repository's settings; pass a 'settings' object to override for one run. Results are stored
              and appear in mlqt_list_findings.
            - mlqt_check_style(source, settings?): stateless snippet check. Reference/icon rules need a loaded
              library and are inert here.
            - mlqt_list_findings aggregates parse errors (available at load) plus style/spell findings from any
              check that has run. Filter by severity / source / classId.

            Severities are real. A style finding carries the severity its rule is configured with, written
            as 'Style error', 'Style warning' or 'Style info'; a parse diagnostic is a bare 'Error' or
            'Fatal'. The filter is a case-insensitive substring, so 'error' matches both kinds and
            'Style error' only the configured rules. Start with the errors: on a library with tens of
            thousands of findings they are usually a few dozen, and they are the ones the team chose to
            treat as errors.

            Reading them all, when you really need to:
            - mlqt_list_findings pages. limit defaults to 100 and is capped at 1000 (a larger value is clamped
              silently, not refused); offset skips. The result carries total (after filtering, before
              paging), offset, count and items — loop until offset >= total.
            - Order is by class id then line, applied before paging, so pages do not overlap or skip. It
              is recomputed per call, so do not run a check between pages.
            - Prefer narrowing to paging. severity for triage, classId to work on one class, source to
              separate parse errors from style findings. 18,000 findings is 19 calls and more text than
              you can reason about; the count and a per-severity breakdown usually answer the question
              that was actually asked.
            - For a genuine full dump, use the CLI rather than this server: `mlqt check <lib> --format
              json --out findings.json` runs the same pipeline to the same counts and adds Fingerprint
              and Status, which is what baseline/ratchet work needs. There is no bulk export here.
            """,

        ["spelling"] = """
            Spell checking of description and Documentation prose:
            - The dictionary language(s) come from the repository's settings (SpellCheckLanguages, default
              en_US/en_GB). Change them with mlqt_set_style_settings (see the 'style' topic); non-bundled
              languages must be imported as Hunspell dictionaries.
            - mlqt_spell_check(classId | source): list misspelled words with line numbers. Covers class and
              component descriptions and Documentation info/revisions.
            - mlqt_spelling_suggestions(word, repositoryId?): ranked corrections using the repository's
              configured language(s) plus your custom dictionary.
            - mlqt_correct_spelling(classId, oldWord, newWord): whole-word, case-sensitive replacement across
              the file's prose (HTML tags, hrefs and code/pre blocks are preserved). By default it writes
              the file to disk and refreshes the graph; pass preview=true to just see the result.
            - When the word is right and the checker is wrong, pick the narrowest waiver:
              mlqt_accept_spelling_in_class(classId, word) writes __MLQT(spelling="word") and accepts it in
              that class and the classes nested in it; the repository's .mlqt/dictionary.txt accepts it everywhere in the
              repository; mlqt_suppress_rule with MLQT.Spelling.Description / MLQT.Spelling.Documentation
              waives spell checking for a whole class, which silences its other misspellings too.
            """,

        ["formatting"] = """
            Formatting. MLQT only formats COMPLETE class definitions, never loose fragments:
            - mlqt_format_code(source, ...): stateless. The source must be one or more whole class definitions
              (model / block / package / record / function / connector / type ... end Name;). It CANNOT
              format a bare equation, a single declaration, or an expression — wrap the fragment in a class
              first, or it returns an error. Syntax errors are reported rather than silently formatted into
              malformed output. Nothing is written.
            - mlqt_format_class(classId, ..., preview?): formats the .mo file that contains a loaded class and,
              unless preview=true, writes it to disk and refreshes the graph. Reformats the whole
              containing file (all classes stored in it), matching how MLQT saves files. If the file has
              syntax errors it reports them and writes nothing (fix them first).
            - Ordering options: oneOfEachSection, importStatementsFirst, componentsBeforeClasses.
            """,

        ["vcs"] = """
            Modelica-aware version control (read-only):
            - mlqt_get_changed_classes(repositoryId, revision?): maps changed .mo files to the classes they
              contain. No revision = uncommitted working copy; a revision = that commit's changes.
            - mlqt_analyze_change_impact(repositoryId, revision?): changed classes -> full transitive impact.
              Requires mlqt_analyze_dependencies.
            These bridge a diff to the semantic graph. For commit/log/push/branch, use your git/svn CLI.
            """,

        ["resources"] = """
            External resources (data files, C sources/libraries, images, directories):
            - Requires mlqt_analyze_dependencies (it populates the resource graph and warnings).
            - mlqt_get_class_resources(classId): resources a class references, with resolved paths and
              whether each file exists.
            - mlqt_find_resource_usages(resolvedFilePath): reverse lookup — which classes use a resource.
            - mlqt_get_resource_warnings(): missing files and non-portable absolute-path references.
            """,
    };

    [McpServerTool(Name = "mlqt_get_guidance")]
    [Description("Get guidance on reading, editing and checking Modelica code with MLQT's tools. Call with " +
                "no topic for an overview and the list of topics, or a topic for focused recipes. Read " +
                "'simulators' when a Modelica simulator's server (OpenModelica, Dymola) is also connected. " +
                "Topics: overview, workflows, simulators, views, editing, diagrams, dependencies, style, " +
                "spelling, formatting, vcs, resources.")]
    public object GetGuidance(
        [Description("Optional topic. Omit for the overview. One of: overview, workflows, simulators, views, " +
                     "editing, diagrams, dependencies, style, spelling, formatting, vcs, resources.")]
        string? topic = null)
    {
        var key = string.IsNullOrWhiteSpace(topic) ? "overview" : topic.Trim();
        if (!Guidance.TryGetValue(key, out var text))
            return new ToolError($"Unknown topic '{topic}'. Available topics: {string.Join(", ", Topics)}.");

        return new { topic = key.ToLowerInvariant(), guidance = text, availableTopics = Topics };
    }
}
