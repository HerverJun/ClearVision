using System.Runtime.InteropServices;
using System.Text;
using Acme.Product.Core.Cameras;
using Microsoft.Extensions.Logging;

namespace Acme.Product.Desktop.Triggers;

public sealed class EnterPhotoelectricTriggerInputService : ITriggerInputService, IDisposable
{
    public const int WmInput = 0x00FF;

    private static readonly TimeSpan PendingSignalTtl = TimeSpan.FromSeconds(10);
    private const int MaxPendingSignals = 32;

    private const int RimTypeKeyboard = 1;
    private const uint RidInput = 0x10000003;
    private const uint RidiDeviceName = 0x20000007;
    private const int RidevInputSink = 0x00000100;
    private const int WmKeyDown = 0x0100;
    private const int WmSysKeyDown = 0x0104;
    private const ushort VkReturn = 0x0D;

    private readonly ILogger<EnterPhotoelectricTriggerInputService> _logger;
    private readonly object _sync = new();
    private readonly List<TriggerWaiter> _waiters = new();
    private readonly List<PendingSignal> _pendingSignals = new();
    private readonly Dictionary<IntPtr, string> _deviceNames = new();
    private readonly Dictionary<string, DateTime> _lastAcceptedUtcByBinding = new(StringComparer.OrdinalIgnoreCase);
    private TaskCompletionSource<TriggerDeviceLearnResult>? _learnRequest;
    private IntPtr _attachedWindow;
    private bool _registered;
    private string? _lastDeviceId;
    private DateTime? _lastSignalUtc;
    private string? _lastError;

    public EnterPhotoelectricTriggerInputService(ILogger<EnterPhotoelectricTriggerInputService> logger)
    {
        _logger = logger;
    }

    public bool IsAvailable => _registered;

    public void AttachWindow(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero)
        {
            return;
        }

        lock (_sync)
        {
            if (_registered && _attachedWindow == windowHandle)
            {
                return;
            }
        }

        var devices = new[]
        {
            new RawInputDevice
            {
                UsagePage = 0x01,
                Usage = 0x06,
                Flags = RidevInputSink,
                Target = windowHandle
            }
        };

        if (!RegisterRawInputDevices(devices, (uint)devices.Length, (uint)Marshal.SizeOf<RawInputDevice>()))
        {
            var error = Marshal.GetLastWin32Error();
            lock (_sync)
            {
                _registered = false;
                _lastError = $"RegisterRawInputDevices failed: {error}";
            }

            _logger.LogWarning("Failed to register raw keyboard input. Win32Error={Error}", error);
            return;
        }

        lock (_sync)
        {
            _attachedWindow = windowHandle;
            _registered = true;
            _lastError = null;
        }

        _logger.LogInformation("Enter photoelectric trigger listener attached. WindowHandle={WindowHandle}", windowHandle);
    }

    public void HandleWindowMessage(int message, IntPtr lParam)
    {
        if (message != WmInput || lParam == IntPtr.Zero)
        {
            return;
        }

        if (!TryReadKeyboardInput(lParam, out var deviceHandle, out var virtualKey, out var keyMessage))
        {
            return;
        }

        if (virtualKey != VkReturn || keyMessage is not (WmKeyDown or WmSysKeyDown))
        {
            return;
        }

        var deviceId = ResolveDeviceName(deviceHandle);
        PublishEnterSignal(deviceId);
    }

    public async Task<TriggerInputEvent> WaitForEnterPhotoelectricAsync(
        EnterPhotoelectricTriggerOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();

        var timeoutMs = CameraSoftwareTriggerSourceExtensions.NormalizeEnterPhotoelectricTimeoutMs(options.TimeoutMs);
        var normalizedOptions = options with
        {
            CameraBindingId = string.IsNullOrWhiteSpace(options.CameraBindingId) ? "camera" : options.CameraBindingId.Trim(),
            DeviceId = options.DeviceId?.Trim() ?? string.Empty,
            DebounceMs = CameraSoftwareTriggerSourceExtensions.NormalizeEnterPhotoelectricDebounceMs(options.DebounceMs),
            TimeoutMs = timeoutMs
        };

        var waiter = new TriggerWaiter(normalizedOptions);
        lock (_sync)
        {
            PrunePendingSignals(DateTime.UtcNow);
            if (!normalizedOptions.IgnoreWhileBusy &&
                TryTakePendingSignal(normalizedOptions, out var pendingEvent))
            {
                return pendingEvent;
            }

            _waiters.Add(waiter);
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeoutMs);

        try
        {
            return await waiter.Completion.Task.WaitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"等待回车光电触发超时: {timeoutMs}ms");
        }
        finally
        {
            lock (_sync)
            {
                _waiters.Remove(waiter);
            }
        }
    }

    public async Task<TriggerDeviceLearnResult> LearnEnterPhotoelectricDeviceAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var effectiveTimeout = timeout <= TimeSpan.Zero ? TimeSpan.FromSeconds(10) : timeout;
        var request = new TaskCompletionSource<TriggerDeviceLearnResult>(TaskCreationOptions.RunContinuationsAsynchronously);

        lock (_sync)
        {
            if (_learnRequest != null)
            {
                throw new InvalidOperationException("已有设备学习请求正在等待回车光电触发。");
            }

            _learnRequest = request;
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(effectiveTimeout);

        try
        {
            return await request.Task.WaitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"学习回车光电设备超时: {(int)effectiveTimeout.TotalMilliseconds}ms");
        }
        finally
        {
            lock (_sync)
            {
                if (ReferenceEquals(_learnRequest, request))
                {
                    _learnRequest = null;
                }
            }
        }
    }

    public TriggerInputDiagnostics GetDiagnostics()
    {
        lock (_sync)
        {
            return new TriggerInputDiagnostics(
                _registered,
                "WindowsRawInput",
                _waiters.Count,
                _attachedWindow == IntPtr.Zero ? null : _attachedWindow.ToString("X"),
                _lastDeviceId,
                _lastSignalUtc,
                _lastError);
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            foreach (var waiter in _waiters.ToArray())
            {
                waiter.Completion.TrySetCanceled();
            }

            _waiters.Clear();
            _pendingSignals.Clear();
            _learnRequest?.TrySetCanceled();
            _learnRequest = null;
        }
    }

    private void PublishEnterSignal(string deviceId)
    {
        var nowUtc = DateTime.UtcNow;
        List<TriggerWaiter> completedWaiters = new();
        TaskCompletionSource<TriggerDeviceLearnResult>? learnRequest = null;
        var matchedWaiterRejectedByDebounce = false;
        var learnedThisSignal = false;

        lock (_sync)
        {
            _lastDeviceId = deviceId;
            _lastSignalUtc = nowUtc;
            PrunePendingSignals(nowUtc);

            if (_learnRequest != null)
            {
                learnRequest = _learnRequest;
                _learnRequest = null;
                learnedThisSignal = true;
            }

            foreach (var waiter in _waiters.ToArray())
            {
                if (!MatchesDevice(waiter.Options.DeviceId, deviceId))
                {
                    continue;
                }

                if (!TryPassDebounce(waiter.Options.CameraBindingId, waiter.Options.DebounceMs, nowUtc))
                {
                    matchedWaiterRejectedByDebounce = true;
                    continue;
                }

                _waiters.Remove(waiter);
                completedWaiters.Add(waiter);
            }

            if (completedWaiters.Count == 0 && !matchedWaiterRejectedByDebounce && !learnedThisSignal)
            {
                _pendingSignals.Add(new PendingSignal(deviceId, nowUtc));
                if (_pendingSignals.Count > MaxPendingSignals)
                {
                    _pendingSignals.RemoveRange(0, _pendingSignals.Count - MaxPendingSignals);
                }
            }
        }

        learnRequest?.TrySetResult(new TriggerDeviceLearnResult(deviceId, nowUtc));
        foreach (var waiter in completedWaiters)
        {
            waiter.Completion.TrySetResult(new TriggerInputEvent(
                "EnterPhotoelectric",
                waiter.Options.CameraBindingId,
                deviceId,
                nowUtc));
        }
    }

    private bool TryPassDebounce(string bindingId, int debounceMs, DateTime nowUtc)
    {
        if (debounceMs <= 0)
        {
            _lastAcceptedUtcByBinding[bindingId] = nowUtc;
            return true;
        }

        if (_lastAcceptedUtcByBinding.TryGetValue(bindingId, out var lastAccepted) &&
            (nowUtc - lastAccepted).TotalMilliseconds < debounceMs)
        {
            return false;
        }

        _lastAcceptedUtcByBinding[bindingId] = nowUtc;
        return true;
    }

    private bool TryTakePendingSignal(
        EnterPhotoelectricTriggerOptions options,
        out TriggerInputEvent triggerEvent)
    {
        triggerEvent = default!;
        for (var index = 0; index < _pendingSignals.Count;)
        {
            var pending = _pendingSignals[index];
            if (!MatchesDevice(options.DeviceId, pending.DeviceId))
            {
                index++;
                continue;
            }

            if (!TryPassDebounce(options.CameraBindingId, options.DebounceMs, pending.TimestampUtc))
            {
                _pendingSignals.RemoveAt(index);
                continue;
            }

            _pendingSignals.RemoveAt(index);
            triggerEvent = new TriggerInputEvent(
                "EnterPhotoelectric",
                options.CameraBindingId,
                pending.DeviceId,
                pending.TimestampUtc);
            return true;
        }

        return false;
    }

    private void PrunePendingSignals(DateTime nowUtc)
    {
        _pendingSignals.RemoveAll(signal => nowUtc - signal.TimestampUtc > PendingSignalTtl);
    }

    private static bool MatchesDevice(string configuredDeviceId, string actualDeviceId)
    {
        if (string.IsNullOrWhiteSpace(configuredDeviceId))
        {
            return true;
        }

        return string.Equals(configuredDeviceId.Trim(), actualDeviceId, StringComparison.OrdinalIgnoreCase);
    }

    private bool TryReadKeyboardInput(
        IntPtr rawInputHandle,
        out IntPtr deviceHandle,
        out ushort virtualKey,
        out uint keyMessage)
    {
        deviceHandle = IntPtr.Zero;
        virtualKey = 0;
        keyMessage = 0;

        var headerSize = (uint)Marshal.SizeOf<RawInputHeader>();
        uint size = 0;
        var result = GetRawInputData(rawInputHandle, RidInput, IntPtr.Zero, ref size, headerSize);
        if (result == uint.MaxValue || size == 0)
        {
            return false;
        }

        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            result = GetRawInputData(rawInputHandle, RidInput, buffer, ref size, headerSize);
            if (result == uint.MaxValue || result != size)
            {
                return false;
            }

            var input = Marshal.PtrToStructure<RawInput>(buffer);
            if (input.Header.Type != RimTypeKeyboard)
            {
                return false;
            }

            deviceHandle = input.Header.Device;
            virtualKey = input.Keyboard.VKey;
            keyMessage = input.Keyboard.Message;
            return true;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private string ResolveDeviceName(IntPtr deviceHandle)
    {
        if (deviceHandle == IntPtr.Zero)
        {
            return string.Empty;
        }

        lock (_sync)
        {
            if (_deviceNames.TryGetValue(deviceHandle, out var cached))
            {
                return cached;
            }
        }

        uint size = 0;
        var result = GetRawInputDeviceInfo(deviceHandle, RidiDeviceName, null, ref size);
        if (result == uint.MaxValue || size == 0)
        {
            return deviceHandle.ToString("X");
        }

        var builder = new StringBuilder((int)size);
        result = GetRawInputDeviceInfo(deviceHandle, RidiDeviceName, builder, ref size);
        var name = result == uint.MaxValue
            ? deviceHandle.ToString("X")
            : builder.ToString();

        lock (_sync)
        {
            _deviceNames[deviceHandle] = name;
        }

        return name;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterRawInputDevices(
        RawInputDevice[] rawInputDevices,
        uint numDevices,
        uint size);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetRawInputData(
        IntPtr rawInput,
        uint command,
        IntPtr data,
        ref uint size,
        uint headerSize);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern uint GetRawInputDeviceInfo(
        IntPtr device,
        uint command,
        StringBuilder? data,
        ref uint size);

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputDevice
    {
        public ushort UsagePage;
        public ushort Usage;
        public int Flags;
        public IntPtr Target;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputHeader
    {
        public int Type;
        public int Size;
        public IntPtr Device;
        public IntPtr WParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawKeyboard
    {
        public ushort MakeCode;
        public ushort Flags;
        public ushort Reserved;
        public ushort VKey;
        public uint Message;
        public uint ExtraInformation;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInput
    {
        public RawInputHeader Header;
        public RawKeyboard Keyboard;
    }

    private sealed class TriggerWaiter
    {
        public TriggerWaiter(EnterPhotoelectricTriggerOptions options)
        {
            Options = options;
        }

        public EnterPhotoelectricTriggerOptions Options { get; }

        public TaskCompletionSource<TriggerInputEvent> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed record PendingSignal(string DeviceId, DateTime TimestampUtc);
}
