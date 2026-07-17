import { reactive } from 'vue';
import { describe, expect, it, vi } from 'vitest';
import { ApiNetworkError } from '@/platform/api';
import {
  createWorkspaceLifecycleDiagnosticsOwner,
  type WorkspacePersistenceOwner
} from '@/capabilities/project-workspace';
import {
  createWorkspaceRunCommandOwner,
  type WorkspaceRunAdmissionV1,
  type WorkspaceRunPort,
  type WorkspaceRunResultV1
} from '@/capabilities/project-workspace/run';

const projectId = '11111111-1111-4111-8111-111111111111';
const resultId = '22222222-2222-4222-8222-222222222222';
const flowHash = 'flow-hash';
const decisionHash = 'decision-hash';

function createPersistence(canRun = true) {
  const projection = reactive({
    phase: 'clean' as const,
    projectId,
    persistenceRevision: 7,
    dirty: !canRun,
    canRun,
    canSave: false,
    canRetry: false,
    canReconcile: false,
    canReapplyConflict: false,
    canDiscardConflict: false,
    dirtyGeneration: 0,
    submittedDirtyGeneration: null,
    message: '',
    errorCode: null,
    conflictServerRevision: null,
    lastSavedAt: null
  });
  return {
    projection,
    owner: {
      projectId,
      projection,
      setRunning: vi.fn(() => canRun),
      clearRunning: vi.fn(),
      save: vi.fn(),
      retry: vi.fn(),
      reconcile: vi.fn(),
      reapplyConflict: vi.fn(),
      discardConflict: vi.fn(),
      setReadonly: vi.fn(),
      prepareForLeave: vi.fn(),
      settle: vi.fn(),
      dispose: vi.fn()
    } as unknown as WorkspacePersistenceOwner
  };
}

function admission(snapshotId: string, allowed = true): WorkspaceRunAdmissionV1 {
  return Object.freeze({
    allowed,
    code: allowed ? null : 'ADMISSION_FINAL_DECISION_INVALID',
    message: allowed ? 'admitted' : 'final decision is invalid',
    projectId,
    clientSnapshotId: snapshotId,
    persistenceRevision: allowed ? 7 : null,
    canonicalFlowHash: allowed ? flowHash : null,
    decisionConfigurationHash: allowed ? decisionHash : null,
    violations: Object.freeze([])
  });
}

function result(snapshotId: string, decision: WorkspaceRunResultV1['outcome']['decision']): WorkspaceRunResultV1 {
  return Object.freeze({
    id: resultId,
    projectId,
    status: 'Completed',
    outcome: Object.freeze({ execution: 'Succeeded', decision }),
    executionSnapshotId: snapshotId,
    persistenceRevision: 7,
    flowHash,
    decisionConfigurationHash: decisionHash,
    errorMessage: null
  });
}

function createPort(overrides: Partial<WorkspaceRunPort> = {}): WorkspaceRunPort {
  return {
    projectId,
    admit: vi.fn(async payload => admission(payload.clientSnapshotId)),
    execute: vi.fn(async payload => result(payload.clientSnapshotId, 'Ok')),
    ...overrides
  };
}

describe('F03 G6 Workspace Run command owner', () => {
  it.each(['Ok', 'Ng', 'Undetermined', 'Invalid'] as const)(
    'keeps canonical final decision %s separate from execution success',
    async decision => {
      const persistence = createPersistence();
      const diagnostics = createWorkspaceLifecycleDiagnosticsOwner({ publishToWindow: false });
      const port = createPort({ execute: vi.fn(async payload => result(payload.clientSnapshotId, decision)) });
      const owner = createWorkspaceRunCommandOwner({ projectId, persistenceOwner: persistence.owner, port, diagnostics });

      await expect(owner.run()).resolves.toMatchObject({ outcome: { execution: 'Succeeded', decision } });
      expect(owner.projection).toMatchObject({ phase: 'succeeded', result: { outcome: { decision } } });
      expect(persistence.owner.setRunning).toHaveBeenCalledTimes(1);
      expect(persistence.owner.clearRunning).toHaveBeenCalledTimes(1);
      owner.dispose();
      expect(diagnostics.diagnostics).toMatchObject({ runOwnerCount: 0, inFlightExecute: 0 });
      diagnostics.dispose();
    }
  );

  it('blocks dirty persistence before admission and never executes', async () => {
    const persistence = createPersistence(false);
    const diagnostics = createWorkspaceLifecycleDiagnosticsOwner({ publishToWindow: false });
    const port = createPort();
    const owner = createWorkspaceRunCommandOwner({ projectId, persistenceOwner: persistence.owner, port, diagnostics });

    await expect(owner.run()).resolves.toBeNull();
    expect(owner.projection).toMatchObject({ phase: 'blocked', errorCode: 'RUN_PERSISTENCE_GATE' });
    expect(port.admit).not.toHaveBeenCalled();
    expect(port.execute).not.toHaveBeenCalled();
    owner.dispose();
    diagnostics.dispose();
  });

  it('does not execute when admission is rejected', async () => {
    const persistence = createPersistence();
    const diagnostics = createWorkspaceLifecycleDiagnosticsOwner({ publishToWindow: false });
    const port = createPort({ admit: vi.fn(async payload => admission(payload.clientSnapshotId, false)) });
    const owner = createWorkspaceRunCommandOwner({ projectId, persistenceOwner: persistence.owner, port, diagnostics });

    await expect(owner.run()).resolves.toBeNull();
    expect(owner.projection).toMatchObject({ phase: 'blocked', errorCode: 'ADMISSION_FINAL_DECISION_INVALID' });
    expect(port.execute).not.toHaveBeenCalled();
    expect(persistence.owner.clearRunning).toHaveBeenCalledTimes(1);
    owner.dispose();
    diagnostics.dispose();
  });

  it('keeps the Workspace locked after a network-unknown execute outcome', async () => {
    const persistence = createPersistence();
    const diagnostics = createWorkspaceLifecycleDiagnosticsOwner({ publishToWindow: false });
    const port = createPort({
      execute: vi.fn(async () => {
        throw new ApiNetworkError('http://localhost/api/inspection/execute', new Error('lost'));
      })
    });
    const owner = createWorkspaceRunCommandOwner({ projectId, persistenceOwner: persistence.owner, port, diagnostics });

    await expect(owner.run()).resolves.toBeNull();
    expect(owner.projection).toMatchObject({ phase: 'unknown-outcome', errorCode: 'RUN_NETWORK_FAILURE' });
    expect(persistence.owner.clearRunning).not.toHaveBeenCalled();
    owner.dispose();
    diagnostics.dispose();
  });

  it('drops an admission response that arrives after route disposal', async () => {
    let resolveAdmission: ((value: WorkspaceRunAdmissionV1) => void) | undefined;
    const persistence = createPersistence();
    const diagnostics = createWorkspaceLifecycleDiagnosticsOwner({ publishToWindow: false });
    const port = createPort({ admit: vi.fn(payload => new Promise<WorkspaceRunAdmissionV1>(resolve => {
      resolveAdmission = resolve;
      void payload;
    })) });
    const owner = createWorkspaceRunCommandOwner({ projectId, persistenceOwner: persistence.owner, port, diagnostics });

    const running = owner.run();
    expect(owner.projection.phase).toBe('admitting');
    owner.dispose('route-leave');
    resolveAdmission?.(admission('33333333-3333-4333-8333-333333333333'));

    await expect(running).resolves.toBeNull();
    expect(port.execute).not.toHaveBeenCalled();
    expect(owner.projection.phase).toBe('disposed');
    expect(diagnostics.diagnostics).toMatchObject({ runOwnerCount: 0, inFlightExecute: 0, activeAbortControllers: 0 });
    diagnostics.dispose();
  });
});
