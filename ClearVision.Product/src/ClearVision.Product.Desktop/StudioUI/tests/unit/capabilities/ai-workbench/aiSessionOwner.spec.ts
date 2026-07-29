import { describe, expect, it, vi } from 'vitest';
import type { ApiTransport } from '@/platform/api';
import { createAiSessionOwner } from '@/capabilities/ai-workbench/aiSessionOwner';

const timestamp = '2026-07-29T08:00:00.000Z';
const operationId = '11111111-1111-4111-8111-111111111111';

function session() {
  return {
    sessionId: 'session_01',
    snapshot: {
      schemaVersion: 1,
      revision: 1,
      projectId: null,
      lifecycleState: 'idle',
      planRunId: null,
      planRunStatus: null,
      buildRunId: null,
      buildRunStatus: null,
      buildClientOperationId: null,
      projectBaseline: null,
      updatedAtUtc: timestamp
    },
    updatedAtUtc: timestamp
  } as const;
}

function operation() {
  return {
    clientOperationId: operationId,
    kind: 'session_create',
    status: 'created',
    sessionId: 'session_01',
    runId: null,
    payloadFingerprint: `sha256:${'a'.repeat(64)}`,
    projectBaseline: null,
    errorCode: null,
    publicMessage: null,
    createdAtUtc: timestamp,
    updatedAtUtc: timestamp,
    expiresAtUtc: timestamp
  } as const;
}

describe('route-scoped AiSessionOwner', () => {
  it('reconciles a lost create response through durable operation lookup', async () => {
    const api = {
      apiBaseUrl: 'http://localhost:5000/api',
      post: vi.fn(async () => { throw new Error('response lost'); }),
      get: vi.fn(async (path: string) => path.startsWith('ai/operations/') ? operation() : session())
    } as unknown as ApiTransport;
    const owner = createAiSessionOwner({ api, operationIdFactory: () => operationId, now: () => 1 });

    await owner.start();

    expect(owner.state.value.phase).toBe('ready');
    expect(owner.state.value.session?.sessionId).toBe('session_01');
    expect(owner.state.value.operation?.clientOperationId).toBe(operationId);
    expect(owner.diagnostics()).toMatchObject({ requestCount: 0, streamCount: 0, timerCount: 0, subscriptionCount: 0 });
    owner.dispose();
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
      const owner = createAiSessionOwner({ api, operationIdFactory: () => operationId });
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
