using System.Net;
using System.Net.Sockets;
using System.Globalization;
using System.Text;
using ClearVision.Product.Core.Services;
using Microsoft.Extensions.Options;

namespace ClearVision.Product.Infrastructure.Services;

/// <summary>
/// Server-owned policy for outbound HTTP operators. Private or otherwise
/// non-public address space stays denied unless a deployment explicitly adds
/// the required CIDR to <see cref="AdditionalAllowedCidrs"/>.
/// </summary>
public sealed class HttpResourceBrokerOptions
{
    public const string ConfigurationSection = "Execution:HttpResourceBroker";

    public int MaxRedirects { get; set; } = 5;
    public int MaxResponseBodyBytes { get; set; } = 1_048_576;
    public List<string> AllowedHosts { get; set; } = [];
    public List<int> AllowedPorts { get; set; } = [];
    public List<string> AdditionalAllowedCidrs { get; set; } = [];
}

public interface IHttpResourceDnsResolver
{
    Task<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken cancellationToken);
}

public sealed class SystemHttpResourceDnsResolver : IHttpResourceDnsResolver
{
    public async Task<IReadOnlyList<IPAddress>> ResolveAsync(
        string host,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (IPAddress.TryParse(host, out var literal))
        {
            return [literal];
        }

        return await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false);
    }
}

public interface IHttpResourceTransport : IDisposable
{
    Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken);
}

/// <summary>
/// Test/adapter transport for an injected handler. Automatic redirect behavior
/// is determined by the supplied handler and is never enabled here.
/// </summary>
internal sealed class HttpMessageHandlerResourceTransport : IHttpResourceTransport
{
    private readonly HttpClient _client;

    public HttpMessageHandlerResourceTransport(HttpMessageHandler handler, bool disposeHandler = true)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _client = new HttpClient(handler, disposeHandler);
    }

    public Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }

    public void Dispose() => _client.Dispose();
}

/// <summary>
/// Production transport. Redirects are handled exclusively by the broker.
/// The connect callback resolves and validates every address immediately before
/// opening the socket, so the HttpClient stack cannot perform a second,
/// unvalidated DNS lookup.
/// </summary>
public sealed class SecureHttpResourceTransport : IHttpResourceTransport
{
    private readonly IHttpResourceDnsResolver _resolver;
    private readonly HttpResourcePolicy _policy;
    private readonly HttpClient _client;

    public SecureHttpResourceTransport(
        IHttpResourceDnsResolver resolver,
        IOptions<HttpResourceBrokerOptions> options)
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        ArgumentNullException.ThrowIfNull(options);
        _policy = HttpResourcePolicy.Create(options.Value);

        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseProxy = false,
            UseCookies = false,
            ConnectCallback = ConnectValidatedAsync,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(1)
        };
        _client = new HttpClient(handler, disposeHandler: true);
    }

    public Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }

    public void Dispose() => _client.Dispose();

    private async ValueTask<Stream> ConnectValidatedAsync(
        SocketsHttpConnectionContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var addresses = await HttpResourceDestinationValidator.ResolveAndValidateAsync(
                context.DnsEndPoint.Host,
                context.DnsEndPoint.Port,
                _resolver,
                _policy,
                cancellationToken)
            .ConfigureAwait(false);

        Exception? lastFailure = null;
        foreach (var address in addresses)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
            {
                NoDelay = true
            };

            try
            {
                await socket.ConnectAsync(
                        new IPEndPoint(address, context.DnsEndPoint.Port),
                        cancellationToken)
                    .ConfigureAwait(false);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (OperationCanceledException)
            {
                socket.Dispose();
                throw;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                lastFailure = ex;
                socket.Dispose();
            }
        }

        throw new HttpRequestException(
            $"Unable to connect to validated HTTP destination '{context.DnsEndPoint.Host}'.",
            lastFailure);
    }
}

/// <summary>
/// Performs destination validation on the initial URI and every redirect,
/// disables implicit redirects through the production transport, and reads
/// response bodies through a fixed byte budget.
/// </summary>
public sealed class ServerHttpResourceBroker : IHttpResourceBroker
{
    private static readonly HashSet<string> AllowedMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "GET", "HEAD", "OPTIONS", "POST", "PUT", "DELETE", "PATCH"
    };

    private static readonly HashSet<string> ForbiddenForwardedHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Host",
        "Connection",
        "Content-Length",
        "Transfer-Encoding",
        "Upgrade",
        "Proxy-Authorization",
        "Proxy-Connection"
    };

    private static readonly HashSet<string> SensitiveRedirectHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Authorization",
        "Cookie"
    };

    private readonly IHttpResourceDnsResolver _resolver;
    private readonly IHttpResourceTransport _transport;
    private readonly HttpResourcePolicy _policy;

    public ServerHttpResourceBroker(
        IHttpResourceDnsResolver resolver,
        IHttpResourceTransport transport,
        IOptions<HttpResourceBrokerOptions> options)
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        ArgumentNullException.ThrowIfNull(options);
        _policy = HttpResourcePolicy.Create(options.Value);
    }

    public async Task<HttpResourceResponse> SendAsync(
        HttpResourceRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Method);
        cancellationToken.ThrowIfCancellationRequested();
        if (!AllowedMethods.Contains(request.Method.Method))
        {
            throw new HttpResourceBrokerException(
                "HTTP_METHOD_NOT_ALLOWED",
                "HTTP method is not allowed by the outbound resource broker.");
        }

        var currentUri = HttpResourceDestinationValidator.ParseAndValidateUri(request.Url, _policy);
        var currentMethod = request.Method;
        var currentBody = request.Body;
        var currentHeaders = NormalizeHeaders(request.Headers);
        var redirectCount = 0;

        while (true)
        {
            await HttpResourceDestinationValidator.ResolveAndValidateAsync(
                    currentUri,
                    _resolver,
                    _policy,
                    cancellationToken)
                .ConfigureAwait(false);

            // This is the dispatch boundary. A cancellation observed after DNS
            // preflight must never reach the handler/transport.
            cancellationToken.ThrowIfCancellationRequested();
            using var message = CreateRequestMessage(
                currentUri,
                currentMethod,
                currentBody,
                request.ContentType,
                currentHeaders);
            cancellationToken.ThrowIfCancellationRequested();
            using var response = await _transport.SendAsync(message, cancellationToken).ConfigureAwait(false);

            if (IsRedirect(response.StatusCode))
            {
                if (redirectCount >= _policy.MaxRedirects)
                {
                    throw new HttpResourceBrokerException(
                        "HTTP_REDIRECT_LIMIT_EXCEEDED",
                        $"HTTP response exceeded the redirect limit of {_policy.MaxRedirects}.");
                }

                var location = response.Headers.Location;
                if (location == null)
                {
                    throw new HttpResourceBrokerException(
                        "HTTP_REDIRECT_LOCATION_REQUIRED",
                        "HTTP redirect response did not provide a Location header.");
                }

                Uri nextUri;
                try
                {
                    nextUri = location.IsAbsoluteUri ? location : new Uri(currentUri, location);
                }
                catch (UriFormatException ex)
                {
                    throw new HttpResourceBrokerException(
                        "HTTP_REDIRECT_DESTINATION_INVALID",
                        "HTTP redirect Location is invalid.",
                        ex);
                }

                nextUri = HttpResourceDestinationValidator.ParseAndValidateUri(nextUri.AbsoluteUri, _policy);
                if (!HasSameOrigin(currentUri, nextUri))
                {
                    foreach (var header in SensitiveRedirectHeaders)
                    {
                        currentHeaders.Remove(header);
                    }
                }

                ApplyRedirectMethod(response.StatusCode, ref currentMethod, ref currentBody);
                currentUri = nextUri;
                redirectCount++;
                continue;
            }

            var responseBody = await ReadBoundedBodyAsync(
                    response.Content,
                    _policy.MaxResponseBodyBytes,
                    cancellationToken)
                .ConfigureAwait(false);
            return new HttpResourceResponse(
                response.StatusCode,
                response.IsSuccessStatusCode,
                responseBody,
                currentUri,
                redirectCount);
        }
    }

    private static Dictionary<string, string> NormalizeHeaders(IReadOnlyDictionary<string, string>? headers)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (headers == null)
        {
            return result;
        }

        foreach (var (name, value) in headers)
        {
            var normalizedName = name?.Trim() ?? string.Empty;
            if (normalizedName.Length == 0 || ForbiddenForwardedHeaders.Contains(normalizedName))
            {
                continue;
            }

            result[normalizedName] = value ?? string.Empty;
        }

        return result;
    }

    private static HttpRequestMessage CreateRequestMessage(
        Uri uri,
        HttpMethod method,
        string? body,
        string? contentType,
        IReadOnlyDictionary<string, string> headers)
    {
        var message = new HttpRequestMessage(method, uri);
        if (body != null)
        {
            message.Content = new StringContent(
                body,
                Encoding.UTF8,
                string.IsNullOrWhiteSpace(contentType) ? "application/json" : contentType);
        }

        foreach (var (name, value) in headers)
        {
            if (name.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
            {
                if (message.Content != null)
                {
                    message.Content.Headers.Remove("Content-Type");
                    message.Content.Headers.TryAddWithoutValidation("Content-Type", value);
                }

                continue;
            }

            if (!message.Headers.TryAddWithoutValidation(name, value) && message.Content != null)
            {
                message.Content.Headers.TryAddWithoutValidation(name, value);
            }
        }

        return message;
    }

    private static bool IsRedirect(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.MovedPermanently or
            HttpStatusCode.Redirect or
            HttpStatusCode.SeeOther or
            HttpStatusCode.TemporaryRedirect or
            HttpStatusCode.PermanentRedirect;

    private static void ApplyRedirectMethod(
        HttpStatusCode statusCode,
        ref HttpMethod method,
        ref string? body)
    {
        var convertToGet = statusCode == HttpStatusCode.SeeOther && method != HttpMethod.Head ||
            statusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Redirect &&
            method == HttpMethod.Post;
        if (!convertToGet)
        {
            return;
        }

        method = HttpMethod.Get;
        body = null;
    }

    private static bool HasSameOrigin(Uri left, Uri right) =>
        left.Scheme.Equals(right.Scheme, StringComparison.OrdinalIgnoreCase) &&
        left.IdnHost.Equals(right.IdnHost, StringComparison.OrdinalIgnoreCase) &&
        left.Port == right.Port;

    private static async Task<string> ReadBoundedBodyAsync(
        HttpContent content,
        int maxBytes,
        CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is long declaredLength && declaredLength > maxBytes)
        {
            throw new HttpResourceBrokerException(
                "HTTP_RESPONSE_BODY_TOO_LARGE",
                $"HTTP response body exceeds the {maxBytes}-byte limit.");
        }

        await using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var buffer = new MemoryStream(Math.Min(maxBytes, 64 * 1024));
        var chunk = new byte[Math.Min(16 * 1024, maxBytes + 1)];
        while (true)
        {
            var remaining = maxBytes + 1 - checked((int)buffer.Length);
            if (remaining <= 0)
            {
                throw new HttpResourceBrokerException(
                    "HTTP_RESPONSE_BODY_TOO_LARGE",
                    $"HTTP response body exceeds the {maxBytes}-byte limit.");
            }

            var read = await stream.ReadAsync(
                    chunk.AsMemory(0, Math.Min(chunk.Length, remaining)),
                    cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            buffer.Write(chunk, 0, read);
            if (buffer.Length > maxBytes)
            {
                throw new HttpResourceBrokerException(
                    "HTTP_RESPONSE_BODY_TOO_LARGE",
                    $"HTTP response body exceeds the {maxBytes}-byte limit.");
            }
        }

        var encoding = Encoding.UTF8;
        var charset = content.Headers.ContentType?.CharSet?.Trim('"');
        if (!string.IsNullOrWhiteSpace(charset))
        {
            try
            {
                encoding = Encoding.GetEncoding(charset);
            }
            catch (ArgumentException)
            {
                encoding = Encoding.UTF8;
            }
        }

        return encoding.GetString(buffer.GetBuffer(), 0, checked((int)buffer.Length));
    }
}

internal sealed class HttpResourcePolicy
{
    private HttpResourcePolicy(
        int maxRedirects,
        int maxResponseBodyBytes,
        HashSet<string> allowedHosts,
        HashSet<int> allowedPorts,
        IReadOnlyList<HttpCidrBlock> additionalAllowedCidrs)
    {
        MaxRedirects = maxRedirects;
        MaxResponseBodyBytes = maxResponseBodyBytes;
        AllowedHosts = allowedHosts;
        AllowedPorts = allowedPorts;
        AdditionalAllowedCidrs = additionalAllowedCidrs;
    }

    public int MaxRedirects { get; }
    public int MaxResponseBodyBytes { get; }
    public HashSet<string> AllowedHosts { get; }
    public HashSet<int> AllowedPorts { get; }
    public IReadOnlyList<HttpCidrBlock> AdditionalAllowedCidrs { get; }

    public static HttpResourcePolicy Create(HttpResourceBrokerOptions? options)
    {
        options ??= new HttpResourceBrokerOptions();
        var allowedCidrs = new List<HttpCidrBlock>();
        foreach (var value in options.AdditionalAllowedCidrs ?? [])
        {
            if (!HttpCidrBlock.TryParse(value, out var cidr))
            {
                throw new InvalidOperationException(
                    $"Invalid HTTP resource CIDR in server configuration: '{value}'.");
            }

            allowedCidrs.Add(cidr!);
        }

        var hosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var host in options.AllowedHosts ?? [])
        {
            if (string.IsNullOrWhiteSpace(host))
            {
                throw new InvalidOperationException(
                    "HTTP resource host allowlist contains an empty entry.");
            }

            var normalizedHost = NormalizeHost(host);
            if (Uri.CheckHostName(normalizedHost) is not (
                    UriHostNameType.Dns or UriHostNameType.IPv4 or UriHostNameType.IPv6))
            {
                throw new InvalidOperationException(
                    $"Invalid HTTP resource host in server configuration: '{host}'.");
            }

            hosts.Add(normalizedHost);
        }

        var configuredPorts = options.AllowedPorts ?? [];
        if (configuredPorts.Any(port => port is < 1 or > 65535))
        {
            throw new InvalidOperationException(
                "HTTP resource port allowlist contains a value outside 1-65535.");
        }

        return new HttpResourcePolicy(
            Math.Clamp(options.MaxRedirects, 0, 20),
            Math.Clamp(options.MaxResponseBodyBytes, 1, 16 * 1024 * 1024),
            hosts,
            configuredPorts.ToHashSet(),
            allowedCidrs);
    }

    public static string NormalizeHost(string host)
    {
        var normalized = host.Trim().TrimEnd('.');
        if (IPAddress.TryParse(normalized, out var address))
        {
            return address.ToString();
        }

        return new IdnMapping().GetAscii(normalized);
    }
}

internal static class HttpResourceDestinationValidator
{
    public static Uri ParseAndValidateUri(string value, HttpResourcePolicy policy)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            !(uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
              uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) ||
            string.IsNullOrWhiteSpace(uri.Host) ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            uri.Port is < 1 or > 65535 ||
            uri.HostNameType is not (
                UriHostNameType.Dns or UriHostNameType.IPv4 or UriHostNameType.IPv6))
        {
            throw new HttpResourceBrokerException(
                "HTTP_DESTINATION_INVALID",
                "HTTP destination must be an absolute http/https URI with a valid host and port.");
        }

        var normalizedHost = HttpResourcePolicy.NormalizeHost(uri.IdnHost);
        if (policy.AllowedHosts.Count > 0 && !policy.AllowedHosts.Contains(normalizedHost))
        {
            throw new HttpResourceBrokerException(
                "HTTP_HOST_NOT_ALLOWED",
                "HTTP destination host is not present in the server allowlist.");
        }

        if (policy.AllowedPorts.Count > 0 && !policy.AllowedPorts.Contains(uri.Port))
        {
            throw new HttpResourceBrokerException(
                "HTTP_PORT_NOT_ALLOWED",
                "HTTP destination port is not present in the server allowlist.");
        }

        return uri;
    }

    public static Task<IReadOnlyList<IPAddress>> ResolveAndValidateAsync(
        Uri uri,
        IHttpResourceDnsResolver resolver,
        HttpResourcePolicy policy,
        CancellationToken cancellationToken) =>
        ResolveAndValidateAsync(uri.IdnHost, uri.Port, resolver, policy, cancellationToken);

    public static async Task<IReadOnlyList<IPAddress>> ResolveAndValidateAsync(
        string host,
        int port,
        IHttpResourceDnsResolver resolver,
        HttpResourcePolicy policy,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<IPAddress> addresses;
        try
        {
            addresses = await resolver.ResolveAsync(host, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new HttpResourceBrokerException(
                "HTTP_DNS_RESOLUTION_FAILED",
                "HTTP destination DNS resolution failed.",
                ex);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var distinct = addresses
            .Where(address => address != null)
            .Distinct()
            .ToArray();
        if (distinct.Length == 0)
        {
            throw new HttpResourceBrokerException(
                "HTTP_DNS_NO_ADDRESSES",
                "HTTP destination DNS resolution returned no addresses.");
        }

        foreach (var address in distinct)
        {
            if (IsForbiddenAddress(address, policy.AdditionalAllowedCidrs))
            {
                throw new HttpResourceBrokerException(
                    "HTTP_DESTINATION_CIDR_FORBIDDEN",
                    $"HTTP destination '{host}:{port}' resolved to a forbidden network range.");
            }
        }

        return distinct;
    }

    internal static bool IsForbiddenAddress(
        IPAddress address,
        IReadOnlyList<HttpCidrBlock>? additionalAllowedCidrs = null)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (HardForbiddenCidrs.Any(cidr => cidr.Contains(address)))
        {
            return true;
        }

        if (additionalAllowedCidrs?.Any(cidr => cidr.Contains(address)) == true)
        {
            return false;
        }

        return ServerApprovalRequiredCidrs.Any(cidr => cidr.Contains(address));
    }

    private static readonly HttpCidrBlock[] HardForbiddenCidrs =
    [
        HttpCidrBlock.Parse("0.0.0.0/8"),
        HttpCidrBlock.Parse("127.0.0.0/8"),
        HttpCidrBlock.Parse("169.254.0.0/16"),
        HttpCidrBlock.Parse("192.0.0.0/24"),
        HttpCidrBlock.Parse("192.0.2.0/24"),
        HttpCidrBlock.Parse("198.18.0.0/15"),
        HttpCidrBlock.Parse("198.51.100.0/24"),
        HttpCidrBlock.Parse("203.0.113.0/24"),
        HttpCidrBlock.Parse("224.0.0.0/4"),
        HttpCidrBlock.Parse("240.0.0.0/4"),
        HttpCidrBlock.Parse("::/128"),
        HttpCidrBlock.Parse("::1/128"),
        HttpCidrBlock.Parse("::/96"),
        HttpCidrBlock.Parse("100::/64"),
        HttpCidrBlock.Parse("2001:db8::/32"),
        HttpCidrBlock.Parse("2002::/16"),
        HttpCidrBlock.Parse("fe80::/10"),
        HttpCidrBlock.Parse("fec0::/10"),
        HttpCidrBlock.Parse("ff00::/8")
    ];

    private static readonly HttpCidrBlock[] ServerApprovalRequiredCidrs =
    [
        HttpCidrBlock.Parse("10.0.0.0/8"),
        HttpCidrBlock.Parse("100.64.0.0/10"),
        HttpCidrBlock.Parse("172.16.0.0/12"),
        HttpCidrBlock.Parse("192.168.0.0/16"),
        HttpCidrBlock.Parse("fc00::/7")
    ];
}

internal sealed class HttpCidrBlock
{
    private HttpCidrBlock(IPAddress network, int prefixLength)
    {
        Network = network;
        PrefixLength = prefixLength;
    }

    private IPAddress Network { get; }
    private int PrefixLength { get; }

    public static HttpCidrBlock Parse(string value) =>
        TryParse(value, out var result)
            ? result!
            : throw new FormatException($"Invalid CIDR '{value}'.");

    public static bool TryParse(string? value, out HttpCidrBlock? result)
    {
        result = null;
        var parts = value?.Split('/', StringSplitOptions.TrimEntries);
        if (parts is not { Length: 2 } ||
            !IPAddress.TryParse(parts[0], out var address) ||
            !int.TryParse(parts[1], out var prefixLength))
        {
            return false;
        }

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        var bitLength = address.AddressFamily == AddressFamily.InterNetwork ? 32 : 128;
        if (prefixLength < 0 || prefixLength > bitLength)
        {
            return false;
        }

        result = new HttpCidrBlock(address, prefixLength);
        return true;
    }

    public bool Contains(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (address.AddressFamily != Network.AddressFamily)
        {
            return false;
        }

        var addressBytes = address.GetAddressBytes();
        var networkBytes = Network.GetAddressBytes();
        var wholeBytes = PrefixLength / 8;
        var remainingBits = PrefixLength % 8;
        for (var index = 0; index < wholeBytes; index++)
        {
            if (addressBytes[index] != networkBytes[index])
            {
                return false;
            }
        }

        if (remainingBits == 0)
        {
            return true;
        }

        var mask = (byte)(0xff << (8 - remainingBits));
        return (addressBytes[wholeBytes] & mask) == (networkBytes[wholeBytes] & mask);
    }
}
