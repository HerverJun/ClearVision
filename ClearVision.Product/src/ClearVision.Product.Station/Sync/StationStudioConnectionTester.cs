using ClearVision.Product.Runtime.Abstractions;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;

namespace ClearVision.Product.Station.Sync;

public sealed class StationStudioConnectionTester
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(8);

    public async Task<StationStudioConnectionTestResult> TestAsync(
        StationSyncConnectionSettings settings,
        CancellationToken cancellationToken = default)
    {
        var baseUrl = NormalizeBaseUrl(settings.StudioBaseUrl);
        var hubUrl = NormalizeUrl(settings.ResolvedStudioHubUrl);
        var token = settings.SharedToken.Trim();

        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return StationStudioConnectionTestResult.Fail("请填写 Studio 地址，例如 http://192.168.137.13:5000。");
        }

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri) ||
            (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps))
        {
            return StationStudioConnectionTestResult.Fail("Studio 地址必须是 http 或 https 开头的完整地址。");
        }

        if (string.IsNullOrWhiteSpace(hubUrl) || !Uri.TryCreate(hubUrl, UriKind.Absolute, out _))
        {
            return StationStudioConnectionTestResult.Fail("无法解析 Studio Hub 地址，请检查 Studio 地址。");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(DefaultTimeout);

        var healthResult = await TestHealthAsync(baseUri, timeout.Token);
        if (!healthResult.Success)
        {
            return healthResult;
        }

        return await TestSignalRAsync(hubUrl, token, timeout.Token);
    }

    private static async Task<StationStudioConnectionTestResult> TestHealthAsync(
        Uri baseUri,
        CancellationToken cancellationToken)
    {
        try
        {
            using var httpClient = new HttpClient();
            using var response = await httpClient.GetAsync(BuildHealthUri(baseUri), cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return StationStudioConnectionTestResult.Succeeded("Studio HTTP /health 可达。", healthReachable: true, hubReachable: false);
            }

            return StationStudioConnectionTestResult.Fail(
                $"Studio /health 返回 {(int)response.StatusCode} {response.ReasonPhrase}。",
                healthReachable: false);
        }
        catch (OperationCanceledException)
        {
            return StationStudioConnectionTestResult.Fail("连接 Studio /health 超时，请检查地址、防火墙或 Studio 是否已启动。");
        }
        catch (Exception ex)
        {
            return StationStudioConnectionTestResult.Fail($"无法访问 Studio /health：{ex.Message}");
        }
    }

    private static async Task<StationStudioConnectionTestResult> TestSignalRAsync(
        string hubUrl,
        string token,
        CancellationToken cancellationToken)
    {
        HubConnection? connection = null;
        try
        {
            connection = new HubConnectionBuilder()
                .WithUrl(
                    hubUrl,
                    options =>
                    {
                        options.Transports = HttpTransportType.WebSockets | HttpTransportType.LongPolling;
                        if (!string.IsNullOrWhiteSpace(token))
                        {
                            options.AccessTokenProvider = () => Task.FromResult<string?>(token);
                            options.Headers[StationSyncContractDefaults.StationTokenHeaderName] = token;
                            options.Headers["X-Station-Token"] = token;
                        }
                    })
                .Build();

            await connection.StartAsync(cancellationToken);
            var probe = await connection.InvokeAsync<StationProbeAckDto>(
                StationHubMethods.Probe,
                cancellationToken);
            if (probe.Accepted)
            {
                return StationStudioConnectionTestResult.Succeeded(
                    "Studio 可达，SignalR 入口和 token 验证通过。",
                    healthReachable: true,
                    hubReachable: true);
            }

            return StationStudioConnectionTestResult.Fail(
                probe.Message ?? "Studio SignalR 入口拒绝了连接测试。",
                healthReachable: true);
        }
        catch (HubException ex) when (IsTokenOrIngressFailure(ex))
        {
            return StationStudioConnectionTestResult.Fail(
                "Studio 可达，但 token 不正确或 Studio 未启用 Station 入口。",
                healthReachable: true);
        }
        catch (OperationCanceledException)
        {
            return StationStudioConnectionTestResult.Fail(
                "连接 Studio SignalR 入口超时，请检查地址、防火墙或 Studio Station 入口设置。",
                healthReachable: true);
        }
        catch (Exception ex)
        {
            return StationStudioConnectionTestResult.Fail(
                $"Studio 可达，但 SignalR 入口连接失败：{ex.Message}",
                healthReachable: true);
        }
        finally
        {
            if (connection != null)
            {
                await connection.DisposeAsync();
            }
        }
    }

    private static bool IsTokenOrIngressFailure(HubException ex)
    {
        return ex.Message.Contains("token", StringComparison.OrdinalIgnoreCase) ||
               ex.Message.Contains("ingress", StringComparison.OrdinalIgnoreCase) ||
               ex.Message.Contains("disabled", StringComparison.OrdinalIgnoreCase) ||
               ex.Message.Contains("not configured", StringComparison.OrdinalIgnoreCase);
    }

    private static Uri BuildHealthUri(Uri baseUri)
    {
        return new Uri(baseUri.ToString().TrimEnd('/') + "/health", UriKind.Absolute);
    }

    private static string NormalizeBaseUrl(string value)
    {
        var normalized = NormalizeUrl(value);
        var hubIndex = normalized.IndexOf(StationSyncContractDefaults.HubPath, StringComparison.OrdinalIgnoreCase);
        return hubIndex > 0 ? normalized[..hubIndex].TrimEnd('/') : normalized;
    }

    private static string NormalizeUrl(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().TrimEnd('/');
    }
}

public sealed record StationStudioConnectionTestResult
{
    public bool Success { get; init; }

    public string Message { get; init; } = string.Empty;

    public bool HealthReachable { get; init; }

    public bool HubReachable { get; init; }

    public static StationStudioConnectionTestResult Succeeded(
        string message,
        bool healthReachable,
        bool hubReachable)
    {
        return new StationStudioConnectionTestResult
        {
            Success = true,
            Message = message,
            HealthReachable = healthReachable,
            HubReachable = hubReachable
        };
    }

    public static StationStudioConnectionTestResult Fail(
        string message,
        bool healthReachable = false)
    {
        return new StationStudioConnectionTestResult
        {
            Success = false,
            Message = message,
            HealthReachable = healthReachable,
            HubReachable = false
        };
    }
}
