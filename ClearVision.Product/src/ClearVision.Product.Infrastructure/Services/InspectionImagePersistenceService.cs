using ClearVision.Product.Application.Services;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Interfaces;
using Microsoft.Extensions.Logging;
using OpenCvSharp;

namespace ClearVision.Product.Infrastructure.Services;

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
        cancellationToken.ThrowIfCancellationRequested();

        if (result.OutputImage == null || result.OutputImage.Length == 0)
        {
            return;
        }

        var config = _configurationService.GetCurrent();
        var storage = config.Storage ?? new StorageConfig();
        if (!InspectionImagePersistencePolicy.ShouldPersistImage(storage.SavePolicy, result.Status))
        {
            return;
        }

        try
        {
            var capturedAt = DateTime.Now;
            var dateFolder = capturedAt.ToString("yyyyMMdd");
            var statusFolder = result.Status switch
            {
                InspectionStatus.OK => "OK",
                InspectionStatus.NG => "NG",
                _ => "ERROR"
            };
            var persistedImage = EncodeForPersistence(result.OutputImage);
            var fileName = $"{result.ProjectId:N}_{result.Id:N}_{capturedAt:HHmmssfff}{persistedImage.Extension}";

            foreach (var rootPath in InspectionImagePersistencePaths.ResolveImageSaveRoots(storage.ImageSavePath))
            {
                var targetDir = Path.Combine(rootPath, dateFolder, statusFolder);
                var targetPath = Path.Combine(targetDir, fileName);
                try
                {
                    Directory.CreateDirectory(targetDir);
                    await File.WriteAllBytesAsync(targetPath, persistedImage.Bytes, cancellationToken);
                    _logger.LogDebug("[InspectionImagePersistence] 检测图像已落盘: {Path}", targetPath);
                    return;
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    _logger.LogWarning(ex, "[InspectionImagePersistence] 检测图像落盘失败，尝试下一个保存目录: {Path}", targetPath);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _logger.LogWarning(ex, "[InspectionImagePersistence] 检测图像落盘失败");
        }
    }

    private static PersistedImage EncodeForPersistence(byte[] imageBytes)
    {
        try
        {
            using var decoded = Cv2.ImDecode(imageBytes, ImreadModes.Unchanged);
            if (decoded.Empty())
            {
                return new PersistedImage(imageBytes, InspectionImageFormatDetector.GuessExtension(imageBytes));
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

        return new PersistedImage(imageBytes, InspectionImageFormatDetector.GuessExtension(imageBytes));
    }

    private static Mat CreateJpegCompatibleMat(Mat source)
    {
        Mat? channelConverted = null;
        var compatibleSource = source;

        if (source.Channels() == 4)
        {
            channelConverted = new Mat();
            Cv2.CvtColor(source, channelConverted, ColorConversionCodes.BGRA2BGR);
            compatibleSource = channelConverted;
        }

        if (compatibleSource.Depth() != MatType.CV_8U)
        {
            var converted = new Mat();
            compatibleSource.ConvertTo(
                converted,
                MatType.MakeType(MatType.CV_8U, compatibleSource.Channels()),
                GetDepthConversionScale(compatibleSource.Depth()));
            channelConverted?.Dispose();
            return converted;
        }

        return channelConverted ?? source.Clone();
    }

    private static double GetDepthConversionScale(int depth)
    {
        return depth == MatType.CV_16U
            ? byte.MaxValue / (double)ushort.MaxValue
            : 1.0;
    }

    private readonly record struct PersistedImage(byte[] Bytes, string Extension);
}
