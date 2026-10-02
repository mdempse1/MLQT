using MLQT.McpServer.Dtos;
using MLQT.McpServer.Tools;

namespace MLQT.McpServer.Tests;

/// <summary>
/// A rename rewrites a name where it was <b>bound</b> to the renamed class, and nowhere else: not a
/// local alias or short class that happens to share its name, but every <c>redeclare</c> that replaces
/// it. Moves, for the same reason, judge a full name where it will be written, not where it was.
/// </summary>
/// <remarks>
/// Each test is a shape a whole-branch review found: an alias called after the class was rewritten
/// with it; renaming a replaceable package left every <c>redeclare package Medium = ...</c> naming an
/// element that no longer existed; an import clause, which is looked up from the top, was checked for
/// capture as if it were looked up from its class; a quoted identifier with a dot in it was cut at
/// that dot; and a move judged the names inside the moved class from where it had been.
/// </remarks>
public class RenameClassBindingTests
{
    private static async Task<EditTools> Load(TestHost h, string package)
    {
        var dir = h.WriteLibraryDir(new Dictionary<string, string> { ["package.mo"] = package });
        await h.Libraries.AddLibraryFromDirectoryAsync(dir);
        await new DependencyTools(h.Libraries, h.Impact, h.Resources, h.Session).AnalyzeDependencies();
        return new EditTools(h.Libraries, h.Resources, h.Session);
    }

    private static string Code(TestHost h, string id) =>
        h.Libraries.GetModelById(id)!.Definition.ModelicaCode.Replace("\r\n", "\n");

    // ---- aliases --------------------------------------------------------------------------------

    private const string Aliases = """
        within;
        package Root "root"
          package Interfaces
            connector RealInput = input Real;
          end Interfaces;
          model ByImport
            import Interfaces = Root.Interfaces;
            Interfaces.RealInput u;
          end ByImport;
          model ByShortClass
            package Interfaces = Root.Interfaces;
            Interfaces.RealInput u;
          end ByShortClass;
          model Direct
            Interfaces.RealInput u;
          end Direct;
        end Root;
        """;

    [Fact]
    public async Task AnAliasNamedLikeTheClass_IsLeftAsWritten_AndItsTargetIsRenamed()
    {
        // `Interfaces` in ByImport is the alias, not the class: renaming the class renames what the
        // alias stands for, and `Interfaces.RealInput` still means the same connector.
        using var host = new TestHost();
        var edit = await Load(host, Aliases);

        ToolAssert.Ok<RenameClassResult>(await edit.RenameClass("Root.Interfaces", "Ports"));

        var byImport = Code(host, "Root.ByImport");
        Assert.Contains("import Interfaces = Root.Ports;", byImport);
        Assert.Contains("Interfaces.RealInput u;", byImport);
    }

    [Fact]
    public async Task ALocalShortClassNamedLikeTheClass_IsLeftAsWritten_AndItsBaseIsRenamed()
    {
        using var host = new TestHost();
        var edit = await Load(host, Aliases);

        ToolAssert.Ok<RenameClassResult>(await edit.RenameClass("Root.Interfaces", "Ports"));

        var byShortClass = Code(host, "Root.ByShortClass");
        Assert.Contains("package Interfaces = Root.Ports;", byShortClass);
        Assert.Contains("Interfaces.RealInput u;", byShortClass);
        // ...where the name is the class itself, it is renamed.
        Assert.Contains("Ports.RealInput u;", Code(host, "Root.Direct"));
    }

    // ---- redeclarations -------------------------------------------------------------------------

    private const string Redeclarations = """
        within;
        package Root "root"
          package Water
            record State
            end State;
          end Water;
          partial model Base
            replaceable package Medium = Water;
            Medium.State s;
          end Base;
          model ByExtends
            extends Base(redeclare package Medium = Water);
          end ByExtends;
          model InBody
            extends Base;
            redeclare package Medium = Water;
            Medium.State t;
          end InBody;
          model Holder
            Base b;
          end Holder;
          model ByComponent
            ByExtends b(redeclare package Medium = Water);
            Holder h(b(redeclare package Medium = Water));
          end ByComponent;
          model Unrelated
            replaceable package Medium = Water;
            ByExtends e;
          end Unrelated;
        end Root;
        """;

    [Fact]
    public async Task RenamingAReplaceableClass_RenamesEveryRedeclarationOfIt()
    {
        // Left as they were, every `redeclare package Medium` would redeclare an element Base no longer
        // has: an error in each of them.
        using var host = new TestHost();
        var edit = await Load(host, Redeclarations);

        ToolAssert.Ok<RenameClassResult>(await edit.RenameClass("Root.Base.Medium", "Fluid"));

        Assert.Contains("replaceable package Fluid = Water;", Code(host, "Root.Base"));
        Assert.Contains("extends Base(redeclare package Fluid = Water);", Code(host, "Root.ByExtends"));
        var inBody = Code(host, "Root.InBody");
        Assert.Contains("redeclare package Fluid = Water;", inBody);
        Assert.Contains("Fluid.State t;", inBody);
        var byComponent = Code(host, "Root.ByComponent");
        Assert.Contains("ByExtends b(redeclare package Fluid = Water);", byComponent);
        Assert.Contains("Holder h(b(redeclare package Fluid = Water));", byComponent);
    }

    [Fact]
    public async Task AReplaceableClassOfTheSameName_ThatIsNotARedeclaration_IsLeftAlone()
    {
        using var host = new TestHost();
        var edit = await Load(host, Redeclarations);

        ToolAssert.Ok<RenameClassResult>(await edit.RenameClass("Root.Base.Medium", "Fluid"));

        Assert.Contains("replaceable package Medium = Water;", Code(host, "Root.Unrelated"));
    }

    // ---- capture --------------------------------------------------------------------------------

    [Fact]
    public async Task AnImportClause_IsNotCapturedByAClassOfTheNewName()
    {
        // `import Root.Pkg.State` becomes `import Root.Box.State`, which is looked up from the top, so
        // Imp's own Box does not stand in its way. `State v` does not spell the name at all.
        using var host = new TestHost();
        var edit = await Load(host, """
            within;
            package Root "root"
              package Pkg
                record State
                end State;
              end Pkg;
              model Imp
                package Box
                end Box;
                import Root.Pkg.State;
                State v;
              end Imp;
            end Root;
            """);

        ToolAssert.Ok<RenameClassResult>(await edit.RenameClass("Root.Pkg", "Box"));

        Assert.Contains("import Root.Box.State;", Code(host, "Root.Imp"));
    }

    // ---- quoted identifiers ---------------------------------------------------------------------

    [Fact]
    public async Task AClassInAQuotedPackageWithADot_IsRenamedWhereItIsUsed()
    {
        using var host = new TestHost();
        var edit = await Load(host, """
            within;
            package Root "root"
              package 'a.b'
                model C
                end C;
              end 'a.b';
              model User
                'a.b'.C c;
                Root.'a.b'.C d;
              end User;
            end Root;
            """);

        ToolAssert.Ok<RenameClassResult>(await edit.RenameClass("Root.'a.b'.C", "D"));

        Assert.NotNull(host.Libraries.GetModelById("Root.'a.b'.D"));
        var user = Code(host, "Root.User");
        Assert.Contains("'a.b'.D c;", user);
        Assert.Contains("Root.'a.b'.D d;", user);
    }

    // ---- moves ----------------------------------------------------------------------------------

    [Fact]
    public async Task AMove_JudgesTheNamesInsideTheMovedClass_WhereTheyWillBe()
    {
        // Pkg.Inner's `Root.Src.Pkg.Thing` is re-qualified to `Root.Dst.Pkg.Thing` - and Inner will be
        // in Dst, where Root is the component. Judged from Src, where it had been, nothing stood in the
        // way and the move wrote a name that does not mean the class.
        using var host = new TestHost();
        var edit = await Load(host, """
            within;
            package Root "root"
              package Src
                package Pkg
                  record Thing
                  end Thing;
                  model Inner
                    Root.Src.Pkg.Thing t;
                  end Inner;
                end Pkg;
              end Src;
              package Dst
                constant Real Root = 1;
              end Dst;
            end Root;
            """);

        var error = Assert.IsType<ToolError>(await edit.MoveClass("Root.Src.Pkg", "Root.Dst")).Error;

        Assert.Contains("component Root of Root.Dst", error);
        Assert.Contains("Nothing was changed", error);
        Assert.NotNull(host.Libraries.GetModelById("Root.Src.Pkg.Inner"));
    }
}
