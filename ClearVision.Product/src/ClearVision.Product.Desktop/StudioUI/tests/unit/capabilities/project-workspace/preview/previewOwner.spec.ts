import { nextTick, reactive } from 'vue';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { ApiTransport } from '@/platform/api';
import type { FlowCanvasOwner } from '@/capabilities/project-workspace/flow';
import { createPreviewOwner } from '@/capabilities/project-workspace/preview/previewOwner';
import { createWorkspaceLifecycleDiagnosticsOwner } from '@/capabilities/project-workspace/workspaceLifecycleDiagnostics';

const projectId = '11111111-1111-4111-8111-111111111111';
const nodeId = '22222222-2222-4222-8222-222222222222';

function response(body: Readonly<Record<string, unknown>>, outputData: Record<string, unknown> = { score: 0.9 }) {
  const debugSessionId = String(body.debugSessionId);
  const sequence = Number(body.clientRequestSequence);
  const flowRevision = Number(body.flowRevision);
  return {
    success: true,
    projectId,
    targetNodeId: nodeId,
    debugSessionId,
    executionTimeMs: 5,
    inputImageBase64: null,
    outputImageBase64: null,
    outputData,
    errorMessage: null,
    failedOperatorId: null,
    failedOperatorName: null,
    failedOperatorType: null,
    diagnostics: [],
    missingResources: [],
    artifacts: [],
    observation: {
      schemaVersion: 'execution-observation.v1',
      identity: {
        projectId,
        targetNodeId: nodeId,
        debugSessionId,
        clientRequestSequence: sequence,
        flowRevision
      },
      outcome: {
        success: true,
        executionTimeMs: 5,
        errorMessage: null,
        failedOperatorId: null,
        failedOperatorName: null,
        failedOperatorType: null,
        executedOperatorCount: 1
      },
      diagnostics: []
    }
  };
}

function createFlowOwner(): FlowCanvasOwner {
  const projection = reactive({
    phase: 'mounted',
    projectId,
    mutationGate: 'editable',
    draft: {
      id: '33333333-3333-4333-8333-333333333333',
      name: 'Preview flow',
      operators: [{
        id: nodeId,
        name: 'Threshold',
        type: 20,
        inputPorts: [],
        outputPorts: [{ id: 'out-1', name: 'Image', dataType: 'Image' }],
        parameters: [{ id: 'p-1', name: 'Threshold', value: 10, defaultValue: 0 }],
        isEnabled: true
      }],
      connections: [],
      decisionConfiguration: null,
      opaquePassthrough: {}
    },
    runtime: {
      selectedNodeId: nodeId,
      selectedNodeIds: [nodeId],
      selectionRevision: 1,
      flowRevision: 0
    },
    feedback: null,
    catalog: {
      phase: 'success',
      operators: [{
        operatorType: '20',
        displayName: 'Threshold',
        outputPorts: [{ name: 'Image', dataType: 'Image' }],
        inputPorts: [],
        parameters: []
      }],
      isRefreshing: false,
      message: null
    },
    error: null
  });
  return { projection } as unknown as FlowCanvasOwner;
}

beforeEach(() => vi.useFakeTimers());
afterEach(() => vi.useRealTimers());

describe('G4 Preview owner identity and lifecycle', () => {
  it('binds the request to project/node/revision/snapshot hash and marks changed drafts stale', async () => {
    const flowOwner = createFlowOwner();
    let resolveSecond: ((value: unknown) => void) | undefined;
    const post = vi.fn(async (_path: string, body: unknown) => {
      const request = body as Readonly<Record<string, unknown>>;
      if (Number(request.flowRevision) === 1) {
        return await new Promise(resolve => { resolveSecond = resolve; });
      }
      return response(request);
    });
    const api: ApiTransport = {
      apiBaseUrl: 'http://localhost:5000/api',
      get: vi.fn(),
      post: post as unknown as NonNullable<ApiTransport['post']>,
      getBlob: vi.fn(),
      delete: vi.fn()
    };
    const diagnostics = createWorkspaceLifecycleDiagnosticsOwner({ publishToWindow: false });
    const owner = createPreviewOwner({ projectId, flowOwner, api, diagnostics });

    await vi.advanceTimersByTimeAsync(500);
    await nextTick();
    expect(owner.projection.phase).toBe('success');
    expect(owner.projection.requestIdentity).toMatchObject({
      projectId,
      nodeId,
      flowRevision: 0
    });
    expect(owner.projection.requestIdentity?.clientSnapshotHash).toMatch(/^[0-9a-f]{16}$/);
    expect(post).toHaveBeenCalledTimes(1);

    const projection = flowOwner.projection as unknown as {
      runtime: { flowRevision: number };
      draft: { operators: Array<Record<string, unknown>> };
    };
    projection.draft.operators = [{
      ...projection.draft.operators[0],
      parameters: [{ id: 'p-1', name: 'Threshold', value: 20, defaultValue: 0 }]
    }];
    projection.runtime.flowRevision = 1;
    await nextTick();
    expect(owner.projection.isStale).toBe(true);
    expect(owner.projection.phase).toBe('loading');

    await vi.advanceTimersByTimeAsync(500);
    expect(post).toHaveBeenCalledTimes(2);
    resolveSecond?.(response((post.mock.calls[1]?.[1] ?? {}) as Readonly<Record<string, unknown>>, { score: 1 }));
    await vi.advanceTimersByTimeAsync(0);
    await nextTick();
    expect(owner.projection.phase).toBe('success');
    expect(owner.projection.isStale).toBe(false);
    expect(owner.projection.requestIdentity?.flowRevision).toBe(1);
    expect(diagnostics.diagnostics.previewOwnerCount).toBe(1);

    owner.dispose('test-route-leave');
    expect(diagnostics.diagnostics).toMatchObject({
      previewOwnerCount: 0,
      activeAbortControllers: 0,
      activeBlobUrls: 0,
      activePreviewArtifactIds: 0,
      inFlightPreview: 0
    });
    diagnostics.dispose();
  });

  it('ignores a late response after route disposal', async () => {
    const flowOwner = createFlowOwner();
    let resolveRequest: ((value: unknown) => void) | undefined;
    let lastBody: Readonly<Record<string, unknown>> = {};
    const api: ApiTransport = {
      apiBaseUrl: 'http://localhost:5000/api',
      get: vi.fn(),
      post: vi.fn(async (_path, body) => {
        lastBody = body as Readonly<Record<string, unknown>>;
        return await new Promise(resolve => { resolveRequest = resolve; });
      }) as unknown as NonNullable<ApiTransport['post']>,
      getBlob: vi.fn(),
      delete: vi.fn()
    };
    const diagnostics = createWorkspaceLifecycleDiagnosticsOwner({ publishToWindow: false });
    const owner = createPreviewOwner({ projectId, flowOwner, api, diagnostics });
    await vi.advanceTimersByTimeAsync(500);
    expect(owner.projection.phase).toBe('loading');
    owner.dispose('route-leave');
    resolveRequest?.(response(lastBody));
    await Promise.resolve();
    await nextTick();
    expect(owner.projection.phase).toBe('disposed');
    expect(diagnostics.diagnostics.previewOwnerCount).toBe(0);
    diagnostics.dispose();
  });
});
