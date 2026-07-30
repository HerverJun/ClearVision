import { flushPromises, mount } from '@vue/test-utils';
import { reactive } from 'vue';
import { createMemoryHistory, createRouter } from 'vue-router';
import { describe, expect, it, vi } from 'vitest';
import type { ProjectLifecycleCommandOwner } from '@/capabilities/project-lifecycle';
import {
  WorkspacePage,
  decodeWorkspaceHandoffArtifactV1,
  decodeWorkspaceProjectV1,
  type WorkspaceNewDraftOwner,
  type WorkspaceOwner,
  type WorkspaceRuntime
} from '@/capabilities/project-workspace';
import { artifactId, candidateFlowFixture, handoffArtifactPayload } from './handoffFixtures';

const projectId = '99999999-9999-4999-8999-999999999999';

function rawProject() {
  return {
    id: projectId,
    name: 'AI 候选工程',
    description: null,
    version: '1.0.0',
    persistenceRevision: 0,
    flow: null,
    globalSettings: {},
    globalVariables: { schemaVersion: '1.0', variables: [], sourceBindings: [], targetBindings: [] },
    assets: { schemaVersion: 1, calibrationAssets: [], spatialAssets: [] },
    createdAt: '2026-07-29T08:10:00.000Z',
    modifiedAt: null,
    lastOpenedAt: null
  };
}

async function routerForNew() {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/projects/:id/workspace', name: 'project-workspace', component: { template: '<div />' } },
      { path: '/projects/:id', component: { template: '<div />' } },
      { path: '/projects', component: { template: '<div />' } }
    ]
  });
  await router.push(`/projects/new/workspace?handoff=${artifactId}`);
  await router.isReady();
  return router;
}

function createNewOwner(): WorkspaceNewDraftOwner {
  const artifact = decodeWorkspaceHandoffArtifactV1(handoffArtifactPayload());
  const projection = reactive({
    phase: 'ready' as const,
    project: { id: null, name: 'AI 候选工程', description: null, flow: null },
    handoff: null as WorkspaceNewDraftOwner['projection']['handoff'],
    savePhase: 'workspace-staged-unsaved' as WorkspaceNewDraftOwner['projection']['savePhase'],
    message: '等待候选',
    canSave: false,
    metadataLocked: false
  });
  return {
    projectId: null,
    projection,
    openFlowCanvas: vi.fn(),
    getFlowCanvasOwner: vi.fn(() => null),
    isDirty: vi.fn(() => projection.handoff !== null),
    setMetadata: vi.fn(input => {
      projection.project = { ...projection.project, ...input };
    }),
    stageHandoffDraft: vi.fn(async () => {
      projection.handoff = {
        phase: 'workspace-staging', source: null, build: artifact.build, message: 'staging'
      };
    }),
    confirmHandoff: vi.fn(source => {
      projection.handoff = {
        phase: 'workspace-staged-unsaved', source, build: artifact.build, message: '尚未保存'
      };
      projection.canSave = true;
      projection.message = '确认后显式保存';
    }),
    createSaveIntent: vi.fn(() => ({
      name: projection.project.name,
      description: projection.project.description,
      flow: candidateFlowFixture(),
      source: projection.handoff!.source!,
      build: artifact.build
    })),
    markProjectCreating: vi.fn(() => {
      projection.savePhase = 'workspace-project-creating';
      projection.canSave = false;
      projection.metadataLocked = true;
    }),
    markSaveUnknown: vi.fn(message => {
      projection.savePhase = 'workspace-save-unknown-outcome';
      projection.message = message;
      projection.canSave = true;
    }),
    markSaveFailed: vi.fn(message => {
      projection.savePhase = 'workspace-save-failed';
      projection.message = message;
      projection.canSave = true;
    }),
    discardHandoffDraft: vi.fn(async () => {
      projection.handoff = null;
      projection.canSave = false;
    }),
    dispose: vi.fn()
  } as unknown as WorkspaceNewDraftOwner;
}

function createPersistedOwner() {
  const project = decodeWorkspaceProjectV1(rawProject());
  const persistence = reactive({
    phase: 'clean', projectId, persistenceRevision: 0, dirtyGeneration: 0,
    submittedDirtyGeneration: null, dirty: false, canSave: false, canRun: false,
    canRetry: false, canReconcile: false, canReapplyConflict: false, canDiscardConflict: false,
    message: 'clean', errorCode: null, conflictServerRevision: null, lastSavedAt: null
  });
  const projection = reactive({
    phase: 'empty' as const,
    project,
    readonlyReason: null,
    persistence,
    run: null,
    handoff: null as WorkspaceOwner['projection']['handoff']
  });
  const owner = {
    projectId,
    projection,
    openFlowCanvas: vi.fn(),
    getFlowCanvasOwner: vi.fn(() => null),
    getGlobalVariablesOwner: vi.fn(() => null),
    getFinalDecisionOwner: vi.fn(() => null),
    getRuntimePackageExportOwner: vi.fn(() => null),
    save: vi.fn(async () => {
      persistence.phase = 'saved';
      persistence.dirty = false;
      persistence.canSave = false;
      projection.handoff = projection.handoff ? {
        ...projection.handoff,
        phase: 'workspace-saved',
        message: '已保存'
      } : null;
      return { status: 'saved', project };
    }),
    retrySave: vi.fn(),
    reconcileSave: vi.fn(),
    reapplyConflict: vi.fn(),
    discardConflict: vi.fn(),
    runFormal: vi.fn(),
    stopFormal: vi.fn(),
    reconcileFormalRun: vi.fn(),
    prepareForLeave: vi.fn(async () => true),
    quarantineForSessionExpiration: vi.fn(() => null),
    reconcileAfterReauthentication: vi.fn(async () => true),
    setReadonly: vi.fn(),
    stageHandoffDraft: vi.fn(),
    adoptNewHandoffDraft: vi.fn(async input => {
      projection.handoff = {
        phase: 'workspace-staged-unsaved', source: input.source,
        build: input.build, message: '尚未保存'
      };
      persistence.phase = 'dirty';
      persistence.dirty = true;
      persistence.canSave = true;
    }),
    confirmHandoff: vi.fn(),
    discardHandoffDraft: vi.fn(),
    dispose: vi.fn()
  } as unknown as WorkspaceOwner;
  return owner;
}

function createLifecycle(mode: 'success' | 'unknown') {
  const projection = reactive({
    phase: 'idle', command: null, projectId: null, clientOperationId: null,
    project: null, operation: null, message: '', errorCode: null,
    canRetry: false, canReconcile: false
  });
  const authority = {
    projectId,
    project: {
      id: projectId, name: 'AI 候选工程', description: null, version: '1.0.0',
      persistenceRevision: 0, createdAt: '2026-07-29T08:10:00.000Z', modifiedAt: null,
      lastOpenedAt: null, flow: null, assets: { schemaVersion: 1, calibrationAssetCount: 0, spatialAssetCount: 0 }
    },
    operationReplayed: mode === 'unknown',
    operation: {
      clientOperationId: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa', kind: 'create', status: 'completed',
      projectId, result: null, errorCode: null, createdAtUtc: '2026-07-29T08:10:00.000Z',
      updatedAtUtc: '2026-07-29T08:10:00.000Z', expiresAtUtc: null
    }
  };
  const lifecycle = {
    projection,
    diagnostics: {},
    setProjectScope: vi.fn(),
    createBlank: vi.fn(async () => {
      if (mode === 'unknown') {
        projection.phase = 'unknown-outcome';
        projection.message = '创建结果未知';
        return null;
      }
      projection.phase = 'succeeded';
      return authority;
    }),
    reconcile: vi.fn(async () => {
      projection.phase = 'succeeded';
      return authority;
    }),
    openProject: vi.fn(async () => ({ projectId, lastOpenedAtUtc: '2026-07-29T08:10:00.000Z' })),
    reset: vi.fn(),
    dispose: vi.fn()
  } as unknown as ProjectLifecycleCommandOwner;
  return { lifecycle, projection };
}

function createRuntime(newOwner: WorkspaceNewDraftOwner) {
  const persistedOwners: ReturnType<typeof createPersistedOwner>[] = [];
  const project = decodeWorkspaceProjectV1(rawProject());
  const receiverProjection = reactive({
    phase: 'idle', message: '', blocker: null, nextStep: '', inFlightCount: 0
  });
  const receive = vi.fn(async (options: {
    stage: (artifact: ReturnType<typeof decodeWorkspaceHandoffArtifactV1>) => Promise<void>;
  }) => {
    const artifact = decodeWorkspaceHandoffArtifactV1(handoffArtifactPayload());
    await options.stage(artifact);
    const source = {
      artifactId, sessionId: artifact.sessionId, planId: artifact.planId,
      buildId: artifact.build.buildId, candidateFlowFingerprint: artifact.candidateFlowFingerprint,
      targetKind: 'new' as const, receivedAtUtc: '2026-07-29T08:05:00.000Z'
    };
    receiverProjection.phase = 'workspace-staged-unsaved';
    return { artifact, source };
  });
  const runtime = {
    enabled: true,
    session: reactive({
      phase: 'authenticated', user: { userId: 'user-1', username: 'engineer', role: 'Engineer' },
      sessionGeneration: 1, message: '会话有效', updatedAt: Date.now()
    }),
    diagnostics: reactive({
      workspaceOwnerCount: 1, flowCanvasOwnerCount: 1, inspectorOwnerCount: 0,
      imageCanvasOwnerCount: 0, roiOwnerCount: 0, previewOwnerCount: 0,
      persistenceOwnerCount: 0, runOwnerCount: 0, activeProjectId: null, activeReadProjectId: null,
      totalWorkspaceMounts: 1, totalWorkspaceDisposals: 0, totalReadMounts: 0,
      totalReadDisposals: 0, totalInspectorMounts: 0, totalInspectorDisposals: 0,
      totalPersistenceMounts: 0, totalPersistenceDisposals: 0, totalRunMounts: 0,
      totalRunDisposals: 0, activeInspectorDrafts: 0, ownerConflictCount: 0,
      lastDisposedProjectId: null, lastDisposeReason: null, lastDisposedResources: null,
      activeSubscriptions: 0, activeTimers: 0, activeAnimationFrames: 0, activeObservers: 0,
      activeAbortControllers: 0, activeBlobUrls: 0, activePreviewArtifactIds: 0,
      activeHostSubscriptions: 0, inFlightReads: 0, inFlightWrites: 0,
      inFlightPreview: 0, inFlightExecute: 0, disposed: false
    }),
    refreshSession: vi.fn(),
    mountNewHandoffDraft: vi.fn(() => newOwner),
    openHandoffReceiver: vi.fn(() => ({
      projection: receiverProjection,
      receive,
      reject: vi.fn(),
      dispose: vi.fn()
    })),
    openProject: vi.fn(() => ({
      projectId,
      state: reactive({ phase: 'success', data: project, failure: null }),
      refresh: vi.fn(async () => ({ phase: 'success', data: project, failure: null })),
      dispose: vi.fn()
    })),
    mountProject: vi.fn(() => {
      const owner = createPersistedOwner();
      persistedOwners.push(owner);
      return owner;
    }),
    dispose: vi.fn()
  } as unknown as WorkspaceRuntime;
  return { runtime, persistedOwners, receive };
}

const flowWorkspaceStub = { name: 'FlowWorkspace', template: '<div data-testid="flow-workspace-stub" />' };

describe('F06 G4 new-project Workspace page handoff', () => {
  it('creates only on explicit save, adopts into the real Workspace owner, then saves once', async () => {
    const router = await routerForNew();
    const newOwner = createNewOwner();
    const context = createRuntime(newOwner);
    const commands = createLifecycle('success');
    const wrapper = mount(WorkspacePage, {
      props: { runtime: context.runtime, projectLifecycle: commands.lifecycle },
      global: { plugins: [router], stubs: { FlowWorkspace: flowWorkspaceStub } }
    });
    await flushPromises();

    expect(commands.lifecycle.createBlank).not.toHaveBeenCalled();
    expect(wrapper.get('[data-testid="workspace-save"]').text()).toBe('保存');
    await wrapper.get('[data-testid="workspace-save"]').trigger('click');
    await flushPromises();

    expect(commands.lifecycle.createBlank).toHaveBeenCalledTimes(1);
    expect(context.persistedOwners[0]?.adoptNewHandoffDraft).toHaveBeenCalledTimes(1);
    expect(context.persistedOwners[0]?.save).toHaveBeenCalledTimes(1);
    expect(context.persistedOwners).toHaveLength(1);
    expect(wrapper.get('[data-capability="project-workspace"]').attributes()).toMatchObject({
      'data-workspace-persistence-phase': 'saved',
      'data-workspace-dirty': 'false',
      'data-workspace-handoff-phase': 'workspace-saved'
    });
    expect(router.currentRoute.value.fullPath).toBe(`/projects/${projectId}/workspace`);
    expect(newOwner.dispose).toHaveBeenCalledWith('new-project-authority-created');
    wrapper.unmount();
  });

  it('reconciles an unknown create outcome without issuing a second create command', async () => {
    const router = await routerForNew();
    const newOwner = createNewOwner();
    const context = createRuntime(newOwner);
    const commands = createLifecycle('unknown');
    const wrapper = mount(WorkspacePage, {
      props: { projectId: 'new', runtime: context.runtime, projectLifecycle: commands.lifecycle },
      global: { plugins: [router], stubs: { FlowWorkspace: flowWorkspaceStub } }
    });
    await flushPromises();

    await wrapper.get('[data-testid="workspace-save"]').trigger('click');
    await flushPromises();
    expect(newOwner.markSaveUnknown).toHaveBeenCalled();
    expect(wrapper.get('[data-testid="workspace-save"]').text()).toBe('核对创建结果');

    await wrapper.get('[data-testid="workspace-save"]').trigger('click');
    await flushPromises();
    expect(commands.lifecycle.createBlank).toHaveBeenCalledTimes(1);
    expect(commands.lifecycle.reconcile).toHaveBeenCalledTimes(1);
    expect(context.persistedOwners[0]?.save).toHaveBeenCalledTimes(1);
    wrapper.unmount();
  });

  it('keeps the mounted dirty draft when only the route artifact query changes', async () => {
    const router = await routerForNew();
    const newOwner = createNewOwner();
    const context = createRuntime(newOwner);
    const commands = createLifecycle('success');
    const wrapper = mount(WorkspacePage, {
      props: { projectId: 'new', runtime: context.runtime, projectLifecycle: commands.lifecycle },
      global: { plugins: [router], stubs: { FlowWorkspace: flowWorkspaceStub } }
    });
    await flushPromises();

    await router.push(`/projects/new/workspace?handoff=${'f'.repeat(32)}`);
    await flushPromises();
    expect(context.runtime.mountNewHandoffDraft).toHaveBeenCalledTimes(1);
    expect(newOwner.dispose).not.toHaveBeenCalled();
    expect(context.receive).toHaveBeenCalledTimes(2);
    wrapper.unmount();
  });
});
