import { readonly, shallowRef, type DeepReadonly, type ShallowRef } from 'vue';
import { ApiAbortError, ApiHttpError, ApiNetworkError, ApiNotFoundError, ApiServerError } from '@/platform/api';
import type { AiOperationProjectionV1, AiRunHistoryPageV1, AiSessionPageV1, AiSessionSummaryV1 } from './contracts';
import type { AiWorkbenchApi } from './apiAdapter';

export type AiHistoryLoadPhase = 'idle' | 'loading' | 'ready' | 'error';
export type AiHistoryDeletePhase = 'idle' | 'deleting' | 'unknown-outcome' | 'blocked' | 'failed' | 'deleted';

export interface AiHistoryState {
  readonly sessions: AiSessionPageV1;
  readonly runs: AiRunHistoryPageV1;
  readonly sessionsPhase: AiHistoryLoadPhase;
  readonly runsPhase: AiHistoryLoadPhase;
  readonly deletePhase: AiHistoryDeletePhase;
  readonly deletingSessionId: string | null;
  readonly deleteOperation: AiOperationProjectionV1 | null;
  readonly errorCode: string | null;
  readonly message: string;
}

export interface AiHistoryController {
  readonly state: DeepReadonly<ShallowRef<AiHistoryState>>;
  loadSessions(offset?: number): Promise<void>;
  loadRuns(offset?: number, sessionId?: string | null): Promise<void>;
  deleteSession(session: AiSessionSummaryV1): Promise<boolean>;
  reconcileDelete(): Promise<boolean>;
  dispose(): void;
}

const pageSize = 10;
const emptySessions: AiSessionPageV1 = Object.freeze({ items: Object.freeze([]), offset: 0, limit: pageSize, total: 0 });
const emptyRuns: AiRunHistoryPageV1 = Object.freeze({ items: Object.freeze([]), offset: 0, limit: pageSize, total: 0 });
const initialState: AiHistoryState = Object.freeze({
  sessions: emptySessions,
  runs: emptyRuns,
  sessionsPhase: 'idle',
  runsPhase: 'idle',
  deletePhase: 'idle',
  deletingSessionId: null,
  deleteOperation: null,
  errorCode: null,
  message: ''
});

function publicFailure(error: unknown): Readonly<{ errorCode: string; message: string; blocked: boolean }> {
  if (error instanceof ApiHttpError) {
    const payload = typeof error.payload === 'object' && error.payload !== null
      ? error.payload as Record<string, unknown>
      : null;
    const errorCode = typeof payload?.errorCode === 'string' && /^[a-z0-9_.:-]{1,96}$/i.test(payload.errorCode)
      ? payload.errorCode
      : `http_${error.status}`;
    const message = typeof payload?.publicMessage === 'string' && payload.publicMessage.trim()
      ? payload.publicMessage.trim()
      : error.status === 409
        ? '服务端拒绝了当前操作，请先处理运行、交接或暂存草稿。'
        : '历史记录请求未能完成，请稍后重试。';
    return Object.freeze({ errorCode, message, blocked: error.status === 409 });
  }
  return Object.freeze({
    errorCode: 'history_service_unavailable',
    message: '历史服务暂时不可用，请恢复本地服务后重试。',
    blocked: false
  });
}

export function createAiHistoryController(options: Readonly<{
  api: AiWorkbenchApi;
  execute<T>(request: (signal: AbortSignal) => Promise<T>): Promise<T>;
  operationIdFactory(): string;
}>): AiHistoryController {
  const state = shallowRef<AiHistoryState>(initialState);
  let disposed = false;
  let sessionsGeneration = 0;
  let runsGeneration = 0;
  let pendingDelete: Readonly<{ session: AiSessionSummaryV1; operationId: string }> | null = null;

  function patch(update: Partial<AiHistoryState>): void {
    if (disposed) return;
    state.value = Object.freeze({ ...state.value, ...update });
  }

  async function loadSessions(offset = 0): Promise<void> {
    if (disposed) return;
    const requestGeneration = ++sessionsGeneration;
    patch({ sessionsPhase: 'loading', errorCode: null, message: '' });
    try {
      const page = await options.execute(signal => options.api.listSessions(offset, pageSize, signal));
      if (disposed || requestGeneration !== sessionsGeneration) return;
      patch({ sessions: page, sessionsPhase: 'ready' });
    } catch (error) {
      if (disposed || requestGeneration !== sessionsGeneration || error instanceof ApiAbortError) return;
      const failure = publicFailure(error);
      patch({ sessionsPhase: 'error', errorCode: failure.errorCode, message: failure.message });
    }
  }

  async function loadRuns(offset = 0, sessionId: string | null = null): Promise<void> {
    if (disposed) return;
    const requestGeneration = ++runsGeneration;
    patch({ runsPhase: 'loading', errorCode: null, message: '' });
    try {
      const page = await options.execute(signal => options.api.listRuns(offset, pageSize, sessionId, signal));
      if (disposed || requestGeneration !== runsGeneration) return;
      patch({ runs: page, runsPhase: 'ready' });
    } catch (error) {
      if (disposed || requestGeneration !== runsGeneration || error instanceof ApiAbortError) return;
      const failure = publicFailure(error);
      patch({ runsPhase: 'error', errorCode: failure.errorCode, message: failure.message });
    }
  }

  function confirmDeleted(): boolean {
    const deletedSessionId = pendingDelete?.session.sessionId;
    if (!deletedSessionId) return false;
    pendingDelete = null;
    patch({
      sessions: Object.freeze({
        ...state.value.sessions,
        items: Object.freeze(state.value.sessions.items.filter(item => item.sessionId !== deletedSessionId)),
        total: Math.max(0, state.value.sessions.total - 1)
      }),
      deletePhase: 'deleted',
      deletingSessionId: deletedSessionId,
      deleteOperation: null,
      errorCode: null,
      message: '会话已由服务端安全删除。'
    });
    return true;
  }

  async function lookupDeleteOperation(): Promise<boolean> {
    if (!pendingDelete || disposed) return false;
    try {
      const operation = await options.execute(signal => options.api.getOperation(
        pendingDelete!.operationId,
        'session_delete',
        signal
      ));
      if (disposed || !pendingDelete || operation.clientOperationId !== pendingDelete.operationId) return false;
      patch({ deleteOperation: operation });
      if (operation.status === 'created') return confirmDeleted();
      if (operation.status === 'pending') {
        patch({
          deletePhase: 'unknown-outcome',
          errorCode: 'session_delete_unknown_outcome',
          message: '删除操作仍在服务端协调中，请继续核对，禁止重复删除。'
        });
        return false;
      }
      patch({
        deletePhase: operation.status === 'rejected' ? 'blocked' : 'failed',
        errorCode: operation.errorCode ?? 'session_delete_failed',
        message: operation.publicMessage ?? '服务端未完成会话删除。'
      });
      return false;
    } catch (error) {
      if (error instanceof ApiAbortError || disposed) return false;
      return false;
    }
  }

  async function reconcileDelete(): Promise<boolean> {
    if (!pendingDelete || disposed) return false;
    if (await lookupDeleteOperation()) return true;
    try {
      await options.execute(signal => options.api.getSession(pendingDelete!.session.sessionId, signal));
      patch({
        deletePhase: 'unknown-outcome',
        errorCode: 'session_delete_unknown_outcome',
        message: '会话仍存在；请继续核对原删除操作，禁止创建第二个删除请求。'
      });
      return false;
    } catch (error) {
      if (disposed || error instanceof ApiAbortError) return false;
      if (error instanceof ApiNotFoundError) return confirmDeleted();
      const failure = publicFailure(error);
      patch({ deletePhase: 'unknown-outcome', errorCode: failure.errorCode, message: failure.message });
      return false;
    }
  }

  async function deleteSession(session: AiSessionSummaryV1): Promise<boolean> {
    if (disposed || state.value.deletePhase === 'deleting' || state.value.deletePhase === 'unknown-outcome') return false;
    const operationId = options.operationIdFactory();
    pendingDelete = Object.freeze({ session, operationId });
    patch({
      deletePhase: 'deleting',
      deletingSessionId: session.sessionId,
      deleteOperation: null,
      errorCode: null,
      message: '正在由服务端检查运行、交接和暂存草稿。'
    });
    try {
      await options.execute(signal => options.api.deleteSession(
        session.sessionId,
        session.revision,
        operationId,
        signal
      ));
      return confirmDeleted();
    } catch (error) {
      if (disposed || error instanceof ApiAbortError) return false;
      if (error instanceof ApiNetworkError || error instanceof ApiServerError) {
        if (await lookupDeleteOperation()) return true;
        patch({
          deletePhase: 'unknown-outcome',
          errorCode: 'session_delete_unknown_outcome',
          message: '删除响应未能确认；请核对原操作，禁止重复删除。'
        });
        return false;
      }
      const failure = publicFailure(error);
      pendingDelete = null;
      patch({
        deletePhase: failure.blocked ? 'blocked' : 'failed',
        errorCode: failure.errorCode,
        message: failure.message
      });
      return false;
    }
  }

  return Object.freeze({
    state: readonly(state),
    loadSessions,
    loadRuns,
    deleteSession,
    reconcileDelete,
    dispose() {
      if (disposed) return;
      disposed = true;
      sessionsGeneration += 1;
      runsGeneration += 1;
      pendingDelete = null;
    }
  });
}
