// CameraManager.cs
// 相机管理器实现
// 负责相机枚举、绑定配置与逻辑 ID 映射管理
// 作者：蘅芜君
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Acme.Product.Core.Cameras;
using Acme.Product.Core.Entities;
using Microsoft.Extensions.Logging;

namespace Acme.Product.Infrastructure.Cameras;

/// <summary>
/// 相机管理器实现
/// </summary>
public class CameraManager : ICameraManager, IDisposable
{
    private readonly ConcurrentDictionary<string, ICamera> _cameras = new();
    private readonly ConcurrentDictionary<string, ICameraProvider> _providers = new();
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
    public Task<ICamera> GetOrCreateCameraAsync(string cameraId)
    {
        if (_cameras.TryGetValue(cameraId, out var existingCamera))
        {
            return Task.FromResult(existingCamera);
        }

        // AutoDetect internally opens the camera and returns a connected provider.
        var provider = CameraProviderFactory.AutoDetect(cameraId);
        if (provider == null)
            throw new InvalidOperationException($"Failed to detect camera: {cameraId}. Check power, connection, and SDK installation.");

        var cameraAdapter = new CameraProviderAdapter(cameraId, provider, _loggerFactory.CreateLogger<CameraProviderAdapter>());
        _cameras[cameraId] = cameraAdapter;
        _providers[cameraId] = provider;

        return Task.FromResult<ICamera>(cameraAdapter);
    }

    /// <summary>
    /// 根据绑定ID获取相机
    /// </summary>
    public async Task<ICamera> GetOrCreateByBindingAsync(string bindingId)
    {
        var binding = _bindings.FirstOrDefault(b => b.Id == bindingId);
        if (binding == null)
        {
            // 如果找不到绑定，尝试直接作为SN处理（向下兼容）
            return await GetOrCreateCameraAsync(bindingId);
        }

        if (string.IsNullOrEmpty(binding.SerialNumber))
        {
            throw new InvalidOperationException($"绑定 '{binding.DisplayName}' 未关联物理设备序列号");
        }

        return await GetOrCreateCameraAsync(binding.SerialNumber);
    }

    public Task<ICamera> OpenCameraAsync(string cameraId) => GetOrCreateCameraAsync(cameraId);

    public Task CloseCameraAsync(string cameraId)
    {
        if (_cameras.TryRemove(cameraId, out var camera))
            camera.Dispose();
        if (_providers.TryRemove(cameraId, out var provider))
            provider.Dispose();
        return Task.CompletedTask;
    }

    public ICamera? GetCamera(string cameraId)
    {
        _cameras.TryGetValue(cameraId, out var camera);
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
            _disposed = true;
        }
        GC.SuppressFinalize(this);
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
        // 严格按照华睿 SDK 正确时序调用
        // 1) StartGrabbing -> 2) TriggerMode=On,TriggerSource=Software -> 3) ExecuteSoftwareTrigger -> 4) GetFrame

        CameraTriggerMode? modeToRestore = null;
        await _providerGate.WaitAsync();
        try
        {
            if (_isAcquiring)
            {
                modeToRestore = _currentTriggerMode;
            }

            // 1) 确保采集已启动
            if (!_provider.IsGrabbing)
                _provider.StartGrabbing();

            // 2) 设置软件触发模式（TriggerMode=On, TriggerSource=Software）
            SetProviderTriggerMode(CameraTriggerMode.Software);

            // 3) 发送软触发命令
            _provider.ExecuteSoftwareTrigger();

            // 4) 获取帧（给 SDK 足够响应时间）
            var frame = _provider.GetFrame(3000);
            if (frame == null)
                throw new TimeoutException("获取图像超时");

            return EncodeFrameToBytes(frame);
        }
        finally
        {
            if (modeToRestore.HasValue && modeToRestore.Value != CameraTriggerMode.Software && _provider.IsConnected)
            {
                TryRestoreTriggerMode(modeToRestore.Value);
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
                        if (!_provider.IsGrabbing)
                            _provider.StartGrabbing();

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
                await _acquisitionTask;
            }
            catch (OperationCanceledException)
            {
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
        await _providerGate.WaitAsync();
        try
        {
            _provider.StopGrabbing();
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
            _provider.SetExposure(exposureTime);
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
            _provider.SetGain(gain);
        }
        finally
        {
            _providerGate.Release();
        }
    }

    public async Task SetTriggerModeAsync(CameraTriggerMode mode)
    {
        await _providerGate.WaitAsync();
        try
        {
            SetProviderTriggerMode(mode);
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
            _provider.ExecuteSoftwareTrigger();
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

    private void SetProviderTriggerMode(CameraTriggerMode mode)
    {
        if (_provider.SetTriggerMode(mode))
        {
            _currentTriggerMode = mode;
        }
    }

    private void TryRestoreTriggerMode(CameraTriggerMode mode)
    {
        try
        {
            SetProviderTriggerMode(mode);
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
