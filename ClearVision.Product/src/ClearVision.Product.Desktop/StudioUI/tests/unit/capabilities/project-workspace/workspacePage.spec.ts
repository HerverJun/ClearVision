import { flushPromises, mount } from '@vue/test-utils';
import { reactive } from 'vue';
import { createMemoryHistory, createRouter } from 'vue-router';
import { describe, expect, it, vi } from 'vitest';
import type { SessionProjectionOwner } from '@/app/session/sessionProjectionOwner';
import {
  WorkspacePage,
  createWorkspaceLifecycleDiagnosticsOwner,
  createWorkspaceRuntime,
  workspaceCapabilityFlagKey
} from '@/capabilities/project-workspace';
import {
  ApiForbiddenError,
  ApiNotFoundError,
  ApiUnauthorizedError,
  type ApiGetOptions,
  type ApiTransport
} from '@/platform/api';
import { createReadQueryClient } from '@/platform/query';

const flowWorkspaceStub = {
  name: 'FlowWorkspace',
  template: '<div data-testid="flow-workspace-stub" />'
};

const projectA = '11111111-1111-4111-8111-111111111111';
const projectB = '22222222-2222-4222-8222-222222222222';
const flowId = '33333333-3333-4333-8333-333333333333';

function projectPayload(projectId = projectA, overrides: Record<string, unknown> = {}) {
  return {
    id: projectId,
    name: projectId === projectA ? '工程 A' : '工程 B',
    description: null,
    version: '1.0.0',
    persistenceRevision: 3,
    flow: {
      id: flowId,
      name: '空流程',
      operators: [],
      connections: [],
      decisionConfiguration: null
    },
    globalSettings: {},
    globalVariables: {
      schemaVersion: '1.0',
      variables: [],
      sourceBindings: [],
      targetBindings: []
    },
    assets: {
      schemaVersion: 1,
      calibrationAssets: [],
      spatialAssets: []
    },
    createdAt: '2026-07-15T01:00:00Z',
    modifiedAt: null,
    lastOpenedAt: null,
    ...overrides
  };
}

function httpDetails(status: number) {
  return {
    url: `http://localhost:5000/api/projects/${projectA}`,
    status,
    statusText: 'test',
    payload: undefined,
    responseBody: ''
  };
}

function createSession(phase: 'authenticated' | 'unauthorized' = 'authenticated'): SessionProjectionOwner {
  const projection = reactive({
    phase,
    user: phase === 'authenticated'
      ? { userId: 'user-1', username: 'engineer', role: 'Engineer' }
      : null,
    sessionGeneration: 1,
    message: phase === 'authenticated' ? '会话有效' : '没有预置会话',
    updatedAt: Date.now()
  });
  return {
    projection,
    start: vi.fn(),
    refresh: vi.fn(async () => undefined),
    dispose: vi.fn()
  };
}

type GetImplementation = (path: string, options?: ApiGetOptions) => Promise<unknown>;

function apiWith(implementation: GetImplementation): ApiTransport {
  return {
    apiBaseUrl: 'http://localhost:5000/api',
    async get<T = unknown>(path: string, options?: ApiGetOptions): Promise<T | undefined> {
      return await implementation(path, options) as T | undefined;
    }
  };
}

function createTestRouter(projectId = projectA) {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/projects/:id/workspace', component: { template: '<div />' } },
      { path: '/projects/:id', component: { template: '<div />' } },
      { path: '/projects', component: { template: '<div />' } }
    ]
  });
  return router.push(`/projects/${projectId}/workspace`).then(() => router.isReady()).then(() => router);
}

async function createHarness(options: {
  enabled?: boolean;
  sessionPhase?: 'authenticated' | 'unauthorized';
  get?: GetImplementation;
} = {}) {
  const requests: string[] = [];
  const api = apiWith(async (path, requestOptions) => {
    requests.push(path);
    return await (options.get ?? (async requestedPath => {
      const id = requestedPath.split('/').at(-1) ?? projectA;
      return projectPayload(id);
    }))(path, requestOptions);
  });
  const queries = createReadQueryClient(api);
  const diagnostics = createWorkspaceLifecycleDiagnosticsOwner({ publishToWindow: false });
  const runtime = createWorkspaceRuntime({
    queries,
    session: createSession(options.sessionPhase),
    featureFlags: { [workspaceCapabilityFlagKey]: options.enabled ?? true },
    diagnostics
  });
  const router = await createTestRouter();
  return { requests, queries, diagnostics, runtime, router };
}

describe('F03 G1 WorkspacePage', () => {
  it('keeps flag-off at owner=0 and does not issue a Project GET', async () => {
    const harness = await createHarness({ enabled: false });
    const wrapper = mount(WorkspacePage, {
      props: { projectId: projectA, runtime: harness.runtime },
      global: { plugins: [harness.router], stubs: { FlowWorkspace: flowWorkspaceStub } }
    });
    await flushPromises();

    expect(wrapper.get('[data-evidence-surface="f03-workspace-shell"]').attributes())
      .toMatchObject({
        'data-workspace-state': 'flag-off',
        'data-workspace-owner-count': '0',
        'data-workspace-active-subscriptions': '0'
      });
    expect(harness.requests).toEqual([]);

    wrapper.unmount();
    harness.runtime.dispose();
    harness.queries.dispose();
  });

  it('mounts exactly one Workspace owner only after strict Project decode succeeds', async () => {
    const harness = await createHarness();
    const wrapper = mount(WorkspacePage, {
      props: { projectId: projectA, runtime: harness.runtime },
      global: { plugins: [harness.router], stubs: { FlowWorkspace: flowWorkspaceStub } }
    });
    await flushPromises();

    expect(wrapper.get('[data-evidence-surface="f03-workspace-shell"]').attributes())
      .toMatchObject({
        'data-workspace-state': 'empty',
        'data-workspace-owner-count': '1',
        'data-workspace-save-compatibility': 'compatible'
      });
    expect(harness.requests).toEqual([`projects/${projectA}`]);
    expect(harness.diagnostics.diagnostics).toMatchObject({
      workspaceOwnerCount: 1,
      activeSubscriptions: 1,
      inFlightReads: 0
    });

    wrapper.unmount();
    expect(harness.diagnostics.diagnostics).toMatchObject({
      workspaceOwnerCount: 0,
      activeSubscriptions: 0,
      activeAbortControllers: 0,
      inFlightReads: 0
    });
    harness.runtime.dispose();
    harness.queries.dispose();
  });

  it('disposes the old project before creating the next project owner', async () => {
    const harness = await createHarness();
    const wrapper = mount(WorkspacePage, {
      props: { projectId: projectA, runtime: harness.runtime },
      global: { plugins: [harness.router], stubs: { FlowWorkspace: flowWorkspaceStub } }
    });
    await flushPromises();
    expect(harness.diagnostics.diagnostics.activeProjectId).toBe(projectA);

    await wrapper.setProps({ projectId: projectB });
    await flushPromises();

    expect(harness.diagnostics.diagnostics).toMatchObject({
      workspaceOwnerCount: 1,
      activeProjectId: projectB,
      lastDisposedProjectId: projectA
    });
    expect(harness.diagnostics.diagnostics.lastDisposedResources).toEqual({
      activeSubscriptions: 0,
      activeTimers: 0,
      activeAnimationFrames: 0,
      activeObservers: 0,
      activeAbortControllers: 0,
      activeBlobUrls: 0,
      activePreviewArtifactIds: 0,
      activeHostSubscriptions: 0,
      inFlightReads: 0,
      inFlightWrites: 0,
      inFlightPreview: 0,
      inFlightExecute: 0
    });
    expect(harness.requests).toEqual([`projects/${projectA}`, `projects/${projectB}`]);

    wrapper.unmount();
    harness.runtime.dispose();
    harness.queries.dispose();
  });

  it.each([
    ['unauthorized', new ApiUnauthorizedError(httpDetails(401)), 'unauthorized'],
    ['forbidden/readonly', new ApiForbiddenError(httpDetails(403)), 'forbidden'],
    ['not-found', new ApiNotFoundError(httpDetails(404)), 'not-found']
  ] as const)('renders the %s state without creating an owner', async (_label, failure, state) => {
    const harness = await createHarness({ get: async () => { throw failure; } });
    const wrapper = mount(WorkspacePage, {
      props: { projectId: projectA, runtime: harness.runtime },
      global: { plugins: [harness.router], stubs: { FlowWorkspace: flowWorkspaceStub } }
    });
    await flushPromises();

    expect(wrapper.get('[data-evidence-surface="f03-workspace-shell"]').attributes())
      .toMatchObject({
        'data-workspace-state': state,
        'data-workspace-owner-count': '0'
      });
    expect(harness.diagnostics.diagnostics.workspaceOwnerCount).toBe(0);

    wrapper.unmount();
    harness.runtime.dispose();
    harness.queries.dispose();
  });

  it('renders Decode Error for malformed required fields and never invents fallback data', async () => {
    const harness = await createHarness({
      get: async () => ({ id: projectA, operatorCount: 40, connectionCount: 50 })
    });
    const wrapper = mount(WorkspacePage, {
      props: { projectId: projectA, runtime: harness.runtime },
      global: { plugins: [harness.router], stubs: { FlowWorkspace: flowWorkspaceStub } }
    });
    await flushPromises();

    expect(wrapper.get('[data-evidence-surface="f03-workspace-shell"]').attributes())
      .toMatchObject({
        'data-workspace-state': 'decode-error',
        'data-workspace-owner-count': '0',
        'data-workspace-save-compatibility': 'unavailable'
      });
    expect(wrapper.text()).toContain('未生成伪 Flow');
    expect(harness.diagnostics.diagnostics.workspaceOwnerCount).toBe(0);

    wrapper.unmount();
    harness.runtime.dispose();
    harness.queries.dispose();
  });

  it('passes 20 component mount/unmount cycles with owner and resources returning to zero', async () => {
    const harness = await createHarness();

    for (let cycle = 0; cycle < 20; cycle += 1) {
      const wrapper = mount(WorkspacePage, {
        props: { projectId: projectA, runtime: harness.runtime },
        global: { plugins: [harness.router], stubs: { FlowWorkspace: flowWorkspaceStub } }
      });
      await flushPromises();
      expect(harness.diagnostics.diagnostics.workspaceOwnerCount, `mount ${cycle}`).toBe(1);
      wrapper.unmount();
      expect(harness.diagnostics.diagnostics.workspaceOwnerCount, `unmount ${cycle}`).toBe(0);
      expect(harness.diagnostics.diagnostics.activeSubscriptions, `unmount ${cycle}`).toBe(0);
      expect(harness.diagnostics.diagnostics.activeAbortControllers, `unmount ${cycle}`).toBe(0);
      expect(harness.diagnostics.diagnostics.inFlightReads, `unmount ${cycle}`).toBe(0);
    }

    expect(harness.diagnostics.diagnostics).toMatchObject({
      totalWorkspaceMounts: 20,
      totalWorkspaceDisposals: 20,
      totalReadMounts: 20,
      totalReadDisposals: 20,
      ownerConflictCount: 0
    });
    harness.runtime.dispose();
    harness.queries.dispose();
  });
});
