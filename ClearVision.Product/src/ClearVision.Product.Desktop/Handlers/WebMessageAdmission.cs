using System.Text.Json;
using ClearVision.Product.Application.Security;
using ClearVision.Product.Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ClearVision.Product.Desktop.Handlers;

internal static class WebMessageErrorCodes
{
    public const string AuthRequired = "auth-required";
    public const string Forbidden = "forbidden";
    public const string NotFound = "not-found";
    public const string Conflict = "conflict";
    public const string Validation = "validation-error";
}

internal sealed record AuthenticatedWebMessagePrincipal(
    string UserId,
    string Role,
    string OwnerHash);

internal sealed record WebMessageAdmissionResult(
    bool Allowed,
    string MessageType,
    string RequestId,
    string ClientBindingId,
    long NavigationEpoch,
    JsonElement Payload,
    AuthenticatedWebMessagePrincipal? Principal,
    string ErrorCode,
    string PublicMessage)
{
    public static WebMessageAdmissionResult Denied(
        string messageType,
        string requestId,
        string errorCode,
        string publicMessage,
        JsonElement payload = default) =>
        new(
            false,
            messageType,
            requestId,
            string.Empty,
            0,
            payload,
            null,
            errorCode,
            publicMessage);
}

internal sealed record WebMessageDeliveryBinding(
    long ServerEpoch,
    string OwnerHash,
    string ClientBindingId,
    long NavigationEpoch);

internal sealed record PendingWebMessage(string Json, WebMessageDeliveryBinding? Binding);

internal sealed class WebMessageAdmissionService
{
    public const string TrustedOrigin = "https://app.local";
    public const string BindingChangedMessageType = "BridgeBindingChanged";

    private static readonly IReadOnlyDictionary<string, string> Policies =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [nameof(ClearVision.Product.Contracts.Messages.PickFileCommand)] =
                ClearVisionAuthorizationPolicies.CanEditProject,
            ["ListAiSessions"] = ClearVisionAuthorizationPolicies.RequireAuthenticated,
            ["GetAiSession"] = ClearVisionAuthorizationPolicies.RequireAuthenticated,
            ["DeleteAiSession"] = ClearVisionAuthorizationPolicies.RequireAuthenticated,
            ["GenerateFlow"] = ClearVisionAuthorizationPolicies.RequireAuthenticated,
            ["CancelGenerateFlow"] = ClearVisionAuthorizationPolicies.RequireAuthenticated,
            ["planar2d:solve"] = ClearVisionAuthorizationPolicies.RequireAuthenticated,
            ["planar2d:save"] = ClearVisionAuthorizationPolicies.CanEditProject,
            [BindingChangedMessageType] = ClearVisionAuthorizationPolicies.RequireAuthenticated
        };

    private readonly IServiceScopeFactory _scopeFactory;

    public WebMessageAdmissionService(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task<WebMessageAdmissionResult> AdmitAsync(
        string messageJson,
        string? source,
        CancellationToken cancellationToken = default)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(messageJson);
        }
        catch (JsonException)
        {
            return WebMessageAdmissionResult.Denied(
                string.Empty,
                string.Empty,
                WebMessageErrorCodes.Validation,
                "WebMessage envelope is invalid.");
        }

        using (document)
        {
            var root = document.RootElement;
            var messageType = ReadString(root, "messageType", "MessageType", "type", "Type");
            var requestId = ReadString(root, "requestId", "RequestId", "id", "Id");
            var payload = root.TryGetProperty("payload", out var payloadElement)
                ? payloadElement.Clone()
                : root.Clone();

            if (WebMessageHandler.IsLegacyExecutionMessage(messageType))
            {
                return WebMessageAdmissionResult.Denied(
                    messageType,
                    requestId,
                    WebMessageErrorCodes.Forbidden,
                    "Legacy execution WebMessage is disabled.",
                    payload);
            }

            if (!IsTrustedOrigin(source))
            {
                return WebMessageAdmissionResult.Denied(
                    messageType,
                    requestId,
                    WebMessageErrorCodes.Forbidden,
                    "WebMessage origin is not allowed.",
                    payload);
            }

            if (!Policies.TryGetValue(messageType, out var policy))
            {
                return WebMessageAdmissionResult.Denied(
                    messageType,
                    requestId,
                    WebMessageErrorCodes.Forbidden,
                    "WebMessage command is not admitted.",
                    payload);
            }

            var bridge = root.TryGetProperty("bridge", out var bridgeElement) &&
                         bridgeElement.ValueKind == JsonValueKind.Object
                ? bridgeElement
                : default;
            var token = bridge.ValueKind == JsonValueKind.Object
                ? ReadString(bridge, "token", "Token")
                : string.Empty;
            var clientBindingId = bridge.ValueKind == JsonValueKind.Object
                ? ReadString(bridge, "bindingId", "BindingId")
                : string.Empty;
            var navigationEpoch = bridge.ValueKind == JsonValueKind.Object
                ? ReadLong(bridge, "navigationEpoch", "NavigationEpoch")
                : 0;

            if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(clientBindingId))
            {
                return WebMessageAdmissionResult.Denied(
                    messageType,
                    requestId,
                    WebMessageErrorCodes.AuthRequired,
                    "Authentication is required.",
                    payload);
            }

            using var scope = _scopeFactory.CreateScope();
            var authService = scope.ServiceProvider.GetRequiredService<IAuthService>();
            var session = await authService.GetSessionAsync(token);
            cancellationToken.ThrowIfCancellationRequested();
            if (session == null)
            {
                return WebMessageAdmissionResult.Denied(
                    messageType,
                    requestId,
                    WebMessageErrorCodes.AuthRequired,
                    "Authentication is required.",
                    payload);
            }

            if (!ClearVisionAuthorizationPolicies.IsAllowed(session.Role, policy))
            {
                return WebMessageAdmissionResult.Denied(
                    messageType,
                    requestId,
                    WebMessageErrorCodes.Forbidden,
                    "Permission denied.",
                    payload);
            }

            var ownerHash = AuthenticatedOwnerResolver.ResolveOwnerHash(session.UserId);
            if (string.IsNullOrWhiteSpace(ownerHash))
            {
                return WebMessageAdmissionResult.Denied(
                    messageType,
                    requestId,
                    WebMessageErrorCodes.AuthRequired,
                    "Authentication is required.",
                    payload);
            }

            return new WebMessageAdmissionResult(
                true,
                messageType,
                requestId,
                clientBindingId.Trim(),
                navigationEpoch,
                payload,
                new AuthenticatedWebMessagePrincipal(session.UserId, session.Role, ownerHash),
                string.Empty,
                string.Empty);
        }
    }

    public static bool IsTrustedOrigin(string? source)
    {
        if (!Uri.TryCreate(source, UriKind.Absolute, out var uri))
        {
            return false;
        }

        return uri.IsDefaultPort &&
               string.Equals(
                   uri.GetLeftPart(UriPartial.Authority),
                   TrustedOrigin,
                   StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadString(JsonElement element, params string[] names)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return string.Empty;
        }

        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString()?.Trim() ?? string.Empty;
            }
        }

        return string.Empty;
    }

    private static long ReadLong(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var value) && value.TryGetInt64(out var number))
            {
                return number;
            }
        }

        return 0;
    }
}
