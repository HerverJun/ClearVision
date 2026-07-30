import { describe, expect, it, vi } from 'vitest';
import { ApiConflictError, ApiUnauthorizedError, type ApiTransport } from '@/platform/api';
import { createAiSessionOwner } from '@/capabilities/ai-workbench/aiSessionOwner';
import {
  aiBuildOperationId,
  aiOperationId,
  aiPlanOperationId,
  aiProjectId,
  buildOperationFixture,
  buildParameterFixture,
  buildReplayFixture,
  buildResultFixture,
  buildRunResponseFixture,
  buildSessionFixture,
  existingProjectBaselineFixture,
  intentFixture,
  operationFixture,
  planRunResponseFixture,
  projectFixture,
  readyPlanReplayFixture,
  replayFixture,
  resourceDecisionFixture,
  resourceDecisionSelectionFixture,
  resourceRequirementFixture,
  sessionFixture,
  snapshotFixture
} from './aiFixtures';

function createResponse() {
  return { operation: operationFixture(), session: sessionFixture() };
}

function operationIds() {
  const ids = [aiOperationId, aiPlanOperationId, aiBuildOperationId];
  let index = 0;
  return () => ids[index++] ?? aiBuildOperationId;
}

function runningBuildReplayFixture() {
  const events: readonly unknown[] = [];
  return {
    summary: {
      runId: 'run_build_01', createdAt: '2026-07-29T08:00:00.000Z', updatedAt: '2026-07-29T08:00:00.000Z',
      status: 'running', title: 'Building', summary: 'Build is running.', firstFixRecommendation: '',
      lastSequence: 0, eventCount: 0, duplicateEventCount: 0, droppedEventCount: 0, staleEventCount: 0,
      ownerHash: 'redacted-owner', terminalIntent: null, metadataOnly: true, redactionPass: true, payload: null
    },
    events,
    snapshot: {
      storageVersion: 'agent-run-events.jsonl.v1', runId: 'run_build_01', generatedAt: '2026-07-29T08:00:00.000Z',
      firstSequence: 0, lastSequence: 0, eventCount: 0, metadataOnly: true, redactionPass: true, events
    },
    diagnostics: {
      runId: 'run_build_01', eventCount: 0, duplicateEventCount: 0, droppedEventCount: 0,
      staleEventCount: 0, metadataOnly: true, redactionPass: true
    }
  };
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

  it('reconciles a lost Build create response without issuing a duplicate POST', async () => {
    const post = vi.fn(async (path: string) => {
      if (path === 'ai/sessions') return createResponse();
      if (path === 'ai/agent-intent-router-runs') return intentFixture();
      if (path === 'ai/agent-plan-runs') return planRunResponseFixture();
      if (path === 'ai/agent-runs') throw new Error('Build create response lost');
      throw new Error(`Unexpected POST ${path}`);
    });
    const api = {
      apiBaseUrl: 'http://localhost:5000/api',
      post,
      get: vi.fn(async (path: string) => {
        if (path === 'ai/agent-runs/run_plan_01') return readyPlanReplayFixture();
        if (path === `ai/operations/${aiBuildOperationId}?kind=build_run`) return buildOperationFixture();
        if (path === 'ai/agent-runs/run_build_01') return buildReplayFixture();
        throw new Error(`Unexpected GET ${path}`);
      })
    } as unknown as ApiTransport;
    const owner = createAiSessionOwner({ api, operationIdFactory: operationIds(), now: () => 1 });

    await owner.start();
    await owner.submitTask('Detect surface scratches and report their locations.', 'strict');
    expect(owner.state.value.phase).toBe('plan-ready');
    await owner.startBuild();

    expect(owner.state.value.phase).toBe('parameters-pending');
    expect(owner.state.value.build?.clientOperationId).toBe(aiBuildOperationId);
    expect(owner.state.value.replayDiagnostics?.eventCount).toBe(1);
    expect(post.mock.calls.filter(([path]) => path === 'ai/agent-runs')).toHaveLength(1);
    expect(api.get).toHaveBeenCalledWith(
      `ai/operations/${aiBuildOperationId}?kind=build_run`, expect.anything()
    );
    owner.dispose();
  });

  it('rejects a mismatched Build response and surfaces an unknown outcome for reconciliation', async () => {
    const post = vi.fn(async (path: string) => {
      if (path === 'ai/sessions') return createResponse();
      if (path === 'ai/agent-intent-router-runs') return intentFixture();
      if (path === 'ai/agent-plan-runs') return planRunResponseFixture();
      if (path === 'ai/agent-runs') return buildRunResponseFixture({ sessionId: 'other_session' });
      throw new Error(`Unexpected POST ${path}`);
    });
    const api = {
      apiBaseUrl: 'http://localhost:5000/api',
      post,
      get: vi.fn(async (path: string) => {
        if (path === 'ai/agent-runs/run_plan_01') return readyPlanReplayFixture();
        if (path === `ai/operations/${aiBuildOperationId}?kind=build_run`) {
          return buildOperationFixture({ status: 'rejected', runId: null, publicMessage: 'Rejected mismatch.' });
        }
        throw new Error(`Unexpected GET ${path}`);
      })
    } as unknown as ApiTransport;
    const owner = createAiSessionOwner({ api, operationIdFactory: operationIds(), now: () => 1 });

    await owner.start();
    await owner.submitTask('Detect surface scratches and report their locations.', 'strict');
    await owner.startBuild();

    expect(owner.state.value.phase).toBe('unknown-outcome');
    expect(owner.state.value.build).toBeNull();
    expect(post.mock.calls.filter(([path]) => path === 'ai/agent-runs')).toHaveLength(1);
    owner.dispose();
  });

  it('rejects a replay workspace snapshot from another session', async () => {
    const post = vi.fn(async (path: string) => {
      if (path === 'ai/sessions') return createResponse();
      if (path === 'ai/agent-intent-router-runs') return intentFixture();
      if (path === 'ai/agent-plan-runs') return planRunResponseFixture();
      if (path === 'ai/agent-runs') throw new Error('Build create response lost');
      throw new Error(`Unexpected POST ${path}`);
    });
    const replay = buildReplayFixture();
    const event = replay.events[0]!;
    const payload = event.payload as Record<string, unknown>;
    const mismatched = { ...event, payload: { ...payload, sessionId: 'session_other' } };
    const api = {
      apiBaseUrl: 'http://localhost:5000/api',
      post,
      get: vi.fn(async (path: string) => {
        if (path === 'ai/agent-runs/run_plan_01') return readyPlanReplayFixture();
        if (path === `ai/operations/${aiBuildOperationId}?kind=build_run`) return buildOperationFixture();
        if (path === 'ai/agent-runs/run_build_01') return {
          ...replay,
          events: [mismatched],
          snapshot: { ...replay.snapshot, events: [mismatched] }
        };
        throw new Error(`Unexpected GET ${path}`);
      })
    } as unknown as ApiTransport;
    const owner = createAiSessionOwner({ api, operationIdFactory: operationIds(), now: () => 1 });

    await owner.start();
    await owner.submitTask('Detect surface scratches and report their locations.', 'strict');
    await owner.startBuild();

    expect(owner.state.value.phase).toBe('building');
    expect(owner.state.value.build).toBeNull();
    owner.dispose();
  });

  it('invalidates Validation after confirmed parameter and canonical resource mutations', async () => {
    const resource = resourceRequirementFixture();
    const parameter = buildParameterFixture({ pending: false, value: 128, hasExplicitValue: true, valueSummary: '128' });
    const build = buildResultFixture({ parameterMapping: [parameter], missingResources: [resource] });
    const initialSession = buildSessionFixture(build, { missingResources: [resource] });
    let resourceRevision = 0;
    const post = vi.fn(async (path: string, body: Record<string, unknown>) => {
      if (!path.includes('workspace-snapshot')) throw new Error(`Unexpected POST ${path}`);
      const decisions = body.resourceDecisions as Array<{ resourceKey: string }> | undefined;
      if (decisions) resourceRevision += 1;
      return { snapshot: snapshotFixture({
        ...initialSession.snapshot,
        revision: 3 + resourceRevision,
        lifecycleState: 'build_inputs_changed',
        resourceRevision,
        resourceDecisions: decisions ? [resourceDecisionFixture({ resourceKey: decisions[0]!.resourceKey })] : [],
        buildResult: build
      }) };
    });
    const api = {
      apiBaseUrl: 'http://localhost:5000/api',
      get: vi.fn(async (path: string) => {
        if (path === 'ai/sessions/session_01') return initialSession;
        if (path === 'ai/resource-candidates/camera-bindings') return [{
          id: 'camera-binding-01', displayName: 'Line camera', isEnabled: true
        }, {
          id: 'camera-binding-02', displayName: 'Backup camera', isEnabled: true
        }];
        throw new Error(`Unexpected GET ${path}`);
      }),
      post
    } as unknown as ApiTransport;
    const owner = createAiSessionOwner({ api, requestedSessionId: 'session_01', operationIdFactory: operationIds() });

    await owner.start();
    expect(owner.state.value.phase).toBe('resources-pending');
    await owner.updateResourceDecisions([resourceDecisionSelectionFixture()]);

    expect(owner.state.value.phase).toBe('build-blocked');
    expect(owner.state.value.buildStale).toBe(true);
    expect(owner.state.value.session?.snapshot.resourceRevision).toBe(1);
    await owner.updateResourceDecisions([resourceDecisionSelectionFixture({ resourceKey: 'camera-binding-02' })]);

    expect(owner.state.value.session?.snapshot.resourceRevision).toBe(2);
    const mutations = post.mock.calls.map(call => call[1] as Record<string, unknown>);
    expect(mutations[0]?.resourceDecisions).toEqual([resourceDecisionSelectionFixture()]);
    expect(mutations[1]?.resourceDecisions).toEqual([
      resourceDecisionSelectionFixture({ resourceKey: 'camera-binding-02' })
    ]);
    expect(mutations.every(mutation => !('resourceRevision' in mutation))).toBe(true);
    owner.dispose();
  });

  it('hydrates an older Build as stale after a resource revision changed while the route was closed', async () => {
    const build = buildResultFixture({ resourceRevision: 0 });
    const session = buildSessionFixture(build, { revision: 8, resourceRevision: 2 });
    const api = {
      apiBaseUrl: 'http://localhost:5000/api',
      get: vi.fn(async (path: string) => {
        if (path === 'ai/sessions/session_01') return session;
        throw new Error(`Unexpected GET ${path}`);
      })
    } as unknown as ApiTransport;
    const owner = createAiSessionOwner({ api, requestedSessionId: 'session_01' });

    await owner.start();

    expect(owner.state.value.phase).toBe('build-blocked');
    expect(owner.state.value.buildStale).toBe(true);
    expect(owner.state.value.build?.buildId).toBe(build.buildId);
    expect(owner.state.value.session?.snapshot.resourceRevision).toBe(2);
    owner.dispose();
  });

  it('rejects a revalidation response whose candidate identity changed', async () => {
    const build = buildResultFixture();
    const api = {
      apiBaseUrl: 'http://localhost:5000/api',
      get: vi.fn(async (path: string) => {
        if (path === 'ai/sessions/session_01') return buildSessionFixture(build);
        throw new Error(`Unexpected GET ${path}`);
      }),
      post: vi.fn(async (path: string) => {
        if (!path.endsWith('/revalidate')) throw new Error(`Unexpected POST ${path}`);
        const changed = buildResultFixture({ candidateFlowFingerprint: 'f'.repeat(64) });
        return {
          build: changed,
          snapshot: buildSessionFixture(changed, { revision: 4 }).snapshot,
          metadataOnly: true
        };
      })
    } as unknown as ApiTransport;
    const owner = createAiSessionOwner({ api, requestedSessionId: 'session_01', operationIdFactory: operationIds() });

    await owner.start();
    await owner.recheckReadiness();

    expect(owner.state.value.phase).toBe('build-failed');
    expect(owner.state.value.build?.candidateFlowFingerprint).toBe(build.candidateFlowFingerprint);
    owner.dispose();
  });

  it('rejects a recovered Build Snapshot whose Project baseline changed', async () => {
    const staleBaseline = {
      ...existingProjectBaselineFixture(), persistenceRevision: 13, canonicalFlowHash: '8'.repeat(64)
    };
    const staleBuild = buildResultFixture({ projectBaseline: staleBaseline });
    const api = {
      apiBaseUrl: 'http://localhost:5000/api',
      get: vi.fn(async (path: string) => {
        if (path === `projects/${aiProjectId}`) return projectFixture();
        if (path === `ai/projects/${aiProjectId}/baseline`) return existingProjectBaselineFixture();
        if (path === 'ai/sessions/session_01') {
          return buildSessionFixture(staleBuild, { projectId: aiProjectId });
        }
        throw new Error(`Unexpected GET ${path}`);
      })
    } as unknown as ApiTransport;
    const owner = createAiSessionOwner({
      api, requestedSessionId: 'session_01', projectId: aiProjectId
    });

    await owner.start();

    expect(owner.state.value.phase).toBe('offline-or-service-unavailable');
    expect(owner.state.value.build).toBeNull();
    expect(owner.state.value.projectBaseline).toEqual(existingProjectBaselineFixture());
    owner.dispose();
  });

  it('returns Build replay, SSE and request resources to zero across 20 dispose cycles', async () => {
    for (let cycle = 0; cycle < 20; cycle += 1) {
      const runningSession = sessionFixture({
        lifecycleState: 'building', buildRunId: 'run_build_01', buildRunStatus: 'running',
        buildClientOperationId: aiBuildOperationId, projectBaseline: buildResultFixture().projectBaseline
      });
      const api = {
        apiBaseUrl: 'http://localhost:5000/api',
        get: vi.fn(async (path: string) => path === 'ai/sessions/session_01'
          ? runningSession
          : runningBuildReplayFixture()),
        getTextStream: vi.fn(async () => ({
          stream: new ReadableStream<Uint8Array>({ start() { /* held open until owner disposal */ } }),
          headers: new Headers()
        }))
      } as unknown as ApiTransport;
      const owner = createAiSessionOwner({ api, requestedSessionId: 'session_01' });
      const starting = owner.start();
      await vi.waitFor(() => expect(owner.diagnostics().streamCount).toBeGreaterThan(0));
      owner.dispose();
      await starting;
      expect(owner.diagnostics()).toEqual({
        requestCount: 0, streamCount: 0, timerCount: 0, subscriptionCount: 0, disposed: true
      });
    }
  });
});
