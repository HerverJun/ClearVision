using System.Net;
using ClearVision.Product.Desktop.Station;
using ClearVision.Product.Runtime.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace ClearVision.Product.Desktop.Middleware;

public sealed class StationIngressIsolationMiddleware
{
    private static readonly string[] AllowedRemotePathPrefixes =
    [
        StationSyncContractDefaults.HubPath,
        "/api/station-packages/"
    ];

    private static readonly string[] AllowedRemotePaths =
    [
        "/health",
        "/api/health"
    ];

    private readonly RequestDelegate _next;
    private readonly StationIngressOptions _options;

    public StationIngressIsolationMiddleware(
        RequestDelegate next,
        IOptions<StationIngressOptions> options)
    {
        _next = next;
        _options = options.Value;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!ShouldRestrictRemoteSurface() ||
            IsLoopbackOrUnknown(context.Connection.RemoteIpAddress) ||
            IsAllowedRemotePath(context.Request.Path))
        {
            await _next(context);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(new
        {
            Error = "RemoteAccessDisabled",
            Message = "Remote browser access is disabled for this Studio instance."
        });
    }

    private bool ShouldRestrictRemoteSurface()
    {
        return _options.Enabled && _options.ListenMode == StationIngressListenMode.Lan;
    }

    private static bool IsLoopbackOrUnknown(IPAddress? address)
    {
        return address == null || IPAddress.IsLoopback(address);
    }

    private static bool IsAllowedRemotePath(PathString path)
    {
        var value = path.Value ?? string.Empty;
        if (AllowedRemotePaths.Contains(value, StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        return AllowedRemotePathPrefixes.Any(prefix => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }
}
