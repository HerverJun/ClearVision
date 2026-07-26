import { describe, expect, it, vi } from 'vitest';
import { createInspectionRunOwner, type InspectionRunApiPort, type InspectionRunIdentity, type InspectionRunState, type InspectionSseEvent, type InspectionSsePort } from '@/capabilities/inspection-run';

const projectId = '11111111-1111-1111-1111-111111111111';
const identity: InspectionRunIdentity = { projectId, clientSnapshotId: '22222222-2222-2222-2222-222222222222',
  expectedPersistenceRevision: 7, expectedCanonicalFlowHash: 'sha256:flow', expectedDecisionConfigurationHash: 'sha256:decision' };
const idle: InspectionRunState = { projectId, status: 'Idle', isBusy: false, sessionId: null, startedAt: null, stoppedAt: null,
  clientSnapshotId: null, persistenceRevision: null, canonicalFlowHash: null, decisionConfigurationHash: null, executionSource: null };
const running: InspectionRunState = { ...idle, status: 'Running', isBusy: true, sessionId: '33333333-3333-3333-3333-333333333333',
  clientSnapshotId: identity.clientSnapshotId, persistenceRevision: 7, canonicalFlowHash: 'sha256:flow',
  decisionConfigurationHash: 'sha256:decision', executionSource: 'PersistedProject' };

function harness(initial = idle) {
  let state = initial;
  let connection: Parameters<InspectionSsePort['connect']>[0] | null = null;
  const api: InspectionRunApiPort = { hydrate: vi.fn(async () => state), start: vi.fn(async () => ({ ...identity,
    persistenceRevision: 7, canonicalFlowHash: 'sha256:flow', decisionConfigurationHash: 'sha256:decision', runMode: 'canonical-project' as const, cameraId: null })),
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
    const event: InspectionSseEvent = { type: 'resultProduced', id: '9', result: { projectId, sessionId: 's', resultId: 'r', status: 'OK',
      executionOutcome: 'Succeeded', decisionOutcome: 'OK', defectCount: 0, processingTimeMs: 4, errorMessage: null, timestamp: new Date().toISOString() } };
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
    connections[1]?.onEvent({ type: 'stateChanged', id: '13', state: { projectId, sessionId: 's', oldState: 'Running', newState: 'Stopped',
      errorMessage: null, timestamp: new Date().toISOString(), isSnapshot: false, startedAt: null, stoppedAt: new Date().toISOString() } });
    expect(owner.projection.phase).toBe('idle'); owner.dispose(); await owner.settle(); vi.useRealTimers();
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
