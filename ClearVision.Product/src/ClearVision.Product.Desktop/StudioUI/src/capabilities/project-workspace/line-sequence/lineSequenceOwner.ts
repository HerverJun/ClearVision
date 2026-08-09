import { reactive, readonly, watch, type DeepReadonly } from 'vue';
import {
  ApiAbortError,
  ApiBadRequestError,
  ApiForbiddenError,
  ApiNetworkError,
  ApiUnauthorizedError,
  type ApiTransport
} from '@/platform/api';
import type { FlowCanvasOwner } from '../flow';
import {
  decodeLineSequenceAnalysisV1,
  decodeLineSequenceRecommendationV1,
  resolveLineSequenceParameterPatch,
  type LineSequenceAnalysisV1,
  type LineSequencePreviewV1,
  type LineSequenceRecommendationV1
} from './lineSequenceContracts';
import type {
  WorkspaceCapabilityDiagnosticsLease,
  WorkspaceLifecycleDiagnosticsOwner
} from '../workspaceLifecycleDiagnostics';

export type LineSequencePhase =
  | 'idle'
  | 'analyzing'
  | 'analyzed'
  | 'recommending'
  | 'recommended'
  | 'applying'
  | 'applied'
  | 'stale'
  | 'error'
  | 'disposed';

export interface LineSequenceProjection {
  readonly phase: LineSequencePhase;
  readonly available: boolean;
  readonly selectedNodeId: string | null;
  readonly sourceFlowRevision: number | null;
  readonly analysis: LineSequenceAnalysisV1 | null;
  readonly recommendation: LineSequenceRecommendationV1 | null;
  readonly preview: LineSequencePreviewV1 | null;
  readonly message: string;
  readonly canAnalyze: boolean;
  readonly canRecommend: boolean;
  readonly canApply: boolean;
}

type MutableProjection = {
  -readonly [Key in keyof LineSequenceProjection]: LineSequenceProjection[Key]
};

export interface LineSequenceOwner {
  readonly projection: DeepReadonly<LineSequenceProjection>;
  analyze(): Promise<void>;
  recommend(): Promise<void>;
  applyRecommendation(): void;
  dispose(reason?: string): void;
}

interface LineSequenceRequestIdentity {
  readonly nodeId: string;
  readonly selectionRevision: number;
  readonly flowRevision: number;
}

function value(source: Readonly<Record<string, unknown>>, camel: string, pascal: string): unknown {
  return source[camel] ?? source[pascal];
}

function selectedNode(flowOwner: FlowCanvasOwner): Readonly<Record<string, unknown>> | null {
  const nodeId = flowOwner.projection.runtime?.selectedNodeId;
  if (!nodeId) return null;
  return flowOwner.projection.draft.operators.find(operator => value(operator, 'id', 'Id') === nodeId) ?? null;
}

function selectedLineSequenceNodeId(flowOwner: FlowCanvasOwner): string | null {
  const node = selectedNode(flowOwner);
  const id = node ? value(node, 'id', 'Id') : null;
  const type = node ? value(node, 'type', 'Type') : null;
  const typeIdentity = typeof type === 'string' || typeof type === 'number'
    ? String(type).toLocaleLowerCase()
    : '';
  return typeof id === 'string' && (typeIdentity === 'detectionsequencejudge' || typeIdentity === '61')
    ? id
    : null;
}

function requestIdentity(flowOwner: FlowCanvasOwner): LineSequenceRequestIdentity | null {
  const nodeId = selectedLineSequenceNodeId(flowOwner);
  const runtime = flowOwner.projection.runtime;
  return nodeId && runtime
    ? Object.freeze({
        nodeId,
        selectionRevision: runtime.selectionRevision,
        flowRevision: runtime.flowRevision
      })
    : null;
}

function errorMessage(error: unknown): string {
  if (error instanceof ApiForbiddenError) return '当前账户没有执行线序分析或推荐的权限。';
  if (error instanceof ApiUnauthorizedError) return '会话已失效，请重新建立会话后继续。';
  if (error instanceof ApiBadRequestError) return '当前流程不满足线序分析条件，请检查节点、资源和外部副作用。';
  if (error instanceof ApiNetworkError) return '线序服务暂时不可达，当前草稿未发生变化。';
  return '线序分析未完成，请检查诊断信息后重试。';
}

export function createLineSequenceOwner(options: Readonly<{
  projectId: string;
  flowOwner: FlowCanvasOwner;
  api: ApiTransport;
  getRecentImageBase64?: () => string | null;
  diagnostics?: WorkspaceLifecycleDiagnosticsOwner | undefined;
}>): LineSequenceOwner {
  if (typeof options.api.post !== 'function') {
    throw new TypeError('Line sequence owner requires POST on the shared ApiTransport.');
  }
  const post = options.api.post.bind(options.api);
  const state = reactive<MutableProjection>({
    phase: 'idle',
    available: false,
    selectedNodeId: null,
    sourceFlowRevision: null,
    analysis: null,
    recommendation: null,
    preview: null,
    message: '等待线序节点。',
    canAnalyze: false,
    canRecommend: false,
    canApply: false
  });
  let disposed = false;
  let requestSequence = 0;
  let controller: AbortController | null = null;
  const lease: WorkspaceCapabilityDiagnosticsLease | undefined = options.diagnostics?.reserveCapability(
    options.projectId,
    'line-sequence'
  );

  function syncDiagnostics(): void {
    lease?.update(Object.freeze({
      activeSubscriptions: disposed ? 0 : 1,
      activeTimers: 0,
      activeAnimationFrames: 0,
      activeObservers: 0,
      activeAbortControllers: Number(Boolean(controller)),
      activeBlobUrls: 0,
      activePreviewArtifactIds: 0,
      activeHostSubscriptions: 0,
      inFlightReads: Number(Boolean(controller)),
      inFlightWrites: 0,
      inFlightPreview: 0,
      inFlightExecute: 0
    }));
  }

  function isCurrent(identity: LineSequenceRequestIdentity): boolean {
    const current = requestIdentity(options.flowOwner);
    return !disposed && current !== null &&
      current.nodeId === identity.nodeId &&
      current.selectionRevision === identity.selectionRevision &&
      current.flowRevision === identity.flowRevision;
  }

  function syncActions(): void {
    const identity = requestIdentity(options.flowOwner);
    state.available = identity !== null;
    state.selectedNodeId = identity?.nodeId ?? null;
    const busy = state.phase === 'analyzing' || state.phase === 'recommending' || state.phase === 'applying';
    state.canAnalyze = !disposed && state.available && !busy;
    state.canRecommend = !disposed && state.phase === 'analyzed' &&
      state.analysis?.success === true && state.analysis.missingResources.length === 0;
    const patch = state.recommendation && identity
      ? resolveLineSequenceParameterPatch(
          options.flowOwner.projection.draft,
          identity.nodeId,
          state.recommendation.finalParameters)
      : null;
    state.canApply = !disposed && state.phase === 'recommended' &&
      options.flowOwner.projection.mutationGate === 'editable' &&
      state.sourceFlowRevision === identity?.flowRevision &&
      state.recommendation?.missingResources.length === 0 && patch !== null;
  }

  function cancelRequest(reason: string): void {
    requestSequence += 1;
    controller?.abort(reason);
    controller = null;
    syncDiagnostics();
  }

  function clearResult(message: string): void {
    state.phase = 'idle';
    state.sourceFlowRevision = null;
    state.analysis = null;
    state.recommendation = null;
    state.preview = null;
    state.message = message;
    syncActions();
  }

  function flowRequest(identity: LineSequenceRequestIdentity): Readonly<Record<string, unknown>> {
    const draft = options.flowOwner.projection.draft;
    const inputImageBase64 = options.getRecentImageBase64?.() ?? null;
    return Object.freeze({
      flowId: draft.id ?? options.projectId,
      targetNodeId: identity.nodeId,
      inputImageBase64: inputImageBase64 || null,
      flowData: Object.freeze({
        ...draft.opaquePassthrough,
        id: draft.id ?? options.projectId,
        name: draft.name,
        operators: draft.operators,
        connections: draft.connections
      })
    });
  }

  async function analyze(): Promise<void> {
    const identity = requestIdentity(options.flowOwner);
    if (disposed || !identity || state.phase === 'analyzing' || state.phase === 'recommending') return;
    cancelRequest('line-sequence-analysis-replaced');
    const sequence = requestSequence;
    const requestController = new AbortController();
    controller = requestController;
    syncDiagnostics();
    state.phase = 'analyzing';
    state.analysis = null;
    state.recommendation = null;
    state.preview = null;
    state.sourceFlowRevision = identity.flowRevision;
    state.message = '正在分析当前线序。';
    syncActions();
    try {
      const payload = await post('autotune/flow-node/preview', flowRequest(identity), {
        signal: requestController.signal
      });
      if (sequence !== requestSequence || !isCurrent(identity)) return;
      const analysis = decodeLineSequenceAnalysisV1(payload);
      if (analysis.targetNodeId.toLocaleLowerCase() !== identity.nodeId.toLocaleLowerCase()) {
        throw new Error('Line sequence analysis target identity mismatch.');
      }
      state.analysis = analysis;
      state.preview = analysis.preview;
      state.phase = 'analyzed';
      state.message = analysis.missingResources.length > 0
        ? '分析完成，但必要资源尚未就绪。'
        : analysis.success
          ? '分析完成，可计算参数建议。'
          : analysis.errorMessage ?? '分析完成，当前线序未通过。';
    } catch (error) {
      if (error instanceof ApiAbortError || requestController.signal.aborted || sequence !== requestSequence) return;
      if (!isCurrent(identity)) return;
      state.phase = 'error';
      state.message = errorMessage(error);
    } finally {
      if (controller === requestController) controller = null;
      syncActions();
      syncDiagnostics();
    }
  }

  async function recommend(): Promise<void> {
    const identity = requestIdentity(options.flowOwner);
    if (disposed || !identity || !state.canRecommend) return;
    cancelRequest('line-sequence-recommendation-replaced');
    const sequence = requestSequence;
    const requestController = new AbortController();
    controller = requestController;
    syncDiagnostics();
    state.phase = 'recommending';
    state.recommendation = null;
    state.sourceFlowRevision = identity.flowRevision;
    state.message = '正在计算白名单参数建议。';
    syncActions();
    try {
      const payload = await post('autotune/scenario', Object.freeze({
        ...flowRequest(identity),
        scenarioKey: 'wire-sequence-terminal',
        maxIterations: 5
      }), { signal: requestController.signal });
      if (sequence !== requestSequence || !isCurrent(identity)) return;
      const recommendation = decodeLineSequenceRecommendationV1(payload);
      if (recommendation.scenarioKey.toLocaleLowerCase() !== 'wire-sequence-terminal') {
        throw new Error('Line sequence recommendation scenario identity mismatch.');
      }
      state.recommendation = recommendation;
      state.preview = recommendation.finalPreview ?? state.preview;
      state.phase = 'recommended';
      const patch = resolveLineSequenceParameterPatch(
        options.flowOwner.projection.draft,
        identity.nodeId,
        recommendation.finalParameters);
      state.message = recommendation.missingResources.length > 0
        ? '推荐已停止，必要资源尚未就绪。'
        : patch === null
          ? '后端未返回可安全应用的白名单参数。'
          : recommendation.isGoalAchieved
            ? '参数建议已收敛，等待应用到草稿。'
            : '已保留限定轮次内的最佳参数，等待人工审查。';
    } catch (error) {
      if (error instanceof ApiAbortError || requestController.signal.aborted || sequence !== requestSequence) return;
      if (!isCurrent(identity)) return;
      state.phase = 'error';
      state.message = errorMessage(error);
    } finally {
      if (controller === requestController) controller = null;
      syncActions();
      syncDiagnostics();
    }
  }

  function applyRecommendation(): void {
    const identity = requestIdentity(options.flowOwner);
    const recommendation = state.recommendation;
    if (disposed || !identity || !recommendation || !state.canApply) return;
    const patch = resolveLineSequenceParameterPatch(
      options.flowOwner.projection.draft,
      identity.nodeId,
      recommendation.finalParameters);
    if (!patch) {
      state.phase = 'error';
      state.message = '没有可安全应用的白名单参数。';
      syncActions();
      return;
    }

    state.phase = 'applying';
    syncActions();
    const result = options.flowOwner.commands.patchNodeParameters({
      nodeId: patch.nodeId,
      values: patch.values
    });
    if (!result.ok) {
      state.phase = 'error';
      state.message = result.message;
      syncActions();
      return;
    }
    state.phase = 'applied';
    state.sourceFlowRevision = result.flowRevision;
    state.message = `已将建议应用到 ${patch.operatorType} 草稿；正式保存仍由工程保存链完成。`;
    syncActions();
  }

  const stopWatch = watch(
    () => [
      options.flowOwner.projection.runtime?.selectedNodeId ?? null,
      options.flowOwner.projection.runtime?.selectionRevision ?? 0,
      options.flowOwner.projection.runtime?.flowRevision ?? 0,
      options.flowOwner.projection.mutationGate
    ] as const,
    ([nodeId, selectionRevision, flowRevision], previous) => {
      if (disposed) return;
      const availableNodeId = selectedLineSequenceNodeId(options.flowOwner);
      const nodeChanged = previous !== undefined && previous[0] !== nodeId;
      const selectionEpochChanged = previous !== undefined && previous[1] !== selectionRevision;
      const flowChanged = previous !== undefined && previous[2] !== flowRevision;
      if (!availableNodeId) {
        cancelRequest('line-sequence-node-left');
        clearResult('选择线序判定节点后可进行分析。');
        return;
      }
      if (nodeChanged) {
        cancelRequest('line-sequence-selection-changed');
        clearResult('等待分析当前线序。');
        return;
      }
      if (flowChanged && state.sourceFlowRevision !== null && state.sourceFlowRevision !== flowRevision &&
          state.phase !== 'idle' && state.phase !== 'applied') {
        cancelRequest('line-sequence-flow-changed');
        state.phase = 'stale';
        state.message = '流程草稿已变化，旧分析和建议已过期。';
        syncActions();
        return;
      }
      if (flowChanged && state.phase === 'applied' && state.sourceFlowRevision === flowRevision) {
        syncActions();
        return;
      }
      if (selectionEpochChanged) {
        cancelRequest('line-sequence-selection-changed');
        clearResult('等待分析当前线序。');
        return;
      }
      syncActions();
    },
    { immediate: true }
  );

  return Object.freeze({
    projection: readonly(state),
    analyze,
    recommend,
    applyRecommendation,
    dispose(reason = 'line-sequence-owner-disposed') {
      if (disposed) return;
      disposed = true;
      cancelRequest(reason);
      stopWatch();
      state.phase = 'disposed';
      state.available = false;
      state.canAnalyze = false;
      state.canRecommend = false;
      state.canApply = false;
      state.message = '线序辅助已卸载。';
      syncDiagnostics();
      lease?.dispose(reason);
    }
  });
  syncDiagnostics();
}
