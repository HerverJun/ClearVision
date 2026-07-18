import { reactive, readonly, type DeepReadonly } from 'vue';
import {
  ApiAbortError,
  ApiForbiddenError,
  ApiHttpError,
  ApiNetworkError,
  ApiServerError,
  ApiUnauthorizedError,
  ApiUnexpectedHttpError,
  type ApiTransport
} from '@/platform/api';
import { decodeProjectDetails, isProjectId, type ProjectDetails } from '@/capabilities/projects-read/projectContracts';
import {
  decodeProjectCreateAuthorityResult,
  decodeProjectDeleteAuthorityResult,
  decodeProjectLifecycleOperation,
  decodeProjectOpenAuthorityResult,
  isLifecycleOperationId,
  type ProjectCreateAuthorityResult,
  type ProjectDeleteAuthorityResult,
  type ProjectLifecycleOperation,
  type ProjectLifecycleOperationKind,
  type ProjectOpenAuthorityResult
} from './projectLifecycleContracts';

export type ProjectLifecycleCommandPhase =
  | 'idle'
  | 'creating'
  | 'updating'
  | 'deleting'
  | 'reconciling'
  | 'conflict'
  | 'unknown-outcome'
  | 'succeeded'
  | 'failed'
  | 'disposed';

export type ProjectLifecycleCommandKind = 'create' | 'update' | 'open' | 'delete' | null;

export interface ProjectLifecycleCommandProjection {
  readonly phase: ProjectLifecycleCommandPhase;
  readonly command: ProjectLifecycleCommandKind;
  readonly projectId: string | null;
  readonly clientOperationId: string | null;
  readonly project: ProjectDetails | null;
  readonly operation: ProjectLifecycleOperation | null;
  readonly openedAtUtc: string | null;
  readonly errorCode: string | null;
  readonly message: string;
  readonly canReconcile: boolean;
  readonly generation: number;
}

type MutableProjectLifecycleCommandProjection = {
  -readonly [Key in keyof ProjectLifecycleCommandProjection]: ProjectLifecycleCommandProjection[Key]
};

export interface ProjectLifecycleCommandDiagnostics {
  readonly ownerCount: number;
  readonly activeAbortControllerCount: number;
  readonly inFlightCommandCount: number;
  readonly totalCommandCount: number;
  readonly totalReconcileCount: number;
  readonly pendingOperationKind: ProjectLifecycleOperationKind | null;
  readonly pendingOperationId: string | null;
  readonly disposed: boolean;
}

export interface ProjectLifecycleDiagnosticsWindow {
  readonly __STUDIO_UI_PROJECT_LIFECYCLE_DIAGNOSTICS__?: ProjectLifecycleCommandDiagnostics;
}

export interface ProjectLifecycleCommandOwner {
  readonly projection: DeepReadonly<ProjectLifecycleCommandProjection>;
  readonly diagnostics: ProjectLifecycleCommandDiagnostics;
  setProjectScope(projectId: string | null): void;
  createBlank(input: Readonly<{ name: string; description?: string | null }>): Promise<ProjectCreateAuthorityResult | null>;
  updateProject(input: Readonly<{
    projectId: string;
    name: string;
    description?: string | null;
    expectedPersistenceRevision: number;
  }>): Promise<ProjectDetails | null>;
  openProject(projectId: string): Promise<ProjectOpenAuthorityResult | null>;
  deleteProject(input: Readonly<{
    projectId: string;
    expectedPersistenceRevision: number;
  }>): Promise<ProjectDeleteAuthorityResult | null>;
  reconcile(): Promise<ProjectCreateAuthorityResult | ProjectDeleteAuthorityResult | null>;
  prepareForProtectedTransition(reason?: string): Promise<boolean>;
  quarantineForSessionExpiration(): boolean;
  reconcileAfterReauthentication(): Promise<boolean>;
  reset(): void;
  dispose(reason?: string): void;
}

export interface CreateProjectLifecycleCommandOwnerOptions {
  readonly api: ApiTransport;
  readonly createOperationId?: () => string;
  readonly prepareProjectLeave?: (projectId: string, reason: 'project-delete') => Promise<boolean>;
  readonly runtimeWindow?: ProjectLifecycleDiagnosticsWindow;
  readonly publishToWindow?: boolean;
}

interface PendingOperation {
  readonly kind: ProjectLifecycleOperationKind;
  readonly clientOperationId: string;
  readonly projectId: string | null;
}

interface ActiveFlight<T> {
  readonly key: string;
  readonly promise: Promise<T>;
}

let activeOwnerCount = 0;

function payloadCode(error: unknown): string | null {
  if (!(error instanceof ApiHttpError) || typeof error.payload !== 'object' || error.payload === null) return null;
  const payload = error.payload as Record<string, unknown>;
  const code = payload.code ?? payload.Code;
  return typeof code === 'string' && code.trim() ? code.trim().toUpperCase() : null;
}

function phaseForCode(code: string | null): ProjectLifecycleCommandPhase {
  return code === 'PROJECT_REVISION_CONFLICT' || code === 'PROJECT_MUTATION_CONFLICT' ||
    code === 'OPERATION_PAYLOAD_MISMATCH'
    ? 'conflict'
    : 'failed';
}

function messageForCode(code: string | null): string {
  switch (code) {
    case 'PROJECT_REVISION_CONFLICT': return '工程已由其他保存更新。请重新读取服务端 revision 后再决定如何处理。';
    case 'PROJECT_MUTATION_CONFLICT': return '工程当前存在运行、保存或其他写入，暂时不能执行此操作。';
    case 'OPERATION_PAYLOAD_MISMATCH': return '该 operation identity 已绑定不同请求，不能复用。';
    case 'PROJECT_NOT_FOUND': return '工程不存在或已删除。';
    case 'PROJECT_OPERATION_NOT_FOUND': return '服务端没有找到当前用户的 operation authority。';
    case 'PROJECT_OPERATION_RETRYABLE': return '操作结果尚未确定，必须先查询 operation authority。';
    case 'PROJECT_UPDATE_UNKNOWN_OUTCOME': return '更新响应不可确定。请重新读取工程，不要自动覆盖或盲目重试。';
    case 'PROJECT_OPERATION_FAILED': return '服务端 operation 已进入失败终态。';
    case 'PROJECT_CLEANUP_RETRYABLE': return '工程已删除，后台资源清理仍在重试。';
    case 'PROJECT_VALIDATION_NAME_REQUIRED': return '请输入工程名称。';
    case 'PROJECT_VALIDATION_NAME_TOO_LONG': return '工程名称超出允许长度。';
    case 'PROJECT_VALIDATION_DESCRIPTION_TOO_LONG': return '工程描述超出允许长度。';
    case 'PROJECT_LEAVE_BLOCKED': return '当前工程仍有未完成的保存或运行协调，删除已被阻止。';
    case 'SESSION_UNAUTHORIZED': return '当前会话已失效，认证 owner 正在处理重新登录。';
    case 'PROJECT_FORBIDDEN': return '当前账号无权修改此工程。';
    case 'PROJECT_COMMAND_ABORTED': return '工程操作已取消。';
    case 'PROJECT_CONTRACT_INVALID': return '服务响应不符合冻结的 Project 生命周期合同。';
    case 'PROJECT_NETWORK_FAILURE': return '本地服务响应不可确定；不要盲目重试写请求。';
    default: return '工程操作未完成。';
  }
}

function isUnknownOutcomeFailure(error: unknown): boolean {
  const code = payloadCode(error);
  return error instanceof ApiNetworkError || error instanceof ApiServerError ||
    error instanceof ApiUnexpectedHttpError || code === 'PROJECT_OPERATION_RETRYABLE' ||
    code === 'PROJECT_CLEANUP_RETRYABLE';
}

function operationId(): string {
  const value = globalThis.crypto?.randomUUID?.();
  if (!value || !isLifecycleOperationId(value)) {
    throw new Error('A secure UUID generator is required for Project lifecycle commands.');
  }
  return value;
}

function normalizedName(value: string): string {
  const name = value.trim();
  if (!name) throw new TypeError('Project name is required.');
  return name;
}

function normalizedDescription(value: string | null | undefined): string | null {
  const description = value?.trim() ?? '';
  return description || null;
}

function assertRevision(value: number): void {
  if (!Number.isInteger(value) || value < 0) {
    throw new TypeError('Expected persistence revision must be a non-negative integer.');
  }
}

export function createProjectLifecycleCommandOwner(
  options: CreateProjectLifecycleCommandOwnerOptions
): ProjectLifecycleCommandOwner {
  if (!options.api.post || !options.api.put) {
    throw new TypeError('Project lifecycle commands require POST and PUT on the shared ApiTransport.');
  }
  if (activeOwnerCount !== 0) {
    throw new Error('projectLifecycleCommandOwner already has an active mounted owner.');
  }
  activeOwnerCount += 1;

  const state = reactive<MutableProjectLifecycleCommandProjection>({
    phase: 'idle',
    command: null,
    projectId: null,
    clientOperationId: null,
    project: null,
    operation: null,
    openedAtUtc: null,
    errorCode: null,
    message: '工程命令 owner 已就绪。',
    canReconcile: false,
    generation: 0
  });
  let disposed = false;
  let generation = 0;
  let activeController: AbortController | undefined;
  let activeFlight: ActiveFlight<unknown> | undefined;
  let pendingOperation: PendingOperation | null = null;
  let totalCommandCount = 0;
  let totalReconcileCount = 0;
  let publishedWindow: ProjectLifecycleDiagnosticsWindow | undefined;

  const diagnostics: ProjectLifecycleCommandDiagnostics = Object.freeze({
    get ownerCount() { return disposed ? 0 : 1; },
    get activeAbortControllerCount() { return activeController ? 1 : 0; },
    get inFlightCommandCount() { return activeFlight ? 1 : 0; },
    get totalCommandCount() { return totalCommandCount; },
    get totalReconcileCount() { return totalReconcileCount; },
    get pendingOperationKind() { return pendingOperation?.kind ?? null; },
    get pendingOperationId() { return pendingOperation?.clientOperationId ?? null; },
    get disposed() { return disposed; }
  });

  const shouldPublish = options.publishToWindow ?? typeof window !== 'undefined';
  const runtimeWindow = options.runtimeWindow ?? (
    typeof window === 'undefined' ? undefined : window as unknown as ProjectLifecycleDiagnosticsWindow
  );
  if (shouldPublish && runtimeWindow) {
    if (runtimeWindow.__STUDIO_UI_PROJECT_LIFECYCLE_DIAGNOSTICS__) {
      activeOwnerCount -= 1;
      throw new Error('Project lifecycle diagnostics already has a published owner.');
    }
    Object.defineProperty(runtimeWindow, '__STUDIO_UI_PROJECT_LIFECYCLE_DIAGNOSTICS__', {
      value: diagnostics,
      writable: false,
      configurable: true,
      enumerable: true
    });
    publishedWindow = runtimeWindow;
  }

  function assertActive(): void {
    if (disposed) throw new Error('projectLifecycleCommandOwner has been disposed.');
  }

  function isCurrent(operationGeneration: number, projectId?: string | null): boolean {
    return !disposed && generation === operationGeneration &&
      (projectId === undefined || state.projectId === projectId);
  }

  function abortActive(reason: string): void {
    generation += 1;
    state.generation = generation;
    activeController?.abort(reason);
    activeController = undefined;
  }

  function clearResult(): void {
    state.project = null;
    state.operation = null;
    state.openedAtUtc = null;
    state.errorCode = null;
    state.canReconcile = false;
  }

  function begin(command: Exclude<ProjectLifecycleCommandKind, null>, projectId: string | null): {
    readonly generation: number;
    readonly controller: AbortController;
  } {
    assertActive();
    abortActive('superseded-project-command');
    clearResult();
    state.command = command;
    state.projectId = projectId;
    state.phase = command === 'create' ? 'creating' : command === 'update' ? 'updating' :
      command === 'delete' ? 'deleting' : 'updating';
    state.message = command === 'open' ? '正在确认工程打开 authority。' : '正在提交工程命令。';
    const controller = new AbortController();
    activeController = controller;
    totalCommandCount += 1;
    return { generation, controller };
  }

  function applyFailure(error: unknown, operationGeneration: number, projectId: string | null): void {
    if (!isCurrent(operationGeneration, projectId)) return;
    if (error instanceof ApiAbortError) {
      state.phase = 'failed';
      state.errorCode = 'PROJECT_COMMAND_ABORTED';
    } else if (error instanceof ApiUnauthorizedError) {
      state.phase = 'failed';
      state.errorCode = 'SESSION_UNAUTHORIZED';
    } else if (error instanceof ApiForbiddenError) {
      state.phase = 'failed';
      state.errorCode = 'PROJECT_FORBIDDEN';
    } else {
      const code = payloadCode(error) ?? (error instanceof TypeError ? 'PROJECT_CONTRACT_INVALID' : 'PROJECT_NETWORK_FAILURE');
      state.phase = phaseForCode(code);
      state.errorCode = code;
    }
    state.message = messageForCode(state.errorCode);
    state.canReconcile = false;
  }

  function applyCompletedOperation(operation: ProjectLifecycleOperation, operationGeneration: number):
    ProjectCreateAuthorityResult | ProjectDeleteAuthorityResult | null {
    if (!isCurrent(operationGeneration)) return null;
    state.operation = operation;
    if (operation.status === 'pending' || operation.status === 'failed-retryable') {
      state.phase = 'unknown-outcome';
      state.errorCode = operation.errorCode ?? 'PROJECT_OPERATION_RETRYABLE';
      state.message = messageForCode(state.errorCode);
      state.canReconcile = true;
      return null;
    }
    if (operation.status === 'failed-terminal') {
      state.phase = phaseForCode(operation.errorCode);
      state.errorCode = operation.errorCode ?? 'PROJECT_OPERATION_FAILED';
      state.message = messageForCode(state.errorCode);
      state.canReconcile = false;
      pendingOperation = null;
      return null;
    }

    const projectId = operation.projectId;
    if (!projectId || !isProjectId(projectId)) {
      applyFailure(new TypeError('Operation Project identity is invalid.'), operationGeneration, state.projectId);
      return null;
    }
    pendingOperation = null;
    state.projectId = projectId;
    state.clientOperationId = operation.clientOperationId;
    state.errorCode = null;
    state.canReconcile = false;
    state.phase = 'succeeded';
    if (operation.kind === 'create') {
      const project = operation.result?.project;
      if (!project || project.id !== projectId) {
        applyFailure(new TypeError('Create operation result did not contain the bound Project.'), operationGeneration, projectId);
        return null;
      }
      state.project = project;
      state.message = '空白工程已由服务端创建。';
      return Object.freeze({
        projectId,
        project,
        operationReplayed: true,
        operation
      });
    }
    state.message = operation.result?.cleanupStatus === 'cleanup-failed-retryable'
      ? '工程 tombstone 已生效；后台资源清理仍在重试。'
      : '工程已由服务端 tombstone authority 删除。';
    return Object.freeze({
      projectId,
      operationReplayed: true,
      operation
    });
  }

  async function reconcilePending(): Promise<ProjectCreateAuthorityResult | ProjectDeleteAuthorityResult | null> {
    assertActive();
    const pending = pendingOperation;
    if (!pending) return null;
    const started = begin(pending.kind, pending.projectId);
    state.phase = 'reconciling';
    state.clientOperationId = pending.clientOperationId;
    state.canReconcile = false;
    state.message = '正在查询服务端 operation authority。';
    totalReconcileCount += 1;
    try {
      const payload = await options.api.get(
        `project-operations/${pending.clientOperationId}?kind=${pending.kind}`,
        { signal: started.controller.signal }
      );
      if (!isCurrent(started.generation, pending.projectId)) return null;
      const operation = decodeProjectLifecycleOperation(payload);
      if (operation.clientOperationId !== pending.clientOperationId || operation.kind !== pending.kind) {
        throw new TypeError('Operation reconcile identity changed.');
      }
      return applyCompletedOperation(operation, started.generation);
    } catch (error) {
      if (!isCurrent(started.generation, pending.projectId)) return null;
      if (error instanceof ApiUnauthorizedError) {
        state.phase = 'unknown-outcome';
        state.errorCode = 'SESSION_UNAUTHORIZED';
        state.message = '会话已失效；保留 operation identity，重新认证后继续 reconcile。';
        state.canReconcile = true;
      } else if (error instanceof ApiAbortError) {
        state.phase = 'unknown-outcome';
        state.errorCode = 'PROJECT_OPERATION_RETRYABLE';
        state.message = messageForCode(state.errorCode);
        state.canReconcile = true;
      } else {
        const code = error instanceof TypeError ? 'PROJECT_CONTRACT_INVALID' : payloadCode(error);
        state.phase = code === 'PROJECT_OPERATION_NOT_FOUND' ? 'failed' : 'unknown-outcome';
        state.errorCode = code ?? 'PROJECT_OPERATION_RETRYABLE';
        state.message = messageForCode(state.errorCode);
        state.canReconcile = state.phase === 'unknown-outcome' && code !== 'PROJECT_CONTRACT_INVALID';
        if (code === 'PROJECT_CONTRACT_INVALID') state.phase = 'failed';
        if (!state.canReconcile) pendingOperation = null;
      }
      return null;
    } finally {
      if (activeController === started.controller) activeController = undefined;
    }
  }

  function track<T>(key: string, create: () => Promise<T>): Promise<T> {
    const current = activeFlight;
    if (current?.key === key) return current.promise as Promise<T>;
    const operation = create();
    const flight: ActiveFlight<T> = {
      key,
      promise: operation.finally(() => {
        if (activeFlight === flight) activeFlight = undefined;
      })
    };
    activeFlight = flight as ActiveFlight<unknown>;
    return flight.promise;
  }

  const owner: ProjectLifecycleCommandOwner = Object.freeze({
    projection: readonly(state),
    diagnostics,
    setProjectScope(projectId: string | null): void {
      assertActive();
      if (projectId !== null && !isProjectId(projectId)) {
        throw new TypeError('Project command scope requires a non-empty Project UUID.');
      }
      if (state.projectId === projectId) return;
      abortActive('project-scope-changed');
      pendingOperation = null;
      clearResult();
      state.phase = 'idle';
      state.command = null;
      state.projectId = projectId;
      state.clientOperationId = null;
      state.message = '工程命令 scope 已切换。';
    },
    createBlank(input: Readonly<{ name: string; description?: string | null }>): Promise<ProjectCreateAuthorityResult | null> {
      return track('create', async () => {
        let name: string;
        let description: string | null;
        try {
          name = normalizedName(input.name);
          description = normalizedDescription(input.description);
        } catch {
          clearResult();
          state.phase = 'failed';
          state.command = 'create';
          state.projectId = null;
          state.errorCode = 'PROJECT_VALIDATION_NAME_REQUIRED';
          state.message = messageForCode(state.errorCode);
          return null;
        }
        const clientOperationId = (options.createOperationId ?? operationId)();
        if (!isLifecycleOperationId(clientOperationId)) {
          throw new TypeError('Project create operation id must be a non-empty UUID.');
        }
        pendingOperation = { kind: 'create', clientOperationId, projectId: null };
        const started = begin('create', null);
        state.clientOperationId = clientOperationId;
        try {
          const payload = await options.api.post?.('projects', {
            clientOperationId,
            name,
            description
          }, { signal: started.controller.signal });
          if (!isCurrent(started.generation, null)) return null;
          const result = decodeProjectCreateAuthorityResult(payload);
          if (result.operation.clientOperationId !== clientOperationId) {
            throw new TypeError('Create response operation identity changed.');
          }
          pendingOperation = null;
          state.phase = 'succeeded';
          state.projectId = result.projectId;
          state.project = result.project;
          state.operation = result.operation;
          state.errorCode = null;
          state.message = result.operationReplayed
            ? '空白工程已从既有 operation authority 恢复。'
            : '空白工程已创建。';
          return result;
        } catch (error) {
          if (!isCurrent(started.generation, null)) return null;
          if (isUnknownOutcomeFailure(error)) {
            state.phase = 'unknown-outcome';
            state.errorCode = payloadCode(error) ?? 'PROJECT_OPERATION_RETRYABLE';
            state.message = messageForCode(state.errorCode);
            state.canReconcile = true;
            return await reconcilePending() as ProjectCreateAuthorityResult | null;
          }
          pendingOperation = null;
          applyFailure(error, started.generation, null);
          return null;
        } finally {
          if (activeController === started.controller) activeController = undefined;
        }
      });
    },
    updateProject(input: Readonly<{
      projectId: string;
      name: string;
      description?: string | null;
      expectedPersistenceRevision: number;
    }>): Promise<ProjectDetails | null> {
      return track(`update:${input.projectId}`, async () => {
        if (!isProjectId(input.projectId)) throw new TypeError('Project update requires a Project UUID.');
        assertRevision(input.expectedPersistenceRevision);
        const name = normalizedName(input.name);
        const description = normalizedDescription(input.description);
        const started = begin('update', input.projectId);
        try {
          const payload = await options.api.put?.(`projects/${input.projectId}`, {
            name,
            description,
            expectedPersistenceRevision: input.expectedPersistenceRevision
          }, { signal: started.controller.signal });
          if (!isCurrent(started.generation, input.projectId)) return null;
          const project = decodeProjectDetails(payload);
          if (project.id !== input.projectId) throw new TypeError('Update response Project identity changed.');
          state.phase = 'succeeded';
          state.project = project;
          state.errorCode = null;
          state.message = '工程名称与描述已按服务端 revision 保存。';
          return project;
        } catch (error) {
          if (!isCurrent(started.generation, input.projectId)) return null;
          if (isUnknownOutcomeFailure(error)) {
            state.phase = 'unknown-outcome';
            state.errorCode = 'PROJECT_UPDATE_UNKNOWN_OUTCOME';
            state.message = '更新响应不可确定。请重新读取工程，不要自动覆盖或盲目重试。';
            state.canReconcile = false;
          } else {
            applyFailure(error, started.generation, input.projectId);
          }
          return null;
        } finally {
          if (activeController === started.controller) activeController = undefined;
        }
      });
    },
    openProject(projectId: string): Promise<ProjectOpenAuthorityResult | null> {
      return track(`open:${projectId}`, async () => {
        if (!isProjectId(projectId)) throw new TypeError('Project open requires a Project UUID.');
        const started = begin('open', projectId);
        try {
          const payload = await options.api.post?.(`projects/${projectId}/open`, {}, {
            signal: started.controller.signal
          });
          if (!isCurrent(started.generation, projectId)) return null;
          const result = decodeProjectOpenAuthorityResult(payload);
          if (result.projectId !== projectId) throw new TypeError('Open response Project identity changed.');
          state.phase = 'succeeded';
          state.openedAtUtc = result.lastOpenedAtUtc;
          state.errorCode = null;
          state.message = '工程打开 authority 已确认。';
          return result;
        } catch (error) {
          if (!isCurrent(started.generation, projectId)) return null;
          applyFailure(error, started.generation, projectId);
          return null;
        } finally {
          if (activeController === started.controller) activeController = undefined;
        }
      });
    },
    deleteProject(input: Readonly<{
      projectId: string;
      expectedPersistenceRevision: number;
    }>): Promise<ProjectDeleteAuthorityResult | null> {
      return track(`delete:${input.projectId}`, async () => {
        if (!isProjectId(input.projectId)) throw new TypeError('Project delete requires a Project UUID.');
        assertRevision(input.expectedPersistenceRevision);
        if (options.prepareProjectLeave && !(await options.prepareProjectLeave(input.projectId, 'project-delete'))) {
          clearResult();
          state.phase = 'failed';
          state.command = 'delete';
          state.projectId = input.projectId;
          state.errorCode = 'PROJECT_LEAVE_BLOCKED';
          state.message = messageForCode(state.errorCode);
          return null;
        }
        const clientOperationId = (options.createOperationId ?? operationId)();
        if (!isLifecycleOperationId(clientOperationId)) {
          throw new TypeError('Project delete operation id must be a non-empty UUID.');
        }
        pendingOperation = { kind: 'delete', clientOperationId, projectId: input.projectId };
        const started = begin('delete', input.projectId);
        state.clientOperationId = clientOperationId;
        try {
          const payload = await options.api.post?.(`projects/${input.projectId}/delete`, {
            clientOperationId,
            expectedPersistenceRevision: input.expectedPersistenceRevision
          }, { signal: started.controller.signal });
          if (!isCurrent(started.generation, input.projectId)) return null;
          const result = decodeProjectDeleteAuthorityResult(payload);
          if (result.projectId !== input.projectId || result.operation.clientOperationId !== clientOperationId) {
            throw new TypeError('Delete response authority identity changed.');
          }
          pendingOperation = null;
          state.phase = 'succeeded';
          state.operation = result.operation;
          state.errorCode = null;
          state.message = result.operation.result?.cleanupStatus === 'cleanup-failed-retryable'
            ? '工程已删除；后台资源清理仍在重试。'
            : '工程已删除。';
          return result;
        } catch (error) {
          if (!isCurrent(started.generation, input.projectId)) return null;
          if (isUnknownOutcomeFailure(error)) {
            state.phase = 'unknown-outcome';
            state.errorCode = payloadCode(error) ?? 'PROJECT_OPERATION_RETRYABLE';
            state.message = messageForCode(state.errorCode);
            state.canReconcile = true;
            return await reconcilePending() as ProjectDeleteAuthorityResult | null;
          }
          pendingOperation = null;
          applyFailure(error, started.generation, input.projectId);
          return null;
        } finally {
          if (activeController === started.controller) activeController = undefined;
        }
      });
    },
    reconcile(): Promise<ProjectCreateAuthorityResult | ProjectDeleteAuthorityResult | null> {
      return track('reconcile', reconcilePending);
    },
    async prepareForProtectedTransition(): Promise<boolean> {
      if (disposed) return true;
      if (activeFlight) return false;
      if (pendingOperation) {
        const result = await owner.reconcile();
        return result !== null || (!pendingOperation && state.phase !== 'unknown-outcome');
      }
      return state.phase !== 'unknown-outcome' && state.phase !== 'reconciling';
    },
    quarantineForSessionExpiration(): boolean {
      if (disposed) return false;
      abortActive('session-expired');
      activeFlight = undefined;
      if (pendingOperation) {
        state.phase = 'unknown-outcome';
        state.errorCode = 'SESSION_UNAUTHORIZED';
        state.message = '会话已失效；operation identity 已隔离，重新认证后继续 reconcile。';
        state.canReconcile = true;
        return true;
      }
      state.phase = 'failed';
      state.errorCode = 'SESSION_UNAUTHORIZED';
      state.message = messageForCode(state.errorCode);
      return false;
    },
    async reconcileAfterReauthentication(): Promise<boolean> {
      if (disposed) return false;
      if (!pendingOperation) return true;
      await owner.reconcile();
      return !pendingOperation && state.phase !== 'unknown-outcome' && state.phase !== 'reconciling';
    },
    reset(): void {
      assertActive();
      abortActive('project-command-reset');
      pendingOperation = null;
      clearResult();
      state.phase = 'idle';
      state.command = null;
      state.clientOperationId = null;
      state.message = '工程命令 owner 已重置。';
    },
    dispose(reason = 'project-lifecycle-owner-disposed'): void {
      void reason;
      if (disposed) return;
      disposed = true;
      abortActive('project-lifecycle-owner-disposed');
      activeFlight = undefined;
      pendingOperation = null;
      state.phase = 'disposed';
      state.command = null;
      state.canReconcile = false;
      state.message = '工程命令 owner 已释放。';
      activeOwnerCount = Math.max(0, activeOwnerCount - 1);
      if (publishedWindow) {
        delete (publishedWindow as { __STUDIO_UI_PROJECT_LIFECYCLE_DIAGNOSTICS__?: ProjectLifecycleCommandDiagnostics })
          .__STUDIO_UI_PROJECT_LIFECYCLE_DIAGNOSTICS__;
        publishedWindow = undefined;
      }
    }
  });

  return owner;
}
