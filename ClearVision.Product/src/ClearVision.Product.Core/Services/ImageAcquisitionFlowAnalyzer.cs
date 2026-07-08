using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;

namespace ClearVision.Product.Core.Services;

public static class ImageAcquisitionFlowAnalyzer
{
    public static bool ShouldPassExternalInputImageToPreview(OperatorFlow? flow, Guid targetNodeId)
    {
        if (flow?.Operators == null || flow.Operators.Count == 0)
        {
            return true;
        }

        var relevantIds = CollectRelevantOperatorIds(flow, targetNodeId);
        var relevantAcquisitionNodes = flow.Operators
            .Where(item => relevantIds.Contains(item.Id) && item.Type == OperatorType.ImageAcquisition)
            .ToList();
        if (relevantAcquisitionNodes.Count == 0)
        {
            return true;
        }

        if (relevantAcquisitionNodes.Any(IsCameraSource))
        {
            return false;
        }

        return relevantAcquisitionNodes.Any(op => ConsumesRuntimeImageWhenFileSource(flow, op));
    }

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

    public static bool ConsumesRuntimeImageWhenFileSource(OperatorFlow? flow, Operator op)
    {
        var sourceType = NormalizeSourceType(GetParameterString(op, "SourceType", "sourceType"));
        var isFileSource = sourceType.Equals("File", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(sourceType);
        if (!isFileSource)
        {
            return false;
        }

        var filePath = GetParameterString(op, "FilePath", "filePath");
        return string.IsNullOrWhiteSpace(filePath) && !HasIncomingFilePathInput(flow, op);
    }

    public static string NormalizeSourceType(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = NormalizeOptionValue(value);
        var token = normalized.ToLowerInvariant();
        if (token == "camera" ||
            token.Contains("cam", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("相机", StringComparison.Ordinal) ||
            normalized.Contains("摄像", StringComparison.Ordinal))
        {
            return "Camera";
        }

        if (token == "file" ||
            token.Contains("image", StringComparison.OrdinalIgnoreCase) ||
            token.Contains("path", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("文件", StringComparison.Ordinal) ||
            normalized.Contains("图像", StringComparison.Ordinal) ||
            normalized.Contains("图片", StringComparison.Ordinal) ||
            normalized.Contains("路径", StringComparison.Ordinal))
        {
            return "File";
        }

        return normalized;
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

    private static HashSet<Guid> CollectRelevantOperatorIds(OperatorFlow flow, Guid targetNodeId)
    {
        var visited = new HashSet<Guid>();
        var stack = new Stack<Guid>();
        stack.Push(targetNodeId);

        while (stack.Count > 0)
        {
            var currentId = stack.Pop();
            if (!visited.Add(currentId))
            {
                continue;
            }

            foreach (var connection in flow.Connections.Where(item => item.TargetOperatorId == currentId))
            {
                stack.Push(connection.SourceOperatorId);
            }
        }

        return visited;
    }

    private static bool HasIncomingFilePathInput(OperatorFlow? flow, Operator op)
    {
        if (flow?.Connections == null)
        {
            return false;
        }

        var filePathPortIds = op.InputPorts
            .Where(port => port.Name.Equals("FilePath", StringComparison.OrdinalIgnoreCase))
            .Select(port => port.Id)
            .ToHashSet();
        return filePathPortIds.Count > 0 &&
            flow.Connections.Any(connection =>
                connection.TargetOperatorId == op.Id &&
                filePathPortIds.Contains(connection.TargetPortId));
    }
}
