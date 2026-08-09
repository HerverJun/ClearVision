import { reactive } from 'vue';
import { afterEach, describe, expect, it, vi } from 'vitest';
import {
  createProductLeaveGuardOwner,
  type ProductLeaveGuardOwner
} from '@/app/leave';
import type { ProjectLifecycleCommandOwner } from '@/capabilities/project-lifecycle';
import type { InspectionRunOwner } from '@/capabilities/inspection-run';
import type {
  WorkspaceLeaveProtectionSnapshot,
  WorkspaceRuntime
} from '@/capabilities/project-workspace/workspaceRuntime';

let owner: ProductLeaveGuardOwner | undefined;

afterEach(() => {
  owner?.dispose('test-cleanup');
  owner = undefined;
});

function harness(options: {
  projectPhase?: string;
  projectCommand?: string | null;
  workspace?: WorkspaceLeaveProtectionSnapshot | null;
  projectSettled?: boolean;
  workspaceSettled?: boolean;
} = {}) {
  const projectProjection = reactive({
    phase: options.projectPhase ?? 'idle',
    command: options.projectCommand ?? null,
    projectId: null,
    clientOperationId: null,
    project: null,
    operation: null,
    openedAtUtc: null,
    errorCode: null,
    message: '',
    canReconcile: false,
    generation: 0
  });
  let workspaceSnapshot = options.workspace ?? null;
  const projectLifecycle = {
    projection: projectProjection,
    prepareForProtectedTransition: vi.fn(async () => options.projectSettled ?? true)
  } as unknown as ProjectLifecycleCommandOwner;
  const workspace = {
    getLeaveProtectionSnapshot: vi.fn(() => workspaceSnapshot),
    prepareForLeave: vi.fn(async () => options.workspaceSettled ?? workspaceSnapshot === null)
  } as unknown as WorkspaceRuntime;
  owner = createProductLeaveGuardOwner({
    projectLifecycle,
    workspace,
    publishToWindow: false
  });
  return {
    owner,
    projectProjection,
    projectLifecycle,
    workspace,
    setWorkspace(value: WorkspaceLeaveProtectionSnapshot | null) {
      workspaceSnapshot = value;
    }
  };
}

describe('productLeaveGuardOwner', () => {
  it('allows a clean route leave and keeps one owner', async () => {
    const h = harness();

    await expect(h.owner.request('route-leave')).resolves.toBe(true);

    expect(h.owner.projection).toMatchObject({ phase: 'allowed', protectionKind: null });
    expect(h.owner.diagnostics).toMatchObject({ ownerCount: 1, requestCount: 1 });
  });

  it('prompts for a dirty draft and resolves only from the accessible product dialog action', async () => {
    const h = harness({
      workspace: {
        projectId: '11111111-1111-4111-8111-111111111111',
        persistencePhase: 'dirty',
        dirty: true,
        runPhase: 'idle'
      },
      workspaceSettled: false
    });

    const pending = h.owner.request('route-leave');
    await vi.waitFor(() => expect(h.owner.projection.phase).toBe('prompting'));
    expect(h.owner.projection).toMatchObject({
      protectionKind: 'workspace-draft',
      forceCloseAllowed: true
    });
    h.owner.confirmPrompt();

    await expect(pending).resolves.toBe(true);
    expect(h.owner.projection.phase).toBe('allowed');
  });

  it('keeps the route when the user cancels a draft prompt', async () => {
    const h = harness({
      workspace: {
        projectId: '11111111-1111-4111-8111-111111111111',
        persistencePhase: 'conflict',
        dirty: true,
        runPhase: 'idle'
      },
      workspaceSettled: false
    });

    const pending = h.owner.request('project-switch');
    await vi.waitFor(() => expect(h.owner.projection.phase).toBe('prompting'));
    h.owner.cancelPrompt();

    await expect(pending).resolves.toBe(false);
    expect(h.owner.projection).toMatchObject({
      phase: 'blocked',
      protectionKind: 'workspace-save-conflict'
    });
  });

  it.each([
    ['executing', 'workspace-run-active'],
    ['unknown-outcome', 'workspace-run-unknown']
  ] as const)('blocks %s Formal Run without a force-discard prompt', async (runPhase, protectionKind) => {
    const h = harness({
      workspace: {
        projectId: '11111111-1111-4111-8111-111111111111',
        persistencePhase: 'running',
        dirty: false,
        runPhase
      },
      workspaceSettled: false
    });

    await expect(h.owner.request('route-leave')).resolves.toBe(false);
    expect(h.owner.projection).toMatchObject({
      phase: 'blocked',
      protectionKind,
      forceCloseAllowed: false
    });
  });

  it('blocks unresolved Project operation outcome and calls its authoritative reconcile path', async () => {
    const h = harness({ projectPhase: 'unknown-outcome', projectCommand: 'delete', projectSettled: false });

    await expect(h.owner.request('logout')).resolves.toBe(false);

    expect(h.projectLifecycle.prepareForProtectedTransition).toHaveBeenCalledOnce();
    expect(h.owner.projection).toMatchObject({
      phase: 'blocked',
      protectionKind: 'project-command-unknown'
    });
  });

  it.each([
    ['pending', 'workspace-child-pending'],
    ['unknown', 'workspace-child-unknown']
  ] as const)('blocks leave for a workspace child %s operation', async (_label, protectionKind) => {
    const h = harness({
      workspace: {
        projectId: '11111111-1111-1111-1111-111111111111',
        persistencePhase: 'clean',
        dirty: false,
        runPhase: 'idle',
        ...(_label === 'pending' ? { childPending: true } : { childUnknown: true })
      },
      workspaceSettled: false
    });

    await expect(h.owner.request('route-leave')).resolves.toBe(false);
    expect(h.owner.projection).toMatchObject({
      phase: 'blocked',
      protectionKind,
      forceCloseAllowed: false
    });
  });

  it('lets a stable continuous inspection persist across ordinary route leave', async () => {
    const h = harness();
    const runtime = reactive({ isBusy: true, sessionType: 'ContinuousInspection' as const });
    const inspection = {
      projection: reactive({ phase: 'running', runtime }),
      prepareForLeave: vi.fn(async () => { runtime.isBusy = false; return true; }),
      stop: vi.fn(async () => { runtime.isBusy = false; return true; }),
      reconcile: vi.fn(async () => undefined)
    } as unknown as InspectionRunOwner;
    h.owner.attachInspectionRun(inspection);

    await expect(h.owner.request('route-leave')).resolves.toBe(true);

    expect(inspection.prepareForLeave).not.toHaveBeenCalled();
    expect(inspection.stop).not.toHaveBeenCalled();
    expect(inspection.reconcile).not.toHaveBeenCalled();
    expect(runtime.isBusy).toBe(true);
    expect(h.owner.projection.phase).toBe('allowed');
  });

  it('settles continuous inspection before protected session leave', async () => {
    const h = harness();
    const runtime = reactive({ isBusy: true, sessionType: 'ContinuousInspection' as const });
    const inspection = {
      projection: reactive({ phase: 'running', runtime }),
      prepareForLeave: vi.fn(async () => { runtime.isBusy = false; return true; }),
      stop: vi.fn(async () => { runtime.isBusy = false; return true; }),
      reconcile: vi.fn(async () => undefined)
    } as unknown as InspectionRunOwner;
    h.owner.attachInspectionRun(inspection);

    await expect(h.owner.request('logout')).resolves.toBe(true);

    expect(inspection.prepareForLeave).toHaveBeenCalledOnce();
    expect(inspection.stop).not.toHaveBeenCalled();
    expect(runtime.isBusy).toBe(false);
  });

  it('blocks route leave when continuous inspection cannot settle', async () => {
    const h = harness();
    const inspection = {
      projection: reactive({ runtime: { isBusy: true, sessionType: 'ContinuousInspection' as const } }),
      prepareForLeave: vi.fn(async () => false),
      stop: vi.fn(async () => false),
      reconcile: vi.fn(async () => undefined)
    } as unknown as InspectionRunOwner;
    h.owner.attachInspectionRun(inspection);

    await expect(h.owner.request('route-leave')).resolves.toBe(false);
    expect(inspection.prepareForLeave).toHaveBeenCalledOnce();
    expect(inspection.stop).not.toHaveBeenCalled();
    expect(inspection.reconcile).not.toHaveBeenCalled();
    expect(h.owner.projection).toMatchObject({
      phase: 'blocked',
      protectionKind: 'continuous-inspection-active',
      forceCloseAllowed: false
    });
  });

  it('allows leave only after active Project and Workspace authority settle', async () => {
    const h = harness({
      projectPhase: 'unknown-outcome',
      projectCommand: 'create',
      workspace: {
        projectId: '11111111-1111-4111-8111-111111111111',
        persistencePhase: 'saving',
        dirty: true,
        runPhase: 'idle'
      }
    });
    vi.mocked(h.projectLifecycle.prepareForProtectedTransition).mockImplementation(async () => {
      h.projectProjection.phase = 'succeeded';
      return true;
    });
    vi.mocked(h.workspace.prepareForLeave).mockImplementation(async () => {
      h.setWorkspace(null);
      return true;
    });

    await expect(h.owner.request('logout')).resolves.toBe(true);

    expect(h.projectLifecycle.prepareForProtectedTransition).toHaveBeenCalledOnce();
    expect(h.workspace.prepareForLeave).toHaveBeenCalledWith('product-logout', undefined);
    expect(h.owner.projection).toMatchObject({ phase: 'allowed', protectionKind: null });
  });

  it('prompts for update conflict but does not prompt Host close inside WebView2', async () => {
    const h = harness({ projectPhase: 'conflict', projectCommand: 'update' });
    const routeLeave = h.owner.request('route-leave');
    await vi.waitFor(() => expect(h.owner.projection.phase).toBe('prompting'));
    h.owner.cancelPrompt();
    await expect(routeLeave).resolves.toBe(false);

    await expect(h.owner.request('host-close')).resolves.toBe(false);
    expect(h.owner.projection).toMatchObject({
      phase: 'blocked',
      protectionKind: 'project-update-conflict',
      forceCloseAllowed: false
    });
  });

  it('serializes different leave reasons so Host close cannot inherit a route discard decision', async () => {
    const h = harness({
      workspace: {
        projectId: '11111111-1111-4111-8111-111111111111',
        persistencePhase: 'dirty',
        dirty: true,
        runPhase: 'idle'
      },
      workspaceSettled: false
    });

    const routeLeave = h.owner.request('route-leave');
    await vi.waitFor(() => expect(h.owner.projection.phase).toBe('prompting'));
    const hostClose = h.owner.request('host-close');
    h.owner.confirmPrompt();

    await expect(routeLeave).resolves.toBe(true);
    await expect(hostClose).resolves.toBe(false);
    expect(h.owner.projection).toMatchObject({
      phase: 'blocked',
      reason: 'host-close',
      protectionKind: 'workspace-draft',
      forceCloseAllowed: false
    });
    expect(h.owner.diagnostics).toMatchObject({ requestCount: 2, promptCount: 1 });
  });

  it('invalidates a pending prompt without allowing stale completion after session expiration', async () => {
    const h = harness({
      workspace: {
        projectId: '11111111-1111-4111-8111-111111111111',
        persistencePhase: 'dirty',
        dirty: true,
        runPhase: 'idle'
      },
      workspaceSettled: false
    });

    const pending = h.owner.request('change-password');
    await vi.waitFor(() => expect(h.owner.projection.phase).toBe('prompting'));
    h.owner.suspendForSessionExpiration();

    await expect(pending).resolves.toBe(false);
    expect(h.owner.projection).toMatchObject({
      phase: 'idle',
      reason: null,
      protectionKind: null
    });
  });

  it('rejects a second mounted owner and releases diagnostics on dispose', () => {
    const h = harness();
    expect(() => createProductLeaveGuardOwner({
      projectLifecycle: h.projectLifecycle,
      workspace: h.workspace,
      publishToWindow: false
    })).toThrow('already has an active owner');

    h.owner.dispose();
    owner = undefined;
    expect(h.owner.diagnostics).toMatchObject({ ownerCount: 0, disposed: true });
  });
});
