import { describe, expect, it, vi } from 'vitest';
import { ApiConflictError, ApiNetworkError } from '@/platform/api';
import { createAiHistoryController } from '@/capabilities/ai-workbench/aiHistoryController';
import type { AiWorkbenchApi } from '@/capabilities/ai-workbench/apiAdapter';
import type { AiOperationProjectionV1, AiSessionSummaryV1 } from '@/capabilities/ai-workbench/contracts';
import { aiOperationId, aiTimestamp, operationFixture } from './aiFixtures';

const session: AiSessionSummaryV1 = Object.freeze({
  sessionId: 'session_01',
  lifecycleState: 'plan_ready',
  projectId: null,
  revision: 7,
  updatedAtUtc: aiTimestamp
});

function operation(status: AiOperationProjectionV1['status']): AiOperationProjectionV1 {
  return Object.freeze({
    ...operationFixture(),
    kind: 'session_delete' as const,
    status,
    sessionId: session.sessionId
  });
}

function createController(apiOverrides: Partial<AiWorkbenchApi> = {}) {
  const api = {
    listSessions: vi.fn(async (offset: number, limit: number) => ({
      items: [session], offset, limit, total: 1
    })),
    listRuns: vi.fn(async (offset: number, limit: number) => ({ items: [], offset, limit, total: 0 })),
    deleteSession: vi.fn(async () => undefined),
    getOperation: vi.fn(async () => operation('created')),
    getSession: vi.fn(async () => { throw new Error('unexpected getSession'); }),
    ...apiOverrides
  } as unknown as AiWorkbenchApi;
  const controller = createAiHistoryController({
    api,
    execute: request => request(new AbortController().signal),
    operationIdFactory: () => aiOperationId
  });
  return { api, controller };
}

describe('AI history controller', () => {
  it('keeps Session and Run pagination independent and ignores a late Session page', async () => {
    let releaseFirst!: (value: { items: AiSessionSummaryV1[]; offset: number; limit: number; total: number }) => void;
    const first = new Promise<{ items: AiSessionSummaryV1[]; offset: number; limit: number; total: number }>(resolve => {
      releaseFirst = resolve;
    });
    const listSessions = vi.fn(async (offset: number, limit: number) => offset === 0
      ? first
      : { items: [{ ...session, sessionId: 'session_02' }], offset, limit, total: 12 });
    const { controller } = createController({ listSessions } as Partial<AiWorkbenchApi>);

    const stale = controller.loadSessions(0);
    await controller.loadSessions(10);
    await controller.loadRuns(0);
    releaseFirst({ items: [session], offset: 0, limit: 10, total: 12 });
    await stale;

    expect(controller.state.value.sessions.offset).toBe(10);
    expect(controller.state.value.sessions.items[0]?.sessionId).toBe('session_02');
    expect(controller.state.value.runsPhase).toBe('ready');
    controller.dispose();
  });

  it('reconciles a lost delete response through the original operation identity', async () => {
    const deleteSession = vi.fn(async () => {
      throw new ApiNetworkError('http://localhost/api/ai/sessions/session_01', new Error('response lost'));
    });
    const getOperation = vi.fn(async () => operation('created'));
    const { controller } = createController({ deleteSession, getOperation } as Partial<AiWorkbenchApi>);
    await controller.loadSessions();

    await expect(controller.deleteSession(session)).resolves.toBe(true);

    expect(getOperation).toHaveBeenCalledWith(aiOperationId, 'session_delete', expect.any(AbortSignal));
    expect(controller.state.value.deletePhase).toBe('deleted');
    expect(controller.state.value.sessions.total).toBe(0);
    controller.dispose();
  });

  it('fails closed when the server reports an active operation, artifact or staged draft', async () => {
    const deleteSession = vi.fn(async () => {
      throw new ApiConflictError({
        url: 'http://localhost/api/ai/sessions/session_01',
        status: 409,
        statusText: 'Conflict',
        payload: {
          errorCode: 'session_staged_draft_conflict',
          publicMessage: '此会话的候选已进入工作区暂存草稿，当前不能删除。'
        },
        responseBody: ''
      });
    });
    const { controller } = createController({ deleteSession } as Partial<AiWorkbenchApi>);

    await expect(controller.deleteSession(session)).resolves.toBe(false);

    expect(controller.state.value.deletePhase).toBe('blocked');
    expect(controller.state.value.errorCode).toBe('session_staged_draft_conflict');
    expect(controller.state.value.sessions.items).toHaveLength(0);
    controller.dispose();
  });

  it('does not publish a late page after disposal', async () => {
    let release!: (value: { items: AiSessionSummaryV1[]; offset: number; limit: number; total: number }) => void;
    const pending = new Promise<{ items: AiSessionSummaryV1[]; offset: number; limit: number; total: number }>(resolve => {
      release = resolve;
    });
    const { controller } = createController({ listSessions: vi.fn(async () => pending) } as Partial<AiWorkbenchApi>);
    const load = controller.loadSessions();
    controller.dispose();
    release({ items: [session], offset: 0, limit: 10, total: 1 });
    await load;

    expect(controller.state.value.sessions.items).toHaveLength(0);
  });
});
