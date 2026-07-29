import type { ApiTextStreamResponse, ApiTransport } from '@/platform/api';
import type {
  AiAgentRunReplayV1,
  AiIntentResultV1,
  AiOperationKind,
  AiOperationProjectionV1,
  AiPlanAnswerV1,
  AiPlanRunResponseV1,
  AiProjectContextV1,
  AiReadinessPreviewCommandV1,
  AiReadinessPreviewV1,
  AiRequirementMode,
  AiSessionCreateCommandV1,
  AiSessionCreateResponseV1,
  AiSessionDetailV1,
  AiSessionSnapshotV1,
  AiWorkspaceSnapshotMutationV1
} from './contracts';
import {
  decodeAiAgentRunReplayV1,
  decodeAiIntentResultV1,
  decodeAiOperationProjectionV1,
  decodeAiPlanRunResponseV1,
  decodeAiProjectContextV1,
  decodeAiReadinessPreviewV1,
  decodeAiSessionCreateResponseV1,
  decodeAiSessionDetailV1,
  decodeAiSessionSnapshotV1
} from './decoder';

export interface AiWorkbenchApi {
  createSession(command: AiSessionCreateCommandV1, signal?: AbortSignal): Promise<AiSessionCreateResponseV1>;
  getSession(sessionId: string, signal?: AbortSignal): Promise<AiSessionDetailV1>;
  getOperation(clientOperationId: string, kind: AiOperationKind, signal?: AbortSignal): Promise<AiOperationProjectionV1>;
  getProject(projectId: string, signal?: AbortSignal): Promise<AiProjectContextV1>;
  routeIntent(command: Readonly<{
    description: string;
    sessionId: string;
    requirementMode: AiRequirementMode;
    confirmedPlanAnswers: readonly AiPlanAnswerV1[];
    resolvedPlanFields: readonly string[];
    remainingPlanFields: readonly string[];
  }>, signal?: AbortSignal): Promise<AiIntentResultV1>;
  createPlanRun(command: Readonly<{
    clientOperationId: string;
    description: string;
    sessionId: string;
    requirementMode: AiRequirementMode;
    confirmedPlanAnswers: readonly AiPlanAnswerV1[];
    resolvedPlanFields: readonly string[];
    remainingPlanFields: readonly string[];
  }>, signal?: AbortSignal): Promise<AiPlanRunResponseV1>;
  getRunReplay(runId: string, signal?: AbortSignal): Promise<AiAgentRunReplayV1>;
  openRunEvents(runId: string, afterSequence: number, signal?: AbortSignal): Promise<ApiTextStreamResponse>;
  cancelPlanRun(runId: string, signal?: AbortSignal): Promise<void>;
  previewReadiness(command: AiReadinessPreviewCommandV1, signal?: AbortSignal): Promise<AiReadinessPreviewV1>;
  updateWorkspaceSnapshot(
    sessionId: string,
    mutation: AiWorkspaceSnapshotMutationV1,
    signal?: AbortSignal
  ): Promise<AiSessionSnapshotV1>;
}

const identifierPattern = /^[a-z0-9_.:-]{1,128}$/i;
const guidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;

function identifier(value: string, label: string, pattern = identifierPattern): string {
  const normalized = value.trim();
  if (!pattern.test(normalized)) throw new Error(`${label} is invalid.`);
  return normalized;
}

export function createAiWorkbenchApi(api: ApiTransport): AiWorkbenchApi {
  const signalOptions = (signal?: AbortSignal) => signal ? { signal } : {};
  const requirePost = () => {
    if (!api.post) throw new Error('Shared API transport does not support POST.');
    return api.post.bind(api);
  };

  return Object.freeze({
    async createSession(command: AiSessionCreateCommandV1, signal?: AbortSignal) {
      const payload = await requirePost()('ai/sessions', command, signalOptions(signal));
      return decodeAiSessionCreateResponseV1(payload);
    },
    async getSession(sessionId: string, signal?: AbortSignal) {
      const safeSessionId = encodeURIComponent(identifier(sessionId, 'sessionId'));
      return decodeAiSessionDetailV1(await api.get(`ai/sessions/${safeSessionId}`, signalOptions(signal)));
    },
    async getOperation(clientOperationId: string, kind: AiOperationKind, signal?: AbortSignal) {
      const safeOperationId = encodeURIComponent(identifier(clientOperationId, 'clientOperationId', guidPattern));
      const safeKind = encodeURIComponent(kind);
      return decodeAiOperationProjectionV1(
        await api.get(`ai/operations/${safeOperationId}?kind=${safeKind}`, signalOptions(signal))
      );
    },
    async getProject(projectId: string, signal?: AbortSignal) {
      const safeProjectId = encodeURIComponent(identifier(projectId, 'projectId', guidPattern));
      return decodeAiProjectContextV1(await api.get(`projects/${safeProjectId}`, signalOptions(signal)));
    },
    async routeIntent(command: Parameters<AiWorkbenchApi['routeIntent']>[0], signal?: AbortSignal) {
      const payload = await requirePost()('ai/agent-intent-router-runs', {
        description: command.description,
        originalUserPrompt: command.description,
        sessionId: command.sessionId,
        requirementMode: command.requirementMode,
        confirmedPlanAnswers: command.confirmedPlanAnswers,
        resolvedPlanFields: command.resolvedPlanFields,
        remainingPlanFields: command.remainingPlanFields,
        hasPendingPlan: false,
        pendingPlanHash: '',
        attachmentSummary: { count: 0, resourceKinds: [], pathsRedacted: true },
        metadataOnly: true
      }, signalOptions(signal));
      return decodeAiIntentResultV1(payload);
    },
    async createPlanRun(command: Parameters<AiWorkbenchApi['createPlanRun']>[0], signal?: AbortSignal) {
      const payload = await requirePost()('ai/agent-plan-runs', {
        clientOperationId: command.clientOperationId,
        description: command.description,
        originalUserPrompt: command.description,
        sessionId: command.sessionId,
        requirementMode: command.requirementMode,
        confirmedPlanAnswers: command.confirmedPlanAnswers,
        resolvedPlanFields: command.resolvedPlanFields,
        remainingPlanFields: command.remainingPlanFields,
        attachmentSummary: { count: 0, resourceKinds: [], pathsRedacted: true }
      }, signalOptions(signal));
      return decodeAiPlanRunResponseV1(payload);
    },
    async getRunReplay(runId: string, signal?: AbortSignal) {
      const safeRunId = encodeURIComponent(identifier(runId, 'runId'));
      return decodeAiAgentRunReplayV1(await api.get(`ai/agent-runs/${safeRunId}`, signalOptions(signal)));
    },
    async openRunEvents(runId: string, afterSequence: number, signal?: AbortSignal) {
      if (!api.getTextStream) throw new Error('Shared API transport does not support text streams.');
      const safeRunId = encodeURIComponent(identifier(runId, 'runId'));
      const safeSequence = Math.max(0, Math.trunc(afterSequence));
      return api.getTextStream(
        `ai/agent-runs/${safeRunId}/events?afterSequence=${safeSequence}`,
        signalOptions(signal)
      );
    },
    async cancelPlanRun(runId: string, signal?: AbortSignal) {
      const safeRunId = encodeURIComponent(identifier(runId, 'runId'));
      await requirePost()(`ai/agent-runs/${safeRunId}/cancel`, {}, signalOptions(signal));
    },
    async previewReadiness(command: AiReadinessPreviewCommandV1, signal?: AbortSignal) {
      return decodeAiReadinessPreviewV1(
        await requirePost()('ai/agent-plan/readiness-preview', command, signalOptions(signal))
      );
    },
    async updateWorkspaceSnapshot(
      sessionId: string,
      mutation: AiWorkspaceSnapshotMutationV1,
      signal?: AbortSignal
    ) {
      const safeSessionId = encodeURIComponent(identifier(sessionId, 'sessionId'));
      const payload = await requirePost()<Record<string, unknown>>(
        `ai/sessions/${safeSessionId}/workspace-snapshot`,
        mutation,
        signalOptions(signal)
      );
      if (!payload || !('snapshot' in payload)) throw new Error('Workspace snapshot response is malformed.');
      return decodeAiSessionSnapshotV1(payload.snapshot, '$.snapshot');
    }
  });
}
