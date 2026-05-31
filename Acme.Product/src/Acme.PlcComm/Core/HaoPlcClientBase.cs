using System.IO;
using System.Text;
using Acme.PlcComm.Common;
using Acme.PlcComm.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Acme.PlcComm.Core;

public abstract class HaoPlcClientBase : IPlcClient
{
    private readonly SemaphoreSlim _communicationLock = new(1, 1);
    private readonly SemaphoreSlim _connectLock = new(1, 1);
    private bool _isConnected;
    private bool _disposed;

    protected readonly ILogger Logger;

    protected HaoPlcClientBase(string ipAddress, int defaultPort, ILogger? logger = null)
    {
        IpAddress = ipAddress;
        Port = defaultPort;
        Logger = logger ?? NullLogger.Instance;
    }

    public string IpAddress { get; protected set; }
    public int Port { get; set; }
    public abstract int DefaultPort { get; }
    public bool IsConnected => !_disposed && _isConnected;
    public int ConnectTimeout { get; set; } = 10000;
    public int ReadTimeout { get; set; } = 5000;
    public int WriteTimeout { get; set; } = 5000;
    public ReconnectPolicy ReconnectPolicy { get; set; } = new();
    public IByteTransform ByteTransform { get; protected set; } = BigEndianTransform.Instance;

    public event EventHandler<ConnectionEventArgs>? Connected;
    public event EventHandler<DisconnectionEventArgs>? Disconnected;
    public event EventHandler<PlcErrorEventArgs>? ErrorOccurred;

    public async Task<bool> ConnectAsync(CancellationToken ct = default)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(GetType().Name);
        }

        await _connectLock.WaitAsync(ct);
        try
        {
            if (_isConnected)
            {
                return true;
            }

            if (Port == 0)
            {
                Port = DefaultPort;
            }

            ApplyConnectionSettings();
            ct.ThrowIfCancellationRequested();

            var result = await ConnectCoreAsync(ct);
            _isConnected = result.IsSuccess;
            if (_isConnected)
            {
                Connected?.Invoke(this, new ConnectionEventArgs
                {
                    IpAddress = IpAddress,
                    Port = Port,
                    Timestamp = DateTime.Now
                });
            }
            else
            {
                RaiseError(result, "Connect");
            }

            return _isConnected;
        }
        catch (Exception ex)
        {
            _isConnected = false;
            RaiseError(-1, $"Connect failed: {ex.Message}", "Connect");
            Logger.LogError(ex, "[{ClientType}] Connect failed: {Message}", GetType().Name, ex.Message);
            return false;
        }
        finally
        {
            _connectLock.Release();
        }
    }

    public async Task DisconnectAsync()
    {
        if (_disposed && !_isConnected)
        {
            return;
        }

        await _connectLock.WaitAsync();
        try
        {
            if (!_isConnected)
            {
                return;
            }

            try
            {
                await DisconnectCoreAsync();
            }
            finally
            {
                _isConnected = false;
                Disconnected?.Invoke(this, new DisconnectionEventArgs
                {
                    Timestamp = DateTime.Now,
                    Reason = DisconnectionReason.UserInitiated,
                    Message = "Disconnected"
                });
            }
        }
        finally
        {
            _connectLock.Release();
        }
    }

    public async Task<OperateResult<byte[]>> ReadAsync(string address, ushort length, CancellationToken ct = default)
    {
        return await ExecuteWithReconnectAsync(async () =>
        {
            var result = await ReadCoreAsync(address, length, ct);
            if (!result.IsSuccess)
            {
                RaiseError(result, "Read");
            }

            return result;
        }, ct);
    }

    public async Task<OperateResult> WriteAsync(string address, byte[] value, CancellationToken ct = default)
    {
        return await ExecuteWithReconnectAsync(async () =>
        {
            var result = await WriteCoreAsync(address, value, ct);
            if (!result.IsSuccess)
            {
                RaiseError(result, "Write");
            }

            return result;
        }, ct);
    }

    public async Task<OperateResult<T>> ReadAsync<T>(string address, CancellationToken ct = default) where T : struct
    {
        try
        {
            var (length, dataType) = GetTypeInfo<T>();
            var result = await ReadAsync(address, length, ct);
            if (!result.IsSuccess || result.Content == null)
            {
                return OperateResult<T>.Failure(result.ErrorCode, result.Message);
            }

            return OperateResult<T>.Success(ConvertBytesToType<T>(result.Content, dataType));
        }
        catch (Exception ex)
        {
            RaiseError(-1, $"Typed read failed: {ex.Message}", "ReadTyped");
            return OperateResult<T>.Failure($"Typed read failed: {ex.Message}");
        }
    }

    public async Task<OperateResult> WriteAsync<T>(string address, T value, CancellationToken ct = default) where T : struct
    {
        try
        {
            return await WriteAsync(address, ConvertTypeToBytes(value), ct);
        }
        catch (Exception ex)
        {
            RaiseError(-1, $"Typed write failed: {ex.Message}", "WriteTyped");
            return OperateResult.Failure($"Typed write failed: {ex.Message}");
        }
    }

    public async Task<OperateResult<Dictionary<string, byte[]>>> ReadBatchAsync(
        string[] addresses,
        ushort[] lengths,
        CancellationToken ct = default)
    {
        if (addresses.Length != lengths.Length)
        {
            return OperateResult<Dictionary<string, byte[]>>.Failure("Address and length arrays do not match.");
        }

        var results = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < addresses.Length; i++)
        {
            var result = await ReadAsync(addresses[i], lengths[i], ct);
            if (!result.IsSuccess || result.Content == null)
            {
                return OperateResult<Dictionary<string, byte[]>>.Failure(result.ErrorCode, result.Message);
            }

            results[addresses[i]] = result.Content;
        }

        return OperateResult<Dictionary<string, byte[]>>.Success(results);
    }

    public async Task<OperateResult<string>> ReadStringAsync(string address, ushort length, CancellationToken ct = default)
    {
        var result = await ReadAsync(address, length, ct);
        if (!result.IsSuccess || result.Content == null)
        {
            return OperateResult<string>.Failure(result.ErrorCode, result.Message);
        }

        return OperateResult<string>.Success(ByteTransform.ToString(result.Content, 0, result.Content.Length, Encoding.ASCII));
    }

    public async Task<OperateResult> WriteStringAsync(string address, string value, CancellationToken ct = default)
    {
        return await WriteAsync(address, Encoding.ASCII.GetBytes(value ?? string.Empty), ct);
    }

    public async Task<bool> PingAsync(CancellationToken ct = default)
    {
        if (!IsConnected)
        {
            return false;
        }

        try
        {
            return await PingCoreAsync(ct);
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            var disconnectTask = DisconnectCoreAsync();
            if (disconnectTask.IsCompleted)
            {
                // 已同步完成（例如在单元测试环境下的虚实现），同步获取结果以防时序断言竞态，0 阻断
                disconnectTask.GetAwaiter().GetResult();
            }
            else
            {
                // 真实异步，丢给后台，不阻塞 Dispose 同步线程，物理彻底消灭 sync-over-async
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await disconnectTask;
                    }
                    catch
                    {
                    }
                });
            }
        }
        catch
        {
            // 忽略物理连接释放过程中的异常
        }
        finally
        {
            _isConnected = false;
            _communicationLock.Dispose();
            _connectLock.Dispose();
            GC.SuppressFinalize(this);
        }
    }

    protected virtual void ForcePhysicalClose()
    {
    }

    protected async Task<T> WithCancellationAsync<T>(Task<T> task, CancellationToken ct)
    {
        if (!ct.CanBeCanceled)
        {
            return await task;
        }

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using (ct.Register(s => ((TaskCompletionSource<bool>)s!).TrySetResult(true), tcs))
        {
            if (task != await Task.WhenAny(task, tcs.Task))
            {
                ForcePhysicalClose(); // 物理强行掐断 Socket 连接，促使底层 HSL 操作快速报错失败
                throw new OperationCanceledException(ct);
            }
        }
        return await task;
    }

    protected abstract void ApplyConnectionSettings();
    protected abstract Task<OperateResult> ConnectCoreAsync(CancellationToken ct);
    protected abstract Task DisconnectCoreAsync();
    protected abstract Task<OperateResult<byte[]>> ReadCoreAsync(string address, ushort length, CancellationToken ct);
    protected abstract Task<OperateResult> WriteCoreAsync(string address, byte[] value, CancellationToken ct);
    protected abstract Task<bool> PingCoreAsync(CancellationToken ct);

    protected static OperateResult ToAcmeResult(HslCommunication.OperateResult result)
    {
        return result.IsSuccess
            ? OperateResult.Success()
            : OperateResult.Failure(result.ErrorCode, result.Message);
    }

    protected static OperateResult<T> ToAcmeResult<T>(HslCommunication.OperateResult<T> result)
    {
        return result.IsSuccess
            ? OperateResult<T>.Success(result.Content)
            : OperateResult<T>.Failure(result.ErrorCode, result.Message);
    }

    protected void RaiseError(OperateResult result, string operationType)
    {
        if (!result.IsSuccess)
        {
            RaiseError(result.ErrorCode, result.Message, operationType);
        }
    }

    protected void RaiseError(int errorCode, string message, string operationType)
    {
        ErrorOccurred?.Invoke(this, new PlcErrorEventArgs
        {
            Timestamp = DateTime.Now,
            ErrorCode = errorCode,
            Message = message,
            OperationType = operationType
        });
    }

    private async Task<OperateResult<T>> ExecuteWithReconnectAsync<T>(
        Func<Task<OperateResult<T>>> operation,
        CancellationToken ct)
    {
        if (!ReconnectPolicy.Enabled)
        {
            await _communicationLock.WaitAsync(ct);
            try
            {
                return await operation();
            }
            finally
            {
                _communicationLock.Release();
            }
        }

        for (var retry = 0; retry <= ReconnectPolicy.MaxRetries; retry++)
        {
            if (!IsConnected)
            {
                var connected = await ConnectAsync(ct);
                if (!connected)
                {
                    if (retry < ReconnectPolicy.MaxRetries)
                    {
                        await Task.Delay(GetRetryDelay(retry), ct);
                        continue;
                    }

                    return OperateResult<T>.Failure("Reconnect failed.");
                }
            }

            try
            {
                await _communicationLock.WaitAsync(ct);
                try
                {
                    return await operation();
                }
                finally
                {
                    _communicationLock.Release();
                }
            }
            catch (IOException ex)
            {
                _isConnected = false;
                RaiseError(-1, $"I/O error: {ex.Message}", "Communication");
                return OperateResult<T>.Failure($"I/O error: {ex.Message}");
            }
            catch (Exception ex)
            {
                RaiseError(-1, $"Operation failed: {ex.Message}", "Operation");
                return OperateResult<T>.Failure($"Operation failed: {ex.Message}");
            }
        }

        return OperateResult<T>.Failure("Reconnect failed.");
    }

    private async Task<OperateResult> ExecuteWithReconnectAsync(
        Func<Task<OperateResult>> operation,
        CancellationToken ct)
    {
        var result = await ExecuteWithReconnectAsync<object?>(async () =>
        {
            var inner = await operation();
            return inner.IsSuccess
                ? OperateResult<object?>.Success(null)
                : OperateResult<object?>.Failure(inner.ErrorCode, inner.Message);
        }, ct);

        return result.IsSuccess
            ? OperateResult.Success()
            : OperateResult.Failure(result.ErrorCode, result.Message);
    }

    private TimeSpan GetRetryDelay(int retry)
    {
        var calculatedDelay = ReconnectPolicy.ExponentialBackoff
            ? TimeSpan.FromSeconds(Math.Pow(2, retry))
            : ReconnectPolicy.RetryInterval;

        return calculatedDelay <= ReconnectPolicy.MaxRetryInterval
            ? calculatedDelay
            : ReconnectPolicy.MaxRetryInterval;
    }

    private static (ushort length, PlcDataType dataType) GetTypeInfo<T>()
    {
        var type = typeof(T);
        return type.Name switch
        {
            "Boolean" or "Bool" => (1, PlcDataType.Bit),
            "Byte" => (1, PlcDataType.Byte),
            "Int16" or "UInt16" or "Short" or "UShort" => (1, PlcDataType.Word),
            "Int32" or "UInt32" or "Int" or "UInt" => (2, PlcDataType.DWord),
            "Single" or "Float" => (2, PlcDataType.Float),
            "Double" => (4, PlcDataType.Double),
            "Int64" or "UInt64" or "Long" or "ULong" => (4, PlcDataType.LWord),
            _ => throw new NotSupportedException($"Unsupported data type: {type.Name}")
        };
    }

    private T ConvertBytesToType<T>(byte[] buffer, PlcDataType dataType) where T : struct
    {
        object value = typeof(T).Name switch
        {
            "Boolean" or "Bool" => ByteTransform.ToBool(buffer, 0),
            "Byte" => buffer[0],
            "Int16" or "Short" => ByteTransform.ToInt16(buffer, 0),
            "UInt16" or "UShort" => ByteTransform.ToUInt16(buffer, 0),
            "Int32" or "Int" => ByteTransform.ToInt32(buffer, 0),
            "UInt32" or "UInt" => ByteTransform.ToUInt32(buffer, 0),
            "Single" or "Float" => ByteTransform.ToFloat(buffer, 0),
            "Double" => ByteTransform.ToDouble(buffer, 0),
            "Int64" or "Long" => ByteTransform.ToInt64(buffer, 0),
            "UInt64" or "ULong" => ByteTransform.ToUInt64(buffer, 0),
            _ => throw new NotSupportedException($"Unsupported data type: {typeof(T).Name}")
        };
        return (T)value;
    }

    private byte[] ConvertTypeToBytes<T>(T value) where T : struct
    {
        return typeof(T).Name switch
        {
            "Boolean" or "Bool" => ByteTransform.GetBytes(Convert.ToBoolean(value)),
            "Byte" => new[] { Convert.ToByte(value) },
            "Int16" or "Short" => ByteTransform.GetBytes(Convert.ToInt16(value)),
            "UInt16" or "UShort" => ByteTransform.GetBytes(Convert.ToUInt16(value)),
            "Int32" or "Int" => ByteTransform.GetBytes(Convert.ToInt32(value)),
            "UInt32" or "UInt" => ByteTransform.GetBytes(Convert.ToUInt32(value)),
            "Single" or "Float" => ByteTransform.GetBytes(Convert.ToSingle(value)),
            "Double" => ByteTransform.GetBytes(Convert.ToDouble(value)),
            "Int64" or "Long" => ByteTransform.GetBytes(Convert.ToInt64(value)),
            "UInt64" or "ULong" => ByteTransform.GetBytes(Convert.ToUInt64(value)),
            _ => throw new NotSupportedException($"Unsupported data type: {typeof(T).Name}")
        };
    }
}
