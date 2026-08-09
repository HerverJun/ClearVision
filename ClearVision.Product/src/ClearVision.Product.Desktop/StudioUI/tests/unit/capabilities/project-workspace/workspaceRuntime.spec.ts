import { reactive } from 'vue';
import { describe, expect, it, vi } from 'vitest';
import { ApiAbortError, ApiNetworkError, type ApiTransport } from '@/platform/api';
import { createReadQueryClient } from '@/platform/query';
import {
  createWorkspaceLifecycleDiagnosticsOwner,
  decodeWorkspaceHandoffArtifactV1
} from '@/capabilities/project-workspace';
import { createWorkspaceRuntime } from '@/capabilities/project-workspace/workspaceRuntime';
import { artifactId, handoffArtifactPayload } from './handoff/handoffFixtures';

vi.mock('@/capabilities/project-workspace/flow', () => ({
  createFlowCanvasOwner: vi.fn((options: {
    diagnosticsKey: string;
    diagnostics: { reserveFlowCanvas(key: string): { dispose(reason?: string): void } };
  }) => {
    const lease = options.diagnostics.reserveFlowCanvas(options.diagnosticsKey);
    const projection = reactive<{
      phase: string;
      projectId: string | null;
      mutationGate: 'editable' | 'readonly' | 'running';
      draft: {
        id: string | null;
        name: string;
        operators: unknown[];
        connections: unknown[];
        decisionConfiguration: unknown;
        opaquePassthrough: Record<string, unknown>;
      };
      runtime: { flowRevision: number };
      feedback: unknown;
      catalog: { phase: string; operators: unknown[]; isRefreshing: boolean; message: string | null };
      error: unknown;
    }>({
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
      replaceFlow: vi.fn((flow: Record<string, unknown> | null, projectName: string) => {
        const source = flow ?? {
          id: null,
          name: `${projectName} 流程`,
          operators: [],
          connections: [],
          decisionConfiguration: null
        };
        projection.draft = {
          ...projection.draft,
          id: typeof source.id === 'string' ? source.id : null,
          name: typeof source.name === 'string' ? source.name : `${projectName} 流程`,
          operators: Array.isArray(source.operators) ? structuredClone(source.operators) : [],
          connections: Array.isArray(source.connections) ? structuredClone(source.connections) : [],
          decisionConfiguration: source.decisionConfiguration ?? null
        };
        projection.runtime.flowRevision += 1;
      }),
      openInspector: vi.fn(),
      openCameraBindingEditor: vi.fn(),
      openPreviewWorkbench: vi.fn(),
      hasPendingLifecycleOperation: vi.fn(() => false),
      hasUnknownLifecycleOutcome: vi.fn(() => false),
      prepareForLeave: vi.fn(async () => true),
      settle: vi.fn(async () => undefined),
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

function createHarness(options: {
  get?: ApiTransport['get'];
  post?: ApiTransport['post'];
} = {}) {
  const api = {
    apiBaseUrl: 'http://localhost/api',
    get: options.get ?? (vi.fn(async () => handoffArtifactPayload()) as unknown as ApiTransport['get']),
    post: options.post ?? (vi.fn(async () => handoffArtifactPayload()) as unknown as ApiTransport['post'])
  } as unknown as ApiTransport;
  const queries = createReadQueryClient(api);
  const diagnostics = createWorkspaceLifecycleDiagnosticsOwner({ publishToWindow: false });
  const session = {
    projection: reactive({
      phase: 'authenticated',
      user: { userId: 'user-1', username: 'engineer', role: 'Engineer' },
      sessionGeneration: 1,
      message: '',
      updatedAt: Date.now()
    }),
    refresh: vi.fn(async () => undefined)
  };
  const runtime = createWorkspaceRuntime({
    queries,
    api,
    session: session as never,
    featureFlags: { 'Studio2.Workspace': true },
    diagnostics
  });
  return { api, queries, diagnostics, runtime };
}

describe('WorkspaceRuntime G1 leave protection', () => {
  it('includes a new-project draft in the cross-project leave snapshot', async () => {
    const context = createHarness();
    const owner = context.runtime.mountNewHandoffDraft(artifactId);
    owner.openFlowCanvas();
    const artifact = decodeWorkspaceHandoffArtifactV1(handoffArtifactPayload());
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

    expect(context.runtime.getLeaveProtectionSnapshot()).toMatchObject({
      projectId: null,
      dirty: true,
      childPending: false,
      childUnknown: false
    });
    const quarantine = context.runtime.quarantineForSessionExpiration();
    expect(quarantine).toMatchObject({
      activeOwnerCount: 0,
      activeNewDraftOwnerCount: 1,
      activeHandoffReceiverCount: 0
    });
    expect(() => owner.createSaveIntent()).toThrow();
    await expect(context.runtime.reconcileAfterReauthentication()).resolves.toBe(true);
    expect(() => owner.createSaveIntent()).not.toThrow();

    owner.markProjectCreating();
    expect(context.runtime.getLeaveProtectionSnapshot()).toMatchObject({ childPending: true });
    owner.markSaveUnknown('创建结果未知');
    expect(context.runtime.getLeaveProtectionSnapshot()).toMatchObject({ childUnknown: true });
    await expect(context.runtime.prepareForLeave('project-switch')).resolves.toBe(false);

    context.runtime.dispose();
    context.queries.dispose();
  });

  it('tracks an in-flight handoff read and aborts it before leaving', async () => {
    let signal: AbortSignal | undefined;
    const get = vi.fn(async (_path: string, options?: { signal?: AbortSignal }) => await new Promise<never>((_resolve, reject) => {
      signal = options?.signal;
      signal?.addEventListener('abort', () => reject(new ApiAbortError('handoff-read')), { once: true });
    }));
    const context = createHarness({ get: get as unknown as ApiTransport['get'] });
    const receiver = context.runtime.openHandoffReceiver();
    const pending = receiver.receive({
      artifactId,
      targetProjectId: null,
      isDirty: () => false,
      baselineMatches: () => true,
      stage: vi.fn()
    });

    await vi.waitFor(() => expect(signal).toBeDefined());
    expect(context.runtime.getLeaveProtectionSnapshot()).toMatchObject({ childPending: true });
    expect(context.runtime.quarantineForSessionExpiration()).toMatchObject({
      activeNewDraftOwnerCount: 0,
      activeHandoffReceiverCount: 1
    });
    await expect(pending).resolves.toBeNull();
    await expect(context.runtime.reconcileAfterReauthentication()).resolves.toBe(true);
    expect(context.runtime.getLeaveProtectionSnapshot()).toMatchObject({ childPending: false, childUnknown: false });

    context.runtime.dispose();
    context.queries.dispose();
  });

  it('blocks leave when a handoff consume result is unknown', async () => {
    const get = vi.fn(async () => handoffArtifactPayload());
    const post = vi.fn(async (path: string) => {
      if (path.endsWith('/consume')) throw new ApiNetworkError(path, new Error('consume response lost'));
      return handoffArtifactPayload();
    });
    const context = createHarness({
      get: get as unknown as ApiTransport['get'],
      post: post as unknown as ApiTransport['post']
    });
    const receiver = context.runtime.openHandoffReceiver();

    await expect(receiver.receive({
      artifactId,
      targetProjectId: null,
      isDirty: () => false,
      baselineMatches: () => true,
      stage: vi.fn()
    })).resolves.toBeNull();
    expect(context.runtime.getLeaveProtectionSnapshot()).toMatchObject({ childUnknown: true });
    expect(context.runtime.quarantineForSessionExpiration()).toMatchObject({ activeHandoffReceiverCount: 1 });
    await expect(context.runtime.reconcileAfterReauthentication()).resolves.toBe(false);
    await expect(context.runtime.prepareForLeave('project-switch')).resolves.toBe(false);

    context.runtime.dispose();
    context.queries.dispose();
  });
});
