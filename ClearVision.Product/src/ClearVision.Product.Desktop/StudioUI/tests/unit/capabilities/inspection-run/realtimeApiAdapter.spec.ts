import { describe, expect, it, vi } from 'vitest';
import { createInspectionRunApiAdapter, type InspectionRunIdentity } from '@/capabilities/inspection-run';
import type { ApiTransport } from '@/platform/api';

describe('inspection realtime API adapter', () => {
  it('starts a saved canonical Project without a browser draft payload', async () => {
    const post = vi.fn(async (path: string, body: unknown) => {
      void path; void body;
      return { projectId: 'p', clientSnapshotId: 's', persistenceRevision: 3,
        canonicalFlowHash: 'flow', decisionConfigurationHash: 'decision', runMode: 'canonical-project', cameraId: null };
    });
    const api = { apiBaseUrl: 'http://localhost/api', get: vi.fn(), post } as ApiTransport;
    const identity: InspectionRunIdentity = { projectId: 'p', clientSnapshotId: 's', expectedPersistenceRevision: 3,
      expectedCanonicalFlowHash: 'flow', expectedDecisionConfigurationHash: 'decision' };
    await createInspectionRunApiAdapter(api).start(identity, null);
    const body = post.mock.calls[0]?.[1] as Record<string, unknown>;
    expect(body).toEqual({ ...identity, cameraId: null, runMode: 'canonical-project' });
    expect(Object.keys(body).some(key => key.toLowerCase() === ['flow', 'data'].join(''))).toBe(false);
  });
});
