using System.Text.Json;
using ClearVision.Product.Core.AI.Tools;
using ClearVision.Product.Core.Cameras;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Interfaces;

namespace ClearVision.Product.Infrastructure.AI.Tools;

public sealed class CameraBindingsTool : VisionAgentToolBase
{
    private readonly ICameraManager _cameraManager;
    private readonly IConfigurationService _configurationService;

    public CameraBindingsTool(ICameraManager cameraManager, IConfigurationService configurationService)
    {
        _cameraManager = cameraManager;
        _configurationService = configurationService;
    }

    public override string Name => "list_camera_bindings";
    public override string DisplayName => "List camera bindings";
    public override string Description => "Lists current logical camera bindings from ClearVision configuration.";
    public override string Category => "camera";
    public override VisionAgentToolPermission Permission => VisionAgentToolPermission.ReadOnly;
    public override JsonElement ParametersSchema { get; } = Schema("""{"type":"object","properties":{}}""");

    public override Task<VisionAgentToolResult> ExecuteAsync(
        VisionAgentToolContext context,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var config = _configurationService.GetCurrent();
        var bindings = _cameraManager.GetBindings();
        if (bindings.Count == 0 && config.Cameras.Count > 0)
        {
            bindings = config.Cameras;
        }

        return Task.FromResult(VisionAgentToolResult.Ok(new
        {
            activeCameraId = config.ActiveCameraId,
            bindings = bindings.Select(ToBindingSummary).ToList()
        }));
    }

    private static object ToBindingSummary(CameraBindingConfig binding)
    {
        return new
        {
            binding.Id,
            binding.DisplayName,
            binding.SerialNumber,
            binding.IpAddress,
            binding.Manufacturer,
            binding.ModelName,
            binding.InterfaceType,
            binding.IsEnabled,
            binding.PixelFormat,
            binding.TriggerMode,
            binding.HardwareTriggerSource,
            binding.SoftwareTriggerSource,
            binding.ExposureTimeUs,
            binding.GainDb,
            binding.TargetFrameRateFps
        };
    }
}

public sealed class CameraDiscoveryTool : VisionAgentToolBase
{
    private readonly ICameraManager _cameraManager;

    public CameraDiscoveryTool(ICameraManager cameraManager)
    {
        _cameraManager = cameraManager;
    }

    public override string Name => "discover_cameras";
    public override string DisplayName => "Discover cameras";
    public override string Description => "Discovers available physical cameras using ClearVision camera providers.";
    public override string Category => "camera";
    public override VisionAgentToolPermission Permission => VisionAgentToolPermission.ReadOnly;
    public override JsonElement ParametersSchema { get; } = Schema("""
        {
          "type": "object",
          "properties": {
            "manufacturer": { "type": "string", "enum": ["Huaray", "Hikvision", "Any"] }
          }
        }
        """);

    public override async Task<VisionAgentToolResult> ExecuteAsync(
        VisionAgentToolContext context,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        var cameras = await _cameraManager.EnumerateCamerasAsync();
        cancellationToken.ThrowIfCancellationRequested();
        var all = cameras.ToList();
        var manufacturer = ReadString(arguments, "manufacturer") ?? "Any";
        var filtered = FilterByManufacturer(all, manufacturer).ToList();
        return VisionAgentToolResult.Ok(new
        {
            manufacturer = NormalizeManufacturerFilter(manufacturer),
            diagnostics = new
            {
                totalDiscovered = all.Count,
                returned = filtered.Count,
                unknownManufacturer = all.Count(camera => string.IsNullOrWhiteSpace(camera.Manufacturer))
            },
            cameras = filtered.Select(camera => new
            {
                camera.CameraId,
                camera.Name,
                camera.IpAddress,
                camera.Manufacturer,
                camera.Model,
                camera.ConnectionType,
                camera.IsConnected
            }).ToList()
        });
    }

    private static IEnumerable<CameraInfo> FilterByManufacturer(IEnumerable<CameraInfo> cameras, string manufacturer)
    {
        var normalized = NormalizeManufacturerFilter(manufacturer);
        if (normalized == "Any")
        {
            return cameras;
        }

        return cameras.Where(camera =>
            string.Equals(camera.Manufacturer, normalized, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeManufacturerFilter(string? manufacturer)
    {
        var normalized = (manufacturer ?? string.Empty).Trim();
        if (normalized.Equals("Huaray", StringComparison.OrdinalIgnoreCase))
        {
            return "Huaray";
        }

        if (normalized.Equals("Hikvision", StringComparison.OrdinalIgnoreCase))
        {
            return "Hikvision";
        }

        return "Any";
    }
}

public sealed class CameraTestFrameTool : VisionAgentToolBase
{
    private readonly ICameraManager _cameraManager;
    private readonly IVisionAgentTemporaryFrameStore _frameStore;

    public CameraTestFrameTool(ICameraManager cameraManager, IVisionAgentTemporaryFrameStore frameStore)
    {
        _cameraManager = cameraManager;
        _frameStore = frameStore;
    }

    public override string Name => "capture_test_frame";
    public override string DisplayName => "Capture test frame";
    public override string Description => "Captures one frame through an existing ClearVision camera binding and stores it as a short-lived temporaryFrameId.";
    public override string Category => "camera";
    public override VisionAgentToolPermission Permission => VisionAgentToolPermission.RuntimePreview;
    public override JsonElement ParametersSchema { get; } = Schema("""
        {
          "type": "object",
          "required": ["cameraBindingId"],
          "properties": {
            "cameraBindingId": { "type": "string" },
            "timeoutMs": { "type": "integer", "minimum": 500, "maximum": 60000 }
          }
        }
        """);

    public override async Task<VisionAgentToolResult> ExecuteAsync(
        VisionAgentToolContext context,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        var bindingId = ReadString(arguments, "cameraBindingId");
        if (string.IsNullOrWhiteSpace(bindingId))
        {
            return VisionAgentToolResult.Fail("camera_binding_required", "cameraBindingId is required.");
        }

        var binding = _cameraManager.FindBinding(bindingId);
        if (binding == null)
        {
            return VisionAgentToolResult.Fail(
                "camera_binding_not_found",
                $"Camera binding '{bindingId}' was not found.");
        }

        binding.Normalize();
        var triggerMode = CameraTriggerModeExtensions.Normalize(binding.TriggerMode);
        var softwareTriggerSource = CameraSoftwareTriggerSourceExtensions.Normalize(binding.SoftwareTriggerSource);
        if (triggerMode != CameraTriggerMode.Software ||
            softwareTriggerSource != CameraSoftwareTriggerSource.Manual)
        {
            return VisionAgentToolResult.Fail(
                "unsupported_trigger_mode_for_test_frame",
                "capture_test_frame only supports Software/Manual trigger bindings by default.",
                new
                {
                    cameraBindingId = binding.Id,
                    binding.DisplayName,
                    triggerMode = binding.TriggerMode,
                    softwareTriggerSource = binding.SoftwareTriggerSource
                });
        }

        var camera = await _cameraManager.GetOrCreateByBindingAsync(binding.Id);
        var timeoutMs = Math.Clamp(ReadInt(arguments, "timeoutMs") ?? 5000, 500, 60000);
        byte[] bytes;
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var acquireTask = camera.AcquireSingleFrameAsync();
            var completed = await Task.WhenAny(acquireTask, Task.Delay(timeoutMs, timeoutCts.Token));
            if (completed != acquireTask)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return VisionAgentToolResult.Fail(
                    "capture_test_frame_timeout",
                    $"AcquireSingleFrameAsync timed out after {timeoutMs} ms.",
                    new { cameraBindingId = binding.Id, timeoutMs });
            }

            timeoutCts.Cancel();
            bytes = await acquireTask;
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            return VisionAgentToolResult.Fail(
                "capture_test_frame_cancelled",
                ex.Message,
                new { cameraBindingId = binding.Id });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return VisionAgentToolResult.Fail(
                "capture_test_frame_failed",
                ex.Message,
                new { cameraBindingId = binding.Id });
        }
        cancellationToken.ThrowIfCancellationRequested();
        var parameters = camera.GetParameters();
        string temporaryFrameId;
        try
        {
            temporaryFrameId = _frameStore.Store(bytes, new VisionAgentTemporaryFrameMetadata
            {
                CameraBindingId = binding.Id,
                CameraId = camera.CameraId,
                CameraName = camera.Name,
                Width = parameters.Width,
                Height = parameters.Height,
                PixelFormat = parameters.PixelFormat,
                CapturedAtUtc = DateTimeOffset.UtcNow
            });
        }
        catch (InvalidOperationException ex)
        {
            return VisionAgentToolResult.Fail(
                "temporary_frame_store_limit_exceeded",
                ex.Message,
                new { byteLength = bytes.Length, stats = _frameStore.GetStats() });
        }

        return VisionAgentToolResult.Ok(new
        {
            temporaryFrameId,
            cameraBindingId = binding.Id,
            camera.CameraId,
            camera.Name,
            byteLength = bytes.Length,
            parameters.Width,
            parameters.Height,
            parameters.PixelFormat,
            triggerMode = binding.TriggerMode,
            softwareTriggerSource = binding.SoftwareTriggerSource,
            frameStore = _frameStore.GetStats()
        });
    }
}

public sealed class CameraBindingDraftTool : VisionAgentToolBase
{
    public override string Name => "draft_camera_binding";
    public override string DisplayName => "Draft camera binding";
    public override string Description => "Creates a camera binding draft for user confirmation. It never saves AppConfig directly.";
    public override string Category => "camera";
    public override VisionAgentToolPermission Permission => VisionAgentToolPermission.ConfigDraft;
    public override JsonElement ParametersSchema { get; } = Schema("""
        {
          "type": "object",
          "properties": {
            "cameraId": { "type": "string" },
            "displayName": { "type": "string" },
            "manufacturer": { "type": "string" },
            "modelName": { "type": "string" },
            "serialNumber": { "type": "string" },
            "ipAddress": { "type": "string" },
            "triggerMode": { "type": "string" },
            "pixelFormat": { "type": "string" }
          }
        }
        """);

    public override Task<VisionAgentToolResult> ExecuteAsync(
        VisionAgentToolContext context,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var serialNumber = ReadString(arguments, "serialNumber") ?? ReadString(arguments, "cameraId") ?? string.Empty;
        var draft = new
        {
            id = CreateDraftId(serialNumber),
            displayName = ReadString(arguments, "displayName") ?? "Camera",
            serialNumber,
            ipAddress = ReadString(arguments, "ipAddress") ?? string.Empty,
            manufacturer = ReadString(arguments, "manufacturer") ?? "Huaray",
            modelName = ReadString(arguments, "modelName") ?? string.Empty,
            interfaceType = ReadString(arguments, "interfaceType") ?? string.Empty,
            isEnabled = true,
            exposureTimeUs = 5000.0,
            gainDb = 1.0,
            pixelFormat = ReadString(arguments, "pixelFormat") ?? CameraPixelFormatExtensions.DefaultPixelFormat,
            triggerMode = ReadString(arguments, "triggerMode") ?? "Software"
        };

        var action = new VisionAgentPendingAction
        {
            ActionType = "cameraBindingDraft.apply",
            Title = "Apply camera binding draft",
            Summary = $"Draft camera binding for {draft.displayName} ({draft.serialNumber}).",
            Payload = draft,
            RequiresUserConfirmation = true
        };

        return Task.FromResult(VisionAgentToolResult.Ok(
            new { draftBinding = draft, requiresUserConfirmation = true },
            requiresUserConfirmation: true,
            pendingActions: [action]));
    }

    private static string CreateDraftId(string serialNumber)
    {
        var seed = string.IsNullOrWhiteSpace(serialNumber)
            ? Guid.NewGuid().ToString("N")
            : serialNumber.Trim();
        var normalized = new string(seed.Where(char.IsLetterOrDigit).Take(16).ToArray());
        return string.IsNullOrWhiteSpace(normalized)
            ? Guid.NewGuid().ToString("N")[..8]
            : $"cam_{normalized}";
    }
}

