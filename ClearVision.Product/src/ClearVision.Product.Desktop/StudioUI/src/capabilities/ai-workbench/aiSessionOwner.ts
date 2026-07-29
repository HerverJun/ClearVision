import { computed, readonly, shallowRef, type ComputedRef, type DeepReadonly, type ShallowRef } from 'vue';
import { ApiAbortError, ApiHttpError, type ApiTransport } from '@/platform/api';
import type { AiOperationProjectionV1, AiSessionDetailV1 } from './contracts';
import { createAiWorkbenchApi, type AiWorkbenchApi } from './apiAdapter';
import { projectAiWorkbench, type AiWorkbenchProjection } from './projection';
import { createAiResourceLedger, type AiResourceLedgerDiagnostics } from './resourceLedger';
import { initialAiSessionState, reduceAiSession, type AiSessionEvent, type AiSessionState } from './reducer';

export interface CreateAiSessionOwnerOptions {
  readonly api: ApiTransport;
  readonly requestedSessionId?: string | null;
  readonly projectId?: string | null;
  readonly operationIdFactory?: () => string;
  readonly now?: () => number;
}

export interface AiSessionOwner {
  readonly state: DeepReadonly<ShallowRef<AiSessionState>>;
  readonly projection: ComputedRef<AiWorkbenchProjection>;
  start(): Promise<void>;
  retry(): Promise<void>;
  refresh(): Promise<void>;
  diagnostics(): AiResourceLedgerDiagnostics;
  dispose(): void;
}

interface PublicFailure {
  readonly errorCode: string;
  readonly message: string;
}

function publicFailure(error: unknown): PublicFailure {
  if (error instanceof ApiHttpError) {
    const payload = typeof error.payload === 'object' && error.payload !== null
      ? error.payload as Record<string, unknown>
      : null;
    const errorCode = typeof payload?.errorCode === 'string' && /^[a-z0-9_.:-]{1,96}$/i.test(payload.errorCode)
      ? payload.errorCode
      : `http_${error.status}`;
    const message = typeof payload?.publicMessage === 'string' && payload.publicMessage.trim()
      ? payload.publicMessage.trim()
      : error.status === 404
        ? '会话不存在或当前用户无权访问。'
        : '服务端未能确认会话状态，请稍后重试。';
    return Object.freeze({ errorCode, message });
  }
  return Object.freeze({
    errorCode: 'session_request_failed',
    message: '本地服务暂时不可用，请检查服务状态后重试。'
  });
}

export function createAiSessionOwner(options: CreateAiSessionOwnerOptions): AiSessionOwner {
  const api: AiWorkbenchApi = createAiWorkbenchApi(options.api);
  const state = shallowRef<AiSessionState>(initialAiSessionState);
  const ledger = createAiResourceLedger();
  const now = options.now ?? Date.now;
  const operationIdFactory = options.operationIdFactory ?? (() => globalThis.crypto.randomUUID());
  const requestedSessionId = options.requestedSessionId?.trim() || null;
  const projectId = options.projectId?.trim() || null;
  let currentSessionId = requestedSessionId;
  let createOperationId: string | null = null;
  let generation = 0;
  let disposed = false;

  function dispatch(event: AiSessionEvent): void {
    state.value = reduceAiSession(state.value, event);
  }

  async function request<T>(run: (signal: AbortSignal) => Promise<T>): Promise<T> {
    const controller = new AbortController();
    const release = ledger.trackRequest(controller);
    try {
      return await run(controller.signal);
    } finally {
      release();
    }
  }

  function accept(session: AiSessionDetailV1, operation: AiOperationProjectionV1 | null = null): void {
    currentSessionId = session.sessionId;
    dispatch({ type: 'ready', session, operation, at: now() });
  }

  async function reconcileCreate(operationId: string): Promise<boolean> {
    dispatch({ type: 'start', mode: 'hydrate', at: now() });
    const operation = await request(signal => api.getOperation(operationId, 'session_create', signal));
    if (operation.status !== 'created' || !operation.sessionId) return false;
    const session = await request(signal => api.getSession(operation.sessionId!, signal));
    accept(session, operation);
    return true;
  }

  async function start(): Promise<void> {
    if (disposed || state.value.phase === 'loading' || state.value.phase === 'recovering') return;
    const runGeneration = ++generation;
    const mode = currentSessionId ? 'hydrate' : 'create';
    dispatch({ type: 'start', mode, at: now() });
    try {
      if (currentSessionId) {
        const session = await request(signal => api.getSession(currentSessionId!, signal));
        if (!disposed && generation === runGeneration) accept(session);
        return;
      }

      createOperationId ??= operationIdFactory();
      const response = await request(signal => api.createSession({
        clientOperationId: createOperationId!,
        ...(projectId ? { projectId } : {})
      }, signal));
      if (disposed || generation !== runGeneration) return;
      if (response.session) {
        accept(response.session, response.operation);
        return;
      }
      if (await reconcileCreate(createOperationId)) return;
      throw new Error('The session operation is still pending.');
    } catch (error) {
      if (disposed || generation !== runGeneration || error instanceof ApiAbortError) return;
      if (!currentSessionId && createOperationId) {
        try {
          if (await reconcileCreate(createOperationId)) return;
        } catch (reconcileError) {
          if (reconcileError instanceof ApiAbortError || disposed) return;
        }
      }
      const failure = publicFailure(error);
      dispatch({ type: 'failed', errorCode: failure.errorCode, message: failure.message, at: now() });
    }
  }

  async function retry(): Promise<void> {
    if (disposed) return;
    dispatch({ type: 'retry', at: now() });
    await start();
  }

  async function refresh(): Promise<void> {
    if (disposed || !currentSessionId) return;
    dispatch({ type: 'retry', at: now() });
    await start();
  }

  return Object.freeze({
    state: readonly(state),
    projection: computed(() => projectAiWorkbench(state.value)),
    start,
    retry,
    refresh,
    diagnostics: ledger.diagnostics,
    dispose() {
      if (disposed) return;
      disposed = true;
      generation += 1;
      ledger.dispose();
      dispatch({ type: 'dispose', at: now() });
    }
  });
}
