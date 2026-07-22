import { reactive, readonly, watch, type DeepReadonly } from 'vue';
import { NodePreviewCoordinator } from '@clearvision/canonical-preview-coordinator';
import type { ApiTransport } from '@/platform/api';
import type { FlowCanvasOwner } from '../flow';
import type {
  WorkspaceCapabilityDiagnosticsLease,
  WorkspaceLifecycleDiagnosticsOwner,
  WorkspaceResourceSnapshot
} from '../workspaceLifecycleDiagnostics';
import type {
  PreviewArtifactReferenceV1,
  PreviewDiagnosticV1,
  PreviewIdentityV1,
  PreviewMissingResourceV1,
  PreviewStructuredObjectV1
} from './previewContracts';
import {
  createPreviewTransportPort,
  type PreviewTransportPort
} from './previewTransport';

export type PreviewOwnerPhase =
  | 'idle'
  | 'loading'
  | 'success'
  | 'empty'
  | 'blocked'
  | 'cancelled'
  | 'auth-error'
  | 'error'
  | 'disposed';

export interface PreviewRequestIdentity {
  readonly projectId: string;
  readonly nodeId: string;
  readonly flowRevision: number;
  readonly clientSnapshotHash: string;
  readonly requestKey: string;
  readonly observationIdentity: PreviewIdentityV1 | null;
}

export interface PreviewArtifactProjection extends PreviewArtifactReferenceV1 {
  readonly isImage: boolean;
}

export interface PreviewOwnerProjection {
  readonly phase: PreviewOwnerPhase;
  readonly projectId: string;
  readonly selectedNodeId: string | null;
  readonly selectedNodeType: string | null;
  readonly title: string;
  readonly statusText: string;
  readonly isStale: boolean;
  readonly staleReason: string | null;
  readonly canPreview: boolean;
  readonly canCancel: boolean;
  readonly autoPreviewAllowed: boolean;
  readonly manualReason: string | null;
  readonly inputImageSrc: string | null;
  readonly outputImageSrc: string | null;
  readonly outputData: Readonly<Record<string, unknown>> | null;
  readonly artifacts: readonly PreviewArtifactProjection[];
  readonly diagnostics: readonly PreviewDiagnosticV1[];
  readonly missingResources: readonly PreviewMissingResourceV1[];
  readonly errorMessage: string | null;
  readonly executionTimeMs: number | null;
  readonly requestIdentity: PreviewRequestIdentity | null;
}

type MutablePreviewOwnerProjection = {
  -readonly [Key in keyof PreviewOwnerProjection]: PreviewOwnerProjection[Key]
};

export interface PreviewOwner {
  readonly projectId: string;
  readonly projection: DeepReadonly<PreviewOwnerProjection>;
  previewNow(): Promise<unknown>;
  cancel(reason?: string): void;
  readArtifact(
    artifactId: string,
    options?: Readonly<{ signal?: AbortSignal; objectUrl?: boolean }>
  ): Promise<Readonly<{ artifact: unknown; blob: Blob; headers?: Headers; objectUrl: string | null }>>;
  dispose(reason?: string): void;
}

function record(value: unknown): Readonly<Record<string, unknown>> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
    ? value as Readonly<Record<string, unknown>>
    : Object.freeze({});
}

function text(value: unknown): string {
  return typeof value === 'string' ? value : value === null || value === undefined ? '' : String(value);
}

function number(value: unknown, fallback = 0): number {
  return typeof value === 'number' && Number.isFinite(value) ? value : fallback;
}

function array(value: unknown): readonly unknown[] {
  return Array.isArray(value) ? value : Object.freeze([]);
}

function selectedNode(flowOwner: FlowCanvasOwner): Readonly<Record<string, unknown>> | null {
  const nodeId = flowOwner.projection.runtime?.selectedNodeId;
  if (!nodeId) return null;
  return flowOwner.projection.draft.operators.find(operator => text(operator.id ?? operator.Id) === nodeId) ?? null;
}

function operatorMetadata(
  flowOwner: FlowCanvasOwner,
  type: unknown
): Readonly<Record<string, unknown>> | null {
  const identity = text(type).trim().toLowerCase();
  return flowOwner.projection.catalog.operators.find(operator =>
    operator.operatorType.trim().toLowerCase() === identity) as unknown as Readonly<Record<string, unknown>> ?? null;
}

function stableSerialize(value: unknown): string {
  if (value === null || typeof value !== 'object') return JSON.stringify(value) ?? 'null';
  if (Array.isArray(value)) return `[${value.map(stableSerialize).join(',')}]`;
  const source = value as Readonly<Record<string, unknown>>;
  return `{${Object.keys(source).sort().map(key => `${JSON.stringify(key)}:${stableSerialize(source[key])}`).join(',')}}`;
}

export function buildClientFlowSnapshotHash(value: unknown): string {
  const serialized = stableSerialize(value);
  let hash = 0xcbf29ce484222325n;
  for (let index = 0; index < serialized.length; index += 1) {
    hash ^= BigInt(serialized.charCodeAt(index));
    hash = BigInt.asUintN(64, hash * 0x100000001b3n);
  }
  return hash.toString(16).padStart(16, '0');
}

function zeroResources(): WorkspaceResourceSnapshot {
  return Object.freeze({
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

function previewPhase(status: string, hasContent: boolean): PreviewOwnerPhase {
  if (status === 'loading') return 'loading';
  if (status === 'success') return hasContent ? 'success' : 'empty';
  if (status === 'blocked') return 'blocked';
  if (status === 'canceled') return 'cancelled';
  if (status === 'auth-error') return 'auth-error';
  if (status === 'error') return 'error';
  return 'idle';
}

function normalizeDiagnostics(value: unknown): readonly PreviewDiagnosticV1[] {
  return Object.freeze(array(value).map(entry => {
    if (typeof entry === 'string') return Object.freeze({ code: 'preview', message: entry, pathHint: null });
    const item = record(entry);
    return Object.freeze({
      code: text(item.code) || 'preview',
      message: text(item.message) || text(entry),
      pathHint: text(item.pathHint) || null
    });
  }));
}

function normalizeMissingResources(value: unknown): readonly PreviewMissingResourceV1[] {
  return Object.freeze(array(value).map(entry => {
    const item = record(entry);
    return Object.freeze({
      resourceType: text(item.resourceType),
      resourceKey: text(item.resourceKey),
      description: text(item.description),
      diagnosticCode: text(item.diagnosticCode)
    });
  }));
}

function normalizeArtifacts(value: unknown): readonly PreviewArtifactProjection[] {
  return Object.freeze(array(value).map(entry => {
    const item = record(entry);
    const contentType = text(item.contentType).toLowerCase();
    return Object.freeze({
      artifactId: text(item.artifactId),
      kind: text(item.kind),
      role: text(item.role),
      pathHint: text(item.pathHint) || null,
      contentType,
      length: number(item.length),
      sha256: text(item.sha256),
      createdAtUtc: text(item.createdAtUtc) || null,
      expiresAtUtc: text(item.expiresAtUtc) || null,
      width: item.width === null || item.width === undefined ? null : number(item.width),
      height: item.height === null || item.height === undefined ? null : number(item.height),
      channels: item.channels === null || item.channels === undefined ? null : number(item.channels),
      isImage: contentType.startsWith('image/') || text(item.kind).toLowerCase() === 'image'
    });
  }));
}

function ownerResources(coordinator: NodePreviewCoordinator): WorkspaceResourceSnapshot {
  const diagnostics = coordinator.getResourceDiagnostics();
  return Object.freeze({
    activeSubscriptions: number(diagnostics.activeSubscriptions),
    activeTimers: number(diagnostics.activeTimers),
    activeAnimationFrames: 0,
    activeObservers: 0,
    activeAbortControllers: number(diagnostics.activeAbortControllers),
    activeBlobUrls: number(diagnostics.activeBlobUrls),
    activePreviewArtifactIds: number(diagnostics.activePreviewArtifactIds),
    activeHostSubscriptions: 0,
    inFlightReads: number(diagnostics.inFlightArtifactReads),
    inFlightWrites: 0,
    inFlightPreview: Math.max(
      number(diagnostics.inFlightPreview),
      number(diagnostics.inFlightArtifactReads) + number(diagnostics.inFlightArtifactDeletes)
    ),
    inFlightExecute: 0
  });
}

export function createPreviewOwner(options: {
  readonly projectId: string;
  readonly flowOwner: FlowCanvasOwner;
  readonly api: ApiTransport;
  readonly diagnostics: WorkspaceLifecycleDiagnosticsOwner;
  readonly getInputImageContext?: (
    targetNode: Readonly<Record<string, unknown>>
  ) => Readonly<Record<string, unknown>> | null;
}): PreviewOwner {
  const lease: WorkspaceCapabilityDiagnosticsLease = options.diagnostics.reservePreview(options.projectId);
  const transport: PreviewTransportPort = createPreviewTransportPort(options.api);
  const state = reactive<MutablePreviewOwnerProjection>({
    phase: 'idle',
    projectId: options.projectId,
    selectedNodeId: null,
    selectedNodeType: null,
    title: '',
    statusText: '请选择节点后预览。',
    isStale: false,
    staleReason: null,
    canPreview: false,
    canCancel: false,
    autoPreviewAllowed: true,
    manualReason: null,
    inputImageSrc: null,
    outputImageSrc: null,
    outputData: null,
    artifacts: Object.freeze([]),
    diagnostics: Object.freeze([]),
    missingResources: Object.freeze([]),
    errorMessage: null,
    executionTimeMs: null,
    requestIdentity: null
  });
  let disposed = false;
  let latestSnapshotHash = '';

  const coordinator = new NodePreviewCoordinator({
    getProjectId: () => options.projectId,
    getFlowRevision: () => options.flowOwner.projection.runtime?.flowRevision ?? 0,
    getNodeById: nodeId => options.flowOwner.projection.draft.operators.find(operator =>
      text(operator.id ?? operator.Id) === nodeId) ?? null,
    getOperatorMetadata: type => operatorMetadata(options.flowOwner, type),
    getInputImageBase64: () => null,
    getInputImageContext: node => options.getInputImageContext?.(node) ?? null,
    previewExecutor: async (nodeId, executorOptions) => {
      const flowRevision = number(executorOptions.flowRevision, -1);
      const runtimeRevision = options.flowOwner.projection.runtime?.flowRevision ?? 0;
      if (flowRevision !== runtimeRevision) {
        const error = new DOMException('Preview flow snapshot was superseded.', 'AbortError');
        throw error;
      }
      const flowData = options.flowOwner.projection.draft as unknown as Readonly<Record<string, unknown>>;
      latestSnapshotHash = buildClientFlowSnapshotHash(flowData);
      const command = {
        projectId: options.projectId,
        targetNodeId: nodeId,
        debugSessionId: text(executorOptions.debugSessionId),
        clientRequestSequence: number(executorOptions.clientRequestSequence),
        flowRevision,
        flowData,
        clientSnapshotHash: latestSnapshotHash,
        inputImageBase64: text(executorOptions.inputImageBase64) || null,
        inputImageSourceNodeId: text(executorOptions.inputImageSourceNodeId) || null,
        parameters: null,
        imageFormat: '.png',
        timeoutMs: number(executorOptions.timeoutMs, 15000)
      };
      return transport.previewNode(executorOptions.signal instanceof AbortSignal
        ? { ...command, signal: executorOptions.signal }
        : command);
    },
    artifactClient: transport,
    subscribeStructureState: listener => {
      let initialized = false;
      return watch(
        () => options.flowOwner.projection.runtime?.flowRevision ?? 0,
        (revision, previous) => {
          if (!initialized) {
            initialized = true;
            return;
          }
          if (revision === previous || disposed) return;
          state.isStale = true;
          state.staleReason = '本地流程已变化，正在更新预览。';
          listener();
        },
        { immediate: true }
      );
    },
    debounceMs: 500
  });

  function syncDiagnostics(): void {
    lease.update(ownerResources(coordinator));
  }

  const unsubscribeCoordinator = coordinator.subscribe(rawState => {
    if (disposed) return;
    const presenter = record(rawState.presenter);
    const request = record(rawState.request);
    const observation = record(rawState.observation);
    const observationIdentity = Object.keys(observation).length > 0
      ? record(observation.identity) as unknown as PreviewIdentityV1
      : null;
    const outputData = rawState.outputData && typeof rawState.outputData === 'object' && !Array.isArray(rawState.outputData)
      ? rawState.outputData as PreviewStructuredObjectV1
      : null;
    const artifacts = normalizeArtifacts(rawState.artifacts);
    const hasContent = Boolean(presenter.outputImageSrc) || outputData !== null || artifacts.length > 0;
    const status = text(rawState.status);
    state.phase = previewPhase(status, hasContent);
    state.selectedNodeId = text(rawState.activeNodeId) || null;
    state.selectedNodeType = text(rawState.nodeType) || null;
    state.title = text(rawState.title);
    state.statusText = text(presenter.statusText) || '等待预览';
    state.canPreview = state.selectedNodeId !== null && state.phase !== 'disposed';
    state.canCancel = state.phase === 'loading';
    const previewCost = record(rawState.previewCost);
    state.autoPreviewAllowed = previewCost.autoPreviewAllowed !== false;
    state.manualReason = text(previewCost.reason) || null;
    state.inputImageSrc = text(presenter.inputImageSrc) || null;
    state.outputImageSrc = text(presenter.outputImageSrc) || null;
    state.outputData = outputData;
    state.artifacts = artifacts;
    state.diagnostics = normalizeDiagnostics(rawState.diagnostics);
    state.missingResources = normalizeMissingResources(rawState.missingResources);
    state.errorMessage = text(rawState.errorMessage) || null;
    state.executionTimeMs = rawState.executionTimeMs === null || rawState.executionTimeMs === undefined
      ? null
      : number(rawState.executionTimeMs);
    const requestKey = text(request.requestKey);
    state.requestIdentity = requestKey && state.selectedNodeId
      ? Object.freeze({
          projectId: options.projectId,
          nodeId: state.selectedNodeId,
          flowRevision: number(request.flowRevision),
          clientSnapshotHash: latestSnapshotHash || buildClientFlowSnapshotHash(options.flowOwner.projection.draft),
          requestKey,
          observationIdentity
        })
      : null;
    if (state.phase === 'success' || state.phase === 'empty' || state.phase === 'error' ||
      state.phase === 'blocked' || state.phase === 'auth-error' || state.phase === 'cancelled') {
      const currentRevision = options.flowOwner.projection.runtime?.flowRevision ?? 0;
      state.isStale = Boolean(state.requestIdentity && state.requestIdentity.flowRevision !== currentRevision);
      state.staleReason = state.isStale ? '该结果对应旧的本地流程，请重新预览。' : null;
    }
    syncDiagnostics();
  });
  const unsubscribeTransport = transport.subscribeDiagnostics(() => syncDiagnostics());

  const stopSelectionWatch = watch(
    () => [
      options.flowOwner.projection.runtime?.selectedNodeId ?? null,
      options.flowOwner.projection.runtime?.selectionRevision ?? 0,
      options.flowOwner.projection.catalog.operators
    ] as const,
    ([nodeId], previous) => {
      const previousNodeId = previous?.[0] ?? null;
      if (disposed) return;
      if (previousNodeId && previousNodeId !== nodeId) {
        state.isStale = true;
        state.staleReason = '节点选择已变化。';
      }
      coordinator.setActiveNode(selectedNode(options.flowOwner));
      syncDiagnostics();
    },
    { immediate: true }
  );

  return Object.freeze({
    projectId: options.projectId,
    projection: readonly(state),
    previewNow(): Promise<unknown> {
      if (disposed) return Promise.resolve({ status: 'disposed' });
      state.isStale = state.requestIdentity !== null;
      state.staleReason = state.isStale ? '正在使用当前本地流程重新预览。' : null;
      return coordinator.invalidateActivePreview({ immediate: true, force: true, trigger: 'manual' });
    },
    cancel(reason = '用户取消预览'): void {
      if (disposed) return;
      coordinator.cancelPreview(reason);
      syncDiagnostics();
    },
    readArtifact(
      artifactId: string,
      readOptions: Readonly<{ signal?: AbortSignal; objectUrl?: boolean }> = {}
    ) {
      const identity = state.requestIdentity?.observationIdentity;
      if (!identity || state.isStale) {
        return Promise.reject(new DOMException('Preview artifact request is stale.', 'AbortError'));
      }
      return coordinator.readArtifactForCurrentState(artifactId, identity, readOptions);
    },
    dispose(reason = 'preview-owner-disposed'): void {
      if (disposed) return;
      disposed = true;
      stopSelectionWatch();
      unsubscribeCoordinator();
      coordinator.destroy();
      transport.dispose();
      state.phase = 'disposed';
      state.canPreview = false;
      state.canCancel = false;
      state.inputImageSrc = null;
      state.outputImageSrc = null;
      state.outputData = null;
      state.artifacts = Object.freeze([]);
      state.requestIdentity = null;
      syncDiagnostics();
      void transport.settle().finally(() => {
        unsubscribeTransport();
        lease.update(zeroResources());
        lease.dispose(reason);
      });
    }
  });
}
