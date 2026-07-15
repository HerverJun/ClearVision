using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ClearVision.Product.Infrastructure.AI.Anomaly;

public sealed record AnomalyEmbeddingPreprocessSpec(
    string ResizeMode,
    string Interpolation,
    string ColorOrder,
    double Scale,
    IReadOnlyList<double> Mean,
    IReadOnlyList<double> Std,
    string TensorLayout,
    string InputDataType,
    string OutputNormalization);

internal sealed record AnomalyEmbeddingManifestIdentity(
    string ManifestPath,
    string ModelSha256,
    string PreprocessFingerprint,
    AnomalyEmbeddingPreprocessSpec Preprocess);

internal static class AnomalyEmbeddingManifest
{
    public static AnomalyEmbeddingManifestIdentity LoadAndValidate(string manifestPath, string modelPath)
    {
        if (string.IsNullOrWhiteSpace(manifestPath))
        {
            throw new InvalidOperationException("EmbeddingManifestPath is required for ONNX anomaly preprocessing.");
        }
        if (string.IsNullOrWhiteSpace(modelPath) || !File.Exists(modelPath))
        {
            throw new FileNotFoundException("Embedding ONNX model was not found.", modelPath);
        }

        var resolvedManifestPath = Path.GetFullPath(manifestPath);
        if (!File.Exists(resolvedManifestPath))
        {
            throw new FileNotFoundException("Embedding manifest was not found.", resolvedManifestPath);
        }

        using var document = JsonDocument.Parse(File.ReadAllBytes(resolvedManifestPath));
        var root = document.RootElement;
        var declaredModelSha = root.GetProperty("modelSha256").GetString()?.Trim().ToLowerInvariant() ?? string.Empty;
        if (declaredModelSha.Length != 64 || declaredModelSha.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new InvalidOperationException("Embedding manifest modelSha256 must be a 64-character SHA-256 hex string.");
        }

        var actualModelSha = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.GetFullPath(modelPath)))).ToLowerInvariant();
        if (!string.Equals(actualModelSha, declaredModelSha, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Embedding model SHA mismatch. Manifest={declaredModelSha}, actual={actualModelSha}.");
        }

        var preprocessElement = root.GetProperty("preprocess");
        var preprocess = ParsePreprocess(preprocessElement);
        Validate(preprocess);
        var canonical = JsonSerializer.Serialize(new
        {
            resizeMode = preprocess.ResizeMode,
            interpolation = preprocess.Interpolation,
            colorOrder = preprocess.ColorOrder,
            scale = preprocess.Scale,
            mean = preprocess.Mean,
            std = preprocess.Std,
            tensorLayout = preprocess.TensorLayout,
            inputDataType = preprocess.InputDataType,
            outputNormalization = preprocess.OutputNormalization
        });
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
        return new AnomalyEmbeddingManifestIdentity(resolvedManifestPath, actualModelSha, fingerprint, preprocess);
    }

    private static AnomalyEmbeddingPreprocessSpec ParsePreprocess(JsonElement element)
    {
        return new AnomalyEmbeddingPreprocessSpec(
            element.GetProperty("resizeMode").GetString() ?? string.Empty,
            element.GetProperty("interpolation").GetString() ?? string.Empty,
            element.GetProperty("colorOrder").GetString() ?? string.Empty,
            element.GetProperty("scale").GetDouble(),
            element.GetProperty("mean").EnumerateArray().Select(item => item.GetDouble()).ToArray(),
            element.GetProperty("std").EnumerateArray().Select(item => item.GetDouble()).ToArray(),
            element.GetProperty("tensorLayout").GetString() ?? string.Empty,
            element.GetProperty("inputDataType").GetString() ?? string.Empty,
            element.GetProperty("outputNormalization").GetString() ?? string.Empty);
    }

    private static void Validate(AnomalyEmbeddingPreprocessSpec preprocess)
    {
        if (!preprocess.ResizeMode.Equals("stretch", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("ONNX anomaly preprocessing currently supports resizeMode='stretch' only.");
        }
        if (preprocess.Interpolation.ToLowerInvariant() is not ("linear" or "area" or "cubic" or "nearest"))
        {
            throw new InvalidOperationException("ONNX anomaly preprocessing interpolation must be linear, area, cubic or nearest.");
        }
        if (preprocess.ColorOrder.ToUpperInvariant() is not ("RGB" or "BGR"))
        {
            throw new InvalidOperationException("ONNX anomaly preprocessing colorOrder must be RGB or BGR.");
        }
        if (!double.IsFinite(preprocess.Scale) || preprocess.Scale <= 0 || preprocess.Mean.Count != 3 || preprocess.Std.Count != 3 || preprocess.Mean.Any(value => !double.IsFinite(value)) || preprocess.Std.Any(value => !double.IsFinite(value) || value <= 0))
        {
            throw new InvalidOperationException("ONNX anomaly preprocessing scale/mean/std must be finite, and std must be positive for all three channels.");
        }
        if (!preprocess.TensorLayout.Equals("NCHW", StringComparison.OrdinalIgnoreCase) || !preprocess.InputDataType.Equals("float32", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("ONNX anomaly preprocessing currently requires NCHW float32 tensors.");
        }
        if (!preprocess.OutputNormalization.Equals("l2", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("ONNX anomaly embedding outputNormalization must be l2.");
        }
    }
}
