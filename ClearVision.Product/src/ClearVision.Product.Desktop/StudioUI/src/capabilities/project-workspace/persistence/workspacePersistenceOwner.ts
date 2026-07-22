import { reactive, readonly, watch, type DeepReadonly } from 'vue';
import {
  ApiAbortError,
  ApiConflictError,
  ApiForbiddenError,
  ApiHttpError,
  ApiNetworkError
} from '@/platform/api';
import type { FlowCanvasOwner } from '../flow';
import {
  buildWorkspaceProjectUpdatePayloadV1,
  encodeWorkspaceFlowDraftUpdateV1,
  encodeWorkspaceFlowUpdateV1,
  encodeWorkspaceGlobalVariablesV1,
  workspacePersistenceFingerprint,
  type WorkspaceJsonObject,
  type WorkspaceProjectUpdatePayloadV1,
  type WorkspaceProjectV1
} from '../workspaceContracts';
import type { WorkspaceGlobalVariablesOwner } from '../global-variables';
import type {
  WorkspaceCapabilityDiagnosticsLease,
  WorkspaceLifecycleDiagnosticsOwner,
  WorkspaceResourceSnapshot
} from '../workspaceLifecycleDiagnostics';
import type { WorkspaceProjectPersistencePort } from './projectPersistencePort';

export type WorkspacePersistencePhase =
  | 'clean'
  | 'dirty'
  | 'saving'
  | 'saved'
  | 'error'
  | 'conflict'
  | 'running'
  | 'readonly'
  | 'unknown-outcome'
  | 'disposed';

export interface WorkspacePersistenceProjection {
  readonly phase: WorkspacePersistencePhase;
  readonly projectId: string;
  readonly persistenceRevision: number;
  readonly dirtyGeneration: number;
  readonly submittedDirtyGeneration: number | null;
  readonly dirty: boolean;
  readonly canSave: boolean;
  readonly canRun: boolean;
  readonly canRetry: boolean;
  readonly canReconcile: boolean;
  readonly canReapplyConflict: boolean;
  readonly canDiscardConflict: boolean;
  readonly message: string;
  readonly errorCode: string | null;
  readonly conflictServerRevision: number | null;
  readonly lastSavedAt: number | null;
}

type MutableWorkspacePersistenceProjection = {
  -readonly [Key in keyof WorkspacePersistenceProjection]: WorkspacePersistenceProjection[Key]
};

export type WorkspaceSaveAttemptStatus =
  | 'saved'
  | 'no-op'
  | 'busy'
  | 'conflict'
  | 'running'
  | 'readonly'
  | 'unknown-outcome'
  | 'failed'
  | 'disposed';

export interface WorkspaceSaveAttemptResult {
  readonly status: WorkspaceSaveAttemptStatus;
  readonly project: WorkspaceProjectV1 | null;
}

export interface WorkspacePersistenceOwner {
  readonly projectId: string;
  readonly projection: DeepReadonly<WorkspacePersistenceProjection>;
  save(): Promise<WorkspaceSaveAttemptResult>;
  retry(): Promise<WorkspaceSaveAttemptResult>;
  reconcile(): Promise<WorkspaceSaveAttemptResult>;
  reapplyConflict(): void;
  discardConflict(): void;
  setRunning(reason?: string): boolean;
  clearRunning(reason?: string): void;
  setReadonly(reason: string): void;
  prepareForLeave(reason?: string): Promise<boolean>;
  settle(): Promise<void>;
  dispose(reason?: string): void;
}

interface SubmittedSave {
  readonly dirtyGeneration: number;
  readonly payload: WorkspaceProjectUpdatePayloadV1;
  readonly contentFingerprint: string;
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

function httpErrorCode(error: unknown): string | null {
  if (!(error instanceof ApiHttpError) || typeof error.payload !== 'object' || error.payload === null) {
    return null;
  }
  const payload = error.payload as Readonly<Record<string, unknown>>;
  const value = payload.code ?? payload.Code;
  return typeof value === 'string' && value.trim() ? value.trim().toUpperCase() : null;
}

function errorMessage(error: unknown): string {
  if (error instanceof Error && error.message.trim()) return error.message;
  return '工程保存失败。';
}

function sameProjectContent(
  project: WorkspaceProjectV1,
  submitted: SubmittedSave
): boolean {
  return project.name === submitted.payload.name &&
    project.description === submitted.payload.description &&
    workspacePersistenceFingerprint({
      flow: encodeWorkspaceFlowUpdateV1(project),
      globalVariables: encodeWorkspaceGlobalVariablesV1(project.globalVariables)
    }) === submitted.contentFingerprint;
}

export function createWorkspacePersistenceOwner(options: {
  readonly baseline: WorkspaceProjectV1;
  readonly flowOwner: FlowCanvasOwner;
  readonly globalVariablesOwner: WorkspaceGlobalVariablesOwner;
  readonly port: WorkspaceProjectPersistencePort;
  readonly diagnostics: WorkspaceLifecycleDiagnosticsOwner;
  readonly readonlyReason?: string | null;
  readonly onBaselineChanged: (project: WorkspaceProjectV1) => void;
}): WorkspacePersistenceOwner {
  const lease: WorkspaceCapabilityDiagnosticsLease =
    options.diagnostics.reservePersistence(options.baseline.id);
  let baseline = options.baseline;
  let baselineContentFingerprint = workspacePersistenceFingerprint({
    flow: encodeWorkspaceFlowUpdateV1(baseline),
    globalVariables: encodeWorkspaceGlobalVariablesV1(baseline.globalVariables)
  });
  let materializedFlowId: string | null = baseline.flow?.id ?? null;
  let lastObservedDraftFingerprint = '';
  let lastObservedFlowRevision = options.flowOwner.projection.runtime?.flowRevision ?? 0;
  let lastObservedVariablesRevision = options.globalVariablesOwner.projection.appliedRevision;
  let conflictServerProject: WorkspaceProjectV1 | null = null;
  let lastSubmitted: SubmittedSave | null = null;
  let disposed = false;
  let operationGeneration = 0;
  let suppressDraftObservation = false;
  let inFlightReads = 0;
  let inFlightWrites = 0;
  let savePromise: Promise<WorkspaceSaveAttemptResult> | null = null;
  const pending = new Set<Promise<unknown>>();
  const initiallyReadonly = Boolean(options.readonlyReason) || !baseline.saveCompatibility.canEncode;
  const state = reactive<MutableWorkspacePersistenceProjection>({
    phase: initiallyReadonly ? 'readonly' : 'clean',
    projectId: baseline.id,
    persistenceRevision: baseline.persistenceRevision,
    dirtyGeneration: 0,
    submittedDirtyGeneration: null,
    dirty: false,
    canSave: false,
    canRun: false,
    canRetry: false,
    canReconcile: false,
    canReapplyConflict: false,
    canDiscardConflict: false,
    message: options.readonlyReason ?? (
      baseline.saveCompatibility.canEncode
        ? '所有修改已保存。'
        : `保存合同阻断：${baseline.saveCompatibility.blockedPaths.join(', ')}`
    ),
    errorCode: null,
    conflictServerRevision: null,
    lastSavedAt: null
  });

  function materializeFlowId(): string {
    materializedFlowId ??= globalThis.crypto.randomUUID();
    return materializedFlowId;
  }

  function encodedDraftFlow(): WorkspaceJsonObject | null {
    return encodeWorkspaceFlowDraftUpdateV1(baseline, options.flowOwner.projection.draft, {
      materializedFlowId,
      createFlowId: materializeFlowId
    });
  }

  function draftFingerprint(): string {
    return workspacePersistenceFingerprint({
      flow: encodedDraftFlow(),
      globalVariables: encodeWorkspaceGlobalVariablesV1(options.globalVariablesOwner.getApplied())
    });
  }

  function syncAvailability(): void {
    if (disposed) return;
    const editable = state.phase !== 'readonly' && state.phase !== 'running' &&
      state.phase !== 'conflict' && state.phase !== 'unknown-outcome' && state.phase !== 'disposed';
    state.canSave = editable && state.dirty && inFlightWrites === 0 && state.phase !== 'error';
    state.canRun = !state.dirty && inFlightReads === 0 && inFlightWrites === 0 &&
      (state.phase === 'clean' || state.phase === 'saved');
    state.canRetry = state.phase === 'error' && state.dirty && inFlightWrites === 0;
    state.canReconcile = (state.phase === 'conflict' || state.phase === 'unknown-outcome') &&
      inFlightReads === 0 && inFlightWrites === 0;
    state.canReapplyConflict = state.phase === 'conflict' && conflictServerProject !== null;
    state.canDiscardConflict = state.canReapplyConflict;
  }

  function syncDiagnostics(activeSubscription = !disposed): void {
    if (disposed) return;
    lease.update(Object.freeze({
      ...zeroResources(),
      activeSubscriptions: activeSubscription ? 1 : 0,
      inFlightReads,
      inFlightWrites
    }));
  }

  function isCurrentOperation(generation: number): boolean {
    return !disposed && generation === operationGeneration && state.projectId === baseline.id &&
      options.port.projectId === state.projectId;
  }

  function track<T>(promise: Promise<T>): Promise<T> {
    pending.add(promise);
    promise.finally(() => pending.delete(promise)).catch(() => {});
    return promise;
  }

  function observeDraft(): void {
    if (disposed || suppressDraftObservation) return;
    const flowRevision = options.flowOwner.projection.runtime?.flowRevision ?? 0;
    const variablesRevision = options.globalVariablesOwner.projection.appliedRevision;
    if (flowRevision === lastObservedFlowRevision && variablesRevision === lastObservedVariablesRevision) return;
    lastObservedFlowRevision = flowRevision;
    lastObservedVariablesRevision = variablesRevision;
    let fingerprint: string;
    try {
      fingerprint = draftFingerprint();
    } catch (error) {
      state.phase = 'readonly';
      state.dirty = true;
      state.errorCode = 'PERSISTENCE_ENCODER_BLOCKED';
      state.message = errorMessage(error);
      options.flowOwner.setMutationGate('readonly');
      syncAvailability();
      return;
    }
    if (fingerprint === lastObservedDraftFingerprint) return;
    lastObservedDraftFingerprint = fingerprint;
    state.dirtyGeneration += 1;
    state.dirty = fingerprint !== baselineContentFingerprint;
    if (state.phase !== 'saving' && state.phase !== 'conflict' && state.phase !== 'unknown-outcome' &&
      state.phase !== 'readonly' && state.phase !== 'running') {
      state.phase = state.dirty ? 'dirty' : 'clean';
      state.message = state.dirty ? '存在未保存修改。' : '所有修改已保存。';
      state.errorCode = null;
    }
    syncAvailability();
  }

  try {
    lastObservedDraftFingerprint = draftFingerprint();
    state.dirty = lastObservedDraftFingerprint !== baselineContentFingerprint;
    if (state.dirty && !initiallyReadonly) {
      state.phase = 'dirty';
      state.message = '存在未保存修改。';
    }
  } catch (error) {
    state.phase = 'readonly';
    state.dirty = true;
    state.errorCode = 'PERSISTENCE_ENCODER_BLOCKED';
    state.message = errorMessage(error);
    options.flowOwner.setMutationGate('readonly');
  }
  syncAvailability();
  syncDiagnostics();

  const stopDraftWatch = watch(
    () => [
      options.flowOwner.projection.draft,
      options.flowOwner.projection.runtime?.flowRevision ?? 0,
      options.globalVariablesOwner.projection.appliedRevision
    ] as const,
    observeDraft,
    { flush: 'sync' }
  );

  function replaceFlow(flow: WorkspaceJsonObject | null, projectName: string): void {
    suppressDraftObservation = true;
    try {
      options.flowOwner.replaceFlow(flow, projectName);
      lastObservedDraftFingerprint = workspacePersistenceFingerprint(flow);
      lastObservedFlowRevision = options.flowOwner.projection.runtime?.flowRevision ?? 0;
    } finally {
      suppressDraftObservation = false;
    }
  }

  function acceptServerBaseline(project: WorkspaceProjectV1): void {
    baseline = project;
    materializedFlowId = project.flow?.id ?? materializedFlowId;
    baselineContentFingerprint = workspacePersistenceFingerprint({
      flow: encodeWorkspaceFlowUpdateV1(project),
      globalVariables: encodeWorkspaceGlobalVariablesV1(project.globalVariables)
    });
    state.persistenceRevision = project.persistenceRevision;
    options.onBaselineChanged(project);
  }

  function applySuccessfulProject(
    project: WorkspaceProjectV1,
    submitted: SubmittedSave
  ): WorkspaceSaveAttemptResult {
    const currentDraft = options.flowOwner.projection.draft;
    const currentVariables = options.globalVariablesOwner.getApplied();
    const editedDuringSave = state.dirtyGeneration !== submitted.dirtyGeneration;
    acceptServerBaseline(project);
    options.globalVariablesOwner.acceptServerBaseline(project.globalVariables, editedDuringSave);
    conflictServerProject = null;
    state.conflictServerRevision = null;
    state.errorCode = null;
    state.lastSavedAt = Date.now();

    if (!editedDuringSave) {
      const canonicalFlow = encodeWorkspaceFlowUpdateV1(project);
      replaceFlow(canonicalFlow, project.name);
      state.dirty = false;
      state.phase = 'saved';
      state.message = `已保存 revision ${project.persistenceRevision}。`;
    } else {
      const replayed = encodeWorkspaceFlowDraftUpdateV1(project, currentDraft, {
        materializedFlowId,
        createFlowId: materializeFlowId
      });
      replaceFlow(replayed, project.name);
      options.globalVariablesOwner.replaceApplied(currentVariables);
      state.dirty = workspacePersistenceFingerprint({
        flow: replayed,
        globalVariables: encodeWorkspaceGlobalVariablesV1(currentVariables)
      }) !== baselineContentFingerprint;
      state.phase = state.dirty ? 'dirty' : 'saved';
      state.message = state.dirty
        ? `revision ${project.persistenceRevision} 已更新；保存期间的新修改仍未保存。`
        : `已保存 revision ${project.persistenceRevision}。`;
    }
    syncAvailability();
    return Object.freeze({ status: 'saved', project });
  }

  async function readServerProject(operation: number): Promise<WorkspaceProjectV1> {
    inFlightReads += 1;
    syncDiagnostics();
    syncAvailability();
    try {
      const project = await options.port.getProject();
      if (!isCurrentOperation(operation)) throw new ApiAbortError('workspace-reconcile', new Error('owner disposed'));
      return project;
    } finally {
      if (isCurrentOperation(operation)) {
        inFlightReads = Math.max(0, inFlightReads - 1);
        syncDiagnostics();
        syncAvailability();
      }
    }
  }

  async function enterConflict(code = 'PSV011'): Promise<WorkspaceSaveAttemptResult> {
    const operation = operationGeneration;
    state.phase = 'conflict';
    state.errorCode = code;
    state.message = '保存冲突：本地 draft 已保留，正在读取服务器版本。';
    options.flowOwner.setMutationGate('readonly');
    syncAvailability();
    try {
      const server = await readServerProject(operation);
      if (!isCurrentOperation(operation)) return Object.freeze({ status: 'disposed', project: null });
      conflictServerProject = server;
      state.conflictServerRevision = conflictServerProject.persistenceRevision;
      state.message = `保存冲突：服务器 revision ${conflictServerProject.persistenceRevision}；请选择重放或放弃本地 draft。`;
    } catch (error) {
      if (!isCurrentOperation(operation)) return Object.freeze({ status: 'disposed', project: null });
      state.message = `保存冲突：本地 draft 已保留；服务器版本读取失败。${errorMessage(error)}`;
    }
    syncAvailability();
    return Object.freeze({ status: 'conflict', project: conflictServerProject });
  }

  async function performSave(): Promise<WorkspaceSaveAttemptResult> {
    if (disposed) return Object.freeze({ status: 'disposed', project: null });
    if (inFlightWrites > 0) return Object.freeze({ status: 'busy', project: null });
    if (state.phase === 'readonly') return Object.freeze({ status: 'readonly', project: null });
    if (state.phase === 'running') return Object.freeze({ status: 'running', project: null });
    if (state.phase === 'conflict' || state.phase === 'unknown-outcome') {
      return Object.freeze({ status: state.phase, project: conflictServerProject });
    }

    const operation = operationGeneration;
    const payload = buildWorkspaceProjectUpdatePayloadV1(
      baseline,
      options.flowOwner.projection.draft,
      {
        materializedFlowId,
        createFlowId: materializeFlowId,
        globalVariables: options.globalVariablesOwner.getApplied()
      }
    );
    const contentFingerprint = workspacePersistenceFingerprint({
      flow: payload.flow,
      globalVariables: payload.globalVariables
    });
    if (contentFingerprint === baselineContentFingerprint && payload.name === baseline.name &&
      payload.description === baseline.description) {
      state.dirty = false;
      state.phase = 'clean';
      state.message = '没有可保存的业务修改。';
      state.errorCode = null;
      syncAvailability();
      return Object.freeze({ status: 'no-op', project: baseline });
    }

    const submitted: SubmittedSave = Object.freeze({
      dirtyGeneration: state.dirtyGeneration,
      payload,
      contentFingerprint
    });
    lastSubmitted = submitted;
    state.phase = 'saving';
    state.submittedDirtyGeneration = submitted.dirtyGeneration;
    state.message = '正在保存工程…';
    state.errorCode = null;
    inFlightWrites += 1;
    syncDiagnostics();
    syncAvailability();
    try {
      const project = await options.port.putProject(payload);
      if (!isCurrentOperation(operation)) return Object.freeze({ status: 'disposed', project: null });
      return applySuccessfulProject(project, submitted);
    } catch (error) {
      if (!isCurrentOperation(operation)) return Object.freeze({ status: 'disposed', project: null });
      const code = httpErrorCode(error);
      if (error instanceof ApiForbiddenError) {
        state.phase = 'readonly';
        state.errorCode = code ?? 'HTTP_403';
        state.message = '后端拒绝保存；Workspace 已切换为只读，本地 draft 保留。';
        options.flowOwner.setMutationGate('readonly');
        syncAvailability();
        return Object.freeze({ status: 'readonly', project: null });
      }
      if (error instanceof ApiConflictError && code === 'GV031') {
        state.phase = 'running';
        state.errorCode = code;
        state.message = '工程正在运行，保存被后端锁定；本地 draft 保留。';
        options.flowOwner.setMutationGate('running');
        syncAvailability();
        return Object.freeze({ status: 'running', project: null });
      }
      if (error instanceof ApiConflictError && code === 'PSV011') {
        return await enterConflict(code);
      }
      if (error instanceof ApiNetworkError || error instanceof ApiAbortError) {
        state.phase = 'unknown-outcome';
        state.errorCode = error.code.toUpperCase();
        state.message = '保存响应未知；本地 draft 已保留，必须先 GET reconcile，禁止盲重试。';
        syncAvailability();
        return Object.freeze({ status: 'unknown-outcome', project: null });
      }
      state.phase = 'error';
      state.errorCode = code ?? 'SAVE_FAILED';
      state.message = `保存失败：${errorMessage(error)} 本地 draft 已保留。`;
      if (error instanceof ApiHttpError) options.globalVariablesOwner.setServerDiagnostics(error.payload);
      syncAvailability();
      return Object.freeze({ status: 'failed', project: null });
    } finally {
      if (isCurrentOperation(operation)) {
        inFlightWrites = Math.max(0, inFlightWrites - 1);
        state.submittedDirtyGeneration = null;
        syncDiagnostics();
        syncAvailability();
      }
    }
  }

  async function performReconcile(): Promise<WorkspaceSaveAttemptResult> {
    if (disposed) return Object.freeze({ status: 'disposed', project: null });
    if (state.phase !== 'conflict' && state.phase !== 'unknown-outcome') {
      return Object.freeze({ status: 'failed', project: null });
    }
    const operation = operationGeneration;
    try {
      const server = await readServerProject(operation);
      if (!isCurrentOperation(operation)) return Object.freeze({ status: 'disposed', project: null });
      conflictServerProject = server;
      state.conflictServerRevision = server.persistenceRevision;
      if (state.phase === 'unknown-outcome' && lastSubmitted && sameProjectContent(server, lastSubmitted)) {
        return applySuccessfulProject(server, lastSubmitted);
      }
      if (state.phase === 'unknown-outcome' && server.persistenceRevision === baseline.persistenceRevision) {
        conflictServerProject = null;
        state.conflictServerRevision = null;
        state.phase = 'error';
        state.errorCode = 'SAVE_NOT_COMMITTED';
        state.message = 'GET reconcile 确认服务器 revision 未变化，可以人工重试。';
        syncAvailability();
        return Object.freeze({ status: 'failed', project: server });
      }
      state.phase = 'conflict';
      state.errorCode = 'PSV011';
      state.message = `服务器 revision ${server.persistenceRevision} 与本地 baseline 不一致；请选择重放或放弃 draft。`;
      options.flowOwner.setMutationGate('readonly');
      syncAvailability();
      return Object.freeze({ status: 'conflict', project: server });
    } catch (error) {
      if (!isCurrentOperation(operation)) return Object.freeze({ status: 'disposed', project: null });
      state.message = `Reconcile 失败：${errorMessage(error)}`;
      syncAvailability();
      return Object.freeze({ status: 'failed', project: null });
    }
  }

  const owner: WorkspacePersistenceOwner = Object.freeze({
    projectId: baseline.id,
    projection: readonly(state),
    save(): Promise<WorkspaceSaveAttemptResult> {
      if (savePromise) return savePromise;
      const operation = track(performSave());
      savePromise = operation.finally(() => { savePromise = null; });
      return savePromise;
    },
    retry(): Promise<WorkspaceSaveAttemptResult> {
      if (state.phase !== 'error') {
        return Promise.resolve(Object.freeze({ status: 'failed', project: null }));
      }
      return owner.save();
    },
    reconcile(): Promise<WorkspaceSaveAttemptResult> {
      return track(performReconcile());
    },
    reapplyConflict(): void {
      if (disposed || state.phase !== 'conflict' || !conflictServerProject) return;
      const currentDraft = options.flowOwner.projection.draft;
      const currentVariables = options.globalVariablesOwner.getApplied();
      const server = conflictServerProject;
      acceptServerBaseline(server);
      options.globalVariablesOwner.acceptServerBaseline(server.globalVariables, true);
      const replayed = encodeWorkspaceFlowDraftUpdateV1(server, currentDraft, {
        materializedFlowId,
        createFlowId: materializeFlowId
      });
      replaceFlow(replayed, server.name);
      options.globalVariablesOwner.replaceApplied(currentVariables);
      state.dirtyGeneration += 1;
      state.dirty = workspacePersistenceFingerprint({
        flow: replayed,
        globalVariables: encodeWorkspaceGlobalVariablesV1(currentVariables)
      }) !== baselineContentFingerprint;
      state.phase = state.dirty ? 'dirty' : 'clean';
      state.errorCode = null;
      state.conflictServerRevision = null;
      state.message = state.dirty
        ? `本地 draft 已重放到 revision ${server.persistenceRevision}；请再次手动保存。`
        : '服务器版本与本地 draft 等价。';
      conflictServerProject = null;
      options.flowOwner.setMutationGate('editable');
      syncAvailability();
    },
    discardConflict(): void {
      if (disposed || state.phase !== 'conflict' || !conflictServerProject) return;
      const server = conflictServerProject;
      acceptServerBaseline(server);
      replaceFlow(encodeWorkspaceFlowUpdateV1(server), server.name);
      options.globalVariablesOwner.replaceApplied(server.globalVariables);
      state.dirty = false;
      state.phase = 'clean';
      state.errorCode = null;
      state.conflictServerRevision = null;
      state.message = `已放弃本地 draft，加载服务器 revision ${server.persistenceRevision}。`;
      conflictServerProject = null;
      options.flowOwner.setMutationGate('editable');
      syncAvailability();
    },
    setRunning(reason = 'Formal Run is active.'): boolean {
      if (disposed || !state.canRun) return false;
      state.phase = 'running';
      state.message = reason;
      state.errorCode = null;
      options.flowOwner.setMutationGate('running');
      syncAvailability();
      return true;
    },
    clearRunning(reason = 'Formal Run completed.'): void {
      if (disposed || state.phase !== 'running') return;
      state.phase = state.dirty ? 'dirty' : 'clean';
      state.message = reason;
      state.errorCode = null;
      options.flowOwner.setMutationGate('editable');
      syncAvailability();
    },
    setReadonly(reason: string): void {
      if (disposed) return;
      state.phase = 'readonly';
      state.message = reason.trim() || 'Workspace 已切换为只读。';
      options.flowOwner.setMutationGate('readonly');
      syncAvailability();
    },
    async prepareForLeave(): Promise<boolean> {
      if (savePromise) await savePromise;
      return !state.dirty && state.phase !== 'conflict' && state.phase !== 'unknown-outcome' &&
        state.phase !== 'saving';
    },
    async settle(): Promise<void> {
      await Promise.allSettled([...pending]);
    },
    dispose(reason = 'workspace-persistence-disposed'): void {
      if (disposed) return;
      disposed = true;
      operationGeneration += 1;
      stopDraftWatch();
      state.phase = 'disposed';
      state.canSave = false;
      state.canRetry = false;
      state.canReconcile = false;
      state.canReapplyConflict = false;
      state.canDiscardConflict = false;
      lease.update(zeroResources());
      lease.dispose(reason);
    }
  });
  return owner;
}
