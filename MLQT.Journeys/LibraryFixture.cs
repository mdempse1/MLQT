using LibGit2Sharp;

namespace MLQT.Journeys;

/// <summary>
/// A small Modelica library in a Git working copy, on disk, for a journey to open.
/// </summary>
/// <remarks>
/// <para>Journeys need a real repository rather than mocks: they exercise the loader, the graph, the
/// style checker and the VCS status together, and every one of those reads the file system. The
/// library is deliberately small and deliberately imperfect — one class breaks a rule that a journey
/// switches on, so "run a check and see a finding" has something to find.</para>
///
/// <para><b>It also has to be photographable</b> (B152). The first version was four flat classes
/// that referenced nothing and used no files, which is a fair test of the checker and a useless
/// subject for the documentation screenshots: the Dependencies tab drew an empty graph and the
/// External Resources tab said there were none, both correctly. So the library now has the two
/// things those pages are about — <b>classes that use each other</b>, three levels deep, and
/// <b>every kind of external resource reference MLQT recognises</b>, with the files they point at
/// actually on disk. One of them deliberately is not, because a missing resource is a state the
/// External Resources page exists to show.</para>
///
/// <para>Small enough to stay fast: eleven classes, four resource files and an image of 231 bytes.
/// What it is not is a stand-in for a real library — the reference-library performance work uses
/// libraries of tens of thousands of classes, and nothing here says anything about that.</para>
///
/// <para>There is no SVN fixture, for the reason <c>RevisionControl.Tests</c> already documents: SVN
/// integration needs a live working copy and a server no runner has. The Git side covers the same
/// pipeline.</para>
/// </remarks>
public sealed class LibraryFixture : IDisposable
{
    /// <summary>The working copy root, which is also the repository root.</summary>
    public string RepositoryPath { get; }

    /// <summary>The library directory inside it — the path a user would open.</summary>
    public string LibraryPath => Path.Combine(RepositoryPath, "Lib");

    /// <summary>The library's <c>Resources</c> directory, the target of its <c>modelica://</c> URIs.</summary>
    public string ResourcesPath => Path.Combine(LibraryPath, "Resources");

    /// <summary>A file that is modified relative to HEAD, so VCS status has something to report.</summary>
    public string ModifiedFile => Path.Combine(LibraryPath, "Modified.mo");

    /// <summary>
    /// A 96×96 PNG, as bytes rather than a committed binary.
    /// </summary>
    /// <remarks>
    /// A real image, because the icon renderer resolves a <c>Bitmap</c>'s <c>fileName</c> and embeds
    /// what it finds — a zero-byte placeholder would exercise the lookup and none of the rendering.
    /// Generated rather than committed so the repository carries no binary whose provenance nobody
    /// can check from the source.
    /// </remarks>
    private static byte[] LogoPng() => Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAGAAAABgCAIAAABt+uBvAAAArklEQVR42u3dUREAEBBFUSUEEE4leXQSAAn422HmvLkJToDd" +
        "lGvXoYQAEKAwoNKGdoAAAQIECBAgQIAAAQIECBAgQIDuzeMAAQIECBAgQIAAAQIECBAgQIAAAQIECBAgQIAAAQIECBAgQIAA" +
        "AQIECBAgQIAAAQIECBAgQIAAAQL0ClD8AAECBAgQIECAAAH6A8hhAUCAAAECBAgQIECAAAECBAgQoP+A5DMLIEDBLQqjRGeS" +
        "h5lNAAAAAElFTkSuQmCC");

    /// <param name="repositoryPath">
    /// Where to put the working copy, or null for a fresh directory under the temp path.
    /// </param>
    /// <remarks>
    /// The path is a parameter because MLQT shows it: the External Resources page, the repository
    /// list and the Edit Repository dialog all print the full path of what they are describing. A
    /// temp directory named after a GUID under a developer's profile is fine for a test and wrong in
    /// a manual, so the screenshot generator asks for somewhere a reader can recognise. A named path
    /// is emptied first, because it is reused between runs.
    /// </remarks>
    public LibraryFixture(string? repositoryPath = null)
    {
        RepositoryPath = repositoryPath
            ?? Path.Combine(Path.GetTempPath(), "mlqt-journey-" + Guid.NewGuid().ToString("N"));

        if (repositoryPath is not null && Directory.Exists(RepositoryPath))
            Delete(RepositoryPath);

        Directory.CreateDirectory(LibraryPath);

        WriteLibrary();
        WriteResources();
        CommitEverything();

        // One uncommitted edit, so the working copy is not clean - the baseline classification has
        // nothing to classify against a pristine checkout, and neither has the "changed files only"
        // filter in Code Review. Deliberately badly laid out as well as changed, so a journey that
        // formats it has something to put right; a file that is already canonical would pass a
        // "formatting ran" assertion by doing nothing.
        Write("Modified.mo", """
            within Lib;
                model Modified    "The model the journey edits"
              Real y   "Another state";
                    equation
                der(y) =    -y;
            annotation(Documentation(info="<html><p>Edited by the fixture.</p></html>"));
                end Modified;
            """);
    }

    private void WriteLibrary()
    {
        // The root package. Its documentation carries a modelica:// image URI, which is one of the
        // reference kinds the External Resources page reports (UriReference) and the one a library is
        // most likely to have without anyone thinking of it as a resource at all.
        Write("package.mo", """
            within;
            package Lib "A small library for the journey tests"
              annotation(
                uses(Modelica(version="4.0.0")),
                Documentation(info="<html>
                  <p>The library the journeys open.</p>
                  <img src=\"modelica://Lib/Resources/Images/logo.png\" width=\"96\"/>
                </html>"));
            end Lib;
            """);

        Write("package.order", "Interfaces\nComponents\nExamples\nExternals\nDocumented\nModified\nBadlyNamed\nUntidy\n");

        Write("Documented.mo", """
            within Lib;
            model Documented "A model with everything a rule could ask for"
              Real x "The state";
            equation
              der(x) = -x;
              annotation(Documentation(info="<html><p>Nothing to report here.</p></html>"));
            end Documented;
            """);

        Write("Modified.mo", """
            within Lib;
            model Modified "The model the journey edits"
              Real y "Another state";
            equation
              der(y) = -y;
              annotation(Documentation(info="<html><p>Edited by the fixture.</p></html>"));
            end Modified;
            """);

        // Lower-case class name and an undescribed variable: naming and documentation findings, on
        // purpose, so a journey that runs a check has something to see.
        Write("BadlyNamed.mo", """
            within Lib;
            model badly_named
              Real z;
            equation
              der(z) = -z;
            end badly_named;
            """);

        // Laid out the way the layout rules would rather it was not: an import after a declaration,
        // and three public sections where one would do. Nothing here is a *naming* or *documentation*
        // problem, so it only reports when those rules are switched on - which is the distinction the
        // settings documentation is trying to draw when it shows this class.
        Write("Untidy.mo", """
            within Lib;
            model Untidy "A class the layout rules have something to say about"
              Real a "The first state";
              import Lib.Interfaces.Pin;
            public
              Real b "The second state";
            protected
              Real c "One nobody outside can see";
            public
              Real d "A third state, in a second public section";
            equation
              der(a) = -a;
              der(b) = -b;
              der(c) = -c;
              der(d) = -d;
              annotation(Documentation(info="<html><p>Untidy on purpose.</p></html>"));
            end Untidy;
            """);

        WriteInterfaces();
        WriteComponents();
        WriteExamples();
        WriteExternals();
    }

    /// <summary>The connector everything else shares — the leaf of the dependency graph.</summary>
    private void WriteInterfaces()
    {
        WritePackage("Interfaces", "Lib", "The connectors the components share", "Pin");

        Write("Interfaces/Pin.mo", """
            within Lib.Interfaces;
            connector Pin "An electrical connection point"
              Real v "Potential at the pin";
              flow Real i "Current flowing into the pin";
              annotation(Documentation(info="<html><p>Used by every component in the library.</p></html>"));
            end Pin;
            """);
    }

    /// <summary>
    /// The components, and most of the library's external resource references.
    /// </summary>
    /// <remarks>
    /// Between them they carry every reference kind <c>ExternalResourceExtractor</c> recognises
    /// except the external-function annotations, which are in <c>Externals</c>: a <c>Bitmap</c> in an
    /// icon, a <c>loadResource</c> parameter default, and a <c>loadSelector</c> annotation. One of
    /// them points at a file that is not there.
    /// </remarks>
    private void WriteComponents()
    {
        WritePackage("Components", "Lib", "The parts a model is built from", "Source", "Load", "Profile");

        Write("Components/Source.mo", """
            within Lib.Components;
            model Source "A constant potential source"
              parameter Real potential = 1 "The potential across the source";
              Lib.Interfaces.Pin p "Positive pin";
              Lib.Interfaces.Pin n "Negative pin";
            equation
              p.v - n.v = potential;
              p.i + n.i = 0;
              annotation(
                Icon(graphics={Bitmap(
                  extent={{-100,-100},{100,100}},
                  fileName="modelica://Lib/Resources/Images/logo.png")}),
                Documentation(info="<html><p>Holds a fixed potential across its pins.</p></html>"));
            end Source;
            """);

        Write("Components/Load.mo", """
            within Lib.Components;
            model Load "A resistive load"
              parameter Real resistance = 1 "The resistance of the load";
              Lib.Interfaces.Pin p "Positive pin";
              Lib.Interfaces.Pin n "Negative pin";
            equation
              p.v - n.v = resistance * p.i;
              p.i + n.i = 0;
              annotation(Documentation(info="<html><p>Ohm's law, and nothing else.</p></html>"));
            end Load;
            """);

        Write("Components/Profile.mo", """
            within Lib.Components;
            model Profile "A load profile read from a data file"
              parameter String profileFile =
                Modelica.Utilities.Files.loadResource("modelica://Lib/Resources/Data/profile.txt")
                "The profile the load follows";
              parameter String calibrationFile =
                Modelica.Utilities.Files.loadResource("modelica://Lib/Resources/Data/calibration.txt")
                "A file this library does not ship, so the resource tree has something to report";
              parameter String userFile = "" "A file the user chooses at run time"
                annotation(Dialog(loadSelector(
                  filter="Comma separated values (*.csv)",
                  caption="Open a measured profile")));
              Real demand "The demand the profile asks for";
            equation
              der(demand) = -demand;
              annotation(Documentation(info="<html><p>Reads its data from Resources.</p></html>"));
            end Profile;
            """);
    }

    /// <summary>The example that ties the components together — the hub an impact analysis starts from.</summary>
    private void WriteExamples()
    {
        WritePackage("Examples", "Lib", "Models that show the components working", "Circuit");

        Write("Examples/Circuit.mo", """
            within Lib.Examples;
            model Circuit "A source driving a load, following a profile"
              Lib.Components.Source source "The supply";
              Lib.Components.Load load "The load it drives";
              Lib.Components.Profile profile "The demand over time";
            equation
              connect(source.p, load.p);
              connect(source.n, load.n);
              annotation(
                experiment(StopTime=1),
                Documentation(info="<html><p>The whole library in one model.</p></html>"));
            end Circuit;
            """);
    }

    /// <summary>The external function, for the four annotation-borne resource kinds.</summary>
    private void WriteExternals()
    {
        WritePackage("Externals", "Lib", "Functions implemented outside Modelica", "solveStep");

        // camelCase, because that is what MLQT's naming rules expect of a function: a PascalCase
        // name here would put a finding of its own into every picture of the findings list.
        Write("Externals/solveStep.mo", """
            within Lib.Externals;
            function solveStep "One step of the external solver"
              input Real x "The current state";
              output Real y "The state one step on";
              external "C" y = solve_step(x)
                annotation(
                  Include="#include \"solve.h\"",
                  IncludeDirectory="modelica://Lib/Resources/Include",
                  Library="solve",
                  LibraryDirectory="modelica://Lib/Resources/Library");
              annotation(Documentation(info="<html><p>Calls the bundled C library.</p></html>"));
            end solveStep;
            """);
    }

    /// <summary>
    /// The files the <c>modelica://</c> URIs point at.
    /// </summary>
    /// <remarks>
    /// <c>calibration.txt</c> is deliberately absent: the External Resources page distinguishes a
    /// reference it can resolve from one it cannot, and a fixture where everything resolves cannot
    /// show the difference. The rest are real files of the right kind, so the page's file-type
    /// filters (Data, C/C++, Libs, Images) each have something to match.
    /// </remarks>
    private void WriteResources()
    {
        WriteResource("Data/profile.txt", "0.0 0.0\n0.5 1.0\n1.0 0.0\n");
        WriteResource("Include/solve.h", "double solve_step(double x);\n");
        WriteResource("Library/libsolve.a", "not a real archive, and not one anything here links\n");

        var image = Path.Combine(ResourcesPath, "Images", "logo.png");
        Directory.CreateDirectory(Path.GetDirectoryName(image)!);
        File.WriteAllBytes(image, LogoPng());
    }

    /// <summary>Writes a sub-package's <c>package.mo</c> and <c>package.order</c>.</summary>
    private void WritePackage(string name, string within, string description, params string[] children)
    {
        Directory.CreateDirectory(Path.Combine(LibraryPath, name));

        Write($"{name}/package.mo", $"""
            within {within};
            package {name} "{description}"
              annotation(Documentation(info="<html><p>{description}.</p></html>"));
            end {name};
            """);

        Write($"{name}/package.order", string.Join("\n", children) + "\n");
    }

    private void Write(string name, string content)
    {
        var path = Path.Combine(LibraryPath, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content.ReplaceLineEndings("\n"));
    }

    private void WriteResource(string name, string content)
    {
        var path = Path.Combine(ResourcesPath, name.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content.ReplaceLineEndings("\n"));
    }

    private void CommitEverything()
    {
        Repository.Init(RepositoryPath);
        using var repo = new Repository(RepositoryPath);
        Commands.Stage(repo, "*");
        var who = new Signature("MLQT journeys", "journeys@mlqt.invalid", DateTimeOffset.Now);
        var first = repo.Commit("The library as it stands before the journey edits it", who, who);

        // A second commit, so the history is a history rather than a single row - and so one file in
        // it is *modified* rather than added, which is what gives the changed-files popover a diff to
        // offer. A repository with one commit cannot show either.
        Write("Documented.mo", """
            within Lib;
            model Documented "A model with everything a rule could ask for"
              Real x "The state";
              Real dx "Its rate of change";
            equation
              der(x) = -x;
              dx = der(x);
              annotation(Documentation(info="<html><p>Nothing to report here.</p></html>"));
            end Documented;
            """);

        Commands.Stage(repo, "*");
        repo.Commit("Report the rate of change as well as the state", who, who);

        // A second branch, not checked out. A repository with one branch makes the switch-branch and
        // merge dialogs pictures of an empty list, and those dialogs are most of what git-operations.md
        // is about.
        repo.Branches.Add("feature/pump-curves", first);
    }

    /// <summary>Touches a file under the library, the way a user editing it outside MLQT would.</summary>
    /// <remarks>
    /// A trailing comment: it changes the file's bytes and its timestamp, which is what the monitor
    /// watches, and leaves the Modelica in it valid, which is what everything downstream needs.
    /// </remarks>
    public void TouchOutsideMlqt(string relativePath) =>
        File.AppendAllText(Path.Combine(LibraryPath, relativePath),
                           "\n// Edited outside MLQT.\n");

    public void Dispose() => Delete(RepositoryPath);

    private static void Delete(string path)
    {
        if (!Directory.Exists(path))
            return;

        // A git working copy has read-only files under .git that a plain recursive delete refuses.
        foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);

        try { Directory.Delete(path, recursive: true); }
        catch (IOException) { /* a watcher still has a handle; the temp directory will be swept */ }
    }
}
