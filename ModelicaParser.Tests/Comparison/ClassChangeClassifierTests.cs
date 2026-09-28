using ModelicaParser.Comparison;

namespace ModelicaParser.Tests;

/// <summary>
/// The distinction B191 is about: an edit that can change what is simulated, against one that
/// cannot. Asserted over pairs of real class text rather than over the signature strings, because
/// the signature is an implementation detail and the promise is about the classification.
/// </summary>
public class ClassChangeClassifierTests
{
    private static readonly string Original = Lf("""
        within MyLib;
        model Resistor "An ideal resistor"
          parameter Real R = 100 "Resistance";
          Real v;
          Real i;
        equation
          v = R * i;
          annotation (Icon(graphics={Rectangle(extent={{-70,30},{70,-30}})}),
            Documentation(info="<html>A resistor.</html>"));
        end Resistor;
        """);

    /// <summary>
    /// The fixtures are raw string literals, so their line endings are whatever the <i>test file</i>
    /// carries - CRLF on a Windows checkout, LF on a runner with <c>core.autocrlf</c> off. Every one
    /// of them goes through here.
    /// </summary>
    /// <remarks>
    /// <para><b>B255 again, and caught the same way: on CI.</b> Two tests edited a fixture by
    /// searching it for a line ending, which cannot match a literal that arrived with the other
    /// kind - so the edit silently did nothing, the two versions were identical, and the
    /// classification came back <c>Unchanged</c>. They passed on Windows and failed on Linux.</para>
    ///
    /// <para><b>The product was right on both.</b> <c>ClassSignatures.Of</c> preprocesses its input,
    /// so a classification does not depend on line endings at all - which
    /// <see cref="TheAnswerIsTheSameWhicheverLineEndingsTheFileHas"/> now says outright, rather than
    /// leaving it as something the fixtures happened to rely on.</para>
    /// </remarks>
    private static string Lf(string source) => source.Replace("\r\n", "\n");

    private static ClassChangeKind Classify(string committed, string working)
    {
        var kinds = ClassChangeClassifier.Compare(committed, working);
        return Assert.Contains("MyLib.Resistor", kinds);
    }

    // ---------------------------------------------------------------- nothing changed

    [Fact]
    public void AnIdenticalClassIsUnchanged()
    {
        Assert.Equal(ClassChangeKind.Unchanged, Classify(Original, Original));
    }

    /// <summary>
    /// A classification does not depend on the line endings of either version. Modelica files in
    /// the wild carry both, a working copy can differ from what was committed by nothing else, and
    /// a diff that reported that as a change to what is simulated would be useless.
    /// </summary>
    [Fact]
    public void TheAnswerIsTheSameWhicheverLineEndingsTheFileHas()
    {
        static string Crlf(string source) => source.Replace("\n", "\r\n");

        var edited = Original.Replace("R = 100", "R = 220");

        Assert.Equal(ClassChangeKind.AffectsSimulation, Classify(Original, edited));
        Assert.Equal(ClassChangeKind.AffectsSimulation, Classify(Crlf(Original), edited));
        Assert.Equal(ClassChangeKind.AffectsSimulation, Classify(Original, Crlf(edited)));

        // And re-saving a file with the other endings and nothing else is no change at all.
        Assert.Equal(ClassChangeKind.Unchanged, Classify(Original, Crlf(Original)));
    }

    // ---------------------------------------------------------------- cosmetic

    [Fact]
    public void ReformattingIsCosmetic()
    {
        var reformatted = Original
            .Replace("  parameter Real R = 100", "    parameter  Real  R  =  100")
            .Replace("  v = R * i;", "  v = R*i;");

        Assert.Equal(ClassChangeKind.Cosmetic, Classify(Original, reformatted));
    }

    [Fact]
    public void RewordingADescriptionIsCosmetic()
    {
        var reworded = Original.Replace("\"Resistance\"", "\"Resistance of the device\"");

        Assert.Equal(ClassChangeKind.Cosmetic, Classify(Original, reworded));
    }

    [Fact]
    public void RewordingTheClassDescriptionIsCosmetic()
    {
        var reworded = Original.Replace("\"An ideal resistor\"", "\"A linear resistor\"");

        Assert.Equal(ClassChangeKind.Cosmetic, Classify(Original, reworded));
    }

    [Fact]
    public void AddingACommentIsCosmetic()
    {
        var commented = Original.Replace("  Real v;", "  // the voltage across it\n  Real v;");

        Assert.Equal(ClassChangeKind.Cosmetic, Classify(Original, commented));
    }

    [Fact]
    public void MovingSomethingOnTheDiagramIsCosmetic()
    {
        var redrawn = Original.Replace("{{-70,30},{70,-30}}", "{{-80,40},{80,-40}}");

        Assert.Equal(ClassChangeKind.Cosmetic, Classify(Original, redrawn));
    }

    [Fact]
    public void RewritingTheDocumentationIsCosmetic()
    {
        var documented = Original.Replace(
            "<html>A resistor.</html>",
            "<html>An ideal linear resistor, per Ohm's law.</html>");

        Assert.Equal(ClassChangeKind.Cosmetic, Classify(Original, documented));
    }

    /// <summary>
    /// Reordering an annotation's elements means nothing — they are a set — so it is cosmetic
    /// rather than a change to what is simulated. A save from another tool is how this arises, and
    /// it arrives alongside the graphical rewrites above.
    /// </summary>
    [Fact]
    public void ReorderingAnnotationElementsIsCosmetic()
    {
        var before = Wrap("  annotation (Evaluate=true, Inline=true);");
        var after = Wrap("  annotation (Inline=true, Evaluate=true);");

        Assert.Equal(ClassChangeKind.Cosmetic, ClassifyWrapped(before, after));
    }

    // ---------------------------------------------------------------- affects simulation

    [Fact]
    public void ChangingAnEquationAffectsSimulation()
    {
        var changed = Original.Replace("v = R * i;", "v = R * i + 1;");

        Assert.Equal(ClassChangeKind.AffectsSimulation, Classify(Original, changed));
    }

    [Fact]
    public void ChangingAParameterValueAffectsSimulation()
    {
        var changed = Original.Replace("R = 100", "R = 220");

        Assert.Equal(ClassChangeKind.AffectsSimulation, Classify(Original, changed));
    }

    [Fact]
    public void AddingADeclarationAffectsSimulation()
    {
        var changed = Original.Replace("  Real i;", "  Real i;\n  Real p;");

        Assert.Equal(ClassChangeKind.AffectsSimulation, Classify(Original, changed));
    }

    [Fact]
    public void RemovingAnEquationAffectsSimulation()
    {
        var changed = Original.Replace("  v = R * i;\n", "");

        Assert.Equal(ClassChangeKind.AffectsSimulation, Classify(Original, changed));
    }

    [Fact]
    public void ChangingATypeAffectsSimulation()
    {
        var changed = Original.Replace("parameter Real R", "parameter Integer R");

        Assert.Equal(ClassChangeKind.AffectsSimulation, Classify(Original, changed));
    }

    /// <summary>
    /// The half of B191 that a "skip every annotation" rule would get wrong: <c>Evaluate</c> changes
    /// how the model is translated, and a graphical edit in the same annotation must not hide it.
    /// </summary>
    [Theory]
    [InlineData("Evaluate=true", "Evaluate=false")]
    [InlineData("Inline=true", "Inline=false")]
    [InlineData("smoothOrder=1", "smoothOrder=2")]
    [InlineData("HideResult=true", "HideResult=false")]
    [InlineData("experiment(StopTime=1)", "experiment(StopTime=10)")]
    public void ChangingASignificantAnnotationAffectsSimulation(string before, string after)
    {
        Assert.Equal(
            ClassChangeKind.AffectsSimulation,
            ClassifyWrapped(Wrap($"  annotation ({before});"), Wrap($"  annotation ({after});")));
    }

    [Fact]
    public void ASignificantAnnotationChangedAlongsideAGraphicalOneStillAffectsSimulation()
    {
        var before = Wrap("  annotation (Evaluate=true, Icon(graphics={Line(points={{0,0},{1,1}})}));");
        var after = Wrap("  annotation (Evaluate=false, Icon(graphics={Line(points={{0,0},{9,9}})}));");

        Assert.Equal(ClassChangeKind.AffectsSimulation, ClassifyWrapped(before, after));
    }

    /// <summary>
    /// A class is one thing or the other, never both: a class carrying an equation change <i>and</i>
    /// a redrawn icon is <see cref="ClassChangeKind.AffectsSimulation"/>.
    /// </summary>
    /// <remarks>
    /// <para>The kinds are a ranking, not a set of labels, and this is the case that decides which.
    /// "Cosmetic" has to mean <b>only</b> cosmetic, or it is not an answer to the question it is
    /// asked: a reviewer narrowing to it is looking for the classes they can pass over, and one
    /// with a changed equation in it is not one of those. The same class does appear under
    /// "affects simulation", which is where it needs to be read.</para>
    /// </remarks>
    [Fact]
    public void AClassWithBothKindsOfChangeAffectsSimulation()
    {
        var changed = Original
            .Replace("R = 100", "R = 220")
            .Replace("{{-70,30},{70,-30}}", "{{-80,40},{80,-40}}")
            .Replace("\"Resistance\"", "\"The resistance\"");

        Assert.Equal(ClassChangeKind.AffectsSimulation, Classify(Original, changed));
    }

    /// <summary>
    /// An annotation MLQT has never heard of is significant. The alternative — ignoring what it
    /// cannot name — would quietly hide every vendor annotation that steers a translator.
    /// </summary>
    [Fact]
    public void ChangingAnUnrecognisedAnnotationAffectsSimulation()
    {
        var before = Wrap("  annotation (__SomeVendor_inlineOrder=1);");
        var after = Wrap("  annotation (__SomeVendor_inlineOrder=2);");

        Assert.Equal(ClassChangeKind.AffectsSimulation, ClassifyWrapped(before, after));
    }

    [Fact]
    public void AddingASignificantAnnotationAffectsSimulation()
    {
        Assert.Equal(
            ClassChangeKind.AffectsSimulation,
            ClassifyWrapped(Wrap("  Real x;"), Wrap("  Real x;\n  annotation (Evaluate=true);")));
    }

    /// <summary>
    /// Adding a purely graphical annotation to a class that had none is cosmetic — the whole
    /// annotation drops out of the comparison, so only the text differs.
    /// </summary>
    [Fact]
    public void AddingOnlyAGraphicalAnnotationIsCosmetic()
    {
        Assert.Equal(
            ClassChangeKind.Cosmetic,
            ClassifyWrapped(
                Wrap("  Real x;"),
                Wrap("  Real x;\n  annotation (Icon(graphics={Line(points={{0,0},{1,1}})}));")));
    }

    // ---------------------------------------------------------------- where annotations attach

    /// <summary>
    /// A class body's annotation is a statement, so its semicolon goes with it. Without that, a
    /// class that gained nothing but an icon would differ by a stray <c>;</c> and read as a change
    /// to what is simulated — which is what <c>EmitComposition</c> is for.
    /// </summary>
    [Fact]
    public void GainingOnlyAGraphicalAnnotationOnAnEquationSectionIsCosmetic()
    {
        var before = Wrap("  Real x;\nequation\n  x = 1;");
        var after = Wrap("  Real x;\nequation\n  x = 1;\n  annotation (Diagram(coordinateSystem(extent={{-1,-1},{1,1}})));");

        Assert.Equal(ClassChangeKind.Cosmetic, ClassifyWrapped(before, after));
    }

    [Fact]
    public void AnAnnotationOnAnExtendsClauseIsJudgedTheSameWay()
    {
        var plain = Wrap("  extends Base;");
        var drawn = Wrap("  extends Base annotation (Icon(graphics={Line(points={{0,0},{1,1}})}));");
        var evaluated = Wrap("  extends Base annotation (Evaluate=true);");

        Assert.Equal(ClassChangeKind.Cosmetic, ClassifyWrapped(plain, drawn));
        Assert.Equal(ClassChangeKind.AffectsSimulation, ClassifyWrapped(plain, evaluated));
    }

    [Fact]
    public void AnAnnotationOnADeclarationIsJudgedTheSameWay()
    {
        var plain = Wrap("  Real x;");
        var placed = Wrap("  Real x annotation (Placement(transformation(extent={{-1,-1},{1,1}})));");
        var evaluated = Wrap("  Real x annotation (Evaluate=true);");

        Assert.Equal(ClassChangeKind.Cosmetic, ClassifyWrapped(plain, placed));
        Assert.Equal(ClassChangeKind.AffectsSimulation, ClassifyWrapped(plain, evaluated));
    }

    /// <summary>
    /// The external clause's semicolon terminates a declaration, not an annotation, so it is kept
    /// whether or not the annotation on it survives. An external function's <c>Library</c> is
    /// significant; a display-only annotation beside it is not.
    /// </summary>
    [Fact]
    public void AnExternalFunctionsLibraryAnnotationAffectsSimulation()
    {
        var before = WrapFunction("  external \"C\" f(x) annotation (Library=\"mylib\");");
        var after = WrapFunction("  external \"C\" f(x) annotation (Library=\"otherlib\");");

        Assert.Equal(ClassChangeKind.AffectsSimulation, ClassifyWrappedFunction(before, after));
    }

    [Fact]
    public void ADisplayOnlyAnnotationOnAnExternalFunctionIsCosmetic()
    {
        var before = WrapFunction("  external \"C\" f(x);");
        var after = WrapFunction("  external \"C\" f(x) annotation (Documentation(info=\"<html>x</html>\"));");

        Assert.Equal(ClassChangeKind.Cosmetic, ClassifyWrappedFunction(before, after));
    }

    // ---------------------------------------------------------------- the other class shapes

    /// <summary>
    /// A short class definition — <c>type X = Real(...)</c> — is a class like any other, and its
    /// modification is what it means.
    /// </summary>
    [Fact]
    public void AShortClassDefinitionIsCompared()
    {
        var before = "within MyLib;\ntype Voltage = Real(unit=\"V\", min=0) \"Electrical potential\";\n";
        var reworded = before.Replace("\"Electrical potential\"", "\"A voltage\"");
        var changed = before.Replace("min=0", "min=-1");

        Assert.Equal(ClassChangeKind.Cosmetic, Assert.Contains("MyLib.Voltage", ClassChangeClassifier.Compare(before, reworded)));
        Assert.Equal(ClassChangeKind.AffectsSimulation, Assert.Contains("MyLib.Voltage", ClassChangeClassifier.Compare(before, changed)));
    }

    /// <summary>
    /// A <c>der</c> class definition names the function and then the variables it is differentiated
    /// with respect to; the first identifier is its own name.
    /// </summary>
    [Fact]
    public void ADerClassDefinitionIsCompared()
    {
        var before = "within MyLib;\nfunction dArea = der(Area, r);\n";
        var changed = "within MyLib;\nfunction dArea = der(Area, h);\n";

        var kinds = ClassChangeClassifier.Compare(before, changed);

        Assert.Equal(ClassChangeKind.AffectsSimulation, Assert.Contains("MyLib.dArea", kinds));
    }

    [Fact]
    public void AClassDefinedByExtendingAnotherIsCompared()
    {
        var before = "within MyLib;\nmodel extends Base(R=1)\n  Real x;\nend Base;\n";
        var changed = before.Replace("R=1", "R=2");

        var kinds = ClassChangeClassifier.Compare(before, changed);

        Assert.Equal(ClassChangeKind.AffectsSimulation, Assert.Contains("MyLib.Base", kinds));
    }

    /// <summary>
    /// An annotation with nothing in it contributes nothing, so adding or removing one is cosmetic
    /// rather than a change to what is simulated.
    /// </summary>
    [Fact]
    public void AnEmptyAnnotationIsCosmetic()
    {
        Assert.Equal(
            ClassChangeKind.Cosmetic,
            ClassifyWrapped(Wrap("  Real x;"), Wrap("  Real x;\n  annotation ();")));
    }

    // ---------------------------------------------------------------- added and unknown

    [Fact]
    public void AClassWithNoCommittedVersionIsAdded()
    {
        var kinds = ClassChangeClassifier.Compare(committedText: null, Original);

        Assert.Equal(ClassChangeKind.Added, Assert.Contains("MyLib.Resistor", kinds));
    }

    [Fact]
    public void AClassAddedToAnExistingFileIsAdded()
    {
        var withAnother = Original + "\n\nmodel Capacitor\n  Real v;\nend Capacitor;\n";

        var kinds = ClassChangeClassifier.Compare(Original, withAnother);

        Assert.Equal(ClassChangeKind.Added, Assert.Contains("MyLib.Capacitor", kinds));
        Assert.Equal(ClassChangeKind.Unchanged, Assert.Contains("MyLib.Resistor", kinds));
    }

    /// <summary>
    /// A class that was deleted has nothing left in the tree to mark, so it is not reported. The
    /// file itself still shows as modified.
    /// </summary>
    [Fact]
    public void AClassRemovedFromTheFileIsNotReported()
    {
        var withAnother = Original + "\n\nmodel Capacitor\n  Real v;\nend Capacitor;\n";

        var kinds = ClassChangeClassifier.Compare(withAnother, Original);

        Assert.DoesNotContain("MyLib.Capacitor", kinds);
    }

    // A Fact rather than a Theory carrying one case: the fixtures are normalised at the source
    // now, so they are not compile-time constants and cannot go in an InlineData.
    [Fact]
    public void AVersionThatWillNotParseIsUnknown()
    {
        var kinds = ClassChangeClassifier.Compare("model Broken\n  Real x\nend Broken;", Original);

        Assert.All(kinds.Values, kind => Assert.Equal(ClassChangeKind.Unknown, kind));
    }

    [Fact]
    public void AWorkingCopyThatWillNotParseReportsNothing()
    {
        var kinds = ClassChangeClassifier.Compare(Original, "model Broken\n  Real x\nend Broken;");

        Assert.Empty(kinds);
    }

    // ---------------------------------------------------------------- nesting

    private static readonly string Package = Lf("""
        within MyLib;
        package Components "Some components"

          model Resistor
            parameter Real R = 100;
          end Resistor;

          model Capacitor
            parameter Real C = 1;
          end Capacitor;

        end Components;
        """);

    /// <summary>
    /// The reason both halves of a signature exclude nested classes: editing one class in a package
    /// must mark that class, not every package above it.
    /// </summary>
    [Fact]
    public void EditingANestedClassLeavesItsPackageUnchanged()
    {
        var edited = Package.Replace("R = 100", "R = 220");

        var kinds = ClassChangeClassifier.Compare(Package, edited);

        Assert.Equal(ClassChangeKind.AffectsSimulation, Assert.Contains("MyLib.Components.Resistor", kinds));
        Assert.Equal(ClassChangeKind.Unchanged, Assert.Contains("MyLib.Components.Capacitor", kinds));
        Assert.Equal(ClassChangeKind.Unchanged, Assert.Contains("MyLib.Components", kinds));
    }

    [Fact]
    public void AddingANestedClassChangesItsPackage()
    {
        var edited = Package.Replace(
            "end Components;",
            "  model Inductor\n    parameter Real L = 1;\n  end Inductor;\n\nend Components;");

        var kinds = ClassChangeClassifier.Compare(Package, edited);

        Assert.Equal(ClassChangeKind.AffectsSimulation, Assert.Contains("MyLib.Components", kinds));
        Assert.Equal(ClassChangeKind.Added, Assert.Contains("MyLib.Components.Inductor", kinds));
    }

    [Fact]
    public void RenamingANestedClassChangesItsPackage()
    {
        var edited = Package.Replace("Capacitor", "Cap");

        var kinds = ClassChangeClassifier.Compare(Package, edited);

        Assert.Equal(ClassChangeKind.AffectsSimulation, Assert.Contains("MyLib.Components", kinds));
    }

    /// <summary>
    /// Reindenting a package is cosmetic for the package and invisible to its children, because the
    /// children's own text is compared separately and has not moved relative to itself.
    /// </summary>
    [Fact]
    public void ReindentingAPackageIsCosmeticForThePackageOnly()
    {
        var edited = Package.Replace("\n  model Resistor", "\n\n  model Resistor");

        var kinds = ClassChangeClassifier.Compare(Package, edited);

        Assert.Equal(ClassChangeKind.Cosmetic, Assert.Contains("MyLib.Components", kinds));
        Assert.Equal(ClassChangeKind.Unchanged, Assert.Contains("MyLib.Components.Resistor", kinds));
    }

    // ---------------------------------------------------------------- names

    [Fact]
    public void ClassesAreKeyedByTheirFullModelicaName()
    {
        var kinds = ClassChangeClassifier.Compare(null, Package);

        Assert.Equal(
            ["MyLib.Components", "MyLib.Components.Capacitor", "MyLib.Components.Resistor"],
            kinds.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void AFileWithNoWithinClauseKeysOnTheClassNameAlone()
    {
        var kinds = ClassChangeClassifier.Compare(null, "model Top\n  Real x;\nend Top;");

        Assert.Equal(["Top"], kinds.Keys.ToArray());
    }

    // ---------------------------------------------------------------- helpers

    private static string Wrap(string body) =>
        $"within MyLib;\nmodel Thing\n{body}\nend Thing;\n";

    private static string WrapFunction(string body) =>
        $"within MyLib;\nfunction F\n  input Real x;\n  output Real y;\n{body}\nend F;\n";

    private static ClassChangeKind ClassifyWrappedFunction(string committed, string working)
    {
        var kinds = ClassChangeClassifier.Compare(committed, working);
        return Assert.Contains("MyLib.F", kinds);
    }

    private static ClassChangeKind ClassifyWrapped(string committed, string working)
    {
        var kinds = ClassChangeClassifier.Compare(committed, working);
        return Assert.Contains("MyLib.Thing", kinds);
    }
}
