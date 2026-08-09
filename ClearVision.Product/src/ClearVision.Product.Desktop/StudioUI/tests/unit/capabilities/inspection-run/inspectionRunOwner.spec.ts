import { describe, expect, it, vi } from 'vitest';
import { createInspectionRunOwner, type InspectionRunApiPort, type InspectionRunIdentity, type InspectionRunState, type InspectionSseEvent, type InspectionSsePort } from '@/capabilities/inspection-run';
import { ApiConflictError, ApiNetworkError } from '@/platform/api';

const projectId = '11111111-1111-1111-1111-111111111111';
const identity: InspectionRunIdentity = { projectId, clientSnapshotId: '22222222-2222-2222-2222-222222222222',
  expectedPersistenceRevision: 7, expectedCanonicalFlowHash: 'sha256:flow', expectedDecisionConfigurationHash: 'sha256:decision' };
const idle: InspectionRunState = { projectId, status: 'Idle', isBusy: false, sessionId: null, startedAt: null, stoppedAt: null,
  clientSnapshotId: null, persistenceRevision: null, canonicalFlowHash: null, decisionConfigurationHash: null, executionSource: null,
  sessionType: null };
const running: InspectionRunState = { ...idle, status: 'Running', isBusy: true, sessionId: '33333333-3333-3333-3333-333333333333',
  clientSnapshotId: identity.clientSnapshotId, persistenceRevision: 7, canonicalFlowHash: 'sha256:flow',
  decisionConfigurationHash: 'sha256:decision', executionSource: 'PersistedProject', sessionType: 'ContinuousInspection' };

function harness(initial = idle) {
  let state = initial;
  let connection: Parameters<InspectionSsePort['connect']>[0] | null = null;
  const api: InspectionRunApiPort = { hydrate: vi.fn(async () => state), start: vi.fn(async requested => ({ ...requested,
    persistenceRevision: 7, canonicalFlowHash: 'sha256:flow', decisionConfigurationHash: 'sha256:decision', runMode: 'canonical-project' as const, cameraId: null,
    sessionId: running.sessionId!, sessionType: 'ContinuousInspection' as const })),
    stop: vi.fn(async () => { state = idle; }) };
  const sse: InspectionSsePort = { connect: vi.fn(options => { connection = options; options.onOpen();
    return new Promise<void>(resolve => options.signal.addEventListener('abort', () => resolve(), { once: true })); }) };
  return { api, sse, connection: () => connection, setState: (next: InspectionRunState) => { state = next; } };
}

describe('inspectionRunOwner', () => {
  it('hydrates authoritative state and restores the SSE connection', async () => {
    const h = harness(running); const owner = createInspectionRunOwner({ projectId, api: h.api, sse: h.sse });
    await owner.hydrate();
    expect(owner.projection.phase).toBe('running'); expect(owner.projection.runtime).toEqual(running);
    expect(owner.resources()).toEqual({ streams: 1, timers: 0, abortControllers: 1, subscriptions: 1 });
    owner.dispose(); await owner.settle();
  });

  it('starts only with persisted identity, projects events, and stops through projectId', async () => {
    const h = harness(); const owner = createInspectionRunOwner({ projectId, api: h.api, sse: h.sse });
    expect(await owner.start(identity)).toBe(true);
    expect(h.api.start).toHaveBeenCalledWith(identity, null, expect.anything());
    const event: InspectionSseEvent = { type: 'resultProduced', id: '9', result: {
      projectId, sessionId: running.sessionId!, resultId: 'r', imageId: null, status: 'OK',
      outcome: { execution: 'Succeeded', decision: 'Ok' }, decisionSource: null, reasonCode: null,
      hasJudgmentSignal: true, defectCount: 0, processingTimeMs: 4, errorMessage: null,
      outputData: null, analysisData: null, timestamp: new Date().toISOString()
    } };
    h.connection()?.onEvent(event); expect(owner.projection.latestResult?.resultId).toBe('r');
    h.setState(idle); expect(await owner.stop()).toBe(true); expect(h.api.stop).toHaveBeenCalledWith(identity, expect.anything());
    expect(owner.projection.phase).toBe('idle'); owner.dispose(); await owner.settle();
  });

  it('reconnects with the last event id and closes on a terminal event', async () => {
    vi.useFakeTimers(); const h = harness();
    const connections: Array<Parameters<InspectionSsePort['connect']>[0]> = [];
    const sse: InspectionSsePort = { connect: vi.fn(async options => {
      connections.push(options); options.onOpen();
      if (connections.length === 1) { options.onEvent({ type: 'heartbeat', id: '12' }); throw new Error('disconnected'); }
      await new Promise<void>(resolve => options.signal.addEventListener('abort', () => resolve(), { once: true }));
    }) };
    const owner = createInspectionRunOwner({ projectId, api: h.api, sse, retryDelaysMs: [10] });
    await owner.start(identity); await Promise.resolve(); await Promise.resolve();
    expect(owner.projection.phase).toBe('reconnecting');
    await vi.advanceTimersByTimeAsync(10);
    expect(connections[1]?.lastEventId).toBe('12');
    connections[1]?.onEvent({ type: 'stateChanged', id: '13', state: { projectId, sessionId: running.sessionId!, oldState: 'Running', newState: 'Stopped',
      errorMessage: null, timestamp: new Date().toISOString(), isSnapshot: false, startedAt: null, stoppedAt: new Date().toISOString(),
      sessionType: 'ContinuousInspection' } });
    expect(owner.projection.phase).toBe('idle');
    expect(owner.projection.runtime).toMatchObject({ status: 'Stopped', isBusy: false });
    owner.dispose(); await owner.settle(); vi.useRealTimers();
  });

  it('preserves stable 409 codes and rereads authority without starting again', async () => {
    const h = harness();
    vi.mocked(h.api.start).mockRejectedValueOnce(new ApiConflictError({
      url: 'http://localhost/api/inspection/realtime/start', status: 409, statusText: 'Conflict',
      payload: { Code: 'ADMISSION_SNAPSHOT_MISMATCH' }, responseBody: ''
    }));
    const owner = createInspectionRunOwner({ projectId, api: h.api, sse: h.sse });

    expect(await owner.start(identity)).toBe(false);
    expect(owner.projection.phase).toBe('faulted');
    expect(owner.projection.errorCode).toBe('ADMISSION_SNAPSHOT_MISMATCH');
    expect(h.api.hydrate).toHaveBeenCalledOnce();
    expect(h.api.start).toHaveBeenCalledOnce();
    owner.dispose();
  });

  it('hydrates formal-run occupancy without restoring stop authority or an SSE subscription', async () => {
    const formal: InspectionRunState = { ...running, sessionType: 'WorkspaceFormalRun' };
    const h = harness(formal);
    const owner = createInspectionRunOwner({ projectId, api: h.api, sse: h.sse });

    await owner.hydrate();

    expect(owner.projection.phase).toBe('occupied');
    expect(owner.projection.message).toContain('当前会话不属于连续检测');
    expect(owner.projection.message).not.toContain('已恢复后端连续检测运行状态');
    expect(owner.resources()).toEqual({ streams: 0, timers: 0, abortControllers: 0, subscriptions: 0 });
    expect(await owner.stop()).toBe(false);
    expect(h.api.stop).not.toHaveBeenCalled();
    owner.dispose();
  });

  it('stops an active continuous inspection before leave and confirms the backend is idle', async () => {
    const h = harness(running);
    const owner = createInspectionRunOwner({ projectId, api: h.api, sse: h.sse });

    await owner.hydrate();
    await expect(owner.prepareForLeave()).resolves.toBe(true);

    expect(h.api.stop).toHaveBeenCalledWith(identity, expect.anything());
    expect(owner.projection.runtime).toMatchObject({ status: 'Idle', isBusy: false });
    expect(owner.resources()).toEqual({ streams: 0, timers: 0, abortControllers: 0, subscriptions: 0 });

    owner.dispose();
    expect(h.api.stop).toHaveBeenCalledOnce();
  });

  it('blocks leave when the stop outcome is unknown and the backend remains busy', async () => {
    const h = harness(running);
    const owner = createInspectionRunOwner({ projectId, api: h.api, sse: h.sse });
    await owner.hydrate();
    vi.mocked(h.api.stop).mockRejectedValueOnce(new ApiNetworkError(
      'http://localhost/api/inspection/realtime/stop',
      new Error('offline')
    ));

    await expect(owner.prepareForLeave()).resolves.toBe(false);

    expect(owner.projection.runtime).toMatchObject({ status: 'Running', isBusy: true });
    expect(owner.projection.errorCode).toBe('INSPECTION_STOP_FAILED');
    expect(owner.resources()).toEqual({ streams: 0, timers: 0, abortControllers: 0, subscriptions: 0 });
    owner.dispose();
  });

  it('blocks leave when a busy continuous session has no complete stop identity', async () => {
    const incomplete: InspectionRunState = { ...running, clientSnapshotId: null };
    const h = harness(incomplete);
    const owner = createInspectionRunOwner({ projectId, api: h.api, sse: h.sse });
    await owner.hydrate();

    await expect(owner.prepareForLeave()).resolves.toBe(false);

    expect(h.api.stop).not.toHaveBeenCalled();
    expect(owner.projection.errorCode).toBe('INSPECTION_LEAVE_IDENTITY_MISSING');
    owner.dispose();
  });

  it('ignores replay events from an older session and resets the cursor for an explicit new start', async () => {
    const h = harness();
    const connections: Array<Parameters<InspectionSsePort['connect']>[0]> = [];
    const sse: InspectionSsePort = { connect: vi.fn(options => {
      connections.push(options); options.onOpen();
      return new Promise<void>(resolve => options.signal.addEventListener('abort', () => resolve(), { once: true }));
    }) };
    const owner = createInspectionRunOwner({ projectId, api: h.api, sse });

    await owner.start(identity);
    connections[0]?.onEvent({ type: 'resultProduced', id: '40', result: {
      projectId, sessionId: 'old-session', resultId: 'old-result', imageId: null, status: 'NG',
      outcome: { execution: 'Succeeded', decision: 'Ng' }, decisionSource: null, reasonCode: null,
      hasJudgmentSignal: true, defectCount: 1, processingTimeMs: 5, errorMessage: null,
      outputData: null, analysisData: null, timestamp: new Date().toISOString()
    } });
    expect(owner.projection.latestResult).toBeNull();

    h.setState(idle);
    await owner.stop();
    await owner.start({ ...identity, clientSnapshotId: '44444444-4444-4444-4444-444444444444' });
    expect(connections.at(-1)?.lastEventId).toBeNull();
    owner.dispose(); await owner.settle();
  });

  it('reconciles an unknown stop outcome against authoritative state', async () => {
    const h = harness(); const owner = createInspectionRunOwner({ projectId, api: h.api, sse: h.sse });
    await owner.start(identity);
    h.setState(running);
    vi.mocked(h.api.stop).mockRejectedValueOnce(new ApiNetworkError('http://localhost/api/inspection/realtime/stop', new Error('offline')));

    expect(await owner.stop()).toBe(false);
    expect(h.api.hydrate).toHaveBeenCalledWith(projectId, expect.anything());
    expect(owner.projection.phase).toBe('running');
    expect(owner.projection.errorCode).toBe('INSPECTION_STOP_FAILED');
    owner.dispose(); await owner.settle();
  });

  it('recovers a response-less start from authoritative state', async () => {
    const h = harness(running);
    vi.mocked(h.api.start).mockRejectedValueOnce(new ApiNetworkError('http://localhost/api/inspection/realtime/start', new Error('offline')));
    const owner = createInspectionRunOwner({ projectId, api: h.api, sse: h.sse });

    expect(await owner.start(identity)).toBe(false);
    expect(owner.projection.phase).toBe('running');
    expect(owner.projection.runtime).toEqual(running);
    expect(owner.projection.message).toContain('状态已恢复');
    owner.dispose(); await owner.settle();
  });

  it('drops a late authoritative start recovery after disposal', async () => {
    let resolveHydrate: ((value: InspectionRunState) => void) | undefined;
    const h = harness();
    vi.mocked(h.api.start).mockRejectedValueOnce(
      new ApiNetworkError('http://localhost/api/inspection/realtime/start', new Error('offline'))
    );
    vi.mocked(h.api.hydrate).mockImplementationOnce(() => new Promise<InspectionRunState>(resolve => {
      resolveHydrate = resolve;
    }));
    const owner = createInspectionRunOwner({ projectId, api: h.api, sse: h.sse });

    const start = owner.start(identity);
    await vi.waitFor(() => expect(h.api.hydrate).toHaveBeenCalledOnce());
    owner.dispose();
    resolveHydrate?.(running);

    await expect(start).resolves.toBe(false);
    expect(owner.projection).toMatchObject({ phase: 'disposed', errorCode: null });
    expect(owner.resources()).toEqual({ streams: 0, timers: 0, abortControllers: 0, subscriptions: 0 });
    await owner.settle();
  });

  it('backs off across repeated disconnects until a valid event resets the attempt', async () => {
    vi.useFakeTimers(); const h = harness(); let calls = 0;
    const sse: InspectionSsePort = { connect: vi.fn(async options => {
      calls += 1; options.onOpen();
      if (calls === 1) options.onEvent({ type: 'heartbeat', id: '1' });
      if (calls < 3) throw new Error('disconnected');
      await new Promise<void>(resolve => options.signal.addEventListener('abort', () => resolve(), { once: true }));
    }) };
    const owner = createInspectionRunOwner({ projectId, api: h.api, sse, retryDelaysMs: [10, 20] });

    await owner.start(identity); await Promise.resolve(); await Promise.resolve();
    expect(owner.projection.reconnectAttempt).toBe(1);
    await vi.advanceTimersByTimeAsync(10);
    expect(owner.projection.reconnectAttempt).toBe(2);
    await vi.advanceTimersByTimeAsync(20);
    expect(sse.connect).toHaveBeenCalledTimes(3);
    owner.dispose(); await owner.settle(); vi.useRealTimers();
  });

  it('drops duplicate and out-of-order SSE sequences before they can replace current statistics', async () => {
    const h = harness();
    const owner = createInspectionRunOwner({ projectId, api: h.api, sse: h.sse });
    await owner.start(identity);
    const resultEvent = (id: string, resultId: string): InspectionSseEvent => ({
      type: 'resultProduced',
      id,
      result: {
        projectId,
        sessionId: running.sessionId!,
        resultId,
        imageId: null,
        status: 'OK',
        outcome: { execution: 'Succeeded', decision: 'Ok' },
        decisionSource: 'FinalDecision',
        reasonCode: null,
        hasJudgmentSignal: true,
        defectCount: 0,
        processingTimeMs: 8,
        errorMessage: null,
        outputData: { score: 0.98 },
        analysisData: { threshold: 0.9 },
        timestamp: '2026-08-02T00:00:00Z'
      }
    });

    h.connection()?.onEvent(resultEvent('10', 'current'));
    h.connection()?.onEvent(resultEvent('9', 'stale'));
    h.connection()?.onEvent(resultEvent('10', 'duplicate'));

    expect(owner.projection.latestResult?.resultId).toBe('current');
    expect(owner.projection.recentResults).toHaveLength(1);
    expect(owner.projection.statistics).toMatchObject({
      total: 1,
      ok: 1,
      ng: 0,
      yieldRate: 1,
      decisionCoverageRate: 1,
      averageProcessingTimeMs: 8
    });
    owner.dispose();
    await owner.settle();
  });

  it('bounds SSE retries, rereads authority once, and never restarts the session', async () => {
    vi.useFakeTimers();
    const h = harness(running);
    const sse: InspectionSsePort = {
      connect: vi.fn(async options => {
        options.onOpen();
        throw new Error('disconnected');
      })
    };
    const owner = createInspectionRunOwner({
      projectId,
      api: h.api,
      sse,
      retryDelaysMs: [10, 20]
    });

    await owner.start(identity);
    await Promise.resolve();
    await vi.advanceTimersByTimeAsync(10);
    await vi.advanceTimersByTimeAsync(20);
    await vi.waitFor(() => expect(owner.projection.phase).toBe('disconnected'));

    expect(sse.connect).toHaveBeenCalledTimes(3);
    expect(h.api.hydrate).toHaveBeenCalledOnce();
    expect(h.api.start).toHaveBeenCalledOnce();
    expect(owner.projection.errorCode).toBe('INSPECTION_SSE_RETRY_EXHAUSTED');
    expect(owner.resources()).toEqual({ streams: 0, timers: 0, abortControllers: 0, subscriptions: 0 });
    owner.dispose();
    await owner.settle();
    vi.useRealTimers();
  });

  it('disposes every resource and survives 20 mount/unmount cycles without leaks', async () => {
    for (let cycle = 0; cycle < 20; cycle += 1) {
      const h = harness(running); const owner = createInspectionRunOwner({ projectId, api: h.api, sse: h.sse });
      await owner.hydrate(); owner.dispose(); await owner.settle();
      expect(owner.resources(), `cycle ${cycle}`).toEqual({ streams: 0, timers: 0, abortControllers: 0, subscriptions: 0 });
      expect(owner.projection.phase).toBe('disposed');
    }
  });
});
