using System;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ConversationSessionDeleteStatus = ClearVision.Product.Infrastructure.AI.ConversationSessionDeleteStatus;
using ConversationSession = ClearVision.Product.Infrastructure.AI.ConversationSession;
using ConversationSessionSummary = ClearVision.Product.Infrastructure.AI.ConversationSessionSummary;
using IConversationalFlowService = ClearVision.Product.Infrastructure.AI.IConversationalFlowService;
using MicrosoftLogger = Microsoft.Extensions.Logging.ILogger<ClearVision.Product.Desktop.Handlers.AiSessionMessageHandler>;

namespace ClearVision.Product.Desktop.Handlers;

internal sealed class AiSessionMessageHandler
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IWebMessageClient _client;
    private readonly MicrosoftLogger _logger;

    public AiSessionMessageHandler(
        IServiceScopeFactory scopeFactory,
        IWebMessageClient client,
        MicrosoftLogger logger)
    {
        _scopeFactory = scopeFactory;
        _client = client;
        _logger = logger;
    }

    public Task HandleListAsync(
        AuthenticatedWebMessagePrincipal principal,
        WebMessageDeliveryBinding binding)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<IConversationalFlowService>();
            var sessions = service.ListSessions(principal.OwnerHash);

            _client.SendBoundProgressMessage("ListAiSessionsResult", new
            {
                success = true,
                sessions
            }, binding);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AiSessionMessageHandler] List AI sessions failed.");
            _client.SendBoundProgressMessage("ListAiSessionsResult", new
            {
                success = false,
                code = WebMessageErrorCodes.Conflict,
                errorMessage = "Session list is temporarily unavailable.",
                sessions = Array.Empty<ConversationSessionSummary>()
            }, binding);
        }

        return Task.CompletedTask;
    }

    public Task HandleGetAsync(
        string messageJson,
        AuthenticatedWebMessagePrincipal principal,
        WebMessageDeliveryBinding binding)
    {
        var request = AiSessionRequestEnvelope.Empty;
        try
        {
            request = ExtractSessionRequest(messageJson);
            if (string.IsNullOrWhiteSpace(request.SessionId))
            {
                _client.SendBoundProgressMessage("GetAiSessionResult", new
                {
                    success = false,
                    code = WebMessageErrorCodes.Validation,
                    sessionId = request.SessionId,
                    requestId = request.RequestId,
                    navigationEpoch = request.NavigationEpoch,
                    errorMessage = "sessionId is required."
                }, binding);
                return Task.CompletedTask;
            }

            using var scope = _scopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<IConversationalFlowService>();
            var session = service.GetSession(principal.OwnerHash, request.SessionId);

            _client.SendBoundProgressMessage("GetAiSessionResult", new
            {
                success = session != null,
                code = session == null ? WebMessageErrorCodes.NotFound : null,
                sessionId = request.SessionId,
                requestId = request.RequestId,
                navigationEpoch = request.NavigationEpoch,
                session = session == null ? null : ProjectSession(session),
                errorMessage = session == null ? "Session not found." : null
            }, binding);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AiSessionMessageHandler] Get AI session failed.");
            _client.SendBoundProgressMessage("GetAiSessionResult", new
            {
                success = false,
                code = WebMessageErrorCodes.Conflict,
                sessionId = request.SessionId,
                requestId = request.RequestId,
                navigationEpoch = request.NavigationEpoch,
                errorMessage = "Session is temporarily unavailable."
            }, binding);
        }

        return Task.CompletedTask;
    }

    public Task HandleDeleteAsync(
        string messageJson,
        AuthenticatedWebMessagePrincipal principal,
        WebMessageDeliveryBinding binding)
    {
        try
        {
            var sessionId = ExtractSessionId(messageJson);
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                _client.SendBoundProgressMessage("DeleteAiSessionResult", new
                {
                    success = false,
                    code = WebMessageErrorCodes.Validation,
                    errorMessage = "sessionId is required."
                }, binding);
                return Task.CompletedTask;
            }

            using var scope = _scopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<IConversationalFlowService>();
            var deleteResult = service.DeleteSessionWithResult(principal.OwnerHash, sessionId);
            var deleted = deleteResult.Status == ConversationSessionDeleteStatus.Deleted;
            var errorMessage = deleteResult.Status switch
            {
                ConversationSessionDeleteStatus.Deleted => null,
                ConversationSessionDeleteStatus.PersistenceFailed => deleteResult.PersistenceStatus.PublicMessage,
                _ => "Session not found."
            };

            _client.SendBoundProgressMessage("DeleteAiSessionResult", new
            {
                success = deleted,
                code = deleteResult.Status switch
                {
                    ConversationSessionDeleteStatus.NotFound => WebMessageErrorCodes.NotFound,
                    ConversationSessionDeleteStatus.PersistenceFailed => WebMessageErrorCodes.Conflict,
                    _ => (string?)null
                },
                sessionId,
                persistenceStatus = deleteResult.PersistenceStatus,
                errorMessage
            }, binding);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AiSessionMessageHandler] Delete AI session failed.");
            _client.SendBoundProgressMessage("DeleteAiSessionResult", new
            {
                success = false,
                code = WebMessageErrorCodes.Conflict,
                errorMessage = "Session could not be deleted."
            }, binding);
        }

        return Task.CompletedTask;
    }

    private static string? ExtractSessionId(string messageJson)
    {
        return ExtractSessionRequest(messageJson).SessionId;
    }

    private static object ProjectSession(ConversationSession session) => new
    {
        session.SessionId,
        session.CurrentFlowJson,
        session.CurrentCanvasFlowJson,
        session.WorkspaceSnapshot,
        session.History,
        session.MutationReceipts,
        session.UpdatedAtUtc
    };

    private static AiSessionRequestEnvelope ExtractSessionRequest(string messageJson)
    {
        using var doc = JsonDocument.Parse(messageJson);
        var root = doc.RootElement;
        var source = root;
        if (root.TryGetProperty("payload", out var payload) &&
            payload.ValueKind == JsonValueKind.Object)
        {
            source = payload;
        }

        return new AiSessionRequestEnvelope(
            ReadString(source, "sessionId", "SessionId"),
            ReadString(source, "requestId", "RequestId"),
            ReadLong(source, "navigationEpoch", "NavigationEpoch"));
    }

    private static string? ReadString(JsonElement element, string camelName, string pascalName)
    {
        if (element.TryGetProperty(camelName, out var camel) ||
            element.TryGetProperty(pascalName, out camel))
        {
            return camel.GetString();
        }

        return null;
    }

    private static long ReadLong(JsonElement element, string camelName, string pascalName)
    {
        if ((element.TryGetProperty(camelName, out var value) ||
             element.TryGetProperty(pascalName, out value)) &&
            value.TryGetInt64(out var number))
        {
            return number;
        }

        return 0;
    }

    private sealed record AiSessionRequestEnvelope(
        string? SessionId,
        string? RequestId,
        long NavigationEpoch)
    {
        public static AiSessionRequestEnvelope Empty { get; } = new(null, null, 0);
    }
}
