import type { ApiTextStreamResponse, ApiTransport } from '@/platform/api';
import type {
  AiAgentRunReplayV1,
  AiBuildRevalidationResponseV1,
  AiBuildRunCommandV1,
  AiCameraBindingOptionV1,
  AiHandoffArtifactIdentityV1,
  AiHandoffCreateCommandV1,
  AiIntentResultV1,
  AiOperationKind,
  AiOperationProjectionV1,
  AiPlanAnswerV1,
  AiPlanRunResponseV1,
  AiProjectContextV1,
  AiProjectBaselineV1,
  AiReadinessPreviewCommandV1,
  AiReadinessPreviewV1,
  AiRequirementMode,
  AiRunHistoryPageV1,
  AiSessionCreateCommandV1,
  AiSessionCreateResponseV1,
  AiSessionDetailV1,
  AiSessionPageV1,
  AiSessionSnapshotV1,
  AiWorkspaceSnapshotMutationV1
} from './contracts';
import {
  decodeAiAgentRunReplayV1,
  decodeAiBuildRevalidationResponseV1,
  decodeAiCameraBindingOptionsV1,
  decodeAiHandoffArtifactIdentityV1,
  decodeAiIntentResultV1,
  decodeAiOperationProjectionV1,
  decodeAiPlanRunResponseV1,
  decodeAiProjectContextV1,
  decodeAiProjectBaselineV1,
  decodeAiReadinessPreviewV1,
  decodeAiRunHistoryPageV1,
  decodeAiSessionCreateResponseV1,
  decodeAiSessionDetailV1,
  decodeAiSessionPageV1,
  decodeAiSessionSnapshotV1
} from './decoder';

export interface AiWorkbenchApi {
  createSession(command: AiSessionCreateCommandV1, signal?: AbortSignal): Promise<AiSessionCreateResponseV1>;
  getSession(sessionId: string, signal?: AbortSignal): Promise<AiSessionDetailV1>;
  listSessions(offset: number, limit: number, signal?: AbortSignal): Promise<AiSessionPageV1>;
  listRuns(offset: number, limit: number, sessionId?: string | null, signal?: AbortSignal): Promise<AiRunHistoryPageV1>;
  deleteSession(
    sessionId: string,
    expectedRevision: number,
    clientMutationId: string,
    signal?: AbortSignal
  ): Promise<void>;
  getOperation(clientOperationId: string, kind: AiOperationKind, signal?: AbortSignal): Promise<AiOperationProjectionV1>;
  getProject(projectId: string, signal?: AbortSignal): Promise<AiProjectContextV1>;
  getProjectBaseline(projectId: string, signal?: AbortSignal): Promise<AiProjectBaselineV1>;
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
  createBuildRun(command: AiBuildRunCommandV1, signal?: AbortSignal): Promise<AiPlanRunResponseV1>;
  getRunReplay(runId: string, signal?: AbortSignal): Promise<AiAgentRunReplayV1>;
  openRunEvents(runId: string, afterSequence: number, signal?: AbortSignal): Promise<ApiTextStreamResponse>;
  cancelPlanRun(runId: string, signal?: AbortSignal): Promise<void>;
  revalidateBuild(command: Readonly<{
    runId: string;
    sessionId: string;
    expectedRevision: number;
    clientMutationId: string;
    buildId: string;
    candidateFlowFingerprint: string;
    answerRevision: number;
    resourceRevision: number;
  }>, signal?: AbortSignal): Promise<AiBuildRevalidationResponseV1>;
  listCameraBindings(signal?: AbortSignal): Promise<readonly AiCameraBindingOptionV1[]>;
  previewReadiness(command: AiReadinessPreviewCommandV1, signal?: AbortSignal): Promise<AiReadinessPreviewV1>;
  updateWorkspaceSnapshot(
    sessionId: string,
    mutation: AiWorkspaceSnapshotMutationV1,
    signal?: AbortSignal
  ): Promise<AiSessionSnapshotV1>;
  createHandoff(command: AiHandoffCreateCommandV1, signal?: AbortSignal): Promise<AiHandoffArtifactIdentityV1>;
  getHandoffByBuild(buildRunId: string, signal?: AbortSignal): Promise<AiHandoffArtifactIdentityV1>;
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
    async listSessions(offset: number, limit: number, signal?: AbortSignal) {
      const safeOffset = Math.max(0, Math.trunc(offset));
      const safeLimit = Math.min(100, Math.max(1, Math.trunc(limit)));
      return decodeAiSessionPageV1(await api.get(
        `ai/sessions?offset=${safeOffset}&limit=${safeLimit}`,
        signalOptions(signal)
      ));
    },
    async listRuns(offset: number, limit: number, sessionId?: string | null, signal?: AbortSignal) {
      const safeOffset = Math.max(0, Math.trunc(offset));
      const safeLimit = Math.min(100, Math.max(1, Math.trunc(limit)));
      const sessionQuery = sessionId
        ? `&sessionId=${encodeURIComponent(identifier(sessionId, 'sessionId'))}`
        : '';
      return decodeAiRunHistoryPageV1(await api.get(
        `ai/agent-runs?offset=${safeOffset}&limit=${safeLimit}${sessionQuery}`,
        signalOptions(signal)
      ));
    },
    async deleteSession(
      sessionId: string,
      expectedRevision: number,
      clientMutationId: string,
      signal?: AbortSignal
    ) {
      if (!api.delete) throw new Error('Shared API transport does not support DELETE.');
      const safeSessionId = encodeURIComponent(identifier(sessionId, 'sessionId'));
      const safeRevision = Math.max(0, Math.trunc(expectedRevision));
      const safeMutationId = encodeURIComponent(identifier(clientMutationId, 'clientMutationId', guidPattern));
      await api.delete(
        `ai/sessions/${safeSessionId}?expectedRevision=${safeRevision}&clientMutationId=${safeMutationId}`,
        signalOptions(signal)
      );
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
    async getProjectBaseline(projectId: string, signal?: AbortSignal) {
      const safeProjectId = encodeURIComponent(identifier(projectId, 'projectId', guidPattern));
      return decodeAiProjectBaselineV1(
        await api.get(`ai/projects/${safeProjectId}/baseline`, signalOptions(signal))
      );
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
    async createBuildRun(command: AiBuildRunCommandV1, signal?: AbortSignal) {
      const payload = await requirePost()('ai/agent-runs', {
        ...command,
        mode: command.buildFromPlan.buildIntent,
        useVisionAgentGenerateFlow: true,
        agentGenerateFlowMode: 'scripted',
        runtimePreviewConsent: false
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
    async revalidateBuild(command: Parameters<AiWorkbenchApi['revalidateBuild']>[0], signal?: AbortSignal) {
      const safeRunId = encodeURIComponent(identifier(command.runId, 'runId'));
      return decodeAiBuildRevalidationResponseV1(await requirePost()(
        `ai/agent-runs/${safeRunId}/revalidate`,
        {
          sessionId: command.sessionId,
          expectedRevision: command.expectedRevision,
          clientMutationId: command.clientMutationId,
          buildId: command.buildId,
          candidateFlowFingerprint: command.candidateFlowFingerprint,
          answerRevision: command.answerRevision,
          resourceRevision: command.resourceRevision
        },
        signalOptions(signal)
      ));
    },
    async listCameraBindings(signal?: AbortSignal) {
      return decodeAiCameraBindingOptionsV1(
        await api.get('ai/resource-candidates/camera-bindings', signalOptions(signal))
      );
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
    },
    async createHandoff(command: AiHandoffCreateCommandV1, signal?: AbortSignal) {
      return decodeAiHandoffArtifactIdentityV1(
        await requirePost()('ai/handoffs', command, signalOptions(signal))
      );
    },
    async getHandoffByBuild(buildRunId: string, signal?: AbortSignal) {
      const safeBuildRunId = encodeURIComponent(identifier(buildRunId, 'buildRunId'));
      return decodeAiHandoffArtifactIdentityV1(
        await api.get(`ai/handoffs/by-build/${safeBuildRunId}`, signalOptions(signal))
      );
    }
  });
}
