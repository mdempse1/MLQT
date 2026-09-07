using System.Security.Cryptography;
using System.Text;

namespace ModelicaParser.DataTypes;

/// <summary>
/// Computes a stable, semantic fingerprint for a <see cref="Finding"/>.
///
/// The fingerprint is built from the rule identity plus the semantic location
/// (fully qualified model name + element + discriminator) and deliberately EXCLUDES the
/// line number, so it survives edits elsewhere in the file and â€” critically â€” survives a
/// full reformat by <c>ModelicaRenderer</c>, and a standalone class moving between
/// <c>package.mo</c> and its own file.
///
/// It uses a fixed hash (SHA-256), never <see cref="string.GetHashCode()"/>, because that is
/// randomised per process and would silently invalidate every stored baseline across restarts.
/// </summary>
public static class FindingFingerprint
{
    public static string Compute(string ruleId, string modelId, string? elementPath, string? discriminator)
    {
        // NUL separators keep the fields unambiguous â€” a NUL cannot appear in Modelica identifiers, and a discriminator may contain spaces.
        var raw = string.Join('\0',
            ruleId,
            modelId,
            elementPath ?? string.Empty,
            discriminator ?? string.Empty);

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));

        // A 128-bit (16-byte) hex prefix is ample for collision resistance and keeps the
        // baseline file compact.
        return Convert.ToHexString(hash.AsSpan(0, 16)).ToLowerInvariant();
    }
}
