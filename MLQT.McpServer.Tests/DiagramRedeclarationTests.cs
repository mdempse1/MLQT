using System.Text.RegularExpressions;
using MLQT.McpServer.Dtos;
using MLQT.McpServer.Helpers;
using MLQT.McpServer.Tools;
using ModelicaGraph;
using Xunit;

namespace MLQT.McpServer.Tests;

/// <summary>
/// A component is drawn as the class it is after its redeclarations, not the class it was declared
/// with. A library built the template way declares its components <c>replaceable</c>, constrained by a
/// partial class with an empty icon, and a model extends the template and redeclares them; drawn with
/// the declared classes, such a model was a page of blank boxes with no names - the template, as a
/// tool shows it before anything is redeclared.
///
/// <para>Each class's icon has a colour of its own, so the SVG says which class was drawn. Every test
/// runs twice: from a directory, and with the same text supplied by a host as a read-only library.</para>
/// </summary>
public class DiagramRedeclarationTests
{
    private static readonly string Package = """
        within;
        package R "r"
          connector Port "port"
            Real v;
            annotation (Icon(graphics={Ellipse(extent={{-40,-40},{40,40}}, lineColor={0,0,255})}));
          end Port;

          partial model PartialEngine "constraining, with no icon"
            Port shaft annotation (Placement(transformation(extent={{-110,-10},{-90,10}})));
          end PartialEngine;

          model Diesel "a concrete engine, with a second port on its right and an optional one on top"
            extends PartialEngine;
            parameter Real k = 1;
            parameter Boolean useAux = false;
            Port flange annotation (Placement(transformation(extent={{90,-10},{110,10}})));
            Port aux if useAux annotation (Placement(transformation(extent={{-10,90},{10,110}})));
            annotation (Icon(graphics={Rectangle(extent={{-100,-100},{100,100}}, lineColor={17,34,51}),
              Text(extent={{-100,110},{100,130}}, textString="%name"),
              Text(extent={{-100,-130},{100,-110}}, textString="k=%k")}));
          end Diesel;

          partial model PartialGearbox "constraining, with no icon"
            Port shaft annotation (Placement(transformation(extent={{-110,-10},{-90,10}})));
          end PartialGearbox;

          model Manual "a concrete gearbox"
            extends PartialGearbox;
            parameter Real ratio = 1;
            annotation (Icon(graphics={Rectangle(extent={{-100,-100},{100,100}}, lineColor={170,187,204}),
              Text(extent={{-100,110},{100,130}}, textString="%name"),
              Text(extent={{-100,-130},{100,-110}}, textString="ratio=%ratio")}));
          end Manual;

          model Plain "a block nobody redeclares"
            annotation (Icon(graphics={Ellipse(extent={{-100,-100},{100,100}}, lineColor={68,85,102})}));
          end Plain;

          model ShortPlain = Plain "a short class with no icon of its own";

          connector AliasPort = Port "a library's alias of a connector";

          model WithAlias "a block whose port is an alias"
            AliasPort p annotation (Placement(transformation(extent={{-110,-10},{-90,10}})));
            annotation (Icon(graphics={Rectangle(extent={{-100,-100},{100,100}}, lineColor={4,5,6})}));
          end WithAlias;

          model Uses "a short-class component and an alias-port component"
            ShortPlain s annotation (Placement(transformation(extent={{-60,-10},{-40,10}})));
            WithAlias w annotation (Placement(transformation(extent={{40,-10},{60,10}})));
          end Uses;

          partial model Template "a replaceable component and a replaceable class"
            replaceable PartialEngine engine constrainedby PartialEngine
              annotation (Placement(transformation(extent={{-60,-10},{-40,10}})));
            replaceable model Gearbox = PartialGearbox;
            Gearbox gearbox annotation (Placement(transformation(extent={{40,-10},{60,10}})));
            Plain plain annotation (Placement(transformation(extent={{-10,30},{10,50}})));
          end Template;

          model ByExtends "redeclares in its extends clause"
            extends Template(redeclare Diesel engine(k = 7, useAux = true),
              redeclare model Gearbox = Manual(ratio = 3));
          equation
            connect(engine.flange, gearbox.shaft);
          end ByExtends;

          model InBody "redeclares in its body, with no annotation of its own"
            extends Template;
            redeclare Diesel engine(k = 7, useAux = true);
            redeclare model Gearbox = Manual(ratio = 3);
          equation
            connect(engine.flange, gearbox.shaft);
          end InBody;

          model Deeper "extends a model that redeclares"
            extends ByExtends;
          end Deeper;

          model Missing "redeclares to a class that is not loaded"
            extends Template(redeclare NotLoaded engine);
          end Missing;
        end R;
        """.Replace("\r\n", "\n");

    private sealed class Supplied : IReadOnlyClassSource
    {
        public string LibraryName => "R";
        public string? LibraryVersion => null;
        public ReadOnlySourceKind Kind => ReadOnlySourceKind.Supplied;
        public string? Location => null;
        public string ProvenanceNote => "Supplied by a test host.";

        public ReadOnlySourceContent Read(CancellationToken cancellationToken) =>
            new() { Texts = [new SuppliedText("R/package.mo", Package)] };
    }

    private static TestHost Load(bool supplied)
    {
        var host = new TestHost();
        if (supplied)
        {
            host.Libraries.AddLibraryFromSourceAsync(new Supplied()).GetAwaiter().GetResult();
        }
        else
        {
            var dir = host.WriteLibraryDir(new Dictionary<string, string> { ["package.mo"] = Package });
            host.Libraries.AddLibraryFromDirectoryAsync(dir).GetAwaiter().GetResult();
        }
        return host;
    }

    private static string Svg(TestHost host, string classId)
        => DiagramImage.RenderSvg(host.Libraries, host.Libraries.GetModelById(classId)!, 800)!;

    private const string DieselColour = "#112233";
    private const string ManualColour = "#AABBCC";
    private const string PlainColour = "#445566";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheTemplate_DrawsItsConstrainingClasses(bool supplied)
    {
        using var host = Load(supplied);
        var svg = Svg(host, "R.Template");

        Assert.DoesNotContain(DieselColour, svg);
        Assert.DoesNotContain(ManualColour, svg);
        Assert.Contains(PlainColour, svg);
    }

    [Theory]
    [InlineData(false, "R.ByExtends")]
    [InlineData(true, "R.ByExtends")]
    [InlineData(false, "R.InBody")]
    [InlineData(true, "R.InBody")]
    [InlineData(false, "R.Deeper")]
    [InlineData(true, "R.Deeper")]
    public void ARedeclaredComponent_IsDrawnAsTheClassItIsRedeclaredTo(bool supplied, string classId)
    {
        using var host = Load(supplied);
        var svg = Svg(host, classId);

        // `redeclare Diesel engine`: the component's own redeclaration.
        Assert.Contains(DieselColour, svg);
        Assert.Contains(">engine<", svg);
        // `redeclare model Gearbox = Manual`: the replaceable class the component is declared with.
        Assert.Contains(ManualColour, svg);
        Assert.Contains(">gearbox<", svg);
        // Nothing redeclares it, so it is its declared class.
        Assert.Contains(PlainColour, svg);
    }

    /// <summary>
    /// What the redeclaration gives the component is what its icon shows: <c>k = 7</c> in its label,
    /// and the optional port <c>useAux = true</c> switches on - three ports on the engine and one on
    /// the gearbox. Read from the declaration it replaced, the label said 1 and the port was off. A
    /// class redeclared with modifications (<c>Manual(ratio = 3)</c>) gives the gearbox its defaults.
    /// </summary>
    [Theory]
    [InlineData(false, "R.ByExtends")]
    [InlineData(true, "R.ByExtends")]
    [InlineData(false, "R.InBody")]
    [InlineData(true, "R.InBody")]
    [InlineData(false, "R.Deeper")]
    [InlineData(true, "R.Deeper")]
    public void WhatTheRedeclarationGivesTheComponent_IsWhatItsIconShows(bool supplied, string classId)
    {
        using var host = Load(supplied);
        var svg = Svg(host, classId);

        Assert.Contains(">k=7<", svg);
        Assert.Contains(">ratio=3<", svg);
        Assert.Equal(4, Regex.Matches(svg, "#0000FF").Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AShortClassComponent_AndAnAliasConnector_AreDrawnAsWhatTheyAlias(bool supplied)
    {
        using var host = Load(supplied);
        var svg = Svg(host, "R.Uses");

        // `model ShortPlain = Plain` wears Plain's icon; `connector AliasPort = Port` is drawn as Port
        // on the icon of the block that has one.
        Assert.Contains(PlainColour, svg);
        Assert.Single(Regex.Matches(svg, "#0000FF"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ARedeclarationToAClassNotLoaded_IsReportedAsOne(bool supplied)
    {
        using var host = Load(supplied);
        var tools = new DiagramTools(host.Libraries, host.Resources, host.Session);

        var layout = ToolAssert.Ok<DiagramLayoutResult>(tools.GetDiagramLayout("R.Missing"));

        // Named as written: not "not redeclared", and not the constraining class it replaced.
        Assert.Equal("NotLoaded", layout.Components.Single(c => c.Name == "engine").RedeclaredTo);
        Assert.Null(layout.Components.Single(c => c.Name == "plain").RedeclaredTo);
        // Drawn as a class that is not loaded: an outlined box with its name.
        Assert.Contains(">engine</text>", Svg(host, "R.Missing"));
    }

    /// <summary>
    /// The line to <c>engine.flange</c> ends where Diesel puts its flange, on the engine's right edge
    /// (x = -40). PartialEngine has no flange, so a line routed against the declared class went to the
    /// component's centre (x = -50).
    /// </summary>
    [Theory]
    [InlineData(false, "R.ByExtends")]
    [InlineData(true, "R.ByExtends")]
    [InlineData(false, "R.InBody")]
    [InlineData(true, "R.InBody")]
    public void AConnectionEndsOnTheRedeclaredClassesConnector(bool supplied, string classId)
    {
        using var host = Load(supplied);

        var line = Assert.Single(Regex.Matches(Svg(host, classId), "<polyline points=\"([^\"]+)\"")).Groups[1].Value;

        Assert.StartsWith("-40,0 ", line);
        Assert.EndsWith(" 40,0", line);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheLayoutView_NamesTheClassARedeclarationMakesAComponent(bool supplied)
    {
        using var host = Load(supplied);
        var tools = new DiagramTools(host.Libraries, host.Resources, host.Session);

        var layout = ToolAssert.Ok<DiagramLayoutResult>(tools.GetDiagramLayout("R.ByExtends"));

        Assert.Equal("R.Diesel", layout.Components.Single(c => c.Name == "engine").RedeclaredTo);
        Assert.Equal("R.Manual", layout.Components.Single(c => c.Name == "gearbox").RedeclaredTo);
        Assert.Null(layout.Components.Single(c => c.Name == "plain").RedeclaredTo);
        Assert.Equal("PartialEngine", layout.Components.Single(c => c.Name == "engine").Type);
    }
}
