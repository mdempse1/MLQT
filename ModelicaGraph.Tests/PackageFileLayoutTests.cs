using ModelicaGraph;
using ModelicaGraph.DataTypes;
using Xunit;

namespace ModelicaGraph.Tests;

/// <summary>
/// B245 — which of a package's children can have their own file. The question is about the
/// <b>directory entry</b> each would be written as, not about its class name.
/// </summary>
public class PackageFileLayoutTests
{
    private static ModelNode Child(string name, string classType = "model",
        bool standalone = true, string? code = null)
        => new("P." + name, name, code ?? $"{classType} {name} end {name};")
        {
            ClassType = classType,
            ParentModelName = "P",
            CanBeStoredStandalone = standalone
        };

    [Fact]
    public void APackageIsADirectoryAndEverythingElseIsAFile()
    {
        Assert.Equal("Jfet", PackageFileLayout.EntryFor(Child("Jfet", "package")));
        Assert.Equal("JFET.mo", PackageFileLayout.EntryFor(Child("JFET")));
        Assert.Equal("R.mo", PackageFileLayout.EntryFor(Child("R", "record")));
    }

    [Fact]
    public void AShortClassDefinitionIsAFile_EvenWhenItIsAPackage()
    {
        // 'package Alias = Other;' has no contents to put in a directory.
        var alias = Child("Alias", "package", code: "package Alias = Other;");
        Assert.False(PackageFileLayout.WrittenAsDirectory(alias));
        Assert.Equal("Alias.mo", PackageFileLayout.EntryFor(alias));
    }

    [Fact]
    public void AModelAndAPackageDifferingOnlyInCase_CanBothBeStored()
    {
        // JFET.mo beside the directory Jfet: no filesystem anywhere confuses those two.
        var names = PackageFileLayout.StandaloneChildNames(
            [Child("JFET"), Child("Jfet", "package")]);

        Assert.Equal(new HashSet<string> { "JFET", "Jfet" }, names);
    }

    [Fact]
    public void TwoModelsDifferingOnlyInCase_CanNeitherBeStored()
    {
        Assert.Empty(PackageFileLayout.StandaloneChildNames([Child("JFET"), Child("Jfet")]));
    }

    [Fact]
    public void TwoPackagesDifferingOnlyInCase_CanNeitherBeStored()
    {
        Assert.Empty(PackageFileLayout.StandaloneChildNames(
            [Child("JFET", "package"), Child("Jfet", "package")]));
    }

    [Fact]
    public void AClassThatCannotStandAlone_IsNeverStored()
    {
        // replaceable, redeclare, inner, outer — the language's own reason, asked first.
        Assert.Empty(PackageFileLayout.StandaloneChildNames([Child("A", standalone: false)]));
    }

    [Fact]
    public void ReservedEntries_AreRefusedForAFileAndAllowedForADirectory()
    {
        // 'Package.mo' would land on the package.mo the directory already holds; 'Package' would not.
        Assert.Empty(PackageFileLayout.StandaloneChildNames([Child("Package")]));
        Assert.Equal(
            new HashSet<string> { "Package" },
            PackageFileLayout.StandaloneChildNames([Child("Package", "package")]));
    }

    [Fact]
    public void NamesThatDoNotCollide_AreAllStored()
    {
        var names = PackageFileLayout.StandaloneChildNames(
            [Child("A"), Child("B", "package"), Child("C", "record")]);

        Assert.Equal(new HashSet<string> { "A", "B", "C" }, names);
    }

    [Fact]
    public void TheShortClassQuestionIsAskedOnlyWhereItCanMatter()
    {
        // Two children with different names cannot produce the same entry, so nothing needs asking
        // of them — which is what keeps this off the parser for a library of tens of thousands. Of
        // the colliding pair only the package is asked; a model is written as a file either way.
        var asked = new List<string>();
        PackageFileLayout.StandaloneChildNames(
            [Child("A"), Child("B", "package"), Child("C"), Child("c", "package")],
            m => { asked.Add(m.Id); return false; });

        Assert.Equal(["P.c"], asked);
    }

    [Fact]
    public void ACallersOwnAnswerIsUsed()
    {
        // The saver holds every parse tree at this point and hands its answer in rather than have
        // it re-derived. Told both are short classes, both want the same .mo file.
        Assert.Empty(PackageFileLayout.StandaloneChildNames(
            [Child("JFET", "package"), Child("Jfet", "package")], _ => true));
    }
}
