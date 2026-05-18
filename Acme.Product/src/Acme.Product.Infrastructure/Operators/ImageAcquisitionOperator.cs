// ImageAcquisitionOperator.cs
// 图像采集算子 - 支持相机和文件采集
// 作者：蘅芜君

using Acme.Product.Core.Attributes;
using Acme.Product.Core.Cameras;
using Acme.Product.Core.Entities;
using Acme.Product.Core.Enums;
using Acme.Product.Core.Operators;
using Acme.Product.Core.Streaming;
using Acme.Product.Core.ValueObjects;
using Acme.Product.Infrastructure.Cameras;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
namespace Acme.Product.Infrastructure.Operators;

/// <summary>
/// 图像采集算子 - 支持相机和文件采集
/// </summary>
[OperatorMeta(
    DisplayName = "图像采集",
    Description = "从文件或相机采集图像",
    Category = "采集",
    IconName = "camera",
    Keywords = new[] { "采集", "相机", "拍照", "取图", "摄像头", "图像输入", "Acquire", "Camera", "Capture" }
)]
[InputPort("Image", "Runtime supplied image", PortDataType.Image, IsRequired = false)]
[InputPort("FilePath", "文件路径输入", PortDataType.String, IsRequired = false)]
[OutputPort("Image", "图像", PortDataType.Image)]
[OperatorParam("SourceType", "采集源", "enum", DefaultValue = "File", Options = new[] { "File|文件", "Camera|相机" })]
[OperatorParam("FilePath", "文件路径", "file", DefaultValue = "")]
[OperatorParam("CameraId", "相机", "cameraBinding", DefaultValue = "")]
[OperatorParam("ExposureTime", "曝光时间(us)", "double", DefaultValue = 5000.0, Min = 1.0)]
[OperatorParam("Gain", "增益(dB)", "double", DefaultValue = 1.0, Min = 0.0)]
[OperatorParam("TriggerMode", "触发模式", "enum", DefaultValue = "Software", Options = new[] { "Software|软件触发", "External|外部触发", "Continuous|连续采集" })]
public class ImageAcquisitionOperator : OperatorBase
{
    public override OperatorType OperatorType => OperatorType.ImageAcquisition;
    private readonly ICameraManager _cameraManager;
    private readonly ICameraFrameStreamCoordinator _streamCoordinator;
    private readonly ITriggerInputService _triggerInputService;
    private readonly ISerialPhotoelectricTriggerInputService _serialPhotoelectricTriggerInputService;

    public ImageAcquisitionOperator(ILogger<ImageAcquisitionOperator> logger, ICameraManager cameraManager)
        : this(logger, cameraManager, NoOpCameraFrameStreamCoordinator.Instance, NoOpTriggerInputService.Instance, NoOpSerialPhotoelectricTriggerInputService.Instance)
    {
    }

    public ImageAcquisitionOperator(
        ILogger<ImageAcquisitionOperator> logger,
        ICameraManager cameraManager,
        ICameraFrameStreamCoordinator streamCoordinator)
        : this(logger, cameraManager, streamCoordinator, NoOpTriggerInputService.Instance, NoOpSerialPhotoelectricTriggerInputService.Instance)
    {
    }

    public ImageAcquisitionOperator(
        ILogger<ImageAcquisitionOperator> logger,
        ICameraManager cameraManager,
        ICameraFrameStreamCoordinator streamCoordinator,
        ITriggerInputService triggerInputService,
        ISerialPhotoelectricTriggerInputService serialPhotoelectricTriggerInputService) : base(logger)
    {
        _cameraManager = cameraManager;
        _streamCoordinator = streamCoordinator;
        _triggerInputService = triggerInputService;
        _serialPhotoelectricTriggerInputService = serialPhotoelectricTriggerInputService;
    }

    protected override async Task<OperatorExecutionOutput> ExecuteCoreAsync(
        Operator @operator,
        Dictionary<string, object>? inputs,
        CancellationToken cancellationToken)
    {
        if (TryGetProvidedFrameEnvelope(inputs, out var envelope) && envelope != null)
        {
            return CreateOutputFromEnvelope(envelope);
        }

        // 优先获取 sourceType 和 filePath 参数
        // 1. 尝试从连线输入获取
        // 2. 如果没有连线输入，从算子自身的参数列表中获取 (Metadata-driven)
        var sourceType = TryGetStringInput(inputs, "SourceType")
            ?? TryGetStringInput(inputs, "sourceType")
            ?? GetStringParam(@operator, "SourceType", GetStringParam(@operator, "sourceType", string.Empty));
        var normalizedSourceType = NormalizeSourceType(sourceType);

        var filePath = TryGetStringInput(inputs, "FilePath")
            ?? TryGetStringInput(inputs, "filePath")
            ?? GetStringParam(@operator, "FilePath", GetStringParam(@operator, "filePath", string.Empty));

        var isFileSource = normalizedSourceType.Equals("File", StringComparison.OrdinalIgnoreCase);
        var isCameraSource = normalizedSourceType.Equals("Camera", StringComparison.OrdinalIgnoreCase);
        if (!isFileSource && !isCameraSource)
        {
            return OperatorExecutionOutput.Failure("SourceType must be File or Camera.");
        }

        var hasExplicitFilePath = !string.IsNullOrWhiteSpace(filePath);
        if (TryCreateOutputFromProvidedImage(inputs, out var providedImageOutput) &&
            (isCameraSource || !hasExplicitFilePath))
        {
            return providedImageOutput;
        }

        if (isFileSource)
        {
            if (!hasExplicitFilePath)
            {
                return OperatorExecutionOutput.Failure("FilePath is required when SourceType is File.");
            }

            if (!File.Exists(filePath))
            {
                return OperatorExecutionOutput.Failure($"图像文件不存在: {filePath}");
            }

            var mat = Cv2.ImRead(filePath, ImreadModes.Color);
            if (mat.Empty())
            {
                return OperatorExecutionOutput.Failure("无法加载图像文件，格式可能不受支持");
            }

            return OperatorExecutionOutput.Success(CreateImageOutput(mat, new Dictionary<string, object>
            {
                { "Channels", mat.Channels() },
                { "Source", "file" },
                { "FilePath", filePath }
            }));
        }

        if (isCameraSource)
        {
            var cameraId = GetStringParam(@operator, "CameraId", GetStringParam(@operator, "cameraId", string.Empty));
            if (string.IsNullOrEmpty(cameraId))
            {
                throw new InvalidOperationException("未选择相机");
            }

            try
            {
                var bindingConfig = _cameraManager.FindBinding(cameraId);
                bindingConfig?.Normalize();
                var normalizedTriggerMode = CameraTriggerModeExtensions.Normalize(
                    bindingConfig?.TriggerMode
                    ?? GetStringParam(@operator, "TriggerMode", GetStringParam(@operator, "triggerMode", "Software")));

                if (normalizedTriggerMode.IsFrameDriven())
                {
                    var sharedFrame = await _streamCoordinator.AcquireFrameAsync(bindingConfig?.Id ?? cameraId, cancellationToken);
                    var sharedMat = Cv2.ImDecode(sharedFrame.ImageData, ImreadModes.Color);
                    if (sharedMat.Empty())
                    {
                        return OperatorExecutionOutput.Failure("Camera returned invalid image data.");
                    }

                    return OperatorExecutionOutput.Success(CreateImageOutput(sharedMat, new Dictionary<string, object>
                    {
                        { "Channels", sharedMat.Channels() },
                        { "Source", normalizedTriggerMode.ToConfigValue().ToLowerInvariant() },
                        { "CameraId", bindingConfig?.Id ?? cameraId }
                    }));
                }

                // 获取并配置相机
                var camera = await _cameraManager.GetOrCreateByBindingAsync(cameraId);

                // 相机参数优先来自“系统设置 -> 相机管理”，保留旧算子参数作为向后兼容 fallback。
                var exposureTime = bindingConfig?.ExposureTimeUs
                    ?? GetDoubleParam(@operator, "ExposureTime", GetDoubleParam(@operator, "exposureTime", 5000));
                var gain = bindingConfig?.GainDb
                    ?? GetDoubleParam(@operator, "Gain", GetDoubleParam(@operator, "gain", 1.0));
                await camera.SetExposureTimeAsync(exposureTime);
                await camera.SetGainAsync(gain);

                if (camera is IIndustrialCamera industrialCamera)
                {
                    await industrialCamera.SetTriggerModeAsync(CameraTriggerMode.Software);
                }

                if (bindingConfig?.UsesEnterPhotoelectricTrigger() == true)
                {
                    await _triggerInputService.WaitForEnterPhotoelectricAsync(
                        bindingConfig.ToEnterPhotoelectricTriggerOptions(),
                        cancellationToken);
                }
                else if (bindingConfig?.UsesSerialPhotoelectricTrigger() == true)
                {
                    await _serialPhotoelectricTriggerInputService.WaitForSerialPhotoelectricAsync(
                        bindingConfig.ToSerialPhotoelectricTriggerOptions(),
                        cancellationToken);
                }

                // 采集图像
                var imageData = await camera.AcquireSingleFrameAsync();

                // 解码图像以获取尺寸信息
                var mat = Cv2.ImDecode(imageData, ImreadModes.Color);
                if (mat.Empty())
                {
                    return OperatorExecutionOutput.Failure("相机返回的图像数据无效");
                }

                return OperatorExecutionOutput.Success(CreateImageOutput(mat, new Dictionary<string, object>
                {
                    { "Channels", mat.Channels() },
                    { "Source", ResolveCameraSourceTag(bindingConfig) },
                    { "CameraId", cameraId }
                }));
            }
            catch (Exception ex)
            {
                return OperatorExecutionOutput.Failure($"相机采集失败: {ex.Message}");
            }
        }

        return OperatorExecutionOutput.Failure("SourceType must be File or Camera.");
    }

    public override ValidationResult ValidateParameters(Operator @operator)
    {
        var sourceType = NormalizeSourceType(
            GetStringParam(@operator, "SourceType", GetStringParam(@operator, "sourceType", "File")));
        if (!sourceType.Equals("File", StringComparison.OrdinalIgnoreCase) &&
            !sourceType.Equals("Camera", StringComparison.OrdinalIgnoreCase))
        {
            return ValidationResult.Invalid("SourceType must be File or Camera.");
        }

        if (sourceType.Equals("Camera", StringComparison.OrdinalIgnoreCase))
        {
            var cameraId = GetStringParam(@operator, "CameraId", GetStringParam(@operator, "cameraId", string.Empty));
            if (string.IsNullOrWhiteSpace(cameraId))
            {
                return ValidationResult.Invalid("CameraId is required when SourceType is Camera.");
            }
        }

        return ValidationResult.Valid();
    }

    private static string NormalizeSourceType(string? value)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? "File" : value.Trim();
        var separatorIndex = normalized.IndexOf('|', StringComparison.Ordinal);
        if (separatorIndex >= 0)
        {
            normalized = normalized[..separatorIndex].Trim();
        }

        return normalized;
    }

    private static string? TryGetStringInput(Dictionary<string, object>? inputs, string key)
    {
        if (inputs != null && inputs.TryGetValue(key, out var value))
        {
            return value?.ToString();
        }

        return null;
    }

    private static bool TryCreateOutputFromProvidedImage(
        Dictionary<string, object>? inputs,
        out OperatorExecutionOutput output)
    {
        output = OperatorExecutionOutput.Failure("Image input was not provided.");
        if (inputs == null)
        {
            return false;
        }

        if (!inputs.TryGetValue("Image", out var value) &&
            !inputs.TryGetValue("image", out value))
        {
            return false;
        }

        try
        {
            ImageWrapper image;
            switch (value)
            {
                case ImageWrapper wrapper:
                    var width = wrapper.Width;
                    var height = wrapper.Height;
                    var channels = wrapper.Channels;
                    image = wrapper.AddRef();
                    output = OperatorExecutionOutput.Success(new Dictionary<string, object>
                    {
                        ["Image"] = image,
                        ["Width"] = width,
                        ["Height"] = height,
                        ["Channels"] = channels,
                        ["Source"] = "provided-image"
                    });
                    return true;

                case byte[] bytes:
                    image = new ImageWrapper(bytes);
                    output = OperatorExecutionOutput.Success(new Dictionary<string, object>
                    {
                        ["Image"] = image,
                        ["Width"] = image.Width,
                        ["Height"] = image.Height,
                        ["Channels"] = image.Channels,
                        ["Source"] = "provided-image"
                    });
                    return true;

                case Mat mat:
                    if (mat.Empty())
                    {
                        output = OperatorExecutionOutput.Failure("Provided Image input is empty.");
                        return true;
                    }

                    var cloned = mat.Clone();
                    output = OperatorExecutionOutput.Success(new Dictionary<string, object>
                    {
                        ["Image"] = new ImageWrapper(cloned),
                        ["Width"] = cloned.Width,
                        ["Height"] = cloned.Height,
                        ["Channels"] = cloned.Channels(),
                        ["Source"] = "provided-image"
                    });
                    return true;
            }

            output = OperatorExecutionOutput.Failure($"Provided Image input type is not supported: {value?.GetType().Name ?? "null"}.");
            return true;
        }
        catch (Exception ex)
        {
            output = OperatorExecutionOutput.Failure($"Provided Image input cannot be decoded: {ex.Message}");
            return true;
        }
    }

    private static bool TryGetProvidedFrameEnvelope(Dictionary<string, object>? inputs, out FrameEnvelope? envelope)
    {
        envelope = null;
        if (inputs == null)
        {
            return false;
        }

        if (!inputs.TryGetValue("ProvidedFrameEnvelope", out var value) &&
            !inputs.TryGetValue("providedFrameEnvelope", out value))
        {
            return false;
        }

        envelope = value as FrameEnvelope;
        return envelope != null;
    }

    private OperatorExecutionOutput CreateOutputFromEnvelope(FrameEnvelope envelope)
    {
        try
        {
            var mat = DecodeEnvelope(envelope);
            if (mat.Empty())
            {
                return OperatorExecutionOutput.Failure("Provided frame envelope cannot be decoded.");
            }

            return OperatorExecutionOutput.Success(CreateImageOutput(mat, new Dictionary<string, object>
            {
                { "Channels", mat.Channels() },
                { "Source", "provided-frame-envelope" },
                { "CameraId", envelope.CameraId },
                { "Sequence", envelope.Sequence },
                { "TimestampSource", envelope.TimestampSource.ToString() },
                { "HostReceiveTimestampUtc", envelope.HostReceiveTimestampUtc.UtcDateTime },
                { "CorrelationId", envelope.EffectiveCorrelationId },
                { "TrackId", envelope.Tags != null && envelope.Tags.TryGetValue("TrackId", out var trackId) ? trackId : string.Empty }
            }));
        }
        catch (Exception ex)
        {
            return OperatorExecutionOutput.Failure($"Provided frame envelope decode failed: {ex.Message}");
        }
    }

    private static string ResolveCameraSourceTag(CameraBindingConfig? binding) =>
        binding?.UsesEnterPhotoelectricTrigger() == true
            ? "enter-photoelectric"
            : binding?.UsesSerialPhotoelectricTrigger() == true
                ? "serial-photoelectric"
                : "camera";

    private static Mat DecodeEnvelope(FrameEnvelope envelope)
    {
        if (envelope.PayloadKind == FramePayloadKind.EncodedImage)
        {
            return Cv2.ImDecode(envelope.Payload.ToArray(), ImreadModes.Color);
        }

        var matType = envelope.PixelFormat.Equals("Mono8", StringComparison.OrdinalIgnoreCase)
            ? MatType.CV_8UC1
            : MatType.CV_8UC3;
        using var raw = new Mat(envelope.Height, envelope.Width, matType, envelope.Payload.ToArray());
        var decoded = raw.Clone();
        if (envelope.PixelFormat.Equals("RGB8", StringComparison.OrdinalIgnoreCase))
        {
            Cv2.CvtColor(decoded, decoded, ColorConversionCodes.RGB2BGR);
        }

        return decoded;
    }
}
