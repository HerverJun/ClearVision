// HttpRequestOperator.cs
// HTTP 请求算子 - Sprint 3 Task 3.5a
// 调用 REST API，触发 MES/AGV 等外部服务
// 作者：蘅芜君

using ClearVision.Product.Core.Attributes;
using ClearVision.Product.Core.Entities;
using ClearVision.Product.Core.Enums;
using ClearVision.Product.Core.Operators;
using ClearVision.Product.Core.Services;
using Microsoft.Extensions.Logging;
namespace ClearVision.Product.Infrastructure.Operators;

/// <summary>
/// HTTP 请求算子 - 调用 REST API
/// 
/// 功能：
/// - 支持 GET/POST/PUT/DELETE
/// - 支持自定义 Headers
/// - 支持 JSON 请求体
/// - 支持超时配置
/// - 支持重试机制
/// 
/// 使用场景：
/// - 调用 MES API 上报检测结果
/// - 触发 AGV 搬运指令
/// - 查询外部系统数据
/// </summary>
[OperatorMeta(
    DisplayName = "HTTP 请求",
    Description = "调用外部 REST API",
    CategoryId = OperatorCategoryId.Communication,
    IconName = "http",
    Version = "1.0.1"
)]
[InputPort("Body", "请求体", PortDataType.String, IsRequired = false)]
[InputPort("Headers", "请求头", PortDataType.Any, IsRequired = false)]
[OutputPort("Response", "响应内容", PortDataType.String)]
[OutputPort("StatusCode", "状态码", PortDataType.Integer)]
[OutputPort("IsSuccess", "是否成功", PortDataType.Boolean)]
[OperatorParam("Url", "API 地址", "string", DefaultValue = "http://localhost:5000/api")]
[OperatorParam("Method", "方法", "enum", DefaultValue = "POST", Options = new[] { "GET|GET", "POST|POST", "PUT|PUT", "DELETE|DELETE" })]
[OperatorParam("TimeoutMs", "超时(ms)", "int", DefaultValue = 10000, Min = 1000, Max = 60000)]
[OperatorParam("RetryCount", "最大重试", "int", DefaultValue = 0, Min = 0, Max = 5)]
[OperatorParam("ContentType", "内容类型", "string", DefaultValue = "application/json")]
[OperatorParam("RetryDelayMs", "重试延迟(ms)", "int", DefaultValue = 1000, Min = 0, Max = 10000)]
public class HttpRequestOperator : OperatorBase
{
    public override OperatorType OperatorType => OperatorType.HttpRequest;

    private readonly IHttpResourceBroker _httpResourceBroker;

    public HttpRequestOperator(
        ILogger<HttpRequestOperator> logger,
        IHttpResourceBroker httpResourceBroker)
        : base(logger)
    {
        _httpResourceBroker = httpResourceBroker ?? throw new ArgumentNullException(nameof(httpResourceBroker));
    }

    protected override async Task<OperatorExecutionOutput> ExecuteCoreAsync(
        Operator @operator,
        Dictionary<string, object>? inputs,
        CancellationToken cancellationToken)
    {
        // 获取参数
        var url = GetStringParam(@operator, "Url", "");
        var method = GetStringParam(@operator, "Method", "POST");
        var contentType = GetStringParam(@operator, "ContentType", "application/json");
        var timeoutMs = GetIntParam(@operator, "TimeoutMs", 10000, 1000, 60000);
        var retryCount = GetIntParam(@operator, "RetryCount", 0, 0, 5);
        var retryDelayMs = GetIntParam(@operator, "RetryDelayMs", 1000, 0, 10000);
        var normalizedMethod = method.Trim().ToUpperInvariant();
        var maxAttempts = IsAutomaticRetryAllowed(normalizedMethod) ? retryCount : 0;

        if (string.IsNullOrWhiteSpace(url))
        {
            return OperatorExecutionOutput.Failure("Url 参数不能为空");
        }

        // 构建请求体
        string? body = null;
        if (inputs != null)
        {
            if (inputs.TryGetValue("Body", out var bodyObj) && bodyObj != null)
            {
                body = bodyObj.ToString();
            }
        }

        // 构建 Headers
        var headers = new Dictionary<string, string>();
        if (inputs != null && inputs.TryGetValue("Headers", out var headersObj) && headersObj is Dictionary<string, object> headersDict)
        {
            foreach (var (key, value) in headersDict)
            {
                headers[key] = value?.ToString() ?? "";
            }
        }

        // 执行请求（带重试）
        for (int attempt = 0; attempt <= maxAttempts; attempt++)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(timeoutMs));
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, cancellationToken);

                var result = await ExecuteRequestAsync(
                    url,
                    normalizedMethod,
                    body,
                    contentType,
                    headers,
                    linkedCts.Token);

                if (result.IsSuccess)
                {
                    return OperatorExecutionOutput.Success(result.OutputData);
                }

                // 如果不是最后一次尝试，等待后重试
                if (attempt < maxAttempts)
                {
                    Logger.LogWarning("[HttpRequest] 请求失败，{Delay}ms 后重试 ({Attempt}/{RetryCount})",
                        retryDelayMs, attempt + 1, maxAttempts);
                    await Task.Delay(retryDelayMs, cancellationToken);
                }
                else
                {
                    return OperatorExecutionOutput.Failure(result.ErrorMessage ?? "HTTP 请求失败");
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // 超时
                if (attempt < maxAttempts)
                {
                    Logger.LogWarning("[HttpRequest] 请求超时，{Delay}ms 后重试 ({Attempt}/{RetryCount})",
                        retryDelayMs, attempt + 1, maxAttempts);
                    await Task.Delay(retryDelayMs, cancellationToken);
                }
                else
                {
                    return OperatorExecutionOutput.Failure($"HTTP 请求超时 ({timeoutMs}ms)");
                }
            }
            catch (HttpResourceBrokerException ex)
            {
                Logger.LogWarning(
                    "[HttpRequest] outbound resource policy rejected request: {Code}",
                    ex.Code);
                return OperatorExecutionOutput.Failure($"{ex.Code}: {ex.Message}");
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "[HttpRequest] 请求异常");

                if (attempt < maxAttempts)
                {
                    await Task.Delay(retryDelayMs, cancellationToken);
                }
                else
                {
                    return OperatorExecutionOutput.Failure($"HTTP 请求异常: {ex.Message}");
                }
            }
        }

        return OperatorExecutionOutput.Failure("HTTP 请求失败（超出重试次数）");
    }

    private static bool IsAutomaticRetryAllowed(string normalizedMethod)
    {
        return normalizedMethod switch
        {
            "GET" => true,
            "HEAD" => true,
            "OPTIONS" => true,
            _ => false
        };
    }

    private async Task<RequestResult> ExecuteRequestAsync(
        string url,
        string method,
        string? body,
        string contentType,
        Dictionary<string, string> headers,
        CancellationToken cancellationToken)
    {
        var response = await _httpResourceBroker.SendAsync(
            new HttpResourceRequest(
                url,
                new HttpMethod(method),
                body,
                contentType,
                headers),
            cancellationToken);

        var outputData = new Dictionary<string, object>
        {
            { "StatusCode", (int)response.StatusCode },
            { "IsSuccess", response.IsSuccessStatusCode },
            { "IsSuccessStatusCode", response.IsSuccessStatusCode },
            { "Response", response.Body },
            { "ResponseBody", response.Body }
        };

        if (response.IsSuccessStatusCode)
        {
            return new RequestResult { IsSuccess = true, OutputData = outputData };
        }
        else
        {
            return new RequestResult
            {
                IsSuccess = false,
                ErrorMessage = $"HTTP {(int)response.StatusCode}: {response.Body}",
                OutputData = outputData
            };
        }
    }

    public override ValidationResult ValidateParameters(Operator @operator)
    {
        var url = GetStringParam(@operator, "Url", "");
        var method = GetStringParam(@operator, "Method", "POST");

        if (string.IsNullOrWhiteSpace(url))
        {
            return ValidationResult.Invalid("Url 不能为空");
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            !(uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
              uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) ||
            string.IsNullOrWhiteSpace(uri.Host) ||
            !string.IsNullOrWhiteSpace(uri.UserInfo) ||
            uri.Port is < 1 or > 65535 ||
            uri.HostNameType is not (
                UriHostNameType.Dns or UriHostNameType.IPv4 or UriHostNameType.IPv6))
        {
            return ValidationResult.Invalid("Url 必须是有效的 http/https 绝对地址，且不得包含用户凭据");
        }

        var validMethods = new[] { "GET", "POST", "PUT", "DELETE", "PATCH" };
        if (!validMethods.Contains(method.ToUpper()))
        {
            return ValidationResult.Invalid($"Method 必须是以下之一: {string.Join(", ", validMethods)}");
        }

        return ValidationResult.Valid();
    }

    private class RequestResult
    {
        public bool IsSuccess { get; set; }
        public string? ErrorMessage { get; set; }
        public Dictionary<string, object>? OutputData { get; set; }
    }
}
