using System.Reflection;
using System.ComponentModel;
using ModelContextProtocol.Server;
using MLQT.McpServer.Dtos;
using MLQT.McpServer.Helpers;
using MLQT.McpServer.Services;

namespace MLQT.McpServer.Tools;

/// <summary>
/// On-demand guidance for using this server effectively. Keeps per-tool descriptions short while
/// giving an agent a place to learn the intended sequencing and workflows.
/// </summary>
[Obfuscation]
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
            reader gives you raw text; mlqt_get_class with the interface, elements or behavior view gives
            you the class with its inherited members resolved, for a fraction of the size. Editing a .mo
            file as text writes whatever you send; every mlqt_ edit is parse-checked and writes nothing
            that would not parse, rewrites references on a rename or move, keeps the file's encoding and
            line endings, refuses read-only libraries, and keeps the dependency graph current so the next
            query sees the change.

            The 16 tools:
            - Session: mlqt_load (libraries, a working copy, or a startup script, with their
              dependencies), mlqt_session (what is loaded; unload; reload from disk), mlqt_create_library.
            - Read: mlqt_find (by name, prose, shape or a line of code), mlqt_browse (the package tree),
              mlqt_get_class (one or more classes in the views you ask for), mlqt_get_diagram_image.
            - Write: mlqt_write_class (whole classes), mlqt_edit_class (part of one class),
              mlqt_refactor_class (rename, move, delete), mlqt_format.
            - Check: mlqt_check (style, spelling, references, resources), mlqt_list_findings,
              mlqt_style_settings, mlqt_dependencies (what uses what, and the impact of a change).
            - This guidance: mlqt_get_guidance.

            Core concepts:
            - Load first. MLQT starts empty and has its own session: a library loaded into a simulator is
              not loaded here (see the 'simulators' topic). mlqt_load takes library folders, package.mo or
              .mo files, a Git/SVN working copy or folder of libraries, or a Dymola startup script, and
              loads what they use too. mlqt_session shows what is loaded. To start a NEW project,
              mlqt_create_library writes and loads an empty top-level library.
            - The session can outlive your conversation, and be shared with other tasks: each library says when
              it was loaded. A second copy of a loaded library is refused - replace it only if you loaded the
              first. mlqt_session with action 'reset' goes back to empty.
            - Class ids are fully-qualified dotted names, e.g. 'Modelica.Blocks.Continuous.Integrator'.
              Find one with mlqt_find. Library and repository ids are GUIDs, but their names (e.g.
              "Modelica") work too.
            - The dependency analysis runs when a tool needs it: mlqt_dependencies, mlqt_refactor_class,
              and the resources view and check. It can take a while on a big library, once.
            - After an external change (a manual edit, a VCS pull, a simulator writing a file),
              mlqt_session with action 'reload' re-reads from disk.
            - Generic git/svn operations (commit, log, push, branch) are intentionally NOT provided —
              use your normal CLI. mlqt_dependencies adds the Modelica-awareness the CLI lacks.

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

            Load every library into BOTH servers. Each starts empty, and loading into one does nothing
            for the other — if you loaded the MSL into the simulator, MLQT still has nothing, and its
            searches find nothing:
            - Into the simulator: its own load_library, usually by name ('Modelica'), or the user's startup
              script.
            - Into MLQT: mlqt_load(paths, tool), in this order:
              1. The libraries the simulator has loaded, all in one call, so that MLQT has the same versions.
                 OpenModelica: getLoadedLibraries() (through its raw-command tool) lists each with its
                 directory. Dymola cannot say what it has loaded: ask the user for their startup script (if
                 the Dymola server was given one, that is the one) and pass it as mlqt_load's script. It is
                 read, not run: the libraries its openModel, openModelFile and import lines open are loaded,
                 following its cd, RunScript and ModelicaPath changes, and a loader function it calls in a
                 library it opened is followed through its if branches. Any line it cannot work out is
                 listed - ask the user about those, or pass the path yourself.
              2. Leave withDependencies on, with tool 'dymola' or 'openmodelica': what those libraries use
                 and nothing loaded is then found beside them, in the tool's library folders and on
                 MODELICAPATH - the MSL, Complex and ModelicaServices included. The answer says which
                 install it searched and why (toolInstallChosen). If the simulator says it is running
                 another release, load again with toolInstall: what the first search found is searched for
                 again from that install.
              3. Read the answer: each library's version and where it came from, and anything not found.
              4. If the simulator's set changes - another project's startup script - load the new paths. What
                 you name explicitly replaces what a search found.
              With no simulator and no script, load the user's own library: the search finds the rest.

            Example — find and simulate an MSL example, such as a PID controller:
              1. Simulator: load_library 'Modelica'; then getLoadedLibraries() for the directories.
              2. MLQT: mlqt_load with the Modelica directory and tool 'openmodelica'; its dependencies follow.
              3. MLQT: mlqt_find with match 'text' and 'PID' (or match 'interface' for simulatable models) to
                 find the example, and mlqt_get_class with the documentation and interface views to
                 understand it.
              4. Simulator: simulate the class id MLQT found, and read the results there.

            Names that look alike and are not:
            - mlqt_load takes PATHS and loads source into MLQT. A simulator's load_library usually takes a
              library NAME and loads it into the simulator. Load each library into each server that needs it.
            - mlqt_check is a STYLE, spelling and reference check. It does not translate the model and will
              pass a model that does not compile. To ask whether a model is correct Modelica, use the
              simulator's check_model (or equivalent).

            The edit-and-simulate loop:
              1. Read and edit with MLQT (mlqt_get_class with includeAnnotations=true, then mlqt_edit_class to
                 change part of a class, or mlqt_write_class with replace=true for all of it). The result is
                 written to disk.
              2. Load or reload that FILE in the simulator (e.g. load_file on the edited .mo, or the
                 library's package.mo). A simulator does not see an edit until it re-reads the file.
              3. Check or simulate there, and read its errors.
              4. Fix with MLQT, and go back to step 2.
            Do not author code by sending it to the simulator as a string (load_string or a raw command):
            it then exists only in the simulator's memory, is never written to the library, and MLQT
            cannot see, check or format it. Write it with mlqt_write_class first, then load the file.

            If the simulator (or anything else) writes a .mo file, call mlqt_session with action 'reload'
            so MLQT re-reads it before you read or edit that class again.
            """,

        ["workflows"] = """
            Common workflows:

            Explore a library:
              mlqt_load -> mlqt_browse (from a root, a few levels down) or mlqt_find -> mlqt_get_class (info
              and source by default; includeAnnotations=false keeps the source compact).

            Find a class when you don't know its name:
              mlqt_find with match 'name' (id substring), 'text' (description/documentation prose, e.g.
              'PID controller') or 'interface' (by shape: class type, has connectors/parameters,
              simulatable via an experiment annotation).

            Find code - where a name is used, a Placement, a connect, a parameter declaration:
              mlqt_find with match 'code' and the text to look for, e.g. 'connect(motor.' or 'parameter Real T'.
              It searches every line of every loaded class, MSL included, and gives each line's innermost class
              and file line. Use it instead of grep or your own file search, which miss libraries loaded from
              elsewhere and cannot say which class a line belongs to. regex=true and ignoreCase=true refine it;
              library limits it to one library.

            Understand how to USE a class without reading its source (cheap):
              mlqt_get_class with views ['interface', 'documentation']. 'elements' is the full declaration
              list. Far smaller than the source — see the 'views' topic. Several classes go in one call.

            Understand the impact of changing a class:
              mlqt_dependencies(classIds, 'used_by') for direct dependents, with transitive=true for the
              whole blast radius.

            Review uncommitted changes before committing (via your CLI):
              mlqt_load the working copy -> mlqt_dependencies(repositoryId) for the classes it touched ->
              the same with transitive=true for what downstream is affected.

            Quality pass on a library:
              mlqt_style_settings -> enable rules -> mlqt_check(libraryId) -> mlqt_list_findings.
              Parse errors are available from mlqt_list_findings immediately after loading (no check needed).
              mlqt_check on a library answers with a COUNT and a sample, not the findings: findingCount is
              the real total and findings holds only the first 200 (truncated says so). Read the rest through
              mlqt_list_findings, and narrow before you page — a real library runs to tens of thousands, which
              is worth nothing to you as a list. See the 'style' topic for how to page and what to filter
              on; if what you actually want is the whole set in a file, the mlqt CLI writes it in one go
              (mlqt check <lib> --format json --out findings.json) and costs you no context at all.

            Fix a spelling mistake:
              mlqt_check(classId, checks ['spelling']) gives each word with suggestions ->
              mlqt_edit_class with a correct_spelling operation.

            Edit a class and re-check it:
              mlqt_get_class (source, includeAnnotations=true) -> mlqt_edit_class (replace, or the element
              operations) -> optionally mlqt_format -> mlqt_check(classId, checks ['style', 'spelling',
              'references']).

            Rename a class (updates references too):
              mlqt_refactor_class(classId, 'rename', newName) [preview first] -> the declaration and every
              resolved reference are rewritten and dependencies refreshed. Precise: a same-named unrelated
              class is not touched, nor an alias that shares the name; every redeclare of a replaceable class
              is renamed with it. A class in a file of its own name takes the file with it, and package.order
              follows. Read-only files abort the rename.

            Start a new library/project:
              mlqt_create_library(name, directory) writes an empty top-level library on disk (package.mo +
              package.order) and loads it. A name already loaded is refused, as a simulator would refuse it:
              pick another, or unload the loaded library first -> add classes, and packages of classes, with mlqt_write_class:
              several in one call, in order, so a later one can go in a package an earlier one created.
              Storage defaults to one class per file: a class becomes its own .mo file and a sub-package its
              own directory package (folder + package.mo). Classes that cannot be standalone (a
              replaceable/redeclare/inner/outer prefix) are nested.

            Author a new class in your library:
              mlqt_get_class with the interface view on the classes you'll reference (learn their API) ->
              mlqt_write_class -> mlqt_check with style, spelling and references -> mlqt_format.

            Study the library's idioms BEFORE assembling a model. Incrementally wiring up primitives is
            easy but often the wrong altitude: a good library usually provides higher-level, aggregate
            components that are the intended, more efficient building blocks (e.g. MSL MultiBody offers
            analytic assembly joints for closed kinematic loops instead of hand-wiring individual joints).
            Find them: mlqt_find by name or interface for candidates, read example models in the library
            (mlqt_find with match 'text', then mlqt_get_class with the behavior and documentation views on
            the examples) to see which components the library itself uses for the problem you are
            modelling, and prefer those over composing low-level parts. (This requires the dependency
            libraries to be loaded.)

            Build up a model (surgical edits — no need to resend the whole class):
              mlqt_write_class for the empty model -> one mlqt_edit_class with add_component operations
              (instantiate parts, e.g. a source and an integrator), add_connection('source1.y',
              'integrator1.u') [refuses incompatible connectors], set_component_modifier for parameters,
              add_equation / add_statement for behaviour, and set_component_placement for each component —
              all in one call, applied in order and all or none, so later operations see earlier ones. Once
              both ends of a connection are placed its diagram line is drawn, and refreshed when a component
              moves. mlqt_get_class with the connections and layout views reviews it, and
              mlqt_get_diagram_image shows it. Document as you go: description strings in the source or
              add_component's description, set_class_description / set_component_description, and
              set_class_documentation for the HTML help.

            Restructure a library:
              mlqt_refactor_class(classId, 'move', newParentId) re-qualifies references to the moved class
              (its own refs to former siblings are reported, not auto-fixed; a name reached through
              inheritance or an import is left as written; a full name that would mean something else where
              it is written refuses the move, listing the references); mlqt_refactor_class(classId,
              'delete') removes a class and lists what still refers to it. Use preview=true first.
            """,

        ["views"] = """
            Class "views" — compact projections so you don't have to read full source. mlqt_get_class takes
            one or more class ids and the views you want, default ['info', 'source']; each view answers as
            the tool it replaces did, and a view you did not ask for is left out:
            - 'info': kind, file, description, parent and children.
            - 'source': the class's code. Annotations are hidden unless includeAnnotations=true — show them
              before copying text for an edit's replace, so it matches the file.
            - 'interface': the PUBLIC interface — settable parameters (name/type/default/description),
              connectors (with causality input/output and flow/stream), extends base classes, and for a
              function its input/output signature. The best first read to learn how to USE a class.
              Members INHERITED via extends are merged in and each marked with its base class in
              inheritedFrom (e.g. Integrator's u/y connectors come from Interfaces.SISO); pass
              includeInherited=false for own declarations only. Parameter defaults reflect extends-clause
              modifications, so the value shown is the effective one.
            - 'elements': every element (components, extends, imports, nested classes) with full detail;
              inherited members included by default and marked with inheritedFrom. Public only unless
              includeProtected=true.
            - 'behavior': the class's OWN equations, connect() statements and algorithm statements;
              connections inside a for/if/when equation carry 'within' naming their branches. Inherited
              behaviour is NOT merged — basesWithBehavior points to the base classes that declare it.
            - 'connections': its connect equations, nested ones included.
            - 'documentation': the description plus the Documentation(info/revisions) prose;
              documentationFormat 'text' strips HTML, 'html' returns it raw.
            - 'layout': each component's placement on the diagram, origin applied.
            - 'resources': the files it loads, resolved, and whether each exists (runs the dependency
              analysis first if it has not run).
            Unresolved type references are a check, not a view: mlqt_check with checks ['references'] for a
            class or a whole library, each hit with its class, file and line, in one paged call.
            """,

        ["editing"] = """
            Editing Modelica on disk. EVERY edit: writes the .mo file, parse-checks the result and makes NO
            change if it would not parse, refuses read-only files (e.g. a reference library under Program
            Files), refreshes dependencies, and supports preview=true to see the result first.

            Start a project:
            - mlqt_create_library(name, directory): write and load a new, empty top-level library on disk
              (the first step of a new project). Then add classes with mlqt_write_class.

            Whole classes:
            - mlqt_write_class(classes[]): write several whole classes in one call, each {parentId, source,
              standalone?}, in order, so a later one can go in a package an earlier one created. A directory
              package gets a standalone .mo file for each, otherwise it is nested (force with standalone).
              A class that exists is replaced only with replace=true, so a typo cannot overwrite one; a
              replaced class keeps its name (to rename, use mlqt_refactor_class).
            - mlqt_refactor_class(classId, action): 'rename' (newName) rewrites the class AND every resolved
              reference, precisely (a same-named unrelated class is untouched), whole directory packages too;
              'move' (newParentId) re-qualifies references; the moved class's own references to former
              siblings are reported, not auto-fixed, and if a full name would not mean the class where it is
              written (a nearer element of the top-level name, or an encapsulated class) the move is refused
              and nothing changes; 'delete' removes a class (or a whole directory package) and lists what
              still refers to it.

            Change part of a class - the usual edit:
            - mlqt_edit_class(classId, operations[]): one class, a list of operations applied in order and
              all or none. replace {old, new} swaps text that occurs exactly once in the class - use it
              instead of your own file edit or a script, then there is nothing to reload. With all=true it
              swaps every occurrence - a name used throughout the class - instead of a sed, and says how
              many. Copy the old text from mlqt_get_class's source view with includeAnnotations=true,
              indentation included.
            - The element operations, each with its fields:
              add_component{type, name, modifier?, description?, visibility?, prefix?, constrainedBy?,
              condition?} covers the full declaration: visibility 'protected' (a protected section is created
              if absent); prefix for keywords like 'parameter', 'constant', 'replaceable', 'final',
              'inner'/'outer', 'flow'/'stream' (space-separated in Modelica order); constrainedBy for a
              replaceable's constraining clause; condition for a conditional 'if <expr>' component.
              remove_component{name} also removes every connect() naming the component (nested ones
              included) and lists them in its note; other equations using it are left for you to change.
              set_component_modifier{name, modifier}; add_extends{baseType}; add_import{import};
              add_equation{equation}; add_statement{statement} (algorithm section);
              add_connection{portA, portB} resolves both ports' connector types and REFUSES incompatible ones
              (RealOutput->RealInput is fine; a signal port to a physical Pin is not); remove_connection
              {portA, portB}, by the ports as listed, nested ones too; set_component_placement{name, x1, y1,
              x2, y2, rotation?}; set_class_description{description}; set_component_description{name,
              description}; set_class_documentation{info?, revisions?} (the HTML help); suppress_rule{ruleId,
              component?, reason?}; accept_spelling{word, reason?}; correct_spelling{word, newWord}. The add
              operations take an optional comment, placed as a // line above the element.
              Restricted-class rules are enforced: equations/connect() only in a model/block/class;
              algorithm statements also in a function; a package holds only constants; a function's public
              components must be input/output (locals go in protected); a record takes only public data; a
              block's connectors must be causal. An illegal addition is refused with what is allowed.
            - One call changes one class. To change several, call it once per class.

            Diagram layout:
            - set_component_placement positions a component; mlqt_get_class's layout view shows the
              arrangement and mlqt_get_diagram_image draws it. No auto-layout — set placements explicitly.
              See the 'diagrams' topic for the conventions to place by.

            After an external change (a manual edit or VCS pull), mlqt_session with action 'reload'
            re-reads from disk. Then validate: mlqt_check with style, spelling and references.
            Type/unit/equation-balance and simulation are out of scope here — reload the file in a
            simulator and check it there (see the 'simulators' topic).
            """,

        ["diagrams"] = """
            Laying out a diagram that looks like the library it sits in.

            LOOK AT IT. mlqt_get_diagram_image(classId) renders the class as a PNG: components with their own
            types' icons at their placements, and the connection lines between them. mlqt_get_class's layout
            view gives you the same arrangement as numbers. Coordinates do not tell you that two components
            overlap, that a signal runs backwards or that a connector ended up on the wrong edge, and the
            image does — check the picture after placing, not just the extents.

            The coordinate system. A diagram is {{-100,-100},{100,100}} unless the class declares otherwise,
            with x to the right and y UP (not down, as in most image formats). A component's Placement
            extent is where its type's icon is scaled to, so its size is your choice, not the type's.
            An extent written with an origin is RELATIVE to it - origin={60,-120} with
            extent={{-20,-20},{20,20}} is a 40x40 box on the bottom edge, not one in the middle - and the
            layout view reports extents with the origin already added in, so what it gives you is where the
            component actually is.

            Inherited ports are part of the diagram. Most blocks declare no connector at all and get
            u and y from a base class (Modelica.Blocks.Interfaces.SISO and its neighbours); the layout view
            lists those with the base class in inheritedFrom, and they are drawn. You cannot move one
            without redeclaring it, so lay the rest of the model out around them.

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
              Read it: mlqt_get_class's interface view lists the connectors, and its layout view on an
              existing model in the same library shows how that library arranges them.
            - Rotation turns the connectors with the component. rotation=90 puts a left-edge input at the
              bottom; rotation=180 mirrors a component end for end, which is how a two-port component is
              turned round in a loop.
            - Ground, references and anything that terminates a physical branch go BELOW the branch, because
              that is where every electrical and mechanical library in Modelica draws them.

            Connections. mlqt_edit_class's add_connection and set_component_placement route the line for
            you, orthogonally, from the real connector positions — so position the components first and the
            wiring follows. A connect inside a for/if/when equation is left as you wrote it, Line or no Line.
            Modelica diagrams use horizontal and vertical segments, not diagonals; if a route looks wrong in
            the image, the fix is almost always to move a component rather than to hand-write points.

            Copy the library. The strongest thing you can do is open an existing model from the same library
            with mlqt_get_diagram_image and match its spacing, its sizes and the direction its signals run.
            Every library has house style, and a diagram that follows it is read as belonging.
            """,

        ["dependencies"] = """
            Dependency, usage and impact, with mlqt_dependencies. It runs the dependency analysis itself the
            first time (it parses everything, so it can take a while on a big library), and the editing
            tools keep it current for the files they change. Give one target:
            - classIds with direction 'uses': what each class directly uses (one hop).
            - classIds with direction 'used_by': what directly uses each class (one hop); with
              transitive=true, the full transitive set of classes depending on them — the complete blast
              radius, with the immediate source that pulled each in. limit and offset page it.
            - repositoryId: the classes a working copy's changes touch, or with transitive=true everything
              those changes affect (see the 'vcs' topic).
            - resourcePath: the classes that refer to a file (see the 'resources' topic).
            """,

        ["style"] = """
            Style / quality checking. Rules are PER-REPOSITORY:
            - Settings live in each repository's .mlqt/settings.json (the same file MLQT uses), read when
              mlqt_load loads a working copy. mlqt_style_settings(repositoryId?) reads them; with settings it
              merges the rule toggles and spell languages you pass and writes them back. repositoryId is
              optional when one repo is loaded.
            - mlqt_check(classId | libraryId | source, settings?): by default uses the repository's settings;
              pass settings to override for one run. Results of a class or library check are stored and
              appear in mlqt_list_findings. A check of source text is stateless; reference and icon rules
              need a loaded library and are inert there.
            - mlqt_list_findings aggregates parse errors (available at load) plus style and spelling findings
              from any check that has run. Filter by severity / source / classId.

            What your change introduced, in a library carrying accepted debt:
            - A repository whose .mlqt/baseline.json is committed (made with `mlqt baseline`) gives each
              finding a status: New (not in the baseline), TouchedDebt (accepted debt in a class the
              working copy has changed) or AcceptedDebt. The result's baseline field counts each.
            - mlqt_check(classId or libraryId, baseline='new') returns only the New findings - check the
              classes you wrote or edited, or the whole library, and fix what comes back. baseline=
              'changed' adds the debt in classes you touched, if you are asked to leave them cleaner.
              mlqt_list_findings takes the same baseline filter.
            - With no baseline there is no status, and every view shows every finding.

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
              separate parse errors from style findings.
            - For a genuine full dump, use the CLI rather than this server: `mlqt check <lib> --format
              json --out findings.json` runs the same pipeline to the same counts and adds Fingerprint,
              which is what writing or updating a baseline needs. There is no bulk export here.
            """,

        ["spelling"] = """
            Spell checking of description and Documentation prose:
            - The dictionary language(s) come from the repository's settings (SpellCheckLanguages, default
              en_US/en_GB). Change them with mlqt_style_settings; non-bundled languages must be imported as
              Hunspell dictionaries.
            - mlqt_check(classId | source, checks ['spelling']): the misspelled words with line numbers, in
              class and component descriptions and Documentation info/revisions, and the ranked suggestions
              for each word, from the configured language(s) plus your custom dictionary.
            - mlqt_edit_class with correct_spelling{word, newWord}: whole-word, case-sensitive replacement
              across the file's prose (HTML tags, hrefs and code/pre blocks are preserved).
            - When the word is right and the checker is wrong, pick the narrowest waiver: mlqt_edit_class
              with accept_spelling{word} writes __MLQT(spelling="word") and accepts it in that class and the
              classes nested in it; the repository's .mlqt/dictionary.txt accepts it everywhere in the
              repository; suppress_rule with MLQT.Spelling.Description / MLQT.Spelling.Documentation waives
              spell checking for a whole class, which silences its other misspellings too.
            """,

        ["formatting"] = """
            Formatting. MLQT only formats COMPLETE class definitions, never loose fragments:
            - mlqt_format(classId | library | source): one tool for all three.
              - classId formats the .mo file that holds the class and, unless preview=true, writes it and
                refreshes the graph. It reformats the whole containing file (all classes stored in it),
                matching how MLQT saves files.
              - library formats every file of a loaded library in one call.
              - source is stateless: one or more whole class definitions (model / block / package / record /
                function / connector / type ... end Name;); nothing is written. It CANNOT format a bare
                equation, a single declaration or an expression — wrap the fragment in a class first.
            - A file with syntax errors, or holding a class excluded from formatting, is left alone and
              listed, never written in part.
            - Ordering options: oneOfEachSection, importStatementsFirst, componentsBeforeClasses.
            """,

        ["vcs"] = """
            Modelica-aware version control (read-only), through mlqt_dependencies with a repositoryId:
            - On its own: maps the changed .mo files to the classes they contain. No revision = the
              uncommitted working copy; a revision = that commit's changes.
            - With transitive=true: the changed classes and their full transitive impact.
            The working copy must be loaded with mlqt_load first. These bridge a diff to the semantic
            graph. For commit/log/push/branch, use your git/svn CLI.
            """,

        ["resources"] = """
            External resources (data files, C sources/libraries, images, directories). Each of these runs
            the dependency analysis first if it has not run, since that is what finds them:
            - mlqt_get_class with the 'resources' view: the resources a class references, with resolved
              paths and whether each file exists.
            - mlqt_dependencies with resourcePath: reverse lookup — which classes use a resource.
            - mlqt_check with checks ['resources']: missing files and non-portable absolute-path references
              across the loaded libraries.
            """,
    };

    [McpServerTool(Name = "mlqt_get_guidance", ReadOnly = true, OpenWorld = false)]
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

        // Unwrapped: the text is hard-wrapped for the source file, and a wrap means nothing to an agent.
        return new GuidanceResult(key.ToLowerInvariant(), Prose.Unwrap(text), Topics);
    }
}
