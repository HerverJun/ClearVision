// CameraManager.cs
// 相机管理器实现
// 负责相机枚举、绑定配置与逻辑 ID 映射管理
// 作者：蘅芜君
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using ClearVision.Product.Core.Cameras;
using ClearVision.Product.Core.Entities;
using Microsoft.Extensions.Logging;

namespace ClearVision.Product.Infrastructure.Cameras;

/// <summary>
/// 相机管理器实现
/// </summary>
public class CameraManager : ICameraManager, IDisposable
{
    private readonly ConcurrentDictionary<string, ICamera> _cameras = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ICameraProvider> _providers = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _cameraLocks = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<CameraManager> _logger;
    private List<CameraBindingConfig> _bindings = new();
    private string _activeCameraId = "";
    private bool _disposed;

    public CameraManager(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<CameraManager>();
    }

    /// <summary>
    /// 枚举所有可用相机设备
    /// </summary>
    public Task<IEnumerable<CameraInfo>> EnumerateCamerasAsync()
    {
        var allDevices = CameraProviderFactory.DiscoverAll();
        var cameraInfos = allDevices.Select(d => new CameraInfo
        {
            CameraId = d.SerialNumber,
            Name = string.IsNullOrEmpty(d.UserDefinedName) ? d.Model : d.UserDefinedName,
            Manufacturer = d.Manufacturer,
            Model = d.Model,
            ConnectionType = d.InterfaceType,
            IsConnected = _cameras.ContainsKey(d.SerialNumber)
        });

        return Task.FromResult(cameraInfos);
    }

    /// <summary>
    /// 获取或创建相机（基于原始序列号）
    /// </summary>
    public async Task<ICamera> GetOrCreateCameraAsync(string cameraId)
    {
        ThrowIfDisposed();
        var cameraKey = NormalizeCameraKey(cameraId);
        if (_cameras.TryGetValue(cameraKey, out var existingCamera))
        {
            return existingCamera;
        }

        var cameraLock = _cameraLocks.GetOrAdd(cameraKey, _ => new SemaphoreSlim(1, 1));
        await cameraLock.WaitAsync();
        try
        {
            if (_cameras.TryGetValue(cameraKey, out existingCamera))
            {
                return existingCamera;
            }

            // AutoDetect internally opens the camera and returns a connected provider.
            var provider = CameraProviderFactory.AutoDetect(cameraKey);
            if (provider == null)
                throw new InvalidOperationException($"Failed to detect camera: {cameraKey}. Check power, connection, and SDK installation.");

            var cameraAdapter = new CameraProviderAdapter(cameraKey, provider, _loggerFactory.CreateLogger<CameraProviderAdapter>());
            _cameras[cameraKey] = cameraAdapter;
            _providers[cameraKey] = provider;

            return cameraAdapter;
        }
        finally
        {
            cameraLock.Release();
        }
    }

    /// <summary>
    /// 根据绑定ID获取相机
    /// </summary>
    public async Task<ICamera> GetOrCreateByBindingAsync(string bindingId)
    {
        var bindingKey = NormalizeCameraKey(bindingId);
        var binding = _bindings.FirstOrDefault(b => b.Id.Equals(bindingKey, StringComparison.OrdinalIgnoreCase));
        if (binding == null)
        {
            // 如果找不到绑定，尝试直接作为SN处理（向下兼容）
            return await GetOrCreateCameraAsync(bindingKey);
        }

        if (string.IsNullOrEmpty(binding.SerialNumber))
        {
            throw new InvalidOperationException($"绑定 '{binding.DisplayName}' 未关联物理设备序列号");
        }

        return await GetOrCreateCameraAsync(binding.SerialNumber);
    }

    public Task<ICamera> OpenCameraAsync(string cameraId) => GetOrCreateCameraAsync(cameraId);

    public async Task CloseCameraAsync(string cameraId)
    {
        var cameraKey = NormalizeCameraKey(cameraId);
        var cameraLock = _cameraLocks.GetOrAdd(cameraKey, _ => new SemaphoreSlim(1, 1));
        await cameraLock.WaitAsync();
        try
        {
            _providers.TryRemove(cameraKey, out var provider);
            if (_cameras.TryRemove(cameraKey, out var camera))
            {
                camera.Dispose();
            }
            else
            {
                provider?.Dispose();
            }
        }
        finally
        {
            cameraLock.Release();
        }
    }

    public ICamera? GetCamera(string cameraId)
    {
        if (string.IsNullOrWhiteSpace(cameraId))
        {
            return null;
        }

        _cameras.TryGetValue(cameraId.Trim(), out var camera);
        return camera;
    }

    /// <summary>
    /// This is intentionally a synchronous shim. The disconnect path only disposes in-memory camera
    /// wrappers/providers and does not perform async I/O, so callers should not infer UI-thread deadlock risk.
    /// </summary>
    public Task DisconnectAllAsync()
    {
        DisconnectAllCore();
        return Task.CompletedTask;
    }

    private void DisconnectAllCore()
    {
        foreach (var camera in _cameras.Values)
        {
            camera.Dispose();
        }

        foreach (var (cameraId, provider) in _providers)
        {
            if (!_cameras.ContainsKey(cameraId))
            {
                provider.Dispose();
            }
        }

        _cameras.Clear();
        _providers.Clear();
    }

    // --- 相机绑定管理功能 ---

    public void LoadBindings(List<CameraBindingConfig> bindings, string activeCameraId)
    {
        _bindings = (bindings ?? new List<CameraBindingConfig>())
            .Select(binding =>
            {
                binding.Normalize();
                return binding;
            })
            .ToList();
        _activeCameraId = activeCameraId ?? "";
        _logger.LogDebug("[CameraManager] 已加载 {Count} 个相机绑定", _bindings.Count);
    }

    public List<CameraBindingConfig> GetBindings() => _bindings;

    public void UpdateBindings(List<CameraBindingConfig> bindings, string activeCameraId)
    {
        _bindings = (bindings ?? new List<CameraBindingConfig>())
            .Select(binding =>
            {
                binding.Normalize();
                return binding;
            })
            .ToList();
        _activeCameraId = activeCameraId ?? "";
        _logger.LogDebug("[CameraManager] 已更新绑定，活动相机: {ActiveCameraId}", _activeCameraId);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            DisconnectAllCore();
            foreach (var cameraLock in _cameraLocks.Values)
            {
                cameraLock.Dispose();
            }

            _cameraLocks.Clear();
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }

    private static string NormalizeCameraKey(string cameraId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cameraId);
        return cameraId.Trim();
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}

/// <summary>
/// 相机提供器适配器
/// </summary>
public class CameraProviderAdapter : IIndustrialCamera
{
    private readonly string _cameraId;
    private readonly ICameraProvider _provider;
    private readonly SemaphoreSlim _providerGate = new(1, 1);
    private bool _isAcquiring;
    private Func<byte[], Task>? _frameCallback;
    private CancellationTokenSource? _acquisitionCts;
    private Task? _acquisitionTask;
    private CameraTriggerMode _currentTriggerMode = CameraTriggerMode.Software;
    private string _currentHardwareTriggerSource = CameraHardwareTriggerSourceExtensions.DefaultHardwareTriggerSource;
    private bool _providerTriggerModeKnown;

    public string CameraId => _cameraId;
    public string Name => _provider.CurrentDevice?.UserDefinedName ?? _cameraId;
    public bool IsConnected => _provider.IsConnected;
    public bool IsAcquiring => _isAcquiring;

    public event EventHandler<CameraFrameReceivedEventArgs>? FrameReceived;

    private readonly ILogger<CameraProviderAdapter> _logger;

    public CameraProviderAdapter(string cameraId, ICameraProvider provider, ILogger<CameraProviderAdapter> logger)
    {
        _cameraId = cameraId;
        _provider = provider;
        _logger = logger;
    }

    public Task ConnectAsync() => Task.CompletedTask;

    public async Task DisconnectAsync()
    {
        await StopContinuousAcquisitionAsync();
        await _providerGate.WaitAsync();
        try
        {
            _provider.Close();
        }
        finally
        {
            _providerGate.Release();
        }
    }

    public async Task<byte[]> AcquireSingleFrameAsync()
    {
        CameraTriggerMode? modeToRestore = null;
        string? hardwareTriggerSourceToRestore = null;
        bool wasGrabbing = false;
        bool stoppedForTriggerModeChange = false;
        await _providerGate.WaitAsync();
        try
        {
            wasGrabbing = _provider.IsGrabbing;
            if (_isAcquiring)
            {
                modeToRestore = _currentTriggerMode;
                hardwareTriggerSourceToRestore = _currentHardwareTriggerSource;
            }

            if (!_providerTriggerModeKnown || _currentTriggerMode != CameraTriggerMode.Software)
            {
                try
                {
                    SetProviderTriggerMode(CameraTriggerMode.Software);
                }
                catch when (_provider.IsGrabbing)
                {
                    if (!_provider.StopGrabbing())
                    {
                        throw new InvalidOperationException($"Failed to stop camera acquisition before software trigger: {_cameraId}");
                    }

                    stoppedForTriggerModeChange = true;
                    SetProviderTriggerMode(CameraTriggerMode.Software);
                }
            }

            if (!_provider.IsGrabbing && !_provider.StartGrabbing())
            {
                throw new InvalidOperationException($"Failed to start camera acquisition: {_cameraId}");
            }

            if (!_provider.ExecuteSoftwareTrigger())
            {
                throw new InvalidOperationException($"Failed to execute software trigger: {_cameraId}");
            }

            var frame = _provider.GetFrame(3000);
            if (frame == null)
            {
                throw new TimeoutException("获取图像超时");
            }

            return EncodeFrameToBytes(frame);
        }
        finally
        {
            if (modeToRestore.HasValue && modeToRestore.Value != CameraTriggerMode.Software && _provider.IsConnected)
            {
                if ((stoppedForTriggerModeChange || _provider.IsGrabbing) && !_provider.StopGrabbing())
                {
                    _logger.LogWarning("[CameraProviderAdapter] 切回原触发模式前停止 SDK 采集失败。CameraId={CameraId}", _cameraId);
                }

                TryRestoreTriggerMode(modeToRestore.Value, hardwareTriggerSourceToRestore);
                if (wasGrabbing && !_provider.IsGrabbing && !_provider.StartGrabbing())
                {
                    _logger.LogWarning("[CameraProviderAdapter] 软件触发后恢复 SDK 采集失败。CameraId={CameraId}", _cameraId);
                }
            }

            _providerGate.Release();
        }
    }

    public Task StartContinuousAcquisitionAsync(Func<byte[], Task> frameCallback)
    {
        if (_isAcquiring)
            return Task.CompletedTask;

        _frameCallback = frameCallback;
        _isAcquiring = true;

        _acquisitionCts?.Dispose();
        _acquisitionCts = new CancellationTokenSource();
        var token = _acquisitionCts.Token;

        _acquisitionTask = Task.Run(async () =>
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    byte[] imageData;
                    CameraFrame frame;
                    await _providerGate.WaitAsync(token);
                    try
                    {
                        if (!_provider.IsGrabbing && !_provider.StartGrabbing())
                            throw new InvalidOperationException($"Failed to start camera acquisition: {_cameraId}");

                        var grabbedFrame = _provider.GetFrame(1000);
                        if (grabbedFrame == null)
                            continue;

                        frame = grabbedFrame;
                        imageData = EncodeFrameToBytes(frame, useFastEncoding: true);
                    }
                    finally
                    {
                        _providerGate.Release();
                    }

                    if (_frameCallback != null)
                        await _frameCallback(imageData);

                    FrameReceived?.Invoke(this, new CameraFrameReceivedEventArgs
                    {
                        ImageData = imageData,
                        Width = frame.Width,
                        Height = frame.Height,
                        Timestamp = DateTime.UtcNow,
                        DeviceFrameCounter = frame.FrameNumber == 0 ? null : frame.FrameNumber,
                        CameraTimestampNs = frame.Timestamp == 0 ? null : frame.Timestamp,
                        PixelFormat = frame.PixelFormat,
                        Stride = ResolveStride(frame)
                    });
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[CameraProviderAdapter] 连续采集异常，相机可能已断线。CameraId={CameraId}", _cameraId);
            }
            finally
            {
                _isAcquiring = false;
            }
        }, token);

        return Task.CompletedTask;
    }

    public async Task StopContinuousAcquisitionAsync()
    {
        _isAcquiring = false;

        if (_acquisitionCts != null)
        {
            _acquisitionCts.Cancel();
        }

        if (_acquisitionTask != null)
        {
            try
            {
                await _acquisitionTask.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (OperationCanceledException)
            {
            }
            catch (TimeoutException ex)
            {
                _logger.LogWarning(ex, "[CameraProviderAdapter] 停止采集等待超时，将继续释放调用方。CameraId={CameraId}", _cameraId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[CameraProviderAdapter] 停止采集等待异常。CameraId={CameraId}", _cameraId);
            }
            finally
            {
                _acquisitionTask = null;
            }
        }

        _acquisitionCts?.Dispose();
        _acquisitionCts = null;
        if (!await _providerGate.WaitAsync(TimeSpan.FromSeconds(2)))
        {
            _logger.LogWarning("[CameraProviderAdapter] 停止采集时等待 SDK 门锁超时，跳过 StopGrabbing。CameraId={CameraId}", _cameraId);
            return;
        }

        try
        {
            if (_provider.IsConnected && !_provider.StopGrabbing())
            {
                _logger.LogWarning("[CameraProviderAdapter] 停止 SDK 采集失败。CameraId={CameraId}", _cameraId);
            }
        }
        finally
        {
            _providerGate.Release();
        }
    }

    public async Task SetExposureTimeAsync(double exposureTime)
    {
        await _providerGate.WaitAsync();
        try
        {
            if (!_provider.SetExposure(exposureTime))
            {
                throw new InvalidOperationException($"Failed to set exposure for camera '{_cameraId}' to {exposureTime} us.");
            }
        }
        finally
        {
            _providerGate.Release();
        }
    }

    public async Task SetGainAsync(double gain)
    {
        await _providerGate.WaitAsync();
        try
        {
            if (!_provider.SetGain(gain))
            {
                throw new InvalidOperationException($"Failed to set gain for camera '{_cameraId}' to {gain} dB.");
            }
        }
        finally
        {
            _providerGate.Release();
        }
    }

    public async Task SetPixelFormatAsync(CameraPixelFormat pixelFormat)
    {
        await _providerGate.WaitAsync();
        var restartRequired = false;
        try
        {
            if (_provider.IsGrabbing)
            {
                if (!_provider.StopGrabbing())
                {
                    throw new InvalidOperationException($"Failed to stop acquisition before setting pixel format for camera '{_cameraId}'.");
                }

                restartRequired = true;
            }

            var normalizedPixelFormat = CameraPixelFormatExtensions.Normalize(pixelFormat.ToConfigValue());
            if (!_provider.SetPixelFormat(normalizedPixelFormat))
            {
                throw new InvalidOperationException($"Failed to set pixel format for camera '{_cameraId}' to {normalizedPixelFormat.ToConfigValue()}.");
            }

            if (restartRequired)
            {
                restartRequired = false;
                if (!_provider.StartGrabbing())
                {
                    throw new InvalidOperationException($"Failed to restart acquisition after setting pixel format for camera '{_cameraId}'.");
                }
            }
        }
        finally
        {
            if (restartRequired && _provider.IsConnected && !_provider.IsGrabbing && !_provider.StartGrabbing())
            {
                _logger.LogWarning("[CameraProviderAdapter] 设置像素格式后恢复 SDK 采集失败。CameraId={CameraId}", _cameraId);
            }

            _providerGate.Release();
        }
    }

    public async Task SetTriggerModeAsync(CameraTriggerMode mode, string? hardwareTriggerSource = null)
    {
        await _providerGate.WaitAsync();
        try
        {
            SetProviderTriggerMode(mode, hardwareTriggerSource);
        }
        finally
        {
            _providerGate.Release();
        }
    }

    public async Task ExecuteSoftwareTriggerAsync()
    {
        await _providerGate.WaitAsync();
        try
        {
            if (!_provider.ExecuteSoftwareTrigger())
            {
                throw new InvalidOperationException($"Failed to execute software trigger: {_cameraId}");
            }
        }
        finally
        {
            _providerGate.Release();
        }
    }

    public CameraParameters GetParameters() => new CameraParameters();

    public void Dispose()
    {
        StopContinuousAcquisitionAsync().GetAwaiter().GetResult();
        _provider.Dispose();
        _providerGate.Dispose();
    }

    private void SetProviderTriggerMode(CameraTriggerMode mode, string? hardwareTriggerSource = null)
    {
        var normalizedHardwareSource = CameraHardwareTriggerSourceExtensions.Normalize(hardwareTriggerSource ?? _currentHardwareTriggerSource);
        var providerHardwareSource = mode == CameraTriggerMode.External
            ? normalizedHardwareSource
            : null;

        if (!_provider.SetTriggerMode(mode, providerHardwareSource))
        {
            throw new InvalidOperationException(
                $"Failed to set trigger mode for camera '{_cameraId}' to {mode}"
                + (mode == CameraTriggerMode.External ? $" ({normalizedHardwareSource})" : string.Empty)
                + ".");
        }

        _currentTriggerMode = mode;
        _providerTriggerModeKnown = true;
        if (mode == CameraTriggerMode.External)
        {
            _currentHardwareTriggerSource = normalizedHardwareSource;
        }
    }

    private void TryRestoreTriggerMode(CameraTriggerMode mode, string? hardwareTriggerSource)
    {
        try
        {
            SetProviderTriggerMode(mode, hardwareTriggerSource);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[CameraProviderAdapter] 恢复连续采集触发模式失败。CameraId={CameraId}, TriggerMode={TriggerMode}", _cameraId, mode);
        }
    }

    private byte[] EncodeFrameToBytes(CameraFrame frame, bool useFastEncoding = false)
    {
        OpenCvSharp.MatType matType;
        bool needConversion = false;
        OpenCvSharp.ColorConversionCodes conversionCode = OpenCvSharp.ColorConversionCodes.BayerBG2BGR;

        switch (frame.PixelFormat)
        {
            case CameraPixelFormat.Mono8:
                matType = OpenCvSharp.MatType.CV_8UC1;
                break;
            case CameraPixelFormat.RGB8:
                matType = OpenCvSharp.MatType.CV_8UC3;
                needConversion = true;
                conversionCode = OpenCvSharp.ColorConversionCodes.RGB2BGR;
                break;
            case CameraPixelFormat.BGR8:
                matType = OpenCvSharp.MatType.CV_8UC3;
                break;
            case CameraPixelFormat.BayerRG8:
                matType = OpenCvSharp.MatType.CV_8UC1;
                needConversion = true;
                conversionCode = OpenCvSharp.ColorConversionCodes.BayerRG2BGR;
                break;
            case CameraPixelFormat.BayerGB8:
                matType = OpenCvSharp.MatType.CV_8UC1;
                needConversion = true;
                conversionCode = OpenCvSharp.ColorConversionCodes.BayerGB2BGR;
                break;
            case CameraPixelFormat.BayerGR8:
                matType = OpenCvSharp.MatType.CV_8UC1;
                needConversion = true;
                conversionCode = OpenCvSharp.ColorConversionCodes.BayerGR2BGR;
                break;
            case CameraPixelFormat.BayerBG8:
                matType = OpenCvSharp.MatType.CV_8UC1;
                needConversion = true;
                conversionCode = OpenCvSharp.ColorConversionCodes.BayerBG2BGR;
                break;
            default:
                matType = OpenCvSharp.MatType.CV_8UC1;
                break;
        }

        using var mat = new OpenCvSharp.Mat(frame.Height, frame.Width, matType, frame.DataPtr);

        if (needConversion)
        {
            using var cvtMat = new OpenCvSharp.Mat();
            OpenCvSharp.Cv2.CvtColor(mat, cvtMat, conversionCode);
            if (useFastEncoding)
                return cvtMat.ToBytes(".jpg", new int[] { (int)OpenCvSharp.ImwriteFlags.JpegQuality, 85 });
            return cvtMat.ToBytes(".png");
        }
        else
        {
            if (useFastEncoding)
                return mat.ToBytes(".jpg", new int[] { (int)OpenCvSharp.ImwriteFlags.JpegQuality, 85 });
            return mat.ToBytes(".png");
        }
    }

    private static int? ResolveStride(CameraFrame frame)
    {
        var channels = frame.PixelFormat is CameraPixelFormat.RGB8 or CameraPixelFormat.BGR8 ? 3 : 1;
        return frame.Width > 0 ? frame.Width * channels : null;
    }
}
