using ModelicaParser.Comparison;

namespace ModelicaParser.Tests;

/// <summary>
/// What <see cref="ClassSignature.Semantic"/> leaves out is kept by kind, so a caller can say
/// <i>which</i> cosmetic change an edit was — "only the graphics changed", "only the documentation
/// changed" — from <see cref="ClassSignatures.Of"/> alone, without repeating MLQT's list of
/// display-only annotations and drifting from it.
/// </summary>
public class CosmeticSignatureTests
{
    private static readonly string Original = Lf("""
        within MyLib;
        model Resistor "An ideal resistor"
          parameter Real R = 100 "Resistance"
            annotation (Dialog(group = "Electrical"));
          Real v;
          Real i;
        equation
          v = R * i;
          annotation (Icon(graphics={Rectangle(extent={{-70,30},{70,-30}})}),
            Documentation(info="<html>A resistor.</html>"),
            defaultComponentName = "resistor",
            Evaluate = true,
            __MLQT(suppress = {"MLQT.Style.Example"}));
        end Resistor;
        """);

    private static string Lf(string source) => source.Replace("\r\n", "\n");

    private static ClassSignature Sign(string text) =>
        Assert.Contains("MyLib.Resistor", ClassSignatures.Of(text).Classes);

    /// <summary>
    /// The one property that changed among the cosmetic ones, or "none"; and the semantic stream
    /// must not have, or the edit was not cosmetic at all.
    /// </summary>
    private static string WhatChanged(string edited)
    {
        var before = Sign(Original);
        var after = Sign(edited);
        Assert.Equal(before.Semantic, after.Semantic);

        var changed = new List<string>();
        if (before.Graphics != after.Graphics) changed.Add(nameof(ClassSignature.Graphics));
        if (before.Documentation != after.Documentation) changed.Add(nameof(ClassSignature.Documentation));
        if (before.Dialog != after.Dialog) changed.Add(nameof(ClassSignature.Dialog));
        if (before.Tooling != after.Tooling) changed.Add(nameof(ClassSignature.Tooling));
        return changed.Count == 0 ? "none" : string.Join(",", changed);
    }

    [Fact]
    public void AnIconEdit_ChangesOnlyTheGraphics() =>
        Assert.Equal("Graphics", WhatChanged(Original.Replace("{-70,30},{70,-30}", "{-80,40},{80,-40}")));

    [Fact]
    public void ADocumentationEdit_ChangesOnlyTheDocumentation() =>
        Assert.Equal("Documentation", WhatChanged(Original.Replace("A resistor.", "An ideal linear resistor.")));

    [Fact]
    public void ADescriptionEdit_IsDocumentationToo() =>
        Assert.Equal("Documentation", WhatChanged(Original.Replace("\"Resistance\"", "\"Resistance at 20 degC\"")));

    [Fact]
    public void ADialogEdit_ChangesOnlyTheDialog() =>
        Assert.Equal("Dialog", WhatChanged(Original.Replace("\"Electrical\"", "\"Parameters\"")));

    [Fact]
    public void AnMlqtDirective_IsTooling() =>
        Assert.Equal("Tooling", WhatChanged(Original.Replace("MLQT.Style.Example", "MLQT.Style.Other")));

    [Fact]
    public void ReorderingAnAnnotationsElements_ChangesNothing()
    {
        var reordered = Original.Replace(
            """
            annotation (Icon(graphics={Rectangle(extent={{-70,30},{70,-30}})}),
                Documentation(info="<html>A resistor.</html>"),
                defaultComponentName = "resistor",
            """.Replace("\r\n", "\n"),
            """
            annotation (defaultComponentName = "resistor",
                Documentation(info="<html>A resistor.</html>"),
                Icon(graphics={Rectangle(extent={{-70,30},{70,-30}})}),
            """.Replace("\r\n", "\n"));
        Assert.NotEqual(Original, reordered);

        Assert.Equal("none", WhatChanged(reordered));
    }

    [Fact]
    public void ReorderingTwoElementsOfOneKind_ChangesNothing()
    {
        // Within a category, as the kept elements are: an Icon and a Diagram written the other way
        // round draw the same class.
        const string iconFirst = "within MyLib;\nmodel Resistor\n  annotation (Icon(graphics={Line(points={{0,0},{1,1}})}), Diagram(graphics={Line(points={{2,2},{3,3}})}));\nend Resistor;\n";
        const string diagramFirst = "within MyLib;\nmodel Resistor\n  annotation (Diagram(graphics={Line(points={{2,2},{3,3}})}), Icon(graphics={Line(points={{0,0},{1,1}})}));\nend Resistor;\n";

        Assert.Equal(Sign(iconFirst).Graphics, Sign(diagramFirst).Graphics);
        Assert.NotEqual(Sign(iconFirst).Surface, Sign(diagramFirst).Surface);
    }

    [Fact]
    public void ReformattingAndComments_ChangeNoneOfThem()
    {
        var reformatted = Original
            .Replace("v = R * i;", "v = R*i; // Ohm's law")
            .Replace("Real v;", "Real   v;");

        Assert.Equal("none", WhatChanged(reformatted));
        Assert.NotEqual(Sign(Original).Surface, Sign(reformatted).Surface);
    }

    [Fact]
    public void WhatADisplayOnlyAnnotationHolds_IsInItsOwnPropertyAndNotInTheSemanticStream()
    {
        var signature = Sign(Original);

        Assert.Contains("Rectangle", signature.Graphics);
        Assert.Contains("A resistor.", signature.Documentation);
        Assert.Contains("Resistance", signature.Documentation);
        Assert.Contains("Electrical", signature.Dialog);
        Assert.Contains("resistor", signature.Dialog);
        Assert.Contains("MLQT.Style.Example", signature.Tooling);
        Assert.DoesNotContain("Rectangle", signature.Semantic);
        Assert.DoesNotContain("Electrical", signature.Semantic);
        // Kept where a translator acts on it.
        Assert.Contains("Evaluate", signature.Semantic);
        Assert.DoesNotContain("Evaluate", signature.Graphics + signature.Documentation + signature.Dialog + signature.Tooling);
    }

    [Fact]
    public void AClassWithNoneOfThem_HasEmptyProperties()
    {
        var signature = Sign("within MyLib;\nmodel Resistor\n  Real v;\nend Resistor;\n");

        Assert.Equal("", signature.Graphics);
        Assert.Equal("", signature.Documentation);
        Assert.Equal("", signature.Dialog);
        Assert.Equal("", signature.Tooling);
    }

    [Fact]
    public void ANestedClassesAnnotations_StayWithTheNestedClass()
    {
        var signatures = ClassSignatures.Of(Lf("""
            within MyLib;
            package P
              model Inner
                annotation (Icon(graphics={Line(points={{0,0},{1,1}})}));
              end Inner;
              annotation (Documentation(info="<html>The package.</html>"));
            end P;
            """)).Classes;

        Assert.DoesNotContain("Line", signatures["MyLib.P"].Graphics);
        Assert.Contains("The package.", signatures["MyLib.P"].Documentation);
        Assert.Contains("Line", signatures["MyLib.P.Inner"].Graphics);
        Assert.Equal("", signatures["MyLib.P.Inner"].Documentation);
    }

    [Fact]
    public void TheClassificationIsUnchanged()
    {
        // The properties are additive: what the classifier answers comes from Semantic and Surface
        // exactly as before.
        var edited = Original.Replace("{-70,30},{70,-30}", "{-80,40},{80,-40}");

        Assert.Equal(ClassChangeKind.Cosmetic,
            Assert.Contains("MyLib.Resistor", ClassChangeClassifier.Compare(Original, edited)));
    }

    // ---------------------------------------------------------------- the categories

    [Fact]
    public void EveryDisplayOnlyName_HasACategory()
    {
        // A name with none would be dropped from the semantic stream and kept nowhere, so a change
        // to it would read as no change at all.
        Assert.All(SimulationAnnotations.CosmeticNames, name =>
            Assert.NotNull(SimulationAnnotations.CategoryOf(name)));
    }

    [Theory]
    [InlineData("Icon", CosmeticCategory.Graphics)]
    [InlineData("Placement", CosmeticCategory.Graphics)]
    [InlineData("coordinateSystem", CosmeticCategory.Graphics)]
    [InlineData("Documentation", CosmeticCategory.Documentation)]
    [InlineData("revisions", CosmeticCategory.Documentation)]
    [InlineData("obsolete", CosmeticCategory.Documentation)]
    [InlineData("Dialog", CosmeticCategory.Dialog)]
    [InlineData("choices", CosmeticCategory.Dialog)]
    [InlineData("defaultComponentName", CosmeticCategory.Dialog)]
    [InlineData("__MLQT", CosmeticCategory.Tooling)]
    [InlineData("versionDate", CosmeticCategory.Tooling)]
    public void CategoryOf_SaysWhatADisplayOnlyAnnotationIsAbout(string name, CosmeticCategory category) =>
        Assert.Equal(category, SimulationAnnotations.CategoryOf(name));

    [Theory]
    [InlineData("Evaluate")]
    [InlineData("experiment")]
    [InlineData("version")]
    [InlineData("__Vendor_Unknown")]
    [InlineData("icon")]
    public void CategoryOf_IsNullForAnythingATranslatorMayActOn(string name)
    {
        Assert.Null(SimulationAnnotations.CategoryOf(name));
        Assert.True(SimulationAnnotations.AffectsSimulation(name));
    }
}
