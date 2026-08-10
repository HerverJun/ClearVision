import { reactive, readonly, type DeepReadonly } from 'vue';
import type { ProjectLifecycleCommandOwner } from '@/capabilities/project-lifecycle';
import type { InspectionRunOwner } from '@/capabilities/inspection-run';
import type {
  WorkspaceLeaveProtectionSnapshot,
  WorkspaceRuntime
} from '@/capabilities/project-workspace/workspaceRuntime';

export type ProductLeaveReason =
  | 'route-leave'
  | 'project-switch'
  | 'logout'
  | 'change-password'
  | 'project-delete'
  | 'host-close';

export type ProductLeaveGuardPhase =
  | 'idle'
  | 'evaluating'
  | 'prompting'
  | 'allowed'
  | 'blocked'
  | 'disposed';

export type ProductLeaveProtectionKind =
  | 'workspace-draft'
  | 'workspace-save-conflict'
  | 'workspace-save-unknown'
  | 'workspace-run-active'
  | 'workspace-run-unknown'
  | 'workspace-child-pending'
  | 'workspace-child-unknown'
  | 'project-command-active'
  | 'project-command-unknown'
  | 'project-update-conflict'
  | 'continuous-inspection-active'
  | 'settings-draft'
  | 'settings-pending'
  | 'settings-unknown'
  | null;

export interface ProductLeaveGuardProjection {
  readonly phase: ProductLeaveGuardPhase;
  readonly reason: ProductLeaveReason | null;
  readonly targetProjectId: string | null;
  readonly protectionKind: ProductLeaveProtectionKind;
  readonly message: string;
  readonly forceCloseAllowed: boolean;
  readonly requestCount: number;
}

type MutableProductLeaveGuardProjection = {
  -readonly [Key in keyof ProductLeaveGuardProjection]: ProductLeaveGuardProjection[Key]
};

export interface ProductLeaveGuardDiagnostics {
  readonly ownerCount: number;
  readonly phase: ProductLeaveGuardPhase;
  readonly reason: ProductLeaveReason | null;
  readonly protectionKind: ProductLeaveProtectionKind;
  readonly requestCount: number;
  readonly promptCount: number;
  readonly blockedCount: number;
  readonly disposed: boolean;
}

export interface ProductLeaveGuardDiagnosticsWindow {
  readonly __STUDIO_UI_LEAVE_GUARD_DIAGNOSTICS__?: ProductLeaveGuardDiagnostics;
}

export interface ProductLeaveGuardOwner {
  readonly projection: DeepReadonly<ProductLeaveGuardProjection>;
  readonly diagnostics: ProductLeaveGuardDiagnostics;
  request(reason: ProductLeaveReason, targetProjectId?: string): Promise<boolean>;
  hasProtection(targetProjectId?: string): boolean;
  confirmPrompt(): void;
  cancelPrompt(): void;
  suspendForSessionExpiration(): void;
  attachInspectionRun(owner: InspectionRunOwner): () => void;
  attachSettingsParticipant(participant: ProductLeaveGuardParticipant): () => void;
  dispose(reason?: string): void;
}

export interface ProductLeaveGuardParticipant {
  inspect(): ProductLeaveProtectionKind;
}

export interface CreateProductLeaveGuardOwnerOptions {
  readonly projectLifecycle?: ProjectLifecycleCommandOwner;
  readonly workspace: WorkspaceRuntime;
  readonly runtimeWindow?: ProductLeaveGuardDiagnosticsWindow;
  readonly publishToWindow?: boolean;
}

interface PromptSettlement {
  readonly promise: Promise<boolean>;
  settle(value: boolean): void;
}

const activeRunPhases = new Set(['admitting', 'executing', 'cancel-requested']);
const unknownRunPhases = new Set(['unknown-outcome']);
const activeProjectCommandPhases = new Set(['creating', 'updating', 'deleting', 'reconciling']);
const detachableInspectionPhases = new Set(['running', 'reconnecting', 'disconnected']);
let activeOwnerCount = 0;

function promptSettlement(): PromptSettlement {
  let settled = false;
  let resolve!: (value: boolean) => void;
  const promise = new Promise<boolean>(next => { resolve = next; });
  return Object.freeze({
    promise,
    settle(value: boolean): void {
      if (settled) return;
      settled = true;
      resolve(value);
    }
  });
}

function workspaceProtection(snapshot: WorkspaceLeaveProtectionSnapshot | null): ProductLeaveProtectionKind {
  if (!snapshot) return null;
  if (snapshot.runPhase && activeRunPhases.has(snapshot.runPhase)) return 'workspace-run-active';
  if (snapshot.runPhase && unknownRunPhases.has(snapshot.runPhase)) return 'workspace-run-unknown';
  if (snapshot.childUnknown) return 'workspace-child-unknown';
  if (snapshot.childPending) return 'workspace-child-pending';
  if (snapshot.persistencePhase === 'unknown-outcome') return 'workspace-save-unknown';
  if (snapshot.persistencePhase === 'conflict') return 'workspace-save-conflict';
  if (snapshot.dirty || snapshot.persistencePhase === 'saving') return 'workspace-draft';
  return null;
}

function messageFor(kind: ProductLeaveProtectionKind): string {
  switch (kind) {
    case 'workspace-draft':
      return '当前工程仍有未保存修改。继续离开会放弃本地草稿。';
    case 'workspace-save-conflict':
      return '当前工程存在保存冲突。继续离开会放弃尚未解决的本地草稿。';
    case 'workspace-save-unknown':
      return '保存结果仍未知，必须先重新读取服务端状态；当前禁止离开。';
    case 'workspace-run-active':
      return '正式运行仍在准入校验、执行或取消协调中；当前禁止强制离开。';
    case 'workspace-run-unknown':
      return '正式运行结果未知，必须先核对服务端状态；当前禁止强制离开。';
    case 'workspace-child-pending':
      return '工程子功能仍有请求或写入处理中；请等待其完成后再离开。';
    case 'workspace-child-unknown':
      return '工程子功能写入结果未知；当前后端合同不支持安全重放，必须先重新读取协调。';
    case 'project-command-active':
      return '工程创建、更新、删除或结果核对尚未完成；当前禁止离开。';
    case 'project-command-unknown':
      return '工程操作结果未知，必须先查询服务端操作记录；当前禁止离开。';
    case 'project-update-conflict':
      return '工程信息更新存在保存修订冲突。继续离开会放弃当前页面中的未解决编辑。';
    case 'continuous-inspection-active':
      return '连续检测仍由当前页面持有，停止后端会话并确认释放前不能离开。';
    case 'settings-draft':
      return '设置中仍有未保存草稿，继续离开会放弃当前页面草稿。';
    case 'settings-pending':
      return '设置操作仍在执行中，请等待操作完成或失败后再离开。';
    case 'settings-unknown':
      return '设置操作结果未知，请先重新读取服务端状态。';
    default:
      return '当前可以安全离开。';
  }
}

function isPromptable(kind: ProductLeaveProtectionKind): boolean {
  return kind === 'workspace-draft' || kind === 'workspace-save-conflict' ||
    kind === 'project-update-conflict' || kind === 'settings-draft';
}

export function createProductLeaveGuardOwner(
  options: CreateProductLeaveGuardOwnerOptions
): ProductLeaveGuardOwner {
  if (activeOwnerCount !== 0) {
    throw new Error('Product leave guard already has an active owner.');
  }
  activeOwnerCount += 1;
  const state = reactive<MutableProductLeaveGuardProjection>({
    phase: 'idle',
    reason: null,
    targetProjectId: null,
    protectionKind: null,
    message: '页面离开保护已就绪。',
    forceCloseAllowed: false,
    requestCount: 0
  });
  let disposed = false;
  let activeRequest: Promise<boolean> | undefined;
  let pendingPrompt: PromptSettlement | undefined;
  let promptCount = 0;
  let blockedCount = 0;
  let lifecycleGeneration = 0;
  let publishedWindow: ProductLeaveGuardDiagnosticsWindow | undefined;
  let inspectionRunOwner: InspectionRunOwner | undefined;
  let settingsParticipant: ProductLeaveGuardParticipant | undefined;

  const diagnostics: ProductLeaveGuardDiagnostics = Object.freeze({
    get ownerCount() { return disposed ? 0 : 1; },
    get phase() { return state.phase; },
    get reason() { return state.reason; },
    get protectionKind() { return state.protectionKind; },
    get requestCount() { return state.requestCount; },
    get promptCount() { return promptCount; },
    get blockedCount() { return blockedCount; },
    get disposed() { return disposed; }
  });

  const shouldPublish = options.publishToWindow ?? typeof window !== 'undefined';
  const runtimeWindow = options.runtimeWindow ?? (
    typeof window === 'undefined' ? undefined : window as unknown as ProductLeaveGuardDiagnosticsWindow
  );
  if (shouldPublish && runtimeWindow) {
    if (runtimeWindow.__STUDIO_UI_LEAVE_GUARD_DIAGNOSTICS__) {
      activeOwnerCount -= 1;
      throw new Error('Product leave guard diagnostics already has a published owner.');
    }
    Object.defineProperty(runtimeWindow, '__STUDIO_UI_LEAVE_GUARD_DIAGNOSTICS__', {
      value: diagnostics,
      writable: false,
      configurable: true,
      enumerable: true
    });
    publishedWindow = runtimeWindow;
  }

  function currentProjectProtection(): ProductLeaveProtectionKind {
    const projection = options.projectLifecycle?.projection;
    if (!projection) return null;
    if (activeProjectCommandPhases.has(projection.phase)) return 'project-command-active';
    if (projection.phase === 'unknown-outcome') return 'project-command-unknown';
    if (projection.phase === 'conflict' && projection.command === 'update') return 'project-update-conflict';
    return null;
  }

  function currentWorkspaceProtection(projectId?: string): ProductLeaveProtectionKind {
    return workspaceProtection(options.workspace.getLeaveProtectionSnapshot(projectId));
  }

  function currentSettingsProtection(): ProductLeaveProtectionKind {
    return settingsParticipant?.inspect() ?? null;
  }

  function currentInspectionProtection(): ProductLeaveProtectionKind {
    const projection = inspectionRunOwner?.projection;
    if (!projection) return null;
    const runtime = projection.runtime;
    if (runtime?.isBusy && runtime.sessionType === 'ContinuousInspection') {
      return 'continuous-inspection-active';
    }
    if (projection.phase === 'starting' || projection.phase === 'stopping' ||
        projection.phase === 'reconnecting' || projection.phase === 'disconnected') {
      return 'continuous-inspection-active';
    }
    return null;
  }

  function preservesInspectionAcrossRouteLeave(reason: ProductLeaveReason): boolean {
    const projection = inspectionRunOwner?.projection;
    return reason === 'route-leave' && projection?.runtime?.isBusy === true &&
      projection.runtime.sessionType === 'ContinuousInspection' &&
      detachableInspectionPhases.has(projection.phase);
  }

  function isCurrent(generation: number): boolean {
    return !disposed && generation === lifecycleGeneration;
  }

  function block(kind: ProductLeaveProtectionKind, generation: number): false {
    if (!isCurrent(generation)) return false;
    blockedCount += 1;
    state.phase = 'blocked';
    state.protectionKind = kind;
    state.message = messageFor(kind);
    state.forceCloseAllowed = false;
    return false;
  }

  async function prompt(
    kind: ProductLeaveProtectionKind,
    reason: ProductLeaveReason,
    generation: number
  ): Promise<boolean> {
    if (reason === 'host-close') return block(kind, generation);
    if (!isCurrent(generation)) return false;
    promptCount += 1;
    state.phase = 'prompting';
    state.protectionKind = kind;
    state.message = messageFor(kind);
    state.forceCloseAllowed = true;
    const settlement = promptSettlement();
    pendingPrompt = settlement;
    const allowed = await settlement.promise;
    if (pendingPrompt === settlement) pendingPrompt = undefined;
    if (!isCurrent(generation)) return false;
    state.phase = allowed ? 'allowed' : 'blocked';
    state.forceCloseAllowed = allowed;
    if (!allowed) blockedCount += 1;
    return allowed;
  }

  async function evaluate(
    reason: ProductLeaveReason,
    targetProjectId: string | undefined,
    generation: number
  ): Promise<boolean> {
    if (!isCurrent(generation)) return false;
    state.phase = 'evaluating';
    state.reason = reason;
    state.targetProjectId = targetProjectId ?? null;
    state.protectionKind = null;
    state.message = '正在等待后端确认操作结果。';
    state.forceCloseAllowed = false;
    state.requestCount += 1;

    const projectBefore = currentProjectProtection();
    if (options.projectLifecycle &&
        (projectBefore === 'project-command-active' || projectBefore === 'project-command-unknown')) {
      const settled = await options.projectLifecycle.prepareForProtectedTransition(`leave-${reason}`);
      if (!isCurrent(generation)) return false;
      if (!settled) return block(currentProjectProtection() ?? projectBefore, generation);
    }
    const projectAfter = currentProjectProtection();
    if (projectAfter === 'project-update-conflict') return await prompt(projectAfter, reason, generation);
    if (projectAfter) return block(projectAfter, generation);

    if (!preservesInspectionAcrossRouteLeave(reason)) {
      const inspectionBefore = currentInspectionProtection();
      if (inspectionBefore && inspectionRunOwner) {
        let settled: boolean;
        try {
          settled = await inspectionRunOwner.prepareForLeave();
        } catch {
          settled = false;
        }
        if (!isCurrent(generation)) return false;
        if (!settled) return block(currentInspectionProtection() ?? inspectionBefore, generation);
      }
      const inspectionAfter = currentInspectionProtection();
      if (inspectionAfter) return block(inspectionAfter, generation);
    }

    const settingsProtection = currentSettingsProtection();
    if (settingsProtection) {
      return isPromptable(settingsProtection)
        ? await prompt(settingsProtection, reason, generation)
        : block(settingsProtection, generation);
    }

    const workspaceSettled = await options.workspace.prepareForLeave(
      `product-${reason}`,
      targetProjectId
    );
    if (!isCurrent(generation)) return false;
    if (!workspaceSettled) {
      const protection = currentWorkspaceProtection(targetProjectId) ?? 'workspace-save-unknown';
      return isPromptable(protection)
        ? await prompt(protection, reason, generation)
        : block(protection, generation);
    }

    const remaining = currentWorkspaceProtection(targetProjectId);
    if (remaining) {
      return isPromptable(remaining)
        ? await prompt(remaining, reason, generation)
        : block(remaining, generation);
    }
    state.phase = 'allowed';
    state.protectionKind = null;
    state.message = '后端操作结果已确认，可以安全离开。';
    state.forceCloseAllowed = true;
    return true;
  }

  const owner: ProductLeaveGuardOwner = Object.freeze({
    projection: readonly(state),
    diagnostics,
    request(reason: ProductLeaveReason, targetProjectId?: string): Promise<boolean> {
      if (disposed) return Promise.resolve(false);
      const generation = lifecycleGeneration;
      const predecessor = activeRequest;
      const operation = predecessor
        ? predecessor.catch(() => false).then(() => evaluate(reason, targetProjectId, generation))
        : evaluate(reason, targetProjectId, generation);
      const flight = operation.finally(() => {
        if (activeRequest === flight) activeRequest = undefined;
      });
      activeRequest = flight;
      return flight;
    },
    hasProtection(targetProjectId?: string): boolean {
      if (disposed) return false;
      return currentProjectProtection() !== null ||
        currentWorkspaceProtection(targetProjectId) !== null || currentSettingsProtection() !== null ||
        currentInspectionProtection() !== null;
    },
    confirmPrompt(): void {
      pendingPrompt?.settle(true);
    },
    cancelPrompt(): void {
      pendingPrompt?.settle(false);
    },
    suspendForSessionExpiration(): void {
      lifecycleGeneration += 1;
      pendingPrompt?.settle(false);
      pendingPrompt = undefined;
      activeRequest = undefined;
      state.phase = 'idle';
      state.reason = null;
      state.protectionKind = null;
      state.message = '会话失效；离开提示已取消，保留结果核对状态。';
      state.forceCloseAllowed = false;
    },
    attachInspectionRun(owner: InspectionRunOwner): () => void {
      if (disposed) throw new Error('Cannot attach inspection run to a disposed leave guard.');
      if (inspectionRunOwner && inspectionRunOwner !== owner) {
        throw new Error('Product leave guard already has a mounted inspection run owner.');
      }
      inspectionRunOwner = owner;
      return () => {
        if (inspectionRunOwner === owner) inspectionRunOwner = undefined;
      };
    },
    attachSettingsParticipant(participant: ProductLeaveGuardParticipant): () => void {
      if (disposed) throw new Error('Cannot attach a Settings participant to a disposed leave guard.');
      if (settingsParticipant && settingsParticipant !== participant) {
        throw new Error('Product leave guard already has a mounted Settings participant.');
      }
      settingsParticipant = participant;
      return () => {
        if (settingsParticipant === participant) settingsParticipant = undefined;
      };
    },
    dispose(reason = 'product-leave-guard-disposed'): void {
      void reason;
      if (disposed) return;
      disposed = true;
      lifecycleGeneration += 1;
      pendingPrompt?.settle(false);
      pendingPrompt = undefined;
      activeRequest = undefined;
      inspectionRunOwner = undefined;
      settingsParticipant = undefined;
      state.phase = 'disposed';
      state.reason = null;
      state.protectionKind = null;
      state.message = '页面离开保护已释放。';
      state.forceCloseAllowed = false;
      activeOwnerCount = Math.max(0, activeOwnerCount - 1);
      if (publishedWindow) {
        delete (publishedWindow as { __STUDIO_UI_LEAVE_GUARD_DIAGNOSTICS__?: ProductLeaveGuardDiagnostics })
          .__STUDIO_UI_LEAVE_GUARD_DIAGNOSTICS__;
        publishedWindow = undefined;
      }
    }
  });
  return owner;
}
