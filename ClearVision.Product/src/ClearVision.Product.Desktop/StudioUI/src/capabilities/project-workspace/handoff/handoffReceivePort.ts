import { reactive, readonly, type DeepReadonly } from 'vue';
import {
  ApiAbortError,
  ApiConflictError,
  ApiHttpError,
  ApiNetworkError,
  type ApiTransport
} from '@/platform/api';
import { decodeWorkspaceHandoffArtifactV1 } from './handoffDecoder';
import {
  WorkspaceHandoffContractError,
  type WorkspaceHandoffArtifactV1,
  type WorkspaceHandoffReceivePhase,
  type WorkspaceHandoffReceiveProjection,
  type WorkspaceHandoffSourceV1
} from './handoffContracts';

type MutableProjection = {
  -readonly [Key in keyof WorkspaceHandoffReceiveProjection]: WorkspaceHandoffReceiveProjection[Key]
};

export interface WorkspaceHandoffReceiveResult {
  readonly artifact: WorkspaceHandoffArtifactV1;
  readonly source: WorkspaceHandoffSourceV1;
}

export interface WorkspaceHandoffReceivePort {
  readonly projection: DeepReadonly<WorkspaceHandoffReceiveProjection>;
  receive(options: Readonly<{
    artifactId: string;
    targetProjectId: string | null;
    isDirty: () => boolean;
    baselineMatches: (artifact: WorkspaceHandoffArtifactV1) => boolean;
    stage: (artifact: WorkspaceHandoffArtifactV1) => Promise<void>;
  }>): Promise<WorkspaceHandoffReceiveResult | null>;
  reject(reason?: string): Promise<boolean>;
  dispose(reason?: string): void;
}

const artifactPattern = /^[0-9a-f]{32}$/i;
const guidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;

function failureCode(error: unknown): string {
  if (!(error instanceof ApiHttpError) || typeof error.payload !== 'object' || error.payload === null) return '';
  const payload = error.payload as Record<string, unknown>;
  return typeof payload.errorCode === 'string' ? payload.errorCode.trim().toLowerCase() : '';
}

function failureMessage(error: unknown): string {
  if (error instanceof ApiHttpError && typeof error.payload === 'object' && error.payload !== null) {
    const payload = error.payload as Record<string, unknown>;
    if (typeof payload.publicMessage === 'string' && payload.publicMessage.trim()) return payload.publicMessage.trim();
  }
  if (error instanceof ApiNetworkError) return '无法确认交接状态；请恢复本地服务后协调，禁止重新创建候选。';
  return error instanceof Error && error.message.trim() ? error.message : '工作区未能接收 AI 候选。';
}

function phaseFor(error: unknown): WorkspaceHandoffReceivePhase {
  const code = failureCode(error);
  if ((error instanceof ApiHttpError && error.status === 410) || code === 'handoff_expired') return 'artifact-expired';
  if (code === 'handoff_consumed') return 'artifact-consumed';
  if (code.includes('baseline') || code.includes('revision') || code.includes('target_project')) {
    return 'artifact-baseline-conflict';
  }
  if (code.includes('dirty')) return 'workspace-dirty-conflict';
  return 'error';
}

export function createWorkspaceHandoffReceivePort(options: Readonly<{
  api: ApiTransport;
  operationIdFactory?: () => string;
  now?: () => Date;
}>): WorkspaceHandoffReceivePort {
  const operationIdFactory = options.operationIdFactory ?? (() => globalThis.crypto.randomUUID());
  const now = options.now ?? (() => new Date());
  const state = reactive<MutableProjection>({
    phase: 'idle',
    message: '',
    blocker: null,
    nextStep: '',
    inFlightCount: 0
  });
  let disposed = false;
  let generation = 0;
  let controller: AbortController | null = null;
  let activeArtifactId: string | null = null;
  let consumeOperationId: string | null = null;

  function setPhase(
    phase: WorkspaceHandoffReceivePhase,
    message: string,
    blocker: string | null,
    nextStep: string
  ): void {
    if (disposed) return;
    state.phase = phase;
    state.message = message;
    state.blocker = blocker;
    state.nextStep = nextStep;
  }

  function assertTarget(artifact: WorkspaceHandoffArtifactV1, projectId: string | null): void {
    if (artifact.targetKind === 'new') {
      if (projectId !== null || artifact.projectBaseline.projectId !== null) {
        throw new ApiConflictError({
          status: 409,
          statusText: 'Conflict',
          url: '',
          payload: { errorCode: 'handoff_new_target_project_forbidden', publicMessage: '新工程候选不能绑定伪造的 Project id。' },
          responseBody: ''
        });
      }
      return;
    }
    if (!projectId || artifact.projectBaseline.projectId !== projectId) {
      throw new ApiConflictError({
        status: 409,
        statusText: 'Conflict',
        url: '',
        payload: { errorCode: 'handoff_target_project_conflict', publicMessage: '当前工作区工程与 artifact baseline 不一致。' },
        responseBody: ''
      });
    }
  }

  async function postArtifact(
    artifactId: string,
    action: 'consume' | 'acknowledge',
    operationId: string,
    projectId: string | null,
    fingerprint: string,
    signal: AbortSignal
  ): Promise<WorkspaceHandoffArtifactV1> {
    if (!options.api.post) throw new Error('Shared API transport does not support POST.');
    const payload = await options.api.post(`ai/handoffs/${encodeURIComponent(artifactId)}/${action}`, {
      clientOperationId: operationId,
      targetProjectId: projectId,
      candidateFlowFingerprint: fingerprint
    }, { signal });
    return decodeWorkspaceHandoffArtifactV1(payload);
  }

  return Object.freeze({
    projection: readonly(state),
    async receive(
      receiveOptions: Parameters<WorkspaceHandoffReceivePort['receive']>[0]
    ): Promise<WorkspaceHandoffReceiveResult | null> {
      if (disposed) return null;
      if (!artifactPattern.test(receiveOptions.artifactId) ||
          receiveOptions.targetProjectId !== null && !guidPattern.test(receiveOptions.targetProjectId)) {
        setPhase('error', '交接链接无效。', 'artifact identity invalid', '返回 AI 工作台重新发起交接。');
        return null;
      }
      generation += 1;
      const requestGeneration = generation;
      controller?.abort('handoff-receive-replaced');
      controller = new AbortController();
      state.inFlightCount = 1;
      activeArtifactId = receiveOptions.artifactId;
      setPhase('workspace-loading-artifact', '正在读取并预检 AI 候选。', null, '完成预检后进入工作区本地草稿。');
      try {
        const loaded = decodeWorkspaceHandoffArtifactV1(await options.api.get(
          `ai/handoffs/${encodeURIComponent(receiveOptions.artifactId)}`,
          { signal: controller.signal }
        ));
        if (disposed || requestGeneration !== generation) return null;
        assertTarget(loaded, receiveOptions.targetProjectId);
        if (!receiveOptions.baselineMatches(loaded)) {
          setPhase(
            'artifact-baseline-conflict',
            '当前工程保存基线与 AI 候选不一致，未预留或装载候选。',
            'artifact baseline changed',
            '返回 AI 基于最新工程重新 Build。'
          );
          return null;
        }
        if (loaded.status === 'expired') {
          setPhase('artifact-expired', 'AI 候选已过期。', 'artifact expired', '返回 AI 基于当前条件重新 Build。');
          return null;
        }
        if (loaded.status === 'consumed') {
          setPhase('artifact-consumed', 'AI 候选已由工作区接收。', 'artifact consumed', '返回 AI 创建新的候选交接。');
          return null;
        }
        if (loaded.status === 'rejected') {
          setPhase('error', 'AI 候选已放弃。', 'artifact rejected', '返回 AI 重新 Build。');
          return null;
        }
        if (receiveOptions.isDirty()) {
          setPhase(
            'workspace-dirty-conflict',
            '当前工作区已有未保存修改，未覆盖本地草稿。',
            'workspace dirty',
            '先保存或放弃当前草稿，再重新接收候选。'
          );
          return null;
        }
        consumeOperationId = loaded.consumeClientOperationId ?? operationIdFactory();
        const reserved = await postArtifact(
          loaded.artifactId,
          'consume',
          consumeOperationId,
          receiveOptions.targetProjectId,
          loaded.candidateFlowFingerprint,
          controller.signal
        );
        if (disposed || requestGeneration !== generation) return null;
        if (reserved.status !== 'consuming' || reserved.consumeClientOperationId !== consumeOperationId) {
          throw new WorkspaceHandoffContractError('$.status', 'consuming for the current operation');
        }
        if (receiveOptions.isDirty()) {
          setPhase(
            'workspace-dirty-conflict',
            '接收期间工作区产生了未保存修改，候选未装载。',
            'workspace dirty',
            '保留当前草稿并协调交接状态。'
          );
          return null;
        }
        setPhase('workspace-staging', '正在把候选装载到唯一 Workspace owner。', null, '装载成功后确认一次性接收。');
        await receiveOptions.stage(reserved);
        if (disposed || requestGeneration !== generation) return null;
        const acknowledged = await postArtifact(
          reserved.artifactId,
          'acknowledge',
          consumeOperationId,
          receiveOptions.targetProjectId,
          reserved.candidateFlowFingerprint,
          controller.signal
        );
        if (acknowledged.status !== 'consumed') {
          throw new WorkspaceHandoffContractError('$.status', 'consumed after Workspace staging');
        }
        const source = Object.freeze({
          artifactId: acknowledged.artifactId,
          sessionId: acknowledged.sessionId,
          planId: acknowledged.planId,
          buildId: acknowledged.build.buildId,
          candidateFlowFingerprint: acknowledged.candidateFlowFingerprint,
          targetKind: acknowledged.targetKind,
          receivedAtUtc: now().toISOString()
        });
        setPhase(
          'workspace-staged-unsaved',
          'AI 候选已进入工作区本地草稿，尚未保存。',
          null,
          '检查画布、参数和资源后，使用现有“保存”操作显式保存工程。'
        );
        return Object.freeze({ artifact: acknowledged, source });
      } catch (error) {
        if (disposed || requestGeneration !== generation || error instanceof ApiAbortError) return null;
        const phase = phaseFor(error);
        setPhase(phase, failureMessage(error), failureCode(error) || 'handoff receive failed',
          phase === 'artifact-baseline-conflict'
            ? '返回 AI 基于最新工程重新 Build。'
            : '协调当前 artifact 状态后重试；不要重新创建候选。');
        return null;
      } finally {
        if (requestGeneration === generation) {
          controller = null;
          state.inFlightCount = 0;
        }
      }
    },
    async reject(reason = 'workspace_staging_failed'): Promise<boolean> {
      if (disposed || !activeArtifactId || !consumeOperationId || !options.api.post) return false;
      const rejectController = new AbortController();
      controller = rejectController;
      state.inFlightCount = 1;
      try {
        await options.api.post(`ai/handoffs/${encodeURIComponent(activeArtifactId)}/reject`, {
          clientOperationId: consumeOperationId,
          rejectionCode: reason
        }, { signal: rejectController.signal });
        return true;
      } catch (error) {
        if (!(error instanceof ApiAbortError)) {
          setPhase('error', failureMessage(error), failureCode(error) || 'handoff reject failed',
            '协调 artifact 状态后离开。');
        }
        return false;
      } finally {
        if (controller === rejectController) controller = null;
        state.inFlightCount = 0;
      }
    },
    dispose(reason = 'handoff-receive-disposed'): void {
      if (disposed) return;
      disposed = true;
      generation += 1;
      controller?.abort(reason);
      controller = null;
      activeArtifactId = null;
      consumeOperationId = null;
      state.inFlightCount = 0;
      state.phase = 'disposed';
      state.message = '';
      state.blocker = null;
      state.nextStep = '';
    }
  });
}
