import { reactive } from 'vue';
import { describe, expect, it, vi } from 'vitest';
import type { ApiTransport } from '@/platform/api';
import { createReadQueryClient } from '@/platform/query';

vi.mock('@/capabilities/project-workspace/flow', () => ({
  createFlowCanvasOwner: vi.fn((options: {
    diagnosticsKey: string;
    diagnostics: {
      reserveFlowCanvas(key: string): { dispose(reason?: string): void };
    };
  }) => {
    const lease = options.diagnostics.reserveFlowCanvas(options.diagnosticsKey);
    const projection = reactive({
      phase: 'mounted',
      projectId: null,
      mutationGate: 'editable',
      draft: {
        id: null,
        name: 'AI 候选工程流程',
        operators: [],
        connections: [],
        decisionConfiguration: null,
        opaquePassthrough: {}
      },
      runtime: { flowRevision: 0 },
      feedback: null,
      catalog: { phase: 'success', operators: [], isRefreshing: false, message: null },
      error: null
    });
    let disposed = false;
    return Object.freeze({
      projectId: null,
      projection,
      commands: {},
      mountCanvas: vi.fn(),
      replaceFlow: vi.fn((flow: Record<string, unknown> | null, projectName: string) => {
        const source = flow ?? {
          id: null,
          name: `${projectName} 流程`,
          operators: [],
          connections: [],
          decisionConfiguration: null
        };
        projection.draft = reactive({
          id: typeof source.id === 'string' ? source.id : null,
          name: typeof source.name === 'string' ? source.name : `${projectName} 流程`,
          operators: Array.isArray(source.operators) ? structuredClone(source.operators) : [],
          connections: Array.isArray(source.connections) ? structuredClone(source.connections) : [],
          decisionConfiguration: source.decisionConfiguration ?? null,
          opaquePassthrough: {}
        }) as typeof projection.draft;
        projection.runtime.flowRevision += 1;
      }),
      openInspector: vi.fn(),
      openCameraBindingEditor: vi.fn(),
      openPreviewWorkbench: vi.fn(),
      refreshOperators: vi.fn(),
      setMutationGate: vi.fn((gate: 'editable' | 'readonly' | 'running') => {
        projection.mutationGate = gate;
      }),
      dispose: vi.fn((reason?: string) => {
        if (disposed) return;
        disposed = true;
        projection.phase = 'disposed';
        lease.dispose(reason);
      })
    });
  })
}));

import {
  createWorkspaceLifecycleDiagnosticsOwner,
  createWorkspaceNewDraftOwner,
  decodeWorkspaceHandoffArtifactV1
} from '@/capabilities/project-workspace';
import { artifactId, handoffArtifactPayload } from './handoffFixtures';

function harness() {
  const api = {
    apiBaseUrl: 'http://localhost/api',
    get: vi.fn(async () => [])
  } as unknown as ApiTransport;
  const queries = createReadQueryClient(api);
  const diagnostics = createWorkspaceLifecycleDiagnosticsOwner({ publishToWindow: false });
  return { api, queries, diagnostics };
}

describe('F06 G4 new-project Workspace draft owner', () => {
  it('keeps Project identity null, stages locally, snapshots edits and discards without persistence', async () => {
    const context = harness();
    const artifact = decodeWorkspaceHandoffArtifactV1(handoffArtifactPayload());
    const owner = createWorkspaceNewDraftOwner({
      artifactId,
      diagnostics: context.diagnostics,
      queries: context.queries,
      api: context.api,
      featureFlags: {}
    });
    const flow = owner.openFlowCanvas();

    expect(owner.projectId).toBeNull();
    expect(flow.projectId).toBeNull();
    await owner.stageHandoffDraft(artifact);
    owner.confirmHandoff({
      artifactId,
      sessionId: artifact.sessionId,
      planId: artifact.planId,
      buildId: artifact.build.buildId,
      candidateFlowFingerprint: artifact.candidateFlowFingerprint,
      targetKind: 'new',
      receivedAtUtc: '2026-07-29T08:05:00.000Z'
    });
    owner.setMetadata({ name: '视觉检测新工程', description: '来自审核后的 AI 候选' });

    expect(owner.createSaveIntent()).toMatchObject({
      name: '视觉检测新工程',
      description: '来自审核后的 AI 候选',
      flow: { id: artifact.candidateFlow.id }
    });
    expect(owner.projection.handoff?.phase).toBe('workspace-staged-unsaved');
    expect(context.diagnostics.diagnostics).toMatchObject({
      workspaceOwnerCount: 1,
      flowCanvasOwnerCount: 1
    });

    await owner.discardHandoffDraft();
    expect(owner.projection.handoff).toBeNull();
    expect(owner.projection.canSave).toBe(false);
    expect(context.api.get).not.toHaveBeenCalledWith(expect.stringContaining('projects'));
    owner.dispose();
    expect(context.diagnostics.diagnostics).toMatchObject({
      workspaceOwnerCount: 0,
      flowCanvasOwnerCount: 0
    });
    context.diagnostics.dispose();
    context.queries.dispose();
  });

  it('locks the draft for create and unknown outcome instead of issuing a second create intent', async () => {
    const context = harness();
    const artifact = decodeWorkspaceHandoffArtifactV1(handoffArtifactPayload());
    const owner = createWorkspaceNewDraftOwner({
      artifactId,
      diagnostics: context.diagnostics,
      queries: context.queries,
      api: context.api,
      featureFlags: {}
    });
    const flow = owner.openFlowCanvas();
    await owner.stageHandoffDraft(artifact);
    owner.confirmHandoff({
      artifactId, sessionId: artifact.sessionId, planId: artifact.planId,
      buildId: artifact.build.buildId, candidateFlowFingerprint: artifact.candidateFlowFingerprint,
      targetKind: 'new', receivedAtUtc: '2026-07-29T08:05:00.000Z'
    });

    owner.markProjectCreating();
    expect(owner.projection).toMatchObject({
      savePhase: 'workspace-project-creating', metadataLocked: true, canSave: false
    });
    expect(flow.projection.mutationGate).toBe('readonly');
    owner.markSaveUnknown('创建响应未知，必须核对 operation。');
    expect(owner.projection).toMatchObject({
      savePhase: 'workspace-save-unknown-outcome', metadataLocked: true, canSave: true
    });
    owner.dispose();
    context.diagnostics.dispose();
    context.queries.dispose();
  });

  it('returns the Workspace and Flow owner ledger to zero across 20 mount/unmount cycles', () => {
    const context = harness();
    for (let cycle = 0; cycle < 20; cycle += 1) {
      const owner = createWorkspaceNewDraftOwner({
        artifactId,
        diagnostics: context.diagnostics,
        queries: context.queries,
        api: context.api,
        featureFlags: {}
      });
      owner.openFlowCanvas();
      expect(context.diagnostics.diagnostics.workspaceOwnerCount).toBe(1);
      expect(context.diagnostics.diagnostics.flowCanvasOwnerCount).toBe(1);
      owner.dispose(`cycle-${cycle}`);
      expect(context.diagnostics.diagnostics.workspaceOwnerCount).toBe(0);
      expect(context.diagnostics.diagnostics.flowCanvasOwnerCount).toBe(0);
    }
    expect(context.diagnostics.diagnostics.totalWorkspaceMounts).toBe(20);
    expect(context.diagnostics.diagnostics.totalWorkspaceDisposals).toBe(20);
    context.diagnostics.dispose();
    context.queries.dispose();
  });
});
