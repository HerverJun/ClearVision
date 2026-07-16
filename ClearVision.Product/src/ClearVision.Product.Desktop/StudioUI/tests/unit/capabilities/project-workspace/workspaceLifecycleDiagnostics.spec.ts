import { describe, expect, it } from 'vitest';
import {
  WorkspaceOwnerConflictError,
  createWorkspaceLifecycleDiagnosticsOwner
} from '@/capabilities/project-workspace/workspaceLifecycleDiagnostics';

const projectA = '11111111-1111-4111-8111-111111111111';
const projectB = '22222222-2222-4222-8222-222222222222';

describe('F03 G1 Workspace lifecycle diagnostics', () => {
  it('keeps every owner count at 0 or 1 and fails immediately on conflicts', () => {
    const diagnostics = createWorkspaceLifecycleDiagnosticsOwner({ publishToWindow: false });
    const read = diagnostics.reserveRead(projectA);
    const owner = diagnostics.reserveWorkspaceOwner(projectA);

    expect(diagnostics.diagnostics).toMatchObject({
      workspaceOwnerCount: 1,
      activeSubscriptions: 1,
      activeProjectId: projectA,
      activeReadProjectId: projectA
    });
    expect(() => diagnostics.reserveRead(projectB)).toThrow(WorkspaceOwnerConflictError);
    expect(() => diagnostics.reserveWorkspaceOwner(projectB)).toThrow(WorkspaceOwnerConflictError);
    expect(diagnostics.diagnostics.ownerConflictCount).toBe(2);

    read.dispose('test-read-dispose');
    owner.dispose('test-owner-dispose');
    diagnostics.dispose();
  });

  it('projects the active GET resource and returns every G1 resource to zero after disposal', () => {
    const diagnostics = createWorkspaceLifecycleDiagnosticsOwner({ publishToWindow: false });
    const read = diagnostics.reserveRead(projectA);
    const request = read.startRequest();

    expect(diagnostics.diagnostics).toMatchObject({
      activeSubscriptions: 1,
      activeAbortControllers: 1,
      inFlightReads: 1,
      inFlightWrites: 0,
      inFlightPreview: 0,
      inFlightExecute: 0
    });

    read.settleRequest(request);
    const owner = diagnostics.reserveWorkspaceOwner(projectA);
    expect(diagnostics.diagnostics.workspaceOwnerCount).toBe(1);

    read.dispose('route-leave');
    owner.dispose('route-leave');

    expect(diagnostics.diagnostics).toMatchObject({
      workspaceOwnerCount: 0,
      flowCanvasOwnerCount: 0,
      imageCanvasOwnerCount: 0,
      roiOwnerCount: 0,
      previewOwnerCount: 0,
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
      inFlightExecute: 0,
      lastDisposedProjectId: projectA,
      lastDisposeReason: 'route-leave'
    });
    expect(diagnostics.diagnostics.lastDisposedResources).toEqual({
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
    diagnostics.dispose();
  });

  it('passes 20 mount/unmount cycles with a zero resource ledger after every cycle', () => {
    const diagnostics = createWorkspaceLifecycleDiagnosticsOwner({ publishToWindow: false });

    for (let cycle = 0; cycle < 20; cycle += 1) {
      const read = diagnostics.reserveRead(cycle % 2 === 0 ? projectA : projectB);
      const request = read.startRequest();
      read.settleRequest(request);
      const owner = diagnostics.reserveWorkspaceOwner(cycle % 2 === 0 ? projectA : projectB);
      read.dispose(`cycle-${cycle}-read`);
      owner.dispose(`cycle-${cycle}-owner`);

      expect(diagnostics.diagnostics.workspaceOwnerCount, `cycle ${cycle}`).toBe(0);
      expect(diagnostics.diagnostics.activeSubscriptions, `cycle ${cycle}`).toBe(0);
      expect(diagnostics.diagnostics.activeAbortControllers, `cycle ${cycle}`).toBe(0);
      expect(diagnostics.diagnostics.inFlightReads, `cycle ${cycle}`).toBe(0);
      expect(diagnostics.diagnostics.lastDisposedResources, `cycle ${cycle}`).toEqual({
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
    }

    expect(diagnostics.diagnostics).toMatchObject({
      totalWorkspaceMounts: 20,
      totalWorkspaceDisposals: 20,
      totalReadMounts: 20,
      totalReadDisposals: 20,
      ownerConflictCount: 0
    });
    diagnostics.dispose();
  });
});
