using System.Security.Cryptography;
using System.Text;

namespace ClearVision.Product.Application.Security;

/// <summary>
/// Produces the stable, non-reversible owner authority shared by authenticated
/// HTTP requests, WebView messages, conversation sessions, and Agent runs.
/// </summary>
public static class AuthenticatedOwnerResolver
{
    private const string Prefix = "agent-run-owner:";

    public static string ResolveOwnerHash(string? userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return string.Empty;
        }

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(Prefix + userId.Trim()));
        return "usr_" + Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static bool IsValidOwnerHash(string? ownerHash)
    {
        return ownerHash is { Length: 68 } &&
               ownerHash.StartsWith("usr_", StringComparison.Ordinal) &&
               ownerHash.AsSpan(4).ToString().All(character =>
                   character is >= '0' and <= '9' or >= 'a' and <= 'f');
    }
}
