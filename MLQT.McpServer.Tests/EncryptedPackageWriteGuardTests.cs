using ModelicaGraph;
using MLQT.McpServer.Dtos;
using MLQT.McpServer.Helpers;
using Xunit;

namespace MLQT.McpServer.Tests;

/// <summary>
/// An encrypted <c>package.moe</c> is never written to, whatever the filesystem says.
///
/// <para>A class recovered from a vendor's documentation carries a file node pointing at the
/// encrypted package it came from — the honest answer to where it lives — so an edit tool taking the
/// class's file path at face value would write synthesized Modelica over an encrypted binary. The
/// only guard used to be <c>FileWritability</c>, which infers read-only from filesystem permissions
/// and so protects a library under <c>Program Files</c> and nothing installed in a home directory or
/// on a share. The design note calls this the highest-severity failure mode and asks for a refusal,
/// which is what <c>ModelicaPackageSaver</c> does on the desktop side; this is the same refusal on
/// the path that does not go through it (backlog B85).</para>
/// </summary>
public class EncryptedPackageWriteGuardTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "mlqt-moe-guard", Guid.NewGuid().ToString("N"));

    public EncryptedPackageWriteGuardTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    /// <summary>A perfectly writable encrypted package — the case permissions do not catch.</summary>
    private string WritableEncryptedPackage()
    {
        var path = Path.Combine(_dir, "package.moe");
        File.WriteAllBytes(path, new byte[] { 0x4D, 0x4F, 0x45, 0x00, 0x01 });
        return path;
    }

    private string WritableSourceFile()
    {
        var path = Path.Combine(_dir, "Foo.mo");
        File.WriteAllText(path, "model Foo end Foo;");
        return path;
    }

    [Fact]
    public void AWritableEncryptedPackage_IsNotWritable()
    {
        var path = WritableEncryptedPackage();

        // The filesystem would allow it, which is the whole point.
        Assert.True(new FileInfo(path).Exists);
        Assert.False(FileWritability.IsWritable(path));
    }

    [Fact]
    public void AnOrdinarySourceFileBesideIt_Is()
        => Assert.True(FileWritability.IsWritable(WritableSourceFile()));

    [Fact]
    public void AWriteToAnEncryptedPackage_IsRefusedAndSaysWhy()
    {
        var error = FileWritability.RequireWritable(WritableEncryptedPackage(), "update this class");

        var message = Assert.IsType<ToolError>(error).Error;
        // Not the permissions message: an agent told "needs admin rights" would go looking for a way
        // to get them, and there is no version of this that should succeed.
        Assert.Contains("encrypted", message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("admin rights", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AMultiFileEditTouchingOne_IsRefusedWhole()
    {
        // All-or-nothing, as the permissions pre-flight already is: a move that rewrites three files
        // and one encrypted package must change none of them.
        var source = WritableSourceFile();
        var encrypted = WritableEncryptedPackage();

        Assert.NotNull(FileWritability.PreflightWritable([source, encrypted], "move this class"));
        Assert.Null(FileWritability.PreflightWritable([source], "move this class"));
    }

    [Fact]
    public void TheExtensionIsDecidedInOnePlace()
    {
        // ExternalStubBuilder owns it, and DirectedGraph asks the same question when deciding whether
        // a class's file is an encrypted package — two spellings of ".moe" is one edit away from a
        // guard that covers one caller and not the other.
        Assert.True(ExternalStubBuilder.IsEncryptedPackageFile("C:/lib/Battery/package.moe"));
        Assert.True(ExternalStubBuilder.IsEncryptedPackageFile("C:/lib/Battery/PACKAGE.MOE"));
        Assert.False(ExternalStubBuilder.IsEncryptedPackageFile("C:/lib/Battery/package.mo"));
        Assert.False(ExternalStubBuilder.IsEncryptedPackageFile(null));
    }
}
