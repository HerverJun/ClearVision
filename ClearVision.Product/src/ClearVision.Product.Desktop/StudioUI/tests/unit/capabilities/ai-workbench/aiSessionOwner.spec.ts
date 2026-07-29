import { describe, expect, it, vi } from 'vitest';
import { ApiConflictError, ApiUnauthorizedError, type ApiTransport } from '@/platform/api';
import { createAiSessionOwner } from '@/capabilities/ai-workbench/aiSessionOwner';
import {
  aiOperationId,
  aiPlanOperationId,
  intentFixture,
  operationFixture,
  replayFixture,
  sessionFixture,
  snapshotFixture
} from './aiFixtures';

function createResponse() {
  return { operation: operationFixture(), session: sessionFixture() };
}

describe('route-scoped AiSessionOwner', () => {
  it('reconciles a lost Session create response through durable operation lookup', async () => {
    const api = {
      apiBaseUrl: 'http://localhost:5000/api',
      post: vi.fn(async () => { throw new Error('response lost'); }),
      get: vi.fn(async (path: string) => path.startsWith('ai/operations/') ? operationFixture() : sessionFixture())
    } as unknown as ApiTransport;
    const owner = createAiSessionOwner({ api, operationIdFactory: () => aiOperationId, now: () => 1 });

    await owner.start();

    expect(owner.state.value.phase).toBe('idle');
    expect(owner.state.value.session?.sessionId).toBe('session_01');
    expect(owner.state.value.operation?.clientOperationId).toBe(aiOperationId);
    expect(owner.diagnostics()).toMatchObject({ requestCount: 0, streamCount: 0, timerCount: 0, subscriptionCount: 0 });
    owner.dispose();
  });

  it('recovers a lost Plan create response by operation lookup and replay without a second create', async () => {
    let operationIndex = 0;
    const operationIds = [aiOperationId, aiPlanOperationId];
    const api = {
      apiBaseUrl: 'http://localhost:5000/api',
      post: vi.fn(async (path: string) => {
        if (path === 'ai/sessions') return createResponse();
        if (path === 'ai/agent-intent-router-runs') return intentFixture();
        if (path === 'ai/agent-plan-runs') throw new Error('create response lost');
        throw new Error(`Unexpected POST ${path}`);
      }),
      get: vi.fn(async (path: string) => {
        if (path.startsWith('ai/operations/')) return operationFixture('plan_run');
        if (path === 'ai/agent-runs/run_plan_01') return replayFixture();
        throw new Error(`Unexpected GET ${path}`);
      })
    } as unknown as ApiTransport;
    const owner = createAiSessionOwner({
      api,
      operationIdFactory: () => operationIds[operationIndex++] ?? aiPlanOperationId,
      now: () => 1
    });

    await owner.start();
    await owner.submitTask('检测冲压件表面划伤与压痕并输出缺陷位置', 'strict');

    expect(owner.state.value.plan?.planId).toBe('plan_fixture_01');
    expect(owner.state.value.phase).toBe('clarifying');
    expect(api.post).toHaveBeenCalledTimes(3);
    expect(api.get).toHaveBeenCalledWith('ai/operations/33333333-3333-4333-8333-333333333333?kind=plan_run', expect.anything());
    owner.dispose();
  });

  it('loads the canonical latest Snapshot on a 409 and does not overwrite it', async () => {
    const latest = snapshotFixture({
      revision: 9,
      confirmedPlanAnswers: [{
        questionId: 'q_defect_definition', field: 'defect_definition', value: '3 mm',
        origin: 'user', confidence: 1, resolved: true
      }],
      answerRevision: 4
    });
    const conflict = new ApiConflictError({
      url: 'http://localhost/api/ai/sessions/session_01/workspace-snapshot',
      status: 409,
      statusText: 'Conflict',
      payload: { errorCode: 'workspace_revision_conflict', publicMessage: 'conflict', latestSnapshot: latest },
      responseBody: ''
    });
    const api = {
      apiBaseUrl: 'http://localhost:5000/api',
      get: vi.fn(async (path: string) => path === 'ai/sessions/session_01'
        ? sessionFixture({ planRunId: 'run_plan_01', planRunStatus: 'completed' })
        : replayFixture()),
      post: vi.fn(async (path: string) => {
        if (path.includes('workspace-snapshot')) throw conflict;
        throw new Error(`Unexpected POST ${path}`);
      })
    } as unknown as ApiTransport;
    const owner = createAiSessionOwner({ api, requestedSessionId: 'session_01' });

    await owner.start();
    await owner.answerClarification({ defect_definition: '2 mm' });

    expect(owner.state.value.phase).toBe('session-conflict');
    expect(owner.state.value.session?.snapshot.revision).toBe(9);
    expect(owner.state.value.session?.snapshot.confirmedPlanAnswers[0]?.value).toBe('3 mm');
    expect(api.post).toHaveBeenCalledTimes(1);
    owner.dispose();
  });

  it('freezes after 401 while a newly mounted authenticated owner can resume', async () => {
    const unauthorized = new ApiUnauthorizedError({
      url: 'http://localhost/api/ai/sessions/session_01', status: 401, statusText: 'Unauthorized',
      payload: null, responseBody: ''
    });
    const blockedApi = {
      apiBaseUrl: 'http://localhost:5000/api',
      get: vi.fn(async () => { throw unauthorized; })
    } as unknown as ApiTransport;
    const blocked = createAiSessionOwner({ api: blockedApi, requestedSessionId: 'session_01' });
    await blocked.start();
    await blocked.retry();
    expect(blockedApi.get).toHaveBeenCalledTimes(1);
    expect(blocked.state.value.phase).toBe('offline-or-service-unavailable');
    blocked.dispose();

    const resumedApi = {
      apiBaseUrl: 'http://localhost:5000/api',
      get: vi.fn(async () => sessionFixture())
    } as unknown as ApiTransport;
    const resumed = createAiSessionOwner({ api: resumedApi, requestedSessionId: 'session_01' });
    await resumed.start();
    expect(resumed.state.value.phase).toBe('idle');
    resumed.dispose();
  });

  it('returns every imperative resource count to zero across 20 create/dispose cycles', async () => {
    for (let cycle = 0; cycle < 20; cycle += 1) {
      const api = {
        apiBaseUrl: 'http://localhost:5000/api',
        post: vi.fn((_path: string, _body: unknown, options?: { signal?: AbortSignal }) => new Promise((_resolve, reject) => {
          options?.signal?.addEventListener('abort', () => reject(new DOMException('aborted', 'AbortError')), { once: true });
        })),
        get: vi.fn()
      } as unknown as ApiTransport;
      const owner = createAiSessionOwner({ api, operationIdFactory: () => aiOperationId });
      const starting = owner.start();
      await Promise.resolve();
      expect(owner.diagnostics().requestCount).toBe(1);
      owner.dispose();
      await starting;
      expect(owner.diagnostics()).toEqual({
        requestCount: 0,
        streamCount: 0,
        timerCount: 0,
        subscriptionCount: 0,
        disposed: true
      });
    }
  });
});
