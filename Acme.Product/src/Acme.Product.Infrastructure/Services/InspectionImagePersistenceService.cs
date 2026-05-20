using Acme.Product.Application.Services;
using Acme.Product.Core.Entities;
using Acme.Product.Core.Enums;
using Acme.Product.Core.Interfaces;
using Microsoft.Extensions.Logging;
using OpenCvSharp;

namespace Acme.Product.Infrastructure.Services;

public sealed class InspectionImagePersistenceService : IInspectionImagePersistenceService
{
    private const int JpegQuality = 85;

    private readonly IConfigurationService _configurationService;
    private readonly ILogger<InspectionImagePersistenceService> _logger;

    public InspectionImagePersistenceService(
        IConfigurationService configurationService,
        ILogger<InspectionImagePersistenceService> logger)
    {
        _configurationService = configurationService;
        _logger = logger;
    }

    public async Task PersistAsync(InspectionResult result, CancellationToken cancellationToken = default)
    {
        if (result.OutputImage == null || result.OutputImage.Length == 0)
        {
            return;
        }

        var config = _configurationService.GetCurrent();
        var storage = config.Storage ?? new StorageConfig();
        if (!ShouldPersistImage(storage.SavePolicy, result.Status))
        {
            return;
        }

        try
        {
            var rootPath = ResolveImageSaveRoot(storage.ImageSavePath);
            var dateFolder = DateTime.Now.ToString("yyyyMMdd");
            var statusFolder = result.Status switch
            {
                InspectionStatus.OK => "OK",
                InspectionStatus.NG => "NG",
                _ => "ERROR"
            };

            var targetDir = Path.Combine(rootPath, dateFolder, statusFolder);
            Directory.CreateDirectory(targetDir);

            var persistedImage = EncodeForPersistence(result.OutputImage);
            var fileName = $"{result.ProjectId:N}_{result.Id:N}_{DateTime.Now:HHmmssfff}{persistedImage.Extension}";
            var targetPath = Path.Combine(targetDir, fileName);

            await File.WriteAllBytesAsync(targetPath, persistedImage.Bytes, cancellationToken);
            _logger.LogDebug("[InspectionImagePersistence] 检测图像已落盘: {Path}", targetPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[InspectionImagePersistence] 检测图像落盘失败");
        }
    }

    private static bool ShouldPersistImage(string? savePolicy, InspectionStatus status)
    {
        var policy = (savePolicy ?? "NgOnly").Trim();
        if (policy.Equals("None", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (policy.Equals("All", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (policy.Equals("NgOnly", StringComparison.OrdinalIgnoreCase))
        {
            return status == InspectionStatus.NG;
        }

        return status == InspectionStatus.NG;
    }

    private static string ResolveImageSaveRoot(string? configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            try
            {
                var configuredRoot = Path.GetFullPath(configuredPath);
                if (CanWriteToDirectory(configuredRoot))
                {
                    return configuredRoot;
                }
            }
            catch
            {
            }
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ClearVision",
            "Images");
    }

    private static bool CanWriteToDirectory(string directory)
    {
        try
        {
            var pathRoot = Path.GetPathRoot(directory);
            if (!string.IsNullOrWhiteSpace(pathRoot) && !Directory.Exists(pathRoot))
            {
                return false;
            }

            Directory.CreateDirectory(directory);
            var probePath = Path.Combine(directory, $".cv-write-test-{Guid.NewGuid():N}.tmp");
            using (File.Create(probePath, 1, FileOptions.DeleteOnClose))
            {
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static PersistedImage EncodeForPersistence(byte[] imageBytes)
    {
        try
        {
            using var decoded = Cv2.ImDecode(imageBytes, ImreadModes.Unchanged);
            if (decoded.Empty())
            {
                return new PersistedImage(imageBytes, GuessImageExtension(imageBytes));
            }

            using var jpegSource = CreateJpegCompatibleMat(decoded);
            Cv2.ImEncode(
                ".jpg",
                jpegSource,
                out var compressedBytes,
                new[] { new ImageEncodingParam(ImwriteFlags.JpegQuality, JpegQuality) });

            if (compressedBytes.Length > 0)
            {
                return new PersistedImage(compressedBytes, ".jpg");
            }
        }
        catch
        {
            // Fall back to the operator output bytes if OpenCV cannot decode a custom image payload.
        }

        return new PersistedImage(imageBytes, GuessImageExtension(imageBytes));
    }

    private static Mat CreateJpegCompatibleMat(Mat source)
    {
        if (source.Channels() == 4)
        {
            var bgr = new Mat();
            Cv2.CvtColor(source, bgr, ColorConversionCodes.BGRA2BGR);
            return bgr;
        }

        if (source.Depth() != MatType.CV_8U)
        {
            var converted = new Mat();
            source.ConvertTo(converted, MatType.MakeType(MatType.CV_8U, source.Channels()));
            return converted;
        }

        return source.Clone();
    }

    private static string GuessImageExtension(byte[] bytes)
    {
        if (bytes.Length >= 8 &&
            bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
        {
            return ".png";
        }

        if (bytes.Length >= 3 &&
            bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
        {
            return ".jpg";
        }

        if (bytes.Length >= 2 && bytes[0] == 0x42 && bytes[1] == 0x4D)
        {
            return ".bmp";
        }

        return ".bin";
    }

    private readonly record struct PersistedImage(byte[] Bytes, string Extension);
}
