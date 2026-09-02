namespace ClearVision.Product.Infrastructure.AI.Agent;

internal enum VisionAgentImageSourceKind
{
    Pending,
    File,
    Camera,
    Unsupported
}

internal sealed record VisionAgentImageSourceResolution(
    VisionAgentImageSourceKind Kind,
    string SourceType,
    string DiagnosticCode,
    string OriginalValue)
{
    public bool Supported => Kind is VisionAgentImageSourceKind.File or VisionAgentImageSourceKind.Camera;
}

/// <summary>
/// Single fail-closed alias table for image-source answers used by readiness and Build mapping.
/// The values describe the source capability only; concrete file/camera resources remain separate.
/// </summary>
internal static class VisionAgentImageSourceResolver
{
    private static readonly HashSet<string> FileAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        "file",
        "file_sample",
        "image_file",
        "image_folder",
        "offline_sample",
        "sample_image",
        "文件",
        "图像文件",
        "图片目录",
        "文件夹",
        "离线样张"
    };

    private static readonly HashSet<string> CameraAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        "camera",
        "station_camera",
        "line_camera",
        "industrial_camera",
        "工站相机",
        "产线相机",
        "工业相机",
        "相机"
    };

    private static readonly HashSet<string> PendingAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        "camera_pending",
        "image_source_pending",
        "source_pending",
        "pending",
        "placeholder"
    };

    private static readonly HashSet<string> UnsupportedAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        "video",
        "video_stream",
        "stream",
        "rtsp",
        "视频",
        "视频流",
        "unknown"
    };

    public static VisionAgentImageSourceResolution Resolve(string? value)
    {
        var original = (value ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(original))
        {
            return new(
                VisionAgentImageSourceKind.Pending,
                "<pending-image-source>",
                "image_source_pending",
                original);
        }

        var normalized = Normalize(original);
        if (FileAliases.Contains(normalized))
        {
            return new(VisionAgentImageSourceKind.File, "File", string.Empty, original);
        }

        if (CameraAliases.Contains(normalized))
        {
            return new(VisionAgentImageSourceKind.Camera, "Camera", string.Empty, original);
        }

        if (PendingAliases.Contains(normalized))
        {
            return new(
                VisionAgentImageSourceKind.Pending,
                "<pending-image-source>",
                "image_source_pending",
                original);
        }

        if (UnsupportedAliases.Contains(normalized))
        {
            return new(
                VisionAgentImageSourceKind.Unsupported,
                "<unsupported-image-source>",
                "unsupported_image_source",
                original);
        }

        return new(
            VisionAgentImageSourceKind.Unsupported,
            "<unsupported-image-source>",
            "unsupported_image_source",
            original);
    }

    private static string Normalize(string value)
    {
        var separatorIndex = value.IndexOf('|', StringComparison.Ordinal);
        var token = separatorIndex >= 0 ? value[..separatorIndex] : value;
        return token.Trim()
            .Replace('-', '_')
            .Replace(' ', '_')
            .ToLowerInvariant();
    }
}
