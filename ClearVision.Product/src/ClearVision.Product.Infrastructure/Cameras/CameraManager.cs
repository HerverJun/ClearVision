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
    private readonly ConcurrentDictionary<string, ManagedCameraEntry> _cameras = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, CameraLockEntry> _cameraLocks = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<CameraManager> _logger;
    private readonly Func<string, string?, ICameraProvider?> _providerFactory;
    private readonly Func<List<CameraDeviceInfo>> _deviceDiscovery;
    private readonly object _bindingsSync = new();
    private List<CameraBindingConfig> _bindings = new();
    private string _activeCameraId = "";
    private int _disposed;

    internal int RetainedCameraLockCount => _cameraLocks.Count;

    internal int GetCameraLockReferenceCount(string cameraId)
    {
        var cameraKey = NormalizeCameraKey(cameraId);
        return _cameraLocks.TryGetValue(cameraKey, out var entry)
            ? entry.ReferenceCount
            : 0;
    }

    public CameraManager(ILoggerFactory loggerFactory)
        : this(
            loggerFactory,
            (serialNumber, manufacturerHint) => CameraProviderFactory.AutoDetect(serialNumber, manufacturerHint),
            CameraProviderFactory.DiscoverAll)
    {
    }

    public CameraManager(
        ILoggerFactory loggerFactory,
        Func<string, string?, ICameraProvider?> providerFactory,
        Func<List<CameraDeviceInfo>>? deviceDiscovery = null)
    {
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<CameraManager>();
        _providerFactory = providerFactory ?? throw new ArgumentNullException(nameof(providerFactory));
        _deviceDiscovery = deviceDiscovery ?? (() => new List<CameraDeviceInfo>());
    }

    /// <summary>
    /// 枚举所有可用相机设备
    /// </summary>
    public Task<IEnumerable<CameraInfo>> EnumerateCamerasAsync()
    {
        var allDevices = _deviceDiscovery();
        var cameraInfos = allDevices.Select(d => new CameraInfo
        {
            CameraId = d.SerialNumber,
            Name = string.IsNullOrEmpty(d.UserDefinedName) ? d.Model : d.UserDefinedName,
            Manufacturer = d.Manufacturer,
            Model = d.Model,
            ConnectionType = d.InterfaceType,
            IsConnected = GetCamera(d.SerialNumber) != null
        });

        return Task.FromResult(cameraInfos);
    }

    /// <summary>
    /// 获取或创建相机。参数必须是已加载且启用的服务端绑定 ID。
    /// </summary>
    public Task<ICamera> GetOrCreateCameraAsync(string cameraId) => GetOrCreateByBindingAsync(cameraId);

    private async Task<ICamera> GetOrCreateCameraBySerialAsync(
        string serialNumber,
        string? manufacturerHint,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        var cameraKey = NormalizeCameraKey(serialNumber);
        using var cameraLock = await AcquireCameraLockAsync(cameraKey, cancellationToken);
        var entry = await GetOrCreateCameraEntryUnderLockAsync(cameraKey, manufacturerHint, cancellationToken);
        return entry.GetActiveCamera();
    }

    public async Task<ICameraLease> AcquireCameraLeaseAsync(
        string cameraId,
        CancellationToken cancellationToken = default)
    {
        return await AcquireByBindingLeaseAsync(cameraId, cancellationToken);
    }

    /// <summary>
    /// 根据绑定ID获取相机
    /// </summary>
    public async Task<ICamera> GetOrCreateByBindingAsync(string bindingId)
    {
        var resolved = ResolveBinding(bindingId);
        return await GetOrCreateCameraBySerialAsync(
            resolved.CameraId,
            resolved.ManufacturerHint,
            CancellationToken.None);
    }

    public async Task<ICameraLease> AcquireByBindingLeaseAsync(
        string bindingId,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var resolved = ResolveBinding(bindingId);
        var cameraKey = NormalizeCameraKey(resolved.CameraId);
        using var cameraLock = await AcquireCameraLockAsync(cameraKey, cancellationToken);
        var entry = await GetOrCreateCameraEntryUnderLockAsync(
            cameraKey,
            resolved.ManufacturerHint,
            cancellationToken);
        return entry.AcquireLease();
    }

    public Task<ICamera> OpenCameraAsync(string cameraId) => GetOrCreateByBindingAsync(cameraId);

    public async Task CloseCameraAsync(string cameraId)
    {
        ThrowIfDisposed();
        var resolved = ResolveBinding(cameraId);
        await CloseCameraBySerialAsync(resolved.CameraId);
    }

    private async Task CloseCameraBySerialAsync(string serialNumber)
    {
        var cameraKey = NormalizeCameraKey(serialNumber);
        using var cameraLock = await AcquireCameraLockAsync(cameraKey, CancellationToken.None);
        if (_cameras.TryGetValue(cameraKey, out var entry))
        {
            entry.Retire();
        }
    }

    public ICamera? GetCamera(string cameraId)
    {
        if (string.IsNullOrWhiteSpace(cameraId))
        {
            return null;
        }

        return _cameras.TryGetValue(cameraId.Trim(), out var entry)
            ? entry.TryGetActiveCamera()
            : null;
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
        foreach (var entry in _cameras.Values)
        {
            entry.Retire();
        }
    }

    // --- 相机绑定管理功能 ---

    public void LoadBindings(List<CameraBindingConfig> bindings, string activeCameraId)
    {
        var normalized = NormalizeBindings(bindings);
        lock (_bindingsSync)
        {
            _bindings = normalized;
            _activeCameraId = activeCameraId ?? "";
        }

        _logger.LogDebug("[CameraManager] 已加载 {Count} 个相机绑定", normalized.Count);
    }

    public List<CameraBindingConfig> GetBindings()
    {
        lock (_bindingsSync)
        {
            return NormalizeBindings(_bindings);
        }
    }

    public void UpdateBindings(List<CameraBindingConfig> bindings, string activeCameraId)
    {
        var normalized = NormalizeBindings(bindings);
        lock (_bindingsSync)
        {
            _bindings = normalized;
            _activeCameraId = activeCameraId ?? "";
        }

        _logger.LogDebug("[CameraManager] 已更新绑定，活动相机: {ActiveCameraId}", _activeCameraId);
    }

    public async Task ApplyBindingsAsync(List<CameraBindingConfig> bindings, string activeCameraId)
    {
        ThrowIfDisposed();
        var normalized = NormalizeBindings(bindings);
        HashSet<string> previousSerialNumbers;
        lock (_bindingsSync)
        {
            previousSerialNumbers = _bindings
                .Select(binding => binding.SerialNumber?.Trim() ?? string.Empty)
                .Where(serialNumber => !string.IsNullOrWhiteSpace(serialNumber))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        var retainedSerialNumbers = normalized
            .Select(binding => binding.SerialNumber?.Trim() ?? string.Empty)
            .Where(serialNumber => !string.IsNullOrWhiteSpace(serialNumber))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var retiredSerialNumbers = previousSerialNumbers
            .Where(serialNumber => !retainedSerialNumbers.Contains(serialNumber))
            .ToArray();

        foreach (var serialNumber in retiredSerialNumbers)
        {
            if (GetCamera(serialNumber) is { IsAcquiring: true } camera)
            {
                await camera.StopContinuousAcquisitionAsync();
            }

            await CloseCameraBySerialAsync(serialNumber);
        }

        lock (_bindingsSync)
        {
            _bindings = normalized;
            _activeCameraId = activeCameraId ?? string.Empty;
        }

        _logger.LogInformation(
            "[CameraManager] Applied {BindingCount} bindings and retired {RetiredCount} providers. ActiveCameraId={ActiveCameraId}",
            normalized.Count,
            retiredSerialNumbers.Length,
            _activeCameraId);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            DisconnectAllCore();
        }

        GC.SuppressFinalize(this);
    }

    private async Task<ManagedCameraEntry> GetOrCreateCameraEntryUnderLockAsync(
        string cameraKey,
        string? manufacturerHint,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        while (_cameras.TryGetValue(cameraKey, out var observedEntry))
        {
            if (observedEntry.TryGetActiveCamera() != null)
            {
                return observedEntry;
            }

            await observedEntry.DisposalCompleted.WaitAsync(cancellationToken);
            TryRemoveCameraEntry(cameraKey, observedEntry);
            ThrowIfDisposed();
        }

        // AutoDetect internally opens the camera and returns a connected provider.
        var provider = _providerFactory(cameraKey, manufacturerHint);
        if (provider == null)
        {
            throw new InvalidOperationException(
                $"Failed to detect camera: {cameraKey}. Check power, connection, and SDK installation.");
        }

        var cameraAdapter = new CameraProviderAdapter(
            cameraKey,
            provider,
            _loggerFactory.CreateLogger<CameraProviderAdapter>());
        var createdEntry = new ManagedCameraEntry(
            cameraAdapter,
            disposedEntry => TryRemoveCameraEntry(cameraKey, disposedEntry));

        if (!_cameras.TryAdd(cameraKey, createdEntry))
        {
            createdEntry.Retire();
            throw new InvalidOperationException($"Camera '{cameraKey}' was opened concurrently through an uncoordinated path.");
        }

        if (Volatile.Read(ref _disposed) != 0)
        {
            createdEntry.Retire();
            ThrowIfDisposed();
        }

        return createdEntry;
    }

    private (string CameraId, string? ManufacturerHint) ResolveBinding(string bindingId)
    {
        var bindingKey = NormalizeCameraKey(bindingId);
        CameraBindingConfig? binding;
        lock (_bindingsSync)
        {
            binding = _bindings.FirstOrDefault(b =>
                b.Id.Equals(bindingKey, StringComparison.OrdinalIgnoreCase));
        }

        if (binding == null)
        {
            throw new InvalidOperationException($"Camera binding '{bindingKey}' is not configured.");
        }

        if (!binding.IsEnabled)
        {
            throw new InvalidOperationException($"Camera binding '{bindingKey}' is disabled.");
        }

        if (string.IsNullOrWhiteSpace(binding.SerialNumber))
        {
            throw new InvalidOperationException($"绑定 '{binding.DisplayName}' 未关联物理设备序列号");
        }

        return (binding.SerialNumber.Trim(), binding.Manufacturer);
    }

    private async ValueTask<CameraLockLease> AcquireCameraLockAsync(
        string cameraKey,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            var entry = _cameraLocks.GetOrAdd(cameraKey, static _ => new CameraLockEntry());
            if (!entry.TryAddReference())
            {
                TryRemoveCameraLock(cameraKey, entry);
                continue;
            }

            try
            {
                await entry.Semaphore.WaitAsync(cancellationToken);
                return new CameraLockLease(this, cameraKey, entry);
            }
            catch
            {
                ReleaseCameraLockReference(cameraKey, entry);
                throw;
            }
        }
    }

    private void ReleaseCameraLock(string cameraKey, CameraLockEntry entry)
    {
        entry.Semaphore.Release();
        ReleaseCameraLockReference(cameraKey, entry);
    }

    private void ReleaseCameraLockReference(string cameraKey, CameraLockEntry entry)
    {
        if (!entry.ReleaseReference())
        {
            return;
        }

        TryRemoveCameraLock(cameraKey, entry);
        entry.Dispose();
    }

    private bool TryRemoveCameraLock(string cameraKey, CameraLockEntry entry) =>
        ((ICollection<KeyValuePair<string, CameraLockEntry>>)_cameraLocks)
        .Remove(new KeyValuePair<string, CameraLockEntry>(cameraKey, entry));

    private bool TryRemoveCameraEntry(string cameraKey, ManagedCameraEntry entry) =>
        ((ICollection<KeyValuePair<string, ManagedCameraEntry>>)_cameras)
        .Remove(new KeyValuePair<string, ManagedCameraEntry>(cameraKey, entry));

    private static string NormalizeCameraKey(string cameraId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cameraId);
        return cameraId.Trim();
    }

    private static List<CameraBindingConfig> NormalizeBindings(IEnumerable<CameraBindingConfig>? bindings)
    {
        return (bindings ?? Enumerable.Empty<CameraBindingConfig>())
            .Select(binding =>
            {
                var clone = new CameraBindingConfig
                {
                    Id = binding.Id,
                    DisplayName = binding.DisplayName,
                    SerialNumber = binding.SerialNumber,
                    IpAddress = binding.IpAddress,
                    Manufacturer = binding.Manufacturer,
                    ModelName = binding.ModelName,
                    InterfaceType = binding.InterfaceType,
                    IsEnabled = binding.IsEnabled,
                    ExposureTimeUs = binding.ExposureTimeUs,
                    GainDb = binding.GainDb,
                    PixelFormat = binding.PixelFormat,
                    TriggerMode = binding.TriggerMode,
                    HardwareTriggerSource = binding.HardwareTriggerSource,
                    SoftwareTriggerSource = binding.SoftwareTriggerSource,
                    EnterPhotoelectricDebounceMs = binding.EnterPhotoelectricDebounceMs,
                    EnterPhotoelectricTimeoutMs = binding.EnterPhotoelectricTimeoutMs,
                    IgnoreEnterTriggerWhileBusy = binding.IgnoreEnterTriggerWhileBusy,
                    EnterPhotoelectricDeviceId = binding.EnterPhotoelectricDeviceId,
                    SerialPhotoelectricPortName = binding.SerialPhotoelectricPortName,
                    SerialPhotoelectricBaudRate = binding.SerialPhotoelectricBaudRate,
                    SerialPhotoelectricDebounceMs = binding.SerialPhotoelectricDebounceMs,
                    SerialPhotoelectricTimeoutMs = binding.SerialPhotoelectricTimeoutMs,
                    IgnoreSerialPhotoelectricTriggerWhileBusy = binding.IgnoreSerialPhotoelectricTriggerWhileBusy,
                    TargetFrameRateFps = binding.TargetFrameRateFps,
                    ContinuousInspection = System.Text.Json.JsonSerializer.Deserialize<ClearVision.Product.Core.Continuous.ContinuousInspectionConfig>(
                        System.Text.Json.JsonSerializer.Serialize(binding.ContinuousInspection)) ?? new ClearVision.Product.Core.Continuous.ContinuousInspectionConfig()
                };
                clone.Normalize();
                return clone;
            })
            .ToList();
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
    }

    private sealed class CameraLockEntry : IDisposable
    {
        private readonly object _sync = new();
        private int _references;
        private bool _retired;

        public SemaphoreSlim Semaphore { get; } = new(1, 1);

        public int ReferenceCount
        {
            get
            {
                lock (_sync)
                {
                    return _references;
                }
            }
        }

        public bool TryAddReference()
        {
            lock (_sync)
            {
                if (_retired)
                {
                    return false;
                }

                _references++;
                return true;
            }
        }

        public bool ReleaseReference()
        {
            lock (_sync)
            {
                if (_references <= 0)
                {
                    throw new InvalidOperationException("Camera lock reference count underflow.");
                }

                _references--;
                if (_references != 0)
                {
                    return false;
                }

                _retired = true;
                return true;
            }
        }

        public void Dispose() => Semaphore.Dispose();
    }

    private sealed class CameraLockLease : IDisposable
    {
        private readonly CameraManager _owner;
        private readonly string _cameraKey;
        private CameraLockEntry? _entry;

        public CameraLockLease(CameraManager owner, string cameraKey, CameraLockEntry entry)
        {
            _owner = owner;
            _cameraKey = cameraKey;
            _entry = entry;
        }

        public void Dispose()
        {
            var entry = Interlocked.Exchange(ref _entry, null);
            if (entry != null)
            {
                _owner.ReleaseCameraLock(_cameraKey, entry);
            }
        }
    }

    private sealed class ManagedCameraEntry
    {
        private readonly object _sync = new();
        private readonly Action<ManagedCameraEntry> _onDisposed;
        private readonly TaskCompletionSource _disposalCompleted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _leaseCount;
        private bool _retired;
        private bool _disposeStarted;

        public ManagedCameraEntry(ICamera camera, Action<ManagedCameraEntry> onDisposed)
        {
            Camera = camera;
            _onDisposed = onDisposed;
        }

        public ICamera Camera { get; }

        public Task DisposalCompleted => _disposalCompleted.Task;

        public ICamera GetActiveCamera() =>
            TryGetActiveCamera()
            ?? throw new InvalidOperationException($"Camera '{Camera.CameraId}' is retiring.");

        public ICamera? TryGetActiveCamera()
        {
            lock (_sync)
            {
                return _retired ? null : Camera;
            }
        }

        public ICameraLease AcquireLease()
        {
            lock (_sync)
            {
                if (_retired)
                {
                    throw new InvalidOperationException($"Camera '{Camera.CameraId}' is retiring.");
                }

                _leaseCount++;
                return new ManagedCameraLease(this, Camera);
            }
        }

        public void Retire()
        {
            var disposeNow = false;
            lock (_sync)
            {
                if (_retired)
                {
                    return;
                }

                _retired = true;
                if (_leaseCount == 0)
                {
                    _disposeStarted = true;
                    disposeNow = true;
                }
            }

            if (disposeNow)
            {
                DisposeCamera();
            }
        }

        private void ReleaseLease()
        {
            var disposeNow = false;
            lock (_sync)
            {
                if (_leaseCount <= 0)
                {
                    throw new InvalidOperationException("Camera lease count underflow.");
                }

                _leaseCount--;
                if (_retired && _leaseCount == 0 && !_disposeStarted)
                {
                    _disposeStarted = true;
                    disposeNow = true;
                }
            }

            if (disposeNow)
            {
                DisposeCamera();
            }
        }

        private void DisposeCamera()
        {
            try
            {
                Camera.Dispose();
            }
            finally
            {
                _onDisposed(this);
                _disposalCompleted.TrySetResult();
            }
        }

        private sealed class ManagedCameraLease : ICameraLease
        {
            private ManagedCameraEntry? _owner;

            public ManagedCameraLease(ManagedCameraEntry owner, ICamera camera)
            {
                _owner = owner;
                Camera = camera;
            }

            public ICamera Camera { get; }

            public void Dispose()
            {
                Interlocked.Exchange(ref _owner, null)?.ReleaseLease();
            }

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
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
    private int _disposed;

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
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            StopContinuousAcquisitionAsync().GetAwaiter().GetResult();
        }
        finally
        {
            _provider.Dispose();
            _providerGate.Dispose();
        }
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
