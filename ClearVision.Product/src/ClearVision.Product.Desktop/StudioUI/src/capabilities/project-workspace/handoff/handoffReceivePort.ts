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
import type {
  WorkspaceCapabilityDiagnosticsLease,
  WorkspaceLifecycleDiagnosticsOwner
} from '../workspaceLifecycleDiagnostics';

type MutableProjection = {
  -readonly [Key in keyof WorkspaceHandoffReceiveProjection]: WorkspaceHandoffReceiveProjection[Key]
};

export interface WorkspaceHandoffReceiveResult {
  readonly artifact: WorkspaceHandoffArtifactV1;
  readonly source: WorkspaceHandoffSourceV1;
}

export interface WorkspaceHandoffReceivePort {
  readonly projection: DeepReadonly<WorkspaceHandoffReceiveProjection>;
  hasPendingOperation(): boolean;
  hasUnknownOutcome(): boolean;
  quarantineForSessionExpiration(): boolean;
  reconcileAfterReauthentication(): boolean;
  prepareForLeave(): Promise<boolean>;
  settle(): Promise<void>;
  receive(options: Readonly<{
    artifactId: string;
    targetProjectId: string | null;
    isDirty: () => boolean;
    baselineMatches: (artifact: WorkspaceHandoffArtifactV1) => boolean;
    stage: (artifact: WorkspaceHandoffArtifactV1) => Promise<void>;
    rollback?: (artifact: WorkspaceHandoffArtifactV1) => Promise<void>;
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
  diagnostics?: WorkspaceLifecycleDiagnosticsOwner;
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
  let unknownOutcome = false;
  let writeInFlight = false;
  let sessionQuarantined = false;
  const idleWaiters = new Set<() => void>();
  const lease: WorkspaceCapabilityDiagnosticsLease | undefined = options.diagnostics?.reserveCapability(
    'workspace-handoff',
    'handoff'
  );

  function syncDiagnostics(): void {
    const active = state.inFlightCount > 0;
    const loading = active && state.phase === 'workspace-loading-artifact';
    lease?.update(Object.freeze({
      activeSubscriptions: 0,
      activeTimers: 0,
      activeAnimationFrames: 0,
      activeObservers: 0,
      activeAbortControllers: Number(Boolean(controller)),
      activeBlobUrls: 0,
      activePreviewArtifactIds: 0,
      activeHostSubscriptions: 0,
      inFlightReads: loading ? 1 : 0,
      inFlightWrites: active && !loading ? 1 : 0,
      inFlightPreview: 0,
      inFlightExecute: 0
    }));
  }

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
    syncDiagnostics();
  }

  function waitForIdle(): Promise<void> {
    if (state.inFlightCount === 0) return Promise.resolve();
    return new Promise(resolve => idleWaiters.add(resolve));
  }

  function notifyIdle(): void {
    if (state.inFlightCount !== 0) return;
    const waiters = [...idleWaiters];
    idleWaiters.clear();
    for (const resolve of waiters) resolve();
  }

  function assertTarget(artifact: WorkspaceHandoffArtifactV1, projectId: string | null): void {
    if (artifact.targetKind === 'new') {
      if (projectId !== null || artifact.projectBaseline.projectId !== null) {
        throw new ApiConflictError({
          status: 409,
          statusText: 'Conflict',
          url: '',
          payload: { errorCode: 'handoff_new_target_project_forbidden', publicMessage: '新工程候选不能绑定伪造的工程标识。' },
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
        payload: { errorCode: 'handoff_target_project_conflict', publicMessage: '当前工作区工程与交接候选的保存基线不一致。' },
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
    if (!options.api.post) throw new Error('共享服务连接不支持提交交接操作。');
    const payload = await options.api.post(`ai/handoffs/${encodeURIComponent(artifactId)}/${action}`, {
      clientOperationId: operationId,
      targetProjectId: projectId,
      candidateFlowFingerprint: fingerprint
    }, { signal });
    return decodeWorkspaceHandoffArtifactV1(payload);
  }

  return Object.freeze({
    projection: readonly(state),
    hasPendingOperation(): boolean {
      return state.inFlightCount > 0;
    },
    hasUnknownOutcome(): boolean {
      return unknownOutcome;
    },
    async prepareForLeave(): Promise<boolean> {
      if (disposed || unknownOutcome) return !unknownOutcome;
      if (sessionQuarantined) return false;
      if (state.inFlightCount === 0) return true;
      if (state.phase !== 'workspace-loading-artifact') return false;
      controller?.abort('handoff-read-leave');
      await waitForIdle();
      return !unknownOutcome && state.inFlightCount === 0;
    },
    quarantineForSessionExpiration(): boolean {
      if (disposed) return false;
      sessionQuarantined = true;
      generation += 1;
      const preserveUnknown = writeInFlight;
      controller?.abort('session-expired');
      controller = null;
      state.inFlightCount = 0;
      if (preserveUnknown) {
        unknownOutcome = true;
        setPhase('error', '会话已失效；交接写入结果未知，重新认证后必须先协调。',
          'SESSION_UNAUTHORIZED', '重新认证后查询当前交接候选状态；禁止重复接收。');
      } else {
        setPhase('idle', '会话已失效；交接读取已停止，候选状态未改变。', null, '重新认证后可重新读取候选。');
      }
      syncDiagnostics();
      notifyIdle();
      return true;
    },
    reconcileAfterReauthentication(): boolean {
      if (disposed) return false;
      if (unknownOutcome) return false;
      sessionQuarantined = false;
      return true;
    },
    async settle(): Promise<void> {
      await waitForIdle();
    },
    async receive(
      receiveOptions: Parameters<WorkspaceHandoffReceivePort['receive']>[0]
    ): Promise<WorkspaceHandoffReceiveResult | null> {
      if (disposed || sessionQuarantined) return null;
      if (!artifactPattern.test(receiveOptions.artifactId) ||
          receiveOptions.targetProjectId !== null && !guidPattern.test(receiveOptions.targetProjectId)) {
        setPhase('error', '交接链接无效。', 'AI_CANDIDATE_IDENTITY_INVALID', '返回 AI 工作台重新发起交接。');
        return null;
      }
      generation += 1;
      const requestGeneration = generation;
      controller?.abort('handoff-receive-replaced');
      controller = new AbortController();
      state.inFlightCount = 1;
      activeArtifactId = receiveOptions.artifactId;
      setPhase('workspace-loading-artifact', '正在读取并预检 AI 候选。', null, '完成预检后进入工作区本地草稿。');
      let rollbackAttempted = false;
      let rollbackFailed = false;
      let stagingAttempted = false;
      let stagingConfirmed = false;
      let writeAttempted = false;
      let writeOutcomeReconciled = false;
      let stagedArtifact: WorkspaceHandoffArtifactV1 | null = null;

      const rollbackStage = async (artifact: WorkspaceHandoffArtifactV1): Promise<void> => {
        if (!receiveOptions.rollback || rollbackAttempted) return;
        rollbackAttempted = true;
        try {
          await receiveOptions.rollback(artifact);
        } catch {
          rollbackFailed = true;
        }
      };

      try {
        const loaded = decodeWorkspaceHandoffArtifactV1(await options.api.get(
          `ai/handoffs/${encodeURIComponent(receiveOptions.artifactId)}`,
          { signal: controller.signal }
        ));
        if (disposed || requestGeneration !== generation || controller.signal.aborted) return null;
        assertTarget(loaded, receiveOptions.targetProjectId);
        if (!receiveOptions.baselineMatches(loaded)) {
          setPhase(
            'artifact-baseline-conflict',
            '当前工程保存基线与 AI 候选不一致，未预留或装载候选。',
            'AI_CANDIDATE_BASELINE_CHANGED',
            '返回 AI，基于最新工程重新构建候选。'
          );
          return null;
        }
        if (loaded.status === 'expired') {
          unknownOutcome = false;
          setPhase('artifact-expired', 'AI 候选已过期。', 'AI_CANDIDATE_EXPIRED', '返回 AI，基于当前条件重新构建候选。');
          return null;
        }
        if (loaded.status === 'consumed') {
          unknownOutcome = false;
          setPhase('artifact-consumed', 'AI 候选已由工作区接收。', 'AI_CANDIDATE_CONSUMED', '返回 AI 创建新的候选交接。');
          return null;
        }
        if (loaded.status === 'rejected') {
          unknownOutcome = false;
          setPhase('error', 'AI 候选已放弃。', 'AI_CANDIDATE_REJECTED', '返回 AI 重新构建候选。');
          return null;
        }
        if (receiveOptions.isDirty()) {
          setPhase(
            'workspace-dirty-conflict',
            '当前工作区已有未保存修改，未覆盖本地草稿。',
            'WORKSPACE_HAS_LOCAL_DRAFT',
            '先保存或放弃当前草稿，再重新接收候选。'
          );
          return null;
        }
        consumeOperationId = loaded.consumeClientOperationId ?? operationIdFactory();
        writeAttempted = true;
        writeInFlight = true;
        const reserved = await postArtifact(
          loaded.artifactId,
          'consume',
          consumeOperationId,
          receiveOptions.targetProjectId,
          loaded.candidateFlowFingerprint,
          controller.signal
        ).finally(() => { writeInFlight = false; });
        if (disposed || requestGeneration !== generation || controller.signal.aborted) return null;
        if (reserved.status !== 'consuming' || reserved.consumeClientOperationId !== consumeOperationId) {
          throw new WorkspaceHandoffContractError('$.status', 'consuming for the current operation');
        }
        if (receiveOptions.isDirty()) {
          unknownOutcome = true;
          setPhase(
            'workspace-dirty-conflict',
            '接收期间工作区产生了未保存修改，候选未装载。',
            'WORKSPACE_HAS_LOCAL_DRAFT',
            '保留当前草稿并协调交接状态。'
          );
          return null;
        }
        setPhase('workspace-staging', '正在把候选装载到当前工程工作区。', null, '装载成功后确认一次性接收。');
        stagingAttempted = true;
        stagedArtifact = reserved;
        await receiveOptions.stage(reserved);
        if (disposed || requestGeneration !== generation) {
          await rollbackStage(reserved);
          return null;
        }
        let acknowledged: WorkspaceHandoffArtifactV1;
        try {
          writeAttempted = true;
          writeInFlight = true;
          acknowledged = await postArtifact(
            reserved.artifactId,
            'acknowledge',
            consumeOperationId,
            receiveOptions.targetProjectId,
            reserved.candidateFlowFingerprint,
            controller.signal
          ).finally(() => { writeInFlight = false; });
          if (acknowledged.status !== 'consumed') {
            throw new WorkspaceHandoffContractError('$.status', 'consumed after Workspace staging');
          }
          stagingConfirmed = true;
          writeOutcomeReconciled = true;
          unknownOutcome = false;
        } catch (acknowledgeError) {
          let reconciled: WorkspaceHandoffArtifactV1 | null = null;
          if (!(acknowledgeError instanceof ApiAbortError) && !controller.signal.aborted) {
            try {
              reconciled = decodeWorkspaceHandoffArtifactV1(await options.api.get(
                `ai/handoffs/${encodeURIComponent(reserved.artifactId)}`,
                { signal: controller.signal }
              ));
            } catch {
              reconciled = null;
            }
          }
          if (reconciled?.status === 'consumed' &&
              reconciled.consumeClientOperationId === consumeOperationId) {
            acknowledged = reconciled;
            writeOutcomeReconciled = true;
            unknownOutcome = false;
          } else {
            unknownOutcome = true;
            await rollbackStage(reserved);
            throw acknowledgeError;
          }
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
        if (writeAttempted && !writeOutcomeReconciled) unknownOutcome = true;
        if (stagingAttempted && !stagingConfirmed && stagedArtifact) {
          await rollbackStage(stagedArtifact);
        }
        if (disposed || requestGeneration !== generation || error instanceof ApiAbortError) return null;
        const phase = phaseFor(error);
        const message = rollbackAttempted
          ? rollbackFailed
            ? `${failureMessage(error)} 本地草稿回滚失败，请保持页面并继续核对工作区状态。`
            : `${failureMessage(error)} 本地候选已回滚，未留下未确认的流程草稿。`
          : failureMessage(error);
        setPhase(phase, message, failureCode(error) || 'AI_CANDIDATE_RECEIVE_FAILED',
          phase === 'artifact-baseline-conflict'
            ? '返回 AI，基于最新工程重新构建候选。'
            : '核对当前交接候选状态后重试；不要重新创建候选。');
        return null;
      } finally {
        if (requestGeneration === generation) {
          writeInFlight = false;
          controller = null;
          state.inFlightCount = 0;
          syncDiagnostics();
          notifyIdle();
        }
      }
    },
    async reject(reason = 'workspace_staging_failed'): Promise<boolean> {
      if (disposed || sessionQuarantined || !activeArtifactId || !consumeOperationId || !options.api.post) return false;
      const rejectController = new AbortController();
      controller = rejectController;
      state.inFlightCount = 1;
      unknownOutcome = true;
      syncDiagnostics();
      try {
        await options.api.post(`ai/handoffs/${encodeURIComponent(activeArtifactId)}/reject`, {
          clientOperationId: consumeOperationId,
          rejectionCode: reason
        }, { signal: rejectController.signal });
        unknownOutcome = false;
        return true;
      } catch (error) {
        if (!(error instanceof ApiAbortError)) {
          setPhase('error', failureMessage(error), failureCode(error) || 'handoff reject failed',
            '核对交接候选状态后离开。');
        }
        return false;
      } finally {
        if (controller === rejectController) controller = null;
        state.inFlightCount = 0;
        syncDiagnostics();
        notifyIdle();
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
      writeInFlight = false;
      sessionQuarantined = false;
      state.inFlightCount = 0;
      state.phase = 'disposed';
      state.message = '';
      state.blocker = null;
      state.nextStep = '';
      unknownOutcome = false;
      syncDiagnostics();
      notifyIdle();
      lease?.dispose(reason);
    }
  });
}
