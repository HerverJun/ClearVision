using ClearVision.Product.Runtime.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ClearVision.Product.Desktop.Station;

public sealed class StationIngressAuthService
{
    private readonly StationIngressOptions _options;
    private readonly ILogger<StationIngressAuthService> _logger;

    public StationIngressAuthService(
        IOptions<StationIngressOptions> options,
        ILogger<StationIngressAuthService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public bool IsEnabled => _options.Enabled;

    public bool TryAuthorize(HttpContext? context, out string failureReason)
    {
        if (!_options.Enabled)
        {
            failureReason = "Station ingress is disabled.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(_options.SharedToken) && !_options.AllowInsecureDevelopment)
        {
            failureReason = "Station ingress shared token is not configured.";
            return false;
        }

#if DEBUG
        if (_options.AllowInsecureDevelopment && string.IsNullOrWhiteSpace(_options.SharedToken))
        {
            _logger.LogWarning("Station ingress accepted without a shared token because AllowInsecureDevelopment is enabled in a DEBUG build.");
            failureReason = string.Empty;
            return true;
        }
#endif

        if (string.IsNullOrWhiteSpace(_options.SharedToken))
        {
            failureReason = "Station ingress shared token is not configured.";
            return false;
        }

        var suppliedToken = ExtractToken(context);
        if (!string.Equals(suppliedToken, _options.SharedToken, StringComparison.Ordinal))
        {
            failureReason = "Station ingress token is invalid.";
            _logger.LogWarning("Rejected Station ingress request from {RemoteIpAddress}", context?.Connection.RemoteIpAddress);
            return false;
        }

        failureReason = string.Empty;
        return true;
    }

    private static string? ExtractToken(HttpContext? context)
    {
        if (context == null)
        {
            return null;
        }

        if (context.Request.Headers.TryGetValue(StationSyncContractDefaults.StationTokenHeaderName, out var clearVisionHeaderToken))
        {
            return clearVisionHeaderToken.FirstOrDefault();
        }

        if (context.Request.Headers.TryGetValue("X-Station-Token", out var headerToken))
        {
            return headerToken.FirstOrDefault();
        }

        if (context.Request.Headers.Authorization.FirstOrDefault() is { Length: > 0 } authHeader &&
            authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return authHeader["Bearer ".Length..].Trim();
        }

        if (context.Request.Query.TryGetValue("access_token", out var queryToken))
        {
            return queryToken.FirstOrDefault();
        }

        return null;
    }
}
