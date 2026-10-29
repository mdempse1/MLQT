using ModelicaGraph.DataTypes;
using ModelicaParser.StyleRules;
using Xunit;

namespace ModelicaGraph.Tests;

/// <summary>
/// What a <c>modelica://</c> link to a class leads to (<see cref="StyleChecking.CreateClassReferenceLookup"/>):
/// found, missing, or something MLQT cannot see, which <c>MLQT.Reference.ModelReferences</c> does not
/// report.
/// </summary>
public class ClassReferencesTests
{
    private readonly DirectedGraph _graph = new();

    private void Load(string file, string source) =>
        GraphBuilder.LoadModelicaFile(_graph, Path.Combine(Path.GetTempPath(), "ClassRefs", file), source);

    private ClassReferenceTarget TargetOf(string name) => StyleChecking.CreateClassReferenceLookup(_graph)!(name);

    private const string Lib = """
        package Lib
          package Interfaces
            partial package PartialMedium
              record ThermodynamicState
                Real T;
              end ThermodynamicState;
            end PartialMedium;
          end Interfaces;
          package Water
            extends Lib.Interfaces.PartialMedium;
          end Water;
          package Vendor
            extends OtherLib.Base;
          end Vendor;
          model 'quoted name'
          end 'quoted name';
        end Lib;
        """;

    [Fact]
    public void AClassThatIsThere_IsFound()
    {
        Load("Lib.mo", Lib);

        Assert.Equal(ClassReferenceTarget.Found, TargetOf("Lib.Interfaces.PartialMedium"));
        Assert.Equal(ClassReferenceTarget.Found, TargetOf("Lib"));
    }

    [Fact]
    public void AClassAPackageInherits_IsFound_ThoughNoNodeHasItsName()
    {
        // modelica://Modelica.Media.Water.StandardWater.ThermodynamicState: StandardWater gets it by
        // extending PartialMedium, and every tool follows the link. A lookup of the name in the graph
        // reported it broken.
        Load("Lib.mo", Lib);

        Assert.Null(_graph.GetNode<ModelNode>("Lib.Water.ThermodynamicState"));
        Assert.Equal(ClassReferenceTarget.Found, TargetOf("Lib.Water.ThermodynamicState"));
    }

    [Fact]
    public void AClassACompletePackageDoesNotHave_IsMissing()
    {
        Load("Lib.mo", Lib);

        Assert.Equal(ClassReferenceTarget.Missing, TargetOf("Lib.Interfaces.Pin"));
        Assert.Equal(ClassReferenceTarget.Missing, TargetOf("Lib.Water.NotInheritedEither"));
        Assert.Equal(ClassReferenceTarget.Missing, TargetOf("Lib.Nowhere.Deeper"));
    }

    [Fact]
    public void ALinkIntoALibraryThatIsNotLoaded_CannotBeJudged()
    {
        // The three-state rule's first case. MSL links to ModelicaReference throughout, and was
        // reported broken seven times until ModelicaReference was loaded.
        Load("Lib.mo", Lib);

        Assert.Equal(ClassReferenceTarget.CannotTell, TargetOf("ModelicaReference.Operators.'assert()'"));
    }

    [Fact]
    public void AClassMissingFromOneWithABaseMLQTCannotSee_CannotBeJudged()
    {
        // Vendor extends a class in a library that is not loaded, which could declare anything.
        Load("Lib.mo", Lib);

        Assert.Equal(ClassReferenceTarget.CannotTell, TargetOf("Lib.Vendor.Anything"));
    }

    [Fact]
    public void AClassAnEncryptedPackagesDocumentationDoesNotList_IsMissing()
    {
        // An encrypted class is recovered from the vendor's help HTML, which lists the classes each
        // package holds, so a link naming one it does not list is reported - the decision
        // EncryptedLibraryEndToEndTests holds end to end. Only its members are unknowable, which is
        // MLQT.Reference.Undeclared's question and not this one.
        Load("Lib.mo", Lib);
        _graph.GetNode<ModelNode>("Lib.Interfaces")!.IsExternalStub = true;

        Assert.Equal(ClassReferenceTarget.Missing, TargetOf("Lib.Interfaces.NotDocumented"));
        Assert.Equal(ClassReferenceTarget.Found, TargetOf("Lib.Interfaces.PartialMedium"));
    }

    [Fact]
    public void AQuotedIdentifier_IsOneSegment()
    {
        Load("Lib.mo", Lib);

        Assert.Equal(ClassReferenceTarget.Found, TargetOf("Lib.'quoted name'"));
    }

    [Fact]
    public void WithNoGraph_ThereIsNoLookup()
    {
        Assert.Null(StyleChecking.CreateClassReferenceLookup(null));
    }

    [Fact]
    public void TheRule_ReportsOnlyAMissingLink()
    {
        // The pipeline's path through StyleChecking: a verdict for every link, and only Missing a finding.
        Load("Lib.mo", Lib);
        const string doc = """
            model Doc
              annotation(Documentation(info="<html>
                <a href=\"modelica://Lib.Water.ThermodynamicState\">inherited</a>
                <a href=\"modelica://ModelicaReference.Classes\">not loaded</a>
                <a href=\"modelica://Lib.Interfaces.Pin\">missing</a>
              </html>"));
            end Doc;
            """;

        var findings = StyleChecking.RunStyleCheckingFindings(
            new ModelDefinition("Doc", doc),
            new StyleCheckingSettings { ValidateModelReferences = true },
            fullModelId: "Doc",
            classReferenceTarget: StyleChecking.CreateClassReferenceLookup(_graph));

        var finding = Assert.Single(findings, f => f.RuleId == RuleIds.ModelReferences);
        Assert.Contains("Lib.Interfaces.Pin", finding.Message);
    }
}
