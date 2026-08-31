using System.Security.Cryptography;

namespace ClearVision.Product.Infrastructure.Operators;

/// <summary>
/// Immutable identity of the exact ONNX bytes observed at a canonical filesystem path.
/// </summary>
internal readonly record struct OnnxModelFileIdentity(
    string CanonicalPath,
    string ContentSha256)
{
    public static OnnxModelFileIdentity Capture(string modelPath)
    {
        var canonicalPath = Canonicalize(modelPath);
        using var stream = new FileStream(
            canonicalPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 128 * 1024,
            options: FileOptions.SequentialScan);
        return new OnnxModelFileIdentity(canonicalPath, Hash(stream));
    }

    public static OnnxModelFileSnapshot CaptureSnapshot(string modelPath)
    {
        var canonicalPath = Canonicalize(modelPath);
        var content = File.ReadAllBytes(canonicalPath);
        var sha256 = Convert.ToHexString(SHA256.HashData(content));
        return new OnnxModelFileSnapshot(
            new OnnxModelFileIdentity(canonicalPath, sha256),
            content);
    }

    private static string Canonicalize(string modelPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelPath);
        return new FileInfo(Path.GetFullPath(modelPath)).FullName;
    }

    private static string Hash(Stream stream) =>
        Convert.ToHexString(SHA256.HashData(stream));
}

internal sealed record OnnxModelFileSnapshot(
    OnnxModelFileIdentity Identity,
    byte[] Content);
