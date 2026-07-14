using System.Collections.Concurrent;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Services;
using OpenCvSharp;

namespace ClearVision.Product.Infrastructure.Operators;

internal static class ImageInputRuntimeContractEvaluator
{
    private static readonly ConcurrentDictionary<Type, IReadOnlyList<ImageInputContract>> ContractCache = new();

    public static bool TryValidate(
        Type executorType,
        OperatorType operatorType,
        Operator @operator,
        Dictionary<string, object>? inputs,
        out string error)
    {
        error = string.Empty;
        if (inputs is null)
        {
            return true;
        }

        var contracts = ContractCache.GetOrAdd(
            executorType,
            type => OperatorImageContractResolver.Resolve(type, operatorType));
        foreach (var contract in contracts)
        {
            if (!inputs.TryGetValue(contract.InputPort, out var raw) || raw is null)
            {
                continue;
            }

            if (!ImageWrapper.TryGetFromObject(raw, out var wrapper, out var ownsCreatedWrapper) || wrapper is null)
            {
                continue;
            }

            if (ownsCreatedWrapper)
            {
                inputs[contract.InputPort] = wrapper;
            }

            var mat = wrapper.GetMat();
            if (mat.Empty())
            {
                continue;
            }

            var mode = ResolveModeSummary(@operator);
            if (contract.Status == ImageContractStatus.Unknown)
            {
                error = FormatFailure(
                    "IMAGE_CONTRACT_UNKNOWN",
                    operatorType,
                    contract,
                    mat,
                    mode,
                    "Unknown is not support.");
                return false;
            }

            var depth = ToDepthName(mat.Depth());
            if (!contract.SupportedDepths.Contains(depth, StringComparer.Ordinal))
            {
                error = FormatFailure(
                    contract.FailureCode,
                    operatorType,
                    contract,
                    mat,
                    mode,
                    $"Depth={depth} is not supported.");
                return false;
            }

            var channels = mat.Channels();
            if (!contract.SupportedChannels.Contains(channels))
            {
                error = FormatFailure(
                    "IMAGE_CHANNELS_UNSUPPORTED",
                    operatorType,
                    contract,
                    mat,
                    mode,
                    $"Channels={channels} is not supported.");
                return false;
            }

            if (contract.NonFinitePolicy.StartsWith("Reject", StringComparison.OrdinalIgnoreCase) &&
                mat.Depth() is var floatingDepth &&
                (floatingDepth == MatType.CV_32F || floatingDepth == MatType.CV_64F) &&
                !Cv2.CheckRange(mat, quiet: true))
            {
                error = FormatFailure(
                    "IMAGE_NONFINITE_INPUT",
                    operatorType,
                    contract,
                    mat,
                    mode,
                    "Input contains NaN or Infinity.");
                return false;
            }
        }

        return true;
    }

    public static string ToDepthName(MatType depth)
    {
        if (depth == MatType.CV_8U) return "CV_8U";
        if (depth == MatType.CV_8S) return "CV_8S";
        if (depth == MatType.CV_16U) return "CV_16U";
        if (depth == MatType.CV_16S) return "CV_16S";
        if (depth == MatType.CV_32S) return "CV_32S";
        if (depth == MatType.CV_32F) return "CV_32F";
        if (depth == MatType.CV_64F) return "CV_64F";
        return depth.ToString();
    }

    public static string FormatFailure(
        string code,
        OperatorType operatorType,
        ImageInputContract contract,
        Mat mat,
        string mode,
        string diagnostic)
    {
        var supported = string.Join(
            ",",
            contract.SupportedDepths.SelectMany(depth =>
                contract.SupportedChannels.Select(channels => $"{depth}C{channels}")));
        return $"{code}: OperatorType={operatorType}; InputPort={contract.InputPort}; " +
               $"InputMatType={mat.Type()}; Mode={mode}; Supported={supported}; Diagnostic={diagnostic}";
    }

    private static string ResolveModeSummary(Operator @operator)
    {
        var modeParameters = new[] { "FilterMode", "Method", "Type", "ThresholdMode", "OutputImagePolicy" };
        var values = @operator.Parameters
            .Where(parameter => modeParameters.Contains(parameter.Name, StringComparer.OrdinalIgnoreCase))
            .Select(parameter => $"{parameter.Name}={parameter.Value}")
            .ToArray();
        return values.Length == 0 ? "Default" : string.Join(",", values);
    }
}
