using System.Collections.Concurrent;
using System.Globalization;
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

            if (!TryResolveMode(operatorType, @operator, mat.Channels(), out var mode, out var modeError))
            {
                error = FormatModeResolutionFailure(operatorType, contract, mat, modeError);
                return false;
            }

            if (!TryValidateResolvedMode(operatorType, contract, mat, mode, out error))
            {
                return false;
            }
        }

        return true;
    }

    internal static bool TryValidateResolvedMode(
        OperatorType operatorType,
        ImageInputContract contract,
        Mat mat,
        string mode,
        out string error)
    {
        error = string.Empty;
        var modeVariants = contract.Variants
            .Where(variant => variant.Mode.Equals(mode, StringComparison.Ordinal))
            .ToArray();
        if (modeVariants.Length == 0)
        {
            error = FormatFailure(
                "IMAGE_MODE_UNRESOLVED",
                operatorType,
                contract,
                mat,
                mode,
                $"No authoritative variant exists for resolved mode '{mode}'.");
            return false;
        }

        var depth = ToDepthName(mat.Depth());
        var exact = modeVariants
            .Where(variant => variant.Depth.Equals(depth, StringComparison.Ordinal) &&
                              variant.Channels == mat.Channels())
            .ToArray();
        if (exact.Length != 1)
        {
            var code = exact.Length > 1 ? "IMAGE_CONTRACT_AMBIGUOUS" : "IMAGE_COMBINATION_UNDECLARED";
            error = FormatFailure(
                code,
                operatorType,
                contract,
                mat,
                mode,
                exact.Length > 1
                    ? "More than one authoritative exact variant matched."
                    : $"No exact variant declares Depth={depth}, Channels={mat.Channels()} for this mode.");
            return false;
        }

        var variant = exact[0];
        if (variant.Admission != ImageContractAdmission.Allowed)
        {
            error = FormatFailure(
                variant.FailureCode,
                operatorType,
                contract,
                mat,
                mode,
                variant.Condition,
                variant);
            return false;
        }

        if (!TryValidateInputValues(mat, variant, out var valueDiagnostic))
        {
            error = FormatFailure(
                variant.FailureCode,
                operatorType,
                contract,
                mat,
                mode,
                valueDiagnostic,
                variant);
            return false;
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
        string diagnostic) =>
        FormatFailure(code, operatorType, contract, mat, mode, diagnostic, matchedVariant: null);

    private static string FormatFailure(
        string code,
        OperatorType operatorType,
        ImageInputContract contract,
        Mat mat,
        string mode,
        string diagnostic,
        ImageContractVariant? matchedVariant)
    {
        var supported = contract.Variants
            .Where(variant => variant.Mode.Equals(mode, StringComparison.Ordinal) &&
                              variant.Admission == ImageContractAdmission.Allowed)
            .Select(variant => $"{variant.Depth}C{variant.Channels}")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        var evidence = matchedVariant is null
            ? string.Empty
            : $"; Admission={matchedVariant.Admission}; Verification={matchedVariant.Verification}; Evidence={matchedVariant.EvidenceLevel}";
        return $"{code}: OperatorType={operatorType}; InputPort={contract.InputPort}; " +
               $"InputMatType={mat.Type()}; Mode={mode}; Supported={string.Join(',', supported)}{evidence}; Diagnostic={diagnostic}";
    }

    internal static bool TryResolveMode(
        OperatorType operatorType,
        Operator @operator,
        int inputChannels,
        out string mode,
        out string error)
    {
        mode = "Default";
        error = string.Empty;
        switch (operatorType)
        {
            case OperatorType.Thresholding:
                return TryResolveThresholdType(@operator, out _, out mode, out error);
            case OperatorType.Filtering:
            case OperatorType.MeanFilter:
            case OperatorType.MedianBlur:
            case OperatorType.BilateralFilter:
                return TryResolveSpatialMode(operatorType, @operator, out mode, out error);
            case OperatorType.HistogramAnalysis:
                return TryResolveHistogramMode(@operator, out mode, out error);
            case OperatorType.SharpnessEvaluation:
                return TryResolveSharpnessMode(@operator, out mode, out error);
            case OperatorType.ImageNormalize:
                return TryResolveImageNormalizeMode(@operator, inputChannels, out mode, out error);
            default:
                return true;
        }
    }

    internal static bool TryResolveThresholdType(
        Operator @operator,
        out ThresholdTypes thresholdType,
        out string mode,
        out string error)
    {
        if (!TryGetInt(@operator, "Type", 0, out var typeValue) ||
            !TryGetBool(@operator, "UseOtsu", false, out var useOtsu))
        {
            thresholdType = ThresholdTypes.Binary;
            mode = string.Empty;
            error = "Type or UseOtsu could not be parsed.";
            return false;
        }

        return TryResolveThresholdType(typeValue, useOtsu, out thresholdType, out mode, out error);
    }

    internal static bool TryResolveThresholdType(
        int typeValue,
        bool useOtsu,
        out ThresholdTypes thresholdType,
        out string mode,
        out string error)
    {
        const int automaticMask = (int)(ThresholdTypes.Otsu | ThresholdTypes.Triangle);
        thresholdType = ThresholdTypes.Binary;
        mode = "Fixed";
        error = string.Empty;

        var explicitAutomatic = typeValue & automaticMask;
        if (explicitAutomatic == automaticMask)
        {
            error = "Threshold type cannot combine Otsu and Triangle.";
            return false;
        }
        if (useOtsu && explicitAutomatic == (int)ThresholdTypes.Triangle)
        {
            error = "UseOtsu cannot be combined with Triangle threshold type.";
            return false;
        }

        var baseType = typeValue & ~automaticMask;
        if (baseType is not 0
            and not (int)ThresholdTypes.BinaryInv
            and not (int)ThresholdTypes.Trunc
            and not (int)ThresholdTypes.Tozero
            and not (int)ThresholdTypes.TozeroInv)
        {
            error = $"Unsupported threshold type value: {typeValue}.";
            return false;
        }

        var automaticType = explicitAutomatic;
        if (useOtsu)
        {
            automaticType |= (int)ThresholdTypes.Otsu;
        }
        if (automaticType == automaticMask)
        {
            error = "Threshold type cannot combine Otsu and Triangle.";
            return false;
        }
        if (automaticType != 0 &&
            baseType is not (int)ThresholdTypes.Binary and not (int)ThresholdTypes.BinaryInv)
        {
            error = "Otsu and Triangle require Binary or BinaryInv as the base threshold type.";
            return false;
        }

        thresholdType = (ThresholdTypes)(baseType | automaticType);
        mode = (thresholdType & ThresholdTypes.Otsu) == ThresholdTypes.Otsu
            ? "Otsu"
            : (thresholdType & ThresholdTypes.Triangle) == ThresholdTypes.Triangle
                ? "Triangle"
                : "Fixed";
        return true;
    }

    private static bool TryResolveSpatialMode(
        OperatorType operatorType,
        Operator @operator,
        out string mode,
        out string error)
    {
        SpatialFilterMode filterMode;
        if (operatorType == OperatorType.Filtering)
        {
            if (!SpatialFilterKernel.TryParseMode(GetString(@operator, "FilterMode", "Gaussian"), out filterMode))
            {
                mode = string.Empty;
                error = "FilterMode must be Gaussian, Mean, Box, Median or Bilateral.";
                return false;
            }
        }
        else
        {
            filterMode = operatorType switch
            {
                OperatorType.MeanFilter => SpatialFilterMode.Mean,
                OperatorType.MedianBlur => SpatialFilterMode.Median,
                OperatorType.BilateralFilter => SpatialFilterMode.Bilateral,
                _ => SpatialFilterMode.Gaussian
            };
        }

        if (!TryGetInt(@operator, "KernelSize", 5, out var kernelSize) ||
            !TryGetDouble(@operator, "SigmaX", 1.0, out var sigmaX) ||
            !TryGetDouble(@operator, "SigmaY", 0.0, out var sigmaY) ||
            !TryGetInt(@operator, "BorderType", 4, out var borderType) ||
            !TryGetInt(@operator, "Diameter", 9, out var diameter) ||
            !TryGetDouble(@operator, "SigmaColor", 75.0, out var sigmaColor) ||
            !TryGetDouble(@operator, "SigmaSpace", 75.0, out var sigmaSpace))
        {
            mode = filterMode.ToString();
            error = "One or more spatial-filter parameters could not be parsed.";
            return false;
        }

        var settings = new SpatialFilterSettings(
            filterMode,
            KernelSize: kernelSize,
            SigmaX: sigmaX,
            SigmaY: sigmaY,
            BorderType: borderType,
            Diameter: diameter,
            SigmaColor: sigmaColor,
            SigmaSpace: sigmaSpace);
        if (!SpatialFilterKernel.TryValidate(settings, out error))
        {
            mode = filterMode.ToString();
            return false;
        }

        mode = SpatialFilterKernel.ResolveContractMode(settings);
        return true;
    }

    private static bool TryResolveHistogramMode(Operator @operator, out string mode, out string error)
    {
        var channel = GetString(@operator, "Channel", "Gray");
        var canonical = new[] { "Gray", "B", "G", "R" }
            .SingleOrDefault(value => value.Equals(channel, StringComparison.OrdinalIgnoreCase));
        if (canonical is null)
        {
            mode = string.Empty;
            error = "Channel must be Gray, B, G or R.";
            return false;
        }

        mode = $"Channel={canonical}";
        error = string.Empty;
        return true;
    }

    private static bool TryResolveSharpnessMode(Operator @operator, out string mode, out string error)
    {
        var method = Canonical(GetString(@operator, "Method", "Laplacian"), "Laplacian", "Brenner", "Tenengrad", "SMD");
        var thresholdMode = Canonical(GetString(@operator, "ThresholdMode", "PerMethodDefault"), "PerMethodDefault", "Manual");
        var outputPolicy = Canonical(GetString(@operator, "OutputImagePolicy", "FullOverlay"), "FullOverlay", "Passthrough", "None");
        if (method is null || thresholdMode is null || outputPolicy is null)
        {
            mode = string.Empty;
            error = "Method, ThresholdMode, or OutputImagePolicy is invalid.";
            return false;
        }

        mode = $"{method}:{thresholdMode}:{outputPolicy}";
        error = string.Empty;
        return true;
    }

    private static bool TryResolveImageNormalizeMode(
        Operator @operator,
        int inputChannels,
        out string mode,
        out string error)
    {
        var method = Canonical(GetString(@operator, "Method", "MinMax"), "MinMax", "ZScore", "Histogram");
        var colorMode = Canonical(GetString(@operator, "ColorMode", "LumaOnly"), "LumaOnly", "PerChannel");
        if (method is null || colorMode is null)
        {
            mode = string.Empty;
            error = "Method or ColorMode is invalid.";
            return false;
        }

        mode = inputChannels == 1 ? $"{method}:Gray" : $"{method}:{colorMode}";
        error = string.Empty;
        return true;
    }

    private static bool TryValidateInputValues(
        Mat mat,
        ImageContractVariant variant,
        out string diagnostic)
    {
        diagnostic = string.Empty;
        if (variant.InputValuePolicy == ImageContractInputValuePolicy.Any)
        {
            return true;
        }

        if (!Cv2.CheckRange(mat, quiet: true))
        {
            diagnostic = "Input contains NaN or Infinity.";
            return false;
        }

        if (variant.InputValuePolicy == ImageContractInputValuePolicy.RequireFiniteFloat32Representable &&
            !IsFloat32Representable(mat))
        {
            diagnostic = "Finite CV_64F input contains values outside the representable CV_32F range.";
            return false;
        }

        return true;
    }

    private static bool IsFloat32Representable(Mat mat)
    {
        if (mat.Depth() != MatType.CV_64F)
        {
            return true;
        }

        Cv2.Split(mat, out var channels);
        try
        {
            foreach (var channel in channels)
            {
                double min;
                double max;
                Cv2.MinMaxLoc(channel, out min, out max);
                if (min < -float.MaxValue || max > float.MaxValue)
                {
                    return false;
                }
            }

            return true;
        }
        finally
        {
            foreach (var channel in channels)
            {
                channel.Dispose();
            }
        }
    }

    private static string FormatModeResolutionFailure(
        OperatorType operatorType,
        ImageInputContract contract,
        Mat mat,
        string diagnostic)
    {
        var modes = contract.Variants.Select(variant => variant.Mode)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal);
        return $"IMAGE_MODE_UNRESOLVED: OperatorType={operatorType}; InputPort={contract.InputPort}; " +
               $"InputMatType={mat.Type()}; SupportedModes={string.Join(',', modes)}; Diagnostic={diagnostic}";
    }

    private static string? Canonical(string value, params string[] candidates) =>
        candidates.SingleOrDefault(candidate => candidate.Equals(value, StringComparison.OrdinalIgnoreCase));

    private static object? GetParameterValue(Operator @operator, string name) =>
        @operator.Parameters
            .FirstOrDefault(parameter => parameter.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?
            .Value;

    private static string GetString(Operator @operator, string name, string defaultValue)
    {
        var value = GetParameterValue(@operator, name);
        return value is null
            ? defaultValue
            : Convert.ToString(value, CultureInfo.InvariantCulture) ?? defaultValue;
    }

    private static bool TryGetInt(Operator @operator, string name, int defaultValue, out int result)
    {
        var value = GetParameterValue(@operator, name);
        if (value is null)
        {
            result = defaultValue;
            return true;
        }
        if (value is int integer)
        {
            result = integer;
            return true;
        }
        return int.TryParse(
            Convert.ToString(value, CultureInfo.InvariantCulture),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out result);
    }

    private static bool TryGetDouble(Operator @operator, string name, double defaultValue, out double result)
    {
        var value = GetParameterValue(@operator, name);
        if (value is null)
        {
            result = defaultValue;
            return true;
        }
        if (value is double number)
        {
            result = number;
            return true;
        }
        return double.TryParse(
            Convert.ToString(value, CultureInfo.InvariantCulture),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out result);
    }

    private static bool TryGetBool(Operator @operator, string name, bool defaultValue, out bool result)
    {
        var value = GetParameterValue(@operator, name);
        if (value is null)
        {
            result = defaultValue;
            return true;
        }
        if (value is bool boolean)
        {
            result = boolean;
            return true;
        }
        return bool.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out result);
    }
}
