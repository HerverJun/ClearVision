using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;

namespace ClearVision.Product.Core.Services;

public static class ImageAcquisitionFlowAnalyzer
{
    public static bool ShouldBypassExternalCameraInput(OperatorFlow? flow)
    {
        if (flow?.Operators == null)
        {
            return false;
        }

        var hasExplicitFileSource = false;
        foreach (var op in flow.Operators.Where(item => item.Type == OperatorType.ImageAcquisition))
        {
            if (IsCameraSource(op))
            {
                return false;
            }

            var sourceType = NormalizeSourceType(GetParameterString(op, "SourceType", "sourceType"));
            var filePath = GetParameterString(op, "FilePath", "filePath");
            if ((sourceType.Equals("File", StringComparison.OrdinalIgnoreCase) ||
                 string.IsNullOrWhiteSpace(sourceType)) &&
                !string.IsNullOrWhiteSpace(filePath))
            {
                hasExplicitFileSource = true;
            }
        }

        return hasExplicitFileSource;
    }

    public static bool TryResolveCameraId(OperatorFlow? flow, string? externalCameraId, out string resolvedCameraId)
    {
        resolvedCameraId = string.Empty;
        if (!string.IsNullOrWhiteSpace(externalCameraId) &&
            !ShouldBypassExternalCameraInput(flow))
        {
            resolvedCameraId = externalCameraId.Trim();
            return true;
        }

        if (flow?.Operators == null)
        {
            return false;
        }

        foreach (var op in flow.Operators.Where(item => item.Type == OperatorType.ImageAcquisition))
        {
            if (!IsCameraSource(op))
            {
                continue;
            }

            var bindingId = GetParameterString(op, "CameraId", "cameraId");
            if (!string.IsNullOrWhiteSpace(bindingId))
            {
                resolvedCameraId = bindingId.Trim();
                return true;
            }
        }

        return false;
    }

    public static bool IsCameraSource(Operator op)
    {
        var sourceType = NormalizeSourceType(GetParameterString(op, "SourceType", "sourceType"));
        var bindingId = GetParameterString(op, "CameraId", "cameraId");
        return sourceType.Equals("Camera", StringComparison.OrdinalIgnoreCase) ||
            (string.IsNullOrWhiteSpace(sourceType) && !string.IsNullOrWhiteSpace(bindingId));
    }

    public static string NormalizeSourceType(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = NormalizeOptionValue(value);
        return normalized switch
        {
            "文件" => "File",
            "相机" => "Camera",
            _ => normalized
        };
    }

    public static string NormalizeOptionValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Trim();
        var separatorIndex = normalized.IndexOf('|', StringComparison.Ordinal);
        return separatorIndex >= 0
            ? normalized[..separatorIndex].Trim()
            : normalized;
    }

    public static string? GetParameterString(Operator op, params string[] names)
    {
        foreach (var name in names)
        {
            var value = op.Parameters
                .FirstOrDefault(parameter => parameter.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                ?.GetValue()
                ?.ToString();
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }
}
