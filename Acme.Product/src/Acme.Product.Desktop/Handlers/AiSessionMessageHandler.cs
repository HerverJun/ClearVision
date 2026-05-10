using System;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ConversationSessionSummary = Acme.Product.Infrastructure.AI.ConversationSessionSummary;
using IConversationalFlowService = Acme.Product.Infrastructure.AI.IConversationalFlowService;

namespace Acme.Product.Desktop.Handlers;

internal sealed class AiSessionMessageHandler
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IWebMessageClient _client;
    private readonly ILogger<AiSessionMessageHandler> _logger;

    public AiSessionMessageHandler(
        IServiceScopeFactory scopeFactory,
        IWebMessageClient client,
        ILogger<AiSessionMessageHandler> logger)
    {
        _scopeFactory = scopeFactory;
        _client = client;
        _logger = logger;
    }

    public Task HandleListAsync()
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<IConversationalFlowService>();
            var sessions = service.ListSessions();

            _client.SendProgressMessage("ListAiSessionsResult", new
            {
                success = true,
                sessions
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AiSessionMessageHandler] List AI sessions failed.");
            _client.SendProgressMessage("ListAiSessionsResult", new
            {
                success = false,
                errorMessage = ex.Message,
                sessions = Array.Empty<ConversationSessionSummary>()
            });
        }

        return Task.CompletedTask;
    }

    public Task HandleGetAsync(string messageJson)
    {
        try
        {
            var sessionId = ExtractSessionId(messageJson);
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                _client.SendProgressMessage("GetAiSessionResult", new
                {
                    success = false,
                    errorMessage = "sessionId is required."
                });
                return Task.CompletedTask;
            }

            using var scope = _scopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<IConversationalFlowService>();
            var session = service.GetSession(sessionId);

            _client.SendProgressMessage("GetAiSessionResult", new
            {
                success = session != null,
                sessionId,
                session,
                errorMessage = session == null ? "Session not found." : null
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AiSessionMessageHandler] Get AI session failed.");
            _client.SendProgressMessage("GetAiSessionResult", new
            {
                success = false,
                errorMessage = ex.Message
            });
        }

        return Task.CompletedTask;
    }

    public Task HandleDeleteAsync(string messageJson)
    {
        try
        {
            var sessionId = ExtractSessionId(messageJson);
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                _client.SendProgressMessage("DeleteAiSessionResult", new
                {
                    success = false,
                    errorMessage = "sessionId is required."
                });
                return Task.CompletedTask;
            }

            using var scope = _scopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<IConversationalFlowService>();
            var deleted = service.DeleteSession(sessionId);

            _client.SendProgressMessage("DeleteAiSessionResult", new
            {
                success = deleted,
                sessionId,
                errorMessage = deleted ? null : "Session not found."
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AiSessionMessageHandler] Delete AI session failed.");
            _client.SendProgressMessage("DeleteAiSessionResult", new
            {
                success = false,
                errorMessage = ex.Message
            });
        }

        return Task.CompletedTask;
    }

    private static string? ExtractSessionId(string messageJson)
    {
        using var doc = JsonDocument.Parse(messageJson);
        if (doc.RootElement.TryGetProperty("payload", out var payload) &&
            payload.ValueKind == JsonValueKind.Object &&
            (payload.TryGetProperty("sessionId", out var payloadSessionId) ||
             payload.TryGetProperty("SessionId", out payloadSessionId)))
        {
            return payloadSessionId.GetString();
        }

        if (doc.RootElement.TryGetProperty("sessionId", out var sessionId) ||
            doc.RootElement.TryGetProperty("SessionId", out sessionId))
        {
            return sessionId.GetString();
        }

        return null;
    }
}
