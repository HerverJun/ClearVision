import { describe, expect, it } from 'vitest';
import {
  WorkspaceOwnerConflictError,
  createWorkspaceLifecycleDiagnosticsOwner
} from '@/capabilities/project-workspace/workspaceLifecycleDiagnostics';

const projectA = '11111111-1111-4111-8111-111111111111';
const projectB = '22222222-2222-4222-8222-222222222222';

const zeroResources = {
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
} as const;

describe('F03 G1-G4 Workspace lifecycle diagnostics', () => {
  it('tracks auxiliary capability owners and their request resources independently', () => {
    const diagnostics = createWorkspaceLifecycleDiagnosticsOwner({ publishToWindow: false });
    const variables = diagnostics.reserveCapability(projectA, 'global-variables');
    const template = diagnostics.reserveCapability(projectA, 'template');
    const camera = diagnostics.reserveCapability(projectA, 'camera-binding');
    const inspection = diagnostics.reserveCapability(projectA, 'inspection-run');

    expect(diagnostics.diagnostics.capabilityOwnerCounts).toEqual({
      'global-variables': 1,
      template: 1,
      'camera-binding': 1,
      'inspection-run': 1,
      'final-decision': 0,
      'runtime-package': 0,
      'line-sequence': 0,
      calibration: 0,
      handoff: 0
    });
    expect(() => diagnostics.reserveCapability(projectB, 'template')).toThrow(WorkspaceOwnerConflictError);

    variables.update({ ...zeroResources, inFlightReads: 1, activeAbortControllers: 1 });
    camera.update({ ...zeroResources, activeTimers: 1, inFlightWrites: 1, activeAbortControllers: 1 });
    expect(diagnostics.diagnostics).toMatchObject({
      capabilityOwnerCounts: {
        'global-variables': 1,
        template: 1,
        'camera-binding': 1,
        'inspection-run': 1,
        'final-decision': 0,
        'runtime-package': 0,
        'line-sequence': 0,
        calibration: 0,
        handoff: 0
      },
      activeTimers: 1,
      activeAbortControllers: 2,
      inFlightReads: 1,
      inFlightWrites: 1
    });

    inspection.dispose('inspection-leave');
    camera.dispose('camera-leave');
    template.dispose('template-leave');
    variables.dispose('variables-leave');
    expect(diagnostics.diagnostics).toMatchObject({
      capabilityOwnerCounts: {
        'global-variables': 0,
        template: 0,
        'camera-binding': 0,
        'inspection-run': 0,
        'final-decision': 0,
        'runtime-package': 0,
        'line-sequence': 0,
        calibration: 0,
        handoff: 0
      },
      activeTimers: 0,
      activeAbortControllers: 0,
      inFlightReads: 0,
      inFlightWrites: 0
    });
    diagnostics.dispose();
  });

  it('keeps every owner count at 0 or 1 and fails immediately on conflicts', () => {
    const diagnostics = createWorkspaceLifecycleDiagnosticsOwner({ publishToWindow: false });
    const read = diagnostics.reserveRead(projectA);
    const owner = diagnostics.reserveWorkspaceOwner(projectA);
    const flow = diagnostics.reserveFlowCanvas(projectA);
    const inspector = diagnostics.reserveInspector(projectA);
    const preview = diagnostics.reservePreview(projectA);
    const image = diagnostics.reserveImageCanvas(projectA);
    const roi = diagnostics.reserveRoi(projectA);

    expect(diagnostics.diagnostics).toMatchObject({
      workspaceOwnerCount: 1,
      flowCanvasOwnerCount: 1,
      inspectorOwnerCount: 1,
      previewOwnerCount: 1,
      imageCanvasOwnerCount: 1,
      roiOwnerCount: 1,
      activeSubscriptions: 2,
      activeProjectId: projectA,
      activeReadProjectId: projectA
    });
    expect(() => diagnostics.reserveRead(projectB)).toThrow(WorkspaceOwnerConflictError);
    expect(() => diagnostics.reserveWorkspaceOwner(projectB)).toThrow(WorkspaceOwnerConflictError);
    expect(() => diagnostics.reserveFlowCanvas(projectB)).toThrow(WorkspaceOwnerConflictError);
    expect(() => diagnostics.reserveInspector(projectB)).toThrow(WorkspaceOwnerConflictError);
    expect(() => diagnostics.reservePreview(projectB)).toThrow(WorkspaceOwnerConflictError);
    expect(() => diagnostics.reserveImageCanvas(projectB)).toThrow(WorkspaceOwnerConflictError);
    expect(() => diagnostics.reserveRoi(projectB)).toThrow(WorkspaceOwnerConflictError);
    expect(diagnostics.diagnostics.ownerConflictCount).toBe(7);

    roi.dispose('test-roi-dispose');
    image.dispose('test-image-dispose');
    preview.dispose('test-preview-dispose');
    inspector.dispose('test-inspector-dispose');
    flow.dispose('test-flow-dispose');
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
    const flow = diagnostics.reserveFlowCanvas(projectA);
    const inspector = diagnostics.reserveInspector(projectA);
    const preview = diagnostics.reservePreview(projectA);
    const image = diagnostics.reserveImageCanvas(projectA);
    const roi = diagnostics.reserveRoi(projectA);
    inspector.updateDraftCount(2);
    flow.update({
      activeSubscriptions: 5,
      activeTimers: 1,
      activeAnimationFrames: 2,
      activeObservers: 2,
      activeAbortControllers: 0,
      activeBlobUrls: 0,
      activePreviewArtifactIds: 0,
      activeHostSubscriptions: 0,
      inFlightReads: 0,
      inFlightWrites: 0,
      inFlightPreview: 0,
      inFlightExecute: 0
    });
    preview.update({
      ...zeroResources,
      activeSubscriptions: 1,
      activeTimers: 1,
      activeAbortControllers: 1,
      activePreviewArtifactIds: 2,
      inFlightPreview: 1
    });
    image.update({
      ...zeroResources,
      activeAnimationFrames: 1,
      activeObservers: 1,
      activeBlobUrls: 1
    });
    roi.update({
      ...zeroResources,
      activeSubscriptions: 1
    });
    expect(diagnostics.diagnostics.workspaceOwnerCount).toBe(1);
    expect(diagnostics.diagnostics).toMatchObject({
      flowCanvasOwnerCount: 1,
      inspectorOwnerCount: 1,
      previewOwnerCount: 1,
      imageCanvasOwnerCount: 1,
      roiOwnerCount: 1,
      activeInspectorDrafts: 2,
      activeSubscriptions: 9,
      activeTimers: 2,
      activeAnimationFrames: 3,
      activeObservers: 3,
      activeAbortControllers: 1,
      activeBlobUrls: 1,
      activePreviewArtifactIds: 2,
      inFlightPreview: 1
    });

    read.dispose('route-leave');
    roi.dispose('route-leave');
    image.dispose('route-leave');
    preview.dispose('route-leave');
    inspector.dispose('route-leave');
    flow.dispose('route-leave');
    owner.dispose('route-leave');

    expect(diagnostics.diagnostics).toMatchObject({
      workspaceOwnerCount: 0,
      flowCanvasOwnerCount: 0,
      inspectorOwnerCount: 0,
      imageCanvasOwnerCount: 0,
      roiOwnerCount: 0,
      previewOwnerCount: 0,
      activeInspectorDrafts: 0,
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
    expect(diagnostics.diagnostics.lastDisposedResources).toEqual(zeroResources);
    diagnostics.dispose();
  });

  it('passes 20 mount/unmount cycles with a zero resource ledger after every cycle', () => {
    const diagnostics = createWorkspaceLifecycleDiagnosticsOwner({ publishToWindow: false });

    for (let cycle = 0; cycle < 20; cycle += 1) {
      const read = diagnostics.reserveRead(cycle % 2 === 0 ? projectA : projectB);
      const request = read.startRequest();
      read.settleRequest(request);
      const owner = diagnostics.reserveWorkspaceOwner(cycle % 2 === 0 ? projectA : projectB);
      const flow = diagnostics.reserveFlowCanvas(cycle % 2 === 0 ? projectA : projectB);
      const inspector = diagnostics.reserveInspector(cycle % 2 === 0 ? projectA : projectB);
      const preview = diagnostics.reservePreview(cycle % 2 === 0 ? projectA : projectB);
      const image = diagnostics.reserveImageCanvas(cycle % 2 === 0 ? projectA : projectB);
      const roi = diagnostics.reserveRoi(cycle % 2 === 0 ? projectA : projectB);
      inspector.updateDraftCount(1);
      flow.update({
        activeSubscriptions: 5,
        activeTimers: 0,
        activeAnimationFrames: 1,
        activeObservers: 2,
        activeAbortControllers: 0,
        activeBlobUrls: 0,
        activePreviewArtifactIds: 0,
        activeHostSubscriptions: 0,
        inFlightReads: 0,
        inFlightWrites: 0,
        inFlightPreview: 0,
        inFlightExecute: 0
      });
      preview.update({
        ...zeroResources,
        activeSubscriptions: 1,
        activeTimers: 1,
        activeAbortControllers: 1,
        activePreviewArtifactIds: 2,
        inFlightPreview: 1
      });
      image.update({
        ...zeroResources,
        activeAnimationFrames: 1,
        activeObservers: 1,
        activeBlobUrls: 1
      });
      roi.update({
        ...zeroResources,
        activeSubscriptions: 1
      });
      read.dispose(`cycle-${cycle}-read`);
      roi.dispose(`cycle-${cycle}-roi`);
      image.dispose(`cycle-${cycle}-image`);
      preview.dispose(`cycle-${cycle}-preview`);
      inspector.dispose(`cycle-${cycle}-inspector`);
      flow.dispose(`cycle-${cycle}-flow`);
      owner.dispose(`cycle-${cycle}-owner`);

      expect(diagnostics.diagnostics.workspaceOwnerCount, `cycle ${cycle}`).toBe(0);
      expect(diagnostics.diagnostics.flowCanvasOwnerCount, `cycle ${cycle}`).toBe(0);
      expect(diagnostics.diagnostics.inspectorOwnerCount, `cycle ${cycle}`).toBe(0);
      expect(diagnostics.diagnostics.previewOwnerCount, `cycle ${cycle}`).toBe(0);
      expect(diagnostics.diagnostics.imageCanvasOwnerCount, `cycle ${cycle}`).toBe(0);
      expect(diagnostics.diagnostics.roiOwnerCount, `cycle ${cycle}`).toBe(0);
      expect(diagnostics.diagnostics.activeInspectorDrafts, `cycle ${cycle}`).toBe(0);
      expect(diagnostics.diagnostics.activeSubscriptions, `cycle ${cycle}`).toBe(0);
      expect(diagnostics.diagnostics.activeTimers, `cycle ${cycle}`).toBe(0);
      expect(diagnostics.diagnostics.activeAnimationFrames, `cycle ${cycle}`).toBe(0);
      expect(diagnostics.diagnostics.activeObservers, `cycle ${cycle}`).toBe(0);
      expect(diagnostics.diagnostics.activeAbortControllers, `cycle ${cycle}`).toBe(0);
      expect(diagnostics.diagnostics.activeBlobUrls, `cycle ${cycle}`).toBe(0);
      expect(diagnostics.diagnostics.activePreviewArtifactIds, `cycle ${cycle}`).toBe(0);
      expect(diagnostics.diagnostics.inFlightReads, `cycle ${cycle}`).toBe(0);
      expect(diagnostics.diagnostics.inFlightPreview, `cycle ${cycle}`).toBe(0);
      expect(diagnostics.diagnostics.lastDisposedResources, `cycle ${cycle}`).toEqual(zeroResources);
    }

    expect(diagnostics.diagnostics).toMatchObject({
      totalWorkspaceMounts: 20,
      totalWorkspaceDisposals: 20,
      totalReadMounts: 20,
      totalReadDisposals: 20,
      totalInspectorMounts: 20,
      totalInspectorDisposals: 20,
      ownerConflictCount: 0
    });
    diagnostics.dispose();
  });
});
