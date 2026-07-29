import type { ApiTransport } from '@/platform/api';
import type {
  AiOperationKind,
  AiOperationProjectionV1,
  AiSessionCreateCommandV1,
  AiSessionCreateResponseV1,
  AiSessionDetailV1,
  AiWorkspaceSnapshotMutationV1
} from './contracts';
import {
  decodeAiOperationProjectionV1,
  decodeAiSessionCreateResponseV1,
  decodeAiSessionDetailV1,
  decodeAiSessionSnapshotV1
} from './decoder';

export interface AiWorkbenchApi {
  createSession(command: AiSessionCreateCommandV1, signal?: AbortSignal): Promise<AiSessionCreateResponseV1>;
  getSession(sessionId: string, signal?: AbortSignal): Promise<AiSessionDetailV1>;
  getOperation(clientOperationId: string, kind: AiOperationKind, signal?: AbortSignal): Promise<AiOperationProjectionV1>;
  updateWorkspaceSnapshot(
    sessionId: string,
    mutation: AiWorkspaceSnapshotMutationV1,
    signal?: AbortSignal
  ): Promise<ReturnType<typeof decodeAiSessionSnapshotV1>>;
}

const identifierPattern = /^[a-z0-9_-]{1,80}$/i;
const guidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;

function identifier(value: string, label: string, pattern = identifierPattern): string {
  const normalized = value.trim();
  if (!pattern.test(normalized)) throw new Error(`${label} is invalid.`);
  return normalized;
}

export function createAiWorkbenchApi(api: ApiTransport): AiWorkbenchApi {
  const signalOptions = (signal?: AbortSignal) => signal ? { signal } : {};
  return Object.freeze({
    async createSession(command: AiSessionCreateCommandV1, signal?: AbortSignal) {
      if (!api.post) throw new Error('Shared API transport does not support POST.');
      const payload = await api.post('ai/sessions', command, signalOptions(signal));
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
    async updateWorkspaceSnapshot(
      sessionId: string,
      mutation: AiWorkspaceSnapshotMutationV1,
      signal?: AbortSignal
    ) {
      if (!api.post) throw new Error('Shared API transport does not support POST.');
      const safeSessionId = encodeURIComponent(identifier(sessionId, 'sessionId'));
      const payload = await api.post<Record<string, unknown>>(
        `ai/sessions/${safeSessionId}/workspace-snapshot`,
        mutation,
        signalOptions(signal)
      );
      if (!payload || !('snapshot' in payload)) throw new Error('Workspace snapshot response is malformed.');
      return decodeAiSessionSnapshotV1(payload.snapshot, '$.snapshot');
    }
  });
}
