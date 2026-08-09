import { reactive, readonly, watch, type DeepReadonly } from 'vue';
import type { FlowCanvasOwner } from '../flow';
import type { ImageCanvasOwner } from '../image/imageCanvasOwner';
import type { InspectorMutationResult, InspectorOwner } from '../inspector/inspectorOwner';
import type { PreviewOwner } from '../preview/previewOwner';
import type {
  WorkspaceCapabilityDiagnosticsLease,
  WorkspaceLifecycleDiagnosticsOwner,
  WorkspaceResourceSnapshot
} from '../workspaceLifecycleDiagnostics';
import {
  createRoiCommitPayload,
  createRoiSessionIdentity,
  decodeRoiGeometry,
  resolveRoiEditorDescriptor,
  type RoiEditorDescriptor,
  type RoiGeometry,
  type RoiSessionIdentity,
  type RoiStartupFlags
} from './roiContracts';

export type RoiOwnerPhase =
  | 'unavailable'
  | 'ready'
  | 'editing'
  | 'stale'
  | 'readonly'
  | 'error'
  | 'disposed';

export interface RoiOwnerProjection {
  readonly phase: RoiOwnerPhase;
  readonly descriptor: RoiEditorDescriptor | null;
  readonly geometry: RoiGeometry | null;
  readonly dirty: boolean;
  readonly message: string;
  readonly canStart: boolean;
  readonly canConfirm: boolean;
  readonly canCancel: boolean;
  readonly canUndo: boolean;
  readonly canRedo: boolean;
  readonly sessionIdentity: RoiSessionIdentity | null;
  readonly lastCommit: InspectorMutationResult | null;
}

type MutableRoiOwnerProjection = {
  -readonly [Key in keyof RoiOwnerProjection]: RoiOwnerProjection[Key]
};

export interface RoiInteractionOwner {
  readonly projectId: string;
  readonly projection: DeepReadonly<RoiOwnerProjection>;
  start(): boolean;
  confirm(): InspectorMutationResult | null;
  cancel(reason?: string): void;
  undo(): void;
  redo(): void;
  dispose(reason?: string): void;
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

function text(value: unknown): string {
  return typeof value === 'string' ? value : value === null || value === undefined ? '' : String(value);
}

function number(value: unknown, fallback = 0): number {
  return typeof value === 'number' && Number.isFinite(value) ? value : fallback;
}

function record(value: unknown): Readonly<Record<string, unknown>> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
    ? value as Readonly<Record<string, unknown>>
    : Object.freeze({});
}

function nodeById(flowOwner: FlowCanvasOwner, nodeId: string): Readonly<Record<string, unknown>> | null {
  return flowOwner.projection.draft.operators.find(node => text(node.id ?? node.Id) === nodeId) ?? null;
}

function selectedNode(flowOwner: FlowCanvasOwner): Readonly<Record<string, unknown>> | null {
  const nodeId = flowOwner.projection.runtime?.selectedNodeId;
  return nodeId ? nodeById(flowOwner, nodeId) : null;
}

function parameterValues(node: Readonly<Record<string, unknown>>): Readonly<Record<string, unknown>> {
  const source = node.parameters ?? node.Parameters;
  const parameters: readonly unknown[] = Array.isArray(source) ? source : Object.freeze([]);
  const values: Record<string, unknown> = {};
  for (const value of parameters) {
    const parameter = record(value);
    const name = text(parameter.name ?? parameter.Name);
    if (!name) continue;
    values[name] = parameter.value ?? parameter.Value ?? parameter.defaultValue ?? parameter.DefaultValue;
  }
  return Object.freeze(values);
}

function rectangleGeometry(node: Readonly<Record<string, unknown>>): RoiGeometry {
  const values = parameterValues(node);
  return Object.freeze({
    kind: 'rectangle',
    x: number(values.X),
    y: number(values.Y),
    width: Math.max(1, number(values.Width, 20)),
    height: Math.max(1, number(values.Height, 20))
  });
}

function portIdByName(node: Readonly<Record<string, unknown>>, name: string, input: boolean): string | null {
  const source = input ? node.inputPorts ?? node.InputPorts : node.outputPorts ?? node.OutputPorts;
  const ports = Array.isArray(source) ? source : [];
  const identity = name.toLowerCase();
  const port = ports.map(record).find(item => text(item.name ?? item.Name).toLowerCase() === identity);
  return port ? text(port.id ?? port.Id) || null : null;
}

function caliperGeometry(
  flowOwner: FlowCanvasOwner,
  caliperNode: Readonly<Record<string, unknown>>,
  imageWidth: number,
  imageHeight: number
): Readonly<{ geometry: RoiGeometry | null; error: string | null }> {
  const caliperNodeId = text(caliperNode.id ?? caliperNode.Id);
  const targetPortId = portIdByName(caliperNode, 'SearchRegion', true);
  if (!targetPortId) return Object.freeze({ geometry: null, error: 'CaliperTool.SearchRegion 输入端口不存在。' });
  const connection = flowOwner.projection.draft.connections.find(item =>
    text(item.targetOperatorId ?? item.TargetOperatorId) === caliperNodeId &&
    text(item.targetPortId ?? item.TargetPortId) === targetPortId);
  if (!connection) {
    const width = Math.max(20, Math.round(imageWidth * 0.3));
    const height = Math.max(20, Math.round(imageHeight * 0.2));
    return Object.freeze({
      geometry: Object.freeze({
        kind: 'rectangle',
        x: Math.max(0, Math.round((imageWidth - width) / 2)),
        y: Math.max(0, Math.round((imageHeight - height) / 2)),
        width,
        height
      }),
      error: null
    });
  }
  const sourceNodeId = text(connection.sourceOperatorId ?? connection.SourceOperatorId);
  const sourceNode = nodeById(flowOwner, sourceNodeId);
  if (!sourceNode || text(sourceNode.type ?? sourceNode.Type).toLowerCase() !== 'rectangleregion') {
    return Object.freeze({
      geometry: null,
      error: 'CaliperTool.SearchRegion 已连接到非 RectangleRegion 节点。'
    });
  }
  const sourcePortId = text(connection.sourcePortId ?? connection.SourcePortId);
  if (portIdByName(sourceNode, 'Rectangle', false) !== sourcePortId) {
    return Object.freeze({ geometry: null, error: 'SearchRegion 连接的输出不是 Rectangle。' });
  }
  return Object.freeze({ geometry: rectangleGeometry(sourceNode), error: null });
}

function sameGeometry(left: RoiGeometry | null, right: RoiGeometry | null): boolean {
  return JSON.stringify(left) === JSON.stringify(right);
}

export function createRoiInteractionOwner(options: {
  readonly projectId: string;
  readonly flowOwner: FlowCanvasOwner;
  readonly inspectorOwner: InspectorOwner;
  readonly previewOwner: PreviewOwner;
  readonly imageOwner: ImageCanvasOwner;
  readonly diagnostics: WorkspaceLifecycleDiagnosticsOwner;
  readonly startupFlags: RoiStartupFlags;
}): RoiInteractionOwner {
  const lease: WorkspaceCapabilityDiagnosticsLease = options.diagnostics.reserveRoi(options.projectId);
  const state = reactive<MutableRoiOwnerProjection>({
    phase: 'unavailable',
    descriptor: null,
    geometry: null,
    dirty: false,
    message: '请选择支持图像几何编辑的节点。',
    canStart: false,
    canConfirm: false,
    canCancel: false,
    canUndo: false,
    canRedo: false,
    sessionIdentity: null,
    lastCommit: null
  });
  let disposed = false;
  let initialGeometry: RoiGeometry | null = null;
  let draftGeometry: RoiGeometry | null = null;
  let historyDepth = 0;
  let futureDepth = 0;
  const draftKey = `roi:${options.projectId}`;

  function syncDiagnostics(): void {
    if (disposed) return;
    lease.update(Object.freeze({
      ...zeroResources(),
      activeSubscriptions: 1,
      inFlightWrites: 0
    }));
  }

  function resetSession(reason: string, phase?: RoiOwnerPhase): void {
    options.inspectorOwner.setDraftActive(draftKey, false);
    options.imageOwner.roi.end();
    initialGeometry = null;
    draftGeometry = null;
    historyDepth = 0;
    futureDepth = 0;
    state.geometry = null;
    state.dirty = false;
    state.canConfirm = false;
    state.canCancel = false;
    state.canUndo = false;
    state.canRedo = false;
    state.sessionIdentity = null;
    if (phase) state.phase = phase;
    state.message = reason;
    syncAvailability();
  }

  function currentGeometry(descriptor: RoiEditorDescriptor, node: Readonly<Record<string, unknown>>): Readonly<{
    geometry: RoiGeometry | null;
    error: string | null;
  }> {
    if (descriptor.commandKind === 'caliper-structural') {
      return caliperGeometry(
        options.flowOwner,
        node,
        options.imageOwner.projection.width,
        options.imageOwner.projection.height
      );
    }
    return Object.freeze({
      geometry: decodeRoiGeometry(node, descriptor, {
        width: options.imageOwner.projection.width,
        height: options.imageOwner.projection.height
      }),
      error: null
    });
  }

  function syncAvailability(): void {
    if (disposed || state.sessionIdentity) return;
    const node = selectedNode(options.flowOwner);
    if (!node) {
      state.phase = 'unavailable';
      state.descriptor = null;
      state.message = '请选择支持图像几何编辑的节点。';
      state.canStart = false;
      return;
    }
    const descriptor = resolveRoiEditorDescriptor(node, options.startupFlags);
    state.descriptor = descriptor;
    if (!descriptor.supported || !descriptor.editable) {
      state.phase = 'unavailable';
      state.message = descriptor.message;
      state.canStart = false;
      return;
    }
    if (options.flowOwner.projection.mutationGate !== 'editable') {
      state.phase = 'readonly';
      state.message = options.flowOwner.projection.mutationGate === 'running'
        ? '流程正在运行，ROI 仅可查看。'
        : '当前工程只读，ROI 仅可查看。';
      state.canStart = false;
      return;
    }
    if (options.previewOwner.projection.isStale) {
      state.phase = 'stale';
      state.message = '预览图像已过期，请先重新预览。';
      state.canStart = false;
      return;
    }
    if (options.imageOwner.projection.phase !== 'ready' || !options.imageOwner.projection.imageIdentity) {
      state.phase = 'unavailable';
      state.message = '当前没有可用于 ROI 编辑的预览图像。';
      state.canStart = false;
      return;
    }
    const resolved = currentGeometry(descriptor, node);
    if (!resolved.geometry) {
      state.phase = 'error';
      state.message = resolved.error ?? '当前 ROI 参数无效，无法开始图上编辑。';
      state.canStart = false;
      return;
    }
    state.phase = 'ready';
    state.message = descriptor.kind === 'npoint-sequence'
      ? `${descriptor.message} 新增点需要明确 WorldX / WorldY，本阶段只编辑既有点。`
      : descriptor.message;
    state.canStart = true;
  }

  function activeIdentity(): RoiSessionIdentity | null {
    const nodeId = options.flowOwner.projection.runtime?.selectedNodeId;
    const requestKey = options.previewOwner.projection.requestIdentity?.requestKey;
    if (!nodeId || !requestKey || !options.imageOwner.projection.imageIdentity) return null;
    return createRoiSessionIdentity({
      projectId: options.projectId,
      nodeId,
      selectionRevision: options.flowOwner.projection.runtime?.selectionRevision ?? 0,
      flowRevision: options.flowOwner.projection.runtime?.flowRevision ?? 0,
      previewRequestKey: requestKey,
      imageGeneration: options.imageOwner.projection.imageGeneration
    });
  }

  const stopWatch = watch(
    () => [
      options.flowOwner.projection.runtime?.selectedNodeId ?? null,
      options.flowOwner.projection.runtime?.selectionRevision ?? 0,
      options.flowOwner.projection.runtime?.flowRevision ?? 0,
      options.flowOwner.projection.mutationGate,
      options.previewOwner.projection.requestIdentity?.requestKey ?? null,
      options.previewOwner.projection.isStale,
      options.imageOwner.projection.imageIdentity,
      options.imageOwner.projection.imageGeneration,
      options.imageOwner.projection.phase
    ] as const,
    () => {
      if (disposed) return;
      if (state.sessionIdentity) {
        const current = activeIdentity();
        if (!current || current.key !== state.sessionIdentity.key ||
          options.flowOwner.projection.mutationGate !== 'editable' ||
          options.previewOwner.projection.isStale) {
          resetSession('ROI 编辑会话已因选择、Flow、预览或图像变化而取消。', 'stale');
          return;
        }
      }
      syncAvailability();
    },
    { immediate: true }
  );

  return Object.freeze({
    projectId: options.projectId,
    projection: readonly(state),
    start(): boolean {
      if (disposed || !state.canStart || !state.descriptor) return false;
      const node = selectedNode(options.flowOwner);
      const identity = activeIdentity();
      if (!node || !identity) return false;
      const resolved = currentGeometry(state.descriptor, node);
      if (!resolved.geometry) {
        state.phase = 'error';
        state.message = resolved.error ?? 'ROI 参数无效。';
        return false;
      }
      initialGeometry = resolved.geometry;
      draftGeometry = resolved.geometry;
      historyDepth = 0;
      futureDepth = 0;
      state.geometry = resolved.geometry;
      state.dirty = false;
      state.sessionIdentity = identity;
      const began = options.imageOwner.roi.begin(resolved.geometry, (geometry, phase) => {
        if (disposed || !state.sessionIdentity || !geometry) return;
        if (phase === 'cancel') {
          draftGeometry = initialGeometry;
          state.geometry = initialGeometry;
          state.dirty = false;
          return;
        }
        draftGeometry = geometry as RoiGeometry;
        state.geometry = draftGeometry;
        state.dirty = !sameGeometry(initialGeometry, draftGeometry);
        if (phase === 'commit') {
          historyDepth += 1;
          futureDepth = 0;
        }
        state.canConfirm = state.dirty;
        state.canUndo = historyDepth > 0;
        state.canRedo = futureDepth > 0;
        options.imageOwner.roi.showStatistics(draftGeometry);
      });
      if (!began) {
        state.sessionIdentity = null;
        initialGeometry = null;
        draftGeometry = null;
        return false;
      }
      options.inspectorOwner.setDraftActive(draftKey, true);
      state.phase = 'editing';
      state.message = 'ROI 草稿仅保存在当前编辑会话；确认后才写入流程草稿。';
      state.canStart = false;
      state.canConfirm = false;
      state.canCancel = true;
      syncDiagnostics();
      return true;
    },
    confirm(): InspectorMutationResult | null {
      if (disposed || !state.sessionIdentity || !state.descriptor || !draftGeometry || !state.canConfirm) {
        return null;
      }
      const payload = createRoiCommitPayload(state.descriptor, draftGeometry);
      if (payload.kind === 'unsupported') {
        state.phase = 'error';
        state.message = payload.reason;
        return null;
      }
      const identity = state.sessionIdentity;
      const result = options.inspectorOwner.commitImageBacked({
        nodeId: payload.kind === 'caliper-structural' ? payload.caliperNodeId : payload.nodeId,
        selectionRevision: identity.selectionRevision,
        flowRevision: identity.flowRevision,
        mode: payload.kind === 'caliper-structural' ? 'caliper-search-region' : 'parameters',
        values: payload.kind === 'caliper-structural' ? payload.regionParameters : payload.values
      });
      state.lastCommit = result;
      if (!result.ok) {
        state.phase = result.code === 'readonly' || result.code === 'running' ? 'readonly' : 'error';
        state.message = result.message;
        return result;
      }
      options.inspectorOwner.setDraftActive(draftKey, false);
      options.imageOwner.roi.end();
      state.sessionIdentity = null;
      initialGeometry = null;
      draftGeometry = null;
      state.geometry = null;
      state.dirty = false;
      state.canConfirm = false;
      state.canCancel = false;
      state.canUndo = false;
      state.canRedo = false;
      state.message = 'ROI 已通过唯一编辑命令写入流程草稿。';
      syncAvailability();
      return result;
    },
    cancel(reason = '已取消 ROI 编辑，流程草稿未变化。'): void {
      if (disposed || !state.sessionIdentity) return;
      if (initialGeometry) options.imageOwner.roi.replace(initialGeometry, true);
      resetSession(reason, 'ready');
    },
    undo(): void {
      if (disposed || !state.sessionIdentity) return;
      const geometry = options.imageOwner.roi.undo();
      if (!geometry) return;
      draftGeometry = geometry as RoiGeometry;
      state.geometry = draftGeometry;
      state.dirty = !sameGeometry(initialGeometry, draftGeometry);
      historyDepth = Math.max(0, historyDepth - 1);
      futureDepth += 1;
      state.canUndo = historyDepth > 0;
      state.canRedo = futureDepth > 0;
      state.canConfirm = state.dirty;
    },
    redo(): void {
      if (disposed || !state.sessionIdentity) return;
      const geometry = options.imageOwner.roi.redo();
      if (!geometry) return;
      draftGeometry = geometry as RoiGeometry;
      state.geometry = draftGeometry;
      state.dirty = !sameGeometry(initialGeometry, draftGeometry);
      historyDepth += 1;
      futureDepth = Math.max(0, futureDepth - 1);
      state.canUndo = historyDepth > 0;
      state.canRedo = futureDepth > 0;
      state.canConfirm = state.dirty;
    },
    dispose(reason = 'roi-owner-disposed'): void {
      if (disposed) return;
      disposed = true;
      stopWatch();
      options.inspectorOwner.setDraftActive(draftKey, false);
      options.imageOwner.roi.end();
      state.phase = 'disposed';
      state.descriptor = null;
      state.geometry = null;
      state.sessionIdentity = null;
      state.canStart = false;
      state.canConfirm = false;
      state.canCancel = false;
      lease.update(zeroResources());
      lease.dispose(reason);
    }
  });
}
