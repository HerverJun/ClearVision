import { reactive } from 'vue';
import { describe, expect, it, vi } from 'vitest';
import { createInspectionRunPageOwner, type InspectionRunOwner } from '@/capabilities/inspection-run';
import type { ApiTransport } from '@/platform/api';

const projectId = '11111111-1111-1111-1111-111111111111';
const project = {
  id: projectId,
  name: '在线检测工程',
  description: '主线',
  version: '1.0.0',
  persistenceRevision: 12,
  createdAt: '2026-07-26T00:00:00Z',
  modifiedAt: null,
  lastOpenedAt: null,
  flow: null,
  assets: { schemaVersion: 1, calibrationAssets: [], spatialAssets: [] }
};

function harness() {
  const runProjection = reactive({ runtime: null, message: 'ready', errorCode: null });
  const run = {
    projection: runProjection,
    hydrate: vi.fn(async () => undefined),
    start: vi.fn(async () => true),
    stop: vi.fn(async () => true),
    dispose: vi.fn()
  } as unknown as InspectionRunOwner;
  const post = vi.fn(async (path: string, body: unknown) => {
    void path;
    void body;
    return {
    allowed: true,
    code: null,
    message: 'admitted',
    projectId,
    clientSnapshotId: '22222222-2222-2222-2222-222222222222',
    projectPersistenceRevision: 12,
    canonicalFlowHash: 'sha256:flow',
    decisionConfigurationHash: 'sha256:decision',
      violations: []
    };
  });
  const api = {
    apiBaseUrl: 'http://localhost/api',
    get: vi.fn(async (path: string) => path === 'cameras/bindings'
      ? [{ id: 'camera-a', displayName: '顶视相机', isEnabled: true, connectionStatus: 'Connected' }]
      : project),
    post
  } as unknown as ApiTransport;
  return { run, post, api, owner: createInspectionRunPageOwner({ projectId, api, run }) };
}

describe('inspectionRunPageOwner', () => {
  it('uses persisted admission identity and never sends FlowData', async () => {
    const h = harness();
    await h.owner.load();

    expect(await h.owner.start()).toBe(true);

    const [path, body] = h.post.mock.calls[0] ?? [];
    expect(path).toBe('inspection/admission');
    expect(body).toMatchObject({ projectId, expectedPersistenceRevision: 12 });
    expect(Object.keys(body as object).some(key => key.toLowerCase() === 'flowdata')).toBe(false);
    expect(h.run.start).toHaveBeenCalledWith(expect.objectContaining({
      projectId,
      expectedPersistenceRevision: 12,
      expectedCanonicalFlowHash: 'sha256:flow',
      expectedDecisionConfigurationHash: 'sha256:decision'
    }), 'camera-a');
    h.owner.dispose();
  });
});
