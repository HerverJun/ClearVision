import { reactive } from 'vue';
import { describe, expect, it, vi } from 'vitest';
import type { ApiTransport } from '@/platform/api';
import type { WorkspacePersistenceOwner } from '@/capabilities/project-workspace/persistence';
import { createRuntimePackageExportOwner } from '@/capabilities/project-workspace/runtime-package';

describe('runtimePackageExportOwner', () => {
  it('saves dirty state first and never sends a Flow override', async () => {
    const projection = reactive({ dirty: true, canRun: false, persistenceRevision: 5 });
    const persistence = {
      projection,
      save: vi.fn(async () => {
        projection.dirty = false;
        projection.canRun = true;
        projection.persistenceRevision = 6;
        return { status: 'saved', project: null };
      })
    } as unknown as WorkspacePersistenceOwner;
    const post = vi.fn(async (path: string, body: unknown) => {
      expect(path).toContain('runtime-package/export');
      expect(body).toEqual({ registerForStationDeployment: true });
      return { packageRootPath: 'C:/packages/p', packageId: 'cvpkg-1', packageName: 'P', flowHash: 'flow-hash', decisionConfigurationHash: 'decision-hash', registeredForStationDeployment: true, stationPackageId: 'cvpkg-1', readmePath: null };
    });
    const owner = createRuntimePackageExportOwner({ projectId: crypto.randomUUID(), persistenceOwner: persistence, api: { apiBaseUrl: 'http://localhost/api', get: vi.fn(), post } as unknown as ApiTransport });
    await expect(owner.exportPackage()).resolves.toMatchObject({ packageId: 'cvpkg-1' });
    expect(persistence.save).toHaveBeenCalledOnce();
    expect(post.mock.calls[0]?.[1]).toEqual({ registerForStationDeployment: true });
    expect(post.mock.calls[0]?.[1]).not.toHaveProperty('flow');
    owner.dispose();
  });
});
