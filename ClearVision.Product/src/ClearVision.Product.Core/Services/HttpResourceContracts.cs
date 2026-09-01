using System.Net;

namespace ClearVision.Product.Core.Services;

/// <summary>
/// Read-only request description passed to a host-owned outbound HTTP broker.
/// The contract carries no transport, DNS, policy, or runtime host implementation.
/// </summary>
public sealed record HttpResourceRequest(
    string Url,
    HttpMethod Method,
    string? Body,
    string? ContentType,
    IReadOnlyDictionary<string, string>? Headers = null);

/// <summary>
/// Immutable result returned by a host-owned outbound HTTP broker.
/// </summary>
public sealed record HttpResourceResponse(
    HttpStatusCode StatusCode,
    bool IsSuccessStatusCode,
    string Body,
    Uri FinalUri,
    int RedirectCount);

/// <summary>
/// Public capability boundary. A package operator can describe an HTTP request,
/// but only the product host may supply an implementation that performs network I/O.
/// </summary>
public interface IHttpResourceBroker
{
    Task<HttpResourceResponse> SendAsync(
        HttpResourceRequest request,
        CancellationToken cancellationToken);
}

public sealed class HttpResourceBrokerException : Exception
{
    public HttpResourceBrokerException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public HttpResourceBrokerException(string code, string message, Exception innerException)
        : base(message, innerException)
    {
        Code = code;
    }

    public string Code { get; }
}
