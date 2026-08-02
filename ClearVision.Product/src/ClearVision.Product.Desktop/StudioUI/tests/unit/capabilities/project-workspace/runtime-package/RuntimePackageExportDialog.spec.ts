import { mount } from '@vue/test-utils';
import { createMemoryHistory, createRouter } from 'vue-router';
import { describe, expect, it, vi } from 'vitest';
import RuntimePackageExportDialog from '@/capabilities/project-workspace/runtime-package/RuntimePackageExportDialog.vue';
import type { RuntimePackageExportOwner } from '@/capabilities/project-workspace/runtime-package';
import type { WorkspaceProjectV1 } from '@/capabilities/project-workspace/workspaceContracts';

describe('Runtime Package production trace link', () => {
  it('links a registered package to fleet authority by package, project and saved revision only', async () => {
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [
        { path: '/', component: { template: '<div />' } },
        { path: '/stations', component: { template: '<div />' } }
      ]
    });
    await router.push('/');
    const project = {
      id: '11111111-1111-4111-8111-111111111111',
      name: '瓶盖检测',
      persistenceRevision: 8
    } as unknown as WorkspaceProjectV1;
    const owner = {
      projection: {
        phase: 'success',
        result: {
          packageRootPath: 'C:\\ClearVision\\Packages\\package-a',
          packageId: 'package-a',
          packageName: '瓶盖检测包',
          flowHash: 'flow-hash',
          decisionConfigurationHash: 'decision-hash',
          registeredForStationDeployment: true,
          stationPackageId: 'station-package-a',
          readmePath: null
        },
        message: '运行包已生成。',
        canExport: true,
        requestedRevision: 8,
        requestedAtUtc: '2026-08-02T00:00:00Z'
      },
      exportPackage: vi.fn(async () => null),
      cancel: vi.fn(),
      dispose: vi.fn()
    } as unknown as RuntimePackageExportOwner;
    const wrapper = mount(RuntimePackageExportDialog, {
      props: { open: true, project, dirty: false, owner },
      global: { plugins: [router] },
      attachTo: document.body
    });

    const link = document.querySelector<HTMLAnchorElement>('[data-testid="runtime-package-open-stations"]');
    expect(link?.getAttribute('href')).toBe(
      '/stations?packageId=station-package-a&projectId=11111111-1111-4111-8111-111111111111&revision=8'
    );
    expect(document.body.innerHTML).not.toContain('packageRootPath=');
    wrapper.unmount();
  });
});
