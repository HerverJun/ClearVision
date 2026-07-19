import { flushPromises, mount } from '@vue/test-utils';
import { reactive } from 'vue';
import { createMemoryHistory, createRouter } from 'vue-router';
import { describe, expect, it, vi } from 'vitest';
import type { SessionProjectionOwner } from '@/app/session/sessionProjectionOwner';
import { createProjectLifecycleCommandOwner } from '@/capabilities/project-lifecycle';
import {
  WorkspacePage,
  createWorkspaceLifecycleDiagnosticsOwner,
  createWorkspaceRuntime,
  workspaceCapabilityFlagKey,
  type WorkspaceOwner,
  type WorkspaceProjectV1,
  type WorkspaceRuntime
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

function createProtectedRunHarness(runPhase: 'executing' | 'cancel-requested' | 'unknown-outcome' = 'executing') {
  const diagnostics = createWorkspaceLifecycleDiagnosticsOwner({ publishToWindow: false });
  const project = {
    ...projectPayload(),
    opaqueProjectFields: {},
    saveCompatibility: {
      status: 'compatible',
      canEncode: true,
      opaquePassthroughPaths: [],
      blockedPaths: [],
      readOnlyUnknownPaths: []
    }
  } as unknown as WorkspaceProjectV1;
  const session = createSession();
  const persistence = reactive({
    phase: 'running',
    projectId: projectA,
    persistenceRevision: 3,
    dirtyGeneration: 0,
    submittedDirtyGeneration: null,
    dirty: false,
    canSave: false,
    canRun: false,
    canRetry: false,
    canReconcile: false,
    canReapplyConflict: false,
    canDiscardConflict: false,
    message: 'running',
    errorCode: null,
    conflictServerRevision: null,
    lastSavedAt: null
  });
  const run = reactive({
    phase: runPhase,
    projectId: projectA,
    clientSnapshotId: '33333333-3333-4333-8333-333333333333',
    admission: null,
    result: null,
    message: runPhase,
    errorCode: null,
    canRun: false,
    canStop: runPhase === 'executing',
    canReconcile: runPhase !== 'executing'
  });
  const owner = {
    projectId: projectA,
    projection: reactive({
      phase: 'ready',
      project,
      readonlyReason: null,
      persistence,
      run
    }),
    openFlowCanvas: vi.fn(),
    save: vi.fn(async () => ({ status: 'running', project: null })),
    retrySave: vi.fn(async () => ({ status: 'running', project: null })),
    reconcileSave: vi.fn(async () => ({ status: 'running', project: null })),
    reapplyConflict: vi.fn(),
    discardConflict: vi.fn(),
    runFormal: vi.fn(async () => null),
    stopFormal: vi.fn(async () => true),
    reconcileFormalRun: vi.fn(async () => null),
    prepareForLeave: vi.fn(async () => false),
    setReadonly: vi.fn(),
    dispose: vi.fn()
  } as unknown as WorkspaceOwner;
  const readState = reactive({ phase: 'success', data: project, failure: null });
  const readPort = {
    projectId: projectA,
    state: readState,
    refresh: vi.fn(async () => readState),
    dispose: vi.fn()
  };
  const runtime = {
    enabled: true,
    session: session.projection,
    diagnostics: diagnostics.diagnostics,
    refreshSession: vi.fn(async () => undefined),
    openProject: vi.fn(() => readPort),
    mountProject: vi.fn(() => owner),
    dispose: vi.fn()
  } as unknown as WorkspaceRuntime;
  return { diagnostics, owner, runtime };
}

describe('F03 G1 WorkspacePage', () => {
  it('confirms explicit open authority before issuing the Workspace Project GET', async () => {
    const sequence: string[] = [];
    const harness = await createHarness({
      get: async path => {
        sequence.push(`get:${path}`);
        return projectPayload(projectA);
      }
    });
    const commandApi: ApiTransport = {
      apiBaseUrl: 'http://localhost:5000/api',
      async get<T>(): Promise<T | undefined> { return undefined; },
      async post<T>(path: string): Promise<T | undefined> {
        sequence.push(`post:${path}`);
        return {
          projectId: projectA,
          lastOpenedAtUtc: '2026-07-19T00:00:00Z'
        } as T;
      },
      async put<T>(): Promise<T | undefined> { return undefined; }
    };
    const projectLifecycle = createProjectLifecycleCommandOwner({
      api: commandApi,
      publishToWindow: false
    });
    const wrapper = mount(WorkspacePage, {
      props: {
        projectId: projectA,
        runtime: harness.runtime,
        projectLifecycle
      },
      global: { plugins: [harness.router], stubs: { FlowWorkspace: flowWorkspaceStub } }
    });
    await flushPromises();

    expect(sequence).toEqual([
      `post:projects/${projectA}/open`,
      `get:projects/${projectA}`
    ]);
    expect(projectLifecycle.projection).toMatchObject({
      phase: 'succeeded',
      projectId: projectA
    });

    wrapper.unmount();
    projectLifecycle.dispose();
    harness.runtime.dispose();
    harness.queries.dispose();
  });

  it('does not install a second route, beforeunload, or Host-close guard', async () => {
    const harness = createProtectedRunHarness();
    const router = await createTestRouter();
    const wrapper = mount(WorkspacePage, {
      props: { projectId: projectA, runtime: harness.runtime },
      global: { plugins: [router], stubs: { FlowWorkspace: flowWorkspaceStub } }
    });
    await flushPromises();

    await router.push('/about');
    expect(router.currentRoute.value.fullPath).toBe('/about');
    expect(harness.owner.prepareForLeave).not.toHaveBeenCalled();
    expect(harness.owner.dispose).not.toHaveBeenCalled();

    await router.push(`/projects/${projectB}/workspace`);
    expect(router.currentRoute.value.fullPath).toBe(`/projects/${projectB}/workspace`);
    expect(harness.owner.prepareForLeave).not.toHaveBeenCalled();

    const flush = (window as Window & {
      __clearVisionFlushProjectWorkspace?: (reason?: string) => Promise<boolean>;
    }).__clearVisionFlushProjectWorkspace;
    expect(flush).toBeUndefined();

    wrapper.unmount();
    harness.runtime.dispose();
  });

  it('presents save and formal-run recovery commands in user-facing Chinese', async () => {
    const executingHarness = createProtectedRunHarness('executing');
    const executingRouter = await createTestRouter();
    const executing = mount(WorkspacePage, {
      props: { projectId: projectA, runtime: executingHarness.runtime },
      global: { plugins: [executingRouter], stubs: { FlowWorkspace: flowWorkspaceStub } }
    });
    await flushPromises();

    expect(executing.get('[data-testid="workspace-save"]').text()).toBe('保存');
    expect(executing.get('[data-testid="workspace-run"]').text()).toBe('运行');
    expect(executing.get('[data-testid="workspace-run-stop"]').text()).toBe('停止运行');
    expect(executing.text()).not.toContain('Formal Run:');
    executing.unmount();
    executingHarness.runtime.dispose();

    const reconcileHarness = createProtectedRunHarness('cancel-requested');
    const reconcileRouter = await createTestRouter();
    const reconciling = mount(WorkspacePage, {
      props: { projectId: projectA, runtime: reconcileHarness.runtime },
      global: { plugins: [reconcileRouter], stubs: { FlowWorkspace: flowWorkspaceStub } }
    });
    await flushPromises();

    expect(reconciling.get('[data-testid="workspace-run-reconcile"]').text()).toBe('查询运行结果');
    reconciling.unmount();
    reconcileHarness.runtime.dispose();
  });

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
    expect(wrapper.text()).toContain('未创建临时流程');
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
