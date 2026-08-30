using ClearVision.Product.Application.Security;
using ClearVision.Product.Core.DTOs;
using ClearVision.Product.Infrastructure.AI;

namespace ClearVision.Product.Tests.AI;

/// <summary>
/// Keeps historical single-owner test fixtures concise without restoring any
/// ownerless API to the production service contract.
/// </summary>
internal static class ConversationTestCompatibilityExtensions
{
    internal static readonly string OwnerHash =
        AuthenticatedOwnerResolver.ResolveOwnerHash("conversation-compatibility-tests");

    internal static AiFlowGenerationRequest WithTestOwner(this AiFlowGenerationRequest request) =>
        request with { OwnerHash = OwnerHash };

    internal static Task<string> HandleAsTestOwnerAsync(
        this GenerateFlowMessageHandler handler,
        string description,
        string? sessionId = null,
        string? existingFlowJson = null,
        string? hint = null,
        GenerateFlowMode mode = GenerateFlowMode.Auto,
        bool debugPrompt = false,
        string? requestId = null,
        IReadOnlyList<string>? attachments = null,
        string? requirementMode = null,
        AiTemplateSelectionInfo? templateSelection = null,
        VisionAgentBuildFromPlanRequest? buildFromPlan = null,
        bool useVisionAgentGenerateFlow = false,
        string? agentGenerateFlowMode = null,
        bool runtimePreviewConsent = false,
        Action<string, string>? onMessage = null,
        CancellationToken cancellationToken = default,
        Action<string>? onAgentRunCreated = null) =>
        handler.HandleAsync(
            description,
            sessionId,
            existingFlowJson,
            hint,
            mode,
            debugPrompt,
            requestId,
            attachments,
            requirementMode,
            templateSelection,
            buildFromPlan,
            useVisionAgentGenerateFlow,
            agentGenerateFlowMode,
            runtimePreviewConsent,
            ownerHash: OwnerHash,
            onMessage: onMessage,
            cancellationToken: cancellationToken,
            onAgentRunCreated: onAgentRunCreated);

    internal static ConversationContext PrepareContext(
        this IConversationalFlowService service,
        AiFlowGenerationRequest request) =>
        service.PrepareContext(
            string.IsNullOrWhiteSpace(request.OwnerHash) ? OwnerHash : request.OwnerHash,
            string.IsNullOrWhiteSpace(request.OwnerHash) ? request.WithTestOwner() : request);

    internal static ConversationSession GetOrCreateSession(
        this IConversationalFlowService service,
        string? sessionId) =>
        service.GetOrCreateSession(OwnerHash, sessionId);

    internal static void RecordAssistantResponse(
        this IConversationalFlowService service,
        string sessionId,
        string assistantMessage,
        string? latestFlowJson,
        string? latestCanvasFlowJson = null,
        ConversationTurnPayload? payload = null) =>
        service.RecordAssistantResponseWithPersistence(
            OwnerHash,
            sessionId,
            assistantMessage,
            latestFlowJson,
            latestCanvasFlowJson,
            payload);

    internal static ConversationSessionWriteResult RecordAssistantResponseWithPersistence(
        this IConversationalFlowService service,
        string sessionId,
        string assistantMessage,
        string? latestFlowJson,
        string? latestCanvasFlowJson = null,
        ConversationTurnPayload? payload = null) =>
        service.RecordAssistantResponseWithPersistence(
            OwnerHash,
            sessionId,
            assistantMessage,
            latestFlowJson,
            latestCanvasFlowJson,
            payload);

    internal static IReadOnlyList<ConversationSessionSummary> ListSessions(
        this IConversationalFlowService service) =>
        service.ListSessions(OwnerHash);

    internal static ConversationSession? GetSession(
        this IConversationalFlowService service,
        string sessionId) =>
        service.GetSession(OwnerHash, sessionId);

    internal static bool TryBackfillCanvasFlowJson(
        this IConversationalFlowService service,
        string sessionId,
        string canvasFlowJson) =>
        service.TryBackfillCanvasFlowJson(OwnerHash, sessionId, canvasFlowJson);

    internal static ConversationBackfillResult TryBackfillCanvasFlowJsonWithResult(
        this IConversationalFlowService service,
        string sessionId,
        string canvasFlowJson) =>
        service.TryBackfillCanvasFlowJsonWithResult(OwnerHash, sessionId, canvasFlowJson);

    internal static ConversationSession UpdateWorkspaceSnapshot(
        this IConversationalFlowService service,
        string sessionId,
        VisionAgentWorkspaceSnapshotUpdate update)
    {
        var result = service.TryInitializeWorkspaceSnapshot(OwnerHash, sessionId, update);
        return service.GetSession(OwnerHash, sessionId) ?? new ConversationSession
        {
            SessionId = sessionId,
            OwnerHash = OwnerHash,
            WorkspaceSnapshot = result.Snapshot,
            UpdatedAtUtc = DateTime.UtcNow
        };
    }

    internal static VisionAgentWorkspaceSnapshotMutationResult TryUpdateWorkspaceSnapshot(
        this IConversationalFlowService service,
        string sessionId,
        VisionAgentWorkspaceSnapshotUpdate update) =>
        service.TryInitializeWorkspaceSnapshot(OwnerHash, sessionId, update);

    internal static VisionAgentWorkspaceSnapshotMutationResult TryBeginAgentRun(
        this IConversationalFlowService service,
        string sessionId,
        string runId,
        string kind,
        string? clientMutationId = null) =>
        service.TryBeginAgentRun(OwnerHash, sessionId, runId, kind, clientMutationId);

    internal static VisionAgentWorkspaceSnapshotMutationResult ProjectBuildTerminal(
        this IConversationalFlowService service,
        VisionAgentTerminalProjectionRequest request) =>
        service.ProjectBuildTerminal(OwnerHash, request);

    internal static bool DeleteSession(
        this IConversationalFlowService service,
        string sessionId) =>
        service.DeleteSessionWithResult(OwnerHash, sessionId).Status ==
        ConversationSessionDeleteStatus.Deleted;

    internal static ConversationSessionDeleteResult DeleteSessionWithResult(
        this IConversationalFlowService service,
        string sessionId) =>
        service.DeleteSessionWithResult(OwnerHash, sessionId);
}
