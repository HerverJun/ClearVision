using ClearVision.Product.Core.Entities;

namespace ClearVision.Product.Core.Interfaces;

public interface ITcpDeviceManager : IAsyncDisposable
{
    Task<TcpCommunicationConfig> GetConfigAsync(CancellationToken cancellationToken = default);

    Task<TcpCommunicationConfig> SaveConfigAsync(
        TcpCommunicationConfig config,
        CancellationToken cancellationToken = default);

    Task<TcpDeviceOperationResult> ConnectAsync(
        string profileId,
        CancellationToken cancellationToken = default);

    Task<TcpDeviceOperationResult> DisconnectAsync(
        string profileId,
        CancellationToken cancellationToken = default);

    Task<TcpDeviceSendResult> SendAsync(
        string profileId,
        TcpDeviceSendRequest request,
        CancellationToken cancellationToken = default);

    Task<TcpDeviceSendResult> SendTransientAsync(
        TcpCommunicationProfile profile,
        TcpDeviceSendRequest request,
        CancellationToken cancellationToken = default);

    Task<TcpDeviceOperationResult> StartServerAsync(
        string profileId,
        CancellationToken cancellationToken = default);

    Task<TcpDeviceOperationResult> StopServerAsync(
        string profileId,
        CancellationToken cancellationToken = default);

    Task<TcpProfileStatus> GetStatusAsync(
        string profileId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TcpFrameLogEntry>> GetFramesAsync(
        string profileId,
        CancellationToken cancellationToken = default);

    Task ClearFramesAsync(
        string profileId,
        CancellationToken cancellationToken = default);
}

public sealed record TcpDeviceOperationResult(
    bool Success,
    string Message,
    TcpProfileStatus? Status = null,
    IReadOnlyList<TcpCommunicationValidationIssue>? Errors = null)
{
    public static TcpDeviceOperationResult Ok(string message, TcpProfileStatus? status = null)
    {
        return new TcpDeviceOperationResult(true, message, status);
    }

    public static TcpDeviceOperationResult Fail(
        string message,
        TcpProfileStatus? status = null,
        IReadOnlyList<TcpCommunicationValidationIssue>? errors = null)
    {
        return new TcpDeviceOperationResult(false, message, status, errors);
    }
}

public sealed record TcpDeviceSendRequest(
    string Payload,
    bool IsHex = false,
    bool WaitResponse = true,
    int? ResponseTimeoutMs = null);

public sealed record TcpDeviceSendResult(
    bool Success,
    string Message,
    string Response = "",
    TcpProfileStatus? Status = null,
    IReadOnlyList<TcpCommunicationValidationIssue>? Errors = null)
{
    public static TcpDeviceSendResult Ok(string message, string response, TcpProfileStatus? status = null)
    {
        return new TcpDeviceSendResult(true, message, response, status);
    }

    public static TcpDeviceSendResult Fail(
        string message,
        TcpProfileStatus? status = null,
        IReadOnlyList<TcpCommunicationValidationIssue>? errors = null)
    {
        return new TcpDeviceSendResult(false, message, string.Empty, status, errors);
    }
}

public sealed record TcpProfileStatus(
    string ProfileId,
    string Mode,
    bool IsConnected,
    bool IsListening,
    string? LocalEndpoint,
    string? RemoteEndpoint,
    int ConnectedClients,
    string LastError,
    DateTimeOffset? LastConnectedAtUtc,
    DateTimeOffset? LastReceivedAtUtc,
    DateTimeOffset? LastSentAtUtc);

public sealed record TcpFrameLogEntry(
    string Id,
    string ProfileId,
    string Direction,
    DateTimeOffset TimestampUtc,
    int ByteCount,
    string Text,
    string Hex,
    string? RemoteEndpoint);
