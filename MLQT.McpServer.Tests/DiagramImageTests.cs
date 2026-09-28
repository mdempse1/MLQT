using System.Text;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using MLQT.McpServer.Dtos;
using MLQT.McpServer.Services;
using MLQT.McpServer.Tools;
using Xunit;

namespace MLQT.McpServer.Tests;

/// <summary>
/// <c>get_diagram_image</c> — the picture an agent could not see (B196).
///
/// <para>The diagram tools let an agent place components and wire them up and then reported, in
/// coordinates, what it had just done. These check the three things that make an image worth
/// returning at all: it contains what the class contains, it is a real PNG, and it reaches a client
/// as an image rather than as a paragraph about one.</para>
/// </summary>
public class DiagramImageTests
{
    /// <summary>
    /// Two placed components with icons of their own, wired together, plus one component whose type
    /// is not loaded — the case that must not look like an empty space.
    /// </summary>
    private const string Package = """
        within;
        package Lib "l"
          connector Pin "pin"
            Real v;
            annotation (Icon(graphics={Ellipse(extent={{-40,-40},{40,40}}, lineColor={0,0,255})}));
          end Pin;

          model Source "src"
            Pin p "out" annotation (Placement(transformation(extent={{90,-10},{110,10}})));
            annotation (Icon(graphics={
              Rectangle(extent={{-100,-100},{100,100}}, lineColor={0,0,0}),
              Text(extent={{-100,-140},{100,-110}}, textString="%name")}));
          end Source;

          model Sink "snk"
            Pin p "in" annotation (Placement(transformation(extent={{-110,-10},{-90,10}})));
            annotation (Icon(graphics={Ellipse(extent={{-100,-100},{100,100}}, lineColor={255,0,0})}));
          end Sink;

          model Wired "a wired model"
            Source src annotation (Placement(transformation(extent={{-60,-10},{-40,10}})));
            Sink snk annotation (Placement(transformation(extent={{40,-10},{60,10}})));
            NotLoaded.Thing mystery annotation (Placement(transformation(extent={{-10,40},{10,60}})));
          equation
            connect(src.p, snk.p) annotation (Line(points={{-40,0},{40,0}}, color={0,0,255}));
          end Wired;

          model Bare "nothing placed"
            Real x;
          end Bare;
        end Lib;
        """;

    private static TestHost Load()
    {
        var host = new TestHost();
        var dir = host.WriteLibraryDir(new Dictionary<string, string>
        {
            ["package.mo"] = Package,
            ["package.order"] = "Pin\nSource\nSink\nWired\nBare\n",
        });
        host.Libraries.AddLibraryFromDirectoryAsync(dir).GetAwaiter().GetResult();
        return host;
    }

    /// <summary>The declared canvas's outline, named by the colour nothing else uses.</summary>
    private const string CanvasOutline = "stroke=\"#c0c0c0\"";

    private static DiagramTools Tools(TestHost host)
        => new(host.Libraries, host.Resources, host.Session);

    private static string Svg(TestHost host, string classId, int width = 800)
    {
        var node = host.Libraries.GetModelById(classId);
        Assert.NotNull(node);
        var svg = MLQT.McpServer.Helpers.DiagramImage.RenderSvg(host.Libraries, node!, width);
        Assert.NotNull(svg);
        return svg!;
    }

    [Fact]
    public void TheDiagramHoldsTheComponentsItsIconsAndItsConnection()
    {
        using var host = Load();
        var svg = Svg(host, "Lib.Wired");

        // Source's own icon, drawn through its component's transform, with %name resolved.
        Assert.Contains(">src<", svg);
        // Sink's icon is an ellipse in red; Pin's connectors are not drawn (they are in the icon layer).
        Assert.Contains("#FF0000", svg);
        // The connection's own Line annotation, not a re-route.
        Assert.Contains("<polyline", svg);
        Assert.Contains("#0000FF", svg);
    }

    [Fact]
    public void AComponentWhoseTypeIsNotLoadedIsDrawnRatherThanOmitted()
    {
        // "MLQT could not resolve this type" and "there is no component here" must not look the same
        // to an agent judging its own layout.
        using var host = Load();
        var svg = Svg(host, "Lib.Wired");

        Assert.Contains(">mystery<", svg);
        Assert.Contains("stroke-dasharray", svg);
    }

    [Fact]
    public void TheViewGrowsToHoldWhatWasPlacedOutsideTheCanvas()
    {
        using var host = Load();

        var before = Svg(host, "Lib.Wired");

        ToolAssert.Ok<StructureEditResult>(Tools(host)
            .SetComponentPlacement("Lib.Wired", "snk", 400, 400, 420, 420).GetAwaiter().GetResult());

        var after = Svg(host, "Lib.Wired");

        // The declared canvas is outlined only once something is outside it - inside, the frame is
        // the edge of the image and drawing it says nothing.
        Assert.DoesNotContain(CanvasOutline, before);
        Assert.Contains(CanvasOutline, after);
    }

    [Fact]
    public void AClassWithNothingPlacedSaysSo_RatherThanReturningABlankImage()
    {
        using var host = Load();

        var error = Assert.IsType<ToolError>(Tools(host).GetDiagramImage("Lib.Bare"));
        Assert.Contains("nothing to draw", error.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WhatComesBackIsARealPng()
    {
        using var host = Load();

        var image = Assert.IsType<ImageContentBlock>(Tools(host).GetDiagramImage("Lib.Wired"));
        Assert.Equal("image/png", image.MimeType);

        var png = Convert.FromBase64String(Encoding.UTF8.GetString(image.Data.Span));
        Assert.True(png.Length > 100, "an empty rasterisation is the failure this is here to catch");
        Assert.Equal<byte[]>([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A], png[..8]);

        // ...and it is the size that was asked for. IHDR width is bytes 16-19, big-endian.
        Assert.Equal(800, (png[16] << 24) | (png[17] << 16) | (png[18] << 8) | png[19]);
    }

    /// <summary>
    /// Through the SDK, because a tool that returns an image and a tool that returns a paragraph
    /// describing one are the same method signature, and only the wire format tells them apart.
    /// </summary>
    [Fact]
    public async Task TheClientReceivesItAsAnImage()
    {
        using var host = Load();
        var tools = Tools(host);
        var tool = McpServerTool.Create(
            tools.GetDiagramImage, new McpServerToolCreateOptions { Name = "get_diagram_image" });

        // A real server, because it is the SDK that decides what a returned object becomes on the
        // wire: everything else here returns a DTO and arrives as JSON text, and an image that
        // arrived the same way would be a paragraph describing a picture.
        await using var transport = new StreamServerTransport(new MemoryStream(), new MemoryStream());
        await using var server = ModelContextProtocol.Server.McpServer.Create(transport, new McpServerOptions
        {
            ServerInfo = new Implementation { Name = "mlqt-test", Version = "1.0" },
        });

        var result = await tool.InvokeAsync(
            new RequestContext<CallToolRequestParams>(
                server, new JsonRpcRequest { Method = "tools/call" },
                new CallToolRequestParams
                {
                    Name = "get_diagram_image",
                    Arguments = new Dictionary<string, JsonElement>
                    {
                        ["classId"] = JsonSerializer.SerializeToElement("Lib.Wired"),
                    },
                }),
            TestContext.Current.CancellationToken);

        var block = Assert.Single(result.Content);
        var image = Assert.IsType<ImageContentBlock>(block);
        Assert.Equal("image/png", image.MimeType);
    }
}
