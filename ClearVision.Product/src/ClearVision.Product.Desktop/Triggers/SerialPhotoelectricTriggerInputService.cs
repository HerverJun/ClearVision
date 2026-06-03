using System.IO.Ports;
using ClearVision.Product.Core.Cameras;
using ClearVision.Product.Core.Entities;
using Microsoft.Extensions.Logging;

namespace ClearVision.Product.Desktop.Triggers;

public sealed class SerialPhotoelectricTriggerInputService : ISerialPhotoelectricTriggerInputService, IDisposable
{
    private static readonly TimeSpan PendingSignalTtl = TimeSpan.FromSeconds(10);
    private const int MaxPendingSignals = 32;

    private readonly ILogger<SerialPhotoelectricTriggerInputService> _logger;
    private readonly object _sync = new();
    private readonly List<TriggerWaiter> _waiters = new();
    private readonly List<PendingSignal> _pendingSignals = new();
    private readonly Dictionary<string, SerialPhotoelectricPortListener> _listeners = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTime> _lastAcceptedUtcByBinding = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;
    private string? _lastPortName;
    private DateTime? _lastSignalUtc;

    public SerialPhotoelectricTriggerInputService(ILogger<SerialPhotoelectricTriggerInputService> logger)
    {
        _logger = logger;
    }

    public bool IsAvailable
    {
        get
        {
            lock (_sync)
            {
                return _listeners.Values.Any(listener => listener.IsRunning);
            }
        }
    }

    public void ConfigureBindings(IEnumerable<CameraBindingConfig>? bindings)
    {
        var desired = new Dictionary<string, SerialPhotoelectricConnectionOptions>(StringComparer.OrdinalIgnoreCase);
        foreach (var binding in bindings ?? Enumerable.Empty<CameraBindingConfig>())
        {
            if (binding?.UsesSerialPhotoelectricTrigger() != true ||
                !TryCreateConnectionOptions(
                    binding.SerialPhotoelectricPortName,
                    binding.SerialPhotoelectricBaudRate,
                    out var options))
            {
                continue;
            }

            desired[options.PortName] = options;
        }

        foreach (var options in desired.Values)
        {
            EnsureListener(options);
        }

        List<SerialPhotoelectricPortListener> staleListeners = new();
        lock (_sync)
        {
            foreach (var (portName, listener) in _listeners.ToArray())
            {
                if (desired.ContainsKey(portName))
                {
                    continue;
                }

                _listeners.Remove(portName);
                staleListeners.Add(listener);
            }
        }

        foreach (var listener in staleListeners)
        {
            listener.Dispose();
        }
    }

    public async Task<TriggerInputEvent> WaitForSerialPhotoelectricAsync(
        SerialPhotoelectricTriggerOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryCreateConnectionOptions(options.PortName, options.BaudRate, out var serialOptions))
        {
            throw new InvalidOperationException("串口光电触发需要配置有效串口号，例如 COM3。");
        }

        var timeoutMs = CameraSoftwareTriggerSourceExtensions.NormalizeSerialPhotoelectricTimeoutMs(options.TimeoutMs);
        var normalizedOptions = options with
        {
            CameraBindingId = string.IsNullOrWhiteSpace(options.CameraBindingId) ? "camera" : options.CameraBindingId.Trim(),
            PortName = serialOptions.PortName,
            BaudRate = serialOptions.BaudRate,
            DebounceMs = CameraSoftwareTriggerSourceExtensions.NormalizeSerialPhotoelectricDebounceMs(options.DebounceMs),
            TimeoutMs = timeoutMs,
            AcceptPendingSignalsAfterUtc = NormalizeAcceptPendingSignalsAfterUtc(options.AcceptPendingSignalsAfterUtc)
        };

        var waiter = new TriggerWaiter(normalizedOptions);
        lock (_sync)
        {
            PrunePendingSignals(DateTime.UtcNow);
            if ((!normalizedOptions.IgnoreWhileBusy || normalizedOptions.AcceptPendingSignalsAfterUtc.HasValue) &&
                TryTakePendingSignal(normalizedOptions, out var pendingEvent))
            {
                return pendingEvent;
            }

            _waiters.Add(waiter);
        }

        EnsureListener(serialOptions);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeoutMs);

        try
        {
            return await waiter.Completion.Task.WaitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"等待串口光电触发超时: {timeoutMs}ms");
        }
        finally
        {
            lock (_sync)
            {
                _waiters.Remove(waiter);
            }
        }
    }

    public TriggerInputDiagnostics GetDiagnostics()
    {
        lock (_sync)
        {
            var ports = _listeners.Keys.OrderBy(port => port, StringComparer.OrdinalIgnoreCase).ToArray();
            var errors = _listeners.Values
                .Select(listener => listener.LastError)
                .Where(error => !string.IsNullOrWhiteSpace(error))
                .ToArray();

            return new TriggerInputDiagnostics(
                _listeners.Values.Any(listener => listener.IsRunning),
                ports.Length == 0 ? "SerialPhotoelectric" : $"SerialPhotoelectric({string.Join(",", ports)})",
                _waiters.Count,
                null,
                _lastPortName,
                _lastSignalUtc,
                errors.Length == 0 ? null : string.Join("; ", errors));
        }
    }

    public void Dispose()
    {
        List<SerialPhotoelectricPortListener> listeners;
        lock (_sync)
        {
            _disposed = true;
            foreach (var waiter in _waiters.ToArray())
            {
                waiter.Completion.TrySetCanceled();
            }

            _waiters.Clear();
            _pendingSignals.Clear();
            listeners = _listeners.Values.ToList();
            _listeners.Clear();
        }

        foreach (var listener in listeners)
        {
            listener.Dispose();
        }
    }

    private void EnsureListener(SerialPhotoelectricConnectionOptions options)
    {
        SerialPhotoelectricPortListener listener;
        SerialPhotoelectricPortListener? staleListener = null;

        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            if (_listeners.TryGetValue(options.PortName, out var existing) &&
                existing.Matches(options))
            {
                listener = existing;
            }
            else
            {
                staleListener = existing;
                listener = new SerialPhotoelectricPortListener(
                    options,
                    PublishBlockSignal,
                    _logger);
                _listeners[options.PortName] = listener;
            }
        }

        staleListener?.Dispose();
        listener.Start();
    }

    private void PublishBlockSignal(string portName)
    {
        var nowUtc = DateTime.UtcNow;
        List<TriggerWaiter> completedWaiters = new();
        var matchedWaiterRejectedByDebounce = false;

        lock (_sync)
        {
            _lastPortName = portName;
            _lastSignalUtc = nowUtc;
            PrunePendingSignals(nowUtc);

            foreach (var waiter in _waiters.ToArray())
            {
                if (!MatchesPort(waiter.Options.PortName, portName))
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

            if (completedWaiters.Count == 0 && !matchedWaiterRejectedByDebounce)
            {
                _pendingSignals.Add(new PendingSignal(portName, nowUtc));
                if (_pendingSignals.Count > MaxPendingSignals)
                {
                    _pendingSignals.RemoveRange(0, _pendingSignals.Count - MaxPendingSignals);
                }
            }
        }

        foreach (var waiter in completedWaiters)
        {
            waiter.Completion.TrySetResult(new TriggerInputEvent(
                "SerialPhotoelectric",
                waiter.Options.CameraBindingId,
                portName,
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
        SerialPhotoelectricTriggerOptions options,
        out TriggerInputEvent triggerEvent)
    {
        triggerEvent = default!;
        for (var index = 0; index < _pendingSignals.Count;)
        {
            var pending = _pendingSignals[index];
            if (!MatchesPort(options.PortName, pending.PortName))
            {
                index++;
                continue;
            }

            if (options.AcceptPendingSignalsAfterUtc is { } acceptAfterUtc &&
                pending.TimestampUtc < acceptAfterUtc)
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
                "SerialPhotoelectric",
                options.CameraBindingId,
                pending.PortName,
                pending.TimestampUtc);
            return true;
        }

        return false;
    }

    private static DateTime? NormalizeAcceptPendingSignalsAfterUtc(DateTime? value)
    {
        if (!value.HasValue)
        {
            return null;
        }

        return value.Value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc)
            : value.Value.ToUniversalTime();
    }

    private void PrunePendingSignals(DateTime nowUtc)
    {
        _pendingSignals.RemoveAll(signal => nowUtc - signal.TimestampUtc > PendingSignalTtl);
    }

    private static bool MatchesPort(string configuredPortName, string actualPortName) =>
        string.Equals(
            NormalizePortName(configuredPortName),
            NormalizePortName(actualPortName),
            StringComparison.OrdinalIgnoreCase);

    private static bool TryCreateConnectionOptions(
        string? portName,
        int baudRate,
        out SerialPhotoelectricConnectionOptions options)
    {
        options = default!;
        var normalizedPortName = NormalizePortName(portName);
        if (!IsWindowsComPortName(normalizedPortName))
        {
            return false;
        }

        options = new SerialPhotoelectricConnectionOptions(
            normalizedPortName,
            CameraSoftwareTriggerSourceExtensions.NormalizeSerialPhotoelectricBaudRate(baudRate));
        return true;
    }

    private static string NormalizePortName(string? value) =>
        (value ?? string.Empty).Trim().ToUpperInvariant();

    private static bool IsWindowsComPortName(string value)
    {
        if (value.Length <= 3 || !value.StartsWith("COM", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        for (var index = 3; index < value.Length; index++)
        {
            if (!char.IsDigit(value[index]))
            {
                return false;
            }
        }

        return true;
    }

    private sealed record SerialPhotoelectricConnectionOptions(string PortName, int BaudRate);

    private sealed record PendingSignal(string PortName, DateTime TimestampUtc);

    private sealed class TriggerWaiter
    {
        public TriggerWaiter(SerialPhotoelectricTriggerOptions options)
        {
            Options = options;
        }

        public SerialPhotoelectricTriggerOptions Options { get; }

        public TaskCompletionSource<TriggerInputEvent> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class SerialPhotoelectricPortListener : IDisposable
    {
        private static readonly TimeSpan IdleDelay = TimeSpan.FromMilliseconds(10);
        private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(1);
        private const int ReadTimeoutMs = 100;
        private const int WriteTimeoutMs = 1000;

        private readonly SerialPhotoelectricConnectionOptions _options;
        private readonly Action<string> _publishBlockSignal;
        private readonly ILogger _logger;
        private readonly object _sync = new();
        private readonly List<byte> _buffer = new(capacity: 8);
        private CancellationTokenSource? _cts;
        private Task? _readTask;
        private SerialPort? _serialPort;
        private bool _isRunning;
        private bool _disposed;
        private bool _isBlocked;
        private string? _lastError;

        public SerialPhotoelectricPortListener(
            SerialPhotoelectricConnectionOptions options,
            Action<string> publishBlockSignal,
            ILogger logger)
        {
            _options = options;
            _publishBlockSignal = publishBlockSignal;
            _logger = logger;
        }

        public string? LastError
        {
            get
            {
                lock (_sync)
                {
                    return _lastError;
                }
            }
        }

        public bool IsRunning
        {
            get
            {
                lock (_sync)
                {
                    return _isRunning;
                }
            }
        }

        public bool Matches(SerialPhotoelectricConnectionOptions options) =>
            string.Equals(_options.PortName, options.PortName, StringComparison.OrdinalIgnoreCase) &&
            _options.BaudRate == options.BaudRate;

        public void Start()
        {
            lock (_sync)
            {
                if (_disposed || _readTask is { IsCompleted: false })
                {
                    return;
                }

                _cts = new CancellationTokenSource();
                _readTask = Task.Run(() => ReadLoopAsync(_cts.Token));
            }
        }

        public void Dispose()
        {
            CancellationTokenSource? cts;
            SerialPort? serialPort;
            lock (_sync)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                cts = _cts;
                _cts = null;
                serialPort = _serialPort;
            }

            try
            {
                cts?.Cancel();
                serialPort?.Close();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Closing serial photoelectric listener failed. Port={Port}", _options.PortName);
            }
            finally
            {
                cts?.Dispose();
            }
        }

        private async Task ReadLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    using var serialPort = CreateSerialPort();
                    lock (_sync)
                    {
                        _serialPort = serialPort;
                    }

                    serialPort.Open();
                    SetStatus(isRunning: true, lastError: null);
                    _logger.LogInformation(
                        "Serial photoelectric listener started. Port={Port}, BaudRate={BaudRate}",
                        _options.PortName,
                        _options.BaudRate);

                    await ReadFramesAsync(serialPort, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex) when (cancellationToken.IsCancellationRequested)
                {
                    _logger.LogDebug(ex, "Serial photoelectric listener stopped. Port={Port}", _options.PortName);
                    break;
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    SetStatus(isRunning: false, lastError: $"Serial photoelectric listener failed on {_options.PortName}: {ex.Message}");
                    _logger.LogWarning(
                        ex,
                        "Serial photoelectric listener failed. Port={Port}, BaudRate={BaudRate}",
                        _options.PortName,
                        _options.BaudRate);

                    try
                    {
                        await Task.Delay(ReconnectDelay, cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }
                }
                finally
                {
                    lock (_sync)
                    {
                        _serialPort = null;
                        _isRunning = false;
                    }
                }
            }
        }

        private SerialPort CreateSerialPort() =>
            new(_options.PortName, _options.BaudRate, Parity.None, 8, StopBits.One)
            {
                ReadTimeout = ReadTimeoutMs,
                WriteTimeout = WriteTimeoutMs
            };

        private async Task ReadFramesAsync(SerialPort serialPort, CancellationToken cancellationToken)
        {
            var readBuffer = new byte[64];
            while (!cancellationToken.IsCancellationRequested)
            {
                var bytesToRead = Math.Max(serialPort.BytesToRead, 1);
                bytesToRead = Math.Min(bytesToRead, readBuffer.Length);

                int bytesRead;
                try
                {
                    bytesRead = serialPort.Read(readBuffer, 0, bytesToRead);
                }
                catch (TimeoutException)
                {
                    await Task.Delay(IdleDelay, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if (bytesRead <= 0)
                {
                    await Task.Delay(IdleDelay, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                ProcessBytes(readBuffer, bytesRead);
                await Task.Delay(IdleDelay, cancellationToken).ConfigureAwait(false);
            }
        }

        private void ProcessBytes(byte[] bytes, int count)
        {
            var blockSignalCount = 0;
            lock (_sync)
            {
                for (var index = 0; index < count; index++)
                {
                    _buffer.Add(bytes[index]);
                }

                while (_buffer.Count >= 2)
                {
                    var first = _buffer[0];
                    var second = _buffer[1];
                    _buffer.RemoveRange(0, 2);

                    if (first == 0x01 && second == 0x11)
                    {
                        if (!_isBlocked)
                        {
                            _isBlocked = true;
                            _logger.LogDebug(
                                "Serial photoelectric blocked. Port={Port}, Frame={Frame}",
                                _options.PortName,
                                FormatFrame(first, second));
                            blockSignalCount++;
                        }
                        else
                        {
                            _logger.LogDebug(
                                "Serial photoelectric duplicate blocked frame ignored. Port={Port}, Frame={Frame}",
                                _options.PortName,
                                FormatFrame(first, second));
                        }
                    }
                    else if (first == 0x01 && second == 0x22)
                    {
                        _isBlocked = false;
                        _logger.LogDebug(
                            "Serial photoelectric cleared. Port={Port}, Frame={Frame}",
                            _options.PortName,
                            FormatFrame(first, second));
                    }
                    else
                    {
                        _logger.LogDebug(
                            "Serial photoelectric unknown frame. Port={Port}, Frame={Frame}, Blocked={Blocked}",
                            _options.PortName,
                            FormatFrame(first, second),
                            _isBlocked);
                    }
                }
            }

            for (var index = 0; index < blockSignalCount; index++)
            {
                _publishBlockSignal(_options.PortName);
            }
        }

        private void SetStatus(bool isRunning, string? lastError)
        {
            lock (_sync)
            {
                _isRunning = isRunning;
                _lastError = lastError;
            }
        }

        private static string FormatFrame(byte first, byte second) => $"{first:X2} {second:X2}";
    }
}
